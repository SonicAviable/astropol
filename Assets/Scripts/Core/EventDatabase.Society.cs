using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>События общества: бунт на планете — уступки, подавление, высылка чужаков или переговоры губернатора.</summary>
    public static partial class EventDatabase
    {
        /// <summary>Запланировать решение по бунту (вызывает PlanetStability, когда колония игрока восстаёт).</summary>
        public static void ScheduleRiot(PlanetData p)
        {
            if (p?.ParentSystem == null) return;
            Schedule("colony_riot", 2, new EventContext { Planet = p, System = p.ParentSystem, Leader = Leaders?.GovernorOf(p) });
        }

        private static int AlienPops(PlanetData p)
        {
            int founder = SpeciesRegistry.FounderOf(0);
            return p.CountOf(s => !s.Machine && !SpeciesRegistry.Related(s.Id, founder));
        }

        /// <summary>Выслать всех жителей чужих видов (на планете остаётся хотя бы один житель); их империи обижаются.</summary>
        private static int ExpelAliens(PlanetData p)
        {
            int founder = SpeciesRegistry.FounderOf(0);
            var offended = new HashSet<string>();
            int expelled = 0;
            foreach (var g in new List<PopGroup>(p.Pops))
            {
                var s = SpeciesRegistry.Get(g.Species);
                if (s == null || s.Machine || SpeciesRegistry.Related(s.Id, founder)) continue;
                int n = Mathf.Min(g.Count, p.Population - 1);
                if (n <= 0) break;
                expelled += p.RemovePops(s.Id, n);
                var root = SpeciesRegistry.Root(s.Id);
                if (root != null) offended.Add(root.Faction);
            }
            foreach (var ai in AIEmpireManager.All)
                if (ai != null && offended.Contains(SpeciesRegistry.FactionKeyOf(ai.OwnerId))) Opinion(ai.OwnerId, -12f);
            return expelled;
        }

        private static void AddSocietyEvents()
        {
            Add(new Template
            {
                Id = "colony_riot", When = Trigger.Chain,
                CanFire = c => c?.Planet != null && c.Planet.IsPlayerOwned && c.Planet.Population > 0 && c.Planet.Tier <= StabilityTier.Strike,
                Build = c =>
                {
                    var p = c.Planet;
                    float k = K();
                    float pay = R(60 * k);
                    int aliens = AlienPops(p);
                    var dom = SpeciesRegistry.Get(p.DominantOrganic);
                    string who = dom != null ? dom.InText : Loc.T("колонисты");
                    return Ev("colony_riot", Loc.F("БУНТ НА {0}", p.Name.ToUpper()),
                        Loc.F("Толпы на улицах {0}: {1} перекрыли космопорт и захватили узел связи. ", p.Name, who) +
                        Loc.F("Стабильность — {0:0} из 100. Если беспорядки продлятся {1} мес., колония объявит независимость.", p.Stability, PlanetStability.SecessionMonths),
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption(Loc.T("ПОЙТИ НА УСТУПКИ"),
                                Loc.F("−{0} гелия-3 и −20 влияния: недовольство после завоевания уходит, стабильность +15 сразу и +{1} на {2} мес.", pay, PlanetStability.PropagandaBonus, PlanetStability.PropagandaDuration), () =>
                            {
                                Pay(energy: pay, influence: 20f);
                                p.Unrest = 0f; p.RiotMonths = 0;
                                p.PropagandaMonths = PlanetStability.PropagandaDuration;
                                p.Stability = Mathf.Min(100f, p.Stability + 15f);
                                Recalc();
                            }, Need(energy: pay, influence: 20f), () => CanPay(energy: pay, influence: 20f)),
                            new EventOption(Loc.T("ПОДАВИТЬ"),
                                Loc.F("Бесплатно: колония теряет 1 жителя, военное положение +{0} стабильности на {1} мес.", PlanetStability.MartialLawBonus, PlanetStability.MartialLawDuration), () =>
                            {
                                p.Population = Mathf.Max(1, p.Population - 1);
                                p.SyncPops();
                                p.MartialLawMonths = PlanetStability.MartialLawDuration;
                                p.RiotMonths = 0;
                                p.Stability = Mathf.Min(100f, p.Stability + 10f);
                                Recalc();
                            }),
                            new EventOption(Loc.T("ВЫСЛАТЬ ЧУЖАКОВ"),
                                Loc.F("Чужих видов на планете: {0}. Их вывезут — штраф за чужие виды исчезнет, но их родные империи обидятся (−12 отношения).", aliens), () =>
                            {
                                ExpelAliens(p);
                                p.RiotMonths = 0;
                                p.Stability = Mathf.Min(100f, p.Stability + 5f);
                                Recalc();
                            }, Loc.T("Нужны жители чужих видов"), () => aliens > 0 && aliens < p.Population),
                            new EventOption(Loc.T("ПЕРЕГОВОРЫ ГУБЕРНАТОРА"),
                                Loc.T("Бесплатно: губернатор выходит к толпе. Стабильность +20, губернатор получает опыт."), () =>
                            {
                                p.RiotMonths = 0;
                                p.Stability = Mathf.Min(100f, p.Stability + 20f);
                                Xp(c, 40f);
                            }, Loc.F("Губернатор-{0} или «{1}»", TraitName("gov_admin").ToLower(), TraitName("gov_honest")),
                               () => LeaderHas(c, "gov_admin") || LeaderHas(c, "gov_honest"))));
                }
            });
        }
    }
}
