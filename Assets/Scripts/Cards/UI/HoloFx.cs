using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Живой голографический элемент интерфейса: вращается, «дышит» яркостью и размером. Кольца и лучи за портретами
    /// лидеров, кольцо вокруг кнопки хода и гнёзд счёта. Время — unscaled (идёт и на паузе анимаций боя).
    /// </summary>
    public sealed class HoloSpin : MonoBehaviour
    {
        /// <summary>Градусов в секунду (минус — по часовой).</summary>
        public float Speed = 10f;
        /// <summary>Размах «дыхания» яркости (доля от исходной альфы) и его частота, рад/с.</summary>
        public float Pulse = 0.25f, PulseRate = 1.6f;
        /// <summary>Размах «дыхания» размера.</summary>
        public float Breathe;
        public float Phase;

        private Graphic _g;
        private float _a0 = -1f;

        public static HoloSpin Add(Graphic g, float speed, float pulse = 0.25f, float breathe = 0f, float phase = 0f)
        {
            var h = g.gameObject.AddComponent<HoloSpin>();
            h.Speed = speed; h.Pulse = pulse; h.Breathe = breathe; h.Phase = phase;
            return h;
        }

        private void Update()
        {
            if (_g == null) { _g = GetComponent<Graphic>(); if (_g != null) _a0 = _g.color.a; }
            float t = Time.unscaledTime;
            transform.localRotation = Quaternion.Euler(0f, 0f, Phase * 57f + t * Speed);
            float w = Mathf.Sin(t * PulseRate + Phase);
            if (_g != null && Pulse > 0f)
            {
                var c = _g.color;
                c.a = _a0 * (1f + Pulse * w);
                _g.color = c;
            }
            if (Breathe > 0f) transform.localScale = Vector3.one * (1f + Breathe * w);
        }
    }

    /// <summary>
    /// Искры за портретом лидера: тонкие светящиеся штрихи цвета фракции поднимаются из-за круга и тают.
    /// Штрихи вытянуты по ходу движения — не «точки». Сами картинки лежат под портретом.
    /// </summary>
    public sealed class HoloMotes : MonoBehaviour
    {
        private sealed class M { public RectTransform Rt; public Image Img; public Vector2 Pos, Vel; public float Age, Life, Len; }

        public Color Tint = Color.white;
        public float Radius = 46f, Rate = 5f;
        private readonly List<M> _m = new List<M>();
        private float _acc;

        public static HoloMotes Add(RectTransform parent, Vector2 pos, Color tint, float radius)
        {
            var go = new GameObject("Motes", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = Vector2.zero;
            var h = go.AddComponent<HoloMotes>();
            h.Tint = tint; h.Radius = radius;
            return h;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _acc += dt * Rate;
            while (_acc >= 1f) { _acc -= 1f; Spawn(); }
            foreach (var m in _m)
            {
                if (m.Img == null || !m.Img.enabled) continue;
                m.Age += dt;
                float k = m.Age / m.Life;
                if (k >= 1f) { m.Img.enabled = false; continue; }
                m.Vel *= 1f - dt * 0.6f;
                m.Pos += m.Vel * dt;
                m.Rt.anchoredPosition = m.Pos;
                float a = Mathf.Sin(k * Mathf.PI);
                m.Img.color = new Color(Tint.r, Tint.g, Tint.b, Tint.a * a);
                m.Rt.sizeDelta = new Vector2(m.Len * (0.6f + 0.4f * a), 3f);
                m.Rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(m.Vel.y, m.Vel.x) * Mathf.Rad2Deg);
            }
        }

        private void Spawn()
        {
            M m = null;
            foreach (var x in _m) if (x.Img != null && !x.Img.enabled) { m = x; break; }
            if (m == null)
            {
                if (_m.Count >= 24) return;
                var go = new GameObject("Mote", typeof(RectTransform));
                m = new M { Rt = (RectTransform)go.transform };
                m.Rt.SetParent(transform, false);
                m.Img = go.AddComponent<Image>();
                m.Img.sprite = CardSprites.Spark;
                m.Img.material = UIFx.GlowMat;
                m.Img.raycastTarget = false;
                _m.Add(m);
            }
            float ang = Random.Range(0f, Mathf.PI * 2f);
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            m.Pos = dir * Radius * Random.Range(0.8f, 1.05f);
            // наружу от круга и немного вверх — как жар над портретом
            m.Vel = dir * Random.Range(18f, 40f) + new Vector2(0f, Random.Range(14f, 34f));
            m.Age = 0f;
            m.Life = Random.Range(1.1f, 2.2f);
            m.Len = Random.Range(10f, 22f);
            m.Img.enabled = true;
            m.Rt.anchoredPosition = m.Pos;
        }
    }

    /// <summary>
    /// Импульс энергии по линии фронта: яркий сгусток с хвостом пробегает от гнезда счёта к гнезду кнопки хода
    /// и гаснет; пауза — и снова.
    /// </summary>
    public sealed class HoloRunner : MonoBehaviour
    {
        public Vector2 From, To;
        public float Duration = 1.6f, Pause = 3.5f;
        private float _t;
        private Image _img;

        private void Update()
        {
            if (_img == null) _img = GetComponent<Image>();
            _t += Time.unscaledDeltaTime;
            float cycle = Duration + Pause;
            if (_t > cycle) _t -= cycle;
            float k = Mathf.Clamp01(_t / Duration);
            var rt = (RectTransform)transform;
            rt.anchoredPosition = Vector2.Lerp(From, To, Mathf.SmoothStep(0f, 1f, k));
            float a = _t < Duration ? Mathf.Sin(k * Mathf.PI) : 0f;
            if (_img != null) { var c = _img.color; c.a = a * 0.85f; _img.color = c; }
        }
    }
}
