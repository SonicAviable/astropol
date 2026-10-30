using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>Помесячная история одной империи (для графиков «Итогов партии»).</summary>
    [Serializable]
    public class ChronicleTrack
    {
        public int Owner;
        public string Name;
        public Color Color;
        public List<int> Score = new List<int>(), Systems = new List<int>(), Population = new List<int>(),
                         Techs = new List<int>(), Power = new List<int>();
    }

    /// <summary>Памятное событие партии: дата, что случилось, хорошо ли это для игрока, где.</summary>
    [Serializable]
    public class ChronicleEvent
    {
        public int Year, Month;
        /// <summary>0 — нейтральное, 1 — успех игрока, 2 — потеря.</summary>
        public int Tone;
        public string Text;
        public int SystemId = -1;
    }

    [Serializable]
    public class ChronicleSave
    {
        public int StartYear, StartMonth;
        public List<ChronicleTrack> Tracks = new List<ChronicleTrack>();
        public List<ChronicleEvent> Events = new List<ChronicleEvent>();
        public int ShipsLost, EnemyShipsDestroyed, SystemsCaptured, SystemsLost, AssaultsWon, AssaultsLost,
                   DefensesHeld, Achievements, Contacts, EventsSeen, Wars;
        public int PeakSystems, PeakPopulation, PeakPower;
    }

    /// <summary>
    /// Летопись партии: раз в месяц снимает показатели каждой империи (очки, системы, население, технологии, мощь флота),
    /// считает итоги игрока (корабли, захваты, штурмы, достижения) и запоминает ключевые события. Всё это показывает
    /// окно «Итоги партии» — в конце игры и из меню паузы в любой момент. Сохраняется вместе с партией.
    /// Новая партия — новая сцена и новый объект, поэтому отдельного сброса нет.
    /// </summary>
    public class GameChronicle : MonoBehaviour
    {
        public static GameChronicle Instance { get; private set; }
        public const int MaxEvents = 80;

        private ChronicleSave _s = new ChronicleSave();
        private bool _subscribed;

        public ChronicleSave Data => _s;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start() => Subscribe();

        private void Subscribe()
        {
            if (_subscribed || TimeManager.Instance == null) return;
            _subscribed = true;
            TimeManager.Instance.OnDayPassed += HandleDay;
            CombatManager.OnShipDestroyed += HandleShipDestroyed;
            SiegeManager.OnSystemCaptured += HandleCaptured;
            Achievements.OnUnlocked += HandleAchievement;
            Contacts.OnFirstContact += HandleContact;
            AnomalyEventSystem.OnEventTriggered += HandleEvent;
            StellarisClone.Cards.AssaultLink.OnApplied += HandleAssault;
        }

        private void OnDestroy()
        {
            if (_subscribed)
            {
                if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
                CombatManager.OnShipDestroyed -= HandleShipDestroyed;
                SiegeManager.OnSystemCaptured -= HandleCaptured;
                Achievements.OnUnlocked -= HandleAchievement;
                Contacts.OnFirstContact -= HandleContact;
                AnomalyEventSystem.OnEventTriggered -= HandleEvent;
                StellarisClone.Cards.AssaultLink.OnApplied -= HandleAssault;
            }
            if (Instance == this) Instance = null;
        }

        // ==================== СНИМКИ ====================

        private void HandleDay(int day, int month, int year)
        {
            if (!UIManager.IsGameStarted) return;
            if (_s.Tracks.Count == 0 || _s.Tracks[0].Score.Count == 0) { _s.StartYear = year; _s.StartMonth = month; Sample(); return; }
            if (day == 1) Sample();
        }

        /// <summary>Снять показатели всех империй за этот месяц.</summary>
        private void Sample()
        {
            SampleOwner(0, PlayerName(), FleetIndicator.OwnerColor(0));
            foreach (var ai in AIEmpireManager.All)
                if (ai != null) SampleOwner(ai.OwnerId, ai.AIName, FleetIndicator.OwnerColor(ai.OwnerId));

            var me = Track(0);
            int n = me.Score.Count - 1;
            if (n >= 0)
            {
                _s.PeakSystems = Mathf.Max(_s.PeakSystems, me.Systems[n]);
                _s.PeakPopulation = Mathf.Max(_s.PeakPopulation, me.Population[n]);
                _s.PeakPower = Mathf.Max(_s.PeakPower, me.Power[n]);
            }

            // Войны игрока: считаем начало каждой (переход «мир → война» между снимками)
            foreach (var ai in AIEmpireManager.All)
            {
                if (ai == null) continue;
                bool war = ai.AtWar;
                if (war && !_atWar.Contains(ai.OwnerId))
                {
                    _s.Wars++;
                    AddEvent(Loc.F("Война с империей «{0}»", ai.AIName), 2, -1);
                }
                if (war) _atWar.Add(ai.OwnerId); else _atWar.Remove(ai.OwnerId);
            }
        }

        private readonly HashSet<int> _atWar = new HashSet<int>();

        private void SampleOwner(int owner, string name, Color color)
        {
            var t = Track(owner);
            if (t == null)
            {
                t = new ChronicleTrack { Owner = owner, Name = name, Color = color };
                // Империя появилась позже начала — догоняем нулями, чтобы графики шли в ногу
                int len = _s.Tracks.Count > 0 ? _s.Tracks[0].Score.Count : 0;
                for (int i = 0; i < len; i++) { t.Score.Add(0); t.Systems.Add(0); t.Population.Add(0); t.Techs.Add(0); t.Power.Add(0); }
                _s.Tracks.Add(t);
            }
            t.Name = name;
            t.Color = color;
            t.Score.Add(EmpireStats.Score(owner).Total);
            t.Systems.Add(EmpireStats.SystemCount(owner));
            t.Population.Add(EmpireStats.Population(owner));
            t.Techs.Add(EmpireStats.TechCount(owner));
            t.Power.Add(Mathf.RoundToInt(EmpireStats.MilitaryPower(owner)));
        }

        public ChronicleTrack Track(int owner)
        {
            foreach (var t in _s.Tracks) if (t.Owner == owner) return t;
            return null;
        }

        public static string PlayerName()
        {
            var f = UIManager.Instance != null ? UIManager.Instance.SelectedFaction : null;
            return f != null && !string.IsNullOrEmpty(f.Name) ? f.Name : Loc.T("Ваша империя");
        }

        // ==================== СОБЫТИЯ ====================

        private void HandleShipDestroyed(FleetData d, int killer)
        {
            if (d == null) return;
            if (d.OwnerId == 0) _s.ShipsLost++;
            else if (killer == 0) _s.EnemyShipsDestroyed++;
        }

        private void HandleCaptured(StarSystem sys, int oldOwner, int newOwner)
        {
            if (sys == null) return;
            if (newOwner == 0 && oldOwner != 0)
            {
                _s.SystemsCaptured++;
                AddEvent(Loc.F("Захвачена система {0}", sys.Name), 1, sys.Id);
            }
            else if (oldOwner == 0 && newOwner != 0)
            {
                _s.SystemsLost++;
                AddEvent(Loc.F("Потеряна система {0}", sys.Name), 2, sys.Id);
            }
        }

        private void HandleAchievement(Achievements.Def d)
        {
            if (d == null) return;
            _s.Achievements++;
            AddEvent(Loc.F("Достижение «{0}»", d.Name), 1, -1);
        }

        private void HandleContact(int a, int b)
        {
            int other = a == 0 ? b : b == 0 ? a : -1;
            if (other < 0) return;
            _s.Contacts++;
            var ai = AIEmpireManager.For(other);
            if (ai != null) AddEvent(Loc.F("Первый контакт: {0}", ai.AIName), 0, -1);
        }

        private void HandleEvent(GameEventData e) => _s.EventsSeen++;

        private void HandleAssault(StellarisClone.Cards.BattleSetup setup, StellarisClone.Cards.BattleResult r)
        {
            if (setup == null || r == null) return;
            var sys = EmpireStats.GetSystem(setup.SystemId);
            string name = sys != null ? sys.Name : "";
            if (setup.Attacker.Owner == 0)
            {
                if (r.AttackerWon) { _s.AssaultsWon++; AddEvent(Loc.F("Колония {0} взята штурмом", name), 1, setup.SystemId); }
                else { _s.AssaultsLost++; AddEvent(Loc.F("Штурм колонии {0} отбит", name), 2, setup.SystemId); }
            }
            else if (setup.Defender.Owner == 0)
            {
                if (r.AttackerWon) AddEvent(Loc.F("Враг взял колонию {0} штурмом", name), 2, setup.SystemId);
                else { _s.DefensesHeld++; AddEvent(Loc.F("Колония {0} отбила штурм", name), 1, setup.SystemId); }
            }
        }

        /// <summary>Добавить запись в летопись (например, из целей империи).</summary>
        public void AddEvent(string text, int tone, int systemId)
        {
            var t = TimeManager.Instance;
            _s.Events.Add(new ChronicleEvent { Year = t != null ? t.Year : 0, Month = t != null ? t.Month : 0, Tone = tone, Text = text, SystemId = systemId });
            if (_s.Events.Count > MaxEvents) _s.Events.RemoveAt(0);
        }

        // ==================== СОХРАНЕНИЕ ====================

        public ChronicleSave CaptureState() => _s;

        public void RestoreState(ChronicleSave s)
        {
            _s = s ?? new ChronicleSave();
            if (_s.Tracks == null) _s.Tracks = new List<ChronicleTrack>();
            if (_s.Events == null) _s.Events = new List<ChronicleEvent>();
            _atWar.Clear();
            foreach (var ai in AIEmpireManager.All) if (ai != null && ai.AtWar) _atWar.Add(ai.OwnerId);
        }
    }
}
