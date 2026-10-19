using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public enum TechCategory
    {
        Weapons, Defense, Propulsion, Sensors,
        Industry, Reactor, Construction, Society,
        Colonization, Doctrine
    }

    /// <summary>Три ветки науки, как в Stellaris: каждая — несколько направлений (категорий).</summary>
    public enum TechBranch { Physics, Society, Engineering }

    public static class TechBranchInfo
    {
        public static readonly TechBranch[] All = { TechBranch.Physics, TechBranch.Society, TechBranch.Engineering };

        public static TechBranch Of(TechCategory c) => c switch
        {
            TechCategory.Reactor or TechCategory.Sensors or TechCategory.Propulsion => TechBranch.Physics,
            TechCategory.Society or TechCategory.Colonization or TechCategory.Doctrine => TechBranch.Society,
            _ => TechBranch.Engineering
        };

        /// <summary>Направления ветки в порядке отображения сверху вниз.</summary>
        public static TechCategory[] Lanes(TechBranch b) => b switch
        {
            TechBranch.Physics => new[] { TechCategory.Reactor, TechCategory.Sensors, TechCategory.Propulsion },
            TechBranch.Society => new[] { TechCategory.Society, TechCategory.Colonization, TechCategory.Doctrine },
            _ => new[] { TechCategory.Weapons, TechCategory.Defense, TechCategory.Industry, TechCategory.Construction }
        };

        public static string Name(TechBranch b) => b switch
        {
            TechBranch.Physics => Loc.T("ФИЗИКА"),
            TechBranch.Society => Loc.T("ОБЩЕСТВО"),
            _ => Loc.T("ИНЖЕНЕРИЯ")
        };

        public static string Motto(TechBranch b) => b switch
        {
            TechBranch.Physics => Loc.T("Энергия, вычисления и движение"),
            TechBranch.Society => Loc.T("Наука, экспансия и доктрина флота"),
            _ => Loc.T("Оружие, броня, промышленность и верфи")
        };

        public static Color Color(TechBranch b) => b switch
        {
            TechBranch.Physics => new Color(0.42f, 0.66f, 1.00f),
            TechBranch.Society => new Color(0.36f, 0.95f, 0.58f),
            _ => new Color(1.00f, 0.68f, 0.30f)
        };

        public static string Key(TechBranch b) => b switch
        {
            TechBranch.Physics => "physics",
            TechBranch.Society => "society",
            _ => "engineering"
        };
    }

    public static class TechCategoryInfo
    {
        public static string Name(TechCategory c) => c switch
        {
            TechCategory.Weapons      => Loc.T("ОРУЖИЕ"),
            TechCategory.Defense      => Loc.T("ЗАЩИТА"),
            TechCategory.Propulsion   => Loc.T("ДВИГАТЕЛИ"),
            TechCategory.Sensors      => Loc.T("СЕНСОРЫ И ИИ"),
            TechCategory.Industry     => Loc.T("ПРОМЫШЛЕННОСТЬ"),
            TechCategory.Reactor      => Loc.T("ЭНЕРГЕТИКА"),
            TechCategory.Construction => Loc.T("ВЕРФИ"),
            TechCategory.Society      => Loc.T("НАУКА И УПРАВЛЕНИЕ"),
            TechCategory.Colonization => Loc.T("ЭКСПАНСИЯ"),
            TechCategory.Doctrine     => Loc.T("ВОЕННАЯ ДОКТРИНА"),
            _ => "?"
        };
        public static string Icon(TechCategory c) => c switch
        {
            TechCategory.Weapons      => "⚔",
            TechCategory.Defense      => "◆",
            TechCategory.Propulsion   => "⌁",
            TechCategory.Sensors      => "◉",
            TechCategory.Industry     => "⚙",
            TechCategory.Reactor      => "⚛",
            TechCategory.Construction => "✦",
            TechCategory.Society      => "★",
            TechCategory.Colonization => "◎",
            TechCategory.Doctrine     => "▲",
            _ => "?"
        };
        public static Color Color(TechCategory c) => c switch
        {
            TechCategory.Weapons      => new Color(1.00f, 0.45f, 0.40f),
            TechCategory.Defense      => new Color(0.55f, 0.75f, 1.00f),
            TechCategory.Propulsion   => new Color(1.00f, 0.75f, 0.30f),
            TechCategory.Sensors      => new Color(0.30f, 0.92f, 1.00f),
            TechCategory.Industry     => new Color(1.00f, 0.62f, 0.25f),
            TechCategory.Reactor      => new Color(0.78f, 0.58f, 1.00f),
            TechCategory.Construction => new Color(0.65f, 0.85f, 0.45f),
            TechCategory.Society      => new Color(0.45f, 1.00f, 0.65f),
            TechCategory.Colonization => new Color(0.40f, 0.90f, 0.85f),
            TechCategory.Doctrine     => new Color(0.95f, 0.85f, 0.45f),
            _ => UnityEngine.Color.white
        };
    }

    [Serializable]
    public class Technology
    {
        public string Id;
        public string Name;
        public TechCategory Category;
        public string Description;
        public int Tier;
        public int GridColumn;
        /// <summary>Базовая трудоёмкость из таблицы (до множителя уровня).</summary>
        public float RawDays;
        /// <summary>Стоимость в очках науки — растёт с уровнем технологии.</summary>
        public float Cost;
        public int YearAvailable;
        public string[] RequiredTechIds;
        public string BonusKey;

        /// <summary>Накопленные очки науки (у игрока).</summary>
        [NonSerialized] public float Progress;
        [NonSerialized] public bool IsResearched;

        public float ProgressNormalized => Mathf.Clamp01(Progress / Mathf.Max(1f, Cost));

        public Technology(string id, string name, TechCategory cat, string desc,
                          int tier, int col, float rawDays,
                          string[] reqs, int yearAvailable, string bonusKey)
        {
            Id = id; Name = name; Category = cat; Description = desc;
            Tier = tier; GridColumn = col; RawDays = rawDays;
            Cost = Mathf.Round(rawDays * TechnologyManager.CostPerRawDay * TechnologyManager.TierCostFactor(tier));
            RequiredTechIds = reqs ?? new string[0];
            YearAvailable = yearAvailable;
            BonusKey = bonusKey;
        }
    }

    public class ResearchSlot
    {
        public Technology CurrentTech;
        public float Progress;
        public bool IsPaused;
        public readonly List<Technology> Queue = new List<Technology>();

        public float ProgressNormalized =>
            CurrentTech == null ? 0f : Mathf.Clamp01(Progress / Mathf.Max(1f, CurrentTech.Cost));
        public float PointsRemaining =>
            CurrentTech == null ? 0f : Mathf.Max(0f, CurrentTech.Cost - Progress);
    }

    public class TechnologyManager : MonoBehaviour
    {
        public static TechnologyManager Instance { get; private set; }

        public static event Action<Technology> OnTechCompleted;
        public static event Action OnTechProgressUpdated;
        public static event Action<int> OnSlotsChanged;

        // ==================== БАЛАНС ====================

        /// <summary>Очков науки на «день» табличной трудоёмкости.</summary>
        public const float CostPerRawDay = 6.0f;   // было 3.3 при 25-летней партии; дерево растянуто на 60 лет
        /// <summary>Наука без населения (администрация, архивы).</summary>
        public const float BaseScience = 20f;
        /// <summary>Наука с каждой единицы населения.</summary>
        public const float SciencePerPop = 1f;
        /// <summary>Штраф за параллельные исследования: скорость слота = доход / N^0.7.</summary>
        public const float SlotSplitExponent = 0.7f;

        private static int s_totalTechs;
        /// <summary>Сколько технологий в дереве всего.</summary>
        public static int TotalTechCount => s_totalTechs > 0 ? s_totalTechs : (s_totalTechs = BuildTechTree().Count);

        /// <summary>
        /// Множитель стоимости по уровню: базовые ×1, Tier 1 ×4.4, Tier 2 ×9, Tier 3 ×13, Tier 4 ×17. Ранние технологии
        /// изучаются за месяцы, поздние — за годы: научная победа достижима ближе к 2250-м.
        /// </summary>
        public static float TierCostFactor(int tier) => tier <= 0 ? 1f : tier == 1 ? 4.4f : 9f + (tier - 2) * 4f;

        [SerializeField] private int initialSlots = 3;
        [SerializeField] private int maxSlots = 6;

        private readonly List<ResearchSlot> _slots = new List<ResearchSlot>();
        public IReadOnlyList<ResearchSlot> Slots => _slots;
        public int MaxSlots => maxSlots;

        /// <summary>Бонусы изученных технологий игрока.</summary>
        /// <summary>Законы империи (EmpireLaws) накладываются сюда же — при каждом обращении сверяются по версии.</summary>
        public EmpireBonuses Bonuses
        {
            get { EmpireLaws.SyncBonuses(_bonuses); return _bonuses; }
            private set => _bonuses = value;
        }
        private EmpireBonuses _bonuses = new EmpireBonuses();

        public float HyperlaneSpeedMultiplier => Bonuses.HyperlaneSpeed;
        public float MiningBonusMultiplier => Bonuses.MineralsMult;
        public float StarbaseCostDiscount => Bonuses.OutpostInfluenceDiscount;
        public bool DestroyerUnlocked => Bonuses.DestroyerUnlocked;
        public float GlobalResearchSpeedMultiplier => Bonuses.ResearchMult * TempBoost;

        /// <summary>Постоянная прибавка к науке (события, аномалии).</summary>
        public float FlatScience { get; private set; }
        public float TempBoost { get; private set; } = 1f;
        private int _tempBoostDays;

        private int _population;
        public int Population => _population;

        /// <summary>Наука от жителей: черты видов (умные +20%, роботы — 0) и стабильность планет.</summary>
        public float ScienceFromPopulation => EmpireStats.ResearchPops(0) * SciencePerPop;

        public float MonthlyResearchIncome =>
            (BaseScience + ScienceFromPopulation + FlatScience + Landmarks.Science(0)) * Bonuses.ResearchMult * TempBoost * LeaderManager.ResearchMult(0)
            * AIEmpireManager.PlayerTreatyResearchMult;

        private readonly List<Technology> _allTechs = new List<Technology>();
        public IReadOnlyList<Technology> AllTechs => _allTechs;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            for (int i = 0; i < initialSlots; i++) _slots.Add(new ResearchSlot());
            _allTechs.AddRange(BuildTechTree());
        }

        private void Start()
        {
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed += HandleDayPassed;
            RecountPopulation();
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed -= HandleDayPassed;
        }

        public void RecountPopulation() => _population = EmpireStats.Population(0);

        // ==================== ДЕРЕВО ====================

        /// <summary>Дерево технологий. Общее для игрока и ИИ (у ИИ — свой прогресс).</summary>
        public static List<Technology> BuildTechTree()
        {
            var list = new List<Technology>();
            void Add(string id, string name, TechCategory cat, string desc,
                     int tier, int col, float days, string[] reqs, int year, string bonusKey)
                => list.Add(new Technology(id, name, cat, desc, tier, col, days, reqs, year, bonusKey));

            // Уровни: базовые (2200), I (2208), II (2216), III (2229), IV (2242) — под 60-летнюю партию.
            // Внутри направления GridColumn — номер дорожки (строки), по которой технология идёт слева направо.
            const int Y0 = 2200, Y1 = 2208, Y2 = 2216, Y3 = 2229, Y4 = 2242;

            // ======================= ФИЗИКА =======================

            // --- ЭНЕРГЕТИКА ---
            Add("rct_fus_1", Loc.T("Термоядерные реакторы I"), TechCategory.Reactor,
                Loc.T("+4 Гелия-3 в месяц. Открывает термоядерный реактор кораблей."), 0, 0, 70f, null, Y0, "rct_fus_1");
            Add("rct_fus_2", Loc.T("Термоядерные реакторы II"), TechCategory.Reactor,
                Loc.T("+8 Гелия-3 в месяц."), 1, 0, 120f, new[]{"rct_fus_1"}, Y1, "rct_fus_2");
            Add("rct_ant_1", Loc.T("Антиматерия"), TechCategory.Reactor,
                Loc.T("+12 Гелия-3 в месяц. Открывает реактор на антиматерии."), 2, 0, 200f, new[]{"rct_fus_2"}, Y2, "rct_ant_1");
            Add("rct_zpe_1", Loc.T("Энергия нулевой точки"), TechCategory.Reactor,
                Loc.T("+18 Гелия-3 в месяц."), 3, 0, 220f, new[]{"rct_ant_1"}, Y3, "rct_zpe_1");
            Add("rct_sc_1", Loc.T("Сверхпроводники"), TechCategory.Reactor,
                Loc.T("+10% щитов, +3 Гелия-3 в месяц."), 1, 1, 110f, new[]{"rct_fus_1"}, Y1, "rct_sc_1");

            // --- СЕНСОРЫ И ИИ ---
            Add("sen_bas_1", Loc.T("Сенсорные массивы"), TechCategory.Sensors,
                Loc.T("+5 точности орудий, +20% скорости разведки систем."), 0, 0, 50f, null, Y0, "sen_bas_1");
            Add("sen_grv_1", Loc.T("Гравиметрия"), TechCategory.Sensors,
                Loc.T("+5 точности орудий, +20% скорости разведки систем."), 1, 0, 100f, new[]{"sen_bas_1"}, Y1, "sen_grv_1");
            Add("sen_tac_1", Loc.T("Тактические вычислители"), TechCategory.Sensors,
                Loc.T("+10 точности орудий."), 2, 0, 160f, new[]{"sen_grv_1"}, Y2, "sen_tac_1");
            Add("sen_ai_1", Loc.T("Простейший ИИ"), TechCategory.Sensors,
                Loc.T("+1 слот исследования."), 1, 1, 100f, new[]{"sen_bas_1"}, Y1, "slot+1");
            Add("sen_ai_2", Loc.T("Продвинутый ИИ"), TechCategory.Sensors,
                Loc.T("+1 слот исследования."), 2, 1, 180f, new[]{"sen_ai_1"}, Y2, "slot+1");
            Add("sen_qc_1", Loc.T("Квантовые вычисления"), TechCategory.Sensors,
                Loc.T("+20% скорости исследований."), 3, 1, 200f, new[]{"sen_ai_2"}, Y3, "sen_qc_1");

            // --- ДВИГАТЕЛИ ---
            Add("prp_hyp_1", Loc.T("Гипердвигатели I"), TechCategory.Propulsion,
                Loc.T("+15% скорости гиперпрыжков."), 0, 0, 70f, null, Y0, "prp_hyp_1");
            Add("prp_hyp_2", Loc.T("Гипердвигатели II"), TechCategory.Propulsion,
                Loc.T("+20% скорости гиперпрыжков (итого +35%)."), 1, 0, 120f, new[]{"prp_hyp_1"}, Y1, "prp_hyp_2");
            Add("prp_fld_1", Loc.T("Складки пространства"), TechCategory.Propulsion,
                Loc.T("+25% скорости гиперпрыжков (итого +60%)."), 2, 0, 170f, new[]{"prp_hyp_2"}, Y2, "prp_fld_1");
            Add("prp_eng_1", Loc.T("Импульсные двигатели"), TechCategory.Propulsion,
                Loc.T("+10% уклонения кораблей. Открывает модуль «Импульсный двигатель»."), 0, 1, 60f, null, Y0, "prp_eng_1");
            Add("prp_ion_1", Loc.T("Ионные маневровые"), TechCategory.Propulsion,
                Loc.T("+10% уклонения кораблей."), 1, 1, 100f, new[]{"prp_eng_1"}, Y1, "prp_ion_1");
            Add("prp_inr_1", Loc.T("Инерционные компенсаторы"), TechCategory.Propulsion,
                Loc.T("+15% уклонения кораблей."), 2, 1, 160f, new[]{"prp_ion_1"}, Y2, "prp_inr_1");

            // ======================= ОБЩЕСТВО =======================

            // --- НАУКА И УПРАВЛЕНИЕ ---
            Add("soc_admin_1", Loc.T("Административные системы"), TechCategory.Society,
                Loc.T("+1 слот исследования."), 0, 0, 100f, null, Y0, "slot+1");
            Add("soc_admin_2", Loc.T("Бюрократическая реформа"), TechCategory.Society,
                Loc.T("+1 слот исследования."), 1, 0, 180f, new[]{"soc_admin_1"}, Y1, "slot+1");
            Add("soc_sci_1", Loc.T("Научные институты"), TechCategory.Society,
                Loc.T("+15% скорости исследований."), 0, 1, 90f, null, Y0, "soc_sci_1");
            Add("soc_sci_2", Loc.T("Нейросетевое планирование"), TechCategory.Society,
                Loc.T("+25% скорости исследований."), 1, 1, 150f, new[]{"soc_sci_1"}, Y1, "soc_sci_2");
            Add("soc_sci_3", Loc.T("Академия Звёздного Совета"), TechCategory.Society,
                Loc.T("+20% скорости исследований."), 2, 1, 170f, new[]{"soc_sci_2"}, Y2, "soc_sci_3");
            Add("soc_sci_4", Loc.T("Коллективный разум"), TechCategory.Society,
                Loc.T("+30% скорости исследований."), 3, 1, 240f, new[]{"soc_sci_3", "sen_ai_2"}, Y3, "soc_sci_4");

            // --- ЭКСПАНСИЯ ---
            Add("soc_geo_1", Loc.T("Звёздная геодезия"), TechCategory.Colonization,
                Loc.T("+35% скорости разведки систем."), 0, 0, 100f, null, Y0, "soc_geo_1");
            Add("col_xgeo_1", Loc.T("Ксеногеология"), TechCategory.Colonization,
                Loc.T("+15% добычи титана, +15% скорости разведки систем."), 1, 0, 110f, new[]{"soc_geo_1"}, Y1, "col_xgeo_1");
            Add("col_hab_1", Loc.T("Орбитальные поселения"), TechCategory.Colonization,
                Loc.T("+10% добычи титана, +10% производства сплавов."), 2, 0, 180f, new[]{"col_xgeo_1"}, Y2, "col_hab_1");
            Add("cns_col_1", Loc.T("Колониальный устав"), TechCategory.Colonization,
                Loc.T("-10 Влияния на форпосты."), 0, 1, 70f, null, Y0, "cns_col_1");
            Add("col_frt_1", Loc.T("Пограничные протоколы"), TechCategory.Colonization,
                Loc.T("-5 Влияния на форпосты, +10% скорости гиперпрыжков."), 1, 1, 100f, new[]{"cns_col_1"}, Y1, "col_frt_1");

            // --- ВОЕННАЯ ДОКТРИНА ---
            Add("doc_flt_1", Loc.T("Флотская доктрина"), TechCategory.Doctrine,
                Loc.T("+5 точности орудий, +5% уклонения кораблей."), 0, 0, 60f, null, Y0, "doc_flt_1");
            Add("doc_drl_1", Loc.T("Учения флота"), TechCategory.Doctrine,
                Loc.T("+10% урона всего оружия."), 1, 0, 110f, new[]{"doc_flt_1"}, Y1, "doc_drl_1");
            Add("doc_net_1", Loc.T("Командная сеть"), TechCategory.Doctrine,
                Loc.T("+10% урона всего оружия, +5 точности орудий."), 2, 0, 170f, new[]{"doc_drl_1"}, Y2, "doc_net_1");
            Add("doc_log_1", Loc.T("Военная логистика"), TechCategory.Doctrine,
                Loc.T("-5% стоимости кораблей в сплавах, +10% скорости гиперпрыжков."), 1, 1, 100f, new[]{"doc_flt_1"}, Y1, "doc_log_1");

            // ======================= ИНЖЕНЕРИЯ =======================

            // --- ОРУЖИЕ ---
            Add("wpn_kin_1", Loc.T("Кинетические орудия I"), TechCategory.Weapons,
                Loc.T("Базовые рельсовые пушки. +10% урона кинетического оружия."), 0, 0, 60f, null, Y0, "weap_kin_1");
            Add("wpn_kin_2", Loc.T("Кинетические орудия II"), TechCategory.Weapons,
                Loc.T("Улучшенные рельсы. +15% урона кинетического оружия. Открывает рельсотрон."), 1, 0, 90f, new[]{"wpn_kin_1"}, Y1, "weap_kin_2");
            Add("wpn_kin_3", Loc.T("Масс-драйверы"), TechCategory.Weapons,
                Loc.T("+20% урона кинетического оружия."), 2, 0, 160f, new[]{"wpn_kin_2"}, Y2, "weap_kin_3");
            Add("wpn_las_1", Loc.T("Лазерные батареи I"), TechCategory.Weapons,
                Loc.T("Первые лазеры. +10% урона энергооружия, +5 точности."), 0, 1, 70f, null, Y0, "weap_las_1");
            Add("wpn_las_2", Loc.T("Лазерные батареи II"), TechCategory.Weapons,
                Loc.T("Улучшенные лазеры. +15% урона энергооружия, +5 точности. Открывает синий лазер."), 1, 1, 100f, new[]{"wpn_las_1"}, Y1, "weap_las_2");
            Add("wpn_las_3", Loc.T("Рентгеновские лазеры"), TechCategory.Weapons,
                Loc.T("+20% урона энергооружия, +5 точности."), 2, 1, 170f, new[]{"wpn_las_2"}, Y2, "weap_las_3");
            Add("wpn_pls_1", Loc.T("Плазменные орудия"), TechCategory.Weapons,
                Loc.T("Разряд плазмы. +20% урона всего оружия. Открывает плазменную пушку и торпеды."), 2, 2, 140f, new[]{"wpn_kin_2","wpn_las_2"}, Y2, "weap_pls_1");
            Add("wpn_lnc_1", Loc.T("Лэнс-орудия"), TechCategory.Weapons,
                Loc.T("+25% урона всего оружия."), 3, 2, 240f, new[]{"wpn_pls_1"}, Y3, "weap_lnc_1");

            // --- ЗАЩИТА ---
            Add("def_arm_1", Loc.T("Титановая броня I"), TechCategory.Defense,
                Loc.T("Базовая броня. +10% прочности корпуса и брони."), 0, 0, 60f, null, Y0, "def_arm_1");
            Add("def_arm_2", Loc.T("Титановая броня II"), TechCategory.Defense,
                Loc.T("Усиленная броня. +15% прочности корпуса и брони. Открывает керамостальную броню."), 1, 0, 90f, new[]{"def_arm_1"}, Y1, "def_arm_2");
            Add("def_arm_3", Loc.T("Нейтрониевая броня"), TechCategory.Defense,
                Loc.T("+20% прочности корпуса и брони."), 2, 0, 160f, new[]{"def_arm_2"}, Y2, "def_arm_3");
            Add("def_aeg_1", Loc.T("Протокол «Эгида»"), TechCategory.Defense,
                Loc.T("+15% прочности корпуса, брони и щитов."), 3, 0, 230f, new[]{"def_arm_3","def_shl_3"}, Y3, "def_aeg_1");
            Add("def_shl_1", Loc.T("Энергощиты I"), TechCategory.Defense,
                Loc.T("Первые щиты. +10% щитов."), 0, 1, 80f, null, Y0, "def_shl_1");
            Add("def_shl_2", Loc.T("Энергощиты II"), TechCategory.Defense,
                Loc.T("Улучшенные щиты. +15% щитов. Открывает усиленный дефлектор."), 1, 1, 110f, new[]{"def_shl_1"}, Y1, "def_shl_2");
            Add("def_shl_3", Loc.T("Гиперщиты"), TechCategory.Defense,
                Loc.T("+20% щитов."), 2, 1, 170f, new[]{"def_shl_2"}, Y2, "def_shl_3");

            // --- ПРОМЫШЛЕННОСТЬ ---
            Add("ind_min_1", Loc.T("Плазменные буры"), TechCategory.Industry,
                Loc.T("+20% добычи титана."), 0, 0, 80f, null, Y0, "ind_min_1");
            Add("ind_min_2", Loc.T("Глубокое обогащение"), TechCategory.Industry,
                Loc.T("+15% добычи титана (итого +35%)."), 1, 0, 130f, new[]{"ind_min_1"}, Y1, "ind_min_2");
            Add("ind_min_3", Loc.T("Астероидные комбинаты"), TechCategory.Industry,
                Loc.T("+20% добычи титана."), 2, 0, 170f, new[]{"ind_min_2"}, Y2, "ind_min_3");
            Add("ind_all_1", Loc.T("Сплавы нового поколения"), TechCategory.Industry,
                Loc.T("+20% производства сплавов."), 0, 1, 80f, null, Y0, "ind_all_1");
            Add("ind_all_2", Loc.T("Нанофабрикация"), TechCategory.Industry,
                Loc.T("+15% производства сплавов."), 1, 1, 120f, new[]{"ind_all_1"}, Y1, "ind_all_2");
            Add("ind_all_3", Loc.T("Автоматические кузни"), TechCategory.Industry,
                Loc.T("+20% производства сплавов."), 2, 1, 180f, new[]{"ind_all_2"}, Y2, "ind_all_3");
            Add("ind_meg_1", Loc.T("Мегаинженерия"), TechCategory.Industry,
                Loc.T("+25% производства сплавов, +15% добычи титана."), 3, 1, 250f, new[]{"ind_all_3","ind_min_3"}, Y3, "ind_meg_1");

            // --- ВЕРФИ ---
            Add("cns_sta_1", Loc.T("Орбитальные верфи"), TechCategory.Construction,
                Loc.T("-20% стоимости кораблей в сплавах."), 0, 0, 90f, null, Y0, "cns_sta_1");
            Add("cns_dst_1", Loc.T("Верфи класса «Эсминец»"), TechCategory.Construction,
                Loc.T("Открывает постройку эсминцев."), 1, 0, 150f, new[]{"cns_sta_1"}, Y1, "cns_dst_1");
            Add("cns_dck_1", Loc.T("Орбитальные доки"), TechCategory.Construction,
                Loc.T("-10% стоимости кораблей в сплавах."), 2, 0, 160f, new[]{"cns_dst_1"}, Y2, "cns_dck_1");
            Add("cns_mod_1", Loc.T("Модульное строительство"), TechCategory.Construction,
                Loc.T("-8% стоимости кораблей в сплавах."), 1, 1, 110f, new[]{"cns_sta_1"}, Y1, "cns_mod_1");

            // ======================= НОВЫЕ ТЕХНОЛОГИИ (позднее дерево и шпионаж) =======================

            // --- Энергетика ---
            Add("rct_mag_1", Loc.T("Магнитные ловушки"), TechCategory.Reactor,
                Loc.T("+10% добычи Гелия-3."), 2, 1, 150f, new[]{"rct_sc_1"}, Y2, "rct_mag_1");
            Add("rct_dys_1", Loc.T("Сфера Дайсона"), TechCategory.Reactor,
                Loc.T("+10 Гелия-3 в месяц и +10% добычи Гелия-3."), 3, 1, 240f, new[]{"rct_mag_1","rct_ant_1"}, Y3, "rct_dys_1");
            Add("rct_vac_1", Loc.T("Энергия вакуума"), TechCategory.Reactor,
                Loc.T("+25 Гелия-3 в месяц."), 4, 0, 280f, new[]{"rct_zpe_1"}, Y4, "rct_vac_1");

            // --- Сенсоры и ИИ: линия тайных операций ---
            Add("sen_crp_1", Loc.T("Криптография"), TechCategory.Sensors,
                Loc.T("+8% к защите от чужих агентов: шанс сорвать и раскрыть тайную операцию."), 0, 2, 60f, null, Y0, "sen_crp_1");
            Add("sen_net_1", Loc.T("Агентурные сети"), TechCategory.Sensors,
                Loc.T("+8% к шансу успеха ваших тайных операций."), 1, 2, 100f, new[]{"sen_crp_1"}, Y1, "sen_net_1");
            Add("sen_enc_1", Loc.T("Квантовое шифрование"), TechCategory.Sensors,
                Loc.T("+10% к защите от чужих агентов."), 2, 2, 160f, new[]{"sen_net_1"}, Y2, "sen_enc_1");
            Add("sen_dep_1", Loc.T("Глубокое внедрение"), TechCategory.Sensors,
                Loc.T("+10% к шансу успеха тайных операций, агенты внедряются в полтора раза быстрее."), 3, 2, 220f, new[]{"sen_enc_1"}, Y3, "sen_dep_1");
            Add("sen_sng_1", Loc.T("Сингулярный разум"), TechCategory.Sensors,
                Loc.T("+25% скорости исследований."), 4, 1, 300f, new[]{"sen_qc_1"}, Y4, "sen_sng_1");

            // --- Двигатели ---
            Add("prp_slp_1", Loc.T("Подпространственные туннели"), TechCategory.Propulsion,
                Loc.T("+25% скорости гиперпрыжков."), 3, 0, 230f, new[]{"prp_fld_1"}, Y3, "prp_slp_1");
            Add("prp_wrm_1", Loc.T("Червоточинные коридоры"), TechCategory.Propulsion,
                Loc.T("+30% скорости гиперпрыжков."), 4, 0, 300f, new[]{"prp_slp_1"}, Y4, "prp_wrm_1");

            // --- Наука и управление ---
            Add("soc_inf_1", Loc.T("Дипломатическая служба"), TechCategory.Society,
                Loc.T("+1 Влияния в месяц."), 1, 2, 110f, new[]{"soc_sci_1"}, Y1, "soc_inf_1");
            Add("soc_inf_2", Loc.T("Культурная гегемония"), TechCategory.Society,
                Loc.T("+1,5 Влияния в месяц."), 2, 2, 170f, new[]{"soc_inf_1"}, Y2, "soc_inf_2");
            Add("soc_inf_3", Loc.T("Галактический авторитет"), TechCategory.Society,
                Loc.T("+2 Влияния в месяц."), 3, 2, 230f, new[]{"soc_inf_2"}, Y3, "soc_inf_3");
            Add("soc_asc_1", Loc.T("Проект «Вознесение»"), TechCategory.Society,
                Loc.T("+30% скорости исследований. Венец науки: без этого проекта технологическая победа невозможна."), 4, 1, 320f,
                new[]{"soc_sci_4","sen_sng_1"}, Y4, "soc_asc_1");

            // --- Экспансия ---
            Add("col_gro_1", Loc.T("Гидропоника"), TechCategory.Colonization,
                Loc.T("+25% скорости роста населения."), 2, 1, 160f, new[]{"col_frt_1"}, Y2, "col_gro_1");
            Add("col_gro_2", Loc.T("Биоинженерия"), TechCategory.Colonization,
                Loc.T("+35% скорости роста населения."), 3, 1, 220f, new[]{"col_gro_1"}, Y3, "col_gro_2");
            Add("col_arc_1", Loc.T("Археотехнология"), TechCategory.Colonization,
                Loc.T("+50% к бонусам узлов силы под вашим контролем."), 3, 0, 220f, new[]{"col_hab_1"}, Y3, "col_arc_1");

            // --- Военная доктрина ---
            Add("doc_cap_1", Loc.T("Эскадренная организация"), TechCategory.Doctrine,
                Loc.T("+4 места во флотском лимите."), 2, 1, 150f, new[]{"doc_log_1"}, Y2, "doc_cap_1");
            Add("doc_cmd_1", Loc.T("Корабельные уставы"), TechCategory.Doctrine,
                Loc.T("-10% содержания военных кораблей."), 3, 1, 210f, new[]{"doc_cap_1"}, Y3, "doc_cmd_1");
            Add("doc_war_1", Loc.T("Тотальная мобилизация"), TechCategory.Doctrine,
                Loc.T("+15% урона всего оружия."), 3, 0, 230f, new[]{"doc_net_1"}, Y3, "doc_war_1");
            Add("doc_cap_2", Loc.T("Тыловое обеспечение"), TechCategory.Doctrine,
                Loc.T("+6 мест во флотском лимите, -15% содержания военных кораблей."), 4, 1, 270f, new[]{"doc_cmd_1"}, Y4, "doc_cap_2");
            Add("doc_ult_1", Loc.T("Доктрина Господства"), TechCategory.Doctrine,
                Loc.T("+20% урона всего оружия."), 4, 0, 290f, new[]{"doc_war_1"}, Y4, "doc_ult_1");

            // --- Оружие ---
            Add("wpn_kin_4", Loc.T("Гауссовы батареи"), TechCategory.Weapons,
                Loc.T("+25% урона кинетического оружия."), 3, 0, 230f, new[]{"wpn_kin_3"}, Y3, "wpn_kin_4");
            Add("wpn_las_4", Loc.T("Фотонные лэнсы"), TechCategory.Weapons,
                Loc.T("+25% урона энергооружия, +5 точности."), 3, 1, 230f, new[]{"wpn_las_3"}, Y3, "wpn_las_4");
            Add("wpn_ult_1", Loc.T("Аннигиляторы"), TechCategory.Weapons,
                Loc.T("+25% урона всего оружия."), 4, 2, 300f, new[]{"wpn_lnc_1"}, Y4, "wpn_ult_1");

            // --- Защита ---
            Add("def_shl_4", Loc.T("Планетарные щиты"), TechCategory.Defense,
                Loc.T("+25% щитов."), 3, 1, 220f, new[]{"def_shl_3"}, Y3, "def_shl_4");
            Add("def_bar_1", Loc.T("Барьер Предтеч"), TechCategory.Defense,
                Loc.T("+20% прочности корпуса, брони и щитов."), 4, 0, 290f, new[]{"def_aeg_1"}, Y4, "def_bar_1");

            // --- Промышленность ---
            Add("ind_rec_1", Loc.T("Рециклинг"), TechCategory.Industry,
                Loc.T("+10% добычи титана и производства сплавов."), 3, 0, 230f, new[]{"ind_min_3"}, Y3, "ind_rec_1");
            Add("ind_sin_1", Loc.T("Сингулярные кузни"), TechCategory.Industry,
                Loc.T("+30% производства сплавов, +20% добычи титана."), 4, 1, 300f, new[]{"ind_meg_1"}, Y4, "ind_sin_1");

            // --- Верфи ---
            Add("cns_fort_1", Loc.T("Звёздные крепости"), TechCategory.Construction,
                Loc.T("+30% корпуса, брони и щитов звёздных баз."), 2, 1, 160f, new[]{"cns_mod_1"}, Y2, "cns_fort_1");
            Add("cns_arc_1", Loc.T("Автоматические верфи"), TechCategory.Construction,
                Loc.T("-10% стоимости кораблей в сплавах."), 3, 0, 220f, new[]{"cns_dck_1"}, Y3, "cns_arc_1");

            return list;
        }

        // ==================== ДОСТУПНОСТЬ ====================

        public Technology FindTech(string id) => _allTechs.Find(t => t.Id == id);

        public bool IsTechAvailable(Technology tech)
        {
            if (tech == null || tech.IsResearched) return false;
            foreach (var reqId in tech.RequiredTechIds)
            {
                var req = FindTech(reqId);
                if (req == null || !req.IsResearched) return false;
            }
            return true;
        }

        public bool IsBeingResearched(Technology tech)
        {
            foreach (var s in _slots)
            {
                if (s.CurrentTech == tech) return true;
                if (s.Queue.Contains(tech)) return true;
            }
            return false;
        }

        public int ResearchedCount
        {
            get
            {
                int n = 0;
                foreach (var t in _allTechs) if (t.IsResearched) n++;
                return n;
            }
        }

        // ==================== НАЗНАЧЕНИЕ ====================

        public bool AssignTech(Technology tech)
        {
            if (tech == null) return false;
            if (tech.IsResearched) return false;
            if (!IsTechAvailable(tech)) return false;
            if (IsBeingResearched(tech)) return false;

            foreach (var s in _slots)
            {
                if (s.CurrentTech == null)
                {
                    s.CurrentTech = tech;
                    s.Progress = tech.Progress;
                    s.IsPaused = false;
                    OnTechProgressUpdated?.Invoke();
                    return true;
                }
            }

            ResearchSlot best = null;
            float minRemaining = float.MaxValue;
            foreach (var s in _slots)
            {
                if (s.CurrentTech == null) continue;
                if (s.Queue.Count >= 5) continue;
                float left = SlotDaysToFinish(s);
                if (left < minRemaining) { minRemaining = left; best = s; }
            }
            if (best != null)
            {
                best.Queue.Add(tech);
                OnTechProgressUpdated?.Invoke();
                return true;
            }
            return false;
        }

        public void RemoveFromQueue(Technology tech)
        {
            foreach (var s in _slots) s.Queue.Remove(tech);
            OnTechProgressUpdated?.Invoke();
        }

        public void CancelCurrent(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return;
            var slot = _slots[slotIndex];
            if (slot.CurrentTech != null)
            {
                slot.CurrentTech.Progress = slot.Progress;
                slot.CurrentTech = null;
                slot.Progress = 0f;
            }
            OnTechProgressUpdated?.Invoke();
        }

        public void ToggleSlotPause(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return;
            _slots[slotIndex].IsPaused = !_slots[slotIndex].IsPaused;
            OnTechProgressUpdated?.Invoke();
        }

        // ==================== СКОРОСТЬ И ОЦЕНКА ВРЕМЕНИ ====================

        public int ActiveSlotCount
        {
            get
            {
                int n = 0;
                foreach (var s in _slots) if (s.CurrentTech != null && !s.IsPaused) n++;
                return n;
            }
        }

        public bool HasFreeSlot
        {
            get
            {
                foreach (var s in _slots) if (s.CurrentTech == null) return true;
                return false;
            }
        }

        /// <summary>Очков науки в день на один слот при заданном числе активных слотов.</summary>
        public float DailyPointsPerSlot(int activeSlots)
            => MonthlyResearchIncome / Mathf.Pow(Mathf.Max(1, activeSlots), SlotSplitExponent) / 30f;

        private float DaysFor(Technology tech, float pointsLeft, int activeSlots)
        {
            float perDay = DailyPointsPerSlot(activeSlots) / YearPenaltyMultiplier(tech, GetCurrentYear()) * EspionageManager.ResearchBoost(tech);
            if (perDay <= 0.0001f) return float.PositiveInfinity;
            return pointsLeft / perDay;
        }

        private float SlotDaysToFinish(ResearchSlot s)
            => s.CurrentTech == null ? 0f : DaysFor(s.CurrentTech, s.PointsRemaining, Mathf.Max(1, ActiveSlotCount));

        /// <summary>
        /// Сколько игровых дней займёт технология при ТЕКУЩЕЙ скорости науки:
        /// для изучаемой — остаток, для стоящей в очереди — ожидание + изучение,
        /// для новой — изучение в свободном слоте (или в общем потоке, если слотов нет).
        /// </summary>
        public int EstimateDays(Technology tech)
        {
            if (tech == null || tech.IsResearched) return 0;
            int active = ActiveSlotCount;

            foreach (var s in _slots)
            {
                if (s.CurrentTech == tech)
                {
                    if (s.IsPaused) return -1;
                    return CeilDays(DaysFor(tech, s.PointsRemaining, Mathf.Max(1, active)));
                }
                int qi = s.Queue.IndexOf(tech);
                if (qi >= 0)
                {
                    int n = Mathf.Max(1, active);
                    float total = s.CurrentTech != null ? DaysFor(s.CurrentTech, s.PointsRemaining, n) : 0f;
                    for (int i = 0; i < qi; i++)
                        total += DaysFor(s.Queue[i], Mathf.Max(0f, s.Queue[i].Cost - s.Queue[i].Progress), n);
                    total += DaysFor(tech, Mathf.Max(0f, tech.Cost - tech.Progress), n);
                    return CeilDays(total);
                }
            }

            int slots = HasFreeSlot ? active + 1 : Mathf.Max(1, active);
            return CeilDays(DaysFor(tech, Mathf.Max(0f, tech.Cost - tech.Progress), slots));
        }

        private static int CeilDays(float d) => float.IsInfinity(d) ? -1 : Mathf.Max(1, Mathf.CeilToInt(d));

        /// <summary>«~45 дн.» / «~2 г. 3 мес.» — для подписей в окне технологий.</summary>
        public static string FormatDays(int days)
        {
            if (days < 0) return "—";
            if (days < 60) return Loc.F("{0} дн.", days);
            int months = Mathf.RoundToInt(days / 30f);
            if (months < 12) return Loc.F("{0} мес.", months);
            int y = months / 12, m = months % 12;
            return m == 0 ? Loc.F("{0} г.", y) : Loc.F("{0} г. {1} мес.", y, m);
        }

        // ==================== ПРОГРЕСС ====================

        private void HandleDayPassed(int day, int month, int year)
        {
            RecountPopulation();

            if (_tempBoostDays > 0 && --_tempBoostDays == 0)
            {
                TempBoost = 1f;
                NotificationCenter.Show(Loc.T("Научный подъём завершён"), Loc.T("Скорость исследований вернулась к норме"), NotificationCenter.Kind.Info, 4f);
            }

            bool bootstrapped = false;
            foreach (var slot in _slots)
            {
                if (slot.CurrentTech == null && slot.Queue.Count > 0)
                {
                    slot.CurrentTech = slot.Queue[0];
                    slot.Queue.RemoveAt(0);
                    slot.Progress = slot.CurrentTech.Progress;
                    slot.IsPaused = false;
                    bootstrapped = true;
                }
            }
            if (bootstrapped) OnTechProgressUpdated?.Invoke();

            int activeSlots = ActiveSlotCount;
            if (activeSlots == 0) return;

            float perSlot = DailyPointsPerSlot(activeSlots);

            bool changed = false;
            foreach (var slot in _slots)
            {
                if (slot.CurrentTech == null || slot.IsPaused) continue;

                // Разведсеть: технологию, известную цели, изучаем быстрее (EspionageManager.ResearchBoost)
                slot.Progress += perSlot / YearPenaltyMultiplier(slot.CurrentTech, year) * EspionageManager.ResearchBoost(slot.CurrentTech);
                slot.CurrentTech.Progress = slot.Progress;

                if (slot.Progress >= slot.CurrentTech.Cost)
                    CompleteTech(slot);

                changed = true;
            }

            if (changed) OnTechProgressUpdated?.Invoke();
        }

        /// <summary>Технология «из будущего» изучается медленнее: ×(1 + лет до доступности).</summary>
        public static float YearPenaltyMultiplier(Technology tech, int currentYear)
        {
            if (currentYear >= tech.YearAvailable) return 1f;
            int yearsAhead = tech.YearAvailable - currentYear;
            return 1f + yearsAhead;
        }

        public float GetYearPenalty(Technology tech) => YearPenaltyMultiplier(tech, GetCurrentYear());

        private static int GetCurrentYear() => TimeManager.Instance != null ? TimeManager.Instance.Year : 2200;

        private void CompleteTech(ResearchSlot slot)
        {
            var tech = slot.CurrentTech;
            tech.IsResearched = true;
            tech.Progress = tech.Cost;

            ApplyTechBonus(tech);
            OnTechCompleted?.Invoke(tech);

            slot.CurrentTech = null;
            slot.Progress = 0f;

            NotificationCenter.Show(Loc.T("Технология изучена"), tech.Name, NotificationCenter.Kind.Success, 5f);
            Debug.Log($"<color=#3FE>[Наука] Изучено: {tech.Name}</color>");
        }

        // ==================== БОНУСЫ ====================

        private void ApplyTechBonus(Technology tech)
        {
            var before = Bonuses.Clone();
            if (TechEffects.Apply(Bonuses, tech.BonusKey)) AddSlot();
            if (TechEffects.AffectsDurability(tech.BonusKey)) FleetManager.Instance?.RescaleDurability(0, before, Bonuses);
            EconomyManager.Instance?.RecalculateAll();
        }

        public void AddSlot()
        {
            if (_slots.Count >= maxSlots) return;
            _slots.Add(new ResearchSlot());
            OnSlotsChanged?.Invoke(_slots.Count);
            OnTechProgressUpdated?.Invoke();
        }

        public void SelectTechToResearch(Technology tech) => AssignTech(tech);

        // ==================== СОХРАНЕНИЕ ====================

        public TechSave CaptureState()
        {
            var s = new TechSave
            {
                FlatScience = FlatScience,
                TempBoost = TempBoost,
                TempBoostDays = _tempBoostDays
            };
            foreach (var t in _allTechs)
            {
                if (t.IsResearched) s.Researched.Add(t.Id);
                else if (t.Progress > 0f) { s.ProgressIds.Add(t.Id); s.ProgressDays.Add(t.Progress); }
            }
            foreach (var slot in _slots)
            {
                var ss = new SlotSave
                {
                    Current = slot.CurrentTech?.Id,
                    Days = slot.Progress,
                    Paused = slot.IsPaused
                };
                foreach (var q in slot.Queue) ss.Queue.Add(q.Id);
                s.Slots.Add(ss);
            }
            return s;
        }

        /// <summary>
        /// Восстановление. Бонусы не хранятся — они пересчитываются по списку изученного.
        /// Сохранения версии 1 хранили прогресс в старых «днях» — переводим в долю стоимости.
        /// </summary>
        public void RestoreState(TechSave s, int version)
        {
            if (s == null) return;
            bool legacy = version < 2;
            float Convert(Technology t, float stored) =>
                legacy ? Mathf.Clamp01(stored / Mathf.Max(1f, t.RawDays * 0.65f)) * t.Cost : stored;

            Bonuses = new EmpireBonuses();
            foreach (var t in _allTechs) { t.IsResearched = false; t.Progress = 0f; }

            int slotTechs = 0;
            foreach (var id in s.Researched)
            {
                var t = FindTech(id);
                if (t == null) continue;
                t.IsResearched = true;
                t.Progress = t.Cost;
                if (TechEffects.Apply(Bonuses, t.BonusKey)) slotTechs++;
            }
            for (int i = 0; i < s.ProgressIds.Count && i < s.ProgressDays.Count; i++)
            {
                var t = FindTech(s.ProgressIds[i]);
                if (t != null) t.Progress = Convert(t, s.ProgressDays[i]);
            }

            FlatScience = legacy ? Mathf.Max(0f, s.BaseMonthlyScience - 35f) : s.FlatScience;
            TempBoost = s.TempBoost > 0f ? s.TempBoost : 1f;
            _tempBoostDays = s.TempBoostDays;
            if (_tempBoostDays <= 0) TempBoost = 1f;

            _slots.Clear();
            foreach (var ss in s.Slots)
            {
                var slot = new ResearchSlot { IsPaused = ss.Paused };
                var cur = string.IsNullOrEmpty(ss.Current) ? null : FindTech(ss.Current);
                if (cur != null && !cur.IsResearched) { slot.CurrentTech = cur; slot.Progress = Convert(cur, ss.Days); }
                foreach (var qid in ss.Queue)
                {
                    var q = FindTech(qid);
                    if (q != null && !q.IsResearched) slot.Queue.Add(q);
                }
                _slots.Add(slot);
            }
            int wanted = Mathf.Min(maxSlots, initialSlots + slotTechs);
            while (_slots.Count < wanted) _slots.Add(new ResearchSlot());

            RecountPopulation();
            OnSlotsChanged?.Invoke(_slots.Count);
            OnTechProgressUpdated?.Invoke();
        }

        // ==================== ДЛЯ ИВЕНТОВ ====================

        /// <summary>Разовый вклад очков науки в текущие исследования.</summary>
        public void AddInstantScience(float sciencePoints)
        {
            if (sciencePoints <= 0) return;
            float remaining = sciencePoints;
            bool changed = false;
            foreach (var slot in _slots)
            {
                if (slot.CurrentTech == null || slot.IsPaused) continue;
                float toInject = Mathf.Min(remaining, slot.PointsRemaining + 1f);
                slot.Progress += toInject;
                slot.CurrentTech.Progress = slot.Progress;
                remaining -= toInject;
                changed = true;
                if (slot.Progress >= slot.CurrentTech.Cost)
                    CompleteTech(slot);
                if (remaining <= 0) break;
            }
            if (changed) OnTechProgressUpdated?.Invoke();
        }

        public void AddMonthlyScience(float amount)
        {
            if (amount <= 0) return;
            FlatScience += amount;
            OnTechProgressUpdated?.Invoke();
        }

        /// <summary>Снять ранее добавленную ежемесячную науку (окончание временного эффекта).</summary>
        public void RemoveMonthlyScience(float amount)
        {
            if (amount <= 0) return;
            FlatScience = Mathf.Max(0f, FlatScience - amount);
            OnTechProgressUpdated?.Invoke();
        }

        /// <summary>Временное ускорение науки на заданное число дней.</summary>
        public void ApplyTempResearchBoost(float multiplier, int days = 30)
        {
            TempBoost = Mathf.Max(TempBoost, Mathf.Max(1f, multiplier));
            _tempBoostDays = Mathf.Max(_tempBoostDays, days);
            OnTechProgressUpdated?.Invoke();
        }

        public void CompleteRandomTech()
        {
            var available = new List<Technology>();
            foreach (var t in _allTechs)
                if (IsTechAvailable(t)) available.Add(t);
            if (available.Count == 0) return;
            GrantTech(available[UnityEngine.Random.Range(0, available.Count)]);
        }

        /// <summary>Мгновенно изучить технологию (события, кража агентами); идущие и очередные исследования её освобождаются.</summary>
        public void GrantTech(Technology pick)
        {
            if (pick == null || pick.IsResearched) return;
            foreach (var slot in _slots) slot.Queue.Remove(pick);
            ResearchSlot host = null;
            foreach (var slot in _slots) if (slot.CurrentTech == pick) host = slot;
            if (host == null) host = new ResearchSlot { CurrentTech = pick };
            CompleteTech(host);
            OnTechProgressUpdated?.Invoke();
        }

        /// <summary>Добавить очки к конкретной технологии (чертежи, добытые агентами); до порога изучения.</summary>
        public void AddTechProgress(Technology t, float points)
        {
            if (t == null || t.IsResearched || points <= 0f) return;
            t.Progress = Mathf.Min(t.Cost * 0.95f, t.Progress + points);
            foreach (var slot in _slots) if (slot.CurrentTech == t) slot.Progress = t.Progress;
            OnTechProgressUpdated?.Invoke();
        }
    }
}
