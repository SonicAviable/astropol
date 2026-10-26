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
        private const int Grid = 197;               // вершин по стороне (шаг ~1,3 м — видны воронки и колеи)
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

            // Воронки от снарядов — на любом мире: поле уже видело бои
            int shells = b.Craters > 0f ? 10 : 18;
            for (int i = 0; i < shells; i++)
            {
                var c = new Vector2(Rand(rng, -100f, 100f), Rand(rng, -80f, 70f));
                if (NearFlat(map, c, 4f)) continue;
                map.Craters.Add((c, Rand(rng, 2.2f, 4.2f), Rand(rng, 0.6f, 1.2f)));
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
            Battlefield(map, root, rng);
            Dressing(map, root, rng);
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
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(Rendering.ShaderCache.Lit) { name = "GroundTerrain", hideFlags = HideFlags.DontSave };
            var tex = GroundTexture(map, rng);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            // Рельеф: карта нормалей всего поля (валы воронок, бугры) + тайловая детальная текстура (щебень, песок, трещины) каждые ~6,5 м
            if (mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", GroundDetail.MacroNormal(map));
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (mat.HasProperty("_DetailAlbedoMap"))
            {
                GroundDetail.Make(map.Biome.Type, rng.Next(1, 100000), out var dAlb, out var dNrm);
                mat.SetTexture("_DetailAlbedoMap", dAlb);
                mat.SetTextureScale("_DetailAlbedoMap", new Vector2(40f, 40f));
                mat.SetFloat("_DetailAlbedoMapScale", 1f);
                mat.SetTexture("_DetailNormalMap", dNrm);
                mat.SetFloat("_DetailNormalMapScale", 1f);
                mat.EnableKeyword("_DETAIL_MULX2");
            }
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
                    // Крупные пятна сырости/пыли — без них поле выглядит залитым одним цветом
                    float patch = Mathf.PerlinNoise(lx * 0.012f + ox * 0.3f, lz * 0.012f + 7f);
                    c *= Mathf.Lerp(0.78f, 1.12f, patch);

                    // Воронки: тёмная выжженная середина, светлый выброс грунта по валу
                    foreach (var cr in map.Craters)
                    {
                        float d = Vector2.Distance(new Vector2(lx, lz), cr.c) / cr.r;
                        if (d > 1.5f) continue;
                        if (d < 1f) c = Color.Lerp(c, c * 0.42f, (1f - d) * 0.8f);
                        else c = Color.Lerp(c, Color.Lerp(c, b.GroundC, 0.6f) * 1.08f, (1.5f - d) * 1.2f);
                    }

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
                    // Колеи техники по краям дороги
                    if (rd > 0.8f && rd < 1.7f) c *= 0.72f + Mathf.Abs(rd - 1.25f) * 0.5f;
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
            var body = GroundMats.Box(go.transform, new Vector3(0f, size.y * 0.5f - 0.3f, 0f), size, mat, "Body", null, Mathf.Min(0.35f, Mathf.Min(size.x, size.z) * 0.06f));
            var d = new Destructible { Type = kind, Go = go, Pos = world, Radius = Mathf.Max(size.x, size.z) * 0.6f, Hp = hp, MaxHp = hp, District = district, Side = 1 };
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, size.y * 0.5f - 0.3f, 0f);
            col.size = size;
            d.Colliders.Add(col);
            map.Destructibles.Add(d);
            d.Size = size;
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
            var concrete = GroundMats.Lit(new Color(0.42f, 0.41f, 0.4f), 0.05f, 0.22f);
            var concreteDk = GroundMats.Lit(new Color(0.26f, 0.25f, 0.25f), 0.05f, 0.2f);
            var dark = GroundMats.Lit(new Color(0.2f, 0.21f, 0.23f), 0.6f, 0.45f);
            var windows = GroundMats.Glow(new Color(1f, 0.72f, 0.38f) * 2.2f);
            var glass = GroundMats.Lit(new Color(0.04f, 0.05f, 0.06f), 0.6f, 0.85f);
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
                        Facade(d.Go.transform, size, concreteDk, windows, glass, rng);
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
            Facade(d.Go.transform, new Vector3(18f, 10f, 14f), GroundMats.Lit(new Color(0.24f, 0.25f, 0.27f), 0.5f, 0.4f),
                GroundMats.Glow(Color.Lerp(setup.Defender.Color, Color.white, 0.3f) * 1.8f), GroundMats.Lit(new Color(0.04f, 0.05f, 0.06f), 0.6f, 0.85f), new Random(setup.Seed));
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
                    var bt = d.Go.transform;
                    // Скошенная бетонная крыша, амбразура с отсветом, мешки по бокам
                    GroundMats.Box(bt, new Vector3(0f, 2.35f, 0.2f), new Vector3(6.6f, 0.6f, 5.2f), bunkerMat, "Roof", Quaternion.Euler(-6f, 0f, 0f), 0.2f);
                    GroundMats.Box(bt, new Vector3(0f, 1.45f, 2.3f), new Vector3(4.2f, 0.42f, 0.12f), GroundMats.Lit(new Color(0.02f, 0.02f, 0.02f), 0f, 0.1f), "Embrasure", null, 0.02f);
                    GroundMats.Box(bt, new Vector3(0f, 1.45f, 2.25f), new Vector3(3.8f, 0.16f, 0.1f), slit, "Slit", null, 0.02f);
                    var bag = GroundMats.Lit(Color.Lerp(map.Biome.GroundC, new Color(0.5f, 0.45f, 0.32f), 0.5f), 0f, 0.15f);
                    for (int k = 0; k < 4; k++)
                        GroundMats.Box(bt, new Vector3(-2.4f + k * 1.6f, 0.35f, 2.9f), new Vector3(1.5f, 0.7f, 0.7f), bag, "Bag", null, 0.3f);
                    GroundMats.Box(bt, new Vector3(2.3f, 2.85f, -1.2f), new Vector3(0.6f, 0.5f, 0.6f), GroundMats.Lit(new Color(0.18f, 0.19f, 0.2f), 0.7f, 0.5f), "Vent", null, 0.05f);
                    d.Weapon = new WeaponDef { Name = Loc.T("Пулемёт бункера"), Kind = WeaponKind.Heavy, Range = 40f, Damage = 5f, Cooldown = 0.15f, Accuracy = 0.35f, Suppression = 3f, Burst = 12, Reload = 2.5f };
                }
            if (f >= 2)
            {
                var steel = GroundMats.Lit(new Color(0.3f, 0.31f, 0.34f), 0.8f, 0.5f);
                Vector2[] tur = { new Vector2(-40f, 30f), new Vector2(40f, 30f), new Vector2(0f, 64f) };
                foreach (var t in tur)
                {
                    var d = Building(map, root, map.ToWorld(t.x, t.y), new Vector3(3.2f, 1.4f, 3.2f), 180f, steel, Destructible.Kind.Turret, 1200f, null, CoverKind.Light);
                    var tt = d.Go.transform;
                    GroundMats.Box(tt, new Vector3(0f, 1.75f, 0f), new Vector3(2.2f, 1.1f, 2.4f), steel, "Housing", null, 0.25f);
                    GroundMats.Box(tt, new Vector3(0f, 1.75f, 1.25f), new Vector3(1.0f, 0.7f, 0.3f), GroundMats.Lit(new Color(0.1f, 0.1f, 0.11f), 0.8f, 0.5f), "Mantlet", null, 0.06f);
                    GroundMats.Part(tt, PrimitiveType.Cylinder, new Vector3(-0.22f, 1.75f, 2.3f), new Vector3(0.22f, 1.0f, 0.22f), steel, "Barrel", Quaternion.Euler(90f, 0f, 0f));
                    GroundMats.Part(tt, PrimitiveType.Cylinder, new Vector3(0.22f, 1.75f, 2.3f), new Vector3(0.22f, 1.0f, 0.22f), steel, "Barrel", Quaternion.Euler(90f, 0f, 0f));
                    GroundMats.Box(tt, new Vector3(0f, 2.35f, -0.4f), new Vector3(0.5f, 0.12f, 0.5f), slit, "Sensor", null, 0.03f);
                    d.Weapon = new WeaponDef { Name = Loc.T("Автопушка турели"), Kind = WeaponKind.Heavy, Range = 42f, Damage = 12f, Cooldown = 0.45f, Accuracy = 0.4f, Suppression = 2.5f, Burst = 6, Reload = 2.5f, Pierce = 0.4f, Splash = 1.2f };
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
                    var d = Building(map, root, map.ToWorld(i == 0 ? -12f : 12f, 82f), new Vector3(5.5f, 2f, 5.5f), 180f, steel, Destructible.Kind.Turret, 2600f, null);
                    var tt = d.Go.transform;
                    GroundMats.Box(tt, new Vector3(0f, 2.4f, -0.3f), new Vector3(3.8f, 1.8f, 4.2f), steel, "Housing", null, 0.4f);
                    GroundMats.Box(tt, new Vector3(0f, 2.4f, 1.9f), new Vector3(1.6f, 1.2f, 0.5f), GroundMats.Lit(new Color(0.1f, 0.1f, 0.11f), 0.8f, 0.5f), "Mantlet", null, 0.1f);
                    GroundMats.Part(tt, PrimitiveType.Cylinder, new Vector3(0f, 2.4f, 4.4f), new Vector3(0.55f, 2.4f, 0.55f), steel, "Barrel", Quaternion.Euler(90f, 0f, 0f));
                    GroundMats.Box(tt, new Vector3(0f, 2.4f, 6.7f), new Vector3(0.9f, 0.7f, 0.6f), GroundMats.Lit(new Color(0.1f, 0.1f, 0.11f), 0.8f, 0.5f), "Brake", null, 0.08f);
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
            GroundMats.MeshPart(go.transform, ProcMesh.Rock(rng.Next()), Vector3.zero, new Vector3(size * 0.78f, size * 0.62f, size * 0.66f), mat, "Stone");
            // Мелкие осколки вокруг
            for (int i = 0; i < 2; i++)
                GroundMats.MeshPart(go.transform, ProcMesh.Rock(rng.Next()), new Vector3(Rand(rng, -1f, 1f) * size, -size * 0.1f, Rand(rng, -1f, 1f) * size), Vector3.one * size * Rand(rng, 0.12f, 0.22f), mat, "Shard");
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


        // ================================================================ Фасады и война

        /// <summary>
        /// Фасад здания: цоколь, карниз, пилястры-контрфорсы, окна по этажам (часть горит тёплым светом, часть разбита и темна),
        /// на крыше — вентиляция и баки. Превращает «коробку» в дом.
        /// </summary>
        private static void Facade(Transform t, Vector3 size, Material trim, Material lit, Material dark, Random rng)
        {
            float y0 = -0.3f;
            GroundMats.Box(t, new Vector3(0f, y0 + 0.5f, 0f), new Vector3(size.x + 0.35f, 1.0f, size.z + 0.35f), trim, "Plinth", null, 0.12f);
            GroundMats.Box(t, new Vector3(0f, y0 + size.y - 0.15f, 0f), new Vector3(size.x + 0.45f, 0.4f, size.z + 0.45f), trim, "Cornice", null, 0.12f);
            // Пилястры на длинных сторонах
            int bays = Mathf.Max(2, Mathf.RoundToInt(size.x / 3f));
            for (int i = 0; i <= bays; i++)
            {
                float x = -size.x * 0.5f + i * size.x / bays;
                foreach (float zs in new[] { -1f, 1f })
                    GroundMats.Box(t, new Vector3(x, y0 + size.y * 0.5f, zs * (size.z * 0.5f + 0.14f)), new Vector3(0.5f, size.y, 0.3f), trim, "Pilaster", null, 0.08f);
            }
            // Окна
            int floors = Mathf.Max(1, Mathf.FloorToInt((size.y - 1.5f) / 3f));
            for (int f = 0; f < floors; f++)
            {
                float y = y0 + 2.3f + f * 3f;
                for (int i = 0; i < bays; i++)
                {
                    float x = -size.x * 0.5f + (i + 0.5f) * size.x / bays;
                    foreach (float zs in new[] { -1f, 1f })
                        GroundMats.Box(t, new Vector3(x, y, zs * (size.z * 0.5f + 0.03f)), new Vector3(Mathf.Min(1.1f, size.x / bays * 0.5f), 1.4f, 0.08f),
                            rng.NextDouble() < 0.45 ? lit : dark, "Window", null, 0.02f);
                }
                int side = Mathf.Max(1, Mathf.RoundToInt(size.z / 3f));
                for (int i = 0; i < side; i++)
                {
                    float z = -size.z * 0.5f + (i + 0.5f) * size.z / side;
                    foreach (float xs in new[] { -1f, 1f })
                        GroundMats.Box(t, new Vector3(xs * (size.x * 0.5f + 0.03f), y, z), new Vector3(0.08f, 1.4f, Mathf.Min(1.1f, size.z / side * 0.5f)),
                            rng.NextDouble() < 0.4 ? lit : dark, "Window", null, 0.02f);
                }
            }
            // Крыша
            var roofMat = GroundMats.Lit(new Color(0.16f, 0.17f, 0.18f), 0.7f, 0.45f);
            int units = 1 + rng.Next(3);
            for (int i = 0; i < units; i++)
                GroundMats.Box(t, new Vector3(Rand(rng, -size.x * 0.3f, size.x * 0.3f), y0 + size.y + 0.45f, Rand(rng, -size.z * 0.3f, size.z * 0.3f)),
                    new Vector3(Rand(rng, 1f, 2.2f), 0.9f, Rand(rng, 1f, 2.2f)), roofMat, "RoofUnit", null, 0.1f);
            if (rng.NextDouble() < 0.5)
                GroundMats.Part(t, PrimitiveType.Cylinder, new Vector3(size.x * 0.25f, y0 + size.y + 1.2f, -size.z * 0.2f), new Vector3(1.6f, 1.1f, 1.6f), roofMat, "Tank");
        }

        /// <summary>
        /// Следы прошлых боёв: сгоревшие остовы техники (тяжёлое укрытие, часть ещё дымит), противотанковые «ежи»
        /// (лёгкое укрытие), разбитые стены (тяжёлое) — на нейтральной полосе, вне площадок, дорог и реки.
        /// </summary>
        private static void Battlefield(GroundMap map, Transform root, Random rng)
        {
            var burnt = GroundMats.Lit(new Color(0.07f, 0.065f, 0.06f), 0.35f, 0.2f);
            var rust = GroundMats.Lit(new Color(0.26f, 0.14f, 0.08f), 0.4f, 0.25f);
            var steel = GroundMats.Lit(new Color(0.2f, 0.2f, 0.21f), 0.85f, 0.45f);
            var wall = GroundMats.Lit(Color.Lerp(new Color(0.4f, 0.39f, 0.37f), map.Biome.GroundC, 0.3f), 0.05f, 0.2f);

            bool Free(Vector2 l, float r)
            {
                if (NearFlat(map, l, r)) return false;
                var w = map.ToWorld(l.x, l.y);
                return map.Inside(w, 10f) && !map.InRiver(w, 3f);
            }

            // Остовы
            int wrecks = 6 + rng.Next(3), placed = 0;
            for (int tries = 0; tries < 40 && placed < wrecks; tries++)
            {
                var l = new Vector2(Rand(rng, -105f, 105f), Rand(rng, -75f, 75f));
                if (!Free(l, 6f)) continue;
                placed++;
                var w = map.ToWorld(l.x, l.y);
                float yaw = Rand(rng, 0f, 360f);
                var d = Building(map, root, w + Vector3.down * 0.25f, new Vector3(3f, 1.2f, 5.6f), yaw, burnt, Destructible.Kind.Rock, 6000f, null);
                d.Side = -1;
                var t = d.Go.transform;
                t.rotation *= Quaternion.Euler(Rand(rng, -5f, 5f), 0f, Rand(rng, -7f, 7f));
                GroundMats.Box(t, new Vector3(-1.75f, 0.35f, 0f), new Vector3(0.8f, 1.1f, 5.8f), rust, "Track", null, 0.25f);
                if (rng.NextDouble() < 0.7)
                    GroundMats.Box(t, new Vector3(1.75f, 0.35f, 0f), new Vector3(0.8f, 1.1f, 5.8f), rust, "Track", null, 0.25f);
                else
                    GroundMats.Box(t, new Vector3(3.2f, 0f, 1.2f), new Vector3(0.8f, 0.3f, 4f), rust, "TrackOff", Quaternion.Euler(0f, 20f, 85f), 0.1f);
                if (rng.NextDouble() < 0.6)
                {
                    // Башня сорвана и лежит рядом
                    var tur = GroundMats.Box(t, new Vector3(Rand(rng, -3f, 3f), 0.6f, Rand(rng, -5f, -3f)), new Vector3(2f, 0.8f, 2.4f), burnt, "Turret",
                        Quaternion.Euler(Rand(rng, -25f, 25f), Rand(rng, 0f, 360f), Rand(rng, -30f, 30f)), 0.2f);
                    GroundMats.Part(tur, PrimitiveType.Cylinder, new Vector3(0f, 0f, 2f), new Vector3(0.22f, 1.2f, 0.22f), steel, "Gun", Quaternion.Euler(90f + Rand(rng, -20f, 20f), 0f, 0f));
                }
                else
                {
                    var tur = GroundMats.Box(t, new Vector3(0f, 1.15f, -0.2f), new Vector3(2f, 0.75f, 2.4f), burnt, "Turret", Quaternion.Euler(0f, Rand(rng, -90f, 90f), 0f), 0.2f);
                    GroundMats.Part(tur, PrimitiveType.Cylinder, new Vector3(0f, -0.1f, 2.2f), new Vector3(0.22f, 1.3f, 0.22f), steel, "Gun", Quaternion.Euler(100f, 0f, 0f));
                }
                if (placed <= 4) map.Burning.Add(w + Vector3.up * 1.2f);
            }

            // Противотанковые ежи: три скрещённые балки, группами
            for (int g = 0; g < 7; g++)
            {
                var c = new Vector2(Rand(rng, -100f, 100f), Rand(rng, -60f, 60f));
                if (!Free(c, 5f)) continue;
                int n = 2 + rng.Next(4);
                float dir = Rand(rng, 0f, 180f) * Mathf.Deg2Rad;
                for (int i = 0; i < n; i++)
                {
                    var l = c + new Vector2(Mathf.Cos(dir), Mathf.Sin(dir)) * (i * 3.2f);
                    if (!Free(l, 2f)) continue;
                    var w = map.ToWorld(l.x, l.y);
                    var go = new GameObject("TankTrap");
                    go.transform.SetParent(root, false);
                    go.transform.position = w;
                    go.transform.rotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
                    GroundMats.Box(go.transform, new Vector3(0f, 0.7f, 0f), new Vector3(0.22f, 2.2f, 0.22f), rust, "Beam", Quaternion.Euler(45f, 0f, 0f), 0.03f);
                    GroundMats.Box(go.transform, new Vector3(0f, 0.7f, 0f), new Vector3(0.22f, 2.2f, 0.22f), rust, "Beam", Quaternion.Euler(-45f, 0f, 0f), 0.03f);
                    GroundMats.Box(go.transform, new Vector3(0f, 0.7f, 0f), new Vector3(0.22f, 2.2f, 0.22f), rust, "Beam", Quaternion.Euler(0f, 0f, 90f), 0.03f);
                    map.Cover.Add(map.Snap(w + go.transform.forward * 1.1f), CoverKind.Light, -go.transform.forward);
                    map.Cover.Add(map.Snap(w - go.transform.forward * 1.1f), CoverKind.Light, go.transform.forward);
                }
            }

            // Разбитые стены: обломок с рваным верхом
            for (int i = 0; i < 6; i++)
            {
                var l = new Vector2(Rand(rng, -95f, 95f), Rand(rng, -70f, 70f));
                if (!Free(l, 5f)) continue;
                var w = map.ToWorld(l.x, l.y);
                float len = Rand(rng, 5f, 9f);
                var d = Building(map, root, w, new Vector3(len, Rand(rng, 1.4f, 2.2f), 0.7f), Rand(rng, 0f, 180f), wall, Destructible.Kind.Building, 900f, null);
                d.Side = -1;
                int chunks = 2 + rng.Next(3);
                for (int k = 0; k < chunks; k++)
                    GroundMats.Box(d.Go.transform, new Vector3(Rand(rng, -len * 0.4f, len * 0.4f), Rand(rng, 1.6f, 2.8f), 0f), new Vector3(Rand(rng, 0.8f, 2f), Rand(rng, 0.8f, 1.8f), 0.7f), wall, "Chunk",
                        Quaternion.Euler(0f, 0f, Rand(rng, -15f, 15f)), 0.08f);
                for (int k = 0; k < 3; k++)
                    GroundMats.MeshPart(d.Go.transform, ProcMesh.Rock(rng.Next()), new Vector3(Rand(rng, -len * 0.5f, len * 0.5f), 0.1f, Rand(rng, -1.6f, 1.6f)), Vector3.one * Rand(rng, 0.3f, 0.6f), wall, "Rubble");
            }
        }


        // ================================================================ Обстановка колонии

        /// <summary>
        /// Обстановка, которой поле «дышит», как в Dawn of War: металлические площадки с жёлто-чёрной разметкой
        /// у районов, штаба и зоны высадки, фонари с тёплым светом, штабеля ящиков и бочек (лёгкое укрытие),
        /// россыпь обломков по всему полю.
        /// </summary>
        private static void Dressing(GroundMap map, Transform root, Random rng)
        {
            var deckTex = DeckTexture(rng);
            var deck = new Material(Rendering.ShaderCache.Lit) { name = "GroundDeck", hideFlags = HideFlags.DontSave };
            if (deck.HasProperty("_BaseMap")) deck.SetTexture("_BaseMap", deckTex);
            if (deck.HasProperty("_MainTex")) deck.SetTexture("_MainTex", deckTex);
            if (deck.HasProperty("_Metallic")) deck.SetFloat("_Metallic", 0.55f);
            if (deck.HasProperty("_Smoothness")) deck.SetFloat("_Smoothness", 0.38f);
            map.Owned.Add(deckTex);
            map.Owned.Add(deck);
            var pole = GroundMats.Lit(new Color(0.1f, 0.1f, 0.11f), 0.8f, 0.45f);
            var lampGlow = GroundMats.Glow(new Color(1f, 0.72f, 0.4f) * 3f);
            var crateA = GroundMats.Lit(new Color(0.24f, 0.26f, 0.18f), 0.2f, 0.3f);
            var crateB = GroundMats.Lit(new Color(0.3f, 0.22f, 0.14f), 0.1f, 0.25f);
            var band = GroundMats.Lit(new Color(0.12f, 0.12f, 0.12f), 0.8f, 0.5f);
            var barrelR = GroundMats.Lit(new Color(0.42f, 0.12f, 0.07f), 0.5f, 0.45f);
            var barrelY = GroundMats.Lit(new Color(0.7f, 0.52f, 0.1f), 0.5f, 0.45f);
            int lights = 0;

            foreach (var f in map.Flats)
            {
                var c = map.ToWorld(f.c.x, f.c.y);
                // Площадка
                float side = f.r * 1.3f;
                var plate = GroundMats.MeshPart(root, ProcMesh.FlatQuad, map.Snap(c) + Vector3.up * 0.05f, new Vector3(side, 1f, side), deck, "Deck",
                    Quaternion.Euler(0f, rng.Next(4) * 90f, 0f));
                plate.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // Фонари по углам площадки
                for (int i = 0; i < 4 && lights < 18; i++)
                {
                    if (rng.NextDouble() < 0.35) continue;
                    float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                    var w = map.Snap(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * side * 0.62f);
                    var go = new GameObject("Lamp");
                    go.transform.SetParent(root, false);
                    go.transform.position = w;
                    go.transform.rotation = Quaternion.LookRotation((c - w).WithY(0f).normalized);
                    GroundMats.Part(go.transform, PrimitiveType.Cylinder, new Vector3(0f, 2.6f, 0f), new Vector3(0.16f, 2.6f, 0.16f), pole, "Pole");
                    GroundMats.Box(go.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.5f, 0.5f, 0.5f), pole, "Foot", null, 0.06f);
                    GroundMats.Box(go.transform, new Vector3(0f, 5.1f, 0.5f), new Vector3(0.12f, 0.12f, 1.1f), pole, "Arm", null, 0.02f);
                    GroundMats.Box(go.transform, new Vector3(0f, 4.95f, 1.0f), new Vector3(0.42f, 0.18f, 0.32f), pole, "Head", null, 0.04f);
                    GroundMats.Box(go.transform, new Vector3(0f, 4.85f, 1.0f), new Vector3(0.34f, 0.04f, 0.24f), lampGlow, "Bulb", null, 0.01f);
                    var l = new GameObject("LampLight").AddComponent<Light>();
                    l.transform.SetParent(go.transform, false);
                    l.transform.localPosition = new Vector3(0f, 4.6f, 1.0f);
                    l.type = LightType.Point;
                    l.color = new Color(1f, 0.68f, 0.38f);
                    l.range = 13f;
                    l.intensity = 2.6f;
                    l.shadows = LightShadows.None;
                    lights++;
                }
                // Ящики и бочки — лёгкое укрытие
                int stacks = 2 + rng.Next(2);
                for (int k = 0; k < stacks; k++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    var w = map.Snap(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * f.r * Rand(rng, 0.45f, 0.7f));
                    var go = new GameObject("Supplies");
                    go.transform.SetParent(root, false);
                    go.transform.position = w;
                    go.transform.rotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
                    int crates = 2 + rng.Next(3);
                    for (int i = 0; i < crates; i++)
                    {
                        var size = new Vector3(Rand(rng, 0.9f, 1.4f), Rand(rng, 0.7f, 1.1f), Rand(rng, 0.9f, 1.4f));
                        var pos = new Vector3((i % 2) * 1.35f - 0.6f, size.y * 0.5f + (i >= 2 ? 1f : 0f), (i / 2 % 2) * 0.2f);
                        var cr = GroundMats.Box(go.transform, pos, size, rng.NextDouble() < 0.5 ? crateA : crateB, "Crate", Quaternion.Euler(0f, Rand(rng, -12f, 12f), 0f), 0.05f);
                        GroundMats.Box(cr, Vector3.zero, new Vector3(size.x + 0.03f, 0.08f, size.z + 0.03f), band, "Strap", null, 0.01f);
                    }
                    int barrels = 2 + rng.Next(3);
                    for (int i = 0; i < barrels; i++)
                        GroundMats.Part(go.transform, PrimitiveType.Cylinder, new Vector3(1.6f + (i % 2) * 0.75f, 0.5f, -0.4f + (i / 2) * 0.75f), new Vector3(0.65f, 0.5f, 0.65f),
                            rng.NextDouble() < 0.5 ? barrelR : barrelY, "Barrel");
                    map.Cover.Add(map.Snap(w + go.transform.forward * 1.4f), CoverKind.Light, -go.transform.forward);
                    map.Cover.Add(map.Snap(w - go.transform.forward * 1.4f), CoverKind.Light, go.transform.forward);
                }
            }

            // Обломки и камешки по всему полю (без коллайдеров)
            var rubble = GroundMats.Lit(Color.Lerp(map.Biome.Rock, map.Biome.GroundB, 0.4f) * 0.9f, 0.05f, 0.2f);
            var metal = GroundMats.Lit(new Color(0.18f, 0.16f, 0.15f), 0.7f, 0.35f);
            for (int i = 0; i < 160; i++)
            {
                var l = new Vector2(Rand(rng, -118f, 118f), Rand(rng, -110f, 110f));
                var w = map.ToWorld(l.x, l.y);
                if (map.InRiver(w, 1f)) continue;
                w = map.Snap(w);
                if (rng.NextDouble() < 0.75)
                    GroundMats.MeshPart(root, ProcMesh.Rock(rng.Next()), w, Vector3.one * Rand(rng, 0.12f, 0.45f), rubble, "Pebble",
                        Quaternion.Euler(Rand(rng, 0f, 360f), Rand(rng, 0f, 360f), Rand(rng, 0f, 360f)));
                else
                    GroundMats.Box(root, w + Vector3.up * 0.05f, new Vector3(Rand(rng, 0.3f, 1.4f), 0.06f, Rand(rng, 0.2f, 0.8f)), metal, "Scrap",
                        Quaternion.Euler(Rand(rng, -10f, 10f), Rand(rng, 0f, 360f), Rand(rng, -10f, 10f)), 0.01f);
            }
        }

        /// <summary>Настил площадки: стальные плиты со швами и заклёпками, грязь и копоть, жёлто-чёрная разметка по краю.</summary>
        private static Texture2D DeckTexture(Random rng)
        {
            const int N = 512;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Deck", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave, anisoLevel = 8 };
            var px = new Color[N * N];
            float ox = (float)rng.NextDouble() * 50f;
            const int plates = 4;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    int pxI = (int)(u * plates), pyI = (int)(v * plates);
                    float fu = u * plates - pxI, fv = v * plates - pyI;
                    float tone = 0.34f + ((pxI * 7 + pyI * 13) % 5) * 0.012f;
                    var c = new Color(tone, tone * 0.98f, tone * 0.95f);
                    // Рифление плит
                    float tread = Mathf.Abs(Mathf.Sin((u + v) * 260f)) > 0.92f ? 0.04f : 0f;
                    c += new Color(tread, tread, tread);
                    // Швы и заклёпки
                    float seam = Mathf.Min(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv));
                    if (seam < 0.012f) c *= 0.35f;
                    else if (seam < 0.03f) c *= 1.12f;
                    foreach (var cu in new[] { 0.06f, 0.94f })
                        foreach (var cv in new[] { 0.06f, 0.94f })
                            if ((fu - cu) * (fu - cu) + (fv - cv) * (fv - cv) < 0.00025f) c = new Color(0.55f, 0.53f, 0.5f);
                    // Грязь и копоть
                    float grime = Mathf.PerlinNoise(u * 6f + ox, v * 6f);
                    c *= Mathf.Lerp(0.6f, 1.05f, grime);
                    float soot = Mathf.PerlinNoise(u * 2.2f + ox * 2f, v * 2.2f + 3f);
                    if (soot > 0.66f) c *= Mathf.Lerp(1f, 0.45f, (soot - 0.66f) * 3f);
                    // Жёлто-чёрная разметка по краю
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    if (edge < 0.05f && edge > 0.012f)
                    {
                        bool yellow = Mathf.Repeat((u + v) * 22f, 1f) < 0.5f;
                        var stripe = yellow ? new Color(0.78f, 0.6f, 0.08f) : new Color(0.06f, 0.06f, 0.06f);
                        c = Color.Lerp(c, stripe * Mathf.Lerp(0.65f, 1f, grime), 0.9f);
                    }
                    c.a = 1f;
                    px[y * N + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
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
