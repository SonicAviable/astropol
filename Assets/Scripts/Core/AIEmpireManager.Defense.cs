using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Оборона ИИ сверх флота:
    ///   • модернизация — корабли старых проектов перевооружаются под новые технологии (как «Модернизировать все» у игрока);
    ///   • укрепление баз — столица и пограничные колонии получают уровни укреплений, на войне — быстрее.
    /// Оба решения идут после постройки кораблей и тратят только излишки сплавов.
    /// </summary>
    public partial class AIEmpireManager
    {
        /// <summary>Модернизация окупается, только если корабль станет заметно сильнее.</summary>
        private const float RetrofitMinGain = 0.12f;
        private const int RetrofitsPerThink = 2;

        private void ThinkRetrofit()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            float reserve = LosingWar ? 30f : 100f;
            int done = 0;
            foreach (var f in OwnFleets(FleetType.Military))
            {
                if (done >= RetrofitsPerThink) break;
                var d = f.Data;
                if (d.InCombat || d.State != FleetState.Orbiting) continue;
                var sys = EmpireStats.GetSystem(d.CurrentSystemId);
                if (sys == null || sys.OwnerId != OwnerId || !sys.HasStarbase) continue;   // перевооружение — только у своей базы

                var design = GetDesign(d.HullClass);
                if (design == null || design.Id == d.DesignId) continue;
                float gain = ShipDesignManager.DesignPower(design) / Mathf.Max(1f, d.MilitaryPower) - 1f;
                if (gain < RetrofitMinGain) continue;
                float cost = Mathf.Max(ShipDesignManager.MinRetrofitCost, design.AlloyCost - d.DesignAlloyCost);
                if (Alloys < cost + reserve) break;
                Alloys -= cost;
                d.ApplyDesign(design);
                done++;
            }
        }

        // ==================== УКРЕПЛЕНИЕ БАЗ ====================

        /// <summary>Какой уровень укреплений ИИ хочет в системе: столица — больше, пограничные колонии — меньше, на войне — выше.</summary>
        private int WantedFortification(StarSystem sys, bool threatened)
        {
            if (sys == null || sys.OwnerId != OwnerId || !sys.HasStarbase) return 0;
            int colonies = 0;
            foreach (var p in sys.Planets) if (p.Population > 0) colonies++;
            bool capital = sys.Id == CapitalSystemId;
            bool border = false;
            foreach (int n in sys.ConnectedSystemIds)
            {
                var ns = EmpireStats.GetSystem(n);
                if (ns != null && ns.OwnerId >= 0 && ns.OwnerId != OwnerId && !Threats.IsThreat(ns.OwnerId)) { border = true; break; }
            }
            int years = YearsPlayed;
            int want = 0;
            if (capital) want = years >= 40 ? 3 : years >= 22 ? 2 : years >= 8 ? 1 : 0;
            else if (border && colonies > 0) want = years >= 30 ? 2 : years >= 12 ? 1 : 0;
            else if (border && Landmarks.AIValue(sys) > 0f) want = years >= 15 ? 1 : 0;   // узлы силы на границе
            if (threatened && (capital || border) && want < Fortifications.MaxLevel) want++;
            return Mathf.Min(want, Fortifications.MaxLevel);
        }

        private void ThinkFortify()
        {
            var cm = ConstructionManager.Instance;
            if (cm == null) return;
            // Одна стройка укреплений за раз — флот и районы важнее
            foreach (var j in cm.Jobs) if (j != null && j.Kind == JobKind.Fortify && j.Owner == OwnerId) return;
            if (MonthlyEnergyIncome < Fortifications.UpkeepPerLevel + 4f) return;

            bool threatened = AtWarWithAnyone || RivalPowerForBuild() > EmpireStats.MilitaryPower(OwnerId) * 1.2f;
            StarSystem best = null;
            float bestScore = 0f;
            foreach (var sys in EmpireStats.Systems)
            {
                if (sys.OwnerId != OwnerId) continue;
                int gap = WantedFortification(sys, threatened) - sys.Fortification;
                if (gap <= 0 || Fortifications.Blocker(sys, OwnerId) != null) continue;
                float score = gap * 10f + (sys.Id == CapitalSystemId ? 15f : 0f);
                foreach (var p in sys.Planets) if (p.Population > 0) score += 3f + p.Population * 0.3f;
                if (score > bestScore) { bestScore = score; best = sys; }
            }
            if (best == null) return;

            int to = best.Fortification + 1;
            float m = Fortifications.Minerals(to), a = Fortifications.Alloys(to);
            // Сплавы — только сверх запаса на корабли (в войне запас меньше: крепость тоже оборона)
            float alloyReserve = threatened ? 60f : 150f;
            if (Minerals < m + 60f || Alloys < a + alloyReserve) return;
            Minerals -= m;
            Alloys -= a;
            cm.EnqueueSystemJob(JobKind.Fortify, OwnerId, best.Id, Fortifications.Days(to), 0f, m, a, 0f);
        }
    }
}
