using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public class ShipDesignManager : MonoBehaviour
    {
        public static ShipDesignManager Instance { get; private set; }

        public static event Action OnDesignsChanged;

        private readonly Dictionary<string, ShipModule> _modules = new Dictionary<string, ShipModule>();
        private readonly List<HullBlueprint> _hulls = new List<HullBlueprint>();
        private readonly List<ShipDesign> _saved = new List<ShipDesign>();

        public IReadOnlyDictionary<string, ShipModule> Modules => _modules;
        public IReadOnlyList<HullBlueprint> Hulls => _hulls;
        public IReadOnlyList<ShipDesign> SavedDesigns => _saved;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildCatalog();
            EnsureDefaultDesigns();
        }

        private void BuildCatalog()
        {
            _hulls.Clear();
            _hulls.Add(new HullBlueprint
            {
                Class = ShipClass.Corvette, DisplayName = "Корвет",
                WeaponSlots = 2, DefenseSlots = 1, UtilitySlots = 2,
                BaseHull = 80f, BaseArmor = 10f, BaseShields = 8f,
                BaseEvasion = 18f, BaseSpeed = 1.15f, BaseAlloyCost = 60f
            });
            _hulls.Add(new HullBlueprint
            {
                Class = ShipClass.Frigate, DisplayName = "Фрегат",
                WeaponSlots = 3, DefenseSlots = 2, UtilitySlots = 2,
                BaseHull = 140f, BaseArmor = 22f, BaseShields = 18f,
                BaseEvasion = 10f, BaseSpeed = 1.0f, BaseAlloyCost = 110f
            });
            _hulls.Add(new HullBlueprint
            {
                Class = ShipClass.Destroyer, DisplayName = "Эсминец",
                WeaponSlots = 4, DefenseSlots = 3, UtilitySlots = 3,
                BaseHull = 260f, BaseArmor = 40f, BaseShields = 36f,
                BaseEvasion = 6f, BaseSpeed = 0.85f, BaseAlloyCost = 180f,
                RequiredTechId = "cns_dst_1"
            });

            AddMod(new ShipModule("wpn_laser_red", "Красный лазер",
                "Энергетический луч. Эффективен против брони.",
                ModuleSlotType.Weapon, "",
                0, 0, 0, 9f, 0.85f, 0, 0, 0, 4f, 18f, WeaponDamageType.Energy));

            AddMod(new ShipModule("wpn_autocannon", "Автопушка",
                "Кинетические снаряды. Ломают щиты.",
                ModuleSlotType.Weapon, "",
                0, 0, 0, 8f, 1.15f, 0, 0, 0, 3f, 16f, WeaponDamageType.Kinetic));

            AddMod(new ShipModule("wpn_missile", "Ракетная установка",
                "Взрывчатые боеголовки. Удар по всем слоям защиты.",
                ModuleSlotType.Weapon, "",
                0, 0, 0, 14f, 0.45f, 0, 0, 0, 5f, 24f, WeaponDamageType.Explosive));

            AddMod(new ShipModule("def_shield", "Дефлекторный щит",
                "+Щиты. Требует энергию реактора.",
                ModuleSlotType.Defense, "",
                0, 0, 42f, 0, 0, 2f, 0, 0, 8f, 22f));

            AddMod(new ShipModule("def_armor", "Нанокомпозитная броня",
                "+Броня. Не потребляет энергию.",
                ModuleSlotType.Defense, "",
                8f, 48f, 0, 0, 0, -2f, 0, 0, 0, 20f));

            AddMod(new ShipModule("utl_reactor", "Реактор",
                "Выработка энергоёмкости Power.",
                ModuleSlotType.Utility, "",
                0, 0, 0, 0, 0, 0, 0, 16f, 0, 28f));

            AddMod(new ShipModule("utl_reactor_fus", "Термоядерный реактор",
                "Усиленная энергоёмкость.",
                ModuleSlotType.Utility, "rct_fus_1",
                0, 0, 0, 0, 0, 0, 0, 28f, 0, 40f));

            AddMod(new ShipModule("utl_engine", "Импульсный двигатель",
                "Скорость гиперперехода и уклонение.",
                ModuleSlotType.Utility, "prp_eng_1",
                0, 0, 0, 0, 0, 6f, 0.25f, 0, 4f, 18f));

            AddMod(new ShipModule("utl_engine_basic", "Маршевый двигатель",
                "Базовая тяга.",
                ModuleSlotType.Utility, "",
                0, 0, 0, 0, 0, 3f, 0.12f, 0, 2f, 10f));
        }

        private void AddMod(ShipModule m) => _modules[m.Id] = m;

        public HullBlueprint GetHull(ShipClass c) => _hulls.Find(h => h.Class == c);

        public ShipModule GetModule(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _modules.TryGetValue(id, out var m) ? m : null;
        }

        public List<ShipModule> GetUnlockedModules(ModuleSlotType slot)
        {
            var list = new List<ShipModule>();
            foreach (var m in _modules.Values)
            {
                if (m.SlotType != slot) continue;
                if (m.IsUnlocked()) list.Add(m);
            }
            return list;
        }

        public ShipDesign GetDesign(string id) => _saved.Find(d => d.Id == id);

        public ShipDesign GetLatestDesign(ShipClass hull)
        {
            ShipDesign best = null;
            foreach (var d in _saved)
            {
                if (d.HullClass != hull) continue;
                if (best == null || d.AlloyCost >= best.AlloyCost) best = d;
            }
            return best;
        }

        public ShipDesign CreateDraft(ShipClass hullClass)
        {
            var hull = GetHull(hullClass);
            var d = new ShipDesign
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = $"Проект «{hull.DisplayName}»",
                HullClass = hullClass
            };
            FillEmptySlots(d, hull);
            d.Recalculate(hull, _modules);
            return d;
        }

        private void FillEmptySlots(ShipDesign d, HullBlueprint hull)
        {
            while (d.WeaponModuleIds.Count < hull.WeaponSlots) d.WeaponModuleIds.Add("");
            while (d.DefenseModuleIds.Count < hull.DefenseSlots) d.DefenseModuleIds.Add("");
            while (d.UtilityModuleIds.Count < hull.UtilitySlots) d.UtilityModuleIds.Add("");
            if (d.WeaponModuleIds.Count > hull.WeaponSlots) d.WeaponModuleIds.RemoveRange(hull.WeaponSlots, d.WeaponModuleIds.Count - hull.WeaponSlots);
            if (d.DefenseModuleIds.Count > hull.DefenseSlots) d.DefenseModuleIds.RemoveRange(hull.DefenseSlots, d.DefenseModuleIds.Count - hull.DefenseSlots);
            if (d.UtilityModuleIds.Count > hull.UtilitySlots) d.UtilityModuleIds.RemoveRange(hull.UtilitySlots, d.UtilityModuleIds.Count - hull.UtilitySlots);
        }

        public void Recalc(ShipDesign d)
        {
            if (d == null) return;
            var hull = GetHull(d.HullClass);
            FillEmptySlots(d, hull);
            d.Recalculate(hull, _modules);
        }

        public bool SaveDesign(ShipDesign d)
        {
            if (d == null) return false;
            Recalc(d);
            if (!d.IsPowerValid) return false;
            var existing = GetDesign(d.Id);
            if (existing != null)
            {
                _saved.Remove(existing);
            }
            _saved.Add(d);
            OnDesignsChanged?.Invoke();
            return true;
        }

        /// <summary>Проекты из сохранения (заменяют стартовые).</summary>
        public void RestoreDesigns(List<ShipDesign> designs)
        {
            if (designs == null || designs.Count == 0) return;
            _saved.Clear();
            foreach (var d in designs)
            {
                if (d == null || GetHull(d.HullClass) == null) continue;
                Recalc(d);
                _saved.Add(d);
            }
            EnsureDefaultDesigns();
            OnDesignsChanged?.Invoke();
        }

        private void EnsureDefaultDesigns()
        {
            if (_saved.Count > 0) return;
            var corvette = CreateDraft(ShipClass.Corvette);
            corvette.Name = "Корвет «Страж»";
            corvette.WeaponModuleIds[0] = "wpn_missile";
            corvette.WeaponModuleIds[1] = "wpn_missile";
            corvette.DefenseModuleIds[0] = "def_armor";
            corvette.UtilityModuleIds[0] = "utl_reactor";
            corvette.UtilityModuleIds[1] = "utl_engine_basic";
            Recalc(corvette);
            _saved.Add(corvette);
        }

        public float RetrofitCost(FleetData fleet)
        {
            if (fleet == null) return 0f;
            var latest = GetLatestDesign(fleet.HullClass);
            if (latest == null) return 0f;
            if (fleet.DesignId == latest.Id) return 0f;
            return Mathf.Max(0f, latest.AlloyCost - fleet.DesignAlloyCost);
        }

        public bool CanRetrofit(FleetData fleet)
        {
            if (fleet == null || fleet.Type != FleetType.Military) return false;
            var latest = GetLatestDesign(fleet.HullClass);
            if (latest == null) return false;
            if (fleet.DesignId == latest.Id) return false;
            var eco = EconomyManager.Instance;
            float cost = RetrofitCost(fleet);
            return eco != null && eco.CanAfford(0f, 0f, cost, 0f);
        }

        public bool TryRetrofit(FleetData fleet)
        {
            if (!CanRetrofit(fleet)) return false;
            var latest = GetLatestDesign(fleet.HullClass);
            float cost = RetrofitCost(fleet);
            if (!EconomyManager.Instance.TrySpend(0f, 0f, cost, 0f)) return false;
            fleet.ApplyDesign(latest);
            return true;
        }
    }
}
