using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Rendering;
using StellarisClone.Generation;
using StellarisClone.Core.Audio;

namespace StellarisClone.Core
{
    /// <summary>
    /// Сражения в духе Stellaris.
    ///   • Бой идёт в ИГРОВОМ времени раундами по 0,1 дня: пауза останавливает бой, скорость ускоряет.
    ///   • Сражаются все стороны, находящиеся в состоянии войны, в одной системе: военные корабли
    ///     и звёздная база владельца системы. Гражданские суда — цели, но не стреляют.
    ///   • Цель выбирается с весом по размеру (крупные корабли и база — чаще), огонь «прилипает» к цели.
    ///   • Слои защиты: щиты → броня → корпус; кинетика ломает щиты, энергия — броню, ракеты бьют всё.
    ///   • Корабль с нулевым корпусом может выйти из боя (корвет — чаще, эсминец — реже) и уйти
    ///     на ремонт с остатком прочности, иначе погибает.
    ///   • Звёздная база, потерявшая корпус, выводится из строя — только после этого возможна осада.
    ///   • По итогам — отчёт о битве; на карте над системой горит значок сражения.
    ///   • Перехват: военный флот, прибывший в систему с вооружённым врагом или действующей вражеской
    ///     базой, останавливается и принимает бой — сквозь противника не пролететь, «догонялок» нет.
    ///   • Бой неспешный: сначала фаза сближения (бьют ракеты, лучи ещё не достают), затем огневой контакт.
    ///     Стороны выстраиваются друг против друга, корабли маневрируют на своих позициях.
    ///   • Экстренный прыжок — только после разгона ГПД (3 дня боя) и стоит 10% корпуса.
    ///   • Перехват в коридоре: враждебные военные флоты, встретившиеся в одном гиперкоридоре
    ///     (навстречу друг другу или когда догоняющий настиг цель), выходят из прыжка в ближайшей
    ///     системе и сражаются там. Аварийный прыжок из боя перехватить нельзя.
    ///   • Тактика стороны (CombatManager.Tactics.cs): стойка и приоритет целей; игрок выбирает на панели боя,
    ///     ИИ — сам по соотношению сил. Панель боя — CombatManager.Hud.cs.
    /// </summary>
    public partial class CombatManager : MonoBehaviour
    {
        public static CombatManager Instance { get; private set; }

        public const float KineticVsShield = 1.45f;
        public const float KineticVsArmor = 0.65f;
        public const float EnergyVsShield = 0.70f;
        public const float EnergyVsArmor = 1.45f;
        public const float ExplosiveVsAll = 1.05f;

        /// <summary>Длина боевого раунда, игровых дней.</summary>
        public const float RoundDays = 0.1f;
        /// <summary>
        /// Выстрелов в день на единицу скорострельности орудия. Раньше урон и скорострельность всех
        /// орудий корабля перемножались суммами; теперь каждое орудие стреляет само, а темп
        /// поднят вдвое, чтобы корвет с двумя орудиями воевал так же быстро, как прежде.
        /// </summary>
        public const float ShotsPerDay = 1f;
        /// <summary>Фаза сближения в начале боя (дней): лучи почти не достают, кинетика — вполсилы.</summary>
        public const float ApproachDays = 0.8f;
        /// <summary>Разгон гиперпривода в бою до экстренного прыжка, дней.</summary>
        public const float FtlSpoolDays = 3f;
        /// <summary>Цена экстренного прыжка — доля максимального корпуса.</summary>
        private const float FtlHullCost = 0.10f;
        private const float FtlSuccess = 0.8f;
        /// <summary>Сколько дней без перестрелки, чтобы бой считался завершённым.</summary>
        private const float BattleEndQuietDays = 0.5f;

        /// <summary>Корабль уничтожен в бою (владелец, кем уничтожен).</summary>
        public static event System.Action<FleetData, int> OnShipDestroyed;

        private GalaxyGenerator _generator;
        private readonly List<FloatingDmg> _floaters = new List<FloatingDmg>();
        private GameObject _fxRoot;
        private Font _font;
        private float _clock;

        private class FloatingDmg
        {
            public TextMesh Mesh;
            public float Life;
            public Vector3 Velocity;
        }

        // ==================== БИТВЫ ====================

        public class SideStats
        {
            public int Owner;
            public int StartShips, Ships;
            public float StartHp, Hp, Power;
            public float DamageDealt, DamageTaken;
            public int Lost, Disengaged, Kills;
            public bool HasStarbase;
            /// <summary>Корабли по классам: 0 корветы, 1 фрегаты, 2 эсминцы, 3 гражданские.</summary>
            public readonly int[] ClassAlive = new int[4], ClassStart = new int[4];
            /// <summary>Урон в день без учёта уклонения и слоёв защиты — для прогноза исхода.</summary>
            public float Dps;
            public float Shields, MaxShields;
        }

        public class Battle
        {
            public int SystemId;
            public float Days;
            public float QuietDays;
            public float BaseAngle;
            public readonly List<int> SideOrder = new List<int>();
            public bool Approach => Days < ApproachDays;
            public readonly Dictionary<int, SideStats> Sides = new Dictionary<int, SideStats>();
            public bool PlayerInvolved => Sides.ContainsKey(0);
            public GameObject Marker;
            /// <summary>Тактика сторон: стойка и приоритет целей (владелец → выбор).</summary>
            public readonly Dictionary<int, Stance> Stances = new Dictionary<int, Stance>();
            public readonly Dictionary<int, TargetPriority> Priorities = new Dictionary<int, TargetPriority>();
            /// <summary>Общая цель стороны в «Фокусе огня» (владелец → ключ цели).</summary>
            public readonly Dictionary<int, int> FocusTarget = new Dictionary<int, int>();
            /// <summary>Лента событий боя (новые — в конце).</summary>
            public readonly List<LogEntry> Log = new List<LogEntry>();
            public float AiThinkIn;
            public bool ContactAnnounced;
        }

        private readonly Dictionary<int, Battle> _battles = new Dictionary<int, Battle>();
        public IEnumerable<Battle> Battles => _battles.Values;
        public Battle GetBattle(int systemId) => _battles.TryGetValue(systemId, out var b) ? b : null;

        // ==================== ЗВЁЗДНЫЕ БАЗЫ ====================

        public class Starbase
        {
            public int SystemId;
            public float Hull, Armor, Shields;
            public float MaxHull, MaxArmor, MaxShields;
            public float Damage = 9f, FireRate = 1f;
            public WeaponDamageType Weapon = WeaponDamageType.Kinetic;
            public bool Disabled;
            public float Cooldown;
            public int TargetFleetId = -1;
            /// <summary>Пробуждение сада (Конклав Тэрра'ан): сколько дней ещё действует и перезарядка.</summary>
            public int GardenDays, GardenCooldown;
            public float Integrity => (Hull + Armor) / Mathf.Max(1f, MaxHull + MaxArmor);
        }

        private readonly Dictionary<int, Starbase> _starbases = new Dictionary<int, Starbase>();

        // Цели кораблей (огонь «прилипает»): id стрелка → id цели (-1000-sysId — звёздная база)
        private readonly Dictionary<int, int> _targets = new Dictionary<int, int>();
        // Корабли, вышедшие из боя: их не трогают, пока они не покинут систему
        private readonly HashSet<int> _disengaged = new HashSet<int>();
        // Ушедшие аварийным прыжком: в коридоре их не перехватывают, пока не долетят
        private readonly HashSet<int> _escaping = new HashSet<int>();
        // Взаимный порядок пар враждебных флотов в коридоре: смена знака — флоты встретились
        private readonly Dictionary<(int, int, int, int), int> _laneOrder = new Dictionary<(int, int, int, int), int>();
        // Сколько дней корабль уже в бою (разгон ГПД, фаза сближения)
        private readonly Dictionary<int, float> _combatDays = new Dictionary<int, float>();
        // Приказ на отступление: прыжок, как только ГПД разгонится (id → куда лететь дальше)
        private readonly Dictionary<int, int> _pendingFtl = new Dictionary<int, int>();
        // Позиции в боевом строю и цели — для манёвров FleetView
        private struct Station { public int SystemId; public Vector3 Pos; public int TargetKey; }
        private readonly Dictionary<int, Station> _stations = new Dictionary<int, Station>();
        private readonly Dictionary<int, FleetView> _viewById = new Dictionary<int, FleetView>();

        // ==================== ЖИЗНЕННЫЙ ЦИКЛ ====================

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _font = GameFont.Regular;
        }

        private void Start()
        {
            _generator = FindFirstObjectByType<GalaxyGenerator>();
            _fxRoot = new GameObject("CombatFxRoot");
            _fxRoot.transform.SetParent(transform, false);
            BuildHud();
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed += HandleDay;
            SiegeManager.OnSystemCaptured += HandleCaptured;
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
            SiegeManager.OnSystemCaptured -= HandleCaptured;
        }

        private void Update()
        {
            if (!UIManager.IsGameStarted) { HideHud(); return; }
            float dt = Time.deltaTime;
            var tm = TimeManager.Instance;
            int speed = tm != null ? tm.CurrentSpeed : 1;

            if (speed > 0)
            {
                float secondsPerDay = tm != null ? tm.SecondsPerDay : GamePace.SecondsPerDay;
                _clock += dt * speed / Mathf.Max(0.05f, secondsPerDay);
                int rounds = 0;
                while (_clock >= RoundDays && rounds < 8)
                {
                    _clock -= RoundDays;
                    CombatRound(RoundDays);
                    rounds++;
                }
                if (_clock > RoundDays * 8) _clock = 0f;
            }

            TickFloaters(Time.unscaledDeltaTime);
            UpdateMarkers();
            RefreshHud();
        }

        public static bool AreHostile(FleetData a, FleetData b)
        {
            if (a == null || b == null) return false;
            if (a.OwnerId == b.OwnerId) return false;
            if (a.Destroyed || b.Destroyed) return false;
            return Diplomacy.AtWar(a.OwnerId, b.OwnerId);
        }

        // ==================== РАУНД БОЯ ====================

        private struct Combatant
        {
            public FleetView Fleet;     // null — звёздная база
            public Starbase Base;
            public int Owner;
            public bool Armed => Fleet != null ? Fleet.Data.Type == FleetType.Military && Fleet.Data.Damage > 0f : !Base.Disabled;
            public bool Alive => Fleet != null ? Fleet.Data != null && !Fleet.Data.Destroyed : !Base.Disabled;
            public int Key => Fleet != null ? Fleet.Data.Id : -1000 - Base.SystemId;
            public Vector3 Position(GalaxyGenerator g) => Fleet != null ? Fleet.transform.position : StarbaseVisuals.SiteFor(g.Systems[Base.SystemId].Position) + Vector3.up * 0.3f;
        }

        private void CombatRound(float dt)
        {
            if (_generator == null) return;
            var fm = FleetManager.Instance;
            if (fm == null) return;

            // Флоты на орбитах по системам
            var bySystem = new Dictionary<int, List<FleetView>>();
            _viewById.Clear();
            foreach (var fv in fm.AllFleets)
            {
                var d = fv?.Data;
                if (d == null || d.Destroyed) continue;
                _viewById[d.Id] = fv;
                if (d.State == FleetState.InHyperlane)
                {
                    if (_disengaged.Remove(d.Id)) _escaping.Add(d.Id);
                    continue;
                }
                _escaping.Remove(d.Id);
                if (_disengaged.Contains(d.Id)) continue;
                if (!bySystem.TryGetValue(d.CurrentSystemId, out var list)) bySystem[d.CurrentSystemId] = list = new List<FleetView>();
                list.Add(fv);
            }

            CheckLaneEncounters(fm, bySystem);

            var engaged = new HashSet<int>();
            var activeSystems = new HashSet<int>();
            _stations.Clear();

            foreach (var kv in bySystem)
            {
                int sysId = kv.Key;
                if (sysId < 0 || sysId >= _generator.Systems.Count) continue;
                var sys = _generator.Systems[sysId];

                // Участники: корабли + действующая звёздная база владельца
                var parts = new List<Combatant>();
                foreach (var fv in kv.Value) parts.Add(new Combatant { Fleet = fv, Owner = fv.Data.OwnerId });
                var sb = GetStarbase(sysId);
                if (sb != null && !sb.Disabled) parts.Add(new Combatant { Base = sb, Owner = sys.OwnerId });

                if (!HasFight(parts)) continue;

                activeSystems.Add(sysId);
                var battle = EnsureBattle(sysId, parts);
                battle.QuietDays = 0f;
                battle.Days += dt;
                if (!battle.ContactAnnounced && battle.Days >= ApproachDays)
                {
                    battle.ContactAnnounced = true;
                    AddLog(battle, Loc.T("Огневой контакт — в бой вступили все орудия"), LogKind.Phase);
                }
                if ((battle.AiThinkIn -= dt) <= 0f) { battle.AiThinkIn = 1f; ThinkAiTactics(battle); }

                foreach (var p in parts)
                {
                    if (p.Fleet == null) continue;
                    if (HasEnemy(parts, p.Owner))
                    {
                        p.Fleet.Data.InCombat = true;
                        engaged.Add(p.Fleet.Data.Id);
                        if (!_combatDays.TryGetValue(p.Fleet.Data.Id, out float cd))
                        {
                            // Вступая в бой, орудия открывают огонь вразнобой, а не единым залпом
                            foreach (var w in p.Fleet.Data.Weapons)
                                if (w.FireRate > 0f) w.Cooldown = Random.value / (w.FireRate * ShotsPerDay);
                        }
                        _combatDays[p.Fleet.Data.Id] = cd + dt;
                    }
                }

                // Огонь
                for (int i = 0; i < parts.Count; i++)
                {
                    var shooter = parts[i];
                    if (!shooter.Alive || !shooter.Armed) continue;
                    FireRound(shooter, parts, battle, dt);
                }

                UpdateSideStats(battle, parts);
                AssignStations(battle, parts, sys);
            }

            // Корабли, которые больше не в бою
            foreach (var fv in fm.AllFleets)
                if (fv?.Data != null && fv.Data.InCombat && !engaged.Contains(fv.Data.Id)) fv.Data.InCombat = false;
            if (_combatDays.Count > 0)
            {
                List<int> gone = null;
                foreach (var id in _combatDays.Keys) if (!engaged.Contains(id)) (gone ??= new List<int>()).Add(id);
                if (gone != null) foreach (var id in gone) { _combatDays.Remove(id); _pendingFtl.Remove(id); }
            }

            ProcessPendingFtl();

            // Завершение боёв
            var ended = new List<Battle>();
            foreach (var b in _battles.Values)
            {
                if (activeSystems.Contains(b.SystemId)) continue;
                b.QuietDays += dt;
                if (b.QuietDays >= BattleEndQuietDays) ended.Add(b);
            }
            foreach (var b in ended) EndBattle(b);
        }

        // ==================== ПЕРЕХВАТ В КОРИДОРЕ ====================

        /// <summary>Положение флота в коридоре: 0 — у системы lo, 1 — у другого конца.</summary>
        private static float LanePos(FleetData d, int lo)
        {
            float t = Mathf.Clamp01(1f - d.DaysRemainingInTransit / Mathf.Max(0.01f, d.TotalDaysForTransit));
            return d.CurrentSystemId == lo ? t : 1f - t;
        }

        /// <summary>
        /// Враждебные военные флоты в одном коридоре: встречные — когда поравнялись, попутные — когда
        /// догоняющий настиг. Оба выходят из прыжка в ближайшей к месту встречи системе; там их держит перехват,
        /// и начинается бой. Флоты, вышедшие из боя аварийным прыжком, не перехватываются.
        /// </summary>
        private void CheckLaneEncounters(FleetManager fm, Dictionary<int, List<FleetView>> bySystem)
        {
            var lanes = new Dictionary<long, List<FleetView>>();
            foreach (var fv in fm.AllFleets)
            {
                var d = fv?.Data;
                if (d == null || d.Destroyed || d.State != FleetState.InHyperlane || d.TargetSystemId < 0) continue;
                if (d.Type != FleetType.Military || _escaping.Contains(d.Id)) continue;
                long key = (long)Mathf.Min(d.CurrentSystemId, d.TargetSystemId) * 100000L + Mathf.Max(d.CurrentSystemId, d.TargetSystemId);
                if (!lanes.TryGetValue(key, out var list)) lanes[key] = list = new List<FleetView>();
                list.Add(fv);
            }

            var seen = new HashSet<(int, int, int, int)>();
            List<(FleetView a, FleetView b, int sys)> meets = null;
            foreach (var kv in lanes)
            {
                var list = kv.Value;
                if (list.Count < 2) continue;
                list.Sort((x, y) => x.Data.Id.CompareTo(y.Data.Id));
                int lo = (int)(kv.Key / 100000L), hi = (int)(kv.Key % 100000L);
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        FleetData a = list[i].Data, b = list[j].Data;
                        if (!Diplomacy.AtWar(a.OwnerId, b.OwnerId) || (a.Damage <= 0f && b.Damage <= 0f)) continue;
                        if (a.OwnerId == Threats.LeviathanOwner || b.OwnerId == Threats.LeviathanOwner) continue;
                        float pa = LanePos(a, lo), pb = LanePos(b, lo);
                        var pk = (a.Id, b.Id, a.CurrentSystemId, b.CurrentSystemId);
                        seen.Add(pk);
                        int sign = pa < pb ? -1 : 1;
                        bool opposite = a.CurrentSystemId != b.CurrentSystemId;
                        bool met = opposite && Mathf.Abs(pa - pb) < 0.04f;
                        if (_laneOrder.TryGetValue(pk, out int prev) && prev != sign) met = true;
                        _laneOrder[pk] = sign;
                        if (met) (meets ??= new List<(FleetView, FleetView, int)>()).Add((list[i], list[j], (pa + pb) * 0.5f < 0.5f ? lo : hi));
                    }
            }

            if (_laneOrder.Count > seen.Count)
            {
                var stale = new List<(int, int, int, int)>();
                foreach (var k in _laneOrder.Keys) if (!seen.Contains(k)) stale.Add(k);
                foreach (var k in stale) _laneOrder.Remove(k);
            }
            if (meets == null) return;

            foreach (var m in meets)
            {
                // Флот мог уже выйти из прыжка из-за другой встречи в этом раунде
                if (m.a.Data.State == FleetState.InHyperlane) m.a.DropOutOfLane(m.sys);
                if (m.b.Data.State == FleetState.InHyperlane) m.b.DropOutOfLane(m.sys);
                foreach (var fv in new[] { m.a, m.b })
                {
                    if (fv.Data.CurrentSystemId != m.sys || fv.Data.State == FleetState.InHyperlane) continue;
                    if (!bySystem.TryGetValue(m.sys, out var here)) bySystem[m.sys] = here = new List<FleetView>();
                    if (!here.Contains(fv)) here.Add(fv);
                }
                if ((m.a.Data.OwnerId == 0 || m.b.Data.OwnerId == 0) && _generator != null)
                {
                    var enemy = m.a.Data.OwnerId == 0 ? m.b.Data : m.a.Data;
                    NotificationCenter.Show(Loc.T("Перехват в коридоре"),
                        Loc.F("{0}: флоты сошлись в гиперкоридоре — бой у {1}", AIEmpireManager.NameOf(enemy.OwnerId, Loc.T("Противник")), _generator.Systems[m.sys].Name),
                        NotificationCenter.Kind.Danger, 5f);
                }
            }
        }

        // ==================== СТРОЙ ====================

        private static float StationRadius(FleetData d)
        {
            // Линии классов разнесены на длину корпуса с запасом — корабли одной стороны не наслаиваются
            if (d.Type != FleetType.Military) return 9.5f;
            return d.HullClass == ShipClass.Destroyer ? 7.8f : d.HullClass == ShipClass.Frigate ? 5.6f : 3.4f;
        }

        /// <summary>
        /// Стороны встают друг против друга (вторая — напротив первой, третья и четвёртая — с флангов),
        /// внутри стороны — линиями по классам: корветы впереди, эсминцы в глубине, гражданские в тылу.
        /// </summary>
        private void AssignStations(Battle battle, List<Combatant> parts, StarSystem sys)
        {
            if (battle.SideOrder.Count == 0 && sys.OwnerId >= 0)
                foreach (var p in parts) if (p.Owner == sys.OwnerId) { battle.SideOrder.Add(sys.OwnerId); break; }
            foreach (var p in parts)
                if (!battle.SideOrder.Contains(p.Owner)) battle.SideOrder.Add(p.Owner);

            var groups = new Dictionary<long, List<FleetData>>();
            foreach (var p in parts)
            {
                if (p.Fleet == null || !p.Alive) continue;
                var d = p.Fleet.Data;
                int cls = d.Type != FleetType.Military ? 3 : (int)d.HullClass;
                long key = (long)p.Owner * 8 + cls;
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = new List<FleetData>();
                g.Add(d);
            }

            const int PerRow = 6;
            // Шаг в строю: размах крыла корабля (до 2.4) плюс зазор; ряды — на длину корпуса
            const float LateralStep = 2.8f, RowStep = 2.4f;
            foreach (var g in groups.Values)
            {
                g.Sort((x, y) => x.Id.CompareTo(y.Id));
                int side = battle.SideOrder.IndexOf(g[0].OwnerId);
                float angle = battle.BaseAngle + SideAngle(side);
                var dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
                var perp = new Vector3(-dir.z, 0f, dir.x);
                for (int i = 0; i < g.Count; i++)
                {
                    var d = g[i];
                    int row = i / PerRow, col = i % PerRow;
                    int inRow = Mathf.Min(PerRow, g.Count - row * PerRow);
                    float lateral = (col - (inRow - 1) * 0.5f) * LateralStep;
                    float radial = StationRadius(d) * StanceRadiusMult(battle, d.OwnerId) + row * RowStep;
                    float lift = d.HullClass == ShipClass.Corvette ? 0.75f : d.HullClass == ShipClass.Frigate ? 0.5f : 0.3f;
                    var pos = sys.Position + dir * radial + perp * lateral + Vector3.up * (lift + (row % 2) * 0.35f);
                    _stations[d.Id] = new Station
                    {
                        SystemId = sys.Id,
                        Pos = pos,
                        TargetKey = _targets.TryGetValue(d.Id, out int t) ? t : int.MinValue
                    };
                }
            }
        }

        private static float SideAngle(int side) => side switch
        {
            0 => 0f, 1 => 180f, 2 => 90f, 3 => 270f, _ => 45f + 90f * (side - 4)
        };

        /// <summary>Место корабля в боевом строю и точка, куда он целится. false — корабль не в бою.</summary>
        public bool TryGetCombatStation(FleetData d, out Vector3 station, out Vector3 aim)
        {
            station = aim = Vector3.zero;
            if (d == null || !_stations.TryGetValue(d.Id, out var st) || st.SystemId != d.CurrentSystemId) return false;
            station = st.Pos;
            aim = _generator != null ? _generator.Systems[st.SystemId].Position : st.Pos;
            if (st.TargetKey >= 0)
            {
                if (_viewById.TryGetValue(st.TargetKey, out var tv) && tv != null) aim = tv.transform.position;
            }
            else if (st.TargetKey != int.MinValue) aim = StarbaseVisuals.SiteFor(aim) + Vector3.up * 0.3f;   // звёздная база
            return true;
        }

        // ==================== ПЕРЕХВАТ ====================

        /// <summary>
        /// Держит ли система этот флот: военный корабль не может пройти через систему, где стоит
        /// вооружённый враг или действует вражеская звёздная база, — он останавливается и принимает бой.
        /// Выходящие из боя (аварийный прыжок, отход на ремонт) не задерживаются.
        /// </summary>
        public bool IsInterdicted(FleetData d, int systemId)
        {
            if (d == null || d.Destroyed || d.Type != FleetType.Military || _generator == null) return false;
            if (systemId < 0 || systemId >= _generator.Systems.Count || _disengaged.Contains(d.Id)) return false;
            var sys = _generator.Systems[systemId];
            if (sys.OwnerId >= 0 && sys.OwnerId != d.OwnerId && Diplomacy.AtWar(d.OwnerId, sys.OwnerId) && IsStarbaseActive(systemId))
                return true;
            var fm = FleetManager.Instance;
            if (fm == null) return false;
            foreach (var fv in fm.AllFleets)
            {
                var o = fv?.Data;
                if (o == null || o.Destroyed || o == d || o.CurrentSystemId != systemId || o.State == FleetState.InHyperlane) continue;
                if (o.Type != FleetType.Military || o.Damage <= 0f || _disengaged.Contains(o.Id)) continue;
                if (o.OwnerId == Threats.LeviathanOwner) continue;   // стражи не преследуют проходящих — бьют только тех, кто остановился
                if (Diplomacy.AtWar(d.OwnerId, o.OwnerId)) return true;
            }
            return false;
        }

        /// <summary>Корабль выходит из боя (аварийный прыжок или отход на ремонт) — его не задерживают.</summary>
        public bool IsDisengaging(FleetData d) => d != null && _disengaged.Contains(d.Id);

        /// <summary>Сколько дней ещё разгоняться ГПД до экстренного прыжка (0 — готов).</summary>
        public float FtlReadyIn(FleetData d)
        {
            if (d == null) return FtlSpoolDays;
            _combatDays.TryGetValue(d.Id, out float days);
            return Mathf.Max(0f, FtlSpoolDays * StanceSpoolMult(GetBattle(d.CurrentSystemId), d.OwnerId) - days);
        }

        /// <summary>Отдан ли кораблю приказ на отступление (ждёт разгона ГПД).</summary>
        public bool IsRetreating(FleetData d) => d != null && _pendingFtl.ContainsKey(d.Id);

        private static bool HasFight(List<Combatant> parts)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (!parts[i].Armed) continue;
                for (int j = 0; j < parts.Count; j++)
                    if (i != j && Diplomacy.AtWar(parts[i].Owner, parts[j].Owner) && parts[j].Alive) return true;
            }
            return false;
        }

        private static bool HasEnemy(List<Combatant> parts, int owner)
        {
            foreach (var p in parts) if (p.Alive && Diplomacy.AtWar(owner, p.Owner)) return true;
            return false;
        }

        /// <summary>Каждое орудие корабля стреляет само: своим типом урона и со своей перезарядкой.</summary>
        private void FireRound(Combatant shooter, List<Combatant> parts, Battle battle, float dt)
        {
            if (shooter.Fleet != null)
            {
                var d = shooter.Fleet.Data;
                float rateMult = LeaderManager.AdmiralFireRateMult(d.OwnerId, battle.SystemId);
                _combatDays.TryGetValue(d.Id, out float inBattle);
                bool approach = inBattle < ApproachDays * StanceApproachMult(battle, d.OwnerId);
                foreach (var w in d.Weapons)
                    w.Cooldown = FireWeapon(shooter, parts, battle, dt, w.Damage, w.FireRate * rateMult * (approach ? ApproachRate(w.Type) : 1f), w.Type, w.Cooldown);
            }
            else
            {
                var b = shooter.Base;
                b.Cooldown = FireWeapon(shooter, parts, battle, dt, b.Damage, b.FireRate, b.Weapon, b.Cooldown);
            }
        }

        /// <summary>На сближении ракеты бьют в полную силу, кинетика — вполсилы, лучи почти не достают.</summary>
        private static float ApproachRate(WeaponDamageType t) => t switch
        {
            WeaponDamageType.Explosive => 1f,
            WeaponDamageType.Kinetic => 0.5f,
            _ => 0.2f
        };

        private float FireWeapon(Combatant shooter, List<Combatant> parts, Battle battle, float dt,
                                 float damage, float fireRate, WeaponDamageType weapon, float cooldown)
        {
            if (damage <= 0f || fireRate <= 0f) return 0f;
            cooldown -= dt;
            int shots = 0;
            while (cooldown <= 0f && shots < 4)
            {
                var target = PickTarget(shooter, parts, battle);
                if (!target.HasValue) return 0f;
                Shoot(shooter, target.Value, damage, weapon, battle);
                cooldown += 1f / Mathf.Max(0.05f, fireRate * ShotsPerDay);
                shots++;
            }
            return cooldown;
        }

        /// <summary>
        /// Выбор цели: в «Фокусе огня» — общая цель стороны (самая повреждённая из приоритетных);
        /// иначе прежняя, если жива; иначе случайная с весом по размеру и приоритету целей.
        /// </summary>
        private Combatant? PickTarget(Combatant shooter, List<Combatant> parts, Battle battle)
        {
            int sKey = shooter.Key;
            if (StanceOf(battle, shooter.Owner) == Stance.Focus)
            {
                var focus = FocusPick(shooter.Owner, parts, battle);
                if (focus.HasValue) { _targets[sKey] = focus.Value.Key; return focus; }
            }
            if (_targets.TryGetValue(sKey, out int tKey))
                foreach (var p in parts)
                    if (p.Key == tKey && p.Alive && Diplomacy.AtWar(shooter.Owner, p.Owner)) return p;

            var prio = PriorityOf(battle, shooter.Owner);
            float total = 0f;
            foreach (var p in parts)
                if (p.Alive && Diplomacy.AtWar(shooter.Owner, p.Owner)) total += TargetWeight(p) * PriorityWeight(p, prio);
            if (total <= 0f) return null;

            float r = Random.value * total;
            foreach (var p in parts)
            {
                if (!p.Alive || !Diplomacy.AtWar(shooter.Owner, p.Owner)) continue;
                r -= TargetWeight(p) * PriorityWeight(p, prio);
                if (r <= 0f) { _targets[sKey] = p.Key; return p; }
            }
            return null;
        }

        private static float TargetWeight(Combatant p)
        {
            if (p.Fleet == null) return 4f;
            var d = p.Fleet.Data;
            if (d.Type != FleetType.Military) return 1f;
            return d.HullClass == ShipClass.Destroyer ? 5f : d.HullClass == ShipClass.Frigate ? 4f : 3f;
        }

        private void Shoot(Combatant shooter, Combatant target, float damage, WeaponDamageType weapon, Battle battle)
        {
            var ab = EmpireBonuses.For(shooter.Owner);
            Vector3 from = shooter.Position(_generator), to = target.Position(_generator);
            Color wc = WeaponColor(weapon);
            bool show = ShowFx(battle.SystemId, to);
            float size = HitSize(target);
            if (show) from = Muzzle(shooter, from, to);

            // Уклонение (у базы его нет)
            if (target.Fleet != null)
            {
                var db = EmpireBonuses.For(target.Owner);
                float accuracy = ab.Accuracy + (shooter.Fleet != null ? shooter.Fleet.Data.Accuracy : 0f);
                float evade = Mathf.Clamp01((target.Fleet.Data.Evasion * db.EvasionMult * StanceEvasionMult(battle, target.Owner)
                                             * LeaderManager.AdmiralEvasionMult(target.Owner, battle.SystemId) - accuracy) / 100f);
                if (Random.value < evade * 0.45f)
                {
                    if (show)
                    {
                        CombatFx.Instance.Fire(weapon, from, to, wc, false, CombatFx.Impact.None, size, null, FxTransform(shooter));
                        if (Random.value < 0.5f) SFXManager.PlayAt(WeaponSfx(weapon), from, 0.8f, GunPitch(shooter));
                    }
                    return;
                }
            }

            float raw = damage * ab.DamageMult(weapon);
            if (shooter.Fleet != null) raw *= LeaderManager.AdmiralDamageMult(shooter.Owner, battle.SystemId);
            if (target.Fleet != null) raw *= FactionTraits.IncomingDamage(target.Owner, weapon);   // биокора Конклава боится энергии
            raw *= StanceDamageMult(battle, shooter.Owner) * StanceIncomingMult(battle, target.Owner);
            float shieldBefore = target.Fleet != null ? target.Fleet.Data.ShieldPoints : target.Base.Shields;
            float armorBefore = target.Fleet != null ? target.Fleet.Data.ArmorPoints : target.Base.Armor;
            float dealt;
            if (target.Fleet != null) dealt = ApplyLayeredDamage(target.Fleet.Data, raw, weapon);
            else dealt = ApplyLayeredDamage(target.Base, raw, weapon);

            if (show)
            {
                float shieldAfter = target.Fleet != null ? target.Fleet.Data.ShieldPoints : target.Base.Shields;
                var impact = shieldBefore > 0f ? CombatFx.Impact.Shield : armorBefore > 0f ? CombatFx.Impact.Armor : CombatFx.Impact.Hull;
                CombatFx.Instance.Fire(weapon, from, to, wc, true, impact, size, FxTransform(target), FxTransform(shooter),
                                       shieldBefore > 0f && shieldAfter <= 0f);
                if (shieldBefore > 0f && shieldAfter <= 0f && (target.Fleet == null || target.Fleet.Data.HullClass == ShipClass.Destroyer))
                    AddLog(battle, Loc.F("{0}: щиты пробиты", (target.Fleet != null ? target.Fleet.Data.Name : Loc.T("Звёздная база"))), LogKind.Shield, target.Owner);
                ShotSound(weapon, from, to, shieldBefore > 0f, target.Fleet == null || target.Fleet.Data.HullClass == ShipClass.Destroyer, GunPitch(shooter));
                // Числа урона — изредка и только для крупных попаданий: картину боя рисуют эффекты
                if (dealt >= 12f && Random.value < 0.22f) SpawnFloater(to, $"-{dealt:0}", Color.Lerp(wc, Color.white, 0.35f));
            }

            Side(battle, shooter.Owner).DamageDealt += dealt;
            if (shooter.Fleet != null) LeaderManager.Instance?.OnDamageDealt(shooter.Owner, battle.SystemId, dealt);
            Side(battle, target.Owner).DamageTaken += dealt;

            if (target.Fleet != null)
            {
                if (target.Fleet.Data.HullPoints <= 0f) ResolveLethal(target.Fleet, shooter.Owner, battle);
            }
            else if (target.Base.Hull <= 0f && !target.Base.Disabled)
            {
                DisableStarbase(target.Base, shooter.Owner);
                LeaderManager.Instance?.OnKill(shooter.Owner, battle.SystemId);
                Side(battle, shooter.Owner).Kills++;
            }
        }

        /// <summary>Носитель эффектов: корабль или модель станции (снаряды доводятся до неё, щит рисуется вокруг).</summary>
        private static Transform FxTransform(Combatant c)
            => c.Fleet != null ? c.Fleet.transform : StarbaseVisuals.Instance != null ? StarbaseVisuals.Instance.StationOf(c.Base.SystemId) : null;

        /// <summary>Эффекты боя рисуются, только если игрок видит систему и камера достаточно близко.</summary>
        private static bool ShowFx(int systemId, Vector3 at)
        {
            if (!Vision.PlayerSees(systemId)) return false;
            if (SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView) return false;
            return CombatFx.CanShow(at);
        }

        private static float HitSize(Combatant c)
        {
            if (c.Fleet == null) return 1.7f;
            var d = c.Fleet.Data;
            if (d.Type != FleetType.Military) return 0.8f;
            return d.HullClass == ShipClass.Destroyer ? 1.25f : d.HullClass == ShipClass.Frigate ? 0.95f : 0.72f;
        }

        /// <summary>Точка выстрела: орудие на борту, обращённом к цели (у базы — по окружности станции).</summary>
        private static Vector3 Muzzle(Combatant shooter, Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            float reach = shooter.Fleet == null ? 1.1f : shooter.Fleet.Data.HullClass == ShipClass.Destroyer ? 0.55f : 0.35f;
            return from + dir * reach + side * Random.Range(-reach, reach) + Vector3.up * Random.Range(0.05f, 0.25f);
        }

        private static SideStats Side(Battle b, int owner)
        {
            if (!b.Sides.TryGetValue(owner, out var s)) b.Sides[owner] = s = new SideStats { Owner = owner };
            return s;
        }

        // ==================== ГИБЕЛЬ И ВЫХОД ИЗ БОЯ ====================

        /// <summary>Шанс выйти из боя вместо гибели: лёгкие корпуса маневреннее.</summary>
        public static float DisengageChance(FleetData d)
        {
            if (d.Type != FleetType.Military) return 0.25f;
            float baseChance = d.HullClass == ShipClass.Destroyer ? 0.20f : d.HullClass == ShipClass.Frigate ? 0.30f : 0.45f;
            return baseChance * EmpireBonuses.For(d.OwnerId).EvasionMult;
        }

        private void ResolveLethal(FleetView fv, int killer, Battle battle)
        {
            var d = fv.Data;
            int escape = EscapeSystem(d);
            if (escape >= 0 && Random.value < DisengageChance(d) * LeaderManager.AdmiralDisengageMult(d.OwnerId, battle.SystemId))
            {
                d.HullPoints = d.MaxHullPoints * 0.12f;
                d.ArmorPoints = 0f;
                d.ShieldPoints = 0f;
                d.InCombat = false;
                d.Path.Clear();
                d.OrderQueue.Clear();
                _disengaged.Add(d.Id);
                _stations.Remove(d.Id);
                _pendingFtl.Remove(d.Id);
                FleetManager.Instance?.IssueMoveOrder(fv, escape);
                Side(battle, d.OwnerId).Disengaged++;
                AddLog(battle, Loc.F("{0} выходит из боя", d.Name), LogKind.Disengage, d.OwnerId);
                if (ShowFx(battle.SystemId, fv.transform.position))
                {
                    SpawnFloater(fv.transform.position, Loc.T("ВЫХОД ИЗ БОЯ"), UIManager.DS.Gold);
                    SFXManager.PlayAt(Sfx.Disengage, fv.transform.position);
                }
                if (d.OwnerId == 0)
                    NotificationCenter.Show(Loc.T("Корабль вышел из боя"), Loc.F("{0} тяжело повреждён и отходит на ремонт", d.Name), NotificationCenter.Kind.Warning, 4f);
                return;
            }

            Side(battle, d.OwnerId).Lost++;
            Side(battle, killer).Kills++;
            AddLog(battle, Loc.F("{0} уничтожен", d.Name), LogKind.Kill, d.OwnerId);
            LeaderManager.Instance?.OnKill(killer, battle.SystemId);
            DestroyFleet(fv, killer);
        }

        /// <summary>Куда отступить: соседняя своя система, иначе любая соседняя.</summary>
        private int EscapeSystem(FleetData d)
        {
            if (_generator == null || d.CurrentSystemId < 0) return -1;
            var sys = _generator.Systems[d.CurrentSystemId];
            int any = -1;
            foreach (int n in sys.ConnectedSystemIds)
            {
                if (n < 0 || n >= _generator.Systems.Count) continue;
                if (_generator.Systems[n].OwnerId == d.OwnerId) return n;
                if (any < 0 && !Diplomacy.AtWar(_generator.Systems[n].OwnerId, d.OwnerId)) any = n;
            }
            if (any < 0 && sys.ConnectedSystemIds.Count > 0) any = sys.ConnectedSystemIds[0];
            return any;
        }

        private void DestroyFleet(FleetView fv, int killerOwner)
        {
            if (fv?.Data == null) return;
            fv.Data.Destroyed = true;
            fv.Data.InCombat = false;
            _stations.Remove(fv.Data.Id);
            _pendingFtl.Remove(fv.Data.Id);
            _combatDays.Remove(fv.Data.Id);
            if (ShowFx(fv.Data.CurrentSystemId, fv.transform.position))
            {
                float size = fv.Data.Type != FleetType.Military ? 0.8f
                           : fv.Data.HullClass == ShipClass.Destroyer ? 1.5f : fv.Data.HullClass == ShipClass.Frigate ? 1.15f : 0.85f;
                CombatFx.Instance.ShipDestroyed(fv.transform.position, size, FleetIndicator.OwnerColor(fv.Data.OwnerId));
                CombatFx.Instance.Wreck(fv.transform, size);
                SpawnFloater(fv.transform.position, Loc.T("УНИЧТОЖЕН"), UIManager.DS.Red);
            }
            bool big = fv.Data.Type == FleetType.Military && fv.Data.HullClass != ShipClass.Corvette;
            SFXManager.PlayAt(big ? Sfx.ExplosionLarge : Sfx.ExplosionSmall, fv.transform.position, fv.Data.OwnerId == 0 ? 1f : 0.9f);
            OnShipDestroyed?.Invoke(fv.Data, killerOwner);

            // Игрока касаются только его потери и его победы — бои соседей между собой без уведомлений
            if (fv.Data.OwnerId == 0 || killerOwner == 0)
            {
                string ownerName = fv.Data.OwnerId == 0 ? Loc.T("Ваш корабль") : AIEmpireManager.NameOf(fv.Data.OwnerId, Loc.T("Противник"));
                NotificationCenter.Show(Loc.T("Корабль уничтожен"), $"{ownerName}: {fv.Data.Name}",
                    fv.Data.OwnerId == 0 ? NotificationCenter.Kind.Danger : NotificationCenter.Kind.Success, 4f);
            }

            FleetManager.Instance?.NotifyFleetDestroyed(fv);
            Destroy(fv.gameObject, 0.15f);
        }

        public bool TryEmergencyFtl(FleetView fleet) => TryEmergencyFtl(fleet, -1);

        /// <summary>
        /// Приказ на экстренный прыжок из боя. Гиперпривод должен разогнаться (3 дня боя): до того корабль
        /// держит строй и ждёт, прыжок выполняется сам, как только ГПД готов (80% успеха, сбой — ещё полдня разгона).
        /// Прыжок стоит 10% корпуса. Корабль уходит в соседнюю систему — свою, если есть; preferredTarget — куда дальше.
        /// true — прыжок совершён прямо сейчас.
        /// </summary>
        public bool TryEmergencyFtl(FleetView fleet, int preferredTarget)
        {
            if (fleet?.Data == null || !fleet.Data.InCombat || _generator == null) return false;
            var d = fleet.Data;
            bool fresh = !_pendingFtl.ContainsKey(d.Id);
            _pendingFtl[d.Id] = preferredTarget;
            if (FtlReadyIn(d) > 0f)
            {
                if (fresh && ShowFx(d.CurrentSystemId, fleet.transform.position))
                    SpawnFloater(fleet.transform.position, Loc.T("РАЗГОН ГПД"), UIManager.DS.NeonCyan);
                return false;
            }
            return ExecuteFtl(fleet, preferredTarget);
        }

        /// <summary>Корабли с приказом на отступление прыгают, как только разгонится гиперпривод.</summary>
        private void ProcessPendingFtl()
        {
            if (_pendingFtl.Count == 0) return;
            var ready = new List<KeyValuePair<int, int>>();
            foreach (var kv in _pendingFtl)
                if (_viewById.TryGetValue(kv.Key, out var fv) && fv?.Data != null && fv.Data.InCombat && FtlReadyIn(fv.Data) <= 0f)
                    ready.Add(kv);
            foreach (var kv in ready) ExecuteFtl(_viewById[kv.Key], kv.Value);
        }

        private bool ExecuteFtl(FleetView fleet, int preferredTarget)
        {
            var d = fleet.Data;
            bool show = ShowFx(d.CurrentSystemId, fleet.transform.position);
            if (Random.value >= FtlSuccess)
            {
                // Сбой: гиперпривод снова разгоняется полдня
                _combatDays[d.Id] = Mathf.Max(0f, FtlSpoolDays - 0.5f);
                if (show)
                {
                    SpawnFloater(fleet.transform.position, Loc.T("СБОЙ ГПД"), UIManager.DS.Gold);
                    SFXManager.PlayAt(Sfx.FtlFail, fleet.transform.position);
                }
                return false;
            }

            int escape = EscapeSystem(d);
            if (escape < 0) { _pendingFtl.Remove(d.Id); return false; }

            _pendingFtl.Remove(d.Id);
            d.HullPoints = Mathf.Max(1f, d.HullPoints - d.MaxHullPoints * FtlHullCost);
            d.InCombat = false;
            d.Path.Clear();
            d.OrderQueue.Clear();
            _disengaged.Add(d.Id);
            _stations.Remove(d.Id);
            FleetManager.Instance?.IssueMoveOrder(fleet, escape);
            if (preferredTarget >= 0 && preferredTarget != escape)
            {
                var tail = GalaxyPathfinder.FindPath(escape, preferredTarget, _generator);
                if (tail != null) foreach (int step in tail) d.Path.Enqueue(step);
            }
            var battle = GetBattle(d.CurrentSystemId);
            if (battle != null) Side(battle, d.OwnerId).Disengaged++;
            if (show)
            {
                SpawnFloater(fleet.transform.position, Loc.T("ГПД АКТИВИРОВАН"), UIManager.DS.NeonCyan);
                SFXManager.PlayAt(Sfx.Disengage, fleet.transform.position);
            }
            return true;
        }

        // ==================== УРОН ====================

        public static float ApplyLayeredDamage(FleetData target, float raw, WeaponDamageType type)
        {
            float shield = target.ShieldPoints, armor = target.ArmorPoints, hull = target.HullPoints;
            float shown = Layered(ref shield, ref armor, ref hull, raw, type);
            target.ShieldPoints = shield; target.ArmorPoints = armor; target.HullPoints = hull;
            return shown;
        }

        private static float ApplyLayeredDamage(Starbase target, float raw, WeaponDamageType type)
        {
            float shield = target.Shields, armor = target.Armor, hull = target.Hull;
            float shown = Layered(ref shield, ref armor, ref hull, raw, type);
            target.Shields = shield; target.Armor = armor; target.Hull = hull;
            return shown;
        }

        private static float Layered(ref float shield, ref float armor, ref float hull, float raw, WeaponDamageType type)
        {
            float remaining = raw, shown = 0f;
            if (shield > 0f && remaining > 0f)
            {
                float mul = type == WeaponDamageType.Kinetic ? KineticVsShield : type == WeaponDamageType.Energy ? EnergyVsShield : ExplosiveVsAll;
                float absorbed = Mathf.Min(shield, remaining * mul);
                shield -= absorbed;
                remaining -= absorbed / mul;
                shown += absorbed;
            }
            if (armor > 0f && remaining > 0f)
            {
                float mul = type == WeaponDamageType.Energy ? EnergyVsArmor : type == WeaponDamageType.Kinetic ? KineticVsArmor : ExplosiveVsAll;
                float absorbed = Mathf.Min(armor, remaining * mul);
                armor -= absorbed;
                remaining -= absorbed / mul;
                shown += absorbed;
            }
            if (remaining > 0f)
            {
                hull = Mathf.Max(0f, hull - remaining);
                shown += remaining;
            }
            return shown;
        }

        // ==================== СТАТИСТИКА И КОНЕЦ БОЯ ====================

        private Battle EnsureBattle(int sysId, List<Combatant> parts)
        {
            if (_battles.TryGetValue(sysId, out var b)) return b;
            b = new Battle { SystemId = sysId, BaseAngle = Random.Range(0f, 360f) };
            _battles[sysId] = b;
            UpdateSideStats(b, parts);
            foreach (var s in b.Sides.Values) { s.StartShips = s.Ships; s.StartHp = s.Hp; }
            if (b.PlayerInvolved) { b.Stances[0] = s_playerStance; b.Priorities[0] = s_playerPriority; }
            ThinkAiTactics(b);
            AddLog(b, Loc.T("Сближение: работают ракеты, лучи ещё не достают"), LogKind.Phase);

            if (b.PlayerInvolved)
            {
                var sys = _generator.Systems[sysId];
                // Бой на нашей территории — это атака на нас: сирена и вспышка, а не обычный «сигнал к бою»
                if (sys.OwnerId == 0) { SFXManager.Play(Sfx.RedAlert); AlarmFeedback.Attack(); }
                else SFXManager.Play(Sfx.BattleStart);
                NotificationCenter.Show(Loc.T("Сражение!"), Loc.F("Бой в системе {0}. Выделите флот, чтобы следить за боем и при необходимости отступить", sys.Name),
                    NotificationCenter.Kind.Danger, 6f);
            }
            return b;
        }

        private void UpdateSideStats(Battle b, List<Combatant> parts)
        {
            foreach (var s in b.Sides.Values)
            {
                s.Ships = 0; s.Hp = 0f; s.Power = 0f; s.HasStarbase = false; s.Dps = 0f; s.Shields = 0f; s.MaxShields = 0f;
                System.Array.Clear(s.ClassAlive, 0, s.ClassAlive.Length);
            }
            foreach (var p in parts)
            {
                if (!p.Alive) continue;
                var s = Side(b, p.Owner);
                if (p.Fleet != null)
                {
                    var d = p.Fleet.Data;
                    s.Ships++;
                    s.Hp += d.HullPoints + d.ArmorPoints + d.ShieldPoints;
                    s.Shields += d.ShieldPoints;
                    s.MaxShields += d.MaxShieldPoints;
                    s.Power += CombatMath.Power(d);
                    s.ClassAlive[d.Type != FleetType.Military ? 3 : Mathf.Clamp((int)d.HullClass, 0, 2)]++;
                    if (d.Type == FleetType.Military)
                        foreach (var w in d.Weapons) s.Dps += w.Damage * w.FireRate * ShotsPerDay;
                }
                else
                {
                    s.HasStarbase = true;
                    s.Hp += p.Base.Hull + p.Base.Armor + p.Base.Shields;
                    s.Shields += p.Base.Shields;
                    s.MaxShields += p.Base.MaxShields;
                    s.Power += StarbasePower(p.Base);
                    s.Dps += p.Base.Damage * p.Base.FireRate * ShotsPerDay;
                }
                if (s.StartHp < s.Hp) s.StartHp = s.Hp;
                if (s.StartShips < s.Ships) s.StartShips = s.Ships;
            }
            foreach (var s in b.Sides.Values)
            {
                s.Dps *= StanceDamageMult(b, s.Owner);
                for (int i = 0; i < s.ClassAlive.Length; i++) if (s.ClassStart[i] < s.ClassAlive[i]) s.ClassStart[i] = s.ClassAlive[i];
            }
        }

        private void EndBattle(Battle b)
        {
            _battles.Remove(b.SystemId);
            if (b.Marker != null) Destroy(b.Marker);
            NewsAboutBattle(b);
            if (!b.PlayerInvolved || _generator == null) return;

            var sys = _generator.Systems[b.SystemId];
            var me = b.Sides[0];
            int enemyLost = 0, enemyLeft = 0, enemyDisengaged = 0;
            foreach (var kv in b.Sides)
            {
                if (kv.Key == 0) continue;
                enemyLost += kv.Value.Lost;
                enemyLeft += kv.Value.Ships;
                enemyDisengaged += kv.Value.Disengaged;
            }
            bool won = me.Ships > 0 && enemyLeft == 0;
            bool lost = me.Ships == 0 && enemyLeft > 0;
            if (won && enemyLost + enemyDisengaged > 0) SFXManager.Play(Sfx.BattleVictory);
            if (won && enemyLost > 0) Achievements.Unlock(Ach.FirstBattle);
            else if (lost) SFXManager.Play(Sfx.BattleDefeat);
            string title = won ? Loc.F("Победа при {0}", sys.Name) : lost ? Loc.F("Поражение при {0}", sys.Name) : Loc.F("Бой при {0} завершён", sys.Name);
            string body = Loc.F("{0} дн. · уничтожено врагов: {1}", Mathf.CeilToInt(b.Days), enemyLost) +
                          (enemyDisengaged > 0 ? Loc.F(" (бежало {0})", enemyDisengaged) : "") +
                          Loc.F(" · наши потери: {0}", me.Lost) + (me.Disengaged > 0 ? Loc.F(" (вышло из боя {0})", me.Disengaged) : "") +
                          Loc.F(" · урон {0:N0} / {1:N0}", me.DamageDealt, me.DamageTaken);
            NotificationCenter.Show(title, body, won ? NotificationCenter.Kind.Success : lost ? NotificationCenter.Kind.Danger : NotificationCenter.Kind.Info, 9f);
        }

        /// <summary>В ленту новостей — сражения, где погибло хотя бы два корабля: кто устоял и кто потерял больше.</summary>
        private void NewsAboutBattle(Battle b)
        {
            if (_generator == null || b.SystemId < 0 || b.SystemId >= _generator.Systems.Count) return;
            int lost = 0, winner = -1, loser = -1, winnerShips = 0, loserLost = -1;
            foreach (var kv in b.Sides)
            {
                lost += kv.Value.Lost;
                if (kv.Value.Ships > winnerShips) { winnerShips = kv.Value.Ships; winner = kv.Key; }
            }
            foreach (var kv in b.Sides)
                if (kv.Key != winner && kv.Value.Lost > loserLost) { loserLost = kv.Value.Lost; loser = kv.Key; }
            GalacticNews.Battle(_generator.Systems[b.SystemId], winner, loser, lost);
        }

        // ==================== ЗВЁЗДНЫЕ БАЗЫ: ДАННЫЕ ====================

        private bool IsCapital(StarSystem sys)
        {
            if (sys.OwnerId == 0) return sys.Id == EconomyManager.PlayerCapitalId;
            var ai = AIEmpireManager.For(sys.OwnerId);
            return ai != null && sys.Id == ai.CapitalSystemId;
        }

        /// <summary>Параметры базы: форпост — скромная батарея, колонии и столица укреплены сильнее.</summary>
        private void ApplyStarbaseTemplate(Starbase sb, StarSystem sys)
        {
            int colonies = 0;
            foreach (var p in sys.Planets) if (p.Population > 0) colonies++;
            colonies = Mathf.Min(colonies, 4);
            float k = IsCapital(sys) ? 2f : 1f;
            var b = EmpireBonuses.For(sys.OwnerId);
            float oldMaxHull = sb.MaxHull, oldMaxArmor = sb.MaxArmor, oldMaxShields = sb.MaxShields;

            // «Звёздные крепости», Цитадель Древних и укрепление самой базы
            float fort = b.StarbaseMult * Landmarks.StarbaseMult(sys.OwnerId) * Fortifications.DurabilityMult(sys.Fortification);
            sb.MaxHull = (400f + colonies * 150f) * k * b.HullMult * fort;
            sb.MaxArmor = (200f + colonies * 60f) * k * b.ArmorMult * fort * (sb.GardenDays > 0 ? FactionTraits.GardenArmor : 1f);
            sb.MaxShields = (150f + colonies * 40f) * k * b.ShieldMult * fort;
            sb.Damage = (9f + colonies * 4f) * k * Fortifications.DamageMult(sys.Fortification);
            sb.FireRate = 1f;

            // Масштабируем текущие значения при изменении максимума
            if (oldMaxHull > 0f) sb.Hull = sb.Hull / oldMaxHull * sb.MaxHull; else sb.Hull = sb.MaxHull;
            if (oldMaxArmor > 0f) sb.Armor = sb.Armor / oldMaxArmor * sb.MaxArmor; else sb.Armor = sb.MaxArmor;
            if (oldMaxShields > 0f) sb.Shields = sb.Shields / oldMaxShields * sb.MaxShields; else sb.Shields = sb.MaxShields;
        }

        /// <summary>Пересчитать базу после смены укрепления (текущая прочность растёт вместе с максимумом).</summary>
        public void RefreshStarbase(int systemId)
        {
            var sb = GetStarbase(systemId);
            if (sb != null && _generator != null) ApplyStarbaseTemplate(sb, _generator.Systems[systemId]);
        }

        /// <summary>Звёздная база системы (создаётся при первом обращении). null — у системы нет базы.</summary>
        public Starbase GetStarbase(int systemId)
        {
            if (_generator == null || systemId < 0 || systemId >= _generator.Systems.Count) return null;
            var sys = _generator.Systems[systemId];
            if (sys.OwnerId < 0 || !sys.HasStarbase) { _starbases.Remove(systemId); return null; }
            if (!_starbases.TryGetValue(systemId, out var sb))
            {
                sb = new Starbase { SystemId = systemId };
                ApplyStarbaseTemplate(sb, sys);
                _starbases[systemId] = sb;
            }
            return sb;
        }

        /// <summary>База в строю (стреляет и мешает осаде).</summary>
        public bool IsStarbaseActive(int systemId)
        {
            var sb = GetStarbase(systemId);
            return sb != null && !sb.Disabled;
        }

        /// <summary>
        /// Средняя мощь боевого корабля игрока (по проекту, без учёта повреждений) — чтобы мощь базы
        /// можно было сравнить с привычной единицей: «≈ N ваших кораблей». 0 — боевых кораблей нет.
        /// </summary>
        public static float PlayerAverageShipPower()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return 0f;
            float sum = 0f;
            int n = 0;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.OwnerId != 0 || d.Type != FleetType.Military) continue;
                sum += d.MilitaryPower;
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>Подпись «≈ N ваших боевых кораблей» для мощи (пусто, если сравнивать не с чем).</summary>
        public static string PowerInShips(float power)
        {
            float avg = PlayerAverageShipPower();
            if (avg <= 0f || power <= 0f) return "";
            float n = power / avg;
            return n < 0.95f ? Loc.T("слабее одного вашего боевого корабля")
                 : Loc.F("≈ {0:0.#} ваш{1}", n, (n < 1.5f ? Loc.T("его боевого корабля") : Loc.T("их боевых кораблей")));
        }

        public float StarbasePower(int systemId)
        {
            var sb = GetStarbase(systemId);
            return sb == null ? 0f : StarbasePower(sb);
        }

        private float StarbasePower(Starbase sb)
        {
            if (sb.Disabled) return 0f;
            var sys = _generator.Systems[sb.SystemId];
            float dps = sb.Damage * sb.FireRate * EmpireBonuses.For(sys.OwnerId).DamageMult(sb.Weapon);
            return dps * 12f + sb.Hull * 0.35f + sb.Armor * 0.2f + sb.Shields * 0.15f;
        }

        private void DisableStarbase(Starbase sb, int attacker)
        {
            if (_battles.TryGetValue(sb.SystemId, out var battle))
                AddLog(battle, Loc.T("Звёздная база выведена из строя"), LogKind.Kill, _generator.Systems[sb.SystemId].OwnerId);
            sb.Disabled = true;
            sb.Hull = 0f;
            sb.Shields = 0f;
            var sys = _generator.Systems[sb.SystemId];
            if (ShowFx(sb.SystemId, sys.Position))
                CombatFx.Instance.ShipDestroyed(StarbaseVisuals.SiteFor(sys.Position) + Vector3.up * 0.3f, 1.9f, FleetIndicator.OwnerColor(sys.OwnerId));
            SFXManager.PlayAt(Sfx.StarbaseDown, sys.Position, 1f);
            if (attacker == 0 && sys.OwnerId != 0 && UIManager.IsGameStarted) Achievements.Unlock(Ach.Starbase);
            if (sys.OwnerId == 0)
                NotificationCenter.Show(Loc.T("Звёздная база выведена из строя"), Loc.F("{0}: враг может начать осаду", sys.Name), NotificationCenter.Kind.Danger, 7f);
            else if (attacker == 0)
                NotificationCenter.Show(Loc.T("Вражеская база подавлена"), Loc.F("{0}: держите флот на орбите — идёт осада", sys.Name), NotificationCenter.Kind.Success, 6f);
        }

        /// <summary>Пробуждение сада действует в системе: враги уходят отсюда медленнее.</summary>
        public bool IsGardenAwake(int systemId)
            => _starbases.TryGetValue(systemId, out var sb) && sb.GardenDays > 0;

        /// <summary>
        /// Пробуждение сада: враг в системе Конклава — база на 3 дня получает +30% брони,
        /// мицелий опутывает вражеские флоты (из системы они уходят на 15% медленнее). Перезарядка 15 дней.
        /// </summary>
        private void TickGardens()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            var hostiles = new Dictionary<int, int>();   // система → владелец вражеского флота
            foreach (var fv in fm.AllFleets)
            {
                var d = fv?.Data;
                if (d == null || d.Destroyed || d.State == FleetState.InHyperlane || d.Type != FleetType.Military) continue;
                if (d.CurrentSystemId < 0 || d.CurrentSystemId >= _generator.Systems.Count) continue;
                int sysOwner = _generator.Systems[d.CurrentSystemId].OwnerId;
                if (sysOwner >= 0 && sysOwner != d.OwnerId && Diplomacy.AtWar(d.OwnerId, sysOwner)) hostiles[d.CurrentSystemId] = d.OwnerId;
            }

            foreach (var sys in _generator.Systems)
            {
                if (sys.OwnerId < 0 || !sys.HasStarbase || !FactionTraits.IsTerraan(sys.OwnerId)) continue;
                var sb = GetStarbase(sys.Id);
                if (sb == null) continue;
                if (sb.GardenCooldown > 0) sb.GardenCooldown--;
                if (sb.GardenDays > 0)
                {
                    if (--sb.GardenDays == 0) ApplyStarbaseTemplate(sb, sys);
                    continue;
                }
                if (sb.GardenCooldown > 0 || sb.Disabled || !hostiles.TryGetValue(sys.Id, out int enemy)) continue;

                sb.GardenDays = FactionTraits.GardenDays;
                sb.GardenCooldown = 15;
                float before = sb.MaxArmor;
                ApplyStarbaseTemplate(sb, sys);
                sb.Armor = Mathf.Min(sb.MaxArmor, sb.Armor + (sb.MaxArmor - before));
                GardenFx(sys);
                if (sys.OwnerId == 0)
                    NotificationCenter.Show(Loc.T("Пробуждение сада"), Loc.F("{0}: враг в наших корнях. База укреплена на 3 дня, мицелий опутывает чужие флоты", sys.Name),
                        NotificationCenter.Kind.Success, 6f);
                else if (enemy == 0)
                    NotificationCenter.Show(Loc.T("Пробуждение сада"), Loc.F("{0}: сад Конклава проснулся — база укреплена, наши корабли вязнут в мицелии", sys.Name),
                        NotificationCenter.Kind.Warning, 6f);
            }
        }

        private void GardenFx(StarSystem sys)
        {
            Vector3 pos = StarbaseVisuals.SiteFor(sys.Position);
            if (!ShowFx(sys.Id, pos)) return;
            var green = new Color(0.45f, 1f, 0.45f);
            CombatFx.Instance.Blip(pos, green, 7f, 1.4f);
            CombatFx.Instance.Sparks(pos, green, 18, 1.4f);
            SFXManager.PlayAt(Sfx.Anomaly, pos, 0.6f);
        }

        /// <summary>Ежедневный ремонт баз вне боя (выведенная из строя база оживает на половине прочности).</summary>
        private void HandleDay(int day, int month, int year)
        {
            if (_generator == null) return;
            TickGardens();
            var remove = new List<int>();
            foreach (var kv in _starbases)
            {
                var sys = _generator.Systems[kv.Key];
                if (sys.OwnerId < 0 || !sys.HasStarbase) { remove.Add(kv.Key); continue; }
                var sb = kv.Value;
                if (day == 1) ApplyStarbaseTemplate(sb, sys);   // колонии и технологии меняют базу
                if (_battles.ContainsKey(kv.Key)) continue;
                // Пока в системе стоит враг, база не чинится (осада)
                var siege = SiegeManager.Instance?.GetSiege(kv.Key);
                if (siege != null && siege.Active) continue;

                sb.Shields = Mathf.Min(sb.MaxShields, sb.Shields + sb.MaxShields * 0.2f);
                sb.Armor = Mathf.Min(sb.MaxArmor, sb.Armor + sb.MaxArmor * 0.05f);
                sb.Hull = Mathf.Min(sb.MaxHull, sb.Hull + sb.MaxHull * 0.05f);
                if (sb.Disabled && sb.Hull >= sb.MaxHull * 0.5f) sb.Disabled = false;
            }
            foreach (int id in remove) _starbases.Remove(id);
        }

        private void HandleCaptured(StarSystem sys, int oldOwner, int newOwner)
        {
            // Захваченная база переходит к новому владельцу повреждённой, но в строю; переданная по договору — целой.
            // Штурм разрушает один уровень укреплений
            if (!SiegeManager.IsPeacefulTransfer) sys.Fortification = Mathf.Max(0, sys.Fortification - 1);
            _starbases.Remove(sys.Id);
            if (SiegeManager.IsPeacefulTransfer) return;
            var sb = GetStarbase(sys.Id);
            if (sb != null)
            {
                sb.Hull = sb.MaxHull * 0.25f;
                sb.Armor = 0f;
                sb.Shields = 0f;
                sb.Disabled = false;
            }
        }

        public List<StarbaseSave> CaptureStarbases()
        {
            var list = new List<StarbaseSave>();
            foreach (var sb in _starbases.Values)
                list.Add(new StarbaseSave { System = sb.SystemId, Hull = sb.Hull, Armor = sb.Armor, Shields = sb.Shields, Disabled = sb.Disabled });
            return list;
        }

        public void RestoreStarbases(List<StarbaseSave> list)
        {
            _starbases.Clear();
            foreach (var b in _battles.Values) if (b.Marker != null) Destroy(b.Marker);
            _battles.Clear();
            _targets.Clear();
            _disengaged.Clear();
            _escaping.Clear();
            _laneOrder.Clear();
            _combatDays.Clear();
            _pendingFtl.Clear();
            _stations.Clear();
            if (_generator == null) _generator = FindFirstObjectByType<GalaxyGenerator>();
            if (list == null) return;
            foreach (var s in list)
            {
                var sb = GetStarbase(s.System);
                if (sb == null) continue;
                sb.Hull = s.Hull; sb.Armor = s.Armor; sb.Shields = s.Shields; sb.Disabled = s.Disabled;
            }
        }

        // ==================== ЭФФЕКТЫ ====================

        private static Sfx WeaponSfx(WeaponDamageType t) => t switch
        {
            WeaponDamageType.Energy => Sfx.WeaponEnergy,
            WeaponDamageType.Kinetic => Sfx.WeaponKinetic,
            _ => Sfx.WeaponMissile
        };

        /// <summary>Калибр на слух: орудия эсминцев и баз ниже и тяжелее, корветов — суше и выше.</summary>
        private static float GunPitch(Combatant shooter)
        {
            if (shooter.Fleet == null) return 0.78f;
            var h = shooter.Fleet.Data.HullClass;
            return h == ShipClass.Destroyer ? 0.84f : h == ShipClass.Frigate ? 0.94f : 1.04f;
        }

        /// <summary>
        /// Звук выстрела у стрелка и попадания у цели — когда снаряд долетел (как в эффектах: трассер, ракета по дуге).
        /// В крупных боях выстрелов очень много — часть пропускается (плюс лимит голосов в SFXManager), чтобы не было «каши».
        /// </summary>
        private static void ShotSound(WeaponDamageType weapon, Vector3 from, Vector3 to, bool shieldHit, bool heavyTarget, float gunPitch)
        {
            if (Random.value < 0.75f) SFXManager.PlayAt(WeaponSfx(weapon), from, 1f, gunPitch);
            if (Random.value < 0.55f)
            {
                var sfx = shieldHit ? Sfx.HitShield : Sfx.HitArmor;
                float pitch = heavyTarget ? 0.85f : 1f;
                float dist = Vector3.Distance(from, to);
                float delay = weapon == WeaponDamageType.Explosive ? 0.75f + dist * 0.03f
                            : weapon == WeaponDamageType.Kinetic ? 0.22f + dist * 0.012f
                            : 0.02f;
                SFXManager.PlayAtDelayed(sfx, to, delay, 1f, pitch);
            }
        }

        private static Color WeaponColor(WeaponDamageType t) => t switch
        {
            WeaponDamageType.Energy => new Color(1f, 0.25f, 0.22f, 1f),
            WeaponDamageType.Kinetic => new Color(1f, 0.85f, 0.35f, 1f),
            _ => new Color(0.45f, 1f, 0.55f, 1f)
        };

        private void SpawnFloater(Vector3 pos, string text, Color col)
        {
            if (_floaters.Count > 60) return;
            var go = new GameObject("DmgFloater");
            go.transform.position = pos + Vector3.up * 2.2f + Random.insideUnitSphere * 0.4f;
            go.transform.SetParent(_fxRoot.transform, true);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 28;
            tm.characterSize = 0.11f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = col;
            if (_font != null)
            {
                tm.font = _font;
                go.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
            }
            _floaters.Add(new FloatingDmg { Mesh = tm, Life = 0.85f, Velocity = Vector3.up * 2.4f });
        }

        private void TickFloaters(float dt)
        {
            var cam = Camera.main;
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                var f = _floaters[i];
                f.Life -= dt;
                if (f.Mesh == null) { _floaters.RemoveAt(i); continue; }
                f.Mesh.transform.position += f.Velocity * dt;
                if (cam != null) f.Mesh.transform.rotation = cam.transform.rotation;
                var c = f.Mesh.color;
                c.a = Mathf.Clamp01(f.Life / 0.85f);
                f.Mesh.color = c;
                if (f.Life <= 0f)
                {
                    Destroy(f.Mesh.gameObject);
                    _floaters.RemoveAt(i);
                }
            }
        }

        /// <summary>Значок сражения над системой — скрещённые мечи, пульсирует, постоянный экранный размер.</summary>
        private void UpdateMarkers()
        {
            var cam = Camera.main;
            if (cam == null || _generator == null) return;
            foreach (var b in _battles.Values)
            {
                if (b.Marker == null)
                {
                    b.Marker = new GameObject($"BattleMarker_{b.SystemId}");
                    b.Marker.transform.SetParent(_fxRoot.transform, false);
                    var glow = new GameObject("Glow").AddComponent<SpriteRenderer>();
                    glow.transform.SetParent(b.Marker.transform, false);
                    glow.sprite = FleetIndicator.GetDiscSprite();
                    glow.sharedMaterial = FleetIndicator.SpriteMaterial;
                    glow.sortingOrder = 55;
                    glow.transform.localScale = Vector3.one * 1.3f;
                    var icon = new GameObject("Icon").AddComponent<SpriteRenderer>();
                    icon.transform.SetParent(b.Marker.transform, false);
                    icon.sprite = LGIcons.Get(LGIcon.Swords);
                    icon.sharedMaterial = FleetIndicator.SpriteMaterial;
                    icon.sortingOrder = 56;
                    icon.transform.localScale = Vector3.one * 0.75f;
                }
                Vector3 pos = _generator.Systems[b.SystemId].Position + Vector3.up * 5.5f;
                float dist = Mathf.Max(1f, Vector3.Distance(pos, cam.transform.position));
                float wpp = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
                float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f);
                b.Marker.transform.position = pos;
                b.Marker.transform.rotation = cam.transform.rotation;
                b.Marker.transform.localScale = Vector3.one * (34f * wpp) * pulse;
                var srs = b.Marker.GetComponentsInChildren<SpriteRenderer>();
                Color c = b.PlayerInvolved ? new Color(1f, 0.3f, 0.25f) : new Color(1f, 0.7f, 0.3f);
                srs[0].color = new Color(0.12f, 0.02f, 0.02f, 0.85f);
                srs[1].color = Color.Lerp(c, Color.white, 0.2f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f));
            }
        }
    }
}
