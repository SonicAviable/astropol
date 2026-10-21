using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core.Audio;
using StellarisClone.Rendering;
using StellarisClone.Generation;

namespace StellarisClone.Core
{
    /// <summary>
    /// Озвучка игровых событий, у которых есть общие события-сигналы: старт партии, наука, аномалии,
    /// строительство, захват систем, дипломатия (война / мир / пакт), итог партии.
    /// Точечные звуки (клики, приказы флотам, выстрелы, взрывы) вызываются прямо в местах действия.
    ///
    /// Звук по смыслу: беды звучат так, что их не спутать с кликом —
    ///   • вражеский флот вошёл в нашу систему — сирена (RedAlert), вспышка краёв экрана и вибрация;
    ///   • казна вот-вот опустеет — «тающая казна» (BudgetWarning);
    ///   • банкротство — «обесточивание» (Bankrupt), янтарная вспышка и гул.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }

        private static float _suppressUntil;
        private bool _knownDiplo;
        private int _wars, _pacts;

        // Банкротство: 0 — норма, 1 — угроза, 2 — банкрот
        private int _ecoState = -1;
        private float _lastBudgetWarn = -100f;

        // Вражеские флоты на нашей территории (флот × система), чтобы тревожить только при входе
        private readonly HashSet<long> _intruders = new HashSet<long>();
        private readonly HashSet<long> _scan = new HashSet<long>();
        private bool _knownIntruders;
        private float _nextScan;
        private GalaxyGenerator _gen;

        /// <summary>Заглушить событийные звуки на время (например, пока применяется загруженное сохранение).</summary>
        public static void Suppress(float seconds) => _suppressUntil = Mathf.Max(_suppressUntil, Time.unscaledTime + seconds);
        public static bool Suppressed => Time.unscaledTime < _suppressUntil;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            UIManager.OnGameStarted += HandleGameStarted;
            TechnologyManager.OnTechCompleted += HandleTech;
            AnomalyEventSystem.OnEventTriggered += HandleAnomaly;
            VictoryManager.OnGameFinished += HandleFinished;
            SiegeManager.OnSystemCaptured += HandleCaptured;
            ConstructionManager.OnJobCompleted += HandleJob;
        }

        private void OnDisable()
        {
            UIManager.OnGameStarted -= HandleGameStarted;
            TechnologyManager.OnTechCompleted -= HandleTech;
            AnomalyEventSystem.OnEventTriggered -= HandleAnomaly;
            VictoryManager.OnGameFinished -= HandleFinished;
            SiegeManager.OnSystemCaptured -= HandleCaptured;
            ConstructionManager.OnJobCompleted -= HandleJob;
        }

        private void HandleGameStarted()
        {
            _knownDiplo = false;
            _ecoState = -1;
            _knownIntruders = false;
            if (Suppressed) return;
            SFXManager.Play(Sfx.GameStart);
            Suppress(1.2f);   // стартовые уведомления и пауза не перебивают заставку
        }

        private void HandleTech(Technology t)
        {
            if (Suppressed) return;
            SFXManager.Play(Sfx.ResearchComplete);
        }

        private void HandleAnomaly(GameEventData ev) => SFXManager.Play(Sfx.Anomaly);

        private void HandleFinished(VictoryManager.Outcome outcome, string title, string body)
        {
            SFXManager.Play(VictoryManager.IsVictory(outcome) ? Sfx.GameVictory : Sfx.GameDefeat);
        }

        private void HandleCaptured(StarSystem sys, int oldOwner, int newOwner)
        {
            if (Suppressed) return;
            if (newOwner == 0) SFXManager.Play(Sfx.SystemCaptured);
            else if (oldOwner == 0) SFXManager.Play(Sfx.SystemLost);
        }

        private void HandleJob(ConstructionJob job)
        {
            if (job == null || job.Owner != 0 || Suppressed) return;
            switch (job.Kind)
            {
                case JobKind.Ship: SFXManager.Play(Sfx.ShipLaunched); break;
                case JobKind.Colony: SFXManager.Play(Sfx.ColonyFounded); break;
                default: SFXManager.Play(Sfx.BuildComplete); break;
            }
        }

        /// <summary>Дипломатия: отслеживаем переходы состояния (объявление войны, мир, пакт).</summary>
        private void Update()
        {
            if (!UIManager.IsGameStarted) { _knownDiplo = false; _ecoState = -1; _knownIntruders = false; return; }
            TrackEconomy();
            if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 0.5f; TrackIntruders(); }
            if (AIEmpireManager.All.Count == 0) { _knownDiplo = false; return; }
            // Войны и пакты игрока с любой из империй ИИ
            int war = 0, pact = 0;
            foreach (var ai in AIEmpireManager.All) { if (ai.AtWar) war++; if (ai.HasPact) pact++; }
            if (!_knownDiplo || Suppressed)
            {
                _knownDiplo = true;
                _wars = war; _pacts = pact;
                return;
            }
            if (war > _wars) SFXManager.Play(Sfx.WarDeclared);
            else if (war < _wars) SFXManager.Play(Sfx.PeaceSigned);
            else if (pact > _pacts) SFXManager.Play(Sfx.PactSigned);
            _wars = war; _pacts = pact;
        }

        /// <summary>Казна: звук при переходе в угрозу банкротства и в само банкротство (не каждый месяц подряд).</summary>
        private void TrackEconomy()
        {
            var eco = EconomyManager.Instance;
            if (eco == null) return;
            int state = eco.IsBankrupt ? 2 : eco.BankruptcyLooming ? 1 : 0;
            if (_ecoState < 0 || Suppressed) { _ecoState = state; return; }
            if (state > _ecoState)
            {
                if (state == 2)
                {
                    SFXManager.Play(Sfx.Bankrupt);
                    AlarmFeedback.Bankrupt();
                }
                else if (Time.unscaledTime - _lastBudgetWarn > 60f)
                {
                    _lastBudgetWarn = Time.unscaledTime;
                    SFXManager.Play(Sfx.BudgetWarning);
                }
            }
            _ecoState = state;
        }

        /// <summary>Вражеский военный флот встал в нашей системе — тревога (один раз на вход).</summary>
        private void TrackIntruders()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            if (_gen == null) _gen = FindAnyObjectByType<GalaxyGenerator>();
            if (_gen == null) return;
            var systems = _gen.Systems;

            _scan.Clear();
            FleetData first = null;
            StarSystem firstSys = null;
            foreach (var f in fm.AllFleets)
            {
                var d = f != null ? f.Data : null;
                if (d == null || d.Destroyed || d.OwnerId == 0 || d.OwnerId == Threats.LeviathanOwner) continue;
                if (d.Type != FleetType.Military || d.State == FleetState.InHyperlane) continue;
                if (d.CurrentSystemId < 0 || d.CurrentSystemId >= systems.Count) continue;
                var sys = systems[d.CurrentSystemId];
                if (sys == null || sys.OwnerId != 0 || !Diplomacy.AtWar(0, d.OwnerId)) continue;
                long key = ((long)d.Id << 20) | (uint)d.CurrentSystemId;
                _scan.Add(key);
                if (first == null && !_intruders.Contains(key)) { first = d; firstSys = sys; }
            }

            bool alarm = _knownIntruders && !Suppressed && first != null;
            _intruders.Clear();
            _intruders.UnionWith(_scan);
            _knownIntruders = true;
            if (!alarm) return;

            SFXManager.Play(Sfx.RedAlert);
            AlarmFeedback.Attack();
            NotificationCenter.Show(Loc.T("Враг на нашей территории"),
                Loc.F("Флот «{0}» вошёл в систему {1}. Перехватите его военным флотом, иначе начнётся осада", first.Name, firstSys.Name),
                NotificationCenter.Kind.Danger, 7f);
        }
    }
}
