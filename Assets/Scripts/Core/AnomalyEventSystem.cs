using System;
using UnityEngine;

namespace StellarisClone.Core
{
    public class AnomalyEventSystem : MonoBehaviour
    {
        public static AnomalyEventSystem Instance { get; private set; }

        public static event Action<GameEventData> OnEventTriggered;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public static Color GetAnomalyAccent(AnomalyType type)
        {
            return type switch
            {
                AnomalyType.AlienFauna      => new Color(0.30f, 0.92f, 0.55f),
                AnomalyType.PrecursorRelic  => new Color(0.95f, 0.78f, 0.28f),
                AnomalyType.SpatialRift     => new Color(0.85f, 0.65f, 0.95f),
                AnomalyType.DistressSignal  => new Color(0.95f, 0.30f, 0.30f),
                _                           => new Color(0.20f, 0.92f, 0.82f)
            };
        }

        public GameEventData GetRandomEventForSurvey()
        {
            return EventDatabase.GetRandomEvent();
        }

        public void TriggerEventForSurvey(StarSystem system)
        {
            var ev = GetRandomEventForSurvey();
            if (ev == null) return;
            OnEventTriggered?.Invoke(ev);
        }
    }
}
