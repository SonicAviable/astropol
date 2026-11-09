using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Экран загрузки: при запуске игры, при старте новой партии, загрузке сохранения и выходе в меню.
    /// Живёт между перезагрузками сцены (DontDestroyOnLoad) и показывает реальный прогресс по этапам:
    ///   сцена (асинхронная загрузка) → построение мира (бутстрап) → карта галактики → восстановление
    ///   сохранения → синтез звуков → несколько кадров прогрева (компиляция шейдеров, первый рендер).
    /// Оформление «Орбита»: арт с виньеткой и всплывающей пылью, название слева сверху, совет слева снизу,
    /// кольцо прогресса справа снизу (свечение, спутник, вспышка на 100%), этап и шаги рядом; всё появляется каскадом.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        public enum Kind { Launch, NewGame, LoadGame, Menu }

        private static LoadingScreen s_inst;
        public static bool IsActive => s_inst != null && s_inst._active;

        private CanvasGroup _group;
        private RectTransform _artRt;
        private RawImage _art;
        private AspectRatioFitter _artFit;
        private static readonly System.Collections.Generic.List<Texture2D> s_arts = new System.Collections.Generic.List<Texture2D>();
        private static int s_lastArt = -1;
        private Text _title, _subtitle, _stage, _percent, _tip, _detail;
        private RectTransform _titleRt, _accent, _ringRt, _ringDash, _ringSat, _ringHead, _infoRt, _stageRt, _tipRt, _tipLine, _tipTextRt, _dustRoot;
        private Image _ringFill, _ringFillGlow, _ringGlow, _ringHeadImg;
        private CanvasGroup _tipGroup, _titleGroup, _subtitleGroup, _ringGroup, _infoGroup, _tipBlockGroup;

        private const int MaxSteps = 8, TipDots = 5, DustCount = 36;
        private const float TipPeriod = 6f;
        private readonly Image[] _stepBg = new Image[MaxSteps], _stepFill = new Image[MaxSteps];
        private readonly float[] _stepDoneT = new float[MaxSteps];
        private readonly Image[] _tipDots = new Image[TipDots];
        private float _introStart, _stageChangeT, _doneT = -1f;
        private int _stageIndex = -1, _tipCount;

        private struct Mote { public RectTransform rt; public Image img; public float x, y, speed, sway, phase, alpha; }
        private readonly Mote[] _dust = new Mote[DustCount];

        private bool _active, _hiding;
        private Kind _kind;
        private float _alpha, _shown, _startTime, _tipTimer;
        private int _tipIndex = -1;
        private Action _onCovered;
        private int _coveredFrames;
        private AsyncOperation _op;
        private bool _booted;
        private int _framesSinceBoot;

        // Темп полосы: 8–10 секунд (выход в меню — ~3 с), неравномерно — с рывками и паузами,
        // как при настоящей загрузке. Реальный прогресс полоса никогда не обгоняет.
        private float _duration;
        private readonly System.Collections.Generic.List<Vector2> _curve = new System.Collections.Generic.List<Vector2>();
        private string[] _stages;
        private StellarisClone.Generation.GalaxyGenerator _gen;

        private static readonly Color Cyan = new Color(0.36f, 0.95f, 0.92f);

        private static readonly string[] Tips =
        {
            Loc.T("Звёздную базу нужно вывести из строя в бою — только после этого начнётся осада системы."),
            Loc.T("Адмирал на флагмане усиливает все ваши боевые корабли в той же системе."),
            Loc.T("Учёный в научном совете ускоряет все исследования империи."),
            Loc.T("В режиме спирали рукава соединены узкими проходами — их выгодно оборонять."),
            Loc.T("Корабль с нулевым корпусом может выйти из боя и уйти на ремонт — у корветов шанс выше."),
            Loc.T("Губернатор ускоряет стройку на своей планете: районы и колонии появятся раньше."),
            Loc.T("Неизведанные системы на карте тусклые: пошлите научный корабль, чтобы открыть их ресурсы."),
            Loc.T("Shift + ПКМ добавляет точку в маршрут флота, Backspace убирает последнюю."),
            Loc.T("Содержание флота растёт сверх лимита: следите за прогнозом энергии в верхней панели."),
            Loc.T("Пробел — пауза, клавиши 1, 2, 3 — скорость времени."),
            Loc.T("Перемирие после войны длится 3 года — используйте его, чтобы отстроить флот."),
            Loc.T("Аномалии, найденные при разведке, могут дать науку, ресурсы или неприятности."),
        };

        // ==================== ПУБЛИЧНОЕ API ====================

        /// <summary>Мгновенно показать (запуск игры: первый кадр уже идёт).</summary>
        public static void ShowImmediate(Kind kind)
        {
            var s = Ensure();
            s.Reset(kind);
            s._alpha = 1f;
            s.Apply();
        }

        /// <summary>Плавно закрыть экран загрузкой и после этого выполнить переход (перезагрузку сцены).</summary>
        public static void Begin(Kind kind, Action onCovered)
        {
            var s = Ensure();
            s.Reset(kind);
            s._onCovered = onCovered;
        }

        /// <summary>Асинхронная загрузка сцены — даёт реальный прогресс первого этапа.</summary>
        public static void TrackSceneLoad(AsyncOperation op)
        {
            if (s_inst != null) s_inst._op = op;
        }

        /// <summary>Бутстрап закончил создавать мир (вызывается в конце GameBootstrap.Boot).</summary>
        public static void NotifyBooted()
        {
            if (s_inst == null) return;
            s_inst._booted = true;
            s_inst._framesSinceBoot = 0;
        }

        // ==================== ПОСТРОЕНИЕ ====================

        private static LoadingScreen Ensure()
        {
            if (s_inst != null) return s_inst;
            var go = new GameObject("[LoadingScreen]");
            DontDestroyOnLoad(go);
            s_inst = go.AddComponent<LoadingScreen>();
            s_inst.Build();
            return s_inst;
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 7000;                      // выше всего, включая шторку SceneFader
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            LG.Ignore(gameObject);                           // без авто-стекла: экран рисуется сам

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            var root = (RectTransform)transform;

            // Фон: чёрная подложка + арт «на весь экран с обрезкой» (cover)
            var black = NewImage(root, "Black", new Color(0.01f, 0.015f, 0.03f, 1f));
            Stretch(black.rectTransform);
            black.raycastTarget = true;   // ловит все клики и наведение, пока идёт загрузка — сквозь экран ничего не нажать

            var tex = PickArt();
            var art = new GameObject("Art").AddComponent<RawImage>();
            _art = art;
            art.transform.SetParent(root, false);
            art.texture = tex;
            art.raycastTarget = false;
            _artRt = art.rectTransform;
            _artRt.anchorMin = _artRt.anchorMax = new Vector2(0.5f, 0.5f);
            var fitter = art.gameObject.AddComponent<AspectRatioFitter>();
            _artFit = fitter;
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = tex != null ? tex.width / (float)tex.height : 16f / 9f;
            if (tex == null) art.color = new Color(0.02f, 0.05f, 0.09f);

            // Виньетка: края, низ и левый верх (под названием) — арт в центре остаётся открытым
            var shade = NewImage(root, "Shade", Color.white);
            Stretch(shade.rectTransform);
            shade.sprite = VignetteSprite();

            var dot = SoftDotSprite();
            var circle = CircleSprite();

            // Пылинки, медленно всплывающие в тёплом воздухе
            _dustRoot = NewRect(root, "Dust");
            Stretch(_dustRoot);
            for (int i = 0; i < DustCount; i++)
            {
                var m = NewImage(_dustRoot, "Mote", Color.clear);
                m.sprite = dot;
                m.rectTransform.anchorMin = m.rectTransform.anchorMax = Vector2.zero;
                _dust[i].rt = m.rectTransform;
                _dust[i].img = m;
                SpawnMote(ref _dust[i], true, 1920f, 1080f);
            }

            // ---- Название (левый верх) ----
            _title = NewText(root, "A S T R O P O L I T Y", 42, Color.white, TextAnchor.UpperLeft, false);
            _title.font = GameFont.Display;
            _title.horizontalOverflow = HorizontalWrapMode.Overflow;
            _titleRt = Place(_title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -72), new Vector2(1000, 60));
            var glow = _title.gameObject.AddComponent<Shadow>();
            glow.effectColor = new Color(0.2f, 0.9f, 0.95f, 0.3f);
            glow.effectDistance = new Vector2(0, -2);
            _titleGroup = _title.gameObject.AddComponent<CanvasGroup>();

            _accent = Place(NewImage(root, "Accent", Cyan).rectTransform, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(100, -130), new Vector2(0, 2));

            _subtitle = NewText(root, "", 15, new Color(0.66f, 0.87f, 0.88f, 0.95f), TextAnchor.UpperLeft, false);
            Place(_subtitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(100, -142), new Vector2(1300, 24));
            _subtitleGroup = _subtitle.gameObject.AddComponent<CanvasGroup>();

            // ---- Кольцо прогресса (правый низ) ----
            _ringRt = NewRect(root, "Ring");
            Place(_ringRt, new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-210, 200), new Vector2(200, 200));
            _ringGroup = _ringRt.gameObject.AddComponent<CanvasGroup>();

            _ringGlow = NewImage(_ringRt, "Glow", Color.clear);
            _ringGlow.sprite = dot;
            Center(_ringGlow.rectTransform, 380);

            var disk = NewImage(_ringRt, "Disk", new Color(0.01f, 0.03f, 0.055f, 0.62f));
            disk.sprite = circle;
            Center(disk.rectTransform, 186);

            var orbit = NewImage(_ringRt, "Orbit", new Color(1f, 1f, 1f, 0.08f));
            orbit.sprite = RingSprite(0.98f, 0.006f);
            Center(orbit.rectTransform, 244);
            _ringSat = Center(NewRect(_ringRt, "Satellite"), 244);
            var sat = NewImage(_ringSat, "Dot", new Color(0.85f, 1f, 1f, 0.95f));
            sat.sprite = dot;
            Place(sat.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(14, 14));

            var ringSpr = RingSprite(0.92f, 0.022f);
            var track = NewImage(_ringRt, "Track", new Color(1f, 1f, 1f, 0.10f));
            track.sprite = ringSpr;
            Center(track.rectTransform, 200);

            _ringFillGlow = NewImage(_ringRt, "FillGlow", Cyan);
            _ringFillGlow.sprite = GlowRingSprite(0.92f);
            Radial(_ringFillGlow);
            Center(_ringFillGlow.rectTransform, 200);

            _ringFill = NewImage(_ringRt, "Fill", Cyan);
            _ringFill.sprite = ringSpr;
            Radial(_ringFill);
            Center(_ringFill.rectTransform, 200);

            var dash = NewImage(_ringRt, "Dash", new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f));
            dash.sprite = DashedRingSprite();
            _ringDash = Center(dash.rectTransform, 160);

            _ringHeadImg = NewImage(_ringRt, "Head", Color.clear);
            _ringHeadImg.sprite = dot;
            _ringHead = Center(_ringHeadImg.rectTransform, 32);

            _percent = NewText(_ringRt, "", 54, Color.white, TextAnchor.MiddleCenter, true);
            Place(_percent.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(180, 66));
            var lbl = NewText(_ringRt, Loc.T("ПРОЦЕНТОВ"), 11, new Color(0.56f, 0.73f, 0.75f), TextAnchor.MiddleCenter, false);
            Place(lbl.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(180, 18));

            // ---- Этап, счётчики и шаги (слева от кольца) ----
            _infoRt = NewRect(root, "Info");
            Place(_infoRt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-340, 120), new Vector2(1000, 90));
            _infoGroup = _infoRt.gameObject.AddComponent<CanvasGroup>();

            _stage = NewText(_infoRt, "", 22, Color.white, TextAnchor.LowerRight, true);
            _stageRt = Place(_stage.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0, 62), new Vector2(1000, 30));

            _detail = NewText(_infoRt, "", 13, new Color(0.54f, 0.64f, 0.67f, 0.9f), TextAnchor.UpperRight, false);
            Place(_detail.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 52), new Vector2(1000, 20));

            for (int i = 0; i < MaxSteps; i++)
            {
                var bg = NewImage(_infoRt, "Step", new Color(1f, 1f, 1f, 0.16f));
                Place(bg.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-(MaxSteps - 1 - i) * 34f, 8), new Vector2(26, 3));
                var f = NewImage(bg.rectTransform, "Fill", Cyan);
                var frt = f.rectTransform;
                frt.anchorMin = new Vector2(0, 0);
                frt.anchorMax = new Vector2(0, 1);
                frt.pivot = new Vector2(0, 0.5f);
                frt.offsetMin = frt.offsetMax = Vector2.zero;
                _stepBg[i] = bg;
                _stepFill[i] = f;
            }

            // ---- Совет (левый низ) ----
            _tipRt = NewRect(root, "TipBlock");
            Place(_tipRt, new Vector2(0, 0), new Vector2(0, 0), new Vector2(96, 96), new Vector2(700, 120));
            _tipBlockGroup = _tipRt.gameObject.AddComponent<CanvasGroup>();

            _tipLine = Place(NewImage(_tipRt, "Line", Cyan).rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(2, 0));

            var tipLbl = NewText(_tipRt, Loc.T("СОВЕТ"), 12, Cyan, TextAnchor.UpperLeft, true);
            Place(tipLbl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, 0), new Vector2(300, 18));

            _tip = NewText(_tipRt, "", 19, new Color(0.89f, 0.93f, 0.94f, 1f), TextAnchor.UpperLeft, false);
            _tipTextRt = Place(_tip.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -24), new Vector2(660, 70));
            _tipGroup = _tip.gameObject.AddComponent<CanvasGroup>();

            for (int i = 0; i < TipDots; i++)
            {
                var d = NewImage(_tipRt, "Dot", Color.white);
                d.sprite = circle;
                Place(d.rectTransform, new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(25 + i * 13f, 4), new Vector2(6, 6));
                _tipDots[i] = d;
            }
        }

        // ==================== ЛОГИКА ====================

        private void Reset(Kind kind)
        {
            _kind = kind;
            _active = true;
            _hiding = false;
            _shown = 0f;
            _startTime = Time.unscaledTime;
            _op = null;
            _booted = false;
            _framesSinceBoot = 0;
            _onCovered = null;
            _coveredFrames = 0;
            _tipTimer = 0f;
            _group.blocksRaycasts = true;
            // Выделенная кнопка нажимается и с клавиатуры (Enter/пробел) — снимаем выделение
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null) es.SetSelectedGameObject(null);
            _artRt.localScale = Vector3.one;
            var tex = PickArt();
            if (tex != null && _art != null)
            {
                _art.texture = tex;
                _art.color = Color.white;
                _artFit.aspectRatio = tex.width / (float)tex.height;
            }
            _subtitle.text = Subtitle(kind);
            _detail.text = "";
            _gen = null;
            _duration = kind == Kind.Menu ? UnityEngine.Random.Range(2.8f, 3.4f) : UnityEngine.Random.Range(8f, 10f);
            _stages = StagesFor(kind);
            BuildCurve();
            _introStart = _stageChangeT = Time.unscaledTime;
            _stageIndex = -1;
            _doneT = -1f;
            _tipCount = 0;
            int n = Mathf.Min(_stages.Length, MaxSteps);
            for (int i = 0; i < MaxSteps; i++)
            {
                _stepDoneT[i] = -1f;
                _stepBg[i].gameObject.SetActive(i >= MaxSteps - n);
                _stepFill[i].rectTransform.sizeDelta = Vector2.zero;
            }
            NextTip();
            Apply();
        }

        /// <summary>Подписи этапов: сменяются по ходу полосы.</summary>
        /// <summary>
        /// Арт экрана загрузки: случайный из UI/LoadingArt и UI/LoadingArt_2…_9, не повторяя прошлый.
        /// </summary>
        private static Texture2D PickArt()
        {
            if (s_arts.Count == 0)
            {
                var first = Resources.Load<Texture2D>("UI/LoadingArt");
                if (first != null) s_arts.Add(first);
                for (int i = 2; i <= 9; i++)
                {
                    var t = Resources.Load<Texture2D>("UI/LoadingArt_" + i);
                    if (t != null) s_arts.Add(t);
                }
            }
            if (s_arts.Count == 0) return null;
            int idx = UnityEngine.Random.Range(0, s_arts.Count);
            if (s_arts.Count > 1 && idx == s_lastArt) idx = (idx + 1) % s_arts.Count;
            s_lastArt = idx;
            return s_arts[idx];
        }

        private static string[] StagesFor(Kind kind) => kind switch
        {
            Kind.NewGame => new[]
            {
                Loc.T("Инициализация ядра симуляции"), Loc.T("Генерация звёздных систем"), Loc.T("Прокладка гиперкоридоров"),
                Loc.T("Расчёт орбит и планет"), Loc.T("Заселение империй"), Loc.T("Пробуждение империи-соперника"),
                Loc.T("Синтез звукового ландшафта"), Loc.T("Подготовка командного интерфейса")
            },
            Kind.LoadGame => new[]
            {
                Loc.T("Чтение архива сохранения"), Loc.T("Восстановление галактики"), Loc.T("Возвращение флотов на орбиты"),
                Loc.T("Восстановление экономики"), Loc.T("Синхронизация дипломатии"), Loc.T("Пробуждение лидеров"),
                Loc.T("Синтез звукового ландшафта"), Loc.T("Подготовка командного интерфейса")
            },
            Kind.Menu => new[] { Loc.T("Сохранение состояния вселенной"), Loc.T("Возвращение в главное меню") },
            _ => new[]
            {
                Loc.T("Инициализация ядра"), Loc.T("Компиляция шейдеров стекла"), Loc.T("Подготовка звёздного неба"),
                Loc.T("Генерация галактики"), Loc.T("Синтез звукового ландшафта"), Loc.T("Настройка музыки"),
                Loc.T("Сборка интерфейса"), Loc.T("Последние штрихи")
            }
        };

        /// <summary>
        /// Кривая «время → прогресс» из нескольких отрезков случайной длины и крутизны:
        /// где-то полоса бежит, где-то «задумывается» — выглядит как реальная загрузка ресурсов.
        /// </summary>
        private void BuildCurve()
        {
            _curve.Clear();
            int n = _kind == Kind.Menu ? 3 : 9;
            var dt = new float[n];
            var dp = new float[n];
            float sumT = 0f, sumP = 0f;
            for (int i = 0; i < n; i++)
            {
                dt[i] = UnityEngine.Random.Range(0.5f, 1.5f);
                dp[i] = UnityEngine.Random.Range(0.2f, 1.4f);
                sumT += dt[i]; sumP += dp[i];
            }
            float t = 0f, p = 0f;
            _curve.Add(Vector2.zero);
            for (int i = 0; i < n; i++)
            {
                t += dt[i] / sumT;
                p += dp[i] / sumP;
                _curve.Add(new Vector2(t, p));
            }
        }

        private float CurveAt(float x)
        {
            x = Mathf.Clamp01(x);
            for (int i = 1; i < _curve.Count; i++)
            {
                if (x > _curve[i].x) continue;
                var a = _curve[i - 1]; var b = _curve[i];
                float k = Mathf.InverseLerp(a.x, b.x, x);
                return Mathf.Lerp(a.y, b.y, k * k * (3f - 2f * k));   // внутри отрезка — плавный разгон и торможение
            }
            return 1f;
        }

        private static string Subtitle(Kind kind)
        {
            var s = GameSession.Settings;
            switch (kind)
            {
                case Kind.NewGame:
                    return Loc.F("Новая партия   ·   {0} галактика   ·   ", NewGameSettings.SizeNames[Mathf.Clamp(s.GalaxySize, 0, 2)].ToLower()) +
                           $"{NewGameSettings.ShapeNames[Mathf.Clamp(s.Shape, 0, NewGameSettings.ShapeNames.Length - 1)].ToLower()}   ·   " +
                           Loc.F("сложность: {0}", NewGameSettings.LevelName(s.Tier).ToLower());
                case Kind.LoadGame:
                {
                    var m = GameSession.PendingLoad?.Meta;
                    return m != null ? Loc.F("Загрузка сохранения   ·   {0}   ·   {1}   ·   {2}", m.Name, m.Empire, m.GameDate) : Loc.T("Загрузка сохранения");
                }
                case Kind.Menu: return Loc.T("Возвращение в главное меню");
                default: return Loc.T("Галактическая стратегия");
            }
        }

        private void NextTip()
        {
            int i;
            do { i = UnityEngine.Random.Range(0, Tips.Length); } while (i == _tipIndex && Tips.Length > 1);
            _tipIndex = i;
            _tip.text = Tips[i];
            _tipCount++;
        }


        private void Update()
        {
            if (!_active) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);

            // Появление; после полного закрытия экрана ждём 2 кадра (он точно отрисован) и запускаем переход
            if (_onCovered != null)
            {
                _alpha = Mathf.MoveTowards(_alpha, 1f, dt / 0.35f);
                if (_alpha >= 1f && ++_coveredFrames >= 2)
                {
                    var cb = _onCovered;
                    _onCovered = null;
                    _startTime = Time.unscaledTime;
                    cb();
                }
                Apply();
                Animate(dt);
                return;
            }

            if (_booted) _framesSinceBoot++;

            // Этапы и их вес
            bool loadingSave = _kind == Kind.LoadGame;
            float scene = _op != null ? Mathf.Clamp01(_op.progress / 0.9f) : (_booted ? 1f : 0f);
            if (_booted) scene = 1f;
            float boot = _booted ? 1f : 0f;
            float galaxy = _booted && GalaxyView.IsBuilt ? 1f : 0f;
            float save = !loadingSave ? 1f : (_booted && !GameSession.IsLoading ? 1f : 0f);
            float sfx = SFXManager.PrewarmProgress;
            float warm = _booted ? Mathf.Clamp01(_framesSinceBoot / 12f) : 0f;
            float target = scene * 0.2f + boot * 0.15f + galaxy * 0.15f + save * 0.12f + sfx * 0.3f + warm * 0.08f;

            // Полоса идёт по «живой» кривой времени, но не обгоняет реальную загрузку
            float elapsed = Time.unscaledTime - _startTime;
            float goal = Mathf.Min(target, CurveAt(elapsed / _duration));
            _shown = Mathf.Max(_shown, Mathf.MoveTowards(_shown, goal, dt * 0.9f));

            int si = Mathf.Clamp(Mathf.FloorToInt(_shown * _stages.Length), 0, _stages.Length - 1);
            if (_shown >= 0.999f && _doneT < 0f) { _doneT = Time.unscaledTime; _stageChangeT = _doneT; }
            if (si != _stageIndex) { _stageIndex = si; _stageChangeT = Time.unscaledTime; }
            _stage.text = _shown >= 0.999f ? Loc.T("Готово") : _stages[si] + Dots(elapsed);
            _percent.text = $"{_shown * 100f:0}";
            _detail.text = Detail(sfx);

            bool done = target >= 0.999f && _shown >= 0.995f && elapsed >= _duration;
            if (done) _hiding = true;
            if (_hiding)
            {
                _alpha = Mathf.MoveTowards(_alpha, 0f, dt / 0.7f);
                if (_alpha <= 0f)
                {
                    _active = false;
                    _group.blocksRaycasts = false;
                }
            }
            else _alpha = Mathf.MoveTowards(_alpha, 1f, dt / 0.35f);

            // Советы меняются каждые 6 секунд с мягким переходом
            _tipTimer += dt;
            if (_tipTimer > TipPeriod) { _tipTimer = 0f; NextTip(); }

            Apply();
            Animate(dt);
        }

        private static string Dots(float t) => new string('.', 1 + (int)(t * 2.5f) % 3);

        /// <summary>Строка с живыми цифрами: что уже построено и сколько звуков синтезировано.</summary>
        private string Detail(float sfx)
        {
            var sb = new System.Text.StringBuilder();
            if (_booted && GalaxyView.IsBuilt)
            {
                if (_gen == null) _gen = FindAnyObjectByType<StellarisClone.Generation.GalaxyGenerator>();
                if (_gen != null)
                {
                    // Счётчики «докручиваются» вместе с полосой
                    float k = Mathf.Clamp01(_shown * 1.6f);
                    sb.Append(Loc.F("звёздных систем {0}   ·   гиперкоридоров {1}   ·   ", Mathf.RoundToInt(_gen.Systems.Count * k), Mathf.RoundToInt(_gen.Hyperlanes.Count * k)));
                }
            }
            sb.Append(Loc.F("звуков синтезировано {0:0}%", sfx * 100f));
            if (_kind != Kind.Menu) sb.Append(Loc.F("   ·   шейдеров {0}/48", Mathf.RoundToInt(Mathf.Clamp01(_shown * 1.3f) * 48)));
            return sb.ToString();
        }

        private void Animate(float dt)
        {
            float now = Time.unscaledTime;
            float t = now - _startTime;
            float it = now - _introStart;                    // время с момента показа — для каскада появления

            // «Наезд камеры»: 1.00 → 1.08 за 25 секунд и лёгкий дрейф; арт проявляется из темноты
            float k = Mathf.Clamp01(t / 25f);
            _artRt.localScale = Vector3.one * Mathf.Lerp(1.0f, 1.08f, Mathf.SmoothStep(0f, 1f, k));
            _artRt.anchoredPosition = new Vector2(Mathf.Sin(t * 0.05f) * 18f, Mathf.Cos(t * 0.04f) * 10f);
            if (_art.texture != null)
            {
                float b = Mathf.Lerp(0.12f, 1f, OutCubic(it / 1.4f));
                _art.color = new Color(b, b, b, 1f);
            }

            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3f);

            // Название выезжает слева, акцентная черта прорисовывается, подзаголовок всплывает
            float a = OutCubic(Seg(it, 0.15f, 0.95f));
            _titleGroup.alpha = a;
            _titleRt.anchoredPosition = new Vector2(96f - 40f * (1f - a), -72f);
            _accent.sizeDelta = new Vector2(72f * OutCubic(Seg(it, 0.6f, 1.4f)), 2f);
            float s = OutCubic(Seg(it, 0.45f, 1.15f));
            _subtitleGroup.alpha = s;
            _subtitle.rectTransform.anchoredPosition = new Vector2(100f, -142f - 10f * (1f - s));

            // Кольцо: «распружинивается» при появлении, вспыхивает на 100%
            float r = Seg(it, 0.25f, 1.05f);
            _ringGroup.alpha = OutCubic(r);
            float burst = _doneT >= 0f ? Mathf.Sin(Mathf.Clamp01((now - _doneT) / 0.55f) * Mathf.PI) : 0f;
            _ringRt.localScale = Vector3.one * (Mathf.LerpUnclamped(0.75f, 1f, OutBack(r)) + burst * 0.07f);
            _ringFill.fillAmount = _shown;
            _ringFillGlow.fillAmount = _shown;
            _ringFillGlow.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f + 0.25f * pulse + burst * 0.4f);
            _ringGlow.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.07f + 0.05f * pulse + burst * 0.3f);
            float ang = (90f - _shown * 360f) * Mathf.Deg2Rad;
            _ringHead.anchoredPosition = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 92f;
            _ringHead.localScale = Vector3.one * (0.85f + 0.3f * pulse + burst * 0.6f);
            _ringHeadImg.color = new Color(0.8f, 1f, 1f, _shown > 0.004f ? 0.65f + 0.35f * pulse : 0f);
            _ringDash.localRotation = Quaternion.Euler(0f, 0f, -t * 12f);
            _ringSat.localRotation = Quaternion.Euler(0f, 0f, -t * 55f);

            // Блок этапа: выезжает справа; новый этап всплывает снизу
            float inf = OutCubic(Seg(it, 0.5f, 1.2f));
            _infoGroup.alpha = inf;
            _infoRt.anchoredPosition = new Vector2(-340f + 30f * (1f - inf), 120f);
            float sk = OutCubic((now - _stageChangeT) / 0.4f);
            _stageRt.anchoredPosition = new Vector2(0f, 62f - 14f * (1f - sk));
            _stage.color = new Color(1f, 1f, 1f, sk);

            // Шаги: заполняются по очереди, завершённый вспыхивает, текущий дышит
            int n = Mathf.Min(_stages != null ? _stages.Length : 0, MaxSteps);
            int off = MaxSteps - n;
            for (int i = 0; i < n; i++)
            {
                float f = Mathf.Clamp01(_shown * n - i);
                if (f >= 1f && _stepDoneT[i] < 0f) _stepDoneT[i] = now;
                var fr = _stepFill[off + i].rectTransform;
                fr.sizeDelta = new Vector2(26f * f, 0f);
                Color c;
                if (f >= 1f) c = Color.Lerp(Cyan, Color.white, Mathf.Clamp01(1f - (now - _stepDoneT[i]) / 0.6f));
                else c = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f + 0.4f * pulse);
                _stepFill[off + i].color = c;
                bool cur = f < 1f && f > 0f || (f <= 0f && i == Mathf.FloorToInt(_shown * n));
                _stepBg[off + i].color = new Color(1f, 1f, 1f, cur ? 0.16f + 0.14f * pulse : 0.16f);
            }

            // Совет: черта прорисовывается сверху вниз, текст въезжает и уходит, точки отмечают номер
            float tb = OutCubic(Seg(it, 0.7f, 1.35f));
            _tipBlockGroup.alpha = tb;
            _tipLine.sizeDelta = new Vector2(2f, 120f * OutCubic(Seg(it, 0.8f, 1.6f)));
            float tin = OutCubic(_tipTimer / 0.6f), tout = Mathf.Clamp01((TipPeriod - _tipTimer) / 0.5f);
            _tipGroup.alpha = Mathf.Min(Mathf.Clamp01(_tipTimer / 0.5f), tout);
            _tipTextRt.anchoredPosition = new Vector2(22f + 18f * (1f - tin) - 12f * (1f - tout), -24f);
            int active = (_tipCount - 1 + TipDots) % TipDots;
            for (int i = 0; i < TipDots; i++)
            {
                bool on = i == active;
                var dc = _tipDots[i].color;
                var target = on ? Cyan : new Color(1f, 1f, 1f, 0.25f);
                _tipDots[i].color = Color.Lerp(dc, target, dt * 8f);
                float sc = Mathf.Lerp(_tipDots[i].rectTransform.localScale.x, on ? 1.35f : 1f, dt * 8f);
                _tipDots[i].rectTransform.localScale = Vector3.one * sc;
            }

            UpdateDust(dt, t);
        }

        private void SpawnMote(ref Mote m, bool anywhere, float w, float h)
        {
            m.x = UnityEngine.Random.Range(0f, w);
            m.y = anywhere ? UnityEngine.Random.Range(0f, h) : -10f;
            m.speed = UnityEngine.Random.Range(10f, 32f);
            m.sway = UnityEngine.Random.Range(8f, 26f);
            m.phase = UnityEngine.Random.Range(0f, 10f);
            m.alpha = UnityEngine.Random.Range(0.2f, 0.65f);
            float size = UnityEngine.Random.Range(3f, 9f);
            m.rt.sizeDelta = new Vector2(size, size);
        }

        private void UpdateDust(float dt, float t)
        {
            var rect = _dustRoot.rect;
            float w = rect.width, h = rect.height;
            if (w <= 0f || h <= 0f) return;
            for (int i = 0; i < DustCount; i++)
            {
                ref var m = ref _dust[i];
                m.y += m.speed * dt;
                if (m.y > h + 10f || m.x > w + 30f) SpawnMote(ref m, false, w, h);
                m.rt.anchoredPosition = new Vector2(m.x + Mathf.Sin(t * 0.4f + m.phase) * m.sway, m.y);
                float fade = Mathf.Clamp01(m.y / 120f) * Mathf.Clamp01((h - m.y) / (h * 0.5f));
                float tw = 0.65f + 0.35f * Mathf.Sin(t * 2.3f + m.phase * 3f);
                m.img.color = new Color(1f, 0.86f, 0.7f, m.alpha * fade * tw);
            }
        }

        private static float Seg(float t, float a, float b) => Mathf.Clamp01((t - a) / (b - a));
        private static float OutCubic(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
        private static float OutBack(float x)
        {
            x = Mathf.Clamp01(x) - 1f;
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        private void Apply()
        {
            if (_group != null) _group.alpha = _alpha;
        }

        // ==================== ГРАФИКА ====================

        private static Image NewImage(Transform parent, string name, Color c)
        {
            var img = new GameObject(name).AddComponent<Image>();
            img.transform.SetParent(parent, false);
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private static Text NewText(Transform parent, string s, int size, Color c, TextAnchor anchor, bool bold)
        {
            var t = new GameObject("Text").AddComponent<Text>();
            t.transform.SetParent(parent, false);
            t.font = bold ? GameFont.Bold : GameFont.Regular;
            t.fontSize = size;
            t.color = c;
            t.alignment = anchor;
            t.text = s;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.7f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static RectTransform NewRect(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static RectTransform Center(RectTransform rt, float size) =>
            Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));

        private static void Radial(Image img)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = true;
            img.fillAmount = 0f;
        }

        /// <summary>Классический smoothstep по краям e0→e1 (Mathf.SmoothStep — это интерполяция, не он).</summary>
        private static float Edge(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static Sprite MakeSprite(int w, int h, Func<float, float, float> alpha)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha(x, y))));
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Затемнение: виньетка, плотный низ и пятно под названием в левом верхнем углу.</summary>
        private static Sprite VignetteSprite()
        {
            const int W = 160, H = 90;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1), v = y / (float)(H - 1);
                float dx = (u - 0.5f) * 1.6f, dy = (v - 0.6f) * 1.4f;
                float vig = Edge(0.45f, 1.25f, Mathf.Sqrt(dx * dx + dy * dy)) * 0.75f;
                float bottom = Mathf.Pow(1f - Mathf.Clamp01(v / 0.4f), 1.8f) * 0.85f;
                float cx = u / 0.35f, cy = (1f - v) / 0.25f;
                float corner = Mathf.Exp(-(cx * cx + cy * cy) * 1.2f) * 0.5f;
                float a = 1f - (1f - vig) * (1f - bottom) * (1f - corner);
                tex.SetPixel(x, y, new Color(0.004f, 0.012f, 0.03f, Mathf.Clamp01(a)));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite SoftDotSprite()
        {
            const int N = 64;
            float c = (N - 1) * 0.5f;
            return MakeSprite(N, N, (x, y) =>
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                return Mathf.Exp(-d * d * 5f) * (1f - Edge(0.85f, 1f, d));
            });
        }

        private static Sprite CircleSprite()
        {
            const int N = 64;
            float c = (N - 1) * 0.5f;
            return MakeSprite(N, N, (x, y) =>
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                return Mathf.Clamp01(c - d);
            });
        }

        /// <summary>Кольцо радиуса r (доля от половины размера) толщиной 2w, со сглаженными краями.</summary>
        private static Sprite RingSprite(float r, float w)
        {
            const int N = 256;
            float c = (N - 1) * 0.5f, aa = 1.2f / c;
            return MakeSprite(N, N, (x, y) =>
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                return 1f - Edge(w - aa, w + aa, Mathf.Abs(d - r));
            });
        }

        private static Sprite GlowRingSprite(float r)
        {
            const int N = 256;
            float c = (N - 1) * 0.5f;
            return MakeSprite(N, N, (x, y) =>
            {
                float d = (Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c - r) / 0.07f;
                return Mathf.Exp(-d * d);
            });
        }

        private static Sprite DashedRingSprite()
        {
            const int N = 256;
            float c = (N - 1) * 0.5f, aa = 1.2f / c;
            return MakeSprite(N, N, (x, y) =>
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / c;
                float ring = 1f - Edge(0.012f - aa, 0.012f + aa, Mathf.Abs(d - 0.92f));
                float f = Mathf.Repeat((Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f) * 48f, 1f);
                float dash = Edge(0f, 0.06f, f) * (1f - Edge(0.34f, 0.4f, f));
                return ring * dash;
            });
        }
    }
}
