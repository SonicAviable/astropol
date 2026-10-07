using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Rendering;
using StellarisClone.Core.Audio;
using StellarisClone.Generation;
using Object = UnityEngine.Object;

namespace StellarisClone.Core
{
    /// <summary>
    /// Обучение с ИИ-советником ОРАКУЛ-7 (живой аватар-робот).
    /// Каждый шаг — короткий рассказ и, если нужно, задание: что сделать в игре. Нужный элемент
    /// подсвечивается — экран вокруг притушен, рамка пульсирует, стрелка указывает на цель
    /// (кнопку интерфейса, корабль или звезду на карте). Задание засчитывается само, когда игрок
    /// его выполнил; информационные шаги — кнопкой «Далее». Любой шаг можно пропустить,
    /// «Показать» наводит камеру на цель.
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        public static TutorialManager Instance { get; private set; }

        private const float TypeSpeed = 75f;                     // символов в секунду
        private const float CompleteDelay = 1.1f;                // пауза после выполненного задания
        private const float CardW = 600f, CardH = 214f, AvatarW = 150f;

        private sealed class Step
        {
            public string Title;
            public string Text;
            /// <summary>Задание; null — информационный шаг с кнопкой «Далее».</summary>
            public string Task;
            public Func<bool> Done;
            /// <summary>Что подсветить: ключ UIAnchors или точка в мире. Оба null — без подсветки.</summary>
            public string Anchor;
            public Func<Vector3?> World;
            public Action Focus;
            public Action Enter;
        }

        private readonly List<Step> _steps = new List<Step>();
        private int _index = -1;
        private bool _active;
        private float _completeTimer = -1f;

        // --- UI ---
        private GameObject _root;
        private RectTransform _stage, _card;
        private CanvasGroup _cardGroup;
        private LiquidGlassEffect _cardFx;
        private TutorAvatar _avatar;
        private Text _stepLabel, _title, _body, _taskText;
        private Image _taskIcon;
        private RectTransform _progress;
        private Button _next, _show;
        private Text _nextLabel;
        private readonly Image[] _dim = new Image[4];
        private Image _frame, _glow, _arrow;
        private float _dimAlpha;
        private Rect _hole;
        private bool _holeValid;

        // --- набор текста ---
        private string _fullText = "";
        private float _typed;

        // --- состояние заданий ---
        private int _speedAtEnter;
        private MapModeController.MapMode _modeAtEnter;
        private bool _overviewSeen;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ==================================================================== API

        public void BeginTutorial()
        {
            BuildSteps();
            BuildUI();
            _active = true;
            Go(0);
        }

        /// <summary>Закрыть обучение досрочно.</summary>
        public void SkipTutorial()
        {
            _active = false;
            _index = -1;
            if (_root != null) LG.Hide(_card.gameObject, () => { if (_root != null) _root.SetActive(false); });
        }

        // Оставлены для совместимости со старыми вызовами
        public void CompleteSurveyStep() { }
        public void CompleteBuildStep() { }

        /// <summary>Перейти к шагу по номеру (с 1) — для отладки.</summary>
        public void SetStep(int step)
        {
            if (_steps.Count == 0) BeginTutorial();
            Go(Mathf.Clamp(step - 1, 0, _steps.Count - 1));
        }

        // ==================================================================== ШАГИ

        private void BuildSteps()
        {
            _steps.Clear();

            _steps.Add(new Step
            {
                Title = "Связь установлена",
                Text = "Командующий, я ОРАКУЛ-7, бортовой ИИ-советник. Проведу вас через первые шаги: время, " +
                       "разведку, границы, колонии и науку.\n\nПодсвеченный элемент — то, с чем нужно поработать. " +
                       "Кнопка <b>«Показать»</b> наведёт камеру на цель."
            });

            _steps.Add(new Step
            {
                Title = "Правитель и обзор империи",
                Text = "В левом верхнем углу — правитель вашей империи. Щёлчок по портрету открывает " +
                       "<b>обзор империи</b>: ресурсы, колонии, флоты, лидеры, соперники и путь к победе.",
                Task = "Откройте обзор империи, затем закройте его (Esc)",
                Anchor = UIAnchors.Ruler,
                Enter = () => _overviewSeen = false,
                Done = () =>
                {
                    bool open = EmpireOverviewWindow.Instance != null && EmpireOverviewWindow.Instance.IsOpen;
                    if (open) _overviewSeen = true;
                    return _overviewSeen && !open;
                }
            });

            _steps.Add(new Step
            {
                Title = "Ресурсы",
                Text = "<color=#F2C747>Гелий-3</color> питает флот и станции, <color=#4DE6CC>титан</color> идёт на стройку, " +
                       "<color=#F29E5C>сплавы</color> — на корабли, <color=#FF7A94>влияние</color> — на форпосты и лидеров, " +
                       "<color=#5CF59A>наука</color> двигает исследования.\n\nНаведите курсор на ресурс — подсказка покажет доходы и расходы.",
                Anchor = UIAnchors.Resources
            });

            _steps.Add(new Step
            {
                Title = "Ход времени",
                Text = "Галактика живёт в реальном времени. <b>Пробел</b> — пауза и пуск, клавиши <b>1</b>, <b>2</b>, <b>3</b> — скорость. " +
                       "На паузе можно спокойно отдавать приказы.",
                Task = "Запустите время или смените скорость",
                Anchor = UIAnchors.Speed,
                Enter = () => _speedAtEnter = TimeManager.Instance != null ? TimeManager.Instance.CurrentSpeed : 0,
                Done = () => TimeManager.Instance != null && TimeManager.Instance.CurrentSpeed != _speedAtEnter
            });

            _steps.Add(new Step
            {
                Title = "Научный корабль",
                Text = "Ваш первый инструмент — научный корабль. Он изучает соседние системы, находит залежи, " +
                       "аномалии и места для колоний.",
                Task = "Выделите научный корабль — щелчок ЛКМ по его значку",
                World = () => ShipPos(FleetType.Science),
                Focus = () => FocusShip(FleetType.Science),
                Done = () => Selected(FleetType.Science)
            });

            _steps.Add(new Step
            {
                Title = "Разведка",
                Text = "Серые системы ещё не изучены. Щелчок <b>ПКМ</b> по системе отправляет корабль; пунктир покажет маршрут " +
                       "и дни в пути. <b>Shift+ПКМ</b> добавляет цель в очередь.",
                Task = "ПКМ по подсвеченной системе — отправьте разведчика",
                World = () => SystemPos(SurveyTarget()),
                Focus = () => FocusSystem(SurveyTarget()),
                Done = HasScienceShipOnMission
            });

            _steps.Add(new Step
            {
                Title = "Панель флота",
                Text = "Внизу — панель выделенного флота: состояние, прочность, маршрут и лидер на борту. " +
                       "Кнопки справа: стоп, следующая цель, очистить очередь, камера к флоту, снять выделение.",
                Anchor = UIAnchors.FleetHud
            });

            _steps.Add(new Step
            {
                Title = "Строительный корабль",
                Text = "Границы растут форпостами. Их строит строительный корабль — за титан, а каждый форпост " +
                       "стоит 2 гелия-3 в месяц.",
                Task = "Выделите строительный корабль",
                World = () => ShipPos(FleetType.Constructor),
                Focus = () => FocusShip(FleetType.Constructor),
                Done = () => Selected(FleetType.Constructor)
            });

            _steps.Add(new Step
            {
                Title = "Первый форпост",
                Text = "Форпост можно заложить только в изученной системе. Если подходящей пока нет — ускорьте время " +
                       "(клавиша <b>3</b>) и дождитесь разведчика.",
                Task = "ПКМ строительным кораблём по изученной соседней системе",
                World = () => SystemPos(OutpostTarget()),
                Focus = () => FocusSystem(OutpostTarget()),
                Done = () => HasAnyStarbaseBuilt() || ConstructorBusy()
            });

            _steps.Add(new Step
            {
                Title = "Режимы карты",
                Text = "Карта умеет показывать разное: <b>политику</b> (чьи системы), <b>ресурсы</b>, <b>коридоры</b> и " +
                       "<b>разведку</b>. Удобно, когда галактика разрастётся.",
                Task = "Переключите режим карты",
                Anchor = UIAnchors.MapModes,
                Enter = () => _modeAtEnter = MapModeController.Instance != null ? MapModeController.Instance.Active : default,
                Done = () => MapModeController.Instance != null && MapModeController.Instance.Active != _modeAtEnter
            });

            _steps.Add(new Step
            {
                Title = "Столица",
                Text = "Внутри системы видны планеты, станции и флоты. Столица — сердце империи: здесь строятся районы.",
                Task = "Двойной щелчок по звезде столицы",
                World = () => SystemPos(Capital()),
                Focus = () => FocusSystem(Capital()),
                Done = () => SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView
            });

            _steps.Add(new Step
            {
                Title = "Обзор планеты",
                Text = "Щёлкните по обитаемой планете — откроется её обзор: население, производство и районы.",
                Task = "Откройте обзор планеты-столицы",
                Done = () => PlanetFocusOverlay.Instance != null && PlanetFocusOverlay.Instance.IsOpen
            });

            _steps.Add(new Step
            {
                Title = "Районы",
                Text = "Каждый район — рабочие места: <b>городской</b> даёт жильё, <b>горнодобывающий</b> — титан, " +
                       "<b>энергетический</b> — гелий-3, <b>промышленный</b> — сплавы. Население растёт, пока есть жильё.\n\n" +
                       "Закройте обзор (Esc), когда будете готовы."
            });

            _steps.Add(new Step
            {
                Title = "Исследования",
                Text = "Наука открывает новые районы, корабли и бонусы. Выберите направление в одной из трёх ветвей — " +
                       "физика, общество, инженерия.",
                Task = "Откройте исследования",
                Anchor = UIAnchors.Tech,
                Done = () => TechTreePanel.Instance != null && TechTreePanel.Instance.IsOpen
            });

            _steps.Add(new Step
            {
                Title = "Соседи",
                Text = "В галактике вы не одни. <b>Дипломатия</b> — договоры, торговля и войны. Соперники следят за " +
                       "вашей мощью: слабую империю попробуют съесть.",
                Anchor = UIAnchors.Diplomacy
            });

            _steps.Add(new Step
            {
                Title = "Путь к победе",
                Text = "Победить можно доминированием, наукой или по очкам к концу эпохи. Цели и прогресс — в обзоре империи " +
                       "и в меню (Esc).\n\nУдачи, Командующий. ОРАКУЛ-7 на связи."
            });
        }

        // ==================================================================== ЛОГИКА

        private void Go(int index)
        {
            if (index >= _steps.Count) { Finish(); return; }
            _index = index;
            _completeTimer = -1f;
            var s = _steps[index];
            s.Enter?.Invoke();

            _title.text = s.Title;
            _fullText = s.Text ?? "";
            _typed = 0f;
            _body.text = "";
            _stepLabel.text = $"ШАГ {index + 1} / {_steps.Count}";
            LGBuild.SetBar(_progress, (index + 1) / (float)_steps.Count, UIManager.DS.NeonCyan);

            bool task = s.Task != null;
            _taskText.transform.parent.gameObject.SetActive(task);
            if (task) SetTask(s.Task, false);
            _nextLabel.text = task ? "ПРОПУСТИТЬ" : index == _steps.Count - 1 ? "ЗАВЕРШИТЬ" : "ДАЛЕЕ";
            _show.gameObject.SetActive(s.Focus != null);

            _avatar?.Pulse();
            _cardFx?.SetPulse(1f);
            _pulse = 1f;
            if (index > 0) SFXManager.Play(Sfx.UiConfirm, 0.6f);
        }

        private void SetTask(string text, bool done)
        {
            _taskText.text = done ? $"<color=#5CF59A>{text}</color>" : text;
            _taskIcon.sprite = LGIcons.Get(done ? LGIcon.Check : LGIcon.Target);
            _taskIcon.color = done ? UIManager.DS.Green : UIManager.DS.Gold;
        }

        private void Next()
        {
            if (!_active) return;
            if (_typed < _fullText.Length) { _typed = _fullText.Length; return; }   // сначала — дописать текст
            Go(_index + 1);
        }

        private void Finish()
        {
            SFXManager.Play(Sfx.NotifySuccess, 0.7f);
            SkipTutorial();
        }

        private float _pulse;

        private void Update()
        {
            if (!_active || _root == null || _index < 0) return;
            float dt = Time.unscaledDeltaTime;
            var s = _steps[_index];

            // Карточку прячем под паузой, событием и экраном итогов
            bool blocked = (UIManager.Instance != null && UIManager.Instance.IsBlockingFlowOpen) ||
                           (GameFlowUI.Instance != null && GameFlowUI.Instance.BlocksTimeHotkeys);
            _cardGroup.alpha = Mathf.MoveTowards(_cardGroup.alpha, blocked ? 0f : 1f, dt * 6f);
            _cardGroup.blocksRaycasts = !blocked;

            // Набор текста (щелчок по тексту или «Далее» дописывает его сразу)
            if (_typed < _fullText.Length)
            {
                _typed = Mathf.Min(_fullText.Length, _typed + dt * TypeSpeed);
                _body.text = Reveal(_fullText, Mathf.FloorToInt(_typed));
            }
            else if (_body.text != _fullText) _body.text = _fullText;
            if (_avatar != null) _avatar.Speaking = _typed < _fullText.Length;

            // Задание
            if (s.Done != null && _completeTimer < 0f && SafeDone(s))
            {
                _completeTimer = CompleteDelay;
                SetTask(s.Task, true);
                SFXManager.Play(Sfx.NotifySuccess, 0.55f);
                _cardFx?.SetPulse(1f);
            }
            if (_completeTimer >= 0f)
            {
                _completeTimer -= dt;
                if (_completeTimer < 0f) { Go(_index + 1); return; }
            }

            if (Input.GetKeyDown(KeyCode.Return) && s.Done == null && !blocked) Next();

            UpdateHighlight(s, blocked, dt);

            _pulse = Mathf.MoveTowards(_pulse, 0f, dt * 1.5f);
        }

        private static bool SafeDone(Step s)
        {
            try { return s.Done(); }
            catch (Exception e) { Debug.LogWarning($"[Tutorial] {s.Title}: {e.Message}"); return false; }
        }

        /// <summary>Часть текста с rich-тегами: незакрытые теги дописываются, чтобы Text не показал их буквами.</summary>
        private static string Reveal(string text, int visible)
        {
            var sb = new StringBuilder();
            var open = new Stack<string>();
            int shown = 0;
            for (int i = 0; i < text.Length && shown < visible; i++)
            {
                if (text[i] == '<')
                {
                    int end = text.IndexOf('>', i);
                    if (end > i)
                    {
                        string tag = text.Substring(i + 1, end - i - 1);
                        if (tag.StartsWith("/")) { if (open.Count > 0) open.Pop(); }
                        else open.Push(tag.Split('=')[0]);
                        sb.Append(text, i, end - i + 1);
                        i = end;
                        continue;
                    }
                }
                sb.Append(text[i]);
                shown++;
            }
            while (open.Count > 0) sb.Append("</").Append(open.Pop()).Append('>');
            return sb.ToString();
        }

        // ==================================================================== ПОДСВЕТКА

        private void UpdateHighlight(Step s, bool blocked, float dt)
        {
            Rect target = default;
            bool has = !blocked && TryTargetRect(s, out target);
            if (has)
            {
                if (!_holeValid) { _hole = Inflate(target, 40f); _holeValid = true; }
                float k = 1f - Mathf.Exp(-dt * 12f);
                _hole = new Rect(Vector2.Lerp(_hole.position, target.position, k), Vector2.Lerp(_hole.size, target.size, k));
            }
            _dimAlpha = Mathf.MoveTowards(_dimAlpha, has ? 0.5f : 0f, dt * 2.5f);
            if (_dimAlpha <= 0.001f) _holeValid = false;

            var stage = _stage.rect;
            float W = stage.width, H = stage.height;
            var h = _hole;
            SetDim(_dim[0], 0, 0, W, h.yMin);
            SetDim(_dim[1], 0, h.yMax, W, H - h.yMax);
            SetDim(_dim[2], 0, h.yMin, h.xMin, h.height);
            SetDim(_dim[3], h.xMax, h.yMin, W - h.xMax, h.height);

            // Рамка и свечение пульсируют
            float t = Time.unscaledTime;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 4f);
            float a = _dimAlpha / 0.5f;
            var c = UIManager.DS.NeonCyan;
            Place(_frame.rectTransform, Inflate(h, 3f + 3f * pulse));
            _frame.color = new Color(c.r, c.g, c.b, a * (0.75f + 0.25f * pulse));
            Place(_glow.rectTransform, Inflate(h, 18f + 4f * pulse));
            _glow.color = new Color(c.r, c.g, c.b, a * (0.35f + 0.25f * pulse + 0.3f * _pulse));

            // Стрелка — снаружи рамки со стороны центра экрана, покачивается
            bool upper = h.center.y > H * 0.5f;
            float bob = 6f * Mathf.Sin(t * 5f);
            Vector2 pos = upper ? new Vector2(h.center.x, h.yMin - 26f - bob) : new Vector2(h.center.x, h.yMax + 26f + bob);
            _arrow.rectTransform.anchoredPosition = pos;
            _arrow.rectTransform.localEulerAngles = new Vector3(0, 0, upper ? -90f : 90f);   // значок «назад» смотрит влево
            _arrow.color = new Color(c.r, c.g, c.b, a);

            // Карточка — в углу, который не закрывает цель
            bool rightCorner = has && h.center.x < W * 0.5f && h.yMin < CardH + 40f;
            float x = rightCorner ? W - CardW - 16f : 16f;
            _card.anchoredPosition = Vector2.Lerp(_card.anchoredPosition, new Vector2(x, 16f), 1f - Mathf.Exp(-dt * 10f));
        }

        private bool TryTargetRect(Step s, out Rect r)
        {
            r = default;
            if (s.Anchor != null && UIAnchors.TryGetScreenRect(s.Anchor, out var sr))
            {
                r = Inflate(ToStage(sr), 6f);
                return r.width > 1f;
            }
            if (s.World != null)
            {
                Vector3? w = null;
                try { w = s.World(); } catch { }
                var cam = WorldCamera();
                if (w == null || cam == null) return false;
                var sp = cam.WorldToScreenPoint(w.Value);
                if (sp.z < 0f) return false;
                var p = ToStage(new Rect(sp.x, sp.y, 0f, 0f)).position;
                r = new Rect(p.x - 38f, p.y - 38f, 76f, 76f);
                var st = _stage.rect;
                return p.x > 0f && p.y > 0f && p.x < st.width && p.y < st.height;
            }
            return false;
        }

        private static Camera WorldCamera()
        {
            var cam = Camera.main;
            if (cam != null) return cam;
            var ctrl = Object.FindAnyObjectByType<StellarisClone.Cam.StrategyCameraController>();
            return ctrl != null ? ctrl.GetComponentInChildren<Camera>() : null;
        }

        private Rect ToStage(Rect screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_stage, screen.min, null, out var a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_stage, screen.max, null, out var b);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        private static Rect Inflate(Rect r, float d) => new Rect(r.x - d, r.y - d, r.width + d * 2f, r.height + d * 2f);

        private void SetDim(Image img, float x, float y, float w, float h)
        {
            var rt = img.rectTransform;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, w), Mathf.Max(0f, h));
            img.color = new Color(0.005f, 0.012f, 0.02f, _dimAlpha);
        }

        private static void Place(RectTransform rt, Rect r)
        {
            rt.anchoredPosition = r.position;
            rt.sizeDelta = r.size;
        }

        // ==================================================================== ЦЕЛИ В МИРЕ

        private static FleetView FindShip(FleetType type)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return null;
            foreach (var f in fm.AllFleets)
                if (f?.Data != null && !f.Data.Destroyed && f.Data.OwnerId == 0 && f.Data.Type == type) return f;
            return null;
        }

        private static bool InGalaxy => SystemViewManager.Instance == null || !SystemViewManager.Instance.IsInSystemView;

        private static Vector3? ShipPos(FleetType type)
        {
            var f = FindShip(type);
            return f != null && InGalaxy ? f.transform.position : (Vector3?)null;
        }

        private static Vector3? SystemPos(StarSystem s) => s != null && InGalaxy ? s.Position : (Vector3?)null;

        private static bool Selected(FleetType type)
        {
            var sel = FleetManager.Instance?.SelectedFleet;
            return sel != null && sel.Data != null && sel.Data.OwnerId == 0 && sel.Data.Type == type;
        }

        private static void FocusShip(FleetType type)
        {
            var f = FindShip(type);
            if (f != null) FleetSelectionController.FocusCamera(f);
        }

        private static void FocusSystem(StarSystem s)
        {
            if (s == null) return;
            Object.FindAnyObjectByType<StellarisClone.Cam.StrategyCameraController>()?.FocusOn(s.Position);
        }

        private static GalaxyGenerator Gen => Object.FindAnyObjectByType<GalaxyGenerator>();

        /// <summary>Ближайшая к разведчику неизученная система (сначала — соседи по коридорам).</summary>
        private static StarSystem SurveyTarget()
        {
            var ship = FindShip(FleetType.Science)?.Data;
            var gen = Gen;
            if (ship == null || gen == null) return null;
            if (ship.SurveyTargetSystemId >= 0) return EmpireStats.GetSystem(ship.SurveyTargetSystemId);
            var from = EmpireStats.GetSystem(ship.CurrentSystemId);
            if (from == null) return null;
            foreach (int id in from.ConnectedSystemIds)
            {
                var s = EmpireStats.GetSystem(id);
                if (s != null && !s.IsSurveyedBy(0)) return s;
            }
            return Nearest(gen, from.Position, s => !s.IsSurveyedBy(0));
        }

        /// <summary>Изученная ничейная система рядом со строительным кораблём; иначе — цель разведчика.</summary>
        private static StarSystem OutpostTarget()
        {
            var ship = FindShip(FleetType.Constructor)?.Data;
            var gen = Gen;
            if (ship == null || gen == null) return null;
            var from = EmpireStats.GetSystem(ship.CurrentSystemId);
            if (from == null) return null;
            var t = Nearest(gen, from.Position, s => s.OwnerId < 0 && !s.HasStarbase && s.IsSurveyedBy(0));
            return t ?? SurveyTarget();
        }

        private static StarSystem Capital()
        {
            var gen = Gen;
            if (gen == null) return null;
            foreach (var s in gen.Systems)
            {
                if (s.OwnerId != 0) continue;
                foreach (var p in s.Planets) if (p.Population > 0) return s;
            }
            return null;
        }

        private static StarSystem Nearest(GalaxyGenerator gen, Vector3 from, Func<StarSystem, bool> filter)
        {
            StarSystem best = null;
            float bestD = float.MaxValue;
            foreach (var s in gen.Systems)
            {
                if (!filter(s)) continue;
                float d = (s.Position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary>Научный кораблю выдан приказ на разведку.</summary>
        private static bool HasScienceShipOnMission()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return false;
            foreach (var f in fm.AllFleets)
            {
                if (f?.Data == null || f.Data.OwnerId != 0 || f.Data.Type != FleetType.Science) continue;
                if (f.Data.SurveyTargetSystemId >= 0 || f.Data.State == FleetState.Surveying) return true;
            }
            return false;
        }

        private static bool ConstructorBusy()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return false;
            foreach (var f in fm.AllFleets)
                if (f?.Data != null && f.Data.OwnerId == 0 && f.Data.Type == FleetType.Constructor && f.Data.State == FleetState.Constructing)
                    return true;
            return false;
        }

        /// <summary>Построен хотя бы один форпост, кроме столичного.</summary>
        private static bool HasAnyStarbaseBuilt()
        {
            var gen = Gen;
            if (gen == null) return false;
            var cap = Capital();
            foreach (var s in gen.Systems)
                if (s.OwnerId == 0 && s.HasStarbase && s != cap) return true;
            return false;
        }

        // ==================================================================== ПОСТРОЙКА UI

        private void BuildUI()
        {
            if (_root != null) Destroy(_root);
            _root = new GameObject("[UI] Tutorial");
            _root.transform.SetParent(transform, false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 650;                           // поверх окон: советник ведёт и внутри них
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            _stage = LGBuild.Rect(_root.transform, "Stage");
            _stage.Stretch();
            _stage.pivot = Vector2.zero;
            LG.Ignore(_stage.gameObject);

            // Затемнение вокруг цели — клики проходят насквозь
            for (int i = 0; i < 4; i++)
            {
                var d = LGBuild.Panel(_stage, "Dim", Color.clear);
                d.rectTransform.anchorMin = d.rectTransform.anchorMax = Vector2.zero;
                d.rectTransform.pivot = Vector2.zero;
                _dim[i] = d;
            }
            _glow = Overlay("Glow", OutlineSprite(16, 12f, true));
            _frame = Overlay("Frame", OutlineSprite(12, 2.5f, false));
            _arrow = LGIcons.Create(_stage, LGIcon.Back, 30, Color.clear);
            _arrow.rectTransform.anchorMin = _arrow.rectTransform.anchorMax = Vector2.zero;
            _arrow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _arrow.rectTransform.sizeDelta = new Vector2(30, 30);
            _arrow.raycastTarget = false;

            BuildCard();
        }

        private Image Overlay(string name, Sprite sprite)
        {
            var img = LGBuild.Panel(_stage, name, Color.clear);
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
            img.rectTransform.pivot = Vector2.zero;
            return img;
        }

        private void BuildCard()
        {
            // Отдельный слой: его прозрачность прячет карточку под паузой и событиями,
            // не мешая анимации появления самой карточки
            var host = LGBuild.Rect(_root.transform, "CardHost");
            host.Stretch();
            _cardGroup = host.gameObject.AddComponent<CanvasGroup>();

            var rt = LGBuild.Rect(host, "Card");
            rt.At(Vector2.zero, Vector2.zero, new Vector2(16, 16), new Vector2(CardW, CardH));
            _card = rt;
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            _cardFx = LG.Glass(rt.gameObject, 22f);
            _cardFx.SetRim(new Color(0.45f, 0.95f, 0.90f, 0.55f));
            var motion = LG.Motion(rt.gameObject, LGAppear.Kind.Fade);   // без сдвига: позицию ведёт UpdateHighlight
            motion.inDuration = 0.5f;

            // Аватар слева
            var avFrame = LGBuild.Rect(rt, "Avatar");
            avFrame.anchorMin = new Vector2(0, 0);
            avFrame.anchorMax = new Vector2(0, 1);
            avFrame.pivot = new Vector2(0, 0.5f);
            avFrame.offsetMin = new Vector2(8, 8);
            avFrame.offsetMax = new Vector2(8 + AvatarW, -8);
            var avMask = LG.RoundedMask(avFrame, 16f);
            _avatar = TutorAvatar.Create(avMask, 1.3f);

            float x = AvatarW + 24f;

            // Полоса прогресса по верхнему краю
            var progHost = LGBuild.Rect(rt, "Progress");
            progHost.TopBand(10, 3, x, 46);
            _progress = LGBuild.Bar(progHost, UIManager.DS.NeonCyan, 0f, 3f);

            var who = LGBuild.Label(rt, "ОРАКУЛ-7  ·  ИИ-СОВЕТНИК", 10, UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true);
            who.rectTransform.TopBand(18, 16, x, 46);
            _stepLabel = LGBuild.Label(rt, "", 10, UIManager.DS.TextMuted, TextAnchor.UpperRight, bold: true);
            _stepLabel.rectTransform.TopBand(18, 16, x, 46);

            var close = LGBuild.Button(rt, "Close", new Color(0.16f, 0.20f, 0.24f), new Color(1f, 0.45f, 0.48f, 0.55f),
                                       SkipTutorial, LGIcon.Close, null, 9, 12f);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -10), new Vector2(28, 26));
            TooltipHelper.Attach(close.gameObject, "<b>Закрыть обучение</b>\nПодсказки больше не появятся в этой партии.");

            _title = LGBuild.Label(rt, "", 17, Color.white, TextAnchor.UpperLeft, bold: true);
            _title.rectTransform.TopBand(36, 24, x, 16);

            _body = LGBuild.Label(rt, "", 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            _body.rectTransform.TopBand(64, 84, x, 16);
            _body.lineSpacing = 1.2f;
            _body.supportRichText = true;
            _body.raycastTarget = true;
            var skipType = _body.gameObject.AddComponent<Button>();
            skipType.transition = Selectable.Transition.None;
            skipType.onClick.AddListener(() => _typed = _fullText.Length);

            // Задание
            var taskRow = LGBuild.Rect(rt, "Task");
            taskRow.TopBand(150, 20, x, 16);
            _taskIcon = LGIcons.Create(taskRow, LGIcon.Target, 15, UIManager.DS.Gold);
            _taskIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(15, 15));
            _taskText = LGBuild.Label(taskRow, "", 12, UIManager.DS.Gold, TextAnchor.MiddleLeft, bold: true);
            _taskText.rectTransform.Stretch(22, 0, 0, 0);

            // Кнопки
            _next = LGBuild.Button(rt, "Next", UIManager.DS.BtnPrimary, UIManager.DS.NeonCyan, Next, LGIcon.Play, "ДАЛЕЕ", 11);
            ((RectTransform)_next.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-14, 12), new Vector2(150, 32));
            _nextLabel = _next.GetComponentInChildren<Text>();
            _show = LGBuild.Button(rt, "Show", UIManager.DS.BtnNeutral, new Color(0.6f, 0.85f, 1f), () =>
            {
                if (_index >= 0) _steps[_index].Focus?.Invoke();
            }, LGIcon.Target, "ПОКАЗАТЬ", 11);
            ((RectTransform)_show.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-172, 12), new Vector2(130, 32));
            TooltipHelper.Attach(_show.gameObject, "Навести камеру на цель");

            LG.Skin(rt);
        }

        // ==================================================================== СПРАЙТЫ

        private static readonly Dictionary<string, Sprite> s_Outlines = new Dictionary<string, Sprite>();

        /// <summary>Скруглённая рамка (9-slice): тонкая линия или мягкое свечение наружу.</summary>
        private static Sprite OutlineSprite(int radius, float width, bool glow)
        {
            string key = $"{radius}_{width}_{glow}";
            if (s_Outlines.TryGetValue(key, out var cached) && cached != null) return cached;
            int pad = glow ? Mathf.CeilToInt(width) + 2 : 2;
            int size = (radius + pad) * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            float c = size * 0.5f, half = c - pad;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - c) - (half - radius);
                float qy = Mathf.Abs(y + 0.5f - c) - (half - radius);
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                float a = glow
                    ? (d > 0f ? Mathf.Pow(Mathf.Clamp01(1f - d / width), 2f) : 0f)
                    : Mathf.Clamp01(width * 0.5f + 0.5f - Mathf.Abs(d + width * 0.5f));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = radius + pad + 1f;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            sprite.hideFlags = HideFlags.DontSave;
            s_Outlines[key] = sprite;
            return sprite;
        }
    }
}
