using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Экран «Настройки» главного меню во весь экран: вкладки «Графика / Звук / Интерфейс / Игра» сверху,
    /// на «Графике» — качество четырьмя карточками-картинками, ниже две колонки: «Экран» и «Точная настройка».
    /// Пункты с короткими вариантами — сегментные переключатели, длинные списки (разрешение, язык) — селектор.
    /// Изменения применяются сразу. В меню паузы остаётся компактная <see cref="SettingsPanel"/>.
    /// </summary>
    public sealed class SettingsScreen : MonoBehaviour
    {
        private const float Side = 80f, Top = 170f, Bottom = 110f, ControlW = 420f;
        private static int s_tab;

        private System.Action _onClose;
        private CanvasGroup _cg, _contentCg;
        private RectTransform _content;
        private ScrollRect _scroll;
        private MenuSegment _tabs;
        private float _k, _kv, _fade = 1f, _lastSfxPreview;
        private bool _shown;
        private readonly List<MenuSlideIn> _slides = new List<MenuSlideIn>();

        // Качество: карточки пресетов и пункты, которые пресет переставляет
        private readonly List<QualityCard> _cards = new List<QualityCard>();
        private MenuSegment _qScale, _qUpscale, _qSharp, _qAA, _qShadows, _qPost, _qTex;
        private Text _customChip;

        public static SettingsScreen Create(RectTransform root, System.Action onClose)
        {
            var rt = LGBuild.Rect(root, "Settings");
            rt.Stretch();
            var s = rt.gameObject.AddComponent<SettingsScreen>();
            s._onClose = onClose;
            s.Build(rt);
            rt.gameObject.SetActive(false);
            return s;
        }

        private void Build(RectTransform rt)
        {
            GameSettings.EnsureLoaded();
            GraphicsQuality.EnsureLoaded();
            _cg = rt.gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;
            var dim = LGBuild.Panel(rt, "Dim", new Color(0.005f, 0.012f, 0.022f, 0.55f), raycast: true);
            dim.rectTransform.Stretch();
            LG.Ignore(dim.gameObject);

            var cap = LGBuild.Label(rt, Loc.T("ПАРАМЕТРЫ"), 13, UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true);
            cap.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Side, -56), new Vector2(400, 18));
            var title = LGBuild.Label(rt, Loc.T("НАСТРОЙКИ"), 40, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Side, -76), new Vector2(600, 52));

            var tabsHost = LGBuild.Rect(rt, "Tabs");
            tabsHost.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -82), new Vector2(720, 50));
            _tabs = MenuSegment.Create(tabsHost, new[] { Loc.T("Графика"), Loc.T("Звук"), Loc.T("Интерфейс"), Loc.T("Игра") }, null, s_tab, ShowTab);
            _tabs.GetComponent<RectTransform>().Stretch();
            _slides.Add(MenuSlideIn.Add(tabsHost.gameObject, 0.05f, new Vector2(0f, 16f)));

            var back = LGBuild.Button(rt, "Back", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.3f), () => _onClose?.Invoke(), null, Loc.T("НАЗАД"), 14);
            ((RectTransform)back.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Side, -78), new Vector2(170, 46));

            // Прокручиваемая область содержимого
            var view = LGBuild.Rect(rt, "View");
            view.Stretch(Side, Bottom, Side, Top);
            view.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 18);
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            LG.Ignore(view.gameObject, includeChildren: false);
            _content = LGBuild.Rect(view, "Content");
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = new Vector2(1, 1);
            _content.pivot = new Vector2(0.5f, 1);
            _content.sizeDelta = Vector2.zero;
            _contentCg = _content.gameObject.AddComponent<CanvasGroup>();
            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.elasticity = 0.08f;
            _scroll.scrollSensitivity = 36f;
            _scroll.viewport = view;
            _scroll.content = _content;

            // Низ
            var reset = LGBuild.Button(rt, "Reset", UIManager.DS.BtnNeutral, new Color(1f, 0.82f, 0.4f, 0.45f), () =>
            {
                GameSettings.ResetDefaults();
                ShowTab(_tabs.Value);
                NotificationCenter.Show(Loc.T("Настройки сброшены"), Loc.T("Восстановлены значения по умолчанию"), NotificationCenter.Kind.Info, 3f);
            }, null, Loc.T("ПО УМОЛЧАНИЮ"), 13);
            ((RectTransform)reset.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(Side, 44), new Vector2(230, 46));
            var hint = LGBuild.Label(rt, Loc.T("Изменения применяются и сохраняются сразу"), 13, new Color(0.56f, 0.66f, 0.71f), TextAnchor.MiddleRight);
            hint.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-Side, 44), new Vector2(600, 46));
        }

        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _shown = true;
            foreach (var s in _slides) s.Restart();
            ShowTab(s_tab);
        }

        public void Hide() => _shown = false;

        // ------------------------------------------------------------------ Вкладки

        private void ShowTab(int tab)
        {
            s_tab = tab;
            LGBuild.Clear(_content);
            _cards.Clear();
            _qScale = _qUpscale = _qSharp = _qAA = _qShadows = _qPost = _qTex = null;
            _customChip = null;

            float h;
            switch (tab)
            {
                case 0: h = BuildGraphics(); break;
                case 1: h = BuildAudio(); break;
                case 2: h = BuildInterface(); break;
                default: h = BuildGame(); break;
            }
            _content.sizeDelta = new Vector2(0, h + 12f);
            _content.anchoredPosition = Vector2.zero;
            _scroll.velocity = Vector2.zero;
            _fade = 0f;
        }

        /// <summary>Две колонки от отступа y сверху; в каждую строят свои строки.</summary>
        private (RectTransform left, RectTransform right) Columns(float y)
        {
            RectTransform Col(string name, float a0, float a1, float l, float r)
            {
                var c = LGBuild.Rect(_content, name);
                c.anchorMin = new Vector2(a0, 0);
                c.anchorMax = new Vector2(a1, 1);
                c.offsetMin = new Vector2(l, 0);
                c.offsetMax = new Vector2(-r, -y);
                return c;
            }
            return (Col("Left", 0f, 0.5f, 0f, 12f), Col("Right", 0.5f, 1f, 12f, 0f));
        }

        private static float Section(RectTransform col, float y, string title)
        {
            var t = LGBuild.Label(col, title, 13, UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.TopBand(y, 18);
            return y + 30f;
        }

        private static MenuSegment Seg(RectTransform col, ref float y, string label, string desc, IList<string> opts, int value, System.Action<int> onChange)
        {
            var host = LGControls.Row(col, ref y, label, desc, ControlW);
            var s = MenuSegment.Create(host, opts, null, value, onChange);
            s.GetComponent<RectTransform>().Stretch(0, -3, 0, -3);
            return s;
        }

        // ------------------------------------------------------------------ Графика

        private float BuildGraphics()
        {
            float y = Section(_content, 0f, Loc.T("КАЧЕСТВО ГРАФИКИ"));
            _customChip = LGBuild.Label(_content, Loc.T("Сейчас: свой набор настроек"), 13, new Color(1f, 0.82f, 0.48f), TextAnchor.UpperRight, bold: true);
            _customChip.rectTransform.TopBand(0, 18);

            var row = LGBuild.Rect(_content, "Presets");
            row.TopBand(y, 186);
            string[] desc =
            {
                Loc.T("для слабых ПК и ноутбуков"), Loc.T("баланс скорости и вида"),
                Loc.T("тени, постобработка, сглаживание"), Loc.T("всё на максимум")
            };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var card = QualityCard.Create(row, i, GraphicsQuality.PresetNames[i], desc[i], () =>
                {
                    GameSettings.GraphicsChanged(() => GraphicsQuality.SetPreset(idx));
                    SyncQuality();
                });
                var crt = (RectTransform)card.transform;
                crt.anchorMin = new Vector2(i / 4f, 0);
                crt.anchorMax = new Vector2((i + 1) / 4f, 1);
                crt.offsetMin = new Vector2(i == 0 ? 0 : 9, 0);
                crt.offsetMax = new Vector2(i == 3 ? 0 : -9, 0);
                card.AppearDelay = 0.05f + 0.06f * i;
                _cards.Add(card);
            }
            y += 186f + 30f;

            var (left, right) = Columns(y);
            float ly = Section(left, 0f, Loc.T("ЭКРАН"));
            Seg(left, ref ly, Loc.T("Режим экрана"), Loc.T("Полный экран без рамки удобнее для переключения окон"),
                GameSettings.ScreenModeNames, GameSettings.ScreenMode, GameSettings.SetScreenMode);
            var res = new List<string>();
            for (int i = 0; i < GameSettings.Resolutions.Count; i++) res.Add(GameSettings.ResolutionName(i));
            LGControls.Selector(LGControls.Row(left, ref ly, Loc.T("Разрешение"), null, ControlW), res, GameSettings.ResolutionIndex, GameSettings.SetResolution);
            LGControls.Switch(LGControls.Row(left, ref ly, Loc.T("Вертикальная синхронизация"), Loc.T("Убирает разрывы кадра"), ControlW),
                GameSettings.VSync, GameSettings.SetVSync);
            var fps = new List<string>();
            foreach (int f in GameSettings.FpsOptions) fps.Add(f < 0 ? "∞" : f.ToString());
            Seg(left, ref ly, Loc.T("Ограничение частоты кадров"), Loc.T("Работает при выключенной синхронизации"), fps, GameSettings.FpsIndex, GameSettings.SetFps);
            LGControls.Slider(LGControls.Row(left, ref ly, Loc.T("Неоновое свечение"), Loc.T("Яркость ореола вокруг линий и выделенных элементов"), ControlW),
                GameSettings.Glow, 0f, 0.8f, v => Mathf.RoundToInt(v / 0.8f * 100f) + "%", GameSettings.SetGlow);

            float ry = Section(right, 0f, Loc.T("ТОЧНАЯ НАСТРОЙКА"));
            _qScale = Seg(right, ref ry, Loc.T("Масштаб рендера"), Loc.T("Ниже — быстрее, интерфейс остаётся чётким"),
                GraphicsQuality.ScaleNames, GraphicsQuality.ScaleIndex, i => QualityItem(() => GraphicsQuality.SetScale(i)));
            _qUpscale = Seg(right, ref ry, Loc.T("Масштабирование"), Loc.T("FSR чётче растягивает картинку при масштабе ниже 100%"),
                GraphicsQuality.UpscalerNames, GraphicsQuality.Upscaler, i => GameSettings.GraphicsChanged(() => GraphicsQuality.SetUpscaler(i)));
            _qSharp = Seg(right, ref ry, Loc.T("Резкость FSR"), Loc.T("Насколько подчёркивать края после масштабирования"),
                GraphicsQuality.SharpnessNames, GraphicsQuality.Sharpness, i => GameSettings.GraphicsChanged(() => GraphicsQuality.SetSharpness(i)));
            _qAA = Seg(right, ref ry, Loc.T("Сглаживание"), Loc.T("Убирает «лесенку» на краях кораблей и планет"),
                GraphicsQuality.AANames, GraphicsQuality.AntiAliasing, i => QualityItem(() => GraphicsQuality.SetAntiAliasing(i)));
            _qShadows = Seg(right, ref ry, Loc.T("Тени"), Loc.T("Тени планет и кораблей вблизи"),
                GraphicsQuality.ShadowNames, GraphicsQuality.Shadows, i => QualityItem(() => GraphicsQuality.SetShadows(i)));
            _qPost = Seg(right, ref ry, Loc.T("Постобработка"), Loc.T("Свечение звёзд, тонирование и виньетка"),
                GraphicsQuality.OnOffNames, GraphicsQuality.PostFx ? 1 : 0, i => QualityItem(() => GraphicsQuality.SetPostFx(i == 1)));
            _qTex = Seg(right, ref ry, Loc.T("Текстуры"), Loc.T("Половинные экономят видеопамять"),
                GraphicsQuality.TextureNames, GraphicsQuality.FullTextures ? 1 : 0, i => QualityItem(() => GraphicsQuality.SetFullTextures(i == 1)));

            SyncCards();
            return y + Mathf.Max(ly, ry);
        }

        private void QualityItem(System.Action set)
        {
            GameSettings.GraphicsChanged(set);
            SyncCards();
        }

        private void SyncQuality()
        {
            _qScale?.Set(GraphicsQuality.ScaleIndex, false);
            _qUpscale?.Set(GraphicsQuality.Upscaler, false);
            _qSharp?.Set(GraphicsQuality.Sharpness, false);
            _qAA?.Set(GraphicsQuality.AntiAliasing, false);
            _qShadows?.Set(GraphicsQuality.Shadows, false);
            _qPost?.Set(GraphicsQuality.PostFx ? 1 : 0, false);
            _qTex?.Set(GraphicsQuality.FullTextures ? 1 : 0, false);
            SyncCards();
        }

        private void SyncCards()
        {
            int p = GraphicsQuality.CurrentPreset;
            foreach (var c in _cards) c.Selected = c.Index == p;
            if (_customChip != null) _customChip.gameObject.SetActive(p == GraphicsQuality.PresetCustom);
        }

        // ------------------------------------------------------------------ Звук

        private float BuildAudio()
        {
            var (left, right) = Columns(0f);
            float ly = Section(left, 0f, Loc.T("ГРОМКОСТЬ"));
            LGControls.Slider(LGControls.Row(left, ref ly, Loc.T("Общая громкость"), null, ControlW),
                GameSettings.MasterVolume, 0f, 1f, Percent, GameSettings.SetMaster);
            LGControls.Slider(LGControls.Row(left, ref ly, Loc.T("Музыка"), Loc.T("Фоновый саундтрек"), ControlW),
                GameSettings.MusicVolume, 0f, 1f, Percent, GameSettings.SetMusic);
            LGControls.Slider(LGControls.Row(left, ref ly, Loc.T("Звуки и интерфейс"), Loc.T("Щелчки, корабли, уведомления"), ControlW),
                GameSettings.SfxVolume, 0f, 1f, Percent, v =>
                {
                    GameSettings.SetSfx(v);
                    if (Time.unscaledTime - _lastSfxPreview > 0.12f)
                    {
                        _lastSfxPreview = Time.unscaledTime;
                        SFXManager.Play("ui_click", 0.6f, 1.2f);
                    }
                });

            float ry = Section(right, 0f, Loc.T("ПОВЕДЕНИЕ"));
            LGControls.Switch(LGControls.Row(right, ref ry, Loc.T("Тишина в фоне"), Loc.T("Приглушать звук, когда окно игры неактивно"), ControlW),
                GameSettings.MuteInBackground, GameSettings.SetMuteInBackground);
            LGControls.Switch(LGControls.Row(right, ref ry, Loc.T("Вспышка и вибрация при тревоге"), Loc.T("При атаке и банкротстве края экрана вспыхивают, геймпад вибрирует"), ControlW),
                GameSettings.AlarmFx, GameSettings.SetAlarmFx);
            return Mathf.Max(ly, ry);
        }

        // ------------------------------------------------------------------ Интерфейс

        private float BuildInterface()
        {
            var (left, right) = Columns(0f);
            float ly = Section(left, 0f, Loc.T("ИНТЕРФЕЙС"));

            // Язык: подпись на обоих языках, чтобы её нашёл любой игрок; применяется после перезапуска
            int lang = System.Array.IndexOf(Loc.Codes, Loc.Saved);
            var langRow = LGControls.Row(left, ref ly, "Язык · Language",
                Loc.Saved == Loc.Current ? "Перезапустите игру после смены · Restart the game to apply"
                                         : "<color=#F2C14E>Перезапустите игру, чтобы сменить язык · Restart the game to apply</color>", ControlW);
            LGControls.Selector(langRow, Loc.Names, Mathf.Max(0, lang), i =>
            {
                Loc.Save(Loc.Codes[i]);
                NotificationCenter.Show(Loc.Codes[i] == "en" ? "Language: English" : "Язык: русский",
                    Loc.Codes[i] == Loc.Current ? (Loc.English ? "No restart needed" : "Перезапуск не нужен")
                                                : "Перезапустите игру · Restart the game to apply", NotificationCenter.Kind.Info, 6f);
            });
            var scales = new List<string>();
            foreach (float s in GameSettings.UIScaleOptions) scales.Add(Mathf.RoundToInt(s * 100f) + "%");
            Seg(left, ref ly, Loc.T("Масштаб интерфейса"), Loc.T("Крупнее — для больших экранов и 4K"), scales, GameSettings.UIScaleIndex, GameSettings.SetUIScale);
            LGControls.Slider(LGControls.Row(left, ref ly, Loc.T("Яркость интерфейса"), Loc.T("Светлее панели окон, кнопок и списков"), ControlW),
                GameSettings.UIBrightness, 0f, 1f, Percent, GameSettings.SetUIBrightness);
            LGControls.Switch(LGControls.Row(left, ref ly, Loc.T("Всплывающие подсказки"), Loc.T("Пояснения при наведении на элементы"), ControlW),
                GameSettings.Tooltips, GameSettings.SetTooltips);

            float ry = Section(right, 0f, Loc.T("КАМЕРА"));
            LGControls.Slider(LGControls.Row(right, ref ry, Loc.T("Скорость камеры"), Loc.T("Перемещение клавишами WASD / стрелками"), ControlW),
                GameSettings.CameraSpeed, 0.4f, 2.5f, v => $"×{v:0.0}", GameSettings.SetCameraSpeed);
            LGControls.Switch(LGControls.Row(right, ref ry, Loc.T("Прокрутка у края экрана"), Loc.T("Камера двигается, когда курсор у края"), ControlW),
                GameSettings.EdgeScroll, GameSettings.SetEdgeScroll);
            return Mathf.Max(ly, ry);
        }

        // ------------------------------------------------------------------ Игра

        private float BuildGame()
        {
            var (left, right) = Columns(0f);
            float ly = Section(left, 0f, Loc.T("СОВЕТНИК"));
            LGControls.Switch(LGControls.Row(left, ref ly, Loc.T("Советы по ходу партии"), Loc.T("ОРАКУЛ-7 подскажет: простаивающие корабли, дефицит, угроза колонии, шанс на штурм"), ControlW),
                GameSettings.Advisor, GameSettings.SetAdvisor);

            float ry = Section(right, 0f, Loc.T("СОХРАНЕНИЯ"));
            var auto = new List<string>();
            foreach (int m in GameSettings.AutosaveOptions)
                auto.Add(m == 0 ? Loc.T("Выключено") : m == 1 ? Loc.T("Каждый месяц") : m == 12 ? Loc.T("Каждый год") : Loc.F("Каждые {0} мес.", m));
            LGControls.Selector(LGControls.Row(right, ref ry, Loc.T("Автосохранение"), Loc.T("По игровому календарю, в слот «Автосохранение»"), ControlW),
                auto, GameSettings.AutosaveIndex, GameSettings.SetAutosave);
            var keys = LGControls.Row(right, ref ry, Loc.T("Быстрое сохранение"), Loc.T("F5 — сохранить, F9 — загрузить"), ControlW);
            var k = LGBuild.Label(keys, "<color=#4DF2DB><b>F5</b></color>  /  <color=#4DF2DB><b>F9</b></color>", 15,
                                  UIManager.DS.TextPrimary, TextAnchor.MiddleRight);
            k.rectTransform.offsetMax = new Vector2(-8, 0);
            var folder = LGControls.Row(right, ref ry, Loc.T("Папка сохранений"), SaveSystem.Folder.Replace('\\', '/'), ControlW, 70f);
            var open = LGBuild.Button(folder, "Open", UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.3f),
                () => Application.OpenURL("file:///" + SaveSystem.Folder.Replace('\\', '/')), null, Loc.T("ОТКРЫТЬ"), 12);
            ((RectTransform)open.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(170, 38));
            return Mathf.Max(ly, ry);
        }

        private static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _k = LGEase.Spring(_k, _shown ? 1f : 0f, ref _kv, 4.5f, 0.9f, dt);
            _cg.alpha = Mathf.Clamp01(_k);
            _cg.blocksRaycasts = _cg.interactable = _shown;
            if (!_shown && _k < 0.01f) { gameObject.SetActive(false); return; }

            // Смена вкладки — содержимое мягко проявляется
            if (_fade < 1f)
            {
                _fade = Mathf.Min(1f, _fade + dt / 0.3f);
                _contentCg.alpha = LGEase.OutCubic(_fade);
            }
        }
    }

    /// <summary>
    /// Карточка уровня качества: картинка (чем выше уровень, тем ярче и насыщеннее), название и пояснение.
    /// Выбранная — с яркой кромкой и меткой «ВЫБРАНО»; при наведении поднимается и картинка приближается.
    /// </summary>
    public sealed class QualityCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public int Index;
        public bool Selected;
        public float AppearDelay;
        private CanvasGroup _cg;
        private RectTransform _body;
        private LiquidGlassEffect _fx;
        private RawImage _art;
        private Rect _uv;
        private GameObject _badge;
        private System.Action _onClick;
        private bool _over;
        private float _h, _hv, _s, _sv, _t;

        private static readonly float[] Bright = { 0.42f, 0.68f, 0.9f, 1.05f };

        public static QualityCard Create(RectTransform parent, int index, string name, string desc, System.Action onClick)
        {
            var root = LGBuild.Rect(parent, "Quality" + index);
            var c = root.gameObject.AddComponent<QualityCard>();
            c.Index = index;
            c._onClick = onClick;
            c._cg = root.gameObject.AddComponent<CanvasGroup>();
            c._cg.alpha = 0f;

            var body = LGBuild.Panel(root, "Body", new Color(0.03f, 0.06f, 0.10f, 0.95f), raycast: true);
            c._body = body.rectTransform;
            c._body.Stretch();
            c._fx = LG.Glass(body.gameObject, 18f);
            var mask = LG.RoundedMask(body.transform, 17f, 2f);
            var tex = Resources.Load<Texture2D>("Menu/menu_bg");
            if (tex != null)
            {
                c._art = LGBuild.Rect(mask, "Art").gameObject.AddComponent<RawImage>();
                c._art.rectTransform.Stretch();
                c._art.texture = tex;
                c._art.raycastTarget = false;
                c._uv = new Rect(0.2f + 0.1f * index, 0.30f, 0.45f, 0.45f * 1.79f / 2.3f);
                float b = Bright[Mathf.Clamp(index, 0, 3)];
                // Низкое — блёклое, Ультра — насыщенное
                c._art.color = index == 0 ? new Color(b, b, b * 1.05f) : new Color(b, b, b);
            }
            var shade = LGBuild.Panel(mask, "Shade", new Color(0.02f, 0.045f, 0.08f, 1f));
            shade.sprite = MenuArt.VerticalFade;
            shade.rectTransform.Stretch();
            LG.Ignore(shade.gameObject);

            var t = LGBuild.Label(body.transform, name, 24, Color.white, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.Stretch(20, 40, 20, 0);
            var d = LGBuild.Label(body.transform, desc, 13, new Color(0.62f, 0.72f, 0.76f), TextAnchor.LowerLeft);
            d.rectTransform.Stretch(20, 16, 20, 0);

            var badge = LGBuild.Panel(body.transform, "Selected", new Color(0.10f, 0.32f, 0.32f, 0.9f));
            badge.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14, -14), new Vector2(96, 24));
            LG.Chip(badge.gameObject, new Color(0.44f, 0.90f, 0.85f, 0.7f), 6f);
            var bt = LGBuild.Label(badge.transform, Loc.T("ВЫБРАНО"), 11, new Color(0.56f, 0.94f, 0.90f), TextAnchor.MiddleCenter, bold: true);
            bt.rectTransform.Stretch();
            c._badge = badge.gameObject;
            return c;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _over = true;
            if (SFXManager.Has("ui_hover")) SFXManager.Play("ui_hover", 0.3f, 1.1f);
        }

        public void OnPointerExit(PointerEventData e) => _over = false;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || Selected) return;
            SFXManager.Play("ui_click", 0.8f, 1f);
            _onClick?.Invoke();
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _t += dt;
            float k = LGEase.OutCubic(Mathf.Clamp01((_t - AppearDelay) / 0.5f));
            _cg.alpha = k;
            _h = LGEase.Spring(_h, _over ? 1f : 0f, ref _hv, 4f, 0.7f, dt);
            _s = LGEase.Spring(_s, Selected ? 1f : 0f, ref _sv, 4f, 0.8f, dt);
            float h = Mathf.Clamp01(_h), s = Mathf.Clamp01(_s);
            _body.anchoredPosition = new Vector2(0f, -30f * (1f - k) + 6f * h + 4f * s);
            float sc = 1f + 0.02f * h;
            _body.localScale = new Vector3(sc, sc, 1f);
            float pulse = s * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f));
            _fx.SetRim(new Color(0.44f, 0.90f, 0.85f, Mathf.Lerp(0.12f, 0.9f, Mathf.Max(s, h * 0.6f)) - 0.12f * pulse));
            _fx.SetHover(Mathf.Max(h * 0.6f, s * 0.35f));
            _fx.SetPulse(0.2f * pulse);
            if (_badge.activeSelf != Selected) _badge.SetActive(Selected);
            if (_art != null)
            {
                float z = 1f - 0.08f * h;
                var size = _uv.size * z;
                _art.uvRect = new Rect(_uv.center - size * 0.5f, size);
            }
        }
    }
}
