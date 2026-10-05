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
    /// Оформление: арт на весь экран с медленным «наездом камеры», название, подзаголовок с контекстом,
    /// светящаяся полоса прогресса, этап и сменяющиеся советы.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        public enum Kind { Launch, NewGame, LoadGame, Menu }

        private static LoadingScreen s_inst;
        public static bool IsActive => s_inst != null && s_inst._active;

        private CanvasGroup _group;
        private RectTransform _artRt;
        private Text _subtitle, _stage, _percent, _tip, _detail;
        private RectTransform _barFill, _barHead, _spinner;
        private CanvasGroup _tipGroup;

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
            "Звёздную базу нужно вывести из строя в бою — только после этого начнётся осада системы.",
            "Адмирал на флагмане усиливает все ваши боевые корабли в той же системе.",
            "Учёный в научном совете ускоряет все исследования империи.",
            "В режиме спирали рукава соединены узкими проходами — их выгодно оборонять.",
            "Корабль с нулевым корпусом может выйти из боя и уйти на ремонт — у корветов шанс выше.",
            "Губернатор ускоряет стройку на своей планете: районы и колонии появятся раньше.",
            "Неизведанные системы на карте тусклые: пошлите научный корабль, чтобы открыть их ресурсы.",
            "Shift + ПКМ добавляет точку в маршрут флота, Backspace убирает последнюю.",
            "Содержание флота растёт сверх лимита: следите за прогнозом энергии в верхней панели.",
            "Пробел — пауза, клавиши 1, 2, 3 — скорость времени.",
            "Перемирие после войны длится 3 года — используйте его, чтобы отстроить флот.",
            "Аномалии, найденные при разведке, могут дать науку, ресурсы или неприятности.",
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

            var tex = Resources.Load<Texture2D>("UI/LoadingArt");
            var art = new GameObject("Art").AddComponent<RawImage>();
            art.transform.SetParent(root, false);
            art.texture = tex;
            art.raycastTarget = false;
            _artRt = art.rectTransform;
            _artRt.anchorMin = _artRt.anchorMax = new Vector2(0.5f, 0.5f);
            var fitter = art.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = tex != null ? tex.width / (float)tex.height : 16f / 9f;
            if (tex == null) art.color = new Color(0.02f, 0.05f, 0.09f);

            // Затемнение снизу и по краям — текст читается на любом фрагменте арта
            var shade = NewImage(root, "Shade", Color.white);
            Stretch(shade.rectTransform);
            shade.sprite = VerticalGradientSprite();
            shade.type = Image.Type.Simple;

            // Название
            var title = NewText(root, "ASTROPOLITY", 64, Color.white, TextAnchor.LowerLeft, true);
            title.rectTransform.anchorMin = new Vector2(0, 0);
            title.rectTransform.anchorMax = new Vector2(1, 0);
            title.rectTransform.pivot = new Vector2(0, 0);
            title.rectTransform.anchoredPosition = new Vector2(96, 196);
            title.rectTransform.sizeDelta = new Vector2(-192, 80);
            var glow = title.gameObject.AddComponent<Shadow>();
            glow.effectColor = new Color(0.2f, 0.9f, 0.95f, 0.35f);
            glow.effectDistance = new Vector2(0, -3);

            _subtitle = NewText(root, "", 18, new Color(0.72f, 0.88f, 0.92f, 0.95f), TextAnchor.UpperLeft, false);
            _subtitle.rectTransform.anchorMin = new Vector2(0, 0);
            _subtitle.rectTransform.anchorMax = new Vector2(1, 0);
            _subtitle.rectTransform.pivot = new Vector2(0, 1);
            _subtitle.rectTransform.anchoredPosition = new Vector2(100, 192);
            _subtitle.rectTransform.sizeDelta = new Vector2(-200, 30);

            // Полоса прогресса
            var track = NewImage(root, "Track", new Color(1f, 1f, 1f, 0.10f));
            track.rectTransform.anchorMin = new Vector2(0, 0);
            track.rectTransform.anchorMax = new Vector2(1, 0);
            track.rectTransform.pivot = new Vector2(0.5f, 0);
            track.rectTransform.anchoredPosition = new Vector2(0, 120);
            track.rectTransform.sizeDelta = new Vector2(-192, 4);

            var fill = NewImage(track.rectTransform, "Fill", Cyan);
            _barFill = fill.rectTransform;
            _barFill.anchorMin = new Vector2(0, 0);
            _barFill.anchorMax = new Vector2(0, 1);
            _barFill.pivot = new Vector2(0, 0.5f);
            _barFill.offsetMin = _barFill.offsetMax = Vector2.zero;

            var head = NewImage(track.rectTransform, "Head", new Color(0.7f, 1f, 1f, 0.9f));
            head.sprite = SoftDotSprite();
            _barHead = head.rectTransform;
            _barHead.anchorMin = _barHead.anchorMax = new Vector2(0, 0.5f);
            _barHead.sizeDelta = new Vector2(28, 28);

            _stage = NewText(root, "", 15, new Color(0.85f, 0.95f, 0.98f, 0.95f), TextAnchor.LowerLeft, true);
            _stage.rectTransform.anchorMin = new Vector2(0, 0);
            _stage.rectTransform.anchorMax = new Vector2(1, 0);
            _stage.rectTransform.pivot = new Vector2(0, 0);
            _stage.rectTransform.anchoredPosition = new Vector2(130, 132);
            _stage.rectTransform.sizeDelta = new Vector2(-400, 24);

            _percent = NewText(root, "", 15, Cyan, TextAnchor.LowerRight, true);
            _percent.rectTransform.anchorMin = new Vector2(1, 0);
            _percent.rectTransform.anchorMax = new Vector2(1, 0);
            _percent.rectTransform.pivot = new Vector2(1, 0);
            _percent.rectTransform.anchoredPosition = new Vector2(-96, 132);
            _percent.rectTransform.sizeDelta = new Vector2(200, 24);

            _detail = NewText(root, "", 12, new Color(0.62f, 0.74f, 0.8f, 0.85f), TextAnchor.UpperLeft, false);
            _detail.rectTransform.anchorMin = new Vector2(0, 0);
            _detail.rectTransform.anchorMax = new Vector2(1, 0);
            _detail.rectTransform.pivot = new Vector2(0, 1);
            _detail.rectTransform.anchoredPosition = new Vector2(96, 116);
            _detail.rectTransform.sizeDelta = new Vector2(-192, 18);

            // Вращающееся кольцо возле этапа
            var spin = NewImage(root, "Spinner", Cyan);
            spin.sprite = ArcSprite();
            _spinner = spin.rectTransform;
            _spinner.anchorMin = _spinner.anchorMax = new Vector2(0, 0);
            _spinner.anchoredPosition = new Vector2(110, 143);
            _spinner.sizeDelta = new Vector2(20, 20);

            // Совет
            _tip = NewText(root, "", 15, new Color(0.78f, 0.86f, 0.9f, 0.9f), TextAnchor.UpperLeft, false);
            _tip.rectTransform.anchorMin = new Vector2(0, 0);
            _tip.rectTransform.anchorMax = new Vector2(1, 0);
            _tip.rectTransform.pivot = new Vector2(0, 1);
            _tip.rectTransform.anchoredPosition = new Vector2(96, 88);
            _tip.rectTransform.sizeDelta = new Vector2(-192, 60);
            _tipGroup = _tip.gameObject.AddComponent<CanvasGroup>();
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
            _artRt.localScale = Vector3.one;
            _subtitle.text = Subtitle(kind);
            _detail.text = "";
            _gen = null;
            _duration = kind == Kind.Menu ? UnityEngine.Random.Range(2.8f, 3.4f) : UnityEngine.Random.Range(8f, 10f);
            _stages = StagesFor(kind);
            BuildCurve();
            NextTip();
            Apply();
        }

        /// <summary>Подписи этапов: сменяются по ходу полосы.</summary>
        private static string[] StagesFor(Kind kind) => kind switch
        {
            Kind.NewGame => new[]
            {
                "Инициализация ядра симуляции", "Генерация звёздных систем", "Прокладка гиперкоридоров",
                "Расчёт орбит и планет", "Заселение империй", "Пробуждение империи-соперника",
                "Синтез звукового ландшафта", "Подготовка командного интерфейса"
            },
            Kind.LoadGame => new[]
            {
                "Чтение архива сохранения", "Восстановление галактики", "Возвращение флотов на орбиты",
                "Восстановление экономики", "Синхронизация дипломатии", "Пробуждение лидеров",
                "Синтез звукового ландшафта", "Подготовка командного интерфейса"
            },
            Kind.Menu => new[] { "Сохранение состояния вселенной", "Возвращение в главное меню" },
            _ => new[]
            {
                "Инициализация ядра", "Компиляция шейдеров стекла", "Подготовка звёздного неба",
                "Генерация галактики", "Синтез звукового ландшафта", "Настройка музыки",
                "Сборка интерфейса", "Последние штрихи"
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
                    return $"Новая партия   ·   {NewGameSettings.SizeNames[Mathf.Clamp(s.GalaxySize, 0, 2)].ToLower()} галактика   ·   " +
                           $"{NewGameSettings.ShapeNames[Mathf.Clamp(s.Shape, 0, NewGameSettings.ShapeNames.Length - 1)].ToLower()}   ·   " +
                           $"сложность: {NewGameSettings.DifficultyNames[Mathf.Clamp(s.Difficulty, 0, 2)].ToLower()}";
                case Kind.LoadGame:
                {
                    var m = GameSession.PendingLoad?.Meta;
                    return m != null ? $"Загрузка сохранения   ·   {m.Name}   ·   {m.Empire}   ·   {m.GameDate}" : "Загрузка сохранения";
                }
                case Kind.Menu: return "Возвращение в главное меню";
                default: return "Галактическая стратегия";
            }
        }

        private void NextTip()
        {
            int i;
            do { i = UnityEngine.Random.Range(0, Tips.Length); } while (i == _tipIndex && Tips.Length > 1);
            _tipIndex = i;
            _tip.text = "<color=#5CF2EB>СОВЕТ</color>   " + Tips[i];
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
            _stage.text = _shown >= 0.999f ? "Готово" : _stages[si] + Dots(elapsed);
            _percent.text = $"{_shown * 100f:0}%";
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
            if (_tipTimer > 6f) { _tipTimer = 0f; NextTip(); }
            _tipGroup.alpha = Mathf.Clamp01(Mathf.Min(_tipTimer / 0.5f, (6f - _tipTimer) / 0.5f));

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
                    sb.Append($"звёздных систем {Mathf.RoundToInt(_gen.Systems.Count * k)}   ·   гиперкоридоров {Mathf.RoundToInt(_gen.Hyperlanes.Count * k)}   ·   ");
                }
            }
            sb.Append($"звуков синтезировано {sfx * 100f:0}%");
            if (_kind != Kind.Menu) sb.Append($"   ·   шейдеров {Mathf.RoundToInt(Mathf.Clamp01(_shown * 1.3f) * 48)}/48");
            return sb.ToString();
        }

        private void Animate(float dt)
        {
            float t = Time.unscaledTime - _startTime;
            // «Наезд камеры»: 1.00 → 1.08 за 25 секунд и лёгкий дрейф
            float k = Mathf.Clamp01(t / 25f);
            _artRt.localScale = Vector3.one * Mathf.Lerp(1.0f, 1.08f, Mathf.SmoothStep(0f, 1f, k));
            _artRt.anchoredPosition = new Vector2(Mathf.Sin(t * 0.05f) * 18f, Mathf.Cos(t * 0.04f) * 10f);
            _spinner.localRotation = Quaternion.Euler(0f, 0f, -t * 240f);

            var parent = (RectTransform)_barFill.parent;
            float w = parent.rect.width;
            _barFill.sizeDelta = new Vector2(w * _shown, 0f);
            _barHead.anchoredPosition = new Vector2(w * _shown, 0f);
            float pulse = 0.75f + 0.25f * Mathf.Sin(t * 5f);
            var hc = _barHead.GetComponent<Image>().color;
            hc.a = 0.9f * pulse;
            _barHead.GetComponent<Image>().color = hc;
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

        /// <summary>Затемнение: плотное внизу (под текстом), лёгкая виньетка по краям.</summary>
        private static Sprite VerticalGradientSprite()
        {
            const int W = 64, H = 256;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = y / (float)(H - 1), u = x / (float)(W - 1);
                float bottom = Mathf.Pow(1f - Mathf.Clamp01(v / 0.45f), 1.6f) * 0.88f;
                float side = Mathf.Pow(Mathf.Abs(u - 0.5f) * 2f, 3f) * 0.35f;
                float top = Mathf.Pow(Mathf.Clamp01((v - 0.85f) / 0.15f), 2f) * 0.35f;
                tex.SetPixel(x, y, new Color(0.0f, 0.01f, 0.02f, Mathf.Clamp01(bottom + side + top)));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite SoftDotSprite()
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (N - 1) * 0.5f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Exp(-d * d * 5f) * (1f - Mathf.SmoothStep(0.85f, 1f, d))));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Дуга 3/4 окружности со сглаженными краями — индикатор «идёт работа».</summary>
        private static Sprite ArcSprite()
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (N - 1) * 0.5f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / c;
                float ring = 1f - Mathf.SmoothStep(0.06f, 0.12f, Mathf.Abs(d - 0.8f));
                float ang = (Mathf.Atan2(dy, dx) + Mathf.PI) / (2f * Mathf.PI);   // 0..1
                float arc = Mathf.SmoothStep(0f, 0.75f, ang);                       // хвост тает
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, ring * arc));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
