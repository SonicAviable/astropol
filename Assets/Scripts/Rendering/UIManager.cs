using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StellarisClone.Core;
using StellarisClone.Cam;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    public class UIManager : MonoBehaviour
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
            // Оттенки стекла (холодный дымчатый сине-бирюзовый)
            public static readonly Color BgDeep        = new Color(0.030f, 0.062f, 0.088f, 1.00f);
            public static readonly Color BgPanel       = new Color(0.040f, 0.080f, 0.110f, 1.00f);
            public static readonly Color BgHeader      = new Color(0.070f, 0.130f, 0.170f, 0.95f);
            public static readonly Color BgSlot        = new Color(0.055f, 0.110f, 0.150f, 0.90f);
            public static readonly Color BgRowEven     = new Color(0.050f, 0.100f, 0.135f, 0.75f);
            public static readonly Color BgRowOdd      = new Color(0.060f, 0.118f, 0.155f, 0.75f);
            public static readonly Color BgVisor       = new Color(0.018f, 0.045f, 0.065f, 0.95f);

            // Неон
            public static readonly Color NeonCyan      = new Color(0.30f, 0.95f, 0.86f, 1.00f);
            public static readonly Color NeonTeal      = new Color(0.36f, 0.88f, 0.82f, 0.55f);
            public static readonly Color Gold          = new Color(1.00f, 0.80f, 0.32f, 1.00f);
            public static readonly Color Green         = new Color(0.36f, 0.96f, 0.60f, 1.00f);
            public static readonly Color Red           = new Color(1.00f, 0.36f, 0.38f, 1.00f);

            // Текст
            public static readonly Color TextPrimary   = new Color(0.94f, 0.98f, 1.00f, 1.00f);
            public static readonly Color TextMuted     = new Color(0.62f, 0.74f, 0.80f, 1.00f);
            public static readonly Color TextShadow    = new Color(0.000f, 0.020f, 0.035f, 0.55f);

            // Оттенки стеклянных кнопок
            public static readonly Color BtnPrimary    = new Color(0.10f, 0.40f, 0.48f, 1.00f);
            public static readonly Color BtnPrimaryHi  = new Color(0.30f, 0.80f, 0.86f, 1.00f);
            public static readonly Color BtnSuccess    = new Color(0.12f, 0.50f, 0.34f, 1.00f);
            public static readonly Color BtnDanger     = new Color(0.58f, 0.17f, 0.21f, 1.00f);
            public static readonly Color BtnNeutral    = new Color(0.09f, 0.15f, 0.19f, 1.00f);
            public static readonly Color BtnDisabled   = new Color(0.06f, 0.10f, 0.13f, 0.60f);
        }

        private Canvas _canvas;
        private Canvas _modalCanvas;
        private GameObject _modalDim;
        private Font _uiFont;
        private GalaxyGenerator _generator;

        private GameObject _factionSelectionModal;
        private GameObject _advisorIntroModal;
        private Text _advisorText;
        private FactionInfo _selectedFaction;

        public FactionInfo SelectedFaction => _selectedFaction;

        private Text _dateText, _energyVal, _mineralsVal, _alloysVal, _influenceVal, _researchVal;
        private TooltipTrigger _energyTip, _mineralsTip, _alloysTip, _influenceTip, _researchTip;


        private GameObject _eventPopupModal;
        private Transform _eventOptionsHolder;
        private Text _eventTitleText;
        private Text _eventDescText;
        private Image _eventAccentBar;

        private RectTransform _inspectorRect;
        private CanvasGroup _inspectorGroup;
        private Text _inspTitle, _inspStatus, _inspActionBtnText;
        private Text _inspBodyText;
        private GameObject _districtRowRoot;
        private Button _inspActionBtn;
        private Image _inspActionBtnBg;
        private Button _inspExitBtn;
        private Button _inspShipyardBtn;
        private float _inspTargetAlpha;
        private readonly StringBuilder _bodySb = new StringBuilder();

        private GameObject _shipyardPanel;
        private CanvasGroup _shipyardGroup;
        private Button _shipyardRetrofitBtn;
        private Text _shipyardRetrofitTxt;

        private GameObject _rewardPopUp;
        private Text _rewardText;
        private CanvasGroup _rewardGroup;

        private StarSystem _activeSystem;
        private PlanetData _activePlanet;
        private bool _isInSystemMode;

        private const float TopBarHeight = 44f;
        private const float TopBarMargin = 10f;
        private const float StarbaseAlloysCost = 50f;
        private const float StarbaseInfluenceCost = 25f;

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
            BuildTopBar();
            BuildStellarisLeftInspector();
            BuildShipyardModal();
            BuildEmpireOverviewModal();
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
            if (_factionSelectionModal != null) _factionSelectionModal.SetActive(false);
            if (_advisorIntroModal != null) _advisorIntroModal.SetActive(false);
            HideModalDim();
            Time.timeScale = 1f;
            SpawnInGameHUD();
            TimeManager.Instance?.SetSpeed(0);
            RefreshResourceBar();
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
            SystemViewManager.OnViewModeChanged += OnViewModeChanged;

            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed += (_, _, _) => RefreshResourceBar();

            if (EconomyManager.Instance != null)
                EconomyManager.Instance.OnResourcesChanged += RefreshResourceBar;

            AnomalyEventSystem.OnEventTriggered += HandleAnomalyEvent;
            FleetManager.OnFleetSelected += HandleFleetSelectedForRetrofit;

            BindAuxiliaryUi();
            RefreshResourceBar();
        }

        private void OnDestroy()
        {
            StarSystemSelector.OnSystemSelected -= OnSystemSelected;
            SystemViewManager.OnViewModeChanged -= OnViewModeChanged;

            if (EconomyManager.Instance != null)
                EconomyManager.Instance.OnResourcesChanged -= RefreshResourceBar;

            AnomalyEventSystem.OnEventTriggered -= HandleAnomalyEvent;
            FleetManager.OnFleetSelected -= HandleFleetSelectedForRetrofit;
        }

        private void EnsureEventSystemExists()
        {
            var es = FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                go.AddComponent<EventSystem>();
                go.AddComponent<StandaloneInputModule>();
            }
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
        public bool IsShipyardOpen => _shipyardPanel != null && LG.IsVisible(_shipyardPanel);
        public void CloseShipyard() { if (IsShipyardOpen) CloseModal(_shipyardPanel); }

        /// <summary>Открыто окно, которое нельзя закрыть по Esc (выбор фракции, советник, событие).</summary>
        public bool IsBlockingFlowOpen =>
            (_factionSelectionModal != null && LG.IsVisible(_factionSelectionModal)) ||
            (_advisorIntroModal != null && LG.IsVisible(_advisorIntroModal)) ||
            (_eventPopupModal != null && LG.IsVisible(_eventPopupModal));

        public void ShowModalDimPublic() => ShowModalDim();
        public void HideModalDimPublic() => HideModalDim();
        private void ShowModalDim() { if (_modalDim != null) LG.Show(_modalDim); }
        private void HideModalDim() { if (_modalDim != null) LG.Hide(_modalDim); }

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

        private void HandleFleetSelectedForRetrofit(FleetView fleet) => RefreshRetrofitButton();

        private void RefreshRetrofitButton()
        {
            if (_shipyardRetrofitBtn == null || _shipyardRetrofitTxt == null) return;
            var fleet = FleetManager.Instance?.SelectedFleet;
            var dm = ShipDesignManager.Instance;
            if (fleet?.Data == null || fleet.Data.OwnerId != 0 || fleet.Data.Type != FleetType.Military || dm == null)
            {
                _shipyardRetrofitBtn.interactable = false;
                _shipyardRetrofitTxt.text = "⟳  ВЫБЕРИТЕ ЭСКАДРУ СТАРОГО ОБРАЗЦА";
                return;
            }

            float cost = dm.RetrofitCost(fleet.Data);
            bool can = dm.CanRetrofit(fleet.Data);
            if (cost <= 0.01f)
            {
                _shipyardRetrofitBtn.interactable = false;
                _shipyardRetrofitTxt.text = "✓  ФЛОТ СООТВЕТСТВУЕТ АКТУАЛЬНОМУ ПРОЕКТУ";
                return;
            }

            _shipyardRetrofitBtn.interactable = can;
            _shipyardRetrofitTxt.text = can
                ? $"⟳  МОДЕРНИЗИРОВАТЬ ФЛОТ (RETROFIT)  ·  {cost:0} ⬢"
                : $"✕  МОДЕРНИЗАЦИЯ  ·  НЕ ХВАТАЕТ {cost:0} ⬢";
        }

        private void OnRetrofitClicked()
        {
            if (FleetManager.Instance != null && FleetManager.Instance.TryRetrofitSelectedFleet())
            {
                RefreshResourceBar();
                RefreshRetrofitButton();
            }
        }

        // ==================== TOPBAR ====================

        private void BuildTopBar()
        {
            // Парящая стеклянная капсула с отступом от краёв экрана
            var bar = new GameObject("TopBar");
            bar.transform.SetParent(_canvas.transform, false);
            var rt = bar.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(-TopBarMargin * 2f, TopBarHeight);
            rt.anchoredPosition = new Vector2(0, -TopBarMargin);

            var bg = bar.AddComponent<Image>();
            bg.color = DS.BgDeep;
            bg.raycastTarget = false;
            LG.Glass(bar, TopBarHeight * 0.5f).SetRim(new Color(0.55f, 0.95f, 0.92f, 0.32f));
            var barMotion = LG.Motion(bar, LGAppear.Kind.SlideUp);
            barMotion.distance = 22f;
            barMotion.inDuration = 0.55f;

            var overviewBtn = CreateButton(bar.transform, "EmpireOverviewBtn", new Vector2(150, 32),
                new Color(0.12f, 0.40f, 0.48f, 1f), DS.NeonCyan, OpenEmpireOverviewModal);
            var ovRt = overviewBtn.GetComponent<RectTransform>();
            ovRt.anchorMin = ovRt.anchorMax = new Vector2(0, 0.5f);
            ovRt.pivot = new Vector2(0, 0.5f);
            ovRt.anchoredPosition = new Vector2(8, 0);
            LGIcons.IconLabel(overviewBtn.transform, LGIcon.Globe, "ОБЗОР ИМПЕРИИ", 10, DS.NeonCyan, Color.white, 15f);

            // Дата — в стеклянной капсуле
            var dateChip = new GameObject("DateChip");
            dateChip.transform.SetParent(bar.transform, false);
            var dcRt = dateChip.AddComponent<RectTransform>();
            dcRt.anchorMin = dcRt.anchorMax = new Vector2(0, 0.5f);
            dcRt.pivot = new Vector2(0, 0.5f);
            dcRt.sizeDelta = new Vector2(96, 30);
            dcRt.anchoredPosition = new Vector2(166, 0);
            var dcImg = dateChip.AddComponent<Image>();
            dcImg.color = DS.BgVisor;
            dcImg.raycastTarget = false;
            LG.Chip(dateChip, LG.Palette.GlassRimSoft);

            _dateText = LGIcons.IconLabel(dateChip.transform, LGIcon.Calendar, "01.01.2200", 13, DS.TextMuted, DS.TextPrimary, 13f);

            BuildSpeedControl(bar.transform, 270f);

            float startX = 400f;
            _energyVal    = CreateResourceBadge(bar.transform, LGIcon.Energy, "ГЕЛИЙ-3", DS.Gold,     ref startX, out _energyTip);
            _mineralsVal  = CreateResourceBadge(bar.transform, LGIcon.Minerals, "ТИТАН", DS.NeonCyan, ref startX, out _mineralsTip);
            _alloysVal    = CreateResourceBadge(bar.transform, LGIcon.Alloys, "СПЛАВЫ", new Color(0.95f, 0.62f, 0.36f), ref startX, out _alloysTip);
            _influenceVal = CreateResourceBadge(bar.transform, LGIcon.Influence, "ВЛИЯНИЕ", new Color(1.00f, 0.48f, 0.58f), ref startX, out _influenceTip);
            _researchVal  = CreateResourceBadge(bar.transform, LGIcon.Research, "НАУКА", DS.Green,    ref startX, out _researchTip);

            var techBtn = CreateButton(bar.transform, "TechBtn", new Vector2(134, 32),
                DS.BtnPrimary, DS.NeonCyan, OpenTechModal);
            var tbRt = techBtn.GetComponent<RectTransform>();
            tbRt.anchorMin = tbRt.anchorMax = new Vector2(0, 0.5f);
            tbRt.pivot = new Vector2(0, 0.5f);
            tbRt.anchoredPosition = new Vector2(startX + 8, 0);
            LGIcons.IconLabel(techBtn.transform, LGIcon.Research, "ИССЛЕДОВАНИЯ", 10, DS.NeonCyan, Color.white, 15f);

            var designBtn = CreateButton(bar.transform, "DesignBtn", new Vector2(146, 32),
                DS.BtnPrimary, DS.NeonCyan, () => ShipDesignerModal.Instance?.Open());
            var dbRt = designBtn.GetComponent<RectTransform>();
            dbRt.anchorMin = dbRt.anchorMax = new Vector2(0, 0.5f);
            dbRt.pivot = new Vector2(0, 0.5f);
            dbRt.anchoredPosition = new Vector2(startX + 150, 0);
            LGIcons.IconLabel(designBtn.transform, LGIcon.Gear, "КОНСТРУКТОР", 10, DS.NeonCyan, Color.white, 15f);

            var diploBtn = CreateButton(bar.transform, "DiploBtn", new Vector2(146, 32),
                new Color(0.42f, 0.32f, 0.10f), DS.Gold, () => DiplomacyModal.Instance?.Open());
            var dpRt = diploBtn.GetComponent<RectTransform>();
            dpRt.anchorMin = dpRt.anchorMax = new Vector2(0, 0.5f);
            dpRt.pivot = new Vector2(0, 0.5f);
            dpRt.anchoredPosition = new Vector2(startX + 304, 0);
            LGIcons.IconLabel(diploBtn.transform, LGIcon.Diplomacy, "ДИПЛОМАТИЯ", 10, DS.Gold, Color.white, 15f);
        }

        private void SetSpeed(int s) { if (TimeManager.Instance != null) TimeManager.Instance.SetSpeed(s); }

        private Text CreateResourceBadge(Transform parent, LGIcon icon, string label, Color accentCol, ref float xPos, out TooltipTrigger tip)
        {
            var badge = new GameObject($"Badge_{label}");
            badge.transform.SetParent(parent, false);
            var rt = badge.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(xPos, 0);
            rt.sizeDelta = new Vector2(120, 30);

            var img = badge.AddComponent<Image>();
            img.color = DS.BgSlot;
            img.raycastTarget = true;

            // Капсула с тонкой неоновой кромкой цвета ресурса
            LG.Chip(badge, new Color(accentCol.r, accentCol.g, accentCol.b, 0.50f));
            badge.AddComponent<LGInteractive>().hoverScale = 1.04f;

            tip = badge.AddComponent<TooltipTrigger>();

            var ic = LGIcons.Create(badge.transform, icon, 16, accentCol);
            ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0, 0.5f);
            ic.rectTransform.pivot = new Vector2(0, 0.5f);
            ic.rectTransform.anchoredPosition = new Vector2(11, 0);

            var val = CreateText(badge.transform, "0", 11, FontStyle.Bold, DS.TextPrimary, TextAnchor.MiddleLeft);
            val.rectTransform.anchorMin = Vector2.zero;
            val.rectTransform.anchorMax = Vector2.one;
            val.rectTransform.offsetMin = new Vector2(33, 0);
            val.rectTransform.offsetMax = new Vector2(-8, 0);

            xPos += 126f;
            return val;
        }

        // ---------- Скорость времени: сегментный контрол с "жидким" ползунком ----------

        private RectTransform _speedThumb;
        private readonly Graphic[] _speedLabels = new Graphic[4];
        private float _speedThumbX, _speedThumbV;
        private bool _speedLabelsReady;
        private const float SpeedSegW = 28f;
        private const float SpeedSegPad = 3f;

        private void BuildSpeedControl(Transform parent, float xPos)
        {
            var track = new GameObject("SpeedControl");
            track.transform.SetParent(parent, false);
            var tRt = track.AddComponent<RectTransform>();
            tRt.anchorMin = tRt.anchorMax = new Vector2(0, 0.5f);
            tRt.pivot = new Vector2(0, 0.5f);
            tRt.sizeDelta = new Vector2(SpeedSegW * 4f + SpeedSegPad * 2f, 30f);
            tRt.anchoredPosition = new Vector2(xPos, 0);
            var tImg = track.AddComponent<Image>();
            tImg.color = DS.BgVisor;
            tImg.raycastTarget = false;
            LG.Chip(track, LG.Palette.GlassRimSoft);

            var thumb = new GameObject("Thumb");
            thumb.transform.SetParent(track.transform, false);
            _speedThumb = thumb.AddComponent<RectTransform>();
            _speedThumb.anchorMin = _speedThumb.anchorMax = new Vector2(0, 0.5f);
            _speedThumb.pivot = new Vector2(0, 0.5f);
            _speedThumb.sizeDelta = new Vector2(SpeedSegW, 24f);
            _speedThumbX = SpeedSegPad + SpeedSegW;
            _speedThumb.anchoredPosition = new Vector2(_speedThumbX, 0);
            var thImg = thumb.AddComponent<Image>();
            thImg.color = DS.BtnPrimary;
            thImg.raycastTarget = false;
            var thFx = LG.Chip(thumb, new Color(DS.NeonCyan.r, DS.NeonCyan.g, DS.NeonCyan.b, 0.85f));
            thFx.FillMultiplier = 1.9f;
            thFx.GlowMultiplier = 2.5f;

            LGIcon[] icons = { LGIcon.Pause, LGIcon.Play, LGIcon.Fast, LGIcon.Fastest };
            string[] tips =
            {
                "<b>Пауза</b>\nКлавиша: ПРОБЕЛ",
                "<b>Нормальная скорость</b>\nКлавиша: 1",
                "<b>Быстро</b>\nКлавиша: 2",
                "<b>Очень быстро</b>\nКлавиша: 3"
            };
            int[] speeds = { 0, 1, 2, 3 };
            for (int i = 0; i < 4; i++)
            {
                int captured = speeds[i];
                var seg = new GameObject($"Speed_{i}");
                seg.transform.SetParent(track.transform, false);
                var sRt = seg.AddComponent<RectTransform>();
                sRt.anchorMin = sRt.anchorMax = new Vector2(0, 0.5f);
                sRt.pivot = new Vector2(0, 0.5f);
                sRt.sizeDelta = new Vector2(SpeedSegW, 26f);
                sRt.anchoredPosition = new Vector2(SpeedSegPad + i * SpeedSegW, 0);

                var hit = seg.AddComponent<Image>();
                hit.color = new Color(0, 0, 0, 0);   // невидимая зона клика
                var b = seg.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => SetSpeed(captured));

                _speedLabels[i] = LGIcons.Create(seg.transform, icons[i], i == 3 ? 14f : 12f, DS.TextMuted);
                TooltipHelper.Attach(seg, tips[i]);
            }
        }

        private void UpdateSpeedControl()
        {
            if (_speedThumb == null) return;
            int speed = TimeManager.Instance != null ? TimeManager.Instance.CurrentSpeed : 1;
            int idx = Mathf.Clamp(speed >= 3 ? 3 : speed, 0, 3);

            float target = SpeedSegPad + idx * SpeedSegW;
            bool settled = Mathf.Abs(_speedThumbX - target) < 0.01f && Mathf.Abs(_speedThumbV) < 0.01f;
            if (settled && _speedLabelsReady) return; // в покое — не трогаем канвас
            _speedLabelsReady = true;

            _speedThumbX = LGEase.Spring(_speedThumbX, target, ref _speedThumbV, 4.2f, 0.62f, Time.unscaledDeltaTime);
            _speedThumb.anchoredPosition = new Vector2(_speedThumbX, 0);

            // "Жидкая" капля: растягивается по ходу движения
            float stretch = Mathf.Clamp(Mathf.Abs(_speedThumbV) * 0.012f, 0f, 0.35f);
            _speedThumb.localScale = new Vector3(1f + stretch, 1f - stretch * 0.35f, 1f);

            for (int i = 0; i < 4; i++)
            {
                if (_speedLabels[i] == null) continue;
                float closeness = 1f - Mathf.Clamp01(Mathf.Abs(_speedThumbX - (SpeedSegPad + i * SpeedSegW)) / SpeedSegW);
                _speedLabels[i].color = Color.Lerp(DS.TextMuted, Color.white, closeness);
            }
        }
                // ==================== ИНСПЕКТОР ====================

        private void BuildStellarisLeftInspector()
        {
            var insp = new GameObject("StarbaseInspector");
            insp.transform.SetParent(_canvas.transform, false);

            _inspectorRect = insp.AddComponent<RectTransform>();
            _inspectorRect.anchorMin = new Vector2(0, 0.5f);
            _inspectorRect.anchorMax = new Vector2(0, 0.5f);
            _inspectorRect.pivot = new Vector2(0, 0.5f);
            _inspectorRect.anchoredPosition = new Vector2(16, -15);
            _inspectorRect.sizeDelta = new Vector2(430, 590);

            _inspectorGroup = insp.AddComponent<CanvasGroup>();
            _inspectorGroup.alpha = 0f;
            _inspectorGroup.blocksRaycasts = false;

            var inspBg = insp.AddComponent<Image>();
            inspBg.color = DS.BgDeep;
            inspBg.raycastTarget = false;
            LG.Glass(insp, 22f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.34f));

            var header = new GameObject("Header");
            header.transform.SetParent(insp.transform, false);
            var hRt = header.AddComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0, 1);
            hRt.anchorMax = new Vector2(1, 1);
            hRt.pivot = new Vector2(0.5f, 1);
            hRt.sizeDelta = new Vector2(0, 52);
            var hBg = header.AddComponent<Image>();
            hBg.color = DS.BgHeader;
            hBg.raycastTarget = false;
            LG.Header(header);

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

            _inspTitle = CreateText(header.transform, "SOL STATION", 15, FontStyle.Bold, DS.TextPrimary, TextAnchor.MiddleLeft);
            _inspTitle.rectTransform.anchorMin = new Vector2(0, 0.5f);
            _inspTitle.rectTransform.anchorMax = new Vector2(1, 1);
            _inspTitle.rectTransform.offsetMin = new Vector2(16, 0);
            _inspTitle.rectTransform.offsetMax = new Vector2(-40, 0);

            _inspStatus = CreateText(header.transform, "Starbase  ·  Outpost", 10, FontStyle.Bold, DS.NeonCyan, TextAnchor.MiddleLeft);
            _inspStatus.rectTransform.anchorMin = new Vector2(0, 0);
            _inspStatus.rectTransform.anchorMax = new Vector2(1, 0.5f);
            _inspStatus.rectTransform.offsetMin = new Vector2(16, 0);
            _inspStatus.rectTransform.offsetMax = new Vector2(-40, 0);

            var closeBtn = CreateCloseButton(header.transform, 26f, () =>
                {
                    _inspTargetAlpha = 0f;
                    _inspectorGroup.blocksRaycasts = false;
                });
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
            bbRt.offsetMax = new Vector2(-13, -62);

            var bbBg = bodyBox.AddComponent<Image>();
            bbBg.color = DS.BgSlot;
            bbBg.raycastTarget = false;
            LG.Platter(bodyBox, 16f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.22f));

            _inspBodyText = CreateText(bodyBox.transform, "", 11, FontStyle.Normal, DS.TextPrimary, TextAnchor.UpperLeft);
            _inspBodyText.rectTransform.anchorMin = Vector2.zero;
            _inspBodyText.rectTransform.anchorMax = Vector2.one;
            _inspBodyText.rectTransform.offsetMin = new Vector2(12, 12);
            _inspBodyText.rectTransform.offsetMax = new Vector2(-12, -12);
            _inspBodyText.lineSpacing = 1.35f;
            _inspBodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _inspBodyText.verticalOverflow = VerticalWrapMode.Truncate;
            _inspBodyText.supportRichText = true;

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
            _inspActionBtn = actionBtn.GetComponent<Button>();
            _inspActionBtnText = CreateText(actionBtn.transform, "ДЕЙСТВИЕ", 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            _inspActionBtnText.rectTransform.sizeDelta = abRt.sizeDelta;

            var shipyardBtn = CreateButton(insp.transform, "ShipyardBtn", new Vector2(395, 28),
                new Color(0.10f, 0.36f, 0.42f, 1f), DS.NeonCyan, OpenShipyardModal);
            var sbRt = shipyardBtn.GetComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(0.5f, 0);
            sbRt.anchorMax = new Vector2(0.5f, 0);
            sbRt.pivot = new Vector2(0.5f, 0);
            sbRt.anchoredPosition = new Vector2(0, 48);
            _inspShipyardBtn = shipyardBtn.GetComponent<Button>();
            LGIcons.IconLabel(shipyardBtn.transform, LGIcon.Shipyard, "ОРБИТАЛЬНАЯ ВЕРФЬ", 10, DS.NeonCyan, Color.white, 14f);

            var exitBtn = CreateButton(insp.transform, "ExitSystemBtn", new Vector2(395, 28),
                DS.BtnDanger, DS.Red, () => SystemViewManager.Instance?.ExitToGalaxyView());
            var ebRt = exitBtn.GetComponent<RectTransform>();
            ebRt.anchorMin = new Vector2(0.5f, 0);
            ebRt.anchorMax = new Vector2(0.5f, 0);
            ebRt.pivot = new Vector2(0.5f, 0);
            ebRt.anchoredPosition = new Vector2(0, 48);
            _inspExitBtn = exitBtn.GetComponent<Button>();
            LGIcons.IconLabel(exitBtn.transform, LGIcon.Back, "ВЕРНУТЬСЯ В ГАЛАКТИКУ", 10, Color.white, Color.white, 13f);
            exitBtn.SetActive(false);
        }

        private RectTransform _inspBodyRect, _inspDistrictRect;

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
            Stack(_inspShipyardBtn, 28f);
            Stack(_inspExitBtn, 28f);

            bool districts = _districtRowRoot != null && _districtRowRoot.transform.childCount > 0;
            if (districts)
            {
                _inspDistrictRect.offsetMin = new Vector2(13, y + 2f);
                _inspDistrictRect.offsetMax = new Vector2(-13, y + 44f);
                y += 50f;
            }
            _inspBodyRect.offsetMin = new Vector2(13, y + 4f);
        }

        private void ClearInspectorBody()
        {
            _bodySb.Length = 0;
            if (_inspBodyText != null) _inspBodyText.text = "";

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

        private void AddBodyHeader(string text)
        {
            if (_bodySb.Length > 0) _bodySb.Append('\n');
            _bodySb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(DS.Gold)}><b>◆  {text}</b></color>\n");
        }

        private void AddBodyLine(string text)
        {
            _bodySb.Append(text).Append('\n');
        }

        private void CommitBody()
        {
            if (_inspBodyText != null) _inspBodyText.text = _bodySb.ToString();
        }

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
            if (_inspBodyText == null || _inspTitle == null || _inspStatus == null) return;

            _activeSystem = system;
            _activePlanet = null;

            _inspTitle.text = system.Name.ToUpper();

            string owner = system.OwnerId == 0
                ? $"<color=#33E6CC>◆  Под контролем {_selectedFaction?.Name ?? "Империи"}</color>"
                : system.OwnerId == AIEmpireManager.AIOwnerId
                    ? $"<color=#FF5555>◆  Территория: {AIEmpireManager.Instance?.AIName ?? "соперник"}</color>"
                    : "<color=#8AA2A8>◆  Нейтральный фронтир</color>";
            _inspStatus.text = owner;

            PulseInspectorContent();
            ClearInspectorBody();

            AddBodyHeader("СТАТУС СИСТЕМЫ");
            AddBodyLine($"<color=#8AA2A8>Класс звезды:</color> {system.SpectralClass}");
            AddBodyLine($"<color=#8AA2A8>Орбитальных тел:</color> {system.Planets.Count}");
            AddBodyLine($"<color=#8AA2A8>Гиперкоридоров:</color> {system.ConnectedSystemIds.Count}");

            AddBodyHeader("РЕСУРСЫ");
            if (system.IsSurveyed)
            {
                system.RecalculateHarvest();
                AddBodyLine($"<color=#8AA2A8>Добыча:</color> <color=#4DF08C>◆ {system.HarvestedMinerals}  ⚡ {system.HarvestedEnergy}</color>");
                AddBodyLine($"<color=#8AA2A8>Потенциал:</color> <color=#33E6CC>◆ {system.PotentialMinerals}  ⚡ {system.PotentialEnergy}</color>");
            }
            else
            {
                AddBodyLine("<color=#FFAA88>⚠  Требуется разведка научным кораблём</color>");
            }

            var sb = CombatManager.Instance?.GetStarbase(system.Id);
            if (sb != null)
            {
                AddBodyHeader("ЗВЁЗДНАЯ БАЗА");
                string sbState = sb.Disabled
                    ? $"<color=#FF6666>выведена из строя</color> · ремонт {sb.Hull / Mathf.Max(1f, sb.MaxHull) * 100f:0}% / 50%"
                    : $"<color=#4DF08C>в строю</color> · корпус {sb.Hull:0}/{sb.MaxHull:0} · броня {sb.Armor:0}/{sb.MaxArmor:0} · щиты {sb.Shields:0}/{sb.MaxShields:0}";
                AddBodyLine(sbState);
                AddBodyLine($"<color=#8AA2A8>Огневая мощь:</color> {sb.Damage:0} урона · мощь {CombatManager.Instance.StarbasePower(system.Id):N0}");
            }

            var siege = SiegeManager.Instance?.GetSiege(system.Id);
            if (siege != null && system.OwnerId >= 0)
            {
                float need = SiegeManager.RequiredDays(system);
                string who = siege.Attacker == 0 ? "Ваша осада" : "Враг осаждает систему";
                string state = siege.BaseHolding ? "приостановлена: звёздная база в строю"
                             : siege.Contested ? "приостановлена: на орбите флот защитника"
                             : siege.Active ? $"{siege.Progress:0} / {need:0} дн."
                             : "осаждающие ушли, прогресс спадает";
                AddBodyHeader("ОСАДА");
                AddBodyLine($"<color={(siege.Attacker == 0 ? "#4DF08C" : "#FF6666")}>{who}</color>: {state}");
            }

            AddBodyHeader("УПРАВЛЕНИЕ");
            if (!_isInSystemMode && system.OwnerId < 0)
            {
                if (!system.IsSurveyed)
                    AddBodyLine("Отправьте научный корабль для изучения системы.");
                else
                    AddBodyLine("Заложите форпост, чтобы присоединить систему.");
            }
            else if (system.OwnerId == 0)
            {
                AddBodyLine("Система под вашим контролем.");
                var fm = FleetManager.Instance;
                if (fm != null) AddBodyLine($"<color=#8AA2A8>Ремонт кораблей здесь:</color> {fm.RepairRateAt(system.Id, 0) * 100f:0}% в день");
            }
            else
            {
                var ai = AIEmpireManager.Instance;
                AddBodyLine(ai != null && ai.AtWar
                    ? $"Чтобы захватить систему, держите здесь военный флот без защитников ({SiegeManager.RequiredDays(system):0} дн. осады)."
                    : "Чужая система. Захват возможен только во время войны — осадой.");
            }

            CommitBody();

            _inspShipyardBtn.gameObject.SetActive(system.OwnerId == 0 && !_isInSystemMode);

            if (!_isInSystemMode && system.OwnerId < 0)
            {
                _inspActionBtn.gameObject.SetActive(true);

                if (!system.IsSurveyed)
                {
                    _inspActionBtn.interactable = true;
                    _inspActionBtnBg.color = DS.BtnSuccess;
                    _inspActionBtnText.text = "▶  НАПРАВИТЬ НАУЧНЫЙ КОРАБЛЬ";
                }
                else
                {
                    var eco = EconomyManager.Instance;
                    float finalInfluenceCost = StarbaseInfluenceCost
                        - (TechnologyManager.Instance != null ? TechnologyManager.Instance.StarbaseCostDiscount : 0f);
                    bool canAfford = eco != null && eco.Alloys >= StarbaseAlloysCost && eco.Influence >= finalInfluenceCost;

                    _inspActionBtn.interactable = canAfford;
                    _inspActionBtnBg.color = canAfford ? DS.BtnPrimary : new Color(0.15f, 0.20f, 0.25f);
                    _inspActionBtnText.text = canAfford
                        ? $"▶  ФОРПОСТ ({(int)StarbaseAlloysCost} ⬢ · {(int)finalInfluenceCost} ★)"
                        : "✕  НЕДОСТАТОЧНО РЕСУРСОВ";
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
            if (_inspBodyText == null || _inspTitle == null || _inspStatus == null) return;

            _activePlanet = planet;
            if (parentSystem != null) _activeSystem = parentSystem;

            _inspTitle.text = planet.Name.ToUpper();
            _inspStatus.text = "<color=#E5B842>◆  Планетарный объект</color>";

            bool isSurveyed = _activeSystem != null && _activeSystem.IsSurveyed;

            PulseInspectorContent();
            ClearInspectorBody();

            if (!isSurveyed)
            {
                AddBodyHeader("КОЛОНИЯ");
                AddBodyLine("Информация о населении и производстве появится после разведки системы.");
            }
            else if (planet.Type == PlanetType.GasGiant || planet.Type == PlanetType.Molten)
            {
                AddBodyHeader("КОЛОНИЗАЦИЯ НЕВОЗМОЖНА");
                AddBodyLine($"<color=#E5B842>Планета типа «{planet.Type}» непригодна для проживания.</color>");
                AddBodyLine("Можно построить только добывающий комплекс.");
            }
            else
            {
                AddBodyHeader("ОБЗОР КОЛОНИИ");
                AddBodyLine($"<color=#8AA2A8>Класс:</color> {planet.Type}");
                AddBodyLine($"<color=#8AA2A8>Районы:</color> {planet.BuiltDistricts} / {planet.MaxDistricts}");
                AddBodyLine($"<color=#8AA2A8>Залежи:</color> ◆ {planet.MineralDeposit}  ⚡ {planet.EnergyDeposit}");

                AddBodyHeader("НАСЕЛЕНИЕ");
                int pop = planet.Population;
                int housing = planet.HousingCapacity;
                int employed = planet.EmployedPops;
                int idle = planet.IdlePops;

                string popColor = housing >= pop ? "#4DF08C" : "#FF6666";
                AddBodyLine($"<color=#8AA2A8>Население:</color> <color={popColor}>{pop}</color> / {housing} жилья");
                AddBodyLine($"<color=#8AA2A8>Рабочие:</color> {employed}   " +
                            $"<color=#8AA2A8>Безработные:</color> {StatFormat.Colored(idle, "", "0;0;0")}");

                float growthPct = Mathf.Clamp(planet.PopGrowthProgress, 0f, 100f);
                float speedPct = planet.PopGrowthBaseSpeedPctPerMonth;
                AddBodyLine($"<color=#8AA2A8>Рост:</color> {StatFormat.Colored(speedPct, "%/мес")}   " +
                            $"Прогресс: {growthPct:F1}%");

                AddBodyHeader("ПРОИЗВОДСТВО / МЕСЯЦ");
                AddBodyLine($"<color=#33E6CC>◆ Титан:</color> {StatFormat.Positive("+" + planet.ProducedMineralsPerMonth)}");
                AddBodyLine($"<color=#E5B842>⚡ Гелий-3:</color> {StatFormat.Positive("+" + planet.ProducedEnergyPerMonth)}");
                AddBodyLine($"<color=#20EBB0>⬢ Сплавы:</color> {StatFormat.Positive("+" + planet.ProducedAlloysPerMonth)}");
            }

            CommitBody();

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
                    _inspActionBtnText.text = "✕  ТРЕБУЕТСЯ РАЗВЕДКА СИСТЕМЫ";
                }
                else if (!planet.IsPlayerOwned)
                {
                    _inspActionBtn.interactable = false;
                    _inspActionBtnBg.color = new Color(0.15f, 0.20f, 0.25f);
                    _inspActionBtnText.text = "✕  СИСТЕМА НЕ ПРИНАДЛЕЖИТ ВАМ";
                }
                else
                {
                    var eco = EconomyManager.Instance;
                    bool canAfford = eco != null && eco.Minerals >= 50f;
                    _inspActionBtn.interactable = canAfford;
                    _inspActionBtnBg.color = canAfford ? DS.BtnSuccess : new Color(0.15f, 0.20f, 0.25f);
                    _inspActionBtnText.text = canAfford
                        ? "▶  ДОБЫВАЮЩАЯ СТАНЦИЯ (50 ◆)"
                        : "✕  НЕДОСТАТОЧНО ТИТАНА (50 ◆)";
                }
            }
            else
            {
                _inspActionBtn.interactable = false;
                _inspActionBtnBg.color = new Color(0.10f, 0.25f, 0.18f);
                _inspActionBtnText.text = "✓  КОМПЛЕКС ФУНКЦИОНИРУЕТ";
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
                        _inspActionBtnText.text = "◎  СУДНО В ПУТИ";
                    }
                }
                else
                {
                    if (FleetManager.Instance != null && FleetManager.Instance.OrderBuildStarbase(_activeSystem.Id))
                    {
                        _inspActionBtn.interactable = false;
                        _inspActionBtnBg.color = new Color(0.10f, 0.25f, 0.20f);
                        _inspActionBtnText.text = "⚙  СТРОИТЕЛЬСТВО НАЧАТО";
                        RefreshResourceBar();
                    }
                }
            }
        }

        private float _inspShow, _inspShowV;
        private float _inspBodyFade = 1f;
        private static readonly Vector2 InspectorRestPos = new Vector2(16, -15);

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            bool inspSettled = Mathf.Abs(_inspShow - _inspTargetAlpha) < 0.001f && Mathf.Abs(_inspShowV) < 0.001f;
            if (_inspectorGroup != null && !inspSettled)
            {
                // Инспектор выезжает слева на пружине и "проявляется"
                _inspShow = LGEase.Spring(_inspShow, _inspTargetAlpha, ref _inspShowV, 2.4f, 0.78f, dt);
                float k = Mathf.Clamp01(_inspShow);
                _inspectorGroup.alpha = LGEase.OutCubic(k);
                _inspectorRect.anchoredPosition = InspectorRestPos + new Vector2(-(1f - k) * 40f, 0f);
                _inspectorRect.localScale = Vector3.one * Mathf.LerpUnclamped(0.965f, 1f, _inspShow);
            }

            // Смена содержимого — мягкое "проявление" текста вместо мгновенной подмены
            if (_inspBodyText != null && _inspBodyFade < 1f)
            {
                _inspBodyFade = Mathf.MoveTowards(_inspBodyFade, 1f, dt * 4.5f);
                var c = _inspBodyText.color;
                c.a = LGEase.OutCubic(_inspBodyFade);
                _inspBodyText.color = c;
            }

            UpdateSpeedControl();

            if (TimeManager.Instance != null && _dateText != null)
                _dateText.text = TimeManager.Instance.GetFormattedDate();
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
        _energyVal.text = $"{(int)eco.EnergyCredits}  {StatFormat.Colored(net, "")}";

        if (_energyTip != null)
        {
            string netColor = net >= 0f ? StatFormat.GreenHex : StatFormat.RedHex;
            string warn = eco.IsBankrupt
                ? "<color=#FF5555><b>БАНКРОТСТВО</b> — производство урезано вдвое</color>\n\n"
                : eco.BankruptcyLooming
                    ? $"<color=#FFAA55><b>Угроза банкротства</b>: казна опустеет через ~{Mathf.Max(1, Mathf.CeilToInt(eco.MonthsUntilEmpty))} мес.</color>\n\n"
                    : "";
            var rep = eco.Report;
            string overCap = rep.NavalUsed > rep.NavalCapacity
                ? $"  <color=#FF8888>(перебор лимита: ×{rep.OverCapacityMult:0.##})</color>" : "";

            _energyTip.SetText(
                $"<b>Гелий-3</b>\n" +
                warn +
                $"<color=#8AA2A8>Запас:</color> <b>{(int)eco.EnergyCredits}</b>\n" +
                $"<color=#8AA2A8>База империи:</color> {eco.BaseEnergyIncome:+0;-0;0} / мес\n" +
                $"<color=#8AA2A8>Районы-генераторы:</color> {eco.PlanetEnergyOutput:+0;-0;0} / мес\n" +
                $"<color=#8AA2A8>Добывающие станции:</color> {eco.StationEnergyOutput:+0;-0;0} / мес\n" +
                (eco.TechEnergyOutput > 0f ? $"<color=#8AA2A8>Реакторные технологии:</color> {eco.TechEnergyOutput:+0;-0;0} / мес\n" : "") +
                $"<color=#8AA2A8>Военный флот:</color> <color=#FF8888>-{rep.FleetUpkeep:0.#}</color> / мес{overCap}\n" +
                $"<color=#8AA2A8>Гражданские суда:</color> <color=#FF8888>-{rep.CivilianUpkeep:0.#}</color> / мес\n" +
                $"<color=#8AA2A8>Форпосты ({rep.Outposts} × {EmpireEconomy.OutpostUpkeep:0.#}):</color> <color=#FF8888>-{rep.OutpostUpkeepTotal:0.#}</color> / мес\n" +
                $"<color=#8AA2A8>──────────────</color>\n" +
                $"<color=#8AA2A8>Чистый доход:</color> <color={netColor}><b>{net:+0.#;-0.#;0} / мес</b></color>\n\n" +
                $"<color=#8AA2A8>Флотский лимит: {rep.NavalUsed} / {rep.NavalCapacity} (корвет 1, фрегат 2, эсминец 3; +{EmpireEconomy.NavalCapacityPerColony} за колонию). " +
                "Сверх лимита содержание всего военного флота растёт.</color>"
            );
        }
    }

    // === МИНЕРАЛЫ ===
    if (_mineralsVal != null)
    {
        _mineralsVal.text = $"{(int)eco.Minerals}  {StatFormat.Colored(eco.MonthlyMineralsIncome, "")}";
        if (_mineralsTip != null)
            _mineralsTip.SetText(
                $"<b>Титан</b>\n" +
                $"<color=#8AA2A8>Запас:</color> <b>{(int)eco.Minerals}</b>\n" +
                $"<color=#8AA2A8>База:</color> {eco.BaseMineralsIncome:+0;-0;0} / мес\n" +
                $"<color=#8AA2A8>Горные районы:</color> {eco.PlanetMineralsOutput:+0;-0;0} / мес\n" +
                $"<color=#8AA2A8>Добывающие станции:</color> {eco.StationMineralsOutput:+0;-0;0} / мес\n" +
                $"<color=#8AA2A8>──────────────</color>\n" +
                $"<color=#8AA2A8>Итого:</color> {StatFormat.Income(eco.MonthlyMineralsIncome)}"
            );
    }

    // === СПЛАВЫ ===
    if (_alloysVal != null)
    {
        _alloysVal.text = $"{(int)eco.Alloys}  {StatFormat.Colored(eco.MonthlyAlloysIncome, "")}";
        if (_alloysTip != null)
            _alloysTip.SetText(
                $"<b>Сплавы</b>\n" +
                $"<color=#8AA2A8>Запас:</color> <b>{(int)eco.Alloys}</b>\n" +
                $"<color=#8AA2A8>База:</color> {eco.BaseAlloysIncome:+0;-0;0} / мес\n" +
                $"<color=#8AA2A8>Планеты:</color> {eco.PlanetAlloysOutput:+0;-0;0} / мес\n" +
                $"<color=#8AA2A8>──────────────</color>\n" +
                $"<color=#8AA2A8>Итого:</color> {StatFormat.Income(eco.MonthlyAlloysIncome)}"
            );
    }

    // === ВЛИЯНИЕ ===
    if (_influenceVal != null)
    {
        _influenceVal.text = $"{(int)eco.Influence}  {StatFormat.Colored(eco.MonthlyInfluenceIncome, "")}";
        if (_influenceTip != null)
            _influenceTip.SetText(
                $"<b>Влияние</b>\n" +
                $"<color=#8AA2A8>Запас:</color> <b>{(int)eco.Influence}</b>\n" +
                $"<color=#8AA2A8>Доход:</color> {StatFormat.Income(eco.MonthlyInfluenceIncome)}\n\n" +
                $"<color=#8AA2A8><i>Расходуется на форпосты ({FleetManager.OutpostInfluenceCost(0):0}), колонии ({PlanetData.ColonyInfluenceCost:0}), " +
                $"пакты ({AIEmpireManager.PactInfluenceCost:0}), мирные предложения ({AIEmpireManager.PeaceInfluenceCost:0}) и терраформинг.</i></color>"
            );
    }

    // === НАУКА ===
    if (_researchVal != null && TechnologyManager.Instance != null)
    {
        var tm = TechnologyManager.Instance;
        _researchVal.text = $"{StatFormat.Colored(tm.MonthlyResearchIncome, " / мес")}";
        if (_researchTip != null)
        {
            int active = 0;
            foreach (var s in tm.Slots) if (s.CurrentTech != null && !s.IsPaused) active++;

            float mult = tm.GlobalResearchSpeedMultiplier;
            _researchTip.SetText(
                $"<b>Наука</b>\n" +
                $"<color=#8AA2A8>Администрация:</color> +{TechnologyManager.BaseScience:0.#}\n" +
                $"<color=#8AA2A8>Население ({tm.Population} × {TechnologyManager.SciencePerPop:0.#}):</color> +{tm.ScienceFromPopulation:0.#}\n" +
                (tm.FlatScience > 0f ? $"<color=#8AA2A8>Открытия и события:</color> +{tm.FlatScience:0.#}\n" : "") +
                (Mathf.Abs(mult - 1f) > 0.01f ? $"<color=#8AA2A8>Множитель технологий и событий:</color> ×{mult:0.##}\n" : "") +
                $"<color=#8AA2A8>──────────────</color>\n" +
                $"<color=#8AA2A8>Итого:</color> <b>+{tm.MonthlyResearchIncome:0.#} / мес</b>\n" +
                $"<color=#8AA2A8>Активных слотов:</color> {active} / {tm.Slots.Count}\n\n" +
                "<color=#8AA2A8><i>Наука растёт вместе с населением — заселяйте планеты и стройте жильё. " +
                "Скорость делится между слотами: один слот идёт быстрее, чем каждый из нескольких.</i></color>"
            );
        }
    }
}

// ==================== ХЕЛПЕРЫ ДЛЯ ТУЛТИПОВ ====================


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

                // ==================== ВЕРФЬ ====================

        private void BuildShipyardModal()
        {
            _shipyardPanel = new GameObject("ShipyardModal");
            _shipyardPanel.transform.SetParent(_modalCanvas.transform, false);

            var rt = _shipyardPanel.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(520, 660);

            _shipyardGroup = _shipyardPanel.AddComponent<CanvasGroup>();
            _shipyardPanel.AddComponent<Image>().color = DS.BgDeep;
            StyleModalWindow(_shipyardPanel, new Color(0.45f, 0.95f, 0.90f, 0.40f));

            var header = new GameObject("Header");
            header.transform.SetParent(_shipyardPanel.transform, false);
            var hbRt = header.AddComponent<RectTransform>();
            hbRt.anchorMin = new Vector2(0, 1);
            hbRt.anchorMax = new Vector2(1, 1);
            hbRt.pivot = new Vector2(0.5f, 1);
            hbRt.sizeDelta = new Vector2(0, 52);
            header.AddComponent<Image>().color = DS.BgHeader;
            LG.Header(header);

            var title = CreateText(header.transform, "⚙   ОРБИТАЛЬНАЯ ВЕРФЬ ФЛОТА", 13, FontStyle.Bold, DS.NeonCyan, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = new Vector2(22, 0);

            var closeBtn = CreateCloseButton(header.transform, 28f, () => CloseModal(_shipyardPanel));
            var cRt = closeBtn.GetComponent<RectTransform>();
            cRt.anchorMin = cRt.anchorMax = new Vector2(1, 0.5f);
            cRt.pivot = new Vector2(1, 0.5f);
            cRt.anchoredPosition = new Vector2(-14, 0);

            CreateShipyardOption(_shipyardPanel.transform, "НАУЧНЫЙ КОРАБЛЬ",      178, "science",  () => FleetManager.Instance != null && FleetManager.Instance.BuildShip(FleetType.Science));
            CreateShipyardOption(_shipyardPanel.transform, "СТРОИТЕЛЬНЫЙ КОРАБЛЬ", 118, "builder",  () => FleetManager.Instance != null && FleetManager.Instance.BuildShip(FleetType.Constructor));
            CreateShipyardOption(_shipyardPanel.transform, "БОЕВОЙ КОРВЕТ",         58, "corvette", () => FleetManager.Instance != null && FleetManager.Instance.BuildShip(FleetType.Military));
            CreateShipyardOption(_shipyardPanel.transform, "ФРЕГАТ",                -2, "frigate",  () => FleetManager.Instance != null && FleetManager.Instance.BuildWarship(ShipClass.Frigate));
            CreateShipyardOption(_shipyardPanel.transform, "ЭСМИНЕЦ",              -62, "destroyer",() => FleetManager.Instance != null && FleetManager.Instance.BuildWarship(ShipClass.Destroyer));

            var designBtn = CreateButton(_shipyardPanel.transform, "OpenDesigner", new Vector2(440, 40),
                DS.BtnPrimary, DS.BtnPrimaryHi, () =>
                {
                    LG.Hide(_shipyardPanel);
                    ShipDesignerModal.Instance?.Open();
                });
            var dRt = designBtn.GetComponent<RectTransform>();
            dRt.anchorMin = dRt.anchorMax = new Vector2(0.5f, 0.5f);
            dRt.anchoredPosition = new Vector2(0, -126);
            var dTxt = CreateText(designBtn.transform, "◆  ОТКРЫТЬ КОНСТРУКТОР КОРАБЛЕЙ", 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            dTxt.rectTransform.sizeDelta = dRt.sizeDelta;

            var retrofitBtn = CreateButton(_shipyardPanel.transform, "RetrofitBtn", new Vector2(440, 40),
                DS.BtnSuccess, DS.Gold, OnRetrofitClicked);
            var rRt = retrofitBtn.GetComponent<RectTransform>();
            rRt.anchorMin = rRt.anchorMax = new Vector2(0.5f, 0.5f);
            rRt.anchoredPosition = new Vector2(0, -176);
            _shipyardRetrofitBtn = retrofitBtn.GetComponent<Button>();
            _shipyardRetrofitTxt = CreateText(retrofitBtn.transform, "⟳  МОДЕРНИЗИРОВАТЬ ФЛОТ (RETROFIT)", 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            _shipyardRetrofitTxt.rectTransform.sizeDelta = rRt.sizeDelta;

            // Очередь верфи
            _shipyardQueue = CreateText(_shipyardPanel.transform, "", 10, FontStyle.Normal, DS.TextPrimary, TextAnchor.UpperLeft);
            var qRt = _shipyardQueue.rectTransform;
            qRt.anchorMin = qRt.anchorMax = new Vector2(0.5f, 0.5f);
            qRt.pivot = new Vector2(0.5f, 1f);
            qRt.sizeDelta = new Vector2(440, 70);
            qRt.anchoredPosition = new Vector2(0, -204);
            _shipyardQueue.horizontalOverflow = HorizontalWrapMode.Wrap;

            _shipyardPanel.SetActive(false);
        }

        private readonly Dictionary<string, Text> _shipyardPrices = new Dictionary<string, Text>();
        private Text _shipyardQueue;

        private static FleetType KeyType(string key) => key == "science" ? FleetType.Science : key == "builder" ? FleetType.Constructor : FleetType.Military;
        private static ShipClass KeyHull(string key) => key == "destroyer" ? ShipClass.Destroyer : key == "frigate" ? ShipClass.Frigate : ShipClass.Corvette;

        private void RefreshShipyardQueue()
        {
            if (_shipyardQueue == null) return;
            var cm = ConstructionManager.Instance;
            var q = cm != null ? cm.ShipQueue(0) : new List<ConstructionJob>();
            if (q.Count == 0)
            {
                _shipyardQueue.text = $"<color=#8AA2A8>Стапели свободны · одновременно строится {ConstructionManager.ShipyardSlots} корабля</color>";
                return;
            }
            var sb = new StringBuilder($"<color=#F2C747><b>НА СТАПЕЛЯХ ({q.Count})</b></color>\n");
            for (int i = 0; i < q.Count && i < 4; i++)
            {
                var j = q[i];
                string name = j.ShipType == FleetType.Science ? "Научный корабль"
                            : j.ShipType == FleetType.Constructor ? "Строительный корабль"
                            : j.Hull == ShipClass.Destroyer ? "Эсминец" : j.Hull == ShipClass.Frigate ? "Фрегат" : "Корвет";
                string state = i < ConstructionManager.ShipyardSlots ? $"{Mathf.RoundToInt(j.Progress * 100f)}%" : "в очереди";
                sb.Append($"{name} — {state}, готов через ~{Mathf.CeilToInt(cm.DaysUntilDone(j))} дн.\n");
            }
            if (q.Count > 4) sb.Append($"<color=#8AA2A8>… и ещё {q.Count - 4}</color>");
            _shipyardQueue.text = sb.ToString();
        }
        private readonly Dictionary<string, Button> _shipyardButtons = new Dictionary<string, Button>();

        /// <summary>Цены верфи с учётом технологий и содержание, которое добавит корабль.</summary>
        private void RefreshShipyardPrices()
        {
            var eco = EconomyManager.Instance;
            var fm = FleetManager.Instance;
            foreach (var kv in _shipyardPrices)
            {
                float alloys, energy, upkeep;
                string locked = null;
                switch (kv.Key)
                {
                    case "science":
                        alloys = FleetManager.ScienceShipAlloys; energy = FleetManager.ScienceShipEnergy;
                        upkeep = FleetData.UpkeepFor(FleetType.Science, ShipClass.Corvette);
                        break;
                    case "builder":
                        alloys = FleetManager.ConstructorAlloys; energy = FleetManager.ConstructorEnergy;
                        upkeep = FleetData.UpkeepFor(FleetType.Constructor, ShipClass.Corvette);
                        break;
                    default:
                        var cls = kv.Key == "destroyer" ? ShipClass.Destroyer : kv.Key == "frigate" ? ShipClass.Frigate : ShipClass.Corvette;
                        var design = fm != null ? fm.PlayerDesignFor(cls) : null;
                        alloys = design != null ? design.AlloyCost : 60f; energy = FleetManager.WarshipEnergy;
                        upkeep = eco != null ? EmpireEconomy.ExtraUpkeepForShip(eco.Report, cls)
                                             : FleetData.UpkeepFor(FleetType.Military, cls);
                        if (cls == ShipClass.Destroyer && !(TechnologyManager.Instance?.DestroyerUnlocked ?? false))
                            locked = "нужна технология «Верфи класса „Эсминец“»";
                        break;
                }
                alloys = FleetManager.ShipAlloyCost(alloys, 0);
                kv.Value.text = locked != null
                    ? $"<color=#8AA2A8>{locked}</color>"
                    : $"{alloys:0} спл. · {energy:0} гел. · {ConstructionManager.ShipDays(KeyType(kv.Key), KeyHull(kv.Key)):0} дн.\n<color=#FF8888>содержание −{upkeep:0.#} гел./мес</color>";
                if (_shipyardButtons.TryGetValue(kv.Key, out var b) && b != null) b.interactable = locked == null;
            }
            RefreshShipyardQueue();
        }

        private void OpenShipyardModal()
        {
            ShowModalDim();
            MapModeController.HideGlobal();
            RefreshShipyardPrices();
            RefreshRetrofitButton();
            LG.Show(_shipyardPanel);
            _shipyardPanel.transform.SetAsLastSibling();
        }

        private void CreateShipyardOption(Transform parent, string shipTitle, float yPos, string key, System.Func<bool> build)
        {
            string price = "";
            var btn = CreateButton(parent, $"Buy_{key}", new Vector2(440, 52),
                DS.BgSlot, DS.BtnPrimaryHi, () =>
                {
                    if (build())
                    {
                        RefreshResourceBar();
                        RefreshShipyardPrices();   // окно остаётся открытым — можно заложить ещё
                    }
                });

            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, yPos);

            var tName = CreateText(btn.transform, shipTitle, 13, FontStyle.Bold, DS.TextPrimary, TextAnchor.MiddleLeft);
            tName.rectTransform.anchorMin = new Vector2(0, 0);
            tName.rectTransform.anchorMax = new Vector2(0.45f, 1);
            tName.rectTransform.offsetMin = new Vector2(16, 0);

            var tPrice = CreateText(btn.transform, price, 11, FontStyle.Bold, DS.Gold, TextAnchor.MiddleRight);
            _shipyardPrices[key] = tPrice;
            _shipyardButtons[key] = btn.GetComponent<Button>();
            tPrice.rectTransform.anchorMin = new Vector2(0.45f, 0);
            tPrice.rectTransform.anchorMax = new Vector2(1, 1);
            tPrice.rectTransform.offsetMax = new Vector2(-16, 0);
        }

        private void OpenTechModal()
        {
            if (TechTreePanel.Instance == null) return;
            ShowModalDim();
            TechTreePanel.Instance.Open();
        }

        // ==================== ФРАКЦИЯ ====================

        private void BuildFactionSelectionModal()
        {
            _factionSelectionModal = new GameObject("FactionSelectionModal");
            _factionSelectionModal.transform.SetParent(_modalCanvas.transform, false);

            var rt = _factionSelectionModal.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1120, 660);

            _factionSelectionModal.AddComponent<Image>().color = DS.BgDeep;
            StyleModalWindow(_factionSelectionModal, new Color(0.45f, 0.95f, 0.90f, 0.45f));

            // ===== ЗАГОЛОВОК =====
            var header = new GameObject("Header");
            header.transform.SetParent(_factionSelectionModal.transform, false);
            var hbRt = header.AddComponent<RectTransform>();
            hbRt.anchorMin = new Vector2(0, 1);
            hbRt.anchorMax = new Vector2(1, 1);
            hbRt.pivot = new Vector2(0.5f, 1);
            hbRt.sizeDelta = new Vector2(0, 66);
            hbRt.anchoredPosition = Vector2.zero;
            header.AddComponent<Image>().color = DS.BgHeader;
            LG.Header(header);

            var headerLine = new GameObject("HeaderLine");
            headerLine.transform.SetParent(header.transform, false);
            var hlRt = headerLine.AddComponent<RectTransform>();
            hlRt.anchorMin = new Vector2(0, 0);
            hlRt.anchorMax = new Vector2(1, 0);
            hlRt.pivot = new Vector2(0.5f, 0.5f);
            hlRt.sizeDelta = new Vector2(-120, 1.5f);
            hlRt.anchoredPosition = Vector2.zero;
            headerLine.AddComponent<Image>().color = DS.NeonCyan;
            LG.Line(headerLine, hairline: true);

            var title = CreateText(header.transform, "◆  ВЫБОР ЦИВИЛИЗАЦИИ  ◆", 22, FontStyle.Bold, DS.NeonCyan, TextAnchor.MiddleCenter);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = new Vector2(40, 0);
            title.rectTransform.offsetMax = new Vector2(-40, 0);

            // Возврат в главное меню (передумал с параметрами галактики)
            var backBtn = LGBuild.Button(header.transform, "BackToMenu", DS.BtnNeutral, new Color(0.7f, 0.85f, 0.95f, 0.45f),
                ReturnToMainMenuFromSetup, LGIcon.Back, "МЕНЮ", 11);
            ((RectTransform)backBtn.transform).At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(112, 36));

            // ===== КАРТОЧКИ =====
            const float CardW = 320f;
            const float CardH = 500f;
            const float CardGap = 30f;
            const float CardY = 8f;

            int count = FactionRegistry.AvailableFactions.Length;
            float totalW = count * CardW + (count - 1) * CardGap;
            float startX = -totalW * 0.5f + CardW * 0.5f;

            var cards = new List<RectTransform>(count);
            var groups = new List<CanvasGroup>(count);
            var hovers = new List<FactionCardHover>(count);

            for (int i = 0; i < count; i++)
            {
                var f = FactionRegistry.AvailableFactions[i];
                float cx = startX + i * (CardW + CardGap);

                var card = new GameObject($"Card_{f.Name}");
                card.transform.SetParent(_factionSelectionModal.transform, false);

                var cRt = card.AddComponent<RectTransform>();
                cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 0.5f);
                cRt.pivot = new Vector2(0.5f, 0.5f);
                cRt.sizeDelta = new Vector2(CardW, CardH);
                cRt.anchoredPosition = new Vector2(cx, CardY);

                var cardGroup = card.AddComponent<CanvasGroup>();
                cardGroup.alpha = 1f;

                var cardBg = card.AddComponent<Image>();
                cardBg.color = DS.BgSlot;
                cardBg.raycastTarget = true;

                // Outline остаётся "источником" цвета кромки: FactionCardHover анимирует его,
                // а LiquidGlassEffect превращает в неоновую обводку и свечение карточки.
                var cardOutline = card.AddComponent<Outline>();
                cardOutline.effectColor = Color.Lerp(f.EmpireColor, new Color(0.02f, 0.06f, 0.08f), 0.55f);
                cardOutline.effectDistance = new Vector2(1.5f, -1.5f);
                LG.Platter(card, 20f).GlowMultiplier = 3f;

                // Плашка с названием
                var namePlate = new GameObject("NamePlate");
                namePlate.transform.SetParent(card.transform, false);
                var npRt = namePlate.AddComponent<RectTransform>();
                npRt.anchorMin = new Vector2(0, 1);
                npRt.anchorMax = new Vector2(1, 1);
                npRt.pivot = new Vector2(0.5f, 1);
                npRt.sizeDelta = new Vector2(0, 48);
                npRt.anchoredPosition = Vector2.zero;

                var npImg = namePlate.AddComponent<Image>();
                npImg.color = Color.Lerp(f.EmpireColor, Color.black, 0.45f);
                npImg.raycastTarget = false;
                LG.Header(namePlate).FillMultiplier = 1.8f;

                string roman = i switch { 0 => "I", 1 => "II", 2 => "III", 3 => "IV", _ => (i + 1).ToString() };

                var numCircle = new GameObject("NumCircle");
                numCircle.transform.SetParent(namePlate.transform, false);
                var ncRt = numCircle.AddComponent<RectTransform>();
                ncRt.anchorMin = ncRt.anchorMax = new Vector2(0, 0.5f);
                ncRt.pivot = new Vector2(0, 0.5f);
                ncRt.sizeDelta = new Vector2(34, 34);
                ncRt.anchoredPosition = new Vector2(8, 0);

                var ncImg = numCircle.AddComponent<Image>();
                ncImg.color = new Color(0f, 0f, 0f, 0.55f);
                ncImg.raycastTarget = false;
                var ncFx = LG.Platter(numCircle, 17f);
                ncFx.SetRim(new Color(f.EmpireColor.r, f.EmpireColor.g, f.EmpireColor.b, 0.9f));
                ncFx.GlowMultiplier = 0f;

                var ncTxt = CreateText(numCircle.transform, roman, 14, FontStyle.Bold, f.EmpireColor, TextAnchor.MiddleCenter);
                ncTxt.rectTransform.anchorMin = Vector2.zero;
                ncTxt.rectTransform.anchorMax = Vector2.one;
                ncTxt.rectTransform.offsetMin = Vector2.zero;
                ncTxt.rectTransform.offsetMax = Vector2.zero;

                var nameTxt = CreateText(namePlate.transform, f.Name.ToUpper(), 14, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
                nameTxt.rectTransform.anchorMin = Vector2.zero;
                nameTxt.rectTransform.anchorMax = Vector2.one;
                nameTxt.rectTransform.offsetMin = new Vector2(50, 0);
                nameTxt.rectTransform.offsetMax = new Vector2(-10, 0);

                // Подзаголовок
                var subTitle = new GameObject("SubTitle");
                subTitle.transform.SetParent(card.transform, false);
                var stRt = subTitle.AddComponent<RectTransform>();
                stRt.anchorMin = new Vector2(0, 1);
                stRt.anchorMax = new Vector2(1, 1);
                stRt.pivot = new Vector2(0.5f, 1);
                stRt.sizeDelta = new Vector2(0, 22);
                stRt.anchoredPosition = new Vector2(0, -48);

                var stBg = subTitle.AddComponent<Image>();
                stBg.color = new Color(0.02f, 0.06f, 0.08f, 0.9f);
                stBg.raycastTarget = false;
                LG.Apply(subTitle, LiquidGlassEffect.Role.Header, 0f);

                var stTxt = CreateText(subTitle.transform, f.Title, 11, FontStyle.Italic, f.EmpireColor, TextAnchor.MiddleCenter);
                stTxt.rectTransform.anchorMin = Vector2.zero;
                stTxt.rectTransform.anchorMax = Vector2.one;
                stTxt.rectTransform.offsetMin = new Vector2(6, 0);
                stTxt.rectTransform.offsetMax = new Vector2(-6, 0);

                // Видео-зона
                var videoZone = new GameObject("VideoZone");
videoZone.transform.SetParent(card.transform, false);
var vRt = videoZone.AddComponent<RectTransform>();
vRt.anchorMin = new Vector2(0, 1);
vRt.anchorMax = new Vector2(1, 1);
vRt.pivot = new Vector2(0.5f, 1);
vRt.sizeDelta = new Vector2(-16, 156);         // было 170 → 156
vRt.anchoredPosition = new Vector2(0, -72);    // было -78 → -72

var vBg = videoZone.AddComponent<Image>();
vBg.color = new Color(0.008f, 0.020f, 0.028f, 1f); 
vBg.raycastTarget = false;

var vOutline = videoZone.AddComponent<Outline>();
vOutline.effectColor = new Color(f.EmpireColor.r, f.EmpireColor.g, f.EmpireColor.b, 0.35f);
vOutline.effectDistance = new Vector2(1f, -1f);

                LG.Platter(videoZone, 14f);
                var videoMask = LG.RoundedMask(videoZone.transform, 14f, 1f);

                var videoHost = new GameObject("VideoHost");
                videoHost.transform.SetParent(videoZone.transform, false);
                var videoBg = videoHost.AddComponent<FactionVideoBackground>();
                videoBg.Initialize(videoMask);

                string videoKey = i switch
                {
                    0 => "un",
                    1 => "chinvar",
                    2 => "orion",
                    _ => "un"
                };
                videoBg.Preload(videoKey);

                // Описание
                // Описание — панель расширена вниз и вверх
var descBox = new GameObject("DescBox");
descBox.transform.SetParent(card.transform, false);
var dBgRt = descBox.AddComponent<RectTransform>();
dBgRt.anchorMin = new Vector2(0, 0);
dBgRt.anchorMax = new Vector2(1, 1);
dBgRt.offsetMin = new Vector2(14, 60);                     // было 120 — опустили вниз ближе к кнопке
dBgRt.offsetMax = new Vector2(-14, -(72 + 156 + 8));       // видеозона уменьшена, панель выше

var dBgImg = descBox.AddComponent<Image>();
dBgImg.color = new Color(0.008f, 0.025f, 0.035f, 0.85f);
dBgImg.raycastTarget = false;

var dBorder = descBox.AddComponent<Outline>();
dBorder.effectColor = new Color(f.EmpireColor.r * 0.5f, f.EmpireColor.g * 0.5f, f.EmpireColor.b * 0.5f, 0.4f);
dBorder.effectDistance = new Vector2(0.8f, -0.8f);

var descTxt = CreateText(descBox.transform, f.Description, 10, FontStyle.Normal, DS.TextPrimary, TextAnchor.UpperLeft);  // было 11 — уменьшили
descTxt.rectTransform.anchorMin = Vector2.zero;
descTxt.rectTransform.anchorMax = Vector2.one;
descTxt.rectTransform.offsetMin = new Vector2(10, 6);
descTxt.rectTransform.offsetMax = new Vector2(-10, -6);
descTxt.lineSpacing = 1.2f;                                  // было 1.35 — плотнее
descTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
descTxt.verticalOverflow = VerticalWrapMode.Overflow;
descTxt.supportRichText = true;

                // Кнопка
                var selectBtn = CreateButton(card.transform, "SelectBtn", new Vector2(-12, 44),
    DS.BtnPrimary, DS.BtnPrimaryHi, () => OnFactionChosen(f));

var sRt = selectBtn.GetComponent<RectTransform>();
sRt.anchorMin = new Vector2(0, 0);
sRt.anchorMax = new Vector2(1, 0);
sRt.pivot = new Vector2(0.5f, 0);
sRt.offsetMin = new Vector2(6, 6);
sRt.offsetMax = new Vector2(-6, 6 + 44);

                var btnImg = selectBtn.GetComponent<Image>();
                if (btnImg != null)
                    btnImg.color = Color.Lerp(f.EmpireColor, Color.black, 0.55f);
                LG.Button(selectBtn, new Color(f.EmpireColor.r, f.EmpireColor.g, f.EmpireColor.b, 0.75f));

                var tBtn = CreateText(selectBtn.transform, "▶  ПРИНЯТЬ КОМАНДОВАНИЕ", 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
                tBtn.rectTransform.anchorMin = Vector2.zero;
                tBtn.rectTransform.anchorMax = Vector2.one;
                tBtn.rectTransform.offsetMin = Vector2.zero;
                tBtn.rectTransform.offsetMax = Vector2.zero;

                // Hover / фокус
                var hover = card.AddComponent<FactionCardHover>();
                hover.Configure(cRt, cardOutline, cardGroup, f.EmpireColor, videoBg);

                int capturedIndex = i;
                var trigger = card.AddComponent<UnityEngine.EventSystems.EventTrigger>();

                var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry
                {
                    eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter
                };
                enterEntry.callback.AddListener(_ => SetFocusedFaction(capturedIndex, hovers));
                trigger.triggers.Add(enterEntry);

                cards.Add(cRt);
                groups.Add(cardGroup);
                hovers.Add(hover);
            }

            // Нижняя полоса со слоганом
            var footer = new GameObject("Footer");
            footer.transform.SetParent(_factionSelectionModal.transform, false);
            var fRt = footer.AddComponent<RectTransform>();
            fRt.anchorMin = new Vector2(0, 0);
            fRt.anchorMax = new Vector2(1, 0);
            fRt.pivot = new Vector2(0.5f, 0);
            fRt.sizeDelta = new Vector2(0, 36);
            fRt.anchoredPosition = Vector2.zero;

            var fBg = footer.AddComponent<Image>();
            fBg.color = DS.BgVisor;
            fBg.raycastTarget = false;
            LG.Header(footer);

            var fLine = new GameObject("TopLine");
            fLine.transform.SetParent(footer.transform, false);
            var flRt = fLine.AddComponent<RectTransform>();
            flRt.anchorMin = new Vector2(0, 1);
            flRt.anchorMax = new Vector2(1, 1);
            flRt.pivot = new Vector2(0.5f, 0.5f);
            flRt.sizeDelta = new Vector2(-120, 1.2f);
            flRt.anchoredPosition = Vector2.zero;
            fLine.AddComponent<Image>().color = new Color(0.36f, 0.88f, 0.82f, 0.35f);
            LG.Line(fLine, hairline: true);

            var gs = GameSession.Settings;
            string galaxyInfo = $"{NewGameSettings.SizeNames[Mathf.Clamp(gs.GalaxySize, 0, 2)].ToUpper()} ГАЛАКТИКА · {gs.StarCount} СИСТЕМ · " +
                                $"СЛОЖНОСТЬ: {NewGameSettings.DifficultyNames[Mathf.Clamp(gs.Difficulty, 0, 2)].ToUpper()} · СИД {gs.Seed}";
            var footerTxt = CreateText(footer.transform, "ВЫБЕРИТЕ ПУТЬ, КОТОРЫМ ПОВЕДЁТЕ СВОЮ ЦИВИЛИЗАЦИЮ В ГЛУБИНЫ ГАЛАКТИКИ   ·   " + galaxyInfo,
                                       10, FontStyle.Italic, DS.TextMuted, TextAnchor.MiddleCenter);
            footerTxt.rectTransform.anchorMin = Vector2.zero;
            footerTxt.rectTransform.anchorMax = Vector2.one;
            footerTxt.rectTransform.offsetMin = new Vector2(20, 0);
            footerTxt.rectTransform.offsetMax = new Vector2(-20, 0);

            SetFocusedFaction(0, hovers);

            StartCoroutine(AnimateFactionCardsIn(cards, groups));
        }

        private void SetFocusedFaction(int index, List<FactionCardHover> hovers)
        {
            for (int i = 0; i < hovers.Count; i++)
            {
                if (hovers[i] == null) continue;
                hovers[i].SetFocused(i == index);
            }
        }

        private IEnumerator AnimateFactionCardsIn(List<RectTransform> cards, List<CanvasGroup> groups)
        {
            const float dur = 0.32f;
            const float stagger = 0.07f;

            var finalPositions = new Vector2[cards.Count];
            for (int i = 0; i < cards.Count; i++)
            {
                finalPositions[i] = cards[i].anchoredPosition;
                groups[i].alpha = 0f;
                cards[i].anchoredPosition = finalPositions[i] + new Vector2(0, -25f);
                cards[i].localScale = Vector3.one * 0.94f;
            }

            yield return null;

            for (int i = 0; i < cards.Count; i++)
            {
                float t = 0f;
                Vector2 startPos = cards[i].anchoredPosition;
                Vector3 startScale = cards[i].localScale;

                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                    groups[i].alpha = k;
                    cards[i].anchoredPosition = Vector2.Lerp(startPos, finalPositions[i], k);
                    cards[i].localScale = Vector3.Lerp(startScale, Vector3.one, k);
                    yield return null;
                }

                groups[i].alpha = 1f;
                cards[i].anchoredPosition = finalPositions[i];
                cards[i].localScale = Vector3.one;

                if (i < cards.Count - 1)
                    yield return new WaitForSecondsRealtime(stagger);
            }
        }

        private void OnFactionChosen(FactionInfo faction)
        {
            _selectedFaction = faction;
            LG.Hide(_factionSelectionModal);

            if (EconomyManager.Instance != null)
                EconomyManager.Instance.ApplyFactionBonuses(faction);

            OpenAdvisorIntroModal(faction);
        }

        private void BuildAdvisorIntroModal()
        {
            _advisorIntroModal = new GameObject("AdvisorIntroModal");
            _advisorIntroModal.transform.SetParent(_modalCanvas.transform, false);

            var rt = _advisorIntroModal.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(660, 340);

            _advisorIntroModal.AddComponent<Image>().color = DS.BgDeep;
            StyleModalWindow(_advisorIntroModal, new Color(0.45f, 0.95f, 0.90f, 0.45f));

            _advisorText = CreateText(_advisorIntroModal.transform, "", 13, FontStyle.Normal, DS.TextPrimary, TextAnchor.UpperLeft);
            _advisorText.rectTransform.anchorMin = new Vector2(0, 0);
            _advisorText.rectTransform.anchorMax = new Vector2(1, 1);
            _advisorText.rectTransform.offsetMin = new Vector2(28, 75);
            _advisorText.rectTransform.offsetMax = new Vector2(-28, -25);
            _advisorText.lineSpacing = 1.35f;
            _advisorText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _advisorText.supportRichText = true;

            var startBtn = CreateButton(_advisorIntroModal.transform, "StartGameBtn",
                new Vector2(280, 40), DS.BtnSuccess, DS.NeonCyan, () =>
                {
                    LG.Hide(_advisorIntroModal);
                    HideModalDim();
                    Time.timeScale = 1f;
                    SpawnInGameHUD();
                    if (GameSession.Settings.Tutorial) TutorialManager.Instance?.BeginTutorial();
                });

            var sRt = startBtn.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0.5f, 0);
            sRt.anchorMax = new Vector2(0.5f, 0);
            sRt.pivot = new Vector2(0.5f, 0);
            sRt.anchoredPosition = new Vector2(0, 18);

            var sTxt = CreateText(startBtn.transform, "▶  ВСТУПИТЬ В ДОЛЖНОСТЬ", 12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            sTxt.rectTransform.sizeDelta = sRt.sizeDelta;

            _advisorIntroModal.SetActive(false);
        }

        private void OpenAdvisorIntroModal(FactionInfo faction)
        {
            ShowModalDim();
            LG.Show(_advisorIntroModal);
            _advisorIntroModal.transform.SetAsLastSibling();

            _advisorText.text = $"Приветствую, Командующий!\n\n" +
                                $"Бортовой тактический сервер развернут. Государственный суверенитет: <b>{faction.Name}</b> ({faction.Title}).\n\n" +
                                $"• Доктрина цивилизации: {faction.Description}\n\n" +
                                $"Все сенсоры на связи. Готовьте научный корабль к разведке приграничных систем.";
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
            _rewardText.text = $"★  <b>{sysName}</b>\n<color=#4DF08C>Разведка завершена!</color>";
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

        private void BuildEventPopupModal()
        {
            _eventPopupModal = new GameObject("EventPopupModal");
            _eventPopupModal.transform.SetParent(_modalCanvas.transform, false);

            var rt = _eventPopupModal.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(650f, 420f);

            _eventPopupModal.AddComponent<Image>().color = DS.BgDeep;
            _eventWindowFx = LG.Glass(_eventPopupModal);
            LG.Motion(_eventPopupModal, LGAppear.Kind.Pop);

            var header = new GameObject("Header");
            header.transform.SetParent(_eventPopupModal.transform, false);
            var hRt = header.AddComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0, 1);
            hRt.anchorMax = new Vector2(1, 1);
            hRt.pivot = new Vector2(0.5f, 1);
            hRt.sizeDelta = new Vector2(0, 52);
            header.AddComponent<Image>().color = DS.BgHeader;
            LG.Header(header);

            // Акцент цвета аномалии — светящаяся линия под шапкой
            var accentBarObj = new GameObject("AccentBar");
            accentBarObj.transform.SetParent(header.transform, false);
            var aRt = accentBarObj.AddComponent<RectTransform>();
            aRt.anchorMin = new Vector2(0, 0);
            aRt.anchorMax = new Vector2(1, 0);
            aRt.pivot = new Vector2(0.5f, 0.5f);
            aRt.sizeDelta = new Vector2(-60, 2f);
            aRt.anchoredPosition = Vector2.zero;
            _eventAccentBar = accentBarObj.AddComponent<Image>();
            _eventAccentBar.color = DS.NeonCyan;
            LG.Line(accentBarObj, hairline: true);

            _eventTitleText = CreateText(header.transform, "◆  АНОМАЛИЯ", 15, FontStyle.Bold, DS.Gold, TextAnchor.MiddleLeft);
            _eventTitleText.rectTransform.anchorMin = Vector2.zero;
            _eventTitleText.rectTransform.anchorMax = Vector2.one;
            _eventTitleText.rectTransform.offsetMin = new Vector2(22, 0);
            _eventTitleText.rectTransform.offsetMax = new Vector2(-60, 0);

            var closeBtn = CreateCloseButton(header.transform, 28f, () => CloseEventPopup());
            var cRt = closeBtn.GetComponent<RectTransform>();
            cRt.anchorMin = cRt.anchorMax = new Vector2(1, 0.5f);
            cRt.pivot = new Vector2(1, 0.5f);
            cRt.anchoredPosition = new Vector2(-14, 0);

            var descBox = new GameObject("DescriptionBox");
            descBox.transform.SetParent(_eventPopupModal.transform, false);
            var dRt = descBox.AddComponent<RectTransform>();
            dRt.anchorMin = new Vector2(0, 0);
            dRt.anchorMax = new Vector2(1, 1);
            dRt.offsetMin = new Vector2(20, 170);
            dRt.offsetMax = new Vector2(-20, -62);
            descBox.AddComponent<Image>().color = DS.BgVisor;
            LG.Platter(descBox, 16f);

            _eventDescText = CreateText(descBox.transform, "", 11, FontStyle.Normal, DS.TextPrimary, TextAnchor.UpperLeft);
            _eventDescText.rectTransform.anchorMin = Vector2.zero;
            _eventDescText.rectTransform.anchorMax = Vector2.one;
            _eventDescText.rectTransform.offsetMin = new Vector2(14, 10);
            _eventDescText.rectTransform.offsetMax = new Vector2(-14, -10);
            _eventDescText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _eventDescText.verticalOverflow = VerticalWrapMode.Truncate;
            _eventDescText.lineSpacing = 1.15f;

            var optionsBox = new GameObject("Options");
            optionsBox.transform.SetParent(_eventPopupModal.transform, false);
            var oRt = optionsBox.AddComponent<RectTransform>();
            oRt.anchorMin = new Vector2(0, 0);
            oRt.anchorMax = new Vector2(1, 0);
            oRt.pivot = new Vector2(0.5f, 0);
            oRt.sizeDelta = new Vector2(0, 150);
            oRt.anchoredPosition = new Vector2(0, 12);

            var vlg = optionsBox.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(16, 16, 8, 8);

            _eventOptionsHolder = optionsBox.transform;

            _eventPopupModal.SetActive(false);
        }

        private LiquidGlassEffect _eventWindowFx;

        private void CloseEventPopup()
        {
            if (_eventPopupModal != null) LG.Hide(_eventPopupModal);
            HideModalDim();
            MapModeController.ShowGlobal();
            Time.timeScale = 1f;
        }

        private void ShowEventPopup(GameEventData ev)
        {
            if (_eventPopupModal == null || ev == null) return;

            Color accent = AnomalyEventSystem.GetAnomalyAccent(ev.Anomaly);
            if (_eventAccentBar != null) _eventAccentBar.color = accent;
            _eventWindowFx?.SetRim(new Color(accent.r, accent.g, accent.b, 0.55f));
            if (_eventTitleText != null)
            {
                _eventTitleText.text = $"◆  {ev.Title}";
                _eventTitleText.color = accent;
            }
            if (_eventDescText != null) _eventDescText.text = ev.Description;

            if (_eventOptionsHolder != null)
            {
                for (int i = _eventOptionsHolder.childCount - 1; i >= 0; i--)
                    Destroy(_eventOptionsHolder.GetChild(i).gameObject);
            }

            if (ev.Options != null)
            {
                int idx = 0;
                foreach (var opt in ev.Options)
                {
                    if (opt == null) continue;
                    idx++;
                    Color optHover = idx == 1 ? DS.BtnSuccess : idx == 2 ? DS.BtnPrimaryHi : DS.Gold;
                    var capturedOpt = opt;

                    var btnGo = CreateButton(_eventOptionsHolder, $"Opt_{idx}", new Vector2(0, 44),
                        DS.BgSlot, optHover, () =>
                        {
                            try { capturedOpt.OnSelect?.Invoke(); }
                            catch (System.Exception e) { Debug.LogWarning($"[Event] OnSelect: {e.Message}"); }
                            CloseEventPopup();
                        });
                    var le = btnGo.GetComponent<LayoutElement>();
                    if (le == null) le = btnGo.AddComponent<LayoutElement>();
                    le.preferredHeight = 44f;

                    var label = CreateText(btnGo.transform, opt.OptionText, 11, FontStyle.Bold, DS.TextPrimary, TextAnchor.MiddleCenter);
                    label.rectTransform.anchorMin = Vector2.zero;
                    label.rectTransform.anchorMax = Vector2.one;
                    label.rectTransform.offsetMin = new Vector2(12, 0);
                    label.rectTransform.offsetMax = new Vector2(-12, 0);
                    label.supportRichText = true;

                    if (!string.IsNullOrEmpty(opt.ResultTooltip))
                    {
                        var tt = btnGo.GetComponent<TooltipTrigger>();
                        if (tt == null) TooltipHelper.Attach(btnGo, opt.ResultTooltip);
                        else tt.SetText(opt.ResultTooltip);
                    }
                }
            }

            LG.Show(_eventPopupModal);
            _eventPopupModal.transform.SetAsLastSibling();
            LG.Skin(_eventPopupModal.transform);
        }

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
