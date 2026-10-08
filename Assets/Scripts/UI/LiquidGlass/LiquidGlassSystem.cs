using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Сердце Liquid Glass:
    ///   • каждый кадр размывает картинку камеры (dual Kawase, 1/4 разрешения) и отдаёт её
    ///     в глобальную текстуру _LGBackdropTex — из неё стекло берёт "то, что за панелью";
    ///   • ведёт виртуальный источник света (_LGLightDir) — блики плавно плывут и следуют за курсором;
    ///   • опрашивает стеклянные элементы (синхронизация с Outline, смена спрайта);
    ///   • страхует авто-скиннингом: всё, что создано в коде без явного стиля, получает стекло.
    /// Создаётся автоматически, руками добавлять не нужно.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class LiquidGlassSystem : MonoBehaviour
    {
        public static LiquidGlassSystem Instance { get; private set; }

        [Header("Backdrop blur")]
        [Tooltip("Размывать сцену под стеклом. Выключите — стекло станет тонированным без блюра.")]
        public bool backdropEnabled = true;
        [Tooltip("Если размытый фон под стеклом перевёрнут — включите.")]
        public bool flipBackdrop = false;
        [Range(0f, 1f)] public float vibrancy = 0.25f;
        [Tooltip("Сила неонового свечения вокруг стекла (0 — без ореола).")]
        [Range(0f, 1.5f)] public float glowStrength = 0.2f;

        [Header("Light")]
        [Tooltip("Базовый угол света в градусах (90 — сверху, 135 — сверху-слева).")]
        public float lightBaseAngle = 118f;
        public float lightSway = 12f;
        public float pointerInfluence = 22f;

        [Header("Auto skin")]
        public bool autoSkin = true;
        public float scanInterval = 0.25f;

        private static readonly int BackdropTexId = Shader.PropertyToID("_LGBackdropTex");
        private static readonly int BackdropReadyId = Shader.PropertyToID("_LGBackdropReady");
        private static readonly int BackdropFlipId = Shader.PropertyToID("_LGBackdropFlip");
        private static readonly int LightDirId = Shader.PropertyToID("_LGLightDir");
        private static readonly int PointerId = Shader.PropertyToID("_LGPointer");
        private static readonly int VibrancyId = Shader.PropertyToID("_LGVibrancy");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_LGGlowStrength");

        private LiquidGlassBackdropPass _pass;
        private float _scanTimer;
        private float _canvasRefreshTimer;
        private readonly List<Canvas> _canvases = new List<Canvas>();
        private Vector2 _smoothPointer = new Vector2(0.5f, 0.5f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => Ensure();

        public static LiquidGlassSystem Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[LiquidGlass]");
            DontDestroyOnLoad(go);
            return go.AddComponent<LiquidGlassSystem>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            Shader.SetGlobalTexture(BackdropTexId, Texture2D.blackTexture);
            Shader.SetGlobalFloat(BackdropReadyId, 0f);
            _pass = new LiquidGlassBackdropPass(BackdropTexId);
            PushGlobals(0f);
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Shader.SetGlobalFloat(BackdropReadyId, 0f);
        }

        private void OnDestroy()
        {
            _pass?.Dispose();
            _pass = null;
            if (Instance == this) Instance = null;
        }

        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!backdropEnabled || _pass == null || !_pass.IsValid) return;
            if (cam.cameraType != CameraType.Game) return;
            if (cam.targetTexture != null) return; // голо-камеры планет и т.п.

            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(_pass);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            PushGlobals(dt);
            LiquidGlassEffect.TickAll();

            if (!autoSkin) return;

            _canvasRefreshTimer -= dt;
            if (_canvasRefreshTimer <= 0f)
            {
                _canvasRefreshTimer = 2f;
                _canvases.Clear();
                _canvases.AddRange(FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            }

            _scanTimer -= dt;
            if (_scanTimer > 0f) return;
            _scanTimer = scanInterval;

            for (int i = 0; i < _canvases.Count; i++)
            {
                var c = _canvases[i];
                if (c == null || !c.isRootCanvas) continue;
                if (c.renderMode == RenderMode.WorldSpace) continue;
                LG.Skin(c.transform);
            }
        }

        private void PushGlobals(float dt)
        {
            bool ready = backdropEnabled && _pass != null && _pass.IsValid
                         && Time.frameCount - _pass.LastRecordedFrame <= 2;
            Shader.SetGlobalFloat(BackdropReadyId, ready ? 1f : 0f);
            Shader.SetGlobalFloat(BackdropFlipId, flipBackdrop ? 1f : 0f);
            Shader.SetGlobalFloat(VibrancyId, vibrancy);
            Shader.SetGlobalFloat(GlowStrengthId, glowStrength);

            Vector2 ptr = new Vector2(0.5f, 0.5f);
            if (Screen.width > 0 && Screen.height > 0)
            {
                Vector3 m = Input.mousePosition;
                ptr = new Vector2(m.x / Screen.width, m.y / Screen.height);
            }

            // Курсор — мгновенно для бликов под ним, сглаженно — для направления света
            _smoothPointer = dt > 0f
                ? Vector2.Lerp(_smoothPointer, ptr, 1f - Mathf.Exp(-dt * 3f))
                : ptr;

            float t = Time.unscaledTime;
            float angle = lightBaseAngle
                          + Mathf.Sin(t * 0.31f) * lightSway
                          + Mathf.Sin(t * 0.13f + 1.7f) * lightSway * 0.5f
                          - (_smoothPointer.x - 0.5f) * 2f * pointerInfluence;
            float rad = angle * Mathf.Deg2Rad;

            Shader.SetGlobalVector(LightDirId, new Vector4(Mathf.Cos(rad), Mathf.Sin(rad), 0f, 0f));
            Shader.SetGlobalVector(PointerId, new Vector4(ptr.x, ptr.y, 0f, 0f));
        }
    }

    /// <summary>
    /// Render Graph-проход URP: копирует цвет камеры после пост-обработки и размывает его
    /// в постоянную текстуру _LGBackdropTex, которую затем читает UI-шейдер стекла.
    /// </summary>
    internal sealed class LiquidGlassBackdropPass : ScriptableRenderPass, System.IDisposable
    {
        private sealed class PassData
        {
            public TextureHandle Source;
            public Material Material;
            public int ShaderPass;
        }

        private static readonly int BlurParamsId = Shader.PropertyToID("_LGBlurParams");
        private const int StepCount = 6;

        private readonly int _globalTexId;
        private readonly Material[] _materials = new Material[StepCount];
        private readonly GraphicsFormat _format;
        private RenderTexture _finalRT;
        private RTHandle _final;

        public int LastRecordedFrame { get; private set; } = -100;
        public bool IsValid => _materials[0] != null;

        public LiquidGlassBackdropPass(int globalTexId)
        {
            _globalTexId = globalTexId;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;

            var shader = Resources.Load<Shader>("LiquidGlass/LiquidGlassBlur");
            if (shader == null) shader = Shader.Find("Hidden/Astropolity/LiquidGlassBlur");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("[LiquidGlass] Шейдер размытия недоступен — стекло будет без блюра фона.");
                return;
            }

            for (int i = 0; i < StepCount; i++)
                _materials[i] = CoreUtils.CreateEngineMaterial(shader);

            _format = SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormatUsage.Render)
                ? GraphicsFormat.B10G11R11_UFloatPack32
                : GraphicsFormat.R8G8B8A8_UNorm;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!IsValid) return;

            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;

            TextureHandle source = resources.activeColorTexture;
            if (!source.IsValid()) return;

            var cameraData = frameData.Get<UniversalCameraData>();
            int w = Mathf.Max(1, cameraData.cameraTargetDescriptor.width);
            int h = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);

            int w2 = Mathf.Max(1, w >> 1),  h2 = Mathf.Max(1, h >> 1);
            int w4 = Mathf.Max(1, w >> 2),  h4 = Mathf.Max(1, h >> 2);
            int w8 = Mathf.Max(1, w >> 3),  h8 = Mathf.Max(1, h >> 3);
            int w16 = Mathf.Max(1, w >> 4), h16 = Mathf.Max(1, h >> 4);

            EnsureFinal(w4, h4);

            var down1 = Create(renderGraph, w2, h2, "LG_Down_1/2");
            var down2 = Create(renderGraph, w4, h4, "LG_Down_1/4");
            var down3 = Create(renderGraph, w8, h8, "LG_Down_1/8");
            var down4 = Create(renderGraph, w16, h16, "LG_Down_1/16");
            var up3   = Create(renderGraph, w8, h8, "LG_Up_1/8");
            var final = renderGraph.ImportTexture(_final);

            AddBlit(renderGraph, source, down1, 0, 0, w, h, 1.0f);
            AddBlit(renderGraph, down1, down2, 1, 0, w2, h2, 1.0f);
            AddBlit(renderGraph, down2, down3, 2, 0, w4, h4, 1.25f);
            AddBlit(renderGraph, down3, down4, 3, 0, w8, h8, 1.5f);
            AddBlit(renderGraph, down4, up3, 4, 1, w16, h16, 1.5f);
            AddBlit(renderGraph, up3, final, 5, 1, w8, h8, 1.25f);

            LastRecordedFrame = Time.frameCount;
        }

        private TextureHandle Create(RenderGraph rg, int w, int h, string name)
        {
            var desc = new TextureDesc(w, h)
            {
                format = _format,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = false,
                name = name
            };
            return rg.CreateTexture(desc);
        }

        private void AddBlit(RenderGraph rg, TextureHandle src, TextureHandle dst, int step, int shaderPass,
                             int srcW, int srcH, float offset)
        {
            var mat = _materials[step];
            mat.SetVector(BlurParamsId, new Vector4(1f / srcW, 1f / srcH, offset, 0f));

            using (var builder = rg.AddRasterRenderPass<PassData>("LiquidGlass Backdrop Blur", out var data))
            {
                data.Source = src;
                data.Material = mat;
                data.ShaderPass = shaderPass;

                builder.UseTexture(src, AccessFlags.Read);
                builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, d.ShaderPass);
                });
            }
        }

        private void EnsureFinal(int w, int h)
        {
            if (_finalRT != null && _finalRT.width == w && _finalRT.height == h && _finalRT.IsCreated())
                return;

            ReleaseFinal();

            _finalRT = new RenderTexture(w, h, _format, GraphicsFormat.None)
            {
                name = "_LGBackdropTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            _finalRT.Create();
            _final = RTHandles.Alloc(_finalRT);
            Shader.SetGlobalTexture(_globalTexId, _finalRT);
        }

        private void ReleaseFinal()
        {
            // RTHandle лишь оборачивает наш RenderTexture — им владеем мы, освобождаем сами.
            _final = null;
            if (_finalRT != null)
            {
                _finalRT.Release();
                Object.Destroy(_finalRT);
                _finalRT = null;
            }
        }

        public void Dispose()
        {
            ReleaseFinal();
            for (int i = 0; i < _materials.Length; i++)
            {
                if (_materials[i] != null) CoreUtils.Destroy(_materials[i]);
                _materials[i] = null;
            }
            Shader.SetGlobalTexture(_globalTexId, Texture2D.blackTexture);
        }
    }
}
