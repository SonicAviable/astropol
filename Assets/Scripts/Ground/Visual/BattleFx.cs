using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using Random = UnityEngine.Random;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Эффекты боя на пулах: трассеры (LineRenderer), вспышки выстрелов (короткий свет), попадания, взрывы
    /// (префабы ParticleVfx + вспышка света), навесные снаряды, десантные капсулы с огненным следом,
    /// орбитальный удар (прицел, столб света, ударная волна), обрушение зданий и остовы техники с дымом.
    /// </summary>
    public sealed class BattleFx : MonoBehaviour
    {
        private sealed class TracerSlot { public LineRenderer Lr; public float Life, Max; public Color C; }
        private sealed class Flash { public Light L; public float Life, Max, Intensity; }
        private sealed class Timed { public float At; public Action Act; }
        private sealed class Mover { public Transform T; public Vector3 From, To; public float Life, Max, Arc; public Action Done; public TrailRenderer Trail; }

        private readonly List<TracerSlot> _tracers = new List<TracerSlot>();
        private readonly List<Flash> _flashes = new List<Flash>();
        private readonly List<Timed> _timed = new List<Timed>();
        private readonly List<Mover> _movers = new List<Mover>();
        private readonly List<(Transform t, float until, float rise)> _smokes = new List<(Transform, float, float)>();
        private int _flashIndex;
        private const int FlashPool = 8;

        // ================================================================ Пулы

        private TracerSlot GetTracer()
        {
            foreach (var t in _tracers) if (t.Life <= 0f) return t;
            var go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = GroundMats.Additive;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            var tr = new TracerSlot { Lr = lr };
            _tracers.Add(tr);
            return tr;
        }

        private Flash GetFlash()
        {
            if (_flashes.Count < FlashPool)
            {
                var go = new GameObject("Flash");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.enabled = false;
                var f = new Flash { L = l };
                _flashes.Add(f);
                return f;
            }
            _flashIndex = (_flashIndex + 1) % FlashPool;
            return _flashes[_flashIndex];
        }

        public void FlashLight(Vector3 pos, Color c, float intensity, float range, float life)
        {
            var f = GetFlash();
            f.L.transform.position = pos;
            f.L.color = c;
            f.L.range = range;
            f.Intensity = intensity;
            f.L.intensity = intensity;
            f.L.enabled = true;
            f.Life = f.Max = life;
        }

        public void Delayed(float delay, Action act) => _timed.Add(new Timed { At = Time.time + delay, Act = act });

        // ================================================================ Стрельба

        public void Tracer(Vector3 from, Vector3 to, WeaponDef w, Color side)
        {
            if (w.Kind == WeaponKind.Melee) return;
            var t = GetTracer();
            Color c;
            float width, life;
            switch (w.Kind)
            {
                case WeaponKind.Energy: c = Color.Lerp(side, Color.white, 0.4f) * 2.2f; width = 0.09f; life = 0.09f; break;
                case WeaponKind.Flame: c = new Color(1f, 0.55f, 0.15f) * 2f; width = 0.5f; life = 0.12f; break;
                case WeaponKind.Sniper: c = new Color(1f, 0.95f, 0.8f) * 2.4f; width = 0.05f; life = 0.25f; break;
                case WeaponKind.Explosive: c = new Color(1f, 0.7f, 0.4f) * 1.6f; width = 0.18f; life = 0.15f; break;
                default: c = new Color(1f, 0.85f, 0.45f) * 1.8f; width = 0.05f; life = 0.06f; break;
            }
            if (w.Kind == WeaponKind.Flame)
            {
                // Струя огня — короткие языки до цели
                for (int i = 0; i < 3; i++)
                    Fire(Vector3.Lerp(from, to, Random.Range(0.4f, 1f)) + Random.insideUnitSphere * 0.4f, 0.6f);
            }
            t.Lr.SetPosition(0, from);
            t.Lr.SetPosition(1, w.Kind == WeaponKind.Bullet || w.Kind == WeaponKind.Heavy ? Vector3.Lerp(from, to, Random.Range(0.55f, 1f)) : to);
            t.Lr.startWidth = width;
            t.Lr.endWidth = width * 0.6f;
            t.C = c;
            t.Lr.startColor = t.Lr.endColor = c;
            t.Lr.enabled = true;
            t.Life = t.Max = life;
        }

        public void MuzzleFlash(Vector3 at, WeaponDef w)
        {
            if (w.Kind == WeaponKind.Melee || w.Kind == WeaponKind.Flame) return;
            float big = w.Kind == WeaponKind.Explosive || w.Indirect ? 2.5f : w.Kind == WeaponKind.Heavy ? 1.2f : 0.7f;
            if (Random.value < 0.35f || big > 1f)
                FlashLight(at, w.Kind == WeaponKind.Energy ? new Color(0.6f, 0.9f, 1f) : new Color(1f, 0.75f, 0.4f), 2.5f * big, 5f * big, 0.05f + 0.03f * big);
            if (big > 2f) ParticleVfx.Play(ParticleVfx.Effect.TinyExplosion, at, 0.5f);
        }

        public void Impact(Vector3 at, WeaponDef w)
        {
            if (Random.value < 0.4f) GoreFx.Dust(at, 0.4f);
        }

        public void Sparks(Vector3 at, float size)
        {
            ParticleVfx.Play(ParticleVfx.Effect.Sparks, at, size);
        }

        public void Explosion(Vector3 at, float radius)
        {
            ParticleVfx.Play(radius >= 4f ? ParticleVfx.Effect.BigExplosion : radius >= 1.8f ? ParticleVfx.Effect.SmallExplosion : ParticleVfx.Effect.TinyExplosion,
                at + Vector3.up * 0.3f, Mathf.Clamp(radius * 0.45f, 0.4f, 3f), null, -1f, Vector3.up);
            FlashLight(at + Vector3.up * 1.5f, new Color(1f, 0.6f, 0.25f), 6f + radius * 2f, radius * 5f + 6f, 0.15f);
            GoreFx.Dust(at, radius * 0.6f);
        }

        public void Fire(Vector3 at, float size)
        {
            GoreFx.Flame(at, size);
        }

        /// <summary>Навесной снаряд по дуге; done — в точке падения.</summary>
        public void Shell(Vector3 from, Vector3 to, float time, Action done)
        {
            var go = new GameObject("Shell");
            go.transform.SetParent(transform, false);
            go.transform.position = from;
            var tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = GroundMats.Additive;
            tr.time = 0.35f;
            tr.startWidth = 0.25f;
            tr.endWidth = 0f;
            tr.startColor = new Color(1f, 0.7f, 0.35f, 1f);
            tr.endColor = new Color(0.5f, 0.5f, 0.5f, 0f);
            _movers.Add(new Mover { T = go.transform, From = from, To = to, Life = 0f, Max = time, Arc = Mathf.Clamp(Vector3.Distance(from, to) * 0.35f, 6f, 40f), Done = done, Trail = tr });
            GroundSfx.Whistle(to, time);
        }

        // ================================================================ Капсулы и орбита

        /// <summary>Десантная капсула: падает с неба с огненным следом, удар, пыль, воронка; done — после удара.</summary>
        public void DropPod(Vector3 at, Color side, Action done)
        {
            var pod = new GameObject("DropPod");
            pod.transform.SetParent(transform, false);
            var hull = GroundMats.Lit(new Color(0.25f, 0.26f, 0.28f), 0.7f, 0.5f);
            GroundMats.Part(pod.transform, PrimitiveType.Cylinder, new Vector3(0f, 1.6f, 0f), new Vector3(2.2f, 1.6f, 2.2f), hull, "Body");
            GroundMats.Part(pod.transform, PrimitiveType.Sphere, new Vector3(0f, 3.3f, 0f), new Vector3(2.2f, 1.6f, 2.2f), hull, "Nose");
            GroundMats.Part(pod.transform, PrimitiveType.Cube, new Vector3(0f, 1.6f, 1.12f), new Vector3(1.2f, 2f, 0.05f), GroundMats.Glow(side * 1.6f), "Stripe");
            var tr = pod.AddComponent<TrailRenderer>();
            tr.sharedMaterial = GroundMats.Additive;
            tr.time = 0.6f;
            tr.startWidth = 2.2f;
            tr.endWidth = 0.2f;
            tr.startColor = new Color(1f, 0.6f, 0.2f, 1f);
            tr.endColor = new Color(0.3f, 0.3f, 0.3f, 0f);
            Vector3 from = at + new Vector3(Random.Range(-25f, 25f), 160f, Random.Range(-25f, 25f));
            pod.transform.position = from;
            GroundSfx.Whistle(at, 2.2f);
            _movers.Add(new Mover
            {
                T = pod.transform, From = from, To = at, Life = 0f, Max = 2.2f, Arc = 0f, Trail = tr, Done = () =>
                {
                    Explosion(at, 3f);
                    FlashLight(at + Vector3.up * 2f, new Color(1f, 0.6f, 0.3f), 10f, 18f, 0.25f);
                    GameObject.Destroy(pod.GetComponent<TrailRenderer>());
                    Smoke(pod.transform, 8f);
                    done?.Invoke();
                    Delayed(25f, () => { if (pod != null) Destroy(pod); });
                }
            });
        }

        /// <summary>Орбитальный удар: красный прицел 3 с, столб света, затем done (взрыв).</summary>
        public void OrbitalStrike(Vector3 at, Color side, Action done)
        {
            var mark = new GameObject("OrbitalMark");
            mark.transform.SetParent(transform, false);
            mark.transform.position = at + Vector3.up * 0.2f;
            var lr = mark.AddComponent<LineRenderer>();
            lr.sharedMaterial = GroundMats.Additive;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = 48;
            for (int i = 0; i < 48; i++) { float a = i / 48f * Mathf.PI * 2f; lr.SetPosition(i, new Vector3(Mathf.Cos(a) * 9f, 0f, Mathf.Sin(a) * 9f)); }
            lr.startWidth = lr.endWidth = 0.25f;
            lr.startColor = lr.endColor = new Color(1f, 0.15f, 0.1f, 1f) * 2f;
            var beam = new GameObject("TargetBeam").AddComponent<LineRenderer>();
            beam.transform.SetParent(mark.transform, false);
            beam.sharedMaterial = GroundMats.Additive;
            beam.positionCount = 2;
            beam.useWorldSpace = false;
            beam.SetPosition(0, Vector3.zero);
            beam.SetPosition(1, Vector3.up * 200f);
            beam.startWidth = beam.endWidth = 0.08f;
            beam.startColor = beam.endColor = new Color(1f, 0.1f, 0.1f, 1f) * 2f;
            GroundSfx.Charge(at);
            Delayed(3f, () =>
            {
                if (beam != null)
                {
                    beam.startWidth = 7f;
                    beam.endWidth = 9f;
                    beam.startColor = beam.endColor = Color.Lerp(side, Color.white, 0.7f) * 3f;
                }
                FlashLight(at + Vector3.up * 6f, Color.Lerp(side, Color.white, 0.6f), 30f, 60f, 0.5f);
                done?.Invoke();
                Delayed(0.45f, () => { if (mark != null) Destroy(mark); });
            });
        }

        // ================================================================ Разрушения

        public void Collapse(Destructible d)
        {
            if (d.Go == null) return;
            var t = d.Go.transform;
            // Здание оседает и темнеет: груда обломков
            var rubble = GroundMats.Lit(new Color(0.22f, 0.21f, 0.2f), 0f, 0.1f);
            foreach (var r in d.Renderers) if (r != null) r.sharedMaterial = rubble;
            _movers.Add(new Mover { T = t, From = t.position, To = t.position + Vector3.down * Mathf.Max(1f, d.Radius * 0.9f), Life = 0f, Max = 1.6f, Arc = -1f });
            Explosion(d.Pos + Vector3.up * 2f, Mathf.Clamp(d.Radius, 2f, 6f));
            for (int i = 0; i < 3; i++) Fire(d.Pos + Random.insideUnitSphere.WithY(0f) * d.Radius * 0.6f, 1.2f);
            Smoke(t, 40f);
            GoreFx.Debris(d.Pos, d.Radius);
        }

        /// <summary>Подбитая техника: чёрный остов, огонь и дым.</summary>
        public void VehicleWreck(Soldier v)
        {
            var burnt = GroundMats.Lit(new Color(0.08f, 0.07f, 0.07f), 0.3f, 0.15f);
            if (v.Rig?.Renderers != null) foreach (var r in v.Rig.Renderers) if (r != null) { r.sharedMaterial = burnt; r.enabled = true; }
            if (v.Rig?.Turret != null) v.Rig.Turret.localRotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(-60f, 60f), Random.Range(-10f, 10f));
            Explosion(v.Pos + Vector3.up, 3.5f);
            Fire(v.Pos + Vector3.up * 1.5f, 1.4f);
            Smoke(v.transform, 60f);
            GoreFx.Debris(v.Pos, 2.5f);
        }

        public void Smoke(Transform at, float seconds) => _smokes.Add((at, Time.time + seconds, 0f));

        // ================================================================ Такт

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _tracers.Count; i++)
            {
                var t = _tracers[i];
                if (t.Life <= 0f) continue;
                t.Life -= dt;
                if (t.Life <= 0f) { t.Lr.enabled = false; continue; }
                var c = t.C; c.a = t.Life / t.Max;
                t.Lr.startColor = t.Lr.endColor = c;
            }
            foreach (var f in _flashes)
            {
                if (f.Life <= 0f) continue;
                f.Life -= dt;
                f.L.intensity = f.Intensity * Mathf.Clamp01(f.Life / f.Max);
                if (f.Life <= 0f) f.L.enabled = false;
            }
            for (int i = _movers.Count - 1; i >= 0; i--)
            {
                var m = _movers[i];
                if (m.T == null) { _movers.RemoveAt(i); continue; }
                m.Life += dt;
                float k = Mathf.Clamp01(m.Life / m.Max);
                if (m.Arc < 0f) m.T.position = Vector3.Lerp(m.From, m.To, k * k);
                else
                {
                    var p = Vector3.Lerp(m.From, m.To, m.Arc > 0f ? k : k * k);
                    p.y += m.Arc * 4f * k * (1f - k);
                    m.T.position = p;
                }
                if (k >= 1f)
                {
                    _movers.RemoveAt(i);
                    m.Done?.Invoke();
                    if (m.Trail != null && m.T != null && m.T.name == "Shell") Destroy(m.T.gameObject, 0.4f);
                }
            }
            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                if (Time.time < _timed[i].At) continue;
                var a = _timed[i].Act;
                _timed.RemoveAt(i);
                a?.Invoke();
            }
            // Дым от горящих остовов и руин
            for (int i = _smokes.Count - 1; i >= 0; i--)
            {
                var s = _smokes[i];
                if (s.t == null || Time.time > s.until) { _smokes.RemoveAt(i); continue; }
                float next = s.rise - dt;
                if (next <= 0f) { GoreFx.SmokePuff(s.t.position + Vector3.up * 1.5f); next = 0.35f; }
                _smokes[i] = (s.t, s.until, next);
            }
        }
    }
}
