using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace StellarisClone.Core
{
    /// <summary>
    /// База событий. Каждое событие — шаблон: когда может случиться (разведка, раз в месяц,
    /// продолжение цепочки), с каким контекстом (система, корабль, лидер, планета, соседняя империя)
    /// и какие варианты предлагает. Часть вариантов открывается только лидеру с нужной чертой
    /// или классом — так лидеры становятся частью историй, а не только множителями.
    /// </summary>
    public static partial class EventDatabase
    {
        private enum Trigger { Survey, Monthly, Chain }

        private class Template
        {
            public string Id;
            public Trigger When;
            public float Weight = 1f;
            public bool Once;
            /// <summary>Разведка и цепочки: подходит ли контекст.</summary>
            public Func<EventContext, bool> CanFire;
            /// <summary>Ежемесячные: подобрать контекст (null — событие сейчас невозможно).</summary>
            public Func<EventContext> FindContext;
            public Func<EventContext, GameEventData> Build;
        }

        private static readonly List<Template> _all = new List<Template>();
        private static bool _initialized;

        // ==================== ВЫБОР СОБЫТИЯ ====================

        public static GameEventData RollSurvey(EventContext ctx, EventState st)
        {
            Init();
            var pool = new List<(Template, EventContext)>();
            foreach (var t in _all)
                if (t.When == Trigger.Survey && Allowed(t, st) && (t.CanFire == null || t.CanFire(ctx))) pool.Add((t, ctx));
            return Pick(pool, st);
        }

        public static GameEventData RollMonthly(EventState st)
        {
            Init();
            var pool = new List<(Template, EventContext)>();
            foreach (var t in _all)
            {
                if (t.When != Trigger.Monthly || !Allowed(t, st) || t.FindContext == null) continue;
                var ctx = t.FindContext();
                if (ctx != null) pool.Add((t, ctx));
            }
            return Pick(pool, st);
        }

        public static GameEventData BuildChain(string id, EventContext ctx)
        {
            Init();
            foreach (var t in _all)
                if (t.Id == id && t.When == Trigger.Chain && (t.CanFire == null || t.CanFire(ctx)))
                    return Finish(t, ctx);
            return null;
        }

        private static bool Allowed(Template t, EventState st) => !t.Once || st == null || !st.Fired.Contains(t.Id);

        private static GameEventData Pick(List<(Template t, EventContext ctx)> pool, EventState st)
        {
            if (pool.Count == 0) return null;
            float total = 0f;
            foreach (var p in pool) total += p.t.Weight;
            float r = Random.value * total;
            foreach (var p in pool)
            {
                r -= p.t.Weight;
                if (r > 0f) continue;
                if (p.t.Once && st != null) st.Fired.Add(p.t.Id);
                return Finish(p.t, p.ctx);
            }
            var last = pool[pool.Count - 1];
            return Finish(last.t, last.ctx);
        }

        private static GameEventData Finish(Template t, EventContext ctx)
        {
            var ev = t.Build(ctx);
            if (ev != null && string.IsNullOrEmpty(ev.Subtitle)) ev.Subtitle = Subtitle(ctx);
            return ev;
        }

        private static string Subtitle(EventContext c)
        {
            if (c == null) return "";
            var parts = new List<string>();
            if (c.System != null) parts.Add(Loc.F("система {0}", c.System.Name));
            if (c.Planet != null) parts.Add(Loc.F("колония {0}", c.Planet.Name));
            if (c.Ship != null) parts.Add(c.Ship.Name);
            if (c.Leader != null) parts.Add(Loc.F("{0} {1}, ур. {2}", LeaderManager.ClassName(c.Leader.Class).ToLower(), c.Leader.Name, c.Leader.Level));
            if (c.Rival >= 0) parts.Add(RivalName(c.Rival));
            return string.Join("  ·  ", parts);
        }

        // ==================== ПОМОЩНИКИ ====================

        private static EconomyManager Eco => EconomyManager.Instance;
        private static LeaderManager Leaders => LeaderManager.Instance;

        private static void Give(float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f)
        {
            var eco = Eco;
            if (eco == null) return;
            eco.EnergyCredits = Mathf.Max(0f, eco.EnergyCredits + energy);
            eco.Minerals = Mathf.Max(0f, eco.Minerals + minerals);
            eco.Alloys = Mathf.Max(0f, eco.Alloys + alloys);
            eco.Influence = Mathf.Max(0f, eco.Influence + influence);
            eco.RaiseResourcesChanged();
        }

        private static bool CanPay(float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f)
            => Eco != null && Eco.CanAfford(energy, minerals, alloys, influence);

        /// <summary>Пометка платного варианта: «Нужно 80 титана», а если не хватает — и сколько именно.</summary>
        private static string Need(float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f)
        {
            var parts = new List<string>();
            if (energy > 0f) parts.Add(Loc.F("{0} гелия-3", Mathf.RoundToInt(energy)));
            if (minerals > 0f) parts.Add(Loc.F("{0} титана", Mathf.RoundToInt(minerals)));
            if (alloys > 0f) parts.Add(Loc.F("{0} сплавов", Mathf.RoundToInt(alloys)));
            if (influence > 0f) parts.Add(Loc.F("{0} влияния", Mathf.RoundToInt(influence)));
            string need = Loc.T("Нужно ") + string.Join(", ", parts);
            string miss = CostCheck.Full(energy, minerals, alloys, influence);
            return miss == null ? need : $"{need} · {miss.ToLower()}";
        }

        private static void Pay(float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f)
            => Eco?.TrySpend(energy, minerals, alloys, influence);

        private static void Monthly(float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f)
        {
            Eco?.AddIncome(energy, minerals, alloys, influence);
            Eco?.RaiseResourcesChanged();
        }

        private static void Science(float amount) => TechnologyManager.Instance?.AddInstantScience(amount);

        private static void Xp(EventContext c, float xp)
        {
            if (c?.Leader != null) Leaders?.GrantXp(c.Leader, xp);
        }

        private static bool LeaderHas(EventContext c, string trait) => c?.Leader != null && c.Leader.Has(trait);
        private static string Years(int n)
        {
            int m = n % 100, d = n % 10;
            if (m >= 11 && m <= 14) return Loc.T("лет");
            return d == 1 ? Loc.T("год") : d >= 2 && d <= 4 ? Loc.T("года") : Loc.T("лет");
        }

        private static string TraitName(string id) => LeaderManager.Trait(id)?.Name ?? id;

        private static void DamageShip(FleetData d, float fraction)
        {
            if (d == null || d.Destroyed) return;
            d.ShieldPoints = 0f;
            d.ArmorPoints = Mathf.Max(0f, d.ArmorPoints - d.MaxArmorPoints * fraction);
            d.HullPoints = Mathf.Max(d.MaxHullPoints * 0.1f, d.HullPoints - d.MaxHullPoints * fraction);
        }

        private static void Recalc() => EconomyManager.Instance?.RecalculateAll();

        private static List<EventOption> Opts(params EventOption[] o) => new List<EventOption>(o);

        private static GameEventData Ev(string id, string title, string text, AnomalyType type, List<EventOption> options)
            => new GameEventData(id, title, text, type, options) { Art = _art.TryGetValue(id, out var a) ? a : null };

        /// <summary>Иллюстрации событий (файлы в Resources/UI/Events).</summary>
        private static readonly Dictionary<string, string> _art = new Dictionary<string, string>
        {
            ["ancient_derelict_cruiser"] = "Derelict",
            ["alien_amoeba_belt"]        = "Amoeba",
            ["precursor_relic"]          = "Relic",
            ["spatial_rift"]             = "Rift",
            ["rift_stabilized"]          = "Rift",
            ["ghost_signal"]             = "Rift",
            ["distress_signal"]          = "Distress",
            ["distress_survivors"]       = "Distress",
            ["rival_defector"]           = "Defector",
            ["colony_festival"]          = "Festival",
            ["colony_quake"]             = "Quake",
            ["colony_plague"]            = "Plague",
            ["trade_delegation"]         = "Trade",
            ["dormant_probe"]            = "Probe",
            ["crystal_field"]            = "Crystal",
            ["leader_breakthrough"]      = "Breakthrough",
            ["admiral_drills"]           = "Drills",
            ["governor_corruption"]      = "Corruption",
            ["leader_aging"]             = "Aging",
            ["spy_intercept"]            = "Intercept",
            ["spy_double_agent"]         = "DoubleAgent",
            ["spy_mole_found"]           = "Mole",
            ["spy_mole_result"]          = "Mole",
            ["spy_agent_compromised"]    = "Compromised",
            ["spy_info_broker"]          = "Broker",
            ["colony_refugees"]          = "Refugees",
            ["colony_strike"]            = "Strike",
            ["colony_riot"]              = "Strike",
            ["colony_meteor"]            = "Meteor",
            ["colony_young_scientists"]  = "Academy",
            ["military_deserters"]       = "Deserters",
            ["military_ghost_contact"]   = "GhostContact",
            ["survey_gas_harvest"]       = "GasStorm",
            ["survey_frozen_colony"]     = "FrozenArk",
            ["survey_ancient_battlefield"] = "Battlefield",
            ["diplo_summit"]             = "Summit",
            ["diplo_rival_crisis"]       = "Famine",
            ["pirate_haven"]             = "PirateHaven",
            ["leviathan_slain"]          = "Leviathan",
            ["precursor_signal"]         = "PrecursorSignal",
            ["precursor_decoding"]       = "PrecursorDecoding",
            ["precursor_map"]            = "PrecursorMap",
            ["precursor_vault"]          = "PrecursorVault",
            ["precursor_core"]           = "PrecursorCore",
            ["precursor_echo"]           = "PrecursorEcho",
            ["leader_ambition"]          = "Ambition",
            ["leader_ambition_result"]   = "Ambition",
        };

        private static void Schedule(string id, int days, EventContext ctx) => AnomalyEventSystem.Instance?.Schedule(id, days, ctx);

        // ---- Контексты для ежемесячных событий ----

        private static T RandomOf<T>(List<T> list) => list.Count == 0 ? default : list[Random.Range(0, list.Count)];

        private static EventContext LeaderCtx(Func<Leader, bool> filter)
        {
            var lm = Leaders;
            if (lm == null) return null;
            var list = new List<Leader>();
            foreach (var l in lm.Of(0)) if (filter(l)) list.Add(l);
            var pick = RandomOf(list);
            if (pick == null) return null;
            var ctx = new EventContext { Leader = pick };
            if (pick.Post == LeaderPost.Planet)
            {
                ctx.System = EmpireStats.GetSystem(pick.SystemId);
                if (ctx.System != null && pick.PlanetIndex >= 0 && pick.PlanetIndex < ctx.System.Planets.Count)
                    ctx.Planet = ctx.System.Planets[pick.PlanetIndex];
            }
            else if (pick.Post == LeaderPost.Ship)
            {
                ctx.Ship = FindShip(pick.ShipId);
                if (ctx.Ship != null) ctx.System = EmpireStats.GetSystem(ctx.Ship.CurrentSystemId);
            }
            return ctx;
        }

        private static EventContext ColonyCtx(int minPop)
        {
            var list = new List<PlanetData>();
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != 0) continue;
                foreach (var p in s.Planets) if (p.Population >= minPop) list.Add(p);
            }
            var pick = RandomOf(list);
            if (pick == null) return null;
            return new EventContext { Planet = pick, System = pick.ParentSystem, Leader = Leaders?.GovernorOf(pick) };
        }

        private static EventContext WarshipCtx()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return null;
            var list = new List<FleetData>();
            foreach (var f in fm.AllFleets)
                if (f?.Data != null && !f.Data.Destroyed && f.Data.OwnerId == 0 && f.Data.Type == FleetType.Military && !f.Data.InCombat)
                    list.Add(f.Data);
            var pick = RandomOf(list);
            if (pick == null) return null;
            return new EventContext { Ship = pick, System = EmpireStats.GetSystem(pick.CurrentSystemId), Leader = Leaders?.LeaderOfShip(pick.Id) };
        }

        /// <summary>
        /// Случайная империя ИИ, подходящая событию (needPeace — только те, с кем мир; border — только соседи).
        /// Только знакомые игроку: о незнакомцах события не рассказывают.
        /// </summary>
        private static EventContext RivalCtx(bool needPeace, bool needBorder = false)
        {
            var list = new List<AIEmpireManager>();
            foreach (var ai in AIEmpireManager.Alive)
            {
                if (!Contacts.PlayerMet(ai.OwnerId)) continue;
                if (needPeace && ai.AtWar) continue;
                if (EmpireStats.SystemCount(ai.OwnerId) == 0) continue;
                if (needBorder && EmpireStats.SharedBorderCount(ai.OwnerId, 0) == 0) continue;
                list.Add(ai);
            }
            var pick = RandomOf(list);
            return pick != null ? new EventContext { Rival = pick.OwnerId } : null;
        }

        private static string RivalName(int owner) => AIEmpireManager.NameOf(owner, Loc.T("соседняя империя"));

        private static void Opinion(int rival, float delta) => AIEmpireManager.For(rival)?.AddMemory("incident", delta);

        private static FleetData FindShip(int id)
        {
            var fm = FleetManager.Instance;
            if (fm == null || id < 0) return null;
            foreach (var f in fm.AllFleets) if (f?.Data != null && f.Data.Id == id && !f.Data.Destroyed) return f.Data;
            return null;
        }

        private static int SpawnSystem(EventContext c)
        {
            if (c?.Ship != null && c.Ship.CurrentSystemId >= 0) return c.Ship.CurrentSystemId;
            return FleetManager.Instance != null ? FleetManager.Instance.PlayerShipyardSystem() : -1;
        }

        // ==================== СОБЫТИЯ ====================

        private static void Add(Template t) => _all.Add(t);

        private static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            AddSurveyEvents();
            AddLeaderEvents();
            AddColonyEvents();
            AddMilitaryEvents();
            AddDiplomaticEvents();
            AddFactionDecisions();
            AddThreatEvents();
            AddPrecursorChain();
            AddLeaderAmbitions();
            AddMoreEvents();
            AddSocietyEvents();
            AddAllianceEvents();
            AddLongChains();
        }

        // -------------------- Разведка --------------------

        private static void AddSurveyEvents()
        {
            Add(new Template
            {
                Id = "ancient_derelict_cruiser", When = Trigger.Survey,
                Build = c => Ev("ancient_derelict_cruiser", Loc.T("ЗАБРОШЕННЫЙ ДРЕЙФУЮЩИЙ КРЕЙСЕР"),
                    Loc.T("Датчики научного судна засекли на высокой орбите объект класса «Крейсер». Корпус покрыт микрометеоритной эрозией ") +
                    Loc.T("возрастом около трёхсот лет. Бортовой транслятор молчит, но сигнатура реактора стабильна. Архивы в центральном блоке, ") +
                    Loc.T("вероятно, сохранились."),
                    AnomalyType.AncientDerelict, Opts(
                        new EventOption(Loc.T("РАЗОБРАТЬ НА МЕТАЛЛОЛОМ"), Loc.T("+120 сплавов."), () => Give(alloys: 120f)),
                        new EventOption(Loc.T("ИЗУЧИТЬ БОРТОВОЙ АРХИВ"), Loc.T("+80 науки. 30% — мгновенный прорыв случайной технологии."), () =>
                        {
                            Science(80f);
                            if (Random.value < 0.30f) TechnologyManager.Instance?.CompleteRandomTech();
                            Xp(c, 20f);
                        }),
                        new EventOption(Loc.T("ВОССТАНОВИТЬ РЕАКТОР"), Loc.T("50%: +240 сплавов и трофейный корвет. 50%: выброс — корабль повреждён, −50 гелия-3."), () =>
                        {
                            if (Random.value < 0.5f)
                            {
                                Give(alloys: 240f);
                                int sys = SpawnSystem(c);
                                if (sys >= 0) FleetManager.Instance?.CreateFleetObject(Loc.F("Корвет-Призрак {0}", FleetManager.Instance.AllFleets.Count + 1), sys, FleetType.Military);
                                NotificationCenter.Show(Loc.T("Реактор ожил"), Loc.T("Крейсер разобран, из уцелевших модулей собран корвет"), NotificationCenter.Kind.Success, 5f);
                            }
                            else
                            {
                                DamageShip(c?.Ship, 0.5f);
                                Give(energy: -50f);
                                NotificationCenter.Show(Loc.T("Выброс реактора"), Loc.T("Научное судно повреждено при попытке запуска"), NotificationCenter.Kind.Danger, 5f);
                            }
                        }),
                        new EventOption(Loc.T("ПРОЧИТАТЬ ЧУЖУЮ СХЕМОТЕХНИКУ"), Loc.T("Без риска: +120 сплавов и +80 науки, учёный получает опыт."), () =>
                        {
                            Give(alloys: 120f); Science(80f); Xp(c, 40f);
                        }, Loc.F("Учёный-{0}", TraitName("sci_xeno").ToLower()), () => LeaderHas(c, "sci_xeno"))))
            });

            Add(new Template
            {
                Id = "alien_amoeba_belt", When = Trigger.Survey,
                Build = c => Ev("alien_amoeba_belt", Loc.T("КОСМИЧЕСКАЯ АМЁБА В АСТЕРОИДНОМ ПОЯСЕ"),
                    Loc.T("В поясе астероидов обнаружена колония кремниевых амёбоподобных форм. Они поглощают силикаты и выделяют в метаболические ") +
                    Loc.T("пузырьки чистый гелий-3. Нейросканеры показывают реакцию на фононные колебания — возможно, колония способна к обучению."),
                    AnomalyType.AlienFauna, Opts(
                        new EventOption(Loc.T("ОСТАВИТЬ В ПОКОЕ И НАБЛЮДАТЬ"), Loc.T("+2 науки в месяц навсегда."), () =>
                        {
                            TechnologyManager.Instance?.AddMonthlyScience(2f); Xp(c, 15f);
                        }),
                        new EventOption(Loc.T("УНИЧТОЖИТЬ И СОБРАТЬ БИОМАТЕРИАЛ"), Loc.T("+90 гелия-3 разово."), () => Give(energy: 90f)),
                        new EventOption(Loc.T("ПРИРУЧИТЬ КОЛОНИЮ"), Loc.T("+3 гелия-3 в месяц навсегда, учёный получает опыт."), () =>
                        {
                            Monthly(energy: 3f); Xp(c, 40f);
                        }, Loc.F("Учёный-{0}", TraitName("sci_xeno").ToLower()), () => LeaderHas(c, "sci_xeno"))))
            });

            Add(new Template
            {
                Id = "precursor_relic", When = Trigger.Survey, Weight = 0.8f,
                Build = c => Ev("precursor_relic", Loc.T("РЕЛИКТ ПРЕДШЕСТВЕННИКОВ"),
                    Loc.T("На экваториальной плите третьей планеты обнаружен монолит неизвестного сплава. Возраст — 127 миллионов лет. ") +
                    Loc.T("На поверхности 14 тысяч глифов, похожих на протоматематические уравнения."),
                    AnomalyType.PrecursorRelic, Opts(
                        new EventOption(Loc.T("ДЕШИФРОВАТЬ ГЛИФЫ"), Loc.T("+150 науки, +1 влияния в месяц навсегда."), () =>
                        {
                            Science(150f); Monthly(influence: 1f); Xp(c, 25f);
                        }),
                        new EventOption(Loc.T("ИЗВЛЕЧЬ МОНОЛИТ НА БАЗУ"), Loc.T("+200 сплавов за сверхпрочную оболочку."), () => Give(alloys: 200f)),
                        new EventOption(Loc.T("ПОЛНАЯ РАСШИФРОВКА"), Loc.T("+250 науки, +1 влияния в месяц и прорыв случайной технологии."), () =>
                        {
                            Science(250f); Monthly(influence: 1f); TechnologyManager.Instance?.CompleteRandomTech(); Xp(c, 50f);
                        }, Loc.F("Учёный-{0}", TraitName("sci_analyst").ToLower()), () => LeaderHas(c, "sci_analyst"))))
            });

            Add(new Template
            {
                Id = "spatial_rift", When = Trigger.Survey, Weight = 0.8f,
                Build = c => Ev("spatial_rift", Loc.T("ПРОСТРАНСТВЕННЫЙ РАЗЛОМ «ТИФОН»"),
                    Loc.T("На окраине системы возникла субпространственная аномалия. Через разлом идут слабые гравитационные волны, ") +
                    Loc.T("синхронизированные с неизвестным пульсаром в полутора килопарсеках."),
                    AnomalyType.SpatialRift, Opts(
                        new EventOption(Loc.T("ЗОНДИРОВАТЬ РАЗЛОМ"), Loc.T("60%: +40 влияния. 40%: корабль повреждён, −50 гелия-3."), () =>
                        {
                            if (Random.value < 0.6f) Give(influence: 40f);
                            else { Give(energy: -50f); DamageShip(c?.Ship, 0.4f); }
                            Xp(c, 20f);
                        }),
                        new EventOption(Loc.T("ВЕРНУТЬСЯ С ДАННЫМИ"), Loc.T("+60 науки. Безопасно."), () => Science(60f)),
                        new EventOption(Loc.T("НАНЕСТИ РАЗЛОМ НА КАРТЫ"), Loc.T("+100 науки. Через пару месяцев разлом может стабилизироваться — продолжение следует."), () =>
                        {
                            Science(100f); Xp(c, 30f);
                            Schedule("rift_stabilized", 60, c);
                        }, Loc.F("Учёный-{0}", TraitName("sci_cartographer").ToLower()), () => LeaderHas(c, "sci_cartographer"))))
            });

            Add(new Template
            {
                Id = "rift_stabilized", When = Trigger.Chain,
                Build = c => Ev("rift_stabilized", Loc.T("РАЗЛОМ СТАБИЛИЗИРОВАЛСЯ"),
                    Loc.T("Карты разлома «Тифон» оказались точнее, чем мы надеялись: аномалия схлопнулась в устойчивый канал. Через него ") +
                    Loc.T("можно гнать зонды в соседние области пространства или превратить канал в источник энергии."),
                    AnomalyType.SpatialRift, Opts(
                        new EventOption(Loc.T("НАУЧНАЯ ПРОГРАММА"), Loc.T("Исследования +25% на 120 дней."), () => TechnologyManager.Instance?.ApplyTempResearchBoost(1.25f, 120)),
                        new EventOption(Loc.T("ЭНЕРГОСТАНЦИЯ НА КАНАЛЕ"), Loc.T("+4 гелия-3 в месяц навсегда."), () => Monthly(energy: 4f))))
            });

            Add(new Template
            {
                Id = "distress_signal", When = Trigger.Survey,
                Build = c => Ev("distress_signal", Loc.T("ЗАШИФРОВАННЫЙ СИГНАЛ БЕДСТВИЯ"),
                    Loc.T("В дециметровом диапазоне принят сигнал бедствия. Источник — обломок транспорта с радиационным шлейфом. ") +
                    Loc.T("Бортовой самописец, вероятно, содержит навигационные карты ближних систем, а в спасательных капсулах может быть кто-то живой."),
                    AnomalyType.DistressSignal, Opts(
                        new EventOption(Loc.T("СПАСАТЕЛЬНАЯ ОПЕРАЦИЯ"), Loc.T("+30 влияния, +60 титана, +60 гелия-3."), () => Give(energy: 60f, minerals: 60f, influence: 30f)),
                        new EventOption(Loc.T("БЕСКОНТАКТНОЕ СКАНИРОВАНИЕ"), Loc.T("+60 науки и исследования +50% на 30 дней."), () =>
                        {
                            Science(60f); TechnologyManager.Instance?.ApplyTempResearchBoost(1.5f, 30);
                        }),
                        new EventOption(Loc.T("ВСКРЫТЬ КАПСУЛЫ"), Loc.T("Опытный учёный успеет откачать выживших. Продолжение следует."), () =>
                        {
                            Xp(c, 30f);
                            Schedule("distress_survivors", 25, c);
                        }, Loc.T("Учёный 2-го уровня и выше"), () => c?.Leader != null && c.Leader.Level >= 2)))
            });

            Add(new Template
            {
                Id = "distress_survivors", When = Trigger.Chain,
                Build = c => Ev("distress_survivors", Loc.T("ВЫЖИВШИЕ С «ПОЛЯРИСА»"),
                    Loc.T("Из капсул извлекли одиннадцать человек в криосне. Среди них — капитан транспорта, опытный навигатор, и инженер ") +
                    Loc.T("орбитальных верфей. Оба готовы служить приютившей их империи."),
                    AnomalyType.DistressSignal, Opts(
                        new EventOption(Loc.T("ПРИНЯТЬ КАПИТАНА"), Loc.T("Адмирал 2-го уровня с чертой «Навигатор» поступает на службу бесплатно."), () =>
                        {
                            var l = Leaders?.RecruitFree(0, LeaderClass.Admiral, 2, "adm_navigator");
                            if (l == null) Give(influence: 40f);
                        }, Loc.T("Есть место для лидера"), () => Leaders != null && Leaders.CountFor(0) < LeaderManager.MaxLeaders),
                        new EventOption(Loc.T("ПРИНЯТЬ ИНЖЕНЕРА"), Loc.T("Губернатор 2-го уровня с чертой «Инженер» поступает на службу бесплатно."), () =>
                        {
                            var l = Leaders?.RecruitFree(0, LeaderClass.Governor, 2, "gov_engineer");
                            if (l == null) Give(influence: 40f);
                        }, Loc.T("Есть место для лидера"), () => Leaders != null && Leaders.CountFor(0) < LeaderManager.MaxLeaders),
                        new EventOption(Loc.T("РАССЕЛИТЬ В КОЛОНИЯХ"), Loc.T("+40 влияния: спасённые станут живой легендой."), () => Give(influence: 40f))))
            });

            Add(new Template
            {
                Id = "crystal_field", When = Trigger.Survey,
                Build = c => Ev("crystal_field", Loc.T("ПОЮЩЕЕ КРИСТАЛЛИЧЕСКОЕ ПОЛЕ"),
                    Loc.T("Поверхность луны покрыта кристаллами в рост человека. Под звёздным ветром они резонируют на одной ноте, а их ") +
                    Loc.T("решётка растёт на глазах, поглощая пыль из окружающего пространства."),
                    AnomalyType.AlienFauna, Opts(
                        new EventOption(Loc.T("СОБРАТЬ УРОЖАЙ"), Loc.T("+150 титана."), () => Give(minerals: 150f)),
                        new EventOption(Loc.T("ИЗУЧИТЬ РЕЗОНАНС"), Loc.T("+100 науки."), () => { Science(100f); Xp(c, 20f); }),
                        new EventOption(Loc.T("ВЫРАЩИВАТЬ КРИСТАЛЛЫ"), Loc.T("+4 титана в месяц навсегда."), () =>
                        {
                            Monthly(minerals: 4f); Xp(c, 30f);
                        }, Loc.F("Учёный-{0}", TraitName("sci_curious").ToLower()), () => LeaderHas(c, "sci_curious"))))
            });

            Add(new Template
            {
                Id = "ghost_signal", When = Trigger.Survey, Weight = 0.7f,
                Build = c => Ev("ghost_signal", Loc.T("ГОЛОС ИЗ ПУСТОТЫ"),
                    Loc.T("Приёмники улавливают повторяющийся сигнал из точки, где ничего нет. Ни звезды, ни планеты, ни корабля — ") +
                    Loc.T("только сигнал, который становится громче, когда корабль разворачивается к нему кормой."),
                    AnomalyType.SpatialRift, Opts(
                        new EventOption(Loc.T("ИДТИ НА СИГНАЛ"), Loc.T("50%: +220 науки. 50%: корабль повреждён, учёный травмирован."), () =>
                        {
                            if (Random.value < 0.5f) { Science(220f); Xp(c, 40f); }
                            else
                            {
                                DamageShip(c?.Ship, 0.5f);
                                if (c?.Leader != null) Leaders?.GiveTrait(c.Leader, "sci_scarred");
                            }
                        }),
                        new EventOption(Loc.T("ЗАПИСАТЬ И УЙТИ"), Loc.T("+50 науки. Безопасно."), () => Science(50f)),
                        new EventOption(Loc.T("ВЫЧИСЛИТЬ ИСТОЧНИК"), Loc.T("Без риска: +220 науки, учёный может стать «Гением»."), () =>
                        {
                            Science(220f); Xp(c, 50f);
                            if (c?.Leader != null && Random.value < 0.5f) Leaders?.GiveTrait(c.Leader, "sci_brilliant");
                        }, Loc.F("Учёный-{0}", TraitName("sci_cartographer").ToLower()), () => LeaderHas(c, "sci_cartographer"))))
            });

            Add(new Template
            {
                Id = "dormant_probe", When = Trigger.Survey,
                Build = c => Ev("dormant_probe", Loc.T("СПЯЩИЙ ЗОНД"),
                    Loc.T("На орбите дремлет автоматический зонд чужой постройки. Его антенны развёрнуты в сторону галактического ядра, ") +
                    Loc.T("а память, судя по энергопотреблению, до сих пор пишет данные."),
                    AnomalyType.PrecursorRelic, Opts(
                        new EventOption(Loc.T("ПЕРЕПРОГРАММИРОВАТЬ"), Loc.T("Исследования +15% на 90 дней."), () =>
                        {
                            TechnologyManager.Instance?.ApplyTempResearchBoost(1.15f, 90); Xp(c, 20f);
                        }),
                        new EventOption(Loc.T("РАЗОБРАТЬ"), Loc.T("+100 сплавов."), () => Give(alloys: 100f)),
                        new EventOption(Loc.T("ПОДКЛЮЧИТЬСЯ К СЕТИ ЗОНДОВ"), Loc.T("+3 науки в месяц навсегда."), () =>
                        {
                            TechnologyManager.Instance?.AddMonthlyScience(3f); Xp(c, 40f);
                        }, Loc.F("Учёный-{0}", TraitName("sci_analyst").ToLower()), () => LeaderHas(c, "sci_analyst"))))
            });
        }

        // -------------------- Лидеры --------------------

        private static void AddLeaderEvents()
        {
            Add(new Template
            {
                Id = "leader_breakthrough", When = Trigger.Monthly,
                FindContext = () => LeaderCtx(l => l.Class == LeaderClass.Scientist && l.Post != LeaderPost.None && l.Level >= 2),
                Build = c => Ev("leader_breakthrough", Loc.T("ОЗАРЕНИЕ"),
                    Loc.F("Лаборатория, которой руководит {0}, после трёх бессонных суток представила теорию, объясняющую ", c.Leader.Name) +
                    Loc.T("половину наших нерешённых задач. Коллеги спорят, публиковать ли её сразу или придержать для оборонных проектов."),
                    AnomalyType.LeaderStory, Opts(
                        new EventOption(Loc.T("ОПУБЛИКОВАТЬ"), Loc.T("+120 науки, +15 влияния."), () => { Science(120f); Give(influence: 15f); Xp(c, 30f); }),
                        new EventOption(Loc.T("ЗАСЕКРЕТИТЬ"), Loc.T("Учёный получает много опыта (+80)."), () => Xp(c, 80f)),
                        new EventOption(Loc.T("СИСТЕМАТИЗИРОВАТЬ"), Loc.T("+220 науки, учёный становится «Гением»."), () =>
                        {
                            Science(220f); Xp(c, 40f); Leaders?.GiveTrait(c.Leader, "sci_brilliant");
                        }, Loc.F("Черта «{0}»", TraitName("sci_analyst")), () => LeaderHas(c, "sci_analyst"))))
            });

            Add(new Template
            {
                Id = "admiral_drills", When = Trigger.Monthly,
                FindContext = () => LeaderCtx(l => l.Class == LeaderClass.Admiral && l.Post == LeaderPost.Ship),
                Build = c => Ev("admiral_drills", Loc.T("МАНЁВРЫ ФЛОТА"),
                    Loc.F("Адмирал {0} просит средства на большие учения: боевые стрельбы, отработку выхода из боя и ", c.Leader.Name) +
                    Loc.T("слаженности экипажей. Штаб считает, что флот засиделся на орбитах."),
                    AnomalyType.MilitaryIncident, Opts(
                        new EventOption(Loc.T("ВЫДЕЛИТЬ СРЕДСТВА"), Loc.T("−60 гелия-3. Адмирал получает +90 опыта."), () =>
                        {
                            Pay(energy: 60f); Xp(c, 90f);
                        }, Need(energy: 60f), () => CanPay(energy: 60f)),
                        new EventOption(Loc.T("ОТКАЗАТЬ"), Loc.T("Ничего не происходит."), () => { }),
                        new EventOption(Loc.T("ОТРАБОТАТЬ НОВУЮ ТАКТИКУ"), Loc.T("−60 гелия-3. Адмирал становится «Ветераном»."), () =>
                        {
                            Pay(energy: 60f); Xp(c, 50f); Leaders?.GiveTrait(c.Leader, "adm_veteran");
                        }, Loc.F("Черта «{0}», 60 гелия-3", TraitName("adm_tactician")), () => LeaderHas(c, "adm_tactician") && CanPay(energy: 60f)),
                        new EventOption(Loc.T("БОЕВЫЕ СТРЕЛЬБЫ БЕЗ ОГРАНИЧЕНИЙ"), Loc.T("Бесплатно, но рискованно: адмирал становится «Безрассудным», флагман повреждён."), () =>
                        {
                            Xp(c, 60f); Leaders?.GiveTrait(c.Leader, "adm_reckless"); DamageShip(c.Ship, 0.3f);
                        }, Loc.F("Черта «{0}»", TraitName("adm_aggressive")), () => LeaderHas(c, "adm_aggressive"))))
            });

            Add(new Template
            {
                Id = "governor_corruption", When = Trigger.Monthly, Weight = 0.8f,
                FindContext = () => LeaderCtx(l => l.Class == LeaderClass.Governor && l.Post == LeaderPost.Planet && !l.Has("gov_corrupt") && !l.Has("gov_honest")),
                Build = c => Ev("governor_corruption", Loc.T("СЛУХИ О ВЗЯТКАХ"),
                    Loc.F("На {0} говорят, что подряды на строительство достаются только «своим». Губернатор ", c.Planet?.Name ?? Loc.T("колонии")) +
                    Loc.F("{0} всё отрицает, но счета колонии подозрительно расходятся с отчётами.", c.Leader.Name),
                    AnomalyType.LeaderStory, Opts(
                        new EventOption(Loc.T("ПРОВЕСТИ РАССЛЕДОВАНИЕ"), Loc.T("−25 влияния. Губернатор очищен от подозрений и становится «Неподкупным»."), () =>
                        {
                            Pay(influence: 25f); Leaders?.GiveTrait(c.Leader, "gov_honest");
                        }, Need(influence: 25f), () => CanPay(influence: 25f)),
                        new EventOption(Loc.T("ЗАКРЫТЬ ГЛАЗА"), Loc.T("+70 гелия-3 «благодарности», но губернатор становится «Коррупционером»."), () =>
                        {
                            Give(energy: 70f); Leaders?.GiveTrait(c.Leader, "gov_corrupt");
                        }),
                        new EventOption(Loc.T("ВНЕЗАПНЫЙ АУДИТ"), Loc.T("+60 гелия-3 возвращено в казну, губернатор получает опыт и становится «Неподкупным»."), () =>
                        {
                            Give(energy: 60f); Xp(c, 40f); Leaders?.GiveTrait(c.Leader, "gov_honest");
                        }, Loc.F("Черта «{0}»", TraitName("gov_admin")), () => LeaderHas(c, "gov_admin"))))
            });

            Add(new Template
            {
                Id = "leader_aging", When = Trigger.Monthly, Weight = 0.7f,
                FindContext = () => LeaderCtx(l => l.Lifespan - l.Age <= 4 && l.Level >= 2),
                Build = c => Ev("leader_aging", Loc.T("ПОСЛЕДНИЕ ГОДЫ СЛУЖБЫ"),
                    Loc.F("{0} {1} ({2} {3}) всё чаще пропускает советы. Врачи ", LeaderManager.ClassName(c.Leader.Class), c.Leader.Name, c.Leader.Age, Years(c.Leader.Age)) +
                    Loc.T("дают не больше нескольких лет, но есть экспериментальная терапия, продлевающая жизнь."),
                    AnomalyType.LeaderStory, Opts(
                        new EventOption(Loc.T("ПОЧЁТНАЯ ОТСТАВКА"), Loc.T("+40 влияния, лидер уходит на покой."), () =>
                        {
                            Give(influence: 40f);
                            Leaders?.Retire(c.Leader, Loc.T("Почётная отставка"), Loc.F("{0} с почестями покинул службу", c.Leader.Name));
                        }),
                        new EventOption(Loc.T("ЭКСПЕРИМЕНТАЛЬНАЯ ТЕРАПИЯ"), Loc.T("−150 гелия-3: +8 лет жизни."), () =>
                        {
                            Pay(energy: 150f); c.Leader.Lifespan += 8;
                        }, Need(energy: 150f), () => CanPay(energy: 150f)),
                        new EventOption(Loc.T("ПУСТЬ СЛУЖИТ ДО КОНЦА"), Loc.T("Ничего не меняется."), () => { })))
            });

            Add(new Template
            {
                Id = "rival_defector", When = Trigger.Monthly, Weight = 0.5f,
                FindContext = () => Leaders != null && Leaders.CountFor(0) < LeaderManager.MaxLeaders ? RivalCtx(false) : null,
                Build = c => Ev("rival_defector", Loc.T("ПЕРЕБЕЖЧИК"),
                    Loc.F("Ведущий учёный империи {0} просит убежища через наше посольство и готов работать на нас. ", RivalName(c.Rival)) +
                    Loc.T("Но такое бегство станет пощёчиной соседям."),
                    AnomalyType.Diplomatic, Opts(
                        new EventOption(Loc.T("ПРИНЯТЬ"), Loc.T("Учёный 3-го уровня поступает на службу бесплатно. Отношения с соседом ухудшатся (−15)."), () =>
                        {
                            Leaders?.RecruitFree(0, LeaderClass.Scientist, 3, null);
                            Opinion(c.Rival, -15f);
                        }),
                        new EventOption(Loc.T("ВЫДАТЬ СОСЕДЯМ"), Loc.T("Отношения с соседом улучшатся (+12)."), () => Opinion(c.Rival, 12f)),
                        new EventOption(Loc.T("ОТКАЗАТЬ ТИХО"), Loc.T("Ничего не происходит."), () => { })))
            });
        }

        // -------------------- Колонии --------------------

        private static void AddColonyEvents()
        {
            Add(new Template
            {
                Id = "colony_festival", When = Trigger.Monthly,
                FindContext = () => ColonyCtx(3),
                Build = c => Ev("colony_festival", Loc.T("ПРАЗДНИК ПЕРВОЙ ВЫСАДКИ"),
                    Loc.F("Жители {0} отмечают годовщину высадки первых колонистов. Совет колонии просит средств на ", c.Planet.Name) +
                    Loc.T("праздник, который запомнится на поколения."),
                    AnomalyType.ColonyIncident, Opts(
                        new EventOption(Loc.T("ЩЕДРЫЙ ПРАЗДНИК"), Loc.T("−50 гелия-3: +1 население (если есть жильё), +15 влияния."), () =>
                        {
                            Pay(energy: 50f); Give(influence: 15f);
                            if (c.Planet.Population < c.Planet.HousingCapacity) c.Planet.Population++;
                            Recalc();
                        }, Need(energy: 50f), () => CanPay(energy: 50f)),
                        new EventOption(Loc.T("СКРОМНОЕ ТОРЖЕСТВО"), Loc.T("+10 влияния."), () => Give(influence: 10f))))
            });

            Add(new Template
            {
                Id = "colony_quake", When = Trigger.Monthly, Weight = 0.8f,
                FindContext = () =>
                {
                    var c = ColonyCtx(1);
                    return c != null && c.Planet.Districts.Count >= 2 ? c : null;
                },
                Build = c => Ev("colony_quake", Loc.T("ТЕКТОНИЧЕСКИЙ СДВИГ"),
                    Loc.F("Серия толчков прошла по коре {0}. Один из районов треснул по фундаменту: если не укрепить его ", c.Planet.Name) +
                    Loc.T("сейчас, его придётся снести."),
                    AnomalyType.ColonyIncident, Opts(
                        new EventOption(Loc.T("УКРЕПИТЬ"), Loc.T("−80 титана, район спасён."), () => Pay(minerals: 80f), Need(minerals: 80f), () => CanPay(minerals: 80f)),
                        new EventOption(Loc.T("СНЕСТИ"), Loc.T("Колония теряет один район."), () =>
                        {
                            if (c.Planet.Districts.Count > 0) c.Planet.Districts.RemoveAt(c.Planet.Districts.Count - 1);
                            Recalc();
                        }),
                        new EventOption(Loc.T("СЕЙСМОСТОЙКАЯ ПЕРЕСТРОЙКА"), Loc.T("Бесплатно: район спасён, губернатор получает опыт."), () => Xp(c, 40f),
                            Loc.F("Губернатор-{0}", TraitName("gov_engineer").ToLower()), () => LeaderHas(c, "gov_engineer"))))
            });

            Add(new Template
            {
                Id = "colony_plague", When = Trigger.Monthly, Weight = 0.6f,
                FindContext = () => ColonyCtx(4),
                Build = c => Ev("colony_plague", Loc.T("НЕИЗВЕСТНАЯ ЛИХОРАДКА"),
                    Loc.F("В госпиталях {0} — вспышка болезни, которой нет в медицинских базах. Возбудитель, похоже, ", c.Planet.Name) +
                    Loc.T("местный: он годами ждал в почве, пока колонисты не начали глубокое бурение."),
                    AnomalyType.ColonyIncident, Opts(
                        new EventOption(Loc.T("КАРАНТИН"), Loc.T("Колония теряет 1 население."), () =>
                        {
                            c.Planet.Population = Mathf.Max(1, c.Planet.Population - 1); Recalc();
                        }),
                        new EventOption(Loc.T("ИСКАТЬ ЛЕКАРСТВО"), Loc.T("−100 гелия-3, +80 науки."), () =>
                        {
                            Pay(energy: 100f); Science(80f);
                        }, Need(energy: 100f), () => CanPay(energy: 100f)),
                        new EventOption(Loc.T("ВЫЗВАТЬ КСЕНОЛОГА"), Loc.T("Бесплатно: болезнь побеждена, +120 науки."), () =>
                        {
                            Science(120f);
                            var sci = Leaders?.CouncilScientist(0);
                            if (sci != null) Leaders.GrantXp(sci, 40f);
                        }, Loc.F("Учёный-{0} в научном совете", TraitName("sci_xeno").ToLower()), () =>
                        {
                            var sci = Leaders?.CouncilScientist(0);
                            return sci != null && sci.Has("sci_xeno");
                        })))
            });
        }

        // -------------------- Флот --------------------

        private static void AddMilitaryEvents()
        {
            Add(new Template
            {
                Id = "crew_unrest", When = Trigger.Monthly, Weight = 0.7f,
                FindContext = WarshipCtx,
                Build = c => Ev("crew_unrest", Loc.T("РОПОТ В КУБРИКАХ"),
                    Loc.F("Экипаж {0} месяцами не видел увольнений. Офицеры докладывают о драках и саботаже: ", c.Ship.Name) +
                    Loc.T("ещё немного — и корабль придётся ставить в док."),
                    AnomalyType.MilitaryIncident, Opts(
                        new EventOption(Loc.T("ВЫПЛАТИТЬ ПРЕМИИ"), Loc.T("−40 гелия-3."), () => Pay(energy: 40f), Need(energy: 40f), () => CanPay(energy: 40f)),
                        new EventOption(Loc.T("ЖЁСТКАЯ ДИСЦИПЛИНА"), Loc.T("Бесплатно, но корабль повреждён в беспорядках."), () => DamageShip(c.Ship, 0.3f)),
                        new EventOption(Loc.T("АДМИРАЛ ГОВОРИТ С ЭКИПАЖЕМ"), Loc.T("Бесплатно: порядок восстановлен, адмирал получает опыт."), () => Xp(c, 40f),
                            Loc.T("Адмирал на борту"), () => c.Leader != null && c.Leader.Class == LeaderClass.Admiral)))
            });

            Add(new Template
            {
                Id = "salvage_wreck", When = Trigger.Monthly, Weight = 0.6f,
                FindContext = WarshipCtx,
                Build = c => Ev("salvage_wreck", Loc.T("ОБЛОМКИ СТАРОЙ БИТВЫ"),
                    Loc.F("Патруль {0} наткнулся на поле обломков времён забытой войны. Среди корпусов — целые оружейные ", c.Ship.Name) +
                    Loc.T("модули, но старые мины по-прежнему активны."),
                    AnomalyType.MilitaryIncident, Opts(
                        new EventOption(Loc.T("ОСТОРОЖНЫЙ СБОР"), Loc.T("+80 сплавов."), () => Give(alloys: 80f)),
                        new EventOption(Loc.T("ВЗЯТЬ ВСЁ"), Loc.T("+180 сплавов, но корабль подрывается на мине."), () =>
                        {
                            Give(alloys: 180f); DamageShip(c.Ship, 0.45f);
                        }),
                        new EventOption(Loc.T("РАЗМИНИРОВАТЬ ПО ВСЕМ ПРАВИЛАМ"), Loc.T("+180 сплавов без потерь, адмирал получает опыт."), () =>
                        {
                            Give(alloys: 180f); Xp(c, 30f);
                        }, Loc.F("Адмирал-«{0}»", TraitName("adm_cautious")), () => LeaderHas(c, "adm_cautious"))))
            });
        }

        // -------------------- Дипломатия --------------------

        private static void AddDiplomaticEvents()
        {
            Add(new Template
            {
                Id = "border_incident", When = Trigger.Monthly, Weight = 0.8f,
                FindContext = () => RivalCtx(true, true),
                Build = c => Ev("border_incident", Loc.T("ПОГРАНИЧНЫЙ ИНЦИДЕНТ"),
                    Loc.F("Патрульный корвет империи {0} открыл предупредительный огонь по нашему транспорту у самой ", RivalName(c.Rival)) +
                    Loc.T("границы. Никто не пострадал, но в эфире уже требуют ответа."),
                    AnomalyType.Diplomatic, Opts(
                        new EventOption(Loc.T("ПРИНЯТЬ ИЗВИНЕНИЯ"), Loc.T("Отношения +10."), () => Opinion(c.Rival, 10f)),
                        new EventOption(Loc.T("ПОТРЕБОВАТЬ КОМПЕНСАЦИЮ"), Loc.T("+60 гелия-3, отношения −10."), () =>
                        {
                            Give(energy: 60f); Opinion(c.Rival, -10f);
                        }),
                        new EventOption(Loc.T("ДЕМОНСТРАЦИЯ СИЛЫ"), Loc.T("+30 влияния, отношения −15. Нужен флот сильнее соседского."), () =>
                        {
                            Give(influence: 30f); Opinion(c.Rival, -15f);
                        }, Loc.T("Флот сильнее соседского"), () => EmpireStats.MilitaryPower(0) > EmpireStats.MilitaryPower(c.Rival))))
            });

            Add(new Template
            {
                Id = "trade_delegation", When = Trigger.Monthly, Weight = 0.7f,
                FindContext = () => RivalCtx(true),
                Build = c => Ev("trade_delegation", Loc.T("ТОРГОВАЯ ДЕЛЕГАЦИЯ"),
                    Loc.F("Купцы империи {0} прибыли с предложением: открыть рынки колоний для их грузов в обмен ", RivalName(c.Rival)) +
                    Loc.T("на пошлины. Местные производители недовольны."),
                    AnomalyType.Diplomatic, Opts(
                        new EventOption(Loc.T("ОТКРЫТЬ РЫНКИ"), Loc.T("+90 гелия-3, отношения +8."), () => { Give(energy: 90f); Opinion(c.Rival, 8f); }),
                        new EventOption(Loc.T("ВЫСОКИЕ ПОШЛИНЫ"), Loc.T("+40 гелия-3, +40 титана, отношения −4."), () =>
                        {
                            Give(energy: 40f, minerals: 40f); Opinion(c.Rival, -4f);
                        }),
                        new EventOption(Loc.T("ОТКАЗАТЬ"), Loc.T("Отношения −6."), () => Opinion(c.Rival, -6f))))
            });
        }
    }
}
