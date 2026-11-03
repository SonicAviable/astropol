// Tools → Список ассетов: собирает в docs/assets_manifest.txt пути к импортированным ассетам,
// чтобы по ним писать код без доступа к самим файлам.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class AssetManifest
{
    // Свои папки — не сканируем.
    static readonly string[] Skip = { "Assets/Scripts", "Assets/Editor", "Assets/Settings", "Assets/TutorialInfo" };

    static readonly (string filter, string title)[] Types =
    {
        ("t:Scene", "Сцены (демо)"),
        ("t:Prefab", "Префабы"),
        ("t:Model", "Модели"),
        ("t:AnimatorController", "Аниматоры"),
        ("t:AnimationClip", "Анимации"),
        ("t:AudioClip", "Звуки"),
        ("t:Material", "Материалы"),
        ("t:TerrainLayer", "Слои террейна"),
    };

    [MenuItem("Tools/Список ассетов")]
    public static void Export()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Манифест ассетов (сгенерирован Tools → Список ассетов)");
        sb.AppendLine();

        var outside = new HashSet<string>();
        foreach (var (filter, title) in Types)
        {
            var paths = AssetDatabase.FindAssets(filter, new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !Skip.Any(s => p.StartsWith(s + "/")))
                .Distinct().OrderBy(p => p).ToList();
            if (paths.Count == 0) continue;

            sb.AppendLine($"## {title} ({paths.Count})");
            foreach (var p in paths)
            {
                sb.AppendLine(p);
                if (filter == "t:Model") AppendClips(sb, p);
                if (!p.StartsWith("Assets/ThirdParty/")) outside.Add(p.Split('/')[1]);
            }
            sb.AppendLine();
        }

        if (outside.Count > 0)
        {
            sb.AppendLine("## ВНИМАНИЕ: папки вне Assets/ThirdParty (попадут в Git)");
            foreach (var d in outside.OrderBy(d => d)) sb.AppendLine("Assets/" + d);
        }

        var dir = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "docs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "assets_manifest.txt");
        File.WriteAllText(file, sb.ToString(), Encoding.UTF8);

        var msg = $"Готово: {file}";
        if (outside.Count > 0)
            msg += $"\n\nПеренесите в Assets/ThirdParty (иначе уйдут в Git):\n{string.Join("\n", outside)}";
        EditorUtility.DisplayDialog("Список ассетов", msg, "OK");
        EditorUtility.RevealInFinder(file);
    }

    // Клипы, лежащие внутри FBX.
    static void AppendClips(StringBuilder sb, string modelPath)
    {
        foreach (var a in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath))
            if (a is AnimationClip c && !c.name.StartsWith("__preview__"))
                sb.AppendLine("    clip: " + c.name);
    }
}
