using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Сборка игры для Windows — из меню «Astropolity», независимо от того, какая платформа сейчас активна
/// в Build Profiles. Папка и имя exe запоминаются между сборками.
/// Библиотеку достижений VK Play рядом с exe кладёт VkPlayBuildStep.
/// </summary>
public static class WindowsBuild
{
    private const string PathKey = "Astropolity.WindowsBuildPath";

    [MenuItem("Astropolity/Собрать для Windows", priority = 0)]
    public static void Build()
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("Сборка для Windows", "В Build Settings нет включённых сцен.", "OK");
            return;
        }

        string last = EditorPrefs.GetString(PathKey, Path.GetFullPath("Builds/Windows/Astropolity.exe"));
        string exe = EditorUtility.SaveFilePanel("Куда собрать игру для Windows",
            Path.GetDirectoryName(last), Path.GetFileName(last), "exe");
        if (string.IsNullOrEmpty(exe)) return;
        EditorPrefs.SetString(PathKey, exe);
        Directory.CreateDirectory(Path.GetDirectoryName(exe));

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        });

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[WindowsBuild] Готово: {exe}");
            EditorUtility.RevealInFinder(exe);
        }
        else
        {
            Debug.LogError($"[WindowsBuild] Сборка не удалась: {report.summary.result}, ошибок: {report.summary.totalErrors}");
            EditorUtility.DisplayDialog("Сборка для Windows", "Сборка не удалась — подробности в Console.", "OK");
        }
    }
}
