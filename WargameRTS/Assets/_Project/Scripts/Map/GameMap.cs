using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using WargameRTS.Core;
using Random = System.Random;

namespace WargameRTS.Map
{
    [Serializable]
    public struct ForestZone
    {
        public Vector2 Center;
        public float Radius;
    }

    [Serializable]
    public struct TownZone
    {
        public Vector2 Center;
        public float Radius;
    }

    /// <summary>
    /// Procedural battlefield: rolling hills, forests (block sight, give cover),
    /// towns (buildings block movement and sight) and a baked NavMesh.
    /// The map is a square centered at the world origin, 1 unit = 1 meter.
    /// </summary>
    public sealed class GameMap : MonoBehaviour
    {
        const int GroundResolution = 200;        // grid cells per side
        const int GroundTextureSize = 512;
        const float HillHeight = 30f;
        const float HillFrequency = 0.0011f;
        const float DetailHeight = 6f;
        const float DetailFrequency = 0.0042f;

        public static GameMap Instance { get; private set; }

        public float Size { get; private set; }
        public float HalfSize => Size * 0.5f;
        public IReadOnlyList<ForestZone> Forests => forests;
        public IReadOnlyList<TownZone> Towns => towns;

        /// <summary>Colour map of the ground (also used by the minimap).</summary>
        public Texture2D GroundTexture { get; private set; }

        readonly List<ForestZone> forests = new List<ForestZone>();
        readonly List<TownZone> towns = new List<TownZone>();
        Vector2 noiseOffset;
        Vector2 detailOffset;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        public float HeightAt(float x, float z)
        {
            float hills = Mathf.PerlinNoise((x + noiseOffset.x) * HillFrequency, (z + noiseOffset.y) * HillFrequency);
            float detail = Mathf.PerlinNoise((x + detailOffset.x) * DetailFrequency, (z + detailOffset.y) * DetailFrequency);
            return hills * HillHeight + detail * DetailHeight;
        }

        public Vector3 OnGround(Vector3 position) =>
            new Vector3(position.x, HeightAt(position.x, position.z), position.z);

        public Vector3 NormalAt(float x, float z)
        {
            const float step = 2f;
            float dx = HeightAt(x + step, z) - HeightAt(x - step, z);
            float dz = HeightAt(x, z + step) - HeightAt(x, z - step);
            return new Vector3(-dx, 2f * step, -dz).normalized;
        }

        public Vector3 Clamp(Vector3 position, float margin)
        {
            float limit = HalfSize - margin;
            position.x = Mathf.Clamp(position.x, -limit, limit);
            position.z = Mathf.Clamp(position.z, -limit, limit);
            return position;
        }

        public bool IsInForest(Vector3 position)
        {
            var p = new Vector2(position.x, position.z);
            foreach (var forest in forests)
            {
                if ((p - forest.Center).sqrMagnitude <= forest.Radius * forest.Radius)
                    return true;
            }
            return false;
        }

        public bool IsInTown(Vector3 position)
        {
            var p = new Vector2(position.x, position.z);
            foreach (var town in towns)
            {
                if ((p - town.Center).sqrMagnitude <= town.Radius * town.Radius)
                    return true;
            }
            return false;
        }

        /// <summary>Meters of forest crossed by the straight segment a→b (top-down).</summary>
        public float ForestLengthAlong(Vector3 a, Vector3 b)
        {
            var from = new Vector2(a.x, a.z);
            var to = new Vector2(b.x, b.z);
            float total = 0f;
            foreach (var forest in forests)
                total += SegmentCircleLength(from, to, forest.Center, forest.Radius);
            return total;
        }

        static float SegmentCircleLength(Vector2 a, Vector2 b, Vector2 center, float radius)
        {
            Vector2 d = b - a;
            Vector2 f = a - center;
            float qa = Vector2.Dot(d, d);
            if (qa < 1e-4f)
                return 0f;

            float qb = 2f * Vector2.Dot(f, d);
            float qc = Vector2.Dot(f, f) - radius * radius;
            float discriminant = qb * qb - 4f * qa * qc;
            if (discriminant <= 0f)
                return 0f;

            float sqrt = Mathf.Sqrt(discriminant);
            float t1 = Mathf.Clamp01((-qb - sqrt) / (2f * qa));
            float t2 = Mathf.Clamp01((-qb + sqrt) / (2f * qa));
            return Mathf.Max(0f, t2 - t1) * Mathf.Sqrt(qa);
        }

        // ------------------------------------------------------------------
        // Generation
        // ------------------------------------------------------------------

        public void Generate(int seed, float size)
        {
            Size = size;
            var rng = new Random(seed);
            noiseOffset = new Vector2(rng.Next(0, 100000), rng.Next(0, 100000));
            detailOffset = new Vector2(rng.Next(0, 100000), rng.Next(0, 100000));

            forests.Clear();
            towns.Clear();
            PlaceTowns(rng);
            PlaceForests(rng);

            BuildGround();
            BuildForestMeshes(rng);
            BuildTowns(rng);
        }

        public void BuildNavMesh()
        {
            var surface = gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = GameLayers.GroundMask | GameLayers.ObstaclesMask;
            surface.BuildNavMesh();
        }

        /// <summary>True if z is inside a team's deployment strip (south for Blue, north for Red).</summary>
        bool IsInDeploymentZone(float z) => Mathf.Abs(z) > HalfSize - 320f;

        void PlaceTowns(Random rng)
        {
            int townCount = Mathf.Max(2, Mathf.RoundToInt(4f * (Size * Size) / (2000f * 2000f)));
            for (int attempt = 0; attempt < townCount * 60 && towns.Count < townCount; attempt++)
            {
                var center = RandomPoint(rng, 250f);
                if (IsInDeploymentZone(center.y))
                    continue;

                bool tooClose = false;
                foreach (var town in towns)
                    tooClose |= (town.Center - center).magnitude < 500f;
                if (tooClose)
                    continue;

                towns.Add(new TownZone { Center = center, Radius = 70f + (float)rng.NextDouble() * 40f });
            }
        }

        void PlaceForests(Random rng)
        {
            // Forest count scales with map area (22 on a 2 km map).
            int forestCount = Mathf.RoundToInt(22f * (Size * Size) / (2000f * 2000f));
            int placed = 0;
            for (int attempt = 0; attempt < forestCount * 30 && placed < forestCount; attempt++)
            {
                var center = RandomPoint(rng, 80f);
                float radius = 55f + (float)rng.NextDouble() * 90f;
                if (!IsForestSpotFree(center, radius))
                    continue;

                // A forest is a cluster of overlapping circles for an organic outline.
                forests.Add(new ForestZone { Center = center, Radius = radius });
                int lobes = rng.Next(1, 4);
                for (int l = 0; l < lobes; l++)
                {
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float lobeRadius = radius * (0.45f + (float)rng.NextDouble() * 0.35f);
                    var lobeCenter = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * (0.6f + (float)rng.NextDouble() * 0.4f);
                    if (IsForestSpotFree(lobeCenter, lobeRadius))
                        forests.Add(new ForestZone { Center = lobeCenter, Radius = lobeRadius });
                }
                placed++;
            }
        }

        bool IsForestSpotFree(Vector2 center, float radius)
        {
            if (Mathf.Abs(center.x) > HalfSize - radius * 0.5f || Mathf.Abs(center.y) > HalfSize - radius * 0.5f)
                return false;
            if (IsInDeploymentZone(center.y + Mathf.Sign(center.y) * radius * 0.5f))
                return false;
            foreach (var town in towns)
            {
                if ((town.Center - center).magnitude < town.Radius + radius + 30f)
                    return false;
            }
            return true;
        }

        Vector2 RandomPoint(Random rng, float margin)
        {
            float range = Size - 2f * margin;
            return new Vector2(
                (float)rng.NextDouble() * range - range * 0.5f,
                (float)rng.NextDouble() * range - range * 0.5f);
        }

        void BuildGround()
        {
            int vertsPerSide = GroundResolution + 1;
            var vertices = new Vector3[vertsPerSide * vertsPerSide];
            var uvs = new Vector2[vertices.Length];
            float cell = Size / GroundResolution;

            for (int z = 0; z < vertsPerSide; z++)
            {
                for (int x = 0; x < vertsPerSide; x++)
                {
                    float wx = -HalfSize + x * cell;
                    float wz = -HalfSize + z * cell;
                    int i = z * vertsPerSide + x;
                    vertices[i] = new Vector3(wx, HeightAt(wx, wz), wz);
                    uvs[i] = new Vector2((float)x / GroundResolution, (float)z / GroundResolution);
                }
            }

            var triangles = new int[GroundResolution * GroundResolution * 6];
            int t = 0;
            for (int z = 0; z < GroundResolution; z++)
            {
                for (int x = 0; x < GroundResolution; x++)
                {
                    int i = z * vertsPerSide + x;
                    triangles[t++] = i;
                    triangles[t++] = i + vertsPerSide;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + vertsPerSide;
                    triangles[t++] = i + vertsPerSide + 1;
                }
            }

            var mesh = new Mesh { name = "Ground" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var ground = new GameObject("Ground");
            ground.layer = GameLayers.Ground;
            ground.transform.SetParent(transform, false);
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = GameMaterials.CreateLit(Color.white, 0.05f);
            GroundTexture = BuildGroundTexture();
            material.mainTexture = GroundTexture;
            ground.AddComponent<MeshRenderer>().sharedMaterial = material;
            ground.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        Texture2D BuildGroundTexture()
        {
            var lowland = new Color(0.33f, 0.42f, 0.20f);
            var highland = new Color(0.48f, 0.50f, 0.28f);
            var forestFloor = new Color(0.20f, 0.27f, 0.13f);
            var townGround = new Color(0.45f, 0.42f, 0.36f);
            var deployment = new Color(0.38f, 0.40f, 0.24f);

            var pixels = new Color32[GroundTextureSize * GroundTextureSize];
            float texel = Size / GroundTextureSize;
            for (int y = 0; y < GroundTextureSize; y++)
            {
                for (int x = 0; x < GroundTextureSize; x++)
                {
                    float wx = -HalfSize + (x + 0.5f) * texel;
                    float wz = -HalfSize + (y + 0.5f) * texel;
                    var point = new Vector3(wx, 0f, wz);

                    float height01 = Mathf.InverseLerp(0f, HillHeight + DetailHeight, HeightAt(wx, wz));
                    Color color = Color.Lerp(lowland, highland, height01);
                    if (IsInDeploymentZone(wz))
                        color = Color.Lerp(color, deployment, 0.35f);
                    if (IsInForest(point))
                        color = forestFloor;
                    else if (IsInTown(point))
                        color = townGround;

                    float grain = Mathf.PerlinNoise(wx * 0.05f, wz * 0.05f) * 0.12f - 0.06f;
                    color = new Color(color.r + grain, color.g + grain, color.b + grain * 0.5f);
                    pixels[y * GroundTextureSize + x] = color;
                }
            }

            var texture = new Texture2D(GroundTextureSize, GroundTextureSize, TextureFormat.RGBA32, true)
            {
                name = "GroundColors",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        void BuildForestMeshes(Random rng)
        {
            var material = GameMaterials.Lit(new Color(0.13f, 0.25f, 0.11f));
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int f = 0; f < forests.Count; f++)
            {
                var forest = forests[f];
                vertices.Clear();
                triangles.Clear();

                float area = Mathf.PI * forest.Radius * forest.Radius;
                int treeCount = Mathf.RoundToInt(area / 110f);
                for (int i = 0; i < treeCount; i++)
                {
                    // Uniform point in circle.
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float distance = Mathf.Sqrt((float)rng.NextDouble()) * (forest.Radius - 3f);
                    float x = forest.Center.x + Mathf.Cos(angle) * distance;
                    float z = forest.Center.y + Mathf.Sin(angle) * distance;
                    if (IsInEarlierForest(new Vector2(x, z), f))
                        continue;
                    float height = 11f + (float)rng.NextDouble() * 9f;
                    float radius = height * (0.22f + (float)rng.NextDouble() * 0.06f);
                    var basePoint = new Vector3(x, HeightAt(x, z) - 0.5f, z);

                    AddCone(vertices, triangles, basePoint + Vector3.up * height * 0.25f, radius, height * 0.75f);
                    AddCone(vertices, triangles, basePoint + Vector3.up * height * 0.55f, radius * 0.75f, height * 0.45f);
                }

                var mesh = new Mesh { name = $"Forest_{f}" };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                // Trees have no colliders: units drive through forests, which only block sight.
                var go = new GameObject($"Forest_{f}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        bool IsInEarlierForest(Vector2 point, int index)
        {
            for (int i = 0; i < index; i++)
            {
                if ((point - forests[i].Center).sqrMagnitude < forests[i].Radius * forests[i].Radius)
                    return true;
            }
            return false;
        }

        static void AddCone(List<Vector3> vertices, List<int> triangles, Vector3 basePoint, float radius, float height)
        {
            const int segments = 7;
            var tip = basePoint + Vector3.up * height;
            for (int s = 0; s < segments; s++)
            {
                float a0 = s * Mathf.PI * 2f / segments;
                float a1 = (s + 1) * Mathf.PI * 2f / segments;
                var p0 = basePoint + new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                var p1 = basePoint + new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);

                // Unshared vertices give a flat-shaded low-poly look.
                int start = vertices.Count;
                vertices.Add(p0);
                vertices.Add(tip);
                vertices.Add(p1);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }
        }

        void BuildTowns(Random rng)
        {
            var palette = new[]
            {
                new Color(0.72f, 0.68f, 0.60f),
                new Color(0.62f, 0.55f, 0.48f),
                new Color(0.70f, 0.62f, 0.52f),
                new Color(0.55f, 0.55f, 0.55f)
            };

            for (int t = 0; t < towns.Count; t++)
            {
                var town = towns[t];
                var root = new GameObject($"Town_{t}");
                root.transform.SetParent(transform, false);

                // Blocks on a grid with streets between them.
                const float blockStep = 32f;
                int half = Mathf.FloorToInt(town.Radius / blockStep);
                for (int gx = -half; gx <= half; gx++)
                {
                    for (int gz = -half; gz <= half; gz++)
                    {
                        var offset = new Vector2(gx, gz) * blockStep;
                        if (offset.magnitude > town.Radius || rng.NextDouble() < 0.2)
                            continue;

                        float width = 10f + (float)rng.NextDouble() * 10f;
                        float depth = 10f + (float)rng.NextDouble() * 10f;
                        float height = 6f + (float)rng.NextDouble() * 10f;
                        float x = town.Center.x + offset.x;
                        float z = town.Center.y + offset.y;
                        float ground = Mathf.Min(
                            Mathf.Min(HeightAt(x - width * 0.5f, z - depth * 0.5f), HeightAt(x + width * 0.5f, z - depth * 0.5f)),
                            Mathf.Min(HeightAt(x - width * 0.5f, z + depth * 0.5f), HeightAt(x + width * 0.5f, z + depth * 0.5f)));

                        var building = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        building.name = "Building";
                        building.layer = GameLayers.Obstacles;
                        building.transform.SetParent(root.transform, false);
                        building.transform.position = new Vector3(x, ground - 1f + (height + 1f) * 0.5f, z);
                        building.transform.localScale = new Vector3(width, height + 1f, depth);
                        building.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 8f - 4f, 0f);
                        building.GetComponent<MeshRenderer>().sharedMaterial =
                            GameMaterials.Lit(palette[rng.Next(palette.Length)]);
                    }
                }
            }
        }
    }
}
