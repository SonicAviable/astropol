using System.Collections.Generic;
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
    ///   • диск живой (шейдер GalaxyDisk): пыль медленно закручивается, ядро дышит светом;
    ///     неразрешённые звёзды спокойно мерцают (TwinklePoints);
    ///   • под диском — цветные туманности регионов (GalaxyNebula): облака медленно текут.
    /// Текстура строится один раз (≈0,1–0,2 с) и детерминирована по расположению систем.
    /// </summary>
    public partial class GalaxyView
    {
        private const int DiskRes = 768;
        private const float DiskHalfExtent = 1.3f;      // половина стороны диска в радиусах галактики

        private GameObject _diskPlane;
        private GameObject _dustStars;
        private Material _diskMat, _dustStarsMat;
        private GameObject _nebulaRoot;
        private GameObject _coreRoot;
        private Material _coreMat;
        private SpriteRenderer _coreBulge;
        private float _coreBulgeBase = 1f;
        private readonly List<(Material mat, float baseIntensity, int layer)> _nebulaMats = new List<(Material, float, int)>();
        private readonly List<Material> _darkDustMats = new List<Material>();
        private readonly List<float> _darkDustBase = new List<float>();
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

            var diskShader = Resources.Load<Shader>("Shaders/GalaxyDisk");
            var mat = new Material(diskShader != null ? diskShader : GetSpriteShader()) { mainTexture = tex };
            if (diskShader != null) mat.SetFloat("_RadiusUv", R / (2f * half));
            mat.renderQueue = 2960;          // под территориями (2980), коридорами и звёздами (3000)
            _diskMat = mat;
            var mr = _diskPlane.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = -20;

            BuildDustStars(R, rng);
            BuildNebulae(R, rng);
            BuildGalaxyCore(R);
            BuildAsteroidFields(R, rng);
        }

        // ==================== ЯДРО ГАЛАКТИКИ ====================

        /// <summary>
        /// Ядро: плоский квад с шейдером GalaxyCore (раскалённый центр, балдж, закрученные рукава с
        /// пылевыми прожилками) + объёмное свечение-билборд, чтобы ядро читалось шаром при наклоне камеры.
        /// </summary>
        private void BuildGalaxyCore(float R)
        {
            var shader = Resources.Load<Shader>("Shaders/GalaxyCore");
            if (shader == null) return;
            _coreRoot = new GameObject("GalaxyCore");
            _coreRoot.transform.SetParent(transform, false);

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "CoreDisk";
            var col = q.GetComponent<Collider>();
            if (col != null) Destroy(col);
            q.transform.SetParent(_coreRoot.transform, false);
            q.transform.position = new Vector3(0f, -0.5f, 0f);
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = new Vector3(R * 1.2f, R * 1.2f, 1f);
            _coreMat = new Material(shader);
            _coreMat.renderQueue = 2962;
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _coreMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var b = new GameObject("CoreBulge");
            b.transform.SetParent(_coreRoot.transform, false);
            b.transform.position = new Vector3(0f, 0.5f, 0f);
            _coreBulgeBase = R * 0.16f / 0.64f;
            b.transform.localScale = Vector3.one * _coreBulgeBase;
            _coreBulge = b.AddComponent<SpriteRenderer>();
            _coreBulge.sprite = _starHotSprite;
            _coreBulge.sharedMaterial = new Material(GetSpriteShader());
            _coreBulge.sortingOrder = 5;
            b.AddComponent<BillboardLookAt>();
        }

        // ==================== ТУМАННОСТИ РЕГИОНОВ ====================

        private static readonly Color[] NebulaPalette =
        {
            new Color(0.15f, 0.75f, 0.80f),   // бирюза
            new Color(0.70f, 0.25f, 0.85f),   // фиолет
            new Color(1.00f, 0.50f, 0.22f),   // янтарь
            new Color(0.25f, 0.40f, 1.00f),   // синий
            new Color(0.95f, 0.25f, 0.40f),   // малиновый
            new Color(0.35f, 0.90f, 0.55f)    // зелёный
        };

        /// <summary>
        /// Несколько больших цветных облаков под диском: у каждого региона галактики свой оттенок.
        /// Расположение и цвета детерминированы картой.
        /// </summary>
        private void BuildNebulae(float R, System.Random rng)
        {
            var shader = Resources.Load<Shader>("Shaders/GalaxyNebula");
            if (shader == null) return;
            _nebulaRoot = new GameObject("GalaxyNebulae");
            _nebulaRoot.transform.SetParent(transform, false);
            int pal = rng.Next(NebulaPalette.Length);

            // Слои по высоте — при движении камеры проплывают с разной скоростью (параллакс):
            //   0 — глубокие (далеко под картой, крупные, тусклые)
            //   1 — средние (под диском)
            //   2 — клочья над картой (ближе к камере, мелкие, тонкие; вблизи растворяются)
            void Layer(int layer, int count, float yMin, float yMax, float sMin, float sMax, float dMin, float dMax, float iMin, float iMax)
            {
                for (int k = 0; k < count; k++)
                {
                    float ang = (k + (float)rng.NextDouble() * 0.7f) / count * Mathf.PI * 2f + layer * 0.9f;
                    float dist = R * (dMin + (float)rng.NextDouble() * (dMax - dMin));
                    float y = yMin + (float)rng.NextDouble() * (yMax - yMin);
                    float size = R * (sMin + (float)rng.NextDouble() * (sMax - sMin));
                    var q = Quad("Nebula_" + layer + "_" + k, _nebulaRoot.transform,
                        new Vector3(Mathf.Cos(ang) * dist, y, Mathf.Sin(ang) * dist),
                        (float)rng.NextDouble() * 360f, new Vector2(size * (1f + (float)rng.NextDouble() * 0.6f), size));

                    var m = new Material(shader);
                    int ci = pal + k + layer * 2;
                    m.SetColor("_ColorA", NebulaPalette[ci % NebulaPalette.Length]);
                    m.SetColor("_ColorB", NebulaPalette[(ci + 1 + rng.Next(2)) % NebulaPalette.Length]);
                    m.SetFloat("_Seed", (float)rng.NextDouble() * 100f);
                    m.SetFloat("_Scale", (layer == 2 ? 3.6f : 2.4f) + (float)rng.NextDouble() * 1.4f);
                    m.renderQueue = layer == 0 ? 2940 : layer == 1 ? 2950 : 2996;
                    _nebulaMats.Add((m, iMin + (float)rng.NextDouble() * (iMax - iMin), layer));
                    q.GetComponent<MeshRenderer>().sharedMaterial = m;
                }
            }

            Layer(0, 4, -R * 0.32f, -R * 0.2f, 0.55f, 0.8f, 0.5f, 1.0f, 0.09f, 0.14f);
            Layer(1, 5, -6f, -1.5f, 0.3f, 0.5f, 0.6f, 1.05f, 0.14f, 0.24f);
            Layer(2, 5, R * 0.05f, R * 0.11f, 0.16f, 0.3f, 0.3f, 1.0f, 0.08f, 0.14f);

            BuildDarkDust(R, rng);
        }

        /// <summary>Тёмные пылевые облака над диском и ядром, вытянуты вдоль рукавов (по касательной).</summary>
        private void BuildDarkDust(float R, System.Random rng)
        {
            var shader = Resources.Load<Shader>("Shaders/GalaxyDarkDust");
            if (shader == null) return;
            int count = 5;
            for (int k = 0; k < count; k++)
            {
                float ang = (k + (float)rng.NextDouble() * 0.8f) / count * Mathf.PI * 2f;
                float dist = R * (0.38f + (float)rng.NextDouble() * 0.45f);   // не вокруг ядра — иначе читается тёмным кольцом
                float len = R * (0.28f + (float)rng.NextDouble() * 0.25f);
                // длинная ось — по касательной к окружности (вдоль рукава), с небольшим разбросом
                float yaw = -ang * Mathf.Rad2Deg + 90f + ((float)rng.NextDouble() - 0.5f) * 40f;
                var q = Quad("DarkDust_" + k, _nebulaRoot.transform,
                    new Vector3(Mathf.Cos(ang) * dist, -0.25f + k * 0.02f, Mathf.Sin(ang) * dist), yaw, new Vector2(len, len * 0.32f));
                var m = new Material(shader);
                m.SetFloat("_Seed", (float)rng.NextDouble() * 100f);
                m.SetFloat("_Opacity", 0.22f + (float)rng.NextDouble() * 0.12f);
                m.SetColor("_Tint", Color.Lerp(new Color(0.05f, 0.03f, 0.035f), new Color(0.02f, 0.025f, 0.05f), (float)rng.NextDouble()));
                m.renderQueue = 2963;    // над диском (2960) и ядром (2962), под коридорами и звёздами
                _darkDustMats.Add(m);
                _darkDustBase.Add(m.GetFloat("_Opacity"));
                q.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
        }

        private static GameObject Quad(string name, Transform parent, Vector3 pos, float yaw, Vector2 size)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            var col = q.GetComponent<Collider>();
            if (col != null) Destroy(col);
            q.transform.SetParent(parent, false);
            q.transform.position = pos;
            q.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var mr = q.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return q;
        }

        /// <summary>
        /// Неразрешённые звёзды галактики — облако точек (по 1 пикселю на любом зуме): плотнее вокруг систем
        /// и в рукавах, лёгкий тёплый оттенок ближе к ядру. Одна сетка, один вызов отрисовки.
        /// </summary>
        private void BuildDustStars(float R, System.Random rng)
        {
            int count = Mathf.Min(60000, _generator.Systems.Count * 70);
            int coreCount = Mathf.Min(9000, count / 2);         // плотное звёздное скопление ядра
            count += coreCount;
            var verts = new Vector3[count];
            var cols = new Color32[count];
            var idx = new int[count];
            for (int k = 0; k < count; k++)
            {
                float x, z;
                if (k < coreCount)
                {
                    float sg = rng.NextDouble() < 0.6 ? 0.06f : 0.14f;
                    x = Gauss(rng) * sg * R;
                    z = Gauss(rng) * sg * R;
                }
                else
                {
                    var s = _generator.Systems[rng.Next(_generator.Systems.Count)];
                    x = s.Position.x + Gauss(rng) * 0.12f * R;
                    z = s.Position.z + Gauss(rng) * 0.12f * R;
                }
                // Толщина: диск тоньше к краю, ядро — вздутый шар (балдж)
                float rr = new Vector2(x, z).magnitude / R;
                float thick = k < coreCount
                    ? R * 0.055f * Mathf.Exp(-rr * rr / 0.03f) + R * 0.012f
                    : R * Mathf.Lerp(0.035f, 0.012f, Mathf.Clamp01(rr));
                float y = Gauss(rng) * thick;
                verts[k] = new Vector3(x, y - 0.6f, z);
                float r = new Vector2(x, z).magnitude / R;
                float b = 0.25f + (float)(rng.NextDouble() * rng.NextDouble()) * 0.75f;   // больше тусклых, мало ярких
                Color c = Color.Lerp(new Color(0.78f, 0.84f, 1f), new Color(1f, 0.88f, 0.7f), Mathf.Exp(-r * r / 0.08f));
                cols[k] = new Color(c.r, c.g, c.b, b);
                idx[k] = k;
            }
            var mesh = new Mesh { name = "GalaxyDustStars" };
            if (count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.colors32 = cols;
            mesh.SetIndices(idx, MeshTopology.Points, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(R * 3f, R * 0.6f, R * 3f));

            _dustStars = new GameObject("GalaxyDustStars");
            _dustStars.transform.SetParent(transform, false);
            _dustStars.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = _dustStars.AddComponent<MeshRenderer>();
            var twinkle = Resources.Load<Shader>("Shaders/TwinklePoints");
            _dustStarsMat = new Material(twinkle != null ? twinkle : GetSpriteShader());
            _dustStarsMat.renderQueue = 2965;
            mr.sharedMaterial = _dustStarsMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>
        /// Пыль и ядро — для обзора: вблизи растянутая текстура превращается в муть, поэтому гаснет.
        /// Заливка территорий вблизи тоже почти исчезает — остаётся только кромка.
        /// </summary>
        private void UpdateBackdropZoom(Camera cam)
        {
            float h = cam.transform.position.y;
            float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(45f, 170f, h));
            if (_diskMat != null) _diskMat.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.08f, 1f, far));
            if (_dustStarsMat != null) _dustStarsMat.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.45f, 1f, far));

            // Живой фон: время для шейдеров, туманности чуть тише вблизи
            float t = Time.unscaledTime;
            if (_diskMat != null) _diskMat.SetFloat("_T", t);
            if (_dustStarsMat != null) _dustStarsMat.SetFloat("_T", t);
            float nebK = Mathf.Lerp(0.5f, 1f, far);
            // клочья над картой близко к камере — вблизи растворяются, чтобы не мешать
            float wispK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(55f, 130f, h));
            float dustK = Mathf.Lerp(0.25f, 1f, far);
            for (int i = 0; i < _darkDustMats.Count; i++)
            {
                _darkDustMats[i].SetFloat("_T", t);
                _darkDustMats[i].SetFloat("_Opacity", _darkDustBase[i] * dustK);
            }
            if (_coreMat != null)
            {
                _coreMat.SetFloat("_T", t);
                _coreMat.SetFloat("_Intensity", Mathf.Lerp(0.08f, 1f, far));   // вблизи ядро не заливает карту
            }
            if (_coreBulge != null)
            {
                float pulse = 1f + 0.2f * Mathf.Sin(t * 0.9f);
                _coreBulge.color = new Color(1f, 0.86f, 0.62f, Mathf.Lerp(0.03f, 0.5f, far) * pulse);
                _coreBulge.transform.localScale = Vector3.one * (_coreBulgeBase * (1f + 0.08f * Mathf.Sin(t * 0.9f)));
            }
            foreach (var (m, k, layer) in _nebulaMats)
            {
                m.SetFloat("_T", t);
                m.SetFloat("_Intensity", k * (layer == 2 ? wispK : nebK));
            }
            if (_borderMat != null)
            {
                // Линейное цветовое пространство: малая альфа на чёрном выглядит намного плотнее,
                // поэтому заливка вблизи — едва заметная дымка, остаётся тонкая кромка
                _borderMat.SetFloat("_FillAlpha", Mathf.Lerp(0.008f, 0.07f, far));
                _borderMat.SetFloat("_BandAlpha", Mathf.Lerp(0.02f, 0.07f, far));
                _borderMat.SetFloat("_LinePx", Mathf.Lerp(1.4f, 2.2f, far));
                _borderMat.SetFloat("_LineAlpha", Mathf.Lerp(0.5f, 0.85f, far));
            }
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
