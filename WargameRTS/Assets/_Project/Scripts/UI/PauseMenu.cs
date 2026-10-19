using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using WargameRTS.Core;
using WargameRTS.Selection;

namespace WargameRTS.UI
{
    /// <summary>
    /// Esc (with nothing selected) or F10 opens the pause menu: resume, settings,
    /// restart, main menu, quit. Time is frozen while it is open.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        bool open;
        bool settingsOpen;
        float timeScaleBeforePause = 1f;

        public static PauseMenu Instance { get; private set; }

        public bool IsOpen => open;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            // Time scale and InputGate are reset by the next scene's bootstrap.
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.f10Key.wasPressedThisFrame)
            {
                SetOpen(!open);
                return;
            }

            if (!keyboard.escapeKey.wasPressedThisFrame)
                return;

            if (open)
            {
                if (settingsOpen)
                    settingsOpen = false;
                else
                    SetOpen(false);
                return;
            }

            // Escape first cancels an armed order / drops the selection (handled elsewhere).
            bool selectionEmpty = SelectionManager.Instance == null || SelectionManager.Instance.Selected.Count == 0;
            if (!InputGate.ConsumedThisFrame && !InputGate.BlocksSelection && selectionEmpty)
                SetOpen(true);
        }

        public void SetOpen(bool value)
        {
            if (open == value)
                return;
            open = value;
            settingsOpen = false;
            InputGate.MenuOpen = value;

            if (value)
            {
                timeScaleBeforePause = Time.timeScale;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = timeScaleBeforePause;
            }
        }

        /// <summary>Called from BattleHud.OnGUI so the menu is drawn on top of everything.</summary>
        public void DrawGui()
        {
            if (!open)
                return;

            // Dim the whole screen and swallow clicks.
            var screen = new Rect(0f, 0f, HudStyle.Width, HudStyle.Height);
            HudStyle.Block(screen);
            HudStyle.Fill(screen, new Color(0f, 0f, 0f, 0.55f));

            if (settingsOpen)
            {
                if (SettingsPanel.Draw())
                    settingsOpen = false;
                return;
            }

            const float width = 320f;
            const float height = 330f;
            var rect = new Rect((HudStyle.Width - width) * 0.5f, (HudStyle.Height - height) * 0.5f, width, height);
            HudStyle.DrawPanel(rect, true);
            HudStyle.Label(new Rect(rect.x, rect.y + 18f, rect.width, 34f), "ПАУЗА",
                new GUIStyle(HudStyle.CenterStyle) { fontSize = 26 });

            float x = rect.x + 30f;
            float y = rect.y + 74f;
            float buttonWidth = width - 60f;
            if (HudStyle.Button(new Rect(x, y, buttonWidth, 38f), "ПРОДОЛЖИТЬ"))
                SetOpen(false);
            y += 48f;
            if (HudStyle.Button(new Rect(x, y, buttonWidth, 38f), "НАСТРОЙКИ"))
                settingsOpen = true;
            y += 48f;
            if (HudStyle.Button(new Rect(x, y, buttonWidth, 38f), "ПЕРЕЗАПУСТИТЬ БОЙ"))
                Restart();
            y += 48f;
            if (HudStyle.Button(new Rect(x, y, buttonWidth, 38f), "ГЛАВНОЕ МЕНЮ"))
                LoadMainMenu();
            y += 48f;
            if (HudStyle.Button(new Rect(x, y, buttonWidth, 38f), "ВЫЙТИ ИЗ ИГРЫ"))
                Quit();
        }

        public static void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public static void LoadMainMenu()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(MatchSettings.MainMenuScene);
        }

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
