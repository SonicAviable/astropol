using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>Направления законов: в каждом всегда действует ровно один закон.</summary>
    public enum LawCategory { Economy, Military, Science, Society, Intel }

    /// <summary>Закон империи: постоянные плюсы и минусы, ежемесячная плата влиянием, влияние на стабильность колоний.</summary>
    public sealed class LawDef
    {
        public string Id;
        public LawCategory Category;
        public string Name;
        public string Desc;
        /// <summary>Действует с начала партии; возврат к нему — тоже смена закона.</summary>
        public bool Default;
        /// <summary>Влияние в месяц, пока закон действует.</summary>
        public float Upkeep;
        /// <summary>Прибавка к стабильности всех колоний игрока.</summary>
        public float Stability;
        /// <summary>Наложить (+1) или снять (−1) бонусы закона.</summary>
        public Action<EmpireBonuses, float> Apply;
        /// <summary>Строки эффектов для интерфейса: текст и хорош ли он для игрока.</summary>
        public List<(string Text, bool Good)> Effects = new List<(string, bool)>();
    }

    [Serializable]
    public class LawsSave
    {
        public List<string> Active = new List<string>();
        public List<int> CooldownCategory = new List<int>();
        public List<int> CooldownUntil = new List<int>();
    }

    /// <summary>
    /// Законы империи игрока — долгие решения, которые меняют правила на месяцы и годы:
    /// экономика, армия, наука, общество, разведка; в каждом направлении три закона, один из них исходный.
    ///
    /// Бонусы закона накладываются на бонусы технологий игрока (TechnologyManager.Bonuses) сдвигом,
    /// как черты фракции: при каждом обращении к бонусам набор законов сверяется по версии,
    /// поэтому после загрузки (бонусы собираются заново) законы ложатся сами и не задваиваются.
    /// Смена закона стоит влияния и закрывает направление на <see cref="CooldownDays"/> дней.
    /// </summary>
    public static class EmpireLaws
    {
        public const float SwitchCost = 25f;
        public const int CooldownDays = 120;

        private static readonly List<LawDef> s_all = new List<LawDef>();
        private static readonly Dictionary<LawCategory, LawDef> s_active = new Dictionary<LawCategory, LawDef>();
        private static readonly Dictionary<LawCategory, int> s_cooldown = new Dictionary<LawCategory, int>();
        private static int s_version = 1;

        /// <summary>Законы сменились (окно законов, подсказки, стабильность).</summary>
        public static event Action OnChanged;

        public static IReadOnlyList<LawDef> All { get { EnsureDefs(); return s_all; } }

        public static readonly LawCategory[] Categories =
            { LawCategory.Economy, LawCategory.Military, LawCategory.Science, LawCategory.Society, LawCategory.Intel };

        public static string CategoryName(LawCategory c) => c switch
        {
            LawCategory.Economy => Loc.T("Экономика"),
            LawCategory.Military => Loc.T("Армия"),
            LawCategory.Science => Loc.T("Наука"),
            LawCategory.Society => Loc.T("Общество"),
            _ => Loc.T("Разведка")
        };

        // ================================================================ Состояние

        public static LawDef Active(LawCategory c)
        {
            EnsureDefs();
            if (s_active.TryGetValue(c, out var l) && l != null) return l;
            foreach (var d in s_all) if (d.Category == c && d.Default) { s_active[c] = d; return d; }
            return null;
        }

        public static bool IsActive(LawDef law) => law != null && Active(law.Category) == law;

        public static LawDef Get(string id)
        {
            EnsureDefs();
            foreach (var d in s_all) if (d.Id == id) return d;
            return null;
        }

        /// <summary>Сколько влияния в месяц забирают действующие законы.</summary>
        public static float InfluenceUpkeep
        {
            get
            {
                float s = 0f;
                foreach (var c in Categories) { var l = Active(c); if (l != null) s += l.Upkeep; }
                return s;
            }
        }

        public static int Today()
        {
            var t = TimeManager.Instance;
            return t == null ? 0 : (t.Year - 2200) * 360 + (t.Month - 1) * 30 + (t.Day - 1);
        }

        public static int CooldownLeft(LawCategory c)
            => s_cooldown.TryGetValue(c, out int until) ? Mathf.Max(0, until - Today()) : 0;

        /// <summary>Почему нельзя принять закон (null — можно).</summary>
        public static string Blocker(LawDef law)
        {
            if (law == null) return Loc.T("Нет закона");
            if (IsActive(law)) return Loc.T("Уже действует");
            int cd = CooldownLeft(law.Category);
            if (cd > 0) return Loc.F("Направление недавно менялось: ещё {0} дн.", cd);
            var eco = EconomyManager.Instance;
            if (eco != null && eco.Influence < SwitchCost)
                return Loc.F("Не хватает {0:0} влияния", SwitchCost - eco.Influence);
            return null;
        }

        public static bool TryEnact(LawDef law)
        {
            if (Blocker(law) != null) return false;
            var eco = EconomyManager.Instance;
            if (eco != null) { eco.Influence -= SwitchCost; eco.RaiseResourcesChanged(); }
            s_active[law.Category] = law;
            s_cooldown[law.Category] = Today() + CooldownDays;
            s_version++;
            NotificationCenter.Show(Loc.F("Принят закон «{0}»", law.Name), law.Desc, NotificationCenter.Kind.Success, 5f);
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Наложить действующие законы на бонусы игрока (вызывается при каждом обращении к ним — дёшево).</summary>
        public static void SyncBonuses(EmpireBonuses b)
        {
            if (b == null || b.LawsVersion == s_version) return;
            EnsureDefs();
            if (b.LawsApplied != null)
                foreach (var id in b.LawsApplied) Get(id)?.Apply?.Invoke(b, -1f);
            var applied = new List<string>();
            foreach (var c in Categories)
            {
                var l = Active(c);
                if (l == null) continue;
                l.Apply?.Invoke(b, 1f);
                applied.Add(l.Id);
            }
            b.LawsApplied = applied;
            b.LawsVersion = s_version;
        }

        /// <summary>Слагаемые стабильности колоний игрока от законов.</summary>
        public static void StabilityFactors(int owner, Action<string, float> add)
        {
            if (owner != 0 || add == null) return;
            foreach (var c in Categories)
            {
                var l = Active(c);
                if (l != null && Mathf.Abs(l.Stability) > 0.01f) add(Loc.F("Закон «{0}»", l.Name), l.Stability);
            }
        }

        // ================================================================ Партия и сохранения

        public static void Reset()
        {
            s_active.Clear();
            s_cooldown.Clear();
            s_version++;
            OnChanged?.Invoke();
        }

        public static LawsSave Capture()
        {
            var s = new LawsSave();
            foreach (var c in Categories) { var l = Active(c); if (l != null) s.Active.Add(l.Id); }
            foreach (var kv in s_cooldown) { s.CooldownCategory.Add((int)kv.Key); s.CooldownUntil.Add(kv.Value); }
            return s;
        }

        public static void Restore(LawsSave s)
        {
            s_active.Clear();
            s_cooldown.Clear();
            if (s != null)
            {
                foreach (var id in s.Active) { var l = Get(id); if (l != null) s_active[l.Category] = l; }
                for (int i = 0; i < s.CooldownCategory.Count && i < s.CooldownUntil.Count; i++)
                    s_cooldown[(LawCategory)s.CooldownCategory[i]] = s.CooldownUntil[i];
            }
            s_version++;
            OnChanged?.Invoke();
        }

        // ================================================================ Законы

        private static void EnsureDefs()
        {
            if (s_all.Count > 0) return;

            // ---- Экономика ----
            Def("econ_market", LawCategory.Economy, Loc.T("Свободный рынок"),
                Loc.T("Казна не вмешивается: ровная экономика без перекосов и без недовольства."), true, 0f, 0f,
                (b, k) => { });
            Def("econ_planned", LawCategory.Economy, Loc.T("Плановая промышленность"),
                Loc.T("Заводы работают по плану, сырьё распределяет государство. Больше сплавов, меньше свободы."), false, 0.5f, -3f,
                (b, k) => { b.AlloysMult += 0.15f * k; b.MineralsMult -= 0.05f * k; },
                (Loc.T("+15% сплавов"), true), (Loc.T("−5% титана"), false));
            Def("econ_extraction", LawCategory.Economy, Loc.T("Добыча любой ценой"),
                Loc.T("Шахты и станции на пределе. Сырьё рекой, но колонисты устают и растут медленнее."), false, 0.5f, -4f,
                (b, k) => { b.MineralsMult += 0.20f * k; b.EnergyMult += 0.10f * k; b.PopGrowth -= 0.15f * k; },
                (Loc.T("+20% титана"), true), (Loc.T("+10% гелия-3"), true), (Loc.T("−15% роста населения"), false));

            // ---- Армия ----
            Def("mil_defense", LawCategory.Military, Loc.T("Оборонительная доктрина"),
                Loc.T("Флот бережёт свои системы: крепости и форпосты прочнее."), true, 0f, 0f,
                (b, k) => { b.StarbaseMult += 0.20f * k; },
                (Loc.T("+20% прочности звёздных баз"), true));
            Def("mil_offense", LawCategory.Military, Loc.T("Наступательная доктрина"),
                Loc.T("Удар первым и всей мощью. Орудия злее, но флот обходится дороже."), false, 1f, 0f,
                (b, k) => { b.AllDamage += 0.10f * k; b.UpkeepMult += 0.10f * k; },
                (Loc.T("+10% урона кораблей"), true), (Loc.T("+10% содержания флота"), false));
            Def("mil_mobilize", LawCategory.Military, Loc.T("Всеобщая мобилизация"),
                Loc.T("Вся империя работает на войну: больше кораблей и дешевле, но колонии недовольны."), false, 1.5f, -6f,
                (b, k) => { b.NavalCapacity += 6f * k; b.ShipCostMult -= 0.10f * k; },
                (Loc.T("+6 к флотскому лимиту"), true), (Loc.T("−10% цены кораблей"), true));

            // ---- Наука ----
            Def("sci_applied", LawCategory.Science, Loc.T("Прикладная наука"),
                Loc.T("Учёные работают на нужды дня: разведка систем идёт быстрее."), true, 0f, 0f,
                (b, k) => { b.SurveySpeed += 0.10f * k; },
                (Loc.T("+10% скорости исследования систем"), true));
            Def("sci_fundamental", LawCategory.Science, Loc.T("Фундаментальные исследования"),
                Loc.T("Большие лаборатории и долгие проекты. Наука быстрее, промышленность делится ресурсами."), false, 1f, 0f,
                (b, k) => { b.ResearchMult += 0.15f * k; b.AlloysMult -= 0.05f * k; },
                (Loc.T("+15% науки"), true), (Loc.T("−5% сплавов"), false));
            Def("sci_military", LawCategory.Science, Loc.T("Военные НИОКР"),
                Loc.T("Лучшие умы — на оружие: точнее и злее орудия, остальная наука медленнее."), false, 0.5f, 0f,
                (b, k) => { b.AllDamage += 0.05f * k; b.Accuracy += 5f * k; b.ResearchMult -= 0.05f * k; },
                (Loc.T("+5% урона, +5 точности"), true), (Loc.T("−5% науки"), false));

            // ---- Общество ----
            Def("soc_equality", LawCategory.Society, Loc.T("Равноправие видов"),
                Loc.T("Все граждане равны перед законом — спокойствие в колониях."), true, 0f, 2f,
                (b, k) => { });
            Def("soc_order", LawCategory.Society, Loc.T("Культ порядка"),
                Loc.T("Строгие законы и вездесущие патрули. Бунты редки, но жизнь скучнее."), false, 1f, 8f,
                (b, k) => { b.PopGrowth -= 0.10f * k; b.ResearchMult -= 0.05f * k; },
                (Loc.T("−10% роста населения"), false), (Loc.T("−5% науки"), false));
            Def("soc_growth", LawCategory.Society, Loc.T("Демографическая программа"),
                Loc.T("Пособия семьям и новые жилые кварталы: население растёт заметно быстрее."), false, 1f, -3f,
                (b, k) => { b.PopGrowth += 0.30f * k; },
                (Loc.T("+30% роста населения"), true));

            // ---- Разведка ----
            Def("int_open", LawCategory.Intel, Loc.T("Открытые границы"),
                Loc.T("Торговцы и путешественники ходят свободно — корабли быстрее идут по коридорам."), true, 0f, 0f,
                (b, k) => { b.HyperlaneSpeed += 0.05f * k; },
                (Loc.T("+5% скорости в коридорах"), true));
            Def("int_closed", LawCategory.Intel, Loc.T("Закрытые границы"),
                Loc.T("Досмотр на каждом посту: чужим агентам трудно работать, но и свои разведчики медленнее."), false, 0.5f, 0f,
                (b, k) => { b.CounterIntel += 0.12f * k; b.SurveySpeed -= 0.10f * k; },
                (Loc.T("+12% к контрразведке"), true), (Loc.T("−10% скорости исследования систем"), false));
            Def("int_network", LawCategory.Intel, Loc.T("Агентурная сеть"),
                Loc.T("Резидентуры в каждой столице: операции удаются чаще, агенты внедряются быстрее."), false, 1.5f, 0f,
                (b, k) => { b.SpyPower += 0.10f * k; b.CoverSpeed += 0.30f * k; },
                (Loc.T("+10% к успеху операций"), true), (Loc.T("+30% скорости внедрения"), true));
        }

        private static void Def(string id, LawCategory c, string name, string desc, bool isDefault, float upkeep, float stability,
                                Action<EmpireBonuses, float> apply, params (string, bool)[] effects)
        {
            var d = new LawDef { Id = id, Category = c, Name = name, Desc = desc, Default = isDefault, Upkeep = upkeep, Stability = stability, Apply = apply };
            d.Effects.AddRange(effects);
            if (Mathf.Abs(stability) > 0.01f)
                d.Effects.Add((Loc.F("{0}{1:0} стабильности колоний", stability > 0 ? "+" : "−", Mathf.Abs(stability)), stability > 0));
            s_all.Add(d);
        }
    }
}
