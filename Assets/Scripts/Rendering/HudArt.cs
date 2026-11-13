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

        /// <summary>Рамка плашки со срезанными углами (тот же силуэт, что CutPanel) толщиной thickness px.</summary>
        public static Sprite CutFrame(int cut = 10, float thickness = 1.5f)
        {
            int n = cut * 2 + 8;
            return Make($"cutframe_{cut}_{thickness:0.0}", n, n, (x, y) =>
            {
                bool Inside(float px, float py, float inset)
                {
                    if (px < inset || py < inset || px > n - inset || py > n - inset) return false;
                    float c = cut + inset * 0.4142f;                  // срез смещается на inset·(√2−1)
                    if (px + py < c) return false;
                    if ((n - px) + (n - py) < c) return false;
                    return true;
                }
                return Inside(x, y, 0f) && !Inside(x, y, thickness) ? 1f : 0f;
            }, new Vector4(cut + 2, cut + 2, cut + 2, cut + 2));
        }

        /// <summary>
        /// Объёмный шар (превью звезды/планеты): освещён слева сверху, с бликом и тёмным краем.
        /// Цвет — оттенки серого, Image.color окрашивает шар.
        /// </summary>
        public static Sprite Sphere()
        {
            const string key = "sphere";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "Hud_sphere", hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            var light = new Vector3(-0.45f, 0.55f, 0.70f).normalized;
            float r = n * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f - r) / r, v = (y + 0.5f - r) / r;
                float d2 = u * u + v * v;
                float a = Mathf.Clamp01((1f - Mathf.Sqrt(d2)) * r * 0.9f);        // сглаженный край
                if (a <= 0f) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }
                float z = Mathf.Sqrt(Mathf.Max(0f, 1f - d2));
                var nrm = new Vector3(u, v, z);
                float diff = Mathf.Max(0f, Vector3.Dot(nrm, light));
                float spec = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(Vector3.Reflect(-light, nrm), Vector3.forward)), 18f);
                float rim = Mathf.Pow(1f - z, 3f) * 0.35f;                          // свечение по краю
                float c = Mathf.Clamp01(0.18f + 0.82f * diff + rim) ;
                float sw = Mathf.Clamp01(spec * 0.9f);
                byte g = (byte)Mathf.RoundToInt(Mathf.Lerp(c, 1f, sw) * 255f);
                px[y * n + x] = new Color32(g, g, g, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            var sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            sp.hideFlags = HideFlags.DontSave;
            s_cache[key] = sp;
            return sp;
        }

        /// <summary>
        /// Звезда для превью: яркое ядро, корона с мягким спадом и лучи (крест и короткая диагональ).
        /// Белая — Image.color окрашивает её в цвет звезды; поверх кладётся маленькое белое ядро.
        /// </summary>
        public static Sprite Star()
        {
            const int n = 128;
            float r = n * 0.5f;
            return Make("star", n, n, (x, y) =>
            {
                float u = (x - r) / r, v = (y - r) / r;
                float d = Mathf.Sqrt(u * u + v * v);
                float core = Mathf.Clamp01((0.22f - d) / 0.05f);
                float corona = Mathf.Exp(-d * 5.5f) * 0.9f;
                float au = Mathf.Abs(u), av = Mathf.Abs(v);
                float spike = Mathf.Max(Mathf.Exp(-av * 90f) * Mathf.Clamp01(1f - au), Mathf.Exp(-au * 90f) * Mathf.Clamp01(1f - av)) * 0.85f;
                float du = Mathf.Abs(u - v) * 0.7071f, dv = Mathf.Abs(u + v) * 0.7071f;
                float diag = Mathf.Max(Mathf.Exp(-du * 110f), Mathf.Exp(-dv * 110f)) * Mathf.Clamp01(1f - d * 1.7f) * 0.45f;
                return Mathf.Clamp01(core + corona + spike + diag) * Mathf.Clamp01((1f - d) * 6f);
            });
        }

        /// <summary>Шкала-разделитель: повторяемая риска (Image.Type.Tiled), шаг 8 px.</summary>
        public static Sprite TickRow()
        {
            return Make("tickrow", 8, 6, (x, y) => x < 1f ? 1f : 0f);
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

        /// <summary>Вертикальный градиент: снизу непрозрачно, кверху гаснет (плавная, без ступенек). top — доля высоты, где альфа доходит до нуля.</summary>
        public static Sprite VFade(float top = 1f)
        {
            const int w = 4, h = 256;
            return Make($"vfade_{top:0.00}", w, h, (x, y) =>
            {
                float v = Mathf.Clamp01(y / (h * Mathf.Max(0.01f, top)));
                return 1f - Mathf.SmoothStep(0f, 1f, v);
            });
        }

        /// <summary>«Таблетка» (полностью скруглённый прямоугольник), девятисрезная: при высоте 0.56 ед. концы — полукруги.
        /// frame — только контур толщиной ~2.5 px.</summary>
        public static Sprite Pill(bool frame)
        {
            const int w = 112, h = 56, r = 28;
            const float t = 2.5f;
            return Make(frame ? "pill_frame" : "pill", w, h, (x, y) =>
            {
                float dx = Mathf.Max(Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r), 0f), dy = Mathf.Abs(y - h * 0.5f);
                float d = Mathf.Sqrt(dx * dx + dy * dy) - (r - 0.5f);
                float inside = Mathf.Clamp01(0.5f - d);
                return frame ? inside * Mathf.Clamp01(0.5f + d + t) : inside;
            }, new Vector4(r, r, r, r));
        }

        /// <summary>Ромб (квадрат на углу) во весь спрайт, слегка скруглённые углы; frame — только контур ~7 px из 128.</summary>
        public static Sprite Diamond(bool frame)
        {
            const int n = 128;
            float r = n * 0.5f - 1.5f;
            const float t = 7f;
            return Make(frame ? "diamond_frame" : "diamond", n, n, (x, y) =>
            {
                float d = (Mathf.Abs(x - n * 0.5f) + Mathf.Abs(y - n * 0.5f) - r) * 0.7071f;   // расстояние до края
                float inside = Mathf.Clamp01(0.5f - d);
                return frame ? inside * Mathf.Clamp01(0.5f + d + t) : inside;
            });
        }

        /// <summary>Тонкая черта-«линза»: толще в середине, сходит на нет к концам, края мягкие (черта под названием системы).</summary>
        public static Sprite TaperLine()
        {
            const int w = 256, h = 16;
            return Make("taperline", w, h, (x, y) =>
            {
                float u = x / w * 2f - 1f;
                float half = 0.42f * Mathf.Pow(Mathf.Clamp01(1f - u * u), 0.8f);   // полутолщина в долях высоты
                float d = Mathf.Abs(y / h - 0.5f);
                return Mathf.Clamp01((half - d) * h * 0.5f + 0.5f);
            });
        }

        /// <summary>Мягкое пятно-эллипс: гаснет и по горизонтали, и по вертикали (свечение без жёстких краёв).</summary>
        public static Sprite SoftGlow()
        {
            const int w = 128, h = 32;
            return Make("softglow", w, h, (x, y) =>
            {
                float u = x / w * 2f - 1f, v = y / h * 2f - 1f;
                float r2 = u * u * 0.9f + v * v;
                return Mathf.Clamp01(Mathf.Exp(-r2 * 3.2f) - 0.04f) / 0.96f;
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
