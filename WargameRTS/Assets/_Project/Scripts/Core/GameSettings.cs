using UnityEngine;

namespace WargameRTS.Core
{
    /// <summary>Player preferences (stored in PlayerPrefs) edited in the Settings panel.</summary>
    public static class GameSettings
    {
        const string UiScaleKey = "WargameRTS.UiScale";
        const string EdgePanKey = "WargameRTS.EdgePan";
        const string CameraSpeedKey = "WargameRTS.CameraSpeed";
        const string QualityKey = "WargameRTS.Quality";
        const string VSyncKey = "WargameRTS.VSync";

        public static readonly float[] UiScaleOptions = { 0.75f, 0.9f, 1f, 1.15f, 1.3f, 1.5f };

        /// <summary>Multiplier on top of automatic resolution scaling (1080p = 1).</summary>
        public static float UiScale
        {
            get => PlayerPrefs.GetFloat(UiScaleKey, 1f);
            set => PlayerPrefs.SetFloat(UiScaleKey, value);
        }

        public static bool EdgePan
        {
            get => PlayerPrefs.GetInt(EdgePanKey, 1) == 1;
            set => PlayerPrefs.SetInt(EdgePanKey, value ? 1 : 0);
        }

        public static float CameraSpeed
        {
            get => PlayerPrefs.GetFloat(CameraSpeedKey, 1f);
            set => PlayerPrefs.SetFloat(CameraSpeedKey, Mathf.Clamp(value, 0.3f, 3f));
        }

        public static int QualityLevel
        {
            get => PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel());
            set
            {
                PlayerPrefs.SetInt(QualityKey, value);
                QualitySettings.SetQualityLevel(value, true);
            }
        }

        public static bool VSync
        {
            get => PlayerPrefs.GetInt(VSyncKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0);
                QualitySettings.vSyncCount = value ? 1 : 0;
            }
        }

        /// <summary>Applies stored graphics settings once at startup.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyOnStartup()
        {
            if (PlayerPrefs.HasKey(QualityKey))
            {
                int level = Mathf.Clamp(PlayerPrefs.GetInt(QualityKey), 0, QualitySettings.names.Length - 1);
                QualitySettings.SetQualityLevel(level, true);
            }
            QualitySettings.vSyncCount = VSync ? 1 : 0;
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
