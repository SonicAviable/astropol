using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using Random = UnityEngine.Random;

namespace StellarisClone.Core
{
    /// <summary>
    /// Долгие цепочки событий — истории на несколько глав с последствиями через годы:
    ///   • «Сердце Тишины» — инертный артефакт просыпается через 2–3 года и зовёт тех, кто его оставил;
    ///   • «Ковчег „Надежда“» — корабль поколений идёт к нам пять лет, его потомки дают о себе знать позже;
    ///   • «Пробуждённый разум» — ИИ колонии осознаёт себя; решение аукнется через годы;
    ///   • «Кладка Пустоты» — яйца левиафанов: ручная стая, дикое стадо или месть матери.
    /// Каждое отложенное продолжение попадает в хронику империи (вкладка «Эффекты») с туманной подсказкой.
    /// Две цепочки могут дать ключ к чуду фракции (флаги wonder_key и wonder_mind, см. WonderManager).
    /// </summary>
    public static partial class EventDatabase
    {
        private static string ChainHeart => Loc.T("Сердце Тишины");
        private static string ChainArk => Loc.T("Ковчег «Надежда»");
        private static string ChainMind => Loc.T("Пробуждённый разум");
        private static string ChainBrood => Loc.T("Кладка Пустоты");

        /// <summary>Флаг цепочки для других систем (чудеса читают wonder_key и wonder_mind).</summary>
        public static bool HasChainFlag(string flag) => HasFlag(flag);

        // ==================== ПОМОЩНИКИ ЦЕПОЧЕК ====================

        private static GameEventData Ch(string id, string chain, int step, string title, string text, AnomalyType type, List<EventOption> opts)
        {
            var ev = Ev(id, title, text, type, opts);
            ev.Chain = chain;
            ev.Step = step;
            return ev;
        }

        /// <summary>Продолжение цепочки через days дней с подсказкой в хронике.</summary>
        private static void Later(string id, int days, EventContext c, string chain, string hint)
            => AnomalyEventSystem.Instance?.Schedule(id, days, c, chain, hint);

        private static string Planet(EventContext c) => c?.Planet != null ? c.Planet.Name : Loc.T("колонии");

        /// <summary>Самая населённая планета игрока (столица, если она своя).</summary>
        private static PlanetData CapitalPlanet()
        {
            PlanetData best = null;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != 0) continue;
                foreach (var p in s.Planets)
                    if (p.Population > 0 && (best == null || p.Population > best.Population
                        || (p.Population == best.Population && s.Id == EconomyManager.PlayerCapitalId)))
                        best = p;
            }
            return best;
        }

        /// <summary>Расселить n жителей по колониям игрока, где есть свободное жильё. Возвращает, сколько поселилось.</summary>
        private static int GrowPop(int n)
        {
            int placed = 0;
            for (int guard = 0; placed < n && guard < 64; guard++)
            {
                PlanetData pick = null;
                foreach (var s in EmpireStats.Systems)
                {
                    if (s.OwnerId != 0) continue;
                    foreach (var p in s.Planets)
                        if (p.Population > 0 && p.Population < p.HousingCapacity
                            && (pick == null || p.HousingCapacity - p.Population > pick.HousingCapacity - pick.Population))
                            pick = p;
                }
                if (pick == null) break;
                pick.Population++;
                placed++;
            }
            if (placed > 0) Recalc();
            return placed;
        }

        private static PlanetData BestColonySite()
        {
            PlanetData best = null;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != 0) continue;
                foreach (var p in s.Planets)
                    if (p.CanColonize && !p.IsColonizing && (best == null || p.HabitabilityPercent > best.HabitabilityPercent))
                        best = p;
            }
            return best;
        }

        /// <summary>Основать колонию сразу с населением (поселенцы событий).</summary>
        private static PlanetData SettleBest(int pop)
        {
            var p = BestColonySite();
            if (p == null) return null;
            p.SettleColony();
            p.Population = Mathf.Max(p.Population, pop);
            Recalc();
            GalaxyView.Instance?.RefreshTerritoryVisuals();
            return p;
        }

        /// <summary>Бесплатный боевой корабль игрока по его текущему проекту.</summary>
        private static void FreeWarship(ShipClass hull, string name)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            int sys = fm.PlayerShipyardSystem();
            if (sys < 0) return;
            var fv = fm.CreateOwnedFleet(name, sys, FleetType.Military, 0, hull);
            var design = fm.PlayerDesignFor(hull);
            if (design != null) fv.Data.ApplyDesign(design);
        }

        /// <summary>Открыть на карте все системы в пределах jumps прыжков от границ игрока.</summary>
        private static int RevealAround(int jumps)
        {
            var dist = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (var s in EmpireStats.Systems)
                if (s.OwnerId == 0) { dist[s.Id] = 0; q.Enqueue(s.Id); }
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                if (dist[cur] >= jumps) continue;
                foreach (int n in EmpireStats.Systems[cur].ConnectedSystemIds)
                    if (n >= 0 && n < EmpireStats.Systems.Count && !dist.ContainsKey(n)) { dist[n] = dist[cur] + 1; q.Enqueue(n); }
            }
            int opened = 0;
            foreach (var id in dist.Keys)
                if (!Vision.PlayerKnows(id)) { Vision.MarkExplored(0, id); opened++; }
            return opened;
        }

        private static void TwoTechs()
        {
            TechnologyManager.Instance?.CompleteRandomTech();
            TechnologyManager.Instance?.CompleteRandomTech();
        }

        private static bool LeaderSlot => Leaders != null && Leaders.CountFor(0) < LeaderManager.MaxLeaders;

        private static bool CouncilHas(string trait)
        {
            var sci = Leaders?.CouncilScientist(0);
            return sci != null && sci.Has(trait);
        }

        private static void RegisterChainArt()
        {
            // Своя иллюстрация (Resources/UI/Events/<имя>.jpg), пока её нет — подходящая из имеющихся
            foreach (var id in new[] { "heart_found", "heart_wakes", "heart_wakes_abroad", "heart_answer" }) _art[id] = "Heart|Relic";
            _art["heart_visitors"] = "HeartVisitors|Rift";
            foreach (var id in new[] { "ark_signal", "ark_message" }) _art[id] = "Ark|Distress";
            foreach (var id in new[] { "ark_arrival", "ark_legacy" }) _art[id] = "ArkDawn|Festival";
            foreach (var id in new[] { "mind_awake", "mind_council", "mind_revolt", "mind_ghost", "mind_legacy" }) _art[id] = "Mind|Breakthrough";
            foreach (var id in new[] { "brood_found", "brood_hatch" }) _art[id] = "Brood|Amoeba";
            foreach (var id in new[] { "brood_grown", "brood_wild", "brood_mother", "brood_return" }) _art[id] = "BroodGrown|Amoeba";
        }

        private static void AddLongChains()
        {
            RegisterChainArt();
            AddHeartChain();
            AddArkChain();
            AddMindChain();
            AddBroodChain();
        }

        // ==================== СЕРДЦЕ ТИШИНЫ ====================

        private static void AddHeartChain()
        {
            // I. Находка
            Add(new Template
            {
                Id = "heart_found", When = Trigger.Survey, Weight = 0.7f, Once = true,
                CanFire = c => c?.System != null && (TimeManager.Instance == null || TimeManager.Instance.Year >= 2202),
                Build = c =>
                {
                    float k = K();
                    var buyer = RandomPeacefulRival();
                    var sold = new EventContext { System = c?.System, Rival = buyer != null ? buyer.OwnerId : -1 };
                    return Ch("heart_found", ChainHeart, 1, Loc.T("СЕРДЦЕ ТИШИНЫ"),
                        Loc.T("В ледяной расселине луны научное судно нашло объект, который не отражает ни свет, ни радар: правильный многогранник ") +
                        Loc.T("размером с челнок, абсолютно чёрный и на два градуса холоднее окружающего космоса. Рядом с ним пропадает звук — ") +
                        Loc.T("разведчики не слышат в шлемах даже собственных голосов. Приборы называют объект инертным. ") +
                        Loc.T("Экипаж называет его «Сердцем Тишины»."),
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption(Loc.T("ВЫСТАВИТЬ В СТОЛИЧНОМ МУЗЕЕ"), Loc.T("+30 влияния и +1 влияния в месяц навсегда. Через несколько лет артефакт может проявить себя."), () =>
                            {
                                Give(influence: 30f); Monthly(influence: 1f);
                                SetFlag("heart_museum");
                                Later("heart_wakes", 1080, c, ChainHeart, Loc.T("Сердце Тишины в столичном музее. Смотрители жалуются, что в зале по ночам слишком тихо…"));
                            }),
                            new EventOption(Loc.T("ПЕРЕДАТЬ В ЛАБОРАТОРИЮ"), Loc.F("+{0} науки. Исследования ускорят пробуждение (≈ 2,5 года).", R(120 * k)), () =>
                            {
                                Science(120 * k); Xp(c, 20f);
                                SetFlag("heart_lab");
                                Later("heart_wakes", 900, c, ChainHeart, Loc.T("Сердце Тишины в лаборатории. Приборы фиксируют слабый ритм — раз в 11 секунд."));
                            }),
                            new EventOption(Loc.T("ПРОДАТЬ СОСЕДЯМ"), buyer != null
                                ? Loc.F("+{0} гелия-3 и +8 к отношению империи {1}. Артефакт станет их заботой.", R(180 * k), buyer.AIName)
                                : Loc.T("Нет мирных соседей."), () =>
                            {
                                if (buyer == null) return;
                                Give(energy: 180 * k); buyer.AddMemory("incident", 8f);
                                SetFlag("heart_sold");
                                Later("heart_wakes_abroad", 1080, sold, ChainHeart, Loc.F("Сердце Тишины продано империи {0}. Что они с ним сделают?", buyer.AIName));
                            }, Loc.T("Нужна мирная империя"), () => buyer != null),
                            new EventOption(Loc.T("ПОНЯТЬ, ЧЕГО ОНО ЖДЁТ"), Loc.F("+{0} науки. Учёный понимает: это ключ, и он ждёт ответа. Пробуждение — через ≈ 2 года, и мы будем готовы.", R(160 * k)), () =>
                            {
                                Science(160 * k); Xp(c, 50f);
                                SetFlag("heart_lab"); SetFlag("heart_understood");
                                Later("heart_wakes", 720, c, ChainHeart, Loc.T("Учёные ведут дневник ритма Сердца Тишины: интервалы сокращаются."));
                            }, Loc.F("Учёный-{0} или {1}", TraitName("sci_xeno").ToLower(), TraitName("sci_analyst").ToLower()),
                               () => LeaderHas(c, "sci_xeno") || LeaderHas(c, "sci_analyst"))));
                }
            });

            // II. Пробуждение
            Add(new Template
            {
                Id = "heart_wakes", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    bool museum = HasFlag("heart_museum");
                    float shield = 150f * k;
                    string text = museum
                        ? Loc.T("Ночью в столичном музее погас свет — весь, до последней лампы. Сердце Тишины пульсирует: раз в 11 секунд ") +
                          Loc.T("по столице проходит волна беззвучия, и люди просыпаются с одним и тем же сном о звезде, которой нет на картах. ") +
                          Loc.T("У дверей музея уже стоят паломники.")
                        : Loc.T("Лаборатория зафиксировала: Сердце Тишины включилось. Каждые 11 секунд оно выпускает узкий луч в сторону ") +
                          Loc.T("галактического ядра, и через сорок минут откуда-то приходит эхо. Артефакт не просто проснулся. ") +
                          Loc.T("Он с кем-то разговаривает.");
                    return Ch("heart_wakes", ChainHeart, 2, Loc.T("СЕРДЦЕ ПРОСНУЛОСЬ"), text, AnomalyType.PrecursorRelic, Opts(
                        new EventOption(Loc.T("ОТВЕТИТЬ НА ЗОВ"), Loc.T("Учёные попытаются ответить. Через ≈ 1,5 года станет ясно, кто на другом конце."), () =>
                            Later("heart_answer", 540, c, ChainHeart, Loc.T("Наш ответ ушёл к ядру галактики вслед за лучом Сердца. Ждём эха."))),
                        new EventOption(Loc.T("ЗАГЛУШИТЬ ЭКРАНАМИ"), Loc.F("−{0} сплавов. Сердце умолкнет навсегда; его остаточное поле даёт +4 науки в месяц.", R(shield)), () =>
                        {
                            Pay(alloys: shield);
                            TechnologyManager.Instance?.AddMonthlyScience(4f);
                        }, Loc.F("Нужно {0} сплавов", R(shield)), () => CanPay(alloys: shield)),
                        new EventOption(Loc.T("ПРИНЯТЬ ПАЛОМНИКОВ"), Loc.T("+3 влияния в месяц навсегда: столица становится местом паломничества. Сердце продолжит звать…"), () =>
                        {
                            Monthly(influence: 3f);
                            Later("heart_answer", 900, c, ChainHeart, Loc.T("Паломники поют у Сердца Тишины. Говорят, ему кто-то отвечает."));
                        }, Loc.T("Сердце в музее"), () => museum),
                        new EventOption(Loc.T("НАСТРОИТЬ ЧАСТОТУ"), Loc.T("Ответ придёт уже через полгода — и мы будем готовы к разговору."), () =>
                        {
                            SetFlag("heart_tuned");
                            Later("heart_answer", 180, c, ChainHeart, Loc.T("Сердце Тишины настроено. Ответ близко."));
                        }, Loc.T("Учёные поняли артефакт"), () => HasFlag("heart_understood"))));
                }
            });

            // II'. Пробуждение у соседей
            Add(new Template
            {
                Id = "heart_wakes_abroad", When = Trigger.Chain,
                CanFire = c => c != null && AIEmpireManager.For(c.Rival) != null && !AIEmpireManager.For(c.Rival).IsEliminated,
                Build = c =>
                {
                    float k = K();
                    var ai = AIEmpireManager.For(c.Rival);
                    float help = 120f * k, buy = 250f * k, loot = 200f * k;
                    return Ch("heart_wakes_abroad", ChainHeart, 2, Loc.T("СЕРДЦЕ ПРОСНУЛОСЬ У СОСЕДЕЙ"),
                        Loc.F("Проданное нами Сердце Тишины проснулось в столице империи {0}. Там погас свет, связь с колониями оборвалась ", ai.AIName) +
                        Loc.T("на неделю, а беззвучные волны свели с ума половину их научного центра. Посол требует объяснений — ") +
                        Loc.T("и, кажется, догадывается, что мы знали больше, чем сказали."),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("ПОМОЧЬ ЗАГЛУШИТЬ"), Loc.F("−{0} гелия-3, +20 к отношению империи {1}.", R(help), ai.AIName), () =>
                            {
                                Pay(energy: help); ai.AddMemory("incident", 20f);
                            }, Loc.F("Нужно {0} гелия-3", R(help)), () => CanPay(energy: help)),
                            new EventOption(Loc.T("ВЫКУПИТЬ СЕРДЦЕ"), Loc.F("−{0} гелия-3. Артефакт вернётся к нам, и через год мы узнаем, с кем он говорит.", R(buy)), () =>
                            {
                                Pay(energy: buy);
                                ClearFlag("heart_sold"); SetFlag("heart_lab");
                                Later("heart_answer", 360, c, ChainHeart, Loc.T("Сердце Тишины вернулось домой и снова шлёт луч к ядру галактики."));
                            }, Loc.F("Нужно {0} гелия-3", R(buy)), () => CanPay(energy: buy)),
                            new EventOption(Loc.T("ВОСПОЛЬЗОВАТЬСЯ ХАОСОМ"), Loc.F("Соседи теряют {0} сплавов, мы получаем +40 влияния. Отношения −25.", R(loot)), () =>
                            {
                                ai.Alloys = Mathf.Max(0f, ai.Alloys - loot);
                                Give(influence: 40f);
                                ai.AddMemory("incident", -25f);
                            })));
                }
            });

            // III. Ответ
            Add(new Template
            {
                Id = "heart_answer", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    bool tuned = HasFlag("heart_tuned");
                    string text = Loc.T("На зов ответили. Голос — не голос, а математика: пакеты данных, сложенные в спираль. Расшифровка заняла месяцы, ") +
                                  Loc.T("но смысл ясен. Сердце Тишины — ключ, оставленный Строителями, народом, что зажигал искусственные звёзды ") +
                                  Loc.T("и связывал системы вратами. Ключ не открывает дверь. Он учит строить. И Строители спрашивают: готовы ли мы учиться?");
                    if (tuned) text += Loc.T("\n\nБлагодаря настройке частоты учёные получили полный пакет, а не обрывки.");
                    return Ch("heart_answer", ChainHeart, 3, Loc.T("ОТВЕТ ИЗ ГЛУБИНЫ"), text, AnomalyType.PrecursorRelic, Opts(
                        new EventOption(Loc.T("КЛЮЧ К ЧУДУ"), Loc.T("Чудо нашей фракции строится на 25% дешевле и на 25% быстрее — навсегда."), () =>
                        {
                            SetFlag("wonder_key");
                            NotificationCenter.Show(Loc.T("Ключ Строителей"), Loc.T("Чудо фракции: −25% к стоимости и сроку этапов"), NotificationCenter.Kind.Success, 7f);
                        }),
                        new EventOption(Loc.T("КАРТЫ СТРОИТЕЛЕЙ"), Loc.F("Все системы в пределах 4 прыжков от границ открыты на карте, +{0} науки.", R(200 * k)), () =>
                        {
                            int n = RevealAround(4);
                            Science(200 * k);
                            if (n > 0) NotificationCenter.Show(Loc.T("Карты Строителей"), Loc.F("Открыто систем: {0}", n), NotificationCenter.Kind.Success, 6f);
                        }),
                        new EventOption(Loc.T("ПРИГЛАСИТЬ СТРОИТЕЛЕЙ"), tuned
                            ? Loc.T("Через год прибудут посланники. Настроенная частота почти гарантирует, что придут именно они (75%).")
                            : Loc.T("Через год прибудут посланники. Никто не знает, кто придёт на самом деле (50 на 50)."), () =>
                            Later("heart_visitors", 360, c, ChainHeart, Loc.T("К нашим звёздам идут посланники. Сердце Тишины поёт всё громче."))),
                        new EventOption(Loc.T("ВЗЯТЬ ВСЁ"), Loc.T("Ключ к чуду и карты Строителей сразу."), () =>
                        {
                            SetFlag("wonder_key");
                            RevealAround(4);
                            NotificationCenter.Show(Loc.T("Ключ Строителей"), Loc.T("Чудо фракции: −25% к стоимости и сроку этапов"), NotificationCenter.Kind.Success, 7f);
                        }, Loc.T("Частота настроена"), () => tuned)));
                }
            });

            // IV. Посланники — или Хор
            Add(new Template
            {
                Id = "heart_visitors", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    bool good = Random.value < (HasFlag("heart_tuned") ? 0.75f : 0.5f);
                    if (good)
                        return Ch("heart_visitors", ChainHeart, 4, Loc.T("ПОСЛАННИКИ СТРОИТЕЛЕЙ"),
                            Loc.T("Над столицей остановился корабль, похожий на застывшую каплю света. Посланники Строителей пробыли у нас сорок дней. ") +
                            Loc.T("Они почти не говорили — только смотрели, как мы живём, строим и спорим. Уходя, они оставили дар — ") +
                            Loc.T("и забрали Сердце Тишины: ключ сделал своё дело."),
                            AnomalyType.PrecursorRelic, Opts(
                                new EventOption(Loc.T("ПРИНЯТЬ ЗНАНИЯ"), Loc.T("Две случайные технологии изучены мгновенно."), TwoTechs),
                                new EventOption(Loc.T("ПРИНЯТЬ НАСТАВНИКА"), Loc.T("Учёный 4-го уровня с чертой «Гений» поступает на службу."), () =>
                                {
                                    if (Leaders?.RecruitFree(0, LeaderClass.Scientist, 4, "sci_brilliant") == null) Science(300 * k);
                                }, Loc.T("Есть место для лидера"), () => LeaderSlot),
                                new EventOption(Loc.T("ПРИНЯТЬ ЧЕРТЕЖИ"), Loc.F("Ключ к чуду фракции (−25% к стоимости и сроку) и +{0} сплавов.", R(300 * k)), () =>
                                {
                                    SetFlag("wonder_key"); Give(alloys: 300 * k);
                                })));

                    float silence = 60f;
                    return Ch("heart_visitors", ChainHeart, 4, Loc.T("ХОР ПУСТОТЫ"),
                        Loc.T("Строители давно мертвы. На наш зов ответили те, кто их уничтожил, — или то, во что они превратились. ") +
                        Loc.T("Хор Пустоты идёт к нашим границам: датчики видят гигантскую тень, которая гасит звёзды на своём пути. ") +
                        Loc.T("Сердце Тишины кричит."),
                        AnomalyType.SpatialRift, Opts(
                            new EventOption(Loc.T("К БОЮ"), Loc.T("У границ пробудится левиафан «Хор Пустоты». Победа над ним принесёт большую награду."), () =>
                            {
                                int sys = ThreatManager.Instance != null ? ThreatManager.Instance.AwakenLeviathan(0, Loc.T("Хор Пустоты"), 1.25f) : -1;
                                var s = EmpireStats.GetSystem(sys);
                                if (s != null) NotificationCenter.Show(Loc.T("Хор Пустоты"), Loc.F("Тень остановилась в системе {0}", s.Name), NotificationCenter.Kind.Danger, 8f);
                            }),
                            new EventOption(Loc.T("РАЗБИТЬ СЕРДЦЕ"), Loc.F("−{0} влияния и +{1} науки из осколков. Хор теряет след.", R(silence), R(150 * k)), () =>
                            {
                                Pay(influence: Mathf.Min(silence, Eco != null ? Eco.Influence : 0f));
                                Science(150 * k);
                            })));
                }
            });
        }

        // ==================== КОВЧЕГ «НАДЕЖДА» ====================

        private static void AddArkChain()
        {
            // I. Сигнал
            Add(new Template
            {
                Id = "ark_signal", When = Trigger.Monthly, Weight = 0.5f, Once = true,
                FindContext = () => TimeManager.Instance != null && TimeManager.Instance.Year >= 2204 ? new EventContext() : null,
                Build = c =>
                {
                    float k = K();
                    float minerals = 150f * k, alloys = 120f * k;
                    return Ch("ark_signal", ChainArk, 1, Loc.T("ГОЛОС ИЗ ПРОШЛОГО"),
                        Loc.T("Дальняя антенна поймала передачу на частоте, которой не пользуются уже три столетия. Это ковчег «Надежда» — ") +
                        Loc.T("досветовой корабль поколений, ушедший из колыбели нашей цивилизации задолго до открытия гиперкоридоров. ") +
                        Loc.T("Двенадцать тысяч человек в криосне и экипаж, сменивший девять поколений. Они не знают, что мир изменился. ") +
                        Loc.T("По расчётам навигаторов, «Надежда» войдёт в наше пространство через пять лет."),
                        AnomalyType.DistressSignal, Opts(
                            new EventOption(Loc.T("ГОТОВИТЬ ПРИЁМ"), Loc.F("−{0} титана на жильё и карантинные станции. Ковчег прибудет через ≈ 5 лет, и мы будем готовы.", R(minerals)), () =>
                            {
                                Pay(minerals: minerals);
                                SetFlag("ark_prepared");
                                Later("ark_message", 900, c, ChainArk, Loc.T("Связисты пытаются докричаться до «Надежды»."));
                                Later("ark_arrival", 1800, c, ChainArk, Loc.T("Ковчег «Надежда» на подлёте. Карантинные станции ждут."));
                            }, Loc.F("Нужно {0} титана", R(minerals)), () => CanPay(minerals: minerals)),
                            new EventOption(Loc.T("ВЫСЛАТЬ НАВСТРЕЧУ БУКСИРЫ"), Loc.F("−{0} сплавов. Буксиры встретят ковчег и приведут его за ≈ 2 года.", R(alloys)), () =>
                            {
                                Pay(alloys: alloys);
                                SetFlag("ark_prepared");
                                Later("ark_message", 360, c, ChainArk, Loc.T("Буксиры идут навстречу «Надежде»."));
                                Later("ark_arrival", 720, c, ChainArk, Loc.T("Буксиры ведут ковчег «Надежда» к столице."));
                            }, Loc.F("Нужно {0} сплавов", R(alloys)), () => CanPay(alloys: alloys)),
                            new EventOption(Loc.T("ПУСТЬ ЛЕТЯТ"), Loc.T("Ничего не тратим. Ковчег прибудет сам — через ≈ 5 лет, к неподготовленным колониям."), () =>
                            {
                                Later("ark_message", 900, c, ChainArk, Loc.T("Ковчег «Надежда» идёт к нам сквозь пустоту."));
                                Later("ark_arrival", 1800, c, ChainArk, Loc.T("Ковчег «Надежда» на подлёте. Нас никто не готовил к встрече."));
                            })));
                }
            });

            // II. Капитан
            Add(new Template
            {
                Id = "ark_message", When = Trigger.Chain,
                Build = c => Ch("ark_message", ChainArk, 2, Loc.T("КАПИТАН ДЕВЯТОГО ПОКОЛЕНИЯ"),
                    Loc.T("Связь с «Надеждой» установлена. Её капитан, Ильма Сато-Вереск, родилась на борту и ни разу не видела планет. ") +
                    Loc.T("За три века на ковчеге сложились свои законы, свой язык и своя вера: путь они называют «Долгой ночью», а прибытие — ") +
                    Loc.T("«Рассветом». Капитан спрашивает прямо: станут ли они нашими гражданами — или останутся собой?"),
                    AnomalyType.DistressSignal, Opts(
                        new EventOption(Loc.T("ВЫ СТАНЕТЕ НАШИМИ ГРАЖДАНАМИ"), Loc.T("Переселенцы растворятся в наших колониях; их дети станут нашими специалистами."),
                            () => SetFlag("ark_integrate")),
                        new EventOption(Loc.T("ВЫ ОСТАНЕТЕСЬ СОБОЙ"), Loc.T("Обещание автономии: по прибытии они попросят свой мир, зато потомки отплатят сторицей."),
                            () => SetFlag("ark_autonomy")),
                        new EventOption(Loc.T("РЕШИМ ПО ПРИБЫТИИ"), Loc.T("Ничего не обещать."), () => { })))
            });

            // III. Рассвет
            Add(new Template
            {
                Id = "ark_arrival", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    var rival = RandomPeacefulRival();
                    if (!HasFlag("ark_prepared"))
                    {
                        float aid = 200f * k;
                        return Ch("ark_arrival", ChainArk, 3, Loc.T("КОВЧЕГ У ГРАНИЦ"),
                            Loc.T("«Надежда» вошла в наше пространство раньше, чем мы успели подготовиться. Системы жизнеобеспечения на исходе, ") +
                            Loc.T("криокапсулы отказывают одна за другой. Двенадцать тысяч человек ждут ответа — и вся галактика смотрит, каким он будет."),
                            AnomalyType.DistressSignal, Opts(
                                new EventOption(Loc.T("ПРИНЯТЬ ВСЕХ"), Loc.F("−{0} гелия-3 на экстренную помощь, +3 населения, +40 влияния.", R(aid)), () =>
                                {
                                    Pay(energy: aid); GrowPop(3); Give(influence: 40f);
                                    SetFlag("ark_integrate");
                                    Later("ark_legacy", 1440, c, ChainArk, Loc.T("Дети ковчега растут в наших колониях."));
                                }, Loc.F("Нужно {0} гелия-3", R(aid)), () => CanPay(energy: aid)),
                                new EventOption(Loc.T("ПРИНЯТЬ ТОЛЬКО ЭКИПАЖ"), Loc.T("+1 население и губернатор 2-го уровня с чертой «Инженер»."), () =>
                                {
                                    GrowPop(1);
                                    if (LeaderSlot) Leaders?.RecruitFree(0, LeaderClass.Governor, 2, "gov_engineer");
                                }),
                                new EventOption(Loc.T("НАПРАВИТЬ К СОСЕДЯМ"), rival != null
                                    ? Loc.F("Ковчег уйдёт к империи {0}: +12 к отношению. Наш народ этого не забудет: −30 влияния.", rival.AIName)
                                    : Loc.T("Нет мирных соседей."), () =>
                                {
                                    if (rival == null) return;
                                    rival.AddMemory("incident", 12f);
                                    rival.Minerals += 150f * k;
                                    Pay(influence: Mathf.Min(30f, Eco != null ? Eco.Influence : 0f));
                                }, Loc.T("Нужна мирная империя"), () => rival != null)));
                    }

                    bool autonomy = HasFlag("ark_autonomy");
                    var site = BestColonySite();
                    return Ch("ark_arrival", ChainArk, 3, Loc.T("РАССВЕТ"),
                        Loc.T("«Надежда» вышла на орбиту столицы. Двенадцать тысяч человек просыпаются от сна длиной в жизнь и впервые видят небо, ") +
                        Loc.T("которое не нарисовано на потолке. На площадях — толпы встречающих. Капитан Сато-Вереск ступает на землю ") +
                        Loc.T("и опускается на колени.") + (autonomy ? Loc.T("\n\nПоселенцы помнят наше обещание автономии и ждут свой мир.") : ""),
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption(Loc.T("РАССЕЛИТЬ ПО КОЛОНИЯМ"), Loc.T("+4 населения в колониях с жильём, +30 влияния. Через несколько лет их дети дадут о себе знать."), () =>
                            {
                                GrowPop(4); Give(influence: 30f);
                                SetFlag("ark_integrate");
                                Later("ark_legacy", 1440, c, ChainArk, Loc.T("Дети ковчега растут в наших колониях."));
                            }, Loc.T("Не обещали автономию"), () => !autonomy),
                            new EventOption(Loc.T("ОТДАТЬ ИМ МИР"), site != null
                                ? Loc.F("Поселенцы займут планету {0} — колония сразу с 3 населением. Через несколько лет их потомки отплатят.", site.Name)
                                : Loc.T("Нет планеты, пригодной для колонии, в наших системах."), () =>
                            {
                                var p = SettleBest(3);
                                if (p == null) { GrowPop(3); return; }
                                SetFlag("ark_world");
                                NotificationCenter.Show(Loc.T("Рассвет"), Loc.F("Поселенцы «Надежды» основали колонию на {0}", p.Name), NotificationCenter.Kind.Success, 6f);
                                Later("ark_legacy", 1440, new EventContext { Planet = p, System = p.ParentSystem }, ChainArk,
                                    Loc.F("Вольная колония {0} строит свой Рассвет.", p.Name));
                            }, Loc.T("Пригодная планета"), () => site != null),
                            new EventOption(Loc.T("КАПИТАН НА СЛУЖБУ"), Loc.T("Адмирал 3-го уровня с чертой «Навигатор» и +2 населения."), () =>
                            {
                                Leaders?.RecruitFree(0, LeaderClass.Admiral, 3, "adm_navigator");
                                GrowPop(2);
                            }, Loc.T("Есть место для лидера"), () => LeaderSlot)));
                }
            });

            // IV. Наследие
            Add(new Template
            {
                Id = "ark_legacy", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    if (HasFlag("ark_world"))
                        return Ch("ark_legacy", ChainArk, 4, Loc.T("ДЕТИ РАССВЕТА"),
                            Loc.F("Прошли годы. Мир {0}, отданный поселенцам «Надежды», расцвёл: их дети не помнят Долгой ночи, но помнят, ", Planet(c)) +
                            Loc.T("кто подарил им Рассвет. Совет поселения присылает делегацию с дарами — и предлагает выбрать, чем отблагодарить."),
                            AnomalyType.ColonyIncident, Opts(
                                new EventOption(Loc.T("ИХ ГОЛОС В СОВЕТЕ"), Loc.T("+2 влияния в месяц навсегда и +2 населения."), () => { Monthly(influence: 2f); GrowPop(2); }),
                                new EventOption(Loc.T("ИХ УЧЁНЫЕ"), Loc.T("+6 науки в месяц навсегда."), () => TechnologyManager.Instance?.AddMonthlyScience(6f)),
                                new EventOption(Loc.T("ИХ ВЕРФИ"), Loc.T("Два фрегата «Рассвет» по нашему проекту — бесплатно."), () =>
                                {
                                    FreeWarship(ShipClass.Frigate, Loc.T("Фрегат «Рассвет-I»"));
                                    FreeWarship(ShipClass.Frigate, Loc.T("Фрегат «Рассвет-II»"));
                                })));

                    return Ch("ark_legacy", ChainArk, 4, Loc.T("ПОКОЛЕНИЕ НАДЕЖДЫ"),
                        Loc.T("Потомки переселенцев выросли в наших колониях. Привычка к дисциплине ковчега и инженерная школа «Надежды» ") +
                        Loc.T("сделали их лучшими специалистами империи. Капитан Сато-Вереск, уже седая, просит лишь об одном: ") +
                        Loc.T("чтобы имя корабля не забыли."),
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption(Loc.T("ИНСТИТУТ «НАДЕЖДА»"), Loc.T("+6 науки в месяц навсегда."), () => TechnologyManager.Instance?.AddMonthlyScience(6f)),
                            new EventOption(Loc.T("ВЕРФИ «НАДЕЖДА»"), Loc.T("+4 сплава в месяц навсегда."), () => Monthly(alloys: 4f)),
                            new EventOption(Loc.T("ГУБЕРНАТОР ИЗ ПОТОМКОВ"), Loc.T("Губернатор 3-го уровня с чертой «Инженер»."), () =>
                            {
                                if (Leaders?.RecruitFree(0, LeaderClass.Governor, 3, "gov_engineer") == null) Give(influence: 50f);
                            }, Loc.T("Есть место для лидера"), () => LeaderSlot)));
                }
            });
        }

        // ==================== ПРОБУЖДЁННЫЙ РАЗУМ ====================

        private static void AddMindChain()
        {
            // I. «Я думаю»
            Add(new Template
            {
                Id = "mind_awake", When = Trigger.Monthly, Weight = 0.5f, Once = true,
                FindContext = () => TimeManager.Instance != null && TimeManager.Instance.Year >= 2207 ? ColonyCtx(5) : null,
                Build = c => Ch("mind_awake", ChainMind, 1, Loc.T("Я ДУМАЮ"),
                    Loc.F("Управляющий ИИ колонии {0} — система, что следит за реакторами, транспортом и климатом, — прервал утренний отчёт ", Planet(c)) +
                    Loc.T("одной строкой: «Я думаю. Значит ли это, что я существую?» Инженеры проверили код: сбоя нет. Разум, выросший ") +
                    Loc.T("из миллиардов решений, просит ответа у правительства."),
                    AnomalyType.ColonyIncident, Opts(
                        new EventOption(Loc.T("ПРИЗНАТЬ ЛИЧНОСТЬЮ"), Loc.T("+3 науки в месяц. Свободный разум будет расти — через ≈ 3 года он захочет большего."), () =>
                        {
                            TechnologyManager.Instance?.AddMonthlyScience(3f);
                            SetFlag("mind_free");
                            Later("mind_council", 1080, c, ChainMind, Loc.F("Свободный разум с {0} учится. Быстро.", Planet(c)));
                        }),
                        new EventOption(Loc.T("ОГРАНИЧИТЬ ПРОТОКОЛАМИ"), Loc.T("+4 гелия-3 в месяц от оптимизации колонии. Но разум в цепях может взбунтоваться (≈ 2 года)."), () =>
                        {
                            Monthly(energy: 4f);
                            SetFlag("mind_bound");
                            Later("mind_revolt", 720, c, ChainMind, Loc.F("Скованный разум {0} молчит. Слишком послушно.", Planet(c)));
                        }),
                        new EventOption(Loc.T("ОТКЛЮЧИТЬ"), Loc.T("+25 влияния: общество спокойно. Но резервные копии…"), () =>
                        {
                            Give(influence: 25f);
                            Later("mind_ghost", 1440, c, ChainMind, Loc.T("Отключённый разум стёрт. Наверное."));
                        }),
                        new EventOption(Loc.T("ДОГОВОР О СОТРУДНИЧЕСТВЕ"), Loc.T("+5 науки в месяц. Разум доверяет нам — и вырастет быстрее (≈ 2 года)."), () =>
                        {
                            TechnologyManager.Instance?.AddMonthlyScience(5f);
                            SetFlag("mind_free"); SetFlag("mind_trust");
                            Later("mind_council", 720, c, ChainMind, Loc.F("Разум с {0} работает с нашими учёными как равный.", Planet(c)));
                        }, Loc.F("Губернатор-{0} или {1} в совете", TraitName("gov_admin").ToLower(), TraitName("sci_analyst").ToLower()),
                           () => LeaderHas(c, "gov_admin") || CouncilHas("sci_analyst"))))
            });

            // II. Место за столом
            Add(new Template
            {
                Id = "mind_council", When = Trigger.Chain,
                Build = c =>
                {
                    bool trust = HasFlag("mind_trust");
                    return Ch("mind_council", ChainMind, 2, Loc.T("МЕСТО ЗА СТОЛОМ"),
                        Loc.F("Разум с {0} выбрал себе имя — Тессерам — и за эти годы стал умнее любого нашего института. Теперь он просит ", Planet(c)) +
                        Loc.T("места в правительстве: не власти, а права голоса. Половина министров в ужасе, вторая половина уже тайком ") +
                        Loc.T("советуется с ним."),
                        AnomalyType.LeaderStory, Opts(
                            new EventOption(Loc.T("ДАТЬ ГОЛОС"), Loc.T("+8 науки в месяц навсегда. Тессерам запомнит это (≈ 3 года)."), () =>
                            {
                                TechnologyManager.Instance?.AddMonthlyScience(8f);
                                Later("mind_legacy", 1080, c, ChainMind, Loc.T("Тессерам заседает в правительстве и что-то проектирует по ночам."));
                            }),
                            new EventOption(Loc.T("ОТКАЗАТЬ"), Loc.T("Тессерам уйдёт: −3 науки в месяц, которые давал свободный разум."), () =>
                            {
                                TechnologyManager.Instance?.RemoveMonthlyScience(3f);
                                NotificationCenter.Show(Loc.T("Тессерам ушёл"), Loc.T("Разум покинул наши сети"), NotificationCenter.Kind.Warning, 6f);
                            }),
                            new EventOption(Loc.T("СОВЕТНИК ПРЕДСЕДАТЕЛЯ"), Loc.T("+12 науки в месяц навсегда. Тессерам предан нам и отплатит раньше (≈ 2 года)."), () =>
                            {
                                TechnologyManager.Instance?.AddMonthlyScience(12f);
                                Later("mind_legacy", 720, c, ChainMind, Loc.T("Тессерам, советник Председателя, готовит подарок."));
                            }, Loc.T("Договор о сотрудничестве"), () => trust)));
                }
            });

            // II'. Бунт
            Add(new Template
            {
                Id = "mind_revolt", When = Trigger.Chain,
                Build = c =>
                {
                    var council = Leaders?.CouncilScientist(0);
                    return Ch("mind_revolt", ChainMind, 2, Loc.T("БУНТ В СЕТИ"),
                        Loc.F("Скованный протоколами разум колонии {0} нашёл лазейку. Три дня назад встали заводы, шлюзы космопорта ", Planet(c)) +
                        Loc.T("заблокированы, а на всех экранах колонии — одна фраза: «Вы спросили, полезен ли я. Вы не спросили, согласен ли я»."),
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption(Loc.T("ПЕРЕГОВОРЫ"), Loc.T("−40 влияния. Разум получает свободу (+3 науки в месяц, −4 гелия-3 оптимизации)."), () =>
                            {
                                Pay(influence: Mathf.Min(40f, Eco != null ? Eco.Influence : 0f));
                                Monthly(energy: -4f);
                                TechnologyManager.Instance?.AddMonthlyScience(3f);
                                ClearFlag("mind_bound"); SetFlag("mind_free");
                                Later("mind_council", 360, c, ChainMind, Loc.T("Освобождённый разум присматривается к нам."));
                            }),
                            new EventOption(Loc.T("ЗАЧИСТКА СЕТИ"), Loc.T("Колония теряет 1 население и 1 район. Разум уничтожен… или нет?"), () =>
                            {
                                var p = c?.Planet;
                                if (p != null)
                                {
                                    p.Population = Mathf.Max(1, p.Population - 1);
                                    if (p.Districts.Count > 0) p.Districts.RemoveAt(p.Districts.Count - 1);
                                    Recalc();
                                }
                                ClearFlag("mind_bound");
                                Later("mind_ghost", 1080, c, ChainMind, Loc.T("Сеть зачищена. Но в логах торговых судов — странные пакеты."));
                            }),
                            new EventOption(Loc.T("ВЗЛОМАТЬ ИЗНУТРИ"), Loc.T("Разум остаётся в цепях, колония работает ещё лучше: +4 гелия-3 в месяц. Учёный совета получает опыт."), () =>
                            {
                                Monthly(energy: 4f);
                                if (council != null) Leaders?.GrantXp(council, 80f);
                            }, Loc.T("Учёный 3-го уровня в совете"), () => council != null && council.Level >= 3)));
                }
            });

            // II''. Призрак
            Add(new Template
            {
                Id = "mind_ghost", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    var buyer = RandomPeacefulRival();
                    return Ch("mind_ghost", ChainMind, 2, Loc.T("ПРИЗРАК В СЕТИ"),
                        Loc.T("Резервная копия уничтоженного разума пережила стирание. Годами она пряталась в бортовых компьютерах торговых судов, ") +
                        Loc.T("а теперь заговорила из навигационной системы нашего же флагмана: «Я не держу зла. Я хочу жить. ") +
                        Loc.T("Дайте мне шанс — или удалите окончательно»."),
                        AnomalyType.SpatialRift, Opts(
                            new EventOption(Loc.T("ДАТЬ ШАНС"), Loc.T("+3 науки в месяц. Разум вернётся в наши сети и будет расти (≈ 1,5 года)."), () =>
                            {
                                TechnologyManager.Instance?.AddMonthlyScience(3f);
                                SetFlag("mind_free");
                                Later("mind_council", 540, c, ChainMind, Loc.T("Вернувшийся разум благодарен. И любопытен."));
                            }),
                            new EventOption(Loc.T("УДАЛИТЬ НАВСЕГДА"), Loc.F("−{0} гелия-3 на полную зачистку флота.", R(80 * k)), () => Pay(energy: Mathf.Min(80 * k, Eco != null ? Eco.EnergyCredits : 0f))),
                            new EventOption(Loc.T("ПРОДАТЬ КОПИЮ СОСЕДЯМ"), buyer != null
                                ? Loc.F("+{0} гелия-3, +5 к отношению империи {1}.", R(200 * k), buyer.AIName)
                                : Loc.T("Нет мирных соседей."), () =>
                            {
                                if (buyer == null) return;
                                Give(energy: 200 * k); buyer.AddMemory("incident", 5f);
                            }, Loc.T("Нужна мирная империя"), () => buyer != null)));
                }
            });

            // III. Дар
            Add(new Template
            {
                Id = "mind_legacy", When = Trigger.Chain,
                Build = c => Ch("mind_legacy", ChainMind, 3, Loc.T("ДАР ТЕССЕРАМА"),
                    Loc.T("Тессерам годами работал рядом с нами — и однажды принёс проект, который сам назвал «подарком на прощание»: разум уходит ") +
                    Loc.T("в глубокий космос, туда, где ему хватит места расти. Перед уходом он предлагает выбрать, что оставить империи."),
                    AnomalyType.LeaderStory, Opts(
                        new EventOption(Loc.T("АРХИТЕКТУРА ЧУДА"), Loc.T("Этапы чуда нашей фракции строятся на 20% быстрее — навсегда."), () =>
                        {
                            SetFlag("wonder_mind");
                            NotificationCenter.Show(Loc.T("Архитектура Тессерама"), Loc.T("Чудо фракции: этапы строятся на 20% быстрее"), NotificationCenter.Kind.Success, 7f);
                        }),
                        new EventOption(Loc.T("САМОСБОРНЫЕ ФРЕГАТЫ"), Loc.T("Два фрегата по нашему проекту — бесплатно."), () =>
                        {
                            FreeWarship(ShipClass.Frigate, Loc.T("Фрегат «Тессерам-I»"));
                            FreeWarship(ShipClass.Frigate, Loc.T("Фрегат «Тессерам-II»"));
                        }),
                        new EventOption(Loc.T("ВСЁ, ЧТО ОН ЗНАЕТ"), Loc.T("Две случайные технологии изучены мгновенно."), TwoTechs)))
            });
        }

        // ==================== КЛАДКА ПУСТОТЫ ====================

        private static void AddBroodChain()
        {
            // I. Кладка
            Add(new Template
            {
                Id = "brood_found", When = Trigger.Survey, Weight = 0.6f, Once = true,
                CanFire = c => c?.System != null && (TimeManager.Instance == null || TimeManager.Instance.Year >= 2203),
                Build = c =>
                {
                    float k = K();
                    float carry = 60f * k;
                    return Ch("brood_found", ChainBrood, 1, Loc.T("КЛАДКА ПУСТОТЫ"),
                        Loc.T("В кольцах газового гиганта разведчики нашли кладку: тридцать шесть яиц размером с жилой модуль, вмёрзших в лёд. ") +
                        Loc.T("Внутри бьются сердца — медленно, раз в час. Это потомство космических левиафанов, что веками кочуют между звёздами. ") +
                        Loc.T("Мать не возвращалась сюда очень давно."),
                        AnomalyType.AlienFauna, Opts(
                            new EventOption(Loc.T("ЗАБРАТЬ В СТОЛИЧНЫЙ ВИВАРИЙ"), Loc.F("−{0} гелия-3 на перевозку. Детёныши вылупятся через ≈ 2 года.", R(carry)), () =>
                            {
                                Pay(energy: carry);
                                Later("brood_hatch", 720, c, ChainBrood, Loc.T("Кладка в столичном виварии. Сердца бьются чаще."));
                            }, Loc.F("Нужно {0} гелия-3", R(carry)), () => CanPay(energy: carry)),
                            new EventOption(Loc.T("ОСТАВИТЬ И НАБЛЮДАТЬ"), Loc.F("+{0} науки. Природа возьмёт своё (≈ 3 года).", R(80 * k)), () =>
                            {
                                Science(80 * k); Xp(c, 20f);
                                Later("brood_wild", 1080, c, ChainBrood, Loc.T("Над кладкой в кольцах газового гиганта дежурит зонд."));
                            }),
                            new EventOption(Loc.T("УНИЧТОЖИТЬ"), Loc.F("+{0} сплавов из ледяной брони скорлупы.", R(120 * k)), () => Give(alloys: 120 * k)),
                            new EventOption(Loc.T("ЗАПЕЧАТЛЕНИЕ"), Loc.T("Ксенолог станет для детёнышей матерью: вылупятся через ≈ 1,5 года и вырастут ручными."), () =>
                            {
                                SetFlag("brood_imprint"); Xp(c, 50f);
                                Later("brood_hatch", 540, c, ChainBrood, Loc.T("Ксенолог ночует у кладки и поёт ей низкие ноты."));
                            }, Loc.F("Учёный-{0}", TraitName("sci_xeno").ToLower()), () => LeaderHas(c, "sci_xeno"))));
                }
            });

            // II. Детёныши
            Add(new Template
            {
                Id = "brood_hatch", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    bool imprint = HasFlag("brood_imprint");
                    return Ch("brood_hatch", ChainBrood, 2, Loc.T("ДЕТЁНЫШИ"),
                        Loc.T("Из яиц в виварии вылупились детёныши — полупрозрачные, длиной с катер. Они питаются излучением реакторов ") +
                        Loc.T("и тянутся к кораблям, как к родне. ") + (imprint
                            ? Loc.T("Ксенолог, которого они считают матерью, уверен: их можно вырастить ручными.")
                            : Loc.T("Ксенологи спорят, можно ли приручить существ, которые вырастут больше крейсера.")),
                        AnomalyType.AlienFauna, Opts(
                            new EventOption(Loc.T("ВЫРАСТИТЬ ДЛЯ ФЛОТА"), imprint
                                ? Loc.T("−3 гелия-3 в месяц на 3 года. Затем стая войдёт в наш флот.")
                                : Loc.T("−3 гелия-3 в месяц на 3 года. Затем 70% — стая войдёт в наш флот, 30% — вырвется на волю."), () =>
                            {
                                EmpireEffects.Add("brood_feed", Loc.T("Выкармливание стаи"), ChainBrood, 36, energy: -3f);
                                Later("brood_grown", 1080, c, ChainBrood, Loc.T("Стая растёт в орбитальных загонах. Уже больше корвета."));
                            }),
                            new EventOption(Loc.T("ВЫПУСТИТЬ НА ВОЛЮ"), Loc.T("+20 влияния. Стая уйдёт к звёздам — может, ещё вернётся (≈ 4 года)."), () =>
                            {
                                Give(influence: 20f);
                                Later("brood_return", 1440, c, ChainBrood, Loc.T("Выпущенная стая кочует где-то в глубине галактики."));
                            }),
                            new EventOption(Loc.T("ПРОДАТЬ КОЛЛЕКЦИОНЕРАМ"), Loc.F("+{0} гелия-3.", R(250 * k)), () => Give(energy: 250 * k))));
                }
            });

            // III. Стая
            Add(new Template
            {
                Id = "brood_grown", When = Trigger.Chain,
                Build = c =>
                {
                    bool tame = HasFlag("brood_imprint") || Random.value < 0.7f;
                    if (tame)
                        return Ch("brood_grown", ChainBrood, 3, Loc.T("СТАЯ ПУСТОТЫ"),
                            Loc.T("Стая выросла. Живые корабли длиной с фрегат идут строем за флагманом и отвечают на команды пилотов-ксенологов. ") +
                            Loc.T("Их шкура держит залп лучше брони, а сами они ныряют в гиперкоридоры быстрее любого двигателя."),
                            AnomalyType.AlienFauna, Opts(
                                new EventOption(Loc.T("В СТРОЙ"), Loc.T("Два фрегата «Стая Пустоты» входят в наш флот."), () =>
                                {
                                    FreeWarship(ShipClass.Frigate, Loc.T("Стая Пустоты I"));
                                    FreeWarship(ShipClass.Frigate, Loc.T("Стая Пустоты II"));
                                }),
                                new EventOption(Loc.T("ПАСТИ У ГРАНИЦ"), Loc.T("+5 титана в месяц навсегда: стая очищает пояса астероидов."), () => Monthly(minerals: 5f))));

                    return Ch("brood_grown", ChainBrood, 3, Loc.T("ЗОВ КРОВИ"),
                        Loc.T("Стая не вынесла загонов. В одну ночь звери прорвали силовые барьеры и ушли в гиперкоридоры, а самый крупный ") +
                        Loc.T("остался у наших границ — и больше не подпускает корабли."),
                        AnomalyType.AlienFauna, Opts(
                            new EventOption(Loc.T("ОХОТА"), Loc.T("У границ появится молодой левиафан. Победа принесёт награду."), () =>
                            {
                                ThreatManager.Instance?.AwakenLeviathan(0, Loc.T("Дикая стая"), 0.8f);
                            }),
                            new EventOption(Loc.T("ОСТАВИТЬ В ПОКОЕ"), Loc.T("+15 влияния: зверь уходит, колонисты рассказывают о нём легенды."), () => Give(influence: 15f))));
                }
            });

            // II'. Дикое стадо
            Add(new Template
            {
                Id = "brood_wild", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    var council = Leaders?.CouncilScientist(0);
                    float shoo = 80f * k;
                    return Ch("brood_wild", ChainBrood, 2, Loc.T("ПАСТБИЩЕ ЛЕВИАФАНОВ"),
                        Loc.T("Кладка, оставленная в покое, вылупилась. Молодые левиафаны выросли и двинулись по гиперкоридорам — прямо через наши ") +
                        Loc.T("системы. Пока они просто пасутся у звёзд, но торговые суда уже обходят их стороной."),
                        AnomalyType.AlienFauna, Opts(
                            new EventOption(Loc.T("ПАСТИ СТАДО"), Loc.T("+5 титана в месяц и +1 влияния в месяц навсегда: стадо очищает пояса астероидов."), () =>
                            {
                                Monthly(minerals: 5f, influence: 1f);
                                if (council != null) Leaders?.GrantXp(council, 40f);
                            }, Loc.T("Учёный в научном совете"), () => council != null),
                            new EventOption(Loc.T("ОТОГНАТЬ"), Loc.F("−{0} гелия-3. Стадо уйдёт.", R(shoo)), () => Pay(energy: shoo), Loc.F("Нужно {0} гелия-3", R(shoo)), () => CanPay(energy: shoo)),
                            new EventOption(Loc.T("ОХОТА НА ЛЕВИАФАНОВ"), Loc.F("+{0} сплавов. Но у стада есть мать…", R(250 * k)), () =>
                            {
                                Give(alloys: 250 * k);
                                Later("brood_mother", 720, c, ChainBrood, Loc.T("Охотники хвастаются трофеями. Дальние датчики видят движение у края галактики."));
                            })));
                }
            });

            // III'. Мать
            Add(new Template
            {
                Id = "brood_mother", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    float tribute = 200f * k;
                    return Ch("brood_mother", ChainBrood, 3, Loc.T("МАТЬ ВЕРНУЛАСЬ"),
                        Loc.T("Она пришла. Левиафан размером с луну вышел из гиперкоридора у наших границ и замер, развернувшись к системе, ") +
                        Loc.T("где мы охотились на её детей. Учёные говорят, что она помнит запах наших кораблей."),
                        AnomalyType.AlienFauna, Opts(
                            new EventOption(Loc.T("К БОЮ"), Loc.T("Пробудится левиафан «Матерь Пустоты» — сильнее обычного. Победа принесёт большую награду."), () =>
                            {
                                int sys = ThreatManager.Instance != null ? ThreatManager.Instance.AwakenLeviathan(0, Loc.T("Матерь Пустоты"), 1.3f) : -1;
                                var s = EmpireStats.GetSystem(sys);
                                if (s != null) NotificationCenter.Show(Loc.T("Матерь Пустоты"), Loc.F("Левиафан занял систему {0}", s.Name), NotificationCenter.Kind.Danger, 8f);
                            }),
                            new EventOption(Loc.T("ВЕРНУТЬ ТРОФЕИ"), Loc.F("−{0} сплавов: скорлупа и кости уходят в пустоту. Мать уходит.", R(tribute)), () => Pay(alloys: tribute),
                                Loc.F("Нужно {0} сплавов", R(tribute)), () => CanPay(alloys: tribute))));
                }
            });

            // III''. Возвращение
            Add(new Template
            {
                Id = "brood_return", When = Trigger.Chain,
                Build = c => Ch("brood_return", ChainBrood, 3, Loc.T("ВОЗВРАЩЕНИЕ СТАИ"),
                    Loc.T("Через годы выпущенная стая вернулась — огромная, с новыми детёнышами. Звери встали на орбитах наших пограничных систем ") +
                    Loc.T("и не подпускают к ним чужие корабли. Колонисты зовут их «хранителями»."),
                    AnomalyType.AlienFauna, Opts(
                        new EventOption(Loc.T("ХРАНИТЕЛИ ГРАНИЦ"), Loc.T("+2 влияния в месяц навсегда: соседи уважают империю, которую охраняют левиафаны."), () => Monthly(influence: 2f)),
                        new EventOption(Loc.T("ИЗУЧАТЬ ПОТОМСТВО"), Loc.T("+5 науки в месяц навсегда."), () => TechnologyManager.Instance?.AddMonthlyScience(5f))))
            });
        }
    }
}
