using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Generation;

namespace StellarisClone.Core
{
    /// <summary>
    /// Сводная статистика империй (игрок — 0, ИИ — AIOwnerId): население, системы, колонии,
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
            var ai = AIEmpireManager.Instance;
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
            public int Systems, Population, Techs, Military;
            public int Total => Systems + Population + Techs + Military;
        }

        public static ScoreBreakdown Score(int owner) => new ScoreBreakdown
        {
            Systems = SystemCount(owner) * ScorePerSystem,
            Population = Population(owner) * ScorePerPop,
            Techs = TechCount(owner) * ScorePerTech,
            Military = Mathf.RoundToInt(MilitaryPower(owner) * ScorePerPower)
        };

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
