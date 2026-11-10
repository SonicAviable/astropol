using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Фон галактической карты в духе Stellaris:
    ///   • скайбокс (яркая розовая туманность) на карте погашен — фон чёрный, вместо него далёкие звёзды
    ///     и виньетка к краям; в режиме системы скайбокс возвращается, за планетами он красив;
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
        private Material _skyInstance, _deepStarsMat;
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

        /// <summary>
        /// Галактика (вариант C «Туманности») — чёрный космос: скайбокс погашен, светятся только сама галактика,
        /// туманности регионов и далёкие звёзды. Система — исходная туманность скайбокса чуть мягче.
        /// </summary>
        private void ApplySkyMood(bool systemView)
        {
            if (_skyInstance == null) return;
            if (_skyInstance.HasProperty("_Exposure"))
                _skyInstance.SetFloat("_Exposure", _skyExposure * (systemView ? 0.85f : 0f));
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
            var warm = new Color(1.00f, 0.90f, 0.75f);
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

                    float dA = dust * 0.38f, cA = core * 0.45f;
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
            // Рукава ядра — свои (не совпадают с рукавами систем): почти бесцветные, чтобы не рисовать
            // оранжевую спираль на пол-экрана; тёплым остаётся только самый центр
            _coreMat.SetColor("_Warm", new Color(0.78f, 0.8f, 0.95f));
            _coreMat.SetColor("_Outer", new Color(0.42f, 0.42f, 0.5f));
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

        /// <summary>Приглушённый цвет туманности: на 45% к серому той же яркости.</summary>
        private static Color Muted(Color c)
        {
            float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            return Color.Lerp(c, new Color(l, l, l), 0.45f) * 0.9f;
        }

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
                    m.SetColor("_ColorA", Muted(NebulaPalette[ci % NebulaPalette.Length]));
                    m.SetColor("_ColorB", Muted(NebulaPalette[(ci + 1 + rng.Next(2)) % NebulaPalette.Length]));
                    m.SetFloat("_Seed", (float)rng.NextDouble() * 100f);
                    m.SetFloat("_Scale", (layer == 2 ? 3.6f : 2.4f) + (float)rng.NextDouble() * 1.4f);
                    m.renderQueue = layer == 0 ? 2940 : layer == 1 ? 2950 : 2996;
                    _nebulaMats.Add((m, iMin + (float)rng.NextDouble() * (iMax - iMin), layer));
                    q.GetComponent<MeshRenderer>().sharedMaterial = m;
                }
            }

            // Туманности — фон, а не картинка: тусклые и приглушённые, главное на карте — звёзды и границы
            Layer(0, 5, -R * 0.32f, -R * 0.2f, 0.6f, 0.9f, 0.5f, 1.05f, 0.09f, 0.14f);
            Layer(1, 6, -6f, -1.5f, 0.32f, 0.55f, 0.55f, 1.05f, 0.12f, 0.19f);
            Layer(2, 5, R * 0.05f, R * 0.11f, 0.16f, 0.3f, 0.3f, 1.0f, 0.05f, 0.09f);

            BuildDarkDust(R, rng);
            BuildVignette(R);
            BuildDeepStars(R, rng);
        }

        /// <summary>Виньетка вокруг галактики: туманности и фон к краям карты уходят в черноту (над туманностями, под диском).</summary>
        private void BuildVignette(float R)
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "GalaxyVignette" };
            var px = new Color32[n * n];
            float inner = 0.75f / 2.6f, outer = 1.9f / 2.6f;      // доли полуразмера квада (2.6 R)
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float dx = (i + 0.5f) / n * 2f - 1f, dy = (j + 0.5f) / n * 2f - 1f;
                float t = Mathf.InverseLerp(inner, outer, Mathf.Sqrt(dx * dx + dy * dy));
                px[j * n + i] = new Color(0f, 0f, 0f, 0.88f * t * t * (3f - 2f * t));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var q = Quad("GalaxyVignette", _nebulaRoot.transform, new Vector3(0f, -1.2f, 0f), 0f, new Vector2(R * 5.2f, R * 5.2f));
            var m = new Material(GetSpriteShader()) { mainTexture = tex, renderQueue = 2955 };
            q.GetComponent<MeshRenderer>().sharedMaterial = m;
            // В главном меню камера облетает галактику под низким углом — край квада виньетки виден тёмной полосой
            // через весь экран; виньетка нужна только на карте (вид сверху)
            q.AddComponent<HideInMainMenu>();
        }

        /// <summary>Выключает рендер объекта, пока открыто главное меню.</summary>
        private sealed class HideInMainMenu : MonoBehaviour
        {
            private MeshRenderer _r;
            private void Awake() => _r = GetComponent<MeshRenderer>();
            private void LateUpdate()
            {
                if (_r != null) _r.enabled = MainMenuUI.Instance == null;
            }
        }

        /// <summary>Далёкие звёзды фона — вместо скайбокса: тусклые точки глубоко под картой (медленно плывут при движении камеры).</summary>
        private void BuildDeepStars(float R, System.Random rng)
        {
            int count = 3500;
            var verts = new Vector3[count];
            var cols = new Color32[count];
            var idx = new int[count];
            for (int k = 0; k < count; k++)
            {
                verts[k] = new Vector3(((float)rng.NextDouble() - 0.5f) * R * 7f, -R * (0.6f + (float)rng.NextDouble() * 0.5f), ((float)rng.NextDouble() - 0.5f) * R * 7f);
                float b = 0.12f + Mathf.Pow((float)rng.NextDouble(), 4f) * 0.6f;   // почти все тусклые, редкие яркие
                Color c = Color.Lerp(new Color(0.75f, 0.82f, 1f), new Color(1f, 0.92f, 0.82f), (float)rng.NextDouble());
                cols[k] = new Color(c.r, c.g, c.b, b);
                idx[k] = k;
            }
            var mesh = new Mesh { name = "GalaxyDeepStars", vertices = verts, colors32 = cols };
            mesh.SetIndices(idx, MeshTopology.Points, 0);
            mesh.bounds = new Bounds(new Vector3(0f, -R * 0.85f, 0f), new Vector3(R * 7f, R * 0.6f, R * 7f));
            var go = new GameObject("GalaxyDeepStars");
            go.transform.SetParent(_nebulaRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var twinkle = Resources.Load<Shader>("Shaders/TwinklePoints");
            _deepStarsMat = new Material(twinkle != null ? twinkle : GetSpriteShader()) { renderQueue = 2930 };
            // Дальние звёзды чуть ярче — вблизи между системами не пустая чернота
            _deepStarsMat.color = new Color(1.35f, 1.35f, 1.4f, 1f);
            mr.sharedMaterial = _deepStarsMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>Тёмные пылевые облака над диском и ядром, вытянуты вдоль рукавов (по касательной).</summary>
        private void BuildDarkDust(float R, System.Random rng)
        {
            var shader = Resources.Load<Shader>("Shaders/GalaxyDarkDust");
            if (shader == null) return;
            int count = 9;
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
                m.SetFloat("_Opacity", 0.32f + (float)rng.NextDouble() * 0.14f);
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
            // Звёзды рукавов по форме галактики (как у генератора систем): форму рисуют тысячи тусклых точек
            int armCount = Mathf.Clamp(_generator.Systems.Count * 110, 25000, 50000);
            int armStart = coreCount + count;
            count += coreCount + armCount;
            var verts = new Vector3[count];
            var cols = new Color32[count];
            var idx = new int[count];
            for (int k = 0; k < count; k++)
            {
                float x, z;
                bool arm = k >= armStart;
                if (k < coreCount)
                {
                    float sg = rng.NextDouble() < 0.6 ? 0.06f : 0.14f;
                    x = Gauss(rng) * sg * R;
                    z = Gauss(rng) * sg * R;
                }
                else if (arm)
                {
                    var p = ShapePoint(_generator.Shape, rng);
                    x = p.x * R;
                    z = p.y * R;
                }
                else
                {
                    var s = _generator.Systems[rng.Next(_generator.Systems.Count)];
                    x = s.Position.x + Gauss(rng) * 0.08f * R;
                    z = s.Position.z + Gauss(rng) * 0.08f * R;
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
                if (arm) b = 0.05f + (float)(rng.NextDouble() * rng.NextDouble() * rng.NextDouble()) * 0.45f;   // рукава — россыпь тусклых точек
                Color c = Color.Lerp(new Color(0.78f, 0.84f, 1f), new Color(1f, 0.9f, 0.76f), Mathf.Exp(-r * r / 0.08f));
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
        /// Точка галактики той же формы, что у генератора систем (GalaxyGenerator.SamplePosition), в долях радиуса:
        /// рукава спирали (угол рукава — тот же, без случайного сдвига), эллипс или кольцо; ~15% — тусклое межрукавье.
        /// </summary>
        private static Vector2 ShapePoint(GalaxyShape shape, System.Random rng)
        {
            float U() => (float)rng.NextDouble();
            if (U() < 0.15f)
            {
                // Межрукавье: редкие звёзды по всему диску, гуще к центру
                float a0 = U() * Mathf.PI * 2f, r0 = Mathf.Lerp(0.12f, 1.05f, Mathf.Sqrt(U()));
                var q = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r0;
                if (shape == GalaxyShape.Elliptical) q.y *= 0.65f;
                return q;
            }
            switch (shape)
            {
                case GalaxyShape.Elliptical:
                {
                    var p = new Vector2(Gauss(rng), Gauss(rng)) * 0.42f;
                    if (p.magnitude > 1.05f) p = p.normalized * Mathf.Lerp(0.55f, 1.05f, U());
                    p.y *= 0.65f;
                    return p;
                }
                case GalaxyShape.Ring:
                {
                    float a = U() * Mathf.PI * 2f, r = Mathf.Clamp(0.74f + Gauss(rng) * 0.09f, 0.45f, 1.05f);
                    return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }
                default:
                {
                    int arms = shape == GalaxyShape.Spiral4 ? 4 : 2;
                    int arm = rng.Next(arms);
                    float t = Mathf.Pow(U(), 0.85f);
                    float rr = Mathf.Lerp(0.14f, 1.05f, t);
                    float twist = arms == 2 ? 3.3f : 2.4f;
                    float ang = arm * Mathf.PI * 2f / arms + t * twist;
                    // Хребет рукава плотный и узкий, к краям рукав рыхлеет
                    float spread = Mathf.Lerp(0.03f, 0.09f, t) * (arms == 2 ? 1f : 0.8f) * (0.5f + U());
                    return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr + new Vector2(Gauss(rng), Gauss(rng)) * spread;
                }
            }
        }

        /// <summary>
        /// Пыль и ядро — для обзора: вблизи растянутая текстура превращается в муть, поэтому гаснет.
        /// Заливка территорий вблизи тоже почти исчезает — остаётся только кромка.
        /// </summary>
        /// <summary>Общая яркость фона карты (туманности, диск, звёздная пыль) и плотность тёмных пылевых полос.</summary>
        // Как в Stellaris: фон тихий и почти бесцветный, цвет на карте дают границы империй; форму галактики
        // рисуют звёзды (DustStarsBright), а не заливка. Пылевые полосы плотнее — у рукавов есть рельеф.
        private const float BackdropBright = 0.8f, DarkDustK = 1.3f, DustStarsBright = 1f, CoreK = 0.1f;

        private void UpdateBackdropZoom(Camera cam)
        {
            float h = cam.transform.position.y;
            float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(45f, 170f, h));
            // Обзор (галактика разнесена шире — камера уходит на 400–1000 ед.): системы мельчают, поэтому
            // территории заливаются гуще и обводятся толще, а фон диска приглушается — карта читается целиком
            float overview = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(220f, 600f, h));
            if (_diskMat != null) _diskMat.color = new Color(BackdropBright, BackdropBright, BackdropBright, Mathf.Lerp(0.24f, 1f, far) * Mathf.Lerp(1f, 0.6f, overview));
            if (_dustStarsMat != null) _dustStarsMat.color = new Color(1f, 1f, 1f, Mathf.Min(1f, Mathf.Lerp(0.8f, 1f, far) * Mathf.Lerp(1f, 0.6f, overview) * DustStarsBright));

            // Живой фон: время для шейдеров, туманности чуть тише вблизи
            float t = Time.unscaledTime;
            if (_diskMat != null) _diskMat.SetFloat("_T", t);
            if (_dustStarsMat != null) _dustStarsMat.SetFloat("_T", t);
            if (_deepStarsMat != null) _deepStarsMat.SetFloat("_T", t);
            // Вблизи космос не должен проваливаться в черноту: галактика большая, ядро и рукава далеко
            float nebK = Mathf.Lerp(1.8f, 1f, far);
            // клочья над картой близко к камере — вблизи растворяются, чтобы не мешать
            float wispK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(55f, 130f, h));
            float dustK = Mathf.Lerp(0.15f, 1f, far);
            for (int i = 0; i < _darkDustMats.Count; i++)
            {
                _darkDustMats[i].SetFloat("_T", t);
                _darkDustMats[i].SetFloat("_Opacity", _darkDustBase[i] * dustK * DarkDustK);
            }
            if (_coreMat != null)
            {
                _coreMat.SetFloat("_T", t);
                _coreMat.SetFloat("_Intensity", Mathf.Lerp(0.08f, 1f, far) * BackdropBright * CoreK);   // вблизи ядро не заливает карту
            }
            if (_coreBulge != null)
            {
                float pulse = 1f + 0.2f * Mathf.Sin(t * 0.9f);
                _coreBulge.color = new Color(0.9f, 0.92f, 1f, Mathf.Lerp(0.01f, 0.07f, far) * pulse);   // ядро белее и тише — не спорит с картой
                _coreBulge.transform.localScale = Vector3.one * (_coreBulgeBase * (1f + 0.08f * Mathf.Sin(t * 0.9f)));
            }
            foreach (var (m, k, layer) in _nebulaMats)
            {
                m.SetFloat("_T", t);
                m.SetFloat("_Intensity", k * (layer == 2 ? wispK : nebK) * BackdropBright);
            }
            if (_borderMat != null)
            {
                // Линейное цветовое пространство: малая альфа на чёрном выглядит намного плотнее,
                // поэтому заливка вблизи — едва заметная дымка, остаётся тонкая кромка
                // Режим карты усиливает (политика) или приглушает (простой, ресурсы…) границы — GalaxyView.MapModes
                // В обзоре заливка не тоньше 0,14–0,2 в любом режиме карты — иначе в «Простом» территорий не видно
                float fill = Mathf.Lerp(0.008f, 0.07f, far) * _borderFillK;
                float band = Mathf.Lerp(0.02f, 0.07f, far) * _borderFillK;
                _borderMat.SetFloat("_FillAlpha", Mathf.Lerp(fill, Mathf.Max(fill, 0.14f), overview));
                _borderMat.SetFloat("_BandAlpha", Mathf.Lerp(band, Mathf.Max(band, 0.2f), overview));
                _borderMat.SetFloat("_LinePx", Mathf.Lerp(1.4f, 2.2f, far) * Mathf.Lerp(0.85f, 1.15f, Mathf.Clamp01(_borderK - 0.2f)) + 1.6f * overview);
                _borderMat.SetFloat("_LineAlpha", Mathf.Clamp01(Mathf.Lerp(Mathf.Lerp(0.5f, 0.85f, far) * _borderK, 1f, overview)));
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
