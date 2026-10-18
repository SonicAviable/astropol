using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace WargameRTS.Editor
{
    /// <summary>
    /// Opens the Sandbox scene automatically when the project is opened with an
    /// empty untitled scene (e.g. the first time after cloning), and adds menu items.
    /// </summary>
    [InitializeOnLoad]
    static class SandboxSceneOpener
    {
        const string ScenePath = "Assets/_Project/Scenes/Sandbox.unity";
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
            if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(ScenePath))
                return;

            var active = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path) && !active.isDirty)
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("WargameRTS/Open Sandbox Scene", priority = 0)]
        static void OpenSandbox()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("WargameRTS/Play Sandbox", priority = 1)]
        static void PlaySandbox()
        {
            if (EditorApplication.isPlaying)
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }
    }
}
