using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Студия чертежа для конструктора кораблей: настоящая модель выбранного корпуса, отрисованная голографическим
    /// каркасом (шейдер ShipHolo: светящиеся контуры, сетка «проволоки», полоса сканирования), в рендер-текстуру.
    /// Нос корабля смотрит вправо, модель медленно покачивается — чертёж «живой». Камера и модель — далеко под картой,
    /// на своём слое; камера выключена, пока конструктор закрыт.
    /// </summary>
    public class ShipHoloStudio : MonoBehaviour
    {
        public static ShipHoloStudio Instance { get; private set; }
        public RenderTexture Target { get; private set; }

        private Camera _cam;
        private Transform _stage;
        private GameObject _model;
        private Material _holo;
        private ShipClass _shown = (ShipClass)(-1);
        private bool _shownReal;
        private bool _real;
        private float _yawOffset, _pitchOffset;
        private Light _key, _fill, _rim;
        private Color _color = new Color(0.35f, 0.9f, 1f, 1f);
        private int _layer;
        private float _t;

        private static readonly int IdColor = Shader.PropertyToID("_Color");
        private static readonly int IdGrid = Shader.PropertyToID("_GridScale");

        public static ShipHoloStudio Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[UI] ShipHoloStudio");
            DontDestroyOnLoad(go);
            return go.AddComponent<ShipHoloStudio>();
        }

        private void Awake()
        {
            Instance = this;
            _layer = LayerMask.NameToLayer("PlanetHolo");
            if (_layer < 0) _layer = 8;
            transform.position = new Vector3(0f, -5400f, 0f);

            Target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { name = "ShipHoloRT", filterMode = FilterMode.Bilinear, antiAliasing = 2 };

            var camGo = new GameObject("ShipHoloCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.fieldOfView = 26f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 60f;
            _cam.cullingMask = 1 << _layer;
            _cam.targetTexture = Target;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.enabled = false;
            // Чуть сверху и сбоку: корпус читается объёмно, нос — вправо
            camGo.transform.localPosition = new Vector3(0f, 3.2f, -13f);
            camGo.transform.LookAt(transform.position);

            _stage = new GameObject("Stage").transform;
            _stage.SetParent(transform, false);

            var sh = Resources.Load<Shader>("Shaders/ShipHolo");
            if (sh != null) _holo = new Material(sh) { name = "ShipHolo" };

            // Студийный свет для «настоящей» модели (светит только на слой студии)
            _key = MakeLight("Key", new Color(1f, 0.96f, 0.9f), 1.5f, new Vector3(30f, -35f, 0f));
            _fill = MakeLight("Fill", new Color(0.55f, 0.70f, 1f), 0.55f, new Vector3(15f, 140f, 0f));
            _rim = MakeLight("Rim", new Color(0.45f, 0.9f, 1f), 0.9f, new Vector3(-20f, 180f, 0f));
            SetLights(false);
        }

        private Light MakeLight(string name, Color c, float intensity, Vector3 euler)
        {
            var go = new GameObject("Light_" + name);
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = c;
            l.intensity = intensity;
            l.cullingMask = 1 << _layer;
            l.shadows = LightShadows.None;
            return l;
        }

        private void SetLights(bool on)
        {
            if (_key != null) _key.enabled = on;
            if (_fill != null) _fill.enabled = on;
            if (_rim != null) _rim.enabled = on;
        }

        /// <summary>Показать настоящую модель корпуса (материалы фракции игрока, студийный свет) — экран проектов кораблей.</summary>
        public void ShowReal(ShipClass hull)
        {
            _real = true;
            if (_model == null || _shown != hull || !_shownReal) Rebuild(hull);
            _cam.enabled = _model != null;
            SetLights(_cam.enabled);
        }

        /// <summary>Повернуть модель мышью (градусы по горизонтали и вертикали).</summary>
        public void Spin(float dx, float dy)
        {
            _yawOffset += dx;
            _pitchOffset = Mathf.Clamp(_pitchOffset + dy, -25f, 25f);
        }

        /// <summary>Показать корпус каркасом заданного цвета.</summary>
        public void Show(ShipClass hull, Color color)
        {
            _real = false;
            _color = color;
            if (_holo != null) _holo.SetColor(IdColor, color);
            if (_model == null || _shown != hull || _shownReal) Rebuild(hull);
            _cam.enabled = _model != null;
            SetLights(false);
        }

        public void Hide()
        {
            if (_cam != null) _cam.enabled = false;
            SetLights(false);
        }

        private void Rebuild(ShipClass hull)
        {
            if (_model != null) Destroy(_model);
            _shown = hull;
            _shownReal = _real;
            _model = new GameObject(_real ? "StudioShip" : "HoloShip");
            _model.transform.SetParent(_stage, false);
            ShipMeshFactory.Build(_model.transform, FleetType.Military, hull, _real ? FleetIndicator.OwnColor : _color, 0);

            // Голограмма: все материалы — каркас; сетка — около 18 ячеек вдоль каждого меша.
            // Настоящая модель: свои материалы, без теней и частиц.
            foreach (var r in _model.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) { r.enabled = false; continue; }
                if (_real) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; continue; }
                if (_holo == null) continue;
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = _holo;
                r.sharedMaterials = mats;
                var mf = r.GetComponent<MeshFilter>();
                float size = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size.magnitude : 1f;
                var mpb = new MaterialPropertyBlock();
                mpb.SetFloat(IdGrid, 18f / Mathf.Max(0.01f, size));
                r.SetPropertyBlock(mpb);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            foreach (var l in _model.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var c in _model.GetComponentsInChildren<Collider>(true)) Destroy(c);
            SetLayer(_model, _layer);

            // Вписать в кадр: длина корабля ~ 9 единиц, центр — в центр сцены
            var rs = _model.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                float len = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (len > 0.01f)
                {
                    _model.transform.localScale *= 9f / len;
                    b = rs[0].bounds;
                    for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                    _model.transform.position += _stage.position - b.center;
                }
            }
        }

        private void LateUpdate()
        {
            if (_cam == null || !_cam.enabled || _model == null) return;
            _t += Time.unscaledDeltaTime;
            // Нос вправо (+X), лёгкое покачивание; мышью — повернуть
            float yaw = 90f + _yawOffset + Mathf.Sin(_t * 0.35f) * (_real ? 10f : 22f);
            _stage.localRotation = Quaternion.Euler(Mathf.Sin(_t * 0.5f) * 3f + _pitchOffset, yaw, 0f);
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }

        private void OnDestroy()
        {
            if (Target != null) { Target.Release(); Destroy(Target); }
            if (_holo != null) Destroy(_holo);
        }
    }
}
