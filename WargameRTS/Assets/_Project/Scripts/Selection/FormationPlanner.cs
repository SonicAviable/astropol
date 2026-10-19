using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using WargameRTS.Map;
using WargameRTS.Units;

namespace WargameRTS.Selection
{
    public struct FormationSlot
    {
        public Unit Unit;
        public Vector3 Position;
    }

    /// <summary>Spreads a group of units into a line formation (Wargame-style).</summary>
    public static class FormationPlanner
    {
        public const float Spacing = 22f;
        const int MaxPerRow = 8;

        /// <summary>Formation centered on a point, units facing <paramref name="facing"/>.</summary>
        public static List<FormationSlot> AroundPoint(IReadOnlyList<Unit> units, Vector3 center, Vector3 facing)
        {
            facing = Flat(facing, Vector3.forward);
            Vector3 right = Vector3.Cross(Vector3.up, facing);
            return Assign(units, Grid(units.Count, center, facing, Spacing), right);
        }

        /// <summary>Rows of up to 8 slots centered on a point, front row first.</summary>
        public static List<Vector3> Grid(int count, Vector3 center, Vector3 facing, float spacing)
        {
            facing = Flat(facing, Vector3.forward);
            Vector3 right = Vector3.Cross(Vector3.up, facing);
            var positions = new List<Vector3>(count);
            if (count <= 0)
                return positions;

            int rows = Mathf.CeilToInt(count / (float)MaxPerRow);
            int perRow = Mathf.CeilToInt(count / (float)rows);
            for (int i = 0; i < count; i++)
            {
                int row = i / perRow;
                int column = i % perRow;
                int inRow = Mathf.Min(perRow, count - row * perRow);
                float x = (column - (inRow - 1) * 0.5f) * spacing;
                float z = -row * spacing;
                positions.Add(center + right * x + facing * z);
            }
            return positions;
        }

        /// <summary>Units spread evenly along the segment a→b, facing away from the group.</summary>
        public static List<FormationSlot> AlongLine(IReadOnlyList<Unit> units, Vector3 a, Vector3 b, out Vector3 facing)
        {
            Vector3 along = Flat(b - a, Vector3.right);
            facing = Vector3.Cross(along, Vector3.up);
            if (Vector3.Dot(facing, (a + b) * 0.5f - Centroid(units)) < 0f)
                facing = -facing;

            int count = units.Count;
            var positions = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                positions.Add(Vector3.Lerp(a, b, t));
            }
            return Assign(units, positions, Vector3.Cross(Vector3.up, facing));
        }

        public static Vector3 Centroid(IReadOnlyList<Unit> units)
        {
            if (units.Count == 0)
                return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (var unit in units)
                sum += unit.Position;
            return sum / units.Count;
        }

        /// <summary>Matches units to slots left-to-right so paths do not cross.</summary>
        static List<FormationSlot> Assign(IReadOnlyList<Unit> units, List<Vector3> positions, Vector3 right)
        {
            var sortedUnits = new List<Unit>(units);
            sortedUnits.Sort((u1, u2) => Vector3.Dot(u1.Position, right).CompareTo(Vector3.Dot(u2.Position, right)));
            positions.Sort((p1, p2) => Vector3.Dot(p1, right).CompareTo(Vector3.Dot(p2, right)));

            var map = GameMap.Instance;
            var slots = new List<FormationSlot>(units.Count);
            for (int i = 0; i < sortedUnits.Count; i++)
            {
                Vector3 position = positions[i];
                if (map != null)
                    position = map.OnGround(map.Clamp(position, 20f));
                if (NavMesh.SamplePosition(position, out var hit, 40f, NavMesh.AllAreas))
                    position = hit.position;
                slots.Add(new FormationSlot { Unit = sortedUnits[i], Position = position });
            }
            return slots;
        }

        static Vector3 Flat(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return direction.sqrMagnitude < 0.01f ? fallback : direction.normalized;
        }
    }
}
