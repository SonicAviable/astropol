using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Процедурные спрайты карточного боя (без внешних ассетов): скруглённые плашки с кромкой (9-slice), мягкое свечение,
    /// кольцо ударной волны, искра, клуб дыма, капля, трещины, блик фольги, луч, рубашка карты.
    /// Белые — цвет задаёт Image.color. Создаются один раз за запуск.
    /// </summary>
    public static class CardSprites
    {
        private static readonly Dictionary<string, Sprite> s_cache = new Dictionary<string, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => s_cache.Clear();

        private static Sprite Make(string key, int w, int h, System.Func<float, float, float> alpha, Vector4 border = default, float ppu = 100f)
        {
            if (s_cache.TryGetValue(key, out var sp) && sp != null) return sp;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Card_" + key, hideFlags = HideFlags.DontSave };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h));
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect, border);
            sp.hideFlags = HideFlags.DontSave;
            s_cache[key] = sp;
            return sp;
        }

        private static float RoundRectDist(float px, float py, float w, float h, float r)
        {
            float qx = Mathf.Abs(px - w * 0.5f) - (w * 0.5f - r);
            float qy = Mathf.Abs(py - h * 0.5f) - (h * 0.5f - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>Скруглённая заливка 64×64, радиус 14, 9-slice.</summary>
        public static Sprite Round => Make("round", 64, 64, (u, v) => Mathf.Clamp01(0.5f - RoundRectDist(u * 64, v * 64, 64, 64, 14)), new Vector4(20, 20, 20, 20));

        /// <summary>Скруглённая кромка (только обвод 2 px), 9-slice.</summary>
        public static Sprite RoundEdge => Make("roundEdge", 64, 64, (u, v) =>
        {
            float d = RoundRectDist(u * 64, v * 64, 64, 64, 14);
            return Mathf.Clamp01(1.4f - Mathf.Abs(d + 1.5f));
        }, new Vector4(20, 20, 20, 20));

        /// <summary>Мягкое свечение вокруг скруглённой плашки (за картой), 9-slice.</summary>
        public static Sprite RoundGlow => Make("roundGlow", 96, 96, (u, v) =>
        {
            // Плашка с отступом 30 px: внутри — полная яркость, снаружи свет спадает к краю спрайта
            float d = RoundRectDist(u * 96 - 30, v * 96 - 30, 36, 36, 8);
            return d <= 0 ? 1f : Mathf.Pow(Mathf.Clamp01(1f - d / 30f), 2.2f);
        }, new Vector4(40, 40, 40, 40));

        public static Sprite Glow => Make("glow", 64, 64, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            return Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
        });

        public static Sprite Dot => Make("dot", 32, 32, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            return Mathf.Clamp01((1f - d) * 3f);
        });

        public static Sprite Ring => Make("ring", 128, 128, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            return Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.12f) * Mathf.Clamp01((1f - d) * 20f);
        });

        /// <summary>Вытянутая искра (по X).</summary>
        public static Sprite Spark => Make("spark", 64, 16, (u, v) =>
        {
            float dy = Mathf.Abs(v - 0.5f) * 2f;
            float dx = Mathf.Abs(u - 0.5f) * 2f;
            return Mathf.Clamp01(1f - dy) * Mathf.Pow(Mathf.Clamp01(1f - dx), 0.7f);
        });

        /// <summary>Клуб дыма: шумная капля.</summary>
        public static Sprite Smoke => Make("smoke", 64, 64, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float n = Mathf.PerlinNoise(u * 5.3f + 11f, v * 5.3f + 7f) * 0.6f + Mathf.PerlinNoise(u * 11f + 3f, v * 11f + 5f) * 0.4f;
            return Mathf.Clamp01((1f - d) * 1.6f) * Mathf.Clamp01(n * 1.5f - 0.15f);
        });

        public static Sprite Drop => Make("drop", 32, 32, (u, v) =>
        {
            float x = (u - 0.5f) * 2f, y = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(x * x + Mathf.Max(0, y) * Mathf.Max(0, y) * 0.35f + Mathf.Min(0, y) * Mathf.Min(0, y));
            return Mathf.Clamp01((0.9f - r) * 6f);
        });

        /// <summary>Трещины по карте (наложение при сильных повреждениях).</summary>
        public static Sprite Cracks => Make("cracks", 128, 176, (u, v) =>
        {
            float a = 0f;
            // Несколько ломаных из точки удара
            var center = new Vector2(0.62f, 0.45f);
            for (int k = 0; k < 7; k++)
            {
                float ang = k * 0.9f + 0.4f;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var p = new Vector2(u, v) - center;
                float along = Vector2.Dot(p, dir);
                if (along < 0f || along > 0.55f) continue;
                float wobble = Mathf.Sin(along * 31f + k) * 0.012f;
                float perp = Mathf.Abs(dir.x * p.y - dir.y * p.x + wobble);
                a = Mathf.Max(a, Mathf.Clamp01(1f - perp / (0.006f + 0.004f * (1f - along))) * (1f - along * 1.4f));
            }
            return a;
        });

        /// <summary>Наклонная полоса блика (для «фольги» и пробегающего света).</summary>
        public static Sprite Sheen => Make("sheen", 128, 32, (u, v) =>
        {
            float d = Mathf.Abs(u - 0.5f) * 2f;
            return Mathf.Pow(Mathf.Clamp01(1f - d), 2.5f);
        });

        /// <summary>Луч (вертикальный градиент к краям) — орбитальный удар, трассеры.</summary>
        public static Sprite Beam => Make("beam", 32, 64, (u, v) =>
        {
            float d = Mathf.Abs(u - 0.5f) * 2f;
            return Mathf.Pow(Mathf.Clamp01(1f - d), 1.5f);
        });

        /// <summary>Вертикальный градиент (затемнение снизу/сверху).</summary>
        public static Sprite Fade => Make("fade", 4, 64, (u, v) => v);

        /// <summary>Виньетка на весь экран.</summary>
        public static Sprite Vignette => Make("vignette", 128, 72, (u, v) =>
        {
            float x = (u - 0.5f) * 2f, y = (v - 0.5f) * 2f;
            return Mathf.Clamp01(Mathf.Pow(x * x * 0.55f + y * y * 0.8f, 1.4f));
        });

        /// <summary>Узор рубашки карты: шестиугольная сетка и концентрические кольца.</summary>
        public static Sprite BackPattern => Make("back", 128, 176, (u, v) =>
        {
            float x = u * 128f, y = v * 176f;
            float hex = Mathf.Abs(Mathf.Sin(x * 0.18f) + Mathf.Sin((x * 0.5f + y * 0.866f) * 0.18f) + Mathf.Sin((x * 0.5f - y * 0.866f) * 0.18f));
            float grid = Mathf.Clamp01(1f - hex * 2.2f) * 0.25f;
            float d = Vector2.Distance(new Vector2(u, v * 1.375f), new Vector2(0.5f, 0.6875f));
            float rings = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Repeat(d * 9f, 1f) - 0.5f) * 6f) * Mathf.Clamp01(1f - d * 1.6f) * 0.6f;
            return Mathf.Max(grid, rings);
        });

        /// <summary>Шестиугольник (значок стоимости).</summary>
        public static Sprite Hex => Make("hex", 64, 64, (u, v) =>
        {
            float x = Mathf.Abs(u - 0.5f) * 2f, y = Mathf.Abs(v - 0.5f) * 2f;
            float d = Mathf.Max(x * 0.866f + y * 0.5f, y);
            return Mathf.Clamp01((0.92f - d) * 30f);
        });

        /// <summary>Ромб (значок силы).</summary>
        public static Sprite Diamond => Make("diamond", 64, 64, (u, v) =>
        {
            float d = Mathf.Abs(u - 0.5f) * 2f + Mathf.Abs(v - 0.5f) * 2f;
            return Mathf.Clamp01((0.96f - d) * 30f);
        });

        /// <summary>Щит (значок брони).</summary>
        public static Sprite ShieldShape => Make("shield", 64, 64, (u, v) =>
        {
            float x = Mathf.Abs(u - 0.5f) * 2f, y = v;
            float top = y > 0.55f ? Mathf.Clamp01((0.95f - y) * 30f) * Mathf.Clamp01((0.92f - x) * 30f) : 0f;
            float bottom = y <= 0.55f ? Mathf.Clamp01((y - (0.05f + x * x * 0.9f * 0.55f + x * 0.45f)) * 30f) * Mathf.Clamp01((0.92f - x) * 30f) : 0f;
            return Mathf.Max(top, bottom);
        });

        /// <summary>Купол (полусфера щита Штаба).</summary>
        public static Sprite Dome => Make("dome", 128, 64, (u, v) =>
        {
            float x = (u - 0.5f) * 2f, y = v;
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > 1f) return 0f;
            float rim = Mathf.Clamp01(1f - (1f - r) / 0.08f);
            float hexes = Mathf.Abs(Mathf.Sin(u * 40f) * Mathf.Sin(v * 40f)) < 0.08f ? 0.25f : 0f;
            return Mathf.Max(0.12f + rim * 0.8f, hexes);
        });
    }
}
