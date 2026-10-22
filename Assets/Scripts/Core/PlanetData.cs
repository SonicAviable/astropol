using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public enum PlanetType { Continental, Desert, Molten, GasGiant, Barren }

    public enum DistrictType { Urban, Mining, Generator, Industrial }

    /// <summary>Приоритет колонии: куда жители идут работать в первую очередь и что строит автостройка.</summary>
    public enum ColonyFocus { Balanced, Minerals, Energy, Industry }

    public static class ColonyFocusInfo
    {
        public static readonly ColonyFocus[] All = { ColonyFocus.Balanced, ColonyFocus.Minerals, ColonyFocus.Energy, ColonyFocus.Industry };

        public static string Name(ColonyFocus f) => f switch
        {
            ColonyFocus.Minerals => Loc.T("Добыча титана"),
            ColonyFocus.Energy   => Loc.T("Энергетика"),
            ColonyFocus.Industry => Loc.T("Промышленность"),
            _ => Loc.T("Равномерно")
        };

        public static string Desc(ColonyFocus f) => f switch
        {
            ColonyFocus.Minerals => Loc.T("Жители сначала идут в шахты; автостройка ставит горнодобывающие районы"),
            ColonyFocus.Energy   => Loc.T("Жители сначала идут на генераторы; автостройка ставит энергорайоны"),
            ColonyFocus.Industry => Loc.T("Жители сначала идут на заводы; автостройка ставит промышленные районы"),
            _ => Loc.T("Рабочие места заполняются по порядку; автостройка держит районы в равновесии")
        };

        /// <summary>Район, которому приоритет отдаёт первые рабочие руки (null — равномерно).</summary>
        public static DistrictType? District(ColonyFocus f) => f switch
        {
            ColonyFocus.Minerals => DistrictType.Mining,
            ColonyFocus.Energy   => DistrictType.Generator,
            ColonyFocus.Industry => DistrictType.Industrial,
            _ => (DistrictType?)null
        };
    }

    public static class DistrictInfo
    {
        /// <summary>Место работы жителей района (для подсказок «кто где работает»).</summary>
        public static string JobPlace(DistrictType t) => t switch
        {
            DistrictType.Urban      => Loc.T("служащие"),
            DistrictType.Mining     => Loc.T("шахты"),
            DistrictType.Generator  => Loc.T("электростанции"),
            DistrictType.Industrial => Loc.T("заводы"),
            _ => "?"
        };

        public static string Name(DistrictType t) => t switch
        {
            DistrictType.Urban      => Loc.T("ГОРОДСКОЙ РАЙОН"),
            DistrictType.Mining     => Loc.T("ГОРНОДОБЫВАЮЩИЙ"),
            DistrictType.Generator  => Loc.T("ЭНЕРГОГЕНЕРАЦИЯ"),
            DistrictType.Industrial => Loc.T("ПРОМЫШЛЕННЫЙ"),
            _ => "?"
        };

        public static string ShortDesc(DistrictType t) => t switch
        {
            DistrictType.Urban      => Loc.T("+2 жилья, +1 место служащего (+1 наука)"),
            DistrictType.Mining     => Loc.T("2 рабочих места · +4 титана за каждое"),
            DistrictType.Generator  => Loc.T("2 рабочих места · +4 гелия-3 за каждое"),
            DistrictType.Industrial => Loc.T("2 рабочих места · +3 сплава за каждое"),
            _ => ""
        };

        public static int HousingPopCapacity(DistrictType t) => t == DistrictType.Urban ? 2 : 0;

        public static int MineralsCost(DistrictType t) => t switch
        {
            DistrictType.Urban      => 60,
            DistrictType.Mining     => 80,
            DistrictType.Generator  => 70,
            DistrictType.Industrial => 120,
            _ => 0
        };

        public static int AlloysCost(DistrictType t) => t switch
        {
            DistrictType.Urban      => 20,
            DistrictType.Mining     => 10,
            DistrictType.Generator  => 25,
            DistrictType.Industrial => 60,
            _ => 0
        };

        public static Color Color(DistrictType t) => t switch
        {
            DistrictType.Urban      => new Color(0.45f, 0.75f, 0.95f),
            DistrictType.Mining     => new Color(0.55f, 0.95f, 0.75f),
            DistrictType.Generator  => new Color(0.95f, 0.80f, 0.35f),
            DistrictType.Industrial => new Color(0.90f, 0.55f, 0.45f),
            _ => UnityEngine.Color.gray
        };
    }

    [Serializable]
    public class DistrictData
    {
        public DistrictType Type;
        public DistrictData(DistrictType t) { Type = t; }
    }

    [Serializable]
    public partial class PlanetData
    {
        public string Name;
        public PlanetType Type;
        public float OrbitRadius;
        public float OrbitSpeed;
        public float Size;
        public Color PlanetColor;
        public int EnergyDeposit;
        public int MineralDeposit;
        public bool HasMiningStation;
        public bool TerraformingInProgress;
        public int Population;
        public float PopGrowthProgress;

        public int MaxDistricts;
        public List<DistrictData> Districts = new List<DistrictData>();

        /// <summary>Приоритет колонии (распределение рабочих и выбор автостройки).</summary>
        public ColonyFocus Focus;
        /// <summary>Автостройка: когда очередь пуста, колония сама заказывает нужный район.</summary>
        public bool AutoBuild;

        /// <summary>Система, в которой находится планета (проставляется при генерации/загрузке).</summary>
        [NonSerialized] public StarSystem ParentSystem;

        /// <summary>Владелец планеты = владелец системы (-1 — ничья).</summary>
        public int OwnerId => ParentSystem != null ? ParentSystem.OwnerId : -1;
        public bool IsPlayerOwned => OwnerId == 0;

        public int BuiltDistricts => Districts.Count;

        public int HousingCapacity
        {
            get
            {
                int cap = 0;
                foreach (var d in Districts) cap += DistrictInfo.HousingPopCapacity(d.Type);
                return cap;
            }
        }

        /// <summary>Рабочих мест в производственном районе (добыча, энергия, заводы); городской даёт одно — служащие.</summary>
        public const int JobsPerDistrict = 2;

        public static int JobsPer(DistrictType t) => t == DistrictType.Urban ? 1 : JobsPerDistrict;

        /// <summary>Рабочих мест районов этого вида.</summary>
        public int JobsOf(DistrictType t) => GetDistrictCount(t) * JobsPer(t);

        public int WorkerSlots
        {
            get { int n = 0; foreach (var d in Districts) n += JobsPer(d.Type); return n; }
        }
        public int EmployedPops
        {
            get { var w = Distribute(); return w[0] + w[1] + w[2] + w[3]; }
        }
        public int IdlePops => Mathf.Max(0, Population - EmployedPops);

        /// <summary>
        /// Ручное распределение жителей (перетаскивание на экране колонии): сколько работает в районах
        /// каждого вида, по индексу DistrictType; null — распределяет приоритет колонии.
        /// </summary>
        public List<int> JobPlan;
        public bool HasJobPlan => JobPlan != null && JobPlan.Count == 4;

        public PlanetData(string name, PlanetType type, float orbitRadius, float orbitSpeed,
                          float size, Color color, int energy, int minerals)
        {
            Name = name; Type = type; OrbitRadius = orbitRadius; OrbitSpeed = orbitSpeed;
            Size = size; PlanetColor = color; EnergyDeposit = energy; MineralDeposit = minerals;

            MaxDistricts = Mathf.Clamp(Mathf.RoundToInt(size * 5f), 4, 25);

            Population = 0;
            PopGrowthProgress = 0f;
        }

        /// <summary>Стартовая колония в столице.</summary>
        public void SetupAsStartingColony(int pop, int urban, int mining, int generator, int industrial = 0)
        {
            Pops?.Clear();
            JobPlan = null;
            Stability = 65f;
            Population = Mathf.Clamp(pop, 0, MaxDistricts * 2);
            PopGrowthProgress = 0f;

            Districts.Clear();
            for (int i = 0; i < urban; i++)      Districts.Add(new DistrictData(DistrictType.Urban));
            for (int i = 0; i < mining; i++)     Districts.Add(new DistrictData(DistrictType.Mining));
            for (int i = 0; i < generator; i++)  Districts.Add(new DistrictData(DistrictType.Generator));
            for (int i = 0; i < industrial; i++) Districts.Add(new DistrictData(DistrictType.Industrial));
        }

        public int GetDistrictCount(DistrictType t)
        {
            int n = 0;
            foreach (var d in Districts) if (d.Type == t) n++;
            return n;
        }

        // ==================== РАСПРЕДЕЛЕНИЕ РАБОЧИХ ====================

        private static readonly DistrictType[] ProductionTypes = { DistrictType.Mining, DistrictType.Generator, DistrictType.Industrial };

        /// <summary>
        /// Сколько жителей работает в районах каждого вида (индекс — DistrictType).
        /// Сначала — ручной план (если игрок расставил жителей сам), остальные — по приоритету:
        /// район приоритета заполняется первым, прочие производственные — по очереди поровну,
        /// городские места служащих — последними.
        /// </summary>
        public int[] Distribute()
        {
            var cap = new int[4];
            foreach (var d in Districts) cap[(int)d.Type] += JobsPer(d.Type);
            var w = new int[4];
            int pool = Mathf.Max(0, Population);

            if (HasJobPlan)
                for (int t = 0; t < 4; t++)
                {
                    int want = JobPlan[t];
                    if (want <= 0) continue;
                    int n = Mathf.Min(want, Mathf.Min(cap[t], pool));
                    w[t] = n; pool -= n;
                }
            if (pool <= 0) return w;

            var first = ColonyFocusInfo.District(Focus);
            if (first.HasValue)
            {
                int ft = (int)first.Value;
                int n = Mathf.Min(cap[ft] - w[ft], pool);
                w[ft] += n; pool -= n;
            }
            // Остальные производственные — по одному по кругу (поровну)
            bool any = true;
            while (pool > 0 && any)
            {
                any = false;
                foreach (var t in ProductionTypes)
                {
                    int i = (int)t;
                    if (pool <= 0 || w[i] >= cap[i]) continue;
                    w[i]++; pool--; any = true;
                }
            }
            int u = (int)DistrictType.Urban;
            int nu = Mathf.Min(cap[u] - w[u], pool);
            w[u] += nu;
            return w;
        }

        /// <summary>Сколько жителей работает в районах данного вида.</summary>
        public int WorkedCount(DistrictType t) => Distribute()[(int)t];

        /// <summary>Перевести одного жителя с работы одного вида на другую (если там есть свободное место).</summary>
        public bool MoveWorker(DistrictType from, DistrictType to)
        {
            if (from == to) return false;
            var w = Distribute();
            int f = (int)from, t = (int)to;
            if (w[f] <= 0 || w[t] >= JobsOf(to)) return false;
            w[f]--; w[t]++;
            JobPlan = new List<int>(w);
            return true;
        }

        /// <summary>Вернуть распределение приоритету колонии.</summary>
        public void ClearJobPlan() => JobPlan = null;

        public int ProducedMineralsPerMonth
        {
            get
            {
                return Distribute()[(int)DistrictType.Mining] * 4;
            }
        }

        public int ProducedEnergyPerMonth
        {
            get
            {
                return Distribute()[(int)DistrictType.Generator] * 4;
            }
        }

        public int ProducedAlloysPerMonth
        {
            get
            {
                return Distribute()[(int)DistrictType.Industrial] * 3;
            }
        }

        // ==================== СТРОИТЕЛЬСТВО ====================

        public bool CanBuildDistrict(DistrictType t)
        {
            if (!IsPlayerOwned) return false;
            if (BuiltDistricts + PendingDistricts >= MaxDistricts) return false;
            if (Builds != null && Builds.QueueFor(this).Count >= ConstructionManager.MaxPlanetQueue) return false;
            var eco = EconomyManager.Instance;
            if (eco == null) return false;
            return eco.Minerals >= DistrictInfo.MineralsCost(t) && eco.Alloys >= DistrictInfo.AlloysCost(t);
        }

        public bool TryBuildDistrict(DistrictType t)
        {
            if (!CanBuildDistrict(t)) return false;
            var eco = EconomyManager.Instance;
            if (!eco.TrySpend(0, DistrictInfo.MineralsCost(t), DistrictInfo.AlloysCost(t), 0)) return false;
            if (Builds == null) { Districts.Add(new DistrictData(t)); return true; }
            Builds.EnqueuePlanetJob(JobKind.District, 0, this, t, 0f, DistrictInfo.MineralsCost(t), DistrictInfo.AlloysCost(t), 0f);
            return true;
        }

        /// <summary>
        /// Что заказала бы автостройка сейчас (null — ничего не нужно): тесно — жильё,
        /// есть безработные — рабочие места по приоритету, при «Равномерно» — самого редкого вида.
        /// Учитывает районы, уже стоящие в очереди.
        /// </summary>
        public DistrictType? AutoBuildChoice()
        {
            if (Population <= 0 || BuiltDistricts + PendingDistricts >= MaxDistricts) return null;
            int housing = HousingCapacity, jobs = WorkerSlots;
            if (Builds != null)
                foreach (var j in Builds.QueueFor(this))
                {
                    if (j.Kind != JobKind.District) continue;
                    housing += DistrictInfo.HousingPopCapacity(j.District);
                    jobs += JobsPer(j.District);
                }
            if (Population >= jobs)
            {
                var f = ColonyFocusInfo.District(Focus);
                if (f.HasValue) return f.Value;
                DistrictType best = DistrictType.Mining;
                int bestN = int.MaxValue;
                foreach (var t in new[] { DistrictType.Mining, DistrictType.Generator, DistrictType.Industrial })
                {
                    int n = GetDistrictCount(t) + (Builds != null ? Builds.PendingDistricts(this, t) : 0);
                    if (t == DistrictType.Industrial) n *= 2;   // заводы дороже — их вдвое меньше
                    if (n < bestN) { bestN = n; best = t; }
                }
                return best;
            }
            if (housing - Population <= 1) return DistrictType.Urban;
            return null;
        }

        /// <summary>Скорость роста населения (% к следующему жителю в месяц); 0 — если нет жилья или колонии.</summary>
        public float PopGrowthBaseSpeedPctPerMonth =>
            Population <= 0 || HousingCapacity <= Population ? 0f
            : (4f + (HousingCapacity - Population) * 0.4f) * EmpireBonuses.For(OwnerId).PopGrowth * HabitabilityGrowthMult
              * SpeciesGrowthMult * PlanetStability.GrowthMult(Tier);

        /// <summary>Комфортный мир (пригодность от 70% — континентальный) растёт быстрее: это и даёт последний шаг терраформинга.</summary>
        public const float ComfortGrowthBonus = 1.25f;
        public const int ComfortHabitability = 70;
        public float HabitabilityGrowthMult => HabitabilityPercent >= ComfortHabitability ? ComfortGrowthBonus : 1f;

        /// <summary>Сколько месяцев до следующего жителя (-1 — рост остановлен).</summary>
        public int MonthsToNextPop
        {
            get
            {
                float speed = PopGrowthBaseSpeedPctPerMonth;
                if (speed <= 0f) return -1;
                return Mathf.Max(1, Mathf.CeilToInt((100f - PopGrowthProgress) / speed));
            }
        }

        public int PlanetSize => MaxDistricts;

        public int HabitabilityPercent => HabitabilityOf(Type);

        public string ClassDisplayName => Type switch
        {
            PlanetType.Continental => Loc.T("Континентальный мир"),
            PlanetType.Desert      => Loc.T("Пустынный мир"),
            PlanetType.Molten      => Loc.T("Расплавленный мир"),
            PlanetType.GasGiant    => Loc.T("Газовый гигант"),
            PlanetType.Barren      => Loc.T("Бесплодный мир"),
            _ => Loc.T("Неклассифицированный объект")
        };

        public bool CanColonize => HabitabilityPercent >= 40 && Population <= 0
            && Type != PlanetType.GasGiant && Type != PlanetType.Molten;

        public bool CanTerraform => Type == PlanetType.Barren || Type == PlanetType.Molten || Type == PlanetType.Desert;

        public const float ColonyMineralsCost = 80f;
        public const float ColonyAlloysCost = 20f;
        public const float ColonyInfluenceCost = 25f;

        public const float TerraformEnergyCost = 120f;
        public const float TerraformMineralsCost = 90f;
        public const float TerraformInfluenceCost = 10f;

        private static ConstructionManager Builds => ConstructionManager.Instance;

        /// <summary>Колонисты уже в пути (заказ в очереди строительства).</summary>
        public bool IsColonizing => Builds != null && Builds.HasJob(this, JobKind.Colony);
        public bool StationUnderConstruction => Builds != null && Builds.HasJob(this, JobKind.MiningStation);
        public int PendingDistricts => Builds != null ? Builds.PendingDistricts(this) : 0;

        /// <summary>Заказать колонию: ресурсы сразу, колонисты прибудут через ColonyDays.</summary>
        public bool TryFoundColony()
        {
            if (!CanColonize || !IsPlayerOwned || IsColonizing || Builds == null) return false;
            var eco = EconomyManager.Instance;
            if (eco == null || !eco.CanAfford(0f, ColonyMineralsCost, ColonyAlloysCost, ColonyInfluenceCost)) return false;
            if (!eco.TrySpend(0f, ColonyMineralsCost, ColonyAlloysCost, ColonyInfluenceCost)) return false;
            Builds.EnqueuePlanetJob(JobKind.Colony, 0, this, DistrictType.Urban, 0f, ColonyMineralsCost, ColonyAlloysCost, ColonyInfluenceCost);
            return true;
        }

        /// <summary>Заселить планету (без оплаты — её проверяет вызывающий).</summary>
        public void SettleColony()
        {
            Pops?.Clear();
            JobPlan = null;
            Stability = 55f; Unrest = 0f; RiotMonths = 0;
            MigrantsIn = MigrantsOut = 0;
            // Колонисты — вид империи, которому подходит климат этого мира (Migration.ColonistSpecies)
            int colonists = Migration.ColonistSpecies(OwnerId, Type, out float integration);
            Population = 2;
            Pops ??= new List<PopGroup>();
            Pops.Add(new PopGroup(colonists, 2) { Integration = integration });
            PopGrowthProgress = 0f;
            if (Districts.Count == 0)
            {
                Districts.Add(new DistrictData(DistrictType.Urban));
                Districts.Add(new DistrictData(DistrictType.Mining));
            }
        }

        public bool TryStartTerraform()
        {
            if (!CanTerraform || TerraformingInProgress || !IsPlayerOwned || Builds == null) return false;
            var eco = EconomyManager.Instance;
            if (eco == null || !eco.CanAfford(TerraformEnergyCost, TerraformMineralsCost, 0f, TerraformInfluenceCost)) return false;
            if (!eco.TrySpend(TerraformEnergyCost, TerraformMineralsCost, 0f, TerraformInfluenceCost)) return false;
            Builds.EnqueuePlanetJob(JobKind.Terraform, 0, this, DistrictType.Urban,
                                    TerraformEnergyCost, TerraformMineralsCost, 0f, TerraformInfluenceCost);
            return true;
        }

        /// <summary>Класс мира после следующего шага терраформинга (null — шаг невозможен).</summary>
        public PlanetType? TerraformNextType => Type switch
        {
            PlanetType.Molten => PlanetType.Barren,
            PlanetType.Barren => PlanetType.Desert,
            PlanetType.Desert => PlanetType.Continental,
            _ => (PlanetType?)null
        };

        /// <summary>Пригодность мира данного класса — та же таблица, что в HabitabilityPercent.</summary>
        public static int HabitabilityOf(PlanetType t) => t switch
        {
            PlanetType.Continental => 82,
            PlanetType.Desert      => 54,
            PlanetType.Barren      => 18,
            PlanetType.Molten      => 0,
            PlanetType.GasGiant    => 0,
            _ => 30
        };

        public static string ClassName(PlanetType t) => t switch
        {
            PlanetType.Continental => Loc.T("континентальный"),
            PlanetType.Desert      => Loc.T("пустынный"),
            PlanetType.Molten      => Loc.T("расплавленный"),
            PlanetType.GasGiant    => Loc.T("газовый гигант"),
            PlanetType.Barren      => Loc.T("бесплодный"),
            _ => "?"
        };

        /// <summary>Сколько шагов терраформинга осталось до мира, пригодного для колонии (0 — уже пригоден, -1 — никогда).</summary>
        public int TerraformStepsToColonize
        {
            get
            {
                if (HabitabilityPercent >= 40) return 0;
                int steps = 0;
                var t = Type;
                while (HabitabilityOf(t) < 40)
                {
                    PlanetType? n = t switch
                    {
                        PlanetType.Molten => PlanetType.Barren,
                        PlanetType.Barren => PlanetType.Desert,
                        _ => (PlanetType?)null
                    };
                    if (!n.HasValue) return -1;
                    t = n.Value;
                    steps++;
                }
                return steps;
            }
        }

        /// <summary>Доход колонии при основании: 2 жителя, городской и горнодобывающий районы — работает добыча титана.</summary>
        public const int ColonyStartMineralsPerMonth = 4;

        /// <summary>
        /// Что даст колония и выгодна ли она: сразу, потенциал планеты, цена и окупаемость. Для подсказок.
        /// </summary>
        public string ColonyBenefitText()
        {
            int size = MaxDistricts;
            int payback = Mathf.CeilToInt(ColonyMineralsCost / ColonyStartMineralsPerMonth);
            // Потенциал: примерно треть районов — жильё, остальные — добыча (+4 в среднем)
            int productive = Mathf.Max(1, size - Mathf.CeilToInt(size / 3f));
            int potential = productive * 4;
            return
                Loc.T("<color=#8AA2A8>Сразу после основания:</color>\n") +
                Loc.T("  • 2 жителя, городской и горнодобывающий районы\n") +
                Loc.F("  • <color=#4DF2DB>+{0} титана/мес</color>\n", ColonyStartMineralsPerMonth) +
                Loc.F("  • +{0} к лимиту флота\n", EmpireEconomy.NavalCapacityPerColony) +
                Loc.T("<color=#8AA2A8>Дальше:</color>\n") +
                Loc.F("  • место под {0} районов; каждый рабочий район добычи даёт +4 титана или гелия-3/мес, завод +3 сплава\n", size) +
                Loc.F("  • при полной застройке ~<color=#4DF2DB>+{0}/мес</color> ресурсов\n", potential) +
                (HabitabilityPercent >= ComfortHabitability ? Loc.F("  • комфортный мир: рост населения +{0:0}%\n", (ComfortGrowthBonus - 1f) * 100f) : "") +
                Loc.T("<color=#8AA2A8>Цена:</color> ") +
                Loc.F("{0:0} титана, {1:0} сплавов, {2:0} влияния · {3:0} дн.\n", ColonyMineralsCost, ColonyAlloysCost, ColonyInfluenceCost, ConstructionManager.ColonyDays) +
                Loc.F("<color=#8AA2A8>Окупаемость по титану:</color> ~{0} мес., потом чистая прибыль", payback);
        }

        /// <summary>Что даст следующий шаг терраформинга — для подсказок.</summary>
        public string TerraformBenefitText()
        {
            var next = TerraformNextType;
            if (!next.HasValue) return Loc.T("Этот мир терраформировать нельзя.");
            int habNow = HabitabilityPercent, habNext = HabitabilityOf(next.Value);
            string head = Loc.F("{0} → <b>{1}</b>  ·  пригодность {2}% → <b>{3}%</b>\n", ClassName(Type), ClassName(next.Value), habNow, habNext);
            string price = Loc.F("\n<color=#8AA2A8>Цена шага:</color> {0:0} гелия-3, {1:0} титана, {2:0} влияния · {3:0} дн.", TerraformEnergyCost, TerraformMineralsCost, TerraformInfluenceCost, ConstructionManager.TerraformDays);
            string what;
            if (habNow < 40 && habNext >= 40)
                what = Loc.F("<color=#5CE68A>Откроет колонизацию:</color> место под {0} районов, колония сразу даст +{1} титана/мес.", MaxDistricts, ColonyStartMineralsPerMonth);
            else if (habNext < 40)
            {
                int left = TerraformStepsToColonize;
                what = Loc.F("<color=#FFB070>Сам по себе колонию не откроет.</color> До пригодного мира шагов: {0} ", left) +
                       Loc.F("(всего ~{0:0} дн. и {1:0} титана). Выгодно, если планета большая ({2} районов).", left * ConstructionManager.TerraformDays, left * TerraformMineralsCost, MaxDistricts);
            }
            else if (habNext >= ComfortHabitability)
                what = Loc.F("<color=#5CE68A>Комфортный мир:</color> рост населения <b>+{0:0}%</b> — колония быстрее заполнит районы.", (ComfortGrowthBonus - 1f) * 100f) +
                       (Population > 0 ? "" : Loc.T("\nКолонию можно основать и без этого шага."));
            else
                what = Loc.T("Пригодность вырастет.");
            return head + what + price;
        }

        /// <summary>Шаг терраформинга (по завершении заказа): расплавленный → бесплодный → пустынный → континентальный.</summary>
        public void ApplyTerraformStep()
        {
            if (Type == PlanetType.Molten) Type = PlanetType.Barren;
            else if (Type == PlanetType.Barren) Type = PlanetType.Desert;
            else if (Type == PlanetType.Desert) Type = PlanetType.Continental;
            TerraformingInProgress = false;
            if (Type == PlanetType.Continental)
                PlanetColor = new Color(0.2f, 0.7f, 0.3f);
            else if (Type == PlanetType.Desert)
                PlanetColor = new Color(0.85f, 0.75f, 0.35f);
            else
                PlanetColor = new Color(0.5f, 0.5f, 0.55f);
        }
    }
}