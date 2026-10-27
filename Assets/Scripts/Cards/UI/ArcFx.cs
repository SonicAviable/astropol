using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Световые ленты карточного боя: одна сетка на весь стол, всё рисуется светом (UIAdditiveSoft).
    ///   • Slash — режущая дуга: горячая сердцевина, цветная кромка и широкое свечение, голова бежит по дуге,
    ///     хвост догоняет и истончается; с головы летят раскалённые искры;
    ///   • Vortex — сходящаяся спираль из нескольких рукавов (удар по Штабу, захват);
    ///   • Trail — шлейф за летящей картой (берёт её положение каждый кадр);
    ///   • Glint — анаморфный блик (горизонтальная «линза»), Spikes — лучи удара, RingWave — тонкая ударная волна;
    ///   • Embers — искры-штрихи (вытянуты по скорости — не «пузырьки»), Ambient — тлеющие искры над столом.
    /// Ширина ленты — по текстуре поперёк (яркая середина, мягкий край), поэтому линии чёткие на любом разрешении.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ArcFx : MaskableGraphic
    {
        public float Speed = 1f;
        /// <summary>Тлеющие искры над столом.</summary>
        public bool Ambient;
        /// <summary>Где рождаются тлеющие искры (в координатах слоя).</summary>
        public Rect AmbientArea = new Rect(-900, -500, 1800, 1000);
        private float _ambientAcc;
        private bool _drawn;

        private const int MaxStreaks = 700;

        // ================================================================ текстура

        private static Texture2D s_tex;

        /// <summary>Профиль ленты поперёк: тонкая яркая сердцевина и мягкое свечение к краям.</summary>
        private static Texture2D Tex
        {
            get
            {
                if (s_tex != null) return s_tex;
                const int H = 64, W = 4;
                s_tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "ArcFxRibbon", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                {
                    float d = Mathf.Abs((y + 0.5f) / H - 0.5f) * 2f;   // 0 — середина, 1 — край
                    float core = Mathf.Exp(-(d / 0.26f) * (d / 0.26f));
                    float glow = Mathf.Exp(-(d / 0.62f) * (d / 0.62f)) * 0.5f;
                    float a = Mathf.Clamp01(core + glow) * Mathf.Clamp01((1f - d) * 6f);
                    byte b = (byte)Mathf.RoundToInt(a * 255f);
                    for (int x = 0; x < W; x++) px[y * W + x] = new Color32(255, 255, 255, b);
                }
                s_tex.SetPixels32(px);
                s_tex.Apply(false, true);
                return s_tex;
            }
        }

        public override Texture mainTexture => Tex;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            var m = UIFx.GlowMat;
            if (m != null) material = m;
        }

        // ================================================================ эффекты

        private abstract class Fx
        {
            public float Age, Dur;
            public virtual bool Done => Age >= Dur;
            public abstract void Tick(ArcFx f, float dt);
            public abstract void Build(ArcFx f, VertexHelper vh);
        }

        private sealed class Streak
        {
            public Vector2 Pos, Vel;
            public float Age, Life, Len, W, Drag, Grav, Flick, FlickPhase;
            public Color C0, C1;
            public bool Alive;
        }

        private readonly List<Fx> _fx = new List<Fx>();
        private readonly List<Streak> _streaks = new List<Streak>();

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);
        private static float EaseInOut(float t) => t * t * (3f - 2f * t);

        // ---- Режущая дуга

        private sealed class SlashFx : Fx
        {
            public Vector2 C;
            public float R, A0, A1, W, Squash, Tilt;
            public Color Hot, Mid, Out;
            public bool Embers;
            private float _emit;

            private float HeadT => EaseOut(Mathf.Clamp01(Age / (Dur * 0.42f)));
            private float TailT => EaseInOut(Mathf.Clamp01((Age - Dur * 0.1f) / (Dur * 0.62f)));
            private float Fade => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Dur * 0.55f, Dur, Age));

            public Vector2 Point(float angDeg, float radius)
            {
                float a = angDeg * Mathf.Deg2Rad;
                var p = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * Squash);
                float t = Tilt * Mathf.Deg2Rad, c = Mathf.Cos(t), s = Mathf.Sin(t);
                return C + new Vector2(p.x * c - p.y * s, p.x * s + p.y * c);
            }

            public override void Tick(ArcFx f, float dt)
            {
                Age += dt;
                if (!Embers || Age > Dur * 0.45f) return;
                // С головы дуги сыплются раскалённые искры — по касательной и наружу
                _emit += dt * 140f;
                float head = Mathf.Lerp(A0, A1, HeadT);
                var p = Point(head, R);
                var tan = (Point(head + Mathf.Sign(A1 - A0) * 2f, R) - p).normalized;
                var outward = (p - C).normalized;
                while (_emit >= 1f)
                {
                    _emit -= 1f;
                    var v = tan * Random.Range(250f, 700f) + outward * Random.Range(-60f, 260f) + Random.insideUnitCircle * 80f;
                    f.AddStreak(p + Random.insideUnitCircle * W * 0.3f, v, Random.Range(0.35f, 0.8f), Random.Range(10f, 26f), Random.Range(2f, 3.6f),
                        Color.Lerp(Hot, Mid, Random.value * 0.6f), new Color(Out.r, Out.g * 0.6f, Out.b * 0.4f, 0f), 2.2f, 260f);
                }
            }

            public override void Build(ArcFx f, VertexHelper vh)
            {
                float fade = Fade;
                if (fade <= 0.001f) return;
                float head = HeadT, tail = TailT;
                if (head - tail < 0.004f) return;
                Layer(f, vh, R, W * 2.6f, new Color(Out.r, Out.g, Out.b, Out.a * 0.38f * fade), head, tail);
                Layer(f, vh, R, W, new Color(Mid.r, Mid.g, Mid.b, Mid.a * fade), head, tail);
                Layer(f, vh, R, W * 0.34f, new Color(Hot.r, Hot.g, Hot.b, Hot.a * fade), head, tail);
                // Отголоски: тонкие дуги чуть внутри и снаружи, с запаздыванием — у удара появляется «объём»
                Layer(f, vh, R * 0.9f, W * 0.22f, new Color(Mid.r, Mid.g, Mid.b, 0.75f * fade), Mathf.Max(0f, head - 0.08f), Mathf.Max(0f, tail - 0.04f));
                Layer(f, vh, R * 1.09f, W * 0.16f, new Color(Hot.r, Hot.g, Hot.b, 0.55f * fade), Mathf.Max(0f, head - 0.14f), Mathf.Max(0f, tail - 0.02f));
                // Языки пламени: мерцающие пряди вдоль дуги, толщина «дышит» — дуга горит, а не просто светится
                for (int k = 0; k < 4; k++)
                    Layer(f, vh, R * (0.95f + 0.035f * k), W * 0.24f, new Color(Mathf.Lerp(Mid.r, Hot.r, 0.5f), Mathf.Lerp(Mid.g, Hot.g, 0.5f), Mathf.Lerp(Mid.b, Hot.b, 0.5f), 0.7f * fade),
                          Mathf.Max(0f, head - 0.03f * k), Mathf.Min(head, tail + 0.06f * k), 1f + k * 1.7f);
            }

            private void Layer(ArcFx f, VertexHelper vh, float radius, float width, Color c, float head, float tail, float flicker = 0f)
            {
                if (head - tail < 0.004f || c.a <= 0.002f) return;
                const int N = 30;
                f.Begin();
                for (int i = 0; i < N; i++)
                {
                    float t = i / (N - 1f);
                    float ang = Mathf.Lerp(Mathf.Lerp(A0, A1, tail), Mathf.Lerp(A0, A1, head), t);
                    // Хвост тонкий и тусклый, к голове — толще и ярче, на самом острие снова сходит на нет
                    float prof = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Lerp(0.02f, 0.96f, t)), 0.6f) * Mathf.Lerp(0.3f, 1f, t);
                    if (flicker > 0f) prof *= 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(t * 17f + flicker * 2.3f + Age * 28f) * Mathf.Sin(t * 7f - flicker + Age * 11f));
                    f.Add(Point(ang, radius), width * prof, new Color(c.r, c.g, c.b, c.a * Mathf.Lerp(0.25f, 1f, t)));
                }
                f.Strip(vh, false);
            }
        }

        // ---- Вихрь

        private sealed class VortexFx : Fx
        {
            public Vector2 C;
            public float R, W, Spin;
            public int Arms;
            public Color Col;
            private float _emit;

            public override void Tick(ArcFx f, float dt)
            {
                Age += dt;
                if (Age > Dur * 0.7f) return;
                _emit += dt * 60f;
                while (_emit >= 1f)
                {
                    _emit -= 1f;
                    float a = Random.value * Mathf.PI * 2f;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    var p = C + dir * R * Random.Range(0.8f, 1.15f);
                    var tan = new Vector2(-dir.y, dir.x) * Mathf.Sign(Spin);
                    f.AddStreak(p, tan * Random.Range(200f, 380f) - dir * Random.Range(150f, 320f), Random.Range(0.4f, 0.7f), Random.Range(12f, 22f), Random.Range(2f, 3.2f),
                        Color.Lerp(Col, Color.white, 0.5f), new Color(Col.r, Col.g, Col.b, 0f), 1.5f, 0f);
                }
            }

            public override void Build(ArcFx f, VertexHelper vh)
            {
                float t0 = Mathf.Clamp01(Age / Dur);
                float grow = EaseOut(Mathf.Clamp01(Age / (Dur * 0.25f)));
                float collapse = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, t0));
                float fade = (1f - collapse * 0.9f) * grow;
                if (fade <= 0.002f) return;
                for (int arm = 0; arm < Arms; arm++)
                {
                    float rot = Spin * Age + arm * 360f / Arms;
                    for (int layer = 0; layer < 2; layer++)
                    {
                        const int N = 28;
                        f.Begin();
                        for (int i = 0; i < N; i++)
                        {
                            float t = i / (N - 1f);
                            float ang = (rot + t * 290f * Mathf.Sign(Spin)) * Mathf.Deg2Rad;
                            float r = R * (0.12f + 0.88f * t) * grow * Mathf.Lerp(1f, 0.15f, collapse);
                            float prof = Mathf.Pow(Mathf.Sin(Mathf.PI * t), 0.55f) * (0.25f + 0.75f * t);
                            var col = layer == 0 ? new Color(Col.r, Col.g, Col.b, 0.42f * fade) : new Color(Mathf.Lerp(Col.r, 1f, 0.55f), Mathf.Lerp(Col.g, 1f, 0.55f), Mathf.Lerp(Col.b, 1f, 0.55f), fade);
                            f.Add(C + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r, (layer == 0 ? W * 2.6f : W * 0.5f) * prof, col);
                        }
                        f.Strip(vh, false);
                    }
                }
            }
        }

        // ---- Шлейф за картой

        private sealed class TrailFx : Fx
        {
            public RectTransform Target;
            public Color Col;
            public float W, Life;
            public readonly List<(Vector2 p, float t)> H = new List<(Vector2, float)>();
            private bool _lost;
            private float _emit;

            public override bool Done => _lost && H.Count == 0 || Age > 8f;

            public override void Tick(ArcFx f, float dt)
            {
                Age += dt;
                if (!_lost && (Target == null || !Target.gameObject.activeInHierarchy)) _lost = true;
                if (!_lost)
                {
                    var p = f.ToLocal(Target);
                    if (H.Count == 0 || (H[H.Count - 1].p - p).sqrMagnitude > 4f) H.Add((p, Age));
                    else H[H.Count - 1] = (p, Age);
                    // Пока карта летит быстро — с неё срываются искры
                    if (H.Count >= 2)
                    {
                        var v = (H[H.Count - 1].p - H[H.Count - 2].p) / Mathf.Max(0.001f, H[H.Count - 1].t - H[H.Count - 2].t);
                        if (v.sqrMagnitude > 250000f)
                        {
                            _emit += dt * 70f;
                            while (_emit >= 1f)
                            {
                                _emit -= 1f;
                                f.AddStreak(p + Random.insideUnitCircle * W * 0.6f, -v * Random.Range(0.08f, 0.25f) + Random.insideUnitCircle * 120f, Random.Range(0.3f, 0.6f),
                                    Random.Range(8f, 16f), Random.Range(1.8f, 3f), Color.Lerp(Col, Color.white, 0.6f), new Color(Col.r, Col.g, Col.b, 0f), 2f, 0f);
                            }
                        }
                    }
                }
                while (H.Count > 0 && Age - H[0].t > Life) H.RemoveAt(0);
                if (_lost && H.Count > 0 && Age - H[H.Count - 1].t > Life) H.Clear();
            }

            public override void Build(ArcFx f, VertexHelper vh)
            {
                if (H.Count < 2) return;
                for (int layer = 0; layer < 2; layer++)
                {
                    f.Begin();
                    for (int i = 0; i < H.Count; i++)
                    {
                        float k = 1f - Mathf.Clamp01((Age - H[i].t) / Life);
                        float w = (layer == 0 ? W * 1.9f : W * 0.45f) * Mathf.Pow(k, 1.4f);
                        var c = layer == 0 ? new Color(Col.r, Col.g, Col.b, 0.38f * k) : new Color(Mathf.Lerp(Col.r, 1f, 0.6f), Mathf.Lerp(Col.g, 1f, 0.6f), Mathf.Lerp(Col.b, 1f, 0.6f), k);
                        f.Add(H[i].p, w, c);
                    }
                    f.Strip(vh, false);
                }
            }
        }

        // ---- Блик линзы, лучи удара, ударная волна

        private sealed class GlintFx : Fx
        {
            public Vector2 C;
            public float Len, W, Ang;
            public Color Col;

            public override void Tick(ArcFx f, float dt) => Age += dt;

            public override void Build(ArcFx f, VertexHelper vh)
            {
                float t = Mathf.Clamp01(Age / Dur);
                float len = Len * EaseOut(Mathf.Clamp01(t * 3f)) * (1f + t * 0.3f);
                float a = 1f - Mathf.SmoothStep(0f, 1f, t);
                var dir = new Vector2(Mathf.Cos(Ang * Mathf.Deg2Rad), Mathf.Sin(Ang * Mathf.Deg2Rad));
                for (int layer = 0; layer < 2; layer++)
                {
                    f.Begin();
                    for (int i = 0; i <= 8; i++)
                    {
                        float u = i / 8f * 2f - 1f;
                        float prof = Mathf.Pow(1f - Mathf.Abs(u), 1.6f);
                        var c = layer == 0 ? new Color(Col.r, Col.g, Col.b, 0.65f * a) : new Color(1f, 1f, 1f, a);
                        f.Add(C + dir * (u * len * 0.5f), (layer == 0 ? W * 4f : W * 1.4f) * prof, c);
                    }
                    f.Strip(vh, false);
                }
            }
        }

        private sealed class SpikesFx : Fx
        {
            public Vector2 C;
            public float Len, W;
            public Color Col;
            public float[] Angles, Lens;

            public override void Tick(ArcFx f, float dt) => Age += dt;

            public override void Build(ArcFx f, VertexHelper vh)
            {
                float t = Mathf.Clamp01(Age / Dur);
                float a = 1f - t * t;
                for (int i = 0; i < Angles.Length; i++)
                {
                    var dir = new Vector2(Mathf.Cos(Angles[i]), Mathf.Sin(Angles[i]));
                    float outer = Len * Lens[i] * EaseOut(Mathf.Clamp01(t * 2.2f));
                    float inner = outer * Mathf.Lerp(0.1f, 0.75f, t);
                    f.Begin();
                    for (int k = 0; k <= 4; k++)
                    {
                        float u = k / 4f;
                        float prof = Mathf.Sin(Mathf.PI * Mathf.Lerp(0.05f, 0.95f, u)) * (1f - u * 0.6f);
                        f.Add(C + dir * Mathf.Lerp(inner, outer, u), W * 1.5f * prof, new Color(Mathf.Lerp(Col.r, 1f, 1f - u), Mathf.Lerp(Col.g, 1f, 1f - u), Mathf.Lerp(Col.b, 1f, 1f - u), a * (1f - u * 0.5f)));
                    }
                    f.Strip(vh, false);
                }
            }
        }

        private sealed class RingFx : Fx
        {
            public Vector2 C;
            public float R0, R1, W, Squash = 1f;
            public Color Col;

            public override void Tick(ArcFx f, float dt) => Age += dt;

            public override void Build(ArcFx f, VertexHelper vh)
            {
                float t = Mathf.Clamp01(Age / Dur);
                float r = Mathf.Lerp(R0, R1, EaseOut(t));
                float a = (1f - t) * (1f - t);
                float w = W * 1.5f * Mathf.Lerp(1f, 0.35f, t);
                const int N = 56;
                for (int layer = 0; layer < 2; layer++)
                {
                    f.Begin();
                    for (int i = 0; i <= N; i++)
                    {
                        float ang = i / (float)N * Mathf.PI * 2f;
                        var col = layer == 0 ? new Color(Col.r, Col.g, Col.b, Col.a * a * 0.4f) : new Color(Mathf.Lerp(Col.r, 1f, 0.5f), Mathf.Lerp(Col.g, 1f, 0.5f), Mathf.Lerp(Col.b, 1f, 0.5f), Col.a * a);
                        f.Add(C + new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r * Squash), layer == 0 ? w * 3.2f : w, col);
                    }
                    f.Strip(vh, true);
                }
            }
        }

        // ================================================================ API

        /// <summary>Режущая дуга вокруг center от угла a0 к a1 (градусы; направление взмаха — знак разницы).</summary>
        public void Slash(Vector2 center, float radius, float a0, float a1, float width, Color hot, Color mid, Color outer,
                          float dur = 0.55f, float squash = 1f, float tilt = 0f, bool embers = true)
        {
            _fx.Add(new SlashFx { C = center, R = radius, A0 = a0, A1 = a1, W = width, Hot = hot, Mid = mid, Out = outer, Dur = dur, Squash = squash, Tilt = tilt, Embers = embers });
            Wake();
        }

        public void Vortex(Vector2 center, float radius, Color col, float dur = 1f, int arms = 3, float width = 16f, float spin = -420f)
        {
            _fx.Add(new VortexFx { C = center, R = radius, Col = col, Dur = dur, Arms = Mathf.Max(1, arms), W = width, Spin = spin });
            Wake();
        }

        /// <summary>Шлейф за элементом интерфейса, пока он жив (или 8 секунд).</summary>
        public void Trail(RectTransform target, Color col, float width, float life = 0.32f)
        {
            if (target == null) return;
            _fx.Add(new TrailFx { Target = target, Col = col, W = width, Life = life, Dur = 8f });
            Wake();
        }

        public void Glint(Vector2 pos, Color col, float length, float width, float dur = 0.3f, float angle = 0f)
        {
            _fx.Add(new GlintFx { C = pos, Col = col, Len = length, W = width, Dur = dur, Ang = angle });
            Wake();
        }

        /// <summary>Лучи удара: тонкие клинья наружу разной длины.</summary>
        public void Spikes(Vector2 pos, Color col, int count, float length, float width, float dur = 0.28f)
        {
            var fx = new SpikesFx { C = pos, Col = col, Len = length, W = width, Dur = dur, Angles = new float[count], Lens = new float[count] };
            float baseA = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < count; i++)
            {
                fx.Angles[i] = baseA + i * Mathf.PI * 2f / count + Random.Range(-0.25f, 0.25f);
                fx.Lens[i] = Random.Range(0.45f, 1f);
            }
            _fx.Add(fx);
            Wake();
        }

        public void RingWave(Vector2 pos, Color col, float r0, float r1, float width, float dur = 0.4f, float squash = 1f)
        {
            _fx.Add(new RingFx { C = pos, Col = col, R0 = r0, R1 = r1, W = width, Dur = dur, Squash = squash });
            Wake();
        }

        /// <summary>Искры-штрихи из точки: направление dirDeg ± spread/2, скорость, гравитация (вниз — положительная).</summary>
        public void Embers(Vector2 pos, Color hot, Color cool, int n, float speedMin, float speedMax, float dirDeg = 90f, float spread = 360f,
                           float lifeMin = 0.4f, float lifeMax = 0.9f, float gravity = 300f, float width = 2.6f)
        {
            for (int i = 0; i < n; i++)
            {
                float a = (dirDeg + Random.Range(-spread, spread) * 0.5f) * Mathf.Deg2Rad;
                var v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(speedMin, speedMax);
                AddStreak(pos, v, Random.Range(lifeMin, lifeMax), Random.Range(8f, 22f), width * Random.Range(0.7f, 1.3f), hot, cool, 2.4f, gravity);
            }
            Wake();
        }

        public void ClearAll()
        {
            _fx.Clear();
            foreach (var s in _streaks) s.Alive = false;
            SetVerticesDirty();
        }

        // ================================================================ частицы-штрихи

        private void AddStreak(Vector2 pos, Vector2 vel, float life, float len, float w, Color c0, Color c1, float drag, float grav, float flick = 0f)
        {
            Streak s = null;
            foreach (var q in _streaks) if (!q.Alive) { s = q; break; }
            if (s == null)
            {
                if (_streaks.Count >= MaxStreaks) return;
                s = new Streak();
                _streaks.Add(s);
            }
            s.Pos = pos; s.Vel = vel; s.Age = 0f; s.Life = life; s.Len = len; s.W = w; s.Drag = drag; s.Grav = grav;
            s.C0 = c0; s.C1 = c1; s.Flick = flick; s.FlickPhase = Random.value * 10f; s.Alive = true;
        }

        private void Wake() { enabled = true; SetVerticesDirty(); }

        public Vector2 ToLocal(RectTransform rt)
        {
            var screen = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, null, out var local);
            return local;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f) * Speed;
            bool any = false;

            if (Ambient)
            {
                // Тлеющие искры: редкие, медленные, мерцают и гаснут — воздух над полем боя
                _ambientAcc += dt * 9f;
                while (_ambientAcc >= 1f)
                {
                    _ambientAcc -= 1f;
                    var p = new Vector2(Random.Range(AmbientArea.xMin, AmbientArea.xMax), Random.Range(AmbientArea.yMin, AmbientArea.yMax));
                    var v = new Vector2(Random.Range(-12f, 30f), Random.Range(14f, 42f));
                    var hot = Random.value < 0.8f ? new Color(1f, 0.72f, 0.32f, 0.55f) : new Color(0.55f, 0.9f, 1f, 0.45f);
                    AddStreak(p, v, Random.Range(3f, 5.5f), Random.Range(3f, 7f), Random.Range(1.6f, 2.8f), hot, new Color(hot.r, hot.g * 0.5f, hot.b * 0.4f, 0f), 0f, 0f, Random.Range(3f, 9f));
                }
                any = true;
            }

            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                _fx[i].Tick(this, dt);
                if (_fx[i].Done) _fx.RemoveAt(i);
            }
            if (_fx.Count > 0) any = true;

            foreach (var s in _streaks)
            {
                if (!s.Alive) continue;
                s.Age += dt;
                if (s.Age >= s.Life) { s.Alive = false; continue; }
                s.Vel += new Vector2(0f, -s.Grav) * dt;
                s.Vel *= 1f / (1f + s.Drag * dt);
                // Тлеющие искры чуть «плывут» в воздухе
                if (s.Flick > 0f) s.Vel.x += Mathf.Sin((s.Age + s.FlickPhase) * 1.7f) * 8f * dt;
                s.Pos += s.Vel * dt;
                any = true;
            }
            // Последний кадр после того, как всё погасло, тоже перерисовать — иначе на экране застынут хвосты
            if (any || _drawn) SetVerticesDirty();
            _drawn = any;
        }

        // ================================================================ сетка

        private readonly List<Vector2> _p = new List<Vector2>(64);
        private readonly List<float> _w = new List<float>(64);
        private readonly List<Color32> _c = new List<Color32>(64);

        private void Begin() { _p.Clear(); _w.Clear(); _c.Clear(); }
        private void Add(Vector2 p, float w, Color c) { _p.Add(p); _w.Add(w); _c.Add(c); }

        /// <summary>Лента по точкам: поперечник — профиль текстуры, closed — замкнуть (кольцо).</summary>
        private void Strip(VertexHelper vh, bool closed)
        {
            int n = _p.Count;
            if (n < 2 || vh.currentVertCount > 60000) return;
            int start = vh.currentVertCount;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = i > 0 ? _p[i - 1] : (closed ? _p[n - 2] : _p[i]);
                Vector2 next = i < n - 1 ? _p[i + 1] : (closed ? _p[1] : _p[i]);
                var t = next - prev;
                if (t.sqrMagnitude < 1e-6f) t = Vector2.right;
                t.Normalize();
                var nrm = new Vector2(-t.y, t.x) * (_w[i] * 0.5f);
                float u = i / (n - 1f);
                vh.AddVert(_p[i] - nrm, _c[i], new Vector2(u, 0f));
                vh.AddVert(_p[i] + nrm, _c[i], new Vector2(u, 1f));
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = start + i * 2;
                vh.AddTriangle(a, a + 1, a + 3);
                vh.AddTriangle(a, a + 3, a + 2);
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach (var fx in _fx) fx.Build(this, vh);

            // Штрихи: хвост прозрачный, голова яркая; длина растёт со скоростью
            foreach (var s in _streaks)
            {
                if (!s.Alive || vh.currentVertCount > 60000) continue;
                float k = s.Age / s.Life;
                var col = Color.Lerp(s.C0, s.C1, k);
                float a = Mathf.Clamp01(s.Age * 12f) * (1f - k * k);
                if (s.Flick > 0f) a *= 0.55f + 0.45f * Mathf.Sin((s.Age + s.FlickPhase) * s.Flick);
                col.a *= a;
                if (col.a <= 0.003f) continue;
                float sp = s.Vel.magnitude;
                var dir = sp > 1f ? s.Vel / sp : Vector2.up;
                float len = s.Len + sp * 0.035f;
                var nrm = new Vector2(-dir.y, dir.x) * (s.W * 0.5f);
                var head = s.Pos;
                var tail = s.Pos - dir * len;
                var tc = new Color(col.r, col.g, col.b, 0f);
                int st = vh.currentVertCount;
                vh.AddVert(tail - nrm, tc, new Vector2(0f, 0f));
                vh.AddVert(tail + nrm, tc, new Vector2(0f, 1f));
                vh.AddVert(head - nrm, col, new Vector2(1f, 0f));
                vh.AddVert(head + nrm, col, new Vector2(1f, 1f));
                vh.AddTriangle(st, st + 1, st + 3);
                vh.AddTriangle(st, st + 3, st + 2);
            }
        }
    }
}
