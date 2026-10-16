using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using Random = UnityEngine.Random;

namespace StellarisClone.Core
{
    public enum NewsKind { War, Peace, Conquest, Battle, Science, Colony, Threat, Contact, Society }

    public class NewsItem
    {
        public NewsKind Kind;
        public string Text;
        public int Day, Month, Year;
        /// <summary>Система, к которой можно перелететь камерой по клику (−1 — нет).</summary>
        public int SystemId = -1;
        /// <summary>Касается ли новость нашей империи (выделяется в ленте).</summary>
        public bool AboutPlayer;
    }

    /// <summary>
    /// «Новости галактики»: что происходит у соседей — войны и мир, захваты систем, крупные сражения,
    /// открытия, новые колонии, пиратские налёты, первые контакты — и мирная хроника раз в пару месяцев.
    ///
    /// Туман войны соблюдается: о делах империи пишут, только если мы с ней знакомы (или новость о нас),
    /// неизвестная система — «далёкая система». Войны, мир и гибель империй замечаются сравнением
    /// состояния раз в день — так ловятся все пути (ИИ↔ИИ, коалиции, игрок), без правок в каждом из них.
    /// </summary>
    public static class GalacticNews
    {
        public const int MaxItems = 60;
        private static readonly List<NewsItem> _items = new List<NewsItem>();
        public static IReadOnlyList<NewsItem> Items => _items;
        public static event Action<NewsItem> OnPosted;

        private static bool _attached;
        private static TimeManager _time;
        private static readonly Dictionary<long, bool> _wars = new Dictionary<long, bool>();
        private static readonly HashSet<int> _eliminated = new HashSet<int>();
        private static bool _snapshotTaken;
        private static int _lastMonthKey = -1;

        // ==================== ЖИЗНЕННЫЙ ЦИКЛ ====================

        /// <summary>Новая партия или загрузка: лента пуста, текущие войны считаются уже известными.</summary>
        public static void Reset()
        {
            _items.Clear();
            _wars.Clear();
            _eliminated.Clear();
            _snapshotTaken = false;
            _lastMonthKey = -1;
        }

        /// <summary>Подписаться на события игры (повторный вызов безопасен; TimeManager может смениться).</summary>
        public static void Attach()
        {
            if (!_attached)
            {
                _attached = true;
                SiegeManager.OnSystemCaptured += OnCaptured;
                Contacts.OnFirstContact += OnContact;
                ConstructionManager.OnJobCompleted += OnJob;
                TechnologyManager.OnTechCompleted += OnPlayerTech;
            }
            var tm = TimeManager.Instance;
            if (tm != _time)
            {
                if (_time != null) _time.OnDayPassed -= OnDay;
                _time = tm;
                if (_time != null) _time.OnDayPassed += OnDay;
            }
        }

        // ==================== ПУБЛИКАЦИЯ ====================

        public static void Post(NewsKind kind, string text, int systemId = -1, bool aboutPlayer = false)
        {
            if (string.IsNullOrEmpty(text) || !UIManager.IsGameStarted) return;
            var tm = TimeManager.Instance;
            var item = new NewsItem
            {
                Kind = kind, Text = text, SystemId = systemId, AboutPlayer = aboutPlayer,
                Day = tm != null ? tm.Day : 1, Month = tm != null ? tm.Month : 1, Year = tm != null ? tm.Year : 2200
            };
            _items.Insert(0, item);
            if (_items.Count > MaxItems) _items.RemoveAt(_items.Count - 1);
            OnPosted?.Invoke(item);
        }

        /// <summary>Имя империи для ленты: наша — по фракции, угрозы — по названию, незнакомые — «неизвестная империя».</summary>
        public static string Who(int owner)
        {
            if (owner == 0) return FactionTraits.FactionOf(0)?.Name ?? "Наша империя";
            if (Threats.IsThreat(owner)) return Threats.NameOf(owner);
            return Contacts.PlayerMet(owner) ? AIEmpireManager.NameOf(owner) : "Неизвестная империя";
        }

        /// <summary>Известна ли игроку хотя бы одна сторона (или это мы сами).</summary>
        public static bool Known(params int[] owners)
        {
            foreach (int o in owners)
                if (o == 0 || (o > 0 && !Threats.IsThreat(o) && Contacts.PlayerMet(o))) return true;
            return false;
        }

        public static string Place(StarSystem s)
            => s == null ? "далёкой системе" : (!Vision.Active || Vision.PlayerKnows(s.Id) ? s.Name : "далёкой системе");

        // ==================== СОБЫТИЯ ====================

        private static void OnCaptured(StarSystem sys, int oldOwner, int newOwner)
        {
            if (sys == null || !Known(oldOwner, newOwner)) return;
            bool player = oldOwner == 0 || newOwner == 0;
            string text = oldOwner >= 0
                ? $"{Who(newOwner)} захватывает систему {Place(sys)} у {Who(oldOwner)}"
                : $"{Who(newOwner)} берёт под контроль систему {Place(sys)}";
            Post(NewsKind.Conquest, text, sys.Id, player);
        }

        private static void OnContact(int a, int b)
        {
            if (a != 0 && b != 0) return;
            int other = a == 0 ? b : a;
            if (Threats.IsThreat(other)) return;
            Post(NewsKind.Contact, $"Первый контакт: {Who(0)} и {AIEmpireManager.NameOf(other)} обменялись посланиями", -1, true);
        }

        private static void OnJob(ConstructionJob job)
        {
            if (job == null || job.Kind != JobKind.Colony || !Known(job.Owner)) return;
            var sys = EmpireStats.GetSystem(job.SystemId);
            var planet = sys != null && job.PlanetIndex >= 0 && job.PlanetIndex < sys.Planets.Count ? sys.Planets[job.PlanetIndex] : null;
            string where = planet != null ? $"«{planet.Name}»" : $"в системе {Place(sys)}";
            Post(NewsKind.Colony, $"{Who(job.Owner)} основывает новую колонию {where}", job.SystemId, job.Owner == 0);
        }

        private static void OnPlayerTech(Technology t)
        {
            if (t == null || t.Tier < 2) return;
            Post(NewsKind.Science, $"Прорыв: учёные {Who(0)} освоили «{t.Name}»", -1, true);
        }

        /// <summary>Изучение технологии империей ИИ (вызывается из AIEmpireManager): в ленту — только крупные.</summary>
        public static void AITech(AIEmpireManager ai, Technology t)
        {
            if (ai == null || t == null || !Known(ai.OwnerId)) return;
            if (t.Tier < 2 && Random.value > 0.15f) return;
            if (t.Tier >= 2 && Random.value > 0.5f) return;
            Post(NewsKind.Science, $"{ai.AIName} совершает научный прорыв: «{t.Name}»");
        }

        /// <summary>Итог сражения (вызывается из CombatManager): победитель, проигравший, сколько кораблей потеряно.</summary>
        public static void Battle(StarSystem sys, int winner, int loser, int shipsLost)
        {
            if (sys == null || shipsLost < 2 || !Known(winner, loser)) return;
            bool player = winner == 0 || loser == 0;
            string text = winner >= 0 && loser >= 0 && winner != loser
                ? $"Битва при {Place(sys)}: {Who(winner)} разбивает флот {Who(loser)}, потеряно кораблей: {shipsLost}"
                : $"Кровопролитное сражение при {Place(sys)}: потеряно кораблей: {shipsLost}";
            Post(NewsKind.Battle, text, sys.Id, player);
        }

        // ==================== ЕЖЕДНЕВНАЯ СВЕРКА ====================

        private static void OnDay(int day, int month, int year)
        {
            var owners = new List<int> { 0 };
            foreach (var ai in AIEmpireManager.All) if (ai != null && !ai.IsEliminated) owners.Add(ai.OwnerId);

            // Войны и мир: сравниваем с прошлым днём
            for (int i = 0; i < owners.Count; i++)
                for (int j = i + 1; j < owners.Count; j++)
                {
                    int a = owners[i], b = owners[j];
                    long key = (long)a * 64 + b;
                    bool war = Diplomacy.AtWar(a, b);
                    bool had = _wars.TryGetValue(key, out bool was);
                    _wars[key] = war;
                    if (!_snapshotTaken || !had || was == war || !Known(a, b)) continue;
                    bool player = a == 0 || b == 0;
                    if (war) Post(NewsKind.War, $"Война! {Who(a)} и {Who(b)} обменялись объявлениями войны", -1, player);
                    else Post(NewsKind.Peace, $"Мир: {Who(a)} и {Who(b)} подписали перемирие", -1, player);
                }

            // Гибель империй
            foreach (var ai in AIEmpireManager.All)
            {
                if (ai == null || !ai.IsEliminated || !_eliminated.Add(ai.OwnerId)) continue;
                if (_snapshotTaken && Known(ai.OwnerId))
                    Post(NewsKind.Conquest, $"Конец эпохи: {ai.AIName} пала — у империи не осталось ни одной системы");
            }
            _snapshotTaken = true;

            // Хроника мирной жизни — раз в месяц с шансом
            int monthKey = year * 12 + month;
            if (monthKey != _lastMonthKey)
            {
                bool first = _lastMonthKey < 0;
                _lastMonthKey = monthKey;
                if (!first && Random.value < 0.5f) PostChronicle();
            }
        }

        // ==================== ХРОНИКА ====================

        private static readonly Dictionary<string, string[]> FactionLines = new Dictionary<string, string[]>
        {
            ["xarn"] = new[]
            {
                "Легионы {0} прошли парадом по столичной планете — смотр длился трое суток",
                "В {0} объявлен новый набор в легионы: очереди у призывных пунктов растянулись на кварталы",
                "Военные академии {0} выпустили рекордное число офицеров",
            },
            ["astrea"] = new[]
            {
                "Сенат {0} принял закон о свободе научных исследований",
                "Выборы в {0}: правящая коалиция сохранила большинство",
                "Университеты {0} открыли двери для студентов со всей галактики",
            },
            ["aquila"] = new[]
            {
                "Акции торговых домов {0} выросли на {1}% за месяц",
                "{0} открывает новую биржу на окраинах — гелий-3 дешевеет",
                "Совет акционеров {0} утвердил рекордные дивиденды",
            },
            ["iridia"] = new[]
            {
                "Оракул {0} предрёк «великую перемену» — паломники стекаются к храмам",
                "Жрецы {0} объявили год тишины: звёздные маяки погашены на сутки",
                "В {0} расшифрована ещё одна строка древних пророчеств",
            },
            ["terraan"] = new[]
            {
                "Рощи {0} зацвели — праздник длится неделю",
                "{0} высадила живой лес на безжизненной луне",
                "Совет Корней {0} собрался впервые за десятилетие",
            },
        };

        private static readonly string[] GenericLines =
        {
            "Биологи {0} описали новый вид, живущий в ледяных облаках газового гиганта",
            "Торговые маршруты {0} побили рекорд грузопотока",
            "В столице {0} открылся крупнейший в галактике космопорт",
            "Инженеры {0} испытали новый гиперпривод — без жертв",
            "Художники {0} расписали орбитальную станцию — её видно невооружённым глазом",
        };

        private static void PostChronicle()
        {
            var known = new List<AIEmpireManager>();
            foreach (var ai in AIEmpireManager.Alive) if (Contacts.PlayerMet(ai.OwnerId)) known.Add(ai);

            // Каждая третья хроника — сводка: кто крупнейший и кто впереди в науке
            if (known.Count > 0 && Random.value < 0.33f)
            {
                AIEmpireManager biggest = null, smartest = null;
                foreach (var ai in known)
                {
                    if (biggest == null || EmpireStats.SystemCount(ai.OwnerId) > EmpireStats.SystemCount(biggest.OwnerId)) biggest = ai;
                    if (smartest == null || ai.ResearchedCount > smartest.ResearchedCount) smartest = ai;
                }
                if (Random.value < 0.5f && biggest != null)
                    Post(NewsKind.Society, $"{biggest.AIName} — крупнейшая из известных держав: {EmpireStats.SystemCount(biggest.OwnerId)} систем");
                else if (smartest != null)
                    Post(NewsKind.Science, $"{smartest.AIName} лидирует в науке: изучено технологий — {smartest.ResearchedCount}");
                return;
            }

            // Иначе — бытовая новость случайной знакомой империи (или нашей, если знакомых нет)
            int owner = known.Count > 0 && Random.value < 0.8f ? known[Random.Range(0, known.Count)].OwnerId : 0;
            string key = FactionRegistry.KeyOf(FactionTraits.FactionOf(owner));
            string[] pool = key != null && FactionLines.TryGetValue(key, out var lines) && Random.value < 0.65f ? lines : GenericLines;
            string line = pool[Random.Range(0, pool.Length)];
            Post(NewsKind.Society, string.Format(line, Who(owner), Random.Range(4, 19)), -1, owner == 0);
        }
    }
}
