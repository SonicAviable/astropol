using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Переселение внутри империи (раз в месяц, правила одни для игрока и ИИ).
    /// Жители уезжают оттуда, где им плохо: нет работы, не хватает жилья, забастовки и бунт.
    /// Едут туда, где есть и свободное жильё, и свободное место работы: охотнее — в родной климат,
    /// в соседний мир той же системы и в процветающие колонии. Роботы приписаны к своей планете и не переезжают.
    /// С одной колонии уезжает 1 житель в месяц (2 — если лишних много); переселенцы сохраняют свою интеграцию.
    /// </summary>
    public static class Migration
    {
        public const float MoveChance = 0.6f;

        public static void MonthlyTick(IReadOnlyList<StarSystem> systems)
        {
            if (systems == null) return;
            bool newYear = TimeManager.Instance != null && TimeManager.Instance.Month == 1;
            var byOwner = new Dictionary<int, List<PlanetData>>();
            foreach (var sys in systems)
            {
                if (sys.OwnerId < 0) continue;
                foreach (var p in sys.Planets)
                {
                    if (p.Population <= 0) continue;
                    if (newYear) { p.MigrantsIn = 0; p.MigrantsOut = 0; }
                    if (!byOwner.TryGetValue(sys.OwnerId, out var list)) byOwner[sys.OwnerId] = list = new List<PlanetData>();
                    list.Add(p);
                }
            }
            foreach (var kv in byOwner) if (kv.Value.Count > 1) Run(kv.Value);
        }

        private static bool Troubled(PlanetData p) => p.Tier == StabilityTier.Riot || p.Tier == StabilityTier.Strike;

        /// <summary>Сколько жителей хотят уехать: безработные, лишние сверх жилья, +1 при забастовках и бунте.</summary>
        public static int Push(PlanetData p)
            => p.IdlePops + Mathf.Max(0, p.Population - p.HousingCapacity) + (Troubled(p) ? 1 : 0);

        /// <summary>Сколько переселенцев колония примет: есть и жильё, и работа; в беспокойные миры не едут.</summary>
        public static int Pull(PlanetData p)
        {
            if (Troubled(p)) return 0;
            int housing = p.HousingCapacity - p.Population;
            int jobs = p.WorkerSlots - p.Population;
            return Mathf.Max(0, Mathf.Min(housing, jobs));
        }

        private static void Run(List<PlanetData> colonies)
        {
            var sources = new List<(PlanetData p, int push)>();
            foreach (var p in colonies)
            {
                int push = Push(p);
                if (push > 0 && p.Population > 1) sources.Add((p, push));
            }
            if (sources.Count == 0) return;
            sources.Sort((a, b) => b.push.CompareTo(a.push));

            foreach (var (src, push) in sources)
            {
                int moves = push >= 4 ? 2 : 1;
                for (int k = 0; k < moves; k++)
                {
                    if (Random.value > MoveChance || src.Population <= 1) continue;
                    var sp = Leaver(src);
                    if (sp == null) break;
                    var dest = BestDestination(src, sp, colonies);
                    if (dest == null) break;
                    float integration = src.IntegrationOf(sp.Id);
                    if (src.RemovePops(sp.Id, 1) != 1) break;
                    dest.AddPops(sp.Id, 1, integration);
                    src.MigrantsOut++;
                    dest.MigrantsIn++;
                }
            }
        }

        /// <summary>Кто уезжает: сначала те, кому чужой климат, затем самая большая община; роботы остаются.</summary>
        private static Species Leaver(PlanetData p)
        {
            Species best = null;
            int bestScore = int.MinValue;
            foreach (var g in p.Pops)
            {
                var sp = SpeciesRegistry.Get(g.Species);
                if (sp == null || sp.Robot || g.Count <= 0) continue;
                int score = g.Count + (sp.AtHome(p.Type) ? 0 : 100);
                if (score > bestScore) { bestScore = score; best = sp; }
            }
            return best;
        }

        private static PlanetData BestDestination(PlanetData src, Species sp, List<PlanetData> colonies)
        {
            PlanetData best = null;
            float bestScore = 0f;
            foreach (var d in colonies)
            {
                if (d == src) continue;
                int pull = Pull(d);
                if (pull <= 0) continue;
                float score = pull * 1.5f + (sp.AtHome(d.Type) ? 10f : 0f)
                            + (d.ParentSystem != null && d.ParentSystem == src.ParentSystem ? 4f : 0f)
                            + (d.Tier == StabilityTier.Prosperity ? 2f : 0f);
                if (score > bestScore) { bestScore = score; best = d; }
            }
            return best;
        }

        /// <summary>
        /// Кого отправить колонистами на новый мир владельца: вид из его колоний, которому подходит климат
        /// (свои и интегрированные — охотнее), иначе вид-основатель. integration — интеграция выбранных колонистов.
        /// </summary>
        public static int ColonistSpecies(int owner, PlanetType type, out float integration)
        {
            int founder = SpeciesRegistry.FounderOf(owner);
            int best = founder;
            float bestScore = float.MinValue;
            integration = 1f;
            foreach (var sys in EmpireStats.Systems)
            {
                if (sys.OwnerId != owner) continue;
                foreach (var p in sys.Planets)
                {
                    if (p.Population <= 0) continue;
                    foreach (var g in p.Pops)
                    {
                        var sp = SpeciesRegistry.Get(g.Species);
                        if (sp == null || sp.Robot || g.Count <= 0) continue;
                        bool kin = SpeciesRegistry.Related(sp.Id, founder);
                        float score = (sp.AtHome(type) ? 10f : 0f) + (kin ? 2f : 2f * g.Integration) + Mathf.Min(3f, g.Count * 0.2f);
                        if (score > bestScore) { bestScore = score; best = sp.Id; integration = kin ? 1f : g.Integration; }
                    }
                }
            }
            return best;
        }
    }
}
