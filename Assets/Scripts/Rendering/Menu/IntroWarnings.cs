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
    /// Каждый экран плавно проявляется и гаснет; любая клавиша (не раньше MinShow) — к следующему.
    /// Пока идут предупреждения, IntroVideo готовит ролик в фоне. Done — все экраны показаны.
    /// Разметка — в «эталонных» 1920×1080 (холст IntroVideo масштабируется CanvasScaler).
    /// </summary>
    public sealed class IntroWarnings : MonoBehaviour
    {
        private const float FadeIn = 0.9f, FadeOut = 0.7f, MinShow = 1.2f;

        private static readonly Color Gold = new Color(0.906f, 0.757f, 0.439f);
        private static readonly Color Cream = new Color(0.925f, 0.898f, 0.827f);
        private static readonly Color Muted = new Color(0.663f, 0.639f, 0.573f);

        private sealed class Card
        {
            public CanvasGroup Group;
            public float Hold;
        }

        private readonly List<Card> _cards = new List<Card>();
        private int _index;
        private float _t;
        private float _leave = -1f;
        private CanvasGroup _hint;

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

            var ic = LGIcons.Create(rt, icon, 46f, Gold);
            ic.raycastTarget = false;
            ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            ic.rectTransform.pivot = new Vector2(0.5f, 1f);
            ic.rectTransform.anchoredPosition = new Vector2(0f, -40f);
            ic.rectTransform.sizeDelta = new Vector2(46f, 46f);

            var t = Label(rt, title, GameFont.Display, 30, Gold, TextAnchor.UpperCenter);
            Top(t.rectTransform, 112f, 44f);
            t.Track(4f);

            // Латунная линия с ромбом
            float ry = -176f;
            Line(rt, new Vector2(-190f, ry), 360f);
            var gem = new GameObject("Gem", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            gem.transform.SetParent(rt, false);
            gem.color = Gold;
            gem.raycastTarget = false;
            gem.rectTransform.anchorMin = gem.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            gem.rectTransform.anchoredPosition = new Vector2(0f, ry);
            gem.rectTransform.sizeDelta = new Vector2(8f, 8f);
            gem.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            var b = Label(rt, body, GameFont.Serif, 23, Cream, TextAnchor.UpperCenter);
            b.horizontalOverflow = HorizontalWrapMode.Wrap;
            b.lineSpacing = 1.2f;
            Top(b.rectTransform, 214f, 420f, 60f);

            if (!string.IsNullOrEmpty(footer))
            {
                var f = Label(rt, footer, GameFont.SerifItalic, 18, Muted, TextAnchor.UpperCenter);
                Top(f.rectTransform, 560f, 30f);
            }
            return new Card { Group = group, Hold = hold };
        }

        private void Update()
        {
            if (Done || _cards.Count == 0) return;
            float dt = Time.unscaledDeltaTime;
            var card = _cards[_index];
            _t += dt;

            bool pressed = Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            if (_leave < 0f && (_t >= FadeIn + card.Hold || (pressed && _t >= MinShow))) _leave = 0f;

            if (_leave >= 0f)
            {
                _leave += dt;
                card.Group.alpha = 1f - Mathf.Clamp01(_leave / FadeOut);
                _hint.alpha = Mathf.Min(_hint.alpha, card.Group.alpha);
                if (_leave >= FadeOut + 0.25f)   // короткая пауза в темноте между экранами
                {
                    card.Group.alpha = 0f;
                    _index++;
                    _t = 0f;
                    _leave = -1f;
                    if (_index >= _cards.Count) { Done = true; _hint.alpha = 0f; }
                }
                return;
            }

            card.Group.alpha = Ease(Mathf.Clamp01(_t / FadeIn));
            _hint.alpha = Mathf.MoveTowards(_hint.alpha, _t >= MinShow ? 0.75f : 0f, dt * 1.5f);
        }

        // ================================================================ Помощники

        private static float Ease(float x) => x * x * (3f - 2f * x);

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

        /// <summary>Полоса во всю ширину карточки на расстоянии top от её верха.</summary>
        private static void Top(RectTransform rt, float top, float height, float side = 0f)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(side, -top - height);
            rt.offsetMax = new Vector2(-side, -top);
        }

        /// <summary>Две половинки латунной линии по сторонам от ромба.</summary>
        private static void Line(RectTransform parent, Vector2 center, float width)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                var img = new GameObject("Rule", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                img.transform.SetParent(parent, false);
                img.color = new Color(Gold.r, Gold.g, Gold.b, 0.7f);
                img.raycastTarget = false;
                var r = img.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
                r.pivot = new Vector2(s < 0 ? 1f : 0f, 0.5f);
                r.anchoredPosition = new Vector2(s * 12f, center.y);
                r.sizeDelta = new Vector2(width * 0.5f, 1.5f);
            }
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
