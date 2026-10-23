using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>Характер вольного мира: чем живёт, что ценит и что даёт покровителю.</summary>
    public enum NeutralCharacter { Trade, Science, Industry, Mining, Spiritual, Mercenary }

    [Serializable]
    public class NeutralWorld
    {
        public const int Owners = 8;

        public int SystemId;
        public int Character;
        public int Population;
        /// <summary>Сила гарнизона на старте (растёт с годами — EffectiveDefense).</summary>
        public float BaseDefense;
        /// <summary>Покровитель (−1 — нет): самое высокое расположение не ниже порога.</summary>
        public int Patron = -1;
        /// <summary>Кто установил контакт (бит на владельца).</summary>
        public int MetMask;
        /// <summary>Кто воюет с этим миром (бит на владельца).</summary>
        public int WarMask;
        public float[] Favor = new float[Owners];
        public int[] EnvoyCd = new int[Owners], GiftCd = new int[Owners], TradeCd = new int[Owners], HireCd = new int[Owners];
        /// <summary>Перемирие после войны: месяцев до права снова напасть.</summary>
        public int[] Truce = new int[Owners];
        /// <summary>Просьба: ресурс (−1 — нет), количество, месяцев до конца; пауза до новой просьбы.</summary>
        public int QuestRes = -1, QuestAmount, QuestMonths, QuestWait = 6;
        /// <summary>Штурм: дни блокады и кто её ведёт.</summary>
        public float SiegeDays;
        public int Besieger = -1;

        public NeutralCharacter Kind => (NeutralCharacter)Mathf.Clamp(Character, 0, 5);
        public static bool Valid(int owner) => owner >= 0 && owner < Owners;
        public bool Met(int owner) => Valid(owner) && (MetMask & (1 << owner)) != 0;
        public bool AtWarWith(int owner) => Valid(owner) && (WarMask & (1 << owner)) != 0;
        public float FavorOf(int owner) => Valid(owner) ? Favor[owner] : 0f;
    }

    [Serializable]
    public class NeutralSave
    {
        public bool Spawned;
        public List<NeutralWorld> Worlds = new List<NeutralWorld>();
    }

    /// <summary>
    /// Вольные миры — нейтральные системы, как города-государства в «Цивилизации». Сколько их — выбирается
    /// в настройках новой партии (2–6). Каждый — одна населённая система со своим характером:
    ///   • Вольный порт (торговцы)  — ценит титан, продаёт гелий-3; любит соседей — через них идут караваны;
    ///   • Академия (учёные)        — ценит гелий-3, продаёт знания; уважает самую передовую империю;
    ///   • Кузня (мастера)          — ценит титан, продаёт сплавы;
    ///   • Рудная вольница          — ценит гелий-3, продаёт титан;
    ///   • Святилище (мистики)      — ценит титан, даёт покровителю влияние; не прощает войн;
    ///   • Вольные клинки (наёмники) — ценят гелий-3, уважают сильнейший флот, сдают корабли внаём.
    /// Расположение (−100…100) у каждой империи своё: посольства, дары, торговля и выполненные просьбы
    /// его поднимают, со временем оно остывает. Друг (≥30) получает малый бонус и проход через систему,
    /// покровитель (≥60 и больше всех) — полный бонус характера. Империи ИИ ведут ту же борьбу за влияние.
    /// Вольный мир можно захватить: объявить ему войну и держать в системе флот сильнее гарнизона —
    /// блокада. Это пугает остальные вольные миры, а покровитель жертвы не забудет.
    /// Система вольного мира помечена владельцем <see cref="Owner"/>: форпост в ней не построить.
    /// </summary>
    public static class NeutralWorlds
    {
        public const int Owner = -2;
        public const float FriendFavor = 30f, PatronFavor = 60f;
        public const float EnvoyInfluence = 25f, EnvoyFavor = 15f;
        public const int EnvoyMonths = 6, GiftMonths = 3, TradeMonths = 1, HireMonths = 12;
        public const float GiftFavor = 10f, TradeFavor = 2f, QuestFavor = 30f;
        public const float TradeGive = 100f;
        public const float HireAlloys = 150f, HireEnergy = 100f;
        public const int TruceMonths = 24;

        private static NeutralSave s = new NeutralSave();
        private static int s_waitDays;

        public static IReadOnlyList<NeutralWorld> All => s.Worlds;
        public static bool Spawned => s.Spawned;

        public static void Reset() { s = new NeutralSave(); s_waitDays = 0; }

        public static NeutralWorld At(int systemId)
        {
            foreach (var w in s.Worlds) if (w.SystemId == systemId) return w;
            return null;
        }

        public static bool IsNeutral(StarSystem sys) => sys != null && sys.OwnerId == Owner && At(sys.Id) != null;

        // ==================== ХАРАКТЕРЫ ====================

        public static string Title(NeutralWorld w)
        {
            var sys = EmpireStats.GetSystem(w.SystemId);
            string n = sys != null ? sys.Name : "?";
            return w.Kind switch
            {
                NeutralCharacter.Trade => Loc.F("Вольный порт {0}", n),
                NeutralCharacter.Science => Loc.F("Академия {0}", n),
                NeutralCharacter.Industry => Loc.F("Кузня {0}", n),
                NeutralCharacter.Mining => Loc.F("Рудная вольница {0}", n),
                NeutralCharacter.Spiritual => Loc.F("Святилище {0}", n),
                _ => Loc.F("Вольные клинки {0}", n)
            };
        }

        public static string CharacterName(NeutralCharacter c) => c switch
        {
            NeutralCharacter.Trade => Loc.T("Торговцы"),
            NeutralCharacter.Science => Loc.T("Учёные"),
            NeutralCharacter.Industry => Loc.T("Мастера"),
            NeutralCharacter.Mining => Loc.T("Рудокопы"),
            NeutralCharacter.Spiritual => Loc.T("Мистики"),
            _ => Loc.T("Наёмники")
        };

        public static string Description(NeutralCharacter c) => c switch
        {
            NeutralCharacter.Trade => Loc.T("Живут торговлей. Ценят титан, продают гелий-3 по лучшей цене. Любят соседей: через их владения идут караваны"),
            NeutralCharacter.Science => Loc.T("Хранители знаний. Ценят гелий-3, делятся открытиями. Уважают самую передовую империю"),
            NeutralCharacter.Industry => Loc.T("Мастера-оружейники. Ценят титан, продают сплавы"),
            NeutralCharacter.Mining => Loc.T("Независимые рудокопы. Ценят гелий-3, продают титан"),
            NeutralCharacter.Spiritual => Loc.T("Древний орден. Ценит титан на храмы, дарит покровителю влияние. Войн не прощает"),
            _ => Loc.T("Вольные клинки. Ценят гелий-3, уважают сильнейший флот, сдают корабли внаём")
        };

        /// <summary>Что получает покровитель (друг — 40%).</summary>
        public static string PatronBonus(NeutralCharacter c) => c switch
        {
            NeutralCharacter.Trade => Loc.F("+{0:0} гелия-3 в месяц", Yield(c) ),
            NeutralCharacter.Science => Loc.F("+{0:0}% к науке", Yield(c) * 100f),
            NeutralCharacter.Industry => Loc.F("+{0:0.#} сплавов в месяц", Yield(c)),
            NeutralCharacter.Mining => Loc.F("+{0:0} титана в месяц", Yield(c)),
            NeutralCharacter.Spiritual => Loc.F("+{0:0.#} влияния в месяц", Yield(c)),
            _ => Loc.F("+{0:0.#} сплавов в месяц, наём кораблей на 40% дешевле", Yield(c))
        };

        public static Color ColorOf(NeutralCharacter c) => c switch
        {
            NeutralCharacter.Trade => new Color(1f, 0.80f, 0.36f),
            NeutralCharacter.Science => new Color(0.45f, 0.70f, 1f),
            NeutralCharacter.Industry => new Color(1f, 0.58f, 0.30f),
            NeutralCharacter.Mining => new Color(0.36f, 0.92f, 0.62f),
            NeutralCharacter.Spiritual => new Color(0.80f, 0.55f, 1f),
            _ => new Color(1f, 0.42f, 0.42f)
        };

        public static LGIcon IconOf(NeutralCharacter c) => c switch
        {
            NeutralCharacter.Trade => LGIcon.Trade,
            NeutralCharacter.Science => LGIcon.Research,
            NeutralCharacter.Industry => LGIcon.Industry,
            NeutralCharacter.Mining => LGIcon.Minerals,
            NeutralCharacter.Spiritual => LGIcon.Influence,
            _ => LGIcon.Swords
        };

        /// <summary>Какой ресурс они ценят (дары, торговля, просьбы).</summary>
        public static string Wants(NeutralCharacter c)
            => c == NeutralCharacter.Trade || c == NeutralCharacter.Industry || c == NeutralCharacter.Spiritual ? "minerals" : "energy";

        /// <summary>Что они продают: ресурс или "science" (очки науки); у наёмников — корабли, торговли нет.</summary>
        public static string Sells(NeutralCharacter c) => c switch
        {
            NeutralCharacter.Trade => "energy",
            NeutralCharacter.Science => "science",
            NeutralCharacter.Industry => "alloys",
            NeutralCharacter.Mining => "minerals",
            NeutralCharacter.Spiritual => "influence",
            _ => null
        };

        public static string ResName(string res) => res switch
        {
            "energy" => Loc.T("гелий-3"),
            "minerals" => Loc.T("титан"),
            "alloys" => Loc.T("сплавы"),
            "influence" => Loc.T("влияние"),
            "science" => Loc.T("наука"),
            _ => res
        };

        private static float BaseValue(string res) => res switch
        {
            "minerals" => 1.2f,
            "alloys" => 3f,
            "influence" => 4f,
            _ => 1f
        };

        private static int YearsPlayed => TimeManager.Instance != null ? Mathf.Max(0, TimeManager.Instance.Year - 2200) : 0;

        /// <summary>Бонус покровителя: со временем растёт (до ×2 к 33-му году).</summary>
        public static float Yield(NeutralCharacter c)
        {
            float grow = Mathf.Min(2f, 1f + YearsPlayed * 0.03f);
            return c switch
            {
                NeutralCharacter.Trade => 8f * grow,
                NeutralCharacter.Science => 0.06f * grow,
                NeutralCharacter.Industry => 3f * grow,
                NeutralCharacter.Mining => 8f * grow,
                NeutralCharacter.Spiritual => 1.5f * grow,
                _ => 1.5f * grow
            };
        }

        /// <summary>Гарнизон: растёт на 5% в год.</summary>
        public static float Defense(NeutralWorld w) => w.BaseDefense * (1f + YearsPlayed * 0.05f);

        /// <summary>Сколько дней блокады нужно для захвата.</summary>
        public static float SiegeNeed(NeutralWorld w) => w.Kind == NeutralCharacter.Mercenary ? 120f : 90f;

        // ==================== СТАТУС ====================

        public static bool IsFriend(NeutralWorld w, int owner) => w != null && !w.AtWarWith(owner) && w.FavorOf(owner) >= FriendFavor;

        /// <summary>Доля бонуса характера: покровитель — 1, друг — 0,4, иначе 0.</summary>
        public static float Share(NeutralWorld w, int owner)
        {
            if (w == null || !NeutralWorld.Valid(owner) || w.AtWarWith(owner)) return 0f;
            if (w.Patron == owner) return 1f;
            return w.FavorOf(owner) >= FriendFavor ? 0.4f : 0f;
        }

        /// <summary>Пускает ли вольный мир корабли владельца: друзья, покровитель и те, кто с ним воюет.</summary>
        public static bool CanEnter(int mover, int systemId)
        {
            var w = At(systemId);
            if (w == null) return true;
            return w.AtWarWith(mover) || w.Patron == mover || w.FavorOf(mover) >= FriendFavor;
        }

        /// <summary>Месячный доход владельца от вольных миров (energy, minerals, alloys, influence).</summary>
        public static float Income(int owner, string res)
        {
            float v = 0f;
            foreach (var w in s.Worlds)
            {
                float share = Share(w, owner);
                if (share <= 0f) continue;
                var c = w.Kind;
                switch (res)
                {
                    case "energy": if (c == NeutralCharacter.Trade) v += Yield(c) * share; break;
                    case "minerals": if (c == NeutralCharacter.Mining) v += Yield(c) * share; break;
                    case "alloys": if (c == NeutralCharacter.Industry || c == NeutralCharacter.Mercenary) v += Yield(c) * share; break;
                    case "influence": if (c == NeutralCharacter.Spiritual) v += Yield(c) * share; break;
                }
            }
            return v;
        }

        /// <summary>Множитель науки владельца от академий.</summary>
        public static float ResearchMult(int owner)
        {
            float m = 1f;
            foreach (var w in s.Worlds)
                if (w.Kind == NeutralCharacter.Science) m += Yield(NeutralCharacter.Science) * Share(w, owner);
            return m;
        }

        /// <summary>Вольный мир, с которым владелец воюет (−1 — нет): цель его флота.</summary>
        public static int WarTargetOf(int owner)
        {
            foreach (var w in s.Worlds) if (w.AtWarWith(owner)) return w.SystemId;
            return -1;
        }

        private static string OwnerName(int owner)
            => owner == 0 ? Loc.T("Вы") : Contacts.NameForPlayer(owner, AIEmpireManager.NameOf(owner));

        private static IEnumerable<int> Empires()
        {
            yield return 0;
            foreach (var ai in AIEmpireManager.Alive)
                if (NeutralWorld.Valid(ai.OwnerId)) yield return ai.OwnerId;
        }

        // ==================== ПОЯВЛЕНИЕ ====================

        /// <summary>
        /// Расставить вольные миры, когда все столицы уже на карте: подальше от столиц (не ближе 3 прыжков),
        /// между империями и друг от друга.
        /// </summary>
        private static void Spawn()
        {
            s.Spawned = true;
            var systems = EmpireStats.Systems;
            if (systems.Count == 0) return;
            int count = GameSession.Settings != null ? GameSession.Settings.NeutralCount : 4;

            var capitals = new List<int> { EconomyManager.PlayerCapitalId };
            foreach (var ai in AIEmpireManager.All) if (ai.CapitalSystemId >= 0) capitals.Add(ai.CapitalSystemId);
            var capDist = new List<Dictionary<int, int>>();
            foreach (int c in capitals) capDist.Add(Hops(new List<int> { c }, 40));

            var chosen = new List<int>();
            var characters = new List<int> { 0, 1, 2, 3, 4, 5 };
            for (int k = 0; k < count; k++)
            {
                var fromChosen = chosen.Count > 0 ? Hops(chosen, 40) : null;
                int best = -1;
                float bestScore = float.MinValue;
                for (int minGap = 4; minGap >= 2 && best < 0; minGap--)
                {
                    foreach (var sys in systems)
                    {
                        if (sys.OwnerId != -1 || sys.Landmark >= 0 || sys.SpectralClass == StarSpectralClass.BlackHole) continue;
                        if (sys.ConnectedSystemIds.Count < 2 || chosen.Contains(sys.Id)) continue;
                        int d1 = 99, d2 = 99;
                        foreach (var cd in capDist)
                        {
                            int d = cd.TryGetValue(sys.Id, out int v) ? v : 99;
                            if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                        }
                        if (d1 < minGap || d1 >= 99) continue;
                        int dn = fromChosen != null && fromChosen.TryGetValue(sys.Id, out int dv) ? dv : 12;
                        if (dn < 3) continue;
                        // Между империями (d1 ≈ d2), не слишком далеко от всех, подальше от других вольных миров
                        float score = Mathf.Min(d1, 6) - 0.5f * Mathf.Min(d2 - d1, 8) + Mathf.Min(dn, 8) * 0.8f + UnityEngine.Random.value;
                        if (score > bestScore) { bestScore = score; best = sys.Id; }
                    }
                }
                if (best < 0) break;
                chosen.Add(best);

                if (characters.Count == 0) characters.AddRange(new[] { 0, 1, 2, 3, 4, 5 });
                int ci = characters[UnityEngine.Random.Range(0, characters.Count)];
                characters.Remove(ci);
                Create(systems[best], ci);
            }
            GalaxyView.Instance?.RefreshTerritoryVisuals();
        }

        private static void Create(StarSystem sys, int character)
        {
            sys.GeneratePlanets();
            sys.OwnerId = Owner;
            sys.HasStarbase = false;
            sys.MarkSurveyedByAll();
            var w = new NeutralWorld
            {
                SystemId = sys.Id,
                Character = character,
                Population = UnityEngine.Random.Range(8, 15),
                BaseDefense = (NeutralCharacter)character == NeutralCharacter.Mercenary ? UnityEngine.Random.Range(800f, 950f) : UnityEngine.Random.Range(420f, 560f),
                QuestWait = UnityEngine.Random.Range(4, 10)
            };
            s.Worlds.Add(w);
        }

        /// <summary>Прыжки от набора систем по карте (без учёта границ).</summary>
        private static Dictionary<int, int> Hops(List<int> from, int depth)
        {
            var systems = EmpireStats.Systems;
            var dist = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (int f in from) if (f >= 0 && f < systems.Count) { dist[f] = 0; q.Enqueue(f); }
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                if (dist[cur] >= depth) continue;
                foreach (int n in systems[cur].ConnectedSystemIds)
                {
                    if (n < 0 || n >= systems.Count || dist.ContainsKey(n)) continue;
                    dist[n] = dist[cur] + 1;
                    q.Enqueue(n);
                }
            }
            return dist;
        }

        // ==================== ВРЕМЯ ====================

        /// <summary>Ежедневно (из AIRelations.Tick).</summary>
        public static void Tick(int day)
        {
            if (!s.Spawned)
            {
                if (!UIManager.IsGameStarted) return;
                // Ждём, пока все империи выберут столицы (но не дольше недели)
                bool ready = true;
                foreach (var ai in AIEmpireManager.All) if (ai.CapitalSystemId < 0) ready = false;
                if (!ready && ++s_waitDays < 7) return;
                Spawn();
            }
            if (s.Worlds.Count == 0) return;
            for (int i = s.Worlds.Count - 1; i >= 0; i--) DailySiege(s.Worlds[i]);
            if (day % 5 == 0) UpdateContacts();
            if (day == 1) Monthly();
        }

        private static void UpdateContacts()
        {
            var fm = FleetManager.Instance;
            foreach (var w in s.Worlds)
            {
                var sys = EmpireStats.GetSystem(w.SystemId);
                if (sys == null) continue;
                var near2 = Hops(new List<int> { w.SystemId }, 2);
                foreach (int owner in Empires())
                {
                    if (w.Met(owner)) continue;
                    bool met = false;
                    foreach (var kv in near2)
                    {
                        var n = EmpireStats.GetSystem(kv.Key);
                        if (n != null && n.OwnerId == owner) { met = true; break; }
                    }
                    if (!met && fm != null)
                        foreach (var f in fm.AllFleets)
                        {
                            var d = f?.Data;
                            if (d == null || d.Destroyed || d.OwnerId != owner) continue;
                            if (near2.TryGetValue(d.CurrentSystemId, out int hops) && hops <= 1) { met = true; break; }
                        }
                    if (!met) continue;
                    w.MetMask |= 1 << owner;
                    if (owner == 0)
                        NotificationCenter.Show(Loc.T("Вольный мир"), Loc.F("Установлен контакт: {0} ({1}). Выберите систему, чтобы торговать и добиваться их расположения",
                            Title(w), CharacterName(w.Kind).ToLower()), NotificationCenter.Kind.Info, 8f);
                }
            }
        }

        private static void Monthly()
        {
            foreach (var w in s.Worlds)
            {
                for (int o = 0; o < NeutralWorld.Owners; o++)
                {
                    if (w.EnvoyCd[o] > 0) w.EnvoyCd[o]--;
                    if (w.GiftCd[o] > 0) w.GiftCd[o]--;
                    if (w.TradeCd[o] > 0) w.TradeCd[o]--;
                    if (w.HireCd[o] > 0) w.HireCd[o]--;
                    if (w.Truce[o] > 0) w.Truce[o]--;
                    if (w.AtWarWith(o)) continue;
                    // Расположение остывает к нулю
                    float f = w.Favor[o];
                    w.Favor[o] = f > 0f ? Mathf.Max(0f, f - 0.6f) : Mathf.Min(0f, f + 0.6f);
                }
                Drift(w);
                MonthlyQuest(w);
                UpdatePatron(w);
            }
        }

        /// <summary>Характер: кого вольный мир любит сам по себе.</summary>
        private static void Drift(NeutralWorld w)
        {
            int best = -1;
            float bestValue = 0f;
            foreach (int o in Empires())
            {
                if (!w.Met(o) || w.AtWarWith(o)) continue;
                switch (w.Kind)
                {
                    case NeutralCharacter.Trade:
                        // Соседи — торговые партнёры
                        var sys = EmpireStats.GetSystem(w.SystemId);
                        if (sys != null && AIEmpireManager.Borders(sys, o)) w.Favor[o] = Mathf.Min(100f, w.Favor[o] + 0.8f);
                        break;
                    case NeutralCharacter.Science:
                    {
                        float v = EmpireStats.TechCount(o);
                        if (v > bestValue) { bestValue = v; best = o; }
                        break;
                    }
                    case NeutralCharacter.Mercenary:
                    {
                        float v = EmpireStats.MilitaryPower(o);
                        if (v > bestValue) { bestValue = v; best = o; }
                        break;
                    }
                }
            }
            if (best >= 0) w.Favor[best] = Mathf.Min(100f, w.Favor[best] + 1f);
        }

        private static void UpdatePatron(NeutralWorld w)
        {
            int best = -1;
            float bestFavor = PatronFavor - 0.01f;
            foreach (int o in Empires())
            {
                if (w.AtWarWith(o)) continue;
                float f = w.Favor[o];
                // Прежний покровитель удерживает место при равенстве
                if (f > bestFavor || (f >= PatronFavor && Mathf.Approximately(f, bestFavor) && o == w.Patron)) { bestFavor = f; best = o; }
            }
            if (best == w.Patron) return;
            int old = w.Patron;
            w.Patron = best;
            if (best == 0)
                NotificationCenter.Show(Loc.T("Покровительство"), Loc.F("Вы стали покровителем: {0}. {1}", Title(w), PatronBonus(w.Kind)), NotificationCenter.Kind.Success, 8f);
            else if (old == 0)
                NotificationCenter.Show(Loc.T("Покровительство потеряно"),
                    best > 0 ? Loc.F("{0}: новый покровитель — {1}", Title(w), OwnerName(best)) : Loc.F("{0}: ваше влияние ослабло", Title(w)),
                    NotificationCenter.Kind.Warning, 8f);
            else if (best > 0 && w.Met(0))
                GalacticNews.Post(NewsKind.Contact, Loc.F("{0} берёт под покровительство: {1}", OwnerName(best), Title(w)), w.SystemId);
        }

        // ==================== ПРОСЬБЫ ====================

        private static readonly string[] QuestResources = { "energy", "minerals", "alloys" };

        public static string QuestText(NeutralWorld w)
            => w.QuestRes < 0 ? null : Loc.F("Просят {0} {1}", w.QuestAmount, ResName(QuestResources[Mathf.Clamp(w.QuestRes, 0, 2)]));

        public static string QuestResource(NeutralWorld w) => w.QuestRes < 0 ? null : QuestResources[Mathf.Clamp(w.QuestRes, 0, 2)];

        private static void MonthlyQuest(NeutralWorld w)
        {
            if (w.QuestRes >= 0)
            {
                if (--w.QuestMonths <= 0) { w.QuestRes = -1; w.QuestWait = UnityEngine.Random.Range(6, 12); }
                return;
            }
            if (w.WarMask != 0 || --w.QuestWait > 0) return;
            // Чаще просят то, что ценят; иногда — сплавы на оборону
            string want = UnityEngine.Random.value < 0.25f ? "alloys" : Wants(w.Kind);
            w.QuestRes = Array.IndexOf(QuestResources, want);
            float grow = 1f + YearsPlayed * 0.04f;
            w.QuestAmount = Mathf.RoundToInt((want == "alloys" ? 80f : 200f) * grow / 10f) * 10;
            w.QuestMonths = 12;
            if (w.Met(0))
                NotificationCenter.Show(Loc.T("Просьба вольного мира"), Loc.F("{0}: {1}. Кто поможет первым — получит +{2:0} расположения",
                    Title(w), QuestText(w).ToLower(), QuestFavor), NotificationCenter.Kind.Info, 7f);
        }

        // ==================== РЕСУРСЫ ВЛАДЕЛЬЦА ====================

        public static float Stock(int owner, string res)
        {
            if (owner == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco == null) return 0f;
                return res switch
                {
                    "energy" => eco.EnergyCredits,
                    "minerals" => eco.Minerals,
                    "alloys" => eco.Alloys,
                    "influence" => eco.Influence,
                    _ => 0f
                };
            }
            var ai = AIEmpireManager.For(owner);
            return ai != null ? ai.Stock(res) : 0f;
        }

        private static void Spend(int owner, string res, float amt)
        {
            if (owner == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco == null) return;
                eco.TrySpend(res == "energy" ? amt : 0f, res == "minerals" ? amt : 0f, res == "alloys" ? amt : 0f, res == "influence" ? amt : 0f);
                eco.RaiseResourcesChanged();
                return;
            }
            AIEmpireManager.For(owner)?.SpendStock(res, amt);
        }

        private static void Give(int owner, string res, float amt)
        {
            if (res == "science")
            {
                if (owner == 0) TechnologyManager.Instance?.AddInstantScience(amt);
                else AIEmpireManager.For(owner)?.AddInstantScience(amt);
                return;
            }
            if (owner == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco == null) return;
                switch (res)
                {
                    case "energy": eco.EnergyCredits += amt; break;
                    case "minerals": eco.Minerals += amt; break;
                    case "alloys": eco.Alloys += amt; break;
                    case "influence": eco.Influence += amt; break;
                }
                eco.RaiseResourcesChanged();
                return;
            }
            AIEmpireManager.For(owner)?.AddStock(res, amt);
        }

        private static void AddFavor(NeutralWorld w, int owner, float delta)
        {
            if (!NeutralWorld.Valid(owner)) return;
            w.Favor[owner] = Mathf.Clamp(w.Favor[owner] + delta, -100f, 100f);
            UpdatePatron(w);
        }

        // ==================== ДЕЙСТВИЯ ====================

        /// <summary>Общая проверка: контакт, мир. null — можно.</summary>
        private static string CommonBlocker(NeutralWorld w, int owner)
        {
            if (w == null) return Loc.T("Вольного мира больше нет");
            if (!NeutralWorld.Valid(owner)) return "?";
            if (!w.Met(owner)) return Loc.T("Нет контакта: подведите корабль или границу ближе");
            if (w.AtWarWith(owner)) return Loc.T("Идёт война");
            return null;
        }

        public static string EnvoyBlocker(NeutralWorld w, int owner)
        {
            string b = CommonBlocker(w, owner);
            if (b != null) return b;
            if (w.EnvoyCd[owner] > 0) return Loc.F("Посольство вернётся через {0} мес.", w.EnvoyCd[owner]);
            if (Stock(owner, "influence") < EnvoyInfluence) return Loc.F("Нужно {0:0} влияния", EnvoyInfluence);
            return null;
        }

        public static bool SendEnvoy(NeutralWorld w, int owner)
        {
            if (EnvoyBlocker(w, owner) != null) return false;
            Spend(owner, "influence", EnvoyInfluence);
            w.EnvoyCd[owner] = EnvoyMonths;
            AddFavor(w, owner, EnvoyFavor);
            return true;
        }

        public static float GiftAmount(NeutralWorld w) => Wants(w.Kind) == "minerals" ? 180f : 150f;

        public static string GiftBlocker(NeutralWorld w, int owner)
        {
            string b = CommonBlocker(w, owner);
            if (b != null) return b;
            if (w.GiftCd[owner] > 0) return Loc.F("Новый дар — через {0} мес.", w.GiftCd[owner]);
            string res = Wants(w.Kind);
            if (Stock(owner, res) < GiftAmount(w)) return Loc.F("Нужно {0:0} {1}", GiftAmount(w), ResName(res));
            return null;
        }

        public static bool SendGift(NeutralWorld w, int owner)
        {
            if (GiftBlocker(w, owner) != null) return false;
            Spend(owner, Wants(w.Kind), GiftAmount(w));
            w.GiftCd[owner] = GiftMonths;
            AddFavor(w, owner, GiftFavor);
            return true;
        }

        /// <summary>Сколько их товара дадут за TradeGive ценимого ресурса (лучше — при высоком расположении).</summary>
        public static float TradeReturn(NeutralWorld w, int owner)
        {
            string sells = Sells(w.Kind);
            if (sells == null) return 0f;
            string wants = Wants(w.Kind);
            float rate = 0.75f + Mathf.Max(0f, w.FavorOf(owner)) / 400f;
            if (w.Kind == NeutralCharacter.Trade) rate += 0.15f;
            if (w.Patron == owner) rate += 0.10f;
            float value = TradeGive * BaseValue(wants) * rate;
            float got = sells == "science" ? value * 1.1f : value / BaseValue(sells);
            return Mathf.Round(got);
        }

        public static string TradeBlocker(NeutralWorld w, int owner)
        {
            string b = CommonBlocker(w, owner);
            if (b != null) return b;
            if (Sells(w.Kind) == null) return Loc.T("Наёмники не торгуют — они сдают корабли внаём");
            if (w.FavorOf(owner) < 0f) return Loc.T("С недругами не торгуют: нужно расположение не ниже нуля");
            if (w.TradeCd[owner] > 0) return Loc.T("Караван уже ушёл — следующий в новом месяце");
            if (Stock(owner, Wants(w.Kind)) < TradeGive) return Loc.F("Нужно {0:0} {1}", TradeGive, ResName(Wants(w.Kind)));
            return null;
        }

        public static bool Trade(NeutralWorld w, int owner)
        {
            if (TradeBlocker(w, owner) != null) return false;
            float got = TradeReturn(w, owner);
            Spend(owner, Wants(w.Kind), TradeGive);
            Give(owner, Sells(w.Kind), got);
            w.TradeCd[owner] = TradeMonths;
            AddFavor(w, owner, TradeFavor);
            return true;
        }

        public static string QuestBlocker(NeutralWorld w, int owner)
        {
            string b = CommonBlocker(w, owner);
            if (b != null) return b;
            if (w.QuestRes < 0) return Loc.T("Просьб сейчас нет");
            string res = QuestResource(w);
            if (Stock(owner, res) < w.QuestAmount) return Loc.F("Нужно {0} {1}", w.QuestAmount, ResName(res));
            return null;
        }

        public static bool FulfilQuest(NeutralWorld w, int owner)
        {
            if (QuestBlocker(w, owner) != null) return false;
            Spend(owner, QuestResource(w), w.QuestAmount);
            w.QuestRes = -1;
            w.QuestWait = UnityEngine.Random.Range(8, 14);
            AddFavor(w, owner, QuestFavor);
            if (owner != 0 && w.Met(0))
                GalacticNews.Post(NewsKind.Contact, Loc.F("{0} выполняет просьбу: {1}", OwnerName(owner), Title(w)), w.SystemId);
            return true;
        }

        public static float HireMult(NeutralWorld w, int owner) => w.Patron == owner ? 0.6f : 1f;

        public static string HireBlocker(NeutralWorld w, int owner)
        {
            string b = CommonBlocker(w, owner);
            if (b != null) return b;
            if (w.Kind != NeutralCharacter.Mercenary) return Loc.T("Корабли внаём сдают только наёмники");
            if (w.FavorOf(owner) < FriendFavor) return Loc.F("Наёмники служат только друзьям: нужно расположение {0:0}", FriendFavor);
            if (w.HireCd[owner] > 0) return Loc.F("Свободный отряд будет через {0} мес.", w.HireCd[owner]);
            float k = HireMult(w, owner);
            if (Stock(owner, "alloys") < HireAlloys * k || Stock(owner, "energy") < HireEnergy * k)
                return Loc.F("Нужно {0:0} сплавов и {1:0} гелия-3", HireAlloys * k, HireEnergy * k);
            return null;
        }

        /// <summary>Нанять отряд: фрегат (у покровителя — эсминец, если он ему доступен) появляется в столице.</summary>
        public static bool Hire(NeutralWorld w, int owner)
        {
            if (HireBlocker(w, owner) != null) return false;
            float k = HireMult(w, owner);
            Spend(owner, "alloys", HireAlloys * k);
            Spend(owner, "energy", HireEnergy * k);
            w.HireCd[owner] = HireMonths;
            AddFavor(w, owner, 3f);
            var fm = FleetManager.Instance;
            if (owner == 0)
            {
                fm?.LaunchPlayerShip(new ConstructionJob { ShipType = FleetType.Military, Hull = ShipClass.Frigate, SystemId = EconomyManager.PlayerCapitalId });
                NotificationCenter.Show(Loc.T("Наёмники"), Loc.F("{0}: отряд прибыл в столицу и поступил к вам на службу", Title(w)), NotificationCenter.Kind.Success, 6f);
            }
            else AIEmpireManager.For(owner)?.SpawnMercenary(w.Patron == owner);
            return true;
        }

        // ==================== ВОЙНА И ЗАХВАТ ====================

        public static string WarBlocker(NeutralWorld w, int owner)
        {
            if (w == null) return Loc.T("Вольного мира больше нет");
            if (!w.Met(owner)) return Loc.T("Нет контакта");
            if (w.AtWarWith(owner)) return Loc.T("Война уже идёт");
            if (w.Truce[owner] > 0) return Loc.F("Перемирие: ещё {0} мес.", w.Truce[owner]);
            return null;
        }

        /// <summary>Объявить войну вольному миру: остальные вольные миры напуганы, покровитель жертвы оскорблён.</summary>
        public static bool DeclareWar(NeutralWorld w, int owner)
        {
            if (WarBlocker(w, owner) != null) return false;
            int patron = w.Patron;   // покровитель жертвы — до войны
            w.WarMask |= 1 << owner;
            w.Favor[owner] = -100f;
            if (w.Patron == owner) w.Patron = -1;
            foreach (var o in s.Worlds)
                if (o != w) o.Favor[owner] = Mathf.Clamp(o.Favor[owner] - (o.Kind == NeutralCharacter.Spiritual ? 30f : 20f), -100f, 100f);
            foreach (var o in s.Worlds) UpdatePatron(o);

            if (owner == 0)
            {
                Reputation.Add(-8f, Loc.T("Нападение на вольный мир"));
                if (patron > 0) AIEmpireManager.For(patron)?.AddMemory("incident", -15f);
                NotificationCenter.Show(Loc.T("Война с вольным миром"),
                    Loc.F("{0}: держите в системе флот сильнее гарнизона ({1:N0}) — {2:0} дн. блокады, и мир ваш. Остальные вольные миры встревожены",
                        Title(w), Defense(w), SiegeNeed(w)), NotificationCenter.Kind.Warning, 9f);
            }
            else
            {
                if (patron > 0 && patron != owner) AIRelations.AddGrudge(patron, owner, -20f);
                if (patron == 0)
                    NotificationCenter.Show(Loc.T("Нападение на ваш протекторат"), Loc.F("{0} напал на {1} — мир под вашим покровительством", OwnerName(owner), Title(w)),
                        NotificationCenter.Kind.Warning, 9f);
                else if (w.Met(0))
                    GalacticNews.Post(NewsKind.War, Loc.F("{0} нападает на вольный мир: {1}", OwnerName(owner), Title(w)), w.SystemId);
            }
            return true;
        }

        public const float PeaceInfluence = 25f;

        public static string PeaceBlocker(NeutralWorld w, int owner)
        {
            if (w == null || !w.AtWarWith(owner)) return Loc.T("Войны нет");
            if (Stock(owner, "influence") < PeaceInfluence) return Loc.F("Нужно {0:0} влияния", PeaceInfluence);
            return null;
        }

        public static bool MakePeace(NeutralWorld w, int owner)
        {
            if (PeaceBlocker(w, owner) != null) return false;
            Spend(owner, "influence", PeaceInfluence);
            EndWar(w, owner);
            if (owner == 0) NotificationCenter.Show(Loc.T("Мир с вольным миром"), Loc.F("{0}: война окончена, перемирие {1} мес.", Title(w), TruceMonths), NotificationCenter.Kind.Info, 6f);
            return true;
        }

        private static void EndWar(NeutralWorld w, int owner)
        {
            w.WarMask &= ~(1 << owner);
            w.Favor[owner] = Mathf.Min(w.Favor[owner], -40f);
            w.Truce[owner] = TruceMonths;
            if (w.Besieger == owner) { w.Besieger = -1; w.SiegeDays = 0f; }
        }

        /// <summary>Блокада: флот воюющей империи сильнее гарнизона — штурм идёт; гарнизон бьёт по кораблям в системе.</summary>
        private static void DailySiege(NeutralWorld w)
        {
            if (w.WarMask == 0) { w.SiegeDays = 0f; w.Besieger = -1; return; }
            var fm = FleetManager.Instance;
            if (fm == null) return;
            float defense = Defense(w);
            int best = -1;
            float bestPower = 0f;
            for (int o = 0; o < NeutralWorld.Owners; o++)
            {
                if (!w.AtWarWith(o)) continue;
                float p = fm.GetMilitaryPowerInSystem(o, w.SystemId);
                if (p > bestPower) { bestPower = p; best = o; }
            }

            if (best >= 0 && bestPower >= defense)
            {
                if (w.Besieger != best) { w.Besieger = best; w.SiegeDays = 0f; }
                w.SiegeDays += 1f;
            }
            else w.SiegeDays = Mathf.Max(0f, w.SiegeDays - 2f);

            if (best >= 0) Bombard(w, defense, bestPower >= defense ? 1f : 2f);
            if (w.Besieger >= 0 && w.SiegeDays >= SiegeNeed(w)) Capture(w, w.Besieger);
        }

        /// <summary>Гарнизон обстреливает корабли воюющих с ним империй; разбитый корабль гибнет.</summary>
        private static void Bombard(NeutralWorld w, float defense, float mult)
        {
            var fm = FleetManager.Instance;
            var dead = new List<FleetView>();
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.CurrentSystemId != w.SystemId || d.State == FleetState.InHyperlane) continue;
                if (!w.AtWarWith(d.OwnerId)) continue;
                float dmg = defense * 0.0015f * mult;
                float shield = Mathf.Min(d.ShieldPoints, dmg);
                d.ShieldPoints -= shield;
                d.HullPoints -= dmg - shield;
                if (d.HullPoints <= 0f) dead.Add(f);
            }
            foreach (var f in dead)
            {
                if (f.Data.OwnerId == 0)
                    NotificationCenter.Show(Loc.T("Потеря при штурме"), Loc.F("{0}: гарнизон {1} уничтожил корабль", f.Data.Name, Title(w)), NotificationCenter.Kind.Warning, 5f);
                fm.DisbandFleet(f);
            }
        }

        /// <summary>Вольный мир захвачен: система становится колонией победителя, жители остаются (своим видом).</summary>
        private static void Capture(NeutralWorld w, int owner)
        {
            var sys = EmpireStats.GetSystem(w.SystemId);
            s.Worlds.Remove(w);
            if (sys == null) return;
            sys.OwnerId = owner;
            sys.HasStarbase = true;
            sys.MarkSurveyedByAll();

            PlanetData home = null;
            foreach (var p in sys.Planets)
                if (p.Type == PlanetType.Continental || p.Type == PlanetType.Desert) { home = p; break; }
            if (home == null)
                foreach (var p in sys.Planets)
                    if (p.Type != PlanetType.GasGiant) { home = p; break; }
            if (home != null)
            {
                home.SetupAsStartingColony(pop: 0, urban: 2, mining: 1, generator: 1, industrial: w.Kind == NeutralCharacter.Industry ? 2 : 1);
                int pop = Mathf.Clamp(w.Population / 2, 3, 8);
                home.AddPops(SpeciesRegistry.Founder("generic").Id, pop, 0.2f);
            }

            // Остальные вольные миры боятся завоевателя
            foreach (var o in s.Worlds) { o.Favor[owner] = Mathf.Clamp(o.Favor[owner] - 15f, -100f, 100f); UpdatePatron(o); }

            if (owner == 0)
                NotificationCenter.Show(Loc.T("Вольный мир захвачен"), Loc.F("{0} теперь ваша колония. Жители ещё долго будут чужими", Title(w)), NotificationCenter.Kind.Success, 9f);
            else if (w.Met(0))
                GalacticNews.Post(NewsKind.Conquest, Loc.F("{0} захватывает вольный мир: {1}", OwnerName(owner), Title(w)), w.SystemId);

            GalaxyView.Instance?.RefreshTerritoryVisuals();
            EconomyManager.Instance?.RecalculateAll();
            TechnologyManager.Instance?.RecountPopulation();
        }

        // ==================== СОХРАНЕНИЕ ====================

        public static NeutralSave Capture() => s;

        /// <summary>null — сохранение прежней версии: вольных миров в той партии не было.</summary>
        public static void Restore(NeutralSave save)
        {
            s = save ?? new NeutralSave { Spawned = true };
            s.Worlds ??= new List<NeutralWorld>();
            foreach (var w in s.Worlds)
            {
                w.Favor = Fit(w.Favor);
                w.EnvoyCd = Fit(w.EnvoyCd); w.GiftCd = Fit(w.GiftCd); w.TradeCd = Fit(w.TradeCd); w.HireCd = Fit(w.HireCd); w.Truce = Fit(w.Truce);
            }
        }

        private static float[] Fit(float[] a)
        {
            if (a != null && a.Length == NeutralWorld.Owners) return a;
            var n = new float[NeutralWorld.Owners];
            if (a != null) Array.Copy(a, n, Mathf.Min(a.Length, n.Length));
            return n;
        }

        private static int[] Fit(int[] a)
        {
            if (a != null && a.Length == NeutralWorld.Owners) return a;
            var n = new int[NeutralWorld.Owners];
            if (a != null) Array.Copy(a, n, Mathf.Min(a.Length, n.Length));
            return n;
        }
    }
}
