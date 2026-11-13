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

        /// <summary>Вид беженцев — основатели империи соперника.</summary>
        private static Species RefugeeSpecies(EventContext c) => SpeciesRegistry.Founder(SpeciesRegistry.FactionKeyOf(c.Rival));

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
                    return Ev("spy_intercept", Loc.T("ПЕРЕХВАЧЕННАЯ ДЕПЕША"),
                        Loc.F("{0} перехватил шифрованную депешу из канцелярии {1}. Ключ к шифру у нас есть, ", c.Leader.Name, rival) +
                        Loc.T("но ненадолго: утром курьер хватится пропажи. Решать, что делать с письмом, нужно сейчас."),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("РАСШИФРОВАТЬ"), Loc.F("+{0} науки: в депеше — отчёты их лабораторий. Агент получает опыт.", R(70 * k)), () =>
                            {
                                Science(70 * k); Xp(c, 25f);
                            }),
                            new EventOption(Loc.T("ВЕРНУТЬ ПОДДЕЛКУ"), Loc.F("Разведсеть в {0} +15%. Риск 20%: подлог раскроют — отношение −10.", rival), () =>
                            {
                                EspionageManager.AdjustNetwork(c.Rival, 15f); Xp(c, 30f);
                                if (Random.value < 0.20f)
                                {
                                    Opinion(c.Rival, -10f);
                                    NotificationCenter.Show(Loc.T("Подлог раскрыт"), Loc.F("{0} знает о перехвате депеши", rival), NotificationCenter.Kind.Warning, 6f);
                                }
                            }),
                            new EventOption(Loc.T("ПРОДАТЬ ТРЕТЬЕЙ СТОРОНЕ"), Loc.F("+{0} гелия-3. Отношение {1} −5: слухи дойдут.", R(90 * k), rival), () =>
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
                    return Ev("spy_double_agent", Loc.T("ЧИНОВНИК ПРЕДЛАГАЕТ УСЛУГИ"),
                        Loc.F("Заместитель министра связи {0} сам вышел на {1}: долги, обида на начальство, ", rival, c.Leader.Name) +
                        Loc.T("желание уехать подальше. Он готов работать на нас — или это ловушка их контрразведки."),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("ЗАВЕРБОВАТЬ"), Loc.T("−30 влияния. 70%: разведсеть +25%. 30%: ловушка — сеть −20%, отношение −8."), () =>
                            {
                                Pay(influence: 30f);
                                if (Random.value < 0.70f) { EspionageManager.AdjustNetwork(c.Rival, 25f); Xp(c, 40f); }
                                else
                                {
                                    EspionageManager.AdjustNetwork(c.Rival, -20f); Opinion(c.Rival, -8f);
                                    NotificationCenter.Show(Loc.T("Это была ловушка"), Loc.F("Контрразведка {0} вскрыла часть нашей сети", rival), NotificationCenter.Kind.Danger, 6f);
                                }
                            }, Need(influence: 30f), () => CanPay(influence: 30f)),
                            new EventOption(Loc.T("ПРОВЕРИТЬ И ВЗЯТЬ"), Loc.T("Бесплатно и без риска: опыт агента видит ловушку насквозь. Разведсеть +25%."), () =>
                            {
                                EspionageManager.AdjustNetwork(c.Rival, 25f); Xp(c, 50f);
                            }, Loc.F("Агент 3-го уровня или «{0}»", TraitName("agt_shadow")), () => sharp),
                            new EventOption(Loc.T("ОТКАЗАТЬ"), Loc.T("Ничего не меняется. 25%: обиженный чиновник донесёт — сеть −10%."), () =>
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
                    return Ev("spy_mole_found", Loc.T("КРОТ В АДМИНИСТРАЦИИ"),
                        Loc.F("Служба безопасности взяла с поличным архивариуса Министерства флота: он годами передавал {0} ", rival) +
                        Loc.T("графики верфей. Крот напуган и готов на сделку."),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("ПОКАЗАТЕЛЬНЫЙ СУД"), Loc.F("+25 влияния. Отношение {0} −15.", rival), () =>
                            {
                                Give(influence: 25f); Opinion(c.Rival, -15f);
                            }),
                            new EventOption(Loc.T("КОРМИТЬ ДЕЗИНФОРМАЦИЕЙ"), Loc.T("Через 3 месяца — итог игры. 65%: разведсеть у них +30% и наука; 35%: крот поймёт и сбежит."), () =>
                            {
                                Schedule("spy_mole_result", 90, new EventContext { Rival = c.Rival });
                            }),
                            new EventOption(Loc.T("ТИХО ВЫСЛАТЬ"), Loc.F("Отношение {0} +5: они оценят молчание.", rival), () => Opinion(c.Rival, 5f))));
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
                        return Ev("spy_mole_result", Loc.T("ДЕЗИНФОРМАЦИЯ СРАБОТАЛА"),
                            Loc.F("Три месяца {0} строил планы по нашим фальшивкам. Их штаб перебросил силы не туда, а связные крота ", rival) +
                            Loc.T("вывели нас на собственную резидентуру противника."),
                            AnomalyType.Diplomatic, Opts(
                                new EventOption(Loc.T("ЗАБРАТЬ ИХ СЕТЬ"), Loc.F("Разведсеть в {0} +30%, +{1} науки.", rival, R(60 * k)), () =>
                                {
                                    EspionageManager.AdjustNetwork(c.Rival, 30f); Science(60 * k);
                                }),
                                new EventOption(Loc.T("ОБМЕНЯТЬ КРОТА"), Loc.F("+40 влияния и отношение {0} +5: тихий обмен пленными.", rival), () =>
                                {
                                    Give(influence: 40f); Opinion(c.Rival, 5f);
                                })));
                    return Ev("spy_mole_result", Loc.T("КРОТ СБЕЖАЛ"),
                        Loc.F("Крот почуял игру и ушёл через границу. {0} теперь знает, что мы его вели, и в ярости.", rival),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("ПРИЗНАТЬ ПРОВАЛ"), Loc.F("Отношение {0} −10, −15 влияния.", rival), () =>
                            {
                                Opinion(c.Rival, -10f); Give(influence: -15f);
                            }),
                            new EventOption(Loc.T("ВСЁ ОТРИЦАТЬ"), Loc.F("Отношение {0} −20, влияние сохранено.", rival), () => Opinion(c.Rival, -20f))));
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
                    return Ev("spy_agent_compromised", Loc.T("АГЕНТ ПОД ПОДОЗРЕНИЕМ"),
                        Loc.F("Связной {0} не вышел на встречу, а у конспиративной квартиры замечена машина контрразведки {1}. ", c.Leader.Name, rival) +
                        Loc.T("Возможно, это совпадение. Возможно — арест через несколько часов."),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("ЭВАКУИРОВАТЬ СВЯЗИ"), Loc.T("Агент в безопасности, но разведсеть −25%."), () =>
                            {
                                EspionageManager.AdjustNetwork(c.Rival, -25f);
                            }),
                            new EventOption(Loc.T("ЗАТАИТЬСЯ"), Loc.F("50%: обошлось, агент +40 опыта. 50%: агент арестован и потерян, отношение {0} −10.", rival), () =>
                            {
                                if (Random.value < 0.5f) Xp(c, 40f);
                                else
                                {
                                    Opinion(c.Rival, -10f);
                                    Leaders?.Retire(c.Leader, Loc.T("Агент арестован"), Loc.F("{0} схвачен контрразведкой {1}", c.Leader.Name, rival));
                                }
                            }),
                            new EventOption(Loc.T("ПОДКУПИТЬ ПОЛИЦИЮ"), Loc.F("−{0} гелия-3 и −15 влияния: дело закроют, сеть цела.", bribe), () =>
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
                    return Ev("spy_info_broker", Loc.T("ТОРГОВЕЦ ДОСЬЕ"),
                        Loc.T("На станции у границы появился посредник, торгующий чужими секретами. Его каталог — шифры, имена ") +
                        Loc.F("осведомителей и схемы постов. Самое ценное — досье на {0}.", rival),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("КУПИТЬ ДОСЬЕ"), Loc.F("−{0} гелия-3: разведсеть в {1} +20% (нет сети — зацепка в их столице).", price, rival), () =>
                            {
                                Pay(energy: price); EspionageManager.AdjustNetwork(c.Rival, 20f);
                            }, Need(energy: price), () => CanPay(energy: price)),
                            new EventOption(Loc.T("КУПИТЬ ВЕСЬ КАТАЛОГ"), Loc.T("−40 влияния: разведсеть +10% во всех империях."), () =>
                            {
                                Pay(influence: 40f);
                                foreach (var ai in AIEmpireManager.Alive) EspionageManager.AdjustNetwork(ai.OwnerId, 10f);
                            }, Need(influence: 40f), () => CanPay(influence: 40f)),
                            new EventOption(Loc.T("СДАТЬ ПОСРЕДНИКА"), Loc.F("+15 влияния, отношение {0} +5. Каталог уйдёт на склад улик.", rival), () =>
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
                    return Ev("colony_refugees", Loc.T("КОРАБЛЬ БЕЖЕНЦЕВ"),
                        Loc.F("На орбиту {0} вышел перегруженный транспорт из пограничья {1}. На борту — семьи шахтёров ", c.Planet.Name, rival) +
                        Loc.T("и инженеры, бегущие от голода. Свободное жильё на планете есть."),
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption(Loc.T("ПРИНЯТЬ ВСЕХ"), Loc.F("−{0} гелия-3 на обустройство: +2 населения на {1} — чужой вид, {2}. Отношение {3} −5.", cost, c.Planet.Name, RefugeeSpecies(c).InText, rival), () =>
                            {
                                Pay(energy: cost);
                                c.Planet.AddPops(RefugeeSpecies(c).Id, Mathf.Clamp(c.Planet.HousingCapacity - c.Planet.Population, 0, 2));
                                Opinion(c.Rival, -5f); Recalc();
                            }, Need(energy: cost), () => CanPay(energy: cost)),
                            new EventOption(Loc.T("ТОЛЬКО ИНЖЕНЕРОВ"), Loc.F("+1 население и +{0} науки.", R(50 * k)), () =>
                            {
                                if (c.Planet.Population < c.Planet.HousingCapacity) c.Planet.AddPops(RefugeeSpecies(c).Id, 1);
                                Science(50 * k); Recalc();
                            }),
                            new EventOption(Loc.T("ВЕРНУТЬ ДОМОЙ"), Loc.F("Отношение {0} +8: они ценят порядок на границе.", rival), () => Opinion(c.Rival, 8f))));
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
                    return Ev("colony_strike", Loc.T("ЗАБАСТОВКА В ШАХТАХ"),
                        Loc.F("Шахтёры {0} остановили конвейеры: смены по 14 часов, пыль в лёгких, обещанные премии так и не пришли. ", c.Planet.Name) +
                        Loc.T("Профсоюз ждёт ответа губернатора."),
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption(Loc.T("ВЫПЛАТИТЬ ПРЕМИИ"), Loc.F("−{0} гелия-3: работа возобновится, +10 влияния.", pay), () =>
                            {
                                Pay(energy: pay); Give(influence: 10f);
                            }, Need(energy: pay), () => CanPay(energy: pay)),
                            new EventOption(Loc.T("КОРОТКАЯ СМЕНА"), Loc.F("Бесплатно сейчас, но 12 месяцев: −{0} титана в месяц, +1 влияния в месяц.", R(3 * k)), () =>
                                EmpireEffects.Add("colony_strike", Loc.T("Короткая смена"), Loc.F("Забастовка на {0}", c.Planet.Name), 12, minerals: -3 * k, influence: 1f)),
                            new EventOption(Loc.T("РАЗОГНАТЬ"), Loc.T("Бесплатно, но колония теряет 1 население: недовольные улетят."), () =>
                            {
                                c.Planet.Population = Mathf.Max(1, c.Planet.Population - 1); Recalc();
                            }),
                            new EventOption(Loc.T("ДОГОВОРИТЬСЯ ЛИЧНО"), Loc.T("Бесплатно: губернатор уговорит профсоюз. +10 влияния, губернатор получает опыт."), () =>
                            {
                                Give(influence: 10f); Xp(c, 40f);
                            }, Loc.F("Губернатор-{0} или «{1}»", TraitName("gov_admin").ToLower(), TraitName("gov_honest")),
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
                    return Ev("colony_meteor", Loc.T("БОЛИД НА ПОДЛЁТЕ"),
                        Loc.F("Обсерватория {0} засекла каменную глыбу размером с квартал. Расчётная точка падения — ", c.Planet.Name) +
                        Loc.T("в сорока километрах от жилых районов, но погрешность велика. До удара шесть дней."),
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption(Loc.T("СБИТЬ РАКЕТАМИ"), Loc.F("−{0} сплавов: угроза устранена, обломки дадут +{1} титана.", alloys, R(60 * k)), () =>
                            {
                                Pay(alloys: alloys); Give(minerals: 60 * k);
                            }, Need(alloys: alloys), () => CanPay(alloys: alloys)),
                            new EventOption(Loc.T("ЭВАКУИРОВАТЬ"), Loc.F("−{0} гелия-3: никто не пострадает, район уцелеет.", energy), () => Pay(energy: energy),
                                Need(energy: energy), () => CanPay(energy: energy)),
                            new EventOption(Loc.T("ПОЛОЖИТЬСЯ НА РАСЧЁТ"), Loc.F("60%: пролетит мимо, +{0} титана из кратера. 40%: удар — −1 население и −1 район.", R(120 * k)), () =>
                            {
                                if (Random.value < 0.6f)
                                {
                                    Give(minerals: 120 * k);
                                    NotificationCenter.Show(Loc.T("Болид упал в пустыне"), Loc.T("Кратер полон руды"), NotificationCenter.Kind.Success, 5f);
                                }
                                else
                                {
                                    c.Planet.Population = Mathf.Max(1, c.Planet.Population - 1);
                                    if (c.Planet.Districts.Count > 0) c.Planet.Districts.RemoveAt(c.Planet.Districts.Count - 1);
                                    Recalc();
                                    NotificationCenter.Show(Loc.T("Удар болида"), Loc.F("{0} потеряла район", c.Planet.Name), NotificationCenter.Kind.Danger, 6f);
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
                    return Ev("colony_young_scientists", Loc.T("КРУЖОК ЮНЫХ АСТРОФИЗИКОВ"),
                        Loc.F("Школьники {0} собрали из списанных деталей радиотелескоп и открыли две кометы. ", c.Planet.Name) +
                        Loc.T("Учителя просят деньги на настоящую академию."),
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption(Loc.T("ОТКРЫТЬ АКАДЕМИЮ"), Loc.F("−{0} гелия-3: +3 науки в месяц навсегда — за 2 года это 72 науки, дальше — чистый плюс.", cost), () =>
                            {
                                Pay(energy: cost); TechnologyManager.Instance?.AddMonthlyScience(3f);
                            }, Need(energy: cost), () => CanPay(energy: cost)),
                            new EventOption(Loc.T("ВЗЯТЬ В ЭКСПЕДИЦИЮ"), Loc.T("Лучшие ученики полетят с нашим учёным: учёный получает +60 опыта."), () =>
                            {
                                foreach (var l in PlayerLeaders(LeaderClass.Scientist)) { Leaders.GrantXp(l, 60f); break; }
                            }, Loc.T("Есть учёный"), () =>
                            {
                                foreach (var l in PlayerLeaders(LeaderClass.Scientist)) return true;
                                return false;
                            }),
                            new EventOption(Loc.T("ВРУЧИТЬ ГРАМОТЫ"), Loc.F("+{0} науки: каталог комет пригодится.", R(30 * k)), () => Science(30 * k))));
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
                    return Ev("military_deserters", Loc.T("ПЕРЕБЕЖЧИКИ НА КОРВЕТЕ"),
                        Loc.F("К {0} подошёл корвет {1} с выключенным оружием. Капитан просит убежища для экипажа — ", c.Ship.Name, rival) +
                        Loc.T("и предлагает корабль в качестве платы."),
                        AnomalyType.MilitaryIncident, Opts(
                            new EventOption(Loc.T("ПРИНЯТЬ С КОРАБЛЁМ"), Loc.F("+1 корвет во флот. Отношение {0} −20.", rival), () =>
                            {
                                int sys = SpawnSystem(c);
                                if (sys >= 0) FleetManager.Instance?.CreateFleetObject(Loc.F("Корвет «Свобода» {0}", FleetManager.Instance.AllFleets.Count + 1), sys, FleetType.Military);
                                Opinion(c.Rival, -20f);
                            }),
                            new EventOption(Loc.T("РАЗОБРАТЬ, ЛЮДЕЙ ОСТАВИТЬ"), Loc.F("+{0} сплавов. Отношение {1} −8.", R(100 * k), rival), () =>
                            {
                                Give(alloys: 100 * k); Opinion(c.Rival, -8f);
                            }),
                            new EventOption(Loc.T("ВЕРНУТЬ ВЛАДЕЛЬЦАМ"), Loc.F("+30 влияния и отношение {0} +12.", rival), () =>
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
                    return Ev("military_ghost_contact", Loc.T("КОНТАКТ БЕЗ СИГНАТУРЫ"),
                        Loc.F("Радары {0} трижды за сутки поймали отражение там, где пусто. Объект держится на краю дальности ", c.Ship.Name) +
                        Loc.T("и уходит, когда корабль поворачивает к нему. Маскировка, какой у нас нет."),
                        AnomalyType.MilitaryIncident, Opts(
                            new EventOption(Loc.T("ПРЕСЛЕДОВАТЬ"), Loc.F("50%: захвачен зонд-невидимка, +{0} науки. 50%: засада — корабль теряет 30% корпуса.", R(150 * k)), () =>
                            {
                                if (Random.value < 0.5f) { Science(150 * k); Xp(c, 30f); }
                                else
                                {
                                    DamageShip(c.Ship, 0.3f);
                                    NotificationCenter.Show(Loc.T("Засада"), Loc.F("{0} попал под огонь невидимки", c.Ship.Name), NotificationCenter.Kind.Danger, 5f);
                                }
                            }),
                            new EventOption(Loc.T("ВЫСТАВИТЬ ЛОВУШКУ"), Loc.F("Без риска: +{0} науки, адмирал +50 опыта.", R(150 * k)), () =>
                            {
                                Science(150 * k); Xp(c, 50f);
                            }, Loc.F("Адмирал «{0}», «{1}» или «{2}»", TraitName("adm_tactician"), TraitName("adm_cautious"), TraitName("adm_veteran")), () => skilled),
                            new EventOption(Loc.T("ЗАПИСАТЬ И УЙТИ"), Loc.F("+{0} науки: записи датчиков изучат на базе.", R(40 * k)), () => Science(40 * k))));
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
                    return Ev("survey_frozen_colony", Loc.T("ЗАМЁРЗШИЙ КОВЧЕГ"),
                        Loc.T("На ледяной луне лежит колониальный корабль, разбитый три века назад. Половина криокапсул ещё работает: ") +
                        Loc.T("датчики видят пульс у двух сотен спящих. Их язык — древний диалект нашего."),
                        AnomalyType.DistressSignal, Opts(
                            new EventOption(Loc.T("РАЗБУДИТЬ И ПОСЕЛИТЬ"), Loc.F("+3 населения в колонии со свободным жильём (сейчас свободно мест: {0}). ", free) +
                                Loc.F("Если мест нет — +{0} влияния за спасение.", R(60 * k)), () =>
                            {
                                int done = Settle(3);
                                if (done == 0) Give(influence: 60 * k);
                                Xp(c, 30f);
                            }),
                            new EventOption(Loc.T("ИЗУЧИТЬ КРИОКАПСУЛЫ"), Loc.F("+{0} науки. Спящих оставят в стазисе.", R(140 * k)), () =>
                            {
                                Science(140 * k); Xp(c, 20f);
                            }),
                            new EventOption(Loc.T("РАЗОБРАТЬ КОРПУС"), Loc.F("+{0} сплавов.", R(90 * k)), () => Give(alloys: 90 * k))));
                }
            });

            Add(new Template
            {
                Id = "survey_gas_harvest", When = Trigger.Survey, Weight = 0.8f,
                CanFire = c => c?.System != null,
                Build = c =>
                {
                    float k = K();
                    return Ev("survey_gas_harvest", Loc.T("ГЕЛИЕВЫЕ ШТОРМЫ"),
                        Loc.T("В верхних слоях газового гиганта бушуют штормы, богатые гелием-3. Концентрация втрое выше обычной, ") +
                        Loc.T("но вихри непредсказуемы: черпать придётся осторожно."),
                        AnomalyType.AlienFauna, Opts(
                            new EventOption(Loc.T("ВЗЯТЬ ПРОБЫ СЕЙЧАС"), Loc.F("+{0} гелия-3 сразу.", R(130 * k)), () => Give(energy: 130 * k)),
                            new EventOption(Loc.T("ПОСТАВИТЬ ЗОНДЫ-ЧЕРПАКИ"), Loc.F("+{0} гелия-3 в месяц на 24 месяца (≈{1} всего, выгоднее, но дольше).", R(3 * k), R(72 * k)), () =>
                                EmpireEffects.Add("gas_harvest", Loc.T("Зонды-черпаки"), Loc.T("Гелиевые штормы"), 24, energy: 3 * k)),
                            new EventOption(Loc.T("ИЗУЧИТЬ ВИХРИ"), Loc.T("+2 науки в месяц навсегда: учёный составит модель штормов."), () =>
                            {
                                TechnologyManager.Instance?.AddMonthlyScience(2f); Xp(c, 30f);
                            }, Loc.F("Учёный-{0} или «{1}»", TraitName("sci_analyst").ToLower(), TraitName("sci_brilliant")),
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
                    return Ev("survey_ancient_battlefield", Loc.T("ПОЛЕ ДРЕВНЕЙ БИТВЫ"),
                        Loc.T("Тысячи обломков кружат вокруг звезды: здесь когда-то сошлись два флота, и ни один не ушёл. ") +
                        Loc.T("Между корпусами дрейфуют неразорвавшиеся мины."),
                        AnomalyType.AncientDerelict, Opts(
                            new EventOption(Loc.T("СОБРАТЬ ОБЛОМКИ"), Loc.F("+{0} сплавов. Риск 25%: мина — корабль теряет 40% корпуса.", R(200 * k)), () =>
                            {
                                Give(alloys: 200 * k);
                                if (Random.value < 0.25f)
                                {
                                    DamageShip(c?.Ship, 0.4f);
                                    NotificationCenter.Show(Loc.T("Подрыв на мине"), Loc.T("Научное судно повреждено"), NotificationCenter.Kind.Danger, 5f);
                                }
                            }),
                            new EventOption(Loc.T("ВОССТАНОВИТЬ ХОД БОЯ"), Loc.T("Все наши адмиралы получают +60 опыта: тактика древних поучительна."), () =>
                            {
                                foreach (var l in PlayerLeaders(LeaderClass.Admiral)) Leaders.GrantXp(l, 60f);
                                Xp(c, 20f);
                            }, Loc.T("Есть адмирал"), () =>
                            {
                                foreach (var l in PlayerLeaders(LeaderClass.Admiral)) return true;
                                return false;
                            }),
                            new EventOption(Loc.T("ОСТАВИТЬ МЕМОРИАЛЬНЫЙ БУЙ"), Loc.T("+25 влияния: история облетит новостные каналы."), () => Give(influence: 25f))));
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
                    return Ev("diplo_summit", Loc.T("ГАЛАКТИЧЕСКИЙ САММИТ"),
                        Loc.F("{0} созывает саммит на нейтральной станции. Приглашены все известные империи: обсуждать будут ", rival) +
                        Loc.T("торговые пути и границы. От состава нашей делегации зависит, кто будет говорить громче."),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("БОЛЬШАЯ ДЕЛЕГАЦИЯ"), Loc.T("−40 влияния: отношение всех империй +12."), () =>
                            {
                                Pay(influence: 40f); OpinionAll(12f);
                            }, Need(influence: 40f), () => CanPay(influence: 40f)),
                            new EventOption(Loc.T("ПОСЛАТЬ ПОСЛА"), Loc.F("Бесплатно: отношение {0} +8.", rival), () => Opinion(c.Rival, 8f)),
                            new EventOption(Loc.T("БОЙКОТ"), Loc.F("+15 влияния дома: «нас не купить». Отношение {0} −12.", rival), () =>
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
                    return Ev("diplo_rival_crisis", Loc.T("НЕУРОЖАЙ У СОСЕДЕЙ"),
                        Loc.F("Грибковая гниль уничтожила гидропонные фермы {0}. Их правительство просит помощи открыто — ", rival) +
                        Loc.T("впервые за всю историю отношений."),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("ОТПРАВИТЬ ГРУЗ"), Loc.F("−{0} титана: отношение {1} +25, +10 влияния.", aid, rival), () =>
                            {
                                Pay(minerals: aid); Opinion(c.Rival, 25f); Give(influence: 10f);
                            }, Need(minerals: aid), () => CanPay(minerals: aid)),
                            new EventOption(Loc.T("ГУМАНИТАРНАЯ МИССИЯ С «ВРАЧАМИ»"), Loc.F("−{0} титана: отношение +10, разведсеть в {1} +25%. Риск 15%: раскроют — отношение −20.", R(aid / 2f), rival), () =>
                            {
                                Pay(minerals: aid / 2f);
                                EspionageManager.AdjustNetwork(c.Rival, 25f);
                                Opinion(c.Rival, Random.value < 0.15f ? -20f : 10f);
                            }, Need(minerals: aid / 2f), () => CanPay(minerals: aid / 2f)),
                            new EventOption(Loc.T("ВЫРАЗИТЬ СОЧУВСТВИЕ"), Loc.F("Бесплатно. Отношение {0} −5: они запомнят.", rival), () => Opinion(c.Rival, -5f))));
                }
            });
        }
    }
}
