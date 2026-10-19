using UnityEngine;
using WargameRTS.Core;

namespace WargameRTS.UI
{
    /// <summary>Settings window shared by the main menu and the pause menu.</summary>
    public static class SettingsPanel
    {
        public const float Width = 520f;
        public const float Height = 400f;

        static readonly string[] UiScaleLabels = { "75%", "90%", "100%", "115%", "130%", "150%" };
        static readonly string[] CameraSpeedLabels = { "Медленно", "Норма", "Быстро", "Очень быстро" };
        static readonly float[] CameraSpeeds = { 0.6f, 1f, 1.5f, 2.2f };

        /// <summary>Draws the window centered on screen. Returns true when the player closes it.</summary>
        public static bool Draw()
        {
            var rect = new Rect((HudStyle.Width - Width) * 0.5f, (HudStyle.Height - Height) * 0.5f, Width, Height);
            HudStyle.DrawPanel(rect, true);

            float x = rect.x + 24f;
            float labelWidth = 170f;
            float controlX = x + labelWidth;
            float controlWidth = rect.width - labelWidth - 48f;
            float y = rect.y + 20f;

            HudStyle.Label(new Rect(x, y, rect.width, 28f), "НАСТРОЙКИ", new GUIStyle(HudStyle.TitleStyle) { fontSize = 22 });
            y += 50f;

            // UI scale
            HudStyle.Label(new Rect(x, y + 6f, labelWidth, 20f), "Масштаб интерфейса");
            int scaleIndex = ClosestIndex(GameSettings.UiScaleOptions, GameSettings.UiScale);
            int newScale = HudStyle.Segmented(new Rect(controlX, y, controlWidth, 30f), UiScaleLabels, scaleIndex, 12);
            if (newScale != scaleIndex)
                GameSettings.UiScale = GameSettings.UiScaleOptions[newScale];
            y += 44f;

            // Graphics quality
            HudStyle.Label(new Rect(x, y + 6f, labelWidth, 20f), "Качество графики");
            var names = QualitySettings.names;
            int quality = Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, Mathf.Max(0, names.Length - 1));
            if (names.Length > 0)
            {
                int newQuality = HudStyle.Segmented(new Rect(controlX, y, controlWidth, 30f), names, quality, 12);
                if (newQuality != quality)
                    GameSettings.QualityLevel = newQuality;
            }
            y += 44f;

            // Camera speed
            HudStyle.Label(new Rect(x, y + 6f, labelWidth, 20f), "Скорость камеры");
            int speedIndex = ClosestIndex(CameraSpeeds, GameSettings.CameraSpeed);
            int newSpeed = HudStyle.Segmented(new Rect(controlX, y, controlWidth, 30f), CameraSpeedLabels, speedIndex, 12);
            if (newSpeed != speedIndex)
                GameSettings.CameraSpeed = CameraSpeeds[newSpeed];
            y += 48f;

            GameSettings.EdgePan = HudStyle.Toggle(new Rect(x, y, 400f, 24f), "Прокрутка камеры краем экрана", GameSettings.EdgePan);
            y += 32f;
            GameSettings.VSync = HudStyle.Toggle(new Rect(x, y, 400f, 24f), "Вертикальная синхронизация (VSync)", GameSettings.VSync);
            y += 32f;

            bool fullscreen = Screen.fullScreen;
            bool newFullscreen = HudStyle.Toggle(new Rect(x, y, 400f, 24f), "Полноэкранный режим", fullscreen);
            if (newFullscreen != fullscreen)
                Screen.fullScreen = newFullscreen;

            bool close = HudStyle.Button(new Rect(rect.xMax - 164f, rect.yMax - 52f, 140f, 34f), "ГОТОВО");
            if (close)
                GameSettings.Save();
            return close;
        }

        static int ClosestIndex(float[] values, float value)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++)
            {
                if (Mathf.Abs(values[i] - value) < Mathf.Abs(values[best] - value))
                    best = i;
            }
            return best;
        }
    }
}
