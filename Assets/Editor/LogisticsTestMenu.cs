using System.Text;
using UnityEditor;
using UnityEngine;
using StellarisClone.Core;

/// <summary>
/// Меню «Astropolity → Снабжение»: проверка решателя потока (без запуска игры) и отчёт сети снабжения
/// игрока (в Play Mode): хабы, отрезанные системы, забитые коридоры, склады.
/// </summary>
public static class LogisticsTestMenu
{
    [MenuItem("Astropolity/Снабжение/Проверка решателя потока", priority = 60)]
    private static void SolverTests()
    {
        int fails = 0;
        void Check(bool ok, string name)
        {
            if (!ok) fails++;
            Debug.Log((ok ? "<color=#5F5>OK</color>   " : "<color=#F55>FAIL</color> ") + name);
        }

        // Цепочка: узкое место 3
        var f = new FlowSolver();
        f.AddNodes(3);
        f.AddEdge(0, 1, 5f, 1f);
        f.AddEdge(1, 2, 3f, 1f);
        float flow = f.Solve(0, 2, 100f, out float cost);
        Check(Mathf.Approximately(flow, 3f) && Mathf.Approximately(cost, 6f), $"цепочка: поток {flow}, цена {cost}");

        // Дешёвый путь заполняется первым
        f = new FlowSolver();
        f.AddNodes(4);
        f.AddEdge(0, 1, 4f, 1f); f.AddEdge(1, 3, 4f, 1f);
        f.AddEdge(0, 2, 10f, 5f); f.AddEdge(2, 3, 10f, 5f);
        flow = f.Solve(0, 3, 6f, out cost);
        Check(Mathf.Approximately(flow, 6f) && Mathf.Approximately(cost, 28f), $"два пути: поток {flow}, цена {cost}");

        // Нужен откат по обратному ребру
        f = new FlowSolver();
        f.AddNodes(4);
        f.AddEdge(0, 1, 1f, 1f); f.AddEdge(0, 2, 1f, 10f); f.AddEdge(1, 2, 1f, 1f);
        f.AddEdge(1, 3, 1f, 10f); f.AddEdge(2, 3, 1f, 1f);
        flow = f.Solve(0, 3, 10f, out cost);
        Check(Mathf.Approximately(flow, 2f) && Mathf.Approximately(cost, 22f), $"остаточная сеть: поток {flow}, цена {cost}");

        // Нагрузка: 400 систем
        var rnd = new System.Random(1);
        int n = 400;
        f = new FlowSolver();
        f.AddNodes(n + 2);
        for (int i = 0; i < n; i++)
            for (int k = 0; k < 3; k++)
            {
                int j = rnd.Next(n);
                f.AddEdge(i, j, 30f, 1f);
                f.AddEdge(j, i, 30f, 1f);
            }
        for (int i = 0; i < n; i += 3) f.AddEdge(n, i, 40f, 0f);
        for (int i = 0; i < 10; i++) f.AddEdge(rnd.Next(n), n + 1, 1e6f, 0f);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        flow = f.Solve(n, n + 1, 1e9f, out cost);
        Check(sw.ElapsedMilliseconds < 200, $"400 систем: поток {flow:0}, {sw.ElapsedMilliseconds} мс");

        Debug.Log(fails == 0 ? "<color=#5F5>[Снабжение] Решатель: все проверки пройдены</color>" : $"<color=#F55>[Снабжение] Провалено проверок: {fails}</color>");
    }

    [MenuItem("Astropolity/Снабжение/Отчёт сети игрока", priority = 61)]
    private static void PlayerReport()
    {
        if (!EditorApplication.isPlaying) { Debug.Log("[Снабжение] Отчёт доступен в Play Mode"); return; }
        var r = SupplyNetwork.Get(0);
        if (r == null) { Debug.Log("[Снабжение] Сеть игрока ещё не считалась"); return; }
        var sb = new StringBuilder();
        sb.AppendLine($"[Снабжение] Систем {r.Systems.Count}, хабов {r.Hubs.Count}{(r.FallbackHub ? " (запасной)" : "")}, отрезано {r.CutOff}, блокада {r.Blockaded}, доходит {r.Efficiency * 100f:0}%");
        sb.AppendLine($"  Титан: застряло {r.StuckMinerals:0.#}, со складов {r.FromStockMinerals:0.#}, заводам не довезли {r.Unfed:0.#}; сплавы: застряло {r.StuckAlloys:0.#}, потеряно {r.LostAlloys:0.#}");
        foreach (var f in r.Systems.Values)
        {
            var st = SupplyState.StockOf(f.SystemId);
            if (f.State == SupplyNetwork.NodeState.Connected && f.DeliveredShare > 0.99f && st.Total < 1f) continue;
            sb.AppendLine($"  {EmpireStats.GetSystem(f.SystemId)?.Name}: {f.State}, доходит {f.DeliveredShare * 100f:0}%, склад {st.Total:0}/{f.StockCapacity:0}, транзит {f.Through:0}");
        }
        foreach (var l in r.Lanes.Values)
            if (l.Load >= 0.9f || l.Blockaded)
                sb.AppendLine($"  Коридор {EmpireStats.GetSystem(l.A)?.Name} — {EmpireStats.GetSystem(l.B)?.Name}: {l.Flow:0}/{l.Capacity:0}{(l.Blockaded ? " БЛОКАДА" : "")}");
        Debug.Log(sb.ToString());
    }
}
