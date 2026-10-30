using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Студия для рендера одной планеты в RenderTexture.
    /// Используется в PlanetOverviewModal и PlanetFocusOverlay.
    /// Загружает процедурные префабы из Resources/Planets/, если они есть.
    /// </summary>
    public class PlanetHoloStudio : MonoBehaviour
    {
        public static PlanetHoloStudio Instance { get; private set; }

        public RenderTexture Target { get; private set; }
        public Camera HoloCamera { get; private set; }

        private Transform _stage;
        private GameObject _planetInstance;
        private Light _keyLight;
        private Light _rimLight;
        private Light _fillLight;
        private int _layer;
        private bool _dragging;
        private Vector3 _lastMouse;
        private float _autoSpin = 6f;

        private void Awake()
        {
            Instance = this;
            _layer = LayerMask.NameToLayer("PlanetHolo");
            if (_layer < 0) _layer = 8;

            Target = new RenderTexture(1024, 1024, 16, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "PlanetHoloRT"
            };

            var camGo = new GameObject("PlanetHoloCamera");
            camGo.transform.SetParent(transform, false);
            HoloCamera = camGo.AddComponent<Camera>();
            HoloCamera.clearFlags = CameraClearFlags.SolidColor;
            HoloCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            HoloCamera.orthographic = false;
            HoloCamera.fieldOfView = 30f;
            HoloCamera.nearClipPlane = 0.1f;
            HoloCamera.farClipPlane = 40f;
            HoloCamera.cullingMask = 1 << _layer;
            HoloCamera.targetTexture = Target;
            HoloCamera.enabled = false;
            HoloCamera.allowHDR = true;
            HoloCamera.allowMSAA = false;

            transform.position = new Vector3(0f, -4800f, 0f);
            // Камера строго напротив центра — планета в центре кадра и своего кольца
            camGo.transform.localPosition = new Vector3(0f, 0f, -6.5f);
            camGo.transform.localRotation = Quaternion.identity;

            _stage = new GameObject("HoloStage").transform;
            _stage.SetParent(transform, false);

            // Основной свет
            var lightGo = new GameObject("HoloKeyLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(-2.4f, 1.6f, -2.6f);
            lightGo.transform.LookAt(transform.position);
            _keyLight = lightGo.AddComponent<Light>();
            _keyLight.type = LightType.Directional;
            _keyLight.color = new Color(1f, 0.96f, 0.85f);
            _keyLight.intensity = 1.6f;
            _keyLight.cullingMask = 1 << _layer;
            _keyLight.shadows = LightShadows.Soft;

            // Подсветка сзади — как rim light
            var rimGo = new GameObject("HoloRimLight");
            rimGo.transform.SetParent(transform, false);
            rimGo.transform.localRotation = Quaternion.Euler(-20f, 200f, 0f);
            _rimLight = rimGo.AddComponent<Light>();
            _rimLight.type = LightType.Directional;
            _rimLight.color = new Color(0.15f, 0.35f, 0.55f);
            _rimLight.intensity = 0.85f;
            _rimLight.cullingMask = 1 << _layer;

            // Мягкая заливка спереди-снизу: ночная сторона остаётся тёмной, но читается как шар
            var fillGo = new GameObject("HoloFillLight");
            fillGo.transform.SetParent(transform, false);
            fillGo.transform.localRotation = Quaternion.Euler(-12f, -25f, 0f);
            _fillLight = fillGo.AddComponent<Light>();
            _fillLight.type = LightType.Directional;
            _fillLight.color = new Color(0.30f, 0.42f, 0.60f);
            _fillLight.intensity = 0.35f;
            _fillLight.cullingMask = 1 << _layer;

            SetLayerRecursively(gameObject, _layer);
            ExcludeFromMainCameras();
        }

        private void LateUpdate()
        {
            if (!HoloCamera.enabled || _planetInstance == null) return;
            if (!_dragging)
                _planetInstance.transform.Rotate(Vector3.up * _autoSpin * Time.unscaledDeltaTime, Space.Self);
        }

        /// <summary>
        /// Подогнать модель под кадр по её реальным границам: у процедурных префабов размер меша
        /// не совпадает с planet.Size, и планета выходила то крошечной, то обрезанной.
        /// </summary>
        private static void FitToFrame(Transform model, float targetDiameter)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            float d = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (d < 0.01f) return;
            float k = targetDiameter / d;
            Vector3 pivot = model.position;
            model.localScale *= k;
            // Центр модели — в центр кадра (у некоторых префабов пивот смещён)
            Vector3 newCenter = pivot + (b.center - pivot) * k;
            if (model.parent != null) model.position += model.parent.position - newCenter;
        }

        public void ShowPlanet(PlanetData planet, Material sourceMat)
        {
            ClearPlanet();
            if (planet == null) return;

            _planetInstance = BuildModel(planet, sourceMat, _stage, 3.1f);

            SetLayerRecursively(_planetInstance, _layer);
            HoloCamera.enabled = true;
            _keyLight.enabled = true;
            _rimLight.enabled = true;
            _fillLight.enabled = true;
        }

        /// <summary>Модель планеты как в системе (процедурная / «настоящая» / префаб, иначе сфера), вписанная в diameter.</summary>
        public static GameObject BuildModel(PlanetData planet, Material sourceMat, Transform parent, float diameter)
        {
            GameObject go;
            // Пытаемся загрузить процедурный префаб как в SystemViewManager
            var prefab = PlanetModelLibrary.Template(planet) ?? RealPlanetModel.Template(planet) ?? LoadPlanetPrefabFor(planet);
            if (prefab != null)
            {
                go = Instantiate(prefab, parent);
                go.name = "HoloPlanet_Procedural";
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;

                // Подгоняем размер под сцену HoloCamera
                // Диаметр планеты в сцене всегда 2.2 unit — независимо от planet.Size
float targetDiameter = 2.2f;
float srcDiameter = Mathf.Max(0.5f, planet.Size);
float scale = targetDiameter / srcDiameter;
go.transform.localScale = Vector3.one * scale;

                // Убираем коллайдеры префаба
                foreach (var c in go.GetComponentsInChildren<Collider>())
                    Destroy(c);
                FitToFrame(go.transform, diameter);
            }
            else
            {
                // Fallback — старая сфера
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "HoloPlanet_Fallback";
                go.transform.SetParent(parent, false);
                go.transform.localScale = Vector3.one * diameter;
                Destroy(go.GetComponent<Collider>());

                var rend = go.GetComponent<MeshRenderer>();
                if (sourceMat != null)
                    rend.material = new Material(sourceMat);
                else
                {
                    var mat = new Material(ShaderCache.Lit);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", planet.PlanetColor);
                    if (mat.HasProperty("_Color")) mat.color = planet.PlanetColor;
                    rend.material = mat;
                }
            }

            return go;
        }

        private static GameObject LoadPlanetPrefabFor(PlanetData planet)
        {
            string[] candidates = planet.Type switch
            {
                PlanetType.Continental => new[] { "planet_continental", "planet_continental_2" },
                PlanetType.Desert      => new[] { "planet_desert", "planet_desert_2" },
                PlanetType.Molten      => new[] { "planet_molten", "planet_barren" },
                PlanetType.GasGiant    => new[] { "planet_gas", "planet_gas_2" },
                PlanetType.Barren      => new[] { "planet_barren", "planet_moon" },
                _                      => new[] { "planet_barren" }
            };

            // Стабильный seed — та же планета, что в системе
            int stableSeed = (planet.Name?.GetHashCode() ?? 0);
            var rng = new System.Random(stableSeed);
            int firstPick = rng.Next(0, candidates.Length);

            for (int i = 0; i < candidates.Length; i++)
            {
                int idx = (firstPick + i) % candidates.Length;
                var prefab = Resources.Load<GameObject>("Planets/" + candidates[idx]);
                if (prefab != null) return prefab;
            }
            return null;
        }

        public void Hide()
        {
            if (HoloCamera != null) HoloCamera.enabled = false;
            if (_keyLight != null) _keyLight.enabled = false;
            if (_rimLight != null) _rimLight.enabled = false;
            if (_fillLight != null) _fillLight.enabled = false;
            ClearPlanet();
        }

        public void BeginDrag(Vector3 mouse)
        {
            _dragging = true;
            _lastMouse = mouse;
        }

        public void Drag(Vector3 mouse)
        {
            if (!_dragging || _planetInstance == null) return;
            Vector3 delta = mouse - _lastMouse;
            _planetInstance.transform.Rotate(Vector3.up, -delta.x * 0.4f, Space.World);
            _planetInstance.transform.Rotate(Vector3.right, delta.y * 0.3f, Space.World);
            _lastMouse = mouse;
        }

        public void EndDrag() => _dragging = false;

        private void ClearPlanet()
        {
            if (_planetInstance != null) Destroy(_planetInstance);
            _planetInstance = null;
        }

        private void OnDestroy()
        {
            if (Target != null)
            {
                Target.Release();
                Destroy(Target);
            }
        }

        internal static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform)
                SetLayerRecursively(c.gameObject, layer);
        }

        private static void ExcludeFromMainCameras()
        {
            int layer = LayerMask.NameToLayer("PlanetHolo");
            if (layer < 0) return;
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            {
                if (cam.targetTexture != null) continue;
                cam.cullingMask &= ~(1 << layer);
            }
        }
    }

    /// <summary>
    /// Снимки моделей планет для интерфейса (карточки колоний и т.п.): своя маленькая студия далеко от карты,
    /// модель ставится перед камерой, кадр снимается в RenderTexture один раз, модель убирается.
    /// Get отдаёт текстуру сразу (пока прозрачную) — картинка появится через кадр-другой.
    /// </summary>
    public class PlanetThumbs : MonoBehaviour
    {
        private const int Size = 256;
        private static PlanetThumbs s_inst;
        private static readonly Dictionary<PlanetData, RenderTexture> s_cache = new Dictionary<PlanetData, RenderTexture>();
        private readonly Queue<PlanetData> _queue = new Queue<PlanetData>();
        private Camera _cam;
        private Transform _stage;
        private Light[] _lights;
        private int _layer;
        private bool _busy;

        public static RenderTexture Get(PlanetData planet)
        {
            if (planet == null) return null;
            if (s_cache.TryGetValue(planet, out var rt) && rt != null) return rt;
            if (s_inst == null) s_inst = new GameObject("[UI] PlanetThumbs").AddComponent<PlanetThumbs>();
            if (s_cache.Count > 96) Flush();   // новая партия — старые снимки не нужны

            rt = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "PlanetThumb_" + planet.Name, antiAliasing = 2
            };
            rt.Create();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            RenderTexture.active = prev;
            s_cache[planet] = rt;
            s_inst._queue.Enqueue(planet);
            return rt;
        }

        private static void Flush()
        {
            foreach (var rt in s_cache.Values) if (rt != null) { rt.Release(); Destroy(rt); }
            s_cache.Clear();
        }

        private void Awake()
        {
            _layer = LayerMask.NameToLayer("PlanetHolo");
            if (_layer < 0) _layer = 8;
            transform.position = new Vector3(0f, -5600f, 0f);

            var camGo = new GameObject("ThumbCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -6.5f);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 40f;
            _cam.cullingMask = 1 << _layer;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.enabled = false;

            _stage = new GameObject("ThumbStage").transform;
            _stage.SetParent(transform, false);

            // Свет как у голограммы планеты: тёплый ключевой слева сверху, холодный контровой, мягкая заливка
            _lights = new[]
            {
                MakeLight("Key", new Vector3(-2.4f, 1.6f, -2.6f), null, new Color(1f, 0.96f, 0.85f), 1.6f),
                MakeLight("Rim", Vector3.zero, Quaternion.Euler(-20f, 200f, 0f), new Color(0.15f, 0.35f, 0.55f), 0.85f),
                MakeLight("Fill", Vector3.zero, Quaternion.Euler(-12f, -25f, 0f), new Color(0.30f, 0.42f, 0.60f), 0.35f),
            };
            foreach (var l in _lights) l.enabled = false;
            PlanetHoloStudio.SetLayerRecursively(gameObject, _layer);
            DontDestroyOnLoad(gameObject);
        }

        private Light MakeLight(string name, Vector3 pos, Quaternion? rot, Color c, float intensity)
        {
            var go = new GameObject("Thumb" + name + "Light");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            if (rot.HasValue) go.transform.localRotation = rot.Value; else go.transform.LookAt(transform.position);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = c;
            l.intensity = intensity;
            l.cullingMask = 1 << _layer;
            l.shadows = LightShadows.None;
            return l;
        }

        private void Update()
        {
            if (!_busy && _queue.Count > 0) StartCoroutine(Shoot(_queue.Dequeue()));
        }

        private System.Collections.IEnumerator Shoot(PlanetData planet)
        {
            _busy = true;
            if (!s_cache.TryGetValue(planet, out var rt) || rt == null) { _busy = false; yield break; }

            Material src = null;
            var inst = SystemViewManager.Instance?.GetPlanetInstance(planet);
            if (inst?.Renderer != null) src = inst.Renderer.sharedMaterial;
            GameObject model = null;
            try
            {
                model = PlanetHoloStudio.BuildModel(planet, src, _stage, 3.1f);
                // Чуть повернуть к свету — терминатор красивее, чем ровно анфас
                model.transform.localRotation = Quaternion.Euler(12f, -25f, 0f) * model.transform.localRotation;
                PlanetHoloStudio.SetLayerRecursively(model, _layer);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PlanetThumbs] " + e.Message);
                if (model != null) Destroy(model);
                _busy = false;
                yield break;
            }

            _cam.targetTexture = rt;
            _cam.enabled = true;
            foreach (var l in _lights) l.enabled = true;
            yield return new WaitForEndOfFrame();   // кадр снят в RenderTexture
            _cam.enabled = false;
            _cam.targetTexture = null;
            foreach (var l in _lights) l.enabled = false;
            Destroy(model);
            yield return null;
            _busy = false;
        }
    }

    public class HoloDragCatcher : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        public void OnBeginDrag(PointerEventData eventData) => PlanetHoloStudio.Instance?.BeginDrag(eventData.position);
        public void OnDrag(PointerEventData eventData) => PlanetHoloStudio.Instance?.Drag(eventData.position);
        public void OnEndDrag(PointerEventData eventData) => PlanetHoloStudio.Instance?.EndDrag();
        public void OnPointerDown(PointerEventData eventData) { }
        public void OnPointerUp(PointerEventData eventData) => PlanetHoloStudio.Instance?.EndDrag();
    }
}