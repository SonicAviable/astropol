using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Живой портрет звезды для интерфейса (подсказка, шапка инспектора): та же объёмная звезда, что на карте
    /// (AstroStarModel — кипящая поверхность и корона), в своей маленькой студии далеко от карты.
    /// Камера рисует в RenderTexture, пока привязанная картинка видна; картинка выводится с премультиплицированной альфой.
    /// </summary>
    public sealed class StarPortrait : MonoBehaviour
    {
        private const int Size = 256;
        private const float ModelRadius = 1f;
        private static int s_count;
        private static Material s_uiMat;

        private Camera _cam;
        private RenderTexture _rt;
        private Transform _stage;
        private GameObject _model;
        private RawImage _img;
        private int _systemId = -1, _layer;

        /// <summary>Создать студию и картинку портрета внутри parent.</summary>
        public static StarPortrait Create(Transform parent, string name)
        {
            var go = new GameObject("[Studio] StarPortrait_" + name);
            DontDestroyOnLoad(go);
            var p = go.AddComponent<StarPortrait>();
            p.Init(parent, name);
            return p;
        }

        public RawImage Image => _img;

        private void Init(Transform parent, string name)
        {
            _layer = LayerMask.NameToLayer("PlanetHolo");
            if (_layer < 0) _layer = 8;
            transform.position = new Vector3(0f, -6000f - 100f * s_count++, 0f);

            _rt = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32)
            { name = "StarPortraitRT_" + name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

            var camGo = new GameObject("StarPortraitCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -30f);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = ModelRadius * 1.04f;   // в кадре только шар; свечение вокруг рисует сам интерфейс
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 60f;
            _cam.cullingMask = 1 << _layer;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.depth = -60;
            _cam.targetTexture = _rt;
            _cam.enabled = false;

            _stage = new GameObject("Stage").transform;
            _stage.SetParent(transform, false);

            var imgGo = new GameObject("StarPortrait", typeof(RectTransform));
            imgGo.transform.SetParent(parent, false);
            _img = imgGo.AddComponent<RawImage>();
            _img.texture = _rt;
            _img.raycastTarget = false;
            if (s_uiMat == null)
            {
                var sh = Resources.Load<Shader>("Shaders/UIPremultiplied");
                if (sh != null) s_uiMat = new Material(sh) { name = "StarPortraitUI" };
            }
            if (s_uiMat != null) _img.material = s_uiMat;
            imgGo.SetActive(false);

            // Главные камеры этот слой не видят (как у студии планет)
            int bit = 1 << _layer;
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
                if (cam.targetTexture == null) cam.cullingMask &= ~bit;
        }

        /// <summary>
        /// Показать звезду системы. false — объёмной звезды нет (чёрная дыра, нет шейдера):
        /// картинка скрыта, вызывающий оставляет нарисованную звезду.
        /// </summary>
        public bool Show(StarSystem s)
        {
            if (s == null) { Hide(); return false; }
            if (s.Id != _systemId || _model == null)
            {
                Clear();
                _model = AstroStarModel.Build(_stage, s, ModelRadius);
                if (_model != null)
                {
                    _systemId = s.Id;
                    Strip(_model);
                    SetLayer(_model, _layer);
                }
            }
            bool ok = _model != null;
            _img.gameObject.SetActive(ok);
            return ok;
        }

        public void Hide()
        {
            if (_img != null) _img.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            // Камера работает, только пока портрет на экране — поверхность звезды «кипит» вживую
            bool on = _model != null && _img != null && _img.isActiveAndEnabled;
            if (_cam.enabled != on) _cam.enabled = on;
        }

        /// <summary>
        /// Убрать то, что завязано на главную камеру (поворот короны, блик объектива), и саму корону:
        /// это квадрат-билборд, в маленьком кадре его края видны квадратом. Свечение вокруг шара рисует интерфейс.
        /// </summary>
        private static void Strip(GameObject model)
        {
            var corona = model.transform.Find("Corona");
            if (corona != null)
            {
                var cr = corona.GetComponent<Renderer>();
                if (cr != null && cr.sharedMaterial != null) Destroy(cr.sharedMaterial);
                corona.gameObject.SetActive(false);
                Destroy(corona.gameObject);
            }
            foreach (var b in model.GetComponentsInChildren<BillboardLookAt>(true)) Destroy(b);
            foreach (var f in model.GetComponentsInChildren<StarFlare>(true)) Destroy(f);
            foreach (var f in model.GetComponentsInChildren<UnityEngine.Rendering.LensFlareComponentSRP>(true)) Destroy(f);
            foreach (Transform t in model.transform) t.localRotation = Quaternion.identity;   // корона — лицом к камере студии
        }

        private void Clear()
        {
            if (_model == null) return;
            foreach (var r in _model.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial != null) Destroy(r.sharedMaterial);
            Destroy(_model);
            _model = null;
            _systemId = -1;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }

        private void OnDestroy()
        {
            Clear();
            if (_img != null) Destroy(_img.gameObject);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
