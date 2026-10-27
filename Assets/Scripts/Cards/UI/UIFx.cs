using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Rendering;

namespace StellarisClone.Cards
{
    /// <summary>Цвет «крови» по виду: люди — красная, иридийцы — фиолетовая, терраане — зелёная, «Ноль» — хладагент.</summary>
    public enum BloodKind { Red, Violet, Green, Coolant }

    /// <summary>
    /// Эффекты карточного боя прямо в интерфейсе (без камер и ParticleSystem — работает в любом канвасе):
    /// частицы (искры, дым, пыль, брызги), кольца ударной волны, трассеры и лучи, вспышка экрана, тряска стола,
    /// всплывающие числа урона. Всё в пуле картинок, обновляется в Update по unscaledDeltaTime × скорость боя.
    /// </summary>
    public sealed class UIFx : MonoBehaviour
    {
        private sealed class P
        {
            public RectTransform Rt;
            public Image Img;
            public Vector2 Pos, Vel;
            public float Life, Age, Rot, RotVel, Size0, Size1, Drag, Gravity, Stretch;
            public Color C0, C1;
            public bool Alive, Align;
        }

        private sealed class Tween
        {
            public RectTransform Rt;
            public Graphic G;
            public float Age, Dur;
            public Vector2 S0, S1;
            public Color C0, C1;
            public bool Alive;
            public Vector2 P0, P1;
            public bool Move;
        }

        /// <summary>Покадровая анимация из атласа (взрыв, дым, энергия) на RawImage.</summary>
        private sealed class FB
        {
            public RawImage Img;
            public RectTransform Rt;
            public int Cols, Rows, Frames;
            public float Age, Dur, Size0, Size1, Rot, Delay;
            public Vector2 Pos, Vel;
            public Color C;
            public bool Alive;
        }

        private readonly List<FB> _fbs = new List<FB>();
        private RectTransform _layer;
        private RectTransform _shakeTarget;
        private Vector2 _shakeBase;
        private float _shakeAmp, _shakeTime;
        private Image _flash;
        private float _flashA, _flashDecay;
        private readonly List<P> _pool = new List<P>();
        private readonly List<Tween> _tweens = new List<Tween>();
        private readonly List<(Text t, RectTransform rt, Vector2 pos, float age, float life, Color c)> _numbers = new List<(Text, RectTransform, Vector2, float, float, Color)>();
        public float Speed = 1f;

        public RectTransform Layer => _layer;

        public void Init(RectTransform layer, RectTransform shakeTarget)
        {
            _layer = layer;
            _shakeTarget = shakeTarget;
            _shakeBase = shakeTarget != null ? shakeTarget.anchoredPosition : Vector2.zero;
            _flash = LGBuild.Panel(layer, "Flash", new Color(1f, 1f, 1f, 0f));
            _flash.rectTransform.Stretch();
            _flash.raycastTarget = false;
        }

        /// <summary>Перевести позицию элемента интерфейса в координаты слоя эффектов.</summary>
        public Vector2 Local(RectTransform rt, Vector2 offset = default)
        {
            if (rt == null || _layer == null) return Vector2.zero;
            var screen = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screen, null, out var local);
            return local + offset;
        }

        private P Get(Sprite sp)
        {
            P p = null;
            foreach (var q in _pool) if (!q.Alive) { p = q; break; }
            if (p == null)
            {
                if (_pool.Count >= 900) return null;
                var img = LGBuild.Panel(_layer, "P", Color.white);
                img.raycastTarget = false;
                p = new P { Img = img, Rt = img.rectTransform };
                p.Rt.anchorMin = p.Rt.anchorMax = new Vector2(0.5f, 0.5f);
                _pool.Add(p);
            }
            p.Img.sprite = sp;
            p.Img.enabled = true;
            p.Rt.SetAsLastSibling();
            p.Alive = true;
            p.Age = 0f;
            p.Stretch = 1f;
            p.Align = false;
            return p;
        }

        /// <summary>
        /// Россыпь частиц: count штук из pos во все стороны (или в конус dir±spread/2, градусы), скорость, жизнь,
        /// размер от size до sizeEnd, цвет от c к c с нулевой прозрачностью, гравитация вниз (px/с²).
        /// </summary>
        public void Burst(Vector2 pos, int count, Sprite sp, Color c, float speedMin, float speedMax, float lifeMin, float lifeMax,
                          float size, float sizeEnd, float gravity = 0f, float drag = 1.5f, float dir = 90f, float spread = 360f,
                          bool align = false, Color? end = null, float stretch = 1f, bool rotate = true)
        {
            for (int i = 0; i < count; i++)
            {
                var p = Get(sp);
                if (p == null) return;
                float ang = (dir + Random.Range(-spread * 0.5f, spread * 0.5f)) * Mathf.Deg2Rad;
                float spd = Random.Range(speedMin, speedMax);
                p.Pos = pos + Random.insideUnitCircle * 4f;
                p.Vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd;
                p.Life = Random.Range(lifeMin, lifeMax);
                p.Size0 = size * Random.Range(0.75f, 1.25f);
                p.Size1 = sizeEnd * Random.Range(0.75f, 1.25f);
                p.Rot = rotate ? Random.Range(0f, 360f) : 0f;
                p.RotVel = align || !rotate ? 0f : Random.Range(-120f, 120f);
                p.Drag = drag;
                p.Gravity = gravity;
                p.C0 = c;
                p.C1 = end ?? new Color(c.r, c.g, c.b, 0f);
                p.Align = align;
                p.Stretch = stretch;
                Apply(p);
            }
        }

        // ---------------------------------------------------------------- флипбуки

        /// <summary>Покадровый атлас tex (cols×rows, кадры слева направо сверху вниз) в точке pos за dur секунд.</summary>
        public void Flipbook(string tex, int cols, int rows, Vector2 pos, float size, float dur, Color c, float sizeEnd = -1f, float rot = 0f, Vector2 vel = default, float delay = 0f)
        {
            var t = CardSkin.Tex(tex);
            if (t == null || _layer == null) return;
            FB f = null;
            foreach (var q in _fbs) if (!q.Alive) { f = q; break; }
            if (f == null)
            {
                if (_fbs.Count >= 60) return;
                var go = new GameObject("FB", typeof(RectTransform));
                go.transform.SetParent(_layer, false);
                f = new FB { Rt = (RectTransform)go.transform, Img = go.AddComponent<RawImage>() };
                f.Img.raycastTarget = false;
                f.Rt.anchorMin = f.Rt.anchorMax = new Vector2(0.5f, 0.5f);
                _fbs.Add(f);
            }
            f.Img.texture = t;
            f.Img.enabled = delay <= 0f;
            f.Rt.SetAsLastSibling();
            f.Cols = cols; f.Rows = rows; f.Frames = cols * rows;
            f.Age = 0f; f.Dur = dur; f.Delay = delay;
            f.Size0 = size; f.Size1 = sizeEnd > 0f ? sizeEnd : size;
            f.Pos = pos; f.Vel = vel; f.Rot = rot; f.C = c;
            f.Alive = true;
            ApplyFB(f);
        }

        private static void ApplyFB(FB f)
        {
            float t = Mathf.Clamp01(f.Age / Mathf.Max(0.01f, f.Dur));
            int fr = Mathf.Min(f.Frames - 1, Mathf.FloorToInt(t * f.Frames));
            int cx = fr % f.Cols, cy = fr / f.Cols;
            f.Img.uvRect = new Rect(cx / (float)f.Cols, 1f - (cy + 1) / (float)f.Rows, 1f / f.Cols, 1f / f.Rows);
            float s = Mathf.Lerp(f.Size0, f.Size1, t);
            f.Rt.sizeDelta = new Vector2(s, s);
            f.Rt.anchoredPosition = f.Pos;
            f.Rt.localRotation = Quaternion.Euler(0, 0, f.Rot);
            f.Img.color = new Color(f.C.r, f.C.g, f.C.b, f.C.a * (t > 0.85f ? 1f - (t - 0.85f) / 0.15f : 1f));
        }

        // ---------------------------------------------------------------- готовые эффекты

        private static Sprite K(string n) => CardSkin.Spr(n);

        public void Sparks(Vector2 pos, Color c, int n = 18, float power = 1f)
        {
            Burst(pos, n, CardSprites.Spark, c, 180f * power, 520f * power, 0.25f, 0.6f, 22f, 6f, 600f, 2.2f, align: true, stretch: 3.2f);
            Burst(pos, 1, K("k_flare"), new Color(c.r, c.g, c.b, 0.9f), 0f, 0f, 0.18f, 0.22f, 70f * power, 140f * power, 0f, 0f, rotate: false);
        }

        public void Dust(Vector2 pos, float scale = 1f, Color? c = null)
            => Burst(pos, Mathf.RoundToInt(8 * scale) + 1, K(Random.value < 0.5f ? "k_smoke1" : "k_smoke2"), c ?? new Color(0.62f, 0.55f, 0.46f, 0.5f), 40f, 170f * scale, 0.8f, 1.5f, 70f * scale, 170f * scale, -20f, 2.5f, 90f, 200f);

        public void Smoke(Vector2 pos, float scale = 1f, Color? c = null)
        {
            var col = c ?? new Color(0.14f, 0.13f, 0.13f, 0.75f);
            Burst(pos, Mathf.RoundToInt(5 * scale) + 1, K("k_smoke1"), col, 20f, 70f, 1.6f, 2.8f, 80f * scale, 210f * scale, -45f, 1.2f, 90f, 120f);
            Flipbook("fx_smoke", 5, 5, pos + new Vector2(0, 20f * scale), 220f * scale, 1.8f, new Color(col.r * 2.2f, col.g * 2.2f, col.b * 2.2f, col.a), 300f * scale, Random.Range(0f, 360f), new Vector2(0, 30f));
        }

        /// <summary>Взрыв: настоящий огненный флипбук, вспышка, обломки, искры, дым и ударная волна.</summary>
        public void Explosion(Vector2 pos, float scale = 1f)
        {
            Flipbook("fx_explosion", 6, 6, pos, 230f * scale, 1.0f, Color.white, 300f * scale, Random.Range(0f, 360f));
            Burst(pos, 1, K("k_light"), new Color(1f, 0.85f, 0.55f, 1f), 0f, 0f, 0.3f, 0.3f, 120f * scale, 420f * scale, 0f, 0f, end: new Color(1f, 0.4f, 0.1f, 0f), rotate: false);
            Burst(pos, Mathf.RoundToInt(6 * scale) + 2, K("k_fire1"), new Color(1f, 0.6f, 0.2f, 0.9f), 80f, 300f * scale, 0.25f, 0.5f, 60f * scale, 20f, 0f, 3f, end: new Color(0.6f, 0.1f, 0.02f, 0f));
            Burst(pos, Mathf.RoundToInt(8 * scale) + 2, K("k_dirt"), new Color(0.25f, 0.2f, 0.17f, 1f), 250f, 650f * scale, 0.6f, 1.1f, 24f * scale, 14f * scale, 1400f, 0.6f, 90f, 160f);
            Sparks(pos, new Color(1f, 0.8f, 0.4f), Mathf.RoundToInt(16 * scale), scale);
            Smoke(pos, scale);
            Ring(pos, new Color(1f, 0.75f, 0.45f, 0.8f), 30f * scale, 300f * scale, 0.45f);
            Burst(pos, 1, K("k_scorch"), new Color(0.05f, 0.03f, 0.02f, 0.6f), 0f, 0f, 2.5f, 2.5f, 160f * scale, 170f * scale, 0f, 0f);
        }

        /// <summary>Энергетический разряд (щит, ЭМИ, пси): флипбук энергии в цвете c.</summary>
        public void Energy(Vector2 pos, Color c, float scale = 1f)
        {
            Flipbook("fx_energy", 6, 6, pos, 200f * scale, 0.8f, c, 280f * scale, Random.Range(0f, 360f));
            Burst(pos, 1, K("k_star"), new Color(c.r, c.g, c.b, 1f), 0f, 0f, 0.3f, 0.3f, 60f * scale, 260f * scale, 0f, 0f, rotate: false);
        }

        /// <summary>Пуля/снаряд: яркий вытянутый трассер с ореолом летит из a в b ровно за dur.</summary>
        public void Bullet(Vector2 a, Vector2 b, Color c, float dur, float size = 14f)
        {
            var v = (b - a) / Mathf.Max(0.01f, dur);
            var core = Get(CardSprites.Spark);
            if (core != null)
            {
                core.Pos = a; core.Vel = v; core.Life = dur; core.Size0 = core.Size1 = size; core.Stretch = 4.5f; core.Align = true;
                core.Drag = 0f; core.Gravity = 0f; core.RotVel = 0f;
                core.C0 = new Color(Mathf.Lerp(c.r, 1f, 0.6f), Mathf.Lerp(c.g, 1f, 0.6f), Mathf.Lerp(c.b, 1f, 0.6f), 1f); core.C1 = core.C0;
                Apply(core);
            }
            var halo = Get(K("k_light"));
            if (halo != null)
            {
                halo.Pos = a; halo.Vel = v; halo.Life = dur; halo.Size0 = halo.Size1 = size * 3.2f; halo.Align = false; halo.Rot = 0f; halo.RotVel = 0f;
                halo.Drag = 0f; halo.Gravity = 0f; halo.C0 = new Color(c.r, c.g, c.b, 0.85f); halo.C1 = halo.C0;
                Apply(halo);
            }
        }

        /// <summary>Энергетический луч: белое ядро, цветной ореол, мерцание по длине; гаснет за dur.</summary>
        public void Beam(Vector2 a, Vector2 b, Color c, float width, float dur)
        {
            Line(a, b, new Color(c.r, c.g, c.b, 0.55f), width * 3.2f, dur * 1.2f);
            Line(a, b, new Color(c.r, c.g, c.b, 0.95f), width * 1.4f, dur);
            Line(a, b, new Color(1f, 1f, 1f, 1f), width * 0.45f, dur * 0.8f);
            var d = b - a;
            for (int i = 0; i < 5; i++)
            {
                var p = a + d * Random.value;
                Burst(p, 1, K("k_light"), new Color(c.r, c.g, c.b, 0.7f), 0f, 0f, dur, dur * 1.3f, width * 3f, width * 5f, 0f, 0f, rotate: false);
            }
        }

        /// <summary>Попадание: вспышка-звезда, искры по направлению выстрела, ударная волна.</summary>
        public void Impact(Vector2 pos, float dirDeg, Color c, float scale = 1f)
        {
            Burst(pos, 1, K("k_star2"), new Color(Mathf.Lerp(c.r, 1f, 0.5f), Mathf.Lerp(c.g, 1f, 0.5f), Mathf.Lerp(c.b, 1f, 0.5f), 1f), 0f, 0f, 0.14f, 0.16f, 70f * scale, 130f * scale, 0f, 0f, rotate: true);
            Burst(pos, Mathf.RoundToInt(8 * scale) + 2, CardSprites.Spark, c, 200f * scale, 520f * scale, 0.15f, 0.35f, 16f * scale, 4f, 500f, 2f, dirDeg + 180f, 110f, align: true, stretch: 3f);
            Ring(pos, new Color(c.r, c.g, c.b, 0.6f), 10f * scale, 90f * scale, 0.22f);
        }

        /// <summary>Вспышка выстрела у ствола, направленная по углу ang (градусы).</summary>
        public void Muzzle(Vector2 pos, float ang, Color c, float scale = 1f)
        {
            var p = Get(K(Random.value < 0.5f ? "k_muzzle1" : "k_muzzle2"));
            if (p == null) return;
            p.Pos = pos; p.Vel = Vector2.zero; p.Life = 0.09f; p.Size0 = 64f * scale; p.Size1 = 80f * scale;
            p.Rot = ang - 90f; p.RotVel = 0f; p.Drag = 0f; p.Gravity = 0f;
            p.C0 = new Color(Mathf.Lerp(c.r, 1f, 0.5f), Mathf.Lerp(c.g, 1f, 0.5f), Mathf.Lerp(c.b, 1f, 0.5f), 1f);
            p.C1 = new Color(c.r, c.g, c.b, 0f);
            Apply(p);
        }

        /// <summary>Брызги по видам: кровь людей, фиолетовая у Иридии, зелёный сок Тэрра'ан, хладагент и искры «Ноля».</summary>
        public void Blood(Vector2 pos, BloodKind k, float scale = 1f)
        {
            if (!GoreOn)
            {
                Burst(pos, Mathf.RoundToInt(8 * scale), CardSprites.Smoke, new Color(0.5f, 0.48f, 0.45f, 0.5f), 60f, 200f, 0.4f, 0.8f, 26f, 60f, 200f, 2f);
                return;
            }
            Color c = k == BloodKind.Violet ? new Color(0.55f, 0.18f, 0.85f) : k == BloodKind.Green ? new Color(0.35f, 0.85f, 0.25f)
                    : k == BloodKind.Coolant ? new Color(0.85f, 0.95f, 1f) : new Color(0.72f, 0.04f, 0.05f);
            Burst(pos, Mathf.RoundToInt(22 * scale), CardSprites.Drop, c, 140f, 520f * scale, 0.45f, 0.9f, 14f * scale, 8f, 1400f, 1.2f, 90f, 260f, align: true, end: new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 0f), stretch: 1.8f);
            Burst(pos, Mathf.RoundToInt(4 * scale) + 1, K("k_smoke2"), new Color(c.r, c.g, c.b, 0.6f), 20f, 90f, 0.5f, 0.9f, 50f * scale, 120f * scale, 60f, 2f);
            Burst(pos, 1, K("k_magic"), new Color(c.r, c.g, c.b, 0.8f), 0f, 0f, 0.35f, 0.35f, 60f * scale, 140f * scale, 0f, 0f);
            if (k == BloodKind.Coolant) Sparks(pos, new Color(0.7f, 0.9f, 1f), 14, scale);
        }

        public static bool GoreOn => PlayerPrefs.GetInt("set.gore", 1) == 1;

        public void Ring(Vector2 pos, Color c, float from, float to, float dur, Sprite sp = null)
        {
            var p = Get(sp ?? K("ring"));
            if (p == null) return;
            p.Pos = pos; p.Vel = Vector2.zero; p.Life = dur; p.Size0 = from; p.Size1 = to; p.Rot = 0f; p.RotVel = 0f;
            p.Drag = 0f; p.Gravity = 0f; p.C0 = c; p.C1 = new Color(c.r, c.g, c.b, 0f);
            Apply(p);
        }

        /// <summary>Неподвижный светящийся отрезок (лазер-прицел, трассер) на dur секунд.</summary>
        public void Line(Vector2 a, Vector2 b, Color c, float width, float dur, Sprite sp = null)
        {
            var p = Get(sp ?? CardSprites.Beam);
            if (p == null) return;
            var d = b - a;
            p.Pos = (a + b) * 0.5f; p.Vel = Vector2.zero; p.Life = dur;
            p.Size0 = p.Size1 = width; p.Stretch = Mathf.Max(1f, d.magnitude / Mathf.Max(1f, width));
            p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f; p.RotVel = 0f; p.Align = false;
            p.Drag = 0f; p.Gravity = 0f; p.C0 = c; p.C1 = new Color(c.r, c.g, c.b, 0f);
            p.Rt.sizeDelta = new Vector2(width, d.magnitude);
            Apply(p);
        }

        /// <summary>Снаряд: светящееся ядро летит из a в b за dur (с хвостом искр). Вернёт время полёта.</summary>
        public float Projectile(Vector2 a, Vector2 b, Color c, float dur, float size = 18f, bool lob = false)
        {
            var p = Get(K("k_light"));
            if (p == null) return dur;
            p.Pos = a; p.Life = dur; p.Size0 = size * 2.2f; p.Size1 = size * 1.8f; p.C0 = c; p.C1 = c; p.Drag = 0f; p.Rot = 0f; p.RotVel = 0f;
            var v = (b - a) / Mathf.Max(0.01f, dur);
            p.Gravity = 0f;
            if (lob)
            {
                // Навесом: старт с вертикальной скоростью, гравитация возвращает точно в цель
                float g = 2400f;
                v.y += 0.5f * g * dur;
                p.Gravity = g;
            }
            p.Vel = v;
            Apply(p);
            // Хвост: линия-трассер вдоль пути
            if (!lob) Line(a, b, new Color(c.r, c.g, c.b, 0.55f), size * 0.35f, dur * 1.2f);
            return dur;
        }

        public void Flash(Color c, float strength = 0.6f, float decay = 3f)
        {
            if (_flash == null) return;
            _flash.color = new Color(c.r, c.g, c.b, 0f);
            _flashA = Mathf.Max(_flashA, strength);
            _flashDecay = decay;
            _flash.transform.SetAsLastSibling();
        }

        public void Shake(float amp, float dur)
        {
            if (PlayerPrefs.GetInt("set.cardshake", 1) == 0) return;
            _shakeAmp = Mathf.Max(_shakeAmp, amp);
            _shakeTime = Mathf.Max(_shakeTime, dur);
        }

        public void Number(Vector2 pos, string text, Color c, int size = 34, float life = 1.1f)
        {
            var t = CardSkin.TitleText(_layer, text, size, c, TextAnchor.MiddleCenter, 2.5f);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(200, 60);
            rt.anchoredPosition = pos;
            _numbers.Add((t, rt, pos, 0f, life, c));
        }

        /// <summary>Плавно изменить размер и цвет картинки (баннеры, вспышки щита).</summary>
        public void Animate(Graphic g, Vector2 s0, Vector2 s1, Color c0, Color c1, float dur, Vector2? move = null)
        {
            if (g == null) return;
            var tw = new Tween { Rt = g.rectTransform, G = g, Dur = dur, S0 = s0, S1 = s1, C0 = c0, C1 = c1, Alive = true };
            if (move.HasValue) { tw.Move = true; tw.P0 = g.rectTransform.anchoredPosition; tw.P1 = move.Value; }
            _tweens.Add(tw);
        }

        private static void Apply(P p)
        {
            float t = p.Life <= 0f ? 1f : Mathf.Clamp01(p.Age / p.Life);
            float s = Mathf.Lerp(p.Size0, p.Size1, t);
            if (p.Stretch > 1f && p.Rt.sizeDelta.y > p.Size0 * 1.5f && !p.Align)
            {
                // Линия: ширина задана, длина — в sizeDelta.y при создании
                p.Rt.sizeDelta = new Vector2(s, p.Rt.sizeDelta.y);
            }
            else p.Rt.sizeDelta = new Vector2(s * (p.Align ? p.Stretch : 1f), s);
            p.Rt.anchoredPosition = p.Pos;
            p.Rt.localRotation = Quaternion.Euler(0, 0, p.Align && p.Vel.sqrMagnitude > 1f ? Mathf.Atan2(p.Vel.y, p.Vel.x) * Mathf.Rad2Deg : p.Rot);
            p.Img.color = Color.Lerp(p.C0, p.C1, t);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime * Speed;
            foreach (var p in _pool)
            {
                if (!p.Alive) continue;
                p.Age += dt;
                if (p.Age >= p.Life) { p.Alive = false; p.Img.enabled = false; p.Rt.sizeDelta = Vector2.zero; continue; }
                p.Vel.y -= p.Gravity * dt;
                p.Vel *= Mathf.Max(0f, 1f - p.Drag * dt);
                p.Pos += p.Vel * dt;
                p.Rot += p.RotVel * dt;
                Apply(p);
            }
            foreach (var f in _fbs)
            {
                if (!f.Alive) continue;
                if (f.Delay > 0f) { f.Delay -= dt; if (f.Delay > 0f) continue; f.Img.enabled = true; }
                f.Age += dt;
                if (f.Age >= f.Dur) { f.Alive = false; f.Img.enabled = false; continue; }
                f.Pos += f.Vel * dt;
                ApplyFB(f);
            }
            for (int i = _tweens.Count - 1; i >= 0; i--)
            {
                var tw = _tweens[i];
                if (tw.Rt == null) { _tweens.RemoveAt(i); continue; }
                tw.Age += dt;
                float t = Mathf.Clamp01(tw.Age / Mathf.Max(0.001f, tw.Dur));
                float e = 1f - (1f - t) * (1f - t);
                tw.Rt.sizeDelta = Vector2.Lerp(tw.S0, tw.S1, e);
                tw.G.color = Color.Lerp(tw.C0, tw.C1, t);
                if (tw.Move) tw.Rt.anchoredPosition = Vector2.Lerp(tw.P0, tw.P1, e);
                if (t >= 1f) _tweens.RemoveAt(i);
            }
            for (int i = _numbers.Count - 1; i >= 0; i--)
            {
                var n = _numbers[i];
                n.age += dt;
                float t = n.age / n.life;
                if (n.t == null || t >= 1f) { if (n.t != null) Destroy(n.t.gameObject); _numbers.RemoveAt(i); continue; }
                float pop = t < 0.15f ? Mathf.Lerp(0.4f, 1.35f, t / 0.15f) : Mathf.Lerp(1.35f, 1f, Mathf.Clamp01((t - 0.15f) / 0.2f));
                n.rt.localScale = Vector3.one * pop;
                n.rt.anchoredPosition = n.pos + new Vector2(0, 70f * Mathf.Sqrt(t));
                n.t.color = new Color(n.c.r, n.c.g, n.c.b, t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f);
                _numbers[i] = n;
            }
            // Вспышка экрана
            if (_flash != null && _flashA > 0f)
            {
                _flashA = Mathf.Max(0f, _flashA - _flashDecay * Time.unscaledDeltaTime);
                var c = _flash.color; c.a = _flashA; _flash.color = c;
            }
            // Тряска стола
            if (_shakeTarget != null)
            {
                if (_shakeTime > 0f)
                {
                    _shakeTime -= Time.unscaledDeltaTime;
                    float a = _shakeAmp * Mathf.Clamp01(_shakeTime * 3f);
                    float tt = Time.unscaledTime * 38f;
                    _shakeTarget.anchoredPosition = _shakeBase + new Vector2((Mathf.PerlinNoise(tt, 0.3f) - 0.5f) * 2f * a, (Mathf.PerlinNoise(0.7f, tt) - 0.5f) * 2f * a);
                }
                else if (_shakeAmp > 0f) { _shakeAmp = 0f; _shakeTarget.anchoredPosition = _shakeBase; }
            }
        }

        public void ClearAll()
        {
            foreach (var p in _pool) { p.Alive = false; if (p.Img != null) p.Img.enabled = false; }
            foreach (var f in _fbs) { f.Alive = false; if (f.Img != null) f.Img.enabled = false; }
            foreach (var n in _numbers) if (n.t != null) Destroy(n.t.gameObject);
            _numbers.Clear();
            _tweens.Clear();
            _flashA = 0f;
        }
    }
}
