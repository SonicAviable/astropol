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
                          bool align = false, Color? end = null, float stretch = 1f)
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
                p.Rot = Random.Range(0f, 360f);
                p.RotVel = align ? 0f : Random.Range(-120f, 120f);
                p.Drag = drag;
                p.Gravity = gravity;
                p.C0 = c;
                p.C1 = end ?? new Color(c.r, c.g, c.b, 0f);
                p.Align = align;
                p.Stretch = stretch;
                Apply(p);
            }
        }

        // ---------------------------------------------------------------- готовые эффекты

        public void Sparks(Vector2 pos, Color c, int n = 18, float power = 1f)
            => Burst(pos, n, CardSprites.Spark, c, 180f * power, 520f * power, 0.25f, 0.6f, 22f, 6f, 600f, 2.2f, align: true, stretch: 3.2f);

        public void Dust(Vector2 pos, float scale = 1f, Color? c = null)
            => Burst(pos, Mathf.RoundToInt(10 * scale), CardSprites.Smoke, c ?? new Color(0.62f, 0.55f, 0.46f, 0.55f), 40f, 160f * scale, 0.7f, 1.4f, 50f * scale, 120f * scale, -20f, 2.5f, 90f, 200f);

        public void Smoke(Vector2 pos, float scale = 1f, Color? c = null)
            => Burst(pos, Mathf.RoundToInt(6 * scale), CardSprites.Smoke, c ?? new Color(0.12f, 0.12f, 0.13f, 0.7f), 20f, 70f, 1.4f, 2.6f, 60f * scale, 160f * scale, -40f, 1.2f, 90f, 120f);

        public void Explosion(Vector2 pos, float scale = 1f)
        {
            Burst(pos, 1, CardSprites.Glow, new Color(1f, 0.85f, 0.55f, 1f), 0f, 0f, 0.35f, 0.35f, 60f * scale, 260f * scale, 0f, 0f, end: new Color(1f, 0.4f, 0.1f, 0f));
            Burst(pos, Mathf.RoundToInt(14 * scale), CardSprites.Glow, new Color(1f, 0.55f, 0.15f, 0.9f), 60f, 260f * scale, 0.25f, 0.55f, 40f * scale, 10f, 0f, 3f, end: new Color(0.6f, 0.1f, 0.02f, 0f));
            Sparks(pos, new Color(1f, 0.8f, 0.4f), Mathf.RoundToInt(16 * scale), scale);
            Smoke(pos, scale);
            Ring(pos, new Color(1f, 0.75f, 0.45f, 0.8f), 20f * scale, 260f * scale, 0.45f);
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
            Burst(pos, Mathf.RoundToInt(5 * scale), CardSprites.Smoke, new Color(c.r, c.g, c.b, 0.55f), 20f, 90f, 0.5f, 0.9f, 36f * scale, 90f * scale, 60f, 2f);
            if (k == BloodKind.Coolant) Sparks(pos, new Color(0.7f, 0.9f, 1f), 14, scale);
        }

        public static bool GoreOn => PlayerPrefs.GetInt("set.gore", 1) == 1;

        public void Ring(Vector2 pos, Color c, float from, float to, float dur, Sprite sp = null)
        {
            var p = Get(sp ?? CardSprites.Ring);
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
            var p = Get(CardSprites.Glow);
            if (p == null) return dur;
            p.Pos = a; p.Life = dur; p.Size0 = size; p.Size1 = size * 0.8f; p.C0 = c; p.C1 = c; p.Drag = 0f; p.Rot = 0f; p.RotVel = 0f;
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
            var t = LGBuild.Label(_layer, text, size, c, TextAnchor.MiddleCenter, bold: true);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(200, 60);
            rt.anchoredPosition = pos;
            var ol = t.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0, 0, 0, 0.85f);
            ol.effectDistance = new Vector2(2, -2);
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
            foreach (var n in _numbers) if (n.t != null) Destroy(n.t.gameObject);
            _numbers.Clear();
            _tweens.Clear();
            _flashA = 0f;
        }
    }
}
