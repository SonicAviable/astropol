using System.Collections.Generic;
using UnityEngine;
using WargameRTS.Combat;

namespace WargameRTS.Units
{
    public enum UnitCategory
    {
        Infantry,
        Recon,
        Tank,
        InfantryFightingVehicle,
        AntiTank,
        AntiAir,
        Artillery
    }

    /// <summary>Which placeholder model <see cref="UnitView"/> builds.</summary>
    public enum UnitModel
    {
        MediumTank,
        HeavyTank,
        LightTank,
        TrackedIfv,
        WheeledApc,
        InfantrySquad,
        InfantryTeam,
        ReconCar,
        AtgmCarrier,
        Spaag,
        Howitzer,
        RocketLauncher
    }

    /// <summary>Static data of a unit type ("unit card" in the deck).</summary>
    [CreateAssetMenu(menuName = "WargameRTS/Unit Definition", fileName = "Unit")]
    public sealed class UnitDefinition : ScriptableObject
    {
        public string DisplayName = "Unit";

        [Tooltip("Short label drawn above the unit on the map.")]
        public string ShortName = "UNT";

        public UnitCategory Category;
        public UnitModel Model;

        [Tooltip("Price in command points (deck / reinforcements).")]
        public int Cost = 50;

        public float MaxHealth = 10f;

        [Header("Armour")]
        public int ArmorFront;
        public int ArmorSide;
        public int ArmorRear;

        [Header("Mobility")]
        [Tooltip("Off-road speed, km/h.")]
        public float SpeedKmh = 50f;

        [Tooltip("Turn rate, degrees per second.")]
        public float TurnRate = 120f;

        [Header("Recon")]
        [Tooltip("Distance at which this unit spots a non-concealed target, meters.")]
        public float OpticsRange = 1300f;

        [Tooltip("Multiplier to enemy optics range when spotting this unit (lower = stealthier).")]
        [Range(0.1f, 1f)] public float Concealment = 1f;

        [Header("Weapons")]
        public List<WeaponDefinition> Weapons = new List<WeaponDefinition>();

        public bool IsInfantry => Model == UnitModel.InfantrySquad || Model == UnitModel.InfantryTeam;

        /// <summary>True if every weapon is indirect: the unit engages without its own line of sight.</summary>
        public bool IsIndirectFire
        {
            get
            {
                if (Weapons.Count == 0)
                    return false;
                foreach (var weapon in Weapons)
                {
                    if (!weapon.Indirect)
                        return false;
                }
                return true;
            }
        }

        public float SpeedMetersPerSecond => SpeedKmh / 3.6f;

        public float MaxWeaponRange
        {
            get
            {
                float range = 0f;
                foreach (var weapon in Weapons)
                    range = Mathf.Max(range, weapon.Range);
                return range;
            }
        }
    }
}
