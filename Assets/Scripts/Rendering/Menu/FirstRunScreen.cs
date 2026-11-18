using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Окно первого запуска поверх главного меню (макет Tools/menu_mock/firstrun.html, вариант B): слева приветствие,
    /// сведения о компьютере и рекомендуемое качество, кнопка «Начать»; справа — язык, качество и режим экрана,
    /// масштаб интерфейса, громкость и опыт игрока (обучение, советник и сложность новой партии по умолчанию).
    ///
    /// Всё применяется сразу, как в «Настройках». Качество при открытии ставится по железу. Смена языка
    /// перезагружает меню (GameSession.ReturnToMainMenu) — окно открывается снова уже на новом языке.
    /// Esc и Enter закрывают окно с выбранными значениями. Пройденное окно больше не показывается.
    /// </summary>
    public sealed class FirstRunScreen : MonoBehaviour
    {
        private const float MaxW = 1500f, MaxH = 860f, LeftW = 520f, ControlW = 440f;

        private System.Action _onDone;
        private RectTransform _panel, _root;
        private CanvasGroup _cg;
        private Text _expDesc;
        private MenuSegment _quality;
        private MenuPrimaryButton _start;
        private int _exp, _recommended;
        private float _k, _kv, _age;
        private bool _closing;

        public static bool IsOpen { get; private set; }

        public static FirstRunScreen Create(RectTransform root, System.Action onDone)
        {
            var rt = LGBuild.Rect(root, "FirstRun");
            rt.Stretch();
            var s = rt.gameObject.AddComponent<FirstRunScreen>();
            s._root = root;
            s._onDone = onDone;
            s.Build(rt);
            IsOpen = true;
            return s;
        }

        private void OnDestroy() => IsOpen = false;

        private void Build(RectTransform rt)
        {
            GameSettings.EnsureLoaded();
            GraphicsQuality.EnsureLoaded();
            GameSettings.BeginFirstRun();

            // Качество по железу — только пока игрок его сам не выбирал (после смены языка окно строится заново)
            _recommended = GraphicsQuality.RecommendedPreset();
            if (!PlayerPrefs.HasKey("gfx.scale"))
                GameSettings.GraphicsChanged(() => GraphicsQuality.SetPreset(_recommended));
            _exp = GameSettings.Experience >= 0 ? GameSettings.Experience : 0;

            _cg = rt.gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;
            var dim = LGBuild.Panel(rt, "Dim", new Color(0.005f, 0.012f, 0.022f, 0.5f), raycast: true);
            dim.rectTransform.Stretch();
            LG.Ignore(dim.gameObject);

            var panel = LGBuild.Panel(rt, "Panel", new Color(0.03f, 0.06f, 0.10f, 0.93f), raycast: true);
            _panel = panel.rectTransform;
            _panel.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MaxW, MaxH));
            LG.Glass(panel.gameObject, 24f).SetRim(new Color(0.45f, 0.95f, 0.9f, 0.3f));
            MenuPanelFx.Sheen(_panel);
            MenuSlideIn.Add(panel.gameObject, 0.05f, new Vector2(0f, -30f));

            BuildWelcome();
            BuildOptions();
        }

        // ------------------------------------------------------------------ Слева: приветствие и компьютер

        private void BuildWelcome()
        {
            var col = LGBuild.Panel(_panel, "Welcome", new Color(0.30f, 0.90f, 0.85f, 0.06f));
            var crt = col.rectTransform;
            crt.anchorMin = new Vector2(0, 0);
            crt.anchorMax = new Vector2(0, 1);
            crt.pivot = new Vector2(0, 0.5f);
            crt.offsetMin = new Vector2(2, 2);
            crt.offsetMax = new Vector2(LeftW, -2);
            LG.Ignore(col.gameObject);
            var line = LGBuild.Panel(_panel, "Split", new Color(1f, 1f, 1f, 0.06f));
            line.rectTransform.anchorMin = new Vector2(0, 0);
            line.rectTransform.anchorMax = new Vector2(0, 1);
            line.rectTransform.offsetMin = new Vector2(LeftW, 24);
            line.rectTransform.offsetMax = new Vector2(LeftW + 1, -24);
            LG.Ignore(line.gameObject);

            const float pad = 44f;
            var brand = LGBuild.Label(crt, "ASTROPOLITY", 15, UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true).Track(4f);
            brand.rectTransform.TopBand(50, 20, pad, pad);
            var cap = LGBuild.Label(crt, Loc.T("ДОБРО ПОЖАЛОВАТЬ"), 13, UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true).Track(3f);
            cap.rectTransform.TopBand(118, 18, pad, pad);
            var title = LGBuild.Label(crt, Loc.T("Настроим игру под вас"), 36, Color.white, TextAnchor.UpperLeft, bold: true, wrap: true);
            title.rectTransform.TopBand(144, 92, pad, pad);
            var text = LGBuild.Label(crt, Loc.T("Мы уже подобрали параметры по вашему компьютеру. Проверьте их и нажмите «Начать» — всё это можно поменять позже в «Настройках»."),
                                     16, new Color(0.60f, 0.70f, 0.74f), TextAnchor.UpperLeft, wrap: true);
            text.rectTransform.TopBand(244, 100, pad, pad);
            text.lineSpacing = 1.15f;

            // Ваш компьютер
            var box = LGBuild.Panel(crt, "PC", new Color(0f, 0f, 0f, 0.25f));
            box.rectTransform.TopBand(370, 212, pad, pad);
            LG.Platter(box.gameObject, 14f).SetRim(new Color(1f, 1f, 1f, 0.08f));
            var bh = LGBuild.Label(box.transform, Loc.T("ВАШ КОМПЬЮТЕР"), 11, new Color(0.55f, 0.65f, 0.68f), TextAnchor.UpperLeft, bold: true).Track(2f);
            bh.rectTransform.TopBand(18, 16, 20, 20);
            string[] keys = { Loc.T("Видеокарта"), Loc.T("Процессор"), Loc.T("Память"), Loc.T("Экран") };
            string[] vals = { Gpu(), Cpu(), Loc.F("{0} ГБ", Mathf.Max(1, Mathf.RoundToInt(SystemInfo.systemMemorySize / 1024f))), Display() };
            float y = 46f;
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i], v = vals[i];
                var kl = LGBuild.Label(box.transform, k, 14, new Color(0.55f, 0.65f, 0.68f), TextAnchor.MiddleLeft);
                kl.rectTransform.TopBand(y, 22, 20, 20);
                var vl = LGBuild.Label(box.transform, v, 14, Color.white, TextAnchor.MiddleRight, bold: true);
                vl.rectTransform.TopBand(y, 22, 130, 20);
                vl.horizontalOverflow = HorizontalWrapMode.Wrap;
                vl.resizeTextForBestFit = true;
                vl.resizeTextMinSize = 10;
                vl.resizeTextMaxSize = 14;
                y += 28f;
            }
            var rec = LGBuild.Label(box.transform, "● " + Loc.F("Рекомендуем качество «{0}»", GraphicsQuality.PresetNames[_recommended]), 13,
                                    new Color(0.36f, 0.96f, 0.60f), TextAnchor.MiddleLeft);
            rec.rectTransform.TopBand(y + 8f, 20, 20, 20);

            _start = MenuPrimaryButton.Create(crt, Loc.T("НАЧАТЬ"), 18, Finish);
            ((RectTransform)_start.transform).At(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 88), new Vector2(LeftW - pad * 2, 58));
            var hint = LGBuild.Label(crt, Loc.T("Enter — начать · Esc — оставить как есть"), 13, new Color(0.50f, 0.60f, 0.64f), TextAnchor.MiddleCenter);
            hint.rectTransform.At(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(LeftW - pad * 2, 20));
        }

        private static string Gpu()
        {
            string name = Clean(SystemInfo.graphicsDeviceName);
            int vram = SystemInfo.graphicsMemorySize;
            return vram >= 1000 ? name + " · " + Loc.F("{0} ГБ", Mathf.RoundToInt(vram / 1024f)) : name;
        }

        private static string Cpu()
        {
            string name = Clean(SystemInfo.processorType);
            int at = name.IndexOf(" @", System.StringComparison.Ordinal);
            if (at > 0) name = name.Substring(0, at);
            foreach (var junk in new[] { " Processor", " CPU", " with Radeon Graphics" })
                name = name.Replace(junk, "");
            int core = name.IndexOf("-Core", System.StringComparison.Ordinal);
            if (core > 0) { int sp = name.LastIndexOf(' ', core); if (sp > 0) name = name.Substring(0, sp); }
            return name.Trim() + " · " + Loc.F("{0} потоков", SystemInfo.processorCount);
        }

        private static string Display()
        {
            var r = Screen.currentResolution;
            int hz = Mathf.RoundToInt((float)r.refreshRateRatio.value);
            return hz > 0 ? Loc.F("{0} × {1} · {2} Гц", r.width, r.height, hz) : $"{r.width} × {r.height}";
        }

        private static string Clean(string s) =>
            string.IsNullOrEmpty(s) ? "—" : s.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "").Replace("  ", " ").Trim();

        // ------------------------------------------------------------------ Справа: параметры

        private void BuildOptions()
        {
            var col = LGBuild.Rect(_panel, "Options");
            col.Stretch(LeftW + 44f, 30f, 44f, 40f);
            float y = 0f;

            // Язык: подпись на обоих языках, чтобы её нашёл любой игрок
            y = LGControls.Section(col, y, "ЯЗЫК · LANGUAGE");
            int lang = Mathf.Max(0, System.Array.IndexOf(Loc.Codes, Loc.Current));
            var langSeg = MenuSegment.Create(col, Loc.Names, null, lang, i =>
            {
                if (Loc.Codes[i] == Loc.Current) return;
                // Окна собираются на языке запуска — меню перестраивается заново, окно откроется уже на новом языке
                Loc.Switch(Loc.Codes[i]);
                IsOpen = false;
                GameSession.ReturnToMainMenu();
            });
            langSeg.GetComponent<RectTransform>().TopBand(y, 52);
            y += 52f + 22f;

            y = LGControls.Section(col, y, Loc.T("ГРАФИКА"));
            var host = LGControls.Row(col, ref y, Loc.T("Качество"), Loc.T("Тени, сглаживание и масштаб рендера"), ControlW);
            _quality = Seg(host, GraphicsQuality.PresetNames, 4, Mathf.Min(GraphicsQuality.CurrentPreset, 3),
                           i => GameSettings.GraphicsChanged(() => GraphicsQuality.SetPreset(i)));
            host = LGControls.Row(col, ref y, Loc.T("Режим экрана"), Loc.T("Без рамки удобнее переключаться между окнами"), ControlW);
            Seg(host, GameSettings.ScreenModeNames, 3, GameSettings.ScreenMode, GameSettings.SetScreenMode);
            y += 14f;

            y = LGControls.Section(col, y, Loc.T("ИНТЕРФЕЙС И ЗВУК"));
            var scales = new List<string>();
            foreach (float s in GameSettings.UIScaleOptions) scales.Add(Mathf.RoundToInt(s * 100f) + "%");
            host = LGControls.Row(col, ref y, Loc.T("Масштаб интерфейса"), Loc.T("Крупнее — удобнее на ноутбуке, 4K и телевизоре"), ControlW);
            Seg(host, scales, scales.Count, GameSettings.UIScaleIndex, GameSettings.SetUIScale);
            host = LGControls.Row(col, ref y, Loc.T("Громкость"), Loc.T("Общая — музыку и звуки отдельно можно настроить позже"), ControlW);
            LGControls.Slider(host, GameSettings.MasterVolume, 0f, 1f, v => Mathf.RoundToInt(v * 100f) + "%", GameSettings.SetMaster);
            y += 14f;

            y = LGControls.Section(col, y, Loc.T("ВЫ ИГРАЛИ В КОСМИЧЕСКИЕ СТРАТЕГИИ?"));
            host = LGControls.Row(col, ref y, Loc.T("Опыт"), Loc.T("Обучение, советник и сложность первой партии"), ControlW);
            Seg(host, new[] { Loc.T("Впервые"), Loc.T("Немного"), Loc.T("Ветеран") }, 3, _exp, i => { _exp = i; GameSettings.SetExperience(i); SyncExperience(); });
            _expDesc = LGBuild.Label(col, "", 13, new Color(0.56f, 0.66f, 0.71f), TextAnchor.UpperLeft, wrap: true);
            _expDesc.rectTransform.TopBand(y + 2f, 40, 18, 0);
            SyncExperience();
        }

        private static MenuSegment Seg(RectTransform host, IList<string> opts, int count, int value, System.Action<int> onChange)
        {
            var list = new List<string>();
            for (int i = 0; i < count && i < opts.Count; i++) list.Add(opts[i]);
            var s = MenuSegment.Create(host, list, null, Mathf.Clamp(value, 0, list.Count - 1), onChange);
            s.GetComponent<RectTransform>().Stretch(0, -3, 0, -3);
            return s;
        }

        private void SyncExperience()
        {
            if (_expDesc == null) return;
            _expDesc.text = _exp == 0 ? Loc.T("Обучение в первой партии, советы ОРАКУЛА-7, сложность «Новичок».")
                          : _exp == 1 ? Loc.T("Обучение можно пропустить в любой момент, советы ОРАКУЛА-7, сложность «Лёгкая».")
                          : Loc.T("Без обучения и советника, сложность «Нормальная». Всё меняется в «Новой игре» и «Настройках».");
        }

        // ------------------------------------------------------------------ Закрытие

        private void Finish()
        {
            if (_closing) return;
            _closing = true;
            IsOpen = false;
            GameSettings.SetExperience(_exp);
            GameSettings.MarkFirstRunDone();
            _onDone?.Invoke();
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _age += dt;
            _k = LGEase.Spring(_k, _closing ? 0f : 1f, ref _kv, 4.5f, 0.9f, dt);
            _cg.alpha = Mathf.Clamp01(_k);
            _cg.blocksRaycasts = _cg.interactable = !_closing;
            if (_closing && _k < 0.01f) { Destroy(gameObject); return; }

            // Окно влезает в экран при любом масштабе интерфейса
            var r = _root.rect;
            var size = new Vector2(Mathf.Min(MaxW, r.width - 40f), Mathf.Min(MaxH, r.height - 30f));
            if ((_panel.sizeDelta - size).sqrMagnitude > 0.25f) _panel.sizeDelta = size;

            // Пресет могли переставить в «Настройках» — держим выбор в окне честным
            if (_quality != null && GraphicsQuality.CurrentPreset < 4 && _quality.Value != GraphicsQuality.CurrentPreset)
                _quality.Set(GraphicsQuality.CurrentPreset, false);

            // Клавиша, которой пропустили заставку или экран загрузки, не должна сразу закрыть окно
            if (LoadingScreen.IsActive || IntroVideo.IsPlaying) { _age = 0f; return; }
            if (_closing || _age < 0.6f) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) _start.Press();
            else if (Input.GetKeyDown(KeyCode.Escape)) Finish();
        }
    }
}
