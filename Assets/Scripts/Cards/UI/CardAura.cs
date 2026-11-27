using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Живое свечение вокруг карты (как у карт в Hearthstone): мягкий ореол по скруглённому контуру, край «дышит»
    /// языками света, по контуру бегут два огонька с хвостами, от них отлетают искры-угольки.
    /// Один меш, аддитивно (Shaders/UIAdditive): светится поверх стола, середину закрывает сама карта.
    /// Меш пересобирается каждый кадр, только пока свечение видно.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CardAura : MaskableGraphic
    {
        /// <summary>Цвет свечения (золото — наведение, зелень — можно сыграть).</summary>
        public Color Tint = new Color(1f, 0.85f, 0.4f);
        /// <summary>0..1 — яркость ореола.</summary>
        public float Level;
        /// <summary>0..1 — сила бегущих огоньков и искр.</summary>
        public float Comets;
        /// <summary>Ширина ореола наружу от края карты, px.</summary>
        public float Width = 8f;
        /// <summary>Скругление контура (как у рамки карты), px.</summary>
        public float Corner = 12f;
        /// <summary>Насколько контур заходит под край карты, px.</summary>
        public float Inset = 3f;
        /// <summary>Оборотов огонька в секунду.</summary>
        public float Speed = 0.28f;
        /// <summary>Искр в секунду от бликов (0 — без искр: чистый контур).</summary>
        public float SparkRate = 0f;

        private const int Segs = 128;
        private const float Tau = Mathf.PI * 2f;
        private static Texture2D s_tex;
        private static Material s_mat;

        private struct Spark { public Vector2 P, V; public float Life, Max, Size; }
        private readonly List<Spark> _sparks = new List<Spark>(64);
        private float _t, _emit;
        private bool _drawn;

        public override Texture mainTexture => Tex;

        /// <summary>Мягкая точка: яркость = альфа (аддитивный шейдер умножает цвет на альфу вершины, альфу текстуры не смотрит).</summary>
        private static Texture2D Tex
        {
            get
            {
                if (s_tex != null) return s_tex;
                const int n = 64;
                s_tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "CardAuraDot", hideFlags = HideFlags.DontSave };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2((x + 0.5f) / n, (y + 0.5f) / n), new Vector2(0.5f, 0.5f)) * 2f;
                        byte a = (byte)Mathf.RoundToInt(Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f) * 255f);
                        px[y * n + x] = new Color32(a, a, a, a);
                    }
                s_tex.SetPixels32(px);
                s_tex.Apply(false);
                return s_tex;
            }
        }

        private static Material Mat
        {
            get
            {
                if (s_mat == null)
                {
                    var sh = Resources.Load<Shader>("Shaders/UIAdditive");
                    if (sh != null) s_mat = new Material(sh) { name = "CardAura", hideFlags = HideFlags.DontSave };
                }
                return s_mat;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            var m = Mat;
            if (m != null) material = m;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool visible = Level > 0.004f || Comets > 0.004f || _sparks.Count > 0;
            if (!visible)
            {
                if (_drawn) { _drawn = false; SetVerticesDirty(); }
                return;
            }
            _t += dt * Speed * (0.55f + 0.9f * Comets);

            // Искры-угольки срываются с голов огоньков и улетают наружу, слегка всплывая
            if (Comets > 0.05f && isActiveAndEnabled)
            {
                _emit += dt * SparkRate * Comets;
                while (_emit >= 1f) { _emit -= 1f; Emit(); }
            }
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i];
                s.Life -= dt;
                if (s.Life <= 0f) { _sparks.RemoveAt(i); continue; }
                s.P += s.V * dt;
                s.V *= 1f - dt * 1.6f;
                s.V.y += 26f * dt;
                _sparks[i] = s;
            }
            _drawn = true;
            SetVerticesDirty();
        }

        private void Emit()
        {
            if (_sparks.Count >= 60) return;
            var r = Contour();
            float head = Mathf.Repeat(_t + (Random.value < 0.5f ? 0f : 0.5f), 1f);
            Perim(r, head - Random.value * 0.015f, out var p, out var n);
            var tan = new Vector2(n.y, -n.x);
            float max = Random.Range(0.45f, 0.9f);
            _sparks.Add(new Spark
            {
                P = p + n * (Inset + Random.Range(0f, 4f)),
                V = n * Random.Range(20f, 60f) + tan * Random.Range(-25f, 15f),
                Max = max,
                Life = max,
                Size = Random.Range(2f, 4f) * Mathf.Clamp(Width / 10f, 0.6f, 1.4f),
            });
        }

        private Rect Contour()
        {
            var r = rectTransform.rect;
            return new Rect(r.x + Inset, r.y + Inset, Mathf.Max(1f, r.width - 2f * Inset), Mathf.Max(1f, r.height - 2f * Inset));
        }

        /// <summary>Точка и внешняя нормаль на скруглённом прямоугольнике; s — доля периметра по часовой от верхнего левого угла.</summary>
        private void Perim(Rect r, float s, out Vector2 p, out Vector2 n)
        {
            float c = Mathf.Min(Corner, Mathf.Min(r.width, r.height) * 0.5f - 0.5f);
            float w = r.width - 2f * c, h = r.height - 2f * c, arc = Mathf.PI * 0.5f * c;
            float d = Mathf.Repeat(s, 1f) * (2f * w + 2f * h + 4f * arc);
            float x0 = r.xMin + c, x1 = r.xMax - c, y0 = r.yMin + c, y1 = r.yMax - c;
            if (d < w) { p = new Vector2(x0 + d, r.yMax); n = Vector2.up; return; }
            d -= w;
            if (d < arc) { float a = Mathf.PI * 0.5f - d / c; n = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); p = new Vector2(x1, y1) + n * c; return; }
            d -= arc;
            if (d < h) { p = new Vector2(r.xMax, y1 - d); n = Vector2.right; return; }
            d -= h;
            if (d < arc) { float a = -d / c; n = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); p = new Vector2(x1, y0) + n * c; return; }
            d -= arc;
            if (d < w) { p = new Vector2(x1 - d, r.yMin); n = Vector2.down; return; }
            d -= w;
            if (d < arc) { float a = -Mathf.PI * 0.5f - d / c; n = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); p = new Vector2(x0, y0) + n * c; return; }
            d -= arc;
            if (d < h) { p = new Vector2(r.xMin, y0 + d); n = Vector2.left; return; }
            d -= h;
            float b = Mathf.PI - d / c;
            n = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
            p = new Vector2(x0, y1) + n * c;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!_drawn) return;
            var r = Contour();
            var uv = new Vector2(0.5f, 0.5f);
            var hot = Color.Lerp(Tint, Color.white, 0.65f);

            // Контур как у карт в дорогих карточных играх: чёткая светящаяся линия по самому краю карты,
            // за ней — узкое мягкое свечение в несколько пикселей; по линии пробегает короткий яркий блик.
            // Ширина не «дышит» (иначе край выглядит рыхлым пятном) — живёт только яркость.
            float px = Mathf.Max(0.6f, Width / 8f);   // пиксель в масштабе карты
            for (int i = 0; i <= Segs; i++)
            {
                float s = i / (float)Segs;
                Perim(r, s, out var p, out var n);
                float shimmer = 0.9f + 0.1f * Mathf.Sin(s * Tau * 5f + _t * Tau * 2.2f);
                float comet = 0f;
                for (int k = 0; k < 2; k++)
                {
                    float behind = Mathf.Repeat(_t + k * 0.5f - s, 1f);   // расстояние позади головы блика
                    comet += Mathf.Exp(-behind * 30f) + 0.25f * Mathf.Exp(-behind * 8f);
                }
                comet *= Comets;
                float b = Level * shimmer + comet;
                var col = Color.Lerp(Tint, hot, Mathf.Clamp01(0.25f + comet * 0.9f));
                var edge = p + n * Inset;
                var c0 = col; c0.a = Mathf.Clamp01(b * 0.9f);
                var c1 = col; c1.a = Mathf.Clamp01(b);
                var c2 = Tint; c2.a = Mathf.Clamp01(b * 0.35f);
                var c3 = Tint; c3.a = 0f;
                vh.AddVert(p - n * 1f, c0, uv);
                vh.AddVert(edge + n * (1.3f * px), c1, uv);
                vh.AddVert(edge + n * (3f * px), c2, uv);
                vh.AddVert(edge + n * Width, c3, uv);
                if (i > 0)
                {
                    int a0 = (i - 1) * 4, a1 = i * 4;
                    for (int q = 0; q < 3; q++)
                    {
                        vh.AddTriangle(a0 + q, a1 + q, a0 + q + 1);
                        vh.AddTriangle(a1 + q, a1 + q + 1, a0 + q + 1);
                    }
                }
            }

            // Искры
            foreach (var sp in _sparks)
            {
                float k = sp.Life / sp.Max;
                var c = Color.Lerp(Tint, Color.white, 0.5f * k);
                c.a = Mathf.Clamp01(k * 1.3f) * Mathf.Max(0.3f, Comets);
                float h = sp.Size * (0.5f + 0.5f * k);
                int v = vh.currentVertCount;
                vh.AddVert(sp.P + new Vector2(-h, -h), c, new Vector2(0, 0));
                vh.AddVert(sp.P + new Vector2(-h, h), c, new Vector2(0, 1));
                vh.AddVert(sp.P + new Vector2(h, h), c, new Vector2(1, 1));
                vh.AddVert(sp.P + new Vector2(h, -h), c, new Vector2(1, 0));
                vh.AddTriangle(v, v + 1, v + 2); vh.AddTriangle(v, v + 2, v + 3);
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _sparks.Clear();
        }
    }
}
