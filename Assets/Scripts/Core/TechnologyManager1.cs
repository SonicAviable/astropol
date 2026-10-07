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
            TechBranch.Physics => "ФИЗИКА",
            TechBranch.Society => "ОБЩЕСТВО",
            _ => "ИНЖЕНЕРИЯ"
        };

        public static string Motto(TechBranch b) => b switch
        {
            TechBranch.Physics => "Энергия, вычисления и движение",
            TechBranch.Society => "Наука, экспансия и доктрина флота",
            _ => "Оружие, броня, промышленность и верфи"
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
            TechCategory.Weapons      => "ОРУЖИЕ",
            TechCategory.Defense      => "ЗАЩИТА",
            TechCategory.Propulsion   => "ДВИГАТЕЛИ",
            TechCategory.Sensors      => "СЕНСОРЫ И ИИ",
            TechCategory.Industry     => "ПРОМЫШЛЕННОСТЬ",
            TechCategory.Reactor      => "ЭНЕРГЕТИКА",
            TechCategory.Construction => "ВЕРФИ",
            TechCategory.Society      => "НАУКА И УПРАВЛЕНИЕ",
            TechCategory.Colonization => "ЭКСПАНСИЯ",
            TechCategory.Doctrine     => "ВОЕННАЯ ДОКТРИНА",
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
        public const float CostPerRawDay = 3.3f;
        /// <summary>Наука без населения (администрация, архивы).</summary>
        public const float BaseScience = 20f;
        /// <summary>Наука с каждой единицы населения.</summary>
        public const float SciencePerPop = 1f;
        /// <summary>Штраф за параллельные исследования: скорость слота = доход / N^0.7.</summary>
        public const float SlotSplitExponent = 0.7f;

        /// <summary>
        /// Множитель стоимости по уровню: базовые ×1, Tier 1 ×4.4, Tier 2 ×9, Tier 3 ×13. Ранние технологии
        /// изучаются за месяцы, поздние — за годы: научная победа достижима ближе к 2220-м.
        /// </summary>
        public static float TierCostFactor(int tier) => tier <= 0 ? 1f : tier == 1 ? 4.4f : 9f + (tier - 2) * 4f;

        [SerializeField] private int initialSlots = 3;
        [SerializeField] private int maxSlots = 6;

        private readonly List<ResearchSlot> _slots = new List<ResearchSlot>();
        public IReadOnlyList<ResearchSlot> Slots => _slots;
        public int MaxSlots => maxSlots;

        /// <summary>Бонусы изученных технологий игрока.</summary>
        public EmpireBonuses Bonuses { get; private set; } = new EmpireBonuses();

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

        public float ScienceFromPopulation => _population * SciencePerPop;

        public float MonthlyResearchIncome =>
            (BaseScience + ScienceFromPopulation + FlatScience) * Bonuses.ResearchMult * TempBoost * LeaderManager.ResearchMult(0);

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

            // Уровни: базовые (2200), I (2205), II (2210), III (2218). Внутри направления
            // GridColumn — номер дорожки (строки), по которой технология идёт слева направо.
            const int Y0 = 2200, Y1 = 2205, Y2 = 2210, Y3 = 2218;

            // ======================= ФИЗИКА =======================

            // --- ЭНЕРГЕТИКА ---
            Add("rct_fus_1", "Термоядерные реакторы I", TechCategory.Reactor,
                "+4 Гелия-3 в месяц. Открывает термоядерный реактор кораблей.", 0, 0, 70f, null, Y0, "rct_fus_1");
            Add("rct_fus_2", "Термоядерные реакторы II", TechCategory.Reactor,
                "+8 Гелия-3 в месяц.", 1, 0, 120f, new[]{"rct_fus_1"}, Y1, "rct_fus_2");
            Add("rct_ant_1", "Антиматерия", TechCategory.Reactor,
                "+12 Гелия-3 в месяц. Открывает реактор на антиматерии.", 2, 0, 200f, new[]{"rct_fus_2"}, Y2, "rct_ant_1");
            Add("rct_zpe_1", "Энергия нулевой точки", TechCategory.Reactor,
                "+18 Гелия-3 в месяц.", 3, 0, 220f, new[]{"rct_ant_1"}, Y3, "rct_zpe_1");
            Add("rct_sc_1", "Сверхпроводники", TechCategory.Reactor,
                "+10% щитов, +3 Гелия-3 в месяц.", 1, 1, 110f, new[]{"rct_fus_1"}, Y1, "rct_sc_1");

            // --- СЕНСОРЫ И ИИ ---
            Add("sen_bas_1", "Сенсорные массивы", TechCategory.Sensors,
                "+5 точности орудий, +20% скорости разведки систем.", 0, 0, 50f, null, Y0, "sen_bas_1");
            Add("sen_grv_1", "Гравиметрия", TechCategory.Sensors,
                "+5 точности орудий, +20% скорости разведки систем.", 1, 0, 100f, new[]{"sen_bas_1"}, Y1, "sen_grv_1");
            Add("sen_tac_1", "Тактические вычислители", TechCategory.Sensors,
                "+10 точности орудий.", 2, 0, 160f, new[]{"sen_grv_1"}, Y2, "sen_tac_1");
            Add("sen_ai_1", "Простейший ИИ", TechCategory.Sensors,
                "+1 слот исследования.", 1, 1, 100f, new[]{"sen_bas_1"}, Y1, "slot+1");
            Add("sen_ai_2", "Продвинутый ИИ", TechCategory.Sensors,
                "+1 слот исследования.", 2, 1, 180f, new[]{"sen_ai_1"}, Y2, "slot+1");
            Add("sen_qc_1", "Квантовые вычисления", TechCategory.Sensors,
                "+20% скорости исследований.", 3, 1, 200f, new[]{"sen_ai_2"}, Y3, "sen_qc_1");

            // --- ДВИГАТЕЛИ ---
            Add("prp_hyp_1", "Гипердвигатели I", TechCategory.Propulsion,
                "+15% скорости гиперпрыжков.", 0, 0, 70f, null, Y0, "prp_hyp_1");
            Add("prp_hyp_2", "Гипердвигатели II", TechCategory.Propulsion,
                "+20% скорости гиперпрыжков (итого +35%).", 1, 0, 120f, new[]{"prp_hyp_1"}, Y1, "prp_hyp_2");
            Add("prp_fld_1", "Складки пространства", TechCategory.Propulsion,
                "+25% скорости гиперпрыжков (итого +60%).", 2, 0, 170f, new[]{"prp_hyp_2"}, Y2, "prp_fld_1");
            Add("prp_eng_1", "Импульсные двигатели", TechCategory.Propulsion,
                "+10% уклонения кораблей. Открывает модуль «Импульсный двигатель».", 0, 1, 60f, null, Y0, "prp_eng_1");
            Add("prp_ion_1", "Ионные маневровые", TechCategory.Propulsion,
                "+10% уклонения кораблей.", 1, 1, 100f, new[]{"prp_eng_1"}, Y1, "prp_ion_1");
            Add("prp_inr_1", "Инерционные компенсаторы", TechCategory.Propulsion,
                "+15% уклонения кораблей.", 2, 1, 160f, new[]{"prp_ion_1"}, Y2, "prp_inr_1");

            // ======================= ОБЩЕСТВО =======================

            // --- НАУКА И УПРАВЛЕНИЕ ---
            Add("soc_admin_1", "Административные системы", TechCategory.Society,
                "+1 слот исследования.", 0, 0, 100f, null, Y0, "slot+1");
            Add("soc_admin_2", "Бюрократическая реформа", TechCategory.Society,
                "+1 слот исследования.", 1, 0, 180f, new[]{"soc_admin_1"}, Y1, "slot+1");
            Add("soc_sci_1", "Научные институты", TechCategory.Society,
                "+15% скорости исследований.", 0, 1, 90f, null, Y0, "soc_sci_1");
            Add("soc_sci_2", "Нейросетевое планирование", TechCategory.Society,
                "+25% скорости исследований.", 1, 1, 150f, new[]{"soc_sci_1"}, Y1, "soc_sci_2");
            Add("soc_sci_3", "Академия Звёздного Совета", TechCategory.Society,
                "+20% скорости исследований.", 2, 1, 170f, new[]{"soc_sci_2"}, Y2, "soc_sci_3");
            Add("soc_sci_4", "Коллективный разум", TechCategory.Society,
                "+30% скорости исследований.", 3, 1, 240f, new[]{"soc_sci_3", "sen_ai_2"}, Y3, "soc_sci_4");

            // --- ЭКСПАНСИЯ ---
            Add("soc_geo_1", "Звёздная геодезия", TechCategory.Colonization,
                "+35% скорости разведки систем.", 0, 0, 100f, null, Y0, "soc_geo_1");
            Add("col_xgeo_1", "Ксеногеология", TechCategory.Colonization,
                "+15% добычи титана, +15% скорости разведки систем.", 1, 0, 110f, new[]{"soc_geo_1"}, Y1, "col_xgeo_1");
            Add("col_hab_1", "Орбитальные поселения", TechCategory.Colonization,
                "+10% добычи титана, +10% производства сплавов.", 2, 0, 180f, new[]{"col_xgeo_1"}, Y2, "col_hab_1");
            Add("cns_col_1", "Колониальный устав", TechCategory.Colonization,
                "-10 Влияния на форпосты.", 0, 1, 70f, null, Y0, "cns_col_1");
            Add("col_frt_1", "Пограничные протоколы", TechCategory.Colonization,
                "-5 Влияния на форпосты, +10% скорости гиперпрыжков.", 1, 1, 100f, new[]{"cns_col_1"}, Y1, "col_frt_1");

            // --- ВОЕННАЯ ДОКТРИНА ---
            Add("doc_flt_1", "Флотская доктрина", TechCategory.Doctrine,
                "+5 точности орудий, +5% уклонения кораблей.", 0, 0, 60f, null, Y0, "doc_flt_1");
            Add("doc_drl_1", "Учения флота", TechCategory.Doctrine,
                "+10% урона всего оружия.", 1, 0, 110f, new[]{"doc_flt_1"}, Y1, "doc_drl_1");
            Add("doc_net_1", "Командная сеть", TechCategory.Doctrine,
                "+10% урона всего оружия, +5 точности орудий.", 2, 0, 170f, new[]{"doc_drl_1"}, Y2, "doc_net_1");
            Add("doc_log_1", "Военная логистика", TechCategory.Doctrine,
                "-5% стоимости кораблей в сплавах, +10% скорости гиперпрыжков.", 1, 1, 100f, new[]{"doc_flt_1"}, Y1, "doc_log_1");

            // ======================= ИНЖЕНЕРИЯ =======================

            // --- ОРУЖИЕ ---
            Add("wpn_kin_1", "Кинетические орудия I", TechCategory.Weapons,
                "Базовые рельсовые пушки. +10% урона кинетического оружия.", 0, 0, 60f, null, Y0, "weap_kin_1");
            Add("wpn_kin_2", "Кинетические орудия II", TechCategory.Weapons,
                "Улучшенные рельсы. +15% урона кинетического оружия. Открывает рельсотрон.", 1, 0, 90f, new[]{"wpn_kin_1"}, Y1, "weap_kin_2");
            Add("wpn_kin_3", "Масс-драйверы", TechCategory.Weapons,
                "+20% урона кинетического оружия.", 2, 0, 160f, new[]{"wpn_kin_2"}, Y2, "weap_kin_3");
            Add("wpn_las_1", "Лазерные батареи I", TechCategory.Weapons,
                "Первые лазеры. +10% урона энергооружия, +5 точности.", 0, 1, 70f, null, Y0, "weap_las_1");
            Add("wpn_las_2", "Лазерные батареи II", TechCategory.Weapons,
                "Улучшенные лазеры. +15% урона энергооружия, +5 точности. Открывает синий лазер.", 1, 1, 100f, new[]{"wpn_las_1"}, Y1, "weap_las_2");
            Add("wpn_las_3", "Рентгеновские лазеры", TechCategory.Weapons,
                "+20% урона энергооружия, +5 точности.", 2, 1, 170f, new[]{"wpn_las_2"}, Y2, "weap_las_3");
            Add("wpn_pls_1", "Плазменные орудия", TechCategory.Weapons,
                "Разряд плазмы. +20% урона всего оружия. Открывает плазменную пушку и торпеды.", 2, 2, 140f, new[]{"wpn_kin_2","wpn_las_2"}, Y2, "weap_pls_1");
            Add("wpn_lnc_1", "Лэнс-орудия", TechCategory.Weapons,
                "+25% урона всего оружия.", 3, 2, 240f, new[]{"wpn_pls_1"}, Y3, "weap_lnc_1");

            // --- ЗАЩИТА ---
            Add("def_arm_1", "Титановая броня I", TechCategory.Defense,
                "Базовая броня. +10% прочности корпуса и брони.", 0, 0, 60f, null, Y0, "def_arm_1");
            Add("def_arm_2", "Титановая броня II", TechCategory.Defense,
                "Усиленная броня. +15% прочности корпуса и брони. Открывает керамостальную броню.", 1, 0, 90f, new[]{"def_arm_1"}, Y1, "def_arm_2");
            Add("def_arm_3", "Нейтрониевая броня", TechCategory.Defense,
                "+20% прочности корпуса и брони.", 2, 0, 160f, new[]{"def_arm_2"}, Y2, "def_arm_3");
            Add("def_aeg_1", "Протокол «Эгида»", TechCategory.Defense,
                "+15% прочности корпуса, брони и щитов.", 3, 0, 230f, new[]{"def_arm_3","def_shl_3"}, Y3, "def_aeg_1");
            Add("def_shl_1", "Энергощиты I", TechCategory.Defense,
                "Первые щиты. +10% щитов.", 0, 1, 80f, null, Y0, "def_shl_1");
            Add("def_shl_2", "Энергощиты II", TechCategory.Defense,
                "Улучшенные щиты. +15% щитов. Открывает усиленный дефлектор.", 1, 1, 110f, new[]{"def_shl_1"}, Y1, "def_shl_2");
            Add("def_shl_3", "Гиперщиты", TechCategory.Defense,
                "+20% щитов.", 2, 1, 170f, new[]{"def_shl_2"}, Y2, "def_shl_3");

            // --- ПРОМЫШЛЕННОСТЬ ---
            Add("ind_min_1", "Плазменные буры", TechCategory.Industry,
                "+20% добычи титана.", 0, 0, 80f, null, Y0, "ind_min_1");
            Add("ind_min_2", "Глубокое обогащение", TechCategory.Industry,
                "+15% добычи титана (итого +35%).", 1, 0, 130f, new[]{"ind_min_1"}, Y1, "ind_min_2");
            Add("ind_min_3", "Астероидные комбинаты", TechCategory.Industry,
                "+20% добычи титана.", 2, 0, 170f, new[]{"ind_min_2"}, Y2, "ind_min_3");
            Add("ind_all_1", "Сплавы нового поколения", TechCategory.Industry,
                "+20% производства сплавов.", 0, 1, 80f, null, Y0, "ind_all_1");
            Add("ind_all_2", "Нанофабрикация", TechCategory.Industry,
                "+15% производства сплавов.", 1, 1, 120f, new[]{"ind_all_1"}, Y1, "ind_all_2");
            Add("ind_all_3", "Автоматические кузни", TechCategory.Industry,
                "+20% производства сплавов.", 2, 1, 180f, new[]{"ind_all_2"}, Y2, "ind_all_3");
            Add("ind_meg_1", "Мегаинженерия", TechCategory.Industry,
                "+25% производства сплавов, +15% добычи титана.", 3, 1, 250f, new[]{"ind_all_3","ind_min_3"}, Y3, "ind_meg_1");

            // --- ВЕРФИ ---
            Add("cns_sta_1", "Орбитальные верфи", TechCategory.Construction,
                "-20% стоимости кораблей в сплавах.", 0, 0, 90f, null, Y0, "cns_sta_1");
            Add("cns_dst_1", "Верфи класса «Эсминец»", TechCategory.Construction,
                "Открывает постройку эсминцев.", 1, 0, 150f, new[]{"cns_sta_1"}, Y1, "cns_dst_1");
            Add("cns_dck_1", "Орбитальные доки", TechCategory.Construction,
                "-10% стоимости кораблей в сплавах.", 2, 0, 160f, new[]{"cns_dst_1"}, Y2, "cns_dck_1");
            Add("cns_mod_1", "Модульное строительство", TechCategory.Construction,
                "-8% стоимости кораблей в сплавах.", 1, 1, 110f, new[]{"cns_sta_1"}, Y1, "cns_mod_1");
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
            float perDay = DailyPointsPerSlot(activeSlots) / YearPenaltyMultiplier(tech, GetCurrentYear());
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
            if (days < 60) return $"{days} дн.";
            int months = Mathf.RoundToInt(days / 30f);
            if (months < 12) return $"{months} мес.";
            int y = months / 12, m = months % 12;
            return m == 0 ? $"{y} г." : $"{y} г. {m} мес.";
        }

        // ==================== ПРОГРЕСС ====================

        private void HandleDayPassed(int day, int month, int year)
        {
            RecountPopulation();

            if (_tempBoostDays > 0 && --_tempBoostDays == 0)
            {
                TempBoost = 1f;
                NotificationCenter.Show("Научный подъём завершён", "Скорость исследований вернулась к норме", NotificationCenter.Kind.Info, 4f);
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

                slot.Progress += perSlot / YearPenaltyMultiplier(slot.CurrentTech, year);
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

            NotificationCenter.Show("Технология изучена", tech.Name, NotificationCenter.Kind.Success, 5f);
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
            var pick = available[UnityEngine.Random.Range(0, available.Count)];

            foreach (var slot in _slots) slot.Queue.Remove(pick);
            ResearchSlot host = null;
            foreach (var slot in _slots) if (slot.CurrentTech == pick) host = slot;
            if (host == null)
            {
                host = new ResearchSlot { CurrentTech = pick };
                CompleteTech(host);
            }
            else CompleteTech(host);
            OnTechProgressUpdated?.Invoke();
        }
    }
}
