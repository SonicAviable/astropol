using System;
using System.Collections.Generic;

namespace StellarisClone.Core
{
    /// <summary>
    /// Поток минимальной стоимости (последовательные кратчайшие пути, Дейкстра с потенциалами).
    /// Чистая математика без Unity: узлы — целые числа, рёбра — направленные с пропускной способностью и ценой
    /// за единицу. У каждого ребра есть обратное (индекс ^ 1) для остаточной сети.
    /// Цены должны быть неотрицательными — тогда начальные потенциалы нулевые и Дейкстра корректна.
    /// </summary>
    public sealed class FlowSolver
    {
        public const float Eps = 1e-4f;

        private readonly List<int> _to = new List<int>();
        private readonly List<float> _cap = new List<float>();
        private readonly List<float> _cost = new List<float>();
        private readonly List<float> _flow = new List<float>();
        private readonly List<int> _next = new List<int>();
        private readonly List<int> _head = new List<int>();

        public int NodeCount => _head.Count;

        public int AddNode()
        {
            _head.Add(-1);
            return _head.Count - 1;
        }

        public void AddNodes(int count)
        {
            for (int i = 0; i < count; i++) _head.Add(-1);
        }

        /// <summary>Направленное ребро u → v; возвращает его индекс (обратное — индекс ^ 1).</summary>
        public int AddEdge(int u, int v, float capacity, float cost)
        {
            if (cost < 0f) cost = 0f;
            int e = _to.Count;
            Push(u, v, Math.Max(0f, capacity), cost);
            Push(v, u, 0f, -cost);
            return e;
        }

        private void Push(int u, int v, float cap, float cost)
        {
            _to.Add(v); _cap.Add(cap); _cost.Add(cost); _flow.Add(0f);
            _next.Add(_head[u]);
            _head[u] = _to.Count - 1;
        }

        /// <summary>Сколько прошло по ребру (только для прямых рёбер из AddEdge).</summary>
        public float Flow(int edge) => edge >= 0 && edge < _flow.Count ? _flow[edge] : 0f;
        public float Capacity(int edge) => edge >= 0 && edge < _cap.Count ? _cap[edge] : 0f;

        /// <summary>
        /// Пустить из s в t как можно больше (но не больше limit) с наименьшей суммарной ценой.
        /// Возвращает объём; totalCost — его цена.
        /// </summary>
        public float Solve(int s, int t, float limit, out float totalCost)
        {
            totalCost = 0f;
            int n = _head.Count;
            if (s < 0 || t < 0 || s >= n || t >= n || s == t || limit <= Eps) return 0f;

            var pot = new float[n];
            var dist = new float[n];
            var prevEdge = new int[n];
            var done = new bool[n];
            var heap = new MinHeap(n);
            float flow = 0f;
            int guard = 0;

            while (flow < limit - Eps && guard++ < 100000)
            {
                for (int i = 0; i < n; i++) { dist[i] = float.PositiveInfinity; prevEdge[i] = -1; done[i] = false; }
                dist[s] = 0f;
                heap.Clear();
                heap.Push(s, 0f);
                while (heap.Count > 0)
                {
                    heap.Pop(out int u, out float du);
                    if (done[u] || du > dist[u]) continue;
                    done[u] = true;
                    for (int e = _head[u]; e != -1; e = _next[e])
                    {
                        if (_cap[e] - _flow[e] <= Eps) continue;
                        int v = _to[e];
                        if (done[v]) continue;
                        float rc = _cost[e] + pot[u] - pot[v];
                        if (rc < 0f) rc = 0f;   // погрешность float: приведённая цена не бывает отрицательной
                        float nd = du + rc;
                        if (nd < dist[v] - 1e-6f)
                        {
                            dist[v] = nd;
                            prevEdge[v] = e;
                            heap.Push(v, nd);
                        }
                    }
                }
                if (float.IsPositiveInfinity(dist[t])) break;
                for (int i = 0; i < n; i++) if (!float.IsPositiveInfinity(dist[i])) pot[i] += dist[i];

                float push = limit - flow;
                for (int v = t; v != s; v = _to[prevEdge[v] ^ 1])
                {
                    int e = prevEdge[v];
                    push = Math.Min(push, _cap[e] - _flow[e]);
                }
                if (push <= Eps) break;
                float pathCost = 0f;
                for (int v = t; v != s; v = _to[prevEdge[v] ^ 1])
                {
                    int e = prevEdge[v];
                    _flow[e] += push;
                    _flow[e ^ 1] -= push;
                    pathCost += _cost[e];
                }
                flow += push;
                totalCost += push * pathCost;
            }
            return flow;
        }

        /// <summary>Двоичная куча по ключу (с повторами: устаревшие записи отбрасывает Solve).</summary>
        private sealed class MinHeap
        {
            private readonly List<int> _node;
            private readonly List<float> _key;

            public MinHeap(int capacity)
            {
                _node = new List<int>(capacity);
                _key = new List<float>(capacity);
            }

            public int Count => _node.Count;

            public void Clear() { _node.Clear(); _key.Clear(); }

            public void Push(int node, float key)
            {
                _node.Add(node); _key.Add(key);
                int i = _node.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_key[p] <= _key[i]) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public void Pop(out int node, out float key)
            {
                node = _node[0]; key = _key[0];
                int last = _node.Count - 1;
                _node[0] = _node[last]; _key[0] = _key[last];
                _node.RemoveAt(last); _key.RemoveAt(last);
                int i = 0, n = _node.Count;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < n && _key[l] < _key[m]) m = l;
                    if (r < n && _key[r] < _key[m]) m = r;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
            }

            private void Swap(int a, int b)
            {
                (_node[a], _node[b]) = (_node[b], _node[a]);
                (_key[a], _key[b]) = (_key[b], _key[a]);
            }
        }
    }
}
