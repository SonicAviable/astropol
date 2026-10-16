using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Расчёт производства и содержания империи — один и тот же для игрока и ИИ.
    /// Всё, что даёт территория (районы, добывающие станции), считается заново каждый день,
    /// поэтому потерянная система сразу перестаёт приносить доход, а захваченная — начинает.
    /// </summary>
    public static class EmpireEconomy
    {
        public const float OutpostUpkeep = 2f;
        public const int BaseNavalCapacity = 8;
        public const int NavalCapacityPerColony = 2;

        public struct Report
        {
            // Производство (уже с множителями фракции и технологий)
            public float DistrictEnergy, DistrictMinerals, DistrictAlloys;
            public float StationEnergy, StationMinerals;
            public float TechEnergy;

            // Содержание
            public float FleetUpkeep, CivilianUpkeep, OutpostUpkeepTotal;
            public int Outposts;
            public int NavalUsed, NavalCapacity;
            public float OverCapacityMult;
            /// <summary>Множитель содержания флота от технологий (1 — без скидки).</summary>
            public float UpkeepMult;
            /// <summary>Доход от узлов силы под контролем (уже с множителем технологий).</summary>
            public float LandmarkEnergy, LandmarkMinerals;

            public float Energy => DistrictEnergy + StationEnergy + TechEnergy + LandmarkEnergy;
            public float Minerals => DistrictMinerals + StationMinerals + LandmarkMinerals;
            public float Alloys => DistrictAlloys;
            public float Upkeep => FleetUpkeep + CivilianUpkeep + OutpostUpkeepTotal;
        }

        /// <summary>Сколько «мест» флота занимает корабль.</summary>
        public static int NavalSize(ShipClass c) => c == ShipClass.Destroyer ? 3 : c == ShipClass.Frigate ? 2 : 1;

        public static Report Compute(int owner, int capitalSystemId, float energyMult, float mineralMult, float alloyMult,
                                     EmpireBonuses bonuses, bool bankrupt)
        {
            var r = new Report();
            bonuses ??= new EmpireBonuses();
            float penalty = bankrupt ? 0.5f : 1f;

            int colonies = 0, stE = 0, stM = 0;
            float en = 0f, min = 0f, al = 0f;
            foreach (var sys in EmpireStats.Systems)
            {
                if (sys.OwnerId != owner) continue;
                if (sys.HasStarbase && sys.Id != capitalSystemId) r.Outposts++;
                foreach (var p in sys.Planets)
                {
                    if (p.HasMiningStation) { stE += p.EnergyDeposit; stM += p.MineralDeposit; }
                    if (p.Type == PlanetType.GasGiant || p.Type == PlanetType.Molten) continue;
                    if (p.Population <= 0) continue;
                    colonies++;
                    // Губернатор, черты видов и стабильность меняют производство планеты
                    min += p.ProducedMineralsPerMonth * LeaderManager.PlanetOutputMult(p, "minerals") * p.OutputMult("minerals");
                    en += p.ProducedEnergyPerMonth * LeaderManager.PlanetOutputMult(p, "energy") * p.OutputMult("energy");
                    al += p.ProducedAlloysPerMonth * LeaderManager.PlanetOutputMult(p, "alloys") * p.OutputMult("alloys");
                }
            }

            r.DistrictEnergy = en * energyMult * bonuses.EnergyMult * penalty;
            r.DistrictMinerals = min * mineralMult * bonuses.MineralsMult * penalty;
            r.DistrictAlloys = al * alloyMult * bonuses.AlloysMult * penalty;
            r.StationEnergy = stE * energyMult * bonuses.EnergyMult * penalty;
            r.StationMinerals = stM * mineralMult * bonuses.MineralsMult * penalty;
            r.TechEnergy = bonuses.EnergyFlat;
            r.LandmarkEnergy = Landmarks.Energy(owner, bonuses) * penalty;
            r.LandmarkMinerals = Landmarks.Minerals(owner, bonuses) * penalty;

            r.NavalCapacity = BaseNavalCapacity + colonies * NavalCapacityPerColony + Mathf.RoundToInt(bonuses.NavalCapacity);
            float military = 0f;
            var fm = FleetManager.Instance;
            if (fm != null)
            {
                foreach (var f in fm.AllFleets)
                {
                    if (f?.Data == null || f.Data.Destroyed || f.Data.OwnerId != owner) continue;
                    if (f.Data.Type == FleetType.Military)
                    {
                        military += f.Data.UpkeepEnergy;
                        r.NavalUsed += NavalSize(f.Data.HullClass);
                    }
                    else r.CivilianUpkeep += f.Data.UpkeepEnergy;
                }
            }

            // Сверх флотского лимита содержание военных кораблей растёт пропорционально перебору
            int over = Mathf.Max(0, r.NavalUsed - r.NavalCapacity);
            r.OverCapacityMult = 1f + over / (float)Mathf.Max(1, r.NavalCapacity);
            r.UpkeepMult = Mathf.Max(0.3f, bonuses.UpkeepMult);
            r.FleetUpkeep = military * r.OverCapacityMult * r.UpkeepMult;
            r.OutpostUpkeepTotal = r.Outposts * OutpostUpkeep;
            return r;
        }

        /// <summary>Содержание, которое добавит ещё один военный корабль (с учётом лимита).</summary>
        public static float ExtraUpkeepForShip(Report r, ShipClass hull)
        {
            float baseUp = FleetData.UpkeepFor(FleetType.Military, hull);
            int used = r.NavalUsed + NavalSize(hull);
            float mult = 1f + Mathf.Max(0, used - r.NavalCapacity) / (float)Mathf.Max(1, r.NavalCapacity);
            float um = r.UpkeepMult > 0f ? r.UpkeepMult : 1f;
            float rawMilitary = r.FleetUpkeep / Mathf.Max(0.01f, r.OverCapacityMult * um);
            return (rawMilitary + baseUp) * mult * um - r.FleetUpkeep;
        }
    }
}
