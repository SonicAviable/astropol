using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Прогрев шейдеров на экране загрузки. Шейдер компилируется драйвером при первой отрисовке — без прогрева
    /// это фриз в момент, когда впервые появляется пират, взрыв, планета или окно события. Здесь скрытая камера
    /// (далеко под картой, свой слой) по разу рисует в крошечную текстуру квад с каждым шейдером игры
    /// и с теми вариантами URP Lit, которые включает код (_EMISSION, _NORMALMAP, …). Раз за запуск игры.
    /// </summary>
    public static class ShaderPrewarm
    {
        private const int Layer = 31;                 // свободный слой (30 — туман войны)
        private const int PerFrame = 12;              // квадов за кадр: полоса загрузки не замирает
        private static readonly Vector3 Site = new Vector3(0f, -50000f, 0f);

        private static bool s_started, s_finished;
        private static int s_done, s_total;

        /// <summary>Доля прогретых шейдеров (0..1) — для экрана загрузки. Не начинался или уже был — 1.</summary>
        public static float Progress => !s_started || s_finished || s_total <= 0 ? 1f : Mathf.Clamp01(s_done / (float)s_total);
        public static int Done => s_done;
        public static int Total => s_total;
        public static bool Running => s_started && !s_finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_started = s_finished = false; s_done = s_total = 0; }

        /// <summary>Запустить прогрев (один раз за сеанс; повторные вызовы ничего не делают).</summary>
        public static void Begin()
        {
            if (s_started || Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            s_started = true;
            var host = new GameObject("[Render] ShaderPrewarm");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<Runner>();
        }

        private sealed class Runner : MonoBehaviour
        {
            private readonly List<Material> _mats = new List<Material>();

            private void Start() => StartCoroutine(Run());

            private IEnumerator Run()
            {
                // Ждём камеру и свет сцены: варианты освещения зависят от них
                for (int i = 0; i < 120 && Camera.main == null; i++) yield return null;

                CollectMaterials();
                s_total = _mats.Count;
                if (s_total == 0) { Finish(); yield break; }

                var rt = new RenderTexture(64, 64, 16) { name = "ShaderPrewarm", hideFlags = HideFlags.DontSave };
                var camGo = new GameObject("PrewarmCamera");
                camGo.transform.SetParent(transform, false);
                camGo.transform.SetPositionAndRotation(Site + Vector3.back * 3f, Quaternion.identity);
                var cam = camGo.AddComponent<Camera>();
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 10f;
                cam.fieldOfView = 60f;
                cam.targetTexture = rt;
                cam.depth = -100f;
                cam.allowHDR = true;
                cam.allowMSAA = false;
                HideLayerFromOtherCameras(cam);

                var mesh = QuadMesh();
                var batch = new List<GameObject>();
                for (int i = 0; i < _mats.Count; i += PerFrame)
                {
                    foreach (var go in batch) Destroy(go);
                    batch.Clear();
                    int end = Mathf.Min(_mats.Count, i + PerFrame);
                    for (int k = i; k < end; k++) batch.Add(Spawn(mesh, _mats[k], k - i));
                    HideLayerFromOtherCameras(cam);
                    yield return null;          // кадр: камера рисует пачку — драйвер компилирует шейдеры
                    s_done = end;
                }
                yield return null;

                foreach (var go in batch) Destroy(go);
                cam.targetTexture = null;
                Destroy(camGo);
                rt.Release();
                Destroy(rt);
                Destroy(mesh);
                foreach (var m in _mats) if (m != null) Destroy(m);
                _mats.Clear();
                Finish();
            }

            private void Finish()
            {
                s_done = s_total;
                s_finished = true;
                Debug.Log($"[ShaderPrewarm] Прогрето материалов: {s_total}");
                Destroy(gameObject);
            }

            private GameObject Spawn(Mesh mesh, Material m, int slot)
            {
                var go = new GameObject("Warm_" + m.shader.name) { layer = Layer };
                go.transform.SetParent(transform, false);
                // Сетка 4×3 перед камерой, все квады в кадре
                go.transform.position = Site + new Vector3((slot % 4 - 1.5f) * 0.8f, (slot / 4 - 1f) * 0.8f, 0f);
                go.transform.localScale = Vector3.one * 0.7f;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;
                return go;
            }

            private static void HideLayerFromOtherCameras(Camera mine)
            {
                int bit = 1 << Layer;
                foreach (var c in Camera.allCameras)
                    if (c != mine && (c.cullingMask & bit) != 0) c.cullingMask &= ~bit;
            }

            // ---------------------------------------------------------------- что греть

            private void CollectMaterials()
            {
                var seen = new HashSet<Shader>();

                // Все свои шейдеры (Resources/Shaders) — в состоянии по умолчанию
                foreach (var sh in Resources.LoadAll<Shader>("Shaders")) AddShader(sh, seen);

                // Шейдеры, которые код ищет по имени
                string[] named =
                {
                    "Sprites/Default", "UI/Default", "Universal Render Pipeline/Unlit",
                    "Universal Render Pipeline/2D/Sprite-Unlit-Default", "Universal Render Pipeline/Particles/Unlit",
                    "Stellaris/OrganicBorders", "Astropolity/UI/PlanetDisc", "Astropolity/UI/LiquidGlass",
                    "Astropolity/Star/Surface", "Astropolity/Star/Fx", "Astropolity/Star/Corona",
                    "Astropolity/Planet/Atmosphere", "Astropolity/Planet/Clouds", "Astropolity/Planet/NightLights",
                    "Astropolity/Planet/AstroRing", "Astropolity/Planet/Astro",
                };
                foreach (var n in named) AddShader(Shader.Find(n), seen);
                AddShader(ShaderCache.Unlit, seen);
                AddShader(ShaderCache.Sprite, seen);

                // URP Lit — варианты с ключевыми словами, которые включают модели кораблей, станций и планет
                var lit = ShaderCache.Lit;
                if (lit != null && lit.isSupported)
                {
                    string[][] combos =
                    {
                        new string[0],
                        new[] { "_EMISSION" },
                        new[] { "_NORMALMAP" },
                        new[] { "_NORMALMAP", "_EMISSION" },
                        new[] { "_METALLICSPECGLOSSMAP" },
                        new[] { "_METALLICSPECGLOSSMAP", "_NORMALMAP" },
                        new[] { "_METALLICSPECGLOSSMAP", "_NORMALMAP", "_EMISSION" },
                        new[] { "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP", "_NORMALMAP", "_EMISSION" },
                        new[] { "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP", "_NORMALMAP" },
                        new[] { "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A" },
                        new[] { "_ALPHATEST_ON" },
                    };
                    foreach (var c in combos)
                    {
                        var m = new Material(lit) { name = "Warm_Lit", hideFlags = HideFlags.DontSave };
                        foreach (var kw in c) m.EnableKeyword(kw);
                        if (System.Array.IndexOf(c, "_ALPHATEST_ON") >= 0 && m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
                        _mats.Add(m);
                    }
                }
            }

            private void AddShader(Shader sh, HashSet<Shader> seen)
            {
                if (sh == null || !sh.isSupported || !seen.Add(sh)) return;
                if (sh == ShaderCache.Lit) return;    // Lit — отдельными вариантами ниже
                _mats.Add(new Material(sh) { name = "Warm_" + sh.name, hideFlags = HideFlags.DontSave });
            }

            /// <summary>Квад с теми же каналами, что у моделей (позиция, нормаль, касательная, UV, цвет).</summary>
            private static Mesh QuadMesh()
            {
                var m = new Mesh { name = "PrewarmQuad", hideFlags = HideFlags.DontSave };
                m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
                m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                var tan = new Vector4(1f, 0f, 0f, 1f);
                m.tangents = new[] { tan, tan, tan, tan };
                m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                m.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
                m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
