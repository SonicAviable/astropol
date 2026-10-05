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
        public float BaseDays;
        public int YearAvailable;
        public string[] RequiredTechIds;
        public string BonusKey;

        [NonSerialized] public float AccumulatedDays;
        [NonSerialized] public bool IsResearched;

        public float ProgressNormalized => Mathf.Clamp01(AccumulatedDays / Mathf.Max(1f, BaseDays));

        public Technology(string id, string name, TechCategory cat, string desc,
                          int tier, int col, float baseDays,
                          string[] reqs, int yearAvailable, string bonusKey)
        {
            Id = id; Name = name; Category = cat; Description = desc;
            Tier = tier; GridColumn = col; BaseDays = baseDays;
            RequiredTechIds = reqs ?? new string[0];
            YearAvailable = yearAvailable;
            BonusKey = bonusKey;
        }
    }

    public class ResearchSlot
    {
        public Technology CurrentTech;
        public float AccumulatedDays;
        public bool IsPaused;
        public readonly List<Technology> Queue = new List<Technology>();

        public float ProgressNormalized =>
            CurrentTech == null ? 0f : Mathf.Clamp01(AccumulatedDays / Mathf.Max(1f, CurrentTech.BaseDays));
        public float DaysRemaining =>
            CurrentTech == null ? 0f : Mathf.Max(0f, CurrentTech.BaseDays - AccumulatedDays);
    }

    public class TechnologyManager : MonoBehaviour
    {
        public static TechnologyManager Instance { get; private set; }

        public static event Action<Technology> OnTechCompleted;
        public static event Action OnTechProgressUpdated;
        public static event Action<int> OnSlotsChanged;

        [SerializeField] private int   initialSlots       = 3;
        [SerializeField] private int   maxSlots           = 6;
        [SerializeField] private float baseMonthlyScience = 35f;
        [SerializeField] private float techDayScale       = 0.65f;

        private readonly List<ResearchSlot> _slots = new List<ResearchSlot>();
        public IReadOnlyList<ResearchSlot> Slots => _slots;
        public int MaxSlots => maxSlots;

        public float GlobalResearchSpeedMultiplier { get; private set; } = 1f;

        public float HyperlaneSpeedMultiplier { get; private set; } = 1f;
        public float MiningBonusMultiplier { get; private set; } = 1f;
        public float StarbaseCostDiscount { get; private set; } = 0f;
        public float ShipBuildSpeedMultiplier { get; private set; } = 1f;
        public bool DestroyerUnlocked { get; private set; }

        public float MonthlyResearchIncome => baseMonthlyScience * GlobalResearchSpeedMultiplier;

        private readonly List<Technology> _allTechs = new List<Technology>();
        public IReadOnlyList<Technology> AllTechs => _allTechs;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            for (int i = 0; i < initialSlots; i++) _slots.Add(new ResearchSlot());
            BuildTechTree();
        }

        private void Start()
        {
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed += HandleDayPassed;
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed -= HandleDayPassed;
        }

        // ==================== ДЕРЕВО ====================

        private void BuildTechTree()
        {
            // --- WEAPONS ---
            Add("wpn_kin_1", "Кинетические орудия I", TechCategory.Weapons,
                "Базовые рельсовые пушки. +10% урона.", 0, 0, 60f, null, 2200, "weap_kin_1");
            Add("wpn_kin_2", "Кинетические орудия II", TechCategory.Weapons,
                "Улучшенные рельсы. +15% урона.", 1, 0, 90f, new[]{"wpn_kin_1"}, 2205, "weap_kin_2");
            Add("wpn_las_1", "Лазерные батареи I", TechCategory.Weapons,
                "Первые лазеры. +10% точности.", 0, 1, 70f, null, 2200, "weap_las_1");
            Add("wpn_las_2", "Лазерные батареи II", TechCategory.Weapons,
                "Улучшенные лазеры. +15% точности.", 1, 1, 100f, new[]{"wpn_las_1"}, 2205, "weap_las_2");
            Add("wpn_pls_1", "Плазменные орудия", TechCategory.Weapons,
                "Разряд плазмы. +20% урона.", 2, 2, 140f, new[]{"wpn_kin_2","wpn_las_2"}, 2210, "weap_pls_1");

            // --- DEFENSE ---
            Add("def_arm_1", "Титановая броня I", TechCategory.Defense,
                "Базовая броня. +10% HP.", 0, 0, 60f, null, 2200, "def_arm_1");
            Add("def_arm_2", "Титановая броня II", TechCategory.Defense,
                "Усиленная броня. +15% HP.", 1, 0, 90f, new[]{"def_arm_1"}, 2205, "def_arm_2");
            Add("def_shl_1", "Энергощиты I", TechCategory.Defense,
                "Первые щиты. +10% щита.", 0, 1, 80f, null, 2200, "def_shl_1");
            Add("def_shl_2", "Энергощиты II", TechCategory.Defense,
                "Улучшенные щиты. +15% щита.", 1, 1, 110f, new[]{"def_shl_1"}, 2205, "def_shl_2");

            // --- PROPULSION ---
            Add("prp_hyp_1", "Гипердвигатели I", TechCategory.Propulsion,
                "+15% скорости гиперпрыжков.", 0, 0, 70f, null, 2200, "prp_hyp_1");
            Add("prp_hyp_2", "Гипердвигатели II", TechCategory.Propulsion,
                "+30% скорости гиперпрыжков.", 1, 0, 120f, new[]{"prp_hyp_1"}, 2205, "prp_hyp_2");
            Add("prp_eng_1", "Импульсные двигатели", TechCategory.Propulsion,
                "+10% маневренности.", 0, 1, 60f, null, 2200, "prp_eng_1");

            // --- SENSORS ---
            Add("sen_bas_1", "Сенсорные массивы", TechCategory.Sensors,
                "+20% радиуса обнаружения.", 0, 0, 50f, null, 2200, "sen_bas_1");
            Add("sen_ai_1", "Простейший ИИ", TechCategory.Sensors,
                "+1 слот исследования.", 1, 1, 100f, new[]{"sen_bas_1"}, 2205, "slot+1");
            Add("sen_ai_2", "Продвинутый ИИ", TechCategory.Sensors,
                "+1 слот исследования.", 2, 1, 180f, new[]{"sen_ai_1"}, 2210, "slot+1");

            // --- INDUSTRY ---
            Add("ind_min_1", "Плазменные буры", TechCategory.Industry,
                "+20% добычи титана.", 0, 0, 80f, null, 2200, "ind_min_1");
            Add("ind_min_2", "Глубокое обогащение", TechCategory.Industry,
                "+35% добычи титана.", 1, 0, 130f, new[]{"ind_min_1"}, 2205, "ind_min_2");
            Add("ind_all_1", "Сплавы нового поколения", TechCategory.Industry,
                "+20% производства сплавов.", 0, 1, 80f, null, 2200, "ind_all_1");

            // --- REACTOR ---
            Add("rct_fus_1", "Термоядерные реакторы I", TechCategory.Reactor,
                "+4 Гелия-3 в месяц.", 0, 0, 70f, null, 2200, "rct_fus_1");
            Add("rct_fus_2", "Термоядерные реакторы II", TechCategory.Reactor,
                "+8 Гелия-3 в месяц.", 1, 0, 120f, new[]{"rct_fus_1"}, 2205, "rct_fus_2");
            Add("rct_ant_1", "Антиматерия I", TechCategory.Reactor,
                "+12 Гелия-3 в месяц.", 2, 0, 200f, new[]{"rct_fus_2"}, 2210, "rct_ant_1");

            // --- CONSTRUCTION ---
            Add("cns_sta_1", "Орбитальные верфи", TechCategory.Construction,
                "+25% скорости постройки кораблей.", 0, 0, 90f, null, 2200, "cns_sta_1");
            Add("cns_col_1", "Колониальный устав", TechCategory.Construction,
                "-10 Влияния на аванпосты.", 0, 1, 70f, null, 2200, "cns_col_1");
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
        }

        private void Add(string id, string name, TechCategory cat, string desc,
                         int tier, int col, float days, string[] reqs, int year, string bonusKey)
        {
            _allTechs.Add(new Technology(id, name, cat, desc, tier, col,
                days * techDayScale, reqs, year, bonusKey));
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
                    s.AccumulatedDays = tech.AccumulatedDays;
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
                if (s.DaysRemaining < minRemaining) { minRemaining = s.DaysRemaining; best = s; }
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
                slot.CurrentTech.AccumulatedDays = slot.AccumulatedDays;
                slot.CurrentTech = null;
                slot.AccumulatedDays = 0f;
            }
            OnTechProgressUpdated?.Invoke();
        }

        public void ToggleSlotPause(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return;
            _slots[slotIndex].IsPaused = !_slots[slotIndex].IsPaused;
            OnTechProgressUpdated?.Invoke();
        }

        // ==================== ПРОГРЕСС ====================

        private void HandleDayPassed(int day, int month, int year)
        {
            bool bootstrapped = false;
            foreach (var slot in _slots)
            {
                if (slot.CurrentTech == null && slot.Queue.Count > 0)
                {
                    slot.CurrentTech = slot.Queue[0];
                    slot.Queue.RemoveAt(0);
                    slot.AccumulatedDays = slot.CurrentTech.AccumulatedDays;
                    slot.IsPaused = false;
                    bootstrapped = true;
                }
            }
            if (bootstrapped) OnTechProgressUpdated?.Invoke();

            int activeSlots = 0;
            foreach (var s in _slots)
                if (s.CurrentTech != null && !s.IsPaused) activeSlots++;

            if (activeSlots == 0) return;

            float speedPerSlot = MonthlyResearchIncome / Mathf.Pow(activeSlots, 0.7f);

            bool changed = false;
            foreach (var slot in _slots)
            {
                if (slot.CurrentTech == null || slot.IsPaused) continue;

                float penalty = YearPenaltyMultiplier(slot.CurrentTech, year);
                float gainedThisDay = speedPerSlot / 30f / penalty;
                slot.AccumulatedDays += gainedThisDay;
                slot.CurrentTech.AccumulatedDays = slot.AccumulatedDays;

                if (slot.AccumulatedDays >= slot.CurrentTech.BaseDays)
                    CompleteTech(slot);

                changed = true;
            }

            if (changed) OnTechProgressUpdated?.Invoke();
        }

        private float YearPenaltyMultiplier(Technology tech, int currentYear)
        {
            if (currentYear >= tech.YearAvailable) return 1f;
            int yearsAhead = tech.YearAvailable - currentYear;
            return 1f + yearsAhead;
        }

        public float GetYearPenalty(Technology tech)
        {
            return YearPenaltyMultiplier(tech, GetCurrentYear());
        }

        private int GetCurrentYear()
        {
            if (TimeManager.Instance == null) return 2200;
            var str = TimeManager.Instance.GetFormattedDate();
            var parts = str.Split('.');
            if (parts.Length >= 3 && int.TryParse(parts[2], out int y)) return y;
            return 2200;
        }

        private void CompleteTech(ResearchSlot slot)
        {
            var tech = slot.CurrentTech;
            tech.IsResearched = true;
            tech.AccumulatedDays = tech.BaseDays;

            ApplyTechBonus(tech);
            OnTechCompleted?.Invoke(tech);

            slot.CurrentTech = null;
            slot.AccumulatedDays = 0f;

            NotificationCenter.Show("Технология изучена", tech.Name, NotificationCenter.Kind.Success, 5f);
            Debug.Log($"<color=#3FE>[Наука] Изучено: {tech.Name}</color>");
        }

        // ==================== БОНУСЫ ====================

        private void ApplyTechBonus(Technology tech)
        {
            switch (tech.BonusKey)
            {
                case "prp_hyp_1": HyperlaneSpeedMultiplier = 1.15f; break;
                case "prp_hyp_2": HyperlaneSpeedMultiplier = 1.35f; break;

                case "rct_fus_1": EconomyManager.Instance?.AddIncome(4f, 0f, 0f, 0f); break;
                case "rct_fus_2": EconomyManager.Instance?.AddIncome(8f, 0f, 0f, 0f); break;
                case "rct_ant_1": EconomyManager.Instance?.AddIncome(12f, 0f, 0f, 0f); break;

                case "cns_col_1": StarbaseCostDiscount = 10f; break;
                case "cns_sta_1": ShipBuildSpeedMultiplier = 1.25f; break;
                case "cns_dst_1": DestroyerUnlocked = true; break;

                case "ind_min_1": MiningBonusMultiplier = 1.20f; break;
                case "ind_min_2": MiningBonusMultiplier = 1.35f; break;

                case "soc_sci_1": GlobalResearchSpeedMultiplier += 0.15f; break;
                case "soc_sci_2": GlobalResearchSpeedMultiplier += 0.25f; break;

                case "slot+1": AddSlot(); break;
            }
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
                BaseMonthlyScience = baseMonthlyScience,
                ResearchMult = GlobalResearchSpeedMultiplier,
                HyperlaneMult = HyperlaneSpeedMultiplier,
                MiningMult = MiningBonusMultiplier,
                ShipBuildMult = ShipBuildSpeedMultiplier,
                StarbaseDiscount = StarbaseCostDiscount,
                DestroyerUnlocked = DestroyerUnlocked
            };
            foreach (var t in _allTechs)
            {
                if (t.IsResearched) s.Researched.Add(t.Id);
                else if (t.AccumulatedDays > 0f) { s.ProgressIds.Add(t.Id); s.ProgressDays.Add(t.AccumulatedDays); }
            }
            foreach (var slot in _slots)
            {
                var ss = new SlotSave
                {
                    Current = slot.CurrentTech?.Id,
                    Days = slot.AccumulatedDays,
                    Paused = slot.IsPaused
                };
                foreach (var q in slot.Queue) ss.Queue.Add(q.Id);
                s.Slots.Add(ss);
            }
            return s;
        }

        public void RestoreState(TechSave s)
        {
            if (s == null) return;
            foreach (var t in _allTechs) { t.IsResearched = false; t.AccumulatedDays = 0f; }
            foreach (var id in s.Researched)
            {
                var t = FindTech(id);
                if (t == null) continue;
                t.IsResearched = true;
                t.AccumulatedDays = t.BaseDays;
            }
            for (int i = 0; i < s.ProgressIds.Count && i < s.ProgressDays.Count; i++)
            {
                var t = FindTech(s.ProgressIds[i]);
                if (t != null) t.AccumulatedDays = s.ProgressDays[i];
            }

            if (s.BaseMonthlyScience > 0f) baseMonthlyScience = s.BaseMonthlyScience;
            GlobalResearchSpeedMultiplier = s.ResearchMult;
            HyperlaneSpeedMultiplier = s.HyperlaneMult;
            MiningBonusMultiplier = s.MiningMult;
            ShipBuildSpeedMultiplier = s.ShipBuildMult;
            StarbaseCostDiscount = s.StarbaseDiscount;
            DestroyerUnlocked = s.DestroyerUnlocked;

            _slots.Clear();
            foreach (var ss in s.Slots)
            {
                var slot = new ResearchSlot { IsPaused = ss.Paused };
                var cur = string.IsNullOrEmpty(ss.Current) ? null : FindTech(ss.Current);
                if (cur != null && !cur.IsResearched) { slot.CurrentTech = cur; slot.AccumulatedDays = ss.Days; }
                foreach (var qid in ss.Queue)
                {
                    var q = FindTech(qid);
                    if (q != null && !q.IsResearched) slot.Queue.Add(q);
                }
                _slots.Add(slot);
            }
            while (_slots.Count < initialSlots) _slots.Add(new ResearchSlot());

            OnSlotsChanged?.Invoke(_slots.Count);
            OnTechProgressUpdated?.Invoke();
        }

        // ==================== ДЛЯ ИВЕНТОВ ====================

        public void AddInstantScience(float sciencePoints)
        {
            if (sciencePoints <= 0) return;
            float daysEquivalent = Mathf.Max(1f, sciencePoints / Mathf.Max(1f, baseMonthlyScience)) * 30f;
            float remaining = daysEquivalent;
            bool changed = false;
            foreach (var slot in _slots)
            {
                if (slot.CurrentTech == null || slot.IsPaused) continue;
                float toInject = Mathf.Min(remaining, slot.CurrentTech.BaseDays - slot.AccumulatedDays + 1f);
                slot.AccumulatedDays += toInject;
                slot.CurrentTech.AccumulatedDays = slot.AccumulatedDays;
                remaining -= toInject;
                changed = true;
                if (slot.AccumulatedDays >= slot.CurrentTech.BaseDays)
                    CompleteTech(slot);
                if (remaining <= 0) break;
            }
            if (changed) OnTechProgressUpdated?.Invoke();
        }

        public void AddMonthlyScience(float amount)
        {
            if (amount <= 0) return;
            baseMonthlyScience += amount;
            OnTechProgressUpdated?.Invoke();
        }

        public void ApplyTempResearchBoost(float multiplier, int days = 30)
        {
            GlobalResearchSpeedMultiplier *= Mathf.Max(1f, multiplier);
            OnTechProgressUpdated?.Invoke();
        }

        public void CompleteRandomTech()
        {
            var available = new List<Technology>();
            foreach (var t in _allTechs)
                if (!t.IsResearched) available.Add(t);
            if (available.Count == 0) return;
            var pick = available[UnityEngine.Random.Range(0, available.Count)];
            AssignTech(pick);
            foreach (var slot in _slots)
            {
                if (slot.CurrentTech == pick)
                {
                    slot.AccumulatedDays = pick.BaseDays;
                    pick.AccumulatedDays = pick.BaseDays;
                    CompleteTech(slot);
                    break;
                }
            }
        }
    }
}