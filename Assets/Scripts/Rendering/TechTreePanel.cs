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

        private const float WinW = 1640f, WinH = 940f;
        private const float HeaderH = 66f, Pad = 14f, Gap = 12f;
        private const float LeftW = 300f, RightW = 350f;
        private const float TabsH = 58f, TierHeadH = 26f;
        private const float LaneHeadW = 120f, CardW = 186f, CardH = 58f, RowH = 68f;
        private const float LanePadV = 9f, LaneGap = 10f;
        private const float DoubleClick = 0.4f;
        private static readonly string[] TierNames = { "БАЗОВЫЕ", "УРОВЕНЬ I", "УРОВЕНЬ II", "УРОВЕНЬ III" };

        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Green => UIManager.DS.Green;
        private static Color Red => UIManager.DS.Red;
        private static readonly Color Orange = new Color(1f, 0.66f, 0.50f);
        private static readonly Color WireIdle = new Color(0.36f, 0.50f, 0.60f, 0.42f);

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
            public Image StateIcon, Accent;
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

            BuildHeader();
            BuildSlotsColumn();
            BuildCenter();
            BuildDetailColumn();
        }

        private void BuildHeader()
        {
            var head = LGBuild.Rect(_window.transform, "Header");
            head.TopBand(0, HeaderH);

            var badge = LGBuild.Panel(head, "Badge", new Color(0.04f, 0.16f, 0.18f, 1f));
            badge.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(42, 42));
            LG.Platter(badge.gameObject, 12f).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f));
            LGIcons.Create(badge.transform, LGIcon.Research, 26, Cyan);

            var title = LGBuild.Label(head, "ИССЛЕДОВАНИЯ", 20, Primary, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.Stretch(76, 0, 520, 12);
            _headerSub = LGBuild.Label(head, "", 11, Muted, TextAnchor.LowerLeft);
            _headerSub.rectTransform.Stretch(76, 12, 420, 0);

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
            box.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-70, 0), new Vector2(270, 36));
            LG.Apply(box.gameObject, LiquidGlassEffect.Role.Field).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f));

            var ic = LGIcons.Create(box.transform, LGIcon.Target, 14, Muted);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(14, 14));

            var placeholder = LGBuild.Label(box.transform, "Поиск технологии или эффекта…", 12, new Color(Muted.r, Muted.g, Muted.b, 0.7f));
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.rectTransform.Stretch(34, 0, 12, 0);
            var input = LGBuild.Label(box.transform, "", 12, Primary);
            input.supportRichText = false;
            input.rectTransform.Stretch(34, 0, 12, 0);

            _searchInput = box.gameObject.AddComponent<InputField>();
            _searchInput.textComponent = input;
            _searchInput.placeholder = placeholder;
            _searchInput.onValueChanged.AddListener(OnSearchChanged);
        }

        private RectTransform Column(string name, float x0, float width)
        {
            var col = LGBuild.Panel(_window.transform, name, new Color(0.02f, 0.05f, 0.07f, 0.74f), raycast: true);
            var rt = col.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.offsetMin = new Vector2(x0, Pad);
            rt.offsetMax = new Vector2(x0 + width, -HeaderH - 8f);
            LG.Platter(col.gameObject, 18f).SetRim(new Color(1f, 1f, 1f, 0.10f));
            return rt;
        }

        private static Text ColumnTitle(RectTransform col, string text, LGIcon icon, Color tint)
        {
            var head = LGBuild.Rect(col, "Title");
            head.TopBand(12, 22, 16, 16);
            var ic = LGIcons.Create(head, icon, 15, tint);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(15, 15));
            var t = LGBuild.Label(head, text, 11, tint, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch(22, 0, 0, 0);
            var right = LGBuild.Label(head, "", 11, Muted, TextAnchor.MiddleRight, bold: true);
            return right;
        }

        // ---------- Слоты ----------

        private void BuildSlotsColumn()
        {
            var col = Column("SlotsColumn", Pad, LeftW);
            _slotsCount = ColumnTitle(col, "НАУЧНЫЕ СЛОТЫ", LGIcon.Research, Cyan);

            var host = LGBuild.Rect(col, "ListHost");
            host.Stretch(6, 74, 6, 42);
            _slotsList = LGBuild.ScrollList(host, 8f, 6);

            var foot = LGBuild.Panel(col, "Footer", new Color(0.03f, 0.08f, 0.10f, 0.9f));
            foot.rectTransform.anchorMin = new Vector2(0, 0);
            foot.rectTransform.anchorMax = new Vector2(1, 0);
            foot.rectTransform.pivot = new Vector2(0.5f, 0);
            foot.rectTransform.offsetMin = new Vector2(10, 10);
            foot.rectTransform.offsetMax = new Vector2(-10, 66);
            LG.Platter(foot.gameObject, 12f).SetRim(new Color(1f, 1f, 1f, 0.08f));
            _slotsFooter = LGBuild.Label(foot.transform, "", 10, Muted, TextAnchor.MiddleLeft, wrap: true);
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
                var lbl = LGBuild.Label(cell, "", 10, Muted, TextAnchor.MiddleCenter, bold: true);
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
            var bg = LGBuild.Panel(parent, "Tab_" + b, UIManager.DS.BgSlot, raycast: true);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(w, 0);
            rt.anchoredPosition = new Vector2(x, 0);
            var btn = bg.gameObject.AddComponent<Button>();
            var fx = LG.Button(bg.gameObject, new Color(bc.r, bc.g, bc.b, 0.3f), 14f);
            btn.onClick.AddListener(() => SwitchBranch(b));

            var ic = LGIcons.Create(rt, BranchIcon(b), 26, bc);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(26, 26));

            var tab = new TabUi { Branch = b, Bg = bg, Fx = fx };
            tab.Name = LGBuild.Label(rt, TechBranchInfo.Name(b), 14, Primary, TextAnchor.UpperLeft, bold: true);
            tab.Name.rectTransform.Stretch(54, 0, 110, 9);
            tab.Motto = LGBuild.Label(rt, TechBranchInfo.Motto(b), 10, Muted, TextAnchor.LowerLeft);
            tab.Motto.rectTransform.Stretch(54, 9, 110, 0);
            tab.Count = LGBuild.Label(rt, "", 13, bc, TextAnchor.UpperRight, bold: true);
            tab.Count.rectTransform.Stretch(0, 0, 16, 9);

            var barHost = LGBuild.Rect(rt, "Bar");
            barHost.anchorMin = barHost.anchorMax = new Vector2(1, 0);
            barHost.pivot = new Vector2(1, 0);
            barHost.sizeDelta = new Vector2(86, 6);
            barHost.anchoredPosition = new Vector2(-16, 12);
            tab.Bar = LGBuild.Bar(barHost, bc, 0f, 5f);
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

            _detailBadge = LGBuild.Panel(col, "Badge", new Color(0.05f, 0.12f, 0.16f, 1f));
            _detailBadge.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -16), new Vector2(64, 64));
            _detailBadgeFx = LG.Platter(_detailBadge.gameObject, 16f);
            _detailIcon = LGIcons.Create(_detailBadge.transform, LGIcon.Research, 38, Cyan);

            _detailName = LGBuild.Label(col, "", 16, Primary, TextAnchor.UpperLeft, bold: true, wrap: true);
            _detailName.rectTransform.TopBand(16, 44, 92, 16);
            _detailSub = LGBuild.Label(col, "", 10, Muted, TextAnchor.UpperLeft, bold: true);
            _detailSub.rectTransform.TopBand(62, 16, 92, 16);

            _pillBg = LGBuild.Panel(col, "Pill", new Color(0.1f, 0.2f, 0.2f, 1f));
            _pillBg.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -90), new Vector2(RightW - 32, 26));
            LG.Chip(_pillBg.gameObject, new Color(1f, 1f, 1f, 0.2f), 13f);
            _pillText = LGBuild.Label(_pillBg.transform, "", 11, Primary, TextAnchor.MiddleCenter, bold: true);

            var host = LGBuild.Rect(col, "DetailHost");
            host.Stretch(6, 104, 6, 124);
            _detailList = LGBuild.ScrollList(host, 6f, 10);

            (_primaryBtn, _primaryImg, _primaryLabel) = ActionButton(col, "Primary", 56f, 40f, () => _primaryAction?.Invoke());
            (_secondaryBtn, _secondaryImg, _secondaryLabel) = ActionButton(col, "Secondary", 14f, 34f, () => _secondaryAction?.Invoke());
        }

        private static (Button, Image, Text) ActionButton(RectTransform col, string name, float bottom, float h, System.Action onClick)
        {
            var btn = LGBuild.Button(col, name, UIManager.DS.BtnPrimary, new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f), onClick, null, " ", 12);
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
                tab.Bg.color = on ? new Color(bc.r * 0.20f, bc.g * 0.20f, bc.b * 0.20f, 1f) : new Color(0.04f, 0.08f, 0.11f, 0.85f);
                tab.Fx.SetRim(new Color(bc.r, bc.g, bc.b, on ? 0.9f : 0.18f));
                tab.Fx.GlowMultiplier = on ? 2.0f : 0.5f;
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

            float y = 0f;
            foreach (var cat in TechBranchInfo.Lanes(s_branch))
            {
                var techs = new List<Technology>();
                int rows = 1;
                foreach (var t in tm.AllTechs)
                    if (t.Category == cat) { techs.Add(t); rows = Mathf.Max(rows, t.GridColumn + 1); }
                if (techs.Count == 0) continue;

                float laneH = rows * RowH - (RowH - CardH) + LanePadV * 2f;
                BuildLane(cat, techs, y, laneH);
                foreach (var t in techs)
                {
                    float cx = LaneHeadW + Mathf.Clamp(t.Tier, 0, 3) * ColW + (ColW - CardW) * 0.5f;
                    float cy = -(y + LanePadV + t.GridColumn * RowH);
                    _cards[t.Id] = BuildCard(t, new Vector2(cx, cy), tm);
                }
                y += laneH + LaneGap;
            }
            _treeContent.sizeDelta = new Vector2(0, Mathf.Max(0f, y - LaneGap + 6f));

            foreach (var c in _cards.Values)
                foreach (var req in c.Tech.RequiredTechIds)
                    if (_cards.TryGetValue(req, out var from)) DrawWire(from, c, tm);
        }

        private void BuildLane(TechCategory cat, List<Technology> techs, float y, float h)
        {
            Color cc = TechCategoryInfo.Color(cat);
            var lane = LGBuild.Panel(_lanesLayer, "Lane_" + cat, new Color(cc.r * 0.07f, cc.g * 0.07f, cc.b * 0.07f, 0.55f));
            var rt = lane.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(0, -y - h);
            rt.offsetMax = new Vector2(0, -y);
            LG.Platter(lane.gameObject, 14f).SetRim(new Color(cc.r, cc.g, cc.b, 0.14f));

            // Заголовок направления — слева, по центру дорожки
            var head = LGBuild.Panel(rt, "Head", new Color(cc.r * 0.12f, cc.g * 0.12f, cc.b * 0.12f, 0.9f));
            var hrt = head.rectTransform;
            hrt.anchorMin = new Vector2(0, 0);
            hrt.anchorMax = new Vector2(0, 1);
            hrt.pivot = new Vector2(0, 0.5f);
            hrt.offsetMin = new Vector2(6, 6);
            hrt.offsetMax = new Vector2(LaneHeadW - 8f, -6);
            LG.Platter(head.gameObject, 11f).SetRim(new Color(cc.r, cc.g, cc.b, 0.3f));

            int done = 0;
            foreach (var t in techs) if (t.IsResearched) done++;

            var group = LGBuild.Rect(hrt, "Group");
            group.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(LaneHeadW - 22f, 92f));
            var ic = LGIcons.Create(group, LGIcons.ForTechCategory(cat), 26, cc);
            ic.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(26, 26));
            var nm = LGBuild.Label(group, TechCategoryInfo.Name(cat), 10, Primary, TextAnchor.MiddleCenter, bold: true, wrap: true);
            FitText(nm, 8);
            nm.rectTransform.TopBand(30, 30);
            var cnt = LGBuild.Label(group, $"{done} / {techs.Count}", 10, cc, TextAnchor.MiddleCenter, bold: true);
            cnt.rectTransform.TopBand(62, 14);
            var barHost = LGBuild.Rect(group, "Bar");
            barHost.TopBand(80, 6, 12, 12);
            LGBuild.Bar(barHost, cc, done / (float)Mathf.Max(1, techs.Count), 4f);
        }

        private CardUi BuildCard(Technology t, Vector2 pos, TechnologyManager tm)
        {
            Color cc = TechCategoryInfo.Color(t.Category);
            var bg = LGBuild.Panel(_cardsLayer, "Card_" + t.Id, UIManager.DS.BgSlot, raycast: true);
            var rt = bg.rectTransform;
            rt.At(new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(CardW, CardH));

            var c = new CardUi { Tech = t, Rt = rt };
            c.Group = bg.gameObject.AddComponent<CanvasGroup>();
            var btn = bg.gameObject.AddComponent<Button>();
            c.Fx = LG.Button(bg.gameObject, new Color(cc.r, cc.g, cc.b, 0.45f), 12f, animateScale: false);
            c.Fx.FillMultiplier = 0.8f;
            c.Hover = bg.gameObject.AddComponent<TechCardFX>();
            var captured = t;
            btn.onClick.AddListener(() => OnCardClick(captured));

            // Рамка выбора
            var frame = LGBuild.Panel(rt, "Frame", new Color(1f, 1f, 1f, 0.02f));
            frame.rectTransform.Stretch(-3, -3, -3, -3);
            LG.Border(frame.gameObject, new Color(1f, 1f, 1f, 0.9f));
            c.Frame = frame.gameObject;
            c.Frame.SetActive(t == _selected);

            c.Accent = LGBuild.Panel(rt, "Accent", cc);
            c.Accent.rectTransform.anchorMin = new Vector2(0, 0);
            c.Accent.rectTransform.anchorMax = new Vector2(0, 1);
            c.Accent.rectTransform.offsetMin = new Vector2(0, 12);
            c.Accent.rectTransform.offsetMax = new Vector2(3, -12);
            LG.Fill(c.Accent.gameObject);

            var badge = LGBuild.Panel(rt, "Badge", new Color(cc.r * 0.15f, cc.g * 0.15f, cc.b * 0.15f, 1f));
            badge.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(9, 0), new Vector2(34, 34));
            LG.Platter(badge.gameObject, 10f).SetRim(new Color(cc.r, cc.g, cc.b, 0.4f));
            LGIcons.Create(badge.transform, LGIcons.ForTechCategory(t.Category), 20, cc);

            var name = LGBuild.Label(rt, t.Name, 11, Primary, TextAnchor.UpperLeft, bold: true, wrap: true);
            name.lineSpacing = 0.9f;
            FitText(name, 9);
            name.rectTransform.Stretch(51, 22, 7, 6);

            c.Status = LGBuild.Label(rt, "", 10, Muted, TextAnchor.LowerLeft, wrap: true);
            FitText(c.Status, 8);
            c.Status.rectTransform.Stretch(51, 9, 24, CardH - 23);

            c.StateIcon = LGIcons.Create(rt, LGIcon.Lock, 13, Muted);
            c.StateIcon.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-8, 9), new Vector2(13, 13));

            var barHost = LGBuild.Rect(rt, "Progress");
            barHost.anchorMin = new Vector2(0, 0);
            barHost.anchorMax = new Vector2(1, 0);
            barHost.pivot = new Vector2(0.5f, 0);
            barHost.offsetMin = new Vector2(51, 3);
            barHost.offsetMax = new Vector2(-8, 7);
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
            bool match = Matches(t);
            bool searching = !string.IsNullOrEmpty(_search);

            Color rim, accent;
            float alpha = 1f;
            bool showBar = false;
            float progress = t.ProgressNormalized;
            LGIcon icon = LGIcon.Lock;
            Color iconCol = Muted;
            string status;

            switch (st)
            {
                case CardState.Researched:
                    rim = new Color(Green.r, Green.g, Green.b, 0.7f);
                    accent = Green;
                    icon = LGIcon.Check; iconCol = Green;
                    status = $"<color={LGBuild.Hex(Green)}>Изучено</color>";
                    break;
                case CardState.Researching:
                    rim = new Color(Cyan.r, Cyan.g, Cyan.b, 0.9f);
                    accent = Cyan;
                    icon = slot.IsPaused ? LGIcon.Pause : LGIcon.Play; iconCol = slot.IsPaused ? Gold : Cyan;
                    showBar = true;
                    progress = slot.ProgressNormalized;
                    status = slot.IsPaused
                        ? $"<color={LGBuild.Hex(Gold)}>Пауза · {progress * 100f:0}%</color>"
                        : $"<color={LGBuild.Hex(Cyan)}>{progress * 100f:0}% · ~{TechnologyManager.FormatDays(tm.EstimateDays(t))}</color>";
                    break;
                case CardState.Queued:
                    rim = new Color(Gold.r, Gold.g, Gold.b, 0.75f);
                    accent = Gold;
                    icon = LGIcon.Clock; iconCol = Gold;
                    showBar = progress > 0.001f;
                    status = $"<color={LGBuild.Hex(Gold)}>#{qpos + 1} в очереди · ~{TechnologyManager.FormatDays(tm.EstimateDays(t))}</color>";
                    break;
                case CardState.Available:
                    rim = new Color(cc.r, cc.g, cc.b, 0.5f);
                    accent = cc;
                    icon = LGIcon.Research; iconCol = new Color(cc.r, cc.g, cc.b, 0.8f);
                    showBar = progress > 0.001f;
                    float pen = tm.GetYearPenalty(t);
                    status = $"~{TechnologyManager.FormatDays(tm.EstimateDays(t))}" +
                             (pen > 1.01f ? $"  <color={LGBuild.Hex(Orange)}>×{pen:0.0}</color>" : "");
                    break;
                default:
                    rim = new Color(1f, 1f, 1f, 0.08f);
                    accent = new Color(0.4f, 0.48f, 0.55f, 0.6f);
                    alpha = 0.62f;
                    status = $"Нужно: {MissingReqName(t, tm)}";
                    break;
            }

            if (searching)
            {
                if (match) rim = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
                else alpha = 0.2f;
            }

            c.Group.alpha = alpha;
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
            Color col = done ? new Color(bc.r, bc.g, bc.b, to.Tech.IsResearched ? 0.85f : 0.6f) : WireIdle;

            Vector2 a = from.Rt.anchoredPosition + new Vector2(CardW, -CardH * 0.5f);
            Vector2 b = to.Rt.anchoredPosition + new Vector2(0f, -CardH * 0.5f);
            if (b.x <= a.x + 4f)
            {
                Segment(a, b, col, done, flowing);
                return;
            }
            float midX = b.x - (ColW - CardW) * 0.5f;
            if (Mathf.Abs(a.y - b.y) < 0.5f)
            {
                Segment(a, b, col, done, flowing);
                return;
            }
            Segment(a, new Vector2(midX, a.y), col, done, false);
            Segment(new Vector2(midX, a.y), new Vector2(midX, b.y), col, done, false);
            Segment(new Vector2(midX, b.y), b, col, done, flowing);
        }

        private void Segment(Vector2 a, Vector2 b, Color col, bool bright, bool pulse)
        {
            var img = LGBuild.Panel(_wiresLayer, "Wire", col);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            Vector2 d = b - a;
            float len = d.magnitude;
            rt.sizeDelta = new Vector2(len + 1f, bright ? 2.4f : 1.6f);
            rt.anchoredPosition = a;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            LG.Line(img.gameObject).Intensity = bright ? 0.9f : 0.25f;

            if (!pulse || len < 12f) return;
            var dot = LGBuild.Panel(rt, "Pulse", Color.white);
            dot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 6f));
            LG.Dot(dot.gameObject);
            dot.gameObject.AddComponent<WirePulseAnimator>().Init(dot.rectTransform, len, col);
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
            LG.Platter(bg.gameObject, 14f).SetRim(rim);
            return bg.rectTransform;
        }

        private void BuildFreeSlot(int i)
        {
            var rt = SlotCard("Free" + i, 70f, new Color(0.08f, 0.07f, 0.03f, 0.55f), new Color(Gold.r, Gold.g, Gold.b, 0.45f));
            var ic = LGIcons.Create(rt, LGIcon.Research, 20, Gold);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -12), new Vector2(20, 20));
            var t = LGBuild.Label(rt, $"СЛОТ {i + 1}  ·  СВОБОДЕН", 11, Gold, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.Stretch(40, 0, 10, 14);
            var hint = LGBuild.Label(rt, "Выберите технологию в дереве: двойной щелчок или «Исследовать».", 10, Muted, TextAnchor.UpperLeft, wrap: true);
            hint.rectTransform.Stretch(12, 6, 12, 38);
        }

        private void BuildLockedSlot(int i, Technology opener)
        {
            var rt = SlotCard("Locked" + i, 50f, new Color(0.02f, 0.04f, 0.06f, 0.6f), new Color(1f, 1f, 1f, 0.07f));
            var ic = LGIcons.Create(rt, LGIcon.Lock, 16, new Color(Muted.r, Muted.g, Muted.b, 0.6f));
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(16, 16));
            var t = LGBuild.Label(rt, $"СЛОТ {i + 1}  ·  ЗАКРЫТ", 10, new Color(Muted.r, Muted.g, Muted.b, 0.8f), TextAnchor.UpperLeft, bold: true);
            t.rectTransform.Stretch(40, 0, 10, 9);
            var sub = LGBuild.Label(rt, opener != null ? $"Откроет: {opener.Name}" : "Откроется технологией", 10,
                new Color(Muted.r, Muted.g, Muted.b, 0.65f), TextAnchor.LowerLeft);
            sub.rectTransform.Stretch(40, 9, 10, 0);
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
            float h = 104f + s.Queue.Count * 24f + (s.Queue.Count > 0 ? 6f : 0f);
            var rt = SlotCard("Slot" + i, h, new Color(0.03f, 0.10f, 0.13f, 0.92f),
                s.IsPaused ? new Color(Gold.r, Gold.g, Gold.b, 0.55f) : new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f));
            var sel = rt.gameObject.AddComponent<Button>();
            sel.transition = Selectable.Transition.None;
            sel.onClick.AddListener(() => Reveal(t));

            var lbl = LGBuild.Label(rt, $"СЛОТ {i + 1}" + (s.IsPaused ? $"  ·  <color={LGBuild.Hex(Gold)}>ПАУЗА</color>" : ""), 9, Muted, TextAnchor.UpperLeft, bold: true);
            lbl.rectTransform.Stretch(12, 0, 70, 9);

            int idx = i;
            var pause = LGBuild.Button(rt, "Pause", UIManager.DS.BtnNeutral, new Color(Gold.r, Gold.g, Gold.b, 0.4f),
                () => { TechnologyManager.Instance?.ToggleSlotPause(idx); SFXManager.Play(Sfx.UiClick); },
                s.IsPaused ? LGIcon.Play : LGIcon.Pause, null, 8);
            ((RectTransform)pause.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-38, -6), new Vector2(24, 24));
            TooltipHelper.Attach(pause.gameObject, s.IsPaused ? "Продолжить исследование" : "Приостановить слот: наука перейдёт к остальным");
            var cancel = LGBuild.Button(rt, "Cancel", UIManager.DS.BtnDanger, new Color(Red.r, Red.g, Red.b, 0.45f),
                () => { TechnologyManager.Instance?.CancelCurrent(idx); SFXManager.Play(Sfx.UiBack); },
                LGIcon.Close, null, 8);
            ((RectTransform)cancel.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -6), new Vector2(24, 24));
            TooltipHelper.Attach(cancel.gameObject, "Отменить. Накопленный прогресс сохранится в технологии.");

            var badge = LGBuild.Panel(rt, "Badge", new Color(cc.r * 0.15f, cc.g * 0.15f, cc.b * 0.15f, 1f));
            badge.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -28), new Vector2(36, 36));
            LG.Platter(badge.gameObject, 10f).SetRim(new Color(cc.r, cc.g, cc.b, 0.45f));
            LGIcons.Create(badge.transform, LGIcons.ForTechCategory(t.Category), 21, cc);

            var name = LGBuild.Label(rt, t.Name, 12, Primary, TextAnchor.MiddleLeft, bold: true, wrap: true);
            name.lineSpacing = 0.9f;
            name.rectTransform.TopBand(26, 40, 56, 10);

            var barHost = LGBuild.Rect(rt, "Bar");
            barHost.TopBand(72, 8, 12, 12);
            var su = new SlotUi { Index = i };
            su.Bar = LGBuild.Bar(barHost, s.IsPaused ? Gold : Cyan, s.ProgressNormalized, 6f);
            su.Stats = LGBuild.Label(rt, SlotStats(tm, s), 10, Primary, TextAnchor.UpperLeft);
            su.Stats.rectTransform.TopBand(83, 16, 12, 12);
            _slotUis.Add(su);

            for (int q = 0; q < s.Queue.Count; q++)
            {
                var qt = s.Queue[q];
                var row = LGBuild.Panel(rt, "Q" + q, new Color(0f, 0.03f, 0.05f, 0.55f), raycast: true);
                row.rectTransform.TopBand(106 + q * 24, 20, 10, 10);
                LG.Ignore(row.gameObject, includeChildren: false);
                var rb = row.gameObject.AddComponent<Button>();
                rb.transition = Selectable.Transition.None;
                rb.onClick.AddListener(() => Reveal(qt));

                var qn = LGBuild.Label(row.transform, $"<color={LGBuild.Hex(Gold)}>{q + 1}</color>   {qt.Name}", 10, Primary, TextAnchor.MiddleLeft);
                qn.rectTransform.Stretch(8, 0, 90, 0);
                var qe = LGBuild.Label(row.transform, "~" + TechnologyManager.FormatDays(tm.EstimateDays(qt)), 9, Muted, TextAnchor.MiddleRight);
                qe.rectTransform.Stretch(0, 0, 26, 0);
                var rm = LGBuild.Button(row.transform, "Remove", new Color(0.2f, 0.06f, 0.08f, 1f), new Color(Red.r, Red.g, Red.b, 0.35f),
                    () => { TechnologyManager.Instance?.RemoveFromQueue(qt); SFXManager.Play(Sfx.UiBack); }, LGIcon.Close, null, 6);
                ((RectTransform)rm.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-3, 0), new Vector2(16, 16));
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
                Paragraph("Щёлкните по карточке в дереве, чтобы увидеть, что даёт технология, сколько она стоит и что откроет дальше.", 12, Muted);
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
            _detailBadge.color = new Color(cc.r * 0.15f, cc.g * 0.15f, cc.b * 0.15f, 1f);
            _detailBadgeFx.SetRim(new Color(cc.r, cc.g, cc.b, 0.6f));
            _pillBg.gameObject.SetActive(true);

            Section("ЭФФЕКТ", LGIcon.Info, cc);
            Paragraph(TechConfirmDialog.ColorizeModifiers(t.Description), 12, Primary);

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
                var f = Paragraph(TechFlavorDatabase.Get(t.Id), 11, new Color(Muted.r, Muted.g, Muted.b, 0.95f));
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
            _pillBg.color = new Color(col.r * 0.14f, col.g * 0.14f, col.b * 0.14f, 1f);
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
            LGBuild.Height(row.gameObject, 26f);
            var ic = LGIcons.Create(row, icon, 12, tint);
            ic.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(2, 3), new Vector2(12, 12));
            var t = LGBuild.Label(row, text, 10, tint, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.Stretch(20, 2, 0, 0);
            var line = LGBuild.Panel(row, "Line", new Color(tint.r, tint.g, tint.b, 0.25f));
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
            LGBuild.Height(row.gameObject, 22f);
            var ic = LGIcons.Create(row, icon, 14, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(14, 14));
            var t = LGBuild.Label(row, text, 11, Primary, TextAnchor.MiddleLeft);
            t.rectTransform.Stretch(24, 0, 0, 0);
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
            if (top < cur || top + CardH > cur + viewH)
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
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * (_hovered ? 1.035f : 1f), k);
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
