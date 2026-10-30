using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public enum ShipClass { Corvette, Frigate, Destroyer }
    public enum ModuleSlotType { Weapon, Defense, Utility }
    public enum WeaponDamageType { Energy, Kinetic, Explosive }

    /// <summary>Одно орудие корабля: стреляет независимо, своим типом урона и со своей перезарядкой.</summary>
    [Serializable]
    public class WeaponMount
    {
        public WeaponDamageType Type;
        public float Damage;
        public float FireRate;
        [NonSerialized] public float Cooldown;

        public WeaponMount(WeaponDamageType type, float damage, float fireRate)
        {
            Type = type; Damage = damage; FireRate = fireRate;
        }

        public float Dps => Damage * FireRate;
    }

    [Serializable]
    public class ShipModule
    {
        public string Id;
        public string Name;
        public string Description;
        public ModuleSlotType SlotType;
        public WeaponDamageType DamageType;
        public string RequiredTechId;

        public float Hull;
        public float Armor;
        public float Shields;
        public float Damage;
        public float FireRate;
        public float Evasion;
        public float SpeedBonus;
        public float PowerProduce;
        public float PowerDraw;
        public float AlloyCost;
        /// <summary>Точность: на столько пунктов снижает уклонение цели.</summary>
        public float Accuracy;
        public float Dps => Damage * FireRate;

        public ShipModule(
            string id, string name, string desc, ModuleSlotType slot,
            string requiredTechId, float hull, float armor, float shields,
            float damage, float fireRate, float evasion, float speedBonus,
            float powerProduce, float powerDraw, float alloyCost,
            WeaponDamageType dmgType = WeaponDamageType.Kinetic)
        {
            Id = id; Name = name; Description = desc; SlotType = slot;
            RequiredTechId = requiredTechId;
            Hull = hull; Armor = armor; Shields = shields;
            Damage = damage; FireRate = fireRate; Evasion = evasion;
            SpeedBonus = speedBonus;
            PowerProduce = powerProduce; PowerDraw = powerDraw;
            AlloyCost = alloyCost; DamageType = dmgType;
        }

        public bool IsUnlocked()
        {
            if (string.IsNullOrEmpty(RequiredTechId)) return true;
            var tm = TechnologyManager.Instance;
            if (tm == null) return true;
            var tech = tm.FindTech(RequiredTechId);
            return tech != null && tech.IsResearched;
        }
    }

    [Serializable]
    public class HullBlueprint
    {
        public ShipClass Class;
        public string DisplayName;
        public int WeaponSlots;
        public int DefenseSlots;
        public int UtilitySlots;
        public float BaseHull;
        public float BaseArmor;
        public float BaseShields;
        public float BaseEvasion;
        public float BaseSpeed;
        public float BaseAlloyCost;
        public string RequiredTechId;

        public bool IsUnlocked()
        {
            if (Class == ShipClass.Destroyer)
            {
                var tm = TechnologyManager.Instance;
                if (tm != null && tm.DestroyerUnlocked) return true;
            }
            if (string.IsNullOrEmpty(RequiredTechId)) return true;
            var tech = TechnologyManager.Instance?.FindTech(RequiredTechId);
            return tech != null && tech.IsResearched;
        }
    }

    [Serializable]
    public class ShipDesign
    {
        public string Id;
        public string Name;
        public ShipClass HullClass;
        public readonly List<string> WeaponModuleIds = new List<string>();
        public readonly List<string> DefenseModuleIds = new List<string>();
        public readonly List<string> UtilityModuleIds = new List<string>();

        public float Hull;
        public float Armor;
        public float Shields;
        /// <summary>Суммарный урон одного залпа всех орудий.</summary>
        public float Damage;
        public float Evasion;
        public float Speed;
        public float Accuracy;
        public float PowerProduce;
        public float PowerDraw;
        public float PowerBalance;
        public float AlloyCost;
        public float Dps;
        public WeaponDamageType PrimaryWeapon = WeaponDamageType.Energy;
        public readonly List<WeaponMount> Mounts = new List<WeaponMount>();

        public void Recalculate(HullBlueprint hull, IReadOnlyDictionary<string, ShipModule> catalog)
        {
            Hull = hull.BaseHull;
            Armor = hull.BaseArmor;
            Shields = hull.BaseShields;
            Damage = 0f;
            Evasion = hull.BaseEvasion;
            Speed = hull.BaseSpeed;
            Accuracy = 0f;
            PowerProduce = PowerDraw = 0f;
            AlloyCost = hull.BaseAlloyCost;
            Mounts.Clear();

            void Acc(string mid)
            {
                if (string.IsNullOrEmpty(mid) || !catalog.TryGetValue(mid, out var m) || m == null) return;
                Hull += m.Hull;
                Armor += m.Armor;
                Shields += m.Shields;
                Evasion += m.Evasion;
                Speed += m.SpeedBonus;
                Accuracy += m.Accuracy;
                PowerProduce += m.PowerProduce;
                PowerDraw += m.PowerDraw;
                AlloyCost += m.AlloyCost;
                if (m.SlotType == ModuleSlotType.Weapon && m.Damage > 0f)
                {
                    Mounts.Add(new WeaponMount(m.DamageType, m.Damage, m.FireRate));
                    Damage += m.Damage;
                }
            }

            foreach (var id in WeaponModuleIds) Acc(id);
            foreach (var id in DefenseModuleIds) Acc(id);
            foreach (var id in UtilityModuleIds) Acc(id);

            Evasion = Mathf.Max(0f, Evasion);
            PowerBalance = PowerProduce - PowerDraw;
            Dps = 0f;
            foreach (var w in Mounts) Dps += w.Dps;
            PrimaryWeapon = DominantType(Mounts, WeaponDamageType.Energy);
        }

        public float DpsOf(WeaponDamageType t)
        {
            float s = 0f;
            foreach (var w in Mounts) if (w.Type == t) s += w.Dps;
            return s;
        }

        /// <summary>Тип оружия с наибольшим вкладом в урон (для цвета, подсказок и ИИ).</summary>
        public static WeaponDamageType DominantType(List<WeaponMount> mounts, WeaponDamageType fallback)
        {
            float e = 0f, k = 0f, x = 0f;
            foreach (var w in mounts)
            {
                if (w.Type == WeaponDamageType.Energy) e += w.Dps;
                else if (w.Type == WeaponDamageType.Kinetic) k += w.Dps;
                else x += w.Dps;
            }
            if (e <= 0f && k <= 0f && x <= 0f) return fallback;
            return e >= k && e >= x ? WeaponDamageType.Energy : k >= x ? WeaponDamageType.Kinetic : WeaponDamageType.Explosive;
        }

        public bool IsPowerValid => PowerBalance >= -0.01f;
    }
}
