using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using Random = UnityEngine.Random;

namespace StellarisClone.Core
{
    /// <summary>
    /// Второй пакет событий: шпионаж (агенты в поле, кроты, торговцы досье), жизнь колоний,
    /// флот, находки разведки и дипломатия. Каждый вариант пишет цену и последствия цифрами.
    /// </summary>
    public static partial class EventDatabase
    {
        // ---- Контексты ----

        /// <summary>Агент игрока, внедрённый в живую империю ИИ (minCover — минимальное прикрытие).</summary>
        private static EventContext AgentCtx(float minCover)
        {
            var list = new List<Leader>();
            foreach (var a in EspionageManager.PlayerAgents())
                if (a.Post == LeaderPost.Spy && a.TargetOwner > 0 && a.Cover >= minCover && AIEmpireManager.For(a.TargetOwner) != null)
                    list.Add(a);
            var pick = RandomOf(list);
            if (pick == null) return null;
            return new EventContext { Leader = pick, Rival = pick.TargetOwner, System = EmpireStats.GetSystem(pick.TargetSystem) };
        }

        /// <summary>Колония игрока со свободным жильём минимум на free мест.</summary>
        private static EventContext HousingCtx(int free)
        {
            var list = new List<PlanetData>();
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != 0) continue;
                foreach (var p in s.Planets)
                    if (p.Population > 0 && p.HousingCapacity - p.Population >= free) list.Add(p);
            }
            var pick = RandomOf(list);
            if (pick == null) return null;
            return new EventContext { Planet = pick, System = pick.ParentSystem, Leader = Leaders?.GovernorOf(pick) };
        }

        /// <summary>Поселить n жителей в любые колонии со свободным жильём; вернуть, сколько поселилось.</summary>
        private static int Settle(int n)
        {
            int done = 0;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != 0) continue;
                foreach (var p in s.Planets)
                    while (done < n && p.Population > 0 && p.Population < p.HousingCapacity) { p.Population++; done++; }
            }
            if (done > 0) Recalc();
            return done;
        }

        private static int FreeHousing()
        {
            int free = 0;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != 0) continue;
                foreach (var p in s.Planets) if (p.Population > 0) free += Mathf.Max(0, p.HousingCapacity - p.Population);
            }
            return free;
        }

        private static string Pct(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        private static void AddMoreEvents()
        {
            AddSpyEvents();
            AddMoreColonyEvents();
            AddMoreMilitaryEvents();
            AddMoreSurveyEvents();
            AddMoreDiplomaticEvents();
        }

        // ==================== ШПИОНАЖ ====================

        private static void AddSpyEvents()
        {
            Add(new Template
            {
                Id = "spy_intercept", When = Trigger.Monthly, Weight = 0.7f,
                FindContext = () => AgentCtx(EspionageManager.IntelCover),
                Build = c =>
                {
                    float k = K();
                    string rival = RivalName(c.Rival);
                    return Ev("spy_intercept", "ПЕРЕХВАЧЕННАЯ ДЕПЕША",
                        $"{c.Leader.Name} перехватил шифрованную депешу из канцелярии {rival}. Ключ к шифру у нас есть, " +
                        "но ненадолго: утром курьер хватится пропажи. Решать, что делать с письмом, нужно сейчас.",
                        AnomalyType.Diplomatic, Opts(
                            new EventOption("РАСШИФРОВАТЬ", $"+{R(70 * k)} науки: в депеше — отчёты их лабораторий. Агент получает опыт.", () =>
                            {
                                Science(70 * k); Xp(c, 25f);
                            }),
                            new EventOption("ВЕРНУТЬ ПОДДЕЛКУ", $"Разведсеть в {rival} +15%. Риск 20%: подлог раскроют — отношение −10.", () =>
                            {
                                EspionageManager.AdjustNetwork(c.Rival, 15f); Xp(c, 30f);
                                if (Random.value < 0.20f)
                                {
                                    Opinion(c.Rival, -10f);
                                    NotificationCenter.Show("Подлог раскрыт", $"{rival} знает о перехвате депеши", NotificationCenter.Kind.Warning, 6f);
                                }
                            }),
                            new EventOption("ПРОДАТЬ ТРЕТЬЕЙ СТОРОНЕ", $"+{R(90 * k)} гелия-3. Отношение {rival} −5: слухи дойдут.", () =>
                            {
                                Give(energy: 90 * k); Opinion(c.Rival, -5f);
                            })));
                }
            });

            Add(new Template
            {
                Id = "spy_double_agent", When = Trigger.Monthly, Weight = 0.6f,
                FindContext = () => AgentCtx(10f),
                Build = c =>
                {
                    string rival = RivalName(c.Rival);
                    bool sharp = c.Leader.Has("agt_shadow") || c.Leader.Level >= 3;
                    return Ev("spy_double_agent", "ЧИНОВНИК ПРЕДЛАГАЕТ УСЛУГИ",
                        $"Заместитель министра связи {rival} сам вышел на {c.Leader.Name}: долги, обида на начальство, " +
                        "желание уехать подальше. Он готов работать на нас — или это ловушка их контрразведки.",
                        AnomalyType.Diplomatic, Opts(
                            new EventOption("ЗАВЕРБОВАТЬ", "−30 влияния. 70%: разведсеть +25%. 30%: ловушка — сеть −20%, отношение −8.", () =>
                            {
                                Pay(influence: 30f);
                                if (Random.value < 0.70f) { EspionageManager.AdjustNetwork(c.Rival, 25f); Xp(c, 40f); }
                                else
                                {
                                    EspionageManager.AdjustNetwork(c.Rival, -20f); Opinion(c.Rival, -8f);
                                    NotificationCenter.Show("Это была ловушка", $"Контрразведка {rival} вскрыла часть нашей сети", NotificationCenter.Kind.Danger, 6f);
                                }
                            }, Need(influence: 30f), () => CanPay(influence: 30f)),
                            new EventOption("ПРОВЕРИТЬ И ВЗЯТЬ", "Бесплатно и без риска: опыт агента видит ловушку насквозь. Разведсеть +25%.", () =>
                            {
                                EspionageManager.AdjustNetwork(c.Rival, 25f); Xp(c, 50f);
                            }, $"Агент 3-го уровня или «{TraitName("agt_shadow")}»", () => sharp),
                            new EventOption("ОТКАЗАТЬ", "Ничего не меняется. 25%: обиженный чиновник донесёт — сеть −10%.", () =>
                            {
                                if (Random.value < 0.25f) EspionageManager.AdjustNetwork(c.Rival, -10f);
                            })));
                }
            });

            Add(new Template
            {
                Id = "spy_mole_found", When = Trigger.Monthly, Weight = 0.5f,
                FindContext = () => RivalCtx(needPeace: false),
                Build = c =>
                {
                    string rival = RivalName(c.Rival);
                    return Ev("spy_mole_found", "КРОТ В АДМИНИСТРАЦИИ",
                        $"Служба безопасности взяла с поличным архивариуса Министерства флота: он годами передавал {rival} " +
                        "графики верфей. Крот напуган и готов на сделку.",
                        AnomalyType.Diplomatic, Opts(
                            new EventOption("ПОКАЗАТЕЛЬНЫЙ СУД", $"+25 влияния. Отношение {rival} −15.", () =>
                            {
                                Give(influence: 25f); Opinion(c.Rival, -15f);
                            }),
                            new EventOption("КОРМИТЬ ДЕЗИНФОРМАЦИЕЙ", "Через 3 месяца — итог игры. 65%: разведсеть у них +30% и наука; 35%: крот поймёт и сбежит.", () =>
                            {
                                Schedule("spy_mole_result", 90, new EventContext { Rival = c.Rival });
                            }),
                            new EventOption("ТИХО ВЫСЛАТЬ", $"Отношение {rival} +5: они оценят молчание.", () => Opinion(c.Rival, 5f))));
                }
            });

            Add(new Template
            {
                Id = "spy_mole_result", When = Trigger.Chain,
                CanFire = c => c != null && AIEmpireManager.For(c.Rival) != null,
                Build = c =>
                {
                    float k = K();
                    string rival = RivalName(c.Rival);
                    bool ok = Random.value < 0.65f;
                    if (ok)
                        return Ev("spy_mole_result", "ДЕЗИНФОРМАЦИЯ СРАБОТАЛА",
                            $"Три месяца {rival} строил планы по нашим фальшивкам. Их штаб перебросил силы не туда, а связные крота " +
                            "вывели нас на собственную резидентуру противника.",
                            AnomalyType.Diplomatic, Opts(
                                new EventOption("ЗАБРАТЬ ИХ СЕТЬ", $"Разведсеть в {rival} +30%, +{R(60 * k)} науки.", () =>
                                {
                                    EspionageManager.AdjustNetwork(c.Rival, 30f); Science(60 * k);
                                }),
                                new EventOption("ОБМЕНЯТЬ КРОТА", $"+40 влияния и отношение {rival} +5: тихий обмен пленными.", () =>
                                {
                                    Give(influence: 40f); Opinion(c.Rival, 5f);
                                })));
                    return Ev("spy_mole_result", "КРОТ СБЕЖАЛ",
                        $"Крот почуял игру и ушёл через границу. {rival} теперь знает, что мы его вели, и в ярости.",
                        AnomalyType.Diplomatic, Opts(
                            new EventOption("ПРИЗНАТЬ ПРОВАЛ", $"Отношение {rival} −10, −15 влияния.", () =>
                            {
                                Opinion(c.Rival, -10f); Give(influence: -15f);
                            }),
                            new EventOption("ВСЁ ОТРИЦАТЬ", $"Отношение {rival} −20, влияние сохранено.", () => Opinion(c.Rival, -20f))));
                }
            });

            Add(new Template
            {
                Id = "spy_agent_compromised", When = Trigger.Monthly, Weight = 0.5f,
                FindContext = () => AgentCtx(10f),
                Build = c =>
                {
                    float k = K();
                    string rival = RivalName(c.Rival);
                    float bribe = R(60 * k);
                    return Ev("spy_agent_compromised", "АГЕНТ ПОД ПОДОЗРЕНИЕМ",
                        $"Связной {c.Leader.Name} не вышел на встречу, а у конспиративной квартиры замечена машина контрразведки {rival}. " +
                        "Возможно, это совпадение. Возможно — арест через несколько часов.",
                        AnomalyType.Diplomatic, Opts(
                            new EventOption("ЭВАКУИРОВАТЬ СВЯЗИ", "Агент в безопасности, но разведсеть −25%.", () =>
                            {
                                EspionageManager.AdjustNetwork(c.Rival, -25f);
                            }),
                            new EventOption("ЗАТАИТЬСЯ", $"50%: обошлось, агент +40 опыта. 50%: агент арестован и потерян, отношение {rival} −10.", () =>
                            {
                                if (Random.value < 0.5f) Xp(c, 40f);
                                else
                                {
                                    Opinion(c.Rival, -10f);
                                    Leaders?.Retire(c.Leader, "Агент арестован", $"{c.Leader.Name} схвачен контрразведкой {rival}");
                                }
                            }),
                            new EventOption("ПОДКУПИТЬ ПОЛИЦИЮ", $"−{bribe} гелия-3 и −15 влияния: дело закроют, сеть цела.", () =>
                            {
                                Pay(energy: bribe, influence: 15f);
                            }, Need(energy: bribe, influence: 15f), () => CanPay(energy: bribe, influence: 15f))));
                }
            });

            Add(new Template
            {
                Id = "spy_info_broker", When = Trigger.Monthly, Weight = 0.5f,
                FindContext = () => RivalCtx(needPeace: false),
                Build = c =>
                {
                    float k = K();
                    string rival = RivalName(c.Rival);
                    float price = R(80 * k);
                    return Ev("spy_info_broker", "ТОРГОВЕЦ ДОСЬЕ",
                        "На станции у границы появился посредник, торгующий чужими секретами. Его каталог — шифры, имена " +
                        $"осведомителей и схемы постов. Самое ценное — досье на {rival}.",
                        AnomalyType.Diplomatic, Opts(
                            new EventOption("КУПИТЬ ДОСЬЕ", $"−{price} гелия-3: разведсеть в {rival} +20% (нет сети — зацепка в их столице).", () =>
                            {
                                Pay(energy: price); EspionageManager.AdjustNetwork(c.Rival, 20f);
                            }, Need(energy: price), () => CanPay(energy: price)),
                            new EventOption("КУПИТЬ ВЕСЬ КАТАЛОГ", "−40 влияния: разведсеть +10% во всех империях.", () =>
                            {
                                Pay(influence: 40f);
                                foreach (var ai in AIEmpireManager.Alive) EspionageManager.AdjustNetwork(ai.OwnerId, 10f);
                            }, Need(influence: 40f), () => CanPay(influence: 40f)),
                            new EventOption("СДАТЬ ПОСРЕДНИКА", $"+15 влияния, отношение {rival} +5. Каталог уйдёт на склад улик.", () =>
                            {
                                Give(influence: 15f); Opinion(c.Rival, 5f);
                            })));
                }
            });
        }

        // ==================== КОЛОНИИ ====================

        private static void AddMoreColonyEvents()
        {
            Add(new Template
            {
                Id = "colony_refugees", When = Trigger.Monthly, Weight = 0.6f,
                FindContext = () =>
                {
                    var c = HousingCtx(2);
                    var r = RivalCtx(needPeace: false);
                    if (c == null || r == null) return null;
                    c.Rival = r.Rival;
                    return c;
                },
                Build = c =>
                {
                    float k = K();
                    string rival = RivalName(c.Rival);
                    float cost = R(40 * k);
                    return Ev("colony_refugees", "КОРАБЛЬ БЕЖЕНЦЕВ",
                        $"На орбиту {c.Planet.Name} вышел перегруженный транспорт из пограничья {rival}. На борту — семьи шахтёров " +
                        "и инженеры, бегущие от голода. Свободное жильё на планете есть.",
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption("ПРИНЯТЬ ВСЕХ", $"−{cost} гелия-3 на обустройство: +2 населения на {c.Planet.Name}. Отношение {rival} −5.", () =>
                            {
                                Pay(energy: cost);
                                c.Planet.Population = Mathf.Min(c.Planet.HousingCapacity, c.Planet.Population + 2);
                                Opinion(c.Rival, -5f); Recalc();
                            }, Need(energy: cost), () => CanPay(energy: cost)),
                            new EventOption("ТОЛЬКО ИНЖЕНЕРОВ", $"+1 население и +{R(50 * k)} науки.", () =>
                            {
                                c.Planet.Population = Mathf.Min(c.Planet.HousingCapacity, c.Planet.Population + 1);
                                Science(50 * k); Recalc();
                            }),
                            new EventOption("ВЕРНУТЬ ДОМОЙ", $"Отношение {rival} +8: они ценят порядок на границе.", () => Opinion(c.Rival, 8f))));
                }
            });

            Add(new Template
            {
                Id = "colony_strike", When = Trigger.Monthly, Weight = 0.6f,
                FindContext = () => ColonyCtx(4),
                Build = c =>
                {
                    float k = K();
                    float pay = R(60 * k);
                    return Ev("colony_strike", "ЗАБАСТОВКА В ШАХТАХ",
                        $"Шахтёры {c.Planet.Name} остановили конвейеры: смены по 14 часов, пыль в лёгких, обещанные премии так и не пришли. " +
                        "Профсоюз ждёт ответа губернатора.",
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption("ВЫПЛАТИТЬ ПРЕМИИ", $"−{pay} гелия-3: работа возобновится, +10 влияния.", () =>
                            {
                                Pay(energy: pay); Give(influence: 10f);
                            }, Need(energy: pay), () => CanPay(energy: pay)),
                            new EventOption("КОРОТКАЯ СМЕНА", $"Бесплатно сейчас, но 12 месяцев: −{R(3 * k)} титана в месяц, +1 влияния в месяц.", () =>
                                EmpireEffects.Add("colony_strike", "Короткая смена", $"Забастовка на {c.Planet.Name}", 12, minerals: -3 * k, influence: 1f)),
                            new EventOption("РАЗОГНАТЬ", "Бесплатно, но колония теряет 1 население: недовольные улетят.", () =>
                            {
                                c.Planet.Population = Mathf.Max(1, c.Planet.Population - 1); Recalc();
                            }),
                            new EventOption("ДОГОВОРИТЬСЯ ЛИЧНО", "Бесплатно: губернатор уговорит профсоюз. +10 влияния, губернатор получает опыт.", () =>
                            {
                                Give(influence: 10f); Xp(c, 40f);
                            }, $"Губернатор-{TraitName("gov_admin").ToLower()} или «{TraitName("gov_honest")}»",
                               () => LeaderHas(c, "gov_admin") || LeaderHas(c, "gov_honest"))));
                }
            });

            Add(new Template
            {
                Id = "colony_meteor", When = Trigger.Monthly, Weight = 0.5f,
                FindContext = () =>
                {
                    var c = ColonyCtx(1);
                    return c != null && c.Planet.Districts.Count >= 1 ? c : null;
                },
                Build = c =>
                {
                    float k = K();
                    float alloys = R(50 * k), energy = R(40 * k);
                    return Ev("colony_meteor", "БОЛИД НА ПОДЛЁТЕ",
                        $"Обсерватория {c.Planet.Name} засекла каменную глыбу размером с квартал. Расчётная точка падения — " +
                        "в сорока километрах от жилых районов, но погрешность велика. До удара шесть дней.",
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption("СБИТЬ РАКЕТАМИ", $"−{alloys} сплавов: угроза устранена, обломки дадут +{R(60 * k)} титана.", () =>
                            {
                                Pay(alloys: alloys); Give(minerals: 60 * k);
                            }, Need(alloys: alloys), () => CanPay(alloys: alloys)),
                            new EventOption("ЭВАКУИРОВАТЬ", $"−{energy} гелия-3: никто не пострадает, район уцелеет.", () => Pay(energy: energy),
                                Need(energy: energy), () => CanPay(energy: energy)),
                            new EventOption("ПОЛОЖИТЬСЯ НА РАСЧЁТ", $"60%: пролетит мимо, +{R(120 * k)} титана из кратера. 40%: удар — −1 население и −1 район.", () =>
                            {
                                if (Random.value < 0.6f)
                                {
                                    Give(minerals: 120 * k);
                                    NotificationCenter.Show("Болид упал в пустыне", "Кратер полон руды", NotificationCenter.Kind.Success, 5f);
                                }
                                else
                                {
                                    c.Planet.Population = Mathf.Max(1, c.Planet.Population - 1);
                                    if (c.Planet.Districts.Count > 0) c.Planet.Districts.RemoveAt(c.Planet.Districts.Count - 1);
                                    Recalc();
                                    NotificationCenter.Show("Удар болида", $"{c.Planet.Name} потеряла район", NotificationCenter.Kind.Danger, 6f);
                                }
                            })));
                }
            });

            Add(new Template
            {
                Id = "colony_young_scientists", When = Trigger.Monthly, Weight = 0.6f,
                FindContext = () => ColonyCtx(3),
                Build = c =>
                {
                    float k = K();
                    float cost = R(80 * k);
                    return Ev("colony_young_scientists", "КРУЖОК ЮНЫХ АСТРОФИЗИКОВ",
                        $"Школьники {c.Planet.Name} собрали из списанных деталей радиотелескоп и открыли две кометы. " +
                        "Учителя просят деньги на настоящую академию.",
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption("ОТКРЫТЬ АКАДЕМИЮ", $"−{cost} гелия-3: +3 науки в месяц навсегда — за 2 года это 72 науки, дальше — чистый плюс.", () =>
                            {
                                Pay(energy: cost); TechnologyManager.Instance?.AddMonthlyScience(3f);
                            }, Need(energy: cost), () => CanPay(energy: cost)),
                            new EventOption("ВЗЯТЬ В ЭКСПЕДИЦИЮ", "Лучшие ученики полетят с нашим учёным: учёный получает +60 опыта.", () =>
                            {
                                foreach (var l in PlayerLeaders(LeaderClass.Scientist)) { Leaders.GrantXp(l, 60f); break; }
                            }, "Есть учёный", () =>
                            {
                                foreach (var l in PlayerLeaders(LeaderClass.Scientist)) return true;
                                return false;
                            }),
                            new EventOption("ВРУЧИТЬ ГРАМОТЫ", $"+{R(30 * k)} науки: каталог комет пригодится.", () => Science(30 * k))));
                }
            });
        }

        // ==================== ФЛОТ ====================

        private static void AddMoreMilitaryEvents()
        {
            Add(new Template
            {
                Id = "military_deserters", When = Trigger.Monthly, Weight = 0.4f,
                FindContext = () =>
                {
                    var c = WarshipCtx();
                    var r = RivalCtx(needPeace: false);
                    if (c == null || r == null) return null;
                    c.Rival = r.Rival;
                    return c;
                },
                Build = c =>
                {
                    float k = K();
                    string rival = RivalName(c.Rival);
                    return Ev("military_deserters", "ПЕРЕБЕЖЧИКИ НА КОРВЕТЕ",
                        $"К {c.Ship.Name} подошёл корвет {rival} с выключенным оружием. Капитан просит убежища для экипажа — " +
                        "и предлагает корабль в качестве платы.",
                        AnomalyType.MilitaryIncident, Opts(
                            new EventOption("ПРИНЯТЬ С КОРАБЛЁМ", $"+1 корвет во флот. Отношение {rival} −20.", () =>
                            {
                                int sys = SpawnSystem(c);
                                if (sys >= 0) FleetManager.Instance?.CreateFleetObject($"Корвет «Свобода» {FleetManager.Instance.AllFleets.Count + 1}", sys, FleetType.Military);
                                Opinion(c.Rival, -20f);
                            }),
                            new EventOption("РАЗОБРАТЬ, ЛЮДЕЙ ОСТАВИТЬ", $"+{R(100 * k)} сплавов. Отношение {rival} −8.", () =>
                            {
                                Give(alloys: 100 * k); Opinion(c.Rival, -8f);
                            }),
                            new EventOption("ВЕРНУТЬ ВЛАДЕЛЬЦАМ", $"+30 влияния и отношение {rival} +12.", () =>
                            {
                                Give(influence: 30f); Opinion(c.Rival, 12f);
                            })));
                }
            });

            Add(new Template
            {
                Id = "military_ghost_contact", When = Trigger.Monthly, Weight = 0.5f,
                FindContext = WarshipCtx,
                Build = c =>
                {
                    float k = K();
                    bool skilled = LeaderHas(c, "adm_tactician") || LeaderHas(c, "adm_cautious") || LeaderHas(c, "adm_veteran");
                    return Ev("military_ghost_contact", "КОНТАКТ БЕЗ СИГНАТУРЫ",
                        $"Радары {c.Ship.Name} трижды за сутки поймали отражение там, где пусто. Объект держится на краю дальности " +
                        "и уходит, когда корабль поворачивает к нему. Маскировка, какой у нас нет.",
                        AnomalyType.MilitaryIncident, Opts(
                            new EventOption("ПРЕСЛЕДОВАТЬ", $"50%: захвачен зонд-невидимка, +{R(150 * k)} науки. 50%: засада — корабль теряет 30% корпуса.", () =>
                            {
                                if (Random.value < 0.5f) { Science(150 * k); Xp(c, 30f); }
                                else
                                {
                                    DamageShip(c.Ship, 0.3f);
                                    NotificationCenter.Show("Засада", $"{c.Ship.Name} попал под огонь невидимки", NotificationCenter.Kind.Danger, 5f);
                                }
                            }),
                            new EventOption("ВЫСТАВИТЬ ЛОВУШКУ", $"Без риска: +{R(150 * k)} науки, адмирал +50 опыта.", () =>
                            {
                                Science(150 * k); Xp(c, 50f);
                            }, $"Адмирал «{TraitName("adm_tactician")}», «{TraitName("adm_cautious")}» или «{TraitName("adm_veteran")}»", () => skilled),
                            new EventOption("ЗАПИСАТЬ И УЙТИ", $"+{R(40 * k)} науки: записи датчиков изучат на базе.", () => Science(40 * k))));
                }
            });
        }

        // ==================== РАЗВЕДКА ====================

        private static void AddMoreSurveyEvents()
        {
            Add(new Template
            {
                Id = "survey_frozen_colony", When = Trigger.Survey, Weight = 0.7f, Once = true,
                CanFire = c => c?.System != null,
                Build = c =>
                {
                    float k = K();
                    int free = FreeHousing();
                    return Ev("survey_frozen_colony", "ЗАМЁРЗШИЙ КОВЧЕГ",
                        "На ледяной луне лежит колониальный корабль, разбитый три века назад. Половина криокапсул ещё работает: " +
                        "датчики видят пульс у двух сотен спящих. Их язык — древний диалект нашего.",
                        AnomalyType.DistressSignal, Opts(
                            new EventOption("РАЗБУДИТЬ И ПОСЕЛИТЬ", $"+3 населения в колонии со свободным жильём (сейчас свободно мест: {free}). " +
                                $"Если мест нет — +{R(60 * k)} влияния за спасение.", () =>
                            {
                                int done = Settle(3);
                                if (done == 0) Give(influence: 60 * k);
                                Xp(c, 30f);
                            }),
                            new EventOption("ИЗУЧИТЬ КРИОКАПСУЛЫ", $"+{R(140 * k)} науки. Спящих оставят в стазисе.", () =>
                            {
                                Science(140 * k); Xp(c, 20f);
                            }),
                            new EventOption("РАЗОБРАТЬ КОРПУС", $"+{R(90 * k)} сплавов.", () => Give(alloys: 90 * k))));
                }
            });

            Add(new Template
            {
                Id = "survey_gas_harvest", When = Trigger.Survey, Weight = 0.8f,
                CanFire = c => c?.System != null,
                Build = c =>
                {
                    float k = K();
                    return Ev("survey_gas_harvest", "ГЕЛИЕВЫЕ ШТОРМЫ",
                        "В верхних слоях газового гиганта бушуют штормы, богатые гелием-3. Концентрация втрое выше обычной, " +
                        "но вихри непредсказуемы: черпать придётся осторожно.",
                        AnomalyType.AlienFauna, Opts(
                            new EventOption("ВЗЯТЬ ПРОБЫ СЕЙЧАС", $"+{R(130 * k)} гелия-3 сразу.", () => Give(energy: 130 * k)),
                            new EventOption("ПОСТАВИТЬ ЗОНДЫ-ЧЕРПАКИ", $"+{R(3 * k)} гелия-3 в месяц на 24 месяца (≈{R(72 * k)} всего, выгоднее, но дольше).", () =>
                                EmpireEffects.Add("gas_harvest", "Зонды-черпаки", "Гелиевые штормы", 24, energy: 3 * k)),
                            new EventOption("ИЗУЧИТЬ ВИХРИ", "+2 науки в месяц навсегда: учёный составит модель штормов.", () =>
                            {
                                TechnologyManager.Instance?.AddMonthlyScience(2f); Xp(c, 30f);
                            }, $"Учёный-{TraitName("sci_analyst").ToLower()} или «{TraitName("sci_brilliant")}»",
                               () => LeaderHas(c, "sci_analyst") || LeaderHas(c, "sci_brilliant"))));
                }
            });

            Add(new Template
            {
                Id = "survey_ancient_battlefield", When = Trigger.Survey, Weight = 0.6f,
                CanFire = c => c?.System != null,
                Build = c =>
                {
                    float k = K();
                    return Ev("survey_ancient_battlefield", "ПОЛЕ ДРЕВНЕЙ БИТВЫ",
                        "Тысячи обломков кружат вокруг звезды: здесь когда-то сошлись два флота, и ни один не ушёл. " +
                        "Между корпусами дрейфуют неразорвавшиеся мины.",
                        AnomalyType.AncientDerelict, Opts(
                            new EventOption("СОБРАТЬ ОБЛОМКИ", $"+{R(200 * k)} сплавов. Риск 25%: мина — корабль теряет 40% корпуса.", () =>
                            {
                                Give(alloys: 200 * k);
                                if (Random.value < 0.25f)
                                {
                                    DamageShip(c?.Ship, 0.4f);
                                    NotificationCenter.Show("Подрыв на мине", "Научное судно повреждено", NotificationCenter.Kind.Danger, 5f);
                                }
                            }),
                            new EventOption("ВОССТАНОВИТЬ ХОД БОЯ", "Все наши адмиралы получают +60 опыта: тактика древних поучительна.", () =>
                            {
                                foreach (var l in PlayerLeaders(LeaderClass.Admiral)) Leaders.GrantXp(l, 60f);
                                Xp(c, 20f);
                            }, "Есть адмирал", () =>
                            {
                                foreach (var l in PlayerLeaders(LeaderClass.Admiral)) return true;
                                return false;
                            }),
                            new EventOption("ОСТАВИТЬ МЕМОРИАЛЬНЫЙ БУЙ", "+25 влияния: история облетит новостные каналы.", () => Give(influence: 25f))));
                }
            });
        }

        // ==================== ДИПЛОМАТИЯ ====================

        private static void AddMoreDiplomaticEvents()
        {
            Add(new Template
            {
                Id = "diplo_summit", When = Trigger.Monthly, Weight = 0.4f,
                FindContext = () => RivalCtx(needPeace: true),
                Build = c =>
                {
                    string rival = RivalName(c.Rival);
                    return Ev("diplo_summit", "ГАЛАКТИЧЕСКИЙ САММИТ",
                        $"{rival} созывает саммит на нейтральной станции. Приглашены все известные империи: обсуждать будут " +
                        "торговые пути и границы. От состава нашей делегации зависит, кто будет говорить громче.",
                        AnomalyType.Diplomatic, Opts(
                            new EventOption("БОЛЬШАЯ ДЕЛЕГАЦИЯ", "−40 влияния: отношение всех империй +12.", () =>
                            {
                                Pay(influence: 40f); OpinionAll(12f);
                            }, Need(influence: 40f), () => CanPay(influence: 40f)),
                            new EventOption("ПОСЛАТЬ ПОСЛА", $"Бесплатно: отношение {rival} +8.", () => Opinion(c.Rival, 8f)),
                            new EventOption("БОЙКОТ", $"+15 влияния дома: «нас не купить». Отношение {rival} −12.", () =>
                            {
                                Give(influence: 15f); Opinion(c.Rival, -12f);
                            })));
                }
            });

            Add(new Template
            {
                Id = "diplo_rival_crisis", When = Trigger.Monthly, Weight = 0.4f,
                FindContext = () => RivalCtx(needPeace: true),
                Build = c =>
                {
                    float k = K();
                    string rival = RivalName(c.Rival);
                    float aid = R(100 * k);
                    return Ev("diplo_rival_crisis", "НЕУРОЖАЙ У СОСЕДЕЙ",
                        $"Грибковая гниль уничтожила гидропонные фермы {rival}. Их правительство просит помощи открыто — " +
                        "впервые за всю историю отношений.",
                        AnomalyType.Diplomatic, Opts(
                            new EventOption("ОТПРАВИТЬ ГРУЗ", $"−{aid} титана: отношение {rival} +25, +10 влияния.", () =>
                            {
                                Pay(minerals: aid); Opinion(c.Rival, 25f); Give(influence: 10f);
                            }, Need(minerals: aid), () => CanPay(minerals: aid)),
                            new EventOption("ГУМАНИТАРНАЯ МИССИЯ С «ВРАЧАМИ»", $"−{R(aid / 2f)} титана: отношение +10, разведсеть в {rival} +25%. Риск 15%: раскроют — отношение −20.", () =>
                            {
                                Pay(minerals: aid / 2f);
                                EspionageManager.AdjustNetwork(c.Rival, 25f);
                                Opinion(c.Rival, Random.value < 0.15f ? -20f : 10f);
                            }, Need(minerals: aid / 2f), () => CanPay(minerals: aid / 2f)),
                            new EventOption("ВЫРАЗИТЬ СОЧУВСТВИЕ", $"Бесплатно. Отношение {rival} −5: они запомнят.", () => Opinion(c.Rival, -5f))));
                }
            });
        }
    }
}
