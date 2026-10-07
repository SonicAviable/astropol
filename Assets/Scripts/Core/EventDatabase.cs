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
    public static class EventDatabase
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
            if (c.System != null) parts.Add($"система {c.System.Name}");
            if (c.Planet != null) parts.Add($"колония {c.Planet.Name}");
            if (c.Ship != null) parts.Add(c.Ship.Name);
            if (c.Leader != null) parts.Add($"{LeaderManager.ClassName(c.Leader.Class).ToLower()} {c.Leader.Name}, ур. {c.Leader.Level}");
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
            if (m >= 11 && m <= 14) return "лет";
            return d == 1 ? "год" : d >= 2 && d <= 4 ? "года" : "лет";
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

        /// <summary>Случайная империя ИИ, подходящая событию (needPeace — только те, с кем мир; border — только соседи).</summary>
        private static EventContext RivalCtx(bool needPeace, bool needBorder = false)
        {
            var list = new List<AIEmpireManager>();
            foreach (var ai in AIEmpireManager.Alive)
            {
                if (needPeace && ai.AtWar) continue;
                if (EmpireStats.SystemCount(ai.OwnerId) == 0) continue;
                if (needBorder && EmpireStats.SharedBorderCount(ai.OwnerId, 0) == 0) continue;
                list.Add(ai);
            }
            var pick = RandomOf(list);
            return pick != null ? new EventContext { Rival = pick.OwnerId } : null;
        }

        private static string RivalName(int owner) => AIEmpireManager.NameOf(owner, "соседняя империя");

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
        }

        // -------------------- Разведка --------------------

        private static void AddSurveyEvents()
        {
            Add(new Template
            {
                Id = "ancient_derelict_cruiser", When = Trigger.Survey,
                Build = c => Ev("ancient_derelict_cruiser", "ЗАБРОШЕННЫЙ ДРЕЙФУЮЩИЙ КРЕЙСЕР",
                    "Датчики научного судна засекли на высокой орбите объект класса «Крейсер». Корпус покрыт микрометеоритной эрозией " +
                    "возрастом около трёхсот лет. Бортовой транслятор молчит, но сигнатура реактора стабильна. Архивы в центральном блоке, " +
                    "вероятно, сохранились.",
                    AnomalyType.AncientDerelict, Opts(
                        new EventOption("РАЗОБРАТЬ НА МЕТАЛЛОЛОМ", "+120 сплавов.", () => Give(alloys: 120f)),
                        new EventOption("ИЗУЧИТЬ БОРТОВОЙ АРХИВ", "+80 науки. 30% — мгновенный прорыв случайной технологии.", () =>
                        {
                            Science(80f);
                            if (Random.value < 0.30f) TechnologyManager.Instance?.CompleteRandomTech();
                            Xp(c, 20f);
                        }),
                        new EventOption("ВОССТАНОВИТЬ РЕАКТОР", "50%: +240 сплавов и трофейный корвет. 50%: выброс — корабль повреждён, −50 гелия-3.", () =>
                        {
                            if (Random.value < 0.5f)
                            {
                                Give(alloys: 240f);
                                int sys = SpawnSystem(c);
                                if (sys >= 0) FleetManager.Instance?.CreateFleetObject($"Корвет-Призрак {FleetManager.Instance.AllFleets.Count + 1}", sys, FleetType.Military);
                                NotificationCenter.Show("Реактор ожил", "Крейсер разобран, из уцелевших модулей собран корвет", NotificationCenter.Kind.Success, 5f);
                            }
                            else
                            {
                                DamageShip(c?.Ship, 0.5f);
                                Give(energy: -50f);
                                NotificationCenter.Show("Выброс реактора", "Научное судно повреждено при попытке запуска", NotificationCenter.Kind.Danger, 5f);
                            }
                        }),
                        new EventOption("ПРОЧИТАТЬ ЧУЖУЮ СХЕМОТЕХНИКУ", "Без риска: +120 сплавов и +80 науки, учёный получает опыт.", () =>
                        {
                            Give(alloys: 120f); Science(80f); Xp(c, 40f);
                        }, $"Учёный-{TraitName("sci_xeno").ToLower()}", () => LeaderHas(c, "sci_xeno"))))
            });

            Add(new Template
            {
                Id = "alien_amoeba_belt", When = Trigger.Survey,
                Build = c => Ev("alien_amoeba_belt", "КОСМИЧЕСКАЯ АМЁБА В АСТЕРОИДНОМ ПОЯСЕ",
                    "В поясе астероидов обнаружена колония кремниевых амёбоподобных форм. Они поглощают силикаты и выделяют в метаболические " +
                    "пузырьки чистый гелий-3. Нейросканеры показывают реакцию на фононные колебания — возможно, колония способна к обучению.",
                    AnomalyType.AlienFauna, Opts(
                        new EventOption("ОСТАВИТЬ В ПОКОЕ И НАБЛЮДАТЬ", "+2 науки в месяц навсегда.", () =>
                        {
                            TechnologyManager.Instance?.AddMonthlyScience(2f); Xp(c, 15f);
                        }),
                        new EventOption("УНИЧТОЖИТЬ И СОБРАТЬ БИОМАТЕРИАЛ", "+90 гелия-3 разово.", () => Give(energy: 90f)),
                        new EventOption("ПРИРУЧИТЬ КОЛОНИЮ", "+3 гелия-3 в месяц навсегда, учёный получает опыт.", () =>
                        {
                            Monthly(energy: 3f); Xp(c, 40f);
                        }, $"Учёный-{TraitName("sci_xeno").ToLower()}", () => LeaderHas(c, "sci_xeno"))))
            });

            Add(new Template
            {
                Id = "precursor_relic", When = Trigger.Survey, Weight = 0.8f,
                Build = c => Ev("precursor_relic", "РЕЛИКТ ПРЕДШЕСТВЕННИКОВ",
                    "На экваториальной плите третьей планеты обнаружен монолит неизвестного сплава. Возраст — 127 миллионов лет. " +
                    "На поверхности 14 тысяч глифов, похожих на протоматематические уравнения.",
                    AnomalyType.PrecursorRelic, Opts(
                        new EventOption("ДЕШИФРОВАТЬ ГЛИФЫ", "+150 науки, +1 влияния в месяц навсегда.", () =>
                        {
                            Science(150f); Monthly(influence: 1f); Xp(c, 25f);
                        }),
                        new EventOption("ИЗВЛЕЧЬ МОНОЛИТ НА БАЗУ", "+200 сплавов за сверхпрочную оболочку.", () => Give(alloys: 200f)),
                        new EventOption("ПОЛНАЯ РАСШИФРОВКА", "+250 науки, +1 влияния в месяц и прорыв случайной технологии.", () =>
                        {
                            Science(250f); Monthly(influence: 1f); TechnologyManager.Instance?.CompleteRandomTech(); Xp(c, 50f);
                        }, $"Учёный-{TraitName("sci_analyst").ToLower()}", () => LeaderHas(c, "sci_analyst"))))
            });

            Add(new Template
            {
                Id = "spatial_rift", When = Trigger.Survey, Weight = 0.8f,
                Build = c => Ev("spatial_rift", "ПРОСТРАНСТВЕННЫЙ РАЗЛОМ «ТИФОН»",
                    "На окраине системы возникла субпространственная аномалия. Через разлом идут слабые гравитационные волны, " +
                    "синхронизированные с неизвестным пульсаром в полутора килопарсеках.",
                    AnomalyType.SpatialRift, Opts(
                        new EventOption("ЗОНДИРОВАТЬ РАЗЛОМ", "60%: +40 влияния. 40%: корабль повреждён, −50 гелия-3.", () =>
                        {
                            if (Random.value < 0.6f) Give(influence: 40f);
                            else { Give(energy: -50f); DamageShip(c?.Ship, 0.4f); }
                            Xp(c, 20f);
                        }),
                        new EventOption("ВЕРНУТЬСЯ С ДАННЫМИ", "+60 науки. Безопасно.", () => Science(60f)),
                        new EventOption("НАНЕСТИ РАЗЛОМ НА КАРТЫ", "+100 науки. Через пару месяцев разлом может стабилизироваться — продолжение следует.", () =>
                        {
                            Science(100f); Xp(c, 30f);
                            Schedule("rift_stabilized", 60, c);
                        }, $"Учёный-{TraitName("sci_cartographer").ToLower()}", () => LeaderHas(c, "sci_cartographer"))))
            });

            Add(new Template
            {
                Id = "rift_stabilized", When = Trigger.Chain,
                Build = c => Ev("rift_stabilized", "РАЗЛОМ СТАБИЛИЗИРОВАЛСЯ",
                    "Карты разлома «Тифон» оказались точнее, чем мы надеялись: аномалия схлопнулась в устойчивый канал. Через него " +
                    "можно гнать зонды в соседние области пространства или превратить канал в источник энергии.",
                    AnomalyType.SpatialRift, Opts(
                        new EventOption("НАУЧНАЯ ПРОГРАММА", "Исследования +25% на 120 дней.", () => TechnologyManager.Instance?.ApplyTempResearchBoost(1.25f, 120)),
                        new EventOption("ЭНЕРГОСТАНЦИЯ НА КАНАЛЕ", "+4 гелия-3 в месяц навсегда.", () => Monthly(energy: 4f))))
            });

            Add(new Template
            {
                Id = "distress_signal", When = Trigger.Survey,
                Build = c => Ev("distress_signal", "ЗАШИФРОВАННЫЙ СИГНАЛ БЕДСТВИЯ",
                    "В дециметровом диапазоне принят сигнал бедствия. Источник — обломок транспорта с радиационным шлейфом. " +
                    "Бортовой самописец, вероятно, содержит навигационные карты ближних систем, а в спасательных капсулах может быть кто-то живой.",
                    AnomalyType.DistressSignal, Opts(
                        new EventOption("СПАСАТЕЛЬНАЯ ОПЕРАЦИЯ", "+30 влияния, +60 минералов, +60 гелия-3.", () => Give(energy: 60f, minerals: 60f, influence: 30f)),
                        new EventOption("БЕСКОНТАКТНОЕ СКАНИРОВАНИЕ", "+60 науки и исследования +50% на 30 дней.", () =>
                        {
                            Science(60f); TechnologyManager.Instance?.ApplyTempResearchBoost(1.5f, 30);
                        }),
                        new EventOption("ВСКРЫТЬ КАПСУЛЫ", "Опытный учёный успеет откачать выживших. Продолжение следует.", () =>
                        {
                            Xp(c, 30f);
                            Schedule("distress_survivors", 25, c);
                        }, "Учёный 2-го уровня и выше", () => c?.Leader != null && c.Leader.Level >= 2)))
            });

            Add(new Template
            {
                Id = "distress_survivors", When = Trigger.Chain,
                Build = c => Ev("distress_survivors", "ВЫЖИВШИЕ С «ПОЛЯРИСА»",
                    "Из капсул извлекли одиннадцать человек в криосне. Среди них — капитан транспорта, опытный навигатор, и инженер " +
                    "орбитальных верфей. Оба готовы служить приютившей их империи.",
                    AnomalyType.DistressSignal, Opts(
                        new EventOption("ПРИНЯТЬ КАПИТАНА", "Адмирал 2-го уровня с чертой «Навигатор» поступает на службу бесплатно.", () =>
                        {
                            var l = Leaders?.RecruitFree(0, LeaderClass.Admiral, 2, "adm_navigator");
                            if (l == null) Give(influence: 40f);
                        }, "Есть место для лидера", () => Leaders != null && Leaders.CountFor(0) < LeaderManager.MaxLeaders),
                        new EventOption("ПРИНЯТЬ ИНЖЕНЕРА", "Губернатор 2-го уровня с чертой «Инженер» поступает на службу бесплатно.", () =>
                        {
                            var l = Leaders?.RecruitFree(0, LeaderClass.Governor, 2, "gov_engineer");
                            if (l == null) Give(influence: 40f);
                        }, "Есть место для лидера", () => Leaders != null && Leaders.CountFor(0) < LeaderManager.MaxLeaders),
                        new EventOption("РАССЕЛИТЬ В КОЛОНИЯХ", "+40 влияния: спасённые станут живой легендой.", () => Give(influence: 40f))))
            });

            Add(new Template
            {
                Id = "crystal_field", When = Trigger.Survey,
                Build = c => Ev("crystal_field", "ПОЮЩЕЕ КРИСТАЛЛИЧЕСКОЕ ПОЛЕ",
                    "Поверхность луны покрыта кристаллами в рост человека. Под звёздным ветром они резонируют на одной ноте, а их " +
                    "решётка растёт на глазах, поглощая пыль из окружающего пространства.",
                    AnomalyType.AlienFauna, Opts(
                        new EventOption("СОБРАТЬ УРОЖАЙ", "+150 минералов.", () => Give(minerals: 150f)),
                        new EventOption("ИЗУЧИТЬ РЕЗОНАНС", "+100 науки.", () => { Science(100f); Xp(c, 20f); }),
                        new EventOption("ВЫРАЩИВАТЬ КРИСТАЛЛЫ", "+4 минерала в месяц навсегда.", () =>
                        {
                            Monthly(minerals: 4f); Xp(c, 30f);
                        }, $"Учёный-{TraitName("sci_curious").ToLower()}", () => LeaderHas(c, "sci_curious"))))
            });

            Add(new Template
            {
                Id = "ghost_signal", When = Trigger.Survey, Weight = 0.7f,
                Build = c => Ev("ghost_signal", "ГОЛОС ИЗ ПУСТОТЫ",
                    "Приёмники улавливают повторяющийся сигнал из точки, где ничего нет. Ни звезды, ни планеты, ни корабля — " +
                    "только сигнал, который становится громче, когда корабль разворачивается к нему кормой.",
                    AnomalyType.SpatialRift, Opts(
                        new EventOption("ИДТИ НА СИГНАЛ", "50%: +220 науки. 50%: корабль повреждён, учёный травмирован.", () =>
                        {
                            if (Random.value < 0.5f) { Science(220f); Xp(c, 40f); }
                            else
                            {
                                DamageShip(c?.Ship, 0.5f);
                                if (c?.Leader != null) Leaders?.GiveTrait(c.Leader, "sci_scarred");
                            }
                        }),
                        new EventOption("ЗАПИСАТЬ И УЙТИ", "+50 науки. Безопасно.", () => Science(50f)),
                        new EventOption("ВЫЧИСЛИТЬ ИСТОЧНИК", "Без риска: +220 науки, учёный может стать «Гением».", () =>
                        {
                            Science(220f); Xp(c, 50f);
                            if (c?.Leader != null && Random.value < 0.5f) Leaders?.GiveTrait(c.Leader, "sci_brilliant");
                        }, $"Учёный-{TraitName("sci_cartographer").ToLower()}", () => LeaderHas(c, "sci_cartographer"))))
            });

            Add(new Template
            {
                Id = "dormant_probe", When = Trigger.Survey,
                Build = c => Ev("dormant_probe", "СПЯЩИЙ ЗОНД",
                    "На орбите дремлет автоматический зонд чужой постройки. Его антенны развёрнуты в сторону галактического ядра, " +
                    "а память, судя по энергопотреблению, до сих пор пишет данные.",
                    AnomalyType.PrecursorRelic, Opts(
                        new EventOption("ПЕРЕПРОГРАММИРОВАТЬ", "Исследования +15% на 90 дней.", () =>
                        {
                            TechnologyManager.Instance?.ApplyTempResearchBoost(1.15f, 90); Xp(c, 20f);
                        }),
                        new EventOption("РАЗОБРАТЬ", "+100 сплавов.", () => Give(alloys: 100f)),
                        new EventOption("ПОДКЛЮЧИТЬСЯ К СЕТИ ЗОНДОВ", "+3 науки в месяц навсегда.", () =>
                        {
                            TechnologyManager.Instance?.AddMonthlyScience(3f); Xp(c, 40f);
                        }, $"Учёный-{TraitName("sci_analyst").ToLower()}", () => LeaderHas(c, "sci_analyst"))))
            });
        }

        // -------------------- Лидеры --------------------

        private static void AddLeaderEvents()
        {
            Add(new Template
            {
                Id = "leader_breakthrough", When = Trigger.Monthly,
                FindContext = () => LeaderCtx(l => l.Class == LeaderClass.Scientist && l.Post != LeaderPost.None && l.Level >= 2),
                Build = c => Ev("leader_breakthrough", "ОЗАРЕНИЕ",
                    $"Лаборатория, которой руководит {c.Leader.Name}, после трёх бессонных суток представила теорию, объясняющую " +
                    "половину наших нерешённых задач. Коллеги спорят, публиковать ли её сразу или придержать для оборонных проектов.",
                    AnomalyType.LeaderStory, Opts(
                        new EventOption("ОПУБЛИКОВАТЬ", "+120 науки, +15 влияния.", () => { Science(120f); Give(influence: 15f); Xp(c, 30f); }),
                        new EventOption("ЗАСЕКРЕТИТЬ", "Учёный получает много опыта (+80).", () => Xp(c, 80f)),
                        new EventOption("СИСТЕМАТИЗИРОВАТЬ", "+220 науки, учёный становится «Гением».", () =>
                        {
                            Science(220f); Xp(c, 40f); Leaders?.GiveTrait(c.Leader, "sci_brilliant");
                        }, $"Черта «{TraitName("sci_analyst")}»", () => LeaderHas(c, "sci_analyst"))))
            });

            Add(new Template
            {
                Id = "admiral_drills", When = Trigger.Monthly,
                FindContext = () => LeaderCtx(l => l.Class == LeaderClass.Admiral && l.Post == LeaderPost.Ship),
                Build = c => Ev("admiral_drills", "МАНЁВРЫ ФЛОТА",
                    $"Адмирал {c.Leader.Name} просит средства на большие учения: боевые стрельбы, отработку выхода из боя и " +
                    "слаженности экипажей. Штаб считает, что флот засиделся на орбитах.",
                    AnomalyType.MilitaryIncident, Opts(
                        new EventOption("ВЫДЕЛИТЬ СРЕДСТВА", "−60 гелия-3. Адмирал получает +90 опыта.", () =>
                        {
                            Pay(energy: 60f); Xp(c, 90f);
                        }, "Нужно 60 гелия-3", () => CanPay(energy: 60f)),
                        new EventOption("ОТКАЗАТЬ", "Ничего не происходит.", () => { }),
                        new EventOption("ОТРАБОТАТЬ НОВУЮ ТАКТИКУ", "−60 гелия-3. Адмирал становится «Ветераном».", () =>
                        {
                            Pay(energy: 60f); Xp(c, 50f); Leaders?.GiveTrait(c.Leader, "adm_veteran");
                        }, $"Черта «{TraitName("adm_tactician")}», 60 гелия-3", () => LeaderHas(c, "adm_tactician") && CanPay(energy: 60f)),
                        new EventOption("БОЕВЫЕ СТРЕЛЬБЫ БЕЗ ОГРАНИЧЕНИЙ", "Бесплатно, но рискованно: адмирал становится «Безрассудным», флагман повреждён.", () =>
                        {
                            Xp(c, 60f); Leaders?.GiveTrait(c.Leader, "adm_reckless"); DamageShip(c.Ship, 0.3f);
                        }, $"Черта «{TraitName("adm_aggressive")}»", () => LeaderHas(c, "adm_aggressive"))))
            });

            Add(new Template
            {
                Id = "governor_corruption", When = Trigger.Monthly, Weight = 0.8f,
                FindContext = () => LeaderCtx(l => l.Class == LeaderClass.Governor && l.Post == LeaderPost.Planet && !l.Has("gov_corrupt") && !l.Has("gov_honest")),
                Build = c => Ev("governor_corruption", "СЛУХИ О ВЗЯТКАХ",
                    $"На {c.Planet?.Name ?? "колонии"} говорят, что подряды на строительство достаются только «своим». Губернатор " +
                    $"{c.Leader.Name} всё отрицает, но счета колонии подозрительно расходятся с отчётами.",
                    AnomalyType.LeaderStory, Opts(
                        new EventOption("ПРОВЕСТИ РАССЛЕДОВАНИЕ", "−25 влияния. Губернатор очищен от подозрений и становится «Неподкупным».", () =>
                        {
                            Pay(influence: 25f); Leaders?.GiveTrait(c.Leader, "gov_honest");
                        }, "Нужно 25 влияния", () => CanPay(influence: 25f)),
                        new EventOption("ЗАКРЫТЬ ГЛАЗА", "+70 гелия-3 «благодарности», но губернатор становится «Коррупционером».", () =>
                        {
                            Give(energy: 70f); Leaders?.GiveTrait(c.Leader, "gov_corrupt");
                        }),
                        new EventOption("ВНЕЗАПНЫЙ АУДИТ", "+60 гелия-3 возвращено в казну, губернатор получает опыт и становится «Неподкупным».", () =>
                        {
                            Give(energy: 60f); Xp(c, 40f); Leaders?.GiveTrait(c.Leader, "gov_honest");
                        }, $"Черта «{TraitName("gov_admin")}»", () => LeaderHas(c, "gov_admin"))))
            });

            Add(new Template
            {
                Id = "leader_aging", When = Trigger.Monthly, Weight = 0.7f,
                FindContext = () => LeaderCtx(l => l.Lifespan - l.Age <= 4 && l.Level >= 2),
                Build = c => Ev("leader_aging", "ПОСЛЕДНИЕ ГОДЫ СЛУЖБЫ",
                    $"{LeaderManager.ClassName(c.Leader.Class)} {c.Leader.Name} ({c.Leader.Age} {Years(c.Leader.Age)}) всё чаще пропускает советы. Врачи " +
                    "дают не больше нескольких лет, но есть экспериментальная терапия, продлевающая жизнь.",
                    AnomalyType.LeaderStory, Opts(
                        new EventOption("ПОЧЁТНАЯ ОТСТАВКА", "+40 влияния, лидер уходит на покой.", () =>
                        {
                            Give(influence: 40f);
                            Leaders?.Retire(c.Leader, "Почётная отставка", $"{c.Leader.Name} с почестями покинул службу");
                        }),
                        new EventOption("ЭКСПЕРИМЕНТАЛЬНАЯ ТЕРАПИЯ", "−150 гелия-3: +8 лет жизни.", () =>
                        {
                            Pay(energy: 150f); c.Leader.Lifespan += 8;
                        }, "Нужно 150 гелия-3", () => CanPay(energy: 150f)),
                        new EventOption("ПУСТЬ СЛУЖИТ ДО КОНЦА", "Ничего не меняется.", () => { })))
            });

            Add(new Template
            {
                Id = "rival_defector", When = Trigger.Monthly, Weight = 0.5f,
                FindContext = () => Leaders != null && Leaders.CountFor(0) < LeaderManager.MaxLeaders ? RivalCtx(false) : null,
                Build = c => Ev("rival_defector", "ПЕРЕБЕЖЧИК",
                    $"Ведущий учёный империи {RivalName(c.Rival)} просит убежища через наше посольство и готов работать на нас. " +
                    "Но такое бегство станет пощёчиной соседям.",
                    AnomalyType.Diplomatic, Opts(
                        new EventOption("ПРИНЯТЬ", "Учёный 3-го уровня поступает на службу бесплатно. Отношения с соседом ухудшатся (−15).", () =>
                        {
                            Leaders?.RecruitFree(0, LeaderClass.Scientist, 3, null);
                            Opinion(c.Rival, -15f);
                        }),
                        new EventOption("ВЫДАТЬ СОСЕДЯМ", "Отношения с соседом улучшатся (+12).", () => Opinion(c.Rival, 12f)),
                        new EventOption("ОТКАЗАТЬ ТИХО", "Ничего не происходит.", () => { })))
            });
        }

        // -------------------- Колонии --------------------

        private static void AddColonyEvents()
        {
            Add(new Template
            {
                Id = "colony_festival", When = Trigger.Monthly,
                FindContext = () => ColonyCtx(3),
                Build = c => Ev("colony_festival", "ПРАЗДНИК ПЕРВОЙ ВЫСАДКИ",
                    $"Жители {c.Planet.Name} отмечают годовщину высадки первых колонистов. Совет колонии просит средств на " +
                    "праздник, который запомнится на поколения.",
                    AnomalyType.ColonyIncident, Opts(
                        new EventOption("ЩЕДРЫЙ ПРАЗДНИК", "−50 гелия-3: +1 население (если есть жильё), +15 влияния.", () =>
                        {
                            Pay(energy: 50f); Give(influence: 15f);
                            if (c.Planet.Population < c.Planet.HousingCapacity) c.Planet.Population++;
                            Recalc();
                        }, "Нужно 50 гелия-3", () => CanPay(energy: 50f)),
                        new EventOption("СКРОМНОЕ ТОРЖЕСТВО", "+10 влияния.", () => Give(influence: 10f))))
            });

            Add(new Template
            {
                Id = "colony_quake", When = Trigger.Monthly, Weight = 0.8f,
                FindContext = () =>
                {
                    var c = ColonyCtx(1);
                    return c != null && c.Planet.Districts.Count >= 2 ? c : null;
                },
                Build = c => Ev("colony_quake", "ТЕКТОНИЧЕСКИЙ СДВИГ",
                    $"Серия толчков прошла по коре {c.Planet.Name}. Один из районов треснул по фундаменту: если не укрепить его " +
                    "сейчас, его придётся снести.",
                    AnomalyType.ColonyIncident, Opts(
                        new EventOption("УКРЕПИТЬ", "−80 минералов, район спасён.", () => Pay(minerals: 80f), "Нужно 80 минералов", () => CanPay(minerals: 80f)),
                        new EventOption("СНЕСТИ", "Колония теряет один район.", () =>
                        {
                            if (c.Planet.Districts.Count > 0) c.Planet.Districts.RemoveAt(c.Planet.Districts.Count - 1);
                            Recalc();
                        }),
                        new EventOption("СЕЙСМОСТОЙКАЯ ПЕРЕСТРОЙКА", "Бесплатно: район спасён, губернатор получает опыт.", () => Xp(c, 40f),
                            $"Губернатор-{TraitName("gov_engineer").ToLower()}", () => LeaderHas(c, "gov_engineer"))))
            });

            Add(new Template
            {
                Id = "colony_plague", When = Trigger.Monthly, Weight = 0.6f,
                FindContext = () => ColonyCtx(4),
                Build = c => Ev("colony_plague", "НЕИЗВЕСТНАЯ ЛИХОРАДКА",
                    $"В госпиталях {c.Planet.Name} — вспышка болезни, которой нет в медицинских базах. Возбудитель, похоже, " +
                    "местный: он годами ждал в почве, пока колонисты не начали глубокое бурение.",
                    AnomalyType.ColonyIncident, Opts(
                        new EventOption("КАРАНТИН", "Колония теряет 1 население.", () =>
                        {
                            c.Planet.Population = Mathf.Max(1, c.Planet.Population - 1); Recalc();
                        }),
                        new EventOption("ИСКАТЬ ЛЕКАРСТВО", "−100 гелия-3, +80 науки.", () =>
                        {
                            Pay(energy: 100f); Science(80f);
                        }, "Нужно 100 гелия-3", () => CanPay(energy: 100f)),
                        new EventOption("ВЫЗВАТЬ КСЕНОЛОГА", "Бесплатно: болезнь побеждена, +120 науки.", () =>
                        {
                            Science(120f);
                            var sci = Leaders?.CouncilScientist(0);
                            if (sci != null) Leaders.GrantXp(sci, 40f);
                        }, $"Учёный-{TraitName("sci_xeno").ToLower()} в научном совете", () =>
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
                Build = c => Ev("crew_unrest", "РОПОТ В КУБРИКАХ",
                    $"Экипаж {c.Ship.Name} месяцами не видел увольнений. Офицеры докладывают о драках и саботаже: " +
                    "ещё немного — и корабль придётся ставить в док.",
                    AnomalyType.MilitaryIncident, Opts(
                        new EventOption("ВЫПЛАТИТЬ ПРЕМИИ", "−40 гелия-3.", () => Pay(energy: 40f), "Нужно 40 гелия-3", () => CanPay(energy: 40f)),
                        new EventOption("ЖЁСТКАЯ ДИСЦИПЛИНА", "Бесплатно, но корабль повреждён в беспорядках.", () => DamageShip(c.Ship, 0.3f)),
                        new EventOption("АДМИРАЛ ГОВОРИТ С ЭКИПАЖЕМ", "Бесплатно: порядок восстановлен, адмирал получает опыт.", () => Xp(c, 40f),
                            "Адмирал на борту", () => c.Leader != null && c.Leader.Class == LeaderClass.Admiral)))
            });

            Add(new Template
            {
                Id = "salvage_wreck", When = Trigger.Monthly, Weight = 0.6f,
                FindContext = WarshipCtx,
                Build = c => Ev("salvage_wreck", "ОБЛОМКИ СТАРОЙ БИТВЫ",
                    $"Патруль {c.Ship.Name} наткнулся на поле обломков времён забытой войны. Среди корпусов — целые оружейные " +
                    "модули, но старые мины по-прежнему активны.",
                    AnomalyType.MilitaryIncident, Opts(
                        new EventOption("ОСТОРОЖНЫЙ СБОР", "+80 сплавов.", () => Give(alloys: 80f)),
                        new EventOption("ВЗЯТЬ ВСЁ", "+180 сплавов, но корабль подрывается на мине.", () =>
                        {
                            Give(alloys: 180f); DamageShip(c.Ship, 0.45f);
                        }),
                        new EventOption("РАЗМИНИРОВАТЬ ПО ВСЕМ ПРАВИЛАМ", "+180 сплавов без потерь, адмирал получает опыт.", () =>
                        {
                            Give(alloys: 180f); Xp(c, 30f);
                        }, $"Адмирал-«{TraitName("adm_cautious")}»", () => LeaderHas(c, "adm_cautious"))))
            });
        }

        // -------------------- Дипломатия --------------------

        private static void AddDiplomaticEvents()
        {
            Add(new Template
            {
                Id = "border_incident", When = Trigger.Monthly, Weight = 0.8f,
                FindContext = () => RivalCtx(true, true),
                Build = c => Ev("border_incident", "ПОГРАНИЧНЫЙ ИНЦИДЕНТ",
                    $"Патрульный корвет империи {RivalName(c.Rival)} открыл предупредительный огонь по нашему транспорту у самой " +
                    "границы. Никто не пострадал, но в эфире уже требуют ответа.",
                    AnomalyType.Diplomatic, Opts(
                        new EventOption("ПРИНЯТЬ ИЗВИНЕНИЯ", "Отношения +10.", () => Opinion(c.Rival, 10f)),
                        new EventOption("ПОТРЕБОВАТЬ КОМПЕНСАЦИЮ", "+60 гелия-3, отношения −10.", () =>
                        {
                            Give(energy: 60f); Opinion(c.Rival, -10f);
                        }),
                        new EventOption("ДЕМОНСТРАЦИЯ СИЛЫ", "+30 влияния, отношения −15. Нужен флот сильнее соседского.", () =>
                        {
                            Give(influence: 30f); Opinion(c.Rival, -15f);
                        }, "Флот сильнее соседского", () => EmpireStats.MilitaryPower(0) > EmpireStats.MilitaryPower(c.Rival))))
            });

            Add(new Template
            {
                Id = "trade_delegation", When = Trigger.Monthly, Weight = 0.7f,
                FindContext = () => RivalCtx(true),
                Build = c => Ev("trade_delegation", "ТОРГОВАЯ ДЕЛЕГАЦИЯ",
                    $"Купцы империи {RivalName(c.Rival)} прибыли с предложением: открыть рынки колоний для их грузов в обмен " +
                    "на пошлины. Местные производители недовольны.",
                    AnomalyType.Diplomatic, Opts(
                        new EventOption("ОТКРЫТЬ РЫНКИ", "+90 гелия-3, отношения +8.", () => { Give(energy: 90f); Opinion(c.Rival, 8f); }),
                        new EventOption("ВЫСОКИЕ ПОШЛИНЫ", "+40 гелия-3, +40 минералов, отношения −4.", () =>
                        {
                            Give(energy: 40f, minerals: 40f); Opinion(c.Rival, -4f);
                        }),
                        new EventOption("ОТКАЗАТЬ", "Отношения −6.", () => Opinion(c.Rival, -6f))))
            });
        }
    }
}
