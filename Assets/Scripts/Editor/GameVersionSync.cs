#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using StellarisClone.Core;

/// <summary>Держит версию проекта равной GameVersion.Current: при загрузке редактора и перед каждой сборкой.</summary>
[InitializeOnLoad]
public sealed class GameVersionSync : IPreprocessBuildWithReport
{
    static GameVersionSync() => Sync();

    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) => Sync();

    private static void Sync()
    {
        if (PlayerSettings.bundleVersion == GameVersion.Current) return;
        PlayerSettings.bundleVersion = GameVersion.Current;
        AssetDatabase.SaveAssets();
    }
}
#endif
