using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    public partial class FaunaView
    {
        // ==================================================================== Солнечные скаты

        private sealed class RayVis : Vis
        {
            private static readonly Vector2[] Offs =
            {
                new Vector2(0f, 0f), new Vector2(-4f, -4f), new Vector2(4f, -4.5f), new Vector2(-8f, -8.5f),
                new Vector2(8f, -9f), new Vector2(-2.5f, -12.5f), new Vector2(3f, -14f)
            };
            private const int TailPts = 10;
            private Transform[] _rays;
            private float[] _ph;
            private Color[] _glow;
            private Lines _tails;
            private Vector3 _tg = Vector3.forward, _prevLead;
            private float _orbit;

            public override void Build(FaunaView v)
            {
                int n = Mathf.Clamp(H.Count, 0, Offs.Length);
                _rays = new Transform[n];
                _ph = new float[n];
                _glow = new Color[n];
                for (int i = 0; i < n; i++)
                {
                    var go = MeshObj("Ray" + i, Res.RayMesh, v._rayMat, Root.transform);
                    _ph[i] = R(0f, 6f);
                    _glow[i] = i % 3 == 2 ? new Color(0.54f, 0.71f, 1f) : new Color(0.36f, 0.95f, 0.88f);
                    var b = Track(go.GetComponent<MeshRenderer>());
                    b.SetColor(IdGlow, _glow[i]);
                    b.SetColor(IdBody, new Color(0.05f, 0.125f, 0.22f));
                    b.SetFloat(IdPhase, _ph[i]);
                    b.SetFloat(IdFlap, 2.1f);
                    go.transform.localScale = Vector3.one * (i == 0 ? 2.4f : R(1.8f, 2.1f));
                    _rays[i] = go.transform;
                }
                _tails = new Lines(n * (TailPts - 1), v._lineMat, Root.transform, "RayTails");
                Track(_tails.R);
                _orbit = R(0f, 6.28f);
            }

            protected override Vector3 LabelPoint => Lead;

            public override void Tick(FaunaView v, float t, float dt, Vector3 anchor)
            {
                // Отдых — стая кружит у звезды за орбитами мини-системы; в пути — летит по маршруту
                Vector3 target = anchor;
                if (!H.Moving)
                {
                    _orbit += dt * 0.22f;
                    target = anchor + new Vector3(Mathf.Cos(_orbit), 0f, Mathf.Sin(_orbit)) * 11f;
                }
                _prevLead = HasLead ? Lead : target;
                Follow(target, dt, 2f);
                Vector3 vel = dt > 0f ? (Lead - _prevLead) / dt : Vector3.zero;
                vel.y = 0f;
                if (vel.sqrMagnitude > 0.0004f) _tg = Vector3.Slerp(_tg, vel.normalized, Mathf.Min(1f, dt * 3f));
                Vector3 side = new Vector3(-_tg.z, 0f, _tg.x).normalized;
                var flat = Quaternion.LookRotation(_tg, Vector3.up);

                int seg = 0;
                for (int i = 0; i < _rays.Length; i++)
                {
                    float ph = _ph[i];
                    Vector2 o = Offs[i] * 1.45f;
                    Vector3 pos = Lead + side * (o.x + Mathf.Sin(t * 0.4f + ph) * 0.8f) + _tg * o.y + Vector3.up * (Mathf.Sin(t * 0.7f + ph) * 1.2f);
                    var tr = _rays[i];
                    tr.position = pos;
                    tr.rotation = flat * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.5f + ph) * 14f);

                    Vector3 prev = tr.TransformPoint(new Vector3(0f, 0f, -0.8f));
                    for (int k = 1; k < TailPts; k++)
                    {
                        float q = k / (float)(TailPts - 1);
                        Vector3 p = tr.TransformPoint(new Vector3(Mathf.Sin(t * 2.1f + ph - q * 3f) * q * 0.25f, 0f, -0.8f - q * 1.6f));
                        Color c0 = _glow[i] * (0.7f * (1f - (q - 1f / (TailPts - 1)))), c1 = _glow[i] * (0.7f * (1f - q));
                        c0.a = 1f; c1.a = 1f;
                        _tails.Set(seg++, prev, p, c0, c1);
                        prev = p;
                    }
                    if (Alpha > 0.2f && Random.value < Mathf.Min(1f, dt * 30f))
                        v._trails.Spawn(pos - _tg * 1.6f * tr.localScale.x, _glow[i], Random.Range(0.35f, 0.7f), Random.Range(1.5f, 3f), 0.8f * Alpha);
                }
                _tails.Upload();
            }
        }

        // ==================================================================== Пустотные медузы

        private sealed class JellyVis : Vis
        {
            private const int Tent = 12, Pts = 26;
            private sealed class J
            {
                public Transform T;
                public float Rad, A, W, Y, Ph;
                public Color Rim;
                public Vector3 Pos, Prev;
                public bool Init;
            }
            private J[] _j;
            private Lines _tent;
            private Vector3 _center;

            public override void Build(FaunaView v)
            {
                int n = Mathf.Max(1, H.Count);
                _j = new J[n];
                for (int i = 0; i < n; i++)
                {
                    float hue = R(0.74f, 0.9f);
                    var rim = Color.HSVToRGB(hue, 0.6f, 1f);
                    var inn = Color.HSVToRGB(hue - 0.08f, 0.65f, 0.55f);
                    float ph = R(0f, 6f);
                    var body = new GameObject("Jelly" + i).transform;
                    body.SetParent(Root.transform, false);
                    body.localScale = Vector3.one * R(1f, 1.6f);
                    foreach (var mesh in new[] { Res.BellMesh, Res.CoreMesh })
                    {
                        var go = MeshObj(mesh.name, mesh, v._bellMat, body);
                        if (mesh == Res.CoreMesh) go.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                        var b = Track(go.GetComponent<MeshRenderer>());
                        b.SetColor(IdIn, inn);
                        b.SetColor(IdRim, rim);
                        b.SetFloat(IdPhase, ph);
                    }
                    _j[i] = new J { T = body, Rad = R(5f, 15f), A = R(0f, 6.28f), W = R(0.05f, 0.11f) * (i % 2 == 0 ? -1f : 1f), Y = R(-2f, 4f), Ph = ph, Rim = rim };
                }
                _tent = new Lines(n * Tent * (Pts - 1), v._lineMat, Root.transform, "Tentacles");
                Track(_tent.R);
            }

            protected override Vector3 LabelPoint => _center;

            public override void Tick(FaunaView v, float t, float dt, Vector3 anchor)
            {
                _center = anchor + Vector3.up * 3f;
                int seg = 0;
                foreach (var J in _j)
                {
                    float pulse = Mathf.Sin(t * 1.7f + J.Ph);
                    J.A += dt * J.W * (1f + Mathf.Max(0f, pulse) * 1.6f);   // толчок на сжатии купола
                    J.Prev = J.Init ? J.Pos : _center;
                    J.Pos = _center + new Vector3(Mathf.Cos(J.A) * J.Rad, J.Y + Mathf.Sin(t * 0.3f + J.Ph) * 1.5f, Mathf.Sin(J.A) * J.Rad * 0.8f);
                    J.T.position = J.Pos;
                    Vector3 dir = J.Pos - J.Prev;
                    if (dir.sqrMagnitude > 1e-8f)
                    {
                        var want = Quaternion.FromToRotation(Vector3.up, dir.normalized);
                        J.T.rotation = J.Init ? Quaternion.Slerp(J.T.rotation, want, Mathf.Min(1f, dt * 5f)) : want;
                    }
                    J.Init = true;

                    for (int i = 0; i < Tent; i++)
                    {
                        bool oral = i < 4;
                        float ang = i / (float)Tent * Mathf.PI * 2f + (oral ? 0.4f : 0f);
                        float rad = oral ? 0.25f : 0.92f, len = oral ? 6.5f : 4.6f;
                        Vector3 prev = Vector3.zero;
                        for (int k = 0; k < Pts; k++)
                        {
                            float q = k / (float)(Pts - 1);
                            float sw = Mathf.Sin(t * 1.4f + q * 5f + i) * q * (oral ? 0.55f : 0.35f);
                            float rr = rad * (1f - q * 0.35f) * (1f + 0.16f * pulse * (1f - q));
                            var local = new Vector3(Mathf.Cos(ang) * rr + sw, -q * len * (1f - 0.08f * pulse),
                                                    Mathf.Sin(ang) * rr + Mathf.Cos(t * 1.1f + q * 4f + i) * q * 0.3f);
                            Vector3 p = J.T.TransformPoint(local);
                            if (k > 0)
                            {
                                float k0 = (oral ? 0.95f : 0.55f) * (1f - (k - 1) / (float)(Pts - 1)), k1 = (oral ? 0.95f : 0.55f) * (1f - q);
                                Color c0 = J.Rim * k0, c1 = J.Rim * k1;
                                c0.a = 1f; c1.a = 1f;
                                _tent.Set(seg++, prev, p, c0, c1);
                            }
                            prev = p;
                        }
                    }
                    if (Alpha > 0.2f && Random.value < Mathf.Min(1f, dt * 7f))
                        v._trails.Spawn(J.Pos, J.Rim, Random.Range(0.3f, 0.6f), Random.Range(2f, 4f), 0.6f * Alpha,
                            new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-0.2f, 0.2f), Random.Range(-0.3f, 0.3f)));
                }
                _tent.Upload();
            }
        }

        // ==================================================================== Рой Искр

        private sealed class SwarmVis : Vis
        {
            private Sparks _sp;
            private float[] _r, _a, _w, _ph, _h;
            private Color[] _base;
            private Vector3 _center;
            private bool _init;
            private float _feed;
            private int _drainSys = -1;

            public override void Build(FaunaView v)
            {
                int n = SpaceFauna.SwarmCount;
                _sp = new Sparks(n, v._sparkMat, Root.transform, "Swarm", false);
                _r = new float[n]; _a = new float[n]; _w = new float[n]; _ph = new float[n]; _h = new float[n];
                _base = new Color[n];
                for (int i = 0; i < n; i++)
                {
                    float g = (R(0f, 1f) + R(0f, 1f) + R(0f, 1f)) / 3f;
                    _r[i] = Mathf.Abs(g - 0.5f) * 2f * 7f + 2.5f;
                    _a[i] = R(0f, 6.28f);
                    _w[i] = R(0.6f, 2f) * (R(0f, 1f) < 0.5f ? 1f : -1f);
                    _ph[i] = R(0f, 6.28f);
                    _h[i] = R(0f, 1f);
                    _base[i] = Color.HSVToRGB(R(0.07f, 0.14f) + (R(0f, 1f) < 0.15f ? 0.38f : 0f), 0.85f, 1f);
                    _sp.Size[i] = R(0.4f, 0.9f);
                }
            }

            protected override Vector3 LabelPoint => _center;

            public override void Tick(FaunaView v, float t, float dt, Vector3 anchor)
            {
                _center = Follow(anchor, dt, 4f);
                float travel = 0f;
                Vector3 dir = Vector3.right;
                if (H.Moving && H.From >= 0 && H.To >= 0)
                {
                    float q = Mathf.Clamp01(H.Days / Mathf.Max(1f, H.PhaseDays));
                    travel = Mathf.Sin(q * Mathf.PI);
                    var a = EmpireStats.GetSystem(H.From)?.Position ?? Vector3.zero;
                    var b = EmpireStats.GetSystem(H.To)?.Position ?? Vector3.right;
                    dir = b - a; dir.y = 0f;
                    dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.right;
                }
                _feed = Mathf.MoveTowards(_feed, H.Moving ? 0f : 1f, dt * 0.8f);

                // Звезда под роем тускнеет
                int sys = H.Moving ? -1 : H.Home;
                var gv = GalaxyView.Instance;
                if (gv != null)
                {
                    if (_drainSys >= 0 && _drainSys != sys) { var old = gv.AnimatorOf(_drainSys); if (old != null) old.Drain = 0f; }
                    _drainSys = sys;
                    var an = gv.AnimatorOf(sys);
                    if (an != null) an.Drain = _feed * Alpha;
                }

                Vector3 side = new Vector3(-dir.z, 0f, dir.x);
                int n = _sp.Cap;
                for (int i = 0; i < n; i++)
                {
                    float ang = _a[i] + t * _w[i] * (1f + _feed * 0.6f);
                    float r = _r[i] * (1f + 0.35f * Mathf.Sin(t * 0.9f + _h[i] * 9f + Mathf.Cos(t * 0.37f) * 3f)) * (1f + _feed * 0.5f) * (1f - travel * 0.55f);
                    float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r, y = Mathf.Sin(ang * 1.7f + _ph[i]) * r * 0.35f;
                    float along = (_h[i] - 0.5f) * 22f * travel;
                    float wob = Mathf.Sin(_h[i] * 40f + t * 2f) * travel * 1.5f;
                    var target = _center + new Vector3(x, y, z) + dir * along + side * wob;
                    if (!_init) _sp.P[i] = target;
                    else _sp.P[i] = Vector3.Lerp(_sp.P[i], target, Mathf.Min(1f, dt * (2f + _h[i] * 2.5f)));
                    float tw = 0.6f + 0.4f * Mathf.Sin(t * 6f + _h[i] * 50f) + _feed * 0.5f;
                    var c = _base[i] * tw;
                    c.a = Alpha;
                    _sp.Col[i] = c;
                }
                _init = true;
                _sp.Upload(1f);
            }

            public override void Destroy()
            {
                var an = GalaxyView.Instance != null ? GalaxyView.Instance.AnimatorOf(_drainSys) : null;
                if (an != null) an.Drain = 0f;
                base.Destroy();
            }
        }

        // ==================================================================== Кристаллиды

        private sealed class CrystalVis : Vis
        {
            private sealed class C
            {
                public Transform T, Halo;
                public MaterialPropertyBlock B, HB;
                public float Rad, A, W, Incl, Flash, Cd;
                public Vector3 Spin;
            }
            private sealed class Ring { public Transform T; public MaterialPropertyBlock B; public float Life; }
            private C[] _c;
            private Ring[] _rings;
            private int _ri;
            private Lines _lattice;
            private Transform _beam;
            private MaterialPropertyBlock _beamB;
            private Vector3 _star;
            private bool _pulsar;
            private float _pulsarPhase;

            public override void Build(FaunaView v)
            {
                var sys = EmpireStats.GetSystem(H.Home);
                _pulsar = sys != null && sys.SpectralClass == StarSpectralClass.Pulsar;
                _pulsarPhase = (H.Home * 0.6180339f) % 1f * 10f;   // как у лучей пульсара в StarExtras
                int n = Mathf.Max(0, H.Count);
                _c = new C[n];
                for (int i = 0; i < n; i++)
                {
                    var go = MeshObj("Crystal" + i, Res.CrystalMesh(Rng), v._crystalMat, Root.transform);
                    go.transform.localScale = Vector3.one * R(0.8f, 1.15f);
                    var b = Track(go.GetComponent<MeshRenderer>());
                    var halo = MeshObj("Halo", Res.Quad, v._glowMat, Root.transform);
                    halo.transform.localScale = Vector3.one * 3f * go.transform.localScale.x;
                    var hb = Track(halo.GetComponent<MeshRenderer>());
                    _c[i] = new C
                    {
                        T = go.transform, Halo = halo.transform, B = b, HB = hb,
                        Rad = 8f + i * 2.6f + R(-0.8f, 0.8f), A = i * 1.3f + R(0f, 1f), W = R(0.07f, 0.14f), Incl = R(-0.25f, 0.25f),
                        Spin = new Vector3(R(-0.4f, 0.4f), R(-0.5f, 0.5f), R(-0.3f, 0.3f)) * Mathf.Rad2Deg
                    };
                }
                _rings = new Ring[10];
                for (int i = 0; i < _rings.Length; i++)
                {
                    var go = MeshObj("Ring", Res.Quad, v._ringMat, Root.transform);
                    _rings[i] = new Ring { T = go.transform, B = Track(go.GetComponent<MeshRenderer>()) };
                    go.SetActive(false);
                }
                _lattice = new Lines(Mathf.Max(1, n), v._lineMat, Root.transform, "Lattice");
                Track(_lattice.R);
                var beam = MeshObj("Sweep", Res.Beam, v._beamMat, Root.transform);
                _beam = beam.transform;
                _beamB = Track(beam.GetComponent<MeshRenderer>());
                float maxR = 8f + n * 2.6f + 6f;
                _beam.localScale = new Vector3(maxR, 1f, 1.1f);
            }

            protected override Vector3 LabelPoint => _star;

            public override void Tick(FaunaView v, float t, float dt, Vector3 anchor)
            {
                _star = anchor;
                // Азимут луча: у пульсара — его магнитная ось (StarExtras: поворот раз в 5 с), у остальных — своя развёртка
                float psi;
                if (_pulsar)
                {
                    float th = (Time.unscaledTime + _pulsarPhase) / 5f * Mathf.PI * 2f;
                    psi = Mathf.PI - th;
                }
                else psi = -Time.unscaledTime * 0.9f;
                _beam.position = _star + Vector3.up;
                _beam.rotation = Quaternion.Euler(0f, -psi * Mathf.Rad2Deg, 0f);
                _beamB.SetColor(IdColor, new Color(0.65f, 0.85f, 1f, 0.35f));

                var cam = v._cam.transform;
                for (int i = 0; i < _c.Length; i++)
                {
                    var c = _c[i];
                    c.A += dt * c.W;
                    var p = new Vector3(_star.x + Mathf.Cos(c.A) * c.Rad, _star.y + Mathf.Sin(c.A) * c.Rad * c.Incl + 1f, _star.z - Mathf.Sin(c.A) * c.Rad);
                    c.T.position = p;
                    c.T.Rotate(c.Spin * dt, Space.Self);
                    float phi = Mathf.Atan2(p.z - _star.z, p.x - _star.x);
                    float d = Mathf.Repeat(phi - psi, Mathf.PI);
                    d = Mathf.Min(d, Mathf.PI - d);
                    c.Cd -= dt;
                    if (d < 0.07f && c.Cd <= 0f)
                    {
                        c.Flash = 1f;
                        c.Cd = 0.6f;
                        var r = _rings[_ri];
                        _ri = (_ri + 1) % _rings.Length;
                        r.Life = 1.3f;
                        r.T.position = p;
                        r.T.gameObject.SetActive(true);
                    }
                    c.Flash = Mathf.Max(0f, c.Flash - dt * 1.6f);
                    c.B.SetFloat(IdFlash, c.Flash * c.Flash);
                    c.Halo.position = p;
                    c.Halo.rotation = cam.rotation;
                    c.HB.SetColor(IdColor, new Color(0.56f, 0.78f, 1f, 0.18f + c.Flash * 0.7f));
                }
                for (int i = 0; i < _c.Length; i++)
                {
                    var a = _c[i]; var b = _c[(i + 1) % _c.Length];
                    float k = 0.06f + Mathf.Max(a.Flash, b.Flash) * 0.9f;
                    var col = new Color(0.55f * k, 0.8f * k, k, 1f);
                    _lattice.Set(i, a.T.position, b.T.position, col, col);
                }
                if (_c.Length > 0) _lattice.Upload();
                foreach (var r in _rings)
                {
                    if (r.Life <= 0f) continue;
                    r.Life -= dt;
                    if (r.Life <= 0f) { r.T.gameObject.SetActive(false); continue; }
                    float q = 1f - r.Life / 1.3f;
                    r.T.localScale = Vector3.one * (1f + q * 7f);
                    r.T.rotation = cam.rotation;
                    r.B.SetColor(IdColor, new Color(0.75f, 0.9f, 1f, (1f - q) * 0.45f));
                }
            }
        }

        // ==================================================================== Странник

        private sealed class WandererVis : Vis
        {
            private const int S = 50;
            private const float SP = 1.75f;
            private Transform[] _seg;
            private Transform _fin, _fluke;
            private readonly Vector3[] _p = new Vector3[S];
            private static readonly Color Glitter = new Color(0.56f, 0.78f, 1f);

            public override void Build(FaunaView v)
            {
                _seg = new Transform[S];
                for (int i = 0; i < S; i++)
                {
                    var go = MeshObj("Seg" + i, Res.Sphere, v._wandMat, Root.transform);
                    var b = Track(go.GetComponent<MeshRenderer>());
                    b.SetFloat(IdK, i / (float)(S - 1));
                    _seg[i] = go.transform;
                }
                _fin = Fin(v, "Fins", 8.5f, 0.9f);
                _fluke = Fin(v, "Fluke", 4.2f, 1.3f);
            }

            private Transform Fin(FaunaView v, string name, float scale, float flap)
            {
                var go = MeshObj(name, Res.RayMesh, v._rayMat, Root.transform);
                go.transform.localScale = Vector3.one * scale;
                var b = Track(go.GetComponent<MeshRenderer>());
                b.SetColor(IdGlow, new Color(0.5f, 0.85f, 1f));
                b.SetColor(IdBody, new Color(0.04f, 0.08f, 0.19f));
                b.SetFloat(IdFlap, flap);
                b.SetFloat(IdPhase, R(0f, 6f));
                return go.transform;
            }

            private static float Rad(float k) =>
                0.6f + 3f * Mathf.Pow(Mathf.Min(1f, (k + 0.04f) / 0.16f), 0.55f) * Mathf.Pow(1f - k, 0.9f);

            protected override Vector3 LabelPoint => _p[0];

            public override void Tick(FaunaView v, float t, float dt, Vector3 anchor)
            {
                int n = H.Path.Count;
                float u = Mathf.Clamp01(H.Progress + (TimeManager.Instance != null ? TimeManager.Instance.DayFraction : 0f) / SpaceFauna.WandererDays);
                for (int i = 0; i < S; i++)
                {
                    if (i > 0) u = Mathf.Max(0f, u - SP / (SpaceFauna.WandererSegLength(H, u) * Mathf.Max(1, n - 1)));
                    float k = i / (float)(S - 1);
                    Vector3 p = SpaceFauna.WandererPoint(H, u);
                    Vector3 tg = SpaceFauna.WandererPoint(H, Mathf.Min(1f, u + 0.0005f)) - SpaceFauna.WandererPoint(H, Mathf.Max(0f, u - 0.0005f));
                    tg.y = 0f;
                    tg = tg.sqrMagnitude > 1e-8f ? tg.normalized : Vector3.forward;
                    Vector3 side = new Vector3(-tg.z, 0f, tg.x);
                    p += side * (Mathf.Sin(t * 0.8f - i * 0.22f) * (0.3f + k * 2.8f)) + Vector3.up * (Mathf.Sin(t * 0.6f - i * 0.15f) * (0.4f + k * 1.2f));
                    _p[i] = p;
                }
                for (int i = 0; i < S; i++)
                {
                    float k = i / (float)(S - 1), r = Rad(k);
                    Vector3 fwd = i == 0 ? _p[0] - _p[1] : _p[i - 1] - _p[i];
                    if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                    var tr = _seg[i];
                    tr.position = _p[i];
                    tr.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                    tr.localScale = new Vector3(r, r * 0.78f, r * 1.25f + SP * 0.4f);
                }
                const int fi = 9;
                _fin.position = _p[fi] - Vector3.up * 0.6f;
                _fin.rotation = Quaternion.LookRotation(Dir(_p[fi - 1] - _p[fi]), Vector3.up);
                _fluke.position = _p[S - 2];
                _fluke.rotation = Quaternion.LookRotation(Dir(_p[S - 3] - _p[S - 2]), Vector3.up);
                if (Alpha > 0.2f)
                    for (int m = 0; m < 4; m++)
                    {
                        if (Random.value > Mathf.Min(1f, dt * 60f)) continue;
                        Vector3 at = _p[Random.Range(0, S)] + new Vector3(Random.Range(-2f, 2f), Random.Range(-1f, 2f), Random.Range(-2f, 2f));
                        v._trails.Spawn(at, Glitter, Random.Range(0.3f, 0.8f), Random.Range(4f, 8f), 0.7f * Alpha);
                    }
            }

            private static Vector3 Dir(Vector3 d) => d.sqrMagnitude > 1e-6f ? d : Vector3.forward;
        }
    }
}
