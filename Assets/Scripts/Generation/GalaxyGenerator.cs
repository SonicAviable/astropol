using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Generation
{
    /// <summary>Форма галактики (как при создании партии в Stellaris).</summary>
    public enum GalaxyShape { Elliptical, Spiral2, Spiral4, Ring }

    public class GalaxyGenerator : MonoBehaviour
    {
        /// <summary>Форма галактики для следующей генерации.</summary>
        public GalaxyShape Shape = GalaxyShape.Spiral2;

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
            var names = new StarNameGenerator();
            PlanetNames.Reset();

            while (Systems.Count < starCount && attempts < maxAttempts)
            {
                attempts++;

                Vector3 pos = SamplePosition();

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
                    Systems.Add(new StarSystem(Systems.Count, names.Next(), pos, spectral));
                }
            }
        }

        /// <summary>Точка по форме галактики. Центр оставлен пустым — там «ядро», как в Stellaris.</summary>
        private Vector3 SamplePosition()
        {
            float R = galaxyRadius;
            Vector2 p;
            switch (Shape)
            {
                case GalaxyShape.Elliptical:
                {
                    // Плотный центр, разрежённая периферия; эллипс 1 : 0,65
                    p = new Vector2(Gauss(), Gauss()) * 0.42f;
                    if (p.magnitude > 1f) p = p.normalized * Random.Range(0.55f, 1f);
                    if (p.magnitude < 0.12f) p = (p.sqrMagnitude < 1e-6f ? Vector2.right : p.normalized) * Random.Range(0.12f, 0.2f);
                    p.y *= 0.65f;
                    break;
                }
                case GalaxyShape.Ring:
                {
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    float r = Mathf.Clamp(0.74f + Gauss() * 0.1f, 0.5f, 1f);
                    p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    break;
                }
                default:
                {
                    int arms = Shape == GalaxyShape.Spiral4 ? 4 : 2;
                    if (Random.value < 0.12f)
                    {
                        // Кольцо звёзд вокруг ядра (балдж)
                        float a = Random.Range(0f, Mathf.PI * 2f);
                        p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(0.15f, 0.27f);
                        break;
                    }
                    int arm = Random.Range(0, arms);
                    float t = Mathf.Pow(Random.value, 0.85f);              // 0 — у ядра, 1 — край рукава
                    float rr = Mathf.Lerp(0.2f, 1f, t);
                    float twist = arms == 2 ? 3.3f : 2.4f;                 // закрутка рукава, радиан
                    float ang = arm * Mathf.PI * 2f / arms + t * twist;
                    float spread = Mathf.Lerp(0.035f, 0.085f, t) * (arms == 2 ? 1f : 0.8f);
                    p = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr + new Vector2(Gauss(), Gauss()) * spread;
                    break;
                }
            }
            return new Vector3(p.x * R, 0f, p.y * R);
        }

        /// <summary>Нормальное распределение (Бокс — Мюллер) на UnityEngine.Random — повторяемо по сиду.</summary>
        private static float Gauss()
        {
            float u1 = Mathf.Max(1e-6f, Random.value), u2 = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

        // ==================== ГИПЕРКОРИДОРЫ ====================

        private bool CrossesExistingLane(StarSystem a, StarSystem b)
        {
            Vector2 p1 = new Vector2(a.Position.x, a.Position.z), p2 = new Vector2(b.Position.x, b.Position.z);
            foreach (var lane in Hyperlanes)
            {
                if (lane.SystemA == a.Id || lane.SystemA == b.Id || lane.SystemB == a.Id || lane.SystemB == b.Id) continue;
                var c = Systems[lane.SystemA].Position;
                var d = Systems[lane.SystemB].Position;
                if (SegmentsIntersect(p1, p2, new Vector2(c.x, c.z), new Vector2(d.x, d.z))) return true;
            }
            return false;
        }

        private bool PassesNearStar(StarSystem a, StarSystem b, float clearance = 4f)
        {
            Vector2 p1 = new Vector2(a.Position.x, a.Position.z), p2 = new Vector2(b.Position.x, b.Position.z);
            Vector2 seg = p2 - p1;
            float len2 = Mathf.Max(1e-4f, seg.sqrMagnitude);
            foreach (var s in Systems)
            {
                if (s.Id == a.Id || s.Id == b.Id) continue;
                Vector2 q = new Vector2(s.Position.x, s.Position.z);
                float t = Mathf.Clamp01(Vector2.Dot(q - p1, seg) / len2);
                if (t <= 0f || t >= 1f) continue;
                if ((p1 + seg * t - q).sqrMagnitude < clearance * clearance) return true;
            }
            return false;
        }

        private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            float d1 = Cross(p3, p4, p1), d2 = Cross(p3, p4, p2), d3 = Cross(p1, p2, p3), d4 = Cross(p1, p2, p4);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private void GenerateHyperlanes()
        {
            // Радиус связи подстраивается под плотность формы: в узких рукавах звёзды стоят иначе,
            // чем в эллипсе. Берём 2,3 медианных расстояния до ближайшего соседа.
            float connect = maxConnectionDistance;
            if (Systems.Count > 2)
            {
                var nn = new List<float>(Systems.Count);
                foreach (var s in Systems)
                {
                    float best = float.MaxValue;
                    foreach (var o in Systems)
                        if (o != s) best = Mathf.Min(best, Vector3.Distance(s.Position, o.Position));
                    nn.Add(best);
                }
                nn.Sort();
                connect = Mathf.Max(connect, nn[nn.Count / 2] * 2.3f);
            }

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
                    if (dist > connect) break;

                    if (systemA.ConnectedSystemIds.Count >= maxConnectionsPerStar ||
                        systemB.ConnectedSystemIds.Count >= maxConnectionsPerStar) continue;

                    // Как в Stellaris: коридоры не пересекаются и не проходят «сквозь» чужие звёзды —
                    // карта читается как сеть маршрутов, а не паутина
                    if (CrossesExistingLane(systemA, systemB) || PassesNearStar(systemA, systemB)) continue;

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

                // Ищем пару звёзд из разных компонент с минимальным расстоянием;
                // мост, не пересекающий существующие коридоры, всегда предпочтительнее
                float bestDist = float.MaxValue;
                bool bestCrosses = true;
                int bestA = -1, bestB = -1;

                for (int c = 1; c < components.Count; c++)
                {
                    foreach (int idA in components[0])
                    {
                        foreach (int idB in components[c])
                        {
                            float d = Vector3.Distance(Systems[idA].Position, Systems[idB].Position);
                            if (!bestCrosses && d >= bestDist) continue;
                            bool crosses = CrossesExistingLane(Systems[idA], Systems[idB]);
                            if ((!crosses && bestCrosses) || (crosses == bestCrosses && d < bestDist))
                            {
                                bestDist = d;
                                bestCrosses = crosses;
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