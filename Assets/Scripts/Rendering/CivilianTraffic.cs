using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Гражданская жизнь галактики — чисто визуальная, на экономику и бои не влияет:
    ///   • торговцы летают по гиперкоридорам внутри империй и между соседями, которые не воюют
    ///     (светлый грузовой корпус с гондолами в цвет владельца, тонкий светящийся след);
    ///   • патрули ИИ-империй кружат у пограничных систем (малые корветы фракции).
    /// Видно только то, что видит игрок (туман войны); в окне системы трафик скрыт; на паузе всё замирает.
    /// Число кораблей ограничено и растёт с числом коридоров.
    /// </summary>
    public class CivilianTraffic : MonoBehaviour
    {
        private const int MaxTraders = 10, MaxPatrols = 4;
        private const float TraderLength = 0.7f, PatrolLength = 0.9f;
        /// <summary>Длина корвета из ShipMeshFactory (3.0 × 0.62) — от неё масштаб патруля.</summary>
        private const float CorvetteModelLength = 1.86f;
        private const float StarClearance = 3.2f;     // торговцы выходят и входят на краю мини-системы, а не из звезды
        private const float LaneLift = 0.05f;          // ниже флотов (те идут на +0.5)
        private const float RebuildEvery = 3f;

        private class Lane { public int A, B, Owner; public bool Border; }
        private class Trader
        {
            public Transform T;
            public Lane Lane;
            public bool Forward;
            public float Pos, Len, Lateral, Speed, Life;
            /// <summary>Масштаб модели: 1 — корпус Star Sparrow нужной длины, меньше — запасной процедурный.</summary>
            public float BaseScale = 1f;
            public bool Leaving;
        }
        private class Patrol
        {
            public Transform T;
            public int System, Owner;
            public float Angle, Radius, Speed, Life;
            public bool Leaving;
        }

        private readonly List<Lane> _lanes = new List<Lane>();
        private readonly Dictionary<int, List<Lane>> _byNode = new Dictionary<int, List<Lane>>();
        private readonly List<int> _borderSystems = new List<int>();
        private readonly List<Trader> _traders = new List<Trader>();
        private readonly List<Patrol> _patrols = new List<Patrol>();
        private float _rebuild;
        private bool _systemView;
        private Transform _root;
        private static Material s_trailMat;

        private void Start()
        {
            _root = new GameObject("CivilianTrafficRoot").transform;
            _root.SetParent(transform, false);
            SystemViewManager.OnViewModeChanged += OnViewMode;
        }

        private void OnDestroy()
        {
            SystemViewManager.OnViewModeChanged -= OnViewMode;
        }

        private void OnViewMode(bool systemView)
        {
            _systemView = systemView;
            if (_root != null) _root.gameObject.SetActive(!systemView);
        }

        private void Update()
        {
            if (!UIManager.IsGameStarted || EmpireStats.Systems.Count == 0)
            {
                if (_traders.Count + _patrols.Count > 0) Clear();
                return;
            }
            if (_systemView) return;

            _rebuild -= Time.unscaledDeltaTime;
            if (_rebuild <= 0f)
            {
                _rebuild = RebuildEvery;
                RebuildRoutes();
                Populate();
            }

            var tm = TimeManager.Instance;
            int speed = tm != null ? tm.CurrentSpeed : 1;
            float dt = speed <= 0 ? 0f : Time.deltaTime * (0.6f + 0.4f * speed);
            TickTraders(dt);
            TickPatrols(dt);
        }

        // ==================== МАРШРУТЫ ====================

        private static bool Civil(int owner) => owner >= 0 && !Threats.IsThreat(owner);

        private static bool Visible(int systemId) => !Vision.Active || Vision.PlayerSees(systemId);

        private void RebuildRoutes()
        {
            _lanes.Clear();
            _byNode.Clear();
            _borderSystems.Clear();
            var systems = EmpireStats.Systems;
            foreach (var s in systems)
            {
                if (!Civil(s.OwnerId)) continue;
                bool border = false;
                foreach (int n in s.ConnectedSystemIds)
                {
                    var o = EmpireStats.GetSystem(n);
                    if (o == null) continue;
                    if (o.OwnerId != s.OwnerId && o.OwnerId >= 0) border = true;
                    if (n < s.Id || !Civil(o.OwnerId)) continue;
                    bool same = o.OwnerId == s.OwnerId;
                    // Между империями торгуют, только если они не воюют
                    if (!same && Diplomacy.AtWar(s.OwnerId, o.OwnerId)) continue;
                    if (!Visible(s.Id) && !Visible(n)) continue;
                    var lane = new Lane { A = s.Id, B = n, Owner = s.OwnerId, Border = !same };
                    _lanes.Add(lane);
                    AddNode(s.Id, lane);
                    AddNode(n, lane);
                }
                if (border && s.OwnerId > 0 && Visible(s.Id)) _borderSystems.Add(s.Id);
            }
        }

        private void AddNode(int id, Lane lane)
        {
            if (!_byNode.TryGetValue(id, out var list)) _byNode[id] = list = new List<Lane>();
            list.Add(lane);
        }

        private void Populate()
        {
            int wantTraders = Mathf.Min(MaxTraders, Mathf.CeilToInt(_lanes.Count * 0.2f));
            int alive = 0;
            foreach (var t in _traders) if (!t.Leaving) alive++;
            for (int i = alive; i < wantTraders && _lanes.Count > 0; i++)
            {
                // Внутренние коридоры — втрое чаще пограничных
                Lane lane = _lanes[Random.Range(0, _lanes.Count)];
                if (lane.Border && Random.value < 0.66f) lane = _lanes[Random.Range(0, _lanes.Count)];
                SpawnTrader(lane, Random.value < 0.5f, Random.value * 0.8f);
            }

            int wantPatrols = Mathf.Min(MaxPatrols, Mathf.CeilToInt(_borderSystems.Count * 0.35f));
            alive = 0;
            foreach (var p in _patrols) if (!p.Leaving) alive++;
            for (int i = alive; i < wantPatrols; i++)
            {
                int sys = _borderSystems[Random.Range(0, _borderSystems.Count)];
                bool taken = false;
                foreach (var p in _patrols) if (p.System == sys && !p.Leaving) { taken = true; break; }
                if (!taken) SpawnPatrol(sys);
            }
            // Патруль в системе, которая больше не пограничная (или ушла под туман), — улетает
            foreach (var p in _patrols)
                if (!p.Leaving && !_borderSystems.Contains(p.System)) p.Leaving = true;
        }

        // ==================== ТОРГОВЦЫ ====================

        private void SpawnTrader(Lane lane, bool forward, float startFrac)
        {
            var owner = FleetIndicator.OwnerColor(lane.Owner);
            var holder = new GameObject("Trader").transform;
            holder.SetParent(_root, false);
            float baseScale = 1f;
            if (StarSparrowModel.BuildTrader(holder, owner, TraderLength) == null)
            {
                var v = ShipMeshFactory.Build(holder, FleetType.Constructor, ShipClass.Corvette, owner, lane.Owner);
                if (v == null) { Destroy(holder.gameObject); return; }
                baseScale = 0.24f;
            }
            AddTrail(holder, Color.Lerp(owner, Color.white, 0.35f));

            var t = new Trader
            {
                T = holder, Lane = lane, Forward = forward,
                Lateral = Random.Range(0.45f, 1.1f) * (Random.value < 0.5f ? -1f : 1f),
                Speed = Random.Range(2.6f, 4.2f), Life = 0f, BaseScale = baseScale
            };
            t.Len = LaneLength(lane);
            t.Pos = t.Len * startFrac;
            _traders.Add(t);
            PlaceTrader(t);
        }

        private float LaneLength(Lane lane)
        {
            var a = EmpireStats.GetSystem(lane.A);
            var b = EmpireStats.GetSystem(lane.B);
            if (a == null || b == null) return 1f;
            return Mathf.Max(0.5f, Vector3.Distance(a.Position, b.Position) - StarClearance * 2f);
        }

        private void TickTraders(float dt)
        {
            for (int i = _traders.Count - 1; i >= 0; i--)
            {
                var t = _traders[i];
                if (t.T == null) { _traders.RemoveAt(i); continue; }
                t.Life += dt;
                t.Pos += t.Speed * dt;

                if (t.Pos >= t.Len)
                {
                    // Прибыл: дальше по другому коридору из этой системы или уходит «в порт»
                    int at = t.Forward ? t.Lane.B : t.Lane.A;
                    if (!t.Leaving && Random.value < 0.65f && _byNode.TryGetValue(at, out var next) && next.Count > 0)
                    {
                        var lane = next[Random.Range(0, next.Count)];
                        t.Lane = lane;
                        t.Forward = lane.A == at;
                        t.Len = LaneLength(lane);
                        t.Pos = 0f;
                        t.Life = 0.6f;   // без повторного «появления»
                    }
                    else
                    {
                        Destroy(t.T.gameObject);
                        _traders.RemoveAt(i);
                        continue;
                    }
                }
                PlaceTrader(t);
            }
        }

        private void PlaceTrader(Trader t)
        {
            var a = EmpireStats.GetSystem(t.Forward ? t.Lane.A : t.Lane.B);
            var b = EmpireStats.GetSystem(t.Forward ? t.Lane.B : t.Lane.A);
            if (a == null || b == null) return;
            Vector3 dir = b.Position - a.Position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            var side = new Vector3(-dir.z, 0f, dir.x);
            Vector3 p = a.Position + dir * (StarClearance + t.Pos) + side * t.Lateral + Vector3.up * LaneLift;
            t.T.position = p;
            t.T.rotation = Quaternion.LookRotation(dir, Vector3.up);

            // Появление и уход — плавным масштабом у краёв коридора
            float edge = Mathf.Min(t.Pos, t.Len - t.Pos);
            float s = Mathf.Clamp01(edge / 1.2f);
            if (t.Life < 0.6f) s = Mathf.Min(s, t.Life / 0.6f);
            t.T.localScale = Vector3.one * Mathf.Max(0.001f, s * t.BaseScale);

            bool vis = Visible(t.Lane.A) || Visible(t.Lane.B);
            if (t.T.gameObject.activeSelf != vis) t.T.gameObject.SetActive(vis);
        }

        // ==================== ПАТРУЛИ ====================

        private void SpawnPatrol(int systemId)
        {
            var sys = EmpireStats.GetSystem(systemId);
            if (sys == null) return;
            var color = FleetIndicator.OwnerColor(sys.OwnerId);
            var holder = new GameObject("Patrol").transform;
            holder.SetParent(_root, false);
            var v = ShipMeshFactory.Build(holder, FleetType.Military, ShipClass.Corvette, color, sys.OwnerId);
            if (v == null) { Destroy(holder.gameObject); return; }
            holder.localScale = Vector3.one * (PatrolLength / CorvetteModelLength);
            AddTrail(holder, Color.Lerp(color, Color.white, 0.25f));
            _patrols.Add(new Patrol
            {
                T = holder, System = systemId, Owner = sys.OwnerId,
                Angle = Random.value * Mathf.PI * 2f, Radius = Random.Range(4.8f, 6.2f),
                Speed = Random.Range(0.22f, 0.32f) * (Random.value < 0.5f ? -1f : 1f)
            });
        }

        private void TickPatrols(float dt)
        {
            for (int i = _patrols.Count - 1; i >= 0; i--)
            {
                var p = _patrols[i];
                var sys = EmpireStats.GetSystem(p.System);
                if (p.T == null || sys == null || sys.OwnerId != p.Owner) { if (p.T != null) Destroy(p.T.gameObject); _patrols.RemoveAt(i); continue; }
                p.Life += p.Leaving ? -dt : dt;
                if (p.Leaving && p.Life <= 0f) { Destroy(p.T.gameObject); _patrols.RemoveAt(i); continue; }

                p.Angle += p.Speed * dt;
                var pos = sys.Position + new Vector3(Mathf.Cos(p.Angle) * p.Radius, 0.9f + 0.15f * Mathf.Sin(p.Angle * 3f), Mathf.Sin(p.Angle) * p.Radius);
                var tangent = new Vector3(-Mathf.Sin(p.Angle), 0f, Mathf.Cos(p.Angle)) * Mathf.Sign(p.Speed);
                p.T.position = pos;
                p.T.rotation = Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(0f, 0f, -14f * Mathf.Sign(p.Speed));
                float s = Mathf.Clamp01(p.Life / 0.8f) * (PatrolLength / CorvetteModelLength);
                p.T.localScale = Vector3.one * Mathf.Max(0.001f, s);

                bool vis = Visible(p.System);
                if (p.T.gameObject.activeSelf != vis) p.T.gameObject.SetActive(vis);
            }
        }

        // ==================== ОБЩЕЕ ====================

        private static void AddTrail(Transform holder, Color c)
        {
            if (s_trailMat == null)
            {
                var add = Resources.Load<Shader>("Shaders/CombatAdditive") ?? ShaderCache.Sprite;
                s_trailMat = new Material(add) { name = "TrafficTrail", hideFlags = HideFlags.DontSave };
            }
            var go = new GameObject("Trail");
            go.transform.SetParent(holder, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.6f);
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.6f;
            tr.minVertexDistance = 0.12f;
            tr.widthMultiplier = 0.07f;
            tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            tr.startColor = new Color(c.r, c.g, c.b, 0.35f);
            tr.endColor = new Color(c.r, c.g, c.b, 0f);
            tr.sharedMaterial = s_trailMat;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
        }

        private void Clear()
        {
            foreach (var t in _traders) if (t.T != null) Destroy(t.T.gameObject);
            foreach (var p in _patrols) if (p.T != null) Destroy(p.T.gameObject);
            _traders.Clear();
            _patrols.Clear();
        }
    }
}
