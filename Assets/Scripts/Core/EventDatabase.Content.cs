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

        /// <summary>Рост наград со временем: к 2250 году события щедрее в 2,5 раза.</summary>
        private static float K()
        {
            int year = TimeManager.Instance != null ? TimeManager.Instance.Year : 2200;
            return Mathf.Clamp(1f + (year - 2200) * 0.03f, 1f, 2.5f);
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
                    return Ev("decision_astrea", Loc.T("СЕССИЯ СОВЕТА РЕСПУБЛИКИ"),
                        Loc.T("Раз в два года Совет Республики собирается, чтобы утвердить главный закон на ближайшие годы. ") +
                        Loc.T("Фракции спорят до хрипоты, журналисты дежурят у дверей зала, но последнее слово, как всегда, за Председателем. ") +
                        Loc.T("Какой курс выберет Республика?"),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("ЗАКОН О НАУЧНЫХ ГРАНТАХ"), Loc.F("+{0} науки и −{1} гелия-3 в месяц на 24 месяца.", R(8 * k), R(3 * k)),
                                () => EmpireEffects.Add("law_astrea", Loc.T("Закон о научных грантах"), Loc.T("Совет Республики"), 24, energy: -3 * k, science: 8 * k)),
                            new EventOption(Loc.T("ПРОГРАММА ОСВОЕНИЯ"), Loc.F("+2 влияния и +{0} титана в месяц на 24 месяца.", R(4 * k)),
                                () => EmpireEffects.Add("law_astrea", Loc.T("Программа освоения"), Loc.T("Совет Республики"), 24, minerals: 4 * k, influence: 2f)),
                            new EventOption(Loc.T("ОБОРОННЫЙ БЮДЖЕТ"), Loc.F("+{0} сплавов и −{1} гелия-3 в месяц на 24 месяца.", R(4 * k), R(3 * k)),
                                () => EmpireEffects.Add("law_astrea", Loc.T("Оборонный бюджет"), Loc.T("Совет Республики"), 24, energy: -3 * k, alloys: 4 * k)),
                            new EventOption(Loc.T("ВСЕНАРОДНЫЙ РЕФЕРЕНДУМ"), Loc.F("Закон не принимается, зато народ доверяет Совету: сразу +{0} влияния.", R(40 * k)),
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
                    return Ev("decision_xarn", Loc.T("СМОТР ЛЕГИОНОВ"),
                        Loc.T("Каждые два года верховный маршал принимает смотр легионов Доминиона. Над плацем гремят марши, ") +
                        Loc.T("дредноуты висят над столицей в парадном строю. Генералы ждут приказа: куда направить силу империи?"),
                        AnomalyType.MilitaryIncident, Opts(
                            new EventOption(Loc.T("ВСЕОБЩАЯ МОБИЛИЗАЦИЯ"), Loc.F("Сразу 2 корвета у верфи; −{0} гелия-3 в месяц на 24 месяца на содержание призывников.", R(4 * k)), () =>
                            {
                                int sys = SpawnSystem(null);
                                if (sys >= 0)
                                    for (int i = 0; i < 2; i++)
                                        FleetManager.Instance?.CreateFleetObject(Loc.F("Легион призыва {0}", FleetManager.Instance.AllFleets.Count + 1), sys, FleetType.Military);
                                EmpireEffects.Add("doctrine_xarn", Loc.T("Всеобщая мобилизация"), Loc.T("Смотр легионов"), 24, energy: -4 * k);
                            }),
                            new EventOption(Loc.T("ВОЕННАЯ ЭКОНОМИКА"), Loc.F("+{0} сплавов и −{1} титана в месяц на 24 месяца.", R(5 * k), R(3 * k)),
                                () => EmpireEffects.Add("doctrine_xarn", Loc.T("Военная экономика"), Loc.T("Смотр легионов"), 24, minerals: -3 * k, alloys: 5 * k)),
                            new EventOption(Loc.T("ТРИУМФАЛЬНЫЙ ПАРАД"), Loc.F("Сразу +{0} влияния и +1 влияния в месяц на 24 месяца.", R(50 * k)), () =>
                            {
                                Give(influence: 50 * k);
                                EmpireEffects.Add("doctrine_xarn", Loc.T("Слава триумфа"), Loc.T("Смотр легионов"), 24, influence: 1f);
                            }),
                            new EventOption(Loc.T("УЧЕНИЯ ВЕТЕРАНОВ"), Loc.T("Каждый адмирал получает 120 опыта."), () =>
                            {
                                foreach (var l in new List<Leader>(PlayerLeaders(LeaderClass.Admiral))) Leaders?.GrantXp(l, 120f);
                            }, Loc.T("Нужен адмирал"), () => hasAdmiral)));
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
                    return Ev("decision_aquila", Loc.T("СОБРАНИЕ АКЦИОНЕРОВ"),
                        Loc.T("Совет директоров собирает акционеров Синдиката. Отчёт о прибыли зачитан, бокалы подняты, аналитики ") +
                        Loc.T("разложили графики по столам. Генеральный директор должен назвать стратегию на ближайшие два года."),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("ВЕНЧУРНЫЕ ИНВЕСТИЦИИ"), Loc.F("Вложить {0} гелия-3. Через год: 85% — вернуть почти вдвое больше, 15% — проект провалится.", R(invest)), () =>
                            {
                                Pay(energy: invest);
                                Schedule("aquila_dividends", 360, new EventContext());
                            }, Need(energy: invest), () => CanPay(energy: invest)),
                            new EventOption(Loc.T("РУДНАЯ МОНОПОЛИЯ"), Loc.F("+{0} титана и +{1} гелия-3 в месяц на 24 месяца. Соседи недовольны: −8 к отношению всех империй.", R(6 * k), R(3 * k)), () =>
                            {
                                EmpireEffects.Add("contract_aquila", Loc.T("Рудная монополия"), Loc.T("Собрание акционеров"), 24, energy: 3 * k, minerals: 6 * k);
                                OpinionAll(-8f);
                            }),
                            new EventOption(Loc.T("ТОРГОВЫЕ ПРЕДСТАВИТЕЛЬСТВА"), Loc.F("+{0} гелия-3 в месяц за каждую мирную империю (сейчас {1}) на 24 месяца и +4 к их отношению.", R(2 * k), peaceful), () =>
                            {
                                EmpireEffects.Add("contract_aquila", Loc.T("Торговые представительства"), Loc.T("Собрание акционеров"), 24, energy: 2 * k * peaceful);
                                foreach (var ai in AIEmpireManager.Alive) if (!ai.AtWar) ai.AddMemory("trade", 4f);
                            }, Loc.T("Нужна хотя бы одна мирная империя"), () => peaceful > 0),
                            new EventOption(Loc.T("ВЫКУП ДОЛГОВ"), Loc.F("Сразу +{0} влияния, −{1} гелия-3 в месяц на 24 месяца.", R(70 * k), R(4 * k)), () =>
                            {
                                Give(influence: 70 * k);
                                EmpireEffects.Add("contract_aquila", Loc.T("Выплата долгов"), Loc.T("Собрание акционеров"), 24, energy: -4 * k);
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
                        return Ev("aquila_dividends", Loc.T("ДИВИДЕНДЫ"),
                            Loc.T("Год назад Синдикат вложился в рискованные стартапы на окраинах. Один из них — фабрика квантовых ") +
                            Loc.T("аккумуляторов — выстрелил. Акционеры довольны, генеральный директор принимает поздравления."),
                            AnomalyType.Diplomatic, Opts(
                                new EventOption(Loc.T("ПОЛУЧИТЬ ПРИБЫЛЬ"), Loc.F("+{0} гелия-3.", R(280 * k)), () => Give(energy: 280 * k))));
                    return Ev("aquila_dividends", Loc.T("ИНВЕСТИЦИИ СГОРЕЛИ"),
                        Loc.T("Фабрика на окраине оказалась пустой коробкой, а её основатель исчез вместе с деньгами. ") +
                        Loc.T("Юристы Синдиката нашли лишь часть активов."),
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("СПИСАТЬ УБЫТКИ"), Loc.F("Вернуть хоть что-то: +{0} гелия-3.", R(40 * k)), () => Give(energy: 40 * k)),
                            new EventOption(Loc.T("НАЙТИ И НАКАЗАТЬ"), Loc.T("−20 влияния; учёт в Синдикате станет строже: +2 гелия-3 в месяц на 24 месяца."), () =>
                            {
                                Pay(influence: 20f);
                                EmpireEffects.Add("aquila_audit", Loc.T("Строгий аудит"), Loc.T("Последствия провала"), 24, energy: 2f);
                            }, Need(influence: 20f), () => CanPay(influence: 20f))));
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
                    return Ev("decision_iridia", Loc.T("ВИДЕНИЯ ОРАКУЛА"),
                        Loc.T("Раз в два года Верховная Оракул Аурэлия погружается в Сон Пророчеств. Пробудившись, она говорит о том, что видела: ") +
                        Loc.T("звёзды, которые ещё никто не посещал, урожай, которого ещё нет, войны, которые ещё не начались. ") +
                        Loc.T("Но удержать и воплотить можно лишь одно видение. Какое из них станет судьбой Иридии?"),
                        AnomalyType.SpatialRift, Opts(
                            new EventOption(Loc.T("ВИДЕНИЕ ЗВЁЗД"), Loc.F("Разведать все системы в 3 прыжках от ваших границ (сейчас таких {0}).", near.Count), () =>
                            {
                                foreach (var s in near) { s.GeneratePlanets(); s.MarkSurveyedBy(0); }
                                GalaxyView.Instance?.RefreshTerritoryVisuals();
                                NotificationCenter.Show(Loc.T("Видение звёзд"), Loc.F("Оракул открыла {0} сист.", near.Count), NotificationCenter.Kind.Success, 6f);
                            }, Loc.T("Нет неизученных систем рядом"), () => near.Count > 0),
                            new EventOption(Loc.T("ВИДЕНИЕ ИЗОБИЛИЯ"), Loc.F("+{0} гелия-3 и +{1} титана в месяц на 24 месяца.", R(5 * k), R(5 * k)),
                                () => EmpireEffects.Add("vision_iridia", Loc.T("Видение изобилия"), Loc.T("Видения Оракула"), 24, energy: 5 * k, minerals: 5 * k)),
                            new EventOption(Loc.T("ВИДЕНИЕ ГРЯДУЩЕГО"), Loc.F("+{0} науки сразу; 35% — мгновенный прорыв случайной технологии.", R(220 * k)), () =>
                            {
                                Science(220 * k);
                                if (Random.value < 0.35f) TechnologyManager.Instance?.CompleteRandomTech();
                            }),
                            new EventOption(Loc.T("ВИДЕНИЕ ВОЙНЫ"), Loc.F("Узнать намерения каждого соседа и +{0} сплавов в месяц на 24 месяца на подготовку.", R(4 * k)), () =>
                            {
                                foreach (var ai in AIEmpireManager.Alive)
                                {
                                    bool hostile = ai.AtWar || ai.Opinion <= ai.Profile.WarOpinionThreshold + 15f;
                                    NotificationCenter.Show(Loc.F("Видение: {0}", ai.AIName),
                                        ai.AtWar ? Loc.T("уже воюет с вами и не отступит легко")
                                        : hostile ? Loc.F("готовится к войне против вас (отношение {0:+0;-0;0})", ai.Opinion)
                                        : Loc.F("намерения мирные (отношение {0:+0;-0;0})", ai.Opinion),
                                        hostile ? NotificationCenter.Kind.Warning : NotificationCenter.Kind.Info, 9f);
                                }
                                EmpireEffects.Add("vision_iridia", Loc.T("Видение войны"), Loc.T("Видения Оракула"), 24, alloys: 4 * k);
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
                    return Ev("decision_terraan", Loc.T("СОВЕТ КОРНЕЙ"),
                        Loc.T("Раз в два года Конклав сплетает корни всех миров в единый Совет. Сквозь мицелий звучат миллионы голосов, ") +
                        Loc.T("и Хранительница Исинна Кральтэр выслушивает каждый — это занимает недели. Совет может направить соки сада лишь в одну сторону. ") +
                        Loc.T("Куда потечёт сила Конклава в этот раз?"),
                        AnomalyType.AlienFauna, Opts(
                            new EventOption(Loc.T("СПОРЫ В ПУСТОТУ"), Loc.F("Споры мицелия разносят разведку на 4 прыжка от границ: изучено систем — {0}.", near.Count), () =>
                            {
                                foreach (var s in near) { s.GeneratePlanets(); s.MarkSurveyedBy(0); s.IsSurveyed = true; Vision.MarkExplored(0, s.Id); }
                                GalaxyView.Instance?.RefreshTerritoryVisuals();
                                NotificationCenter.Show(Loc.T("Споры в пустоту"), Loc.F("Мицелий изучил {0} сист.", near.Count), NotificationCenter.Kind.Success, 6f);
                            }, Loc.T("Нет неизученных систем рядом"), () => near.Count > 0),
                            new EventOption(Loc.T("ПРОБУЖДЕНИЕ РОЩ"), Loc.F("Все ваши корабли мгновенно заживают ({0} повреждено), щиты полны; +{1} сплавов в месяц на 24 месяца.", damaged, R(3 * k)), () =>
                            {
                                if (fm != null)
                                    foreach (var f in fm.AllFleets)
                                    {
                                        var d = f?.Data;
                                        if (d == null || d.OwnerId != 0 || d.Destroyed) continue;
                                        d.HullPoints = d.MaxHullPoints; d.ArmorPoints = d.MaxArmorPoints; d.ShieldPoints = d.MaxShieldPoints;
                                    }
                                EmpireEffects.Add("council_terraan", Loc.T("Пробуждение рощ"), Loc.T("Совет Корней"), 24, alloys: 3 * k);
                            }),
                            new EventOption(Loc.T("ДОЛГИЙ СЕЗОН"), Loc.F("Сады плодоносят: +{0} титана и +{1} влияния в месяц на 24 месяца.", R(6 * k), R(2 * k)),
                                () => EmpireEffects.Add("council_terraan", Loc.T("Долгий сезон"), Loc.T("Совет Корней"), 24, minerals: 6 * k, influence: 2 * k)),
                            new EventOption(Loc.T("ПАМЯТЬ ДРЕВНИХ"), Loc.F("Старейшие деревья делятся знанием: +{0} науки и +{1} науки в месяц на 24 месяца.", R(180 * k), R(3 * k)), () =>
                            {
                                Science(180 * k);
                                EmpireEffects.Add("council_terraan", Loc.T("Память древних"), Loc.T("Совет Корней"), 24, science: 3 * k);
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
                    string where = c?.System != null ? Loc.F("системе {0}", c.System.Name) : Loc.T("ничейной системе у границ");
                    return Ev("pirate_haven", Loc.T("ПИРАТСКАЯ ГАВАНЬ"),
                        Loc.F("В {0} обосновались пираты: отставные военные, беглые каторжники и просто те, кому тесно в границах империй. ", where) +
                        Loc.T("Их первая банда уже вышла на промысел, и налёты будут повторяться: пираты грабят слабо защищённые колонии ") +
                        Loc.T("у самых границ. Совет ждёт решения."),
                        AnomalyType.DistressSignal, Opts(
                            new EventOption(Loc.T("ПЛАТИТЬ ДАНЬ"), Loc.F("−{0} гелия-3: 3 года пираты не трогают ваши системы.", R(tribute)), () =>
                            {
                                Pay(energy: tribute);
                                ThreatManager.Instance?.SetTribute(36);
                            }, Need(energy: tribute), () => CanPay(energy: tribute)),
                            new EventOption(Loc.T("ОБЪЯВИТЬ НАГРАДУ"), Loc.T("−30 влияния: 3 года за каждый уничтоженный вами пиратский корабль +40 гелия-3 и +4 влияния."), () =>
                            {
                                Pay(influence: 30f);
                                ThreatManager.Instance?.SetBounty(36);
                            }, Need(influence: 30f), () => CanPay(influence: 30f)),
                            new EventOption(Loc.T("ВЫДАТЬ КАПЕРСКИЕ ГРАМОТЫ"), Loc.F("−{0} сплавов: три следующих налёта обрушатся на соперников (чаще на тех, с кем вы воюете). 30% — сделка вскроется: −10 к отношению всех империй.", R(letters)), () =>
                            {
                                Pay(alloys: letters);
                                ThreatManager.Instance?.SetPrivateers(3);
                                if (Random.value < 0.3f)
                                {
                                    OpinionAll(-10f);
                                    NotificationCenter.Show(Loc.T("Сделка с пиратами вскрылась"), Loc.T("Соседи узнали о каперских грамотах"), NotificationCenter.Kind.Warning, 7f);
                                }
                            }, Need(alloys: letters), () => CanPay(alloys: letters)),
                            new EventOption(Loc.T("ПУСТЬ ПОПРОБУЮТ"), Loc.T("Ничего не делать. Держите военный флот у пограничных колоний — пираты избегают защищённых систем."), () => { })));
                }
            });

            Add(new Template
            {
                Id = "leviathan_slain", When = Trigger.Chain,
                Build = c =>
                {
                    float k = K();
                    bool guardCore = HasFlag("precursor_guard");
                    string where = c?.System != null ? c.System.Name : Loc.T("далёкой системе");
                    string text = Loc.F("Чудовище, веками стерёгшее систему {0}, мертво. Его туша медленно дрейфует среди обломков, ", where) +
                                  Loc.T("а система наконец открыта для освоения. Учёные и инженеры спорят, что делать с останками.");
                    if (guardCore)
                        text += Loc.T("\n\nВнутри стража, в оболочке из живого кристалла, бьётся ядро Предтеч — то, ради чего хранилище и охранялось.");
                    void AfterChoice()
                    {
                        if (!guardCore) return;
                        ClearFlag("precursor_guard");
                        Schedule("precursor_core", 15, c);
                    }
                    return Ev("leviathan_slain", Loc.T("ЛЕВИАФАН ПОВЕРЖЕН"), text, AnomalyType.AlienFauna, Opts(
                        new EventOption(Loc.T("РАЗДЕЛАТЬ НА СПЛАВЫ"), Loc.F("+{0} сплавов.", R(300 * k)), () => { Give(alloys: 300 * k); AfterChoice(); }),
                        new EventOption(Loc.T("ИЗУЧИТЬ ОРГАНЫ ЧУДОВИЩА"), Loc.F("+{0} науки и +3 науки в месяц навсегда.", R(350 * k)), () =>
                        {
                            Science(350 * k);
                            TechnologyManager.Instance?.AddMonthlyScience(3f);
                            AfterChoice();
                        }),
                        new EventOption(Loc.T("ВЫСТАВИТЬ ТРОФЕЙ"), Loc.T("+80 влияния и +10 к отношению всех империй: о подвиге говорит вся галактика."), () =>
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
                    return Ev("precursor_signal", Loc.T("МАЯК ПРЕДТЕЧ"),
                        Loc.T("На орбите безжизненной планеты научное судно нашло маяк — объект из сплава, которого нет в наших справочниках. ") +
                        Loc.T("Он передаёт один и тот же фрагмент сигнала, а лингвистический модуль уверен: это часть координат. ") +
                        Loc.T("Кто бы ни оставил маяк, он хотел, чтобы его нашли."),
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption(Loc.T("НАЧАТЬ РАСШИФРОВКУ"), Loc.T("Цепочка «Наследие Предтеч»: через 3 месяца учёные доложат о результатах."), () =>
                            {
                                Xp(c, 30f);
                                Schedule("precursor_decoding", 90, c);
                            }),
                            new EventOption(Loc.T("ПРОДАТЬ МАЯК СОСЕДЯМ"), Loc.F("+{0} гелия-3 и +8 к отношению случайной мирной империи. Наследие уйдёт к ней.", R(150 * k)), () =>
                            {
                                Give(energy: 150 * k);
                                RandomPeacefulRival()?.AddMemory("incident", 8f);
                            }),
                            new EventOption(Loc.T("УНИЧТОЖИТЬ МАЯК"), Loc.T("+40 влияния: суеверные колонисты успокоены."), () => Give(influence: 40f))));
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
                    return Ev("precursor_decoding", Loc.T("РАСШИФРОВКА СИГНАЛА"),
                        Loc.T("Учёные собрали три фрагмента координат из пяти. Остальные, судя по всему, передают другие маяки — ") +
                        Loc.T("их слышат и соседние империи. Восстановить карту можно своими силами, но это долго и дорого; ") +
                        Loc.T("можно поручить работу научному совету или договориться с соседями."),
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption(Loc.T("СВОИМИ СИЛАМИ"), Loc.F("−{0} гелия-3. Карта будет готова через 5 месяцев.", R(cost)), () =>
                            {
                                Pay(energy: cost);
                                Schedule("precursor_map", 150, c);
                            }, Need(energy: cost), () => CanPay(energy: cost)),
                            new EventOption(Loc.T("ПОРУЧИТЬ НАУЧНОМУ СОВЕТУ"), Loc.T("Карта через 2 месяца, учёный совета получает 60 опыта."), () =>
                            {
                                if (council != null) Leaders?.GrantXp(council, 60f);
                                Schedule("precursor_map", 60, c);
                            }, Loc.T("Нужен учёный в научном совете"), () => council != null),
                            new EventOption(Loc.T("ОБМЕНЯТЬСЯ С СОСЕДЯМИ"), partner != null
                                ? Loc.F("Карта через 3 месяца; +10 к отношению империи {0}, но она тоже получит часть наследия.", partner.AIName)
                                : Loc.T("Нет мирных соседей."), () =>
                            {
                                if (partner == null) return;
                                partner.AddMemory("incident", 10f);
                                SetFlag("precursor_shared");
                                Schedule("precursor_map", 90, new EventContext { System = c?.System, Rival = partner.OwnerId });
                            }, Loc.T("Нужна мирная империя"), () => partner != null)));
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
                    string where = target != null ? Loc.F("мёртвую систему {0}", target.Name) : Loc.T("мёртвую систему на окраине");
                    return Ev("precursor_map", Loc.T("КАРТА ПРЕДТЕЧ"),
                        Loc.F("Координаты сложились. Они указывают на {0}: ни одна наша экспедиция там не бывала. ", where) +
                        Loc.T("Если верить карте, там хранилище — то, что Предтечи хотели сохранить. Или то, что они хотели спрятать."),
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption(Loc.T("СНАРЯДИТЬ ЭКСПЕДИЦИЮ"), Loc.F("−{0} сплавов. Через 4 месяца экспедиция доберётся до хранилища.", R(alloys)), () =>
                            {
                                Pay(alloys: alloys);
                                Schedule("precursor_vault", 120, ctx);
                            }, Need(alloys: alloys), () => CanPay(alloys: alloys)),
                            new EventOption(Loc.T("ЭКСПЕДИЦИЯ ПОД ОХРАНОЙ ФЛОТА"), Loc.F("−{0} сплавов и −{1} гелия-3. Безопаснее: если хранилище охраняется, эскорт примет удар.", R(escortAlloys), R(escortEnergy)), () =>
                            {
                                Pay(energy: escortEnergy, alloys: escortAlloys);
                                SetFlag("precursor_escort");
                                Schedule("precursor_vault", 120, ctx);
                            }, Need(energy: escortEnergy, alloys: escortAlloys), () => CanPay(energy: escortEnergy, alloys: escortAlloys)),
                            new EventOption(Loc.T("ОТЛОЖИТЬ НА ГОД"), Loc.T("Вернуться к карте через год."), () => Schedule("precursor_map", 360, c))));
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
                    return Ev("precursor_vault", Loc.T("ХРАНИЛИЩЕ ПРЕДТЕЧ"),
                        Loc.T("Экспедиция нашла хранилище. Его двери не открывались десятки тысяч лет, а в глубине что-то медленно просыпается: ") +
                        Loc.T("датчики фиксируют растущий поток энергии и очертания огромного тела в стазисе. Решать нужно сейчас."),
                        AnomalyType.PrecursorRelic, Opts(
                            new EventOption(Loc.T("ВЗЛОМАТЬ ДВЕРИ"), Loc.F("{0}% — ядро Предтеч ваше. Иначе пробудится страж-левиафан у ваших границ, и ядро достанется тому, кто его убьёт.", R(chance * 100)), () =>
                            {
                                if (Random.value < chance) { Schedule("precursor_core", 20, c); return; }
                                SetFlag("precursor_guard");
                                int sys = ThreatManager.Instance != null ? ThreatManager.Instance.AwakenLeviathan(0, Loc.T("Страж Предтеч"), 1.15f) : -1;
                                var s = EmpireStats.GetSystem(sys);
                                NotificationCenter.Show(Loc.T("СТРАЖ ПРОБУДИЛСЯ"),
                                    s != null ? Loc.F("Страж Предтеч вырвался из хранилища и занял систему {0}. Ядро — внутри него", s.Name) : Loc.T("Страж Предтеч вырвался из хранилища"),
                                    NotificationCenter.Kind.Danger, 10f);
                            }),
                            new EventOption(Loc.T("ИЗУЧАТЬ ОСТОРОЖНО"), Loc.T("Без риска, но долго: ядро будет извлечено через 8 месяцев."), () => Schedule("precursor_core", 240, c)),
                            new EventOption(Loc.T("ЗАПЕЧАТАТЬ НАВСЕГДА"), Loc.T("+60 влияния и +10 к отношению Иридийского Оракула: некоторые двери должны оставаться закрытыми."), () =>
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
                    string text = Loc.T("Перед нами ядро Предтеч — кристалл размером с дом, внутри которого течёт свет. Его можно встроить ") +
                                  Loc.T("в наши научные сети, переплавить в оружие или отдать галактике. Решение изменит империю навсегда.");
                    if (shared) text += Loc.F("\n\nПо договору копию данных получит {0}.", partner.AIName);
                    void Finish()
                    {
                        SetFlag("precursor_core_done");
                        ClearFlag("precursor_escort");
                        if (shared)
                        {
                            partner.Alloys += 200f;
                            partner.AddMemory("incident", 6f);
                            NotificationCenter.Show(Loc.T("Наследие поделено"), Loc.F("{0} получили данные ядра Предтеч", partner.AIName), NotificationCenter.Kind.Info, 6f);
                        }
                        ClearFlag("precursor_shared");
                    }
                    return Ev("precursor_core", Loc.T("ЯДРО ПРЕДТЕЧ"), text, AnomalyType.PrecursorRelic, Opts(
                        new EventOption(Loc.T("ВСТРОИТЬ В НАУЧНУЮ СЕТЬ"), Loc.T("+12 науки в месяц навсегда."), () =>
                        {
                            TechnologyManager.Instance?.AddMonthlyScience(12f);
                            Finish();
                        }),
                        new EventOption(Loc.T("ПЕРЕПЛАВИТЬ В ОРУЖИЕ"), Loc.F("+{0} сплавов сразу и +6 сплавов в месяц навсегда. Ядро будет звучать и дальше…", R(250 * k)), () =>
                        {
                            Give(alloys: 250 * k);
                            Eco?.AddIncome(0f, 0f, 6f, 0f);
                            Schedule("precursor_echo", 720, c);
                            Finish();
                        }),
                        new EventOption(Loc.T("ОТДАТЬ ГАЛАКТИКЕ"), Loc.T("+100 влияния и +15 к отношению всех империй."), () =>
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
                Build = c => Ev("precursor_echo", Loc.T("ЭХО ПРЕДТЕЧ"),
                    Loc.T("Переплавленное ядро всё ещё звучит. Его эхо разошлось по гиперкоридорам, и в ничейной системе у наших границ ") +
                    Loc.T("что-то откликнулось: датчики видят пробуждение древнего стража. Он идёт на зов. Можно встретить его — ") +
                    Loc.T("или заглушить ядро, отказавшись от части его силы."),
                    AnomalyType.SpatialRift, Opts(
                        new EventOption(Loc.T("К БОЮ"), Loc.T("У ваших границ пробудится левиафан «Эхо Предтеч». Победа над ним принесёт большую награду."), () =>
                        {
                            int sys = ThreatManager.Instance != null ? ThreatManager.Instance.AwakenLeviathan(0, Loc.T("Эхо Предтеч"), 1f) : -1;
                            var s = EmpireStats.GetSystem(sys);
                            if (s != null) NotificationCenter.Show(Loc.T("Эхо Предтеч"), Loc.F("Страж пробудился в системе {0}", s.Name), NotificationCenter.Kind.Danger, 8f);
                        }),
                        new EventOption(Loc.T("ЗАГЛУШИТЬ ЯДРО"), Loc.T("−4 сплава в месяц навсегда (из 6, что даёт ядро), страж не проснётся."),
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
                FindContext = () => LeaderCtx(l => l.Class != LeaderClass.Agent && l.Level >= 3 && !l.Has(EventTraitFor(l.Class))),
                Build = c =>
                {
                    var l = c.Leader;
                    string cls = LeaderManager.ClassName(l.Class).ToLower();
                    return Ev("leader_ambition", Loc.T("АМБИЦИИ"),
                        Loc.F("{0}, {1} {2}-го уровня, просит аудиенции. Годы службы дали опыт, связи и репутацию — ", l.Name, cls, l.Level) +
                        Loc.T("и теперь лидер хочет большего: признания, власти или громкого дела, о котором заговорят по всей галактике. ") +
                        Loc.T("Отказ запомнится."),
                        AnomalyType.LeaderStory, Opts(
                            new EventOption(Loc.T("НАГРАДИТЬ ОРДЕНОМ"), Loc.T("−40 влияния; лидер получает 150 опыта."), () =>
                            {
                                Pay(influence: 40f);
                                Leaders?.GrantXp(l, 150f);
                            }, Need(influence: 40f), () => CanPay(influence: 40f)),
                            new EventOption(Loc.T("ДАТЬ ОСОБОЕ ПОРУЧЕНИЕ"), Loc.F("Через 4 месяца: 65% — успех (черта «{0}» и 200 опыта), 35% — провал, лидер может уйти в отставку.", TraitName(EventTraitFor(l.Class))),
                                () => Schedule("leader_ambition_result", 120, c)),
                            new EventOption(Loc.T("ОТКАЗАТЬ"), Loc.T("30% — лидер уйдёт в отставку."), () =>
                            {
                                if (Random.value < 0.3f)
                                    Leaders?.Retire(l, Loc.T("Отставка"), Loc.F("{0} не простил отказа и покинул службу", l.Name));
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
                        return Ev("leader_ambition_result", Loc.T("ПОРУЧЕНИЕ ВЫПОЛНЕНО"),
                            Loc.F("{0} блестяще справился с особым поручением. Об этом пишут во всех новостных лентах, ", l.Name) +
                            Loc.T("а подчинённые смотрят на лидера с новым уважением."),
                            AnomalyType.LeaderStory, Opts(
                                new EventOption(Loc.T("ВОЗДАТЬ ПОЧЕСТИ"), Loc.F("Черта «{0}» и 200 опыта.", TraitName(trait)), () =>
                                {
                                    Leaders?.GiveTrait(l, trait);
                                    Leaders?.GrantXp(l, 200f);
                                })));
                    return Ev("leader_ambition_result", Loc.T("ПОРУЧЕНИЕ ПРОВАЛЕНО"),
                        Loc.F("Особое поручение обернулось провалом. {0} винит обстоятельства и просит о новом шансе, ", l.Name) +
                        Loc.T("но в правительстве уже шепчутся об отставке."),
                        AnomalyType.LeaderStory, Opts(
                            new EventOption(Loc.T("ПРИНЯТЬ ОТСТАВКУ"), Loc.T("Лидер покидает службу."), () =>
                                Leaders?.Retire(l, Loc.T("Отставка"), Loc.F("{0} ушёл в отставку после провала", l.Name))),
                            new EventOption(Loc.T("ДАТЬ ВТОРОЙ ШАНС"), Loc.T("−30 влияния; лидер остаётся и получает 60 опыта за усвоенный урок."), () =>
                            {
                                Pay(influence: 30f);
                                Leaders?.GrantXp(l, 60f);
                            }, Need(influence: 30f), () => CanPay(influence: 30f))));
                }
            });
        }
    }
}
