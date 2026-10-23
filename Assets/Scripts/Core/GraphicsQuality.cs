using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StellarisClone.Core
{
    /// <summary>
    /// Настройки графики: пресет («Низкое» … «Ультра») и отдельные пункты — масштаб рендера, сглаживание,
    /// тени, постобработка, текстуры. Меняют активный ассет URP на лету и камеры сцены; интерфейс не трогают
    /// (он рисуется поверх в полном разрешении).
    ///
    /// Ассет URP правится на месте: в сборке это лишь память процесса, а в редакторе исходные значения
    /// запоминаются и возвращаются при выходе из Play Mode, чтобы настройки игрока не попали в проект.
    /// </summary>
    public static class GraphicsQuality
    {
        public const int PresetCustom = 4;

        public static readonly float[] RenderScales = { 0.5f, 0.67f, 0.75f, 0.85f, 1f };
        /// <summary>Сглаживание: 0 выкл, 1 FXAA, 2 SMAA, 3 MSAA ×2, 4 MSAA ×4.</summary>
        public const int AAOff = 0, AAFxaa = 1, AASmaa = 2, AAMsaa2 = 3, AAMsaa4 = 4;
        /// <summary>Тени: 0 выкл … 4 ультра — разрешение карты и число каскадов.</summary>
        private static readonly int[] ShadowRes = { 0, 1024, 2048, 2048, 4096 };
        private static readonly int[] ShadowCascades = { 1, 1, 2, 4, 4 };

        public static string[] PresetNames => new[] { Loc.T("Низкое"), Loc.T("Среднее"), Loc.T("Высокое"), Loc.T("Ультра"), Loc.T("Своё") };
        public static string[] ScaleNames => new[] { "50%", "67%", "75%", "85%", "100%" };
        public static string[] AANames => new[] { Loc.T("Выкл"), "FXAA", "SMAA", "MSAA ×2", "MSAA ×4" };
        public static string[] ShadowNames => new[] { Loc.T("Выкл"), Loc.T("Низкие"), Loc.T("Средние"), Loc.T("Высокие"), Loc.T("Ультра") };
        public static string[] OnOffNames => new[] { Loc.T("Выкл"), Loc.T("Вкл") };
        public static string[] TextureNames => new[] { Loc.T("Половинные"), Loc.T("Полные") };

        public static int ScaleIndex { get; private set; } = 4;
        public static int AntiAliasing { get; private set; } = AASmaa;
        public static int Shadows { get; private set; } = 3;
        public static bool PostFx { get; private set; } = true;
        public static bool FullTextures { get; private set; } = true;

        private struct Set
        {
            public int Scale, AA, Shadows;
            public bool Post, Tex;
            public Set(int scale, int aa, int shadows, bool post, bool tex) { Scale = scale; AA = aa; Shadows = shadows; Post = post; Tex = tex; }
        }

        private static readonly Set[] Presets =
        {
            new Set(1, AAOff,   0, false, false),   // Низкое: 67%, без сглаживания, теней и постобработки
            new Set(3, AAFxaa,  1, true,  true),    // Среднее: 85%, FXAA, низкие тени
            new Set(4, AASmaa,  3, true,  true),    // Высокое: как было в игре до настроек
            new Set(4, AAMsaa4, 4, true,  true),    // Ультра: MSAA ×4, тени 4096
        };

        /// <summary>Какой пресет соответствует текущим пунктам (PresetCustom — набор свой).</summary>
        public static int CurrentPreset
        {
            get
            {
                for (int i = 0; i < Presets.Length; i++)
                {
                    var p = Presets[i];
                    if (p.Scale == ScaleIndex && p.AA == AntiAliasing && p.Shadows == Shadows && p.Post == PostFx && p.Tex == FullTextures) return i;
                }
                return PresetCustom;
            }
        }

        // ------------------------------------------------------------------ Загрузка и сохранение

        private static bool s_loaded;

        public static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;
            if (!PlayerPrefs.HasKey("gfx.scale"))
            {
                // Прежняя настройка — уровень качества Unity (0 «Mobile», 1 «PC») → пресет «Среднее» / «Высокое»
                int old = PlayerPrefs.GetInt("set.quality", 1);
                Use(Presets[old <= 0 ? 1 : 2]);
                return;
            }
            ScaleIndex = Mathf.Clamp(PlayerPrefs.GetInt("gfx.scale", 4), 0, RenderScales.Length - 1);
            AntiAliasing = Mathf.Clamp(PlayerPrefs.GetInt("gfx.aa", AASmaa), 0, AAMsaa4);
            Shadows = Mathf.Clamp(PlayerPrefs.GetInt("gfx.shadows", 3), 0, ShadowRes.Length - 1);
            PostFx = PlayerPrefs.GetInt("gfx.post", 1) == 1;
            FullTextures = PlayerPrefs.GetInt("gfx.tex", 1) == 1;
        }

        private static void Use(Set s)
        {
            ScaleIndex = s.Scale; AntiAliasing = s.AA; Shadows = s.Shadows; PostFx = s.Post; FullTextures = s.Tex;
        }

        private static void Save()
        {
            PlayerPrefs.SetInt("gfx.scale", ScaleIndex);
            PlayerPrefs.SetInt("gfx.aa", AntiAliasing);
            PlayerPrefs.SetInt("gfx.shadows", Shadows);
            PlayerPrefs.SetInt("gfx.post", PostFx ? 1 : 0);
            PlayerPrefs.SetInt("gfx.tex", FullTextures ? 1 : 0);
        }

        // ------------------------------------------------------------------ Сеттеры (применяют сразу)

        public static void SetPreset(int i)
        {
            EnsureLoaded();
            if (i < 0 || i >= Presets.Length) return;   // «Своё» — оставить как есть
            Use(Presets[i]);
            Changed();
        }

        public static void SetScale(int i) { EnsureLoaded(); ScaleIndex = Mathf.Clamp(i, 0, RenderScales.Length - 1); Changed(); }
        public static void SetAntiAliasing(int i) { EnsureLoaded(); AntiAliasing = Mathf.Clamp(i, 0, AAMsaa4); Changed(); }
        public static void SetShadows(int i) { EnsureLoaded(); Shadows = Mathf.Clamp(i, 0, ShadowRes.Length - 1); Changed(); }
        public static void SetPostFx(bool v) { EnsureLoaded(); PostFx = v; Changed(); }
        public static void SetFullTextures(bool v) { EnsureLoaded(); FullTextures = v; Changed(); }

        private static void Changed()
        {
            Save();
            Apply();
        }

        // ------------------------------------------------------------------ Применение

        /// <summary>Применить всё: ассет URP, текстуры, камеры и объёмы постобработки текущей сцены.</summary>
        public static void Apply()
        {
            EnsureLoaded();
            // Пресеты строятся поверх старшего уровня качества Unity («PC»): на нём ассет со всеми возможностями
            int top = QualitySettings.names.Length - 1;
            if (top >= 0 && QualitySettings.GetQualityLevel() != top)
            {
                bool vsync = QualitySettings.vSyncCount > 0;
                QualitySettings.SetQualityLevel(top, true);
                QualitySettings.vSyncCount = vsync ? 1 : 0;   // смена уровня сбрасывает выбор игрока
            }
            QualitySettings.globalTextureMipmapLimit = FullTextures ? 0 : 1;
            QualitySettings.anisotropicFiltering = FullTextures ? AnisotropicFiltering.Enable : AnisotropicFiltering.Disable;
            ApplyAsset();
            ApplyScene();
        }

        private static void ApplyAsset()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return;
            Remember(asset);
            asset.renderScale = RenderScales[Mathf.Clamp(ScaleIndex, 0, RenderScales.Length - 1)];
            asset.msaaSampleCount = AntiAliasing == AAMsaa4 ? 4 : AntiAliasing == AAMsaa2 ? 2 : 1;
            bool shadows = Shadows > 0;
            SetMember(asset, "supportsMainLightShadows", "m_MainLightShadowsSupported", shadows);
            if (shadows)
            {
                SetMember(asset, "mainLightShadowmapResolution", "m_MainLightShadowmapResolution", ShadowRes[Shadows]);
                asset.shadowCascadeCount = ShadowCascades[Shadows];
            }
        }

        /// <summary>Камеры сцены (кроме рендера в текстуру — витрины кораблей и планет) и объёмы постобработки.</summary>
        public static void ApplyScene()
        {
            bool postAA = AntiAliasing == AAFxaa || AntiAliasing == AASmaa;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam == null || cam.targetTexture != null) continue;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data == null || data.renderType != CameraRenderType.Base) continue;
                // FXAA и SMAA идут проходом постобработки — без неё камера их не сделает
                data.renderPostProcessing = PostFx || postAA;
                data.antialiasing = AntiAliasing == AAFxaa ? AntialiasingMode.FastApproximateAntialiasing
                                  : AntiAliasing == AASmaa ? AntialiasingMode.SubpixelMorphologicalAntiAliasing
                                  : AntialiasingMode.None;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
            // «Постобработка выкл» — гасим объёмы (свечение, тонирование, виньетка), даже если камера ведёт проход ради сглаживания
            var gone = new System.Collections.Generic.List<Volume>();
            foreach (var k in s_volumeWeights.Keys) if (k == null) gone.Add(k);
            foreach (var k in gone) s_volumeWeights.Remove(k);
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v == null) continue;
                if (!s_volumeWeights.ContainsKey(v)) s_volumeWeights[v] = v.weight;
                v.weight = PostFx ? s_volumeWeights[v] : 0f;
            }
            _lastCamera = Camera.main;
        }

        private static readonly System.Collections.Generic.Dictionary<Volume, float> s_volumeWeights
            = new System.Collections.Generic.Dictionary<Volume, float>();

        private static Camera _lastCamera;

        /// <summary>Вызывается раз в секунду: новая сцена или новая камера получают текущие настройки.</summary>
        internal static void Watch()
        {
            if (!s_loaded) return;
            var cam = Camera.main;
            if (cam != null && cam != _lastCamera) ApplyScene();
        }

        // ------------------------------------------------------------------ Свойства ассета, закрытые в URP

        /// <summary>Свойство с любым уровнем доступа, иначе — сериализованное поле (часть настроек теней в URP только для чтения).</summary>
        private static void SetMember(object target, string property, string field, object value)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = target.GetType();
            try
            {
                var p = t.GetProperty(property, F);
                var setter = p?.GetSetMethod(true);
                if (setter != null) { setter.Invoke(target, new[] { Convert(value, p.PropertyType) }); return; }
                for (var tt = t; tt != null; tt = tt.BaseType)
                {
                    var f = tt.GetField(field, F);
                    if (f != null) { f.SetValue(target, Convert(value, f.FieldType)); return; }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Графика] Не удалось задать {property}: {e.Message}");
            }
        }

        private static object Convert(object value, System.Type to)
            => to.IsEnum ? System.Enum.ToObject(to, value) : System.Convert.ChangeType(value, to);

        // ------------------------------------------------------------------ Исходные значения ассета (для редактора)

        private static UniversalRenderPipelineAsset s_remembered;
        private static float s_origScale;
        private static int s_origMsaa, s_origCascades, s_origShadowRes;
        private static bool s_origShadows;

        private static void Remember(UniversalRenderPipelineAsset asset)
        {
            if (s_remembered == asset) return;
            Restore();
            s_remembered = asset;
            s_origScale = asset.renderScale;
            s_origMsaa = asset.msaaSampleCount;
            s_origCascades = asset.shadowCascadeCount;
            s_origShadows = asset.supportsMainLightShadows;
            s_origShadowRes = asset.mainLightShadowmapResolution;
#if UNITY_EDITOR
            Application.quitting -= Restore;
            Application.quitting += Restore;
#endif
        }

        private static void Restore()
        {
            var a = s_remembered;
            if (a == null) return;
            a.renderScale = s_origScale;
            a.msaaSampleCount = s_origMsaa;
            a.shadowCascadeCount = s_origCascades;
            SetMember(a, "supportsMainLightShadows", "m_MainLightShadowsSupported", s_origShadows);
            SetMember(a, "mainLightShadowmapResolution", "m_MainLightShadowmapResolution", s_origShadowRes);
            s_remembered = null;
        }
    }
}
