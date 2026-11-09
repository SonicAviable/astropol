using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;
using StellarisClone.Cam;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Космическая фауна на карте галактики (данные и правила — SpaceFauna). Каждой стае — свой визуал:
    ///   скаты — сетка-крыло с вершинным взмахом (FaunaRay), хвосты-линии, строй «клином», отдыхают кругом у звезды;
    ///   медузы — пульсирующие купола по Френелю (FaunaBell) и щупальца-линии, плывут толчками вокруг дома;
    ///   Рой Искр — тысяча искр (FaunaSpark), дышит, при перелёте тянется потоком, звезда под ним тускнеет;
    ///   кристаллиды — гранёные переливчатые тела (FaunaCrystal), вспыхивают, когда их проходит луч пульсара;
    ///   Странник — 50 сегментов (FaunaWanderer) с плавниками-скатами, по спине бежит волна света.
    /// Над стаей — плашка с названием; в неизведанных системах фауны не видно; в режиме системы фауна скрыта.
    /// </summary>
    public partial class FaunaView : MonoBehaviour
    {
        private static readonly int IdAlpha = Shader.PropertyToID("_Alpha");
        private static readonly int IdPhase = Shader.PropertyToID("_Phase");
        private static readonly int IdFlap = Shader.PropertyToID("_Flap");
        private static readonly int IdGlow = Shader.PropertyToID("_Glow");
        private static readonly int IdBody = Shader.PropertyToID("_Body");
        private static readonly int IdIn = Shader.PropertyToID("_In");
        private static readonly int IdRim = Shader.PropertyToID("_Rim");
        private static readonly int IdFlash = Shader.PropertyToID("_Flash");
        private static readonly int IdK = Shader.PropertyToID("_K");
        private static readonly int IdColor = Shader.PropertyToID("_Color");

        private GameObject _root;
        private Camera _cam;
        private StrategyCameraController _camCtl;
        private Material _rayMat, _bellMat, _crystalMat, _wandMat, _lineMat, _sparkMat, _glowMat, _ringMat, _beamMat;
        private bool _ready;
        private int _version = -1;
        private readonly List<Vis> _vis = new List<Vis>();
        private Sparks _trails;
        private float _shown = 1f;

        private void Start()
        {
            _root = new GameObject("SpaceFauna");
            _rayMat = Mat("FaunaRay");
            _bellMat = Mat("FaunaBell");
            _crystalMat = Mat("FaunaCrystal");
            _wandMat = Mat("FaunaWanderer");
            _lineMat = Mat("FaunaGlow");
            _sparkMat = Mat("FaunaSpark");
            _glowMat = Mat("FaunaGlow");
            _ringMat = Mat("FaunaGlow");
            _beamMat = Mat("FaunaGlow");
            _ready = _rayMat != null && _bellMat != null && _crystalMat != null && _wandMat != null && _lineMat != null && _sparkMat != null;
            if (!_ready) { Debug.LogWarning("[FaunaView] Шейдеры фауны не найдены (Resources/Shaders/Fauna*) — фауна не рисуется."); return; }
            _glowMat.mainTexture = Res.GlowTex;
            _ringMat.mainTexture = Res.RingTex;
            _beamMat.mainTexture = Res.BeamTex;
            _trails = new Sparks(2600, _sparkMat, _root.transform, "FaunaTrails", true);
        }

        private static Material Mat(string shader)
        {
            var sh = Resources.Load<Shader>("Shaders/" + shader);
            return sh != null ? new Material(sh) { name = shader } : null;
        }

        private void OnDestroy()
        {
            foreach (var v in _vis) v.Destroy();
            _vis.Clear();
            if (_root != null) Destroy(_root);
        }

        private void Update()
        {
            if (!_ready) return;
            if (_cam == null) _cam = Camera.main;
            if (_camCtl == null) _camCtl = FindAnyObjectByType<StrategyCameraController>();
            var sf = SpaceFauna.Instance;
            if (_cam == null || sf == null) return;

            if (sf.Version != _version) Rebuild(sf);

            bool hide = !UIManager.IsGameStarted || MenuAtmosphere.IsActive || (_camCtl != null && _camCtl.IsSystemMode);
            float dt = Mathf.Min(0.05f, Time.deltaTime);
            float t = Time.time;
            _shown = Mathf.MoveTowards(_shown, hide ? 0f : 1f, Time.unscaledDeltaTime * 3f);
            float frac = TimeManager.Instance != null ? TimeManager.Instance.DayFraction : 0f;

            foreach (var v in _vis)
            {
                if (v.H.Gone) continue;
                v.UpdateKnown(dt);
                float target = v.Known * _shown;
                v.Alpha = Mathf.MoveTowards(v.Alpha, target, dt * 1.5f);
                bool active = v.Alpha > 0.003f || target > 0f;
                if (v.Root.activeSelf != active) v.Root.SetActive(active);
                if (!active) { v.ShowLabel(this, 0f); continue; }
                Vector3 anchor = SpaceFauna.Anchor(v.H, v.H.Kind == (int)FaunaKind.Wanderer || v.H.Moving ? frac : 0f);
                v.Tick(this, t, dt, anchor);
                v.ApplyAlpha();
                v.ShowLabel(this, v.Alpha);
            }
            _trails.Simulate(dt);
            _trails.Upload(_shown);
        }

        private void Rebuild(SpaceFauna sf)
        {
            _version = sf.Version;
            foreach (var v in _vis) v.Destroy();
            _vis.Clear();
            foreach (var h in sf.Herds)
            {
                if (h.Gone) continue;
                Vis v = (FaunaKind)h.Kind switch
                {
                    FaunaKind.Rays => new RayVis(),
                    FaunaKind.Jelly => new JellyVis(),
                    FaunaKind.Swarm => new SwarmVis(),
                    FaunaKind.Crystal => new CrystalVis(),
                    FaunaKind.Wanderer => new WandererVis(),
                    _ => null
                };
                if (v == null) continue;
                v.H = h;
                v.Root = new GameObject("Fauna_" + (FaunaKind)h.Kind);
                v.Root.transform.SetParent(_root.transform, false);
                v.Rng = new System.Random(h.Seed);
                v.Build(this);
                v.CreateLabel(_root.transform);
                _vis.Add(v);
            }
        }

        // ==================================================================== общая часть визуала

        private abstract class Vis
        {
            public FaunaHerd H;
            public GameObject Root;
            public System.Random Rng;
            public float Alpha, Known;
            private float _knownTimer;
            private Transform _labelRoot;
            private WorldChip _chip;
            private readonly List<(Renderer r, MaterialPropertyBlock b)> _blocks = new List<(Renderer, MaterialPropertyBlock)>();
            protected Vector3 Lead;
            protected bool HasLead;

            public abstract void Build(FaunaView v);
            public abstract void Tick(FaunaView v, float t, float dt, Vector3 anchor);
            /// <summary>Точка над которой висит плашка.</summary>
            protected abstract Vector3 LabelPoint { get; }
            /// <summary>Система, по которой решается, видит ли игрок стаю.</summary>
            protected virtual int VisionSystem => SpaceFauna.StaySystem(H) >= 0 ? SpaceFauna.StaySystem(H) : SpaceFauna.NearestSystem(LabelPoint);

            public float R(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

            public void UpdateKnown(float dt)
            {
                _knownTimer -= dt;
                if (_knownTimer > 0f) return;
                _knownTimer = 0.5f;
                if (H.Kind == (int)FaunaKind.Wanderer) { Known = 1f; return; }
                int sys = VisionSystem;
                Known = !Vision.Active || (sys >= 0 && (Vision.PlayerKnows(sys) || EmpireStats.GetSystem(sys)?.OwnerId == 0)) ? 1f : 0f;
            }

            protected MaterialPropertyBlock Track(Renderer r)
            {
                var b = new MaterialPropertyBlock();
                _blocks.Add((r, b));
                return b;
            }

            public void ApplyAlpha()
            {
                foreach (var (r, b) in _blocks)
                {
                    if (r == null) continue;
                    b.SetFloat(IdAlpha, Alpha);
                    r.SetPropertyBlock(b);
                }
            }

            public void CreateLabel(Transform parent)
            {
                _labelRoot = new GameObject("FaunaLabel").transform;
                _labelRoot.SetParent(parent, false);
                _chip = WorldChip.Create(_labelRoot, "FaunaChip", 50, true, true);
                _chip.GlowStrength = 0.35f;
                _chip.SetIcon(SpaceFauna.Icon(H.Kind));
                _chip.Set(SpaceFauna.Name(H.Kind) + "  ·  " + SpaceFauna.CountText(H), 0.36f);
                _chip.CenterOn(0f, 0f);
                Color c = SpaceFauna.Color(H.Kind);
                _chip.SetColors(new Color(0.02f, 0.05f, 0.08f, 0.82f), new Color(c.r, c.g, c.b, 0.6f), c, c, Color.white);
                _chip.SetAlpha(0f);
            }

            public void ShowLabel(FaunaView v, float a)
            {
                if (_labelRoot == null) return;
                bool on = a > 0.01f;
                if (_labelRoot.gameObject.activeSelf != on) _labelRoot.gameObject.SetActive(on);
                if (!on) return;
                var cam = v._cam.transform;
                Vector3 p = LabelPoint;
                float dist = Vector3.Distance(cam.position, p);
                float scale = Mathf.Clamp(dist * 0.012f, 0.9f, 3.2f);
                _labelRoot.rotation = cam.rotation;
                _labelRoot.localScale = Vector3.one * scale;
                _labelRoot.position = p + cam.up * (2.2f + scale * 1.1f);
                // Вплотную плашка не нужна — существо видно и так; издалека — главный ориентир
                float near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(14f, 30f, dist));
                _chip.SetAlpha(a * near);
            }

            public virtual void Destroy()
            {
                if (Root != null) Object.Destroy(Root);
                if (_labelRoot != null) Object.Destroy(_labelRoot.gameObject);
            }

            /// <summary>Плавное следование за точкой (без рывков при смене фазы стаи).</summary>
            protected Vector3 Follow(Vector3 target, float dt, float rate)
            {
                if (!HasLead) { Lead = target; HasLead = true; }
                else Lead = Vector3.Lerp(Lead, target, 1f - Mathf.Exp(-dt * rate));
                return Lead;
            }

            protected GameObject MeshObj(string name, Mesh mesh, Material mat, Transform parent)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                return go;
            }
        }

        // ==================================================================== искры (рой, шлейфы)

        /// <summary>Набор искр одним мешем: квад на искру, разворот к камере — в шейдере FaunaSpark.</summary>
        private sealed class Sparks
        {
            public readonly int Cap;
            public readonly Vector3[] P, Vel;
            public readonly float[] Life, Max, Size, A0;
            public readonly Color[] Col;
            private readonly bool _fade;
            private readonly Mesh _mesh;
            private readonly Vector3[] _v;
            private readonly Vector4[] _uv;
            private readonly Color[] _c;
            private readonly MeshRenderer _r;
            private int _next;

            public Sparks(int cap, Material mat, Transform parent, string name, bool fade)
            {
                Cap = cap;
                _fade = fade;
                P = new Vector3[cap]; Vel = new Vector3[cap];
                Life = new float[cap]; Max = new float[cap]; Size = new float[cap]; A0 = new float[cap];
                Col = new Color[cap];
                _v = new Vector3[cap * 4]; _uv = new Vector4[cap * 4]; _c = new Color[cap * 4];
                var tri = new int[cap * 6];
                for (int i = 0; i < cap; i++)
                {
                    int v = i * 4, k = i * 6;
                    tri[k] = v; tri[k + 1] = v + 1; tri[k + 2] = v + 2; tri[k + 3] = v; tri[k + 4] = v + 2; tri[k + 5] = v + 3;
                }
                _mesh = new Mesh { name = name };
                _mesh.MarkDynamic();
                if (cap * 4 > 65000) _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                _mesh.vertices = _v;
                _mesh.SetUVs(0, _uv);
                _mesh.colors = _c;
                _mesh.triangles = tri;
                _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 6000f);
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _r = go.AddComponent<MeshRenderer>();
                _r.sharedMaterial = mat;
                _r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _r.receiveShadows = false;
            }

            public GameObject Go => _r.gameObject;

            public void Spawn(Vector3 p, Color c, float size, float life, float a, Vector3 vel = default)
            {
                int i = _next;
                _next = (_next + 1) % Cap;
                P[i] = p; Vel[i] = vel; Col[i] = c; Size[i] = size; Life[i] = Max[i] = life; A0[i] = a;
            }

            public void Simulate(float dt)
            {
                for (int i = 0; i < Cap; i++)
                {
                    if (Life[i] <= 0f) continue;
                    Life[i] -= dt;
                    P[i] += Vel[i] * dt;
                }
            }

            public void Upload(float alpha)
            {
                for (int i = 0; i < Cap; i++)
                {
                    float a;
                    if (_fade)
                    {
                        float k = Life[i] > 0f ? Life[i] / Mathf.Max(0.01f, Max[i]) : 0f;
                        a = A0[i] * k * k * alpha;
                    }
                    else a = Col[i].a * alpha;
                    var c = Col[i];
                    c.a = a;
                    int v = i * 4;
                    Vector3 p = P[i];
                    float s = a > 0f ? Size[i] : 0f;
                    _v[v] = p; _v[v + 1] = p; _v[v + 2] = p; _v[v + 3] = p;
                    _uv[v] = new Vector4(-1f, -1f, s, 0f);
                    _uv[v + 1] = new Vector4(1f, -1f, s, 0f);
                    _uv[v + 2] = new Vector4(1f, 1f, s, 0f);
                    _uv[v + 3] = new Vector4(-1f, 1f, s, 0f);
                    _c[v] = c; _c[v + 1] = c; _c[v + 2] = c; _c[v + 3] = c;
                }
                _mesh.vertices = _v;
                _mesh.SetUVs(0, _uv);
                _mesh.colors = _c;
            }
        }

        // ==================================================================== линии (щупальца, хвосты, нити)

        private sealed class Lines
        {
            private readonly Mesh _mesh;
            private readonly Vector3[] _v;
            private readonly Color[] _c;
            public readonly MeshRenderer R;
            public readonly int Segments;

            public Lines(int segments, Material mat, Transform parent, string name)
            {
                Segments = segments;
                _v = new Vector3[segments * 2];
                _c = new Color[segments * 2];
                var idx = new int[segments * 2];
                for (int i = 0; i < idx.Length; i++) idx[i] = i;
                _mesh = new Mesh { name = name };
                _mesh.MarkDynamic();
                if (_v.Length > 65000) _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                _mesh.vertices = _v;
                _mesh.colors = _c;
                _mesh.SetIndices(idx, MeshTopology.Lines, 0);
                _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 6000f);
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                R = go.AddComponent<MeshRenderer>();
                R.sharedMaterial = mat;
                R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                R.receiveShadows = false;
            }

            public void Set(int seg, Vector3 a, Vector3 b, Color ca, Color cb)
            {
                int i = seg * 2;
                _v[i] = a; _v[i + 1] = b; _c[i] = ca; _c[i + 1] = cb;
            }

            public void Upload()
            {
                _mesh.vertices = _v;
                _mesh.colors = _c;
            }
        }

        // ==================================================================== меши и текстуры

        private static class Res
        {
            private static Mesh s_ray, s_bell, s_core, s_sphere, s_quad, s_beam;
            private static Texture2D s_glow, s_ring, s_beamTex;

            /// <summary>Сетка 28×16 для ската: форму задаёт шейдер по uv.</summary>
            public static Mesh RayMesh
            {
                get
                {
                    if (s_ray != null) return s_ray;
                    const int nx = 28, ny = 16;
                    var v = new Vector3[(nx + 1) * (ny + 1)];
                    var uv = new Vector2[v.Length];
                    for (int y = 0; y <= ny; y++)
                        for (int x = 0; x <= nx; x++)
                        {
                            int i = y * (nx + 1) + x;
                            v[i] = new Vector3(x / (float)nx - 0.5f, 0f, y / (float)ny - 0.5f);
                            uv[i] = new Vector2(x / (float)nx, y / (float)ny);
                        }
                    var tri = new List<int>();
                    for (int y = 0; y < ny; y++)
                        for (int x = 0; x < nx; x++)
                        {
                            int a = y * (nx + 1) + x, b = a + 1, c = a + nx + 1, d = c + 1;
                            tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(b); tri.Add(c); tri.Add(d);
                        }
                    s_ray = new Mesh { name = "FaunaRay", vertices = v, uv = uv, triangles = tri.ToArray() };
                    s_ray.bounds = new Bounds(Vector3.zero, new Vector3(4f, 2f, 3f));
                    return s_ray;
                }
            }

            /// <summary>Сферический сегмент радиуса 1: от верхушки (y = 1) до угла thetaMax.</summary>
            private static Mesh SphereSeg(string name, float thetaMax, int rings, int segs, float radius)
            {
                var v = new List<Vector3>();
                var n = new List<Vector3>();
                for (int r = 0; r <= rings; r++)
                {
                    float th = thetaMax * r / rings;
                    for (int s = 0; s <= segs; s++)
                    {
                        float ph = Mathf.PI * 2f * s / segs;
                        var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                        v.Add(d * radius);
                        n.Add(d);
                    }
                }
                var tri = new List<int>();
                for (int r = 0; r < rings; r++)
                    for (int s = 0; s < segs; s++)
                    {
                        int a = r * (segs + 1) + s, b = a + 1, c = a + segs + 1, d = c + 1;
                        tri.Add(a); tri.Add(b); tri.Add(c); tri.Add(b); tri.Add(d); tri.Add(c);
                    }
                var m = new Mesh { name = name };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetTriangles(tri, 0);
                m.RecalculateBounds();
                return m;
            }

            public static Mesh BellMesh => s_bell != null ? s_bell : (s_bell = SphereSeg("FaunaBell", Mathf.PI * 0.5f, 18, 40, 1f));
            public static Mesh CoreMesh => s_core != null ? s_core : (s_core = SphereSeg("FaunaBellCore", Mathf.PI * 0.5f, 8, 20, 0.42f));
            public static Mesh Sphere => s_sphere != null ? s_sphere : (s_sphere = SphereSeg("FaunaSphere", Mathf.PI, 16, 28, 1f));

            /// <summary>Квад 2×2 в плоскости XY (разворачивается к камере скриптом).</summary>
            public static Mesh Quad
            {
                get
                {
                    if (s_quad != null) return s_quad;
                    s_quad = new Mesh { name = "FaunaQuad" };
                    s_quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
                    s_quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                    s_quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                    s_quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                    s_quad.RecalculateBounds();
                    return s_quad;
                }
            }

            /// <summary>Луч-развёртка: полоса вдоль X (−1..1) в плоскости XZ, ширина ±1 по Z.</summary>
            public static Mesh Beam
            {
                get
                {
                    if (s_beam != null) return s_beam;
                    s_beam = new Mesh { name = "FaunaBeam" };
                    s_beam.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) };
                    s_beam.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                    s_beam.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                    s_beam.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                    s_beam.RecalculateBounds();
                    return s_beam;
                }
            }

            private static Texture2D Tex(string name, int w, int h, System.Func<float, float, float> alpha)
            {
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float a = Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h));
                        px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                t.SetPixels32(px);
                t.Apply(false, true);
                return t;
            }

            public static Texture2D GlowTex => s_glow != null ? s_glow : (s_glow = Tex("FaunaGlow", 64, 64, (u, v) =>
            {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                return Mathf.Exp(-d * d * 5f) * (1f - Mathf.SmoothStep(0.8f, 1f, d));
            }));

            public static Texture2D RingTex => s_ring != null ? s_ring : (s_ring = Tex("FaunaRing", 128, 128, (u, v) =>
            {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                float x = (d - 0.94f) / 0.05f;
                return Mathf.Exp(-x * x);
            }));

            public static Texture2D BeamTex => s_beamTex != null ? s_beamTex : (s_beamTex = Tex("FaunaBeam", 128, 32, (u, v) =>
            {
                float a = Mathf.Abs(u - 0.5f) * 2f, w = Mathf.Abs(v - 0.5f) * 2f;
                return Mathf.Exp(-w * w * 7f) * Mathf.Pow(1f - a, 1.6f) * Mathf.SmoothStep(0f, 0.06f, a);
            }));

            /// <summary>Тело кристаллида: икосаэдр и вытянутые октаэдры-шипы, плоские нормали (грани).</summary>
            public static Mesh CrystalMesh(System.Random rng)
            {
                float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
                var v = new List<Vector3>();
                var n = new List<Vector3>();
                var tri = new List<int>();
                void Face(Vector3 a, Vector3 b, Vector3 c)
                {
                    var nn = Vector3.Cross(b - a, c - a).normalized;
                    int i = v.Count;
                    v.Add(a); v.Add(b); v.Add(c);
                    n.Add(nn); n.Add(nn); n.Add(nn);
                    tri.Add(i); tri.Add(i + 1); tri.Add(i + 2);
                }

                float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
                var ico = new[]
                {
                    new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                    new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                    new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
                };
                for (int i = 0; i < ico.Length; i++) ico[i] = ico[i].normalized * 0.9f;
                int[] f =
                {
                    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
                };
                for (int i = 0; i < f.Length; i += 3) Face(ico[f[i]], ico[f[i + 1]], ico[f[i + 2]]);

                var oct = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                int[] of = { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 4, 3, 0, 1, 3, 4, 5, 3, 1, 0, 3, 5 };
                int spikes = 7 + rng.Next(4);
                for (int s = 0; s < spikes; s++)
                {
                    var d = new Vector3(R(-1, 1), R(-1, 1), R(-1, 1));
                    if (d.sqrMagnitude < 0.01f) d = Vector3.up;
                    d.Normalize();
                    float len = R(2.5f, 5f);
                    var rot = Quaternion.FromToRotation(Vector3.up, d);
                    Vector3 off = d * (0.6f + len * 0.3f);
                    Vector3 P(int k) => off + rot * Vector3.Scale(oct[k] * 0.32f, new Vector3(1f, len, 1f));
                    for (int i = 0; i < of.Length; i += 3) Face(P(of[i]), P(of[i + 1]), P(of[i + 2]));
                }
                var m = new Mesh { name = "FaunaCrystal" };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetTriangles(tri, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
