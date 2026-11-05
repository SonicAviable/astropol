using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Процедурные спрайты голо-HUD (вариант «Голо-диск»): кольца с радиальной заливкой, шкалы-деления,
    /// шестиугольники меню, скошенные «крылья» ресурсов, диск времени. Белые — цвет задаёт Image.color.
    /// Рисуются один раз со сглаживанием (суперсэмплинг 4×) и кэшируются.
    /// </summary>
    public static class HudArt
    {
        private static readonly Dictionary<string, Sprite> s_cache = new Dictionary<string, Sprite>();

        private delegate float Coverage(float x, float y);   // x, y — пиксели от левого нижнего угла

        private static Sprite Make(string key, int w, int h, Coverage cov, Vector4 border = default)
        {
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "Hud_" + key, hideFlags = HideFlags.DontSave };
            var px = new Color32[w * h];
            const int ss = 4;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = 0f;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                    a += Mathf.Clamp01(cov(x + (sx + 0.5f) / ss, y + (sy + 0.5f) / ss));
                a /= ss * ss;
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sp.hideFlags = HideFlags.DontSave;
            s_cache[key] = sp;
            return sp;
        }

        /// <summary>Кольцо: inner/outer — доли радиуса (0..1). Для прогресса — Image.Type.Filled, Radial360.</summary>
        public static Sprite Ring(float inner, float outer)
        {
            const int n = 256;
            float r = n * 0.5f;
            return Make($"ring_{inner:0.000}_{outer:0.000}", n, n, (x, y) =>
            {
                float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
                return d >= inner && d <= outer ? 1f : 0f;
            });
        }

        /// <summary>Диск: центр чуть светлее края (фон диска времени), мягкий край.</summary>
        public static Sprite Disc()
        {
            const int n = 256;
            float r = n * 0.5f;
            return Make("disc", n, n, (x, y) =>
            {
                float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
                return d <= 0.985f ? 1f : 0f;
            });
        }

        /// <summary>Шкала-деления по окружности: count делений, каждое majorEvery-е — длиннее.</summary>
        public static Sprite Ticks(int count, int majorEvery, float inner, float majorInner, float outer)
        {
            const int n = 256;
            float r = n * 0.5f;
            float halfW = 0.55f;   // полуширина деления, пиксели
            return Make($"ticks_{count}_{majorEvery}_{inner:0.00}_{majorInner:0.00}_{outer:0.00}", n, n, (x, y) =>
            {
                float dx = x - r, dy = y - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > outer * r || d < majorInner * r) return 0f;
                float a = Mathf.Atan2(dy, dx);
                float step = Mathf.PI * 2f / count;
                int k = Mathf.RoundToInt(a / step);
                float off = Mathf.Abs(a - k * step) * d;           // расстояние до ближайшего деления по дуге
                bool major = majorEvery > 0 && ((k % majorEvery) + majorEvery) % majorEvery == 0;
                if (!major && d < inner * r) return 0f;
                return off <= (major ? halfW * 1.6f : halfW) ? 1f : 0f;
            });
        }

        /// <summary>
        /// Шестиугольник с острыми углами слева и справа (как соты меню). thickness = 0 — заливка,
        /// иначе обводка такой толщины (в пикселях спрайта 108×96).
        /// </summary>
        public static Sprite Hex(float thickness = 0f)
        {
            const int w = 108, h = 96;
            var pts = new[] { new Vector2(1, 48), new Vector2(27.5f, 95), new Vector2(80.5f, 95), new Vector2(107, 48), new Vector2(80.5f, 1), new Vector2(27.5f, 1) };
            return Make($"hex_{thickness:0.0}", w, h, (x, y) =>
            {
                float d = InsideDistance(pts, x, y);
                if (d < 0f) return 0f;
                return thickness <= 0f || d <= thickness ? 1f : 0f;
            });
        }

        /// <summary>
        /// «Крыло» панели ресурсов: прямоугольник со скошенным внешним краем (снизу уже, чем сверху).
        /// slantLeft — скос слева (левое крыло). 9-slice: края со скосом не растягиваются.
        /// </summary>
        public static Sprite Wing(bool slantLeft)
        {
            const int w = 96, h = 64, slant = 26;
            return Make($"wing_{(slantLeft ? "l" : "r")}", w, h, (x, y) =>
            {
                // y = 0 — низ; скос: сверху на всю ширину, снизу отступ slant
                float t = y / h;                    // 0 низ … 1 верх
                float cut = slant * (1f - t);
                if (slantLeft) return x >= cut ? 1f : 0f;
                return x <= w - cut ? 1f : 0f;
            }, slantLeft ? new Vector4(slant + 4, 0, 4, 0) : new Vector4(4, 0, slant + 4, 0));
        }

        /// <summary>Тонкая светлая кромка по верху крыла (тот же скос), рисуется поверх Wing.</summary>
        public static Sprite WingRim(bool slantLeft)
        {
            const int w = 96, h = 64, slant = 26;
            return Make($"wingrim_{(slantLeft ? "l" : "r")}", w, h, (x, y) =>
            {
                float t = y / h;
                float cut = slant * (1f - t);
                bool inside = slantLeft ? x >= cut : x <= w - cut;
                if (!inside) return 0f;
                if (y >= h - 1.5f) return 1f;                                   // верхняя кромка
                float edge = slantLeft ? x - cut : (w - cut) - x;               // расстояние до скоса по горизонтали
                return edge <= 1.2f ? 0.55f : 0f;                               // скошенный край — тусклее
            }, slantLeft ? new Vector4(slant + 4, 0, 4, 2) : new Vector4(4, 0, slant + 4, 2));
        }

        /// <summary>Плашка со срезанными углами (левый нижний и правый верхний) — подписи, тултипы, флажки.</summary>
        public static Sprite CutPanel(int cut = 10)
        {
            int n = cut * 2 + 8;
            return Make($"cut_{cut}", n, n, (x, y) =>
            {
                if (x + y < cut) return 0f;                       // левый нижний
                if ((n - x) + (n - y) < cut) return 0f;           // правый верхний
                return 1f;
            }, new Vector4(cut + 2, cut + 2, cut + 2, cut + 2));
        }

        /// <summary>Горизонтальная полоса, гаснущая к обоим краям (фон и линии баннера, бегущей строки).</summary>
        public static Sprite EdgeFade(float edge = 0.18f)
        {
            const int w = 256, h = 4;
            return Make($"efade_{edge:0.00}", w, h, (x, y) =>
            {
                float u = x / w;
                float k = Mathf.Min(u, 1f - u) / Mathf.Max(0.001f, edge);
                return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));
            });
        }

        /// <summary>Расстояние от точки до границы выпуклого многоугольника (по часовой), &lt; 0 — снаружи.</summary>
        private static float InsideDistance(Vector2[] pts, float x, float y)
        {
            float min = float.MaxValue;
            var p = new Vector2(x, y);
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length];
                var e = (b - a).normalized;
                var nrm = new Vector2(e.y, -e.x);                 // внутренняя нормаль для обхода по часовой
                float d = Vector2.Dot(p - a, nrm);
                if (d < min) min = d;
            }
            return min;
        }
    }
}
