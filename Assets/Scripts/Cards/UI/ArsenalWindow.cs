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
    /// Окно «Арсенал» (левое меню, клавиша J) — армия для штурма и обороны колоний. Оформлено в общем стиле окон игры
    /// (стекло, бирюзовая кромка, золотые подписи):
    ///   • слева — режим состава (авто или вручную), размер армии, военная база империи и сводка резерва;
    ///   • вкладка «СОСТАВ АРМИИ» — витрина карт с кнопками −/+ (сколько держать в армии), сверху — общая очередь
    ///     производства (клик по карте — отменить); закрытые технологией — затемнены, с замком;
    ///   • вкладка «РЕЗЕРВ» — готовые карты империи, раненые и ветераны.
    /// Ни погрузки на корабли, ни заказов по колониям: империя сама производит недостающее и пополняет потери.
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

        private enum Tab { Doctrine, Reserve }
        private enum KindFilter { All, Infantry, Vehicles, Orders, Defense, Faction }
        private enum AvailFilter { All, InArmy, Locked }
        private enum SortBy { Cost, Power, Armor, Name }

        private GameObject _root;
        private RectTransform _rt, _content, _list, _previewRoot, _filterBar, _tabBar;
        private Text _subtitle, _baseInfo, _sideHint, _sizeLabel, _sizeValue;
        private Button _sizeMinus, _sizePlus;
        private readonly List<Text> _sideValues = new List<Text>();
        private ScrollRect _scroll;
        private CardView _preview;
        private string _sig;
        private float _refresh;
        private bool _dirty;
        private Tab _tab = Tab.Doctrine;
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
        private static readonly Color Warn = new Color(1f, 0.62f, 0.42f);

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
            if (Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Close(); return; }
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

        private string Signature()
        {
            var eco = EconomyManager.Instance;
            var am = ArmyManager.Instance;
            var sb = new StringBuilder();
            sb.Append((int)_tab).Append((int)_kind).Append((int)_avail).Append((int)_sort).Append('|');
            if (eco != null) sb.Append((int)eco.EnergyCredits / 5).Append(',').Append((int)eco.Minerals / 5).Append(',').Append((int)eco.Alloys / 5).Append(',').Append((int)eco.Influence / 5).Append('|');
            if (am != null)
            {
                foreach (var c in am.Of(0)) sb.Append(c.Id).Append(c.WoundDays > 0 ? 'w' : '-').Append(c.Stars);
                sb.Append('|');
                foreach (var j in am.JobsOf(0)) sb.Append(j.Def).Append(':').Append((int)(j.Progress * 20)).Append(',');
                sb.Append('|').Append(am.TargetTotal(0)).Append(am.PlayerManual ? 'm' : 'a').Append(am.PlayerSize);
            }
            var b = ArmyManager.BaseOf(0);
            sb.Append('|').Append(b.Colonies).Append(b.Urban).Append(',').Append(b.Industrial).Append(',').Append(b.Population / 4);
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
            tx += Chip(_tabBar, Loc.T("СОСТАВ АРМИИ"), LGIcon.Swords, tx, 200f, () => _tab == Tab.Doctrine, () => SetTab(Tab.Doctrine), 38f, 12);
            tx += Chip(_tabBar, Loc.T("РЕЗЕРВ"), LGIcon.Planet, tx, 150f, () => _tab == Tab.Reserve, () => SetTab(Tab.Reserve), 38f, 12);

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
            fx += Chip(_filterBar, Loc.T("В составе"), LGIcon.Check, fx, 130f, () => _avail == AvailFilter.InArmy, () => SetAvail(_avail == AvailFilter.InArmy ? AvailFilter.All : AvailFilter.InArmy));
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
            if (_filterBar != null) _filterBar.gameObject.SetActive(_tab == Tab.Doctrine);
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

            var head = LGBuild.Label(s, Loc.T("СОСТАВ АРМИИ"), 11, Gold, TextAnchor.UpperLeft, bold: true);
            head.rectTransform.TopBand(16, 18, 18, 18);
            head.Track(1.4f);

            // Режим: империя подбирает состав сама или игрок задаёт его кнопками −/+
            var mode = LGBuild.Rect(s, "Mode");
            mode.TopBand(40, 36, 18, 18);
            Chip(mode, Loc.T("АВТО"), LGIcon.Star, 0f, 126f, () => ArmyManager.Instance != null && !ArmyManager.Instance.PlayerManual, () => ArmyManager.Instance?.SetPlayerAuto(true), 34f, 12);
            Chip(mode, Loc.T("ВРУЧНУЮ"), LGIcon.Gear, 134f, 126f, () => ArmyManager.Instance != null && ArmyManager.Instance.PlayerManual, () => ArmyManager.Instance?.SetPlayerAuto(false), 34f, 12);
            TooltipHelper.Attach(mode.GetChild(0).gameObject, Loc.T("<b>Автосостав</b>\nИмперия сама подбирает карты под размер армии: пехота, техника, приказы и оборона, лучшие из открытых. Состав обновляется с новыми технологиями"));
            TooltipHelper.Attach(mode.GetChild(1).gameObject, Loc.T("<b>Вручную</b>\nВы сами задаёте, сколько карт каждого вида держать, — кнопками −/+ под картами. Любое изменение включает этот режим"));

            var size = LGBuild.Rect(s, "Size");
            size.TopBand(86, 34, 18, 18);
            _sizeLabel = LGBuild.Label(size, "", 12, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
            _sizeLabel.rectTransform.Stretch(0, 0, 120, 0);
            _sizeMinus = Step(size, "-", -102f, () => { var a = ArmyManager.Instance; if (a != null) a.SetPlayerSize(a.PlayerSize - 4); });
            _sizePlus = Step(size, "+", -2f, () => { var a = ArmyManager.Instance; if (a != null) a.SetPlayerSize(a.PlayerSize + 4); });
            _sizeValue = LGBuild.Label(size, "", 16, Color.white, TextAnchor.MiddleCenter, bold: true);
            _sizeValue.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-38, 0), new Vector2(28, 30));
            TooltipHelper.Attach(size.gameObject, Loc.T("<b>Размер армии</b>\nСколько карт империя держит в резерве в режиме «Авто». Больше армия — больше содержание"));

            _baseInfo = LGBuild.Label(s, "", 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            _baseInfo.rectTransform.TopBand(130, 70, 18, 18);
            _baseInfo.lineSpacing = 1.2f;
            _baseInfo.supportRichText = true;

            var sep = LGBuild.Panel(s, "Sep", new Color(Gold.r, Gold.g, Gold.b, 0.22f));
            sep.rectTransform.TopBand(208, 1, 18, 18);

            var h2 = LGBuild.Label(s, Loc.T("РЕЗЕРВ ИМПЕРИИ"), 11, Gold, TextAnchor.UpperLeft, bold: true);
            h2.rectTransform.TopBand(222, 18, 18, 18);
            h2.Track(1.4f);
            (LGIcon, Color, string)[] rows =
            {
                (LGIcon.Swords, new Color(1f, 0.82f, 0.45f), Loc.T("Карт в резерве")),
                (LGIcon.Check, UIManager.DS.Green, Loc.T("Готовы к бою")),
                (LGIcon.Warning, new Color(1f, 0.5f, 0.45f), Loc.T("Ранены")),
                (LGIcon.Construction, Cyan, Loc.T("В производстве")),
                (LGIcon.Energy, BuildMenuModal.CEnergy, Loc.T("Содержание в месяц")),
            };
            float y = 248f;
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
            _sideHint.rectTransform.TopBand(y + 14, 260, 18, 18);
            _sideHint.lineSpacing = 1.15f;
            _sideHint.supportRichText = true;
        }

        /// <summary>Маленькая кнопка шага (−/+), прижатая к правому краю родителя со смещением x.</summary>
        private static Button Step(RectTransform parent, string label, float x, System.Action act)
        {
            var b = LGBuild.Button(parent, "Step" + label, UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.25f), () => { act(); SFXManager.Play(Sfx.UiClick, 0.7f); }, null, label, 16);
            ((RectTransform)b.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(x, 0), new Vector2(32, 30));
            return b;
        }

        // ================================================================ Наполнение

        private void Rebuild()
        {
            var am = ArmyManager.Instance;
            if (am == null) return;
            HidePreview();
            RefreshChips();
            var b = ArmyManager.BaseOf(0);
            var mine = am.Of(0);
            var jobs = am.JobsOf(0);
            int wounded = 0;
            foreach (var c in mine) if (c.Wounded) wounded++;
            _sideValues[0].text = mine.Count.ToString();
            _sideValues[1].text = (mine.Count - wounded).ToString();
            _sideValues[2].text = wounded.ToString();
            _sideValues[3].text = jobs.Count.ToString();
            _sideValues[4].text = am.Upkeep(0).ToString("0.#");

            bool auto = !am.PlayerManual;
            _sizeLabel.text = auto ? Loc.T("Размер армии") : Loc.T("Карт в составе");
            _sizeValue.text = (auto ? am.PlayerSize : am.TargetTotal(0)).ToString();
            _sizeMinus.gameObject.SetActive(auto);
            _sizePlus.gameObject.SetActive(auto);
            _sizeMinus.interactable = am.PlayerSize > ArmyManager.MinSize;
            _sizePlus.interactable = am.PlayerSize < ArmyManager.MaxSize;
            _sizeValue.rectTransform.anchoredPosition = new Vector2(auto ? -52 : -14, 0);

            int infantry = 0;
            foreach (var c in mine) if (c.D != null && c.D.Site == ProdSite.City && c.D.Has(Kw.Infantry)) infantry++;
            foreach (var j in jobs) { var d = CardCatalog.Get(j.Def); if (d != null && d.Site == ProdSite.City && d.Has(Kw.Infantry)) infantry++; }
            _baseInfo.text = b.Colonies == 0
                ? Loc.T("<color=#FF8080>Нет колоний с жителями — производить армию негде</color>")
                : Loc.F("Линий производства: <b>{0}</b> <color=#8AA2A8>(районы {1} + {2})</color>\nПризывников: <b>{3}/{4}</b> <color=#8AA2A8>(половина жителей)</color>\nСооружений обороны: до <b>{5}</b>",
                    b.Lines, b.Urban, b.Industrial, infantry, b.InfantryCap, b.StructureCap);

            _subtitle.text = Loc.T("Армия для штурма и обороны колоний. Задайте состав — империя сама производит и пополняет карты. Карты открываются технологиями");
            _sideHint.text = _tab == Tab.Doctrine
                ? Loc.T("<b>Как это работает</b>\nКнопки −/+ под картой — сколько таких держать в армии (с Shift — по 5). Империя сама заказывает недостающее и пополняет потери, когда хватает ресурсов.\nПри штурме в бой идут лучшие карты резерва — сколько поместится на корабли у цели: корвет 2 места, фрегат 4, эсминец 6. При обороне — сколько вместит колония.")
                : Loc.T("<b>Резерв</b>\nОбщий для всей империи: грузить на корабли и развозить по колониям ничего не нужно.\nРаненые лечатся 30 дней и пропускают бои. Выжившие в бою получают звезду ветерана.");

            float sp = _scroll != null ? _scroll.verticalNormalizedPosition : 1f;
            Canvas.ForceUpdateCanvases();
            LGBuild.Clear(_list);
            if (_tab == Tab.Doctrine) BuildDoctrine(am, b, jobs); else BuildReserve(am, mine);
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
                case ProdSite.City: return Loc.T("ПЕХОТА · НУЖЕН ГОРОДСКОЙ РАЙОН");
                case ProdSite.Industry: return Loc.T("ТЕХНИКА · НУЖЕН ПРОМЫШЛЕННЫЙ РАЙОН");
                case ProdSite.Command: return Loc.T("ПРИКАЗЫ И ГЕРОИ · ШТАБ В СТОЛИЦЕ");
                default: return Loc.T("ОБОРОНА · ТОЛЬКО ДЛЯ ЗАЩИТЫ КОЛОНИЙ");
            }
        }

        private int PerRow(float itemW)
        {
            float width = _list.rect.width - 16f;
            if (width < 300f) width = 1400f;
            return Mathf.Max(1, Mathf.FloorToInt((width + 12f) / (itemW + 12f)));
        }

        private void BuildDoctrine(ArmyManager am, ArmyManager.Base b, List<ArmyJob> jobs)
        {
            if (jobs.Count > 0)
            {
                Header(Loc.F("В ПРОИЗВОДСТВЕ · {0} · ЛИНИЙ {1}   ·   клик по карте — отменить заказ", jobs.Count, b.Lines), LGIcon.Construction);
                QueueRow(jobs, b.Lines, am);
            }

            string faction = FactionTraits.Key(0);
            var groups = new SortedDictionary<int, List<CardDef>>();
            int shown = 0;
            foreach (var d in CardCatalog.ForFaction(faction))
            {
                if (d.Site == ProdSite.None || !Match(d, _kind)) continue;
                bool locked = !ArmyManager.Unlocked(0, d);
                if (_avail == AvailFilter.Locked && !locked) continue;
                if (_avail == AvailFilter.InArmy && am.Target(0, d.Id) <= 0 && am.Count(0, d.Id) <= 0) continue;
                if (!groups.TryGetValue((int)d.Site, out var l)) groups[(int)d.Site] = l = new List<CardDef>();
                l.Add(d);
                shown++;
            }
            if (shown == 0) { Note(Loc.T("Под фильтр не подходит ни одна карта")); return; }

            int perRow = PerRow(TileW);
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
                        Tile(row, list[i + k], b, am, 4 + k * (TileW + 12f));
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

        /// <summary>Очередь империи: мини-карты с прогрессом; первые <paramref name="lines"/> — в работе. Клик — отменить.</summary>
        private void QueueRow(List<ArmyJob> jobs, int lines, ArmyManager am)
        {
            int perRow = PerRow(MiniCard.x + 2f);
            for (int i0 = 0; i0 < jobs.Count; i0 += perRow)
            {
                var row = LGBuild.Rect(_list, "Queue");
                LGBuild.Height(row.gameObject, MiniCard.y + 34);
                for (int i = i0; i < jobs.Count && i < i0 + perRow; i++)
                {
                    var j = jobs[i];
                    var d = CardCatalog.Get(j.Def);
                    if (d == null) continue;
                    bool active = i < lines;
                    float x = 10 + MiniCard.x * 0.5f + (i - i0) * (MiniCard.x + 14f);
                    var cv = CardView.Create(row, new BCard(d), 0, FactionTraits.Key(0), MiniCard, true);
                    cv.Rt.anchorMin = cv.Rt.anchorMax = new Vector2(0, 1);
                    cv.Rt.anchoredPosition = new Vector2(x, -MiniCard.y * 0.5f - 2);
                    cv.SetAlpha(active ? 0.55f + 0.45f * j.Progress : 0.4f);
                    HookPreview(cv);
                    cv.OnClick = _ => { am.CancelJob(j); SFXManager.Play(Sfx.DealRemove); };
                    var host = LGBuild.Rect(row, "Bar");
                    host.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(x, -MiniCard.y - 12), new Vector2(MiniCard.x - 6, 6));
                    LGBuild.Bar(host, active ? Cyan : new Color(1f, 1f, 1f, 0.25f), j.Progress, 5f);
                    var days = LGBuild.Label(row, active ? Loc.F("{0:0} дн.", Mathf.Ceil(j.DaysLeft)) : Loc.T("в очереди"), 11,
                        active ? UIManager.DS.TextPrimary : Muted, TextAnchor.MiddleCenter, bold: true);
                    days.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(x, -MiniCard.y - 26), new Vector2(MiniCard.x + 10, 16));
                }
            }
        }

        /// <summary>Плитка состава: карта, сколько в резерве и строится, цена и кнопки −/+ (сколько держать в армии).</summary>
        private void Tile(RectTransform row, CardDef d, ArmyManager.Base b, ArmyManager am, float x)
        {
            bool locked = !ArmyManager.Unlocked(0, d);
            string site = ArmyManager.SiteBlocker(0, d, b);
            int target = am.Target(0, d.Id), have = am.Count(0, d.Id), queued = am.Queued(0, d.Id);
            string wait = target > 0 ? am.WaitReason(0, d) : null;
            ArmyManager.Cost(d, out float e, out float m, out float a, out float inf);
            var eco = EconomyManager.Instance;

            var tile = LGBuild.Panel(row, d.Id, CTile);
            tile.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, 0), new Vector2(TileW, TileH - 6));
            LG.Platter(tile.gameObject, 12f).SetRim(target > 0 ? (wait != null ? new Color(Warn.r, Warn.g, Warn.b, 0.45f) : new Color(Gold.r, Gold.g, Gold.b, 0.5f))
                                                               : new Color(1f, 1f, 1f, 0.08f));

            var cv = CardView.Create(tile.rectTransform, new BCard(d), 0, FactionTraits.Key(0), ShopCard, false);
            cv.Rt.anchorMin = cv.Rt.anchorMax = new Vector2(0.5f, 1f);
            cv.Rt.anchoredPosition = new Vector2(0, -ShopCard.y * 0.5f - 10);
            cv.SetHoverable(true);
            cv.HoverLift = 6f;
            cv.HoverScale = 0.04f;
            HookPreview(cv);
            if (locked || site != null) cv.SetAlpha(locked ? 0.4f : 0.6f);

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

            // Сколько есть
            string stock = queued > 0 ? Loc.F("в резерве {0} · строится {1}", have, queued) : Loc.F("в резерве {0}", have);
            var st = LGBuild.Label(tile.rectTransform, wait != null ? Loc.T("ждёт") + " · " + stock : stock, 11,
                wait != null ? Warn : have > 0 ? UIManager.DS.TextPrimary : Muted, TextAnchor.MiddleCenter, bold: true);
            st.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -ShopCard.y - 20), new Vector2(TileW - 8, 16));

            // Цена
            var price = LGBuild.Rect(tile.rectTransform, "Price");
            price.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -ShopCard.y - 40), new Vector2(TileW - 8, 18));
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

            // Состав: − N +
            bool canAdd = !locked && site == null && target < ArmyManager.MaxDeck;
            var minus = LGBuild.Button(tile.rectTransform, "Minus", target > 0 ? UIManager.DS.BtnNeutral : UIManager.DS.BtnDisabled, new Color(1, 1, 1, 0.22f),
                () => Change(d, -1), null, "−", 18);
            ((RectTransform)minus.transform).At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-58, 12), new Vector2(40, 32));
            minus.interactable = target > 0;
            var plus = LGBuild.Button(tile.rectTransform, "Plus", canAdd ? UIManager.DS.BtnPrimary : UIManager.DS.BtnDisabled,
                canAdd ? UIManager.DS.BtnPrimaryHi : new Color(1, 1, 1, 0.12f), () => Change(d, 1), null, "+", 18);
            ((RectTransform)plus.transform).At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(58, 12), new Vector2(40, 32));
            plus.interactable = canAdd;
            var val = LGBuild.Panel(tile.rectTransform, "Target", new Color(0.02f, 0.05f, 0.07f, 0.9f), raycast: true);
            val.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 12), new Vector2(66, 32));
            LG.Chip(val.gameObject, target > 0 ? new Color(Gold.r, Gold.g, Gold.b, 0.5f) : new Color(1f, 1f, 1f, 0.12f), 10f);
            var vt = LGBuild.Label(val.transform, target.ToString(), 16, target > 0 ? Color.white : Muted, TextAnchor.MiddleCenter, bold: true);
            vt.rectTransform.Stretch(0, 0, 0, 0);

            string tip = $"<b>{d.Name}</b>\n{d.Text}";
            tip += Loc.F("\n\nВ составе армии: <b>{0}</b> · в резерве {1} · строится {2}", target, have, queued);
            if (d.Upkeep > 0f) tip += Loc.F("\n<color=#8AA2A8>Содержание: {0:0.#} гелия в месяц</color>", d.Upkeep);
            if (d.Size > 0 && d.Site != ProdSite.Garrison) tip += Loc.F("\n<color=#8AA2A8>Мест на кораблях: {0}</color>", d.Size);
            if (site != null) tip += $"\n<color=#FF8080>{site}</color>";
            else if (wait != null) tip += $"\n<color=#FFB080>{wait}</color>";
            tip += Loc.T("\n<color=#8AA2A8>−/+ — сколько держать в армии (Shift — по 5)</color>");
            TooltipHelper.Attach(val.gameObject, tip);
            TooltipHelper.Attach(plus.gameObject, tip);
            TooltipHelper.Attach(minus.gameObject, tip);
        }

        private void Change(CardDef d, int dir)
        {
            var am = ArmyManager.Instance;
            if (am == null) return;
            int step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 5 : 1;
            int before = am.Target(0, d.Id);
            am.SetTarget(0, d.Id, before + dir * step);
            SFXManager.Play(am.Target(0, d.Id) != before ? (dir > 0 ? Sfx.BuildQueued : Sfx.DealRemove) : Sfx.UiDenied, 0.8f);
            UIManager.Instance?.RefreshResourceBarPublic();
            _dirty = true;
        }

        // ---------------------------------------------------------------- резерв

        private void BuildReserve(ArmyManager am, List<CardInstance> mine)
        {
            Header(Loc.F("РЕЗЕРВ ИМПЕРИИ · {0}", mine.Count), LGIcon.Swords);
            if (mine.Count == 0)
            {
                Note(am.TargetTotal(0) > 0
                    ? Loc.T("Резерв пока пуст — карты в производстве, империя заказывает их сама, когда хватает ресурсов")
                    : Loc.T("Резерв пуст — включите «Авто» или задайте состав армии на вкладке «Состав армии»"));
                return;
            }
            var groups = new SortedDictionary<int, List<CardInstance>>();
            foreach (var c in mine)
            {
                if (c.D == null) continue;
                int k = (int)c.D.Site;
                if (!groups.TryGetValue(k, out var l)) groups[k] = l = new List<CardInstance>();
                l.Add(c);
            }
            int perRow = PerRow(MiniCard.x - 2f);
            foreach (var kv in groups)
            {
                var list = kv.Value;
                list.Sort((x, y) => { int c = string.CompareOrdinal(x.Def, y.Def); return c != 0 ? c : y.Stars.CompareTo(x.Stars); });
                Header(SiteHeader((ProdSite)kv.Key) + "  ·  " + list.Count, SiteIcon((ProdSite)kv.Key));
                for (int i = 0; i < list.Count; i += perRow)
                {
                    var row = LGBuild.Rect(_list, "Row");
                    LGBuild.Height(row.gameObject, MiniCard.y + 12);
                    for (int k = 0; k < perRow && i + k < list.Count; k++)
                    {
                        var c = list[i + k];
                        var cv = CardView.Create(row, new BCard(c.D, c.Id, c.Stars), 0, FactionTraits.Key(0), MiniCard, true);
                        cv.Rt.anchorMin = cv.Rt.anchorMax = new Vector2(0, 1);
                        cv.Rt.anchoredPosition = new Vector2(10 + MiniCard.x * 0.5f + k * (MiniCard.x + 10f), -MiniCard.y * 0.5f - 4);
                        if (c.Wounded) { cv.SetStatus(Loc.F("РАНЕН {0} дн.", c.WoundDays), new Color(1f, 0.55f, 0.45f)); cv.SetAlpha(0.6f); }
                        HookPreview(cv);
                        cv.HoverLift = 6f;
                        cv.HoverScale = 0.08f;
                    }
                }
            }
            Note(Loc.T("Сооружения, мины и гарнизонные отряды идут в бой только при обороне колоний"));
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
