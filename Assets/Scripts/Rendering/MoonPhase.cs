using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Процедурная луна для диска даты: фаза 0 — новолуние, 0.5 — полнолуние, 1 — снова новолуние
    /// (растущая освещена справа). Сфера с мягким терминатором, затемнением к краю, «морями» и кратерами;
    /// тёмная сторона чуть видна пепельным светом. Спрайты по шагам фазы рисуются по запросу и кэшируются.
    /// </summary>
    public static class MoonPhase
    {
        private const int N = 96, Steps = 96;
        private static readonly Sprite[] s_cache = new Sprite[Steps];
        private static float[] s_albedo;

        public static Sprite Get(float phase)
        {
            phase -= Mathf.Floor(phase);
            int i = Mathf.RoundToInt(phase * Steps) % Steps;
            if (s_cache[i] != null) return s_cache[i];
            return s_cache[i] = Make(i / (float)Steps);
        }

        /// <summary>Доля освещённого диска 0..1 (для яркости ореола).</summary>
        public static float Illumination(float phase) => (1f - Mathf.Cos(phase * Mathf.PI * 2f)) * 0.5f;

        private static Sprite Make(float phase)
        {
            if (s_albedo == null) s_albedo = BuildAlbedo();
            float th = phase * Mathf.PI * 2f;
            Vector3 L = new Vector3(Mathf.Sin(th), 0.12f * Mathf.Sin(th), -Mathf.Cos(th)).normalized;
            var px = new Color32[N * N];
            float r = N * 0.5f - 1f;
            Color lit = new Color(0.93f, 0.96f, 0.97f), ash = new Color(0.16f, 0.30f, 0.33f);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f - N * 0.5f) / r, v = (y + 0.5f - N * 0.5f) / r;
                float d2 = u * u + v * v;
                float edge = Mathf.Clamp01((1f - Mathf.Sqrt(d2)) * r + 0.5f);   // сглаженный край диска
                if (edge <= 0f) { px[y * N + x] = new Color32(0, 0, 0, 0); continue; }
                float z = Mathf.Sqrt(Mathf.Max(0f, 1f - d2));
                float ndl = u * L.x + v * L.y + z * L.z;
                float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.06f, 0.10f, ndl));
                float alb = s_albedo[y * N + x];
                float limb = 0.55f + 0.45f * Mathf.Sqrt(z);
                Color c = Color.Lerp(ash * (0.6f + 0.4f * alb), lit * alb * limb, day);
                float a = edge * Mathf.Lerp(0.55f, 1f, day);
                px[y * N + x] = new Color(c.r, c.g, c.b, a);
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Moon", hideFlags = HideFlags.DontSave };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sp = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sp.hideFlags = HideFlags.DontSave;
            return sp;
        }

        // Поверхность: светлый фон, тёмные «моря» (мягкие пятна) и кратеры с ободком; одна на все фазы
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
