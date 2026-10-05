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
            camGo.transform.localPosition = new Vector3(0f, 0.2f, -6.5f);
camGo.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);

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

            SetLayerRecursively(gameObject, _layer);
            ExcludeFromMainCameras();
        }

        private void LateUpdate()
        {
            if (!HoloCamera.enabled || _planetInstance == null) return;
            if (!_dragging)
                _planetInstance.transform.Rotate(Vector3.up * _autoSpin * Time.unscaledDeltaTime, Space.Self);
        }

        public void ShowPlanet(PlanetData planet, Material sourceMat)
        {
            ClearPlanet();
            if (planet == null) return;

            // Пытаемся загрузить процедурный префаб как в SystemViewManager
            var prefab = LoadPlanetPrefabFor(planet);
            if (prefab != null)
            {
                _planetInstance = Instantiate(prefab, _stage);
                _planetInstance.name = "HoloPlanet_Procedural";
                _planetInstance.transform.localPosition = Vector3.zero;
                _planetInstance.transform.localRotation = Quaternion.identity;

                // Подгоняем размер под сцену HoloCamera
                // Диаметр планеты в сцене всегда 2.2 unit — независимо от planet.Size
float targetDiameter = 2.2f;
float srcDiameter = Mathf.Max(0.5f, planet.Size);
float scale = targetDiameter / srcDiameter;
_planetInstance.transform.localScale = Vector3.one * scale;

                // Убираем коллайдеры префаба
                foreach (var c in _planetInstance.GetComponentsInChildren<Collider>())
                    Destroy(c);
            }
            else
            {
                // Fallback — старая сфера
                _planetInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _planetInstance.name = "HoloPlanet_Fallback";
                _planetInstance.transform.SetParent(_stage, false);
                _planetInstance.transform.localScale = Vector3.one * 1.85f;
                Destroy(_planetInstance.GetComponent<Collider>());

                var rend = _planetInstance.GetComponent<MeshRenderer>();
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

            SetLayerRecursively(_planetInstance, _layer);
            HoloCamera.enabled = true;
            _keyLight.enabled = true;
            _rimLight.enabled = true;
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

        private static void SetLayerRecursively(GameObject go, int layer)
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

    public class HoloDragCatcher : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        public void OnBeginDrag(PointerEventData eventData) => PlanetHoloStudio.Instance?.BeginDrag(eventData.position);
        public void OnDrag(PointerEventData eventData) => PlanetHoloStudio.Instance?.Drag(eventData.position);
        public void OnEndDrag(PointerEventData eventData) => PlanetHoloStudio.Instance?.EndDrag();
        public void OnPointerDown(PointerEventData eventData) { }
        public void OnPointerUp(PointerEventData eventData) => PlanetHoloStudio.Instance?.EndDrag();
    }
}