using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;
using StellarisClone.Core;
using Random = System.Random;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Генерация поля боя по реальной планете: рельеф по шуму и биому, районы колонии объектами на карте
    /// (жилой квартал, завод, электростанция, шахта, штаб), укрепления по уровню базы, укрытия, точки,
    /// зоны высадки и навмеш. Карта ~260×260 м; атакующий высаживается с юга, штаб защитника — на севере.
    /// </summary>
    public static class GroundMapGenerator
    {
        private const int Grid = 131;               // вершин по стороне (шаг 2 м)
        private const float Step = GroundMap.Half * 2f / (Grid - 1);

        // Площадки районов и ретрансляторов (локальные X, Z)
        private static readonly Vector2[] DistrictSlots = { new Vector2(-72f, 48f), new Vector2(72f, 48f), new Vector2(-48f, -4f), new Vector2(50f, 2f) };
        private static readonly Vector2[] RelaySlots = { new Vector2(0f, 14f), new Vector2(-6f, -66f), new Vector2(0f, 52f) };
        private static readonly Vector2 HqSlot = new Vector2(0f, 98f);
        private static readonly Vector2 LandingSlot = new Vector2(0f, -106f);

        public static GroundMap Build(GroundBattleSetup setup, Transform root, Random rng)
        {
            var map = new GroundMap { Origin = root.position, Biome = GroundBiome.For(setup.Planet) };
            var b = map.Biome;
            map.NoiseOffX = (float)rng.NextDouble() * 500f;
            map.NoiseOffZ = (float)rng.NextDouble() * 500f;

            // ---- Раскладка точек ----
            var districts = new List<(PointKind kind, DistrictType type)>();
            if (setup.Urban > 0) districts.Add((PointKind.Urban, DistrictType.Urban));
            if (setup.Industrial > 0) districts.Add((PointKind.Industrial, DistrictType.Industrial));
            if (setup.Generator > 0) districts.Add((PointKind.Generator, DistrictType.Generator));
            if (setup.Mining > 0) districts.Add((PointKind.Mining, DistrictType.Mining));
            if (districts.Count == 0) districts.Add((PointKind.Urban, DistrictType.Urban));

            var slots = new List<(Vector2 pos, PointKind kind, DistrictType? type)>();
            for (int i = 0; i < districts.Count && i < DistrictSlots.Length; i++)
                slots.Add((DistrictSlots[i], districts[i].kind, districts[i].type));
            for (int i = 0; slots.Count < 5 && i < RelaySlots.Length; i++)
                if (slots.Count < 3 || i == 0) slots.Add((RelaySlots[i], PointKind.Relay, null));

            foreach (var s in slots) map.Flats.Add((s.pos, 14f));
            map.Flats.Add((HqSlot, 20f));
            map.Flats.Add((LandingSlot, 18f));

            // Река или лава между высадкой и центром; броды/перешейки — по краям и в центре
            if (b.River || b.LavaRiver)
            {
                map.HasRiver = true;
                map.RiverHalf = b.LavaRiver ? 4.5f : 5.5f;
                map.Fords.Add(-70f + (float)rng.NextDouble() * 20f);
                map.Fords.Add(-8f + (float)rng.NextDouble() * 16f);
                map.Fords.Add(52f + (float)rng.NextDouble() * 20f);
            }

            if (b.Craters > 0f)
                for (int i = 0; i < 26; i++)
                {
                    var c = new Vector2(Rand(rng, -110f, 110f), Rand(rng, -95f, 85f));
                    if (NearFlat(map, c, 6f)) continue;
                    map.Craters.Add((c, Rand(rng, 4f, 11f), Rand(rng, 1.2f, 2.6f)));
                }

            // ---- Земля ----
            map.Terrain = BuildTerrain(map, root, rng);

            // ---- Точки ----
            foreach (var s in slots)
            {
                var p = new StrategicPoint
                {
                    Kind = s.kind, District = s.type, Pos = map.ToWorld(s.pos.x, s.pos.y),
                    Name = StrategicPoint.KindName(s.kind)
                };
                // Районы колонии с начала у защитника, ретрансляторы ничьи
                if (s.type.HasValue) { p.Owner = 1; p.Progress = -1f; }
                map.Points.Add(p);
                BuildPointVisual(map, p, root);
                BuildDistrict(map, p, root, rng, setup);
                SandbagRing(map, p.Pos, s.type.HasValue ? 1 : -1, root, rng);
            }

            map.AttackerBeacon = map.ToWorld(LandingSlot.x, LandingSlot.y);
            map.DefenderBeacon = map.ToWorld(HqSlot.x, HqSlot.y - 14f);
            BuildHq(map, root, setup);
            BuildFortifications(map, root, rng, setup);
            Scatter(map, root, rng);
            if (map.HasRiver) BuildRiver(map, root);
            return map;
        }

        private static float Rand(Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        private static bool NearFlat(GroundMap map, Vector2 p, float extra)
        {
            foreach (var f in map.Flats) if (Vector2.Distance(p, f.c) < f.r + extra) return true;
            return false;
        }

        // ================================================================ Земля

        private static GameObject BuildTerrain(GroundMap map, Transform root, Random rng)
        {
            var go = new GameObject("Terrain");
            go.transform.SetParent(root, false);
            var verts = new Vector3[Grid * Grid];
            var uvs = new Vector2[Grid * Grid];
            for (int z = 0; z < Grid; z++)
                for (int x = 0; x < Grid; x++)
                {
                    float lx = -GroundMap.Half + x * Step, lz = -GroundMap.Half + z * Step;
                    verts[z * Grid + x] = new Vector3(lx, map.Height(lx, lz), lz);
                    uvs[z * Grid + x] = new Vector2(x / (float)(Grid - 1), z / (float)(Grid - 1));
                }
            var tris = new int[(Grid - 1) * (Grid - 1) * 6];
            int t = 0;
            for (int z = 0; z < Grid - 1; z++)
                for (int x = 0; x < Grid - 1; x++)
                {
                    int i = z * Grid + x;
                    tris[t++] = i; tris[t++] = i + Grid; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + Grid; tris[t++] = i + Grid + 1;
                }
            var mesh = new Mesh { name = "GroundTerrain" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(Rendering.ShaderCache.Lit) { name = "GroundTerrain", hideFlags = HideFlags.DontSave };
            var tex = GroundTexture(map, rng);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", map.Biome.Type == PlanetType.Molten ? 0.25f : 0.08f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (map.Biome.Type == PlanetType.Molten && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetTexture("_EmissionMap", LavaGlowTexture(map));
                mat.SetColor("_EmissionColor", new Color(2.2f, 0.7f, 0.2f));
            }
            mr.sharedMaterial = mat;
            mr.receiveShadows = true;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        /// <summary>Текстура всей карты: пятна биома, дороги между точками, тёмные берега, площадки.</summary>
        private static Texture2D GroundTexture(GroundMap map, Random rng)
        {
            const int N = 1024;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "GroundTex", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[N * N];
            var b = map.Biome;
            float ox = (float)rng.NextDouble() * 100f;
            // Дороги: отрезки между точками и штабом
            var roads = new List<(Vector2 a, Vector2 c)>();
            Vector2 L(Vector3 w) { var l = map.ToLocal(w); return new Vector2(l.x, l.z); }
            Vector2 hq = new Vector2(0f, 98f), land = new Vector2(0f, -106f);
            foreach (var p in map.Points) roads.Add((L(p.Pos), hq));
            roads.Add((land, new Vector2(0f, 14f)));
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float lx = -GroundMap.Half + x * (GroundMap.Half * 2f / (N - 1));
                    float lz = -GroundMap.Half + y * (GroundMap.Half * 2f / (N - 1));
                    float n1 = Mathf.PerlinNoise(lx * 0.035f + ox, lz * 0.035f);
                    float n2 = Mathf.PerlinNoise(lx * 0.16f, lz * 0.16f + ox);
                    float n3 = Mathf.PerlinNoise(lx * 0.6f + ox, lz * 0.6f);
                    Color c = Color.Lerp(b.GroundA, b.GroundB, n1);
                    c = Color.Lerp(c, b.GroundC, Mathf.Clamp01((n2 - 0.55f) * 2.2f));
                    c *= 0.88f + n3 * 0.24f;

                    // Площадки — утоптанная земля
                    foreach (var f in map.Flats)
                    {
                        float d = Vector2.Distance(new Vector2(lx, lz), f.c);
                        if (d < f.r) c = Color.Lerp(c, Color.Lerp(b.GroundC, b.Rock, 0.5f) * 0.9f, (1f - d / f.r) * 0.55f);
                    }
                    // Дороги
                    float rd = float.MaxValue;
                    foreach (var r in roads) rd = Mathf.Min(rd, SegDist(new Vector2(lx, lz), r.a, r.c));
                    if (rd < 3f) c = Color.Lerp(c, b.Rock * 0.85f, (1f - rd / 3f) * 0.6f);
                    // Берега и русло
                    if (map.HasRiver)
                    {
                        float dz = Mathf.Abs(lz - map.RiverZ(lx));
                        if (dz < map.RiverHalf + 4f) c = Color.Lerp(c, b.Type == PlanetType.Molten ? new Color(0.08f, 0.05f, 0.04f) : b.GroundB * 0.55f, 1f - Mathf.Clamp01((dz - map.RiverHalf) / 4f));
                    }
                    c.a = 1f;
                    px[y * N + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Свечение трещин вулканического мира (эмиссия земли).</summary>
        private static Texture2D LavaGlowTexture(GroundMap map)
        {
            const int N = 512;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "LavaGlow", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float lx = -GroundMap.Half + x * (GroundMap.Half * 2f / (N - 1));
                    float lz = -GroundMap.Half + y * (GroundMap.Half * 2f / (N - 1));
                    float n = Mathf.PerlinNoise(lx * 0.09f + 40f, lz * 0.09f);
                    float crack = Mathf.Clamp01(1f - Mathf.Abs(n - 0.5f) * 30f) * 0.35f;
                    float river = 0f;
                    if (map.HasRiver && !map.InRiver(map.ToWorld(lx, lz), -100f))
                    {
                        float dz = Mathf.Abs(lz - map.RiverZ(lx));
                        river = 1f - Mathf.Clamp01((dz - map.RiverHalf + 1f) / 3f);
                    }
                    float v = Mathf.Max(crack, river);
                    px[y * N + x] = new Color(v, v * 0.6f, v * 0.3f, 1f);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        // ================================================================ Река и лава

        private static void BuildRiver(GroundMap map, Transform root)
        {
            bool lava = map.Biome.LavaRiver;
            var go = new GameObject(lava ? "Lava" : "River");
            go.transform.SetParent(root, false);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            const int Seg = 140;
            for (int i = 0; i <= Seg; i++)
            {
                float x = -GroundMap.Half + i * (GroundMap.Half * 2f / Seg);
                float z = map.RiverZ(x);
                float y = lava ? -1.3f : -0.75f;
                verts.Add(new Vector3(x, y, z - map.RiverHalf - 1.5f));
                verts.Add(new Vector3(x, y, z + map.RiverHalf + 1.5f));
                if (i < Seg)
                {
                    int k = i * 2;
                    tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
                    tris.Add(k + 2); tris.Add(k + 1); tris.Add(k + 3);
                }
            }
            var mesh = new Mesh { name = go.name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = lava
                ? GroundMats.Lit(new Color(1f, 0.35f, 0.08f), 0f, 0.6f, new Color(3.2f, 1.1f, 0.25f))
                : GroundMats.Lit(new Color(0.18f, 0.32f, 0.38f), 0.2f, 0.92f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Лава непроходима вне перешейков: объём-модификатор навмеша
            if (lava)
            {
                for (int i = 0; i < Seg; i += 2)
                {
                    float x = -GroundMap.Half + (i + 1) * (GroundMap.Half * 2f / Seg);
                    if (!map.InRiver(map.ToWorld(x, map.RiverZ(x)))) continue;
                    var v = new GameObject("LavaBlock");
                    v.transform.SetParent(go.transform, false);
                    v.transform.localPosition = new Vector3(x, 0f, map.RiverZ(x));
                    var mod = v.AddComponent<NavMeshModifierVolume>();
                    mod.size = new Vector3(GroundMap.Half * 2f / Seg * 2.2f, 8f, map.RiverHalf * 2f);
                    mod.center = Vector3.zero;
                    mod.area = 1;   // Not Walkable
                }
            }
        }

        // ================================================================ Точки, районы, штаб

        private static void BuildPointVisual(GroundMap map, StrategicPoint p, Transform root)
        {
            var go = new GameObject("Point_" + p.Kind);
            go.transform.SetParent(root, false);
            go.transform.position = p.Pos;
            p.Visual = go;
            // Кольцо захвата — тонкий светящийся тор из сегментов
            var ring = new GameObject("Ring");
            ring.transform.SetParent(go.transform, false);
            var mesh = RingMesh(StrategicPoint.Radius, 0.25f, 64);
            ring.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rr = ring.AddComponent<MeshRenderer>();
            rr.sharedMaterial = GroundMats.Glow(new Color(0.8f, 0.8f, 0.8f));
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            p.Ring = rr;
            // Флагшток с маяком
            var steel = GroundMats.Lit(new Color(0.3f, 0.32f, 0.35f), 0.7f, 0.5f);
            GroundMats.Part(go.transform, PrimitiveType.Cylinder, new Vector3(0f, 3f, 0f), new Vector3(0.18f, 3f, 0.18f), steel, "Pole");
            p.Flag = GroundMats.Part(go.transform, PrimitiveType.Cube, new Vector3(0.75f, 5.3f, 0f), new Vector3(1.4f, 0.8f, 0.05f),
                GroundMats.Glow(new Color(0.8f, 0.8f, 0.8f)), "Flag").GetComponent<Renderer>();
            GroundMats.Part(go.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.15f, 0f), new Vector3(2.2f, 0.15f, 2.2f), steel, "Base");
        }

        private static Mesh RingMesh(float r, float w, int seg)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d * (r - w)); v.Add(d * (r + w));
                if (i < seg) { int k = i * 2; t.Add(k); t.Add(k + 2); t.Add(k + 1); t.Add(k + 1); t.Add(k + 2); t.Add(k + 3); }
            }
            var m = new Mesh { name = "PointRing" };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals();
            return m;
        }

        private static Destructible Building(GroundMap map, Transform root, Vector3 world, Vector3 size, float yaw, Material mat,
                                             Destructible.Kind kind, float hp, DistrictType? district, CoverKind around = CoverKind.Heavy)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(root, false);
            go.transform.position = world;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var body = GroundMats.Part(go.transform, PrimitiveType.Cube, new Vector3(0f, size.y * 0.5f - 0.3f, 0f), size, mat, "Body");
            var d = new Destructible { Type = kind, Go = go, Pos = world, Radius = Mathf.Max(size.x, size.z) * 0.6f, Hp = hp, MaxHp = hp, District = district, Side = 1 };
            d.Renderers.Add(body.GetComponent<Renderer>());
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, size.y * 0.5f - 0.3f, 0f);
            col.size = size;
            d.Colliders.Add(col);
            map.Destructibles.Add(d);
            // Точки укрытия по периметру (с внешней стороны стен)
            if (around != CoverKind.None)
            {
                var q = go.transform.rotation;
                int nx = Mathf.Max(1, Mathf.RoundToInt(size.x / 2.2f)), nz = Mathf.Max(1, Mathf.RoundToInt(size.z / 2.2f));
                for (int i = 0; i < nx; i++)
                {
                    float fx = (i + 0.5f) / nx - 0.5f;
                    AddCover(map, world, q, new Vector3(fx * size.x, 0f, size.z * 0.5f + 0.9f), new Vector3(0f, 0f, -1f), around, d);
                    AddCover(map, world, q, new Vector3(fx * size.x, 0f, -size.z * 0.5f - 0.9f), new Vector3(0f, 0f, 1f), around, d);
                }
                for (int i = 0; i < nz; i++)
                {
                    float fz = (i + 0.5f) / nz - 0.5f;
                    AddCover(map, world, q, new Vector3(size.x * 0.5f + 0.9f, 0f, fz * size.z), new Vector3(-1f, 0f, 0f), around, d);
                    AddCover(map, world, q, new Vector3(-size.x * 0.5f - 0.9f, 0f, fz * size.z), new Vector3(1f, 0f, 0f), around, d);
                }
            }
            return d;
        }

        private static void AddCover(GroundMap map, Vector3 center, Quaternion q, Vector3 local, Vector3 facingLocal, CoverKind kind, Destructible owner)
        {
            var w = map.Snap(center + q * local);
            if (!map.Inside(w, 6f) || map.InRiver(w)) return;
            map.Cover.Add(w, kind, q * facingLocal, owner);
        }

        /// <summary>Объекты района вокруг точки: квартал, завод с кранами, электростанция с градирнями, шахта.</summary>
        private static void BuildDistrict(GroundMap map, StrategicPoint p, Transform root, Random rng, GroundBattleSetup setup)
        {
            var concrete = GroundMats.Lit(new Color(0.55f, 0.56f, 0.58f), 0.05f, 0.25f);
            var dark = GroundMats.Lit(new Color(0.28f, 0.3f, 0.33f), 0.5f, 0.45f);
            var windows = GroundMats.Glow(new Color(1f, 0.82f, 0.5f) * 1.6f);
            Vector3 c = p.Pos;
            switch (p.Kind)
            {
                case PointKind.Urban:
                {
                    int n = 3 + rng.Next(3);
                    for (int i = 0; i < n; i++)
                    {
                        float a = (i / (float)n) * Mathf.PI * 2f + 0.4f;
                        var pos = map.Snap(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Rand(rng, 16f, 22f));
                        var size = new Vector3(Rand(rng, 6f, 9f), Rand(rng, 6f, 13f), Rand(rng, 6f, 9f));
                        var d = Building(map, root, pos, size, Rand(rng, 0f, 90f), concrete, Destructible.Kind.Building, 1400f, DistrictType.Urban);
                        // Окна — светящиеся полосы
                        for (int f = 1; f < Mathf.FloorToInt(size.y / 3f); f++)
                            GroundMats.Part(d.Go.transform, PrimitiveType.Cube, new Vector3(0f, f * 3f, 0f), new Vector3(size.x + 0.05f, 0.35f, size.z + 0.05f), windows, "Windows");
                    }
                    break;
                }
                case PointKind.Industrial:
                {
                    var pos = map.Snap(c + new Vector3(-18f, 0f, 8f));
                    var d = Building(map, root, pos, new Vector3(16f, 8f, 10f), 12f, dark, Destructible.Kind.Factory, 2200f, DistrictType.Industrial);
                    GroundMats.Part(d.Go.transform, PrimitiveType.Cylinder, new Vector3(5f, 10f, 2f), new Vector3(1.4f, 6f, 1.4f), concrete, "Chimney");
                    // Кран
                    var crane = map.Snap(c + new Vector3(16f, 0f, -10f));
                    var cr = Building(map, root, crane, new Vector3(1.6f, 18f, 1.6f), 0f, GroundMats.Lit(new Color(0.85f, 0.6f, 0.15f), 0.4f, 0.4f), Destructible.Kind.Building, 600f, DistrictType.Industrial, CoverKind.Light);
                    GroundMats.Part(cr.Go.transform, PrimitiveType.Cube, new Vector3(5f, 17.2f, 0f), new Vector3(12f, 0.8f, 0.8f), GroundMats.Lit(new Color(0.85f, 0.6f, 0.15f), 0.4f, 0.4f), "Boom");
                    Building(map, root, map.Snap(c + new Vector3(4f, 0f, 20f)), new Vector3(8f, 5f, 6f), 30f, concrete, Destructible.Kind.Building, 900f, DistrictType.Industrial);
                    break;
                }
                case PointKind.Generator:
                {
                    for (int i = 0; i < 2; i++)
                    {
                        var pos = map.Snap(c + new Vector3(i == 0 ? -17f : 17f, 0f, 12f));
                        var d = Building(map, root, pos, new Vector3(9f, 1f, 9f), 0f, concrete, Destructible.Kind.PowerPlant, 1800f, DistrictType.Generator);
                        // Градирня: усечённый «конус» из двух цилиндров
                        GroundMats.Part(d.Go.transform, PrimitiveType.Cylinder, new Vector3(0f, 6f, 0f), new Vector3(8.5f, 6f, 8.5f), concrete, "Tower");
                        GroundMats.Part(d.Go.transform, PrimitiveType.Cylinder, new Vector3(0f, 12.2f, 0f), new Vector3(7f, 0.4f, 7f), dark, "Rim");
                        var col = d.Go.GetComponent<BoxCollider>();
                        col.size = new Vector3(8.5f, 12f, 8.5f);
                        col.center = new Vector3(0f, 6f, 0f);
                    }
                    Building(map, root, map.Snap(c + new Vector3(0f, 0f, -18f)), new Vector3(10f, 5f, 6f), 0f, dark, Destructible.Kind.Building, 900f, DistrictType.Generator);
                    break;
                }
                case PointKind.Mining:
                {
                    var pos = map.Snap(c + new Vector3(14f, 0f, 12f));
                    var d = Building(map, root, pos, new Vector3(7f, 14f, 7f), 20f, dark, Destructible.Kind.Mine, 1500f, DistrictType.Mining);
                    GroundMats.Part(d.Go.transform, PrimitiveType.Cylinder, new Vector3(0f, 15f, 0f), new Vector3(5f, 0.6f, 5f), concrete, "Wheel", Quaternion.Euler(90f, 0f, 0f));
                    for (int i = 0; i < 3; i++)
                        Rock(map, root, map.Snap(c + new Vector3(-16f + i * 5f, 0f, -14f + i * 2f)), Rand(rng, 1.5f, 2.6f), rng, GroundMats.Lit(map.Biome.Rock * 0.8f, 0.1f, 0.2f));
                    break;
                }
                case PointKind.Relay:
                {
                    var d = Building(map, root, map.Snap(c + new Vector3(6f, 0f, 6f)), new Vector3(2.5f, 3f, 2.5f), 45f, dark, Destructible.Kind.Building, 500f, null, CoverKind.Light);
                    d.Side = -1;
                    GroundMats.Part(d.Go.transform, PrimitiveType.Cylinder, new Vector3(0f, 7f, 0f), new Vector3(0.3f, 5f, 0.3f), concrete, "Mast");
                    GroundMats.Part(d.Go.transform, PrimitiveType.Sphere, new Vector3(0f, 12.2f, 0f), Vector3.one * 0.6f, GroundMats.Glow(new Color(1f, 0.2f, 0.15f) * 2f), "Beacon");
                    break;
                }
            }
        }

        private static void BuildHq(GroundMap map, Transform root, GroundBattleSetup setup)
        {
            var pos = map.ToWorld(HqSlot.x, HqSlot.y);
            var hull = GroundMats.Lit(new Color(0.42f, 0.44f, 0.48f), 0.6f, 0.5f);
            var d = Building(map, root, pos, new Vector3(18f, 10f, 14f), 0f, hull, Destructible.Kind.Headquarters, 6000f + setup.Fortification * 2500f, null);
            d.Radius = 11f;
            var accent = GroundMats.Glow(setup.Defender.Color * 1.6f);
            GroundMats.Part(d.Go.transform, PrimitiveType.Cube, new Vector3(0f, 12f, 0f), new Vector3(10f, 4f, 8f), hull, "Tower");
            GroundMats.Part(d.Go.transform, PrimitiveType.Cylinder, new Vector3(0f, 17f, 0f), new Vector3(0.4f, 3f, 0.4f), hull, "Antenna");
            GroundMats.Part(d.Go.transform, PrimitiveType.Cube, new Vector3(0f, 6f, -7.05f), new Vector3(14f, 0.6f, 0.1f), accent, "Stripe");
            map.Hq = d;

            // Щитовой купол над штабом (укрепление 3): пока у защитника есть электростанция, штаб неуязвим
            if (setup.Fortification >= 3)
            {
                var dome = new GameObject("ShieldDome");
                dome.transform.SetParent(root, false);
                dome.transform.position = pos;
                var sph = GroundMats.Part(dome.transform, PrimitiveType.Sphere, Vector3.zero, new Vector3(30f, 22f, 30f),
                    MakeShieldMat(setup.Defender.Color), "Dome");
                sph.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                map.ShieldDome = new Destructible { Type = Destructible.Kind.ShieldDome, Go = dome, Pos = pos, Radius = 15f, Hp = 1f, MaxHp = 1f, Side = 1 };
                map.ShieldDome.Renderers.Add(sph.GetComponent<Renderer>());
            }
        }

        private static Material MakeShieldMat(Color c)
        {
            var sh = Resources.Load<Shader>("Shaders/ShieldBubble");
            Material m;
            if (sh != null) { m = new Material(sh) { name = "GroundShield", hideFlags = HideFlags.DontSave }; if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(c.r, c.g, c.b, 0.5f)); }
            else
            {
                m = new Material(GroundMats.Additive) { name = "GroundShield", hideFlags = HideFlags.DontSave };
            }
            return m;
        }

        /// <summary>Мешки с песком вокруг точки: дуга из секций с проходами, укрытие с внутренней стороны.</summary>
        private static void SandbagRing(GroundMap map, Vector3 center, int side, Transform root, Random rng)
        {
            var mat = GroundMats.Lit(Color.Lerp(map.Biome.GroundC, new Color(0.6f, 0.55f, 0.4f), 0.5f), 0f, 0.15f);
            int segs = 5;
            for (int i = 0; i < segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.3f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var pos = map.Snap(center + dir * 7.5f);
                var go = new GameObject("Sandbags");
                go.transform.SetParent(root, false);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(dir);
                for (int k = 0; k < 3; k++)
                    GroundMats.Part(go.transform, PrimitiveType.Capsule, new Vector3((k - 1) * 1.15f, 0.35f, 0f), new Vector3(0.55f, 0.6f, 0.5f), mat, "Bag", Quaternion.Euler(0f, 0f, 90f));
                GroundMats.Part(go.transform, PrimitiveType.Capsule, new Vector3(-0.55f, 0.85f, 0f), new Vector3(0.5f, 0.55f, 0.45f), mat, "Bag", Quaternion.Euler(0f, 0f, 90f));
                GroundMats.Part(go.transform, PrimitiveType.Capsule, new Vector3(0.6f, 0.85f, 0f), new Vector3(0.5f, 0.55f, 0.45f), mat, "Bag", Quaternion.Euler(0f, 0f, 90f));
                var bc = go.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0.55f, 0f);
                bc.size = new Vector3(3.4f, 1.1f, 0.6f);
                var d = new Destructible { Type = Destructible.Kind.Sandbags, Go = go, Pos = pos, Radius = 2f, Hp = 220f, MaxHp = 220f, Side = side };
                d.Colliders.Add(bc);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) d.Renderers.Add(r);
                map.Destructibles.Add(d);
                // Укрытие — с внутренней стороны (защищает от огня снаружи), и снаружи — от огня изнутри
                map.Cover.Add(map.Snap(pos - dir * 0.9f + go.transform.right * 0.8f), CoverKind.Light, dir, d);
                map.Cover.Add(map.Snap(pos - dir * 0.9f - go.transform.right * 0.8f), CoverKind.Light, dir, d);
                map.Cover.Add(map.Snap(pos + dir * 0.9f), CoverKind.Light, -dir, d);
            }
        }

        // ================================================================ Укрепления

        private static void BuildFortifications(GroundMap map, Transform root, Random rng, GroundBattleSetup setup)
        {
            int f = Mathf.Clamp(setup.Fortification, 0, 3);
            var bunkerMat = GroundMats.Lit(new Color(0.38f, 0.38f, 0.36f), 0.2f, 0.2f);
            var slit = GroundMats.Glow(new Color(1f, 0.6f, 0.2f) * 1.4f);
            if (f >= 1)
                for (int i = 0; i < 2; i++)
                {
                    var pos = map.ToWorld(i == 0 ? -22f : 22f, 70f);
                    var d = Building(map, root, pos, new Vector3(6f, 2.6f, 4.5f), 180f, bunkerMat, Destructible.Kind.Bunker, 2600f, null);
                    GroundMats.Part(d.Go.transform, PrimitiveType.Cube, new Vector3(0f, 1.6f, 2.26f), new Vector3(4f, 0.35f, 0.1f), slit, "Slit");
                    d.Weapon = new WeaponDef { Name = Loc.T("Пулемёт бункера"), Kind = WeaponKind.Heavy, Range = 44f, Damage = 7f, Cooldown = 0.14f, Accuracy = 0.4f, Suppression = 3f, Burst = 14, Reload = 2f };
                }
            if (f >= 2)
            {
                var steel = GroundMats.Lit(new Color(0.3f, 0.31f, 0.34f), 0.8f, 0.5f);
                Vector2[] tur = { new Vector2(-40f, 30f), new Vector2(40f, 30f), new Vector2(0f, 64f) };
                foreach (var t in tur)
                {
                    var d = Building(map, root, map.ToWorld(t.x, t.y), new Vector3(3f, 2f, 3f), 0f, steel, Destructible.Kind.Turret, 1200f, null, CoverKind.Light);
                    GroundMats.Part(d.Go.transform, PrimitiveType.Cylinder, new Vector3(0f, 2.4f, 1.2f), new Vector3(0.35f, 1.4f, 0.35f), steel, "Barrel", Quaternion.Euler(90f, 0f, 0f));
                    d.Weapon = new WeaponDef { Name = Loc.T("Автопушка турели"), Kind = WeaponKind.Heavy, Range = 46f, Damage = 16f, Cooldown = 0.4f, Accuracy = 0.45f, Suppression = 2.5f, Burst = 6, Reload = 2f, Pierce = 0.4f, Splash = 1.2f };
                }
                // Минные поля на подступах к районам
                foreach (var p in map.Points)
                {
                    if (!p.District.HasValue) continue;
                    var l = map.ToLocal(p.Pos);
                    for (int i = 0; i < 6; i++)
                        map.Mines.Add(new Vector3(l.x + Rand(rng, -14f, 14f), 0f, l.z - Rand(rng, 12f, 22f)));
                }
            }
            if (f >= 3)
            {
                var steel = GroundMats.Lit(new Color(0.26f, 0.27f, 0.3f), 0.8f, 0.5f);
                for (int i = 0; i < 2; i++)
                {
                    var d = Building(map, root, map.ToWorld(i == 0 ? -12f : 12f, 82f), new Vector3(5f, 3f, 5f), 0f, steel, Destructible.Kind.Turret, 2600f, null);
                    GroundMats.Part(d.Go.transform, PrimitiveType.Cylinder, new Vector3(0f, 3.6f, 2.4f), new Vector3(0.6f, 2.6f, 0.6f), steel, "Barrel", Quaternion.Euler(90f, 0f, 0f));
                    d.Weapon = new WeaponDef { Name = Loc.T("Тяжёлое орудие"), Kind = WeaponKind.Explosive, Range = 70f, Damage = 140f, Cooldown = 6f, Accuracy = 0.5f, Splash = 4f, Pierce = 0.9f, Suppression = 20f, StructureMult = 1.5f };
                }
            }
            foreach (var d in map.Destructibles)
                if (d.Weapon != null) d.Side = 1;
        }

        // ================================================================ Природа

        private static void Rock(GroundMap map, Transform root, Vector3 pos, float size, Random rng, Material mat)
        {
            var go = new GameObject("Rock");
            go.transform.SetParent(root, false);
            go.transform.position = pos + Vector3.down * size * 0.25f;
            go.transform.rotation = Quaternion.Euler(Rand(rng, -15f, 15f), Rand(rng, 0f, 360f), Rand(rng, -15f, 15f));
            GroundMats.Part(go.transform, PrimitiveType.Sphere, Vector3.zero, new Vector3(size * 1.4f, size, size * 1.15f), mat, "Stone");
            if (size > 1.4f)
            {
                var col = go.AddComponent<SphereCollider>();
                col.radius = 0.5f;
                go.transform.localScale = Vector3.one;
                col.radius = size * 0.55f;
            }
            var d = new Destructible { Type = Destructible.Kind.Rock, Go = go, Pos = pos, Radius = size, Hp = 4000f, MaxHp = 4000f };
            map.Destructibles.Add(d);
            var kind = size > 1.6f ? CoverKind.Heavy : CoverKind.Light;
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var w = map.Snap(pos + dir * (size * 0.8f + 0.8f));
                if (map.Inside(w) && !map.InRiver(w)) map.Cover.Add(w, kind, -dir, d);
            }
        }

        private static void Scatter(GroundMap map, Transform root, Random rng)
        {
            var b = map.Biome;
            var rockMat = GroundMats.Lit(b.Rock, b.Type == PlanetType.Molten ? 0.6f : 0.05f, b.Type == PlanetType.Molten ? 0.8f : 0.2f);
            int rocks = Mathf.RoundToInt(40 * b.Rocks);
            for (int i = 0; i < rocks; i++)
            {
                var l = new Vector2(Rand(rng, -118f, 118f), Rand(rng, -100f, 110f));
                if (NearFlat(map, l, 3f)) continue;
                var w = map.ToWorld(l.x, l.y);
                if (map.InRiver(w, 2f)) continue;
                Rock(map, root, w, Rand(rng, 0.8f, b.Type == PlanetType.Molten ? 3.2f : 2.4f), rng, rockMat);
            }

            // Обсидиановые гряды — длинные тяжёлые укрытия
            if (b.Type == PlanetType.Molten)
                for (int i = 0; i < 8; i++)
                {
                    var l = new Vector2(Rand(rng, -100f, 100f), Rand(rng, -80f, 80f));
                    if (NearFlat(map, l, 5f)) continue;
                    float yaw = Rand(rng, 0f, 180f);
                    var w = map.ToWorld(l.x, l.y);
                    if (map.InRiver(w, 3f)) continue;
                    Building(map, root, w + Vector3.down * 0.4f, new Vector3(Rand(rng, 8f, 14f), Rand(rng, 1.6f, 2.6f), 1.6f), yaw,
                        GroundMats.Lit(new Color(0.05f, 0.04f, 0.07f), 0.7f, 0.9f), Destructible.Kind.Rock, 6000f, null).Side = -1;
                }

            // Рощи — лёгкие укрытия вокруг деревьев
            if (b.Trees > 0f)
            {
                var trunk = GroundMats.Lit(new Color(0.3f, 0.22f, 0.14f), 0f, 0.2f);
                var leaves = GroundMats.Lit(new Color(0.16f, 0.36f, 0.14f), 0f, 0.15f);
                var leaves2 = GroundMats.Lit(new Color(0.22f, 0.42f, 0.16f), 0f, 0.15f);
                for (int g = 0; g < 14; g++)
                {
                    var center = new Vector2(Rand(rng, -110f, 110f), Rand(rng, -95f, 100f));
                    if (NearFlat(map, center, 4f)) continue;
                    int n = 5 + rng.Next(8);
                    for (int i = 0; i < n; i++)
                    {
                        var l = center + new Vector2(Rand(rng, -9f, 9f), Rand(rng, -9f, 9f));
                        var w = map.ToWorld(l.x, l.y);
                        if (map.InRiver(w, 1.5f) || !map.Inside(w, 8f)) continue;
                        float s = Rand(rng, 0.8f, 1.4f);
                        var go = new GameObject("Tree");
                        go.transform.SetParent(root, false);
                        go.transform.position = w;
                        GroundMats.Part(go.transform, PrimitiveType.Cylinder, new Vector3(0f, 2f * s, 0f), new Vector3(0.35f * s, 2f * s, 0.35f * s), trunk, "Trunk");
                        GroundMats.Part(go.transform, PrimitiveType.Sphere, new Vector3(0f, 5f * s, 0f), new Vector3(3.6f, 4.2f, 3.6f) * s, (i & 1) == 0 ? leaves : leaves2, "Crown");
                        var col = go.AddComponent<CapsuleCollider>();
                        col.radius = 0.4f * s; col.height = 4f * s; col.center = new Vector3(0f, 2f * s, 0f);
                        var d = new Destructible { Type = Destructible.Kind.Tree, Go = go, Pos = w, Radius = 1.4f, Hp = 300f, MaxHp = 300f };
                        d.Colliders.Add(col);
                        foreach (var r in go.GetComponentsInChildren<Renderer>()) d.Renderers.Add(r);
                        map.Destructibles.Add(d);
                        map.Cover.Add(map.Snap(w + new Vector3(1.1f, 0f, 0.3f)), CoverKind.Light, Vector3.zero, d);
                    }
                }
            }

            // Воронки старых боёв и кратеры — лёгкие укрытия на валу
            foreach (var c in map.Craters)
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(c.r * 0.9f), 4, 10);
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    var w = map.ToWorld(c.c.x + Mathf.Cos(a) * c.r * 0.82f, c.c.y + Mathf.Sin(a) * c.r * 0.82f);
                    if (map.Inside(w)) map.Cover.Add(w, CoverKind.Light, Vector3.zero);
                }
            }
        }

        // ================================================================ Навмеш

        /// <summary>Навмеш по коллайдерам корня (земля, здания, камни, деревья); лава вырезается модификаторами.</summary>
        public static NavMeshSurface BuildNavMesh(Transform root)
        {
            var surf = root.gameObject.GetComponent<NavMeshSurface>();
            if (surf == null) surf = root.gameObject.AddComponent<NavMeshSurface>();
            surf.collectObjects = CollectObjects.Children;
            surf.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surf.layerMask = ~0;
            surf.BuildNavMesh();
            return surf;
        }
    }
}
