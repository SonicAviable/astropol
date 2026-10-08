using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Cam;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Настройки игрока (PlayerPrefs). Любое изменение применяется сразу и сохраняется.
    /// </summary>
    public static class GameSettings
    {
        public static event Action Changed;

        // ------------------------------------------------------------------ Графика
        public static readonly string[] ScreenModeNames = { "Полный экран", "Эксклюзивный", "В окне" };
        public static readonly int[] FpsOptions = { 30, 60, 120, 144, 240, -1 };
        public static readonly int[] AutosaveOptions = { 0, 1, 3, 6, 12 };
        public static readonly float[] UIScaleOptions = { 0.8f, 0.9f, 1f, 1.1f, 1.25f };

        public static int ScreenMode { get; private set; }
        public static int ResolutionIndex { get; private set; }
        public static bool VSync { get; private set; }
        public static int FpsIndex { get; private set; }
        public static int Quality { get; private set; }
        public static bool GlassBlur { get; private set; }
        public static float Glow { get; private set; }

        // ------------------------------------------------------------------ Звук
        public static float MasterVolume { get; private set; }
        public static float MusicVolume { get; private set; }
        public static float SfxVolume { get; private set; }
        public static bool MuteInBackground { get; private set; }

        // ------------------------------------------------------------------ Интерфейс / игра
        public static int UIScaleIndex { get; private set; }
        public static bool Tooltips { get; private set; }
        public static int AutosaveIndex { get; private set; }
        public static bool EdgeScroll { get; private set; }
        public static float CameraSpeed { get; private set; }

        public static float UIScale => UIScaleOptions[Mathf.Clamp(UIScaleIndex, 0, UIScaleOptions.Length - 1)];
        public static int AutosaveMonths => AutosaveOptions[Mathf.Clamp(AutosaveIndex, 0, AutosaveOptions.Length - 1)];

        private static bool s_loaded;
        private static List<Vector2Int> s_resolutions;

        // ------------------------------------------------------------------ Разрешения

        public static List<Vector2Int> Resolutions
        {
            get
            {
                if (s_resolutions != null) return s_resolutions;
                s_resolutions = new List<Vector2Int>();
                foreach (var r in Screen.resolutions)
                {
                    var v = new Vector2Int(r.width, r.height);
                    if (v.x < 1024 || v.y < 720 || s_resolutions.Contains(v)) continue;
                    s_resolutions.Add(v);
                }
                if (s_resolutions.Count == 0)
                    s_resolutions.Add(new Vector2Int(Mathf.Max(1280, Screen.width), Mathf.Max(720, Screen.height)));
                s_resolutions.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
                return s_resolutions;
            }
        }

        public static string ResolutionName(int i)
        {
            var list = Resolutions;
            var r = list[Mathf.Clamp(i, 0, list.Count - 1)];
            return $"{r.x} × {r.y}";
        }

        private static int CurrentResolutionIndex()
        {
            var list = Resolutions;
            var cur = Screen.fullScreenMode == FullScreenMode.Windowed
                ? new Vector2Int(Screen.width, Screen.height)
                : new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                int d = Mathf.Abs(list[i].x - cur.x) + Mathf.Abs(list[i].y - cur.y);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // ------------------------------------------------------------------ Загрузка / применение

        public static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;

            int defMode = Screen.fullScreenMode == FullScreenMode.Windowed ? 2
                        : Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? 1 : 0;
            ScreenMode = PlayerPrefs.GetInt("set.screenMode", defMode);
            ResolutionIndex = PlayerPrefs.HasKey("set.resW")
                ? FindResolution(PlayerPrefs.GetInt("set.resW"), PlayerPrefs.GetInt("set.resH"))
                : CurrentResolutionIndex();
            VSync = PlayerPrefs.GetInt("set.vsync", 1) == 1;
            FpsIndex = PlayerPrefs.GetInt("set.fps", 1);
            Quality = PlayerPrefs.GetInt("set.quality", QualitySettings.GetQualityLevel());
            GlassBlur = PlayerPrefs.GetInt("set.blur", 1) == 1;
            Glow = PlayerPrefs.GetFloat("set.glow", 0.2f);

            MasterVolume = PlayerPrefs.GetFloat("set.master", 1f);
            MusicVolume = PlayerPrefs.GetFloat("set.music", 0.45f);
            SfxVolume = PlayerPrefs.GetFloat("set.sfx", 0.7f);
            MuteInBackground = PlayerPrefs.GetInt("set.bgmute", 1) == 1;

            UIScaleIndex = PlayerPrefs.GetInt("set.uiscale", 2);
            Tooltips = PlayerPrefs.GetInt("set.tooltips", 1) == 1;
            AutosaveIndex = PlayerPrefs.GetInt("set.autosave", 2);
            EdgeScroll = PlayerPrefs.GetInt("set.edge", 0) == 1;
            CameraSpeed = PlayerPrefs.GetFloat("set.camspeed", 1f);
        }

        private static int FindResolution(int w, int h)
        {
            var list = Resolutions;
            for (int i = 0; i < list.Count; i++) if (list[i].x == w && list[i].y == h) return i;
            return CurrentResolutionIndex();
        }

        /// <summary>Применить всё (при старте и после перезагрузки сцены).</summary>
        public static void ApplyAll(bool includeDisplay)
        {
            EnsureLoaded();
            SettingsRunner.Ensure();
            if (includeDisplay) ApplyDisplay();
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FpsOptions[Mathf.Clamp(FpsIndex, 0, FpsOptions.Length - 1)];
            ApplyGlass();
            ApplyAudio();
            ApplyUIScale();
            StrategyCameraController.EdgeScrollingSetting = EdgeScroll;
            StrategyCameraController.PanSpeedSetting = CameraSpeed;
        }

        private static void ApplyDisplay()
        {
#if !UNITY_EDITOR
            var r = Resolutions[Mathf.Clamp(ResolutionIndex, 0, Resolutions.Count - 1)];
            var mode = ScreenMode == 1 ? FullScreenMode.ExclusiveFullScreen
                     : ScreenMode == 2 ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            if (Screen.width != r.x || Screen.height != r.y || Screen.fullScreenMode != mode)
                Screen.SetResolution(r.x, r.y, mode);
#endif
            if (Quality >= 0 && Quality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != Quality)
                QualitySettings.SetQualityLevel(Quality, true);
        }

        private static void ApplyGlass()
        {
            var lg = LiquidGlassSystem.Ensure();
            if (lg == null) return;
            lg.backdropEnabled = GlassBlur;
            lg.glowStrength = Glow;
        }

        private static void ApplyAudio()
        {
            AudioListener.volume = MasterVolume;
            MusicManager.Instance?.SetVolume(MusicVolume);
            SFXManager.SetMasterVolume(SfxVolume);
        }

        private static void ApplyUIScale() => UIScaleTarget.ApplyAllTargets();

        internal static bool Dirty;

        /// <summary>Запись на диск откладывается до отпускания мыши (ползунки тянут значения каждый кадр).</summary>
        private static void Commit()
        {
            Dirty = true;
            SettingsRunner.Ensure();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ Сеттеры

        public static void SetScreenMode(int v) { ScreenMode = Mathf.Clamp(v, 0, 2); PlayerPrefs.SetInt("set.screenMode", ScreenMode); ApplyDisplay(); Commit(); }

        public static void SetResolution(int i)
        {
            ResolutionIndex = Mathf.Clamp(i, 0, Resolutions.Count - 1);
            var r = Resolutions[ResolutionIndex];
            PlayerPrefs.SetInt("set.resW", r.x);
            PlayerPrefs.SetInt("set.resH", r.y);
            ApplyDisplay();
            Commit();
        }

        public static void SetVSync(bool v) { VSync = v; PlayerPrefs.SetInt("set.vsync", v ? 1 : 0); QualitySettings.vSyncCount = v ? 1 : 0; Commit(); }

        public static void SetFps(int i)
        {
            FpsIndex = Mathf.Clamp(i, 0, FpsOptions.Length - 1);
            PlayerPrefs.SetInt("set.fps", FpsIndex);
            Application.targetFrameRate = FpsOptions[FpsIndex];
            Commit();
        }

        public static void SetQuality(int i)
        {
            Quality = Mathf.Clamp(i, 0, QualitySettings.names.Length - 1);
            PlayerPrefs.SetInt("set.quality", Quality);
            QualitySettings.SetQualityLevel(Quality, true);
            // Смена уровня качества сбрасывает vSync — возвращаем выбор игрока
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Commit();
        }

        public static void SetGlassBlur(bool v) { GlassBlur = v; PlayerPrefs.SetInt("set.blur", v ? 1 : 0); ApplyGlass(); Commit(); }
        public static void SetGlow(float v) { Glow = Mathf.Clamp(v, 0f, 0.8f); PlayerPrefs.SetFloat("set.glow", Glow); ApplyGlass(); Commit(); }

        public static void SetMaster(float v) { MasterVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat("set.master", MasterVolume); ApplyAudio(); Commit(); }
        public static void SetMusic(float v) { MusicVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat("set.music", MusicVolume); ApplyAudio(); Commit(); }
        public static void SetSfx(float v) { SfxVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat("set.sfx", SfxVolume); ApplyAudio(); Commit(); }
        public static void SetMuteInBackground(bool v) { MuteInBackground = v; PlayerPrefs.SetInt("set.bgmute", v ? 1 : 0); Commit(); }

        public static void SetUIScale(int i) { UIScaleIndex = Mathf.Clamp(i, 0, UIScaleOptions.Length - 1); PlayerPrefs.SetInt("set.uiscale", UIScaleIndex); ApplyUIScale(); Commit(); }
        public static void SetTooltips(bool v) { Tooltips = v; PlayerPrefs.SetInt("set.tooltips", v ? 1 : 0); if (!v) TooltipSystem.Hide(); Commit(); }
        public static void SetAutosave(int i) { AutosaveIndex = Mathf.Clamp(i, 0, AutosaveOptions.Length - 1); PlayerPrefs.SetInt("set.autosave", AutosaveIndex); Commit(); }
        public static void SetEdgeScroll(bool v) { EdgeScroll = v; PlayerPrefs.SetInt("set.edge", v ? 1 : 0); StrategyCameraController.EdgeScrollingSetting = v; Commit(); }
        public static void SetCameraSpeed(float v) { CameraSpeed = Mathf.Clamp(v, 0.4f, 2.5f); PlayerPrefs.SetFloat("set.camspeed", CameraSpeed); StrategyCameraController.PanSpeedSetting = CameraSpeed; Commit(); }

        /// <summary>Сбросить вкладку к значениям по умолчанию.</summary>
        public static void ResetDefaults()
        {
            foreach (var k in new[] { "set.vsync", "set.fps", "set.blur", "set.glow", "set.master", "set.music", "set.sfx",
                                      "set.bgmute", "set.uiscale", "set.tooltips", "set.autosave", "set.edge", "set.camspeed" })
                PlayerPrefs.DeleteKey(k);
            s_loaded = false;
            EnsureLoaded();
            ApplyAll(includeDisplay: false);
            Commit();
        }
    }

    /// <summary>Канвас, масштаб которого зависит от настройки «Масштаб интерфейса».</summary>
    [DisallowMultipleComponent]
    public class UIScaleTarget : MonoBehaviour
    {
        public Vector2 baseResolution = new Vector2(1920, 1080);
        private static readonly List<UIScaleTarget> s_All = new List<UIScaleTarget>();

        private void OnEnable() { s_All.Add(this); Apply(); }
        private void OnDisable() => s_All.Remove(this);

        public void Apply()
        {
            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null) return;
            GameSettings.EnsureLoaded();
            scaler.referenceResolution = baseResolution / Mathf.Max(0.5f, GameSettings.UIScale);
        }

        public static void ApplyAllTargets()
        {
            for (int i = s_All.Count - 1; i >= 0; i--)
                if (s_All[i] != null) s_All[i].Apply();
        }
    }

    /// <summary>Приглушение звука, когда окно игры не в фокусе.</summary>
    public class SettingsRunner : MonoBehaviour
    {
        private static SettingsRunner s_instance;

        public static void Ensure()
        {
            if (s_instance != null) return;
            var go = new GameObject("[Settings]");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<SettingsRunner>();
        }

        private void LateUpdate()
        {
            if (!GameSettings.Dirty || Input.GetMouseButton(0)) return;
            GameSettings.Dirty = false;
            PlayerPrefs.Save();
        }

        private void OnApplicationQuit()
        {
            if (GameSettings.Dirty) PlayerPrefs.Save();
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!GameSettings.MuteInBackground) { AudioListener.pause = false; return; }
            AudioListener.pause = !focus;
        }
    }
}
