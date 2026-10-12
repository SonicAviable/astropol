using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Строй на орбите: флоты одного владельца и одного типа, стоящие в одной системе, не наезжают друг на друга,
    /// а встают единой группой — клином за ведущим (флот с меньшим номером), носами в одну сторону.
    /// Больше семи флотов — следующий клин встаёт позади первого.
    /// Группы пересчитываются раз в кадр, по запросу первого же флота.
    /// </summary>
    public static class FleetFormation
    {
        public struct Slot
        {
            public int Index;          // место в строю (0 — ведущий)
            public int Count;          // флотов в группе
            public FleetView Leader;
            public int SelectedCount;  // выделенных флотов в группе
            public FleetView FirstSelected;
        }

        private static int s_frame = -1;
        private static readonly Dictionary<long, List<FleetView>> s_groups = new Dictionary<long, List<FleetView>>();
        private static readonly Dictionary<FleetView, long> s_keyOf = new Dictionary<FleetView, long>();

        /// <summary>Стоит ли флот в строю: на орбите, не в бою и не за работой.</summary>
        public static bool Holds(FleetData d) => d != null && !d.Destroyed && !d.InCombat
                                                 && d.State == FleetState.Orbiting && d.CurrentSystemId >= 0;

        public static Slot Get(FleetView v)
        {
            Rebuild();
            var s = new Slot { Index = 0, Count = 1, Leader = v, SelectedCount = v != null && v.IsSelected ? 1 : 0, FirstSelected = v };
            if (v == null || !s_keyOf.TryGetValue(v, out long key) || !s_groups.TryGetValue(key, out var list)) return s;
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

        private static void Rebuild()
        {
            if (s_frame == Time.frameCount) return;
            s_frame = Time.frameCount;
            foreach (var l in s_groups.Values) l.Clear();
            s_keyOf.Clear();

            var fm = FleetManager.Instance;
            if (fm == null) return;
            foreach (var fv in fm.AllFleets)
            {
                var d = fv != null ? fv.Data : null;
                if (!Holds(d)) continue;
                long key = ((long)d.CurrentSystemId << 24) | ((long)(d.OwnerId + 1) << 8) | (long)(int)d.Type;
                if (!s_groups.TryGetValue(key, out var list)) s_groups[key] = list = new List<FleetView>();
                list.Add(fv);
                s_keyOf[fv] = key;
            }
            foreach (var l in s_groups.Values)
                if (l.Count > 1) l.Sort((a, b) => a.Data.Id.CompareTo(b.Data.Id));
        }
    }
}
