using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// «Управление колониями» (клавиша C, пункт левого меню) — колонии империи карточками.
    /// Карточка: шар мира, название и класс, приоритет и автостройка, жители и стабильность полосами, доход,
    /// текущая стройка с прогрессом и кнопками «Строить…» / «Приоритет» / «Авто». Клик по шапке — окно планеты.
    /// Слева — сводка империи и «Требует внимания» (простой, нет жилья, брожение); сверху — фильтры и сортировка.
    /// </summary>
    public class ColonyManagerWindow : MonoBehaviour
    {
        public static ColonyManagerWindow Instance { get; private set; }
        public static bool IsOpenNow => Instance != null && Instance.IsOpen;
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private const float SideW = 330f, CardH = 366f, HeadArt = 112f, Gap = 16f, Pad = 34f, Top = 124f;
        private const int Cols = 3;

        private GameObject _root;
        private GameObject _focusPopup;
        private Text _cap, _subtitle;
        private RectTransform _list, _attention;
        private readonly List<Card> _cards = new List<Card>();
        private string _cardsKey = "", _attKey = "";
        private float _refresh;
        private Sort _sort = Sort.Population;
        private int _filter;
        private MenuSegment _filterSeg, _sortSeg;
        private readonly List<Text> _sideValues = new List<Text>();
        private Text _sumPop, _sumCol, _sumStab;
        private Text _autoAllText;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static readonly Color CCard = new Color(0.030f, 0.060f, 0.100f, 0.95f);
        private static readonly Color CHead = new Color(0.016f, 0.036f, 0.066f, 1f);
        private static readonly Color CSci = new Color(0.5f, 0.92f, 0.62f);
        private static readonly Color CRed = new Color(1f, 0.54f, 0.45f);

        private enum Sort { Population, Stability, Income }

        private class Card
        {
            public PlanetData Planet;
            public LiquidGlassEffect Fx;
            public Image World;
            public Text Name, Sub, FocusTag, AutoTag, Pop, Growth, Stab, Min, En, Al, Sci, Job, BuyText, Auto;
            public RectTransform FocusChip, AutoChip, PopBar, StabBar, JobBar;
            public Image JobIcon;
            public Button Buy, AutoBtn, FocusBtn;
            public Image AutoBg;
            public TooltipTrigger StabTip, PopTip, JobTip, BuyTip;
            public bool AutoShown;
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
            if (Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); if (_focusPopup != null) CloseFocusMenu(); else Close(); return; }
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
            _cardsKey = _attKey = "";
            Refresh();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            LG.Skin(_root.transform);
        }

        public void Close()
        {
            if (_root == null) return;
            CloseFocusMenu();
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

        private static float Income(PlanetData p) => Output(p, "minerals") + Output(p, "energy") + Output(p, "alloys") + Science(p);

        private static bool IsIdle(PlanetData p)
        {
            var cm = ConstructionManager.Instance;
            return cm != null && cm.QueueFor(p).Count == 0 && !p.AutoBuild;
        }
        private static bool NoHousing(PlanetData p) => p.Population >= p.HousingCapacity;
        private static bool Unstable(PlanetData p) => p.Tier <= StabilityTier.Discontent;

        private bool Pass(PlanetData p) => _filter switch
        {
            1 => IsIdle(p),
            2 => NoHousing(p),
            3 => Unstable(p),
            _ => true
        };

        private float SortValue(PlanetData p) => _sort switch
        {
            Sort.Stability => p.Stability,
            Sort.Income => Income(p),
            _ => p.Population + Income(p) * 0.001f
        };

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
            bg.color = new Color(0.024f, 0.047f, 0.086f, 0.97f);
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.32f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            _cap = LGBuild.Label(rt, "", 12, Cyan, TextAnchor.UpperLeft, bold: true);
            _cap.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -26), new Vector2(900, 18));
            _cap.Track(3f);
            var title = LGBuild.Label(rt, Loc.T("Управление колониями"), 34, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -44), new Vector2(900, 44));
            _subtitle = LGBuild.Label(rt, "", 14, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -88), new Vector2(1100, 20));
            var close = LGControls.IconButton(rt, LGIcon.Close, 40, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.3f), Close);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -20), new Vector2(40, 40));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc / C)</color>"));

            BuildSide(rt);

            // Фильтры и сортировка
            float left = Pad + SideW + 26f;
            var fHost = LGBuild.Rect(rt, "Filter");
            fHost.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(left, -Top), new Vector2(660, 46));
            _filterSeg = MenuSegment.Create(fHost, new[] { Loc.T("Все"), Loc.T("Простаивают"), Loc.T("Нет жилья"), Loc.T("Неспокойные") }, null, _filter,
                                            i => { _filter = i; _cardsKey = ""; Refresh(); });
            _filterSeg.GetComponent<RectTransform>().Stretch();
            var sHost = LGBuild.Rect(rt, "Sort");
            sHost.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Pad, -Top), new Vector2(380, 46));
            _sortSeg = MenuSegment.Create(sHost, new[] { Loc.T("Жители"), Loc.T("Стабильность"), Loc.T("Доход") }, null, (int)_sort,
                                          i => { _sort = (Sort)i; _cardsKey = ""; Refresh(); });
            _sortSeg.GetComponent<RectTransform>().Stretch();
            var sl = LGBuild.Label(rt, Loc.T("Сортировка"), 12, Muted, TextAnchor.MiddleRight);
            sl.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Pad - 392, -Top), new Vector2(160, 46));

            // Сетка карточек
            var listHost = LGBuild.Rect(rt, "Cards");
            listHost.Stretch(left - 4, 20, Pad - 4, Top + 46 + 12);
            _list = LGBuild.ScrollList(listHost, Gap, 4);
            _root.SetActive(false);
        }

        private RectTransform SideCard(RectTransform parent, string name, string cap, Color capCol)
        {
            var c = LGBuild.Panel(parent, name, new Color(1f, 1f, 1f, 0.03f));
            LG.Platter(c.gameObject, 14f).SetRim(new Color(1f, 1f, 1f, 0.07f));
            var h = LGBuild.Label(c.transform, cap, 12, capCol, TextAnchor.UpperLeft, bold: true);
            h.rectTransform.TopBand(20, 18, 20, 20);
            h.Track(3f);
            return c.rectTransform;
        }

        private void BuildSide(RectTransform rt)
        {
            var side = LGBuild.Rect(rt, "Side");
            side.anchorMin = new Vector2(0, 0); side.anchorMax = new Vector2(0, 1); side.pivot = new Vector2(0, 0.5f);
            side.offsetMin = new Vector2(Pad, 24); side.offsetMax = new Vector2(Pad + SideW, -Top);

            // Сводка: три крупных числа и ресурсы
            var sum = SideCard(side, "Summary", Loc.T("СВОДКА"), Cyan);
            sum.TopBand(0, 290);
            Text Big(int i, string cap, Color c)
            {
                var v = LGBuild.Label(sum, "", 30, c, TextAnchor.UpperLeft, bold: true);
                v.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20 + i * 100, -46), new Vector2(100, 36));
                var l = LGBuild.Label(sum, cap, 11, Muted, TextAnchor.UpperLeft, bold: true);
                l.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20 + i * 100, -84), new Vector2(100, 16));
                l.Track(1.2f);
                return v;
            }
            _sumPop = Big(0, Loc.T("ЖИТЕЛЕЙ"), Color.white);
            _sumCol = Big(1, Loc.T("КОЛОНИЙ"), Color.white);
            _sumStab = Big(2, Loc.T("СТАБИЛ."), Cyan);
            (Color, string)[] rows =
            {
                (BuildMenuModal.CEnergy, Loc.T("Гелий-3")),
                (BuildMenuModal.CMinerals, Loc.T("Титан")),
                (BuildMenuModal.CAlloys, Loc.T("Сплавы")),
                (BuildMenuModal.CInfluence, Loc.T("Влияние")),
                (CSci, Loc.T("Наука")),
            };
            float y = 112f;
            foreach (var (col, label) in rows)
            {
                var line = LGBuild.Panel(sum, "Sep", new Color(1f, 1f, 1f, 0.05f));
                line.rectTransform.TopBand(y, 1, 20, 20);
                LG.Ignore(line.gameObject);
                var r = LGBuild.Rect(sum, "Res");
                r.TopBand(y + 1, 33, 20, 20);
                var l = LGBuild.Label(r, label, 14, col, TextAnchor.MiddleLeft, bold: true);
                l.rectTransform.Stretch();
                var v = LGBuild.Label(r, "", 14, Color.white, TextAnchor.MiddleRight, bold: true);
                v.rectTransform.Stretch();
                _sideValues.Add(v);
                y += 34f;
            }

            // Требует внимания
            var att = SideCard(side, "Attention", Loc.T("ТРЕБУЕТ ВНИМАНИЯ"), Gold);
            att.anchorMin = new Vector2(0, 0); att.anchorMax = new Vector2(1, 1);
            att.offsetMin = new Vector2(0, 66); att.offsetMax = new Vector2(0, -306);
            _attention = LGBuild.Rect(att, "Items");
            _attention.Stretch(14, 14, 14, 50);

            var autoAll = LGBuild.Button(side, "AutoAll", UIManager.DS.BtnSuccess, UIManager.DS.Green, ToggleAutoAll, null, " ", 12);
            var aRt = (RectTransform)autoAll.transform;
            aRt.anchorMin = new Vector2(0, 0); aRt.anchorMax = new Vector2(1, 0); aRt.pivot = new Vector2(0.5f, 0);
            aRt.offsetMin = new Vector2(0, 0); aRt.offsetMax = new Vector2(0, 46);
            _autoAllText = autoAll.GetComponentInChildren<Text>();
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
            var all = Colonies();
            int idle = 0, house = 0, unst = 0;
            foreach (var p in all) { if (IsIdle(p)) idle++; if (NoHousing(p)) house++; if (Unstable(p)) unst++; }
            _filterSeg.SetLabel(0, Loc.F("Все · {0}", all.Count));
            _filterSeg.SetLabel(1, Loc.F("Простаивают · {0}", idle));
            _filterSeg.SetLabel(2, Loc.F("Нет жилья · {0}", house));
            _filterSeg.SetLabel(3, Loc.F("Неспокойные · {0}", unst));

            var list = all.FindAll(Pass);
            list.Sort((a, b) => _sort == Sort.Population && a.Population == b.Population
                ? string.Compare(a.Name, b.Name, System.StringComparison.CurrentCulture)
                : SortValue(b).CompareTo(SortValue(a)));

            string key = _filter + "|";
            foreach (var p in list) key += p.GetHashCode() + ",";
            if (key != _cardsKey)
            {
                _cardsKey = key;
                LGBuild.Clear(_list);
                _cards.Clear();
                int n = list.Count + (_filter == 0 ? 1 : 0);   // в «Все» — ещё карточка «Новая колония»
                for (int row = 0; row * Cols < n; row++)
                {
                    var line = LGBuild.Rect(_list, "Row");
                    LGBuild.Height(line.gameObject, CardH);
                    for (int c = 0; c < Cols; c++)
                    {
                        int i = row * Cols + c;
                        if (i >= n) break;
                        var slot = LGBuild.Rect(line, "Slot");
                        slot.anchorMin = new Vector2(c / (float)Cols, 0);
                        slot.anchorMax = new Vector2((c + 1) / (float)Cols, 1);
                        slot.offsetMin = new Vector2(Gap * 2f * c / Cols, 0);
                        slot.offsetMax = new Vector2(-Gap * 2f * (Cols - 1 - c) / Cols, 0);
                        if (i < list.Count) _cards.Add(MakeCard(slot, list[i]));
                        else MakeNewColonyCard(slot);
                    }
                }
                if (list.Count == 0 && _filter != 0)
                {
                    var e = LGBuild.Label(_list, Loc.T("Здесь пусто — все колонии в порядке."), 15, Muted, TextAnchor.MiddleCenter);
                    LGBuild.Height(e.gameObject, 120f);
                }
                LG.Skin(_list);
            }
            foreach (var c in _cards) FillCard(c);
            RefreshSide(all);
        }

        private void RefreshSide(List<PlanetData> list)
        {
            int pop = 0, robots = 0;
            float stab = 0f;
            foreach (var p in list) { pop += p.Population; robots += p.RobotPops; stab += p.Stability * p.Population; }
            float avg = pop > 0 ? stab / pop : 0f;
            var eco = EconomyManager.Instance;
            var tm = TechnologyManager.Instance;
            string empire = FactionTraits.FactionOf(0)?.Name ?? Loc.T("Ваша империя");
            _cap.text = Loc.F("{0} · КОЛОНИИ", empire.ToUpper());
            _subtitle.text = Loc.F("Колоний {0} · жителей {1}{2} · клик по карточке — окно планеты", list.Count, pop,
                                   robots > 0 ? Loc.F(" (роботов {0})", robots) : "");
            _sumPop.text = pop.ToString();
            _sumCol.text = list.Count.ToString();
            _sumStab.text = avg.ToString("0");
            _sumStab.color = PlanetStability.TierColor(PlanetStability.TierOf(avg));

            string Inc(float stock, float inc) => $"{stock:0} <color={(inc >= 0f ? "#5CF599" : "#FF8A8A")}>{(inc >= 0f ? "+" : "")}{inc:0}</color>";
            int i = 0;
            _sideValues[i++].text = eco != null ? Inc(eco.EnergyCredits, eco.MonthlyEnergyIncome) : "—";
            _sideValues[i++].text = eco != null ? Inc(eco.Minerals, eco.MonthlyMineralsIncome) : "—";
            _sideValues[i++].text = eco != null ? Inc(eco.Alloys, eco.MonthlyAlloysIncome) : "—";
            _sideValues[i++].text = eco != null ? Inc(eco.Influence, eco.MonthlyInfluenceIncome) : "—";
            _sideValues[i++].text = tm != null ? Loc.F("<color=#5CF599>+{0:0}</color> / мес", tm.MonthlyResearchIncome) : "—";

            bool anyOff = list.Exists(p => !p.AutoBuild);
            _autoAllText.text = anyOff ? Loc.T("ВКЛЮЧИТЬ АВТОСТРОЙКУ ВЕЗДЕ") : Loc.T("ВЫКЛЮЧИТЬ АВТОСТРОЙКУ ВЕЗДЕ");
            RefreshAttention(list);
        }

        /// <summary>«Требует внимания»: простой, нет жилья, безработные, брожение. Клик — окно планеты.</summary>
        private void RefreshAttention(List<PlanetData> list)
        {
            var items = new List<(string text, Color col, LGIcon icon, PlanetData p)>();
            foreach (var p in list)
            {
                string n = PlanetNames.Title(p.Name);
                if (Unstable(p)) items.Add((Loc.F("{0} — {1} ({2:0})", n, PlanetStability.TierName(p.Tier).ToLower(), p.Stability), CRed, LGIcon.Warning, p));
                if (IsIdle(p)) items.Add((Loc.F("{0} — ничего не строится", n), Gold, LGIcon.Construction, p));
                if (NoHousing(p)) items.Add((Loc.F("{0} — нет жилья", n), Gold, LGIcon.Population, p));
                else if (p.IdlePops > 0) items.Add((Loc.F("{0} — безработных {1}", n, p.IdlePops), Gold, LGIcon.Population, p));
            }
            string key = "";
            foreach (var it in items) key += it.text + "|";
            if (key == _attKey) return;
            _attKey = key;
            LGBuild.Clear(_attention);

            if (items.Count == 0)
            {
                var ok = LGBuild.Label(_attention, Loc.T("Все колонии заняты делом."), 13, new Color(0.62f, 0.94f, 0.78f), TextAnchor.UpperLeft, bold: true);
                ok.rectTransform.TopBand(4, 20);
                return;
            }
            const float H = 38f;
            int max = Mathf.Max(1, Mathf.FloorToInt((_attention.rect.height > 1f ? _attention.rect.height : 400f) / (H + 6f)));
            int shown = items.Count > max ? max - 1 : items.Count;
            for (int i = 0; i < shown; i++)
            {
                var it = items[i];
                var b = LGBuild.Panel(_attention, "Item", new Color(it.col.r, it.col.g, it.col.b, 0.07f), raycast: true);
                b.rectTransform.TopBand(i * (H + 6f), H);
                LG.Ignore(b.gameObject);
                var bar = LGBuild.Panel(b.transform, "Bar", it.col);
                bar.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(3, H));
                var ic = LGIcons.Create(b.transform, it.icon, 14, it.col);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(14, 14));
                var t = LGBuild.Label(b.transform, it.text, 13, Color.white, TextAnchor.MiddleLeft, bold: true);
                t.rectTransform.Stretch(36, 0, 8, 0);
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 10; t.resizeTextMaxSize = 13;
                var btn = b.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                var p = it.p;
                btn.onClick.AddListener(() => OpenPlanet(p));
                TooltipHelper.Attach(b.gameObject, Loc.T("Открыть окно планеты"));
            }
            if (shown < items.Count)
            {
                var more = LGBuild.Label(_attention, Loc.F("…и ещё {0}", items.Count - shown), 12, Muted, TextAnchor.MiddleLeft);
                more.rectTransform.TopBand(shown * (H + 6f), H, 14, 0);
            }
        }

        // ================================================================ Карточка колонии

        private static Sprite s_sphere;

        /// <summary>Шар планеты: освещённая сфера с мягким краем (красится в цвет мира).</summary>
        private static Sprite Sphere()
        {
            if (s_sphere != null) return s_sphere;
            const int N = 192;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[N * N];
            var light = new Vector3(-0.55f, 0.55f, 0.63f).normalized;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r2 = u * u + v * v;
                    float a = Mathf.Clamp01((1f - Mathf.Sqrt(r2)) * N * 0.5f);
                    if (a <= 0f) { px[y * N + x] = new Color32(0, 0, 0, 0); continue; }
                    var n = new Vector3(u, v, Mathf.Sqrt(Mathf.Max(0f, 1f - r2)));
                    float diff = Mathf.Clamp01(Vector3.Dot(n, light));
                    float lit = 0.10f + 0.95f * Mathf.Pow(diff, 0.8f);
                    float spec = Mathf.Pow(Mathf.Clamp01(Vector3.Dot(Vector3.Reflect(-light, n), Vector3.forward)), 18f) * 0.35f;
                    float rim = Mathf.Pow(1f - n.z, 3f) * 0.25f * diff;
                    float c = Mathf.Clamp01(lit + rim);
                    float w = Mathf.Clamp01(spec);
                    px[y * N + x] = new Color(Mathf.Lerp(c, 1f, w), Mathf.Lerp(c, 1f, w), Mathf.Lerp(c, 1f, w), a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            s_sphere = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            s_sphere.hideFlags = HideFlags.DontSave;
            return s_sphere;
        }

        private static Text Caption(Transform parent, string text)
        {
            var t = LGBuild.Label(parent, text, 11, Muted, TextAnchor.UpperLeft, bold: true);
            t.Track(1.5f);
            return t;
        }

        private static RectTransform Chip(Transform parent, Color col, out Text label)
        {
            var c = LGBuild.Panel(parent, "Tag", new Color(col.r, col.g, col.b, 0.14f));
            LG.Chip(c.gameObject, new Color(col.r, col.g, col.b, 0.5f), 6f);
            label = LGBuild.Label(c.transform, "", 11, Color.Lerp(col, Color.white, 0.35f), TextAnchor.MiddleCenter, bold: true);
            label.rectTransform.Stretch();
            label.Track(1f);
            return c.rectTransform;
        }

        private Card MakeCard(RectTransform slot, PlanetData p)
        {
            var r = new Card { Planet = p };
            var card = LGBuild.Panel(slot, "Colony", CCard, raycast: true);
            card.rectTransform.Stretch();
            r.Fx = LG.Platter(card.gameObject, 14f);
            r.Fx.SetRim(new Color(1f, 1f, 1f, 0.08f));

            // Шапка: тёмная подложка, шар мира справа, название слева; клик — окно планеты
            var mask = LG.RoundedMask(card.transform, 13f, 1f);
            var head = LGBuild.Panel(mask, "Head", CHead, raycast: true);
            head.rectTransform.TopBand(0, HeadArt);
            var hb = head.gameObject.AddComponent<Button>();
            hb.transition = Selectable.Transition.None;
            hb.onClick.AddListener(() => OpenPlanet(p));
            TooltipHelper.Attach(head.gameObject, Loc.T("Открыть окно планеты"));
            var hmask = head.gameObject.AddComponent<RectMask2D>();
            hmask.softness = Vector2Int.zero;
            r.World = LGBuild.Panel(head.transform, "World", p.PlanetColor);
            r.World.sprite = Sphere();
            r.World.rectTransform.At(new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-60, -56), new Vector2(184, 184));
            LG.Ignore(r.World.gameObject);
            var glow = LGBuild.Panel(head.transform, "Glow", new Color(p.PlanetColor.r, p.PlanetColor.g, p.PlanetColor.b, 0.18f));
            glow.sprite = MenuArt.SoftDisc;
            glow.rectTransform.At(new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-60, -56), new Vector2(300, 300));
            glow.transform.SetSiblingIndex(0);
            LG.Ignore(glow.gameObject);
            var fade = LGBuild.Panel(head.transform, "Fade", CHead);
            fade.sprite = MenuArt.HorizontalFade;
            fade.rectTransform.anchorMin = Vector2.zero; fade.rectTransform.anchorMax = new Vector2(0.8f, 1);
            fade.rectTransform.offsetMin = fade.rectTransform.offsetMax = Vector2.zero;
            LG.Ignore(fade.gameObject);

            r.Name = LGBuild.Label(head.transform, "", 22, Color.white, TextAnchor.UpperLeft, bold: true);
            r.Name.rectTransform.TopBand(16, 28, 20, 150);
            r.Name.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.7f);
            r.Sub = LGBuild.Label(head.transform, "", 12, Muted, TextAnchor.UpperLeft);
            r.Sub.rectTransform.TopBand(46, 18, 20, 150);
            r.FocusChip = Chip(head.transform, Cyan, out r.FocusTag);
            r.FocusChip.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -72), new Vector2(110, 22));
            r.AutoChip = Chip(head.transform, UIManager.DS.Green, out r.AutoTag);
            r.AutoTag.text = Loc.T("АВТО ✓");
            r.AutoChip.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(140, -72), new Vector2(70, 22));

            // Жители и стабильность
            var body = LGBuild.Rect(card.transform, "Body");
            body.Stretch(20, 0, 20, HeadArt + 14);
            var pc = LGBuild.Rect(body, "Pop");
            pc.anchorMin = new Vector2(0, 1); pc.anchorMax = new Vector2(0.5f, 1); pc.pivot = new Vector2(0, 1);
            pc.offsetMin = new Vector2(0, -64); pc.offsetMax = new Vector2(-12, 0);
            Caption(pc, Loc.T("ЖИТЕЛИ")).rectTransform.TopBand(0, 16);
            r.Pop = LGBuild.Label(pc, "", 16, Color.white, TextAnchor.UpperLeft, bold: true);
            r.Pop.rectTransform.TopBand(18, 22);
            r.Pop.raycastTarget = true;
            TooltipHelper.Attach(r.Pop.gameObject, "");
            r.PopTip = r.Pop.GetComponent<TooltipTrigger>();
            var pb = LGBuild.Rect(pc, "Bar"); pb.TopBand(42, 6);
            r.PopBar = LGBuild.Bar(pb, UIManager.DS.Green, 0f, 6f);
            r.Growth = LGBuild.Label(pc, "", 11, Muted, TextAnchor.UpperLeft);
            r.Growth.rectTransform.TopBand(50, 16);

            var sc = LGBuild.Rect(body, "Stab");
            sc.anchorMin = new Vector2(0.5f, 1); sc.anchorMax = new Vector2(1, 1); sc.pivot = new Vector2(0, 1);
            sc.offsetMin = new Vector2(12, -64); sc.offsetMax = new Vector2(0, 0);
            Caption(sc, Loc.T("СТАБИЛЬНОСТЬ")).rectTransform.TopBand(0, 16);
            r.Stab = LGBuild.Label(sc, "", 16, Color.white, TextAnchor.UpperLeft, bold: true);
            r.Stab.rectTransform.TopBand(18, 22);
            r.Stab.raycastTarget = true;
            TooltipHelper.Attach(r.Stab.gameObject, "");
            r.StabTip = r.Stab.GetComponent<TooltipTrigger>();
            var sb = LGBuild.Rect(sc, "Bar"); sb.TopBand(42, 6);
            r.StabBar = LGBuild.Bar(sb, Cyan, 0f, 6f);

            // Доход в месяц
            var prod = LGBuild.Panel(body, "Income", new Color(1f, 1f, 1f, 0.035f));
            prod.rectTransform.TopBand(76, 46);
            LG.Platter(prod.gameObject, 10f).SetRim(new Color(1f, 1f, 1f, 0.05f));
            Text Prod(int i, string cap, Color col)
            {
                var cell = LGBuild.Rect(prod.transform, "Cell");
                cell.anchorMin = new Vector2(i / 4f, 0); cell.anchorMax = new Vector2((i + 1) / 4f, 1);
                cell.offsetMin = new Vector2(12, 0); cell.offsetMax = Vector2.zero;
                var v = LGBuild.Label(cell, "", 15, col, TextAnchor.UpperLeft, bold: true);
                v.rectTransform.Stretch(0, 20, 0, 6);
                var l = LGBuild.Label(cell, cap, 9, Muted, TextAnchor.LowerLeft, bold: true);
                l.rectTransform.Stretch(0, 6, 0, 0);
                l.Track(1.2f);
                return v;
            }
            r.En = Prod(0, Loc.T("ГЕЛИЙ"), BuildMenuModal.CEnergy);
            r.Min = Prod(1, Loc.T("ТИТАН"), BuildMenuModal.CMinerals);
            r.Al = Prod(2, Loc.T("СПЛАВЫ"), BuildMenuModal.CAlloys);
            r.Sci = Prod(3, Loc.T("НАУКА"), CSci);

            // Текущая стройка
            var job = LGBuild.Panel(body, "Job", new Color(1f, 1f, 1f, 0f), raycast: true);
            job.rectTransform.TopBand(134, 40);
            TooltipHelper.Attach(job.gameObject, "");
            r.JobTip = job.GetComponent<TooltipTrigger>();
            r.JobIcon = LGIcons.Create(job.transform, LGIcon.Construction, 15, Gold);
            r.JobIcon.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -2), new Vector2(15, 15));
            r.Job = LGBuild.Label(job.transform, "", 13, Color.white, TextAnchor.UpperLeft, bold: true);
            r.Job.rectTransform.TopBand(0, 20, 22, 110);
            r.Job.resizeTextForBestFit = true; r.Job.resizeTextMinSize = 10; r.Job.resizeTextMaxSize = 13;
            var jb = LGBuild.Rect(job.transform, "Bar"); jb.TopBand(28, 6);
            r.JobBar = LGBuild.Bar(jb, Cyan, 0f, 6f);
            LGFx.Shimmer(r.JobBar);
            r.Buy = LGBuild.Button(job.transform, "Buy", new Color(0.36f, 0.28f, 0.06f), Gold, () => BuyFirst(p), null, " ", 11);
            r.Buy.GetComponent<LGGameButton>().Compact = true;
            ((RectTransform)r.Buy.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, 4), new Vector2(104, 26));
            r.BuyText = r.Buy.GetComponentInChildren<Text>();
            TooltipHelper.Attach(r.Buy.gameObject, "");
            r.BuyTip = r.Buy.GetComponent<TooltipTrigger>();

            // Кнопки
            Button Btn(string name, string label, Color tint, Color rim, System.Action act, float a0, float a1)
            {
                var b = LGBuild.Button(card.transform, name, tint, rim, act, null, label, 12);
                b.GetComponent<LGGameButton>().Compact = true;
                var brt = (RectTransform)b.transform;
                brt.anchorMin = new Vector2(a0, 0); brt.anchorMax = new Vector2(a1, 0); brt.pivot = new Vector2(0.5f, 0);
                brt.offsetMin = new Vector2(a0 == 0f ? 20f : 5f, 18f); brt.offsetMax = new Vector2(a1 == 1f ? -20f : -5f, 18f + 38f);
                return b;
            }
            var add = Btn("Build", Loc.T("СТРОИТЬ…"), UIManager.DS.BtnPrimary, Cyan, () => BuildMenuModal.Open(p), 0f, 0.4f);
            TooltipHelper.Attach(add.gameObject, Loc.T("<b>Строить…</b>\nМеню строительства этой планеты"));
            RectTransform frt = null;
            r.FocusBtn = Btn("Focus", Loc.T("ПРИОРИТЕТ"), UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), () => OpenFocusMenu(p, frt), 0.4f, 0.76f);
            frt = (RectTransform)r.FocusBtn.transform;
            TooltipHelper.Attach(r.FocusBtn.gameObject, Loc.T("<b>Приоритет колонии</b>\nКуда жители идут работать в первую очередь и что строит автостройка. Нажмите, чтобы выбрать."));
            r.AutoBtn = Btn("Auto", Loc.T("АВТО"), UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), () => { p.AutoBuild = !p.AutoBuild; Refresh(); }, 0.76f, 1f);
            r.AutoBg = r.AutoBtn.GetComponent<Image>();
            r.Auto = r.AutoBtn.GetComponentInChildren<Text>();
            r.AutoShown = !p.AutoBuild;   // чтобы первый FillCard перекрасил кнопку
            TooltipHelper.Attach(r.AutoBtn.gameObject, Loc.T("<b>Автостройка</b>\nКогда очередь пуста, колония сама заказывает нужный район в начале месяца."));
            return r;
        }

        private void MakeNewColonyCard(RectTransform slot)
        {
            var c = LGBuild.Panel(slot, "NewColony", new Color(1f, 1f, 1f, 0.015f));
            c.rectTransform.Stretch();
            LG.Platter(c.gameObject, 14f).SetRim(new Color(1f, 1f, 1f, 0.06f));
            var plus = LGBuild.Label(c.transform, "+", 34, new Color(0.55f, 0.66f, 0.70f), TextAnchor.MiddleCenter, bold: true);
            plus.rectTransform.Stretch(0, 40, 0, 0);
            var t = LGBuild.Label(c.transform, Loc.T("Новая колония"), 16, new Color(0.62f, 0.72f, 0.76f), TextAnchor.MiddleCenter, bold: true);
            t.rectTransform.Stretch(0, 0, 0, 10);
            var d = LGBuild.Label(c.transform, Loc.T("Выберите пригодную планету в своей системе\nи проект «Колония»"), 12, Muted, TextAnchor.UpperCenter);
            d.rectTransform.Stretch(20, 0, 20, CardH * 0.5f + 18f);
            d.rectTransform.anchorMax = new Vector2(1, 0.5f);
            d.rectTransform.offsetMax = new Vector2(-20, -18);
        }

        private void FillCard(Card r)
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

            r.FocusTag.text = ColonyFocusInfo.Name(p.Focus).ToUpper();
            float fw = r.FocusTag.preferredWidth + 18f;
            r.FocusChip.sizeDelta = new Vector2(fw, 22);
            r.AutoChip.gameObject.SetActive(p.AutoBuild);
            r.AutoChip.anchoredPosition = new Vector2(20 + fw + 8, -72);

            bool full = NoHousing(p);
            r.Pop.text = $"{p.Population}<size=12><color=#8AA2A8> / {p.HousingCapacity}</color></size>";
            LGBuild.SetBar(r.PopBar, p.HousingCapacity > 0 ? p.Population / (float)p.HousingCapacity : 1f, full ? Gold : UIManager.DS.Green);
            int months = p.MonthsToNextPop;
            r.Growth.text = full ? Loc.T("<color=#F2C46B>нет жилья</color>")
                          : months < 0 ? Loc.T("рост остановлен")
                          : Loc.F("+1 через {0} мес.", months);
            var dom = SpeciesRegistry.Get(p.DominantOrganic);
            r.PopTip.SetText(Loc.F("<b>Жители: {0} из {1}</b>\n", p.Population, p.HousingCapacity) +
                             (dom != null ? Loc.F("Основной вид: {0}\n", dom.DisplayName) : "") +
                             Loc.F("Рабочих мест {0} · безработных {1}", p.WorkerSlots, p.IdlePops));

            var tier = p.Tier;
            Color tc = PlanetStability.TierColor(tier);
            r.Stab.text = $"<color=#{PlanetStability.Hex(tc)}>{p.Stability:0}</color> <size=12><color=#{PlanetStability.Hex(tc)}>{PlanetStability.TierName(tier)}</color></size>";
            LGBuild.SetBar(r.StabBar, p.Stability / 100f, tc);
            r.StabTip.SetText(PlanetStability.Tooltip(p));

            r.Min.text = Num(Output(p, "minerals"));
            r.En.text = Num(Output(p, "energy"));
            r.Al.text = Num(Output(p, "alloys"));
            r.Sci.text = Num(Science(p));

            // Текущая стройка
            var job = queue.Count > 0 ? queue[0] : null;
            if (job != null)
            {
                r.JobIcon.sprite = LGIcons.Get(BuildMenuModal.JobIcon(job));
                r.JobIcon.color = Cyan;
                r.Job.text = BuildMenuModal.JobTitle(job) + Loc.F("  <color=#8AA2A8>· {0} дн.</color>", Mathf.CeilToInt(job.DaysLeft))
                           + (queue.Count > 1 ? Loc.F("  <color=#8AA2A8>+{0} в очереди</color>", queue.Count - 1) : "");
                LGBuild.SetBar(r.JobBar, job.Progress, Cyan);
                var sbq = new System.Text.StringBuilder(Loc.T("<b>Очередь планеты</b>"));
                foreach (var j in queue) sbq.Append($"\n• {BuildMenuModal.JobTitle(j)} — {Mathf.CeilToInt(cm.DaysUntilDone(j))} " + Loc.T("дн."));
                r.JobTip.SetText(sbq.ToString());
            }
            else
            {
                r.JobIcon.sprite = LGIcons.Get(p.AutoBuild ? LGIcon.Gear : LGIcon.Warning);
                r.JobIcon.color = p.AutoBuild ? Muted : Gold;
                r.Job.text = p.AutoBuild ? Loc.T("<color=#8AA2A8>Автостройка выберет сама</color>") : Loc.T("<color=#F2C46B>Ничего не строится</color>");
                LGBuild.SetBar(r.JobBar, 0f, Cyan);
                r.JobTip.SetText(Loc.T("Очередь пуста. Нажмите «Строить…», чтобы выбрать постройку."));
            }
            bool idle = job == null && !p.AutoBuild;
            r.Fx.SetRim(idle ? new Color(Gold.r, Gold.g, Gold.b, 0.55f) : new Color(1f, 1f, 1f, 0.08f));

            r.Buy.gameObject.SetActive(job != null);
            if (job != null)
            {
                var blocker = cm.BuyBlocker(job);
                r.Buy.interactable = blocker == null;
                r.BuyText.text = Loc.F("КУПИТЬ {0}", ConstructionManager.BuyCost(job));
                r.BuyTip.SetText(BuildMenuModal.BuyTip(job, blocker));
            }

            if (r.AutoShown != p.AutoBuild)
            {
                r.AutoShown = p.AutoBuild;
                r.AutoBg.color = p.AutoBuild ? UIManager.DS.BtnSuccess : UIManager.DS.BtnNeutral;   // полоса: зелёная — включена
                r.Auto.text = p.AutoBuild ? Loc.T("АВТО ✓") : Loc.T("АВТО");
            }
        }

        private static string Num(float v) => v <= 0.05f ? "<color=#5C7480>—</color>" : $"+{v:0.#}";

        /// <summary>Выпадающий список приоритетов под кнопкой (или над ней, если внизу нет места). Клик мимо — закрыть.</summary>
        private void OpenFocusMenu(PlanetData p, RectTransform anchor)
        {
            CloseFocusMenu();
            if (p == null || anchor == null || _root == null) return;
            var root = (RectTransform)_root.transform;

            var blocker = LGBuild.Panel(root, "FocusBlocker", new Color(0f, 0f, 0f, 0.001f), raycast: true);
            blocker.rectTransform.Stretch();
            var bb = blocker.gameObject.AddComponent<Button>();
            bb.transition = Selectable.Transition.None;
            bb.onClick.AddListener(CloseFocusMenu);
            _focusPopup = blocker.gameObject;

            var opts = ColonyFocusInfo.All;
            const float OptH = 34f;
            float w = Mathf.Max(anchor.rect.width, 190f), h = opts.Length * OptH + 8f;
            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);   // 0 — низ-лево, 1 — верх-лево
            Vector3 bottom = root.InverseTransformPoint(corners[0]);
            Vector3 top = root.InverseTransformPoint(corners[1]);
            bool up = bottom.y - h < root.rect.yMin + 12f;

            var list = LGBuild.Panel(blocker.transform, "FocusMenu", new Color(0.02f, 0.05f, 0.07f, 0.98f), raycast: true);
            var lr = list.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f);   // координаты корня — от центра
            lr.pivot = up ? new Vector2(0, 0) : new Vector2(0, 1);
            lr.anchoredPosition = up ? new Vector2(top.x, top.y + 4f) : new Vector2(bottom.x, bottom.y - 4f);
            lr.sizeDelta = new Vector2(w, h);
            LG.Platter(list.gameObject, 10f).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f));

            for (int i = 0; i < opts.Length; i++)
            {
                var f = opts[i];
                bool cur = f == p.Focus;
                var b = LGBuild.Button(list.transform, "Opt", cur ? new Color(0.16f, 0.52f, 0.56f) : new Color(0.03f, 0.07f, 0.09f, 0.98f),
                    new Color(Cyan.r, Cyan.g, Cyan.b, 0.3f), () => PickFocus(p, f), null, null, 12, 6f);
                ((RectTransform)b.transform).TopBand(4 + i * OptH, OptH - 2, 4, 4);
                LGBuild.Label(b.transform, ColonyFocusInfo.Name(f), 12, Color.white, TextAnchor.MiddleCenter, bold: true);
                TooltipHelper.Attach(b.gameObject, ColonyFocusInfo.Desc(f));
            }
            LG.Skin(blocker.transform);
            SFXManager.Play(Sfx.UiClick, 0.5f);
        }

        private void CloseFocusMenu()
        {
            if (_focusPopup == null) return;
            Destroy(_focusPopup);
            _focusPopup = null;
            TooltipSystem.Hide();
        }

        private void PickFocus(PlanetData p, ColonyFocus f)
        {
            CloseFocusMenu();
            if (p == null) return;
            p.Focus = f;
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
