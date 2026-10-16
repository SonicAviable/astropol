using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Generation;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Условия конца партии (пути к победе — экран VictoryPathWindow). Партия рассчитана на 2–3 часа:
    /// подсчёт очков через 60 лет (2260), остальные победы — не раньше ~2245–2255 при хорошей игре
    /// (на скорости ×4 это 2–2,5 часа; темп часов — GamePace.SecondsPerDay).
    /// Победы нужно не только достичь, но и удержать: у доминирования и экономики есть срок удержания,
    /// у науки — завершающий проект «Вознесение», так что у соперников всегда есть время ответить.
    ///   Победа: доминирование (форпосты примерно в 55% систем, порог удерживается год),
    ///           технологическая (66 технологий из 89 и проект «Вознесение»),
    ///           военная (все соперники повержены),
    ///           экономическая (50% валового продукта галактики 3 года подряд),
    ///           дипломатическая (Галактический совет в 2230/2236/…/2254 — две трети голосов за вас),
    ///           по очкам — больше, чем у каждого соперника, к 1 января 2260 года.
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
        public const int EndYear = 2260;
        /// <summary>Технологическая победа: столько изученных технологий (из всего дерева) и проект «Вознесение».</summary>
        public const int ScienceTarget = 66;
        /// <summary>Завершающая технология дерева: без неё технологическая победа невозможна.</summary>
        public const string CapstoneTech = "soc_asc_1";
        /// <summary>Доминирование: порог систем нужно удерживать столько месяцев подряд.</summary>
        public const int DominationHoldMonths = 12;
        /// <summary>Экономическая победа: доля валового продукта галактики, удерживаемая EconomyMonths месяцев подряд.</summary>
        public const float EconomyShare = 0.50f;
        public const int EconomyMonths = 36;
        /// <summary>Галактический совет: первое заседание, интервал, доля голосов для избрания лидером.</summary>
        public const int CouncilFirstYear = 2230, CouncilEvery = 6;
        public const float CouncilShare = 2f / 3f;

        /// <summary>Годы заседаний Совета: «2230, 2236, 2242, 2248 и 2254».</summary>
        public static string CouncilYearsText()
        {
            var years = new List<string>();
            for (int y = CouncilFirstYear; y < EndYear; y += CouncilEvery) years.Add(y.ToString());
            return years.Count <= 1 ? string.Join("", years) : string.Join(", ", years.GetRange(0, years.Count - 1)) + Loc.T(" и ") + years[years.Count - 1];
        }

        // Технологии, ведущие к «Вознесению»: ИИ перед финалом берётся за них в первую очередь
        private static HashSet<string> s_capstoneChain;
        public static bool InCapstoneChain(string techId)
        {
            if (s_capstoneChain == null)
            {
                var byId = new Dictionary<string, Technology>();
                foreach (var t in TechnologyManager.BuildTechTree()) byId[t.Id] = t;
                s_capstoneChain = new HashSet<string>();
                void Walk(string id)
                {
                    if (!byId.TryGetValue(id, out var t) || !s_capstoneChain.Add(id)) return;
                    foreach (var r in t.RequiredTechIds) Walk(r);
                }
                Walk(CapstoneTech);
            }
            return s_capstoneChain.Contains(techId);
        }

        /// <summary>У империи изучено «Вознесение».</summary>
        public static bool HasAscension(int owner)
        {
            if (owner == 0) return TechnologyManager.Instance?.FindTech(CapstoneTech)?.IsResearched == true;
            return AIEmpireManager.For(owner)?.HasTech(CapstoneTech) == true;
        }

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
        private int _domMonths, _rivalDomOwner = -1, _rivalDomMonths;

        public int EconMonths => _econMonths;
        public int RivalEconMonths => _rivalEconMonths;
        public int RivalEconOwner => _rivalEconOwner;
        /// <summary>Месяцы, которые вы удерживаете порог доминирования, и то же у лидера-соперника.</summary>
        public int DomMonths => _domMonths;
        public int RivalDomMonths => _rivalDomMonths;
        public int RivalDomOwner => _rivalDomOwner;

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
        public string RivalScoreLeaderName => LeadingRival(o => EmpireStats.Score(o).Total)?.AIName ?? Loc.T("Соперник");

        private string _rivalName = Loc.T("Соперник");

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
            EconMonths = _econMonths, RivalEconOwner = _rivalEconOwner, RivalEconMonths = _rivalEconMonths,
            DomMonths = _domMonths, RivalDomOwner = _rivalDomOwner, RivalDomMonths = _rivalDomMonths
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
            _domMonths = s.DomMonths;
            _rivalDomOwner = s.RivalDomOwner;
            _rivalDomMonths = s.RivalDomMonths;
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
                NotificationCenter.Show(Loc.T("Банкротство продолжается"),
                    Loc.F("{0} из {1} мес. — затем экономика рухнет", _bankruptMonths, bankruptcyMonthsRequired), NotificationCenter.Kind.Danger, 6f);
            }
            else _bankruptMonths = 0;

            // === ПОБЕДЫ ИГРОКА ===
            if (TrackDomination()) return;
            if (ScienceProgress >= scienceTechsRequired && HasAscension(0)) { Finish(Outcome.Science); return; }
            bool anyRivals = AIEmpireManager.All.Count > 0;
            bool anyAlive = false;
            foreach (var _ in AIEmpireManager.Alive) { anyAlive = true; break; }
            bool allStarted = true;
            foreach (var a in AIEmpireManager.All) if (a.CapitalSystemId < 0 && !a.IsEliminated) allStarted = false;
            if (anyRivals && allStarted && !anyAlive) { _rivalName = Loc.T("Все соперники"); Finish(Outcome.Conquest); return; }

            // === ЭКОНОМИКА: доля валового продукта галактики, месяцы подряд ===
            if (TrackEconomy()) return;

            // === ГАЛАКТИЧЕСКИЙ СОВЕТ: 1 января года заседания ===
            if (month == 1 && year >= CouncilFirstYear && year < EndYear && (year - CouncilFirstYear) % CouncilEvery == 0 && anyAlive)
            {
                CouncilForecast(out int pro, out int total);
                int need = Mathf.CeilToInt(total * CouncilShare);
                if (pro >= need) { Finish(Outcome.Diplomatic); return; }
                NotificationCenter.Show(Loc.T("Галактический совет"),
                    Loc.F("Лидер галактики не избран: за вас {0} из {1} голосов (нужно {2}). ", pro, total, need) +
                    (NextCouncilYear(year) > 0 ? Loc.F("Следующее заседание — {0} год.", NextCouncilYear(year)) : Loc.T("Заседаний больше не будет.")),
                    NotificationCenter.Kind.Info, 8f);
            }

            // === ПОБЕДЫ ИИ ===
            foreach (var ai in AIEmpireManager.Alive)
                if (EmpireStats.TechCount(ai.OwnerId) >= scienceTechsRequired && HasAscension(ai.OwnerId))
                { _rivalName = ai.AIName; Finish(Outcome.DefeatRivalScience); return; }

            // === ПОДСЧЁТ ОЧКОВ В КОНЦЕ ПАРТИИ ===
            if (year >= EndYear)
            {
                _rivalName = RivalScoreLeaderName;
                Finish(anyAlive && RivalScore > PlayerScore ? Outcome.DefeatScore : Outcome.Score);
                return;
            }
            WarnAboutScore(year, month);
        }

        /// <summary>
        /// Доминирование: порог систем нужно удерживать DominationHoldMonths месяцев подряд — у вас и у лидера-соперника.
        /// Потерянная система сбрасывает счётчик, поэтому у соперников есть время ответить. true — партия окончена.
        /// </summary>
        private bool TrackDomination()
        {
            int me = DominationProgress;
            if (me >= dominationSystemsRequired)
            {
                _domMonths++;
                if (_domMonths >= DominationHoldMonths) { Finish(Outcome.Domination); return true; }
                if (_domMonths == 1 || _domMonths == DominationHoldMonths / 2 || _domMonths == DominationHoldMonths - 2)
                    NotificationCenter.Show(Loc.T("Доминирование"),
                        Loc.F("Вы контролируете {0} систем. Удержите порог ещё {1} мес. — и победа ваша; соперники попытаются вас остановить.", me, DominationHoldMonths - _domMonths),
                        NotificationCenter.Kind.Success, 7f);
            }
            else
            {
                if (_domMonths > 0)
                    NotificationCenter.Show(Loc.T("Порог доминирования потерян"),
                        Loc.F("Систем: {0} из {1}. Счётчик удержания сброшен.", me, dominationSystemsRequired), NotificationCenter.Kind.Warning, 6f);
                _domMonths = 0;
            }

            var top = LeadingRival(EmpireStats.SystemCount);
            int rs = top != null ? EmpireStats.SystemCount(top.OwnerId) : 0;
            if (top != null && rs >= dominationSystemsRequired)
            {
                if (_rivalDomOwner != top.OwnerId) { _rivalDomOwner = top.OwnerId; _rivalDomMonths = 0; }
                _rivalDomMonths++;
                if (_rivalDomMonths >= DominationHoldMonths) { _rivalName = top.AIName; Finish(Outcome.DefeatRivalDomination); return true; }
                if (_rivalDomMonths == 1 || _rivalDomMonths == DominationHoldMonths / 2 || _rivalDomMonths == DominationHoldMonths - 2)
                    NotificationCenter.Show(Loc.T("Экспансия соперника"),
                        Loc.F("{0} контролирует {1} систем. Через {2} мес. — их победа: отбейте системы, чтобы сбросить счётчик.", top.AIName, rs, DominationHoldMonths - _rivalDomMonths),
                        NotificationCenter.Kind.Warning, 8f);
            }
            else { _rivalDomOwner = -1; _rivalDomMonths = 0; }
            return false;
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
                    NotificationCenter.Show(Loc.T("Экономическое превосходство"),
                        Loc.F("Ваша доля в продукте галактики {0:0}%. До экономической победы: {1} мес.", me * 100f, EconomyMonths - _econMonths),
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
                    NotificationCenter.Show(Loc.T("Экономика соперника"),
                        Loc.F("{0}: {1:0}% продукта галактики. Через {2} мес. — их экономическая победа", top.AIName, rs * 100f, EconomyMonths - _rivalEconMonths),
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
            NotificationCenter.Show(Loc.F("До подсчёта очков: {0} {1}", left, YearsWord(left)),
                Loc.F("Ваш счёт {0}, у лучшего соперника ({1}) {2}. {3}", me, RivalScoreLeaderName, rival, (ahead ? Loc.T("Вы впереди — удержите отрыв") : Loc.T("Соперник впереди — наращивайте системы, население и науку"))),
                ahead ? NotificationCenter.Kind.Info : NotificationCenter.Kind.Warning, 8f);
        }

        public static string YearsWord(int n)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return Loc.T("год");
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return Loc.T("года");
            return Loc.T("лет");
        }

        private void Finish(Outcome outcome)
        {
            _finished = true;

            string rival = _rivalName;
            string title, body;
            switch (outcome)
            {
                case Outcome.Domination:
                    title = Loc.T("★ ПОБЕДА — ДОМИНИРОВАНИЕ");
                    body = Loc.F("Империя удерживала {0} систем {1} месяцев подряд.", dominationSystemsRequired, DominationHoldMonths);
                    break;
                case Outcome.Science:
                    title = Loc.T("★ ПОБЕДА — НАУЧНАЯ");
                    body = Loc.F("Изучено {0} технологий, проект «Вознесение» завершён.", scienceTechsRequired);
                    break;
                case Outcome.Conquest:
                    title = Loc.T("★ ПОБЕДА — СОПЕРНИКИ ПОВЕРЖЕНЫ");
                    body = Loc.T("Все империи-соперники потеряли свои системы.");
                    break;
                case Outcome.Economic:
                    title = Loc.T("★ ПОБЕДА — ЭКОНОМИЧЕСКАЯ");
                    body = Loc.F("{0} года подряд ваша империя давала {1:0}% продукта галактики.", EconomyMonths / 12, EconomyShare * 100f);
                    break;
                case Outcome.Diplomatic:
                    title = Loc.T("★ ПОБЕДА — ДИПЛОМАТИЧЕСКАЯ");
                    body = Loc.T("Галактический совет избрал вас лидером галактики.");
                    break;
                case Outcome.DefeatRivalEconomic:
                    title = Loc.T("✕ ПОРАЖЕНИЕ — ЭКОНОМИКА СОПЕРНИКА");
                    body = Loc.F("{0} {1} года подряд давал {2:0}% продукта галактики.", rival, EconomyMonths / 12, EconomyShare * 100f);
                    break;
                case Outcome.Score:
                    title = Loc.T("★ ПОБЕДА ПО ОЧКАМ");
                    body = Loc.F("{0} год: ваш счёт {1} — больше, чем у любого соперника (лучший — {2}).", EndYear, PlayerScore, RivalScore);
                    break;
                case Outcome.DefeatEliminated:
                    title = Loc.T("✕ ПОРАЖЕНИЕ — УНИЧТОЖЕНЫ");
                    body = Loc.T("Империя потеряла все свои системы.");
                    break;
                case Outcome.DefeatScore:
                    title = Loc.T("✕ ПОРАЖЕНИЕ ПО ОЧКАМ");
                    body = Loc.F("{0} год: у {1} {2} очков против ваших {3}.", EndYear, rival, RivalScore, PlayerScore);
                    break;
                case Outcome.DefeatRivalScience:
                    title = Loc.T("✕ ПОРАЖЕНИЕ — НАУЧНЫЙ ПРОРЫВ СОПЕРНИКА");
                    body = Loc.F("{0} изучил {1} технологий и завершил проект «Вознесение».", rival, scienceTechsRequired);
                    break;
                case Outcome.DefeatRivalDomination:
                    title = Loc.T("✕ ПОРАЖЕНИЕ — ЭКСПАНСИЯ СОПЕРНИКА");
                    body = Loc.F("{0} {1} месяцев подряд удерживал {2} систем.", rival, DominationHoldMonths, dominationSystemsRequired);
                    break;
                default:
                    title = Loc.T("✕ ПОРАЖЕНИЕ — КРАХ ЭКОНОМИКИ");
                    body = Loc.F("{0} месяцев подряд в банкротстве.", bankruptcyMonthsRequired);
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
