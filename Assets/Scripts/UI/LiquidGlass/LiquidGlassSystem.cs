using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Служба стиля интерфейса:
    ///   • выставляет глобальные параметры UI-шейдера (сила ореола, яркость панелей);
    ///   • опрашивает элементы (синхронизация с Outline, смена спрайта);
    ///   • страхует авто-скиннингом: всё, что создано в коде без явного стиля, получает оформление.
    /// Размытия фона, преломления и бликов больше нет — интерфейс плоский и непрозрачный.
    /// Создаётся автоматически, руками добавлять не нужно.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class LiquidGlassSystem : MonoBehaviour
    {
        public static LiquidGlassSystem Instance { get; private set; }

        [Tooltip("Сила ореола вокруг линий и выделенных элементов (0 — без ореола).")]
        [Range(0f, 1.5f)] public float glowStrength = 0.2f;
        [Tooltip("Насколько светлее тёмные панели (0 — исходный тёмный вид).")]
        [Range(0f, 1f)] public float brightness = 0.5f;

        [Header("Auto skin")]
        public bool autoSkin = true;
        public float scanInterval = 0.25f;

        private static readonly int GlowStrengthId = Shader.PropertyToID("_LGGlowStrength");
        private static readonly int BrightnessId = Shader.PropertyToID("_LGBrightness");

        private float _scanTimer;
        private float _canvasRefreshTimer;
        private readonly List<Canvas> _canvases = new List<Canvas>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => Ensure();

        public static LiquidGlassSystem Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[UIStyle]");
            DontDestroyOnLoad(go);
            return go.AddComponent<LiquidGlassSystem>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            PushGlobals();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            PushGlobals();
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

        private void PushGlobals()
        {
            Shader.SetGlobalFloat(GlowStrengthId, glowStrength);
            Shader.SetGlobalFloat(BrightnessId, brightness);
        }
    }
}
