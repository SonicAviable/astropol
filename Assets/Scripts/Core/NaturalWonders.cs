using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>Типы чудес природы — систем с особыми законами физики.</summary>
    public enum PhenomenonType { Magnetar = 0, Nebula = 1, IonStorm = 2, Nursery = 3, CrystalRing = 4, GravityWell = 5 }

    /// <summary>
    /// Чудеса природы: в отличие от узлов силы (бонус владельцу), они меняют правила прямо в своей системе —
    /// для любого, кто туда прилетит:
    ///   • Магнитар — щиты кораблей и звёздной базы в системе не работают;
    ///   • Туманность «Покров» — флоты внутри не видны издалека, их сенсоры не видят соседей; уклонение в бою +15;
    ///   • Ионный шторм — корабли теряют корпус каждый день (до 35%), ремонт невозможен; энергетическое оружие +25%;
    ///   • Звёздные ясли — население колоний системы растёт на 75% быстрее;
    ///   • Кристаллическое кольцо — добывающие станции системы дают вдвое больше; энергетическое оружие −30%;
    ///   • Гравитационный колодец (чёрная дыра) — всё оружие −20% урона; владелец системы получает +6 науки.
    /// </summary>
    public static class NaturalWonders
    {
        public const int TypeCount = 6;
        public const float IonHullPerDay = 0.015f, IonHullFloor = 0.35f, IonEnergyMult = 1.25f;
        public const float NurseryGrowth = 1.75f, CrystalStationMult = 2f, CrystalEnergyMult = 0.7f;
        public const float WellDamageMult = 0.8f, WellScience = 6f, NebulaEvasion = 15f;

        // ==================== ОПИСАНИЯ ====================

        public static string Name(int t) => t switch
        {
            0 => Loc.T("Магнитар"),
            1 => Loc.T("Туманность «Покров»"),
            2 => Loc.T("Ионный шторм"),
            3 => Loc.T("Звёздные ясли"),
            4 => Loc.T("Кристаллическое кольцо"),
            5 => Loc.T("Гравитационный колодец"),
            _ => Loc.T("Чудо природы")
        };

        public static string Rule(int t) => t switch
        {
            0 => Loc.T("щиты кораблей и звёздной базы в системе не работают"),
            1 => Loc.F("флоты внутри не видны издалека, их сенсоры не видят соседей; уклонение в бою +{0:0}", NebulaEvasion),
            2 => Loc.F("корабли теряют {0:0.#}% корпуса в день (до {1:0}%), ремонт невозможен; энергетическое оружие +{2:0}%",
                       IonHullPerDay * 100f, IonHullFloor * 100f, (IonEnergyMult - 1f) * 100f),
            3 => Loc.F("население колоний системы растёт на {0:0}% быстрее", (NurseryGrowth - 1f) * 100f),
            4 => Loc.F("добывающие станции системы дают вдвое больше; энергетическое оружие −{0:0}%", (1f - CrystalEnergyMult) * 100f),
            5 => Loc.F("всё оружие −{0:0}% урона; владелец системы получает +{1:0} науки в месяц", (1f - WellDamageMult) * 100f, WellScience),
            _ => ""
        };

        public static string Lore(int t) => t switch
        {
            0 => Loc.T("Мёртвая звезда с магнитным полем в триллион раз сильнее земного. Защитные поля кораблей здесь просто стекают с корпусов, как вода."),
            1 => Loc.T("Облако ионизированного газа размером с десяток звёздных систем. Внутри сенсоры слепнут, а флоты исчезают с карт, словно их и не было."),
            2 => Loc.T("Две звезды обмениваются плазмой, и пространство между ними кипит. Корабли выходят из шторма с оплавленной обшивкой, если выходят вообще."),
            3 => Loc.T("Молодое скопление, где звёзды ещё рождаются. Тёплый свет, богатые органикой миры — колонисты называют эти места колыбелью."),
            4 => Loc.T("Пояс астероидов из чистого кварца и рудных кристаллов. Лучи энергетических орудий рассеиваются в нём радужными брызгами."),
            5 => Loc.T("Горизонт событий чёрной дыры искривляет всё — свет, время и траектории снарядов. Учёные готовы на всё ради места поближе к краю."),
            _ => ""
        };

        public static Color Color(int t) => t switch
        {
            0 => new Color(0.55f, 0.75f, 1f),
            1 => new Color(0.78f, 0.55f, 1f),
            2 => new Color(1f, 0.55f, 0.3f),
            3 => new Color(1f, 0.82f, 0.45f),
            4 => new Color(0.5f, 1f, 0.9f),
            5 => new Color(0.85f, 0.6f, 1f),
            _ => UnityEngine.Color.white
        };

        public static LGIcon Icon(int t) => t switch
        {
            0 => LGIcon.Reactor,
            1 => LGIcon.Sensors,
            2 => LGIcon.Warning,
            3 => LGIcon.Population,
            4 => LGIcon.Minerals,
            5 => LGIcon.Research,
            _ => LGIcon.Star
        };

        // ==================== ЗАПРОСЫ ====================

        public static int At(int systemId)
        {
            var s = EmpireStats.GetSystem(systemId);
            return s != null ? s.Phenomenon : -1;
        }

        public static bool Is(int systemId, PhenomenonType t) => At(systemId) == (int)t;

        private static readonly List<StarSystem> s_all = new List<StarSystem>();
        private static int s_count = -1;

        public static IReadOnlyList<StarSystem> All
        {
            get
            {
                var systems = EmpireStats.Systems;
                if (s_count != systems.Count)
                {
                    s_all.Clear();
                    foreach (var s in systems) if (s.Phenomenon >= 0) s_all.Add(s);
                    s_count = systems.Count;
                }
                return s_all;
            }
        }

        public static void Invalidate() { s_count = -1; s_all.Clear(); }

        // ==================== ПРАВИЛА ====================

        /// <summary>Множитель урона оружия в бою в системе.</summary>
        public static float DamageMult(int systemId, WeaponDamageType w)
        {
            switch (At(systemId))
            {
                case (int)PhenomenonType.IonStorm: return w == WeaponDamageType.Energy ? IonEnergyMult : 1f;
                case (int)PhenomenonType.CrystalRing: return w == WeaponDamageType.Energy ? CrystalEnergyMult : 1f;
                case (int)PhenomenonType.GravityWell: return WellDamageMult;
                default: return 1f;
            }
        }

        public static float EvasionBonus(int systemId) => Is(systemId, PhenomenonType.Nebula) ? NebulaEvasion : 0f;

        public static bool ShieldsDown(int systemId) => Is(systemId, PhenomenonType.Magnetar);

        public static bool BlocksRepair(int systemId) => Is(systemId, PhenomenonType.IonStorm);

        public static float GrowthMult(PlanetData p)
            => p != null && p.ParentSystem != null && p.ParentSystem.Phenomenon == (int)PhenomenonType.Nursery ? NurseryGrowth : 1f;

        public static float StationMult(StarSystem s)
            => s != null && s.Phenomenon == (int)PhenomenonType.CrystalRing ? CrystalStationMult : 1f;

        /// <summary>Наука владельцу гравитационных колодцев (нужна звёздная база).</summary>
        public static float Science(int owner)
        {
            if (owner < 0) return 0f;
            float v = 0f;
            foreach (var s in All)
                if (s.Phenomenon == (int)PhenomenonType.GravityWell && s.OwnerId == owner && s.HasStarbase) v += WellScience;
            return v;
        }

        /// <summary>Туманность: чужой флот внутри виден, только если у наблюдателя там база или свой флот.</summary>
        public static bool HidesFleet(int viewer, FleetData d)
        {
            if (d == null || d.State == FleetState.InHyperlane || !Is(d.CurrentSystemId, PhenomenonType.Nebula)) return false;
            var s = EmpireStats.GetSystem(d.CurrentSystemId);
            if (s != null && s.OwnerId == viewer && s.HasStarbase) return false;
            var fm = FleetManager.Instance;
            if (fm != null)
                foreach (var f in fm.AllFleets)
                {
                    var o = f?.Data;
                    if (o != null && !o.Destroyed && o.OwnerId == viewer && o.State != FleetState.InHyperlane && o.CurrentSystemId == d.CurrentSystemId) return false;
                }
            return true;
        }

        /// <summary>Дальность сенсоров флота: в туманности — только своя система.</summary>
        public static int SensorJumps(FleetData d, int jumps)
            => d != null && d.State != FleetState.InHyperlane && Is(d.CurrentSystemId, PhenomenonType.Nebula) ? 0 : jumps;

        /// <summary>Ценность системы для ИИ при выборе, что занимать (в единицах залежей).</summary>
        public static float AIValue(StarSystem s)
        {
            if (s == null || s.Phenomenon < 0) return 0f;
            return s.Phenomenon switch
            {
                (int)PhenomenonType.Nursery => 14f,
                (int)PhenomenonType.CrystalRing => 10f,
                (int)PhenomenonType.GravityWell => 9f,
                (int)PhenomenonType.Nebula => 4f,
                (int)PhenomenonType.Magnetar => -2f,
                (int)PhenomenonType.IonStorm => -12f,
                _ => 0f
            };
        }

        // ==================== РАССТАНОВКА ====================

        public static int CountFor(int starCount) => Mathf.Clamp(Mathf.RoundToInt(starCount / 30f), 3, 8);

        /// <summary>
        /// Расставить чудеса: чёрные дыры становятся гравитационными колодцами (не больше двух), остальные — в свободные
        /// системы не ближе трёх прыжков от столицы игрока, не на узлах силы, подальше друг от друга.
        /// </summary>
        public static void Place(IList<StarSystem> systems)
        {
            Invalidate();
            if (systems == null || systems.Count < 12) return;
            foreach (var s in systems) s.Phenomenon = -1;

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

            int total = CountFor(systems.Count);
            var chosen = new List<StarSystem>();
            int wells = 0;
            foreach (var s in systems)
            {
                if (wells >= 2 || chosen.Count >= total) break;
                if (s.SpectralClass != StarSpectralClass.BlackHole || s.Landmark >= 0 || s.OwnerId >= 0) continue;
                if (!dist.TryGetValue(s.Id, out int d) || d < 2) continue;
                s.Phenomenon = (int)PhenomenonType.GravityWell;
                chosen.Add(s);
                wells++;
            }

            var candidates = new List<StarSystem>();
            foreach (var s in systems)
            {
                if (s.OwnerId >= 0 || s.Landmark >= 0 || s.Phenomenon >= 0 || s.ConnectedSystemIds.Count == 0) continue;
                if (s.SpectralClass == StarSpectralClass.BlackHole) continue;
                if (!dist.TryGetValue(s.Id, out int d) || d < 3) continue;
                candidates.Add(s);
            }

            // Типы: по разу каждый (кроме колодца, если он уже есть), лишние — случайные
            var types = new List<int>();
            for (int t = 0; t < TypeCount; t++) if (t != (int)PhenomenonType.GravityWell || wells == 0) types.Add(t);
            for (int i = types.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (types[i], types[j]) = (types[j], types[i]); }
            while (types.Count < total - wells) types.Add(Random.Range(0, TypeCount - 1));

            var anchors = new List<Vector3> { systems[0].Position };
            foreach (var c in chosen) anchors.Add(c.Position);
            foreach (var s in systems) if (s.Landmark >= 0) anchors.Add(s.Position);
            int ti = 0;
            while (chosen.Count < total && ti < types.Count && candidates.Count > 0)
            {
                StarSystem best = null;
                float bestScore = -1f;
                foreach (var c in candidates)
                {
                    float near = float.MaxValue;
                    foreach (var a in anchors) near = Mathf.Min(near, Vector3.Distance(a, c.Position));
                    float score = near * Random.Range(0.8f, 1.2f);
                    if (score > bestScore) { bestScore = score; best = c; }
                }
                if (best == null) break;
                best.Phenomenon = types[ti++];
                chosen.Add(best);
                candidates.Remove(best);
                anchors.Add(best.Position);
            }
            Invalidate();
        }

        // ==================== ЕЖЕДНЕВНЫЕ ЭФФЕКТЫ ====================

        /// <summary>Магнитар гасит щиты, ионный шторм жжёт корпус (вызывается раз в день).</summary>
        public static void DailyTick()
        {
            if (All.Count == 0) return;
            var fm = FleetManager.Instance;
            if (fm != null)
                foreach (var f in fm.AllFleets)
                {
                    var d = f?.Data;
                    if (d == null || d.Destroyed || d.State == FleetState.InHyperlane) continue;
                    int t = At(d.CurrentSystemId);
                    if (t == (int)PhenomenonType.Magnetar) d.ShieldPoints = 0f;
                    else if (t == (int)PhenomenonType.IonStorm)
                    {
                        float floor = d.MaxHullPoints * IonHullFloor;
                        if (d.HullPoints > floor) d.HullPoints = Mathf.Max(floor, d.HullPoints - d.MaxHullPoints * IonHullPerDay);
                    }
                }
            var cm = CombatManager.Instance;
            if (cm != null)
                foreach (var s in All)
                    if (s.Phenomenon == (int)PhenomenonType.Magnetar && s.HasStarbase)
                    {
                        var sb = cm.GetStarbase(s.Id);
                        if (sb != null) sb.Shields = 0f;
                    }
        }
    }

    /// <summary>Ежедневные эффекты чудес и уведомления об открытии (как у узлов силы).</summary>
    public class NaturalWondersWatcher : MonoBehaviour
    {
        private readonly HashSet<int> _announced = new HashSet<int>();
        private bool _init;

        private void Start()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed += HandleDay;
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
        }

        private void HandleDay(int day, int month, int year)
        {
            if (!UIManager.IsGameStarted) return;
            NaturalWonders.DailyTick();
            foreach (var s in NaturalWonders.All)
            {
                bool known = Vision.PlayerKnows(s.Id) || s.OwnerId == 0;
                if (!known) continue;
                if (!_init) { _announced.Add(s.Id); continue; }
                if (!_announced.Add(s.Id)) continue;
                NotificationCenter.Show(Loc.T("Открыто чудо природы"),
                    Loc.F("{0} · система {1}: {2}.", NaturalWonders.Name(s.Phenomenon), s.Name, NaturalWonders.Rule(s.Phenomenon)),
                    NotificationCenter.Kind.Info, 8f, s.Id);
            }
            _init = true;
        }
    }
}
