using UnityEngine;
using WargameRTS.Map;
using WargameRTS.Units;

namespace WargameRTS.Combat
{
    public enum ArmorFacing
    {
        Front,
        Side,
        Rear
    }

    /// <summary>Pure combat formulas, kept free of MonoBehaviour state.</summary>
    public static class CombatMath
    {
        /// <summary>Infantry in forests or towns takes less damage and is harder to hit.</summary>
        public const float CoverDamageMultiplier = 0.5f;
        public const float CoverAccuracyMultiplier = 0.6f;

        public static ArmorFacing GetFacing(Transform target, Vector3 attackerPosition)
        {
            Vector3 toAttacker = attackerPosition - target.position;
            toAttacker.y = 0f;
            Vector3 forward = target.forward;
            forward.y = 0f;
            float angle = Vector3.Angle(forward, toAttacker);
            if (angle <= 45f)
                return ArmorFacing.Front;
            return angle >= 135f ? ArmorFacing.Rear : ArmorFacing.Side;
        }

        public static int GetArmor(UnitDefinition definition, ArmorFacing facing)
        {
            switch (facing)
            {
                case ArmorFacing.Front: return definition.ArmorFront;
                case ArmorFacing.Side: return definition.ArmorSide;
                default: return definition.ArmorRear;
            }
        }

        /// <summary>Damage of a single hit against the given armour value.</summary>
        public static float Damage(WeaponDefinition weapon, int armor)
        {
            float kinetic = 0f;
            if (weapon.ArmorPiercing > 0)
            {
                if (armor <= 0)
                    kinetic = 1f;
                else if (weapon.ArmorPiercing > armor)
                    kinetic = 1f + (weapon.ArmorPiercing - armor) * 0.5f;
            }

            float explosive = weapon.HighExplosive / (1f + Mathf.Max(0, armor));
            return kinetic + explosive;
        }

        public static bool IsInCover(Unit unit)
        {
            if (!unit.Definition.IsInfantry)
                return false;
            var map = GameMap.Instance;
            return map != null && (map.IsInForest(unit.Position) || map.IsInTown(unit.Position));
        }

        public static float HitChance(WeaponDefinition weapon, Unit shooter, Unit target, float distance)
        {
            float chance = weapon.Accuracy;
            if (shooter.IsMoving)
                chance *= weapon.MovingAccuracyMultiplier;
            if (IsInCover(target))
                chance *= CoverAccuracyMultiplier;

            // Long shots are less accurate.
            chance *= Mathf.Lerp(1f, 0.6f, distance / Mathf.Max(1f, weapon.Range));
            return Mathf.Clamp01(chance);
        }
    }
}
