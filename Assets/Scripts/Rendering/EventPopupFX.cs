using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Оживление окна события с выбором: иллюстрация проявляется из темноты, медленно «дышит»
    /// и дрейфует (эффект Кена Бёрнса), по ней один раз проходит мягкий блик цвета аномалии,
    /// затем по очереди появляются описание и варианты ответа.
    /// Всё на unscaled time — пока открыто событие, игра стоит на паузе.
    /// </summary>
    public sealed class EventPopupFX : MonoBehaviour
    {
        private const float RevealDelay = 0.06f, RevealTime = 0.75f;
        private const float ZoomTime = 1.6f, ZoomFrom = 1.16f, ZoomRest = 1.05f;
        private const float SweepStart = 0.30f, SweepTime = 1.10f;
        private const float DescDelay = 0.18f, OptDelay = 0.38f, OptStep = 0.07f, ItemTime = 0.32f;
        private const float LabelShift = 18f;

        private static readonly Dictionary<string, Texture2D> s_Art = new Dictionary<string, Texture2D>();
        private static Sprite s_Vertical, s_Horizontal;

        private RectTransform _frame, _mask, _artRt;
        private RawImage _art;
        private AspectRatioFitter _fitter;
        private Image _cover, _sweep, _edge;
        private CanvasGroup _desc;
        private bool _hasArt;
        private Color _accent = Color.white;
        private float _t;
        private bool _playing;

        private struct Item
        {
            public CanvasGroup Group;
            public RectTransform Label;
            public Vector2 Min, Max;
            public float Delay;
        }
        private readonly List<Item> _items = new List<Item>();

        /// <summary>Рамка иллюстрации — под шапкой окна (отступ top), во всю ширину с полями inset.</summary>
        public static EventPopupFX Create(RectTransform window, float top, float inset, float height, RectTransform descBox)
        {
            var fx = window.gameObject.AddComponent<EventPopupFX>();
            fx.Build(top, inset, height);
            fx._desc = descBox.GetComponent<CanvasGroup>();
            if (fx._desc == null) fx._desc = descBox.gameObject.AddComponent<CanvasGroup>();
            return fx;
        }

        private void Build(float top, float inset, float height)
        {
            var go = new GameObject("EventArt");
            go.transform.SetParent(transform, false);
            _frame = go.AddComponent<RectTransform>();
            _frame.anchorMin = new Vector2(0, 1);
            _frame.anchorMax = new Vector2(1, 1);
            _frame.pivot = new Vector2(0.5f, 1);
            _frame.offsetMin = new Vector2(inset, -top - height);
            _frame.offsetMax = new Vector2(-inset, -top);
            LG.Ignore(go);                                       // свои слои, без авто-стекла

            _mask = LG.RoundedMask(_frame, 14f);

            _art = new GameObject("Art").AddComponent<RawImage>();
            _art.transform.SetParent(_mask, false);
            _art.raycastTarget = false;
            _artRt = _art.rectTransform;
            _artRt.anchorMin = _artRt.anchorMax = new Vector2(0.5f, 0.5f);
            _fitter = _art.gameObject.AddComponent<AspectRatioFitter>();
            _fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;

            // Снизу картинка растворяется в фоне окна, сверху — лёгкое затемнение под шапку
            var bottom = Layer("FadeBottom", VerticalSprite(), new Color(0.030f, 0.062f, 0.088f, 0.92f));
            bottom.rectTransform.anchorMax = new Vector2(1, 0.55f);
            var topShade = Layer("FadeTop", VerticalSprite(), new Color(0.030f, 0.062f, 0.088f, 0.45f));
            topShade.rectTransform.anchorMin = new Vector2(0, 0.75f);
            topShade.rectTransform.localScale = new Vector3(1, -1, 1);

            // Блик-сканер: мягкая вертикальная полоса цвета аномалии
            _sweep = Layer("Sweep", HorizontalSprite(), Color.clear);
            _sweep.rectTransform.anchorMin = new Vector2(0, 0);
            _sweep.rectTransform.anchorMax = new Vector2(0, 1);
            _sweep.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _sweep.rectTransform.sizeDelta = new Vector2(140f, 0);

            // Шторка проявления
            _cover = Layer("Cover", null, new Color(0.030f, 0.062f, 0.088f, 1f));

            // Светящаяся кромка снизу
            _edge = Layer("Edge", HorizontalSprite(), Color.clear);
            _edge.rectTransform.anchorMax = new Vector2(1, 0);
            _edge.rectTransform.pivot = new Vector2(0.5f, 0);
            _edge.rectTransform.sizeDelta = new Vector2(0, 2f);

            go.SetActive(false);
        }

        private Image Layer(string name, Sprite sprite, Color color)
        {
            var img = new GameObject(name).AddComponent<Image>();
            img.transform.SetParent(_mask, false);
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return img;
        }

        /// <summary>Поставить иллюстрацию события. false — картинки нет, рамка скрыта.</summary>
        public bool SetArt(string art, Color accent)
        {
            _accent = accent;
            var tex = Load(art);
            _hasArt = tex != null;
            _frame.gameObject.SetActive(_hasArt);
            if (!_hasArt) return false;

            _art.texture = tex;
            _fitter.aspectRatio = tex.width / (float)tex.height;
            return true;
        }

        private static Texture2D Load(string art)
        {
            if (string.IsNullOrEmpty(art)) return null;
            if (s_Art.TryGetValue(art, out var tex) && tex != null) return tex;
            tex = Resources.Load<Texture2D>("UI/Events/" + art);
            if (tex != null)
            {
                tex.wrapMode = TextureWrapMode.Clamp;
                s_Art[art] = tex;
            }
            return tex;
        }

        public void ClearOptions() => _items.Clear();

        /// <summary>Вариант ответа появится в каскаде; подпись чуть выезжает справа.</summary>
        public void AddOption(GameObject button, RectTransform label)
        {
            var cg = button.GetComponent<CanvasGroup>();
            if (cg == null) cg = button.AddComponent<CanvasGroup>();
            _items.Add(new Item
            {
                Group = cg,
                Label = label,
                Min = label != null ? label.offsetMin : Vector2.zero,
                Max = label != null ? label.offsetMax : Vector2.zero,
                Delay = (_hasArt ? OptDelay : OptDelay * 0.5f) + _items.Count * OptStep
            });
        }

        /// <summary>Запустить анимацию с начала (после SetArt и AddOption).</summary>
        public void Play()
        {
            _t = 0f;
            _playing = true;
            Apply(0f);
        }

        private void Update()
        {
            if (!_playing) return;
            _t += Time.unscaledDeltaTime;
            Apply(_t);
        }

        private void Apply(float t)
        {
            if (_hasArt) ApplyArt(t);

            float descDelay = _hasArt ? DescDelay : 0f;
            if (_desc != null) _desc.alpha = LGEase.OutCubic((t - descDelay) / ItemTime);

            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.Group == null) continue;
                float k = LGEase.OutCubic((t - it.Delay) / ItemTime);
                it.Group.alpha = k;
                it.Group.blocksRaycasts = k > 0.6f;              // не ловим клик по ещё невидимой кнопке
                if (it.Label != null)
                {
                    var shift = new Vector2((1f - k) * LabelShift, 0f);
                    it.Label.offsetMin = it.Min + shift;
                    it.Label.offsetMax = it.Max + shift;
                }
            }
        }

        private void ApplyArt(float t)
        {
            // Проявление из темноты
            float reveal = LGEase.OutCubic((t - RevealDelay) / RevealTime);
            _cover.color = new Color(_cover.color.r, _cover.color.g, _cover.color.b, 1f - reveal);

            // Наезд камеры, затем медленное «дыхание» и дрейф (запас по краям даёт масштаб ≥ 1.03)
            float zoom = Mathf.Lerp(ZoomFrom, ZoomRest, LGEase.OutCubic(t / ZoomTime));
            float idle = Mathf.Clamp01((t - ZoomTime * 0.5f) / ZoomTime);
            float scale = zoom + idle * 0.02f * Mathf.Sin(t * 0.35f);
            _artRt.localScale = new Vector3(scale, scale, 1f);
            float w = _mask.rect.width;
            float slackX = Mathf.Max(0f, (scale - 1f) * w * 0.5f - 1f);
            float slackY = Mathf.Max(0f, (_artRt.rect.height * scale - _mask.rect.height) * 0.5f - 1f);
            _artRt.anchoredPosition = new Vector2(
                Mathf.Clamp(-6f + 10f * Mathf.Sin(t * 0.17f) * idle, -slackX, slackX),
                Mathf.Clamp(-14f * (1f - LGEase.OutCubic(t / ZoomTime)) + 12f * Mathf.Sin(t * 0.11f) * idle, -slackY, slackY));

            // Один проход блика слева направо
            float s = (t - SweepStart) / SweepTime;
            if (s > 0f && s < 1f)
            {
                float x = Mathf.Lerp(-80f, _mask.rect.width + 80f, LGEase.InOutCubic(s));
                _sweep.rectTransform.anchoredPosition = new Vector2(x, 0f);
                _sweep.color = new Color(_accent.r, _accent.g, _accent.b, 0.22f * Mathf.Sin(s * Mathf.PI));
            }
            else _sweep.color = Color.clear;

            // Кромка цвета аномалии: вспыхивает вместе с проявлением и тихо пульсирует
            float edge = reveal * (0.55f + 0.15f * Mathf.Sin(t * 1.6f));
            _edge.color = new Color(_accent.r, _accent.g, _accent.b, edge);
        }

        // ---------------- Градиенты ----------------

        /// <summary>Снизу непрозрачно, вверх — в ноль.</summary>
        private static Sprite VerticalSprite()
        {
            if (s_Vertical != null) return s_Vertical;
            const int h = 64;
            var tex = NewTex(1, h);
            for (int y = 0; y < h; y++)
            {
                float a = 1f - y / (h - 1f);
                tex.SetPixel(0, y, new Color(1, 1, 1, a * a * (3f - 2f * a)));
            }
            tex.Apply(false, true);
            return s_Vertical = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Мягкая полоса: прозрачно по краям, пик в центре.</summary>
        private static Sprite HorizontalSprite()
        {
            if (s_Horizontal != null) return s_Horizontal;
            const int w = 64;
            var tex = NewTex(w, 1);
            for (int x = 0; x < w; x++)
            {
                float a = Mathf.Sin(x / (w - 1f) * Mathf.PI);
                tex.SetPixel(x, 0, new Color(1, 1, 1, a * a));
            }
            tex.Apply(false, true);
            return s_Horizontal = Sprite.Create(tex, new Rect(0, 0, w, 1), new Vector2(0.5f, 0.5f));
        }

        private static Texture2D NewTex(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
    }
}
