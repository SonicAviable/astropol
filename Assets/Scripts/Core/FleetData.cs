using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public enum FleetType { Military, Constructor, Science }
    public enum FleetState { Orbiting, InHyperlane, Constructing, Surveying }

    public class FleetData
    {
        public int Id;
        public string Name;
        public FleetType Type;
        public int OwnerId;
        public int CurrentSystemId;
        public int TargetSystemId = -1;
        public FleetState State = FleetState.Orbiting;

        public Queue<int> Path = new Queue<int>();
        public float DaysRemainingInTransit;
        public float TotalDaysForTransit = 15f;
        public int MilitaryPower = 120;

        public int BuildTargetSystemId = -1;
        public float DaysRemainingConstruction = 0f;
        public float TotalConstructionDays = 25f;

        public int SurveyTargetSystemId = -1;
        public float DaysRemainingSurvey = 0f;
        public float TotalSurveyDays = 20f;

        public ShipClass HullClass = ShipClass.Corvette;
        public string DesignId;
        public float DesignAlloyCost;

        public float HullPoints = 80f;
        public float ArmorPoints = 20f;
        public float ShieldPoints = 30f;
        public float MaxHullPoints = 80f;
        public float MaxArmorPoints = 20f;
        public float MaxShieldPoints = 30f;
        public float Damage = 8f;
        public float FireRate = 1f;
        public float Evasion = 12f;
        public WeaponDamageType PrimaryWeapon = WeaponDamageType.Kinetic;
        public float HyperSpeed = 1f;

        // Ежемесячное содержание (энергия)
        public float UpkeepEnergy = 1f;

        public bool InCombat;
        public float FireCooldown;
        public bool Destroyed;

        public FleetData(int id, string name, int startSystemId, FleetType type = FleetType.Military)
        {
            Id = id;
            Name = name;
            CurrentSystemId = startSystemId;
            Type = type;
            OwnerId = 0;
            MilitaryPower = type == FleetType.Military ? 120 : 0;
            ApplyDefaultCombatStats(type);
        }

        public void ApplyDefaultCombatStats(FleetType type)
        {
            switch (type)
            {
                case FleetType.Military:    UpkeepEnergy = 2.5f; break;
                case FleetType.Constructor: UpkeepEnergy = 1.5f; break;
                case FleetType.Science:     UpkeepEnergy = 1.0f; break;
                default:                    UpkeepEnergy = 1.0f; break;
            }

            if (type != FleetType.Military)
            {
                HullPoints = MaxHullPoints = 40f;
                ArmorPoints = MaxArmorPoints = 8f;
                ShieldPoints = MaxShieldPoints = 0f;
                Damage = 0f;
                FireRate = 0f;
                Evasion = 8f;
                MilitaryPower = 0;
                return;
            }

            var dm = ShipDesignManager.Instance;
            var design = dm != null ? dm.GetLatestDesign(HullClass) : null;
            if (design != null)
            {
                ApplyDesign(design);
                return;
            }

            HullPoints = MaxHullPoints = 80f;
            ArmorPoints = MaxArmorPoints = 20f;
            ShieldPoints = MaxShieldPoints = 30f;
            Damage = 8f;
            FireRate = 1f;
            Evasion = 12f;
            PrimaryWeapon = WeaponDamageType.Kinetic;
            MilitaryPower = 120;
        }

        public void ApplyDesign(ShipDesign design)
        {
            if (design == null) return;
            HullClass = design.HullClass;
            DesignId = design.Id;
            DesignAlloyCost = design.AlloyCost;
            MaxHullPoints = HullPoints = design.Hull;
            MaxArmorPoints = ArmorPoints = design.Armor;
            MaxShieldPoints = ShieldPoints = design.Shields;
            Damage = design.Damage;
            FireRate = design.FireRate;
            Evasion = design.Evasion;
            PrimaryWeapon = design.PrimaryWeapon;
            HyperSpeed = design.Speed;
            MilitaryPower = Mathf.RoundToInt(design.Dps * 12f + design.Hull * 0.35f + design.Armor * 0.2f + design.Shields * 0.15f);

            UpkeepEnergy = design.HullClass switch
            {
                ShipClass.Destroyer => 5.5f,
                ShipClass.Frigate   => 3.5f,
                _                   => 2.5f
            };
        }

        public float IntegrityNormalized => Mathf.Clamp01(HullPoints / Mathf.Max(1f, MaxHullPoints));
    }
}