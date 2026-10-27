using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Окно «Арсенал» (левое меню, клавиша J) — наземные войска для штурма колоний. Оформлено в общем стиле окон игры
    /// (стекло, бирюзовая кромка, золотые подписи — как «Управление колониями»):
    ///   • слева — колония производства (переключатель), её районы, сводка армии и подсказка;
    ///   • вкладка «ПРОИЗВОДСТВО» — витрина карт сеткой с фильтрами по типу, доступности и сортировкой; под картой —
    ///     цена и кнопка «Заказать»; закрытые технологией — затемнены, с замком и названием технологии;
    ///   • вкладка «ВОЙСКА» — корабли с десантными местами (карты в выемках, автопогрузка, высадка), резерв и гарнизоны.
    /// Клик по карте резерва — погрузить на корабль у колонии, клик по карте на корабле — высадить.
    /// </summary>
    public sealed class ArsenalWindow : MonoBehaviour
    {
        public static ArsenalWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);
        public static bool IsOpenNow => Instance != null && Instance.IsOpen;

        private const float SideW = 300f;
        private static readonly Vector2 ShopCard = new Vector2(156, 218);
        private static readonly Vector2 MiniCard = new Vector2(84, 118);
        private static readonly Vector2 BigCard = new Vector2(280, 392);
        private const float TileW = 176f, TileH = 318f;

        private enum Tab { Production, Forces }
        private enum KindFilter { All, Infantry, Vehicles, Orders, Defense, Faction }
        private enum AvailFilter { All, Ready, Locked }
        private enum SortBy { Cost, Power, Armor, Name }

        private GameObject _root;
        private RectTransform _rt, _content, _list, _previewRoot, _filterBar, _tabBar;
        private Text _subtitle, _colonyName, _colonyInfo, _sideHint;
        private readonly List<Text> _sideValues = new List<Text>();
        private ScrollRect _scroll;
        private CardView _preview;
        private int _colony = -1;
        private string _sig;
        private float _refresh;
        private bool _dirty;
        private Tab _tab = Tab.Production;
        private KindFilter _kind = KindFilter.All;
        private AvailFilter _avail = AvailFilter.All;
        private SortBy _sort = SortBy.Cost;
        private readonly List<(Image bg, Text t, System.Func<bool> on)> _chips = new List<(Image, Text, System.Func<bool>)>();

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static readonly Color CPanel = new Color(0.020f, 0.045f, 0.065f, 0.90f);
        private static readonly Color CTile = new Color(0.030f, 0.065f, 0.090f, 0.92f);
        private static readonly Color Accent = new Color(0.95f, 0.45f, 0.35f);

        public static void Toggle()
        {
            if (Instance == null) new GameObject("[UI] ArsenalWindow").AddComponent<ArsenalWindow>();
            if (Instance.IsOpen) Instance.Close();
            else Instance.Open();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            ArmyManager.OnChanged += MarkDirty;
        }

        private void OnDestroy()
        {
            ArmyManager.OnChanged -= MarkDirty;
            if (Instance == this) Instance = null;
        }

        private void MarkDirty() => _dirty = true;

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (_preview != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, Input.mousePosition, null, out var lp);
                var half = _rt.rect.size * 0.5f;
                float x = lp.x + (lp.x > 0 ? -BigCard.x * 0.5f - 46f : BigCard.x * 0.5f + 46f);
                float y = Mathf.Clamp(lp.y, -half.y + BigCard.y * 0.5f + 10f, half.y - BigCard.y * 0.5f - 10f);
                _previewRoot.anchoredPosition = new Vector2(x, y);
            }
            if ((_refresh -= Time.unscaledDeltaTime) <= 0f || _dirty)
            {
                _refresh = 1f;
                string sig = Signature();
                if (_dirty || sig != _sig) { _dirty = false; _sig = sig; Rebuild(); }
            }
        }

        public void Open()
        {
            if (ArmyManager.Instance == null) return;
            if (_root == null) Build();
            if (_root == null) return;
            var cols = Colonies();
            if (_colony < 0 || cols.Find(s => s.Id == _colony) == null)
            {
                var sel = UIManager.Instance != null ? UIManager.Instance.ActiveSystemPublic : null;
                _colony = sel != null && sel.OwnerId == 0 && ArmyManager.SystemPopulation(sel) > 0 ? sel.Id
                        : cols.Count > 0 ? (cols.Find(s => s.Id == ArmyManager.CapitalOf(0)) ?? cols[0]).Id : -1;
            }
            _sig = null;
            _dirty = true;
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            SFXManager.Play(Sfx.WindowOpen);
        }

        public void Close()
        {
            if (_root == null) return;
            HidePreview();
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            TooltipSystem.Hide();
            SFXManager.Play(Sfx.WindowClose);
        }

        // ================================================================ Данные

        private static List<StarSystem> Colonies()
        {
            var l = new List<StarSystem>();
            foreach (var s in EmpireStats.Systems)
                if (s != null && s.OwnerId == 0 && s.HasStarbase && ArmyManager.SystemPopulation(s) > 0) l.Add(s);
            l.Sort((a, b) => ArmyManager.SystemPopulation(b).CompareTo(ArmyManager.SystemPopulation(a)));
            return l;
        }

        private static List<FleetData> TroopShips()
        {
            var l = new List<FleetData>();
            var fm = FleetManager.Instance;
            if (fm == null) return l;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d != null && !d.Destroyed && d.OwnerId == 0 && ArmyManager.Capacity(d) > 0) l.Add(d);
            }
            var am = ArmyManager.Instance;
            l.Sort((a, b) => (am != null ? am.Used(b.Id) : 0).CompareTo(am != null ? am.Used(a.Id) : 0));
            return l;
        }

        private string Signature()
        {
            var eco = EconomyManager.Instance;
            var sb = new StringBuilder();
            sb.Append(_colony).Append('|').Append((int)_tab).Append((int)_kind).Append((int)_avail).Append((int)_sort).Append('|');
            if (eco != null) sb.Append((int)eco.EnergyCredits / 5).Append(',').Append((int)eco.Minerals / 5).Append(',').Append((int)eco.Alloys / 5).Append(',').Append((int)eco.Influence / 5).Append('|');
            var am = ArmyManager.Instance;
            if (am != null) foreach (var c in am.Of(0)) sb.Append(c.Id).Append(c.Fleet).Append(c.System).Append(c.WoundDays > 0 ? 'w' : '-');
            sb.Append('|');
            var cm = ConstructionManager.Instance;
            if (cm != null) foreach (var j in cm.Jobs) if (j.Owner == 0 && j.Kind == JobKind.Troop) sb.Append(j.Id).Append(':').Append((int)(j.Progress * 20)).Append(',');
            if (_tab == Tab.Forces)
                foreach (var f in TroopShips()) sb.Append(f.Id).Append('@').Append(f.CurrentSystemId).Append(f.State == FleetState.InHyperlane ? 'h' : 'o').Append(am != null && am.AutoLoad(f.Id) ? 'a' : 'm');
            var tm = TechnologyManager.Instance;
            if (tm != null) { int n = 0; foreach (var t in tm.AllTechs) if (t.IsResearched) n++; sb.Append('t').Append(n); }
            return sb.ToString();
        }

        // ================================================================ Построение

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            _rt = LGBuild.Rect(host.transform, "ArsenalWindow");
            _rt.Stretch(28, 28, 28, 28);
            _root = _rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.010f, 0.025f, 0.038f, 0.97f);
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.42f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            var accent = LGBuild.Panel(_rt, "Accent", Accent);
            accent.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -24), new Vector2(5, 30));
            var title = LGBuild.Label(_rt, Loc.T("АРСЕНАЛ"), 26, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(42, -18), new Vector2(700, 36));
            title.Track(1.6f);
            _subtitle = LGBuild.Label(_rt, "", 12, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(43, -54), new Vector2(1100, 18));
            var close = LGBuild.Button(_rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc / J)</color>"));

            BuildSide();

            // Вкладки и фильтры
            _tabBar = LGBuild.Rect(_rt, "Tabs");
            _tabBar.anchorMin = new Vector2(0, 1); _tabBar.anchorMax = new Vector2(1, 1); _tabBar.pivot = new Vector2(0, 1);
            _tabBar.offsetMin = new Vector2(SideW + 40f, -126f); _tabBar.offsetMax = new Vector2(-20f, -88f);
            float tx = 0f;
            tx += Chip(_tabBar, Loc.T("ПРОИЗВОДСТВО"), LGIcon.Industry, tx, 190f, () => _tab == Tab.Production, () => SetTab(Tab.Production), 38f, 12);
            tx += Chip(_tabBar, Loc.T("ВОЙСКА"), LGIcon.Fleet, tx, 160f, () => _tab == Tab.Forces, () => SetTab(Tab.Forces), 38f, 12);

            _filterBar = LGBuild.Rect(_rt, "Filters");
            _filterBar.anchorMin = new Vector2(0, 1); _filterBar.anchorMax = new Vector2(1, 1); _filterBar.pivot = new Vector2(0, 1);
            _filterBar.offsetMin = new Vector2(SideW + 40f, -166f); _filterBar.offsetMax = new Vector2(-20f, -134f);
            float fx = 0f;
            fx += Chip(_filterBar, Loc.T("Все"), null, fx, 62f, () => _kind == KindFilter.All, () => SetKind(KindFilter.All));
            fx += Chip(_filterBar, Loc.T("Пехота"), LGIcon.Population, fx, 104f, () => _kind == KindFilter.Infantry, () => SetKind(KindFilter.Infantry));
            fx += Chip(_filterBar, Loc.T("Техника"), LGIcon.Industry, fx, 108f, () => _kind == KindFilter.Vehicles, () => SetKind(KindFilter.Vehicles));
            fx += Chip(_filterBar, Loc.T("Приказы"), LGIcon.Target, fx, 108f, () => _kind == KindFilter.Orders, () => SetKind(KindFilter.Orders));
            fx += Chip(_filterBar, Loc.T("Оборона"), LGIcon.Starbase, fx, 108f, () => _kind == KindFilter.Defense, () => SetKind(KindFilter.Defense));
            fx += Chip(_filterBar, Loc.T("Фракция"), LGIcon.Star, fx, 104f, () => _kind == KindFilter.Faction, () => SetKind(KindFilter.Faction));
            fx += 18f;
            fx += Chip(_filterBar, Loc.T("Можно заказать"), LGIcon.Check, fx, 160f, () => _avail == AvailFilter.Ready, () => SetAvail(_avail == AvailFilter.Ready ? AvailFilter.All : AvailFilter.Ready));
            fx += Chip(_filterBar, Loc.T("Закрыто технологией"), LGIcon.Lock, fx, 196f, () => _avail == AvailFilter.Locked, () => SetAvail(_avail == AvailFilter.Locked ? AvailFilter.All : AvailFilter.Locked));
            fx += 18f;
            var sl = LGBuild.Label(_filterBar, Loc.T("СОРТИРОВКА"), 10, Gold, TextAnchor.MiddleLeft, bold: true);
            sl.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(fx, 0), new Vector2(96, 30));
            sl.Track(1.2f);
            fx += 96f;
            fx += Chip(_filterBar, Loc.T("Цена"), null, fx, 70f, () => _sort == SortBy.Cost, () => SetSort(SortBy.Cost));
            fx += Chip(_filterBar, Loc.T("Сила"), null, fx, 70f, () => _sort == SortBy.Power, () => SetSort(SortBy.Power));
            fx += Chip(_filterBar, Loc.T("Броня"), null, fx, 74f, () => _sort == SortBy.Armor, () => SetSort(SortBy.Armor));
            fx += Chip(_filterBar, Loc.T("Имя"), null, fx, 64f, () => _sort == SortBy.Name, () => SetSort(SortBy.Name));

            // Содержимое
            var panel = LGBuild.Panel(_rt, "Content", CPanel);
            panel.rectTransform.anchorMin = Vector2.zero; panel.rectTransform.anchorMax = Vector2.one;
            panel.rectTransform.offsetMin = new Vector2(SideW + 40f, 20f); panel.rectTransform.offsetMax = new Vector2(-20f, -176f);
            LG.Platter(panel.gameObject, 16f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.18f));
            _content = panel.rectTransform;
            var listHost = LGBuild.Rect(_content, "ListHost");
            listHost.Stretch(8, 8, 8, 8);
            _list = LGBuild.ScrollList(listHost, 10f, 8);
            _scroll = listHost.GetComponentInChildren<ScrollRect>();

            _previewRoot = LGBuild.Rect(_rt, "Preview");
            _previewRoot.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, BigCard);
            _root.SetActive(false);
        }

        /// <summary>Чип-переключатель в стиле игры: стекло с кромкой, выбранный — подсвечен бирюзой.</summary>
        private float Chip(RectTransform parent, string text, LGIcon? icon, float x, float w, System.Func<bool> on, System.Action act, float h = 30f, int font = 11)
        {
            var bg = LGBuild.Panel(parent, "Chip_" + text, new Color(0.05f, 0.11f, 0.14f, 0.9f), raycast: true);
            bg.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(w, h));
            LG.Chip(bg.gameObject, new Color(1f, 1f, 1f, 0.14f), h * 0.5f);
            float ix = 0f;
            if (icon.HasValue)
            {
                var ic = LGIcons.Create(bg.transform, icon.Value, 13, Cyan);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(13, 13));
                ix = 18f;
            }
            var t = LGBuild.Label(bg.transform, text, font, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, bold: true);
            t.rectTransform.Stretch(8 + ix, 0, 8, 0);
            var b = bg.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { act(); SFXManager.Play(Sfx.UiTab, 0.6f); RefreshChips(); });
            _chips.Add((bg, t, on));
            return w + 8f;
        }

        private void RefreshChips()
        {
            foreach (var c in _chips)
            {
                bool on = c.on();
                c.bg.color = on ? new Color(0.10f, 0.38f, 0.42f, 0.95f) : new Color(0.05f, 0.11f, 0.14f, 0.9f);
                c.t.color = on ? Color.white : UIManager.DS.TextMuted;
            }
            if (_filterBar != null) _filterBar.gameObject.SetActive(_tab == Tab.Production);
        }

        private void SetTab(Tab t) { _tab = t; _dirty = true; if (_scroll != null) _scroll.verticalNormalizedPosition = 1f; }
        private void SetKind(KindFilter k) { _kind = k; _dirty = true; }
        private void SetAvail(AvailFilter a) { _avail = a; _dirty = true; }
        private void SetSort(SortBy s) { _sort = s; _dirty = true; }

        private void BuildSide()
        {
            var side = LGBuild.Panel(_rt, "Side", CPanel);
            var s = side.rectTransform;
            s.anchorMin = new Vector2(0, 0); s.anchorMax = new Vector2(0, 1); s.pivot = new Vector2(0, 0.5f);
            s.offsetMin = new Vector2(20, 20); s.offsetMax = new Vector2(20 + SideW, -88);
            LG.Platter(side.gameObject, 16f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.18f));

            var head = LGBuild.Label(s, Loc.T("КОЛОНИЯ ПРОИЗВОДСТВА"), 11, Gold, TextAnchor.UpperLeft, bold: true);
            head.rectTransform.TopBand(16, 18, 18, 18);
            head.Track(1.4f);
            var row = LGBuild.Rect(s, "Colony");
            row.TopBand(40, 40, 18, 18);
            var prev = LGBuild.Button(row, "Prev", UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.22f), () => Cycle(-1), LGIcon.Back, null, 11);
            ((RectTransform)prev.transform).At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(34, 34));
            TooltipHelper.Attach(prev.gameObject, Loc.T("Предыдущая колония"));
            var next = LGBuild.Button(row, "Next", UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.22f), () => Cycle(1), LGIcon.Back, null, 11);
            ((RectTransform)next.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(34, 34));
            next.transform.localScale = new Vector3(-1, 1, 1);
            TooltipHelper.Attach(next.gameObject, Loc.T("Следующая колония"));
            _colonyName = LGBuild.Label(row, "", 16, Color.white, TextAnchor.MiddleCenter, bold: true);
            _colonyName.rectTransform.Stretch(42, 0, 42, 0);
            _colonyInfo = LGBuild.Label(s, "", 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            _colonyInfo.rectTransform.TopBand(88, 76, 18, 18);
            _colonyInfo.lineSpacing = 1.2f;
            _colonyInfo.supportRichText = true;

            var sep = LGBuild.Panel(s, "Sep", new Color(Gold.r, Gold.g, Gold.b, 0.22f));
            sep.rectTransform.TopBand(172, 1, 18, 18);

            var h2 = LGBuild.Label(s, Loc.T("АРМИЯ"), 11, Gold, TextAnchor.UpperLeft, bold: true);
            h2.rectTransform.TopBand(186, 18, 18, 18);
            h2.Track(1.4f);
            (LGIcon, Color, string)[] rows =
            {
                (LGIcon.Swords, new Color(1f, 0.82f, 0.45f), Loc.T("Карт всего")),
                (LGIcon.Fleet, Cyan, Loc.T("На кораблях")),
                (LGIcon.Planet, UIManager.DS.TextPrimary, Loc.T("В резерве")),
                (LGIcon.Starbase, new Color(0.55f, 0.85f, 1f), Loc.T("Гарнизоны")),
                (LGIcon.Warning, new Color(1f, 0.5f, 0.45f), Loc.T("Ранены")),
                (LGIcon.Energy, BuildMenuModal.CEnergy, Loc.T("Содержание в месяц")),
            };
            float y = 212f;
            foreach (var (icon, col, label) in rows)
            {
                var r = LGBuild.Rect(s, "Stat");
                r.TopBand(y, 28, 18, 18);
                var ic = LGIcons.Create(r, icon, 15, col);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(15, 15));
                var l = LGBuild.Label(r, label, 12, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
                l.rectTransform.Stretch(24, 0, 0, 0);
                var v = LGBuild.Label(r, "", 13, Color.white, TextAnchor.MiddleRight, bold: true);
                _sideValues.Add(v);
                y += 30f;
            }
            _sideHint = LGBuild.Label(s, "", 12, Muted, TextAnchor.UpperLeft, wrap: true);
            _sideHint.rectTransform.TopBand(y + 14, 220, 18, 18);
            _sideHint.lineSpacing = 1.15f;
            _sideHint.supportRichText = true;
        }

        private void Cycle(int dir)
        {
            var cols = Colonies();
            if (cols.Count == 0) return;
            int i = cols.FindIndex(s => s.Id == _colony);
            i = ((i < 0 ? 0 : i + dir) % cols.Count + cols.Count) % cols.Count;
            _colony = cols[i].Id;
            SFXManager.Play(Sfx.UiTab);
            _dirty = true;
        }

        // ================================================================ Наполнение

        private void Rebuild()
        {
            var am = ArmyManager.Instance;
            if (am == null) return;
            HidePreview();
            RefreshChips();
            var mine = am.Of(0);
            int onShips = 0, reserve = 0, garrison = 0, wounded = 0;
            foreach (var c in mine) { if (c.Fleet >= 0) onShips++; else if (c.Garrison) garrison++; else reserve++; if (c.Wounded) wounded++; }
            _sideValues[0].text = mine.Count.ToString();
            _sideValues[1].text = onShips.ToString();
            _sideValues[2].text = reserve.ToString();
            _sideValues[3].text = garrison.ToString();
            _sideValues[4].text = wounded.ToString();
            _sideValues[5].text = am.Upkeep(0).ToString("0.#");
            _subtitle.text = Loc.T("Наземные войска для штурма колоний: производство в колониях, десант на кораблях, гарнизоны. Карты открываются технологиями");
            _sideHint.text = _tab == Tab.Production
                ? Loc.T("<b>Как это работает</b>\nПехоту набирают в городских районах, технику собирают в промышленных, оборону строят на месте, приказы выдаёт штаб столицы.\nГотовые карты ждут на колонии — боевые корабли с автопогрузкой забирают их сами.")
                : Loc.T("<b>Десант</b>\nКорвет берёт 1 место, фрегат — 2, эсминец — 4; приказы места не занимают.\nКлик по карте резерва — погрузить на корабль у этой колонии, по карте на корабле — высадить.");

            var sys = EmpireStats.GetSystem(_colony);
            if (sys != null && sys.OwnerId == 0)
            {
                _colonyName.text = sys.Name;
                _colonyInfo.text = Loc.F("Жители: <b>{0}</b>\nГородских районов: <b>{1}</b> · промышленных: <b>{2}</b>\nУкрепления базы: <b>{3}</b>",
                    ArmyManager.SystemPopulation(sys), ArmyManager.Districts(sys, DistrictType.Urban), ArmyManager.Districts(sys, DistrictType.Industrial), sys.Fortification);
            }
            else { _colonyName.text = Loc.T("Нет колоний"); _colonyInfo.text = Loc.T("Карты производят колонии с жителями"); }

            float sp = _scroll != null ? _scroll.verticalNormalizedPosition : 1f;
            Canvas.ForceUpdateCanvases();
            LGBuild.Clear(_list);
            if (_tab == Tab.Production) BuildProduction(sys, am); else BuildForces(am);
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = sp;
        }

        private static bool Match(CardDef d, KindFilter k)
        {
            switch (k)
            {
                case KindFilter.Infantry: return d.Site == ProdSite.City;
                case KindFilter.Vehicles: return d.Site == ProdSite.Industry;
                case KindFilter.Orders: return d.Site == ProdSite.Command;
                case KindFilter.Defense: return d.Site == ProdSite.Garrison;
                case KindFilter.Faction: return d.Faction != null;
                default: return true;
            }
        }

        private static string SiteHeader(ProdSite s)
        {
            switch (s)
            {
                case ProdSite.City: return Loc.T("ПЕХОТА · ГОРОДСКИЕ РАЙОНЫ");
                case ProdSite.Industry: return Loc.T("ТЕХНИКА · ПРОМЫШЛЕННЫЕ РАЙОНЫ");
                case ProdSite.Command: return Loc.T("ПРИКАЗЫ И ГЕРОИ · ШТАБ В СТОЛИЦЕ");
                default: return Loc.T("ОБОРОНА КОЛОНИИ");
            }
        }

        private void BuildProduction(StarSystem sys, ArmyManager am)
        {
            if (sys == null || sys.OwnerId != 0) { Note(Loc.T("Нет колоний с жителями — карты производить негде")); return; }

            // Очередь — строка мини-карт с прогрессом
            var cm = ConstructionManager.Instance;
            if (cm != null)
            {
                var jobs = new List<ConstructionJob>();
                foreach (var j in cm.Jobs) if (j.Owner == 0 && j.Kind == JobKind.Troop && j.SystemId == sys.Id) jobs.Add(j);
                if (jobs.Count > 0) { Header(Loc.F("В ПРОИЗВОДСТВЕ · {0}", jobs.Count), LGIcon.Construction); QueueRow(jobs); }
            }

            string faction = FactionTraits.Key(0);
            var groups = new SortedDictionary<int, List<CardDef>>();
            int shown = 0;
            foreach (var d in CardCatalog.ForFaction(faction))
            {
                if (d.Site == ProdSite.None || !Match(d, _kind)) continue;
                bool locked = !ArmyManager.Unlocked(0, d);
                if (_avail == AvailFilter.Locked && !locked) continue;
                if (_avail == AvailFilter.Ready && (locked || am.ProduceBlocker(0, d, d.Site == ProdSite.Command ? null : sys) != null || !Affordable(d))) continue;
                if (!groups.TryGetValue((int)d.Site, out var l)) groups[(int)d.Site] = l = new List<CardDef>();
                l.Add(d);
                shown++;
            }
            if (shown == 0) { Note(Loc.T("Под фильтр не подходит ни одна карта")); return; }

            float width = _list.rect.width - 16f;
            if (width < 300f) width = 1400f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((width + 12f) / (TileW + 12f)));
            foreach (var kv in groups)
            {
                var list = kv.Value;
                list.Sort(Compare);
                Header(SiteHeader((ProdSite)kv.Key), SiteIcon((ProdSite)kv.Key));
                for (int i = 0; i < list.Count; i += perRow)
                {
                    var row = LGBuild.Rect(_list, "Row");
                    LGBuild.Height(row.gameObject, TileH);
                    for (int k = 0; k < perRow && i + k < list.Count; k++)
                        Tile(row, list[i + k], sys, am, 4 + k * (TileW + 12f));
                }
            }
        }

        private int Compare(CardDef a, CardDef b)
        {
            int c;
            switch (_sort)
            {
                case SortBy.Power: c = b.Power.CompareTo(a.Power); break;
                case SortBy.Armor: c = b.Armor.CompareTo(a.Armor); break;
                case SortBy.Name: c = string.Compare(a.Name, b.Name, System.StringComparison.CurrentCulture); break;
                default: c = a.Cost.CompareTo(b.Cost); break;
            }
            if (c == 0) c = ArmyManager.Unlocked(0, b).CompareTo(ArmyManager.Unlocked(0, a));
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        }

        private static LGIcon SiteIcon(ProdSite s) => s == ProdSite.City ? LGIcon.Population : s == ProdSite.Industry ? LGIcon.Industry : s == ProdSite.Command ? LGIcon.Target : LGIcon.Starbase;

        private static bool Affordable(CardDef d)
        {
            ArmyManager.Cost(d, out float e, out float m, out float a, out float inf);
            var eco = EconomyManager.Instance;
            return eco != null && eco.EnergyCredits >= e && eco.Minerals >= m && eco.Alloys >= a && eco.Influence >= inf;
        }

        private void Header(string text, LGIcon icon)
        {
            var row = LGBuild.Rect(_list, "H");
            LGBuild.Height(row.gameObject, 30);
            var ic = LGIcons.Create(row, icon, 14, Gold);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, -2), new Vector2(14, 14));
            var t = LGBuild.Label(row, text, 11, Gold, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch(28, 4, 4, 0);
            t.Track(1.4f);
            var line = LGBuild.Panel(row, "Line", new Color(Gold.r, Gold.g, Gold.b, 0.22f));
            line.raycastTarget = false;
            line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 1);
        }

        private void Note(string text)
        {
            var row = LGBuild.Rect(_list, "Note");
            LGBuild.Height(row.gameObject, 40);
            var t = LGBuild.Label(row, text, 13, Muted, TextAnchor.MiddleLeft, wrap: true);
            t.rectTransform.Stretch(10, 0, 10, 0);
        }

        private void QueueRow(List<ConstructionJob> jobs)
        {
            var row = LGBuild.Rect(_list, "Queue");
            LGBuild.Height(row.gameObject, MiniCard.y + 34);
            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i];
                var d = CardCatalog.Get(j.DesignId);
                if (d == null) continue;
                float x = 10 + MiniCard.x * 0.5f + i * (MiniCard.x + 14f);
                var cv = CardView.Create(row, new BCard(d), 0, FactionTraits.Key(0), MiniCard, true);
                cv.Rt.anchorMin = cv.Rt.anchorMax = new Vector2(0, 1);
                cv.Rt.anchoredPosition = new Vector2(x, -MiniCard.y * 0.5f - 2);
                cv.SetAlpha(0.55f + 0.45f * j.Progress);
                HookPreview(cv);
                var host = LGBuild.Rect(row, "Bar");
                host.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(x, -MiniCard.y - 12), new Vector2(MiniCard.x - 6, 6));
                LGBuild.Bar(host, Cyan, j.Progress, 5f);
                var days = LGBuild.Label(row, Loc.F("{0:0} дн.", Mathf.Ceil(j.DaysLeft)), 11, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, bold: true);
                days.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(x, -MiniCard.y - 26), new Vector2(MiniCard.x, 16));
            }
        }

        /// <summary>Плитка витрины: стеклянная подложка, карта, цена и кнопка. Закрытая технологией — с замком.</summary>
        private void Tile(RectTransform row, CardDef d, StarSystem sys, ArmyManager am, float x)
        {
            bool locked = !ArmyManager.Unlocked(0, d);
            string blocker = am.ProduceBlocker(0, d, d.Site == ProdSite.Command ? null : sys);
            ArmyManager.Cost(d, out float e, out float m, out float a, out float inf);
            var eco = EconomyManager.Instance;
            bool afford = Affordable(d);
            bool can = blocker == null && afford;

            var tile = LGBuild.Panel(row, d.Id, CTile);
            tile.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, 0), new Vector2(TileW, TileH - 6));
            LG.Platter(tile.gameObject, 12f).SetRim(can ? new Color(0.45f, 0.95f, 0.90f, 0.35f) : new Color(1f, 1f, 1f, 0.08f));

            var cv = CardView.Create(tile.rectTransform, new BCard(d), 0, FactionTraits.Key(0), ShopCard, false);
            cv.Rt.anchorMin = cv.Rt.anchorMax = new Vector2(0.5f, 1f);
            cv.Rt.anchoredPosition = new Vector2(0, -ShopCard.y * 0.5f - 10);
            cv.SetHoverable(true);
            cv.HoverLift = 6f;
            cv.HoverScale = 0.04f;
            HookPreview(cv);
            if (locked || blocker != null) cv.SetAlpha(locked ? 0.4f : 0.6f);

            if (locked)
            {
                // Замок с названием технологии поверх карты
                var lk = LGBuild.Panel(tile.rectTransform, "Lock", new Color(0.02f, 0.03f, 0.05f, 0.88f));
                lk.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -ShopCard.y * 0.5f - 10), new Vector2(TileW - 20, 64));
                LG.Chip(lk.gameObject, new Color(Gold.r, Gold.g, Gold.b, 0.45f), 12f);
                var li = LGIcons.Create(lk.transform, LGIcon.Lock, 16, Gold);
                li.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -14), new Vector2(16, 16));
                var lt = LGBuild.Label(lk.transform, ArmyManager.TechName(d) ?? "", 11, Color.white, TextAnchor.LowerCenter, bold: true, wrap: true);
                lt.rectTransform.Stretch(8, 6, 8, 26);
                lt.resizeTextForBestFit = true; lt.resizeTextMinSize = 8; lt.resizeTextMaxSize = 11;
                lt.verticalOverflow = VerticalWrapMode.Truncate;
                TooltipHelper.Attach(lk.gameObject, Loc.F("<b>{0}</b>\nОткроется технологией «{1}» — её можно изучить в окне технологий (T)", d.Name, ArmyManager.TechName(d)));
                lk.raycastTarget = true;
            }

            // Цена
            var price = LGBuild.Rect(tile.rectTransform, "Price");
            price.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -ShopCard.y - 26), new Vector2(TileW - 8, 18));
            var parts = new List<(LGIcon ic, string v, bool ok, Color c)>();
            if (e > 0) parts.Add((LGIcon.Energy, e.ToString("0"), eco != null && eco.EnergyCredits >= e, BuildMenuModal.CEnergy));
            if (m > 0) parts.Add((LGIcon.Minerals, m.ToString("0"), eco != null && eco.Minerals >= m, BuildMenuModal.CMinerals));
            if (a > 0) parts.Add((LGIcon.Alloys, a.ToString("0"), eco != null && eco.Alloys >= a, BuildMenuModal.CAlloys));
            if (inf > 0) parts.Add((LGIcon.Influence, inf.ToString("0"), eco != null && eco.Influence >= inf, BuildMenuModal.CInfluence));
            parts.Add((LGIcon.Clock, d.Days > 0 && d.Site != ProdSite.Command ? Loc.F("{0}д", d.Days) : Loc.T("сразу"), true, Muted));
            float w = 0f;
            foreach (var p in parts) w += 15 + p.v.Length * 7f + 6;
            float px = -w * 0.5f;
            foreach (var p in parts)
            {
                var i = LGIcons.Create(price, p.ic, 12, p.c);
                i.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(px, 0), new Vector2(12, 12));
                var t = LGBuild.Label(price, p.v, 12, p.ok ? UIManager.DS.TextPrimary : UIManager.DS.Red, TextAnchor.MiddleLeft, bold: true);
                t.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(px + 15, 0), new Vector2(60, 16));
                px += 15 + p.v.Length * 7f + 6;
            }

            string label = can ? Loc.T("ЗАКАЗАТЬ") : locked ? Loc.T("ЗАКРЫТО") : blocker != null ? Loc.T("НЕДОСТУПНО") : Loc.T("НЕ ХВАТАЕТ");
            var btn = LGBuild.Button(tile.rectTransform, "Order", can ? UIManager.DS.BtnPrimary : UIManager.DS.BtnDisabled,
                can ? UIManager.DS.BtnPrimaryHi : new Color(1, 1, 1, 0.12f), () => Order(d), can ? LGIcon.Check : (LGIcon?)null, label, 11);
            ((RectTransform)btn.transform).At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 12), new Vector2(TileW - 24, 32));
            btn.interactable = can;
            string tip = $"<b>{d.Name}</b>\n{d.Text}";
            if (d.Upkeep > 0f) tip += Loc.F("\n<color=#8AA2A8>Содержание: {0:0.#} гелия в месяц</color>", d.Upkeep);
            if (d.Size > 0) tip += Loc.F("\n<color=#8AA2A8>Мест на корабле: {0}</color>", d.Size);
            if (blocker != null) tip += $"\n<color=#FF8080>{blocker}</color>";
            else if (!afford) tip += "\n<color=#FF8080>" + Loc.T("Не хватает ресурсов") + "</color>";
            TooltipHelper.Attach(btn.gameObject, tip);
        }

        private void Order(CardDef d)
        {
            var sys = EmpireStats.GetSystem(_colony);
            var am = ArmyManager.Instance;
            if (am == null) return;
            if (am.TryProducePlayer(d, d.Site == ProdSite.Command ? null : sys))
            {
                SFXManager.Play(Sfx.BuildQueued);
                UIManager.Instance?.RefreshResourceBarPublic();
            }
            else SFXManager.Play(Sfx.UiDenied);
            _dirty = true;
        }

        // ---------------------------------------------------------------- войска

        private void BuildForces(ArmyManager am)
        {
            var ships = TroopShips();
            Header(Loc.F("ДЕСАНТ НА КОРАБЛЯХ · {0}", ships.Count), LGIcon.Fleet);
            if (ships.Count == 0) Note(Loc.T("Нет военных кораблей — постройте корвет, фрегат или эсминец"));
            foreach (var f in ships) FleetRow(f, am);

            Header(Loc.T("КОЛОНИИ: РЕЗЕРВ И ГАРНИЗОНЫ"), LGIcon.Planet);
            bool any = false;
            foreach (var s in Colonies())
            {
                var here = am.AtSystem(0, s.Id);
                if (here.Count == 0) continue;
                any = true;
                ColonyRow(s, here, am, ships);
            }
            if (!any) Note(Loc.T("Резерв пуст — закажите карты на вкладке «Производство». Готовые карты ждут на колонии, корабли с автопогрузкой забирают их сами"));
        }

        private RectTransform Block(string name, bool active)
        {
            var b = LGBuild.Panel(_list, name, CTile);
            LG.Platter(b.gameObject, 12f).SetRim(active ? new Color(Gold.r, Gold.g, Gold.b, 0.4f) : new Color(1f, 1f, 1f, 0.08f));
            return b.rectTransform;
        }

        /// <summary>Места под карты: выемки по числу мест, карты ложатся в них по порядку.</summary>
        private void Slots(RectTransform parent, List<CardInstance> cards, int slots, float left, float top,
                           System.Action<CardView, CardInstance> hook, System.Func<CardInstance, string> status)
        {
            int n = Mathf.Max(slots, cards.Count);
            for (int i = 0; i < n; i++)
            {
                var pos = new Vector2(left + MiniCard.x * 0.5f + i * (MiniCard.x + 10f), -top - MiniCard.y * 0.5f);
                if (i >= cards.Count)
                {
                    var sl = CardSkin.Simple(parent, "Slot", "slot", Color.white);
                    sl.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), pos, MiniCard);
                    continue;
                }
                var c = cards[i];
                if (c.D == null) continue;
                var cv = CardView.Create(parent, new BCard(c.D, c.Id, c.Stars), 0, FactionTraits.Key(0), MiniCard, true);
                cv.Rt.anchorMin = cv.Rt.anchorMax = new Vector2(0, 1);
                cv.Rt.anchoredPosition = pos;
                string st = status?.Invoke(c);
                if (st != null) cv.SetStatus(st, new Color(1f, 0.55f, 0.45f));
                if (c.Wounded) cv.SetAlpha(0.6f);
                HookPreview(cv);
                cv.HoverLift = 6f;
                cv.HoverScale = 0.08f;
                hook?.Invoke(cv, c);
            }
        }

        /// <summary>Строка корабля: значок и имя, где стоит и заполнение мест, кнопки — слева; места с картами — справа.</summary>
        private void FleetRow(FleetData f, ArmyManager am)
        {
            var cards = am.OnFleet(f.Id);
            int used = am.Used(f.Id), cap = ArmyManager.Capacity(f);
            var b = Block("Fleet" + f.Id, used > 0);
            LGBuild.Height(b.gameObject, MiniCard.y + 24);
            var sys = EmpireStats.GetSystem(f.CurrentSystemId);
            bool moving = f.State == FleetState.InHyperlane;

            var ic = LGIcons.Create(b, LGIcon.Ship, 22, used > 0 ? Gold : Cyan);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(28, -26), new Vector2(22, 22));
            var t = LGBuild.Label(b, f.Name, 15, Color.white, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(50, -24), new Vector2(300, 22));
            var loc = LGBuild.Label(b, moving ? Loc.T("в пути") : sys != null ? sys.Name : "?", 12, Muted, TextAnchor.MiddleLeft);
            loc.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(50, -44), new Vector2(300, 18));
            var bar = LGBuild.Rect(b, "Cap");
            bar.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(50, -64), new Vector2(170, 6));
            LGBuild.Bar(bar, used >= cap ? Gold : Cyan, cap > 0 ? (float)used / cap : 0f, 5f);
            var capT = LGBuild.Label(b, Loc.F("мест {0}/{1}", used, cap), 11, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, bold: true);
            capT.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(228, -64), new Vector2(90, 16));

            bool auto = am.AutoLoad(f.Id);
            var ab = LGBuild.Button(b, "Auto", auto ? UIManager.DS.BtnSuccess : UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.25f),
                () => { am.SetAutoLoad(f.Id, !auto); SFXManager.Play(Sfx.UiClick); }, auto ? LGIcon.Check : (LGIcon?)null, Loc.T("АВТОПОГРУЗКА"), 10);
            ((RectTransform)ab.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(50, 12), new Vector2(150, 30));
            TooltipHelper.Attach(ab.gameObject, Loc.T("<b>Автопогрузка</b>\nКорабль у своей колонии сам забирает готовые карты резерва — сильнейшие первыми"));
            bool canUnload = !moving && sys != null && sys.OwnerId == 0 && sys.HasStarbase && cards.Count > 0;
            var un = LGBuild.Button(b, "Unload", canUnload ? UIManager.DS.BtnPrimary : UIManager.DS.BtnDisabled, new Color(1, 1, 1, 0.25f),
                () => { if (am.Unload(f) > 0) SFXManager.Play(Sfx.DealRemove); }, null, Loc.T("ВЫСАДИТЬ"), 10);
            ((RectTransform)un.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(208, 12), new Vector2(112, 30));
            un.interactable = canUnload;
            TooltipHelper.Attach(un.gameObject, canUnload ? Loc.T("Весь десант корабля — в резерв этой колонии") : Loc.T("Высадить можно у своей колонии со звёздной базой"));

            Slots(b, cards, Mathf.Max(0, cap - used + cards.Count), 340f, 12f, (cv, c) =>
            {
                cv.OnClick = _ =>
                {
                    if (moving || sys == null || sys.OwnerId != 0 || !sys.HasStarbase) { SFXManager.Play(Sfx.UiDenied); return; }
                    am.UnloadOne(c);
                    SFXManager.Play(Sfx.DealRemove);
                };
            }, null);
        }

        private void ColonyRow(StarSystem s, List<CardInstance> here, ArmyManager am, List<FleetData> ships)
        {
            var reserve = new List<CardInstance>();
            var gar = new List<CardInstance>();
            foreach (var c in here) (c.Garrison ? gar : reserve).Add(c);
            var docked = new List<FleetData>();
            foreach (var f in ships) if (f.State != FleetState.InHyperlane && f.CurrentSystemId == s.Id) docked.Add(f);

            var b = Block("Sys" + s.Id, false);
            int lines = (reserve.Count > 0 ? 1 : 0) + (gar.Count > 0 ? 1 : 0);
            LGBuild.Height(b.gameObject, 24 + Mathf.Max(1, lines) * (MiniCard.y + 14));
            var ic = LGIcons.Create(b, LGIcon.Planet, 22, Cyan);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(28, -26), new Vector2(22, 22));
            var t = LGBuild.Label(b, s.Name, 15, Color.white, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(50, -24), new Vector2(280, 22));
            var info = LGBuild.Label(b, Loc.F("резерв {0} · гарнизон {1}", reserve.Count, gar.Count) + "\n" +
                (reserve.Count == 0 ? "" : docked.Count > 0 ? Loc.T("клик по карте — погрузить") : Loc.T("у колонии нет кораблей")), 12, Muted, TextAnchor.UpperLeft);
            info.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(50, -38), new Vector2(280, 40));

            float top = 12f;
            if (reserve.Count > 0)
            {
                Slots(b, reserve, 0, 340f, top, (cv, c) =>
                {
                    cv.OnClick = _ =>
                    {
                        foreach (var f in docked)
                            if (am.LoadBlocker(f, c) == null) { am.Load(f, c); SFXManager.Play(Sfx.DealPlace); return; }
                        string why = docked.Count == 0 ? Loc.T("У колонии нет кораблей с десантными местами") : am.LoadBlocker(docked[0], c);
                        NotificationCenter.Show(Loc.T("Не погрузить"), why, NotificationCenter.Kind.Warning, 3f);
                        SFXManager.Play(Sfx.UiDenied);
                    };
                }, c => c.Wounded ? Loc.F("РАНЕН {0} дн.", c.WoundDays) : null);
                top += MiniCard.y + 14;
            }
            if (gar.Count > 0)
            {
                var gl = LGBuild.Label(b, Loc.T("ГАРНИЗОН"), 10, new Color(0.55f, 0.85f, 1f), TextAnchor.MiddleRight, bold: true);
                gl.rectTransform.At(new Vector2(0, 1), new Vector2(1, 0.5f), new Vector2(330, -top - MiniCard.y * 0.5f), new Vector2(120, 16));
                gl.Track(1.2f);
                Slots(b, gar, 0, 340f, top, null, c => c.Wounded ? Loc.F("РАНЕН {0} дн.", c.WoundDays) : null);
            }
        }

        // ---------------------------------------------------------------- предпросмотр

        private void HookPreview(CardView cv)
        {
            cv.SetHoverable(true);
            cv.OnHoverIn = ShowPreview;
            cv.OnHoverOut = _ => HidePreview();
        }

        private void ShowPreview(CardView v)
        {
            HidePreview();
            if (v == null || v.Card == null) return;
            _preview = CardView.Create(_previewRoot, v.Card, 0, FactionTraits.Key(0), BigCard, false);
            _preview.Rt.anchoredPosition = Vector2.zero;
            _preview.Punch(0.04f);
            foreach (var g in _preview.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            _previewRoot.SetAsLastSibling();
        }

        private void HidePreview()
        {
            if (_preview != null) Destroy(_preview.gameObject);
            _preview = null;
        }
    }
}
