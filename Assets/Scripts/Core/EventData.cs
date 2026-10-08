using System;
using System.Collections.Generic;

namespace StellarisClone.Core
{
    /// <summary>Тема события — задаёт цвет акцента окна.</summary>
    public enum AnomalyType
    {
        AncientDerelict,
        DistressSignal,
        AlienFauna,
        PrecursorRelic,
        SpatialRift,
        LeaderStory,
        ColonyIncident,
        MilitaryIncident,
        Diplomatic
    }

    /// <summary>С чем связано событие: система, корабль, лидер, планета, соседняя империя.</summary>
    public class EventContext
    {
        public StarSystem System;
        public FleetData Ship;
        public Leader Leader;
        public PlanetData Planet;
        public int Rival = -1;
    }

    [Serializable]
    public class EventOption
    {
        public string OptionText;
        public string ResultTooltip;
        public Action OnSelect;
        /// <summary>Пометка особого варианта («Ксенолог», «Нужно 80 минералов»).</summary>
        public string Requirement;
        /// <summary>null — вариант доступен всегда.</summary>
        public Func<bool> IsAvailable;

        public EventOption(string optionText, string resultTooltip, Action onSelect,
                           string requirement = null, Func<bool> isAvailable = null)
        {
            OptionText = optionText;
            ResultTooltip = resultTooltip;
            OnSelect = onSelect;
            Requirement = requirement;
            IsAvailable = isAvailable;
        }

        public bool Available => IsAvailable == null || IsAvailable();
    }

    [Serializable]
    public class GameEventData
    {
        public string EventId;
        public string Title;
        public string Description;
        public AnomalyType Anomaly;
        public List<EventOption> Options;
        /// <summary>Строка над описанием: где и с кем это случилось.</summary>
        public string Subtitle;
        /// <summary>Иллюстрация из Resources/UI/Events (null — окно без картинки).</summary>
        public string Art;

        public GameEventData(string id, string title, string description, AnomalyType anomaly, List<EventOption> options)
        {
            EventId = id;
            Title = title;
            Description = description;
            Anomaly = anomaly;
            Options = options;
        }
    }

    /// <summary>Отложенное продолжение цепочки событий (сохраняется).</summary>
    [Serializable]
    public class PendingEventSave
    {
        public string Id;
        public int Days;
        public int SystemId = -1, ShipId = -1, LeaderId = -1, PlanetIndex = -1, Rival = -1;
    }

    [Serializable]
    public class EventState
    {
        public int MonthsSinceEvent;
        /// <summary>Месяцев с последнего решения фракции (первое — через год после начала).</summary>
        public int MonthsSinceFaction = 12;
        /// <summary>Сыгранные одноразовые события и флаги цепочек («flag:…»).</summary>
        public List<string> Fired = new List<string>();
        public List<PendingEventSave> Pending = new List<PendingEventSave>();
    }
}
