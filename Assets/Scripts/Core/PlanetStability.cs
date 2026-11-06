using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>Уровень стабильности планеты — от бунта до процветания.</summary>
    public enum StabilityTier { Riot, Strike, Discontent, Normal, Prosperity }

    /// <summary>
    /// Стабильность и счастье колоний (0–100). Считается раз в месяц из понятных слагаемых
    /// (губернатор, безработица, война, чужие виды, черты, пропаганда…) и плавно догоняет цель.
    ///   ≥ 75 — процветание: производство +10%;   50–75 — норма;
    ///   35–50 — недовольство: −10% и рост медленнее;   20–35 — забастовки: −40%, рост вдвое ниже;
    ///   &lt; 20 — бунт: −75%, рост стоит, решение по событию; 6 месяцев бунта — система отделяется.
    /// Правила одинаковы для игрока и ИИ; ИИ опытнее в управлении и сам ведёт пропаганду.
    /// </summary>
    public static class PlanetStability
    {
        public const float Base = 50f;
        public const float MonthlyDrift = 6f;
        public const int SecessionMonths = 6;
        public const float ConquestUnrest = 25f;
        public const float UnrestDecay = 1.5f;

        public const float PropagandaInfluence = 25f;
        public const int PropagandaDuration = 12;
        public const float PropagandaBonus = 15f;
        public const int MartialLawDuration = 6;
        public const float MartialLawBonus = 12f;

        /// <summary>Чужой вид интегрируется полностью за 10 лет (при равноправии видов — за 6–7).</summary>
        public const float IntegrationPerMonth = 1f / 120f;
        /// <summary>Полностью интегрированный чужой вид раздражает на 70% меньше.</summary>
        public const float IntegrationRelief = 0.7f;

        public struct Factor
        {
            public string Name;
            public float Value;
            public Factor(string n, float v) { Name = n; Value = v; }
        }

        // ==================== УРОВНИ ====================

        public static StabilityTier TierOf(float s) =>
            s >= 75f ? StabilityTier.Prosperity :
            s >= 50f ? StabilityTier.Normal :
            s >= 35f ? StabilityTier.Discontent :
            s >= 20f ? StabilityTier.Strike : StabilityTier.Riot;

        public static float OutputMult(StabilityTier t) => t switch
        {
            StabilityTier.Prosperity => 1.10f,
            StabilityTier.Discontent => 0.90f,
            StabilityTier.Strike => 0.60f,
            StabilityTier.Riot => 0.25f,
            _ => 1f
        };

        public static float GrowthMult(StabilityTier t) => t switch
        {
            StabilityTier.Prosperity => 1.10f,
            StabilityTier.Discontent => 0.75f,
            StabilityTier.Strike => 0.50f,
            StabilityTier.Riot => 0f,
            _ => 1f
        };

        public static string TierName(StabilityTier t) => t switch
        {
            StabilityTier.Prosperity => Loc.T("Процветание"),
            StabilityTier.Discontent => Loc.T("Недовольство"),
            StabilityTier.Strike => Loc.T("Забастовки"),
            StabilityTier.Riot => Loc.T("Бунт"),
            _ => Loc.T("Спокойствие")
        };

        public static string TierEffect(StabilityTier t) => t switch
        {
            StabilityTier.Prosperity => Loc.T("производство и наука +10%, рост +10%"),
            StabilityTier.Discontent => Loc.T("производство и наука −10%, рост −25%"),
            StabilityTier.Strike => Loc.T("забастовки: производство и наука −40%, рост вдвое ниже"),
            StabilityTier.Riot => Loc.F("бунт: производство −75%, рост остановлен; через {0} мес. бунта система отделится", SecessionMonths),
            _ => Loc.T("без бонусов и штрафов")
        };

        public static Color TierColor(StabilityTier t) => t switch
        {
            StabilityTier.Prosperity => new Color(0.45f, 0.95f, 0.65f),
            StabilityTier.Normal => new Color(0.55f, 0.85f, 0.95f),
            StabilityTier.Discontent => new Color(0.98f, 0.82f, 0.38f),
            StabilityTier.Strike => new Color(1.00f, 0.58f, 0.30f),
            _ => new Color(1.00f, 0.36f, 0.36f)
        };

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        // ==================== РАСЧЁТ ====================

        public static int CapitalOf(int owner)
        {
            if (owner == 0) return EconomyManager.PlayerCapitalId;
            var ai = AIEmpireManager.For(owner);
            return ai != null ? ai.CapitalSystemId : -1;
        }

        public static bool AtWar(int owner)
        {
            if (owner < 0) return false;
            if (owner == 0)
            {
                foreach (var ai in AIEmpireManager.All) if (ai != null && !ai.IsEliminated && ai.AtWar) return true;
                return false;
            }
            var me = AIEmpireManager.For(owner);
            return me != null && me.AtWarWithAnyone;
        }

        private static bool Bankrupt(int owner)
            => owner == 0 ? EconomyManager.Instance != null && EconomyManager.Instance.IsBankrupt
                          : AIEmpireManager.For(owner)?.IsBankrupt ?? false;

        /// <summary>Целевая стабильность планеты; factors (если передан) — слагаемые для подсказки.</summary>
        public static float Target(PlanetData p, List<Factor> factors = null)
        {
            if (p == null || p.Population <= 0) return Base;
            p.SyncPops();
            var sys = p.ParentSystem;
            int owner = p.OwnerId;
            float s = 0f;
            void Add(string name, float v)
            {
                if (Mathf.Abs(v) < 0.5f) return;
                s += v;
                factors?.Add(new Factor(name, v));
            }

            Add(Loc.T("Основа"), Base);

            // Состав: доли органиков по чертам; роботы вне политики
            int organic = 0, alien = 0, foreignClimate = 0;
            float agreeable = 0f, quarrel = 0f, resilient = 0f, harsh = 0f, alienWeight = 0f, alienInteg = 0f;
            int founder = owner >= 0 ? SpeciesRegistry.FounderOf(owner) : -1;
            foreach (var g in p.Pops)
            {
                var sp = SpeciesRegistry.Get(g.Species);
                if (sp == null || sp.Machine) continue;
                organic += g.Count;
                if (founder >= 0 && !SpeciesRegistry.Related(sp.Id, founder))
                {
                    alien += g.Count;
                    alienWeight += g.Count * (1f - IntegrationRelief * g.Integration);
                    alienInteg += g.Integration * g.Count;
                }
                if (!sp.AtHome(p.Type)) foreignClimate += g.Count;
                if (sp.Has(SpeciesTrait.Agreeable)) agreeable += g.Count;
                if (sp.Has(SpeciesTrait.Quarrelsome)) quarrel += g.Count;
                if (sp.Has(SpeciesTrait.Resilient)) { resilient += g.Count; if (p.HabitabilityPercent < 60) harsh += g.Count; }
            }
            float share(float n) => organic > 0 ? n / organic : 0f;
            // Живучие переносят войну и оккупацию вдвое легче
            float hardship = 1f - 0.5f * share(resilient);

            if (sys != null && sys.Id == CapitalOf(owner)) Add(Loc.T("Столица империи"), 10f);

            var gov = LeaderManager.Instance?.GovernorOf(p);
            if (gov != null)
            {
                Add(Loc.F("Губернатор {0}", gov.Name), 5f);
                if (gov.Has("gov_admin") || gov.Has("gov_honest")) Add(Loc.T("Уважаемый губернатор"), 5f);
                if (gov.Has("gov_corrupt")) Add(Loc.T("Продажный губернатор"), -8f);
            }

            if (sys != null && sys.HasStarbase) Add(Loc.T("Звёздная база охраняет порядок"), 5f);

            int idle = p.IdlePops;
            if (idle > 0) Add(Loc.F("Безработица ({0} без работы)", idle), -Mathf.Min(20f, idle * 4f));
            if (p.HousingCapacity < p.Population) Add(Loc.T("Не хватает жилья"), -6f);

            if (organic > 0)
            {
                if (alien > 0)
                    Add(Loc.F("Чужие виды ({0}%, интеграция {1}%)", Mathf.RoundToInt(share(alien) * 100f), Mathf.RoundToInt(alienInteg / alien * 100f)),
                        -15f * share(alienWeight));
                if (foreignClimate > 0) Add(Loc.T("Чужой климат"), -8f * share(foreignClimate));
                if (agreeable > 0) Add(TraitInfo.Name(SpeciesTrait.Agreeable), 6f * share(agreeable));
                if (quarrel > 0) Add(TraitInfo.Name(SpeciesTrait.Quarrelsome), -8f * share(quarrel));
                if (harsh > 0) Add(Loc.T("Живучие на суровом мире"), 5f * share(harsh));
            }
            else Add(Loc.T("Только роботы: некому бунтовать"), 15f);

            if (AtWar(owner)) Add(Loc.T("Война"), -5f * hardship);
            var siege = SiegeManager.Instance != null && sys != null ? SiegeManager.Instance.GetSiege(sys.Id) : null;
            if (siege != null && siege.Active) Add(Loc.T("Осада системы"), -15f * hardship);
            if (p.Unrest > 0.5f) Add(Loc.T("Недавнее завоевание"), -p.Unrest * hardship);
            if (Bankrupt(owner)) Add(Loc.T("Казна пуста"), -10f);

            if (p.PropagandaMonths > 0) Add(Loc.F("Пропаганда ({0} мес.)", p.PropagandaMonths), PropagandaBonus);
            if (p.MartialLawMonths > 0) Add(Loc.F("Военное положение ({0} мес.)", p.MartialLawMonths), MartialLawBonus);

            if (owner > 0) Add(Loc.T("Опыт управления"), 10f);
            EmpireLaws.StabilityFactors(owner, Add);
            EmpireDecisions.StabilityFactors(owner, Add);

            s = Mathf.Clamp(s, 0f, 100f);
            // Империи ИИ не доводят свои миры до бунта — кроме свежезавоёванных
            if (owner > 0 && p.Unrest <= 0.5f) s = Mathf.Max(s, 36f);
            return s;
        }

        /// <summary>Подсказка: уровень, его действие и все слагаемые.</summary>
        public static string Tooltip(PlanetData p)
        {
            var list = new List<Factor>();
            float target = Target(p, list);
            var t = p.Tier;
            var sb = new System.Text.StringBuilder();
            sb.Append(Loc.F("<b>Стабильность: {0:0}</b> · <color=#{1}>{2}</color>\n", p.Stability, Hex(TierColor(t)), TierName(t)));
            sb.Append(Loc.F("<color=#8AA2A8>{0}</color>\n", TierEffect(t)));
            if (Mathf.Abs(target - p.Stability) >= 1f)
                sb.Append(Loc.F("<color=#8AA2A8>Движется к {0:0} (до {1} в месяц)</color>\n", target, MonthlyDrift));
            sb.Append("<color=#8AA2A8>──────────────</color>\n");
            foreach (var f in list)
            {
                string col = f.Value >= 0f ? "#9EF0C8" : "#FF9A8A";
                sb.Append($"{f.Name}: <color={col}>{(f.Value >= 0f ? "+" : "−")}{Mathf.Abs(f.Value):0}</color>\n");
            }
            if (p.RiotMonths > 0)
                sb.Append(Loc.F("\n<color=#FF6B6B>Бунт идёт {0} мес. из {1}: затем система отделится.</color>", p.RiotMonths, SecessionMonths));
            return sb.ToString().TrimEnd('\n');
        }

        // ==================== ЕЖЕМЕСЯЧНЫЙ ХОД ====================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Boot() => _subscribed = false;

        private static bool _subscribed;

        /// <summary>Подписка на захват систем: завоёванные миры получают недовольство.</summary>
        public static void Attach()
        {
            if (_subscribed) return;
            _subscribed = true;
            SiegeManager.OnSystemCaptured += OnCaptured;
        }

        private static void OnCaptured(StarSystem sys, int oldOwner, int newOwner)
        {
            if (sys == null || newOwner < 0) return;
            foreach (var p in sys.Planets)
            {
                if (p.Population <= 0) continue;
                p.SyncPops();
                // Освобождённые сородичи радуются, чужаки — сопротивляются
                int founder = SpeciesRegistry.FounderOf(newOwner);
                int organic = p.OrganicPops;
                int kin = p.CountOf(s => !s.Machine && SpeciesRegistry.Related(s.Id, founder));
                float alienShare = organic > 0 ? 1f - kin / (float)organic : 0f;
                // Мирная передача по договору встречает вдвое меньше сопротивления, чем завоевание
                float unrest = ConquestUnrest * Mathf.Lerp(0.3f, 1f, alienShare) * (SiegeManager.IsPeacefulTransfer ? 0.5f : 1f);
                p.Unrest = Mathf.Max(p.Unrest, unrest);
                p.RiotMonths = 0;
                p.PropagandaMonths = 0;
                p.MartialLawMonths = 0;
                p.Stability = Mathf.Min(p.Stability, 45f);
                // Новая власть: сородичи сразу свои, остальным приживаться заново
                foreach (var g in p.Pops) g.Integration = SpeciesRegistry.Related(g.Species, founder) ? 1f : 0f;
            }
        }

        /// <summary>Раз в месяц: стабильность, пропаганда ИИ, бунты, отделение, рождение гибридов.</summary>
        public static void MonthlyTick(IReadOnlyList<StarSystem> systems)
        {
            Attach();
            if (systems == null) return;
            var seceding = new List<StarSystem>();
            foreach (var sys in systems)
            {
                if (sys.OwnerId < 0) continue;
                foreach (var p in sys.Planets)
                {
                    if (p.Population <= 0) continue;
                    p.SyncPops();
                    var before = p.Tier;

                    if (p.Unrest > 0f) p.Unrest = Mathf.Max(0f, p.Unrest - UnrestDecay);
                    if (p.PropagandaMonths > 0) p.PropagandaMonths--;
                    if (p.MartialLawMonths > 0) p.MartialLawMonths--;

                    float target = Target(p);
                    p.Stability = Mathf.MoveTowards(p.Stability, target, MonthlyDrift);
                    var after = p.Tier;

                    if (sys.OwnerId > 0) AIManage(sys.OwnerId, p);
                    if (sys.OwnerId == 0 && after != before) NotifyTier(p, before, after);

                    if (after == StabilityTier.Riot)
                    {
                        p.RiotMonths++;
                        if (sys.OwnerId == 0 && (p.RiotMonths == 1 || p.RiotMonths == 4)) EventDatabase.ScheduleRiot(p);
                        if (p.RiotMonths >= SecessionMonths && sys.Id != CapitalOf(sys.OwnerId) && Leads(p) && !seceding.Contains(sys))
                            seceding.Add(sys);
                    }
                    else p.RiotMonths = 0;

                    TryHybrid(p);
                    FactionTraits.TryAssimilate(p);
                    Integrate(p);
                }
            }
            foreach (var sys in seceding) Secede(sys);
            Migration.MonthlyTick(systems);
        }

        /// <summary>Планета — самая населённая в системе (её бунт решает судьбу системы).</summary>
        private static bool Leads(PlanetData p)
        {
            foreach (var o in p.ParentSystem.Planets) if (o != p && o.Population > p.Population) return false;
            return true;
        }

        private static void AIManage(int owner, PlanetData p)
        {
            if (p.Stability >= 42f || p.PropagandaMonths > 0) return;
            var ai = AIEmpireManager.For(owner);
            if (ai == null || ai.Stock("influence") < PropagandaInfluence) return;
            ai.SpendStock("influence", PropagandaInfluence);
            p.PropagandaMonths = PropagandaDuration;
        }

        private static void NotifyTier(PlanetData p, StabilityTier before, StabilityTier after)
        {
            bool worse = after < before;
            string body = Loc.F("{0}: {1}", p.Name, TierEffect(after));
            switch (after)
            {
                case StabilityTier.Riot:
                    NotificationCenter.Show(Loc.F("Бунт на {0}", p.Name), body, NotificationCenter.Kind.Danger, 9f);
                    break;
                case StabilityTier.Strike when worse:
                    NotificationCenter.Show(Loc.F("Забастовки на {0}", p.Name), body, NotificationCenter.Kind.Warning, 7f);
                    break;
                case StabilityTier.Discontent when worse:
                    NotificationCenter.Show(Loc.F("Недовольство на {0}", p.Name), body, NotificationCenter.Kind.Info, 5f);
                    break;
                case StabilityTier.Prosperity:
                    NotificationCenter.Show(Loc.F("{0} процветает", p.Name), body, NotificationCenter.Kind.Success, 5f);
                    break;
                default:
                    if (!worse && before <= StabilityTier.Strike)
                        NotificationCenter.Show(Loc.F("На {0} спокойнее", p.Name), body, NotificationCenter.Kind.Success, 5f);
                    break;
            }
        }

        // ==================== ГИБРИДЫ ====================

        public const float HybridChance = 0.015f;

        /// <summary>Два органических вида по 2+ жителя на спокойной планете иногда дают смешанное потомство.</summary>
        /// <summary>
        /// Чужие виды понемногу приживаются: быстрее при равноправии видов (закон игрока) и в процветании,
        /// медленнее в недовольстве, при забастовках и бунте — не приживаются вовсе.
        /// </summary>
        private static void Integrate(PlanetData p)
        {
            int founder = SpeciesRegistry.FounderOf(p.OwnerId);
            float rate = IntegrationPerMonth;
            if (p.OwnerId == 0 && EmpireLaws.IsActive(EmpireLaws.Get("soc_equality"))) rate *= 1.5f;
            rate *= p.Tier switch
            {
                StabilityTier.Prosperity => 1.25f,
                StabilityTier.Discontent => 0.5f,
                StabilityTier.Strike => 0f,
                StabilityTier.Riot => 0f,
                _ => 1f
            };
            if (rate <= 0f) return;
            foreach (var g in p.Pops)
            {
                var sp = SpeciesRegistry.Get(g.Species);
                if (sp == null || sp.Machine || SpeciesRegistry.Related(sp.Id, founder)) continue;
                g.Integration = Mathf.Min(1f, g.Integration + rate);
            }
        }

        private static void TryHybrid(PlanetData p)
        {
            if (p.Stability < 50f || p.Pops.Count < 2) return;
            PopGroup a = null, b = null;
            foreach (var g in p.Pops)
            {
                var sp = SpeciesRegistry.Get(g.Species);
                if (sp == null || sp.Machine || g.Count < 2) continue;
                if (a == null || g.Count > a.Count) { b = a; a = g; }
                else if (b == null || g.Count > b.Count) b = g;
            }
            if (a == null || b == null || Random.value > HybridChance) return;
            var h = SpeciesRegistry.Hybrid(a.Species, b.Species, out bool created);
            if (h == null) return;
            p.ConvertPops(b.Species, h.Id, 1);

            int owner = p.OwnerId;
            if (owner == 0)
                NotificationCenter.Show(created ? Loc.T("Новый вид") : Loc.T("Гибриды рождаются"),
                    Loc.F("На {0} появились {1}: {2}", p.Name, h.InText, h.TraitsLine), NotificationCenter.Kind.Success, 8f);
            if (created && GalacticNews.Known(owner))
                GalacticNews.Post(NewsKind.Society, Loc.F("{0}: на {1} родился новый вид — {2}", GalacticNews.Who(owner), p.Name, h.InText),
                    p.ParentSystem != null ? p.ParentSystem.Id : -1, owner == 0);
        }

        // ==================== ОТДЕЛЕНИЕ ====================

        private static void Secede(StarSystem sys)
        {
            int old = sys.OwnerId;
            foreach (var p in sys.Planets)
            {
                if (p.Population <= 0) continue;
                p.RiotMonths = 0;
                p.PropagandaMonths = p.MartialLawMonths = 0;
                p.Unrest = Mathf.Max(p.Unrest, 15f);
                p.Stability = 40f;
            }
            if (SiegeManager.Instance != null) SiegeManager.Instance.Secede(sys);
            else sys.OwnerId = -1;

            if (old == 0)
                NotificationCenter.Show(Loc.F("{0} отделилась", sys.Name),
                    Loc.T("Полгода бунта — и колония провозгласила независимость. Постройте в системе форпост, чтобы вернуть её."),
                    NotificationCenter.Kind.Danger, 12f);
            if (GalacticNews.Known(old))
                GalacticNews.Post(NewsKind.Society, Loc.F("Восстание: система {0} вышла из-под власти {1} и объявила независимость", GalacticNews.Place(sys), GalacticNews.Who(old)),
                    sys.Id, old == 0);
        }

        // ==================== ДЕЙСТВИЯ ИГРОКА ====================

        /// <summary>Кампания пропаганды: −25 влияния, +15 стабильности на 12 месяцев.</summary>
        public static bool TryPropaganda(PlanetData p)
        {
            var eco = EconomyManager.Instance;
            if (p == null || !p.IsPlayerOwned || p.Population <= 0 || p.PropagandaMonths > 0 || eco == null) return false;
            if (!eco.TrySpend(0f, 0f, 0f, PropagandaInfluence)) return false;
            p.PropagandaMonths = PropagandaDuration;
            p.Stability = Mathf.Min(100f, p.Stability + 5f);   // первые плакаты — сразу заметно
            eco.RaiseResourcesChanged();
            return true;
        }
    }
}
