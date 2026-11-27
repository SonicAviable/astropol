using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Живые гиперкоридоры: по коридорам, где сейчас летят флоты, бегут световые импульсы в сторону движения.
    ///   • свои флоты — весь оставшийся маршрут (текущий отрезок от корабля и дальше по пути), бирюзовым;
    ///   • видимые чужие флоты — только текущий отрезок, цветом их империи.
    /// Несколько флотов на одном отрезке (строй) дают одну линию. Линии лежат в контейнере коридоров,
    /// поэтому прячутся вместе с ними при входе в систему. Набор линий пересобирается 5 раз в секунду,
    /// движение импульсов — в шейдере LanePulse (без затрат на CPU).
    /// </summary>
    public partial class GalaxyView
    {
        private readonly List<LineRenderer> _routePool = new List<LineRenderer>();
        private readonly HashSet<long> _routeKeys = new HashSet<long>();
        private Material _routeMat;
        private MaterialPropertyBlock _routeMpb;
        private float _routeTimer;
        private bool _routeShaderMissing;

        private static readonly int IdRouteColor = Shader.PropertyToID("_Color");
        private static readonly int IdRouteLen = Shader.PropertyToID("_Len");
        private static readonly int IdRouteSpeed = Shader.PropertyToID("_Speed");

        private void UpdateRoutes()
        {
            _routeTimer -= Time.unscaledDeltaTime;
            if (_routeTimer > 0f) return;
            _routeTimer = 0.2f;
            if (_hyperlanesParent == null || _generator == null || _routeShaderMissing) return;

            if (_routeMat == null)
            {
                var sh = Resources.Load<Shader>("Shaders/LanePulse");
                if (sh == null) { _routeShaderMissing = true; return; }
                _routeMat = new Material(sh) { name = "LanePulse" };
                _routeMpb = new MaterialPropertyBlock();
            }

            int used = 0;
            _routeKeys.Clear();
            var fm = FleetManager.Instance;
            if (fm != null)
            {
                foreach (var f in fm.AllFleets)
                {
                    var d = f != null ? f.Data : null;
                    if (d == null || d.Destroyed || d.State != FleetState.InHyperlane || d.TargetSystemId < 0) continue;
                    bool mine = d.OwnerId == 0;
                    if (!mine && !Vision.CanSeeFleet(0, d)) continue;
                    if (d.TargetSystemId >= _generator.Systems.Count) continue;

                    Color col = mine ? UIManager.DS.NeonCyan : FleetIndicator.OwnerColor(d.OwnerId);
                    // Текущий отрезок — от корабля (первый флот строя задаёт начало)
                    if (_routeKeys.Add(RouteKey(d.CurrentSystemId, d.TargetSystemId, d.OwnerId)))
                        PlaceRoute(ref used, d.CurrentSystemId, d.TargetSystemId, f.transform.position, col, mine ? 1f : 0.75f);
                    if (!mine) continue;

                    // Дальше по маршруту — только у своих
                    int from = d.TargetSystemId;
                    foreach (int step in d.Path)
                    {
                        if (step < 0 || step >= _generator.Systems.Count || step == from) continue;
                        if (_routeKeys.Add(RouteKey(from, step, 0)))
                            PlaceRoute(ref used, from, step, null, col, 0.7f);
                        from = step;
                    }
                }
            }

            for (int i = used; i < _routePool.Count; i++)
                if (_routePool[i] != null && _routePool[i].enabled) _routePool[i].enabled = false;
        }

        private static long RouteKey(int a, int b, int owner) => ((long)owner << 40) | ((long)(a & 0xFFFFF) << 20) | (uint)(b & 0xFFFFF);

        private const int RouteSegs = 16;
        private static readonly Vector3[] s_routeBuf = new Vector3[RouteSegs];

        /// <summary>
        /// Маршрут по дуге коридора (LaneCurve) от системы from к to; ship — позиция корабля в пути:
        /// линия начинается от ближайшей к нему точки дуги.
        /// </summary>
        private void PlaceRoute(ref int used, int from, int to, Vector3? ship, Color col, float strength)
        {
            if (from < 0 || to < 0 || from >= _generator.Systems.Count || to >= _generator.Systems.Count || from == to) return;
            var sa = _generator.Systems[from];
            var sb = _generator.Systems[to];
            float t0 = 0f;
            if (ship.HasValue)
            {
                float best = float.MaxValue;
                for (int k = 0; k <= 24; k++)
                {
                    float tk = k / 24f;
                    Vector3 q = LaneCurve.Point(sa, sb, tk) - ship.Value;
                    q.y = 0f;
                    if (q.sqrMagnitude < best) { best = q.sqrMagnitude; t0 = tk; }
                }
            }
            float len = 0f;
            for (int k = 0; k < RouteSegs; k++)
            {
                Vector3 p = LaneCurve.Point(sa, sb, Mathf.Lerp(t0, 1f, k / (RouteSegs - 1f)));
                p.y = 0.02f;
                if (k > 0) len += Vector3.Distance(p, s_routeBuf[k - 1]);
                s_routeBuf[k] = p;
            }
            if (len < 0.3f) return;

            LineRenderer lr;
            if (used < _routePool.Count) lr = _routePool[used];
            else
            {
                var go = new GameObject("Route");
                go.transform.SetParent(_hyperlanesParent.transform, false);
                lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = RouteSegs;
                lr.textureMode = LineTextureMode.Stretch;
                lr.numCapVertices = 0;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sortingOrder = 4;                    // над коридорами (3), под системами
                lr.sharedMaterial = _routeMat;
                _routePool.Add(lr);
            }
            used++;

            lr.SetPositions(s_routeBuf);
            // Тонкая линия: сердцевина — волосок по центру ширины, остальное — мягкий ореол
            float w = 0.16f + 0.08f * strength;
            lr.startWidth = lr.endWidth = w;
            lr.enabled = true;

            _routeMpb.Clear();
            _routeMpb.SetColor(IdRouteColor, new Color(col.r, col.g, col.b, Mathf.Clamp01(strength)));
            _routeMpb.SetFloat(IdRouteLen, len);
            _routeMpb.SetFloat(IdRouteSpeed, 4f);
            lr.SetPropertyBlock(_routeMpb);
        }
    }
}
