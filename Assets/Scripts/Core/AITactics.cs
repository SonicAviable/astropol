using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Математика «камень-ножницы» для ИИ: энергия бьёт броню, кинетика — щиты, взрывное — всех поровну.
    /// По слоям защиты врага считается, насколько эффективно каждое оружие, и во сколько раз
    /// один флот выигрывает у другого с поправкой на типы урона.
    /// </summary>
    public static class AITactics
    {
        public struct Defense
        {
            public float Shield, Armor, Hull;
            public float Total => Shield + Armor + Hull;

            public static Defense Lerp(Defense a, Defense b, float t) => new Defense
            {
                Shield = Mathf.Lerp(a.Shield, b.Shield, t),
                Armor = Mathf.Lerp(a.Armor, b.Armor, t),
                Hull = Mathf.Lerp(a.Hull, b.Hull, t)
            };
        }

        public static readonly WeaponDamageType[] AllTypes =
            { WeaponDamageType.Energy, WeaponDamageType.Kinetic, WeaponDamageType.Explosive };

        public static string TypeName(WeaponDamageType t) =>
            t == WeaponDamageType.Energy ? "энергетическое" : t == WeaponDamageType.Kinetic ? "кинетическое" : "взрывное";

        public static string TypeShort(WeaponDamageType t) =>
            t == WeaponDamageType.Energy ? "энергия" : t == WeaponDamageType.Kinetic ? "кинетика" : "взрывчатка";

        /// <summary>Максимальная защита (щит, броня, корпус) всех боевых кораблей из списка.</summary>
        public static Defense DefenseOf(IEnumerable<FleetData> fleets, bool current)
        {
            var d = new Defense();
            foreach (var f in fleets)
            {
                if (f == null || f.Destroyed || f.Type != FleetType.Military) continue;
                d.Shield += current ? f.ShieldPoints : f.MaxShieldPoints;
                d.Armor += current ? f.ArmorPoints : f.MaxArmorPoints;
                d.Hull += current ? f.HullPoints : f.MaxHullPoints;
            }
            return d;
        }

        /// <summary>Во сколько раз быстрее оружие проедает такую защиту, чем «чистый» урон по корпусу.</summary>
        public static float Effectiveness(WeaponDamageType t, Defense d)
        {
            float total = d.Total;
            if (total <= 0f) return 1f;
            float mulS = t == WeaponDamageType.Kinetic ? CombatManager.KineticVsShield
                       : t == WeaponDamageType.Energy ? CombatManager.EnergyVsShield : CombatManager.ExplosiveVsAll;
            float mulA = t == WeaponDamageType.Energy ? CombatManager.EnergyVsArmor
                       : t == WeaponDamageType.Kinetic ? CombatManager.KineticVsArmor : CombatManager.ExplosiveVsAll;
            float rawNeeded = d.Shield / mulS + d.Armor / mulA + d.Hull;
            return rawNeeded > 0f ? total / rawNeeded : 1f;
        }

        /// <summary>Средняя эффективность всех орудий флота против заданной защиты (с весом по урону).</summary>
        public static float WeaponEffectiveness(IEnumerable<FleetData> attackers, Defense vs)
        {
            float dps = 0f, weighted = 0f;
            foreach (var f in attackers)
            {
                if (f == null || f.Destroyed || f.Type != FleetType.Military) continue;
                var b = EmpireBonuses.For(f.OwnerId);
                foreach (var w in f.Weapons)
                {
                    float x = w.Dps * b.DamageMult(w.Type);
                    dps += x;
                    weighted += x * Effectiveness(w.Type, vs);
                }
            }
            return dps > 0f ? weighted / dps : 1f;
        }

        /// <summary>
        /// Поправка к силе нашей стороны: &gt;1, если наше оружие хорошо пробивает их защиту,
        /// а их оружие плохо пробивает нашу. Ограничена, чтобы не перекрывать саму численность.
        /// </summary>
        public static float MatchupFactor(IList<FleetData> mine, IList<FleetData> theirs)
        {
            if (mine == null || theirs == null || mine.Count == 0 || theirs.Count == 0) return 1f;
            float ours = WeaponEffectiveness(mine, DefenseOf(theirs, true));
            float they = WeaponEffectiveness(theirs, DefenseOf(mine, true));
            return Mathf.Clamp(ours / Mathf.Max(0.3f, they), 0.6f, 1.6f);
        }
    }
}
