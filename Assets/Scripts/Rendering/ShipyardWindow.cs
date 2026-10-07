using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Орбитальная верфь. Слева — что можно заложить: карточка на каждый тип корабля с иконкой,
    /// ролью, боевым профилем проекта, ценой (сплавы, гелий-3, дни) и содержанием; кнопка «Заложить»
    /// с причиной, если нельзя. Справа — стапели: что строится, прогресс, сроки, отмена с возвратом,
    /// а ниже — модернизация выбранной эскадры и переход в конструктор.
    /// </summary>
    public class ShipyardWindow : MonoBehaviour
    {
        public static ShipyardWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private const float Width = 900f, Height = 610f, HeaderH = 60f;
        private const float LeftW = 548f;

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

        private class SlotUi
        {
            public RectTransform Bar;
            public Text State;
        }

        private GameObject _root;
        private Text _subtitle;
        private RectTransform _queue;
        private Text _queueTitle;
        private Text _retrofitText;
        private Button _retrofitBtn;
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

        public void Open()
        {
            if (_root == null) return;
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
            LG.Glass(_root, 24f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.42f));
            LG.Motion(_root, LGAppear.Kind.Pop);

            // Шапка
            var header = LGBuild.Panel(rt, "Header", UIManager.DS.BgHeader);
            header.rectTransform.TopBand(0, HeaderH);
            LG.Header(header.gameObject);
            var badge = LGBuild.Panel(header.transform, "Badge", new Color(0.06f, 0.26f, 0.28f));
            badge.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(38, 38));
            LG.Platter(badge.gameObject, 19f).FillMultiplier = 2.2f;
            LGIcons.Create(badge.transform, LGIcon.Shipyard, 22, Neon);
            var title = LGBuild.Label(header.transform, "ОРБИТАЛЬНАЯ ВЕРФЬ", 16, Neon, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.Stretch(66, 0, 60, 11);
            _subtitle = LGBuild.Label(header.transform, "", 11, Muted, TextAnchor.LowerLeft);
            _subtitle.rectTransform.Stretch(66, 10, 60, 0);
            var close = LGBuild.Button(header.transform, "Close", new Color(0.16f, 0.20f, 0.24f), new Color(1f, 0.45f, 0.48f, 0.55f),
                Close, LGIcon.Close, null, 11);
            ((RectTransform)close.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(32, 32));

            // Левая колонка — заказ кораблей
            var left = LGBuild.Rect(rt, "Order");
            left.anchorMin = new Vector2(0, 0); left.anchorMax = new Vector2(0, 1); left.pivot = new Vector2(0, 0.5f);
            left.offsetMin = new Vector2(16, 16); left.offsetMax = new Vector2(16 + LeftW, -(HeaderH + 10));
            Section(left, "ЗАЛОЖИТЬ КОРАБЛЬ", LGIcon.Construction, 0f);

            _options.Clear();
            AddOption(left, FleetType.Science, ShipClass.Corvette, "Научный корабль", LGIcon.Sensors, new Color(0.45f, 1f, 0.62f), 0);
            AddOption(left, FleetType.Constructor, ShipClass.Corvette, "Строительный корабль", LGIcon.Construction, new Color(1f, 0.82f, 0.36f), 1);
            AddOption(left, FleetType.Military, ShipClass.Corvette, "Корвет", LGIcon.Corvette, Neon, 2);
            AddOption(left, FleetType.Military, ShipClass.Frigate, "Фрегат", LGIcon.Frigate, Neon, 3);
            AddOption(left, FleetType.Military, ShipClass.Destroyer, "Эсминец", LGIcon.Destroyer, Neon, 4);

            // Правая колонка — стапели и модернизация
            var right = LGBuild.Rect(rt, "Docks");
            right.anchorMin = new Vector2(0, 0); right.anchorMax = new Vector2(1, 1);
            right.offsetMin = new Vector2(16 + LeftW + 16, 16); right.offsetMax = new Vector2(-16, -(HeaderH + 10));

            var qHead = LGBuild.Rect(right, "QueueHead");
            qHead.TopBand(0, 24);
            var qi = LGIcons.Create(qHead, LGIcon.Clock, 14, Gold);
            qi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(14, 14));
            _queueTitle = LGBuild.Label(qHead, "", 11, Gold, TextAnchor.MiddleLeft, bold: true);
            _queueTitle.rectTransform.Stretch(22, 0, 0, 0);

            _queue = LGBuild.Rect(right, "Queue");
            _queue.anchorMin = new Vector2(0, 1); _queue.anchorMax = new Vector2(1, 1); _queue.pivot = new Vector2(0.5f, 1);
            _queue.offsetMin = new Vector2(0, -330); _queue.offsetMax = new Vector2(0, -30);

            // Модернизация
            var refit = LGBuild.Panel(right, "Retrofit", UIManager.DS.BgSlot);
            refit.rectTransform.anchorMin = new Vector2(0, 0); refit.rectTransform.anchorMax = new Vector2(1, 0);
            refit.rectTransform.pivot = new Vector2(0.5f, 0);
            refit.rectTransform.offsetMin = new Vector2(0, 50); refit.rectTransform.offsetMax = new Vector2(0, 160);
            LG.Platter(refit.gameObject, 14f).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.25f));
            var rh = LGBuild.Rect(refit.transform, "Head");
            rh.TopBand(8, 18, 12, 12);
            var ri = LGIcons.Create(rh, LGIcon.Wrench, 13, Gold);
            ri.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(13, 13));
            var rt2 = LGBuild.Label(rh, "МОДЕРНИЗАЦИЯ ЭСКАДРЫ", 10, Gold, TextAnchor.MiddleLeft, bold: true);
            rt2.rectTransform.Stretch(20, 0, 0, 0);
            _retrofitText = LGBuild.Label(refit.transform, "", 10, Primary, TextAnchor.UpperLeft, wrap: true);
            _retrofitText.rectTransform.Stretch(12, 44, 12, 30);
            _retrofitBtn = LGBuild.Button(refit.transform, "RetrofitBtn", UIManager.DS.BtnSuccess, Gold, OnRetrofit, LGIcon.Wrench, "МОДЕРНИЗИРОВАТЬ", 10);
            var rbRt = (RectTransform)_retrofitBtn.transform;
            rbRt.anchorMin = new Vector2(0, 0); rbRt.anchorMax = new Vector2(1, 0); rbRt.pivot = new Vector2(0.5f, 0);
            rbRt.offsetMin = new Vector2(10, 8); rbRt.offsetMax = new Vector2(-10, 38);

            var designer = LGBuild.Button(right, "Designer", UIManager.DS.BtnPrimary, UIManager.DS.BtnPrimaryHi, () =>
            {
                Close();
                ShipDesignerModal.Instance?.Open();
            }, LGIcon.Shipyard, "КОНСТРУКТОР КОРАБЛЕЙ", 11);
            var dRt = (RectTransform)designer.transform;
            dRt.anchorMin = new Vector2(0, 0); dRt.anchorMax = new Vector2(1, 0); dRt.pivot = new Vector2(0.5f, 0);
            dRt.offsetMin = new Vector2(0, 0); dRt.offsetMax = new Vector2(0, 40);
            TooltipHelper.Attach(designer.gameObject, "<b>Конструктор кораблей</b>\nСоберите проект из модулей — верфь строит по последнему сохранённому проекту корпуса.");

            _root.SetActive(false);
        }

        private static void Section(RectTransform parent, string text, LGIcon icon, float top)
        {
            var head = LGBuild.Rect(parent, "Section");
            head.TopBand(top, 24);
            var ic = LGIcons.Create(head, icon, 14, Gold);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(14, 14));
            var t = LGBuild.Label(head, text, 11, Gold, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch(22, 0, 0, 0);
        }

        private void AddOption(RectTransform parent, FleetType type, ShipClass hull, string name, LGIcon icon, Color tint, int index)
        {
            const float rowH = 88f, gap = 8f;
            var card = LGBuild.Panel(parent, "Ship_" + name, UIManager.DS.BgSlot);
            card.rectTransform.TopBand(30 + index * (rowH + gap), rowH);
            LG.Platter(card.gameObject, 14f).SetRim(new Color(tint.r, tint.g, tint.b, 0.22f));

            var badge = LGBuild.Panel(card.transform, "Badge", new Color(tint.r * 0.16f, tint.g * 0.16f, tint.b * 0.16f, 1f));
            badge.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(56, 56));
            LG.Platter(badge.gameObject, 14f).SetRim(new Color(tint.r, tint.g, tint.b, 0.45f));
            LGIcons.Create(badge.transform, icon, 32, tint);

            var o = new Option { Type = type, Hull = hull, Name = name, Icon = icon, Tint = tint };
            var nm = LGBuild.Label(card.transform, name.ToUpper(), 13, Primary, TextAnchor.UpperLeft, bold: true);
            nm.rectTransform.Stretch(80, 0, 200, 10);
            o.Sub = LGBuild.Label(card.transform, "", 10, Muted, TextAnchor.UpperLeft);
            o.Sub.rectTransform.Stretch(80, 0, 200, 30);
            o.Stats = LGBuild.Label(card.transform, "", 10, Primary, TextAnchor.UpperLeft, wrap: true);
            o.Stats.rectTransform.Stretch(80, 6, 200, 48);

            // Цена: сплавы, гелий-3, дни — чипами с иконками
            var price = LGBuild.Rect(card.transform, "Price");
            price.anchorMin = new Vector2(1, 1); price.anchorMax = new Vector2(1, 1); price.pivot = new Vector2(1, 1);
            price.sizeDelta = new Vector2(186, 18); price.anchoredPosition = new Vector2(-12, -10);
            o.Alloys = Chip(price, LGIcon.Alloys, new Color(0.13f, 0.92f, 0.69f), 0f);
            o.Energy = Chip(price, LGIcon.Energy, new Color(1f, 0.80f, 0.32f), 62f);
            o.Days = Chip(price, LGIcon.Clock, Muted, 124f);
            o.Upkeep = LGBuild.Label(card.transform, "", 10, new Color(1f, 0.55f, 0.55f), TextAnchor.UpperRight);
            o.Upkeep.rectTransform.Stretch(0, 0, 12, 32);

            o.Build = LGBuild.Button(card.transform, "Build", UIManager.DS.BtnPrimary, new Color(tint.r, tint.g, tint.b, 0.6f), () => TryBuild(o), null, "ЗАЛОЖИТЬ", 10);
            var bRt = (RectTransform)o.Build.transform;
            bRt.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-12, 10), new Vector2(186, 28));
            o.BuildLabel = o.Build.GetComponentInChildren<Text>();
            _options.Add(o);
        }

        private static Text Chip(RectTransform parent, LGIcon icon, Color col, float x)
        {
            var c = LGBuild.Rect(parent, "Chip");
            c.anchorMin = new Vector2(0, 0); c.anchorMax = new Vector2(0, 1); c.pivot = new Vector2(0, 0.5f);
            c.sizeDelta = new Vector2(60, 0); c.anchoredPosition = new Vector2(x, 0);
            var ic = LGIcons.Create(c, icon, 12, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(12, 12));
            var t = LGBuild.Label(c, "", 11, Primary, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch(16, 0, 0, 0);
            return t;
        }

        // ==================== ДАННЫЕ ====================

        private static string ShipName(FleetType type, ShipClass hull) =>
            type == FleetType.Science ? "Научный корабль" : type == FleetType.Constructor ? "Строительный корабль"
            : hull == ShipClass.Destroyer ? "Эсминец" : hull == ShipClass.Frigate ? "Фрегат" : "Корвет";

        private static LGIcon ShipIcon(FleetType type, ShipClass hull) =>
            type == FleetType.Science ? LGIcon.Sensors : type == FleetType.Constructor ? LGIcon.Construction : ModuleVisuals.HullIcon(hull);

        private void Refresh()
        {
            var fm = FleetManager.Instance;
            var eco = EconomyManager.Instance;
            int yard = fm != null ? fm.PlayerShipyardSystem() : -1;
            var yardSys = EmpireStats.GetSystem(yard);
            _subtitle.text = yardSys != null
                ? $"Верфь в системе {yardSys.Name}  ·  одновременно строится {ConstructionManager.ShipyardSlots} корабля  ·  готовые корабли появляются у верфи"
                : "<color=#FF6A6A>У вас нет систем для верфи</color>";

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
                o.Sub.text = "Разведка систем и аномалий";
                o.Stats.text = "<color=#8AA2A8>Изучает системы, открывает их для форпостов. Учёный на борту ускоряет разведку.</color>";
            }
            else if (o.Type == FleetType.Constructor)
            {
                alloys = FleetManager.ConstructorAlloys; energy = FleetManager.ConstructorEnergy;
                o.Sub.text = "Форпосты и добывающие станции";
                o.Stats.text = "<color=#8AA2A8>Строит звёздные базы в изученных системах — так растёт территория.</color>";
            }
            else
            {
                design = fm != null ? fm.PlayerDesignFor(o.Hull) : null;
                alloys = design != null ? design.AlloyCost : 60f;
                energy = FleetManager.WarshipEnergy;
                o.Sub.text = design != null ? $"Проект «{design.Name}»" : "Проект не задан";
                if (design != null)
                {
                    var b = EmpireBonuses.For(0);
                    float dmg = 0f;
                    foreach (var w in design.Mounts) dmg += w.Dps * b.DamageMult(w.Type);
                    o.Stats.text = $"урон <b>{dmg * CombatManager.ShotsPerDay:0}</b>/день  ·  корпус {design.Hull * b.HullMult:0}  ·  " +
                                   $"броня {design.Armor * b.ArmorMult:0}  ·  щиты {design.Shields * b.ShieldMult:0}";
                }
                if (o.Hull == ShipClass.Destroyer && !(TechnologyManager.Instance?.DestroyerUnlocked ?? false))
                    locked = "нужна технология верфей эсминцев";
            }
            alloys = FleetManager.ShipAlloyCost(alloys, 0);
            bool canAlloys = eco != null && eco.Alloys >= alloys;
            bool canEnergy = eco != null && eco.EnergyCredits >= energy;
            string red = LGBuild.Hex(UIManager.DS.Red);
            o.Alloys.text = canAlloys ? $"{alloys:0}" : $"<color={red}>{alloys:0}</color>";
            o.Energy.text = canEnergy ? $"{energy:0}" : $"<color={red}>{energy:0}</color>";
            o.Days.text = $"{ConstructionManager.ShipDays(o.Type, o.Hull):0} дн.";

            float upkeep = o.Type == FleetType.Military && eco != null
                ? EmpireEconomy.ExtraUpkeepForShip(eco.Report, o.Hull)
                : FleetData.UpkeepFor(o.Type, o.Hull);
            o.Upkeep.text = locked ?? $"содержание −{upkeep:0.#} гелия-3/мес";
            o.Upkeep.color = locked != null ? Muted : new Color(1f, 0.55f, 0.55f);

            bool can = locked == null && hasYard && canAlloys && canEnergy;
            o.Build.interactable = can;
            if (o.BuildLabel != null)
                o.BuildLabel.text = locked != null ? "ЗАКРЫТО" : !hasYard ? "НЕТ ВЕРФИ" : !can ? "НЕ ХВАТАЕТ РЕСУРСОВ" : "ЗАЛОЖИТЬ";
        }

        private void TryBuild(Option o)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            bool ok = o.Type == FleetType.Military && o.Hull != ShipClass.Corvette ? fm.BuildWarship(o.Hull) : fm.BuildShip(o.Type);
            if (ok) { _queueSig = ""; Refresh(); }
        }

        // ==================== СТАПЕЛИ ====================

        private void RefreshQueue()
        {
            var cm = ConstructionManager.Instance;
            var q = cm != null ? cm.ShipQueue(0) : new List<ConstructionJob>();
            int slots = ConstructionManager.ShipyardSlots;
            _queueTitle.text = q.Count == 0 ? $"СТАПЕЛИ  ·  свободно {slots} из {slots}"
                             : $"СТАПЕЛИ  ·  занято {Mathf.Min(q.Count, slots)} из {slots}" + (q.Count > slots ? $"  ·  в очереди {q.Count - slots}" : "");

            string sig = q.Count.ToString();
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
                bool active = i < slots;
                LGBuild.SetBar(ui.Bar, active ? q[i].Progress : 0f);
                ui.State.text = active
                    ? $"<b>{Mathf.RoundToInt(q[i].Progress * 100f)}%</b>  ·  готов через ~{Mathf.CeilToInt(cm.DaysUntilDone(q[i]))} дн."
                    : $"<color=#8AA2A8>в очереди  ·  готов через ~{Mathf.CeilToInt(cm.DaysUntilDone(q[i]))} дн.</color>";
            }
        }

        private void RebuildQueue(ConstructionManager cm, List<ConstructionJob> q)
        {
            LGBuild.Clear(_queue);
            _slots.Clear();
            const float rowH = 54f, gap = 6f;
            int shown = Mathf.Min(q.Count, 4);
            int rows = Mathf.Max(shown, ConstructionManager.ShipyardSlots);
            for (int i = 0; i < rows; i++)
            {
                float top = i * (rowH + gap);
                if (i >= shown)
                {
                    var empty = LGBuild.Panel(_queue, "FreeSlot", new Color(0.03f, 0.07f, 0.09f, 0.7f));
                    empty.rectTransform.TopBand(top, rowH);
                    LG.Platter(empty.gameObject, 12f).SetRim(new Color(Neon.r, Neon.g, Neon.b, 0.12f));
                    var et = LGBuild.Label(empty.transform, "свободный стапель", 10, new Color(Muted.r, Muted.g, Muted.b, 0.7f), TextAnchor.MiddleCenter);
                    et.fontStyle = FontStyle.Italic;
                    continue;
                }

                var job = q[i];
                bool active = i < ConstructionManager.ShipyardSlots;
                var row = LGBuild.Panel(_queue, "Job", active ? new Color(0.05f, 0.14f, 0.16f, 0.95f) : new Color(0.04f, 0.08f, 0.1f, 0.85f));
                row.rectTransform.TopBand(top, rowH);
                LG.Platter(row.gameObject, 12f).SetRim(new Color(Neon.r, Neon.g, Neon.b, active ? 0.35f : 0.15f));
                var ic = LGIcons.Create(row.transform, ShipIcon(job.ShipType, job.Hull), 22, active ? Neon : Muted);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(22, 22));
                var nm = LGBuild.Label(row.transform, ShipName(job.ShipType, job.Hull), 11, Primary, TextAnchor.UpperLeft, bold: true);
                nm.rectTransform.Stretch(44, 0, 40, 7);
                var state = LGBuild.Label(row.transform, "", 10, Primary, TextAnchor.UpperLeft);
                state.rectTransform.Stretch(44, 0, 40, 24);
                var barHost = LGBuild.Rect(row.transform, "Bar");
                barHost.anchorMin = new Vector2(0, 0); barHost.anchorMax = new Vector2(1, 0);
                barHost.offsetMin = new Vector2(44, 6); barHost.offsetMax = new Vector2(-40, 12);
                var bar = LGBuild.Bar(barHost, Neon, active ? job.Progress : 0f, 6f);

                var j = job;
                var cancel = LGBuild.Button(row.transform, "Cancel", new Color(0.24f, 0.08f, 0.1f), new Color(1f, 0.4f, 0.42f, 0.5f),
                    () => { cm.Cancel(j); _queueSig = ""; Refresh(); }, LGIcon.Close, null, 8);
                ((RectTransform)cancel.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(26, 26));
                TooltipHelper.Attach(cancel.gameObject, "<b>Отменить</b>\nРесурсы вернутся полностью.");

                _slots[job.Id] = new SlotUi { Bar = bar, State = state };
            }
            if (q.Count > shown)
            {
                var more = LGBuild.Label(_queue, $"… и ещё {q.Count - shown} в очереди", 10, Muted, TextAnchor.UpperLeft);
                more.rectTransform.TopBand(rows * (rowH + gap), 18, 4, 4);
            }
        }

        // ==================== МОДЕРНИЗАЦИЯ ====================

        private void RefreshRetrofit()
        {
            var fleet = FleetManager.Instance?.SelectedFleet;
            var dm = ShipDesignManager.Instance;
            if (fleet?.Data == null || fleet.Data.OwnerId != 0 || fleet.Data.Type != FleetType.Military || dm == null)
            {
                _retrofitBtn.interactable = false;
                _retrofitText.text = "<color=#8AA2A8>Выделите военный корабль старого проекта — его можно перевооружить по последнему проекту корпуса.</color>";
                return;
            }
            float cost = dm.RetrofitCost(fleet.Data);
            if (cost <= 0.01f)
            {
                _retrofitBtn.interactable = false;
                _retrofitText.text = $"{fleet.Data.Name}: <color=#5CF59A>уже соответствует актуальному проекту.</color>";
                return;
            }
            bool can = dm.CanRetrofit(fleet.Data);
            _retrofitBtn.interactable = can;
            _retrofitText.text = $"{fleet.Data.Name} → проект «{dm.GetLatestDesign(fleet.Data.HullClass)?.Name}»  ·  " +
                                 (can ? $"<color=#F2C747>{cost:0} сплавов</color>" : $"<color=#FF6A6A>нужно {cost:0} сплавов</color>");
        }

        private void OnRetrofit()
        {
            if (FleetManager.Instance != null && FleetManager.Instance.TryRetrofitSelectedFleet()) Refresh();
        }
    }
}
