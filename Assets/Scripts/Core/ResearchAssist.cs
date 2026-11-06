using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// «Помощь исследованиям»: научный корабль без дела, стоящий у своей колонии, работает на научный совет —
    /// +3 науки в месяц и ещё +1 за каждый уровень учёного на борту. Ничего включать не нужно: когда разведывать
    /// больше нечего, авторазведка сама отводит корабль к ближайшей колонии, а появится новая цель — уведёт его туда.
    /// </summary>
    public static class ResearchAssist
    {
        public const float PerShip = 3f, PerLeaderLevel = 1f;

        /// <summary>Помогает ли корабль исследованиям прямо сейчас.</summary>
        public static bool IsAssisting(FleetData d)
        {
            if (d == null || d.Destroyed || d.Type != FleetType.Science || d.InCombat) return false;
            if (d.State != FleetState.Orbiting || d.Path.Count > 0 || d.OrderQueue.Count > 0 || d.SurveyTargetSystemId >= 0) return false;
            var sys = EmpireStats.GetSystem(d.CurrentSystemId);
            return sys != null && sys.OwnerId == d.OwnerId && IsColony(sys);
        }

        public static bool IsColony(StarSystem sys)
        {
            if (sys?.Planets == null) return false;
            foreach (var p in sys.Planets) if (p.Population > 0) return true;
            return false;
        }

        /// <summary>Наука в месяц от одного корабля (с учётом учёного на борту).</summary>
        public static float ShipScience(FleetData d)
        {
            var l = LeaderManager.Instance != null ? LeaderManager.Instance.LeaderOfShip(d.Id) : null;
            return PerShip + (l != null ? l.Level * PerLeaderLevel : 0f);
        }

        /// <summary>Сколько науки в месяц дают помогающие корабли владельца (до общих множителей).</summary>
        public static float Science(int owner) => Science(owner, out _);

        public static float Science(int owner, out int ships)
        {
            ships = 0;
            float s = 0f;
            var fm = FleetManager.Instance;
            if (fm == null) return 0f;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.OwnerId != owner || !IsAssisting(d)) continue;
                ships++;
                s += ShipScience(d);
            }
            return s;
        }

        /// <summary>Ближайшая по прыжкам своя колония (−1 — нет пути).</summary>
        public static int NearestColony(int owner, int from)
        {
            var systems = EmpireStats.Systems;
            if (from < 0 || from >= systems.Count) return -1;
            var seen = new HashSet<int> { from };
            var q = new Queue<int>();
            q.Enqueue(from);
            while (q.Count > 0)
            {
                int id = q.Dequeue();
                var sys = systems[id];
                if (sys.OwnerId == owner && IsColony(sys)) return id;
                foreach (int n in sys.ConnectedSystemIds)
                {
                    if (n < 0 || n >= systems.Count || !seen.Add(n)) continue;
                    if (!BorderRules.CanEnter(owner, systems[n])) continue;
                    q.Enqueue(n);
                }
            }
            return -1;
        }
    }
}
