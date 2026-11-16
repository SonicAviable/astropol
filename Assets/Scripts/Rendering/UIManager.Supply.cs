using System.Text;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Секция «Снабжение» в панели своей системы (SupplyNetwork): связь с хабом, склад, груз за месяц,
    /// загрузка коридоров и стройки логистики — депо снабжения и транзитный узел.
    /// </summary>
    public partial class UIManager
    {
        private static readonly Color SupplyOk = new Color(0.45f, 0.95f, 0.45f);
        private static readonly Color SupplyWarn = new Color(1.00f, 0.72f, 0.30f);
        private static readonly Color SupplyBad = new Color(1.00f, 0.38f, 0.32f);
        private static readonly Color SupplyMineralsCol = new Color(0.30f, 0.94f, 0.55f);
        private static readonly Color SupplyAlloysCol = new Color(0.95f, 0.62f, 0.36f);

        private void BuildSupplyCards(StarSystem system)
        {
            if (system == null || system.OwnerId != 0) return;
            var net = SupplyNetwork.Get(0);
            var f = net?.Of(system.Id);
            if (f == null) return;

            InspSection(Loc.T("СНАБЖЕНИЕ"), LGIcon.Trade);

            string state;
            Color col;
            switch (f.State)
            {
                case SupplyNetwork.NodeState.Hub:
                    state = net.FallbackHub
                        ? Loc.T("Временный хаб: столица потеряна — груз собирается здесь")
                        : SupplyState.IsDepot(system.Id) ? Loc.T("Хаб снабжения: депо принимает груз окрестных систем")
                        : Loc.T("Хаб снабжения: столица принимает груз империи");
                    col = DS.Gold;
                    break;
                case SupplyNetwork.NodeState.Blockaded:
                    state = Loc.T("Блокада: на орбите враг — груз не уходит и не приходит. Прогоните его флотом");
                    col = SupplyBad;
                    break;
                case SupplyNetwork.NodeState.CutOff:
                    state = Loc.T("Отрезана от хабов: груз копится на складе. Нужен открытый коридор или депо");
                    col = SupplyBad;
                    break;
                default:
                    state = f.DeliveredShare >= 0.99f
                        ? Loc.T("Подключена к хабу: весь груз доходит до казны")
                        : Loc.F("Коридоры забиты: до хаба доходит {0:0}% груза", f.DeliveredShare * 100f);
                    col = f.DeliveredShare >= 0.99f ? SupplyOk : SupplyWarn;
                    break;
            }
            InspNote(LGIcon.Trade, state, col);

            var st = SupplyState.StockOf(system.Id);
            if (f.State != SupplyNetwork.NodeState.Hub)
            {
                InspChips(
                    (LGIcon.Minerals, $"{st.Minerals:0}", Loc.T("титан на складе"), SupplyMineralsCol),
                    (LGIcon.Alloys, $"{st.Alloys:0}", Loc.T("сплавы на складе"), SupplyAlloysCol),
                    (LGIcon.Trade, $"{f.DeliveredShare * 100f:0}%", Loc.T("доходит до хаба"), f.DeliveredShare >= 0.99f ? SupplyOk : SupplyWarn));
                InspBar(LGIcon.Housing, Loc.T("Склад системы"), st.Total, f.StockCapacity, st.Total >= f.StockCapacity * 0.9f ? SupplyBad : SupplyWarn);
            }

            // Груз за месяц
            var sb = new StringBuilder();
            float cargo = f.Surplus + f.Alloys - f.LostAlloys;
            if (cargo > 0.05f) sb.Append(Loc.F("Груз на вывоз: <b>{0:0.#}</b>/мес (титан {1:0.#}, сплавы {2:0.#})", cargo, f.Surplus, f.Alloys - f.LostAlloys));
            if (f.StuckMinerals + f.StuckAlloys > 0.05f)
                sb.Append(sb.Length > 0 ? "\n" : "").Append(Loc.F("<color=#FFB84D>Застревает: {0:0.#}/мес — ляжет на склад</color>", f.StuckMinerals + f.StuckAlloys));
            if (f.FromStockMinerals + f.FromStockAlloys > 0.05f && f.State != SupplyNetwork.NodeState.Hub)
                sb.Append(sb.Length > 0 ? "\n" : "").Append(Loc.F("Со склада уходит: {0:0.#}/мес", f.FromStockMinerals + f.FromStockAlloys));
            if (f.Deficit > 0.05f && f.State != SupplyNetwork.NodeState.Hub)
                sb.Append(sb.Length > 0 ? "\n" : "").Append(Loc.F("Заводам не хватает своего титана: {0:0.#}/мес (со склада {1:0.#}, подвоз {2:0.#})", f.Deficit, f.LocalFromStock, f.Inbound));
            if (f.Unfed > 0.05f)
                sb.Append(sb.Length > 0 ? "\n" : "").Append(Loc.F("<color=#FF6B5C>Не довезли {0:0.#} титана — потеряно {1:0.#} сплавов</color>", f.Unfed, f.LostAlloys));
            if (f.Through > 0.5f)
                sb.Append(sb.Length > 0 ? "\n" : "").Append(Loc.F("Транзит через систему: {0:0}/мес", f.Through));

            // Коридоры системы
            foreach (int nb in system.ConnectedSystemIds)
            {
                var lane = net.Lane(system.Id, nb);
                if (lane == null) continue;
                string name = EmpireStats.GetSystem(nb)?.Name ?? "?";
                string line = lane.Blockaded ? Loc.F("<color=#FF6B5C>⇄ {0}: блокада</color>", name)
                            : lane.Capacity <= 0f ? Loc.F("<color=#8AA2A8>⇄ {0}: закрыт</color>", name)
                            : Loc.F("<color={0}>⇄ {1}: {2:0} / {3:0} ({4:0}%)</color>", lane.Load >= 0.95f ? "#FF6B5C" : lane.Load >= 0.6f ? "#FFB84D" : "#8AA2A8",
                                name, lane.Flow, lane.Capacity, lane.Load * 100f);
                sb.Append(sb.Length > 0 ? "\n" : "").Append(line);
            }
            if (sb.Length > 0) InspText(sb.ToString());

            SupplyBuildAction(system, SupplyState.Building.Depot);
            SupplyBuildAction(system, SupplyState.Building.Relay);
        }

        /// <summary>Стройка логистики: прогресс или кнопка с ценой.</summary>
        private void SupplyBuildAction(StarSystem system, SupplyState.Building b)
        {
            if (SupplyState.Has(b, system.Id)) return;
            if (b == SupplyState.Building.Depot && SupplyState.IsCapitalOf(0, system.Id)) return;
            var job = SupplyState.PendingJob(system.Id, b);
            if (job != null)
            {
                InspBar(LGIcon.Clock, Loc.F("Строится: {0}", SupplyState.NameOf(b).ToLower()), job.DaysTotal - job.DaysLeft, job.DaysTotal, DS.Gold);
                return;
            }
            float m = SupplyState.MineralsOf(b), a = SupplyState.AlloysOf(b);
            string block = SupplyState.Blocker(system, 0, b);
            var eco = EconomyManager.Instance;
            if (block == null && (eco == null || !eco.CanAfford(0f, m, a, 0f))) block = CostCheck.Full(0f, m, a);
            string tip = Loc.F("<b>{0}</b>\n{1}\n<color=#8AA2A8>{2:0} титана, {3:0} сплавов · {4:0} дн. · содержание {5:0} гелия-3/мес</color>",
                SupplyState.NameOf(b), SupplyState.DescriptionOf(b), m, a, SupplyState.DaysOf(b), SupplyState.UpkeepOf(b));
            InspButton(Loc.F("{0}  · {1:0} ◆ {2:0} ⬢", SupplyState.NameOf(b).ToUpper(), m, a), LGIcon.Trade,
                b == SupplyState.Building.Depot ? DS.BtnPrimary : DS.BtnNeutral, block, tip,
                () => SupplyState.TryStartPlayer(system, b), system);
        }
    }
}
