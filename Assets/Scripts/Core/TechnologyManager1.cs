using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public enum TechCategory
    {
        Weapons, Defense, Propulsion, Sensors,
        Industry, Reactor, Construction, Society
    }

    public static class TechCategoryInfo
    {
        public static string Name(TechCategory c) => c switch
        {
            TechCategory.Weapons      => "ОРУЖИЕ",
            TechCategory.Defense      => "ЗАЩИТА",
            TechCategory.Propulsion   => "ДВИГАТЕЛИ",
            TechCategory.Sensors      => "СЕНСОРЫ",
            TechCategory.Industry     => "ПРОМЫШЛЕННОСТЬ",
            TechCategory.Reactor      => "РЕАКТОРЫ",
            TechCategory.Construction => "СТРОИТЕЛЬСТВО",
            TechCategory.Society      => "ОБЩЕСТВО",
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
        /// Множитель стоимости по уровню: базовые ×1, Tier 1 ×4.4, Tier 2 ×9. Ранние технологии
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
            (BaseScience + ScienceFromPopulation + FlatScience) * Bonuses.ResearchMult * TempBoost;

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

            // --- WEAPONS ---
            Add("wpn_kin_1", "Кинетические орудия I", TechCategory.Weapons,
                "Базовые рельсовые пушки. +10% урона кинетического оружия.", 0, 0, 60f, null, 2200, "weap_kin_1");
            Add("wpn_kin_2", "Кинетические орудия II", TechCategory.Weapons,
                "Улучшенные рельсы. +15% урона кинетического оружия.", 1, 0, 90f, new[]{"wpn_kin_1"}, 2205, "weap_kin_2");
            Add("wpn_las_1", "Лазерные батареи I", TechCategory.Weapons,
                "Первые лазеры. +10% урона энергооружия, +5 точности.", 0, 1, 70f, null, 2200, "weap_las_1");
            Add("wpn_las_2", "Лазерные батареи II", TechCategory.Weapons,
                "Улучшенные лазеры. +15% урона энергооружия, +5 точности.", 1, 1, 100f, new[]{"wpn_las_1"}, 2205, "weap_las_2");
            Add("wpn_pls_1", "Плазменные орудия", TechCategory.Weapons,
                "Разряд плазмы. +20% урона всего оружия.", 2, 2, 140f, new[]{"wpn_kin_2","wpn_las_2"}, 2210, "weap_pls_1");

            // --- DEFENSE ---
            Add("def_arm_1", "Титановая броня I", TechCategory.Defense,
                "Базовая броня. +10% прочности корпуса и брони.", 0, 0, 60f, null, 2200, "def_arm_1");
            Add("def_arm_2", "Титановая броня II", TechCategory.Defense,
                "Усиленная броня. +15% прочности корпуса и брони.", 1, 0, 90f, new[]{"def_arm_1"}, 2205, "def_arm_2");
            Add("def_shl_1", "Энергощиты I", TechCategory.Defense,
                "Первые щиты. +10% щитов.", 0, 1, 80f, null, 2200, "def_shl_1");
            Add("def_shl_2", "Энергощиты II", TechCategory.Defense,
                "Улучшенные щиты. +15% щитов.", 1, 1, 110f, new[]{"def_shl_1"}, 2205, "def_shl_2");

            // --- PROPULSION ---
            Add("prp_hyp_1", "Гипердвигатели I", TechCategory.Propulsion,
                "+15% скорости гиперпрыжков.", 0, 0, 70f, null, 2200, "prp_hyp_1");
            Add("prp_hyp_2", "Гипердвигатели II", TechCategory.Propulsion,
                "+20% скорости гиперпрыжков (итого +35%).", 1, 0, 120f, new[]{"prp_hyp_1"}, 2205, "prp_hyp_2");
            Add("prp_eng_1", "Импульсные двигатели", TechCategory.Propulsion,
                "+10% уклонения кораблей. Открывает модуль «Импульсный двигатель».", 0, 1, 60f, null, 2200, "prp_eng_1");

            // --- SENSORS ---
            Add("sen_bas_1", "Сенсорные массивы", TechCategory.Sensors,
                "+5 точности орудий, +20% скорости разведки систем.", 0, 0, 50f, null, 2200, "sen_bas_1");
            Add("sen_ai_1", "Простейший ИИ", TechCategory.Sensors,
                "+1 слот исследования.", 1, 1, 100f, new[]{"sen_bas_1"}, 2205, "slot+1");
            Add("sen_ai_2", "Продвинутый ИИ", TechCategory.Sensors,
                "+1 слот исследования.", 2, 1, 180f, new[]{"sen_ai_1"}, 2210, "slot+1");

            // --- INDUSTRY ---
            Add("ind_min_1", "Плазменные буры", TechCategory.Industry,
                "+20% добычи титана.", 0, 0, 80f, null, 2200, "ind_min_1");
            Add("ind_min_2", "Глубокое обогащение", TechCategory.Industry,
                "+15% добычи титана (итого +35%).", 1, 0, 130f, new[]{"ind_min_1"}, 2205, "ind_min_2");
            Add("ind_all_1", "Сплавы нового поколения", TechCategory.Industry,
                "+20% производства сплавов.", 0, 1, 80f, null, 2200, "ind_all_1");

            // --- REACTOR ---
            Add("rct_fus_1", "Термоядерные реакторы I", TechCategory.Reactor,
                "+4 Гелия-3 в месяц. Открывает термоядерный реактор кораблей.", 0, 0, 70f, null, 2200, "rct_fus_1");
            Add("rct_fus_2", "Термоядерные реакторы II", TechCategory.Reactor,
                "+8 Гелия-3 в месяц.", 1, 0, 120f, new[]{"rct_fus_1"}, 2205, "rct_fus_2");
            Add("rct_ant_1", "Антиматерия I", TechCategory.Reactor,
                "+12 Гелия-3 в месяц.", 2, 0, 200f, new[]{"rct_fus_2"}, 2210, "rct_ant_1");

            // --- CONSTRUCTION ---
            Add("cns_sta_1", "Орбитальные верфи", TechCategory.Construction,
                "-20% стоимости кораблей в сплавах.", 0, 0, 90f, null, 2200, "cns_sta_1");
            Add("cns_col_1", "Колониальный устав", TechCategory.Construction,
                "-10 Влияния на форпосты.", 0, 1, 70f, null, 2200, "cns_col_1");
            Add("cns_dst_1", "Верфи класса «Эсминец»", TechCategory.Construction,
                "Открывает постройку эсминцев.", 1, 1, 150f, new[]{"cns_sta_1"}, 2205, "cns_dst_1");

            // --- SOCIETY ---
            Add("soc_admin_1", "Административные системы", TechCategory.Society,
                "+1 слот исследования.", 0, 0, 100f, null, 2200, "slot+1");
            Add("soc_admin_2", "Бюрократическая реформа", TechCategory.Society,
                "+1 слот исследования.", 1, 0, 180f, new[]{"soc_admin_1"}, 2205, "slot+1");
            Add("soc_sci_1", "Научные институты", TechCategory.Society,
                "+15% скорости исследований.", 0, 1, 90f, null, 2200, "soc_sci_1");
            Add("soc_sci_2", "Нейросетевое планирование", TechCategory.Society,
                "+25% скорости исследований.", 1, 1, 150f, new[]{"soc_sci_1"}, 2205, "soc_sci_2");
            Add("soc_geo_1", "Звёздная геодезия", TechCategory.Society,
                "+35% скорости разведки систем.", 0, 2, 100f, null, 2200, "soc_geo_1");
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
