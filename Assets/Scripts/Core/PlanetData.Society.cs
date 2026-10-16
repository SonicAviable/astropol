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
                AddToGroup(GrowthSpecies(), Population - sum);
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

        private void AddToGroup(int species, int n)
        {
            if (n <= 0) return;
            foreach (var g in Pops) if (g.Species == species) { g.Count += n; return; }
            Pops.Add(new PopGroup(species, n));
        }

        /// <summary>Добавить жителей конкретного вида (беженцы, роботы, переселенцы).</summary>
        public void AddPops(int species, int n)
        {
            if (n <= 0 || SpeciesRegistry.Get(species) == null) return;
            SyncPops();
            AddToGroup(species, n);
            Population += n;
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
            int moved = RemovePops(from, n);
            if (moved > 0) AddPops(to, moved);
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

        /// <summary>Самый многочисленный органический вид планеты (-1 — органиков нет).</summary>
        public int DominantOrganic
        {
            get
            {
                SyncPops();
                PopGroup best = null;
                foreach (var g in Pops)
                {
                    var s = SpeciesRegistry.Get(g.Species);
                    if (s == null || s.Robot) continue;
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

        public int OrganicPops => CountOf(s => !s.Robot);
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
            if (eco == null || !eco.TrySpend(RobotEnergyCost, 0f, RobotAlloysCost, 0f)) return false;
            Builds.EnqueuePlanetJob(JobKind.Robot, 0, this, DistrictType.Urban, RobotEnergyCost, 0f, RobotAlloysCost, 0f);
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

        /// <summary>Итоговый множитель производства планеты: черты видов × стабильность.</summary>
        public float OutputMult(string resource) => SpeciesOutputMult(resource) * PlanetStability.OutputMult(Tier);

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
