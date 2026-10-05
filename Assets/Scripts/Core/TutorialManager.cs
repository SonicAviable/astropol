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

            var rt = _boxObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(560f, 130f);
            rt.anchoredPosition = new Vector2(-40f, 20f);

            var bgImg = _boxObj.AddComponent<Image>();
            bgImg.color = UIManager.DS.BgDeep;
            bgImg.raycastTarget = false;
            _fx = LG.Glass(_boxObj, 22f);
            _fx.SetRim(new Color(0.45f, 0.95f, 0.90f, 0.5f));
            var motion = LG.Motion(_boxObj, LGAppear.Kind.SlideDown);
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
            tRt.offsetMax = new Vector2(-16, 0);

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

            switch (s)
            {
                case 1:
                    _boxObj.SetActive(true);
                    _titleText.text = "Шаг 1: Галактическое время";
                    _descText.text = "Добро пожаловать, Командующий!\n" +
                                     "Управление временем: ПРОБЕЛ — пауза/пуск, клавиши 1, 2, 3 — скорость.\n" +
                                     "Нажмите любую из этих клавиш, чтобы продолжить.";
                    break;

                case 2:
                    _boxObj.SetActive(true);
                    _titleText.text = "Шаг 2: Сенсорная разведка";
                    _descText.text = "Выберите нейтральную (серую) систему кликом ЛКМ,\n" +
                                     "затем отправьте туда научный корабль (зелёный).\n\n" +
                                     "<i>Научный корабль выделяется ЛКМ, приказ — ПКМ по системе.</i>";
                    break;

                case 3:
                    _boxObj.SetActive(true);
                    _titleText.text = "Шаг 3: Пограничный форпост";
                    _descText.text = "Научный корабль ушёл на разведку.\n\n" +
                                     "Как только система будет изучена, выберите оранжевый строительный корабль\n" +
                                     "и отправьте его в ту же систему — постройте аванпост.";
                    break;

                case 4:
                    _boxObj.SetActive(true);
                    _titleText.text = "Шаг 4: Разведка звёздной системы";
                    _descText.text = "Аванпост построен!\n\n" +
                                     "Сделайте ДВОЙНОЙ клик по любой звезде,\n" +
                                     "чтобы войти в систему и увидеть её планеты.";
                    break;

                case 5:
                    _boxObj.SetActive(true);
                    _titleText.text = "Обучение завершено!";
                    _descText.text = "Основы освоены.\n\n" +
                                     "• Исследуйте технологии (⚛ ИССЛЕДОВАНИЯ в топ-баре)\n" +
                                     "• Расширяйте границы через аванпосты\n" +
                                     "• Проектируйте корабли в ⚙ КОНСТРУКТОРЕ\n\n" +
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