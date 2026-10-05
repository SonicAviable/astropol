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
    /// </summary>
    public class CombatManager : MonoBehaviour
    {
        public static CombatManager Instance { get; private set; }

        public const float KineticVsShield = 1.45f;
        public const float KineticVsArmor = 0.65f;
        public const float EnergyVsShield = 0.70f;
        public const float EnergyVsArmor = 1.45f;
        public const float ExplosiveVsAll = 1.05f;

        /// <summary>Длина боевого раунда, игровых дней.</summary>
        public const float RoundDays = 0.1f;
        /// <summary>Выстрелов в день на единицу скорострельности (FireRate проекта — «в секунду»).</summary>
        public const float ShotsPerDay = 1.5f;
        /// <summary>Сколько дней без перестрелки, чтобы бой считался завершённым.</summary>
        private const float BattleEndQuietDays = 0.5f;

        /// <summary>Корабль уничтожен в бою (владелец, кем уничтожен).</summary>
        public static event System.Action<FleetData, int> OnShipDestroyed;

        private GalaxyGenerator _generator;
        private readonly List<LineRenderer> _beams = new List<LineRenderer>();
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
        }

        public class Battle
        {
            public int SystemId;
            public float Days;
            public float QuietDays;
            public readonly Dictionary<int, SideStats> Sides = new Dictionary<int, SideStats>();
            public bool PlayerInvolved => Sides.ContainsKey(0);
            public GameObject Marker;
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
            public float Integrity => (Hull + Armor) / Mathf.Max(1f, MaxHull + MaxArmor);
        }

        private readonly Dictionary<int, Starbase> _starbases = new Dictionary<int, Starbase>();

        // Цели кораблей (огонь «прилипает»): id стрелка → id цели (-1000-sysId — звёздная база)
        private readonly Dictionary<int, int> _targets = new Dictionary<int, int>();
        // Корабли, вышедшие из боя: их не трогают, пока они не покинут систему
        private readonly HashSet<int> _disengaged = new HashSet<int>();

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
                float secondsPerDay = tm != null ? tm.SecondsPerDay : 0.75f;
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

            TickBeams(Time.unscaledDeltaTime);
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
            public Vector3 Position(GalaxyGenerator g) => Fleet != null ? Fleet.transform.position : g.Systems[Base.SystemId].Position + Vector3.up * 1.2f;
        }

        private void CombatRound(float dt)
        {
            if (_generator == null) return;
            var fm = FleetManager.Instance;
            if (fm == null) return;

            // Флоты на орбитах по системам
            var bySystem = new Dictionary<int, List<FleetView>>();
            foreach (var fv in fm.AllFleets)
            {
                var d = fv?.Data;
                if (d == null || d.Destroyed) continue;
                if (d.State == FleetState.InHyperlane) { _disengaged.Remove(d.Id); continue; }
                if (_disengaged.Contains(d.Id)) continue;
                if (!bySystem.TryGetValue(d.CurrentSystemId, out var list)) bySystem[d.CurrentSystemId] = list = new List<FleetView>();
                list.Add(fv);
            }

            var engaged = new HashSet<int>();
            var activeSystems = new HashSet<int>();

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

                foreach (var p in parts)
                {
                    if (p.Fleet == null) continue;
                    if (HasEnemy(parts, p.Owner))
                    {
                        p.Fleet.Data.InCombat = true;
                        engaged.Add(p.Fleet.Data.Id);
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
            }

            // Корабли, которые больше не в бою
            foreach (var fv in fm.AllFleets)
                if (fv?.Data != null && fv.Data.InCombat && !engaged.Contains(fv.Data.Id)) fv.Data.InCombat = false;

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

        private void FireRound(Combatant shooter, List<Combatant> parts, Battle battle, float dt)
        {
            float damage, fireRate;
            WeaponDamageType weapon;
            float cooldown;
            if (shooter.Fleet != null)
            {
                var d = shooter.Fleet.Data;
                damage = d.Damage; fireRate = d.FireRate * LeaderManager.AdmiralFireRateMult(d.OwnerId, battle.SystemId);
                weapon = d.PrimaryWeapon;
                cooldown = d.FireCooldown;
            }
            else
            {
                damage = shooter.Base.Damage; fireRate = shooter.Base.FireRate; weapon = shooter.Base.Weapon;
                cooldown = shooter.Base.Cooldown;
            }
            if (damage <= 0f || fireRate <= 0f) return;

            cooldown -= dt;
            int shots = 0;
            while (cooldown <= 0f && shots < 4)
            {
                var target = PickTarget(shooter, parts);
                if (!target.HasValue) { cooldown = 0f; break; }
                Shoot(shooter, target.Value, damage, weapon, battle);
                cooldown += 1f / Mathf.Max(0.05f, fireRate * ShotsPerDay);
                shots++;
            }

            if (shooter.Fleet != null) shooter.Fleet.Data.FireCooldown = cooldown;
            else shooter.Base.Cooldown = cooldown;
        }

        /// <summary>Выбор цели: прежняя, если жива; иначе случайная с весом по размеру.</summary>
        private Combatant? PickTarget(Combatant shooter, List<Combatant> parts)
        {
            int sKey = shooter.Key;
            if (_targets.TryGetValue(sKey, out int tKey))
                foreach (var p in parts)
                    if (p.Key == tKey && p.Alive && Diplomacy.AtWar(shooter.Owner, p.Owner)) return p;

            float total = 0f;
            foreach (var p in parts)
                if (p.Alive && Diplomacy.AtWar(shooter.Owner, p.Owner)) total += TargetWeight(p);
            if (total <= 0f) return null;

            float r = Random.value * total;
            foreach (var p in parts)
            {
                if (!p.Alive || !Diplomacy.AtWar(shooter.Owner, p.Owner)) continue;
                r -= TargetWeight(p);
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

            // Уклонение (у базы его нет)
            if (target.Fleet != null)
            {
                var db = EmpireBonuses.For(target.Owner);
                float evade = Mathf.Clamp01((target.Fleet.Data.Evasion * db.EvasionMult
                                             * LeaderManager.AdmiralEvasionMult(target.Owner, battle.SystemId) - ab.Accuracy) / 100f);
                if (Random.value < evade * 0.45f)
                {
                    SpawnBeam(from, to, wc, 0.12f);
                    SpawnFloater(to, "МИМО", new Color(0.7f, 0.85f, 0.9f));
                    if (Random.value < 0.5f) SFXManager.PlayAt(WeaponSfx(weapon), from, 0.8f);
                    return;
                }
            }

            float raw = damage * ab.DamageMult(weapon);
            if (shooter.Fleet != null) raw *= LeaderManager.AdmiralDamageMult(shooter.Owner, battle.SystemId);
            float shieldBefore = target.Fleet != null ? target.Fleet.Data.ShieldPoints : target.Base.Shields;
            float dealt;
            if (target.Fleet != null) dealt = ApplyLayeredDamage(target.Fleet.Data, raw, weapon);
            else dealt = ApplyLayeredDamage(target.Base, raw, weapon);

            SpawnBeam(from, to, wc, 0.18f);
            ShotSound(weapon, from, to, shieldBefore > 0f, target.Fleet == null || target.Fleet.Data.HullClass == ShipClass.Destroyer);
            SpawnFloater(to, $"-{dealt:0}", wc);

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
                FleetManager.Instance?.IssueMoveOrder(fv, escape);
                Side(battle, d.OwnerId).Disengaged++;
                SpawnFloater(fv.transform.position, "ВЫХОД ИЗ БОЯ", UIManager.DS.Gold);
                SFXManager.PlayAt(Sfx.Disengage, fv.transform.position);
                if (d.OwnerId == 0)
                    NotificationCenter.Show("Корабль вышел из боя", $"{d.Name} тяжело повреждён и отходит на ремонт", NotificationCenter.Kind.Warning, 4f);
                return;
            }

            Side(battle, d.OwnerId).Lost++;
            Side(battle, killer).Kills++;
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
            SpawnExplosion(fv.transform.position, fv.Data.HullClass);
            SpawnFloater(fv.transform.position, "УНИЧТОЖЕН", UIManager.DS.Red);
            bool big = fv.Data.Type == FleetType.Military && fv.Data.HullClass != ShipClass.Corvette;
            SFXManager.PlayAt(big ? Sfx.ExplosionLarge : Sfx.ExplosionSmall, fv.transform.position, fv.Data.OwnerId == 0 ? 1f : 0.9f);
            OnShipDestroyed?.Invoke(fv.Data, killerOwner);

            string ownerName = fv.Data.OwnerId == 0 ? "Ваш корабль"
                : AIEmpireManager.Instance != null ? AIEmpireManager.Instance.AIName : "Противник";
            NotificationCenter.Show("Корабль уничтожен", $"{ownerName}: {fv.Data.Name}",
                fv.Data.OwnerId == 0 ? NotificationCenter.Kind.Danger : NotificationCenter.Kind.Success, 4f);

            FleetManager.Instance?.NotifyFleetDestroyed(fv);
            Destroy(fv.gameObject, 0.15f);
        }

        private readonly Dictionary<int, float> _ftlLocks = new Dictionary<int, float>();

        public bool TryEmergencyFtl(FleetView fleet) => TryEmergencyFtl(fleet, -1);

        /// <summary>
        /// Экстренный прыжок из боя по приказу (72% успеха, после сбоя — пауза 2,4 с).
        /// Корабль уходит в соседнюю систему — свою, если есть; preferredTarget — куда лететь дальше.
        /// </summary>
        public bool TryEmergencyFtl(FleetView fleet, int preferredTarget)
        {
            if (fleet?.Data == null || !fleet.Data.InCombat) return false;
            if (_ftlLocks.TryGetValue(fleet.Data.Id, out float lockUntil) && lockUntil > Time.unscaledTime) return false;
            if (_generator == null) return false;

            if (Random.value >= 0.72f)
            {
                _ftlLocks[fleet.Data.Id] = Time.unscaledTime + 2.4f;
                SpawnFloater(fleet.transform.position, "СБОЙ ГПД", UIManager.DS.Gold);
                SFXManager.PlayAt(Sfx.FtlFail, fleet.transform.position);
                return false;
            }

            int escape = EscapeSystem(fleet.Data);
            if (escape < 0) return false;

            fleet.Data.InCombat = false;
            fleet.Data.Path.Clear();
            fleet.Data.OrderQueue.Clear();
            _disengaged.Add(fleet.Data.Id);
            FleetManager.Instance?.IssueMoveOrder(fleet, escape);
            if (preferredTarget >= 0 && preferredTarget != escape)
            {
                var tail = GalaxyPathfinder.FindPath(escape, preferredTarget, _generator);
                if (tail != null) foreach (int step in tail) fleet.Data.Path.Enqueue(step);
            }
            var battle = GetBattle(fleet.Data.CurrentSystemId);
            if (battle != null) Side(battle, fleet.Data.OwnerId).Disengaged++;
            SpawnFloater(fleet.transform.position, "ГПД АКТИВИРОВАН", UIManager.DS.NeonCyan);
            SFXManager.PlayAt(Sfx.Disengage, fleet.transform.position);
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
            b = new Battle { SystemId = sysId };
            _battles[sysId] = b;
            UpdateSideStats(b, parts);
            foreach (var s in b.Sides.Values) { s.StartShips = s.Ships; s.StartHp = s.Hp; }

            if (b.PlayerInvolved)
            {
                SFXManager.Play(Sfx.BattleStart);
                var sys = _generator.Systems[sysId];
                NotificationCenter.Show("Сражение!", $"Бой в системе {sys.Name}. Выделите флот, чтобы следить за боем и при необходимости отступить",
                    NotificationCenter.Kind.Danger, 6f);
            }
            return b;
        }

        private void UpdateSideStats(Battle b, List<Combatant> parts)
        {
            foreach (var s in b.Sides.Values) { s.Ships = 0; s.Hp = 0f; s.Power = 0f; s.HasStarbase = false; }
            foreach (var p in parts)
            {
                if (!p.Alive) continue;
                var s = Side(b, p.Owner);
                if (p.Fleet != null)
                {
                    var d = p.Fleet.Data;
                    s.Ships++;
                    s.Hp += d.HullPoints + d.ArmorPoints + d.ShieldPoints;
                    s.Power += CombatMath.Power(d);
                }
                else
                {
                    s.HasStarbase = true;
                    s.Hp += p.Base.Hull + p.Base.Armor + p.Base.Shields;
                    s.Power += StarbasePower(p.Base);
                }
                if (s.StartHp < s.Hp) s.StartHp = s.Hp;
                if (s.StartShips < s.Ships) s.StartShips = s.Ships;
            }
        }

        private void EndBattle(Battle b)
        {
            _battles.Remove(b.SystemId);
            if (b.Marker != null) Destroy(b.Marker);
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
            else if (lost) SFXManager.Play(Sfx.BattleDefeat);
            string title = won ? $"Победа при {sys.Name}" : lost ? $"Поражение при {sys.Name}" : $"Бой при {sys.Name} завершён";
            string body = $"{Mathf.CeilToInt(b.Days)} дн. · уничтожено врагов: {enemyLost}" +
                          (enemyDisengaged > 0 ? $" (бежало {enemyDisengaged})" : "") +
                          $" · наши потери: {me.Lost}" + (me.Disengaged > 0 ? $" (вышло из боя {me.Disengaged})" : "") +
                          $" · урон {me.DamageDealt:N0} / {me.DamageTaken:N0}";
            NotificationCenter.Show(title, body, won ? NotificationCenter.Kind.Success : lost ? NotificationCenter.Kind.Danger : NotificationCenter.Kind.Info, 9f);
        }

        // ==================== ЗВЁЗДНЫЕ БАЗЫ: ДАННЫЕ ====================

        private bool IsCapital(StarSystem sys)
        {
            if (sys.OwnerId == 0) return sys.Id == EconomyManager.PlayerCapitalId;
            var ai = AIEmpireManager.Instance;
            return ai != null && sys.OwnerId == AIEmpireManager.AIOwnerId && sys.Id == ai.CapitalSystemId;
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

            sb.MaxHull = (400f + colonies * 150f) * k * b.HullMult;
            sb.MaxArmor = (200f + colonies * 60f) * k * b.ArmorMult;
            sb.MaxShields = (150f + colonies * 40f) * k * b.ShieldMult;
            sb.Damage = (9f + colonies * 4f) * k;
            sb.FireRate = 1f;

            // Масштабируем текущие значения при изменении максимума
            if (oldMaxHull > 0f) sb.Hull = sb.Hull / oldMaxHull * sb.MaxHull; else sb.Hull = sb.MaxHull;
            if (oldMaxArmor > 0f) sb.Armor = sb.Armor / oldMaxArmor * sb.MaxArmor; else sb.Armor = sb.MaxArmor;
            if (oldMaxShields > 0f) sb.Shields = sb.Shields / oldMaxShields * sb.MaxShields; else sb.Shields = sb.MaxShields;
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
            sb.Disabled = true;
            sb.Hull = 0f;
            sb.Shields = 0f;
            var sys = _generator.Systems[sb.SystemId];
            SpawnExplosion(sys.Position + Vector3.up * 1.2f, ShipClass.Destroyer);
            SFXManager.PlayAt(Sfx.StarbaseDown, sys.Position, 1f);
            if (sys.OwnerId == 0)
                NotificationCenter.Show("Звёздная база выведена из строя", $"{sys.Name}: враг может начать осаду", NotificationCenter.Kind.Danger, 7f);
            else if (attacker == 0)
                NotificationCenter.Show("Вражеская база подавлена", $"{sys.Name}: держите флот на орбите — идёт осада", NotificationCenter.Kind.Success, 6f);
        }

        /// <summary>Ежедневный ремонт баз вне боя (выведенная из строя база оживает на половине прочности).</summary>
        private void HandleDay(int day, int month, int year)
        {
            if (_generator == null) return;
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
            // Захваченная база переходит к новому владельцу повреждённой, но в строю
            _starbases.Remove(sys.Id);
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

        /// <summary>
        /// Звук выстрела у стрелка и попадания у цели. В крупных боях выстрелов очень много —
        /// часть пропускается (плюс лимит голосов в SFXManager), чтобы не было «каши».
        /// </summary>
        private static void ShotSound(WeaponDamageType weapon, Vector3 from, Vector3 to, bool shieldHit, bool heavyTarget)
        {
            if (Random.value < 0.75f) SFXManager.PlayAt(WeaponSfx(weapon), from);
            if (Random.value < 0.55f)
            {
                // Ракеты долетают с задержкой — звук попадания чуть позже
                var sfx = shieldHit ? Sfx.HitShield : Sfx.HitArmor;
                float pitch = heavyTarget ? 0.85f : 1f;
                SFXManager.PlayAt(sfx, to, 1f, pitch);
            }
        }

        private static Color WeaponColor(WeaponDamageType t) => t switch
        {
            WeaponDamageType.Energy => new Color(1f, 0.25f, 0.22f, 1f),
            WeaponDamageType.Kinetic => new Color(1f, 0.85f, 0.35f, 1f),
            _ => new Color(0.45f, 1f, 0.55f, 1f)
        };

        private void SpawnBeam(Vector3 from, Vector3 to, Color col, float width)
        {
            if (_beams.Count > 160) return;   // крупные сражения — не захламляем сцену
            var go = new GameObject("CombatBeam");
            go.transform.SetParent(_fxRoot.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.startWidth = width;
            lr.endWidth = width * 0.35f;
            Vector3 jitter = Random.insideUnitSphere * 0.35f;
            lr.SetPosition(0, from + Vector3.up * 0.4f);
            lr.SetPosition(1, to + Vector3.up * 0.4f + jitter);
            var sh = ShaderCache.Unlit;
            if (sh != null) lr.material = new Material(sh) { color = col };
            lr.startColor = col;
            lr.endColor = new Color(col.r, col.g, col.b, 0.15f);
            _beams.Add(lr);
        }

        private void SpawnExplosion(Vector3 pos, ShipClass hull)
        {
            var go = new GameObject("Explosion");
            go.transform.SetParent(_fxRoot.transform, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.6f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            float size = hull == ShipClass.Destroyer ? 1.6f : hull == ShipClass.Frigate ? 1.2f : 0.9f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f * size, 7f * size);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f * size, 0.7f * size);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.15f));
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(30 * size)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var sh = ShaderCache.Sprite;
            if (sh != null) r.material = new Material(sh);
            ps.Play();
            Destroy(go, 2f);
        }

        private void TickBeams(float dt)
        {
            for (int i = _beams.Count - 1; i >= 0; i--)
            {
                var lr = _beams[i];
                if (lr == null) { _beams.RemoveAt(i); continue; }
                var c0 = lr.startColor;
                c0.a -= dt * 4f;
                lr.startColor = c0;
                if (c0.a <= 0.02f)
                {
                    Destroy(lr.gameObject);
                    _beams.RemoveAt(i);
                }
            }
        }

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

        // ==================== ПАНЕЛЬ СРАЖЕНИЯ ====================

        private GameObject _hud;
        private Text _hudTitle, _myLabel, _enemyLabel, _myStats, _enemyStats, _balanceText, _hudFooter;
        private RectTransform _myHp, _enemyHp, _balanceBar;
        private Image _myEmblem, _enemyEmblem;
        private int _hudSystem = -1;

        private void BuildHud()
        {
            var ui = UIManager.Instance;
            if (ui == null || ui.HudCanvas == null) return;

            var rt = LGBuild.Rect(ui.HudCanvas.transform, "[UI] BattlePanel");
            rt.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -118), new Vector2(660, 150));
            _hud = rt.gameObject;
            var bg = _hud.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.03f, 0.04f, 1f);
            bg.raycastTarget = true;
            LG.Glass(_hud, 22f).SetRim(new Color(1f, 0.38f, 0.38f, 0.6f));
            var mo = LG.Motion(_hud, LGAppear.Kind.SlideDown);
            mo.distance = 24f;

            var head = LGBuild.Rect(rt, "Head");
            head.TopBand(10, 22, 16, 16);
            var hi = LGIcons.Create(head, LGIcon.Swords, 16, UIManager.DS.Red);
            hi.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-150, 0), new Vector2(16, 16));
            _hudTitle = LGBuild.Label(head, "", 13, UIManager.DS.Gold, TextAnchor.MiddleCenter, bold: true);

            _myEmblem = SideBlock(rt, true, out _myLabel, out _myStats, out _myHp);
            _enemyEmblem = SideBlock(rt, false, out _enemyLabel, out _enemyStats, out _enemyHp);

            var center = LGBuild.Rect(rt, "Balance");
            center.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -42), new Vector2(150, 56));
            _balanceText = LGBuild.Label(center, "", 12, Color.white, TextAnchor.UpperCenter, bold: true);
            var bh = LGBuild.Rect(center, "Bar");
            bh.anchorMin = new Vector2(0, 0);
            bh.anchorMax = new Vector2(1, 0);
            bh.offsetMin = new Vector2(0, 14);
            bh.offsetMax = new Vector2(0, 24);
            _balanceBar = LGBuild.Bar(bh, FleetIndicator.OwnColor, 0.5f, 8f);

            _hudFooter = LGBuild.Label(rt, "", 10, UIManager.DS.TextMuted, TextAnchor.LowerLeft);
            _hudFooter.rectTransform.Stretch(16, 12, 230, 0);

            var row = LGBuild.Rect(rt, "Buttons");
            row.anchorMin = new Vector2(1, 0);
            row.anchorMax = new Vector2(1, 0);
            row.pivot = new Vector2(1, 0);
            row.sizeDelta = new Vector2(214, 30);
            row.anchoredPosition = new Vector2(-12, 8);
            var cam = LGBuild.Button(row, "Camera", UIManager.DS.BtnNeutral, new Color(0.6f, 0.9f, 1f, 0.5f), FocusBattle, LGIcon.Target, "КАМЕРА", 10);
            ((RectTransform)cam.transform).Column(0f, 0.42f, 0, 3);
            var retreat = LGBuild.Button(row, "Retreat", UIManager.DS.BtnDanger, UIManager.DS.Red, RetreatAll, LGIcon.Warning, "ОТСТУПИТЬ", 10);
            ((RectTransform)retreat.transform).Column(0.42f, 1f, 3, 0);
            TooltipHelper.Attach(retreat.gameObject,
                "<b>Экстренное отступление</b>\nКаждый ваш корабль в бою пытается совершить аварийный прыжок (72% успеха). " +
                "Сбой — повторная попытка через пару секунд.");

            LG.Skin(_hud.transform);
            _hud.SetActive(false);
        }

        private static Image SideBlock(RectTransform parent, bool left, out Text label, out Text stats, out RectTransform hp)
        {
            var block = LGBuild.Rect(parent, left ? "Mine" : "Enemy");
            block.anchorMin = new Vector2(left ? 0f : 0.5f, 0f);
            block.anchorMax = new Vector2(left ? 0.5f : 1f, 1f);
            block.offsetMin = new Vector2(left ? 16 : 85, 44);
            block.offsetMax = new Vector2(left ? -85 : -16, -36);

            var emblem = LGBuild.Panel(block, "Emblem", new Color(0.1f, 0.2f, 0.2f));
            emblem.rectTransform.At(new Vector2(left ? 0 : 1, 1), new Vector2(left ? 0 : 1, 1), Vector2.zero, new Vector2(28, 28));
            LG.Platter(emblem.gameObject, 14f).FillMultiplier = 2f;
            var icon = LGIcons.Create(emblem.transform, LGIcon.Fleet, 16, Color.white);

            label = LGBuild.Label(block, "", 12, Color.white, left ? TextAnchor.UpperLeft : TextAnchor.UpperRight, bold: true);
            label.rectTransform.Stretch(left ? 36 : 0, 0, left ? 0 : 36, 0);
            stats = LGBuild.Label(block, "", 10, UIManager.DS.TextMuted, left ? TextAnchor.UpperLeft : TextAnchor.UpperRight);
            stats.rectTransform.Stretch(left ? 36 : 0, 0, left ? 0 : 36, 16);
            var host = LGBuild.Rect(block, "Hp");
            host.anchorMin = new Vector2(0, 0);
            host.anchorMax = new Vector2(1, 0);
            host.offsetMin = new Vector2(0, 0);
            host.offsetMax = new Vector2(0, 8);
            hp = LGBuild.Bar(host, left ? FleetIndicator.OwnColor : FleetIndicator.EnemyColor, 1f, 6f);
            return emblem;
        }

        /// <summary>Какой бой показывать: где выделенный флот, иначе самый крупный с участием игрока.</summary>
        private Battle HudBattle()
        {
            var sel = FleetManager.Instance?.SelectedFleet;
            if (sel?.Data != null)
            {
                var b = GetBattle(sel.Data.CurrentSystemId);
                if (b != null && b.PlayerInvolved) return b;
            }
            Battle best = null;
            float bestPower = -1f;
            foreach (var b in _battles.Values)
            {
                if (!b.PlayerInvolved) continue;
                float p = 0f;
                foreach (var s in b.Sides.Values) p += s.Power;
                if (p > bestPower) { bestPower = p; best = b; }
            }
            return best;
        }

        private void RefreshHud()
        {
            if (_hud == null) return;
            bool inSystem = SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView;
            var b = inSystem ? null : HudBattle();
            bool show = b != null;
            if (show != LG.IsVisible(_hud)) { if (show) LG.Show(_hud); else LG.Hide(_hud); }
            if (!show) { _hudSystem = -1; return; }
            _hudSystem = b.SystemId;

            var sys = _generator.Systems[b.SystemId];
            int others = 0;
            foreach (var x in _battles.Values) if (x.PlayerInvolved && x != b) others++;
            _hudTitle.text = $"СРАЖЕНИЕ ПРИ {sys.Name.ToUpper()}  ·  {Mathf.CeilToInt(b.Days)} дн." + (others > 0 ? $"  ·  ещё боёв: {others}" : "");

            var me = Side(b, 0);
            SideStats enemy = null;
            foreach (var kv in b.Sides) if (kv.Key != 0 && (enemy == null || kv.Value.Power > enemy.Power)) enemy = kv.Value;
            enemy ??= new SideStats();

            string enemyName = enemy.Owner == AIEmpireManager.AIOwnerId && AIEmpireManager.Instance != null ? AIEmpireManager.Instance.AIName : "Противник";
            _myLabel.text = UIManager.Instance?.SelectedFaction?.Name ?? "Ваш флот";
            _enemyLabel.text = enemyName;
            _myEmblem.color = new Color(0.05f, 0.25f, 0.24f);
            _enemyEmblem.color = new Color(0.3f, 0.06f, 0.06f);

            _myStats.text = SideLine(me);
            _enemyStats.text = SideLine(enemy);
            LGBuild.SetBar(_myHp, me.StartHp > 0 ? me.Hp / me.StartHp : 0f);
            LGBuild.SetBar(_enemyHp, enemy.StartHp > 0 ? enemy.Hp / enemy.StartHp : 0f);

            float total = me.Power + enemy.Power;
            float share = total > 0f ? me.Power / total : 0.5f;
            LGBuild.SetBar(_balanceBar, share, Color.Lerp(UIManager.DS.Red, UIManager.DS.Green, share));
            float ratio = me.Power / Mathf.Max(1f, enemy.Power);
            _balanceText.text = ratio >= 1.1f ? $"<color=#5CF59A>ПЕРЕВЕС ×{ratio:0.0}</color>"
                              : ratio <= 0.9f ? $"<color=#FF6A6A>ВРАГ СИЛЬНЕЕ ×{1f / Mathf.Max(0.01f, ratio):0.0}</color>"
                              : "<color=#F2C747>РАВНЫЕ СИЛЫ</color>";

            _hudFooter.text = $"Урон: нанесено {me.DamageDealt:N0} · получено {me.DamageTaken:N0}   ·   " +
                              $"уничтожено {me.Kills} · потеряно {me.Lost}" + (me.Disengaged > 0 ? $" · вышло из боя {me.Disengaged}" : "");
        }

        private static string SideLine(SideStats s)
        {
            string ships = s.Ships > 0 ? $"кораблей {s.Ships}/{Mathf.Max(s.Ships, s.StartShips)}" : "кораблей нет";
            return $"{ships}{(s.HasStarbase ? " · звёздная база" : "")} · мощь {Mathf.RoundToInt(s.Power):N0}";
        }

        private void FocusBattle()
        {
            if (_hudSystem < 0 || _generator == null) return;
            var cam = FindAnyObjectByType<StellarisClone.Cam.StrategyCameraController>();
            if (cam != null) cam.FocusOn(_generator.Systems[_hudSystem].Position);
        }

        private void RetreatAll()
        {
            var fm = FleetManager.Instance;
            if (fm == null || _hudSystem < 0) return;
            int tried = 0, ok = 0;
            foreach (var f in new List<FleetView>(fm.AllFleets))
            {
                var d = f?.Data;
                if (d == null || d.OwnerId != 0 || !d.InCombat || d.CurrentSystemId != _hudSystem) continue;
                tried++;
                if (TryEmergencyFtl(f)) ok++;
            }
            if (tried > 0)
                NotificationCenter.Show("Отступление", $"Ушли из боя: {ok} из {tried}. Остальные пробуют снова через пару секунд",
                    ok == tried ? NotificationCenter.Kind.Info : NotificationCenter.Kind.Warning, 4f);
        }

        private void HideHud()
        {
            if (_hud != null) _hud.SetActive(false);
        }
    }
}
