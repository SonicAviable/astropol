using System;
using System.Collections.Generic;

namespace StellarisClone.Core
{
    public enum AnomalyType
    {
        AncientDerelict,
        DistressSignal,
        AlienFauna,
        PrecursorRelic,
        SpatialRift
    }

    [Serializable]
    public class EventOption
    {
        public string OptionText;
        public string ResultTooltip;
        public Action OnSelect;

        public EventOption(string optionText, string resultTooltip, Action onSelect)
        {
            OptionText = optionText;
            ResultTooltip = resultTooltip;
            OnSelect = onSelect;
        }
    }

    [Serializable]
    public class GameEventData
    {
        public string EventId;
        public string Title;
        public string Description;
        public AnomalyType Anomaly;
        public List<EventOption> Options;

        public GameEventData(string id, string title, string description, AnomalyType anomaly, List<EventOption> options)
        {
            EventId = id;
            Title = title;
            Description = description;
            Anomaly = anomaly;
            Options = options;
        }
    }
}
