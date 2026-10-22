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
        public readonly List<(Func<string> Text, bool Good)> Effects = new List<(Func<string>, bool)>();

        public DecisionDef Cost(float minerals = 0f, float energy = 0f, float alloys = 0f, float influence = 0f)
        { Minerals = minerals; Energy = energy; Alloys = alloys; Influence = influence; return this; }
        public DecisionDef Time(int days, int cooldown) { Days = days; Cooldown = cooldown; return this; }
        public DecisionDef Great() { Once = true; return this; }
        public DecisionDef Mod(Action<EmpireBonuses, float> apply) { Apply = apply; return this; }
        public DecisionDef Stab(float s) { Stability = s; return Line(Loc.F("+{0:0} стабильности колоний", s), true); }
        public DecisionDef Done(Func<float, string> f) { OnComplete = f; return this; }
        public DecisionDef Need(Func<string> f) { Requirement = f; return this; }
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
    }

    /// <summary>
    /// Решения империи — как в Hearts of Iron: у каждой фракции свои особые решения и великий проект,
    /// плюс общие решения для всех. Решение стоит ресурсов (в первую очередь титана — так излишки сырья
    /// превращаются в сплавы, гелий-3, науку и влияние), идёт несколько дней и потом закрывается на перезарядку.
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

        /// <summary>Решения, которые видит игрок: общие и особые его фракции.</summary>
        public static IEnumerable<DecisionDef> Visible(DecisionCategory c)
        {
            EnsureDefs();
            string key = FactionTraits.Key(0);
            foreach (var d in s_all)
                if (d.Category == c && (d.Faction == null || d.Faction == key)) yield return d;
        }

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

        /// <summary>Каждый день: завершить решения, у которых вышел срок.</summary>
        public static void DailyTick()
        {
            if (s_active.Count == 0) return;
            int today = EmpireLaws.Today();
            bool changed = false;
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
            return s;
        }

        public static void Restore(DecisionsSave s)
        {
            s_active.Clear();
            s_cooldown.Clear();
            s_completed.Clear();
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
                .Line(() => Loc.F("Через 30 дн.: +{0:0} сплавов", 160f * Scale))
                .Done(s => { Gain(alloys: 160f * s); return Loc.F("Склады получили {0:0} сплавов.", 160f * s); });

            D("dec_exchange", null, Eco, LGIcon.Trade, Loc.T("Сырьевая биржа"),
                Loc.T("Титан продают перекупщикам из нейтральных систем. Платят гелием-3 — топливом, которого вечно не хватает флоту."))
                .Cost(minerals: 700f).Time(20, 120)
                .Line(() => Loc.F("Через 20 дн.: +{0:0} гелия-3", 300f * Scale))
                .Done(s => { Gain(energy: 300f * s); return Loc.F("Выручка: {0:0} гелия-3.", 300f * s); });

            D("dec_bonds", null, Eco, LGIcon.Influence, Loc.T("Титановые облигации"),
                Loc.T("Казна выпускает облигации, обеспеченные титаном. Держатели — влиятельные кланы и корпорации — теперь на вашей стороне."))
                .Cost(minerals: 900f).Time(0, 360)
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
                .Line(() => Loc.F("Сразу +{0:0} науки (4 месяца исследований)", MonthlyScience * 4f))
                .Done(s => GiveScience(4f));

            D("dec_expedition", null, Sci, LGIcon.Sensors, Loc.T("Дальняя экспедиция"),
                Loc.T("Караван исследовательских зондов уходит за границу на два месяца и картографирует всё, до чего дотянется."))
                .Cost(minerals: 400f, energy: 150f).Time(60, 360)
                .Line(Loc.T("Через 60 дн.: изучены все системы в 4 прыжках от границ"))
                .Need(() => EventDatabase.UnsurveyedNearPlayer(4).Count == 0 ? Loc.T("Вокруг всё изучено") : null)
                .Done(s => Survey(4));

            D("dec_labs", null, Sci, LGIcon.Sensors, Loc.T("Ночные смены в лабораториях"),
                Loc.T("Учёные получают новые корпуса и трёхсменный график. Науки больше, но заводы делятся мощностями."))
                .Cost(minerals: 1000f).Time(240, 480)
                .Mod((b, k) => { b.ResearchMult += 0.15f * k; })
                .Line(Loc.T("+15% науки на 240 дн."));

            // ---------------------------------------------------------------- Армия
            D("dec_drydock", null, Mil, LGIcon.Wrench, Loc.T("Ремонтные доки"),
                Loc.T("Полевые доки из титановых ферм собирают прямо на орбитах: весь флот встаёт в строй за неделю."))
                .Cost(minerals: 500f, energy: 100f).Time(0, 180)
                .Line(Loc.T("Сразу: все корабли полностью отремонтированы"))
                .Need(() => DamagedShips() == 0 ? Loc.T("Все корабли целы") : null)
                .Done(s => RepairAll());

            D("dec_drills", null, Mil, LGIcon.Target, Loc.T("Военные учения"),
                Loc.T("Флот полгода отрабатывает манёвры на титановых мишенях. Экипажи стреляют быстрее и точнее."))
                .Cost(minerals: 600f, influence: 10f).Time(180, 360)
                .Mod((b, k) => { b.AllDamage += 0.10f * k; b.Accuracy += 5f * k; })
                .Line(Loc.T("+10% урона и +5 точности на 180 дн."));

            D("dec_shipyards", null, Mil, LGIcon.Shipyard, Loc.T("Ускоренное судостроение"),
                Loc.T("Верфи получают запас титана на годы вперёд: корпуса штампуют потоком, и каждый корабль обходится дешевле."))
                .Cost(minerals: 1000f).Time(180, 540)
                .Mod((b, k) => { b.ShipCostMult -= 0.15f * k; })
                .Line(Loc.T("−15% цены кораблей на 180 дн."));

            // ---------------------------------------------------------------- Общество
            D("dec_monument", null, Soc, LGIcon.Construction, Loc.T("Монументальное строительство"),
                Loc.T("Титановые монументы героям и основателям встают в каждой столице. Граждане гордятся империей."))
                .Cost(minerals: 1500f).Time(360, 720)
                .Stab(8f)
                .Line(Loc.T("Действует 360 дн."));

            D("dec_colonial", null, Soc, LGIcon.Population, Loc.T("Колониальные субсидии"),
                Loc.T("Переселенцам — жильё, кредиты и титановые купола. Колонии растут быстрее."))
                .Cost(minerals: 700f, energy: 200f).Time(360, 540)
                .Mod((b, k) => { b.PopGrowth += 0.35f * k; })
                .Line(Loc.T("+35% роста населения на 360 дн."));

            // ================================================================ Фракции

            // ---- Республика Астрея ----
            D("ast_forum", "astrea", F, LGIcon.Diplomacy, Loc.T("Галактический форум"),
                Loc.T("Республика созывает послов всех известных империй на Астрею. Три месяца речей, банкетов и тихих договорённостей."))
                .Cost(minerals: 600f, influence: 40f).Time(90, 720)
                .Line(Loc.T("Через 90 дн.: +12 к отношениям со всеми известными империями"))
                .Need(() => AnyContact() ? null : Loc.T("Нет известных империй"))
                .Done(s => Loc.F("Послы {0} империй уехали довольными.", Opinion("incident", 12f)));
            D("ast_academy", "astrea", F, LGIcon.Research, Loc.T("Академия Республики"),
                Loc.T("Свободный университет для учёных всей галактики. Беженцы-гении едут к нам, а не к соседям."))
                .Cost(minerals: 1500f).Time(360, 900)
                .Mod((b, k) => { b.ResearchMult += 0.20f * k; })
                .Line(Loc.T("+20% науки на 360 дн."));
            D("ast_charter", "astrea", F, LGIcon.Globe, Loc.T("Хартия колоний"),
                Loc.T("Великий проект: колонии получают самоуправление и голос в Сенате. Республика становится союзом равных."))
                .Cost(minerals: 2500f, influence: 50f).Time(360, 0).Great()
                .Mod((b, k) => { b.PopGrowth += 0.10f * k; })
                .Line(Loc.T("Навсегда: +10% роста населения"))
                .Stab(4f);

            // ---- Доминион Ксарн ----
            D("xarn_parade", "xarn", F, LGIcon.Swords, Loc.T("Военный парад"),
                Loc.T("Над столицей проходит весь флот Доминиона. Народ ликует, враги считают стволы."))
                .Cost(minerals: 800f, alloys: 100f).Time(180, 540)
                .Mod((b, k) => { b.AllDamage += 0.10f * k; })
                .Stab(6f)
                .Line(Loc.T("+10% урона на 180 дн."));
            D("xarn_mobilize", "xarn", F, LGIcon.Shipyard, Loc.T("Мобилизация верфей"),
                Loc.T("Каждая верфь Доминиона переходит на военный график. Корпуса сходят со стапелей каждый день."))
                .Cost(minerals: 1200f, influence: 20f).Time(240, 720)
                .Mod((b, k) => { b.ShipCostMult -= 0.25f * k; b.NavalCapacity += 8f * k; })
                .Line(Loc.T("−25% цены кораблей и +8 к флотскому лимиту на 240 дн."));
            D("xarn_forge", "xarn", F, LGIcon.Industry, Loc.T("Кузницы Ксарна"),
                Loc.T("Великий проект: в недрах столицы зажигают горны, способные плавить броню целых флотов."))
                .Cost(minerals: 2500f).Time(360, 0).Great()
                .Mod((b, k) => { b.AlloysMult += 0.10f * k; b.StarbaseMult += 0.15f * k; })
                .Line(Loc.T("Навсегда: +10% сплавов и +15% прочности звёздных баз"));

            // ---- Синдикат Аквила ----
            D("aq_boom", "aquila", F, LGIcon.Energy, Loc.T("Биржевой бум"),
                Loc.T("Синдикат вкладывает титан в гелиевые фьючерсы. Пока пузырь растёт — растёт и казна."))
                .Cost(minerals: 1000f).Time(240, 720)
                .Mod((b, k) => { b.EnergyMult += 0.25f * k; })
                .Line(Loc.T("+25% гелия-3 на 240 дн."));
            D("aq_lobby", "aquila", F, LGIcon.Influence, Loc.T("Лоббирование"),
                Loc.T("Гелий-3 уходит нужным людям в нужных кабинетах. Через месяц нужные решения принимаются сами."))
                .Cost(energy: 400f).Time(30, 360)
                .Line(Loc.T("Через 30 дн.: +50 влияния"))
                .Done(s => { Gain(influence: 50f); return Loc.T("Получено 50 влияния."); });
            D("aq_trade", "aquila", F, LGIcon.Trade, Loc.T("Торговая экспансия"),
                Loc.T("Конторы Синдиката открываются во всех известных столицах. Торговля сближает — и приносит гелий."))
                .Cost(minerals: 800f, influence: 20f).Time(120, 720)
                .Line(Loc.T("Через 120 дн.: +12 к отношениям с известными империями"))
                .Line(() => Loc.F("и +{0:0} гелия-3 в месяц на 12 месяцев", 8f * Scale))
                .Need(() => AnyContact() ? null : Loc.T("Нет известных империй"))
                .Done(s =>
                {
                    int n = Opinion("trade", 12f);
                    EmpireEffects.Add("aq_trade", Loc.T("Торговая экспансия"), Loc.T("Конторы Синдиката в чужих столицах"), 12, energy: 8f * s);
                    return Loc.F("Конторы открыты в {0} столицах.", n);
                });
            D("aq_exchange", "aquila", F, LGIcon.Trade, Loc.T("Галактическая биржа"),
                Loc.T("Великий проект: главная биржа галактики. Через неё проходит каждый контракт — и с каждого Синдикат берёт свою долю."))
                .Cost(minerals: 2500f).Time(360, 0).Great()
                .Mod((b, k) => { b.EnergyMult += 0.10f * k; b.InfluenceFlat += 1f * k; })
                .Line(Loc.T("Навсегда: +10% гелия-3 и +1 влияния в месяц"));

            // ---- Иридийский Оракул ----
            D("ir_vision", "iridia", F, LGIcon.Star, Loc.T("Видение будущего"),
                Loc.T("Верховная Оракул уходит в транс на месяц. Вернувшись, она диктует учёным то, до чего они дошли бы лишь через полгода."))
                .Cost(influence: 30f).Time(30, 540)
                .Line(() => Loc.F("Через 30 дн.: +{0:0} науки (6 месяцев исследований)", MonthlyScience * 6f))
                .Done(s => GiveScience(6f));
            D("ir_pilgrimage", "iridia", F, LGIcon.Society, Loc.T("Паломничество к Саду"),
                Loc.T("Миллионы верующих идут к миру-саду Иридии. Колонии светятся единой мыслью, рождается больше детей."))
                .Cost(minerals: 900f).Time(180, 540)
                .Mod((b, k) => { b.PopGrowth += 0.10f * k; })
                .Stab(10f)
                .Line(Loc.T("+10% роста населения на 180 дн."));
            D("ir_temple", "iridia", F, LGIcon.Globe, Loc.T("Храм Предвидения"),
                Loc.T("Великий проект: хрустальный храм, где сотни провидцев читают нити будущего день и ночь."))
                .Cost(minerals: 2500f).Time(360, 0).Great()
                .Mod((b, k) => { b.InfluenceFlat += 1.5f * k; b.ResearchMult += 0.05f * k; })
                .Line(Loc.T("Навсегда: +1.5 влияния в месяц и +5% науки"));

            // ---- Хлорофиловый Конклав Тэрра'ан ----
            D("ter_bloom", "terraan", F, LGIcon.Population, Loc.T("Великое цветение"),
                Loc.T("Конклав пробуждает спящие семена на всех мирах сразу. Через два месяца сад приносит новых жителей."))
                .Cost(minerals: 1000f, energy: 200f).Time(60, 720)
                .Line(Loc.T("Через 60 дн.: +1 житель на каждой колонии со свободным жильём"))
                .Need(() => ColoniesWithRoom() == 0 ? Loc.T("Нет свободного жилья") : null)
                .Done(s => Loc.F("Новые жители на {0} колониях.", GrowColonies(false)));
            D("ter_spores", "terraan", F, LGIcon.Sensors, Loc.T("Семена спор"),
                Loc.T("Споры мицелия разлетаются по коридорам и прорастают сенсорной сетью в чужих системах."))
                .Cost(energy: 250f).Time(45, 540)
                .Line(Loc.T("Через 45 дн.: изучены все системы в 5 прыжках от границ"))
                .Need(() => EventDatabase.UnsurveyedNearPlayer(5).Count == 0 ? Loc.T("Вокруг всё изучено") : null)
                .Done(s => Survey(5));
            D("ter_roots", "terraan", F, LGIcon.Defense, Loc.T("Корневая оборона"),
                Loc.T("Корни оплетают звёздные базы, а сок сада питает экипажи. Защищаться дешевле и легче."))
                .Cost(minerals: 1200f).Time(360, 900)
                .Mod((b, k) => { b.StarbaseMult += 0.30f * k; b.UpkeepMult -= 0.15f * k; })
                .Line(Loc.T("+30% прочности звёздных баз и −15% содержания флота на 360 дн."));
            D("ter_worldtree", "terraan", F, LGIcon.Planet, Loc.T("Древо Миров"),
                Loc.T("Великий проект: из семени Первого Сада вырастает древо, чьи корни соединяют все миры Конклава."))
                .Cost(minerals: 2500f).Time(360, 0).Great()
                .Mod((b, k) => { b.PopGrowth += 0.15f * k; })
                .Line(Loc.T("Навсегда: +15% роста населения"))
                .Stab(3f);

            // ---- Машинный Консенсус «Ноль» ----
            D("nol_reflash", "nol", F, LGIcon.Gear, Loc.T("Перепрошивка"),
                Loc.T("Органики на мирах Консенсуса проходят «добровольную» загрузку в синтетические тела."))
                .Cost(minerals: 800f, influence: 30f).Time(60, 540)
                .Line(Loc.T("Через 60 дн.: до 2 органиков на каждой колонии станут синтетами"))
                .Need(() => OrganicPops() == 0 ? Loc.T("На колониях нет органиков") : null)
                .Done(s => Reflash());
            D("nol_hive", "nol", F, LGIcon.Population, Loc.T("Фабрики-ульи"),
                Loc.T("Сборочные линии работают без остановки: каждый мир Консенсуса получает новый синтет."))
                .Cost(minerals: 1500f, alloys: 150f).Time(90, 720)
                .Line(Loc.T("Через 90 дн.: +1 синтет на каждой колонии со свободным жильём"))
                .Need(() => ColoniesWithRoom() == 0 ? Loc.T("Нет свободного жилья") : null)
                .Done(s => Loc.F("Новые синтеты на {0} колониях.", GrowColonies(true)));
            D("nol_optimize", "nol", F, LGIcon.Reactor, Loc.T("Протокол оптимизации"),
                Loc.T("Консенсус пересчитывает каждую цепочку производства. Лишних операций больше нет."))
                .Cost(minerals: 1000f).Time(360, 900)
                .Mod((b, k) => { b.EnergyMult += 0.10f * k; b.AlloysMult += 0.10f * k; b.ResearchMult += 0.10f * k; })
                .Line(Loc.T("+10% гелия-3, сплавов и науки на 360 дн."));
            D("nol_core", "nol", F, LGIcon.Monitor, Loc.T("Ядро Консенсуса"),
                Loc.T("Великий проект: планетарный вычислитель, в котором сходятся все мысли «Ноля»."))
                .Cost(minerals: 2500f).Time(360, 0).Great()
                .Mod((b, k) => { b.ResearchMult += 0.10f * k; b.InfluenceFlat += 1.5f * k; })
                .Line(Loc.T("Навсегда: +10% науки и +1.5 влияния в месяц"));
        }
    }
}
