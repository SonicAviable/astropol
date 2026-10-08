using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Метки планет в режиме системы: под каждой планетой — капсула с именем и состоянием.
    ///   • колония — цвет владельца, иконка жителей и население;
    ///   • идёт колонизация — золотая капсула с днями до высадки и полосой прогресса;
    ///   • можно заселить (наша изученная система) — пригодность;
    ///   • добывающий комплекс работает или строится;
    ///   • иначе — только имя, приглушённо.
    /// Следуют за планетами на экране, не ловят клики, прячутся вне режима системы и под обзором планеты.
    /// </summary>
    public class SystemPlanetTags : MonoBehaviour
    {
        private const float W = 164f;

        private class Tag
        {
            public SystemViewManager.PlanetInstance Inst;
            public RectTransform Root, BarHost, Bar;
            public CanvasGroup Group;
            public Image Bg, Icon;
            public LiquidGlassEffect Fx;
            public Text Name, Status;
        }

        private Canvas _canvas;
        private RectTransform _canvasRect, _layer;
        private CanvasGroup _layerGroup;
        private readonly List<Tag> _tags = new List<Tag>();
        private StarSystem _built;
        private int _builtCount;
        private float _alpha;
        private Camera _cam;

        private static readonly Color CGold = UIManager.DS.Gold;
        private static readonly Color CMuted = UIManager.DS.TextMuted;

        public void Init(Canvas hud)
        {
            _canvas = hud;
            _canvasRect = (RectTransform)hud.transform;
            _layer = LGBuild.Rect(hud.transform, "[UI] SystemPlanetTags");
            _layer.Stretch();
            _layer.SetAsFirstSibling();   // под панелями интерфейса
            _layerGroup = _layer.gameObject.AddComponent<CanvasGroup>();
            _layerGroup.alpha = 0f;
            _layerGroup.blocksRaycasts = false;
            _layerGroup.interactable = false;
        }

        private void LateUpdate()
        {
            if (_layer == null) return;
            var svm = SystemViewManager.Instance;
            bool show = UIManager.IsGameStarted && svm != null && svm.IsInSystemView && svm.CurrentSystem != null
                        && !(PlanetFocusOverlay.Instance != null && PlanetFocusOverlay.Instance.IsOpen);
            _alpha = Mathf.MoveTowards(_alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            _layerGroup.alpha = _alpha;
            if (!show)
            {
                if (_alpha <= 0f && _tags.Count > 0) Clear();
                return;
            }

            if (_built != svm.CurrentSystem || _builtCount != svm.ActivePlanets.Count) Rebuild(svm);

            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            var uiCam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            foreach (var t in _tags)
            {
                if (t.Inst?.PlanetTransform == null) { t.Group.alpha = 0f; continue; }
                Place(t, uiCam);
                Fill(t, svm.CurrentSystem);
            }
        }

        // ================================================================ Позиция

        private void Place(Tag t, Camera uiCam)
        {
            var tr = t.Inst.PlanetTransform;
            Vector3 center = tr.position;
            float radius = t.Inst.Renderer != null ? t.Inst.Renderer.bounds.extents.y : tr.lossyScale.y * 0.5f;

            Vector3 sc = _cam.WorldToScreenPoint(center);
            Vector3 sb = _cam.WorldToScreenPoint(center - _cam.transform.up * radius);
            if (sc.z <= 0f) { t.Group.alpha = 0f; return; }
            t.Group.alpha = 1f;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, sb, uiCam, out Vector2 local)) return;
            t.Root.anchoredPosition = local + new Vector2(0f, -8f);
        }

        // ================================================================ Содержимое

        private void Fill(Tag t, StarSystem sys)
        {
            var p = t.Inst.Data;
            t.Name.text = p.Name;
            var builds = ConstructionManager.Instance;
            ConstructionJob colJob = null;
            if (builds != null)
                foreach (var j in builds.QueueFor(p))
                    if (j.Kind == JobKind.Colony) { colJob = j; break; }

            int owner = sys.OwnerId;
            Color accent;
            LGIcon icon;
            string status;
            float bar = -1f;

            if (p.Population > 0)
            {
                accent = owner >= 0 ? FleetIndicator.OwnerColor(owner) : UIManager.DS.Green;
                icon = LGIcon.Population;
                string who = owner == 0 ? "Колония" : $"Колония · {AIEmpireManager.NameOf(owner)}";
                status = $"{who} · {p.Population} жит.";
            }
            else if (colJob != null)
            {
                accent = CGold;
                icon = LGIcon.Clock;
                int days = Mathf.CeilToInt(builds.DaysUntilDone(colJob));
                status = $"Колонизация · {days} дн.";
                bar = colJob.Progress;
            }
            else if (owner == 0 && sys.IsSurveyed && p.CanColonize)
            {
                accent = UIManager.DS.NeonCyan;
                icon = LGIcon.Planet;
                status = $"Можно заселить · {p.HabitabilityPercent}%";
            }
            else if (p.HasMiningStation)
            {
                accent = owner >= 0 ? FleetIndicator.OwnerColor(owner) : CMuted;
                icon = LGIcon.Industry;
                status = "Добывающий комплекс";
            }
            else if (p.StationUnderConstruction)
            {
                accent = CGold;
                icon = LGIcon.Construction;
                status = "Строится комплекс";
            }
            else
            {
                accent = CMuted;
                icon = LGIcon.Planet;
                status = null;
            }

            bool hasStatus = status != null;
            bool colonizing = bar >= 0f;
            float h = !hasStatus ? 22f : colonizing ? 44f : 36f;
            t.Root.sizeDelta = new Vector2(hasStatus ? W : 110f, h);

            t.Icon.sprite = LGIcons.Get(icon);
            t.Icon.color = accent;
            t.Name.color = hasStatus ? Color.white : new Color(CMuted.r, CMuted.g, CMuted.b, 0.9f);
            t.Name.fontSize = hasStatus ? 11 : 10;
            t.Name.rectTransform.TopBand(hasStatus ? 4 : 3, 16, hasStatus ? 30 : 26, 8);
            t.Icon.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(hasStatus ? 9 : 7, hasStatus ? -9 : -3), hasStatus ? new Vector2(16, 16) : new Vector2(14, 14));

            t.Status.gameObject.SetActive(hasStatus);
            if (hasStatus)
            {
                t.Status.text = status;
                t.Status.color = Color.Lerp(accent, Color.white, 0.25f);
            }

            t.BarHost.gameObject.SetActive(colonizing);
            if (colonizing) LGBuild.SetBar(t.Bar, bar, CGold);

            // Колонизация «дышит» золотой кромкой — её видно издалека
            float rimA = !hasStatus ? 0.18f
                       : colonizing ? 0.55f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f))
                       : 0.6f;
            t.Fx?.SetRim(new Color(accent.r, accent.g, accent.b, rimA));
            t.Bg.color = hasStatus
                ? new Color(accent.r * 0.10f + 0.01f, accent.g * 0.10f + 0.02f, accent.b * 0.10f + 0.03f, 0.92f)
                : new Color(0.01f, 0.03f, 0.04f, 0.65f);
        }

        // ================================================================ Построение

        private void Rebuild(SystemViewManager svm)
        {
            Clear();
            _built = svm.CurrentSystem;
            _builtCount = svm.ActivePlanets.Count;
            foreach (var inst in svm.ActivePlanets) _tags.Add(Create(inst));
        }

        private void Clear()
        {
            foreach (var t in _tags) if (t.Root != null) Destroy(t.Root.gameObject);
            _tags.Clear();
            _built = null;
            _builtCount = 0;
        }

        private Tag Create(SystemViewManager.PlanetInstance inst)
        {
            var rt = LGBuild.Rect(_layer, "PlanetTag_" + inst.Data?.Name);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(W, 36f);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            var bg = rt.gameObject.AddComponent<Image>();
            bg.raycastTarget = false;
            var fx = LG.Platter(rt.gameObject, 11f);

            var icon = LGIcons.Create(rt, LGIcon.Planet, 16, CMuted);
            icon.raycastTarget = false;

            var name = LGBuild.Label(rt, "", 11, Color.white, TextAnchor.MiddleLeft, bold: true);
            var status = LGBuild.Label(rt, "", 10, CMuted, TextAnchor.MiddleLeft, bold: true);
            status.rectTransform.TopBand(19, 14, 30, 8);

            var barHost = LGBuild.Rect(rt, "Bar");
            barHost.anchorMin = new Vector2(0, 0);
            barHost.anchorMax = new Vector2(1, 0);
            barHost.offsetMin = new Vector2(30, 5);
            barHost.offsetMax = new Vector2(-10, 10);
            var bar = LGBuild.Bar(barHost, CGold, 0f, 4f);

            return new Tag { Inst = inst, Root = rt, Group = group, Bg = bg, Fx = fx, Icon = icon, Name = name, Status = status, BarHost = barHost, Bar = bar };
        }
    }
}
