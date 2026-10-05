using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Параллакс звёздной пыли в 3 слоя.
    ///   • Дальний слой почти статичен (ощущение бесконечности)
    ///   • Средний слегка двигается
    ///   • Ближний двигается быстрее всего
    /// Слои держатся «под камерой» через формулу pos = camPos * (1 - factor),
    /// поэтому при движении камеры видно разницу скоростей.
    /// </summary>
    public class GalaxyParallaxDust : MonoBehaviour
    {
        [Header("Общие настройки")]
        [SerializeField] private bool enableDust = true;
        [SerializeField] private float quadSize = 900f;
        [SerializeField] private float followSpeed = 4f;
        [SerializeField] private int textureResolution = 512;
        [SerializeField] private float tiling = 3f;

        [Header("Слои (3 штуки)")]
        [Tooltip("Высота слоя. Отрицательные значения — под плоскостью галактики")]
        [SerializeField] private float[] layerYPositions = new float[] { -4f, -8f, -14f };

        [Tooltip("Коэффициент параллакса: 0 = бесконечно далеко, 1 = держится за камерой")]
        [SerializeField] private float[] parallaxFactors = new float[] { 0.12f, 0.30f, 0.55f };

        [Tooltip("Прозрачность слоя. Дальние — плотнее, ближние — легче")]
        [SerializeField] private float[] alphas = new float[] { 0.55f, 0.45f, 0.30f };

        [Header("Цвета слоёв")]
        [SerializeField] private Color[] layerTints = new Color[]
        {
            new Color(0.55f, 0.65f, 1.00f),  // дальний — холодный синий
            new Color(0.75f, 0.65f, 0.95f),  // средний — фиолетовый
            new Color(0.95f, 0.75f, 0.65f),  // ближний — тёплый оранжевый
        };

        private Transform _cameraTransform;
        private readonly List<DustLayer> _layers = new List<DustLayer>();

        private class DustLayer
        {
            public Transform Transform;
            public Material Material;
            public float ParallaxFactor;
            public float YPosition;
            public Color BaseColor;
        }

        private void Start()
        {
            if (!enableDust) return;
            if (Camera.main != null) _cameraTransform = Camera.main.transform;
            CreateLayers();
        }

        private void CreateLayers()
        {
            int count = Mathf.Max(layerYPositions.Length,
                        Mathf.Max(parallaxFactors.Length, alphas.Length));

            for (int i = 0; i < count; i++)
            {
                float y = i < layerYPositions.Length ? layerYPositions[i] : -4f * (i + 1);
                float factor = i < parallaxFactors.Length ? parallaxFactors[i] : 0.15f * (i + 1);
                float alpha = i < alphas.Length ? alphas[i] : 0.4f;
                Color tint = i < layerTints.Length ? layerTints[i] : Color.white;

                // Объект слоя
                var go = new GameObject($"DustLayer_{i}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(0f, y, 0f);
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = new Vector3(quadSize, quadSize, 1f);

                // Mesh — Quad
                var mf = go.AddComponent<MeshFilter>();
                mf.mesh = CreateQuadMesh();

                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                // Текстура — генерируется процедурно для каждого слоя
                Texture2D tex = GenerateDustTexture(1000 + i * 17, i);
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Bilinear;

                // Материал
                Shader shader = ShaderCache.Sprite ?? Shader.Find("Sprites/Default");
                var mat = new Material(shader);

                if (mat.HasProperty("_MainTex")) mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                mat.mainTextureScale = new Vector2(tiling, tiling);

                Color finalColor = new Color(tint.r, tint.g, tint.b, alpha);
                if (mat.HasProperty("_Color")) mat.color = finalColor;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", finalColor);

                mr.material = mat;
                mr.sortingOrder = -200 + i;   // дальний рисуется первым

                _layers.Add(new DustLayer
                {
                    Transform = go.transform,
                    Material = mat,
                    ParallaxFactor = factor,
                    YPosition = y,
                    BaseColor = finalColor,
                });
            }
        }

        private void LateUpdate()
        {
            if (!enableDust || _layers.Count == 0) return;

            if (_cameraTransform == null)
            {
                if (Camera.main != null) _cameraTransform = Camera.main.transform;
                else return;
            }

            Vector3 camPos = _cameraTransform.position;
            float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);

            foreach (var layer in _layers)
            {
                // Формула:
                //   factor = 0 → слой двигается вместе с камерой (стоит на месте на экране)
                //   factor = 1 → слой стоит в мировых координатах (двигается на экране 1:1)
                // Нам нужно промежуточное: чем дальше слой, тем меньше factor.
                Vector3 target = new Vector3(
                    camPos.x * (1f - layer.ParallaxFactor),
                    layer.YPosition,
                    camPos.z * (1f - layer.ParallaxFactor)
                );

                layer.Transform.position = Vector3.Lerp(layer.Transform.position, target, t);
            }
        }

        private Mesh CreateQuadMesh()
        {
            var mesh = new Mesh { name = "DustQuad" };
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
            };
            mesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Процедурная текстура пыли:
        ///   • крупные мягкие облака (туманность)
        ///   • россыпь мелких ярких точек (звёздная пыль)
        /// Текстура тайлится по краям — координаты берутся по модулю.
        /// </summary>
        private Texture2D GenerateDustTexture(int seed, int layerIndex)
        {
            int res = textureResolution;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            var pixels = new Color[res * res];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            Random.InitState(seed);

            Color dustColor = new Color(0.9f, 0.95f, 1f);

            // ============ ОБЛАКА ============
            int cloudCount = 26 + layerIndex * 10;
            for (int i = 0; i < cloudCount; i++)
            {
                float cx = Random.Range(0f, res);
                float cy = Random.Range(0f, res);
                float size = Random.Range(40f, 110f);
                float brightness = Random.Range(0.04f, 0.14f);

                int r = Mathf.CeilToInt(size);
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int px = ((int)cx + dx + res * 2) % res;
                    int py = ((int)cy + dy + res * 2) % res;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > size) continue;

                    float falloff = Mathf.Pow(1f - d / size, 2.2f);
                    float a = falloff * brightness;

                    int idx = py * res + px;
                    Color existing = pixels[idx];
                    float newA = Mathf.Min(1f, existing.a + a);
                    if (newA > existing.a)
                        pixels[idx] = new Color(1f, 1f, 1f, newA);
                }
            }

            // ============ ЗВЁЗДНАЯ ПЫЛЬ ============
            int dustCount = 500 + layerIndex * 250;
            for (int i = 0; i < dustCount; i++)
            {
                int cx = Random.Range(0, res);
                int cy = Random.Range(0, res);
                float brightness = Random.Range(0.10f, 0.45f);

                // 85% точек — пиксель, 15% — плюс-крест
                bool plus = Random.value > 0.85f;
                if (plus)
                {
                    AddPoint(pixels, res, cx, cy, brightness);
                    AddPoint(pixels, res, cx + 1, cy, brightness * 0.6f);
                    AddPoint(pixels, res, cx - 1, cy, brightness * 0.6f);
                    AddPoint(pixels, res, cx, cy + 1, brightness * 0.6f);
                    AddPoint(pixels, res, cx, cy - 1, brightness * 0.6f);
                }
                else
                {
                    AddPoint(pixels, res, cx, cy, brightness);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private void AddPoint(Color[] pixels, int res, int x, int y, float brightness)
        {
            int px = ((x % res) + res) % res;
            int py = ((y % res) + res) % res;
            int idx = py * res + px;
            Color existing = pixels[idx];
            float newA = Mathf.Min(1f, existing.a + brightness);
            // пыль — холодный белый
            pixels[idx] = new Color(
                Mathf.Lerp(existing.r, 0.92f, 0.7f),
                Mathf.Lerp(existing.g, 0.96f, 0.7f),
                Mathf.Lerp(existing.b, 1f, 0.7f),
                newA);
        }

        // ==================== ПУБЛИЧНОЕ УПРАВЛЕНИЕ ====================

        public void SetEnabled(bool value)
        {
            enableDust = value;
            foreach (var layer in _layers)
                if (layer.Transform != null)
                    layer.Transform.gameObject.SetActive(value);
        }

        public void SetLayerAlpha(int index, float alpha)
        {
            if (index < 0 || index >= _layers.Count) return;
            var layer = _layers[index];
            layer.BaseColor.a = alpha;
            if (layer.Material != null)
            {
                if (layer.Material.HasProperty("_Color")) layer.Material.color = layer.BaseColor;
                if (layer.Material.HasProperty("_BaseColor")) layer.Material.SetColor("_BaseColor", layer.BaseColor);
            }
        }

        [ContextMenu("Rebuild Dust Layers")]
        private void RebuildLayers()
        {
            foreach (var layer in _layers)
                if (layer.Transform != null) Destroy(layer.Transform.gameObject);
            _layers.Clear();
            CreateLayers();
        }
    }
}