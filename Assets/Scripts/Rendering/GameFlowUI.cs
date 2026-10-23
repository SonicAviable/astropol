using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using StellarisClone.Cam;
using StellarisClone.Core;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Игровой цикл "от начала до конца":
    ///   • меню паузы по Esc (продолжить / цели / управление / новая игра / выход);
    ///   • трекер целей над миникартой: прогресс к трём победам + подсказка «что делать дальше»
    ///     (клик по подсказке сразу ведёт к нужному действию);
    ///   • экран победы/поражения с итогами партии.
    /// Esc закрывает верхнее открытое окно, и только если окон нет — открывает паузу.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class GameFlowUI : MonoBehaviour
    {
        public static GameFlowUI Instance { get; private set; }

        /// <summary>Открыто меню паузы или экран итогов — горячие клавиши времени не работают.</summary>
        public bool BlocksTimeHotkeys => _pauseOpen || (_end != null && LG.IsVisible(_end));

        private enum Page { Main, Goals, Controls, Save, Load, Settings, Achievements }

        private GameObject _pause, _end, _tracker;
        private RectTransform _pauseRt, _pageHost;
        private Text _pauseTitle, _pauseSubtitle;
        private Image _pauseIcon;
        private Page _page = Page.Main;
        private int _speedBeforePause = 1;
        private bool _pauseOpen;
        private Vector2 _pauseSize = new Vector2(520, 756), _pauseSizeV;

        private static readonly Vector2 PauseNarrow = new Vector2(520, 756);
        private static readonly Vector2 PauseWide = new Vector2(1000, 790);

        private Text _restartLabel, _quitLabel, _menuLabel;
        private float _restartArmed, _quitArmed, _menuArmed;

        private Text _endTitle, _endBody, _endStats;
        private Image _endIcon;
        private LiquidGlassEffect _endFx;
        private Button _endContinue;

        private RectTransform _domBar, _sciBar, _survBar;
        private Text _domVal, _sciVal, _survVal;
        private AlertBar _alerts;
        private float _trackerTimer;
        private bool _trackerWanted;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            VictoryManager.OnGameFinished += ShowOutcome;
            UIManager.OnGameStarted += OnGameStarted;
        }

        private void OnDestroy()
        {
            VictoryManager.OnGameFinished -= ShowOutcome;
            UIManager.OnGameStarted -= OnGameStarted;
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            var ui = UIManager.Instance;
            if (ui == null) return;
            BuildPauseMenu(ui.ModalCanvas.transform);
            BuildEndScreen(ui.ModalCanvas.transform);
            // Трекер целей на HUD убран: прогресс к победе — в меню Esc (страница «Цели»), справа — аутлайнер

            // Алерты под верхней панелью (свободный слот науки, простаивающие корабли и т.д.)
            _alerts = gameObject.AddComponent<AlertBar>();
            _alerts.Build(ui.HudCanvas.transform);

            if (UIManager.IsGameStarted) OnGameStarted();
        }

        private void OnGameStarted()
        {
            _trackerWanted = true;
            if (_tracker != null) LG.Show(_tracker);
            _alerts?.SetVisible(true);
        }

        // ================================================================ Esc / цикл

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) HandleEscape();

            if (_restartArmed > 0f) { _restartArmed -= Time.unscaledDeltaTime; if (_restartArmed <= 0f) ResetConfirmLabels(); }
            if (_quitArmed > 0f) { _quitArmed -= Time.unscaledDeltaTime; if (_quitArmed <= 0f) ResetConfirmLabels(); }
            if (_menuArmed > 0f) { _menuArmed -= Time.unscaledDeltaTime; if (_menuArmed <= 0f) ResetConfirmLabels(); }

            // Окно паузы плавно меняет размер под страницу (узкое меню ↔ широкие настройки/сохранения)
            if (_pauseRt != null && _pause.activeInHierarchy)
            {
                var target = _page == Page.Save || _page == Page.Load || _page == Page.Settings || _page == Page.Achievements ? PauseWide : PauseNarrow;
                float dt = Time.unscaledDeltaTime;
                float vx = _pauseSizeV.x, vy = _pauseSizeV.y;
                _pauseSize.x = LGEase.Spring(_pauseSize.x, target.x, ref vx, 3.2f, 0.85f, dt);
                _pauseSize.y = LGEase.Spring(_pauseSize.y, target.y, ref vy, 3.2f, 0.85f, dt);
                _pauseSizeV = new Vector2(vx, vy);
                _pauseRt.sizeDelta = _pauseSize;
            }

            UpdateTracker();
        }

        private void HandleEscape()
        {
            var ui = UIManager.Instance;
            if (ui == null || !UIManager.IsGameStarted || SceneFader.IsBusy || LoadingScreen.IsActive) return;
            var sel = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            if (sel != null && sel.GetComponent<InputField>() != null && sel.GetComponent<InputField>().isFocused) return;

            // Окна, которые сами обрабатывают Esc (закрылось в этом кадре раньше нас — тоже не открываем паузу)
            if (UIEsc.ConsumedThisFrame) return;
            if (ColonyManagerWindow.IsOpenNow || BuildMenuModal.IsOpenNow) return;
            if (VictoryPathWindow.Instance != null && VictoryPathWindow.Instance.IsOpen) return;
            if (LawsWindow.IsOpenNow || HelpOverlay.IsOpenNow) return;
            if (EmpireOverviewWindow.Instance != null && EmpireOverviewWindow.Instance.IsOpen) return;
            if (EspionageWindow.Instance != null && EspionageWindow.Instance.IsOpen) return;
            if (PlanetFocusOverlay.Instance != null && PlanetFocusOverlay.Instance.IsOpen) return;

            if (_end != null && LG.IsVisible(_end)) return;
            if (_pauseOpen) { if (_page != Page.Main) ShowPage(Page.Main); else ClosePause(); return; }

            if (TechConfirmDialog.Instance != null && TechConfirmDialog.Instance.IsOpen) { TechConfirmDialog.Instance.Close(); return; }
            if (TechTreePanel.Instance != null && TechTreePanel.Instance.IsOpen) { TechTreePanel.Instance.Close(); return; }
            if (TradeModal.Instance != null && TradeModal.Instance.IsOpen) { TradeModal.Instance.Close(); return; }
            if (DiplomacyModal.Instance != null && DiplomacyModal.Instance.IsOpen) { DiplomacyModal.Instance.Close(); return; }
            if (ShipDesignerModal.Instance != null && ShipDesignerModal.Instance.IsOpen) { ShipDesignerModal.Instance.Close(); return; }
            if (PlanetOverviewModal.Instance != null && PlanetOverviewModal.Instance.IsOpen) { PlanetOverviewModal.Instance.Close(); return; }
            if (ui.IsShipyardOpen) { ui.CloseShipyard(); return; }
            if (ui.IsBlockingFlowOpen) return;

            // В режиме системы Esc возвращает на карту галактики (меню паузы — следующим нажатием)
            var svm = SystemViewManager.Instance;
            if (svm != null && svm.IsInSystemView) { svm.ExitToGalaxyView(); return; }

            OpenPause();
        }

        // ================================================================ Меню паузы

        private void BuildPauseMenu(Transform host)
        {
            var rt = LGBuild.Rect(host, "PauseMenu");
            rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, PauseNarrow);
            _pauseRt = rt;
            _pause = rt.gameObject;
            var bg = _pause.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            LG.Glass(_pause, 26f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.5f));
            LG.Motion(_pause, LGAppear.Kind.Pop).inDuration = 0.42f;

            var header = LGBuild.Panel(rt, "Header", UIManager.DS.BgHeader);
            header.rectTransform.TopBand(0, 86);
            LG.Header(header.gameObject);
            _pauseIcon = LGIcons.Create(header.transform, LGIcon.Pause, 26, UIManager.DS.NeonCyan);
            _pauseIcon.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(26, 26));
            var title = LGBuild.Label(header.transform, Loc.T("ПАУЗА"), 22, UIManager.DS.TextPrimary, TextAnchor.UpperCenter, bold: true);
            title.rectTransform.Stretch(0, 0, 0, 44);
            _pauseTitle = title;
            _pauseSubtitle = LGBuild.Label(header.transform, "", 11, UIManager.DS.TextMuted, TextAnchor.LowerCenter);
            _pauseSubtitle.rectTransform.Stretch(0, 8, 0, 0);

            _pageHost = LGBuild.Rect(rt, "Page");
            _pageHost.Stretch(30, 26, 30, 106);

            LG.Skin(_pause.transform);
            _pause.SetActive(false);
        }

        public void OpenPause()
        {
            if (_pause == null || _pauseOpen) return;
            _pauseOpen = true;
            var tm = TimeManager.Instance;
            if (tm != null)
            {
                _speedBeforePause = tm.CurrentSpeed == 0 ? 1 : tm.CurrentSpeed;
                tm.SetSpeed(0);
            }
            UIManager.Instance?.ShowModalDimPublic();
            MapModeController.HideGlobal();
            ShowPage(Page.Main);
            _pauseSize = PauseNarrow;
            _pauseSizeV = Vector2.zero;
            _pauseRt.sizeDelta = _pauseSize;
            LG.Show(_pause);
            _pause.transform.SetAsLastSibling();
        }

        public void ClosePause(bool resume = true)
        {
            if (!_pauseOpen) return;
            _pauseOpen = false;
            LG.Hide(_pause);
            UIManager.Instance?.HideModalDimPublic();
            MapModeController.ShowGlobal();
            if (resume && TimeManager.Instance != null) TimeManager.Instance.SetSpeed(_speedBeforePause);
        }

        private void ShowPage(Page page)
        {
            _page = page;
            LGBuild.Clear(_pageHost);
            _restartArmed = _quitArmed = _menuArmed = 0f;
            string date = TimeManager.Instance != null ? TimeManager.Instance.GetFormattedDate() : "";
            string empire = UIManager.Instance?.SelectedFaction?.Name ?? Loc.T("Империя");
            _pauseSubtitle.text = $"{empire}   ·   {date}";

            (LGIcon icon, string title) head = page switch
            {
                Page.Save => (LGIcon.Save, Loc.T("СОХРАНЕНИЕ")),
                Page.Load => (LGIcon.Load, Loc.T("ЗАГРУЗКА")),
                Page.Settings => (LGIcon.Gear, Loc.T("НАСТРОЙКИ")),
                Page.Goals => (LGIcon.Trophy, Loc.T("ЦЕЛИ")),
                Page.Achievements => (LGIcon.Star, Loc.T("ДОСТИЖЕНИЯ")),
                Page.Controls => (LGIcon.Menu, Loc.T("УПРАВЛЕНИЕ")),
                _ => (LGIcon.Pause, Loc.T("ПАУЗА"))
            };
            _pauseIcon.sprite = LGIcons.Get(head.icon);
            _pauseTitle.text = head.title;

            switch (page)
            {
                case Page.Main: BuildMainPage(); break;
                case Page.Goals: BuildGoalsPage(); break;
                case Page.Achievements: BuildAchievementsPage(); break;
                case Page.Controls: BuildControlsPage(); break;
                case Page.Save: BuildSaveLoadPage(SaveLoadPanel.Mode.Save); break;
                case Page.Load: BuildSaveLoadPage(SaveLoadPanel.Mode.Load); break;
                case Page.Settings: BuildSettingsPage(); break;
            }
            LG.Skin(_pageHost);
        }

        private void BuildMainPage()
        {
            float y = 0f;
            MenuButton(ref y, LGIcon.Play, Loc.T("ПРОДОЛЖИТЬ"), UIManager.DS.BtnPrimary, UIManager.DS.NeonCyan, () => ClosePause());
            var save = MenuButton(ref y, LGIcon.Save, Loc.T("СОХРАНИТЬ ИГРУ"), UIManager.DS.BtnNeutral, UIManager.DS.Green, () => ShowPage(Page.Save));
            if (!SaveSystem.CanSaveNow) save.GetComponentInParent<Button>().interactable = false;
            MenuButton(ref y, LGIcon.Load, Loc.T("ЗАГРУЗИТЬ"), UIManager.DS.BtnNeutral, new Color(0.55f, 0.8f, 1f), () => ShowPage(Page.Load));
            MenuButton(ref y, LGIcon.Gear, Loc.T("НАСТРОЙКИ"), UIManager.DS.BtnNeutral, new Color(0.7f, 0.85f, 0.95f), () => ShowPage(Page.Settings));
            MenuButton(ref y, LGIcon.Trophy, Loc.T("ЦЕЛИ И УСЛОВИЯ ПОБЕДЫ"), UIManager.DS.BtnNeutral, UIManager.DS.Gold, () => ShowPage(Page.Goals));
            MenuButton(ref y, LGIcon.Star, Loc.F("ДОСТИЖЕНИЯ  ·  {0}/{1}", Achievements.UnlockedCount(), Achievements.Total), UIManager.DS.BtnNeutral, new Color(1f, 0.84f, 0.40f), () => ShowPage(Page.Achievements));
            MenuButton(ref y, LGIcon.Menu, Loc.T("УПРАВЛЕНИЕ"), UIManager.DS.BtnNeutral, new Color(0.7f, 0.85f, 0.95f), () => ShowPage(Page.Controls));
            y += 12f;
            _restartLabel = MenuButton(ref y, LGIcon.Globe, Loc.T("НАЧАТЬ ЗАНОВО"), new Color(0.30f, 0.24f, 0.08f), UIManager.DS.Gold, () =>
            {
                if (_restartArmed > 0f) { RestartGame(); return; }
                _restartArmed = 3f;
                _restartLabel.text = Loc.T("ЕЩЁ РАЗ — ПРОГРЕСС БУДЕТ ПОТЕРЯН");
            });
            _menuLabel = MenuButton(ref y, LGIcon.Back, Loc.T("В ГЛАВНОЕ МЕНЮ"), new Color(0.12f, 0.16f, 0.24f), new Color(0.6f, 0.75f, 1f), () =>
            {
                if (_menuArmed > 0f) { GameSession.ReturnToMainMenu(); return; }
                _menuArmed = 3f;
                _menuLabel.text = Loc.T("ЕЩЁ РАЗ — НЕСОХРАНЁННОЕ ПРОПАДЁТ");
            });
            _quitLabel = MenuButton(ref y, LGIcon.Power, Loc.T("ВЫЙТИ ИЗ ИГРЫ"), UIManager.DS.BtnDanger, UIManager.DS.Red, () =>
            {
                if (_quitArmed > 0f) { QuitGame(); return; }
                _quitArmed = 3f;
                _quitLabel.text = Loc.T("НАЖМИТЕ ЕЩЁ РАЗ ДЛЯ ВЫХОДА");
            });

            var hint = LGBuild.Label(_pageHost, Loc.T("Esc — вернуться в игру   ·   F5 — быстрое сохранение"), 10, UIManager.DS.TextMuted, TextAnchor.LowerCenter);
            hint.rectTransform.Stretch(0, 0, 0, 0);
        }

        private void BuildAchievementsPage()
        {
            var defs = Achievements.All();
            int got = 0;
            foreach (var d in defs) if (Achievements.IsUnlocked(d.Id)) got++;
            var gold = new Color(1f, 0.84f, 0.40f);

            var head = LGBuild.Label(_pageHost, Loc.F("Получено {0} из {1}", got, defs.Count), 15, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, bold: true);
            head.rectTransform.TopBand(0, 24);
            string sync = VkPlayStats.Ready
                ? (Achievements.HasPending() ? Loc.T("VK Play: отправляем на сервер…") : Loc.T("VK Play: всё синхронизировано"))
                : Loc.T("Хранятся на этом компьютере — VK Play не подключён");
            var st = LGBuild.Label(_pageHost, sync, 11, UIManager.DS.TextMuted, TextAnchor.MiddleRight);
            st.rectTransform.TopBand(0, 24);
            var bar = LGBuild.Rect(_pageHost, "Bar");
            bar.TopBand(30, 8);
            LGBuild.Bar(bar, gold, got / (float)Mathf.Max(1, defs.Count), 6f);

            var area = LGBuild.Rect(_pageHost, "Area");
            area.Stretch(0, 62, 0, 48);
            var list = LGBuild.ScrollList(area, 6f, 2);
            foreach (var d in defs)
            {
                bool on = Achievements.IsUnlocked(d.Id);
                var card = LGBuild.Panel(list, "Ach", on ? new Color(0.10f, 0.085f, 0.035f, 0.92f) : new Color(0.025f, 0.045f, 0.060f, 0.85f));
                LGBuild.Height(card.gameObject, 58f);
                LG.Platter(card.gameObject, 10f).SetRim(on ? new Color(gold.r, gold.g, gold.b, 0.45f) : new Color(1f, 1f, 1f, 0.08f));
                var ic = LGIcons.Create(card.transform, on ? d.Icon : LGIcon.Lock, 24, on ? gold : new Color(0.55f, 0.62f, 0.66f, 0.7f));
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 0), new Vector2(24, 24));
                var name = LGBuild.Label(card.transform, d.Name, 14, on ? UIManager.DS.TextPrimary : new Color(0.75f, 0.80f, 0.84f), TextAnchor.UpperLeft, bold: true);
                name.rectTransform.Stretch(60, 0, 170, 9);
                string desc = !on && d.Hidden ? Loc.T("Условие откроется после получения") : d.Desc;
                var ds = LGBuild.Label(card.transform, desc, 11, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
                ds.rectTransform.Stretch(60, 0, 170, 30);
                var when = Achievements.UnlockedAt(d.Id);
                string right = on && when.HasValue ? when.Value.ToLocalTime().ToString("dd.MM.yyyy") : Loc.T("не получено");
                var rt = LGBuild.Label(card.transform, right, 11, on ? gold : new Color(0.55f, 0.62f, 0.66f, 0.8f), TextAnchor.MiddleRight, bold: on);
                rt.rectTransform.Stretch(0, 0, 18, 0);
            }
            BackButton();
        }

        private void BuildSaveLoadPage(SaveLoadPanel.Mode mode)
        {
            var area = LGBuild.Rect(_pageHost, "Area");
            area.Stretch(0, 62, 0, 0);
            SaveLoadPanel.Build(area, mode, confirmLoad: true);
            BackButton();
        }

        private void BuildSettingsPage()
        {
            var area = LGBuild.Rect(_pageHost, "Area");
            area.Stretch(0, 62, 0, 0);
            SettingsPanel.Build(area);
            BackButton();
        }

        private void ResetConfirmLabels()
        {
            if (_restartLabel != null && _restartArmed <= 0f) _restartLabel.text = Loc.T("НАЧАТЬ ЗАНОВО");
            if (_menuLabel != null && _menuArmed <= 0f) _menuLabel.text = Loc.T("В ГЛАВНОЕ МЕНЮ");
            if (_quitLabel != null && _quitArmed <= 0f) _quitLabel.text = Loc.T("ВЫЙТИ ИЗ ИГРЫ");
        }

        private Text MenuButton(ref float y, LGIcon icon, string label, Color tint, Color rim, System.Action onClick)
        {
            var b = LGBuild.Button(_pageHost, "Btn", tint, new Color(rim.r, rim.g, rim.b, 0.7f), onClick, icon, label, 13);
            ((RectTransform)b.transform).TopBand(y, 46);
            y += 54f;
            return b.GetComponentInChildren<Text>();
        }

        private void BuildGoalsPage()
        {
            var vm = VictoryManager.Instance;
            float y = 0f;
            if (vm != null)
            {
                GoalRow(ref y, LGIcon.Starbase, Loc.T("Доминирование"),
                        Loc.F("Постройте форпосты в {0} системах и удерживайте порог {1} мес. У лучшего соперника: {2}.", vm.DominationRequired, VictoryManager.DominationHoldMonths, vm.RivalDominationProgress),
                        vm.DominationProgress, vm.DominationRequired, UIManager.DS.NeonCyan);
                GoalRow(ref y, LGIcon.Research, Loc.T("Научная победа"),
                        Loc.F("Изучите {0} технологий и завершите проект «Вознесение». У лучшего соперника: {1}.", vm.ScienceRequired, vm.RivalScienceProgress),
                        vm.ScienceProgress, vm.ScienceRequired, UIManager.DS.Green);
                int me = vm.PlayerScore, rival = vm.RivalScore;
                GoalRow(ref y, LGIcon.Trophy, Loc.F("Очки к {0} году", VictoryManager.EndYear),
                        Loc.F("Осталось {0} {1}. Ваш счёт {2}, у лучшего соперника {3}. ", vm.YearsLeft, VictoryManager.YearsWord(vm.YearsLeft), me, rival) +
                        Loc.F("Очки: система {0}, житель {1}, технология {2}, флот — 1 за {3} мощи.", EmpireStats.ScorePerSystem, EmpireStats.ScorePerPop, EmpireStats.ScorePerTech, Mathf.RoundToInt(1f / EmpireStats.ScorePerPower)),
                        me, Mathf.Max(1, me + rival), UIManager.DS.Gold, $"{me} : {rival}", 112f);
            }

            var lose = LGBuild.Panel(_pageHost, "Lose", new Color(0.30f, 0.06f, 0.08f, 0.9f));
            lose.rectTransform.TopBand(y + 4, 84);
            LG.Platter(lose.gameObject, 16f).SetRim(new Color(1f, 0.4f, 0.42f, 0.45f));
            var wi = LGIcons.Create(lose.transform, LGIcon.Warning, 20, UIManager.DS.Red);
            wi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(20, 20));
            var lt = LGBuild.Label(lose.transform,
                Loc.T("<b>Поражение</b>\nпотеря всех систем, ") + (vm != null ? vm.BankruptcyLimit : 6) + Loc.T(" мес. банкротства, ") +
                Loc.T("любой из соперников добился доминирования, технологической или экономической победы, или у кого-то из них больше очков в ") + VictoryManager.EndYear + Loc.T(" году."),
                11, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, wrap: true);
            lt.rectTransform.Stretch(48, 6, 14, 6);

            BackButton();
        }

        private void GoalRow(ref float y, LGIcon icon, string title, string desc, int cur, int req, Color col, string valueText = null, float height = 84f)
        {
            var row = LGBuild.Panel(_pageHost, "Goal", UIManager.DS.BgSlot);
            row.rectTransform.TopBand(y, height);
            LG.Platter(row.gameObject, 16f).SetRim(new Color(col.r, col.g, col.b, 0.3f));
            var ic = LGIcons.Create(row.transform, icon, 26, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 6), new Vector2(26, 26));
            var body = LGBuild.Rect(row.transform, "Body");
            body.Stretch(56, 10, 16, 10);
            LGBuild.Label(body, $"<b>{title}</b>", 14, UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            LGBuild.Label(body, valueText ?? $"{Mathf.Min(cur, req)} / {req}", 14, col, TextAnchor.UpperRight, bold: true);
            var d = LGBuild.Label(body, desc, 11, UIManager.DS.TextMuted, TextAnchor.UpperLeft, wrap: true);
            d.rectTransform.offsetMax = new Vector2(0, -22);
            var barHost = LGBuild.Rect(body, "Bar");
            barHost.anchorMin = new Vector2(0, 0);
            barHost.anchorMax = new Vector2(1, 0);
            barHost.offsetMin = new Vector2(0, 2);
            barHost.offsetMax = new Vector2(0, 12);
            LGBuild.Bar(barHost, col, cur / (float)Mathf.Max(1, req), 7f);
            y += height + 10f;
        }

        private void BuildControlsPage()
        {
            var sb = new StringBuilder();
            void Line(string key, string act) => sb.Append($"<color=#4DF2DB><b>{key}</b></color>   {act}\n");
            Line(Loc.T("ЛКМ"), Loc.T("выбрать систему, флот или кнопку"));
            Line(Loc.T("ПКМ по системе"), Loc.T("приказ выбранному флоту лететь туда"));
            Line(Loc.T("Двойной клик по звезде"), Loc.T("войти в звёздную систему"));
            Line(Loc.T("WASD / стрелки / край экрана"), Loc.T("перемещение камеры (Shift — быстрее)"));
            Line(Loc.T("Колесо мыши"), Loc.T("приближение к курсору"));
            Line(Loc.T("Средняя кнопка (зажать)"), Loc.T("перетаскивание карты"));
            Line("Q / E", Loc.T("поворот камеры"));
            Line("Home", Loc.T("камера к столице"));
            Line(Loc.T("ПРОБЕЛ"), Loc.T("пауза / продолжить"));
            Line("1 / 2 / 3", Loc.T("скорость времени"));
            Line("I · C · L · T · P · K · N · V", Loc.T("главные окна (клавиша подписана в левом меню); G — все военные флоты"));
            Line("F1 / H", Loc.T("справка: все клавиши и где что находится; M — музыка"));
            Line("B / Tab", Loc.T("на экране планеты: стройка / следующая колония"));
            Line(Loc.T("ПКМ по подсказке в окне"), Loc.T("закрепить всплывающую подсказку"));
            Line(Loc.T("Shift+ЛКМ / рамка ЛКМ"), Loc.T("выделить несколько флотов"));
            Line(Loc.T("Shift+ПКМ по системе"), Loc.T("добавить пункт в очередь приказов (Backspace — очистить)"));
            Line(Loc.T("F / двойной клик по флоту"), Loc.T("камера к выделенному флоту"));
            Line("F5 / F9", Loc.T("быстрое сохранение / загрузка"));
            Line("Esc", Loc.T("закрыть окно / меню паузы"));

            var panel = LGBuild.Panel(_pageHost, "Controls", UIManager.DS.BgSlot);
            panel.rectTransform.Stretch(0, 62, 0, 0);
            LG.Platter(panel.gameObject, 16f);
            var t = LGBuild.Label(panel.transform, sb.ToString(), 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            t.rectTransform.Stretch(18, 14, 18, 16);
            t.lineSpacing = 1.35f;

            BackButton();
        }

        private void BackButton()
        {
            var b = LGBuild.Button(_pageHost, "Back", UIManager.DS.BtnNeutral, new Color(0.7f, 0.85f, 0.95f, 0.5f),
                () => ShowPage(Page.Main), LGIcon.Back, Loc.T("НАЗАД"), 12);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(80, 0);
            rt.offsetMax = new Vector2(-80, 46);
        }

        // ================================================================ Экран итогов

        private void BuildEndScreen(Transform host)
        {
            var rt = LGBuild.Rect(host, "EndScreen");
            rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(660, 560));
            _end = rt.gameObject;
            var bg = _end.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            _endFx = LG.Glass(_end, 28f);
            var m = LG.Motion(_end, LGAppear.Kind.Pop);
            m.inDuration = 0.7f;
            m.fromScale = 0.95f;

            var iconBg = LGBuild.Panel(rt, "IconBg", new Color(0.2f, 0.16f, 0.05f));
            iconBg.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(96, 96));
            var ibFx = LG.Platter(iconBg.gameObject, 48f);
            ibFx.FillMultiplier = 2.5f;
            ibFx.GlowMultiplier = 4f;
            _endIcon = LGIcons.Create(iconBg.transform, LGIcon.Trophy, 54, UIManager.DS.Gold);

            _endTitle = LGBuild.Label(rt, "", 28, UIManager.DS.TextPrimary, TextAnchor.UpperCenter, bold: true);
            _endTitle.rectTransform.Stretch(30, 0, 30, 150);
            _endBody = LGBuild.Label(rt, "", 14, UIManager.DS.TextMuted, TextAnchor.UpperCenter, wrap: true);
            _endBody.rectTransform.Stretch(50, 0, 50, 196);

            var stats = LGBuild.Panel(rt, "Stats", UIManager.DS.BgSlot);
            stats.rectTransform.Stretch(50, 100, 50, 250);
            LG.Platter(stats.gameObject, 18f);
            _endStats = LGBuild.Label(stats.transform, "", 13, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            _endStats.rectTransform.Stretch(22, 14, 22, 16);
            _endStats.lineSpacing = 1.35f;

            var row = LGBuild.Rect(rt, "Buttons");
            row.anchorMin = new Vector2(0, 0);
            row.anchorMax = new Vector2(1, 0);
            row.pivot = new Vector2(0.5f, 0);
            row.offsetMin = new Vector2(40, 32);
            row.offsetMax = new Vector2(-40, 82);

            _endContinue = LGBuild.Button(row, "Continue", UIManager.DS.BtnPrimary, UIManager.DS.NeonCyan, () =>
            {
                LG.Hide(_end);
                UIManager.Instance?.HideModalDimPublic();
                MapModeController.ShowGlobal();
            }, LGIcon.Play, Loc.T("ПРОДОЛЖИТЬ"), 12);
            ((RectTransform)_endContinue.transform).Column(0f, 0.33f, 0, 5);
            var again = LGBuild.Button(row, "Again", new Color(0.30f, 0.24f, 0.08f), UIManager.DS.Gold, RestartGame, LGIcon.Globe, Loc.T("НОВАЯ ИГРА"), 12);
            ((RectTransform)again.transform).Column(0.33f, 0.67f, 5, 5);
            var menu = LGBuild.Button(row, "Menu", new Color(0.12f, 0.16f, 0.24f), new Color(0.6f, 0.75f, 1f),
                                      GameSession.ReturnToMainMenu, LGIcon.Back, Loc.T("ГЛАВНОЕ МЕНЮ"), 12);
            ((RectTransform)menu.transform).Column(0.67f, 1f, 5, 0);

            LG.Skin(_end.transform);
            _end.SetActive(false);
        }

        private void ShowOutcome(VictoryManager.Outcome outcome, string title, string body)
        {
            if (_end == null) return;
            if (_pauseOpen) ClosePause(resume: false);

            bool victory = VictoryManager.IsVictory(outcome);
            Color col = victory ? UIManager.DS.Gold : UIManager.DS.Red;

            _endFx.SetRim(new Color(col.r, col.g, col.b, 0.75f));
            _endIcon.sprite = LGIcons.Get(victory ? LGIcon.Trophy : LGIcon.Warning);
            _endIcon.color = col;
            var ib = _endIcon.transform.parent.GetComponent<Image>();
            if (ib != null) ib.color = new Color(col.r * 0.25f, col.g * 0.25f, col.b * 0.25f, 1f);
            ib?.GetComponent<LiquidGlassEffect>()?.SetRim(new Color(col.r, col.g, col.b, 0.9f));

            _endTitle.text = victory ? Loc.T("ПОБЕДА") : Loc.T("ПОРАЖЕНИЕ");
            _endTitle.color = col;
            _endBody.text = $"{title.Replace("★ ", "").Replace("✕ ", "")}\n{body}";
            _endStats.text = BuildSummary();
            _endContinue.gameObject.SetActive(victory);

            UIManager.Instance?.ShowModalDimPublic();
            MapModeController.HideGlobal();
            LG.Show(_end);
            _end.transform.SetAsLastSibling();
        }

        private string BuildSummary()
        {
            var gen = FindAnyObjectByType<GalaxyGenerator>();
            int systems = 0, colonies = 0, pop = 0;
            if (gen != null)
                foreach (var s in gen.Systems)
                {
                    if (s.OwnerId != 0) continue;
                    systems++;
                    foreach (var p in s.Planets) if (p.Population > 0) { colonies++; pop += p.Population; }
                }
            var vm = VictoryManager.Instance;
            int power = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(0) : 0;
            string date = TimeManager.Instance != null ? TimeManager.Instance.GetFormattedDate() : "";
            string empire = UIManager.Instance?.SelectedFaction?.Name ?? Loc.T("Империя");
            return
                Loc.F("<color=#8AA2A8>Империя:</color>  <b>{0}</b>\n", empire) +
                Loc.F("<color=#8AA2A8>Дата:</color>  {0}   ·   <color=#8AA2A8>лет у власти:</color> {1}\n", date, (vm != null ? vm.YearsElapsed : 0)) +
                Loc.F("<color=#8AA2A8>Систем:</color>  {0}   ·   <color=#8AA2A8>колоний:</color> {1}   ·   <color=#8AA2A8>население:</color> {2}\n", systems, colonies, pop) +
                Loc.F("<color=#8AA2A8>Технологий изучено:</color>  {0}   ·   <color=#8AA2A8>мощь флота:</color> {1:N0}\n", (vm != null ? vm.ScienceProgress : 0), power) +
                Loc.F("<color=#8AA2A8>Очки:</color>  <b>{0}</b>   ·   <color=#8AA2A8>у лучшего соперника:</color> {1}", (vm != null ? vm.PlayerScore : 0), (vm != null ? vm.RivalScore : 0));
        }

        // ================================================================ Трекер целей

        private void BuildTracker(Transform hud)
        {
            var rt = LGBuild.Rect(hud, "[UI] Objectives");
            rt.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-22, 274), new Vector2(240, 118));
            _tracker = rt.gameObject;
            var bg = _tracker.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            var btn = _tracker.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { OpenPause(); ShowPage(Page.Goals); });
            LG.Glass(_tracker, 18f).SetRim(new Color(1f, 0.85f, 0.5f, 0.22f));
            TooltipHelper.Attach(_tracker, Loc.T("<b>Цели империи</b>\nПрогресс к победе.\n\n<color=#8AA2A8>ЛКМ — подробнее</color>"));
            var mo = LG.Motion(_tracker, LGAppear.Kind.SlideRight);
            mo.distance = 30f;
            mo.delay = 0.2f;

            var head = LGBuild.Rect(rt, "Head");
            head.TopBand(10, 16, 14, 14);
            var hi = LGIcons.Create(head, LGIcon.Trophy, 13, UIManager.DS.Gold);
            hi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(13, 13));
            var ht = LGBuild.Label(head, Loc.T("ЦЕЛИ ИМПЕРИИ"), 10, UIManager.DS.Gold, TextAnchor.MiddleLeft, bold: true);
            ht.rectTransform.offsetMin = new Vector2(19, 0);
            LGBuild.Label(head, Loc.T("Esc — меню"), 9, UIManager.DS.TextMuted, TextAnchor.MiddleRight);

            _domVal = TrackerRow(rt, 34, LGIcon.Starbase, Loc.T("Доминирование"), UIManager.DS.NeonCyan, out _domBar);
            _sciVal = TrackerRow(rt, 60, LGIcon.Research, Loc.T("Наука"), UIManager.DS.Green, out _sciBar);
            _survVal = TrackerRow(rt, 86, LGIcon.Trophy, Loc.F("Очки · {0}", VictoryManager.EndYear), UIManager.DS.Gold, out _survBar);

            LG.Skin(_tracker.transform);
            _tracker.SetActive(false);
        }

        private Text TrackerRow(RectTransform parent, float top, LGIcon icon, string label, Color col, out RectTransform bar)
        {
            var row = LGBuild.Rect(parent, "Row_" + label);
            row.TopBand(top, 22, 14, 14);
            var ic = LGIcons.Create(row, icon, 13, col);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(13, 13));
            var l = LGBuild.Label(row, label, 10, UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            l.rectTransform.offsetMin = new Vector2(19, 0);
            l.rectTransform.offsetMax = new Vector2(0, 1);
            var v = LGBuild.Label(row, "", 10, col, TextAnchor.UpperRight, bold: true);
            v.rectTransform.offsetMax = new Vector2(0, 1);
            var host = LGBuild.Rect(row, "Bar");
            host.anchorMin = new Vector2(0, 0);
            host.anchorMax = new Vector2(1, 0);
            host.offsetMin = new Vector2(19, 0);
            host.offsetMax = new Vector2(0, 5);
            bar = LGBuild.Bar(host, col, 0f, 4f);
            return v;
        }

        private void UpdateTracker()
        {
            if (_tracker == null || !_trackerWanted) return;

            bool fullscreen = PlanetFocusOverlay.Instance != null && PlanetFocusOverlay.Instance.IsOpen;
            if (fullscreen && LG.IsVisible(_tracker)) LG.Hide(_tracker);
            else if (!fullscreen && !_tracker.activeSelf) LG.Show(_tracker);
            if (!_tracker.activeInHierarchy) return;

            _trackerTimer -= Time.unscaledDeltaTime;
            if (_trackerTimer > 0f) return;
            _trackerTimer = 0.75f;

            var vm = VictoryManager.Instance;
            if (vm == null) return;
            int d = vm.DominationProgress, s = vm.ScienceProgress;
            int me = vm.PlayerScore, rival = vm.RivalScore;
            _domVal.text = $"{d} / {vm.DominationRequired}";
            _sciVal.text = $"{s} / {vm.ScienceRequired}";
            _survVal.text = me >= rival ? $"{me} : {rival}" : $"<color=#FF6A6A>{me} : {rival}</color>";
            LGBuild.SetBar(_domBar, d / (float)Mathf.Max(1, vm.DominationRequired));
            LGBuild.SetBar(_sciBar, s / (float)Mathf.Max(1, vm.ScienceRequired));
            LGBuild.SetBar(_survBar, me / (float)Mathf.Max(1, me + rival));

            var tip = _tracker.GetComponent<TooltipTrigger>();
            if (tip != null)
                tip.SetText(
                    Loc.T("<b>Цели империи</b>\n") +
                    Loc.F("Доминирование: вы {0}, соперник {1} из {2} систем\n", d, vm.RivalDominationProgress, vm.DominationRequired) +
                    Loc.F("Наука: вы {0}, соперник {1} из {2} технологий\n", s, vm.RivalScienceProgress, vm.ScienceRequired) +
                    Loc.F("Очки: {0} против {1}; подсчёт через {2} {3} ({4})\n\n", me, rival, vm.YearsLeft, VictoryManager.YearsWord(vm.YearsLeft), VictoryManager.EndYear) +
                    Loc.T("<color=#8AA2A8>Если любой из соперников первым достигнет цели или наберёт больше очков — поражение.\nЛКМ — подробнее</color>"));
        }

        // ================================================================ Перезапуск / выход

        /// <summary>Новая партия с теми же параметрами галактики (затемнение + перезагрузка сцены).</summary>
        public static void RestartGame() => GameSession.RestartSameSettings();

        public static void QuitGame() => SceneFader.FadeOutThen(GameSession.Quit, 0.4f);
    }
}
