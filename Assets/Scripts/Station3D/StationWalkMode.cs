using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;
using StellarisClone.Cam;

namespace StellarisClone.Station3D
{
    /// <summary>
    /// Дополнительный режим: игрок переходит со стратегической карты на свою звёздную станцию в 3D,
    /// ходит по ней и через пульты открывает те же окна, что и на карте (игра при этом идёт как обычно).
    /// Вход/выход — клавиша Y, кнопка «Станция 3D» в левом меню; выход — ещё и Esc.
    /// Камера карты на время подменяется: позиция и настройки сохраняются и восстанавливаются при выходе.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class StationWalkMode : MonoBehaviour
    {
        public static StationWalkMode Instance { get; private set; }
        public static bool IsActive => Instance != null && Instance._active;
        public const KeyCode ToggleKey = KeyCode.Y;
        private const float InteractDistance = 3.2f;

        private bool _active, _busy, _manualFree, _cursorFree;
        private StationBuilder.Result _station;
        private StationPlayer _player;
        private StationTerminal _focused;

        // Сохранённое состояние карты
        private Camera _cam;
        private struct CamState
        {
            public Vector3 Pos; public Quaternion Rot; public Transform Parent;
            public float Fov, Near, Far, OrthoSize; public bool Ortho;
            public int Mask; public CameraClearFlags Clear; public Color Bg;
        }
        private CamState _camState;
        private readonly List<Behaviour> _disabled = new List<Behaviour>();
        private bool _fog;
        private UnityEngine.Rendering.AmbientMode _ambMode;
        private Color _ambColor;
        private float _ambIntensity;
        private CursorLockMode _prevLock;
        private bool _prevCursorVisible;

        // HUD
        private GameObject _hudRoot, _blockerRoot;
        private Image _blocker, _crosshair, _fade;
        private Text _prompt, _help, _title;
        private Canvas _fadeCanvas;

        public static void Toggle()
        {
            if (Instance == null) new GameObject("[3D] StationWalkMode").AddComponent<StationWalkMode>();
            if (IsActive) Instance.Exit(); else Instance.Enter();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            if (_active) RestoreWorld();
            StationBuilder.Destroy(_station);
            _station = null;
            if (_hudRoot != null) Destroy(_hudRoot);
            if (_blockerRoot != null) Destroy(_blockerRoot);
            if (_fadeCanvas != null) Destroy(_fadeCanvas.gameObject);
            Instance = null;
        }

        // ================================================================ Вход / выход

        public void Enter()
        {
            if (_active || _busy) return;
            if (!UIManager.IsGameStarted || LoadingScreen.IsActive) return;
            if (StellarisClone.Cards.BattleScreen.IsOpen) return;
            if (UIManager.Instance != null && UIManager.Instance.IsBlockingFlowOpen) return;
            StartCoroutine(EnterRoutine());
        }

        public void Exit()
        {
            if (!_active || _busy) return;
            StartCoroutine(ExitRoutine());
        }

        private IEnumerator EnterRoutine()
        {
            _busy = true;
            EnsureFade();
            yield return Fade(1f, 0.22f);

            _cam = Camera.main;
            if (_cam == null) { _busy = false; yield return Fade(0f, 0.2f); yield break; }

            if (_station == null || _station.Root == null)
            {
                try { _station = StationBuilder.Build(); }
                catch (System.Exception e)
                {
                    Debug.LogError("[Station3D] Не удалось построить станцию: " + e);
                    StationBuilder.Destroy(_station);
                    _station = null;
                }
                if (_station == null) { _busy = false; yield return Fade(0f, 0.2f); yield break; }
            }
            _station.Root.SetActive(true);

            if (_player == null)
            {
                var pgo = new GameObject("StationPlayer");
                pgo.transform.SetParent(_station.Root.transform, false);
                pgo.layer = StationBuilder.Layer;
                pgo.AddComponent<CharacterController>();
                _player = pgo.AddComponent<StationPlayer>();
                _player.Teleport(_station.Spawn, _station.SpawnYaw);
            }

            SaveAndSwitchWorld();
            BuildHud();
            _active = true;
            _manualFree = false;
            ApplyCursor(force: true);
            _title.text = StationTitle();
            NotificationCenter.Show(Loc.T("Вы на борту станции"), Loc.T("Подойдите к пульту и нажмите E. Esc или Y — вернуться на карту"), NotificationCenter.Kind.Info, 4f);

            yield return Fade(0f, 0.35f);
            _busy = false;
        }

        private IEnumerator ExitRoutine()
        {
            _busy = true;
            yield return Fade(1f, 0.2f);
            _active = false;
            SetFocus(null);
            RestoreWorld();
            if (_station != null && _station.Root != null) _station.Root.SetActive(false);
            if (_hudRoot != null) _hudRoot.SetActive(false);
            if (_blockerRoot != null) _blockerRoot.SetActive(false);
            yield return Fade(0f, 0.3f);
            _busy = false;
        }

        private void SaveAndSwitchWorld()
        {
            var t = _cam.transform;
            _camState = new CamState
            {
                Pos = t.position, Rot = t.rotation, Parent = t.parent,
                Fov = _cam.fieldOfView, Near = _cam.nearClipPlane, Far = _cam.farClipPlane,
                Ortho = _cam.orthographic, OrthoSize = _cam.orthographicSize,
                Mask = _cam.cullingMask, Clear = _cam.clearFlags, Bg = _cam.backgroundColor
            };
            _prevLock = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;

            _disabled.Clear();
            var scc = _cam.GetComponent<StrategyCameraController>();
            if (scc != null && scc.enabled) { scc.enabled = false; _disabled.Add(scc); }
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.enabled && l.type == LightType.Directional && l.gameObject.layer != StationBuilder.Layer)
                { l.enabled = false; _disabled.Add(l); }

            t.SetParent(null, true);
            _cam.orthographic = false;
            _cam.fieldOfView = 70f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 300f;
            _cam.cullingMask = 1 << StationBuilder.Layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.01f, 0.012f, 0.02f);

            _fog = RenderSettings.fog;
            _ambMode = RenderSettings.ambientMode;
            _ambColor = RenderSettings.ambientLight;
            _ambIntensity = RenderSettings.ambientIntensity;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.2f, 0.23f, 0.28f);
            UpdateCamera();
        }

        private void RestoreWorld()
        {
            if (_cam != null)
            {
                var t = _cam.transform;
                t.SetParent(_camState.Parent, true);
                t.SetPositionAndRotation(_camState.Pos, _camState.Rot);
                _cam.fieldOfView = _camState.Fov;
                _cam.nearClipPlane = _camState.Near;
                _cam.farClipPlane = _camState.Far;
                _cam.orthographic = _camState.Ortho;
                _cam.orthographicSize = _camState.OrthoSize;
                _cam.cullingMask = _camState.Mask;
                _cam.clearFlags = _camState.Clear;
                _cam.backgroundColor = _camState.Bg;
            }
            foreach (var b in _disabled) if (b != null) b.enabled = true;
            _disabled.Clear();
            RenderSettings.fog = _fog;
            RenderSettings.ambientMode = _ambMode;
            RenderSettings.ambientLight = _ambColor;
            RenderSettings.ambientIntensity = _ambIntensity;
            Cursor.lockState = _prevLock;
            Cursor.visible = _prevCursorVisible;
            _cursorFree = true;
        }

        // ================================================================ Цикл

        private void Update()
        {
            if (!_active)
            {
                if (!_busy && Input.GetKeyDown(ToggleKey) && CanUseHotkey()) Enter();
                return;
            }
            // Партия перезапускается/грузится — тихо выходим
            if (!UIManager.IsGameStarted || LoadingScreen.IsActive || _cam == null || _station == null || _station.Root == null)
            {
                _active = false;
                RestoreWorld();
                if (_station != null && _station.Root != null) _station.Root.SetActive(false);
                if (_hudRoot != null) _hudRoot.SetActive(false);
                if (_blockerRoot != null) _blockerRoot.SetActive(false);
                return;
            }
            if (_busy) { _player.InputEnabled = false; return; }

            bool ui = UiOpen();
            if (Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt)) _manualFree = !_manualFree;
            if (_manualFree && !ui && Input.GetMouseButtonDown(0) && !PointerOverRealUi()) _manualFree = false;
            if (!ui && Input.GetKeyDown(ToggleKey) && CanUseHotkey()) { Exit(); return; }

            ApplyCursor();
            bool walking = !_cursorFree;
            _player.InputEnabled = walking;
            if (_player.transform.position.y < _station.FloorY - 6f) _player.Teleport(_station.Spawn, _station.SpawnYaw);

            UpdateFocus(walking);
            if (walking && _focused != null && (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0))) Use(_focused);

            if (_crosshair != null) _crosshair.enabled = walking;
            if (_help != null) _help.enabled = !ui;
        }

        private void LateUpdate()
        {
            if (_active) UpdateCamera();
        }

        private void UpdateCamera()
        {
            if (_cam == null || _player == null) return;
            _cam.transform.SetPositionAndRotation(_player.EyePosition, _player.LookRotation);
        }

        private void UpdateFocus(bool walking)
        {
            StationTerminal hitTerm = null;
            if (walking && Physics.Raycast(_cam.transform.position, _cam.transform.forward, out var hit, InteractDistance,
                                           1 << StationBuilder.Layer, QueryTriggerInteraction.Ignore))
                hitTerm = hit.collider.GetComponentInParent<StationTerminal>();
            SetFocus(hitTerm);

            if (_prompt == null) return;
            if (_focused == null) { _prompt.text = ""; return; }
            var e = _focused.Entry;
            string reason = e.Blocked?.Invoke();
            string hex = ColorUtility.ToHtmlStringRGB(e.Color);
            _prompt.text = reason == null
                ? "<color=#FFFFFF><b>[E]</b></color>  <color=#" + hex + ">" + e.Title + "</color>"
                : "<color=#" + hex + ">" + e.Title + "</color>\n<size=15><color=#FF9A7A>" + reason + "</color></size>";
        }

        private void SetFocus(StationTerminal t)
        {
            if (_focused == t) return;
            if (_focused != null) _focused.SetFocused(false);
            _focused = t;
            if (_focused != null) _focused.SetFocused(true);
        }

        private void Use(StationTerminal t)
        {
            var e = t.Entry;
            string reason = e.Blocked?.Invoke();
            if (reason != null) { NotificationCenter.Show(e.Title, reason, NotificationCenter.Kind.Warning, 3f); return; }
            try { e.Use?.Invoke(); }
            catch (System.Exception ex) { Debug.LogWarning("[Station3D] Пульт «" + e.Title + "»: " + ex.Message); }
        }

        // ================================================================ Курсор и окна

        private void ApplyCursor(bool force = false)
        {
            bool free = _manualFree || UiOpen();
            if (!force && free == _cursorFree) return;
            _cursorFree = free;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        /// <summary>Открыто ли окно игры — тогда курсор свободен, ходьба на паузе.</summary>
        private static bool UiOpen()
        {
            var ui = UIManager.Instance;
            if (ui != null && ui.AnyWindowOpenPublic) return true;
            if (GameFlowUI.Instance != null && GameFlowUI.Instance.BlocksTimeHotkeys) return true;
            if (FeedbackWindow.IsOpen || ColonyManagerWindow.IsOpenNow || BuildMenuModal.IsOpenNow || NotificationLog.IsOpenNow) return true;
            if (GameSummaryWindow.IsOpenNow || EmpireGoalsChoice.IsOpenNow) return true;
            if (StellarisClone.Cards.BattleScreen.IsOpen || StellarisClone.Cards.AssaultDeckWindow.IsOpenNow) return true;
            if (TradeModal.Instance != null && TradeModal.Instance.IsOpen) return true;
            if (PlanetOverviewModal.Instance != null && PlanetOverviewModal.Instance.IsOpen) return true;
            if (TechConfirmDialog.Instance != null && TechConfirmDialog.Instance.IsOpen) return true;
            if (PlanetFocusOverlay.Instance != null && PlanetFocusOverlay.Instance.IsOpen) return true;
            return false;
        }

        private static bool CanUseHotkey()
        {
            if (!UIManager.IsGameStarted || LoadingScreen.IsActive) return false;
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            if (sel != null && sel.GetComponent<InputField>() != null) return false;
            if (UIManager.Instance != null && UIManager.Instance.IsBlockingFlowOpen) return false;
            if (GameFlowUI.Instance != null && GameFlowUI.Instance.BlocksTimeHotkeys) return false;
            return !StellarisClone.Cards.BattleScreen.IsOpen;
        }

        private readonly List<RaycastResult> _hits = new List<RaycastResult>();

        /// <summary>Курсор над настоящим интерфейсом игры (не над нашей прозрачной заглушкой).</summary>
        private bool PointerOverRealUi()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var data = new PointerEventData(es) { position = Input.mousePosition };
            _hits.Clear();
            es.RaycastAll(data, _hits);
            foreach (var h in _hits)
                if (h.gameObject != null && (_blocker == null || h.gameObject != _blocker.gameObject)) return true;
            return false;
        }

        // ================================================================ HUD

        private string StationTitle()
        {
            var s = EmpireStats.GetSystem(EconomyManager.PlayerCapitalId);
            string name = s != null && s.OwnerId == 0 ? s.Name : Loc.T("флагманская");
            return Loc.F("Звёздная станция «{0}»", name);
        }

        private void BuildHud()
        {
            if (_hudRoot != null)
            {
                _hudRoot.SetActive(true);
                _blockerRoot.SetActive(true);
                return;
            }

            // Прозрачная заглушка под всем интерфейсом: клики по 3D-виду не уходят в карту галактики
            _blockerRoot = NewCanvas("[3D] StationBlocker", -100, raycast: true);
            _blocker = StationBuilder.NewImage(_blockerRoot.transform, "Blocker", new Color(0f, 0f, 0f, 0f));
            _blocker.raycastTarget = true;
            StationBuilder.Stretch(_blocker.rectTransform, 0, 0, 0, 0);

            _hudRoot = NewCanvas("[3D] StationHUD", -50, raycast: false);
            var rt = (RectTransform)_hudRoot.transform;

            _crosshair = StationBuilder.NewImage(rt, "Crosshair", new Color(1f, 1f, 1f, 0.85f));
            _crosshair.rectTransform.sizeDelta = new Vector2(5, 5);

            _prompt = StationBuilder.NewText(rt, "Prompt", "", 20, FontStyle.Normal, Color.white, TextAnchor.UpperCenter);
            _prompt.rectTransform.sizeDelta = new Vector2(600, 80);
            _prompt.rectTransform.anchoredPosition = new Vector2(0, -60);
            AddShadow(_prompt);

            _title = StationBuilder.NewText(rt, "Title", "", 16, FontStyle.Bold, new Color(0.55f, 0.9f, 1f), TextAnchor.LowerCenter);
            _title.rectTransform.anchorMin = _title.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            _title.rectTransform.sizeDelta = new Vector2(900, 24);
            _title.rectTransform.anchoredPosition = new Vector2(0, 52);
            AddShadow(_title);

            _help = StationBuilder.NewText(rt, "Help",
                Loc.T("WASD — ходьба   ·   Shift — бег   ·   E — пульт   ·   Alt — курсор   ·   Esc / Y — на карту"),
                13, FontStyle.Normal, new Color(0.75f, 0.85f, 0.9f, 0.9f), TextAnchor.LowerCenter);
            _help.rectTransform.anchorMin = _help.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            _help.rectTransform.sizeDelta = new Vector2(1000, 22);
            _help.rectTransform.anchoredPosition = new Vector2(0, 30);
            AddShadow(_help);
        }

        private static void AddShadow(Text t)
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.8f);
            s.effectDistance = new Vector2(1.5f, -1.5f);
        }

        private static GameObject NewCanvas(string name, int order, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (raycast) go.AddComponent<GraphicRaycaster>();
            return go;
        }

        private void EnsureFade()
        {
            if (_fade != null) return;
            var go = NewCanvas("[3D] StationFade", 30000, raycast: false);
            _fadeCanvas = go.GetComponent<Canvas>();
            _fade = StationBuilder.NewImage(go.transform, "Fade", new Color(0f, 0f, 0f, 0f));
            StationBuilder.Stretch(_fade.rectTransform, 0, 0, 0, 0);
            _fadeCanvas.enabled = false;
        }

        private IEnumerator Fade(float to, float time)
        {
            EnsureFade();
            _fadeCanvas.enabled = true;
            float from = _fade.color.a;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                _fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / time));
                yield return null;
            }
            _fade.color = new Color(0f, 0f, 0f, to);
            if (to <= 0.001f) _fadeCanvas.enabled = false;
        }

        private void OnDisable()
        {
            if (_fadeCanvas != null) _fadeCanvas.enabled = false;
        }
    }
}
