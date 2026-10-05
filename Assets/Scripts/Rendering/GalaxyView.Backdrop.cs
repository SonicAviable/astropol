using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Фон галактической карты в духе Stellaris:
    ///   • скайбокс (яркая розовая туманность) на карте приглушён и охлаждён — он больше не спорит
    ///     со звёздами и границами; в режиме системы возвращается ярче, за планетами он красив;
    ///   • под картой — «диск галактики»: текстура, построенная по реальным позициям систем.
    ///     Рукава спирали, кольцо или эллипс светятся звёздной пылью сами собой, в центре — тёплое
    ///     ядро, вокруг систем — тысячи мелких неразрешённых звёзд.
    /// Текстура строится один раз (≈0,1–0,2 с) и детерминирована по расположению систем.
    /// </summary>
    public partial class GalaxyView
    {
        private const int DiskRes = 768;
        private const float DiskHalfExtent = 1.3f;      // половина стороны диска в радиусах галактики

        private GameObject _diskPlane;
        private Material _skyInstance;
        private float _skyExposure = 1f;
        private Color _skyTint = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        private void CreateBackdrop()
        {
            SetupSkybox();
            BuildGalaxyDisk();
            ApplySkyMood(false);
        }

        // ==================== СКАЙБОКС ====================

        private void SetupSkybox()
        {
            var src = RenderSettings.skybox;
            if (src == null) return;
            _skyInstance = new Material(src);            // правим копию — ассет на диске не трогаем
            if (_skyInstance.HasProperty("_Exposure")) _skyExposure = _skyInstance.GetFloat("_Exposure");
            if (_skyInstance.HasProperty("_Tint")) _skyTint = _skyInstance.GetColor("_Tint");
            RenderSettings.skybox = _skyInstance;
        }

        /// <summary>Галактика — тёмный холодный фон; система — исходная туманность чуть мягче.</summary>
        private void ApplySkyMood(bool systemView)
        {
            if (_skyInstance == null) return;
            if (_skyInstance.HasProperty("_Exposure"))
                _skyInstance.SetFloat("_Exposure", _skyExposure * (systemView ? 0.85f : 0.38f));
            if (_skyInstance.HasProperty("_Tint"))
            {
                var cold = new Color(0.36f, 0.38f, 0.48f, _skyTint.a);
                _skyInstance.SetColor("_Tint", systemView ? _skyTint : Color.Lerp(_skyTint, cold, 0.7f));
            }
        }

        // ==================== ДИСК ГАЛАКТИКИ ====================

        private void BuildGalaxyDisk()
        {
            if (_generator == null || _generator.Systems.Count == 0) return;

            float R = Mathf.Max(40f, _generator.GalaxyRadius);
            // Реальный радиус по системам (на случай загруженной партии другого размера)
            foreach (var s in _generator.Systems) R = Mathf.Max(R, new Vector2(s.Position.x, s.Position.z).magnitude * 0.95f);
            float half = R * DiskHalfExtent;
            int N = DiskRes;
            float tpu = N / (2f * half);                        // текселей на единицу мира

            // Сид по расположению систем — одна и та же карта всегда даёт один и тот же диск
            int seed = 17;
            foreach (var s in _generator.Systems) seed = seed * 31 + Mathf.RoundToInt(s.Position.x * 7f + s.Position.z * 13f);
            var rng = new System.Random(seed);
            float nox = (float)rng.NextDouble() * 500f, noy = (float)rng.NextDouble() * 500f;

            // 1. Плотность: гауссовы пятна вокруг систем
            var dens = new float[N * N];
            float sigma = 0.10f * R * tpu;
            int rad = Mathf.CeilToInt(sigma * 3f);
            float inv2s2 = 1f / (2f * sigma * sigma);
            foreach (var s in _generator.Systems)
            {
                float cx = (s.Position.x + half) * tpu, cz = (s.Position.z + half) * tpu;
                int x0 = Mathf.Max(0, (int)cx - rad), x1 = Mathf.Min(N - 1, (int)cx + rad);
                int z0 = Mathf.Max(0, (int)cz - rad), z1 = Mathf.Min(N - 1, (int)cz + rad);
                for (int j = z0; j <= z1; j++)
                {
                    float dz = j - cz;
                    for (int i = x0; i <= x1; i++)
                    {
                        float dx = i - cx;
                        dens[j * N + i] += Mathf.Exp(-(dx * dx + dz * dz) * inv2s2);
                    }
                }
            }
            float max = 0.0001f;
            for (int k = 0; k < dens.Length; k++) if (dens[k] > max) max = dens[k];

            // 2. Цвет: холодная пыль рукавов × облачный шум + тёплое ядро
            var px = new Color32[N * N];
            var cool = new Color(0.50f, 0.58f, 0.95f);
            var warm = new Color(1.00f, 0.82f, 0.55f);
            float noiseScale = 1f / (N / 640f * 28f);
            for (int j = 0; j < N; j++)
            {
                for (int i = 0; i < N; i++)
                {
                    float wx = i / tpu - half, wz = j / tpu - half;
                    float r = Mathf.Sqrt(wx * wx + wz * wz) / R;
                    float D = Mathf.Min(1f, dens[j * N + i] / max * 1.6f);
                    float n = Fbm(i * noiseScale + nox, j * noiseScale + noy);
                    float dust = D * (0.35f + 0.65f * n);
                    float core = Mathf.Exp(-r * r / (2f * 0.06f * 0.06f)) + Mathf.Exp(-r * r / (2f * 0.18f * 0.18f)) * 0.35f;
                    float t = Mathf.Exp(-r * r / (2f * 0.3f * 0.3f));
                    Color dc = Color.Lerp(cool, warm, t);

                    float dA = dust * 0.38f, cA = core * 0.9f;
                    float a = Mathf.Clamp01(dA + cA);
                    Color c = a > 0.0001f ? (dc * dA + warm * cA) / a : Color.black;
                    // Мягкое затухание к краю текстуры — никаких видимых границ квада
                    float edge = 1f - Mathf.SmoothStep(0.85f, 1f, Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz)) / half);
                    a *= edge;
                    px[j * N + i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), a);
                }
            }

            // 3. Неразрешённые звёзды вокруг систем (~30 на систему)
            int dots = _generator.Systems.Count * 30;
            for (int d = 0; d < dots; d++)
            {
                var s = _generator.Systems[rng.Next(_generator.Systems.Count)];
                float x = s.Position.x + Gauss(rng) * 0.11f * R;
                float z = s.Position.z + Gauss(rng) * 0.11f * R;
                int i = (int)((x + half) * tpu), j = (int)((z + half) * tpu);
                if (i < 1 || j < 1 || i >= N - 1 || j >= N - 1) continue;
                float b = 0.25f + (float)rng.NextDouble() * 0.55f;
                AddDot(px, N, i, j, b);
            }

            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                name = "GalaxyDisk"
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);

            _diskPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _diskPlane.name = "GalaxyDisk";
            var col = _diskPlane.GetComponent<Collider>();
            if (col != null) Destroy(col);
            _diskPlane.transform.SetParent(transform, false);
            _diskPlane.transform.position = new Vector3(0f, -0.7f, 0f);
            _diskPlane.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _diskPlane.transform.localScale = new Vector3(half * 2f, half * 2f, 1f);

            var mat = new Material(GetSpriteShader()) { mainTexture = tex };
            mat.renderQueue = 2960;          // под территориями (2980), коридорами и звёздами (3000)
            var mr = _diskPlane.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = -20;
        }

        private static void AddDot(Color32[] px, int n, int i, int j, float b)
        {
            void Add(int x, int y, float k)
            {
                int idx = y * n + x;
                var c = px[idx];
                byte v(byte ch, float tint) => (byte)Mathf.Min(255, ch + 255f * b * k * tint);
                px[idx] = new Color32(v(c.r, 0.85f), v(c.g, 0.9f), v(c.b, 1f), (byte)Mathf.Min(255, c.a + 255f * b * k));
            }
            Add(i, j, 1f);
            Add(i + 1, j, 0.25f); Add(i - 1, j, 0.25f); Add(i, j + 1, 0.25f); Add(i, j - 1, 0.25f);
        }

        private static float Fbm(float x, float y)
        {
            float s = 0f, a = 0.5f, f = 1f;
            for (int o = 0; o < 4; o++) { s += Mathf.PerlinNoise(x * f, y * f) * a; a *= 0.5f; f *= 2.03f; }
            return s / 0.9375f;
        }

        private static float Gauss(System.Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }
    }
}
