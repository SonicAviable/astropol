using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Core
{
    /// <summary>
    /// Осада и захват систем — одинаково для игрока и ИИ.
    /// Если в системе с форпостом стоит военный флот стороны, воюющей с владельцем,
    /// а военного флота владельца там нет, — идёт осада. Когда осада доходит до конца,
    /// система (с колониями, районами и станциями) переходит к осаждающему.
    /// Ушли осаждающие — прогресс постепенно спадает; пришёл защитник — осада замирает.
    /// </summary>
    public class SiegeManager : MonoBehaviour
    {
        public static SiegeManager Instance { get; private set; }

        /// <summary>Система захвачена: система, прежний владелец, новый владелец.</summary>
        public static event Action<StarSystem, int, int> OnSystemCaptured;

        /// <summary>
        /// Идёт мирная передача системы по договору (а не захват): слушатели OnSystemCaptured
        /// не выдают достижений, не ломают звёздную базу и пишут в новостях «передаёт», а не «захватывает».
        /// </summary>
        public static bool IsPeacefulTransfer { get; private set; }

        public const float BaseSiegeDays = 12f * GamePace.SiegeMult;
        public const float SiegeDaysPerColony = 8f;
        public const float CapitalSiegeDays = 20f * GamePace.SiegeMult;
        public const float DecayPerDay = 2f;
        /// <summary>Во сколько раз дольше осада колонии без штурма.</summary>
        public const float StarvationMult = 3f;

        public class Siege
        {
            public int SystemId;
            public int Attacker;
            public float Progress;
            public bool Contested;   // защитник на месте — осада стоит
            public bool Active;      // осаждающие в системе прямо сейчас
            public bool BaseHolding; // звёздная база ещё в строю — сначала её нужно подавить
        }

        private readonly Dictionary<int, Siege> _sieges = new Dictionary<int, Siege>();
        public IEnumerable<Siege> Sieges => _sieges.Values;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed += HandleDay;
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
        }

        public Siege GetSiege(int systemId) => _sieges.TryGetValue(systemId, out var s) ? s : null;

        /// <summary>Штурм отбит — осада начинается заново.</summary>
        public void ResetSiege(int systemId)
        {
            if (_sieges.TryGetValue(systemId, out var s)) s.Progress = 0f;
        }

        public static float RequiredDays(StarSystem sys)
        {
            if (sys == null) return BaseSiegeDays;
            float days = BaseSiegeDays;
            foreach (var p in sys.Planets) if (p.Population > 0) days += SiegeDaysPerColony;
            if (IsCapital(sys)) days += CapitalSiegeDays;
            days *= Fortifications.SiegeMult(sys.Fortification);
            // Колонию осада берёт только «голодом» — втрое дольше; быстрый путь — карточный штурм
            if (StellarisClone.Cards.AssaultLink.IsColony(sys)) days *= StarvationMult;
            return days;
        }

        private static bool IsCapital(StarSystem sys)
        {
            if (sys.OwnerId == 0) return sys.Id == EconomyManager.PlayerCapitalId;
            var ai = AIEmpireManager.For(sys.OwnerId);
            return ai != null && sys.Id == ai.CapitalSystemId;
        }

        private void HandleDay(int day, int month, int year)
        {
            if (!UIManager.IsGameStarted) return;
            var fm = FleetManager.Instance;
            var systems = EmpireStats.Systems;
            if (fm == null || systems.Count == 0) return;

            // Военные флоты на орбитах: система → владелец → число кораблей
            var presence = new Dictionary<int, Dictionary<int, int>>();
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.Type != FleetType.Military) continue;
                if (d.State == FleetState.InHyperlane) continue;
                if (!presence.TryGetValue(d.CurrentSystemId, out var byOwner))
                    presence[d.CurrentSystemId] = byOwner = new Dictionary<int, int>();
                byOwner.TryGetValue(d.OwnerId, out int n);
                byOwner[d.OwnerId] = n + 1;
            }

            var captured = new List<(StarSystem sys, int attacker)>();

            foreach (var sys in systems)
            {
                if (sys.OwnerId < 0 || !sys.HasStarbase) { _sieges.Remove(sys.Id); continue; }

                presence.TryGetValue(sys.Id, out var here);
                bool defended = here != null && here.ContainsKey(sys.OwnerId);
                int attacker = -1, ships = 0;
                if (here != null)
                    foreach (var kv in here)
                    {
                        // Пираты и чудовища систем не захватывают — только грабят и сражаются
                        if (Threats.IsThreat(kv.Key)) continue;
                        if (Diplomacy.AtWar(kv.Key, sys.OwnerId) && kv.Value > ships) { attacker = kv.Key; ships = kv.Value; }
                    }

                _sieges.TryGetValue(sys.Id, out var siege);

                if (attacker < 0)
                {
                    if (siege == null) continue;
                    siege.Active = false;
                    siege.Contested = false;
                    siege.BaseHolding = false;
                    // Осада, которую прекратил мир, снимается сразу
                    if (!Diplomacy.AtWar(siege.Attacker, sys.OwnerId)) siege.Progress = 0f;
                    siege.Progress -= DecayPerDay;
                    if (siege.Progress <= 0f) _sieges.Remove(sys.Id);
                    continue;
                }

                if (siege == null || siege.Attacker != attacker)
                {
                    bool fresh = siege == null;
                    siege = new Siege { SystemId = sys.Id, Attacker = attacker };
                    _sieges[sys.Id] = siege;
                    if (fresh) AnnounceSiege(sys, attacker);
                }

                // Пока звёздная база в строю, осада не идёт: её нужно подавить в бою
                bool baseUp = CombatManager.Instance != null && CombatManager.Instance.IsStarbaseActive(sys.Id);
                siege.Active = true;
                siege.BaseHolding = baseUp;
                siege.Contested = defended || baseUp;
                if (siege.Contested) continue;

                // Несколько кораблей осаждают быстрее (до ×2)
                siege.Progress += Mathf.Min(2f, 1f + (ships - 1) * 0.25f);
                if (siege.Progress >= RequiredDays(sys)) captured.Add((sys, attacker));
                else if (attacker > 0) StellarisClone.Cards.AssaultLink.ThinkAI(sys, attacker);   // ИИ может пойти на штурм
            }

            foreach (var (sys, attacker) in captured)
                Capture(sys, attacker);
        }

        private static void AnnounceSiege(StarSystem sys, int attacker)
        {
            // Нашу систему осаждают — тревога (сирена и вспышка); осаждаем мы — боевой гул
            if (sys.OwnerId == 0) { SFXManager.Play(Sfx.RedAlert); AlarmFeedback.Attack(); }
            else if (attacker == 0) SFXManager.Play(Sfx.SiegeStart);
            if (sys.OwnerId == 0)
                NotificationCenter.Show(Loc.T("Осада!"), Loc.F("Враг блокирует {0}. Пришлите военный флот, чтобы снять осаду", sys.Name),
                    NotificationCenter.Kind.Danger, 7f);
            else if (attacker == 0)
                NotificationCenter.Show(Loc.T("Осада началась"), Loc.F("{0}: держите флот на орбите до захвата ({1:0} дн.)", sys.Name, RequiredDays(sys)),
                    NotificationCenter.Kind.Info, 5f);
        }

        /// <summary>Передать систему осаждающему.</summary>
        public void Capture(StarSystem sys, int newOwner)
        {
            if (sys == null) return;
            int oldOwner = sys.OwnerId;
            sys.OwnerId = newOwner;
            sys.HasStarbase = true;
            sys.MarkSurveyedByAll();
            _sieges.Remove(sys.Id);

            // Незаконченные стройки в захваченной системе прерываются
            var fm = FleetManager.Instance;
            if (fm != null)
                foreach (var f in fm.AllFleets)
                {
                    var d = f?.Data;
                    if (d == null || d.OwnerId == newOwner) continue;
                    if (d.State == FleetState.Constructing && d.BuildTargetSystemId == sys.Id)
                    {
                        d.State = FleetState.Orbiting;
                        d.BuildTargetSystemId = -1;
                    }
                }

            int colonies = 0, pop = 0;
            foreach (var p in sys.Planets) if (p.Population > 0) { colonies++; pop += p.Population; }
            string extra = colonies > 0 ? Loc.F(" · колоний: {0}, население {1}", colonies, pop) : "";

            if (oldOwner == 0)
                NotificationCenter.Show(Loc.T("Система потеряна"), Loc.F("{0} захвачена противником{1}", sys.Name, extra), NotificationCenter.Kind.Danger, 9f);
            else if (newOwner == 0)
                NotificationCenter.Show(Loc.T("Система захвачена"), Loc.F("{0} теперь ваша{1}", sys.Name, extra), NotificationCenter.Kind.Success, 7f);

            GalaxyView.Instance?.RefreshTerritoryVisuals();
            EconomyManager.Instance?.RecalculateAll();
            TechnologyManager.Instance?.RecountPopulation();
            CasusBelli.OnSystemCaptured(sys, oldOwner, newOwner);
            OnSystemCaptured?.Invoke(sys, oldOwner, newOwner);
        }

        /// <summary>
        /// Мирная передача системы по договору: колонии, районы и звёздная база целиком переходят к новому владельцу,
        /// незавершённые стройки прежнего владельца отменяются.
        /// </summary>
        public static void Transfer(StarSystem sys, int newOwner)
        {
            if (sys == null || sys.OwnerId == newOwner) return;
            int oldOwner = sys.OwnerId;
            sys.OwnerId = newOwner;
            sys.HasStarbase = true;
            sys.MarkSurveyedByAll();
            Instance?._sieges.Remove(sys.Id);

            var fm = FleetManager.Instance;
            if (fm != null)
                foreach (var f in fm.AllFleets)
                {
                    var d = f?.Data;
                    if (d == null || d.OwnerId == newOwner) continue;
                    if (d.State == FleetState.Constructing && d.BuildTargetSystemId == sys.Id)
                    {
                        d.State = FleetState.Orbiting;
                        d.BuildTargetSystemId = -1;
                    }
                }

            if (oldOwner == 0)
                NotificationCenter.Show(Loc.T("Система передана"), Loc.F("{0} по договору переходит к {1}", sys.Name, Contacts.NameForPlayer(newOwner)), NotificationCenter.Kind.Info, 7f);
            else if (newOwner == 0)
                NotificationCenter.Show(Loc.T("Система получена"), Loc.F("{0} по договору теперь ваша", sys.Name), NotificationCenter.Kind.Success, 7f);

            GalaxyView.Instance?.RefreshTerritoryVisuals();
            EconomyManager.Instance?.RecalculateAll();
            TechnologyManager.Instance?.RecountPopulation();
            IsPeacefulTransfer = true;
            try { OnSystemCaptured?.Invoke(sys, oldOwner, newOwner); }
            finally { IsPeacefulTransfer = false; }
        }

        /// <summary>
        /// Система отделилась после долгого бунта: становится ничьей, звёздная база переходит повстанцам и разоружается.
        /// Остальные системы узнают об этом через OnSystemCaptured (новый владелец −1).
        /// </summary>
        public void Secede(StarSystem sys)
        {
            if (sys == null || sys.OwnerId < 0) return;
            int oldOwner = sys.OwnerId;
            sys.OwnerId = -1;
            sys.HasStarbase = false;
            _sieges.Remove(sys.Id);

            GalaxyView.Instance?.RefreshTerritoryVisuals();
            EconomyManager.Instance?.RecalculateAll();
            TechnologyManager.Instance?.RecountPopulation();
            OnSystemCaptured?.Invoke(sys, oldOwner, -1);
        }

        /// <summary>Мир — все осады между сторонами снимаются.</summary>
        public void ClearSieges(int a, int b)
        {
            var remove = new List<int>();
            foreach (var kv in _sieges)
            {
                var sys = EmpireStats.GetSystem(kv.Key);
                int owner = sys != null ? sys.OwnerId : -1;
                if ((kv.Value.Attacker == a && owner == b) || (kv.Value.Attacker == b && owner == a)) remove.Add(kv.Key);
            }
            foreach (int id in remove) _sieges.Remove(id);
        }

        // ==================== СОХРАНЕНИЕ ====================

        public List<SiegeSave> CaptureState()
        {
            var list = new List<SiegeSave>();
            foreach (var s in _sieges.Values)
                list.Add(new SiegeSave { System = s.SystemId, Attacker = s.Attacker, Progress = s.Progress });
            return list;
        }

        public void RestoreState(List<SiegeSave> list)
        {
            _sieges.Clear();
            if (list == null) return;
            foreach (var s in list)
                _sieges[s.System] = new Siege { SystemId = s.System, Attacker = s.Attacker, Progress = s.Progress };
        }
    }
}
