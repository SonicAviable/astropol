using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Generation
{
    public class GalaxyGenerator : MonoBehaviour
    {
        [Header("Настройки формы")]
        [SerializeField] private int starCount = 100;
        [SerializeField] private float galaxyRadius = 120f;
        [SerializeField] private float minStarDistance = 12f;

        [Header("Настройки связей (гиперкоридоры)")]
        [SerializeField] private float maxConnectionDistance = 45f;
        [SerializeField] private int maxConnectionsPerStar = 5;

        public List<StarSystem> Systems { get; private set; } = new List<StarSystem>();
        public HashSet<Hyperlane> Hyperlanes { get; private set; } = new HashSet<Hyperlane>();

        public int StarCount => starCount;
        public float GalaxyRadius => galaxyRadius;

        public void Configure(int stars, float radius, float minDistance)
        {
            starCount = Mathf.Max(10, stars);
            galaxyRadius = Mathf.Max(40f, radius);
            minStarDistance = Mathf.Max(4f, minDistance);
        }

        /// <summary>Галактика из сохранения: системы как есть, гиперкоридоры — по связям.</summary>
        public void LoadSystems(List<StarSystem> systems)
        {
            Systems.Clear();
            Hyperlanes.Clear();
            if (systems == null) return;
            Systems.AddRange(systems);
            foreach (var s in Systems)
                foreach (int n in s.ConnectedSystemIds)
                    if (n >= 0 && n < Systems.Count && n != s.Id)
                        Hyperlanes.Add(new Hyperlane(s.Id, n));
            Debug.Log($"[GalaxyGenerator] Галактика загружена: {Systems.Count} звёзд, {Hyperlanes.Count} гиперкоридоров.");
        }

        public void GenerateGalaxy()
        {
            Systems.Clear();
            Hyperlanes.Clear();

            GenerateStarPositions();
            GenerateHyperlanes();
            EnsureGraphConnectivity();

            // Финальная страховка: если после всех склеек остались изолированные
            // компоненты — присоединяем их напрямую к столице (звезда 0)
            var finalComponents = FindConnectedComponents();
            if (finalComponents.Count > 1)
            {
                Debug.LogWarning($"[GalaxyGenerator] После склейки осталось {finalComponents.Count} компонент. " +
                                 "Принудительно соединяю со столицей.");

                for (int c = 1; c < finalComponents.Count; c++)
                {
                    int idFirst = finalComponents[c][0];
                    if (idFirst >= 0 && idFirst < Systems.Count)
                        AddHyperlane(Systems[0], Systems[idFirst]);
                }
            }

            Debug.Log($"[GalaxyGenerator] Галактика готова: {Systems.Count} звёзд, {Hyperlanes.Count} гиперкоридоров.");
        }

        // ==================== ГЕНЕРАЦИЯ ЗВЁЗД ====================

        private void GenerateStarPositions()
        {
            int attempts = 0;
            int maxAttempts = starCount * 50;

            while (Systems.Count < starCount && attempts < maxAttempts)
            {
                attempts++;

                // Дисковое распределение с концентрацией к центру
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float radius = Mathf.Sqrt(Random.Range(0.05f, 1f)) * galaxyRadius;
                Vector3 pos = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);

                bool tooClose = false;
                for (int i = 0; i < Systems.Count; i++)
                {
                    if (Vector3.Distance(pos, Systems[i].Position) < minStarDistance)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    StarSpectralClass spectral = (StarSpectralClass)Random.Range(0, 5);
                    Systems.Add(new StarSystem(Systems.Count, $"Система-{Systems.Count + 1}", pos, spectral));
                }
            }
        }

        // ==================== ГИПЕРКОРИДОРЫ ====================

        private void GenerateHyperlanes()
        {
            for (int i = 0; i < Systems.Count; i++)
            {
                var systemA = Systems[i];
                var candidates = new List<StarSystem>(Systems);
                candidates.Remove(systemA);
                candidates.Sort((a, b) =>
                    Vector3.Distance(systemA.Position, a.Position)
                        .CompareTo(Vector3.Distance(systemA.Position, b.Position)));

                foreach (var systemB in candidates)
                {
                    float dist = Vector3.Distance(systemA.Position, systemB.Position);
                    if (dist > maxConnectionDistance) break;

                    if (systemA.ConnectedSystemIds.Count >= maxConnectionsPerStar ||
                        systemB.ConnectedSystemIds.Count >= maxConnectionsPerStar) continue;

                    AddHyperlane(systemA, systemB);
                }
            }
        }

        // ==================== СВЯЗНОСТЬ ====================

        /// <summary>
        /// Склеивает все изолированные группы систем в единый граф.
        /// Ищет компоненты связности, между ними — минимальную по расстоянию пару,
        /// соединяет её гиперкоридором, и повторяет до тех пор, пока компонента не станет одна.
        /// </summary>
        private void EnsureGraphConnectivity()
        {
            int safety = 500;

            while (safety-- > 0)
            {
                var components = FindConnectedComponents();
                if (components.Count <= 1) return;

                // Ищем пару звёзд из разных компонент с минимальным расстоянием
                float bestDist = float.MaxValue;
                int bestA = -1, bestB = -1;

                for (int c = 1; c < components.Count; c++)
                {
                    foreach (int idA in components[0])
                    {
                        foreach (int idB in components[c])
                        {
                            float d = Vector3.Distance(Systems[idA].Position, Systems[idB].Position);
                            if (d < bestDist)
                            {
                                bestDist = d;
                                bestA = idA;
                                bestB = idB;
                            }
                        }
                    }
                }

                if (bestA < 0 || bestB < 0) return;

                AddHyperlane(Systems[bestA], Systems[bestB]);
            }

            Debug.LogWarning("[GalaxyGenerator] EnsureGraphConnectivity: превышен лимит итераций, " +
                             "возможно, остались изолированные системы.");
        }

        /// <summary>
        /// BFS по всему графу — возвращает список несвязанных групп систем.
        /// </summary>
        private List<List<int>> FindConnectedComponents()
        {
            var components = new List<List<int>>();
            var visited = new HashSet<int>();

            for (int start = 0; start < Systems.Count; start++)
            {
                if (visited.Contains(start)) continue;

                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(start);
                visited.Add(start);

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    component.Add(cur);

                    foreach (int neighbor in Systems[cur].ConnectedSystemIds)
                    {
                        if (!visited.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                components.Add(component);
            }

            return components;
        }

        // ==================== УТИЛИТЫ ====================

        private void AddHyperlane(StarSystem a, StarSystem b)
        {
            if (a == null || b == null || a.Id == b.Id) return;
            if (a.ConnectedSystemIds.Contains(b.Id)) return;

            a.ConnectedSystemIds.Add(b.Id);
            b.ConnectedSystemIds.Add(a.Id);
            Hyperlanes.Add(new Hyperlane(a.Id, b.Id));
        }
    }
}