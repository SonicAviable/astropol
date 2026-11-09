using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Эффекты необычных звёзд поверх светила: лучи-маяк пульсара, магнитные петли и вспышки магнетара,
    /// сбрасываемые оболочки Вольфа-Райе, пылевое облако с диском и джетами протозвезды, кольцо планетарной
    /// туманности, спутник двойной звезды, ореолы гигантов и карликов.
    /// Слои — плоские квадраты с шейдером Astropolity/Star/Fx (сложение света), повёрнутые к камере.
    /// Работает и на дальней карте (по спрайтам звезды, StarAnimator), и у объёмной модели (вид системы, мини-системы).
    /// </summary>
    public sealed class StarExtras : MonoBehaviour
    {
        public const string NodeName = "Extras";

        private sealed class Part
        {
            public Transform T;
            public Renderer R;
            public Color Col;
            public float A;
        }

        private StarSpectralClass _cls;
        private float _r, _phase;
        private bool _map;
        private StarAnimator _anim;
        private StarSystem _sys;
        private Transform _bill, _spin, _companion, _disk;
        private readonly List<Part> _parts = new List<Part>();
        private readonly List<Part> _shells = new List<Part>();
        private readonly List<Part> _loops = new List<Part>();
        private Part _glow, _cross, _beamA, _beamB, _burst, _jetA, _jetB, _cloud, _neb, _compGlow;
        private float _fade = 1f;

        /// <summary>Эффекты у объёмной звезды (вид системы, мини-система). Обычным звёздам ничего не добавляется.</summary>
        public static StarExtras Attach(Transform starRoot, StarSpectralClass cls, float radius, int seed, bool map)
        {
            if (!HasExtras(cls) || !Ready()) return null;
            var go = new GameObject(NodeName);
            go.transform.SetParent(starRoot, false);
            var x = go.AddComponent<StarExtras>();
            x.Init(cls, radius, seed, map);
            return x;
        }

        /// <summary>Эффекты на дальней карте: следуют размеру и видимости спрайтов звезды, уступают мини-системе вблизи.</summary>
        public static StarExtras AttachMap(Transform node, StarSystem sys, float radius, StarAnimator anim)
        {
            if (sys == null) return null;
            var x = Attach(node, sys.SpectralClass, radius, sys.Id, true);
            if (x != null) { x._anim = anim; x._sys = sys; }
            return x;
        }

        public static bool HasExtras(StarSpectralClass c) => StarTypes.IsExotic(c);

        // ==================== ПОСТРОЕНИЕ ====================

        private void Init(StarSpectralClass cls, float radius, int seed, bool map)
        {
            _cls = cls;
            _r = radius;
            _map = map;
            _phase = (seed * 0.6180339f) % 1f * 10f;

            _bill = new GameObject("Billboard").transform;
            _bill.SetParent(transform, false);
            var tint = StarTypes.MapColor(cls);
            float R = radius;

            switch (cls)
            {
                case StarSpectralClass.ClassO:
                    _glow = Quad(_bill, Tex.Glow, R * 9f, new Color(0.55f, 0.45f, 1f), 0.35f);
                    _cross = Quad(_bill, Tex.Cross, R * 9f, new Color(0.85f, 0.85f, 1f), 0.55f);
                    break;

                case StarSpectralClass.RedGiant:
                    _glow = Quad(_bill, Tex.Glow, R * 5f, new Color(1f, 0.28f, 0.08f), 0.32f);
                    break;

                case StarSpectralClass.WhiteDwarf:
                    _glow = Quad(_bill, Tex.Glow, R * 11f, new Color(0.75f, 0.88f, 1f), 0.5f);
                    _cross = Quad(_bill, Tex.Cross, R * 18f, new Color(0.92f, 0.97f, 1f), 0.9f);
                    break;

                case StarSpectralClass.BrownDwarf:
                    _glow = Quad(_bill, Tex.Glow, R * 4.5f, new Color(0.75f, 0.2f, 0.42f), 0.28f);
                    break;

                case StarSpectralClass.Binary:
                    // Спутник — красный карлик на орбите вокруг главной звезды
                    _companion = new GameObject("Companion").transform;
                    _companion.SetParent(transform, false);
                    if (!map)
                    {
                        StarTypes.Palette(StarSpectralClass.ClassM, out var cool, out var hot);
                        AstroStarModel.BuildBody(_companion, cool, hot, R * 0.55f, seed * 0.37f + 5f, 2.4f, seed + 100000);
                    }
                    _compGlow = Quad(_companion, Tex.Glow, R * (map ? 3.2f : 2.6f), new Color(1f, 0.42f, 0.25f), map ? 0.9f : 0.35f);
                    if (map) Quad(_companion, Tex.Glow, R * 0.9f, new Color(1f, 0.85f, 0.75f), 1f);
                    break;

                case StarSpectralClass.WolfRayet:
                    _glow = Quad(_bill, Tex.Glow, R * 6f, new Color(0.4f, 0.95f, 1f), 0.3f);
                    for (int i = 0; i < 4; i++) _shells.Add(Quad(_bill, Tex.Shell, R * 2f, tint, 0.6f));
                    break;

                case StarSpectralClass.Pulsar:
                {
                    // Магнитные линии — два вытянутых кольца по бокам
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var l = Quad(_bill, Tex.Ring, 1f, new Color(0.5f, 0.78f, 1f), 0.16f);
                        l.T.localScale = new Vector3(R * 8f, R * 4.4f, 1f);
                        l.T.localPosition = new Vector3(s * R * 3.6f, 0f, 0f);
                        _loops.Add(l);
                    }
                    _spin = new GameObject("Spin").transform;
                    _spin.SetParent(_bill, false);
                    _beamA = Beam(_spin, R * 3f, R * 17f, new Color(0.7f, 0.92f, 1f), 0.85f, 0f);
                    _beamB = Beam(_spin, R * 3f, R * 17f, new Color(0.7f, 0.92f, 1f), 0.85f, 180f);
                    _glow = Quad(_bill, Tex.Glow, R * 12f, new Color(0.62f, 0.88f, 1f), 0.4f);
                    _cross = Quad(_bill, Tex.Cross, R * 14f, new Color(0.88f, 0.97f, 1f), 0.6f);
                    break;
                }

                case StarSpectralClass.Magnetar:
                {
                    _spin = new GameObject("Spin").transform;
                    _spin.SetParent(_bill, false);
                    for (int i = 0; i < 8; i++)
                    {
                        var holder = new GameObject("Loop").transform;
                        holder.SetParent(_spin, false);
                        holder.localRotation = Quaternion.Euler(0f, 0f, i * 45f);
                        var col = Color.Lerp(new Color(0.69f, 0.42f, 1f), new Color(1f, 0.35f, 0.82f), (i % 3) / 2f);
                        var l = Quad(holder, Tex.Ring, 1f, col, 0.45f);
                        _loops.Add(l);
                    }
                    _glow = Quad(_bill, Tex.Glow, R * 11f, new Color(0.7f, 0.42f, 1f), 0.4f);
                    _burst = Quad(_bill, Tex.Ring, R * 2f, new Color(1f, 0.55f, 0.95f), 0f);
                    break;
                }

                case StarSpectralClass.Protostar:
                {
                    _cloud = Quad(_bill, Tex.Dust, R * 30f, Color.white, 0.75f);
                    _glow = Quad(_bill, Tex.Glow, R * 7f, new Color(1f, 0.6f, 0.3f), 0.45f);
                    var holder = new GameObject("DiskTilt").transform;
                    holder.SetParent(_bill, false);
                    holder.localScale = new Vector3(1f, 0.3f, 1f);
                    _disk = new GameObject("Disk").transform;
                    _disk.SetParent(holder, false);
                    Quad(_disk, Tex.Disk, R * 9f, new Color(1f, 0.62f, 0.32f), 0.8f);
                    _jetA = Beam(_bill, R * 1.3f, R * 11f, new Color(0.75f, 0.88f, 1f), 0.7f, 0f);
                    _jetB = Beam(_bill, R * 1.3f, R * 11f, new Color(0.75f, 0.88f, 1f), 0.7f, 180f);
                    break;
                }

                case StarSpectralClass.PlanetaryNebula:
                {
                    var holder = new GameObject("NebulaTilt").transform;
                    holder.SetParent(_bill, false);
                    holder.localScale = new Vector3(1f, 0.78f, 1f);
                    holder.localRotation = Quaternion.Euler(0f, 0f, 24f + _phase * 20f);
                    _neb = Quad(holder, Tex.Nebula, R * 26f, Color.white, 0.95f);
                    _glow = Quad(_bill, Tex.Glow, R * 8f, new Color(0.8f, 0.92f, 1f), 0.5f);
                    break;
                }
            }

            int layer = transform.parent != null ? transform.parent.gameObject.layer : 0;
            SetLayer(gameObject, layer);
        }

        private Part Quad(Transform parent, Texture2D tex, float size, Color col, float alpha)
        {
            var go = new GameObject("Fx");
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(size, size, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Mat(tex);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var p = new Part { T = go.transform, R = r, Col = col, A = alpha };
            _parts.Add(p);
            Set(p, alpha);
            return p;
        }

        /// <summary>Луч от центра наружу (основание в центре звезды), повёрнутый на angle градусов.</summary>
        private Part Beam(Transform parent, float width, float length, Color col, float alpha, float angle)
        {
            var holder = new GameObject("Beam").transform;
            holder.SetParent(parent, false);
            holder.localRotation = Quaternion.Euler(0f, 0f, angle);
            var p = Quad(holder, Tex.Beam, 1f, col, alpha);
            p.T.localScale = new Vector3(width, length, 1f);
            p.T.localPosition = new Vector3(0f, length * 0.5f, 0f);
            return p;
        }

        // ==================== АНИМАЦИЯ ====================

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float t = Time.unscaledTime + _phase;
            float R = _r;

            // На дальней карте — размер и видимость как у спрайтов звезды; вблизи уступаем мини-системе
            if (_map && _anim != null)
            {
                float k = _anim.BaseScale > 0f ? _anim.Scale / _anim.BaseScale : 0f;
                transform.localScale = Vector3.one * k;
                float near = 1f;
                if (_sys != null && GalaxyView.IsKnownToPlayer(_sys))
                    near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(52f, 90f, cam.transform.position.y));
                _fade = _anim.Visibility * near;
                bool on = _fade > 0.01f && k > 0f;
                if (_bill.gameObject.activeSelf != on) _bill.gameObject.SetActive(on);
                if (_companion != null && _companion.gameObject.activeSelf != on) _companion.gameObject.SetActive(on);
                if (!on) return;
            }

            _bill.rotation = cam.transform.rotation;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 1.3f);

            switch (_cls)
            {
                case StarSpectralClass.ClassO:
                    Set(_glow, _glow.A * (0.85f + 0.15f * pulse));
                    _cross.T.localRotation = Quaternion.Euler(0f, 0f, t * 3f);
                    Set(_cross, _cross.A * (0.8f + 0.2f * pulse));
                    break;

                case StarSpectralClass.RedGiant:
                {
                    float b = 1f + 0.06f * Mathf.Sin(t * 0.9f);
                    _glow.T.localScale = Vector3.one * R * 5f * b;
                    Set(_glow, _glow.A * (0.8f + 0.2f * Mathf.Sin(t * 0.9f)));
                    break;
                }

                case StarSpectralClass.WhiteDwarf:
                    _cross.T.localScale = Vector3.one * R * 18f * (0.92f + 0.08f * Mathf.Sin(t * 3f));
                    Set(_cross, _cross.A * (0.85f + 0.15f * Mathf.Sin(t * 3f)));
                    Set(_glow, _glow.A);
                    break;

                case StarSpectralClass.BrownDwarf:
                    Set(_glow, _glow.A * (0.85f + 0.15f * pulse));
                    break;

                case StarSpectralClass.Binary:
                {
                    float a = t * 0.55f, orbit = R * 2.7f;
                    _companion.localPosition = new Vector3(Mathf.Cos(a) * orbit, 0f, Mathf.Sin(a) * orbit);
                    _companion.rotation = cam.transform.rotation;
                    foreach (var p in _parts) Set(p, p.A);
                    break;
                }

                case StarSpectralClass.WolfRayet:
                    Set(_glow, _glow.A * (0.8f + 0.2f * pulse));
                    for (int i = 0; i < _shells.Count; i++)
                    {
                        float k = Mathf.Repeat(t * 0.16f + i / (float)_shells.Count, 1f);
                        var p = _shells[i];
                        p.T.localScale = Vector3.one * R * Mathf.Lerp(2f, 11f, k);
                        p.T.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 1f : -1f) * t * 9f + i * 40f);
                        var c = Color.Lerp(new Color(0.36f, 0.95f, 0.92f), new Color(0.69f, 0.42f, 1f), k);
                        Set(p, Mathf.Pow(1f - k, 1.4f) * 0.7f * Mathf.Clamp01(k * 8f), c);
                    }
                    break;

                case StarSpectralClass.Pulsar:
                {
                    float ang = t * 2.6f;
                    _spin.localRotation = Quaternion.Euler(0f, 0f, -ang * Mathf.Rad2Deg + 20f);
                    // Ширина луча «дышит» — будто конус поворачивается к зрителю; вспышка, когда смотрит на нас
                    float w = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(ang));
                    float flash = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(ang * 2f)), 16f);
                    foreach (var b in new[] { _beamA, _beamB })
                    {
                        var s = b.T.localScale;
                        b.T.localScale = new Vector3(R * 3f * w, s.y, 1f);
                        Set(b, b.A * (0.7f + 0.3f * w));
                    }
                    _glow.T.localScale = Vector3.one * R * (10f + 14f * flash);
                    Set(_glow, 0.35f + 0.6f * flash);
                    _cross.T.localScale = Vector3.one * R * (10f + 22f * flash);
                    Set(_cross, 0.4f + 0.6f * flash);
                    foreach (var l in _loops) Set(l, l.A * (0.8f + 0.2f * pulse));
                    break;
                }

                case StarSpectralClass.Magnetar:
                {
                    _spin.localRotation = Quaternion.Euler(0f, 0f, t * 12f);
                    for (int i = 0; i < _loops.Count; i++)
                    {
                        var l = _loops[i];
                        float h = R * (6f + 2.4f * Mathf.Sin(t * 1.7f + i * 2f));
                        l.T.localScale = new Vector3(h, h * 0.5f, 1f);
                        l.T.localPosition = new Vector3(h * 0.5f, 0f, 0f);
                        Set(l, 0.3f + 0.25f * Mathf.Sin(t * 3f + i));
                    }
                    float q = Mathf.Repeat(t, 3.2f) / 3.2f;
                    float burst = q < 0.45f ? 1f - q / 0.45f : 0f;
                    _burst.T.localScale = Vector3.one * R * Mathf.Lerp(2f, 22f, Mathf.Clamp01(q / 0.45f));
                    Set(_burst, 0.8f * burst);
                    _glow.T.localScale = Vector3.one * R * (11f + 5f * burst);
                    Set(_glow, 0.35f + 0.35f * burst);
                    break;
                }

                case StarSpectralClass.Protostar:
                {
                    _cloud.T.localRotation = Quaternion.Euler(0f, 0f, t * 1.5f);
                    Set(_cloud, _cloud.A);
                    _disk.localRotation = Quaternion.Euler(0f, 0f, -t * 40f);
                    float j = 1f + 0.12f * Mathf.Sin(t * 4f);
                    foreach (var p in new[] { _jetA, _jetB })
                    {
                        p.T.localScale = new Vector3(R * 1.3f, R * 11f * j, 1f);
                        p.T.localPosition = new Vector3(0f, R * 11f * j * 0.5f, 0f);
                        Set(p, p.A);
                    }
                    foreach (var p in _parts) if (p != _cloud && p != _jetA && p != _jetB) Set(p, p.A);
                    break;
                }

                case StarSpectralClass.PlanetaryNebula:
                    _neb.T.localRotation = Quaternion.Euler(0f, 0f, t * 1.2f);
                    _neb.T.localScale = Vector3.one * R * 26f * (1f + 0.025f * Mathf.Sin(t * 0.5f));
                    Set(_neb, _neb.A);
                    Set(_glow, _glow.A * (0.85f + 0.15f * pulse));
                    break;
            }
        }

        // ==================== ОБЩЕЕ ====================

        private static Shader s_shader;
        private static bool s_checked;
        private static Mesh s_quad;
        private static MaterialPropertyBlock s_mpb;
        private static readonly Dictionary<Texture2D, Material> s_mats = new Dictionary<Texture2D, Material>();
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { s_checked = false; s_shader = null; s_quad = null; s_mats.Clear(); Tex.Reset(); }

        private static bool Ready()
        {
            if (!s_checked)
            {
                s_checked = true;
                s_shader = Resources.Load<Shader>("Shaders/StarFx") ?? Shader.Find("Astropolity/Star/Fx");
                if (s_shader == null || !s_shader.isSupported) Debug.LogWarning("[StarExtras] Нет шейдера Astropolity/Star/Fx — эффекты необычных звёзд выключены.");
            }
            return s_shader != null && s_shader.isSupported;
        }

        private static Material Mat(Texture2D tex)
        {
            if (s_mats.TryGetValue(tex, out var m) && m != null) return m;
            m = new Material(s_shader) { name = "StarFx_" + tex.name, hideFlags = HideFlags.DontSave };
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_Intensity", 1.8f);
            s_mats[tex] = m;
            return m;
        }

        private void Set(Part p, float alpha, Color? col = null)
        {
            if (p == null || p.R == null) return;
            if (s_mpb == null) s_mpb = new MaterialPropertyBlock();
            var c = col ?? p.Col;
            s_mpb.Clear();
            s_mpb.SetVector(ColorId, new Vector4(c.r, c.g, c.b, Mathf.Max(0f, alpha) * _fade));
            p.R.SetPropertyBlock(s_mpb);
        }

        private static Mesh QuadMesh()
        {
            if (s_quad != null) return s_quad;
            s_quad = new Mesh
            {
                name = "StarFxQuad", hideFlags = HideFlags.DontSave,
                vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            s_quad.RecalculateBounds();
            s_quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return s_quad;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }

        // ==================== ТЕКСТУРЫ ====================

        /// <summary>Процедурные формы слоёв: строятся один раз, линейные (цвет приводит шейдер).</summary>
        private static class Tex
        {
            private static Texture2D s_glow, s_beam, s_ring, s_shell, s_cross, s_disk, s_dust, s_nebula;

            private static Texture2D Get(ref Texture2D field, System.Func<Texture2D> make)
            {
                if (field == null) field = make();
                return field;
            }

            public static void Reset() { s_glow = s_beam = s_ring = s_shell = s_cross = s_disk = s_dust = s_nebula = null; }

            public static Texture2D Glow => Get(ref s_glow, () => Make("Glow", 128, 128, (u, v) =>
            {
                float d = Len(u, v);
                return A(Mathf.Exp(-d * d * 4.5f) * (1f - Edge(0.85f, 1f, d)));
            }));

            public static Texture2D Beam => Get(ref s_beam, () => Make("Beam", 64, 256, (u, v) =>
            {
                float y = (v + 1f) * 0.5f;                 // 0 — основание, 1 — конец
                float w = 0.08f + 0.5f * y;
                float x = u / w;
                return A(Mathf.Exp(-x * x * 3f) * Mathf.Pow(1f - y, 1.3f) * Edge(0f, 0.06f, y));
            }));

            public static Texture2D Ring => Get(ref s_ring, () => Make("Ring", 256, 256, (u, v) =>
            {
                float d = Len(u, v), x = (d - 0.88f) / 0.045f;
                return A(Mathf.Exp(-x * x));
            }));

            public static Texture2D Shell => Get(ref s_shell, () => Make("Shell", 256, 256, (u, v) =>
            {
                float d = Len(u, v), x = (d - 0.84f) / 0.07f;
                float an = Mathf.Atan2(v, u);
                float clump = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(an * 11f + Mathf.Sin(an * 3f) * 2f)) * (0.6f + 0.4f * Mathf.Sin(an * 23f + 1f));
                return A(Mathf.Exp(-x * x) * clump);
            }));

            public static Texture2D Cross => Get(ref s_cross, () => Make("Cross", 256, 256, (u, v) =>
            {
                float d = Len(u, v);
                float h = Mathf.Exp(-(v / 0.012f) * (v / 0.012f)) * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(u)), 2.2f);
                float vv = Mathf.Exp(-(u / 0.012f) * (u / 0.012f)) * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(v)), 2.2f);
                return A(Mathf.Clamp01(h + vv + Mathf.Exp(-d * d * 60f) * 0.6f));
            }));

            public static Texture2D Disk => Get(ref s_disk, () => Make("Disk", 256, 256, (u, v) =>
            {
                float d = Len(u, v), an = Mathf.Atan2(v, u);
                float band = Edge(0.18f, 0.32f, d) * (1f - Edge(0.78f, 1f, d));
                float stripes = 0.55f + 0.45f * Mathf.Sin(d * 46f + Mathf.Sin(an * 2f) * 1.5f);
                float lump = 0.6f + 0.4f * Mathf.Sin(an * 3f + d * 9f);
                return A(band * stripes * lump);
            }));

            /// <summary>Пылевое облако протозвезды: бурые и лиловые сгустки, цвет запечён в текстуру.</summary>
            public static Texture2D Dust => Get(ref s_dust, () => MakeColored("Dust", 256, 1234, (u, v, blobs) =>
            {
                float wa = 0f, wb = 0f;
                foreach (var b in blobs)
                {
                    float dx = u - b.x, dy = v - b.y, g = Mathf.Exp(-(dx * dx + dy * dy) / (b.z * b.z));
                    if (b.w > 0.5f) wa += g; else wb += g;
                }
                float sum = wa + wb, d = Len(u, v);
                float a = Mathf.Clamp01(sum * 0.55f) * (1f - Edge(0.55f, 1f, d));
                var cA = new Color(0.62f, 0.34f, 0.2f); var cB = new Color(0.38f, 0.22f, 0.46f);
                var c = sum > 1e-4f ? Color.Lerp(cB, cA, wa / sum) : cA;
                c.a = a;
                return c;
            }));

            /// <summary>Кольцо планетарной туманности: бирюза внутри, синее посередине, красное снаружи.</summary>
            public static Texture2D Nebula => Get(ref s_nebula, () => MakeColored("Nebula", 256, 4321, (u, v, blobs) =>
            {
                float d = Len(u, v), an = Mathf.Atan2(v, u);
                float x = (d - 0.6f) / 0.2f;
                float clump = 0.6f + 0.25f * Mathf.Sin(an * 7f + Mathf.Sin(an * 2f) * 3f) + 0.15f * Mathf.Sin(an * 19f);
                float a = Mathf.Exp(-x * x) * clump + Mathf.Exp(-d * d * 6f) * 0.12f;
                a *= 1f - Edge(0.88f, 1f, d);
                Color c = d < 0.55f ? Color.Lerp(new Color(0.36f, 0.95f, 0.92f), new Color(0.42f, 0.62f, 1f), Edge(0.3f, 0.55f, d))
                                    : Color.Lerp(new Color(0.42f, 0.62f, 1f), new Color(1f, 0.36f, 0.42f), Edge(0.55f, 0.8f, d));
                c.a = Mathf.Clamp01(a);
                return c;
            }));

            private static Color A(float a) => new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            private static float Len(float u, float v) => Mathf.Sqrt(u * u + v * v);

            private static float Edge(float e0, float e1, float x)
            {
                float t = Mathf.Clamp01((x - e0) / (e1 - e0));
                return t * t * (3f - 2f * t);
            }

            /// <summary>Текстура по функции от координат -1..1 (u — вправо, v — вверх).</summary>
            private static Texture2D Make(string name, int w, int h, System.Func<float, float, Color> f)
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true)
                { name = "StarFx" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f(x / (w - 1f) * 2f - 1f, y / (h - 1f) * 2f - 1f);
                tex.SetPixels(px);
                tex.Apply(true, true);
                return tex;
            }

            private static Texture2D MakeColored(string name, int n, int seed, System.Func<float, float, Vector4[], Color> f)
            {
                var rnd = new System.Random(seed);
                var blobs = new Vector4[34];
                for (int i = 0; i < blobs.Length; i++)
                {
                    float a = (float)(rnd.NextDouble() * Mathf.PI * 2), r = (float)Mathf.Sqrt((float)rnd.NextDouble()) * 0.7f;
                    blobs[i] = new Vector4(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0.12f + (float)rnd.NextDouble() * 0.22f, (float)rnd.NextDouble());
                }
                return Make(name, n, n, (u, v) => f(u, v, blobs));
            }
        }
    }
}
