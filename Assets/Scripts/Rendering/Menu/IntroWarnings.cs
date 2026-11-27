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
    ///
    /// Эффекты: медленно плывущее звёздное поле, парящие золотые пылинки, тёплое «дышащее» свечение за текстом.
    /// Появление экрана — лёгкий наезд, значок вспыхивает с ореолом, буквы заголовка сходятся из широкой разрядки,
    /// латунная линия вырастает от ромба, по заголовку проходит блик, текст поднимается следом; уход — отдаление.
    /// Звук (Resources/Audio/Intro, Tools/intro_sfx/generate.py): тихий эмбиент под всеми экранами,
    /// мягкое нарастание с глубоким ударом при появлении экрана, стеклянный тон при пропуске клавишей.
    /// Пока идут предупреждения, IntroVideo готовит ролик в фоне. Done — все экраны показаны.
    /// Разметка — в «эталонных» 1920×1080 (холст IntroVideo масштабируется CanvasScaler).
    /// </summary>
    public sealed class IntroWarnings : MonoBehaviour
    {
        private const float FadeIn = 0.9f, FadeOut = 0.8f, MinShow = 1.2f, RuleW = 360f;
        private const float BedVolume = 0.8f;
        private const int DustCount = 46;

        private static readonly Color Gold = new Color(0.906f, 0.757f, 0.439f);
        private static readonly Color Cream = new Color(0.925f, 0.898f, 0.827f);
        private static readonly Color Muted = new Color(0.663f, 0.639f, 0.573f);

        private sealed class Card
        {
            public RectTransform Root;
            public CanvasGroup Group, Body;
            public RectTransform BodyRt, Gem, Sweep;
            public RectTransform[] Rules;
            public Image Icon, Halo, SweepImg;
            public Text Title;
            public float Hold;
        }

        private struct Mote
        {
            public RectTransform Rt;
            public Image Img;
            public Vector2 Pos, Vel;
            public float Size, Phase, Speed, Alpha;
        }

        private readonly List<Card> _cards = new List<Card>();
        private int _index;
        private float _t, _time;
        private float _leave = -1f;
        private CanvasGroup _hint, _fx;
        private RawImage _stars;
        private Image _glow;
        private Mote[] _dust;
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
            BuildBackdrop();

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

        /// <summary>Фон: звёздное поле, тёплое свечение в центре и пылинки. Всё — под карточками.</summary>
        private void BuildBackdrop()
        {
            var fxGo = new GameObject("Fx", typeof(RectTransform));
            fxGo.transform.SetParent(transform, false);
            Stretch((RectTransform)fxGo.transform);
            _fx = fxGo.AddComponent<CanvasGroup>();
            _fx.alpha = 0f;
            _fx.blocksRaycasts = false;

            _stars = new GameObject("Stars", typeof(RectTransform)).AddComponent<RawImage>();
            _stars.transform.SetParent(fxGo.transform, false);
            Stretch(_stars.rectTransform);
            _stars.texture = StarTexture();
            _stars.uvRect = new Rect(0f, 0f, 1.6f, 0.9f);
            _stars.color = new Color(1f, 1f, 1f, 0.55f);
            _stars.raycastTarget = false;

            _glow = Img(fxGo.transform, "Glow", FactionSelectScreen.RadialSprite(), new Color(Gold.r, Gold.g, Gold.b, 0.07f));
            _glow.rectTransform.anchorMin = _glow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _glow.rectTransform.sizeDelta = new Vector2(1500f, 1000f);

            _dust = new Mote[DustCount];
            var rnd = new System.Random(1977);
            var dot = FactionSelectScreen.RadialSprite();
            for (int i = 0; i < DustCount; i++)
            {
                var img = Img(fxGo.transform, "Mote", dot, Gold);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                float size = 4f + (float)rnd.NextDouble() * 9f;
                img.rectTransform.sizeDelta = new Vector2(size, size);
                _dust[i] = new Mote
                {
                    Rt = img.rectTransform, Img = img, Size = size,
                    Pos = new Vector2((float)rnd.NextDouble() * 2000f - 1000f, (float)rnd.NextDouble() * 1160f - 580f),
                    Vel = new Vector2((float)rnd.NextDouble() * 10f - 5f, 6f + (float)rnd.NextDouble() * 14f),
                    Phase = (float)rnd.NextDouble() * 6.28f,
                    Speed = 0.6f + (float)rnd.NextDouble() * 1.4f,
                    Alpha = 0.12f + (float)rnd.NextDouble() * 0.35f
                };
            }
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
            var c = new Card { Root = rt, Group = group, Hold = hold };

            // Значок с ореолом
            c.Halo = Img(rt, "Halo", FactionSelectScreen.RadialSprite(), new Color(Gold.r, Gold.g, Gold.b, 0f));
            TopCenter(c.Halo.rectTransform, 63f, new Vector2(170f, 170f));
            c.Icon = LGIcons.Create(rt, icon, 46f, Gold);
            c.Icon.raycastTarget = false;
            TopCenter(c.Icon.rectTransform, 63f, new Vector2(46f, 46f));

            c.Title = Label(rt, title, GameFont.Display, 30, Gold, TextAnchor.UpperCenter);
            Top(c.Title.rectTransform, 112f, 44f);
            c.Title.Track(14f);

            // Блик по заголовку: мягкая светлая полоса в маске по строке заголовка
            var clip = new GameObject("SweepClip", typeof(RectTransform), typeof(RectMask2D));
            clip.transform.SetParent(rt, false);
            Top((RectTransform)clip.transform, 104f, 58f, 40f);
            c.SweepImg = Img(clip.transform, "Sweep", FactionSelectScreen.RadialSprite(), new Color(1f, 0.95f, 0.82f, 0f));
            c.Sweep = c.SweepImg.rectTransform;
            c.Sweep.anchorMin = c.Sweep.anchorMax = new Vector2(0f, 0.5f);
            c.Sweep.sizeDelta = new Vector2(220f, 120f);

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
            var gem = Img(rt, "Gem", null, Gold);
            gem.rectTransform.anchorMin = gem.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            gem.rectTransform.anchoredPosition = new Vector2(0f, ry);
            gem.rectTransform.sizeDelta = new Vector2(8f, 8f);
            c.Gem = gem.rectTransform;

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
            bool finishing = Done || (_index >= _cards.Count - 1 && _leave >= 0f);

            // Эмбиент и фон: плавно входят, гаснут вместе с последним экраном
            if (_bed != null)
            {
                float target = finishing ? 0f : BedVolume;
                _bed.volume = Mathf.MoveTowards(_bed.volume, target, dt * (target > _bed.volume ? 0.5f : 1.2f));
            }
            _fx.alpha = Mathf.MoveTowards(_fx.alpha, finishing ? 0f : 1f, dt * (finishing ? 1.3f : 0.6f));
            AnimateBackdrop(dt);

            if (Done || _cards.Count == 0) return;
            var card = _cards[_index];
            _t += dt;
            if (!_swellPlayed) { _swellPlayed = true; Play(_swell, 0.9f); }

            bool pressed = Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            if (_leave < 0f && pressed && _t >= MinShow) { _leave = 0f; Play(_tick, 0.8f); }
            if (_leave < 0f && _t >= FadeIn + card.Hold) _leave = 0f;

            AnimateCard(card);

            if (_leave >= 0f)
            {
                _leave += dt;
                float k = Mathf.Clamp01(_leave / FadeOut);
                card.Group.alpha = 1f - Ease(k);
                card.Root.localScale = Vector3.one * (1f + 0.025f * Ease(k));   // уход — лёгкое отдаление на зрителя
                _hint.alpha = Mathf.Min(_hint.alpha, card.Group.alpha);
                if (_leave >= FadeOut + 0.3f)   // короткая пауза в темноте между экранами
                {
                    card.Group.alpha = 0f;
                    _index++;
                    _t = 0f;
                    _leave = -1f;
                    _swellPlayed = false;
                    if (_index >= _cards.Count) { Done = true; _hint.alpha = 0f; }
                }
                return;
            }

            card.Group.alpha = Ease(Mathf.Clamp01(_t / FadeIn));
            card.Root.localScale = Vector3.one * Mathf.Lerp(1.035f, 1f, EaseOut(Mathf.Clamp01(_t / 1.8f)));   // мягкий наезд
            _hint.alpha = Mathf.MoveTowards(_hint.alpha, _t >= MinShow ? 0.75f : 0f, dt * 1.5f);
        }

        /// <summary>Появление элементов карточки по очереди — по времени _t от начала экрана.</summary>
        private void AnimateCard(Card c)
        {
            float t = _t;

            // Значок: вспышка ореола и «выпрыгивание», потом спокойное дыхание
            float pop = EaseOutBack(Mathf.Clamp01(t / 0.7f));
            c.Icon.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1f, pop);
            float flash = Mathf.Exp(-Mathf.Max(0f, t - 0.15f) * 3f) * Mathf.Clamp01(t / 0.15f);
            float breathe = 0.16f + 0.06f * Mathf.Sin(_time * 1.8f);
            c.Halo.color = new Color(Gold.r, Gold.g, Gold.b, Mathf.Clamp01(breathe * Mathf.Clamp01(t / 0.6f) + 0.5f * flash));
            c.Halo.rectTransform.localScale = Vector3.one * (1f + 0.35f * flash);

            // Заголовок: буквы сходятся из широкой разрядки
            c.Title.Track(Mathf.Lerp(14f, 4f, EaseOut(Mathf.Clamp01(t / 1.5f))));

            // Блик проходит по заголовку один раз
            float sw = Mathf.Clamp01((t - 0.7f) / 1.3f);
            float swA = sw <= 0f || sw >= 1f ? 0f : Mathf.Sin(sw * Mathf.PI);
            c.SweepImg.color = new Color(1f, 0.95f, 0.82f, 0.22f * swA);
            c.Sweep.anchoredPosition = new Vector2(Mathf.Lerp(-200f, 1200f, sw), 0f);

            // Линия растёт от ромба, ромб проворачивается на место
            float rule = EaseOut(Mathf.Clamp01((t - 0.35f) / 1.0f));
            for (int s = 0; s < 2; s++) c.Rules[s].sizeDelta = new Vector2(RuleW * 0.5f * rule, 1.5f);
            float g = EaseOutBack(Mathf.Clamp01((t - 0.25f) / 0.6f));
            c.Gem.localScale = Vector3.one * g;
            c.Gem.localRotation = Quaternion.Euler(0f, 0f, 45f + (1f - Mathf.Clamp01((t - 0.25f) / 0.6f)) * 180f);

            // Текст — следом, поднимаясь на место
            float b = Ease(Mathf.Clamp01((t - 0.55f) / 1.0f));
            c.Body.alpha = b;
            c.BodyRt.anchoredPosition = new Vector2(0f, -16f * (1f - b));
        }

        private void AnimateBackdrop(float dt)
        {
            if (_stars != null)
            {
                var r = _stars.uvRect;
                r.x += dt * 0.004f;
                r.y += dt * 0.0015f;
                _stars.uvRect = r;
            }
            if (_glow != null)
            {
                float k = 0.065f + 0.02f * Mathf.Sin(_time * 0.7f);
                _glow.color = new Color(Gold.r, Gold.g, Gold.b, k);
                _glow.rectTransform.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(_time * 0.45f));
            }
            if (_dust == null) return;
            for (int i = 0; i < _dust.Length; i++)
            {
                ref var m = ref _dust[i];
                m.Pos += m.Vel * dt;
                m.Pos.x += Mathf.Sin(_time * 0.4f + m.Phase) * 6f * dt;
                if (m.Pos.y > 600f) { m.Pos.y = -600f; m.Pos.x = Random.Range(-1000f, 1000f); }
                if (m.Pos.x > 1020f) m.Pos.x = -1020f;
                if (m.Pos.x < -1020f) m.Pos.x = 1020f;
                m.Rt.anchoredPosition = m.Pos;
                float tw = 0.55f + 0.45f * Mathf.Sin(_time * m.Speed + m.Phase);
                m.Img.color = new Color(Gold.r, Gold.g, Gold.b, m.Alpha * tw);
            }
        }

        // ================================================================ Помощники

        private static float Ease(float x) => x * x * (3f - 2f * x);
        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        private static Texture2D s_stars;

        /// <summary>Звёздное поле: редкие мягкие точки разной яркости, бесшовное (для прокрутки uvRect).</summary>
        private static Texture2D StarTexture()
        {
            if (s_stars != null) return s_stars;
            const int size = 512;
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
            var rnd = new System.Random(2200);
            for (int n = 0; n < 340; n++)
            {
                int cx = rnd.Next(size), cy = rnd.Next(size);
                float bright = Mathf.Pow((float)rnd.NextDouble(), 2.2f);
                float rad = bright > 0.75f ? 1.6f : 0.9f;
                var tint = rnd.NextDouble() < 0.25 ? new Color(1f, 0.88f, 0.7f) : rnd.NextDouble() < 0.4 ? new Color(0.75f, 0.85f, 1f) : Color.white;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d / (rad + 0.6f)) * (0.25f + 0.75f * bright);
                        if (a <= 0f) continue;
                        int x = (cx + dx + size) % size, y = (cy + dy + size) % size;
                        var p = px[y * size + x];
                        byte av = (byte)Mathf.Max(p.a, Mathf.RoundToInt(a * 255f));
                        px[y * size + x] = new Color32((byte)(tint.r * 255), (byte)(tint.g * 255), (byte)(tint.b * 255), av);
                    }
            }
            s_stars = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "IntroStars" };
            s_stars.SetPixels32(px);
            s_stars.Apply();
            return s_stars;
        }

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
