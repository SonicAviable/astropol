using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public enum ShipClass { Corvette, Frigate, Destroyer }
    public enum ModuleSlotType { Weapon, Defense, Utility }
    public enum WeaponDamageType { Energy, Kinetic, Explosive }

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
        public float Damage;
        public float FireRate;
        public float Evasion;
        public float Speed;
        public float PowerBalance;
        public float AlloyCost;
        public float Dps;
        public WeaponDamageType PrimaryWeapon = WeaponDamageType.Energy;

        public void Recalculate(HullBlueprint hull, IReadOnlyDictionary<string, ShipModule> catalog)
        {
            Hull = hull.BaseHull;
            Armor = hull.BaseArmor;
            Shields = hull.BaseShields;
            Damage = 0f;
            FireRate = 0f;
            Evasion = hull.BaseEvasion;
            Speed = hull.BaseSpeed;
            PowerBalance = 0f;
            AlloyCost = hull.BaseAlloyCost;
            PrimaryWeapon = WeaponDamageType.Energy;
            float bestDmg = -1f;

            void Acc(string mid)
            {
                if (string.IsNullOrEmpty(mid) || !catalog.TryGetValue(mid, out var m) || m == null) return;
                Hull += m.Hull;
                Armor += m.Armor;
                Shields += m.Shields;
                Damage += m.Damage;
                FireRate += m.FireRate;
                Evasion += m.Evasion;
                Speed += m.SpeedBonus;
                PowerBalance += m.PowerProduce - m.PowerDraw;
                AlloyCost += m.AlloyCost;
                if (m.SlotType == ModuleSlotType.Weapon && m.Damage > bestDmg)
                {
                    bestDmg = m.Damage;
                    PrimaryWeapon = m.DamageType;
                }
            }

            foreach (var id in WeaponModuleIds) Acc(id);
            foreach (var id in DefenseModuleIds) Acc(id);
            foreach (var id in UtilityModuleIds) Acc(id);

            if (FireRate < 0.25f && Damage > 0f) FireRate = 0.6f;
            Dps = Damage * FireRate;
        }

        public bool IsPowerValid => PowerBalance >= -0.01f;
    }
}
