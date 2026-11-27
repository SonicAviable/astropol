using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Схема системы в шапке панели («Атлас», вариант «Схема»): орбиты тел — тонкие эллипсы, тела — точки своего цвета
    /// (колонии — в кольце морской зелени), граница системы — пунктир, выходы гиперкоридоров — золотые шевроны у края
    /// кадра в сторону соседних систем. Рисуется одним мешем, без текстур.
    /// </summary>
    public sealed class SystemSchemaGraphic : MaskableGraphic
    {
        public struct Body { public float Orbit, Angle, Radius; public Color Color; public bool Colony; }

        public Vector2 Center01 = new Vector2(0.66f, 0.58f);
        public float Flatten = 0.42f;
        public bool Unknown;
        public readonly List<float> Orbits = new List<float>();
        public readonly List<Body> Bodies = new List<Body>();
        public readonly List<float> Corridors = new List<float>();

        private static readonly Color Cream = new Color(0.925f, 0.898f, 0.827f);
        private static readonly Color Gold = new Color(0.906f, 0.757f, 0.439f);
        private static readonly Color Sea = new Color(0.475f, 0.816f, 0.749f);

        public Vector2 Center(Rect r) => new Vector2(r.xMin + r.width * Center01.x, r.yMin + r.height * Center01.y);
        public float OuterR(Rect r) => r.width * 0.5f;
        public Vector2 OnOrbit(Rect r, float rx, float ang) => Center(r) + new Vector2(Mathf.Cos(ang) * rx, Mathf.Sin(ang) * rx * Flatten);

        /// <summary>Точка выхода коридора: луч из звезды до края кадра (с полем), там стоит шеврон.</summary>
        public Vector2 CorridorPoint(Rect r, float ang, float inset = 14f)
        {
            var c = Center(r);
            var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float tx = d.x > 0.001f ? (r.xMax - inset - c.x) / d.x : d.x < -0.001f ? (r.xMin + inset - c.x) / d.x : float.MaxValue;
            float ty = d.y > 0.001f ? (r.yMax - inset - c.y) / d.y : d.y < -0.001f ? (r.yMin + inset - c.y) / d.y : float.MaxValue;
            return c + d * Mathf.Min(tx, ty);
        }

        public void Rebuild() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            var c = Center(r);
            float outer = OuterR(r);

            // Визир через звезду — едва заметные оси, как на штурманской карте
            Seg(vh, new Vector2(r.xMin + 10f, c.y), new Vector2(r.xMax - 10f, c.y), 1f, A(Cream, 0.05f));
            Seg(vh, new Vector2(c.x, r.yMin + 8f), new Vector2(c.x, r.yMax - 8f), 1f, A(Cream, 0.05f));

            // Орбиты: ближние ярче, дальние тише
            for (int i = 0; i < Orbits.Count; i++)
            {
                float t = Orbits.Count <= 1 ? 0f : i / (float)(Orbits.Count - 1);
                Ellipse(vh, c, Orbits[i], Orbits[i] * Flatten, 1f, A(Cream, Mathf.Lerp(0.24f, 0.12f, t)), false);
            }
            // Граница системы — пунктир
            Ellipse(vh, c, outer, outer * Flatten, 1f, A(Cream, Unknown ? 0.16f : 0.09f), true);

            // Выходы коридоров: пунктир от границы до шеврона, сам шеврон смотрит наружу
            foreach (float ang in Corridors)
            {
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var n = new Vector2(-d.y, d.x);
                var p = CorridorPoint(r, ang);
                var from = c + d * Mathf.Min(Vector2.Distance(c, p) - 10f, outer * 0.75f);
                Dashed(vh, from, p - d * 6f, 1f, A(Gold, 0.28f), 3f, 4f);
                Seg(vh, p - d * 3.5f + n * 5f, p + d * 2.5f, 1.4f, A(Gold, 0.9f));
                Seg(vh, p - d * 3.5f - n * 5f, p + d * 2.5f, 1.4f, A(Gold, 0.9f));
            }

            // Тела
            foreach (var b in Bodies)
            {
                var p = c + new Vector2(Mathf.Cos(b.Angle) * b.Orbit, Mathf.Sin(b.Angle) * b.Orbit * Flatten);
                Disc(vh, p, b.Radius + 2.5f, A(new Color(0.075f, 0.125f, 0.220f), 0.95f));   // тёмный зазор поверх орбиты
                Disc(vh, p, b.Radius, b.Color);
                if (b.Colony) Ring(vh, p, b.Radius + 3.5f, 1.2f, A(Sea, 0.9f));
            }
        }

        private static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static void Seg(VertexHelper vh, Vector2 a, Vector2 b, float w, Color col)
        {
            var d = b - a;
            if (d.sqrMagnitude < 0.0001f) return;
            var n = new Vector2(-d.y, d.x).normalized * (w * 0.5f);
            int i = vh.currentVertCount;
            vh.AddVert(a - n, col, Vector2.zero);
            vh.AddVert(a + n, col, Vector2.zero);
            vh.AddVert(b + n, col, Vector2.zero);
            vh.AddVert(b - n, col, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        private static void Dashed(VertexHelper vh, Vector2 a, Vector2 b, float w, Color col, float dash, float gap)
        {
            float len = Vector2.Distance(a, b);
            if (len < 1f) return;
            var d = (b - a) / len;
            for (float s = 0f; s < len; s += dash + gap)
                Seg(vh, a + d * s, a + d * Mathf.Min(len, s + dash), w, col);
        }

        private static void Ellipse(VertexHelper vh, Vector2 c, float rx, float ry, float w, Color col, bool dashed)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(rx * 0.6f), 48, 144);
            for (int i = 0; i < n; i++)
            {
                if (dashed && (i % 3) == 2) continue;
                float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                Seg(vh, c + new Vector2(Mathf.Cos(a0) * rx, Mathf.Sin(a0) * ry), c + new Vector2(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry), w, col);
            }
        }

        private static void Disc(VertexHelper vh, Vector2 c, float rad, Color col)
        {
            const int n = 20;
            int i0 = vh.currentVertCount;
            vh.AddVert(c, col, Vector2.zero);
            for (int i = 0; i <= n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad, col, Vector2.zero);
            }
            for (int i = 0; i < n; i++) vh.AddTriangle(i0, i0 + 1 + i, i0 + 2 + i);
        }

        private static void Ring(VertexHelper vh, Vector2 c, float rad, float w, Color col)
        {
            const int n = 28;
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                Seg(vh, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * rad, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * rad, w, col);
            }
        }
    }
}
