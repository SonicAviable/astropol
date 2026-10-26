using UnityEngine;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Процедурные текстуры эффектов боя (кэш на бой): клубящийся дым, языки пламени, мягкая точка,
    /// кольцо отряда под ногами, пятно контактной тени. Всё строится кодом один раз и уничтожается в <see cref="Release"/>.
    /// </summary>
    public static class FxTextures
    {
        private static Texture2D s_smoke, s_flame, s_soft, s_ring, s_blob, s_streak;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_smoke = s_flame = s_soft = s_ring = s_blob = s_streak = null; }

        private static float Fbm(float x, float y, int oct)
        {
            float v = 0f, a = 0.5f, f = 1f;
            for (int i = 0; i < oct; i++) { v += Mathf.PerlinNoise(x * f + 31.7f * i, y * f + 17.3f * i) * a; a *= 0.5f; f *= 2.03f; }
            return v;
        }

        private static Texture2D New(string name, int n, bool mips = true)
            => new Texture2D(n, n, TextureFormat.RGBA32, mips) { name = name, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave, anisoLevel = 2 };

        /// <summary>Клуб дыма: рваный край, внутренние завихрения, светлее сверху (будто освещён солнцем).</summary>
        public static Texture2D Smoke
        {
            get
            {
                if (s_smoke != null) return s_smoke;
                const int N = 128;
                var t = New("FxSmoke", N);
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + 0.5f) / N - 0.5f, v = (y + 0.5f) / N - 0.5f;
                        float d = Mathf.Sqrt(u * u + v * v) * 2f;
                        float n = Fbm(u * 5f + 3f, v * 5f + 9f, 5);
                        float edge = Mathf.Clamp01((1f - d) * 1.6f + (n - 0.5f) * 1.3f);
                        float a = edge * edge * (0.55f + n * 0.6f);
                        float shade = Mathf.Clamp01(0.62f + v * 0.55f + (n - 0.5f) * 0.5f);
                        px[y * N + x] = new Color(shade, shade, shade, Mathf.Clamp01(a));
                    }
                t.SetPixels(px); t.Apply(true);
                return s_smoke = t;
            }
        }

        /// <summary>Язык пламени: горячее ядро снизу, рваные вытянутые вверх края.</summary>
        public static Texture2D Flame
        {
            get
            {
                if (s_flame != null) return s_flame;
                const int N = 64;
                var t = New("FxFlame", N);
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + 0.5f) / N - 0.5f, v = (y + 0.5f) / N;
                        float w = Mathf.Lerp(0.42f, 0.08f, v);
                        float n = Fbm(u * 6f + 2f, v * 3f, 4);
                        float a = Mathf.Clamp01(1f - Mathf.Abs(u) / Mathf.Max(0.01f, w * (0.7f + n * 0.8f))) * Mathf.Clamp01((1f - v) * 1.6f) * Mathf.Clamp01(v * 8f);
                        float hot = Mathf.Clamp01(1f - v * 1.4f);
                        px[y * N + x] = new Color(1f, Mathf.Lerp(0.55f, 1f, hot), Mathf.Lerp(0.25f, 0.85f, hot * hot), a * a);
                    }
                t.SetPixels(px); t.Apply(true);
                return s_flame = t;
            }
        }

        /// <summary>Мягкая точка (искры, вспышки).</summary>
        public static Texture2D Soft
        {
            get
            {
                if (s_soft != null) return s_soft;
                const int N = 64;
                var t = New("FxSoft", N, false);
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                        float a = Mathf.Clamp01(1f - d);
                        px[y * N + x] = new Color(1f, 1f, 1f, a * a * a);
                    }
                t.SetPixels(px); t.Apply();
                return s_soft = t;
            }
        }

        /// <summary>Кольцо под бойцом (как в Dawn of War): тонкий яркий обод и мягкая заливка внутри.</summary>
        public static Texture2D Ring
        {
            get
            {
                if (s_ring != null) return s_ring;
                const int N = 128;
                var t = New("FxRing", N);
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                        float rim = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.07f);
                        float fill = d < 0.86f ? 0.12f * d : 0f;
                        px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(rim * rim + fill));
                    }
                t.SetPixels(px); t.Apply(true);
                return s_ring = t;
            }
        }

        /// <summary>Контактная тень (тёмное пятно под моделью — «садит» её на землю).</summary>
        public static Texture2D Blob
        {
            get
            {
                if (s_blob != null) return s_blob;
                const int N = 64;
                var t = New("FxBlob", N);
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                        float a = Mathf.Clamp01(1f - d);
                        px[y * N + x] = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, a) * 0.85f);
                    }
                t.SetPixels(px); t.Apply(true);
                return s_blob = t;
            }
        }

        /// <summary>Вытянутая полоса (трассер, вспышка ствола сбоку).</summary>
        public static Texture2D Streak
        {
            get
            {
                if (s_streak != null) return s_streak;
                const int W = 64, H = 16;
                var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "FxStreak", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var px = new Color[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = (x + 0.5f) / W, v = Mathf.Abs((y + 0.5f) / H - 0.5f) * 2f;
                        float a = Mathf.Clamp01(1f - v) * Mathf.Clamp01(Mathf.Sin(u * Mathf.PI)) ;
                        px[y * W + x] = new Color(1f, 1f, 1f, a * a);
                    }
                t.SetPixels(px); t.Apply();
                return s_streak = t;
            }
        }

        public static void Release()
        {
            foreach (var t in new[] { s_smoke, s_flame, s_soft, s_ring, s_blob, s_streak }) if (t != null) Object.Destroy(t);
            ResetStatics();
        }
    }
}
