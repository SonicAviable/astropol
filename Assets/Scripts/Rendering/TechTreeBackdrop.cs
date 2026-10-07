using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Процедурный фон окна исследований:
    /// две бесшовные туманности, медленно плывущие навстречу друг другу, мягкое свечение сверху,
    /// тонкая «чертёжная» сетка, два слоя мерцающих звёзд и виньетка. Цвет плавно меняется
    /// под ветку: все — бирюза, физика — синий, общество — зелёный, инженерия — янтарь.
    /// Текстуры строятся один раз (десятки миллисекунд) и общие для всех открытий окна.
    /// </summary>
    public class TechTreeBackdrop : MonoBehaviour
    {
        private RawImage _nebulaA, _nebulaB, _glow, _grid, _starsNear, _starsFar, _vignette;
        private RectTransform _rect;
        private Color _colA, _colB, _targetA, _targetB;
        private float _t;
        private bool _active;

        private static Texture2D s_nebulaA, s_nebulaB, s_glow, s_grid, s_stars, s_vignette;

        public static (Color a, Color b) BranchColors(string key) => key switch
        {
            "physics" => (new Color(0.30f, 0.55f, 1.00f), new Color(0.58f, 0.38f, 1.00f)),
            "society" => (new Color(0.30f, 0.92f, 0.56f), new Color(0.20f, 0.72f, 0.78f)),
            "engineering" => (new Color(1.00f, 0.62f, 0.26f), new Color(1.00f, 0.36f, 0.34f)),
            _ => (new Color(0.26f, 0.88f, 0.84f), new Color(0.32f, 0.42f, 1.00f))
        };

        public void Initialize(RectTransform zone)
        {
            EnsureTextures();
            _rect = (RectTransform)transform;

            _nebulaA = Layer("NebulaA", s_nebulaA, true);
            _nebulaB = Layer("NebulaB", s_nebulaB, true);
            _glow = Layer("Glow", s_glow, false);
            _glow.rectTransform.anchorMin = new Vector2(0.15f, 0.35f);
            _glow.rectTransform.anchorMax = new Vector2(0.85f, 1.25f);
            _grid = Layer("Grid", s_grid, true);
            _starsFar = Layer("StarsFar", s_stars, true);
            _starsNear = Layer("StarsNear", s_stars, true);
            _vignette = Layer("Vignette", s_vignette, false);
            _vignette.color = new Color(0f, 0f, 0f, 0.95f);

            (_colA, _colB) = BranchColors("all");
            _targetA = _colA; _targetB = _colB;
            Apply();
        }

        private RawImage Layer(string name, Texture2D tex, bool tiled)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            if (!tiled) img.uvRect = new Rect(0, 0, 1, 1);
            return img;
        }

        public void SetBranch(string key) => (_targetA, _targetB) = BranchColors(key);

        /// <summary>Произвольная пара цветов (например, цвета империи в окне дипломатии).</summary>
        public void SetColors(Color a, Color b) { _targetA = a; _targetB = b; }

        public void SetActive(bool on) => _active = on;

        private void Update()
        {
            if (!_active || _rect == null) return;
            float dt = Time.unscaledDeltaTime;
            _t += dt;
            float k = 1f - Mathf.Exp(-dt * 3f);
            _colA = Color.Lerp(_colA, _targetA, k);
            _colB = Color.Lerp(_colB, _targetB, k);
            Apply();
        }

        private void Apply()
        {
            var size = _rect.rect.size;
            float aspect = size.y > 1f ? size.x / size.y : 1.75f;

            _nebulaA.uvRect = new Rect(_t * 0.006f, _t * 0.0015f, 1f, 1f / aspect * 2f);
            _nebulaB.uvRect = new Rect(-_t * 0.004f + 0.37f, 0.21f - _t * 0.001f, 1.55f, 1.55f / aspect * 2f);
            _nebulaA.color = new Color(_colA.r, _colA.g, _colA.b, 0.16f);
            _nebulaB.color = new Color(_colB.r, _colB.g, _colB.b, 0.10f);

            float pulse = 0.85f + 0.15f * Mathf.Sin(_t * 0.6f);
            _glow.color = new Color(_colA.r, _colA.g, _colA.b, 0.07f * pulse);

            float cell = 72f;
            _grid.uvRect = new Rect(_t * 0.01f, _t * 0.004f, Mathf.Max(1f, size.x / cell), Mathf.Max(1f, size.y / cell));
            _grid.color = new Color(_colA.r, _colA.g, _colA.b, 0.035f);

            float tw = 0.75f + 0.25f * Mathf.Sin(_t * 1.7f);
            _starsFar.uvRect = new Rect(_t * 0.002f, 0f, Mathf.Max(1f, size.x / 512f), Mathf.Max(1f, size.y / 256f));
            _starsFar.color = new Color(0.8f, 0.88f, 1f, 0.14f);
            _starsNear.uvRect = new Rect(0.5f + _t * 0.005f, 0.3f, Mathf.Max(1f, size.x / 800f), Mathf.Max(1f, size.y / 400f));
            _starsNear.color = new Color(1f, 1f, 1f, 0.22f * tw);
        }

        // ==================== ТЕКСТУРЫ ====================

        private static void EnsureTextures()
        {
            if (s_nebulaA != null) return;
            s_nebulaA = Nebula(256, 128, 11.3f, 3.2f);
            s_nebulaB = Nebula(256, 128, 57.9f, 4.4f);
            s_glow = Radial(128);
            s_grid = Grid(64);
            s_stars = Stars(512, 256, 420, 1337);
            s_vignette = Vignette(128);
        }

        private static Texture2D NewTex(int w, int h, bool repeat)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
        }

        private static float Fbm(float x, float y)
        {
            float s = 0f, a = 0.5f, f = 1f;
            for (int o = 0; o < 5; o++) { s += Mathf.PerlinNoise(x * f + 100f, y * f + 100f) * a; a *= 0.5f; f *= 2.02f; }
            return s / 0.97f;
        }

        /// <summary>Бесшовная туманность: смешение четырёх сдвинутых копий шума по краям.</summary>
        private static Texture2D Nebula(int w, int h, float seed, float scale)
        {
            var tex = NewTex(w, h, true);
            var px = new Color32[w * h];
            float sx = scale / w, sy = scale * 0.5f / h;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fx = (float)x / w, fy = (float)y / h;
                float n00 = Fbm(x * sx + seed, y * sy + seed);
                float n10 = Fbm((x - w) * sx + seed, y * sy + seed);
                float n01 = Fbm(x * sx + seed, (y - h) * sy + seed);
                float n11 = Fbm((x - w) * sx + seed, (y - h) * sy + seed);
                float n = Mathf.Lerp(Mathf.Lerp(n00, n10, fx), Mathf.Lerp(n01, n11, fx), fy);
                float v = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.82f, n));
                v = Mathf.Pow(v, 1.4f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(v * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D Radial(int n)
        {
            var tex = NewTex(n, n, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Exp(-d * d * 3.2f) * Mathf.Clamp01(1f - d);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D Grid(int n)
        {
            var tex = NewTex(n, n, true);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool line = x == 0 || y == 0;
                bool tick = (x == n / 2 && y < 3) || (y == n / 2 && x < 3);
                byte a = line ? (byte)255 : tick ? (byte)150 : (byte)0;
                px[y * n + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D Stars(int w, int h, int count, int seed)
        {
            var tex = NewTex(w, h, true);
            var px = new Color32[w * h];
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                int cx = rng.Next(w), cy = rng.Next(h);
                float b = 0.35f + (float)(rng.NextDouble() * rng.NextDouble()) * 0.65f;
                bool big = rng.NextDouble() < 0.08;
                int r = big ? 2 : 1;
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = (cx + dx + w) % w, y = (cy + dy + h) % h;
                    float fall = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / (r + 0.5f));
                    byte a = (byte)Mathf.Min(255, px[y * w + x].a + fall * b * 255f);
                    px[y * w + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D Vignette(int n)
        {
            var tex = NewTex(n, n, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1.35f, d));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
