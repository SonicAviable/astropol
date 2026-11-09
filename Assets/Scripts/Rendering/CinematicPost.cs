using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Кинематографичная постобработка: глобальный Volume создаётся в коде (ассеты не трогаем) и перекрывает
    /// DefaultVolumeProfile, где Bloom, виньетка и зерно стоят на нуле. Свечение звёзд и ядра, ACES-тонирование,
    /// холодные тени и тёплые света, мягкая виньетка, лёгкая хроматика и зерно.
    /// «Постобработка» в настройках графики гасит Volume (вес 0) — следим за GraphicsQuality.PostFx.
    /// Значения подбираются на глаз: правятся константами ниже.
    /// </summary>
    public sealed class CinematicPost : MonoBehaviour
    {
        // ---- Подстройка ----
        private const float BloomThreshold = 1.0f;     // ниже — светятся и яркие спрайты; выше — только HDR-звёзды и ядро
        private const float BloomIntensity = 0.85f;
        private const float BloomScatter = 0.72f;
        private const float PostExposure = 0.15f;
        private const float Contrast = 12f;
        private const float Saturation = 8f;
        private const float VignetteIntensity = 0.28f;
        private const float VignetteSmoothness = 0.45f;
        private const float Chromatic = 0.07f;
        private const float Grain = 0.12f;
        private static readonly Color ShadowTint = new Color(0.50f, 0.56f, 0.78f);   // холодные тени
        private static readonly Color HighlightTint = new Color(0.92f, 0.82f, 0.66f); // тёплые света

        private static CinematicPost s_instance;
        private Volume _volume;
        private VolumeProfile _profile;
        private Camera _lastCam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (s_instance != null) return;
            var go = new GameObject("[Rendering] CinematicPost");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<CinematicPost>();
        }

        private void Awake()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.hideFlags = HideFlags.DontSave;

            var bloom = _profile.Add<Bloom>(true);
            bloom.threshold.Override(BloomThreshold);
            bloom.intensity.Override(BloomIntensity);
            bloom.scatter.Override(BloomScatter);
            bloom.clamp.Override(40f);
            bloom.tint.Override(Color.white);
            bloom.highQualityFiltering.Override(true);
            bloom.downscale.Override(BloomDownscaleMode.Half);

            var tone = _profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var color = _profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(PostExposure);
            color.contrast.Override(Contrast);
            color.saturation.Override(Saturation);

            var split = _profile.Add<SplitToning>(true);
            split.shadows.Override(ShadowTint);
            split.highlights.Override(HighlightTint);
            split.balance.Override(0f);

            var vig = _profile.Add<Vignette>(true);
            vig.color.Override(Color.black);
            vig.intensity.Override(VignetteIntensity);
            vig.smoothness.Override(VignetteSmoothness);
            vig.rounded.Override(false);

            var ca = _profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(Chromatic);

            var grain = _profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(Grain);
            grain.response.Override(0.8f);

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _volume.sharedProfile = _profile;
            _volume.weight = 1f;
        }

        private void LateUpdate()
        {
            bool on = GraphicsQuality.PostFx;
            _volume.weight = on ? 1f : 0f;

            // Новая основная камера: убедиться, что она ведёт проход постобработки и рисует в HDR (без HDR Bloom почти не виден)
            var cam = Camera.main;
            if (on && cam != null && cam != _lastCam && cam.targetTexture == null)
            {
                _lastCam = cam;
                cam.allowHDR = true;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null && data.renderType == CameraRenderType.Base) data.renderPostProcessing = true;
            }
        }

        private void OnDestroy()
        {
            if (_profile != null) Destroy(_profile);
            if (s_instance == this) s_instance = null;
        }
    }
}
