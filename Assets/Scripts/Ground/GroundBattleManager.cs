using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;
using StellarisClone.Rendering;
using Random = UnityEngine.Random;
using SfxId = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Наземный бой в духе Dawn of War: корень далеко под галактикой (своя камера, свет, навмеш), стратегия на паузе.
    /// Отвечает за жизненный цикл боя (запуск, такт, итоги, уборку), стороны и их ресурсы, точки и тикеты,
    /// туман войны, сооружения (турели, бункеры, штаб, щит), мины, подкрепления капсулами и орбитальные удары.
    /// Правила одни для игрока и тактического ИИ.
    /// </summary>
    public sealed class GroundBattleManager : MonoBehaviour
    {
        public static GroundBattleManager Instance { get; private set; }
        public static bool InBattle => Instance != null && Instance._running;

        public const float TimeLimit = 15f * 60f;
        /// <summary>Первые минуты тикеты за точки не тают: у защитника все районы с начала, атакующему нужно время дойти.</summary>
        public const float BleedGrace = 150f;
        public const float TicketBleedInterval = 10f;
        public const float OrbitalCooldown = 75f;
        public const float OrbitalEnergy = 60f;

        public GroundBattleSetup Setup { get; private set; }
        public GroundMap Map { get; private set; }
        public readonly BattleSide[] Sides = new BattleSide[2];
        public BattleFx Fx { get; private set; }
        public GroundSfx Sfx { get; private set; }
        public BattleCamera Cam { get; private set; }
        public GroundHud Hud { get; private set; }
        public GroundInput Controls { get; private set; }
        public float Elapsed { get; private set; }
        public bool Paused { get; private set; }
        public float SpeedMult { get; private set; } = 1f;
        public float WeatherRangeMult { get; private set; } = 1f;
        public float WeatherAccuracyMult { get; private set; } = 1f;
        public bool Sandstorm { get; private set; }
        public System.Random Rng { get; private set; }
        public BattleSide Player => Sides[0].IsPlayer ? Sides[0] : Sides[1].IsPlayer ? Sides[1] : null;

        private Transform _root;
        private bool _running, _ending;
        private Action<GroundBattleResult> _onDone;
        private float _bleed, _fog, _structTimer, _stormTimer, _stormLeft;
        private readonly List<Soldier> _all = new List<Soldier>();
        private readonly List<DistrictType> _destroyedDistricts = new List<DistrictType>();
        private int _civilians;
        private readonly float[] _revealUntil = new float[2];

        // Состояние стратегии на время боя
        private Camera _galaxyCam;
        private int _galaxyMask, _galaxyEventMask;
        private AudioListener _galaxyListener;
        private Behaviour _galaxyController;
        private int _speedBefore;
        private Color _ambientBefore;
        private bool _fogBefore;
        private Color _fogColorBefore;
        private FogMode _fogModeBefore;
        private float _fogStartBefore, _fogEndBefore;
        private UnityEngine.Rendering.AmbientMode _ambientModeBefore;
        private Color _skyBefore, _equatorBefore, _groundBefore;
        private Light _sunBefore;
        private float _shadowDistBefore = -1f;
        private int _cascadesBefore;

        public static GroundBattleManager Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[Ground] BattleManager");
            return go.AddComponent<GroundBattleManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ================================================================ Запуск

        /// <summary>Начать бой. onDone получает итоги, когда бой закончится и галактика вернётся.</summary>
        public void Begin(GroundBattleSetup setup, Action<GroundBattleResult> onDone)
        {
            if (_running) return;
            Setup = setup;
            _onDone = onDone;
            Rng = new System.Random(setup.Seed != 0 ? setup.Seed : Environment.TickCount);
            Elapsed = 0f;
            Paused = false;
            SpeedMult = 1f;
            _ending = false;
            _bleed = TicketBleedInterval;
            _destroyedDistricts.Clear();
            _civilians = 0;
            _all.Clear();
            _revealUntil[0] = _revealUntil[1] = 0f;

            HideStrategy();
            _root = new GameObject("GroundBattle").transform;
            _root.position = new Vector3(0f, -20000f, 0f);

            Map = GroundMapGenerator.Build(setup, _root, Rng);
            SetupEnvironment();
            GroundMapGenerator.BuildNavMesh(_root);

            Fx = new GameObject("BattleFx").AddComponent<BattleFx>();
            Fx.transform.SetParent(_root, false);
            Sfx = new GroundSfx();
            GoreFx.Begin(_root, Map);

            for (int i = 0; i < 2; i++)
            {
                var ss = setup.Side(i);
                Sides[i] = new BattleSide
                {
                    Index = i, Setup = ss, Color = ss.Color, FactionKey = ss.FactionKey, IsPlayer = ss.IsPlayer,
                    SlotLimit = ss.Slots, Beacon = i == 0 ? Map.AttackerBeacon : Map.DefenderBeacon,
                    IncomeMult = i == 1 ? 1.15f : 1f, Requisition = i == 1 ? 250f : 600f, Energy = i == 1 ? 60f : 150f,
                    Tickets = i == 0 ? 650f : 500f
                };
                Sides[i].Ai = ss.IsPlayer ? null : new GroundCommanderAI(Sides[i]);
            }

            // Камера — над своими: у атакующего точка высадки, у защитника штаб
            bool defends = Sides[1].IsPlayer && !Sides[0].IsPlayer;
            Cam = BattleCamera.Create(_root, Map, defends ? Map.DefenderBeacon + new Vector3(0f, 0f, -30f) : Map.AttackerBeacon + new Vector3(0f, 0f, 30f));
            BattleAmbience.Create(_root);
            Controls = gameObject.AddComponent<GroundInput>();
            Hud = GroundHud.Create(this);

            SpawnStartingForces();
            _running = true;
            BattleCamera.PlayIntro();
            Sfx.Play(SfxId.BattleStart, 0.9f);
        }

        private void SpawnStartingForces()
        {
            var a = Sides[0];
            var d = Sides[1];
            // Атакующий: десант капсулами у точки высадки
            string[] opening = { "assault", "assault", "heavy", "mg", "rocket" };
            for (int i = 0; i < opening.Length; i++)
            {
                var def = UnitCatalog.Get(opening[i]);
                var pos = Map.Snap(a.Beacon + new Vector3(-18f + i * 12f, 0f, 6f + (i % 2) * 6f));
                var sq = SpawnSquad(a, def, pos);
                if (sq != null) sq.Veterancy = Mathf.Clamp(Setup.Attacker.StartVeterancy, 0, 3);
            }
            // Командир десанта — всегда (бесплатно, один на бой); адмирал флота только усиливает его
            SpawnHero(a, a.Beacon + new Vector3(0f, 0f, 2f));

            // Защитник: гарнизон у района и штаба
            int garrison = Mathf.Clamp(Setup.GarrisonSquads, 1, 7);
            var spots = new List<Vector3>();
            foreach (var p in Map.Points) if (p.Owner == 1) spots.Add(p.Pos);
            spots.Add(d.Beacon);
            for (int i = 0; i < garrison; i++)
            {
                string id = i % 4 == 2 ? "mg" : i % 4 == 3 ? "rocket" : "assault";
                var spot = spots[i % spots.Count] + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
                var sq = SpawnSquad(d, UnitCatalog.Get(id), Map.Snap(spot));
                if (sq != null) sq.TakeCover(spot);
            }
            if (Setup.Defender.GeneralLeaderId >= 0) SpawnHero(d, d.Beacon);
        }

        // ================================================================ Спавн

        public Squad SpawnSquad(BattleSide side, UnitDef def, Vector3 pos, bool free = true)
        {
            if (def == null) return null;
            var sq = new Squad { Side = side, Def = def, Destination = pos };
            sq.MaxMorale = sq.Morale = 100f * side.Setup.MoraleMult;
            side.Squads.Add(sq);
            for (int i = 0; i < def.Size; i++)
            {
                var p = pos + new Vector3((i % 3 - 1) * 1.6f, 0f, (i / 3) * 1.6f);
                SpawnMember(sq, i == 0, Map.Snap(p));
            }
            return sq;
        }

        public Soldier SpawnMember(Squad sq, bool sergeant, Vector3 pos)
        {
            var go = new GameObject(sq.Def.Id);
            go.transform.SetParent(_root, true);
            var s = go.AddComponent<Soldier>();
            s.Init(sq, sq.Def, sergeant, Map.Snap(pos), sq.Side.Setup.HpMult * (1f + 0.1f * sq.Veterancy), sq.Side.Setup.SpeedMult);
            sq.Members.Add(s);
            _all.Add(s);
            if (!sq.Side.IsPlayer && Player != null) s.SetVisible(sq.VisibleTo[Player.Index]);
            return s;
        }

        public void SpawnHero(BattleSide side, Vector3 pos)
        {
            var sq = SpawnSquad(side, UnitCatalog.Get("hero"), Map.Snap(pos));
            if (sq != null) sq.Veterancy = Mathf.Clamp(side.Setup.GeneralLevel / 2, 0, 3);
        }

        /// <summary>Заказать отряд: капсула падает в точку (давит всех под собой), отряд появляется после удара.</summary>
        public bool OrderReinforcement(BattleSide side, UnitDef def, Vector3 at)
        {
            if (def == null || !CanAfford(side, def, out _)) return false;
            if (def.Role == UnitRole.Mercenary)
            {
                var eco = EconomyManager.Instance;
                if (side.Setup.Owner == 0 && eco != null) eco.EnergyCredits -= UnitCatalog.MercenaryHelium;
            }
            else
            {
                side.Requisition -= def.Requisition;
                side.Energy -= def.Energy;
            }
            at = Map.Clamp(at, 10f);
            // Капсулы — только на своей половине или у своих точек
            if (!CanDropAt(side, at)) at = side.Beacon + new Vector3(Random.Range(-8f, 8f), 0f, Random.Range(-4f, 8f));
            Fx.DropPod(Map.Snap(at), side.Color, () =>
            {
                if (!_running) return;
                CrushAt(Map.Snap(at), 3.2f, side.Index);
                var sq = SpawnSquad(side, def, Map.Snap(at));
                if (sq != null) { sq.Veterancy = Mathf.Clamp(side.Setup.StartVeterancy, 0, 3); sq.Shout(GroundCallouts.Landed(sq)); }
            });
            return true;
        }

        public bool CanAfford(BattleSide side, UnitDef def, out string why)
        {
            why = null;
            if (side.SlotsUsed + def.Slots > side.SlotLimit) { why = Loc.T("Лимит мест исчерпан"); return false; }
            if (def.Role == UnitRole.Mercenary)
            {
                var eco = EconomyManager.Instance;
                if (side.Setup.Owner != 0 || eco == null || eco.EnergyCredits < UnitCatalog.MercenaryHelium) { why = Loc.F("Нужно {0:0} гелия-3 из казны", UnitCatalog.MercenaryHelium); return false; }
                return true;
            }
            if (side.Requisition < def.Requisition) { why = Loc.T("Не хватает реквизиции"); return false; }
            if (side.Energy < def.Energy) { why = Loc.T("Не хватает энергии"); return false; }
            return true;
        }

        public bool CanDropAt(BattleSide side, Vector3 at)
        {
            if ((at - side.Beacon).sqrMagnitude < 40f * 40f) return true;
            foreach (var p in Map.Points) if (p.Owner == side.Index && (p.Pos - at).sqrMagnitude < 25f * 25f) return true;
            return false;
        }

        /// <summary>Удар капсулы: всё живое под ней — в клочья.</summary>
        public void CrushAt(Vector3 pos, float r, int side)
        {
            foreach (var v in SoldiersInRadius(pos, r))
                Combat.Kill(v, Combat.PodImpact, (v.Pos - pos).normalized + Vector3.up, null);
            BattleCamera.Shake(pos, 0.9f);
            AddCrater(pos, 2.2f);
        }

        // ================================================================ Такт

        private void Update()
        {
            if (!_running) return;
            if (_ending) return;
            float dt = Time.deltaTime;
            float now = Time.time;
            if (dt <= 0f) { Hud?.Tick(); return; }
            Elapsed += dt;

            for (int i = 0; i < 2; i++)
                foreach (var sq in Sides[i].Squads.ToArray()) sq.Tick(dt, now);
            _all.RemoveAll(s => s == null || s.Dead);

            TickPoints(dt);
            TickIncome(dt);
            TickStructures(dt);
            TickMines();
            TickHazards(dt);
            TickWeather(dt);
            if ((_fog -= dt) <= 0f) { _fog = 0.25f; UpdateFog(); }
            foreach (var s in Sides)
            {
                if (s.OrbitalCooldown > 0f) s.OrbitalCooldown -= dt;
                if (s.SpecialCooldown > 0f) s.SpecialCooldown -= dt;
                if (s.HeroRespawn > 0f && (s.HeroRespawn -= dt) <= 0f) { s.HeroRespawn = -1f; RespawnHero(s); }
                s.Ai?.Tick(dt);
            }
            TickMilitia(dt);

            if ((_bleed -= dt) <= 0f) { _bleed = TicketBleedInterval; BleedTickets(); }
            CheckVictory();
            Hud?.Tick();
        }

        // ---- Пространственные запросы ----

        public List<Soldier> SoldiersInRadius(Vector3 p, float r)
        {
            var list = new List<Soldier>();
            float r2 = r * r;
            foreach (var s in _all)
            {
                if (s == null || s.Dead) continue;
                Vector3 d = s.Pos - p; d.y = 0f;
                if (d.sqrMagnitude <= r2) list.Add(s);
            }
            return list;
        }

        public IEnumerable<Soldier> AllSoldiers => _all;

        public Squad NearestEnemySquad(Squad from, float range)
        {
            var enemy = Sides[1 - from.Side.Index];
            Squad best = null;
            float bd = range * range;
            Vector3 c = from.Center;
            foreach (var sq in enemy.Squads)
            {
                if (sq.Destroyed || !sq.VisibleTo[from.Side.Index]) continue;
                // По каждому бойцу: отряд может быть растянут
                foreach (var m in sq.Members)
                {
                    if (m == null || m.Dead) continue;
                    float d = (m.Pos - c).sqrMagnitude;
                    if (d < bd) { bd = d; best = sq; }
                }
            }
            return best;
        }

        public Destructible NearestEnemyStructure(Squad from, float range)
        {
            Destructible best = null;
            float bd = range * range;
            int enemy = 1 - from.Side.Index;
            Vector3 c = from.Center;
            foreach (var d in Map.Destructibles)
            {
                if (d.Dead || d.Side != enemy) continue;
                if (d.Weapon == null && d.Type != Destructible.Kind.Headquarters && d.Type != Destructible.Kind.Bunker) continue;
                float dist = (d.Pos - c).sqrMagnitude - d.Radius * d.Radius;
                if (dist < bd) { bd = dist; best = d; }
            }
            return best;
        }

        public bool TerrorNear(Squad sq, float r)
        {
            var c = sq.Center;
            foreach (var e in Sides[1 - sq.Side.Index].Squads)
                if (!e.Destroyed && e.Def.Terror && (e.Center - c).sqrMagnitude < r * r) return true;
            return false;
        }

        public bool HeroNear(Squad sq, float r)
        {
            var c = sq.Center;
            foreach (var e in sq.Side.Squads)
                if (!e.Destroyed && e.Def.Role == UnitRole.Hero && e != sq && (e.Center - c).sqrMagnitude < r * r) return true;
            return false;
        }

        public bool NearReinforcePoint(Squad sq)
        {
            var c = sq.Center;
            if ((c - sq.Side.Beacon).sqrMagnitude < 22f * 22f) return true;
            foreach (var p in Map.Points)
                if (p.Owner == sq.Side.Index && (p.Pos - c).sqrMagnitude < 14f * 14f) return true;
            return false;
        }

        // ---- Туман войны ----

        private void UpdateFog()
        {
            for (int side = 0; side < 2; side++)
            {
                var me = Sides[side];
                foreach (var sq in Sides[1 - side].Squads)
                {
                    if (sq.Destroyed) continue;
                    bool seen = false;
                    foreach (var m in sq.Members)
                    {
                        if (m == null || m.Dead) continue;
                        // Снайпер в укрытии невидим, пока к нему не подойдут вплотную
                        bool stealth = sq.Def.Stealth && sq.InCover && Time.time - m.LastShot > 3f;
                        float near = stealth ? 10f : 0f;
                        foreach (var my in me.Squads)
                        {
                            if (my.Destroyed) continue;
                            float sight = stealth ? near : my.Def.SightRange * WeatherSightMult;
                            foreach (var o in my.Members)
                            {
                                if (o == null || o.Dead) continue;
                                if ((o.Pos - m.Pos).sqrMagnitude < sight * sight) { seen = true; break; }
                            }
                            if (seen) break;
                        }
                        if (seen) break;
                    }
                    // «Видение» Провидца: вся вражеская армия на виду
                    if (!seen && Time.time < _revealUntil[side]) seen = true;
                    // Сооружения стороны тоже «видят» (бункеры, штаб)
                    if (!seen)
                        foreach (var d in Map.Destructibles)
                            if (!d.Dead && d.Side == side && d.Weapon != null && (d.Pos - sq.Center).sqrMagnitude < d.Weapon.Range * d.Weapon.Range) { seen = true; break; }
                    sq.VisibleTo[side] = seen;
                }
                foreach (var sq in me.Squads) sq.VisibleTo[side] = true;
            }
            // Рендер: игрок видит только то, что видят его отряды
            var player = Player;
            if (player == null) return;
            foreach (var sq in Sides[1 - player.Index].Squads)
                foreach (var m in sq.Members) if (m != null && !m.Dead) m.SetVisible(sq.VisibleTo[player.Index]);
        }

        public float WeatherSightMult => Sandstorm ? 0.55f : 1f;

        /// <summary>Сторона side видит всех врагов seconds секунд (способность «Видение»).</summary>
        public void RevealAllFor(int side, float seconds)
        {
            if (side < 0 || side > 1) return;
            _revealUntil[side] = Mathf.Max(_revealUntil[side], Time.time + seconds);
            _fog = 0f;
        }

        // ---- Точки, доход, тикеты ----

        private void TickPoints(float dt)
        {
            foreach (var p in Map.Points)
            {
                bool p0 = false, p1 = false;
                float r2 = StrategicPoint.Radius * StrategicPoint.Radius;
                foreach (var s in _all)
                {
                    if (s.Dead || s.Squad.Retreating || s.Def.Vehicle && s.Def.Role != UnitRole.Walker) continue;
                    Vector3 d = s.Pos - p.Pos; d.y = 0f;
                    if (d.sqrMagnitude > r2) continue;
                    if (s.SideIndex == 0) p0 = true; else p1 = true;
                    if (p0 && p1) break;
                }
                int before = p.Owner;
                p.Tick(dt, p0, p1);
                if (before != p.Owner) OnPointChanged(p, before);
                if (p.Ring != null)
                {
                    Color c = p.Owner >= 0 ? Sides[p.Owner].Color : Color.Lerp(new Color(0.8f, 0.8f, 0.8f), Sides[p.Progress > 0 ? 0 : 1].Color, Mathf.Abs(p.Progress));
                    p.Ring.sharedMaterial = GroundMats.Glow(c * 1.6f);
                    if (p.Flag != null) p.Flag.sharedMaterial = GroundMats.Glow(c * 1.3f);
                }
            }
        }

        private void OnPointChanged(StrategicPoint p, int before)
        {
            if (p.Owner >= 0)
            {
                Sfx.Play(p.Owner == (Player?.Index ?? 0) ? SfxId.OutpostBuilt : SfxId.AlertPop, 0.7f);
                Hud?.Flash(Loc.F("{0}: точка «{1}» захвачена", Sides[p.Owner].IsPlayer ? Loc.T("Мы") : Loc.T("Враг"), p.Name), Sides[p.Owner].Color);
                // Бунт: при низкой стабильности ополчение района сдаётся атакующему
                if (p.Owner == 0 && p.District == DistrictType.Urban && Setup.Stability < 40f && Rng.NextDouble() < 0.2)
                {
                    var sq = SpawnSquad(Sides[0], UnitCatalog.Get("militia"), p.Pos);
                    if (sq != null) sq.Shout(Loc.T("Мы с вами!"));
                }
            }
        }

        private void TickIncome(float dt)
        {
            foreach (var side in Sides)
            {
                IncomeOf(side, out float req, out float en);
                side.Requisition += req * dt;
                side.Energy += en * dt;
            }
        }

        /// <summary>Доход стороны в секунду: атакующего снабжает флот с орбиты (чем мощнее, тем больше), защитника — склады колонии; плюс точки.</summary>
        public void IncomeOf(BattleSide side, out float req, out float en)
        {
            bool attacker = side.Index == 0;
            req = attacker ? 6f + Mathf.Clamp(side.Setup.FleetPower / 4000f, 0f, 4f) : 2.5f;
            en = attacker ? 1.2f : 0.6f;
            foreach (var p in Map.Points) if (p.Owner == side.Index) { req += p.ReqIncome; en += p.EnergyIncome; }
            req *= side.IncomeMult;
            en *= side.IncomeMult;
        }

        private void BleedTickets()
        {
            if (Elapsed < BleedGrace) return;
            int a = 0, d = 0;
            foreach (var p in Map.Points) { if (p.Owner == 0) a++; else if (p.Owner == 1) d++; }
            if (a < d) Sides[0].Tickets -= (d - a) * 2f;
            else if (d < a) Sides[1].Tickets -= (a - d) * 3f;
        }

        private void CheckVictory()
        {
            if (_ending) return;
            var atk = Sides[0];
            var def = Sides[1];
            if (Map.Hq != null && Map.Hq.Dead) { End(true, Loc.T("Штаб колонии разрушен")); return; }
            if (atk.Tickets <= 0f) { End(false, Loc.T("У атакующих кончились силы")); return; }
            if (def.Tickets <= 0f) { End(true, Loc.T("Оборона сломлена")); return; }
            if (def.AliveSquads == 0 && Elapsed > 30f && !DefenderCanRecover()) { End(true, Loc.T("Защитники капитулировали")); return; }
            if (atk.AliveSquads == 0 && atk.Requisition < 100f && Elapsed > 30f) { End(false, Loc.T("Десант уничтожен")); return; }
            if (Elapsed >= TimeLimit)
            {
                int a = 0, d = 0;
                foreach (var p in Map.Points) { if (p.Owner == 0) a++; else if (p.Owner == 1) d++; }
                End(a > d, a > d ? Loc.T("Время вышло: атакующие держат большинство точек") : Loc.T("Время вышло: колония устояла"));
            }
        }

        /// <summary>Защитник ещё может собрать силы: у него есть городской район (ополчение) или деньги на отряд.</summary>
        private bool DefenderCanRecover()
        {
            if (Sides[1].Requisition >= 100f) return true;
            foreach (var p in Map.Points) if (p.Kind == PointKind.Urban && p.Owner == 1) return true;
            return false;
        }

        // ---- Сооружения ----

        private void TickStructures(float dt)
        {
            _structTimer -= dt;
            foreach (var d in Map.Destructibles)
            {
                if (d.Dead || d.Weapon == null) continue;
                d.Cooldown -= dt;
                if (d.Cooldown > 0f) continue;
                Soldier best = null;
                float bd = d.Weapon.Range * d.Weapon.Range;
                foreach (var s in _all)
                {
                    if (s.Dead || s.SideIndex == d.Side) continue;
                    float dist = (s.Pos - d.Pos).sqrMagnitude;
                    if (dist < bd) { bd = dist; best = s; }
                }
                if (best == null) { d.Cooldown = 0.5f; continue; }
                Combat.TurretShoot(d, best);
                d.Cooldown = d.Weapon.Cooldown * Random.Range(0.85f, 1.15f);
                if (d.Weapon.Burst > 1 && Random.value < 1f / d.Weapon.Burst) d.Cooldown += d.Weapon.Reload;
            }
        }

        /// <summary>Генераторы защитника питают щит над штабом.</summary>
        public bool ShieldUp
        {
            get
            {
                if (Map.ShieldDome == null || Map.ShieldDome.Dead) return false;
                foreach (var p in Map.Points) if (p.Kind == PointKind.Generator && p.Owner == 1) return true;
                return false;
            }
        }

        public void DamageStructure(Destructible d, float dmg, Vector3 at, Squad source)
        {
            if (d == null || d.Dead || dmg <= 0f) return;
            if (d.Type == Destructible.Kind.Headquarters && ShieldUp) { Fx.Sparks(at, 1f); return; }
            if (d.Type == Destructible.Kind.Rock && d.MaxHp >= 4000f) return;   // скалы не рушатся
            d.Hp -= dmg;
            if (d.Hp <= 0f) Collapse(d);
        }

        public void DamageStructuresInRadius(Vector3 pos, float r, float dmg, Squad source)
        {
            foreach (var d in Map.Destructibles)
            {
                if (d.Dead) continue;
                float dist = Vector3.Distance(new Vector3(d.Pos.x, pos.y, d.Pos.z), pos) - d.Radius;
                if (dist > r) continue;
                DamageStructure(d, dmg * Mathf.Clamp01(1f - dist / Mathf.Max(0.5f, r) * 0.5f), pos, source);
            }
        }

        /// <summary>Сооружение рушится: груда обломков (лёгкое укрытие), огонь и дым; район колонии считается разрушенным.</summary>
        private void Collapse(Destructible d)
        {
            d.Dead = true;
            d.Hp = 0f;
            Map.Cover.RemoveOwned(d);
            foreach (var c in d.Colliders) if (c != null) c.enabled = false;
            Fx.Collapse(d);
            Sfx.Explosion(d.Pos, Mathf.Max(3f, d.Radius));
            BattleCamera.Shake(d.Pos, Mathf.Clamp(d.Radius * 0.15f, 0.3f, 1.5f));
            if (d.District.HasValue)
            {
                _destroyedDistricts.Add(d.District.Value);
                _civilians += Rng.Next(0, 2);
            }
            // Обломки — новое лёгкое укрытие
            if (d.Type != Destructible.Kind.Tree && d.Type != Destructible.Kind.Sandbags)
                for (int i = 0; i < 6; i++)
                {
                    float a = i / 6f * Mathf.PI * 2f;
                    var p = Map.Snap(d.Pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (d.Radius * 0.7f));
                    Map.Cover.Add(p, CoverKind.Light, Vector3.zero);
                }
            if (d.Type == Destructible.Kind.Headquarters) Hud?.Flash(Loc.T("ШТАБ КОЛОНИИ РАЗРУШЕН"), Sides[0].Color);
        }

        // ---- Мины, лава, погода ----

        private void TickMines()
        {
            for (int i = Map.Mines.Count - 1; i >= 0; i--)
            {
                var w = Map.ToWorld(Map.Mines[i]);
                foreach (var s in _all)
                {
                    if (s.Dead || s.SideIndex != 0) continue;
                    Vector3 d = s.Pos - w; d.y = 0f;
                    if (d.sqrMagnitude > 2.2f) continue;
                    Map.Mines.RemoveAt(i);
                    Combat.Explode(w, MineWeapon, null, 1f, 1);
                    break;
                }
            }
        }

        private static readonly WeaponDef MineWeapon = new WeaponDef { Kind = WeaponKind.Explosive, Damage = 90f, Splash = 3f, Suppression = 30f, Pierce = 0.7f, StructureMult = 0.2f };
        private float _hazard;

        private void TickHazards(float dt)
        {
            if (!Map.HasRiver || (_hazard -= dt) > 0f) return;
            _hazard = 0.5f;
            bool lava = Map.Biome.LavaRiver;
            foreach (var s in _all.ToArray())
            {
                if (s.Dead || !Map.InRiver(s.Pos, lava ? 2.5f : 0f)) continue;
                if (lava)
                {
                    s.Hp -= (s.IsVehicle ? 15f : 10f);
                    if (!s.IsVehicle && s.BurnLeft <= 0f && Random.value < 0.15f) s.BurnLeft = 2f;
                    if (s.Hp <= 0f) Combat.Kill(s, Combat.LavaBurn, Vector3.up, null);
                }
            }
        }

        private void TickWeather(float dt)
        {
            WeatherRangeMult = 1f;
            WeatherAccuracyMult = 1f;
            if (!Map.Biome.Sandstorms) return;
            if (_stormLeft > 0f)
            {
                _stormLeft -= dt;
                Sandstorm = true;
                WeatherAccuracyMult = 0.7f;
                RenderSettings.fogStartDistance = 8f;
                RenderSettings.fogEndDistance = 38f;
                if (_stormLeft <= 0f) { Sandstorm = false; RenderSettings.fogStartDistance = Map.Biome.FogStart; RenderSettings.fogEndDistance = Map.Biome.FogEnd; Hud?.Flash(Loc.T("Буря стихает"), Color.white); }
                return;
            }
            _stormTimer -= dt;
            if (_stormTimer <= 0f)
            {
                _stormTimer = Random.Range(120f, 180f);
                if (Elapsed > 60f) { _stormLeft = 30f; Hud?.Flash(Loc.T("Песчаная буря! Обзор падает до 25 м"), new Color(1f, 0.8f, 0.5f)); }
            }
        }

        // ---- Ополчение защитника и генерал ----

        private void TickMilitia(float dt)
        {
            var d = Sides[1];
            if ((d.MilitiaTimer -= dt) > 0f) return;
            d.MilitiaTimer = 120f;
            foreach (var p in Map.Points)
            {
                if (p.Kind != PointKind.Urban || p.Owner != 1) continue;
                if (d.SlotsUsed + 6 > d.SlotLimit || d.AliveSquads >= 10) break;
                var sq = SpawnSquad(d, UnitCatalog.Get("militia"), p.Pos + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f)));
                if (sq != null) { sq.Shout(GroundCallouts.Home(sq)); sq.TakeCover(p.Pos); }
            }
            // Ополчение Астреи у атакующего: бесплатный отряд на своей точке
        }

        private void RespawnHero(BattleSide s)
        {
            Fx.DropPod(Map.Snap(s.Beacon + new Vector3(0f, 0f, 4f)), s.Color, () =>
            {
                if (!_running) return;
                CrushAt(Map.Snap(s.Beacon + new Vector3(0f, 0f, 4f)), 3f, s.Index);
                SpawnHero(s, s.Beacon + new Vector3(0f, 0f, 4f));
            });
        }

        // ---- Смерть ----

        public void OnSoldierDied(Soldier v, WeaponDef w, Vector3 dir)
        {
            if (v.Def.Role == UnitRole.Hero && v.Squad.Side.HeroRespawn < 0f) v.Squad.Side.HeroRespawn = 60f;
            if (v.IsVehicle)
            {
                Fx.VehicleWreck(v);
                Map.Cover.Add(v.Pos + v.transform.right * 2.2f, CoverKind.Heavy, -v.transform.right);
                Map.Cover.Add(v.Pos - v.transform.right * 2.2f, CoverKind.Heavy, v.transform.right);
                Fx.Delayed(0.05f, () => Combat.Explode(v.Pos, VehicleBlast, null, 1f, -1));
                if (v.Agent != null) v.Agent.enabled = false;
                FeedSwarms(v.Pos);
                Destroy(v);   // корпус остаётся остовом, логика — нет
                return;
            }
            GoreFx.Death(v, w, dir);
        }

        /// <summary>Рой «Ноля» собирает из каждого нового остова по 2 дрона (ближайший рой стороны «Ноль», до 30 дронов).</summary>
        private void FeedSwarms(Vector3 wreck)
        {
            foreach (var side in Sides)
            {
                if (side.FactionKey != "nol") continue;
                Squad best = null;
                float bd = float.MaxValue;
                foreach (var sq in side.Squads)
                {
                    if (sq.Destroyed || sq.Def.Role != UnitRole.Swarm || sq.Members.Count >= 30) continue;
                    float d = (sq.Center - wreck).sqrMagnitude;
                    if (d < bd) { bd = d; best = sq; }
                }
                if (best == null) continue;
                for (int i = 0; i < 2; i++)
                    SpawnMember(best, false, Map.Snap(wreck + new Vector3(Random.Range(-2.5f, 2.5f), 0f, Random.Range(-2.5f, 2.5f))));
                GoreFx.Sparks(wreck + Vector3.up, 12);
                best.Shout(Loc.T("ПЕРЕРАБОТКА ОБЛОМКОВ."));
            }
        }

        private static readonly WeaponDef VehicleBlast = new WeaponDef { Kind = WeaponKind.Explosive, Damage = 60f, Splash = 4f, Suppression = 25f, Pierce = 0.4f, StructureMult = 0.3f };

        public void AddCrater(Vector3 pos, float r)
        {
            GoreFx.Crater(pos, r);
            if (r >= 1.6f) Map.Cover.Add(Map.Snap(pos + new Vector3(r * 0.7f, 0f, 0f)), CoverKind.Light, Vector3.zero);
        }

        // ================================================================ Способности сторон

        /// <summary>Орбитальный удар флота: прицел 3 с, столб света, ударная волна.</summary>
        public bool OrbitalStrike(BattleSide side, Vector3 at)
        {
            if (side.OrbitalCooldown > 0f || side.Energy < OrbitalEnergy) return false;
            side.Energy -= OrbitalEnergy;
            side.OrbitalCooldown = OrbitalCooldown;
            at = Map.Snap(at);
            Fx.OrbitalStrike(at, side.Color, () =>
            {
                if (!_running) return;
                var w = new WeaponDef { Kind = WeaponKind.Explosive, Damage = 260f * Mathf.Clamp(1f + side.Setup.FleetPower / 20000f, 1f, 2f), Splash = 9f, Suppression = 60f, Pierce = 0.8f, StructureMult = 2.5f, Indirect = true };
                Combat.Explode(at, w, null, 1f, side.Index);
                BattleCamera.Shake(at, 2.2f);
                AddCrater(at, 6f);
            });
            Sfx.Play(SfxId.WeaponEnergy, 0.8f, 0.6f);
            return true;
        }

        /// <summary>Особая способность фракции (кнопка в HUD, ИИ — сам). Сейчас: ополчение Астреи на своей точке.</summary>
        public bool UseFactionSpecial(BattleSide side)
        {
            if (side == null || side.SpecialCooldown > 0f || !_running || _ending) return false;
            switch (side.FactionKey)
            {
                case "astrea":
                {
                    // Своя точка ближе к камере (игрок) или к врагу (ИИ), без врагов в круге
                    Vector3 near = side.IsPlayer && Cam != null ? Cam.Focus : Sides[1 - side.Index].Beacon;
                    StrategicPoint best = null;
                    float bd = float.MaxValue;
                    foreach (var p in Map.Points)
                    {
                        if (p.Owner != side.Index) continue;
                        float d = (p.Pos - near).sqrMagnitude;
                        if (d < bd) { bd = d; best = p; }
                    }
                    Vector3 at = best != null ? best.Pos : side.Beacon;
                    var sq = SpawnSquad(side, UnitCatalog.Get("militia"), Map.Snap(at + new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f))));
                    if (sq == null) return false;
                    sq.Shout(GroundCallouts.Home(sq));
                    sq.TakeCover(at);
                    side.SpecialCooldown = 90f;
                    if (side.IsPlayer) Hud?.Flash(Loc.T("Колонисты взялись за оружие"), side.Color);
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>Роща Тэрра'ан: за пару секунд вырастают деревья — лёгкие укрытия (навмеш не меняется, стволы проходимы).</summary>
        public void GrowGrove(Vector3 at)
        {
            if (Map == null) return;
            var trunk = GroundMats.Lit(new Color(0.28f, 0.2f, 0.13f), 0f, 0.2f);
            var leaves = GroundMats.Lit(new Color(0.2f, 0.5f, 0.2f), 0f, 0.2f, new Color(0.05f, 0.25f, 0.05f));
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f + Random.value * 0.6f;
                var w = Map.Snap(at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(1.5f, 3.5f));
                if (!Map.Inside(w, 4f)) continue;
                float s = Random.Range(0.7f, 1.1f);
                var go = new GameObject("Grove");
                go.transform.SetParent(_root, true);
                go.transform.position = w;
                GroundMats.Part(go.transform, PrimitiveType.Cylinder, new Vector3(0f, 2f * s, 0f), new Vector3(0.3f * s, 2f * s, 0.3f * s), trunk, "Trunk");
                GroundMats.Part(go.transform, PrimitiveType.Sphere, new Vector3(0f, 4.6f * s, 0f), new Vector3(3.2f, 3.8f, 3.2f) * s, leaves, "Crown");
                go.transform.localScale = Vector3.one * 0.05f;
                var d = new Destructible { Type = Destructible.Kind.Tree, Go = go, Pos = w, Radius = 1.2f, Hp = 220f, MaxHp = 220f };
                foreach (var r in go.GetComponentsInChildren<Renderer>()) d.Renderers.Add(r);
                Map.Destructibles.Add(d);
                Map.Cover.Add(Map.Snap(w + new Vector3(1f, 0f, 0.2f)), CoverKind.Light, Vector3.zero, d);
                StartCoroutine(Grow(go.transform, 1.2f + i * 0.15f));
            }
            GoreFx.Dust(at, 2.5f);
        }

        private System.Collections.IEnumerator Grow(Transform t, float seconds)
        {
            float k = 0f;
            while (k < 1f && t != null)
            {
                k += Time.deltaTime / seconds;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 3f);
                t.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, e);
                yield return null;
            }
        }

        // ================================================================ Пауза и скорость

        public void TogglePause()
        {
            Paused = !Paused;
            ApplyTimeScale();
        }

        public void ChangeSpeed(int dir)
        {
            float[] steps = { 0.5f, 1f, 1.5f, 2f };
            int i = Array.IndexOf(steps, SpeedMult);
            if (i < 0) i = 1;
            i = Mathf.Clamp(i + dir, 0, steps.Length - 1);
            SpeedMult = steps[i];
            ApplyTimeScale();
        }

        public void ApplyTimeScale()
        {
            if (SyncKills.SlowMotion) return;
            Time.timeScale = Paused ? 0f : SpeedMult;
        }

        // ================================================================ Конец

        public void Surrender() => End(false, Loc.T("Десант отозван"), true);
        public void Capitulate() => End(true, Loc.T("Гарнизон сложил оружие"));

        private void End(bool attackerWon, string reason, bool retreated = false)
        {
            if (_ending) return;
            _ending = true;
            Time.timeScale = 1f;
            var r = new GroundBattleResult
            {
                AttackerWon = attackerWon,
                AttackerLostMen = Sides[0].LostMen, AttackerLostVehicles = Sides[0].LostVehicles,
                DefenderLostMen = Sides[1].LostMen, DefenderLostVehicles = Sides[1].LostVehicles,
                Duration = Elapsed, Retreated = retreated, CiviliansKilled = Mathf.Min(_civilians + (Setup.Population > 0 ? Rng.Next(0, 2) : 0), Mathf.Max(0, Setup.Population - 1))
            };
            r.DestroyedDistricts.AddRange(_destroyedDistricts);
            foreach (var sq in Sides[0].Squads) if (!sq.Destroyed && sq.Veterancy > 0) r.VeteranSquads++;
            Sfx.Play(attackerWon == (Player?.Index == 0) ? SfxId.BattleVictory : SfxId.BattleDefeat, 1f);
            ResultScreen.Show(this, r, reason, () => Finish(r));
        }

        private void Finish(GroundBattleResult r)
        {
            _running = false;
            Cleanup();
            RestoreStrategy();
            var cb = _onDone;
            _onDone = null;
            cb?.Invoke(r);
        }

        private void Cleanup()
        {
            Hud?.Dispose();
            Hud = null;
            if (Controls != null) Destroy(Controls);
            Controls = null;
            GoreFx.End();
            SyncKills.Reset();
            foreach (var s in Sides) s?.Squads.Clear();
            _all.Clear();
            if (Map != null) foreach (var o in Map.Owned) if (o != null) Destroy(o);
            // Текстуры земли создаются на бой — выгрузить вместе с материалом
            var tr = Map?.Terrain != null ? Map.Terrain.GetComponent<Renderer>() : null;
            if (tr != null && tr.sharedMaterial != null)
            {
                var m = tr.sharedMaterial;
                foreach (var prop in new[] { "_BaseMap", "_BumpMap", "_DetailAlbedoMap", "_DetailNormalMap", "_EmissionMap" })
                    if (m.HasProperty(prop) && m.GetTexture(prop) != null) Destroy(m.GetTexture(prop));
                Destroy(m);
            }
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            Map = null;
            GroundMats.Release();
            FxTextures.Release();
            Time.timeScale = 1f;
        }

        // ================================================================ Стратегия: спрятать и вернуть

        private void HideStrategy()
        {
            var tm = TimeManager.Instance;
            _speedBefore = tm != null ? tm.CurrentSpeed : 1;
            tm?.SetSpeed(0);
            _galaxyCam = Camera.main;
            if (_galaxyCam != null)
            {
                _galaxyListener = _galaxyCam.GetComponent<AudioListener>();
                _galaxyController = _galaxyCam.GetComponent<StellarisClone.Cam.StrategyCameraController>();
                // Камера галактики остаётся включённой (на неё опираются скрипты карты), но ничего не рисует и не ловит клики
                _galaxyMask = _galaxyCam.cullingMask;
                _galaxyEventMask = _galaxyCam.eventMask;
                _galaxyCam.cullingMask = 0;
                _galaxyCam.eventMask = 0;
                if (_galaxyListener != null) _galaxyListener.enabled = false;
                if (_galaxyController != null) _galaxyController.enabled = false;
            }
            UIManager.Instance?.SetOverlayMode(true);
            MapModeController.HideGlobal();
            GalaxyMinimap.HideGlobal();
            _ambientBefore = RenderSettings.ambientLight;
            _ambientModeBefore = RenderSettings.ambientMode;
            _fogBefore = RenderSettings.fog;
            _fogColorBefore = RenderSettings.fogColor;
            _fogModeBefore = RenderSettings.fogMode;
            _fogStartBefore = RenderSettings.fogStartDistance;
            _fogEndBefore = RenderSettings.fogEndDistance;
            _skyBefore = RenderSettings.ambientSkyColor;
            _sunBefore = RenderSettings.sun;
            _equatorBefore = RenderSettings.ambientEquatorColor;
            _groundBefore = RenderSettings.ambientGroundColor;
        }

        private void SetupEnvironment()
        {
            var b = Map.Biome;
            // Окружение с трёх сторон: небо сверху, горизонт, отражённый от земли свет снизу — объём вместо плоской заливки
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = b.AmbientSky;
            RenderSettings.ambientEquatorColor = b.AmbientEquator;
            RenderSettings.ambientGroundColor = b.AmbientGround;
            RenderSettings.ambientLight = b.AmbientSky;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = b.Fog;
            RenderSettings.fogStartDistance = b.FogStart;
            RenderSettings.fogEndDistance = b.FogEnd;

            // Солнце: низкое, длинные мягкие тени
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(_root, false);
            sun.type = LightType.Directional;
            sun.color = b.Sun;
            sun.intensity = b.SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.92f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.35f;
            sun.transform.rotation = Quaternion.Euler(b.SunEuler);
            RenderSettings.sun = sun;
            // Контровой свет с противоположной стороны: силуэты отрядов отделяются от земли
            var rim = new GameObject("Rim").AddComponent<Light>();
            rim.transform.SetParent(_root, false);
            rim.type = LightType.Directional;
            rim.color = b.Rim;
            rim.intensity = b.RimIntensity;
            rim.shadows = LightShadows.None;
            rim.transform.rotation = Quaternion.Euler(22f, b.SunEuler.y + 165f, 0f);

            // Тени на всю глубину кадра RTS-камеры (в галактике хватает 50 м, здесь камера висит выше)
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (asset != null)
            {
                _shadowDistBefore = asset.shadowDistance;
                _cascadesBefore = asset.shadowCascadeCount;
                asset.shadowDistance = 170f;
                if (asset.shadowCascadeCount < 4 && GraphicsQuality.Shadows >= 2) asset.shadowCascadeCount = 4;
            }
            BattleAtmosphere.Create(_root, Map);
            _stormTimer = Random.Range(90f, 140f);
        }

        private void RestoreStrategy()
        {
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (asset != null && _shadowDistBefore >= 0f)
            {
                asset.shadowDistance = _shadowDistBefore;
                asset.shadowCascadeCount = _cascadesBefore;
                _shadowDistBefore = -1f;
            }
            RenderSettings.ambientSkyColor = _skyBefore;
            RenderSettings.sun = _sunBefore;
            RenderSettings.ambientEquatorColor = _equatorBefore;
            RenderSettings.ambientGroundColor = _groundBefore;
            RenderSettings.ambientMode = _ambientModeBefore;
            RenderSettings.ambientLight = _ambientBefore;
            RenderSettings.fog = _fogBefore;
            RenderSettings.fogColor = _fogColorBefore;
            RenderSettings.fogMode = _fogModeBefore;
            RenderSettings.fogStartDistance = _fogStartBefore;
            RenderSettings.fogEndDistance = _fogEndBefore;
            if (_galaxyCam != null) { _galaxyCam.cullingMask = _galaxyMask; _galaxyCam.eventMask = _galaxyEventMask; }
            if (_galaxyListener != null) _galaxyListener.enabled = true;
            if (_galaxyController != null) _galaxyController.enabled = true;
            UIManager.Instance?.SetOverlayMode(false);
            MapModeController.ShowGlobal();
            GalaxyMinimap.ShowGlobal();
            TimeManager.Instance?.SetSpeed(_speedBefore);
        }
    }
}
