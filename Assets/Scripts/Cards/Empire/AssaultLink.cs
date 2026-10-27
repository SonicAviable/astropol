using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Связь карточного штурма со стратегией. Колонию (систему с жителями) осада по таймеру берёт лишь «голодом» — втрое дольше;
    /// быстрый путь — штурм, когда флот подавил звёздную базу и держит орбиту. Колода атакующего — лучшие карты резерва его империи,
    /// сколько поместится на военные корабли у системы; защитника — карты резерва, сколько вместит колония, плюс ополчение
    /// из жителей (1 карта на 5 жителей). Генерал — адмирал
    /// флота, комендант — губернатор. Итог: захват системы, потери карт, ветераны, разрушенные районы, погибшие жители,
    /// опыт лидерам, новости. ИИ против ИИ — мгновенный автобой по тем же правилам.
    /// </summary>
    public static class AssaultLink
    {
        /// <summary>Пауза между штурмами одной системы после неудачи, дней.</summary>
        public const int CooldownDays = 30;
        private static readonly Dictionary<int, int> s_cooldown = new Dictionary<int, int>();

        /// <summary>Экран боя регистрирует себя здесь: (исходные данные, по окончании). Нет экрана — автобой.</summary>
        public static Action<BattleSetup, Action<BattleState>> ScreenOpener;
        public static bool BattleRunning { get; set; }

        public static int Today
        {
            get
            {
                var t = TimeManager.Instance;
                return t == null ? 0 : (t.Year - 2200) * 360 + (t.Month - 1) * 30 + t.Day;
            }
        }

        public static bool IsColony(StarSystem s) => ArmyManager.SystemPopulation(s) > 0;

        public static PlanetData MainColony(StarSystem s)
        {
            PlanetData best = null;
            if (s != null) foreach (var p in s.Planets) if (p.Population > 0 && (best == null || p.Population > best.Population)) best = p;
            return best;
        }

        public static int CooldownLeft(int systemId) => s_cooldown.TryGetValue(systemId, out int until) ? Mathf.Max(0, until - Today) : 0;

        /// <summary>Почему сейчас нельзя штурмовать (null — можно).</summary>
        public static string Blocker(int attacker, StarSystem sys)
        {
            if (sys == null || !IsColony(sys)) return Loc.T("Штурм нужен только колониям");
            if (BattleRunning) return Loc.T("Уже идёт штурм");
            var sm = SiegeManager.Instance;
            var siege = sm != null ? sm.GetSiege(sys.Id) : null;
            if (siege == null || siege.Attacker != attacker || !siege.Active) return Loc.T("Сначала осадите систему военным флотом");
            if (siege.BaseHolding) return Loc.T("Звёздная база ещё в строю — подавите её в бою");
            if (siege.Contested) return Loc.T("На орбите флот защитника — сначала разбейте его");
            int cd = CooldownLeft(sys.Id);
            if (cd > 0) return Loc.F("Десант перегруппируется: ещё {0} дн.", cd);
            return null;
        }

        // ==================== СБОРКА БОЯ ====================

        private static Terrain TerrainOf(PlanetData p)
        {
            if (p == null) return Terrain.Continental;
            switch (p.Type)
            {
                case PlanetType.Desert: return Terrain.Desert;
                case PlanetType.Molten: return Terrain.Molten;
                case PlanetType.Barren: return Terrain.Barren;
                default: return Terrain.Continental;
            }
        }

        public static string NameOf(int owner)
            => owner == 0 ? (FactionTraits.FactionOf(0)?.Name ?? Loc.T("Наша империя")) : AIEmpireManager.NameOf(owner, Loc.T("Империя"));

        /// <summary>Ополчение защитника: 1 карта на 5 жителей (от 1 до 8).</summary>
        public static int MilitiaFor(StarSystem sys) => Mathf.Clamp(ArmyManager.SystemPopulation(sys) / 5, 1, 8);

        /// <summary>Собрать бой. <paramref name="chosen"/> — карты десанта, выбранные игроком (null — лучшие автоматически).</summary>
        public static BattleSetup Build(int attacker, StarSystem sys, IList<long> chosen = null)
        {
            var army = ArmyManager.Instance;
            int defender = sys.OwnerId;
            var col = MainColony(sys);
            var s = new BattleSetup
            {
                SystemId = sys.Id,
                PlanetIndex = col != null ? sys.Planets.IndexOf(col) : -1,
                PlanetName = col != null ? col.Name : sys.Name,
                Terrain = TerrainOf(col),
                Seed = UnityEngine.Random.Range(1, int.MaxValue),
                AiSkill = Mathf.Clamp(AIDifficulty.Tier, 0, 4)
            };
            s.Sectors = new[]
            {
                ArmyManager.Districts(sys, DistrictType.Generator) > 0 ? SectorType.Power : SectorType.City,
                ArmyManager.Districts(sys, DistrictType.Industrial) > 0 ? SectorType.Factory
                    : ArmyManager.Districts(sys, DistrictType.Mining) > 0 ? SectorType.Mine : SectorType.Port,
                SectorType.HQ
            };
            // Без электростанции щит и турели питаются от города — роль электростанции берёт первый сектор
            if (s.Sectors[0] == SectorType.City && ArmyManager.Districts(sys, DistrictType.Generator) == 0) s.Sectors[0] = SectorType.Power;

            // ---- Атакующий: десант из резерва империи (по местам на кораблях) + экипажи до минимума колоды
            var a = s.Attacker;
            a.Owner = attacker; a.Faction = FactionTraits.Key(attacker); a.Name = NameOf(attacker); a.IsPlayer = attacker == 0;
            a.FleetPower = ArmyManager.FleetPowerAt(attacker, sys.Id);
            a.PointsPerTurn = 1;   // господство флота на орбите
            if (army != null) foreach (var c in army.PickDeck(attacker, sys.Id, chosen)) s.AttackerCards.Add(new BCard(c.D, c.Id, c.Stars));
            int crew = Mathf.Min(ArmyManager.Warships(attacker, sys.Id), ArmyManager.MinDeck - s.AttackerCards.Count);
            for (int i = 0; i < crew; i++) s.AttackerCards.Add(new BCard(CardCatalog.Crew));
            s.AttackerHero = HeroFor(attacker, sys, true);
            if (attacker == 0)
            {
                a.Treasury = () => EconomyManager.Instance != null ? EconomyManager.Instance.EnergyCredits : 0f;
                a.SpendTreasury = v => { var e = EconomyManager.Instance; if (e != null) { e.EnergyCredits = Mathf.Max(0, e.EnergyCredits - v); e.RaiseResourcesChanged(); } };
            }
            else
            {
                var ai = AIEmpireManager.For(attacker);
                a.Treasury = () => ai != null ? ai.EnergyCredits : 0f;
                a.SpendTreasury = v => { if (ai != null) ai.EnergyCredits = Mathf.Max(0, ai.EnergyCredits - v); };
            }

            // ---- Защитник: резерв империи (сколько вместит колония), ополчение, щит Штаба
            var d = s.Defender;
            d.Owner = defender; d.Faction = FactionTraits.Key(defender); d.Name = NameOf(defender); d.IsPlayer = defender == 0;
            d.Stability = col != null ? col.Stability : 60f;
            if (army != null) foreach (var c in army.DefenseCards(defender, sys.Id)) s.DefenderCards.Add(new BCard(c.D, c.Id, c.Stars));
            int militia = MilitiaFor(sys);
            for (int i = 0; i < militia; i++) s.DefenderCards.Add(new BCard(CardCatalog.Militia));
            if (sys.Fortification >= 1) s.DefenderCards.Add(new BCard(CardCatalog.ShieldHQ));
            s.DefenderHero = HeroFor(defender, sys, false);
            return s;
        }

        /// <summary>Герой — лидер: адмирал сильнейшего корабля у системы (атака) или губернатор колонии (оборона).</summary>
        private static BCard HeroFor(int owner, StarSystem sys, bool attack)
        {
            var lm = LeaderManager.Instance;
            Leader l = null;
            if (lm != null)
            {
                if (attack)
                {
                    var fm = FleetManager.Instance;
                    float best = -1;
                    if (fm != null)
                        foreach (var f in fm.AllFleets)
                        {
                            var fd = f?.Data;
                            if (fd == null || fd.Destroyed || fd.OwnerId != owner || fd.CurrentSystemId != sys.Id || fd.Type != FleetType.Military) continue;
                            var lead = lm.LeaderOfShip(fd.Id);
                            if (lead != null && lead.Level > best) { best = lead.Level; l = lead; }
                        }
                }
                else
                {
                    var col = MainColony(sys);
                    if (col != null) l = lm.GovernorOf(col);
                }
            }
            var def = attack ? CardCatalog.HeroAttack : CardCatalog.HeroDefense;
            int stars = l != null ? Mathf.Clamp(l.Level / 2, 0, 3) : 0;
            var card = new BCard(def, -1, stars);
            if (l != null) HeroLeader[card] = l;
            return card;
        }

        /// <summary>Лидер, стоящий за картой героя (для имени и опыта).</summary>
        public static readonly Dictionary<BCard, Leader> HeroLeader = new Dictionary<BCard, Leader>();

        // ==================== ЗАПУСК ====================

        /// <summary>Игрок начинает штурм (окно «Сбор десанта» или кнопка «АВТОБОЙ»): экран боя или автобой.</summary>
        public static bool StartPlayerAssault(StarSystem sys, bool auto, IList<long> chosen = null)
        {
            if (Blocker(0, sys) != null) return false;
            var setup = Build(0, sys, chosen);
            Run(setup, auto);
            return true;
        }

        /// <summary>Враг высаживается на колонию игрока: игрок решает, защищаться лично или автобоем.</summary>
        public static void StartDefense(int attacker, StarSystem sys, bool auto)
        {
            var setup = Build(attacker, sys);
            Run(setup, auto);
        }

        private static void Run(BattleSetup setup, bool auto)
        {
            HeroLeaderCleanup();
            if (!auto && ScreenOpener != null)
            {
                BattleRunning = true;
                ScreenOpener(setup, st => { BattleRunning = false; Apply(setup, st); });
                return;
            }
            BattleState st2;
            BattleAI.AutoResolve(setup, out st2);
            st2.Result.Turns = Math.Max(st2.Result.Turns, st2.Turn);
            Apply(setup, st2);
        }

        private static void HeroLeaderCleanup() { if (HeroLeader.Count > 64) HeroLeader.Clear(); }

        // ==================== ИТОГИ ====================

        public static void Apply(BattleSetup setup, BattleState st)
        {
            if (st == null || st.Result == null) return;
            var r = st.Result;
            var sys = EmpireStats.GetSystem(setup.SystemId);
            if (sys == null) return;
            int attacker = setup.Attacker.Owner, defender = setup.Defender.Owner;

            ArmyManager.Instance?.ApplyResult(r, attacker, defender, sys.Id);

            // Разрушенные районы (орбитальный удар) и погибшее ополчение — жители колонии
            var col = setup.PlanetIndex >= 0 && setup.PlanetIndex < sys.Planets.Count ? sys.Planets[setup.PlanetIndex] : MainColony(sys);
            if (col != null)
            {
                foreach (var t in r.Ruined)
                {
                    var kind = t == SectorType.Power ? DistrictType.Generator : t == SectorType.Factory ? DistrictType.Industrial
                             : t == SectorType.Mine ? DistrictType.Mining : DistrictType.Urban;
                    int idx = col.Districts.FindIndex(x => x.Type == kind);
                    if (idx >= 0) col.Districts.RemoveAt(idx);
                }
                if (r.MilitiaKilled > 0) col.Population = Mathf.Max(1, col.Population - r.MilitiaKilled);
            }

            // Экипажи, погибшие как «морская пехота», ослабляют корабли
            int crewLost = 0;
            foreach (var u in st.Sectors) foreach (var x in u.Units) if (x.Dead && x.Side == BattleState.Attacker && x.Def == CardCatalog.Crew) crewLost++;
            if (crewLost > 0) DamageFleet(attacker, sys.Id, 0.05f * crewLost);

            // Опыт лидерам
            var lm = LeaderManager.Instance;
            if (lm != null)
            {
                if (setup.AttackerHero != null && HeroLeader.TryGetValue(setup.AttackerHero, out var la)) lm.GrantXp(la, r.AttackerWon ? 120f : 50f);
                if (setup.DefenderHero != null && HeroLeader.TryGetValue(setup.DefenderHero, out var ld)) lm.GrantXp(ld, r.AttackerWon ? 40f : 120f);
            }

            var sm = SiegeManager.Instance;
            if (r.AttackerWon)
            {
                GalacticNews.Post(NewsKind.Conquest, Loc.F("{0} берёт {1} штурмом", GalacticNews.Who(attacker), sys.Name), sys.Id, attacker == 0 || defender == 0);
                sm?.Capture(sys, attacker);
            }
            else
            {
                s_cooldown[sys.Id] = Today + CooldownDays;
                sm?.ResetSiege(sys.Id);
                GalacticNews.Post(NewsKind.Battle, Loc.F("Штурм {0} отбит: {1} удержали колонию", sys.Name, GalacticNews.Who(defender)), sys.Id, attacker == 0 || defender == 0);
            }
            EconomyManager.Instance?.RecalculateAll();
            TechnologyManager.Instance?.RecountPopulation();
            OnApplied?.Invoke(setup, r);
        }

        public static event Action<BattleSetup, BattleResult> OnApplied;

        private static void DamageFleet(int owner, int systemId, float frac)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.OwnerId != owner || d.CurrentSystemId != systemId || d.Type != FleetType.Military) continue;
                d.HullPoints = Mathf.Max(d.MaxHullPoints * 0.2f, d.HullPoints - d.MaxHullPoints * Mathf.Min(0.5f, frac));
            }
        }

        // ==================== ИИ ====================

        /// <summary>
        /// Ежедневно (из SiegeManager): ИИ, держащий орбиту над колонией, идёт на штурм с шансом — чем больше десант, тем
        /// охотнее. По колонии игрока — спрашиваем игрока, защищаться ли лично.
        /// </summary>
        public static void ThinkAI(StarSystem sys, int attacker)
        {
            if (attacker <= 0 || BattleRunning || sys == null || Blocker(attacker, sys) != null) return;
            var army = ArmyManager.Instance;
            int deck = army != null ? army.AttackDeck(attacker, sys.Id).Count : 0;
            float chance = 0.04f + deck * 0.012f;
            if (UnityEngine.Random.value > chance) return;
            if (sys.OwnerId == 0)
            {
                if (DefensePrompt != null) { DefensePrompt(attacker, sys); return; }
                StartDefense(attacker, sys, true);
                return;
            }
            StartDefense(attacker, sys, true);
        }

        /// <summary>Окно «Враг штурмует колонию» (регистрирует интерфейс): защищаться лично или автобой.</summary>
        public static Action<int, StarSystem> DefensePrompt;

        public static void ResetForNewGame() { s_cooldown.Clear(); HeroLeader.Clear(); BattleRunning = false; }
    }
}
