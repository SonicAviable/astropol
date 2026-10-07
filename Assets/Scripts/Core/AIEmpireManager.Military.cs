using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Военная часть ИИ. Боевые корабли действуют одной группой:
    ///   Gather  — сбор на опорной системе (у границы с игроком или в столице);
    ///   Defend  — ответ на вторжение: группа идёт к атакованной системе, если ей по силам;
    ///   Attack  — во время войны собравшаяся группа идёт осаждать слабо защищённую систему игрока;
    ///   Retreat — враг заметно сильнее: экстренный прыжок из боя и отход в ближайшую колонию на ремонт.
    /// </summary>
    public partial class AIEmpireManager
    {
        public enum ArmyMode { Gather, Defend, Attack, Retreat }

        public ArmyMode Mode { get; private set; } = ArmyMode.Gather;
        public int ArmyTargetSystemId { get; private set; } = -1;
        private int _rallySystemId = -1;

        /// <summary>Доля сил, которая должна собраться в точке сбора, прежде чем группа выступит.</summary>
        private const float AssembleFraction = 0.75f;

        // ==================== ПОСТРОЙКА ФЛОТА ====================

        private void ThinkShipbuilding()
        {
            if (SpawnSystem < 0) return;
            // Верфь занята — новые корпуса закладываем, когда освободится место
            int queued = QueuedShips(FleetType.Military);
            if (queued >= ConstructionManager.ShipyardSlots) return;
            float myPower = EmpireStats.MilitaryPower(OwnerId) + queued * 200f;
            float rivalPower = RivalPowerForBuild();
            int years = TimeManager.Instance != null ? Mathf.Max(0, TimeManager.Instance.Year - 2200) : 0;

            // Желаемая мощь: не отставать от самого опасного соседа (с поправкой на характер) и расти со временем
            float baseline = (350f + years * 140f) * Profile.FleetShare * 2f;
            float desired = Mathf.Max(baseline, rivalPower * Profile.DesiredPowerRatio);
            if (AtWarWithAnyone) desired *= 1.3f;
            if (myPower >= desired) return;

            ShipClass hull = PickHull();
            var design = GetDesign(hull);
            if (design == null) return;
            float cost = FleetManager.ShipAlloyCost(design.AlloyCost, OwnerId);

            // Сплавы: часть бюджета держим на форпосты и районы (у воинственных — меньше)
            float reserve = Mathf.Lerp(120f, 30f, Profile.FleetShare);
            if (AtWarWithAnyone) reserve *= 0.4f;
            if (Alloys < cost + reserve || EnergyCredits < FleetManager.WarshipEnergy) return;

            // Содержание: «больше кораблей — меньше энергии» действует и на ИИ
            float extra = EmpireEconomy.ExtraUpkeepForShip(_report, hull);
            bool desperate = AtWarWithAnyone && EnergyCredits > 250f;
            if (MonthlyEnergyIncome - extra < 1f && !desperate) return;
            if (_report.NavalUsed + EmpireEconomy.NavalSize(hull) > _report.NavalCapacity
                && !(AtWarWithAnyone && Personality == AIPersonality.Militarist)) return;

            Alloys -= cost;
            EnergyCredits -= FleetManager.WarshipEnergy;
            QueueShip(FleetType.Military, hull, FleetManager.WarshipEnergy, cost);
        }

        private ShipClass PickHull()
        {
            if (Bonuses.DestroyerUnlocked && Alloys > 320f) return ShipClass.Destroyer;
            if (Alloys > 200f && Random.value < 0.6f) return ShipClass.Frigate;
            return ShipClass.Corvette;
        }

        // ==================== УПРАВЛЕНИЕ ГРУППОЙ ====================

        private void ThinkMilitary()
        {
            var ships = WarShips();
            if (ships.Count == 0) { Mode = ArmyMode.Gather; return; }
            var fm = FleetManager.Instance;
            if (fm == null) return;

            // 1. Отступление: в бою против заметно более сильного врага — прыжок к ближайшей колонии
            bool retreated = false;
            var checkedSystems = new HashSet<int>();
            foreach (var s in ships)
            {
                if (!s.Data.InCombat || !checkedSystems.Add(s.Data.CurrentSystemId)) continue;
                int sys = s.Data.CurrentSystemId;
                float mine = SidePower(OwnerId, sys);
                float enemy = EnemyPowerIn(sys);
                if (enemy <= mine * Profile.RetreatRatio) continue;

                int haven = SafeHaven(sys);
                foreach (var t in ships)
                    if (t.Data.InCombat && t.Data.CurrentSystemId == sys)
                        CombatManager.Instance?.TryEmergencyFtl(t, haven);
                retreated = true;
            }
            if (retreated)
            {
                if (Mode != ArmyMode.Retreat && AtWar)
                    NotificationCenter.Show("Враг отступает", $"Флот {AIName} уходит из боя на ремонт", NotificationCenter.Kind.Info, 4f);
                Mode = ArmyMode.Retreat;
                ArmyTargetSystemId = SafeHaven(CapitalSystemId);
            }

            float armyPower = 0f;
            foreach (var s in ships) armyPower += CombatMath.Power(s.Data);

            // 2. Отход и ремонт, пока группа не восстановится
            if (Mode == ArmyMode.Retreat)
            {
                int haven = ArmyTargetSystemId >= 0 ? ArmyTargetSystemId : SafeHaven(CapitalSystemId);
                MoveAll(ships, haven);
                if (AverageIntegrity(ships) >= 0.85f && AllAt(ships, haven)) Mode = ArmyMode.Gather;
                return;
            }

            // 3. Защита своих систем
            if (AtWarWithAnyone)
            {
                int threat = FindThreat(out float threatPower);
                if (threat >= 0)
                {
                    float baseHelp = CombatManager.Instance != null ? CombatManager.Instance.StarbasePower(threat) : 0f;
                    if (armyPower + baseHelp >= threatPower * 0.9f)
                    {
                        Mode = ArmyMode.Defend;
                        ArmyTargetSystemId = threat;
                        MoveAll(ships, threat);
                        return;
                    }
                    // Не по силам — копим флот вдали от удара
                    Mode = ArmyMode.Gather;
                    _rallySystemId = SafeHaven(CapitalSystemId);
                    MoveAll(ships, _rallySystemId);
                    return;
                }
            }

            // 4. Наступление
            if (AtWarWithAnyone)
            {
                _rallySystemId = StagingSystem();
                if (Mode == ArmyMode.Attack && IsValidSiegeTarget(ArmyTargetSystemId))
                {
                    MoveAll(ships, ArmyTargetSystemId);
                    return;
                }

                float assembled = 0f;
                foreach (var s in ships)
                    if (s.Data.CurrentSystemId == _rallySystemId && IsIdle(s.Data)) assembled += CombatMath.Power(s.Data);

                if (assembled >= armyPower * AssembleFraction && AverageIntegrity(ships) > 0.6f)
                {
                    int target = PickSiegeTarget(armyPower);
                    if (target >= 0)
                    {
                        Mode = ArmyMode.Attack;
                        ArmyTargetSystemId = target;
                        MoveAll(ships, target);
                        return;
                    }
                }
                Mode = ArmyMode.Gather;
                ArmyTargetSystemId = -1;
                MoveAll(ships, _rallySystemId);
                return;
            }

            // 5. Мирное время — охрана границы
            Mode = ArmyMode.Gather;
            ArmyTargetSystemId = -1;
            _rallySystemId = StagingSystem();
            MoveAll(ships, _rallySystemId);
        }

        private bool IsValidSiegeTarget(int sysId)
        {
            var s = EmpireStats.GetSystem(sysId);
            return s != null && IsEnemy(s.OwnerId) && s.HasStarbase;
        }

        /// <summary>Самая опасная угроза: наша система с вражеским флотом (осада, колонии — важнее).</summary>
        private int FindThreat(out float threatPower)
        {
            threatPower = 0f;
            var fm = FleetManager.Instance;
            int best = -1;
            float bestPriority = 0f;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != OwnerId) continue;
                float enemy = EnemyFleetPowerIn(s.Id);
                if (enemy <= 0f) continue;
                float priority = 100f;
                foreach (var p in s.Planets) if (p.Population > 0) priority += 50f + p.Population * 5f;
                if (s.Id == CapitalSystemId) priority += 300f;
                var siege = SiegeManager.Instance?.GetSiege(s.Id);
                if (siege != null) priority += siege.Progress * 10f;
                if (priority > bestPriority) { bestPriority = priority; best = s.Id; threatPower = enemy; }
            }
            return best;
        }

        /// <summary>Сила стороны в системе: её корабли плюс звёздная база, если система её.</summary>
        private float SidePower(int owner, int systemId)
        {
            float p = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPowerInSystem(owner, systemId) : 0f;
            var cm = CombatManager.Instance;
            if (cm != null && systemId >= 0 && systemId < EmpireStats.Systems.Count && EmpireStats.Systems[systemId].OwnerId == owner)
                p += cm.StarbasePower(systemId);
            return p;
        }

        /// <summary>Цель осады: система игрока у нашей границы, ценная и слабо защищённая.</summary>
        private int PickSiegeTarget(float armyPower)
        {
            var fm = FleetManager.Instance;
            var fromRally = Distances(_rallySystemId >= 0 ? _rallySystemId : CapitalSystemId, 12);
            int best = -1;
            float bestScore = float.MinValue;
            foreach (var s in EmpireStats.Systems)
            {
                if (!IsEnemy(s.OwnerId) || !s.HasStarbase) continue;
                if (!fromRally.TryGetValue(s.Id, out int jumps)) continue;
                // Оборона = флот владельца системы + её звёздная база
                float defense = SidePower(s.OwnerId, s.Id);
                if (defense > armyPower * 0.8f) continue;

                float score = -jumps * 4f - defense / Mathf.Max(1f, armyPower) * 40f;
                if (BordersOwn(s)) score += 20f;
                foreach (var p in s.Planets) if (p.Population > 0) score += 15f + p.Population * 2f;
                var owner = For(s.OwnerId);
                if (s.OwnerId == 0 ? s.Id == EconomyManager.PlayerCapitalId : owner != null && s.Id == owner.CapitalSystemId) score -= 10f;
                if (score > bestScore) { bestScore = score; best = s.Id; }
            }
            return best;
        }

        /// <summary>Ближайшая своя колония (там ремонт быстрее), иначе ближайшая своя система.</summary>
        private int SafeHaven(int from)
        {
            if (from < 0) from = CapitalSystemId;
            var dist = Distances(from, 40);
            int bestColony = -1, bestAny = -1, dColony = int.MaxValue, dAny = int.MaxValue;
            foreach (var kv in dist)
            {
                var s = EmpireStats.GetSystem(kv.Key);
                if (s == null || s.OwnerId != OwnerId) continue;
                if (kv.Value < dAny) { dAny = kv.Value; bestAny = s.Id; }
                bool colony = false;
                foreach (var p in s.Planets) if (p.Population > 0) { colony = true; break; }
                if (colony && kv.Value < dColony) { dColony = kv.Value; bestColony = s.Id; }
            }
            if (bestColony >= 0) return bestColony;
            return bestAny >= 0 ? bestAny : CapitalSystemId;
        }

        /// <summary>Своя система, ближайшая к территории главного противника, — точка сбора и охраны границы.</summary>
        private int StagingSystem()
        {
            int focus = FocusRival();
            if (focus < 0) return CapitalSystemId;
            var toPlayer = DistancesFromOwner(focus, 40);
            int best = -1, bestDist = int.MaxValue, bestColonies = -1;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != OwnerId) continue;
                if (!toPlayer.TryGetValue(s.Id, out int d)) continue;
                int colonies = 0;
                foreach (var p in s.Planets) if (p.Population > 0) colonies++;
                if (d < bestDist || (d == bestDist && colonies > bestColonies))
                {
                    bestDist = d; best = s.Id; bestColonies = colonies;
                }
            }
            return best >= 0 ? best : CapitalSystemId;
        }

        private static float AverageIntegrity(List<FleetView> ships)
        {
            if (ships.Count == 0) return 1f;
            float sum = 0f;
            foreach (var s in ships)
                sum += (s.Data.HullPoints + s.Data.ArmorPoints) / Mathf.Max(1f, s.Data.MaxHullPoints + s.Data.MaxArmorPoints);
            return sum / ships.Count;
        }

        private static bool AllAt(List<FleetView> ships, int sysId)
        {
            foreach (var s in ships)
                if (s.Data.CurrentSystemId != sysId || s.Data.State != FleetState.Orbiting) return false;
            return true;
        }

        private static int FinalDestination(FleetData d)
        {
            int last = -1;
            foreach (int step in d.Path) last = step;
            if (last >= 0) return last;
            if (d.State == FleetState.InHyperlane && d.TargetSystemId >= 0) return d.TargetSystemId;
            return d.CurrentSystemId;
        }

        /// <summary>Отправить группу к системе (кто в бою — остаётся сражаться).</summary>
        private static void MoveAll(List<FleetView> ships, int target)
        {
            if (target < 0) return;
            var fm = FleetManager.Instance;
            foreach (var s in ships)
            {
                if (s.Data.InCombat) continue;
                if (FinalDestination(s.Data) == target) continue;
                fm?.IssueMoveOrder(s, target);
            }
        }

        // ==================== СОХРАНЕНИЕ ====================

        private void CaptureMilitary(AISave s)
        {
            s.ArmyMode = (int)Mode;
            s.ArmyTarget = ArmyTargetSystemId;
            s.Rally = _rallySystemId;
        }

        private void RestoreMilitary(AISave s)
        {
            Mode = (ArmyMode)Mathf.Clamp(s.ArmyMode, 0, 3);
            ArmyTargetSystemId = s.ArmyTarget;
            _rallySystemId = s.Rally;
        }
    }
}
