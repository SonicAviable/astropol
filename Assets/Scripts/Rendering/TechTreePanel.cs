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
    /// Экран исследований — «карта эпох». Всё дерево на одном экране: строки — 10 направлений,
    /// сгруппированных по наукам (физика, общество, инженерия), столбцы — эпохи (базовые, 2208, 2216, 2229, 2242)
    /// с отметкой текущего года. Технологии — компактные плашки, цвет — состояние.
    ///
    /// Щелчок по плашке — фокус: подсвечивается путь к ней через все ветки (предпосылки), остальное гаснет,
    /// рядом — карточка с эффектом, путём и действием («Исследовать», «В план», «Весь путь в план»).
    /// Двойной щелчок — то же действие сразу. Щелчок по пустому месту снимает фокус.
    /// Справа — научные слоты, план исследований (свободные слоты сами берут из него следующую готовую
    /// технологию) и прогресс научной победы.
    /// </summary>
    public class TechTreePanel : MonoBehaviour
    {
        public static TechTreePanel Instance { get; private set; }
        public bool IsOpen => _isOpen;

        // ==================== РАЗМЕТКА ====================

        private const float WinW = 1840f, WinH = 1010f;
        private const float HeaderH = 80f, Pad = 16f, Gap = 14f, SideW = 360f;
        private const float LabelW = 238f, BandW = 22f, EpochHeadH = 58f, LegendH = 28f, GroupGap = 8f;
        private const float ChipH = 22f, ChipGap = 3f;
        private const float PopW = 384f;
        private const int TierCount = 5;
        private const float DoubleClick = 0.4f;
        private static readonly int[] TierYears = { 2200, 2208, 2216, 2229, 2242 };
        private static string[] TierNames => new[] { Loc.T("БАЗОВЫЕ"), Loc.T("УРОВЕНЬ I"), Loc.T("УРОВЕНЬ II"), Loc.T("УРОВЕНЬ III"), Loc.T("УРОВЕНЬ IV") };

        private static float BodyTop => HeaderH + 8f;
        private static float MatrixW => WinW - Pad * 2f - SideW - Gap;
        private static float MatrixH => WinH - BodyTop - Pad;
        private static float ColW => (MatrixW - LabelW - 8f) / TierCount;
        private static float ChipW => ColW - 16f;

        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Green => UIManager.DS.Green;
        /// <summary>«Атлас»: исследуемое — коралл (активный узел карты).</summary>
        private static Color Coral => UIManager.DS.Red;
        private static Color Red => UIManager.DS.Red;
        private static readonly Color Orange = new Color(1f, 0.66f, 0.50f);
        private static readonly Color PathBlue = new Color(0.561f, 0.706f, 0.949f);   // #8FB4F2
        private static readonly Color Dim = new Color(0.50f, 0.49f, 0.45f, 1f);
        private static readonly Color PanelBg = new Color(0.055f, 0.094f, 0.157f, 0.94f);
        private static readonly Color ChipBg = new Color(0.075f, 0.125f, 0.220f, 0.96f);

        // ==================== СОСТОЯНИЕ ====================

        private enum St { Done, Active, Paused, Queued, Planned, Avail, Early, Locked }
        private enum Filter { All, Avail, Work, Done }

        private Canvas _canvas;
        private GameObject _dimmer, _window;
        private TechTreeBackdrop _backdrop;
        private bool _built, _isOpen;

        private Technology _selected;
        private Filter _filter = Filter.All;
        private string _search = "";
        private string _lastSig = "";
        private float _lastClickTime;
        private Technology _lastClickTech;

        // Шапка
        private Text _headerSub, _scienceChip;
        private InputField _searchInput;
        private readonly List<(Filter f, Image bg, Text label)> _filterBtns = new List<(Filter, Image, Text)>();

        // Карта
        private RectTransform _matrix, _staticLayer, _wiresLayer, _chipsLayer, _popLayer;
        private RectTransform _nowLine;
        private Text _nowBadge;
        private float _rowH = 80f;

        private class ChipUi
        {
            public Technology Tech;
            public RectTransform Rt;
            public Image Bg, Icon, Mark;
            public Text Name, Pct;
            public LiquidGlassEffect Fx;
            public CanvasGroup Group;
            public RectTransform Prog;
            public TechCardFX Hover;
        }
        private readonly Dictionary<string, ChipUi> _chips = new Dictionary<string, ChipUi>();

        // Правая колонка
        private RectTransform _slotsList, _planList;
        private Text _slotsCap, _planCap, _victoryText;
        private Image _victoryRing;
        private class SlotUi { public int Index; public RectTransform Bar; public Text Pct, Eta; }
        private readonly List<SlotUi> _slotUis = new List<SlotUi>();

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
            if (_selected != null && _selected.IsResearched) _selected = null;
            _backdrop.SetBranch(TechBranchInfo.Key(_selected != null ? TechBranchInfo.Of(_selected.Category) : TechBranch.Society));

            Subscribe();
            RebuildAll();
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
            _dimmer = LGBuild.Panel(_canvas.transform, "TechTreeDimmer", new Color(0.055f, 0.094f, 0.157f, 0.88f), raycast: true).gameObject;
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
            LG.Glass(_window, 28f).SetRim(new Color(0.475f, 0.816f, 0.749f, 0.42f));
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
            var shade = LGBuild.Panel(mask, "Shade", new Color(0.055f, 0.094f, 0.157f, 0.70f));
            shade.rectTransform.Stretch();
            LG.Ignore(shade.gameObject);

            BuildHeader();
            BuildMatrix();
            BuildSide();
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
            _headerSub.rectTransform.Stretch(78, 0, WinW - 78 - 340, 16);
            _headerSub.Track(2.2f);
            var title = LGBuild.Label(head, Loc.T("Исследования"), 32, Primary, TextAnchor.LowerLeft, bold: true);
            title.rectTransform.Stretch(78, 12, 1300, 0);

            // Фильтры: все / доступные / в работе / изученные
            var seg = LGBuild.Rect(head, "Filters");
            seg.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(440, 0), new Vector2(4 * 136f, 36));
            var filters = new[] { Filter.All, Filter.Avail, Filter.Work, Filter.Done };
            for (int i = 0; i < filters.Length; i++)
            {
                var f = filters[i];
                var bg = LGBuild.Panel(seg.transform, "F" + i, new Color(0, 0, 0, 0), raycast: true);
                bg.rectTransform.anchorMin = new Vector2(i / 4f, 0); bg.rectTransform.anchorMax = new Vector2((i + 1) / 4f, 1);
                bg.rectTransform.offsetMin = new Vector2(3, 3); bg.rectTransform.offsetMax = new Vector2(-3, -3);
                LG.Ignore(bg.gameObject, includeChildren: false);
                var lbl = LGBuild.Label(bg.transform, "", 12, Muted, TextAnchor.MiddleCenter, bold: true);
                lbl.rectTransform.Stretch(14, 0, 14, 0);
                lbl.resizeTextForBestFit = true; lbl.resizeTextMinSize = 8; lbl.resizeTextMaxSize = 12;
                var b = bg.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => { _filter = f; SFXManager.Play(Sfx.UiTab); ApplyFocus(); RefreshFilters(); });
                LGAstrolabe.Attach(bg.gameObject, bg, lbl);   // «Астролябия»; выбранный фильтр — как вкладка
                _filterBtns.Add((f, bg, lbl));
            }

            _scienceChip = LGBuild.Label(head, "", 14, Primary, TextAnchor.MiddleRight, bold: true);
            _scienceChip.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-388, 2), new Vector2(220, 30));

            var line = LGBuild.Panel(head, "Line", new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f));
            line.rectTransform.anchorMin = new Vector2(0, 0);
            line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.offsetMin = new Vector2(24, 0);
            line.rectTransform.offsetMax = new Vector2(-24, 1f);
            LG.Ignore(line.gameObject);

            BuildSearch(head);

            var close = LGControls.IconButton(head, LGIcon.Close, 40, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.3f), Close);
            ((RectTransform)close.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(40, 40));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#A9A392>(Esc)</color>"));
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

        // ---------- Карта эпох ----------

        private void BuildMatrix()
        {
            var panel = LGBuild.Panel(_window.transform, "Matrix", new Color(0.055f, 0.094f, 0.157f, 0.82f), raycast: true);
            _matrix = panel.rectTransform;
            _matrix.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -BodyTop), new Vector2(MatrixW, MatrixH));
            var fx = LG.Platter(panel.gameObject, 16f);
            fx.AsSurface = true;   // кнопка «снять фокус» не делает поле светлой капсулой
            fx.SetRim(new Color(0.906f, 0.757f, 0.439f, 0.18f));
            fx.SpecularMultiplier = 0.2f;
            // Щелчок по пустому месту — снять фокус
            var bgBtn = panel.gameObject.AddComponent<Button>();
            bgBtn.transition = Selectable.Transition.None;
            bgBtn.onClick.AddListener(ClearSelection);

            _staticLayer = Layer("Static");
            _wiresLayer = Layer("Wires");
            _chipsLayer = Layer("Chips");

            // Линия «сейчас» — поверх фона, под плашками
            var now = LGBuild.Panel(_staticLayer, "Now", new Color(Gold.r, Gold.g, Gold.b, 0.55f));
            _nowLine = now.rectTransform;
            LG.Ignore(now.gameObject);
            var badge = LGBuild.Panel(_matrix, "NowBadge", new Color(0.11f, 0.085f, 0.02f, 0.95f));
            badge.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(92, 20));
            LG.Platter(badge.gameObject, 10f).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.7f));
            _nowBadge = LGBuild.Label(badge.transform, "", 11, Gold, TextAnchor.MiddleCenter, bold: true);

            _popLayer = Layer("Popover");
        }

        private RectTransform Layer(string name)
        {
            var rt = LGBuild.Rect(_matrix, name);
            rt.Stretch();
            return rt;
        }

        // ---------- Правая колонка ----------

        private void BuildSide()
        {
            float x0 = WinW - Pad - SideW;
            float slotsH = 46f + 46f * 6f;
            const float victoryH = 84f;

            var slots = SideCard("Slots", x0, BodyTop, slotsH);
            _slotsCap = LGBuild.Label(slots, "", 12, Green, TextAnchor.UpperLeft, bold: true);
            _slotsCap.rectTransform.TopBand(14, 18, 16, 16);
            _slotsList = LGBuild.Rect(slots, "List");
            _slotsList.Stretch(10, 8, 10, 40);

            float planTop = BodyTop + slotsH + 12f;
            var plan = SideCard("Plan", x0, planTop, WinH - Pad - victoryH - 12f - planTop);
            _planCap = LGBuild.Label(plan, "", 12, PathBlue, TextAnchor.UpperLeft, bold: true);
            _planCap.rectTransform.TopBand(14, 18, 16, 16);
            var host = LGBuild.Rect(plan, "PlanHost");
            host.Stretch(4, 6, 4, 38);
            _planList = LGBuild.ScrollList(host, 6f, 6);

            var vic = SideCard("Victory", x0, WinH - Pad - victoryH, victoryH);
            Star(vic, "Track", RingSprite(), new Color(1f, 1f, 1f, 0.10f), new Vector2(50, -42), 60f);
            _victoryRing = Star(vic, "Ring", RingSprite(), Gold, new Vector2(50, -42), 60f);
            _victoryRing.type = Image.Type.Filled;
            _victoryRing.fillMethod = Image.FillMethod.Radial360;
            _victoryRing.fillOrigin = (int)Image.Origin360.Top;
            _victoryText = LGBuild.Label(vic, "", 13, Primary, TextAnchor.MiddleLeft);
            _victoryText.rectTransform.Stretch(96, 6, 12, 6);
            TooltipHelper.Attach(vic.gameObject, Loc.T("<b>Научная победа</b>\nИзучите нужное число технологий — любых, из любых веток."));
        }

        private RectTransform SideCard(string name, float x0, float top, float h)
        {
            var col = LGBuild.Panel(_window.transform, name, PanelBg, raycast: true);
            var rt = col.rectTransform;
            rt.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0, -top), new Vector2(SideW, h));
            var fx = LG.Platter(col.gameObject, 14f);
            fx.SetRim(new Color(1f, 1f, 1f, 0.10f));
            fx.FillMultiplier = 3f;
            fx.SpecularMultiplier = 0.25f;
            return rt;
        }

        // ==================== ОБНОВЛЕНИЕ ====================

        /// <summary>Подпись состояния: меняется — перестраиваем всё, иначе только живые полосы.</summary>
        private static string Signature(TechnologyManager tm)
        {
            var sb = new StringBuilder(160);
            sb.Append(tm.ResearchedCount).Append('|').Append(tm.Slots.Count);
            foreach (var s in tm.Slots)
            {
                sb.Append('|').Append(s.CurrentTech != null ? s.CurrentTech.Id : "-").Append(s.IsPaused ? "p" : "");
                foreach (var q in s.Queue) sb.Append(',').Append(q.Id);
            }
            sb.Append("|P");
            foreach (var t in tm.Plan) sb.Append(',').Append(t.Id);
            if (TimeManager.Instance != null) sb.Append('|').Append(TimeManager.Instance.Year);
            return sb.ToString();
        }

        private void Refresh()
        {
            if (!_isOpen) return;
            var tm = TechnologyManager.Instance;
            if (tm == null) return;
            if (Signature(tm) != _lastSig) RebuildAll();
            else UpdateLive(tm);
        }

        private void RebuildAll()
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return;
            _lastSig = Signature(tm);
            RebuildMatrix(tm);
            RebuildSlots(tm);
            RebuildPlan(tm);
            RefreshFilters();
            ApplyFocus();
            UpdateLive(tm);
        }

        private void UpdateLive(TechnologyManager tm)
        {
            foreach (var c in _chips.Values)
            {
                if (c.Prog == null) continue;
                var st = StateOf(c.Tech, tm, out var slot, out _);
                float p = slot != null ? slot.ProgressNormalized : c.Tech.ProgressNormalized;
                c.Prog.anchorMax = new Vector2(Mathf.Clamp01(p), 0f);   // тонкая полоса снизу, не на всю высоту плашки
                if (c.Pct != null && (st == St.Active || st == St.Paused)) c.Pct.text = $"{p * 100f:0}%";
            }
            foreach (var su in _slotUis)
            {
                if (su.Index >= tm.Slots.Count) continue;
                var s = tm.Slots[su.Index];
                if (s.CurrentTech == null) continue;
                LGBuild.SetBar(su.Bar, s.ProgressNormalized);
                su.Pct.text = $"{s.ProgressNormalized * 100f:0}%";
                su.Eta.text = s.IsPaused ? Loc.T("пауза") : "~" + TechnologyManager.FormatDays(tm.EstimateDays(s.CurrentTech));
            }
            RefreshHeader(tm);
        }

        private void RefreshHeader(TechnologyManager tm)
        {
            var vm = VictoryManager.Instance;
            int need = vm != null ? vm.ScienceRequired : tm.AllTechs.Count;
            _headerSub.text = Loc.F("ИЗУЧЕНО {0} ИЗ {1}", tm.ResearchedCount, tm.AllTechs.Count) +
                              Loc.F("  ·  В ПЛАНЕ {0}", tm.Plan.Count);
            _scienceChip.text = Loc.F("Наука <color={0}>+{1:0.#} / мес</color>", LGBuild.Hex(Green), tm.MonthlyResearchIncome);
            _victoryRing.fillAmount = Mathf.Clamp01(tm.ResearchedCount / (float)Mathf.Max(1, need));
            _victoryText.text = Loc.F("<size=11><color={0}><b>НАУЧНАЯ ПОБЕДА</b></color></size>\n<b>{1}</b> из {2} технологий  <color=#A9A392>· {3:0}%</color>",
                LGBuild.Hex(Gold), tm.ResearchedCount, need, Mathf.Clamp01(tm.ResearchedCount / (float)Mathf.Max(1, need)) * 100f);
        }

        private void RefreshFilters()
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return;
            int avail = 0, work = 0;
            foreach (var t in tm.AllTechs)
            {
                var st = StateOf(t, tm, out _, out _);
                if (st == St.Avail) avail++;
                if (st == St.Active || st == St.Paused || st == St.Queued || st == St.Planned) work++;
            }
            foreach (var (f, bg, label) in _filterBtns)
            {
                bool on = f == _filter;
                if (bg.TryGetComponent<LGAstrolabe>(out var astro)) astro.Selected = on;
                label.text = f switch
                {
                    Filter.Avail => Loc.F("Доступно {0}", avail),
                    Filter.Work => Loc.F("В работе {0}", work),
                    Filter.Done => Loc.F("Изучено {0}", tm.ResearchedCount),
                    _ => Loc.F("Все {0}", tm.AllTechs.Count)
                };
                label.color = on ? Cyan : Muted;
                bg.color = on ? new Color(Cyan.r * 0.18f, Cyan.g * 0.18f, Cyan.b * 0.18f, 0.9f) : new Color(0, 0, 0, 0);
            }
        }

        // ==================== КАРТА ====================

        private void RebuildMatrix(TechnologyManager tm)
        {
            LGBuild.Clear(_wiresLayer);
            LGBuild.Clear(_chipsLayer);
            for (int i = _staticLayer.childCount - 1; i >= 0; i--)
            {
                var ch = _staticLayer.GetChild(i);
                if (ch != _nowLine) Destroy(ch.gameObject);
            }
            _chips.Clear();

            int year = CurrentYear;
            var lanes = new List<TechCategory>();
            foreach (var b in TechBranchInfo.All) lanes.AddRange(TechBranchInfo.Lanes(b));
            _rowH = (MatrixH - EpochHeadH - LegendH - GroupGap * (TechBranchInfo.All.Length - 1) - 6f) / lanes.Count;

            // Заголовки эпох и штриховка будущего
            for (int i = 0; i < TierCount; i++)
            {
                float x = LabelW + 8f + i * ColW;
                bool future = TierYears[i] > year;
                var sep = LGBuild.Panel(_staticLayer, "Sep", new Color(1f, 1f, 1f, 0.06f));
                sep.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, 0), new Vector2(1, MatrixH - LegendH));
                LG.Ignore(sep.gameObject);
                if (future)
                {
                    var hatch = LGBuild.Panel(_staticLayer, "Hatch", new Color(1f, 1f, 1f, 0.05f));
                    hatch.sprite = HatchSprite();
                    hatch.type = Image.Type.Tiled;
                    hatch.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x + 1, -EpochHeadH), new Vector2(ColW - 1, MatrixH - EpochHeadH - LegendH));
                    LG.Ignore(hatch.gameObject);
                }
                var cap = LGBuild.Label(_staticLayer, TierNames[i], 10, Dim, TextAnchor.UpperLeft, bold: true);
                cap.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x + 14, -12), new Vector2(ColW - 20, 14));
                cap.Track(1.8f);
                string when = future ? Loc.F("через {0} {1}", TierYears[i] - year, YearsWord(TierYears[i] - year)) : Loc.T("эпоха наступила");
                var yl = LGBuild.Label(_staticLayer, $"{TierYears[i]}  <size=11><color=#{ColorUtility.ToHtmlStringRGB(Dim)}>{when}</color></size>", 18,
                    future ? Dim : Gold, TextAnchor.UpperLeft, bold: true);
                yl.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x + 14, -28), new Vector2(ColW - 20, 24));
                yl.raycastTarget = true;
                TooltipHelper.Attach(yl.gameObject, Loc.F("<b>{0} · {1}</b>\nСтоимость ×{2:0.#} к базовой.\n", TierNames[i], TierYears[i], TechnologyManager.TierCostFactor(i)) +
                    Loc.T("Технологию можно начать и раньше её эпохи, но изучение будет дольше. План ждёт наступления эпохи."));
            }

            // Отметка «сейчас»
            float nowX = LabelW + 8f + ColW * YearToColumn(year);
            _nowLine.At(new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(nowX, -EpochHeadH + 4f), new Vector2(2f, MatrixH - EpochHeadH - LegendH));
            _nowLine.SetAsLastSibling();
            _nowBadge.text = Loc.F("СЕЙЧАС {0}", year);
            ((RectTransform)_nowBadge.transform.parent).anchoredPosition = new Vector2(nowX, -EpochHeadH + 6f);
            _nowBadge.transform.parent.SetSiblingIndex(_chipsLayer.GetSiblingIndex() + 1);

            // Полосы направлений по наукам
            float y = EpochHeadH + 2f;
            foreach (var b in TechBranchInfo.All)
            {
                float top = y;
                Color bc = TechBranchInfo.Color(b);
                foreach (var cat in TechBranchInfo.Lanes(b))
                {
                    BuildLane(tm, cat, bc, y);
                    y += _rowH;
                }
                // «Атлас»: группа — без заливки, золотая моноширинная подпись по вертикали и тонкая линия
                var band = LGBuild.Panel(_staticLayer, "Band", new Color(0f, 0f, 0f, 0f));
                band.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -top), new Vector2(BandW, y - top));
                LG.Ignore(band.gameObject);
                var bl = LGBuild.Label(band.transform, TechBranchInfo.Name(b).ToUpper(), 10, Gold, TextAnchor.MiddleCenter);
                bl.font = GameFont.Mono;
                bl.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(y - top, BandW));
                bl.rectTransform.localRotation = Quaternion.Euler(0, 0, 90f);
                bl.Track(3f);
                var line = LGBuild.Panel(_staticLayer, "GroupLine", new Color(0.925f, 0.898f, 0.827f, 0.20f));
                line.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -(y + GroupGap * 0.5f)), new Vector2(MatrixW, 1));
                LG.Ignore(line.gameObject);
                y += GroupGap;
            }

            BuildLegend();
        }

        private void BuildLane(TechnologyManager tm, TechCategory cat, Color bc, float y)
        {
            int total = 0, done = 0;
            foreach (var t in tm.AllTechs)
                if (t.Category == cat) { total++; if (t.IsResearched) done++; }

            // Подпись направления: рисунок, название, полоса «изучено / всего»
            var lbl = LGBuild.Panel(_staticLayer, "Lane_" + cat, new Color(0, 0, 0, 0), raycast: true);
            lbl.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(BandW + 6f, -y), new Vector2(LabelW - BandW - 10f, _rowH));
            // «Атлас»: линейный значок в тонком кремовом круге, название — Spectral обычным регистром, полоса — золото
            var ring = LGBuild.Panel(lbl.transform, "IconRing", new Color(0.925f, 0.898f, 0.827f, 0.26f));
            ring.sprite = HudArt.Ring(0.94f, 1f);
            ring.raycastTarget = false;
            ring.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(30, 30));
            LG.Ignore(ring.gameObject);
            var ic = LGIcons.Create(lbl.transform, LGIcons.ForTechCategory(cat), 15f, Muted);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(11.5f, 0), new Vector2(15, 15));
            var nm = LGBuild.Label(lbl.transform, TechCategoryInfo.Name(cat), 15, Primary, TextAnchor.MiddleLeft);
            nm.font = GameFont.Display;
            nm.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(40, 8), new Vector2(LabelW - BandW - 54f, 22));
            FitText(nm, 10);
            var barHost = LGBuild.Rect(lbl.transform, "Bar");
            barHost.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(40, -12), new Vector2(LabelW - BandW - 100f, 6));
            LGBuild.Bar(barHost, Gold, done / (float)Mathf.Max(1, total), 3f);
            var cnt = LGBuild.Label(lbl.transform, $"{done}/{total}", 11, Dim, TextAnchor.MiddleRight);
            cnt.font = GameFont.Mono;
            cnt.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-6, -12), new Vector2(50, 16));
            TooltipHelper.Attach(lbl.gameObject, Loc.F("<b>{0}</b>\nИзучено {1} из {2}", TechCategoryInfo.Name(cat), done, total));

            var sepLine = LGBuild.Panel(_staticLayer, "LaneLine", new Color(1f, 1f, 1f, 0.035f));
            sepLine.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(BandW, -(y + _rowH)), new Vector2(MatrixW - BandW, 1));
            LG.Ignore(sepLine.gameObject);

            // Плашки технологий по эпохам
            for (int tier = 0; tier < TierCount; tier++)
            {
                var cell = new List<Technology>();
                foreach (var t in tm.AllTechs) if (t.Category == cat && Mathf.Clamp(t.Tier, 0, TierCount - 1) == tier) cell.Add(t);
                if (cell.Count == 0) continue;
                cell.Sort((a, b) => a.GridColumn.CompareTo(b.GridColumn));
                float block = cell.Count * ChipH + (cell.Count - 1) * ChipGap;
                float cy = y + (_rowH - block) * 0.5f;
                float cx = LabelW + 8f + tier * ColW + 8f;
                foreach (var t in cell)
                {
                    _chips[t.Id] = BuildChip(t, tm, new Vector2(cx, -cy));
                    cy += ChipH + ChipGap;
                }
            }
        }

        private ChipUi BuildChip(Technology t, TechnologyManager tm, Vector2 pos)
        {
            var st = StateOf(t, tm, out var slot, out int qpos);
            Color col = StateColor(st, t);

            var bg = LGBuild.Panel(_chipsLayer, "Chip_" + t.Id, ChipBg, raycast: true);
            var rt = bg.rectTransform;
            rt.At(new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(ChipW, ChipH));
            var c = new ChipUi { Tech = t, Rt = rt, Bg = bg };
            c.Group = bg.gameObject.AddComponent<CanvasGroup>();
            c.Fx = LG.Platter(bg.gameObject, 7f);
            c.Fx.AsSurface = true;   // узел технологии — плашка с цветом состояния, а не кнопка-капсула
            c.Fx.FillMultiplier = 2.6f;
            c.Fx.SpecularMultiplier = 0.15f;
            c.Hover = bg.gameObject.AddComponent<TechCardFX>();
            c.Hover.Fx = c.Fx;
            var btn = bg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => OnChipClick(t));

            bool lit = st != St.Locked && st != St.Early;
            bg.color = lit ? new Color(col.r * 0.13f + 0.02f, col.g * 0.13f + 0.03f, col.b * 0.13f + 0.04f, 0.97f) : ChipBg;
            c.Hover.SetBase(new Color(col.r, col.g, col.b, lit ? 0.55f : 0.14f), st == St.Active);

            c.Icon = TechIcon(rt, t, 17f, lit ? Color.Lerp(col, Color.white, 0.25f) : new Color(Dim.r, Dim.g, Dim.b, 0.8f));
            c.Icon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(5, 0), new Vector2(17, 17));

            Color nameCol = st switch
            {
                // «Атлас»: изученное — золото, остальное — кремовый текст, закрытое — приглушённое
                St.Done => new Color(0.906f, 0.757f, 0.439f),
                St.Avail or St.Active or St.Planned or St.Queued => new Color(0.925f, 0.898f, 0.827f),
                _ => new Color(0.55f, 0.53f, 0.48f)
            };
            c.Name = LGBuild.Label(rt, t.Name, 12, nameCol, TextAnchor.MiddleLeft, bold: st == St.Active || st == St.Avail);
            c.Name.rectTransform.Stretch(27, 0, 34, 0);
            c.Name.horizontalOverflow = HorizontalWrapMode.Wrap;
            FitText(c.Name, 9);

            // Справа — галочка, процент, номер в плане или замок
            switch (st)
            {
                case St.Active:
                case St.Paused:
                    c.Pct = LGBuild.Label(rt, $"{(slot != null ? slot.ProgressNormalized : 0f) * 100f:0}%", 10, st == St.Paused ? Gold : Coral, TextAnchor.MiddleRight, bold: true);
                    c.Pct.rectTransform.Stretch(0, 0, 6, 0);
                    break;
                case St.Planned:
                case St.Queued:
                    var n = LGBuild.Label(rt, st == St.Planned ? $"№{qpos + 1}" : Loc.T("очер."), 10, PathBlue, TextAnchor.MiddleRight, bold: true);
                    n.rectTransform.Stretch(0, 0, 6, 0);
                    break;
                default:
                    LGIcon mark = st == St.Done ? LGIcon.Check : st == St.Early ? LGIcon.Clock : st == St.Locked ? LGIcon.Lock : LGIcon.Play;
                    c.Mark = LGIcons.Create(rt, mark, 11, st == St.Done ? Gold : st == St.Avail ? Cyan : new Color(Dim.r, Dim.g, Dim.b, 0.8f));
                    c.Mark.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-7, 0), new Vector2(11, 11));
                    break;
            }

            // Прогресс — тонкая полоса снизу
            if (st == St.Active || st == St.Paused || (t.ProgressNormalized > 0.001f && st != St.Done))
            {
                var pg = LGBuild.Panel(rt, "Prog", st == St.Paused ? Gold : st == St.Active ? Coral : new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f));
                c.Prog = pg.rectTransform;
                c.Prog.anchorMin = new Vector2(0, 0);
                c.Prog.anchorMax = new Vector2(slot != null ? slot.ProgressNormalized : t.ProgressNormalized, 0);
                c.Prog.pivot = new Vector2(0, 0);
                c.Prog.offsetMin = new Vector2(3, 1);
                c.Prog.offsetMax = new Vector2(-3, 3);
                LG.Ignore(pg.gameObject);
            }

            TooltipHelper.Attach(bg.gameObject, ChipTooltip(t, st));
            return c;
        }

        private void BuildLegend()
        {
            var host = LGBuild.Rect(_staticLayer, "Legend");
            host.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(LabelW + 14f, 6f), new Vector2(MatrixW - LabelW - 20f, 18f));
            var items = new (St st, string name)[]
            {
                (St.Done, Loc.T("Изучено")), (St.Active, Loc.T("Исследуется")), (St.Planned, Loc.T("В плане")),
                (St.Avail, Loc.T("Доступно")), (St.Early, Loc.T("Ждёт эпохи")), (St.Locked, Loc.T("Закрыто"))
            };
            float x = 0f;
            foreach (var (st, name) in items)
            {
                var sw = LGBuild.Panel(host, "Sw", StateColor(st, null));
                sw.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(9, 9));
                LG.Ignore(sw.gameObject);
                var l = LGBuild.Label(host, name, 11, Muted, TextAnchor.MiddleLeft);
                l.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 14, 0), new Vector2(110, 18));
                x += 22f + l.preferredWidth + 14f;
            }
            var pl = LGBuild.Panel(host, "PathSw", PathBlue);
            pl.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(18, 2));
            LG.Ignore(pl.gameObject);
            var pt = LGBuild.Label(host, Loc.T("путь к выбранной технологии · двойной щелчок — сразу в работу"), 11, Muted, TextAnchor.MiddleLeft);
            pt.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 24, 0), new Vector2(460, 18));
        }

        // ==================== ФОКУС И ПУТЬ ====================

        /// <summary>Подсветка: выбранная технология, её путь и прямые предпосылки; фильтр и поиск гасят лишнее.</summary>
        private void ApplyFocus()
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return;
            var lit = new HashSet<Technology>();
            if (_selected != null)
            {
                foreach (var t in tm.PathTo(_selected)) lit.Add(t);
                lit.Add(_selected);
                foreach (var id in _selected.RequiredTechIds) { var r = tm.FindTech(id); if (r != null) lit.Add(r); }
            }
            bool searching = !string.IsNullOrEmpty(_search);

            foreach (var c in _chips.Values)
            {
                var st = StateOf(c.Tech, tm, out _, out _);
                bool pass = _filter switch
                {
                    Filter.Avail => st == St.Avail,
                    Filter.Work => st == St.Active || st == St.Paused || st == St.Queued || st == St.Planned,
                    Filter.Done => st == St.Done,
                    _ => true
                };
                if (searching && !Matches(c.Tech)) pass = false;
                float a = pass ? 1f : 0.22f;
                if (_selected != null && !lit.Contains(c.Tech)) a = Mathf.Min(a, 0.32f);
                c.Group.alpha = a;

                Color col = StateColor(st, c.Tech);
                bool on = st != St.Locked && st != St.Early;
                if (c.Tech == _selected) c.Hover.SetBase(new Color(PathBlue.r, PathBlue.g, PathBlue.b, 1f), true);
                else if (_selected != null && lit.Contains(c.Tech)) c.Hover.SetBase(new Color(PathBlue.r, PathBlue.g, PathBlue.b, 0.75f), false);
                else if (searching && pass) c.Hover.SetBase(new Color(Gold.r, Gold.g, Gold.b, 0.9f), false);
                else c.Hover.SetBase(new Color(col.r, col.g, col.b, on ? 0.55f : 0.14f), st == St.Active);
            }

            // Провода пути
            LGBuild.Clear(_wiresLayer);
            if (_selected != null)
                foreach (var t in lit)
                    foreach (var id in t.RequiredTechIds)
                    {
                        var r = tm.FindTech(id);
                        if (r == null || !lit.Contains(r)) continue;
                        if (_chips.TryGetValue(r.Id, out var from) && _chips.TryGetValue(t.Id, out var to)) DrawWire(from, to, r.IsResearched);
                    }

            RebuildPopover(tm);
        }

        /// <summary>Связь: от правого края одной плашки к левому краю другой — плавная S-кривая.</summary>
        private void DrawWire(ChipUi from, ChipUi to, bool done)
        {
            Color col = new Color(PathBlue.r, PathBlue.g, PathBlue.b, done ? 0.85f : 0.6f);
            Vector2 p0 = from.Rt.anchoredPosition + new Vector2(ChipW, -ChipH * 0.5f);
            Vector2 p3 = to.Rt.anchoredPosition + new Vector2(0f, -ChipH * 0.5f);
            if (p3.x <= p0.x + 4f)
            {
                // В одной колонке (редко): короткая скоба слева
                Vector2 a = from.Rt.anchoredPosition + new Vector2(0f, -ChipH * 0.5f);
                Segment(a, a + new Vector2(-6f, 0f), col, 2f);
                Segment(a + new Vector2(-6f, 0f), p3 + new Vector2(-6f, 0f), col, 2f);
                Segment(p3 + new Vector2(-6f, 0f), p3, col, 2f);
                return;
            }
            float k = (p3.x - p0.x) * 0.5f;
            Vector2 p1 = p0 + new Vector2(k, 0f), p2 = p3 - new Vector2(k, 0f);
            const int Steps = 18;
            Vector2 prev = p0;
            for (int i = 1; i <= Steps; i++)
            {
                float u = i / (float)Steps, iu = 1f - u;
                Vector2 pt = iu * iu * iu * p0 + 3f * iu * iu * u * p1 + 3f * iu * u * u * p2 + u * u * u * p3;
                Segment(prev, pt, col, done ? 2.2f : 1.6f, !done && i == Steps / 2);
                prev = pt;
            }
        }

        private void Segment(Vector2 a, Vector2 b, Color col, float width, bool pulse = false)
        {
            var img = LGBuild.Panel(_wiresLayer, "Wire", col);
            LG.Ignore(img.gameObject);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            Vector2 d = b - a;
            float len = d.magnitude;
            rt.sizeDelta = new Vector2(len + 0.5f, width);
            rt.anchoredPosition = a;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            if (!pulse || len < 8f) return;
            var dot = LGBuild.Panel(rt, "Pulse", Color.white);
            dot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 4f));
            dot.gameObject.AddComponent<WirePulseAnimator>().Init(dot.rectTransform, len, Color.Lerp(col, Color.white, 0.5f));
        }

        // ==================== КАРТОЧКА ВЫБРАННОЙ ТЕХНОЛОГИИ ====================

        private void RebuildPopover(TechnologyManager tm)
        {
            LGBuild.Clear(_popLayer);
            var t = _selected;
            if (t == null || !_chips.TryGetValue(t.Id, out var chip)) return;

            var st = StateOf(t, tm, out var slot, out int qpos);
            Color cc = TechCategoryInfo.Color(t.Category);
            var path = tm.PathTo(t);
            int show = Mathf.Min(path.Count, 6);
            bool hasPath = path.Count > 1;
            string unlocks = UnlocksLine(t, tm);

            float h = 16f + 66f + 10f + 58f + 8f + 22f;
            if (hasPath) h += 26f + show * 22f + (path.Count > show ? 18f : 0f);
            if (unlocks != null) h += 24f;
            h += 12f + 42f + 38f + 14f;

            var pop = LGBuild.Panel(_popLayer, "Popover", new Color(0.055f, 0.094f, 0.157f, 0.98f), raycast: true);
            var prt = pop.rectTransform;
            var pfx = LG.Platter(pop.gameObject, 14f);
            pfx.SetRim(new Color(PathBlue.r, PathBlue.g, PathBlue.b, 0.6f));
            pfx.FillMultiplier = 3f;
            pop.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;   // клики не уходят на фон карты

            // Справа от плашки, если влезает; иначе под ней или над ней (слева обычно лежит путь — его не закрываем)
            Vector2 cp = chip.Rt.anchoredPosition;
            float px, py;
            float bottomLimit = MatrixH - LegendH - 6f;
            if (cp.x + ChipW + 14f + PopW <= MatrixW - 8f)
            {
                px = cp.x + ChipW + 14f;
                py = Mathf.Clamp(-cp.y - 40f, EpochHeadH, bottomLimit - h);
            }
            else
            {
                px = Mathf.Clamp(cp.x + ChipW - PopW, LabelW + 8f, MatrixW - PopW - 8f);
                float below = -cp.y + ChipH + 10f, above = -cp.y - 10f - h;
                py = below + h <= bottomLimit ? below : Mathf.Max(EpochHeadH, above);
            }
            prt.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(px, -py), new Vector2(PopW, h));

            var close = LGControls.IconButton(prt, LGIcon.Close, 24, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.2f), ClearSelection);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -10), new Vector2(24, 24));

            // Шапка: рисунок, ветка и уровень, название, эпоха и цена
            var badge = LGBuild.Panel(prt, "Badge", new Color(cc.r * 0.12f, cc.g * 0.12f, cc.b * 0.12f, 0.95f));
            badge.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -16), new Vector2(66, 66));
            LG.Platter(badge.gameObject, 12f).SetRim(new Color(cc.r, cc.g, cc.b, 0.5f));
            var ic = TechIcon(badge.transform, t, 50f, Color.Lerp(cc, Color.white, 0.35f));
            ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(50, 50));
            if (TechFlavorDatabase.Has(t.Id)) { badge.raycastTarget = true; TooltipHelper.Attach(badge.gameObject, $"<i>{TechFlavorDatabase.Get(t.Id)}</i>"); }

            string tier = t.Tier == 0 ? Loc.T("БАЗОВАЯ") : Loc.T("УРОВЕНЬ ") + Roman(t.Tier);
            var cap = LGBuild.Label(prt, $"{TechCategoryInfo.Name(t.Category).ToUpper()} · {tier}", 10, cc, TextAnchor.UpperLeft, bold: true);
            cap.rectTransform.TopBand(18, 14, 94, 40);
            cap.Track(1.6f);
            FitText(cap, 8);
            var name = LGBuild.Label(prt, t.Name, 19, Primary, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.TopBand(34, 26, 94, 16);
            FitText(name, 13);
            float pen = tm.GetYearPenalty(t);
            var sub = LGBuild.Label(prt, Loc.F("Эпоха {0} · {1:0} очков", t.YearAvailable, t.Cost) +
                (pen > 1.01f ? Loc.F("  <color={0}>· рано ×{1:0.0}</color>", LGBuild.Hex(Orange), pen) : ""), 12, Muted, TextAnchor.UpperLeft);
            sub.rectTransform.TopBand(62, 18, 94, 16);

            float y = 16f + 66f + 10f;
            var eff = LGBuild.Panel(prt, "Effect", new Color(Green.r * 0.10f, Green.g * 0.10f, Green.b * 0.10f, 0.9f));
            eff.rectTransform.TopBand(y, 58f, 16, 16);
            LG.Platter(eff.gameObject, 10f).SetRim(new Color(Green.r, Green.g, Green.b, 0.35f));
            var et = LGBuild.Label(eff.transform, TechConfirmDialog.ColorizeModifiers(t.Description), 14, new Color(0.84f, 1f, 0.90f), TextAnchor.MiddleLeft, bold: true, wrap: true);
            et.rectTransform.Stretch(12, 4, 12, 4);
            FitText(et, 10);
            y += 58f + 8f;

            // Состояние
            var stl = LGBuild.Label(prt, StateLine(t, st, slot, qpos, tm), 13, Primary, TextAnchor.MiddleLeft);
            stl.rectTransform.TopBand(y, 22f, 16, 16);
            FitText(stl, 10);
            y += 22f;

            // Путь к цели
            if (hasPath)
            {
                int branches = 0;
                var seen = new HashSet<TechBranch>();
                foreach (var p in path) if (seen.Add(TechBranchInfo.Of(p.Category))) branches++;
                var pc = LGBuild.Label(prt, Loc.F("ПУТЬ К ЦЕЛИ · {0} {1}", path.Count, TechWord(path.Count)) +
                    (branches > 1 ? Loc.F(" · {0} ВЕТКИ", branches) : ""), 10, PathBlue, TextAnchor.LowerLeft, bold: true);
                pc.rectTransform.TopBand(y, 22f, 16, 16);
                pc.Track(1.6f);
                y += 26f;
                for (int i = 0; i < show; i++)
                {
                    var p = path[i];
                    var pst = StateOf(p, tm, out var ps, out int pq);
                    var row = LGBuild.Panel(prt, "PathRow", new Color(0, 0, 0, 0), raycast: true);
                    row.rectTransform.TopBand(y, 22f, 16, 16);
                    var rb = row.gameObject.AddComponent<Button>();
                    rb.transition = Selectable.Transition.None;
                    var captured = p;
                    rb.onClick.AddListener(() => Select(captured));
                    var n = LGBuild.Label(row.transform, $"{i + 1}.", 11, Muted, TextAnchor.MiddleLeft);
                    n.rectTransform.Stretch(0, 0, 0, 0);
                    var pi = TechIcon(row.transform, p, 16f, StateColor(pst, p));
                    pi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(16, 16));
                    var pn = LGBuild.Label(row.transform, p.Name, 13, p == t ? Color.white : Primary, TextAnchor.MiddleLeft, bold: p == t);
                    pn.rectTransform.Stretch(44, 0, 104, 0);
                    FitText(pn, 10);
                    var ps2 = LGBuild.Label(row.transform, ShortState(pst, ps, pq), 11, StateColor(pst, p), TextAnchor.MiddleRight);
                    ps2.rectTransform.Stretch(0, 0, 0, 0);
                    y += 22f;
                }
                if (path.Count > show)
                {
                    var more = LGBuild.Label(prt, Loc.F("… и ещё {0}", path.Count - show), 11, Muted, TextAnchor.MiddleLeft);
                    more.rectTransform.TopBand(y, 18f, 60, 16);
                    y += 18f;
                }
            }

            if (unlocks != null)
            {
                var ul = LGBuild.Label(prt, unlocks, 12, Muted, TextAnchor.MiddleLeft, wrap: true);
                ul.rectTransform.TopBand(y + 2f, 22f, 16, 16);
                FitText(ul, 9);
                y += 24f;
            }

            // Действия
            y += 12f;
            BuildActions(prt, t, st, slot, path, tm, y);
        }

        private void BuildActions(RectTransform prt, Technology t, St st, ResearchSlot slot, List<Technology> path, TechnologyManager tm, float y)
        {
            string primary = null, secondary = null;
            Color pcol = MenuPrimaryButton.Cyan, scol = UIManager.DS.BtnNeutral;
            bool enabled = true;
            System.Action pAct = null, sAct = null;
            int slotIdx = slot != null ? IndexOf(tm, slot) : -1;
            bool free = tm.HasFreeSlot;

            switch (st)
            {
                case St.Done:
                    primary = Loc.T("ТЕХНОЛОГИЯ ИЗУЧЕНА"); pcol = MenuPrimaryButton.Green; enabled = false;
                    break;
                case St.Active:
                case St.Paused:
                    primary = st == St.Paused ? Loc.T("ПРОДОЛЖИТЬ") : Loc.T("ПРИОСТАНОВИТЬ");
                    pAct = () => { tm.ToggleSlotPause(slotIdx); SFXManager.Play(Sfx.UiClick); };
                    secondary = Loc.T("ОТМЕНИТЬ ИССЛЕДОВАНИЕ"); scol = UIManager.DS.BtnDanger;
                    sAct = () => { tm.CancelCurrent(slotIdx); SFXManager.Play(Sfx.UiBack); };
                    break;
                case St.Queued:
                    primary = Loc.T("В ОЧЕРЕДИ СЛОТА"); enabled = false;
                    secondary = Loc.T("УБРАТЬ ИЗ ОЧЕРЕДИ"); scol = UIManager.DS.BtnDanger;
                    sAct = () => { tm.RemoveFromQueue(t); SFXManager.Play(Sfx.UiBack); };
                    break;
                case St.Planned:
                    if (free && tm.IsTechAvailable(t))
                    {
                        primary = Loc.T("НАЧАТЬ СЕЙЧАС"); pcol = MenuPrimaryButton.Green;
                        pAct = () => StartNow(t);
                    }
                    else { primary = Loc.F("В ПЛАНЕ · №{0}", PlanIndex(tm, t) + 1); enabled = false; }
                    secondary = Loc.T("УБРАТЬ ИЗ ПЛАНА"); scol = UIManager.DS.BtnDanger;
                    sAct = () => { tm.RemoveFromPlan(t); SFXManager.Play(Sfx.UiBack); };
                    break;
                case St.Avail:
                    if (free) { primary = Loc.T("ИССЛЕДОВАТЬ"); pcol = MenuPrimaryButton.Green; pAct = () => StartNow(t); }
                    else { primary = Loc.T("В ПЛАН"); pAct = () => AddPlan(t); }
                    break;
                case St.Early:
                    primary = Loc.T("В ПЛАН — ДОЖДАТЬСЯ ЭПОХИ"); pAct = () => AddPlan(t);
                    if (free)
                    {
                        secondary = Loc.F("НАЧАТЬ РАНО · СРОК ×{0:0.0}", tm.GetYearPenalty(t));
                        sAct = () => StartNow(t);
                    }
                    break;
                default:
                    int missing = 0;
                    foreach (var p in path) if (!tm.InPlan(p) && !tm.IsBeingResearched(p)) missing++;
                    if (missing == 0) { primary = Loc.T("ПУТЬ УЖЕ В ПЛАНЕ"); enabled = false; }
                    else { primary = Loc.F("ВЕСЬ ПУТЬ В ПЛАН · {0}", missing); pAct = () => AddPlan(t); }
                    break;
            }

            var pb = MenuPrimaryButton.Create(prt, primary, 14, () => pAct?.Invoke(), pcol);
            ((RectTransform)pb.transform).TopBand(y, 42f, 16, 16);
            pb.Interactable = enabled;
            pb.Label.color = enabled ? new Color(0.055f, 0.094f, 0.157f) : new Color(0.925f, 0.898f, 0.827f, 0.75f);
            y += 50f;

            if (secondary != null)
            {
                var sb = LGBuild.Button(prt, "Secondary", scol, new Color(1f, 1f, 1f, 0.18f), () => sAct?.Invoke(), null, secondary, 12, 6f);
                ((RectTransform)sb.transform).TopBand(y, 30f, 16, 16);
            }
            else
            {
                string hint = st == St.Locked
                    ? Loc.T("Слоты сами возьмут технологии пути по мере открытия")
                    : st == St.Avail && free ? Loc.T("Займёт свободный научный слот") : null;
                if (hint != null)
                {
                    var hl = LGBuild.Label(prt, hint, 11, Muted, TextAnchor.MiddleCenter);
                    hl.rectTransform.TopBand(y, 24f, 16, 16);
                }
            }
        }

        private static string StateLine(Technology t, St st, ResearchSlot slot, int qpos, TechnologyManager tm)
        {
            string eta = "~" + TechnologyManager.FormatDays(tm.EstimateDays(t));
            return st switch
            {
                St.Done => Loc.F("<color={0}>✔ Изучено</color>", LGBuild.Hex(Gold)),
                St.Active => Loc.F("<color={0}>Исследуется · {1:0}%</color> · {2}", LGBuild.Hex(Coral), slot.ProgressNormalized * 100f, eta),
                St.Paused => Loc.F("<color={0}>Пауза · {1:0}%</color>", LGBuild.Hex(Gold), slot.ProgressNormalized * 100f),
                St.Queued => Loc.F("<color={0}>В очереди слота · №{1}</color> · {2}", LGBuild.Hex(PathBlue), qpos + 1, eta),
                St.Planned => Loc.F("<color={0}>В плане · №{1}</color> · {2}", LGBuild.Hex(PathBlue), qpos + 1, tm.PlanBlocker(t) ?? Loc.T("начнётся, как освободится слот")),
                St.Avail => Loc.F("<color={0}>Доступно</color> · изучение {1}", LGBuild.Hex(Cyan), eta),
                St.Early => Loc.F("<color={0}>Ждёт эпохи {1}</color> · можно начать раньше, но дольше", LGBuild.Hex(Orange), t.YearAvailable),
                _ => Loc.F("<color=#A9A392>Закрыто · {0}</color>", tm.PlanBlocker(t) ?? "")
            };
        }

        private static string ShortState(St st, ResearchSlot slot, int qpos) => st switch
        {
            St.Done => Loc.T("изучено"),
            St.Active => $"{slot.ProgressNormalized * 100f:0}%",
            St.Paused => Loc.T("пауза"),
            St.Queued => Loc.T("в очереди"),
            St.Planned => Loc.F("в плане №{0}", qpos + 1),
            St.Avail => Loc.T("доступно"),
            St.Early => Loc.T("ждёт эпохи"),
            _ => Loc.T("закрыто")
        };

        private static string UnlocksLine(Technology t, TechnologyManager tm)
        {
            var parts = new List<string>();
            if (t.BonusKey == "slot+1") parts.Add(Loc.T("научный слот"));
            if (t.BonusKey == "cns_dst_1") parts.Add(Loc.T("корпус «эсминец»"));
            var sdm = ShipDesignManager.Instance;
            if (sdm != null)
                foreach (var m in sdm.Modules.Values)
                    if (m.RequiredTechId == t.Id) parts.Add(Loc.T("модуль ") + m.Name);
            foreach (var other in tm.AllTechs)
                if (System.Array.IndexOf(other.RequiredTechIds, t.Id) >= 0) parts.Add(other.Name);
            if (parts.Count == 0) return null;
            return Loc.F("<color={0}>Открывает:</color> {1}", LGBuild.Hex(Green), string.Join(", ", parts));
        }

        // ==================== СЛОТЫ ====================

        private void RebuildSlots(TechnologyManager tm)
        {
            LGBuild.Clear(_slotsList);
            _slotUis.Clear();
            int busy = 0;
            foreach (var s in tm.Slots) if (s.CurrentTech != null) busy++;
            _slotsCap.text = Loc.F("СЛОТЫ НАУКИ  <color=#A9A392><size=11>{0} из {1} заняты</size></color>", busy, tm.Slots.Count);

            var openers = new List<Technology>();
            foreach (var t in tm.AllTechs)
                if (t.BonusKey == "slot+1" && !t.IsResearched) openers.Add(t);
            openers.Sort((x, y) => x.Cost.CompareTo(y.Cost));

            bool planReady = false;
            foreach (var t in tm.Plan) if (tm.PlanBlocker(t) == null && !tm.IsBeingResearched(t)) { planReady = true; break; }

            for (int i = 0; i < tm.MaxSlots; i++)
            {
                float top = i * 46f;
                if (i < tm.Slots.Count)
                {
                    var s = tm.Slots[i];
                    if (s.CurrentTech != null) BuildBusySlot(i, s, tm, top);
                    else BuildFreeSlot(i, top, planReady);
                }
                else BuildLockedSlot(i, top, i - tm.Slots.Count < openers.Count ? openers[i - tm.Slots.Count] : null);
            }
        }

        private RectTransform SlotRow(string name, float top, Color tint, Color rim)
        {
            var bg = LGBuild.Panel(_slotsList, name, tint, raycast: true);
            bg.rectTransform.TopBand(top, 40f, 0, 0);
            var fx = LG.Platter(bg.gameObject, 10f);
            fx.SetRim(rim);
            fx.FillMultiplier = 2.6f;
            fx.SpecularMultiplier = 0.15f;
            // «Астролябия»: латунная плитка; цвет состояния слота — в подсветке при наведении
            var astro = LGAstrolabe.Attach(bg.gameObject, bg, null, plain: true);
            if (astro != null) astro.Gem = LGAstrolabe.GemOf(rim);
            return bg.rectTransform;
        }

        private void BuildBusySlot(int i, ResearchSlot s, TechnologyManager tm, float top)
        {
            var t = s.CurrentTech;
            Color c = s.IsPaused ? Gold : Coral;
            var rt = SlotRow("Slot" + i, top, new Color(c.r * 0.08f, c.g * 0.08f, c.b * 0.08f, 0.9f), new Color(c.r, c.g, c.b, 0.45f));
            var sel = rt.gameObject.AddComponent<Button>();
            sel.transition = Selectable.Transition.None;
            sel.onClick.AddListener(() => Select(t));

            var n = LGBuild.Label(rt, (i + 1).ToString(), 12, Muted, TextAnchor.MiddleCenter, bold: true);
            n.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(18, 20));
            var ic = TechIcon(rt, t, 26f, Color.Lerp(c, Color.white, 0.3f));
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(26, 26));
            string q = s.Queue.Count > 0 ? $"  <color={LGBuild.Hex(PathBlue)}>+{s.Queue.Count}</color>" : "";
            var name = LGBuild.Label(rt, t.Name + q, 13, Primary, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.Stretch(58, 18, 104, 5);
            FitText(name, 10);

            var su = new SlotUi { Index = i };
            var barHost = LGBuild.Rect(rt, "Bar");
            barHost.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(58, 8), new Vector2(SideW - 20 - 58 - 150, 6));
            su.Bar = LGBuild.Bar(barHost, c, s.ProgressNormalized, 4f);
            su.Eta = LGBuild.Label(rt, "", 10, Muted, TextAnchor.MiddleLeft);
            su.Eta.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-58, 3), new Vector2(86, 16));
            FitText(su.Eta, 8);
            su.Pct = LGBuild.Label(rt, "", 12, c, TextAnchor.MiddleRight, bold: true);
            su.Pct.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-58, -3), new Vector2(46, 18));
            _slotUis.Add(su);

            int idx = i;
            var pause = LGControls.IconButton(rt, s.IsPaused ? LGIcon.Play : LGIcon.Pause, 20, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.2f),
                () => { TechnologyManager.Instance?.ToggleSlotPause(idx); SFXManager.Play(Sfx.UiClick); });
            ((RectTransform)pause.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-32, 0), new Vector2(20, 20));
            TooltipHelper.Attach(pause.gameObject, s.IsPaused ? Loc.T("Продолжить исследование") : Loc.T("Приостановить слот: наука перейдёт к остальным"));
            var cancel = LGControls.IconButton(rt, LGIcon.Close, 20, UIManager.DS.BgDeep, new Color(Red.r, Red.g, Red.b, 0.4f),
                () => { TechnologyManager.Instance?.CancelCurrent(idx); SFXManager.Play(Sfx.UiBack); });
            ((RectTransform)cancel.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(20, 20));
            TooltipHelper.Attach(cancel.gameObject, Loc.T("Отменить. Накопленный прогресс сохранится в технологии."));
            TooltipHelper.Attach(rt.gameObject, SlotsFooter(tm));
        }

        private void BuildFreeSlot(int i, float top, bool planReady)
        {
            var rt = SlotRow("Free" + i, top, new Color(Cyan.r * 0.06f, Cyan.g * 0.06f, Cyan.b * 0.06f, 0.6f), new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f));
            var n = LGBuild.Label(rt, (i + 1).ToString(), 12, Cyan, TextAnchor.MiddleCenter, bold: true);
            n.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(18, 20));
            var t = LGBuild.Label(rt, planReady ? Loc.T("Свободен — займётся из плана") : Loc.T("Свободен — выберите технологию на карте"),
                13, Cyan, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch(30, 0, 10, 0);
            FitText(t, 10);
        }

        private void BuildLockedSlot(int i, float top, Technology opener)
        {
            var rt = SlotRow("Locked" + i, top, new Color(0.055f, 0.094f, 0.157f, 0.7f), new Color(1f, 1f, 1f, 0.05f));
            var ic = LGIcons.Create(rt, LGIcon.Lock, 12, Dim);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(12, 12));
            var t = LGBuild.Label(rt, opener != null ? Loc.F("Откроет: {0}", opener.Name) : Loc.T("Откроется технологией"), 12,
                new Color(Dim.r, Dim.g, Dim.b, 0.9f), TextAnchor.MiddleLeft);
            t.rectTransform.Stretch(30, 0, 10, 0);
            FitText(t, 9);
            if (opener != null)
            {
                var b = rt.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => Select(opener));
                TooltipHelper.Attach(rt.gameObject, Loc.F("<b>{0}</b>\nНажмите, чтобы показать на карте", opener.Name));
            }
        }

        private static string SlotsFooter(TechnologyManager tm)
        {
            int active = tm.ActiveSlotCount;
            float split = Mathf.Pow(Mathf.Max(1, active), TechnologyManager.SlotSplitExponent);
            return Loc.F("Наука: <color={0}><b>{1:0.#}</b></color>/мес делится между ", LGBuild.Hex(Cyan), tm.MonthlyResearchIncome) +
                   Loc.F("<b>{0}</b> активн. слотами. Каждый получает 1/{1:0.0} — ", active, split) +
                   Loc.T("параллельно быстрее в сумме, но каждая технология медленнее.");
        }

        // ==================== ПЛАН ====================

        private void RebuildPlan(TechnologyManager tm)
        {
            LGBuild.Clear(_planList);
            _planCap.text = Loc.F("ПЛАН ИССЛЕДОВАНИЙ  <color=#A9A392><size=11>{0}</size></color>", tm.Plan.Count);
            if (tm.Plan.Count == 0)
            {
                var e = LGBuild.Label(_planList, Loc.T("План пуст. Выберите на карте технологию — даже закрытую — и нажмите «Весь путь в план». Свободные слоты сами возьмут технологии по порядку, как только они откроются."),
                    12, Muted, TextAnchor.UpperLeft, wrap: true);
                e.lineSpacing = 1.1f;
                return;
            }
            for (int i = 0; i < tm.Plan.Count; i++) BuildPlanRow(tm, tm.Plan[i], i);
            var foot = LGBuild.Label(_planList, Loc.T("Слоты сами берут первую готовую технологию плана — наука не простаивает."), 11, Muted, TextAnchor.UpperLeft, wrap: true);
            foot.lineSpacing = 1.1f;
        }

        private void BuildPlanRow(TechnologyManager tm, Technology t, int i)
        {
            var row = LGBuild.Panel(_planList, "Plan" + i, new Color(1f, 1f, 1f, 0.03f), raycast: true);
            LGBuild.Height(row.gameObject, 42f);
            var astro = LGAstrolabe.Attach(row.gameObject, row, null, plain: true);   // «Астролябия»: выбранная — подсвечена
            if (astro != null) astro.Selected = t == _selected;
            var b = row.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => Select(t));

            string block = tm.PlanBlocker(t);
            Color c = block == null ? Cyan : Dim;
            var n = LGBuild.Label(row.transform, (i + 1).ToString(), 12, Muted, TextAnchor.MiddleCenter, bold: true);
            n.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(18, 20));
            var ic = TechIcon(row.transform, t, 22f, block == null ? Cyan : new Color(Dim.r, Dim.g, Dim.b, 0.9f));
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(22, 22));
            var name = LGBuild.Label(row.transform, t.Name, 13, Primary, TextAnchor.UpperLeft);
            name.rectTransform.Stretch(56, 18, 84, 4);
            FitText(name, 10);
            var sub = LGBuild.Label(row.transform, block ?? Loc.T("готова к старту"), 10, c, TextAnchor.LowerLeft);
            sub.rectTransform.Stretch(56, 4, 84, 20);
            FitText(sub, 8);

            var up = LGControls.IconButton(row.transform, LGIcon.Back, 20, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.15f),
                () => { tm.MovePlan(t, -1); SFXManager.Play(Sfx.UiClick); }, -90f);
            ((RectTransform)up.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-56, 0), new Vector2(20, 20));
            var down = LGControls.IconButton(row.transform, LGIcon.Back, 20, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.15f),
                () => { tm.MovePlan(t, 1); SFXManager.Play(Sfx.UiClick); }, 90f);
            ((RectTransform)down.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-32, 0), new Vector2(20, 20));
            var del = LGControls.IconButton(row.transform, LGIcon.Close, 20, UIManager.DS.BgDeep, new Color(Red.r, Red.g, Red.b, 0.35f),
                () => { tm.RemoveFromPlan(t); SFXManager.Play(Sfx.UiBack); });
            ((RectTransform)del.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(20, 20));
            TooltipHelper.Attach(up.gameObject, Loc.T("Выше в плане"));
            TooltipHelper.Attach(down.gameObject, Loc.T("Ниже в плане"));
            TooltipHelper.Attach(del.gameObject, Loc.T("Убрать из плана"));
        }

        // ==================== ДЕЙСТВИЯ ====================

        private void OnChipClick(Technology t)
        {
            float now = Time.unscaledTime;
            bool dbl = _lastClickTech == t && now - _lastClickTime < DoubleClick;
            _lastClickTech = t;
            _lastClickTime = now;
            var tm = TechnologyManager.Instance;
            if (dbl && tm != null)
            {
                var st = StateOf(t, tm, out _, out _);
                if (st == St.Avail) { if (tm.HasFreeSlot) StartNow(t); else AddPlan(t); }
                else if (st == St.Locked || st == St.Early) AddPlan(t);
                return;
            }
            if (_selected == t) { ClearSelection(); return; }
            Select(t);
            SFXManager.Play(Sfx.UiClick);
        }

        private void Select(Technology t)
        {
            _selected = t;
            if (t != null) _backdrop.SetBranch(TechBranchInfo.Key(TechBranchInfo.Of(t.Category)));
            ApplyFocus();
            var tm = TechnologyManager.Instance;
            if (tm != null) RebuildPlan(tm);
        }

        private void ClearSelection()
        {
            if (_selected == null) return;
            _selected = null;
            ApplyFocus();
            var tm = TechnologyManager.Instance;
            if (tm != null) RebuildPlan(tm);
        }

        private void StartNow(Technology t)
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return;
            if (tm.InPlan(t)) tm.RemoveFromPlan(t);
            if (tm.AssignTech(t)) SFXManager.Play(Sfx.ResearchStart);
            else SFXManager.Play(Sfx.UiDenied);
            Refresh();
        }

        private void AddPlan(Technology t)
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return;
            int n = tm.PlanPath(t);
            SFXManager.Play(n > 0 ? Sfx.OrderQueue : Sfx.UiDenied);
            if (n > 1)
                NotificationCenter.Show(Loc.T("План исследований"), Loc.F("Путь к «{0}»: добавлено технологий — {1}", t.Name, n), NotificationCenter.Kind.Info, 3.5f);
            Refresh();
        }

        private void OnSearchChanged(string val)
        {
            _search = (val ?? "").Trim().ToLowerInvariant();
            if (_isOpen) ApplyFocus();
        }

        private bool Matches(Technology t)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return t.Name.ToLowerInvariant().Contains(_search)
                || (t.Description != null && t.Description.ToLowerInvariant().Contains(_search))
                || TechCategoryInfo.Name(t.Category).ToLowerInvariant().Contains(_search);
        }

        // ==================== СОСТОЯНИЯ И ВСПОМОГАТЕЛЬНОЕ ====================

        private static int CurrentYear => TimeManager.Instance != null ? TimeManager.Instance.Year : 2200;

        private static St StateOf(Technology t, TechnologyManager tm, out ResearchSlot slot, out int queuePos)
        {
            slot = null;
            queuePos = -1;
            if (t.IsResearched) return St.Done;
            foreach (var s in tm.Slots)
            {
                if (s.CurrentTech == t) { slot = s; return s.IsPaused ? St.Paused : St.Active; }
                int qi = s.Queue.IndexOf(t);
                if (qi >= 0) { slot = s; queuePos = qi; return St.Queued; }
            }
            int pi = PlanIndex(tm, t);
            if (pi >= 0) { queuePos = pi; return St.Planned; }
            if (!tm.IsTechAvailable(t)) return St.Locked;
            return t.YearAvailable > CurrentYear ? St.Early : St.Avail;
        }

        private static int PlanIndex(TechnologyManager tm, Technology t)
        {
            for (int i = 0; i < tm.Plan.Count; i++) if (tm.Plan[i] == t) return i;
            return -1;
        }

        private static Color StateColor(St st, Technology t) => st switch
        {
            St.Done => Gold,
            St.Active => Coral,
            St.Paused => Gold,
            St.Queued or St.Planned => PathBlue,
            St.Avail => Cyan,
            St.Early => new Color(0.66f, 0.58f, 0.42f),
            _ => new Color(0.42f, 0.41f, 0.38f)
        };

        private static int IndexOf(TechnologyManager tm, ResearchSlot slot)
        {
            for (int i = 0; i < tm.Slots.Count; i++) if (tm.Slots[i] == slot) return i;
            return -1;
        }

        /// <summary>Колонка (дробная) года на карте: начало колонки — начало её эпохи.</summary>
        private static float YearToColumn(int year)
        {
            for (int i = 0; i < TierYears.Length - 1; i++)
                if (year < TierYears[i + 1])
                    return i + Mathf.Clamp01((year - TierYears[i]) / (float)(TierYears[i + 1] - TierYears[i]));
            return Mathf.Min(TierCount - 0.05f, TierYears.Length - 1 + (year - TierYears[TierYears.Length - 1]) / 12f);
        }

        private static string ChipTooltip(Technology t, St st)
        {
            string tier = t.Tier == 0 ? Loc.T("базовая") : Loc.F("уровень {0}", Roman(t.Tier));
            string hint = st switch
            {
                St.Avail => Loc.T("Щелчок — подробности, двойной — исследовать"),
                St.Locked => Loc.T("Щелчок — путь к технологии, двойной — весь путь в план"),
                St.Early => Loc.T("Щелчок — подробности, двойной — в план"),
                _ => Loc.T("Щелчок — подробности")
            };
            return $"<b>{t.Name}</b>\n<color=#A9A392>{TechCategoryInfo.Name(t.Category)} · {tier} · {t.YearAvailable}</color>\n" +
                   $"{TechConfirmDialog.ColorizeModifiers(t.Description)}\n\n<color=#A9A392>{hint}</color>";
        }

        private static Image TechIcon(Transform parent, Technology t, float size, Color col)
        {
            var art = LGIcons.TechArt(t.Id, t.Category);
            var ic = LGIcons.Create(parent, LGIcons.ForTechCategory(t.Category), art != null ? size : size * 0.75f, col);
            if (art != null) { ic.sprite = art; ic.preserveAspect = true; }
            ic.raycastTarget = false;
            return ic;
        }

        private static string YearsWord(int n)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return Loc.T("год");
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return Loc.T("года");
            return Loc.T("лет");
        }

        private static string TechWord(int n)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return Loc.T("ТЕХНОЛОГИЯ");
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return Loc.T("ТЕХНОЛОГИИ");
            return Loc.T("ТЕХНОЛОГИЙ");
        }

        /// <summary>Длинное название уменьшается, а не вылезает за плашку.</summary>
        private static void FitText(Text t, int minSize)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = minSize;
            t.resizeTextMaxSize = t.fontSize;
        }

        private static string Roman(int tier) => tier switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => tier.ToString() };

        /// <summary>Декоративный круглый слой (кольцо прогресса победы).</summary>
        private static Image Star(RectTransform parent, string name, Sprite sp, Color col, Vector2 center, float size)
        {
            var img = LGBuild.Panel(parent, name, col);
            img.sprite = sp;
            img.raycastTarget = false;
            img.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), center, new Vector2(size, size));
            LG.Ignore(img.gameObject);
            return img;
        }

        private static Sprite s_ring, s_hatch;

        private static Sprite RingSprite()
        {
            if (s_ring != null) return s_ring;
            const int res = 256;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "TechRing", hideFlags = HideFlags.DontSave };
            var px = new Color32[res * res];
            float c = (res - 1) * 0.5f;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1.3f + 0.5f - Mathf.Abs(d - (1f - 10f / c)) * c / 2.2f);
                px[y * res + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            s_ring = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
            s_ring.hideFlags = HideFlags.DontSave;
            return s_ring;
        }

        /// <summary>Косая штриховка будущих эпох (повторяется плиткой).</summary>
        private static Sprite HatchSprite()
        {
            if (s_hatch != null) return s_hatch;
            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "TechHatch", hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int d = (x + y) % n;
                byte a = (byte)(d < 3 ? 255 : 0);
                px[y * n + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            s_hatch = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
            s_hatch.hideFlags = HideFlags.DontSave;
            return s_hatch;
        }

        // ==================== ЭФФЕКТЫ ====================

        /// <summary>Наведение на плашку: лёгкое увеличение и яркая кромка; изучаемая — пульсирует.</summary>
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
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * (_hovered ? 1.03f : 1f), k);
                if (Fx == null) return;
                Color want = _hovered ? new Color(1f, 1f, 1f, 0.85f)
                    : Pulse ? new Color(BaseRim.r, BaseRim.g, BaseRim.b, 0.55f + Mathf.Sin(Time.unscaledTime * 4.5f) * 0.35f)
                    : BaseRim;
                if (want == _applied) return;   // не трогаем материал без нужды
                _applied = want;
                Fx.SetRim(want);
            }
        }

        /// <summary>Светящаяся «капля», бегущая по проводу пути.</summary>
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
