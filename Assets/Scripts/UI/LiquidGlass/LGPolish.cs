using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Мелкая «живость» интерфейса — всё тихое и короткое, чтобы не мешать:
    ///   • полосы прогресса доезжают до нового значения плавно, а не прыгают;
    ///   • по идущей полосе (стройка, исследование, решение) раз в несколько секунд пробегает мягкий блик;
    ///   • карточки окна появляются каскадом — одна за другой, с лёгким подъёмом;
    ///   • информационная карточка подсвечивает кромку под курсором;
    ///   • значок-счётчик «дышит», пока есть что-то новое.
    /// Всё на unscaled time — работает и на паузе.
    /// </summary>
    public static class LGFx
    {
        /// <summary>Плавно довести полосу до значения (используется в LGBuild.SetBar).</summary>
        public static void SmoothBar(RectTransform fill, float value)
        {
            if (fill == null) return;
            var b = fill.GetComponent<LGBarFill>();
            if (b == null) b = fill.gameObject.AddComponent<LGBarFill>();
            b.Set(value);
        }

        /// <summary>Блик, изредка пробегающий по заливке полосы, — признак того, что процесс идёт.</summary>
        public static void Shimmer(RectTransform fill)
        {
            if (fill == null || fill.GetComponent<LGShimmer>() != null) return;
            fill.gameObject.AddComponent<LGShimmer>();
        }

        /// <summary>Каскадное появление: index — порядковый номер элемента в окне.</summary>
        public static void CascadeIn(GameObject go, int index, float step = 0.035f)
        {
            if (go == null) return;
            var c = go.GetComponent<LGCascadeIn>() ?? go.AddComponent<LGCascadeIn>();
            c.Delay = Mathf.Min(index * step, 0.5f);
        }

        /// <summary>Подсветка кромки стеклянной карточки при наведении (без звука и масштаба — карточка не кнопка).</summary>
        public static void HoverGlow(GameObject go)
        {
            if (go == null || go.GetComponent<LGInteractive>() != null) return;
            var img = go.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;
            var i = go.AddComponent<LGInteractive>();
            i.sounds = false;
            i.animateScale = false;
        }

        /// <summary>Мягкое «дыхание» масштабом, пока включено.</summary>
        public static void Pulse(GameObject go, bool on)
        {
            if (go == null) return;
            var p = go.GetComponent<LGPulse>();
            if (p == null) { if (!on) return; p = go.AddComponent<LGPulse>(); }
            p.On = on;
        }

        private static Sprite s_glint;
        /// <summary>Горизонтальная полоса света: прозрачно → бело → прозрачно.</summary>
        internal static Sprite Glint
        {
            get
            {
                if (s_glint != null) return s_glint;
                var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                for (int x = 0; x < 64; x++)
                {
                    float t = x / 63f;
                    float a = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 2.2f);
                    tex.SetPixel(x, 0, new Color(1f, 1f, 1f, a));
                }
                tex.Apply();
                s_glint = Sprite.Create(tex, new Rect(0, 0, 64, 1), new Vector2(0.5f, 0.5f));
                s_glint.name = "LGGlint";
                return s_glint;
            }
        }
    }

    /// <summary>Заливка полосы, доезжающая до цели по экспоненте (~0.3 с).</summary>
    [DisallowMultipleComponent]
    public sealed class LGBarFill : MonoBehaviour
    {
        private RectTransform _rt;
        private Image _img;
        private float _cur, _target;
        private bool _init;

        public void Set(float v)
        {
            if (_rt == null) { _rt = (RectTransform)transform; _img = GetComponent<Image>(); }
            _target = Mathf.Clamp01(v);
            if (!_init)
            {
                // Первое значение берём с полосы как есть — без анимации от нуля при каждой перестройке окна
                _cur = _rt.anchorMax.x;
                _init = true;
            }
            if (!gameObject.activeInHierarchy || Mathf.Abs(_cur - _target) < 0.0005f) { _cur = _target; Apply(); enabled = false; return; }
            enabled = true;
        }

        private void Update()
        {
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f);
            _cur = Mathf.Lerp(_cur, _target, k);
            if (Mathf.Abs(_cur - _target) < 0.0015f) { _cur = _target; enabled = false; }
            Apply();
        }

        private void Apply()
        {
            _rt.anchorMax = new Vector2(_cur, 1f);
            if (_img != null) _img.enabled = _cur > 0.001f;
        }
    }

    /// <summary>Блик по заливке полосы: раз в ~3.5 с, за 0.9 с, полупрозрачный.</summary>
    [DisallowMultipleComponent]
    public sealed class LGShimmer : MonoBehaviour
    {
        private const float Period = 3.5f, Run = 0.9f, Alpha = 0.32f;
        private RectTransform _glint;
        private Image _img;
        private float _t;

        private void Start()
        {
            var host = (RectTransform)transform;
            if (GetComponent<RectMask2D>() == null) gameObject.AddComponent<RectMask2D>();
            var go = new GameObject("Glint", typeof(RectTransform));
            go.transform.SetParent(host, false);
            _glint = (RectTransform)go.transform;
            _glint.anchorMin = new Vector2(0f, 0f);
            _glint.anchorMax = new Vector2(0f, 1f);
            _glint.pivot = new Vector2(0.5f, 0.5f);
            _img = go.AddComponent<Image>();
            _img.sprite = LGFx.Glint;
            _img.raycastTarget = false;
            _img.color = new Color(1f, 1f, 1f, 0f);
            LG.Ignore(go);
            _t = Random.Range(0f, Period);   // соседние полосы не бликуют хором
        }

        private void Update()
        {
            if (_glint == null) return;
            _t += Time.unscaledDeltaTime;
            if (_t > Period) _t -= Period;
            float w = ((RectTransform)transform).rect.width;
            if (_t > Run || w < 12f) { _img.color = new Color(1f, 1f, 1f, 0f); return; }
            float p = Mathf.SmoothStep(0f, 1f, _t / Run);
            float gw = Mathf.Clamp(w * 0.35f, 24f, 140f);
            _glint.sizeDelta = new Vector2(gw, 0f);
            _glint.anchoredPosition = new Vector2(Mathf.Lerp(-gw * 0.5f, w + gw * 0.5f, p), 0f);
            _img.color = new Color(1f, 1f, 1f, Alpha * Mathf.Sin(p * Mathf.PI));
        }
    }

    /// <summary>Появление с задержкой: прозрачность 0 → 1 и подъём на 10 пикселей (если элемент не в раскладке).</summary>
    [DisallowMultipleComponent]
    public sealed class LGCascadeIn : MonoBehaviour
    {
        public float Delay;
        private const float Duration = 0.32f, Rise = 10f;
        private CanvasGroup _cg;
        private RectTransform _rt;
        private Vector2 _rest;
        private bool _move;
        private float _t;

        private void Start()
        {
            _rt = (RectTransform)transform;
            _cg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            // Внутри раскладки позицию задаёт LayoutGroup — двигаем только вне её
            _move = transform.parent == null || transform.parent.GetComponent<LayoutGroup>() == null;
            _rest = _rt.anchoredPosition;
            _cg.alpha = 0f;
            if (_move) _rt.anchoredPosition = _rest - new Vector2(0f, Rise);
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01((_t - Delay) / Duration);
            float e = 1f - Mathf.Pow(1f - p, 3f);
            _cg.alpha = e;
            if (_move) _rt.anchoredPosition = _rest - new Vector2(0f, Rise * (1f - e));
            if (p >= 1f) Destroy(this);
        }

        private void OnDestroy()
        {
            if (_cg != null) _cg.alpha = 1f;
            if (_move && _rt != null) _rt.anchoredPosition = _rest;
        }
    }

    /// <summary>«Дыхание» масштабом ±7% раз в 1.6 с; выключенный — плавно возвращается к 1.</summary>
    [DisallowMultipleComponent]
    public sealed class LGPulse : MonoBehaviour
    {
        public bool On;
        private float _t, _amp;

        private void Update()
        {
            _amp = Mathf.MoveTowards(_amp, On ? 1f : 0f, Time.unscaledDeltaTime * 3f);
            _t += Time.unscaledDeltaTime;
            float s = 1f + 0.07f * _amp * (0.5f - 0.5f * Mathf.Cos(_t * Mathf.PI * 2f / 1.6f));
            transform.localScale = new Vector3(s, s, 1f);
            if (!On && _amp <= 0f) { transform.localScale = Vector3.one; _t = 0f; }
        }
    }
}
