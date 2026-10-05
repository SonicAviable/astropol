using UnityEngine;
using StellarisClone.Generation;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    public class VictoryManager : MonoBehaviour
    {
        public static VictoryManager Instance { get; private set; }

        public enum Outcome { None, Domination, Science, Survival, DefeatEliminated, DefeatBankruptcy }

        [Header("Условия победы")]
        [SerializeField] private int dominationSystemsRequired = 40;
        [SerializeField] private int scienceTechsRequired = 15;
        [SerializeField] private int survivalYearsRequired = 50;

        [Header("Условия поражения")]
        [SerializeField] private int bankruptcyMonthsRequired = 6;

        private GalaxyGenerator _generator;
        private int _startYear = 2200;
        private int _bankruptMonths;
        private bool _finished;
        private bool _started;

        /// <summary>Победа/поражение наступили (передаётся в экран итогов).</summary>
        public static event System.Action<Outcome, string, string> OnGameFinished;

        // ==================== ПРОГРЕСС (для HUD целей и обзора империи) ====================

        public bool IsFinished => _finished;
        public Outcome Result { get; private set; } = Outcome.None;
        public int DominationRequired => dominationSystemsRequired;
        public int ScienceRequired => scienceTechsRequired;
        public int SurvivalYearsRequired => survivalYearsRequired;
        public int BankruptcyLimit => bankruptcyMonthsRequired;
        public int BankruptMonths => _bankruptMonths;

        public int DominationProgress
        {
            get
            {
                if (_generator == null) _generator = FindAnyObjectByType<GalaxyGenerator>();
                if (_generator == null) return 0;
                int owned = 0;
                foreach (var s in _generator.Systems)
                    if (s.OwnerId == 0 && s.HasStarbase) owned++;
                return owned;
            }
        }

        public int ScienceProgress
        {
            get
            {
                var tm = TechnologyManager.Instance;
                if (tm == null) return 0;
                int done = 0;
                foreach (var t in tm.AllTechs) if (t.IsResearched) done++;
                return done;
            }
        }

        public int YearsElapsed
        {
            get
            {
                var time = TimeManager.Instance;
                if (time == null || !_started) return 0;
                return Mathf.Max(0, time.Year - _startYear);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // Цель доминирования зависит от размера галактики
            dominationSystemsRequired = GameSession.Settings.DominationTarget;
            // 15 базовых технологий изучались за ~5 минут — научная победа требует веток Tier 1–2
            scienceTechsRequired = 25;
        }

        public VictorySave CaptureState() => new VictorySave
        {
            StartYear = _startYear, BankruptMonths = _bankruptMonths, Started = _started
        };

        public void RestoreState(VictorySave s)
        {
            if (s == null) return;
            _startYear = s.StartYear;
            _bankruptMonths = s.BankruptMonths;
            _started = s.Started;
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

            // === ПОРАЖЕНИЕ 1: потеря всех систем ===
            if (_generator != null)
            {
                int owned = 0;
                foreach (var s in _generator.Systems)
                    if (s.OwnerId == 0) owned++;

                if (owned == 0) { Finish(Outcome.DefeatEliminated); return; }
            }

            // === ПОРАЖЕНИЕ 2: долгое банкротство ===
            var eco = EconomyManager.Instance;
            if (eco != null && eco.IsBankrupt)
            {
                _bankruptMonths++;
                if (_bankruptMonths >= bankruptcyMonthsRequired)
                {
                    Finish(Outcome.DefeatBankruptcy);
                    return;
                }
            }
            else _bankruptMonths = 0;

            // === ПОБЕДА 1: доминирование ===
            if (_generator != null)
            {
                int owned = 0;
                foreach (var s in _generator.Systems)
                    if (s.OwnerId == 0 && s.HasStarbase) owned++;

                if (owned >= dominationSystemsRequired)
                {
                    Finish(Outcome.Domination);
                    return;
                }
            }

            // === ПОБЕДА 2: научная ===
            var tm = TechnologyManager.Instance;
            if (tm != null)
            {
                int done = 0;
                foreach (var t in tm.AllTechs) if (t.IsResearched) done++;
                if (done >= scienceTechsRequired)
                {
                    Finish(Outcome.Science);
                    return;
                }
            }

            // === ПОБЕДА 3: выживание ===
            if (year - _startYear >= survivalYearsRequired)
            {
                Finish(Outcome.Survival);
            }
        }

        private void Finish(Outcome outcome)
        {
            _finished = true;

            string title, body;
            NotificationCenter.Kind kind;

            switch (outcome)
            {
                case Outcome.Domination:
                    title = "★ ПОБЕДА — ДОМИНИРОВАНИЕ";
                    body = $"Империя контролирует {dominationSystemsRequired} систем.";
                    kind = NotificationCenter.Kind.Success;
                    break;

                case Outcome.Science:
                    title = "★ ПОБЕДА — НАУЧНАЯ";
                    body = $"Изучено {scienceTechsRequired} технологий.";
                    kind = NotificationCenter.Kind.Success;
                    break;

                case Outcome.Survival:
                    title = "★ ПОБЕДА — ВЫЖИВАНИЕ";
                    body = $"Империя простояла {survivalYearsRequired} лет.";
                    kind = NotificationCenter.Kind.Success;
                    break;

                case Outcome.DefeatEliminated:
                    title = "✕ ПОРАЖЕНИЕ — УНИЧТОЖЕНЫ";
                    body = "Империя потеряла все свои системы.";
                    kind = NotificationCenter.Kind.Danger;
                    break;

                default:
                    title = "✕ ПОРАЖЕНИЕ — КРАХ ЭКОНОМИКИ";
                    body = $"{bankruptcyMonthsRequired} месяцев подряд в минусе по энергии.";
                    kind = NotificationCenter.Kind.Danger;
                    break;
            }

            Result = outcome;
            NotificationCenter.Show(title, body, kind, 12f);
            Debug.Log($"<color=#FFD700>[Победа/Поражение]</color> {title} — {body}");

            if (TimeManager.Instance != null) TimeManager.Instance.SetSpeed(0);
            OnGameFinished?.Invoke(outcome, title, body);
        }
    }
}