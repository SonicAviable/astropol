using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Показ событий: аномалии при разведке, ежемесячные истории (лидеры, колонии, флот, соседи)
    /// и продолжения цепочек через заданное число дней. Окна не перекрывают друг друга —
    /// следующее событие ждёт в очереди, пока игрок не ответит на текущее.
    /// </summary>
    public class AnomalyEventSystem : MonoBehaviour
    {
        public static AnomalyEventSystem Instance { get; private set; }

        public static event Action<GameEventData> OnEventTriggered;

        /// <summary>Сколько месяцев затишья гарантированно проходит между ежемесячными событиями.</summary>
        private const int QuietMonths = 3;
        private const float MonthlyChance = 0.3f;
        /// <summary>Как часто фракция игрока принимает своё большое решение (закон, смотр, собрание, видение).</summary>
        private const int FactionDecisionMonths = 24;

        private EventState _s = new EventState();
        private readonly Queue<GameEventData> _queue = new Queue<GameEventData>();
        private bool _showing;
        private bool _subscribed;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // Новая сцена — новая партия: статические системы начинают с чистого листа (загрузка восстановит их позже)
            EmpireEffects.Reset();
            if (GetComponent<ThreatManager>() == null) gameObject.AddComponent<ThreatManager>();
            if (GetComponent<AutoExplore>() == null) gameObject.AddComponent<AutoExplore>();
            if (GetComponent<StarbaseVisuals>() == null) gameObject.AddComponent<StarbaseVisuals>();
            Vision.Reset();
            Contacts.Reset();
            if (GetComponent<ContactGreeter>() == null) gameObject.AddComponent<ContactGreeter>();
            if (GetComponent<VisionTicker>() == null) gameObject.AddComponent<VisionTicker>();
        }

        private void Start() => TrySubscribe();

        private void TrySubscribe()
        {
            if (_subscribed || TimeManager.Instance == null) return;
            TimeManager.Instance.OnDayPassed += HandleDay;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (_subscribed && TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
            if (Instance == this) Instance = null;
        }

        public static Color GetAnomalyAccent(AnomalyType type)
        {
            return type switch
            {
                AnomalyType.AlienFauna       => new Color(0.30f, 0.92f, 0.55f),
                AnomalyType.PrecursorRelic   => new Color(0.95f, 0.78f, 0.28f),
                AnomalyType.SpatialRift      => new Color(0.85f, 0.65f, 0.95f),
                AnomalyType.DistressSignal   => new Color(0.95f, 0.30f, 0.30f),
                AnomalyType.LeaderStory      => new Color(1.00f, 0.86f, 0.45f),
                AnomalyType.ColonyIncident   => new Color(0.45f, 0.85f, 1.00f),
                AnomalyType.MilitaryIncident => new Color(1.00f, 0.50f, 0.40f),
                AnomalyType.Diplomatic       => new Color(0.70f, 0.75f, 1.00f),
                _                            => new Color(0.20f, 0.92f, 0.82f)
            };
        }

        // ==================== ЗАПУСК ====================

        public void TriggerEventForSurvey(StarSystem system) => TriggerEventForSurvey(system, null);

        /// <summary>Аномалия при разведке: учёный на корабле может открыть особые варианты.</summary>
        public void TriggerEventForSurvey(StarSystem system, FleetData ship)
        {
            var ctx = new EventContext
            {
                System = system,
                Ship = ship,
                Leader = ship != null ? LeaderManager.Instance?.LeaderOfShip(ship.Id) : null
            };
            Fire(EventDatabase.RollSurvey(ctx, _s));
        }

        public void Schedule(string id, int days, EventContext ctx)
        {
            var p = new PendingEventSave { Id = id, Days = Mathf.Max(1, days) };
            if (ctx != null)
            {
                p.SystemId = ctx.System != null ? ctx.System.Id : -1;
                p.ShipId = ctx.Ship != null ? ctx.Ship.Id : -1;
                p.LeaderId = ctx.Leader != null ? ctx.Leader.Id : -1;
                p.PlanetIndex = ctx.Planet?.ParentSystem != null ? ctx.Planet.ParentSystem.Planets.IndexOf(ctx.Planet) : -1;
                p.Rival = ctx.Rival;
            }
            _s.Pending.Add(p);
        }

        private void Fire(GameEventData ev)
        {
            if (ev == null) return;
            if (_showing) _queue.Enqueue(ev);
            else Show(ev);
        }

        private void Show(GameEventData ev)
        {
            _showing = true;
            OnEventTriggered?.Invoke(ev);
        }

        /// <summary>Окно события закрыто — показать следующее из очереди (после короткой паузы на анимацию).</summary>
        public void NotifyClosed()
        {
            _showing = false;
            if (_queue.Count > 0) StartCoroutine(ShowNextLater());
        }

        private IEnumerator ShowNextLater()
        {
            _showing = true;
            yield return new WaitForSecondsRealtime(0.45f);
            if (_queue.Count > 0) Show(_queue.Dequeue());
            else _showing = false;
        }

        // ==================== КАЛЕНДАРЬ ====================

        private void HandleDay(int day, int month, int year)
        {
            if (!UIManager.IsGameStarted) return;

            for (int i = _s.Pending.Count - 1; i >= 0; i--)
            {
                var p = _s.Pending[i];
                if (--p.Days > 0) continue;
                _s.Pending.RemoveAt(i);
                Fire(EventDatabase.BuildChain(p.Id, Restore(p)));
            }

            if (day != 1) return;
            EmpireEffects.MonthlyTick();
            if (++_s.MonthsSinceFaction >= FactionDecisionMonths)
            {
                var decision = EventDatabase.FactionDecision();
                if (decision != null) { _s.MonthsSinceFaction = 0; Fire(decision); }
            }
            _s.MonthsSinceEvent++;
            if (_s.MonthsSinceEvent < QuietMonths || _showing || UnityEngine.Random.value > MonthlyChance) return;
            var ev = EventDatabase.RollMonthly(_s);
            if (ev == null) return;
            _s.MonthsSinceEvent = 0;
            Fire(ev);
        }

        private static EventContext Restore(PendingEventSave p)
        {
            var ctx = new EventContext { Rival = p.Rival, System = EmpireStats.GetSystem(p.SystemId) };
            if (ctx.System != null && p.PlanetIndex >= 0 && p.PlanetIndex < ctx.System.Planets.Count)
                ctx.Planet = ctx.System.Planets[p.PlanetIndex];
            var fm = FleetManager.Instance;
            if (fm != null && p.ShipId >= 0)
                foreach (var f in fm.AllFleets)
                    if (f?.Data != null && f.Data.Id == p.ShipId && !f.Data.Destroyed) { ctx.Ship = f.Data; break; }
            var lm = LeaderManager.Instance;
            if (lm != null && p.LeaderId >= 0)
                foreach (var l in lm.All)
                    if (l.Id == p.LeaderId) { ctx.Leader = l; break; }
            return ctx;
        }

        // ==================== СОХРАНЕНИЕ ====================

        public EventState CaptureState() => _s;

        public void RestoreState(EventState s)
        {
            _s = s ?? new EventState();
            _s.Fired ??= new List<string>();
            _s.Pending ??= new List<PendingEventSave>();
            _queue.Clear();
            _showing = false;
        }
    }
}
