using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Cards
{
    /// <summary>Экземпляр карты в империи: где лежит (флот или колония), ветеранство, раны.</summary>
    [Serializable]
    public class CardInstance
    {
        public long Id;
        public string Def;
        public int Owner;
        /// <summary>Флот, на борту которого карта (−1 — на колонии).</summary>
        public int Fleet = -1;
        /// <summary>Система-колония, где карта стоит (если не во флоте).</summary>
        public int System = -1;
        public int Stars;
        /// <summary>Дней до излечения (раненая карта не грузится во флот и не идёт в бой).</summary>
        public int WoundDays;
        /// <summary>Гарнизон колонии: защищает её и не покидает.</summary>
        public bool Garrison;

        [NonSerialized] private CardDef _def;
        public CardDef D { get { if (_def == null) _def = CardCatalog.Get(Def); return _def; } }
        public bool Wounded => WoundDays > 0;
    }

    [Serializable]
    public class ArmySave
    {
        public List<CardInstance> Cards = new List<CardInstance>();
        public long NextId = 1;
        public List<int> NoAutoLoad = new List<int>();
    }

    /// <summary>
    /// Армия империй — карты не берутся из воздуха. Каждая карта произведена на колонии (пехота — в городе, техника — на заводе,
    /// приказы — в штабе за влияние, оборона — в гарнизоне), стоит гелий-3 в месяц и физически лежит на колонии или на борту
    /// флота (десантные места: корвет 1, фрегат 2, эсминец 4). На штурм идёт то, что флоты привезли к цели; погибшее в бою
    /// пропадает, выжившее получает звезду ветерана. Флот погиб — погиб и десант на борту; колонию захватили — её гарнизон
    /// потерян. Правила одинаковы для игрока и ИИ.
    /// </summary>
    public class ArmyManager : MonoBehaviour
    {
        public static ArmyManager Instance { get; private set; }
        /// <summary>Резерв изменился (произведено, погрузка, потери) — для окон.</summary>
        public static event Action OnChanged;

        public const int WoundDaysAfterBattle = 30;
        public const int MaxDeck = 20, MinDeck = 8;

        private readonly List<CardInstance> _cards = new List<CardInstance>();
        private readonly HashSet<int> _noAutoLoad = new HashSet<int>();
        private long _nextId = 1;
        private bool _subscribed;

        public IReadOnlyList<CardInstance> All => _cards;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            AssaultLink.ResetForNewGame();
            BattleScreen.Ensure();
        }

        private void Start() => Subscribe();

        private void Subscribe()
        {
            if (_subscribed || TimeManager.Instance == null) return;
            _subscribed = true;
            TimeManager.Instance.OnDayPassed += HandleDay;
            SiegeManager.OnSystemCaptured += HandleCaptured;
            TechnologyManager.OnTechCompleted += HandleTech;
        }

        /// <summary>Изучена технология — сообщить, какие карты она открыла.</summary>
        private void HandleTech(Technology t)
        {
            if (t == null) return;
            var names = new List<string>();
            string mine = FactionTraits.Key(0);
            foreach (var d in CardCatalog.ForFaction(mine)) if (CardCatalog.TechOf(d) == t.Id) names.Add(d.Name);
            if (names.Count == 0) return;
            NotificationCenter.Show(Loc.T("Новые карты в Арсенале"), string.Join(", ", names), NotificationCenter.Kind.Success, 5f);
            Changed();
        }

        private void OnDestroy()
        {
            if (_subscribed && TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
            SiegeManager.OnSystemCaptured -= HandleCaptured;
            TechnologyManager.OnTechCompleted -= HandleTech;
            if (Instance == this) Instance = null;
        }

        private static void Changed() => OnChanged?.Invoke();

        // ==================== ЗАПРОСЫ ====================

        public List<CardInstance> Of(int owner)
        {
            var l = new List<CardInstance>();
            foreach (var c in _cards) if (c.Owner == owner) l.Add(c);
            return l;
        }

        public List<CardInstance> OnFleet(int fleetId)
        {
            var l = new List<CardInstance>();
            foreach (var c in _cards) if (c.Fleet == fleetId) l.Add(c);
            return l;
        }

        /// <summary>Карты на колонии системы (не во флоте): garrison — только гарнизон, false — только резерв, null — всё.</summary>
        public List<CardInstance> AtSystem(int owner, int systemId, bool? garrison = null)
        {
            var l = new List<CardInstance>();
            foreach (var c in _cards)
                if (c.Owner == owner && c.Fleet < 0 && c.System == systemId && (garrison == null || c.Garrison == garrison.Value)) l.Add(c);
            return l;
        }

        public CardInstance Get(long id)
        {
            foreach (var c in _cards) if (c.Id == id) return c;
            return null;
        }

        /// <summary>Десантных мест у корабля: корвет 1, фрегат 2, эсминец 4 (только военные).</summary>
        public static int Capacity(FleetData d)
        {
            if (d == null || d.Destroyed || d.Type != FleetType.Military) return 0;
            return d.HullClass == ShipClass.Destroyer ? 4 : d.HullClass == ShipClass.Frigate ? 2 : 1;
        }

        public int Used(int fleetId)
        {
            int n = 0;
            foreach (var c in _cards) if (c.Fleet == fleetId && c.D != null) n += c.D.Size;
            return n;
        }

        /// <summary>Содержание резерва (гелий-3 в месяц).</summary>
        public float Upkeep(int owner)
        {
            float s = 0f;
            foreach (var c in _cards) if (c.Owner == owner && c.D != null) s += c.D.Upkeep;
            return s;
        }

        public static float UpkeepOf(int owner) => Instance != null ? Instance.Upkeep(owner) : 0f;

        public int CountAt(int owner, int systemId, Predicate<CardDef> match)
        {
            int n = 0;
            foreach (var c in _cards)
                if (c.Owner == owner && c.Fleet < 0 && c.System == systemId && c.D != null && match(c.D)) n++;
            // И то, что уже строится там
            var cm = ConstructionManager.Instance;
            if (cm != null)
                foreach (var j in cm.Jobs)
                    if (j != null && j.Kind == JobKind.Troop && j.Owner == owner && j.SystemId == systemId)
                    { var d = CardCatalog.Get(j.DesignId); if (d != null && match(d)) n++; }
            return n;
        }

        // ==================== ПРОИЗВОДСТВО ====================

        public static int SystemPopulation(StarSystem sys)
        {
            int p = 0;
            if (sys != null) foreach (var pl in sys.Planets) p += Mathf.Max(0, pl.Population);
            return p;
        }

        public static int Districts(StarSystem sys, DistrictType t)
        {
            int n = 0;
            if (sys == null) return 0;
            foreach (var pl in sys.Planets)
                if (pl.Population > 0) foreach (var d in pl.Districts) if (d.Type == t) n++;
            return n;
        }

        public static int CapitalOf(int owner)
        {
            if (owner == 0) return EconomyManager.PlayerCapitalId;
            var ai = AIEmpireManager.For(owner);
            return ai != null ? ai.CapitalSystemId : -1;
        }

        /// <summary>Доступна ли карта фракции империи.</summary>
        public static bool Available(int owner, CardDef d)
            => d != null && !d.Virtual && (d.Faction == null || d.Faction == FactionTraits.Key(owner));

        /// <summary>Изучена ли технология у империи (игрок — дерево технологий, ИИ — свой список).</summary>
        public static bool HasTech(int owner, string techId)
        {
            if (string.IsNullOrEmpty(techId)) return true;
            if (owner == 0)
            {
                var t = TechnologyManager.Instance != null ? TechnologyManager.Instance.FindTech(techId) : null;
                return t == null || t.IsResearched;   // нет такой технологии в дереве — не блокируем
            }
            var ai = AIEmpireManager.For(owner);
            return ai == null || ai.HasTech(techId);
        }

        /// <summary>Открыта ли карта технологией.</summary>
        public static bool Unlocked(int owner, CardDef d) => HasTech(owner, CardCatalog.TechOf(d));

        /// <summary>Название технологии, которая открывает карту (для подсказок и замка в Арсенале).</summary>
        public static string TechName(CardDef d)
        {
            var id = CardCatalog.TechOf(d);
            if (id == null) return null;
            var t = TechnologyManager.Instance != null ? TechnologyManager.Instance.FindTech(id) : null;
            return t != null ? t.Name : id;
        }

        /// <summary>Почему нельзя заказать карту в этой системе (null — можно; ресурсы проверяются отдельно).</summary>
        public string ProduceBlocker(int owner, CardDef d, StarSystem sys)
        {
            if (!Available(owner, d)) return Loc.T("Карта недоступна вашей фракции");
            if (!Unlocked(owner, d)) return Loc.F("Нужна технология «{0}»", TechName(d));
            if (d.Site == ProdSite.Command)
            {
                if (CapitalOf(owner) < 0) return Loc.T("Нужна столица");
                return null;
            }
            if (sys == null || sys.OwnerId != owner || !sys.HasStarbase) return Loc.T("Нужна ваша колония");
            int pop = SystemPopulation(sys);
            if (pop <= 0) return Loc.T("Нужна колония с жителями");
            switch (d.Site)
            {
                case ProdSite.City:
                    if (Districts(sys, DistrictType.Urban) == 0) return Loc.T("Нужен городской район — там набирают пехоту");
                    int cap = Mathf.Max(2, pop / 2);
                    if (d.Has(Kw.Infantry) && CountAt(owner, sys.Id, x => x.Has(Kw.Infantry) && x.Site == ProdSite.City) >= cap)
                        return Loc.F("Призывников в системе не больше {0} (половина жителей)", cap);
                    return null;
                case ProdSite.Industry:
                    if (Districts(sys, DistrictType.Industrial) == 0) return Loc.T("Нужен промышленный район — там собирают технику");
                    return null;
                case ProdSite.Garrison:
                    if (d.Kind == CardKind.Structure)
                    {
                        int lim = sys.Fortification + 1;
                        if (CountAt(owner, sys.Id, x => x.Kind == CardKind.Structure && !x.Virtual) >= lim)
                            return Loc.F("Сооружений не больше {0}: укрепите звёздную базу, чтобы строить больше", lim);
                    }
                    else if (d.Kind == CardKind.Trap)
                    {
                        if (CountAt(owner, sys.Id, x => x.Kind == CardKind.Trap) >= 3) return Loc.T("Не больше 3 минных полей на колонию");
                    }
                    else if (CountAt(owner, sys.Id, x => x.Id == d.Id) >= Mathf.Max(1, pop / 3))
                        return Loc.T("Гарнизон не больше трети жителей");
                    return null;
            }
            return null;
        }

        /// <summary>Цена карты с учётом скидок империи (пока без скидок — точка расширения для технологий).</summary>
        public static void Cost(CardDef d, out float energy, out float minerals, out float alloys, out float influence)
        {
            energy = d.Energy; minerals = d.Minerals; alloys = d.Alloys; influence = d.Influence;
        }

        /// <summary>Заказ карты игроком: оплатить и поставить в очередь системы (приказы — сразу в столице).</summary>
        public bool TryProducePlayer(CardDef d, StarSystem sys)
        {
            if (ProduceBlocker(0, d, sys) != null) return false;
            Cost(d, out float e, out float m, out float a, out float inf);
            var eco = EconomyManager.Instance;
            if (eco == null || !eco.TrySpend(e, m, a, inf)) return false;
            Produce(0, d, sys, e, m, a, inf);
            eco.RecalculateAll();
            return true;
        }

        /// <summary>Заказ карты ИИ (ресурсы уже проверены и списываются здесь).</summary>
        public bool TryProduceAI(AIEmpireManager ai, CardDef d, StarSystem sys)
        {
            if (ai == null || ProduceBlocker(ai.OwnerId, d, sys) != null) return false;
            Cost(d, out float e, out float m, out float a, out float inf);
            if (ai.EnergyCredits < e || ai.Minerals < m || ai.Alloys < a || ai.Influence < inf) return false;
            ai.EnergyCredits -= e; ai.Minerals -= m; ai.Alloys -= a; ai.Influence -= inf;
            Produce(ai.OwnerId, d, sys, e, m, a, inf);
            return true;
        }

        private void Produce(int owner, CardDef d, StarSystem sys, float e, float m, float a, float inf)
        {
            if (d.Site == ProdSite.Command || d.Days <= 0)
            {
                var c = Create(owner, d.Id, d.Site == ProdSite.Command ? CapitalOf(owner) : sys.Id, d.Site == ProdSite.Garrison);
                if (owner == 0) NotificationCenter.Show(Loc.T("Карта в резерве"), d.Name, NotificationCenter.Kind.Success, 3f);
                Changed();
                return;
            }
            var cm = ConstructionManager.Instance;
            if (cm == null) return;
            cm.EnqueueTroopJob(owner, sys.Id, d.Id, d.Days, e, m, a, inf);
            Changed();
        }

        /// <summary>Стройка карты завершена — карта на колонии.</summary>
        public void CompleteJob(ConstructionJob j)
        {
            var d = CardCatalog.Get(j.DesignId);
            var sys = EmpireStats.GetSystem(j.SystemId);
            if (d == null || sys == null || sys.OwnerId != j.Owner) return;
            Create(j.Owner, d.Id, sys.Id, d.Site == ProdSite.Garrison);
            if (j.Owner == 0)
                NotificationCenter.Show(Loc.T("Отряд готов"), Loc.F("{0} · {1}", d.Name, sys.Name), NotificationCenter.Kind.Success, 4f);
            Changed();
        }

        public CardInstance Create(int owner, string defId, int systemId, bool garrison, int stars = 0)
        {
            var c = new CardInstance { Id = _nextId++, Def = defId, Owner = owner, System = systemId, Garrison = garrison, Stars = stars };
            _cards.Add(c);
            return c;
        }

        // ==================== ПОГРУЗКА ====================

        public bool AutoLoad(int fleetId) => !_noAutoLoad.Contains(fleetId);
        public void SetAutoLoad(int fleetId, bool on) { if (on) _noAutoLoad.Remove(fleetId); else _noAutoLoad.Add(fleetId); Changed(); }

        /// <summary>Можно ли погрузить карту на этот корабль: стоит у той же колонии, есть место, не ранена, не гарнизон.</summary>
        public string LoadBlocker(FleetData f, CardInstance c)
        {
            if (f == null || c == null || f.Destroyed) return Loc.T("Нет корабля");
            if (c.Garrison) return Loc.T("Гарнизон не покидает колонию");
            if (c.Wounded) return Loc.F("Ранены — ещё {0} дн.", c.WoundDays);
            if (c.Fleet >= 0) return Loc.T("Уже во флоте");
            if (f.OwnerId != c.Owner) return Loc.T("Чужой флот");
            if (f.State == FleetState.InHyperlane || f.CurrentSystemId != c.System) return Loc.T("Флот должен стоять у этой колонии");
            if (c.D != null && Used(f.Id) + c.D.Size > Capacity(f)) return Loc.T("Нет десантных мест");
            return null;
        }

        public bool Load(FleetData f, CardInstance c)
        {
            if (LoadBlocker(f, c) != null) return false;
            c.Fleet = f.Id;
            Changed();
            return true;
        }

        /// <summary>Высадить весь десант корабля на колонию, у которой он стоит (своя система со звёздной базой).</summary>
        public int Unload(FleetData f)
        {
            if (f == null || f.State == FleetState.InHyperlane) return 0;
            var sys = EmpireStats.GetSystem(f.CurrentSystemId);
            if (sys == null || sys.OwnerId != f.OwnerId || !sys.HasStarbase) return 0;
            int n = 0;
            foreach (var c in _cards) if (c.Fleet == f.Id) { c.Fleet = -1; c.System = sys.Id; n++; }
            if (n > 0) Changed();
            return n;
        }

        public void UnloadOne(CardInstance c)
        {
            if (c == null || c.Fleet < 0) return;
            var f = FindFleet(c.Fleet);
            if (f == null || f.State == FleetState.InHyperlane) return;
            var sys = EmpireStats.GetSystem(f.CurrentSystemId);
            if (sys == null || sys.OwnerId != c.Owner || !sys.HasStarbase) return;
            c.Fleet = -1; c.System = sys.Id;
            Changed();
        }

        /// <summary>Автопогрузка: военный корабль у своей колонии забирает готовые карты (сильнейшие первыми).</summary>
        private bool AutoLoadFleet(FleetData f)
        {
            int cap = Capacity(f);
            if (cap <= 0 || !AutoLoad(f.Id) || f.State == FleetState.InHyperlane) return false;
            var sys = EmpireStats.GetSystem(f.CurrentSystemId);
            if (sys == null || sys.OwnerId != f.OwnerId || !sys.HasStarbase) return false;
            int used = Used(f.Id);
            var here = AtSystem(f.OwnerId, sys.Id, false);
            if (here.Count == 0) return false;
            here.Sort((x, y) => Score(y).CompareTo(Score(x)));
            bool any = false;
            foreach (var c in here)
            {
                if (c.Wounded || c.D == null) continue;
                // Приказы места не занимают, но и не берутся без меры: до 4 на корабль
                if (c.D.Size == 0 && CountOrders(f.Id) >= 4) continue;
                if (used + c.D.Size > cap) continue;
                c.Fleet = f.Id;
                used += c.D.Size;
                any = true;
            }
            return any;
        }

        private int CountOrders(int fleetId) { int n = 0; foreach (var c in _cards) if (c.Fleet == fleetId && c.D != null && c.D.Size == 0) n++; return n; }

        private static float Score(CardInstance c) => c.D == null ? 0 : c.D.Power * 2 + c.D.Armor * 0.5f + c.D.Cost + c.Stars * 2;

        /// <summary>
        /// Состав десанта корабля для подсказок: «Десант 2/2» и список карт с уроном и бронёй.
        /// null — у корабля нет десантных мест.
        /// </summary>
        public static string TroopSummary(FleetData d, bool header = true)
        {
            var am = Instance;
            if (am == null || d == null) return null;
            int cap = Capacity(d);
            if (cap <= 0) return null;
            var cards = am.OnFleet(d.Id);
            var sb = new System.Text.StringBuilder();
            if (header) sb.Append(Loc.F("<color=#F2C766><b>Десант</b> · мест {0}/{1}</color>", am.Used(d.Id), cap));
            if (cards.Count == 0) sb.Append(header ? "\n" : "").Append(Loc.T("<color=#8AA2A8>Трюм пуст — загрузите карты в «Арсенале» (J)</color>"));
            foreach (var c in cards)
            {
                var def = c.D;
                if (def == null) continue;
                sb.Append('\n').Append("• ").Append(def.Name);
                if (c.Stars > 0) sb.Append(" <color=#FFD15C>").Append(new string('★', Mathf.Min(3, c.Stars))).Append("</color>");
                if (def.IsBoardUnit) sb.Append(Loc.F("  <color=#FFC94D>урон {0}</color> · <color=#FF7B6E>броня {1}</color>", def.Power, def.Armor));
                else sb.Append(Loc.F("  <color=#B48CFF>{0}</color>", def.KindName.ToLower()));
                if (c.Wounded) sb.Append(Loc.F("  <color=#FF8080>ранен, {0} дн.</color>", c.WoundDays));
            }
            if (d.OwnerId == 0 && cards.Count > 0) sb.Append('\n').Append(am.AutoLoad(d.Id) ? Loc.T("<color=#8AA2A8>Автопогрузка включена</color>") : Loc.T("<color=#8AA2A8>Автопогрузка выключена</color>"));
            return sb.ToString();
        }

        public static FleetData FindFleet(int id)
        {
            var fm = FleetManager.Instance;
            if (fm == null || id < 0) return null;
            foreach (var f in fm.AllFleets) if (f?.Data != null && f.Data.Id == id) return f.Data;
            return null;
        }

        // ==================== КОЛОДЫ ШТУРМА ====================

        /// <summary>Десант атакующего: всё, что лежит на его военных кораблях у этой системы (кроме раненых).</summary>
        public List<CardInstance> AttackDeck(int owner, int systemId)
        {
            var l = new List<CardInstance>();
            var fm = FleetManager.Instance;
            if (fm == null) return l;
            var fleets = new HashSet<int>();
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.OwnerId != owner || d.Type != FleetType.Military) continue;
                if (d.CurrentSystemId != systemId || d.State == FleetState.InHyperlane) continue;
                fleets.Add(d.Id);
            }
            foreach (var c in _cards) if (c.Owner == owner && fleets.Contains(c.Fleet) && !c.Wounded) l.Add(c);
            l.Sort((x, y) => Score(y).CompareTo(Score(x)));
            if (l.Count > MaxDeck) l.RemoveRange(MaxDeck, l.Count - MaxDeck);
            return l;
        }

        /// <summary>Число военных кораблей стороны у системы (экипажи — «Морская пехота» при нехватке карт).</summary>
        public static int Warships(int owner, int systemId)
        {
            int n = 0;
            var fm = FleetManager.Instance;
            if (fm == null) return 0;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d != null && !d.Destroyed && d.OwnerId == owner && d.Type == FleetType.Military && d.CurrentSystemId == systemId && d.State != FleetState.InHyperlane) n++;
            }
            return n;
        }

        public static float FleetPowerAt(int owner, int systemId)
        {
            float p = 0;
            var fm = FleetManager.Instance;
            if (fm == null) return 0;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d != null && !d.Destroyed && d.OwnerId == owner && d.Type == FleetType.Military && d.CurrentSystemId == systemId && d.State != FleetState.InHyperlane)
                    p += CombatMath.Power(d);
            }
            return p;
        }

        /// <summary>Оборона колонии: гарнизон и резерв, стоящие в этой системе (кроме раненых).</summary>
        public List<CardInstance> DefenseCards(int owner, int systemId)
        {
            var l = new List<CardInstance>();
            foreach (var c in _cards) if (c.Owner == owner && c.Fleet < 0 && c.System == systemId && !c.Wounded) l.Add(c);
            return l;
        }

        // ==================== ИТОГИ БОЯ ====================

        /// <summary>Применить итог штурма: потери, ветеранство, раны, трофей.</summary>
        public void ApplyResult(BattleResult r, int attacker, int defender, int systemId)
        {
            if (r == null) return;
            var lost = new HashSet<long>(r.Lost);
            _cards.RemoveAll(c => lost.Contains(c.Id));
            foreach (var s in r.Survivors)
            {
                var c = Get(s.inst);
                if (c == null) continue;
                if (s.fought) c.Stars = Mathf.Min(3, c.Stars + 1);
                if (s.hp < 0.5f) c.WoundDays = WoundDaysAfterBattle;
            }
            // Трофей: уцелевшая техника защитника переходит к победителю (на борт его флота у системы)
            if (r.AttackerWon && r.TrophyInst >= 0)
            {
                var t = Get(r.TrophyInst);
                if (t != null)
                {
                    t.Owner = attacker;
                    t.Garrison = false;
                    t.Stars = 0;
                    t.Fleet = -1;
                    t.System = systemId;
                }
            }
            // Победитель-атакующий: гарнизон павшей колонии уничтожен, его резерв в системе — тоже
            if (r.AttackerWon)
                _cards.RemoveAll(c => c.Owner == defender && c.Fleet < 0 && c.System == systemId);
            Changed();
        }

        // ==================== КАЛЕНДАРЬ ====================

        private void HandleDay(int day, int month, int year)
        {
            if (!UIManager.IsGameStarted) return;
            bool changed = false;

            // Раны заживают
            foreach (var c in _cards) if (c.WoundDays > 0) { c.WoundDays--; if (c.WoundDays == 0) changed = true; }

            // Погибший корабль — погиб и десант на борту
            var alive = new HashSet<int>();
            var fm = FleetManager.Instance;
            if (fm != null) foreach (var f in fm.AllFleets) if (f?.Data != null && !f.Data.Destroyed) alive.Add(f.Data.Id);
            int lostPlayer = 0;
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                var c = _cards[i];
                if (c.Fleet < 0 || alive.Contains(c.Fleet)) continue;
                if (c.Owner == 0) lostPlayer++;
                _cards.RemoveAt(i);
                changed = true;
            }
            if (lostPlayer > 0)
                NotificationCenter.Show(Loc.T("Десант погиб"), Loc.F("Вместе с кораблями потеряно карт: {0}", lostPlayer), NotificationCenter.Kind.Danger, 6f);

            // Автопогрузка — раз в несколько дней, чтобы не гонять каждый день
            if (day % 3 == 0 && fm != null)
                foreach (var f in fm.AllFleets)
                    if (f?.Data != null && AutoLoadFleet(f.Data)) changed = true;

            if (day == 1) foreach (var ai in AIEmpireManager.Alive) ThinkAI(ai);
            if (changed) Changed();
        }

        private void HandleCaptured(StarSystem sys, int oldOwner, int newOwner)
        {
            if (sys == null || oldOwner < 0) return;
            int n = _cards.RemoveAll(c => c.Owner == oldOwner && c.Fleet < 0 && c.System == sys.Id);
            if (n > 0 && oldOwner == 0)
                NotificationCenter.Show(Loc.T("Гарнизон потерян"), Loc.F("{0}: потеряно карт — {1}", sys.Name, n), NotificationCenter.Kind.Danger, 6f);
            if (n > 0) Changed();
        }

        // ==================== ИИ ====================

        /// <summary>
        /// ИИ раз в месяц: в войне или при подготовке к ней строит пехоту и технику в столице и пограничных колониях,
        /// на границе — гарнизоны и сооружения. Не тратит больше трети запасов.
        /// </summary>
        private void ThinkAI(AIEmpireManager ai)
        {
            if (ai == null || ai.IsEliminated) return;
            int owner = ai.OwnerId;
            int count = 0;
            foreach (var c in _cards) if (c.Owner == owner) count++;
            bool war = ai.AtWar || ai.Mode == AIEmpireManager.ArmyMode.Attack;
            int target = war ? 28 : 12;
            if (count >= target || ai.MonthlyEnergyIncome < 3f) return;

            // Где строить: столица и колонии с районами
            StarSystem best = null; int bestScore = -1;
            foreach (var sys in EmpireStats.Systems)
            {
                if (sys.OwnerId != owner || !sys.HasStarbase || SystemPopulation(sys) <= 0) continue;
                int sc = Districts(sys, DistrictType.Urban) * 2 + Districts(sys, DistrictType.Industrial) * 3 + (sys.Id == ai.CapitalSystemId ? 4 : 0);
                if (sc > bestScore) { bestScore = sc; best = sys; }
            }
            if (best == null) return;

            var options = new List<CardDef>();
            foreach (var d in CardCatalog.ForFaction(FactionTraits.Key(owner)))
                if (ProduceBlocker(owner, d, best) == null) options.Add(d);
            if (options.Count == 0) return;
            for (int k = 0; k < 2; k++)
            {
                var d = options[UnityEngine.Random.Range(0, options.Count)];
                Cost(d, out float e, out float m, out float a, out float inf);
                if (ai.Minerals * 0.33f < m || ai.Alloys * 0.33f < a || ai.EnergyCredits * 0.33f < e || ai.Influence * 0.33f < inf) continue;
                TryProduceAI(ai, d, best);
            }
        }

        // ==================== СОХРАНЕНИЕ ====================

        public ArmySave CaptureState()
        {
            var s = new ArmySave { NextId = _nextId };
            s.Cards.AddRange(_cards);
            s.NoAutoLoad.AddRange(_noAutoLoad);
            return s;
        }

        public void RestoreState(ArmySave s)
        {
            _cards.Clear();
            _noAutoLoad.Clear();
            _nextId = 1;
            if (s == null) { Changed(); return; }
            foreach (var c in s.Cards) if (c != null && CardCatalog.Exists(c.Def)) _cards.Add(c);
            foreach (var id in s.NoAutoLoad) _noAutoLoad.Add(id);
            _nextId = Math.Max(s.NextId, 1);
            foreach (var c in _cards) if (c.Id >= _nextId) _nextId = c.Id + 1;
            Changed();
        }
    }
}
