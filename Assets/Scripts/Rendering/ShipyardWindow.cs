using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Орбитальная верфь, раскладка «Сухой док». Сверху — ряд стапелей: в занятом доке строящийся корабль
    /// проявляется латунным силуэтом по ходу работ, по шву идёт искра, крупно — процент; отмена с возвратом.
    /// Ниже — пять карточек кораблей: силуэт, назначение, боевой профиль проекта, цена (сплавы, гелий-3, дни),
    /// содержание и «Заложить» с причиной, если нельзя. Внизу — модернизация эскадры и переход в конструктор.
    /// </summary>
    public class ShipyardWindow : MonoBehaviour
    {
        public static ShipyardWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        // «Сухой док»: шапка, ряд стапелей (главное в окне), пять карточек кораблей, строка модернизации внизу
        private const float Width = 1100f, Height = 730f, Pad = 26f;
        private const float DocksTop = 144f, DocksH = 168f, CardsTop = 350f, CardsH = 280f, FootH = 62f;
        private static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI" };
        private static readonly Color Ink = new Color(0.953f, 0.918f, 0.827f);
        private static readonly Color Lit = new Color(0.96f, 0.86f, 0.60f);

        private class Option
        {
            public FleetType Type;
            public ShipClass Hull;
            public string Name;
            public LGIcon Icon;
            public Color Tint;
            public Button Build;
            public Text BuildLabel, Sub, Stats, Alloys, Energy, Days, Upkeep;
        }

        /// <summary>Док со строящимся кораблём: латунный силуэт проявляется по ходу работ, по шву идёт искра.</summary>
        private class SlotUi
        {
            public RectTransform Pic, Weld;
            public RectMask2D Clip;
            public Text Pct, State;
            public int Index;
        }

        private GameObject _root;
        private Text _eyebrow, _subtitle, _alloysText, _energyText;
        private RectTransform _queue;
        private Text _queueTitle;
        private Text _retrofitText;
        private Button _retrofitBtn, _retrofitAllBtn;
        private readonly List<Option> _options = new List<Option>();
        private readonly Dictionary<int, SlotUi> _slots = new Dictionary<int, SlotUi>();
        private string _queueSig = "";
        private float _timer;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Neon => UIManager.DS.NeonCyan;
        private static Color Gold => UIManager.DS.Gold;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ==================== ОТКРЫТИЕ ====================

        /// <summary>Система, из панели которой открыта верфь: заказанные корабли появятся у неё (−1 — главная верфь).</summary>
        private int _yardRequest = -1;

        public void Open() => Open(-1);

        public void Open(int systemId)
        {
            if (_root == null) return;
            _yardRequest = systemId;
            UIManager.Instance?.ShowModalDimPublic();
            MapModeController.HideGlobal();
            _queueSig = "";
            Refresh();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            LG.Skin(_root.transform);
        }

        public void Close()
        {
            if (_root == null) return;
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            MapModeController.ShowGlobal();
        }

        private void Update()
        {
            if (!IsOpen) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.4f;
            Refresh();
        }

        // ==================== КАРКАС ====================

        public void Build(Canvas host)
        {
            var rt = LGBuild.Rect(host.transform, "ShipyardWindow");
            rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Width, Height));
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            LG.Glass(_root, 24f);   // фон «Обсерватория» вешается сам
            LG.Motion(_root, LGAppear.Kind.Pop);

            // Шапка: верфь и система, название антиквой, строка о стапелях; справа — запас сплавов и гелия-3
            _eyebrow = LGBuild.Label(rt, "", 11, Gold, TextAnchor.UpperLeft, bold: true);
            _eyebrow.font = GameFont.MonoMedium;
            _eyebrow.Track(2f);
            _eyebrow.rectTransform.TopBand(24, 16, Pad, 380);
            var title = LGBuild.Label(rt, Loc.T("Орбитальная верфь"), 30, Ink, TextAnchor.UpperLeft);
            title.font = GameFont.Display;
            title.rectTransform.TopBand(40, 40, Pad, 380);
            _subtitle = LGBuild.Label(rt, "", 12, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.TopBand(82, 18, Pad, 380);

            var purse = LGBuild.Rect(rt, "Purse");
            purse.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-74, -28), new Vector2(292, 30));
            _alloysText = PurseChip(purse, LGIcon.Alloys, new Color(0.475f, 0.816f, 0.749f), 0f, Loc.T("сплавов"));
            _energyText = PurseChip(purse, LGIcon.Energy, new Color(1f, 0.80f, 0.32f), 150f, Loc.T("гелия-3"));

            var close = LGBuild.Button(rt, "Close", new Color(0.075f, 0.125f, 0.220f), new Color(1f, 0.45f, 0.48f, 0.55f), Close, LGIcon.Close, null, 11);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -24), new Vector2(34, 34));

            // Линия под шапкой с ромбом посередине
            var line = LGBuild.Panel(rt, "HeadLine", new Color(Gold.r, Gold.g, Gold.b, 0.35f));
            line.rectTransform.TopBand(116, 1, Pad + 40, Pad + 40);
            LG.Ignore(line.gameObject);
            var dia = LGBuild.Panel(rt, "HeadDiamond", Gold);
            dia.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -116.5f), new Vector2(7, 7));
            dia.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            LG.Ignore(dia.gameObject);

            // Стапели — главное в окне
            var qi = LGIcons.Create(rt, LGIcon.Clock, 13, Gold);
            qi.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(Pad, -(DocksTop - 15)), new Vector2(13, 13));
            _queueTitle = LGBuild.Label(rt, "", 11, Gold, TextAnchor.MiddleLeft, bold: true);
            _queueTitle.font = GameFont.MonoMedium;
            _queueTitle.Track(1.6f);
            _queueTitle.rectTransform.TopBand(DocksTop - 24, 18, Pad + 20, Pad);
            _queue = LGBuild.Rect(rt, "Docks");
            _queue.TopBand(DocksTop, DocksH, Pad, Pad);

            // Заложить корабль — пять карточек в ряд
            var sec = LGBuild.Rect(rt, "OrderHead");
            sec.TopBand(CardsTop - 28, 22, Pad, Pad);
            LGBuild.SectionDecor(sec, Loc.T("ЗАЛОЖИТЬ КОРАБЛЬ"), LGIcon.Construction, Gold);
            var cards = LGBuild.Rect(rt, "Order");
            cards.TopBand(CardsTop, CardsH, Pad, Pad);
            _options.Clear();
            AddOption(cards, FleetType.Science, ShipClass.Corvette, Loc.T("Научный корабль"), LGIcon.ScienceShip, new Color(0.45f, 1f, 0.62f), 0);
            AddOption(cards, FleetType.Constructor, ShipClass.Corvette, Loc.T("Строительный корабль"), LGIcon.ConstructorShip, new Color(1f, 0.82f, 0.36f), 1);
            AddOption(cards, FleetType.Military, ShipClass.Corvette, Loc.T("Корвет"), ModuleVisuals.HullIcon(ShipClass.Corvette), new Color(1f, 0.48f, 0.39f), 2);
            AddOption(cards, FleetType.Military, ShipClass.Frigate, Loc.T("Фрегат"), ModuleVisuals.HullIcon(ShipClass.Frigate), new Color(1f, 0.48f, 0.39f), 3);
            AddOption(cards, FleetType.Military, ShipClass.Destroyer, Loc.T("Эсминец"), ModuleVisuals.HullIcon(ShipClass.Destroyer), new Color(1f, 0.48f, 0.39f), 4);

            // Строка внизу: модернизация эскадры и конструктор
            var foot = LGBuild.Panel(rt, "Retrofit", new Color(0.047f, 0.078f, 0.137f, 0.92f));
            var frt = foot.rectTransform;
            frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(1, 0); frt.pivot = new Vector2(0.5f, 0);
            frt.offsetMin = new Vector2(Pad, 22); frt.offsetMax = new Vector2(-Pad, 22 + FootH);
            LG.Platter(foot.gameObject, 2f).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.2f));
            var ri = LGIcons.Create(foot.transform, LGIcon.Wrench, 14, Gold);
            ri.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(14, 14));
            var rh = LGBuild.Label(foot.transform, Loc.T("МОДЕРНИЗАЦИЯ ЭСКАДРЫ"), 10, Gold, TextAnchor.UpperLeft, bold: true);
            rh.font = GameFont.MonoMedium;
            rh.Track(1.6f);
            rh.rectTransform.Stretch(38, 0, 640, 8);
            _retrofitText = LGBuild.Label(foot.transform, "", 11, Primary, TextAnchor.UpperLeft, wrap: true);
            _retrofitText.rectTransform.Stretch(38, 6, 640, 24);
            _retrofitText.resizeTextForBestFit = true; _retrofitText.resizeTextMinSize = 9; _retrofitText.resizeTextMaxSize = 11;

            var designer = LGBuild.Button(foot.transform, "Designer", UIManager.DS.BtnPrimary, UIManager.DS.BtnPrimaryHi, () =>
            {
                Close();
                ShipDesignerModal.Instance?.Open();
            }, null, Loc.T("КОНСТРУКТОР КОРАБЛЕЙ"), 12);
            var game = designer.GetComponent<LGGameButton>();
            if (game != null) DestroyImmediate(game);
            ((RectTransform)designer.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(250, 40));
            var dl = designer.GetComponentInChildren<Text>();
            if (dl != null) dl.rectTransform.Stretch(26, 0, 26, 0);
            LGPrimarySkin.Add(designer.gameObject, dl);   // главная кнопка окна — латунь
            TooltipHelper.Attach(designer.gameObject, Loc.T("<b>Конструктор кораблей</b>\nСоберите проект из модулей — верфь строит по последнему сохранённому проекту корпуса."));

            _retrofitAllBtn = LGBuild.Button(foot.transform, "RetrofitAllBtn", UIManager.DS.BtnSuccess, Gold, OnRetrofitAll, LGIcon.Wrench, Loc.T("ВСЕ УСТАРЕВШИЕ"), 12);
            ((RectTransform)_retrofitAllBtn.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-276, 0), new Vector2(200, 36));
            TooltipHelper.Attach(_retrofitAllBtn.gameObject, Loc.T("<b>Модернизировать все устаревшие корабли</b>\nКорабли строятся по основному проекту корпуса (последнему сохранённому в конструкторе). ") +
                                 Loc.T("Новый проект делает старые корабли устаревшими — модернизация перевооружает их за доплату сплавами. Если сплавов не хватит на всех, модернизируются первые по списку."));
            _retrofitBtn = LGBuild.Button(foot.transform, "RetrofitBtn", UIManager.DS.BtnSuccess, Gold, OnRetrofit, LGIcon.Wrench, Loc.T("ВЫДЕЛЕННЫЙ"), 12);
            ((RectTransform)_retrofitBtn.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-490, 0), new Vector2(160, 36));
            TooltipHelper.Attach(_retrofitBtn.gameObject, Loc.T("Модернизировать выделенный на карте военный корабль по актуальному проекту его корпуса"));

            _root.SetActive(false);
        }

        private static Text PurseChip(RectTransform parent, LGIcon icon, Color col, float x, string unit)
        {
            var c = LGBuild.Panel(parent, "Purse", new Color(0.039f, 0.067f, 0.125f, 0.95f));
            c.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(142, 30));
            LG.Platter(c.gameObject, 2f).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.22f));
            var ic = LGIcons.Create(c.transform, icon, 14, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(14, 14));
            var t = LGBuild.Label(c.transform, "", 13, Ink, TextAnchor.MiddleLeft, bold: true);
            t.font = GameFont.MonoMedium;
            t.rectTransform.Stretch(30, 0, 8, 0);
            var u = LGBuild.Label(c.transform, unit, 10, Muted, TextAnchor.MiddleRight);
            u.rectTransform.Stretch(30, 0, 10, 0);
            return t;
        }

        /// <summary>Карточка корабля: цветная черта назначения, силуэт на мягком свете, название, характеристики, цена и «Заложить».</summary>
        private void AddOption(RectTransform parent, FleetType type, ShipClass hull, string name, LGIcon icon, Color tint, int index)
        {
            const float n = 5f;
            var card = LGBuild.Panel(parent, "Ship_" + name, new Color(0.047f, 0.078f, 0.137f, 0.92f));
            var crt = card.rectTransform;
            crt.anchorMin = new Vector2(index / n, 0); crt.anchorMax = new Vector2((index + 1) / n, 1);
            crt.offsetMin = new Vector2(index == 0 ? 0 : 6, 0); crt.offsetMax = new Vector2(index == 4 ? 0 : -6, 0);
            LG.Platter(card.gameObject, 2f).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.18f));

            var accent = LGBuild.Panel(card.transform, "Accent", new Color(tint.r, tint.g, tint.b, 0.85f));
            accent.rectTransform.TopBand(0, 2, 14, 14);
            LG.Ignore(accent.gameObject);
            var glow = LGBuild.Panel(card.transform, "Glow", new Color(0.16f, 0.24f, 0.44f, 0.6f));
            glow.sprite = MenuArt.SoftDisc;
            glow.rectTransform.TopBand(4, 76, 16, 16);
            glow.raycastTarget = false;
            LG.Ignore(glow.gameObject);
            var ic = LGIcons.Create(card.transform, icon, 50, Color.Lerp(tint, Color.white, 0.3f));
            ic.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(50, 50));

            var o = new Option { Type = type, Hull = hull, Name = name, Icon = icon, Tint = tint };
            var nm = LGBuild.Label(card.transform, name, 16, Ink, TextAnchor.UpperLeft);
            nm.font = GameFont.Display;
            nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 11; nm.resizeTextMaxSize = 16;
            nm.horizontalOverflow = HorizontalWrapMode.Wrap;
            nm.rectTransform.TopBand(80, 22, 14, 10);
            o.Sub = LGBuild.Label(card.transform, "", 11, Muted, TextAnchor.UpperLeft);
            o.Sub.rectTransform.TopBand(102, 16, 14, 10);
            o.Stats = LGBuild.Label(card.transform, "", 10, Primary, TextAnchor.UpperLeft, wrap: true);
            o.Stats.resizeTextForBestFit = true; o.Stats.resizeTextMinSize = 8; o.Stats.resizeTextMaxSize = 10;
            o.Stats.rectTransform.TopBand(122, 44, 14, 10);

            o.Alloys = Chip(card.rectTransform, LGIcon.Alloys, new Color(0.475f, 0.816f, 0.749f), 14f, 170f);
            o.Energy = Chip(card.rectTransform, LGIcon.Energy, new Color(1f, 0.80f, 0.32f), 96f, 170f);
            o.Days = Chip(card.rectTransform, LGIcon.Clock, Muted, 14f, 188f);
            o.Upkeep = LGBuild.Label(card.transform, "", 10, new Color(1f, 0.55f, 0.55f), TextAnchor.UpperLeft);
            o.Upkeep.rectTransform.TopBand(208, 14, 14, 10);

            // камни кнопки: изумруд — наука, янтарь — стройка, жемчуг — боевые (коралл означал бы «опасное»)
            Color gem = type == FleetType.Military ? new Color(1f, 1f, 1f, 0.3f) : new Color(tint.r, tint.g, tint.b, 0.6f);
            o.Build = LGBuild.Button(card.transform, "Build", UIManager.DS.BtnPrimary, gem, () => TryBuild(o), null, Loc.T("ЗАЛОЖИТЬ"), 12);
            var bRt = (RectTransform)o.Build.transform;
            bRt.anchorMin = new Vector2(0, 0); bRt.anchorMax = new Vector2(1, 0); bRt.pivot = new Vector2(0.5f, 0);
            bRt.offsetMin = new Vector2(12, 12); bRt.offsetMax = new Vector2(-12, 46);
            o.BuildLabel = o.Build.GetComponentInChildren<Text>();
            _options.Add(o);
        }

        private static Text Chip(RectTransform parent, LGIcon icon, Color col, float x, float top)
        {
            var c = LGBuild.Rect(parent, "Chip");
            c.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -top), new Vector2(80, 16));
            var ic = LGIcons.Create(c, icon, 12, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(12, 12));
            var t = LGBuild.Label(c, "", 11, Primary, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch(16, 0, 0, 0);
            return t;
        }

        // ==================== ДАННЫЕ ====================

        private static string ShipName(FleetType type, ShipClass hull) =>
            type == FleetType.Science ? Loc.T("Научный корабль") : type == FleetType.Constructor ? Loc.T("Строительный корабль")
            : hull == ShipClass.Destroyer ? Loc.T("Эсминец") : hull == ShipClass.Frigate ? Loc.T("Фрегат") : Loc.T("Корвет");

        private static LGIcon ShipIcon(FleetType type, ShipClass hull) =>
            type == FleetType.Science ? LGIcon.ScienceShip : type == FleetType.Constructor ? LGIcon.ConstructorShip : ModuleVisuals.HullIcon(hull);

        private void Refresh()
        {
            var fm = FleetManager.Instance;
            var eco = EconomyManager.Instance;
            int yard = fm != null ? fm.YardFor(_yardRequest) : -1;
            var yardSys = EmpireStats.GetSystem(yard);
            _eyebrow.text = yardSys != null ? Loc.F("ВЕРФЬ  ·  {0}", yardSys.Name.ToUpper()) : Loc.T("ВЕРФЬ");
            _subtitle.text = yardSys != null
                ? Loc.F("Одновременно строится {0} корабля  ·  готовые корабли появляются у верфи", ConstructionManager.ShipyardSlots)
                : Loc.T("<color=#FF6B57>У вас нет систем для верфи</color>");
            if (eco != null)
            {
                _alloysText.text = $"{eco.Alloys:0}";
                _energyText.text = $"{eco.EnergyCredits:0}";
            }

            foreach (var o in _options) RefreshOption(o, fm, eco, yard >= 0);
            RefreshQueue();
            RefreshRetrofit();
        }

        private void RefreshOption(Option o, FleetManager fm, EconomyManager eco, bool hasYard)
        {
            float alloys, energy;
            string locked = null;
            ShipDesign design = null;
            if (o.Type == FleetType.Science)
            {
                alloys = FleetManager.ScienceShipAlloys; energy = FleetManager.ScienceShipEnergy;
                o.Sub.text = Loc.T("Разведка систем и аномалий");
                o.Stats.text = Loc.T("<color=#A9A392>Изучает системы, открывает их для форпостов. Учёный на борту ускоряет разведку.</color>");
            }
            else if (o.Type == FleetType.Constructor)
            {
                alloys = FleetManager.ConstructorAlloys; energy = FleetManager.ConstructorEnergy;
                o.Sub.text = Loc.T("Форпосты и добывающие станции");
                o.Stats.text = Loc.T("<color=#A9A392>Строит звёздные базы в изученных системах — так растёт территория.</color>");
            }
            else
            {
                design = fm != null ? fm.PlayerDesignFor(o.Hull) : null;
                alloys = design != null ? design.AlloyCost : 60f;
                energy = FleetManager.WarshipEnergy;
                o.Sub.text = design != null ? Loc.F("Проект «{0}»", design.Name) : Loc.T("Проект не задан");
                if (design != null)
                {
                    var b = EmpireBonuses.For(0);
                    float dmg = 0f;
                    foreach (var w in design.Mounts) dmg += w.Dps * b.DamageMult(w.Type);
                    o.Stats.text = Loc.F("урон <b>{0:0}</b>/день  ·  корпус {1:0}  ·  ", dmg * CombatManager.ShotsPerDay, design.Hull * b.HullMult) +
                                   Loc.F("броня {0:0}  ·  щиты {1:0}", design.Armor * b.ArmorMult, design.Shields * b.ShieldMult);
                }
                if (o.Hull == ShipClass.Destroyer && !(TechnologyManager.Instance?.DestroyerUnlocked ?? false))
                    locked = Loc.T("нужна технология верфей эсминцев");
            }
            alloys = FleetManager.ShipAlloyCost(alloys, 0);
            bool canAlloys = eco != null && eco.Alloys >= alloys;
            bool canEnergy = eco != null && eco.EnergyCredits >= energy;
            string red = LGBuild.Hex(UIManager.DS.Red);
            o.Alloys.text = canAlloys ? $"{alloys:0}" : $"<color={red}>{alloys:0}</color>";
            o.Energy.text = canEnergy ? $"{energy:0}" : $"<color={red}>{energy:0}</color>";
            o.Days.text = Loc.F("{0:0} дн.", ConstructionManager.ShipDays(o.Type, o.Hull));

            float upkeep = o.Type == FleetType.Military && eco != null
                ? EmpireEconomy.ExtraUpkeepForShip(eco.Report, o.Hull)
                : FleetData.UpkeepFor(o.Type, o.Hull);
            o.Upkeep.text = locked ?? Loc.F("содержание −{0:0.#} гелия-3/мес", upkeep);
            o.Upkeep.color = locked != null ? Muted : new Color(1f, 0.55f, 0.55f);

            bool can = locked == null && hasYard && canAlloys && canEnergy;
            o.Build.interactable = can;
            if (o.BuildLabel != null)
                o.BuildLabel.text = locked != null ? Loc.T("ЗАКРЫТО") : !hasYard ? Loc.T("НЕТ ВЕРФИ") : !can ? CostCheck.Short(energy, 0f, alloys, 0f) : Loc.T("ЗАЛОЖИТЬ");
            string why = locked != null ? Loc.F("Закрыто: {0}.", locked)
                       : !hasYard ? Loc.T("Нет системы, где можно разместить верфь.")
                       : !can ? CostCheck.Full(energy, 0f, alloys, 0f) + "."
                       : null;
            TooltipHelper.Attach(o.Build.gameObject, BuildTip(o, design, alloys, energy, upkeep, eco, why));
        }

        /// <summary>Подсказка кнопки «Заложить»: что даст корабль, во что обойдётся, как скажется на лимите флота.</summary>
        private static string BuildTip(Option o, ShipDesign design, float alloys, float energy, float upkeep, EconomyManager eco, string why)
        {
            const string M = "#A9A392", Bad = "#FF7F6B", Warn = "#FFB070";
            var sb = new System.Text.StringBuilder();
            sb.Append($"<b>{o.Name}</b>\n");
            if (o.Type == FleetType.Science)
                sb.Append(Loc.T("Изучает системы: без разведки нельзя строить форпосты и колонии. Находит аномалии и события. Учёный на борту ускоряет работу.\n"));
            else if (o.Type == FleetType.Constructor)
                sb.Append(Loc.T("Строит форпосты — каждый расширяет территорию ") +
                          Loc.F("(форпост: {0:0} сплавов и {1:0} влияния) — и добывающие комплексы на планетах.\n", FactionTraits.StarbaseAlloys(0), FleetManager.OutpostInfluenceCost(0)));
            else if (design != null)
            {
                float pw = ShipDesignManager.DesignPower(design);
                string cmp = CombatManager.PowerInShips(pw);
                sb.Append(Loc.F("Проект «{0}»: мощь ≈ {1:0}", design.Name, pw) + (cmp.Length > 0 ? $" ({cmp})" : "") + ".\n");
                if (eco != null)
                {
                    var r = eco.Report;
                    int size = EmpireEconomy.NavalSize(o.Hull);
                    int after = r.NavalUsed + size;
                    sb.Append(Loc.F("Лимит флота: {0} → <b>{1}</b> из {2}", r.NavalUsed, after, r.NavalCapacity));
                    sb.Append(after > r.NavalCapacity
                        ? Loc.F("  <color={0}>— сверх лимита: дороже содержание всех военных кораблей</color>\n", Warn)
                        : "\n");
                }
            }
            sb.Append(Loc.F("\n<color={0}>Цена:</color> {1:0} сплавов, {2:0} гелия-3 · {3:0} дн.\n", M, alloys, energy, ConstructionManager.ShipDays(o.Type, o.Hull)));
            sb.Append(Loc.F("<color={0}>Содержание:</color> −{1:0.#} гелия-3 в месяц", M, upkeep));
            if (why != null) sb.Append($"\n<color={Bad}>{why}</color>");
            return sb.ToString();
        }

        private void TryBuild(Option o)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            bool ok = o.Type == FleetType.Military && o.Hull != ShipClass.Corvette ? fm.BuildWarship(o.Hull, _yardRequest) : fm.BuildShip(o.Type, _yardRequest);
            if (ok) { _queueSig = ""; Refresh(); }
        }

        // ==================== СТАПЕЛИ ====================

        private void RefreshQueue()
        {
            var cm = ConstructionManager.Instance;
            var q = cm != null ? cm.ShipQueue(0) : new List<ConstructionJob>();
            int slots = ConstructionManager.ShipyardSlots;
            _queueTitle.text = q.Count == 0 ? Loc.F("СТАПЕЛИ  ·  свободно {0} из {1}", slots, slots)
                             : Loc.F("СТАПЕЛИ  ·  занято {0} из {1}", Mathf.Min(q.Count, slots), slots) + (q.Count > slots ? Loc.F("  ·  в очереди {0}", q.Count - slots) : "");

            string sig = slots + ":" + q.Count;
            foreach (var j in q) sig += "|" + j.Id;
            if (sig != _queueSig)
            {
                _queueSig = sig;
                RebuildQueue(cm, q);
                LG.Skin(_queue);
            }
            for (int i = 0; i < q.Count; i++)
            {
                if (!_slots.TryGetValue(q[i].Id, out var ui)) continue;
                float p = Mathf.Clamp01(q[i].Progress);
                // латунный силуэт проявляется слева направо, искра — на шве
                ui.Clip.padding = new Vector4(0f, 0f, (1f - p) * ui.Pic.rect.width, 0f);
                ui.Weld.anchorMin = new Vector2(p, 0.1f);
                ui.Weld.anchorMax = new Vector2(p, 0.9f);
                ui.Weld.gameObject.SetActive(p > 0.01f && p < 0.99f);
                ui.Pct.text = Loc.F("{0}%", Mathf.RoundToInt(p * 100f));
                ui.State.text = Loc.F("Стапель {0}  ·  готов через ~{1} дн.", Roman[Mathf.Min(ui.Index, Roman.Length - 1)], Mathf.CeilToInt(cm.DaysUntilDone(q[i])));
            }
        }

        /// <summary>Ряд доков: в занятом — корабль в лесах стапеля, в свободном — приглашение заложить.</summary>
        private void RebuildQueue(ConstructionManager cm, List<ConstructionJob> q)
        {
            LGBuild.Clear(_queue);
            _slots.Clear();
            int slots = Mathf.Clamp(ConstructionManager.ShipyardSlots, 1, 4);
            for (int i = 0; i < slots; i++)
            {
                bool busy = i < q.Count;
                var dock = LGBuild.Panel(_queue, busy ? "Dock" : "FreeDock", busy ? new Color(0.043f, 0.075f, 0.141f, 0.95f) : new Color(0.035f, 0.059f, 0.110f, 0.6f));
                var drt = dock.rectTransform;
                drt.anchorMin = new Vector2(i / (float)slots, 0); drt.anchorMax = new Vector2((i + 1) / (float)slots, 1);
                drt.offsetMin = new Vector2(i == 0 ? 0 : 7, 0); drt.offsetMax = new Vector2(i == slots - 1 ? 0 : -7, 0);
                LG.Platter(dock.gameObject, 2f).SetRim(new Color(Gold.r, Gold.g, Gold.b, busy ? 0.32f : 0.14f));
                if (!busy)
                {
                    var ft = LGBuild.Label(dock.transform, Loc.F("<b>Стапель {0} свободен</b>\nвыберите корабль ниже", Roman[i]), 12, Muted, TextAnchor.MiddleCenter, wrap: true);
                    ft.rectTransform.Stretch(12, 0, 12, 0);
                    continue;
                }
                BuildDock(drt, cm, q[i], i);
            }
        }

        private void BuildDock(RectTransform d, ConstructionManager cm, ConstructionJob job, int index)
        {
            // Леса стапеля: верхний и нижний рельсы, стойки крана
            for (int k = 0; k < 2; k++)
            {
                var rail = LGBuild.Panel(d, "Rail", new Color(Gold.r, Gold.g, Gold.b, 0.2f));
                rail.rectTransform.anchorMin = new Vector2(0, k); rail.rectTransform.anchorMax = new Vector2(1, k);
                rail.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rail.rectTransform.offsetMin = new Vector2(14, k == 0 ? 40 : -38);
                rail.rectTransform.offsetMax = new Vector2(-14, k == 0 ? 41 : -37);
                rail.raycastTarget = false;
                LG.Ignore(rail.gameObject);
            }
            for (int k = 1; k < 8; k++)
            {
                var post = LGBuild.Panel(d, "Post", new Color(Gold.r, Gold.g, Gold.b, 0.08f));
                post.rectTransform.anchorMin = new Vector2(k / 8f, 0); post.rectTransform.anchorMax = new Vector2(k / 8f, 1);
                post.rectTransform.offsetMin = new Vector2(-0.5f, 41); post.rectTransform.offsetMax = new Vector2(0.5f, -38);
                post.raycastTarget = false;
                LG.Ignore(post.gameObject);
            }

            // Силуэт корабля: пунктир-призрак и латунь, которая проявляется по ходу работ
            var icon = ShipIcon(job.ShipType, job.Hull);
            var pic = LGBuild.Rect(d, "Pic");
            pic.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(84, 84));
            var ghost = LGIcons.Create(pic, icon, 84, new Color(Gold.r, Gold.g, Gold.b, 0.16f));
            ghost.rectTransform.Stretch();
            var clip = LGBuild.Rect(pic, "Clip");
            clip.Stretch();
            var mask = clip.gameObject.AddComponent<RectMask2D>();
            var lit = LGIcons.Create(clip, icon, 84, Lit);
            lit.rectTransform.Stretch();
            var weld = LGBuild.Panel(pic, "Weld", new Color(1f, 0.95f, 0.80f, 0.95f));
            weld.rectTransform.anchorMin = new Vector2(0, 0.1f); weld.rectTransform.anchorMax = new Vector2(0, 0.9f);
            weld.rectTransform.sizeDelta = new Vector2(2f, 0f);
            weld.raycastTarget = false;
            LG.Ignore(weld.gameObject);
            var spark = LGBuild.Panel(weld.transform, "Spark", new Color(1f, 0.85f, 0.55f, 0.7f));
            spark.sprite = MenuArt.SoftDisc;
            spark.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30));
            spark.raycastTarget = false;
            LG.Ignore(spark.gameObject);

            var nm = LGBuild.Label(d, ShipName(job.ShipType, job.Hull), 15, Ink, TextAnchor.UpperLeft);
            nm.font = GameFont.Display;
            nm.rectTransform.TopBand(10, 22, 14, 80);
            var pct = LGBuild.Label(d, "", 26, Lit, TextAnchor.UpperRight);
            pct.font = GameFont.Display;
            pct.rectTransform.TopBand(4, 34, 120, 14);
            var state = LGBuild.Label(d, "", 11, Muted, TextAnchor.MiddleLeft);
            var srt = state.rectTransform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0); srt.pivot = new Vector2(0.5f, 0);
            srt.offsetMin = new Vector2(14, 8); srt.offsetMax = new Vector2(-46, 30);

            var j = job;
            var cancel = LGBuild.Button(d, "Cancel", new Color(0.24f, 0.08f, 0.1f), new Color(1f, 0.4f, 0.42f, 0.5f),
                () => { cm.Cancel(j); _queueSig = ""; Refresh(); }, LGIcon.Close, null, 8);
            ((RectTransform)cancel.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-10, 7), new Vector2(26, 26));
            TooltipHelper.Attach(cancel.gameObject, Loc.T("<b>Отменить</b>\nРесурсы вернутся полностью."));

            _slots[job.Id] = new SlotUi { Pic = pic, Clip = mask, Weld = weld.rectTransform, Pct = pct, State = state, Index = index };
        }

        // ==================== МОДЕРНИЗАЦИЯ ====================

        private void RefreshRetrofit()
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null) { _retrofitBtn.interactable = _retrofitAllBtn.interactable = false; return; }

            var outdated = dm.OutdatedShips(0);
            float total = 0f;
            foreach (var f in outdated) total += dm.RetrofitCost(f);
            float alloys = EconomyManager.Instance != null ? EconomyManager.Instance.Alloys : 0f;
            _retrofitAllBtn.interactable = outdated.Count > 0 && alloys >= dm.RetrofitCost(outdated[0]);
            var allLabel = _retrofitAllBtn.GetComponentInChildren<Text>();
            if (allLabel != null) allLabel.text = outdated.Count > 0 ? Loc.F("ВСЕ УСТАРЕВШИЕ ({0})", outdated.Count) : Loc.T("ВСЕ УСТАРЕВШИЕ");

            string all = outdated.Count == 0
                ? Loc.T("<color=#8CD3A0>Все военные корабли соответствуют актуальным проектам.</color>")
                : Loc.F("Устаревших кораблей: <color=#E7C170>{0}</color> · на всех нужно <color=#E7C170>{1:0} сплавов</color>.", outdated.Count, total);

            var fleet = FleetManager.Instance?.SelectedFleet;
            var d = fleet?.Data;
            if (d == null || d.OwnerId != 0 || d.Type != FleetType.Military)
            {
                _retrofitBtn.interactable = false;
                _retrofitText.text = all + Loc.T("\n<color=#A9A392>Новый проект в конструкторе делает старые корабли устаревшими; выделите корабль на карте, чтобы модернизировать только его.</color>");
                return;
            }
            if (!dm.IsOutdated(d))
            {
                _retrofitBtn.interactable = false;
                _retrofitText.text = Loc.F("{0}: <color=#8CD3A0>актуальный проект.</color>\n", d.Name) + all;
                return;
            }
            float cost = dm.RetrofitCost(d);
            float gain = dm.RetrofitGain(d);
            bool can = dm.CanRetrofit(d);
            _retrofitBtn.interactable = can;
            string g = gain >= 0f ? Loc.F("<color=#8CD3A0>+{0:0}% мощи</color>", gain * 100f) : Loc.F("<color=#FF9F7A>{0:0}% мощи</color>", gain * 100f);
            _retrofitText.text = $"{d.Name} → «{dm.GetLatestDesign(d.HullClass)?.Name}»  ·  {g}  ·  " +
                                 (can ? Loc.F("<color=#E7C170>{0:0} сплавов</color>", cost) : Loc.F("<color=#FF6B57>нужно {0:0} сплавов</color>", cost)) + "\n" + all;
        }

        private void OnRetrofit()
        {
            if (FleetManager.Instance != null && FleetManager.Instance.TryRetrofitSelectedFleet()) Refresh();
        }

        private void OnRetrofitAll()
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null) return;
            int n = dm.TryRetrofitAll();
            if (n > 0) NotificationCenter.Show(Loc.T("Модернизация"), Loc.F("Перевооружено кораблей: {0}", n), NotificationCenter.Kind.Success, 5f);
            Refresh();
        }
    }
}
