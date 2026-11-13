using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Чёрная дыра: вблизи на карте галактики (MapBlackHole) и в виде системы вместо плоского спрайта.
    ///
    /// Геометрии нет — сфера-оболочка с шейдером Astropolity/FX/BlackHole, который трассирует лучи в искривлённом
    /// пространстве: тень горизонта, фотонное кольцо, аккреционный диск с дугами гравитационной линзы над и под
    /// горизонтом, кеплеровское вращение плазмы и доплеровское усиление набегающей стороны. Радиус оболочки — radius.
    ///
    /// Камера смотрит на карту сверху, а линза читается под низким углом — поэтому диск всегда повёрнут к камере
    /// так, что она видит его под <see cref="BlackHoleFacing.Elevation"/>.
    /// </summary>
    public static class BlackHoleModel
    {
        /// <summary>Радиус оболочки в мире (диск выходит на ~0,85 её) — внутри первой орбиты (не ближе 13).</summary>
        public const float DefaultRadius = 11f;

        private static Shader s_shader;
        private static Material s_material;
        private static Mesh s_sphere;
        private static bool s_missing;

        /// <summary>Перезагрузка домена в Play Mode выключена — сбрасываем кэш на каждом запуске.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_shader = null;
            s_material = null;
            s_sphere = null;
            s_missing = false;
        }

        public static bool Available
        {
            get
            {
                if (s_shader == null && !s_missing) Load();
                return s_shader != null && s_sphere != null;
            }
        }

        /// <summary>
        /// Собрать дыру под parent (в его центре); null — если шейдера нет (тогда остаётся спрайт карты).
        /// withRings оставлен для совместимости (колец больше нет). elevation — угол камеры к плоскости диска.
        /// </summary>
        public static GameObject Build(Transform parent, float radius = DefaultRadius, bool withRings = true,
                                       float elevation = BlackHoleFacing.DefaultElevation)
        {
            if (!Available) return null;

            var root = new GameObject("BlackHole");
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * radius;
            root.AddComponent<BlackHoleFacing>().Elevation = elevation;

            if (s_material == null)
                s_material = new Material(s_shader) { hideFlags = HideFlags.DontSave, name = "BlackHole" };

            // Примитив-сфера Unity радиуса 0,5 → ×2: оболочка радиуса 1 в координатах root
            var go = new GameObject("lens");
            go.transform.SetParent(root.transform, false);
            go.transform.localScale = Vector3.one * 2f;
            go.AddComponent<MeshFilter>().sharedMesh = s_sphere;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = s_material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return root;
        }

        private static void Load()
        {
            s_shader = Resources.Load<Shader>("Shaders/BlackHole");
            if (s_shader == null || !s_shader.isSupported)
            {
                s_shader = null;
                s_missing = true;
                Debug.LogWarning("[BlackHole] Нет шейдера Shaders/BlackHole — чёрные дыры остаются спрайтом.");
                return;
            }
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s_sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(tmp);
        }
    }

    /// <summary>
    /// Диск поворачивается к камере: она видит его под <see cref="Elevation"/> над плоскостью диска,
    /// диск лежит горизонтально на экране. Дыра симметрична, так что «правильной» стороны у неё нет.
    /// </summary>
    public class BlackHoleFacing : MonoBehaviour
    {
        public const float DefaultElevation = 8f;   // почти с ребра: диск перечёркивает тень, дуги линзы обнимают её
        /// <summary>Угол камеры над плоскостью диска, градусы.</summary>
        public float Elevation = DefaultElevation;
        private Camera _cam;

        private static readonly int BHTimeId = Shader.PropertyToID("_BHTime");

        private void LateUpdate()
        {
            // Течение плазмы — по реальному времени, и на паузе (это декор)
            Shader.SetGlobalFloat(BHTimeId, Time.unscaledTime);
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            var ct = _cam.transform;
            Vector3 toCam = ct.position - transform.position;
            if (toCam.sqrMagnitude < 1e-6f) return;
            toCam.Normalize();
            Vector3 up = Vector3.ProjectOnPlane(ct.up, toCam);
            if (up.sqrMagnitude < 1e-6f) return;
            up.Normalize();
            float e = Elevation * Mathf.Deg2Rad;
            Vector3 normal = toCam * Mathf.Sin(e) + up * Mathf.Cos(e);
            Vector3 side = Vector3.Cross(up, toCam);
            transform.rotation = Quaternion.LookRotation(Vector3.Cross(side, normal), normal);
        }
    }

    /// <summary>
    /// Чёрная дыра на карте галактики: при приближении камеры (там же, где проступают мини-системы)
    /// спрайт плавно уступает место объёмной модели. Модель строится лениво при первом приближении.
    /// В виде системы модель карты спрятана — там своя, крупнее.
    /// </summary>
    public class MapBlackHole : MonoBehaviour
    {
        public const string ModelName = "BlackHoleMap";
        /// <summary>
        /// Масштаб модели (радиус внешнего кольца, которого на карте нет): светящийся диск выходит на ~2,4 —
        /// крупнее короны обычной звезды, ядро с дугами линзы хорошо читается вблизи.
        /// </summary>
        private const float Radius = 4.2f;
        private const float Elevation = 11f;
        private const float ShowHeight = 90f, FullHeight = 52f;   // как у мини-систем

        /// <summary>Отладочный предпросмотр (F8) на обычной звезде.</summary>
        public bool Preview { get; private set; }

        private StarAnimator _anim;
        private GameObject _model;
        private Renderer[] _glow;
        private MaterialPropertyBlock _mpb;
        private Camera _cam;
        private bool _failed;

        public void Init(StarAnimator anim, bool preview = false)
        {
            _anim = anim;
            Preview = preview;
        }

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            bool inSystem = SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView;
            float k = 0f;
            if (_cam != null && !inSystem && !_failed)
            {
                float h = _cam.transform.position.y - transform.position.y;
                k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FullHeight, ShowHeight, h));
            }
            if (_anim != null && !inSystem) _anim.BodyHidden = k;

            if (k <= 0.01f)
            {
                if (_model != null && _model.activeSelf) _model.SetActive(false);
                return;
            }
            if (_model == null && !Create()) return;
            if (!_model.activeSelf) _model.SetActive(true);

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.SetFloat("_Fade", k);
            foreach (var r in _glow) r.SetPropertyBlock(_mpb);
        }

        private bool Create()
        {
            _model = BlackHoleModel.Build(transform, Radius, withRings: false, elevation: Elevation);
            if (_model == null)
            {
                _failed = true;
                if (_anim != null) _anim.BodyHidden = 0f;
                return false;
            }
            _model.name = ModelName;
            _glow = _model.GetComponentsInChildren<Renderer>(true);
            return true;
        }

        private void OnDestroy()
        {
            if (_model != null) Destroy(_model);
            if (_anim != null) _anim.BodyHidden = 0f;
        }
    }
}
