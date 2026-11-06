using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Студия окна чуда: настоящая модель мегаструктуры (с готовыми частями и чертежом недостроенного) вокруг маленькой
    /// звезды, в рендер-текстуру. Камера медленно облетает модель. Сцена — далеко под картой, на своём слое,
    /// со своими точечными огнями; камера выключена, пока окно закрыто.
    /// </summary>
    public class WonderStudio : MonoBehaviour
    {
        public static WonderStudio Instance { get; private set; }
        public RenderTexture Target { get; private set; }

        private Camera _cam;
        private Transform _stage, _pivot;
        private GameObject _model, _star;
        private int _layer, _sig = int.MinValue;
        private float _t, _radius = 8f;

        public static WonderStudio Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[UI] WonderStudio");
            DontDestroyOnLoad(go);
            return go.AddComponent<WonderStudio>();
        }

        private void Awake()
        {
            Instance = this;
            _layer = LayerMask.NameToLayer("PlanetHolo");
            if (_layer < 0) _layer = 8;
            transform.position = new Vector3(0f, -6400f, 0f);

            Target = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32) { name = "WonderRT", filterMode = FilterMode.Bilinear, antiAliasing = 4 };

            _pivot = new GameObject("CamPivot").transform;
            _pivot.SetParent(transform, false);
            var camGo = new GameObject("WonderCamera");
            camGo.transform.SetParent(_pivot, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            // Непрозрачный тёмный фон: аддитивное свечение не теряется при выводе в интерфейс
            _cam.backgroundColor = new Color(0.004f, 0.012f, 0.02f, 1f);
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 200f;
            _cam.cullingMask = 1 << _layer;
            _cam.targetTexture = Target;
            _cam.allowHDR = true;
            _cam.allowMSAA = true;
            _cam.enabled = false;

            _stage = new GameObject("Stage").transform;
            _stage.SetParent(transform, false);

            // Свет: звезда в центре и мягкая подсветка со стороны камеры
            AddLight("StarLight", Vector3.zero, new Color(1f, 0.92f, 0.8f), 4f, 60f);
            AddLight("Fill", new Vector3(-14f, 10f, -18f), new Color(0.55f, 0.7f, 1f), 1.6f, 80f);
        }

        private void AddLight(string name, Vector3 pos, Color c, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.cullingMask = 1 << _layer;
        }

        /// <summary>Показать чудо (сборка заново только при смене этапа/прогресса).</summary>
        public void Show(WonderModels.Spec spec, Color starColor)
        {
            int sig = WonderModels.Signature(spec);
            if (_model == null || sig != _sig)
            {
                _sig = sig;
                if (_model != null) Destroy(_model);
                _model = WonderModels.Build(spec, _stage);
                Fit(spec);
                BuildStar(spec, starColor);
                SetLayer(_stage.gameObject, _layer);
            }
            _cam.enabled = true;
        }

        public void Hide()
        {
            if (_cam != null) _cam.enabled = false;
        }

        private void Fit(WonderModels.Spec spec)
        {
            var rs = _model.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { _radius = 8f; return; }
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            _radius = Mathf.Max(3f, b.extents.magnitude);
        }

        private void BuildStar(WonderModels.Spec spec, Color c)
        {
            if (_star != null) Destroy(_star);
            if (spec.Key == "iridia") return;   // Врата показываются сами по себе
            _star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _star.name = "Star";
            Destroy(_star.GetComponent<Collider>());
            _star.transform.SetParent(_stage, false);
            _star.transform.localScale = Vector3.one * 2.2f * Mathf.Max(0.8f, spec.StarMul);
            var mr = _star.GetComponent<MeshRenderer>();
            mr.sharedMaterial = ShipMeshFactory.GlowMat(Color.Lerp(c, Color.white, 0.55f) * 2.2f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void LateUpdate()
        {
            if (_cam == null || !_cam.enabled) return;
            _t += Time.unscaledDeltaTime;
            // Медленный облёт сверху-сбоку: мегаструктура читается объёмно
            _pivot.localRotation = Quaternion.Euler(24f + Mathf.Sin(_t * 0.21f) * 6f, _t * 7f, 0f);
            float dist = _radius / Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;
            _cam.transform.localPosition = new Vector3(0f, 0f, -dist);
            _cam.transform.localRotation = Quaternion.identity;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }
    }
}
