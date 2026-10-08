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

            // ---- Улучшенные модули: открываются технологиями ----

            AddMod(new ShipModule("wpn_railgun", "Рельсотрон",
                "Тяжёлые кинетические болванки. Быстро срывают щиты.",
                ModuleSlotType.Weapon, "wpn_kin_2",
                0, 0, 0, 13f, 1.05f, 0, 0, 0, 5f, 26f, WeaponDamageType.Kinetic));

            AddMod(new ShipModule("wpn_laser_blue", "Синий лазер",
                "Сфокусированный луч высокой энергии. Прожигает броню.",
                ModuleSlotType.Weapon, "wpn_las_2",
                0, 0, 0, 14f, 0.85f, 0, 0, 0, 6f, 28f, WeaponDamageType.Energy));

            AddMod(new ShipModule("wpn_plasma", "Плазменная пушка",
                "Медленный, но сокрушительный разряд плазмы по броне.",
                ModuleSlotType.Weapon, "wpn_pls_1",
                0, 0, 0, 26f, 0.6f, 0, 0, 0, 10f, 42f, WeaponDamageType.Energy));

            AddMod(new ShipModule("wpn_torpedo", "Торпедный аппарат",
                "Тяжёлые боеголовки, бьют по всем слоям защиты.",
                ModuleSlotType.Weapon, "wpn_pls_1",
                0, 0, 0, 30f, 0.38f, 0, 0, 0, 7f, 38f, WeaponDamageType.Explosive));

            AddMod(new ShipModule("def_armor_2", "Керамостальная броня",
                "Толстые плиты: много брони, корабль чуть тяжелее.",
                ModuleSlotType.Defense, "def_arm_2",
                12f, 80f, 0, 0, 0, -3f, 0, 0, 0, 32f));

            AddMod(new ShipModule("def_shield_2", "Усиленный дефлектор",
                "Мощное поле, но прожорливое по энергии.",
                ModuleSlotType.Defense, "def_shl_2",
                0, 0, 72f, 0, 0, 2f, 0, 0, 12f, 34f));

            AddMod(new ShipModule("utl_reactor_ant", "Реактор антиматерии",
                "Огромная выработка энергии для тяжёлых проектов.",
                ModuleSlotType.Utility, "rct_ant_1",
                0, 0, 0, 0, 0, 0, 0, 44f, 0, 60f));

            AddMod(new ShipModule("utl_targeting", "Компьютер наведения",
                "Снижает уклонение целей на 10 пунктов.",
                ModuleSlotType.Utility, "sen_bas_1",
                0, 0, 0, 0, 0, 0, 0, 0, 3f, 20f) { Accuracy = 10f });
        }

        public static string DamageTypeName(WeaponDamageType t) => t switch
        {
            WeaponDamageType.Energy => "энергия",
            WeaponDamageType.Kinetic => "кинетика",
            _ => "взрыв"
        };

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

        /// <summary>
        /// Основной проект корпуса — последний сохранённый (по нему строит верфь и идёт модернизация).
        /// Раньше брался самый дорогой, и дешёвый новый проект никогда не шёл в производство.
        /// </summary>
        public ShipDesign GetLatestDesign(ShipClass hull)
        {
            for (int i = _saved.Count - 1; i >= 0; i--)
                if (_saved[i].HullClass == hull) return _saved[i];
            return null;
        }

        public List<ShipDesign> DesignsFor(ShipClass hull)
        {
            var list = new List<ShipDesign>();
            foreach (var d in _saved) if (d.HullClass == hull) list.Add(d);
            return list;
        }

        /// <summary>Сделать проект основным для своего корпуса.</summary>
        public void MakePrimary(ShipDesign d)
        {
            if (d == null || !_saved.Remove(d)) return;
            _saved.Add(d);
            OnDesignsChanged?.Invoke();
        }

        /// <summary>Удалить проект (последний проект корпуса удалить нельзя — верфи нужен чертёж).</summary>
        public bool DeleteDesign(ShipDesign d)
        {
            if (d == null || DesignsFor(d.HullClass).Count <= 1) return false;
            bool ok = _saved.Remove(d);
            if (ok) OnDesignsChanged?.Invoke();
            return ok;
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

        private static bool Available(ShipModule m, Func<string, bool> hasTech)
            => string.IsNullOrEmpty(m.RequiredTechId) || (hasTech != null && hasTech(m.RequiredTechId));

        /// <summary>Лучший доступный модуль слота по оценке score (null — подходящих нет).</summary>
        private string Best(ModuleSlotType slot, Func<ShipModule, bool> filter, Func<ShipModule, float> score, Func<string, bool> hasTech)
        {
            ShipModule best = null;
            foreach (var m in _modules.Values)
            {
                if (m.SlotType != slot || !filter(m) || !Available(m, hasTech)) continue;
                if (best == null || score(m) > score(best)) best = m;
            }
            return best?.Id;
        }

        private string BestWeapon(WeaponDamageType t, Func<string, bool> hasTech)
            => Best(ModuleSlotType.Weapon, m => m.DamageType == t, m => m.Dps, hasTech)
               ?? (t == WeaponDamageType.Energy ? "wpn_laser_red" : t == WeaponDamageType.Kinetic ? "wpn_autocannon" : "wpn_missile");

        /// <summary>
        /// Автопроект для ИИ: лучшее изученное оружие по вкусу фракции (основное и запасное
        /// вперемешку), броня и щиты через слот, реактор, затем двигатель или компьютер наведения;
        /// при нехватке энергии — больше реакторов, броня вместо щитов, экономные орудия.
        /// hasTech — технологии владельца проекта.
        /// </summary>
        public ShipDesign CreateAutoDesign(ShipClass cls, WeaponDamageType primary, WeaponDamageType secondary,
                                           Func<string, bool> hasTech, string name)
        {
            var hull = GetHull(cls);
            if (hull == null) return null;
            var d = new ShipDesign { Id = "auto_" + cls + "_" + Guid.NewGuid().ToString("N").Substring(0, 6), Name = name, HullClass = cls };
            FillEmptySlots(d, hull);

            string wPrimary = BestWeapon(primary, hasTech), wSecondary = BestWeapon(secondary, hasTech);
            for (int i = 0; i < d.WeaponModuleIds.Count; i++)
                d.WeaponModuleIds[i] = i % 2 == 0 ? wPrimary : wSecondary;

            string armor = Best(ModuleSlotType.Defense, m => m.Armor > 0f, m => m.Armor, hasTech) ?? "def_armor";
            string shield = Best(ModuleSlotType.Defense, m => m.Shields > 0f, m => m.Shields, hasTech) ?? "def_shield";
            for (int i = 0; i < d.DefenseModuleIds.Count; i++)
                d.DefenseModuleIds[i] = i % 2 == 0 ? armor : shield;

            string reactor = Best(ModuleSlotType.Utility, m => m.PowerProduce > 0f, m => m.PowerProduce, hasTech) ?? "utl_reactor";
            string engine = Best(ModuleSlotType.Utility, m => m.SpeedBonus > 0f, m => m.Evasion + m.SpeedBonus * 10f, hasTech) ?? "utl_engine_basic";
            string targeting = Best(ModuleSlotType.Utility, m => m.Accuracy > 0f, m => m.Accuracy, hasTech);
            for (int i = 0; i < d.UtilityModuleIds.Count; i++)
                d.UtilityModuleIds[i] = i == 0 ? reactor : i == 2 && targeting != null ? targeting : engine;
            d.Recalculate(hull, _modules);

            // Не хватает энергии — двигатели и прицелы меняем на реакторы, затем щиты на броню, затем оружие на экономное
            for (int i = d.UtilityModuleIds.Count - 1; i >= 1 && !d.IsPowerValid; i--)
            { d.UtilityModuleIds[i] = reactor; d.Recalculate(hull, _modules); }
            for (int i = 0; i < d.DefenseModuleIds.Count && !d.IsPowerValid; i++)
                if (d.DefenseModuleIds[i] == shield) { d.DefenseModuleIds[i] = armor; d.Recalculate(hull, _modules); }
            for (int i = 0; i < d.WeaponModuleIds.Count && !d.IsPowerValid; i++)
            { d.WeaponModuleIds[i] = "wpn_autocannon"; d.Recalculate(hull, _modules); }
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
