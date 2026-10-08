using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using Random = UnityEngine.Random;

namespace StellarisClone.Core
{
    /// <summary>Угрозы вне империй: у них свои номера владельцев, они враждебны всем и не владеют системами.</summary>
    public static class Threats
    {
        public const int PirateOwner = 9;
        public const int LeviathanOwner = 10;
        public static readonly int[] Owners = { PirateOwner, LeviathanOwner };

        public static bool IsThreat(int owner) => owner == PirateOwner || owner == LeviathanOwner;

        public static string NameOf(int owner) =>
            owner == PirateOwner ? "Пираты" : owner == LeviathanOwner ? "Левиафан" : null;

        public static Color ColorOf(int owner) =>
            owner == PirateOwner ? new Color(0.62f, 0.95f, 0.30f) : new Color(1f, 0.38f, 0.72f);
    }

    [Serializable]
    public class ThreatSave
    {
        public bool LeviathansSpawned;
        /// <summary>Дней до следующего налёта; −1 — часы ещё не запущены.</summary>
        public int RaidCountdown = -1;
        public int PlannedVictim = -1, PlannedSpawn = -1, PlannedTarget = -1;
        public int HavenSystem = -1;
        public bool HavenEventShown;
        public int TributeMonths, BountyMonths, PrivateerRaids;
        public int Raids;
    }

    /// <summary>
    /// Пираты и левиафаны.
    ///   • Пираты: с определённого года (зависит от сложности) банды выходят из ничейных систем у чьих-то
    ///     границ, летят к слабо защищённой колонии, подавляют базу, грабят и уходят с добычей домой.
    ///     Систем они не захватывают. Игрок может платить дань, объявить награду или выдать каперские грамоты.
    ///   • Левиафаны: древние чудовища стерегут богатые ничейные системы, не двигаются и медленно
    ///     залечивают раны. Победа над левиафаном — отдельное событие с наградой.
    /// Оракул Иридии видит налёты на свои системы за два месяца.
    /// </summary>
    public class ThreatManager : MonoBehaviour
    {
        public static ThreatManager Instance { get; private set; }

        private ThreatSave _s = new ThreatSave();
        public ThreatSave State => _s;

        private class Band
        {
            public int Target = -1, Home = -1;
            public bool Returning;
            public int DaysAlive, DaysLooting;
        }
        // Задачи банд не сохраняются: после загрузки пираты заново выбирают цель
        private readonly Dictionary<int, Band> _bands = new Dictionary<int, Band>();
        private bool _subscribed;

        private static readonly string[] PirateNames =
            { "Ржавый клык", "Чёрная звезда", "Вдова", "Крюк", "Солнечный вор", "Падальщик", "Безымянный", "Кровавая луна" };
        private static readonly string[] LeviathanNames =
            { "Пожиратель солнц", "Безмолвный страж", "Кристаллическая гидра", "Дитя пустоты", "Древний кит" };

        private static int Difficulty => GameSession.Settings.Difficulty;
        private static int FirstRaidYear => Difficulty == 0 ? 2212 : Difficulty == 2 ? 2206 : 2208;
        private static int RaidInterval => Difficulty == 0 ? Random.Range(600, 900) : Difficulty == 2 ? Random.Range(300, 480) : Random.Range(420, 660);
        private const int WarningDays = 60;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start() => TrySubscribe();

        private void TrySubscribe()
        {
            if (_subscribed || TimeManager.Instance == null) return;
            TimeManager.Instance.OnDayPassed += HandleDay;
            CombatManager.OnShipDestroyed += HandleShipDestroyed;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (_subscribed)
            {
                if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
                CombatManager.OnShipDestroyed -= HandleShipDestroyed;
            }
            if (Instance == this) Instance = null;
        }

        // ==================== ДЕЙСТВИЯ ИГРОКА (из событий) ====================

        public bool TributeActive => _s.TributeMonths > 0;
        public bool BountyActive => _s.BountyMonths > 0;
        public int TributeMonths => _s.TributeMonths;
        public int BountyMonths => _s.BountyMonths;
        public int PrivateerRaids => _s.PrivateerRaids;

        public void SetTribute(int months) => _s.TributeMonths = Mathf.Max(_s.TributeMonths, months);
        public void SetBounty(int months) => _s.BountyMonths = Mathf.Max(_s.BountyMonths, months);
        public void SetPrivateers(int raids) => _s.PrivateerRaids = Mathf.Max(_s.PrivateerRaids, raids);

        public int DaysToNextRaid => _s.RaidCountdown;
        public int RaidsSoFar => _s.Raids;

        /// <summary>Пробудить левиафана (последствия событий). Возвращает систему или −1.</summary>
        public int AwakenLeviathan(int nearOwner, string name, float strength)
        {
            int sys = PickLeviathanSystem(nearOwner, 2, 4);
            if (sys < 0) sys = PickLeviathanSystem(-1, 0, 99);
            if (sys < 0) return -1;
            SpawnLeviathan(sys, name, strength);
            return sys;
        }

        // ==================== КАЛЕНДАРЬ ====================

        private void HandleDay(int day, int month, int year)
        {
            if (!UIManager.IsGameStarted || EmpireStats.Systems.Count == 0) return;
            if (!_s.LeviathansSpawned) TrySpawnStartingLeviathans();
            if (day == 1) Monthly();
            TickRaidClock(year);
            TickPirates();
            TickLeviathans();
        }

        private void Monthly()
        {
            if (_s.TributeMonths > 0 && --_s.TributeMonths == 0)
                NotificationCenter.Show("Срок дани истёк", "Пираты снова могут напасть на ваши системы", NotificationCenter.Kind.Warning, 6f);
            if (_s.BountyMonths > 0 && --_s.BountyMonths == 0)
                NotificationCenter.Show("Награда отменена", "Охота за головами пиратов закончилась", NotificationCenter.Kind.Info, 5f);
        }

        // ==================== ЛЕВИАФАНЫ ====================

        private void TrySpawnStartingLeviathans()
        {
            // Ждём, пока все империи ИИ выберут столицы, — чудовища селятся подальше от них
            foreach (var ai in AIEmpireManager.All) if (ai.CapitalSystemId < 0 && !ai.IsEliminated) return;
            _s.LeviathansSpawned = true;
            int size = GameSession.Settings.GalaxySize;
            int count = (size == 0 ? 1 : size == 2 ? 3 : 2) + (Difficulty == 2 ? 1 : 0) - (Difficulty == 0 && size > 0 ? 1 : 0);
            for (int i = 0; i < count; i++)
            {
                int sys = PickLeviathanSystem(-1, 4, 99);
                if (sys < 0) break;
                SpawnLeviathan(sys, LeviathanNames[i % LeviathanNames.Length], 1f);
            }
        }

        /// <summary>
        /// Богатая ничейная система подальше от столиц и других левиафанов.
        /// nearOwner ≥ 0 — наоборот, у границ этой империи (minJumps..maxJumps от её систем).
        /// </summary>
        private int PickLeviathanSystem(int nearOwner, int minJumps, int maxJumps)
        {
            var sources = new List<int>();
            if (nearOwner >= 0)
            {
                foreach (var s in EmpireStats.Systems) if (s.OwnerId == nearOwner) sources.Add(s.Id);
            }
            else
            {
                sources.Add(EconomyManager.PlayerCapitalId);
                foreach (var ai in AIEmpireManager.All) if (ai.CapitalSystemId >= 0) sources.Add(ai.CapitalSystemId);
            }
            var dist = MultiBfs(sources, 99);
            var taken = new HashSet<int>();
            foreach (var d in FleetsOf(Threats.LeviathanOwner)) taken.Add(d.CurrentSystemId);
            var takenDist = MultiBfs(new List<int>(taken), 99);

            int best = -1;
            float bestScore = float.MinValue;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != -1 || s.ConnectedSystemIds.Count == 0 || taken.Contains(s.Id)) continue;
                if (!dist.TryGetValue(s.Id, out int d) || d < minJumps || d > maxJumps) continue;
                if (takenDist.TryGetValue(s.Id, out int td) && td < 4) continue;
                s.GeneratePlanets();
                float value = 0f;
                foreach (var p in s.Planets) value += p.EnergyDeposit + p.MineralDeposit + (p.CanColonize ? 8f : 0f);
                value += Random.value * 4f;
                if (value > bestScore) { bestScore = value; best = s.Id; }
            }
            return best;
        }

        private void SpawnLeviathan(int sysId, string name, float strength)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            float k = strength * (Difficulty == 0 ? 0.75f : Difficulty == 2 ? 1.3f : 1f);
            var fv = fm.CreateOwnedFleet($"Левиафан «{name}»", sysId, FleetType.Military, Threats.LeviathanOwner, ShipClass.Destroyer);
            var d = fv.Data;
            d.MaxHullPoints = d.HullPoints = 2200f * k;
            d.MaxArmorPoints = d.ArmorPoints = 800f * k;
            d.MaxShieldPoints = d.ShieldPoints = 500f * k;
            d.SetWeapons(new[]
            {
                new WeaponMount(WeaponDamageType.Explosive, 26f * k, 0.8f),
                new WeaponMount(WeaponDamageType.Energy, 20f * k, 1f),
                new WeaponMount(WeaponDamageType.Kinetic, 20f * k, 1f)
            });
            d.Evasion = 4f;
            d.UpkeepEnergy = 0f;
        }

        /// <summary>Левиафан вне боя медленно залечивает раны: изматывать его нужно без долгих перерывов.</summary>
        private void TickLeviathans()
        {
            foreach (var d in FleetsOf(Threats.LeviathanOwner))
            {
                if (d.InCombat) continue;
                d.HullPoints = Mathf.Min(d.MaxHullPoints, d.HullPoints + d.MaxHullPoints * 0.005f);
                d.ArmorPoints = Mathf.Min(d.MaxArmorPoints, d.ArmorPoints + d.MaxArmorPoints * 0.005f);
                d.ShieldPoints = Mathf.Min(d.MaxShieldPoints, d.ShieldPoints + d.MaxShieldPoints * 0.02f);
            }
        }

        // ==================== НАЛЁТЫ ====================

        private void TickRaidClock(int year)
        {
            if (year < FirstRaidYear) return;
            if (_s.RaidCountdown < 0) _s.RaidCountdown = Random.Range(90, 240);
            _s.RaidCountdown--;
            if (_s.RaidCountdown == WarningDays) PlanRaid();
            if (_s.RaidCountdown > 0) return;
            LaunchRaid();
            _s.RaidCountdown = RaidInterval;
        }

        private static bool PlayerIsOracle => FactionRegistry.KeyOf(UIManager.Instance?.SelectedFaction) == "iridia";

        private void PlanRaid()
        {
            _s.PlannedVictim = PickVictim();
            _s.PlannedTarget = _s.PlannedVictim >= 0 ? PickTarget(_s.PlannedVictim) : -1;
            _s.PlannedSpawn = _s.PlannedTarget >= 0 ? PickSpawn(_s.PlannedTarget) : -1;
            if (_s.PlannedVictim == 0 && _s.PlannedTarget >= 0 && _s.PlannedSpawn >= 0 && PlayerIsOracle)
            {
                var t = EmpireStats.GetSystem(_s.PlannedTarget);
                NotificationCenter.Show("Видение Оракула",
                    $"Оракул видит пиратов у системы {t?.Name}: налёт примерно через два месяца. Укрепите её заранее",
                    NotificationCenter.Kind.Warning, 10f);
            }
        }

        private void LaunchRaid()
        {
            bool valid = _s.PlannedVictim >= 0 && _s.PlannedTarget >= 0 && _s.PlannedSpawn >= 0
                         && EmpireStats.GetSystem(_s.PlannedTarget)?.OwnerId == _s.PlannedVictim
                         && EmpireStats.GetSystem(_s.PlannedSpawn)?.OwnerId == -1
                         && (_s.PlannedVictim != 0 || !TributeActive);
            if (!valid) PlanRaid();
            int victim = _s.PlannedVictim, target = _s.PlannedTarget, spawn = _s.PlannedSpawn;
            _s.PlannedVictim = _s.PlannedTarget = _s.PlannedSpawn = -1;
            if (victim < 0 || target < 0 || spawn < 0) return;
            if (_s.PrivateerRaids > 0 && victim != 0) _s.PrivateerRaids--;

            int years = TimeManager.Instance != null ? Mathf.Max(0, TimeManager.Instance.Year - 2200) : 0;
            int ships = Mathf.Clamp(1 + years / 8 + (Difficulty == 2 ? 1 : 0), 1, 5);
            ShipClass hull = years >= 25 ? ShipClass.Destroyer : years >= 14 ? ShipClass.Frigate : ShipClass.Corvette;
            float scale = Mathf.Clamp(0.8f + years * 0.035f, 0.8f, 2.2f) * (Difficulty == 0 ? 0.8f : Difficulty == 2 ? 1.15f : 1f);
            string bandName = PirateNames[Random.Range(0, PirateNames.Length)];
            for (int i = 0; i < ships; i++)
                CreatePirate(spawn, target, i == 0 ? hull : ShipClass.Corvette, scale, $"Пираты «{bandName}»");

            _s.Raids++;
            if (_s.HavenSystem < 0 || EmpireStats.GetSystem(_s.HavenSystem)?.OwnerId != -1) _s.HavenSystem = spawn;

            var tSys = EmpireStats.GetSystem(target);
            var sSys = EmpireStats.GetSystem(spawn);
            if (victim == 0)
                NotificationCenter.Show("Пиратский налёт", $"Банда «{bandName}» ({ships} кор.) вышла из {sSys?.Name} и идёт к {tSys?.Name}",
                    NotificationCenter.Kind.Danger, 9f);

            if (!_s.HavenEventShown)
            {
                _s.HavenEventShown = true;
                AnomalyEventSystem.Instance?.Schedule("pirate_haven", 2, new EventContext { System = EmpireStats.GetSystem(_s.HavenSystem) });
            }
        }

        private void CreatePirate(int spawn, int target, ShipClass hull, float scale, string name)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            var fv = fm.CreateOwnedFleet(name, spawn, FleetType.Military, Threats.PirateOwner, hull);
            var dm = ShipDesignManager.Instance;
            var design = dm != null ? dm.CreateAutoDesign(hull, WeaponDamageType.Kinetic, WeaponDamageType.Explosive, _ => false, "Пиратский рейдер") : null;
            var d = fv.Data;
            if (design != null) d.ApplyDesign(design);
            d.MaxHullPoints = d.HullPoints = d.MaxHullPoints * scale;
            d.MaxArmorPoints = d.ArmorPoints = d.MaxArmorPoints * scale;
            d.MaxShieldPoints = d.ShieldPoints = d.MaxShieldPoints * scale;
            var weapons = new List<WeaponMount>();
            foreach (var w in d.Weapons) weapons.Add(new WeaponMount(w.Type, w.Damage * scale, w.FireRate));
            d.SetWeapons(weapons);
            d.UpkeepEnergy = 0f;
            _bands[d.Id] = new Band { Target = target, Home = spawn };
            fm.IssueMoveOrder(fv, target);
        }

        /// <summary>Чья очередь: игрок (если не платит дань и не нанял каперов) или любой ИИ.</summary>
        private int PickVictim()
        {
            var options = new List<(int owner, float w)>();
            bool privateers = _s.PrivateerRaids > 0;
            if (!TributeActive && !privateers && EmpireStats.SystemCount(0) > 0) options.Add((0, 1.2f));
            foreach (var ai in AIEmpireManager.Alive)
            {
                float w = 1f;
                if (privateers) w = ai.AtWar ? 3f : ai.Opinion < 0f ? 1.5f : 0.5f;
                options.Add((ai.OwnerId, w));
            }
            float total = 0f;
            foreach (var o in options) total += o.w;
            if (total <= 0f) return -1;
            float r = Random.value * total;
            foreach (var o in options) { r -= o.w; if (r <= 0f) return o.owner; }
            return options[options.Count - 1].owner;
        }

        /// <summary>Цель: колония или база жертвы у её границы, ценная и слабо защищённая.</summary>
        private static int PickTarget(int victim)
        {
            int best = -1;
            float bestScore = float.MinValue;
            var fm = FleetManager.Instance;
            var cm = CombatManager.Instance;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != victim || !s.HasStarbase) continue;
                bool frontier = false;
                foreach (int n in s.ConnectedSystemIds)
                {
                    var ns = EmpireStats.GetSystem(n);
                    if (ns != null && ns.OwnerId == -1) { frontier = true; break; }
                }
                if (!frontier) continue;
                float score = Random.value * 10f;
                foreach (var p in s.Planets) if (p.Population > 0) score += 15f + p.Population;
                if (fm != null) score -= fm.GetMilitaryPowerInSystem(victim, s.Id) / 40f;
                if (cm != null) score -= cm.StarbasePower(s.Id) / 60f;
                if (score > bestScore) { bestScore = score; best = s.Id; }
            }
            return best;
        }

        /// <summary>Логово банды: ничейная система в 2–4 прыжках от цели, без левиафана.</summary>
        private int PickSpawn(int target)
        {
            var dist = MultiBfs(new List<int> { target }, 5);
            var leviathans = new HashSet<int>();
            foreach (var d in FleetsOf(Threats.LeviathanOwner)) leviathans.Add(d.CurrentSystemId);
            var options = new List<int>();
            foreach (var kv in dist)
            {
                if (kv.Value < 2 || kv.Value > 4 || leviathans.Contains(kv.Key)) continue;
                var s = EmpireStats.GetSystem(kv.Key);
                if (s == null || s.OwnerId != -1) continue;
                if (kv.Key == _s.HavenSystem) return kv.Key;
                options.Add(kv.Key);
            }
            if (options.Count == 0)
                foreach (var kv in dist)
                {
                    var s = EmpireStats.GetSystem(kv.Key);
                    if (kv.Value >= 1 && s != null && s.OwnerId == -1 && !leviathans.Contains(kv.Key)) options.Add(kv.Key);
                }
            return options.Count > 0 ? options[Random.Range(0, options.Count)] : -1;
        }

        // ==================== ПОВЕДЕНИЕ БАНД ====================

        private void TickPirates()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            var pirates = new List<FleetView>();
            foreach (var f in fm.AllFleets)
                if (f?.Data != null && !f.Data.Destroyed && f.Data.OwnerId == Threats.PirateOwner) pirates.Add(f);

            var looted = new HashSet<int>();
            foreach (var fv in pirates)
            {
                var d = fv.Data;
                if (!_bands.TryGetValue(d.Id, out var band))
                    _bands[d.Id] = band = new Band { Home = _s.HavenSystem >= 0 ? _s.HavenSystem : d.CurrentSystemId };
                band.DaysAlive++;
                if (d.InCombat || d.State == FleetState.InHyperlane || d.Path.Count > 0) continue;
                var sys = EmpireStats.GetSystem(d.CurrentSystemId);
                if (sys == null) continue;

                if (band.Returning)
                {
                    if (sys.Id == band.Home || band.Home < 0 || band.DaysAlive > 400) { _bands.Remove(d.Id); fm.DisbandFleet(fv); }
                    else fm.IssueMoveOrder(fv, band.Home);
                    continue;
                }

                if (CanLoot(sys))
                {
                    if (++band.DaysLooting < 10) continue;
                    if (looted.Add(sys.Id)) Loot(sys, PiratesAt(pirates, sys.Id));
                    foreach (var other in pirates)
                        if (other.Data.CurrentSystemId == sys.Id && _bands.TryGetValue(other.Data.Id, out var ob)) ob.Returning = true;
                    continue;
                }
                band.DaysLooting = 0;

                if (band.DaysAlive > 240) { band.Returning = true; continue; }
                if (band.Target < 0 || !ValidTarget(band.Target)) band.Target = NearestTarget(sys.Id);
                if (band.Target < 0) { band.Returning = true; continue; }
                if (band.Target != sys.Id) fm.IssueMoveOrder(fv, band.Target);
            }

            // Корабли, которых больше нет, — забываем
            var alive = new HashSet<int>();
            foreach (var fv in pirates) alive.Add(fv.Data.Id);
            var dead = new List<int>();
            foreach (var id in _bands.Keys) if (!alive.Contains(id)) dead.Add(id);
            foreach (var id in dead) _bands.Remove(id);
        }

        private static int PiratesAt(List<FleetView> pirates, int sysId)
        {
            int n = 0;
            foreach (var p in pirates) if (p.Data.CurrentSystemId == sysId && p.Data.State != FleetState.InHyperlane) n++;
            return n;
        }

        private static bool ValidTarget(int sysId)
        {
            var s = EmpireStats.GetSystem(sysId);
            return s != null && s.OwnerId >= 0 && !Threats.IsThreat(s.OwnerId) && s.HasStarbase;
        }

        /// <summary>Грабить можно, когда база подавлена и хозяин не держит в системе флот.</summary>
        private static bool CanLoot(StarSystem sys)
        {
            if (sys.OwnerId < 0 || Threats.IsThreat(sys.OwnerId)) return false;
            var cm = CombatManager.Instance;
            if (cm != null && cm.IsStarbaseActive(sys.Id)) return false;
            var fm = FleetManager.Instance;
            return fm == null || fm.GetMilitaryPowerInSystem(sys.OwnerId, sys.Id) <= 0f;
        }

        private int NearestTarget(int from)
        {
            var dist = MultiBfs(new List<int> { from }, 6);
            int best = -1, bestD = int.MaxValue;
            foreach (var kv in dist)
            {
                if (!ValidTarget(kv.Key) || kv.Value >= bestD) continue;
                var s = EmpireStats.GetSystem(kv.Key);
                if (s.OwnerId == 0 && TributeActive) continue;
                best = kv.Key; bestD = kv.Value;
            }
            return best;
        }

        private static void Loot(StarSystem sys, int ships)
        {
            int pop = 0;
            foreach (var p in sys.Planets) pop += Mathf.Max(0, p.Population);
            float energy = 30f * ships + pop * 4f;
            float minerals = 30f * ships + pop * 3f;
            int owner = sys.OwnerId;
            if (owner == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco == null) return;
                energy = Mathf.Min(energy, eco.EnergyCredits);
                minerals = Mathf.Min(minerals, eco.Minerals);
                eco.EnergyCredits -= energy;
                eco.Minerals -= minerals;
                eco.RaiseResourcesChanged();
                NotificationCenter.Show("Пираты разграбили систему", $"{sys.Name}: −{energy:0} энергии, −{minerals:0} минералов. Банда уходит с добычей",
                    NotificationCenter.Kind.Danger, 8f);
            }
            else
            {
                var ai = AIEmpireManager.For(owner);
                if (ai == null) return;
                ai.EnergyCredits = Mathf.Max(0f, ai.EnergyCredits - energy);
                ai.Minerals = Mathf.Max(0f, ai.Minerals - minerals);
                NotificationCenter.Show("Пиратский налёт", $"Пираты разграбили {sys.Name} ({ai.AIName})", NotificationCenter.Kind.Info, 5f);
            }
        }

        // ==================== ПОБЕДЫ НАД УГРОЗАМИ ====================

        private void HandleShipDestroyed(FleetData ship, int killer)
        {
            if (ship == null) return;
            if (ship.OwnerId == Threats.PirateOwner && killer == 0 && BountyActive)
            {
                var eco = EconomyManager.Instance;
                if (eco != null)
                {
                    eco.EnergyCredits += 40f;
                    eco.Influence += 4f;
                    eco.RaiseResourcesChanged();
                }
                NotificationCenter.Show("Награда за пирата", "+40 энергии, +4 влияния", NotificationCenter.Kind.Success, 4f);
            }
            else if (ship.OwnerId == Threats.LeviathanOwner)
            {
                var sys = EmpireStats.GetSystem(ship.CurrentSystemId);
                if (killer == 0)
                    AnomalyEventSystem.Instance?.Schedule("leviathan_slain", 1, new EventContext { System = sys });
                else
                {
                    var ai = AIEmpireManager.For(killer);
                    if (ai != null)
                    {
                        ai.Alloys += 250f;
                        NotificationCenter.Show("Левиафан повержен", $"{ai.AIName} сразили чудовище в системе {sys?.Name}", NotificationCenter.Kind.Info, 7f);
                    }
                }
            }
        }

        // ==================== ПОМОЩНИКИ ====================

        private static IEnumerable<FleetData> FleetsOf(int owner)
        {
            var fm = FleetManager.Instance;
            if (fm == null) yield break;
            foreach (var f in fm.AllFleets)
                if (f?.Data != null && !f.Data.Destroyed && f.Data.OwnerId == owner) yield return f.Data;
        }

        /// <summary>Прыжков от ближайшей из стартовых систем по гиперкоридорам.</summary>
        private static Dictionary<int, int> MultiBfs(List<int> sources, int maxJumps)
        {
            var dist = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (int s in sources)
                if (s >= 0 && s < EmpireStats.Systems.Count && !dist.ContainsKey(s)) { dist[s] = 0; q.Enqueue(s); }
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                int d = dist[cur];
                if (d >= maxJumps) continue;
                foreach (int n in EmpireStats.Systems[cur].ConnectedSystemIds)
                {
                    if (n < 0 || n >= EmpireStats.Systems.Count || dist.ContainsKey(n)) continue;
                    dist[n] = d + 1;
                    q.Enqueue(n);
                }
            }
            return dist;
        }

        // ==================== СОХРАНЕНИЕ ====================

        public ThreatSave CaptureState() => _s;

        public void RestoreState(ThreatSave s)
        {
            _s = s ?? new ThreatSave();
            _bands.Clear();
            // Старое сохранение: левиафанов не было — не подселяем их в середине партии
            if (s == null) _s.LeviathansSpawned = true;
        }
    }
}
