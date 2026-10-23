using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>Типы узлов силы — особых систем, чей контроль даёт бонус всей империи.</summary>
    public enum LandmarkType { Gateway = 0, Pulsar = 1, Monument = 2, Bastion = 3, Beacon = 4, Deposit = 5 }

    /// <summary>
    /// Узлы силы: несколько особых систем на карте (4 / 6 / 9 по размеру галактики). Бонус действует, пока
    /// у системы стоит ваш форпост, поэтому за узлы идёт борьба: их ищут разведкой, занимают, отбивают и обороняют.
    ///   • Врата Предтеч — +25% к скорости гиперпрыжков всех ваших флотов;
    ///   • Пульсар Ирис — +12 науки в месяц;
    ///   • Монумент Первых — +1,5 влияния в месяц (в обход потолка влияния);
    ///   • Цитадель Древних — +25% корпуса, брони и щитов всех ваших звёздных баз;
    ///   • Маяк Дальнего Света — обзор вокруг системы на два прыжка;
    ///   • Кладезь Недр — +10 Гелия-3 и +10 титана в месяц.
    /// Технология «Археотехнология» усиливает все бонусы узлов на 50%. ИИ ценит узлы при выборе систем.
    /// </summary>
    public static class Landmarks
    {
        public const int TypeCount = 6;
        public const float GatewaySpeed = 0.25f, PulsarScience = 12f, MonumentInfluence = 1.5f,
                           BastionDurability = 0.25f, DepositEnergy = 10f, DepositMinerals = 10f;
        public const int BeaconJumps = 2;

        // ==================== ОПИСАНИЯ ====================

        public static string Name(int type) => type switch
        {
            0 => Loc.T("Врата Предтеч"),
            1 => Loc.T("Пульсар Ирис"),
            2 => Loc.T("Монумент Первых"),
            3 => Loc.T("Цитадель Древних"),
            4 => Loc.T("Маяк Дальнего Света"),
            5 => Loc.T("Кладезь Недр"),
            _ => Loc.T("Узел силы")
        };

        public static string Effect(int type) => type switch
        {
            0 => Loc.F("+{0:0}% к скорости гиперпрыжков всех ваших флотов", GatewaySpeed * 100f),
            1 => Loc.F("+{0:0} науки в месяц", PulsarScience),
            2 => Loc.F("+{0:0.#} влияния в месяц", MonumentInfluence),
            3 => Loc.F("+{0:0}% корпуса, брони и щитов всех ваших звёздных баз", BastionDurability * 100f),
            4 => Loc.F("обзор вокруг системы на {0} прыжка", BeaconJumps),
            5 => Loc.F("+{0:0} Гелия-3 и +{1:0} титана в месяц", DepositEnergy, DepositMinerals),
            _ => ""
        };

        public static string Lore(int type) => type switch
        {
            0 => Loc.T("Кольцо из чёрного металла вокруг двойной звезды. Корабли, прошедшие сквозь него, выходят на другом краю коридора раньше, чем успевают заметить, что двигались."),
            1 => Loc.T("Нейтронная звезда, мигающая строго по расписанию. Сигнал слишком упорядочен для природы: учёные расшифровывают его уже сорок лет."),
            2 => Loc.T("Гигантский чёрный обелиск, обращённый к ядру галактики. На нём ничего не написано, но стоящие рядом начинают понимать друг друга без слов."),
            3 => Loc.T("Ярус за ярусом брошенные орудийные платформы. Они до сих пор слушаются тех, кто знает правильные коды."),
            4 => Loc.T("Свет этого маяка виден за сотни парсеков. Тот, кто владеет им, видит всё, что виден самому свету."),
            5 => Loc.T("Планетоид, выпотрошенный изнутри и набитый концентратом, о котором шахтёрские династии рассказывают сказки."),
            _ => ""
        };

        public static Color Color(int type) => type switch
        {
            0 => new Color(0.55f, 0.80f, 1.00f),
            1 => new Color(0.42f, 0.98f, 0.62f),
            2 => new Color(1.00f, 0.55f, 0.66f),
            3 => new Color(1.00f, 0.66f, 0.36f),
            4 => new Color(1.00f, 0.92f, 0.45f),
            5 => new Color(0.38f, 0.92f, 0.86f),
            _ => UnityEngine.Color.white
        };

        // ==================== СПИСОК УЗЛОВ ====================

        private static readonly List<StarSystem> s_all = new List<StarSystem>();
        private static StarSystem s_first;
        private static int s_count = -1;

        /// <summary>Системы с узлами силы (кэш пересобирается при смене галактики).</summary>
        public static IReadOnlyList<StarSystem> All
        {
            get
            {
                var src = EmpireStats.Systems;
                var first = src.Count > 0 ? src[0] : null;
                if (s_count != src.Count || !ReferenceEquals(s_first, first))
                {
                    s_all.Clear();
                    foreach (var s in src) if (s.Landmark >= 0) s_all.Add(s);
                    s_count = src.Count;
                    s_first = first;
                }
                return s_all;
            }
        }

        /// <summary>Сбросить кэш (после генерации или загрузки галактики).</summary>
        public static void Invalidate() { s_count = -1; s_first = null; s_all.Clear(); }

        /// <summary>Узел под контролем империи: её форпост стоит в системе.</summary>
        public static bool Controls(int owner, StarSystem s)
            => s != null && s.Landmark >= 0 && owner >= 0 && s.OwnerId == owner && s.HasStarbase;

        private static float Mult(int owner) => EmpireBonuses.For(owner).LandmarkMult;

        private static int Held(int owner, int type)
        {
            int n = 0;
            foreach (var s in All) if (s.Landmark == type && Controls(owner, s)) n++;
            return n;
        }

        // ==================== ЭФФЕКТЫ ====================

        public static float HyperSpeedMult(int owner)
        {
            if (owner < 0 || All.Count == 0) return 1f;
            int n = Held(owner, (int)LandmarkType.Gateway);
            return n == 0 ? 1f : 1f + GatewaySpeed * n * Mult(owner);
        }

        public static float Science(int owner)
        {
            if (owner < 0 || All.Count == 0) return 0f;
            return Held(owner, (int)LandmarkType.Pulsar) * PulsarScience * Mult(owner);
        }

        public static float Influence(int owner)
        {
            if (owner < 0 || All.Count == 0) return 0f;
            return Held(owner, (int)LandmarkType.Monument) * MonumentInfluence * Mult(owner);
        }

        public static float StarbaseMult(int owner)
        {
            if (owner < 0 || All.Count == 0) return 1f;
            int n = Held(owner, (int)LandmarkType.Bastion);
            return n == 0 ? 1f : 1f + BastionDurability * n * Mult(owner);
        }

        public static float Energy(int owner, EmpireBonuses b)
        {
            if (owner < 0 || All.Count == 0) return 0f;
            return Held(owner, (int)LandmarkType.Deposit) * DepositEnergy * (b != null ? b.LandmarkMult : 1f);
        }

        public static float Minerals(int owner, EmpireBonuses b)
        {
            if (owner < 0 || All.Count == 0) return 0f;
            return Held(owner, (int)LandmarkType.Deposit) * DepositMinerals * (b != null ? b.LandmarkMult : 1f);
        }

        /// <summary>Системы, которые маяки владельца держат на виду (сами маяки и соседи в радиусе).</summary>
        public static void AddBeaconVision(int owner, HashSet<int> set)
        {
            if (owner < 0) return;
            foreach (var s in All)
            {
                if (s.Landmark != (int)LandmarkType.Beacon || !Controls(owner, s)) continue;
                SpreadFrom(set, s.Id, BeaconJumps);
            }
        }

        private static void SpreadFrom(HashSet<int> set, int from, int jumps)
        {
            var systems = EmpireStats.Systems;
            if (from < 0 || from >= systems.Count) return;
            set.Add(from);
            if (jumps <= 0) return;
            foreach (int n in systems[from].ConnectedSystemIds)
            {
                if (n < 0 || n >= systems.Count) continue;
                if (jumps == 1) set.Add(n);
                else SpreadFrom(set, n, jumps - 1);
            }
        }

        /// <summary>Ценность узла для ИИ при выборе систем (в тех же единицах, что и залежи).</summary>
        public static float AIValue(StarSystem s)
        {
            if (s == null || s.Landmark < 0) return 0f;
            return s.Landmark switch
            {
                (int)LandmarkType.Deposit => 22f,
                (int)LandmarkType.Pulsar => 20f,
                (int)LandmarkType.Monument => 20f,
                (int)LandmarkType.Gateway => 16f,
                (int)LandmarkType.Bastion => 14f,
                _ => 10f
            };
        }

        // ==================== РАССТАНОВКА ====================

        /// <summary>Сколько узлов на галактику такого размера.</summary>
        public static int CountFor(int starCount) => Mathf.Clamp(Mathf.RoundToInt(starCount / 25f), 4, 9);

        /// <summary>
        /// Расставить узлы: подальше от столицы игрока (не ближе трёх прыжков), подальше друг от друга,
        /// не на чужих владениях. Типы перемешиваются так, чтобы встретились все по разу, лишние — случайные.
        /// </summary>
        public static void Place(IList<StarSystem> systems)
        {
            Invalidate();
            if (systems == null || systems.Count < 12) return;
            foreach (var s in systems) s.Landmark = -1;

            // Прыжки от столицы игрока
            var dist = new Dictionary<int, int> { [systems[0].Id] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(systems[0].Id);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int n in systems[cur].ConnectedSystemIds)
                {
                    if (n < 0 || n >= systems.Count || dist.ContainsKey(n)) continue;
                    dist[n] = dist[cur] + 1;
                    queue.Enqueue(n);
                }
            }

            var candidates = new List<StarSystem>();
            foreach (var s in systems)
            {
                if (s.OwnerId >= 0 || s.ConnectedSystemIds.Count < 2) continue;
                if (!dist.TryGetValue(s.Id, out int d) || d < 3) continue;
                candidates.Add(s);
            }

            int count = Mathf.Min(CountFor(systems.Count), candidates.Count);
            var chosen = new List<StarSystem>();
            var anchors = new List<Vector3> { systems[0].Position };
            for (int i = 0; i < count; i++)
            {
                StarSystem best = null;
                float bestScore = -1f;
                foreach (var c in candidates)
                {
                    if (chosen.Contains(c)) continue;
                    float nearest = float.MaxValue;
                    foreach (var a in anchors) nearest = Mathf.Min(nearest, Vector3.Distance(a, c.Position));
                    // Первый узел — не на самом краю карты: слишком далёкие точки штрафуются
                    float score = nearest * Random.Range(0.75f, 1.25f);
                    if (score > bestScore) { bestScore = score; best = c; }
                }
                if (best == null) break;
                chosen.Add(best);
                anchors.Add(best.Position);
            }

            var types = new List<int>();
            for (int t = 0; t < TypeCount; t++) types.Add(t);
            for (int i = types.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (types[i], types[j]) = (types[j], types[i]);
            }
            for (int i = 0; i < chosen.Count; i++)
                chosen[i].Landmark = i < types.Count ? types[i] : Random.Range(0, TypeCount);
            Invalidate();
        }
    }
}
