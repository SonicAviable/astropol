using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Экраны предупреждений перед заставкой — как в больших играх: на чёрном фоне по очереди
    ///   1. предупреждение о светочувствительной эпилепсии;
    ///   2. автосохранение (не выключать компьютер во время сохранения);
    ///   3. «все события вымышлены», совет играть в наушниках и копирайт студии.
    /// Каждый экран плавно проявляется и гаснет. Пропуск: любая клавиша или клик — к следующему экрану (быстрое
    /// угасание); Esc или ещё одно нажатие, пока экран гаснет, — пропустить все предупреждения сразу.
    ///
    /// Фон — чистый чёрный. Появление экрана: значок мягко проявляется с тёплым ореолом, буквы заголовка сходятся
    /// из широкой разрядки, ромб поворачивается на место и от него вырастает латунная линия, текст плавно поднимается
    /// следом. Уход — плавное угасание. Только плавные кривые, без «пружин» и масштабирования текста (текст не мерцает).
    /// Звук (Resources/Audio/Intro, Tools/intro_sfx/generate.py): тихий эмбиент под всеми экранами,
    /// мягкое нарастание с глубоким ударом при появлении экрана, стеклянный тон при пропуске клавишей.
    /// Пока идут предупреждения, IntroVideo готовит ролик в фоне. Done — все экраны показаны.
    /// Разметка — в «эталонных» 1920×1080 (холст IntroVideo масштабируется CanvasScaler).
    /// </summary>
    public sealed class IntroWarnings : MonoBehaviour
    {
        private const float FadeIn = 1.1f, FadeOut = 0.9f, SkipFade = 0.4f, MinShow = 0.25f, RuleW = 360f, Gap = 0.4f;
        private const float BedVolume = 0.8f;

        private static readonly Color Gold = new Color(0.906f, 0.757f, 0.439f);
        private static readonly Color Cream = new Color(0.925f, 0.898f, 0.827f);
        private static readonly Color Muted = new Color(0.663f, 0.639f, 0.573f);

        private sealed class Card
        {
            public CanvasGroup Group, Body;
            public RectTransform BodyRt, Gem;
            public RectTransform[] Rules;
            public Image Icon, Halo, GemImg;
            public Text Title;
            public float Hold;
        }

        private readonly List<Card> _cards = new List<Card>();
        private int _index;
        private float _t, _time;
        private float _leave = -1f;
        private bool _fast, _skipAll;
        private CanvasGroup _hint;
        private AudioSource _bed, _sfx;
        private AudioClip _swell, _tick;
        private bool _swellPlayed;

        public bool Done { get; private set; }

        public static IntroWarnings Create(Transform parent)
        {
            var go = new GameObject("Warnings", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            var w = go.AddComponent<IntroWarnings>();
            w.Build();
            return w;
        }

        // ================================================================ Построение

        private void Build()
        {
            _cards.Add(BuildCard(LGIcon.Warning,
                Loc.T("ПРЕДУПРЕЖДЕНИЕ О СВЕТОЧУВСТВИТЕЛЬНОЙ ЭПИЛЕПСИИ"),
                Loc.T("У небольшого числа людей вспышки света, мерцающие изображения и световые узоры могут вызывать эпилептические припадки — даже если раньше таких случаев не было.") + "\n\n" +
                Loc.T("Если во время игры вы почувствуете головокружение, нарушение зрения, подёргивание глаз или мышц, потерю ориентации или любые непроизвольные движения, немедленно прекратите игру и обратитесь к врачу.") + "\n\n" +
                Loc.T("Играйте в хорошо освещённом помещении, делайте перерывы каждый час и не садитесь слишком близко к экрану."),
                null, 9f));
            _cards.Add(BuildCard(LGIcon.Save,
                Loc.T("АВТОСОХРАНЕНИЕ"),
                Loc.T("Игра автоматически сохраняет прогресс по ходу партии и при выходе.") + "\n\n" +
                Loc.T("Не закрывайте игру и не выключайте компьютер, пока идёт сохранение: так можно повредить файл сохранения."),
                null, 5f));
            _cards.Add(BuildCard(LGIcon.Speaker,
                Loc.T("ПЕРЕД НАЧАЛОМ"),
                Loc.T("Все персонажи, народы, организации и события этой игры вымышлены. Любые совпадения с реальными лицами и событиями случайны.") + "\n\n" +
                Loc.T("Для полного погружения рекомендуем играть в наушниках."),
                Loc.F("© {0} SmartBiz. Все права защищены.", System.DateTime.Now.Year), 5f));

            // Подсказка внизу: появляется, когда экран уже можно пропустить
            var hint = Label(transform, Loc.T("Нажмите любую клавишу, чтобы продолжить"), GameFont.MonoMedium, 15, Muted, TextAnchor.MiddleCenter);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, 56f);
            hint.rectTransform.sizeDelta = new Vector2(1200f, 24f);
            hint.Track(3f);
            _hint = hint.gameObject.AddComponent<CanvasGroup>();
            _hint.alpha = 0f;

            for (int i = 0; i < _cards.Count; i++) _cards[i].Group.alpha = 0f;

            // Звук
            _bed = gameObject.AddComponent<AudioSource>();
            _bed.clip = Resources.Load<AudioClip>("Audio/Intro/warn_bed");
            _bed.loop = true;
            _bed.playOnAwake = false;
            _bed.spatialBlend = 0f;
            _bed.volume = 0f;
            if (_bed.clip != null) _bed.Play();
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f;
            _swell = Resources.Load<AudioClip>("Audio/Intro/warn_swell");
            _tick = Resources.Load<AudioClip>("Audio/Intro/warn_tick");
        }

        private Card BuildCard(LGIcon icon, string title, string body, string footer, float hold)
        {
            var root = new GameObject("Card", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1080f, 760f);
            var group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var c = new Card { Group = group, Hold = hold };

            // Значок с тёплым ореолом
            c.Halo = Img(rt, "Halo", FactionSelectScreen.RadialSprite(), new Color(Gold.r, Gold.g, Gold.b, 0f));
            TopCenter(c.Halo.rectTransform, 63f, new Vector2(150f, 150f));
            c.Icon = LGIcons.Create(rt, icon, 46f, new Color(Gold.r, Gold.g, Gold.b, 0f));
            c.Icon.raycastTarget = false;
            TopCenter(c.Icon.rectTransform, 63f, new Vector2(46f, 46f));

            c.Title = Label(rt, title, GameFont.Display, 30, Gold, TextAnchor.UpperCenter);
            Top(c.Title.rectTransform, 112f, 44f);
            c.Title.Track(10f);

            // Латунная линия: растёт от ромба в обе стороны
            float ry = -176f;
            c.Rules = new RectTransform[2];
            for (int s = 0; s < 2; s++)
            {
                var img = Img(rt, "Rule", null, new Color(Gold.r, Gold.g, Gold.b, 0.75f));
                var r = img.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
                r.pivot = new Vector2(s == 0 ? 1f : 0f, 0.5f);
                r.anchoredPosition = new Vector2(s == 0 ? -12f : 12f, ry);
                r.sizeDelta = new Vector2(0f, 1.5f);
                c.Rules[s] = r;
            }
            c.GemImg = Img(rt, "Gem", null, new Color(Gold.r, Gold.g, Gold.b, 0f));
            c.Gem = c.GemImg.rectTransform;
            c.Gem.anchorMin = c.Gem.anchorMax = new Vector2(0.5f, 1f);
            c.Gem.anchoredPosition = new Vector2(0f, ry);
            c.Gem.sizeDelta = new Vector2(8f, 8f);

            // Текст и подпись — своим слоем: появляются чуть позже заголовка и поднимаются на место
            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(rt, false);
            c.BodyRt = (RectTransform)bodyGo.transform;
            Stretch(c.BodyRt);
            c.Body = bodyGo.AddComponent<CanvasGroup>();
            c.Body.alpha = 0f;

            var b = Label(c.BodyRt, body, GameFont.Serif, 23, Cream, TextAnchor.UpperCenter);
            b.horizontalOverflow = HorizontalWrapMode.Wrap;
            b.lineSpacing = 1.2f;
            Top(b.rectTransform, 214f, 420f, 60f);
            if (!string.IsNullOrEmpty(footer))
            {
                var f = Label(c.BodyRt, footer, GameFont.SerifItalic, 18, Muted, TextAnchor.UpperCenter);
                Top(f.rectTransform, 560f, 30f);
            }
            return c;
        }

        // ================================================================ Ход

        private void Play(AudioClip clip, float volume)
        {
            if (clip != null && _sfx != null) _sfx.PlayOneShot(clip, volume);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _time += dt;
            bool finishing = Done || _skipAll || (_index >= _cards.Count - 1 && _leave >= 0f);

            // Эмбиент: плавно входит, гаснет вместе с последним экраном
            if (_bed != null)
            {
                float target = finishing ? 0f : BedVolume;
                _bed.volume = Mathf.MoveTowards(_bed.volume, target, dt * (target > _bed.volume ? 0.5f : 1.0f));
            }

            if (Done || _cards.Count == 0) return;
            var card = _cards[_index];
            _t += dt;
            if (!_swellPlayed) { _swellPlayed = true; Play(_swell, 0.9f); }

            bool pressed = Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            bool esc = Input.GetKeyDown(KeyCode.Escape);
            if (pressed && _t >= MinShow)
            {
                if (esc || _leave >= 0f) _skipAll = true;          // Esc или повторное нажатие — пропустить всё
                if (_leave < 0f) { _leave = 0f; _fast = true; Play(_tick, 0.8f); }
                else if (_skipAll) _fast = true;
            }
            if (_leave < 0f && _t >= FadeIn + card.Hold) { _leave = 0f; _fast = false; }

            AnimateCard(card);

            if (_leave >= 0f)
            {
                _leave += dt;
                float fade = _fast ? SkipFade : FadeOut;
                // при пропуске экран гаснет от текущей яркости (если нажали, пока он ещё проявлялся)
                if (_leave <= dt) card.Group.alpha = Mathf.Max(card.Group.alpha, 0.001f);
                float k = Mathf.Clamp01(_leave / fade);
                card.Group.alpha = Mathf.Min(card.Group.alpha, 1f - Ease(k));
                _hint.alpha = Mathf.Min(_hint.alpha, card.Group.alpha);
                if (_leave >= fade + (_fast ? 0.15f : Gap))   // короткая пауза в темноте между экранами
                {
                    card.Group.alpha = 0f;
                    _index++;
                    _t = 0f;
                    _leave = -1f;
                    _fast = false;
                    _swellPlayed = false;
                    if (_skipAll) _index = _cards.Count;
                    if (_index >= _cards.Count) { Done = true; _hint.alpha = 0f; }
                }
                return;
            }

            card.Group.alpha = Ease(Mathf.Clamp01(_t / FadeIn));
            _hint.alpha = Mathf.MoveTowards(_hint.alpha, _t >= MinShow ? 0.75f : 0f, dt * 1.2f);
        }

        /// <summary>Появление элементов карточки по очереди — по времени _t от начала экрана. Только плавные кривые.</summary>
        private void AnimateCard(Card c)
        {
            float t = _t;

            // Значок: мягко проявляется и чуть подрастает; ореол тёплый и спокойно «дышит»
            float ic = EaseOut(Mathf.Clamp01(t / 1.2f));
            c.Icon.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.88f, 1f, ic);
            c.Icon.color = new Color(Gold.r, Gold.g, Gold.b, ic);
            float breathe = 0.13f + 0.035f * Mathf.Sin(_time * 1.3f);
            c.Halo.color = new Color(Gold.r, Gold.g, Gold.b, breathe * Ease(Mathf.Clamp01(t / 1.6f)));

            // Заголовок: буквы плавно сходятся из широкой разрядки
            c.Title.Track(Mathf.Lerp(10f, 4f, EaseOut(Mathf.Clamp01(t / 2.2f))));

            // Ромб поворачивается на место, от него в обе стороны растёт латунная линия
            float g = EaseOut(Mathf.Clamp01((t - 0.3f) / 0.9f));
            c.GemImg.color = new Color(Gold.r, Gold.g, Gold.b, g);
            c.Gem.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, g);
            c.Gem.localRotation = Quaternion.Euler(0f, 0f, 45f + 90f * (1f - g));
            float rule = EaseOut(Mathf.Clamp01((t - 0.45f) / 1.4f));
            for (int s = 0; s < 2; s++) c.Rules[s].sizeDelta = new Vector2(RuleW * 0.5f * rule, 1.5f);

            // Текст — следом, плавно поднимаясь на место
            float b = EaseOut(Mathf.Clamp01((t - 0.6f) / 1.4f));
            c.Body.alpha = b;
            c.BodyRt.anchoredPosition = new Vector2(0f, -12f * (1f - b));
        }

        // ================================================================ Помощники

        private static float Ease(float x) => x * x * (3f - 2f * x);
        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

        private static Image Img(Transform parent, string name, Sprite sprite, Color color)
        {
            var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            img.transform.SetParent(parent, false);
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Text Label(Transform parent, string text, Font font, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Txt", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.text = text;
            t.alignment = anchor;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>Полоса во всю ширину родителя на расстоянии top от его верха.</summary>
        private static void Top(RectTransform rt, float top, float height, float side = 0f)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(side, -top - height);
            rt.offsetMax = new Vector2(-side, -top);
        }

        /// <summary>Элемент по центру сверху: центр на расстоянии centerFromTop от верха.</summary>
        private static void TopCenter(RectTransform rt, float centerFromTop, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -centerFromTop);
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
