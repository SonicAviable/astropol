using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Generation;

namespace StellarisClone.Core
{
    /// <summary>
    /// Сводная статистика империй (игрок — 0, империи ИИ — 1, 2, …): население, системы, колонии,
    /// очки партии. Используется наукой, победой, ИИ и интерфейсом.
    /// </summary>
    public static class EmpireStats
    {
        private static GalaxyGenerator s_gen;

        public static GalaxyGenerator Galaxy
        {
            get
            {
                if (s_gen == null) s_gen = Object.FindAnyObjectByType<GalaxyGenerator>();
                return s_gen;
            }
        }

        public static IReadOnlyList<StarSystem> Systems
        {
            get
            {
                var g = Galaxy;
                return g != null ? g.Systems : (IReadOnlyList<StarSystem>)System.Array.Empty<StarSystem>();
            }
        }

        public static StarSystem GetSystem(int id)
        {
            var list = Systems;
            return id >= 0 && id < list.Count ? list[id] : null;
        }

        public static int Population(int owner)
        {
            int pop = 0;
            foreach (var s in Systems)
            {
                if (s.OwnerId != owner) continue;
                foreach (var p in s.Planets) if (p.Population > 0) pop += p.Population;
            }
            return pop;
        }

        /// <summary>Научная сила населения: жители с учётом черт видов (умные, роботы) и стабильности планет.</summary>
        public static float ResearchPops(int owner)
        {
            float sum = 0f;
            foreach (var s in Systems)
            {
                if (s.OwnerId != owner) continue;
                foreach (var p in s.Planets) if (p.Population > 0) sum += p.ResearchPops;
            }
            return sum;
        }

        public static int Colonies(int owner)
        {
            int n = 0;
            foreach (var s in Systems)
            {
                if (s.OwnerId != owner) continue;
                foreach (var p in s.Planets) if (p.Population > 0) n++;
            }
            return n;
        }

        /// <summary>Системы под контролем (с форпостом).</summary>
        public static int SystemCount(int owner)
        {
            int n = 0;
            foreach (var s in Systems) if (s.OwnerId == owner && s.HasStarbase) n++;
            return n;
        }

        public static int TechCount(int owner)
        {
            if (owner == 0) return TechnologyManager.Instance != null ? TechnologyManager.Instance.ResearchedCount : 0;
            var ai = AIEmpireManager.For(owner);
            return ai != null ? ai.ResearchedCount : 0;
        }

        public static float MilitaryPower(int owner)
            => FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(owner) : 0f;

        // ==================== ОЧКИ ПАРТИИ ====================

        public const int ScorePerSystem = 10;
        public const int ScorePerPop = 4;
        public const int ScorePerTech = 15;
        public const float ScorePerPower = 1f / 50f;

        public struct ScoreBreakdown
        {
            public int Systems, Population, Techs, Military, Wonders;
            public int Total => Systems + Population + Techs + Military + Wonders;
        }

        public static ScoreBreakdown Score(int owner) => new ScoreBreakdown
        {
            Systems = SystemCount(owner) * ScorePerSystem,
            Population = Population(owner) * ScorePerPop,
            Techs = TechCount(owner) * ScorePerTech,
            Military = Mathf.RoundToInt(MilitaryPower(owner) * ScorePerPower),
            Wonders = WonderManager.ScoreOf(owner)
        };

        // ==================== ЭКОНОМИКА И ГОЛОСА (пути к победе) ====================

        /// <summary>
        /// Валовой продукт империи за месяц — одинаково для игрока и ИИ: добыча и производство районов
        /// (сплавы весомее), добывающие комплексы, услуги населения и доход форпостов.
        /// </summary>
        public static float Gdp(int owner)
        {
            float g = 0f;
            foreach (var s in Systems)
            {
                if (s.OwnerId != owner) continue;
                if (s.HasStarbase) g += 1f;
                foreach (var p in s.Planets)
                {
                    g += p.ProducedMineralsPerMonth + p.ProducedEnergyPerMonth + p.ProducedAlloysPerMonth * 1.5f;
                    if (p.HasMiningStation) g += p.MineralDeposit + p.EnergyDeposit;
                    g += p.Population * 0.5f;
                }
            }
            return g;
        }

        /// <summary>Доля империи в валовом продукте галактики (империи, без угроз), 0..1.</summary>
        public static float GdpShare(int owner)
        {
            float total = Gdp(0);
            foreach (var ai in AIEmpireManager.All) if (!ai.IsEliminated) total += Gdp(ai.OwnerId);
            return total > 0.01f ? Gdp(owner) / total : 0f;
        }

        /// <summary>Голоса империи в Галактическом совете: 1 + население / 8 + системы / 4.</summary>
        public static int Votes(int owner) => 1 + Population(owner) / 8 + SystemCount(owner) / 4;

        /// <summary>Есть ли у владельца система, соседняя с системой другого владельца.</summary>
        public static int SharedBorderCount(int a, int b)
        {
            int n = 0;
            var list = Systems;
            foreach (var s in list)
            {
                if (s.OwnerId != a) continue;
                foreach (int id in s.ConnectedSystemIds)
                {
                    if (id >= 0 && id < list.Count && list[id].OwnerId == b) { n++; break; }
                }
            }
            return n;
        }
    }
}
