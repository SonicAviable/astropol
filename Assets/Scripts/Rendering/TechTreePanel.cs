using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Экран исследований. Три ветки (физика, общество, инженерия) — вкладки сверху;
    /// внутри ветки направления идут дорожками сверху вниз, уровни — колонками слева направо,
    /// связи — «шинами» между карточками. Слева — научные слоты с прогрессом и очередями,
    /// справа — досье выбранной технологии: эффект, стоимость, требования, что открывает.
    /// Щелчок — выбрать, двойной щелчок или кнопка — исследовать (или поставить в очередь).
    /// </summary>
    public class TechTreePanel : MonoBehaviour
    {
        public static TechTreePanel Instance { get; private set; }
        public bool IsOpen => _isOpen;

        // ==================== РАЗМЕТКА ====================

        private const float WinW = 1840f, WinH = 1010f;
        private const float HeaderH = 70f, Pad = 14f, Gap = 12f;
        private const float LeftW = 320f, RightW = 390f;
        private const float TabsH = 62f, TierHeadH = 28f;
        private const float LaneHeadW = 128f, CardW = 216f;
        private const float LanePadV = 8f, LaneGap = 10f, RowGap = 10f;
        /// <summary>Высота области дерева: строки растягиваются, чтобы ветка заняла её целиком.</summary>
        private const float TreeH = WinH - HeaderH - 8f - Pad - (TabsH + 8f + TierHeadH + 4f);
        private const float DoubleClick = 0.4f;
        private static readonly string[] TierNames = { "БАЗОВЫЕ", "УРОВЕНЬ I", "УРОВЕНЬ II", "УРОВЕНЬ III" };

        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Green => UIManager.DS.Green;
        private static Color Red => UIManager.DS.Red;
        private static readonly Color Orange = new Color(1f, 0.66f, 0.50f);
        private static readonly Color WireIdle = new Color(0.40f, 0.52f, 0.60f, 0.38f);
        private static readonly Color CardBg = new Color(0.045f, 0.075f, 0.100f, 0.97f);
        private static readonly Color CardBgDone = new Color(0.040f, 0.095f, 0.085f, 0.97f);
        private static readonly Color CardBgLocked = new Color(0.035f, 0.050f, 0.065f, 0.95f);
        private static readonly Color PanelBg = new Color(0.025f, 0.045f, 0.062f, 0.94f);
        private static readonly Color Dim = new Color(0.46f, 0.55f, 0.61f, 1f);

        private float _rowH = 84f, _cardH = 74f;

        private static float CenterW => WinW - Pad * 2f - LeftW - RightW - Gap * 2f;
        private static float ColW => (CenterW - LaneHeadW - 8f) / 4f;

        // ==================== СОСТОЯНИЕ ====================

        private Canvas _canvas;
        private GameObject _dimmer, _window;
        private TechTreeBackdrop _backdrop;
        private bool _built, _isOpen;

        private static TechBranch s_branch = TechBranch.Physics;
        private Technology _selected;
        private string _search = "";
        private string _lastSig = "";
        private float _lastClickTime;
        private Technology _lastClickTech;

        // Шапка
        private Text _headerSub;
        private InputField _searchInput;

        // Вкладки веток и уровни
        private class TabUi
        {
            public TechBranch Branch;
            public Image Bg;
            public LiquidGlassEffect Fx;
            public Text Name, Motto, Count;
            public RectTransform Bar;
        }
        private readonly List<TabUi> _tabs = new List<TabUi>();
        private readonly List<Text> _tierLabels = new List<Text>();

        // Дерево
        private ScrollRect _treeScroll;
        private RectTransform _treeContent, _lanesLayer, _wiresLayer, _cardsLayer;
        private TechBranch _builtBranch;

        private class CardUi
        {
            public Technology Tech;
            public RectTransform Rt;
            public LiquidGlassEffect Fx;
            public CanvasGroup Group;
            public Text Status;
            public Image StateIcon, Accent, Bg, Icon;
            public Text Name;
            public RectTransform Bar;
            public GameObject BarHost, Frame;
            public TechCardFX Hover;
        }
        private readonly Dictionary<string, CardUi> _cards = new Dictionary<string, CardUi>();

        // Слоты
        private RectTransform _slotsList;
        private Text _slotsCount, _slotsFooter;
        private class SlotUi
        {
            public int Index;
            public RectTransform Bar;
            public Text Stats;
        }
        private readonly List<SlotUi> _slotUis = new List<SlotUi>();

        // Досье
        private RectTransform _detailList;
        private Image _detailBadge, _detailIcon, _pillBg;
        private LiquidGlassEffect _detailBadgeFx;
        private Text _detailName, _detailSub, _pillText;
        private Button _primaryBtn, _secondaryBtn;
        private Image _primaryImg, _secondaryImg;
        private Text _primaryLabel, _secondaryLabel;
        private System.Action _primaryAction, _secondaryAction;

        // ==================== ЖИЗНЕННЫЙ ЦИКЛ ====================

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() => Unsubscribe();

        public void Initialize(Canvas canvas)
        {
            Instance = this;
            _canvas = canvas;
            CleanupGhostObjects();
            Build();
            _dimmer.SetActive(false);
            _window.SetActive(false);
            _built = true;
        }

        private void CleanupGhostObjects()
        {
            string[] ghostNames = { "StellarisTechTree", "StellarisTechWindow", "TechTreePanel", "TechTreeDimmer" };
            var objs = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var obj in objs)
                if (obj != null && obj != gameObject && System.Array.IndexOf(ghostNames, obj.name) >= 0)
                    Destroy(obj);
        }

        public void Open()
        {
            if (!_built) return;
            _isOpen = true;
            MapModeController.HideGlobal();
            ToggleWorldNameplates(false);
            SFXManager.Play(Sfx.WindowOpen);

            LG.Show(_dimmer);
            LG.Show(_window);
            _dimmer.transform.SetAsLastSibling();
            _window.transform.SetAsLastSibling();
            FitToScreen();

            _backdrop.SetActive(true);
            _backdrop.SetBranch(TechBranchInfo.Key(s_branch));

            if (_selected == null || _selected.IsResearched) _selected = DefaultSelection();
            if (_selected != null) s_branch = TechBranchInfo.Of(_selected.Category);

            Subscribe();
            RebuildAll(resetScroll: true);
        }

        public void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;
            Unsubscribe();
            LG.Hide(_window);
            LG.Hide(_dimmer);
            _backdrop.SetActive(false);
            ToggleWorldNameplates(true);
            UIManager.Instance?.HideModalDimPublic();
            MapModeController.ShowGlobal();
        }

        /// <summary>Окно рассчитано на 1840×1010; на узких экранах — равномерно уменьшается.</summary>
        private void FitToScreen()
        {
            var canvasRt = (RectTransform)_canvas.transform;
            Vector2 size = canvasRt.rect.size;
            float k = Mathf.Min(1f, (size.x - 40f) / WinW, (size.y - 30f) / WinH);
            _window.transform.localScale = Vector3.one * Mathf.Max(0.5f, k);
        }

        private static void ToggleWorldNameplates(bool visible)
        {
            var nameplateCanvas = GameObject.Find("NameplateCanvas");
            if (nameplateCanvas == null) return;
            var cg = nameplateCanvas.GetComponent<CanvasGroup>();
            if (cg == null) cg = nameplateCanvas.AddComponent<CanvasGroup>();
            cg.alpha = visible ? 1f : 0f;
            cg.blocksRaycasts = visible;
        }

        private void Subscribe()
        {
            Unsubscribe();
            TechnologyManager.OnTechProgressUpdated += Refresh;
            TechnologyManager.OnTechCompleted += HandleTechCompleted;
            TechnologyManager.OnSlotsChanged += HandleSlotsChanged;
        }

        private void Unsubscribe()
        {
            TechnologyManager.OnTechProgressUpdated -= Refresh;
            TechnologyManager.OnTechCompleted -= HandleTechCompleted;
            TechnologyManager.OnSlotsChanged -= HandleSlotsChanged;
        }

        private void HandleTechCompleted(Technology t) => Refresh();
        private void HandleSlotsChanged(int n) => Refresh();

        // ==================== ПОСТРОЕНИЕ ОКНА ====================

        private void Build()
        {
            _dimmer = LGBuild.Panel(_canvas.transform, "TechTreeDimmer", new Color(0.005f, 0.015f, 0.020f, 0.88f), raycast: true).gameObject;
            ((RectTransform)_dimmer.transform).Stretch();
            LG.Scrim(_dimmer);
            LG.Motion(_dimmer, LGAppear.Kind.Fade).outDuration = 0.26f;
            var dimBtn = _dimmer.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Close);

            var win = LGBuild.Panel(_canvas.transform, "StellarisTechTree", UIManager.DS.BgDeep, raycast: true);
            _window = win.gameObject;
            win.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(WinW, WinH));
            _window.AddComponent<CanvasGroup>();
            LG.Glass(_window, 28f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.42f));
            var motion = LG.Motion(_window, LGAppear.Kind.Pop);
            motion.fromScale = 0.96f;
            motion.inDuration = 0.42f;

            // Процедурный фон (туманности, сетка, звёзды) под скруглением окна
            var mask = LG.RoundedMask(_window.transform, 27f, 1f);
            mask.SetAsFirstSibling();
            var bd = LGBuild.Rect(mask, "Backdrop");
            bd.Stretch();
            _backdrop = bd.gameObject.AddComponent<TechTreeBackdrop>();
            _backdrop.Initialize(bd);
            // Затемнение поверх фона: космос — лишь фактура, читаемость важнее
            var shade = LGBuild.Panel(mask, "Shade", new Color(0.012f, 0.022f, 0.032f, 0.62f));
            shade.rectTransform.Stretch();
            LG.Ignore(shade.gameObject);

            BuildHeader();
            BuildSlotsColumn();
            BuildCenter();
            BuildDetailColumn();
        }

        private void BuildHeader()
        {
            var head = LGBuild.Rect(_window.transform, "Header");
            head.TopBand(0, HeaderH);

            var badge = LGBuild.Panel(head, "Badge", PanelBg);
            badge.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(44, 44));
            var bfx = LG.Platter(badge.gameObject, 10f);
            bfx.SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.4f));
            bfx.FillMultiplier = 3f;
            LGIcons.Create(badge.transform, LGIcon.Research, 26, Cyan);

            var title = LGBuild.Label(head, "ИССЛЕДОВАНИЯ", 22, Primary, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.Stretch(78, 0, 520, 12);
            _headerSub = LGBuild.Label(head, "", 13, Muted, TextAnchor.LowerLeft);
            _headerSub.rectTransform.Stretch(78, 12, 440, 0);

            var line = LGBuild.Panel(head, "Line", new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f));
            line.rectTransform.anchorMin = new Vector2(0, 0);
            line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.offsetMin = new Vector2(24, 0);
            line.rectTransform.offsetMax = new Vector2(-24, 1.5f);
            LG.Line(line.gameObject, hairline: true);

            BuildSearch(head);

            var close = LGBuild.Button(head, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.3f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, "Закрыть  <color=#8AA2A8>(Esc)</color>");
        }

        private void BuildSearch(RectTransform head)
        {
            var box = LGBuild.Panel(head, "SearchBox", UIManager.DS.BgVisor, raycast: true);
            box.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-72, 0), new Vector2(300, 38));
            LG.Apply(box.gameObject, LiquidGlassEffect.Role.Field).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f));

            var ic = LGIcons.Create(box.transform, LGIcon.Target, 14, Muted);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(14, 14));

            var placeholder = LGBuild.Label(box.transform, "Поиск технологии или эффекта…", 13, new Color(Muted.r, Muted.g, Muted.b, 0.7f));
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.rectTransform.Stretch(34, 0, 12, 0);
            var input = LGBuild.Label(box.transform, "", 13, Primary);
            input.supportRichText = false;
            input.rectTransform.Stretch(34, 0, 12, 0);

            _searchInput = box.gameObject.AddComponent<InputField>();
            _searchInput.textComponent = input;
            _searchInput.placeholder = placeholder;
            _searchInput.onValueChanged.AddListener(OnSearchChanged);
        }

        private RectTransform Column(string name, float x0, float width)
        {
            var col = LGBuild.Panel(_window.transform, name, PanelBg, raycast: true);
            var rt = col.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.offsetMin = new Vector2(x0, Pad);
            rt.offsetMax = new Vector2(x0 + width, -HeaderH - 8f);
            var fx = LG.Platter(col.gameObject, 12f);
            fx.SetRim(new Color(1f, 1f, 1f, 0.10f));
            fx.FillMultiplier = 3f;
            fx.SpecularMultiplier = 0.25f;
            return rt;
        }

        private static Text ColumnTitle(RectTransform col, string text, LGIcon icon, Color tint)
        {
            var head = LGBuild.Rect(col, "Title");
            head.TopBand(12, 24, 16, 16);
            var ic = LGIcons.Create(head, icon, 16, tint);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16));
            var t = LGBuild.Label(head, text, 13, Primary, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch(24, 0, 0, 0);
            var right = LGBuild.Label(head, "", 13, Muted, TextAnchor.MiddleRight, bold: true);
            return right;
        }

        // ---------- Слоты ----------

        private void BuildSlotsColumn()
        {
            var col = Column("SlotsColumn", Pad, LeftW);
            _slotsCount = ColumnTitle(col, "НАУЧНЫЕ СЛОТЫ", LGIcon.Research, Cyan);

            var host = LGBuild.Rect(col, "ListHost");
            host.Stretch(6, 84, 6, 44);
            _slotsList = LGBuild.ScrollList(host, 8f, 6);

            var foot = LGBuild.Panel(col, "Footer", CardBg);
            foot.rectTransform.anchorMin = new Vector2(0, 0);
            foot.rectTransform.anchorMax = new Vector2(1, 0);
            foot.rectTransform.pivot = new Vector2(0.5f, 0);
            foot.rectTransform.offsetMin = new Vector2(10, 10);
            foot.rectTransform.offsetMax = new Vector2(-10, 76);
            var ffx = LG.Platter(foot.gameObject, 8f);
            ffx.SetRim(new Color(1f, 1f, 1f, 0.07f));
            ffx.FillMultiplier = 3f;
            _slotsFooter = LGBuild.Label(foot.transform, "", 11, Muted, TextAnchor.MiddleLeft, wrap: true);
            _slotsFooter.rectTransform.Stretch(12, 4, 10, 4);
        }

        // ---------- Центр: вкладки, уровни, дерево ----------

        private void BuildCenter()
        {
            float x0 = Pad + LeftW + Gap;
            var center = LGBuild.Rect(_window.transform, "Center");
            center.anchorMin = new Vector2(0, 0);
            center.anchorMax = new Vector2(0, 1);
            center.pivot = new Vector2(0, 1);
            center.offsetMin = new Vector2(x0, Pad);
            center.offsetMax = new Vector2(x0 + CenterW, -HeaderH - 8f);

            var tabs = LGBuild.Rect(center, "Tabs");
            tabs.TopBand(0, TabsH);
            float tabW = (CenterW - 20f) / 3f;
            for (int i = 0; i < TechBranchInfo.All.Length; i++)
                BuildTab(tabs, TechBranchInfo.All[i], i * (tabW + 10f), tabW);

            var tiers = LGBuild.Rect(center, "Tiers");
            tiers.TopBand(TabsH + 8f, TierHeadH);
            for (int t = 0; t < 4; t++)
            {
                var cell = LGBuild.Rect(tiers, "Tier" + t);
                cell.anchorMin = new Vector2(0, 0);
                cell.anchorMax = new Vector2(0, 1);
                cell.pivot = new Vector2(0, 0.5f);
                cell.sizeDelta = new Vector2(ColW, 0);
                cell.anchoredPosition = new Vector2(LaneHeadW + t * ColW, 0);
                var lbl = LGBuild.Label(cell, "", 12, Dim, TextAnchor.MiddleCenter, bold: true);
                lbl.raycastTarget = true;
                _tierLabels.Add(lbl);
                TooltipHelper.Attach(lbl.gameObject,
                    $"<b>{TierNames[t]}</b>\nСтоимость ×{TechnologyManager.TierCostFactor(t):0.#} к базовой.\n" +
                    "Технологии, опережающие свой год, изучаются дольше.");
            }

            var host = LGBuild.Rect(center, "TreeHost");
            host.Stretch(0, 0, 0, TabsH + 8f + TierHeadH + 4f);
            host.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 14);
            var hit = host.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);

            _treeScroll = host.gameObject.AddComponent<ScrollRect>();
            _treeScroll.horizontal = false;
            _treeScroll.vertical = true;
            _treeScroll.movementType = ScrollRect.MovementType.Elastic;
            _treeScroll.elasticity = 0.08f;
            _treeScroll.inertia = true;
            _treeScroll.decelerationRate = 0.12f;
            _treeScroll.scrollSensitivity = 40f;

            _treeContent = LGBuild.Rect(host, "Content");
            _treeContent.anchorMin = new Vector2(0, 1);
            _treeContent.anchorMax = new Vector2(1, 1);
            _treeContent.pivot = new Vector2(0.5f, 1);
            _treeContent.sizeDelta = Vector2.zero;
            _treeScroll.viewport = host;
            _treeScroll.content = _treeContent;

            _lanesLayer = LGBuild.Rect(_treeContent, "Lanes");
            _lanesLayer.Stretch();
            _wiresLayer = LGBuild.Rect(_treeContent, "Wires");
            _wiresLayer.Stretch();
            _cardsLayer = LGBuild.Rect(_treeContent, "Cards");
            _cardsLayer.Stretch();
        }

        private void BuildTab(RectTransform parent, TechBranch b, float x, float w)
        {
            Color bc = TechBranchInfo.Color(b);
            var bg = LGBuild.Panel(parent, "Tab_" + b, PanelBg, raycast: true);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(w, 0);
            rt.anchoredPosition = new Vector2(x, 0);
            var btn = bg.gameObject.AddComponent<Button>();
            var fx = LG.Button(bg.gameObject, new Color(bc.r, bc.g, bc.b, 0.3f), 10f, animateScale: false);
            fx.FillMultiplier = 2.6f;
            fx.SpecularMultiplier = 0.3f;
            btn.onClick.AddListener(() => SwitchBranch(b));

            var ic = LGIcons.Create(rt, BranchIcon(b), 28, bc);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(28, 28));

            var tab = new TabUi { Branch = b, Bg = bg, Fx = fx };
            tab.Name = LGBuild.Label(rt, TechBranchInfo.Name(b), 16, Primary, TextAnchor.UpperLeft, bold: true);
            tab.Name.rectTransform.Stretch(58, 0, 120, 9);
            tab.Motto = LGBuild.Label(rt, TechBranchInfo.Motto(b), 12, Muted, TextAnchor.LowerLeft);
            tab.Motto.rectTransform.Stretch(58, 9, 16, 0);
            tab.Count = LGBuild.Label(rt, "", 15, bc, TextAnchor.UpperRight, bold: true);
            tab.Count.rectTransform.Stretch(0, 0, 16, 9);

            var barHost = LGBuild.Rect(rt, "Bar");
            barHost.anchorMin = barHost.anchorMax = new Vector2(1, 0);
            barHost.pivot = new Vector2(1, 0);
            barHost.anchorMin = new Vector2(0, 0);
            barHost.anchorMax = new Vector2(1, 0);
            barHost.pivot = new Vector2(0.5f, 0);
            barHost.offsetMin = new Vector2(10, 2);
            barHost.offsetMax = new Vector2(-10, 5);
            tab.Bar = LGBuild.Bar(barHost, bc, 0f, 3f);
            _tabs.Add(tab);
        }

        private static LGIcon BranchIcon(TechBranch b) => b switch
        {
            TechBranch.Physics => LGIcon.Reactor,
            TechBranch.Society => LGIcon.Society,
            _ => LGIcon.Gear
        };

        // ---------- Досье ----------

        private void BuildDetailColumn()
        {
            var col = Column("DetailColumn", WinW - Pad - RightW, RightW);

            _detailBadge = LGBuild.Panel(col, "Badge", CardBg);
            _detailBadge.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -16), new Vector2(64, 64));
            _detailBadgeFx = LG.Platter(_detailBadge.gameObject, 8f);
            _detailBadgeFx.FillMultiplier = 3f;
            _detailBadgeFx.SpecularMultiplier = 0.2f;
            _detailIcon = LGIcons.Create(_detailBadge.transform, LGIcon.Research, 38, Cyan);

            _detailName = LGBuild.Label(col, "", 19, Primary, TextAnchor.UpperLeft, bold: true, wrap: true);
            FitText(_detailName, 14);
            _detailName.rectTransform.TopBand(16, 48, 92, 16);
            _detailSub = LGBuild.Label(col, "", 12, Dim, TextAnchor.UpperLeft, bold: true);
            FitText(_detailSub, 9);
            _detailSub.rectTransform.TopBand(64, 18, 92, 16);

            _pillBg = LGBuild.Panel(col, "Pill", CardBg);
            _pillBg.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -92), new Vector2(RightW - 32, 28));
            var pfx = LG.Platter(_pillBg.gameObject, 6f);
            pfx.FillMultiplier = 3f;
            pfx.SpecularMultiplier = 0.2f;
            _pillText = LGBuild.Label(_pillBg.transform, "", 13, Primary, TextAnchor.MiddleCenter, bold: true);

            var host = LGBuild.Rect(col, "DetailHost");
            host.Stretch(6, 108, 6, 128);
            _detailList = LGBuild.ScrollList(host, 6f, 10);

            (_primaryBtn, _primaryImg, _primaryLabel) = ActionButton(col, "Primary", 56f, 40f, () => _primaryAction?.Invoke());
            (_secondaryBtn, _secondaryImg, _secondaryLabel) = ActionButton(col, "Secondary", 14f, 34f, () => _secondaryAction?.Invoke());
        }

        private static (Button, Image, Text) ActionButton(RectTransform col, string name, float bottom, float h, System.Action onClick)
        {
            var btn = LGBuild.Button(col, name, UIManager.DS.BtnPrimary, new Color(1f, 1f, 1f, 0.18f), onClick, null, " ", 13, 6f);
            var fx = btn.GetComponent<LiquidGlassEffect>();
            if (fx != null) { fx.SpecularMultiplier = 0.3f; fx.GlowMultiplier = 0.3f; fx.FillMultiplier = 2.2f; }
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(16, bottom);
            rt.offsetMax = new Vector2(-16, bottom + h);
            return (btn, btn.GetComponent<Image>(), btn.GetComponentInChildren<Text>());
        }

        // ==================== ОБНОВЛЕНИЕ ====================

        /// <summary>Подпись состояния: меняется — перестраиваем всё, иначе только живые полосы.</summary>
        private static string Signature(TechnologyManager tm)
        {
            var sb = new StringBuilder(128);
            sb.Append(tm.ResearchedCount).Append('|').Append(tm.Slots.Count);
            foreach (var s in tm.Slots)
            {
                sb.Append('|').Append(s.CurrentTech != null ? s.CurrentTech.Id : "-").Append(s.IsPaused ? "p" : "");
                foreach (var q in s.Queue) sb.Append(',').Append(q.Id);
            }
            if (TimeManager.Instance != null) sb.Append('|').Append(TimeManager.Instance.Year);
            return sb.ToString();
        }

        private void Refresh()
        {
            if (!_isOpen) return;
            var tm = TechnologyManager.Instance;
            if (tm == null) return;
            if (Signature(tm) != _lastSig) RebuildAll(resetScroll: false);
            else UpdateLive(tm);
        }

        private void RebuildAll(bool resetScroll)
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return;
            _lastSig = Signature(tm);

            if (_selected == null || !IsInBranch(_selected, s_branch)) _selected = DefaultSelection();

            Vector2 scroll = _treeContent.anchoredPosition;
            bool sameBranch = _builtBranch == s_branch && _cards.Count > 0;
            RebuildTree(tm);
            _treeContent.anchoredPosition = resetScroll || !sameBranch ? Vector2.zero : scroll;

            RefreshHeader(tm);
            RefreshTabs(tm);
            RefreshTiers();
            RebuildSlots(tm);
            RebuildDetail(tm);
            UpdateLive(tm);
        }

        private void UpdateLive(TechnologyManager tm)
        {
            foreach (var c in _cards.Values) UpdateCardStatus(c, tm);

            foreach (var su in _slotUis)
            {
                if (su.Index >= tm.Slots.Count) continue;
                var s = tm.Slots[su.Index];
                if (s.CurrentTech == null) continue;
                LGBuild.SetBar(su.Bar, s.ProgressNormalized);
                su.Stats.text = SlotStats(tm, s);
            }
            _slotsFooter.text = SlotsFooter(tm);
            RefreshHeader(tm);
            RefreshPill(tm);
        }

        private void RefreshHeader(TechnologyManager tm)
        {
            var vm = VictoryManager.Instance;
            string victory = vm != null
                ? $"  ·  Научная победа: <color={LGBuild.Hex(Green)}><b>{tm.ResearchedCount}</b></color> / {vm.ScienceRequired}"
                : "";
            _headerSub.text =
                $"Наука <color={LGBuild.Hex(Cyan)}><b>+{tm.MonthlyResearchIncome:0.#}</b></color>/мес" +
                $"  ·  Изучено <b>{tm.ResearchedCount}</b> из {tm.AllTechs.Count}" + victory;
        }

        private void RefreshTabs(TechnologyManager tm)
        {
            foreach (var tab in _tabs)
            {
                int total = 0, done = 0, active = 0, found = 0;
                foreach (var t in tm.AllTechs)
                {
                    if (TechBranchInfo.Of(t.Category) != tab.Branch) continue;
                    total++;
                    if (t.IsResearched) done++;
                    else if (tm.IsBeingResearched(t)) active++;
                    if (Matches(t)) found++;
                }
                bool on = tab.Branch == s_branch;
                Color bc = TechBranchInfo.Color(tab.Branch);
                tab.Bg.color = on ? new Color(0.03f + bc.r * 0.10f, 0.05f + bc.g * 0.10f, 0.07f + bc.b * 0.10f, 0.97f) : PanelBg;
                tab.Fx.SetRim(new Color(bc.r, bc.g, bc.b, on ? 0.75f : 0.12f));
                tab.Fx.GlowMultiplier = on ? 0.8f : 0f;
                tab.Name.color = on ? Primary : Muted;
                tab.Count.text = $"{done} / {total}";
                LGBuild.SetBar(tab.Bar, total > 0 ? done / (float)total : 0f);

                if (!string.IsNullOrEmpty(_search))
                    tab.Motto.text = found > 0 ? $"<color={LGBuild.Hex(Gold)}>Найдено: {found}</color>" : "Ничего не найдено";
                else if (active > 0)
                    tab.Motto.text = $"<color={LGBuild.Hex(Cyan)}>В работе: {active}</color>  ·  {TechBranchInfo.Motto(tab.Branch)}";
                else
                    tab.Motto.text = TechBranchInfo.Motto(tab.Branch);
            }
        }

        private static readonly int[] TierYears = { 2200, 2205, 2210, 2218 };

        private void RefreshTiers()
        {
            int year = TimeManager.Instance != null ? TimeManager.Instance.Year : 2200;
            for (int t = 0; t < _tierLabels.Count; t++)
            {
                bool early = year < TierYears[t];
                _tierLabels[t].text = t == 0
                    ? TierNames[t]
                    : $"{TierNames[t]}  <color={LGBuild.Hex(early ? Orange : Muted)}>· {TierYears[t]}</color>";
            }
        }

        // ==================== ДЕРЕВО ====================

        private static bool IsInBranch(Technology t, TechBranch b) => TechBranchInfo.Of(t.Category) == b;

        private void RebuildTree(TechnologyManager tm)
        {
            LGBuild.Clear(_lanesLayer);
            LGBuild.Clear(_wiresLayer);
            LGBuild.Clear(_cardsLayer);
            _cards.Clear();
            _builtBranch = s_branch;

            // Собираем дорожки и подбираем высоту строки так, чтобы ветка заполнила окно
            var lanes = new List<(TechCategory cat, List<Technology> techs, int rows)>();
            int totalRows = 0;
            foreach (var cat in TechBranchInfo.Lanes(s_branch))
            {
                var techs = new List<Technology>();
                int rows = 1;
                foreach (var t in tm.AllTechs)
                    if (t.Category == cat) { techs.Add(t); rows = Mathf.Max(rows, t.GridColumn + 1); }
                if (techs.Count == 0) continue;
                lanes.Add((cat, techs, rows));
                totalRows += rows;
            }
            if (lanes.Count == 0) return;
            float fixedH = lanes.Count * (LanePadV * 2f - RowGap) + (lanes.Count - 1) * LaneGap + 4f;
            _rowH = Mathf.Clamp(Mathf.Floor((TreeH - fixedH) / Mathf.Max(1, totalRows)), 74f, 116f);
            _cardH = Mathf.Min(_rowH - RowGap, 78f);

            float y = 0f;
            foreach (var (cat, techs, rows) in lanes)
            {
                float laneH = rows * _rowH - RowGap + LanePadV * 2f;
                BuildLane(cat, techs, y, laneH);
                float inset = (_rowH - RowGap - _cardH) * 0.5f;
                foreach (var t in techs)
                {
                    float cx = LaneHeadW + Mathf.Clamp(t.Tier, 0, 3) * ColW + (ColW - CardW) * 0.5f;
                    float cy = -(y + LanePadV + t.GridColumn * _rowH + inset);
                    _cards[t.Id] = BuildCard(t, new Vector2(cx, cy), tm);
                }
                y += laneH + LaneGap;
            }
            _treeContent.sizeDelta = new Vector2(0, Mathf.Max(0f, y - LaneGap + 4f));

            foreach (var c in _cards.Values)
                foreach (var req in c.Tech.RequiredTechIds)
                    if (_cards.TryGetValue(req, out var from)) DrawWire(from, c, tm);
        }

        private void BuildLane(TechCategory cat, List<Technology> techs, float y, float h)
        {
            Color cc = TechCategoryInfo.Color(cat);
            var lane = LGBuild.Panel(_lanesLayer, "Lane_" + cat, new Color(0.030f, 0.050f, 0.068f, 0.80f));
            var rt = lane.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(0, -y - h);
            rt.offsetMax = new Vector2(0, -y);
            var lfx = LG.Platter(lane.gameObject, 8f);
            lfx.SetRim(new Color(1f, 1f, 1f, 0.06f));
            lfx.FillMultiplier = 2.6f;
            lfx.SpecularMultiplier = 0.2f;

            // Тонкая цветная метка направления слева
            var mark = LGBuild.Panel(rt, "Mark", new Color(cc.r, cc.g, cc.b, 0.85f));
            mark.rectTransform.anchorMin = new Vector2(0, 0);
            mark.rectTransform.anchorMax = new Vector2(0, 1);
            mark.rectTransform.offsetMin = new Vector2(0, 10);
            mark.rectTransform.offsetMax = new Vector2(3, -10);
            LG.Ignore(mark.gameObject);

            int done = 0;
            foreach (var t in techs) if (t.IsResearched) done++;

            var group = LGBuild.Rect(rt, "Head");
            group.anchorMin = new Vector2(0, 0.5f);
            group.anchorMax = new Vector2(0, 0.5f);
            group.pivot = new Vector2(0, 0.5f);
            group.sizeDelta = new Vector2(LaneHeadW - 16f, 96f);
            group.anchoredPosition = new Vector2(12f, 0f);
            var ic = LGIcons.Create(group, LGIcons.ForTechCategory(cat), 24, cc);
            ic.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(24, 24));
            var nm = LGBuild.Label(group, TechCategoryInfo.Name(cat), 12, Primary, TextAnchor.MiddleCenter, bold: true, wrap: true);
            FitText(nm, 9);
            nm.rectTransform.TopBand(28, 34);
            var cnt = LGBuild.Label(group, $"{done} / {techs.Count}", 12, Dim, TextAnchor.MiddleCenter, bold: true);
            cnt.rectTransform.TopBand(64, 16);
            var barHost = LGBuild.Rect(group, "Bar");
            barHost.TopBand(84, 6, 14, 14);
            LGBuild.Bar(barHost, cc, done / (float)Mathf.Max(1, techs.Count), 3f);
        }

        private CardUi BuildCard(Technology t, Vector2 pos, TechnologyManager tm)
        {
            Color cc = TechCategoryInfo.Color(t.Category);
            var bg = LGBuild.Panel(_cardsLayer, "Card_" + t.Id, CardBg, raycast: true);
            var rt = bg.rectTransform;
            rt.At(new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(CardW, _cardH));

            var c = new CardUi { Tech = t, Rt = rt, Bg = bg };
            c.Group = bg.gameObject.AddComponent<CanvasGroup>();
            var btn = bg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            c.Fx = LG.Platter(bg.gameObject, 8f);
            c.Fx.FillMultiplier = 3f;
            c.Fx.SpecularMultiplier = 0.2f;
            c.Hover = bg.gameObject.AddComponent<TechCardFX>();
            var captured = t;
            btn.onClick.AddListener(() => OnCardClick(captured));

            // Рамка выбора
            var frame = LGBuild.Panel(rt, "Frame", new Color(1f, 1f, 1f, 0.02f));
            frame.rectTransform.Stretch(-3, -3, -3, -3);
            LG.Border(frame.gameObject, new Color(1f, 1f, 1f, 0.85f));
            c.Frame = frame.gameObject;
            c.Frame.SetActive(t == _selected);

            c.Accent = LGBuild.Panel(rt, "Accent", cc);
            c.Accent.rectTransform.anchorMin = new Vector2(0, 0);
            c.Accent.rectTransform.anchorMax = new Vector2(0, 1);
            c.Accent.rectTransform.offsetMin = new Vector2(0, 8);
            c.Accent.rectTransform.offsetMax = new Vector2(3, -8);
            LG.Ignore(c.Accent.gameObject);

            c.Icon = LGIcons.Create(rt, LGIcons.ForTechCategory(t.Category), 26, cc);
            c.Icon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(28, 0), new Vector2(26, 26));

            c.Name = LGBuild.Label(rt, t.Name, 13, Primary, TextAnchor.UpperLeft, bold: true, wrap: true);
            c.Name.lineSpacing = 0.92f;
            FitText(c.Name, 10);
            c.Name.rectTransform.Stretch(52, 26, 10, 8);

            c.Status = LGBuild.Label(rt, "", 12, Muted, TextAnchor.LowerLeft, wrap: true);
            FitText(c.Status, 9);
            c.Status.rectTransform.Stretch(52, 10, 26, _cardH - 28);

            c.StateIcon = LGIcons.Create(rt, LGIcon.Lock, 14, Muted);
            c.StateIcon.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-9, 11), new Vector2(14, 14));

            var barHost = LGBuild.Rect(rt, "Progress");
            barHost.anchorMin = new Vector2(0, 0);
            barHost.anchorMax = new Vector2(1, 0);
            barHost.pivot = new Vector2(0.5f, 0);
            barHost.offsetMin = new Vector2(52, 4);
            barHost.offsetMax = new Vector2(-10, 8);
            c.Bar = LGBuild.Bar(barHost, Cyan, 0f, 3f);
            c.BarHost = barHost.gameObject;

            TooltipHelper.Attach(bg.gameObject, CardTooltip(t, tm));
            UpdateCardStatus(c, tm);
            return c;
        }

        private static string CardTooltip(Technology t, TechnologyManager tm)
        {
            string tier = t.Tier == 0 ? "базовая" : $"уровень {Roman(t.Tier)}";
            return $"<b>{t.Name}</b>\n<color=#8AA2A8>{TechCategoryInfo.Name(t.Category)} · {tier}</color>\n" +
                   $"{TechConfirmDialog.ColorizeModifiers(t.Description)}\n\n" +
                   "<color=#8AA2A8>Щелчок — подробности, двойной щелчок — исследовать</color>";
        }

        private enum CardState { Researched, Researching, Queued, Available, Locked }

        private static CardState StateOf(Technology t, TechnologyManager tm, out ResearchSlot slot, out int queuePos)
        {
            slot = null;
            queuePos = -1;
            if (t.IsResearched) return CardState.Researched;
            foreach (var s in tm.Slots)
            {
                if (s.CurrentTech == t) { slot = s; return CardState.Researching; }
                int qi = s.Queue.IndexOf(t);
                if (qi >= 0) { slot = s; queuePos = qi; return CardState.Queued; }
            }
            return tm.IsTechAvailable(t) ? CardState.Available : CardState.Locked;
        }

        private void UpdateCardStatus(CardUi c, TechnologyManager tm)
        {
            var t = c.Tech;
            Color cc = TechCategoryInfo.Color(t.Category);
            var st = StateOf(t, tm, out var slot, out int qpos);
            bool searching = !string.IsNullOrEmpty(_search);

            Color rim, accent, bg = CardBg, nameCol = Primary, iconTint = cc;
            bool showBar = false;
            float progress = t.ProgressNormalized;
            LGIcon icon = LGIcon.Lock;
            Color iconCol = Dim;
            string status;

            switch (st)
            {
                case CardState.Researched:
                    rim = new Color(Green.r, Green.g, Green.b, 0.35f);
                    accent = Green;
                    bg = CardBgDone;
                    icon = LGIcon.Check; iconCol = Green;
                    status = $"<color={LGBuild.Hex(Green)}>Изучено</color>";
                    break;
                case CardState.Researching:
                    rim = new Color(Cyan.r, Cyan.g, Cyan.b, 0.85f);
                    accent = Cyan;
                    icon = slot.IsPaused ? LGIcon.Pause : LGIcon.Play; iconCol = slot.IsPaused ? Gold : Cyan;
                    showBar = true;
                    progress = slot.ProgressNormalized;
                    status = slot.IsPaused
                        ? $"<color={LGBuild.Hex(Gold)}>Пауза · {progress * 100f:0}%</color>"
                        : $"<color={LGBuild.Hex(Cyan)}>{progress * 100f:0}% · ~{TechnologyManager.FormatDays(tm.EstimateDays(t))}</color>";
                    break;
                case CardState.Queued:
                    rim = new Color(Gold.r, Gold.g, Gold.b, 0.6f);
                    accent = Gold;
                    icon = LGIcon.Clock; iconCol = Gold;
                    showBar = progress > 0.001f;
                    status = $"<color={LGBuild.Hex(Gold)}>Очередь {qpos + 1} · ~{TechnologyManager.FormatDays(tm.EstimateDays(t))}</color>";
                    break;
                case CardState.Available:
                    rim = new Color(1f, 1f, 1f, 0.16f);
                    accent = cc;
                    icon = LGIcon.Clock; iconCol = Dim;
                    showBar = progress > 0.001f;
                    float pen = tm.GetYearPenalty(t);
                    status = $"<color=#C9D6DD>~{TechnologyManager.FormatDays(tm.EstimateDays(t))}</color>" +
                             (pen > 1.01f ? $"  <color={LGBuild.Hex(Orange)}>×{pen:0.0} рано</color>" : "");
                    break;
                default:
                    rim = new Color(1f, 1f, 1f, 0.06f);
                    accent = new Color(0.30f, 0.36f, 0.42f, 1f);
                    bg = CardBgLocked;
                    nameCol = Dim;
                    iconTint = new Color(cc.r * 0.55f, cc.g * 0.55f, cc.b * 0.55f, 1f);
                    status = $"<color=#7F8E98>Нужно: {MissingReqName(t, tm)}</color>";
                    break;
            }

            float alpha = 1f;
            if (searching)
            {
                if (Matches(t)) rim = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
                else alpha = 0.25f;
            }

            c.Group.alpha = alpha;
            c.Bg.color = bg;
            c.Name.color = nameCol;
            c.Icon.color = iconTint;
            c.Accent.color = accent;
            c.StateIcon.sprite = LGIcons.Get(icon);
            c.StateIcon.color = iconCol;
            c.Status.text = status;
            c.BarHost.SetActive(showBar);
            if (showBar) LGBuild.SetBar(c.Bar, progress, st == CardState.Researching ? Cyan : new Color(Gold.r, Gold.g, Gold.b, 0.8f));
            c.Hover.Fx = c.Fx;
            c.Hover.SetBase(rim, st == CardState.Researching && !slot.IsPaused);
        }

        private static string MissingReqName(Technology t, TechnologyManager tm)
        {
            foreach (var id in t.RequiredTechIds)
            {
                var r = tm.FindTech(id);
                if (r != null && !r.IsResearched) return r.Name;
            }
            return "—";
        }

        private void DrawWire(CardUi from, CardUi to, TechnologyManager tm)
        {
            Color bc = TechBranchInfo.Color(s_branch);
            bool done = from.Tech.IsResearched;
            bool flowing = done && StateOf(to.Tech, tm, out _, out _) == CardState.Researching;
            Color col = done ? new Color(bc.r, bc.g, bc.b, to.Tech.IsResearched ? 0.75f : 0.55f) : WireIdle;

            Vector2 a = from.Rt.anchoredPosition + new Vector2(CardW, -_cardH * 0.5f);
            Vector2 b = to.Rt.anchoredPosition + new Vector2(0f, -_cardH * 0.5f);
            if (b.x <= a.x + 4f || Mathf.Abs(a.y - b.y) < 0.5f)
            {
                Segment(a, b, col, flowing);
                return;
            }
            float midX = b.x - (ColW - CardW) * 0.5f;
            Segment(a, new Vector2(midX, a.y), col, false);
            Segment(new Vector2(midX, a.y), new Vector2(midX, b.y), col, false);
            Segment(new Vector2(midX, b.y), b, col, flowing);
        }

        /// <summary>Провод — плоская линия в 2 px, без свечения: схема должна читаться, а не сиять.</summary>
        private void Segment(Vector2 a, Vector2 b, Color col, bool pulse)
        {
            var img = LGBuild.Panel(_wiresLayer, "Wire", col);
            LG.Ignore(img.gameObject);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            Vector2 d = b - a;
            float len = d.magnitude;
            rt.sizeDelta = new Vector2(len + 2f, 2f);
            rt.anchoredPosition = a - d.normalized;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);

            if (!pulse || len < 12f) return;
            var dot = LGBuild.Panel(rt, "Pulse", Color.white);
            dot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, 4f));
            dot.gameObject.AddComponent<WirePulseAnimator>().Init(dot.rectTransform, len, Color.Lerp(col, Color.white, 0.5f));
        }

        // ==================== СЛОТЫ ====================

        private void RebuildSlots(TechnologyManager tm)
        {
            LGBuild.Clear(_slotsList);
            _slotUis.Clear();
            _slotsCount.text = $"{tm.Slots.Count} / {tm.MaxSlots}";

            for (int i = 0; i < tm.Slots.Count; i++)
            {
                var s = tm.Slots[i];
                if (s.CurrentTech == null) BuildFreeSlot(i);
                else BuildBusySlot(i, s, tm);
            }

            // Закрытые слоты — и какие технологии их откроют
            var openers = new List<Technology>();
            foreach (var t in tm.AllTechs)
                if (t.BonusKey == "slot+1" && !t.IsResearched) openers.Add(t);
            openers.Sort((x, y) => x.Cost.CompareTo(y.Cost));
            for (int i = tm.Slots.Count, k = 0; i < tm.MaxSlots; i++, k++)
                BuildLockedSlot(i, k < openers.Count ? openers[k] : null);
        }

        private RectTransform SlotCard(string name, float h, Color tint, Color rim)
        {
            var bg = LGBuild.Panel(_slotsList, name, tint, raycast: true);
            LGBuild.Height(bg.gameObject, h);
            var fx = LG.Platter(bg.gameObject, 8f);
            fx.SetRim(rim);
            fx.FillMultiplier = 3f;
            fx.SpecularMultiplier = 0.2f;
            return bg.rectTransform;
        }

        private void BuildFreeSlot(int i)
        {
            var rt = SlotCard("Free" + i, 66f, CardBg, new Color(Cyan.r, Cyan.g, Cyan.b, 0.22f));
            var t = LGBuild.Label(rt, $"СЛОТ {i + 1}  ·  <color={LGBuild.Hex(Cyan)}>СВОБОДЕН</color>", 12, Dim, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.Stretch(14, 0, 10, 11);
            var hint = LGBuild.Label(rt, "Выберите технологию в дереве — двойной щелчок или «Исследовать».", 12, Muted, TextAnchor.UpperLeft, wrap: true);
            hint.rectTransform.Stretch(14, 4, 12, 31);
        }

        private void BuildLockedSlot(int i, Technology opener)
        {
            var rt = SlotCard("Locked" + i, 52f, CardBgLocked, new Color(1f, 1f, 1f, 0.05f));
            var ic = LGIcons.Create(rt, LGIcon.Lock, 15, Dim);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(15, 15));
            var t = LGBuild.Label(rt, $"СЛОТ {i + 1}  ·  ЗАКРЫТ", 12, Dim, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.Stretch(40, 0, 10, 8);
            var sub = LGBuild.Label(rt, opener != null ? $"Откроет: {opener.Name}" : "Откроется технологией", 11,
                new Color(Dim.r, Dim.g, Dim.b, 0.85f), TextAnchor.LowerLeft);
            sub.rectTransform.Stretch(40, 8, 10, 0);
            if (opener != null)
            {
                var b = rt.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => Reveal(opener));
            }
        }

        private void BuildBusySlot(int i, ResearchSlot s, TechnologyManager tm)
        {
            var t = s.CurrentTech;
            Color cc = TechCategoryInfo.Color(t.Category);
            float h = 112f + s.Queue.Count * 26f + (s.Queue.Count > 0 ? 6f : 0f);
            var rt = SlotCard("Slot" + i, h, CardBg,
                s.IsPaused ? new Color(Gold.r, Gold.g, Gold.b, 0.45f) : new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f));
            var sel = rt.gameObject.AddComponent<Button>();
            sel.transition = Selectable.Transition.None;
            sel.onClick.AddListener(() => Reveal(t));

            var lbl = LGBuild.Label(rt, $"СЛОТ {i + 1}" + (s.IsPaused ? $"  ·  <color={LGBuild.Hex(Gold)}>ПАУЗА</color>" : ""), 11, Dim, TextAnchor.UpperLeft, bold: true);
            lbl.rectTransform.Stretch(14, 0, 70, 10);

            int idx = i;
            var pause = LGBuild.Button(rt, "Pause", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.15f),
                () => { TechnologyManager.Instance?.ToggleSlotPause(idx); SFXManager.Play(Sfx.UiClick); },
                s.IsPaused ? LGIcon.Play : LGIcon.Pause, null, 8);
            ((RectTransform)pause.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -7), new Vector2(26, 26));
            TooltipHelper.Attach(pause.gameObject, s.IsPaused ? "Продолжить исследование" : "Приостановить слот: наука перейдёт к остальным");
            var cancel = LGBuild.Button(rt, "Cancel", UIManager.DS.BtnNeutral, new Color(Red.r, Red.g, Red.b, 0.3f),
                () => { TechnologyManager.Instance?.CancelCurrent(idx); SFXManager.Play(Sfx.UiBack); },
                LGIcon.Close, null, 8);
            ((RectTransform)cancel.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-9, -7), new Vector2(26, 26));
            TooltipHelper.Attach(cancel.gameObject, "Отменить. Накопленный прогресс сохранится в технологии.");

            var ic = LGIcons.Create(rt, LGIcons.ForTechCategory(t.Category), 26, cc);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(29, -53), new Vector2(26, 26));

            var name = LGBuild.Label(rt, t.Name, 14, Primary, TextAnchor.MiddleLeft, bold: true, wrap: true);
            name.lineSpacing = 0.92f;
            FitText(name, 11);
            name.rectTransform.TopBand(34, 38, 52, 12);

            var barHost = LGBuild.Rect(rt, "Bar");
            barHost.TopBand(78, 6, 14, 14);
            var su = new SlotUi { Index = i };
            su.Bar = LGBuild.Bar(barHost, s.IsPaused ? Gold : Cyan, s.ProgressNormalized, 4f);
            su.Stats = LGBuild.Label(rt, SlotStats(tm, s), 12, Primary, TextAnchor.UpperLeft);
            su.Stats.rectTransform.TopBand(88, 18, 14, 12);
            _slotUis.Add(su);

            for (int q = 0; q < s.Queue.Count; q++)
            {
                var qt = s.Queue[q];
                var row = LGBuild.Panel(rt, "Q" + q, new Color(0.02f, 0.035f, 0.05f, 1f), raycast: true);
                row.rectTransform.TopBand(114 + q * 26, 22, 10, 10);
                LG.Ignore(row.gameObject, includeChildren: false);
                var rb = row.gameObject.AddComponent<Button>();
                rb.transition = Selectable.Transition.None;
                rb.onClick.AddListener(() => Reveal(qt));

                var qn = LGBuild.Label(row.transform, $"<color={LGBuild.Hex(Gold)}>{q + 1}.</color>  {qt.Name}", 12, Primary, TextAnchor.MiddleLeft);
                FitText(qn, 9);
                qn.rectTransform.Stretch(8, 0, 92, 0);
                var qe = LGBuild.Label(row.transform, "~" + TechnologyManager.FormatDays(tm.EstimateDays(qt)), 11, Muted, TextAnchor.MiddleRight);
                qe.rectTransform.Stretch(0, 0, 28, 0);
                var rm = LGBuild.Button(row.transform, "Remove", UIManager.DS.BtnNeutral, new Color(Red.r, Red.g, Red.b, 0.3f),
                    () => { TechnologyManager.Instance?.RemoveFromQueue(qt); SFXManager.Play(Sfx.UiBack); }, LGIcon.Close, null, 6);
                ((RectTransform)rm.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-3, 0), new Vector2(18, 18));
            }
        }

        private static string SlotStats(TechnologyManager tm, ResearchSlot s)
        {
            if (s.CurrentTech == null) return "";
            string pct = $"<b>{s.ProgressNormalized * 100f:0}%</b>";
            if (s.IsPaused) return $"{pct}  ·  <color={LGBuild.Hex(Gold)}>остановлено</color>";
            float perDay = tm.DailyPointsPerSlot(Mathf.Max(1, tm.ActiveSlotCount)) / Mathf.Max(1f, tm.GetYearPenalty(s.CurrentTech));
            return $"{pct}  ·  ~{TechnologyManager.FormatDays(tm.EstimateDays(s.CurrentTech))}" +
                   $"  <color={LGBuild.Hex(Muted)}>· +{perDay:0.#}/день</color>";
        }

        private static string SlotsFooter(TechnologyManager tm)
        {
            int active = tm.ActiveSlotCount;
            float split = Mathf.Pow(Mathf.Max(1, active), TechnologyManager.SlotSplitExponent);
            return $"Наука: <color={LGBuild.Hex(Cyan)}><b>{tm.MonthlyResearchIncome:0.#}</b></color>/мес делится между " +
                   $"<b>{active}</b> активн. слотами. Каждый получает 1/{split:0.0} — " +
                   "параллельно быстрее в сумме, но каждая технология медленнее.";
        }

        // ==================== ДОСЬЕ ====================

        private void RebuildDetail(TechnologyManager tm)
        {
            LGBuild.Clear(_detailList);
            var t = _selected;
            if (t == null)
            {
                _detailName.text = "Выберите технологию";
                _detailSub.text = "";
                _detailIcon.sprite = LGIcons.Get(LGIcon.Research);
                _detailIcon.color = Muted;
                _detailBadgeFx.SetRim(new Color(1f, 1f, 1f, 0.15f));
                _pillBg.gameObject.SetActive(false);
                Paragraph("Щёлкните по карточке в дереве, чтобы увидеть, что даёт технология, сколько она стоит и что откроет дальше.", 13, Muted);
                SetButtons(tm);
                return;
            }

            Color cc = TechCategoryInfo.Color(t.Category);
            TechBranch br = TechBranchInfo.Of(t.Category);
            _detailName.text = t.Name;
            _detailSub.text = $"<color={LGBuild.Hex(TechBranchInfo.Color(br))}>{TechBranchInfo.Name(br)}</color>  ·  " +
                              $"{TechCategoryInfo.Name(t.Category)}  ·  {(t.Tier == 0 ? "БАЗОВАЯ" : "УРОВЕНЬ " + Roman(t.Tier))}";
            _detailIcon.sprite = LGIcons.Get(LGIcons.ForTechCategory(t.Category));
            _detailIcon.color = cc;
            _detailBadgeFx.SetRim(new Color(cc.r, cc.g, cc.b, 0.45f));
            _pillBg.gameObject.SetActive(true);

            Section("ЭФФЕКТ", LGIcon.Info, cc);
            Paragraph(TechConfirmDialog.ColorizeModifiers(t.Description), 14, Primary);

            Section("СТОИМОСТЬ", LGIcon.Clock, Gold);
            int year = TimeManager.Instance != null ? TimeManager.Instance.Year : 2200;
            float pen = tm.GetYearPenalty(t);
            Row(LGIcon.Research, Cyan, $"<b>{t.Cost:0}</b> очков науки  <color={LGBuild.Hex(Muted)}>(×{TechnologyManager.TierCostFactor(t.Tier):0.#} за уровень)</color>");
            if (!t.IsResearched)
                Row(LGIcon.Clock, Gold, $"Срок: <b>~{TechnologyManager.FormatDays(tm.EstimateDays(t))}</b>  <color={LGBuild.Hex(Muted)}>при {tm.MonthlyResearchIncome:0.#} науки/мес</color>");
            if (t.Progress > 0.5f && !t.IsResearched && StateOf(t, tm, out _, out _) != CardState.Researching)
                Row(LGIcon.Check, Green, $"Накоплено: {t.ProgressNormalized * 100f:0}% — прогресс не теряется");
            Row(LGIcon.Calendar, pen > 1.01f ? Orange : Muted,
                pen > 1.01f
                    ? $"Доступна с <b>{t.YearAvailable}</b> г. — сейчас {year}: <color={LGBuild.Hex(Orange)}>срок ×{pen:0.0}</color>"
                    : $"Время пришло: с {t.YearAvailable} г.");

            if (t.RequiredTechIds.Length > 0)
            {
                Section("ТРЕБУЕТ", LGIcon.Lock, Muted);
                foreach (var id in t.RequiredTechIds)
                {
                    var r = tm.FindTech(id);
                    if (r == null) continue;
                    string where = TechBranchInfo.Of(r.Category) != br ? $"  <color={LGBuild.Hex(Muted)}>· {TechBranchInfo.Name(TechBranchInfo.Of(r.Category))}</color>" : "";
                    LinkRow(r, r.IsResearched ? LGIcon.Check : LGIcon.Lock, r.IsResearched ? Green : Red, r.Name + where);
                }
            }

            var unlocks = new List<(LGIcon icon, Color col, string text, Technology link)>();
            if (t.BonusKey == "slot+1") unlocks.Add((LGIcon.Research, Cyan, "Дополнительный научный слот", null));
            if (t.BonusKey == "cns_dst_1") unlocks.Add((LGIcon.Destroyer, Gold, "Корпус: эсминец", null));
            var sdm = ShipDesignManager.Instance;
            if (sdm != null)
                foreach (var m in sdm.Modules.Values)
                    if (m.RequiredTechId == t.Id) unlocks.Add((ModuleVisuals.Icon(m), ModuleVisuals.Tint(m), "Модуль: " + m.Name, null));
            foreach (var other in tm.AllTechs)
                if (System.Array.IndexOf(other.RequiredTechIds, t.Id) >= 0)
                    unlocks.Add((LGIcons.ForTechCategory(other.Category), TechCategoryInfo.Color(other.Category), other.Name, other));
            if (unlocks.Count > 0)
            {
                Section("ОТКРЫВАЕТ", LGIcon.Star, Green);
                foreach (var u in unlocks)
                {
                    if (u.link != null) LinkRow(u.link, u.icon, u.col, u.text);
                    else Row(u.icon, u.col, u.text);
                }
            }

            if (TechFlavorDatabase.Has(t.Id))
            {
                Section("ДОСЬЕ", LGIcon.Info, Muted);
                var f = Paragraph(TechFlavorDatabase.Get(t.Id), 12, Muted);
                f.fontStyle = FontStyle.Italic;
            }

            SetButtons(tm);
        }

        private void RefreshPill(TechnologyManager tm)
        {
            if (_selected == null) return;
            var st = StateOf(_selected, tm, out var slot, out int qpos);
            (string text, Color col) = st switch
            {
                CardState.Researched => ("ИЗУЧЕНО", Green),
                CardState.Researching => (slot.IsPaused ? $"ПАУЗА  ·  {slot.ProgressNormalized * 100f:0}%" : $"ИССЛЕДУЕТСЯ  ·  {slot.ProgressNormalized * 100f:0}%", slot.IsPaused ? Gold : Cyan),
                CardState.Queued => ($"В ОЧЕРЕДИ  ·  №{qpos + 1}", Gold),
                CardState.Available => ("ДОСТУПНО", TechBranchInfo.Color(TechBranchInfo.Of(_selected.Category))),
                _ => ("ЗАБЛОКИРОВАНО", Red)
            };
            _pillText.text = text;
            _pillText.color = col;
            _pillBg.color = new Color(0.03f + col.r * 0.07f, 0.05f + col.g * 0.07f, 0.07f + col.b * 0.07f, 0.97f);
        }

        private void SetButtons(TechnologyManager tm)
        {
            _primaryAction = _secondaryAction = null;
            var t = _selected;
            if (t == null)
            {
                _primaryBtn.gameObject.SetActive(false);
                _secondaryBtn.gameObject.SetActive(false);
                return;
            }

            var st = StateOf(t, tm, out var slot, out _);
            switch (st)
            {
                case CardState.Researched:
                    SetPrimary("ТЕХНОЛОГИЯ ИЗУЧЕНА", UIManager.DS.BtnSuccess, false, null);
                    _secondaryBtn.gameObject.SetActive(false);
                    break;
                case CardState.Researching:
                    int idx = IndexOf(tm, slot);
                    SetPrimary(slot.IsPaused ? "ПРОДОЛЖИТЬ" : "ПРИОСТАНОВИТЬ", UIManager.DS.BtnPrimary, true,
                        () => { tm.ToggleSlotPause(idx); SFXManager.Play(Sfx.UiClick); });
                    SetSecondary("ОТМЕНИТЬ ИССЛЕДОВАНИЕ", UIManager.DS.BtnDanger,
                        () => { tm.CancelCurrent(idx); SFXManager.Play(Sfx.UiBack); });
                    break;
                case CardState.Queued:
                    SetPrimary("В ОЧЕРЕДИ", UIManager.DS.BtnNeutral, false, null);
                    SetSecondary("УБРАТЬ ИЗ ОЧЕРЕДИ", UIManager.DS.BtnDanger,
                        () => { tm.RemoveFromQueue(t); SFXManager.Play(Sfx.UiBack); });
                    break;
                case CardState.Available:
                    bool free = tm.HasFreeSlot;
                    bool canQueue = free || QueueHasRoom(tm);
                    SetPrimary(free ? "ИССЛЕДОВАТЬ" : canQueue ? "ПОСТАВИТЬ В ОЧЕРЕДЬ" : "ОЧЕРЕДИ ЗАПОЛНЕНЫ",
                        free ? UIManager.DS.BtnSuccess : UIManager.DS.BtnPrimary, canQueue, () => TryResearch(t));
                    _secondaryBtn.gameObject.SetActive(false);
                    break;
                default:
                    SetPrimary("СНАЧАЛА: " + MissingReqName(t, tm).ToUpper(), UIManager.DS.BtnDisabled, false, null);
                    _secondaryBtn.gameObject.SetActive(false);
                    break;
            }
            RefreshPill(tm);
        }

        private static bool QueueHasRoom(TechnologyManager tm)
        {
            foreach (var s in tm.Slots) if (s.CurrentTech != null && s.Queue.Count < 5) return true;
            return false;
        }

        private static int IndexOf(TechnologyManager tm, ResearchSlot slot)
        {
            for (int i = 0; i < tm.Slots.Count; i++) if (tm.Slots[i] == slot) return i;
            return -1;
        }

        private void SetPrimary(string text, Color tint, bool interactable, System.Action action)
        {
            _primaryBtn.gameObject.SetActive(true);
            _primaryLabel.text = text;
            _primaryImg.color = tint;
            _primaryBtn.interactable = interactable;
            _primaryLabel.color = interactable ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            _primaryAction = action;
        }

        private void SetSecondary(string text, Color tint, System.Action action)
        {
            _secondaryBtn.gameObject.SetActive(true);
            _secondaryLabel.text = text;
            _secondaryImg.color = tint;
            _secondaryAction = action;
        }

        // ---------- Строки досье ----------

        private void Section(string text, LGIcon icon, Color tint)
        {
            var row = LGBuild.Rect(_detailList, "Section");
            LGBuild.Height(row.gameObject, 32f);
            var ic = LGIcons.Create(row, icon, 13, tint);
            ic.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(2, 5), new Vector2(13, 13));
            var t = LGBuild.Label(row, text, 12, Dim, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.Stretch(22, 4, 0, 0);
            var line = LGBuild.Panel(row, "Line", new Color(1f, 1f, 1f, 0.08f));
            line.rectTransform.anchorMin = new Vector2(0, 0);
            line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.offsetMin = Vector2.zero;
            line.rectTransform.offsetMax = new Vector2(0, 1f);
            LG.Ignore(line.gameObject);
        }

        private Text Paragraph(string text, int size, Color col)
        {
            var t = LGBuild.Label(_detailList, text, size, col, TextAnchor.UpperLeft, wrap: true);
            t.lineSpacing = 1.05f;
            return t;
        }

        private RectTransform Row(LGIcon icon, Color col, string text)
        {
            var row = LGBuild.Rect(_detailList, "Row");
            LGBuild.Height(row.gameObject, 26f);
            var ic = LGIcons.Create(row, icon, 15, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(15, 15));
            var t = LGBuild.Label(row, text, 13, Primary, TextAnchor.MiddleLeft);
            FitText(t, 10);
            t.rectTransform.Stretch(26, 0, 0, 0);
            return row;
        }

        private void LinkRow(Technology target, LGIcon icon, Color col, string text)
        {
            var row = Row(icon, col, text + $"  <color={LGBuild.Hex(Cyan)}>›</color>");
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var b = row.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => Reveal(target));
        }

        // ==================== ДЕЙСТВИЯ ====================

        private void OnCardClick(Technology t)
        {
            float now = Time.unscaledTime;
            bool dbl = _lastClickTech == t && now - _lastClickTime < DoubleClick;
            _lastClickTech = t;
            _lastClickTime = now;

            if (dbl)
            {
                var tm = TechnologyManager.Instance;
                if (tm != null && StateOf(t, tm, out _, out _) == CardState.Available) TryResearch(t);
                return;
            }
            Select(t);
            SFXManager.Play(Sfx.UiClick);
        }

        private void Select(Technology t)
        {
            _selected = t;
            foreach (var c in _cards.Values) c.Frame.SetActive(c.Tech == t);
            var tm = TechnologyManager.Instance;
            if (tm != null) RebuildDetail(tm);
        }

        /// <summary>Показать технологию: переключить ветку при необходимости и выбрать её.</summary>
        private void Reveal(Technology t)
        {
            if (t == null) return;
            var b = TechBranchInfo.Of(t.Category);
            _selected = t;
            if (b != s_branch) SwitchBranch(b);
            else Select(t);
            if (_cards.TryGetValue(t.Id, out var c)) ScrollTo(c);
        }

        private void ScrollTo(CardUi c)
        {
            float viewH = ((RectTransform)_treeScroll.transform).rect.height;
            float top = -c.Rt.anchoredPosition.y;
            float cur = _treeContent.anchoredPosition.y;
            if (top < cur || top + _cardH > cur + viewH)
            {
                float maxY = Mathf.Max(0f, _treeContent.sizeDelta.y - viewH);
                _treeContent.anchoredPosition = new Vector2(0f, Mathf.Clamp(top - viewH * 0.4f, 0f, maxY));
            }
        }

        private void TryResearch(Technology t)
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return;
            bool free = tm.HasFreeSlot;
            _selected = t;
            if (tm.AssignTech(t)) SFXManager.Play(free ? Sfx.ResearchStart : Sfx.OrderQueue);
            else SFXManager.Play(Sfx.UiDenied);
            Refresh();
        }

        private void SwitchBranch(TechBranch b)
        {
            if (s_branch == b && _cards.Count > 0) return;
            s_branch = b;
            if (_selected != null && TechBranchInfo.Of(_selected.Category) != b) _selected = DefaultSelection();
            _backdrop.SetBranch(TechBranchInfo.Key(b));
            SFXManager.Play(Sfx.UiTab);
            RebuildAll(resetScroll: true);
        }

        /// <summary>По умолчанию — исследуемая технология ветки, иначе первая доступная.</summary>
        private Technology DefaultSelection()
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return null;
            Technology avail = null;
            foreach (var t in tm.AllTechs)
            {
                if (TechBranchInfo.Of(t.Category) != s_branch) continue;
                var st = StateOf(t, tm, out _, out _);
                if (st == CardState.Researching) return t;
                if (st == CardState.Available && avail == null) avail = t;
            }
            return avail;
        }

        private void OnSearchChanged(string val)
        {
            _search = (val ?? "").Trim().ToLowerInvariant();
            var tm = TechnologyManager.Instance;
            if (tm == null || !_isOpen) return;

            // В текущей ветке ничего нет, а в другой есть — переходим туда
            if (!string.IsNullOrEmpty(_search))
            {
                bool here = false;
                TechBranch? other = null;
                foreach (var t in tm.AllTechs)
                {
                    if (!Matches(t)) continue;
                    var b = TechBranchInfo.Of(t.Category);
                    if (b == s_branch) { here = true; break; }
                    if (other == null) other = b;
                }
                if (!here && other.HasValue) { s_branch = other.Value; _backdrop.SetBranch(TechBranchInfo.Key(s_branch)); RebuildAll(true); return; }
            }
            foreach (var c in _cards.Values) UpdateCardStatus(c, tm);
            RefreshTabs(tm);
        }

        private bool Matches(Technology t)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return t.Name.ToLowerInvariant().Contains(_search)
                || (t.Description != null && t.Description.ToLowerInvariant().Contains(_search))
                || TechCategoryInfo.Name(t.Category).ToLowerInvariant().Contains(_search);
        }

        /// <summary>Длинное название уменьшается, а не вылезает за карточку.</summary>
        private static void FitText(Text t, int minSize)
        {
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = minSize;
            t.resizeTextMaxSize = t.fontSize;
        }

        private static string Roman(int tier) => tier switch { 1 => "I", 2 => "II", 3 => "III", _ => tier.ToString() };

        // ==================== ЭФФЕКТЫ ====================

        /// <summary>Наведение на карточку: лёгкое увеличение и яркая кромка; изучаемая — пульсирует.</summary>
        public class TechCardFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public LiquidGlassEffect Fx;
            public Color BaseRim;
            public bool Pulse;
            private bool _hovered;
            private Color _applied = new Color(-1f, -1f, -1f, -1f);

            public void SetBase(Color rim, bool pulse)
            {
                BaseRim = rim;
                Pulse = pulse;
                _applied = new Color(-1f, -1f, -1f, -1f);
            }

            public void OnPointerEnter(PointerEventData e) => _hovered = true;
            public void OnPointerExit(PointerEventData e) => _hovered = false;

            private void Update()
            {
                float k = Time.unscaledDeltaTime * 14f;
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * (_hovered ? 1.02f : 1f), k);
                if (Fx == null) return;
                Color want = _hovered ? new Color(1f, 1f, 1f, 0.85f)
                    : Pulse ? new Color(BaseRim.r, BaseRim.g, BaseRim.b, 0.55f + Mathf.Sin(Time.unscaledTime * 4.5f) * 0.35f)
                    : BaseRim;
                if (want == _applied) return;   // не трогаем материал без нужды
                _applied = want;
                Fx.SetRim(want);
            }
        }

        /// <summary>Светящаяся «капля», бегущая по проводу к изучаемой технологии.</summary>
        public class WirePulseAnimator : MonoBehaviour
        {
            private RectTransform _rt;
            private float _length, _progress;
            private Color _color;
            private Image _img;

            public void Init(RectTransform rt, float length, Color color)
            {
                _rt = rt;
                _length = length;
                _color = color;
                _img = GetComponent<Image>();
                _progress = Random.Range(0f, 1f);
            }

            private void Update()
            {
                if (_rt == null) return;
                _progress += Time.unscaledDeltaTime * 0.7f;
                if (_progress > 1f) _progress -= 1f;
                _rt.anchoredPosition = new Vector2(_progress * _length, 0f);
                if (_img != null) _img.color = new Color(_color.r, _color.g, _color.b, Mathf.Sin(_progress * Mathf.PI) * 0.9f);
            }
        }
    }
}
