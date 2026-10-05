using System.Collections.Generic;
using StellarisClone.Generation;

namespace StellarisClone.Core
{
    public static class GalaxyPathfinder
    {
        public static List<int> FindPath(int startSystemId, int targetSystemId, GalaxyGenerator generator)
        {
            if (generator == null || generator.Systems.Count == 0) return null;
            if (startSystemId < 0 || targetSystemId < 0 ||
                startSystemId >= generator.Systems.Count ||
                targetSystemId >= generator.Systems.Count) return null;

            if (startSystemId == targetSystemId) return new List<int>();

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

                foreach (int neighborId in generator.Systems[current].ConnectedSystemIds)
                {
                    if (visited.Add(neighborId))
                    {
                        cameFrom[neighborId] = current;
                        queue.Enqueue(neighborId);
                    }
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