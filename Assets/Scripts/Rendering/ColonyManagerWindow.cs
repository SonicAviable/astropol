using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// «Управление колониями» (клавиша C, пункт левого меню) — все колонии империи одной таблицей:
    /// жители и рост, стабильность, производство, приоритет, текущая стройка с прогрессом и кнопками
    /// «Купить» / «Строить…» / автостройка. Слева — сводка империи и подсказка, где колонии простаивают.
    /// Клик по названию — окно планеты. Заголовки столбцов сортируют таблицу.
    /// </summary>
    public class ColonyManagerWindow : MonoBehaviour
    {
        public static ColonyManagerWindow Instance { get; private set; }
        public static bool IsOpenNow => Instance != null && Instance.IsOpen;
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private const float SideW = 300f, RowH = 66f, HeadH = 30f;

        private GameObject _root;
        private Text _subtitle;
        private RectTransform _list;
        private readonly List<Row> _rows = new List<Row>();
        private string _rowsKey = "";
        private float _refresh;
        private Sort _sort = Sort.Population;
        private bool _sortDesc = true;
        private readonly List<Text> _sideValues = new List<Text>();
        private Text _sideIdle;
        private Text _autoAllText;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static readonly Color CPanel = new Color(0.020f, 0.045f, 0.065f, 0.90f);

        private enum Sort { Name, Population, Stability, Minerals, Energy, Alloys, Science }

        // Столбцы: левая граница (px от начала таблицы) и ширина
        private static readonly float[] ColX = { 12f, 262f, 392f, 520f, 592f, 664f, 736f, 812f, 968f };
        private static readonly float[] ColW = { 240f, 120f, 118f, 66f, 66f, 66f, 66f, 146f, 0f };

        private class Row
        {
            public PlanetData Planet;
            public GameObject Go;
            public Image Bg, World, Idle;
            public Text Name, Sub, Pop, Growth, Stab, Min, En, Al, Sci, Focus, Job, JobDays, BuyText, Auto;
            public RectTransform StabBar, JobBar;
            public Image JobIcon, BuyBg, AutoBg;
            public Button Buy, AutoBtn;
            public TooltipTrigger StabTip, BuyTip, JobTip, PopTip;
        }

        public static void Toggle()
        {
            if (Instance == null) new GameObject("[UI] ColonyManagerWindow").AddComponent<ColonyManagerWindow>();
            if (Instance.IsOpen) Instance.Close(); else Instance.Open();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            bool typing = IsTyping();
            if (Input.GetKeyDown(KeyCode.C) && UIManager.IsGameStarted && !typing && !BuildMenuModal.IsOpenNow
                && !(PlanetFocusOverlay.Instance != null && PlanetFocusOverlay.Instance.IsOpen)) Toggle();
            if (!IsOpen) return;
            if (BuildMenuModal.IsOpenNow) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if ((_refresh -= Time.unscaledDeltaTime) <= 0f) { _refresh = 0.5f; Refresh(); }
        }

        private static bool IsTyping()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            return go != null && go.GetComponent<InputField>() != null;
        }

        public void Open()
        {
            if (_root == null) Build();
            if (_root == null) return;
            _rowsKey = "";
            Refresh();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            LG.Skin(_root.transform);
        }

        public void Close()
        {
            if (_root == null) return;
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            TooltipSystem.Hide();
        }

        // ================================================================ Данные

        /// <summary>Производство планеты за месяц с учётом фракции, технологий, губернатора, видов и стабильности.</summary>
        public static float Output(PlanetData p, string res)
        {
            if (p == null || p.Population <= 0) return 0f;
            var eco = EconomyManager.Instance;
            var b = EmpireBonuses.For(0);
            float baseOut = res == "minerals" ? p.ProducedMineralsPerMonth : res == "energy" ? p.ProducedEnergyPerMonth : p.ProducedAlloysPerMonth;
            float fac = eco == null ? 1f : res == "minerals" ? eco.FactionMineralMult : res == "energy" ? eco.FactionEnergyMult : eco.FactionAlloyMult;
            float tech = res == "minerals" ? b.MineralsMult : res == "energy" ? b.EnergyMult : b.AlloysMult;
            float st = p.HasMiningStation ? (res == "minerals" ? p.MineralDeposit : res == "energy" ? p.EnergyDeposit : 0f) : 0f;
            return baseOut * fac * tech * LeaderManager.PlanetOutputMult(p, res) * p.OutputMult(res) + st * fac * tech;
        }

        public static float Science(PlanetData p)
        {
            var tm = TechnologyManager.Instance;
            return p.ResearchPops * TechnologyManager.SciencePerPop * (tm != null ? tm.GlobalResearchSpeedMultiplier : 1f);
        }

        private static List<PlanetData> Colonies()
        {
            var list = new List<PlanetData>();
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != 0) continue;
                foreach (var p in s.Planets) if (p.Population > 0) list.Add(p);
            }
            return list;
        }

        private float SortValue(PlanetData p) => _sort switch
        {
            Sort.Population => p.Population,
            Sort.Stability => p.Stability,
            Sort.Minerals => Output(p, "minerals"),
            Sort.Energy => Output(p, "energy"),
            Sort.Alloys => Output(p, "alloys"),
            Sort.Science => Science(p),
            _ => 0f
        };

        private void SetSort(Sort s)
        {
            if (_sort == s) _sortDesc = !_sortDesc;
            else { _sort = s; _sortDesc = s != Sort.Name; }
            SFXManager.Play(Sfx.UiClick, 0.5f);
            _rowsKey = "";
            Refresh();
        }

        // ================================================================ Построение

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            var rt = LGBuild.Rect(host.transform, "ColonyManager");
            rt.Stretch(28, 28, 28, 28);
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.010f, 0.025f, 0.038f, 0.97f);
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.42f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            var accent = LGBuild.Panel(rt, "Accent", new Color(0.95f, 0.42f, 0.42f));
            accent.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -24), new Vector2(5, 30));
            var title = LGBuild.Label(rt, Loc.T("УПРАВЛЕНИЕ КОЛОНИЯМИ"), 26, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(42, -18), new Vector2(700, 36));
            title.Track(1.6f);
            _subtitle = LGBuild.Label(rt, "", 12, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(43, -54), new Vector2(1000, 18));
            var close = LGBuild.Button(rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc / C)</color>"));

            BuildSide(rt);

            // Таблица
            var table = LGBuild.Panel(rt, "Table", CPanel);
            table.rectTransform.anchorMin = Vector2.zero; table.rectTransform.anchorMax = Vector2.one;
            table.rectTransform.offsetMin = new Vector2(SideW + 40f, 20f); table.rectTransform.offsetMax = new Vector2(-20f, -88f);
            LG.Platter(table.gameObject, 16f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.18f));
            var head = LGBuild.Rect(table.rectTransform, "Head");
            head.TopBand(8, HeadH, 10, 10);
            HeadCell(head, 0, Loc.T("КОЛОНИЯ"), Sort.Name, null);
            HeadCell(head, 1, Loc.T("ЖИТЕЛИ"), Sort.Population, LGIcon.Population);
            HeadCell(head, 2, Loc.T("СТАБИЛЬНОСТЬ"), Sort.Stability, LGIcon.Society);
            HeadCell(head, 3, "", Sort.Minerals, LGIcon.Minerals);
            HeadCell(head, 4, "", Sort.Energy, LGIcon.Energy);
            HeadCell(head, 5, "", Sort.Alloys, LGIcon.Alloys);
            HeadCell(head, 6, "", Sort.Science, LGIcon.Research);
            HeadCell(head, 7, Loc.T("ПРИОРИТЕТ"), null, null);
            HeadCell(head, 8, Loc.T("СТРОИТСЯ"), null, LGIcon.Construction);
            var line = LGBuild.Panel(table.rectTransform, "Line", new Color(Gold.r, Gold.g, Gold.b, 0.22f));
            line.rectTransform.TopBand(8 + HeadH + 2, 1, 10, 10);

            var listHost = LGBuild.Rect(table.rectTransform, "ListHost");
            listHost.Stretch(6, 8, 6, 8 + HeadH + 6);
            _list = LGBuild.ScrollList(listHost, 4f, 4);
            _root.SetActive(false);
        }

        private void HeadCell(RectTransform head, int col, string text, Sort? sort, LGIcon? icon)
        {
            var cell = LGBuild.Panel(head, "H" + col, new Color(1f, 1f, 1f, 0f), raycast: sort.HasValue);
            var crt = cell.rectTransform;
            crt.anchorMin = new Vector2(0, 0);
            crt.anchorMax = col == ColX.Length - 1 ? new Vector2(1, 1) : new Vector2(0, 1);
            crt.pivot = new Vector2(0, 0.5f);
            crt.offsetMin = new Vector2(ColX[col], 0);
            crt.offsetMax = col == ColX.Length - 1 ? Vector2.zero : new Vector2(ColX[col] + ColW[col], 0);
            float x = 0f;
            if (icon.HasValue)
            {
                Color ic = icon.Value == LGIcon.Minerals ? BuildMenuModal.CMinerals : icon.Value == LGIcon.Energy ? BuildMenuModal.CEnergy
                         : icon.Value == LGIcon.Alloys ? BuildMenuModal.CAlloys : icon.Value == LGIcon.Research ? new Color(0.5f, 0.92f, 0.62f) : Muted;
                var i = LGIcons.Create(cell.transform, icon.Value, 14, ic);
                i.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(14, 14));
                x = 20f;
            }
            var t = LGBuild.Label(cell.transform, text, 10, Gold, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch(x, 0, 0, 0);
            t.Track(1.2f);
            if (sort.HasValue)
            {
                var b = cell.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                var s = sort.Value;
                b.onClick.AddListener(() => SetSort(s));
                string what = icon == LGIcon.Minerals ? Loc.T("титан в месяц") : icon == LGIcon.Energy ? Loc.T("гелий-3 в месяц")
                            : icon == LGIcon.Alloys ? Loc.T("сплавы в месяц") : icon == LGIcon.Research ? Loc.T("наука в месяц") : text.ToLower();
                TooltipHelper.Attach(cell.gameObject, Loc.F("<b>{0}</b>\nНажмите, чтобы отсортировать", what));
            }
        }

        private void BuildSide(RectTransform rt)
        {
            var side = LGBuild.Panel(rt, "Summary", CPanel);
            var s = side.rectTransform;
            s.anchorMin = new Vector2(0, 0); s.anchorMax = new Vector2(0, 1); s.pivot = new Vector2(0, 0.5f);
            s.offsetMin = new Vector2(20, 20); s.offsetMax = new Vector2(20 + SideW, -88);
            LG.Platter(side.gameObject, 16f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.18f));

            var head = LGBuild.Label(s, Loc.T("СВОДКА ИМПЕРИИ"), 11, Gold, TextAnchor.UpperLeft, bold: true);
            head.rectTransform.TopBand(16, 18, 18, 18);
            head.Track(1.4f);
            (LGIcon, Color, string)[] rows =
            {
                (LGIcon.Population, Cyan, Loc.T("Население")),
                (LGIcon.Planet, Cyan, Loc.T("Колонии")),
                (LGIcon.Society, new Color(0.98f, 0.55f, 0.85f), Loc.T("Средняя стабильность")),
                (LGIcon.Energy, BuildMenuModal.CEnergy, Loc.T("Гелий-3")),
                (LGIcon.Minerals, BuildMenuModal.CMinerals, Loc.T("Титан")),
                (LGIcon.Alloys, BuildMenuModal.CAlloys, Loc.T("Сплавы")),
                (LGIcon.Influence, BuildMenuModal.CInfluence, Loc.T("Влияние")),
                (LGIcon.Research, new Color(0.5f, 0.92f, 0.62f), Loc.T("Наука")),
            };
            float y = 44f;
            foreach (var (icon, col, label) in rows)
            {
                var r = LGBuild.Rect(s, "Stat");
                r.TopBand(y, 30, 18, 18);
                var ic = LGIcons.Create(r, icon, 16, col);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16));
                var l = LGBuild.Label(r, label, 12, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
                l.rectTransform.Stretch(26, 0, 0, 0);
                var v = LGBuild.Label(r, "", 13, Color.white, TextAnchor.MiddleRight, bold: true);
                _sideValues.Add(v);
                y += 32f;
                if (label == Loc.T("Средняя стабильность")) y += 10f;
            }

            _sideIdle = LGBuild.Label(s, "", 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            _sideIdle.rectTransform.TopBand(y + 12, 120, 18, 18);
            _sideIdle.lineSpacing = 1.15f;

            var autoAll = LGBuild.Button(s, "AutoAll", UIManager.DS.BtnPrimary, Cyan, ToggleAutoAll, null, null, 11, 10f);
            var aRt = (RectTransform)autoAll.transform;
            aRt.anchorMin = new Vector2(0, 0); aRt.anchorMax = new Vector2(1, 0); aRt.pivot = new Vector2(0.5f, 0);
            aRt.offsetMin = new Vector2(18, 18); aRt.offsetMax = new Vector2(-18, 54);
            _autoAllText = LGBuild.Label(autoAll.transform, "", 11, Color.white, TextAnchor.MiddleCenter, bold: true);
            TooltipHelper.Attach(autoAll.gameObject, Loc.T("<b>Автостройка</b>\nКолония с пустой очередью сама заказывает нужный район в начале месяца: жильё, когда тесно, рабочие места по приоритету."));
        }

        private void ToggleAutoAll()
        {
            var list = Colonies();
            bool anyOff = list.Exists(p => !p.AutoBuild);
            foreach (var p in list) p.AutoBuild = anyOff;
            SFXManager.Play(Sfx.UiConfirm, 0.7f);
            Refresh();
        }

        // ================================================================ Обновление

        private void Refresh()
        {
            if (_root == null) return;
            var list = Colonies();
            list.Sort((a, b) =>
            {
                int c = _sort == Sort.Name ? string.Compare(a.Name, b.Name, System.StringComparison.CurrentCulture)
                                           : SortValue(a).CompareTo(SortValue(b));
                return _sortDesc ? -c : c;
            });

            string key = "";
            foreach (var p in list) key += p.GetHashCode() + ",";
            if (key != _rowsKey)
            {
                _rowsKey = key;
                LGBuild.Clear(_list);
                _rows.Clear();
                for (int i = 0; i < list.Count; i++) _rows.Add(MakeRow(list[i], i));
                if (list.Count == 0)
                {
                    var e = LGBuild.Label(_list, Loc.T("Колоний пока нет. Основайте первую: выберите пригодную планету в своей системе и проект «Колония»."),
                        13, Muted, TextAnchor.MiddleCenter, wrap: true);
                    LGBuild.Height(e.gameObject, 80f);
                }
                LG.Skin(_list);
            }
            foreach (var r in _rows) FillRow(r);
            RefreshSide(list);
        }

        private void RefreshSide(List<PlanetData> list)
        {
            int pop = 0, robots = 0, idle = 0, jobless = 0;
            float stab = 0f, sci = 0f;
            var cm = ConstructionManager.Instance;
            foreach (var p in list)
            {
                pop += p.Population; robots += p.RobotPops; stab += p.Stability * p.Population; sci += Science(p);
                if (cm != null && cm.QueueFor(p).Count == 0 && !p.AutoBuild) idle++;
                jobless += p.IdlePops;
            }
            float avg = pop > 0 ? stab / pop : 0f;
            var eco = EconomyManager.Instance;
            var tm = TechnologyManager.Instance;
            _subtitle.text = Loc.F("{0} · колоний {1} · клик по названию — окно планеты, по заголовку столбца — сортировка", FactionTraits.FactionOf(0)?.Name ?? Loc.T("Ваша империя"), list.Count);
            string Inc(float stock, float inc) => $"{stock:0} <color={(inc >= 0f ? "#9EF0C8" : "#FF8A8A")}>{(inc >= 0f ? "+" : "")}{inc:0}</color>";
            int i = 0;
            _sideValues[i++].text = robots > 0 ? Loc.F("{0} <color=#8AA2A8>(роботов {1})</color>", pop, robots) : pop.ToString();
            _sideValues[i++].text = list.Count.ToString();
            var tier = PlanetStability.TierOf(avg);
            _sideValues[i++].text = $"<color=#{PlanetStability.Hex(PlanetStability.TierColor(tier))}>{avg:0} · {PlanetStability.TierName(tier)}</color>";
            _sideValues[i++].text = eco != null ? Inc(eco.EnergyCredits, eco.MonthlyEnergyIncome) : "—";
            _sideValues[i++].text = eco != null ? Inc(eco.Minerals, eco.MonthlyMineralsIncome) : "—";
            _sideValues[i++].text = eco != null ? Inc(eco.Alloys, eco.MonthlyAlloysIncome) : "—";
            _sideValues[i++].text = eco != null ? Inc(eco.Influence, eco.MonthlyInfluenceIncome) : "—";
            _sideValues[i++].text = tm != null ? Loc.F("+{0:0} / мес", tm.MonthlyResearchIncome) : "—";

            string s = idle > 0 ? Loc.F("<color=#F2C46B>Простаивают: {0}</color> — очередь пуста и автостройка выключена.\n", idle) : Loc.T("<color=#9EF0C8>Все колонии заняты делом.</color>\n");
            if (jobless > 0) s += Loc.F("<color=#F2C46B>Безработных: {0}</color> — нужны районы.\n", jobless);
            int unstable = list.FindAll(p => p.Tier <= StabilityTier.Strike).Count;
            if (unstable > 0) s += Loc.F("<color=#FF8A8A>Неспокойно на {0} планетах</color> — пропаганда или губернатор помогут.", unstable);
            _sideIdle.text = s;
            bool anyOff = list.Exists(p => !p.AutoBuild);
            _autoAllText.text = anyOff ? Loc.T("ВКЛЮЧИТЬ АВТОСТРОЙКУ ВЕЗДЕ") : Loc.T("ВЫКЛЮЧИТЬ АВТОСТРОЙКУ ВЕЗДЕ");
        }

        // ================================================================ Строка колонии

        private Text Cell(RectTransform row, int col, int size, Color c, bool bold = true, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var t = LGBuild.Label(row, "", size, c, anchor, bold: bold);
            var r = t.rectTransform;
            r.anchorMin = new Vector2(0, 0); r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 0.5f);
            r.offsetMin = new Vector2(ColX[col], 0); r.offsetMax = new Vector2(ColX[col] + ColW[col], 0);
            return t;
        }

        private Row MakeRow(PlanetData p, int index)
        {
            var r = new Row { Planet = p };
            var bg = LGBuild.Panel(_list, "Colony", new Color(1f, 1f, 1f, index % 2 == 0 ? 0.035f : 0.02f), raycast: true);
            r.Go = bg.gameObject; r.Bg = bg;
            LGBuild.Height(bg.gameObject, RowH);
            LG.Platter(bg.gameObject, 12f).SetRim(new Color(1f, 1f, 1f, 0.06f));
            var row = bg.rectTransform;

            r.Idle = LGBuild.Panel(row, "IdleEdge", Gold);
            r.Idle.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(3, RowH - 18));
            LG.Ignore(r.Idle.gameObject);

            // Колония: шар цвета мира, название, класс; клик — окно планеты
            var nameBtn = LGBuild.Panel(row, "Open", new Color(1f, 1f, 1f, 0f), raycast: true);
            nameBtn.rectTransform.anchorMin = new Vector2(0, 0); nameBtn.rectTransform.anchorMax = new Vector2(0, 1);
            nameBtn.rectTransform.offsetMin = new Vector2(ColX[0], 4); nameBtn.rectTransform.offsetMax = new Vector2(ColX[0] + ColW[0], -4);
            var nb = nameBtn.gameObject.AddComponent<Button>();
            nb.transition = Selectable.Transition.None;
            nb.onClick.AddListener(() => OpenPlanet(p));
            TooltipHelper.Attach(nameBtn.gameObject, Loc.T("Открыть окно планеты"));
            r.World = LGIcons.Create(nameBtn.transform, LGIcon.Planet, 36, p.PlanetColor);
            r.World.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(36, 36));
            r.Name = LGBuild.Label(nameBtn.transform, "", 14, Color.white, TextAnchor.LowerLeft, bold: true);
            r.Name.rectTransform.Stretch(46, RowH * 0.5f - 2, 0, 0);
            r.Sub = LGBuild.Label(nameBtn.transform, "", 10, Muted, TextAnchor.UpperLeft);
            r.Sub.rectTransform.Stretch(46, 0, 0, RowH * 0.5f);

            r.Pop = Cell(row, 1, 15, Color.white);
            r.Pop.rectTransform.offsetMin += new Vector2(0, RowH * 0.32f);
            r.Growth = Cell(row, 1, 10, Muted, false);
            r.Growth.rectTransform.offsetMax -= new Vector2(0, RowH * 0.42f);
            r.Pop.raycastTarget = true;
            TooltipHelper.Attach(r.Pop.gameObject, "");
            r.PopTip = r.Pop.GetComponent<TooltipTrigger>();

            r.Stab = Cell(row, 2, 13, Color.white);
            r.Stab.rectTransform.offsetMin += new Vector2(0, RowH * 0.30f);
            r.Stab.raycastTarget = true;
            TooltipHelper.Attach(r.Stab.gameObject, "");
            r.StabTip = r.Stab.GetComponent<TooltipTrigger>();
            var sbHost = LGBuild.Rect(row, "StabBar");
            sbHost.anchorMin = new Vector2(0, 0.5f); sbHost.anchorMax = new Vector2(0, 0.5f); sbHost.pivot = new Vector2(0, 0.5f);
            sbHost.anchoredPosition = new Vector2(ColX[2], -12); sbHost.sizeDelta = new Vector2(ColW[2] - 14, 6);
            r.StabBar = LGBuild.Bar(sbHost, Cyan, 0.5f, 5f);

            r.Min = Cell(row, 3, 13, BuildMenuModal.CMinerals);
            r.En = Cell(row, 4, 13, BuildMenuModal.CEnergy);
            r.Al = Cell(row, 5, 13, BuildMenuModal.CAlloys);
            r.Sci = Cell(row, 6, 13, new Color(0.5f, 0.92f, 0.62f));

            // Приоритет: кнопка перебирает варианты
            var focus = LGBuild.Button(row, "Focus", UIManager.DS.BtnNeutral, new Color(Cyan.r, Cyan.g, Cyan.b, 0.4f), () => CycleFocus(p), null, null, 10, 10f);
            var frt = (RectTransform)focus.transform;
            frt.anchorMin = new Vector2(0, 0.5f); frt.anchorMax = new Vector2(0, 0.5f); frt.pivot = new Vector2(0, 0.5f);
            frt.anchoredPosition = new Vector2(ColX[7], 0); frt.sizeDelta = new Vector2(ColW[7] - 12, 32);
            r.Focus = LGBuild.Label(focus.transform, "", 10, Color.white, TextAnchor.MiddleCenter, bold: true);
            TooltipHelper.Attach(focus.gameObject, Loc.T("<b>Приоритет колонии</b>\nКуда жители идут работать в первую очередь и что строит автостройка. Нажмите, чтобы сменить."));

            // Стройка: значок, название, дни и полоса
            var jobHost = LGBuild.Panel(row, "Job", new Color(1f, 1f, 1f, 0f), raycast: true);
            var jrt = jobHost.rectTransform;
            jrt.anchorMin = new Vector2(0, 0); jrt.anchorMax = new Vector2(1, 1);
            jrt.offsetMin = new Vector2(ColX[8], 6); jrt.offsetMax = new Vector2(-252, -6);
            TooltipHelper.Attach(jobHost.gameObject, "");
            r.JobTip = jobHost.GetComponent<TooltipTrigger>();
            r.JobIcon = LGIcons.Create(jrt, LGIcon.Construction, 18, Gold);
            r.JobIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 4), new Vector2(18, 18));
            r.Job = LGBuild.Label(jrt, "", 12, Color.white, TextAnchor.MiddleLeft, bold: true);
            r.Job.rectTransform.Stretch(26, 14, 60, 0);
            r.JobDays = LGBuild.Label(jrt, "", 11, Gold, TextAnchor.MiddleRight, bold: true);
            r.JobDays.rectTransform.Stretch(0, 14, 0, 0);
            var jb = LGBuild.Rect(jrt, "Bar");
            jb.anchorMin = new Vector2(0, 0); jb.anchorMax = new Vector2(1, 0);
            jb.offsetMin = new Vector2(26, 6); jb.offsetMax = new Vector2(0, 10);
            r.JobBar = LGBuild.Bar(jb, Gold, 0f, 3f);

            // Справа: купить, строить…, автостройка
            r.Buy = LGBuild.Button(row, "Buy", new Color(0.36f, 0.28f, 0.06f), Gold, () => BuyFirst(p), null, null, 10, 10f);
            ((RectTransform)r.Buy.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-150, 0), new Vector2(94, 34));
            r.BuyBg = r.Buy.GetComponent<Image>();
            var bi = LGIcons.Create(r.Buy.transform, LGIcon.Energy, 12, BuildMenuModal.CEnergy);
            bi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(9, 0), new Vector2(12, 12));
            r.BuyText = LGBuild.Label(r.Buy.transform, "", 11, Color.white, TextAnchor.MiddleLeft, bold: true);
            r.BuyText.rectTransform.Stretch(25, 0, 4, 0);
            TooltipHelper.Attach(r.Buy.gameObject, "");
            r.BuyTip = r.Buy.GetComponent<TooltipTrigger>();

            var add = LGBuild.Button(row, "Add", UIManager.DS.BtnPrimary, Cyan, () => BuildMenuModal.Open(p), LGIcon.Construction, null, 12, 10f);
            ((RectTransform)add.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-96, 0), new Vector2(46, 34));
            TooltipHelper.Attach(add.gameObject, Loc.T("<b>Строить…</b>\nМеню строительства этой планеты"));

            r.AutoBtn = LGBuild.Button(row, "Auto", UIManager.DS.BtnNeutral, new Color(Cyan.r, Cyan.g, Cyan.b, 0.4f), () => { p.AutoBuild = !p.AutoBuild; Refresh(); }, null, null, 9, 10f);
            ((RectTransform)r.AutoBtn.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(78, 34));
            r.AutoBg = r.AutoBtn.GetComponent<Image>();
            r.Auto = LGBuild.Label(r.AutoBtn.transform, "", 10, Color.white, TextAnchor.MiddleCenter, bold: true);
            TooltipHelper.Attach(r.AutoBtn.gameObject, Loc.T("<b>Автостройка</b>\nКогда очередь пуста, колония сама заказывает нужный район в начале месяца."));
            return r;
        }

        private void FillRow(Row r)
        {
            var p = r.Planet;
            if (p == null) return;
            var cm = ConstructionManager.Instance;
            var queue = cm != null ? cm.QueueFor(p) : new List<ConstructionJob>();
            var gov = LeaderManager.Instance?.GovernorOf(p);

            r.Name.text = PlanetNames.Title(p.Name);
            r.Sub.text = p.ClassDisplayName + (p.ParentSystem != null && p.ParentSystem.Id == EconomyManager.PlayerCapitalId ? Loc.T(" · столица") : "")
                       + (gov != null ? Loc.F(" · губ. {0}", gov.Name) : "");
            r.World.color = p.PlanetColor;

            r.Pop.text = $"{p.Population}<size=11><color=#8AA2A8> / {p.HousingCapacity}</color></size>";
            int months = p.MonthsToNextPop;
            r.Growth.text = p.Population >= p.HousingCapacity ? Loc.T("<color=#F2C46B>нет жилья</color>")
                          : months < 0 ? Loc.T("рост остановлен")
                          : Loc.F("+1 через {0} мес.", months);
            var dom = SpeciesRegistry.Get(p.DominantOrganic);
            r.PopTip.SetText(Loc.F("<b>Жители: {0} из {1}</b>\n", p.Population, p.HousingCapacity) +
                             (dom != null ? Loc.F("Основной вид: {0}\n", dom.DisplayName) : "") +
                             Loc.F("Рабочих мест {0} · безработных {1}", p.WorkerSlots, p.IdlePops));

            var tier = p.Tier;
            Color tc = PlanetStability.TierColor(tier);
            r.Stab.text = $"<color=#{PlanetStability.Hex(tc)}>{p.Stability:0}</color> <size=10><color=#8AA2A8>{PlanetStability.TierName(tier)}</color></size>";
            LGBuild.SetBar(r.StabBar, p.Stability / 100f, tc);
            r.StabTip.SetText(PlanetStability.Tooltip(p));

            r.Min.text = Num(Output(p, "minerals"));
            r.En.text = Num(Output(p, "energy"));
            r.Al.text = Num(Output(p, "alloys"));
            r.Sci.text = Num(Science(p));
            r.Focus.text = ColonyFocusInfo.Name(p.Focus).ToUpper();

            // Текущая стройка
            var job = queue.Count > 0 ? queue[0] : null;
            if (job != null)
            {
                r.JobIcon.sprite = LGIcons.Get(BuildMenuModal.JobIcon(job));
                r.JobIcon.color = Gold;
                r.Job.text = BuildMenuModal.JobTitle(job) + (queue.Count > 1 ? Loc.F("  <color=#8AA2A8>+{0}</color>", queue.Count - 1) : "");
                r.JobDays.text = Loc.F("{0} дн.", Mathf.CeilToInt(job.DaysLeft));
                LGBuild.SetBar(r.JobBar, job.Progress, Gold);
                var sb = new System.Text.StringBuilder(Loc.T("<b>Очередь планеты</b>"));
                foreach (var j in queue) sb.Append($"\n• {BuildMenuModal.JobTitle(j)} — {Mathf.CeilToInt(cm.DaysUntilDone(j))} " + Loc.T("дн."));
                r.JobTip.SetText(sb.ToString());
            }
            else
            {
                r.JobIcon.sprite = LGIcons.Get(p.AutoBuild ? LGIcon.Gear : LGIcon.Warning);
                r.JobIcon.color = p.AutoBuild ? Muted : Gold;
                r.Job.text = p.AutoBuild ? Loc.T("<color=#8AA2A8>Автостройка выберет сама</color>") : Loc.T("<color=#F2C46B>Ничего не строится</color>");
                r.JobDays.text = "";
                LGBuild.SetBar(r.JobBar, 0f, Gold);
                r.JobTip.SetText(Loc.T("Очередь пуста. Нажмите «Строить…», чтобы выбрать постройку."));
            }
            r.Idle.enabled = job == null && !p.AutoBuild;

            bool canBuy = job != null && cm.BuyBlocker(job) == null;
            r.Buy.gameObject.SetActive(job != null);
            if (job != null)
            {
                r.Buy.interactable = canBuy;
                r.BuyBg.color = canBuy ? new Color(0.36f, 0.28f, 0.06f) : UIManager.DS.BtnDisabled;
                r.BuyText.text = Loc.F("КУПИТЬ {0}", ConstructionManager.BuyCost(job));
                r.BuyText.color = canBuy ? Color.white : Muted;
                r.BuyTip.SetText(BuildMenuModal.BuyTip(job, cm.BuyBlocker(job)));
            }
            r.AutoBg.color = p.AutoBuild ? UIManager.DS.BtnPrimary : UIManager.DS.BtnNeutral;
            r.Auto.text = p.AutoBuild ? Loc.T("АВТО ✓") : Loc.T("АВТО");
            r.Auto.color = p.AutoBuild ? Color.white : Muted;
        }

        private static string Num(float v) => v <= 0.05f ? "<color=#5C7480>—</color>" : $"+{v:0.#}";

        private void CycleFocus(PlanetData p)
        {
            int i = System.Array.IndexOf(ColonyFocusInfo.All, p.Focus);
            p.Focus = ColonyFocusInfo.All[(i + 1) % ColonyFocusInfo.All.Length];
            p.ClearJobPlan();   // новый приоритет распределяет жителей заново
            SFXManager.Play(Sfx.UiClick, 0.6f);
            EconomyManager.Instance?.RecalculateAll();
            Refresh();
        }

        private void BuyFirst(PlanetData p)
        {
            var cm = ConstructionManager.Instance;
            if (cm == null) return;
            var q = cm.QueueFor(p);
            if (q.Count == 0) return;
            if (cm.TryBuy(q[0]))
            {
                SFXManager.Play(Sfx.UiConfirm);
                EconomyManager.Instance?.RecalculateAll();
            }
            else SFXManager.Play(Sfx.UiDenied);
            Refresh();
        }

        private void OpenPlanet(PlanetData p)
        {
            Close();
            PlanetFocusOverlay.Instance?.Open(p, p.ParentSystem);
        }
    }
}
