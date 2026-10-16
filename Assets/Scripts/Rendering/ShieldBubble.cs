using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Энергетический щит корабля или станции — эллипсоид по габаритам модели.
    /// Невидим в покое; каждое попадание оставляет яркую точку удара и волну по поверхности с шестигранной сеткой поля.
    /// Пробитие щита — вспышка всей сферы и распад сетки. Анимация в реальном времени (шейдер ShieldBubble).
    /// </summary>
    public class ShieldBubble : MonoBehaviour
    {
        private const float Visible = 1.6f;
        private static Mesh s_sphere;
        private static Shader s_shader;
        private static readonly int IdHits = Shader.PropertyToID("_Hits");
        private static readonly int IdNow = Shader.PropertyToID("_Now");
        private static readonly int IdCollapse = Shader.PropertyToID("_Collapse");
        private static readonly int IdStrength = Shader.PropertyToID("_Strength");
        private static readonly int IdColor = Shader.PropertyToID("_Color");

        private readonly Vector4[] _hits = new Vector4[6];
        private int _next;
        private float _clock = 10f, _lastHit = -100f, _collapse = -100f;
        private Material _mat;
        private MeshRenderer _mr;
        private Transform _host;

        /// <summary>Щит носителя (создаётся при первом попадании). minRadius — если у носителя нет видимой модели.</summary>
        public static ShieldBubble For(Transform host, float minRadius, Color tint)
        {
            if (host == null) return null;
            var existing = host.GetComponentInChildren<ShieldBubble>(true);
            if (existing != null) return existing;
            if (s_shader == null) s_shader = Resources.Load<Shader>("Shaders/ShieldBubble");
            if (s_shader == null || !s_shader.isSupported) return null;
            if (s_sphere == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s_sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }

            var go = new GameObject("ShieldBubble");
            go.transform.SetParent(host, false);
            go.layer = host.gameObject.layer;
            var b = go.AddComponent<ShieldBubble>();
            b._host = host;
            go.AddComponent<MeshFilter>().sharedMesh = s_sphere;
            b._mr = go.AddComponent<MeshRenderer>();
            b._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b._mr.receiveShadows = false;
            b._mat = new Material(s_shader) { name = "ShieldBubble" };
            b._mat.SetColor(IdColor, tint);
            b._mr.sharedMaterial = b._mat;
            b._mr.enabled = false;
            for (int i = 0; i < b._hits.Length; i++) b._hits[i] = new Vector4(0f, 1f, 0f, -100f);
            b._mat.SetVectorArray(IdHits, b._hits);   // размер массива фиксируется первым присваиванием

            // Габариты модели в локальных координатах носителя → эллипсоид с запасом
            var bounds = new Bounds();
            bool any = false;
            foreach (var mf in host.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.gameObject == go) continue;
                string n = mf.gameObject.name;
                if (n.Contains("Plume") || n.Contains("Fan") || n.Contains("Shield")) continue;
                var mb = mf.sharedMesh.bounds;
                var m = host.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
                }
            }
            Vector3 size = any ? bounds.size : Vector3.one * minRadius * 2f;
            size = Vector3.Max(size * 1.3f + Vector3.one * 0.25f, Vector3.one * minRadius * 1.2f);
            size.y = Mathf.Max(size.y, Mathf.Min(size.x, size.z) * 0.55f);
            go.transform.localPosition = any ? bounds.center : Vector3.zero;
            go.transform.localScale = size;
            return b;
        }

        /// <summary>Радиус щита по направлению на точку (для выбора точки попадания на поверхности).</summary>
        public Vector3 SurfacePoint(Vector3 from, float jitter)
        {
            Vector3 dir = transform.InverseTransformDirection(from - transform.position).normalized;
            dir = (dir + Random.insideUnitSphere * jitter).normalized;
            return transform.TransformPoint(dir * 0.5f);
        }

        /// <summary>Попадание в щит: волна от точки удара.</summary>
        public void Hit(Vector3 worldPoint, float strength = 1f)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            if (local.sqrMagnitude < 1e-6f) local = Vector3.up;
            _hits[_next] = new Vector4(local.x, local.y, local.z, _clock);
            _next = (_next + 1) % _hits.Length;
            _lastHit = _clock;
            _mat.SetVectorArray(IdHits, _hits);
            _mr.enabled = true;
        }

        /// <summary>Щит пробит — вспышка всей сферы и распад сетки.</summary>
        public void Collapse()
        {
            _collapse = _clock;
            _lastHit = _clock;
            _mr.enabled = true;
        }

        private void LateUpdate()
        {
            _clock += Time.unscaledDeltaTime;
            bool on = _clock - _lastHit < Visible;
            if (_mr.enabled != on) _mr.enabled = on;
            if (!on) return;
            if (_host != null && gameObject.layer != _host.gameObject.layer) gameObject.layer = _host.gameObject.layer;
            _mat.SetFloat(IdNow, _clock);
            _mat.SetFloat(IdCollapse, _collapse);
            _mat.SetFloat(IdStrength, 1f);
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
