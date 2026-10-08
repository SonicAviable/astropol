using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Эффекты космического боя.
    ///   • Энергия — луч из трёх слоёв (свечение, раскалённое ядро, дрожащие нити разряда), растёт и гаснет, держится на цели.
    ///   • Кинетика — дульная вспышка-конус и очередь трассеров; снаряды летят в точку на корпусе движущейся цели.
    ///   • Ракеты — двигатель с факелом, огненный след и дымный шлейф, наведение на цель по дуге.
    ///   • Щит — настоящий пузырь вокруг корабля: волна по сфере с шестигранной сеткой (ShieldBubble), при пробитии — распад поля.
    ///   • Броня — сноп искр-росчерков, рикошет, раскалённое пятно на корпусе, которое медленно остывает.
    ///   • Корпус — огненный выброс, дым, искры, обломки с горящими следами, пятно пожара на корпусе.
    ///   • Гибель — вспышка, огненный шар из клубов пламени, ударная волна, тяжёлый дым, обломки,
    ///     и сам корабль разламывается на обугленные горящие части, которые разлетаются, кувыркаясь.
    ///   • Повреждённые корабли дымят и горят (ShipDamage), пока их не починят.
    /// Все объекты из пулов, анимация в реальном времени; рисуется только вблизи камеры и в пределах лимита.
    /// </summary>
    public class CombatFx : MonoBehaviour
    {
        private static CombatFx s_instance;
        public static CombatFx Instance
        {
            get
            {
                if (s_instance == null) s_instance = new GameObject("CombatFx").AddComponent<CombatFx>();
                return s_instance;
            }
        }

        public enum Impact { None, Shield, Armor, Hull }
        private enum Kind { Beam, Filament, Tracer, Missile, Flash, Ring, Ember, Fire, Smoke, Spark, Debris, Decal }

        private class Fx
        {
            public Kind K;
            public GameObject Go;
            public Transform T;
            public SpriteRenderer Sr;
            public LineRenderer Lr;
            public TrailRenderer Tr;
            public MeshRenderer Mr;
            public bool Soft;
            public float Age, Life, Size, Grow, Rot, RotSpeed, Drag, EndWidth;
            public Vector3 From, To, Ctrl, Vel, AngVel, Local, FromLocal;
            public Transform Follow, Target, Shooter;
            public Color Col, Col2;
            public Impact Hit;
            public float HitSize;
            public bool Flat, ShieldBreak;
            public float SmokeTimer;
        }

        private class Wreckage
        {
            public Transform Root;
            public Vector3 Vel, AngVel;
            public Material Mat;
            public float Age, Life, SmokeTimer, Size;
            public Vector3[] Fires;
        }

        private const int MaxActive = 900;
        private const float MaxCameraDistance = 190f;
        private const int MaxLights = 8;
        private static readonly Color ShieldBlue = new Color(0.32f, 0.72f, 1f);
        private static readonly Color HotWhite = new Color(1f, 0.93f, 0.78f);
        private static readonly Color FireOrange = new Color(1f, 0.52f, 0.16f);
        private static readonly Color DeepRed = new Color(0.55f, 0.08f, 0.03f);

        private readonly List<Fx> _active = new List<Fx>();
        private readonly Stack<Fx> _sprites = new Stack<Fx>(), _softs = new Stack<Fx>(), _lines = new Stack<Fx>(),
                                   _missiles = new Stack<Fx>(), _debris = new Stack<Fx>();
        private readonly List<Wreckage> _wrecks = new List<Wreckage>();
        private Material _addMat, _lineMat, _softMat, _debrisMat, _trailMat;
        private Sprite _glow, _ring, _hot;
        private Sprite[] _fire, _smoke;
        private Mesh _chunk;
        private Camera _cam;

        // ==================== РЕСУРСЫ ====================

        private void Awake()
        {
            var add = Resources.Load<Shader>("Shaders/CombatAdditive") ?? ShaderCache.Sprite;
            var soft = Resources.Load<Shader>("Shaders/CombatSoft") ?? ShaderCache.Sprite;
            _glow = MakeSprite(64, r => Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));
            _ring = MakeSprite(128, r => Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.07f, 2f)));
            _hot = MakeSprite(64, r => Mathf.Pow(Mathf.Clamp01(1f - r), 1.4f) * (0.8f + 0.2f * Mathf.Cos(r * 20f)));
            _addMat = new Material(add) { name = "CombatAdditive", mainTexture = _glow.texture };
            _softMat = new Material(soft) { name = "CombatSoft" };

            var lineTex = new Texture2D(64, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 16; y++)
            {
                float v = (y + 0.5f) / 16f * 2f - 1f;
                float a = Mathf.Exp(-v * v * 3.2f);
                for (int x = 0; x < 64; x++) lineTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            lineTex.Apply();
            _lineMat = new Material(add) { name = "CombatAdditiveLine", mainTexture = lineTex };
            _trailMat = _lineMat;

            _fire = MakePuffs(1234, 0.42f, 1.5f);
            _smoke = MakePuffs(777, 0.6f, 1.1f);

            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _chunk = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            _debrisMat = ShaderCache.Lit != null ? new Material(ShaderCache.Lit) { name = "Debris" } : _addMat;
            if (_debrisMat.HasProperty("_BaseColor")) _debrisMat.SetColor("_BaseColor", new Color(0.16f, 0.15f, 0.15f));
            if (_debrisMat.HasProperty("_Smoothness")) _debrisMat.SetFloat("_Smoothness", 0.25f);
            if (_debrisMat.HasProperty("_Metallic")) _debrisMat.SetFloat("_Metallic", 0.6f);
            if (_debrisMat.HasProperty("_EmissionColor"))
            {
                _debrisMat.EnableKeyword("_EMISSION");
                _debrisMat.SetColor("_EmissionColor", new Color(0.9f, 0.3f, 0.05f) * 0.8f);
            }
        }

        private static Sprite MakeSprite(int n, System.Func<float, float> alpha)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha(Mathf.Sqrt(dx * dx + dy * dy)))));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }

        /// <summary>
        /// Клубы огня и дыма: четыре варианта на одной текстуре. Край рваный (шум искажает радиус),
        /// внутри — турбулентная плотность. turb — сила рваности, sharp — резкость края.
        /// </summary>
        private static Sprite[] MakePuffs(int seed, float turb, float sharp)
        {
            const int Cell = 128, N = 2;
            var tex = new Texture2D(Cell * N, Cell * N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[Cell * N * Cell * N];
            for (int cy = 0; cy < N; cy++)
                for (int cx = 0; cx < N; cx++)
                {
                    float ox = seed * 0.37f + (cx + cy * N) * 17.3f;
                    for (int y = 0; y < Cell; y++)
                        for (int x = 0; x < Cell; x++)
                        {
                            float u = (x + 0.5f) / Cell * 2f - 1f, v = (y + 0.5f) / Cell * 2f - 1f;
                            float r = Mathf.Sqrt(u * u + v * v);
                            float ang = Mathf.Atan2(v, u);
                            float edge = Fbm(Mathf.Cos(ang) * 1.6f + ox, Mathf.Sin(ang) * 1.6f + ox * 0.7f, 4);
                            float rr = r / Mathf.Max(0.35f, 0.82f + (edge - 0.5f) * turb * 1.4f);
                            float body = Fbm(u * 3.2f + ox, v * 3.2f - ox, 5);
                            float a = Mathf.Pow(Mathf.Clamp01(1f - rr), sharp) * Mathf.Lerp(0.45f, 1.15f, body);
                            px[(cy * Cell + y) * Cell * N + cx * Cell + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                        }
                }
            tex.SetPixels(px);
            tex.Apply(true);
            var sprites = new Sprite[N * N];
            for (int i = 0; i < sprites.Length; i++)
                sprites[i] = Sprite.Create(tex, new Rect((i % N) * Cell, (i / N) * Cell, Cell, Cell), new Vector2(0.5f, 0.5f), Cell);
            return sprites;
        }

        private static float Fbm(float x, float y, int oct)
        {
            float s = 0f, a = 0.5f;
            for (int i = 0; i < oct; i++) { s += a * Mathf.PerlinNoise(x, y); x *= 2.03f; y *= 2.03f; a *= 0.5f; }
            return s / (1f - Mathf.Pow(0.5f, oct));
        }

        // ==================== ПУЛЫ ====================

        private Fx Take(Kind k, bool soft = false)
        {
            Stack<Fx> pool = k == Kind.Beam || k == Kind.Filament || k == Kind.Tracer || k == Kind.Spark ? _lines
                           : k == Kind.Missile ? _missiles : k == Kind.Debris ? _debris : soft ? _softs : _sprites;
            Fx f = pool.Count > 0 ? pool.Pop() : Create(pool);
            f.K = k;
            f.Age = 0f;
            f.Hit = Impact.None;
            f.Flat = f.ShieldBreak = false;
            f.Rot = f.RotSpeed = 0f;
            f.Grow = 1f;
            f.Drag = 0f;
            f.EndWidth = 0.8f;
            f.Vel = f.AngVel = Vector3.zero;
            f.Follow = f.Target = f.Shooter = null;
            f.SmokeTimer = 0f;
            f.Go.SetActive(true);
            if (f.Tr != null) f.Tr.Clear();
            if (f.Lr != null) f.Lr.positionCount = 2;
            _active.Add(f);
            return f;
        }

        private Fx Create(Stack<Fx> pool)
        {
            var go = new GameObject("Fx");
            go.transform.SetParent(transform, false);
            var f = new Fx { Go = go, T = go.transform };
            if (pool == _lines)
            {
                f.Lr = go.AddComponent<LineRenderer>();
                f.Lr.positionCount = 2;
                f.Lr.useWorldSpace = true;
                f.Lr.textureMode = LineTextureMode.Stretch;
                f.Lr.numCapVertices = 2;
                f.Lr.sharedMaterial = _lineMat;
                f.Lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                f.Lr.receiveShadows = false;
            }
            else if (pool == _debris)
            {
                go.AddComponent<MeshFilter>().sharedMesh = _chunk;
                f.Mr = go.AddComponent<MeshRenderer>();
                f.Mr.sharedMaterial = _debrisMat;
                f.Mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                f.Tr = go.AddComponent<TrailRenderer>();
                f.Tr.sharedMaterial = _trailMat;
                f.Tr.time = 0.7f;
                f.Tr.minVertexDistance = 0.04f;
                f.Tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.09f), new Keyframe(1f, 0f));
                f.Tr.startColor = new Color(1f, 0.6f, 0.2f, 0.9f);
                f.Tr.endColor = new Color(0.6f, 0.15f, 0.05f, 0f);
                f.Tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            else
            {
                f.Sr = go.AddComponent<SpriteRenderer>();
                f.Soft = pool == _softs;
                f.Sr.sharedMaterial = f.Soft ? _softMat : _addMat;
                f.Sr.sprite = _glow;
                f.Sr.sortingOrder = f.Soft ? 58 : 60;
                if (pool == _missiles)
                {
                    f.Tr = go.AddComponent<TrailRenderer>();
                    f.Tr.sharedMaterial = _trailMat;
                    f.Tr.time = 0.55f;
                    f.Tr.minVertexDistance = 0.04f;
                    f.Tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.16f), new Keyframe(0.3f, 0.1f), new Keyframe(1f, 0f));
                    f.Tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    f.Tr.receiveShadows = false;
                }
            }
            return f;
        }

        private void Recycle(Fx f)
        {
            f.Go.SetActive(false);
            f.Follow = f.Target = f.Shooter = null;
            var pool = f.Lr != null ? _lines : f.Mr != null ? _debris : f.Tr != null ? _missiles : f.Soft ? _softs : _sprites;
            pool.Push(f);
        }

        // ==================== API ====================

        /// <summary>Стоит ли рисовать бой в этой точке: камера близко и эффектов не слишком много.</summary>
        public static bool CanShow(Vector3 pos)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            if (s_instance != null && s_instance._active.Count >= MaxActive) return false;
            return (cam.transform.position - pos).sqrMagnitude < MaxCameraDistance * MaxCameraDistance;
        }

        public Sprite GlowSprite => _glow;
        public Sprite RingSprite => _ring;
        public Material SpriteMaterial => _addMat;
        public Material LineMaterial => _lineMat;

        /// <summary>
        /// Выстрел. from — дуло, to — центр цели, hitSize — размер цели. target — носитель (корабль или станция):
        /// снаряды доводятся до движущейся цели, щит рисуется вокруг неё. shieldBreak — этот выстрел обнулил щит.
        /// </summary>
        public void Fire(WeaponDamageType weapon, Vector3 from, Vector3 to, Color col, bool hit, Impact impact, float hitSize,
                         Transform target = null, Transform shooter = null, bool shieldBreak = false)
        {
            // Точка попадания: на поверхности щита или на корпусе со стороны стрелка
            ShieldBubble bubble = null;
            if (hit && impact == Impact.Shield && target != null)
            {
                bubble = ShieldBubble.For(target, hitSize, ShieldBlue);
                if (bubble != null) to = bubble.SurfacePoint(from, 0.35f);
            }
            else if (hit)
            {
                Vector3 back = (from - to).normalized;
                to += back * hitSize * 0.3f + Random.insideUnitSphere * hitSize * 0.22f;
            }
            else
            {
                Vector3 off = Random.onUnitSphere * hitSize * 0.9f;
                off.y *= 0.3f;
                to = to + off + (to - from).normalized * hitSize * 1.8f;
                impact = Impact.None;
                target = null;
            }

            Vector3 dir = (to - from).normalized;
            switch (weapon)
            {
                case WeaponDamageType.Energy:
                {
                    MuzzleFlash(from, dir, col, 0.5f, shooter);
                    var glow = Take(Kind.Beam);
                    Bind(glow, from, to, target, shooter);
                    glow.Col = col; glow.Size = 0.42f; glow.Life = 0.48f;
                    var core = Take(Kind.Beam);
                    Bind(core, from, to, target, shooter);
                    core.Col = Color.Lerp(col, Color.white, 0.8f); core.Size = 0.1f; core.Life = 0.42f;
                    for (int i = 0; i < 2; i++)
                    {
                        var fil = Take(Kind.Filament);
                        Bind(fil, from, to, target, shooter);
                        fil.Col = Color.Lerp(col, Color.white, 0.4f); fil.Size = 0.035f; fil.Life = 0.38f;
                        fil.Lr.positionCount = 10;
                    }
                    if (impact != Impact.None) ImpactAt(to, dir, col, impact, hitSize, target, bubble, shieldBreak, 0.03f, true);
                    break;
                }
                case WeaponDamageType.Kinetic:
                {
                    MuzzleFlash(from, dir, new Color(1f, 0.8f, 0.45f), 0.65f, shooter);
                    const int n = 3;
                    float flight = 0.14f + Vector3.Distance(from, to) * 0.011f;
                    for (int i = 0; i < n; i++)
                    {
                        var tr = Take(Kind.Tracer);
                        Bind(tr, from + Random.insideUnitSphere * 0.06f, to + Random.insideUnitSphere * hitSize * 0.12f, target, null);
                        tr.Col = new Color(1f, 0.85f, 0.55f);
                        tr.Size = 0.075f;
                        tr.Life = flight;
                        tr.Age = -i * 0.06f;
                        tr.Hit = i == n - 1 ? impact : impact == Impact.None ? Impact.None : Impact.Armor;
                        tr.HitSize = i == n - 1 ? hitSize : hitSize * 0.4f;
                        tr.ShieldBreak = i == n - 1 && shieldBreak;
                        if (impact == Impact.Shield && i != n - 1) tr.Hit = Impact.Shield;
                    }
                    break;
                }
                default:
                {
                    MuzzleFlash(from, dir, new Color(1f, 0.7f, 0.35f), 0.45f, shooter);
                    var m = Take(Kind.Missile);
                    Bind(m, from, to, target, null);
                    Vector3 mid = (from + to) * 0.5f;
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    m.Ctrl = mid + side * Random.Range(-2.6f, 2.6f) + Vector3.up * Random.Range(0.6f, 2f);
                    m.Col = new Color(1f, 0.72f, 0.4f);
                    m.Size = 0.34f;
                    m.Life = 0.8f + Vector3.Distance(from, to) * 0.03f;
                    m.Hit = impact;
                    m.HitSize = hitSize;
                    m.ShieldBreak = shieldBreak;
                    m.Sr.sprite = _glow;
                    m.T.position = from;
                    m.Tr.Clear();
                    m.Tr.startColor = new Color(1f, 0.75f, 0.4f, 0.95f);
                    m.Tr.endColor = new Color(0.9f, 0.3f, 0.1f, 0f);
                    break;
                }
            }
        }

        /// <summary>Конечная точка следит за целью: запоминаем её в локальных координатах носителя.</summary>
        private static void Bind(Fx f, Vector3 from, Vector3 to, Transform target, Transform shooter)
        {
            f.From = from;
            f.To = to;
            f.Target = target;
            f.Shooter = shooter;
            if (target != null) f.Local = target.InverseTransformPoint(to);
            if (shooter != null) f.FromLocal = shooter.InverseTransformPoint(from);
        }

        private static Vector3 CurrentTo(Fx f) => f.Target != null ? f.Target.TransformPoint(f.Local) : f.To;
        private static Vector3 CurrentFrom(Fx f) => f.Shooter != null ? f.Shooter.TransformPoint(f.FromLocal) : f.From;

        /// <summary>Гибель корабля или станции: вспышка, огненный шар, ударная волна, дым, обломки.</summary>
        public void ShipDestroyed(Vector3 pos, float size, Color ownerCol)
        {
            FlashLight(pos, new Color(1f, 0.75f, 0.45f), size * 11f, 9f, 1.2f);
            Spawn(Kind.Flash, pos, Color.white, size * 3.2f, 0.22f, _glow);
            Spawn(Kind.Flash, pos, HotWhite, size * 5f, 0.6f, _glow);

            // Огненный шар из клубов пламени
            int puffs = Mathf.RoundToInt(7 + size * 4f);
            for (int i = 0; i < puffs; i++)
            {
                var f = Spawn(Kind.Fire, pos + Random.insideUnitSphere * size * 0.55f, HotWhite, size * Random.Range(1.0f, 1.7f),
                              Random.Range(0.9f, 1.6f), _fire[Random.Range(0, _fire.Length)]);
                f.Col2 = DeepRed; f.Grow = Random.Range(1.8f, 2.6f);
                f.Rot = Random.Range(0f, 360f); f.RotSpeed = Random.Range(-40f, 40f);
                f.Vel = Random.onUnitSphere * Random.Range(0.4f, 1.4f) * size; f.Drag = 1.5f;
                f.Age = -Random.Range(0f, 0.25f);
            }
            // Тяжёлый дым после огня
            for (int i = 0; i < puffs; i++)
            {
                var s = Spawn(Kind.Smoke, pos + Random.insideUnitSphere * size * 0.6f, new Color(0.13f, 0.12f, 0.12f, 0.75f),
                              size * Random.Range(1.2f, 1.9f), Random.Range(3.2f, 5f), _smoke[Random.Range(0, _smoke.Length)], true);
                s.Grow = Random.Range(2.2f, 3.2f); s.Rot = Random.Range(0f, 360f); s.RotSpeed = Random.Range(-12f, 12f);
                s.Vel = Random.onUnitSphere * Random.Range(0.3f, 0.9f) * size; s.Drag = 0.7f;
                s.Age = -Random.Range(0.3f, 0.7f);
            }
            var ring = Spawn(Kind.Ring, pos, new Color(1f, 0.75f, 0.45f, 0.9f), size * 8f, 1.0f, _ring);
            ring.Flat = true;
            var ring2 = Spawn(Kind.Ring, pos, Color.Lerp(ownerCol, Color.white, 0.5f), size * 5f, 0.6f, _ring);

            int sparks = Mathf.RoundToInt(22 + size * 12f);
            for (int i = 0; i < sparks; i++)
                SparkStreak(pos, Random.onUnitSphere * Random.Range(4f, 11f) * Mathf.Sqrt(size), HotWhite, Random.Range(0.5f, 1.1f), 0.05f);
            int chunks = Mathf.RoundToInt(5 + size * 4f);
            for (int i = 0; i < chunks; i++)
                DebrisChunk(pos + Random.insideUnitSphere * size * 0.3f, Random.onUnitSphere * Random.Range(1.5f, 4.5f) * Mathf.Sqrt(size),
                            Random.Range(0.08f, 0.22f) * Mathf.Sqrt(size), Random.Range(2.5f, 4.5f));
            for (int i = 0; i < 3; i++)
            {
                var sec = Spawn(Kind.Fire, pos + Random.insideUnitSphere * size * 1.2f, HotWhite, size * 1.1f, 0.7f, _fire[Random.Range(0, _fire.Length)]);
                sec.Col2 = DeepRed; sec.Grow = 2f; sec.Age = -Random.Range(0.3f, 1.1f); sec.Rot = Random.Range(0f, 360f);
            }
        }

        /// <summary>
        /// Корабль разламывается: модель делится на 2–3 части по длине, части обугливаются, раскалённые
        /// края тлеют, обломки разлетаются, кувыркаясь, горят и дымят, затем тают. Оригинал скрывается.
        /// </summary>
        public void Wreck(Transform ship, float size)
        {
            if (ship == null || ShaderCache.Lit == null) return;
            var parts = new List<MeshRenderer>();
            foreach (var r in ship.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || r.sharedMaterial == null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                string n = r.gameObject.name;
                if (n.Contains("Plume") || n.Contains("Shield") || n.Contains("Fan")) continue;
                if (r.sharedMaterial.shader != ShaderCache.Lit && !r.sharedMaterial.shader.name.Contains("Lit")) continue;
                parts.Add(r);
            }
            if (parts.Count == 0) return;

            // Делим по продольной оси корабля
            int groups = parts.Count >= 6 ? 3 : 2;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var r in parts) { float z = ship.InverseTransformPoint(r.bounds.center).z; minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z); }
            var roots = new Wreckage[groups];
            Vector3 center = ship.position;
            for (int g = 0; g < groups; g++)
            {
                var root = new GameObject("Wreck").transform;
                root.SetParent(transform, false);
                root.position = center;
                root.rotation = ship.rotation;
                var mat = new Material(ShaderCache.Lit) { name = "Wreck" };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.13f, 0.12f, 0.12f));
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.5f);
                mat.EnableKeyword("_EMISSION");
                Vector3 axis = ship.forward * ((g - (groups - 1) * 0.5f));
                roots[g] = new Wreckage
                {
                    Root = root, Mat = mat, Size = size,
                    Vel = (axis.normalized * Random.Range(0.6f, 1.4f) + Random.insideUnitSphere * 0.5f) * Mathf.Sqrt(size),
                    AngVel = Random.insideUnitSphere * Random.Range(25f, 70f),
                    Life = Random.Range(7f, 9f),
                    Fires = new[] { Random.insideUnitSphere * 0.3f, Random.insideUnitSphere * 0.3f }
                };
            }
            foreach (var r in parts)
            {
                float z = ship.InverseTransformPoint(r.bounds.center).z;
                int g = Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(minZ, maxZ + 1e-3f, z) * groups), 0, groups - 1);
                var copy = new GameObject("Piece");
                copy.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                copy.transform.localScale = r.transform.lossyScale;
                copy.transform.SetParent(roots[g].Root, true);
                copy.AddComponent<MeshFilter>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                var mr = copy.AddComponent<MeshRenderer>();
                mr.sharedMaterial = roots[g].Mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.enabled = false;
            }
            foreach (var w in roots) _wrecks.Add(w);
        }

        /// <summary>Повреждённый корабль: клуб дыма из точки на корпусе; при тяжёлых повреждениях — языки пламени.</summary>
        public void ShipDamage(Transform ship, Vector3 localPoint, float severity, float size)
        {
            if (ship == null || _active.Count >= MaxActive - 50) return;
            Vector3 p = ship.TransformPoint(localPoint);
            var s = Spawn(Kind.Smoke, p, new Color(0.16f, 0.15f, 0.15f, 0.35f + 0.35f * severity), size * Random.Range(0.25f, 0.4f),
                          Random.Range(1.6f, 2.6f), _smoke[Random.Range(0, _smoke.Length)], true);
            s.Grow = Random.Range(2.5f, 3.5f); s.Rot = Random.Range(0f, 360f); s.RotSpeed = Random.Range(-20f, 20f);
            s.Vel = (Vector3.up * 0.25f + Random.insideUnitSphere * 0.2f); s.Drag = 0.3f;
            if (severity > 0.55f)
            {
                var f = Spawn(Kind.Fire, p, HotWhite, size * Random.Range(0.18f, 0.3f), Random.Range(0.35f, 0.6f), _fire[Random.Range(0, _fire.Length)]);
                f.Col2 = DeepRed; f.Grow = 1.6f; f.Rot = Random.Range(0f, 360f); f.RotSpeed = Random.Range(-90f, 90f);
                f.Follow = ship; f.Local = localPoint;
            }
            if (severity > 0.75f && Random.value < 0.25f)
                SparkStreak(p, Random.onUnitSphere * Random.Range(1.5f, 3.5f), new Color(0.7f, 0.85f, 1f), 0.35f, 0.025f);
        }

        /// <summary>Сварочные искры: короткая вспышка и разлетающиеся росчерки.</summary>
        public void Sparks(Vector3 at, Color col, int count, float size)
        {
            if (_active.Count >= MaxActive) return;
            Spawn(Kind.Flash, at, Color.Lerp(col, Color.white, 0.6f), size * 1.6f, 0.16f, _glow);
            for (int i = 0; i < count; i++)
            {
                Vector3 v = Random.onUnitSphere * Random.Range(1.2f, 3.2f) * size;
                v.y = Mathf.Abs(v.y) * 0.6f;
                SparkStreak(at, v, Color.Lerp(col, new Color(1f, 0.95f, 0.8f), Random.value * 0.6f), Random.Range(0.3f, 0.6f), 0.03f * size);
            }
        }

        /// <summary>Короткая точка-отметка (например, обнаруженный сканером объект).</summary>
        public void Blip(Vector3 at, Color col, float size, float life)
            => Spawn(Kind.Flash, at, col, size, life, _glow);

        // ==================== ПОПАДАНИЯ ====================

        private void ImpactAt(Vector3 at, Vector3 dir, Color col, Impact impact, float size, Transform target,
                              ShieldBubble bubble, bool shieldBreak, float delay, bool beam)
        {
            Vector3 normal = -dir;
            switch (impact)
            {
                case Impact.Shield:
                {
                    if (bubble == null && target != null) bubble = ShieldBubble.For(target, size, ShieldBlue);
                    if (bubble != null)
                    {
                        bubble.Hit(at);
                        if (shieldBreak) bubble.Collapse();
                    }
                    var f = Spawn(Kind.Flash, at, new Color(0.7f, 0.92f, 1f), size * (beam ? 0.7f : 0.5f), 0.25f, _glow);
                    f.Age = -delay;
                    // Разряды по поверхности поля
                    for (int i = 0; i < (shieldBreak ? 14 : 4); i++)
                    {
                        Vector3 t = Vector3.ProjectOnPlane(Random.onUnitSphere, normal).normalized;
                        SparkStreak(at, (t * 2.5f + normal * 0.6f) * Random.Range(0.8f, 1.6f), new Color(0.55f, 0.85f, 1f), Random.Range(0.18f, 0.35f), 0.03f, delay);
                    }
                    if (shieldBreak)
                    {
                        FlashLight(at, ShieldBlue, size * 6f, 3f, 0.5f);
                        var r = Spawn(Kind.Ring, target != null ? target.position : at, new Color(0.5f, 0.85f, 1f, 0.8f), size * 4f, 0.5f, _ring);
                        r.Age = -delay;
                    }
                    break;
                }
                case Impact.Armor:
                {
                    // Броня держит: яркий удар, веер искр-рикошетов, раскалённое пятно
                    var f = Spawn(Kind.Flash, at, HotWhite, size * 0.55f, 0.18f, _glow);
                    f.Age = -delay;
                    int n = beam ? 6 : 10;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 v = Vector3.Reflect(dir, normal + Random.insideUnitSphere * 0.7f).normalized * Random.Range(2.5f, 6f);
                        SparkStreak(at, v, Color.Lerp(HotWhite, FireOrange, Random.value * 0.6f), Random.Range(0.25f, 0.55f), 0.035f, delay);
                    }
                    HotSpot(at, target, size * 0.32f, 2.2f, delay);
                    if (Random.value < 0.5f)
                    {
                        var s = Spawn(Kind.Smoke, at, new Color(0.35f, 0.33f, 0.32f, 0.45f), size * 0.35f, 1.2f, _smoke[Random.Range(0, _smoke.Length)], true);
                        s.Grow = 2.2f; s.Vel = normal * 0.6f; s.Drag = 1f; s.Age = -delay; s.Rot = Random.Range(0f, 360f);
                    }
                    break;
                }
                default:
                {
                    // Корпус пробит: огненный выброс, дым, искры, обломки, пожар на месте пробоины
                    FlashLight(at, FireOrange, size * 4f, 2.2f, 0.35f);
                    var f = Spawn(Kind.Flash, at, HotWhite, size * 0.8f, 0.22f, _glow);
                    f.Age = -delay;
                    for (int i = 0; i < 3; i++)
                    {
                        var fire = Spawn(Kind.Fire, at + normal * size * 0.1f * i, HotWhite, size * Random.Range(0.45f, 0.7f),
                                         Random.Range(0.45f, 0.75f), _fire[Random.Range(0, _fire.Length)]);
                        fire.Col2 = DeepRed; fire.Grow = Random.Range(1.6f, 2.2f); fire.Rot = Random.Range(0f, 360f);
                        fire.RotSpeed = Random.Range(-80f, 80f); fire.Vel = (normal + Random.insideUnitSphere * 0.5f) * Random.Range(0.8f, 1.8f);
                        fire.Drag = 2.5f; fire.Age = -delay - i * 0.03f;
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        var s = Spawn(Kind.Smoke, at, new Color(0.12f, 0.11f, 0.11f, 0.7f), size * Random.Range(0.5f, 0.75f),
                                      Random.Range(1.8f, 2.8f), _smoke[Random.Range(0, _smoke.Length)], true);
                        s.Grow = Random.Range(2.2f, 3f); s.Vel = (normal + Random.insideUnitSphere * 0.4f) * 0.7f; s.Drag = 0.8f;
                        s.Rot = Random.Range(0f, 360f); s.RotSpeed = Random.Range(-15f, 15f); s.Age = -delay - 0.08f;
                    }
                    for (int i = 0; i < 7; i++)
                        SparkStreak(at, (normal + Random.insideUnitSphere * 0.9f).normalized * Random.Range(2f, 5.5f),
                                    Color.Lerp(HotWhite, FireOrange, Random.value), Random.Range(0.3f, 0.7f), 0.035f, delay);
                    if (Random.value < 0.6f)
                        DebrisChunk(at, (normal + Random.insideUnitSphere * 0.6f) * Random.Range(1.5f, 3f), Random.Range(0.05f, 0.11f), Random.Range(1.5f, 2.5f));
                    HotSpot(at, target, size * 0.42f, 3f, delay);
                    if (target != null)
                    {
                        var burn = Spawn(Kind.Fire, at, HotWhite, size * 0.28f, 1.4f, _fire[Random.Range(0, _fire.Length)]);
                        burn.Col2 = DeepRed; burn.Grow = 1.2f; burn.RotSpeed = Random.Range(-120f, 120f);
                        burn.Follow = target; burn.Local = target.InverseTransformPoint(at); burn.Age = -delay;
                    }
                    break;
                }
            }
        }

        /// <summary>Раскалённое пятно на корпусе: белое → оранжевое → тёмно-красное, остывает, держится на корабле.</summary>
        private void HotSpot(Vector3 at, Transform target, float size, float life, float delay)
        {
            var d = Spawn(Kind.Decal, at, HotWhite, size, life, _hot);
            d.Col2 = DeepRed;
            d.Age = -delay;
            if (target != null) { d.Follow = target; d.Local = target.InverseTransformPoint(at); }
        }

        private void SparkStreak(Vector3 at, Vector3 vel, Color col, float life, float width, float delay = 0f)
        {
            if (_active.Count >= MaxActive) return;
            var s = Take(Kind.Spark);
            s.T.position = at;
            s.From = at;
            s.Vel = vel;
            s.Col = col;
            s.Life = life;
            s.Size = width;
            s.Drag = 2.2f;
            s.Age = -delay;
        }

        private void DebrisChunk(Vector3 at, Vector3 vel, float size, float life)
        {
            if (_active.Count >= MaxActive) return;
            var d = Take(Kind.Debris);
            d.T.position = at;
            d.T.rotation = Random.rotation;
            d.T.localScale = new Vector3(size, size * Random.Range(0.3f, 0.8f), size * Random.Range(0.6f, 1.6f));
            d.Size = size;
            d.Vel = vel;
            d.AngVel = Random.insideUnitSphere * Random.Range(180f, 540f);
            d.Life = life;
            d.Drag = 0.25f;
            d.Tr.Clear();
        }

        private void Explode(Vector3 at, Vector3 dir, Color col, Impact impact, float size, Transform target, bool shieldBreak)
        {
            if (impact == Impact.Shield) { ImpactAt(at, dir, col, impact, size * 1.2f, target, null, shieldBreak, 0f, false); return; }
            FlashLight(at, FireOrange, size * 5f, 3f, 0.45f);
            var ring = Spawn(Kind.Ring, at, new Color(1f, 0.7f, 0.4f, 0.8f), size * 1.8f, 0.45f, _ring);
            ring.Flat = true;
            ImpactAt(at, dir, col, Impact.Hull, size * 1.15f, target, null, false, 0f, false);
        }

        // ==================== ПОМОЩНИКИ ====================

        private class FxLight { public Light L; public float Age, Life, Peak; }
        private readonly List<FxLight> _lights = new List<FxLight>();

        /// <summary>Вспышка света — подсвечивает корпуса соседних кораблей.</summary>
        private void FlashLight(Vector3 pos, Color col, float range, float intensity, float life)
        {
            FxLight fl = null;
            foreach (var x in _lights) if (!x.L.enabled) { fl = x; break; }
            if (fl == null)
            {
                if (_lights.Count >= MaxLights) return;
                var go = new GameObject("FxLight");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                fl = new FxLight { L = l };
                _lights.Add(fl);
            }
            fl.L.transform.position = pos;
            fl.L.color = col;
            fl.L.range = range;
            fl.Peak = intensity;
            fl.Age = 0f;
            fl.Life = life;
            fl.L.intensity = intensity;
            fl.L.enabled = true;
        }

        private Fx Spawn(Kind k, Vector3 pos, Color col, float size, float life, Sprite sprite, bool soft = false)
        {
            var f = Take(k, soft);
            f.T.position = pos;
            f.From = pos;
            f.Col = col;
            f.Col2 = col;
            f.Size = size;
            f.Life = life;
            f.Sr.sprite = sprite;
            f.Sr.enabled = false;
            return f;
        }

        /// <summary>Дульная вспышка: шар света и короткий конус пламени по направлению выстрела.</summary>
        private void MuzzleFlash(Vector3 at, Vector3 dir, Color col, float size, Transform shooter)
        {
            var g = Spawn(Kind.Flash, at, Color.Lerp(col, Color.white, 0.55f), size, 0.12f, _glow);
            if (shooter != null) { g.Follow = shooter; g.Local = shooter.InverseTransformPoint(at); }
            var cone = Take(Kind.Beam);
            Bind(cone, at, at + dir * size * 1.6f, null, null);
            cone.Col = Color.Lerp(col, Color.white, 0.4f);
            cone.Size = size * 0.45f;
            cone.EndWidth = 0.05f;
            cone.Life = 0.09f;
        }

        private static Vector3 Bezier(Vector3 a, Vector3 c, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        // ==================== АНИМАЦИЯ ====================

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var fl in _lights)
            {
                if (!fl.L.enabled) continue;
                fl.Age += dt;
                float k = 1f - fl.Age / fl.Life;
                if (k <= 0f) fl.L.enabled = false;
                else fl.L.intensity = fl.Peak * k * k;
            }
            TickWrecks(dt);
            if (_active.Count == 0) return;
            if (_cam == null) _cam = Camera.main;
            Quaternion face = _cam != null ? _cam.transform.rotation : Quaternion.identity;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var f = _active[i];
                f.Age += dt;
                bool waiting = f.Age < 0f;
                if (f.Sr != null) f.Sr.enabled = !waiting;
                if (f.Lr != null) f.Lr.enabled = !waiting;
                if (f.Mr != null) f.Mr.enabled = !waiting;
                if (waiting) continue;

                float t = f.Age / Mathf.Max(0.01f, f.Life);
                if (t >= 1f)
                {
                    _active.RemoveAt(i);
                    if (f.K == Kind.Tracer && f.Hit != Impact.None)
                        ImpactAt(CurrentTo(f), (f.To - f.From).normalized, f.Col, f.Hit, f.HitSize, f.Target, null, f.ShieldBreak, 0f, false);
                    if (f.K == Kind.Missile && f.Hit != Impact.None)
                        Explode(CurrentTo(f), (CurrentTo(f) - f.T.position).normalized, f.Col, f.Hit, f.HitSize, f.Target, f.ShieldBreak);
                    Recycle(f);
                    continue;
                }

                if (f.Follow != null) f.T.position = f.Follow.TransformPoint(f.Local);
                else if (f.K == Kind.Fire || f.K == Kind.Smoke || f.K == Kind.Ember)
                {
                    f.T.position += f.Vel * dt;
                    f.Vel *= Mathf.Exp(-f.Drag * dt);
                }

                switch (f.K)
                {
                    case Kind.Beam:
                    {
                        // Луч: быстрый рост, удержание, угасание; держится за стрелка и цель
                        Vector3 a = CurrentFrom(f), b = CurrentTo(f);
                        f.Lr.SetPosition(0, a);
                        f.Lr.SetPosition(1, b);
                        float grow = Mathf.Clamp01(f.Age / 0.04f);
                        float fade = t < 0.55f ? 1f : 1f - (t - 0.55f) / 0.45f;
                        float pulse = 0.88f + 0.12f * Mathf.Sin(f.Age * 90f);
                        float w = f.Size * grow * (0.4f + 0.6f * fade) * pulse;
                        f.Lr.startWidth = w;
                        f.Lr.endWidth = w * f.EndWidth;
                        var c = f.Col; c.a = fade * fade;
                        f.Lr.startColor = c;
                        f.Lr.endColor = new Color(c.r, c.g, c.b, c.a * 0.85f);
                        break;
                    }
                    case Kind.Filament:
                    {
                        // Нити разряда: дрожат вокруг оси луча, пересобираются каждые несколько кадров
                        Vector3 a = CurrentFrom(f), b = CurrentTo(f);
                        Vector3 axis = b - a;
                        Vector3 side = Vector3.Cross(axis.normalized, Vector3.up);
                        if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
                        Vector3 up = Vector3.Cross(side, axis.normalized);
                        int n = f.Lr.positionCount;
                        float jitter = 0.09f;
                        for (int p = 0; p < n; p++)
                        {
                            float s = p / (float)(n - 1);
                            float env = Mathf.Sin(s * Mathf.PI);
                            Vector3 off = (side * Mathf.Sin(f.Age * 60f + p * 1.9f + i) + up * Mathf.Cos(f.Age * 47f + p * 2.7f + i)) * jitter * env;
                            f.Lr.SetPosition(p, a + axis * s + off);
                        }
                        float fade = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
                        f.Lr.startWidth = f.Lr.endWidth = f.Size;
                        var c = f.Col; c.a = fade * (0.6f + 0.4f * Mathf.Sin(f.Age * 70f + i));
                        f.Lr.startColor = f.Lr.endColor = c;
                        break;
                    }
                    case Kind.Tracer:
                    {
                        Vector3 to = CurrentTo(f);
                        Vector3 dir = to - f.From;
                        float e = t;
                        Vector3 head = f.From + dir * e;
                        Vector3 tail = head - dir.normalized * Mathf.Min(1.1f, dir.magnitude * e);
                        f.Lr.SetPosition(0, tail);
                        f.Lr.SetPosition(1, head);
                        f.Lr.startWidth = f.Size * 0.3f;
                        f.Lr.endWidth = f.Size;
                        f.Lr.startColor = new Color(f.Col.r, f.Col.g * 0.7f, f.Col.b * 0.4f, 0f);
                        f.Lr.endColor = Color.Lerp(f.Col, Color.white, 0.5f);
                        break;
                    }
                    case Kind.Missile:
                    {
                        // Ракета ведёт цель: конец дуги — текущее положение цели
                        float e = t * t * (3f - 2f * t) * 0.35f + t * 0.65f;
                        Vector3 to = CurrentTo(f);
                        f.T.position = Bezier(f.From, f.Ctrl, to, e);
                        f.T.rotation = face;
                        f.T.localScale = Vector3.one * f.Size * (0.8f + 0.25f * Mathf.Sin(f.Age * 47f));
                        f.Sr.color = Color.Lerp(f.Col, Color.white, 0.3f);
                        // Дымный шлейф за двигателем
                        if ((f.SmokeTimer -= dt) <= 0f && _active.Count < MaxActive - 60)
                        {
                            f.SmokeTimer = 0.045f;
                            var s = Spawn(Kind.Smoke, f.T.position, new Color(0.55f, 0.53f, 0.52f, 0.35f), 0.18f, Random.Range(0.8f, 1.3f),
                                          _smoke[Random.Range(0, _smoke.Length)], true);
                            s.Grow = 3.2f; s.Rot = Random.Range(0f, 360f); s.RotSpeed = Random.Range(-30f, 30f);
                            s.Vel = Random.insideUnitSphere * 0.15f; s.Drag = 1f;
                            s.Sr.enabled = true;
                        }
                        break;
                    }
                    case Kind.Flash:
                    {
                        float k = 1f - t;
                        f.T.rotation = face;
                        f.T.localScale = Vector3.one * f.Size * (0.45f + 0.55f * (1f - k * k));
                        var c = f.Col; c.a = k * k;
                        f.Sr.color = c;
                        break;
                    }
                    case Kind.Ring:
                    {
                        float k = 1f - t;
                        f.T.rotation = f.Flat ? Quaternion.Euler(90f, 0f, 0f) : face;
                        f.T.localScale = Vector3.one * f.Size * (1f - k * k * k);
                        var c = f.Col; c.a *= k * k;
                        f.Sr.color = c;
                        break;
                    }
                    case Kind.Ember:
                    {
                        f.T.rotation = face;
                        float k = 1f - t;
                        f.T.localScale = Vector3.one * f.Size * (0.4f + 0.6f * k);
                        var c = f.Col; c.a = k * (0.7f + 0.3f * Mathf.Sin(f.Age * 25f + i));
                        f.Sr.color = c;
                        break;
                    }
                    case Kind.Fire:
                    {
                        // Пламя: белое ядро → оранжевый → тёмно-красный, клуб растёт и вращается, угасает
                        f.Rot += f.RotSpeed * dt;
                        f.T.rotation = face * Quaternion.Euler(0f, 0f, f.Rot);
                        float ease = 1f - (1f - t) * (1f - t);
                        f.T.localScale = Vector3.one * f.Size * Mathf.Lerp(0.55f, f.Grow, ease);
                        Color c = t < 0.25f ? Color.Lerp(f.Col, FireOrange, t / 0.25f) : Color.Lerp(FireOrange, f.Col2, (t - 0.25f) / 0.75f);
                        c.a = Mathf.Pow(1f - t, 1.3f) * Mathf.Clamp01(f.Age / 0.04f);
                        f.Sr.color = c;
                        break;
                    }
                    case Kind.Smoke:
                    {
                        f.Rot += f.RotSpeed * dt;
                        f.T.rotation = face * Quaternion.Euler(0f, 0f, f.Rot);
                        float ease = 1f - (1f - t) * (1f - t);
                        f.T.localScale = Vector3.one * f.Size * Mathf.Lerp(1f, f.Grow, ease);
                        var c = f.Col;
                        c.a = f.Col.a * Mathf.Clamp01(t / 0.08f) * Mathf.Pow(1f - t, 1.6f);
                        f.Sr.color = c;
                        break;
                    }
                    case Kind.Spark:
                    {
                        // Искра-росчерк: хвост вытянут по скорости, тормозит и гаснет
                        f.T.position += f.Vel * dt;
                        f.Vel *= Mathf.Exp(-f.Drag * dt);
                        Vector3 head = f.T.position;
                        Vector3 tail = head - f.Vel * 0.05f;
                        f.Lr.SetPosition(0, tail);
                        f.Lr.SetPosition(1, head);
                        float k = 1f - t;
                        f.Lr.startWidth = f.Size * 0.3f * k;
                        f.Lr.endWidth = f.Size * k;
                        Color c = Color.Lerp(f.Col, FireOrange, t);
                        f.Lr.startColor = new Color(c.r, c.g, c.b, 0f);
                        f.Lr.endColor = new Color(c.r, c.g, c.b, k);
                        break;
                    }
                    case Kind.Debris:
                    {
                        f.T.position += f.Vel * dt;
                        f.Vel *= Mathf.Exp(-f.Drag * dt);
                        f.T.Rotate(f.AngVel * dt, Space.World);
                        float shrink = t > 0.8f ? 1f - (t - 0.8f) / 0.2f : 1f;
                        f.T.localScale = f.T.localScale.normalized * f.Size * 1.7f * Mathf.Max(0.01f, shrink);
                        f.Tr.emitting = t < 0.7f;
                        break;
                    }
                    case Kind.Decal:
                    {
                        // Пятно остывает: белый → оранжевый → тёмно-красный, слегка пульсирует, гаснет
                        f.T.rotation = face;
                        Color c = t < 0.2f ? Color.Lerp(f.Col, FireOrange, t / 0.2f) : Color.Lerp(FireOrange, f.Col2, (t - 0.2f) / 0.8f);
                        c.a = Mathf.Pow(1f - t, 1.2f) * (0.85f + 0.15f * Mathf.Sin(f.Age * 13f + i));
                        f.T.localScale = Vector3.one * f.Size * (1f - 0.25f * t);
                        f.Sr.color = c;
                        break;
                    }
                }
            }
        }

        /// <summary>Обломки корабля: летят, кувыркаются, раскалённые края остывают, горят и дымят, в конце тают.</summary>
        private void TickWrecks(float dt)
        {
            for (int i = _wrecks.Count - 1; i >= 0; i--)
            {
                var w = _wrecks[i];
                if (w.Root == null) { _wrecks.RemoveAt(i); continue; }
                w.Age += dt;
                float t = w.Age / w.Life;
                if (t >= 1f)
                {
                    Destroy(w.Root.gameObject);
                    if (w.Mat != null) Destroy(w.Mat);
                    _wrecks.RemoveAt(i);
                    continue;
                }
                w.Root.position += w.Vel * dt;
                w.Vel *= Mathf.Exp(-0.25f * dt);
                w.Root.Rotate(w.AngVel * dt, Space.World);
                float heat = Mathf.Clamp01(1f - w.Age / 3.5f);
                if (w.Mat != null && w.Mat.HasProperty("_EmissionColor"))
                    w.Mat.SetColor("_EmissionColor", new Color(1f, 0.32f, 0.06f) * (heat * heat * 2.2f * (0.85f + 0.15f * Mathf.Sin(w.Age * 9f + i))));
                if (t > 0.85f) w.Root.localScale = Vector3.one * Mathf.Max(0.01f, 1f - (t - 0.85f) / 0.15f);

                if ((w.SmokeTimer -= dt) <= 0f && _active.Count < MaxActive - 80 && t < 0.75f)
                {
                    w.SmokeTimer = Mathf.Lerp(0.08f, 0.35f, t);
                    foreach (var p in w.Fires)
                    {
                        Vector3 at = w.Root.TransformPoint(p * w.Size);
                        var s = Spawn(Kind.Smoke, at, new Color(0.12f, 0.11f, 0.11f, 0.55f * (1f - t)), w.Size * 0.35f,
                                      Random.Range(1.4f, 2.2f), _smoke[Random.Range(0, _smoke.Length)], true);
                        s.Grow = 3f; s.Vel = Random.insideUnitSphere * 0.25f; s.Drag = 0.5f;
                        s.Rot = Random.Range(0f, 360f); s.RotSpeed = Random.Range(-20f, 20f);
                        if (heat > 0.2f)
                        {
                            var f = Spawn(Kind.Fire, at, HotWhite, w.Size * 0.3f * heat, Random.Range(0.3f, 0.5f), _fire[Random.Range(0, _fire.Length)]);
                            f.Col2 = DeepRed; f.Grow = 1.5f; f.Rot = Random.Range(0f, 360f); f.RotSpeed = Random.Range(-90f, 90f);
                            f.Follow = w.Root; f.Local = p * w.Size;
                        }
                    }
                    if (heat > 0.3f && Random.value < 0.3f)
                        SparkStreak(w.Root.position, Random.onUnitSphere * Random.Range(1.5f, 3f), HotWhite, 0.4f, 0.03f);
                }
            }
        }
    }
}
