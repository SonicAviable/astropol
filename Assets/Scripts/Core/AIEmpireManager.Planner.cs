using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Экономический план ИИ: сколько гелия-3, титана и сплавов ему нужно в месяц, чтобы держать флот,
    /// расширяться и строить. Район выбирается под самую большую нехватку, а не «по остаточному принципу» —
    /// иначе ИИ застревал с доходом гелия-3 около нуля и переставал закладывать корабли.
    /// </summary>
    public partial class AIEmpireManager
    {
        /// <summary>Нехватка, при которой ИИ строит производственный район даже без безработных (работники перераспределятся).</summary>
        private const float RebalanceDeficit = 0.35f;

        private int YearsPlayed => TimeManager.Instance != null ? Mathf.Max(0, TimeManager.Instance.Year - 2200) : 0;

        private int ColonyCount
        {
            get
            {
                int n = 0;
                foreach (var s in EmpireStats.Systems)
                {
                    if (s.OwnerId != OwnerId) continue;
                    foreach (var p in s.Planets) if (p.Population > 0) n++;
                }
                return n;
            }
        }

        /// <summary>Желаемая мощь флота: не отставать от самого опасного соседа (с поправкой на характер) и расти со временем.</summary>
        private float DesiredFleetPower()
        {
            float baseline = (350f + YearsPlayed * 140f) * Profile.FleetShare * 2f;
            float desired = Mathf.Max(Mathf.Max(baseline, RivalPowerForBuild() * Profile.DesiredPowerRatio), PlannedWarPower());
            if (AtWarWithAnyone) desired *= 1.3f;
            return desired;
        }

        private bool WantsMoreFleet => EmpireStats.MilitaryPower(OwnerId) < DesiredFleetPower();

        /// <summary>Проигрываем войну: враг заметно сильнее — сплавы в первую очередь на корабли.</summary>
        private bool LosingWar => AtWarWithAnyone && RivalPowerForBuild() > EmpireStats.MilitaryPower(OwnerId) * 1.2f;

        /// <summary>Нужный доход гелия-3: запас на содержание следующих кораблей и форпостов.</summary>
        private float EnergyTarget
        {
            get
            {
                float t = 6f + _report.Upkeep * 0.15f;
                if (WantsMoreFleet) t += 4f;
                if (AtWarWithAnyone) t += 3f;
                return t;
            }
        }

        /// <summary>Нужный доход сплавов: растёт с годами и воинственностью, в войне — больше.</summary>
        private float AlloyTarget
        {
            get
            {
                float t = (6f + YearsPlayed * 0.7f) * (0.5f + Profile.FleetShare);
                if (AtWarWithAnyone) t *= 1.4f;
                return Mathf.Min(t, 90f);
            }
        }

        /// <summary>Нужный доход титана: районы и станции на всех колониях.</summary>
        private float MineralTarget => 12f + ColonyCount * 4f;

        private static float Deficit(float income, float target) => target <= 0f ? 0f : (target - income) / target;

        /// <summary>Производственный район под самую большую нехватку; worst — её величина (≤ 0 — всего хватает).</summary>
        private DistrictType DeficitDistrict(out float worst)
        {
            float e = Deficit(MonthlyEnergyIncome, EnergyTarget);
            float a = Deficit(MonthlyAlloysIncome, AlloyTarget);
            float m = Deficit(MonthlyMineralsIncome, MineralTarget);
            if (MonthlyEnergyIncome < 0f) e = Mathf.Max(e, 1.5f);          // минус по гелию-3 — банкротство и роспуск флота
            if (LosingWar) a *= 1.5f;
            worst = e; var type = DistrictType.Generator;
            if (a > worst) { worst = a; type = DistrictType.Industrial; }
            if (m > worst) { worst = m; type = DistrictType.Mining; }
            return type;
        }

        /// <summary>Что строить для новых рабочих мест: нехватку, иначе любимый район характера.</summary>
        private DistrictType PlannedProduction(bool energyLow)
        {
            if (energyLow) return DistrictType.Generator;
            var t = DeficitDistrict(out float worst);
            if (worst > 0f) return t;
            return Profile.PreferredDistrict == DistrictType.Urban ? DistrictType.Generator : Profile.PreferredDistrict;
        }

        /// <summary>Система, где появляются новые корабли: столица, иначе самая населённая своя система со звёздной базой.</summary>
        private int ShipSpawnSystem
        {
            get
            {
                var cap = EmpireStats.GetSystem(CapitalSystemId);
                if (cap != null && cap.OwnerId == OwnerId) return CapitalSystemId;
                int best = -1, bestPop = -1;
                foreach (var s in EmpireStats.Systems)
                {
                    if (s.OwnerId != OwnerId || !s.HasStarbase) continue;
                    int pop = 0;
                    foreach (var p in s.Planets) pop += Mathf.Max(0, p.Population);
                    if (pop > bestPop) { bestPop = pop; best = s.Id; }
                }
                return best;
            }
        }
    }
}
