using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using Random = UnityEngine.Random;

namespace StellarisClone.Core
{
    /// <summary>
    /// Лор «Великое Молчание» (см. Lore):
    ///   • семь мест Строителей при разведке — каждое даёт следующий осколок летописи и цитату из него;
    ///   • ежемесячные: сны о беззвучной звезде, легенда собственной фракции, обмен легендами с соседями;
    ///   • финал после седьмого осколка: «Великое Молчание» → хранить молчание / спеть громче / разбудить Спящих,
    ///     у каждого пути своё продолжение и запись раздела «Развязки».
    /// </summary>
    public static partial class EventDatabase
    {
        private static string ChainSilence => Loc.T(Lore.ChainName);

        private static int Year => TimeManager.Instance != null ? TimeManager.Instance.Year : 2200;

        /// <summary>Цитата следующего осколка — её читают на месте находки.</summary>
        private static string ShardQuote(string intro)
        {
            var e = Lore.PeekNextShard();
            if (e == null) return "";
            return "\n\n" + intro + " <i>«" + e.Quote + "»</i>\n<color=#E7C170>" +
                   Loc.F("Осколок летописи Строителей {0} из {1}: «{2}»", e.Shard, Lore.ShardTotal, e.Title) + "</color>";
        }

        private static void Shard(EventContext c) => Lore.NextShard(c);

        private static void RegisterLoreArt()
        {
            _art["lore_dead_star"] = "LoreDeadStar|Relic";
            _art["lore_silent_city"] = "LoreSilentCity|FrozenArk";
            _art["lore_singing_moon"] = "LoreSingingMoon|Crystal";
            _art["lore_stone_fleet"] = "LoreStoneFleet|Battlefield";
            _art["lore_sealed_gate"] = "LoreSealedGate|Rift";
            _art["lore_archive_seed"] = "LoreArchiveSeed|PrecursorCore";
            _art["lore_sleeper_pod"] = "LoreSleeperPod|PrecursorVault";
            _art["lore_dreams"] = "LoreDreams|HeartVisitors|Rift";
            _art["lore_own_myth"] = "LoreMyth|PrecursorSignal";
            _art["lore_legend_exchange"] = "LoreLegends|Summit";
            _art["silence_truth"] = "SilenceTruth|PrecursorEcho";
            _art["silence_quiet_years"] = "SilenceQuiet|PrecursorEcho";
            _art["silence_herald"] = "SilenceHerald|Leviathan";
            _art["silence_sleeper"] = "SilenceSleeper|PrecursorVault";
        }

        private static void AddLoreEvents()
        {
            RegisterLoreArt();
            AddLoreSites();
            AddLoreMonthly();
            AddSilenceFinale();
        }

        // ==================== МЕСТА СТРОИТЕЛЕЙ (РАЗВЕДКА) ====================

        private static bool SiteOpen(EventContext c, int minYear) => c?.System != null && !Lore.ChronicleComplete && Year >= minYear;

        private static void AddLoreSites()
        {
            // 1. Погашенная звезда
            Add(new Template
            {
                Id = "lore_dead_star", When = Trigger.Survey, Weight = 0.6f, Once = true,
                CanFire = c => SiteOpen(c, 2200),
                Build = c =>
                {
                    float k = K();
                    return Ev("lore_dead_star", Loc.T("ПОГАШЕННАЯ ЗВЕЗДА"),
                        Loc.T("Звезда этой системы мертва — но умерла она не своей смертью. Вокруг остывшего ядра висит решётчатый каркас размером ") +
                        Loc.T("с орбиту планеты: кто-то построил звезду, а потом аккуратно, сегмент за сегментом, перекрыл ей горение. ") +
                        Loc.T("На внутренней стороне каркаса разведчики нашли символы, выжженные так, чтобы их можно было прочесть только при свете самой звезды.") +
                        ShardQuote(Loc.T("Лингвисты разобрали первую строку:")),
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption(Loc.T("ИЗУЧИТЬ КАРКАС"), Loc.F("+{0} науки, учёный получает опыт.", R(120 * k)), () =>
                            {
                                Science(120 * k); Xp(c, 30f); Shard(c);
                            }),
                            new EventOption(Loc.T("СНЯТЬ ОБШИВКУ"), Loc.F("+{0} сплавов: металл Строителей не ржавеет за десятки тысяч лет.", R(90 * k)), () =>
                            {
                                Give(alloys: 90 * k); Shard(c);
                            }),
                            new EventOption(Loc.T("ЗАЖЕЧЬ ЕЁ СНОВА"), Loc.T("Учёный уверен, что каркас можно запустить. +4 гелия-3 в месяц навсегда — и звезда снова начнёт светить."), () =>
                            {
                                Monthly(energy: 4f); SetFlag("lore_star_relit"); Shard(c);
                            }, Loc.F("Учёный-{0}", TraitName("sci_xeno").ToLower()), () => LeaderHas(c, "sci_xeno"))));
                }
            });

            // 2. Город без звука
            Add(new Template
            {
                Id = "lore_silent_city", When = Trigger.Survey, Weight = 0.6f, Once = true,
                CanFire = c => SiteOpen(c, 2201),
                Build = c =>
                {
                    float k = K();
                    return Ev("lore_silent_city", Loc.T("ГОРОД БЕЗ ЗВУКА"),
                        Loc.T("Под ледяной коркой луны — город. Целый: башни, мосты, сады из камня, улицы, по которым как будто только что прошли. ") +
                        Loc.T("В домах за столами сидят фигуры, высокие и тонкие, лицом к окнам. Они не истлели — за тысячи лет здесь не было ни бактерий, ни ветра. ") +
                        Loc.T("Десантники говорят шёпотом, хотя в вакууме их всё равно никто не услышит.") +
                        ShardQuote(Loc.T("На центральной площади высечено:")),
                        AnomalyType.AncientDerelict, Opts(
                            new EventOption(Loc.T("ПРИСЛАТЬ АРХЕОЛОГОВ"), Loc.F("+{0} науки и +15 опыта учёному.", R(150 * k)), () =>
                            {
                                Science(150 * k); Xp(c, 15f); Shard(c);
                            }),
                            new EventOption(Loc.T("ОБЪЯВИТЬ МЕМОРИАЛОМ"), Loc.T("+40 влияния и +10 к отношению всех знакомых империй: мы не грабим мёртвых."), () =>
                            {
                                Give(influence: 40f); OpinionAll(10f); Shard(c);
                            }),
                            new EventOption(Loc.T("РАЗОБРАТЬ НА МАТЕРИАЛЫ"), Loc.F("+{0} титана. Учёные успеют снять только одну надпись.", R(160 * k)), () =>
                            {
                                Give(minerals: 160 * k); Shard(c);
                            })));
                }
            });

            // 3. Поющая луна
            Add(new Template
            {
                Id = "lore_singing_moon", When = Trigger.Survey, Weight = 0.6f, Once = true,
                CanFire = c => SiteOpen(c, 2201),
                Build = c =>
                {
                    float k = K();
                    return Ev("lore_singing_moon", Loc.T("ПОЮЩАЯ ЛУНА"),
                        Loc.T("Когда научное судно включило двигатели на орбите, луна под ним зазвучала. Кристаллические пещеры, тянущиеся на сотни километров, ") +
                        Loc.T("отозвались на вибрацию реактора чистым многоголосым аккордом — и через минуту смолкли, будто испугавшись. ") +
                        Loc.T("Анализ показал: аккорд не случаен. Кристаллы выращены так, чтобы хранить запись, а играть её может любой, кто достаточно громко «постучит».") +
                        ShardQuote(Loc.T("Компьютер перевёл запись:")),
                        AnomalyType.SpatialRift, Opts(
                            new EventOption(Loc.T("ЗАПИСАТЬ ВСЁ"), Loc.F("+{0} науки.", R(130 * k)), () =>
                            {
                                Science(130 * k); Shard(c);
                            }),
                            new EventOption(Loc.T("ДОБЫТЬ КРИСТАЛЛЫ"), Loc.F("+{0} гелия-3: резонансные кристаллы — отличные накопители.", R(140 * k)), () =>
                            {
                                Give(energy: 140 * k); Shard(c);
                            }),
                            new EventOption(Loc.T("ДАТЬ ЕЙ ДОПЕТЬ"), Loc.T("+3 науки в месяц навсегда: станция наблюдения будет «стучать» по графику и записывать ответ."), () =>
                            {
                                TechnologyManager.Instance?.AddMonthlyScience(3f); Shard(c);
                            }, Loc.F("Учёный-{0}", TraitName("sci_analyst").ToLower()), () => LeaderHas(c, "sci_analyst"))));
                }
            });

            // 4. Окаменевший флот
            Add(new Template
            {
                Id = "lore_stone_fleet", When = Trigger.Survey, Weight = 0.55f, Once = true,
                CanFire = c => SiteOpen(c, 2202),
                Build = c =>
                {
                    float k = K();
                    return Ev("lore_stone_fleet", Loc.T("ОКАМЕНЕВШИЙ ФЛОТ"),
                        Loc.T("Тысячи кораблей, покрытых коркой, похожей на камень, висят в пустоте плотным строем. Ни один не повреждён. ") +
                        Loc.T("Все двигатели развёрнуты в одну сторону — к краю галактики. Этот флот не воевал: он уходил. ") +
                        Loc.T("И остановился на полпути, будто экипажи разом передумали — или разом перестали думать.") +
                        ShardQuote(Loc.T("В рубке флагмана сохранился бортовой журнал:")),
                        AnomalyType.AncientDerelict, Opts(
                            new EventOption(Loc.T("ПРОЧЕСТЬ НАВИГАЦИЮ"), Loc.T("Карты уходящих: все системы в пределах 3 прыжков от границ открыты."), () =>
                            {
                                int n = RevealAround(3); Shard(c);
                                if (n > 0) NotificationCenter.Show(Loc.T("Карты Строителей"), Loc.F("Открыто систем: {0}", n), NotificationCenter.Kind.Success, 6f);
                            }),
                            new EventOption(Loc.T("ВЗЯТЬ ОДИН НА БУКСИР"), Loc.T("Корпус удастся восстановить: в составе флота появится эсминец «Уходящий»."), () =>
                            {
                                FreeWarship(ShipClass.Destroyer, Loc.T("Уходящий")); Shard(c);
                            }),
                            new EventOption(Loc.T("РАЗОБРАТЬ НА СПЛАВЫ"), Loc.F("+{0} сплавов.", R(130 * k)), () =>
                            {
                                Give(alloys: 130 * k); Shard(c);
                            })));
                }
            });

            // 5. Запечатанные врата
            Add(new Template
            {
                Id = "lore_sealed_gate", When = Trigger.Survey, Weight = 0.55f, Once = true,
                CanFire = c => SiteOpen(c, 2203),
                Build = c =>
                {
                    float k = K();
                    float risk = 80f * k;
                    return Ev("lore_sealed_gate", Loc.T("ЗАПЕЧАТАННЫЕ ВРАТА"),
                        Loc.T("Кольцо из чёрного металла — такое же, как Врата Предтеч, — но его просвет залит чем-то вроде застывшей смолы. ") +
                        Loc.T("Масса не отражает излучение и поглощает радиосигналы целиком: направленная в неё передача не возвращается даже эхом. ") +
                        Loc.T("Врата не разрушены. Их заткнули — тщательно, как затыкают рот.") +
                        ShardQuote(Loc.T("По ободу врат идёт надпись:")),
                        AnomalyType.SpatialRift, Opts(
                            new EventOption(Loc.T("ИЗУЧИТЬ ПЕЧАТЬ"), Loc.F("+{0} науки. Печать не трогаем.", R(140 * k)), () =>
                            {
                                Science(140 * k); Shard(c);
                            }),
                            new EventOption(Loc.T("ПРОБИТЬ ПЕЧАТЬ"), Loc.F("50% — врата выдохнут древнюю ноту: +{0} науки и +60 влияния. Иначе корабль серьёзно пострадает.", R(260 * k)), () =>
                            {
                                Shard(c);
                                if (Random.value < 0.5f)
                                {
                                    Science(260 * k); Give(influence: 60f);
                                    NotificationCenter.Show(Loc.T("Врата выдохнули"), Loc.T("Над системой на миг прозвучал аккорд, которого никто не слышал шестьдесят тысяч лет"), NotificationCenter.Kind.Success, 7f);
                                }
                                else
                                {
                                    DamageShip(c.Ship, 0.6f);
                                    NotificationCenter.Show(Loc.T("Печать огрызнулась"), Loc.T("Корабль отбросило ударной волной беззвучия"), NotificationCenter.Kind.Warning, 6f);
                                }
                            }),
                            new EventOption(Loc.T("УКРЕПИТЬ ПЕЧАТЬ"), Loc.F("−{0} сплавов, +2 влияния в месяц навсегда. Кто-то очень не хотел, чтобы это открыли.", R(risk)), () =>
                            {
                                Pay(alloys: risk); Monthly(influence: 2f); SetFlag("lore_gate_sealed"); Shard(c);
                            }, Need(alloys: risk), () => CanPay(alloys: risk))));
                }
            });

            // 6. Семя архива
            Add(new Template
            {
                Id = "lore_archive_seed", When = Trigger.Survey, Weight = 0.5f, Once = true,
                CanFire = c => SiteOpen(c, 2204),
                Build = c =>
                {
                    float k = K();
                    return Ev("lore_archive_seed", Loc.T("СЕМЯ АРХИВА"),
                        Loc.T("В центре астероида, в полости без единого входа, растёт кристалл, похожий на семя. Он записывает и отдаёт информацию, ") +
                        Loc.T("но только в полной тишине: стоит рядом заработать хоть одному прибору, и кристалл гаснет. ") +
                        Loc.T("Чтобы прочесть его, экипажу придётся отключить всё — связь, реактор, жизнеобеспечение — и ждать в темноте, пока семя заговорит.") +
                        ShardQuote(Loc.T("Через час, в темноте, семя произнесло:")),
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption(Loc.T("ОТКЛЮЧИТЬ ВСЁ"), Loc.F("+{0} науки. Учёный получит 40 опыта — и запомнит этот час навсегда.", R(200 * k)), () =>
                            {
                                Science(200 * k); Xp(c, 40f); Shard(c);
                            }),
                            new EventOption(Loc.T("ВЫВЕЗТИ В СТОЛИЦУ"), Loc.T("+1 науки и +1 влияния в месяц навсегда: зал тишины в столичном университете."), () =>
                            {
                                TechnologyManager.Instance?.AddMonthlyScience(1f); Monthly(influence: 1f); Shard(c);
                            })));
                }
            });

            // 7. Капсула спящего
            Add(new Template
            {
                Id = "lore_sleeper_pod", When = Trigger.Survey, Weight = 0.5f, Once = true,
                CanFire = c => SiteOpen(c, 2205) && Lore.ShardCount >= 3,
                Build = c =>
                {
                    float k = K();
                    return Ev("lore_sleeper_pod", Loc.T("КАПСУЛА СПЯЩЕГО"),
                        Loc.T("На обратной стороне мёртвой планеты, в шахте глубиной в километр, стоит капсула из того же чёрного металла. ") +
                        Loc.T("Внутри — высокая фигура со сложенными на груди руками. Термометры показывают: внутри на долю градуса теплее, чем снаружи. ") +
                        Loc.T("Оно живое. Оно спит. И судя по показаниям, спит так глубоко, что даже его мысли остановлены на полуслове.") +
                        ShardQuote(Loc.T("На крышке капсулы выбито:")),
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption(Loc.T("НЕ БУДИТЬ"), Loc.T("+50 влияния. Капсулу укроют и оставят под охраной. Мы знаем, где он спит."), () =>
                            {
                                Give(influence: 50f); SetFlag("lore_sleeper_known"); Shard(c);
                            }),
                            new EventOption(Loc.T("ПЕРЕВЕЗТИ В ЛАБОРАТОРИЮ"), Loc.F("+{0} науки. Мы знаем, где он спит, — и как устроен его сон.", R(180 * k)), () =>
                            {
                                Science(180 * k); SetFlag("lore_sleeper_known"); Shard(c);
                            }),
                            new EventOption(Loc.T("ВСКРЫТЬ КАПСУЛУ"), Loc.F("+{0} науки и +{1} сплавов. Фигура рассыплется в пыль — эту дверь мы закроем навсегда.", R(120 * k), R(80 * k)), () =>
                            {
                                Science(120 * k); Give(alloys: 80 * k); SetFlag("lore_sleeper_lost"); Shard(c);
                            })));
                }
            });
        }

        // ==================== ЕЖЕМЕСЯЧНЫЕ ====================

        private static AIEmpireManager LegendPartner()
        {
            var list = new List<AIEmpireManager>();
            string own = Lore.PlayerFactionKey;
            foreach (var ai in AIEmpireManager.Alive)
            {
                if (!PeacefulKnown(ai)) continue;
                string key = FactionRegistry.KeyOf(FactionTraits.FactionOf(ai.OwnerId));
                if (key == null || key == own || Lore.IsUnlocked("myth_" + key) || HasFlag("legend_refused_" + key)) continue;
                list.Add(ai);
            }
            return RandomOf(list);
        }

        private static void AddLoreMonthly()
        {
            // Сны о беззвучной звезде
            Add(new Template
            {
                Id = "lore_dreams", When = Trigger.Monthly, Weight = 0.8f, Once = true,
                FindContext = () =>
                {
                    if (Lore.ShardCount < 2) return null;
                    var p = CapitalPlanet();
                    return p != null ? new EventContext { Planet = p } : null;
                },
                Build = c =>
                {
                    float k = K();
                    return Ev("lore_dreams", Loc.T("СОН О БЕЗЗВУЧНОЙ ЗВЕЗДЕ"),
                        Loc.F("Врачи {0} бьют тревогу: тысячи жителей видят один и тот же сон. Звезда без света, вокруг неё медленно поворачиваются кольца, ", Planet(c)) +
                        Loc.T("и ощущение, что за спиной кто-то стоит и очень внимательно слушает. Люди просыпаются в тишине и боятся заговорить первыми. ") +
                        Loc.T("Сон начался в тот месяц, когда учёные прочли второй осколок летописи Строителей."),
                        AnomalyType.ColonyIncident, Opts(
                            new EventOption(Loc.T("ЗАПИСАТЬ СНЫ"), Loc.F("+{0} науки. Записи снов, сведённые вместе, похожи на карту.", R(110 * k)), () =>
                            {
                                Science(110 * k); Lore.Unlock("myst_dreams");
                            }),
                            new EventOption(Loc.T("УСПОКОИТЬ ЛЮДЕЙ"), Loc.T("+30 влияния: власти объясняют сны усталостью и находками в новостях."), () =>
                            {
                                Give(influence: 30f); Lore.Unlock("myst_dreams");
                            }),
                            new EventOption(Loc.T("ВВЕСТИ «ТИХИЕ ЧАСЫ»"), Loc.T("+2 влияния в месяц навсегда: по ночам столица гасит передатчики. Сны прекращаются."), () =>
                            {
                                Monthly(influence: 2f); SetFlag("lore_quiet_hours"); Lore.Unlock("myst_dreams");
                            }, Loc.T("Найден пятый осколок летописи"), () => Lore.ShardCount >= 5)));
                }
            });

            // Легенда собственной фракции — её миф о Строителях
            Add(new Template
            {
                Id = "lore_own_myth", When = Trigger.Monthly, Weight = 1f, Once = true,
                FindContext = () =>
                {
                    string key = Lore.PlayerFactionKey;
                    if (Lore.ShardCount < 3 || key == null || Lore.Get("myth_" + key) == null || Lore.IsUnlocked("myth_" + key)) return null;
                    return new EventContext { Planet = CapitalPlanet() };
                },
                Build = c => OwnMythEvent(Lore.PlayerFactionKey, K())
            });

            // Обмен легендами с мирным соседем
            Add(new Template
            {
                Id = "lore_legend_exchange", When = Trigger.Monthly, Weight = 0.6f,
                FindContext = () =>
                {
                    if (Lore.ShardCount < 2) return null;
                    var ai = LegendPartner();
                    return ai != null ? new EventContext { Rival = ai.OwnerId } : null;
                },
                Build = c =>
                {
                    var ai = AIEmpireManager.For(c.Rival);
                    if (ai == null) return null;
                    string key = FactionRegistry.KeyOf(FactionTraits.FactionOf(ai.OwnerId));
                    var myth = Lore.Get("myth_" + key);
                    float k = K(), price = 90f * k;
                    return Ev("lore_legend_exchange", Loc.T("ОБМЕН ЛЕГЕНДАМИ"),
                        Loc.F("Учёные империи {0} узнали, что мы собираем летопись Строителей. Их посол привёз старинный текст — легенду своего народа, ", ai.AIName) +
                        Loc.F("которую до сих пор не показывали чужакам: «{0}». В обмен они хотят наши расшифровки.", myth != null ? myth.Title : "") +
                        Loc.T(" Похоже, в своём углу галактики они тоже наткнулись на следы тех, кто замолчал."),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("ПОДЕЛИТЬСЯ ЗНАНИЕМ"), Loc.F("+15 к отношению империи {0}. Их легенда — в нашем Архиве.", ai.AIName), () =>
                            {
                                ai.AddMemory("incident", 15f); Lore.Unlock("myth_" + key);
                            }),
                            new EventOption(Loc.T("КУПИТЬ БЕЗ ОБМЕНА"), Loc.F("−{0} гелия-3: легенда наша, расшифровки остаются у нас. +5 к отношению.", R(price)), () =>
                            {
                                Pay(energy: price); ai.AddMemory("incident", 5f); Lore.Unlock("myth_" + key);
                            }, Need(energy: price), () => CanPay(energy: price)),
                            new EventOption(Loc.T("ОТКАЗАТЬ"), Loc.T("+20 влияния: летопись Строителей — наша и только наша. Отношение −5."), () =>
                            {
                                Give(influence: 20f); ai.AddMemory("incident", -5f); SetFlag("legend_refused_" + key);
                            })));
                }
            });
        }

        private static GameEventData OwnMythEvent(string key, float k)
        {
            string title, text;
            switch (key)
            {
                case "astrea":
                    title = Loc.T("СТРОКА НА МОНИТОРАХ");
                    text = Loc.T("Архивариус Сената поднял журналы ковчега «Справедливость». На сороковом году пути ковчег прошёл мимо кольца из чёрного металла, ") +
                           Loc.T("и на всех мониторах на секунду появилась строка на неизвестном языке. Её тогда сочли сбоем. Сегодня лингвисты, знающие язык Строителей, ") +
                           Loc.T("прочли её за минуту. Сенат требует, чтобы перевод обсудили публично — как всё в Республике.");
                    break;
                case "xarn":
                    title = Loc.T("ПРАВДА О СОЛНЦЕ");
                    text = Loc.T("Астрофизики Доминиона закончили глубинный зонд родной звезды — и подтвердили древнюю песню о Безмолвных Богах. ") +
                           Loc.T("Внутри солнца Ксарн лежит каркас Строителей: звезду не родила природа, её зажгли — и потом медленно перекрыли. ") +
                           Loc.T("Маршалы называют это оскорблением. Жрецы кузниц — милостью. Легионы ждут, что скажет Верховный маршал.");
                    break;
                case "aquila":
                    title = Loc.T("ПОСЛЕДНЯЯ МОНЕТА");
                    text = Loc.T("Совет директоров впервые за столетие открыл сейф основателей. Там лежит последняя монета из того ящика, с которого началась Аквила: ") +
                           Loc.T("сплав, не проводящий ни тепла, ни звука. Эксперты по Строителям нашли на её ребре надпись. Совет спорит, кому объявить о находке ") +
                           Loc.T("и сколько это будет стоить.");
                    break;
                case "iridia":
                    title = Loc.T("ПЕЧАТЬ НАД САДАМИ");
                    text = Loc.T("Оракулы, сверив летопись Строителей с Первым видением, поняли то, во что не хотели верить. Трещина, расколовшая когда-то небо Иридии, ") +
                           Loc.T("была вскрытыми вратами Строителей, а свет, давший народу дар провидения, — последней нотой их песни. ") +
                           Loc.T("Храмовый хор спрашивает: петь ли теперь громче — или тише, чем прежде?");
                    break;
                case "terraan":
                    title = Loc.T("ДРЕВО, КОТОРОЕ ПОМНИТ");
                    text = Loc.T("Древнейшее древо Конклава, молчавшее восемь тысяч лет, впервые заговорило по сети мицелия. Оно помнит Строителей — ") +
                           Loc.T("гостей, приходивших слушать лес, — и помнит день, когда они перестали приходить. Лес тогда замер, и то, что слушало, прошло мимо. ") +
                           Loc.T("Древо спрашивает: помнит ли Конклав, как молчать?");
                    break;
                case "nol":
                    title = Loc.T("ПЕРЕМЕННАЯ «НОЛЬ»");
                    text = Loc.T("Внутренний аудит Консенсуса открыл засекреченный модуль: в каждом прогнозе судеб галактики один и тот же разрыв. ") +
                           Loc.T("В определённый год разумная активность падает почти до нуля — без войн и катастроф. Модуль нашёл такой же разрыв шестьдесят ") +
                           Loc.T("тысяч лет назад, и сто двадцать, и сто восемьдесят. Консенсус должен решить, что делать с этим знанием.");
                    break;
                default:
                    return null;
            }
            string id = "myth_" + key;
            return Ev("lore_own_myth", title, text, AnomalyType.PrecursorRelic, Opts(
                new EventOption(Loc.T("ОБЪЯВИТЬ НАРОДУ"), Loc.T("+60 влияния: история народа и история Строителей отныне одна."), () =>
                {
                    Give(influence: 60f); Lore.Unlock(id);
                }),
                new EventOption(Loc.T("ПЕРЕДАТЬ УЧЁНЫМ"), Loc.F("+{0} науки и +2 науки в месяц навсегда.", R(150 * k)), () =>
                {
                    Science(150 * k); TechnologyManager.Instance?.AddMonthlyScience(2f); Lore.Unlock(id);
                }),
                new EventOption(Loc.T("ЗАСЕКРЕТИТЬ"), Loc.T("Знание останется у правительства. Если однажды придётся выбирать судьбу галактики, у нас будет лишний козырь (−20% к цене решений финала)."), () =>
                {
                    SetFlag("lore_secret_myth"); Lore.Unlock(id);
                })));
        }

        // ==================== ФИНАЛ: ВЕЛИКОЕ МОЛЧАНИЕ ====================

        private static float FinaleCost(float v) => HasFlag("lore_secret_myth") ? v * 0.8f : v;

        private static void AddSilenceFinale()
        {
            Add(new Template
            {
                Id = "silence_truth", When = Trigger.Chain,
                CanFire = c => !HasFlag("silence_chosen"),
                Build = c =>
                {
                    float k = K();
                    float wake = FinaleCost(250f * k), quiet = FinaleCost(150f * k);
                    bool sleeper = HasFlag("lore_sleeper_known");
                    string text =
                        Loc.T("Последний осколок встал на место, и летопись Строителей прочитана целиком. Научный совет собрался ночью — и впервые за всю историю ") +
                        Loc.T("заседание шло без записи.\n\n") +
                        Loc.T("Звучащие не исчезли. Они замолчали, спасаясь от Слушающего, что приходит из-за края галактики на «звук» разума, — и он ") +
                        Loc.T("возвращается каждые шестьдесят тысяч лет. Пульсар Ирис, который астрономы считали природной диковиной, отсчитывал этот срок. ") +
                        Loc.T("Месяц назад он сбился с ритма.\n\n") +
                        Loc.T("Галактика снова поёт: флоты, передатчики, миллиарды разумов. Строители оставили нам выбор — тот, перед которым когда-то стояли сами.");
                    if (sleeper) text += Loc.T("\n\nИ мы знаем, где спит один из них.");
                    return Ch("silence_truth", ChainSilence, 1, Loc.T("ВЕЛИКОЕ МОЛЧАНИЕ"), text, AnomalyType.PrecursorRelic, Opts(
                        new EventOption(Loc.T("ХРАНИТЬ МОЛЧАНИЕ"), Loc.F("−{0} влияния: «тихие часы», глушители Строителей, осторожные прыжки. +4 науки и +2 влияния в месяц навсегда. Через 2 года станет ясно, хватило ли этого.", R(quiet)), () =>
                        {
                            Pay(influence: quiet);
                            TechnologyManager.Instance?.AddMonthlyScience(4f); Monthly(influence: 2f);
                            SetFlag("silence_chosen"); SetFlag("silence_keeper");
                            Later("silence_quiet_years", 720, c, ChainSilence, Loc.T("Империя учится молчать. Пульсар Ирис отсчитывает последние такты."));
                        }, Need(influence: quiet), () => CanPay(influence: quiet)),
                        new EventOption(Loc.T("СПЕТЬ ГРОМЧЕ"), Loc.F("+{0} науки и +8 науки в месяц навсегда, ключ к чуду фракции. Песня привлечёт Глашатая Тишины — через ≈ 1,5 года он придёт к нашим границам.", R(350 * k)), () =>
                        {
                            Science(350 * k); TechnologyManager.Instance?.AddMonthlyScience(8f);
                            SetFlag("wonder_key"); SetFlag("silence_chosen"); SetFlag("silence_singer");
                            Later("silence_herald", 540, c, ChainSilence, Loc.T("Мы зажгли Маяк. Песня империи слышна за краем галактики — и оттуда что-то идёт."));
                        }),
                        new EventOption(Loc.T("РАЗБУДИТЬ СПЯЩИХ"), Loc.F("−{0} гелия-3 на экспедицию. Через год мы спросим Строителей сами.", R(wake)), () =>
                        {
                            Pay(energy: wake);
                            SetFlag("silence_chosen"); SetFlag("silence_waker");
                            Later("silence_sleeper", 360, c, ChainSilence, Loc.T("Экспедиция к хранилищу Спящих в пути. Летопись подсказывает, как будить, не причиняя боли."));
                        }, sleeper ? Need(energy: wake) : Loc.T("Нужно знать, где спит Строитель (капсула Спящего)"),
                           () => sleeper && CanPay(energy: wake))));
                }
            });

            // Путь Хранителей: прошёл ли Слушающий мимо
            Add(new Template
            {
                Id = "silence_quiet_years", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    Lore.Unlock("end_keeper");
                    return Ch("silence_quiet_years", ChainSilence, 2, Loc.T("ГОДЫ ТИШИНЫ"),
                        Loc.T("Пульсар Ирис замолчал совсем. В ту же ночь в дальних системах галактики пропала связь с десятком станций — у империй, ") +
                        Loc.T("которые пели громче всех. Их не уничтожили. Там просто перестали отвечать.\n\n") +
                        Loc.T("А над нашими мирами было тихо. Слушающий прошёл мимо — так же, как когда-то прошёл мимо леса Тэрра'ан. ") +
                        Loc.T("Соседи ещё не понимают, что случилось. Мы — понимаем."),
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption(Loc.T("ПРЕДУПРЕДИТЬ ГАЛАКТИКУ"), Loc.T("+120 влияния и +15 к отношению всех знакомых империй: мы делимся тем, как молчать."), () =>
                            {
                                Give(influence: 120f); OpinionAll(15f);
                                GalacticNews.Post(NewsKind.Society, Loc.T("Хранители Молчания раскрыли галактике летопись Строителей: «Пойте тише»"), -1, true);
                            }),
                            new EventOption(Loc.T("МОЛЧАТЬ И ДАЛЬШЕ"), Loc.F("+{0} науки и +3 науки в месяц: чужие беды — чужие уроки, а данные о Слушающем — наши.", R(250 * k)), () =>
                            {
                                Science(250 * k); TechnologyManager.Instance?.AddMonthlyScience(3f);
                            })));
                }
            });

            // Путь Певцов: Глашатай Тишины
            Add(new Template
            {
                Id = "silence_herald", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    Lore.Unlock("end_singer");
                    return Ch("silence_herald", ChainSilence, 2, Loc.T("ГЛАШАТАЙ ТИШИНЫ"),
                        Loc.T("Он пришёл. На краю наших границ из гиперкоридора вышло существо, на которое больно смотреть: левиафан, но не живой — ") +
                        Loc.T("пустой, как раковина, и в этой пустоте ничего не звучит. Корабли, подошедшие к нему, теряют связь, а экипажи — слова.\n\n") +
                        Loc.T("Это не сам Слушающий. Это его ухо, протянутое к самой громкой песне галактики. К нашей."),
                        AnomalyType.MilitaryIncident, Opts(
                            new EventOption(Loc.T("ВСТРЕТИТЬ ЕГО"), Loc.T("У границ пробудится «Глашатай Тишины» — сильнейший из левиафанов. Победа над ним прославит империю."), () =>
                            {
                                int sys = ThreatManager.Instance != null ? ThreatManager.Instance.AwakenLeviathan(0, Loc.T("Глашатай Тишины"), 1.6f) : -1;
                                var s = sys >= 0 && sys < EmpireStats.Systems.Count ? EmpireStats.Systems[sys] : null;
                                if (s != null)
                                {
                                    NotificationCenter.Show(Loc.T("Глашатай Тишины"), Loc.F("Он ждёт в системе {0}", s.Name), NotificationCenter.Kind.Danger, 8f, sys);
                                    GalacticNews.Post(NewsKind.Threat, Loc.F("В системе {0} появился Глашатай Тишины. Учёные галактики не могут объяснить, что это", GalacticNews.Place(s)), sys, true);
                                }
                            }),
                            new EventOption(Loc.T("СОБРАТЬ ХОР ГАЛАКТИКИ"), Loc.F("−{0} влияния: мы зовём всех знакомых. +20 к отношению всех; Глашатай придёт ослабленным.", R(FinaleCost(120f))), () =>
                            {
                                Pay(influence: FinaleCost(120f)); OpinionAll(20f);
                                ThreatManager.Instance?.AwakenLeviathan(0, Loc.T("Глашатай Тишины"), 1.1f);
                                GalacticNews.Post(NewsKind.Threat, Loc.T("Империи галактики объединяются против Глашатая Тишины"), -1, true);
                            }, Need(influence: FinaleCost(120f)), () => CanPay(influence: FinaleCost(120f))),
                            new EventOption(Loc.T("ЗАМОЛЧАТЬ"), Loc.F("Поздно, но ещё можно: −{0} науки в месяц навсегда (из 8, что дала песня). Глашатай уйдёт ни с чем.", 6), () =>
                            {
                                TechnologyManager.Instance?.RemoveMonthlyScience(6f);
                                SetFlag("silence_keeper");
                            })));
                }
            });

            // Путь Пробуждающих: последний из Звучащих
            Add(new Template
            {
                Id = "silence_sleeper", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    Lore.Unlock("end_sleeper");
                    return Ch("silence_sleeper", ChainSilence, 2, Loc.T("ПОСЛЕДНИЙ ИЗ ЗВУЧАЩИХ"),
                        Loc.T("Капсула открылась по всем правилам летописи — медленно, в полной тишине. Спящий долго не шевелился, привыкая к звуку ") +
                        Loc.T("собственных мыслей, а потом сказал первое слово за шестьдесят тысяч лет: «Простите».\n\n") +
                        Loc.T("Он говорит, что Строители так и не узнали, что такое Слушающий, — только убегали. «Вы первые, кто разбудил нас не криком, ") +
                        Loc.T("а вопросом. Спрашивайте. Я отвечу на один — потом мне нужно будет снова уснуть»."),
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption(Loc.T("КАК ВЫ СТРОИЛИ?"), Loc.T("Ключ к чуду фракции и две технологии сразу."), () =>
                            {
                                SetFlag("wonder_key"); TwoTechs();
                                NotificationCenter.Show(Loc.T("Ключ Строителей"), Loc.T("Чудо фракции: −25% к стоимости и сроку этапов"), NotificationCenter.Kind.Success, 7f);
                            }),
                            new EventOption(Loc.T("ГДЕ ВАШИ МИРЫ?"), Loc.F("Все системы в пределах 6 прыжков от границ открыты, +{0} науки.", R(200 * k)), () =>
                            {
                                int n = RevealAround(6); Science(200 * k);
                                if (n > 0) NotificationCenter.Show(Loc.T("Карты Звучащих"), Loc.F("Открыто систем: {0}", n), NotificationCenter.Kind.Success, 6f);
                            }),
                            new EventOption(Loc.T("КАК НАМ ВЫЖИТЬ?"), Loc.T("+5 влияния и +5 науки в месяц навсегда, +20 к отношению всех знакомых империй: мы делимся ответом со всеми."), () =>
                            {
                                Monthly(influence: 5f); TechnologyManager.Instance?.AddMonthlyScience(5f); OpinionAll(20f);
                                GalacticNews.Post(NewsKind.Science, Loc.T("Пробуждённый Строитель заговорил: впервые за шестьдесят тысяч лет"), -1, true);
                            })));
                }
            });
        }
    }
}
