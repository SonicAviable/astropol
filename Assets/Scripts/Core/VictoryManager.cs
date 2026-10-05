using UnityEngine;
using StellarisClone.Generation;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Условия конца партии.
    ///   Победа: доминирование (форпосты в N системах), научная (N технологий), разгром соперника,
    ///           или больше очков, чем у ИИ, к 1 января 2235 года.
    ///   Поражение: потеря всех систем, долгое банкротство, ИИ первым добился доминирования или
    ///              научной победы, или у ИИ больше очков в 2235 году.
    /// </summary>
    public class VictoryManager : MonoBehaviour
    {
        public static VictoryManager Instance { get; private set; }

        public enum Outcome
        {
            None, Domination, Science, Score, Conquest,
            DefeatEliminated, DefeatBankruptcy, DefeatScore, DefeatRivalScience, DefeatRivalDomination
        }

        /// <summary>Год подсчёта очков.</summary>
        public const int EndYear = 2235;

        [Header("Условия победы")]
        [SerializeField] private int dominationSystemsRequired = 40;
        [SerializeField] private int scienceTechsRequired = 25;

        [Header("Условия поражения")]
        [SerializeField] private int bankruptcyMonthsRequired = 6;

        private GalaxyGenerator _generator;
        private int _startYear = 2200;
        private int _bankruptMonths;
        private bool _finished;
        private bool _started;
        private int _scoreWarnLevel;

        /// <summary>Победа/поражение наступили (передаётся в экран итогов).</summary>
        public static event System.Action<Outcome, string, string> OnGameFinished;

        public static bool IsVictory(Outcome o) =>
            o == Outcome.Domination || o == Outcome.Science || o == Outcome.Score || o == Outcome.Conquest;

        // ==================== ПРОГРЕСС (для HUD целей и обзора империи) ====================

        public bool IsFinished => _finished;
        public Outcome Result { get; private set; } = Outcome.None;
        public int DominationRequired => dominationSystemsRequired;
        public int ScienceRequired => scienceTechsRequired;
        public int BankruptcyLimit => bankruptcyMonthsRequired;
        public int BankruptMonths => _bankruptMonths;

        public int DominationProgress => EmpireStats.SystemCount(0);
        public int ScienceProgress => EmpireStats.TechCount(0);
        public int RivalDominationProgress => EmpireStats.SystemCount(AIEmpireManager.AIOwnerId);
        public int RivalScienceProgress => EmpireStats.TechCount(AIEmpireManager.AIOwnerId);

        public int PlayerScore => EmpireStats.Score(0).Total;
        public int RivalScore => EmpireStats.Score(AIEmpireManager.AIOwnerId).Total;

        public int YearsElapsed
        {
            get
            {
                var time = TimeManager.Instance;
                if (time == null || !_started) return 0;
                return Mathf.Max(0, time.Year - _startYear);
            }
        }

        public int YearsLeft
        {
            get
            {
                var time = TimeManager.Instance;
                return time == null ? EndYear - 2200 : Mathf.Max(0, EndYear - time.Year);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            dominationSystemsRequired = GameSession.Settings.DominationTarget;
            scienceTechsRequired = 25;
        }

        public VictorySave CaptureState() => new VictorySave
        {
            StartYear = _startYear, BankruptMonths = _bankruptMonths, Started = _started, ScoreWarnLevel = _scoreWarnLevel
        };

        public void RestoreState(VictorySave s)
        {
            if (s == null) return;
            _startYear = s.StartYear;
            _bankruptMonths = s.BankruptMonths;
            _started = s.Started;
            _scoreWarnLevel = s.ScoreWarnLevel;
        }

        private void Start()
        {
            _generator = FindAnyObjectByType<GalaxyGenerator>();
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed += HandleDay;
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed -= HandleDay;
        }

        private void HandleDay(int day, int month, int year)
        {
            if (_finished) return;
            if (!UIManager.IsGameStarted) return;

            if (!_started)
            {
                _started = true;
                _startYear = year;
            }

            if (day != 1) return;

            // === ПОРАЖЕНИЕ: потеря всех систем ===
            if (_generator != null)
            {
                int owned = 0;
                foreach (var s in _generator.Systems)
                    if (s.OwnerId == 0) owned++;
                if (owned == 0) { Finish(Outcome.DefeatEliminated); return; }
            }

            // === ПОРАЖЕНИЕ: долгое банкротство ===
            var eco = EconomyManager.Instance;
            if (eco != null && eco.IsBankrupt)
            {
                _bankruptMonths++;
                if (_bankruptMonths >= bankruptcyMonthsRequired) { Finish(Outcome.DefeatBankruptcy); return; }
                NotificationCenter.Show("Банкротство продолжается",
                    $"{_bankruptMonths} из {bankruptcyMonthsRequired} мес. — затем экономика рухнет", NotificationCenter.Kind.Danger, 6f);
            }
            else _bankruptMonths = 0;

            // === ПОБЕДЫ ИГРОКА ===
            if (DominationProgress >= dominationSystemsRequired) { Finish(Outcome.Domination); return; }
            if (ScienceProgress >= scienceTechsRequired) { Finish(Outcome.Science); return; }
            var ai = AIEmpireManager.Instance;
            if (ai != null && ai.IsEliminated) { Finish(Outcome.Conquest); return; }

            // === ПОБЕДЫ ИИ ===
            if (ai != null && !ai.IsEliminated)
            {
                if (RivalDominationProgress >= dominationSystemsRequired) { Finish(Outcome.DefeatRivalDomination); return; }
                if (RivalScienceProgress >= scienceTechsRequired) { Finish(Outcome.DefeatRivalScience); return; }
            }

            // === ПОДСЧЁТ ОЧКОВ В 2235 ГОДУ ===
            if (year >= EndYear)
            {
                Finish(ai != null && RivalScore > PlayerScore ? Outcome.DefeatScore : Outcome.Score);
                return;
            }
            WarnAboutScore(year, month);
        }

        private void WarnAboutScore(int year, int month)
        {
            if (month != 1) return;
            int left = EndYear - year;
            int level = left <= 1 ? 3 : left <= 5 ? 2 : left <= 10 ? 1 : 0;
            if (level <= _scoreWarnLevel) return;
            _scoreWarnLevel = level;
            int me = PlayerScore, rival = RivalScore;
            bool ahead = me >= rival;
            NotificationCenter.Show($"До подсчёта очков: {left} {YearsWord(left)}",
                $"Ваш счёт {me}, у соперника {rival}. {(ahead ? "Вы впереди — удержите отрыв" : "Соперник впереди — наращивайте системы, население и науку")}",
                ahead ? NotificationCenter.Kind.Info : NotificationCenter.Kind.Warning, 8f);
        }

        public static string YearsWord(int n)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return "год";
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return "года";
            return "лет";
        }

        private void Finish(Outcome outcome)
        {
            _finished = true;

            string rival = AIEmpireManager.Instance != null ? AIEmpireManager.Instance.AIName : "Соперник";
            string title, body;
            switch (outcome)
            {
                case Outcome.Domination:
                    title = "★ ПОБЕДА — ДОМИНИРОВАНИЕ";
                    body = $"Империя контролирует {dominationSystemsRequired} систем.";
                    break;
                case Outcome.Science:
                    title = "★ ПОБЕДА — НАУЧНАЯ";
                    body = $"Изучено {scienceTechsRequired} технологий.";
                    break;
                case Outcome.Conquest:
                    title = "★ ПОБЕДА — СОПЕРНИК ПОВЕРЖЕН";
                    body = $"{rival} потерял все свои системы.";
                    break;
                case Outcome.Score:
                    title = "★ ПОБЕДА ПО ОЧКАМ";
                    body = $"{EndYear} год: ваш счёт {PlayerScore} против {RivalScore} у соперника.";
                    break;
                case Outcome.DefeatEliminated:
                    title = "✕ ПОРАЖЕНИЕ — УНИЧТОЖЕНЫ";
                    body = "Империя потеряла все свои системы.";
                    break;
                case Outcome.DefeatScore:
                    title = "✕ ПОРАЖЕНИЕ ПО ОЧКАМ";
                    body = $"{EndYear} год: у соперника {RivalScore} очков против ваших {PlayerScore}.";
                    break;
                case Outcome.DefeatRivalScience:
                    title = "✕ ПОРАЖЕНИЕ — НАУЧНЫЙ ПРОРЫВ СОПЕРНИКА";
                    body = $"{rival} первым изучил {scienceTechsRequired} технологий.";
                    break;
                case Outcome.DefeatRivalDomination:
                    title = "✕ ПОРАЖЕНИЕ — ЭКСПАНСИЯ СОПЕРНИКА";
                    body = $"{rival} первым занял {dominationSystemsRequired} систем.";
                    break;
                default:
                    title = "✕ ПОРАЖЕНИЕ — КРАХ ЭКОНОМИКИ";
                    body = $"{bankruptcyMonthsRequired} месяцев подряд в банкротстве.";
                    break;
            }

            Result = outcome;
            NotificationCenter.Show(title, body, IsVictory(outcome) ? NotificationCenter.Kind.Success : NotificationCenter.Kind.Danger, 12f);
            Debug.Log($"<color=#FFD700>[Победа/Поражение]</color> {title} — {body}");

            if (TimeManager.Instance != null) TimeManager.Instance.SetSpeed(0);
            OnGameFinished?.Invoke(outcome, title, body);
        }
    }
}
