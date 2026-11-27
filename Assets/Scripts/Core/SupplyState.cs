using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>Сохранение логистики: склады систем, депо и транзитные узлы.</summary>
    [Serializable]
    public class SupplySave
    {
        [Serializable]
        public class Stock { public int Id; public float Minerals; public float Alloys; }

        public List<Stock> Stocks = new List<Stock>();
        public List<int> Depots = new List<int>();
        public List<int> Relays = new List<int>();
    }

    /// <summary>
    /// Состояние логистики (<see cref="SupplyNetwork"/>): что лежит на складах систем, где стоят депо
    /// снабжения (хабы) и транзитные узлы (коридоры шире). Склад принадлежит системе: захватили систему —
    /// склад достался захватчику. Постройки тоже остаются при системе.
    /// </summary>
    public static class SupplyState
    {
        public struct StockData
        {
            public float Minerals, Alloys;
            public float Total => Minerals + Alloys;
        }

        // ==================== ПРАВИЛА ====================

        public const float BaseStockCapacity = 300f;
        public const float StockPerColony = 100f;
        public const float RelayStockBonus = 300f;

        public const float DepotMinerals = 150f, DepotAlloys = 100f, DepotDays = 120f, DepotUpkeep = 2f;
        public const float RelayMinerals = 80f, RelayAlloys = 50f, RelayDays = 60f, RelayUpkeep = 1f;
        /// <summary>Очередь системы для логистики (не мешает укреплениям −1 и армии −3).</summary>
        public const int SupplyLane = -4;

        private static readonly Dictionary<int, StockData> s_stocks = new Dictionary<int, StockData>();
        private static readonly HashSet<int> s_depots = new HashSet<int>();
        private static readonly HashSet<int> s_relays = new HashSet<int>();
        private static bool s_hooked;

        /// <summary>Растёт при любом изменении построек и складов.</summary>
        public static int Version { get; private set; }

        public static IEnumerable<int> Depots => s_depots;
        public static bool IsDepot(int systemId) => s_depots.Contains(systemId);
        public static bool IsRelay(int systemId) => s_relays.Contains(systemId);

        public static StockData StockOf(int systemId) => s_stocks.TryGetValue(systemId, out var s) ? s : default;

        /// <summary>Вместимость склада системы.</summary>
        public static float StockCapacity(int systemId, int colonies)
            => BaseStockCapacity + StockPerColony * Mathf.Max(0, colonies) + (IsRelay(systemId) ? RelayStockBonus : 0f);

        public static int ColoniesIn(StarSystem sys)
        {
            int c = 0;
            if (sys == null) return 0;
            foreach (var p in sys.Planets)
                if (p.Population > 0 && p.Type != PlanetType.GasGiant && p.Type != PlanetType.Molten) c++;
            return c;
        }

        // ==================== ЖИЗНЕННЫЙ ЦИКЛ ====================

        /// <summary>Новая партия: пустые склады, никаких построек.</summary>
        public static void Reset()
        {
            s_stocks.Clear();
            s_depots.Clear();
            s_relays.Clear();
            s_cutWarned.Clear();
            s_laneWarned.Clear();
            s_overflowWarned = false;
            Version++;
            SupplyNetwork.Reset();
            Hook();
        }

        private static void Hook()
        {
            if (s_hooked) return;
            s_hooked = true;
            SiegeManager.OnSystemCaptured += HandleCaptured;
        }

        public static SupplySave Capture()
        {
            var s = new SupplySave();
            foreach (var kv in s_stocks)
                if (kv.Value.Total > 0.01f) s.Stocks.Add(new SupplySave.Stock { Id = kv.Key, Minerals = kv.Value.Minerals, Alloys = kv.Value.Alloys });
            s.Depots.AddRange(s_depots);
            s.Relays.AddRange(s_relays);
            return s;
        }

        /// <summary>Загрузка: null — сохранение до логистики (склады пусты, построек нет).</summary>
        public static void Restore(SupplySave s)
        {
            Reset();
            if (s == null) return;
            if (s.Stocks != null)
                foreach (var st in s.Stocks)
                    s_stocks[st.Id] = new StockData { Minerals = Mathf.Max(0f, st.Minerals), Alloys = Mathf.Max(0f, st.Alloys) };
            if (s.Depots != null) foreach (int d in s.Depots) s_depots.Add(d);
            if (s.Relays != null) foreach (int d in s.Relays) s_relays.Add(d);
            Version++;
        }

        // ==================== МЕСЯЦ ====================

        /// <summary>
        /// Первое число месяца: склады принимают застрявший груз и отдают ушедший (по последнему расчёту сети
        /// владельца — тому же, что в отчёте экономики). Сверх вместимости груз пропадает.
        /// </summary>
        public static void ApplyMonth(int owner)
        {
            var r = SupplyNetwork.Get(owner);
            if (r == null) return;
            float lost = 0f;
            foreach (var f in r.Systems.Values)
            {
                var sys = EmpireStats.GetSystem(f.SystemId);
                if (sys == null || sys.OwnerId != owner) continue;
                var st = StockOf(f.SystemId);
                st.Minerals = Mathf.Max(0f, st.Minerals - f.LocalFromStock - f.FromStockMinerals + f.StuckMinerals);
                st.Alloys = Mathf.Max(0f, st.Alloys - f.FromStockAlloys + f.StuckAlloys);
                float over = st.Total - f.StockCapacity;
                if (over > 0f)
                {
                    // Переполнение: теряется пропорционально
                    float k = f.StockCapacity / Mathf.Max(0.01f, st.Total);
                    lost += over;
                    st.Minerals *= k;
                    st.Alloys *= k;
                }
                if (st.Total < 0.05f) s_stocks.Remove(f.SystemId);
                else s_stocks[f.SystemId] = st;
            }
            Version++;
            if (owner == 0) NotifyPlayer(r, lost);
        }

        // ==================== ПОСТРОЙКИ ====================

        public enum Building { Depot, Relay }

        public static JobKind KindOf(Building b) => b == Building.Depot ? JobKind.SupplyDepot : JobKind.SupplyRelay;
        public static float MineralsOf(Building b) => b == Building.Depot ? DepotMinerals : RelayMinerals;
        public static float AlloysOf(Building b) => b == Building.Depot ? DepotAlloys : RelayAlloys;
        public static float DaysOf(Building b) => b == Building.Depot ? DepotDays : RelayDays;
        public static float UpkeepOf(Building b) => b == Building.Depot ? DepotUpkeep : RelayUpkeep;

        public static string NameOf(Building b) => b == Building.Depot ? Loc.T("Депо снабжения") : Loc.T("Транзитный узел");

        public static string DescriptionOf(Building b) => b == Building.Depot
            ? Loc.T("Хаб снабжения: титан и сплавы систем вокруг попадают в казну здесь, а не только в столице. Отсюда же везут сырьё заводам.")
            : Loc.F("Коридоры этой системы пропускают в {0:0.##} раза больше груза, склад системы +{1:0}.", SupplyNetwork.RelayMult, RelayStockBonus);

        public static bool Has(Building b, int systemId) => b == Building.Depot ? IsDepot(systemId) : IsRelay(systemId);

        public static ConstructionJob PendingJob(int systemId, Building b)
        {
            var cm = ConstructionManager.Instance;
            if (cm == null) return null;
            var kind = KindOf(b);
            foreach (var j in cm.Jobs) if (j != null && j.Kind == kind && j.SystemId == systemId) return j;
            return null;
        }

        /// <summary>Почему нельзя построить (null — можно; ресурсы проверяет вызывающий).</summary>
        public static string Blocker(StarSystem sys, int owner, Building b)
        {
            if (sys == null || sys.OwnerId != owner || !sys.HasStarbase) return Loc.T("Нужна ваша звёздная база");
            if (Has(b, sys.Id)) return Loc.T("Уже построено");
            if (b == Building.Depot && IsCapitalOf(owner, sys.Id)) return Loc.T("Столица и так хаб снабжения");
            if (PendingJob(sys.Id, b) != null) return Loc.T("Уже строится");
            var siege = SiegeManager.Instance?.GetSiege(sys.Id);
            if (siege != null && siege.Active) return Loc.T("Система в осаде — сначала прогоните врага");
            if (CombatManager.Instance?.GetBattle(sys.Id) != null) return Loc.T("В системе идёт бой");
            return null;
        }

        public static bool IsCapitalOf(int owner, int systemId)
        {
            if (owner == 0) return systemId == EconomyManager.PlayerCapitalId;
            var ai = AIEmpireManager.For(owner);
            return ai != null && ai.CapitalSystemId == systemId;
        }

        /// <summary>Заказ игрока: оплатить и поставить в очередь логистики системы.</summary>
        public static bool TryStartPlayer(StarSystem sys, Building b)
        {
            if (Blocker(sys, 0, b) != null) return false;
            float m = MineralsOf(b), a = AlloysOf(b);
            var eco = EconomyManager.Instance;
            var cm = ConstructionManager.Instance;
            if (eco == null || cm == null || !eco.TrySpend(0f, m, a, 0f)) return false;
            Enqueue(cm, 0, sys.Id, b);
            eco.RecalculateAll();
            return true;
        }

        /// <summary>Поставить стройку в очередь (ресурсы уже списаны).</summary>
        public static ConstructionJob Enqueue(ConstructionManager cm, int owner, int systemId, Building b)
        {
            var job = cm.EnqueueSystemJob(KindOf(b), owner, systemId, DaysOf(b), 0f, MineralsOf(b), AlloysOf(b), 0f);
            if (job != null) job.PlanetIndex = SupplyLane;
            return job;
        }

        /// <summary>Стройка завершена.</summary>
        public static void Complete(ConstructionJob j)
        {
            var sys = EmpireStats.GetSystem(j.SystemId);
            if (sys == null || sys.OwnerId != j.Owner || !sys.HasStarbase) return;
            var b = j.Kind == JobKind.SupplyDepot ? Building.Depot : Building.Relay;
            if (b == Building.Depot) s_depots.Add(sys.Id); else s_relays.Add(sys.Id);
            Version++;
            SupplyNetwork.MarkDirty();
            if (j.Owner == 0)
            {
                NotificationCenter.Show(NameOf(b), Loc.F("{0}: построено. Сеть снабжения пересчитана", sys.Name), NotificationCenter.Kind.Success, 5f);
                EconomyManager.Instance?.RecalculateAll();
            }
        }

        /// <summary>Содержание построек снабжения владельца (гелий-3 в месяц).</summary>
        public static float Upkeep(int owner)
        {
            float s = 0f;
            foreach (int d in s_depots) if (EmpireStats.GetSystem(d)?.OwnerId == owner) s += DepotUpkeep;
            foreach (int d in s_relays) if (EmpireStats.GetSystem(d)?.OwnerId == owner) s += RelayUpkeep;
            return s;
        }

        public static int CountOwned(int owner, Building b)
        {
            int c = 0;
            foreach (int d in b == Building.Depot ? s_depots : s_relays)
                if (EmpireStats.GetSystem(d)?.OwnerId == owner) c++;
            return c;
        }

        // ==================== ЗАХВАТ ====================

        private static void HandleCaptured(StarSystem sys, int oldOwner, int newOwner)
        {
            if (sys == null) return;
            SupplyNetwork.MarkDirty();
            var st = StockOf(sys.Id);
            if (st.Total < 1f || SiegeManager.IsPeacefulTransfer) return;
            if (newOwner == 0)
                NotificationCenter.Show(Loc.T("Трофейные склады"),
                    Loc.F("{0}: захвачено {1:0} титана и {2:0} сплавов — уйдут в казну, когда система подключится к хабу", sys.Name, st.Minerals, st.Alloys),
                    NotificationCenter.Kind.Success, 7f);
            else if (oldOwner == 0)
                NotificationCenter.Show(Loc.T("Склады потеряны"),
                    Loc.F("{0}: враг забрал {1:0} титана и {2:0} сплавов", sys.Name, st.Minerals, st.Alloys),
                    NotificationCenter.Kind.Danger, 7f);
        }

        // ==================== УВЕДОМЛЕНИЯ ИГРОКУ ====================

        private static readonly HashSet<int> s_cutWarned = new HashSet<int>();
        private static readonly HashSet<long> s_laneWarned = new HashSet<long>();
        private static bool s_overflowWarned;

        private static void NotifyPlayer(SupplyNetwork.Result r, float lost)
        {
            // Новые отрезанные системы (с производством — пустые форпосты не тревожат)
            var newlyCut = new List<string>();
            var stillCut = new HashSet<int>();
            foreach (var f in r.Systems.Values)
            {
                bool cut = f.State == SupplyNetwork.NodeState.CutOff || f.State == SupplyNetwork.NodeState.Blockaded;
                bool matters = f.Surplus + f.Alloys + f.Deficit > 0.5f;
                if (!cut || !matters) continue;
                stillCut.Add(f.SystemId);
                if (s_cutWarned.Add(f.SystemId)) newlyCut.Add(EmpireStats.GetSystem(f.SystemId)?.Name ?? "?");
            }
            s_cutWarned.RemoveWhere(id => !stillCut.Contains(id));
            if (newlyCut.Count > 0)
                NotificationCenter.Show(Loc.T("Снабжение прервано"),
                    Loc.F("Отрезаны от хабов: {0}. Груз копится на местных складах — уберите блокаду, восстановите коридор или постройте депо", string.Join(", ", newlyCut)),
                    NotificationCenter.Kind.Warning, 9f);

            // Перегруженные коридоры: предупреждаем один раз, пока загрузка не спадёт ниже 80%
            var jam = new List<string>();
            foreach (var kv in r.Lanes)
            {
                var l = kv.Value;
                if (l.Capacity <= 0f) { s_laneWarned.Remove(kv.Key); continue; }
                if (l.Load >= 0.98f)
                {
                    if (s_laneWarned.Add(kv.Key))
                        jam.Add($"{EmpireStats.GetSystem(l.A)?.Name} — {EmpireStats.GetSystem(l.B)?.Name}");
                }
                else if (l.Load < 0.8f) s_laneWarned.Remove(kv.Key);
            }
            if (jam.Count > 0 && r.StuckMinerals + r.StuckAlloys > 1f)
                NotificationCenter.Show(Loc.T("Коридоры перегружены"),
                    Loc.F("{0}. Груз не успевает: стройте транзитные узлы или депо ближе к добыче (режим карты «Снабжение»)", string.Join(", ", jam.GetRange(0, Mathf.Min(3, jam.Count)))),
                    NotificationCenter.Kind.Warning, 8f);

            if (lost > 1f && !s_overflowWarned)
            {
                s_overflowWarned = true;
                NotificationCenter.Show(Loc.T("Склады систем переполнены"),
                    Loc.F("Пропало {0:0} груза: он не дошёл до хаба. Расширьте коридоры или постройте депо", lost),
                    NotificationCenter.Kind.Warning, 8f);
            }
            else if (lost <= 0f) s_overflowWarned = false;
        }
    }
}
