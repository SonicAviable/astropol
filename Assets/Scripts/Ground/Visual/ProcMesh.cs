using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Процедурные меши боя (кэш на бой):
    ///   • Box — параллелепипед со скруглёнными рёбрами заданного размера (фаска в метрах, не растягивается масштабом):
    ///     на рёбрах ловятся блики, модели перестают выглядеть «кубиками»;
    ///   • Rock — камень из деформированной икосферы с плоскими гранями;
    ///   • FlatQuad — квадрат 1×1, лежащий на земле (кольца отрядов, пятна).
    /// </summary>
    public static class ProcMesh
    {
        private static readonly Dictionary<long, Mesh> s_boxes = new Dictionary<long, Mesh>();
        private static readonly List<Mesh> s_rocks = new List<Mesh>();
        private static Mesh s_quad;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_boxes.Clear(); s_rocks.Clear(); s_quad = null; }

        private static long Key(Vector3 s, float b)
        {
            long q(float v) => (long)Mathf.Clamp(Mathf.RoundToInt(v * 100f), 0, 4095);
            return q(s.x) | (q(s.y) << 12) | (q(s.z) << 24) | (q(b) << 36);
        }

        /// <summary>Параллелепипед size с центром в нуле и скруглением рёбер bevel (м).</summary>
        public static Mesh Box(Vector3 size, float bevel = 0.04f)
        {
            float minHalf = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f;
            bevel = Mathf.Clamp(bevel, 0.002f, minHalf * 0.45f);
            long k = Key(size, bevel);
            if (s_boxes.TryGetValue(k, out var m) && m != null) return m;

            Vector3 h = size * 0.5f;
            Vector3 inner = h - Vector3.one * bevel;
            var verts = new List<Vector3>(96);
            var norms = new List<Vector3>(96);
            var uvs = new List<Vector2>(96);
            var tris = new List<int>(108 * 3);
            // Шесть граней, на каждой сетка 4×4: края сетки загибаются на фаску
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3[] u = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
            Vector3[] v = { Vector3.up, Vector3.up, Vector3.forward, Vector3.back, Vector3.up, Vector3.up };
            for (int f = 0; f < 6; f++)
            {
                int start = verts.Count;
                float hu = Mathf.Abs(Vector3.Dot(h, u[f])), hv = Mathf.Abs(Vector3.Dot(h, v[f])), hn = Mathf.Abs(Vector3.Dot(h, n[f]));
                float[] gu = { -hu, -hu + bevel, hu - bevel, hu };
                float[] gv = { -hv, -hv + bevel, hv - bevel, hv };
                for (int j = 0; j < 4; j++)
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 p = n[f] * hn + u[f] * gu[i] + v[f] * gv[j];
                        var c = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 d = p - c;
                        Vector3 nn = d.sqrMagnitude > 1e-8f ? d.normalized : n[f];
                        verts.Add(c + nn * bevel);
                        norms.Add(nn);
                        uvs.Add(new Vector2((gu[i] + hu) / Mathf.Max(0.01f, 2f * hu), (gv[j] + hv) / Mathf.Max(0.01f, 2f * hv)));
                    }
                for (int j = 0; j < 3; j++)
                    for (int i = 0; i < 3; i++)
                    {
                        int a = start + j * 4 + i;
                        // Лицом наружу (в Unity лицевая сторона — по часовой стрелке): для всех шести граней здесь u × v = −n
                        if (Vector3.Dot(Vector3.Cross(u[f], v[f]), n[f]) < 0f)
                        { tris.Add(a); tris.Add(a + 4); tris.Add(a + 1); tris.Add(a + 1); tris.Add(a + 4); tris.Add(a + 5); }
                        else
                        { tris.Add(a); tris.Add(a + 1); tris.Add(a + 4); tris.Add(a + 1); tris.Add(a + 5); tris.Add(a + 4); }
                    }
            }
            m = new Mesh { name = "BevelBox", hideFlags = HideFlags.DontSave };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            s_boxes[k] = m;
            return m;
        }

        /// <summary>Камень: икосфера, сдвинутая шумом, с плоскими гранями (вариантов 8, выбор по seed).</summary>
        public static Mesh Rock(int seed)
        {
            if (s_rocks.Count == 0)
                for (int i = 0; i < 8; i++) s_rocks.Add(BuildRock(i * 97 + 13));
            return s_rocks[Mathf.Abs(seed) % s_rocks.Count];
        }

        private static Mesh BuildRock(int seed)
        {
            // Икосаэдр → 2 подразделения
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var pts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < pts.Count; i++) pts[i] = pts[i].normalized;
            var faces = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            for (int s = 0; s < 2; s++)
            {
                var cache = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int idx)) return idx;
                    pts.Add(((pts[a] + pts[b]) * 0.5f).normalized);
                    cache[key] = pts.Count - 1;
                    return pts.Count - 1;
                }
                var nf = new List<int>();
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = nf;
            }
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 50f, oz = (float)rng.NextDouble() * 50f;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                float n = Mathf.PerlinNoise(p.x * 1.6f + ox, p.z * 1.6f + p.y * 0.7f + oz);
                float n2 = Mathf.PerlinNoise(p.x * 4f + oz, p.y * 4f + ox) * 0.25f;
                float r = 0.78f + n * 0.4f + n2;
                // Сплющить снизу, срезать вершину — камни лежат, а не висят шарами
                var q = p * r;
                q.y = q.y > 0f ? q.y * 0.72f : q.y * 0.45f;
                pts[i] = q;
            }
            // Плоские грани: каждая вершина своя
            var verts = new Vector3[faces.Count];
            var tris = new int[faces.Count];
            for (int i = 0; i < faces.Count; i++) { verts[i] = pts[faces[i]]; tris[i] = i; }
            var m = new Mesh { name = "Rock", hideFlags = HideFlags.DontSave };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Квадрат 1×1 на земле (нормаль вверх).</summary>
        public static Mesh FlatQuad
        {
            get
            {
                if (s_quad != null) return s_quad;
                s_quad = new Mesh { name = "FlatQuad", hideFlags = HideFlags.DontSave };
                s_quad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
                s_quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                s_quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                s_quad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                s_quad.RecalculateBounds();
                return s_quad;
            }
        }

        public static void Release()
        {
            foreach (var m in s_boxes.Values) if (m != null) Object.Destroy(m);
            foreach (var m in s_rocks) if (m != null) Object.Destroy(m);
            if (s_quad != null) Object.Destroy(s_quad);
            ResetStatics();
        }
    }
}
