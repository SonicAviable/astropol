using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Простой линейный график для интерфейса: несколько рядов значений, сетка из горизонтальных линий.
    /// Рисуется одним мешем (полосы-отрезки), без текстур. Подписи осей — обычные Text рядом (их ставит окно).
    /// Длинные ряды прореживаются до ширины графика.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class LineChart : MaskableGraphic
    {
        public sealed class Series
        {
            public IList<int> Values;
            public Color Color;
            public float Width = 2f;
        }

        public readonly List<Series> Data = new List<Series>();
        public float MaxY = 1f;
        public int GridLines = 4;
        public Color GridColor = new Color(0.55f, 0.75f, 0.85f, 0.12f);

        public void Set(List<Series> series, float maxY)
        {
            Data.Clear();
            Data.AddRange(series);
            MaxY = Mathf.Max(1f, maxY);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            if (r.width < 2f || r.height < 2f) return;

            for (int g = 0; g <= GridLines; g++)
            {
                float y = r.yMin + r.height * g / GridLines;
                Quad(vh, new Vector2(r.xMin, y), new Vector2(r.xMax, y), 1f, GridColor);
            }

            foreach (var s in Data)
            {
                if (s.Values == null || s.Values.Count == 0) continue;
                int n = s.Values.Count;
                int step = Mathf.Max(1, Mathf.CeilToInt(n / Mathf.Max(8f, r.width / 3f)));
                Vector2 prev = Point(r, 0, n, s.Values[0]);
                if (n == 1) { Quad(vh, prev, prev + new Vector2(4f, 0f), s.Width, s.Color); continue; }
                for (int i = step; i < n + step; i += step)
                {
                    int k = Mathf.Min(i, n - 1);
                    var p = Point(r, k, n, s.Values[k]);
                    Quad(vh, prev, p, s.Width, s.Color);
                    prev = p;
                    if (k == n - 1) break;
                }
            }
        }

        private Vector2 Point(Rect r, int i, int n, int v)
        {
            float x = n <= 1 ? r.xMin : r.xMin + r.width * i / (n - 1f);
            float y = r.yMin + r.height * Mathf.Clamp01(v / MaxY);
            return new Vector2(x, y);
        }

        /// <summary>Отрезок толщиной w (с небольшим нахлёстом на стыках).</summary>
        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, float w, Color c)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.0001f) d = Vector2.right;
            d.Normalize();
            Vector2 n = new Vector2(-d.y, d.x) * (w * 0.5f);
            Vector2 ext = d * (w * 0.35f);
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = c;
            v.position = a - ext - n; vh.AddVert(v);
            v.position = a - ext + n; vh.AddVert(v);
            v.position = b + ext + n; vh.AddVert(v);
            v.position = b + ext - n; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }
}
