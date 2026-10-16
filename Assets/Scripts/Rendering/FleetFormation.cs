using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Строй флотов: флоты одного владельца и одного типа, которые держатся вместе, не наезжают друг на друга,
    /// а идут единой группой — клином за ведущим (флот с меньшим номером), носами в одну сторону.
    ///   • на орбите — все такие флоты одной системы;
    ///   • в гиперкоридоре — флоты одного коридора (откуда → куда), идущие почти вровень
    ///     (разница в пройденном пути меньше TransitTogether); отставшие образуют свою группу.
    /// Больше семи флотов — следующий клин встаёт позади первого.
    /// По этим же группам сливаются значки флотов (FleetIndicator) — одна группа, один значок с числом.
    /// Группы пересчитываются раз в кадр, по запросу первого же флота.
    /// </summary>
    public static class FleetFormation
    {
        /// <summary>Флоты в коридоре идут вместе, если их пройденные доли пути отличаются меньше чем на столько.</summary>
        private const float TransitTogether = 0.08f;

        public struct Slot
        {
            public int Index;          // место в строю (0 — ведущий)
            public int Count;          // флотов в группе
            public FleetView Leader;
            public int SelectedCount;  // выделенных флотов в группе
            public FleetView FirstSelected;
            public IReadOnlyList<FleetView> Members;
        }

        private static int s_frame = -1;
        private static readonly Dictionary<FleetView, List<FleetView>> s_groupOf = new Dictionary<FleetView, List<FleetView>>();
        private static readonly Dictionary<long, List<FleetView>> s_orbit = new Dictionary<long, List<FleetView>>();
        private static readonly Dictionary<long, List<List<FleetView>>> s_transit = new Dictionary<long, List<List<FleetView>>>();
        private static readonly List<List<FleetView>> s_pool = new List<List<FleetView>>();
        private static readonly List<FleetView> s_movers = new List<FleetView>();
        private static int s_poolUsed;

        /// <summary>Стоит ли флот в строю на орбите: на орбите, не в бою и не за работой.</summary>
        public static bool Holds(FleetData d) => d != null && !d.Destroyed && !d.InCombat
                                                 && d.State == FleetState.Orbiting && d.CurrentSystemId >= 0;

        /// <summary>Летит ли флот по коридору (может идти строем с попутчиками).</summary>
        public static bool Travels(FleetData d) => d != null && !d.Destroyed
                                                   && d.State == FleetState.InHyperlane && d.TargetSystemId >= 0;

        public static Slot Get(FleetView v)
        {
            Rebuild();
            var s = new Slot { Index = 0, Count = 1, Leader = v, SelectedCount = v != null && v.IsSelected ? 1 : 0, FirstSelected = v };
            if (v == null || !s_groupOf.TryGetValue(v, out var list))
            {
                s.Members = v != null ? new[] { v } : System.Array.Empty<FleetView>();
                return s;
            }
            s.Members = list;
            s.Index = list.IndexOf(v);
            s.Count = list.Count;
            s.Leader = list[0];
            s.SelectedCount = 0;
            s.FirstSelected = null;
            foreach (var f in list)
            {
                if (!f.IsSelected) continue;
                if (s.FirstSelected == null) s.FirstSelected = f;
                s.SelectedCount++;
            }
            return s;
        }

        /// <summary>
        /// Смещение места в строю в осях группы: x — вбок, z — вперёд (по курсу ведущего), y — лёгкий разнобой по высоте.
        /// spacing — шаг между кораблями.
        /// </summary>
        public static Vector3 LocalOffset(int index, float spacing)
        {
            if (index <= 0) return Vector3.zero;
            int wedge = (index - 1) / 6, j = (index - 1) % 6;
            int row = j / 2 + 1;
            float side = j % 2 == 0 ? -1f : 1f;
            float x = side * row * spacing * 0.85f;
            float z = -(row + wedge * 1.6f) * spacing * 0.7f;
            float y = ((index * 37) % 5 - 2) * 0.12f;
            return new Vector3(x, y, z);
        }

        private static float Progress(FleetData d)
            => d.TotalDaysForTransit > 0f ? 1f - d.DaysRemainingInTransit / d.TotalDaysForTransit : 1f;

        private static List<FleetView> NewList()
        {
            if (s_poolUsed >= s_pool.Count) s_pool.Add(new List<FleetView>());
            var l = s_pool[s_poolUsed++];
            l.Clear();
            return l;
        }

        private static void Rebuild()
        {
            if (s_frame == Time.frameCount) return;
            s_frame = Time.frameCount;
            s_groupOf.Clear();
            s_orbit.Clear();
            s_transit.Clear();
            s_movers.Clear();
            s_poolUsed = 0;

            var fm = FleetManager.Instance;
            if (fm == null) return;

            foreach (var fv in fm.AllFleets)
            {
                var d = fv != null ? fv.Data : null;
                if (Holds(d))
                {
                    long key = ((long)d.CurrentSystemId << 24) | ((long)(d.OwnerId + 1) << 8) | (long)(int)d.Type;
                    if (!s_orbit.TryGetValue(key, out var list)) s_orbit[key] = list = NewList();
                    list.Add(fv);
                }
                else if (Travels(d)) s_movers.Add(fv);
            }

            // Попутчики: по номеру флота, каждый — в первую группу своего коридора, чей ведущий почти вровень
            s_movers.Sort((a, b) => a.Data.Id.CompareTo(b.Data.Id));
            foreach (var fv in s_movers)
            {
                var d = fv.Data;
                long key = ((long)d.CurrentSystemId << 40) | ((long)d.TargetSystemId << 20) | ((long)(d.OwnerId + 1) << 4) | (long)(int)d.Type;
                if (!s_transit.TryGetValue(key, out var clusters)) s_transit[key] = clusters = new List<List<FleetView>>();
                float p = Progress(d);
                List<FleetView> home = null;
                foreach (var c in clusters)
                    if (Mathf.Abs(Progress(c[0].Data) - p) < TransitTogether) { home = c; break; }
                if (home == null) clusters.Add(home = NewList());
                home.Add(fv);
            }

            foreach (var l in s_orbit.Values)
            {
                if (l.Count < 2) continue;
                l.Sort((a, b) => a.Data.Id.CompareTo(b.Data.Id));
                foreach (var f in l) s_groupOf[f] = l;
            }
            foreach (var clusters in s_transit.Values)
                foreach (var l in clusters)
                {
                    if (l.Count < 2) continue;   // уже по номеру — попутчики добавлялись в порядке Id
                    foreach (var f in l) s_groupOf[f] = l;
                }
        }
    }
}
