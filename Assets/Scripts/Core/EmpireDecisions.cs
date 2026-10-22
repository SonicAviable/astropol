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
    /// временного усиления или — у великих проектов — постоянного бонуса.
    /// </summary>
    public sealed class DecisionDef
    {
        public string Id;
        /// <summary>Ключ фракции (FactionRegistry.KeyOf) или null — решение доступно всем.</summary>
        public string Faction;
        public DecisionCategory Category;
        public LGIcon Icon;
        public string Name, Desc;

        /// <summary>Цена: титан, гелий-3 и сплавы растут с размером империи (<see cref="EmpireDecisions.Scale"/>), влияние — нет.</summary>
        public float Energy, Minerals, Alloys, Influence;
        /// <summary>Сколько дней идёт решение (0 — мгновенно) и через сколько дней после конца его можно принять снова.</summary>
        public int Days, Cooldown;
        /// <summary>Великий проект: принимается один раз за партию, бонус остаётся навсегда.</summary>
        public bool Once;
        /// <summary>Прибавка к стабильности колоний, пока решение действует (у великих проектов — навсегда).</summary>
        public float Stability;
        /// <summary>Бонус империи (+1 наложить, −1 снять): пока идёт решение, у великого проекта — после завершения.</summary>
        public Action<EmpireBonuses, float> Apply;
        /// <summary>Итог по завершении (масштаб на момент принятия) → текст уведомления.</summary>
        public Func<float, string> OnComplete;
        /// <summary>Особое условие (null — выполнено).</summary>
        public Func<string> Requirement;
        /// <summary>Когда решение вообще видно (null — всегда): война, кризис, эпоха, сезон… Условие пропало — решение исчезает.</summary>
        public Func<bool> Shown;
        /// <summary>Возможность: при условии <see cref="Shown"/> раз в месяц появляется с шансом <see cref="OfferChance"/> и ждёт <see cref="OfferDays"/> дней.</summary>
        public int OfferDays;
        public float OfferChance;
        public readonly List<(Func<string> Text, bool Good)> Effects = new List<(Func<string>, bool)>();

        public DecisionDef Cost(float minerals = 0f, float energy = 0f, float alloys = 0f, float influence = 0f)
        { Minerals = minerals; Energy = energy; Alloys = alloys; Influence = influence; return this; }
        public DecisionDef Time(int days, int cooldown) { Days = days; Cooldown = cooldown; return this; }
        public DecisionDef Great() { Once = true; return this; }
        public DecisionDef Mod(Action<EmpireBonuses, float> apply) { Apply = apply; return this; }
        public DecisionDef Stab(float s) { Stability = s; return Line(Loc.F("+{0:0} стабильности колоний", s), true); }
        public DecisionDef Done(Func<float, string> f) { OnComplete = f; return this; }
        public DecisionDef Need(Func<string> f) { Requirement = f; return this; }
        public DecisionDef When(Func<bool> f) { Shown = f; return this; }
        /// <summary>Подпись условия («только во время войны») — приглушённо на карточке и в подсказке.</summary>
        public string Note;
        public DecisionDef Cond(string note) { Note = note; return this; }
        public DecisionDef Offer(float monthlyChance, int days) { OfferChance = monthlyChance; OfferDays = days; return this; }
        public bool IsOffer => OfferDays > 0;
        public DecisionDef Line(string text, bool good = true) { Effects.Add((() => text, good)); return this; }
        public DecisionDef Line(Func<string> text, bool good = true) { Effects.Add((text, good)); return this; }

        public float EnergyCost => Energy * EmpireDecisions.Scale;
        public float MineralsCost => Minerals * EmpireDecisions.Scale;
        public float AlloysCost => Alloys * EmpireDecisions.Scale;
        /// <summary>Решение с длительным усилением (а не с итогом в конце).</summary>
        public bool IsModifier => Apply != null || Stability > 0f;
    }

    [Serializable]
    public class DecisionsSave
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

    /// <summary>
    /// Решения империи — как в Hearts of Iron: у каждой фракции свои особые решения и великий проект,
    /// плюс общие решения для всех. Решение стоит ресурсов (в первую очередь титана — так излишки сырья
    /// превращаются в сплавы, гелий-3, науку и влияние), идёт несколько дней и потом закрывается на перезарядку.
    /// На старте видно лишь несколько решений: остальные открываются с годами, ростом империи и обстоятельствами.
    /// Одновременно — не больше <see cref="MaxActive"/>. Бонусы ложатся на бонусы игрока по версии, как законы.
    /// </summary>
    public static class EmpireDecisions
    {
        public const int MaxActive = 3;

        private sealed class Running { public DecisionDef Def; public int Start, End; public float Scale; }

        private static readonly List<DecisionDef> s_all = new List<DecisionDef>();
        private static readonly List<Running> s_active = new List<Running>();
        private static readonly Dictionary<string, int> s_cooldown = new Dictionary<string, int>();
        private static readonly HashSet<string> s_completed = new HashSet<string>();
        /// <summary>Открытые возможности: до какого дня ждут.</summary>
        private static readonly Dictionary<string, int> s_offers = new Dictionary<string, int>();
        /// <summary>Решения, которые уже были видны (чтобы сообщать только о новых), и ещё не просмотренные игроком.</summary>
        private static readonly HashSet<string> s_known = new HashSet<string>();
        private static readonly HashSet<string> s_unseen = new HashSet<string>();
        private static bool s_scanned;
        /// <summary>Когда о решении сообщали в последний раз — чтобы мигающие условия (бой, баланс гелия) не засыпали уведомлениями.</summary>
        private static readonly Dictionary<string, int> s_notified = new Dictionary<string, int>();
        private const int RenotifyDays = 120;
        private static int s_version = 1;

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

        /// <summary>Масштаб цен и наград по размеру империи: 1 колония — ×1, каждая следующая +6%, не больше ×2.</summary>
        public static float Scale => Mathf.Clamp(1f + 0.06f * (EmpireStats.Colonies(0) - 1), 1f, 2f);

        private static bool OwnFaction(DecisionDef d) => d.Faction == null || d.Faction == FactionTraits.Key(0);

        /// <summary>Видно ли решение сейчас: идущее и построенное — всегда, возможность — пока не истекла, остальное — по условию.</summary>
        public static bool IsVisible(DecisionDef d)
        {
            if (d == null || !OwnFaction(d)) return false;
            if (IsActive(d) || IsDone(d)) return true;
            if (d.IsOffer) return s_offers.ContainsKey(d.Id);
            return d.Shown == null || d.Shown();
        }

        /// <summary>Решения раздела, которые видит игрок: сначала возможности с таймером, потом остальные.</summary>
        public static IEnumerable<DecisionDef> Visible(DecisionCategory c)
        {
            EnsureDefs();
            foreach (var d in s_all) if (d.Category == c && d.IsOffer && IsVisible(d)) yield return d;
            foreach (var d in s_all) if (d.Category == c && !d.IsOffer && IsVisible(d)) yield return d;
        }

        /// <summary>Сколько решений можно принять прямо сейчас (счётчик на кнопке меню).</summary>
        public static int AvailableCount
        {
            get
            {
                EnsureDefs();
                int n = 0;
                foreach (var d in s_all) if (IsVisible(d) && Blocker(d) == null) n++;
                return n;
            }
        }

        public static bool IsNew(DecisionDef d) => d != null && s_unseen.Contains(d.Id);
        public static int UnseenCount => s_unseen.Count;
        /// <summary>Игрок посмотрел вкладку решений — метки «новое» снимаются.</summary>
        public static void MarkSeen() { if (s_unseen.Count > 0) { s_unseen.Clear(); OnChanged?.Invoke(); } }

        /// <summary>Сколько дней ещё ждёт возможность.</summary>
        public static int OfferLeft(DecisionDef d)
            => d != null && s_offers.TryGetValue(d.Id, out int until) ? Mathf.Max(0, until - EmpireLaws.Today()) : 0;

        // ================================================================ Состояние

        private static Running Find(DecisionDef d)
        {
            foreach (var r in s_active) if (r.Def == d) return r;
            return null;
        }

        public static bool IsActive(DecisionDef d) => Find(d) != null;
        public static bool IsDone(DecisionDef d) => d != null && d.Once && s_completed.Contains(d.Id);
        public static int ActiveCount => s_active.Count;

        public static int DaysLeft(DecisionDef d) { var r = Find(d); return r == null ? 0 : Mathf.Max(0, r.End - EmpireLaws.Today()); }

        /// <summary>Доля выполнения идущего решения (0…1).</summary>
        public static float Progress(DecisionDef d)
        {
            var r = Find(d);
            if (r == null) return 0f;
            return Mathf.Clamp01((EmpireLaws.Today() - r.Start) / (float)Mathf.Max(1, r.End - r.Start));
        }

        public static int CooldownLeft(DecisionDef d)
            => d != null && s_cooldown.TryGetValue(d.Id, out int until) ? Mathf.Max(0, until - EmpireLaws.Today()) : 0;

        /// <summary>Почему нельзя принять решение (null — можно).</summary>
        public static string Blocker(DecisionDef d)
        {
            if (d == null) return Loc.T("Нет решения");
            if (IsDone(d)) return Loc.T("Проект завершён");
            if (IsActive(d)) return Loc.F("Идёт: ещё {0} дн.", DaysLeft(d));
            int cd = CooldownLeft(d);
            if (cd > 0) return Loc.F("Снова через {0} дн.", cd);
            if (s_active.Count >= MaxActive) return Loc.F("Уже идут {0} решения", MaxActive);
            string req = d.Requirement?.Invoke();
            if (req != null) return req;
            var eco = EconomyManager.Instance;
            if (eco == null) return Loc.T("Нет экономики");
            // Без чисел: подпись на кнопке не меняется каждый день, сколько нужно — видно по красной цене
            if (eco.Minerals < d.MineralsCost) return Loc.T("Не хватает титана");
            if (eco.EnergyCredits < d.EnergyCost) return Loc.T("Не хватает гелия-3");
            if (eco.Alloys < d.AlloysCost) return Loc.T("Не хватает сплавов");
            if (eco.Influence < d.Influence) return Loc.T("Не хватает влияния");
            return null;
        }

        public static bool TryTake(DecisionDef d)
        {
            if (Blocker(d) != null) return false;
            var eco = EconomyManager.Instance;
            if (!eco.TrySpend(d.EnergyCost, d.MineralsCost, d.AlloysCost, d.Influence)) return false;
            float scale = Scale;
            s_offers.Remove(d.Id);
            s_unseen.Remove(d.Id);
            if (d.Days <= 0)
            {
                Finish(d, scale);
            }
            else
            {
                int today = EmpireLaws.Today();
                s_active.Add(new Running { Def = d, Start = today, End = today + d.Days, Scale = scale });
                NotificationCenter.Show(Loc.F("Решение принято: «{0}»", d.Name),
                    d.Once ? Loc.F("Строительство займёт {0} дн.", d.Days)
                    : d.IsModifier ? Loc.F("Действует {0} дн.", d.Days) : Loc.F("Итог — через {0} дн.", d.Days),
                    NotificationCenter.Kind.Info, 5f);
            }
            s_version++;
            eco.RecalculateAll();
            OnChanged?.Invoke();
            return true;
        }

        private static void Finish(DecisionDef d, float scale)
        {
            if (d.Once) s_completed.Add(d.Id);
            else if (d.Cooldown > 0) s_cooldown[d.Id] = EmpireLaws.Today() + d.Cooldown;
            string result = d.OnComplete?.Invoke(scale);
            if (d.Once) result ??= Loc.T("Великий проект завершён — его бонусы действуют до конца партии.");
            else if (d.IsModifier && result == null) result = Loc.T("Срок действия закончился.");
            NotificationCenter.Show(d.IsModifier && !d.Once ? Loc.F("Решение «{0}» завершено", d.Name) : Loc.F("Решение выполнено: «{0}»", d.Name),
                result ?? "", NotificationCenter.Kind.Success, 6f);
        }

        /// <summary>Каждый день: завершить решения, у которых вышел срок; закрыть упущенные возможности; раз в 5 дней — найти новые решения.</summary>
        public static void DailyTick()
        {
            int today = EmpireLaws.Today();
            bool changed = ExpireOffers(today);
            if (!s_scanned || today % 5 == 0) changed |= ScanNew();
            if (s_active.Count == 0) { if (changed) OnChanged?.Invoke(); return; }
            for (int i = s_active.Count - 1; i >= 0; i--)
            {
                var r = s_active[i];
                if (today < r.End) continue;
                s_active.RemoveAt(i);
                Finish(r.Def, r.Scale);
                changed = true;
            }
            if (!changed) return;
            s_version++;
            EconomyManager.Instance?.RecalculateAll();
            OnChanged?.Invoke();
        }

        private static bool ExpireOffers(int today)
        {
            if (s_offers.Count == 0) return false;
            List<string> gone = null;
            foreach (var kv in s_offers) if (today >= kv.Value) (gone ??= new List<string>()).Add(kv.Key);
            if (gone == null) return false;
            foreach (var id in gone)
            {
                s_offers.Remove(id);
                s_unseen.Remove(id);
                var d = Get(id);
                if (d == null) continue;
                s_cooldown[id] = today + Mathf.Max(90, d.Cooldown / 2);   // упущенный шанс вернётся не скоро
                NotificationCenter.Show(Loc.F("Возможность упущена: «{0}»", d.Name), Loc.T("Решение больше недоступно."), NotificationCenter.Kind.Info, 4f);
            }
            return true;
        }

        /// <summary>
        /// Сверить, какие решения стали видны: о новых — уведомление и метка «новое», исчезнувшие забываются
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
                bool vis = IsVisible(d) && !IsDone(d);
                if (vis && s_known.Add(d.Id))
                {
                    if (silent || d.Shown == null || CooldownLeft(d) > 0) continue;
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

        /// <summary>Раз в месяц: случайные возможности с таймером.</summary>
        public static void MonthlyTick()
        {
            EnsureDefs();
            int today = EmpireLaws.Today();
            bool changed = false;
            foreach (var d in s_all)
            {
                if (!d.IsOffer || !OwnFaction(d) || s_offers.ContainsKey(d.Id) || IsActive(d) || CooldownLeft(d) > 0) continue;
                if (d.Shown != null && !d.Shown()) continue;
                if (UnityEngine.Random.value >= d.OfferChance) continue;
                s_offers[d.Id] = today + d.OfferDays;
                s_unseen.Add(d.Id);
                changed = true;
                NotificationCenter.Show(Loc.F("Новая возможность: «{0}»", d.Name),
                    Loc.F("Ждёт {0} дн. — законы (L) → вкладка «Решения»", d.OfferDays), NotificationCenter.Kind.Success, 7f);
            }
            if (changed) OnChanged?.Invoke();
        }

        /// <summary>Бонусы, которые сейчас действуют: идущие решения-усиления и завершённые великие проекты.</summary>
        private static IEnumerable<DecisionDef> Applied()
        {
            foreach (var r in s_active) if (!r.Def.Once) yield return r.Def;
            foreach (var id in s_completed) { var d = Get(id); if (d != null) yield return d; }
        }

        /// <summary>Наложить бонусы решений на бонусы игрока (как законы — по версии, без задвоения).</summary>
        public static void SyncBonuses(EmpireBonuses b)
        {
            if (b == null || b.DecisionsVersion == s_version) return;
            EnsureDefs();
            if (b.DecisionsApplied != null)
                foreach (var id in b.DecisionsApplied) Get(id)?.Apply?.Invoke(b, -1f);
            var applied = new List<string>();
            foreach (var d in Applied())
            {
                if (d.Apply == null) continue;
                d.Apply(b, 1f);
                applied.Add(d.Id);
            }
            b.DecisionsApplied = applied;
            b.DecisionsVersion = s_version;
        }

        public static void StabilityFactors(int owner, Action<string, float> add)
        {
            if (owner != 0 || add == null) return;
            foreach (var d in Applied())
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
            s_active.Clear();
            s_cooldown.Clear();
            s_completed.Clear();
            s_offers.Clear(); s_known.Clear(); s_unseen.Clear(); s_notified.Clear(); s_scanned = false;
            s_version++;
            OnChanged?.Invoke();
        }

        public static DecisionsSave Capture()
        {
            var s = new DecisionsSave();
            foreach (var r in s_active)
            {
                s.ActiveId.Add(r.Def.Id); s.ActiveStart.Add(r.Start); s.ActiveEnd.Add(r.End); s.ActiveScale.Add(r.Scale);
            }
            foreach (var kv in s_cooldown) { s.CooldownId.Add(kv.Key); s.CooldownUntil.Add(kv.Value); }
            s.Completed.AddRange(s_completed);
            foreach (var kv in s_offers) { s.OfferId.Add(kv.Key); s.OfferUntil.Add(kv.Value); }
            return s;
        }

        public static void Restore(DecisionsSave s)
        {
            s_active.Clear();
            s_cooldown.Clear();
            s_completed.Clear();
            s_offers.Clear(); s_known.Clear(); s_unseen.Clear(); s_notified.Clear(); s_scanned = false;
            if (s != null)
            {
                for (int i = 0; i < s.ActiveId.Count && i < s.ActiveEnd.Count; i++)
                {
                    var d = Get(s.ActiveId[i]);
                    if (d == null) continue;
                    s_active.Add(new Running
                    {
                        Def = d, End = s.ActiveEnd[i],
                        Start = i < s.ActiveStart.Count ? s.ActiveStart[i] : s.ActiveEnd[i] - d.Days,
                        Scale = i < s.ActiveScale.Count ? s.ActiveScale[i] : 1f
                    });
                }
                for (int i = 0; i < s.CooldownId.Count && i < s.CooldownUntil.Count; i++) s_cooldown[s.CooldownId[i]] = s.CooldownUntil[i];
                foreach (var id in s.Completed) if (Get(id) != null) s_completed.Add(id);
                if (s.OfferId != null && s.OfferUntil != null)
                    for (int i = 0; i < s.OfferId.Count && i < s.OfferUntil.Count; i++)
                        if (Get(s.OfferId[i]) != null) s_offers[s.OfferId[i]] = s.OfferUntil[i];
            }
            s_version++;
            OnChanged?.Invoke();
        }

        // ================================================================ Помощники эффектов

        private static IEnumerable<PlanetData> PlayerColonies()
        {
            foreach (var sys in EmpireStats.Systems)
            {
                if (sys.OwnerId != 0) continue;
                foreach (var p in sys.Planets) if (p.Population > 0) yield return p;
            }
        }

        private static int ColoniesWithRoom()
        {
            int n = 0;
            foreach (var p in PlayerColonies()) if (p.Population < p.HousingCapacity) n++;
            return n;
        }

        /// <summary>По одному жителю на каждую колонию, где есть свободное жильё.</summary>
        private static int GrowColonies(bool synths)
        {
            int founder = SpeciesRegistry.FounderOf(0), n = 0;
            foreach (var p in PlayerColonies())
            {
                if (p.Population >= p.HousingCapacity) continue;
                p.AddPops(synths ? founder : p.GrowthSpecies(), 1);
                n++;
            }
            return n;
        }

        private static float MonthlyScience => TechnologyManager.Instance != null ? TechnologyManager.Instance.MonthlyResearchIncome : 0f;

        private static string GiveScience(float months)
        {
            float pts = MonthlyScience * months;
            TechnologyManager.Instance?.AddInstantScience(pts);
            return Loc.F("Исследования продвинулись на {0:0} очков науки.", pts);
        }

        private static bool AnyContact()
        {
            foreach (var ai in AIEmpireManager.Alive) if (Contacts.PlayerMet(ai.OwnerId)) return true;
            return false;
        }

        private static int Opinion(string key, float delta)
        {
            int n = 0;
            foreach (var ai in AIEmpireManager.Alive)
            {
                if (!Contacts.PlayerMet(ai.OwnerId)) continue;
                ai.AddMemory(key, delta);
                n++;
            }
            return n;
        }

        private static string Survey(int jumps)
        {
            var near = EventDatabase.UnsurveyedNearPlayer(jumps);
            foreach (var s in near) { s.GeneratePlanets(); s.MarkSurveyedBy(0); s.IsSurveyed = true; Vision.MarkExplored(0, s.Id); }
            GalaxyView.Instance?.RefreshTerritoryVisuals();
            return Loc.F("Изучено систем: {0}.", near.Count);
        }

        private static int DamagedShips()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return 0;
            int n = 0;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.OwnerId != 0) continue;
                if (fm.NeedsRepair(d) || d.ShieldPoints < d.MaxShieldPoints - 0.5f) n++;
            }
            return n;
        }

        private static string RepairAll()
        {
            var fm = FleetManager.Instance;
            int n = 0;
            if (fm != null)
                foreach (var f in fm.AllFleets)
                {
                    var d = f?.Data;
                    if (d == null || d.Destroyed || d.OwnerId != 0) continue;
                    d.HullPoints = d.MaxHullPoints; d.ArmorPoints = d.MaxArmorPoints; d.ShieldPoints = d.MaxShieldPoints;
                    n++;
                }
            return Loc.F("Отремонтировано кораблей: {0}.", n);
        }

        private static int OrganicPops()
        {
            int n = 0;
            foreach (var p in PlayerColonies()) n += p.OrganicPops;
            return n;
        }

        private static string Reflash()
        {
            int founder = SpeciesRegistry.FounderOf(0), n = 0;
            foreach (var p in new List<PlanetData>(PlayerColonies()))
                for (int k = 0; k < 2; k++)
                {
                    int from = p.DominantOrganic;
                    if (from < 0 || from == founder) break;
                    n += p.ConvertPops(from, founder, 1);
                }
            return Loc.F("Синтетами стали {0} жителей.", n);
        }

        private static int Colonies => EmpireStats.Colonies(0);
        private static int YearsIn => TimeManager.Instance != null ? TimeManager.Instance.Year - 2200 : 0;
        private static int MonthNow => TimeManager.Instance != null ? TimeManager.Instance.Month : 1;

        private static bool AtWar()
        {
            foreach (var ai in AIEmpireManager.Alive) if (ai.AtWar) return true;
            return false;
        }

        private static bool HasPartner()
        {
            foreach (var ai in AIEmpireManager.Alive) if (ai.HasPact || ai.HasAlliance) return true;
            return false;
        }

        /// <summary>Известные империи воюют между собой (без нас).</summary>
        private static bool OthersAtWar()
        {
            foreach (var ai in AIEmpireManager.Alive) if (Contacts.PlayerMet(ai.OwnerId) && ai.WarsWithAIs > 0) return true;
            return false;
        }

        /// <summary>Известный сосед заметно сильнее нашего флота.</summary>
        private static bool Outgunned()
        {
            float mine = EmpireStats.MilitaryPower(0);
            foreach (var ai in AIEmpireManager.Alive)
                if (Contacts.PlayerMet(ai.OwnerId) && EmpireStats.MilitaryPower(ai.OwnerId) > mine * 1.3f + 50f) return true;
            return false;
        }

        private static bool Unrest()
        {
            foreach (var p in PlayerColonies()) if (p.Stability < 40f) return true;
            return false;
        }

        private static float AvgStability()
        {
            float sum = 0f; int n = 0;
            foreach (var p in PlayerColonies()) { sum += p.Stability; n++; }
            return n > 0 ? sum / n : 0f;
        }

        private static bool EnergyCrisis()
        {
            var eco = EconomyManager.Instance;
            return eco != null && (eco.IsBankrupt || eco.MonthlyEnergyIncome < 0f);
        }

        private static bool StorageFull()
        {
            var eco = EconomyManager.Instance;
            return eco != null && eco.Minerals >= eco.MineralStorage * 0.85f;
        }

        /// <summary>Жители на n колоний со свободным жильём.</summary>
        private static int Settle(int maxColonies)
        {
            int n = 0;
            foreach (var p in PlayerColonies())
            {
                if (n >= maxColonies) break;
                if (p.Population >= p.HousingCapacity) continue;
                p.AddPops(p.GrowthSpecies(), 1);
                n++;
            }
            return n;
        }

        private static void Gain(float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f)
        {
            var eco = EconomyManager.Instance;
            if (eco == null) return;
            eco.EnergyCredits += energy; eco.Minerals += minerals; eco.Alloys += alloys; eco.Influence += influence;
            eco.RaiseResourcesChanged();
        }

        // ================================================================ Решения

        private static DecisionDef D(string id, string faction, DecisionCategory c, LGIcon icon, string name, string desc)
        {
            var d = new DecisionDef { Id = id, Faction = faction, Category = c, Icon = icon, Name = name, Desc = desc };
            s_all.Add(d);
            return d;
        }

        private static void EnsureDefs()
        {
            if (s_all.Count > 0) return;
            const DecisionCategory F = DecisionCategory.Faction, Eco = DecisionCategory.Economy, Sci = DecisionCategory.Science,
                                   Mil = DecisionCategory.Military, Soc = DecisionCategory.Society;

            // ---------------------------------------------------------------- Экономика
            D("dec_smelt", null, Eco, LGIcon.Alloys, Loc.T("Переплавка запасов"),
                Loc.T("Плавильни работают в три смены: титан со складов уходит в печи и выходит сплавами для верфей."))
                .Cost(minerals: 600f).Time(30, 90)
                .When(() => YearsIn >= 3)
                .Line(() => Loc.F("Через 30 дн.: +{0:0} сплавов", 160f * Scale))
                .Done(s => { Gain(alloys: 160f * s); return Loc.F("Склады получили {0:0} сплавов.", 160f * s); });

            D("dec_exchange", null, Eco, LGIcon.Trade, Loc.T("Сырьевая биржа"),
                Loc.T("Титан продают перекупщикам из нейтральных систем. Платят гелием-3 — топливом, которого вечно не хватает флоту."))
                .Cost(minerals: 700f).Time(20, 120)
                .When(() => YearsIn >= 5)
                .Line(() => Loc.F("Через 20 дн.: +{0:0} гелия-3", 300f * Scale))
                .Done(s => { Gain(energy: 300f * s); return Loc.F("Выручка: {0:0} гелия-3.", 300f * s); });

            D("dec_bonds", null, Eco, LGIcon.Influence, Loc.T("Титановые облигации"),
                Loc.T("Казна выпускает облигации, обеспеченные титаном. Держатели — влиятельные кланы и корпорации — теперь на вашей стороне."))
                .Cost(minerals: 900f).Time(0, 360)
                .When(() => YearsIn >= 10 && Colonies >= 3)
                .Line(Loc.T("+2 влияния в месяц на 12 месяцев"))
                .Done(s =>
                {
                    EmpireEffects.Add("dec_bonds", Loc.T("Титановые облигации"), Loc.T("Держатели облигаций поддерживают правительство"), 12, influence: 2f);
                    return Loc.T("Влияние +2 в месяц на год.");
                });

            // ---------------------------------------------------------------- Наука
            D("dec_grant", null, Sci, LGIcon.Research, Loc.T("Научный грант"),
                Loc.T("Щедрый грант лучшим лабораториям: оборудование из титана, лучшие умы — на текущие исследования."))
                .Cost(minerals: 800f, influence: 15f).Time(0, 360)
                .When(() => YearsIn >= 5)
                .Line(() => Loc.F("Сразу +{0:0} науки (4 месяца исследований)", MonthlyScience * 4f))
                .Done(s => GiveScience(4f));

            D("dec_expedition", null, Sci, LGIcon.Sensors, Loc.T("Дальняя экспедиция"),
                Loc.T("Караван исследовательских зондов уходит за границу на два месяца и картографирует всё, до чего дотянется."))
                .Cost(minerals: 400f, energy: 150f).Time(60, 360)
                .Line(Loc.T("Через 60 дн.: изучены все системы в 4 прыжках от границ"))
                .When(() => YearsIn >= 2 && (EventDatabase.UnsurveyedNearPlayer(4).Count > 0))
                .Done(s => Survey(4));

            D("dec_labs", null, Sci, LGIcon.Sensors, Loc.T("Ночные смены в лабораториях"),
                Loc.T("Учёные получают новые корпуса и трёхсменный график. Науки больше, но заводы делятся мощностями."))
                .Cost(minerals: 1000f).Time(240, 480)
                .When(() => YearsIn >= 15 || Colonies >= 5)
                .Mod((b, k) => { b.ResearchMult += 0.15f * k; })
                .Line(Loc.T("+15% науки на 240 дн."));

            // ---------------------------------------------------------------- Армия
            D("dec_drydock", null, Mil, LGIcon.Wrench, Loc.T("Ремонтные доки"),
                Loc.T("Полевые доки из титановых ферм собирают прямо на орбитах: весь флот встаёт в строй за неделю."))
                .Cost(minerals: 500f, energy: 100f).Time(0, 180)
                .Line(Loc.T("Сразу: все корабли полностью отремонтированы"))
                .When(() => YearsIn >= 3 && (DamagedShips() > 0))
                .Done(s => RepairAll());

            D("dec_drills", null, Mil, LGIcon.Target, Loc.T("Военные учения"),
                Loc.T("Флот полгода отрабатывает манёвры на титановых мишенях. Экипажи стреляют быстрее и точнее."))
                .Cost(minerals: 600f, influence: 10f).Time(180, 360)
                .When(() => YearsIn >= 8)
                .Mod((b, k) => { b.AllDamage += 0.10f * k; b.Accuracy += 5f * k; })
                .Line(Loc.T("+10% урона и +5 точности на 180 дн."));

            D("dec_shipyards", null, Mil, LGIcon.Shipyard, Loc.T("Ускоренное судостроение"),
                Loc.T("Верфи получают запас титана на годы вперёд: корпуса штампуют потоком, и каждый корабль обходится дешевле."))
                .Cost(minerals: 1000f).Time(180, 540)
                .When(() => YearsIn >= 12)
                .Mod((b, k) => { b.ShipCostMult -= 0.15f * k; })
                .Line(Loc.T("−15% цены кораблей на 180 дн."));

            // ---------------------------------------------------------------- Общество
            D("dec_monument", null, Soc, LGIcon.Construction, Loc.T("Монументальное строительство"),
                Loc.T("Титановые монументы героям и основателям встают в каждой столице. Граждане гордятся империей."))
                .Cost(minerals: 1500f).Time(360, 720)
                .When(() => Colonies >= 5)
                .Stab(8f)
                .Line(Loc.T("Действует 360 дн."));

            D("dec_colonial", null, Soc, LGIcon.Population, Loc.T("Колониальные субсидии"),
                Loc.T("Переселенцам — жильё, кредиты и титановые купола. Колонии растут быстрее."))
                .Cost(minerals: 700f, energy: 200f).Time(360, 540)
                .When(() => Colonies >= 3)
                .Mod((b, k) => { b.PopGrowth += 0.35f * k; })
                .Line(Loc.T("+35% роста населения на 360 дн."));

            // ================================================================ По обстоятельствам
            // Появляются, когда сложились условия, и исчезают вместе с ними; возможности ждут считанные дни.

            D("eco_crisis", null, Eco, LGIcon.Warning, Loc.T("Чрезвычайный бюджет"),
                Loc.T("Казна в минусе. Титан срочно продают за гелий-3, лишь бы флот не встал на прикол."))
                .Cost(minerals: 600f).Time(0, 360)
                .When(EnergyCrisis)
                .Cond(Loc.T("Появляется, когда гелий-3 уходит в минус"))
                .Line(() => Loc.F("Сразу +{0:0} гелия-3", 350f * Scale))
                .Done(s => { Gain(energy: 350f * s); return Loc.F("Казна получила {0:0} гелия-3.", 350f * s); });

            D("eco_selloff", null, Eco, LGIcon.Trade, Loc.T("Распродажа излишков"),
                Loc.T("Склады ломятся от титана. Половину — на внешний рынок: за гелий-3 и за благодарность торговых домов."))
                .Cost(minerals: 1500f).Time(0, 180)
                .When(StorageFull)
                .Cond(Loc.T("Появляется, когда склады титана почти полны"))
                .Line(() => Loc.F("Сразу +{0:0} гелия-3 и +30 влияния", 500f * Scale))
                .Done(s => { Gain(energy: 500f * s, influence: 30f); return Loc.F("Выручка: {0:0} гелия-3 и 30 влияния.", 500f * s); });

            D("eco_warbonds", null, Eco, LGIcon.Alloys, Loc.T("Военные облигации"),
                Loc.T("Граждане покупают облигации победы. Деньги идут прямо на заводы, броня — прямо на фронт."))
                .Cost(minerals: 800f, influence: 10f).Time(0, 360)
                .When(AtWar)
                .Cond(Loc.T("Только во время войны"))
                .Line(() => Loc.F("+{0:0} сплавов в месяц на 6 месяцев", 12f * Scale))
                .Done(s =>
                {
                    EmpireEffects.Add("eco_warbonds", Loc.T("Военные облигации"), Loc.T("Сборы на нужды фронта"), 6, alloys: 12f * s);
                    return Loc.F("+{0:0} сплавов в месяц на полгода.", 12f * s);
                });

            D("eco_caravan", null, Eco, LGIcon.Trade, Loc.T("Торговый караван"),
                Loc.T("У границы встал караван вольных торговцев. Сплавы у них дешёвые — но ждать они не станут."))
                .Cost(minerals: 500f).Time(0, 240).Offer(0.06f, 45)
                .Line(() => Loc.F("Сразу +{0:0} сплавов", 220f * Scale))
                .Done(s => { Gain(alloys: 220f * s); return Loc.F("Куплено {0:0} сплавов.", 220f * s); });

            D("sci_pioneers", null, Sci, LGIcon.Propulsion, Loc.T("Программа первопроходцев"),
                Loc.T("Молодая империя ставит на разведку: больше зондов, быстрее корабли. Через 15 лет программа устареет."))
                .Cost(minerals: 300f).Time(360, 720)
                .When(() => YearsIn < 15)
                .Mod((b, k) => { b.SurveySpeed += 0.25f * k; b.HyperlaneSpeed += 0.10f * k; })
                .Line(Loc.T("+25% скорости исследования систем и +10% скорости в коридорах на 360 дн."))
                .Cond(Loc.T("Доступно только первые 15 лет"));

            D("sci_exchange", null, Sci, LGIcon.Handshake, Loc.T("Обмен учёными"),
                Loc.T("Партнёры по договору открывают свои лаборатории. Свежие идеи ускоряют исследования."))
                .Cost(minerals: 600f, influence: 20f).Time(360, 540)
                .When(HasPartner)
                .Mod((b, k) => { b.ResearchMult += 0.15f * k; })
                .Cond(Loc.T("Нужен пакт или союз с другой империей"))
                .Line(Loc.T("+15% науки на 360 дн."));

            D("sci_defector", null, Sci, LGIcon.Research, Loc.T("Учёный-перебежчик"),
                Loc.T("Светило чужой науки просит убежища — с чертежами и тетрадями. Решать надо быстро, пока его не хватились."))
                .Cost(influence: 25f).Time(0, 360).Offer(0.05f, 60)
                .Line(() => Loc.F("Сразу +{0:0} науки (3 месяца исследований)", MonthlyScience * 3f))
                .Done(s => GiveScience(3f));

            D("sci_horizon", null, Sci, LGIcon.Star, Loc.T("Проект «Горизонт»"),
                Loc.T("Великий проект зрелой империи: научный город на орбите звезды, где работают лучшие умы целых поколений."))
                .Cost(minerals: 2500f).Time(360, 0).Great()
                .When(() => YearsIn >= 50)
                .Mod((b, k) => { b.ResearchMult += 0.15f * k; })
                .Line(Loc.T("Навсегда: +15% науки"))
                .Cond(Loc.T("Открывается на 50-й год"));

            D("mil_totaldef", null, Mil, LGIcon.Defense, Loc.T("Тотальная оборона"),
                Loc.T("Враг у ворот. Каждая база получает щиты и пушки сверх штата, граждане сплачиваются вокруг флага."))
                .Cost(minerals: 700f).Time(120, 360)
                .When(AtWar)
                .Mod((b, k) => { b.StarbaseMult += 0.40f * k; })
                .Cond(Loc.T("Только во время войны"))
                .Line(Loc.T("+40% прочности звёздных баз на 120 дн."))
                .Stab(6f);

            D("mil_armsrace", null, Mil, LGIcon.Shipyard, Loc.T("Гонка вооружений"),
                Loc.T("Сосед строит флот быстрее нас. Верфи получают приоритет — пока не поздно."))
                .Cost(minerals: 900f).Time(240, 540)
                .When(() => !AtWar() && Outgunned())
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
                .Cost(minerals: 300f, energy: 300f).Time(180, 360)
                .When(Unrest)
                .Cond(Loc.T("Появляется, когда стабильность колонии ниже 40"))
                .Stab(10f)
                .Line(Loc.T("Действует 180 дн."));

            D("soc_landrush", null, Soc, LGIcon.Starbase, Loc.T("Земельная лихорадка"),
                Loc.T("Каждому, кто застолбит новую систему, — налоговые каникулы. Галактика ещё не поделена, но это ненадолго."))
                .Cost(minerals: 500f).Time(240, 540)
                .When(() => YearsIn < 25)
                .Mod((b, k) => { b.OutpostInfluenceDiscount += 10f * k; })
                .Line(Loc.T("Форпосты на 10 влияния дешевле на 240 дн."))
                .Cond(Loc.T("Доступно только первые 25 лет"));

            D("soc_refugees", null, Soc, LGIcon.Population, Loc.T("Беженцы с окраин"),
                Loc.T("Корабли беженцев просят разрешения сесть. Новые руки нужны колониям — но решать надо сейчас."))
                .Cost(influence: 15f).Time(0, 360).Offer(0.05f, 60)
                .When(() => ColoniesWithRoom() > 0)
                .Line(Loc.T("Сразу: +1 житель на 3 колониях со свободным жильём"))
                .Done(s => Loc.F("Беженцы поселились на {0} колониях.", Settle(3)));

            D("soc_golden", null, Soc, LGIcon.Trophy, Loc.T("Золотой век"),
                Loc.T("Великий проект процветающей империи: искусство, наука и промышленность на пике. Об этой эпохе будут слагать легенды."))
                .Cost(minerals: 3000f).Time(360, 0).Great()
                .When(() => YearsIn >= 40 && AvgStability() >= 60f)
                .Mod((b, k) => { b.EnergyMult += 0.05f * k; b.AlloysMult += 0.05f * k; b.ResearchMult += 0.05f * k; })
                .Line(Loc.T("Навсегда: +5% гелия-3, сплавов и науки"))
                .Stab(3f)
                .Cond(Loc.T("С 40-го года, при средней стабильности от 60"));

            // ================================================================ Фракции

            // ---- Республика Астрея ----
            D("ast_forum", "astrea", F, LGIcon.Diplomacy, Loc.T("Галактический форум"),
                Loc.T("Республика созывает послов всех известных империй на Астрею. Три месяца речей, банкетов и тихих договорённостей."))
                .Cost(minerals: 600f, influence: 40f).Time(90, 720)
                .Line(Loc.T("Через 90 дн.: +12 к отношениям со всеми известными империями"))
                .When(AnyContact)
                .Done(s => Loc.F("Послы {0} империй уехали довольными.", Opinion("incident", 12f)));
            D("ast_academy", "astrea", F, LGIcon.Research, Loc.T("Академия Республики"),
                Loc.T("Свободный университет для учёных всей галактики. Беженцы-гении едут к нам, а не к соседям."))
                .Cost(minerals: 1500f).Time(360, 900)
                .When(() => YearsIn >= 10)
                .Mod((b, k) => { b.ResearchMult += 0.20f * k; })
                .Line(Loc.T("+20% науки на 360 дн."));
            D("ast_charter", "astrea", F, LGIcon.Globe, Loc.T("Хартия колоний"),
                Loc.T("Великий проект: колонии получают самоуправление и голос в Сенате. Республика становится союзом равных."))
                .Cost(minerals: 2500f, influence: 50f).Time(360, 0).Great()
                .When(() => YearsIn >= 20 && Colonies >= 5)
                .Mod((b, k) => { b.PopGrowth += 0.10f * k; })
                .Line(Loc.T("Навсегда: +10% роста населения"))
                .Stab(4f);

            D("ast_peace", "astrea", F, LGIcon.Peace, Loc.T("Миротворческая миссия"),
                Loc.T("Соседи воюют. Республика предлагает переговоры на нейтральной станции — и получает славу миротворца."))
                .Cost(minerals: 500f, influence: 30f).Time(120, 540)
                .When(() => !AtWar() && OthersAtWar())
                .Cond(Loc.T("Появляется, когда известные империи воюют между собой"))
                .Stab(5f)
                .Line(Loc.T("По окончании: +8 к отношениям со всеми известными империями"))
                .Done(s => Loc.F("Миротворцев благодарят {0} империй.", Opinion("incident", 8f)));

            // ---- Доминион Ксарн ----
            D("xarn_parade", "xarn", F, LGIcon.Swords, Loc.T("Военный парад"),
                Loc.T("Над столицей проходит весь флот Доминиона. Народ ликует, враги считают стволы."))
                .Cost(minerals: 800f, alloys: 100f).Time(180, 540)
                .When(() => YearsIn >= 5)
                .Mod((b, k) => { b.AllDamage += 0.10f * k; })
                .Stab(6f)
                .Line(Loc.T("+10% урона на 180 дн."));
            D("xarn_mobilize", "xarn", F, LGIcon.Shipyard, Loc.T("Мобилизация верфей"),
                Loc.T("Каждая верфь Доминиона переходит на военный график. Корпуса сходят со стапелей каждый день."))
                .Cost(minerals: 1200f, influence: 20f).Time(240, 720)
                .When(() => YearsIn >= 10 || AtWar())
                .Mod((b, k) => { b.ShipCostMult -= 0.25f * k; b.NavalCapacity += 8f * k; })
                .Line(Loc.T("−25% цены кораблей и +8 к флотскому лимиту на 240 дн."));
            D("xarn_forge", "xarn", F, LGIcon.Industry, Loc.T("Кузницы Ксарна"),
                Loc.T("Великий проект: в недрах столицы зажигают горны, способные плавить броню целых флотов."))
                .Cost(minerals: 2500f).Time(360, 0).Great()
                .When(() => YearsIn >= 20)
                .Mod((b, k) => { b.AlloysMult += 0.10f * k; b.StarbaseMult += 0.15f * k; })
                .Line(Loc.T("Навсегда: +10% сплавов и +15% прочности звёздных баз"));

            D("xarn_vendetta", "xarn", F, LGIcon.Target, Loc.T("Кровная месть"),
                Loc.T("Клан объявляет врагу кровную месть. Пока она не исполнена, ни один воин Ксарна не отступит."))
                .Cost(minerals: 500f, influence: 20f).Time(120, 360)
                .When(AtWar)
                .Mod((b, k) => { b.AllDamage += 0.15f * k; })
                .Cond(Loc.T("Только во время войны"))
                .Line(Loc.T("+15% урона на 120 дн."));

            // ---- Синдикат Аквила ----
            D("aq_boom", "aquila", F, LGIcon.Energy, Loc.T("Биржевой бум"),
                Loc.T("Синдикат вкладывает титан в гелиевые фьючерсы. Пока пузырь растёт — растёт и казна."))
                .Cost(minerals: 1000f).Time(240, 720)
                .When(() => YearsIn >= 8)
                .Mod((b, k) => { b.EnergyMult += 0.25f * k; })
                .Line(Loc.T("+25% гелия-3 на 240 дн."));
            D("aq_lobby", "aquila", F, LGIcon.Influence, Loc.T("Лоббирование"),
                Loc.T("Гелий-3 уходит нужным людям в нужных кабинетах. Через месяц нужные решения принимаются сами."))
                .Cost(energy: 400f).Time(30, 360)
                .When(() => YearsIn >= 3)
                .Line(Loc.T("Через 30 дн.: +50 влияния"))
                .Done(s => { Gain(influence: 50f); return Loc.T("Получено 50 влияния."); });
            D("aq_trade", "aquila", F, LGIcon.Trade, Loc.T("Торговая экспансия"),
                Loc.T("Конторы Синдиката открываются во всех известных столицах. Торговля сближает — и приносит гелий."))
                .Cost(minerals: 800f, influence: 20f).Time(120, 720)
                .Line(Loc.T("Через 120 дн.: +12 к отношениям с известными империями"))
                .Line(() => Loc.F("и +{0:0} гелия-3 в месяц на 12 месяцев", 8f * Scale))
                .When(AnyContact)
                .Done(s =>
                {
                    int n = Opinion("trade", 12f);
                    EmpireEffects.Add("aq_trade", Loc.T("Торговая экспансия"), Loc.T("Конторы Синдиката в чужих столицах"), 12, energy: 8f * s);
                    return Loc.F("Конторы открыты в {0} столицах.", n);
                });
            D("aq_exchange", "aquila", F, LGIcon.Trade, Loc.T("Галактическая биржа"),
                Loc.T("Великий проект: главная биржа галактики. Через неё проходит каждый контракт — и с каждого Синдикат берёт свою долю."))
                .Cost(minerals: 2500f).Time(360, 0).Great()
                .When(() => YearsIn >= 20)
                .Mod((b, k) => { b.EnergyMult += 0.10f * k; b.InfluenceFlat += 1f * k; })
                .Line(Loc.T("Навсегда: +10% гелия-3 и +1 влияния в месяц"));

            D("aq_buyout", "aquila", F, LGIcon.Alloys, Loc.T("Скупка активов"),
                Loc.T("Чужой концерн разорился. Синдикат забирает его склады сплавов за бесценок — если успеет первым."))
                .Cost(energy: 400f).Time(0, 360).Offer(0.06f, 30)
                .Line(() => Loc.F("Сразу +{0:0} сплавов и +15 влияния", 250f * Scale))
                .Done(s => { Gain(alloys: 250f * s, influence: 15f); return Loc.F("Получено {0:0} сплавов и 15 влияния.", 250f * s); });

            // ---- Иридийский Оракул ----
            D("ir_vision", "iridia", F, LGIcon.Star, Loc.T("Видение будущего"),
                Loc.T("Верховная Оракул уходит в транс на месяц. Вернувшись, она диктует учёным то, до чего они дошли бы лишь через полгода."))
                .Cost(influence: 30f).Time(30, 540)
                .When(() => YearsIn >= 3)
                .Line(() => Loc.F("Через 30 дн.: +{0:0} науки (6 месяцев исследований)", MonthlyScience * 6f))
                .Done(s => GiveScience(6f));
            D("ir_pilgrimage", "iridia", F, LGIcon.Society, Loc.T("Паломничество к Саду"),
                Loc.T("Миллионы верующих идут к миру-саду Иридии. Колонии светятся единой мыслью, рождается больше детей."))
                .Cost(minerals: 900f).Time(180, 540)
                .When(() => Colonies >= 3)
                .Mod((b, k) => { b.PopGrowth += 0.10f * k; })
                .Stab(10f)
                .Line(Loc.T("+10% роста населения на 180 дн."));
            D("ir_temple", "iridia", F, LGIcon.Globe, Loc.T("Храм Предвидения"),
                Loc.T("Великий проект: хрустальный храм, где сотни провидцев читают нити будущего день и ночь."))
                .Cost(minerals: 2500f).Time(360, 0).Great()
                .When(() => YearsIn >= 20)
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
                .Cost(minerals: 1000f, energy: 200f).Time(60, 720)
                .When(() => Colonies >= 3)
                .Line(Loc.T("Через 60 дн.: +1 житель на каждой колонии со свободным жильём"))
                .Need(() => ColoniesWithRoom() == 0 ? Loc.T("Нет свободного жилья") : null)
                .Done(s => Loc.F("Новые жители на {0} колониях.", GrowColonies(false)));
            D("ter_spores", "terraan", F, LGIcon.Sensors, Loc.T("Семена спор"),
                Loc.T("Споры мицелия разлетаются по коридорам и прорастают сенсорной сетью в чужих системах."))
                .Cost(energy: 250f).Time(45, 540)
                .Line(Loc.T("Через 45 дн.: изучены все системы в 5 прыжках от границ"))
                .When(() => EventDatabase.UnsurveyedNearPlayer(5).Count > 0)
                .Done(s => Survey(5));
            D("ter_roots", "terraan", F, LGIcon.Defense, Loc.T("Корневая оборона"),
                Loc.T("Корни оплетают звёздные базы, а сок сада питает экипажи. Защищаться дешевле и легче."))
                .Cost(minerals: 1200f).Time(360, 900)
                .When(() => YearsIn >= 10)
                .Mod((b, k) => { b.StarbaseMult += 0.30f * k; b.UpkeepMult -= 0.15f * k; })
                .Line(Loc.T("+30% прочности звёздных баз и −15% содержания флота на 360 дн."));
            D("ter_worldtree", "terraan", F, LGIcon.Planet, Loc.T("Древо Миров"),
                Loc.T("Великий проект: из семени Первого Сада вырастает древо, чьи корни соединяют все миры Конклава."))
                .Cost(minerals: 2500f).Time(360, 0).Great()
                .When(() => YearsIn >= 20)
                .Mod((b, k) => { b.PopGrowth += 0.15f * k; })
                .Line(Loc.T("Навсегда: +15% роста населения"))
                .Stab(3f);

            D("ter_spring", "terraan", F, LGIcon.Population, Loc.T("Сезон цветения"),
                Loc.T("Весной соки сада текут быстрее. Конклав может направить их в рост новых жителей — но только пока весна."))
                .Cost(minerals: 400f).Time(90, 270)
                .When(() => MonthNow >= 3 && MonthNow <= 5)
                .Mod((b, k) => { b.PopGrowth += 0.40f * k; })
                .Cond(Loc.T("Только весной (март–май)"))
                .Line(Loc.T("+40% роста населения на 90 дн."));

            // ---- Машинный Консенсус «Ноль» ----
            D("nol_reflash", "nol", F, LGIcon.Gear, Loc.T("Перепрошивка"),
                Loc.T("Органики на мирах Консенсуса проходят «добровольную» загрузку в синтетические тела."))
                .Cost(minerals: 800f, influence: 30f).Time(60, 540)
                .Line(Loc.T("Через 60 дн.: до 2 органиков на каждой колонии станут синтетами"))
                .When(() => OrganicPops() > 0)
                .Done(s => Reflash());
            D("nol_hive", "nol", F, LGIcon.Population, Loc.T("Фабрики-ульи"),
                Loc.T("Сборочные линии работают без остановки: каждый мир Консенсуса получает новый синтет."))
                .Cost(minerals: 1500f, alloys: 150f).Time(90, 720)
                .When(() => Colonies >= 3)
                .Line(Loc.T("Через 90 дн.: +1 синтет на каждой колонии со свободным жильём"))
                .Need(() => ColoniesWithRoom() == 0 ? Loc.T("Нет свободного жилья") : null)
                .Done(s => Loc.F("Новые синтеты на {0} колониях.", GrowColonies(true)));
            D("nol_optimize", "nol", F, LGIcon.Reactor, Loc.T("Протокол оптимизации"),
                Loc.T("Консенсус пересчитывает каждую цепочку производства. Лишних операций больше нет."))
                .Cost(minerals: 1000f).Time(360, 900)
                .When(() => YearsIn >= 10)
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
                .Cost(minerals: 2500f).Time(360, 0).Great()
                .When(() => YearsIn >= 20)
                .Mod((b, k) => { b.ResearchMult += 0.10f * k; b.InfluenceFlat += 1.5f * k; })
                .Line(Loc.T("Навсегда: +10% науки и +1.5 влияния в месяц"));
        }
    }
}
