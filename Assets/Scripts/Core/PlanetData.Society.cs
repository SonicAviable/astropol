using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Общество планеты: из каких видов состоят жители и насколько они довольны.
    /// Население по-прежнему хранится числом (Population) — состав по видам подстраивается под него лениво:
    /// новые жители — от самого многочисленного органического вида (или основателя империи),
    /// убыль — из самой большой группы. Поэтому любой код, который просто меняет Population, остаётся корректным.
    ///
    /// Жители работают по способностям: на каждое место встаёт вид, у которого черты подходят лучше
    /// (инженеры — на заводы, умные — в лаборатории, роботы — в шахты), и производство района считается
    /// по тем, кто в нём работает. Чужие виды со временем интегрируются (Integration), переселяются туда,
    /// где есть работа и жильё (Migration), а новые колонии заселяет вид, которому подходит климат.
    /// </summary>
    public partial class PlanetData
    {
        /// <summary>Жители по видам (сумма = Population после SyncPops).</summary>
        public List<PopGroup> Pops = new List<PopGroup>();

        /// <summary>Стабильность 0–100: текущая (догоняет целевую на несколько пунктов в месяц).</summary>
        public float Stability = 50f;
        /// <summary>Недовольство после завоевания: вычитается из стабильности и тает со временем.</summary>
        public float Unrest;
        /// <summary>Сколько ещё месяцев идёт кампания пропаганды.</summary>
        public int PropagandaMonths;
        /// <summary>Сколько ещё месяцев действует военное положение после подавления бунта.</summary>
        public int MartialLawMonths;
        /// <summary>Сколько месяцев подряд на планете бунт (6 — система отделяется).</summary>
        public int RiotMonths;
        /// <summary>Переселенцы за текущий год: приехали и уехали (обнуляется в январе).</summary>
        public int MigrantsIn, MigrantsOut;

        public StabilityTier Tier => PlanetStability.TierOf(Stability);

        // ==================== СОСТАВ НАСЕЛЕНИЯ ====================

        /// <summary>Привести состав по видам в соответствие с Population.</summary>
        public void SyncPops()
        {
            Pops ??= new List<PopGroup>();
            int sum = 0;
            for (int i = Pops.Count - 1; i >= 0; i--)
            {
                var g = Pops[i];
                if (g == null || g.Count <= 0 || SpeciesRegistry.Get(g.Species) == null) { Pops.RemoveAt(i); continue; }
                sum += g.Count;
            }
            if (Population < 0) Population = 0;
            if (sum == Population) return;
            if (sum < Population)
            {
                // До старта партии фракции империй ИИ ещё не назначены — состав определим позже
                if (Pops.Count == 0 && !StellarisClone.Rendering.UIManager.IsGameStarted) return;
                AddToGroup(GrowthSpecies(), Population - sum, null);   // родившиеся — свои для своей общины
                return;
            }

            int excess = sum - Population;
            while (excess > 0 && Pops.Count > 0)
            {
                var big = Pops[0];
                foreach (var g in Pops) if (g.Count > big.Count) big = g;
                big.Count--; excess--;
                if (big.Count <= 0) Pops.Remove(big);
            }
        }

        /// <summary>
        /// Добавить жителей в группу вида. integration — их интеграция (null — как у общины, в которую они попадают:
        /// так растёт население); новая группа без указания интеграции начинает с нуля.
        /// </summary>
        private void AddToGroup(int species, int n, float? integration)
        {
            if (n <= 0) return;
            foreach (var g in Pops)
                if (g.Species == species)
                {
                    if (integration.HasValue) g.Integration = (g.Integration * g.Count + integration.Value * n) / (g.Count + n);
                    g.Count += n;
                    return;
                }
            Pops.Add(new PopGroup(species, n) { Integration = Mathf.Clamp01(integration ?? 0f) });
        }

        /// <summary>Добавить жителей конкретного вида (беженцы, роботы, переселенцы); integration — их интеграция в империи.</summary>
        public void AddPops(int species, int n, float integration = 0f)
        {
            if (n <= 0 || SpeciesRegistry.Get(species) == null) return;
            SyncPops();
            AddToGroup(species, n, integration);
            Population += n;
        }

        /// <summary>Интеграция группы вида на планете (0 — группы нет).</summary>
        public float IntegrationOf(int species)
        {
            foreach (var g in Pops) if (g.Species == species) return g.Integration;
            return 0f;
        }

        /// <summary>Убрать жителей конкретного вида (сколько удалось).</summary>
        public int RemovePops(int species, int n)
        {
            SyncPops();
            int removed = 0;
            for (int i = Pops.Count - 1; i >= 0 && removed < n; i--)
            {
                if (Pops[i].Species != species) continue;
                int k = Mathf.Min(n - removed, Pops[i].Count);
                Pops[i].Count -= k; removed += k;
                if (Pops[i].Count <= 0) Pops.RemoveAt(i);
            }
            Population -= removed;
            return removed;
        }

        /// <summary>Перевести жителей одного вида в другой (гибриды, генная адаптация).</summary>
        public int ConvertPops(int from, int to, int n)
        {
            if (SpeciesRegistry.Get(to) == null) return 0;
            float integ = IntegrationOf(from);
            int moved = RemovePops(from, n);
            if (moved > 0) AddPops(to, moved, integ);
            return moved;
        }

        /// <summary>Какой вид даёт прирост: самая многочисленная органическая группа, иначе основатель империи.</summary>
        public int GrowthSpecies()
        {
            PopGroup best = null;
            foreach (var g in Pops)
            {
                var s = SpeciesRegistry.Get(g.Species);
                if (s == null || s.Robot) continue;
                if (best == null || g.Count > best.Count) best = g;
            }
            return best != null ? best.Species : SpeciesRegistry.FounderOf(OwnerId);
        }

        /// <summary>Самый многочисленный органический вид планеты (-1 — органиков нет; роботы и синтеты не в счёт).</summary>
        public int DominantOrganic
        {
            get
            {
                SyncPops();
                PopGroup best = null;
                foreach (var g in Pops)
                {
                    var s = SpeciesRegistry.Get(g.Species);
                    if (s == null || s.Machine) continue;
                    if (best == null || g.Count > best.Count) best = g;
                }
                return best != null ? best.Species : -1;
            }
        }

        public int CountOf(Func<Species, bool> pred)
        {
            SyncPops();
            int n = 0;
            foreach (var g in Pops)
            {
                var s = SpeciesRegistry.Get(g.Species);
                if (s != null && pred(s)) n += g.Count;
            }
            return n;
        }

        public int OrganicPops => CountOf(s => !s.Machine);
        public int RobotPops => CountOf(s => s.Robot);

        // ==================== ПРОЕКТЫ: РОБОТЫ И ГЕННАЯ ИНЖЕНЕРИЯ ====================

        public const float RobotAlloysCost = 40f, RobotEnergyCost = 30f;
        public const float GeneEnergyCost = 120f, GeneInfluenceCost = 40f;

        private bool HasJob(JobKind kind)
        {
            if (Builds == null) return false;
            foreach (var j in Builds.QueueFor(this)) if (j.Kind == kind) return true;
            return false;
        }

        /// <summary>Есть ли место для ещё одного жителя с учётом роботов, уже стоящих в очереди.</summary>
        public bool HasRoomForRobot
        {
            get
            {
                int queued = 0;
                if (Builds != null) foreach (var j in Builds.QueueFor(this)) if (j.Kind == JobKind.Robot) queued++;
                return Population + queued < HousingCapacity;
            }
        }

        public bool TryBuildRobot()
        {
            if (!IsPlayerOwned || Population <= 0 || Builds == null || !HasRoomForRobot) return false;
            if (Builds.QueueFor(this).Count >= ConstructionManager.MaxPlanetQueue) return false;
            var eco = EconomyManager.Instance;
            float energy = FactionTraits.RobotEnergy(OwnerId), alloys = FactionTraits.RobotAlloys(OwnerId);
            if (eco == null || !eco.TrySpend(energy, 0f, alloys, 0f)) return false;
            Builds.EnqueuePlanetJob(JobKind.Robot, 0, this, DistrictType.Urban, energy, 0f, alloys, 0f);
            return true;
        }

        /// <summary>Вид, который изменит генная адаптация (самый многочисленный органический), или null.</summary>
        public Species GeneTarget
        {
            get
            {
                var s = SpeciesRegistry.Get(DominantOrganic);
                return SpeciesRegistry.CanAdapt(s, Type) ? s : null;
            }
        }

        public bool TryGeneAdapt()
        {
            if (!IsPlayerOwned || Population <= 0 || Builds == null || GeneTarget == null || HasJob(JobKind.GeneAdapt)) return false;
            if (Builds.QueueFor(this).Count >= ConstructionManager.MaxPlanetQueue) return false;
            var eco = EconomyManager.Instance;
            if (eco == null || !eco.TrySpend(GeneEnergyCost, 0f, 0f, GeneInfluenceCost)) return false;
            Builds.EnqueuePlanetJob(JobKind.GeneAdapt, 0, this, DistrictType.Urban, GeneEnergyCost, 0f, 0f, GeneInfluenceCost);
            return true;
        }

        // ==================== ВЛИЯНИЕ ВИДОВ НА ЭКОНОМИКУ ====================

        /// <summary>Средний множитель черт по ресурсу ("minerals" / "energy" / "alloys") с учётом состава.</summary>
        public float SpeciesOutputMult(string resource)
        {
            SyncPops();
            if (Population <= 0) return 1f;
            float sum = 0f; int n = 0;
            foreach (var g in Pops)
            {
                var s = SpeciesRegistry.Get(g.Species);
                if (s == null) continue;
                sum += s.OutputMult(resource) * g.Count; n += g.Count;
            }
            return n > 0 ? sum / n : 1f;
        }

        /// <summary>Итоговый множитель производства планеты: черты тех, кто работает в районах этого ресурса, × стабильность.</summary>
        public float OutputMult(string resource)
        {
            var t = resource == "minerals" ? DistrictType.Mining : resource == "energy" ? DistrictType.Generator
                  : resource == "alloys" ? DistrictType.Industrial : (DistrictType?)null;
            float species = t.HasValue ? JobFit(out _)[(int)t.Value] : SpeciesOutputMult(resource);
            return species * PlanetStability.OutputMult(Tier);
        }

        /// <summary>Чем район «нагружает» черты вида: ресурс производства или наука у служащих.</summary>
        public static float FitFor(Species s, DistrictType t) => t switch
        {
            DistrictType.Mining => s.OutputMult("minerals"),
            DistrictType.Generator => s.OutputMult("energy"),
            DistrictType.Industrial => s.OutputMult("alloys"),
            _ => s.ResearchMult
        };

        /// <summary>
        /// Кто где работает: места районов (Distribute) занимают виды по способностям — сначала там, где у вида
        /// самое большое преимущество перед его же средним (сравнительное преимущество), потом по силе черты.
        /// Возвращает средний множитель черт работников каждого вида районов (индекс DistrictType; без работников — 1);
        /// assigned[группа, вид района] — сколько жителей группы (в порядке Pops) там работает.
        /// </summary>
        public float[] JobFit(out int[,] assigned)
        {
            SyncPops();
            var w = Distribute();
            var fit = new float[] { 1f, 1f, 1f, 1f };
            assigned = new int[Pops.Count, 4];
            if (Pops.Count == 0) return fit;

            var pairs = new List<(int g, int t, float f, float adv)>();
            for (int gi = 0; gi < Pops.Count; gi++)
            {
                var sp = SpeciesRegistry.Get(Pops[gi].Species);
                if (sp == null) continue;
                float mean = 0f; int jobs = 0;
                for (int t = 0; t < 4; t++) if (w[t] > 0) { mean += FitFor(sp, (DistrictType)t); jobs++; }
                if (jobs == 0) continue;
                mean /= jobs;
                for (int t = 0; t < 4; t++)
                    if (w[t] > 0) { float f = FitFor(sp, (DistrictType)t); pairs.Add((gi, t, f, f - mean)); }
            }
            pairs.Sort((a, b) => b.adv != a.adv ? b.adv.CompareTo(a.adv) : b.f.CompareTo(a.f));

            var left = new int[Pops.Count];
            for (int gi = 0; gi < Pops.Count; gi++) left[gi] = Pops[gi].Count;
            var slots = (int[])w.Clone();
            var sum = new float[4];
            var cnt = new int[4];
            foreach (var p in pairs)
            {
                int k = Mathf.Min(left[p.g], slots[p.t]);
                if (k <= 0) continue;
                left[p.g] -= k; slots[p.t] -= k;
                assigned[p.g, p.t] += k;
                sum[p.t] += p.f * k; cnt[p.t] += k;
            }
            for (int t = 0; t < 4; t++) if (cnt[t] > 0) fit[t] = sum[t] / cnt[t];
            return fit;
        }

        /// <summary>Наука от жителей планеты (в «жителях»: умные дают больше, роботы — ноль; бунт срывает работу лабораторий).</summary>
        public float ResearchPops
        {
            get
            {
                if (Population <= 0) return 0f;
                SyncPops();
                float sum = 0f;
                foreach (var g in Pops)
                {
                    var s = SpeciesRegistry.Get(g.Species);
                    if (s != null) sum += s.ResearchMult * g.Count;
                }
                // Служащие городских районов дают ещё по единице науки — с чертами тех, кто на этих местах работает
                sum += WorkedCount(DistrictType.Urban) * JobFit(out _)[(int)DistrictType.Urban];
                return sum * PlanetStability.OutputMult(Tier);
            }
        }

        /// <summary>Множитель роста от черт органических жителей (0 — органиков нет, роботы не размножаются).</summary>
        public float SpeciesGrowthMult
        {
            get
            {
                SyncPops();
                float sum = 0f; int n = 0;
                foreach (var g in Pops)
                {
                    var s = SpeciesRegistry.Get(g.Species);
                    if (s == null || s.Robot) continue;
                    sum += s.GrowthMult * g.Count; n += g.Count;
                }
                return n > 0 ? sum / n : 0f;
            }
        }
    }
}
