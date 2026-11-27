using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Выгрузка записанных кадров Profiler в текст: среднее по последним кадрам главного потока —
/// дерево вызовов (Total), самые тяжёлые узлы по собственному времени и по мусору (GC Alloc).
/// Файл — ProfilerDump.txt в корне проекта; тот же текст копируется в буфер обмена.
/// </summary>
public static class ProfilerDump
{
    private const int Frames = 120;          // сколько последних кадров усреднять
    private const float MinTotalMs = 0.15f;  // узлы легче этого в дерево не попадают
    private const int MaxDepth = 9;

    private class Acc { public float Total, Self, Gc, Calls; public int Depth; }

    [MenuItem("Tools/Производительность/Выгрузить профайлер в текст")]
    public static void Dump()
    {
        int last = ProfilerDriver.lastFrameIndex, first = Mathf.Max(ProfilerDriver.firstFrameIndex, last - Frames + 1);
        if (last < 0 || first > last) { EditorUtility.DisplayDialog("Профайлер", "Нет записанных кадров: запустите игру с открытым Profiler (Record).", "OK"); return; }

        var acc = new Dictionary<string, Acc>();
        var order = new List<string>();
        int used = 0;
        var kids = new List<int>();
        for (int f = first; f <= last; f++)
        {
            using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                                                                   HierarchyFrameDataView.columnTotalTime, false))
            {
                if (v == null || !v.valid) continue;
                used++;
                Walk(v, v.GetRootItemID(), "", 0, acc, order, kids);
            }
        }
        if (used == 0) { EditorUtility.DisplayDialog("Профайлер", "Не удалось прочитать кадры.", "OK"); return; }

        var sb = new StringBuilder();
        sb.AppendLine($"Кадров усреднено: {used} ({first}..{last}). Время — мс на кадр, GC — КБ на кадр.");
        sb.AppendLine();
        sb.AppendLine("=== Самое тяжёлое собственное время (Self) ===");
        Top(sb, acc, a => a.Self, 40, "ms");
        sb.AppendLine();
        sb.AppendLine("=== Больше всего мусора (GC Alloc, собственный) ===");
        Top(sb, acc, a => a.Gc, 30, "KB");
        sb.AppendLine();
        sb.AppendLine("=== Дерево (Total ≥ " + MinTotalMs + " мс) ===");
        foreach (var path in order)
        {
            var a = acc[path];
            float tot = a.Total / used;
            if (tot < MinTotalMs) continue;
            string name = path.Substring(path.LastIndexOf('/') + 1);
            sb.Append(new string(' ', a.Depth * 2)).Append(name)
              .Append($"  total {tot:0.00}  self {a.Self / used:0.00}  calls {a.Calls / used:0.#}");
            if (a.Gc > 0f) sb.Append($"  gc {a.Gc / used / 1024f:0.0}KB");
            sb.AppendLine();
        }

        string text = sb.ToString();
        string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ProfilerDump.txt"));
        File.WriteAllText(file, text, Encoding.UTF8);
        EditorGUIUtility.systemCopyBuffer = text;
        Debug.Log($"[ProfilerDump] Записано в {file} и скопировано в буфер обмена ({used} кадров).");
        EditorUtility.RevealInFinder(file);

        void Top(StringBuilder s, Dictionary<string, Acc> d, System.Func<Acc, float> key, int n, string unit)
        {
            var list = new List<KeyValuePair<string, Acc>>(d);
            list.Sort((x, y) => key(y.Value).CompareTo(key(x.Value)));
            for (int i = 0; i < list.Count && i < n; i++)
            {
                float val = key(list[i].Value) / used;
                if (unit == "KB") val /= 1024f;
                if (val < 0.01f) break;
                s.AppendLine($"{val,8:0.00} {unit}  {list[i].Key}");
            }
        }
    }

    private static void Walk(HierarchyFrameDataView v, int id, string parent, int depth,
                             Dictionary<string, Acc> acc, List<string> order, List<int> kids)
    {
        kids.Clear();
        v.GetItemChildren(id, kids);
        var mine = new List<int>(kids);
        foreach (int c in mine)
        {
            string path = parent.Length == 0 ? v.GetItemName(c) : parent + "/" + v.GetItemName(c);
            if (!acc.TryGetValue(path, out var a)) { a = new Acc { Depth = depth }; acc[path] = a; order.Add(path); }
            a.Total += v.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime);
            a.Self += v.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnSelfTime);
            a.Gc += v.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnGcMemory);
            a.Calls += v.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnCalls);
            if (depth < MaxDepth) Walk(v, c, path, depth + 1, acc, order, kids);
        }
    }
}
