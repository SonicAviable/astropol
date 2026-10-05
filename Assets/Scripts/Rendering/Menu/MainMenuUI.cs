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
    /// Главное меню: живая галактика на фоне (облёт камеры), анимированный логотип,
    /// стеклянное меню слева и выезжающие панели справа:
    /// «Новая игра» (параметры + предпросмотр галактики), «Загрузка», «Настройки», «Об игре».
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        public static MainMenuUI Instance { get; private set; }

        private enum Panel { None, NewGame, Load, Settings, About }

        public const string Version = "v0.9 · ранний доступ";

        private RectTransform _root;
        private RectTransform _window, _body;
        private Text _winTitle, _winSub;
        private Image _winIcon;
        private GameObject _news, _escHint;
        private Panel _open = Panel.None;
        private readonly Dictionary<Panel, GameObject> _pages = new Dictionary<Panel, GameObject>();
        private readonly List<MainMenuButton> _buttons = new List<MainMenuButton>();

        // Логотип
        private readonly List<Letter> _title = new List<Letter>();
        private readonly List<Letter> _subtitle = new List<Letter>();
        private RectTransform _underline;
        private Image _titleGlow;
        private Text _tagline;
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
            BuildLogo();
            BuildMenu();
            BuildWindow();
            BuildNewsCard();
            BuildFooter();

            var cam = Camera.main;
            if (cam != null && cam.GetComponent<MainMenuCamera>() == null) cam.gameObject.AddComponent<MainMenuCamera>();
        }

        private void Update()
        {
            _t += Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            AnimateLogo();
            FitWindow();

            if (Input.GetKeyDown(KeyCode.Escape) && _open != Panel.None && !IsTyping()) Close();
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

            // Затемнение слева под меню
            var left = LGBuild.Panel(shade, "Left", ink);
            left.sprite = MenuArt.HorizontalFade;
            left.rectTransform.anchorMin = new Vector2(0, 0);
            left.rectTransform.anchorMax = new Vector2(0.68f, 1);
            left.rectTransform.offsetMin = left.rectTransform.offsetMax = Vector2.zero;

            // Низ
            var bottom = LGBuild.Panel(shade, "Bottom", ink);
            bottom.sprite = MenuArt.VerticalFade;
            bottom.rectTransform.anchorMin = new Vector2(0, 0);
            bottom.rectTransform.anchorMax = new Vector2(1, 0.34f);
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
            logo.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -96), new Vector2(900, 200));
            LG.Ignore(logo.gameObject);

            _titleGlow = LGBuild.Panel(logo, "Glow", new Color(0.30f, 0.95f, 0.86f, 0f));
            _titleGlow.sprite = MenuArt.SoftDisc;
            _titleGlow.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(330, -60), new Vector2(980, 300));

            var word = LGBuild.Rect(logo, "Title");
            word.At(new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(800, 112));
            BuildTrackedWord(word, "ASTROPOLITY", GameFont.Bold, 98, FontStyle.Bold, 10f, _title, 0.35f, 0.055f);

            var sub = LGBuild.Rect(logo, "Subtitle");
            sub.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -122), new Vector2(800, 24));
            BuildTrackedWord(sub, "ГАЛАКТИЧЕСКАЯ СТРАТЕГИЯ", GameFont.Regular, 16, FontStyle.Normal, 7f, _subtitle, 1.15f, 0.022f);
            foreach (var l in _subtitle) l.Text.color = new Color(0.30f, 0.95f, 0.86f, 0f);

            var line = LGBuild.Panel(logo, "Underline", new Color(0.30f, 0.95f, 0.86f, 0.75f));
            _underline = line.rectTransform;
            _underline.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(6, -156), new Vector2(0, 2));
            LG.Line(line.gameObject, hairline: true);

            _tagline = LGBuild.Label(logo, "Исследуйте  ·  Расширяйтесь  ·  Развивайтесь  ·  Побеждайте", 13,
                                     new Color(0.62f, 0.74f, 0.80f, 0f), TextAnchor.UpperLeft);
            _tagline.fontStyle = FontStyle.Italic;
            _tagline.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -170), new Vector2(800, 22));
        }

        private static void BuildTrackedWord(RectTransform parent, string word, Font font, int size, FontStyle style,
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
            _underline.sizeDelta = new Vector2(560f * lineK, 2f);

            var tc = _tagline.color;
            tc.a = 0.9f * LGEase.OutCubic(Mathf.Clamp01((_t - 1.8f) / 0.8f));
            _tagline.color = tc;

            float glowIn = LGEase.OutCubic(Mathf.Clamp01((_t - 0.3f) / 1.5f));
            _titleGlow.color = new Color(neon.r, neon.g, neon.b, glowIn * (0.10f + 0.035f * Mathf.Sin(_t * 0.9f)));
        }

        // ================================================================ Меню слева

        private void BuildMenu()
        {
            var col = LGBuild.Rect(_root, "MenuColumn");
            col.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -330), new Vector2(450, 560));

            float y = 0f;
            float delay = 0.9f;
            var latest = SaveSystem.Latest();
            if (latest != null)
            {
                _continueBtn = AddButton(col, ref y, ref delay, Panel.None, LGIcon.Play, "ПРОДОЛЖИТЬ", ContinueCaption(latest),
                    UIManager.DS.NeonCyan, () =>
                    {
                        var l = SaveSystem.Latest();
                        if (l != null) SaveSystem.LoadSlot(l.Slot);
                    }, primary: true);
                y += 8f;
            }
            AddButton(col, ref y, ref delay, Panel.NewGame, LGIcon.Globe, "НОВАЯ ИГРА", "Создать галактику и выбрать цивилизацию",
                      UIManager.DS.NeonCyan, () => Open(Panel.NewGame), primary: latest == null);
            _loadBtn = AddButton(col, ref y, ref delay, Panel.Load, LGIcon.Load, "ЗАГРУЗИТЬ", LoadCaption(SaveSystem.List().Count),
                      new Color(0.55f, 0.80f, 1f), () => Open(Panel.Load));
            AddButton(col, ref y, ref delay, Panel.Settings, LGIcon.Gear, "НАСТРОЙКИ", "Графика, звук, интерфейс",
                      new Color(0.75f, 0.85f, 0.95f), () => Open(Panel.Settings));
            AddButton(col, ref y, ref delay, Panel.About, LGIcon.Info, "ОБ ИГРЕ", "Как играть и пути к победе",
                      UIManager.DS.Gold, () => Open(Panel.About));
            y += 8f;
            AddButton(col, ref y, ref delay, Panel.None, LGIcon.Power, "ВЫХОД", "Вернуться на рабочий стол",
                      UIManager.DS.Red, () => SceneFader.FadeOutThen(GameSession.Quit, 0.5f));
        }

        private MainMenuButton AddButton(RectTransform col, ref float y, ref float delay, Panel panel, LGIcon icon, string title,
                                         string caption, Color accent, System.Action onClick, bool primary = false)
        {
            var b = MainMenuButton.Create(col, icon, title, caption, accent, onClick, primary);
            b.Panel = (int)panel;
            var rt = (RectTransform)b.transform;
            rt.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -y), new Vector2(440, 64));
            b.AppearDelay = delay;
            _buttons.Add(b);
            y += 74f;
            delay += 0.07f;
            return b;
        }

        private MainMenuButton _continueBtn, _loadBtn;

        private static string ContinueCaption(SaveEntry e) =>
            $"{e.Meta.Empire}  ·  {e.Meta.GameDate}  ·  {SaveSystem.FormatSavedAt(e.SavedAtLocal)}";

        private static string LoadCaption(int count) => count == 0 ? "Сохранений пока нет" : $"Сохранений: {count}";

        private void OnEnable() => SaveSystem.OnSavesChanged += RefreshSaveInfo;
        private void OnDisable() => SaveSystem.OnSavesChanged -= RefreshSaveInfo;

        /// <summary>Сохранение удалили прямо из меню — обновить «Продолжить» и счётчик.</summary>
        private void RefreshSaveInfo()
        {
            var list = SaveSystem.List();
            if (_loadBtn != null) _loadBtn.Caption.text = LoadCaption(list.Count);
            if (_continueBtn == null) return;
            if (list.Count == 0) _continueBtn.Disable("Сохранений больше нет");
            else _continueBtn.Caption.text = ContinueCaption(list[0]);
        }

        // ================================================================ Окно справа

        private void BuildWindow()
        {
            var w = LGBuild.Rect(_root, "Window");
            w.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-70, -14), new Vector2(1080, 800));
            _window = w;
            var bg = w.gameObject.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            var fx = LG.Glass(w.gameObject, 28f);
            fx.SetRim(new Color(0.45f, 0.95f, 0.90f, 0.32f));
            var mo = LG.Motion(w.gameObject, LGAppear.Kind.SlideRight);
            mo.distance = 80f;
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
            TooltipHelper.Attach(close.gameObject, "Закрыть  <color=#8AA2A8>(Esc)</color>");

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
            float w = Mathf.Clamp(r.width - 600f - 70f, 760f, 1080f);
            float h = Mathf.Clamp(r.height - 170f, 600f, 820f);
            var cur = _window.sizeDelta;
            if (Mathf.Abs(cur.x - w) > 0.5f || Mathf.Abs(cur.y - h) > 0.5f) _window.sizeDelta = new Vector2(w, h);
        }

        private void Open(Panel p)
        {
            if (p == Panel.None) return;
            if (_open == p) { Close(); return; }
            SFXManager.Play("ui_click", 0.7f, 1.05f);
            _open = p;
            foreach (var b in _buttons) b.SetSelected(b.Panel == (int)p);

            switch (p)
            {
                case Panel.NewGame:
                    SetHeader(LGIcon.Globe, "НОВАЯ ИГРА", "Настройте галактику — цивилизацию вы выберете на следующем шаге");
                    break;
                case Panel.Load:
                    SetHeader(LGIcon.Load, "ЗАГРУЗКА", "Продолжите одну из своих партий");
                    break;
                case Panel.Settings:
                    SetHeader(LGIcon.Gear, "НАСТРОЙКИ", "Графика, звук, интерфейс и сохранения");
                    break;
                case Panel.About:
                    SetHeader(LGIcon.Info, "ОБ ИГРЕ", "Astropolity — космическая 4X-стратегия в реальном времени с паузой");
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
            if (_news != null) LG.Hide(_news);
        }

        private void Close()
        {
            if (_open == Panel.None) return;
            SFXManager.Play("ui_click", 0.5f, 0.9f);
            _open = Panel.None;
            foreach (var b in _buttons) b.SetSelected(false);
            LG.Hide(_window.gameObject);
            if (_news != null) LG.Show(_news);
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
            y = LGControls.Section(left, y, "ГАЛАКТИКА", LGIcon.Star);

            var sizes = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var tmp = new NewGameSettings { GalaxySize = i };
                sizes.Add($"{NewGameSettings.SizeNames[i]} · {tmp.StarCount} систем");
            }
            LGControls.Selector(LGControls.Row(left, ref y, "Размер", "Сколько звёздных систем в галактике", 270f),
                sizes, _ng.GalaxySize, i => { _ng.GalaxySize = i; RefreshNewGame(); });
            LGControls.Selector(LGControls.Row(left, ref y, "Форма", "Расположение звёзд: рукава спирали дают узкие проходы и фронты", 270f),
                NewGameSettings.ShapeNames, Mathf.Clamp(_ng.Shape, 0, NewGameSettings.ShapeNames.Length - 1),
                i => { _ng.Shape = i; RefreshNewGame(); });

            var seedHost = LGControls.Row(left, ref y, "Зерно генерации", "Одинаковое зерно — одинаковая карта", 270f);
            var inputHost = LGBuild.Rect(seedHost, "SeedInput");
            inputHost.Stretch(0, 0, 50, 0);
            _seedInput = LGControls.Input(inputHost, _ng.Seed.ToString(), "Случайное", 7);
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
            TooltipHelper.Attach(dice.gameObject, "Случайная галактика");

            y += 6f;
            y = LGControls.Section(left, y, "ПАРТИЯ", LGIcon.Leader);
            LGControls.Selector(LGControls.Row(left, ref y, "Сложность", "Доходы и агрессивность соседней империи", 270f),
                NewGameSettings.DifficultyNames, _ng.Difficulty, i => { _ng.Difficulty = i; RefreshNewGame(); });
            LGControls.Switch(LGControls.Row(left, ref y, "Обучение", "Пошаговые подсказки советника в начале", 270f),
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
            }, LGIcon.Play, "ДАЛЕЕ — ВЫБОР ЦИВИЛИЗАЦИИ", 14);
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
                    $"<b><color=#E8F6FA>Пути к победе</color></b>   " +
                    $"<color=#4DF2DB>форпосты в {_ng.DominationTarget} системах</color>  ·  " +
                    $"<color=#5CF599>25 технологий</color>  ·  <color=#FFCC52>больше очков, чем у соперника, в 2235 году</color>";
        }

        // ================================================================ Об игре

        private void BuildAboutPage(RectTransform page)
        {
            var left = LGBuild.Rect(page, "HowTo");
            left.Column(0f, 0.54f, 0, 14);
            float y = LGControls.Section(left, 0f, "КАК ИГРАТЬ", LGIcon.Info);
            Step(left, ref y, LGIcon.Ship, "1 · Исследуйте",
                 "Отправляйте научный корабль (ПКМ по звезде) изучать соседние системы — за каждую разведку вы получаете ресурсы.");
            Step(left, ref y, LGIcon.Starbase, "2 · Расширяйтесь",
                 "Строительный корабль ставит форпосты в разведанных системах. Чем больше систем — тем сильнее империя.");
            Step(left, ref y, LGIcon.Planet, "3 · Развивайте колонии",
                 "Заселяйте пригодные планеты и стройте районы: жильё, добычу, энергию и промышленность.");
            Step(left, ref y, LGIcon.Research, "4 · Изучайте технологии",
                 "Три слота исследований работают параллельно. Не оставляйте их пустыми — следите за алертами под верхней панелью.");
            Step(left, ref y, LGIcon.Fleet, "5 · Защищайтесь",
                 "Соседняя империя наблюдает за вашей мощью. Флот стоит энергии, а войны выигрываются осадой: держите корабли в системе врага без его защитников.");

            var right = LGBuild.Rect(page, "Side");
            right.Column(0.54f, 1f, 14, 0);
            float ry = LGControls.Section(right, 0f, "ПУТИ К ПОБЕДЕ", LGIcon.Trophy);
            Goal(right, ref ry, LGIcon.Starbase, "Доминирование", "Форпосты в 25 / 40 / 60 системах — по размеру галактики", UIManager.DS.NeonCyan);
            Goal(right, ref ry, LGIcon.Research, "Научная победа", "Изучите 25 технологий", UIManager.DS.Green);
            Goal(right, ref ry, LGIcon.Trophy, "Очки в 2235 году", "Обгоните соперника по системам, населению, науке и флоту. Соперник тоже может победить — наукой или экспансией", UIManager.DS.Gold);

            ry += 6f;
            ry = LGControls.Section(right, ry, "УПРАВЛЕНИЕ", LGIcon.Menu);
            var keys = LGBuild.Panel(right, "Keys", new Color(0.05f, 0.10f, 0.135f, 0.55f));
            keys.rectTransform.TopBand(ry, 176);
            LG.Platter(keys.gameObject, 16f).GlowMultiplier = 0f;
            var kt = LGBuild.Label(keys.transform,
                "<color=#4DF2DB><b>ЛКМ</b></color>  выбор      <color=#4DF2DB><b>ПКМ</b></color>  приказ флоту\n" +
                "<color=#4DF2DB><b>WASD</b></color>  камера      <color=#4DF2DB><b>Колесо</b></color>  масштаб\n" +
                "<color=#4DF2DB><b>Пробел</b></color>  пауза      <color=#4DF2DB><b>1 2 3</b></color>  скорость\n" +
                "<color=#4DF2DB><b>F5 / F9</b></color>  быстрое сохранение / загрузка\n" +
                "<color=#4DF2DB><b>Esc</b></color>  меню паузы",
                12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            kt.rectTransform.Stretch(18, 12, 14, 14);
            kt.lineSpacing = 1.45f;

            var credits = LGBuild.Label(page, $"ASTROPOLITY  {Version}   ·   Unity 6 · URP   ·   шрифт Play (SIL Open Font License)",
                                        10, UIManager.DS.TextMuted, TextAnchor.LowerRight);
            credits.rectTransform.Stretch(0, -18, 0, 0);
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
            row.rectTransform.TopBand(y, 64);
            var fx = LG.Platter(row.gameObject, 16f);
            fx.GlowMultiplier = 0f;
            fx.SetRim(new Color(col.r, col.g, col.b, 0.3f));
            var ic = LGIcons.Create(row.transform, icon, 24, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(24, 24));
            var text = LGBuild.Rect(row.transform, "Text");
            text.Stretch(58, 10, 14, 10);
            LGBuild.Label(text, title, 14, col, TextAnchor.UpperLeft, bold: true);
            LGBuild.Label(text, body, 11, UIManager.DS.TextMuted, TextAnchor.LowerLeft);
            y += 72f;
        }

        // ================================================================ «Что нового» и подвал

        private void BuildNewsCard()
        {
            var card = LGBuild.Panel(_root, "News", UIManager.DS.BgDeep, raycast: true);
            card.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-70, 76), new Vector2(390, 196));
            _news = card.gameObject;
            var fx = LG.Glass(_news, 22f);
            fx.SetRim(new Color(1f, 0.82f, 0.36f, 0.28f));
            var mo = LG.Motion(_news, LGAppear.Kind.SlideUp);
            mo.distance = 24f;
            mo.delay = 1.6f;
            mo.inDuration = 0.6f;

            var head = LGBuild.Rect(card.transform, "Head");
            head.TopBand(16, 20, 20, 20);
            var hi = LGIcons.Create(head, LGIcon.Star, 15, UIManager.DS.Gold);
            hi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(15, 15));
            var ht = LGBuild.Label(head, "ЧТО НОВОГО", 12, UIManager.DS.Gold, TextAnchor.MiddleLeft, bold: true);
            ht.rectTransform.offsetMin = new Vector2(22, 0);
            LGBuild.Label(head, Version, 10, UIManager.DS.TextMuted, TextAnchor.MiddleRight);

            string[] items =
            {
                "Главное меню, сохранения и автосохранение",
                "Настройки графики, звука и интерфейса",
                "Новый интерфейс «жидкое стекло»",
                "Алерты событий под верхней панелью",
            };
            float y = 50f;
            foreach (var s in items)
            {
                var r = LGBuild.Rect(card.transform, "Item");
                r.TopBand(y, 22, 20, 16);
                var ic = LGIcons.Create(r, LGIcon.Check, 12, UIManager.DS.Green);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(1, 0), new Vector2(12, 12));
                var t = LGBuild.Label(r, s, 12, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
                t.rectTransform.offsetMin = new Vector2(22, 0);
                y += 32f;
            }
        }

        private void BuildFooter()
        {
            var ver = LGBuild.Label(_root, $"<b>ASTROPOLITY</b>   {Version}", 11, new Color(0.62f, 0.74f, 0.80f, 0.8f), TextAnchor.LowerLeft);
            ver.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(110, 30), new Vector2(500, 20));
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

    // ====================================================================== Кнопка главного меню

    public sealed class MainMenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public int Panel;
        public float AppearDelay;

        private RectTransform _rt, _content, _accent, _arrow;
        private CanvasGroup _cg;
        private Text _title;
        private Image _iconBg;
        private LiquidGlassEffect _fx, _iconFx;
        private Color _accentColor;
        private System.Action _onClick;
        private bool _over, _selected, _primary;
        private float _h, _hv, _t;

        public static MainMenuButton Create(Transform parent, LGIcon icon, string title, string caption, Color accent,
                                            System.Action onClick, bool primary)
        {
            var bg = LGBuild.Panel(parent, "Btn_" + title, primary ? new Color(0.06f, 0.26f, 0.28f, 1f) : UIManager.DS.BgDeep, raycast: true);
            var b = bg.gameObject.AddComponent<MainMenuButton>();
            b._accentColor = accent;
            b._onClick = onClick;
            b._primary = primary;
            b._rt = bg.rectTransform;
            b._cg = bg.gameObject.AddComponent<CanvasGroup>();
            b._cg.alpha = 0f;
            b._fx = LG.Glass(bg.gameObject, 20f);
            b._fx.SetRim(new Color(accent.r, accent.g, accent.b, primary ? 0.55f : 0.16f));
            b._fx.GlowMultiplier = primary ? 0.8f : 0.3f;

            b._content = LGBuild.Rect(bg.transform, "Content");
            b._content.Stretch();

            var acc = LGBuild.Panel(b._content, "Accent", accent);
            b._accent = acc.rectTransform;
            b._accent.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(7, 0), new Vector2(3, 0));
            LG.Fill(acc.gameObject);

            b._iconBg = LGBuild.Panel(b._content, "IconBg", new Color(accent.r * 0.18f, accent.g * 0.18f, accent.b * 0.18f, 1f));
            b._iconBg.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(42, 42));
            b._iconFx = LG.Platter(b._iconBg.gameObject, 21f);
            b._iconFx.FillMultiplier = 1.8f;
            b._iconFx.SetRim(new Color(accent.r, accent.g, accent.b, 0.45f));
            var ic = LGIcons.Create(b._iconBg.transform, icon, 20, accent);
            ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));

            var text = LGBuild.Rect(b._content, "Text");
            text.Stretch(76, 11, 48, 11);
            b._title = LGBuild.Label(text, title, 17, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            var cap = LGBuild.Label(text, caption, 11, UIManager.DS.TextMuted, TextAnchor.LowerLeft);
            cap.horizontalOverflow = HorizontalWrapMode.Wrap;
            cap.verticalOverflow = VerticalWrapMode.Truncate;
            b.Caption = cap;

            var arrow = LGIcons.Create(b._content, LGIcon.Back, 16, accent);
            b._arrow = arrow.rectTransform;
            b._arrow.At(new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-24, 0), new Vector2(16, 16));
            b._arrow.localRotation = Quaternion.Euler(0, 0, 180f);
            return b;
        }

        public Text Caption { get; private set; }
        private bool _disabled;

        public void SetSelected(bool v) => _selected = v;

        public void Disable(string caption)
        {
            _disabled = true;
            _over = false;
            Caption.text = caption;
            _title.color = UIManager.DS.TextMuted;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _over = true;
            if (SFXManager.Has("ui_hover")) SFXManager.Play("ui_hover", 0.35f, 1.1f);
        }

        public void OnPointerExit(PointerEventData e) => _over = false;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || _cg.alpha < 0.5f || _disabled) return;
            _h += 0.35f;   // короткий «толчок» при нажатии
            _onClick?.Invoke();
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _t += dt;

            // Появление: выезд слева с затуханием
            float k = LGEase.OutCubic(Mathf.Clamp01((_t - AppearDelay) / 0.55f));
            _cg.alpha = _disabled ? k * 0.45f : k;
            if (_disabled) { _over = false; _selected = false; }
            _cg.blocksRaycasts = k > 0.5f;

            float target = _over || _selected ? 1f : 0f;
            _h = LGEase.Spring(_h, target, ref _hv, 3.4f, 0.62f, dt);
            float h = Mathf.Clamp01(_h);

            _content.anchoredPosition = new Vector2(-46f * (1f - k) + 12f * _h, 0f);
            _accent.sizeDelta = new Vector2(3f, Mathf.Lerp(_primary ? 22f : 0f, 40f, h));
            _arrow.anchoredPosition = new Vector2(-30f + 10f * h, 0f);
            var ac = _arrow.GetComponent<Image>();
            if (ac != null) { var c = _accentColor; c.a = h; ac.color = c; }

            _fx.SetHover(h * 0.8f);
            _fx.SetRim(new Color(_accentColor.r, _accentColor.g, _accentColor.b, Mathf.Lerp(_primary ? 0.55f : 0.16f, 0.85f, h)));
            _iconFx.SetRim(new Color(_accentColor.r, _accentColor.g, _accentColor.b, Mathf.Lerp(0.45f, 0.95f, h)));
            _title.color = _disabled ? UIManager.DS.TextMuted
                         : Color.Lerp(UIManager.DS.TextPrimary, Color.Lerp(_accentColor, Color.white, 0.55f), h * 0.6f);
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
        private RectTransform _capRing, _aiRing;

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
        }

        private void Rebuild(NewGameSettings s)
        {
            if (_layer != null) Destroy(_layer.gameObject);
            _items.Clear();
            _capRing = _aiRing = null;
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

            // Столица игрока — система 0, столица соперника — самая дальняя (как у ИИ)
            int ai = 0;
            float far = -1f;
            for (int i = 1; i < systems.Count; i++)
            {
                float dd = Vector3.Distance(systems[0].Position, systems[i].Position);
                if (dd > far) { far = dd; ai = i; }
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

            _capRing = Marker(P(systems[0]), UIManager.DS.NeonCyan, "ВЫ");
            if (ai != 0) _aiRing = Marker(P(systems[ai]), UIManager.DS.Red, "СОПЕРНИК");

            _caption.text = $"{NewGameSettings.SizeNames[s.GalaxySize]} галактика   ·   {NewGameSettings.ShapeNames[Mathf.Clamp(s.Shape, 0, 3)].ToLower()}   ·   {systems.Count} систем   ·   {lanes.Count} гиперкоридоров   ·   сид {s.Seed}";
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
