using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Cam;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Управление флотами мышью и клавиатурой, как в Stellaris:
    ///   ЛКМ по значку/кораблю — выделить (Shift — добавить/убрать), двойной клик — камера к флоту;
    ///   ЛКМ по пустому месту — снять выделение; зажать ЛКМ и тянуть — рамка выделения;
    ///   ПКМ по системе — приказ всем выделенным (Shift — в очередь);
    ///   Backspace — очистить очередь, F — камера к выделенному флоту.
    /// Значки выбираются по экранным координатам — попадать в маленький корабль не нужно.
    /// </summary>
    public class FleetSelectionController : MonoBehaviour
    {
        public static FleetSelectionController Instance { get; private set; }

        private const float DragThresholdPx = 8f;
        private const float DoubleClickTime = 0.35f;

        private Camera _cam;
        private bool _pressed;
        private bool _dragging;
        private Vector2 _pressPos;
        private bool _pressOnStar;

        private RectTransform _box;
        private float _lastClickTime;
        private FleetView _lastClicked;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            if (!UIManager.IsGameStarted) return;
            var fm = FleetManager.Instance;
            if (fm == null) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            if (SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView) { CancelDrag(); return; }

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            HandleLeftMouse(fm, shift, overUi);
            HandleRightMouse(fm, shift, overUi);
            HandleHotkeys(fm);
        }

        // ==================== ЛКМ: выбор и рамка ====================

        private void HandleLeftMouse(FleetManager fm, bool shift, bool overUi)
        {
            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                _pressed = true;
                _dragging = false;
                _pressPos = Input.mousePosition;
                _pressOnStar = RaycastSystem() != null;
            }

            if (_pressed && Input.GetMouseButton(0))
            {
                if (!_dragging && Vector2.Distance(_pressPos, Input.mousePosition) > DragThresholdPx
                    && PickFleet(_pressPos) == null)
                    _dragging = true;
                if (_dragging) DrawBox(_pressPos, Input.mousePosition);
            }

            if (_pressed && Input.GetMouseButtonUp(0))
            {
                _pressed = false;
                if (_dragging)
                {
                    SelectInBox(fm, _pressPos, Input.mousePosition, shift);
                    CancelDrag();
                    return;
                }

                var picked = PickFleet(Input.mousePosition);
                if (picked != null)
                {
                    var group = GroupOf(picked);
                    if (shift)
                    {
                        foreach (var g in group) fm.ToggleInSelection(g);
                    }
                    else
                    {
                        fm.SetSelection(group);
                        // Двойной клик — камера к флоту
                        if (_lastClicked == picked && Time.unscaledTime - _lastClickTime < DoubleClickTime)
                            FocusCamera(picked);
                    }
                    _lastClicked = picked;
                    _lastClickTime = Time.unscaledTime;
                }
                else if (!shift && !_pressOnStar && !overUi)
                {
                    fm.SetSelection(null);
                }
            }
        }

        /// <summary>Свой флот под курсором: сначала значки (по экрану), затем модель корабля.</summary>
        private FleetView PickFleet(Vector2 screen)
        {
            FleetIndicator best = null;
            float bestD = float.MaxValue;
            foreach (var ind in FleetIndicator.All)
            {
                if (ind == null || !ind.IsLeader || !ind.OnScreen || ind.Fleet?.Data == null) continue;
                if (ind.Fleet.Data.OwnerId != 0) continue;
                float d = Vector2.Distance(screen, ind.ScreenPos);
                if (d <= ind.ScreenRadius + 3f && d < bestD) { bestD = d; best = ind; }
            }
            if (best != null) return best.Fleet;

            Ray ray = _cam.ScreenPointToRay(screen);
            FleetView found = null;
            float bestDist = float.MaxValue;
            foreach (var h in Physics.RaycastAll(ray, 1000f))
            {
                var fv = h.collider.GetComponentInParent<FleetView>();
                if (fv?.Data != null && fv.Data.OwnerId == 0 && h.distance < bestDist) { found = fv; bestDist = h.distance; }
            }
            return found;
        }

        /// <summary>Все корабли, слитые со значком этого флота (клик по значку выделяет группу).</summary>
        private static List<FleetView> GroupOf(FleetView f)
        {
            foreach (var ind in FleetIndicator.All)
                if (ind != null && ind.IsLeader && ind.Members.Contains(f))
                    return new List<FleetView>(ind.Members);
            return new List<FleetView> { f };
        }

        private StarSystem RaycastSystem()
        {
            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            StarSystem best = null;
            float bestDist = float.MaxValue;
            foreach (var h in Physics.RaycastAll(ray, 1000f))
            {
                if (h.collider.GetComponentInParent<FleetView>() != null) continue;
                var sel = h.collider.GetComponentInParent<StarSystemSelector>();
                if (sel?.Data != null && h.distance < bestDist) { best = sel.Data; bestDist = h.distance; }
            }
            return best;
        }

        private void SelectInBox(FleetManager fm, Vector2 a, Vector2 b, bool additive)
        {
            var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            var picked = new List<FleetView>();
            foreach (var f in fm.AllFleets)
            {
                if (f?.Data == null || f.Data.Destroyed || f.Data.OwnerId != 0) continue;
                Vector3 sp = _cam.WorldToScreenPoint(f.transform.position);
                bool inside = sp.z > 0f && rect.Contains(sp);
                if (!inside)
                {
                    // Корабль мог «уйти» под значок — проверяем и позицию значка
                    foreach (var ind in FleetIndicator.All)
                        if (ind != null && ind.IsLeader && ind.OnScreen && ind.Members.Contains(f) && rect.Contains(ind.ScreenPos)) { inside = true; break; }
                }
                if (inside) picked.Add(f);
            }
            if (picked.Count == 0 && !additive) { fm.SetSelection(null); return; }
            fm.SetSelection(picked, additive);
            if (picked.Count > 1)
                NotificationCenter.Show("Выделение", $"Выделено флотов: {fm.SelectedFleets.Count}", NotificationCenter.Kind.Info, 1.6f);
        }

        // ==================== ПКМ: приказы ====================

        private void HandleRightMouse(FleetManager fm, bool shift, bool overUi)
        {
            if (!Input.GetMouseButtonDown(1) || overUi || fm.SelectedFleets.Count == 0) return;
            var target = RaycastSystem();
            if (target == null) return;
            fm.CommandSelection(target.Id, shift);   // звук приказа — внутри
        }

        // ==================== Клавиши ====================

        private void HandleHotkeys(FleetManager fm)
        {
            if (IsTyping()) return;
            if (fm.SelectedFleets.Count == 0) return;

            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                foreach (var f in fm.SelectedFleets) fm.ClearQueue(f);
                NotificationCenter.Show("Очередь приказов очищена", fm.SelectedFleets.Count > 1 ? $"Флотов: {fm.SelectedFleets.Count}" : fm.SelectedFleet.Data.Name,
                    NotificationCenter.Kind.Info, 2f);
            }
            if (Input.GetKeyDown(KeyCode.F)) FocusCamera(fm.SelectedFleet);
        }

        public static void FocusCamera(FleetView f)
        {
            if (f == null) return;
            var cam = FindAnyObjectByType<StrategyCameraController>();
            if (cam != null && !cam.IsSystemMode) cam.FocusOn(f.transform.position);
        }

        private static bool IsTyping()
        {
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            return sel != null && sel.GetComponent<InputField>() != null;
        }

        // ==================== Рамка ====================

        private void DrawBox(Vector2 a, Vector2 b)
        {
            if (_box == null)
            {
                var hud = UIManager.Instance != null ? UIManager.Instance.HudCanvas : null;
                if (hud == null) return;
                _box = LGBuild.Rect(hud.transform, "[UI] SelectionBox");
                var fill = _box.gameObject.AddComponent<Image>();
                fill.color = new Color(FleetIndicator.OwnColor.r, FleetIndicator.OwnColor.g, FleetIndicator.OwnColor.b, 0.07f);
                fill.raycastTarget = false;
                LG.Ignore(_box.gameObject);
                // Тонкая неоновая рамка — четыре полоски
                for (int i = 0; i < 4; i++)
                {
                    var edge = LGBuild.Panel(_box, "Edge", new Color(0.4f, 1f, 0.92f, 0.9f));
                    var rt = edge.rectTransform;
                    switch (i)
                    {
                        case 0: rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.sizeDelta = new Vector2(0, 1.5f); break;
                        case 1: rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.sizeDelta = new Vector2(0, 1.5f); break;
                        case 2: rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1); rt.sizeDelta = new Vector2(1.5f, 0); break;
                        default: rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 1); rt.sizeDelta = new Vector2(1.5f, 0); break;
                    }
                    rt.anchoredPosition = Vector2.zero;
                    var glow = edge.gameObject.AddComponent<Shadow>();
                    glow.effectColor = new Color(0.3f, 1f, 0.9f, 0.45f);
                    glow.effectDistance = new Vector2(1f, -1f);
                }
            }
            if (!_box.gameObject.activeSelf) _box.gameObject.SetActive(true);
            _box.SetAsLastSibling();

            var canvas = _box.GetComponentInParent<Canvas>();
            float k = canvas != null ? canvas.scaleFactor : 1f;
            Vector2 min = Vector2.Min(a, b) / k, max = Vector2.Max(a, b) / k;
            _box.anchorMin = _box.anchorMax = Vector2.zero;
            _box.pivot = Vector2.zero;
            _box.anchoredPosition = min;
            _box.sizeDelta = max - min;
        }

        private void CancelDrag()
        {
            _dragging = false;
            _pressed = false;
            if (_box != null && _box.gameObject.activeSelf) _box.gameObject.SetActive(false);
        }
    }
}
