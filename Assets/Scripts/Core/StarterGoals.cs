using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using StellarisClone.Generation;
using StellarisClone.Cam;

namespace StellarisClone.Core
{
    /// <summary>
    /// Стартовые цели на первые полчаса: после обучения у игрока есть понятная лестница задач
    /// (разведка → форпосты → наука → колония → флот → соседи → закон → свой корабль).
    ///   • Прогресс считается по состоянию империи, поэтому переживает сохранение и загрузку без отдельных данных.
    ///   • Награда — только за цель, выполненную на глазах у игрока: после загрузки уже выполненные цели
    ///     засчитываются молча, повторно ничего не выдаётся.
    ///   • Показываются первыми тремя невыполненными в «Планировщике» справа; пока идёт обучение — скрыты.
    /// </summary>
    public static class StarterGoals
    {
        public sealed class Goal
        {
            public string Id;
            public LGIcon Icon;
            public string Title, Hint, RewardText;
            public int Target;
            public Func<int> Current;
            public Action Reward;
            public Action Show;
            public bool Done;
        }

        private static List<Goal> s_goals;
        private static bool s_primed;
        private static bool s_designed;
        private static GalaxyGenerator s_gen;

        public const int VisibleCount = 3;

        /// <summary>Партия началась или загружена: всё, что уже выполнено, засчитать без наград.</summary>
        public static void Reset()
        {
            s_goals = null;
            s_primed = false;
            s_designed = false;
            s_gen = null;
        }

        /// <summary>Игрок сохранил свой проект корабля (вызывает конструктор).</summary>
        public static void MarkDesigned() => s_designed = true;

        public static IReadOnlyList<Goal> Goals
        {
            get { if (s_goals == null) s_goals = Build(); return s_goals; }
        }

        public static int DoneCount
        {
            get { int n = 0; foreach (var g in Goals) if (g.Done) n++; return n; }
        }

        public static bool AllDone => DoneCount >= Goals.Count;

        /// <summary>Показывать ли цели сейчас (партия идёт, обучение не на экране, не всё выполнено).</summary>
        public static bool Visible =>
            UIManager.IsGameStarted && s_primed && !AllDone
            && (TutorialManager.Instance == null || !TutorialManager.Instance.IsActive);

        /// <summary>Первые невыполненные цели — их и видит игрок.</summary>
        public static List<Goal> Active()
        {
            var l = new List<Goal>(VisibleCount);
            foreach (var g in Goals)
            {
                if (g.Done) continue;
                l.Add(g);
                if (l.Count >= VisibleCount) break;
            }
            return l;
        }

        /// <summary>Пересчитать прогресс. Возвращает цели, выполненные с прошлой проверки (до первой проверки — пусто).</summary>
        public static List<Goal> Evaluate()
        {
            var fresh = new List<Goal>();
            foreach (var g in Goals)
            {
                if (g.Done) continue;
                int cur = SafeCurrent(g);
                if (cur < g.Target) continue;
                g.Done = true;
                if (s_primed) fresh.Add(g);
            }
            s_primed = true;
            return fresh;
        }

        public static int SafeCurrent(Goal g)
        {
            try { return Mathf.Max(0, g.Current()); }
            catch (Exception) { return 0; }
        }

        // ================================================================ Список целей

        private static List<Goal> Build()
        {
            var l = new List<Goal>();
            l.Add(new Goal
            {
                Id = "survey", Icon = LGIcon.Sensors, Target = 5,
                Title = Loc.T("Разведайте 5 систем"),
                Hint = Loc.T("Научный корабль: ПКМ по серой системе. Кнопка авторазведки на панели флота ищет цели сама."),
                Current = SurveyedCount,
                RewardText = Loc.T("+50 гелия-3"), Reward = () => Give(energy: 50f),
                Show = () => SelectFirst(FleetType.Science)
            });
            l.Add(new Goal
            {
                Id = "outposts", Icon = LGIcon.Starbase, Target = 2,
                Title = Loc.T("Постройте 2 форпоста"),
                Hint = Loc.T("Строительный корабль: ПКМ по изученной системе рядом с границей. Форпост стоит титан и 2 гелия-3 в месяц."),
                Current = () => EmpireStats.SystemCount(0) - 1,
                RewardText = Loc.T("+75 титана"), Reward = () => Give(minerals: 75f),
                Show = () => SelectFirst(FleetType.Constructor)
            });
            l.Add(new Goal
            {
                Id = "research", Icon = LGIcon.Research, Target = 2,
                Title = Loc.T("Завершите 2 исследования"),
                Hint = Loc.T("Технологии — клавиша T. В каждом слоте выберите направление: физика, общество или инженерия."),
                Current = () => TechnologyManager.Instance != null ? TechnologyManager.Instance.ResearchedCount : 0,
                RewardText = Loc.T("+50 гелия-3"), Reward = () => Give(energy: 50f),
                Show = () => UIManager.Instance?.OpenNavWindow(UIManager.NavWindow.Tech)
            });
            l.Add(new Goal
            {
                Id = "colony", Icon = LGIcon.Planet, Target = 1,
                Title = Loc.T("Основайте вторую колонию"),
                Hint = Loc.T("Войдите в свою систему с пригодной планетой (двойной клик по звезде) и нажмите «Основать колонию»."),
                Current = () => EmpireStats.Colonies(0) - 1,
                RewardText = Loc.T("+100 титана"), Reward = () => Give(minerals: 100f),
                Show = ShowColonyTarget
            });
            l.Add(new Goal
            {
                Id = "fleet", Icon = LGIcon.Fleet, Target = 3,
                Title = Loc.T("Соберите 3 военных корабля"),
                Hint = Loc.T("Корабли строятся на верфи столицы. Без флота соседи сочтут вас лёгкой добычей."),
                Current = () => MilitaryShips(),
                RewardText = Loc.T("+50 сплавов"), Reward = () => Give(alloys: 50f),
                Show = () => ShipyardWindow.Instance?.Open()
            });
            l.Add(new Goal
            {
                Id = "contact", Icon = LGIcon.Diplomacy, Target = 1,
                Title = Loc.T("Встретьте соседнюю цивилизацию"),
                Hint = Loc.T("Продолжайте разведку: соседи откроются, когда ваши корабли или границы их найдут."),
                Current = MetCount,
                RewardText = Loc.T("+25 влияния"), Reward = () => Give(influence: 25f),
                Show = () => SelectFirst(FleetType.Science)
            });
            l.Add(new Goal
            {
                Id = "law", Icon = LGIcon.Influence, Target = 1,
                Title = Loc.T("Примите закон империи"),
                Hint = Loc.F("Законы — клавиша L. Смена закона стоит {0:0} влияния и даёт постоянный бонус.", EmpireLaws.SwitchCost),
                Current = () => HasLaw() ? 1 : 0,
                RewardText = Loc.T("+25 влияния"), Reward = () => Give(influence: 25f),
                Show = () => UIManager.Instance?.OpenNavWindow(UIManager.NavWindow.Laws)
            });
            l.Add(new Goal
            {
                Id = "design", Icon = LGIcon.Wrench, Target = 1,
                Title = Loc.T("Создайте свой проект корабля"),
                Hint = Loc.T("Конструктор — клавиша N. Подберите оружие и защиту под соседей и сохраните проект."),
                Current = () => s_designed || (ShipDesignManager.Instance != null && ShipDesignManager.Instance.SavedDesigns.Count >= 2) ? 1 : 0,
                RewardText = Loc.T("+50 сплавов"), Reward = () => Give(alloys: 50f),
                Show = () => UIManager.Instance?.OpenNavWindow(UIManager.NavWindow.Designer)
            });
            return l;
        }

        // ================================================================ Подсчёт

        private static GalaxyGenerator Gen
        {
            get { if (s_gen == null) s_gen = UnityEngine.Object.FindAnyObjectByType<GalaxyGenerator>(); return s_gen; }
        }

        private static int SurveyedCount()
        {
            var g = Gen;
            if (g == null) return 0;
            int n = 0;
            foreach (var s in g.Systems) if (s.IsSurveyedBy(0)) n++;
            return n;
        }

        private static int MilitaryShips()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return 0;
            int n = 0;
            foreach (var f in fm.AllFleets)
            {
                var d = f != null ? f.Data : null;
                if (d != null && !d.Destroyed && d.OwnerId == 0 && d.Type == FleetType.Military) n++;
            }
            return n;
        }

        public static int MetCount()
        {
            int n = 0;
            foreach (var ai in AIEmpireManager.All) if (ai != null && Contacts.PlayerMet(ai.OwnerId)) n++;
            return n;
        }

        public static bool HasLaw()
        {
            foreach (var c in EmpireLaws.Categories)
            {
                var law = EmpireLaws.Active(c);
                if (law != null && !law.Default) return true;
            }
            return false;
        }

        // ================================================================ Награды и действия

        private static void Give(float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f)
        {
            var eco = EconomyManager.Instance;
            if (eco == null) return;
            eco.EnergyCredits += energy;
            eco.Minerals += minerals;
            eco.Alloys += alloys;
            eco.Influence += influence;
            eco.RaiseResourcesChanged();
        }

        private static void SelectFirst(FleetType type)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            foreach (var f in fm.AllFleets)
            {
                var d = f != null ? f.Data : null;
                if (d == null || d.Destroyed || d.OwnerId != 0 || d.Type != type) continue;
                fm.SelectFleet(f);
                UnityEngine.Object.FindAnyObjectByType<StrategyCameraController>()?.FocusOn(f.transform.position);
                return;
            }
        }

        private static void ShowColonyTarget()
        {
            var g = Gen;
            if (g == null) return;
            foreach (var s in g.Systems)
            {
                if (s.OwnerId != 0 || !s.IsSurveyed) continue;
                foreach (var p in s.Planets)
                {
                    if (!p.CanColonize || p.IsColonizing) continue;
                    UnityEngine.Object.FindAnyObjectByType<StrategyCameraController>()?.FocusOn(s.Position);
                    GalaxyView.Instance?.SelectSystem(s.Id);
                    return;
                }
            }
            NotificationCenter.Show(Loc.T("Пока негде основать колонию"),
                Loc.T("Расширьте границы форпостами и изучите соседние системы — пригодная планета найдётся."), NotificationCenter.Kind.Info, 6f);
        }
    }

    /// <summary>
    /// Ведёт стартовые цели (награды и уведомления) и подсказывает новичку по ситуации:
    /// ускорить время, потратить влияние на закон, заглянуть в дипломатию, подтянуть флот, сохраниться.
    /// Каждый совет показывается один раз за всё время (не в каждой партии) и не чаще раза в 40 секунд;
    /// пока идёт обучение — молчит. «Пройти обучение заново» в справке возвращает советы.
    /// </summary>
    public class StarterGuide : MonoBehaviour
    {
        public static StarterGuide Instance { get; private set; }

        private const string HintPrefix = "hint.";
        private static readonly string[] HintIds = { "goals", "speed", "law", "contact", "fleet", "war", "colony", "save" };

        private float _timer, _hintCooldown, _slowSpeedTime, _playTime;
        private bool _announcedDone;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnEnable() => UIManager.OnGameStarted += HandleGameStarted;
        private void OnDisable() => UIManager.OnGameStarted -= HandleGameStarted;

        private void HandleGameStarted()
        {
            StarterGoals.Reset();
            _timer = 2.5f;          // дать партии прогрузиться: выполненное к этому моменту засчитается без наград
            _hintCooldown = 25f;
            _slowSpeedTime = 0f;
            _playTime = 0f;
            _announcedDone = false;
        }

        public static void ResetHints()
        {
            foreach (var id in HintIds) PlayerPrefs.DeleteKey(HintPrefix + id);
            PlayerPrefs.Save();
        }

        private static bool Tutorial => TutorialManager.Instance != null && TutorialManager.Instance.IsActive;

        private void Update()
        {
            if (!UIManager.IsGameStarted || GameAudio.Suppressed) return;
            float dt = Time.unscaledDeltaTime;
            var time = TimeManager.Instance;
            bool running = time != null && time.CurrentSpeed > 0;
            if (running) _playTime += dt;
            _slowSpeedTime = running && time.CurrentSpeed == 1 ? _slowSpeedTime + dt : 0f;
            _hintCooldown -= dt;

            _timer -= dt;
            if (_timer > 0f) return;
            _timer = 1.5f;

            foreach (var g in StarterGoals.Evaluate())
            {
                g.Reward?.Invoke();
                NotificationCenter.Show(Loc.F("Цель выполнена: {0}", g.Title), Loc.F("Награда: {0}", g.RewardText), NotificationCenter.Kind.Success, 6f);
            }
            if (StarterGoals.AllDone && !_announcedDone)
            {
                _announcedDone = true;
                if (StarterGoals.DoneCount > 0 && running)
                    NotificationCenter.Show(Loc.T("Стартовые цели выполнены"),
                        Loc.T("Империя встала на ноги. Дальше — путь к победе: клавиша V покажет, как близко каждая из них."), NotificationCenter.Kind.Success, 9f);
            }

            if (!Tutorial && _hintCooldown <= 0f) TryHint();
        }

        private void TryHint()
        {
            var eco = EconomyManager.Instance;

            if (StarterGoals.Visible && Hint("goals", Loc.T("Справа в «Планировщике» — стартовые цели. Они подскажут, чем заняться в первые годы; за каждую — небольшая награда. Кнопка у цели покажет, с чего начать.")))
                return;
            if (_slowSpeedTime > 150f && Hint("speed", Loc.T("Если ждать скучно — ускорьте время клавишами 2 и 3. Пробел ставит паузу: на ней удобно отдавать приказы.")))
                return;
            if (eco != null && eco.Influence >= EmpireLaws.SwitchCost + 15f && !StarterGoals.HasLaw()
                && Hint("law", Loc.T("Влияние копится без дела. Примите закон империи (клавиша L): законы дают постоянные бонусы к экономике, науке или флоту.")))
                return;
            if (StarterGoals.MetCount() > 0 && Hint("contact", Loc.T("Вы встретили соседей. В дипломатии (клавиша P) видно их отношение к вам; там же — торговля, пакты и мир.")))
                return;
            if (WeakerThanNeighbour() && Hint("fleet", Loc.T("Соседи сильнее вас. Постройте корабли на верфи столицы: слабые империи ИИ охотно завоёвывает.")))
                return;
            if (AtWar() && Hint("war", Loc.T("Война. Чтобы захватить вражескую систему, приведите в неё военный флот и выведите из строя звёздную базу. Свои системы защищайте так же — флотом на орбите.")))
                return;
            var t = TimeManager.Instance;
            if (t != null && t.Year >= 2215 && EmpireStats.Colonies(0) <= 1
                && Hint("colony", Loc.T("Империя растёт за счёт колоний. Ищите в своих системах планеты с высокой пригодностью — оповещение «Планета пригодна для колонии» под ресурсами приведёт к ближайшей.")))
                return;
            if (_playTime > 20f * 60f && Hint("save", Loc.T("Не забывайте сохраняться: F5 — быстрое сохранение, F9 — загрузка. Автосохранение настраивается в меню.")))
                return;
        }

        private bool Hint(string id, string text)
        {
            if (PlayerPrefs.GetInt(HintPrefix + id, 0) == 1) return false;
            PlayerPrefs.SetInt(HintPrefix + id, 1);
            PlayerPrefs.Save();
            NotificationCenter.Show(Loc.T("Совет ОРАКУЛА-7"), text, NotificationCenter.Kind.Info, 12f);
            _hintCooldown = 40f;
            return true;
        }

        private static bool WeakerThanNeighbour()
        {
            float me = EmpireStats.MilitaryPower(0);
            foreach (var ai in AIEmpireManager.All)
                if (ai != null && Contacts.PlayerMet(ai.OwnerId) && EmpireStats.MilitaryPower(ai.OwnerId) > me * 1.6f + 50f) return true;
            return false;
        }

        private static bool AtWar()
        {
            foreach (var ai in AIEmpireManager.All) if (ai != null && ai.AtWar) return true;
            return false;
        }
    }
}
