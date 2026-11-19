using System.Collections.Generic;
using StellarisClone.Generation;

namespace StellarisClone.Core
{
    public static class GalaxyPathfinder
    {
        /// <summary>
        /// Кратчайший путь по гиперкоридорам. mover ≥ 0 — путь для кораблей этого владельца с учётом границ
        /// (Borders): через закрытую территорию не прокладывается, цель должна быть доступна; −1 — без границ.
        /// </summary>
        public static List<int> FindPath(int startSystemId, int targetSystemId, GalaxyGenerator generator, int mover = -1)
        {
            if (generator == null || generator.Systems.Count == 0) return null;
            if (startSystemId < 0 || targetSystemId < 0 ||
                startSystemId >= generator.Systems.Count ||
                targetSystemId >= generator.Systems.Count) return null;

            if (startSystemId == targetSystemId) return new List<int>();

            var systems = generator.Systems;
            bool borders = !BorderRules.Ignores(mover);
            if (borders && !BorderRules.CanEnter(mover, systems[targetSystemId])) return null;

            var queue = new Queue<int>();
            var cameFrom = new Dictionary<int, int>();
            var visited = new HashSet<int>();

            queue.Enqueue(startSystemId);
            visited.Add(startSystemId);
            bool found = false;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (current == targetSystemId) { found = true; break; }

                foreach (int neighborId in systems[current].ConnectedSystemIds)
                {
                    if (visited.Contains(neighborId)) continue;
                    if (borders && !BorderRules.CanStep(mover, systems[current], systems[neighborId])) continue;
                    visited.Add(neighborId);
                    cameFrom[neighborId] = current;
                    queue.Enqueue(neighborId);
                }
            }

            if (!found) return null;

            var path = new List<int>();
            int step = targetSystemId;
            while (step != startSystemId)
            {
                path.Add(step);
                step = cameFrom[step];
            }
            path.Reverse();
            return path;
        }
    }
}
