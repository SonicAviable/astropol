using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Авторазведка научных кораблей игрока. Корабль с включённым режимом сам выбирает ближайшую
    /// неизученную ничейную систему на границе известного космоса и летит её исследовать, затем следующую.
    ///   • Обходит системы, где игрок видит вооружённого врага (флоты, пираты, левиафаны), и владения врагов.
    ///   • Два разведчика не берут одну и ту же цель.
    ///   • Когда исследовать поблизости нечего, режим выключается с уведомлением.
    ///   • Любой ручной приказ кораблю выключает авторазведку.
    /// </summary>
    public class AutoExplore : MonoBehaviour
    {
        private const int SearchDepth = 14;
        private float _timer;

        private void Update()
        {
            if (!UIManager.IsGameStarted) return;
            var tm = TimeManager.Instance;
            if (tm != null && tm.CurrentSpeed == 0) return;
            if ((_timer -= Time.unscaledDeltaTime) > 0f) return;
            _timer = 0.35f;
            Tick();
        }

        public static bool AnyOn(IEnumerable<FleetView> fleets)
        {
            foreach (var f in fleets) if (f?.Data != null && f.Data.AutoExplore) return true;
            return false;
        }

        /// <summary>Включить/выключить авторазведку у научных кораблей из списка.</summary>
        public static void Toggle(IEnumerable<FleetView> fleets)
        {
            var list = new List<FleetView>();
            foreach (var f in fleets)
                if (f?.Data != null && !f.Data.Destroyed && f.Data.Type == FleetType.Science && f.Data.OwnerId == 0) list.Add(f);
            if (list.Count == 0) return;
            bool on = !AnyOn(list);
            foreach (var f in list)
            {
                f.Data.AutoExplore = on;
                if (on && f.Data.SurveyTargetSystemId < 0 && f.Data.State == FleetState.Orbiting)
                {
                    // Включили — забываем прежний маршрут, корабль сам выберет цель
                    f.Data.Path.Clear();
                    f.Data.OrderQueue.Clear();
                    f.Data.HasPlayerOrder = false;
                }
            }
            if (on) Tick();
        }

        private static void Tick()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;

            var claimed = new HashSet<int>();
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d != null && !d.Destroyed && d.OwnerId == 0 && d.Type == FleetType.Science && d.SurveyTargetSystemId >= 0)
                    claimed.Add(d.SurveyTargetSystemId);
            }

            foreach (var f in new List<FleetView>(fm.AllFleets))
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || !d.AutoExplore || d.OwnerId != 0 || d.Type != FleetType.Science) continue;
                if (d.InCombat || d.State != FleetState.Orbiting || d.Path.Count > 0 || d.OrderQueue.Count > 0) continue;

                // Цель успел исследовать кто-то другой — выбираем новую
                if (d.SurveyTargetSystemId >= 0)
                {
                    var t = EmpireStats.GetSystem(d.SurveyTargetSystemId);
                    if (t != null && !t.IsSurveyed && fm.OrderSurveySystem(t.Id, f)) continue;
                    claimed.Remove(d.SurveyTargetSystemId);
                    d.SurveyTargetSystemId = -1;
                }

                int target = FindTarget(d.CurrentSystemId, claimed);
                if (target < 0)
                {
                    d.AutoExplore = false;
                    NotificationCenter.Show("Авторазведка завершена",
                        $"{d.Name}: поблизости не осталось доступных неизученных систем", NotificationCenter.Kind.Info, 5f);
                    continue;
                }
                if (fm.OrderSurveySystem(target, f)) claimed.Add(target);
            }
        }

        /// <summary>Ближайшая по прыжкам неизученная ничейная система на краю известного, без видимой угрозы.</summary>
        private static int FindTarget(int from, HashSet<int> claimed)
        {
            var systems = EmpireStats.Systems;
            if (from < 0 || from >= systems.Count) return -1;
            var dist = new Dictionary<int, int> { [from] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            int best = -1;
            float bestScore = float.MinValue;
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                int dd = dist[id];
                var sys = systems[id];
                if (Candidate(sys, claimed))
                {
                    float score = -dd * 3f + Mathf.Min(sys.ConnectedSystemIds.Count, 5) * 0.4f;
                    if (score > bestScore) { bestScore = score; best = id; }
                }
                if (dd >= SearchDepth) continue;
                // Сквозь владения врага и системы с видимой угрозой не прокладываем путь
                if (id != from && Dangerous(sys)) continue;
                foreach (int n in sys.ConnectedSystemIds)
                {
                    if (n < 0 || n >= systems.Count || dist.ContainsKey(n)) continue;
                    dist[n] = dd + 1;
                    queue.Enqueue(n);
                }
            }
            return best;
        }

        private static bool Candidate(StarSystem sys, HashSet<int> claimed)
        {
            if (sys.IsSurveyed || sys.OwnerId != -1 || claimed.Contains(sys.Id) || Dangerous(sys)) return false;
            if (Vision.PlayerKnows(sys.Id)) return true;
            foreach (int n in sys.ConnectedSystemIds) if (Vision.PlayerKnows(n)) return true;
            return false;
        }

        private static bool Dangerous(StarSystem sys)
        {
            if (sys.OwnerId > 0 && Diplomacy.AtWar(0, sys.OwnerId)) return true;
            var fm = FleetManager.Instance;
            if (fm == null || !Vision.PlayerSees(sys.Id)) return false;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.CurrentSystemId != sys.Id || d.State == FleetState.InHyperlane) continue;
                if (d.Type == FleetType.Military && d.Damage > 0f && Diplomacy.AtWar(0, d.OwnerId)) return true;
            }
            return false;
        }
    }
}
