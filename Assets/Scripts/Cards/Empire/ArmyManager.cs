using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Cards
{
    /// <summary>Экземпляр карты в резерве империи: ветеранство и раны.</summary>
    [Serializable]
    public class CardInstance
    {
        public long Id;
        public string Def;
        public int Owner;
        /// <summary>Старые сохранения: флот, на борту которого была карта. Сейчас всегда −1 — резерв общий.</summary>
        public int Fleet = -1;
        /// <summary>Старые сохранения: колония, где стояла карта. Сейчас всегда −1 — резерв общий.</summary>
        public int System = -1;
        public int Stars;
        /// <summary>Дней до излечения (раненая карта не идёт в бой).</summary>
        public int WoundDays;
        /// <summary>Старые сохранения: гарнизонная карта. Оборонительные карты теперь узнаются по месту производства.</summary>
        public bool Garrison;

        [NonSerialized] private CardDef _def;
        public CardDef D { get { if (_def == null) _def = CardCatalog.Get(Def); return _def; } }
        public bool Wounded => WoundDays > 0;
        /// <summary>Только для обороны колоний: сооружения, мины, гарнизонные отряды.</summary>
        public bool DefenseOnly => D != null && D.Site == ProdSite.Garrison;
    }

    /// <summary>Карта в очереди производства империи (оплачена при заказе).</summary>
    [Serializable]
    public class ArmyJob
    {
        public int Owner;
        public string Def;
        public float Days, DaysLeft;
        public float Energy, Minerals, Alloys, Influence;
        public float Progress => Days <= 0f ? 1f : Mathf.Clamp01(1f - DaysLeft / Days);
        public bool Started => DaysLeft < Days;
    }

    /// <summary>Сколько карт этого вида империя держит в резерве.</summary>
    [Serializable]
    public class ArmyTarget
    {
        public int Owner;
        public string Def;
        public int Count;
    }

    /// <summary>Накопленный военный бюджет империи: сколько ресурсов отложено на армию.</summary>
    [Serializable]
    public class ArmyFund
    {
        public int Owner;
        public float Energy, Minerals, Alloys, Influence;
    }

    /// <summary>Сколько дохода империя отдаёт армии.</summary>
    public enum ArmyBudget { Stop = 0, Low = 1, Normal = 2, War = 3, Mobilize = 4 }

    [Serializable]
    public class ArmySave
    {
        public List<CardInstance> Cards = new List<CardInstance>();
        public long NextId = 1;
        /// <summary>Старые сохранения (автопогрузка флотов) — не используется.</summary>
        public List<int> NoAutoLoad = new List<int>();
        public List<ArmyJob> Jobs = new List<ArmyJob>();
        public List<ArmyTarget> Targets = new List<ArmyTarget>();
        /// <summary>Игрок сам задаёт состав армии (иначе — автосостав).</summary>
        public bool PlayerManual;
        public int PlayerSize;
        /// <summary>Военный бюджет игрока (ArmyBudget); старые сохранения — обычный.</summary>
        public int PlayerBudget = (int)ArmyBudget.Normal;
        public List<ArmyFund> Funds = new List<ArmyFund>();
        /// <summary>Стартовые отряды уже выданы (новая партия — один раз).</summary>
        public bool StarterDone;
    }

    /// <summary>
    /// Армия империй — один общий резерв карт. Игрок задаёт состав армии (сколько карт каждого вида держать) или доверяет
    /// автосоставу; империя сама заказывает недостающее и пополняет потери — из военного бюджета: каждый день в фонд армии
    /// откладывается выбранная доля месячного дохода (25%, 50%, 100%), и заказ уходит, когда фонд покрывает цену.
    /// Так армия не выкачивает запасы, нужные на стройку и флот; «Мобилизация» разрешает тратить и запасы (до четверти
    /// за заказ). В начале партии у каждой империи — стартовый отряд. Производство общее на империю: число линий растёт с городскими и промышленными районами,
    /// пехоте нужен хотя бы один городской район, технике — промышленный, приказам — столица. Погрузки нет: при штурме
    /// в бой идут лучшие карты резерва, сколько поместится на военные корабли у цели (десантные места: корвет 2,
    /// фрегат 4, эсминец 6); при обороне — сколько вместит колония (жители и укрепления), сооружения и мины — только
    /// в обороне. Погибшее пропадает, выжившее получает звезду ветерана. Правила одинаковы для игрока и ИИ.
    /// </summary>
    public class ArmyManager : MonoBehaviour
    {
        public static ArmyManager Instance { get; private set; }
        /// <summary>Резерв, очередь или состав армии изменились — для окон.</summary>
        public static event Action OnChanged;

        public const int WoundDaysAfterBattle = 30;
        public const int MaxDeck = 20, MinDeck = 8;
        public const int DefaultSize = 16, MinSize = 4, MaxSize = 40;
        /// <summary>«Мобилизация»: какую долю запасов каждого ресурса можно отдать за один заказ.</summary>
        private const float MobilizeShare = 0.25f;
        /// <summary>Фонд армии копится не больше чем на столько месяцев бюджета (плюс запас на самую дорогую карту).</summary>
        private const float FundMonths = 6f, FundFloor = 150f;
        private const int ReplenishEveryDays = 3;

        private readonly List<CardInstance> _cards = new List<CardInstance>();
        private readonly List<ArmyJob> _jobs = new List<ArmyJob>();
        private readonly Dictionary<int, Dictionary<string, int>> _targets = new Dictionary<int, Dictionary<string, int>>();
        private long _nextId = 1;
        private bool _subscribed;
        /// <summary>Фонд армии по империям: гелий-3, титан, сплавы, влияние.</summary>
        private readonly Dictionary<int, float[]> _fund = new Dictionary<int, float[]>();
        private readonly Dictionary<int, ArmyBudget> _budget = new Dictionary<int, ArmyBudget>();
        private bool _starterDone;

        /// <summary>Игрок задаёт состав сам; false — состав подбирает империя (по умолчанию).</summary>
        public bool PlayerManual { get; private set; }
        /// <summary>Размер армии игрока для автосостава.</summary>
        public int PlayerSize { get; private set; } = DefaultSize;

        // ==================== ВОЕННЫЙ БЮДЖЕТ ====================

        /// <summary>Доля месячного дохода, которая уходит в фонд армии.</summary>
        public static float ShareOf(ArmyBudget b)
            => b == ArmyBudget.Stop ? 0f : b == ArmyBudget.Low ? 0.25f : b == ArmyBudget.Normal ? 0.5f : 1f;

        public static string BudgetName(ArmyBudget b)
            => b == ArmyBudget.Stop ? Loc.T("Стоп") : b == ArmyBudget.Low ? Loc.T("Мирный") : b == ArmyBudget.Normal ? Loc.T("Обычный")
             : b == ArmyBudget.War ? Loc.T("Военный") : Loc.T("Мобилизация");

        public ArmyBudget BudgetOf(int owner) => _budget.TryGetValue(owner, out var b) ? b : ArmyBudget.Normal;
        public ArmyBudget PlayerBudget => BudgetOf(0);

        public void SetPlayerBudget(ArmyBudget b)
        {
            _budget[0] = b;
            Replenish(0);
            Changed();
        }

        /// <summary>Фонд армии: [гелий-3, титан, сплавы, влияние].</summary>
        public float[] FundOf(int owner)
        {
            if (!_fund.TryGetValue(owner, out var f)) _fund[owner] = f = new float[4];
            return f;
        }

        /// <summary>Месячный доход империи по ресурсам (только положительный) — от него считается бюджет.</summary>
        public static void Income(int owner, out float e, out float m, out float a, out float inf)
        {
            e = m = a = inf = 0f;
            if (owner == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco == null) return;
                e = eco.MonthlyEnergyIncome; m = eco.MonthlyMineralsIncome; a = eco.MonthlyAlloysIncome; inf = eco.MonthlyInfluenceIncome;
            }
            else
            {
                var ai = AIEmpireManager.For(owner);
                if (ai == null || ai.IsEliminated) return;
                e = ai.MonthlyEnergyIncome; m = ai.MonthlyMineralsIncome; a = ai.MonthlyAlloysIncome; inf = ai.MonthlyInfluenceIncome;
            }
            e = Mathf.Max(0f, e); m = Mathf.Max(0f, m); a = Mathf.Max(0f, a); inf = Mathf.Max(0f, inf);
        }

        /// <summary>Бюджет армии в месяц по ресурсам: доля дохода.</summary>
        public float[] MonthlyBudget(int owner)
        {
            Income(owner, out float e, out float m, out float a, out float inf);
            float k = ShareOf(BudgetOf(owner));
            return new[] { e * k, m * k, a * k, inf * k };
        }

        /// <summary>День прошёл: в фонд армии — дневная доля бюджета, пока армии чего-то не хватает (иначе фонд не копится впрок).</summary>
        private void AccrueFund(int owner)
        {
            if (!Lacking(owner)) return;
            var mb = MonthlyBudget(owner);
            var f = FundOf(owner);
            for (int i = 0; i < 4; i++) f[i] = Mathf.Min(f[i] + mb[i] / 30f, Mathf.Max(mb[i] * FundMonths, FundFloor));
        }

        /// <summary>Есть ли в составе армии карты, которых нет в резерве и очереди.</summary>
        private bool Lacking(int owner)
        {
            if (!_targets.TryGetValue(owner, out var t)) return false;
            foreach (var kv in t) if (Count(owner, kv.Key) + Queued(owner, kv.Key) < kv.Value) return true;
            return false;
        }

        /// <summary>Покрывает ли фонд цену карты (при «Мобилизации» — запасы, до четверти на заказ).</summary>
        public bool FundCovers(int owner, CardDef d)
        {
            if (d == null) return false;
            if (BudgetOf(owner) == ArmyBudget.Mobilize) return Affordable(owner, d, MobilizeShare);
            if (!Affordable(owner, d)) return false;
            Cost(d, out float e, out float m, out float a, out float inf);
            var f = FundOf(owner);
            const float eps = 0.01f;
            return e <= f[0] + eps && m <= f[1] + eps && a <= f[2] + eps && inf <= f[3] + eps;
        }

        /// <summary>Чего не хватает в фонде на карту — для подсказки («сплавы 12 / 60»); null — хватает.</summary>
        public string FundLack(int owner, CardDef d)
        {
            Cost(d, out float e, out float m, out float a, out float inf);
            var f = FundOf(owner);
            var parts = new List<string>();
            void P(float need, float have, string name) { if (need > have + 0.01f) parts.Add(Loc.F("{0} {1:0} / {2:0}", name, have, need)); }
            P(e, f[0], Loc.T("гелий-3")); P(m, f[1], Loc.T("титан")); P(a, f[2], Loc.T("сплавы")); P(inf, f[3], Loc.T("влияние"));
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        public IReadOnlyList<CardInstance> All => _cards;
        public IReadOnlyList<ArmyJob> Jobs => _jobs;

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
            TechnologyManager.OnTechCompleted += HandleTech;
        }

        /// <summary>Изучена технология — сообщить, какие карты она открыла, и обновить автосостав.</summary>
        private void HandleTech(Technology t)
        {
            if (t == null) return;
            var names = new List<string>();
            string mine = FactionTraits.Key(0);
            foreach (var d in CardCatalog.ForFaction(mine)) if (CardCatalog.TechOf(d) == t.Id) names.Add(d.Name);
            if (names.Count == 0) return;
            NotificationCenter.Show(Loc.T("Новые карты в Арсенале"), string.Join(", ", names), NotificationCenter.Kind.Success, 5f);
            if (!PlayerManual) ApplyAutoDoctrine(0, PlayerSize);
            Changed();
        }

        private void OnDestroy()
        {
            if (_subscribed && TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
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

        public CardInstance Get(long id)
        {
            foreach (var c in _cards) if (c.Id == id) return c;
            return null;
        }

        /// <summary>Карт этого вида в резерве (с ранеными).</summary>
        public int Count(int owner, string defId)
        {
            int n = 0;
            foreach (var c in _cards) if (c.Owner == owner && c.Def == defId) n++;
            return n;
        }

        /// <summary>Карт этого вида в очереди производства.</summary>
        public int Queued(int owner, string defId)
        {
            int n = 0;
            foreach (var j in _jobs) if (j.Owner == owner && j.Def == defId) n++;
            return n;
        }

        public List<ArmyJob> JobsOf(int owner)
        {
            var l = new List<ArmyJob>();
            foreach (var j in _jobs) if (j.Owner == owner) l.Add(j);
            return l;
        }

        /// <summary>Десантных мест у корабля: корвет 2, фрегат 4, эсминец 6 (только военные). Приказы мест не занимают.</summary>
        public static int Capacity(FleetData d)
        {
            if (d == null || d.Destroyed || d.Type != FleetType.Military) return 0;
            return d.HullClass == ShipClass.Destroyer ? 6 : d.HullClass == ShipClass.Frigate ? 4 : 2;
        }

        /// <summary>Содержание резерва (гелий-3 в месяц).</summary>
        public float Upkeep(int owner)
        {
            float s = 0f;
            foreach (var c in _cards) if (c.Owner == owner && c.D != null) s += c.D.Upkeep;
            return s;
        }

        public static float UpkeepOf(int owner) => Instance != null ? Instance.Upkeep(owner) : 0f;

        // ==================== ВОЕННАЯ БАЗА ИМПЕРИИ ====================

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

        /// <summary>Всё, от чего зависит производство армии: колонии, жители, районы, укрепления.</summary>
        public struct Base
        {
            public int Colonies, Population, Urban, Industrial, Fortification;
            /// <summary>Параллельных линий производства.</summary>
            public int Lines => Colonies == 0 ? 0 : Mathf.Clamp(1 + (Urban + Industrial) / 3, 1, 6);
            public int InfantryCap => Mathf.Max(4, Population / 2);
            public int GarrisonCap => Mathf.Max(2, Population / 3);
            public int StructureCap => Colonies + Fortification;
            public int TrapCap => 3 + Colonies;
        }

        public static Base BaseOf(int owner)
        {
            var b = new Base();
            foreach (var sys in EmpireStats.Systems)
            {
                if (sys == null || sys.OwnerId != owner || !sys.HasStarbase) continue;
                int pop = SystemPopulation(sys);
                if (pop <= 0) continue;
                b.Colonies++;
                b.Population += pop;
                b.Urban += Districts(sys, DistrictType.Urban);
                b.Industrial += Districts(sys, DistrictType.Industrial);
                b.Fortification += sys.Fortification;
            }
            return b;
        }

        public static int CapitalOf(int owner)
        {
            if (owner == 0) return EconomyManager.PlayerCapitalId;
            var ai = AIEmpireManager.For(owner);
            return ai != null ? ai.CapitalSystemId : -1;
        }

        /// <summary>Доступна ли карта фракции империи.</summary>
        public static bool Available(int owner, CardDef d)
            => d != null && !d.Virtual && d.Site != ProdSite.None && (d.Faction == null || d.Faction == FactionTraits.Key(owner));

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

        /// <summary>Есть ли у империи, где производить карту (районы, столица). Лимиты числа — в <see cref="ProduceBlocker"/>.</summary>
        public static string SiteBlocker(int owner, CardDef d, Base b)
        {
            if (!Available(owner, d)) return Loc.T("Карта недоступна вашей фракции");
            if (!Unlocked(owner, d)) return Loc.F("Нужна технология «{0}»", TechName(d));
            if (d.Site == ProdSite.Command) return CapitalOf(owner) < 0 ? Loc.T("Нужна столица") : null;
            if (b.Colonies == 0) return Loc.T("Нужна колония с жителями");
            if (d.Site == ProdSite.City && b.Urban == 0) return Loc.T("Нужен городской район хотя бы в одной колонии — там набирают пехоту");
            if (d.Site == ProdSite.Industry && b.Industrial == 0) return Loc.T("Нужен промышленный район хотя бы в одной колонии — там собирают технику");
            return null;
        }

        /// <summary>Сколько карт подходящих под условие есть и строится.</summary>
        private int CountAll(int owner, Predicate<CardDef> match)
        {
            int n = 0;
            foreach (var c in _cards) if (c.Owner == owner && c.D != null && match(c.D)) n++;
            foreach (var j in _jobs) { if (j.Owner != owner) continue; var d = CardCatalog.Get(j.Def); if (d != null && match(d)) n++; }
            return n;
        }

        /// <summary>Почему империя не может заказать ещё одну такую карту (null — может; ресурсы проверяются отдельно).</summary>
        public string ProduceBlocker(int owner, CardDef d) => ProduceBlocker(owner, d, BaseOf(owner));

        public string ProduceBlocker(int owner, CardDef d, Base b)
        {
            string s = SiteBlocker(owner, d, b);
            if (s != null) return s;
            if (d.Site == ProdSite.City && d.Has(Kw.Infantry) && CountAll(owner, x => x.Site == ProdSite.City && x.Has(Kw.Infantry)) >= b.InfantryCap)
                return Loc.F("Призывников не больше {0} — половины жителей империи", b.InfantryCap);
            if (d.Site == ProdSite.Garrison)
            {
                if (d.Kind == CardKind.Structure)
                {
                    if (CountAll(owner, x => x.Kind == CardKind.Structure && !x.Virtual) >= b.StructureCap)
                        return Loc.F("Сооружений не больше {0}: по одному на колонию плюс уровни укреплений баз", b.StructureCap);
                }
                else if (d.Kind == CardKind.Trap)
                {
                    if (CountAll(owner, x => x.Kind == CardKind.Trap) >= b.TrapCap) return Loc.F("Минных полей не больше {0}", b.TrapCap);
                }
                else if (CountAll(owner, x => x.Site == ProdSite.Garrison && x.Kind == CardKind.Unit) >= b.GarrisonCap)
                    return Loc.F("Гарнизонных отрядов не больше {0} — трети жителей империи", b.GarrisonCap);
            }
            return null;
        }

        /// <summary>Цена карты с учётом скидок империи (пока без скидок — точка расширения для технологий).</summary>
        public static void Cost(CardDef d, out float energy, out float minerals, out float alloys, out float influence)
        {
            energy = d.Energy; minerals = d.Minerals; alloys = d.Alloys; influence = d.Influence;
        }

        // ---- Кошелёк империи (игрок — экономика, ИИ — свои запасы)

        private static bool Stock(int owner, out float e, out float m, out float a, out float inf)
        {
            e = m = a = inf = 0f;
            if (owner == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco == null) return false;
                e = eco.EnergyCredits; m = eco.Minerals; a = eco.Alloys; inf = eco.Influence;
                return true;
            }
            var ai = AIEmpireManager.For(owner);
            if (ai == null || ai.IsEliminated) return false;
            e = ai.EnergyCredits; m = ai.Minerals; a = ai.Alloys; inf = ai.Influence;
            return true;
        }

        private static void Pay(int owner, float e, float m, float a, float inf, int sign)
        {
            if (owner == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco == null) return;
                eco.EnergyCredits -= e * sign; eco.Minerals -= m * sign; eco.Alloys -= a * sign; eco.Influence -= inf * sign;
                eco.RaiseResourcesChanged();
                return;
            }
            var ai = AIEmpireManager.For(owner);
            if (ai == null) return;
            ai.EnergyCredits -= e * sign; ai.Minerals -= m * sign; ai.Alloys -= a * sign; ai.Influence -= inf * sign;
        }

        /// <summary>Хватит ли ресурсов, не отдавая больше доли <paramref name="share"/> каждого запаса (1 — всё).</summary>
        public static bool Affordable(int owner, CardDef d, float share = 1f)
        {
            if (d == null || !Stock(owner, out float se, out float sm, out float sa, out float si)) return false;
            Cost(d, out float e, out float m, out float a, out float inf);
            return e <= se * share && m <= sm * share && a <= sa * share && inf <= si * share;
        }

        /// <summary>Оплатить и поставить в очередь империи (приказы и карты без срока — сразу в резерв).</summary>
        private bool Order(int owner, CardDef d, Base b)
        {
            if (ProduceBlocker(owner, d, b) != null || !FundCovers(owner, d)) return false;
            Cost(d, out float e, out float m, out float a, out float inf);
            Pay(owner, e, m, a, inf, 1);
            if (BudgetOf(owner) != ArmyBudget.Mobilize) Spend(owner, e, m, a, inf, 1);
            if (d.Site == ProdSite.Command || d.Days <= 0) { Create(owner, d.Id); _ready.Add((owner, d.Name)); }
            else _jobs.Add(new ArmyJob { Owner = owner, Def = d.Id, Days = d.Days, DaysLeft = d.Days, Energy = e, Minerals = m, Alloys = a, Influence = inf });
            return true;
        }

        /// <summary>Списать с фонда армии (sign = −1 — вернуть в фонд при отмене заказа).</summary>
        private void Spend(int owner, float e, float m, float a, float inf, int sign)
        {
            var f = FundOf(owner);
            f[0] = Mathf.Max(0f, f[0] - e * sign); f[1] = Mathf.Max(0f, f[1] - m * sign);
            f[2] = Mathf.Max(0f, f[2] - a * sign); f[3] = Mathf.Max(0f, f[3] - inf * sign);
        }

        /// <summary>Снять с очереди ещё не начатую карту этого вида (с конца) и вернуть ресурсы.</summary>
        private bool CancelOne(int owner, string defId)
        {
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                var j = _jobs[i];
                if (j.Owner != owner || j.Def != defId) continue;
                Pay(owner, j.Energy, j.Minerals, j.Alloys, j.Influence, -1);
                Spend(owner, j.Energy, j.Minerals, j.Alloys, j.Influence, -1);
                _jobs.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>Отменить заказ из очереди (игрок, клик по карте в очереди). Цель состава уменьшается, иначе заказ вернётся.</summary>
        public void CancelJob(ArmyJob j)
        {
            if (j == null || !_jobs.Remove(j)) return;
            Pay(j.Owner, j.Energy, j.Minerals, j.Alloys, j.Influence, -1);
            Spend(j.Owner, j.Energy, j.Minerals, j.Alloys, j.Influence, -1);
            int want = Target(j.Owner, j.Def);
            int have = Count(j.Owner, j.Def) + Queued(j.Owner, j.Def);
            if (want > have) SetTargetRaw(j.Owner, j.Def, have);
            if (j.Owner == 0 && !PlayerManual) PlayerManual = true;
            Changed();
        }

        /// <summary>Стройка из старых сохранений (очередь колонии) завершена — карта в резерв.</summary>
        public void CompleteJob(ConstructionJob j)
        {
            var d = CardCatalog.Get(j.DesignId);
            if (d == null) return;
            Create(j.Owner, d.Id);
            if (j.Owner == 0) NotificationCenter.Show(Loc.T("Пополнение армии"), d.Name, NotificationCenter.Kind.Success, 4f);
            Changed();
        }

        public CardInstance Create(int owner, string defId, int stars = 0)
        {
            var c = new CardInstance { Id = _nextId++, Def = defId, Owner = owner, Stars = stars };
            _cards.Add(c);
            return c;
        }

        // ==================== СОСТАВ АРМИИ ====================

        public int Target(int owner, string defId)
            => _targets.TryGetValue(owner, out var t) && t.TryGetValue(defId, out int n) ? n : 0;

        public int TargetTotal(int owner)
        {
            int s = 0;
            if (_targets.TryGetValue(owner, out var t)) foreach (var kv in t) s += kv.Value;
            return s;
        }

        private void SetTargetRaw(int owner, string defId, int n)
        {
            if (!_targets.TryGetValue(owner, out var t)) _targets[owner] = t = new Dictionary<string, int>();
            if (n <= 0) t.Remove(defId); else t[defId] = n;
        }

        /// <summary>
        /// Игрок меняет число карт вида в составе армии (кнопки −/+ в Арсенале). Состав становится ручным;
        /// уменьшение снимает с очереди ещё не готовые карты с возвратом ресурсов.
        /// </summary>
        public void SetTarget(int owner, string defId, int n)
        {
            n = Mathf.Clamp(n, 0, MaxDeck);
            if (owner == 0 && !PlayerManual) PlayerManual = true;
            SetTargetRaw(owner, defId, n);
            while (Count(owner, defId) + Queued(owner, defId) > n && CancelOne(owner, defId)) { }
            Replenish(owner);
            Changed();
        }

        /// <summary>Включить автосостав игрока (империя сама подбирает карты под размер армии).</summary>
        public void SetPlayerAuto(bool on)
        {
            PlayerManual = !on;
            if (on) ApplyAutoDoctrine(0, PlayerSize);
            Replenish(0);
            Changed();
        }

        public void SetPlayerSize(int size)
        {
            PlayerSize = Mathf.Clamp(size, MinSize, MaxSize);
            if (!PlayerManual) ApplyAutoDoctrine(0, PlayerSize);
            Replenish(0);
            Changed();
        }

        /// <summary>Сила карты для отбора в колоду и в автосостав. Смертник (гибнет в конце хода) ценится вдвое меньше.</summary>
        public static float Score(CardDef d) => d == null ? 0 : d.Power * (d.Has(Kw.Fragile) ? 1f : 2f) + d.Armor * 0.5f + d.Cost;
        private static float Score(CardInstance c) => c.D == null ? 0 : Score(c.D) + c.Stars * 2;

        /// <summary>
        /// Рекомендованный состав из <paramref name="size"/> карт: пехота ~45%, техника ~25%, приказы ~15%, оборона ~15%.
        /// В каждой группе — две сильнейшие доступные карты и самая дешёвая «рабочая лошадка». Чего производить негде —
        /// доля уходит пехоте.
        /// </summary>
        public static Dictionary<string, int> Recommend(int owner, int size)
        {
            var b = BaseOf(owner);
            var groups = new Dictionary<ProdSite, List<CardDef>>();
            foreach (var d in CardCatalog.ForFaction(FactionTraits.Key(owner)))
            {
                if (d.Kind == CardKind.Hero || SiteBlocker(owner, d, b) != null) continue;
                if (!groups.TryGetValue(d.Site, out var l)) groups[d.Site] = l = new List<CardDef>();
                l.Add(d);
            }
            var shares = new (ProdSite site, float share)[] { (ProdSite.Industry, 0.25f), (ProdSite.Command, 0.15f), (ProdSite.Garrison, 0.15f) };
            var res = new Dictionary<string, int>();
            int left = size;
            foreach (var (site, share) in shares)
            {
                if (!groups.TryGetValue(site, out var l) || l.Count == 0) continue;
                int n = Mathf.Min(left, Mathf.RoundToInt(size * share));
                Spread(l, n, res);
                left -= n;
            }
            if (groups.TryGetValue(ProdSite.City, out var city) && city.Count > 0) Spread(city, left, res);
            return res;
        }

        private static void Spread(List<CardDef> l, int n, Dictionary<string, int> res)
        {
            if (n <= 0) return;
            l.Sort((x, y) => Score(y).CompareTo(Score(x)));
            var pick = new List<CardDef>();
            for (int i = 0; i < l.Count && pick.Count < 2; i++) pick.Add(l[i]);
            CardDef cheap = null;
            foreach (var d in l) if (!pick.Contains(d) && (cheap == null || d.Cost < cheap.Cost)) cheap = d;
            if (cheap != null) pick.Add(cheap);
            // Сильнейшей — больше остальных: 40/30/30
            float[] w = pick.Count == 3 ? new[] { 0.4f, 0.3f, 0.3f } : pick.Count == 2 ? new[] { 0.55f, 0.45f } : new[] { 1f };
            int given = 0;
            for (int i = 0; i < pick.Count; i++)
            {
                int k = i == pick.Count - 1 ? n - given : Mathf.RoundToInt(n * w[i]);
                k = Mathf.Clamp(k, 0, n - given);
                if (k <= 0) continue;
                res.TryGetValue(pick[i].Id, out int had);
                res[pick[i].Id] = had + k;
                given += k;
            }
        }

        private void ApplyAutoDoctrine(int owner, int size)
        {
            var rec = Recommend(owner, size);
            _targets[owner] = rec;
            // Лишнее в очереди (чего больше нет в составе) — снять с возвратом
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                var j = _jobs[i];
                if (j.Owner != owner || j.Started) continue;
                if (Count(owner, j.Def) + Queued(owner, j.Def) > Target(owner, j.Def))
                {
                    Pay(owner, j.Energy, j.Minerals, j.Alloys, j.Influence, -1);
                    Spend(owner, j.Energy, j.Minerals, j.Alloys, j.Influence, -1);
                    _jobs.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Автопополнение: заказать недостающие до состава карты — сперва те, которых не хватает больше всего.
        /// Не больше 2 заказов за раз, только на средства фонда армии и не длиннее двух заказов на линию —
        /// ресурсы не замораживаются в далёкой очереди.
        /// </summary>
        private bool Replenish(int owner)
        {
            if (!_targets.TryGetValue(owner, out var t) || t.Count == 0) return false;
            var b = BaseOf(owner);
            if (b.Colonies == 0) return false;
            var need = new List<(CardDef d, float lack)>();
            foreach (var kv in t)
            {
                int have = Count(owner, kv.Key) + Queued(owner, kv.Key);
                if (have >= kv.Value) continue;
                var d = CardCatalog.Get(kv.Key);
                if (d != null) need.Add((d, (kv.Value - have) / (float)kv.Value));
            }
            if (need.Count == 0) return false;
            need.Sort((x, y) => y.lack.CompareTo(x.lack));
            int orders = 0, room = QueueRoom(owner, b);
            foreach (var (d, _) in need)
            {
                if (orders >= 2) break;
                bool instant = d.Site == ProdSite.Command || d.Days <= 0;
                if (!instant && room <= 0) continue;
                if (!Order(owner, d, b)) continue;
                orders++;
                if (!instant) room--;
            }
            if (orders > 0 && owner == 0) UIManager.Instance?.RefreshResourceBarPublic();
            return orders > 0;
        }

        /// <summary>Сколько ещё заказов поместится в очередь: не больше двух на линию производства.</summary>
        private int QueueRoom(int owner, Base b)
        {
            int n = 0;
            foreach (var j in _jobs) if (j.Owner == owner) n++;
            return Mathf.Max(0, b.Lines * 2 - n);
        }

        /// <summary>Почему карта из состава сейчас не заказывается (для подсказки в Арсенале; null — всё в порядке).</summary>
        public string WaitReason(int owner, CardDef d)
        {
            if (Count(owner, d.Id) + Queued(owner, d.Id) >= Target(owner, d.Id)) return null;
            var b = BaseOf(owner);
            string s = ProduceBlocker(owner, d, b);
            if (s != null) return s;
            if (d.Site != ProdSite.Command && d.Days > 0 && QueueRoom(owner, b) <= 0)
                return Loc.T("Ждёт места в очереди: заказов не больше двух на линию производства");
            var bud = BudgetOf(owner);
            if (bud == ArmyBudget.Stop) return Loc.T("Военный бюджет остановлен — армия не пополняется");
            if (!FundCovers(owner, d))
            {
                if (bud == ArmyBudget.Mobilize) return Loc.T("Ждёт ресурсов: при мобилизации на заказ уходит не больше четверти запасов");
                if (!Affordable(owner, d)) return Loc.T("Ждёт ресурсов: столько нет на складах");
                return Loc.F("Ждёт военного бюджета: {0}", FundLack(owner, d));
            }
            return null;
        }

        // ==================== КОЛОДЫ ШТУРМА ====================

        private List<CardInstance> Ready(int owner)
        {
            var l = new List<CardInstance>();
            foreach (var c in _cards) if (c.Owner == owner && !c.Wounded && c.D != null) l.Add(c);
            l.Sort((x, y) => Score(y).CompareTo(Score(x)));
            return l;
        }

        /// <summary>Десантных мест у военных кораблей стороны у системы.</summary>
        public static int LandingPlaces(int owner, int systemId)
        {
            int n = 0;
            var fm = FleetManager.Instance;
            if (fm == null) return 0;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d != null && !d.Destroyed && d.OwnerId == owner && d.CurrentSystemId == systemId && d.State != FleetState.InHyperlane) n += Capacity(d);
            }
            return n;
        }

        /// <summary>Сколько приказов берёт десант: до двух на военный корабль, не больше 6.</summary>
        public static int OrderCap(int owner, int systemId) => Mathf.Min(6, Warships(owner, systemId) * 2);

        /// <summary>Карты, которые могут лететь на штурм: готовые (не раненые), не оборонительные, сильнейшие первыми.</summary>
        public List<CardInstance> AttackPool(int owner)
        {
            var l = Ready(owner);
            l.RemoveAll(c => c.DefenseOnly);
            return l;
        }

        /// <summary>
        /// Десант атакующего: лучшие готовые карты резерва, сколько поместится на военные корабли у системы.
        /// Приказы мест не занимают — до двух на корабль (не больше 6). Оборонительные карты не летают.
        /// </summary>
        public List<CardInstance> AttackDeck(int owner, int systemId) => PickDeck(owner, systemId, null);

        /// <summary>
        /// Десант по выбору игрока: карты из <paramref name="ids"/> в их порядке, сколько разрешают места и лимит приказов
        /// (лишнее и недоступное отбрасывается). ids == null — лучшие карты автоматически.
        /// </summary>
        public List<CardInstance> PickDeck(int owner, int systemId, IList<long> ids)
        {
            var l = new List<CardInstance>();
            int places = LandingPlaces(owner, systemId);
            if (places <= 0) return l;
            int orderCap = OrderCap(owner, systemId);
            var pool = AttackPool(owner);
            if (ids != null)
            {
                var byId = new Dictionary<long, CardInstance>();
                foreach (var c in pool) byId[c.Id] = c;
                pool.Clear();
                foreach (var id in ids) if (byId.TryGetValue(id, out var c)) { pool.Add(c); byId.Remove(id); }
            }
            int used = 0, orders = 0;
            foreach (var c in pool)
            {
                if (l.Count >= MaxDeck) break;
                if (c.D.Size <= 0) { if (orders < orderCap) { orders++; l.Add(c); } continue; }
                if (used + c.D.Size > places) continue;
                used += c.D.Size;
                l.Add(c);
            }
            return l;
        }

        /// <summary>
        /// Десант корабля для подсказок: места и сколько готовых карт в резерве империи. null — у корабля нет мест.
        /// </summary>
        public static string TroopSummary(FleetData d, bool header = true)
        {
            var am = Instance;
            if (am == null || d == null) return null;
            int cap = Capacity(d);
            if (cap <= 0) return null;
            int ready = 0;
            foreach (var c in am._cards) if (c.Owner == d.OwnerId && !c.Wounded && !c.DefenseOnly) ready++;
            var sb = new System.Text.StringBuilder();
            if (header) sb.Append(Loc.F("<color=#F2C766><b>Десант</b> · мест {0}</color>\n", cap));
            sb.Append(Loc.F("<color=#8AA2A8>При штурме корабль сам берёт лучшие карты из резерва империи (готово: {0}). Погрузка не нужна</color>", ready));
            return sb.ToString();
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

        /// <summary>Сколько карт резерва вмещает оборона колонии: 3 + жители/4 + 2 за уровень укреплений (до 14).</summary>
        public static int DefensePlaces(StarSystem sys)
            => sys == null ? 0 : Mathf.Clamp(3 + SystemPopulation(sys) / 4 + sys.Fortification * 2, 3, 14);

        /// <summary>
        /// Оборона колонии: из резерва империи сперва оборонительные карты (сооружений — до уровня укреплений + 1,
        /// мин — до 3), затем лучшие отряды, сколько вместит колония; приказов — до 3 сверху.
        /// </summary>
        public List<CardInstance> DefenseCards(int owner, int systemId)
        {
            var l = new List<CardInstance>();
            var sys = EmpireStats.GetSystem(systemId);
            int places = DefensePlaces(sys);
            if (places <= 0) return l;
            var pool = Ready(owner);
            pool.Sort((x, y) => { int c = y.DefenseOnly.CompareTo(x.DefenseOnly); return c != 0 ? c : Score(y).CompareTo(Score(x)); });
            int structCap = sys.Fortification + 1, structs = 0, traps = 0, orders = 0, used = 0;
            foreach (var c in pool)
            {
                var d = c.D;
                if (d.Kind == CardKind.Order) { if (orders < 3) { orders++; l.Add(c); } continue; }
                if (used >= places) continue;
                if (d.Kind == CardKind.Structure) { if (structs >= structCap) continue; structs++; }
                else if (d.Kind == CardKind.Trap) { if (traps >= 3) continue; traps++; }
                used++;
                l.Add(c);
            }
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
                // Колония пала: сооружения и мины остаются врагу (уничтожены), уцелевшие отряды отступают ранеными
                if (r.AttackerWon && c.Owner == defender)
                {
                    if (c.DefenseOnly && c.D.Kind != CardKind.Unit) _cards.Remove(c);
                    else c.WoundDays = Mathf.Max(c.WoundDays, WoundDaysAfterBattle);
                }
            }
            // Трофей: уцелевшая техника защитника переходит в резерв победителя
            if (r.AttackerWon && r.TrophyInst >= 0)
            {
                var t = Get(r.TrophyInst);
                if (t != null) { t.Owner = attacker; t.Stars = 0; t.WoundDays = 0; }
            }
            Changed();
        }

        // ==================== КАЛЕНДАРЬ ====================

        /// <summary>Готовые за день карты (для одного уведомления игроку).</summary>
        private readonly List<(int owner, string name)> _ready = new List<(int, string)>();

        private void HandleDay(int day, int month, int year)
        {
            if (!UIManager.IsGameStarted) return;
            bool changed = false;

            // Раны заживают
            foreach (var c in _cards) if (c.WoundDays > 0) { c.WoundDays--; if (c.WoundDays == 0) changed = true; }

            // Новая партия: стартовые отряды каждой империи
            if (!_starterDone) { GiveStarters(); changed = true; }

            // Военный бюджет: дневная доля дохода — в фонд армии
            AccrueFund(0);
            foreach (var ai in AIEmpireManager.Alive) AccrueFund(ai.OwnerId);

            // Производство: у каждой империи идут первые N заказов очереди (N — линии производства)
            if (_jobs.Count > 0)
            {
                var lines = new Dictionary<int, int>();
                for (int i = 0; i < _jobs.Count; i++)
                {
                    var j = _jobs[i];
                    if (!lines.TryGetValue(j.Owner, out int free)) free = BaseOf(j.Owner).Lines;
                    if (free <= 0) { lines[j.Owner] = 0; continue; }
                    lines[j.Owner] = free - 1;
                    j.DaysLeft -= 1f;
                    changed = true;
                }
                for (int i = _jobs.Count - 1; i >= 0; i--)
                {
                    var j = _jobs[i];
                    if (j.DaysLeft > 0f) continue;
                    _jobs.RemoveAt(i);
                    var d = CardCatalog.Get(j.Def);
                    if (d == null) continue;
                    Create(j.Owner, d.Id);
                    _ready.Add((j.Owner, d.Name));
                }
            }

            // Автосостав пересматривается раз в месяц: открылись технологии, появились районы
            // Новая партия: состав ещё не подобран — подобрать сразу, не дожидаясь начала месяца
            if (!PlayerManual && !_targets.ContainsKey(0)) ApplyAutoDoctrine(0, PlayerSize);
            foreach (var ai in AIEmpireManager.Alive) if (!_targets.ContainsKey(ai.OwnerId)) ThinkAI(ai);
            if (day == 1)
            {
                if (!PlayerManual) ApplyAutoDoctrine(0, PlayerSize);
                foreach (var ai in AIEmpireManager.Alive) ThinkAI(ai);
                changed = true;
            }
            if (day % ReplenishEveryDays == 0)
            {
                if (Replenish(0)) changed = true;
                foreach (var ai in AIEmpireManager.Alive) if (Replenish(ai.OwnerId)) changed = true;
            }

            if (_ready.Count > 0)
            {
                var names = new List<string>();
                foreach (var (o, n) in _ready) if (o == 0) names.Add(n);
                _ready.Clear();
                if (names.Count > 0)
                    NotificationCenter.Show(Loc.T("Пополнение армии"), string.Join(", ", names), NotificationCenter.Kind.Success, 3.5f);
                changed = true;
            }
            if (changed) Changed();
        }

        // ==================== СТАРТ ====================

        /// <summary>Стартовый отряд: 3 штурмовые пехоты и гарнизон — чтобы первые месяцы было чем встретить десант.</summary>
        private void GiveStarters()
        {
            if (EmpireStats.Systems == null || EmpireStats.Systems.Count == 0) return;
            _starterDone = true;
            var owners = new List<int> { 0 };
            foreach (var ai in AIEmpireManager.Alive) owners.Add(ai.OwnerId);
            foreach (int o in owners)
            {
                bool any = false;
                foreach (var c in _cards) if (c.Owner == o) { any = true; break; }
                foreach (var j in _jobs) if (j.Owner == o) { any = true; break; }
                if (any) continue;
                for (int i = 0; i < 3; i++) Create(o, "U1");
                Create(o, "D2");
            }
        }

        // ==================== ИИ ====================

        /// <summary>ИИ раз в месяц: в войне или при подготовке к ней держит армию больше. Состав — тот же автосостав, что у игрока.</summary>
        private void ThinkAI(AIEmpireManager ai)
        {
            if (ai == null || ai.IsEliminated) return;
            bool war = ai.AtWar || ai.Mode == AIEmpireManager.ArmyMode.Attack;
            int size = ai.MonthlyEnergyIncome < 3f ? Mathf.Min(8, TargetTotal(ai.OwnerId)) : war ? 24 : 12;
            _budget[ai.OwnerId] = war ? ArmyBudget.War : ArmyBudget.Normal;
            ApplyAutoDoctrine(ai.OwnerId, size);
        }

        // ==================== СОХРАНЕНИЕ ====================

        public ArmySave CaptureState()
        {
            var s = new ArmySave { NextId = _nextId, PlayerManual = PlayerManual, PlayerSize = PlayerSize, PlayerBudget = (int)PlayerBudget, StarterDone = _starterDone };
            foreach (var kv in _fund) s.Funds.Add(new ArmyFund { Owner = kv.Key, Energy = kv.Value[0], Minerals = kv.Value[1], Alloys = kv.Value[2], Influence = kv.Value[3] });
            s.Cards.AddRange(_cards);
            s.Jobs.AddRange(_jobs);
            foreach (var kv in _targets)
                foreach (var t in kv.Value) s.Targets.Add(new ArmyTarget { Owner = kv.Key, Def = t.Key, Count = t.Value });
            return s;
        }

        public void RestoreState(ArmySave s)
        {
            _cards.Clear();
            _jobs.Clear();
            _targets.Clear();
            _ready.Clear();
            _fund.Clear();
            _budget.Clear();
            _starterDone = false;
            _nextId = 1;
            PlayerManual = false;
            PlayerSize = DefaultSize;
            if (s == null) { Changed(); return; }
            _starterDone = s.StarterDone;
            _budget[0] = Enum.IsDefined(typeof(ArmyBudget), s.PlayerBudget) ? (ArmyBudget)s.PlayerBudget : ArmyBudget.Normal;
            if (s.Funds != null) foreach (var f in s.Funds) if (f != null) _fund[f.Owner] = new[] { f.Energy, f.Minerals, f.Alloys, f.Influence };
            foreach (var c in s.Cards)
            {
                if (c == null || !CardCatalog.Exists(c.Def)) continue;
                // Старые сохранения: карты на кораблях и колониях — в общий резерв
                c.Fleet = -1; c.System = -1; c.Garrison = false;
                _cards.Add(c);
            }
            if (s.Jobs != null) foreach (var j in s.Jobs) if (j != null && CardCatalog.Exists(j.Def)) _jobs.Add(j);
            bool anyPlayerTarget = false;
            if (s.Targets != null)
                foreach (var t in s.Targets)
                    if (t != null && CardCatalog.Exists(t.Def) && t.Count > 0) { SetTargetRaw(t.Owner, t.Def, t.Count); if (t.Owner == 0) anyPlayerTarget = true; }
            PlayerManual = s.PlayerManual && anyPlayerTarget;
            PlayerSize = s.PlayerSize > 0 ? Mathf.Clamp(s.PlayerSize, MinSize, MaxSize) : DefaultSize;
            _nextId = Math.Max(s.NextId, 1);
            foreach (var c in _cards) if (c.Id >= _nextId) _nextId = c.Id + 1;
            Changed();
        }
    }
}
