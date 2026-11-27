using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Снабжение — физическая логистика титана и сплавов (правила одни для игрока и ИИ).
    ///
    /// Гелий-3 и влияние — валюта и остаются общими. А титан и сплавы попадают в казну, только когда доходят
    /// до <b>хаба</b>: столицы или депо снабжения. Груз идёт по гиперкоридорам, у каждого — пропускная способность
    /// в месяц (звёздные базы, укрепления и транзитные узлы её поднимают, чужая и ничья территория — снижает).
    /// Что не пролезло, оседает на складе своей системы (<see cref="SupplyState"/>) и уходит позже, когда
    /// коридоры освободятся; переполненный склад теряет груз, а захватчик системы забирает её склад себе.
    ///
    /// Обратный поток: заводам, которым не хватает своего титана, сырьё везут из хабов по тем же коридорам.
    /// Не довезли — заводы стоят, сплавов меньше.
    ///
    /// Блокада: в системе вражеский (или пиратский) военный флот, а своего нет — через неё не идёт ничего.
    /// Отрезанные от хабов системы живут со складов, а флот дальше <see cref="FleetSupplyRange"/> прыжков от
    /// снабжаемой территории дороже в содержании и изнашивается (<see cref="FleetSupply"/>).
    ///
    /// Расчёт — поток минимальной стоимости (<see cref="FlowSolver"/>) по всей галактике; пересчитывается раз
    /// в день и только если что-то изменилось (производство, владельцы, блокады, постройки).
    /// </summary>
    public static class SupplyNetwork
    {
        // ==================== ПРАВИЛА ====================

        /// <summary>Пропускная способность коридора между своими системами со звёздными базами (единиц в месяц).</summary>
        public const float BaseLaneCapacity = 40f;
        /// <summary>Каждый уровень укрепления базы на конце коридора (по слабому концу).</summary>
        public const float FortLaneBonus = 0.25f;
        /// <summary>Свой конец без звёздной базы.</summary>
        public const float NoStarbaseMult = 0.6f;
        /// <summary>Ничья система на пути.</summary>
        public const float UnclaimedMult = 0.6f;
        /// <summary>Чужая территория, открытая для ваших кораблей (договор, союз, вольный мир).</summary>
        public const float ForeignMult = 0.5f;
        /// <summary>Транзитный узел на любом конце коридора.</summary>
        public const float RelayMult = 1.75f;
        /// <summary>Цена прыжка (своя территория) и надбавка за прыжок по чужой/ничьей — маршрут выбирает свою.</summary>
        public const float JumpCost = 1f;
        public const float ForeignJumpCost = 0.6f;
        /// <summary>Груз со склада уходит после свежей добычи (надбавка к цене).</summary>
        public const float StockCost = 0.5f;
        /// <summary>Флот снабжается не дальше стольких прыжков от подключённой к хабу своей системы.</summary>
        public const int FleetSupplyRange = 2;

        // ==================== ДАННЫЕ ====================

        /// <summary>Производство системы за месяц (уже с множителями) — вход расчёта.</summary>
        public struct SystemInput
        {
            public int SystemId;
            public float Minerals;
            public float Alloys;
            /// <summary>Сколько титана в месяц хотят переплавить заводы системы.</summary>
            public float Refining;
            public int Colonies;
        }

        public enum NodeState { Hub, Connected, CutOff, Blockaded }

        /// <summary>Итог по системе владельца за месяц.</summary>
        public class SystemFlow
        {
            public int SystemId;
            public NodeState State;
            public float Minerals, Alloys, Refining;
            /// <summary>Титан сверх нужд своих заводов — груз на вывоз.</summary>
            public float Surplus;
            /// <summary>Нехватка титана своим заводам (до склада и подвоза).</summary>
            public float Deficit;
            /// <summary>Доля свежего груза, дошедшая до хаба (0…1).</summary>
            public float DeliveredShare = 1f;
            /// <summary>Титан со склада системы на свои заводы.</summary>
            public float LocalFromStock;
            /// <summary>Подвезено титана из хабов.</summary>
            public float Inbound;
            /// <summary>Не довезли титан заводам — эти мощности стоят.</summary>
            public float Unfed;
            /// <summary>Сплавов не выплавлено из-за нехватки сырья.</summary>
            public float LostAlloys;
            /// <summary>Застрянет на складе в этом месяце.</summary>
            public float StuckMinerals, StuckAlloys;
            /// <summary>Уйдёт со склада в казну.</summary>
            public float FromStockMinerals, FromStockAlloys;
            /// <summary>Груз, идущий через систему транзитом (для оценки важности узла).</summary>
            public float Through;
            public float StockCapacity;
        }

        /// <summary>Коридор глазами владельца: ёмкость и груз за месяц (в обе стороны).</summary>
        public class LaneFlow
        {
            public int A, B;
            public float Capacity;
            public float Flow;
            public bool Blockaded;
            public float Load => Capacity > 0f ? Mathf.Clamp01(Flow / Capacity) : (Flow > 0f ? 1f : 0f);
        }

        public class Result
        {
            public int Owner;
            public readonly Dictionary<int, SystemFlow> Systems = new Dictionary<int, SystemFlow>();
            public readonly Dictionary<long, LaneFlow> Lanes = new Dictionary<long, LaneFlow>();
            public readonly HashSet<int> Hubs = new HashSet<int>();
            /// <summary>Системы (любые), где флот владельца на снабжении.</summary>
            public readonly HashSet<int> Supplied = new HashSet<int>();
            public int CutOff, Blockaded;
            public float StuckMinerals, StuckAlloys, FromStockMinerals, FromStockAlloys;
            public float Unfed, LocalFromStock, LostAlloys;
            public float TotalCargo, DeliveredCargo;
            /// <summary>Титан заводам из казны (подвоз и нужды заводов хабов) и сплавы этих заводов.</summary>
            public float PoolRefining, PoolAlloys;
            /// <summary>Хаб назначен запасным (столица потеряна, депо нет).</summary>
            public bool FallbackHub;
            internal int Hash;
            internal int Session;

            /// <summary>Доля груза, дошедшая до хабов (0…1).</summary>
            public float Efficiency => TotalCargo > 0.01f ? Mathf.Clamp01(DeliveredCargo / TotalCargo) : 1f;
            /// <summary>Изменение титана казны из-за логистики (к производству минус переработка).</summary>
            public float MineralsDelta => -StuckMinerals + FromStockMinerals + Unfed + LocalFromStock;
            /// <summary>Изменение сплавов казны из-за логистики.</summary>
            public float AlloysDelta => -StuckAlloys - LostAlloys + FromStockAlloys;

            public SystemFlow Of(int systemId) => Systems.TryGetValue(systemId, out var f) ? f : null;
            public LaneFlow Lane(int a, int b) => Lanes.TryGetValue(LaneKey(a, b), out var l) ? l : null;
        }

        private static readonly Dictionary<int, Result> s_results = new Dictionary<int, Result>();
        private static int s_session = 1;

        /// <summary>Растёт при каждом пересчёте любой сети — по нему перерисовываются слой карты и панели.</summary>
        public static int Version { get; private set; }

        public static event Action<int> OnUpdated;

        public static long LaneKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>Последний расчёт сети владельца (null — ещё не считали).</summary>
        public static Result Get(int owner) => s_results.TryGetValue(owner, out var r) ? r : null;

        /// <summary>Новая партия или загрузка: забыть все сети и флоты на орбитах.</summary>
        public static void Reset()
        {
            s_results.Clear();
            s_presenceStamp = int.MinValue;
            s_session++;
            Version++;
        }

        /// <summary>Пересчитать при следующем запросе (постройка депо, смена договоров).</summary>
        public static void MarkDirty()
        {
            s_session++;
            s_presenceStamp = int.MinValue;
        }

        /// <summary>Флот владельца в этой системе на снабжении (нет данных — считаем, что да).</summary>
        public static bool InSupply(int owner, int systemId)
        {
            var r = Get(owner);
            if (r == null || r.Systems.Count == 0) return true;
            return r.Supplied.Contains(systemId);
        }

        /// <summary>Состояние своей системы в сети владельца (для панелей).</summary>
        public static SystemFlow FlowOf(int systemId)
        {
            var sys = EmpireStats.GetSystem(systemId);
            if (sys == null || sys.OwnerId < 0) return null;
            return Get(sys.OwnerId)?.Of(systemId);
        }

        /// <summary>
        /// Насколько система важна для снабжения владельца: хаб, доля проходящего груза, склад.
        /// Используют ИИ (цели осады, укрепления) и подсказки.
        /// </summary>
        public static float StrategicValue(int owner, int systemId)
        {
            var r = Get(owner);
            var f = r?.Of(systemId);
            if (f == null) return 0f;
            float v = 0f;
            if (f.State == NodeState.Hub) v += r.Hubs.Count <= 1 ? 35f : 22f;
            if (r.DeliveredCargo > 1f) v += Mathf.Clamp01(f.Through / r.DeliveredCargo) * 40f;
            var stock = SupplyState.StockOf(systemId);
            v += Mathf.Min(20f, (stock.Minerals + stock.Alloys) / 40f);
            return v;
        }

        // ==================== БЛОКАДЫ ====================

        /// <summary>Военные флоты на орбитах: система → владельцы. Считается раз в день.</summary>
        private static readonly Dictionary<int, List<int>> s_presence = new Dictionary<int, List<int>>();
        private static int s_presenceStamp = int.MinValue;

        private static void RefreshPresence(int stamp)
        {
            if (s_presenceStamp == stamp) return;
            s_presenceStamp = stamp;
            foreach (var kv in s_presence) kv.Value.Clear();
            var fm = FleetManager.Instance;
            if (fm == null) return;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.Type != FleetType.Military || d.State == FleetState.InHyperlane) continue;
                if (!s_presence.TryGetValue(d.CurrentSystemId, out var list)) s_presence[d.CurrentSystemId] = list = new List<int>();
                if (!list.Contains(d.OwnerId)) list.Add(d.OwnerId);
            }
        }

        /// <summary>Система перекрыта для груза владельца: там враг (или пираты), а своего военного флота нет.</summary>
        public static bool IsBlockaded(int owner, int systemId)
        {
            RefreshPresence(DayStamp());
            if (!s_presence.TryGetValue(systemId, out var here) || here.Count == 0) return false;
            if (here.Contains(owner)) return false;
            foreach (int o in here)
                if (Threats.IsThreat(o) || Diplomacy.AtWar(o, owner)) return true;
            return false;
        }

        private static int DayStamp()
        {
            var tm = TimeManager.Instance;
            int day = tm != null ? (tm.Year * 12 + tm.Month) * 32 + tm.Day : 0;
            return day * 7919 + s_session;
        }

        // ==================== ГРАФ ====================

        /// <summary>Может ли груз владельца идти через систему (своя, ничья или открытая чужая, не вражеская).</summary>
        private static bool Passable(int owner, StarSystem s, out bool own, out float mult)
        {
            own = s.OwnerId == owner;
            mult = 1f;
            if (own)
            {
                mult = s.HasStarbase ? 1f + FortLaneBonus * Mathf.Clamp(s.Fortification, 0, Fortifications.MaxLevel) : NoStarbaseMult;
                return true;
            }
            if (Threats.IsThreat(s.OwnerId)) return false;
            if (s.OwnerId == -1) { mult = UnclaimedMult; return true; }
            if (s.OwnerId >= 0 && Diplomacy.AtWar(owner, s.OwnerId)) return false;
            if (!BorderRules.CanEnter(owner, s)) return false;
            mult = ForeignMult;
            return true;
        }

        /// <summary>Пропускная способность коридора a—b для владельца (0 — закрыт).</summary>
        public static float LaneCapacity(int owner, StarSystem a, StarSystem b)
        {
            if (a == null || b == null) return 0f;
            if (!Passable(owner, a, out _, out float ma) || !Passable(owner, b, out _, out float mb)) return 0f;
            if (IsBlockaded(owner, a.Id) || IsBlockaded(owner, b.Id)) return 0f;
            float cap = BaseLaneCapacity * Mathf.Min(ma, mb);
            if ((a.OwnerId == owner && SupplyState.IsRelay(a.Id)) || (b.OwnerId == owner && SupplyState.IsRelay(b.Id))) cap *= RelayMult;
            return cap;
        }

        // ==================== РАСЧЁТ ====================

        /// <summary>
        /// Посчитать сеть владельца по производству его систем. Повторный вызов в тот же день с теми же данными
        /// возвращает прошлый результат.
        /// </summary>
        public static Result Solve(int owner, int capitalSystemId, List<SystemInput> inputs)
        {
            var systems = EmpireStats.Systems;
            var galaxy = EmpireStats.Galaxy;
            RefreshPresence(DayStamp());

            // Хабы: столица и депо; нет ни того, ни другого — самая населённая своя система
            var hubs = new HashSet<int>();
            var capital = EmpireStats.GetSystem(capitalSystemId);
            if (capital != null && capital.OwnerId == owner) hubs.Add(capital.Id);
            foreach (int d in SupplyState.Depots)
            {
                var ds = EmpireStats.GetSystem(d);
                if (ds != null && ds.OwnerId == owner) hubs.Add(d);
            }
            bool fallback = false;
            if (hubs.Count == 0 && inputs.Count > 0)
            {
                int best = -1, bestPop = -1;
                foreach (var inp in inputs)
                {
                    var s = EmpireStats.GetSystem(inp.SystemId);
                    if (s == null) continue;
                    int pop = 0;
                    foreach (var p in s.Planets) pop += Mathf.Max(0, p.Population);
                    if (pop > bestPop) { bestPop = pop; best = s.Id; }
                }
                if (best >= 0) { hubs.Add(best); fallback = true; }
            }

            // Отпечаток входа: если всё как вчера — расчёт не нужен
            int hash = 17;
            unchecked
            {
                hash = hash * 31 + owner;
                hash = hash * 31 + SupplyState.Version;
                foreach (int h in hubs) hash = hash * 31 + h * 7;
                foreach (var inp in inputs)
                {
                    hash = hash * 31 + inp.SystemId;
                    hash = hash * 31 + Mathf.RoundToInt(inp.Minerals * 4f);
                    hash = hash * 31 + Mathf.RoundToInt(inp.Alloys * 4f);
                    hash = hash * 31 + Mathf.RoundToInt(inp.Refining * 4f);
                    var st = SupplyState.StockOf(inp.SystemId);
                    hash = hash * 31 + Mathf.RoundToInt(st.Minerals) * 3 + Mathf.RoundToInt(st.Alloys);
                }
                for (int i = 0; i < systems.Count; i++)
                {
                    var s = systems[i];
                    hash = hash * 31 + s.OwnerId;
                    hash = hash * 31 + (s.HasStarbase ? 1 + s.Fortification : 0);
                    // Не сами флоты (они ходят каждый день), а только блокады — они меняют сеть
                    if (s_presence.TryGetValue(s.Id, out var here) && here.Count > 0 && IsBlockaded(owner, s.Id))
                        hash = hash * 31 + s.Id * 13 + 1;
                }
                // Войны и договоры меняют проходимость чужих систем
                foreach (var ai in AIEmpireManager.All) hash = hash * 31 + (ai.AtWar ? 1 : 0) + (ai.HasAgreement(DealItemKind.Borders) ? 2 : 0) + (ai.HasAlliance ? 4 : 0);
            }

            if (s_results.TryGetValue(owner, out var prev) && prev.Hash == hash && prev.Session == s_session)
                return prev;

            var r = new Result { Owner = owner, Hash = hash, Session = s_session, FallbackHub = fallback };
            foreach (int h in hubs) r.Hubs.Add(h);

            int n = systems.Count;
            if (n == 0 || galaxy == null) { Store(owner, r); return r; }

            // Узлы 0…n−1 — системы (id совпадает с индексом), n — исток, n+1 — сток
            int src = n, sink = n + 1;
            var outSolver = new FlowSolver();
            outSolver.AddNodes(n + 2);

            var lanes = new List<(int a, int b, float cap, float cost, int eAB, int eBA)>();
            foreach (var lane in galaxy.Hyperlanes)
            {
                var a = lane.SystemA >= 0 && lane.SystemA < n ? systems[lane.SystemA] : null;
                var b = lane.SystemB >= 0 && lane.SystemB < n ? systems[lane.SystemB] : null;
                if (a == null || b == null) continue;
                bool touchesOwn = a.OwnerId == owner || b.OwnerId == owner;
                float cap = LaneCapacity(owner, a, b);
                bool blockaded = touchesOwn && (IsBlockaded(owner, a.Id) || IsBlockaded(owner, b.Id));
                if (touchesOwn) r.Lanes[LaneKey(a.Id, b.Id)] = new LaneFlow { A = a.Id, B = b.Id, Capacity = cap, Blockaded = blockaded };
                if (cap <= FlowSolver.Eps) continue;
                float cost = JumpCost + (a.OwnerId != owner ? ForeignJumpCost : 0f) + (b.OwnerId != owner ? ForeignJumpCost : 0f);
                int eAB = outSolver.AddEdge(a.Id, b.Id, cap, cost);
                int eBA = outSolver.AddEdge(b.Id, a.Id, cap, cost);
                lanes.Add((a.Id, b.Id, cap, cost, eAB, eBA));
            }

            // Состояние систем: хаб / подключена / отрезана / блокада
            var reach = ReachFrom(hubs, lanes, n);
            var flows = new List<SystemFlow>(inputs.Count);
            var prodEdge = new Dictionary<int, int>();
            var stockEdge = new Dictionary<int, int>();
            var stockShippable = new Dictionary<int, float>();
            foreach (var inp in inputs)
            {
                if (inp.SystemId < 0 || inp.SystemId >= n) continue;
                var f = new SystemFlow
                {
                    SystemId = inp.SystemId,
                    Minerals = inp.Minerals, Alloys = inp.Alloys, Refining = inp.Refining,
                    Surplus = Mathf.Max(0f, inp.Minerals - inp.Refining),
                    Deficit = Mathf.Max(0f, inp.Refining - inp.Minerals),
                    StockCapacity = SupplyState.StockCapacity(inp.SystemId, inp.Colonies)
                };
                bool hub = hubs.Contains(inp.SystemId);
                bool blockaded = IsBlockaded(owner, inp.SystemId);
                f.State = hub ? NodeState.Hub : blockaded ? NodeState.Blockaded : reach[inp.SystemId] ? NodeState.Connected : NodeState.CutOff;
                if (f.State == NodeState.CutOff) r.CutOff++;
                if (blockaded) r.Blockaded++;
                r.Systems[inp.SystemId] = f;
                flows.Add(f);

                var stock = SupplyState.StockOf(inp.SystemId);
                if (hub)
                {
                    // Хаб: всё своё сразу в казне, склад (если остался с прежних времён) тоже уходит в казну
                    f.FromStockMinerals = stock.Minerals;
                    f.FromStockAlloys = stock.Alloys;
                    continue;
                }
                // Своим заводам — сначала титан со своего склада
                f.LocalFromStock = Mathf.Min(f.Deficit, stock.Minerals);
                float cargo = f.Surplus + f.Alloys;
                float shippable = Mathf.Max(0f, stock.Minerals - f.LocalFromStock) + stock.Alloys;
                stockShippable[f.SystemId] = shippable;
                if (cargo > FlowSolver.Eps) prodEdge[f.SystemId] = outSolver.AddEdge(src, f.SystemId, cargo, 0f);
                if (shippable > FlowSolver.Eps) stockEdge[f.SystemId] = outSolver.AddEdge(src, f.SystemId, shippable, StockCost);
            }
            foreach (int h in hubs) outSolver.AddEdge(h, sink, 1e7f, 0f);

            outSolver.Solve(src, sink, 1e9f, out _);

            // Обратный поток: титан из хабов заводам, по остатку ёмкости в ту же сторону
            var inSolver = new FlowSolver();
            inSolver.AddNodes(n + 2);
            var inLanes = new List<(int eAB, int eBA)>(lanes.Count);
            foreach (var l in lanes)
            {
                float leftAB = l.cap - outSolver.Flow(l.eAB);
                float leftBA = l.cap - outSolver.Flow(l.eBA);
                inLanes.Add((inSolver.AddEdge(l.a, l.b, Mathf.Max(0f, leftAB), l.cost),
                             inSolver.AddEdge(l.b, l.a, Mathf.Max(0f, leftBA), l.cost)));
            }
            foreach (int h in hubs) inSolver.AddEdge(src, h, 1e7f, 0f);
            var demandEdge = new Dictionary<int, int>();
            foreach (var f in flows)
            {
                if (f.State == NodeState.Hub) continue;
                float need = f.Deficit - f.LocalFromStock;
                if (need > FlowSolver.Eps) demandEdge[f.SystemId] = inSolver.AddEdge(f.SystemId, sink, need, 0f);
            }
            if (demandEdge.Count > 0) inSolver.Solve(src, sink, 1e9f, out _);

            // Разбор: доставлено, застряло, подвезено
            foreach (var f in flows)
            {
                if (f.State == NodeState.Hub)
                {
                    f.DeliveredShare = 1f;
                    r.TotalCargo += f.Surplus + f.Alloys;
                    r.DeliveredCargo += f.Surplus + f.Alloys;
                    r.FromStockMinerals += f.FromStockMinerals;
                    r.FromStockAlloys += f.FromStockAlloys;
                    r.PoolRefining += f.Deficit;
                    if (f.Refining > FlowSolver.Eps) r.PoolAlloys += f.Alloys * Mathf.Clamp01(f.Deficit / f.Refining);
                    continue;
                }
                float cargo = f.Surplus + f.Alloys;
                float shipped = prodEdge.TryGetValue(f.SystemId, out int pe) ? outSolver.Flow(pe) : 0f;
                f.DeliveredShare = cargo > FlowSolver.Eps ? Mathf.Clamp01(shipped / cargo) : 1f;

                float need = f.Deficit - f.LocalFromStock;
                f.Inbound = demandEdge.TryGetValue(f.SystemId, out int de) ? inSolver.Flow(de) : 0f;
                f.Unfed = Mathf.Max(0f, need - f.Inbound);
                float fed = f.Refining > FlowSolver.Eps ? Mathf.Clamp01(1f - f.Unfed / f.Refining) : 1f;
                f.LostAlloys = f.Alloys * (1f - fed);
                float alloysMade = f.Alloys - f.LostAlloys;

                f.StuckMinerals = f.Surplus * (1f - f.DeliveredShare);
                f.StuckAlloys = alloysMade * (1f - f.DeliveredShare);

                var stock = SupplyState.StockOf(f.SystemId);
                float shippable = stockShippable.TryGetValue(f.SystemId, out float sh) ? sh : 0f;
                float fromStock = stockEdge.TryGetValue(f.SystemId, out int se) ? outSolver.Flow(se) : 0f;
                float share = shippable > FlowSolver.Eps ? Mathf.Clamp01(fromStock / shippable) : 0f;
                f.FromStockMinerals = Mathf.Max(0f, stock.Minerals - f.LocalFromStock) * share;
                f.FromStockAlloys = stock.Alloys * share;

                r.TotalCargo += f.Surplus + alloysMade;
                r.DeliveredCargo += (f.Surplus + alloysMade) * f.DeliveredShare;
                r.StuckMinerals += f.StuckMinerals;
                r.StuckAlloys += f.StuckAlloys;
                r.FromStockMinerals += f.FromStockMinerals;
                r.FromStockAlloys += f.FromStockAlloys;
                r.Unfed += f.Unfed;
                r.LocalFromStock += f.LocalFromStock;
                r.LostAlloys += f.LostAlloys;
                r.PoolRefining += f.Inbound;
                if (f.Refining > FlowSolver.Eps) r.PoolAlloys += f.Alloys * Mathf.Clamp01(f.Inbound / f.Refining);
            }

            // Груз по коридорам и транзит через системы
            for (int i = 0; i < lanes.Count; i++)
            {
                var l = lanes[i];
                float ab = Mathf.Max(0f, outSolver.Flow(l.eAB)) + Mathf.Max(0f, inSolver.Flow(inLanes[i].eAB));
                float ba = Mathf.Max(0f, outSolver.Flow(l.eBA)) + Mathf.Max(0f, inSolver.Flow(inLanes[i].eBA));
                if (r.Lanes.TryGetValue(LaneKey(l.a, l.b), out var lf)) lf.Flow = ab + ba;
                // Транзит: груз, вошедший в систему, которая не его конечная точка
                AddThrough(r, l.b, ab);
                AddThrough(r, l.a, ba);
            }
            foreach (var f in flows) f.Through = Mathf.Max(0f, f.Through - f.Inbound);   // подвоз своим заводам — не транзит

            // Снабжение флота: до FleetSupplyRange прыжков от своей подключённой системы
            var frontier = new Queue<(int id, int depth)>();
            foreach (var f in flows)
                if (f.State == NodeState.Hub || f.State == NodeState.Connected)
                {
                    r.Supplied.Add(f.SystemId);
                    frontier.Enqueue((f.SystemId, 0));
                }
            while (frontier.Count > 0)
            {
                var (id, depth) = frontier.Dequeue();
                if (depth >= FleetSupplyRange) continue;
                foreach (int nb in systems[id].ConnectedSystemIds)
                    if (nb >= 0 && nb < n && r.Supplied.Add(nb)) frontier.Enqueue((nb, depth + 1));
            }

            Store(owner, r);
            return r;
        }

        private static void AddThrough(Result r, int systemId, float amount)
        {
            if (amount <= FlowSolver.Eps || r.Hubs.Contains(systemId)) return;
            if (r.Systems.TryGetValue(systemId, out var f)) f.Through += amount;
        }

        private static bool[] ReachFrom(HashSet<int> hubs, List<(int a, int b, float cap, float cost, int eAB, int eBA)> lanes, int n)
        {
            var adj = new List<int>[n];
            foreach (var l in lanes)
            {
                (adj[l.a] ??= new List<int>()).Add(l.b);
                (adj[l.b] ??= new List<int>()).Add(l.a);
            }
            var seen = new bool[n];
            var q = new Queue<int>();
            foreach (int h in hubs) if (h >= 0 && h < n && !seen[h]) { seen[h] = true; q.Enqueue(h); }
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                if (adj[u] == null) continue;
                foreach (int v in adj[u]) if (!seen[v]) { seen[v] = true; q.Enqueue(v); }
            }
            return seen;
        }

        private static void Store(int owner, Result r)
        {
            s_results[owner] = r;
            Version++;
            OnUpdated?.Invoke(owner);
        }
    }
}
