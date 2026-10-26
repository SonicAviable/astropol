using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Ground
{
    /// <summary>Тип укрытия: тяжёлое (бункер, здание, остов, обсидиан), лёгкое (мешки, воронки, лес), открытое, опасное (лава, вода).</summary>
    public enum CoverKind { None, Light, Heavy, Danger }

    /// <summary>
    /// Точка укрытия: место, куда встаёт один боец. Генерируются вместе с картой вокруг препятствий,
    /// добавляются на ходу (воронки, остовы, обломки). Остановившийся отряд сам расходится по ближайшим свободным точкам.
    /// </summary>
    public sealed class CoverPoint
    {
        public Vector3 Pos;
        public CoverKind Kind;
        /// <summary>Откуда укрытие защищает (направление от точки к препятствию); нуль — со всех сторон (воронка).</summary>
        public Vector3 Facing;
        public Soldier Occupant;
        /// <summary>Препятствие, к которому привязана точка (обломки и мешки разрушаются — точка исчезает).</summary>
        public Destructible Owner;
        public bool Alive = true;

        public bool Free => Alive && (Occupant == null || Occupant.Dead);
    }

    /// <summary>Все укрытия поля боя с быстрым поиском по сетке.</summary>
    public sealed class CoverMap
    {
        private const float Cell = 8f;
        private readonly Dictionary<long, List<CoverPoint>> _grid = new Dictionary<long, List<CoverPoint>>();
        public readonly List<CoverPoint> All = new List<CoverPoint>();

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        public CoverPoint Add(Vector3 pos, CoverKind kind, Vector3 facing, Destructible owner = null)
        {
            var c = new CoverPoint { Pos = pos, Kind = kind, Facing = facing, Owner = owner };
            All.Add(c);
            long k = Key(Mathf.FloorToInt(pos.x / Cell), Mathf.FloorToInt(pos.z / Cell));
            if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<CoverPoint>();
            l.Add(c);
            return c;
        }

        /// <summary>Укрытия в радиусе r от точки (живые).</summary>
        public void Query(Vector3 p, float r, List<CoverPoint> result)
        {
            result.Clear();
            int x0 = Mathf.FloorToInt((p.x - r) / Cell), x1 = Mathf.FloorToInt((p.x + r) / Cell);
            int z0 = Mathf.FloorToInt((p.z - r) / Cell), z1 = Mathf.FloorToInt((p.z + r) / Cell);
            float r2 = r * r;
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    if (_grid.TryGetValue(Key(x, z), out var l))
                        foreach (var c in l)
                        {
                            if (!c.Alive) continue;
                            float dx = c.Pos.x - p.x, dz = c.Pos.z - p.z;
                            if (dx * dx + dz * dz <= r2) result.Add(c);
                        }
        }

        /// <summary>Лучшее укрытие под бойцом (в пределах 1,6 м) против стрелка с направления from.</summary>
        public CoverKind CoverAt(Vector3 pos, Vector3 from, List<CoverPoint> scratch)
        {
            Query(pos, 1.6f, scratch);
            var best = CoverKind.None;
            foreach (var c in scratch)
            {
                if (c.Kind == CoverKind.Danger) { if (best == CoverKind.None) best = CoverKind.Danger; continue; }
                // Направленное укрытие защищает только от огня спереди
                if (c.Facing.sqrMagnitude > 0.01f)
                {
                    Vector3 toShooter = from - pos; toShooter.y = 0f;
                    if (toShooter.sqrMagnitude > 0.01f && Vector3.Dot(toShooter.normalized, c.Facing) < -0.2f) continue;
                }
                if (c.Kind == CoverKind.Heavy) return CoverKind.Heavy;
                if (c.Kind == CoverKind.Light) best = CoverKind.Light;
            }
            return best;
        }

        /// <summary>Снять точки, привязанные к разрушенному препятствию.</summary>
        public void RemoveOwned(Destructible d)
        {
            foreach (var c in All) if (c.Owner == d) c.Alive = false;
        }
    }
}
