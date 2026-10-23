using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Решения империи для ИИ — те же, что у игрока (EmpireDecisions), с теми же ценами и условиями.
    /// Раз в месяц ИИ оценивает доступные решения: характер задаёт, что ему ближе (Ксарн — армия, Астрея — наука…),
    /// обстановка — что нужно сейчас (война, кризис гелия, полные склады, бунты, отставание флота).
    /// Платит только из излишков: запас титана на районы, сплавов на флот и влияния на форпосты не трогает.
    /// Новые решения — не чаще раза в два месяца, чтобы не тратить всё разом.
    /// </summary>
    public partial class AIEmpireManager
    {
        private const float DecisionThreshold = 1f;
        private const int DecisionGapMonths = 2;
        private int _decisionWait;

        /// <summary>Вместимость складов титана (растёт с колониями).</summary>
        public float MineralStorage => _report.MineralStorage;

        /// <summary>Разовый вклад науки в текущее исследование (решения «Научный грант», «Видение будущего»…).</summary>
        public void AddInstantScience(float points)
        {
            if (points <= 0f) return;
            if (CurrentTech == null) PickNextTech();
            if (CurrentTech != null) TechProgress += points;
        }

        private void ThinkDecisions()
        {
            if (_decisionWait > 0) { _decisionWait--; return; }
            if (YearsPlayed < 2 || EmpireDecisions.ActiveCountOf(OwnerId) >= EmpireDecisions.MaxActive) return;

            DecisionDef best = null;
            float bestScore = DecisionThreshold;
            foreach (var d in EmpireDecisions.All)
            {
                if (!EmpireDecisions.IsVisible(OwnerId, d) || EmpireDecisions.Blocker(OwnerId, d) != null) continue;
                if (!CanSpare(d)) continue;
                float score = DecisionScore(d) * Random.Range(0.8f, 1.2f);
                if (score > bestScore) { bestScore = score; best = d; }
            }
            if (best != null && EmpireDecisions.TryTake(OwnerId, best)) _decisionWait = DecisionGapMonths;
        }

        /// <summary>Хватит ли излишков: после оплаты остаются запасы на районы, флот и форпосты.</summary>
        private bool CanSpare(DecisionDef d)
        {
            int colonies = EmpireStats.Colonies(OwnerId);
            bool full = EmpireDecisions.StorageFull(OwnerId);
            float mineralReserve = full ? 150f : 200f + 60f * colonies;
            float alloyReserve = AtWarWithAnyone || WantsMoreFleet ? 250f : 150f;
            float energyReserve = d.Id == "eco_crisis" ? 0f : 120f;
            float influenceReserve = YearsPlayed < 25 ? 75f : 50f;   // форпосты важнее, пока галактика не поделена
            return Minerals - d.MineralsCostFor(OwnerId) >= mineralReserve
                && Alloys - d.AlloysCostFor(OwnerId) >= (d.Alloys > 0f ? alloyReserve : 0f)
                && EnergyCredits - d.EnergyCostFor(OwnerId) >= energyReserve
                && Influence - d.Influence >= (d.Influence > 0f ? influenceReserve : 0f);
        }

        /// <summary>Насколько характеру ИИ близко направление решений.</summary>
        private float CategoryWeight(DecisionCategory c)
        {
            switch (Personality)
            {
                case AIPersonality.Militarist: return c == DecisionCategory.Military ? 1.6f : c == DecisionCategory.Science ? 0.7f : c == DecisionCategory.Society ? 0.8f : c == DecisionCategory.Faction ? 1.4f : 1f;
                case AIPersonality.Scientific: return c == DecisionCategory.Science ? 1.6f : c == DecisionCategory.Military ? 0.8f : c == DecisionCategory.Faction ? 1.3f : 1f;
                case AIPersonality.Trader:     return c == DecisionCategory.Economy ? 1.6f : c == DecisionCategory.Military ? 0.8f : c == DecisionCategory.Science ? 0.9f : c == DecisionCategory.Faction ? 1.4f : 1f;
                case AIPersonality.Mystic:     return c == DecisionCategory.Science ? 1.4f : c == DecisionCategory.Society ? 1.3f : c == DecisionCategory.Military ? 0.8f : c == DecisionCategory.Faction ? 1.4f : 0.9f;
                case AIPersonality.Gardener:   return c == DecisionCategory.Society ? 1.5f : c == DecisionCategory.Military ? 0.9f : c == DecisionCategory.Faction ? 1.4f : 1f;
                case AIPersonality.Machine:    return c == DecisionCategory.Economy || c == DecisionCategory.Science ? 1.3f : c == DecisionCategory.Society ? 0.7f : c == DecisionCategory.Faction ? 1.4f : 1f;
            }
            return 1f;
        }

        /// <summary>Ценность решения сейчас: характер × нужда. Порог взятия — 1.</summary>
        private float DecisionScore(DecisionDef d)
        {
            float s = CategoryWeight(d.Category);
            float fill = EmpireDecisions.StorageFill(OwnerId);
            bool war = AtWarWithAnyone;
            int room = 0;
            foreach (var sys in EmpireStats.Systems)
                if (sys.OwnerId == OwnerId)
                    foreach (var p in sys.Planets) if (p.Population > 0 && p.Population < p.HousingCapacity) room++;

            if (d.Once) s *= 2.5f;   // великие проекты — навсегда

            switch (d.Id)
            {
                // Обмен титана — только из излишков: чем полнее склады, тем охотнее
                case "dec_smelt":
                    s *= fill < 0.4f ? 0.3f : fill < 0.7f ? 1f : 1.8f;
                    if (Alloys < 200f || war) s *= 1.4f;
                    break;
                case "dec_exchange":
                    s *= fill < 0.4f ? 0.3f : fill < 0.7f ? 1f : 1.8f;
                    if (MonthlyEnergyIncome < 5f) s *= 1.5f;
                    break;
                case "eco_selloff": s *= 2f; break;
                case "eco_crisis": s *= 3f; break;
                case "dec_bonds":
                case "aq_lobby":
                    s *= Influence < 80f ? 1.3f : 0.8f;
                    break;

                // Война
                case "mil_totaldef":
                case "eco_warbonds":
                case "xarn_vendetta":
                case "mil_mercs":
                case "nol_hack":
                    s *= LosingWar ? 2.4f : 1.8f;
                    break;
                case "dec_drills": s *= war ? 1.5f : 0.6f; break;
                case "dec_shipyards":
                case "xarn_mobilize":
                case "mil_armsrace":
                    s *= WantsMoreFleet || war ? 1.5f : 0.7f;
                    break;
                case "dec_drydock":
                {
                    int damaged = 0;
                    foreach (var f in OwnFleets(FleetType.Military))
                        if (f.Data.HullPoints < f.Data.MaxHullPoints * 0.7f) damaged++;
                    s *= damaged >= 3 ? 2f : 0.4f;
                    break;
                }

                // Общество и рост
                case "soc_bread": s *= 2.5f; break;
                case "dec_monument": s *= AvgStability() < 55f ? 1.5f : 0.8f; break;
                case "dec_colonial":
                case "ter_spring":
                case "ter_bloom":
                case "nol_hive":
                case "soc_refugees":
                case "ir_pilgrimage":
                    s *= room > 2 ? 1.2f : 0.5f;
                    break;
                case "nol_reflash": s *= 1.2f; break;

                // Разведка и ранняя игра
                case "dec_expedition":
                case "ter_spores":
                    s *= YearsPlayed < 30 ? 1.3f : 0.6f;
                    break;
                case "soc_landrush":
                case "sci_pioneers":
                    s *= YearsPlayed < 12 ? 1.3f : 0.8f;
                    break;
            }
            return s;
        }

        private float AvgStability()
        {
            float sum = 0f; int n = 0;
            foreach (var sys in EmpireStats.Systems)
                if (sys.OwnerId == OwnerId)
                    foreach (var p in sys.Planets) if (p.Population > 0) { sum += p.Stability; n++; }
            return n > 0 ? sum / n : 50f;
        }
    }
}
