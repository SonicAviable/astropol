using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Сила вольных миров — настоящая, на карте:
    ///   • Население живёт на планетах: родной мир (до 16 жителей) медленно растёт и раз в 6–10 лет основывает
    ///     колонию (до трёх) на пригодной планете своих систем. Колонии растут до 8 жителей.
    ///   • Гарнизонный флот: 2–4 корабля (наёмникам +1), растёт с территорией и восстанавливается после потерь
    ///     (новый корабль раз в 8 месяцев, у наёмников — в 5). Стоит в родной системе и выходит навстречу
    ///     угрозе в своих системах, если ему по силам. Бьётся с теми, кто воюет с миром, и с пиратами.
    ///     Корабли принадлежат владельцу <see cref="FleetOwnerBase"/>+номер — у каждого мира свой.
    ///   • Звёздная база в каждой системе мира (в присоединённых — вдвое слабее родной). Систему берут
    ///     блокадой: флот сильнее базы, а корабли гарнизона в ней уничтожены или ушли. Присоединённая система
    ///     переходит к победителю сразу (60 дней), родная — вместе со всем, что осталось от мира.
    /// </summary>
    public static partial class NeutralWorlds
    {
        public const int FleetOwnerBase = 11, FleetOwnerSlots = 10;
        public const int GarrisonStart = 2;
        public const int MaxColonies = 3;
        public const int HomePopCap = 16, ColonyPopCap = 8;
        public const float TerritorySiegeDays = 60f;

        // ==================== ВЛАДЕЛЕЦ КОРАБЛЕЙ ====================

        public static bool IsFleetOwner(int owner) => owner >= FleetOwnerBase && owner < FleetOwnerBase + FleetOwnerSlots;

        public static NeutralWorld ByFleetOwner(int owner)
        {
            if (!IsFleetOwner(owner)) return null;
            foreach (var w in s.Worlds) if (w.FleetOwner == owner) return w;
            return null;
        }

        private static int FreeFleetOwner()
        {
            for (int i = 0; i < FleetOwnerSlots; i++)
            {
                int o = FleetOwnerBase + i;
                bool used = false;
                foreach (var w in s.Worlds) if (w.FleetOwner == o) { used = true; break; }
                if (!used) return o;
            }
            return FleetOwnerBase;
        }

        /// <summary>Имя стороны для боя и новостей (null — не корабли вольного мира).</summary>
        public static string FleetOwnerName(int owner)
        {
            if (!IsFleetOwner(owner)) return null;
            var w = ByFleetOwner(owner);
            return w != null ? Title(w) : Loc.T("Вольный гарнизон");
        }

        public static Color FleetOwnerColor(int owner)
        {
            var w = ByFleetOwner(owner);
            return w != null ? ColorOf(w.Kind) : new Color(0.85f, 0.82f, 0.7f);
        }

        /// <summary>Цвет территории вольных миров на карте (все миры — одним слоем границ).</summary>
        public static readonly Color TerritoryColor = new Color(0.93f, 0.86f, 0.62f);

        /// <summary>
        /// Враждебны ли стороны, если хоть одна из них — гарнизон вольного мира: гарнизоны между собой не воюют,
        /// с пиратами и левиафанами — всегда, с империей — если она воюет с этим миром.
        /// </summary>
        public static bool FleetsHostile(int a, int b)
        {
            var wa = ByFleetOwner(a);
            var wb = ByFleetOwner(b);
            if (IsFleetOwner(a) && IsFleetOwner(b)) return false;
            var w = wa ?? wb;
            int other = IsFleetOwner(a) ? b : a;
            if (w == null || other < 0) return false;
            if (Threats.IsThreat(other)) return true;
            return w.AtWarWith(other);
        }

        // ==================== НАСЕЛЕНИЕ И КОЛОНИИ ====================

        private static int GenericSpecies => SpeciesRegistry.Founder("generic").Id;

        /// <summary>Все населённые планеты мира.</summary>
        public static IEnumerable<PlanetData> Colonies(NeutralWorld w)
        {
            foreach (int id in Systems(w))
            {
                var sys = EmpireStats.GetSystem(id);
                if (sys == null) continue;
                foreach (var p in sys.Planets) if (p.Population > 0) yield return p;
            }
        }

        public static int TotalPop(NeutralWorld w)
        {
            int n = 0;
            foreach (var p in Colonies(w)) n += p.Population;
            return n;
        }

        public static int ColonyCount(NeutralWorld w)
        {
            int n = 0;
            foreach (var _ in Colonies(w)) n++;
            return Mathf.Max(0, n - 1);
        }

        private static bool Habitable(PlanetData p) => p.Type == PlanetType.Continental || p.Type == PlanetType.Desert;

        /// <summary>Родная планета: самая населённая (или пригодная для жизни в родной системе).</summary>
        public static PlanetData HomePlanet(NeutralWorld w)
        {
            var sys = EmpireStats.GetSystem(w.SystemId);
            if (sys == null) return null;
            PlanetData best = null;
            foreach (var p in sys.Planets) if (p.Population > 0 && (best == null || p.Population > best.Population)) best = p;
            return best;
        }

        /// <summary>Заселить родную планету: районы и жители мира (свой вид — «Поселенцы»).</summary>
        private static void SetupHome(NeutralWorld w)
        {
            var sys = EmpireStats.GetSystem(w.SystemId);
            if (sys == null) return;
            sys.GeneratePlanets();
            PlanetData home = null;
            foreach (var p in sys.Planets) if (Habitable(p)) { home = p; break; }
            if (home == null) foreach (var p in sys.Planets) if (p.Type != PlanetType.GasGiant) { home = p; break; }
            if (home == null) return;
            home.SetupAsStartingColony(pop: 0, urban: 2, mining: 1, generator: 1, industrial: w.Kind == NeutralCharacter.Industry ? 2 : 1);
            home.AddPops(GenericSpecies, Mathf.Max(4, w.Population), 0.2f);
        }

        /// <summary>Рост жителей и новые колонии.</summary>
        private static void MonthlyGrowth(NeutralWorld w)
        {
            var home = HomePlanet(w);
            foreach (var p in new List<PlanetData>(Colonies(w)))
            {
                bool isHome = p == home;
                int cap = isHome ? HomePopCap : ColonyPopCap;
                float chance = isHome ? 1f / 15f : 1f / 20f;
                if (w.WarMask != 0) chance *= 0.5f;
                if (p.Population < cap && UnityEngine.Random.value < chance) p.AddPops(GenericSpecies, 1, 0.2f);
            }

            if (w.ColonyWait < 0) w.ColonyWait = UnityEngine.Random.Range(60, 97);
            if (w.WarMask != 0 || --w.ColonyWait > 0) return;
            w.ColonyWait = UnityEngine.Random.Range(72, 121);
            if (home == null || home.Population < 6 || ColonyCount(w) >= MaxColonies) return;

            PlanetData target = null;
            foreach (int id in Systems(w))
            {
                var sys = EmpireStats.GetSystem(id);
                if (sys == null) continue;
                sys.GeneratePlanets();
                foreach (var p in sys.Planets)
                    if (p.Population == 0 && Habitable(p) && (target == null || p.Size > target.Size)) target = p;
            }
            if (target == null) return;
            target.SetupAsStartingColony(pop: 0, urban: 1, mining: 1, generator: 0);
            home.RemovePops(GenericSpecies, 2);
            target.AddPops(GenericSpecies, 2, 0.2f);
            if (w.Met(0))
                GalacticNews.Post(NewsKind.Colony, Loc.F("{0} основывает колонию «{1}»", Title(w), target.Name), target.ParentSystem != null ? target.ParentSystem.Id : w.SystemId);
        }

        // ==================== ГАРНИЗОН ====================

        public static int GarrisonTarget(NeutralWorld w)
            => Mathf.Min(4, 2 + (w.Territory != null ? w.Territory.Count : 0)) + (w.Kind == NeutralCharacter.Mercenary ? 1 : 0);

        public static List<FleetView> Garrison(NeutralWorld w)
        {
            var list = new List<FleetView>();
            var fm = FleetManager.Instance;
            if (fm == null) return list;
            foreach (var f in fm.AllFleets)
                if (f?.Data != null && !f.Data.Destroyed && f.Data.OwnerId == w.FleetOwner) list.Add(f);
            return list;
        }

        public static float GarrisonPower(NeutralWorld w)
        {
            float p = 0f;
            foreach (var f in Garrison(w)) p += CombatMath.Power(f.Data);
            return p;
        }

        private static void MonthlyGarrison(NeutralWorld w)
        {
            if (Garrison(w).Count >= GarrisonTarget(w)) { w.GarrisonWait = 0; return; }
            if (--w.GarrisonWait > 0) return;
            // Пока у родной системы враг — верфь молчит
            if (HostilePowerIn(w, w.SystemId) > 0f) return;
            SpawnGuard(w);
            w.GarrisonWait = w.Kind == NeutralCharacter.Mercenary ? 5 : 8;
        }

        /// <summary>Новый корабль гарнизона в родной системе: крепчает с годами, у наёмников — сильнее.</summary>
        private static void SpawnGuard(NeutralWorld w)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            int years = YearsPlayed;
            bool merc = w.Kind == NeutralCharacter.Mercenary;
            var hull = merc || years >= 12 ? ShipClass.Frigate : ShipClass.Corvette;
            var fv = fm.CreateOwnedFleet(Loc.F("{0} · Стража", Title(w)), w.SystemId, FleetType.Military, w.FleetOwner, hull);
            if (fv == null) return;
            var dm = ShipDesignManager.Instance;
            var design = dm != null ? dm.CreateAutoDesign(hull, WeaponDamageType.Energy, WeaponDamageType.Kinetic, _ => false, Loc.T("Страж вольного мира")) : null;
            var d = fv.Data;
            if (design != null) d.ApplyDesign(design);
            float scale = Mathf.Clamp(0.9f + years * 0.035f, 0.9f, 2.4f) * (merc ? 1.2f : 1f);
            d.MaxHullPoints = d.HullPoints = d.MaxHullPoints * scale;
            d.MaxArmorPoints = d.ArmorPoints = d.MaxArmorPoints * scale;
            d.MaxShieldPoints = d.ShieldPoints = d.MaxShieldPoints * scale;
            var weapons = new List<WeaponMount>();
            foreach (var wm in d.Weapons) weapons.Add(new WeaponMount(wm.Type, wm.Damage * scale, wm.FireRate));
            d.SetWeapons(weapons);
            d.UpkeepEnergy = 0f;
        }

        /// <summary>Сила врагов мира (воюющих с ним империй и угроз) в системе.</summary>
        private static float HostilePowerIn(NeutralWorld w, int systemId)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return 0f;
            float p = 0f;
            foreach (var f in fm.GetFleetsInSystem(systemId))
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.State == FleetState.InHyperlane || d.Type != FleetType.Military) continue;
                if (FleetsHostile(w.FleetOwner, d.OwnerId)) p += CombatMath.Power(d);
            }
            return p;
        }

        private static bool ThreatIn(int systemId)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return false;
            foreach (var f in fm.GetFleetsInSystem(systemId))
                if (f?.Data != null && !f.Data.Destroyed && Threats.IsThreat(f.Data.OwnerId)) return true;
            return false;
        }

        /// <summary>
        /// Приказы гарнизону: самая опасная угроза в системах мира — туда, если по силам (с базой системы);
        /// иначе и без угроз — домой.
        /// </summary>
        private static void DailyGarrison(NeutralWorld w)
        {
            var ships = Garrison(w);
            if (ships.Count == 0) return;
            float power = 0f;
            foreach (var f in ships) power += CombatMath.Power(f.Data);

            int target = w.SystemId;
            float worst = 0f;
            foreach (int id in Systems(w))
            {
                float enemy = HostilePowerIn(w, id);
                if (enemy <= worst) continue;
                if (power + StationDefense(w, id) * 0.5f >= enemy * 0.8f) { worst = enemy; target = id; }
            }

            var fm = FleetManager.Instance;
            foreach (var f in ships)
            {
                var d = f.Data;
                if (d.InCombat) continue;
                int dest = d.State == FleetState.InHyperlane ? d.TargetSystemId : d.CurrentSystemId;
                foreach (int step in d.Path) dest = step;
                if (dest == target) continue;
                fm.IssueMoveOrder(f, target, ignoreBorders: true);
            }
        }

        private static void DisbandGarrison(NeutralWorld w)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            foreach (var f in Garrison(w)) fm.DisbandFleet(f);
        }

        // ==================== ЗВЁЗДНЫЕ БАЗЫ И БЛОКАДА ====================

        /// <summary>База системы мира: в родной — полная сила гарнизона, в присоединённых — половина.</summary>
        public static float StationDefense(NeutralWorld w, int systemId)
            => Defense(w) * (systemId == w.SystemId ? 1f : 0.5f);

        public static float SiegeNeed(NeutralWorld w, int systemId)
            => systemId == w.SystemId ? SiegeNeed(w) : TerritorySiegeDays;

        public static NeutralSiege SiegeOf(NeutralWorld w, int systemId)
        {
            foreach (var g in w.Sieges) if (g.SystemId == systemId) return g;
            return null;
        }

        private static void EndWar(NeutralWorld w, int owner)
        {
            w.WarMask &= ~(1 << owner);
            w.Favor[owner] = Mathf.Min(w.Favor[owner], -40f);
            w.Truce[owner] = TruceMonths;
            w.Sieges.RemoveAll(g => g.Besieger == owner);
        }

        /// <summary>
        /// Блокада каждой системы мира: флот воюющей империи сильнее базы и в системе нет кораблей гарнизона —
        /// дни идут; иначе прогресс спадает. База обстреливает корабли врагов в своей системе.
        /// </summary>
        private static void DailySiege(NeutralWorld w)
        {
            if (w.WarMask == 0) { w.Sieges.Clear(); return; }
            var fm = FleetManager.Instance;
            if (fm == null) return;
            foreach (int id in new List<int>(Systems(w)))
            {
                float defense = StationDefense(w, id);
                int best = -1;
                float bestPower = 0f;
                for (int o = 0; o < NeutralWorld.Owners; o++)
                {
                    if (!w.AtWarWith(o)) continue;
                    float p = fm.GetMilitaryPowerInSystem(o, id);
                    if (p > bestPower) { bestPower = p; best = o; }
                }

                var g = SiegeOf(w, id);
                bool guarded = fm.GetMilitaryPowerInSystem(w.FleetOwner, id) > 0f;
                if (best >= 0 && bestPower >= defense && !guarded)
                {
                    if (g == null) w.Sieges.Add(g = new NeutralSiege { SystemId = id });
                    if (g.Besieger != best) { g.Besieger = best; g.Days = 0f; }
                    g.Days += 1f;
                }
                else if (g != null)
                {
                    g.Days = Mathf.Max(0f, g.Days - 2f);
                    if (g.Days <= 0f) w.Sieges.Remove(g);
                }

                if (best >= 0) Bombard(w, id, defense, bestPower >= defense ? 1f : 2f);
                if (g != null && g.Besieger >= 0 && g.Days >= SiegeNeed(w, id))
                {
                    if (id == w.SystemId) { Capture(w, g.Besieger); return; }
                    CaptureTerritory(w, id, g.Besieger);
                }
            }
        }

        /// <summary>База обстреливает корабли воюющих с миром империй в своей системе; разбитый корабль гибнет.</summary>
        private static void Bombard(NeutralWorld w, int systemId, float defense, float mult)
        {
            var fm = FleetManager.Instance;
            var dead = new List<FleetView>();
            foreach (var f in fm.GetFleetsInSystem(systemId))
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.State == FleetState.InHyperlane) continue;
                if (!w.AtWarWith(d.OwnerId)) continue;
                float dmg = defense * 0.0015f * mult;
                float shield = Mathf.Min(d.ShieldPoints, dmg);
                d.ShieldPoints -= shield;
                d.HullPoints -= dmg - shield;
                if (d.HullPoints <= 0f) dead.Add(f);
            }
            foreach (var f in dead)
            {
                if (f.Data.OwnerId == 0)
                    NotificationCenter.Show(Loc.T("Потеря при штурме"), Loc.F("{0}: база {1} уничтожила корабль", f.Data.Name, Title(w)), NotificationCenter.Kind.Warning, 5f);
                fm.DisbandFleet(f);
            }
        }

        /// <summary>Система переходит к победителю: колонии остаются (жители — чужой вид, интеграция низкая).</summary>
        private static void TakeSystem(StarSystem sys, int owner)
        {
            if (sys == null) return;
            sys.OwnerId = owner;
            sys.HasStarbase = true;
            sys.MarkSurveyedByAll();
            foreach (var p in sys.Planets)
                if (p.Population > 0 && p.Pops != null)
                    foreach (var g in p.Pops) g.Integration = Mathf.Min(g.Integration, 0.2f);
        }

        /// <summary>Присоединённая система взята блокадой — переходит к победителю, мир продолжает жить.</summary>
        private static void CaptureTerritory(NeutralWorld w, int systemId, int owner)
        {
            var sys = EmpireStats.GetSystem(systemId);
            w.Territory.Remove(systemId);
            w.Sieges.RemoveAll(g => g.SystemId == systemId);
            TakeSystem(sys, owner);
            if (owner == 0)
                NotificationCenter.Show(Loc.T("Система взята"), Loc.F("{0}: база пала, система {1} теперь ваша", Title(w), sys != null ? sys.Name : "?"),
                    NotificationCenter.Kind.Success, 7f);
            else if (w.Met(0))
                GalacticNews.Post(NewsKind.Conquest, Loc.F("{0} отбирает у вольного мира систему {1}", OwnerName(owner), sys != null ? sys.Name : "?"), systemId);
            GalaxyView.Instance?.RefreshTerritoryVisuals();
            EconomyManager.Instance?.RecalculateAll();
            TechnologyManager.Instance?.RecountPopulation();
        }

        /// <summary>Родная система взята: мир перестаёт существовать, всё оставшееся — победителю, гарнизон сдаётся.</summary>
        private static void Capture(NeutralWorld w, int owner)
        {
            DisbandGarrison(w);
            s.Worlds.Remove(w);
            foreach (int id in Systems(w))
            {
                var sys = EmpireStats.GetSystem(id);
                if (sys != null && sys.OwnerId == Owner) TakeSystem(sys, owner);
            }

            // Остальные вольные миры боятся завоевателя
            foreach (var o in s.Worlds) { o.Favor[owner] = Mathf.Clamp(o.Favor[owner] - 15f, -100f, 100f); UpdatePatron(o); }

            if (owner == 0)
                NotificationCenter.Show(Loc.T("Вольный мир захвачен"), Loc.F("{0} теперь ваша колония. Жители ещё долго будут чужими", Title(w)), NotificationCenter.Kind.Success, 9f);
            else if (w.Met(0))
                GalacticNews.Post(NewsKind.Conquest, Loc.F("{0} захватывает вольный мир: {1}", OwnerName(owner), Title(w)), w.SystemId);

            GalaxyView.Instance?.RefreshTerritoryVisuals();
            EconomyManager.Instance?.RecalculateAll();
            TechnologyManager.Instance?.RecountPopulation();
        }
    }
}
