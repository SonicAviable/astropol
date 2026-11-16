using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Снабжение флота (<see cref="SupplyNetwork"/>): военный корабль дальше
    /// <see cref="SupplyNetwork.FleetSupplyRange"/> прыжков от своей подключённой к хабу системы —
    /// «на подножном корму»: содержание ×<see cref="UpkeepMult"/>, корпус и броня понемногу изнашиваются
    /// (не ниже <see cref="AttritionFloor"/>), ремонта нет. Глубокие рейды возможны, но имеют цену;
    /// перерезанные коридоры бьют и по флоту, который на них опирается.
    /// Пираты, чудовища и гарнизоны вольных миров снабжения не знают.
    /// </summary>
    public static class FleetSupply
    {
        public const float UpkeepMult = 1.5f;
        public const float AttritionPerDay = 0.004f;
        public const float AttritionFloor = 0.4f;

        private static readonly HashSet<int> s_warned = new HashSet<int>();

        /// <summary>Флот владельца-империи на снабжении (угрозы и вольные миры — всегда).</summary>
        public static bool InSupply(FleetData d)
        {
            if (d == null || BorderRules.Ignores(d.OwnerId)) return true;
            return SupplyNetwork.InSupply(d.OwnerId, d.CurrentSystemId);
        }

        /// <summary>Военный флот без снабжения: износ вместо ремонта. Возвращает true — ремонт запрещён.</summary>
        public static bool DailyTick(FleetData d)
        {
            if (d == null || d.Type != FleetType.Military || d.Destroyed) return false;
            if (InSupply(d))
            {
                s_warned.Remove(d.Id);
                return false;
            }
            float floorHull = d.MaxHullPoints * AttritionFloor;
            if (d.HullPoints > floorHull)
                d.HullPoints = Mathf.Max(floorHull, d.HullPoints - d.MaxHullPoints * AttritionPerDay);
            float floorArmor = d.MaxArmorPoints * AttritionFloor;
            if (d.ArmorPoints > floorArmor)
                d.ArmorPoints = Mathf.Max(floorArmor, d.ArmorPoints - d.MaxArmorPoints * AttritionPerDay);

            if (d.OwnerId == 0 && s_warned.Add(d.Id))
                NotificationCenter.Show(Loc.T("Флот без снабжения"),
                    Loc.F("{0}: дальше {1} прыжков от снабжаемой территории — содержание ×{2:0.#}, корабли изнашиваются и не чинятся",
                        d.Name, SupplyNetwork.FleetSupplyRange, UpkeepMult),
                    NotificationCenter.Kind.Warning, 7f);
            return true;
        }

        public static void Reset() => s_warned.Clear();
    }
}
