using UnityEngine;
using StellarisClone.Core.Audio;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Озвучка игровых событий, у которых есть общие события-сигналы: старт партии, наука, аномалии,
    /// строительство, захват систем, дипломатия (война / мир / пакт), итог партии.
    /// Точечные звуки (клики, приказы флотам, выстрелы, взрывы) вызываются прямо в местах действия.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }

        private static float _suppressUntil;
        private bool _knownDiplo;
        private bool _wasAtWar, _hadPact;

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
            bool win = outcome == VictoryManager.Outcome.Domination || outcome == VictoryManager.Outcome.Science
                    || outcome == VictoryManager.Outcome.Score || outcome == VictoryManager.Outcome.Conquest;
            SFXManager.Play(win ? Sfx.GameVictory : Sfx.GameDefeat);
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
            var ai = AIEmpireManager.Instance;
            if (ai == null || !UIManager.IsGameStarted) { _knownDiplo = false; return; }
            bool war = ai.AtWar, pact = ai.HasPact;
            if (!_knownDiplo || Suppressed)
            {
                _knownDiplo = true;
                _wasAtWar = war; _hadPact = pact;
                return;
            }
            if (war && !_wasAtWar) SFXManager.Play(Sfx.WarDeclared);
            else if (!war && _wasAtWar) SFXManager.Play(Sfx.PeaceSigned);
            else if (pact && !_hadPact) SFXManager.Play(Sfx.PactSigned);
            _wasAtWar = war; _hadPact = pact;
        }
    }
}
