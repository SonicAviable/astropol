using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using Random = UnityEngine.Random;

namespace StellarisClone.Core
{
    /// <summary>
    /// Глубокий контент: большие решения фракций раз в два года, пиратская гавань, победа над левиафаном,
    /// цепочка «Наследие Предтеч» с последствиями через годы и амбиции лидеров.
    /// </summary>
    public static partial class EventDatabase
    {
        // ==================== ПОМОЩНИКИ ====================

        /// <summary>Рост наград со временем: к 2240 году события щедрее в 2,5 раза.</summary>
        private static float K()
        {
            int year = TimeManager.Instance != null ? TimeManager.Instance.Year : 2200;
            return Mathf.Clamp(1f + (year - 2200) * 0.04f, 1f, 2.5f);
        }

        private static int R(float v) => Mathf.RoundToInt(v);

        private static List<string> Flags => AnomalyEventSystem.Instance?.CaptureState()?.Fired;
        private static bool HasFlag(string f) => Flags != null && Flags.Contains("flag:" + f);
        private static void SetFlag(string f) { var l = Flags; if (l != null && !l.Contains("flag:" + f)) l.Add("flag:" + f); }
        private static void ClearFlag(string f) => Flags?.Remove("flag:" + f);

        private static void OpinionAll(float delta)
        {
            foreach (var ai in AIEmpireManager.Alive) ai.AddMemory("incident", delta);
        }

        private static AIEmpireManager RandomPeacefulRival()
        {
            var list = new List<AIEmpireManager>();
            foreach (var ai in AIEmpireManager.Alive) if (!ai.AtWar) list.Add(ai);
            return RandomOf(list);
        }

        private static IEnumerable<Leader> PlayerLeaders(LeaderClass cls)
        {
            var lm = Leaders;
            if (lm == null) yield break;
            foreach (var l in lm.Of(0)) if (l.Class == cls) yield return l;
        }

        /// <summary>Неизученные системы в пределах N прыжков от границ игрока.</summary>
        private static List<StarSystem> UnsurveyedNearPlayer(int jumps)
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
            var list = new List<StarSystem>();
            foreach (var kv in dist)
            {
                var s = EmpireStats.GetSystem(kv.Key);
                if (s != null && !s.IsSurveyedBy(0)) list.Add(s);
            }
            return list;
        }

        /// <summary>Большое решение фракции игрока (null — фракция неизвестна).</summary>
        public static GameEventData FactionDecision()
        {
            string key = FactionRegistry.KeyOf(UIManager.Instance?.SelectedFaction);
            return key != null ? BuildChain("decision_" + key, new EventContext()) : null;
        }

        private static void RegisterContentArt()
        {
            _art["decision_astrea"] = "Festival";
            _art["decision_xarn"] = "Drills";
            _art["decision_aquila"] = "Trade";
            _art["aquila_dividends"] = "Trade";
            _art["decision_iridia"] = "Rift";
            _art["decision_terraan"] = "Amoeba";
            _art["pirate_haven"] = "Distress";
            _art["leviathan_slain"] = "Amoeba";
            _art["precursor_signal"] = "Relic";
            _art["precursor_decoding"] = "Probe";
            _art["precursor_map"] = "Relic";
            _art["precursor_vault"] = "Derelict";
            _art["precursor_core"] = "Relic";
            _art["precursor_echo"] = "Rift";
            _art["leader_ambition"] = "Breakthrough";
            _art["leader_ambition_result"] = "Breakthrough";
        }

        // ==================== РЕШЕНИЯ ФРАКЦИЙ ====================

        private static void AddFactionDecisions()
        {
            RegisterContentArt();

            // ---- Республика Астрея: закон Совета на два года (новый закон заменяет прежний) ----
            Add(new Template
            {
                Id = "decision_astrea", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    return Ev("decision_astrea", "СЕССИЯ СОВЕТА РЕСПУБЛИКИ",
                        "Раз в два года Совет Республики собирается, чтобы утвердить главный закон на ближайшие годы. " +
                        "Фракции спорят до хрипоты, журналисты дежурят у дверей зала, но последнее слово, как всегда, за Председателем. " +
                        "Какой курс выберет Республика?",
                        AnomalyType.Diplomatic, Opts(
                            new EventOption("ЗАКОН О НАУЧНЫХ ГРАНТАХ", $"+{R(8 * k)} науки и −{R(3 * k)} энергии в месяц на 24 месяца.",
                                () => EmpireEffects.Add("law_astrea", "Закон о научных грантах", "Совет Республики", 24, energy: -3 * k, science: 8 * k)),
                            new EventOption("ПРОГРАММА ОСВОЕНИЯ", $"+2 влияния и +{R(4 * k)} минералов в месяц на 24 месяца.",
                                () => EmpireEffects.Add("law_astrea", "Программа освоения", "Совет Республики", 24, minerals: 4 * k, influence: 2f)),
                            new EventOption("ОБОРОННЫЙ БЮДЖЕТ", $"+{R(4 * k)} сплавов и −{R(3 * k)} энергии в месяц на 24 месяца.",
                                () => EmpireEffects.Add("law_astrea", "Оборонный бюджет", "Совет Республики", 24, energy: -3 * k, alloys: 4 * k)),
                            new EventOption("ВСЕНАРОДНЫЙ РЕФЕРЕНДУМ", $"Закон не принимается, зато народ доверяет Совету: сразу +{R(40 * k)} влияния.",
                                () => Give(influence: 40 * k))));
                }
            });

            // ---- Доминион Ксарн: смотр легионов ----
            Add(new Template
            {
                Id = "decision_xarn", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    bool hasAdmiral = false;
                    foreach (var _ in PlayerLeaders(LeaderClass.Admiral)) { hasAdmiral = true; break; }
                    return Ev("decision_xarn", "СМОТР ЛЕГИОНОВ",
                        "Каждые два года верховный маршал принимает смотр легионов Доминиона. Над плацем гремят марши, " +
                        "дредноуты висят над столицей в парадном строю. Генералы ждут приказа: куда направить силу империи?",
                        AnomalyType.MilitaryIncident, Opts(
                            new EventOption("ВСЕОБЩАЯ МОБИЛИЗАЦИЯ", $"Сразу 2 корвета у верфи; −{R(4 * k)} энергии в месяц на 24 месяца на содержание призывников.", () =>
                            {
                                int sys = SpawnSystem(null);
                                if (sys >= 0)
                                    for (int i = 0; i < 2; i++)
                                        FleetManager.Instance?.CreateFleetObject($"Легион призыва {FleetManager.Instance.AllFleets.Count + 1}", sys, FleetType.Military);
                                EmpireEffects.Add("doctrine_xarn", "Всеобщая мобилизация", "Смотр легионов", 24, energy: -4 * k);
                            }),
                            new EventOption("ВОЕННАЯ ЭКОНОМИКА", $"+{R(5 * k)} сплавов и −{R(3 * k)} минералов в месяц на 24 месяца.",
                                () => EmpireEffects.Add("doctrine_xarn", "Военная экономика", "Смотр легионов", 24, minerals: -3 * k, alloys: 5 * k)),
                            new EventOption("ТРИУМФАЛЬНЫЙ ПАРАД", $"Сразу +{R(50 * k)} влияния и +1 влияния в месяц на 24 месяца.", () =>
                            {
                                Give(influence: 50 * k);
                                EmpireEffects.Add("doctrine_xarn", "Слава триумфа", "Смотр легионов", 24, influence: 1f);
                            }),
                            new EventOption("УЧЕНИЯ ВЕТЕРАНОВ", "Каждый адмирал получает 120 опыта.", () =>
                            {
                                foreach (var l in new List<Leader>(PlayerLeaders(LeaderClass.Admiral))) Leaders?.GrantXp(l, 120f);
                            }, "Нужен адмирал", () => hasAdmiral)));
                }
            });

            // ---- Синдикат Аквила: собрание акционеров ----
            Add(new Template
            {
                Id = "decision_aquila", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    int peaceful = 0;
                    foreach (var ai in AIEmpireManager.Alive) if (!ai.AtWar) peaceful++;
                    float invest = 150f * k;
                    return Ev("decision_aquila", "СОБРАНИЕ АКЦИОНЕРОВ",
                        "Совет директоров собирает акционеров Синдиката. Отчёт о прибыли зачитан, бокалы подняты, аналитики " +
                        "разложили графики по столам. Генеральный директор должен назвать стратегию на ближайшие два года.",
                        AnomalyType.Diplomatic, Opts(
                            new EventOption("ВЕНЧУРНЫЕ ИНВЕСТИЦИИ", $"Вложить {R(invest)} энергии. Через год: 85% — вернуть почти вдвое больше, 15% — проект провалится.", () =>
                            {
                                Pay(energy: invest);
                                Schedule("aquila_dividends", 360, new EventContext());
                            }, $"Нужно {R(invest)} энергии", () => CanPay(energy: invest)),
                            new EventOption("РУДНАЯ МОНОПОЛИЯ", $"+{R(6 * k)} минералов и +{R(3 * k)} энергии в месяц на 24 месяца. Соседи недовольны: −8 к отношению всех империй.", () =>
                            {
                                EmpireEffects.Add("contract_aquila", "Рудная монополия", "Собрание акционеров", 24, energy: 3 * k, minerals: 6 * k);
                                OpinionAll(-8f);
                            }),
                            new EventOption("ТОРГОВЫЕ ПРЕДСТАВИТЕЛЬСТВА", $"+{R(2 * k)} энергии в месяц за каждую мирную империю (сейчас {peaceful}) на 24 месяца и +4 к их отношению.", () =>
                            {
                                EmpireEffects.Add("contract_aquila", "Торговые представительства", "Собрание акционеров", 24, energy: 2 * k * peaceful);
                                foreach (var ai in AIEmpireManager.Alive) if (!ai.AtWar) ai.AddMemory("trade", 4f);
                            }, "Нужна хотя бы одна мирная империя", () => peaceful > 0),
                            new EventOption("ВЫКУП ДОЛГОВ", $"Сразу +{R(70 * k)} влияния, −{R(4 * k)} энергии в месяц на 24 месяца.", () =>
                            {
                                Give(influence: 70 * k);
                                EmpireEffects.Add("contract_aquila", "Выплата долгов", "Собрание акционеров", 24, energy: -4 * k);
                            })));
                }
            });

            Add(new Template
            {
                Id = "aquila_dividends", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    if (Random.value < 0.85f)
                        return Ev("aquila_dividends", "ДИВИДЕНДЫ",
                            "Год назад Синдикат вложился в рискованные стартапы на окраинах. Один из них — фабрика квантовых " +
                            "аккумуляторов — выстрелил. Акционеры довольны, генеральный директор принимает поздравления.",
                            AnomalyType.Diplomatic, Opts(
                                new EventOption("ПОЛУЧИТЬ ПРИБЫЛЬ", $"+{R(280 * k)} энергии.", () => Give(energy: 280 * k))));
                    return Ev("aquila_dividends", "ИНВЕСТИЦИИ СГОРЕЛИ",
                        "Фабрика на окраине оказалась пустой коробкой, а её основатель исчез вместе с деньгами. " +
                        "Юристы Синдиката нашли лишь часть активов.",
                        AnomalyType.Diplomatic, Opts(
                            new EventOption("СПИСАТЬ УБЫТКИ", $"Вернуть хоть что-то: +{R(40 * k)} энергии.", () => Give(energy: 40 * k)),
                            new EventOption("НАЙТИ И НАКАЗАТЬ", "−20 влияния; учёт в Синдикате станет строже: +2 энергии в месяц на 24 месяца.", () =>
                            {
                                Pay(influence: 20f);
                                EmpireEffects.Add("aquila_audit", "Строгий аудит", "Последствия провала", 24, energy: 2f);
                            }, "Нужно 20 влияния", () => CanPay(influence: 20f))));
                }
            });

            // ---- Иридийский Оракул: видения ----
            Add(new Template
            {
                Id = "decision_iridia", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    var near = UnsurveyedNearPlayer(3);
                    return Ev("decision_iridia", "ВИДЕНИЯ ОРАКУЛА",
                        "Раз в два года Верховная Оракул Аурэлия погружается в Сон Пророчеств. Пробудившись, она говорит о том, что видела: " +
                        "звёзды, которые ещё никто не посещал, урожай, которого ещё нет, войны, которые ещё не начались. " +
                        "Но удержать и воплотить можно лишь одно видение. Какое из них станет судьбой Иридии?",
                        AnomalyType.SpatialRift, Opts(
                            new EventOption("ВИДЕНИЕ ЗВЁЗД", $"Разведать все системы в 3 прыжках от ваших границ (сейчас таких {near.Count}).", () =>
                            {
                                foreach (var s in near) { s.GeneratePlanets(); s.MarkSurveyedBy(0); }
                                GalaxyView.Instance?.RefreshTerritoryVisuals();
                                NotificationCenter.Show("Видение звёзд", $"Оракул открыла {near.Count} сист.", NotificationCenter.Kind.Success, 6f);
                            }, "Нет неизученных систем рядом", () => near.Count > 0),
                            new EventOption("ВИДЕНИЕ ИЗОБИЛИЯ", $"+{R(5 * k)} энергии и +{R(5 * k)} минералов в месяц на 24 месяца.",
                                () => EmpireEffects.Add("vision_iridia", "Видение изобилия", "Видения Оракула", 24, energy: 5 * k, minerals: 5 * k)),
                            new EventOption("ВИДЕНИЕ ГРЯДУЩЕГО", $"+{R(220 * k)} науки сразу; 35% — мгновенный прорыв случайной технологии.", () =>
                            {
                                Science(220 * k);
                                if (Random.value < 0.35f) TechnologyManager.Instance?.CompleteRandomTech();
                            }),
                            new EventOption("ВИДЕНИЕ ВОЙНЫ", $"Узнать намерения каждого соседа и +{R(4 * k)} сплавов в месяц на 24 месяца на подготовку.", () =>
                            {
                                foreach (var ai in AIEmpireManager.Alive)
                                {
                                    bool hostile = ai.AtWar || ai.Opinion <= ai.Profile.WarOpinionThreshold + 15f;
                                    NotificationCenter.Show($"Видение: {ai.AIName}",
                                        ai.AtWar ? "уже воюет с вами и не отступит легко"
                                        : hostile ? $"готовится к войне против вас (отношение {ai.Opinion:+0;-0;0})"
                                        : $"намерения мирные (отношение {ai.Opinion:+0;-0;0})",
                                        hostile ? NotificationCenter.Kind.Warning : NotificationCenter.Kind.Info, 9f);
                                }
                                EmpireEffects.Add("vision_iridia", "Видение войны", "Видения Оракула", 24, alloys: 4 * k);
                            })));
                }
            });

            // ---- Конклав Тэрра'ан: Совет Корней ----
            Add(new Template
            {
                Id = "decision_terraan", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    var near = UnsurveyedNearPlayer(4);
                    int damaged = 0;
                    var fm = FleetManager.Instance;
                    if (fm != null)
                        foreach (var f in fm.AllFleets)
                            if (f?.Data != null && f.Data.OwnerId == 0 && !f.Data.Destroyed &&
                                (f.Data.HullPoints < f.Data.MaxHullPoints || f.Data.ArmorPoints < f.Data.MaxArmorPoints)) damaged++;
                    return Ev("decision_terraan", "СОВЕТ КОРНЕЙ",
                        "Раз в два года Конклав сплетает корни всех миров в единый Совет. Сквозь мицелий звучат миллионы голосов, " +
                        "и Хранительница Исинна Кральтэр выслушивает каждый — это занимает недели. Совет может направить соки сада лишь в одну сторону. " +
                        "Куда потечёт сила Конклава в этот раз?",
                        AnomalyType.AlienFauna, Opts(
                            new EventOption("СПОРЫ В ПУСТОТУ", $"Споры мицелия разносят разведку на 4 прыжка от границ: изучено систем — {near.Count}.", () =>
                            {
                                foreach (var s in near) { s.GeneratePlanets(); s.MarkSurveyedBy(0); s.IsSurveyed = true; Vision.MarkExplored(0, s.Id); }
                                GalaxyView.Instance?.RefreshTerritoryVisuals();
                                NotificationCenter.Show("Споры в пустоту", $"Мицелий изучил {near.Count} сист.", NotificationCenter.Kind.Success, 6f);
                            }, "Нет неизученных систем рядом", () => near.Count > 0),
                            new EventOption("ПРОБУЖДЕНИЕ РОЩ", $"Все ваши корабли мгновенно заживают ({damaged} повреждено), щиты полны; +{R(3 * k)} сплавов в месяц на 24 месяца.", () =>
                            {
                                if (fm != null)
                                    foreach (var f in fm.AllFleets)
                                    {
                                        var d = f?.Data;
                                        if (d == null || d.OwnerId != 0 || d.Destroyed) continue;
                                        d.HullPoints = d.MaxHullPoints; d.ArmorPoints = d.MaxArmorPoints; d.ShieldPoints = d.MaxShieldPoints;
                                    }
                                EmpireEffects.Add("council_terraan", "Пробуждение рощ", "Совет Корней", 24, alloys: 3 * k);
                            }),
                            new EventOption("ДОЛГИЙ СЕЗОН", $"Сады плодоносят: +{R(6 * k)} минералов и +{R(2 * k)} влияния в месяц на 24 месяца.",
                                () => EmpireEffects.Add("council_terraan", "Долгий сезон", "Совет Корней", 24, minerals: 6 * k, influence: 2 * k)),
                            new EventOption("ПАМЯТЬ ДРЕВНИХ", $"Старейшие деревья делятся знанием: +{R(180 * k)} науки и +{R(3 * k)} науки в месяц на 24 месяца.", () =>
                            {
                                Science(180 * k);
                                EmpireEffects.Add("council_terraan", "Память древних", "Совет Корней", 24, science: 3 * k);
                            })));
                }
            });
        }

        // ==================== УГРОЗЫ ====================

        private static void AddThreatEvents()
        {
            Add(new Template
            {
                Id = "pirate_haven", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    float tribute = 150f * k, letters = 100f * k;
                    string where = c?.System != null ? $"системе {c.System.Name}" : "ничейной системе у границ";
                    return Ev("pirate_haven", "ПИРАТСКАЯ ГАВАНЬ",
                        $"В {where} обосновались пираты: отставные военные, беглые каторжники и просто те, кому тесно в границах империй. " +
                        "Их первая банда уже вышла на промысел, и налёты будут повторяться: пираты грабят слабо защищённые колонии " +
                        "у самых границ. Совет ждёт решения.",
                        AnomalyType.DistressSignal, Opts(
                            new EventOption("ПЛАТИТЬ ДАНЬ", $"−{R(tribute)} энергии: 3 года пираты не трогают ваши системы.", () =>
                            {
                                Pay(energy: tribute);
                                ThreatManager.Instance?.SetTribute(36);
                            }, $"Нужно {R(tribute)} энергии", () => CanPay(energy: tribute)),
                            new EventOption("ОБЪЯВИТЬ НАГРАДУ", "−30 влияния: 3 года за каждый уничтоженный вами пиратский корабль +40 энергии и +4 влияния.", () =>
                            {
                                Pay(influence: 30f);
                                ThreatManager.Instance?.SetBounty(36);
                            }, "Нужно 30 влияния", () => CanPay(influence: 30f)),
                            new EventOption("ВЫДАТЬ КАПЕРСКИЕ ГРАМОТЫ", $"−{R(letters)} сплавов: три следующих налёта обрушатся на соперников (чаще на тех, с кем вы воюете). 30% — сделка вскроется: −10 к отношению всех империй.", () =>
                            {
                                Pay(alloys: letters);
                                ThreatManager.Instance?.SetPrivateers(3);
                                if (Random.value < 0.3f)
                                {
                                    OpinionAll(-10f);
                                    NotificationCenter.Show("Сделка с пиратами вскрылась", "Соседи узнали о каперских грамотах", NotificationCenter.Kind.Warning, 7f);
                                }
                            }, $"Нужно {R(letters)} сплавов", () => CanPay(alloys: letters)),
                            new EventOption("ПУСТЬ ПОПРОБУЮТ", "Ничего не делать. Держите военный флот у пограничных колоний — пираты избегают защищённых систем.", () => { })));
                }
            });

            Add(new Template
            {
                Id = "leviathan_slain", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    bool guardCore = HasFlag("precursor_guard");
                    string where = c?.System != null ? c.System.Name : "далёкой системе";
                    string text = $"Чудовище, веками стерёгшее систему {where}, мертво. Его туша медленно дрейфует среди обломков, " +
                                  "а система наконец открыта для освоения. Учёные и инженеры спорят, что делать с останками.";
                    if (guardCore)
                        text += "\n\nВнутри стража, в оболочке из живого кристалла, бьётся ядро Предтеч — то, ради чего хранилище и охранялось.";
                    void AfterChoice()
                    {
                        if (!guardCore) return;
                        ClearFlag("precursor_guard");
                        Schedule("precursor_core", 15, c);
                    }
                    return Ev("leviathan_slain", "ЛЕВИАФАН ПОВЕРЖЕН", text, AnomalyType.AlienFauna, Opts(
                        new EventOption("РАЗДЕЛАТЬ НА СПЛАВЫ", $"+{R(300 * k)} сплавов.", () => { Give(alloys: 300 * k); AfterChoice(); }),
                        new EventOption("ИЗУЧИТЬ ОРГАНЫ ЧУДОВИЩА", $"+{R(350 * k)} науки и +3 науки в месяц навсегда.", () =>
                        {
                            Science(350 * k);
                            TechnologyManager.Instance?.AddMonthlyScience(3f);
                            AfterChoice();
                        }),
                        new EventOption("ВЫСТАВИТЬ ТРОФЕЙ", "+80 влияния и +10 к отношению всех империй: о подвиге говорит вся галактика.", () =>
                        {
                            Give(influence: 80f);
                            OpinionAll(10f);
                            AfterChoice();
                        })));
                }
            });
        }

        // ==================== НАСЛЕДИЕ ПРЕДТЕЧ ====================

        private static void AddPrecursorChain()
        {
            // 1. Маяк
            Add(new Template
            {
                Id = "precursor_signal", When = Trigger.Survey, Weight = 0.6f, Once = true,
                CanFire = c => c?.System != null && (TimeManager.Instance == null || TimeManager.Instance.Year >= 2203),
                Build = c =>
                {
                    float k = K();
                    return Ev("precursor_signal", "МАЯК ПРЕДТЕЧ",
                        "На орбите безжизненной планеты научное судно нашло маяк — объект из сплава, которого нет в наших справочниках. " +
                        "Он передаёт один и тот же фрагмент сигнала, а лингвистический модуль уверен: это часть координат. " +
                        "Кто бы ни оставил маяк, он хотел, чтобы его нашли.",
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption("НАЧАТЬ РАСШИФРОВКУ", "Цепочка «Наследие Предтеч»: через 3 месяца учёные доложат о результатах.", () =>
                            {
                                Xp(c, 30f);
                                Schedule("precursor_decoding", 90, c);
                            }),
                            new EventOption("ПРОДАТЬ МАЯК СОСЕДЯМ", $"+{R(150 * k)} энергии и +8 к отношению случайной мирной империи. Наследие уйдёт к ней.", () =>
                            {
                                Give(energy: 150 * k);
                                RandomPeacefulRival()?.AddMemory("incident", 8f);
                            }),
                            new EventOption("УНИЧТОЖИТЬ МАЯК", "+40 влияния: суеверные колонисты успокоены.", () => Give(influence: 40f))));
                }
            });

            // 2. Расшифровка
            Add(new Template
            {
                Id = "precursor_decoding", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    float cost = 100f * k;
                    var council = Leaders?.CouncilScientist(0);
                    var partner = RandomPeacefulRival();
                    return Ev("precursor_decoding", "РАСШИФРОВКА СИГНАЛА",
                        "Учёные собрали три фрагмента координат из пяти. Остальные, судя по всему, передают другие маяки — " +
                        "их слышат и соседние империи. Восстановить карту можно своими силами, но это долго и дорого; " +
                        "можно поручить работу научному совету или договориться с соседями.",
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption("СВОИМИ СИЛАМИ", $"−{R(cost)} энергии. Карта будет готова через 5 месяцев.", () =>
                            {
                                Pay(energy: cost);
                                Schedule("precursor_map", 150, c);
                            }, $"Нужно {R(cost)} энергии", () => CanPay(energy: cost)),
                            new EventOption("ПОРУЧИТЬ НАУЧНОМУ СОВЕТУ", "Карта через 2 месяца, учёный совета получает 60 опыта.", () =>
                            {
                                if (council != null) Leaders?.GrantXp(council, 60f);
                                Schedule("precursor_map", 60, c);
                            }, "Нужен учёный в научном совете", () => council != null),
                            new EventOption("ОБМЕНЯТЬСЯ С СОСЕДЯМИ", partner != null
                                ? $"Карта через 3 месяца; +10 к отношению империи {partner.AIName}, но она тоже получит часть наследия."
                                : "Нет мирных соседей.", () =>
                            {
                                if (partner == null) return;
                                partner.AddMemory("incident", 10f);
                                SetFlag("precursor_shared");
                                Schedule("precursor_map", 90, new EventContext { System = c?.System, Rival = partner.OwnerId });
                            }, "Нужна мирная империя", () => partner != null)));
                }
            });

            // 3. Карта
            Add(new Template
            {
                Id = "precursor_map", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    var target = PickVaultSystem();
                    var ctx = new EventContext { System = target ?? c?.System, Rival = c?.Rival ?? -1 };
                    float alloys = 120f * k, escortAlloys = 80f * k, escortEnergy = 60f * k;
                    string where = target != null ? $"мёртвую систему {target.Name}" : "мёртвую систему на окраине";
                    return Ev("precursor_map", "КАРТА ПРЕДТЕЧ",
                        $"Координаты сложились. Они указывают на {where}: ни одна наша экспедиция там не бывала. " +
                        "Если верить карте, там хранилище — то, что Предтечи хотели сохранить. Или то, что они хотели спрятать.",
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption("СНАРЯДИТЬ ЭКСПЕДИЦИЮ", $"−{R(alloys)} сплавов. Через 4 месяца экспедиция доберётся до хранилища.", () =>
                            {
                                Pay(alloys: alloys);
                                Schedule("precursor_vault", 120, ctx);
                            }, $"Нужно {R(alloys)} сплавов", () => CanPay(alloys: alloys)),
                            new EventOption("ЭКСПЕДИЦИЯ ПОД ОХРАНОЙ ФЛОТА", $"−{R(escortAlloys)} сплавов и −{R(escortEnergy)} энергии. Безопаснее: если хранилище охраняется, эскорт примет удар.", () =>
                            {
                                Pay(energy: escortEnergy, alloys: escortAlloys);
                                SetFlag("precursor_escort");
                                Schedule("precursor_vault", 120, ctx);
                            }, "Нужны сплавы и энергия", () => CanPay(energy: escortEnergy, alloys: escortAlloys)),
                            new EventOption("ОТЛОЖИТЬ НА ГОД", "Вернуться к карте через год.", () => Schedule("precursor_map", 360, c))));
                }
            });

            // 4. Хранилище
            Add(new Template
            {
                Id = "precursor_vault", When = Trigger.Chain,
                Build = c =>
                {
                    bool escort = HasFlag("precursor_escort");
                    float chance = escort ? 0.8f : 0.6f;
                    return Ev("precursor_vault", "ХРАНИЛИЩЕ ПРЕДТЕЧ",
                        "Экспедиция нашла хранилище. Его двери не открывались десятки тысяч лет, а в глубине что-то медленно просыпается: " +
                        "датчики фиксируют растущий поток энергии и очертания огромного тела в стазисе. Решать нужно сейчас.",
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption("ВЗЛОМАТЬ ДВЕРИ", $"{R(chance * 100)}% — ядро Предтеч ваше. Иначе пробудится страж-левиафан у ваших границ, и ядро достанется тому, кто его убьёт.", () =>
                            {
                                if (Random.value < chance) { Schedule("precursor_core", 20, c); return; }
                                SetFlag("precursor_guard");
                                int sys = ThreatManager.Instance != null ? ThreatManager.Instance.AwakenLeviathan(0, "Страж Предтеч", 1.15f) : -1;
                                var s = EmpireStats.GetSystem(sys);
                                NotificationCenter.Show("СТРАЖ ПРОБУДИЛСЯ",
                                    s != null ? $"Страж Предтеч вырвался из хранилища и занял систему {s.Name}. Ядро — внутри него" : "Страж Предтеч вырвался из хранилища",
                                    NotificationCenter.Kind.Danger, 10f);
                            }),
                            new EventOption("ИЗУЧАТЬ ОСТОРОЖНО", "Без риска, но долго: ядро будет извлечено через 8 месяцев.", () => Schedule("precursor_core", 240, c)),
                            new EventOption("ЗАПЕЧАТАТЬ НАВСЕГДА", "+60 влияния и +10 к отношению Иридийского Оракула: некоторые двери должны оставаться закрытыми.", () =>
                            {
                                Give(influence: 60f);
                                foreach (var ai in AIEmpireManager.Alive)
                                    if (ai.Personality == AIPersonality.Mystic) ai.AddMemory("incident", 10f);
                                ClearFlag("precursor_escort");
                            })));
                }
            });

            // 5. Ядро
            Add(new Template
            {
                Id = "precursor_core", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    var partner = c != null && c.Rival >= 0 ? AIEmpireManager.For(c.Rival) : null;
                    bool shared = HasFlag("precursor_shared") && partner != null && !partner.IsEliminated;
                    string text = "Перед нами ядро Предтеч — кристалл размером с дом, внутри которого течёт свет. Его можно встроить " +
                                  "в наши научные сети, переплавить в оружие или отдать галактике. Решение изменит империю навсегда.";
                    if (shared) text += $"\n\nПо договору копию данных получит {partner.AIName}.";
                    void Finish()
                    {
                        SetFlag("precursor_core_done");
                        ClearFlag("precursor_escort");
                        if (shared)
                        {
                            partner.Alloys += 200f;
                            partner.AddMemory("incident", 6f);
                            NotificationCenter.Show("Наследие поделено", $"{partner.AIName} получили данные ядра Предтеч", NotificationCenter.Kind.Info, 6f);
                        }
                        ClearFlag("precursor_shared");
                    }
                    return Ev("precursor_core", "ЯДРО ПРЕДТЕЧ", text, AnomalyType.PrecursorRelic, Opts(
                        new EventOption("ВСТРОИТЬ В НАУЧНУЮ СЕТЬ", "+12 науки в месяц навсегда.", () =>
                        {
                            TechnologyManager.Instance?.AddMonthlyScience(12f);
                            Finish();
                        }),
                        new EventOption("ПЕРЕПЛАВИТЬ В ОРУЖИЕ", $"+{R(250 * k)} сплавов сразу и +6 сплавов в месяц навсегда. Ядро будет звучать и дальше…", () =>
                        {
                            Give(alloys: 250 * k);
                            Eco?.AddIncome(0f, 0f, 6f, 0f);
                            Schedule("precursor_echo", 720, c);
                            Finish();
                        }),
                        new EventOption("ОТДАТЬ ГАЛАКТИКЕ", "+100 влияния и +15 к отношению всех империй.", () =>
                        {
                            Give(influence: 100f);
                            OpinionAll(15f);
                            Finish();
                        })));
                }
            });

            // 6. Эхо — последствие выбора «в оружие» через два года
            Add(new Template
            {
                Id = "precursor_echo", When = Trigger.Chain,
                Build = c => Ev("precursor_echo", "ЭХО ПРЕДТЕЧ",
                    "Переплавленное ядро всё ещё звучит. Его эхо разошлось по гиперкоридорам, и в ничейной системе у наших границ " +
                    "что-то откликнулось: датчики видят пробуждение древнего стража. Он идёт на зов. Можно встретить его — " +
                    "или заглушить ядро, отказавшись от части его силы.",
                    AnomalyType.SpatialRift, Opts(
                        new EventOption("К БОЮ", "У ваших границ пробудится левиафан «Эхо Предтеч». Победа над ним принесёт большую награду.", () =>
                        {
                            int sys = ThreatManager.Instance != null ? ThreatManager.Instance.AwakenLeviathan(0, "Эхо Предтеч", 1f) : -1;
                            var s = EmpireStats.GetSystem(sys);
                            if (s != null) NotificationCenter.Show("Эхо Предтеч", $"Страж пробудился в системе {s.Name}", NotificationCenter.Kind.Danger, 8f);
                        }),
                        new EventOption("ЗАГЛУШИТЬ ЯДРО", "−4 сплава в месяц навсегда (из 6, что даёт ядро), страж не проснётся.",
                            () => Eco?.AddIncome(0f, 0f, -4f, 0f))))
            });
        }

        /// <summary>Хранилище — ничейная система подальше от игрока (иначе любая ничейная).</summary>
        private static StarSystem PickVaultSystem()
        {
            var near = new HashSet<int>();
            foreach (var s in EmpireStats.Systems)
                if (s.OwnerId == 0) { near.Add(s.Id); foreach (int n in s.ConnectedSystemIds) near.Add(n); }
            var far = new List<StarSystem>();
            var any = new List<StarSystem>();
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != -1 || s.ConnectedSystemIds.Count == 0) continue;
                any.Add(s);
                if (!near.Contains(s.Id)) far.Add(s);
            }
            return RandomOf(far.Count > 0 ? far : any);
        }

        // ==================== АМБИЦИИ ЛИДЕРОВ ====================

        private static string EventTraitFor(LeaderClass cls) =>
            cls == LeaderClass.Scientist ? "sci_brilliant" : cls == LeaderClass.Admiral ? "adm_veteran" : "gov_honest";

        private static void AddLeaderAmbitions()
        {
            Add(new Template
            {
                Id = "leader_ambition", When = Trigger.Monthly, Weight = 0.6f,
                FindContext = () => LeaderCtx(l => l.Level >= 3 && !l.Has(EventTraitFor(l.Class))),
                Build = c =>
                {
                    var l = c.Leader;
                    string cls = LeaderManager.ClassName(l.Class).ToLower();
                    return Ev("leader_ambition", "АМБИЦИИ",
                        $"{l.Name}, {cls} {l.Level}-го уровня, просит аудиенции. Годы службы дали опыт, связи и репутацию — " +
                        "и теперь лидер хочет большего: признания, власти или громкого дела, о котором заговорят по всей галактике. " +
                        "Отказ запомнится.",
                        AnomalyType.LeaderStory, Opts(
                            new EventOption("НАГРАДИТЬ ОРДЕНОМ", "−40 влияния; лидер получает 150 опыта.", () =>
                            {
                                Pay(influence: 40f);
                                Leaders?.GrantXp(l, 150f);
                            }, "Нужно 40 влияния", () => CanPay(influence: 40f)),
                            new EventOption("ДАТЬ ОСОБОЕ ПОРУЧЕНИЕ", $"Через 4 месяца: 65% — успех (черта «{TraitName(EventTraitFor(l.Class))}» и 200 опыта), 35% — провал, лидер может уйти в отставку.",
                                () => Schedule("leader_ambition_result", 120, c)),
                            new EventOption("ОТКАЗАТЬ", "30% — лидер уйдёт в отставку.", () =>
                            {
                                if (Random.value < 0.3f)
                                    Leaders?.Retire(l, "Отставка", $"{l.Name} не простил отказа и покинул службу");
                            })));
                }
            });

            Add(new Template
            {
                Id = "leader_ambition_result", When = Trigger.Chain,
                CanFire = c => c?.Leader != null,
                Build = c =>
                {
                    var l = c.Leader;
                    string trait = EventTraitFor(l.Class);
                    if (Random.value < 0.65f)
                        return Ev("leader_ambition_result", "ПОРУЧЕНИЕ ВЫПОЛНЕНО",
                            $"{l.Name} блестяще справился с особым поручением. Об этом пишут во всех новостных лентах, " +
                            "а подчинённые смотрят на лидера с новым уважением.",
                            AnomalyType.LeaderStory, Opts(
                                new EventOption("ВОЗДАТЬ ПОЧЕСТИ", $"Черта «{TraitName(trait)}» и 200 опыта.", () =>
                                {
                                    Leaders?.GiveTrait(l, trait);
                                    Leaders?.GrantXp(l, 200f);
                                })));
                    return Ev("leader_ambition_result", "ПОРУЧЕНИЕ ПРОВАЛЕНО",
                        $"Особое поручение обернулось провалом. {l.Name} винит обстоятельства и просит о новом шансе, " +
                        "но в правительстве уже шепчутся об отставке.",
                        AnomalyType.LeaderStory, Opts(
                            new EventOption("ПРИНЯТЬ ОТСТАВКУ", "Лидер покидает службу.", () =>
                                Leaders?.Retire(l, "Отставка", $"{l.Name} ушёл в отставку после провала")),
                            new EventOption("ДАТЬ ВТОРОЙ ШАНС", "−30 влияния; лидер остаётся и получает 60 опыта за усвоенный урок.", () =>
                            {
                                Pay(influence: 30f);
                                Leaders?.GrantXp(l, 60f);
                            }, "Нужно 30 влияния", () => CanPay(influence: 30f))));
                }
            });
        }
    }
}
