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
        /// <summary>Жителей на старте (дальше население живёт на планетах мира — NeutralWorlds.TotalPop).</summary>
        public int Population;
        /// <summary>Владелец кораблей гарнизона (NeutralWorlds.FleetOwnerBase + номер мира).</summary>
        public int FleetOwner;
        /// <summary>Месяцев до нового корабля гарнизона и до новой колонии.</summary>
        public int GarrisonWait, ColonyWait = -1;
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
        /// <summary>Блокады станций мира: по системе — дни и кто осаждает.</summary>
        public List<NeutralSiege> Sieges = new List<NeutralSiege>();
        /// <summary>Присоединённые соседние системы (родная — SystemId — сюда не входит).</summary>
        public List<int> Territory = new List<int>();
        /// <summary>Месяцев до следующего расширения границ (−1 — ещё не назначено).</summary>
        public int ExpandWait = -1;

        public bool Owns(int systemId) => systemId == SystemId || (Territory != null && Territory.Contains(systemId));

        public NeutralCharacter Kind => (NeutralCharacter)Mathf.Clamp(Character, 0, 5);
        public static bool Valid(int owner) => owner >= 0 && owner < Owners;
        public bool Met(int owner) => Valid(owner) && (MetMask & (1 << owner)) != 0;
        public bool AtWarWith(int owner) => Valid(owner) && (WarMask & (1 << owner)) != 0;
        public float FavorOf(int owner) => Valid(owner) ? Favor[owner] : 0f;
    }

    [Serializable]
    public class NeutralSiege
    {
        public int SystemId;
        public float Days;
        public int Besieger = -1;
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
    public static partial class NeutralWorlds
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

        /// <summary>Вольный мир, которому принадлежит система (родная или присоединённая).</summary>
        public static NeutralWorld At(int systemId)
        {
            foreach (var w in s.Worlds) if (w.Owns(systemId)) return w;
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
        public static string PatronBonus(NeutralWorld w) => w.Kind switch
        {
            NeutralCharacter.Trade => Loc.F("+{0:0} гелия-3 в месяц", Yield(w)),
            NeutralCharacter.Science => Loc.F("+{0:0}% к науке", Yield(w) * 100f),
            NeutralCharacter.Industry => Loc.F("+{0:0.#} сплавов в месяц", Yield(w)),
            NeutralCharacter.Mining => Loc.F("+{0:0} титана в месяц", Yield(w)),
            NeutralCharacter.Spiritual => Loc.F("+{0:0.#} влияния в месяц", Yield(w)),
            _ => Loc.F("+{0:0.#} сплавов в месяц, наём кораблей на 40% дешевле", Yield(w))
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

        /// <summary>Каждая присоединённая система: +20% к бонусу покровителя и +15% к гарнизону.</summary>
        public static float SizeMult(NeutralWorld w) => 1f + 0.20f * (w.Territory != null ? w.Territory.Count : 0);

        /// <summary>Бонус характера этого мира (растёт с годами и территорией).</summary>
        public static float Yield(NeutralWorld w) => Yield(w.Kind) * SizeMult(w);

        /// <summary>Гарнизон: растёт на 5% в год и на 15% за каждую присоединённую систему.</summary>
        public static float Defense(NeutralWorld w)
            => w.BaseDefense * (1f + YearsPlayed * 0.05f) * (1f + 0.15f * (w.Territory != null ? w.Territory.Count : 0));

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
                    case "energy": if (c == NeutralCharacter.Trade) v += Yield(w) * share; break;
                    case "minerals": if (c == NeutralCharacter.Mining) v += Yield(w) * share; break;
                    case "alloys": if (c == NeutralCharacter.Industry || c == NeutralCharacter.Mercenary) v += Yield(w) * share; break;
                    case "influence": if (c == NeutralCharacter.Spiritual) v += Yield(w) * share; break;
                }
            }
            return v;
        }

        /// <summary>Множитель науки владельца от академий.</summary>
        public static float ResearchMult(int owner)
        {
            float m = 1f;
            foreach (var w in s.Worlds)
                if (w.Kind == NeutralCharacter.Science) m += Yield(w) * Share(w, owner);
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
                        if (sys.ConnectedSystemIds.Count < 2 || chosen.Contains(sys.Id) || ThreatIn(sys.Id)) continue;
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
                QuestWait = UnityEngine.Random.Range(4, 10),
                ExpandWait = FirstExpandMonths(),
                ColonyWait = UnityEngine.Random.Range(60, 97),
                FleetOwner = FreeFleetOwner()
            };
            s.Worlds.Add(w);
            SetupHome(w);
            for (int i = 0; i < GarrisonStart; i++) SpawnGuard(w);
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
            for (int i = s.Worlds.Count - 1; i >= 0; i--)
            {
                var w = s.Worlds[i];
                DailyGarrison(w);
                DailySiege(w);   // может убрать мир из списка (захват)
            }
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
                var near2 = Hops(new List<int>(Systems(w)), 2);
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
                MonthlyExpand(w);
                MonthlyGrowth(w);
                MonthlyGarrison(w);
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
                        if (Touches(w, o)) w.Favor[o] = Mathf.Min(100f, w.Favor[o] + 0.8f);
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

        // ==================== РОСТ ГРАНИЦ ====================

        /// <summary>Сколько систем вольный мир может присоединить к родной.</summary>
        public const int MaxTerritory = 3;

        /// <summary>Первое расширение — на 8–12-м году.</summary>
        private static int FirstExpandMonths() => UnityEngine.Random.Range(96, 145);

        /// <summary>Дальше — раз в 10–15 лет; торговцы и рудокопы растут на 20% быстрее.</summary>
        private static int ExpandMonths(NeutralWorld w)
        {
            int m = UnityEngine.Random.Range(120, 181);
            if (w.Kind == NeutralCharacter.Trade || w.Kind == NeutralCharacter.Mining) m = Mathf.RoundToInt(m * 0.8f);
            return m;
        }

        /// <summary>Все системы мира: родная и присоединённые.</summary>
        public static IEnumerable<int> Systems(NeutralWorld w)
        {
            yield return w.SystemId;
            if (w.Territory != null) foreach (int t in w.Territory) yield return t;
        }

        /// <summary>Граничит ли мир (любой своей системой) с территорией владельца.</summary>
        public static bool Touches(NeutralWorld w, int owner)
        {
            foreach (int id in Systems(w))
            {
                var sys = EmpireStats.GetSystem(id);
                if (sys != null && AIEmpireManager.Borders(sys, owner)) return true;
            }
            return false;
        }

        /// <summary>
        /// Очень медленный рост, как у городов-государств: на войне не растут; некуда — пробуют снова через год.
        /// </summary>
        private static void MonthlyExpand(NeutralWorld w)
        {
            w.Territory ??= new List<int>();
            if (w.ExpandWait < 0) { w.ExpandWait = FirstExpandMonths(); return; }
            if (w.WarMask != 0 || w.Territory.Count >= MaxTerritory) return;
            if (--w.ExpandWait > 0) return;
            int target = PickExpansion(w);
            w.ExpandWait = target >= 0 ? ExpandMonths(w) : 12;
            if (target >= 0) Claim(w, target);
        }

        /// <summary>
        /// Куда расти: соседняя ничейная система (не узел силы, не цель чужого строителя), которая не перекроет
        /// проход между частями карты. Богатые залежи — лучше; вплотную к империям — неохотно.
        /// </summary>
        private static int PickExpansion(NeutralWorld w)
        {
            var building = new HashSet<int>();
            var fm = FleetManager.Instance;
            if (fm != null)
                foreach (var f in fm.AllFleets)
                    if (f?.Data != null && !f.Data.Destroyed && f.Data.BuildTargetSystemId >= 0) building.Add(f.Data.BuildTargetSystemId);

            int best = -1;
            float bestScore = float.MinValue;
            foreach (int own in Systems(w))
            {
                var os = EmpireStats.GetSystem(own);
                if (os == null) continue;
                foreach (int n in os.ConnectedSystemIds)
                {
                    var c = EmpireStats.GetSystem(n);
                    if (c == null || c.OwnerId != -1 || c.Landmark >= 0 || building.Contains(n) || ThreatIn(n)) continue;
                    if (!KeepsLanesOpen(c)) continue;
                    c.GeneratePlanets();
                    float score = c.TotalEnergy + c.TotalMinerals + UnityEngine.Random.value * 3f;
                    foreach (int m in c.ConnectedSystemIds)
                    {
                        var ms = EmpireStats.GetSystem(m);
                        if (ms != null && ms.OwnerId >= 0 && !Threats.IsThreat(ms.OwnerId)) { score -= 8f; break; }
                    }
                    if (score > bestScore) { bestScore = score; best = n; }
                }
            }
            return best;
        }

        /// <summary>
        /// Не перекроет ли система проход: все её соседи (кроме вольных миров) должны оставаться связаны
        /// друг с другом в обход неё и других вольных миров.
        /// </summary>
        private static bool KeepsLanesOpen(StarSystem c)
        {
            var systems = EmpireStats.Systems;
            var need = new List<int>();
            foreach (int n in c.ConnectedSystemIds)
                if (n >= 0 && n < systems.Count && systems[n].OwnerId != Owner) need.Add(n);
            if (need.Count <= 1) return true;
            var seen = new HashSet<int> { need[0], c.Id };
            var q = new Queue<(int id, int d)>();
            q.Enqueue((need[0], 0));
            int found = 1;
            while (q.Count > 0 && found < need.Count)
            {
                var (cur, d) = q.Dequeue();
                if (d >= 16) continue;
                foreach (int n in systems[cur].ConnectedSystemIds)
                {
                    if (n < 0 || n >= systems.Count || !seen.Add(n) || systems[n].OwnerId == Owner) continue;
                    if (need.Contains(n)) found++;
                    q.Enqueue((n, d + 1));
                }
            }
            return found >= need.Count;
        }

        private static void Claim(NeutralWorld w, int systemId)
        {
            var sys = EmpireStats.GetSystem(systemId);
            if (sys == null) return;
            sys.GeneratePlanets();
            sys.OwnerId = Owner;
            sys.HasStarbase = false;
            sys.MarkSurveyedByAll();
            w.Territory.Add(systemId);
            GalaxyView.Instance?.RefreshTerritoryVisuals();
            if (!w.Met(0)) return;
            if (AIEmpireManager.Borders(sys, 0))
                NotificationCenter.Show(Loc.T("Вольный мир растёт"), Loc.F("{0} присоединяет систему {1} у ваших границ", Title(w), sys.Name),
                    NotificationCenter.Kind.Info, 7f);
            else
                GalacticNews.Post(NewsKind.Colony, Loc.F("{0} присоединяет систему {1}", Title(w), sys.Name), systemId);
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
                NotificationCenter.Show(Loc.T("Покровительство"), Loc.F("Вы стали покровителем: {0}. {1}", Title(w), PatronBonus(w)), NotificationCenter.Kind.Success, 8f);
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
                    Loc.F("{0}: разбейте стражу (мощь {1:N0}) и держите в родной системе флот сильнее базы ({2:N0}) — {3:0} дн. блокады, и мир ваш. Остальные вольные миры встревожены",
                        Title(w), GarrisonPower(w), Defense(w), SiegeNeed(w)), NotificationCenter.Kind.Warning, 9f);
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

        // ==================== СОХРАНЕНИЕ ====================

        public static NeutralSave Capture() => s;

        /// <summary>null — сохранение прежней версии: вольных миров в той партии не было.</summary>
        public static void Restore(NeutralSave save)
        {
            s = save ?? new NeutralSave { Spawned = true };
            s.Worlds ??= new List<NeutralWorld>();
            foreach (var w in s.Worlds)
            {
                w.Territory ??= new List<int>();
                w.Sieges ??= new List<NeutralSiege>();
                if (!IsFleetOwner(w.FleetOwner)) w.FleetOwner = FreeFleetOwner();
                if (TotalPop(w) == 0) SetupHome(w);   // мир из сохранения до колоний: жители переезжают на планету
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
