using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// После сборки под Windows кладёт рядом с Astropolity.exe библиотеку достижений VK Play
/// (steam_api64.dll из папки VKPlay в корне проекта) и, если есть, steam_appid.txt.
/// Без библиотеки игра работает как раньше — достижения просто копятся локально.
/// Где взять файл — VKPlay/README.txt.
/// </summary>
public class VkPlayBuildStep : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
        string src = Path.GetFullPath("VKPlay");
        string dst = Path.GetDirectoryName(report.summary.outputPath);
        if (string.IsNullOrEmpty(dst) || !Directory.Exists(src)) return;

        string dll = Path.Combine(src, "steam_api64.dll");
        if (File.Exists(dll))
        {
            File.Copy(dll, Path.Combine(dst, "steam_api64.dll"), true);
            Debug.Log("[VK Play] steam_api64.dll скопирована в сборку — достижения будут работать в VK Play");
        }
        else Debug.LogWarning("[VK Play] Нет VKPlay/steam_api64.dll — достижения в этой сборке будут только локальными. См. VKPlay/README.txt");

        string appid = Path.Combine(src, "steam_appid.txt");
        if (File.Exists(appid)) File.Copy(appid, Path.Combine(dst, "steam_appid.txt"), true);
    }
}
