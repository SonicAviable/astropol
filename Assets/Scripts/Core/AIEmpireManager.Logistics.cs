using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// ИИ и снабжение (<see cref="SupplyNetwork"/>): строит депо там, где груз застревает или заводы стоят без
    /// сырья, и транзитные узлы на забитых коридорах. Одна стройка логистики за раз, только из излишков.
    /// Цели войны: хабы и узлы, через которые идёт снабжение врага, ценнее (<see cref="SupplyNetwork.StrategicValue"/>).
    /// </summary>
    public partial class AIEmpireManager
    {
        /// <summary>Не строить ради мелочи: столько потерь в месяц (с учётом склада) — повод для депо.</summary>
        private const float DepotWorthScore = 20f;
        private const float RelayWorthScore = 12f;

        private void ThinkLogistics()
        {
            var cm = ConstructionManager.Instance;
            if (cm == null) return;
            foreach (var j in cm.Jobs)
                if (j != null && j.Owner == OwnerId && (j.Kind == JobKind.SupplyDepot || j.Kind == JobKind.SupplyRelay)) return;
            var net = SupplyNetwork.Get(OwnerId);
            if (net == null || net.Systems.Count < 3) return;

            bool threatened = AtWarWithAnyone;
            StarSystem depotAt = null, relayAt = null;
            float depotScore = 0f, relayScore = 0f;

            // Депо: там, где застревает груз (и вокруг), где заводам не хватает сырья, где копится склад
            int depots = SupplyState.CountOwned(OwnerId, SupplyState.Building.Depot);
            if (depots < 1 + _report.Colonies / 5)
            {
                foreach (var f in net.Systems.Values)
                {
                    if (f.State == SupplyNetwork.NodeState.Hub || f.State == SupplyNetwork.NodeState.Blockaded) continue;
                    var sys = EmpireStats.GetSystem(f.SystemId);
                    if (SupplyState.Blocker(sys, OwnerId, SupplyState.Building.Depot) != null) continue;
                    float score = LogisticsLoss(f) + SupplyState.ColoniesIn(sys) * 4f;
                    if (f.State == SupplyNetwork.NodeState.CutOff) score += 15f;
                    foreach (int nb in sys.ConnectedSystemIds)
                    {
                        var nf = net.Of(nb);
                        if (nf != null && nf.State != SupplyNetwork.NodeState.Hub) score += LogisticsLoss(nf) * 0.5f;
                    }
                    if (score > depotScore) { depotScore = score; depotAt = sys; }
                }
            }

            // Транзитный узел: на своём конце забитого коридора, через который идёт больше всего груза
            foreach (var l in net.Lanes.Values)
            {
                if (l.Capacity <= 0f || l.Load < 0.95f) continue;
                for (int k = 0; k < 2; k++)
                {
                    var sys = EmpireStats.GetSystem(k == 0 ? l.A : l.B);
                    if (sys == null || sys.OwnerId != OwnerId) continue;
                    if (SupplyState.Blocker(sys, OwnerId, SupplyState.Building.Relay) != null) continue;
                    var f = net.Of(sys.Id);
                    float score = l.Flow * 0.4f + (f != null ? f.Through * 0.2f + LogisticsLoss(f) : 0f);
                    if (score > relayScore) { relayScore = score; relayAt = sys; }
                }
            }

            StarSystem at = null;
            var kind = SupplyState.Building.Depot;
            if (depotAt != null && depotScore >= DepotWorthScore && depotScore >= relayScore * 1.3f) at = depotAt;
            else if (relayAt != null && relayScore >= RelayWorthScore) { at = relayAt; kind = SupplyState.Building.Relay; }
            else if (depotAt != null && depotScore >= DepotWorthScore) at = depotAt;
            if (at == null) return;

            float m = SupplyState.MineralsOf(kind), a = SupplyState.AlloysOf(kind);
            float alloyReserve = threatened ? 120f : 60f;
            if (Minerals < m + 80f || Alloys < a + alloyReserve) return;
            if (MonthlyEnergyIncome < SupplyState.UpkeepOf(kind) + 3f) return;
            Minerals -= m;
            Alloys -= a;
            SupplyState.Enqueue(cm, OwnerId, at.Id, kind);
        }

        /// <summary>Сколько груза в месяц система теряет из-за логистики (с поправкой на склад).</summary>
        private static float LogisticsLoss(SupplyNetwork.SystemFlow f)
        {
            var st = SupplyState.StockOf(f.SystemId);
            return f.StuckMinerals + f.StuckAlloys + f.Unfed * 1.5f + f.LostAlloys + st.Total / 12f;
        }
    }
}
