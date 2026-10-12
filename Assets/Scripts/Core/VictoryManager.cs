using UnityEngine;
using StellarisClone.Generation;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Условия конца партии (пути к победе — экран VictoryPathWindow). Партия рассчитана на 1–2 часа:
    /// подсчёт очков через 40 лет (2240), остальные победы — не раньше ~2228–2233 при хорошей игре
    /// (на скорости ×4 это 1–1,5 часа; темп часов — GamePace.SecondsPerDay).
    /// Баланс сверен с прогоном исследований ИИ по реальному дереву (стоимость технологий и годы уровней
    /// растянуты под 40-летнюю партию): 48 технологий сильнейший ИИ набирает около 2233 года.
    ///   Победа: доминирование (форпосты примерно в 45% систем), технологическая (48 технологий из 58),
    ///           военная (все соперники повержены), экономическая (45% валового продукта галактики 2 года подряд),
    ///           дипломатическая (Галактический совет в 2226/2232/2238 — две трети голосов за вас),
    ///           по очкам — больше, чем у каждого соперника, к 1 января 2240 года.
    ///   Поражение: потеря всех систем, долгое банкротство, ИИ первым добился доминирования,
    ///              технологической или экономической победы, или у кого-то из ИИ больше очков в 2240 году.
    /// </summary>
    public class VictoryManager : MonoBehaviour
    {
        public static VictoryManager Instance { get; private set; }

        public enum Outcome
        {
            None, Domination, Science, Score, Conquest, Economic, Diplomatic,
            DefeatEliminated, DefeatBankruptcy, DefeatScore, DefeatRivalScience, DefeatRivalDomination, DefeatRivalEconomic
        }

        /// <summary>Год подсчёта очков.</summary>
        public const int EndYear = 2240;
        /// <summary>Технологическая победа: столько изученных технологий (из 58).</summary>
        public const int ScienceTarget = 48;
        /// <summary>Экономическая победа: доля валового продукта галактики, удерживаемая EconomyMonths месяцев подряд.</summary>
        public const float EconomyShare = 0.45f;
        public const int EconomyMonths = 24;
        /// <summary>Галактический совет: первое заседание, интервал, доля голосов для избрания лидером.</summary>
        public const int CouncilFirstYear = 2226, CouncilEvery = 6;
        public const float CouncilShare = 2f / 3f;

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
        private int _econMonths, _rivalEconOwner = -1, _rivalEconMonths;

        public int EconMonths => _econMonths;
        public int RivalEconMonths => _rivalEconMonths;
        public int RivalEconOwner => _rivalEconOwner;

        /// <summary>Ближайшее заседание Галактического совета (год), 0 — заседаний больше не будет.</summary>
        public static int NextCouncilYear(int year)
        {
            for (int y = CouncilFirstYear; y < EndYear; y += CouncilEvery) if (y > year) return y;
            return 0;
        }

        /// <summary>
        /// Голосование, будь оно сейчас: за вас — ваши голоса и голоса империй с пактом о ненападении
        /// или хорошим мнением (от 40) без войны; остальные голосуют против.
        /// </summary>
        public static void CouncilForecast(out int forPlayer, out int total)
        {
            forPlayer = EmpireStats.Votes(0);
            total = forPlayer;
            foreach (var ai in AIEmpireManager.Alive)
            {
                int v = EmpireStats.Votes(ai.OwnerId);
                total += v;
                if (!ai.AtWar && (ai.HasPact || ai.Opinion >= 40f)) forPlayer += v;
            }
        }

        /// <summary>Победа/поражение наступили (передаётся в экран итогов).</summary>
        public static event System.Action<Outcome, string, string> OnGameFinished;

        public static bool IsVictory(Outcome o) =>
            o == Outcome.Domination || o == Outcome.Science || o == Outcome.Score || o == Outcome.Conquest
            || o == Outcome.Economic || o == Outcome.Diplomatic;

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
            scienceTechsRequired = ScienceTarget;
        }

        public VictorySave CaptureState() => new VictorySave
        {
            StartYear = _startYear, BankruptMonths = _bankruptMonths, Started = _started, ScoreWarnLevel = _scoreWarnLevel,
            EconMonths = _econMonths, RivalEconOwner = _rivalEconOwner, RivalEconMonths = _rivalEconMonths
        };

        public void RestoreState(VictorySave s)
        {
            if (s == null) return;
            _startYear = s.StartYear;
            _bankruptMonths = s.BankruptMonths;
            _started = s.Started;
            _scoreWarnLevel = s.ScoreWarnLevel;
            _econMonths = s.EconMonths;
            _rivalEconOwner = s.RivalEconOwner;
            _rivalEconMonths = s.RivalEconMonths;
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

            // === ЭКОНОМИКА: доля валового продукта галактики, месяцы подряд ===
            if (TrackEconomy()) return;

            // === ГАЛАКТИЧЕСКИЙ СОВЕТ: 1 января года заседания ===
            if (month == 1 && year >= CouncilFirstYear && year < EndYear && (year - CouncilFirstYear) % CouncilEvery == 0 && anyAlive)
            {
                CouncilForecast(out int pro, out int total);
                int need = Mathf.CeilToInt(total * CouncilShare);
                if (pro >= need) { Finish(Outcome.Diplomatic); return; }
                NotificationCenter.Show("Галактический совет",
                    $"Лидер галактики не избран: за вас {pro} из {total} голосов (нужно {need}). " +
                    (NextCouncilYear(year) > 0 ? $"Следующее заседание — {NextCouncilYear(year)} год." : "Заседаний больше не будет."),
                    NotificationCenter.Kind.Info, 8f);
            }

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

        /// <summary>Месяц экономики: свои и лучшего соперника месяцы над порогом. true — партия окончена.</summary>
        private bool TrackEconomy()
        {
            float me = EmpireStats.GdpShare(0);
            if (me >= EconomyShare)
            {
                _econMonths++;
                if (_econMonths >= EconomyMonths) { Finish(Outcome.Economic); return true; }
                if (_econMonths == 1 || _econMonths == EconomyMonths / 2 || _econMonths == EconomyMonths - 3)
                    NotificationCenter.Show("Экономическое превосходство",
                        $"Ваша доля в продукте галактики {me * 100f:0}%. До экономической победы: {EconomyMonths - _econMonths} мес.",
                        NotificationCenter.Kind.Success, 6f);
            }
            else _econMonths = 0;

            var top = LeadingRival(o => Mathf.RoundToInt(EmpireStats.GdpShare(o) * 1000f));
            float rs = top != null ? EmpireStats.GdpShare(top.OwnerId) : 0f;
            if (top != null && rs >= EconomyShare)
            {
                if (_rivalEconOwner != top.OwnerId) { _rivalEconOwner = top.OwnerId; _rivalEconMonths = 0; }
                _rivalEconMonths++;
                if (_rivalEconMonths >= EconomyMonths) { _rivalName = top.AIName; Finish(Outcome.DefeatRivalEconomic); return true; }
                if (_rivalEconMonths == 1 || _rivalEconMonths == EconomyMonths / 2 || _rivalEconMonths == EconomyMonths - 3)
                    NotificationCenter.Show("Экономика соперника",
                        $"{top.AIName}: {rs * 100f:0}% продукта галактики. Через {EconomyMonths - _rivalEconMonths} мес. — их экономическая победа",
                        NotificationCenter.Kind.Warning, 7f);
            }
            else { _rivalEconOwner = -1; _rivalEconMonths = 0; }
            return false;
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
                case Outcome.Economic:
                    title = "★ ПОБЕДА — ЭКОНОМИЧЕСКАЯ";
                    body = $"{EconomyMonths / 12} года подряд ваша империя давала {EconomyShare * 100f:0}% продукта галактики.";
                    break;
                case Outcome.Diplomatic:
                    title = "★ ПОБЕДА — ДИПЛОМАТИЧЕСКАЯ";
                    body = "Галактический совет избрал вас лидером галактики.";
                    break;
                case Outcome.DefeatRivalEconomic:
                    title = "✕ ПОРАЖЕНИЕ — ЭКОНОМИКА СОПЕРНИКА";
                    body = $"{rival} {EconomyMonths / 12} года подряд давал {EconomyShare * 100f:0}% продукта галактики.";
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
