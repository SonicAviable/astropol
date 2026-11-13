using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Форма гиперкоридора «Гиперпоток»: пологая квадратичная дуга между системами (вершина смещена вбок
    /// на 3,5 % длины). Одна и та же для линии коридора, полёта кораблей и маршрутов — корабли не срезают изгиб.
    /// Направление изгиба зависит только от пары систем, так что дуга A→B совпадает с B→A.
    /// </summary>
    public static class LaneCurve
    {
        /// <summary>Смещение контрольной точки в долях длины (вершина дуги — вдвое меньше).</summary>
        public const float Bend = 0.07f;

        private static void Control(StarSystem a, StarSystem b, out Vector3 p0, out Vector3 p1, out Vector3 p2, out bool flipped)
        {
            flipped = a.Id > b.Id;
            var lo = flipped ? b : a;
            var hi = flipped ? a : b;
            p0 = lo.Position;
            p2 = hi.Position;
            Vector3 d = p2 - p0;
            d.y = 0f;
            float len = d.magnitude;
            Vector3 n = len > 1e-4f ? new Vector3(-d.z, 0f, d.x) / len : Vector3.zero;
            float sign = (((lo.Id * 73856093) ^ (hi.Id * 19349663)) & 1) == 0 ? 1f : -1f;
            p1 = (p0 + p2) * 0.5f + n * (len * Bend * sign);
        }

        /// <summary>Точка дуги: t = 0 — центр системы a, 1 — центр b.</summary>
        public static Vector3 Point(StarSystem a, StarSystem b, float t)
        {
            Control(a, b, out var p0, out var p1, out var p2, out bool flipped);
            if (flipped) t = 1f - t;
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        /// <summary>Направление движения от a к b в точке t (не нормировано).</summary>
        public static Vector3 Tangent(StarSystem a, StarSystem b, float t)
        {
            Control(a, b, out var p0, out var p1, out var p2, out bool flipped);
            if (flipped) t = 1f - t;
            Vector3 d = 2f * (1f - t) * (p1 - p0) + 2f * t * (p2 - p1);
            return flipped ? -d : d;
        }
    }
}
