using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>Виды космической фауны.</summary>
    public enum FaunaKind { Rays = 0, Jelly = 1, Swarm = 2, Crystal = 3, Wanderer = 4 }

    /// <summary>Одна стая / колония / рой / решётка / Странник.</summary>
    [Serializable]
    public class FaunaHerd
    {
        public int Kind;
        /// <summary>Особей (скаты, медузы, кристаллиды; рой — условно).</summary>
        public int Count;
        /// <summary>Дом (медузы, кристаллиды) или система, где стая/рой сейчас стоит.</summary>
        public int Home = -1;
        /// <summary>Скаты — кольцо систем маршрута; рой — недавно посещённые системы.</summary>
        public List<int> Route = new List<int>();
        public int Leg;
        /// <summary>Перелёт: из системы → в систему.</summary>
        public int From = -1, To = -1;
        public bool Moving;
        /// <summary>Дней в текущей фазе и её длительность.</summary>
        public float Days, PhaseDays;
        public int Seed;
        public bool Studied, Tamed, Offered, Gone;
        /// <summary>Рой: заманен к этой системе (−1 — нет).</summary>
        public int Lure = -1;
        /// <summary>Кристаллиды: месяцев под вашим флагом и глава «Песни решётки».</summary>
        public int Months, Chapter;
        /// <summary>Странник: путь через галактику, пройденная доля, решение игрока (−1 — не принято).</summary>
        public List<Vector3> Path = new List<Vector3>();
        public float Progress;
        public int Choice = -1;
        /// <summary>Игроку уже сообщили о находке.</summary>
        public bool Announced;
    }

    [Serializable]
    public class FaunaSave
    {
        public bool Placed;
        public int WandererYear;
        public bool WandererSpawned;
        public List<FaunaHerd> Herds = new List<FaunaHerd>();
    }

    /// <summary>
    /// Космическая фауна: пять видов живут на карте и меняют правила там, где находятся.
    ///   • Солнечные скаты — стая кочует кольцом через 7–9 систем, отдыхает у звёзд по 4 месяца: станции системы +10% энергии;
    ///     в вашей системе можно поохотиться (сплавы, но обида у «Мистических» и «Терпеливых» империй).
    ///   • Пустотные медузы — колонии в туманностях (или у газовых гигантов): флоты уходят из системы на 25% медленнее,
    ///     научный корабль на орбите даёт науку, после изучения колонию можно приручить — тогда она держит врагов.
    ///   • Рой Искр — кочует от звезды к звезде и пьёт корону: станции системы −30% энергии, гражданские корабли
    ///     теряют корпус; военный флот разгоняет рой, а за влияние его можно заманить к соседям.
    ///   • Кристаллиды — решётка у пульсара/магнетара: владелец системы +15% к науке (по числу кристаллов),
    ///     кристалл можно разбить ради ресурсов; цепочка «Песня решётки».
    ///   • Странник — один на партию, в 2228–2236 входит в галактику и пересекает её за 16 лет:
    ///     системы рядом дают науку, колонии волнуются; игрок выбирает — следовать, изучать или атаковать.
    /// Позиции считаются здесь (игровое время), FaunaView только рисует.
    /// </summary>
    public class SpaceFauna : MonoBehaviour
    {
        public static SpaceFauna Instance { get; private set; }

        public const float RaysRestDays = 120f, RaysSpeed = 1.3f, RaysEnergy = 1.1f;
        public const float SwarmFeedDays = 90f, SwarmSpeed = 1.2f, SwarmEnergy = 0.7f, SwarmHullPerDay = 0.03f, SwarmHullFloor = 0.3f;
        public const float JellySlow = 0.75f, JellyTamedSlow = 0.6f, JellyScience = 4f;
        public const float CrystalResearch = 0.15f;
        public const float WandererDays = 16f * 360f, WandererNear = 45f, WandererScience = 4f, WandererUnrest = 8f, WandererFollowScience = 10f;
        public const int RaysCount = 7, JellyCount = 6, CrystalCount = 5, SwarmCount = 1100;
        public const float HuntAlloys = 150f, StudyScience = 150f, TameInfluence = 100f, LureInfluence = 60f;
        public const float BreakMinerals = 250f, BreakAlloys = 80f;

        private FaunaSave _s = new FaunaSave();
        private readonly HashSet<int> _wNear = new HashSet<int>();
        private int _playerFleetHurtNote = -1;

        public IReadOnlyList<FaunaHerd> Herds => _s.Herds;
        public static IReadOnlyList<FaunaHerd> All => Instance != null ? Instance._s.Herds : (IReadOnlyList<FaunaHerd>)Array.Empty<FaunaHerd>();
        /// <summary>Меняется при появлении/исчезновении стай — вид пересобирает визуал.</summary>
        public int Version { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        /// <summary>Расселить фауну сразу после старта партии, не дожидаясь первого игрового дня (игра может стоять на паузе).</summary>
        private void Update()
        {
            if (!_s.Placed && UIManager.IsGameStarted && EmpireStats.Systems.Count >= 12) Place();
        }

        private void Start()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed += HandleDay;
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
            if (Instance == this) Instance = null;
        }

        // ==================== ОПИСАНИЯ ====================

        public static string Name(int k) => k switch
        {
            0 => Loc.T("Солнечные скаты"),
            1 => Loc.T("Пустотные медузы"),
            2 => Loc.T("Рой Искр"),
            3 => Loc.T("Кристаллиды"),
            4 => Loc.T("Странник"),
            _ => Loc.T("Фауна")
        };

        public static string CountText(FaunaHerd h) => h.Kind switch
        {
            0 => Loc.F("стая · {0}", h.Count),
            1 => Loc.F("колония · {0}", h.Count),
            2 => Loc.T("≈1000 особей"),
            3 => Loc.F("решётка · {0}", h.Count),
            4 => Loc.T("один на партию"),
            _ => ""
        };

        public static Color Color(int k) => k switch
        {
            0 => new Color(0.36f, 0.95f, 0.88f),
            1 => new Color(0.84f, 0.55f, 1f),
            2 => new Color(1f, 0.76f, 0.36f),
            3 => new Color(0.62f, 0.82f, 1f),
            4 => new Color(0.5f, 0.66f, 1f),
            _ => UnityEngine.Color.white
        };

        public static LGIcon Icon(int k) => k switch
        {
            0 => LGIcon.Propulsion,
            1 => LGIcon.Sensors,
            2 => LGIcon.Energy,
            3 => LGIcon.Research,
            4 => LGIcon.Globe,
            _ => LGIcon.Star
        };

        /// <summary>Правило в системе — для подсказки.</summary>
        public static string Rule(FaunaHerd h) => h.Kind switch
        {
            0 => h.Moving ? Loc.T("стая в пути между системами")
                          : Loc.F("стая отдыхает у звезды: станции системы +{0:0}% гелия-3", (RaysEnergy - 1f) * 100f),
            1 => (h.Tamed ? Loc.F("колония приручена: враги владельца уходят из системы на {0:0}% медленнее", (1f - JellyTamedSlow) * 100f)
                          : Loc.F("флоты уходят из системы на {0:0}% медленнее", (1f - JellySlow) * 100f))
                 + Loc.F("; научный корабль на орбите — +{0:0} науки в месяц", JellyScience),
            2 => h.Moving ? Loc.T("рой летит к новой звезде")
                          : Loc.F("рой пьёт корону: станции −{0:0}% гелия-3, гражданские корабли теряют корпус; военный флот разгонит рой", (1f - SwarmEnergy) * 100f),
            3 => Loc.F("владелец системы получает +{0:0}% к науке", CrystalResearch * 100f * h.Count / CrystalCount),
            4 => Loc.F("системы рядом дают +{0:0} науки владельцу, колонии рядом волнуются (−{1:0} стабильности)", WandererScience, WandererUnrest),
            _ => ""
        };

        /// <summary>Коротко: что это за существа.</summary>
        public static string Lore(int k) => k switch
        {
            0 => Loc.T("Существа-крылья питаются светом звёзд. Стая кочует кольцом по галактике и подолгу отдыхает у звёзд."),
            1 => Loc.T("Колонии простых организмов ведут себя как одно существо. Их щупальца тянут гравитационные нити."),
            2 => Loc.T("Тысяча светящихся искр кочует от звезды к звезде и пьёт свет из короны. Под роем звезда тускнеет."),
            3 => Loc.T("Гранёные кристаллы растут из пыли у пульсара и вспыхивают, когда их проходит луч. Решётка звучит, как песня."),
            4 => Loc.T("Существо длиной в несколько звёздных систем. Раз в эпоху пересекает галактику и уходит за её край."),
            _ => ""
        };

        /// <summary>Что игрок может сделать с этой стаей.</summary>
        public static string Hint(FaunaHerd h) => h.Kind switch
        {
            0 => (h.Studied ? "" : Loc.T("Пришлите научный корабль к отдыхающей стае — изучение даст науку. ")) +
                 Loc.T("Когда стая отдыхает в вашей системе, можно беречь её или охотиться."),
            1 => (h.Studied ? "" : Loc.T("Пришлите научный корабль — изучение даст науку. ")) +
                 (h.Tamed ? Loc.T("Колония приручена.") : Loc.T("Изученную колонию в вашей системе (нужен форпост) можно приручить.")),
            2 => Loc.T("Пришлите военный флот — рой улетит. Научные и строительные корабли держите подальше."),
            3 => (h.Studied ? "" : Loc.T("Пришлите научный корабль — изучение даст науку. ")) +
                 Loc.T("Постройте форпост в системе: бонус к науке и цепочка «Песня решётки»."),
            4 => h.Choice switch
            {
                0 => Loc.T("Вы следуете за ним: +10 науки в месяц."),
                1 => Loc.T("Вы изучили его вблизи."),
                2 => Loc.T("Вы атаковали его. Соседи этого не забыли."),
                _ => Loc.T("Когда он подойдёт к вашим границам, решите: следовать, изучать или атаковать.")
            },
            _ => ""
        };

        /// <summary>Подсказка при наведении на существо.</summary>
        public static string Tooltip(FaunaHerd h)
        {
            string c = LGBuild.Hex(Color(h.Kind));
            int sys = StaySystem(h);
            string where = sys >= 0 ? Loc.F("система {0}", EmpireStats.GetSystem(sys)?.Name)
                         : h.Kind == (int)FaunaKind.Wanderer ? Loc.F("пройдено {0:0}% пути", h.Progress * 100f)
                         : Loc.T("в пути между системами");
            string tip = Loc.F("<b><color={0}>{1}</color></b> · {2}\n", c, Name(h.Kind), CountText(h)) +
                         Loc.F("<color=#8AA2A8>Космическая фауна · {0}</color>\n\n", where) +
                         Lore(h.Kind) + "\n\n" +
                         Loc.F("<color={0}>Эффект:</color> {1}\n", c, Rule(h));
            string hint = Hint(h);
            if (!string.IsNullOrEmpty(hint)) tip += Loc.F("\n<color=#8AA2A8>{0}</color>", hint);
            if (h.Studied) tip += Loc.T("\n<color=#5CE68A>Изучено</color>");
            return tip;
        }

        // ==================== ПОЗИЦИИ ====================

        private static Vector3 Pos(int id) => EmpireStats.GetSystem(id)?.Position ?? Vector3.zero;

        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        private static float Ease(float q) => q * q * (3f - 2f * q);

        /// <summary>Центр стаи в мире (без косметических смещений вида). frac — доля текущего дня.</summary>
        public static Vector3 Anchor(FaunaHerd h, float frac = 0f)
        {
            switch ((FaunaKind)h.Kind)
            {
                case FaunaKind.Rays:
                {
                    int n = h.Route.Count;
                    if (n == 0) return Pos(h.Home);
                    if (!h.Moving) return Pos(h.Route[h.Leg % n]) + Vector3.up * 5f;
                    float q = Ease(Mathf.Clamp01((h.Days + frac) / Mathf.Max(1f, h.PhaseDays)));
                    Vector3 P(int i) => Pos(h.Route[((h.Leg + i) % n + n) % n]);
                    return CatmullRom(P(-1), P(0), P(1), P(2), q) + Vector3.up * 5f;
                }
                case FaunaKind.Swarm:
                {
                    if (!h.Moving || h.To < 0) return Pos(h.Home) + Vector3.up * 1.5f;
                    float q = Ease(Mathf.Clamp01((h.Days + frac) / Mathf.Max(1f, h.PhaseDays)));
                    Vector3 a = Pos(h.From) + Vector3.up * 1.5f, b = Pos(h.To) + Vector3.up * 1.5f;
                    Vector3 m = (a + b) * 0.5f + Vector3.up * 7f;
                    return (1 - q) * (1 - q) * a + 2 * (1 - q) * q * m + q * q * b;
                }
                case FaunaKind.Wanderer:
                    return WandererPoint(h, Mathf.Clamp01(h.Progress + frac / WandererDays));
                default:
                    return Pos(h.Home);
            }
        }

        /// <summary>Точка пути Странника: u — 0 (вход) … 1 (выход).</summary>
        public static Vector3 WandererPoint(FaunaHerd h, float u)
        {
            var p = h.Path;
            int n = p.Count;
            if (n == 0) return Vector3.zero;
            if (n == 1) return p[0];
            float f = Mathf.Clamp01(u) * (n - 1);
            int seg = Mathf.Min(n - 2, Mathf.FloorToInt(f));
            float t = f - seg;
            return CatmullRom(p[Mathf.Max(0, seg - 1)], p[seg], p[seg + 1], p[Mathf.Min(n - 1, seg + 2)], t);
        }

        /// <summary>Длина отрезка пути Странника, на котором лежит u (для равных шагов между сегментами тела).</summary>
        public static float WandererSegLength(FaunaHerd h, float u)
        {
            int n = h.Path.Count;
            if (n < 2) return 1f;
            int seg = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(u) * (n - 1)), 0, n - 2);
            return Mathf.Max(1f, Vector3.Distance(h.Path[seg], h.Path[seg + 1]) * 1.06f);
        }

        /// <summary>Ближайшая к точке система (для тумана войны и подсказок).</summary>
        public static int NearestSystem(Vector3 p)
        {
            int best = -1;
            float bd = float.MaxValue;
            foreach (var s in EmpireStats.Systems)
            {
                float d = (s.Position.x - p.x) * (s.Position.x - p.x) + (s.Position.z - p.z) * (s.Position.z - p.z);
                if (d < bd) { bd = d; best = s.Id; }
            }
            return best;
        }

        /// <summary>Система, где стая сейчас «стоит» (−1 — в пути / Странник).</summary>
        public static int StaySystem(FaunaHerd h)
        {
            if (h.Gone) return -1;
            switch ((FaunaKind)h.Kind)
            {
                case FaunaKind.Rays: return !h.Moving && h.Route.Count > 0 ? h.Route[h.Leg % h.Route.Count] : -1;
                case FaunaKind.Swarm: return h.Moving ? -1 : h.Home;
                case FaunaKind.Wanderer: return -1;
                default: return h.Home;
            }
        }

        public static IEnumerable<FaunaHerd> In(int systemId)
        {
            foreach (var h in All) if (StaySystem(h) == systemId) yield return h;
        }

        // ==================== ЭФФЕКТЫ ====================

        /// <summary>Множитель гелия-3 добывающих станций системы: скаты +10%, рой −30%.</summary>
        public static float StationEnergyMult(int systemId)
        {
            float k = 1f;
            foreach (var h in In(systemId))
                if (h.Kind == (int)FaunaKind.Rays && h.Count > 0) k *= RaysEnergy;
                else if (h.Kind == (int)FaunaKind.Swarm) k *= SwarmEnergy;
            return k;
        }

        /// <summary>Скорость прыжка флота, уходящего из системы с медузами.</summary>
        public static float LeaveSpeedMult(FleetData d)
        {
            if (d == null || d.CurrentSystemId < 0) return 1f;
            foreach (var h in In(d.CurrentSystemId))
            {
                if (h.Kind != (int)FaunaKind.Jelly) continue;
                if (!h.Tamed) return JellySlow;
                int owner = EmpireStats.GetSystem(h.Home)?.OwnerId ?? -1;
                if (owner >= 0 && d.OwnerId != owner && Diplomacy.AtWar(d.OwnerId, owner)) return JellyTamedSlow;
                return 1f;
            }
            return 1f;
        }

        /// <summary>Наука в месяц: научные корабли у медуз, системы рядом со Странником, «следовать» за Странником.</summary>
        public static float Science(int owner)
        {
            var inst = Instance;
            if (inst == null) return 0f;
            float s = 0f;
            var fm = FleetManager.Instance;
            foreach (var h in inst._s.Herds)
            {
                if (h.Gone) continue;
                if (h.Kind == (int)FaunaKind.Jelly && fm != null)
                    foreach (var f in fm.AllFleets)
                    {
                        var d = f?.Data;
                        if (d != null && !d.Destroyed && d.OwnerId == owner && d.Type == FleetType.Science
                            && d.State != FleetState.InHyperlane && d.CurrentSystemId == h.Home) s += JellyScience;
                    }
                if (h.Kind == (int)FaunaKind.Wanderer && inst._s.WandererSpawned)
                {
                    foreach (int id in inst._wNear) if (EmpireStats.GetSystem(id)?.OwnerId == owner) s += WandererScience;
                    if (owner == 0 && h.Choice == 0) s += WandererFollowScience;
                }
            }
            return s;
        }

        /// <summary>Множитель науки: решётки кристаллидов в системах с вашей звёздной базой.</summary>
        public static float ResearchMult(int owner)
        {
            float k = 1f;
            foreach (var h in All)
            {
                if (h.Gone || h.Kind != (int)FaunaKind.Crystal || h.Count <= 0) continue;
                var s = EmpireStats.GetSystem(h.Home);
                if (s != null && s.OwnerId == owner && s.HasStarbase) k += CrystalResearch * h.Count / CrystalCount;
            }
            return k;
        }

        /// <summary>Странник рядом с системой (колонии волнуются).</summary>
        public static bool WandererNearSystem(int systemId) => Instance != null && Instance._wNear.Contains(systemId);

        // ==================== РАЗМЕЩЕНИЕ ====================

        private void Place()
        {
            var systems = EmpireStats.Systems;
            _s = new FaunaSave { Placed = true, WandererYear = UnityEngine.Random.Range(2228, 2237) };
            Version++;
            if (systems == null || systems.Count < 12) return;

            // Прыжки от стартовых систем империй: фауну не селим вплотную к столицам
            var dist = new Dictionary<int, int>();
            var queue = new Queue<int>();
            foreach (var s in systems) if (s.OwnerId >= 0) { dist[s.Id] = 0; queue.Enqueue(s.Id); }
            if (queue.Count == 0) { dist[systems[0].Id] = 0; queue.Enqueue(systems[0].Id); }
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int nb in systems[cur].ConnectedSystemIds)
                {
                    if (nb < 0 || nb >= systems.Count || dist.ContainsKey(nb)) continue;
                    dist[nb] = dist[cur] + 1;
                    queue.Enqueue(nb);
                }
            }
            int Jumps(StarSystem s) => dist.TryGetValue(s.Id, out int d) ? d : 99;
            bool Wild(StarSystem s, int minJumps) => s.OwnerId < 0 && Jumps(s) >= minJumps && s.SpectralClass != StarSpectralClass.BlackHole;
            var used = new HashSet<int>();

            float R = 1f;
            foreach (var s in systems) R = Mathf.Max(R, new Vector2(s.Position.x, s.Position.z).magnitude);

            // Скаты: кольцо на середине радиуса, по одной системе в каждом из 8 секторов
            {
                var route = new List<int>();
                const int bins = 8;
                float off = UnityEngine.Random.value * Mathf.PI * 2f;
                for (int b = 0; b < bins; b++)
                {
                    StarSystem best = null;
                    float bestScore = float.MaxValue;
                    foreach (var s in systems)
                    {
                        if (s.SpectralClass == StarSpectralClass.BlackHole) continue;
                        float a = Mathf.Repeat(Mathf.Atan2(s.Position.z, s.Position.x) - off, Mathf.PI * 2f);
                        if (Mathf.FloorToInt(a / (Mathf.PI * 2f / bins)) != b) continue;
                        float r = new Vector2(s.Position.x, s.Position.z).magnitude / R;
                        float score = Mathf.Abs(r - 0.6f) + UnityEngine.Random.value * 0.08f;
                        if (score < bestScore) { bestScore = score; best = s; }
                    }
                    if (best != null) route.Add(best.Id);
                }
                if (route.Count >= 4)
                {
                    var h = new FaunaHerd { Kind = (int)FaunaKind.Rays, Count = RaysCount, Route = route, Leg = UnityEngine.Random.Range(0, route.Count), Seed = UnityEngine.Random.Range(0, 99999) };
                    h.Home = route[h.Leg];
                    StartRest(h);
                    h.Days = UnityEngine.Random.Range(0f, RaysRestDays * 0.7f);
                    _s.Herds.Add(h);
                }
            }

            // Медузы: туманности «Покров», без них — газовые гиганты в глуши
            {
                var homes = new List<StarSystem>();
                foreach (var s in systems) if (s.Phenomenon == (int)PhenomenonType.Nebula && s.OwnerId < 0) homes.Add(s);
                if (homes.Count == 0)
                    foreach (var s in systems)
                        if (Wild(s, 3) && s.Planets.Exists(p => p.Type == PlanetType.GasGiant)) { homes.Add(s); if (homes.Count > 6) break; }
                Shuffle(homes);
                int n = Mathf.Min(systems.Count >= 300 ? 2 : 1, homes.Count);
                for (int i = 0; i < n; i++)
                {
                    used.Add(homes[i].Id);
                    _s.Herds.Add(new FaunaHerd { Kind = (int)FaunaKind.Jelly, Count = JellyCount, Home = homes[i].Id, Seed = UnityEngine.Random.Range(0, 99999) });
                }
            }

            // Кристаллиды: пульсары и магнетары, иначе узел «Пульсар» или чудо «Магнитар»
            {
                var homes = new List<StarSystem>();
                foreach (var s in systems)
                    if ((s.SpectralClass == StarSpectralClass.Pulsar || s.SpectralClass == StarSpectralClass.Magnetar) && Wild(s, 2) && !used.Contains(s.Id)) homes.Add(s);
                if (homes.Count == 0)
                    foreach (var s in systems)
                        if ((s.Landmark == (int)LandmarkType.Pulsar || s.Phenomenon == (int)PhenomenonType.Magnetar) && s.OwnerId < 0 && !used.Contains(s.Id)) homes.Add(s);
                Shuffle(homes);
                int n = Mathf.Min(systems.Count >= 300 ? 2 : 1, homes.Count);
                for (int i = 0; i < n; i++)
                {
                    used.Add(homes[i].Id);
                    _s.Herds.Add(new FaunaHerd { Kind = (int)FaunaKind.Crystal, Count = CrystalCount, Home = homes[i].Id, Seed = UnityEngine.Random.Range(0, 99999) });
                }
            }

            // Рой Искр: стартует в глуши, дальше кочует сам
            {
                var cands = new List<StarSystem>();
                foreach (var s in systems) if (Wild(s, 4) && !used.Contains(s.Id)) cands.Add(s);
                if (cands.Count == 0) foreach (var s in systems) if (Wild(s, 2)) cands.Add(s);
                if (cands.Count > 0)
                {
                    var start = cands[UnityEngine.Random.Range(0, cands.Count)];
                    var h = new FaunaHerd { Kind = (int)FaunaKind.Swarm, Count = SwarmCount, Home = start.Id, Seed = UnityEngine.Random.Range(0, 99999) };
                    h.Route.Add(start.Id);
                    StartFeed(h);
                    _s.Herds.Add(h);
                }
            }

            var log = new List<string>();
            foreach (var h in _s.Herds)
                log.Add($"{(FaunaKind)h.Kind} — {EmpireStats.GetSystem(StaySystem(h) >= 0 ? StaySystem(h) : h.Home)?.Name}");
            Debug.Log($"[SpaceFauna] Фауна расселена ({_s.Herds.Count}): {string.Join(", ", log)}; Странник придёт в {_s.WandererYear} г.");
        }

        private static void Shuffle<T>(List<T> l)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (l[i], l[j]) = (l[j], l[i]); }
        }

        private void SpawnWanderer()
        {
            _s.WandererSpawned = true;
            float R = 1f;
            foreach (var s in EmpireStats.Systems) R = Mathf.Max(R, new Vector2(s.Position.x, s.Position.z).magnitude);
            float a = UnityEngine.Random.value * Mathf.PI * 2f, side = UnityEngine.Random.value < 0.5f ? 1f : -1f;
            Vector3 At(float ang, float r, float y) => new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r);
            var h = new FaunaHerd { Kind = (int)FaunaKind.Wanderer, Count = 1, Seed = UnityEngine.Random.Range(0, 99999) };
            h.Path.Add(At(a, R * 1.25f, 11f));
            h.Path.Add(At(a + 0.35f * side, R * 0.7f, 10f));
            h.Path.Add(At(a + 1.2f * side, R * 0.32f, 12f));
            h.Path.Add(At(a + Mathf.PI - 0.9f * side, R * 0.38f, 9f));
            h.Path.Add(At(a + Mathf.PI - 0.25f * side, R * 0.75f, 11f));
            h.Path.Add(At(a + Mathf.PI + 0.1f * side, R * 1.25f, 10f));
            _s.Herds.Add(h);
            Version++;
            GalacticNews.Post(NewsKind.Threat, Loc.T("На окраине галактики замечен Странник — существо длиной в несколько звёздных систем. Он движется к центру."));
            NotificationCenter.Show(Loc.T("Странник вошёл в галактику"),
                Loc.T("Гигантское существо медленно пересекает галактику. Системы рядом с ним дают науку, колонии волнуются."),
                NotificationCenter.Kind.Warning, 9f);
            HudBanner.Show(Loc.T("Странник вошёл в галактику"), Loc.T("Один на всю партию · пересечёт галактику за 16 лет"), LGIcon.Globe, Color((int)FaunaKind.Wanderer));
            SFXManager.PlayDelayed(CallOf((int)FaunaKind.Wanderer), 0.5f);
        }

        // ==================== ЖИЗНЬ ====================

        private static void StartRest(FaunaHerd h)
        {
            h.Moving = false;
            h.Days = 0f;
            h.PhaseDays = RaysRestDays;
            h.Home = h.Route[h.Leg % h.Route.Count];
        }

        private static void StartFeed(FaunaHerd h)
        {
            h.Moving = false;
            h.Days = 0f;
            h.PhaseDays = SwarmFeedDays;
            h.From = h.To = -1;
        }

        private void HandleDay(int day, int month, int year)
        {
            if (!UIManager.IsGameStarted) return;
            if (!_s.Placed) Place();
            if (!_s.WandererSpawned && year >= _s.WandererYear) SpawnWanderer();

            bool newMonth = day == 1;
            foreach (var h in _s.Herds.ToArray())
            {
                if (h.Gone) continue;
                switch ((FaunaKind)h.Kind)
                {
                    case FaunaKind.Rays: TickRays(h); break;
                    case FaunaKind.Swarm: TickSwarm(h); break;
                    case FaunaKind.Jelly: TickJelly(h); break;
                    case FaunaKind.Crystal: TickCrystal(h, newMonth); break;
                    case FaunaKind.Wanderer: TickWanderer(h); break;
                }
                if (!h.Gone) CheckStudy(h);
                Announce(h);
            }
            if (_s.Herds.RemoveAll(x => x.Gone) > 0) Version++;
        }

        private void TickRays(FaunaHerd h)
        {
            h.Days += 1f;
            if (h.Days < h.PhaseDays) return;
            if (!h.Moving)
            {
                float len = 0f;
                Vector3 prev = Anchor(h);
                h.Moving = true;
                h.Days = 0f;
                h.PhaseDays = 1f;
                for (int k = 1; k <= 16; k++)
                {
                    h.Days = k / 16f;
                    Vector3 p = Anchor(h);
                    len += Vector3.Distance(prev, p);
                    prev = p;
                }
                h.Days = 0f;
                h.PhaseDays = Mathf.Max(30f, len / RaysSpeed);
                h.Home = -1;
            }
            else
            {
                h.Leg = (h.Leg + 1) % h.Route.Count;
                StartRest(h);
                var sys = EmpireStats.GetSystem(h.Home);
                if (sys != null && sys.OwnerId == 0 && h.Count > 0) OfferRays(h, sys);
            }
        }

        private void TickSwarm(FaunaHerd h)
        {
            h.Days += 1f;
            if (!h.Moving)
            {
                var fm = FleetManager.Instance;
                FleetData disperser = null;
                bool hurtPlayer = false;
                if (fm != null)
                    foreach (var f in fm.AllFleets)
                    {
                        var d = f?.Data;
                        if (d == null || d.Destroyed || d.State == FleetState.InHyperlane || d.CurrentSystemId != h.Home) continue;
                        if (d.Type == FleetType.Military && !Threats.IsThreat(d.OwnerId) && d.OwnerId >= 0)
                        {
                            disperser = d;
                            break;
                        }
                        if (d.Type != FleetType.Military)
                        {
                            float floor = d.MaxHullPoints * SwarmHullFloor;
                            if (d.HullPoints > floor)
                            {
                                d.HullPoints = Mathf.Max(floor, d.HullPoints - d.MaxHullPoints * SwarmHullPerDay);
                                if (d.OwnerId == 0) hurtPlayer = true;
                            }
                        }
                    }
                if (hurtPlayer && _playerFleetHurtNote != h.Home)
                {
                    _playerFleetHurtNote = h.Home;
                    NotificationCenter.Show(Loc.T("Рой Искр жжёт корабли"),
                        Loc.F("Гражданские корабли у звезды {0} теряют корпус. Уведите их или пришлите военный флот.", EmpireStats.GetSystem(h.Home)?.Name),
                        NotificationCenter.Kind.Warning, 6f, h.Home);
                    SFXManager.PlayDelayed(Audio.Sfx.FaunaSwarm, 0.35f);
                }
                if (disperser != null || h.Days >= h.PhaseDays)
                {
                    int at = h.Home;
                    StartSwarmFlight(h);
                    if (disperser != null && disperser.OwnerId == 0 && h.Moving)
                        NotificationCenter.Show(Loc.T("Рой Искр разогнан"),
                            Loc.F("{0} спугнул рой у звезды {1}.", disperser.Name, EmpireStats.GetSystem(at)?.Name), NotificationCenter.Kind.Success, 5f, at);
                }
            }
            else if (h.Days >= h.PhaseDays)
            {
                h.Home = h.To;
                h.Route.Add(h.To);
                while (h.Route.Count > 6) h.Route.RemoveAt(0);
                if (h.Lure == h.Home) h.Lure = -1;
                StartFeed(h);
                _playerFleetHurtNote = -1;
                if (h.Lure >= 0) { StartSwarmFlight(h); return; }
                var sys = EmpireStats.GetSystem(h.Home);
                if (sys != null && sys.OwnerId == 0) OfferSwarm(h, sys);
            }
        }

        private static void StartSwarmFlight(FaunaHerd h)
        {
            var systems = EmpireStats.Systems;
            int target = -1;
            if (h.Lure >= 0 && h.Lure < systems.Count) target = h.Lure;
            else
            {
                // Соседи в 1–3 прыжках, где рой недавно не кормился
                var dist = new Dictionary<int, int> { [h.Home] = 0 };
                var q = new Queue<int>();
                q.Enqueue(h.Home);
                var cands = new List<int>();
                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                    if (dist[cur] >= 3) continue;
                    foreach (int nb in systems[cur].ConnectedSystemIds)
                    {
                        if (nb < 0 || nb >= systems.Count || dist.ContainsKey(nb)) continue;
                        dist[nb] = dist[cur] + 1;
                        q.Enqueue(nb);
                        if (systems[nb].SpectralClass != StarSpectralClass.BlackHole && !h.Route.Contains(nb)) cands.Add(nb);
                    }
                }
                if (cands.Count == 0) foreach (var kv in dist) if (kv.Key != h.Home) cands.Add(kv.Key);
                if (cands.Count > 0) target = cands[UnityEngine.Random.Range(0, cands.Count)];
            }
            if (target < 0 || target == h.Home) { StartFeed(h); return; }
            h.From = h.Home;
            h.To = target;
            h.Moving = true;
            h.Days = 0f;
            h.PhaseDays = Mathf.Max(25f, Vector3.Distance(Pos(h.From), Pos(h.To)) / SwarmSpeed);
        }

        private void TickJelly(FaunaHerd h)
        {
            var sys = EmpireStats.GetSystem(h.Home);
            if (sys == null || h.Offered || !h.Studied || h.Tamed) return;
            if (sys.OwnerId == 0 && sys.HasStarbase) { h.Offered = true; OfferTame(h, sys); }
        }

        private void TickCrystal(FaunaHerd h, bool newMonth)
        {
            var sys = EmpireStats.GetSystem(h.Home);
            if (sys == null) return;
            bool mine = sys.OwnerId == 0 && sys.HasStarbase;
            if (!mine) return;
            if (!h.Offered) { h.Offered = true; OfferCrystal(h, sys); }
            if (!newMonth) return;
            h.Months++;
            if (h.Chapter == 0 && h.Months >= 12) { h.Chapter = 1; LatticeSong(h, sys, 1); }
            else if (h.Chapter == 1 && h.Months >= 30) { h.Chapter = 2; LatticeSong(h, sys, 2); }
            else if (h.Chapter == 2 && h.Months >= 48) { h.Chapter = 3; LatticeSong(h, sys, 3); }
        }

        private void TickWanderer(FaunaHerd h)
        {
            h.Progress += 1f / WandererDays;
            _wNear.Clear();
            Vector3 head = Anchor(h);
            foreach (var s in EmpireStats.Systems)
            {
                float dx = s.Position.x - head.x, dz = s.Position.z - head.z;
                if (dx * dx + dz * dz < WandererNear * WandererNear) _wNear.Add(s.Id);
            }
            if (h.Choice < 0 && !h.Offered)
                foreach (var s in EmpireStats.Systems)
                {
                    if (s.OwnerId != 0) continue;
                    float dx = s.Position.x - head.x, dz = s.Position.z - head.z;
                    if (dx * dx + dz * dz > 90f * 90f) continue;
                    h.Offered = true;
                    OfferWanderer(h, s);
                    break;
                }
            if (h.Progress >= 1f)
            {
                h.Gone = true;
                _wNear.Clear();
                GalacticNews.Post(NewsKind.Threat, Loc.T("Странник ушёл за край галактики. Учёные ещё долго будут спорить, куда он направлялся."));
            }
        }

        // ==================== ИЗУЧЕНИЕ И НАХОДКИ ====================

        /// <summary>Научный корабль игрока на орбите рядом с фауной — разовая награда и рассказ.</summary>
        private void CheckStudy(FaunaHerd h)
        {
            if (h.Studied || h.Kind == (int)FaunaKind.Wanderer || h.Kind == (int)FaunaKind.Swarm) return;
            int sysId = StaySystem(h);
            if (sysId < 0) return;
            var fm = FleetManager.Instance;
            if (fm == null) return;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.OwnerId != 0 || d.Type != FleetType.Science) continue;
                if (d.State == FleetState.InHyperlane || d.CurrentSystemId != sysId) continue;
                h.Studied = true;
                TechnologyManager.Instance?.AddInstantScience(StudyScience);
                var sys = EmpireStats.GetSystem(sysId);
                Present($"fauna_study_{h.Kind}", Loc.F("Изучены: {0}", Name(h.Kind)), StudyLore(h.Kind), sys,
                    new EventOption(Loc.T("Занести в архив"), Loc.F("+{0:0} очков науки сразу", StudyScience), null));
                return;
            }
        }

        private static string StudyLore(int k) => k switch
        {
            0 => Loc.T("Скаты питаются светом и не боятся кораблей. Их крылья — живые солнечные паруса толщиной в несколько атомов. Когда стая отдыхает у звезды, её поле рассеивает излучение прямо на наши станции."),
            1 => Loc.T("Медузы — колонии простых организмов, которые вместе ведут себя как одно существо. Их щупальца тянут гравитационные нити: корабли, уходящие из системы, будто вязнут в сиропе."),
            3 => Loc.T("Кристаллиды растут из пыли вокруг пульсара. Каждый проход луча заставляет их звенеть, и решётка отвечает ему эхом, складывающимся в сложный ритм."),
            _ => ""
        };

        private void Announce(FaunaHerd h)
        {
            if (h.Announced || h.Gone) return;
            int sysId = h.Kind == (int)FaunaKind.Wanderer ? NearestSystem(Anchor(h)) : StaySystem(h) >= 0 ? StaySystem(h) : NearestSystem(Anchor(h));
            if (sysId < 0 || !Vision.PlayerKnows(sysId)) return;
            h.Announced = true;
            if (h.Kind == (int)FaunaKind.Wanderer) return;   // о Страннике уже сообщили при появлении
            var sys = EmpireStats.GetSystem(sysId);
            NotificationCenter.Show(Loc.T("Обнаружена космическая фауна"),
                Loc.F("{0} · система {1}: {2}.", Name(h.Kind), sys?.Name, Rule(h)), NotificationCenter.Kind.Info, 8f, sysId);
            HudBanner.Show(Loc.T("Обнаружена космическая фауна"), Loc.F("{0} · система {1}", Name(h.Kind), sys?.Name), Icon(h.Kind), Color(h.Kind), sysId);
            SFXManager.PlayDelayed(CallOf(h.Kind), 0.4f);
        }

        /// <summary>Голос вида (Resources/Audio/Sfx/Fauna*, см. Tools/fauna_sfx).</summary>
        public static Audio.Sfx CallOf(int kind) => kind switch
        {
            (int)FaunaKind.Rays => Audio.Sfx.FaunaRays,
            (int)FaunaKind.Jelly => Audio.Sfx.FaunaJelly,
            (int)FaunaKind.Swarm => Audio.Sfx.FaunaSwarm,
            (int)FaunaKind.Crystal => Audio.Sfx.FaunaCrystal,
            _ => Audio.Sfx.FaunaWanderer
        };

        // ==================== СОБЫТИЯ ====================

        private static void Present(string id, string title, string text, StarSystem sys, params EventOption[] options)
        {
            var aes = AnomalyEventSystem.Instance;
            if (aes == null) { if (options.Length > 0) options[0].OnSelect?.Invoke(); return; }   // без окна событий — первый (мирный) вариант
            var ev = new GameEventData(id, title, text, AnomalyType.AlienFauna, new List<EventOption>(options));
            if (sys != null) ev.Subtitle = Loc.F("система {0}", sys.Name);
            aes.Present(ev);
        }

        private static EconomyManager Eco => EconomyManager.Instance;

        private static void Give(float minerals = 0f, float alloys = 0f, float influence = 0f)
        {
            var eco = Eco;
            if (eco == null) return;
            eco.Minerals = Mathf.Max(0f, eco.Minerals + minerals);
            eco.Alloys = Mathf.Max(0f, eco.Alloys + alloys);
            eco.Influence = Mathf.Max(0f, eco.Influence + influence);
            eco.RaiseResourcesChanged();
        }

        private void OfferRays(FaunaHerd h, StarSystem sys)
        {
            Present("fauna_rays_rest", Loc.T("Скаты у нашей звезды"),
                Loc.F("Стая солнечных скатов ({0}) опустилась отдохнуть у звезды {1}. Пока они здесь, станции системы ловят больше света. Охотники уже спрашивают разрешения: панцирь ската — отличный материал для сплавов.", h.Count, sys.Name),
                sys,
                new EventOption(Loc.T("Беречь стаю"), Loc.F("Станции системы +{0:0}% гелия-3, пока стая отдыхает", (RaysEnergy - 1f) * 100f), null),
                new EventOption(Loc.T("Охотиться"),
                    Loc.F("+{0:0} сплавов; стая уменьшится на 2; «Мистические» и «Терпеливые» империи осудят охоту", HuntAlloys),
                    () =>
                    {
                        Give(alloys: HuntAlloys);
                        h.Count = Mathf.Max(0, h.Count - 2);
                        foreach (var ai in AIEmpireManager.Alive)
                            if (Contacts.PlayerMet(ai.OwnerId) && (ai.Personality == AIPersonality.Mystic || ai.Personality == AIPersonality.Gardener))
                                ai.AddMemory("incident", -10f);
                        if (h.Count <= 0)
                        {
                            h.Gone = true;
                            GalacticNews.Post(NewsKind.Society, Loc.T("Последние солнечные скаты погибли от рук охотников."), sys.Id, true);
                        }
                        if (Instance != null) Instance.Version++;
                    }));
        }

        private void OfferSwarm(FaunaHerd h, StarSystem sys)
        {
            Present("fauna_swarm_arrive", Loc.T("Рой Искр у нашей звезды"),
                Loc.F("Тысяча светящихся искр закружилась в короне звезды {0}. Свет тускнеет, станции теряют гелий-3, а гражданские корабли рядом обгорают. Военный флот разгонит рой. Или его можно увести к соседям.", sys.Name),
                sys,
                new EventOption(Loc.T("Пришлём флот"), Loc.T("Рой уйдёт, как только в систему прилетит военный флот"), null),
                new EventOption(Loc.T("Заманить к соседям"),
                    Loc.F("−{0:0} влияния; рой сразу улетит к ближайшей системе другой империи", LureInfluence),
                    () =>
                    {
                        Eco?.TrySpend(0f, 0f, 0f, LureInfluence);
                        int t = NearestRivalSystem(h.Home);
                        if (t < 0) return;
                        h.Lure = t;
                        if (!h.Moving) StartSwarmFlight(h);   // в полёте — свернёт к цели по прилёте
                    },
                    Loc.F("Нужно {0:0} влияния", LureInfluence),
                    () => Eco != null && Eco.CanAfford(0f, 0f, 0f, LureInfluence) && NearestRivalSystem(h.Home) >= 0));
        }

        private static int NearestRivalSystem(int from)
        {
            var systems = EmpireStats.Systems;
            if (from < 0 || from >= systems.Count) return -1;
            var dist = new Dictionary<int, int> { [from] = 0 };
            var q = new Queue<int>();
            q.Enqueue(from);
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                var s = systems[cur];
                if (s.OwnerId > 0 && s.OwnerId <= AIEmpireManager.RivalCount && s.HasStarbase) return cur;
                if (dist[cur] >= 8) continue;
                foreach (int nb in s.ConnectedSystemIds)
                {
                    if (nb < 0 || nb >= systems.Count || dist.ContainsKey(nb)) continue;
                    dist[nb] = dist[cur] + 1;
                    q.Enqueue(nb);
                }
            }
            return -1;
        }

        private void OfferTame(FaunaHerd h, StarSystem sys)
        {
            Present("fauna_jelly_tame", Loc.T("Приручение медуз"),
                Loc.F("Наши ксенобиологи научились говорить с колонией медуз у звезды {0} на языке гравитационных импульсов. Колонию можно приручить: она перестанет держать наши флоты и будет вязать корабли врагов.", sys.Name),
                sys,
                new EventOption(Loc.T("Приручить"),
                    Loc.F("−{0:0} влияния; наши флоты уходят без задержки, враги — на {1:0}% медленнее", TameInfluence, (1f - JellyTamedSlow) * 100f),
                    () => { Eco?.TrySpend(0f, 0f, 0f, TameInfluence); h.Tamed = true; },
                    Loc.F("Нужно {0:0} влияния", TameInfluence),
                    () => Eco != null && Eco.CanAfford(0f, 0f, 0f, TameInfluence)),
                new EventOption(Loc.T("Оставить их в покое"), Loc.T("Колония продолжит задерживать все флоты"), null));
        }

        private void OfferCrystal(FaunaHerd h, StarSystem sys)
        {
            Present("fauna_crystal_own", Loc.T("Решётка кристаллидов"),
                Loc.F("Вокруг звезды {0} кружат кристаллиды. Когда луч пульсара проходит по ним, они вспыхивают, и наши приборы ловят странный ритм. Шахтёры предлагают разбить один кристалл: такого чистого сырья нет нигде.", sys.Name),
                sys,
                new EventOption(Loc.T("Слушать решётку"), Loc.F("Наука +{0:0}%, пока система наша", CrystalResearch * 100f * h.Count / CrystalCount), null),
                new EventOption(Loc.T("Разбить один кристаллид"),
                    Loc.F("+{0:0} титана, +{1:0} сплавов; бонус к науке упадёт на {2:0}%", BreakMinerals, BreakAlloys, CrystalResearch * 100f / CrystalCount),
                    () =>
                    {
                        Give(minerals: BreakMinerals, alloys: BreakAlloys);
                        h.Count = Mathf.Max(0, h.Count - 1);
                        if (Instance != null) Instance.Version++;
                    }));
        }

        private void LatticeSong(FaunaHerd h, StarSystem sys, int chapter)
        {
            if (h.Count <= 0) return;
            string chain = Loc.T("Песня решётки");
            GameEventData ev = null;
            if (chapter == 1)
                ev = new GameEventData("fauna_song_1", Loc.T("Песня решётки · I"),
                    Loc.F("Год наблюдений у звезды {0}: ритм решётки не случаен. Вспышки складываются в повторяющиеся фразы, будто кристаллиды разучивают мелодию.", sys.Name),
                    AnomalyType.AlienFauna, new List<EventOption>
                    {
                        new EventOption(Loc.T("Записать всё"), Loc.T("+200 очков науки сразу"), () => TechnologyManager.Instance?.AddInstantScience(200f)),
                    });
            else if (chapter == 2)
                ev = new GameEventData("fauna_song_2", Loc.T("Песня решётки · II"),
                    Loc.T("Фразы решётки совпадают с сигналами, которые наши первые радиотелескопы приняли века назад и сочли шумом. Кто-то учил кристаллиды задолго до нас."),
                    AnomalyType.AlienFauna, new List<EventOption>
                    {
                        new EventOption(Loc.T("Ответить сигналом"), Loc.T("+50 влияния: соседи восхищены"), () => Give(influence: 50f)),
                        new EventOption(Loc.T("Молчать и слушать"), Loc.T("+300 очков науки сразу"), () => TechnologyManager.Instance?.AddInstantScience(300f)),
                    });
            else
                ev = new GameEventData("fauna_song_3", Loc.T("Песня решётки · III"),
                    Loc.T("Решётка допела. Последняя фраза оказалась схемой: так древний разум записывал свои уравнения в живой кристалл. Наши учёные расшифровали первые строки."),
                    AnomalyType.AlienFauna, new List<EventOption>
                    {
                        new EventOption(Loc.T("Расшифровать"), Loc.T("+600 очков науки и +25% к скорости исследований на полгода"),
                            () => { TechnologyManager.Instance?.AddInstantScience(600f); TechnologyManager.Instance?.ApplyTempResearchBoost(1.25f, 180); }),
                    });
            ev.Subtitle = Loc.F("система {0}", sys.Name);
            ev.Chain = chain;
            ev.Step = chapter;
            AnomalyEventSystem.Instance?.Present(ev);
        }

        private void OfferWanderer(FaunaHerd h, StarSystem sys)
        {
            Present("fauna_wanderer", Loc.T("Странник у наших границ"),
                Loc.F("Странник проходит рядом с системой {0}. Его тело тянется на несколько звёздных систем, по спине бегут волны света. Флот ждёт приказа.", sys.Name),
                sys,
                new EventOption(Loc.T("Следовать за ним"), Loc.F("+{0:0} науки в месяц, пока Странник в галактике", WandererFollowScience), () => h.Choice = 0),
                new EventOption(Loc.T("Изучить вблизи"), Loc.T("+800 очков науки сразу"),
                    () => { h.Choice = 1; TechnologyManager.Instance?.AddInstantScience(800f); }),
                new EventOption(Loc.T("Атаковать"),
                    Loc.T("+150 сплавов из обломков панциря; ближайший военный флот теряет 40% корпуса; репутация −15, соседи осудят"),
                    () =>
                    {
                        h.Choice = 2;
                        Give(alloys: 150f);
                        global::StellarisClone.Core.Reputation.Add(-15f, Loc.T("Нападение на Странника"));
                        foreach (var ai in AIEmpireManager.Alive) if (Contacts.PlayerMet(ai.OwnerId)) ai.AddMemory("incident", -8f);
                        var fm = FleetManager.Instance;
                        FleetData best = null;
                        float bestD = float.MaxValue;
                        Vector3 head = Anchor(h);
                        if (fm != null)
                            foreach (var f in fm.AllFleets)
                            {
                                var d = f?.Data;
                                if (f == null || d == null || d.Destroyed || d.OwnerId != 0 || d.Type != FleetType.Military) continue;
                                Vector3 fp = f.transform.position;
                                float dd = (fp.x - head.x) * (fp.x - head.x) + (fp.z - head.z) * (fp.z - head.z);
                                if (dd < bestD) { bestD = dd; best = d; }
                            }
                        if (best != null) best.HullPoints = Mathf.Max(1f, best.HullPoints - best.MaxHullPoints * 0.4f);
                    }));
        }

        // ==================== СОХРАНЕНИЕ ====================

        public FaunaSave CaptureState() => _s;

        public void RestoreState(FaunaSave s)
        {
            _s = s != null && s.Placed ? s : new FaunaSave();
            if (_s.Herds == null) _s.Herds = new List<FaunaHerd>();
            foreach (var h in _s.Herds)
            {
                if (h.Route == null) h.Route = new List<int>();
                if (h.Path == null) h.Path = new List<Vector3>();
            }
            _wNear.Clear();
            Version++;
        }
    }
}
