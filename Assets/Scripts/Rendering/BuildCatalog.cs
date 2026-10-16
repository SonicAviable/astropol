using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    public enum BuildCategory { District, Project, Ship, Society }

    /// <summary>Что можно заказать на планете: карточка меню строительства.</summary>
    public class BuildOption
    {
        public string Id;
        public BuildCategory Category;
        public LGIcon Icon;
        public Color Color;
        public string Title;
        /// <summary>Короткий эффект под названием («+4 титана в месяц»).</summary>
        public string Effect;
        /// <summary>Подробное описание для правой панели.</summary>
        public string Detail;
        public float Days;
        /// <summary>Срок без ускорения губернатором — по нему считается цена покупки.</summary>
        public float RawDays;
        public float Energy, Minerals, Alloys, Influence;
        /// <summary>null — можно заказать; иначе — почему нельзя.</summary>
        public string Blocker;
        /// <summary>Уже сделано (комплекс работает, терраформинг до конца) — карточка как итог.</summary>
        public bool Done;
        /// <summary>Действие без очереди (пропаганда).</summary>
        public bool Instant;
        /// <summary>Сколько таких заказов уже в очереди планеты или верфи.</summary>
        public int Queued;
        public Func<bool> Order;

        public bool Available => Blocker == null && !Done;
    }

    /// <summary>
    /// Каталог строительства планеты: районы, проекты (добыча, колония, терраформинг, роботы, генная адаптация),
    /// корабли на верфи этой системы и действия общества. Общий для меню строительства и окна колоний.
    /// </summary>
    public static class BuildCatalog
    {
        public static string CategoryName(BuildCategory c) => c switch
        {
            BuildCategory.District => Loc.T("Районы"),
            BuildCategory.Project => Loc.T("Проекты"),
            BuildCategory.Ship => Loc.T("Верфь"),
            _ => Loc.T("Общество")
        };

        public static LGIcon CategoryIcon(BuildCategory c) => c switch
        {
            BuildCategory.District => LGIcon.Industry,
            BuildCategory.Project => LGIcon.Planet,
            BuildCategory.Ship => LGIcon.Shipyard,
            _ => LGIcon.Society
        };

        private static ConstructionManager Builds => ConstructionManager.Instance;

        /// <summary>Сколько дней займёт планетарная стройка с учётом губернатора.</summary>
        private static float PlanetDays(PlanetData p, float days)
        {
            var sys = p.ParentSystem;
            float mult = sys != null ? LeaderManager.BuildSpeedMult(sys.Id, ConstructionManager.PlanetIndex(p)) : 1f;
            return days / Mathf.Max(0.1f, mult);
        }

        private static string Afford(float energy, float minerals, float alloys, float influence)
            => CostCheck.Affordable(energy, minerals, alloys, influence) ? null : CostCheck.Full(energy, minerals, alloys, influence);

        public static List<BuildOption> For(PlanetData p)
        {
            var list = new List<BuildOption>();
            if (p?.ParentSystem == null) return list;
            var sys = p.ParentSystem;
            bool owned = p.IsPlayerOwned;
            bool surveyed = sys.IsSurveyed;
            bool colony = p.Population > 0;
            var queue = Builds != null ? Builds.QueueFor(p) : new List<ConstructionJob>();
            bool queueFull = queue.Count >= ConstructionManager.MaxPlanetQueue;
            string common = !surveyed ? Loc.T("Нужна разведка системы")
                          : !owned ? Loc.T("Не ваша система — постройте форпост")
                          : null;
            string planetQueue = queueFull ? Loc.F("Очередь планеты заполнена ({0})", ConstructionManager.MaxPlanetQueue) : null;

            int Count(JobKind k, DistrictType? d = null)
            {
                int n = 0;
                foreach (var j in queue) if (j.Kind == k && (d == null || j.District == d.Value)) n++;
                return n;
            }

            // ---------- Районы ----------
            foreach (var t in new[] { DistrictType.Urban, DistrictType.Mining, DistrictType.Generator, DistrictType.Industrial })
            {
                float mi = DistrictInfo.MineralsCost(t), al = DistrictInfo.AlloysCost(t);
                var o = new BuildOption
                {
                    Id = "district_" + t, Category = BuildCategory.District, Icon = DistrictIcon(t), Color = DistrictInfo.Color(t),
                    Title = DistrictTitle(t), Effect = DistrictInfo.ShortDesc(t),
                    RawDays = ConstructionManager.DistrictDays(t), Days = PlanetDays(p, ConstructionManager.DistrictDays(t)), Minerals = mi, Alloys = al,
                    Queued = Count(JobKind.District, t)
                };
                o.Detail = DistrictDetail(p, t);
                o.Blocker = common ?? (!colony ? Loc.T("Сначала основайте колонию")
                          : p.BuiltDistricts + p.PendingDistricts >= p.MaxDistricts ? Loc.F("Нет места: районов {0} из {1}", p.BuiltDistricts + p.PendingDistricts, p.MaxDistricts)
                          : planetQueue ?? Afford(0f, mi, al, 0f));
                o.Order = () => p.TryBuildDistrict(t);
                list.Add(o);
            }

            // ---------- Проекты ----------
            {
                float mi = FleetManager.MiningStationMinerals;
                var o = new BuildOption
                {
                    Id = "mining", Category = BuildCategory.Project, Icon = LGIcon.Minerals, Color = new Color(0.30f, 0.92f, 0.70f),
                    Title = Loc.T("Добывающий комплекс"),
                    Effect = p.MineralDeposit + p.EnergyDeposit > 0 ? Loc.F("+{0} титана, +{1} гелия-3 в месяц", p.MineralDeposit, p.EnergyDeposit) : Loc.T("залежей нет"),
                    Detail = Loc.T("Автоматическая добыча без жителей: работает на любой планете системы, даже непригодной для жизни.\n") +
                             Loc.F("Окупится примерно за {0} мес.", Mathf.CeilToInt(mi / Mathf.Max(1, p.MineralDeposit + p.EnergyDeposit))),
                    RawDays = ConstructionManager.MiningStationDays, Days = PlanetDays(p, ConstructionManager.MiningStationDays), Minerals = mi, Queued = Count(JobKind.MiningStation),
                    Done = p.HasMiningStation
                };
                o.Blocker = o.Done ? null : common ?? (p.MineralDeposit + p.EnergyDeposit <= 0 ? Loc.T("На планете нет залежей")
                          : o.Queued > 0 ? Loc.T("Уже строится") : Afford(0f, mi, 0f, 0f));
                o.Order = () => FleetManager.Instance != null && FleetManager.Instance.BuildMiningStationOnPlanet(p);
                list.Add(o);
            }
            if (!colony || Count(JobKind.Colony) > 0)
            {
                var o = new BuildOption
                {
                    Id = "colony", Category = BuildCategory.Project, Icon = LGIcon.Population, Color = UIManager.DS.NeonCyan,
                    Title = Loc.T("Колония"), Effect = Loc.T("2 жителя, городской и горный районы"),
                    Detail = p.ColonyBenefitText(),
                    RawDays = ConstructionManager.ColonyDays, Days = PlanetDays(p, ConstructionManager.ColonyDays),
                    Minerals = PlanetData.ColonyMineralsCost, Alloys = PlanetData.ColonyAlloysCost, Influence = PlanetData.ColonyInfluenceCost,
                    Queued = Count(JobKind.Colony)
                };
                o.Blocker = common ?? (o.Queued > 0 ? Loc.T("Колонисты уже в пути")
                          : !p.CanColonize ? Loc.T("Планета непригодна для жизни")
                          : planetQueue ?? Afford(0f, o.Minerals, o.Alloys, o.Influence));
                o.Order = p.TryFoundColony;
                list.Add(o);
            }
            if (p.CanTerraform || p.HabitabilityPercent >= PlanetData.ComfortHabitability)
            {
                var next = p.TerraformNextType;
                var o = new BuildOption
                {
                    Id = "terraform", Category = BuildCategory.Project, Icon = LGIcon.Planet, Color = new Color(0.72f, 0.60f, 1f),
                    Title = Loc.T("Терраформинг"),
                    Effect = next.HasValue ? Loc.F("→ {0} мир", PlanetData.ClassName(next.Value)) : Loc.T("мир уже лучшего класса"),
                    Detail = Loc.T("Меняет климат планеты на шаг ближе к континентальному: выше пригодность, быстрее рост, больше жителей уживётся.\n") +
                             Loc.F("Сейчас пригодность {0}%.", p.HabitabilityPercent),
                    RawDays = ConstructionManager.TerraformDays, Days = PlanetDays(p, ConstructionManager.TerraformDays),
                    Energy = PlanetData.TerraformEnergyCost, Minerals = PlanetData.TerraformMineralsCost, Influence = PlanetData.TerraformInfluenceCost,
                    Queued = Count(JobKind.Terraform), Done = !next.HasValue
                };
                o.Blocker = o.Done ? null : common ?? (o.Queued > 0 ? Loc.T("Уже идёт") : planetQueue ?? Afford(o.Energy, o.Minerals, 0f, o.Influence));
                o.Order = p.TryStartTerraform;
                list.Add(o);
            }
            if (colony)
            {
                var o = new BuildOption
                {
                    Id = "robots", Category = BuildCategory.Project, Icon = LGIcon.Industry, Color = new Color(0.62f, 0.78f, 0.92f),
                    Title = Loc.T("Сборка роботов"), Effect = Loc.T("+1 житель-робот"),
                    Detail = Loc.F("{0}: {1}.\n", TraitInfo.Name(SpeciesTrait.Mechanical), TraitInfo.Desc(SpeciesTrait.Mechanical)) +
                             Loc.T("Робот занимает рабочее место как обычный житель и не снижает стабильность."),
                    RawDays = ConstructionManager.RobotDays, Days = PlanetDays(p, ConstructionManager.RobotDays), Energy = PlanetData.RobotEnergyCost, Alloys = PlanetData.RobotAlloysCost,
                    Queued = Count(JobKind.Robot)
                };
                o.Blocker = common ?? (!p.HasRoomForRobot ? Loc.T("Нет свободного жилья — постройте городской район")
                          : planetQueue ?? Afford(o.Energy, 0f, o.Alloys, 0f));
                o.Order = p.TryBuildRobot;
                list.Add(o);

                var target = p.GeneTarget;
                var fix = target != null ? SpeciesRegistry.FixableTrait(target) : null;
                var g = new BuildOption
                {
                    Id = "gene", Category = BuildCategory.Project, Icon = LGIcon.Research, Color = new Color(0.98f, 0.55f, 0.85f),
                    Title = Loc.T("Генная адаптация"),
                    Effect = target == null ? Loc.T("вид уже подходит миру")
                           : fix.HasValue ? Loc.F("убрать «{0}»", TraitInfo.Name(fix.Value)) : Loc.F("+ «{0}»", TraitInfo.Name(SpeciesTrait.RapidBreeders)),
                    Detail = Loc.T("Выводит линию самого многочисленного вида колонии под климат этой планеты: штраф «чужой климат» исчезает, одна слабость вида уходит.") +
                             (target != null ? Loc.F("\nВид: {0}", target.DisplayName) : ""),
                    RawDays = ConstructionManager.GeneAdaptDays, Days = PlanetDays(p, ConstructionManager.GeneAdaptDays), Energy = PlanetData.GeneEnergyCost, Influence = PlanetData.GeneInfluenceCost,
                    Queued = Count(JobKind.GeneAdapt), Done = target == null
                };
                g.Blocker = g.Done ? null : common ?? (g.Queued > 0 ? Loc.T("Уже идёт") : planetQueue ?? Afford(g.Energy, 0f, 0f, g.Influence));
                g.Order = p.TryGeneAdapt;
                list.Add(g);

                // ---------- Общество ----------
                var pr = new BuildOption
                {
                    Id = "propaganda", Category = BuildCategory.Society, Icon = LGIcon.Influence, Color = new Color(0.98f, 0.55f, 0.85f),
                    Title = Loc.T("Пропаганда"), Effect = Loc.F("+{0:0} стабильности на {1} мес.", PlanetStability.PropagandaBonus, PlanetStability.PropagandaDuration),
                    Detail = Loc.F("Плакаты, трансляции и праздники. Сейчас стабильность {0:0} — {1}.", p.Stability, PlanetStability.TierName(p.Tier).ToLower()),
                    Influence = PlanetStability.PropagandaInfluence, Instant = true
                };
                pr.Blocker = common ?? (p.PropagandaMonths > 0 ? Loc.F("Уже идёт: ещё {0} мес.", p.PropagandaMonths) : Afford(0f, 0f, 0f, pr.Influence));
                pr.Order = () => PlanetStability.TryPropaganda(p);
                list.Add(pr);

                // ---------- Верфь (корабли спускаются на воду в этой системе) ----------
                AddShips(list, sys, common);
            }
            return list;
        }

        private static void AddShips(List<BuildOption> list, StarSystem sys, string common)
        {
            var fm = FleetManager.Instance;
            var dm = ShipDesignManager.Instance;
            float mult = FactionTraits.BuildTimeMult(0);
            int queued = Builds != null ? Builds.ShipQueue(0).Count : 0;
            string yardNote = queued >= ConstructionManager.ShipyardSlots
                ? Loc.F("\n<color=#F2C46B>Стапели заняты ({0} в очереди): корабль встанет в очередь верфи.</color>", queued) : "";

            void Civil(string id, FleetType type, LGIcon icon, string title, string effect, string detail, float alloys, float energy)
            {
                float a = FleetManager.ShipAlloyCost(alloys, 0);
                var o = new BuildOption
                {
                    Id = id, Category = BuildCategory.Ship, Icon = icon, Color = UIManager.DS.Gold, Title = title, Effect = effect,
                    Detail = detail + yardNote, Days = ConstructionManager.ShipDays(type, ShipClass.Corvette) * mult, RawDays = ConstructionManager.ShipDays(type, ShipClass.Corvette) * mult, Alloys = a, Energy = energy
                };
                o.Blocker = common ?? Afford(energy, 0f, a, 0f);
                o.Order = () => fm != null && fm.BuildShip(type, sys.Id);
                list.Add(o);
            }
            Civil("ship_science", FleetType.Science, LGIcon.ScienceShip, Loc.T("Научный корабль"), Loc.T("разведка и аномалии"),
                  Loc.T("Изучает системы, открывает залежи и аномалии, находит путь к соседям."), FleetManager.ScienceShipAlloys, FleetManager.ScienceShipEnergy);
            Civil("ship_constructor", FleetType.Constructor, LGIcon.ConstructorShip, Loc.T("Строительный корабль"), Loc.T("форпосты в новых системах"),
                  Loc.T("Строит форпосты: каждая новая система расширяет границы и даёт добычу."), FleetManager.ConstructorAlloys, FleetManager.ConstructorEnergy);

            if (dm == null || fm == null) return;
            foreach (var cls in new[] { ShipClass.Corvette, ShipClass.Frigate, ShipClass.Destroyer })
            {
                var hull = dm.GetHull(cls);
                if (hull == null) continue;
                bool unlocked = hull.IsUnlocked();
                var d = unlocked ? fm.PlayerDesignFor(cls) : null;
                float cost = d != null ? FleetManager.ShipAlloyCost(d.AlloyCost, 0) : FleetManager.ShipAlloyCost(hull.BaseAlloyCost, 0);
                var o = new BuildOption
                {
                    Id = "ship_" + cls, Category = BuildCategory.Ship, Icon = ModuleVisuals.HullIcon(cls), Color = new Color(1f, 0.55f, 0.45f),
                    Title = d != null ? $"{hull.DisplayName} «{ShortName(d.Name)}»" : hull.DisplayName,
                    Effect = d != null ? Loc.F("урон {0:0}/день · корпус {1:0}", d.Dps * CombatManager.ShotsPerDay, d.Hull + d.Armor + d.Shields) : Loc.T("корпус не изучен"),
                    Days = ConstructionManager.ShipDays(FleetType.Military, cls) * mult, RawDays = ConstructionManager.ShipDays(FleetType.Military, cls) * mult, Alloys = cost, Energy = FleetManager.WarshipEnergy
                };
                o.Detail = d != null
                    ? Loc.F("Проект «{0}» — основной для корпуса (меняется в конструкторе).\n", d.Name) +
                      Loc.F("Корпус {0:0} · броня {1:0} · щиты {2:0} · орудий {3}\n", d.Hull, d.Armor, d.Shields, d.Mounts.Count) +
                      Loc.F("Содержание {0:0.#} гелия-3/мес · флотский лимит {1}", FleetData.UpkeepFor(FleetType.Military, cls), EmpireEconomy.NavalSize(cls)) + yardNote
                    : Loc.T("Сначала изучите технологию корпуса.");
                o.Blocker = common ?? (!unlocked ? Loc.T("Корпус ещё не изучен")
                          : d == null || !d.IsPowerValid ? Loc.T("Нет годного проекта — откройте конструктор")
                          : Afford(o.Energy, 0f, o.Alloys, 0f));
                var design = d;
                o.Order = () => fm.BuildShipFromDesign(design, sys.Id);
                list.Add(o);
            }
        }

        /// <summary>Имя проекта без корпуса: «Корвет «Оса»» → «Оса».</summary>
        private static string ShortName(string n)
        {
            if (string.IsNullOrEmpty(n)) return "?";
            int a = n.IndexOf('«'), b = n.LastIndexOf('»');
            return a >= 0 && b > a ? n.Substring(a + 1, b - a - 1) : n;
        }

        public static LGIcon DistrictIcon(DistrictType t) => t switch
        {
            DistrictType.Urban => LGIcon.Housing,
            DistrictType.Mining => LGIcon.Minerals,
            DistrictType.Generator => LGIcon.Energy,
            _ => LGIcon.Alloys
        };

        public static string DistrictTitle(DistrictType t) => t switch
        {
            DistrictType.Urban => Loc.T("Городской район"),
            DistrictType.Mining => Loc.T("Горнодобывающий район"),
            DistrictType.Generator => Loc.T("Энергетический район"),
            _ => Loc.T("Промышленный район")
        };

        private static string DistrictDetail(PlanetData p, DistrictType t)
        {
            string res = t switch
            {
                DistrictType.Mining => "minerals",
                DistrictType.Generator => "energy",
                DistrictType.Industrial => "alloys",
                _ => null
            };
            string s = t == DistrictType.Urban
                ? Loc.T("Жильё для двух жителей и одно рабочее место. Без жилья колония перестаёт расти.\n")
                : Loc.T("Одно рабочее место: район производит, только когда в нём работает житель.\n");
            if (res != null && p.Population > 0)
            {
                float baseOut = t == DistrictType.Industrial ? 3f : 4f;
                s += Loc.F("С жителями этой планеты: ≈ {0:0.#} в месяц (виды ×{1:0.##}, стабильность).\n", baseOut * p.OutputMult(res), p.SpeciesOutputMult(res));
            }
            s += Loc.F("Районов {0} из {1} · жителей {2} · рабочих мест {3}", p.BuiltDistricts, p.MaxDistricts, p.Population, p.WorkerSlots);
            if (t != DistrictType.Urban && p.Population > 0 && p.IdlePops == 0)
                s += Loc.T("\n<color=#F2C46B>Свободных жителей нет: район заработает, когда колония вырастет.</color>");
            else if (t != DistrictType.Urban && p.IdlePops > 0)
                s += Loc.F("\n<color=#9EF0C8>Безработных {0} — они сразу выйдут на смену.</color>", p.IdlePops);
            return s;
        }
    }
}
