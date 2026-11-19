#if ASTROPOL_DLSS
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Адаптер стороннего плагина DLSS для URP. Включается define-символом ASTROPOL_DLSS
    /// (Project Settings → Player → Scripting Define Symbols) после установки плагина.
    /// Вызовы плагина ниже — заготовка: подставить API конкретного плагина в места с TODO.
    /// </summary>
    public sealed class DlssBackend : IDlssBackend
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => GraphicsQuality.Dlss = new DlssBackend();

        public bool Supported
        {
            get
            {
                // TODO: проверка плагина, например: return DLSSPlugin.IsSupported();
                return SystemInfo.graphicsDeviceVendorID == 0x10DE; // NVIDIA
            }
        }

        public void Apply(Camera cam, bool enabled, float renderScale, float sharpness)
        {
            if (cam == null) return;
            // TODO: включить/выключить компонент или фичу плагина на камере, например:
            // var d = cam.GetComponent<DLSSCamera>() ?? cam.gameObject.AddComponent<DLSSCamera>();
            // d.enabled = enabled;
            // d.Quality = renderScale >= 0.999f ? DLAA : renderScale >= 0.66f ? Quality : renderScale >= 0.58f ? Balanced : Performance;
            // d.Sharpness = sharpness;
        }
    }
}
#endif
