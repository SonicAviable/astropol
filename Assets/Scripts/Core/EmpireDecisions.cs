using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>Разделы решений: особые решения фракции и общие для всех империй.</summary>
    public enum DecisionCategory { Faction, Economy, Science, Military, Society }

    /// <summary>
    /// Решение империи: разовая затрата ресурсов (в основном титана) ради результата через N дней,
    /// временного усиления или — у великих проектов — постоянного бонуса. Одно и то же для игрока и ИИ:
    /// условия и эффекты получают владельца (0 — игрок).
    /// </summary>
    public sealed class DecisionDef
    {
        public string Id;
        /// <summary>Ключ фракции (FactionRegistry.KeyOf) или null — решение доступно всем.</summary>
        public string Faction;
        public DecisionCategory Category;
        public LGIcon Icon;
        public string Name, Desc;

        /// <summary>Цена: титан, гелий-3 и сплавы растут с размером империи (<see cref="EmpireDecisions.ScaleOf"/>), влияние — нет.</summary>
        public float Energy, Minerals, Alloys, Influence;
        /// <summary>Сколько дней идёт решение (0 — мгновенно) и через сколько дней после конца его можно принять снова.</summary>
        public int Days, Cooldown;
        /// <summary>Великий проект: принимается один раз за партию, бонус остаётся навсегда.</summary>
        public bool Once;
        /// <summary>Прибавка к стабильности колоний, пока решение действует (у великих проектов — навсегда).</summary>
        public float Stability;
        /// <summary>Бонус империи (+1 наложить, −1 снять): пока идёт решение, у великого проекта — после завершения.</summary>
        public Action<EmpireBonuses, float> Apply;
        /// <summary>Итог по завершении: (владелец, масштаб на момент принятия) → текст уведомления.</summary>
        public Func<int, float, string> OnComplete;
        /// <summary>Особое условие для владельца (null — выполнено).</summary>
        public Func<int, string> Requirement;
        /// <summary>Когда решение вообще видно владельцу (null — всегда): война, кризис, эпоха, сезон… Условие пропало — решение исчезает.</summary>
        public Func<int, bool> Shown;
        /// <summary>Возможность: при условии <see cref="Shown"/> раз в месяц появляется с шансом <see cref="OfferChance"/> и ждёт <see cref="OfferDays"/> дней.</summary>
        public int OfferDays;
        public float OfferChance;
        /// <summary>Подпись условия («только во время войны») — приглушённо на карточке и в подсказке.</summary>
        public string Note;
        public readonly List<(Func<string> Text, bool Good)> Effects = new List<(Func<string>, bool)>();

        public DecisionDef Cost(float minerals = 0f, float energy = 0f, float alloys = 0f, float influence = 0f)
        { Minerals = minerals; Energy = energy; Alloys = alloys; Influence = influence; return this; }
        public DecisionDef Time(int days, int cooldown) { Days = days; Cooldown = cooldown; return this; }
        public DecisionDef Great() { Once = true; return this; }
        public DecisionDef Mod(Action<EmpireBonuses, float> apply) { Apply = apply; return this; }
        public DecisionDef Stab(float s) { Stability = s; return Line(Loc.F("+{0:0} стабильности колоний", s), true); }
        public DecisionDef Done(Func<int, float, string> f) { OnComplete = f; return this; }
        public DecisionDef Need(Func<int, string> f) { Requirement = f; return this; }
        public DecisionDef When(Func<int, bool> f) { Shown = f; return this; }
        public DecisionDef Cond(string note) { Note = note; return this; }
        public DecisionDef Offer(float monthlyChance, int days) { OfferChance = monthlyChance; OfferDays = days; return this; }
        public bool IsOffer => OfferDays > 0;
        public DecisionDef Line(string text, bool good = true) { Effects.Add((() => text, good)); return this; }
        public DecisionDef Line(Func<string> text, bool good = true) { Effects.Add((text, good)); return this; }

        public float EnergyCostFor(int owner) => Energy * EmpireDecisions.ScaleOf(owner);
        public float MineralsCostFor(int owner) => Minerals * EmpireDecisions.ScaleOf(owner);
        public float AlloysCostFor(int owner) => Alloys * EmpireDecisions.ScaleOf(owner);
        /// <summary>Цена для игрока (окно решений).</summary>
        public float EnergyCost => EnergyCostFor(0);
        public float MineralsCost => MineralsCostFor(0);
        public float AlloysCost => AlloysCostFor(0);
        /// <summary>Решение с длительным усилением (а не с итогом в конце).</summary>
        public bool IsModifier => Apply != null || Stability > 0f;
    }

    /// <summary>Состояние решений одной империи в сохранении.</summary>
    [Serializable]
    public class DecisionStateSave
    {
        public List<string> ActiveId = new List<string>();
        public List<int> ActiveStart = new List<int>();
        public List<int> ActiveEnd = new List<int>();
        public List<float> ActiveScale = new List<float>();
        public List<string> CooldownId = new List<string>();
        public List<int> CooldownUntil = new List<int>();
        public List<string> Completed = new List<string>();
        public List<string> OfferId = new List<string>();
        public List<int> OfferUntil = new List<int>();
    }

    /// <summary>Решения империи ИИ: то же плюс ежемесячные доходы от решений (у игрока они в EmpireEffects).</summary>
    [Serializable]
    public class AiDecisionStateSave : DecisionStateSave
    {
        public int Owner;
        public List<int> IncomeMonths = new List<int>();
        public List<float> IncomeEnergy = new List<float>();
        public List<float> IncomeMinerals = new List<float>();
        public List<float> IncomeAlloys = new List<float>();
        public List<float> IncomeInfluence = new List<float>();
    }

    /// <summary>Сохранение: поля игрока на верхнем уровне (совместимо со старыми сохранениями) и список империй ИИ.</summary>
    [Serializable]
    public class DecisionsSave : DecisionStateSave
    {
        public List<AiDecisionStateSave> Ai = new List<AiDecisionStateSave>();
    }

    /// <summary>
    /// Решения империи — как в Hearts of Iron: у каждой фракции свои особые решения и великий проект,
    /// плюс общие решения для всех. Решение стоит ресурсов (в первую очередь титана — так излишки сырья
    /// превращаются в сплавы, гелий-3, науку и влияние), идёт несколько дней и потом закрывается на перезарядку.
    /// На старте видно лишь несколько решений: остальные открываются с годами, ростом империи и обстоятельствами.
    /// Одновременно — не больше <see cref="MaxActive"/>. Бонусы ложатся на бонусы империи по версии, как законы.
    ///
    /// Правила одни для игрока и ИИ: у каждой империи своё состояние (идущие решения, перезарядки, великие
    /// проекты, возможности). Какие решения берёт ИИ — решает AIEmpireManager.ThinkDecisions.
    /// </summary>
    public static class EmpireDecisions
    {
        public const int MaxActive = 3;

        private sealed class Running { public DecisionDef Def; public int Start, End; public float Scale; }
        private sealed class Income { public int Months; public float E, M, A, I; }

        private sealed class State
        {
            public readonly List<Running> Active = new List<Running>();
            public readonly Dictionary<string, int> Cooldown = new Dictionary<string, int>();
            public readonly HashSet<string> Completed = new HashSet<string>();
            /// <summary>Открытые возможности: до какого дня ждут.</summary>
            public readonly Dictionary<string, int> Offers = new Dictionary<string, int>();
            /// <summary>Ежемесячные доходы от решений (только ИИ).</summary>
            public readonly List<Income> Incomes = new List<Income>();
            public int Version = NextVersion();
            public void Touch() => Version = NextVersion();
        }

        private static readonly List<DecisionDef> s_all = new List<DecisionDef>();
        private static readonly State s_player = new State();
        private static readonly Dictionary<int, State> s_ai = new Dictionary<int, State>();
        /// <summary>Решения, которые уже были видны игроку (чтобы сообщать только о новых), и ещё не просмотренные.</summary>
        private static readonly HashSet<string> s_known = new HashSet<string>();
        private static readonly HashSet<string> s_unseen = new HashSet<string>();
        private static bool s_scanned;
        /// <summary>Когда о решении сообщали в последний раз — чтобы мигающие условия (бой, баланс гелия) не засыпали уведомлениями.</summary>
        private static readonly Dictionary<string, int> s_notified = new Dictionary<string, int>();
        private const int RenotifyDays = 120;
        /// <summary>Сквозной счётчик версий: новое состояние никогда не совпадёт по версии со старыми бонусами.</summary>
        private static int s_versionCounter = 1;
        private static int NextVersion() => ++s_versionCounter;

        public static event Action OnChanged;

        public static IReadOnlyList<DecisionDef> All { get { EnsureDefs(); return s_all; } }

        public static readonly DecisionCategory[] Categories =
            { DecisionCategory.Faction, DecisionCategory.Economy, DecisionCategory.Science, DecisionCategory.Military, DecisionCategory.Society };

        public static string CategoryName(DecisionCategory c) => c switch
        {
            DecisionCategory.Faction => FactionTraits.FactionOf(0)?.Name ?? Loc.T("Фракция"),
            DecisionCategory.Economy => Loc.T("Экономика"),
            DecisionCategory.Science => Loc.T("Наука"),
            DecisionCategory.Military => Loc.T("Армия"),
            _ => Loc.T("Общество")
        };

        /// <summary>
        /// Масштаб цен и наград по размеру империи: 1 колония — ×1, каждая следующая +10%, не больше ×3.
        /// Добыча титана растёт с империей примерно втрое за 20 лет — масштаб держит решения ощутимыми.
        /// </summary>
        public static float ScaleOf(int owner) => Mathf.Clamp(1f + 0.10f * (EmpireStats.Colonies(owner) - 1), 1f, 3f);
        public static float Scale => ScaleOf(0);

        private static State St(int owner)
        {
            if (owner == 0) return s_player;
            if (!s_ai.TryGetValue(owner, out var s)) s_ai[owner] = s = new State();
            return s;
        }

        private static bool OwnFaction(int owner, DecisionDef d) => d.Faction == null || d.Faction == FactionTraits.Key(owner);

        /// <summary>Видно ли решение владельцу: идущее и построенное — всегда, возможность — пока не истекла, остальное — по условию.</summary>
        public static bool IsVisible(int owner, DecisionDef d)
        {
            if (d == null || !OwnFaction(owner, d)) return false;
            var st = St(owner);
            if (Find(st, d) != null || IsDone(owner, d)) return true;
            if (d.IsOffer) return st.Offers.ContainsKey(d.Id);
            return d.Shown == null || d.Shown(owner);
        }
        public static bool IsVisible(DecisionDef d) => IsVisible(0, d);

        /// <summary>Решения раздела, которые видит игрок: сначала возможности с таймером, потом остальные.</summary>
        public static IEnumerable<DecisionDef> Visible(DecisionCategory c)
        {
            EnsureDefs();
            foreach (var d in s_all) if (d.Category == c && d.IsOffer && IsVisible(0, d)) yield return d;
            foreach (var d in s_all) if (d.Category == c && !d.IsOffer && IsVisible(0, d)) yield return d;
        }

        /// <summary>Сколько решений игрок может принять прямо сейчас (счётчик на кнопке меню).</summary>
        public static int AvailableCount
        {
            get
            {
                EnsureDefs();
                int n = 0;
                foreach (var d in s_all) if (IsVisible(0, d) && Blocker(0, d) == null) n++;
                return n;
            }
        }

        public static bool IsNew(DecisionDef d) => d != null && s_unseen.Contains(d.Id);
        public static int UnseenCount => s_unseen.Count;
        /// <summary>Игрок посмотрел вкладку решений — метки «новое» снимаются.</summary>
        public static void MarkSeen() { if (s_unseen.Count > 0) { s_unseen.Clear(); OnChanged?.Invoke(); } }

        /// <summary>Сколько дней ещё ждёт возможность.</summary>
        public static int OfferLeft(DecisionDef d) => OfferLeft(0, d);
        public static int OfferLeft(int owner, DecisionDef d)
            => d != null && St(owner).Offers.TryGetValue(d.Id, out int until) ? Mathf.Max(0, until - EmpireLaws.Today()) : 0;

        // ================================================================ Состояние

        private static Running Find(State st, DecisionDef d)
        {
            foreach (var r in st.Active) if (r.Def == d) return r;
            return null;
        }

        public static bool IsActive(int owner, DecisionDef d) => d != null && Find(St(owner), d) != null;
        public static bool IsActive(DecisionDef d) => IsActive(0, d);
        public static bool IsDone(int owner, DecisionDef d) => d != null && d.Once && St(owner).Completed.Contains(d.Id);
        public static bool IsDone(DecisionDef d) => IsDone(0, d);
        public static int ActiveCountOf(int owner) => St(owner).Active.Count;
        public static int ActiveCount => s_player.Active.Count;

        public static int DaysLeft(DecisionDef d) { var r = Find(s_player, d); return r == null ? 0 : Mathf.Max(0, r.End - EmpireLaws.Today()); }

        /// <summary>Доля выполнения идущего решения игрока (0…1).</summary>
        public static float Progress(DecisionDef d)
        {
            var r = Find(s_player, d);
            if (r == null) return 0f;
            return Mathf.Clamp01((EmpireLaws.Today() - r.Start) / (float)Mathf.Max(1, r.End - r.Start));
        }

        public static int CooldownLeft(int owner, DecisionDef d)
            => d != null && St(owner).Cooldown.TryGetValue(d.Id, out int until) ? Mathf.Max(0, until - EmpireLaws.Today()) : 0;
        public static int CooldownLeft(DecisionDef d) => CooldownLeft(0, d);

        /// <summary>Почему владелец не может принять решение (null — можно).</summary>
        public static string Blocker(int owner, DecisionDef d)
        {
            if (d == null) return Loc.T("Нет решения");
            if (IsDone(owner, d)) return Loc.T("Проект завершён");
            var st = St(owner);
            var run = Find(st, d);
            if (run != null) return Loc.F("Идёт: ещё {0} дн.", Mathf.Max(0, run.End - EmpireLaws.Today()));
            int cd = CooldownLeft(owner, d);
            if (cd > 0) return Loc.F("Снова через {0} дн.", cd);
            if (st.Active.Count >= MaxActive) return Loc.F("Уже идут {0} решения", MaxActive);
            string req = d.Requirement?.Invoke(owner);
            if (req != null) return req;
            // Без чисел: подпись на кнопке не меняется каждый день, сколько нужно — видно по красной цене
            if (!Stock(owner, out float e, out float m, out float a, out float i)) return Loc.T("Нет экономики");
            if (m < d.MineralsCostFor(owner)) return Loc.T("Не хватает титана");
            if (e < d.EnergyCostFor(owner)) return Loc.T("Не хватает гелия-3");
            if (a < d.AlloysCostFor(owner)) return Loc.T("Не хватает сплавов");
            if (i < d.Influence) return Loc.T("Не хватает влияния");
            return null;
        }
        public static string Blocker(DecisionDef d) => Blocker(0, d);

        public static bool TryTake(DecisionDef d) => TryTake(0, d);

        public static bool TryTake(int owner, DecisionDef d)
        {
            if (Blocker(owner, d) != null) return false;
            if (!Spend(owner, d.EnergyCostFor(owner), d.MineralsCostFor(owner), d.AlloysCostFor(owner), d.Influence)) return false;
            var st = St(owner);
            float scale = ScaleOf(owner);
            st.Offers.Remove(d.Id);
            if (owner == 0) s_unseen.Remove(d.Id);
            if (d.Days <= 0)
            {
                Finish(owner, d, scale);
            }
            else
            {
                int today = EmpireLaws.Today();
                st.Active.Add(new Running { Def = d, Start = today, End = today + d.Days, Scale = scale });
                if (owner == 0)
                    NotificationCenter.Show(Loc.F("Решение принято: «{0}»", d.Name),
                        d.Once ? Loc.F("Строительство займёт {0} дн.", d.Days)
                        : d.IsModifier ? Loc.F("Действует {0} дн.", d.Days) : Loc.F("Итог — через {0} дн.", d.Days),
                        NotificationCenter.Kind.Info, 5f);
                else NewsAboutAI(owner, d, false);
            }
            st.Touch();
            if (owner == 0) { EconomyManager.Instance?.RecalculateAll(); OnChanged?.Invoke(); }
            return true;
        }

        private static void Finish(int owner, DecisionDef d, float scale)
        {
            var st = St(owner);
            if (d.Once) st.Completed.Add(d.Id);
            else if (d.Cooldown > 0) st.Cooldown[d.Id] = EmpireLaws.Today() + d.Cooldown;
            string result = d.OnComplete?.Invoke(owner, scale);
            if (owner != 0)
            {
                if (d.Once) NewsAboutAI(owner, d, true);
                return;
            }
            if (d.Once) result ??= Loc.T("Великий проект завершён — его бонусы действуют до конца партии.");
            else if (d.IsModifier && result == null) result = Loc.T("Срок действия закончился.");
            NotificationCenter.Show(d.IsModifier && !d.Once ? Loc.F("Решение «{0}» завершено", d.Name) : Loc.F("Решение выполнено: «{0}»", d.Name),
                result ?? "", NotificationCenter.Kind.Success, 6f);
        }

        /// <summary>В ленту новостей — только заметное: великие проекты и особые решения фракций известных империй.</summary>
        private static void NewsAboutAI(int owner, DecisionDef d, bool completed)
        {
            var ai = AIEmpireManager.For(owner);
            if (ai == null || !GalacticNews.Known(owner)) return;
            if (d.Once)
                GalacticNews.Post(NewsKind.Society, completed
                    ? Loc.F("{0} завершает великий проект «{1}»", ai.AIName, d.Name)
                    : Loc.F("{0} начинает великий проект «{1}»", ai.AIName, d.Name));
            else if (!completed && d.Category == DecisionCategory.Faction && UnityEngine.Random.value < 0.5f)
                GalacticNews.Post(NewsKind.Society, Loc.F("{0} принимает решение «{1}»", ai.AIName, d.Name));
        }

        /// <summary>Каждый день: завершить решения, у которых вышел срок; закрыть упущенные возможности; раз в 5 дней — найти новые решения игрока.</summary>
        public static void DailyTick()
        {
            int today = EmpireLaws.Today();
            bool changed = ExpireOffers(0, s_player, today);
            if (!s_scanned || today % 5 == 0) changed |= ScanNew();
            if (TickActive(0, s_player, today)) { changed = true; EconomyManager.Instance?.RecalculateAll(); }
            if (changed) OnChanged?.Invoke();

            foreach (var kv in s_ai)
            {
                if (AIEmpireManager.For(kv.Key) == null) continue;
                ExpireOffers(kv.Key, kv.Value, today);
                TickActive(kv.Key, kv.Value, today);
            }
        }

        private static bool TickActive(int owner, State st, int today)
        {
            bool changed = false;
            for (int i = st.Active.Count - 1; i >= 0; i--)
            {
                var r = st.Active[i];
                if (today < r.End) continue;
                st.Active.RemoveAt(i);
                Finish(owner, r.Def, r.Scale);
                changed = true;
            }
            if (changed) st.Touch();
            return changed;
        }

        private static bool ExpireOffers(int owner, State st, int today)
        {
            if (st.Offers.Count == 0) return false;
            List<string> gone = null;
            foreach (var kv in st.Offers) if (today >= kv.Value) (gone ??= new List<string>()).Add(kv.Key);
            if (gone == null) return false;
            foreach (var id in gone)
            {
                st.Offers.Remove(id);
                var d = Get(id);
                if (d == null) continue;
                st.Cooldown[id] = today + Mathf.Max(90, d.Cooldown / 2);   // упущенный шанс вернётся не скоро
                if (owner != 0) continue;
                s_unseen.Remove(id);
                NotificationCenter.Show(Loc.F("Возможность упущена: «{0}»", d.Name), Loc.T("Решение больше недоступно."), NotificationCenter.Kind.Info, 4f);
            }
            return true;
        }

        /// <summary>
        /// Сверить, какие решения стали видны игроку: о новых — уведомление и метка «новое», исчезнувшие забываются
        /// (появятся снова — снова сообщим). Первый проход после начала партии или загрузки — молча.
        /// </summary>
        private static bool ScanNew()
        {
            EnsureDefs();
            bool silent = !s_scanned, changed = false;
            s_scanned = true;
            foreach (var d in s_all)
            {
                if (d.IsOffer) continue;   // о возможностях сообщает MonthlyTick
                bool vis = IsVisible(0, d) && !IsDone(0, d);
                if (vis && s_known.Add(d.Id))
                {
                    if (silent || d.Shown == null || CooldownLeft(0, d) > 0) continue;
                    int today = EmpireLaws.Today();
                    if (s_notified.TryGetValue(d.Id, out int last) && today - last < RenotifyDays) continue;
                    s_notified[d.Id] = today;
                    s_unseen.Add(d.Id);
                    changed = true;
                    NotificationCenter.Show(Loc.F("Новое решение: «{0}»", d.Name), Loc.T("Законы (L) → вкладка «Решения»"), NotificationCenter.Kind.Info, 6f);
                }
                else if (!vis && s_known.Remove(d.Id)) { s_unseen.Remove(d.Id); changed = true; }
            }
            return changed;
        }

        /// <summary>Раз в месяц: случайные возможности с таймером (игроку и ИИ), доходы ИИ от решений.</summary>
        public static void MonthlyTick()
        {
            EnsureDefs();
            if (RollOffers(0, s_player)) OnChanged?.Invoke();
            foreach (var ai in AIEmpireManager.Alive)
            {
                var st = St(ai.OwnerId);
                RollOffers(ai.OwnerId, st);
                for (int i = st.Incomes.Count - 1; i >= 0; i--)
                {
                    var inc = st.Incomes[i];
                    Gain(ai.OwnerId, inc.E, inc.M, inc.A, inc.I);
                    if (--inc.Months <= 0) st.Incomes.RemoveAt(i);
                }
            }
        }

        private static bool RollOffers(int owner, State st)
        {
            int today = EmpireLaws.Today();
            bool changed = false;
            foreach (var d in s_all)
            {
                if (!d.IsOffer || !OwnFaction(owner, d) || st.Offers.ContainsKey(d.Id) || Find(st, d) != null || CooldownLeft(owner, d) > 0) continue;
                if (d.Shown != null && !d.Shown(owner)) continue;
                if (UnityEngine.Random.value >= d.OfferChance) continue;
                st.Offers[d.Id] = today + d.OfferDays;
                changed = true;
                if (owner != 0) continue;
                s_unseen.Add(d.Id);
                NotificationCenter.Show(Loc.F("Новая возможность: «{0}»", d.Name),
                    Loc.F("Ждёт {0} дн. — законы (L) → вкладка «Решения»", d.OfferDays), NotificationCenter.Kind.Success, 7f);
            }
            return changed;
        }

        /// <summary>Бонусы, которые сейчас действуют: идущие решения-усиления и завершённые великие проекты.</summary>
        private static IEnumerable<DecisionDef> Applied(State st)
        {
            foreach (var r in st.Active) if (!r.Def.Once) yield return r.Def;
            foreach (var id in st.Completed) { var d = Get(id); if (d != null) yield return d; }
        }

        /// <summary>Наложить бонусы решений игрока на его бонусы (как законы — по версии, без задвоения).</summary>
        public static void SyncBonuses(EmpireBonuses b) => SyncBonuses(0, b);

        /// <summary>Наложить бонусы решений владельца (вызывается при каждом обращении к бонусам — дёшево).</summary>
        public static void SyncBonuses(int owner, EmpireBonuses b)
        {
            if (b == null) return;
            var st = owner == 0 ? s_player : (s_ai.TryGetValue(owner, out var s) ? s : null);
            int version = st?.Version ?? 0;
            if (b.DecisionsVersion == version) return;
            EnsureDefs();
            if (b.DecisionsApplied != null)
                foreach (var id in b.DecisionsApplied) Get(id)?.Apply?.Invoke(b, -1f);
            var applied = new List<string>();
            if (st != null)
                foreach (var d in Applied(st))
                {
                    if (d.Apply == null) continue;
                    d.Apply(b, 1f);
                    applied.Add(d.Id);
                }
            b.DecisionsApplied = applied;
            b.DecisionsVersion = version;
        }

        public static void StabilityFactors(int owner, Action<string, float> add)
        {
            if (add == null || owner < 0) return;
            var st = owner == 0 ? s_player : (s_ai.TryGetValue(owner, out var s) ? s : null);
            if (st == null) return;
            foreach (var d in Applied(st))
                if (d.Stability > 0.01f) add(Loc.F("Решение «{0}»", d.Name), d.Stability);
        }

        public static DecisionDef Get(string id)
        {
            EnsureDefs();
            foreach (var d in s_all) if (d.Id == id) return d;
            return null;
        }

        // ================================================================ Партия и сохранения

        public static void Reset()
        {
            Clear(s_player);
            s_ai.Clear();
            s_known.Clear(); s_unseen.Clear(); s_notified.Clear(); s_scanned = false;
            OnChanged?.Invoke();
        }

        private static void Clear(State st)
        {
            st.Active.Clear(); st.Cooldown.Clear(); st.Completed.Clear(); st.Offers.Clear(); st.Incomes.Clear();
            st.Touch();
        }

        public static DecisionsSave Capture()
        {
            var s = new DecisionsSave();
            CaptureState(s_player, s);
            foreach (var kv in s_ai)
            {
                var a = new AiDecisionStateSave { Owner = kv.Key };
                CaptureState(kv.Value, a);
                foreach (var inc in kv.Value.Incomes)
                {
                    a.IncomeMonths.Add(inc.Months); a.IncomeEnergy.Add(inc.E); a.IncomeMinerals.Add(inc.M);
                    a.IncomeAlloys.Add(inc.A); a.IncomeInfluence.Add(inc.I);
                }
                s.Ai.Add(a);
            }
            return s;
        }

        private static void CaptureState(State st, DecisionStateSave s)
        {
            foreach (var r in st.Active)
            {
                s.ActiveId.Add(r.Def.Id); s.ActiveStart.Add(r.Start); s.ActiveEnd.Add(r.End); s.ActiveScale.Add(r.Scale);
            }
            foreach (var kv in st.Cooldown) { s.CooldownId.Add(kv.Key); s.CooldownUntil.Add(kv.Value); }
            s.Completed.AddRange(st.Completed);
            foreach (var kv in st.Offers) { s.OfferId.Add(kv.Key); s.OfferUntil.Add(kv.Value); }
        }

        public static void Restore(DecisionsSave s)
        {
            Clear(s_player);
            s_ai.Clear();
            s_known.Clear(); s_unseen.Clear(); s_notified.Clear(); s_scanned = false;
            if (s != null)
            {
                RestoreState(s_player, s);
                if (s.Ai != null)
                    foreach (var a in s.Ai)
                    {
                        if (a == null || a.Owner <= 0) continue;
                        var st = St(a.Owner);
                        RestoreState(st, a);
                        for (int i = 0; a.IncomeMonths != null && i < a.IncomeMonths.Count; i++)
                            st.Incomes.Add(new Income
                            {
                                Months = a.IncomeMonths[i],
                                E = At(a.IncomeEnergy, i), M = At(a.IncomeMinerals, i), A = At(a.IncomeAlloys, i), I = At(a.IncomeInfluence, i)
                            });
                    }
            }
            OnChanged?.Invoke();
        }

        private static float At(List<float> list, int i) => list != null && i < list.Count ? list[i] : 0f;

        private static void RestoreState(State st, DecisionStateSave s)
        {
            for (int i = 0; s.ActiveId != null && i < s.ActiveId.Count && i < s.ActiveEnd.Count; i++)
            {
                var d = Get(s.ActiveId[i]);
                if (d == null) continue;
                st.Active.Add(new Running
                {
                    Def = d, End = s.ActiveEnd[i],
                    Start = s.ActiveStart != null && i < s.ActiveStart.Count ? s.ActiveStart[i] : s.ActiveEnd[i] - d.Days,
                    Scale = s.ActiveScale != null && i < s.ActiveScale.Count ? s.ActiveScale[i] : 1f
                });
            }
            for (int i = 0; s.CooldownId != null && i < s.CooldownId.Count && i < s.CooldownUntil.Count; i++) st.Cooldown[s.CooldownId[i]] = s.CooldownUntil[i];
            if (s.Completed != null) foreach (var id in s.Completed) if (Get(id) != null) st.Completed.Add(id);
            for (int i = 0; s.OfferId != null && s.OfferUntil != null && i < s.OfferId.Count && i < s.OfferUntil.Count; i++)
                if (Get(s.OfferId[i]) != null) st.Offers[s.OfferId[i]] = s.OfferUntil[i];
            st.Touch();
        }

        // ================================================================ Ресурсы владельца

        private static bool Stock(int owner, out float e, out float m, out float a, out float i)
        {
            e = m = a = i = 0f;
            if (owner == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco == null) return false;
                e = eco.EnergyCredits; m = eco.Minerals; a = eco.Alloys; i = eco.Influence;
                return true;
            }
            var ai = AIEmpireManager.For(owner);
            if (ai == null) return false;
            e = ai.EnergyCredits; m = ai.Minerals; a = ai.Alloys; i = ai.Influence;
            return true;
        }

        private static bool Spend(int owner, float e, float m, float a, float i)
        {
            if (owner == 0) return EconomyManager.Instance != null && EconomyManager.Instance.TrySpend(e, m, a, i);
            var ai = AIEmpireManager.For(owner);
            if (ai == null || ai.EnergyCredits < e || ai.Minerals < m || ai.Alloys < a || ai.Influence < i) return false;
            ai.EnergyCredits -= e; ai.Minerals -= m; ai.Alloys -= a; ai.Influence -= i;
            return true;
        }

        private static void Gain(int owner, float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f)
        {
            if (owner == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco == null) return;
                eco.EnergyCredits += energy; eco.Minerals += minerals; eco.Alloys += alloys; eco.Influence += influence;
                eco.RaiseResourcesChanged();
                return;
            }
            var ai = AIEmpireManager.For(owner);
            if (ai == null) return;
            ai.EnergyCredits += energy; ai.Minerals += minerals; ai.Alloys += alloys; ai.Influence += influence;
        }

        /// <summary>Доход каждый месяц: у игрока — временный эффект (виден в обзоре), у ИИ — своя запись.</summary>
        private static void MonthlyIncome(int owner, string id, string title, string detail, int months,
                                          float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f)
        {
            if (owner == 0) { EmpireEffects.Add(id, title, detail, months, energy: energy, minerals: minerals, alloys: alloys, influence: influence); return; }
            St(owner).Incomes.Add(new Income { Months = months, E = energy, M = minerals, A = alloys, I = influence });
        }

        // ================================================================ Помощники эффектов и условий

        private static IEnumerable<PlanetData> ColoniesOf(int owner)
        {
            foreach (var sys in EmpireStats.Systems)
            {
                if (sys.OwnerId != owner) continue;
                foreach (var p in sys.Planets) if (p.Population > 0) yield return p;
            }
        }

        private static int Colonies(int owner) => EmpireStats.Colonies(owner);

        private static int ColoniesWithRoom(int owner)
        {
            int n = 0;
            foreach (var p in ColoniesOf(owner)) if (p.Population < p.HousingCapacity) n++;
            return n;
        }

        /// <summary>По одному жителю на каждую колонию со свободным жильём (не больше max колоний).</summary>
        private static int Grow(int owner, bool founder, int max = int.MaxValue)
        {
            int f = SpeciesRegistry.FounderOf(owner), n = 0;
            foreach (var p in new List<PlanetData>(ColoniesOf(owner)))
            {
                if (n >= max) break;
                if (p.Population >= p.HousingCapacity) continue;
                p.AddPops(founder ? f : p.GrowthSpecies(), 1);
                n++;
            }
            return n;
        }

        private static float ScienceOf(int owner)
        {
            if (owner == 0) return TechnologyManager.Instance != null ? TechnologyManager.Instance.MonthlyResearchIncome : 0f;
            var ai = AIEmpireManager.For(owner);
            return ai != null ? ai.MonthlyScience : 0f;
        }
        private static float MonthlyScience => ScienceOf(0);

        private static string GiveScience(int owner, float months)
        {
            float pts = ScienceOf(owner) * months;
            if (owner == 0) TechnologyManager.Instance?.AddInstantScience(pts);
            else AIEmpireManager.For(owner)?.AddInstantScience(pts);
            return Loc.F("Исследования продвинулись на {0:0} очков науки.", pts);
        }

        /// <summary>Знакомые владельцу империи ИИ (кроме него самого).</summary>
        private static IEnumerable<AIEmpireManager> KnownAIs(int owner)
        {
            foreach (var ai in AIEmpireManager.Alive)
                if (ai.OwnerId != owner && Contacts.Met(owner, ai.OwnerId)) yield return ai;
        }

        private static bool AnyContact(int owner)
        {
            foreach (var _ in KnownAIs(owner)) return true;
            return owner != 0 && Contacts.Met(owner, 0);
        }

        /// <summary>
        /// Знакомые империи лучше относятся к владельцу. Игроку — память ИИ о нём (key);
        /// у ИИ — старые обиды с другими ИИ сглаживаются на delta.
        /// </summary>
        private static int Goodwill(int owner, string key, float delta)
        {
            int n = 0;
            foreach (var ai in KnownAIs(owner))
            {
                if (owner == 0) ai.AddMemory(key, delta);
                else AIRelations.AddGrudge(owner, ai.OwnerId, delta);
                n++;
            }
            return n;
        }

        private static List<StarSystem> UnsurveyedNear(int owner, int jumps)
        {
            var dist = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (var s in EmpireStats.Systems)
                if (s.OwnerId == owner) { dist[s.Id] = 0; q.Enqueue(s.Id); }
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                if (dist[cur] >= jumps) continue;
                var cs = EmpireStats.GetSystem(cur);
                if (cs == null) continue;
                foreach (int n in cs.ConnectedSystemIds)
                    if (EmpireStats.GetSystem(n) != null && !dist.ContainsKey(n)) { dist[n] = dist[cur] + 1; q.Enqueue(n); }
            }
            var list = new List<StarSystem>();
            foreach (var kv in dist)
            {
                var s = EmpireStats.GetSystem(kv.Key);
                if (s != null && !s.IsSurveyedBy(owner)) list.Add(s);
            }
            return list;
        }

        private static string Survey(int owner, int jumps)
        {
            var near = UnsurveyedNear(owner, jumps);
            foreach (var s in near)
            {
                s.GeneratePlanets();
                s.MarkSurveyedBy(owner);
                if (owner == 0) { s.IsSurveyed = true; Vision.MarkExplored(0, s.Id); }
            }
            if (owner == 0) GalaxyView.Instance?.RefreshTerritoryVisuals();
            return Loc.F("Изучено систем: {0}.", near.Count);
        }

        private static int DamagedShips(int owner)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return 0;
            int n = 0;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.OwnerId != owner) continue;
                if (fm.NeedsRepair(d) || d.ShieldPoints < d.MaxShieldPoints - 0.5f) n++;
            }
            return n;
        }

        private static string RepairAll(int owner)
        {
            var fm = FleetManager.Instance;
            int n = 0;
            if (fm != null)
                foreach (var f in fm.AllFleets)
                {
                    var d = f?.Data;
                    if (d == null || d.Destroyed || d.OwnerId != owner) continue;
                    d.HullPoints = d.MaxHullPoints; d.ArmorPoints = d.MaxArmorPoints; d.ShieldPoints = d.MaxShieldPoints;
                    n++;
                }
            return Loc.F("Отремонтировано кораблей: {0}.", n);
        }

        private static int OrganicPops(int owner)
        {
            int n = 0;
            foreach (var p in ColoniesOf(owner)) n += p.OrganicPops;
            return n;
        }

        private static string Reflash(int owner)
        {
            int founder = SpeciesRegistry.FounderOf(owner), n = 0;
            foreach (var p in new List<PlanetData>(ColoniesOf(owner)))
                for (int k = 0; k < 2; k++)
                {
                    int from = p.DominantOrganic;
                    if (from < 0 || from == founder) break;
                    n += p.ConvertPops(from, founder, 1);
                }
            return Loc.F("Синтетами стали {0} жителей.", n);
        }

        private static int YearsIn => TimeManager.Instance != null ? TimeManager.Instance.Year - 2200 : 0;
        private static int MonthNow => TimeManager.Instance != null ? TimeManager.Instance.Month : 1;

        private static bool AtWar(int owner)
        {
            if (owner != 0) { var me = AIEmpireManager.For(owner); return me != null && me.AtWarWithAnyone; }
            foreach (var ai in AIEmpireManager.Alive) if (ai.AtWar) return true;
            return false;
        }

        private static bool HasPartner(int owner)
        {
            if (owner != 0) { var me = AIEmpireManager.For(owner); return me != null && (me.HasPact || me.HasAlliance || AICoalition.IsMember(owner)); }
            foreach (var ai in AIEmpireManager.Alive) if (ai.HasPact || ai.HasAlliance) return true;
            return false;
        }

        /// <summary>Знакомые империи воюют между собой (без владельца).</summary>
        private static bool OthersAtWar(int owner)
        {
            foreach (var ai in KnownAIs(owner))
            {
                if (ai.WarsWithAIs <= 0) continue;
                if (owner == 0 || !AIRelations.AtWar(owner, ai.OwnerId)) return true;
            }
            return false;
        }

        /// <summary>Знакомый сосед (или игрок) заметно сильнее флота владельца.</summary>
        private static bool Outgunned(int owner)
        {
            float mine = EmpireStats.MilitaryPower(owner);
            foreach (var ai in KnownAIs(owner))
                if (EmpireStats.MilitaryPower(ai.OwnerId) > mine * 1.3f + 50f) return true;
            return owner != 0 && Contacts.Met(owner, 0) && EmpireStats.MilitaryPower(0) > mine * 1.3f + 50f;
        }

        private static bool Unrest(int owner)
        {
            foreach (var p in ColoniesOf(owner)) if (p.Stability < 40f) return true;
            return false;
        }

        private static float AvgStability(int owner)
        {
            float sum = 0f; int n = 0;
            foreach (var p in ColoniesOf(owner)) { sum += p.Stability; n++; }
            return n > 0 ? sum / n : 0f;
        }

        private static bool EnergyCrisis(int owner)
        {
            if (owner != 0) { var ai = AIEmpireManager.For(owner); return ai != null && (ai.IsBankrupt || ai.MonthlyEnergyIncome < 0f); }
            var eco = EconomyManager.Instance;
            return eco != null && (eco.IsBankrupt || eco.MonthlyEnergyIncome < 0f);
        }

        /// <summary>Склады титана заполнены на 85% и больше.</summary>
        public static bool StorageFull(int owner) => StorageFill(owner) >= 0.85f;

        public static float StorageFill(int owner)
        {
            if (owner != 0) { var ai = AIEmpireManager.For(owner); return ai != null ? ai.Minerals / Mathf.Max(1f, ai.MineralStorage) : 0f; }
            var eco = EconomyManager.Instance;
            return eco != null ? eco.Minerals / Mathf.Max(1f, eco.MineralStorage) : 0f;
        }

        // ================================================================ Решения

        private static DecisionDef D(string id, string faction, DecisionCategory c, LGIcon icon, string name, string desc)
        {
            var d = new DecisionDef { Id = id, Faction = faction, Category = c, Icon = icon, Name = name, Desc = desc };
            s_all.Add(d);
            return d;
        }

        /// <summary>
        /// Цены подобраны по модели экономики (balance/sim.py): титан от решения возвращается ресурсами примерно
        /// на 50–75% своей номинальной цены (гелий-3 ×1, титан ×1,2, сплавы ×3, влияние ×4, наука ×1,5) —
        /// излишки титана выгоднее пустить в решения, чем терять на переполненных складах. Возможности
        /// с таймером — заметно выгоднее (это награда за удачу), великие проекты окупаются за 10–15 лет.
        /// </summary>
        private static void EnsureDefs()
        {
            if (s_all.Count > 0) return;
            const DecisionCategory F = DecisionCategory.Faction, Eco = DecisionCategory.Economy, Sci = DecisionCategory.Science,
                                   Mil = DecisionCategory.Military, Soc = DecisionCategory.Society;

            // ---------------------------------------------------------------- Экономика
            D("dec_smelt", null, Eco, LGIcon.Alloys, Loc.T("Переплавка запасов"),
                Loc.T("Плавильни работают в три смены: титан со складов уходит в печи и выходит сплавами для верфей."))
                .Cost(minerals: 600f).Time(30, 90)
                .When(o => YearsIn >= 3)
                .Line(() => Loc.F("Через 30 дн.: +{0:0} сплавов", 160f * Scale))
                .Done((o, s) => { Gain(o, alloys: 160f * s); return Loc.F("Склады получили {0:0} сплавов.", 160f * s); });

            D("dec_exchange", null, Eco, LGIcon.Trade, Loc.T("Сырьевая биржа"),
                Loc.T("Титан продают перекупщикам из нейтральных систем. Платят гелием-3 — топливом, которого вечно не хватает флоту."))
                .Cost(minerals: 600f).Time(20, 120)
                .When(o => YearsIn >= 5)
                .Line(() => Loc.F("Через 20 дн.: +{0:0} гелия-3", 420f * Scale))
                .Done((o, s) => { Gain(o, energy: 420f * s); return Loc.F("Выручка: {0:0} гелия-3.", 420f * s); });

            D("dec_bonds", null, Eco, LGIcon.Influence, Loc.T("Титановые облигации"),
                Loc.T("Казна выпускает облигации, обеспеченные титаном. Держатели — влиятельные кланы и корпорации — теперь на вашей стороне."))
                .Cost(minerals: 450f).Time(0, 360)
                .When(o => YearsIn >= 10 && Colonies(o) >= 3)
                .Line(Loc.T("+4 влияния в месяц на 12 месяцев"))
                .Done((o, s) =>
                {
                    MonthlyIncome(o, "dec_bonds", Loc.T("Титановые облигации"), Loc.T("Держатели облигаций поддерживают правительство"), 12, influence: 4f);
                    return Loc.T("Влияние +4 в месяц на год.");
                });

            // ---------------------------------------------------------------- Наука
            D("dec_grant", null, Sci, LGIcon.Research, Loc.T("Научный грант"),
                Loc.T("Щедрый грант лучшим лабораториям: оборудование из титана, лучшие умы — на текущие исследования."))
                .Cost(minerals: 500f, influence: 10f).Time(0, 300)
                .When(o => YearsIn >= 5)
                .Line(() => Loc.F("Сразу +{0:0} науки (4 месяца исследований)", MonthlyScience * 4f))
                .Done((o, s) => GiveScience(o, 4f));

            D("dec_expedition", null, Sci, LGIcon.Sensors, Loc.T("Дальняя экспедиция"),
                Loc.T("Караван исследовательских зондов уходит за границу на два месяца и картографирует всё, до чего дотянется."))
                .Cost(minerals: 400f, energy: 150f).Time(60, 360)
                .Line(Loc.T("Через 60 дн.: изучены все системы в 4 прыжках от границ"))
                .When(o => YearsIn >= 2 && UnsurveyedNear(o, 4).Count > 0)
                .Done((o, s) => Survey(o, 4));

            D("dec_labs", null, Sci, LGIcon.Sensors, Loc.T("Ночные смены в лабораториях"),
                Loc.T("Учёные получают новые корпуса и трёхсменный график. Науки больше, но заводы делятся мощностями."))
                .Cost(minerals: 450f).Time(360, 540)
                .When(o => YearsIn >= 15 || Colonies(o) >= 5)
                .Mod((b, k) => { b.ResearchMult += 0.20f * k; })
                .Line(Loc.T("+20% науки на 360 дн."));

            // ---------------------------------------------------------------- Армия
            D("dec_drydock", null, Mil, LGIcon.Wrench, Loc.T("Ремонтные доки"),
                Loc.T("Полевые доки из титановых ферм собирают прямо на орбитах: весь флот встаёт в строй за неделю."))
                .Cost(minerals: 300f, energy: 100f).Time(0, 180)
                .Line(Loc.T("Сразу: все корабли полностью отремонтированы"))
                .When(o => YearsIn >= 3 && DamagedShips(o) > 0)
                .Done((o, s) => RepairAll(o));

            D("dec_drills", null, Mil, LGIcon.Target, Loc.T("Военные учения"),
                Loc.T("Флот отрабатывает манёвры на титановых мишенях. Экипажи стреляют быстрее и точнее."))
                .Cost(minerals: 500f, influence: 10f).Time(240, 360)
                .When(o => YearsIn >= 8)
                .Mod((b, k) => { b.AllDamage += 0.10f * k; b.Accuracy += 5f * k; })
                .Line(Loc.T("+10% урона и +5 точности на 240 дн."));

            D("dec_shipyards", null, Mil, LGIcon.Shipyard, Loc.T("Ускоренное судостроение"),
                Loc.T("Верфи получают запас титана на годы вперёд: корпуса штампуют потоком, и каждый корабль обходится дешевле."))
                .Cost(minerals: 600f).Time(240, 540)
                .When(o => YearsIn >= 12)
                .Mod((b, k) => { b.ShipCostMult -= 0.20f * k; })
                .Line(Loc.T("−20% цены кораблей на 240 дн."));

            // ---------------------------------------------------------------- Общество
            D("dec_monument", null, Soc, LGIcon.Construction, Loc.T("Монументальное строительство"),
                Loc.T("Титановые монументы героям и основателям встают в каждой столице. Граждане гордятся империей."))
                .Cost(minerals: 600f).Time(360, 720)
                .When(o => Colonies(o) >= 5)
                .Stab(8f)
                .Line(Loc.T("Действует 360 дн."));

            D("dec_colonial", null, Soc, LGIcon.Population, Loc.T("Колониальные субсидии"),
                Loc.T("Переселенцам — жильё, кредиты и титановые купола. Колонии растут быстрее."))
                .Cost(minerals: 400f, energy: 100f).Time(360, 540)
                .When(o => Colonies(o) >= 3)
                .Mod((b, k) => { b.PopGrowth += 0.50f * k; })
                .Line(Loc.T("+50% роста населения на 360 дн."));

            // ================================================================ По обстоятельствам
            // Появляются, когда сложились условия, и исчезают вместе с ними; возможности ждут считанные дни.

            D("eco_crisis", null, Eco, LGIcon.Warning, Loc.T("Чрезвычайный бюджет"),
                Loc.T("Казна в минусе. Титан срочно продают за гелий-3, лишь бы флот не встал на прикол."))
                .Cost(minerals: 500f).Time(0, 360)
                .When(EnergyCrisis)
                .Cond(Loc.T("Появляется, когда гелий-3 уходит в минус"))
                .Line(() => Loc.F("Сразу +{0:0} гелия-3", 450f * Scale))
                .Done((o, s) => { Gain(o, energy: 450f * s); return Loc.F("Казна получила {0:0} гелия-3.", 450f * s); });

            D("eco_selloff", null, Eco, LGIcon.Trade, Loc.T("Распродажа излишков"),
                Loc.T("Склады ломятся от титана. Половину — на внешний рынок: за гелий-3 и за благодарность торговых домов."))
                .Cost(minerals: 1500f).Time(0, 180)
                .When(StorageFull)
                .Cond(Loc.T("Появляется, когда склады титана почти полны"))
                .Line(() => Loc.F("Сразу +{0:0} гелия-3 и +30 влияния", 700f * Scale))
                .Done((o, s) => { Gain(o, energy: 700f * s, influence: 30f); return Loc.F("Выручка: {0:0} гелия-3 и 30 влияния.", 700f * s); });

            D("eco_warbonds", null, Eco, LGIcon.Alloys, Loc.T("Военные облигации"),
                Loc.T("Граждане покупают облигации победы. Деньги идут прямо на заводы, броня — прямо на фронт."))
                .Cost(minerals: 500f, influence: 10f).Time(0, 360)
                .When(AtWar)
                .Cond(Loc.T("Только во время войны"))
                .Line(() => Loc.F("+{0:0} сплавов в месяц на 6 месяцев", 20f * Scale))
                .Done((o, s) =>
                {
                    MonthlyIncome(o, "eco_warbonds", Loc.T("Военные облигации"), Loc.T("Сборы на нужды фронта"), 6, alloys: 20f * s);
                    return Loc.F("+{0:0} сплавов в месяц на полгода.", 20f * s);
                });

            D("eco_caravan", null, Eco, LGIcon.Trade, Loc.T("Торговый караван"),
                Loc.T("У границы встал караван вольных торговцев. Сплавы у них дешёвые — но ждать они не станут."))
                .Cost(minerals: 500f).Time(0, 240).Offer(0.06f, 45)
                .Line(() => Loc.F("Сразу +{0:0} сплавов", 220f * Scale))
                .Done((o, s) => { Gain(o, alloys: 220f * s); return Loc.F("Куплено {0:0} сплавов.", 220f * s); });

            D("sci_pioneers", null, Sci, LGIcon.Propulsion, Loc.T("Программа первопроходцев"),
                Loc.T("Молодая империя ставит на разведку: больше зондов, быстрее корабли. Через 15 лет программа устареет."))
                .Cost(minerals: 300f).Time(360, 720)
                .When(o => YearsIn < 15)
                .Mod((b, k) => { b.SurveySpeed += 0.25f * k; b.HyperlaneSpeed += 0.10f * k; })
                .Line(Loc.T("+25% скорости исследования систем и +10% скорости в коридорах на 360 дн."))
                .Cond(Loc.T("Доступно только первые 15 лет"));

            D("sci_exchange", null, Sci, LGIcon.Handshake, Loc.T("Обмен учёными"),
                Loc.T("Партнёры по договору открывают свои лаборатории. Свежие идеи ускоряют исследования."))
                .Cost(minerals: 400f, influence: 20f).Time(360, 540)
                .When(HasPartner)
                .Mod((b, k) => { b.ResearchMult += 0.15f * k; })
                .Cond(Loc.T("Нужен пакт или союз с другой империей"))
                .Line(Loc.T("+15% науки на 360 дн."));

            D("sci_defector", null, Sci, LGIcon.Research, Loc.T("Учёный-перебежчик"),
                Loc.T("Светило чужой науки просит убежища — с чертежами и тетрадями. Решать надо быстро, пока его не хватились."))
                .Cost(influence: 25f).Time(0, 360).Offer(0.05f, 60)
                .Line(() => Loc.F("Сразу +{0:0} науки (3 месяца исследований)", MonthlyScience * 3f))
                .Done((o, s) => GiveScience(o, 3f));

            D("sci_horizon", null, Sci, LGIcon.Star, Loc.T("Проект «Горизонт»"),
                Loc.T("Великий проект зрелой империи: научный город на орбите звезды, где работают лучшие умы целых поколений."))
                .Cost(minerals: 2000f).Time(360, 0).Great()
                .When(o => YearsIn >= 50)
                .Mod((b, k) => { b.ResearchMult += 0.15f * k; })
                .Line(Loc.T("Навсегда: +15% науки"))
                .Cond(Loc.T("Открывается на 50-й год"));

            D("mil_totaldef", null, Mil, LGIcon.Defense, Loc.T("Тотальная оборона"),
                Loc.T("Враг у ворот. Каждая база получает щиты и пушки сверх штата, граждане сплачиваются вокруг флага."))
                .Cost(minerals: 500f).Time(120, 360)
                .When(AtWar)
                .Mod((b, k) => { b.StarbaseMult += 0.40f * k; })
                .Cond(Loc.T("Только во время войны"))
                .Line(Loc.T("+40% прочности звёздных баз на 120 дн."))
                .Stab(6f);

            D("mil_armsrace", null, Mil, LGIcon.Shipyard, Loc.T("Гонка вооружений"),
                Loc.T("Сосед строит флот быстрее нас. Верфи получают приоритет — пока не поздно."))
                .Cost(minerals: 600f).Time(240, 540)
                .When(o => !AtWar(o) && Outgunned(o))
                .Mod((b, k) => { b.ShipCostMult -= 0.20f * k; b.NavalCapacity += 4f * k; })
                .Cond(Loc.T("Появляется, когда известный сосед заметно сильнее"))
                .Line(Loc.T("−20% цены кораблей и +4 к флотскому лимиту на 240 дн."));

            D("mil_mercs", null, Mil, LGIcon.Swords, Loc.T("Наёмные инструкторы"),
                Loc.T("Ветераны чужих войн предлагают свои услуги. Дорого, но экипажи учатся стрелять как надо."))
                .Cost(energy: 300f).Time(180, 360).Offer(0.15f, 60)
                .When(AtWar)
                .Mod((b, k) => { b.AllDamage += 0.15f * k; })
                .Line(Loc.T("+15% урона на 180 дн."));

            D("soc_bread", null, Soc, LGIcon.Society, Loc.T("Хлеба и зрелищ"),
                Loc.T("Колонии бурлят. Бесплатные пайки, гонки на орбите и праздники — недовольство утихает."))
                .Cost(minerals: 300f, energy: 200f).Time(180, 360)
                .When(Unrest)
                .Cond(Loc.T("Появляется, когда стабильность колонии ниже 40"))
                .Stab(10f)
                .Line(Loc.T("Действует 180 дн."));

            D("soc_landrush", null, Soc, LGIcon.Starbase, Loc.T("Земельная лихорадка"),
                Loc.T("Каждому, кто застолбит новую систему, — налоговые каникулы. Галактика ещё не поделена, но это ненадолго."))
                .Cost(minerals: 400f).Time(240, 540)
                .When(o => YearsIn < 25)
                .Mod((b, k) => { b.OutpostInfluenceDiscount += 10f * k; })
                .Line(Loc.T("Форпосты на 10 влияния дешевле на 240 дн."))
                .Cond(Loc.T("Доступно только первые 25 лет"));

            D("soc_refugees", null, Soc, LGIcon.Population, Loc.T("Беженцы с окраин"),
                Loc.T("Корабли беженцев просят разрешения сесть. Новые руки нужны колониям — но решать надо сейчас."))
                .Cost(influence: 15f).Time(0, 360).Offer(0.05f, 60)
                .When(o => ColoniesWithRoom(o) > 0)
                .Line(Loc.T("Сразу: +1 житель на 3 колониях со свободным жильём"))
                .Done((o, s) => Loc.F("Беженцы поселились на {0} колониях.", Grow(o, false, 3)));

            D("soc_golden", null, Soc, LGIcon.Trophy, Loc.T("Золотой век"),
                Loc.T("Великий проект процветающей империи: искусство, наука и промышленность на пике. Об этой эпохе будут слагать легенды."))
                .Cost(minerals: 2400f).Time(360, 0).Great()
                .When(o => YearsIn >= 40 && AvgStability(o) >= 60f)
                .Mod((b, k) => { b.EnergyMult += 0.05f * k; b.AlloysMult += 0.05f * k; b.ResearchMult += 0.05f * k; })
                .Line(Loc.T("Навсегда: +5% гелия-3, сплавов и науки"))
                .Stab(3f)
                .Cond(Loc.T("С 40-го года, при средней стабильности от 60"));

            // ================================================================ Фракции

            // ---- Республика Астрея ----
            D("ast_forum", "astrea", F, LGIcon.Diplomacy, Loc.T("Галактический форум"),
                Loc.T("Республика созывает послов всех известных империй на Астрею. Три месяца речей, банкетов и тихих договорённостей."))
                .Cost(minerals: 400f, influence: 30f).Time(90, 720)
                .Line(Loc.T("Через 90 дн.: +12 к отношениям со всеми известными империями"))
                .When(AnyContact)
                .Done((o, s) => Loc.F("Послы {0} империй уехали довольными.", Goodwill(o, "incident", 12f)));
            D("ast_academy", "astrea", F, LGIcon.Research, Loc.T("Академия Республики"),
                Loc.T("Свободный университет для учёных всей галактики. Беженцы-гении едут к нам, а не к соседям."))
                .Cost(minerals: 600f).Time(360, 720)
                .When(o => YearsIn >= 10)
                .Mod((b, k) => { b.ResearchMult += 0.25f * k; })
                .Line(Loc.T("+25% науки на 360 дн."));
            D("ast_charter", "astrea", F, LGIcon.Globe, Loc.T("Хартия колоний"),
                Loc.T("Великий проект: колонии получают самоуправление и голос в Сенате. Республика становится союзом равных."))
                .Cost(minerals: 2000f, influence: 50f).Time(360, 0).Great()
                .When(o => YearsIn >= 20 && Colonies(o) >= 5)
                .Mod((b, k) => { b.PopGrowth += 0.10f * k; })
                .Line(Loc.T("Навсегда: +10% роста населения"))
                .Stab(4f);

            D("ast_peace", "astrea", F, LGIcon.Peace, Loc.T("Миротворческая миссия"),
                Loc.T("Соседи воюют. Республика предлагает переговоры на нейтральной станции — и получает славу миротворца."))
                .Cost(minerals: 400f, influence: 25f).Time(120, 540)
                .When(o => !AtWar(o) && OthersAtWar(o))
                .Cond(Loc.T("Появляется, когда известные империи воюют между собой"))
                .Stab(5f)
                .Line(Loc.T("По окончании: +8 к отношениям со всеми известными империями"))
                .Done((o, s) => Loc.F("Миротворцев благодарят {0} империй.", Goodwill(o, "incident", 8f)));

            // ---- Доминион Ксарн ----
            D("xarn_parade", "xarn", F, LGIcon.Swords, Loc.T("Военный парад"),
                Loc.T("Над столицей проходит весь флот Доминиона. Народ ликует, враги считают стволы."))
                .Cost(minerals: 500f, alloys: 60f).Time(180, 540)
                .When(o => YearsIn >= 5)
                .Mod((b, k) => { b.AllDamage += 0.10f * k; })
                .Stab(6f)
                .Line(Loc.T("+10% урона на 180 дн."));
            D("xarn_mobilize", "xarn", F, LGIcon.Shipyard, Loc.T("Мобилизация верфей"),
                Loc.T("Каждая верфь Доминиона переходит на военный график. Корпуса сходят со стапелей каждый день."))
                .Cost(minerals: 700f, influence: 20f).Time(240, 720)
                .When(o => YearsIn >= 10 || AtWar(o))
                .Mod((b, k) => { b.ShipCostMult -= 0.25f * k; b.NavalCapacity += 8f * k; })
                .Line(Loc.T("−25% цены кораблей и +8 к флотскому лимиту на 240 дн."));
            D("xarn_forge", "xarn", F, LGIcon.Industry, Loc.T("Кузницы Ксарна"),
                Loc.T("Великий проект: в недрах столицы зажигают горны, способные плавить броню целых флотов."))
                .Cost(minerals: 2000f).Time(360, 0).Great()
                .When(o => YearsIn >= 20)
                .Mod((b, k) => { b.AlloysMult += 0.10f * k; b.StarbaseMult += 0.15f * k; })
                .Line(Loc.T("Навсегда: +10% сплавов и +15% прочности звёздных баз"));

            D("xarn_vendetta", "xarn", F, LGIcon.Target, Loc.T("Кровная месть"),
                Loc.T("Клан объявляет врагу кровную месть. Пока она не исполнена, ни один воин Ксарна не отступит."))
                .Cost(minerals: 400f, influence: 15f).Time(120, 360)
                .When(AtWar)
                .Mod((b, k) => { b.AllDamage += 0.15f * k; })
                .Cond(Loc.T("Только во время войны"))
                .Line(Loc.T("+15% урона на 120 дн."));

            // ---- Синдикат Аквила ----
            D("aq_boom", "aquila", F, LGIcon.Energy, Loc.T("Биржевой бум"),
                Loc.T("Синдикат вкладывает титан в гелиевые фьючерсы. Пока пузырь растёт — растёт и казна."))
                .Cost(minerals: 500f).Time(360, 720)
                .When(o => YearsIn >= 8)
                .Mod((b, k) => { b.EnergyMult += 0.30f * k; })
                .Line(Loc.T("+30% гелия-3 на 360 дн."));
            D("aq_lobby", "aquila", F, LGIcon.Influence, Loc.T("Лоббирование"),
                Loc.T("Гелий-3 уходит нужным людям в нужных кабинетах. Через месяц нужные решения принимаются сами."))
                .Cost(energy: 400f).Time(30, 360)
                .When(o => YearsIn >= 3)
                .Line(Loc.T("Через 30 дн.: +50 влияния"))
                .Done((o, s) => { Gain(o, influence: 50f); return Loc.T("Получено 50 влияния."); });
            D("aq_trade", "aquila", F, LGIcon.Trade, Loc.T("Торговая экспансия"),
                Loc.T("Конторы Синдиката открываются во всех известных столицах. Торговля сближает — и приносит гелий."))
                .Cost(minerals: 500f, influence: 20f).Time(120, 720)
                .Line(Loc.T("Через 120 дн.: +12 к отношениям с известными империями"))
                .Line(() => Loc.F("и +{0:0} гелия-3 в месяц на 12 месяцев", 12f * Scale))
                .When(AnyContact)
                .Done((o, s) =>
                {
                    int n = Goodwill(o, "trade", 12f);
                    MonthlyIncome(o, "aq_trade", Loc.T("Торговая экспансия"), Loc.T("Конторы Синдиката в чужих столицах"), 12, energy: 12f * s);
                    return Loc.F("Конторы открыты в {0} столицах.", n);
                });
            D("aq_exchange", "aquila", F, LGIcon.Trade, Loc.T("Галактическая биржа"),
                Loc.T("Великий проект: главная биржа галактики. Через неё проходит каждый контракт — и с каждого Синдикат берёт свою долю."))
                .Cost(minerals: 2000f).Time(360, 0).Great()
                .When(o => YearsIn >= 20)
                .Mod((b, k) => { b.EnergyMult += 0.10f * k; b.InfluenceFlat += 1f * k; })
                .Line(Loc.T("Навсегда: +10% гелия-3 и +1 влияния в месяц"));

            D("aq_buyout", "aquila", F, LGIcon.Alloys, Loc.T("Скупка активов"),
                Loc.T("Чужой концерн разорился. Синдикат забирает его склады сплавов за бесценок — если успеет первым."))
                .Cost(energy: 400f).Time(0, 360).Offer(0.06f, 30)
                .Line(() => Loc.F("Сразу +{0:0} сплавов и +15 влияния", 250f * Scale))
                .Done((o, s) => { Gain(o, alloys: 250f * s, influence: 15f); return Loc.F("Получено {0:0} сплавов и 15 влияния.", 250f * s); });

            // ---- Иридийский Оракул ----
            D("ir_vision", "iridia", F, LGIcon.Star, Loc.T("Видение будущего"),
                Loc.T("Верховная Оракул уходит в транс на месяц. Вернувшись, она диктует учёным то, до чего они дошли бы лишь через полгода."))
                .Cost(influence: 35f).Time(30, 540)
                .When(o => YearsIn >= 3)
                .Line(() => Loc.F("Через 30 дн.: +{0:0} науки (5 месяцев исследований)", MonthlyScience * 5f))
                .Done((o, s) => GiveScience(o, 5f));
            D("ir_pilgrimage", "iridia", F, LGIcon.Society, Loc.T("Паломничество к Саду"),
                Loc.T("Миллионы верующих идут к миру-саду Иридии. Колонии светятся единой мыслью, рождается больше детей."))
                .Cost(minerals: 500f).Time(180, 540)
                .When(o => Colonies(o) >= 3)
                .Mod((b, k) => { b.PopGrowth += 0.10f * k; })
                .Stab(10f)
                .Line(Loc.T("+10% роста населения на 180 дн."));
            D("ir_temple", "iridia", F, LGIcon.Globe, Loc.T("Храм Предвидения"),
                Loc.T("Великий проект: хрустальный храм, где сотни провидцев читают нити будущего день и ночь."))
                .Cost(minerals: 2000f).Time(360, 0).Great()
                .When(o => YearsIn >= 20)
                .Mod((b, k) => { b.InfluenceFlat += 1.5f * k; b.ResearchMult += 0.05f * k; })
                .Line(Loc.T("Навсегда: +1.5 влияния в месяц и +5% науки"));

            D("ir_omen", "iridia", F, LGIcon.Star, Loc.T("Знамение звёзд"),
                Loc.T("Звёзды выстроились в знак, который видят раз в поколение. Если провидцы истолкуют его сейчас — откроется многое."))
                .Cost(influence: 20f).Time(180, 540).Offer(0.05f, 60)
                .Mod((b, k) => { b.ResearchMult += 0.25f * k; })
                .Line(Loc.T("+25% науки на 180 дн."));

            // ---- Хлорофиловый Конклав Тэрра'ан ----
            D("ter_bloom", "terraan", F, LGIcon.Population, Loc.T("Великое цветение"),
                Loc.T("Конклав пробуждает спящие семена на всех мирах сразу. Через два месяца сад приносит новых жителей."))
                .Cost(minerals: 600f, energy: 150f).Time(60, 720)
                .When(o => Colonies(o) >= 3)
                .Line(Loc.T("Через 60 дн.: +1 житель на каждой колонии со свободным жильём"))
                .Need(o => ColoniesWithRoom(o) == 0 ? Loc.T("Нет свободного жилья") : null)
                .Done((o, s) => Loc.F("Новые жители на {0} колониях.", Grow(o, false)));
            D("ter_spores", "terraan", F, LGIcon.Sensors, Loc.T("Семена спор"),
                Loc.T("Споры мицелия разлетаются по коридорам и прорастают сенсорной сетью в чужих системах."))
                .Cost(energy: 250f).Time(45, 540)
                .Line(Loc.T("Через 45 дн.: изучены все системы в 5 прыжках от границ"))
                .When(o => UnsurveyedNear(o, 5).Count > 0)
                .Done((o, s) => Survey(o, 5));
            D("ter_roots", "terraan", F, LGIcon.Defense, Loc.T("Корневая оборона"),
                Loc.T("Корни оплетают звёздные базы, а сок сада питает экипажи. Защищаться дешевле и легче."))
                .Cost(minerals: 600f).Time(360, 900)
                .When(o => YearsIn >= 10)
                .Mod((b, k) => { b.StarbaseMult += 0.30f * k; b.UpkeepMult -= 0.15f * k; })
                .Line(Loc.T("+30% прочности звёздных баз и −15% содержания флота на 360 дн."));
            D("ter_worldtree", "terraan", F, LGIcon.Planet, Loc.T("Древо Миров"),
                Loc.T("Великий проект: из семени Первого Сада вырастает древо, чьи корни соединяют все миры Конклава."))
                .Cost(minerals: 2000f).Time(360, 0).Great()
                .When(o => YearsIn >= 20)
                .Mod((b, k) => { b.PopGrowth += 0.15f * k; })
                .Line(Loc.T("Навсегда: +15% роста населения"))
                .Stab(3f);

            D("ter_spring", "terraan", F, LGIcon.Population, Loc.T("Сезон цветения"),
                Loc.T("Весной соки сада текут быстрее. Конклав может направить их в рост новых жителей — но только пока весна."))
                .Cost(minerals: 400f).Time(90, 270)
                .When(o => MonthNow >= 3 && MonthNow <= 5)
                .Mod((b, k) => { b.PopGrowth += 0.40f * k; })
                .Cond(Loc.T("Только весной (март–май)"))
                .Line(Loc.T("+40% роста населения на 90 дн."));

            // ---- Машинный Консенсус «Ноль» ----
            D("nol_reflash", "nol", F, LGIcon.Gear, Loc.T("Перепрошивка"),
                Loc.T("Органики на мирах Консенсуса проходят «добровольную» загрузку в синтетические тела."))
                .Cost(minerals: 500f, influence: 25f).Time(60, 540)
                .Line(Loc.T("Через 60 дн.: до 2 органиков на каждой колонии станут синтетами"))
                .When(o => OrganicPops(o) > 0)
                .Done((o, s) => Reflash(o));
            D("nol_hive", "nol", F, LGIcon.Population, Loc.T("Фабрики-ульи"),
                Loc.T("Сборочные линии работают без остановки: каждый мир Консенсуса получает новый синтет."))
                .Cost(minerals: 800f, alloys: 100f).Time(90, 720)
                .When(o => Colonies(o) >= 3)
                .Line(Loc.T("Через 90 дн.: +1 синтет на каждой колонии со свободным жильём"))
                .Need(o => ColoniesWithRoom(o) == 0 ? Loc.T("Нет свободного жилья") : null)
                .Done((o, s) => Loc.F("Новые синтеты на {0} колониях.", Grow(o, true)));
            D("nol_optimize", "nol", F, LGIcon.Reactor, Loc.T("Протокол оптимизации"),
                Loc.T("Консенсус пересчитывает каждую цепочку производства. Лишних операций больше нет."))
                .Cost(minerals: 600f).Time(360, 900)
                .When(o => YearsIn >= 10)
                .Mod((b, k) => { b.EnergyMult += 0.10f * k; b.AlloysMult += 0.10f * k; b.ResearchMult += 0.10f * k; })
                .Line(Loc.T("+10% гелия-3, сплавов и науки на 360 дн."));
            D("nol_hack", "nol", F, LGIcon.Betrayal, Loc.T("Взлом вражеских сетей"),
                Loc.T("Консенсус нашёл дыру в протоколах противника. Окно открыто недолго: прицелы врага видят то, что мы им покажем."))
                .Cost(influence: 25f).Time(120, 360).Offer(0.12f, 45)
                .When(AtWar)
                .Mod((b, k) => { b.AllDamage += 0.10f * k; b.Accuracy += 10f * k; })
                .Line(Loc.T("+10% урона и +10 точности на 120 дн."));
            D("nol_core", "nol", F, LGIcon.Monitor, Loc.T("Ядро Консенсуса"),
                Loc.T("Великий проект: планетарный вычислитель, в котором сходятся все мысли «Ноля»."))
                .Cost(minerals: 2000f).Time(360, 0).Great()
                .When(o => YearsIn >= 20)
                .Mod((b, k) => { b.ResearchMult += 0.10f * k; b.InfluenceFlat += 1.5f * k; })
                .Line(Loc.T("Навсегда: +10% науки и +1.5 влияния в месяц"));
        }
    }
}
