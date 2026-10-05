using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    public class TutorialManager : MonoBehaviour
    {
        public static TutorialManager Instance { get; private set; }

        private GameObject _boxObj;
        private Text _titleText;
        private Text _descText;
        private Font _font;
        private int _step = 0;

        // Кэш — чтобы не дёргать FleetManager каждый кадр без нужды
        private float _pollTimer;
        private const float PollInterval = 0.15f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                 ?? Font.CreateDynamicFontFromOSFont("Arial", 13);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void BeginTutorial()
        {
            _step = 1;
            BuildUI();
            ShowStep(_step);
        }

        private void BuildUI()
        {
            var canvas = GameObject.Find("GalaxyCanvas")?.GetComponent<Canvas>();
            if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) return;

            if (_boxObj != null) Destroy(_boxObj);

            _boxObj = new GameObject("[UI] TutorialBox");
            _boxObj.transform.SetParent(canvas.transform, false);

            // Компактная карточка в левом нижнем углу: не перекрывает панель флота,
            // миникарту и инспектор системы
            var rt = _boxObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(420f, 168f);
            rt.anchoredPosition = new Vector2(16f, 16f);

            var bgImg = _boxObj.AddComponent<Image>();
            bgImg.color = UIManager.DS.BgDeep;
            bgImg.raycastTarget = false;
            _fx = LG.Glass(_boxObj, 22f);
            _fx.SetRim(new Color(0.45f, 0.95f, 0.90f, 0.5f));
            var motion = LG.Motion(_boxObj, LGAppear.Kind.SlideLeft);
            motion.distance = 34f;
            motion.inDuration = 0.5f;

            // Header bar
            var hGo = new GameObject("HeaderBar");
            hGo.transform.SetParent(_boxObj.transform, false);
            var hRt = hGo.AddComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0, 1);
            hRt.anchorMax = new Vector2(1, 1);
            hRt.pivot = new Vector2(0.5f, 1);
            hRt.sizeDelta = new Vector2(0, 32);
            hRt.anchoredPosition = Vector2.zero;
            var hImg = hGo.AddComponent<Image>();
            hImg.color = UIManager.DS.BgHeader;
            hImg.raycastTarget = false;
            LG.Header(hGo);

            var tGo = new GameObject("Title");
            tGo.transform.SetParent(hGo.transform, false);
            _titleText = tGo.AddComponent<Text>();
            _titleText.font = GameFont.Bold;
            _titleText.fontSize = 13;
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.color = UIManager.DS.NeonCyan;
            _titleText.alignment = TextAnchor.MiddleLeft;
            _titleText.raycastTarget = false;
            var tRt = _titleText.rectTransform;
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.offsetMin = new Vector2(16, 0);
            tRt.offsetMax = new Vector2(-130, 0);

            // Номер шага и кнопка «Пропустить обучение»
            _stepText = LGBuild.Label(hGo.transform, "", 10, UIManager.DS.TextMuted, TextAnchor.MiddleRight, bold: true);
            _stepText.rectTransform.Stretch(0, 0, 44, 0);
            var skip = LGBuild.Button(hGo.transform, "Skip", new Color(0.16f, 0.20f, 0.24f), new Color(1f, 0.45f, 0.48f, 0.55f),
                                      SkipTutorial, LGIcon.Close, null, 9, 11f);
            ((RectTransform)skip.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(24, 22));
            TooltipHelper.Attach(skip.gameObject, "<b>Пропустить обучение</b>\nПодсказки больше не появятся в этой партии.");

            var dGo = new GameObject("Desc");
            dGo.transform.SetParent(_boxObj.transform, false);
            _descText = dGo.AddComponent<Text>();
            _descText.font = GameFont.Regular;
            _descText.fontSize = 11;
            _descText.color = UIManager.DS.TextPrimary;
            _descText.alignment = TextAnchor.UpperLeft;
            _descText.lineSpacing = 1.35f;
            _descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _descText.verticalOverflow = VerticalWrapMode.Truncate;
            _descText.raycastTarget = false;
            var dRt = _descText.rectTransform;
            dRt.anchorMin = Vector2.zero;
            dRt.anchorMax = Vector2.one;
            dRt.offsetMin = new Vector2(14, 10);
            dRt.offsetMax = new Vector2(-14, -38);
        }

        private void ShowStep(int s)
        {
            if (_boxObj == null) return;

            // Новый шаг — вспышка неоновой кромки и мягкое проявление текста
            _stepPulse = 1f;
            _textFade = 0f;
            if (_stepText != null) _stepText.text = s >= 1 && s <= StepCount ? $"{s} / {StepCount}" : "";

            switch (s)
            {
                case 1:
                    _boxObj.SetActive(true);
                    _titleText.text = "Шаг 1: Галактическое время";
                    _descText.text = "Добро пожаловать, Командующий!\n" +
                                     "ПРОБЕЛ — пауза и пуск времени, клавиши 1, 2, 3 — скорость.\n" +
                                     "<i>Нажмите любую из этих клавиш, чтобы продолжить.</i>";
                    break;

                case 2:
                    _boxObj.SetActive(true);
                    _titleText.text = "Шаг 2: Сенсорная разведка";
                    _descText.text = "Выделите научный корабль — щёлкните ЛКМ по его значку.\n" +
                                     "Затем ПКМ по соседней серой (неизученной) системе — корабль полетит на разведку.\n" +
                                     "<i>Пунктир показывает маршрут и дни в пути.</i>";
                    break;

                case 3:
                    _boxObj.SetActive(true);
                    _titleText.text = "Шаг 3: Пограничный форпост";
                    _descText.text = "Разведчик в пути. Когда система будет изучена, выделите строительный корабль " +
                                     "и ПКМ по ней — он заложит форпост.\n" +
                                     "<i>Каждый форпост стоит 2 гелия-3 в месяц — следите за балансом.</i>";
                    break;

                case 4:
                    _boxObj.SetActive(true);
                    _titleText.text = "Шаг 4: Разведка звёздной системы";
                    _descText.text = "Форпост построен — граница расширилась!\n" +
                                     "Сделайте ДВОЙНОЙ клик по звезде, чтобы войти в систему, " +
                                     "и выберите планету — откроется её обзор с районами.";
                    break;

                case 5:
                    _boxObj.SetActive(true);
                    _titleText.text = "Обучение завершено!";
                    _descText.text = "• Исследования — кнопка в верхней панели\n" +
                                     "• Районы и колонии — в обзоре планеты\n" +
                                     "• Флот: рамка ЛКМ, Shift+ПКМ — очередь приказов\n" +
                                     "<i>Подсказка закроется через 8 секунд.</i>";
                    Invoke(nameof(HideBox), 8f);
                    break;

                default:
                    HideBox();
                    break;
            }
        }

        private void HideBox()
        {
            if (_boxObj != null) LG.Hide(_boxObj);
        }

        private Text _stepText;
        private const int StepCount = 5;

        /// <summary>Закрыть обучение досрочно.</summary>
        public void SkipTutorial()
        {
            _step = 0;
            CancelInvoke(nameof(HideBox));
            HideBox();
        }

        private LiquidGlassEffect _fx;
        private float _stepPulse;
        private float _textFade = 1f;

        private void AnimateStepTransition()
        {
            float dt = Time.unscaledDeltaTime;
            if (_fx != null && _stepPulse > 0f)
            {
                _stepPulse = Mathf.MoveTowards(_stepPulse, 0f, dt * 1.6f);
                _fx.SetPulse(LGEase.OutCubic(_stepPulse));
            }
            if (_textFade < 1f && _descText != null && _titleText != null)
            {
                _textFade = Mathf.MoveTowards(_textFade, 1f, dt * 3.5f);
                float a = LGEase.OutCubic(_textFade);
                var dc = _descText.color; dc.a = a; _descText.color = dc;
                var tc = _titleText.color; tc.a = a; _titleText.color = tc;
            }
        }

        private void Update()
        {
            AnimateStepTransition();

            if (_step <= 0) return;

            // Шаг 1 → 2: нажатие клавиш времени
            if (_step == 1)
            {
                if (Input.GetKeyDown(KeyCode.Space) ||
                    Input.GetKeyDown(KeyCode.Alpha1) ||
                    Input.GetKeyDown(KeyCode.Alpha2) ||
                    Input.GetKeyDown(KeyCode.Alpha3))
                {
                    _step = 2;
                    ShowStep(_step);
                }
                return;
            }

            // Поллинг состояния игры (раз в 0.15 сек)
            _pollTimer -= Time.unscaledDeltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = PollInterval;

            // Шаг 2 → 3: научный корабль получил приказ на разведку
            if (_step == 2)
            {
                if (HasScienceShipOnMission())
                {
                    _step = 3;
                    ShowStep(_step);
                }
            }

            // Шаг 3 → 4: построен аванпост
            if (_step == 3)
            {
                if (HasAnyStarbaseBuilt())
                {
                    _step = 4;
                    ShowStep(_step);
                }
            }
            if (_step == 4)
{
    var svm = SystemViewManager.Instance;
    if (svm != null && svm.IsInSystemView)
    {
        _step = 5;
        ShowStep(_step);
    }
}
        }

        /// <summary>Есть ли научный корабль, которому выдан приказ на разведку.</summary>
        private bool HasScienceShipOnMission()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return false;

            foreach (var f in fm.AllFleets)
            {
                if (f?.Data == null) continue;
                if (f.Data.OwnerId != 0) continue;
                if (f.Data.Type != FleetType.Science) continue;

                // Приказ выдан, корабль летит или уже разведует
                if (f.Data.SurveyTargetSystemId >= 0) return true;
                if (f.Data.State == FleetState.Surveying) return true;
            }
            return false;
        }

        /// <summary>Построен ли хотя бы один аванпост (кроме столицы).</summary>
        private bool HasAnyStarbaseBuilt()
        {
            var gen = Object.FindAnyObjectByType<StellarisClone.Generation.GalaxyGenerator>();
            if (gen == null) return false;

            foreach (var s in gen.Systems)
            {
                if (s.OwnerId == 0 && s.HasStarbase && s.Id != 0)
                    return true;
            }
            return false;
        }

        // === Публичные методы для внешних вызовов (оставлены для совместимости) ===

        public void CompleteSurveyStep()
        {
            if (_step == 2) { _step = 3; ShowStep(_step); }
        }

        public void CompleteBuildStep()
        {
            if (_step == 3) { _step = 4; ShowStep(_step); }
        }

        /// <summary>Принудительно перевести на конкретный шаг (для отладки).</summary>
        public void SetStep(int step)
        {
            _step = step;
            ShowStep(_step);
        }
    }
}