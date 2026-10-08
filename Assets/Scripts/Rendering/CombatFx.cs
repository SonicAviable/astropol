using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Эффекты космического боя. Каждое оружие выглядит по-своему:
    ///   • энергия — луч: яркое ядро и цветное свечение, вспышка у дула;
    ///   • кинетика — очередь трассеров, долетающих до цели;
    ///   • ракеты — снаряд со шлейфом летит по дуге и взрывается с ударной волной;
    ///   • попадание в щит — вспышка пузыря щита; в броню и корпус — искры и огненный всплеск;
    ///   • гибель корабля — ослепительная вспышка, ударная волна, догорающие обломки и вторичные взрывы.
    /// Все объекты берутся из пулов, анимация — по реальному времени (на любой скорости игры одинаково красиво).
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

        public enum Impact { None, Shield, Hull }
        private enum Kind { Beam, Tracer, Missile, Flash, Ring, Shield, Ember }

        private class Fx
        {
            public Kind K;
            public GameObject Go;
            public Transform T;
            public SpriteRenderer Sr;
            public LineRenderer Lr;
            public TrailRenderer Tr;
            public float Age, Life, Size, Spin;
            public Vector3 From, To, Ctrl, Vel;
            public Color Col;
            public Impact Hit;
            public float HitSize;
            public bool Flat;
        }

        private const int MaxActive = 600;
        private const float MaxCameraDistance = 190f;
        private const int MaxLights = 6;

        private readonly List<Fx> _active = new List<Fx>();
        private readonly Stack<Fx> _sprites = new Stack<Fx>(), _lines = new Stack<Fx>(), _missiles = new Stack<Fx>();
        private Material _addMat, _lineMat;
        private Sprite _glow, _ring, _shield;
        private Camera _cam;

        private void Awake()
        {
            var shader = Resources.Load<Shader>("Shaders/CombatAdditive") ?? ShaderCache.Sprite;
            _glow = MakeSprite(64, r => Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));
            _ring = MakeSprite(128, r => Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.07f, 2f)));
            _shield = MakeSprite(128, r => r > 1f ? 0f : 0.12f + 0.88f * Mathf.Pow(r, 7f) * (1f - Mathf.Clamp01((r - 0.97f) / 0.03f)));
            _addMat = new Material(shader) { name = "CombatAdditive", mainTexture = _glow.texture };

            var lineTex = new Texture2D(64, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 16; y++)
            {
                float v = (y + 0.5f) / 16f * 2f - 1f;
                float a = Mathf.Exp(-v * v * 3.2f);
                for (int x = 0; x < 64; x++) lineTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            lineTex.Apply();
            _lineMat = new Material(shader) { name = "CombatAdditiveLine", mainTexture = lineTex };
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

        // ==================== ПУЛЫ ====================

        private Fx Take(Kind k)
        {
            Stack<Fx> pool = k == Kind.Beam || k == Kind.Tracer ? _lines : k == Kind.Missile ? _missiles : _sprites;
            Fx f = pool.Count > 0 ? pool.Pop() : Create(pool == _lines ? 1 : pool == _missiles ? 2 : 0);
            f.K = k;
            f.Age = 0f;
            f.Hit = Impact.None;
            f.Flat = false;
            f.Spin = 0f;
            f.Vel = Vector3.zero;
            f.Go.SetActive(true);
            if (f.Tr != null) f.Tr.Clear();
            _active.Add(f);
            return f;
        }

        private Fx Create(int type)
        {
            var go = new GameObject(type == 1 ? "FxLine" : type == 2 ? "FxMissile" : "FxSprite");
            go.transform.SetParent(transform, false);
            var f = new Fx { Go = go, T = go.transform };
            if (type == 1)
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
            else
            {
                f.Sr = go.AddComponent<SpriteRenderer>();
                f.Sr.sharedMaterial = _addMat;
                f.Sr.sprite = _glow;
                f.Sr.sortingOrder = 60;
                if (type == 2)
                {
                    f.Tr = go.AddComponent<TrailRenderer>();
                    f.Tr.sharedMaterial = _lineMat;
                    f.Tr.time = 0.45f;
                    f.Tr.minVertexDistance = 0.05f;
                    f.Tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.14f), new Keyframe(1f, 0f));
                    f.Tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    f.Tr.receiveShadows = false;
                }
            }
            return f;
        }

        private void Recycle(Fx f)
        {
            f.Go.SetActive(false);
            (f.Lr != null ? _lines : f.Tr != null ? _missiles : _sprites).Push(f);
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

        /// <summary>Выстрел: from — дуло, to — центр цели, hitSize — размер цели (для вспышки щита).</summary>
        public void Fire(WeaponDamageType weapon, Vector3 from, Vector3 to, Color col, bool hit, Impact impact, float hitSize)
        {
            // Промах — снаряд проходит мимо цели и уходит дальше
            if (!hit)
            {
                Vector3 off = Random.onUnitSphere * hitSize * 0.9f;
                off.y *= 0.3f;
                to = to + off + (to - from).normalized * hitSize * 1.5f;
                impact = Impact.None;
            }

            MuzzleFlash(from, col, weapon == WeaponDamageType.Explosive ? 0.35f : 0.45f);
            switch (weapon)
            {
                case WeaponDamageType.Energy:
                {
                    var glow = Take(Kind.Beam);
                    glow.From = from; glow.To = to; glow.Col = col; glow.Size = 0.36f; glow.Life = 0.34f;
                    var core = Take(Kind.Beam);
                    core.From = from; core.To = to; core.Col = Color.Lerp(col, Color.white, 0.75f); core.Size = 0.08f; core.Life = 0.24f;
                    if (impact != Impact.None) ImpactAt(to, col, impact, hitSize, 0f);
                    break;
                }
                case WeaponDamageType.Kinetic:
                {
                    int n = 3;
                    for (int i = 0; i < n; i++)
                    {
                        var tr = Take(Kind.Tracer);
                        tr.From = from + Random.insideUnitSphere * 0.08f;
                        tr.To = to + Random.insideUnitSphere * hitSize * 0.18f;
                        tr.Col = Color.Lerp(col, new Color(1f, 0.9f, 0.6f), 0.5f);
                        tr.Size = 0.07f;
                        tr.Life = 0.22f + Vector3.Distance(from, to) * 0.012f;
                        tr.Age = -i * 0.07f;
                        if (i == n - 1) { tr.Hit = impact; tr.HitSize = hitSize; }
                    }
                    break;
                }
                default:
                {
                    var m = Take(Kind.Missile);
                    Vector3 mid = (from + to) * 0.5f;
                    Vector3 side = Vector3.Cross(Vector3.up, (to - from).normalized);
                    m.From = from;
                    m.To = to;
                    m.Ctrl = mid + side * Random.Range(-2.4f, 2.4f) + Vector3.up * Random.Range(0.6f, 1.8f);
                    m.Col = Color.Lerp(col, new Color(1f, 0.65f, 0.3f), 0.4f);
                    m.Size = 0.32f;
                    m.Life = 0.75f + Vector3.Distance(from, to) * 0.03f;
                    m.Hit = impact;
                    m.HitSize = hitSize;
                    m.Sr.sprite = _glow;
                    m.T.position = from;
                    m.Tr.Clear();
                    m.Tr.startColor = new Color(m.Col.r, m.Col.g, m.Col.b, 0.8f);
                    m.Tr.endColor = new Color(0.5f, 0.5f, 0.55f, 0f);
                    break;
                }
            }
        }

        /// <summary>Гибель корабля: вспышка, ударная волна, догорающие обломки, вторичные взрывы.</summary>
        public void ShipDestroyed(Vector3 pos, float size, Color ownerCol)
        {
            FlashLight(pos, new Color(1f, 0.7f, 0.4f), size * 9f, 7f, 0.9f);
            Spawn(Kind.Flash, pos, new Color(1f, 0.92f, 0.75f), size * 4.2f, 0.55f, _glow);
            Spawn(Kind.Flash, pos, new Color(1f, 0.45f, 0.15f), size * 6f, 1.1f, _glow);
            var ring = Spawn(Kind.Ring, pos, new Color(1f, 0.7f, 0.4f, 0.9f), size * 7f, 0.9f, _ring);
            ring.Flat = true;
            Spawn(Kind.Ring, pos, Color.Lerp(ownerCol, Color.white, 0.4f), size * 4.5f, 0.7f, _ring);
            int embers = Mathf.RoundToInt(10 + size * 5f);
            for (int i = 0; i < embers; i++)
            {
                var e = Spawn(Kind.Ember, pos, Color.Lerp(new Color(1f, 0.55f, 0.2f), new Color(1f, 0.9f, 0.6f), Random.value),
                               Random.Range(0.12f, 0.32f) * Mathf.Sqrt(size), Random.Range(1.6f, 2.8f), _glow);
                e.Vel = Random.onUnitSphere * Random.Range(1.2f, 3.6f) * Mathf.Sqrt(size);
                e.Vel.y *= 0.5f;
            }
            for (int i = 0; i < 3; i++)
            {
                var sec = Spawn(Kind.Flash, pos + Random.insideUnitSphere * size * 0.9f, new Color(1f, 0.6f, 0.25f), size * 1.8f, 0.45f, _glow);
                sec.Age = -Random.Range(0.12f, 0.6f);
            }
        }

        // ==================== ОБЩИЕ РЕСУРСЫ ====================

        /// <summary>Мягкое свечение, тонкое кольцо, аддитивные материалы — для других эффектов (работа кораблей).</summary>
        public Sprite GlowSprite => _glow;
        public Sprite RingSprite => _ring;
        public Material SpriteMaterial => _addMat;
        public Material LineMaterial => _lineMat;

        /// <summary>Сварочные искры: короткая вспышка и разлетающиеся угольки.</summary>
        public void Sparks(Vector3 at, Color col, int count, float size)
        {
            if (_active.Count >= MaxActive) return;
            Spawn(Kind.Flash, at, Color.Lerp(col, Color.white, 0.6f), size * 1.6f, 0.16f, _glow);
            for (int i = 0; i < count; i++)
            {
                var e = Spawn(Kind.Ember, at, Color.Lerp(col, new Color(1f, 0.95f, 0.8f), Random.value * 0.6f),
                              Random.Range(0.04f, 0.09f) * size * 2f, Random.Range(0.3f, 0.65f), _glow);
                e.Vel = Random.onUnitSphere * Random.Range(1.2f, 3.2f) * size;
                e.Vel.y = Mathf.Abs(e.Vel.y) * 0.6f;
            }
        }

        /// <summary>Короткая точка-отметка (например, обнаруженный сканером объект).</summary>
        public void Blip(Vector3 at, Color col, float size, float life)
            => Spawn(Kind.Flash, at, col, size, life, _glow);

        // ==================== ПОМОЩНИКИ ====================

        private class FxLight { public Light L; public float Age, Life, Peak; }
        private readonly List<FxLight> _lights = new List<FxLight>();

        /// <summary>Вспышка света от взрыва — подсвечивает корпуса соседних кораблей.</summary>
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

        private Fx Spawn(Kind k, Vector3 pos, Color col, float size, float life, Sprite sprite)
        {
            var f = Take(k);
            f.T.position = pos;
            f.From = pos;
            f.Col = col;
            f.Size = size;
            f.Life = life;
            f.Sr.sprite = sprite;
            return f;
        }

        private void MuzzleFlash(Vector3 at, Color col, float size)
            => Spawn(Kind.Flash, at, Color.Lerp(col, Color.white, 0.5f), size, 0.12f, _glow);

        private void ImpactAt(Vector3 at, Color col, Impact impact, float size, float delay)
        {
            if (impact == Impact.Shield)
            {
                var s = Spawn(Kind.Shield, at, new Color(0.45f, 0.85f, 1f, 0.9f), size * 1.15f, 0.38f, _shield);
                s.Age = -delay;
                var f = Spawn(Kind.Flash, at + Random.insideUnitSphere * size * 0.35f, new Color(0.6f, 0.9f, 1f), size * 0.45f, 0.18f, _glow);
                f.Age = -delay;
                return;
            }
            var flash = Spawn(Kind.Flash, at + Random.insideUnitSphere * size * 0.25f, new Color(1f, 0.6f, 0.25f), size * 0.55f, 0.28f, _glow);
            flash.Age = -delay;
            for (int i = 0; i < 4; i++)
            {
                var e = Spawn(Kind.Ember, at, new Color(1f, 0.75f, 0.35f), Random.Range(0.06f, 0.12f), Random.Range(0.35f, 0.6f), _glow);
                e.Vel = Random.onUnitSphere * Random.Range(1.5f, 3.5f);
                e.Age = -delay;
            }
        }

        private void Explode(Vector3 at, Color col, Impact impact, float size)
        {
            if (impact == Impact.Shield) { ImpactAt(at, col, impact, size, 0f); return; }
            Spawn(Kind.Flash, at, new Color(1f, 0.75f, 0.4f), size * 0.9f, 0.35f, _glow);
            var ring = Spawn(Kind.Ring, at, new Color(1f, 0.65f, 0.35f, 0.8f), size * 1.6f, 0.45f, _ring);
            ring.Flat = true;
            ImpactAt(at, col, Impact.Hull, size * 0.7f, 0f);
        }

        private static Vector3 Bezier(Vector3 a, Vector3 c, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        // ==================== АНИМАЦИЯ ====================

        private void LateUpdate()
        {
            foreach (var fl in _lights)
            {
                if (!fl.L.enabled) continue;
                fl.Age += Time.unscaledDeltaTime;
                float k = 1f - fl.Age / fl.Life;
                if (k <= 0f) fl.L.enabled = false;
                else fl.L.intensity = fl.Peak * k * k;
            }
            if (_active.Count == 0) return;
            if (_cam == null) _cam = Camera.main;
            float dt = Time.unscaledDeltaTime;
            Quaternion face = _cam != null ? _cam.transform.rotation : Quaternion.identity;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var f = _active[i];
                f.Age += dt;
                bool waiting = f.Age < 0f;
                if (f.Sr != null) f.Sr.enabled = !waiting;
                if (f.Lr != null) f.Lr.enabled = !waiting;
                if (waiting) continue;

                float t = f.Age / Mathf.Max(0.01f, f.Life);
                if (t >= 1f)
                {
                    _active.RemoveAt(i);
                    if (f.K == Kind.Tracer && f.Hit != Impact.None) ImpactAt(f.To, f.Col, f.Hit, f.HitSize, 0f);
                    if (f.K == Kind.Missile && f.Hit != Impact.None) Explode(f.To, f.Col, f.Hit, f.HitSize);
                    Recycle(f);
                    continue;
                }

                switch (f.K)
                {
                    case Kind.Beam:
                    {
                        float k = 1f - t;
                        f.Lr.SetPosition(0, f.From);
                        f.Lr.SetPosition(1, f.To);
                        float w = f.Size * (0.35f + 0.65f * Mathf.Sqrt(k));
                        f.Lr.startWidth = w;
                        f.Lr.endWidth = w * 0.8f;
                        var c = f.Col; c.a = k * k;
                        f.Lr.startColor = c;
                        f.Lr.endColor = new Color(c.r, c.g, c.b, c.a * 0.8f);
                        break;
                    }
                    case Kind.Tracer:
                    {
                        Vector3 dir = f.To - f.From;
                        Vector3 head = f.From + dir * t;
                        Vector3 tail = head - dir.normalized * Mathf.Min(0.8f, dir.magnitude * t);
                        f.Lr.SetPosition(0, tail);
                        f.Lr.SetPosition(1, head);
                        f.Lr.startWidth = f.Size * 0.4f;
                        f.Lr.endWidth = f.Size;
                        f.Lr.startColor = new Color(f.Col.r, f.Col.g, f.Col.b, 0f);
                        f.Lr.endColor = f.Col;
                        break;
                    }
                    case Kind.Missile:
                    {
                        float e = t * t * (3f - 2f * t) * 0.35f + t * 0.65f;
                        f.T.position = Bezier(f.From, f.Ctrl, f.To, e);
                        f.T.rotation = face;
                        f.T.localScale = Vector3.one * f.Size * (0.85f + 0.15f * Mathf.Sin(f.Age * 40f));
                        f.Sr.color = f.Col;
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
                        var c = f.Col; c.a *= k;
                        f.Sr.color = c;
                        break;
                    }
                    case Kind.Shield:
                    {
                        float k = 1f - t;
                        f.T.rotation = face;
                        f.T.localScale = Vector3.one * f.Size * (0.94f + 0.1f * t);
                        var c = f.Col; c.a *= k * (0.75f + 0.25f * Mathf.Sin(f.Age * 60f));
                        f.Sr.color = c;
                        break;
                    }
                    case Kind.Ember:
                    {
                        f.T.position += f.Vel * dt;
                        f.Vel *= Mathf.Exp(-1.6f * dt);
                        f.T.rotation = face;
                        float k = 1f - t;
                        f.T.localScale = Vector3.one * f.Size * (0.4f + 0.6f * k);
                        var c = f.Col; c.a = k * (0.7f + 0.3f * Mathf.Sin(f.Age * 25f + i));
                        f.Sr.color = c;
                        break;
                    }
                }
            }
        }
    }
}
