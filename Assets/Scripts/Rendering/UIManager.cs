using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;
using StellarisClone.Cam;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    public partial class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }
        public static bool IsGameStarted { get; private set; }
        public static event System.Action OnGameStarted;

        /// <summary>
        /// Дизайн-токены Liquid Glass. Цвет фона = оттенок стекла (не заливка!),
        /// альфа = общая непрозрачность элемента. Всё стекло рисует LiquidGlassEffect.
        /// </summary>
        public static class DS
        {
            // Оттенки стекла (холодный дымчатый сине-бирюзовый, прозрачный Liquid Glass)
            public static readonly Color BgDeep        = new Color(0.030f, 0.062f, 0.088f, 1.00f);
            public static readonly Color BgPanel       = new Color(0.040f, 0.080f, 0.110f, 1.00f);
            public static readonly Color BgHeader      = new Color(0.070f, 0.130f, 0.170f, 0.95f);
            public static readonly Color BgSlot        = new Color(0.055f, 0.110f, 0.150f, 0.90f);
            public static readonly Color BgRowEven     = new Color(0.050f, 0.100f, 0.135f, 0.75f);
            public static readonly Color BgRowOdd      = new Color(0.060f, 0.118f, 0.155f, 0.75f);
            public static readonly Color BgVisor       = new Color(0.018f, 0.045f, 0.065f, 0.95f);

            // Акценты (приглушённый неон)
            public static readonly Color NeonCyan      = new Color(0.40f, 0.86f, 0.82f, 1.00f);
            public static readonly Color NeonTeal      = new Color(0.36f, 0.88f, 0.82f, 0.55f);
            public static readonly Color Gold          = new Color(0.95f, 0.78f, 0.40f, 1.00f);
            public static readonly Color Green         = new Color(0.44f, 0.88f, 0.60f, 1.00f);
            public static readonly Color Red           = new Color(0.95f, 0.42f, 0.42f, 1.00f);

            // Текст
            public static readonly Color TextPrimary   = new Color(0.94f, 0.98f, 1.00f, 1.00f);
            public static readonly Color TextMuted     = new Color(0.72f, 0.82f, 0.88f, 1.00f);
            public static readonly Color TextShadow    = new Color(0.000f, 0.020f, 0.035f, 0.55f);

            // Оттенки стеклянных кнопок
            public static readonly Color BtnPrimary    = new Color(0.10f, 0.40f, 0.48f, 1.00f);
            public static readonly Color BtnPrimaryHi  = new Color(0.30f, 0.80f, 0.86f, 1.00f);
            public static readonly Color BtnSuccess    = new Color(0.12f, 0.50f, 0.34f, 1.00f);
            public static readonly Color BtnDanger     = new Color(0.58f, 0.17f, 0.21f, 1.00f);
            public static readonly Color BtnNeutral    = new Color(0.05f, 0.10f, 0.16f, 1.00f);
            public static readonly Color BtnDisabled   = new Color(0.04f, 0.07f, 0.11f, 0.60f);
        }

        private Canvas _canvas;
        private Canvas _modalCanvas;
        private GameObject _modalDim;
        private Font _uiFont;
        private GalaxyGenerator _generator;

        private GameObject _factionSelectionModal;
        private GameObject _advisorIntroModal;
        private AdvisorIntroWindow _advisorIntro;
        private FactionHistoryWindow _history;
        private string _historyShownKey;
        private FactionInfo _selectedFaction;

        public FactionInfo SelectedFaction => _selectedFaction;

        private Text _energyVal, _mineralsVal, _alloysVal, _influenceVal, _researchVal, _stabilityVal;
        private TooltipTrigger _energyTip, _mineralsTip, _alloysTip, _influenceTip, _researchTip, _stabilityTip;


        private GameObject _eventPopupModal;
        private Transform _eventOptionsHolder;
        private RectTransform _eventDescBox, _eventOptionsRt;   // окно события — см. BuildEventPopupModal
        private Text _eventTitleText;
        private Text _eventDescText;
        private Image _eventAccentBar;

        private RectTransform _inspectorRect;
        private CanvasGroup _inspectorGroup;
        private Text _inspTitle, _inspStatus, _inspActionBtnText;
        private GameObject _districtRowRoot;
        private Button _inspActionBtn;
        private Image _inspActionBtnBg;
        private Button _inspExitBtn;
        private Button _inspShipyardBtn;
        private float _inspTargetAlpha;


        private GameObject _rewardPopUp;
        private Text _rewardText;
        private CanvasGroup _rewardGroup;

        private StarSystem _activeSystem;
        private PlanetData _activePlanet;
        private bool _isInSystemMode;

        private const float TopBarHeight = 44f;
        private const float TopBarMargin = 10f;
        /// <summary>Левый край верхней панели: правее карточки правителя (RulerBadge).</summary>
        public const float TopBarLeft = TopBarMargin + RulerBadge.Width + 8f;
        private const float StarbaseAlloysCost = 50f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            IsGameStarted = false;

            _uiFont = GameFont.Regular;

            EnsureEventSystemExists();
            SetupCanvas();

            Time.timeScale = 0f;

            if (TutorialManager.Instance == null)
            {
                var tutGo = new GameObject("TutorialManager");
                tutGo.AddComponent<TutorialManager>();
            }

            BuildFactionSelectionModal();
            BuildAdvisorIntroModal();
            _history = FactionHistoryWindow.Create(_modalCanvas.transform, () => OpenAdvisorIntroModal(_selectedFaction));
            BuildTopBar();
            BuildStellarisLeftInspector();
            BuildShipyardModal();
            new GameObject("[UI] BuildProgressBadge").AddComponent<BuildProgressBadge>().Init(_canvas);
            new GameObject("[UI] SystemPlanetTags").AddComponent<SystemPlanetTags>().Init(_canvas);
            new GameObject("[UI] Outliner").AddComponent<OutlinerPanel>().Init(_canvas);
            new GameObject("[UI] Directives").AddComponent<StarterGoalsPanel>().Init(_canvas);
            new GameObject("[UI] EmpireGoals").AddComponent<EmpireGoalsPanel>().Init(_canvas);
            EmpireGoalsChoice.Ensure();
            BuildEmpireOverviewModal();
            BuildEspionageModal();
            BuildEventPopupModal();
            BuildRewardNotification();
            BindAuxiliaryUi();

            // Игровой цикл: меню паузы (Esc), трекер целей с подсказками, экран итогов
            var flow = new GameObject("[UI] GameFlow");
            flow.transform.SetParent(transform, false);
            flow.AddComponent<GameFlowUI>();

            // HUD появляется только с началом партии
            SetHudVisible(false, instant: true);

            switch (GameSession.Mode)
            {
                case GameSession.StartMode.MainMenu:
                    if (_factionSelectionModal != null) _factionSelectionModal.SetActive(false);
                    // Фон меню живёт (звёзды, корабли анимированы), но игровой календарь стоит
                    Time.timeScale = 1f;
                    TimeManager.Instance?.SetSpeed(0);
                    var menu = new GameObject("[UI] MainMenu");
                    menu.transform.SetParent(transform, false);
                    menu.AddComponent<MainMenuUI>();
                    break;

                case GameSession.StartMode.LoadGame:
                    // Партию запустит SaveLoader после восстановления
                    if (_factionSelectionModal != null) _factionSelectionModal.SetActive(false);
                    break;

                default:
                    if (_factionSelectionModal != null) _factionSelectionModal.SetActive(true);
                    ShowModalDim();
                    break;
            }
        }

        // ==================== HUD / СТАРТ ПАРТИИ ====================

        private CanvasGroup _hudGroup;
        private Coroutine _hudFade;

        /// <summary>Полноэкранный обзор (планета) прячет весь HUD, чтобы панели не просвечивали.</summary>
        public void SetOverlayMode(bool on) => SetHudVisible(!on);

        private void SetHudVisible(bool visible, bool instant = false)
        {
            if (_hudGroup == null) return;
            _hudGroup.interactable = visible;
            _hudGroup.blocksRaycasts = visible;
            if (_hudFade != null) { StopCoroutine(_hudFade); _hudFade = null; }
            if (instant || !isActiveAndEnabled) { _hudGroup.alpha = visible ? 1f : 0f; return; }
            _hudFade = StartCoroutine(FadeHud(visible ? 1f : 0f));
        }

        private IEnumerator FadeHud(float target)
        {
            float start = _hudGroup.alpha;
            float t = 0f;
            while (t < 1f)
            {
                t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f) / 0.6f;
                _hudGroup.alpha = Mathf.Lerp(start, target, LGEase.OutCubic(t));
                yield return null;
            }
            _hudGroup.alpha = target;
            _hudFade = null;
        }

        /// <summary>Старт загруженной партии (вызывает SaveLoader после восстановления состояния).</summary>
        public void BeginLoadedGame(FactionInfo faction)
        {
            _selectedFaction = faction;
            ApplyPlayerIdentity();
            if (_factionSelectionModal != null) _factionSelectionModal.SetActive(false);
            if (_advisorIntroModal != null) _advisorIntroModal.SetActive(false);
            if (_history != null) _history.gameObject.SetActive(false);
            HideModalDim();
            Time.timeScale = 1f;
            SpawnInGameHUD();
            TimeManager.Instance?.SetSpeed(0);
            RefreshResourceBar();
        }

        /// <summary>
        /// Цвет империи на карте и корпуса кораблей по выбранной фракции. Стартовые флоты (и флоты из сохранения)
        /// создаются раньше, чем известна фракция игрока, — здесь их модели пересобираются, границы перекрашиваются.
        /// </summary>
        private void ApplyPlayerIdentity()
        {
            FleetIndicator.SetOwnColor(_selectedFaction != null ? _selectedFaction.MapColor : Color.clear);
            var fm = FleetManager.Instance;
            if (fm != null)
                foreach (var f in fm.AllFleets)
                    if (f != null && f.Data != null && !f.Data.Destroyed && f.Data.OwnerId == 0) f.RebuildModel();
            GalaxyView.Instance?.RefreshTerritoryVisuals();
        }

        private void ReturnToMainMenuFromSetup()
        {
            SFXManager.Play("ui_click", 0.7f, 0.9f);
            GameSession.ReturnToMainMenu();
        }

        private void Start()
        {
            _generator = FindAnyObjectByType<GalaxyGenerator>();

            StarSystemSelector.OnSystemSelected += OnSystemSelected;
            FleetSelectionController.OnEmptyClick += HandleEmptyMapClick;
            SystemViewManager.OnViewModeChanged += OnViewModeChanged;

            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed += (_, _, _) => RefreshResourceBar();

            if (EconomyManager.Instance != null)
                EconomyManager.Instance.OnResourcesChanged += RefreshResourceBar;

            AnomalyEventSystem.OnEventTriggered += HandleAnomalyEvent;

            BindAuxiliaryUi();
            RefreshResourceBar();
        }

        private void OnDestroy()
        {
            StarSystemSelector.OnSystemSelected -= OnSystemSelected;
            FleetSelectionController.OnEmptyClick -= HandleEmptyMapClick;
            SystemViewManager.OnViewModeChanged -= OnViewModeChanged;

            if (EconomyManager.Instance != null)
                EconomyManager.Instance.OnResourcesChanged -= RefreshResourceBar;

            AnomalyEventSystem.OnEventTriggered -= HandleAnomalyEvent;
        }

        private void EnsureEventSystemExists()
        {
            var es = FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
                go.AddComponent<StandaloneInputModule>();
            }
            // Без навигации с клавиатуры: иначе пробел/Enter («Submit») повторно нажимают последнюю кликнутую кнопку
            // и открывают окна, а пробел должен только ставить паузу. Поля ввода текст принимают и так.
            es.sendNavigationEvents = false;
        }

        private void SetupCanvas()
        {
            var old = GameObject.Find("GalaxyCanvas");
            if (old != null) Destroy(old);

            var canvasObj = new GameObject("GalaxyCanvas");
            canvasObj.transform.SetParent(transform, false);
            _canvas = canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();
            canvasObj.AddComponent<UIScaleTarget>();
            _hudGroup = canvasObj.AddComponent<CanvasGroup>();

            var modalObj = new GameObject("ModalCanvas");
            modalObj.transform.SetParent(canvasObj.transform, false);
            _modalCanvas = modalObj.AddComponent<Canvas>();
            _modalCanvas.overrideSorting = true;
            _modalCanvas.sortingOrder = 500;
            modalObj.AddComponent<GraphicRaycaster>();
            // Модалки (выбор цивилизации, пауза) не зависят от видимости HUD
            modalObj.AddComponent<CanvasGroup>().ignoreParentGroups = true;

            var mRt = modalObj.GetComponent<RectTransform>();
            mRt.anchorMin = Vector2.zero;
            mRt.anchorMax = Vector2.one;
            mRt.offsetMin = mRt.offsetMax = Vector2.zero;

            _modalDim = new GameObject("ModalDim");
            _modalDim.transform.SetParent(_modalCanvas.transform, false);
            var dRt = _modalDim.AddComponent<RectTransform>();
            dRt.anchorMin = Vector2.zero;
            dRt.anchorMax = Vector2.one;
            dRt.offsetMin = dRt.offsetMax = Vector2.zero;
            var dImg = _modalDim.AddComponent<Image>();
            dImg.color = LG.Palette.ScrimTint;
            dImg.raycastTarget = true;

            // Скрим: размытый и притушенный мир под модальными окнами
            LG.Scrim(_modalDim);
            var dimMotion = LG.Motion(_modalDim, LGAppear.Kind.Fade);
            dimMotion.inDuration = 0.32f;
            dimMotion.outDuration = 0.26f;
            _modalDim.SetActive(false);
        }

        public Canvas HudCanvas => _canvas;
        public Canvas ModalCanvas => _modalCanvas;
        public bool IsShipyardOpen => ShipyardWindow.Instance != null && ShipyardWindow.Instance.IsOpen;
        public void CloseShipyard() { if (IsShipyardOpen) ShipyardWindow.Instance.Close(); }

        /// <summary>Открыто окно, которое нельзя закрыть по Esc (выбор фракции, советник, событие).</summary>
        public bool IsBlockingFlowOpen =>
            (_factionSelectionModal != null && LG.IsVisible(_factionSelectionModal)) ||
            (_advisorIntroModal != null && LG.IsVisible(_advisorIntroModal)) ||
            (_history != null && _history.gameObject.activeSelf) ||
            (_eventPopupModal != null && LG.IsVisible(_eventPopupModal));

        public void ShowModalDimPublic() => ShowModalDim();
        public void HideModalDimPublic() => HideModalDim();
        public void RefreshResourceBarPublic() => RefreshResourceBar();
        public StarSystem ActiveSystemPublic => _activeSystem;
        // Затемнение — общая точка всех модальных окон, здесь же их звук открытия/закрытия
        private void ShowModalDim()
        {
            if (_modalDim == null) return;
            if (!LG.IsVisible(_modalDim)) SFXManager.Play(Sfx.WindowOpen);
            LG.Show(_modalDim);
        }

        private void HideModalDim()
        {
            if (_modalDim == null) return;
            if (LG.IsVisible(_modalDim)) SFXManager.Play(Sfx.WindowClose);
            LG.Hide(_modalDim);
        }

        /// <summary>Применяет Liquid Glass к модальному окну: стекло, пружинное появление.</summary>
        private static void StyleModalWindow(GameObject window, Color? rim = null)
        {
            var fx = LG.Glass(window);
            if (rim.HasValue) fx.SetRim(rim.Value);
            LG.Motion(window, LGAppear.Kind.Pop);
        }

        /// <summary>Плавно закрыть модалку и убрать скрим.</summary>
        private void CloseModal(GameObject window)
        {
            LG.Hide(window);
            HideModalDim();
            MapModeController.ShowGlobal();
        }

        public void RefreshOutliner() { }

        private void SpawnInGameHUD()
        {
            if (IsGameStarted) return;
            IsGameStarted = true;
            SetHudVisible(true);

            if (FindFirstObjectByType<MapModeController>() == null)
            {
                var go = new GameObject("[UI] MapModeController");
                go.transform.SetParent(transform, false);
                go.AddComponent<MapModeController>();
            }

            if (FindFirstObjectByType<GalaxyMinimap>() == null)
            {
                var go = new GameObject("[UI] GalaxyMinimap");
                go.transform.SetParent(transform, false);
                go.AddComponent<GalaxyMinimap>();
            }

            if (FindFirstObjectByType<TechTreePanel>() == null)
            {
                var go = new GameObject("[UI] TechTreePanel");
                go.transform.SetParent(transform, false);
                var panel = go.AddComponent<TechTreePanel>();
                panel.Initialize(_modalCanvas);
            }

            // Новости галактики (лента справа внизу) и гражданский трафик по коридорам
            GalacticNews.Reset();
            NotificationCenter.ClearHistory();
            if (FindFirstObjectByType<NewsFeedPanel>() == null)
                new GameObject("[UI] NewsFeed").AddComponent<NewsFeedPanel>().Init(_canvas);
            if (FindFirstObjectByType<CivilianTraffic>() == null)
                new GameObject("[World] CivilianTraffic").AddComponent<CivilianTraffic>();

            OnGameStarted?.Invoke();
            BindAuxiliaryUi();
        }

       private void BindAuxiliaryUi()
{
    if (_modalCanvas == null) return;

    ShipDesignerModal.Instance?.BindHost(_modalCanvas);
    PlanetOverviewModal.Instance?.BindHost(_modalCanvas);

    // === TechConfirmDialog ===
    if (TechConfirmDialog.Instance == null)
    {
        var go = new GameObject("[UI] TechConfirmDialog");
        go.transform.SetParent(transform, false);
        var dlg = go.AddComponent<TechConfirmDialog>();
        dlg.BindHost(_modalCanvas);
    }
    else
    {
        TechConfirmDialog.Instance.BindHost(_modalCanvas);
    }

    // === PlanetFocusOverlay — НОВОЕ ===
    if (PlanetFocusOverlay.Instance == null)
    {
        var go = new GameObject("[UI] PlanetFocusOverlay");
        go.transform.SetParent(transform, false);
        var ov = go.AddComponent<PlanetFocusOverlay>();
        ov.BindHost(_modalCanvas);
    }
    else
    {
        PlanetFocusOverlay.Instance.BindHost(_modalCanvas);
    }
    if (DiplomacyModal.Instance == null)
{
    var go = new GameObject("[UI] DiplomacyModal");
    go.transform.SetParent(transform, false);
    var m = go.AddComponent<DiplomacyModal>();
    m.BindHost(_modalCanvas);
}
else DiplomacyModal.Instance.BindHost(_modalCanvas);

if (TradeModal.Instance == null)
{
    var go = new GameObject("[UI] TradeModal");
    go.transform.SetParent(transform, false);
    var m = go.AddComponent<TradeModal>();
    m.BindHost(_modalCanvas);
}
else TradeModal.Instance.BindHost(_modalCanvas);
}

        // ==================== TOPBAR ====================

        /// <summary>
        /// Главный экран по референсу дизайна:
        ///   • слева сверху — карточка правителя (как герб), под ней вертикальное меню экранов;
        ///   • сверху по центру — рамка ресурсов: подпись над значением;
        ///   • под ней — рамка с датой и скоростью времени.
        /// </summary>
        /// <summary>Вертикальное меню экранов слева (как в референсе): значок и название, золотая кромка у «Путь к победе».</summary>
        private void BuildNavMenu()
        {
            if (VictoryPathWindow.Instance == null) new GameObject("[UI] VictoryPathWindow").AddComponent<VictoryPathWindow>();
            if (ColonyManagerWindow.Instance == null) new GameObject("[UI] ColonyManagerWindow").AddComponent<ColonyManagerWindow>();

            var menu = new GameObject("NavMenu");
            menu.transform.SetParent(_canvas.transform, false);
            var mRt = menu.AddComponent<RectTransform>();
            mRt.anchorMin = mRt.anchorMax = new Vector2(0, 1);
            mRt.pivot = new Vector2(0, 1);
            mRt.sizeDelta = new Vector2(NavMenuW, 11 * NavStep + 4 * NavGroupGap);
            _navOrder.Clear();
            _navSlots.Clear();
            mRt.anchoredPosition = new Vector2(TopBarMargin, -HoloTopBottom);
            UIAnchors.Register(UIAnchors.NavMenu, mRt);
            LG.Motion(menu, LGAppear.Kind.SlideLeft).distance = 22f;
            // Соты — в своём контейнере (его сворачивает кнопка под колонкой), кнопка — рядом, в самом меню
            var items = LGBuild.Rect(menu.transform, "Items");
            items.Stretch();
            LG.Ignore(items.gameObject, includeChildren: false);
            _navItemsGroup = items.gameObject.AddComponent<CanvasGroup>();
            _navItemsRt = items;

            // Пункт меню — шестиугольная сота со значком; название и клавиша — во флажке справа (наведение / окно открыто).
            // group = true — пункт открывает новую группу (перед ним зазор с чёрточкой).
            float hexX = RulerCenter.x - TopBarMargin;     // соты — ровно под центром портрета
            void Item(string name, LGIcon icon, string label, Color accent, System.Action act, string anchorId, string tip, string key,
                      System.Func<bool> isOpen = null, bool group = false)
            {
                var root = LGBuild.Rect(items, name);
                root.At(new Vector2(0, 1), new Vector2(0.5f, 1f), new Vector2(hexX, 0f), new Vector2(NavHexW, NavHexH));
                LG.Ignore(root.gameObject);

                var glow = HoloImage(root, "Glow", FactionSelectScreen.RadialSprite(), new Color(accent.r, accent.g, accent.b, 0f));
                glow.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(NavHexW + 40f, NavHexH + 40f));
                var fill = HoloImage(root, "Fill", HudArt.Hex(), new Color(0.030f, 0.070f, 0.098f, 0.95f));
                fill.rectTransform.Stretch();
                fill.raycastTarget = true;
                fill.alphaHitTestMinimumThreshold = 0.5f;   // наведение — по форме соты
                var rim = HoloImage(root, "Rim", HudArt.Hex(4f), new Color(accent.r, accent.g, accent.b, 0.55f));
                rim.rectTransform.Stretch();

                var tile = LGBuild.Rect(root, "IconTile");      // сюда же встают кружки-счётчики (решения, журнал)
                tile.Stretch();
                LG.Ignore(tile.gameObject);
                var ic = LGIcons.Create(tile, icon, 22f, accent);
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));
                ic.raycastTarget = false;

                var btn = root.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.targetGraphic = fill;
                btn.onClick.AddListener(() => { SFXManager.Play(Sfx.UiTab, 0.7f); act?.Invoke(); });

                // Флажок вместо подсказки: тёмная плашка со срезанными углами, цветная кромка слева,
                // сверху — название и клавиша, снизу — короткое описание (первая фраза подсказки)
                string desc = ShortNavDesc(tip);
                float fh = string.IsNullOrEmpty(desc) ? 34f : 48f;
                var flag = LGBuild.Rect(root, "Flag");
                flag.At(new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(8f, 0f), new Vector2(160f, fh));
                var fg = flag.gameObject.AddComponent<CanvasGroup>();
                fg.alpha = 0f;
                fg.blocksRaycasts = false;
                fg.interactable = false;
                var fbg = HoloImage(flag, "Bg", HudArt.CutPanel(10), new Color(0.025f, 0.060f, 0.085f, 0.96f));
                fbg.type = Image.Type.Sliced;
                fbg.rectTransform.Stretch();
                var fedge = HoloImage(flag, "Edge", null, accent);
                fedge.rectTransform.anchorMin = new Vector2(0, 0.12f);
                fedge.rectTransform.anchorMax = new Vector2(0, 0.88f);
                fedge.rectTransform.pivot = new Vector2(0, 0.5f);
                fedge.rectTransform.sizeDelta = new Vector2(3f, 0f);
                fedge.rectTransform.anchoredPosition = Vector2.zero;

                string title = label.ToUpper();
                float nameY = string.IsNullOrEmpty(desc) ? 0f : 8f;
                var ft = CreateText(flag, title, 13, FontStyle.Normal, DS.TextPrimary, TextAnchor.MiddleLeft);
                ft.font = GameFont.Display;
                ft.Track(1.2f);
                // preferredWidth не учитывает разрядку (LGTracking) — добавляем её на каждый символ
                float nameW = ft.preferredWidth + title.Length * 1.3f;
                ft.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14f, nameY), new Vector2(nameW + 4f, 20f));
                float w = 14f + nameW + 12f;
                if (!string.IsNullOrEmpty(key))
                {
                    var kb = HoloImage(flag, "Key", null, accent);
                    var kt = CreateText(kb.transform, key, 10, FontStyle.Bold, new Color(0.01f, 0.05f, 0.07f), TextAnchor.MiddleCenter);
                    kt.rectTransform.Stretch();
                    float kw = Mathf.Max(18f, kt.preferredWidth + 8f);
                    kb.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(w, nameY), new Vector2(kw, 16f));
                    w += kw + 12f;
                }
                if (!string.IsNullOrEmpty(desc))
                {
                    var fd = CreateText(flag, desc, 11, FontStyle.Normal, DS.TextMuted, TextAnchor.MiddleLeft);
                    fd.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14f, -11f), new Vector2(fd.preferredWidth + 4f, 16f));
                    w = Mathf.Max(w, 14f + fd.preferredWidth + 14f);
                }
                flag.sizeDelta = new Vector2(w, fh);

                var hx = root.gameObject.AddComponent<HexNavItem>();
                hx.Fill = fill; hx.Rim = rim; hx.Icon = ic; hx.Glow = glow;
                hx.Flag = flag; hx.FlagGroup = fg; hx.Accent = accent; hx.IsOpen = isOpen;

                Image divider = null;
                if (group)
                {
                    divider = HoloImage(items, name + "Divider", null, new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.35f));
                    divider.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(hexX, 0f), new Vector2(26f, 1f));
                }
                _navOrder.Add(root);
                _navSlots.Add(new NavSlot { Rt = root, Group = group, Divider = divider });
                if (anchorId != null) UIAnchors.Register(anchorId, root);
                // Отдельной подсказки нет: её роль играет флажок (подсказка у курсора закрывала бы его)
            }

            Item("NavEmpire", LGIcon.Globe, Loc.T("Империя"), DS.NeonCyan, () => ToggleWindow(NavWindow.Empire), null, Loc.T("<b>Обзор империи</b>\nСистемы, колонии, лидеры, статистика"), "I", () => IsWindowOpen(NavWindow.Empire));
            Item("NavColonies", LGIcon.Population, Loc.T("Колонии"), DS.Green, ColonyManagerWindow.Toggle, UIAnchors.Colonies,
                 Loc.T("<b>Управление колониями</b>\nВсе колонии одной таблицей: жители, стабильность, производство, стройка и «Купить»"), "C", () => IsWindowOpen(NavWindow.Colonies));
            Item("NavLaws", LGIcon.Menu, Loc.T("Законы и решения"), new Color(0.95f, 0.62f, 0.36f), () => ToggleWindow(NavWindow.Laws), UIAnchors.Laws,
                 Loc.T("<b>Законы и решения</b>\nЗаконы — постоянные правила империи с платой влиянием.\nРешения — проекты за титан и другие ресурсы: особые для вашей фракции, по обстоятельствам (война, кризис, эпоха) и редкие возможности с таймером. Кружок на значке — сколько решений можно принять прямо сейчас (золотой — появились новые)"), "L", () => IsWindowOpen(NavWindow.Laws));
            // Счётчик решений, которые можно принять, — кружок в углу значка (золотой — есть новые); виден и в свёрнутом меню
            var lawsTile = items.Find("NavLaws/IconTile");
            if (lawsTile != null)
            {
                // Бейдж-«флажок» со скосом (как в референсе) у правого верхнего угла соты
                _navDecisionPill = LGBuild.Panel(lawsTile, "DecisionBadge", DS.Green);
                _navDecisionPill.sprite = HudArt.CutPanel(4);
                _navDecisionPill.type = Image.Type.Sliced;
                _navDecisionPill.rectTransform.At(new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-4, -2), new Vector2(18, 15));
                _navDecisionBadge = CreateText(_navDecisionPill.transform, "", 9, FontStyle.Bold, new Color(0.02f, 0.06f, 0.05f), TextAnchor.MiddleCenter);
                _navDecisionBadge.rectTransform.anchorMin = Vector2.zero;
                _navDecisionBadge.rectTransform.anchorMax = Vector2.one;
                _navDecisionBadge.rectTransform.offsetMin = _navDecisionBadge.rectTransform.offsetMax = Vector2.zero;
                _navDecisionPill.gameObject.SetActive(false);
            }
            Item("NavTech", LGIcon.Research, Loc.T("Технологии"), DS.NeonCyan, () => ToggleWindow(NavWindow.Tech), UIAnchors.Tech, Loc.T("<b>Технологии</b>\nИсследования и очереди"), "T", () => IsWindowOpen(NavWindow.Tech));
            Item("NavDiplo", LGIcon.Diplomacy, Loc.T("Дипломатия"), DS.Gold, () => ToggleWindow(NavWindow.Diplomacy), UIAnchors.Diplomacy, Loc.T("<b>Дипломатия</b>\nПереговоры, сделки, договоры"), "P", () => IsWindowOpen(NavWindow.Diplomacy), true);
            Item("NavSpy", LGIcon.Betrayal, Loc.T("Шпионаж"), new Color(0.74f, 0.56f, 1f), () => ToggleWindow(NavWindow.Espionage), UIAnchors.Spy,
                 Loc.T("<b>Тайные операции</b>\nАгенты, внедрение в чужие империи, кража технологий, диверсии. Защита от чужих агентов"), "K", () => IsWindowOpen(NavWindow.Espionage));
            Item("NavDesign", LGIcon.Gear, Loc.T("Конструктор судов"), DS.NeonCyan, () => ToggleWindow(NavWindow.Designer), UIAnchors.Designer, Loc.T("<b>Конструктор судов</b>\nПроекты кораблей"), "N", () => IsWindowOpen(NavWindow.Designer), true);
            Item("NavArmy", LGIcon.Swords, Loc.T("Арсенал"), new Color(0.95f, 0.45f, 0.35f), () => ToggleWindow(NavWindow.Army), UIAnchors.Army,
                 Loc.T("<b>Арсенал</b>\nСостав армии для штурма и обороны колоний. Империя сама производит и пополняет карты, в бой они идут из общего резерва"), "J", () => IsWindowOpen(NavWindow.Army));
            // «Флот» убран из меню: все военные флоты по-прежнему выделяет клавиша G
            Item("NavVictory", LGIcon.Trophy, Loc.T("Путь к победе"), DS.Gold, VictoryPathWindow.Toggle, UIAnchors.Victory, Loc.T("<b>Путь к победе</b>\nВсе условия победы и кто к ним ближе"), "V", () => IsWindowOpen(NavWindow.Victory), true);
            Item("NavWonder", LGIcon.Wonder, Loc.T("Чудо фракции"), new Color(1f, 0.78f, 0.35f), () => ToggleWindow(NavWindow.Wonder), UIAnchors.Wonder,
                 Loc.T("<b>Чудо фракции</b>\nМегаструктура вашей фракции: этапы, место стройки, Сеть Врат, чудеса соперников"), "U", () => IsWindowOpen(NavWindow.Wonder));
            Item("NavLog", LGIcon.Calendar, Loc.T("Журнал событий"), DS.Gold, NotificationLog.Toggle, UIAnchors.LogHelp,
                 Loc.T("<b>Журнал событий</b>\nВсе всплывающие сообщения партии. Клик по записи — камера к месту события"), "O", () => NotificationLog.IsOpenNow);
            NotificationLog.AttachBadge(items.Find("NavLog/IconTile"));
            // «Справка» — маленькая кнопка «?» в шапке планировщика справа (OutlinerPanel), клавиша F1

            _navDiplo = items.Find("NavDiplo")?.GetComponent<RectTransform>();
            _navDiploShown = true;
            BuildNavFoldButton(menu.transform, hexX);
            RefreshDiplomacyNav(force: true);
        }

        // ---------- Левое меню: соты ----------

        /// <summary>Короткое описание пункта для флажка: текст подсказки после заголовка до первой точки или двоеточия.</summary>
        private static string ShortNavDesc(string tip)
        {
            if (string.IsNullOrEmpty(tip)) return "";
            int nl = tip.IndexOf('\n');
            string body = nl >= 0 ? tip.Substring(nl + 1) : "";
            body = System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", "");
            int cut = body.IndexOfAny(new[] { '.', ':', '\n' });
            if (cut > 0) body = body.Substring(0, cut);
            body = body.Trim();
            if (body.Length > 64) body = body.Substring(0, 62).TrimEnd() + "…";
            return body;
        }

        private const float NavHexW = 54f, NavHexH = 48f, NavStep = 52f, NavGroupGap = 12f;

        private struct NavSlot { public RectTransform Rt; public bool Group; public Image Divider; }
        private readonly List<NavSlot> _navSlots = new List<NavSlot>();

        /// <summary>Раскладка сот сверху вниз без пустот (скрытый пункт отдаёт свой зазор группы следующему).</summary>
        private void LayoutNavMenu()
        {
            float y = 0f;
            bool pendingGap = false;
            Image pendingDivider = null;
            for (int i = 0; i < _navSlots.Count; i++)
            {
                var sl = _navSlots[i];
                if (sl.Divider != null) sl.Divider.enabled = false;
                if (sl.Group) { pendingGap = true; if (pendingDivider == null) pendingDivider = sl.Divider; }
                if (sl.Rt == null || !sl.Rt.gameObject.activeSelf) continue;
                if (pendingGap && y > 0f)
                {
                    if (pendingDivider != null)
                    {
                        pendingDivider.enabled = true;
                        var p = pendingDivider.rectTransform.anchoredPosition;
                        pendingDivider.rectTransform.anchoredPosition = new Vector2(p.x, -(y + (NavGroupGap - (NavStep - NavHexH)) * 0.5f));
                    }
                    y += NavGroupGap;
                }
                pendingGap = false;
                pendingDivider = null;
                var ap = sl.Rt.anchoredPosition;
                sl.Rt.anchoredPosition = new Vector2(ap.x, -y);
                y += NavStep;
            }
            _navColumnH = y;
            ApplyNavFold(true);
        }

        // ---------- Свернуть / развернуть левое меню ----------

        private CanvasGroup _navItemsGroup;
        private RectTransform _navItemsRt, _navFoldRt;
        private Image _navFoldIcon;
        private Text _navFoldLabel;
        private float _navColumnH, _navFold, _navFoldV;
        private bool _navCollapsed;

        /// <summary>Кнопка под колонкой сот: свернуть меню (соты уезжают влево и тают) или развернуть обратно.</summary>
        private void BuildNavFoldButton(Transform menu, float x)
        {
            _navCollapsed = false;          // с начала партии меню всегда раскрыто
            _navFold = 0f;
            _navFoldRt = LGBuild.Rect(menu, "NavFold");
            _navFoldRt.At(new Vector2(0, 1), new Vector2(0.5f, 1f), new Vector2(x, 0f), new Vector2(44f, 22f));
            var bg = HoloImage(_navFoldRt, "Bg", HudArt.CutPanel(6), new Color(0.030f, 0.070f, 0.098f, 0.95f));
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;
            bg.rectTransform.Stretch();
            var rim = HoloImage(_navFoldRt, "Rim", null, new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.55f));
            rim.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(30f, 1f));
            _navFoldIcon = LGIcons.Create(_navFoldRt, LGIcon.Back, 11f, DS.TextMuted);
            _navFoldIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(11f, 11f));
            _navFoldIcon.raycastTarget = false;
            _navFoldLabel = CreateText(_navFoldRt, Loc.T("МЕНЮ"), 9, FontStyle.Bold, DS.TextMuted, TextAnchor.MiddleLeft);
            _navFoldLabel.rectTransform.At(new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(60f, 16f));
            _navFoldLabel.Track(1.6f);
            var b = bg.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                _navCollapsed = !_navCollapsed;
                SFXManager.Play(Sfx.PanelSlide);
            });
            TooltipHelper.Attach(bg.gameObject, Loc.T("Свернуть или развернуть меню"));
        }

        /// <summary>Пружина сворачивания: соты уезжают влево и тают, кнопка поднимается к верху колонки.</summary>
        private void ApplyNavFold(bool instant, float dt = 0f)
        {
            if (_navItemsGroup == null || _navFoldRt == null) return;
            float target = _navCollapsed ? 1f : 0f;
            if (instant) { _navFold = target; _navFoldV = 0f; }
            else
            {
                if (Mathf.Abs(_navFold - target) < 0.001f && Mathf.Abs(_navFoldV) < 0.001f) return;
                _navFold = LGEase.Spring(_navFold, target, ref _navFoldV, 3.2f, 0.8f, dt);
            }
            float k = Mathf.Clamp01(_navFold);
            _navItemsGroup.alpha = 1f - k;
            _navItemsGroup.blocksRaycasts = _navItemsGroup.interactable = k < 0.5f;
            _navItemsRt.anchoredPosition = new Vector2(-70f * _navFold, 0f);
            var p = _navFoldRt.anchoredPosition;
            _navFoldRt.anchoredPosition = new Vector2(p.x, -Mathf.Lerp(_navColumnH + 2f, 0f, k));
            _navFoldIcon.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(0f, 180f, k));   // ◀ — свернуть, ▶ — развернуть
            _navFoldLabel.color = new Color(DS.TextMuted.r, DS.TextMuted.g, DS.TextMuted.b, k);
        }

        private Image _navDecisionPill;
        private Text _navDecisionBadge;
        private float _navBadgeTimer;

        /// <summary>Раз в секунду: сколько решений можно принять сейчас (на кнопке «Законы и решения»).</summary>
        private void UpdateDecisionBadge(float dt)
        {
            if (_navDecisionPill == null || (_navBadgeTimer -= dt) > 0f) return;
            _navBadgeTimer = 1f;
            int fresh = EmpireDecisions.UnseenCount;
            int n = fresh > 0 ? fresh : EmpireDecisions.AvailableCount;
            _navDecisionPill.gameObject.SetActive(n > 0);
            LGFx.Pulse(_navDecisionPill.gameObject, fresh > 0);
            if (n <= 0) return;
            _navDecisionBadge.text = n > 9 ? "9+" : n.ToString();
            _navDecisionPill.color = fresh > 0 ? DS.Gold : DS.Green;
        }

        // ---------- Дипломатия появляется в меню только после первой встречи с другой империей ----------

        private readonly List<RectTransform> _navOrder = new List<RectTransform>();
        private RectTransform _navDiplo;
        private bool _navDiploShown;
        private bool _navDiploFresh;
        private float _navDiploTimer;

        /// <summary>
        /// Раз в полсекунды: встретил ли игрок хоть одну империю. До встречи пункта «Дипломатия» нет —
        /// остальные пункты сдвигаются вверх без пустого места. Появившийся пункт пульсирует, пока его не откроют.
        /// </summary>
        private void RefreshDiplomacyNav(bool force = false, float dt = 0f)
        {
            if (_navDiplo == null) return;
            if (!force && (_navDiploTimer -= dt) > 0f) return;
            _navDiploTimer = 0.5f;
            bool show = Contacts.PlayerMetAnyEmpire;
            if (!force && show == _navDiploShown)
            {
                if (_navDiploFresh && IsWindowOpen(NavWindow.Diplomacy))
                {
                    _navDiploFresh = false;
                    LGFx.Pulse(_navDiplo.gameObject, false);
                }
                return;
            }
            bool appeared = show && !_navDiploShown && !force;
            _navDiploShown = show;
            _navDiplo.gameObject.SetActive(show);
            LayoutNavMenu();
            if (appeared)
            {
                _navDiploFresh = true;
                LGFx.Pulse(_navDiplo.gameObject, true);
            }
        }

        // ---------- Главные окна: клавиши и «одно окно за раз» ----------

        public enum NavWindow { Empire, Colonies, Laws, Tech, Diplomacy, Espionage, Designer, Victory, Wonder, Help, Army }

        /// <summary>Открыть главное окно (если уже открыто — оставить), остальные закрыть. Для целей и подсказок.</summary>
        public void OpenNavWindow(NavWindow w)
        {
            if (!IsWindowOpen(w)) ToggleWindow(w);
        }

        private static bool IsWindowOpen(NavWindow w) => w switch
        {
            NavWindow.Empire => EmpireOverviewWindow.Instance != null && EmpireOverviewWindow.Instance.IsOpen,
            NavWindow.Colonies => ColonyManagerWindow.IsOpenNow,
            NavWindow.Laws => LawsWindow.IsOpenNow,
            NavWindow.Tech => TechTreePanel.Instance != null && TechTreePanel.Instance.IsOpen,
            NavWindow.Diplomacy => DiplomacyModal.Instance != null && DiplomacyModal.Instance.IsOpen,
            NavWindow.Espionage => EspionageWindow.Instance != null && EspionageWindow.Instance.IsOpen,
            NavWindow.Designer => ShipDesignerModal.Instance != null && ShipDesignerModal.Instance.IsOpen,
            NavWindow.Victory => VictoryPathWindow.Instance != null && VictoryPathWindow.Instance.IsOpen,
            NavWindow.Wonder => WonderWindow.Instance != null && WonderWindow.Instance.IsOpen,
            NavWindow.Army => StellarisClone.Cards.ArsenalWindow.IsOpenNow,
            _ => HelpOverlay.IsOpenNow
        };

        private void CloseWindow(NavWindow w)
        {
            if (!IsWindowOpen(w)) return;
            switch (w)
            {
                case NavWindow.Empire: EmpireOverviewWindow.Instance.Close(); break;
                case NavWindow.Colonies: ColonyManagerWindow.Instance.Close(); break;
                case NavWindow.Laws: LawsWindow.Instance.Close(); break;
                case NavWindow.Tech: TechTreePanel.Instance.Close(); break;
                case NavWindow.Diplomacy: DiplomacyModal.Instance.Close(); break;
                case NavWindow.Espionage: EspionageWindow.Instance.Close(); break;
                case NavWindow.Designer: ShipDesignerModal.Instance.Close(); break;
                case NavWindow.Victory: VictoryPathWindow.Instance.Close(); break;
                case NavWindow.Wonder: WonderWindow.Instance.Close(); break;
                case NavWindow.Army: StellarisClone.Cards.ArsenalWindow.Instance.Close(); break;
                case NavWindow.Help: HelpOverlay.Instance.Close(); break;
            }
        }

        private void CloseMainWindows(NavWindow? except)
        {
            foreach (NavWindow w in System.Enum.GetValues(typeof(NavWindow)))
                if (w != except) CloseWindow(w);
        }

        /// <summary>Открыть главное окно (остальные закрываются) или закрыть, если оно уже открыто.</summary>
        public void ToggleWindowPublic(NavWindow w) => ToggleWindow(w);

        private void ToggleWindow(NavWindow w)
        {
            if (IsWindowOpen(w)) { CloseWindow(w); return; }
            CloseMainWindows(w);
            switch (w)
            {
                case NavWindow.Empire: OpenEmpireOverviewModal(); break;
                case NavWindow.Colonies: ColonyManagerWindow.Toggle(); break;
                case NavWindow.Laws: LawsWindow.Toggle(); break;
                case NavWindow.Tech: OpenTechModal(); break;
                case NavWindow.Diplomacy: DiplomacyModal.Instance?.Open(); break;
                case NavWindow.Espionage: OpenEspionageModal(); break;
                case NavWindow.Designer: ShipDesignerModal.Instance?.Open(); break;
                case NavWindow.Victory: VictoryPathWindow.Toggle(); break;
                case NavWindow.Wonder: WonderWindow.Toggle(_activeSystem); break;
                case NavWindow.Army: StellarisClone.Cards.ArsenalWindow.Toggle(); break;
                case NavWindow.Help: HelpOverlay.Toggle(); break;
            }
        }

        /// <summary>Клавиши главных окон (как подписаны в левом меню). C и V окна ловят сами — здесь лишь закрываем остальные.</summary>
        private void HandleNavHotkeys()
        {
            if (!IsGameStarted || IsBlockingFlowOpen || LoadingScreen.IsActive) return;
            if (GameFlowUI.Instance != null && GameFlowUI.Instance.BlocksTimeHotkeys) return;
            if (StellarisClone.Cards.BattleScreen.IsOpen) return;
            if (PlanetFocusOverlay.Instance != null && PlanetFocusOverlay.Instance.IsOpen) return;
            if (BuildMenuModal.IsOpenNow || (TechConfirmDialog.Instance != null && TechConfirmDialog.Instance.IsOpen)) return;
            var es = UnityEngine.EventSystems.EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            if (sel != null && sel.GetComponent<InputField>() != null) return;

            if (Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.H)) ToggleWindow(NavWindow.Help);
            else if (Input.GetKeyDown(KeyCode.I)) ToggleWindow(NavWindow.Empire);
            else if (Input.GetKeyDown(KeyCode.L)) ToggleWindow(NavWindow.Laws);
            else if (Input.GetKeyDown(KeyCode.T)) ToggleWindow(NavWindow.Tech);
            else if (Input.GetKeyDown(KeyCode.P) && Contacts.PlayerMetAnyEmpire) ToggleWindow(NavWindow.Diplomacy);
            else if (Input.GetKeyDown(KeyCode.K)) ToggleWindow(NavWindow.Espionage);
            else if (Input.GetKeyDown(KeyCode.N)) ToggleWindow(NavWindow.Designer);
            else if (Input.GetKeyDown(KeyCode.U)) ToggleWindow(NavWindow.Wonder);
            else if (Input.GetKeyDown(KeyCode.J)) ToggleWindow(NavWindow.Army);
            else if (Input.GetKeyDown(KeyCode.G)) { CloseMainWindows(null); SelectAllWarships(); }
            else if (Input.GetKeyDown(KeyCode.C)) CloseMainWindows(NavWindow.Colonies);
            else if (Input.GetKeyDown(KeyCode.V)) CloseMainWindows(NavWindow.Victory);
            else if (Input.GetKeyDown(KeyCode.O)) NotificationLog.Toggle();
            else if (Input.GetKeyDown(KeyCode.Tab)) CycleColony(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
        }

        private int _colonyCursor = -1;

        /// <summary>Tab / Shift+Tab на карте: по очереди к своим колониям (столица первой, дальше по числу жителей).</summary>
        private void CycleColony(int dir)
        {
            var list = new List<StarSystem>();
            foreach (var s in EmpireStats.Systems)
            {
                if (s == null || s.OwnerId != 0) continue;
                foreach (var p in s.Planets) if (p.Population > 0) { list.Add(s); break; }
            }
            if (list.Count == 0) return;
            int Pop(StarSystem sy) { int n = 0; foreach (var p in sy.Planets) n += Mathf.Max(0, p.Population); return n; }
            list.Sort((a, b) =>
            {
                bool ca = a.Id == EconomyManager.PlayerCapitalId, cb = b.Id == EconomyManager.PlayerCapitalId;
                if (ca != cb) return ca ? -1 : 1;
                int c = Pop(b).CompareTo(Pop(a));
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            int i = list.FindIndex(s => s.Id == _colonyCursor);
            i = i < 0 ? (dir > 0 ? 0 : list.Count - 1) : ((i + dir) % list.Count + list.Count) % list.Count;
            _colonyCursor = list[i].Id;
            SFXManager.Play(Sfx.UiTab, 0.6f);
            FocusSystem(list[i]);
        }

        /// <summary>
        /// Камера к системе, выделить её и открыть панель — из списков, уведомлений, перебора колоний.
        /// Из вида системы сначала возвращает на карту галактики.
        /// </summary>
        public void FocusSystem(StarSystem sys)
        {
            if (sys == null) return;
            var svm = SystemViewManager.Instance;
            if (svm != null && svm.IsInSystemView) svm.ExitToGalaxyView();
            var cam = FindAnyObjectByType<StrategyCameraController>();
            if (cam != null) cam.FocusOn(sys.Position);
            GalaxyView.Instance?.SelectSystem(sys.Id);
            ShowSystemPanel(sys);
        }

        private void SelectAllWarships()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            var list = new List<FleetView>();
            foreach (var f in fm.AllFleets)
                if (f?.Data != null && !f.Data.Destroyed && f.Data.OwnerId == 0 && f.Data.Type == FleetType.Military) list.Add(f);
            fm.SetSelection(list);
        }

        /// <summary>
        /// Ячейка рамки ресурсов: значок в круглом «гнезде» со свечением цвета ресурса, подпись с разрядкой,
        /// крупное значение (прирост — мельче), снизу — светящаяся черта цвета ресурса, гаснущая к краям.
        /// </summary>
        private Text CreateResourceBadge(Transform parent, LGIcon icon, string label, Color accentCol, ref float xPos, out TooltipTrigger tip, bool separator)
        {
            const float w = 150f, h = 54f;
            var badge = new GameObject($"Badge_{label}");
            badge.transform.SetParent(parent, false);
            var rt = badge.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(xPos, 0);
            rt.sizeDelta = new Vector2(w, h);

            var img = badge.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = true;
            LG.Ignore(badge, includeChildren: false);

            tip = badge.AddComponent<TooltipTrigger>();
            UIAnchors.Register(UIAnchors.Resources, rt);

            // Разделитель между ячейками — гаснет к концам
            if (separator)
            {
                var sep = HudSprite(badge.transform, "Sep", FactionSelectScreen.RadialSprite(), new Color(0.45f, 0.95f, 0.90f, 0.35f));
                sep.rectTransform.anchorMin = new Vector2(0, 0.08f);
                sep.rectTransform.anchorMax = new Vector2(0, 0.92f);
                sep.rectTransform.sizeDelta = new Vector2(2f, 0f);
                sep.rectTransform.anchoredPosition = Vector2.zero;
            }

            // Гнездо значка: мягкое свечение, тёмный диск, кольцо цвета ресурса
            var halo = HudSprite(badge.transform, "Halo", FactionSelectScreen.RadialSprite(), new Color(accentCol.r, accentCol.g, accentCol.b, 0.22f));
            halo.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 1), new Vector2(60, 60));
            var disc = HudSprite(badge.transform, "Socket", SocketSprite(true), new Color(0.015f, 0.04f, 0.055f, 0.95f));
            disc.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 1), new Vector2(36, 36));
            var ring = HudSprite(badge.transform, "Ring", SocketSprite(false), new Color(accentCol.r, accentCol.g, accentCol.b, 0.85f));
            ring.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 1), new Vector2(36, 36));
            // Как в референсе голо-HUD: без гнезда — крупный значок со свечением цвета ресурса
            disc.enabled = false;
            ring.enabled = false;
            var ic = LGIcons.Create(badge.transform, icon, 26, accentCol);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 1), new Vector2(26, 26));
            var icGlow = ic.gameObject.AddComponent<Shadow>();
            icGlow.effectColor = new Color(accentCol.r, accentCol.g, accentCol.b, 0.35f);
            icGlow.effectDistance = new Vector2(0f, 0f);

            var cap = CreateText(badge.transform, label, 9, FontStyle.Bold, DS.TextMuted, TextAnchor.UpperLeft);
            cap.rectTransform.anchorMin = Vector2.zero;
            cap.rectTransform.anchorMax = Vector2.one;
            cap.rectTransform.offsetMin = new Vector2(56, 0);
            cap.rectTransform.offsetMax = new Vector2(-4, -8);
            cap.Track(1.4f);

            var val = CreateText(badge.transform, "0", 19, FontStyle.Normal, DS.TextPrimary, TextAnchor.LowerLeft);
            val.font = GameFont.Display;
            val.rectTransform.anchorMin = Vector2.zero;
            val.rectTransform.anchorMax = Vector2.one;
            val.rectTransform.offsetMin = new Vector2(56, 9);
            val.rectTransform.offsetMax = new Vector2(-4, 0);
            var vs = val.gameObject.AddComponent<Shadow>();
            vs.effectColor = new Color(0f, 0f, 0f, 0.6f);
            vs.effectDistance = new Vector2(0f, -1f);

            // Светящаяся черта цвета ресурса под ячейкой
            // Полоска под ячейкой (как в референсе): тусклая дорожка и заливка цвета ресурса
            var under = HudSprite(badge.transform, "BarTrack", null, new Color(1f, 1f, 1f, 0.08f));
            under.rectTransform.anchorMin = new Vector2(0f, 0f);
            under.rectTransform.anchorMax = new Vector2(1f, 0f);
            under.rectTransform.pivot = new Vector2(0f, 0f);
            under.rectTransform.offsetMin = new Vector2(10f, 3f);
            under.rectTransform.offsetMax = new Vector2(-10f, 5f);
            var barFill = HudSprite(under.transform, "Fill", null, new Color(accentCol.r, accentCol.g, accentCol.b, 0.95f));
            barFill.rectTransform.anchorMin = Vector2.zero;
            barFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            barFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            barFill.rectTransform.offsetMin = barFill.rectTransform.offsetMax = Vector2.zero;
            _resBarFills.Add(barFill.rectTransform);

            // Живая ячейка: докрутка чисел, всплывающая разница, вспышка, «дыхание», наведение
            var fx = badge.AddComponent<ResourceBadgeFx>();
            fx.Halo = halo; fx.Ring = null; fx.Icon = ic; fx.Underline = null; fx.Value = val; fx.Accent = accentCol;

            xPos += w + 4f;
            return val;
        }

        /// <summary>Запас ресурса в ячейке: через анимацию ячейки (докрутка, всплывающая разница), без неё — сразу текстом.</summary>
    private static void ShowRes(Text t, float value, string suffix)
    {
        var fx = t.GetComponentInParent<ResourceBadgeFx>();
        if (fx != null) fx.SetNumeric(value, suffix);
        else t.text = (int)value + suffix;
    }

    private static void ShowText(Text t, string text, float signal)
    {
        var fx = t.GetComponentInParent<ResourceBadgeFx>();
        if (fx != null) fx.SetText(text, signal);
        else t.text = text;
    }

    private static Image HudSprite(Transform parent, string name, Sprite sp, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var im = go.AddComponent<Image>();
            im.sprite = sp;
            im.color = c;
            im.raycastTarget = false;
            LG.Ignore(go);
            return im;
        }

        private static Sprite s_socketDisc, s_socketRing;

        /// <summary>Гнездо значка ресурса: filled — тёмный диск, иначе — тонкое кольцо с внутренним отсветом.</summary>
        private static Sprite SocketSprite(bool filled)
        {
            if (filled && s_socketDisc != null) return s_socketDisc;
            if (!filled && s_socketRing != null) return s_socketRing;
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = filled ? "HudSocketDisc" : "HudSocketRing", hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float a = filled
                    ? Mathf.Clamp01((0.94f - d) * 40f)
                    : Mathf.Max(Mathf.Clamp01(1f - Mathf.Abs(d - 0.90f) / 0.05f), Mathf.Pow(Mathf.Clamp01((d - 0.55f) / 0.33f), 3f) * 0.35f * (d < 0.9f ? 1f : 0f));
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            var sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            sp.hideFlags = HideFlags.DontSave;
            if (filled) s_socketDisc = sp; else s_socketRing = sp;
            return sp;
        }

                // ==================== ИНСПЕКТОР ====================

        private void BuildStellarisLeftInspector()
        {
            var insp = new GameObject("StarbaseInspector");
            insp.transform.SetParent(_canvas.transform, false);

            // Справа, в колонке планировщика (он уступает место, пока панель открыта)
            _inspectorRect = insp.AddComponent<RectTransform>();
            _inspectorRect.anchorMin = new Vector2(1, 1);
            _inspectorRect.anchorMax = new Vector2(1, 1);
            _inspectorRect.pivot = new Vector2(1, 1);
            _inspectorRect.anchoredPosition = InspectorRestPos;
            _inspectorRect.sizeDelta = new Vector2(InspectorWidth, 420);

            _inspectorGroup = insp.AddComponent<CanvasGroup>();
            _inspectorGroup.alpha = 0f;
            _inspectorGroup.blocksRaycasts = false;

            var inspBg = insp.AddComponent<Image>();
            inspBg.color = DS.BgDeep;
            inspBg.raycastTarget = false;
            LG.Glass(insp, 22f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.34f));
            BuildInspectorDecor(_inspectorRect);

            var header = new GameObject("Header");
            header.transform.SetParent(insp.transform, false);
            var hRt = header.AddComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0, 1);
            hRt.anchorMax = new Vector2(1, 1);
            hRt.pivot = new Vector2(0.5f, 1);
            hRt.sizeDelta = new Vector2(0, InspHeaderH);
            var hBg = header.AddComponent<Image>();
            hBg.color = DS.BgHeader;
            hBg.raycastTarget = false;
            LG.Header(header);
            // Свет звезды поверх фона шапки (под портретом и надписями) и редкий блик по шапке
            if (_inspHeaderGlow != null)
            {
                _inspHeaderGlow.transform.SetParent(header.transform, false);
                _inspHeaderGlow.transform.SetAsFirstSibling();
                _inspHeaderGlow.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(60, 0), new Vector2(460, 240));
            }
            InspSweep.Add(header, 7f, 0.07f);

            var hLine = new GameObject("HeaderLine");
            hLine.transform.SetParent(header.transform, false);
            var hlRt = hLine.AddComponent<RectTransform>();
            hlRt.anchorMin = new Vector2(0, 0);
            hlRt.anchorMax = new Vector2(1, 0);
            hlRt.pivot = new Vector2(0.5f, 0.5f);
            hlRt.sizeDelta = new Vector2(-32, 1.2f);
            hlRt.anchoredPosition = Vector2.zero;
            hLine.AddComponent<Image>().color = new Color(DS.NeonCyan.r, DS.NeonCyan.g, DS.NeonCyan.b, 0.55f);
            LG.Line(hLine, hairline: true);

            // Полоса владельца по верхнему краю и плитка со звездой (планетой) — «паспорт» объекта, как в больших стратегиях
            _inspAccent = LGBuild.Panel(insp.transform, "OwnerStripe", DS.NeonCyan);
            _inspAccent.rectTransform.anchorMin = new Vector2(0, 1);
            _inspAccent.rectTransform.anchorMax = new Vector2(1, 1);
            _inspAccent.rectTransform.pivot = new Vector2(0.5f, 1);
            _inspAccent.rectTransform.offsetMin = new Vector2(10, -3);
            _inspAccent.rectTransform.offsetMax = new Vector2(-10, 0);
            LG.Ignore(_inspAccent.gameObject);
            // Свечение под полосой владельца — цвет империи «стекает» на шапку
            _inspOwnerGlow = LGBuild.Panel(insp.transform, "OwnerGlow", new Color(DS.NeonCyan.r, DS.NeonCyan.g, DS.NeonCyan.b, 0.35f));
            _inspOwnerGlow.sprite = FactionSelectScreen.RadialSprite();
            _inspOwnerGlow.raycastTarget = false;
            _inspOwnerGlow.rectTransform.anchorMin = new Vector2(0.1f, 1);
            _inspOwnerGlow.rectTransform.anchorMax = new Vector2(0.9f, 1);
            _inspOwnerGlow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _inspOwnerGlow.rectTransform.sizeDelta = new Vector2(0, 26);
            _inspOwnerGlow.rectTransform.anchoredPosition = Vector2.zero;
            LG.Ignore(_inspOwnerGlow.gameObject);

            // Портрет: живая звезда (или значок планеты) в голографических кольцах цвета владельца
            BuildInspectorPortrait(header.transform);

            _inspTitle = CreateText(header.transform, "SOL STATION", 23, FontStyle.Bold, DS.TextPrimary, TextAnchor.LowerLeft);
            _inspTitle.rectTransform.anchorMin = new Vector2(0, 0.5f);
            _inspTitle.rectTransform.anchorMax = new Vector2(1, 1);
            _inspTitle.rectTransform.offsetMin = new Vector2(104, 2);
            _inspTitle.rectTransform.offsetMax = new Vector2(-48, -8);
            _inspTitle.Track(2f);
            var tsh = _inspTitle.gameObject.AddComponent<Shadow>();
            tsh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            tsh.effectDistance = new Vector2(0f, -2f);

            _inspStatus = CreateText(header.transform, "Starbase  ·  Outpost", 11, FontStyle.Bold, DS.NeonCyan, TextAnchor.UpperLeft);
            _inspStatus.rectTransform.anchorMin = new Vector2(0, 0);
            _inspStatus.rectTransform.anchorMax = new Vector2(1, 0.5f);
            _inspStatus.rectTransform.offsetMin = new Vector2(104, 0);
            _inspStatus.rectTransform.offsetMax = new Vector2(-48, -5);
            _inspStatus.Track(1.2f);

            var closeBtn = CreateCloseButton(header.transform, 26f, HideInspector);
            var cRt = closeBtn.GetComponent<RectTransform>();
            cRt.anchorMin = cRt.anchorMax = new Vector2(1, 0.5f);
            cRt.pivot = new Vector2(1, 0.5f);
            cRt.anchoredPosition = new Vector2(-14, 0);

            var bodyBox = new GameObject("BodyBox");
            bodyBox.transform.SetParent(insp.transform, false);
            var bbRt = bodyBox.AddComponent<RectTransform>();
            _inspBodyRect = bbRt;
            bbRt.anchorMin = Vector2.zero;
            bbRt.anchorMax = Vector2.one;
            bbRt.offsetMin = new Vector2(13, 120);
            bbRt.offsetMax = new Vector2(-13, -(InspHeaderH + 10f));

            var bbBg = bodyBox.AddComponent<Image>();
            bbBg.color = DS.BgSlot;
            bbBg.raycastTarget = false;
            // Непрозрачная подложка: подписи карты не должны просвечивать сквозь карточки
            LG.Platter(bodyBox, 16f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.18f));
            _inspBodyGroup = bodyBox.AddComponent<CanvasGroup>();
            // Содержимое — стопка карточек; если не помещается, прокручивается
            _inspStack = LGBuild.ScrollList(bodyBox.transform, 8f, 10);

            var distRow = new GameObject("DistrictRow");
            distRow.transform.SetParent(insp.transform, false);
            var drRt = distRow.AddComponent<RectTransform>();
            _inspDistrictRect = drRt;
            drRt.anchorMin = new Vector2(0, 0);
            drRt.anchorMax = new Vector2(1, 0);
            drRt.pivot = new Vector2(0.5f, 0);
            drRt.offsetMin = new Vector2(13, 60);
            drRt.offsetMax = new Vector2(-13, 100);

            var hl = distRow.AddComponent<HorizontalLayoutGroup>();
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = true;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.spacing = 4;
            _districtRowRoot = distRow;

            var actionBtn = CreateButton(insp.transform, "InspActionBtn", new Vector2(395, 32),
                DS.BtnPrimary, DS.BtnPrimaryHi, OnInspectorActionClick);
            var abRt = actionBtn.GetComponent<RectTransform>();
            abRt.anchorMin = new Vector2(0.5f, 0);
            abRt.anchorMax = new Vector2(0.5f, 0);
            abRt.pivot = new Vector2(0.5f, 0);
            abRt.anchoredPosition = new Vector2(0, 12);

            _inspActionBtnBg = actionBtn.GetComponent<Image>();
            InspSweep.Add(actionBtn, 5f, 0.14f);
            _inspActionBtn = actionBtn.GetComponent<Button>();
            _inspActionBtnText = CreateText(actionBtn.transform, Loc.T("ДЕЙСТВИЕ"), 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            _inspActionBtnText.rectTransform.sizeDelta = abRt.sizeDelta;

            // Кнопки снизу — в стиле пунктов левого меню (планка цвета действия, значок в ячейке, капслок)
            _inspShipyardBtn = InspGameButton(insp.transform, "ShipyardBtn", LGIcon.Shipyard, Loc.T("ОРБИТАЛЬНАЯ ВЕРФЬ"), DS.NeonCyan, true, OpenShipyardModal, new Vector2(395, 38));
            var sbRt = (RectTransform)_inspShipyardBtn.transform;
            sbRt.anchorMin = new Vector2(0.5f, 0);
            sbRt.anchorMax = new Vector2(0.5f, 0);
            sbRt.pivot = new Vector2(0.5f, 0);
            sbRt.anchoredPosition = new Vector2(0, 48);

            _inspExitBtn = InspGameButton(insp.transform, "ExitSystemBtn", LGIcon.Back, Loc.T("ВЕРНУТЬСЯ В ГАЛАКТИКУ"), DS.Red, true, () => SystemViewManager.Instance?.ExitToGalaxyView(), new Vector2(395, 38));
            var ebRt = (RectTransform)_inspExitBtn.transform;
            ebRt.anchorMin = new Vector2(0.5f, 0);
            ebRt.anchorMax = new Vector2(0.5f, 0);
            ebRt.pivot = new Vector2(0.5f, 0);
            ebRt.anchoredPosition = new Vector2(0, 48);
            var exitBtn = _inspExitBtn.gameObject;
            exitBtn.SetActive(false);
        }

        private RectTransform _inspBodyRect, _inspDistrictRect;
        private Image _inspAccent, _inspTileIcon;
        private const float InspHeaderH = 96f;

        /// <summary>Плитка и полоса шапки: значок объекта, его цвет и цвет владельца.</summary>
        private void SetInspectorBadge(LGIcon icon, Color iconColor, Color ownerColor, Sprite planet = null, StarSystem star = null)
        {
            PaintInspectorPortrait(icon == LGIcon.Star, icon, iconColor, ownerColor, planet, star);
            if (_inspAccent != null) _inspAccent.color = new Color(ownerColor.r, ownerColor.g, ownerColor.b, 0.9f);
        }

        private static Color StarTint(StarSpectralClass c) => StarTypes.Tint(c);

        /// <summary>
        /// Кнопки инспектора складываются снизу вверх (только видимые), над ними — ряд районов,
        /// а текст занимает всё оставшееся место. Кнопки больше не лежат друг на друге.
        /// </summary>
        private void LayoutInspector()
        {
            if (_inspBodyRect == null) return;
            float y = 12f;
            void Stack(Button b, float h)
            {
                if (b == null || !b.gameObject.activeSelf) return;
                var rt = (RectTransform)b.transform;
                rt.anchoredPosition = new Vector2(0, y);
                y += h + 6f;
            }
            Stack(_inspActionBtn, 32f);
            Stack(_inspShipyardBtn, 38f);
            Stack(_inspExitBtn, 38f);

            bool districts = _districtRowRoot != null && _districtRowRoot.transform.childCount > 0;
            if (districts)
            {
                _inspDistrictRect.offsetMin = new Vector2(13, y + 2f);
                _inspDistrictRect.offsetMax = new Vector2(-13, y + 44f);
                y += 50f;
            }
            _inspBodyRect.offsetMin = new Vector2(13, y + 4f);
            FitInspectorHeight(y + 4f);
        }

        private void ClearInspectorBody()
        {
            if (_inspStack != null)
            {
                _inspStack.anchoredPosition = Vector2.zero;   // новая система — с начала списка
                for (int i = _inspStack.childCount - 1; i >= 0; i--)
                {
                    var child = _inspStack.GetChild(i);
                    child.SetParent(null, false);
                    Destroy(child.gameObject);
                }
            }

            if (_districtRowRoot != null)
            {
                for (int i = _districtRowRoot.transform.childCount - 1; i >= 0; i--)
                {
                    // Отцепляем сразу: Destroy отложен до конца кадра, а раскладка считает детей сейчас
                    var child = _districtRowRoot.transform.GetChild(i);
                    child.SetParent(null, false);
                    Destroy(child.gameObject);
                }
            }
        }

        private void AddBodyHeader(string text) => InspSection(text, LGIcon.Info);

        private void AddBodyLine(string text) => InspText(text);

        private void CommitBody() { }

        private void AddDistrictRowFor(PlanetData planet)
        {
            if (_districtRowRoot == null || planet == null) return;

            DistrictType[] order = {
                DistrictType.Urban, DistrictType.Mining,
                DistrictType.Generator, DistrictType.Industrial
            };

            foreach (var dt in order)
            {
                int count = planet.GetDistrictCount(dt);
                bool canBuild = planet.CanBuildDistrict(dt);
                var eco = EconomyManager.Instance;
                bool canAfford = eco != null
                    && eco.Minerals >= DistrictInfo.MineralsCost(dt)
                    && eco.Alloys >= DistrictInfo.AlloysCost(dt);
                bool interactable = canBuild && canAfford;

                Color dc = DistrictInfo.Color(dt);

                var btnGo = new GameObject($"Dist_{dt}");
                btnGo.transform.SetParent(_districtRowRoot.transform, false);

                var img = btnGo.AddComponent<Image>();
                img.color = interactable ? DS.BgVisor : new Color(0.04f, 0.07f, 0.09f, 0.7f);
                img.raycastTarget = true;

                var btn = btnGo.AddComponent<Button>();
                btn.interactable = interactable;
                LG.Button(btnGo, new Color(dc.r, dc.g, dc.b, interactable ? 0.65f : 0.22f), 12f);
                var capturedDt = dt;
                btn.onClick.AddListener(() =>
                {
                    if (planet.TryBuildDistrict(capturedDt))
                    {
                        RefreshResourceBar();
                        ShowPlanetInspector(planet, _activeSystem, openOverview: false);
                    }
                });

                var t = CreateText(btnGo.transform, "", 9, FontStyle.Bold, DS.TextPrimary, TextAnchor.MiddleCenter);
                t.rectTransform.anchorMin = Vector2.zero;
                t.rectTransform.anchorMax = Vector2.one;
                t.rectTransform.offsetMin = new Vector2(2, 2);
                t.rectTransform.offsetMax = new Vector2(-2, -2);

                string nameShort = DistrictInfo.Name(dt).Split(' ')[0];
                string status;
                if (!canBuild) status = "■";
                else if (!canAfford) status = "✕";
                else status = "▶";

                t.text = $"<color=#{ColorUtility.ToHtmlStringRGB(dc)}>{status}</color> " +
                         $"{nameShort}\n" +
                         $"<color=#8AA2A8>×{count}</color>";
            }
        }

        // ==================== СИСТЕМА / ПЛАНЕТА ====================

        public void ShowSystemPanel(StarSystem system)
        {
            if (system == null) return;
            if (_inspStack == null || _inspTitle == null || _inspStatus == null) return;

            _activeSystem = system;
            _activePlanet = null;

            _inspTitle.text = system.Name.ToUpper();
            SetInspectorBadge(LGIcon.Star, StarTint(system.SpectralClass),
                system.OwnerId >= 0 ? FleetIndicator.OwnerColor(system.OwnerId) : new Color(0.45f, 0.55f, 0.62f),
                GalaxyView.PlanetPortrait(system, HeaderPlanet(system)), system);

            string owner = system.OwnerId == 0
                ? Loc.F("<color=#33E6CC>◆  Под контролем {0}</color>", _selectedFaction?.Name ?? Loc.T("Империи"))
                : system.OwnerId > 0
                    ? Loc.F("<color={0}>◆  Территория: {1}</color>", LGBuild.Hex(FleetIndicator.OwnerColor(system.OwnerId)), AIEmpireManager.NameOf(system.OwnerId, Loc.T("соперник")))
                    : system.OwnerId == NeutralWorlds.Owner && NeutralWorlds.At(system.Id) != null
                        ? Loc.F("<color={0}>◆  Вольный мир · {1}</color>", LGBuild.Hex(NeutralWorlds.ColorOf(NeutralWorlds.At(system.Id).Kind)), NeutralWorlds.CharacterName(NeutralWorlds.At(system.Id).Kind))
                        : Loc.T("<color=#8AA2A8>◆  Нейтральный фронтир</color>");
            _inspStatus.text = owner.Replace("◆  ", "");

            PulseInspectorContent();
            ClearInspectorBody();

            BuildSystemCards(system);
            InspCascade(system.Id);
            SyncStarbaseWindow(system);

            _inspShipyardBtn.gameObject.SetActive(system.OwnerId == 0 && !_isInSystemMode);

            if (!_isInSystemMode && system.OwnerId == -1)
            {
                _inspActionBtn.gameObject.SetActive(true);

                if (!system.IsSurveyed)
                {
                    // Корабль уже летит сюда или ведёт разведку — не предлагаем отправить ещё один
                    bool enRoute = false;
                    var fm = FleetManager.Instance;
                    if (fm != null)
                        foreach (var f in fm.AllFleets)
                            if (f != null && f.Data != null && !f.Data.Destroyed && f.Data.OwnerId == 0
                                && f.Data.Type == FleetType.Science && f.Data.SurveyTargetSystemId == system.Id) { enRoute = true; break; }
                    _inspActionBtn.interactable = !enRoute;
                    _inspActionBtnBg.color = enRoute ? new Color(0.10f, 0.25f, 0.20f) : DS.BtnSuccess;
                    _inspActionBtnText.text = enRoute ? Loc.T("◎  СУДНО В ПУТИ") : Loc.T("▶  НАПРАВИТЬ НАУЧНЫЙ КОРАБЛЬ");
                }
                else
                {
                    var eco = EconomyManager.Instance;
                    float finalInfluenceCost = FleetManager.OutpostInfluenceCost(0);   // с надбавкой за разрастание
                    float outpostAlloys = FactionTraits.StarbaseAlloys(0);
                    bool canAfford = eco != null && eco.Alloys >= outpostAlloys && eco.Influence >= finalInfluenceCost;

                    _inspActionBtn.interactable = canAfford;
                    _inspActionBtnBg.color = canAfford ? DS.BtnPrimary : new Color(0.15f, 0.20f, 0.25f);
                    _inspActionBtnText.text = canAfford
                        ? Loc.F("▶  ФОРПОСТ ({0} ⬢ · {1} ★)", (int)outpostAlloys, (int)finalInfluenceCost)
                        : Loc.T("✕  НЕДОСТАТОЧНО РЕСУРСОВ");
                }
            }
            else
            {
                _inspActionBtn.gameObject.SetActive(false);
            }

            LayoutInspector();
            _inspTargetAlpha = 1f;
            _inspectorGroup.blocksRaycasts = true;
        }

        private void OnSystemSelected(StarSystem system) => ShowSystemPanel(system);

        public void ShowPlanetInspector(PlanetData planet, StarSystem parentSystem, bool openOverview = true)
        {
            if (planet == null) return;
            if (_inspStack == null || _inspTitle == null || _inspStatus == null) return;

            _activePlanet = planet;
            if (parentSystem != null) _activeSystem = parentSystem;
            CloseStarbaseWindow();

            _inspTitle.text = PlanetNames.Title(planet.Name);
            SetInspectorBadge(LGIcon.Planet, parentSystem != null ? StarTint(parentSystem.SpectralClass) : DS.NeonCyan,
                parentSystem != null && parentSystem.OwnerId >= 0 ? FleetIndicator.OwnerColor(parentSystem.OwnerId) : new Color(0.45f, 0.55f, 0.62f),
                GalaxyView.PlanetPortrait(parentSystem, planet));
            _inspStatus.text = Loc.T("<color=#E5B842>◆  Планетарный объект</color>").Replace("◆  ", "");

            bool isSurveyed = _activeSystem != null && _activeSystem.IsSurveyed;

            PulseInspectorContent();
            ClearInspectorBody();

            if (!isSurveyed)
            {
                AddBodyHeader(Loc.T("КОЛОНИЯ"));
                AddBodyLine(Loc.T("Информация о населении и производстве появится после разведки системы."));
            }
            else if (planet.Type == PlanetType.GasGiant || planet.Type == PlanetType.Molten)
            {
                AddBodyHeader(Loc.T("КОЛОНИЗАЦИЯ НЕВОЗМОЖНА"));
                AddBodyLine(Loc.F("<color=#E5B842>Планета типа «{0}» непригодна для проживания.</color>", planet.Type));
                AddBodyLine(Loc.T("Можно построить только добывающий комплекс."));
            }
            else
            {
                AddBodyHeader(Loc.T("ОБЗОР КОЛОНИИ"));
                AddBodyLine(Loc.F("<color=#8AA2A8>Класс:</color> {0}", planet.Type));
                AddBodyLine(Loc.F("<color=#8AA2A8>Районы:</color> {0} / {1}", planet.BuiltDistricts, planet.MaxDistricts));
                AddBodyLine(Loc.F("<color=#8AA2A8>Залежи:</color> ◆ {0}  ⚡ {1}", planet.MineralDeposit, planet.EnergyDeposit));

                AddBodyHeader(Loc.T("НАСЕЛЕНИЕ"));
                int pop = planet.Population;
                int housing = planet.HousingCapacity;
                int employed = planet.EmployedPops;
                int idle = planet.IdlePops;

                string popColor = housing >= pop ? "#4DF08C" : "#FF6666";
                AddBodyLine(Loc.F("<color=#8AA2A8>Население:</color> <color={0}>{1}</color> / {2} жилья", popColor, pop, housing));
                AddBodyLine(Loc.F("<color=#8AA2A8>Рабочие:</color> {0}   ", employed) +
                            Loc.F("<color=#8AA2A8>Безработные:</color> {0}", StatFormat.Colored(idle, "", "0;0;0")));

                float growthPct = Mathf.Clamp(planet.PopGrowthProgress, 0f, 100f);
                float speedPct = planet.PopGrowthBaseSpeedPctPerMonth;
                AddBodyLine(Loc.F("<color=#8AA2A8>Рост:</color> {0}   ", StatFormat.Colored(speedPct, Loc.T("%/мес"))) +
                            Loc.F("Прогресс: {0:F1}%", growthPct));

                AddBodyHeader(Loc.T("ПРОИЗВОДСТВО / МЕСЯЦ"));
                AddBodyLine(Loc.F("<color=#33E6CC>◆ Титан:</color> {0}", StatFormat.Positive("+" + planet.ProducedMineralsPerMonth)));
                AddBodyLine(Loc.F("<color=#E5B842>⚡ Гелий-3:</color> {0}", StatFormat.Positive("+" + planet.ProducedEnergyPerMonth)));
                AddBodyLine(Loc.F("<color=#20EBB0>⬢ Сплавы:</color> {0}", StatFormat.Positive("+" + planet.ProducedAlloysPerMonth)));
            }

            CommitBody();
            InspCascade(-1000 - (_activeSystem != null ? _activeSystem.Id * 16 : 0) - (_activeSystem != null ? _activeSystem.Planets.IndexOf(planet) : 0));

            if (isSurveyed && planet.Type != PlanetType.GasGiant && planet.Type != PlanetType.Molten)
                AddDistrictRowFor(planet);

            _inspShipyardBtn.gameObject.SetActive(false);
            _inspActionBtn.gameObject.SetActive(true);

            if (!planet.HasMiningStation)
            {
                if (!isSurveyed)
                {
                    _inspActionBtn.interactable = false;
                    _inspActionBtnBg.color = new Color(0.15f, 0.20f, 0.25f);
                    _inspActionBtnText.text = Loc.T("✕  ТРЕБУЕТСЯ РАЗВЕДКА СИСТЕМЫ");
                }
                else if (!planet.IsPlayerOwned)
                {
                    _inspActionBtn.interactable = false;
                    _inspActionBtnBg.color = new Color(0.15f, 0.20f, 0.25f);
                    _inspActionBtnText.text = Loc.T("✕  СИСТЕМА НЕ ПРИНАДЛЕЖИТ ВАМ");
                }
                else
                {
                    var eco = EconomyManager.Instance;
                    bool canAfford = eco != null && eco.Minerals >= 50f;
                    _inspActionBtn.interactable = canAfford;
                    _inspActionBtnBg.color = canAfford ? DS.BtnSuccess : new Color(0.15f, 0.20f, 0.25f);
                    _inspActionBtnText.text = canAfford
                        ? Loc.T("▶  ДОБЫВАЮЩАЯ СТАНЦИЯ (50 ◆)")
                        : Loc.T("✕  НЕДОСТАТОЧНО ТИТАНА (50 ◆)");
                }
            }
            else
            {
                _inspActionBtn.interactable = false;
                _inspActionBtnBg.color = new Color(0.10f, 0.25f, 0.18f);
                _inspActionBtnText.text = Loc.T("✓  КОМПЛЕКС ФУНКЦИОНИРУЕТ");
            }

            LayoutInspector();
            _inspTargetAlpha = 1f;
            _inspectorGroup.blocksRaycasts = true;

            // Клик по планете сразу открывает полноэкранный обзор (без промежуточного окна)
            if (openOverview) PlanetFocusOverlay.Instance?.Open(planet, parentSystem);
        }

        private void OnViewModeChanged(bool inSystem)
        {
            _isInSystemMode = inSystem;
            _inspExitBtn.gameObject.SetActive(inSystem);
            LayoutInspector();

            if (!inSystem && _activeSystem != null)
                ShowSystemPanel(_activeSystem);
        }

        private void OnInspectorActionClick()
        {
            if (EconomyManager.Instance == null) return;

            if (_activePlanet != null)
            {
                if (FleetManager.Instance != null && FleetManager.Instance.BuildMiningStationOnPlanet(_activePlanet))
                {
                    ShowPlanetInspector(_activePlanet, _activeSystem, openOverview: false);
                    RefreshResourceBar();
                }
            }
            else if (_activeSystem != null)
            {
                if (!_activeSystem.IsSurveyed)
                {
                    if (FleetManager.Instance != null && FleetManager.Instance.OrderSurveySystem(_activeSystem.Id))
                    {
                        _inspActionBtn.interactable = false;
                        _inspActionBtnText.text = Loc.T("◎  СУДНО В ПУТИ");
                    }
                }
                else
                {
                    if (FleetManager.Instance != null && FleetManager.Instance.OrderBuildStarbase(_activeSystem.Id))
                    {
                        _inspActionBtn.interactable = false;
                        _inspActionBtnBg.color = new Color(0.10f, 0.25f, 0.20f);
                        _inspActionBtnText.text = Loc.T("⚙  СТРОИТЕЛЬСТВО НАЧАТО");
                        RefreshResourceBar();
                    }
                }
            }
        }

        private float _inspShow, _inspShowV;
        private float _inspBodyFade = 1f;
        private const float InspectorWidth = 430f;
        /// <summary>Под панелью режимов карты (она стоит на 66…108 px от верха).</summary>
        private const float InspectorTop = 118f;
        /// <summary>Ширина левого меню экранов (колонка сот).</summary>
        public const float NavMenuW = 76f;
        /// <summary>Панель системы — у правого края, в колонке планировщика.</summary>
        private static readonly Vector2 InspectorRestPos = new Vector2(-TopBarMargin, -InspectorTop);

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            HandleNavHotkeys();

            bool inspSettled = Mathf.Abs(_inspShow - _inspTargetAlpha) < 0.001f && Mathf.Abs(_inspShowV) < 0.001f;
            if (_inspectorGroup != null && !inspSettled)
            {
                // Инспектор выезжает справа на пружине и "проявляется"
                _inspShow = LGEase.Spring(_inspShow, _inspTargetAlpha, ref _inspShowV, 2.4f, 0.78f, dt);
                float k = Mathf.Clamp01(_inspShow);
                _inspectorGroup.alpha = LGEase.OutCubic(k);
                _inspectorRect.anchoredPosition = InspectorPos;
                _inspectorRect.localScale = Vector3.one * Mathf.LerpUnclamped(0.965f, 1f, _inspShow);
            }

            // Смена содержимого — мягкое "проявление" текста вместо мгновенной подмены
            if (_inspBodyGroup != null && _inspBodyFade < 1f)
            {
                _inspBodyFade = Mathf.MoveTowards(_inspBodyFade, 1f, dt * 4.5f);
                _inspBodyGroup.alpha = LGEase.OutCubic(_inspBodyFade);
            }

            UpdateStarbaseWindow(dt);
            UpdateHoloTop();
            UpdateDecisionBadge(dt);
            ApplyNavFold(false, dt);
            RefreshDiplomacyNav(dt: dt);
            OutlinerPanel.Yield = _inspTargetAlpha > 0.5f;
        }

        private void PulseInspectorContent()
        {
            if (_inspectorGroup != null && _inspectorGroup.alpha > 0.5f)
                _inspBodyFade = 0.25f;
        }

       private void RefreshResourceBar()
{
    var eco = EconomyManager.Instance;
    if (eco == null) return;

    // === ЭНЕРГИЯ ===
    if (_energyVal != null)
    {
        float net = eco.MonthlyEnergyIncome;
        ShowRes(_energyVal, eco.EnergyCredits, $"  <size=12>{StatFormat.Colored(net, "")}</size>");

        if (_energyTip != null) _energyTip.SetText(EnergyTip(eco));
    }

    // === МИНЕРАЛЫ ===
    if (_mineralsVal != null)
    {
        ShowRes(_mineralsVal, eco.Minerals, $"  <size=12>{StatFormat.Colored(eco.MonthlyMineralsIncome, "")}</size>");
        if (_mineralsTip != null) _mineralsTip.SetText(MineralsTip(eco));
    }

    // === СПЛАВЫ ===
    if (_alloysVal != null)
    {
        ShowRes(_alloysVal, eco.Alloys, $"  <size=12>{StatFormat.Colored(eco.MonthlyAlloysIncome, "")}</size>");
        if (_alloysTip != null) _alloysTip.SetText(AlloysTip(eco));
    }

    // === ВЛИЯНИЕ ===
    if (_influenceVal != null)
    {
        ShowRes(_influenceVal, eco.Influence, $"  <size=12>{StatFormat.Colored(eco.MonthlyInfluenceIncome, "")}</size>");
        if (_influenceTip != null) _influenceTip.SetText(InfluenceTip(eco));
    }

    UpdateStabilityBadge();

    // === НАУКА ===
    if (_researchVal != null && TechnologyManager.Instance != null)
    {
        var tm = TechnologyManager.Instance;
        ShowText(_researchVal, $"{StatFormat.Colored(tm.MonthlyResearchIncome, "")}<size=11><color=#8AA2A8>{Loc.T(" / мес")}</color></size>", tm.MonthlyResearchIncome);
        if (_researchTip != null) _researchTip.SetText(ResearchTip(tm));
    }
}

// ==================== ХЕЛПЕРЫ ДЛЯ ТУЛТИПОВ ====================

    /// <summary>Средняя стабильность колоний (взвешенная по жителям) и подсказка: уровни и самые неспокойные планеты.</summary>
    private void UpdateStabilityBadge()
    {
        if (_stabilityVal == null) return;
        var colonies = new List<PlanetData>();
        int pop = 0;
        float sum = 0f;
        foreach (var s in EmpireStats.Systems)
        {
            if (s.OwnerId != 0) continue;
            foreach (var p in s.Planets)
            {
                if (p.Population <= 0) continue;
                colonies.Add(p);
                pop += p.Population;
                sum += p.Stability * p.Population;
            }
        }
        if (colonies.Count == 0)
        {
            _stabilityVal.text = "—";
            _avgStability = -1f;
            _stabilityTip?.SetText(Loc.T("<b>Стабильность</b>\nПоявится, когда у империи будут колонии."));
            return;
        }
        float avg = sum / Mathf.Max(1, pop);
        _avgStability = avg;
        var tier = PlanetStability.TierOf(avg);
        string hex = PlanetStability.Hex(PlanetStability.TierColor(tier));
        int unstable = colonies.FindAll(p => p.Tier <= StabilityTier.Strike).Count;
        ShowText(_stabilityVal, $"<color=#{hex}>{avg:0}</color>  <size=10><color=#{hex}>{PlanetStability.TierName(tier)}</color></size>" +
                             (unstable > 0 ? $" <size=11><color=#FF6B6B>!{unstable}</color></size>" : ""), avg);
        if (_stabilityTip == null) return;
        colonies.Sort((a, b) => a.Stability.CompareTo(b.Stability));
        _stabilityTip.SetText(StabilityTip(colonies, avg, tier, hex));
    }


        // ==================== ОБЗОР ИМПЕРИИ ====================
        // Окно вынесено в EmpireOverviewWindow (шапка, ресурсы, правитель, статистика,
        // прогресс к победе и вкладки колоний / флотов / границ / соперника).

        private void BuildEmpireOverviewModal()
        {
            var go = new GameObject("[UI] EmpireOverviewWindow");
            go.transform.SetParent(transform, false);
            go.AddComponent<EmpireOverviewWindow>().Build(_modalCanvas);
        }

        public void OpenEmpireOverviewModal()
        {
            EmpireOverviewWindow.Instance?.Open();
        }

        // ==================== ТАЙНЫЕ ОПЕРАЦИИ ====================
        // Окно вынесено в EspionageWindow (агенты, внедрение, кража технологий, диверсии, раздор).

        private void BuildEspionageModal()
        {
            var go = new GameObject("[UI] EspionageWindow");
            go.transform.SetParent(transform, false);
            go.AddComponent<EspionageWindow>().Build(_modalCanvas);
        }

        public void OpenEspionageModal() => EspionageWindow.Instance?.Open();

        // ==================== ВЕРФЬ ====================
        // Окно вынесено в ShipyardWindow (заказ кораблей, стапели, модернизация).

        private void BuildShipyardModal()
        {
            var go = new GameObject("[UI] ShipyardWindow");
            go.transform.SetParent(transform, false);
            go.AddComponent<ShipyardWindow>().Build(_modalCanvas);
        }

        /// <summary>Верфь выбранной системы: корабли появятся у неё.</summary>
        private void OpenShipyardModal() => ShipyardWindow.Instance?.Open(_activeSystem != null ? _activeSystem.Id : -1);

        private void OpenTechModal()
        {
            if (TechTreePanel.Instance == null) return;
            ShowModalDim();
            TechTreePanel.Instance.Open();
        }

        // ==================== ФРАКЦИЯ ====================

        /// <summary>Выбор цивилизации — полноэкранный экран в духе Master of Orion (FactionSelectScreen).</summary>
        private void BuildFactionSelectionModal()
        {
            var rt = LGBuild.Rect(_modalCanvas.transform, "FactionSelectionModal");
            _factionSelectionModal = rt.gameObject;
            var screen = _factionSelectionModal.AddComponent<FactionSelectScreen>();
            screen.Build(OnFactionChosen, ReturnToMainMenuFromSetup);
        }

        private void OnFactionChosen(FactionInfo faction)
        {
            _selectedFaction = faction;
            LG.Hide(_factionSelectionModal);
            // Бонусы применяются при вступлении в должность: из передачи можно вернуться и выбрать другую.
            // Перед передачей — летопись фракции с голосом рассказчика (если включена в настройках)
            // (после «Назад» из передачи та же фракция идёт сразу к передаче — летопись уже видели)
            string key = FactionRegistry.KeyOf(faction);
            if (_history != null && FactionHistoryWindow.Available(faction) && key != _historyShownKey)
            {
                _historyShownKey = key;
                ShowModalDim();
                _history.Play(faction);
            }
            else OpenAdvisorIntroModal(faction);
        }

        private void BuildAdvisorIntroModal()
        {
            // «Входящая передача» от правителя: живой портрет, текст по буквам, бонусы, эффекты сигнала
            _advisorIntro = AdvisorIntroWindow.Create(_modalCanvas.transform, () =>
            {
                if (EconomyManager.Instance != null && _selectedFaction != null)
                    EconomyManager.Instance.ApplyFactionBonuses(_selectedFaction);
                ApplyPlayerIdentity();
                HideModalDim();
                Time.timeScale = 1f;
                if (!IsGameStarted) FindAnyObjectByType<StrategyCameraController>()?.ResetToCapital();
                SpawnInGameHUD();
                if (GameSession.Settings.Tutorial) TutorialManager.Instance?.BeginTutorial();
            },
            onBack: () =>
            {
                // Назад к выбору цивилизации; затемнение остаётся
                LG.Show(_factionSelectionModal);
                _factionSelectionModal.transform.SetAsLastSibling();
            });
            _advisorIntroModal = _advisorIntro.gameObject;
        }

        private void OpenAdvisorIntroModal(FactionInfo faction)
        {
            ShowModalDim();
            _advisorIntro.Open(faction);
        }

        // ==================== REWARD ====================

        private void BuildRewardNotification()
        {
            _rewardPopUp = new GameObject("RewardNotification");
            _rewardPopUp.transform.SetParent(_canvas.transform, false);

            var rt = _rewardPopUp.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.78f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(440, 60);

            _rewardGroup = _rewardPopUp.AddComponent<CanvasGroup>();
            _rewardGroup.alpha = 0f;
            _rewardGroup.blocksRaycasts = false;

            _rewardPopUp.AddComponent<Image>().color = DS.BgDeep;
            _rewardFx = LG.Glass(_rewardPopUp, 30f);
            _rewardFx.SetRim(new Color(DS.Green.r, DS.Green.g, DS.Green.b, 0.75f));

            _rewardText = CreateText(_rewardPopUp.transform, "", 12, FontStyle.Bold, DS.TextPrimary, TextAnchor.MiddleCenter);
            _rewardText.rectTransform.sizeDelta = rt.sizeDelta;
            _rewardText.supportRichText = true;

            _rewardPopUp.SetActive(false);
        }

        public void TriggerRewardPopUp(string sysName)
        {
            if (_rewardPopUp == null) return;
            _rewardPopUp.SetActive(true);
            _rewardText.text = Loc.F("★  <b>{0}</b>\n<color=#4DF08C>Разведка завершена!</color>", sysName);
            if (_rewardRoutine != null) StopCoroutine(_rewardRoutine);
            _rewardRoutine = StartCoroutine(RewardAnimRoutine());
        }

        private Coroutine _rewardRoutine;

        private LiquidGlassEffect _rewardFx;

        private IEnumerator RewardAnimRoutine()
        {
            var rt = (RectTransform)_rewardPopUp.transform;
            Vector2 basePos = Vector2.zero;
            rt.anchoredPosition = basePos;

            // Появление: капля стекла "выдувается" с пружинным перелётом
            float t = 0f;
            const float inDur = 0.5f;
            while (t < inDur)
            {
                t += Time.unscaledDeltaTime;
                float k = t / inDur;
                _rewardGroup.alpha = LGEase.OutCubic(k * 1.8f);
                float s = Mathf.LerpUnclamped(0.82f, 1f, LGEase.OutBack(k, 1.4f));
                rt.localScale = new Vector3(s, Mathf.LerpUnclamped(0.9f, 1f, LGEase.OutBack(k, 1.8f)), 1f);
                rt.anchoredPosition = basePos + new Vector2(0f, -14f * (1f - LGEase.OutCubic(k)));
                _rewardFx?.SetPulse(1f - k);
                yield return null;
            }
            _rewardGroup.alpha = 1f;
            rt.localScale = Vector3.one;
            rt.anchoredPosition = basePos;
            _rewardFx?.SetPulse(0f);

            yield return new WaitForSecondsRealtime(2.5f);

            // Исчезновение: всплывает вверх и тает
            t = 0f;
            const float outDur = 0.45f;
            while (t < outDur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / outDur);
                _rewardGroup.alpha = 1f - LGEase.OutCubic(k);
                rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.96f, LGEase.InCubic(k));
                rt.anchoredPosition = basePos + new Vector2(0f, 18f * LGEase.InOutCubic(k));
                yield return null;
            }
            rt.localScale = Vector3.one;
            rt.anchoredPosition = basePos;
            _rewardPopUp.SetActive(false);
        }

        // ==================== ИВЕНТЫ ====================

        // Окно события, вариант B «Кино»: иллюстрация на всё окно сверху и тает в тёмный низ, поверх неё —
        // плашка типа, крупный заголовок и подзаголовок по центру, ниже текст; варианты ответа — карточки в ряд.
        private const float EventW = 1040f, EventArtHeight = 430f, EventCardH = 118f, EventCardGap = 14f;
        private Text _eventKindText, _eventSubText;
        private Image _eventTitleGlow;
        private RectTransform _eventKindRt, _eventTitleRt, _eventBodyRt;
        private GridLayoutGroup _eventGrid;
        private LiquidGlassEffect _eventWindowFx;
        private EventPopupFX _eventFx;

        private void BuildEventPopupModal()
        {
            _eventPopupModal = new GameObject("EventPopupModal");
            _eventPopupModal.transform.SetParent(_modalCanvas.transform, false);

            var rt = _eventPopupModal.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(EventW, 640f);

            _eventPopupModal.AddComponent<Image>().color = DS.BgDeep;
            _eventWindowFx = LG.Glass(_eventPopupModal);
            LG.Motion(_eventPopupModal, LGAppear.Kind.Pop);

            // Тёмная подложка цвета, в который тает картинка: без неё под картинкой виден светлый край стекла
            var backdrop = new GameObject("Backdrop").AddComponent<Image>();
            backdrop.transform.SetParent(_eventPopupModal.transform, false);
            backdrop.color = EventPopupFX.Bg;
            backdrop.raycastTarget = false;
            backdrop.rectTransform.Stretch(1f, 1f, 1f, 1f);
            LG.Ignore(backdrop.gameObject);

            // Тело (заголовок + текст) — отдельный блок: EventPopupFX проявляет его после картинки
            var body = new GameObject("Body");
            body.transform.SetParent(_eventPopupModal.transform, false);
            _eventBodyRt = body.AddComponent<RectTransform>();
            _eventBodyRt.anchorMin = Vector2.zero;
            _eventBodyRt.anchorMax = Vector2.one;
            _eventBodyRt.offsetMin = _eventBodyRt.offsetMax = Vector2.zero;

            // Иллюстрация — во всю ширину от самого верха, под телом и кнопками
            _eventFx = EventPopupFX.Create(rt, 0f, 0f, EventArtHeight, _eventBodyRt);
            body.transform.SetAsLastSibling();

            // Верхняя строка поверх картинки: где и кто нашёл (слева) и закрыть (справа)
            _eventSubText = CreateText(_eventPopupModal.transform, "", 11, FontStyle.Bold, new Color(0.74f, 0.86f, 0.82f, 1f), TextAnchor.MiddleLeft);
            var sRt = _eventSubText.rectTransform;
            sRt.anchorMin = new Vector2(0, 1); sRt.anchorMax = new Vector2(1, 1); sRt.pivot = new Vector2(0.5f, 1f);
            sRt.offsetMin = new Vector2(30, -54); sRt.offsetMax = new Vector2(-70, -16);
            _eventSubText.Track(2.5f);
            var sh = _eventSubText.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.8f); sh.effectDistance = new Vector2(1, -1);

            var closeBtn = CreateCloseButton(_eventPopupModal.transform, 30f, () => CloseEventPopup());
            var cRt = closeBtn.GetComponent<RectTransform>();
            cRt.anchorMin = cRt.anchorMax = new Vector2(1, 1);
            cRt.pivot = new Vector2(1, 1);
            cRt.anchoredPosition = new Vector2(-18, -18);

            // Плашка типа события («◆ АНОМАЛИЯ» или глава цепочки) — по центру над заголовком
            var kind = new GameObject("Kind");
            kind.transform.SetParent(body.transform, false);
            _eventKindRt = kind.AddComponent<RectTransform>();
            _eventKindRt.anchorMin = _eventKindRt.anchorMax = new Vector2(0.5f, 1f);
            _eventKindRt.pivot = new Vector2(0.5f, 1f);
            var kImg = kind.AddComponent<Image>();
            kImg.sprite = HudArt.Pill(false); kImg.type = Image.Type.Sliced;
            kImg.color = new Color(0f, 0f, 0f, 0.55f);
            kImg.raycastTarget = false;
            var kFrame = new GameObject("Frame").AddComponent<Image>();
            kFrame.transform.SetParent(kind.transform, false);
            kFrame.sprite = HudArt.Pill(true); kFrame.type = Image.Type.Sliced;
            kFrame.raycastTarget = false;
            kFrame.rectTransform.Stretch();
            _eventAccentBar = kFrame;
            _eventKindText = CreateText(kind.transform, "", 10, FontStyle.Bold, DS.Gold, TextAnchor.MiddleCenter);
            _eventKindText.rectTransform.Stretch();
            _eventKindText.Track(3.5f);
            LG.Ignore(kind);

            // Мягкое свечение цвета аномалии за заголовком (без обводки букв)
            _eventTitleGlow = new GameObject("TitleGlow").AddComponent<Image>();
            _eventTitleGlow.transform.SetParent(body.transform, false);
            _eventTitleGlow.sprite = FactionSelectScreen.RadialSprite();
            _eventTitleGlow.raycastTarget = false;
            var gRt = _eventTitleGlow.rectTransform;
            gRt.anchorMin = gRt.anchorMax = new Vector2(0.5f, 1f);
            gRt.pivot = new Vector2(0.5f, 0.5f);
            gRt.sizeDelta = new Vector2(EventW * 0.62f, 110f);
            LG.Ignore(_eventTitleGlow.gameObject);

            // Крупный заголовок
            _eventTitleText = CreateText(body.transform, "", 34, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);
            _eventTitleText.font = GameFont.Display;
            _eventTitleRt = _eventTitleText.rectTransform;
            _eventTitleRt.anchorMin = _eventTitleRt.anchorMax = new Vector2(0.5f, 1f);
            _eventTitleRt.pivot = new Vector2(0.5f, 1f);
            _eventTitleRt.sizeDelta = new Vector2(EventW - 120f, 50f);
            _eventTitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _eventTitleText.resizeTextForBestFit = true;
            _eventTitleText.resizeTextMinSize = 20;
            _eventTitleText.resizeTextMaxSize = 34;
            var tsh = _eventTitleText.gameObject.AddComponent<Shadow>();
            tsh.effectColor = new Color(0, 0, 0, 0.9f); tsh.effectDistance = new Vector2(0, -2);

            // Описание — по центру, под заголовком
            var descRt = new GameObject("Description").AddComponent<RectTransform>();
            descRt.SetParent(body.transform, false);
            descRt.anchorMin = descRt.anchorMax = new Vector2(0.5f, 1f);
            descRt.pivot = new Vector2(0.5f, 1f);
            descRt.sizeDelta = new Vector2(EventW - 260f, 100f);
            _eventDescBox = descRt;
            _eventDescText = CreateText(descRt, "", 14, FontStyle.Normal, new Color(0.83f, 0.89f, 0.91f, 1f), TextAnchor.UpperCenter);
            _eventDescText.rectTransform.Stretch();
            _eventDescText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _eventDescText.verticalOverflow = VerticalWrapMode.Overflow;
            _eventDescText.lineSpacing = 1.2f;
            _eventDescText.supportRichText = true;

            // Варианты ответа — карточки в ряд (4 — сеткой 2×2, 5+ — по три в ряд)
            var optionsBox = new GameObject("Options");
            optionsBox.transform.SetParent(_eventPopupModal.transform, false);
            var oRt = optionsBox.AddComponent<RectTransform>();
            _eventOptionsRt = oRt;
            oRt.anchorMin = new Vector2(0, 0);
            oRt.anchorMax = new Vector2(1, 0);
            oRt.pivot = new Vector2(0.5f, 0);
            oRt.sizeDelta = new Vector2(-60f, EventCardH);
            oRt.anchoredPosition = new Vector2(0, 30f);
            _eventGrid = optionsBox.AddComponent<GridLayoutGroup>();
            _eventGrid.spacing = new Vector2(EventCardGap, EventCardGap);
            _eventGrid.childAlignment = TextAnchor.LowerCenter;
            _eventGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _eventOptionsHolder = optionsBox.transform;

            _eventPopupModal.SetActive(false);
        }

        private void CloseEventPopup()
        {
            if (_eventPopupModal != null) LG.Hide(_eventPopupModal);
            HideModalDim();
            MapModeController.ShowGlobal();
            Time.timeScale = 1f;
            AnomalyEventSystem.Instance?.NotifyClosed();
        }

        private void ShowEventPopup(GameEventData ev)
        {
            if (_eventPopupModal == null || ev == null) return;

            Color accent = AnomalyEventSystem.GetAnomalyAccent(ev.Anomaly);
            _eventWindowFx?.SetRim(new Color(accent.r, accent.g, accent.b, 0.55f));
            if (_eventAccentBar != null) _eventAccentBar.color = new Color(accent.r, accent.g, accent.b, 0.7f);

            // Плашка: глава цепочки (золотом) или тип события
            bool chain = !string.IsNullOrEmpty(ev.Chain);
            string kind = chain ? Loc.F("ЦЕПОЧКА «{0}»  ·  ГЛАВА {1}", ev.Chain.ToUpper(), Roman(ev.Step)) : "◆  " + EventKindName(ev);
            _eventKindText.text = kind;
            _eventKindText.color = chain ? DS.Gold : Color.Lerp(accent, Color.white, 0.25f);
            float kindW = kind.Length * 10.5f + 40f;   // 10 px жирным с разрядкой 3.5
            _eventKindRt.sizeDelta = new Vector2(Mathf.Max(140f, kindW), 24f);

            _eventTitleText.text = (ev.Title ?? "").ToUpper();
            _eventTitleGlow.color = new Color(accent.r, accent.g, accent.b, 0.22f);
            _eventSubText.text = string.IsNullOrEmpty(ev.Subtitle) ? "" : "◆  " + ev.Subtitle.ToUpper();
            _eventDescText.text = ev.Description ?? "";

            bool hasArt = _eventFx != null && _eventFx.SetArt(ev.Art, accent);

            // Нет ни одного доступного варианта (например, все требуют учёного) — окно не должно запирать игру
            bool anyAvailable = false;
            if (ev.Options != null)
                foreach (var o in ev.Options) if (o != null && o.Available) anyAvailable = true;
            if (!anyAvailable)
            {
                ev.Options ??= new List<EventOption>();
                ev.Options.Add(new EventOption(Loc.T("ОСТАВИТЬ КАК ЕСТЬ"), Loc.T("Ничего не делать: подходящих вариантов сейчас нет"), null));
            }

            // Сетка карточек: до трёх — в один ряд, четыре — 2×2, больше — по три
            int optCount = 0;
            if (ev.Options != null) foreach (var o in ev.Options) if (o != null) optCount++;
            optCount = Mathf.Max(1, optCount);
            int cols = optCount <= 3 ? optCount : optCount == 4 ? 2 : 3;
            int rows = Mathf.CeilToInt(optCount / (float)cols);
            float gridW = EventW - 60f;
            float cardW = Mathf.Min(420f, (gridW - (cols - 1) * EventCardGap) / cols);
            _eventGrid.constraintCount = cols;
            _eventGrid.cellSize = new Vector2(cardW, EventCardH);
            float optsH = rows * EventCardH + (rows - 1) * EventCardGap;
            _eventOptionsRt.sizeDelta = new Vector2(-60f, optsH);

            // Вертикаль: заголовок — на нижней части картинки, без картинки — сверху
            float y = hasArt ? EventArtHeight - 150f : 64f;
            _eventKindRt.anchoredPosition = new Vector2(0, -y);
            _eventTitleRt.anchoredPosition = new Vector2(0, -(y + 34f));
            _eventTitleGlow.rectTransform.anchoredPosition = new Vector2(0, -(y + 59f));
            float descTop = y + 34f + 56f;
            float descH = Mathf.Ceil(_eventDescText.preferredHeight) + 4f;
            _eventDescBox.sizeDelta = new Vector2(EventW - 260f, descH);
            _eventDescBox.anchoredPosition = new Vector2(0, -descTop);
            float h = descTop + descH + 30f + optsH + 30f;
            _eventPopupModal.GetComponent<RectTransform>().sizeDelta = new Vector2(EventW, Mathf.Max(hasArt ? EventArtHeight + 60f + optsH : 360f, h));
            _eventFx?.ClearOptions();

            for (int i = _eventOptionsHolder.childCount - 1; i >= 0; i--)
                Destroy(_eventOptionsHolder.GetChild(i).gameObject);

            if (ev.Options != null)
            {
                int idx = 0;
                bool first = true;
                foreach (var opt in ev.Options)
                {
                    if (opt == null) continue;
                    idx++;
                    BuildEventCard(opt, idx, accent, first && opt.Available);
                    if (opt.Available) first = false;
                }
            }

            LG.Show(_eventPopupModal);
            _eventPopupModal.transform.SetAsLastSibling();
            LG.Skin(_eventPopupModal.transform);
            _eventFx?.Play();
        }

        /// <summary>
        /// Карточка варианта ответа: номер в кружке (цвет — обычный, особый или недоступный), текст варианта жирным,
        /// под ним — первая строка последствий или чего не хватает; подробности — в подсказке.
        /// </summary>
        private void BuildEventCard(EventOption opt, int idx, Color accent, bool primary)
        {
            bool available = opt.Available;
            bool special = !string.IsNullOrEmpty(opt.Requirement);
            Color edge = !available ? DS.BtnDisabled : special ? DS.Gold : primary ? accent : DS.NeonCyan;
            var capturedOpt = opt;

            var card = CreateButton(_eventOptionsHolder, $"Opt_{idx}", new Vector2(300f, EventCardH),
                available ? new Color(0.045f, 0.085f, 0.105f, 0.92f) : new Color(0.045f, 0.085f, 0.105f, 0.55f), edge, () =>
                {
                    if (!capturedOpt.Available) return;
                    try { capturedOpt.OnSelect?.Invoke(); }
                    catch (System.Exception e) { Debug.LogWarning($"[Event] OnSelect: {e.Message}"); }
                    CloseEventPopup();
                });
            var sel = card.GetComponent<Selectable>();
            if (sel != null) sel.interactable = available;

            // Фон карточки: крупный бледный значок главного ресурса варианта (что даёт, иначе — что требует), срезан краем
            var main = ParseEffects(opt.ResultTooltip);
            int best = main.FindIndex(f => f.Sign > 0);
            if (best < 0 && special) { var rq = ParseEffects(opt.Requirement); if (rq.Count > 0) { main = rq; best = 0; } }
            if (best < 0 && main.Count > 0) best = 0;
            if (best >= 0)
            {
                card.AddComponent<RectMask2D>();
                var wm = LGIcons.Create(card.transform, main[best].Icon, 118f, new Color(main[best].Tint.r, main[best].Tint.g, main[best].Tint.b, available ? 0.09f : 0.04f), "Watermark");
                var wrt = wm.rectTransform;
                wrt.anchorMin = wrt.anchorMax = new Vector2(1, 0.5f);
                wrt.pivot = new Vector2(0.5f, 0.5f);
                wrt.anchoredPosition = new Vector2(-34f, -8f);
                wrt.localRotation = Quaternion.Euler(0, 0, -14f);
                wm.raycastTarget = false;
                LG.Ignore(wm.gameObject);
            }
            if (primary)
            {
                var ol = card.AddComponent<Outline>();
                ol.effectColor = new Color(accent.r, accent.g, accent.b, 0.55f);
                ol.effectDistance = new Vector2(1.5f, -1.5f);
            }

            // Значок в левом верхнем углу
            var ic = new GameObject("Mark").AddComponent<RectTransform>();
            ic.SetParent(card.transform, false);
            ic.anchorMin = ic.anchorMax = new Vector2(0, 1);
            ic.pivot = new Vector2(0, 1);
            ic.anchoredPosition = new Vector2(18, -16);
            ic.sizeDelta = new Vector2(26, 26);
            Color icCol = !available ? new Color(0.40f, 0.47f, 0.51f, 1f) : special ? DS.Gold : primary ? accent : DS.NeonCyan;
            var ring = ic.gameObject.AddComponent<Image>();
            ring.sprite = HudArt.Ring(0.86f, 1f);
            ring.color = icCol;
            ring.raycastTarget = false;
            var n = CreateText(ic, idx.ToString(), 12, FontStyle.Bold, icCol, TextAnchor.MiddleCenter);
            n.rectTransform.Stretch();

            // Особый вариант — что он требует, мелко справа от номера (ресурс — со значком)
            if (special)
            {
                float rx = 52f;
                var reqRes = ParseEffects(opt.Requirement);
                if (reqRes.Count > 0)
                {
                    var ri = LGIcons.Create(card.transform, reqRes[0].Icon, 14f, reqRes[0].Tint);
                    var rit = ri.rectTransform;
                    rit.anchorMin = rit.anchorMax = new Vector2(0, 1); rit.pivot = new Vector2(0, 0.5f);
                    rit.anchoredPosition = new Vector2(rx, -29f);
                    rx += 19f;
                }
                var req = CreateText(card.transform, opt.Requirement.ToUpper(), 9, FontStyle.Bold,
                    available ? DS.Gold : new Color(0.55f, 0.64f, 0.68f, 1f), TextAnchor.MiddleLeft);
                var rr = req.rectTransform;
                rr.anchorMin = new Vector2(0, 1); rr.anchorMax = new Vector2(1, 1); rr.pivot = new Vector2(0, 1);
                rr.offsetMin = new Vector2(rx, -42); rr.offsetMax = new Vector2(-14, -16);
                req.Track(1.5f);
            }

            var label = CreateText(card.transform, opt.OptionText, 13, FontStyle.Bold,
                available ? DS.TextPrimary : new Color(0.56f, 0.64f, 0.68f, 1f), TextAnchor.UpperLeft);
            var lr = label.rectTransform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(18, 30); lr.offsetMax = new Vector2(-16, -50);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.supportRichText = true;

            // Нижняя строка: ресурсы из последствий — значками с числами; иначе текстом
            var fx = available ? ParseEffects(opt.ResultTooltip) : new List<EventFx>();
            if (fx.Count > 0) BuildEffectChips(card.transform, fx);
            string hint = fx.Count > 0 ? null : !available
                ? (IsScientistRequirement(opt.Requirement) ? Loc.T("Нанять учёного: Империя (I) → Лидеры") : Loc.F("Требуется: {0}", opt.Requirement))
                : FirstLine(opt.ResultTooltip);
            if (!string.IsNullOrEmpty(hint))
            {
                var sub = CreateText(card.transform, hint, 10, FontStyle.Normal, new Color(0.56f, 0.64f, 0.68f, 1f), TextAnchor.LowerLeft);
                var sr = sub.rectTransform;
                sr.anchorMin = Vector2.zero; sr.anchorMax = new Vector2(1, 0); sr.pivot = new Vector2(0.5f, 0);
                sr.offsetMin = new Vector2(18, 12); sr.offsetMax = new Vector2(-16, 30);
                sub.horizontalOverflow = HorizontalWrapMode.Wrap;
                sub.verticalOverflow = VerticalWrapMode.Truncate;
                sub.supportRichText = true;
            }
            _eventFx?.AddOption(card, label.rectTransform);

            string tip = opt.ResultTooltip ?? "";
            if (special) tip = (available ? Loc.T("<color=#F2C747>Особый вариант: ") : Loc.T("<color=#FF8A8A>Недоступно — требуется: ")) + opt.Requirement + "</color>\n" + tip;
            if (!available && IsScientistRequirement(opt.Requirement)) tip += "\n\n" + ScientistHint;
            if (!string.IsNullOrEmpty(tip))
            {
                var tt = card.GetComponent<TooltipTrigger>();
                if (tt == null) TooltipHelper.Attach(card, tip);
                else tt.SetText(tip);
            }
        }

        // ---------- Последствия вариантов: ресурсы значками ----------

        private struct EventFx { public LGIcon Icon; public Color Tint; public string Value; public int Sign; }

        private static readonly System.Text.RegularExpressions.Regex s_fxRx = new System.Text.RegularExpressions.Regex(
            @"([+−\-]?)\s*(\d+)\s*(гели[йяю]-3|титан\w*|сплав\w*|влияни\w*|наук\w*|стабильност\w*|helium-3|titanium|alloys?|influence|science|research|stability)(\s*(?:в месяц|/мес|per month|a month))?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Числа с ресурсами из текста последствий («+6 титана и +3 гелия-3 в месяц», «−40 гелия-3»).</summary>
        private static List<EventFx> ParseEffects(string text)
        {
            var list = new List<EventFx>();
            if (string.IsNullOrEmpty(text)) return list;
            string plain = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "");
            foreach (System.Text.RegularExpressions.Match m in s_fxRx.Matches(plain))
            {
                if (list.Count >= 4) break;
                string res = m.Groups[3].Value.ToLowerInvariant();
                LGIcon icon; Color tint;
                if (res.StartsWith("гели") || res.StartsWith("helium")) { icon = LGIcon.Energy; tint = DS.Gold; }
                else if (res.StartsWith("титан") || res.StartsWith("titan")) { icon = LGIcon.Minerals; tint = DS.NeonCyan; }
                else if (res.StartsWith("сплав") || res.StartsWith("alloy")) { icon = LGIcon.Alloys; tint = new Color(0.95f, 0.62f, 0.36f); }
                else if (res.StartsWith("влия") || res.StartsWith("influ")) { icon = LGIcon.Influence; tint = new Color(1.00f, 0.48f, 0.58f); }
                else if (res.StartsWith("стаб") || res.StartsWith("stab")) { icon = LGIcon.Population; tint = new Color(0.94f, 0.56f, 0.86f); }
                else { icon = LGIcon.Research; tint = DS.Green; }
                string sg = m.Groups[1].Value;
                int sign = sg == "+" ? 1 : sg.Length > 0 ? -1 : 0;
                string val = (sign > 0 ? "+" : sign < 0 ? "−" : "") + m.Groups[2].Value + (m.Groups[4].Success ? Loc.T("/мес") : "");
                list.Add(new EventFx { Icon = icon, Tint = tint, Value = val, Sign = sign });
            }
            return list;
        }

        /// <summary>Ряд плашек «значок + число» внизу карточки: плюс — зелёным, минус — красным.</summary>
        private void BuildEffectChips(Transform card, List<EventFx> fx)
        {
            var row = new GameObject("Effects").AddComponent<RectTransform>();
            row.SetParent(card, false);
            row.anchorMin = Vector2.zero; row.anchorMax = new Vector2(1, 0); row.pivot = new Vector2(0, 0);
            row.offsetMin = new Vector2(18, 12); row.offsetMax = new Vector2(-16, 34);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            foreach (var f in fx)
            {
                Color vc = f.Sign > 0 ? new Color(0.49f, 0.94f, 0.66f) : f.Sign < 0 ? new Color(1f, 0.60f, 0.54f) : DS.TextPrimary;
                var chip = new GameObject("Fx").AddComponent<Image>();
                chip.transform.SetParent(row, false);
                chip.sprite = HudArt.Pill(false); chip.type = Image.Type.Sliced;
                chip.color = new Color(vc.r, vc.g, vc.b, 0.10f);
                chip.raycastTarget = false;
                var ch = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
                ch.padding = new RectOffset(7, 9, 2, 2); ch.spacing = 4f; ch.childAlignment = TextAnchor.MiddleLeft;
                ch.childControlWidth = ch.childControlHeight = true;
                ch.childForceExpandWidth = ch.childForceExpandHeight = false;
                var ic = LGIcons.Create(chip.transform, f.Icon, 13f, f.Tint);
                var le = ic.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = 13f;
                var t = CreateText(chip.transform, f.Value, 11, FontStyle.Bold, vc, TextAnchor.MiddleLeft);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                LG.Ignore(chip.gameObject);
            }
        }

        /// <summary>Подпись типа события на плашке: решение фракции, аномалия (с видом) или событие империи.</summary>
        private static string EventKindName(GameEventData ev)
        {
            if (ev.EventId != null && ev.EventId.StartsWith("decision_")) return Loc.T("РЕШЕНИЕ СОВЕТА");
            switch (ev.Anomaly)
            {
                case AnomalyType.AncientDerelict: return Loc.T("АНОМАЛИЯ · ДРЕВНИЙ КОРАБЛЬ");
                case AnomalyType.DistressSignal: return Loc.T("АНОМАЛИЯ · СИГНАЛ БЕДСТВИЯ");
                case AnomalyType.AlienFauna: return Loc.T("АНОМАЛИЯ · ЖИЗНЬ");
                case AnomalyType.PrecursorRelic: return Loc.T("АНОМАЛИЯ · РЕЛИКВИЯ");
                case AnomalyType.SpatialRift: return Loc.T("АНОМАЛИЯ · РАЗЛОМ");
                case AnomalyType.LeaderStory: return Loc.T("СОБЫТИЕ · ЛИДЕРЫ");
                case AnomalyType.ColonyIncident: return Loc.T("СОБЫТИЕ · КОЛОНИЯ");
                case AnomalyType.MilitaryIncident: return Loc.T("СОБЫТИЕ · ФЛОТ");
                default: return Loc.T("СОБЫТИЕ · ПОЛИТИКА");
            }
        }

        /// <summary>Первая строка текста без пустых строк в начале.</summary>
        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.TrimStart('\n', ' ');
            int n = s.IndexOf('\n');
            return n > 0 ? s.Substring(0, n) : s;
        }

        /// <summary>Вариант события требует учёного (лидера-исследователя).</summary>
        private static bool IsScientistRequirement(string req)
            => !string.IsNullOrEmpty(req) && (req.IndexOf("чён", System.StringComparison.OrdinalIgnoreCase) >= 0
                                               || req.IndexOf("cientist", System.StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Кто такие учёные и где их взять.</summary>
        private static string ScientistHint => Loc.T("<color=#8AA2A8><b>Учёный</b> — лидер-исследователь. Он служит на борту научного корабля (тогда события аномалий учитывают его черты) или в научном совете. Нанять учёного: окно «Империя» (I) → вкладка «Лидеры» → «НАНЯТЬ», затем «НАЗНАЧИТЬ» — на научный корабль или в совет.</color>");

        private static string Roman(int n) => n switch
        {
            1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", _ => n.ToString()
        };

        private void HandleAnomalyEvent(GameEventData ev)
        {
            if (ev == null) return;

            Time.timeScale = 0f;
            ShowModalDim();
            MapModeController.HideGlobal();
            ShowEventPopup(ev);
        }

        // ==================== ХЕЛПЕРЫ ====================

        private Text CreateText(Transform parent, string val, int size, FontStyle style, Color col, TextAnchor anchor)
        {
            var tObj = new GameObject("Txt");
            tObj.transform.SetParent(parent, false);
            var t = tObj.AddComponent<Text>();
            t.font = (style == FontStyle.Bold) ? GameFont.Bold : GameFont.Regular;
            t.text = val;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = col;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            return t;
        }

        private GameObject CreateButton(Transform parent, string name, Vector2 size, Color baseColor, Color hoverColor, System.Action onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = baseColor;
            img.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            // Стеклянная капсула; "цвет наведения" становится неоновой кромкой
            LG.Button(go, new Color(hoverColor.r, hoverColor.g, hoverColor.b, 0.62f));
            return go;
        }

        /// <summary>Круглая стеклянная кнопка закрытия с красной кромкой.</summary>
        private GameObject CreateCloseButton(Transform parent, float size, System.Action onClick)
        {
            var go = CreateButton(parent, "CloseBtn", new Vector2(size, size),
                new Color(0.16f, 0.20f, 0.24f, 1f), DS.Red, onClick);
            LG.Button(go, new Color(1f, 0.45f, 0.48f, 0.55f), size * 0.5f);
            LGIcons.Create(go.transform, LGIcon.Close, size * 0.4f, Color.white);
            return go;
        }
    }
}
