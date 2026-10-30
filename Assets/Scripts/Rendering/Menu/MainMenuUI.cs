using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Cam;
using StellarisClone.Core;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Главное меню: живая галактика на фоне (облёт камеры), анимированный логотип по центру,
    /// под ним три карточки (новая игра · продолжить партию с живым портретом правителя · загрузка)
    /// и полоса «Что нового». Пункты открывают окно по центру — карточки на это время уезжают вниз:
    /// «Новая игра» (параметры + предпросмотр галактики), «Загрузка», «Настройки», «Об игре», «Что нового».
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        public static MainMenuUI Instance { get; private set; }

        private enum Panel { None, NewGame, Load, Settings, About, News }

        /// <summary>Версия берётся из настроек проекта (Player → Version), чтобы меню и сборка не расходились.</summary>
        public static string Version => "v" + GameVersion.Current + " · " + Loc.T("ранний доступ");

        private RectTransform _root;
        private RectTransform _window, _body;
        private Text _winTitle, _winSub;
        private Image _winIcon;
        private Panel _open = Panel.None;
        private readonly Dictionary<Panel, GameObject> _pages = new Dictionary<Panel, GameObject>();

        // Логотип
        private readonly List<Letter> _title = new List<Letter>();
        private readonly List<Letter> _subtitle = new List<Letter>();
        private RectTransform _underline;
        private Image _titleGlow;
        private float _t;

        // Новая игра
        private NewGameSettings _ng;
        private GalaxyPreview _preview;
        private InputField _seedInput;
        private Text _ngGoals;

        private struct Letter
        {
            public Text Text;
            public RectTransform Rt;
            public float Delay;
            public Vector2 Rest;
        }

        // ================================================================ Жизненный цикл

        private void Awake()
        {
            Instance = this;
            _ng = new NewGameSettings { Seed = NewGameSettings.RandomSeed() };
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            BuildCanvas();
            BuildBackdrop();
            // Живое небо за галактикой и пылинки под кнопками; игровые подсказки карты в меню скрыты
            gameObject.AddComponent<MenuAtmosphere>().Init(_root);
            BuildLogo();
            BuildHero();
            BuildCorners();
            BuildWindow();
            BuildFooter();

            var cam = Camera.main;
            if (cam != null && cam.GetComponent<MainMenuCamera>() == null) cam.gameObject.AddComponent<MainMenuCamera>();
        }

        private void Update()
        {
            _t += Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            AnimateLogo();
            AnimateHero();
            FitWindow();

            if (Input.GetKeyDown(KeyCode.Escape) && _open != Panel.None && !IsTyping()) Close();
            // Enter — сразу в последнюю партию
            if (_open == Panel.None && _continueCard != null && !_continueCard.Disabled && _heroK > 0.9f && !IsTyping()
                && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                _continueCard.Click();
        }

        private static bool IsTyping()
        {
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return sel != null && sel.GetComponent<InputField>() != null && sel.GetComponent<InputField>().isFocused;
        }

        // ================================================================ Канвас и фон

        private void BuildCanvas()
        {
            var go = new GameObject("MainMenuCanvas");
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 800;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<UIScaleTarget>();
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;

            // Ловит все клики — сквозь меню нельзя выбрать звезду
            var blocker = LGBuild.Panel(_root, "Blocker", new Color(0, 0, 0, 0), raycast: true);
            blocker.rectTransform.Stretch();
        }

        private void BuildBackdrop()
        {
            var shade = LGBuild.Rect(_root, "Shade");
            shade.Stretch();
            LG.Ignore(shade.gameObject);

            var ink = new Color(0.006f, 0.016f, 0.026f, 1f);

            // Низ — под карточками и полосой новостей
            var bottom = LGBuild.Panel(shade, "Bottom", ink);
            bottom.sprite = MenuArt.VerticalFade;
            bottom.rectTransform.anchorMin = new Vector2(0, 0);
            bottom.rectTransform.anchorMax = new Vector2(1, 0.62f);
            bottom.rectTransform.offsetMin = bottom.rectTransform.offsetMax = Vector2.zero;

            // Виньетка
            var vig = LGBuild.Panel(shade, "Vignette", ink);
            vig.sprite = MenuArt.Vignette;
            vig.rectTransform.Stretch(-40, -40, -40, -40);
        }

        // ================================================================ Логотип

        private void BuildLogo()
        {
            var logo = LGBuild.Rect(_root, "Logo");
            logo.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -112), new Vector2(1400, 230));
            LG.Ignore(logo.gameObject);
            _logo = logo;
            _logoCg = logo.gameObject.AddComponent<CanvasGroup>();
            _logoCg.blocksRaycasts = false;

            _titleGlow = LGBuild.Panel(logo, "Glow", new Color(0.30f, 0.95f, 0.86f, 0f));
            _titleGlow.sprite = MenuArt.SoftDisc;
            _titleGlow.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(1500, 360));

            // Слова строятся слева направо — ширину узнаём после сборки и центрируем контейнер
            var word = LGBuild.Rect(logo, "Title");
            float tw = BuildTrackedWord(word, "ASTROPOLITY", GameFont.Bold, 124, FontStyle.Bold, 16f, _title, 0.35f, 0.055f);
            word.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(tw, 150));

            var sub = LGBuild.Rect(logo, "Subtitle");
            float sw = BuildTrackedWord(sub, Loc.T("ГАЛАКТИЧЕСКАЯ СТРАТЕГИЯ"), GameFont.Regular, 18, FontStyle.Normal, 12f, _subtitle, 1.15f, 0.022f);
            sub.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -160), new Vector2(sw, 26));
            foreach (var l in _subtitle) l.Text.color = new Color(0.30f, 0.95f, 0.86f, 0f);

            var line = LGBuild.Panel(logo, "Underline", new Color(0.30f, 0.95f, 0.86f, 0.75f));
            _underline = line.rectTransform;
            _underline.At(new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -200), new Vector2(0, 2));
            LG.Line(line.gameObject, hairline: true);
        }

        /// <summary>Слово по буквам (каждая анимируется отдельно); возвращает ширину слова.</summary>
        private static float BuildTrackedWord(RectTransform parent, string word, Font font, int size, FontStyle style,
                                              float tracking, List<Letter> output, float baseDelay, float stagger)
        {
            font.RequestCharactersInTexture(word, size, style);
            float x = 0f;
            int i = 0;
            foreach (char c in word)
            {
                float adv = font.GetCharacterInfo(c, out var ci, size, style) ? ci.advance : size * 0.55f;
                if (c == ' ') { x += adv + tracking; continue; }

                var rt = LGBuild.Rect(parent, "L_" + c);
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(adv + 24f, size * 1.6f);
                var rest = new Vector2(x + adv * 0.5f, 0f);
                rt.anchoredPosition = rest;

                var t = rt.gameObject.AddComponent<Text>();
                t.font = font;
                t.fontSize = size;
                t.fontStyle = style;
                t.text = c.ToString();
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.raycastTarget = false;
                t.color = new Color(1, 1, 1, 0);
                var sh = rt.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0f, 0.02f, 0.04f, 0.6f);
                sh.effectDistance = new Vector2(0f, -2f);

                output.Add(new Letter { Text = t, Rt = rt, Delay = baseDelay + i * stagger, Rest = rest });
                x += adv + tracking;
                i++;
            }
            return Mathf.Max(1f, x - tracking);
        }

        private void AnimateLogo()
        {
            var primary = UIManager.DS.TextPrimary;
            var neon = UIManager.DS.NeonCyan;

            // Блик, пробегающий по буквам каждые 7 секунд
            float shineT = _t - 2.2f;
            float sweep = shineT > 0f ? (shineT % 7f) / 1.3f : -1f;

            for (int i = 0; i < _title.Count; i++)
            {
                var l = _title[i];
                float k = Mathf.Clamp01((_t - l.Delay) / 0.75f);
                float e = LGEase.OutCubic(k);
                float s = Mathf.LerpUnclamped(1.35f, 1f, LGEase.OutBack(k, 1.4f));
                l.Rt.anchoredPosition = l.Rest + new Vector2(0f, 26f * (1f - e));
                l.Rt.localScale = new Vector3(s, s, 1f);

                float u = _title.Count > 1 ? i / (float)(_title.Count - 1) : 0f;
                float shine = sweep >= 0f && sweep <= 1.4f ? Mathf.Exp(-Mathf.Pow((u - (sweep - 0.2f)) / 0.09f, 2f)) : 0f;
                var flash = Color.Lerp(Color.white, primary, Mathf.Clamp01(k * 1.6f));
                var c = Color.Lerp(flash, Color.Lerp(neon, Color.white, 0.55f), shine * 0.85f);
                c.a = e;
                l.Text.color = c;
            }

            for (int i = 0; i < _subtitle.Count; i++)
            {
                var l = _subtitle[i];
                float k = Mathf.Clamp01((_t - l.Delay) / 0.3f);
                var c = neon;
                c.a = 0.85f * LGEase.OutCubic(k);
                l.Text.color = c;
                l.Rt.anchoredPosition = l.Rest + new Vector2(-8f * (1f - LGEase.OutCubic(k)), 0f);
            }

            float lineK = LGEase.InOutCubic(Mathf.Clamp01((_t - 1.0f) / 0.9f));
            _underline.sizeDelta = new Vector2(520f * lineK, 2f);

            float glowIn = LGEase.OutCubic(Mathf.Clamp01((_t - 0.3f) / 1.5f));
            _titleGlow.color = new Color(neon.r, neon.g, neon.b, glowIn * (0.10f + 0.035f * Mathf.Sin(_t * 0.9f)));
        }

        // ================================================================ Карточки по центру

        private RectTransform _logo, _hero, _corners;
        private CanvasGroup _logoCg, _heroCg;
        private float _heroK = 1f, _heroV;   // 1 — карточки на месте, 0 — убраны вниз (открыто окно)
        private MenuCard _continueCard;
        private NewsWindow _newsWin;
        private Text _loadCaption, _continueMeta;

        private static readonly Color LoadAccent = new Color(0.55f, 0.80f, 1f);

        private void BuildHero()
        {
            _hero = LGBuild.Rect(_root, "Hero");
            _hero.Stretch();
            _heroCg = _hero.gameObject.AddComponent<CanvasGroup>();

            var row = LGBuild.Rect(_hero, "Cards");
            row.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -78), new Vector2(1540, 400));

            var latest = SaveSystem.Latest();
            if (latest != null)
            {
                _continueCard = BuildContinueCard(row, latest);
                SideCard(row, -1, Loc.T("НОВАЯ ИГРА"), Loc.T("Создать галактику\nи выбрать цивилизацию"),
                         Resources.Load<Texture2D>("Menu/menu_planet"), new Rect(0.38f, 0.03f, 0.6f, 0.94f),
                         UIManager.DS.NeonCyan, () => Open(Panel.NewGame), 0.75f);
            }
            else
            {
                BuildNewGameCard(row);
                SideCard(row, -1, Loc.T("ОБ ИГРЕ"), Loc.T("Как играть\nи пути к победе"),
                         Resources.Load<Texture2D>("Menu/menu_bg"), new Rect(0.55f, 0.25f, 0.4f, 0.62f),
                         UIManager.DS.Gold, () => Open(Panel.About), 0.75f);
            }
            _loadCaption = SideCard(row, 1, Loc.T("ЗАГРУЗИТЬ"), LoadCaption(SaveSystem.List().Count),
                                    Resources.Load<Texture2D>("Menu/menu_bg"), new Rect(0.04f, 0.3f, 0.4f, 0.62f),
                                    LoadAccent, () => Open(Panel.Load), 0.85f);
            BuildNewsStrip();
        }

        /// <summary>Боковая карточка 380×330: арт сверху, название и подпись снизу. side −1 — слева, 1 — справа.</summary>
        private Text SideCard(RectTransform row, int side, string title, string caption, Texture art, Rect uv, Color accent,
                              System.Action onClick, float delay)
        {
            var c = MenuCard.Create(row, title, new Vector2(380, 330), new Vector2(side * 578f, 0f), accent, false, onClick);
            c.AppearDelay = delay;
            c.SetArt(art, uv);
            var t = LGBuild.Label(c.Content, title, 30, UIManager.DS.TextPrimary, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.Stretch(32, 92, 24, 0);
            var cap = LGBuild.Label(c.Content, caption, 16, new Color(0.66f, 0.76f, 0.80f), TextAnchor.UpperLeft, wrap: true);
            cap.rectTransform.anchorMin = Vector2.zero;
            cap.rectTransform.anchorMax = new Vector2(1, 0);
            cap.rectTransform.pivot = new Vector2(0.5f, 0);
            cap.rectTransform.offsetMin = new Vector2(32, 26);
            cap.rectTransform.offsetMax = new Vector2(-24, 84);
            c.Title = t;
            return cap;
        }

        /// <summary>Главная карточка 700×400: последняя партия — правитель справа, империя, дата и цифры, кнопка «В игру».</summary>
        private MenuCard BuildContinueCard(RectTransform row, SaveEntry e)
        {
            var leader = LeaderPortraits.ForFaction(e.Meta.Empire);
            Color accent = leader != null ? leader.Accent : UIManager.DS.NeonCyan;
            var c = MenuCard.Create(row, "Continue", new Vector2(700, 400), Vector2.zero, accent, true, () =>
            {
                var l = SaveSystem.Latest();
                if (l != null) SaveSystem.LoadSlot(l.Slot);
            });
            c.AppearDelay = 0.6f;

            // Правитель — живой портрет (моргает, огни дышат), мягко растворяется к тексту
            if (leader != null)
            {
                var host = LGBuild.Rect(c.ArtMask, "Leader");
                host.anchorMin = new Vector2(1, 0);
                host.anchorMax = new Vector2(1, 1);
                host.pivot = new Vector2(1, 0.5f);
                host.sizeDelta = new Vector2(440, 0);
                host.anchoredPosition = Vector2.zero;
                c.Portrait = LeaderPortraitView.Create(host, leader, 1.55f, new Vector4(0.5f, 0f, 0f, 0f), false, true, false);
            }

            var chip = LGBuild.Panel(c.Content, "Chip", new Color(accent.r * 0.2f, accent.g * 0.2f, accent.b * 0.2f, 0.9f));
            chip.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -40), new Vector2(214, 28));
            LG.Chip(chip.gameObject, new Color(accent.r, accent.g, accent.b, 0.5f));
            var ct = LGBuild.Label(chip.transform, Loc.T("ПРОДОЛЖИТЬ ПАРТИЮ"), 12, Color.Lerp(accent, Color.white, 0.35f), TextAnchor.MiddleCenter, bold: true);
            ct.rectTransform.Stretch();

            var name = LGBuild.Label(c.Content, e.Meta.Empire, 44, Color.white, TextAnchor.UpperLeft, bold: true, wrap: true);
            name.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -84), new Vector2(340, 112));
            name.resizeTextForBestFit = true; name.resizeTextMinSize = 26; name.resizeTextMaxSize = 44;
            name.lineSpacing = 0.92f;
            c.Title = name;

            _continueMeta = LGBuild.Label(c.Content, ContinueCaption(e), 16, new Color(0.68f, 0.78f, 0.82f), TextAnchor.UpperLeft);
            _continueMeta.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -202), new Vector2(380, 22));

            (int v, string k)[] stats =
            {
                (e.Meta.Systems, Loc.T("СИСТЕМ")), (e.Meta.Population, Loc.T("НАСЕЛЕНИЕ")), (e.Meta.Techs, Loc.T("ТЕХНОЛОГИЙ"))
            };
            float x = 40f;
            foreach (var (v, k) in stats)
            {
                var n = LGBuild.Label(c.Content, v.ToString(), 28, Color.Lerp(accent, Color.white, 0.15f), TextAnchor.UpperLeft, bold: true);
                n.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 132), new Vector2(110, 34));
                var l = LGBuild.Label(c.Content, k, 11, UIManager.DS.TextMuted, TextAnchor.UpperLeft, bold: true);
                l.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 112), new Vector2(120, 18));
                x += 112f;
            }

            // «Кнопка» — часть карточки (кликается вся карточка); светится и по ней пробегает блик
            var go = LGBuild.Panel(c.Content, "Go", Color.Lerp(accent, Color.white, 0.2f));
            go.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 36), new Vector2(300, 56));
            LG.Fill(go.gameObject);
            var gt = LGBuild.Label(go.transform, Loc.T("В ИГРУ"), 19, new Color(0.01f, 0.08f, 0.10f), TextAnchor.MiddleCenter, bold: true);
            gt.rectTransform.Stretch();
            InspSweep.Add(go.gameObject, 4.5f, 0.35f);
            c.Glow = go.rectTransform;
            return c;
        }

        /// <summary>Сохранений нет — в центре большая «Новая игра» с планетой во всю карточку.</summary>
        private void BuildNewGameCard(RectTransform row)
        {
            var c = MenuCard.Create(row, "NewGame", new Vector2(700, 400), Vector2.zero, UIManager.DS.NeonCyan, true, () => Open(Panel.NewGame));
            c.AppearDelay = 0.6f;
            c.SetArt(Resources.Load<Texture2D>("Menu/menu_planet"), new Rect(0.25f, 0.06f, 0.75f, 0.75f * 1.792f / 1.75f));
            var t = LGBuild.Label(c.Content, Loc.T("НОВАЯ ИГРА"), 48, Color.white, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -150), new Vector2(560, 60));
            c.Title = t;
            var cap = LGBuild.Label(c.Content, Loc.T("Создайте галактику, выберите цивилизацию\nи поведите её к звёздам"), 17, new Color(0.72f, 0.82f, 0.86f), TextAnchor.UpperLeft);
            cap.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -216), new Vector2(560, 50));
            var go = LGBuild.Panel(c.Content, "Go", new Color(0.55f, 0.97f, 0.92f));
            go.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 36), new Vector2(340, 56));
            LG.Fill(go.gameObject);
            var gt = LGBuild.Label(go.transform, Loc.T("СОЗДАТЬ ГАЛАКТИКУ"), 18, new Color(0.01f, 0.08f, 0.10f), TextAnchor.MiddleCenter, bold: true);
            gt.rectTransform.Stretch();
            InspSweep.Add(go.gameObject, 4.5f, 0.35f);
            c.Glow = go.rectTransform;
        }

        /// <summary>Полоса последней новости под карточками: обложка, метка, заголовок. Клик — окно новостей.</summary>
        private void BuildNewsStrip()
        {
            var news = MenuNews.All();
            if (news.Count == 0) return;
            var item = news[0];
            var c = MenuCard.Create(_hero, "News", new Vector2(760, 116), Vector2.zero, UIManager.DS.Gold, false, () => Open(Panel.News));
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 54);
            c.Rebase();
            c.AppearDelay = 1.15f;
            c.Lift = 4f;
            c.Scale = 0.015f;

            var tex = item.CoverTex;
            if (tex != null)
            {
                var thumb = LGBuild.Rect(c.ArtMask, "Thumb");
                thumb.anchorMin = new Vector2(0, 0);
                thumb.anchorMax = new Vector2(0, 1);
                thumb.pivot = new Vector2(0, 0.5f);
                thumb.sizeDelta = new Vector2(230, 0);
                thumb.anchoredPosition = Vector2.zero;
                var img = thumb.gameObject.AddComponent<RawImage>();
                img.texture = tex;
                img.raycastTarget = false;
                img.uvRect = MenuNews.Crop(tex, item.Thumb, 230f / 112f);
                c.UseArt(img);
                var fade = LGBuild.Panel(thumb, "Fade", new Color(0.025f, 0.05f, 0.085f, 1f));
                fade.sprite = MenuArt.HorizontalFade;
                fade.rectTransform.Stretch();
                fade.rectTransform.localScale = new Vector3(-1f, 1f, 1f);   // гаснет к правому краю миниатюры
                LG.Ignore(fade.gameObject);
            }

            var gold = new Color(1f, 0.82f, 0.48f);
            var accent = item.IsUpdate ? gold : UIManager.DS.NeonCyan;
            var chip = LGBuild.Panel(c.Content, "Chip", new Color(accent.r * 0.22f, accent.g * 0.22f, accent.b * 0.22f, 0.9f));
            chip.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(256, -22), new Vector2(150, 26));
            LG.Chip(chip.gameObject, new Color(accent.r, accent.g, accent.b, 0.55f));
            var ct = LGBuild.Label(chip.transform, item.Chip, 12, accent, TextAnchor.MiddleCenter, bold: true);
            ct.rectTransform.Stretch();
            c.Glow = chip.rectTransform;

            var t = LGBuild.Label(c.Content, string.IsNullOrEmpty(item.Headline) ? item.Title : item.Headline, 20,
                                  UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true, wrap: true);
            t.rectTransform.Stretch(256, 10, 90, 56);
            c.Title = t;

            var arrow = LGControls.IconButton(c.Content, LGIcon.Back, 40, new Color(0.18f, 0.13f, 0.04f, 0.9f), new Color(gold.r, gold.g, gold.b, 0.6f), () => Open(Panel.News), 180f);
            ((RectTransform)arrow.transform).At(new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-46, 0), new Vector2(40, 40));
            c.Arrow = (RectTransform)arrow.transform;
        }

        /// <summary>Углы экрана: «Выход» слева, настройки и справка справа.</summary>
        private void BuildCorners()
        {
            _corners = LGBuild.Rect(_root, "Corners");
            _corners.Stretch();

            var exit = MenuLink.Create(_corners, Loc.T("ВЫХОД"), new Color(0.86f, 0.55f, 0.52f), () => SceneFader.FadeOutThen(GameSession.Quit, 0.5f));
            ((RectTransform)exit.transform).At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(56, -50), new Vector2(160, 36));

            var set = LGControls.IconButton(_corners, LGIcon.Gear, 56, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.28f), () => Open(Panel.Settings));
            ((RectTransform)set.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-136, -44), new Vector2(56, 56));
            TooltipHelper.Attach(set.gameObject, Loc.T("Настройки"));
            var help = LGControls.IconButton(_corners, LGIcon.Info, 56, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.28f), () => Open(Panel.About));
            ((RectTransform)help.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -44), new Vector2(56, 56));
            TooltipHelper.Attach(help.gameObject, Loc.T("Об игре: как играть и пути к победе"));
            foreach (var b in new[] { set.transform, help.transform }) b.gameObject.AddComponent<MenuFadeIn>().Delay = 1.3f;
            exit.gameObject.AddComponent<MenuFadeIn>().Delay = 1.3f;
        }

        /// <summary>Окно открыто — карточки уезжают вниз и гаснут, логотип поднимается и тает; закрыто — всё возвращается.</summary>
        private void AnimateHero()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            float target = _open == Panel.None ? 1f : 0f;
            _heroK = LGEase.Spring(_heroK, target, ref _heroV, 4.2f, 0.9f, dt);
            float k = Mathf.Clamp01(_heroK);
            _heroCg.alpha = k;
            _heroCg.blocksRaycasts = _heroCg.interactable = k > 0.85f && target > 0.5f;
            _hero.anchoredPosition = new Vector2(0f, -120f * (1f - k));
            _logoCg.alpha = k;
            _logo.anchoredPosition = new Vector2(0f, -112f + 60f * (1f - k));
        }

        private static string ContinueCaption(SaveEntry e) =>
            $"{e.Meta.GameDate}  ·  {SaveSystem.FormatSavedAt(e.SavedAtLocal)}";

        private static string LoadCaption(int count) => count == 0 ? Loc.T("Сохранений пока нет") : Loc.F("Сохранений: {0}\nпродолжите любую партию", count);

        private void OnEnable() => SaveSystem.OnSavesChanged += RefreshSaveInfo;
        private void OnDisable() => SaveSystem.OnSavesChanged -= RefreshSaveInfo;

        /// <summary>Сохранение удалили прямо из меню — обновить «Продолжить» и счётчик.</summary>
        private void RefreshSaveInfo()
        {
            var list = SaveSystem.List();
            if (_loadCaption != null) _loadCaption.text = LoadCaption(list.Count);
            if (_continueCard == null) return;
            if (list.Count == 0) { _continueCard.Disable(); if (_continueMeta != null) _continueMeta.text = Loc.T("Сохранений больше нет"); }
            else if (_continueMeta != null) _continueMeta.text = ContinueCaption(list[0]);
        }

        // ================================================================ Окно справа

        private void BuildWindow()
        {
            var w = LGBuild.Rect(_root, "Window");
            w.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(1180, 820));
            _window = w;
            var bg = w.gameObject.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            var fx = LG.Glass(w.gameObject, 28f);
            fx.SetRim(new Color(0.45f, 0.95f, 0.90f, 0.32f));
            var mo = LG.Motion(w.gameObject, LGAppear.Kind.SlideUp);
            mo.distance = 60f;
            mo.inDuration = 0.5f;
            mo.outDuration = 0.22f;

            var header = LGBuild.Rect(w, "Header");
            header.TopBand(0, 92, 36, 36);

            var iconBg = LGBuild.Panel(header, "IconBg", new Color(0.06f, 0.20f, 0.24f, 1f));
            iconBg.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, -4), new Vector2(52, 52));
            var ifx = LG.Platter(iconBg.gameObject, 26f);
            ifx.FillMultiplier = 2f;
            ifx.SetRim(new Color(0.45f, 0.95f, 0.9f, 0.7f));
            _winIcon = LGIcons.Create(iconBg.transform, LGIcon.Globe, 26, UIManager.DS.NeonCyan);
            _winIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26, 26));

            _winTitle = LGBuild.Label(header, "", 24, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            _winTitle.rectTransform.Stretch(70, 0, 80, 22);
            _winSub = LGBuild.Label(header, "", 12, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            _winSub.rectTransform.Stretch(72, 0, 80, 56);

            var close = LGControls.IconButton(header, LGIcon.Close, 40, UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.3f), Close);
            ((RectTransform)close.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -4), new Vector2(40, 40));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc)</color>"));

            var sep = LGBuild.Panel(w, "Sep", new Color(0.45f, 0.95f, 0.9f, 0.22f));
            sep.rectTransform.TopBand(100, 1, 36, 36);
            LG.Line(sep.gameObject, hairline: true);

            _body = LGBuild.Rect(w, "Body");
            _body.Stretch(36, 30, 36, 122);

            w.gameObject.SetActive(false);
        }

        private void FitWindow()
        {
            if (_window == null) return;
            var r = _root.rect;
            float w = Mathf.Clamp(r.width - 300f, 900f, 1180f);
            float h = Mathf.Clamp(r.height - 150f, 600f, 860f);
            var cur = _window.sizeDelta;
            if (Mathf.Abs(cur.x - w) > 0.5f || Mathf.Abs(cur.y - h) > 0.5f) _window.sizeDelta = new Vector2(w, h);
        }

        private void Open(Panel p)
        {
            if (p == Panel.None) return;
            if (_open == p) { Close(); return; }
            SFXManager.Play("ui_click", 0.7f, 1.05f);
            if (_open == Panel.News) _newsWin.Hide();
            _open = p;

            // Новости — своё окно во весь экран
            if (p == Panel.News)
            {
                if (LG.IsVisible(_window.gameObject)) LG.Hide(_window.gameObject);
                if (_newsWin == null) _newsWin = NewsWindow.Create(_root, Close);
                _newsWin.Show();
                return;
            }

            switch (p)
            {
                case Panel.NewGame:
                    SetHeader(LGIcon.Globe, Loc.T("НОВАЯ ИГРА"), Loc.T("Настройте галактику — цивилизацию вы выберете на следующем шаге"));
                    break;
                case Panel.Load:
                    SetHeader(LGIcon.Load, Loc.T("ЗАГРУЗКА"), Loc.T("Продолжите одну из своих партий"));
                    break;
                case Panel.Settings:
                    SetHeader(LGIcon.Gear, Loc.T("НАСТРОЙКИ"), Loc.T("Графика, звук, интерфейс и сохранения"));
                    break;
                case Panel.About:
                    SetHeader(LGIcon.Info, Loc.T("ОБ ИГРЕ"), Loc.T("Astropolity — космическая 4X-стратегия в реальном времени с паузой"));
                    break;
            }

            foreach (var kv in _pages)
                if (kv.Key != p && kv.Value != null) kv.Value.SetActive(false);

            if (!_pages.TryGetValue(p, out var page) || page == null)
            {
                page = BuildPage(p);
                _pages[p] = page;
            }
            else
            {
                page.SetActive(true);
                if (p == Panel.Load) page.GetComponentInChildren<SaveLoadPanel>()?.Refresh();
            }

            if (!LG.IsVisible(_window.gameObject))
            {
                LG.Show(_window.gameObject);
                _window.SetAsLastSibling();
            }
        }

        private void Close()
        {
            if (_open == Panel.None) return;
            SFXManager.Play("ui_click", 0.5f, 0.9f);
            if (_open == Panel.News) { if (_newsWin != null) _newsWin.Hide(); }
            else LG.Hide(_window.gameObject);
            _open = Panel.None;
        }

        private void SetHeader(LGIcon icon, string title, string sub)
        {
            _winIcon.sprite = LGIcons.Get(icon);
            _winTitle.text = title;
            _winSub.text = sub;
        }

        private GameObject BuildPage(Panel p)
        {
            var page = LGBuild.Rect(_body, "Page_" + p);
            page.Stretch();
            page.gameObject.AddComponent<PageFade>();
            switch (p)
            {
                case Panel.NewGame: BuildNewGamePage(page); break;
                case Panel.Load: SaveLoadPanel.Build(page, SaveLoadPanel.Mode.Load, confirmLoad: false); break;
                case Panel.Settings: SettingsPanel.Build(page); break;
                case Panel.About: BuildAboutPage(page); break;
            }
            return page.gameObject;
        }

        // ================================================================ Новая игра

        private void BuildNewGamePage(RectTransform page)
        {
            var left = LGBuild.Rect(page, "Left");
            left.anchorMin = new Vector2(0, 0);
            left.anchorMax = new Vector2(0.55f, 1);
            left.offsetMin = new Vector2(0, 80);
            left.offsetMax = new Vector2(-12, 0);

            float y = 0f;
            y = LGControls.Section(left, y, Loc.T("ГАЛАКТИКА"), LGIcon.Star);

            var sizes = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var tmp = new NewGameSettings { GalaxySize = i };
                sizes.Add(Loc.F("{0} · {1} систем", NewGameSettings.SizeNames[i], tmp.StarCount));
            }
            LGControls.Selector(LGControls.Row(left, ref y, Loc.T("Размер"), Loc.T("Сколько звёздных систем в галактике"), 270f),
                sizes, _ng.GalaxySize, i => { _ng.GalaxySize = i; RefreshNewGame(); });
            LGControls.Selector(LGControls.Row(left, ref y, Loc.T("Форма"), Loc.T("Расположение звёзд: рукава спирали дают узкие проходы и фронты"), 270f),
                NewGameSettings.ShapeNames, Mathf.Clamp(_ng.Shape, 0, NewGameSettings.ShapeNames.Length - 1),
                i => { _ng.Shape = i; RefreshNewGame(); });

            var seedHost = LGControls.Row(left, ref y, Loc.T("Зерно генерации"), Loc.T("Одинаковое зерно — одинаковая карта"), 270f);
            var inputHost = LGBuild.Rect(seedHost, "SeedInput");
            inputHost.Stretch(0, 0, 50, 0);
            _seedInput = LGControls.Input(inputHost, _ng.Seed.ToString(), Loc.T("Случайное"), 7);
            _seedInput.contentType = InputField.ContentType.IntegerNumber;
            _seedInput.onEndEdit.AddListener(s =>
            {
                if (int.TryParse(s, out int v) && v > 0) _ng.Seed = v;
                else { _ng.Seed = NewGameSettings.RandomSeed(); _seedInput.text = _ng.Seed.ToString(); }
                RefreshNewGame();
            });
            var dice = LGControls.IconButton(seedHost, LGIcon.Dice, 40, UIManager.DS.BtnNeutral, new Color(0.45f, 0.95f, 0.9f, 0.5f), () =>
            {
                _ng.Seed = NewGameSettings.RandomSeed();
                _seedInput.text = _ng.Seed.ToString();
                RefreshNewGame();
            });
            ((RectTransform)dice.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(40, 40));
            TooltipHelper.Attach(dice.gameObject, Loc.T("Случайная галактика"));

            y += 6f;
            y = LGControls.Section(left, y, Loc.T("ПАРТИЯ"), LGIcon.Leader);
            LGControls.Selector(LGControls.Row(left, ref y, Loc.T("Соперники"), Loc.T("Сколько империй ИИ делят с вами галактику"), 270f),
                NewGameSettings.RivalNames, _ng.RivalCount - 1, i => { _ng.Rivals = i + 1; RefreshNewGame(); });
            LGControls.Selector(LGControls.Row(left, ref y, Loc.T("Вольные миры"), Loc.T("Торговля, покровительство, захват"), 270f, 54f),
                NewGameSettings.NeutralNames, _ng.NeutralCount - NewGameSettings.MinNeutrals, i => { _ng.Neutrals = i + NewGameSettings.MinNeutrals; RefreshNewGame(); });
            // Под названием — что значит выбранный уровень (меняется вместе с выбором)
            var diffRow = LGControls.Row(left, ref y, Loc.T("Сложность"), AIDifficulty.Description(_ng.Tier), 270f, 74f);
            var diffText = diffRow.parent != null ? diffRow.parent.Find("Text") : null;
            var diffDesc = diffText != null && diffText.childCount > 1 ? diffText.GetChild(1).GetComponent<Text>() : null;
            LGControls.Selector(diffRow, NewGameSettings.DifficultyNames, _ng.Tier, i =>
            {
                _ng.SetLevel(i);
                if (diffDesc != null) diffDesc.text = AIDifficulty.Description(i);
                RefreshNewGame();
            });
            LGControls.Switch(LGControls.Row(left, ref y, Loc.T("Обучение"), Loc.T("Пошаговые подсказки советника в начале"), 270f),
                _ng.Tutorial, v => _ng.Tutorial = v);

            // Справа — предпросмотр
            var right = LGBuild.Panel(page, "Preview", new Color(0.01f, 0.025f, 0.04f, 0.9f));
            right.rectTransform.anchorMin = new Vector2(0.55f, 0);
            right.rectTransform.anchorMax = new Vector2(1, 1);
            right.rectTransform.offsetMin = new Vector2(12, 80);
            right.rectTransform.offsetMax = Vector2.zero;
            var pfx = LG.Platter(right.gameObject, 22f);
            pfx.FillMultiplier = 1.4f;
            pfx.GlowMultiplier = 0f;
            pfx.SetRim(new Color(0.45f, 0.95f, 0.9f, 0.18f));
            _preview = right.gameObject.AddComponent<GalaxyPreview>();
            _preview.Build(right.rectTransform);

            // Низ: цели и кнопка старта
            _ngGoals = LGBuild.Label(page, "", 12, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, wrap: true);
            _ngGoals.rectTransform.anchorMin = new Vector2(0, 0);
            _ngGoals.rectTransform.anchorMax = new Vector2(1, 0);
            _ngGoals.rectTransform.pivot = new Vector2(0.5f, 0);
            _ngGoals.rectTransform.offsetMin = new Vector2(4, 0);
            _ngGoals.rectTransform.offsetMax = new Vector2(-470, 60);

            var start = LGBuild.Button(page, "Start", new Color(0.08f, 0.42f, 0.40f), UIManager.DS.NeonCyan, () =>
            {
                SFXManager.Play("ui_click", 1f, 0.8f);
                GameSession.StartNewGame(_ng);
            }, LGIcon.Play, Loc.T("ДАЛЕЕ — ВЫБОР ЦИВИЛИЗАЦИИ"), 14);
            ((RectTransform)start.transform).At(new Vector2(1, 0), new Vector2(1, 0), Vector2.zero, new Vector2(450, 60));
            var sfx = start.GetComponent<LiquidGlassEffect>();
            if (sfx != null) start.gameObject.AddComponent<PulseGlow>().Init(sfx);

            RefreshNewGame();
        }

        private void RefreshNewGame()
        {
            if (_preview != null) _preview.Show(_ng);
            if (_ngGoals != null)
                _ngGoals.text =
                    Loc.T("<b><color=#E8F6FA>Пути к победе</color></b>") + "\n" +
                    Loc.F("<color=#4DF2DB>форпосты в {0} системах</color>   ·   <color=#5CF599>{1} технологий и «Вознесение»</color>", _ng.DominationTarget, VictoryManager.ScienceTarget) + "\n" +
                    Loc.F("<color=#7FD8FF>экономика или совет галактики</color>   ·   <color=#FFCC52>больше всех очков в {0} году</color>", VictoryManager.EndYear);
        }

        // ================================================================ Об игре

        private void BuildAboutPage(RectTransform page)
        {
            var left = LGBuild.Rect(page, "HowTo");
            left.Column(0f, 0.54f, 0, 14);
            float y = LGControls.Section(left, 0f, Loc.T("КАК ИГРАТЬ"), LGIcon.Info);
            Step(left, ref y, LGIcon.Ship, Loc.T("1 · Исследуйте"),
                 Loc.T("Отправляйте научный корабль (ПКМ по звезде) изучать соседние системы — за каждую разведку вы получаете ресурсы."));
            Step(left, ref y, LGIcon.Starbase, Loc.T("2 · Расширяйтесь"),
                 Loc.T("Строительный корабль ставит форпосты в разведанных системах. Чем больше систем — тем сильнее империя."));
            Step(left, ref y, LGIcon.Planet, Loc.T("3 · Развивайте колонии"),
                 Loc.T("Заселяйте пригодные планеты и стройте районы: жильё, добычу, энергию и промышленность."));
            Step(left, ref y, LGIcon.Research, Loc.T("4 · Изучайте технологии"),
                 Loc.T("Три слота исследований работают параллельно. Не оставляйте их пустыми — следите за алертами под верхней панелью."));
            Step(left, ref y, LGIcon.Fleet, Loc.T("5 · Защищайтесь"),
                 Loc.T("Соседняя империя наблюдает за вашей мощью. Флот стоит энергии, а войны выигрываются осадой: держите корабли в системе врага без его защитников."));

            var right = LGBuild.Rect(page, "Side");
            right.Column(0.54f, 1f, 14, 0);
            float ry = LGControls.Section(right, 0f, Loc.T("ПУТИ К ПОБЕДЕ"), LGIcon.Trophy);
            int[] dom = new int[3];
            for (int i = 0; i < 3; i++) dom[i] = new NewGameSettings { GalaxySize = i }.DominationTarget;
            int econYears = VictoryManager.EconomyMonths / 12;
            Goal(right, ref ry, LGIcon.Starbase, Loc.T("Доминирование"),
                 Loc.F("Форпосты в {0} / {1} / {2} системах (малая / средняя / большая галактика). Удержите порог {3} мес.", dom[0], dom[1], dom[2], VictoryManager.DominationHoldMonths), UIManager.DS.NeonCyan);
            Goal(right, ref ry, LGIcon.Research, Loc.T("Научная"),
                 Loc.F("Изучите {0} технологий и завершите проект «Вознесение».", VictoryManager.ScienceTarget), UIManager.DS.Green);
            Goal(right, ref ry, LGIcon.Energy, Loc.T("Экономическая"),
                 Loc.F("Держите {0:0}% продукта всей галактики {1} {2} подряд.", VictoryManager.EconomyShare * 100f, econYears, VictoryManager.YearsWord(econYears)), new Color(1f, 0.78f, 0.36f));
            Goal(right, ref ry, LGIcon.Diplomacy, Loc.T("Дипломатическая"),
                 Loc.F("Соберите две трети голосов галактического совета. Заседания — с {0} года.", VictoryManager.CouncilFirstYear), new Color(0.50f, 0.85f, 1f));
            Goal(right, ref ry, LGIcon.Trophy, Loc.F("Очки в {0} году", VictoryManager.EndYear),
                 Loc.T("Обгоните соперников по системам, населению, науке и флоту. Соперники тоже могут победить."), UIManager.DS.Gold);

            ry += 6f;
            ry = LGControls.Section(right, ry, Loc.T("УПРАВЛЕНИЕ"), LGIcon.Menu);
            var keys = LGBuild.Panel(right, "Keys", new Color(0.05f, 0.10f, 0.135f, 0.55f));
            keys.rectTransform.TopBand(ry, 128);
            LG.Platter(keys.gameObject, 16f).GlowMultiplier = 0f;
            var kt = LGBuild.Label(keys.transform,
                Loc.T("<color=#4DF2DB><b>ЛКМ</b></color>  выбор      <color=#4DF2DB><b>ПКМ</b></color>  приказ флоту\n") +
                Loc.T("<color=#4DF2DB><b>WASD</b></color>  камера      <color=#4DF2DB><b>Колесо</b></color>  масштаб\n") +
                Loc.T("<color=#4DF2DB><b>Пробел</b></color>  пауза      <color=#4DF2DB><b>1 2 3</b></color>  скорость\n") +
                Loc.T("<color=#4DF2DB><b>F5 / F9</b></color>  быстрое сохранение / загрузка\n") +
                Loc.T("<color=#4DF2DB><b>Esc</b></color>  меню паузы"),
                12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            kt.rectTransform.Stretch(18, 8, 14, 10);
            kt.lineSpacing = 1.25f;

            var credits = LGBuild.Label(page, "ASTROPOLITY  " + Version,
                                        10, UIManager.DS.TextMuted, TextAnchor.LowerRight);
            credits.rectTransform.Stretch(0, -18, 0, 0);
            // Модели планет по лицензии CC BY 4.0 — авторы указаны (Resources/Planets/Models/CREDITS.txt)
            var models = LGBuild.Label(page, Loc.T("Модели планет (Sketchfab): WeSVa, jimmyn64, SebastianSosnowski, tamminen — CC BY 4.0 и Sketchfab Standard") + "\n" +
                                             Loc.T("Пиратский корабль (Sketchfab): jkazulyblanco — CC BY 4.0"),
                                       10, UIManager.DS.TextMuted, TextAnchor.LowerLeft);
            models.rectTransform.Stretch(0, -18, 220, 0);
        }

        private static void Step(RectTransform parent, ref float y, LGIcon icon, string title, string body)
        {
            var row = LGBuild.Panel(parent, "Step", new Color(0.05f, 0.10f, 0.135f, 0.55f));
            row.rectTransform.TopBand(y, 88);
            var fx = LG.Platter(row.gameObject, 16f);
            fx.GlowMultiplier = 0f;
            var ib = LGBuild.Panel(row.transform, "IconBg", new Color(0.06f, 0.20f, 0.24f, 1f));
            ib.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(44, 44));
            var ibfx = LG.Platter(ib.gameObject, 22f);
            ibfx.FillMultiplier = 1.8f;
            ibfx.SetRim(new Color(0.45f, 0.95f, 0.9f, 0.5f));
            var ic = LGIcons.Create(ib.transform, icon, 22, UIManager.DS.NeonCyan);
            ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));
            var text = LGBuild.Rect(row.transform, "Text");
            text.Stretch(76, 10, 16, 10);
            LGBuild.Label(text, title, 14, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            var b = LGBuild.Label(text, body, 11, UIManager.DS.TextMuted, TextAnchor.UpperLeft, wrap: true);
            b.rectTransform.offsetMax = new Vector2(0, -22);
            b.lineSpacing = 1.15f;
            y += 96f;
        }

        private static void Goal(RectTransform parent, ref float y, LGIcon icon, string title, string body, Color col)
        {
            var row = LGBuild.Panel(parent, "Goal", new Color(0.05f, 0.10f, 0.135f, 0.55f));
            row.rectTransform.TopBand(y, 60);
            var fx = LG.Platter(row.gameObject, 16f);
            fx.GlowMultiplier = 0f;
            fx.SetRim(new Color(col.r, col.g, col.b, 0.3f));
            var ic = LGIcons.Create(row.transform, icon, 24, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(24, 24));
            var text = LGBuild.Rect(row.transform, "Text");
            text.Stretch(58, 6, 14, 7);
            LGBuild.Label(text, title, 13, col, TextAnchor.UpperLeft, bold: true);
            var b = LGBuild.Label(text, body, 11, UIManager.DS.TextMuted, TextAnchor.UpperLeft, wrap: true);
            b.rectTransform.offsetMax = new Vector2(0, -18);
            b.verticalOverflow = VerticalWrapMode.Truncate;
            b.lineSpacing = 1.05f;
            y += 66f;
        }

        // ================================================================ Подвал

        private void BuildFooter()
        {
            var ver = LGBuild.Label(_root, $"<b>ASTROPOLITY</b>   {Version}", 12, new Color(0.62f, 0.74f, 0.80f, 0.8f), TextAnchor.LowerLeft);
            ver.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(56, 44), new Vector2(500, 20));
            ver.gameObject.AddComponent<MenuFadeIn>().Delay = 1.4f;
        }

        // ================================================================ Вспомогательные компоненты

        /// <summary>Мягкий вход страницы при переключении вкладок меню.</summary>
        private sealed class PageFade : MonoBehaviour
        {
            private CanvasGroup _cg;
            private RectTransform _rt;
            private float _t;
            private void Awake() { _cg = gameObject.AddComponent<CanvasGroup>(); _rt = (RectTransform)transform; }
            private void OnEnable() { _t = 0f; Apply(); }
            private void Update() { if (_t >= 1f) return; _t += Time.unscaledDeltaTime / 0.32f; Apply(); }
            private void Apply()
            {
                float k = LGEase.OutCubic(_t);
                _cg.alpha = k;
                _rt.anchoredPosition = new Vector2(22f * (1f - k), 0f);
            }
        }

        /// <summary>Медленное «дыхание» свечения главной кнопки.</summary>
        private sealed class PulseGlow : MonoBehaviour
        {
            private LiquidGlassEffect _fx;
            public void Init(LiquidGlassEffect fx) => _fx = fx;
            private void Update()
            {
                if (_fx != null) _fx.SetPulse(0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 2.2f));
            }
            private void OnDisable() { if (_fx != null) _fx.SetPulse(0f); }
        }
    }

    // ====================================================================== Карточка главного меню

    /// <summary>
    /// Карточка главного меню: тёмное стекло со скруглённой маской под арт, ореол цвета действия позади.
    /// Анимации: появление снизу с лёгким «пружинящим» масштабом, при наведении — подъём, увеличение, яркая кромка,
    /// ореол и наезд на арт (арт и без наведения медленно дрейфует), нажатие — короткое «вдавливание».
    /// Главная карточка дышит свечением; портрет правителя на ней подсвечивается при наведении.
    /// </summary>
    public sealed class MenuCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
                                   IPointerDownHandler, IPointerUpHandler
    {
        public float AppearDelay;
        public float Lift = 10f, Scale = 0.035f;
        /// <summary>Content — слой текста; ArtMask — скруглённая маска под арт и портрет.</summary>
        public RectTransform Content, ArtMask;
        /// <summary>Glow — элемент, который «вспухает» при наведении (кнопка, метка); Arrow — сдвигается вправо.</summary>
        public RectTransform Glow, Arrow;
        public RawImage Art;
        public Text Title;
        public LeaderPortraitView Portrait;
        public bool Disabled { get; private set; }

        private RectTransform _rt;
        private CanvasGroup _cg;
        private LiquidGlassEffect _fx;
        private Image _halo;
        private Color _accent;
        private System.Action _onClick;
        private bool _over, _down, _primary;
        private float _h, _hv, _t, _press, _pv, _seed;
        private Vector2 _rest, _arrowRest;
        private Rect _uv;
        private Color _titleBase = Color.white;
        private bool _titleKnown, _arrowKnown;

        public static MenuCard Create(Transform parent, string name, Vector2 size, Vector2 pos, Color accent, bool primary, System.Action onClick)
        {
            var root = LGBuild.Rect(parent, "Card_" + name);
            root.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var c = root.gameObject.AddComponent<MenuCard>();
            c._rt = root;
            c._rest = pos;
            c._accent = accent;
            c._primary = primary;
            c._onClick = onClick;
            c._seed = Random.Range(0f, 50f);
            c._cg = root.gameObject.AddComponent<CanvasGroup>();
            c._cg.alpha = 0f;

            // Ореол позади карточки
            c._halo = LGBuild.Panel(root, "Halo", new Color(accent.r, accent.g, accent.b, 0f));
            c._halo.sprite = MenuArt.SoftDisc;
            c._halo.rectTransform.Stretch(-size.x * 0.22f, -size.y * 0.35f, -size.x * 0.22f, -size.y * 0.35f);
            LG.Ignore(c._halo.gameObject);

            var body = LGBuild.Panel(root, "Body", new Color(0.025f, 0.05f, 0.085f, 0.94f), raycast: true);
            body.rectTransform.Stretch();
            c._fx = LG.Glass(body.gameObject, 24f);
            c._fx.GlowMultiplier = primary ? 0.9f : 0.35f;
            c._fx.SetRim(new Color(accent.r, accent.g, accent.b, primary ? 0.6f : 0.22f));
            c.ArtMask = LG.RoundedMask(body.transform, 22f, 2f);
            c.Content = LGBuild.Rect(body.transform, "Content");
            c.Content.Stretch();
            InspSweep.Add(body.gameObject, primary ? 6.5f : 9f + Random.Range(0f, 3f), primary ? 0.09f : 0.05f);
            return c;
        }

        /// <summary>Арт во всю карточку (uv — какой кусок картинки показать) и затемнение снизу под текст.</summary>
        public void SetArt(Texture tex, Rect uv)
        {
            if (tex == null) return;
            var img = LGBuild.Rect(ArtMask, "Art").gameObject.AddComponent<RawImage>();
            img.rectTransform.Stretch();
            img.texture = tex;
            img.uvRect = uv;
            img.raycastTarget = false;
            Art = img;
            _uv = uv;
            var shade = LGBuild.Panel(ArtMask, "Shade", new Color(0.02f, 0.045f, 0.08f, 1f));
            shade.sprite = MenuArt.VerticalFade;
            shade.rectTransform.Stretch();
            LG.Ignore(shade.gameObject);
        }

        /// <summary>Свой арт (уже с кадром) — карточка будет так же медленно дрейфовать и наезжать на него.</summary>
        public void UseArt(RawImage img)
        {
            Art = img;
            _uv = img.uvRect;
        }

        /// <summary>Карточку переставили (другие якоря) — запомнить новое место покоя.</summary>
        public void Rebase() => _rest = _rt.anchoredPosition;

        public void Disable()
        {
            Disabled = true;
            _over = false;
        }

        public void Click()
        {
            if (Disabled || _cg.alpha < 0.5f) return;
            SFXManager.Play("ui_click", 0.8f, 1f);
            _h += 0.3f;
            _onClick?.Invoke();
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (Disabled) return;
            _over = true;
            if (SFXManager.Has("ui_hover")) SFXManager.Play("ui_hover", 0.35f, 1.1f);
        }

        public void OnPointerExit(PointerEventData e) { _over = false; _down = false; }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) _down = true; }
        public void OnPointerUp(PointerEventData e) => _down = false;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            Click();
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _t += dt;

            // Появление: снизу, с затуханием и лёгким пружинящим масштабом
            float k = Mathf.Clamp01((_t - AppearDelay) / 0.7f);
            float e = LGEase.OutCubic(k);
            float pop = Mathf.LerpUnclamped(0.92f, 1f, LGEase.OutBack(k, 1.3f));
            _cg.alpha = Disabled ? e * 0.5f : e;
            _cg.blocksRaycasts = k > 0.6f;

            _h = LGEase.Spring(_h, _over && !Disabled ? 1f : 0f, ref _hv, 3.6f, 0.62f, dt);
            _press = LGEase.Spring(_press, _down ? 1f : 0f, ref _pv, 7f, 0.75f, dt);
            float h = Mathf.Clamp01(_h);

            float sc = pop * (1f + Scale * _h - 0.03f * _press);
            _rt.localScale = new Vector3(sc, sc, 1f);
            _rt.anchoredPosition = _rest + new Vector2(0f, -56f * (1f - e) + Lift * _h);

            float breathe = 0.5f + 0.5f * Mathf.Sin(_t * 1.9f + _seed);
            _fx.SetHover(h * 0.7f);
            _fx.SetRim(new Color(_accent.r, _accent.g, _accent.b, Mathf.Lerp(_primary ? 0.5f + 0.15f * breathe : 0.22f, 0.95f, h)));
            if (_primary) _fx.SetPulse(0.12f + 0.14f * breathe + 0.2f * h);
            _halo.color = new Color(_accent.r, _accent.g, _accent.b, e * ((_primary ? 0.10f + 0.06f * breathe : 0f) + 0.20f * h));

            // Арт: медленный дрейф и наезд при наведении
            if (Art != null)
            {
                float zoom = 1f - 0.07f * h - 0.025f * (0.5f + 0.5f * Mathf.Sin(_t * 0.21f + _seed));
                var c = _uv.center + new Vector2(Mathf.Sin(_t * 0.07f + _seed), Mathf.Cos(_t * 0.05f + _seed)) * 0.012f;
                var size = _uv.size * zoom;
                Art.uvRect = new Rect(c - size * 0.5f, size);
            }
            if (Portrait != null) Portrait.Glow = h;

            if (Glow != null)
            {
                float g = 1f + 0.05f * h - 0.03f * _press;
                Glow.localScale = new Vector3(g, g, 1f);
            }
            if (Arrow != null)
            {
                if (!_arrowKnown) { _arrowRest = Arrow.anchoredPosition; _arrowKnown = true; }
                Arrow.anchoredPosition = _arrowRest + new Vector2(8f * h, 0f);
            }
            if (Title != null)
            {
                if (!_titleKnown) { _titleBase = Title.color; _titleKnown = true; }
                Title.color = Color.Lerp(_titleBase, Color.Lerp(_accent, Color.white, 0.55f), h * 0.45f);
            }
        }
    }

    /// <summary>Текстовая ссылка в углу меню («Выход»): при наведении светлеет, под ней вырастает черта.</summary>
    public sealed class MenuLink : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private Text _text;
        private Image _line;
        private Color _color;
        private System.Action _onClick;
        private bool _over;
        private float _h, _hv;

        public static MenuLink Create(Transform parent, string label, Color color, System.Action onClick)
        {
            var rt = LGBuild.Rect(parent, "Link_" + label);
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            LG.Ignore(rt.gameObject);
            var l = rt.gameObject.AddComponent<MenuLink>();
            l._color = color;
            l._onClick = onClick;
            l._text = LGBuild.Label(rt, label, 16, color, TextAnchor.MiddleLeft, bold: true);
            l._text.rectTransform.Stretch();
            l._line = LGBuild.Panel(rt, "Line", color);
            l._line.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(0, 4), new Vector2(0, 2));
            LG.Ignore(l._line.gameObject);
            return l;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _over = true;
            if (SFXManager.Has("ui_hover")) SFXManager.Play("ui_hover", 0.3f, 1.2f);
        }

        public void OnPointerExit(PointerEventData e) => _over = false;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            SFXManager.Play("ui_click", 0.6f, 0.9f);
            _onClick?.Invoke();
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _h = LGEase.Spring(_h, _over ? 1f : 0f, ref _hv, 4f, 0.7f, dt);
            float h = Mathf.Clamp01(_h);
            var c = Color.Lerp(new Color(_color.r, _color.g, _color.b, 0.75f), Color.Lerp(_color, Color.white, 0.4f), h);
            _text.color = c;
            _line.color = c;
            _line.rectTransform.sizeDelta = new Vector2(_text.preferredWidth * h, 2f);
        }
    }

    /// <summary>Плавное появление элемента меню через Delay секунд после открытия.</summary>
    public sealed class MenuFadeIn : MonoBehaviour
    {
        public float Delay;
        private CanvasGroup _cg;
        private float _t;

        private void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;
        }

        private void Update()
        {
            _t += Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            float k = Mathf.Clamp01((_t - Delay) / 0.6f);
            _cg.alpha = LGEase.OutCubic(k);
            if (k >= 1f) enabled = false;
        }
    }

    // ====================================================================== Предпросмотр галактики

    /// <summary>
    /// Точная миниатюра будущей галактики: тот же генератор и тот же сид, что при старте партии.
    /// </summary>
    public sealed class GalaxyPreview : MonoBehaviour
    {
        private RectTransform _area, _layer;
        private Text _caption;
        private NewGameSettings _pending;
        private float _t;

        private struct Item { public RectTransform Rt; public Graphic G; public float Delay; public float Alpha; public bool Pop; }
        private readonly List<Item> _items = new List<Item>();
        private RectTransform _capRing, _aiRing, _aiRing2, _aiRing3, _aiRing4;

        public void Build(RectTransform host)
        {
            var glow = LGBuild.Panel(host, "Nebula", new Color(0.35f, 0.45f, 1f, 0.10f));
            glow.sprite = MenuArt.SoftDisc;
            glow.rectTransform.Stretch(20, 50, 20, 20);
            LG.Ignore(glow.gameObject);

            _area = LGBuild.Rect(host, "Area");
            _area.Stretch(24, 54, 24, 24);
            LG.Ignore(_area.gameObject);

            _caption = LGBuild.Label(host, "", 11, UIManager.DS.TextMuted, TextAnchor.LowerCenter);
            _caption.rectTransform.Stretch(16, 14, 16, 0);
        }

        public void Show(NewGameSettings s)
        {
            _pending = s.Clone();
        }

        private void Update()
        {
            if (_pending != null && _area.rect.width > 10f)
            {
                Rebuild(_pending);
                _pending = null;
            }

            _t += Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.Rt == null) continue;
                float k = Mathf.Clamp01((_t - it.Delay) / 0.35f);
                var c = it.G.color;
                c.a = it.Alpha * LGEase.OutCubic(k);
                it.G.color = c;
                if (it.Pop)
                {
                    float s = LGEase.OutBack(k, 1.8f);
                    it.Rt.localScale = new Vector3(s, s, 1f);
                }
            }

            float pulse = 1f + 0.12f * Mathf.Sin(_t * 3f);
            if (_capRing != null && _t > 0.9f) _capRing.localScale = new Vector3(pulse, pulse, 1f);
            if (_aiRing != null && _t > 0.9f) _aiRing.localScale = new Vector3(2f - pulse, 2f - pulse, 1f);
            if (_aiRing2 != null && _t > 0.9f) _aiRing2.localScale = new Vector3(2f - pulse, 2f - pulse, 1f);
            if (_aiRing3 != null && _t > 0.9f) _aiRing3.localScale = new Vector3(2f - pulse, 2f - pulse, 1f);
            if (_aiRing4 != null && _t > 0.9f) _aiRing4.localScale = new Vector3(2f - pulse, 2f - pulse, 1f);
        }

        private void Rebuild(NewGameSettings s)
        {
            if (_layer != null) Destroy(_layer.gameObject);
            _items.Clear();
            _capRing = _aiRing = _aiRing2 = _aiRing3 = _aiRing4 = null;
            _t = 0f;

            _layer = LGBuild.Rect(_area, "Layer");
            _layer.Stretch();

            // Тот же генератор, тот же сид — но без побочных эффектов для текущей сцены
            List<StarSystem> systems;
            List<Hyperlane> lanes;
            var go = new GameObject("~GalaxyPreviewGen") { hideFlags = HideFlags.HideAndDontSave };
            var oldState = Random.state;
            bool logs = Debug.unityLogger.logEnabled;
            try
            {
                var gen = go.AddComponent<GalaxyGenerator>();
                GameBootstrap.ConfigureGenerator(gen, s);
                Random.InitState(s.Seed);
                Debug.unityLogger.logEnabled = false;
                gen.GenerateGalaxy();
                systems = new List<StarSystem>(gen.Systems);
                lanes = new List<Hyperlane>(gen.Hyperlanes);
            }
            finally
            {
                Debug.unityLogger.logEnabled = logs;
                Random.state = oldState;
                DestroyImmediate(go);
            }
            if (systems.Count == 0) return;

            float half = Mathf.Min(_area.rect.width, _area.rect.height) * 0.5f - 8f;
            float scale = half / Mathf.Max(1f, s.Radius);
            Vector2 P(StarSystem sys) => new Vector2(sys.Position.x, sys.Position.z) * scale;

            foreach (var l in lanes)
            {
                if (l.SystemA >= systems.Count || l.SystemB >= systems.Count) continue;
                Vector2 a = P(systems[l.SystemA]), b = P(systems[l.SystemB]);
                var img = LGBuild.Panel(_layer, "Lane", new Color(0.45f, 0.85f, 0.95f, 0f));
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = (a + b) * 0.5f;
                rt.sizeDelta = new Vector2((b - a).magnitude, 1.3f);
                rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
                float d = Mathf.Min(a.magnitude, b.magnitude) / Mathf.Max(1f, half);
                _items.Add(new Item { Rt = rt, G = img, Delay = 0.15f + d * 0.55f, Alpha = 0.16f, Pop = false });
            }

            // Столица игрока — система 0; столицы соперников — как в игре: каждая следующая
            // максимально далека от ближайшей уже занятой столицы
            var caps = new List<int> { 0 };
            for (int r = 0; r < s.RivalCount; r++)
            {
                int best = -1;
                float far = -1f;
                for (int i = 1; i < systems.Count; i++)
                {
                    if (caps.Contains(i) || systems[i].ConnectedSystemIds.Count == 0) continue;
                    float dd = float.MaxValue;
                    foreach (int c in caps) dd = Mathf.Min(dd, Vector3.Distance(systems[c].Position, systems[i].Position));
                    if (dd > far) { far = dd; best = i; }
                }
                if (best < 0) break;
                caps.Add(best);
            }

            foreach (var sys in systems)
            {
                var p = P(sys);
                float size = sys.SpectralClass == StarSpectralClass.ClassB ? 13f
                           : sys.SpectralClass == StarSpectralClass.ClassM ? 9f : 11f;
                var img = LGBuild.Panel(_layer, "Star", StarColor(sys.SpectralClass));
                img.sprite = MenuArt.Star;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = p;
                rt.sizeDelta = new Vector2(size, size);
                _items.Add(new Item { Rt = rt, G = img, Delay = 0.1f + p.magnitude / Mathf.Max(1f, half) * 0.55f, Alpha = 1f, Pop = true });
            }

            _capRing = Marker(P(systems[0]), UIManager.DS.NeonCyan, Loc.T("ВЫ"));
            if (caps.Count > 1) _aiRing = Marker(P(systems[caps[1]]), AIEmpireManager.MapColorFor(1), Loc.T("СОПЕРНИК"));
            if (caps.Count > 2) _aiRing2 = Marker(P(systems[caps[2]]), AIEmpireManager.MapColorFor(2), Loc.T("СОПЕРНИК"));
            if (caps.Count > 3) _aiRing3 = Marker(P(systems[caps[3]]), AIEmpireManager.MapColorFor(3), Loc.T("СОПЕРНИК"));
            if (caps.Count > 4) _aiRing4 = Marker(P(systems[caps[4]]), AIEmpireManager.MapColorFor(4), Loc.T("СОПЕРНИК"));

            _caption.text = Loc.F("{0} галактика   ·   {1}   ·   {2} систем   ·   {3} гиперкоридоров   ·   сид {4}", NewGameSettings.SizeNames[s.GalaxySize], NewGameSettings.ShapeNames[Mathf.Clamp(s.Shape, 0, 3)].ToLower(), systems.Count, lanes.Count, s.Seed);
        }

        private RectTransform Marker(Vector2 pos, Color col, string label)
        {
            var ring = LGBuild.Panel(_layer, "Ring", col);
            ring.sprite = MenuArt.Ring;
            var rt = ring.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(28, 28);
            _items.Add(new Item { Rt = rt, G = ring, Delay = 0.85f, Alpha = 0.9f, Pop = false });

            var t = LGBuild.Label(_layer, label, 10, col, TextAnchor.MiddleCenter, bold: true);
            var trt = t.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0f);
            trt.anchoredPosition = pos + new Vector2(0, 16);
            trt.sizeDelta = new Vector2(100, 14);
            _items.Add(new Item { Rt = trt, G = t, Delay = 1f, Alpha = 1f, Pop = false });
            return rt;
        }

        private static Color StarColor(StarSpectralClass c)
        {
            switch (c)
            {
                case StarSpectralClass.ClassM: return new Color(1f, 0.55f, 0.42f);
                case StarSpectralClass.ClassK: return new Color(1f, 0.76f, 0.46f);
                case StarSpectralClass.ClassG: return new Color(1f, 0.94f, 0.66f);
                case StarSpectralClass.ClassA: return new Color(0.86f, 0.93f, 1f);
                case StarSpectralClass.ClassB: return new Color(0.62f, 0.78f, 1f);
                default: return new Color(0.70f, 0.50f, 1f);
            }
        }
    }

    // ====================================================================== Облёт камеры в меню

    /// <summary>Медленный облёт галактики, пока открыто главное меню.</summary>
    public sealed class MainMenuCamera : MonoBehaviour
    {
        private StrategyCameraController _ctl;
        private GalaxyGenerator _gen;
        private float _angle = 205f;
        private float _t;

        private void OnEnable()
        {
            _ctl = GetComponent<StrategyCameraController>();
            if (_ctl != null) _ctl.enabled = false;
        }

        private void OnDisable()
        {
            if (_ctl != null) _ctl.enabled = true;
        }

        private void LateUpdate()
        {
            if (MainMenuUI.Instance == null) { enabled = false; return; }
            if (_gen == null) _gen = FindAnyObjectByType<GalaxyGenerator>();
            float radius = _gen != null ? _gen.GalaxyRadius : 160f;

            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _t += dt;
            _angle += dt * 2.4f;

            // Плавный «наезд» в первые секунды
            float intro = LGEase.OutCubic(Mathf.Clamp01(_t / 6f));
            float dist = radius * Mathf.Lerp(1.75f, 1.18f, intro);
            float height = radius * Mathf.Lerp(0.95f, 0.5f, intro) + Mathf.Sin(_t * 0.25f) * 5f;

            float a = _angle * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Cos(a) * dist, height, Mathf.Sin(a) * dist);
            var rot = Quaternion.LookRotation(-pos.normalized * 1f + Vector3.down * 0.05f);
            // Сдвигаем камеру влево — галактика оказывается справа от меню
            pos += rot * Vector3.left * radius * 0.30f;
            transform.SetPositionAndRotation(pos, rot);
        }
    }

    // ====================================================================== Процедурные текстуры меню

    public static class MenuArt
    {
        private static Sprite s_h, s_v, s_vig, s_disc, s_star, s_ring;

        public static Sprite HorizontalFade => s_h != null ? s_h : (s_h = Make(256, 4, (u, v) => Mathf.Pow(1f - u, 1.6f) * 0.94f));
        public static Sprite VerticalFade => s_v != null ? s_v : (s_v = Make(4, 128, (u, v) => Mathf.Pow(1f - v, 1.8f) * 0.85f));

        public static Sprite Vignette => s_vig != null ? s_vig : (s_vig = Make(128, 128, (u, v) =>
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            float r = Mathf.Sqrt(x * x * 0.8f + y * y);
            return Mathf.Clamp01((r - 0.55f) / 0.75f) * 0.75f;
        }));

        public static Sprite SoftDisc => s_disc != null ? s_disc : (s_disc = Make(128, 128, (u, v) =>
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            float r = Mathf.Sqrt(x * x + y * y);
            return Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
        }));

        public static Sprite Star => s_star != null ? s_star : (s_star = Make(48, 48, (u, v) =>
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            float r = Mathf.Sqrt(x * x + y * y);
            float core = Mathf.Clamp01((0.32f - r) / 0.08f);
            float halo = Mathf.Pow(Mathf.Clamp01(1f - r), 2.6f) * 0.55f;
            return Mathf.Max(core, halo);
        }));

        public static Sprite Ring => s_ring != null ? s_ring : (s_ring = Make(64, 64, (u, v) =>
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            float r = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.07f);
        }));

        private static Sprite Make(int w, int h, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h));
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.hideFlags = HideFlags.DontSave;
            return s;
        }
    }
}
