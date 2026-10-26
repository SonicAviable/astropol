using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Связь наземных боёв со стратегией (раздел 4.3 ТЗ):
    ///   • когда осада колонизированной системы идёт без помех, игроку-атакующему доступна «ВЫСАДКА»
    ///     (одно уведомление; кнопка в окне системы → «Командовать лично / Автобой / Ждать осады»);
    ///   • ИИ-атакующий бросает десант сам: 50% в месяц или сразу при прогрессе осады ≥ 50% — всегда автобоем;
    ///     если цель — колония игрока, игрок выбирает «Защищаться лично / Автобой»;
    ///   • итог применяется к галактике: захват, разрушенные районы, погибшие жители, недовольство,
    ///     урон флоту при поражении, опыт генералу, ветераны флоту, новость в ленту.
    /// Системы без колоний захватываются по-прежнему только осадой.
    /// </summary>
    public static class GroundBattleLink
    {
        /// <summary>Захват сейчас идёт штурмом (для текста новости).</summary>
        public static bool Storming { get; private set; }

        private static readonly HashSet<int> s_notified = new HashSet<int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_notified.Clear(); Storming = false; }

        // ================================================================ Условия

        /// <summary>Колония системы, за которую идёт бой (самая населённая).</summary>
        public static PlanetData MainColony(StarSystem sys, out int index)
        {
            index = -1;
            if (sys == null) return null;
            PlanetData best = null;
            for (int i = 0; i < sys.Planets.Count; i++)
            {
                var p = sys.Planets[i];
                if (p.Population > 0 && (best == null || p.Population > best.Population)) { best = p; index = i; }
            }
            return best;
        }

        public static bool HasColony(StarSystem sys) => MainColony(sys, out _) != null;

        /// <summary>Осада идёт без помех: осаждающие на месте, база подавлена, защитника нет.</summary>
        public static bool SiegeOpen(SiegeManager.Siege s) => s != null && s.Active && !s.BaseHolding && !s.Contested;

        /// <summary>Может ли игрок высадиться в системе прямо сейчас (why — почему нет).</summary>
        public static bool CanPlayerAssault(StarSystem sys, out string why)
        {
            why = null;
            var s = sys != null ? SiegeManager.Instance?.GetSiege(sys.Id) : null;
            if (s == null || s.Attacker != 0) { why = Loc.T("Нет вашей осады"); return false; }
            if (!HasColony(sys)) { why = Loc.T("В системе нет колоний — её берут осадой"); return false; }
            if (s.BaseHolding) { why = Loc.T("Сначала подавите звёздную базу"); return false; }
            if (s.Contested) { why = Loc.T("На орбите флот защитника"); return false; }
            if (!s.Active) { why = Loc.T("Флот осады ушёл с орбиты"); return false; }
            if (GroundBattleManager.InBattle) { why = Loc.T("Уже идёт бой"); return false; }
            return true;
        }

        // ================================================================ Сборка сторон

        public static GroundBattleSetup Build(StarSystem sys, int attacker)
        {
            var planet = MainColony(sys, out int pIndex);
            int defender = sys.OwnerId;
            var setup = new GroundBattleSetup
            {
                SystemId = sys.Id,
                PlanetIndex = pIndex,
                PlanetName = planet != null ? planet.Name : sys.Name,
                Planet = planet != null ? planet.Type : PlanetType.Barren,
                Fortification = Mathf.Clamp(sys.Fortification, 0, Fortifications.MaxLevel),
                Population = planet != null ? planet.Population : 0,
                Stability = planet != null ? planet.Stability : 50f,
                Seed = Random.Range(1, int.MaxValue)
            };
            if (planet != null)
            {
                setup.Urban = planet.GetDistrictCount(DistrictType.Urban);
                setup.Mining = planet.GetDistrictCount(DistrictType.Mining);
                setup.Generator = planet.GetDistrictCount(DistrictType.Generator);
                setup.Industrial = planet.GetDistrictCount(DistrictType.Industrial);
            }
            setup.GarrisonSquads = Mathf.Clamp(2 + setup.Population / 6 + setup.Fortification, 2, 7);

            // ---- Атакующий: флот на орбите, адмирал, ветераны ----
            var a = setup.Attacker;
            FillSide(a, attacker);
            float power = 0f;
            int vets = 0;
            Leader admiral = null;
            var lm = LeaderManager.Instance;
            foreach (var f in AttackerFleets(sys, attacker))
            {
                power += CombatMath.Power(f);
                vets = Mathf.Max(vets, f.GroundVeterans);
                var l = lm?.LeaderOfShip(f.Id);
                if (l != null && (admiral == null || l.Level > admiral.Level)) admiral = l;
            }
            a.FleetPower = power;
            a.Slots = Mathf.Clamp(40 + Mathf.FloorToInt(power / 1000f), 40, 80);
            a.StartVeterancy = Mathf.Clamp(vets, 0, 3);
            if (admiral != null)
            {
                a.GeneralLeaderId = admiral.Id;
                a.GeneralName = admiral.Name;
                a.GeneralLevel = admiral.Level;
                if (admiral.Has("adm_aggressive")) a.DamageMult *= 1.10f;
                if (admiral.Has("adm_veteran")) { a.DamageMult *= 1.06f; a.MoraleMult *= 1.1f; }
                if (admiral.Has("adm_reckless")) { a.DamageMult *= 1.08f; a.MoraleMult *= 0.9f; }
                if (admiral.Has("adm_tactician")) a.DamageMult *= 1.05f;
                if (admiral.Has("adm_cautious")) a.MoraleMult *= 1.15f;
                if (admiral.Has("adm_navigator")) a.SpeedMult *= 1.08f;
                a.MoraleMult *= 1f + 0.03f * admiral.Level;
            }

            // ---- Защитник: губернатор, вид колонии ----
            var d = setup.Defender;
            FillSide(d, defender);
            d.Slots = 40 + setup.Fortification * 5;
            var gov = planet != null ? lm?.GovernorOf(planet) : null;
            if (gov != null)
            {
                d.GeneralLeaderId = gov.Id;
                d.GeneralName = gov.Name;
                d.GeneralLevel = gov.Level;
                d.MoraleMult *= 1f + 0.04f * gov.Level;
                if (gov.Has("gov_honest")) d.MoraleMult *= 1.1f;
                if (gov.Has("gov_corrupt")) d.MoraleMult *= 0.85f;
            }
            var species = planet != null ? SpeciesRegistry.Get(planet.DominantOrganic) : null;
            if (species != null)
            {
                if (species.Has(SpeciesTrait.Resilient)) { d.HpMult *= 1.15f; d.MoraleMult *= 1.15f; }
                if (species.Has(SpeciesTrait.Slow)) d.SpeedMult *= 0.9f;
                if (species.Has(SpeciesTrait.Agreeable)) d.MoraleMult *= 1.05f;
                if (species.Has(SpeciesTrait.Quarrelsome)) d.DamageMult *= 1.05f;
                if (species.Has(SpeciesTrait.Adaptive)) d.SpeedMult *= 1.05f;
                if (species.Machine) { d.HpMult *= 1.1f; d.MoraleMult *= 1.2f; }
                if (species.Faction == "iridia" || species.Faction == "terraan" || species.Faction == "nol") d.Blood = FactionBlood.Faction(species.Faction);
                else if (species.Machine) d.Blood = BloodKind.Coolant;
            }
            // Нестабильная колония дерётся хуже
            if (setup.Stability < 40f) d.MoraleMult *= 0.85f;
            return setup;
        }

        private static void FillSide(GroundSideSetup s, int owner)
        {
            s.Owner = owner;
            s.IsPlayer = owner == 0;
            s.FactionKey = WonderManager.KeyOf(owner);
            s.Color = owner == 0 ? FleetIndicator.OwnColor : AIEmpireManager.MapColorFor(owner);
            s.Blood = FactionBlood.Faction(s.FactionKey);
            var b = EmpireBonuses.For(owner);
            if (b != null)
            {
                s.DamageMult = b.AllDamage * (b.KineticDamage + b.EnergyDamage) * 0.5f;
                s.HpMult = Mathf.Sqrt(Mathf.Max(0.1f, b.ArmorMult * b.HullMult));
            }
        }

        private static IEnumerable<FleetData> AttackerFleets(StarSystem sys, int attacker)
        {
            var fm = FleetManager.Instance;
            if (fm == null) yield break;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.OwnerId != attacker || d.Type != FleetType.Military) continue;
                if (d.State == FleetState.InHyperlane || d.CurrentSystemId != sys.Id) continue;
                yield return d;
            }
        }

        // ================================================================ Игрок атакует

        /// <summary>Кнопка «ВЫСАДКА»: диалог выбора.</summary>
        public static void OpenAssaultDialog(StarSystem sys)
        {
            if (!CanPlayerAssault(sys, out _)) return;
            var setup = Build(sys, 0);
            int defender = sys.OwnerId;
            LaunchDialog.ShowAttack(setup, GroundAutoResolve.AttackerChance(setup),
                personal: () => StartPersonal(sys, setup, defender),
                auto: () => RunAuto(sys, setup, defender));
        }

        /// <summary>Личный бой: автосохранение, потом поле боя. Итог применяется, когда галактика вернётся.</summary>
        public static void StartPersonal(StarSystem sys, GroundBattleSetup setup, int defender)
        {
            if (GroundBattleManager.InBattle) return;
            SaveSystem.Save(null, auto: true, notify: false);
            GroundBattleManager.Ensure().Begin(setup, r => Apply(sys, setup, r, defender));
        }

        /// <summary>Автобой: мгновенный итог и окно с потерями.</summary>
        public static void RunAuto(StarSystem sys, GroundBattleSetup setup, int defender)
        {
            var r = GroundAutoResolve.Resolve(setup, new System.Random(setup.Seed));
            Apply(sys, setup, r, defender);
            bool playerSide = setup.Attacker.IsPlayer || setup.Defender.IsPlayer;
            if (playerSide)
                ResultScreen.Show(setup, r, r.AttackerWon ? Loc.T("Десант сломил оборону колонии") : Loc.T("Гарнизон отбросил десант"), null);
        }

        // ================================================================ Ежедневная проверка осад

        /// <summary>
        /// Вызывается из SiegeManager для каждой осады колонизированной системы, идущей без помех.
        /// Возвращает true, если система уже обработана штурмом (её не нужно захватывать по таймеру в этот день).
        /// </summary>
        public static bool OnSiegeDay(StarSystem sys, SiegeManager.Siege siege, float requiredDays)
        {
            if (sys == null || siege == null || !HasColony(sys) || !SiegeOpen(siege)) return false;

            if (siege.Attacker == 0)
            {
                // Игроку — одно уведомление за осаду
                if (s_notified.Add(sys.Id))
                    NotificationCenter.Show(Loc.T("Высадка возможна"),
                        Loc.F("{0}: оборона сломлена, колония открыта для штурма. Кнопка «ВЫСАДКА» — в окне системы", sys.Name),
                        NotificationCenter.Kind.Info, 7f);
                return false;
            }

            // ИИ-атакующий: 50% в месяц или сразу при прогрессе ≥ 50%
            float daily = 1f - Mathf.Pow(0.5f, 1f / 30f);
            bool go = siege.Progress >= requiredDays * 0.5f || Random.value < daily;
            if (!go) return false;
            if (GroundBattleManager.InBattle || LaunchDialog.IsOpen || ResultScreen.IsOpen) return false;

            var setup = Build(sys, siege.Attacker);
            int defender = sys.OwnerId;
            if (defender == 0)
            {
                // Враг высаживается на нашу колонию: защищаться лично или автобой
                LaunchDialog.ShowDefense(setup, 1f - GroundAutoResolve.AttackerChance(setup),
                    personal: () => StartPersonal(sys, setup, defender),
                    auto: () => RunAuto(sys, setup, defender));
                return true;
            }
            // ИИ против ИИ — всегда автобой, без окон
            RunAuto(sys, setup, defender);
            return true;
        }

        public static void ForgetNotice(int systemId) => s_notified.Remove(systemId);

        // ================================================================ Итоги → галактика

        public static void Apply(StarSystem sys, GroundBattleSetup setup, GroundBattleResult r, int defender)
        {
            if (sys == null || setup == null || r == null || setup.Sandbox) return;
            int attacker = setup.Attacker.Owner;
            // Пока шёл бой, система могла смениться владельцем (мир, отделение) — тогда итог только на бумаге
            bool valid = sys.OwnerId == defender;
            var planet = setup.PlanetIndex >= 0 && setup.PlanetIndex < sys.Planets.Count ? sys.Planets[setup.PlanetIndex] : null;

            // ---- Колония: районы и жители ----
            if (valid && planet != null)
            {
                var lost = new int[4];
                foreach (var t in r.DestroyedDistricts) lost[(int)t]++;
                for (int t = 0; t < 4; t++)
                {
                    int have = planet.GetDistrictCount((DistrictType)t);
                    int remove = Mathf.Min(lost[t], have / 2);   // не больше половины каждого типа
                    for (int i = planet.Districts.Count - 1; i >= 0 && remove > 0; i--)
                        if (planet.Districts[i].Type == (DistrictType)t) { planet.Districts.RemoveAt(i); remove--; }
                }
                planet.Population = Mathf.Max(1, planet.Population - Mathf.Max(0, r.CiviliansKilled));
                planet.SyncPops();
            }

            var lm = LeaderManager.Instance;
            var fleets = new List<FleetData>(AttackerFleets(sys, attacker));
            if (r.AttackerWon)
            {
                if (valid)
                {
                    Storming = true;
                    try { SiegeManager.Instance?.Capture(sys, attacker); }
                    finally { Storming = false; }
                    // Штурм злит жителей сильнее осады
                    if (planet != null) planet.Unrest += 15f;
                }
                foreach (var f in fleets) if (r.VeteranSquads > 0) f.GroundVeterans = Mathf.Min(3, f.GroundVeterans + 1);
                Xp(lm, setup.Attacker.GeneralLeaderId, 40f);
            }
            else
            {
                // Десант потерян: осада сначала, флоту −10% корпуса
                var siege = SiegeManager.Instance?.GetSiege(sys.Id);
                if (siege != null) siege.Progress = 0f;
                foreach (var f in fleets)
                    f.HullPoints = Mathf.Max(1f, f.HullPoints - f.MaxHullPoints * 0.1f);
                Xp(lm, setup.Defender.GeneralLeaderId, 40f);
                if (valid)
                {
                    bool aboutPlayer = attacker == 0 || defender == 0;
                    if (GalacticNews.Known(attacker, defender))
                        GalacticNews.Post(NewsKind.Battle, Loc.F("Штурм системы {0} отбит: {1} теряет десант", GalacticNews.Place(sys), GalacticNews.Who(attacker)), sys.Id, aboutPlayer);
                }
            }
            s_notified.Remove(sys.Id);

            if (attacker == 0 || defender == 0)
            {
                bool good = (attacker == 0) == r.AttackerWon;
                NotificationCenter.Show(r.AttackerWon ? Loc.F("{0}: колония пала", sys.Name) : Loc.F("{0}: штурм отбит", sys.Name),
                    Loc.F("Потери: атакующие {0}, защитники {1}", r.AttackerLostMen + r.AttackerLostVehicles, r.DefenderLostMen + r.DefenderLostVehicles),
                    good ? NotificationCenter.Kind.Success : NotificationCenter.Kind.Danger, 7f);
            }
            EconomyManager.Instance?.RecalculateAll();
            TechnologyManager.Instance?.RecountPopulation();
        }

        private static void Xp(LeaderManager lm, int leaderId, float xp)
        {
            if (lm == null || leaderId < 0) return;
            foreach (var l in lm.All) if (l.Id == leaderId) { lm.GrantXp(l, xp); return; }
        }
    }
}
