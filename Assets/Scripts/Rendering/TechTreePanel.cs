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
    /// Экран исследований — «ветви». Слева — все направления (ветви), сгруппированные по трём наукам
    /// (физика, общество, инженерия), со счётчиками; в центре — выбранная ветвь крупно: технологии — большие
    /// круглые узлы по колонкам уровней, связи — плавные кривые. Справа — досье выбранной технологии,
    /// снизу — полоса научных слотов с прогрессом и очередями.
    /// Щелчок — выбрать, двойной щелчок или кнопка — исследовать (или поставить в очередь).
    /// </summary>
    public class TechTreePanel : MonoBehaviour
    {
        public static TechTreePanel Instance { get; private set; }
        public bool IsOpen => _isOpen;

        // ==================== РАЗМЕТКА ====================

        private const float WinW = 1840f, WinH = 1010f;
        private const float HeaderH = 80f, Pad = 16f, Gap = 14f;
        private const float NavW = 300f, RightW = 390f, DockH = 96f, DockLabelW = 214f;
        private const float LaneHeadH = 96f, TierHeadH = 26f, TreeMargin = 16f;
        private const int TierCount = 5;
        private const float NodeD = 84f, NameH = 36f, StatusH = 30f;
        private const float DoubleClick = 0.4f;
        private static string[] TierNames => new[] { Loc.T("БАЗОВЫЕ"), Loc.T("УРОВЕНЬ I"), Loc.T("УРОВЕНЬ II"), Loc.T("УРОВЕНЬ III"), Loc.T("УРОВЕНЬ IV") };

        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Green => UIManager.DS.Green;
        private static Color Red => UIManager.DS.Red;
        private static readonly Color Orange = new Color(1f, 0.66f, 0.50f);
        private static readonly Color CardBg = new Color(0.045f, 0.075f, 0.100f, 0.97f);
        private static readonly Color CardBgDone = new Color(0.040f, 0.095f, 0.085f, 0.97f);
        private static readonly Color CardBgLocked = new Color(0.035f, 0.050f, 0.065f, 0.95f);
        private static readonly Color PanelBg = new Color(0.025f, 0.045f, 0.062f, 0.94f);
        private static readonly Color Dim = new Color(0.46f, 0.55f, 0.61f, 1f);

        /// <summary>Высота строки дерева (подбирается под число строк ветви) и отступ узла сверху в ячейке.</summary>
        private float _rowH = 180f, _nodeTop = 12f;
        private float _cardH => _rowH;

        private static float CenterX => Pad + NavW + Gap;
        private static float CenterW => WinW - Pad * 2f - NavW - RightW - Gap * 2f;
        private static float BodyTop => HeaderH + 8f;
        private static float BodyBottom => Pad + DockH + Gap;
        private static float CenterH => WinH - BodyTop - BodyBottom;
        private static float TreeTop => LaneHeadH + TierHeadH + 8f;
        private static float TreeH => CenterH - TreeTop - 12f;
        private static float ColW => (CenterW - TreeMargin * 2f) / TierCount;
        private static float CardW => ColW;

        // ==================== СОСТОЯНИЕ ====================

        private Canvas _canvas;
        private GameObject _dimmer, _window;
        private TechTreeBackdrop _backdrop;
        private bool _built, _isOpen;

        /// <summary>Открытая ветвь (направление). Наука (физика / общество / инженерия) — производная от неё.</summary>
        private static TechCategory s_lane = TechCategory.Reactor;
        private static TechBranch s_branch => TechBranchInfo.Of(s_lane);
        private Technology _selected;
        private string _search = "";
        private string _lastSig = "";
        private float _lastClickTime;
        private Technology _lastClickTech;

        // Шапка
        private Text _headerSub;
        private InputField _searchInput;

        // Ветви слева, шапка ветви и уровни
        private RectTransform _navList;
        private Text _laneCap, _laneTitle, _laneSub;
        private Image _laneArt, _laneGlow;
        private readonly List<Text> _tierLabels = new List<Text>();

        // Дерево
        private ScrollRect _treeScroll;
        private RectTransform _treeContent, _lanesLayer, _wiresLayer, _cardsLayer;
        private TechCategory _builtLane = (TechCategory)(-1);

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
            public Image Disc, Glow;   // узел и его свечение (Accent — кольцо прогресса)
        }

        private readonly Dictionary<string, CardUi> _cards = new Dictionary<string, CardUi>();

        // Слоты (полоса снизу)
        private RectTransform _slotsList;
        private Text _slotsCount;
        private TooltipTrigger _slotsTip;
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
        private MenuPrimaryButton _primaryBtn;
        private Button _secondaryBtn;
        private Image _secondaryImg;
        private Text _secondaryLabel;
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

            // Открываем там, где идёт работа: ветвь изучаемой технологии
            if (_selected == null || _selected.IsResearched)
            {
                var busy = FirstResearching();
                if (busy != null) s_lane = busy.Category;
                _selected = busy ?? DefaultSelection();
            }
            if (_selected != null) s_lane = _selected.Category;
            _backdrop.SetBranch(TechBranchInfo.Key(s_branch));

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
            BuildNav();
            BuildCenter();
            BuildDetailColumn();
            BuildDock();
        }

        private void BuildHeader()
        {
            var head = LGBuild.Rect(_window.transform, "Header");
            head.TopBand(0, HeaderH);

            var badge = LGBuild.Panel(head, "Badge", PanelBg);
            badge.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(46, 46));
            var bfx = LG.Platter(badge.gameObject, 10f);
            bfx.SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.4f));
            bfx.FillMultiplier = 3f;
            LGIcons.Create(badge.transform, LGIcon.Research, 26, Cyan);

            _headerSub = LGBuild.Label(head, "", 12, Cyan, TextAnchor.UpperLeft, bold: true);
            _headerSub.rectTransform.Stretch(78, 0, 440, 16);
            _headerSub.Track(2.2f);
            var title = LGBuild.Label(head, Loc.T("Исследования"), 32, Primary, TextAnchor.LowerLeft, bold: true);
            title.rectTransform.Stretch(78, 12, 520, 0);

            var line = LGBuild.Panel(head, "Line", new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f));
            line.rectTransform.anchorMin = new Vector2(0, 0);
            line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.offsetMin = new Vector2(24, 0);
            line.rectTransform.offsetMax = new Vector2(-24, 1f);
            LG.Ignore(line.gameObject);

            BuildSearch(head);

            var close = LGControls.IconButton(head, LGIcon.Close, 40, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.3f), Close);
            ((RectTransform)close.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(40, 40));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc)</color>"));
        }

        private void BuildSearch(RectTransform head)
        {
            var box = LGBuild.Panel(head, "SearchBox", UIManager.DS.BgVisor, raycast: true);
            box.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-72, 0), new Vector2(300, 38));
            LG.Apply(box.gameObject, LiquidGlassEffect.Role.Field).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f));

            var ic = LGIcons.Create(box.transform, LGIcon.Target, 14, Muted);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(14, 14));

            var placeholder = LGBuild.Label(box.transform, Loc.T("Поиск технологии или эффекта…"), 13, new Color(Muted.r, Muted.g, Muted.b, 0.7f));
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
            rt.offsetMin = new Vector2(x0, BodyBottom);
            rt.offsetMax = new Vector2(x0 + width, -BodyTop);
            var fx = LG.Platter(col.gameObject, 14f);
            fx.SetRim(new Color(1f, 1f, 1f, 0.10f));
            fx.FillMultiplier = 3f;
            fx.SpecularMultiplier = 0.25f;
            return rt;
        }

        // ---------- Ветви слева ----------

        private void BuildNav()
        {
            var col = LGBuild.Rect(_window.transform, "Nav");
            col.anchorMin = new Vector2(0, 0);
            col.anchorMax = new Vector2(0, 1);
            col.pivot = new Vector2(0, 1);
            col.offsetMin = new Vector2(Pad, BodyBottom);
            col.offsetMax = new Vector2(Pad + NavW, -BodyTop);
            _navList = LGBuild.ScrollList(col, 4f, 0);
        }

        /// <summary>Список ветвей: заголовок науки, под ним её направления со счётчиком «изучено / всего».</summary>
        private void RefreshNav(TechnologyManager tm)
        {
            LGBuild.Clear(_navList);
            bool searching = !string.IsNullOrEmpty(_search);
            foreach (var b in TechBranchInfo.All)
            {
                Color bc = TechBranchInfo.Color(b);
                int bTotal = 0, bDone = 0;
                foreach (var t in tm.AllTechs)
                    if (TechBranchInfo.Of(t.Category) == b) { bTotal++; if (t.IsResearched) bDone++; }

                var head = LGBuild.Rect(_navList, "Branch_" + b);
                LGBuild.Height(head.gameObject, 34f);
                var hn = LGBuild.Label(head, TechBranchInfo.Name(b).ToUpper(), 12, bc, TextAnchor.LowerLeft, bold: true);
                hn.rectTransform.Stretch(8, 4, 60, 0);
                hn.Track(2.4f);
                var hc = LGBuild.Label(head, $"{bDone} / {bTotal}", 11, Dim, TextAnchor.LowerRight, bold: true);
                hc.rectTransform.Stretch(0, 4, 12, 0);

                foreach (var cat in TechBranchInfo.Lanes(b))
                {
                    int total = 0, done = 0, active = 0, found = 0;
                    foreach (var t in tm.AllTechs)
                    {
                        if (t.Category != cat) continue;
                        total++;
                        if (t.IsResearched) done++;
                        else if (tm.IsBeingResearched(t)) active++;
                        if (searching && Matches(t)) found++;
                    }
                    if (total == 0) continue;
                    bool on = cat == s_lane;
                    Color cc = TechCategoryInfo.Color(cat);

                    var row = LGBuild.Panel(_navList, "Lane_" + cat, on ? new Color(bc.r * 0.22f, bc.g * 0.22f, bc.b * 0.22f, 0.9f) : new Color(1f, 1f, 1f, 0f), raycast: true);
                    LGBuild.Height(row.gameObject, 50f);
                    if (on)
                    {
                        LG.Platter(row.gameObject, 10f).SetRim(new Color(bc.r, bc.g, bc.b, 0.35f));
                        var bar = LGBuild.Panel(row.transform, "Mark", bc);
                        bar.rectTransform.anchorMin = new Vector2(0, 0); bar.rectTransform.anchorMax = new Vector2(0, 1);
                        bar.rectTransform.offsetMin = new Vector2(0, 8); bar.rectTransform.offsetMax = new Vector2(3, -8);
                        LG.Ignore(bar.gameObject);
                    }
                    else LG.Ignore(row.gameObject, includeChildren: false);
                    var btn = row.gameObject.AddComponent<Button>();
                    btn.transition = Selectable.Transition.None;
                    var captured = cat;
                    btn.onClick.AddListener(() => SwitchLane(captured));

                    var art = LGIcons.TechArt(cat);
                    var ic = LGIcons.Create(row.transform, LGIcons.ForTechCategory(cat), art != null ? 32f : 22f, on ? Color.Lerp(cc, Color.white, 0.3f) : cc);
                    if (art != null) { ic.sprite = art; ic.preserveAspect = true; }
                    ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(32, 32));
                    var nm = LGBuild.Label(row.transform, TechCategoryInfo.Name(cat), 15, on ? Color.white : new Color(0.72f, 0.80f, 0.84f), TextAnchor.MiddleLeft, bold: true);
                    nm.rectTransform.Stretch(58, 0, 70, 0);
                    FitText(nm, 11);
                    string cnt = searching
                        ? (found > 0 ? Loc.F("<color={0}>найдено {1}</color>", LGBuild.Hex(Gold), found) : $"<color=#55656E>{done}/{total}</color>")
                        : active > 0 ? $"<color={LGBuild.Hex(Cyan)}>●</color> {done}/{total}" : $"{done}/{total}";
                    var cl = LGBuild.Label(row.transform, cnt, 12, Dim, TextAnchor.MiddleRight, bold: true);
                    cl.rectTransform.Stretch(0, 0, 14, 0);
                    TooltipHelper.Attach(row.gameObject, active > 0
                        ? Loc.F("<b>{0}</b>\nИзучено {1} из {2} · в работе {3}", TechCategoryInfo.Name(cat), done, total, active)
                        : Loc.F("<b>{0}</b>\nИзучено {1} из {2}", TechCategoryInfo.Name(cat), done, total));
                }
            }
        }

        // ---------- Центр: шапка ветви, уровни, дерево ----------

        private void BuildCenter()
        {
            var panel = LGBuild.Panel(_window.transform, "Center", new Color(0.015f, 0.028f, 0.045f, 0.82f), raycast: true);
            var center = panel.rectTransform;
            center.anchorMin = new Vector2(0, 0);
            center.anchorMax = new Vector2(0, 1);
            center.pivot = new Vector2(0, 1);
            center.offsetMin = new Vector2(CenterX, BodyBottom);
            center.offsetMax = new Vector2(CenterX + CenterW, -BodyTop);
            var cfx = LG.Platter(panel.gameObject, 16f);
            cfx.SetRim(new Color(1f, 1f, 1f, 0.08f));
            cfx.SpecularMultiplier = 0.2f;

            // Подсветка цветом науки и крупный рисунок ветви — фоном
            var cmask = LG.RoundedMask(center, 15f, 1f);
            _laneGlow = LGBuild.Panel(cmask, "Glow", Color.clear);
            _laneGlow.sprite = GlowSprite();
            _laneGlow.rectTransform.At(new Vector2(0.3f, 0.55f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CenterW * 1.1f, CenterH * 1.1f));
            LG.Ignore(_laneGlow.gameObject);
            _laneArt = LGBuild.Panel(cmask, "Art", Color.clear);
            _laneArt.preserveAspect = true;
            _laneArt.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -18), new Vector2(110, 110));
            LG.Ignore(_laneArt.gameObject);

            _laneCap = LGBuild.Label(center, "", 12, Cyan, TextAnchor.UpperLeft, bold: true);
            _laneCap.rectTransform.TopBand(24, 18, 30, 160);
            _laneCap.Track(2.4f);
            _laneTitle = LGBuild.Label(center, "", 30, Primary, TextAnchor.UpperLeft, bold: true);
            _laneTitle.rectTransform.TopBand(42, 38, 30, 160);
            _laneSub = LGBuild.Label(center, "", 13, Muted, TextAnchor.UpperLeft);
            _laneSub.rectTransform.TopBand(80, 18, 30, 160);

            var tiers = LGBuild.Rect(center, "Tiers");
            tiers.TopBand(LaneHeadH + 8f, TierHeadH, TreeMargin, TreeMargin);
            for (int t = 0; t < TierCount; t++)
            {
                var cell = LGBuild.Rect(tiers, "Tier" + t);
                cell.anchorMin = new Vector2(0, 0);
                cell.anchorMax = new Vector2(0, 1);
                cell.pivot = new Vector2(0, 0.5f);
                cell.sizeDelta = new Vector2(ColW, 0);
                cell.anchoredPosition = new Vector2(t * ColW, 0);
                var lbl = LGBuild.Label(cell, "", 11, Dim, TextAnchor.MiddleCenter, bold: true);
                lbl.raycastTarget = true;
                lbl.Track(1.4f);
                _tierLabels.Add(lbl);
                TooltipHelper.Attach(lbl.gameObject,
                    Loc.F("<b>{0}</b>\nСтоимость ×{1:0.#} к базовой.\n", TierNames[t], TechnologyManager.TierCostFactor(t)) +
                    Loc.T("Технологии, опережающие свой год, изучаются дольше."));
            }

            var host = LGBuild.Rect(center, "TreeHost");
            host.Stretch(TreeMargin, 12f, TreeMargin, TreeTop);
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

            _lanesLayer = LGBuild.Rect(_treeContent, "Guides");
            _lanesLayer.Stretch();
            _wiresLayer = LGBuild.Rect(_treeContent, "Wires");
            _wiresLayer.Stretch();
            _cardsLayer = LGBuild.Rect(_treeContent, "Nodes");
            _cardsLayer.Stretch();
        }

        // ---------- Слоты: полоса снизу ----------

        private void BuildDock()
        {
            var dock = LGBuild.Rect(_window.transform, "Dock");
            dock.anchorMin = new Vector2(0, 0);
            dock.anchorMax = new Vector2(1, 0);
            dock.pivot = new Vector2(0.5f, 0);
            dock.offsetMin = new Vector2(Pad, Pad);
            dock.offsetMax = new Vector2(-Pad, Pad + DockH);

            var label = LGBuild.Panel(dock, "Label", new Color(1f, 1f, 1f, 0f), raycast: true);
            label.rectTransform.anchorMin = new Vector2(0, 0); label.rectTransform.anchorMax = new Vector2(0, 1);
            label.rectTransform.pivot = new Vector2(0, 0.5f);
            label.rectTransform.offsetMin = new Vector2(0, 0); label.rectTransform.offsetMax = new Vector2(DockLabelW - 14f, 0);
            LG.Ignore(label.gameObject, includeChildren: false);
            var cap = LGBuild.Label(label.transform, Loc.T("НАУЧНЫЕ СЛОТЫ"), 12, Cyan, TextAnchor.LowerLeft, bold: true);
            cap.rectTransform.Stretch(6, DockH * 0.5f + 2f, 0, 0);
            cap.Track(2.4f);
            _slotsCount = LGBuild.Label(label.transform, "", 12, Muted, TextAnchor.UpperLeft);
            _slotsCount.rectTransform.Stretch(6, 0, 0, DockH * 0.5f + 4f);
            TooltipHelper.Attach(label.gameObject, "");
            _slotsTip = label.GetComponent<TooltipTrigger>();

            _slotsList = LGBuild.Rect(dock, "Slots");
            _slotsList.Stretch(DockLabelW, 0, 0, 0);
        }

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

            // Главное действие — яркая кнопка, как во всех меню
            _primaryBtn = MenuPrimaryButton.Create(col, " ", 15, () => _primaryAction?.Invoke());
            var prt = (RectTransform)_primaryBtn.transform;
            prt.anchorMin = new Vector2(0, 0);
            prt.anchorMax = new Vector2(1, 0);
            prt.pivot = new Vector2(0.5f, 0);
            prt.offsetMin = new Vector2(16, 56f);
            prt.offsetMax = new Vector2(-16, 56f + 44f);
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

            if (_selected == null || _selected.Category != s_lane) _selected = DefaultSelection();

            Vector2 scroll = _treeContent.anchoredPosition;
            bool sameLane = _builtLane == s_lane && _cards.Count > 0;
            RebuildTree(tm);
            _treeContent.anchoredPosition = resetScroll || !sameLane ? Vector2.zero : scroll;

            RefreshHeader(tm);
            RefreshNav(tm);
            RefreshLaneHead(tm);
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
            _slotsTip?.SetText(SlotsFooter(tm));
            RefreshHeader(tm);
            RefreshPill(tm);
        }

        private void RefreshHeader(TechnologyManager tm)
        {
            var vm = VictoryManager.Instance;
            string victory = vm != null
                ? Loc.F("  ·  НАУЧНАЯ ПОБЕДА <color={0}>{1}</color> / {2}", LGBuild.Hex(Green), tm.ResearchedCount, vm.ScienceRequired)
                : "";
            _headerSub.text =
                Loc.F("НАУКА <color={0}>+{1:0.#}</color> / МЕС", LGBuild.Hex(Green), tm.MonthlyResearchIncome) +
                Loc.F("  ·  ИЗУЧЕНО {0} ИЗ {1}", tm.ResearchedCount, tm.AllTechs.Count) + victory;
        }

        /// <summary>Шапка ветви: наука, название, «изучено N из M», рисунок и цвет подсветки.</summary>
        private void RefreshLaneHead(TechnologyManager tm)
        {
            Color bc = TechBranchInfo.Color(s_branch);
            Color cc = TechCategoryInfo.Color(s_lane);
            int total = 0, done = 0, active = 0;
            foreach (var t in tm.AllTechs)
            {
                if (t.Category != s_lane) continue;
                total++;
                if (t.IsResearched) done++;
                else if (tm.IsBeingResearched(t)) active++;
            }
            _laneCap.text = Loc.F("{0} · ВЕТВЬ", TechBranchInfo.Name(s_branch).ToUpper());
            _laneCap.color = bc;
            _laneTitle.text = TechCategoryInfo.Name(s_lane);
            _laneSub.text = Loc.F("Изучено {0} из {1}", done, total) + (active > 0 ? Loc.F("  ·  <color={0}>в работе {1}</color>", LGBuild.Hex(Cyan), active) : "")
                          + "  ·  " + TechBranchInfo.Motto(s_branch);
            var art = LGIcons.TechArt(s_lane);
            _laneArt.sprite = art;
            _laneArt.color = art != null ? new Color(cc.r, cc.g, cc.b, 0.22f) : Color.clear;
            _laneGlow.color = new Color(bc.r, bc.g, bc.b, 0.16f);
        }

        private static readonly int[] TierYears = { 2200, 2208, 2216, 2229, 2242 };

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

        private void RebuildTree(TechnologyManager tm)
        {
            LGBuild.Clear(_lanesLayer);
            LGBuild.Clear(_wiresLayer);
            LGBuild.Clear(_cardsLayer);
            _cards.Clear();
            _builtLane = s_lane;

            var techs = new List<Technology>();
            int rows = 1;
            foreach (var t in tm.AllTechs)
                if (t.Category == s_lane) { techs.Add(t); rows = Mathf.Max(rows, t.GridColumn + 1); }
            if (techs.Count == 0) return;

            // Строки делят высоту дерева; узел с подписью — по центру строки
            float block = NodeD + 10f + NameH + StatusH;
            _rowH = Mathf.Clamp(Mathf.Floor(TreeH / rows), block + 12f, 220f);
            _nodeTop = Mathf.Max(8f, (_rowH - block) * 0.5f);

            // Направляющие колонок уровней
            for (int c = 0; c < TierCount; c++)
            {
                var g = LGBuild.Panel(_lanesLayer, "Guide", new Color(1f, 1f, 1f, 0.035f));
                g.rectTransform.anchorMin = new Vector2(0, 0); g.rectTransform.anchorMax = new Vector2(0, 1);
                g.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                g.rectTransform.offsetMin = new Vector2(c * ColW + ColW * 0.5f - 0.5f, 0);
                g.rectTransform.offsetMax = new Vector2(c * ColW + ColW * 0.5f + 0.5f, 0);
                LG.Ignore(g.gameObject);
            }

            foreach (var t in techs)
            {
                float cx = Mathf.Clamp(t.Tier, 0, TierCount - 1) * ColW;
                float cy = -(t.GridColumn * _rowH);
                _cards[t.Id] = BuildCard(t, new Vector2(cx, cy), tm);
            }
            _treeContent.sizeDelta = new Vector2(0, rows * _rowH);

            foreach (var c in _cards.Values)
                foreach (var req in c.Tech.RequiredTechIds)
                    if (_cards.TryGetValue(req, out var from)) DrawWire(from, c, tm);
        }

        private Vector2 NodeCenterLocal => new Vector2(CardW * 0.5f, -(_nodeTop + NodeD * 0.5f));

        /// <summary>
        /// Узел ветви: прозрачная ячейка уровня (ловит клики); в ней — свечение, кольцо прогресса,
        /// стеклянный диск с рисунком направления и значок состояния, под ним — название и состояние.
        /// </summary>
        private CardUi BuildCard(Technology t, Vector2 pos, TechnologyManager tm)
        {
            Color cc = TechCategoryInfo.Color(t.Category);
            float N = NodeD;
            var bg = LGBuild.Panel(_cardsLayer, "Node_" + t.Id, new Color(0f, 0f, 0f, 0f), raycast: true);
            var rt = bg.rectTransform;
            rt.At(new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(CardW, _rowH));
            LG.Ignore(bg.gameObject, includeChildren: false);

            var c = new CardUi { Tech = t, Rt = rt, Bg = bg };
            c.Group = bg.gameObject.AddComponent<CanvasGroup>();
            var btn = bg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            var captured = t;
            btn.onClick.AddListener(() => OnCardClick(captured));

            var nc = NodeCenterLocal;
            c.Glow = Star(rt, "Glow", GlowSprite(), Color.clear, nc, N * 2.2f);
            Star(rt, "Track", RingSprite(), new Color(1f, 1f, 1f, 0.10f), nc, N + 12f);
            c.Accent = Star(rt, "Ring", RingSprite(), cc, nc, N + 12f);
            c.Accent.type = Image.Type.Filled;
            c.Accent.fillMethod = Image.FillMethod.Radial360;
            c.Accent.fillOrigin = (int)Image.Origin360.Top;
            c.Accent.fillClockwise = true;

            c.Disc = LGBuild.Panel(rt, "Disc", CardBg);
            c.Disc.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), nc, new Vector2(N, N));
            c.Fx = LG.Platter(c.Disc.gameObject, N * 0.5f);
            c.Fx.FillMultiplier = 3f;
            c.Fx.SpecularMultiplier = 0.6f;
            c.Hover = bg.gameObject.AddComponent<TechCardFX>();

            var art = LGIcons.TechArt(t.Category);
            c.Icon = LGIcons.Create(c.Disc.transform, LGIcons.ForTechCategory(t.Category), art != null ? N * 0.66f : N * 0.46f, cc);
            if (art != null) { c.Icon.sprite = art; c.Icon.preserveAspect = true; }
            c.StateIcon = LGIcons.Create(rt, LGIcon.Lock, 15, Muted);
            c.StateIcon.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), nc + new Vector2(N * 0.37f, -N * 0.37f), new Vector2(15, 15));

            // Рамка выбора — кольцо вокруг узла
            var frame = Star(rt, "Frame", RingSprite(), new Color(1f, 1f, 1f, 0.9f), nc, N + 26f);
            c.Frame = frame.gameObject;
            c.Frame.SetActive(t == _selected);

            float nameTop = _nodeTop + N + 10f;
            c.Name = LGBuild.Label(rt, t.Name, 13, Primary, TextAnchor.UpperCenter, bold: true, wrap: true);
            c.Name.lineSpacing = 0.92f;
            FitText(c.Name, 10);
            c.Name.rectTransform.TopBand(nameTop, NameH, 4, 4);

            c.Status = LGBuild.Label(rt, "", 11, Muted, TextAnchor.UpperCenter, wrap: true);
            FitText(c.Status, 9);
            c.Status.rectTransform.TopBand(nameTop + NameH, StatusH, 6, 6);

            // Линейная полоса не нужна — прогресс показывает кольцо
            var barHost = LGBuild.Rect(rt, "Progress");
            barHost.sizeDelta = Vector2.zero;
            c.Bar = LGBuild.Bar(barHost, Cyan, 0f, 3f);
            c.BarHost = barHost.gameObject;
            c.BarHost.SetActive(false);

            TooltipHelper.Attach(bg.gameObject, CardTooltip(t, tm));
            UpdateCardStatus(c, tm);
            return c;
        }

        /// <summary>Декоративный круглый слой узла (без лучей и стекла).</summary>
        private static Image Star(RectTransform parent, string name, Sprite sp, Color col, Vector2 center, float size)
        {
            var img = LGBuild.Panel(parent, name, col);
            img.sprite = sp;
            img.raycastTarget = false;
            img.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), center, new Vector2(size, size));
            LG.Ignore(img.gameObject);
            return img;
        }

        private static Sprite s_ring, s_glow;

        private static Sprite RingSprite()
        {
            if (s_ring != null) return s_ring;
            s_ring = CircleSprite(256, (d, half) => Mathf.Clamp01(2.6f * 0.5f + 0.5f - Mathf.Abs(d - (1f - 4f / half)) * half), "TechRing");
            return s_ring;
        }

        private static Sprite GlowSprite()
        {
            if (s_glow != null) return s_glow;
            s_glow = CircleSprite(128, (d, half) => { float k = Mathf.Clamp01(1f - d); return k * k * k; }, "TechGlow");
            return s_glow;
        }

        private static Sprite CircleSprite(int res, System.Func<float, float, float> alpha, string name)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = name, hideFlags = HideFlags.DontSave };
            var px = new Color32[res * res];
            float c = (res - 1) * 0.5f;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                px[y * res + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(d, c)) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            var sp = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
            sp.hideFlags = HideFlags.DontSave;
            return sp;
        }

        private static string CardTooltip(Technology t, TechnologyManager tm)
        {
            string tier = t.Tier == 0 ? Loc.T("базовая") : Loc.F("уровень {0}", Roman(t.Tier));
            return $"<b>{t.Name}</b>\n<color=#8AA2A8>{TechCategoryInfo.Name(t.Category)} · {tier}</color>\n" +
                   $"{TechConfirmDialog.ColorizeModifiers(t.Description)}\n\n" +
                   Loc.T("<color=#8AA2A8>Щелчок — подробности, двойной щелчок — исследовать</color>");
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
                    status = Loc.F("<color={0}>Изучено</color>", LGBuild.Hex(Green));
                    break;
                case CardState.Researching:
                    rim = new Color(Cyan.r, Cyan.g, Cyan.b, 0.85f);
                    accent = Cyan;
                    icon = slot.IsPaused ? LGIcon.Pause : LGIcon.Play; iconCol = slot.IsPaused ? Gold : Cyan;
                    showBar = true;
                    progress = slot.ProgressNormalized;
                    status = slot.IsPaused
                        ? Loc.F("<color={0}>Пауза · {1:0}%</color>", LGBuild.Hex(Gold), progress * 100f)
                        : $"<color={LGBuild.Hex(Cyan)}>{progress * 100f:0}% · ~{TechnologyManager.FormatDays(tm.EstimateDays(t))}</color>";
                    break;
                case CardState.Queued:
                    rim = new Color(Gold.r, Gold.g, Gold.b, 0.6f);
                    accent = Gold;
                    icon = LGIcon.Clock; iconCol = Gold;
                    showBar = progress > 0.001f;
                    status = Loc.F("<color={0}>Очередь {1} · ~{2}</color>", LGBuild.Hex(Gold), qpos + 1, TechnologyManager.FormatDays(tm.EstimateDays(t)));
                    break;
                case CardState.Available:
                    rim = new Color(1f, 1f, 1f, 0.16f);
                    accent = cc;
                    icon = LGIcon.Clock; iconCol = Dim;
                    showBar = progress > 0.001f;
                    float pen = tm.GetYearPenalty(t);
                    status = $"<color=#C9D6DD>~{TechnologyManager.FormatDays(tm.EstimateDays(t))}</color>" +
                             (pen > 1.01f ? Loc.F("  <color={0}>×{1:0.0} рано</color>", LGBuild.Hex(Orange), pen) : "");
                    break;
                default:
                    rim = new Color(1f, 1f, 1f, 0.06f);
                    accent = new Color(0.30f, 0.36f, 0.42f, 1f);
                    bg = CardBgLocked;
                    nameCol = Dim;
                    iconTint = new Color(cc.r * 0.55f, cc.g * 0.55f, cc.b * 0.55f, 1f);
                    status = Loc.F("<color=#7F8E98>Нужно: {0}</color>", MissingReqName(t, tm));
                    break;
            }

            float alpha = 1f;
            if (searching)
            {
                if (Matches(t)) rim = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
                else alpha = 0.25f;
            }

            c.Group.alpha = alpha;
            c.Disc.color = bg;
            c.Name.color = nameCol;
            c.Icon.color = iconTint;
            c.StateIcon.sprite = LGIcons.Get(icon);
            c.StateIcon.color = iconCol;
            c.Status.text = status;

            // Кольцо: изучено — полное, изучается/в очереди — доля прогресса; свечение — по состоянию
            c.Accent.color = st == CardState.Locked ? new Color(accent.r, accent.g, accent.b, 0.35f) : accent;
            c.Accent.fillAmount = st == CardState.Researched ? 1f : showBar ? progress : st == CardState.Available ? 1f : 0f;
            if (st == CardState.Available) c.Accent.color = new Color(accent.r, accent.g, accent.b, 0.45f);
            c.Glow.color = st switch
            {
                CardState.Researched => new Color(Green.r, Green.g, Green.b, 0.20f),
                CardState.Researching => new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f),
                CardState.Queued => new Color(Gold.r, Gold.g, Gold.b, 0.22f),
                CardState.Available => new Color(cc.r, cc.g, cc.b, 0.12f),
                _ => Color.clear
            };
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

        /// <summary>Связь: от правого края одного узла к левому краю другого; между строками — плавная S-кривая.</summary>
        private void DrawWire(CardUi from, CardUi to, TechnologyManager tm)
        {
            Color bc = TechBranchInfo.Color(s_branch);
            bool done = from.Tech.IsResearched;
            bool flowing = done && StateOf(to.Tech, tm, out _, out _) == CardState.Researching;
            Color col = done ? new Color(bc.r, bc.g, bc.b, to.Tech.IsResearched ? 0.85f : 0.6f)
                             : new Color(bc.r * 0.6f + 0.15f, bc.g * 0.6f + 0.15f, bc.b * 0.6f + 0.15f, 0.42f);

            float r = NodeD * 0.5f + 8f;
            Vector2 ca = from.Rt.anchoredPosition + NodeCenterLocal;
            Vector2 cb = to.Rt.anchoredPosition + NodeCenterLocal;
            float width = done ? 2.2f : 1.6f;

            if (cb.x - ca.x < r * 2f + 4f || Mathf.Abs(cb.y - ca.y) < 1f)
            {
                Vector2 dir = (cb - ca).normalized;
                Vector2 a = ca + dir * r, b = cb - dir * r;
                if ((b - a).sqrMagnitude < 4f) return;
                if (done) Segment(a, b, new Color(col.r, col.g, col.b, col.a * 0.22f), false, 7f);
                Segment(a, b, col, flowing, width);
                return;
            }

            // Кривая Безье: выходит из узла горизонтально и так же входит в следующий
            Vector2 p0 = ca + new Vector2(r, 0f), p3 = cb - new Vector2(r, 0f);
            float k = (p3.x - p0.x) * 0.5f;
            Vector2 p1 = p0 + new Vector2(k, 0f), p2 = p3 - new Vector2(k, 0f);
            const int Steps = 16;
            Vector2 prev = p0;
            for (int i = 1; i <= Steps; i++)
            {
                float u = i / (float)Steps, iu = 1f - u;
                Vector2 pt = iu * iu * iu * p0 + 3f * iu * iu * u * p1 + 3f * iu * u * u * p2 + u * u * u * p3;
                Segment(prev, pt, col, flowing && i == Steps / 2, width);
                prev = pt;
            }
        }

        /// <summary>Отрезок связи заданной толщины; pulse — бегущая к цели «капля».</summary>
        private void Segment(Vector2 a, Vector2 b, Color col, bool pulse, float width = 2f)
        {
            var img = LGBuild.Panel(_wiresLayer, "Wire", col);
            LG.Ignore(img.gameObject);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            Vector2 d = b - a;
            float len = d.magnitude;
            rt.sizeDelta = new Vector2(len, width);
            rt.anchoredPosition = a;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);

            if (!pulse || len < 12f) return;
            var dot = LGBuild.Panel(rt, "Pulse", Color.white);
            dot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 4f));
            dot.gameObject.AddComponent<WirePulseAnimator>().Init(dot.rectTransform, len, Color.Lerp(col, Color.white, 0.5f));
        }

        // ==================== СЛОТЫ ====================

        private void RebuildSlots(TechnologyManager tm)
        {
            LGBuild.Clear(_slotsList);
            _slotUis.Clear();
            _slotsCount.text = Loc.F("{0} из {1}  ·  наука <color={2}>+{3:0.#}</color> / мес", tm.Slots.Count, tm.MaxSlots, LGBuild.Hex(Green), tm.MonthlyResearchIncome);

            int total = Mathf.Max(1, tm.MaxSlots);
            for (int i = 0; i < tm.Slots.Count; i++)
            {
                var s = tm.Slots[i];
                if (s.CurrentTech == null) BuildFreeSlot(i, total);
                else BuildBusySlot(i, total, s, tm);
            }

            // Закрытые слоты — и какие технологии их откроют
            var openers = new List<Technology>();
            foreach (var t in tm.AllTechs)
                if (t.BonusKey == "slot+1" && !t.IsResearched) openers.Add(t);
            openers.Sort((x, y) => x.Cost.CompareTo(y.Cost));
            for (int i = tm.Slots.Count, k = 0; i < tm.MaxSlots; i++, k++)
                BuildLockedSlot(i, total, k < openers.Count ? openers[k] : null);
        }

        /// <summary>Карточка слота в полосе: n слотов делят ширину поровну.</summary>
        private RectTransform SlotCard(string name, int index, int total, Color tint, Color rim)
        {
            const float gap = 10f;
            var bg = LGBuild.Panel(_slotsList, name, tint, raycast: true);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(index / (float)total, 0);
            rt.anchorMax = new Vector2((index + 1) / (float)total, 1);
            rt.offsetMin = new Vector2(gap * index / total, 0);
            rt.offsetMax = new Vector2(-gap * (total - 1 - index) / total, 0);
            var fx = LG.Platter(bg.gameObject, 12f);
            fx.SetRim(rim);
            fx.FillMultiplier = 3f;
            fx.SpecularMultiplier = 0.2f;
            return rt;
        }

        private void BuildFreeSlot(int i, int total)
        {
            var rt = SlotCard("Free" + i, i, total, new Color(Cyan.r * 0.06f, Cyan.g * 0.06f, Cyan.b * 0.06f, 0.6f), new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f));
            var t = LGBuild.Label(rt, Loc.F("СЛОТ {0}  ·  СВОБОДЕН", i + 1), 11, Cyan, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.Stretch(16, 0, 10, 16);
            t.Track(1.4f);
            var hint = LGBuild.Label(rt, Loc.T("Выберите технологию — двойной щелчок или «Исследовать»"), 12, Muted, TextAnchor.UpperLeft, wrap: true);
            hint.rectTransform.Stretch(16, 8, 12, 38);
        }

        private void BuildLockedSlot(int i, int total, Technology opener)
        {
            var rt = SlotCard("Locked" + i, i, total, CardBgLocked, new Color(1f, 1f, 1f, 0.05f));
            var ic = LGIcons.Create(rt, LGIcon.Lock, 13, Dim);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -17), new Vector2(13, 13));
            var t = LGBuild.Label(rt, Loc.F("СЛОТ {0}", i + 1), 11, Dim, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.Stretch(36, 0, 10, 16);
            t.Track(1.4f);
            var sub = LGBuild.Label(rt, opener != null ? Loc.F("Откроет: {0}", opener.Name) : Loc.T("Откроется технологией"), 12,
                new Color(Dim.r, Dim.g, Dim.b, 0.85f), TextAnchor.UpperLeft, wrap: true);
            sub.rectTransform.Stretch(16, 8, 12, 40);
            if (opener != null)
            {
                var b = rt.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => Reveal(opener));
                TooltipHelper.Attach(rt.gameObject, Loc.F("<b>{0}</b>\nНажмите, чтобы показать технологию", opener.Name));
            }
        }

        private void BuildBusySlot(int i, int total, ResearchSlot s, TechnologyManager tm)
        {
            var t = s.CurrentTech;
            Color cc = TechCategoryInfo.Color(t.Category);
            var rt = SlotCard("Slot" + i, i, total, CardBg,
                s.IsPaused ? new Color(Gold.r, Gold.g, Gold.b, 0.5f) : new Color(Green.r, Green.g, Green.b, 0.45f));
            var sel = rt.gameObject.AddComponent<Button>();
            sel.transition = Selectable.Transition.None;
            sel.onClick.AddListener(() => Reveal(t));

            var art = LGIcons.TechArt(t.Category);
            var ic = LGIcons.Create(rt, LGIcons.ForTechCategory(t.Category), art != null ? 42f : 26f, cc);
            if (art != null) { ic.sprite = art; ic.preserveAspect = true; }
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 4), new Vector2(42, 42));

            var name = LGBuild.Label(rt, t.Name, 14, Primary, TextAnchor.UpperLeft, bold: true);
            FitText(name, 10);
            name.rectTransform.TopBand(12, 20, 64, 64);

            var barHost = LGBuild.Rect(rt, "Bar");
            barHost.TopBand(38, 6, 64, 14);
            var su = new SlotUi { Index = i };
            su.Bar = LGBuild.Bar(barHost, s.IsPaused ? Gold : Green, s.ProgressNormalized, 5f);
            if (!s.IsPaused) LGFx.Shimmer(su.Bar);
            su.Stats = LGBuild.Label(rt, SlotStats(tm, s), 11, Primary, TextAnchor.UpperLeft);
            FitText(su.Stats, 9);
            su.Stats.rectTransform.TopBand(50, 16, 64, 10);
            _slotUis.Add(su);

            // Очередь — одной строкой, полный список в подсказке
            if (s.Queue.Count > 0)
            {
                var q = LGBuild.Label(rt, Loc.F("<color={0}>очередь {1}</color> · дальше: {2}", LGBuild.Hex(Gold), s.Queue.Count, s.Queue[0].Name), 11, Muted, TextAnchor.UpperLeft);
                FitText(q, 9);
                q.rectTransform.TopBand(68, 16, 64, 10);
                var sb = new StringBuilder(Loc.T("<b>Очередь слота</b>"));
                for (int k = 0; k < s.Queue.Count; k++)
                    sb.Append($"\n{k + 1}. {s.Queue[k].Name} — ~{TechnologyManager.FormatDays(tm.EstimateDays(s.Queue[k]))}");
                sb.Append(Loc.T("\n\n<color=#8AA2A8>Убрать из очереди — в досье технологии</color>"));
                q.raycastTarget = true;
                TooltipHelper.Attach(q.gameObject, sb.ToString());
            }

            int idx = i;
            var pause = LGControls.IconButton(rt, s.IsPaused ? LGIcon.Play : LGIcon.Pause, 24, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.2f),
                () => { TechnologyManager.Instance?.ToggleSlotPause(idx); SFXManager.Play(Sfx.UiClick); });
            ((RectTransform)pause.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -8), new Vector2(24, 24));
            TooltipHelper.Attach(pause.gameObject, s.IsPaused ? Loc.T("Продолжить исследование") : Loc.T("Приостановить слот: наука перейдёт к остальным"));
            var cancel = LGControls.IconButton(rt, LGIcon.Close, 24, UIManager.DS.BgDeep, new Color(Red.r, Red.g, Red.b, 0.4f),
                () => { TechnologyManager.Instance?.CancelCurrent(idx); SFXManager.Play(Sfx.UiBack); });
            ((RectTransform)cancel.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -8), new Vector2(24, 24));
            TooltipHelper.Attach(cancel.gameObject, Loc.T("Отменить. Накопленный прогресс сохранится в технологии."));
        }

        private static string SlotStats(TechnologyManager tm, ResearchSlot s)
        {
            if (s.CurrentTech == null) return "";
            string pct = $"<b>{s.ProgressNormalized * 100f:0}%</b>";
            if (s.IsPaused) return Loc.F("{0}  ·  <color={1}>остановлено</color>", pct, LGBuild.Hex(Gold));
            float perDay = tm.DailyPointsPerSlot(Mathf.Max(1, tm.ActiveSlotCount)) / Mathf.Max(1f, tm.GetYearPenalty(s.CurrentTech));
            return $"{pct}  ·  ~{TechnologyManager.FormatDays(tm.EstimateDays(s.CurrentTech))}" +
                   Loc.F("  <color={0}>· +{1:0.#}/день</color>", LGBuild.Hex(Muted), perDay);
        }

        private static string SlotsFooter(TechnologyManager tm)
        {
            int active = tm.ActiveSlotCount;
            float split = Mathf.Pow(Mathf.Max(1, active), TechnologyManager.SlotSplitExponent);
            return Loc.F("Наука: <color={0}><b>{1:0.#}</b></color>/мес делится между ", LGBuild.Hex(Cyan), tm.MonthlyResearchIncome) +
                   Loc.F("<b>{0}</b> активн. слотами. Каждый получает 1/{1:0.0} — ", active, split) +
                   Loc.T("параллельно быстрее в сумме, но каждая технология медленнее.");
        }

        // ==================== ДОСЬЕ ====================

        private void RebuildDetail(TechnologyManager tm)
        {
            LGBuild.Clear(_detailList);
            var t = _selected;
            if (t == null)
            {
                _detailName.text = Loc.T("Выберите технологию");
                _detailSub.text = "";
                _detailIcon.sprite = LGIcons.Get(LGIcon.Research);
                _detailIcon.rectTransform.sizeDelta = new Vector2(38, 38);
                _detailIcon.color = Muted;
                _detailBadgeFx.SetRim(new Color(1f, 1f, 1f, 0.15f));
                _pillBg.gameObject.SetActive(false);
                Paragraph(Loc.T("Щёлкните по карточке в дереве, чтобы увидеть, что даёт технология, сколько она стоит и что откроет дальше."), 13, Muted);
                SetButtons(tm);
                return;
            }

            Color cc = TechCategoryInfo.Color(t.Category);
            TechBranch br = TechBranchInfo.Of(t.Category);
            _detailName.text = t.Name;
            _detailSub.text = $"<color={LGBuild.Hex(TechBranchInfo.Color(br))}>{TechBranchInfo.Name(br)}</color>  ·  " +
                              $"{TechCategoryInfo.Name(t.Category)}  ·  {(t.Tier == 0 ? Loc.T("БАЗОВАЯ") : Loc.T("УРОВЕНЬ ") + Roman(t.Tier))}";
            var detailArt = LGIcons.TechArt(t.Category);
            _detailIcon.sprite = detailArt ?? LGIcons.Get(LGIcons.ForTechCategory(t.Category));
            _detailIcon.rectTransform.sizeDelta = detailArt != null ? new Vector2(54, 54) : new Vector2(38, 38);
            _detailIcon.color = cc;
            _detailBadgeFx.SetRim(new Color(cc.r, cc.g, cc.b, 0.45f));
            _pillBg.gameObject.SetActive(true);

            Section(Loc.T("ЭФФЕКТ"), LGIcon.Info, cc);
            Paragraph(TechConfirmDialog.ColorizeModifiers(t.Description), 14, Primary);

            int year = TimeManager.Instance != null ? TimeManager.Instance.Year : 2200;
            float pen = tm.GetYearPenalty(t);
            Spacer(8f);
            Tiles(
                (Loc.T("СТОИМОСТЬ"), $"{t.Cost:0}", Loc.F("очков · ×{0:0.#} за уровень", TechnologyManager.TierCostFactor(t.Tier)), Primary,
                    Loc.F("<b>{0:0}</b> очков науки — ×{1:0.#} к базовой цене за уровень", t.Cost, TechnologyManager.TierCostFactor(t.Tier))),
                (Loc.T("СРОК"), t.IsResearched ? "—" : "~" + TechnologyManager.FormatDays(tm.EstimateDays(t)), Loc.F("при {0:0.#} / мес", tm.MonthlyResearchIncome), Primary,
                    Loc.F("Срок при нынешних {0:0.#} науки в месяц и занятых слотах", tm.MonthlyResearchIncome)),
                (Loc.T("ЭПОХА"), t.YearAvailable.ToString(), pen > 1.01f ? Loc.F("рано · срок ×{0:0.0}", pen) : Loc.T("время пришло"), pen > 1.01f ? Orange : Primary,
                    pen > 1.01f ? Loc.F("Доступна с {0} г. — сейчас {1}: изучение дольше в {2:0.0} раза", t.YearAvailable, year, pen) : Loc.F("Время пришло: с {0} г.", t.YearAvailable)));
            if (t.Progress > 0.5f && !t.IsResearched && StateOf(t, tm, out _, out _) != CardState.Researching)
                Row(LGIcon.Check, Green, Loc.F("Накоплено: {0:0}% — прогресс не теряется", t.ProgressNormalized * 100f));

            if (t.RequiredTechIds.Length > 0)
            {
                Section(Loc.T("ТРЕБУЕТ"), LGIcon.Lock, Muted);
                foreach (var id in t.RequiredTechIds)
                {
                    var r = tm.FindTech(id);
                    if (r == null) continue;
                    string where = TechBranchInfo.Of(r.Category) != br ? $"  <color={LGBuild.Hex(Muted)}>· {TechBranchInfo.Name(TechBranchInfo.Of(r.Category))}</color>" : "";
                    LinkRow(r, r.IsResearched ? LGIcon.Check : LGIcon.Lock, r.IsResearched ? Green : Red, r.Name + where);
                }
            }

            var unlocks = new List<(LGIcon icon, Color col, string text, Technology link)>();
            if (t.BonusKey == "slot+1") unlocks.Add((LGIcon.Research, Cyan, Loc.T("Дополнительный научный слот"), null));
            if (t.BonusKey == "cns_dst_1") unlocks.Add((LGIcon.Destroyer, Gold, Loc.T("Корпус: эсминец"), null));
            var sdm = ShipDesignManager.Instance;
            if (sdm != null)
                foreach (var m in sdm.Modules.Values)
                    if (m.RequiredTechId == t.Id) unlocks.Add((ModuleVisuals.Icon(m), ModuleVisuals.Tint(m), Loc.T("Модуль: ") + m.Name, null));
            foreach (var other in tm.AllTechs)
                if (System.Array.IndexOf(other.RequiredTechIds, t.Id) >= 0)
                    unlocks.Add((LGIcons.ForTechCategory(other.Category), TechCategoryInfo.Color(other.Category), other.Name, other));
            if (unlocks.Count > 0)
            {
                Section(Loc.T("ОТКРЫВАЕТ"), LGIcon.Star, Green);
                foreach (var u in unlocks)
                {
                    if (u.link != null) LinkRow(u.link, u.icon, u.col, u.text);
                    else Row(u.icon, u.col, u.text);
                }
            }

            if (TechFlavorDatabase.Has(t.Id))
            {
                Section(Loc.T("ДОСЬЕ"), LGIcon.Info, Muted);
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
                CardState.Researched => (Loc.T("ИЗУЧЕНО"), Green),
                CardState.Researching => (slot.IsPaused ? Loc.F("ПАУЗА  ·  {0:0}%", slot.ProgressNormalized * 100f) : Loc.F("ИССЛЕДУЕТСЯ  ·  {0:0}%", slot.ProgressNormalized * 100f), slot.IsPaused ? Gold : Cyan),
                CardState.Queued => (Loc.F("В ОЧЕРЕДИ  ·  №{0}", qpos + 1), Gold),
                CardState.Available => (Loc.T("ДОСТУПНО"), TechBranchInfo.Color(TechBranchInfo.Of(_selected.Category))),
                _ => (Loc.T("ЗАБЛОКИРОВАНО"), Red)
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
                    SetPrimary(Loc.T("ТЕХНОЛОГИЯ ИЗУЧЕНА"), MenuPrimaryButton.Green, false, null);
                    _secondaryBtn.gameObject.SetActive(false);
                    break;
                case CardState.Researching:
                    int idx = IndexOf(tm, slot);
                    SetPrimary(slot.IsPaused ? Loc.T("ПРОДОЛЖИТЬ") : Loc.T("ПРИОСТАНОВИТЬ"), MenuPrimaryButton.Cyan, true,
                        () => { tm.ToggleSlotPause(idx); SFXManager.Play(Sfx.UiClick); });
                    SetSecondary(Loc.T("ОТМЕНИТЬ ИССЛЕДОВАНИЕ"), UIManager.DS.BtnDanger,
                        () => { tm.CancelCurrent(idx); SFXManager.Play(Sfx.UiBack); });
                    break;
                case CardState.Queued:
                    SetPrimary(Loc.T("В ОЧЕРЕДИ"), MenuPrimaryButton.Cyan, false, null);
                    SetSecondary(Loc.T("УБРАТЬ ИЗ ОЧЕРЕДИ"), UIManager.DS.BtnDanger,
                        () => { tm.RemoveFromQueue(t); SFXManager.Play(Sfx.UiBack); });
                    break;
                case CardState.Available:
                    bool free = tm.HasFreeSlot;
                    bool canQueue = free || QueueHasRoom(tm);
                    SetPrimary(free ? Loc.T("ИССЛЕДОВАТЬ") : canQueue ? Loc.T("ПОСТАВИТЬ В ОЧЕРЕДЬ") : Loc.T("ОЧЕРЕДИ ЗАПОЛНЕНЫ"),
                        free ? MenuPrimaryButton.Green : MenuPrimaryButton.Cyan, canQueue, () => TryResearch(t));
                    _secondaryBtn.gameObject.SetActive(false);
                    break;
                default:
                    SetPrimary(Loc.T("СНАЧАЛА: ") + MissingReqName(t, tm).ToUpper(), MenuPrimaryButton.Cyan, false, null);
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
            _primaryBtn.SetLabel(text);
            _primaryBtn.SetColor(tint);
            _primaryBtn.Interactable = interactable;
            _primaryBtn.Label.color = interactable ? new Color(0.01f, 0.08f, 0.10f) : new Color(0.85f, 0.92f, 0.94f, 0.75f);
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
            LGBuild.SectionDecor(row, text, icon, tint);
        }

        private void Spacer(float h)
        {
            var r = LGBuild.Rect(_detailList, "Space");
            LGBuild.Height(r.gameObject, h);
        }

        /// <summary>Три плитки в ряд: подпись, крупное значение, пояснение (подсказка — подробнее).</summary>
        private void Tiles(params (string cap, string value, string sub, Color col, string tip)[] tiles)
        {
            var row = LGBuild.Rect(_detailList, "Tiles");
            LGBuild.Height(row.gameObject, 76f);
            int n = tiles.Length;
            const float gap = 8f;
            for (int i = 0; i < n; i++)
            {
                var (cap, value, sub, col, tip) = tiles[i];
                var tile = LGBuild.Panel(row, "Tile", new Color(1f, 1f, 1f, 0.035f), raycast: true);
                var rt = tile.rectTransform;
                rt.anchorMin = new Vector2(i / (float)n, 0); rt.anchorMax = new Vector2((i + 1) / (float)n, 1);
                rt.offsetMin = new Vector2(gap * i / n, 0); rt.offsetMax = new Vector2(-gap * (n - 1 - i) / n, 0);
                LG.Platter(tile.gameObject, 10f).SetRim(new Color(1f, 1f, 1f, 0.07f));
                var c = LGBuild.Label(rt, cap, 10, Muted, TextAnchor.UpperLeft, bold: true);
                c.rectTransform.TopBand(10, 14, 12, 6);
                c.Track(1.2f);
                var v = LGBuild.Label(rt, value, 19, col, TextAnchor.UpperLeft, bold: true);
                FitText(v, 12);
                v.rectTransform.TopBand(26, 24, 12, 6);
                var s2 = LGBuild.Label(rt, sub, 10, Muted, TextAnchor.UpperLeft);
                FitText(s2, 8);
                s2.rectTransform.TopBand(52, 16, 12, 6);
                TooltipHelper.Attach(tile.gameObject, tip);
            }
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
            _selected = t;
            if (t.Category != s_lane) SwitchLane(t.Category);
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

        private void SwitchLane(TechCategory lane)
        {
            if (s_lane == lane && _cards.Count > 0) return;
            s_lane = lane;
            if (_selected != null && _selected.Category != lane) _selected = DefaultSelection();
            _backdrop.SetBranch(TechBranchInfo.Key(s_branch));
            SFXManager.Play(Sfx.UiTab);
            RebuildAll(resetScroll: true);
        }

        /// <summary>По умолчанию — исследуемая технология ветви, иначе первая доступная, иначе первая в ветви.</summary>
        private Technology DefaultSelection()
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return null;
            Technology avail = null, first = null;
            foreach (var t in tm.AllTechs)
            {
                if (t.Category != s_lane) continue;
                if (first == null) first = t;
                var st = StateOf(t, tm, out _, out _);
                if (st == CardState.Researching) return t;
                if (st == CardState.Available && avail == null) avail = t;
            }
            return avail ?? first;
        }

        private static Technology FirstResearching()
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return null;
            foreach (var s in tm.Slots) if (s.CurrentTech != null) return s.CurrentTech;
            return null;
        }

        private void OnSearchChanged(string val)
        {
            _search = (val ?? "").Trim().ToLowerInvariant();
            var tm = TechnologyManager.Instance;
            if (tm == null || !_isOpen) return;

            // В открытой ветви ничего нет, а в другой есть — переходим туда
            if (!string.IsNullOrEmpty(_search))
            {
                bool here = false;
                TechCategory? other = null;
                foreach (var t in tm.AllTechs)
                {
                    if (!Matches(t)) continue;
                    if (t.Category == s_lane) { here = true; break; }
                    if (other == null) other = t.Category;
                }
                if (!here && other.HasValue) { s_lane = other.Value; _backdrop.SetBranch(TechBranchInfo.Key(s_branch)); RebuildAll(true); return; }
            }
            foreach (var c in _cards.Values) UpdateCardStatus(c, tm);
            RefreshNav(tm);
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

        private static string Roman(int tier) => tier switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => tier.ToString() };

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
