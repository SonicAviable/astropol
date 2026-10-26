using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Процедурные модели чудес фракций. Каждая модель — набор частей с пометкой (этап, порядок внутри этапа):
    /// часть готового этапа — металл, часть строящегося этапа — металл до текущего прогресса и голограмма лесов дальше,
    /// будущие этапы не видны. Так стройка растёт на глазах: панель за панелью, сегмент за сегментом.
    /// Единицы — как в виде системы (звезда в нуле, первая планета на радиусе 13–17); на карте галактики модель уменьшается.
    ///   • Сфера Дайсона — геодезические панели-коллекторы с золотой решёткой, от экватора к полюсам; потоки с полюсов.
    ///   • Кольцо Согласия — наклонное кольцо-мир: жилая лента с городами, борта, обшивка; растёт от трёх якорей.
    ///   • Врата Провидения — кристаллическое кольцо из 12 сегментов, пилоны и воронка горизонта.
    ///   • Наковальня Ксарна — гироскоп из трёх колец тяжёлых блоков с раскалёнными швами, кузня и струи плазмы.
    ///   • Древо Миров — три живых ствола от пояса к звезде, крона-колыбель вокруг светила и светящиеся плоды.
    /// </summary>
    public static class WonderModels
    {
        public struct Spec
        {
            public string Key;
            public int Built;
            public bool InProgress;
            public float Progress;
            public float StarMul;
            /// <summary>Чертёж (окно чуда): все ещё не построенные части видны голограммой лесов.</summary>
            public bool Blueprint;
        }

        /// <summary>Сигнатура состояния: модель пересобирается, только когда она меняется (прогресс — шагами по 5%).</summary>
        public static int Signature(Spec s) => (s.Key?.GetHashCode() ?? 0) ^ (s.Built * 7919) ^ (s.InProgress ? 104729 : 0)
                                                ^ (Mathf.FloorToInt(s.Progress * 20f) * 31) ^ (s.Blueprint ? 1299709 : 0);

        /// <summary>Модель чуда под parent. Врата строятся вокруг собственного центра, остальное — вокруг звезды.</summary>
        public static GameObject Build(Spec s, Transform parent)
        {
            var root = new GameObject("Wonder_" + s.Key);
            root.transform.SetParent(parent, false);
            var spin = root.AddComponent<WonderSpin>();
            switch (s.Key)
            {
                case "aquila": Dyson(s, root.transform, spin); break;
                case "astrea": Ring(s, root.transform, spin); break;
                case "iridia": Gate(s, root.transform, spin); break;
                case "xarn": Forge(s, root.transform, spin); break;
                case "terraan": Tree(s, root.transform, spin); break;
            }
            return root;
        }

        // ==================== ЧАСТИ И ЭТАПЫ ====================

        private enum Show { Hidden, Solid, Scaffold }

        private static Show Part(Spec s, int stage, float order)
        {
            if (stage < s.Built) return Show.Solid;
            if (stage == s.Built && s.InProgress) return order <= s.Progress ? Show.Solid : Show.Scaffold;
            return s.Blueprint ? Show.Scaffold : Show.Hidden;
        }

        // ==================== МАТЕРИАЛЫ ====================

        private static readonly Dictionary<string, Material> s_mats = new Dictionary<string, Material>();
        private static Shader s_fx;
        private static bool s_fxMissing;
        private static Texture2D s_habitat, s_habitatLights;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_mats.Clear();
            s_fx = null;
            s_fxMissing = false;
            s_habitat = s_habitatLights = null;
            s_dysonTex = s_dysonEm = null;
            s_unitIco = null;
        }

        private static Material Lit(string key, Color baseCol, Color emission, float metallic, float smooth)
        {
            if (s_mats.TryGetValue(key, out var m) && m != null) return m;
            m = ShipMeshFactory.LitMat(baseCol, emission, metallic, smooth);
            m.name = "Wonder_" + key;
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // тонкие ленты и панели видны с обеих сторон
            m.doubleSidedGI = true;
            s_mats[key] = m;
            return m;
        }

        /// <summary>Аддитивное свечение WonderFx (режимы см. шейдер); без шейдера — простой Unlit.</summary>
        private static Material Fx(string key, Color c, int mode, float speed = 1f)
        {
            if (s_mats.TryGetValue(key, out var m) && m != null) return m;
            if (s_fx == null && !s_fxMissing)
            {
                s_fx = Resources.Load<Shader>("Shaders/WonderFx");
                s_fxMissing = s_fx == null;
            }
            if (s_fx != null)
            {
                m = new Material(s_fx) { name = "WonderFx_" + key, hideFlags = HideFlags.DontSave };
                m.SetColor("_Color", c);
                m.SetFloat("_Mode", mode);
                m.SetFloat("_Speed", speed);
            }
            else m = ShipMeshFactory.GlowMat(c);
            s_mats[key] = m;
            return m;
        }

        private static Material Scaffold(Spec s, Color faction) => Fx("scaffold_" + s.Key, faction * 1.6f, 3, 1f);

        // ==================== СБОРКА МЕШЕЙ ====================

        private sealed class MB
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();
            public bool Lines;

            public int Add(Vector3 v, Vector3 n, Vector2 uv, Color c)
            {
                V.Add(v); N.Add(n); UV.Add(uv); C.Add(c);
                return V.Count - 1;
            }

            public int Add(Vector3 v, Vector3 n, Vector2 uv) => Add(v, n, uv, Color.white);

            public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
            public void Line(int a, int b) { T.Add(a); T.Add(b); }

            public void Seg(Vector3 a, Vector3 b, Color c)
            {
                int i = Add(a, Vector3.up, Vector2.zero, c);
                int j = Add(b, Vector3.up, Vector2.right, c);
                Line(i, j);
            }

            public bool Empty => T.Count == 0;

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetUVs(0, UV);
                m.SetColors(C);
                if (Lines) m.SetIndices(T, MeshTopology.Lines, 0);
                else m.SetTriangles(T, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        private static GameObject Emit(MB mb, Transform parent, string name, Material mat)
        {
            if (mb == null || mb.Empty || mat == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private static Vector3 Dir(float a) => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

        /// <summary>Лента цилиндра (ось Y) радиуса r от угла a0 до a1; uv.x — доля полного круга × repeat.</summary>
        private static void Band(MB mb, float r, float y0, float y1, float a0, float a1, bool inward, float uRepeat = 1f)
        {
            int seg = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 0.05f));
            int start = mb.V.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)seg);
                var d = Dir(a);
                var n = inward ? -d : d;
                float u = a / (Mathf.PI * 2f) * uRepeat;
                mb.Add(d * r + Vector3.up * y0, n, new Vector2(u, 0f));
                mb.Add(d * r + Vector3.up * y1, n, new Vector2(u, 1f));
            }
            for (int i = 0; i < seg; i++)
            {
                int b = start + i * 2;
                if (inward) { mb.Tri(b, b + 3, b + 1); mb.Tri(b, b + 2, b + 3); }
                else { mb.Tri(b, b + 1, b + 3); mb.Tri(b, b + 3, b + 2); }
            }
        }

        /// <summary>Плоское кольцо-полоса в плоскости y (борта кольца-мира).</summary>
        private static void Annulus(MB mb, float rIn, float rOut, float y, float a0, float a1, bool up)
        {
            int seg = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 0.05f));
            int start = mb.V.Count;
            var n = up ? Vector3.up : Vector3.down;
            for (int i = 0; i <= seg; i++)
            {
                var d = Dir(Mathf.Lerp(a0, a1, i / (float)seg));
                mb.Add(d * rIn + Vector3.up * y, n, new Vector2(i / (float)seg, 0f));
                mb.Add(d * rOut + Vector3.up * y, n, new Vector2(i / (float)seg, 1f));
            }
            for (int i = 0; i < seg; i++)
            {
                int b = start + i * 2;
                mb.Tri(b, b + 1, b + 3); mb.Tri(b, b + 3, b + 2);
            }
        }

        /// <summary>Тор (или его дуга) радиуса R с трубкой r в плоскости XZ.</summary>
        private static void Torus(MB mb, float R, float r, float a0, float a1, int sides = 8)
        {
            int seg = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 0.07f));
            int start = mb.V.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)seg);
                var d = Dir(a);
                for (int j = 0; j <= sides; j++)
                {
                    float b = j / (float)sides * Mathf.PI * 2f;
                    var n = d * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    mb.Add(d * R + n * r, n, new Vector2(i / (float)seg, j / (float)sides));
                }
            }
            int row = sides + 1;
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < sides; j++)
                {
                    int v00 = start + i * row + j, v10 = v00 + row, v01 = v00 + 1, v11 = v10 + 1;
                    mb.Tri(v00, v01, v11); mb.Tri(v00, v11, v10);
                }
        }

        private static readonly Vector3[] CubeN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        /// <summary>Ящик: центр, поворот, размеры.</summary>
        private static void Box(MB mb, Vector3 c, Quaternion q, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            foreach (var n in CubeN)
            {
                // Две оси, перпендикулярные нормали грани
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(n, u);
                int s = mb.V.Count;
                for (int k = 0; k < 4; k++)
                {
                    float su = (k == 0 || k == 3) ? -1f : 1f, sw = k < 2 ? -1f : 1f;
                    Vector3 p = n + u * su + w * sw;
                    mb.Add(c + q * Vector3.Scale(p, h), q * n, new Vector2(su * 0.5f + 0.5f, sw * 0.5f + 0.5f), col);
                }
                mb.Tri(s, s + 1, s + 2); mb.Tri(s, s + 2, s + 3);
            }
        }

        private static void BoxEdges(MB mb, Vector3 c, Quaternion q, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(int i) => c + q * Vector3.Scale(new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1), h);
            int[] e = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
            for (int i = 0; i < e.Length; i += 2) mb.Seg(P(e[i]), P(e[i + 1]), col);
        }

        /// <summary>Ромб-кристалл от base к tip шириной w (шесть граней-лепестков).</summary>
        private static void Diamond(MB mb, Vector3 a, Vector3 b, float w, Color col)
        {
            Vector3 axis = b - a;
            Vector3 mid = a + axis * 0.38f;
            Vector3 side = Vector3.Cross(axis.normalized, Mathf.Abs(axis.normalized.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 side2 = Vector3.Cross(axis.normalized, side).normalized;
            var ring = new Vector3[4];
            for (int k = 0; k < 4; k++)
            {
                float t = k * Mathf.PI * 0.5f;
                ring[k] = mid + (side * Mathf.Cos(t) + side2 * Mathf.Sin(t)) * w;
            }
            for (int k = 0; k < 4; k++)
            {
                var r0 = ring[k]; var r1 = ring[(k + 1) % 4];
                Face(mb, r0, r1, b, col);
                Face(mb, r1, r0, a, col);
            }
        }

        private static void Face(MB mb, Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int i = mb.Add(a, n, new Vector2(0, 0), col), j = mb.Add(b, n, new Vector2(1, 0), col), k = mb.Add(c, n, new Vector2(0.5f, 1), col);
            mb.Tri(i, j, k);
        }

        /// <summary>Трубка вдоль ломаной с переменным радиусом (стволы и ветви Древа).</summary>
        private static void Tube(MB mb, IList<Vector3> pts, IList<float> radii, int sides = 7)
        {
            if (pts.Count < 2) return;
            Vector3 prevN = Vector3.zero;
            int start = mb.V.Count;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 t = (i == 0 ? pts[1] - pts[0] : i == pts.Count - 1 ? pts[i] - pts[i - 1] : pts[i + 1] - pts[i - 1]).normalized;
                Vector3 n = prevN == Vector3.zero
                    ? Vector3.Cross(t, Mathf.Abs(t.y) > 0.9f ? Vector3.right : Vector3.up).normalized
                    : Vector3.ProjectOnPlane(prevN, t).normalized;
                if (n == Vector3.zero) n = Vector3.Cross(t, Vector3.right).normalized;
                prevN = n;
                Vector3 b = Vector3.Cross(t, n);
                for (int j = 0; j <= sides; j++)
                {
                    float a = j / (float)sides * Mathf.PI * 2f;
                    Vector3 d = n * Mathf.Cos(a) + b * Mathf.Sin(a);
                    mb.Add(pts[i] + d * radii[i], d, new Vector2(i / (float)(pts.Count - 1) * 4f, j / (float)sides));
                }
            }
            int row = sides + 1;
            for (int i = 0; i < pts.Count - 1; i++)
                for (int j = 0; j < sides; j++)
                {
                    int v00 = start + i * row + j, v10 = v00 + row, v01 = v00 + 1, v11 = v10 + 1;
                    mb.Tri(v00, v11, v01); mb.Tri(v00, v10, v11);
                }
        }

        /// <summary>Квадраты «крестом» вдоль отрезка — луч, видимый с любой стороны (uv.x вдоль, uv.y поперёк).</summary>
        private static void Beam(MB mb, Vector3 a, Vector3 b, float w, Color col)
        {
            Vector3 ax = (b - a).normalized;
            Vector3 s1 = Vector3.Cross(ax, Mathf.Abs(ax.y) > 0.9f ? Vector3.right : Vector3.up).normalized * w;
            Vector3 s2 = Vector3.Cross(ax, s1).normalized * w;
            foreach (var s in new[] { s1, s2 })
            {
                int i0 = mb.Add(a - s, Vector3.up, new Vector2(0, 0), col), i1 = mb.Add(a + s, Vector3.up, new Vector2(0, 1), col);
                int i2 = mb.Add(b + s, Vector3.up, new Vector2(1, 1), col), i3 = mb.Add(b - s, Vector3.up, new Vector2(1, 0), col);
                mb.Tri(i0, i1, i2); mb.Tri(i0, i2, i3);
            }
        }

        /// <summary>Диск-квад в плоскости XZ (uv 0..1) — горизонт Врат.</summary>
        private static void Disc(MB mb, float r, Color col)
        {
            int a = mb.Add(new Vector3(-r, 0, -r), Vector3.up, new Vector2(0, 0), col);
            int b = mb.Add(new Vector3(-r, 0, r), Vector3.up, new Vector2(0, 1), col);
            int c = mb.Add(new Vector3(r, 0, r), Vector3.up, new Vector2(1, 1), col);
            int d = mb.Add(new Vector3(r, 0, -r), Vector3.up, new Vector2(1, 0), col);
            mb.Tri(a, b, c); mb.Tri(a, c, d);
        }

        private static void Arc(MB mb, float r, float y, float a0, float a1, Color col)
        {
            int seg = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 0.08f));
            for (int i = 0; i < seg; i++)
                mb.Seg(Dir(Mathf.Lerp(a0, a1, i / (float)seg)) * r + Vector3.up * y, Dir(Mathf.Lerp(a0, a1, (i + 1) / (float)seg)) * r + Vector3.up * y, col);
        }

        // ---- Икосфера (для Сферы Дайсона и светящихся плодов) ----

        private static List<Vector3[]> Icosphere(int subdiv)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            var tris = new List<Vector3[]>();
            for (int i = 0; i < f.Length; i += 3) tris.Add(new[] { v[f[i]].normalized, v[f[i + 1]].normalized, v[f[i + 2]].normalized });
            for (int s = 0; s < subdiv; s++)
            {
                var next = new List<Vector3[]>(tris.Count * 4);
                foreach (var tr in tris)
                {
                    var ab = (tr[0] + tr[1]).normalized; var bc = (tr[1] + tr[2]).normalized; var ca = (tr[2] + tr[0]).normalized;
                    next.Add(new[] { tr[0], ab, ca }); next.Add(new[] { tr[1], bc, ab });
                    next.Add(new[] { tr[2], ca, bc }); next.Add(new[] { ab, bc, ca });
                }
                tris = next;
            }
            return tris;
        }

        private static List<Vector3[]> s_unitIco;

        /// <summary>Маленькая сфера (плод, узел) в общий меш.</summary>
        private static void Ball(MB mb, Vector3 c, float r, Color col)
        {
            s_unitIco ??= Icosphere(1);
            foreach (var tr in s_unitIco)
            {
                int a = mb.Add(c + tr[0] * r, tr[0], Vector2.zero, col);
                int b = mb.Add(c + tr[1] * r, tr[1], Vector2.zero, col);
                int d = mb.Add(c + tr[2] * r, tr[2], Vector2.zero, col);
                mb.Tri(a, d, b);
            }
        }

        // ==================== СФЕРА ДАЙСОНА ====================

        private static void Dyson(Spec s, Transform root, WonderSpin spin)
        {
            Color gold = new Color(1f, 0.78f, 0.3f);
            float R = 3.2f * Mathf.Max(0.8f, s.StarMul);
            var shell = new GameObject("Shell").transform;
            shell.SetParent(root, false);
            spin.Add(shell, Vector3.up, 2.2f);

            var panels = new MB();
            var lattice = new MB { Lines = true };
            var scaffold = new MB { Lines = true };
            var hull = DysonPanelMat();
            var inner = Lit("dyson_inner", new Color(0.9f, 0.7f, 0.35f), new Color(0.55f, 0.32f, 0.08f), 0.2f, 0.5f);
            var back = new MB();

            foreach (var tr in Icosphere(2))
            {
                Vector3 c = (tr[0] + tr[1] + tr[2]).normalized;
                float lat = Mathf.Abs(c.y);
                if (lat > 0.96f) continue;                       // полюса открыты — оттуда бьют потоки
                int stage = lat < 0.24f ? 0 : lat < 0.62f ? 1 : 2;
                float order = (Mathf.Atan2(c.z, c.x) + Mathf.PI) / (Mathf.PI * 2f);
                // Экватор — сначала каждая вторая панель («рой»), потом остальные
                if (stage == 0) order = order * 0.5f + (Mathf.Repeat(Mathf.Floor(order * 40f), 2f) > 0.5f ? 0.5f : 0f);
                var show = Part(s, stage, order);
                if (show == Show.Hidden) continue;

                var p = new Vector3[3];
                for (int k = 0; k < 3; k++) p[k] = Vector3.Lerp(c, tr[k], 0.86f).normalized * R;
                if (show == Show.Solid)
                {
                    int a = panels.Add(p[0], c, new Vector2(0, 0)), b = panels.Add(p[1], c, new Vector2(1, 0)), d = panels.Add(p[2], c, new Vector2(0.5f, 1));
                    panels.Tri(a, d, b);
                    // Изнанка — тёплая, её подсвечивает звезда сквозь щели
                    Vector3 off = -c * 0.05f * R;
                    int a2 = back.Add(p[0] + off, -c, Vector2.zero), b2 = back.Add(p[1] + off, -c, Vector2.zero), d2 = back.Add(p[2] + off, -c, Vector2.zero);
                    back.Tri(a2, b2, d2);
                    for (int k = 0; k < 3; k++) lattice.Seg(tr[k] * R * 1.004f, tr[(k + 1) % 3] * R * 1.004f, gold);
                }
                else
                {
                    for (int k = 0; k < 3; k++) scaffold.Seg(p[k] * 1.01f, p[(k + 1) % 3] * 1.01f, gold);
                }
            }
            Emit(panels, shell, "Panels", hull);
            Emit(back, shell, "Inner", inner);
            Emit(lattice, shell, "Lattice", Fx("dyson_lattice", gold * 0.9f, 0));
            Emit(scaffold, shell, "Scaffold", Scaffold(s, gold));

            // Завершённая сфера: с полюсов бьют потоки собранной энергии
            if (s.Built >= 3)
            {
                var beams = new MB();
                Beam(beams, Vector3.up * R * 0.7f, Vector3.up * R * 2.6f, 0.45f, Color.white);
                Beam(beams, Vector3.down * R * 0.7f, Vector3.down * R * 2.6f, 0.45f, Color.white);
                Emit(beams, root, "PolarJets", Fx("dyson_jet", new Color(1f, 0.85f, 0.45f) * 2.2f, 2, 1.2f));
            }
        }

        private static Texture2D s_dysonTex, s_dysonEm;

        /// <summary>
        /// Панель-коллектор (uv треугольника: (0,0), (1,0), (0.5,1)): тёмные солнечные ячейки в золотой раме.
        /// Звезда светит изнутри, снаружи панели в тени — рама и сетка ячеек светятся сами, сфера не выглядит чёрной.
        /// </summary>
        private static Material DysonPanelMat()
        {
            const string key = "dyson_panel";
            if (s_mats.TryGetValue(key, out var m) && m != null) return m;
            if (s_dysonTex == null)
            {
                const int N = 128;
                s_dysonTex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "DysonPanel", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                s_dysonEm = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "DysonPanelEm", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var c = new Color[N * N];
                var e = new Color[N * N];
                var A = new Vector2(0f, 0f); var B = new Vector2(1f, 0f); var C = new Vector2(0.5f, 1f);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        var p = new Vector2((x + 0.5f) / N, (y + 0.5f) / N);
                        float d = Mathf.Min(EdgeDist(p, A, B), Mathf.Min(EdgeDist(p, B, C), EdgeDist(p, C, A)));
                        float frame = 1f - Mathf.SmoothStep(0.025f, 0.06f, d);
                        float cells = Mathf.Max(Mathf.Repeat(p.x * 9f, 1f) < 0.07f ? 1f : 0f, Mathf.Repeat(p.y * 9f, 1f) < 0.07f ? 1f : 0f);
                        float sheen = Mathf.Clamp01(1f - Vector2.Distance(p, new Vector2(0.45f, 0.42f)) * 2.2f);
                        Color baseC = Color.Lerp(new Color(0.06f, 0.08f, 0.13f), new Color(0.14f, 0.16f, 0.24f), cells * 0.8f + sheen * 0.3f);
                        c[y * N + x] = Color.Lerp(baseC, new Color(0.9f, 0.68f, 0.3f), frame);
                        e[y * N + x] = new Color(1f, 0.72f, 0.3f) * (frame * 0.9f + cells * 0.06f) + new Color(0.25f, 0.35f, 0.6f) * sheen * 0.12f;
                    }
                s_dysonTex.SetPixels(c); s_dysonTex.Apply(true);
                s_dysonEm.SetPixels(e); s_dysonEm.Apply(true);
            }
            m = Lit(key, Color.white, Color.black, 0.65f, 0.86f);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", s_dysonTex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", s_dysonTex);
            if (m.HasProperty("_EmissionMap"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", s_dysonEm);
                m.SetColor("_EmissionColor", new Color(1.5f, 1.2f, 0.8f));
            }
            return m;
        }

        private static float EdgeDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // ==================== КОЛЬЦО СОГЛАСИЯ ====================

        private static Texture2D HabitatTex(bool lights)
        {
            if (lights ? s_habitatLights != null : s_habitat != null) return lights ? s_habitatLights : s_habitat;
            const int W = 1024, H = 64;
            var col = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "RingHabitat", wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
            var em = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "RingLights", wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
            var rng = new System.Random(7);
            var c = new Color[W * H];
            var e = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = y / (float)H;
                    float land = Mathf.PerlinNoise(u * 22f, v * 3f) * 0.7f + Mathf.PerlinNoise(u * 70f, v * 9f) * 0.3f;
                    bool sea = land < 0.42f;
                    Color g = sea ? Color.Lerp(new Color(0.05f, 0.2f, 0.35f), new Color(0.1f, 0.35f, 0.5f), land / 0.42f)
                                  : Color.Lerp(new Color(0.18f, 0.42f, 0.2f), new Color(0.5f, 0.55f, 0.35f), (land - 0.42f) * 1.8f);
                    // Облака и края (у бортов — серые технические полосы)
                    float cloud = Mathf.Clamp01(Mathf.PerlinNoise(u * 40f + 9f, v * 6f) * 1.6f - 0.95f);
                    g = Color.Lerp(g, Color.white, cloud * 0.7f);
                    if (v < 0.08f || v > 0.92f) g = new Color(0.45f, 0.48f, 0.52f);
                    c[y * W + x] = g;
                    // Огни городов — на суше, гуще у «магистралей»
                    float city = !sea && v > 0.1f && v < 0.9f && rng.NextDouble() < 0.07 + 0.25 * Mathf.Pow(Mathf.PerlinNoise(u * 30f, 3f), 4f) ? 1f : 0f;
                    e[y * W + x] = new Color(1f, 0.8f, 0.45f) * city + (v < 0.06f || v > 0.94f ? new Color(0.3f, 0.85f, 1f) : Color.black);
                }
            col.SetPixels(c); col.Apply(true);
            em.SetPixels(e); em.Apply(true);
            s_habitat = col; s_habitatLights = em;
            return lights ? em : col;
        }

        private static Material HabitatMat(bool lit)
        {
            string key = lit ? "ring_habitat_lit" : "ring_habitat";
            if (s_mats.TryGetValue(key, out var m) && m != null) return m;
            m = Lit(key, Color.white, Color.black, 0.05f, 0.35f);
            var tex = HabitatTex(false);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_EmissionMap"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", HabitatTex(true));
                m.SetColor("_EmissionColor", lit ? new Color(1.6f, 1.4f, 1.2f) : new Color(0.5f, 0.45f, 0.4f));
            }
            return m;
        }

        private static void Ring(Spec s, Transform root, WonderSpin spin)
        {
            Color cyan = new Color(0.35f, 0.85f, 1f);
            const float R = 5.6f, W = 0.95f, Wall = 0.32f, HullOff = 0.14f;
            var tilt = new GameObject("Tilt").transform;
            tilt.SetParent(root, false);
            tilt.localRotation = Quaternion.Euler(34f, 0f, 9f);
            var ring = new GameObject("Ring").transform;
            ring.SetParent(tilt, false);
            spin.Add(ring, Vector3.up, 1.6f);

            var habitat = new MB();
            var hullMb = new MB();
            var rims = new MB { Lines = true };
            var scaffold = new MB { Lines = true };
            var anchors = new MB();
            var scaffoldBoxes = new MB { Lines = true };

            const int Segs = 72;
            float step = Mathf.PI * 2f / Segs;
            for (int j = 0; j < Segs; j++)
            {
                float a0 = j * step, a1 = a0 + step, mid = a0 + step * 0.5f;
                float deg = Mathf.Repeat(mid * Mathf.Rad2Deg, 120f);
                float dist = Mathf.Min(deg, 120f - deg);                 // до ближайшего якоря, 0…60°
                int stage = dist < 15f ? 0 : dist < 45f ? 1 : 2;
                float order = stage == 0 ? 0.3f + dist / 15f * 0.7f : stage == 1 ? (dist - 15f) / 30f : (dist - 45f) / 15f;
                var show = Part(s, stage, order);
                if (show == Show.Hidden) continue;
                if (show == Show.Solid)
                {
                    Band(habitat, R, -W * 0.5f, W * 0.5f, a0, a1 + 0.002f, true, 6f);
                    Band(hullMb, R + HullOff, -W * 0.5f - 0.04f, W * 0.5f + 0.04f, a0, a1 + 0.002f, false);
                    Annulus(hullMb, R - Wall, R + HullOff, W * 0.5f, a0, a1 + 0.002f, true);
                    Annulus(hullMb, R - Wall, R + HullOff, -W * 0.5f, a0, a1 + 0.002f, false);
                    Arc(rims, R - Wall, W * 0.5f + 0.01f, a0, a1, cyan);
                    Arc(rims, R - Wall, -W * 0.5f - 0.01f, a0, a1, cyan);
                }
                else
                {
                    Arc(scaffold, R, W * 0.5f, a0, a1, cyan);
                    Arc(scaffold, R, -W * 0.5f, a0, a1, cyan);
                    scaffold.Seg(Dir(a0) * R + Vector3.up * W * 0.5f, Dir(a1) * R - Vector3.up * W * 0.5f, cyan);
                }
            }

            // Три якорные станции — первым делом этапа I
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2f / 3f;
                var show = Part(s, 0, 0.15f + k * 0.05f);
                if (show == Show.Hidden) continue;
                var q = Quaternion.LookRotation(Dir(a), Vector3.up);
                var c = Dir(a) * (R + 0.55f);
                if (show == Show.Solid)
                {
                    Box(anchors, c, q, new Vector3(1.5f, 1.7f, 0.9f), Color.white);
                    Box(anchors, c + Dir(a) * 0.65f, q, new Vector3(0.6f, 0.6f, 0.6f), Color.white);
                }
                else BoxEdges(scaffoldBoxes, c, q, new Vector3(1.5f, 1.7f, 0.9f), cyan);
            }

            bool closed = s.Built >= 3;
            Emit(habitat, ring, "Habitat", HabitatMat(closed));
            Emit(hullMb, ring, "Hull", Lit("ring_hull", new Color(0.72f, 0.76f, 0.82f), new Color(0.06f, 0.07f, 0.09f), 0.7f, 0.65f));
            Emit(anchors, ring, "Anchors", Lit("ring_anchor", new Color(0.82f, 0.85f, 0.9f), new Color(0.05f, 0.08f, 0.1f), 0.6f, 0.6f));
            Emit(rims, ring, "Rims", Fx("ring_rim", cyan * (closed ? 1.6f : 1f), 0));
            Emit(scaffold, ring, "Scaffold", Scaffold(s, cyan));
            Emit(scaffoldBoxes, ring, "ScaffoldAnchors", Scaffold(s, cyan));
        }

        // ==================== ВРАТА ПРОВИДЕНИЯ ====================

        private static void Gate(Spec s, Transform root, WonderSpin spin)
        {
            Color violet = new Color(0.72f, 0.5f, 1f);
            const float R = 2.6f, r = 0.2f;
            var tilt = new GameObject("Tilt").transform;
            tilt.SetParent(root, false);
            tilt.localRotation = Quaternion.Euler(30f, 0f, 0f);
            var ringT = new GameObject("Ring").transform;
            ringT.SetParent(tilt, false);
            spin.Add(ringT, Vector3.up, -3f);

            var crystal = new MB();
            var scaffold = new MB { Lines = true };
            var nodes = new MB();
            const int Segs = 12;
            float step = Mathf.PI * 2f / Segs, gap = 0.07f;
            for (int k = 0; k < Segs; k++)
            {
                float a0 = k * step + gap * 0.5f, a1 = (k + 1) * step - gap * 0.5f;
                var show = Part(s, 0, (k + 0.5f) / Segs);
                if (show == Show.Solid)
                {
                    Torus(crystal, R, r, a0, a1, 6);
                    // Гребень-кристалл на каждом сегменте
                    Vector3 m = Dir((a0 + a1) * 0.5f);
                    Diamond(crystal, m * (R + r * 0.6f), m * (R + r * 0.6f + 0.55f) + Vector3.up * 0.1f, 0.12f, Color.white);
                }
                else if (show == Show.Scaffold) Arc(scaffold, R, 0f, a0, a1, violet);
                if (s.Built >= 2) Ball(nodes, Dir(k * step) * R, 0.13f, Color.white);
            }

            // Этап II: пилоны и внутреннее кольцо-зеркало
            var pylons = new MB();
            for (int k = 0; k < 4; k++)
            {
                float a = Mathf.PI * 0.25f + k * Mathf.PI * 0.5f;
                var show = Part(s, 1, 0.2f + k * 0.2f);
                Vector3 b = Dir(a) * (R + 0.1f), t = Dir(a) * (R + 1.9f) + Vector3.up * 0.5f;
                if (show == Show.Solid) Diamond(pylons, b, t, 0.28f, Color.white);
                else if (show == Show.Scaffold) scaffold.Seg(b, t, violet);
            }
            var mirror = new MB();
            if (Part(s, 1, 0.95f) == Show.Solid) Torus(mirror, R * 0.9f, 0.035f, 0f, Mathf.PI * 2f, 5);

            var crystalMat = Lit("gate_crystal", new Color(0.62f, 0.5f, 0.95f), new Color(0.32f, 0.16f, 0.6f), 0.15f, 0.95f);
            Emit(crystal, ringT, "Crystal", crystalMat);
            Emit(pylons, tilt, "Pylons", crystalMat);
            Emit(nodes, ringT, "Nodes", Fx("gate_node", violet * 2.4f, 0, 2f));
            Emit(mirror, tilt, "Mirror", Fx("gate_mirror", violet * 1.8f, 0, 1.5f));
            Emit(scaffold, tilt, "Scaffold", Scaffold(s, violet));

            // Этап III: открытый горизон — закрученная воронка
            if (s.Built >= 3)
            {
                var disc = new MB();
                Disc(disc, R * 0.9f, Color.white);
                var horizon = Emit(disc, tilt, "Horizon", Fx("gate_horizon", new Color(0.75f, 0.55f, 1f) * 1.8f, 1, 1f));
                if (horizon != null) spin.Add(horizon.transform, Vector3.up, 8f);
            }
        }

        // ==================== НАКОВАЛЬНЯ КСАРНА ====================

        private static void Forge(Spec s, Transform root, WonderSpin spin)
        {
            Color ember = new Color(1f, 0.42f, 0.16f);
            float[] radii = { 3.3f, 3.85f, 4.4f };
            Vector3[] tilts = { new Vector3(0f, 0f, 0f), new Vector3(64f, 0f, 0f), new Vector3(0f, 0f, 64f) };
            float[] speeds = { 9f, -7f, 5.5f };
            var steel = Lit("forge_steel", new Color(0.36f, 0.33f, 0.32f), new Color(0.08f, 0.04f, 0.02f), 0.8f, 0.55f);
            var seamMat = Fx("forge_seam", ember * 2.2f, 0, 1.6f);

            for (int k = 0; k < 3; k++)
            {
                var tilt = new GameObject("Gyro" + k).transform;
                tilt.SetParent(root, false);
                tilt.localRotation = Quaternion.Euler(tilts[k]);
                var ringT = new GameObject("Ring").transform;
                ringT.SetParent(tilt, false);
                spin.Add(ringT, Vector3.up, speeds[k]);

                var blocks = new MB();
                var seams = new MB();
                var scaffold = new MB { Lines = true };
                const int Blocks = 18;
                float step = Mathf.PI * 2f / Blocks;
                float R = radii[k];
                for (int b = 0; b < Blocks; b++)
                {
                    float a = (b + 0.5f) * step;
                    var show = Part(s, k, (b + 0.5f) / Blocks);
                    if (show == Show.Hidden) continue;
                    var q = Quaternion.LookRotation(Vector3.Cross(Vector3.up, Dir(a)), Vector3.up);
                    var size = new Vector3(0.42f, 0.34f, R * step * 0.84f);
                    if (show == Show.Solid)
                    {
                        Box(blocks, Dir(a) * R, q, size, Color.white);
                        Box(blocks, Dir(a) * (R + 0.24f), q, new Vector3(0.12f, 0.5f, R * step * 0.3f), Color.white);   // рёбра жёсткости
                        Torus(seams, R - 0.23f, 0.045f, b * step, (b + 1) * step, 4);
                    }
                    else BoxEdges(scaffold, Dir(a) * R, q, size, ember);
                }
                Emit(blocks, ringT, "Blocks", steel);
                Emit(seams, ringT, "Seams", seamMat);
                Emit(scaffold, ringT, "Scaffold", Scaffold(s, ember));
            }

            // Кузня-наковальня на внешней орбите (этап II) и потоки плазмы от звезды
            float fa = Mathf.PI / 6f;
            Vector3 fc = Dir(fa) * 6.6f;
            var forgeQ = Quaternion.LookRotation(Dir(fa), Vector3.up);
            var anvil = new MB();
            var anvilScaffold = new MB { Lines = true };
            (Vector3 off, Vector3 size, float order)[] parts =
            {
                (new Vector3(0f, -0.35f, 0f), new Vector3(1.7f, 0.6f, 1.2f), 0.2f),
                (new Vector3(0f, 0.15f, 0f), new Vector3(1.1f, 0.5f, 0.8f), 0.4f),
                (new Vector3(0f, 0.6f, 0.1f), new Vector3(2.6f, 0.45f, 1.3f), 0.6f),
            };
            foreach (var p in parts)
            {
                var show = Part(s, 1, p.order);
                if (show == Show.Solid) Box(anvil, fc + forgeQ * p.off, forgeQ, p.size, Color.white);
                else if (show == Show.Scaffold) BoxEdges(anvilScaffold, fc + forgeQ * p.off, forgeQ, p.size, ember);
            }
            var hornShow = Part(s, 1, 0.75f);
            if (hornShow == Show.Solid) Diamond(anvil, fc + forgeQ * new Vector3(-1.2f, 0.6f, 0.1f), fc + forgeQ * new Vector3(-2.6f, 0.65f, 0.1f), 0.26f, Color.white);
            Emit(anvil, root, "Anvil", steel);
            Emit(anvilScaffold, root, "AnvilScaffold", Scaffold(s, ember));

            var core = new MB();
            var beams = new MB();
            if (Part(s, 1, 0.9f) == Show.Solid)
            {
                Ball(core, fc + Vector3.up * 0.15f, 0.42f, Color.white);
                Beam(beams, Dir(fa) * 1.4f, fc, 0.3f, Color.white);
            }
            if (s.Built >= 3)
                for (int k = 0; k < 3; k++)
                {
                    float a = fa + Mathf.PI * 2f / 3f * (k + 0.5f);
                    Beam(beams, Dir(a) * 1.4f + Vector3.up * 0.2f, Dir(a) * radii[2] * 0.94f + Vector3.up * 0.2f, 0.22f, Color.white);
                }
            Emit(core, root, "MoltenCore", Fx("forge_core", new Color(1f, 0.55f, 0.2f) * 2.6f, 0, 2.5f));
            Emit(beams, root, "Plasma", Fx("forge_plasma", new Color(1f, 0.55f, 0.22f) * 2.4f, 2, 1.4f));
        }

        // ==================== ДРЕВО МИРОВ ====================

        private static void Tree(Spec s, Transform root, WonderSpin spin)
        {
            Color sap = new Color(0.45f, 1f, 0.45f);
            var bark = Lit("tree_bark", new Color(0.3f, 0.27f, 0.15f), new Color(0.05f, 0.12f, 0.04f), 0f, 0.3f);
            var wood = new MB();
            var veins = new MB { Lines = true };
            var growth = new MB { Lines = true };
            var pods = new MB();
            var leaves = new MB();

            const int Trunks = 3, Steps = 26;
            const float Outer = 9.4f, Inner = 2.5f;
            for (int k = 0; k < Trunks; k++)
            {
                float baseA = k * Mathf.PI * 2f / Trunks + 0.35f;
                var pts = new List<Vector3>();
                var rad = new List<float>();
                var stageOf = new List<int>();
                var orderOf = new List<float>();
                for (int i = 0; i <= Steps; i++)
                {
                    float t = i / (float)Steps;
                    float r = Mathf.Lerp(Outer, Inner, t);
                    float a = baseA + t * 1.15f;
                    float y = Mathf.Sin(t * Mathf.PI) * 1.1f * (k == 1 ? -1f : 1f) + Mathf.Sin(t * 9f + k) * 0.12f;
                    pts.Add(Dir(a) * r + Vector3.up * y);
                    rad.Add(Mathf.Lerp(0.6f, 0.2f, t) * (1f + 0.12f * Mathf.Sin(t * 17f + k)));
                    // Этап I — корни (внешняя часть), этап II — ствол к звезде
                    stageOf.Add(t < 0.45f ? 0 : 1);
                    orderOf.Add(t < 0.45f ? t / 0.45f : (t - 0.45f) / 0.55f);
                }

                // Готовая часть ствола — сплошной трубкой, недостроенная — пунктиром роста
                var solidPts = new List<Vector3>();
                var solidRad = new List<float>();
                for (int i = 0; i <= Steps; i++)
                {
                    var show = Part(s, stageOf[i], orderOf[i]);
                    if (show == Show.Solid) { solidPts.Add(pts[i]); solidRad.Add(rad[i]); }
                    else if (show == Show.Scaffold && i > 0) growth.Seg(pts[i - 1], pts[i], sap);
                }
                Tube(wood, solidPts, solidRad);
                for (int i = 1; i < solidPts.Count; i++)
                    veins.Seg(solidPts[i - 1] + Vector3.up * solidRad[i - 1] * 0.92f, solidPts[i] + Vector3.up * solidRad[i] * 0.92f, sap);

                // Корневые отростки у пояса (этап I)
                for (int b = 0; b < 3; b++)
                {
                    var show = Part(s, 0, 0.2f + b * 0.25f);
                    if (show == Show.Hidden) continue;
                    Vector3 o = pts[2 + b * 3];
                    float side = b % 2 == 0 ? 1f : -1f;
                    Vector3 tip = o + Dir(baseA + side * 0.35f) * 2.2f + Vector3.up * (side * 0.4f);
                    if (show == Show.Solid) Tube(wood, new[] { o, Vector3.Lerp(o, tip, 0.5f) + Vector3.up * 0.25f, tip }, new[] { 0.28f, 0.16f, 0.05f }, 5);
                    else growth.Seg(o, tip, sap);
                }

                // Этап III — крона: ветви обнимают звезду, на них светящиеся плоды и листья
                for (int b = 0; b < 2; b++)
                {
                    var show = Part(s, 2, (k * 2 + b + 0.5f) / (Trunks * 2f));
                    if (show == Show.Hidden) continue;
                    float a0 = baseA + 1.15f, dirSign = b == 0 ? 1f : -1f;
                    var bp = new List<Vector3>();
                    var br = new List<float>();
                    for (int i = 0; i <= 14; i++)
                    {
                        float t = i / 14f;
                        float a = a0 + dirSign * t * 1.9f;
                        float r = Mathf.Lerp(Inner, 2.15f, t) + Mathf.Sin(t * 6f) * 0.15f;
                        bp.Add(Dir(a) * r + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.4f * (b == 0 ? 1f : -1f)));
                        br.Add(Mathf.Lerp(0.2f, 0.05f, t));
                    }
                    if (show == Show.Solid)
                    {
                        Tube(wood, bp, br, 5);
                        for (int i = 3; i < bp.Count; i += 3) Ball(pods, bp[i] + Vector3.up * 0.18f, 0.16f, Color.white);
                        for (int i = 1; i < bp.Count; i++)
                        {
                            Vector3 leaf = bp[i] + new Vector3(Mathf.Sin(i * 2.3f + k), Mathf.Cos(i * 1.7f + b), Mathf.Sin(i * 3.1f)) * 0.35f;
                            Diamond(leaves, leaf, leaf + (leaf - bp[i]).normalized * 0.45f, 0.1f, Color.white);
                        }
                    }
                    else for (int i = 1; i < bp.Count; i++) growth.Seg(bp[i - 1], bp[i], sap);
                }
            }

            Emit(wood, root, "Wood", bark);
            Emit(veins, root, "Sap", Fx("tree_sap", sap * 1.4f, 0, 1.2f));
            Emit(growth, root, "Growth", Scaffold(s, sap));
            Emit(pods, root, "Pods", Fx("tree_pods", new Color(0.85f, 1f, 0.45f) * 2.2f, 0, 2f));
            var leafGo = Emit(leaves, root, "Leaves", Fx("tree_leaves", new Color(0.4f, 1f, 0.5f) * 1.2f, 0, 0.8f));
            if (leafGo != null) spin.Add(leafGo.transform, Vector3.up, 0.6f);
        }
    }

    /// <summary>Медленное вращение частей чуда (кольца, решётки, горизонт).</summary>
    public class WonderSpin : MonoBehaviour
    {
        private readonly List<(Transform t, Vector3 axis, float speed)> _items = new List<(Transform, Vector3, float)>();

        public void Add(Transform t, Vector3 axis, float degPerSec) => _items.Add((t, axis, degPerSec));

        private void Update()
        {
            float time = Time.unscaledTime;
            foreach (var it in _items)
                if (it.t != null) it.t.localRotation = Quaternion.AngleAxis(time * it.speed, it.axis);
        }

        /// <summary>Меши моделей создаются на лету — освобождаем их вместе с моделью (стройка пересобирает её часто).</summary>
        private void OnDestroy()
        {
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        }
    }
}
