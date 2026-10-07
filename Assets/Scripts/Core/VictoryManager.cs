using UnityEngine;
using StellarisClone.Generation;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Условия конца партии.
    ///   Победа: доминирование (форпосты в N системах), научная (N технологий), разгром всех соперников,
    ///           или больше очков, чем у каждой империи ИИ, к 1 января 2235 года.
    ///   Поражение: потеря всех систем, долгое банкротство, любой ИИ первым добился доминирования или
    ///              научной победы, или у кого-то из ИИ больше очков в 2235 году.
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
        [SerializeField] private int scienceTechsRequired = 40;

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
        /// <summary>Лучший из живых соперников по показателю (null — соперников не осталось).</summary>
        public static AIEmpireManager LeadingRival(System.Func<int, int> metric)
        {
            AIEmpireManager best = null;
            int bestValue = int.MinValue;
            foreach (var ai in AIEmpireManager.Alive)
            {
                int v = metric(ai.OwnerId);
                if (v > bestValue) { bestValue = v; best = ai; }
            }
            return best;
        }

        private static int Best(System.Func<int, int> metric)
        {
            var r = LeadingRival(metric);
            return r != null ? metric(r.OwnerId) : 0;
        }

        public int RivalDominationProgress => Best(EmpireStats.SystemCount);
        public int RivalScienceProgress => Best(EmpireStats.TechCount);

        public int PlayerScore => EmpireStats.Score(0).Total;
        public int RivalScore => Best(o => EmpireStats.Score(o).Total);
        public string RivalScoreLeaderName => LeadingRival(o => EmpireStats.Score(o).Total)?.AIName ?? "Соперник";

        private string _rivalName = "Соперник";

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
            scienceTechsRequired = 40;
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
            bool anyRivals = AIEmpireManager.All.Count > 0;
            bool anyAlive = false;
            foreach (var _ in AIEmpireManager.Alive) { anyAlive = true; break; }
            bool allStarted = true;
            foreach (var a in AIEmpireManager.All) if (a.CapitalSystemId < 0 && !a.IsEliminated) allStarted = false;
            if (anyRivals && allStarted && !anyAlive) { _rivalName = "Все соперники"; Finish(Outcome.Conquest); return; }

            // === ПОБЕДЫ ИИ ===
            var dom = LeadingRival(EmpireStats.SystemCount);
            if (dom != null && EmpireStats.SystemCount(dom.OwnerId) >= dominationSystemsRequired)
            { _rivalName = dom.AIName; Finish(Outcome.DefeatRivalDomination); return; }
            var sci = LeadingRival(EmpireStats.TechCount);
            if (sci != null && EmpireStats.TechCount(sci.OwnerId) >= scienceTechsRequired)
            { _rivalName = sci.AIName; Finish(Outcome.DefeatRivalScience); return; }

            // === ПОДСЧЁТ ОЧКОВ В 2235 ГОДУ ===
            if (year >= EndYear)
            {
                _rivalName = RivalScoreLeaderName;
                Finish(anyAlive && RivalScore > PlayerScore ? Outcome.DefeatScore : Outcome.Score);
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
                $"Ваш счёт {me}, у лучшего соперника ({RivalScoreLeaderName}) {rival}. {(ahead ? "Вы впереди — удержите отрыв" : "Соперник впереди — наращивайте системы, население и науку")}",
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

            string rival = _rivalName;
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
                    title = "★ ПОБЕДА — СОПЕРНИКИ ПОВЕРЖЕНЫ";
                    body = "Все империи-соперники потеряли свои системы.";
                    break;
                case Outcome.Score:
                    title = "★ ПОБЕДА ПО ОЧКАМ";
                    body = $"{EndYear} год: ваш счёт {PlayerScore} — больше, чем у любого соперника (лучший — {RivalScore}).";
                    break;
                case Outcome.DefeatEliminated:
                    title = "✕ ПОРАЖЕНИЕ — УНИЧТОЖЕНЫ";
                    body = "Империя потеряла все свои системы.";
                    break;
                case Outcome.DefeatScore:
                    title = "✕ ПОРАЖЕНИЕ ПО ОЧКАМ";
                    body = $"{EndYear} год: у {rival} {RivalScore} очков против ваших {PlayerScore}.";
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
