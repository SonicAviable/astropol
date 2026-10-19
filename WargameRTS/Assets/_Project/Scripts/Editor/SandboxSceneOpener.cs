using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace WargameRTS.Editor
{
    /// <summary>
    /// Opens the main menu scene automatically when the project is opened with an
    /// empty untitled scene (e.g. the first time after cloning), and adds menu items.
    /// </summary>
    [InitializeOnLoad]
    static class SandboxSceneOpener
    {
        const string MainMenuPath = "Assets/_Project/Scenes/MainMenu.unity";
        const string BattlePath = "Assets/_Project/Scenes/Battle.unity";
        const string SessionKey = "WargameRTS.SandboxSceneOpener.Done";

        static SandboxSceneOpener()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;
            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += OpenIfUntitled;
        }

        static void OpenIfUntitled()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(MainMenuPath))
                return;

            var active = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path) && !active.isDirty)
                EditorSceneManager.OpenScene(MainMenuPath);
        }

        [MenuItem("WargameRTS/Play (Main Menu)", priority = 0)]
        static void PlayMainMenu() => Play(MainMenuPath);

        [MenuItem("WargameRTS/Play Battle Directly (Sandbox)", priority = 1)]
        static void PlayBattle() => Play(BattlePath);

        [MenuItem("WargameRTS/Open Main Menu Scene", priority = 20)]
        static void OpenMainMenu() => Open(MainMenuPath);

        [MenuItem("WargameRTS/Open Battle Scene", priority = 21)]
        static void OpenBattle() => Open(BattlePath);

        static void Open(string path)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(path);
        }

        static void Play(string path)
        {
            if (EditorApplication.isPlaying || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(path);
            EditorApplication.isPlaying = true;
        }
    }
}
