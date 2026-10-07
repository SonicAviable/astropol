using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Правители империй: портреты (Resources/Leaders), имена и параметры «оживления».
    /// Координаты глаз заданы в пикселях исходной картинки 1342×2000 — по ним рисуется моргание.
    /// </summary>
    public static class LeaderPortraits
    {
        public sealed class Entry
        {
            public string Key;
            public string Name;
            public string Title;
            public string Quote;
            public Color Accent;
            public Vector4 EyeL, EyeR;      // u, v, rx, ry в uv текстуры
            public Vector2 Face;            // центр лица в uv — точка фокуса при кадрировании

            private Texture2D _tex, _fx;
            public Texture2D Texture => _tex != null ? _tex : (_tex = Resources.Load<Texture2D>("Leaders/leader_" + Key));
            public Texture2D Fx => _fx != null ? _fx : (_fx = Resources.Load<Texture2D>("Leaders/leader_" + Key + "_fx"));
        }

        private const float SrcW = 1342f, SrcH = 2000f;

        private static Vector4 Eye(float x, float y, float rx, float ry)
            => new Vector4(x / SrcW, 1f - y / SrcH, rx / SrcW, ry / SrcH);

        private static Vector2 Uv(float x, float y) => new Vector2(x / SrcW, 1f - y / SrcH);

        public static readonly Entry Xarn = new Entry
        {
            Key = "xarn",
            Name = "Ардан Ворх",
            Title = "Верховный маршал Доминиона",
            Quote = "Мир — это пауза между победами. Не заставляйте меня её прерывать.",
            Accent = new Color(1f, 0.32f, 0.28f),
            EyeL = Eye(535, 422, 44, 15),
            EyeR = Eye(707, 416, 42, 15),
            Face = Uv(625, 470)
        };

        public static readonly Entry Astrea = new Entry
        {
            Key = "astrea",
            Name = "Элина Сорель",
            Title = "Председатель Совета Республики",
            Quote = "Любой спор можно решить словами. Почти любой.",
            Accent = new Color(0.30f, 0.90f, 0.86f),
            EyeL = Eye(590, 402, 42, 15),
            EyeR = Eye(740, 399, 42, 15),
            Face = Uv(665, 460)
        };

        public static readonly Entry Aquila = new Entry
        {
            Key = "aquila",
            Name = "Лукан Вейл",
            Title = "Генеральный директор Синдиката",
            Quote = "У всего есть цена. Даже у вашей независимости.",
            Accent = new Color(1f, 0.76f, 0.34f),
            EyeL = Eye(552, 556, 44, 16),
            EyeR = Eye(756, 546, 42, 15),
            Face = Uv(660, 610)
        };

        /// <summary>Правитель фракции по её названию (Ксарн / Астрея / Аквила).</summary>
        public static Entry ForFaction(string factionName)
        {
            if (string.IsNullOrEmpty(factionName)) return null;
            if (factionName.Contains("Ксарн")) return Xarn;
            if (factionName.Contains("Астре")) return Astrea;
            if (factionName.Contains("Аквил")) return Aquila;
            return null;
        }

        public static Entry ForFaction(FactionInfo f) => f != null ? ForFaction(f.Name) : null;
    }

    /// <summary>
    /// Анимированный портрет правителя в UI: RawImage с шейдером «живого портрета».
    /// Сам кадрирует картинку под размер рамки (zoom — насколько приблизить к лицу),
    /// задаёт моргание (с редким двойным), дыхание, мерцание огней и редкий сбой сигнала.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class LeaderPortraitView : MonoBehaviour
    {
        private static Shader s_shader;

        private RawImage _img;
        private Material _mat;
        private LeaderPortraits.Entry _entry;
        private float _zoom = 1f;
        private float _focusBias;
        private float _seed;

        private float _nextBlink, _blinkStart = -1f;
        private bool _doubleBlink;
        private float _nextGlitch, _glitchStart = -1f;
        private bool _glitches = true;
        private bool _blinks = true;

        private const float BlinkClose = 0.07f, BlinkHold = 0.03f, BlinkOpen = 0.10f;
        private static readonly int IdT = Shader.PropertyToID("_T");
        private static readonly int IdBlink = Shader.PropertyToID("_Blink");
        private static readonly int IdGlitch = Shader.PropertyToID("_Glitch");
        private static readonly int IdUvRect = Shader.PropertyToID("_UvRect");

        /// <summary>
        /// Создать портрет на всю площадь родителя. zoom 1 — по ширине картинки (в полный рост),
        /// 2 — крупный план головы и плеч. fadeLeft — мягкий левый край (доля ширины).
        /// </summary>
        public static LeaderPortraitView Create(Transform parent, LeaderPortraits.Entry entry, float zoom = 1.6f,
                                                float fadeLeft = 0f, bool glitches = true)
            => Create(parent, entry, zoom, new Vector4(fadeLeft, 0f, 0f, 0f), glitches);

        /// <summary>То же, но с мягкими краями со всех сторон (left, right, bottom, top — доли кадра).</summary>
        public static LeaderPortraitView Create(Transform parent, LeaderPortraits.Entry entry, float zoom, Vector4 fade, bool glitches)
            => Create(parent, entry, zoom, fade, glitches, true, false);

        /// <summary>
        /// Полная версия: blinks — моргание, cutout — вырезать фигуру из фона картинки
        /// (маска фигуры лежит в альфа-канале FX-текстуры), чтобы правитель стоял в своей сцене.
        /// </summary>
        public static LeaderPortraitView Create(Transform parent, LeaderPortraits.Entry entry, float zoom, Vector4 fade,
                                                bool glitches, bool blinks, bool cutout)
        {
            if (entry == null || entry.Texture == null) return null;
            var rt = LGBuild.Rect(parent, "LeaderPortrait");
            rt.Stretch();
            var img = rt.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            LG.Ignore(rt.gameObject);
            var v = rt.gameObject.AddComponent<LeaderPortraitView>();
            v._blinks = blinks;
            v.Setup(entry, zoom, fade, glitches);
            if (v._mat != null) v._mat.SetFloat("_Cutout", cutout ? 1f : 0f);
            return v;
        }

        private void Setup(LeaderPortraits.Entry entry, float zoom, Vector4 fade, bool glitches)
        {
            _img = GetComponent<RawImage>();
            _entry = entry;
            _zoom = Mathf.Max(1f, zoom);
            _glitches = glitches;
            _seed = Random.Range(0f, 100f);
            _img.texture = entry.Texture;

            if (s_shader == null) s_shader = Resources.Load<Shader>("Shaders/LeaderPortrait");
            if (s_shader != null && s_shader.isSupported)
            {
                _mat = new Material(s_shader) { hideFlags = HideFlags.DontSave, name = "LeaderPortrait_" + entry.Key };
                if (entry.Fx != null) _mat.SetTexture("_FxTex", entry.Fx);
                _mat.SetColor("_Accent", entry.Accent);
                _mat.SetVector("_EyeL", entry.EyeL);
                _mat.SetVector("_EyeR", entry.EyeR);
                _mat.SetVector("_Fade", fade);
                _img.material = _mat;
            }

            float now = Time.unscaledTime;
            _nextBlink = now + Random.Range(0.8f, 3.5f);
            _nextGlitch = now + Random.Range(10f, 25f);
            Frame();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_img != null) Frame();
        }

        /// <summary>Кадрирование «cover» с приближением к лицу.</summary>
        private void Frame()
        {
            var rect = ((RectTransform)transform).rect;
            if (rect.width < 1f || rect.height < 1f || _entry == null) return;
            var tex = _entry.Texture;
            float texAspect = tex != null ? 1342f / 2000f : 0.671f;
            float aspect = rect.width / rect.height;

            float w, h;
            if (aspect >= texAspect) { w = 1f; h = texAspect / aspect; }
            else { h = 1f; w = aspect / texAspect; }
            w /= _zoom;
            h /= _zoom;

            // Лицо — чуть выше центра кадра
            Vector2 f = _entry.Face;
            float x = Mathf.Clamp(f.x - w * 0.5f, 0f, 1f - w);
            float y = Mathf.Clamp(f.y - h * 0.58f, 0f, 1f - h);
            var r = new Rect(x, y, w, h);
            _img.uvRect = r;
            if (_mat != null)
            {
                var v = new Vector4(r.x, r.y, r.width, r.height);
                _mat.SetVector(IdUvRect, v);
                var rm = _img.materialForRendering;
                if (rm != null && rm != _mat) rm.SetVector(IdUvRect, v);
            }
        }

        private void Update()
        {
            if (_mat == null) return;
            float now = Time.unscaledTime;
            // Под маской (stencil) UI рисует копию материала — параметры кладём и в неё
            var rm = _img.materialForRendering;
            if (rm == _mat) rm = null;
            SetF(rm, IdT, now + _seed);

            // Моргание: закрыть → задержать → открыть; иногда дважды подряд
            float blink = 0f;
            if (_blinks && _blinkStart < 0f && now >= _nextBlink) { _blinkStart = now; }
            if (_blinkStart >= 0f)
            {
                float t = now - _blinkStart;
                if (t < BlinkClose) blink = t / BlinkClose;
                else if (t < BlinkClose + BlinkHold) blink = 1f;
                else if (t < BlinkClose + BlinkHold + BlinkOpen) blink = 1f - (t - BlinkClose - BlinkHold) / BlinkOpen;
                else
                {
                    _blinkStart = -1f;
                    if (!_doubleBlink && Random.value < 0.15f) { _doubleBlink = true; _nextBlink = now + 0.12f; }
                    else { _doubleBlink = false; _nextBlink = now + Random.Range(2.6f, 6.5f); }
                }
            }
            SetF(rm, IdBlink, Mathf.SmoothStep(0f, 1f, blink));

            // Редкий короткий сбой сигнала связи
            float glitch = 0f;
            if (_glitches)
            {
                if (_glitchStart < 0f && now >= _nextGlitch) _glitchStart = now;
                if (_glitchStart >= 0f)
                {
                    float t = now - _glitchStart;
                    if (t < 0.18f) glitch = Mathf.Sin(t / 0.18f * Mathf.PI) * (0.6f + 0.4f * Mathf.Sin(now * 90f));
                    else { _glitchStart = -1f; _nextGlitch = now + Random.Range(14f, 32f); }
                }
            }
            SetF(rm, IdGlitch, Mathf.Max(0f, glitch));
        }

        private void SetF(Material rendering, int id, float v)
        {
            _mat.SetFloat(id, v);
            if (rendering != null) rendering.SetFloat(id, v);
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
