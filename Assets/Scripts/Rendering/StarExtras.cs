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
        private Transform _bill, _spin, _companion;
        private readonly List<Part> _parts = new List<Part>();
        private readonly List<Part> _shells = new List<Part>();
        private readonly List<Part> _loops = new List<Part>();
        private Part _glow, _cross, _beamA, _beamB, _burst, _cloud, _neb, _compGlow, _ring;
        private Transform _magnet;
        private readonly List<Part> _lines = new List<Part>();

        // Пульсар: магнитная ось наклонена к оси вращения и медленно описывает конус — лучи-маяк обходят небо
        private const float PulsarTilt = 35f, PulsarPeriod = 5f;
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
                    // Как WR 124 / M1-67 на снимках «Хаббла»: ослепительная голубая звезда в рваном облаке
                    // сгустков и радиальных струй раскалённого газа (розово-красное свечение водорода).
                    // Два слоя облака медленно расширяются и сменяют друг друга — газ всё время истекает наружу
                    for (int i = 0; i < 2; i++) _shells.Add(Quad(_bill, Tex.Wind, R * 24f, Color.white, 0.8f));
                    _glow = Quad(_bill, Tex.Glow, R * 5f, new Color(0.62f, 0.85f, 1f), 0.45f);
                    break;

                case StarSpectralClass.Pulsar:
                {
                    // Мягкое свечение вокруг нейтронной звезды; вспыхивает, когда луч проходит мимо камеры
                    _glow = Quad(_bill, Tex.Glow, R * 9f, new Color(0.62f, 0.85f, 1f), 0.28f);
                    // Туманность ветра пульсара — слабое кольцо в плоскости экватора вращения
                    var eq = new GameObject("Equator").transform;
                    eq.SetParent(transform, false);
                    eq.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    _ring = Quad(eq, Tex.Shell, R * 15f, new Color(0.45f, 0.7f, 1f), 0.12f);
                    // Дипольное поле: петли r = L·sin²θ в плоскостях через магнитную ось, вращаются вместе с ней
                    _magnet = new GameObject("Magnet").transform;
                    _magnet.SetParent(transform, false);
                    for (int k = 0; k < 6; k++)
                    {
                        _lines.Add(FieldLine(_magnet, R, R * 5f, k * 60f, new Color(0.5f, 0.75f, 1f), 0.22f));
                        _lines.Add(FieldLine(_magnet, R, R * 9f, k * 60f + 30f, new Color(0.45f, 0.65f, 1f), 0.1f));
                    }
                    // Лучи — вдоль магнитной оси в обе стороны, повёрнуты к камере вокруг своей оси (объёмный конус)
                    _beamA = Quad(transform, Tex.Beam, 1f, new Color(0.72f, 0.92f, 1f), 0.7f);
                    _beamB = Quad(transform, Tex.Beam, 1f, new Color(0.72f, 0.92f, 1f), 0.7f);
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
                    // Как L1527 / HH 30 на снимках JWST и «Хаббла»: звезду прячет пылевой диск, видимый ребром
                    // (тёмная перемычка), вверх и вниз раскрываются воронки отражательной туманности — оранжевые
                    // у центра, голубые к краям; вдоль оси бьют тонкие джеты с узлами, на концах — ударные дуги.
                    // Ось наклонена по-своему у каждой системы
                    var tilt = new GameObject("Axis").transform;
                    tilt.SetParent(_bill, false);
                    tilt.localRotation = Quaternion.Euler(0f, 0f, (_phase * 7.3f) % 70f - 35f);
                    _cloud = Quad(tilt, Tex.Hourglass, R * 30f, Color.white, 0.95f);
                    _glow = Quad(tilt, Tex.Glow, R * 3.5f, new Color(1f, 0.62f, 0.34f), 0.45f);
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        var jet = Quad(tilt, Tex.Line, 1f, new Color(1f, 0.45f, 0.58f), 0.32f);
                        jet.T.localRotation = Quaternion.Euler(0f, 0f, 90f);
                        jet.T.localScale = new Vector3(R * 25f, R * 0.45f, 1f);
                        jet.T.localPosition = new Vector3(0f, sgn * R * 13.5f, 0f);
                        var bow = Quad(tilt, Tex.Bow, R * 4.5f, new Color(1f, 0.45f, 0.6f), 0.55f);
                        bow.T.localPosition = new Vector3(0f, sgn * R * 26f, 0f);
                        bow.T.localRotation = Quaternion.Euler(0f, 0f, sgn > 0 ? 0f : 180f);
                        _shells.Add(bow);
                        for (int i = 0; i < 4; i++)
                        {
                            var knot = Quad(tilt, Tex.Glow, R * 1.4f, new Color(1f, 0.5f, 0.62f), 0f);
                            knot.Col.a = sgn;                         // направление полёта узла
                            _loops.Add(knot);
                        }
                    }
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

        /// <summary>Силовая линия диполя (магнитная ось — локальная Y) в плоскости, повёрнутой на azimuth вокруг оси.</summary>
        private Part FieldLine(Transform parent, float R, float L, float azimuth, Color col, float alpha)
        {
            var go = new GameObject("FieldLine");
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(0f, azimuth, 0f);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.sharedMaterial = Mat(Tex.Line);
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            const int N = 48;
            float th0 = Mathf.Asin(Mathf.Sqrt(Mathf.Clamp01(R * 1.05f / L)));
            lr.positionCount = N;
            for (int i = 0; i < N; i++)
            {
                float th = Mathf.Lerp(th0, Mathf.PI - th0, i / (N - 1f));
                float r = L * Mathf.Sin(th) * Mathf.Sin(th);
                lr.SetPosition(i, new Vector3(r * Mathf.Sin(th), r * Mathf.Cos(th), 0f));
            }
            lr.widthMultiplier = R * 0.12f;
            var p = new Part { T = go.transform, R = lr, Col = col, A = alpha };
            _parts.Add(p);
            Set(p, alpha);
            return p;
        }

        /// <summary>Луч вдоль оси dir (локальные координаты), повёрнутый плоскостью к камере — выглядит объёмным конусом.</summary>
        private static void AimBeam(Transform beam, Vector3 dir, Vector3 toCam, float width, float length)
        {
            Vector3 right = Vector3.Cross(dir, toCam);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(dir, Vector3.right);
            right.Normalize();
            Vector3 fwd = Vector3.Cross(right, dir);
            beam.localRotation = Quaternion.LookRotation(fwd, dir);
            beam.localPosition = dir * (length * 0.5f);
            beam.localScale = new Vector3(width, length, 1f);
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
                    Set(_glow, _glow.A * (0.9f + 0.1f * pulse));
                    for (int i = 0; i < _shells.Count; i++)
                    {
                        // Цикл ~25 с: слой растёт 0,8 → 1,2, проявляется и гаснет; второй — в противофазе
                        float k = Mathf.Repeat(t * 0.04f + i * 0.5f, 1f);
                        var p = _shells[i];
                        p.T.localScale = Vector3.one * R * 24f * Mathf.Lerp(0.8f, 1.2f, k);
                        p.T.localRotation = Quaternion.Euler(0f, 0f, i * 137f + (i == 0 ? 1f : -1f) * t * 0.8f);
                        Set(p, p.A * Mathf.Sin(k * Mathf.PI));
                    }
                    break;

                case StarSpectralClass.Pulsar:
                {
                    // Магнитная ось обходит конус вокруг оси вращения (локальная Y) — спокойно, раз в 5 с
                    _magnet.localRotation = Quaternion.Euler(0f, t / PulsarPeriod * 360f, 0f) * Quaternion.Euler(0f, 0f, PulsarTilt);
                    Vector3 axis = _magnet.localRotation * Vector3.up;
                    Vector3 toCam = transform.InverseTransformDirection(cam.transform.position - transform.position).normalized;
                    float flash = 0f;
                    for (int b = 0; b < 2; b++)
                    {
                        var beam = b == 0 ? _beamA : _beamB;
                        Vector3 dir = b == 0 ? axis : -axis;
                        float face = Mathf.Max(0f, Vector3.Dot(dir, toCam));
                        flash = Mathf.Max(flash, Mathf.Pow(face, 24f));
                        AimBeam(beam.T, dir, toCam, R * 3.2f, R * 16f);
                        Set(beam, beam.A * (0.75f + 0.25f * face));
                    }
                    _glow.T.localScale = Vector3.one * R * (9f + 9f * flash);
                    Set(_glow, 0.28f + 0.55f * flash);
                    _ring.T.localRotation = Quaternion.Euler(0f, 0f, t * 4f);
                    Set(_ring, _ring.A);
                    foreach (var l in _lines) Set(l, l.A * (0.85f + 0.15f * pulse));
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
                    Set(_cloud, _cloud.A * (0.93f + 0.07f * Mathf.Sin(t * 0.4f)));
                    Set(_glow, _glow.A * (0.85f + 0.15f * pulse));
                    // Узлы Хербига-Аро летят по джетам наружу и гаснут к ударной дуге
                    for (int i = 0; i < _loops.Count; i++)
                    {
                        var k = _loops[i];
                        float q = Mathf.Repeat(t * 0.035f + (i % 4) / 4f + (i / 4) * 0.13f, 1f);
                        k.T.localPosition = new Vector3(0f, k.Col.a * R * Mathf.Lerp(2f, 25f, q), 0f);
                        float b = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(i * 1.7f + 0.4f));
                        Set(k, Mathf.Sin(q * Mathf.PI) * 0.75f * b, new Color(k.Col.r, k.Col.g, k.Col.b));
                    }
                    foreach (var bw in _shells) Set(bw, bw.A * (0.8f + 0.2f * Mathf.Sin(t * 0.7f)));
                    foreach (var p in _parts) if (p != _cloud && p != _glow && !_loops.Contains(p) && !_shells.Contains(p)) Set(p, p.A);
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
            private static Texture2D s_hour, s_bow, s_wind, s_line, s_glow, s_beam, s_ring, s_shell, s_cross, s_nebula;

            private static Texture2D Get(ref Texture2D field, System.Func<Texture2D> make)
            {
                if (field == null) field = make();
                return field;
            }

            public static void Reset() { s_hour = s_bow = null; s_wind = null; s_line = null; s_glow = s_beam = s_ring = s_shell = s_cross = s_nebula = null; }

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

            /// <summary>
            /// Облако ветра звезды Вольфа-Райе (как M1-67 вокруг WR 124): сотни узелков газа, слегка вытянутых
            /// по радиусу, гуще в рваной оболочке, на подложке крупных тусклых облаков. Цвет запечён.
            /// Сгустки рисуются по своим рамкам — быстро даже при сотнях штук.
            /// </summary>
            public static Texture2D Wind => Get(ref s_wind, () =>
            {
                const int N = 256;
                var rgb = new Vector3[N * N];
                var alpha = new float[N * N];
                var rnd = new System.Random(777);
                float Rnd() => (float)rnd.NextDouble();
                // Сгусток — гауссово пятно, вытянутое по радиусу; рисуется только в своей рамке
                void Blob(float cx, float cy, float dx, float dy, float len, float wid, float bright, Vector3 col)
                {
                    float ext = Mathf.Max(len, wid) * 2.6f;
                    int x0 = Mathf.Max(0, (int)((cx - ext + 1f) * 0.5f * (N - 1))), x1 = Mathf.Min(N - 1, (int)((cx + ext + 1f) * 0.5f * (N - 1)) + 1);
                    int y0 = Mathf.Max(0, (int)((cy - ext + 1f) * 0.5f * (N - 1))), y1 = Mathf.Min(N - 1, (int)((cy + ext + 1f) * 0.5f * (N - 1)) + 1);
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float u = x / (N - 1f) * 2f - 1f - cx, v = y / (N - 1f) * 2f - 1f - cy;
                        float along = u * dx + v * dy, across = -u * dy + v * dx;
                        float g = Mathf.Exp(-(along * along) / (len * len) - (across * across) / (wid * wid)) * bright;
                        int k = y * N + x;
                        rgb[k] += col * g;
                        alpha[k] += g;
                    }
                }
                // Оболочка рваная: её радиус гуляет по углу
                float Shell(float a) => 0.5f + 0.12f * Mathf.Sin(a * 3f + 1.3f) + 0.06f * Mathf.Sin(a * 7f);
                // Крупные тусклые облака — подложка оболочки
                for (int c = 0; c < 46; c++)
                {
                    float a = Rnd() * Mathf.PI * 2f;
                    float r = Mathf.Clamp(Shell(a) + (Rnd() - 0.5f) * 0.3f, 0.15f, 0.85f);
                    Blob(Mathf.Cos(a) * r, Mathf.Sin(a) * r, Mathf.Cos(a), Mathf.Sin(a),
                         0.08f + Rnd() * 0.08f, 0.06f + Rnd() * 0.07f, 0.12f + Rnd() * 0.12f, new Vector3(1f, 0.42f, 0.42f));
                }
                // Узелки газа: в основном розово-красные (водород), часть — жёлто-оранжевые; яркость с длинным хвостом
                for (int c = 0; c < 420; c++)
                {
                    float a = Rnd() * Mathf.PI * 2f;
                    float r = Rnd() < 0.85f ? Shell(a) + (Rnd() - 0.5f) * 0.36f : 0.18f + Rnd() * 0.3f;
                    r = Mathf.Clamp(r, 0.12f, 0.88f);
                    float len = 0.012f + Rnd() * 0.035f, wid = 0.009f + Rnd() * 0.016f;
                    float bright = 0.15f + Mathf.Pow(Rnd(), 2.2f) * 0.9f;
                    float tone = Rnd();
                    Vector3 col = tone < 0.65f ? new Vector3(1f, 0.36f + 0.1f * tone, 0.4f) : new Vector3(1f, 0.62f + 0.15f * Rnd(), 0.45f);
                    Blob(Mathf.Cos(a) * r, Mathf.Sin(a) * r, Mathf.Cos(a), Mathf.Sin(a), len, wid, bright, col);
                }
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, true, true)
                { name = "StarFxWind", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    float u = x / (N - 1f) * 2f - 1f, v = y / (N - 1f) * 2f - 1f, d = Len(u, v);
                    // Слабая диффузная дымка под сгустками, к краю кадра — в ноль
                    float haze = Mathf.Exp(-(d - 0.5f) * (d - 0.5f) / 0.05f) * 0.1f;
                    float aSum = alpha[i] + haze;
                    Vector3 c = aSum > 1e-5f ? (rgb[i] + new Vector3(1f, 0.45f, 0.45f) * haze) / aSum : Vector3.one;
                    float a = (1f - Mathf.Exp(-aSum * 1.4f)) * (1f - Edge(0.8f, 1f, d));
                    px[i] = new Color(c.x, c.y, c.z, a);
                }
                tex.SetPixels(px);
                tex.Apply(true, true);
                return tex;
            });

            /// <summary>
            /// «Песочные часы» протозвезды (как L1527): две воронки отражательной туманности вверх и вниз,
            /// со светлыми стенками и клочковатым светом внутри; тёмная перемычка пылевого диска посередине;
            /// у центра оранжевые, к краям голубые; нижняя (от нас) тусклее. Цвет запечён.
            /// </summary>
            public static Texture2D Hourglass => Get(ref s_hour, () =>
            {
                var rnd = new System.Random(5);
                var waves = new Vector4[9];
                for (int k = 0; k < waves.Length; k++)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI;
                    waves[k] = new Vector4(Mathf.Cos(a), Mathf.Sin(a), 2f + (float)rnd.NextDouble() * 9f, (float)rnd.NextDouble() * 6.28f);
                }
                return Make("Hourglass", 256, 256, (u, v) =>
                {
                    float n = 0f, nMax = 0f;
                    for (int k = 0; k < waves.Length; k++)
                    {
                        float amp = 1f / (1f + k * 0.4f);
                        n += Mathf.Sin((u * waves[k].x + v * waves[k].y) * waves[k].z + waves[k].w) * amp;
                        nMax += amp;
                    }
                    n = n / nMax * 0.5f + 0.5f;
                    float V = Mathf.Abs(v);
                    float w = 0.06f + 0.62f * Mathf.Pow(V, 0.75f);           // полуширина воронки
                    float xr = Mathf.Abs(u) / w;
                    float wall = Mathf.Exp(-((xr - 0.9f) / 0.22f) * ((xr - 0.9f) / 0.22f));
                    float fill = Mathf.Exp(-xr * xr * 0.9f) * 0.6f * (1f - Edge(0.75f, 1.2f, xr));
                    float lobe = (wall * 1.1f + fill) * (Mathf.Exp(-V * 1.3f) + Mathf.Exp(-V * 7f) * 0.8f)
                                 * Edge(0f, 0.08f, V) * (0.45f + 0.9f * n) * 1.3f;
                    if (v < 0f) lobe *= 0.6f;
                    lobe *= 1f - Mathf.Exp(-(v / 0.07f) * (v / 0.07f)) * Mathf.Exp(-(u / 0.55f) * (u / 0.55f)) * 0.95f;
                    lobe *= 1f - Edge(0.8f, 1f, Len(u, v));
                    var c = Color.Lerp(new Color(1f, 0.62f, 0.32f), new Color(0.5f, 0.65f, 1f), Edge(0.12f, 0.75f, V));
                    c.a = Mathf.Clamp01(lobe);
                    return c;
                });
            });

            /// <summary>Ударная дуга на конце джета: выпуклая наружу (вверх по текстуре), края гаснут.</summary>
            public static Texture2D Bow => Get(ref s_bow, () => Make("Bow", 128, 128, (u, v) =>
            {
                float r = Len(u, v + 0.55f), x = (r - 0.95f) / 0.1f;
                return A(Mathf.Exp(-x * x) * Edge(-0.35f, 0.25f, v) * (1f - Edge(0.55f, 0.95f, Mathf.Abs(u))));
            }));

            /// <summary>Линия поля: яркая середина поперёк, гаснет к концам вдоль.</summary>
            public static Texture2D Line => Get(ref s_line, () => Make("Line", 64, 32, (u, v) =>
                A(Mathf.Exp(-v * v * 5f) * (1f - Mathf.Pow(Mathf.Abs(u), 6f)))));

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
