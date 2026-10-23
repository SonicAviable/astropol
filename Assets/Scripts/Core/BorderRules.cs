using System.Collections.Generic;

namespace StellarisClone.Core
{
    /// <summary>
    /// Границы империй. В чужую территорию корабли (любые: военные, научные, строительные) входят только:
    ///   • по договору о границах (с игроком — DealItemKind.Borders, между империями ИИ — AIRelations);
    ///   • во время войны с хозяином территории;
    ///   • союзники (оборонительный союз с игроком, коалиция ИИ) открыты друг для друга.
    /// Ничейные системы открыты всем; пираты и левиафаны границ не признают.
    /// Вольные миры (NeutralWorlds) пускают друзей, покровителя и тех, кто с ними воюет.
    /// Флот, оказавшийся в закрытой территории (договор истёк, заключён мир), может уйти: путь по территории
    /// того же хозяина, в которой он стоит, открыт — но только к доступной системе.
    /// </summary>
    public static class BorderRules
    {
        /// <summary>Пираты, левиафаны и «никто» границ не признают.</summary>
        public static bool Ignores(int mover) => mover < 0 || Threats.IsThreat(mover);

        public static bool CanEnter(int mover, int systemId) => CanEnter(mover, EmpireStats.GetSystem(systemId));

        public static bool CanEnter(int mover, StarSystem sys)
        {
            if (sys == null) return false;
            int owner = sys.OwnerId;
            if (owner == -1 || owner == mover || Ignores(mover) || Threats.IsThreat(owner)) return true;
            if (owner == NeutralWorlds.Owner) return NeutralWorlds.CanEnter(mover, sys.Id);
            if (owner < 0) return true;
            return HasAccess(mover, owner);
        }

        /// <summary>Открыта ли территория owner для кораблей mover (между империями).</summary>
        public static bool HasAccess(int mover, int owner)
        {
            if (mover == owner) return true;
            if (mover == 0 || owner == 0)
            {
                var ai = AIEmpireManager.For(mover == 0 ? owner : mover);
                if (ai == null) return true;
                return ai.AtWar || ai.HasAlliance || ai.HasAgreement(DealItemKind.Borders);
            }
            if (!AIEmpireManager.IsAI(mover) || !AIEmpireManager.IsAI(owner)) return true;
            return AIRelations.AtWar(mover, owner) || AIRelations.HasBorderAccess(mover, owner) || AICoalition.AreAllies(mover, owner);
        }

        /// <summary>
        /// Прыжок from → to: в доступную систему или — уход из закрытой территории — по территории её же хозяина.
        /// </summary>
        public static bool CanStep(int mover, StarSystem from, StarSystem to)
        {
            if (CanEnter(mover, to)) return true;
            return from != null && to != null && to.OwnerId == from.OwnerId && !CanEnter(mover, from);
        }

        /// <summary>Есть ли у mover хоть какой-то доступ к owner помимо войны (для подсказок).</summary>
        public static bool HasTreaty(int mover, int owner)
        {
            if (mover == 0 || owner == 0)
            {
                var ai = AIEmpireManager.For(mover == 0 ? owner : mover);
                return ai != null && (ai.HasAlliance || ai.HasAgreement(DealItemKind.Borders));
            }
            return AIRelations.HasBorderAccess(mover, owner) || AICoalition.AreAllies(mover, owner);
        }

        /// <summary>
        /// Расстояния в прыжках от from с учётом границ: только туда, куда mover может долететь.
        /// </summary>
        public static Dictionary<int, int> Reachable(int mover, int from, int maxDepth)
        {
            var dist = new Dictionary<int, int>();
            var systems = EmpireStats.Systems;
            if (from < 0 || from >= systems.Count) return dist;
            var q = new Queue<int>();
            dist[from] = 0;
            q.Enqueue(from);
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                int dcur = dist[cur];
                if (dcur >= maxDepth) continue;
                var cs = systems[cur];
                foreach (int n in cs.ConnectedSystemIds)
                {
                    if (n < 0 || n >= systems.Count || dist.ContainsKey(n)) continue;
                    if (!CanStep(mover, cs, systems[n])) continue;
                    dist[n] = dcur + 1;
                    q.Enqueue(n);
                }
            }
            // Системы, куда войти нельзя (проход «на выход» из закрытой территории), целью не считаются
            var closed = new List<int>();
            foreach (var kv in dist) if (kv.Key != from && !CanEnter(mover, kv.Key)) closed.Add(kv.Key);
            foreach (int id in closed) dist.Remove(id);
            return dist;
        }

        /// <summary>
        /// Сколько ничейных систем (в depth прыжках от своей территории) откроется mover, если owner откроет границу.
        /// Ноль — проход через owner ничего не даёт (или уже открыт).
        /// </summary>
        public static int BlockedFrontier(int mover, int owner, int depth = 10)
        {
            var systems = EmpireStats.Systems;
            var start = new List<int>();
            foreach (var s in systems) if (s.OwnerId == mover) start.Add(s.Id);
            if (start.Count == 0 || HasAccess(mover, owner)) return 0;
            var now = Flood(mover, start, depth, int.MinValue);
            var open = Flood(mover, start, depth, owner);
            int n = 0;
            foreach (int id in open)
                if (!now.Contains(id) && systems[id].OwnerId == -1) n++;
            return n;
        }

        private static HashSet<int> Flood(int mover, List<int> start, int depth, int openOwner)
        {
            var systems = EmpireStats.Systems;
            var dist = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (int s in start) { dist[s] = 0; q.Enqueue(s); }
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                if (dist[cur] >= depth) continue;
                var cs = systems[cur];
                foreach (int n in cs.ConnectedSystemIds)
                {
                    if (n < 0 || n >= systems.Count || dist.ContainsKey(n)) continue;
                    if (systems[n].OwnerId != openOwner && !CanStep(mover, cs, systems[n])) continue;
                    dist[n] = dist[cur] + 1;
                    q.Enqueue(n);
                }
            }
            return new HashSet<int>(dist.Keys);
        }

        /// <summary>
        /// Сохранение прежней версии: границ тогда не было, и флоты стоят где угодно. Чтобы партия не замерла,
        /// договоры о границах получают все, кто не воюет и не враждует (на обычный срок).
        /// </summary>
        public static void GrandfatherOldSave()
        {
            foreach (var ai in AIEmpireManager.Alive)
                if (!ai.AtWar && (ai.HasPact || ai.Opinion >= 0f) && !ai.HasAgreement(DealItemKind.Borders))
                    ai.Agreements.Add(new Agreement { Kind = DealItemKind.Borders, MonthsLeft = DealCatalog.TreatyMonths });
            AIRelations.OpenBordersForOldSave();
        }

        /// <summary>Чья граница закрыта для mover в системе (для уведомления), −1 — открыто.</summary>
        public static int ClosedBy(int mover, int systemId)
        {
            var s = EmpireStats.GetSystem(systemId);
            return s != null && !CanEnter(mover, s) ? s.OwnerId : -1;
        }

        /// <summary>Имя хозяина закрытой территории для игрока.</summary>
        public static string OwnerName(int owner)
            => owner == NeutralWorlds.Owner ? Loc.T("вольный мир") : Contacts.NameForPlayer(owner, Loc.T("соседняя империя"));

        private static float s_lastNotice = -10f;

        /// <summary>Подсказка игроку, почему приказ не выполнен (не чаще раза в секунду — приказ группе даёт одну).</summary>
        public static void NotifyBlocked(int targetSystemId, int closedOwner)
        {
            if (UnityEngine.Time.unscaledTime - s_lastNotice < 1f) return;
            s_lastNotice = UnityEngine.Time.unscaledTime;
            var s = EmpireStats.GetSystem(targetSystemId);
            string where = s != null ? s.Name : "";
            string body = closedOwner == NeutralWorlds.Owner
                ? Loc.F("{0}: вольный мир пускает только друзей, покровителя и тех, кто с ним воюет", where)
                : closedOwner >= 0
                    ? Loc.F("{0}: граница {1} закрыта. Нужен договор о границах, союз или война", where, OwnerName(closedOwner))
                    : Loc.F("{0}: путь лежит через закрытые границы. Нужен договор о границах, союз или война", where);
            NotificationCenter.Show(Loc.T("Граница закрыта"), body, NotificationCenter.Kind.Warning, 5f);
        }
    }
}
