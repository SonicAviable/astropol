using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public enum PlanetType { Continental, Desert, Molten, GasGiant, Barren }

    public enum DistrictType { Urban, Mining, Generator, Industrial }

    public static class DistrictInfo
    {
        public static string Name(DistrictType t) => t switch
        {
            DistrictType.Urban      => "ГОРОДСКОЙ РАЙОН",
            DistrictType.Mining     => "ГОРНОДОБЫВАЮЩИЙ",
            DistrictType.Generator  => "ЭНЕРГОГЕНЕРАЦИЯ",
            DistrictType.Industrial => "ПРОМЫШЛЕННЫЙ",
            _ => "?"
        };

        public static string ShortDesc(DistrictType t) => t switch
        {
            DistrictType.Urban      => "+2 жилья, +1 рабочее место",
            DistrictType.Mining     => "+4 Титана/мес",
            DistrictType.Generator  => "+4 Гелия-3/мес",
            DistrictType.Industrial => "+3 Сплава/мес",
            _ => ""
        };

        public static int HousingPopCapacity(DistrictType t) => t == DistrictType.Urban ? 2 : 0;

        public static int MineralsCost(DistrictType t) => t switch
        {
            DistrictType.Urban      => 60,
            DistrictType.Mining     => 80,
            DistrictType.Generator  => 70,
            DistrictType.Industrial => 120,
            _ => 0
        };

        public static int AlloysCost(DistrictType t) => t switch
        {
            DistrictType.Urban      => 20,
            DistrictType.Mining     => 10,
            DistrictType.Generator  => 25,
            DistrictType.Industrial => 60,
            _ => 0
        };

        public static Color Color(DistrictType t) => t switch
        {
            DistrictType.Urban      => new Color(0.45f, 0.75f, 0.95f),
            DistrictType.Mining     => new Color(0.55f, 0.95f, 0.75f),
            DistrictType.Generator  => new Color(0.95f, 0.80f, 0.35f),
            DistrictType.Industrial => new Color(0.90f, 0.55f, 0.45f),
            _ => UnityEngine.Color.gray
        };
    }

    [Serializable]
    public class DistrictData
    {
        public DistrictType Type;
        public DistrictData(DistrictType t) { Type = t; }
    }

    [Serializable]
    public class PlanetData
    {
        public string Name;
        public PlanetType Type;
        public float OrbitRadius;
        public float OrbitSpeed;
        public float Size;
        public Color PlanetColor;
        public int EnergyDeposit;
        public int MineralDeposit;
        public bool HasMiningStation;
        public bool TerraformingInProgress;
        public int Population;
        public float PopGrowthProgress;

        public int MaxDistricts;
        public List<DistrictData> Districts = new List<DistrictData>();

        public int BuiltDistricts => Districts.Count;

        public int HousingCapacity
        {
            get
            {
                int cap = 0;
                foreach (var d in Districts) cap += DistrictInfo.HousingPopCapacity(d.Type);
                return cap;
            }
        }

        // Каждый район даёт 1 рабочее место (включая городской)
        public int WorkerSlots => Districts.Count;
        public int EmployedPops => Mathf.Min(Population, WorkerSlots);
        public int IdlePops => Mathf.Max(0, Population - EmployedPops);

        public PlanetData(string name, PlanetType type, float orbitRadius, float orbitSpeed,
                          float size, Color color, int energy, int minerals)
        {
            Name = name; Type = type; OrbitRadius = orbitRadius; OrbitSpeed = orbitSpeed;
            Size = size; PlanetColor = color; EnergyDeposit = energy; MineralDeposit = minerals;

            MaxDistricts = Mathf.Clamp(Mathf.RoundToInt(size * 5f), 4, 25);

            Population = 0;
            PopGrowthProgress = 0f;
        }

        /// <summary>Стартовая колония в столице.</summary>
        public void SetupAsStartingColony(int pop, int urban, int mining, int generator, int industrial = 0)
        {
            Population = Mathf.Clamp(pop, 0, MaxDistricts * 2);
            PopGrowthProgress = 0f;

            Districts.Clear();
            for (int i = 0; i < urban; i++)      Districts.Add(new DistrictData(DistrictType.Urban));
            for (int i = 0; i < mining; i++)     Districts.Add(new DistrictData(DistrictType.Mining));
            for (int i = 0; i < generator; i++)  Districts.Add(new DistrictData(DistrictType.Generator));
            for (int i = 0; i < industrial; i++) Districts.Add(new DistrictData(DistrictType.Industrial));
        }

        public int GetDistrictCount(DistrictType t)
        {
            int n = 0;
            foreach (var d in Districts) if (d.Type == t) n++;
            return n;
        }

        // ==================== РАСПРЕДЕЛЕНИЕ РАБОЧИХ ====================

        private void DistributeWorkers(out int urban, out int mining, out int gen, out int ind)
        {
            int pool = Population;

            urban = Mathf.Min(GetDistrictCount(DistrictType.Urban), pool);
            pool -= urban;

            mining = Mathf.Min(GetDistrictCount(DistrictType.Mining), pool);
            pool -= mining;

            gen = Mathf.Min(GetDistrictCount(DistrictType.Generator), pool);
            pool -= gen;

            ind = Mathf.Min(GetDistrictCount(DistrictType.Industrial), pool);
        }

        public int ProducedMineralsPerMonth
        {
            get
            {
                DistributeWorkers(out _, out int m, out _, out _);
                return m * 4;
            }
        }

        public int ProducedEnergyPerMonth
        {
            get
            {
                DistributeWorkers(out _, out _, out int g, out _);
                return g * 4;
            }
        }

        public int ProducedAlloysPerMonth
        {
            get
            {
                DistributeWorkers(out _, out _, out _, out int i);
                return i * 3;
            }
        }

        // ==================== СТРОИТЕЛЬСТВО ====================

        public bool CanBuildDistrict(DistrictType t)
        {
            if (BuiltDistricts >= MaxDistricts) return false;
            var eco = EconomyManager.Instance;
            if (eco == null) return false;
            return eco.Minerals >= DistrictInfo.MineralsCost(t) && eco.Alloys >= DistrictInfo.AlloysCost(t);
        }

        public bool TryBuildDistrict(DistrictType t)
        {
            if (!CanBuildDistrict(t)) return false;
            var eco = EconomyManager.Instance;
            if (!eco.TrySpend(0, DistrictInfo.MineralsCost(t), DistrictInfo.AlloysCost(t), 0)) return false;
            Districts.Add(new DistrictData(t));
            return true;
        }

        public float PopGrowthBaseSpeedPctPerMonth => 4f + (HousingCapacity - Population) * 0.4f;

        public int PlanetSize => MaxDistricts;

        public int HabitabilityPercent => Type switch
        {
            PlanetType.Continental => 82,
            PlanetType.Desert      => 54,
            PlanetType.Barren      => 18,
            PlanetType.Molten      => 0,
            PlanetType.GasGiant    => 0,
            _ => 30
        };

        public string ClassDisplayName => Type switch
        {
            PlanetType.Continental => "Континентальный мир",
            PlanetType.Desert      => "Пустынный мир",
            PlanetType.Molten      => "Расплавленный мир",
            PlanetType.GasGiant    => "Газовый гигант",
            PlanetType.Barren      => "Бесплодный мир",
            _ => "Неклассифицированный объект"
        };

        public bool CanColonize => HabitabilityPercent >= 40 && Population <= 0
            && Type != PlanetType.GasGiant && Type != PlanetType.Molten;

        public bool CanTerraform => Type == PlanetType.Barren || Type == PlanetType.Molten || Type == PlanetType.Desert;

        public bool TryFoundColony()
        {
            if (!CanColonize) return false;
            var eco = EconomyManager.Instance;
            if (eco == null || !eco.CanAfford(0f, 80f, 20f, 25f)) return false;
            if (!eco.TrySpend(0f, 80f, 20f, 25f)) return false;
            Population = 2;
            PopGrowthProgress = 0f;
            if (Districts.Count == 0)
            {
                Districts.Add(new DistrictData(DistrictType.Urban));
                Districts.Add(new DistrictData(DistrictType.Mining));
            }
            return true;
        }

        public bool TryStartTerraform()
        {
            if (!CanTerraform || TerraformingInProgress) return false;
            var eco = EconomyManager.Instance;
            if (eco == null || !eco.CanAfford(120f, 90f, 0f, 10f)) return false;
            if (!eco.TrySpend(120f, 90f, 0f, 10f)) return false;
            TerraformingInProgress = true;
            if (Type == PlanetType.Molten) Type = PlanetType.Barren;
            else if (Type == PlanetType.Barren) Type = PlanetType.Desert;
            else if (Type == PlanetType.Desert) Type = PlanetType.Continental;
            TerraformingInProgress = false;
            if (Type == PlanetType.Continental)
                PlanetColor = new Color(0.2f, 0.7f, 0.3f);
            else if (Type == PlanetType.Desert)
                PlanetColor = new Color(0.85f, 0.75f, 0.35f);
            else
                PlanetColor = new Color(0.5f, 0.5f, 0.55f);
            return true;
        }
    }
}