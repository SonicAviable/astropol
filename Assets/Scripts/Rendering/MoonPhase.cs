using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Луна для диска даты (вариант A «Полная луна»): шар с текстурой Planets/Real/moon_albedo, фаза 0 — новолуние,
    /// 0.5 — полнолуние, 1 — снова новолуние (растущая освещена справа); мягкий терминатор, затемнение к краю,
    /// тёмная сторона едва видна. Нет текстуры — процедурные «моря» и кратеры. Спрайты по шагам фазы кэшируются.
    /// </summary>
    public static class MoonPhase
    {
        public const int Steps = 96;
        private const int N = 160;
        private const int TW = 512, TH = 256;                // копия текстуры для чтения на CPU
        private static Color32[] s_tex;
        private static bool s_texTried;
        private static readonly Sprite[] s_cache = new Sprite[Steps];
        private static float[] s_albedo;

        /// <summary>Спрайт шага фазы (0..Steps-1, по кругу) — для плавного перехода между соседними шагами.</summary>
        public static Sprite GetStep(int i)
        {
            i = ((i % Steps) + Steps) % Steps;
            return s_cache[i] != null ? s_cache[i] : (s_cache[i] = Make(i / (float)Steps));
        }

        public static Sprite Get(float phase)
        {
            phase -= Mathf.Floor(phase);
            int i = Mathf.RoundToInt(phase * Steps) % Steps;
            if (s_cache[i] != null) return s_cache[i];
            return s_cache[i] = Make(i / (float)Steps);
        }

        /// <summary>Доля освещённого диска 0..1 (для яркости ореола).</summary>
        public static float Illumination(float phase) => (1f - Mathf.Cos(phase * Mathf.PI * 2f)) * 0.5f;

        // Геометрия и цвета шара не зависят от фазы — считаются один раз; на фазу остаётся одно скалярное
        // произведение на пиксель (раньше каждый новый шаг фазы стоил несколько мс и давал рывок кадра)
        private static float[] s_u, s_v, s_z, s_edge;
        private static Color[] s_lit, s_dark;

        private static void EnsureLut()
        {
            if (s_u != null) return;
            if (s_albedo == null) s_albedo = BuildAlbedo();
            LoadTexture();
            int n = N * N;
            s_u = new float[n]; s_v = new float[n]; s_z = new float[n]; s_edge = new float[n];
            s_lit = new Color[n]; s_dark = new Color[n];
            float r = N * 0.5f - 1f;
            Color lit = new Color(1.08f, 1.08f, 1.06f), ash = new Color(0.025f, 0.045f, 0.06f);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                float u = (x + 0.5f - N * 0.5f) / r, v = (y + 0.5f - N * 0.5f) / r;
                float d2 = u * u + v * v;
                s_edge[i] = Mathf.Clamp01((1f - Mathf.Sqrt(d2)) * r + 0.5f);   // сглаженный край диска
                if (s_edge[i] <= 0f) continue;
                float z = Mathf.Sqrt(Mathf.Max(0f, 1f - d2));
                s_u[i] = u; s_v[i] = v; s_z[i] = z;
                Color alb = Albedo(u, v, z, x, y);
                float limb = 0.45f + 0.55f * Mathf.Pow(z, 0.35f);
                s_lit[i] = lit * limb * alb;
                s_dark[i] = ash + alb * 0.06f;
            }
        }

        private static Sprite Make(float phase)
        {
            EnsureLut();
            float th = phase * Mathf.PI * 2f;
            Vector3 L = new Vector3(Mathf.Sin(th), 0.12f * Mathf.Sin(th), -Mathf.Cos(th)).normalized;
            var px = new Color32[N * N];
            for (int i = 0; i < px.Length; i++)
            {
                float e = s_edge[i];
                if (e <= 0f) continue;
                float ndl = s_u[i] * L.x + s_v[i] * L.y + s_z[i] * L.z;
                float t = Mathf.Clamp01((ndl + 0.06f) / 0.16f);
                float day = t * t * (3f - 2f * t);
                Color d = s_dark[i], l = s_lit[i];
                px[i] = new Color(d.r + (l.r - d.r) * day, d.g + (l.g - d.g) * day, d.b + (l.b - d.b) * day, e);
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Moon", hideFlags = HideFlags.DontSave };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sp = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sp.hideFlags = HideFlags.DontSave;
            return sp;
        }

        /// <summary>Цвет поверхности в точке шара: из текстуры (долгота/широта) или процедурный.</summary>
        private static Color Albedo(float u, float v, float z, int x, int y)
        {
            if (s_tex == null) { float k = s_albedo[y * N + x]; return new Color(k * 0.85f, k * 0.86f, k * 0.86f); }
            float lon = Mathf.Atan2(u, z) + 0.2f, lat = Mathf.Asin(Mathf.Clamp(v, -1f, 1f));
            float fx = lon / (Mathf.PI * 2f); fx -= Mathf.Floor(fx);
            int tx = Mathf.Clamp((int)(fx * TW), 0, TW - 1), ty = Mathf.Clamp((int)((0.5f + lat / Mathf.PI) * TH), 0, TH - 1);
            return s_tex[ty * TW + tx];
        }

        /// <summary>Текстура в Resources может быть нечитаемой — копируем через RenderTexture один раз.</summary>
        private static void LoadTexture()
        {
            if (s_texTried) return;
            s_texTried = true;
            var src = Resources.Load<Texture2D>("Planets/Real/moon_albedo");
            if (src == null) return;
            var rt = RenderTexture.GetTemporary(TW, TH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tmp = new Texture2D(TW, TH, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, TW, TH), 0, 0);
            tmp.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            s_tex = tmp.GetPixels32();
            Object.Destroy(tmp);
        }

        // Поверхность (запасная, без текстуры): светлый фон, тёмные «моря» (мягкие пятна) и кратеры с ободком; одна на все фазы
        private static float[] BuildAlbedo()
        {
            var a = new float[N * N];
            float r = N * 0.5f - 1f;
            Vector3[] maria = { new Vector3(-0.25f, 0.30f, 0.32f), new Vector3(0.18f, 0.12f, 0.26f), new Vector3(-0.05f, -0.28f, 0.22f), new Vector3(0.38f, -0.18f, 0.16f), new Vector3(-0.45f, -0.05f, 0.14f) };
            Vector3[] craters = { new Vector3(0.10f, -0.55f, 0.09f), new Vector3(-0.55f, 0.42f, 0.07f), new Vector3(0.52f, 0.40f, 0.06f), new Vector3(-0.30f, -0.60f, 0.05f), new Vector3(0.62f, -0.05f, 0.05f), new Vector3(0.0f, 0.60f, 0.045f) };
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f - N * 0.5f) / r, v = (y + 0.5f - N * 0.5f) / r;
                float k = 0.92f;
                foreach (var m in maria)
                {
                    float dx = u - m.x, dy = v - m.y;
                    k -= 0.22f * Mathf.Exp(-(dx * dx + dy * dy) / (m.z * m.z));
                }
                foreach (var c in craters)
                {
                    float d = Mathf.Sqrt((u - c.x) * (u - c.x) + (v - c.y) * (v - c.y)) / c.z;
                    if (d < 1f) k -= 0.14f * (1f - d * d);                      // чаша
                    else if (d < 1.35f) k += 0.07f * (1f - (d - 1f) / 0.35f);   // светлый ободок
                }
                // мелкая «пыль» — детерминированный шум
                float h = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.547f;
                k += (h - Mathf.Floor(h) - 0.5f) * 0.05f;
                a[y * N + x] = Mathf.Clamp(k, 0.35f, 1f);
            }
            return a;
        }
    }
}
