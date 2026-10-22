using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Дипломатия ИИ. Отношение к игроку — сумма понятных слагаемых (характер, угроза, граница,
    /// подарки, торговля, пакты, обиды), каждое видно в окне дипломатии с пояснением.
    /// Состояния: мир, пакт о ненападении, оборонительный союз (включает пакт), война, перемирие после войны.
    /// Союзник вступает в войну, если на игрока нападают; если нападают на союзника — игрока зовут на помощь.
    /// ИИ сам объявляет войну (плохое отношение + перевес сил), предлагает мир (усталость или поражение)
    /// и пакт (хорошее отношение и уважение к вашему флоту).
    /// </summary>
    public partial class AIEmpireManager
    {
        public enum OfferKind { None, Peace, Pact, Trade, Demand, Alliance }

        public static event Action OnDiplomacyChanged;
        private static void RaiseDiplomacyChanged() => OnDiplomacyChanged?.Invoke();
        public static void NotifyDiplomacyChanged() => OnDiplomacyChanged?.Invoke();

        // ==================== СОСТОЯНИЕ ====================

        public bool AtWar { get; private set; }
        public bool HasPact { get; private set; }
        /// <summary>Оборонительный союз с игроком. Всегда вместе с пактом о ненападении.</summary>
        public bool HasAlliance { get; private set; }
        public int TruceDays { get; private set; }
        public float WarWeariness { get; private set; }
        public int WarMonths => _warMonths;
        public int SystemsLostInWar => _systemsLostInWar;
        public int SystemsTakenInWar => _systemsTakenInWar;
        public int ShipsLostInWar => _shipsLostInWar;
        public int PlayerShipsLostInWar => _playerShipsLostInWar;

        public float Opinion { get; private set; }
        public readonly List<OpinionModifier> OpinionBreakdown = new List<OpinionModifier>();

        public OfferKind PendingOffer { get; private set; } = OfferKind.None;
        public string PendingOfferReason { get; private set; } = "";
        public int PendingOfferDays => _offerDays;

        /// <summary>Совместимость со старым кодом (торговля, обзор империи).</summary>
        public float RelationsWithPlayer => Opinion;
        public bool IsHostileToPlayer => AtWar;

        private int _warMonths, _systemsLostInWar, _systemsTakenInWar, _shipsLostInWar, _playerShipsLostInWar;
        private bool _capitalLost;
        private int _offerDays, _offerCooldownDays, _warCooldownDays;
        private float _lastOpinionBucket;
        private bool _borderWarned;

        private readonly Dictionary<string, float> _memory = new Dictionary<string, float>();

        public const float OpinionMin = -100f, OpinionMax = 100f;
        public const float PeaceAcceptThreshold = 20f;
        public const float PeaceInfluenceCost = 30f;
        public const float PactInfluenceCost = 25f;
        public const float AllianceInfluenceCost = 35f;
        public const int TruceLengthDays = 3 * 360;
        public const float GiftEnergyAmount = 100f, GiftAlloysAmount = 50f;

        // ---- Память (затухающие события) ----

        private struct MemoryInfo
        {
            public string Label, Detail;
            public LGIcon Icon;
            public float MonthlyDecay;   // доля, исчезающая за месяц
            public float Min, Max;
        }

        private static readonly Dictionary<string, MemoryInfo> MemoryTable = new Dictionary<string, MemoryInfo>
        {
            ["gifts"]      = new MemoryInfo { Label = Loc.T("Подарки"), Detail = Loc.T("Щедрость помнят, но со временем забывают"), Icon = LGIcon.Gift, MonthlyDecay = 0.08f, Min = 0f, Max = 40f },
            ["trade"]      = new MemoryInfo { Label = Loc.T("Торговые сделки"), Detail = Loc.T("Выгодная торговля сближает, невыгодная — раздражает"), Icon = LGIcon.Trade, MonthlyDecay = 0.08f, Min = -20f, Max = 30f },
            ["border"]     = new MemoryInfo { Label = Loc.T("Нарушение границ"), Detail = Loc.T("Ваши военные корабли в их системах в мирное время"), Icon = LGIcon.Border, MonthlyDecay = 0.25f, Min = -40f, Max = 0f },
            ["grudge"]     = new MemoryInfo { Label = Loc.T("Обида за войну"), Detail = Loc.T("Недавняя война ещё не забыта"), Icon = LGIcon.Swords, MonthlyDecay = 0.04f, Min = -40f, Max = 0f },
            ["betrayal"]   = new MemoryInfo { Label = Loc.T("Вероломство"), Detail = Loc.T("Вы объявили войну, нарушив пакт о ненападении"), Icon = LGIcon.Betrayal, MonthlyDecay = 0.03f, Min = -60f, Max = 0f },
            ["aggression"] = new MemoryInfo { Label = Loc.T("Агрессия"), Detail = Loc.T("Вы первыми объявили им войну"), Icon = LGIcon.Warning, MonthlyDecay = 0.05f, Min = -30f, Max = 0f },
            ["pactcancel"] = new MemoryInfo { Label = Loc.T("Расторгнутый пакт"), Detail = Loc.T("Вы в одностороннем порядке вышли из пакта"), Icon = LGIcon.Handshake, MonthlyDecay = 0.1f, Min = -20f, Max = 0f },
            ["rejected"]   = new MemoryInfo { Label = Loc.T("Отвергнутое предложение"), Detail = Loc.T("Вы отклонили их дипломатическое предложение"), Icon = LGIcon.Close, MonthlyDecay = 0.2f, Min = -15f, Max = 0f },
            ["coalitionleft"] = new MemoryInfo { Label = Loc.T("Выход из коалиции"), Detail = Loc.T("Вы бросили общее дело"), Icon = LGIcon.Close, MonthlyDecay = 0.1f, Min = -30f, Max = 0f },
            ["separate"]   = new MemoryInfo { Label = Loc.T("Сепаратный мир"), Detail = Loc.T("Вы вышли из войны, оставив союзников одних"), Icon = LGIcon.Peace, MonthlyDecay = 0.06f, Min = -40f, Max = 0f },
            ["trust"]      = new MemoryInfo { Label = Loc.T("Годы добрососедства"), Detail = Loc.T("Долгий пакт и спокойные границы сближают, но одно предательство обнуляет доверие"), Icon = LGIcon.Handshake, MonthlyDecay = 0.01f, Min = 0f, Max = 20f },
            ["espionage"]  = new MemoryInfo { Label = Loc.T("Шпионаж"), Detail = Loc.T("Ваши агенты пойманы или замечены за тайными операциями на их территории"), Icon = LGIcon.Betrayal, MonthlyDecay = 0.08f, Min = -40f, Max = 0f },
            ["incident"]   = new MemoryInfo { Label = Loc.T("Инциденты"), Detail = Loc.T("Как вы повели себя в пограничных и дипломатических происшествиях"), Icon = LGIcon.Info, MonthlyDecay = 0.06f, Min = -35f, Max = 30f },
            ["allyhelp"]   = new MemoryInfo { Label = Loc.T("Помощь в войне"), Detail = Loc.T("Вы пришли им на помощь, когда на них напали"), Icon = LGIcon.Handshake, MonthlyDecay = 0.03f, Min = 0f, Max = 30f },
            ["allyabandon"] = new MemoryInfo { Label = Loc.T("Брошенный союзник"), Detail = Loc.T("Вы отказались помочь, когда на них напали"), Icon = LGIcon.Betrayal, MonthlyDecay = 0.03f, Min = -50f, Max = 0f },
            ["alliancecancel"] = new MemoryInfo { Label = Loc.T("Расторгнутый союз"), Detail = Loc.T("Вы в одностороннем порядке вышли из союза"), Icon = LGIcon.Handshake, MonthlyDecay = 0.08f, Min = -25f, Max = 0f },
        };

        public void AddMemory(string key, float delta)
        {
            if (!MemoryTable.TryGetValue(key, out var info)) return;
            _memory.TryGetValue(key, out float v);
            _memory[key] = Mathf.Clamp(v + delta, info.Min, info.Max);
            RecomputeOpinion();
            RaiseDiplomacyChanged();
        }

        private float AggressionFactor => Mathf.Max(0.3f, GameSession.Settings.AIAggression);

        private void InitDiplomacy()
        {
            AtWar = false; HasPact = false; HasAlliance = false; TruceDays = 0; WarWeariness = 0f;
            _memory.Clear();
            PendingOffer = OfferKind.None;
            // Первые годы — время на обустройство: ИИ не объявляет войну
            _warCooldownDays = Mathf.RoundToInt(360f * AIDifficulty.PeaceYears);
            RecomputeOpinion();
            _lastOpinionBucket = Opinion;
        }

        // ==================== ОТНОШЕНИЕ ====================

        private void RecomputeOpinion()
        {
            OpinionBreakdown.Clear();
            void Add(string label, string detail, float value, LGIcon icon)
            {
                if (Mathf.Abs(value) < 0.5f) return;
                OpinionBreakdown.Add(new OpinionModifier(label, detail, value, icon));
            }

            Add(Loc.F("Характер: {0} империя", Profile.Name.ToLower()), Profile.Summary, Profile.BaseOpinion, LGIcon.Leader);

            float bias = AIDifficulty.OpinionBias;
            if (bias > 0f) Add(Loc.F("Сложность: {0}", NewGameSettings.LevelName(AIDifficulty.Tier).ToLower()), Loc.T("Соперник изначально настроен миролюбиво"), bias, LGIcon.Info);
            else if (bias < 0f) Add(Loc.F("Сложность: {0}", NewGameSettings.LevelName(AIDifficulty.Tier).ToLower()), Loc.T("Соперник изначально подозрителен"), bias, LGIcon.Info);

            // Машины и органики друг другу не доверяют
            bool meMachine = Personality == AIPersonality.Machine;
            if (meMachine != FactionTraits.IsMachine(0))
                Add(meMachine ? Loc.T("Органическая империя") : Loc.T("Машинный разум"),
                    meMachine ? Loc.T("Консенсус считает органиков непредсказуемыми") : Loc.T("Органики не доверяют бездушным машинам"),
                    -FactionTraits.MachineDistrust, LGIcon.Leader);

            float myPow = Mathf.Max(1f, EmpireStats.MilitaryPower(OwnerId));
            float playerPow = EmpireStats.MilitaryPower(0);
            float ratio = playerPow / myPow;
            if (ratio > 1.15f)
            {
                float v = -Mathf.Clamp((ratio - 1f) * 25f * Profile.ThreatSensitivity * AggressionFactor, 0f, 35f);
                Add(Loc.F("Угроза: ваш флот сильнее (×{0:0.0})", ratio), Loc.T("Чем сильнее ваш флот по сравнению с их, тем тревожнее соседу"), v, LGIcon.Fleet);
            }
            else if (Personality == AIPersonality.Militarist && ratio < 0.67f && playerPow >= 0f)
            {
                Add(Loc.T("Вы выглядите слабыми"), Loc.T("Воинственная империя видит в слабом соседе добычу"), -12f * AggressionFactor, LGIcon.Target);
            }

            int border = EmpireStats.SharedBorderCount(OwnerId, 0);
            if (border > 0)
                Add(Loc.F("Общая граница ({0} сист.)", border), Loc.T("Соседство порождает споры за территорию"), -Mathf.Min(18f, border * 3f), LGIcon.Border);

            int mySys = EmpireStats.SystemCount(OwnerId), playerSys = EmpireStats.SystemCount(0);
            if (playerSys >= 5 && playerSys > mySys * 1.25f)
                Add(Loc.T("Вы расширяетесь быстрее"), Loc.F("У вас {0} систем против их {1}", playerSys, mySys), Personality == AIPersonality.Militarist ? -8f : -5f, LGIcon.Starbase);

            int myTech = ResearchedCount, playerTech = EmpireStats.TechCount(0);
            if (Personality == AIPersonality.Scientific && playerTech > myTech + 3)
                Add(Loc.T("Научное соперничество"), Loc.F("Вы опережаете их в науке ({0} против {1})", playerTech, myTech), -8f, LGIcon.Research);
            if (Personality == AIPersonality.Trader && !AtWar && border > 0 && _memory.TryGetValue("trade", out float tr) && tr > 5f)
                Add(Loc.T("Выгодный сосед"), Loc.T("Торговая империя ценит партнёров у самой границы"), 6f, LGIcon.Trade);

            if (HasPact) Add(Loc.T("Пакт о ненападении"), Loc.T("Действующий договор укрепляет доверие"), 15f, LGIcon.Handshake);
            if (HasAlliance) Add(Loc.T("Оборонительный союз"), Loc.T("Вы обязались защищать друг друга"), 15f, LGIcon.Defense);
            // Репутация игрока видна всем, кто с ним знаком
            if (Contacts.PlayerMet(OwnerId))
                Add(Loc.F("Ваша репутация: {0}", Reputation.Current.ToLower()), Loc.T("Как вы держите слово: войны без повода, нарушенные пакты и брошенные союзники видны всей галактике"),
                    Reputation.OpinionEffect, Reputation.Value >= 0f ? LGIcon.Handshake : LGIcon.Betrayal);
            foreach (var ally in Alive)
            {
                if (ally == this || !ally.HasAlliance) continue;
                if (AIRelations.AtWar(OwnerId, ally.OwnerId))
                    Add(Loc.F("Вы в союзе с их врагом: {0}", ally.AIName), Loc.T("Союзник их противника — их противник"), -15f, LGIcon.Swords);
            }
            foreach (var other in Alive)
            {
                if (other == this || !other.AtWar || !AIRelations.AtWar(OwnerId, other.OwnerId)) continue;
                Add(Loc.F("Общий враг: {0}", other.AIName), Loc.T("Вы оба воюете с этой империей"), 12f, LGIcon.Swords);
            }
            if (AICoalition.IsMember(OwnerId) && AICoalition.TargetOwner == 0)
                Add(Loc.T("Коалиция против вас"), Loc.T("Соперники объединились против вас"), -15f, LGIcon.Swords);
            if (AICoalition.AreAllies(0, OwnerId))
                Add(Loc.T("Союзник по коалиции"), Loc.T("Вы вместе выступаете против общего врага"), 15f, LGIcon.Handshake);
            if (AICoalition.IsTarget(OwnerId) && AICoalition.PlayerMember)
                Add(Loc.T("Вы в коалиции против них"), Loc.T("Вы объединились с их врагами"), -20f, LGIcon.Swords);
            if (TruceDays > 0 && !AtWar) Add(Loc.F("Перемирие ({0} мес.)", Mathf.CeilToInt(TruceDays / 30f)), Loc.T("Мирный договор ещё в силе"), 5f, LGIcon.Peace);
            if (AtWar) Add(Loc.T("Идёт война"), Loc.T("Между вашими империями открытый конфликт"), -40f, LGIcon.Swords);

            foreach (var kv in _memory)
            {
                if (!MemoryTable.TryGetValue(kv.Key, out var info)) continue;
                Add(info.Label, info.Detail, kv.Value, info.Icon);
            }

            float sum = 0f;
            foreach (var m in OpinionBreakdown) sum += m.Value;
            Opinion = Mathf.Clamp(sum, OpinionMin, OpinionMax);
            OpinionBreakdown.Sort((a, b) => Mathf.Abs(b.Value).CompareTo(Mathf.Abs(a.Value)));
        }

        public static string OpinionLabel(float v) =>
              v <= -60f ? Loc.T("Враждебность")
            : v <= -20f ? Loc.T("Неприязнь")
            : v < 10f   ? Loc.T("Настороженность")
            : v < 35f   ? Loc.T("Расположение")
            : Loc.T("Дружба");

        private static int Bucket(float v) => v <= -60f ? 0 : v <= -20f ? 1 : v < 10f ? 2 : v < 35f ? 3 : 4;

        // ==================== ЕЖЕДНЕВНО / ЕЖЕМЕСЯЧНО ====================

        private void DailyDiplomacy()
        {
            if (TruceDays > 0) TruceDays--;
            if (_offerCooldownDays > 0) _offerCooldownDays--;
            if (_warCooldownDays > 0) _warCooldownDays--;
            if (PendingOffer != OfferKind.None && --_offerDays <= 0)
            {
                PendingOffer = OfferKind.None;
                PendingDeal = null;
                _offerCooldownDays = 120;
            }

            // Военные корабли игрока в их системах в мирное время
            if (!AtWar && FleetManager.Instance != null)
            {
                int intruders = 0;
                foreach (var f in FleetManager.Instance.AllFleets)
                {
                    var d = f?.Data;
                    if (d == null || d.Destroyed || d.OwnerId != 0 || d.Type != FleetType.Military) continue;
                    if (d.State == FleetState.InHyperlane) continue;
                    var s = EmpireStats.GetSystem(d.CurrentSystemId);
                    if (s != null && s.OwnerId == OwnerId) intruders++;
                }
                if (intruders > 0)
                {
                    float rate = HasPact ? 0.15f : 0.4f;
                    _memory.TryGetValue("border", out float b);
                    _memory["border"] = Mathf.Max(MemoryTable["border"].Min, b - rate * intruders);
                    if (!_borderWarned && _memory["border"] < -5f)
                    {
                        _borderWarned = true;
                        NotificationCenter.Show(Loc.T("Нарушение границ"), Loc.F("{0} требует увести ваш флот из их систем", AIName), NotificationCenter.Kind.Warning, 6f);
                    }
                }
                else _borderWarned = false;
            }

            RecomputeOpinion();
            NotifyOpinionShift();
        }

        private void NotifyOpinionShift()
        {
            int now = Bucket(Opinion), was = Bucket(_lastOpinionBucket);
            if (now == was) return;
            _lastOpinionBucket = Opinion;
            // До первого контакта игрок об этой империи ничего не знает — сдвиг отношения запоминаем молча
            if (!Contacts.PlayerMet(OwnerId)) return;
            if (now < was)
                NotificationCenter.Show(Loc.T("Отношения ухудшились"), $"{AIName}: {OpinionLabel(Opinion).ToLower()} ({Opinion:+0;-0;0})",
                    NotificationCenter.Kind.Warning, 5f);
            else
                NotificationCenter.Show(Loc.T("Отношения улучшились"), $"{AIName}: {OpinionLabel(Opinion).ToLower()} ({Opinion:+0;-0;0})",
                    NotificationCenter.Kind.Success, 4f);
            RaiseDiplomacyChanged();
        }

        private void MonthlyDiplomacy()
        {
            // Затухание памяти
            var keys = new List<string>(_memory.Keys);
            foreach (var k in keys)
            {
                float decay = MemoryTable[k].MonthlyDecay;
                // Мистики помнят обиды вдвое дольше
                if (Personality == AIPersonality.Mystic && _memory[k] < 0f) decay *= 0.5f;
                // Конклав мыслит веками: и добро, и зло помнит дольше всех
                if (Personality == AIPersonality.Gardener) decay *= 0.6f;
                float v = _memory[k] * (1f - decay);
                if (Mathf.Abs(v) < 0.5f) _memory.Remove(k);
                else _memory[k] = v;
            }
            // Незнакомы с игроком — никаких переговоров, требований и войн (как до первого контакта в Civilization)
            if (!Contacts.PlayerMet(OwnerId))
            {
                if (PendingOffer != OfferKind.None) { PendingOffer = OfferKind.None; PendingDeal = null; }
                return;
            }

            // Годы мира под пактом копят доверие
            if (HasPact && !AtWar && Smart >= 1) AddMemory("trust", 0.7f);

            if (AtWar)
            {
                _warMonths++;
                WarWeariness = Mathf.Min(100f, WarWeariness + 2.5f * Profile.WearinessRate);
            }
            else WarWeariness = Mathf.Max(0f, WarWeariness - 5f);

            RecomputeOpinion();
            MonthlyAgreements();

            // Союз держится, пока союзник к вам расположен
            if (HasAlliance && !AtWar && Opinion < 0f)
            {
                HasAlliance = false;
                NotificationCenter.Show(Loc.T("Союз распался"), Loc.F("{0} больше не считает вас союзником: отношения испортились", AIName), NotificationCenter.Kind.Warning, 8f);
            }

            if (AtWar) ConsiderPeaceOffer();
            else
            {
                // Войну теперь планирует стратегия (AIEmpireManager.Strategy) — с подготовкой, а не броском кубика
                ConsiderPactOffer();
                if (!AtWar) ConsiderAllianceOffer();
                if (!AtWar) ConsiderTradeOffer();
            }
            RaiseDiplomacyChanged();
        }

        // ==================== РЕШЕНИЯ ИИ ====================

        private float PowerRatio => EmpireStats.MilitaryPower(OwnerId) / Mathf.Max(1f, EmpireStats.MilitaryPower(0));

        /// <summary>Объявить войну игроку по готовому плану (стратегия проверила условия и собрала флот).</summary>
        private void DeclareWarOnPlayer(float ratio)
        {
            if (AtWar || TruceDays > 0 || _bankrupt || AICoalition.AreAllies(0, OwnerId) || HasAlliance) return;
            var reasons = new List<string>();
            foreach (var m in OpinionBreakdown)
                if (m.Value < 0f && reasons.Count < 2) reasons.Add(m.Label.ToLower());
            reasons.Add(Loc.F("перевес сил ×{0:0.0}", ratio));

            bool broke = HasPact;
            HasPact = false;
            StartWar();
            NotificationCenter.Show(broke ? Loc.T("ВЕРОЛОМНОЕ НАПАДЕНИЕ") : Loc.T("ВОЙНА ОБЪЯВЛЕНА"),
                Loc.F("{0} объявляет вам войну. Причины: {1}", AIName, string.Join(", ", reasons)),
                NotificationCenter.Kind.Danger, 10f);
            OnAttackedPlayer(broke);
        }

        /// <summary>ИИ напал на игрока: у игрока появляется повод для ответа, союзники вступают в войну.</summary>
        private void OnAttackedPlayer(bool brokePact)
        {
            CasusBelli.Grant(OwnerId, CasusBelliKind.Retribution, brokePact ? CasusBelli.BetrayalMonths : CasusBelli.RetributionMonths);
            CallPlayerAllies(this);
        }

        /// <summary>На игрока напали — союзники выполняют обязательство и объявляют войну агрессору.</summary>
        public static void CallPlayerAllies(AIEmpireManager attacker)
        {
            if (attacker == null) return;
            foreach (var ally in new List<AIEmpireManager>(Alive))
            {
                if (ally == attacker || !ally.HasAlliance) continue;
                if (AIRelations.AtWar(ally.OwnerId, attacker.OwnerId)) continue;
                if (!AIRelations.JoinWar(ally, attacker))
                    NotificationCenter.Show(Loc.T("Союзник не смог помочь"), Loc.F("{0} не может вступить в войну: перемирие или пустая казна", ally.AIName),
                        NotificationCenter.Kind.Warning, 7f);
                else
                {
                    if (!Contacts.Met(ally.OwnerId, attacker.OwnerId)) Contacts.Meet(ally.OwnerId, attacker.OwnerId);
                    NotificationCenter.Show(Loc.T("Союзник вступил в войну"), Loc.F("{0} выполняет союзный долг и объявляет войну: {1}", ally.AIName, Contacts.NameForPlayer(attacker.OwnerId)),
                        NotificationCenter.Kind.Success, 8f);
                }
            }
        }

        /// <summary>Вступление в войну по обязательству коалиции против игрока (после предупреждения в несколько месяцев).</summary>
        public bool CoalitionDeclareWar()
        {
            if (AtWar || _bankrupt || IsEliminated || TruceDays > 360 || _warCooldownDays > 0) return false;
            TruceDays = 0;
            StartWar();
            NotificationCenter.Show(Loc.T("КОАЛИЦИЯ ОБЪЯВИЛА ВОЙНУ"),
                Loc.F("{0} вступает в войну против вас вместе с остальными соперниками", AIName),
                NotificationCenter.Kind.Danger, 10f);
            OnAttackedPlayer(false);
            return true;
        }

        private void ConsiderPeaceOffer()
        {
            if (PendingOffer != OfferKind.None || _offerCooldownDays > 0) return;
            bool tired = WarWeariness >= Profile.PeaceWeariness;
            bool losing = _warMonths >= 3 && PowerRatio < 0.6f;
            // Живой игрок фиксирует выигрыш: взяли несколько систем, война затягивается — пора закрепить успех миром
            bool satisfied = Smart >= 1 && _systemsTakenInWar >= 2 && _systemsTakenInWar > _systemsLostInWar
                             && WarWeariness >= Profile.PeaceWeariness * 0.5f;
            if (!tired && !losing && !_capitalLost && !satisfied) return;

            string reason = _capitalLost ? Loc.T("их столица пала")
                          : losing ? Loc.T("их флот разбит и уступает вашему")
                          : satisfied && !tired ? Loc.F("они захватили {0} сист. и хотят закрепить успех", _systemsTakenInWar)
                          : Loc.F("страна устала от войны ({0:0}%)", WarWeariness);
            Offer(OfferKind.Peace, reason);
            NotificationCenter.Show(Loc.T("Предложение мира"), Loc.F("{0} предлагает мир: {1}. Ответьте в окне дипломатии", AIName, reason),
                NotificationCenter.Kind.Info, 9f);
        }

        private void ConsiderPactOffer()
        {
            if (HasPact || PendingOffer != OfferKind.None || _offerCooldownDays > 0) return;
            if (WarPlanTarget == 0) return;
            // Готовим войну с другой империей — пакт с игроком прикроет тыл, на него согласны охотнее
            if (Opinion < Profile.PactOpinion + (WarPlanTarget > 0 ? 0f : 15f)) return;
            if (PowerRatio > 1.2f && Personality == AIPersonality.Militarist) return;
            if (UnityEngine.Random.value > 0.25f) return;
            Offer(OfferKind.Pact, Loc.T("они ценят добрососедство и хотят гарантий безопасности"));
            NotificationCenter.Show(Loc.T("Предложение пакта"), Loc.F("{0} предлагает пакт о ненападении. Ответьте в окне дипломатии", AIName),
                NotificationCenter.Kind.Info, 8f);
        }

        /// <summary>Сосед с пактом, хорошим отношением и общим врагом (или сильным соседом) зовёт в союз.</summary>
        private void ConsiderAllianceOffer()
        {
            if (!HasPact || HasAlliance || PendingOffer != OfferKind.None || _offerCooldownDays > 0) return;
            if (Smart < 1 || UnityEngine.Random.value > 0.2f) return;
            var e = EvaluateAlliance();
            if (e.Blocker != null || e.Score < 10f) return;
            bool commonEnemy = false;
            foreach (var other in Alive)
                if (other != this && AIRelations.AtWar(OwnerId, other.OwnerId) && Diplomacy.AtWar(0, other.OwnerId)) { commonEnemy = true; break; }
            Offer(OfferKind.Alliance, commonEnemy ? Loc.T("у вас общий враг — вместе легче выстоять") : Loc.T("они доверяют вам и хотят гарантий защиты"));
            NotificationCenter.Show(Loc.T("Предложение союза"), Loc.F("{0} предлагает оборонительный союз. Ответьте в окне дипломатии", AIName),
                NotificationCenter.Kind.Info, 9f);
        }

        private void Offer(OfferKind kind, string reason)
        {
            // Мир, пакт и союз тоже ложатся на стол переговоров как договор
            var d = new Deal();
            d.Treaties.Add(new DealItem(kind == OfferKind.Peace ? DealItemKind.Peace : kind == OfferKind.Alliance ? DealItemKind.Alliance : DealItemKind.Pact, 0f));
            SetPendingDeal(d, kind, reason);
        }

        private void StartWar()
        {
            AtWar = true;
            HasAlliance = false;
            PendingOffer = OfferKind.None;
            PendingDeal = null;
            BreakAgreements();
            _warMonths = 0;
            _systemsLostInWar = _systemsTakenInWar = _shipsLostInWar = _playerShipsLostInWar = 0;
            _capitalLost = false;
            Mode = ArmyMode.Gather;
            RecomputeOpinion();
            RaiseDiplomacyChanged();
        }

        private void MakePeace(bool silent = false)
        {
            AtWar = false;
            PendingOffer = OfferKind.None;
            PendingDeal = null;
            TruceDays = TruceLengthDays;
            _offerCooldownDays = 180;
            // Чем больше крови пролито, тем дольше помнят: потерянные системы и корабли добавляют обиду
            float bloodshed = Smart >= 1 ? Mathf.Min(25f, _systemsLostInWar * 4f + _shipsLostInWar * 0.4f) : 0f;
            AddMemory("grudge", -15f - bloodshed);
            AddMemory("trust", -100f);
            SiegeManager.Instance?.ClearSieges(0, OwnerId);
            Mode = ArmyMode.Gather;
            ArmyTargetSystemId = -1;
            RecomputeOpinion();
            if (!silent) AICoalition.OnPlayerPeace(OwnerId);
            // Сепаратный мир: союзник ещё воюет с этой империей
            if (!silent)
                foreach (var ally in Alive)
                    if (ally != this && ally.HasAlliance && AIRelations.AtWar(ally.OwnerId, OwnerId))
                        ally.AddMemory("separate", -10f);
            if (!silent)
                NotificationCenter.Show(Loc.T("Мир заключён"), Loc.F("{0} прекращает военные действия. Перемирие — 3 года", AIName),
                    NotificationCenter.Kind.Success, 8f);
            RaiseDiplomacyChanged();
        }

        // ==================== ОЦЕНКА ПРЕДЛОЖЕНИЙ ИГРОКА ====================

        public struct Evaluation
        {
            public float Score, Threshold;
            public bool Accept => Score >= Threshold;
            public List<OpinionModifier> Reasons;
            public string Blocker;   // непреодолимая причина отказа
        }

        /// <summary>Примут ли мир — с разбором причин.</summary>
        public Evaluation EvaluatePeace()
        {
            var e = new Evaluation { Threshold = PeaceAcceptThreshold, Reasons = new List<OpinionModifier>() };
            void Add(string l, string d, float v, LGIcon i) { e.Reasons.Add(new OpinionModifier(l, d, v, i)); e.Score += v; }
            if (!AtWar) { e.Blocker = Loc.T("Вы не воюете"); return e; }

            Add(Loc.T("Усталость от войны"), Loc.F("{0:0}% — растёт со временем и потерями", WarWeariness), WarWeariness * 0.6f, LGIcon.Clock);
            float myPow = Mathf.Max(1f, EmpireStats.MilitaryPower(OwnerId));
            float balance = Mathf.Clamp((EmpireStats.MilitaryPower(0) / myPow - 1f) * 40f, -40f, 40f);
            Add(Loc.T("Соотношение сил"), balance >= 0 ? Loc.T("Ваш флот сильнее — продолжать опасно") : Loc.T("Их флот сильнее — они рассчитывают на победу"), balance, LGIcon.Fleet);
            if (_systemsLostInWar > 0) Add(Loc.T("Потерянные системы"), Loc.F("Захвачено вами: {0}", _systemsLostInWar), _systemsLostInWar * 6f, LGIcon.Starbase);
            if (_systemsTakenInWar > 0) Add(Loc.T("Успехи на фронте"), Loc.F("Они захватили у вас: {0}", _systemsTakenInWar), -_systemsTakenInWar * 5f, LGIcon.Siege);
            if (_capitalLost) Add(Loc.T("Потеря столицы"), Loc.T("Правительство в изгнании"), 25f, LGIcon.Warning);
            Add(Loc.T("Отношение к вам"), OpinionLabel(Opinion), Opinion * 0.2f, LGIcon.Diplomacy);
            if (Mathf.Abs(Profile.PeaceBias) > 0.1f) Add(Loc.F("Характер: {0}", Profile.Name.ToLower()), Profile.PeaceBias > 0 ? Loc.T("Предпочитают договариваться") : Loc.T("Не любят отступать"), Profile.PeaceBias, LGIcon.Leader);
            if (AICoalition.IsMember(OwnerId) && AICoalition.TargetOwner == 0)
                Add(Loc.T("Союзники по коалиции"), Loc.T("Остальные члены коалиции требуют довести войну до конца"), -12f, LGIcon.Swords);
            if (_warMonths < 3) Add(Loc.T("Война только началась"), Loc.T("Первые месяцы стороны ещё надеются на победу"), -20f, LGIcon.Calendar);
            return e;
        }

        /// <summary>Примут ли пакт о ненападении — с разбором причин.</summary>
        public Evaluation EvaluatePact()
        {
            var e = new Evaluation { Threshold = 0f, Reasons = new List<OpinionModifier>() };
            void Add(string l, string d, float v, LGIcon i) { e.Reasons.Add(new OpinionModifier(l, d, v, i)); e.Score += v; }
            if (AtWar) { e.Blocker = Loc.T("Сначала нужно заключить мир"); return e; }
            if (HasPact) { e.Blocker = Loc.T("Пакт уже действует"); return e; }

            Add(Loc.T("Отношение к вам"), OpinionLabel(Opinion), Opinion, LGIcon.Diplomacy);
            Add(Loc.T("Требование характера"), Loc.F("{0} империя соглашается при отношении от {1:+0;-0;0}", Profile.Name, Profile.PactOpinion), -Profile.PactOpinion, LGIcon.Leader);
            if (WarPlanTarget == 0)
                Add(Loc.T("Уклончивый ответ"), Loc.T("Посол ссылается на «внутренние дела» и уходит от ответа"), -30f, LGIcon.Warning);
            else if (WarPlanTarget > 0)
                Add(Loc.T("Им нужен спокойный тыл"), Loc.T("У них свои планы на другом рубеже — пакт с вами кстати"), 12f, LGIcon.Handshake);
            float ratio = PowerRatio;
            if (ratio < 0.85f && Personality != AIPersonality.Militarist)
                Add(Loc.T("Опасаются вашего флота"), Loc.T("Пакт защищает их от вашего нападения"), 10f, LGIcon.Defense);
            if (ratio > 1.3f && Personality == AIPersonality.Militarist)
                Add(Loc.T("Не видят в вас угрозы"), Loc.T("Сильный не связывает себе руки договорами"), -15f, LGIcon.Fleet);
            if (_memory.TryGetValue("betrayal", out float b) && b < -5f)
                Add(Loc.T("Вы уже нарушали пакт"), Loc.T("Вероломству не доверяют"), b * 0.5f, LGIcon.Betrayal);
            if (Mathf.Abs(Reputation.Value) >= 5f)
                Add(Loc.F("Ваша репутация: {0}", Reputation.Current.ToLower()), Loc.T("Слову надёжной империи верят охотнее"), Reputation.Value * 0.3f, LGIcon.Diplomacy);
            return e;
        }

        /// <summary>Примут ли оборонительный союз — с разбором причин.</summary>
        public Evaluation EvaluateAlliance()
        {
            var e = new Evaluation { Threshold = 0f, Reasons = new List<OpinionModifier>() };
            void Add(string l, string d, float v, LGIcon i) { e.Reasons.Add(new OpinionModifier(l, d, v, i)); e.Score += v; }
            if (AtWar) { e.Blocker = Loc.T("Сначала нужно заключить мир"); return e; }
            if (HasAlliance) { e.Blocker = Loc.T("Союз уже действует"); return e; }
            if (AICoalition.IsMember(OwnerId) && AICoalition.TargetOwner == 0) { e.Blocker = Loc.T("Они состоят в коалиции против вас"); return e; }
            foreach (var other in Alive)
                if (other != this && other.HasAlliance && AIRelations.AtWar(OwnerId, other.OwnerId))
                { e.Blocker = Loc.F("Вы в союзе с их врагом: {0}", other.AIName); return e; }

            Add(Loc.T("Отношение к вам"), OpinionLabel(Opinion), Opinion, LGIcon.Diplomacy);
            float need = Profile.PactOpinion + 20f;
            Add(Loc.T("Требование характера"), Loc.F("{0} империя вступает в союз при отношении от {1:+0;-0;0}", Profile.Name, need), -need, LGIcon.Leader);
            if (HasPact) Add(Loc.T("Пакт уже действует"), Loc.T("Годы ненападения — основа союза"), 8f, LGIcon.Handshake);
            else Add(Loc.T("Нет пакта о ненападении"), Loc.T("Сразу в союз, минуя пакт, — большой шаг"), -10f, LGIcon.Handshake);

            foreach (var other in Alive)
            {
                if (other == this) continue;
                if (AIRelations.AtWar(OwnerId, other.OwnerId) && Diplomacy.AtWar(0, other.OwnerId))
                    Add(Loc.F("Общий враг: {0}", other.AIName), Loc.T("Вместе против одного противника"), 20f, LGIcon.Swords);
                else if (EmpireStats.SharedBorderCount(OwnerId, other.OwnerId) > 0
                         && EmpireStats.MilitaryPower(other.OwnerId) > Mathf.Max(1f, EmpireStats.MilitaryPower(OwnerId)) * 1.3f
                         && AIRelations.Opinion(this, other) < 0f)
                    Add(Loc.F("Опасный сосед: {0}", Contacts.NameForPlayer(other.OwnerId)), Loc.T("Им нужна защита от сильной и недружелюбной империи"), 10f, LGIcon.Defense);
            }

            float ratio = EmpireStats.MilitaryPower(0) / Mathf.Max(1f, EmpireStats.MilitaryPower(OwnerId));
            if (ratio >= 1f) Add(Loc.T("Сильный союзник"), Loc.F("Ваш флот сильнее их (×{0:0.0})", ratio), 8f, LGIcon.Fleet);
            else if (ratio < 0.5f) Add(Loc.T("Слабый союзник"), Loc.T("Ваш флот вдвое слабее — от вас мало пользы в войне"), -10f, LGIcon.Fleet);

            float character = Personality switch
            {
                AIPersonality.Militarist => -5f,
                AIPersonality.Mystic => -5f,
                AIPersonality.Gardener => 5f,
                AIPersonality.Scientific => 3f,
                _ => 0f
            };
            if (Mathf.Abs(character) > 0.1f)
                Add(Loc.F("Характер: {0}", Profile.Name.ToLower()), character > 0f ? Loc.T("Ценят надёжных друзей") : Loc.T("Не любят связывать себе руки"), character, LGIcon.Leader);
            if (_memory.TryGetValue("betrayal", out float b) && b < -5f)
                Add(Loc.T("Вы уже нарушали пакт"), Loc.T("Вероломству не доверяют"), b * 0.6f, LGIcon.Betrayal);
            if (_memory.TryGetValue("allyabandon", out float ab) && ab < -5f)
                Add(Loc.T("Вы бросили их в беде"), Loc.T("Прошлый союз вы не сдержали"), ab * 0.6f, LGIcon.Betrayal);
            if (Mathf.Abs(Reputation.Value) >= 5f)
                Add(Loc.F("Ваша репутация: {0}", Reputation.Current.ToLower()), Loc.T("Союз — это обещание; репутация показывает, держите ли вы обещания"), Reputation.Value * 0.5f, LGIcon.Diplomacy);
            return e;
        }

        // ==================== ДЕЙСТВИЯ ИГРОКА ====================

        public bool PlayerGiftEnergy()
        {
            var eco = EconomyManager.Instance;
            if (eco == null || AtWar || !eco.TrySpend(GiftEnergyAmount, 0f, 0f, 0f)) return false;
            EnergyCredits += GiftEnergyAmount;
            AddMemory("gifts", 8f * Profile.GiftOpinionMult);
            return true;
        }

        public bool PlayerGiftAlloys()
        {
            var eco = EconomyManager.Instance;
            if (eco == null || AtWar || !eco.TrySpend(0f, 0f, GiftAlloysAmount, 0f)) return false;
            Alloys += GiftAlloysAmount;
            AddMemory("gifts", 12f * Profile.GiftOpinionMult);
            return true;
        }

        /// <summary>Причина, по которой войну объявить нельзя (или null).</summary>
        public string WarBlocker => AtWar ? Loc.T("Война уже идёт")
            : TruceDays > 0 ? Loc.F("Действует перемирие ещё {0} мес.", Mathf.CeilToInt(TruceDays / 30f))
            : null;

        public bool PlayerDeclareWar()
        {
            if (WarBlocker != null) return false;
            bool cause = CasusBelli.Has(OwnerId);
            bool betrayal = HasPact, allied = HasAlliance;
            if (betrayal)
            {
                HasPact = false;
                AddMemory("betrayal", allied ? -60f : -40f);
                AddMemory("trust", -100f);
                Reputation.Add(allied ? -40f : cause ? -20f : -30f,
                    allied ? Loc.F("Нападение на союзника: {0}", AIName) : Loc.F("Нарушен пакт с {0}", AIName));
            }
            if (!cause) Reputation.Add(CasusBelli.NoCauseReputation, Loc.F("Война без повода против {0}", AIName));
            AddMemory("aggression", cause ? -5f : -15f);
            StartWar();
            NotificationCenter.Show(betrayal ? Loc.T("ВЫ НАРУШИЛИ ПАКТ") : Loc.T("ВОЙНА ОБЪЯВЛЕНА"),
                Loc.F("{0} переходит в наступление. Держите флот у границ и осаждайте их системы", AIName),
                NotificationCenter.Kind.Danger, 8f);
            return true;
        }

        public bool PlayerProposePeace(out Evaluation eval)
        {
            eval = EvaluatePeace();
            var eco = EconomyManager.Instance;
            if (eval.Blocker != null || eco == null || !eco.TrySpend(0f, 0f, 0f, PeaceInfluenceCost)) return false;
            if (eval.Accept) { MakePeace(); return true; }
            NotificationCenter.Show(Loc.T("Мир отвергнут"), Loc.F("{0} продолжает войну: готовность {1:0} из {2:0}", AIName, eval.Score, eval.Threshold),
                NotificationCenter.Kind.Danger, 6f);
            return false;
        }

        public bool PlayerProposePact(out Evaluation eval)
        {
            eval = EvaluatePact();
            var eco = EconomyManager.Instance;
            if (eval.Blocker != null || eco == null || !eco.TrySpend(0f, 0f, 0f, PactInfluenceCost)) return false;
            if (eval.Accept)
            {
                HasPact = true;
                RecomputeOpinion();
                NotificationCenter.Show(Loc.T("Пакт подписан"), Loc.F("{0}: пакт о ненападении вступил в силу", AIName), NotificationCenter.Kind.Success, 6f);
                RaiseDiplomacyChanged();
                return true;
            }
            NotificationCenter.Show(Loc.T("Пакт отклонён"), Loc.F("{0}: готовность {1:+0;-0;0} (нужно не меньше 0)", AIName, eval.Score),
                NotificationCenter.Kind.Warning, 6f);
            return false;
        }

        public void PlayerCancelPact()
        {
            if (!HasPact) return;
            if (HasAlliance) PlayerCancelAlliance();
            HasPact = false;
            AddMemory("pactcancel", -10f);
            Reputation.Add(-3f, Loc.F("Расторгнут пакт с {0}", AIName));
            NotificationCenter.Show(Loc.T("Пакт расторгнут"), Loc.F("Договор с {0} больше не действует", AIName), NotificationCenter.Kind.Warning, 5f);
            RaiseDiplomacyChanged();
        }

        /// <summary>Игрок выходит из союза (пакт о ненападении остаётся).</summary>
        public void PlayerCancelAlliance()
        {
            if (!HasAlliance) return;
            HasAlliance = false;
            AddMemory("alliancecancel", -15f);
            Reputation.Add(-5f, Loc.F("Расторгнут союз с {0}", AIName));
            NotificationCenter.Show(Loc.T("Союз расторгнут"), Loc.F("Вы больше не обязаны защищать {0}, а они — вас. Пакт о ненападении остаётся", AIName), NotificationCenter.Kind.Warning, 6f);
            RecomputeOpinion();
            RaiseDiplomacyChanged();
        }

        private void SignAlliance()
        {
            if (AtWar || HasAlliance) return;
            HasPact = true;
            HasAlliance = true;
            RecomputeOpinion();
            NotificationCenter.Show(Loc.T("Союз заключён"), Loc.F("{0}: оборонительный союз вступил в силу. Если на одного нападут, другой придёт на помощь", AIName),
                NotificationCenter.Kind.Success, 8f);
            RaiseDiplomacyChanged();
        }

        /// <summary>Союзника этой империи атаковали — игрок пришёл на помощь (из события-призыва).</summary>
        public void PlayerHonorAlliance(AIEmpireManager attacker)
        {
            if (attacker == null || attacker == this) return;
            AddMemory("allyhelp", 15f);
            Reputation.Add(5f, Loc.F("Пришли на помощь союзнику: {0}", AIName));
            attacker.PlayerJoinWarAsAlly(this);
        }

        /// <summary>Игрок отказался защищать союзника: союз распадается, репутация падает.</summary>
        public void PlayerAbandonAlliance(AIEmpireManager attacker)
        {
            if (!HasAlliance) return;
            HasAlliance = false;
            AddMemory("allyabandon", -30f);
            Reputation.Add(-15f, Loc.F("Брошен союзник: {0}", AIName));
            NotificationCenter.Show(Loc.T("Союз распался"), Loc.F("{0} не простит, что вы не пришли на помощь", AIName), NotificationCenter.Kind.Danger, 8f);
            RecomputeOpinion();
            RaiseDiplomacyChanged();
        }

        /// <summary>
        /// Игрок вступает в войну против этой империи, защищая союзника. Это не агрессия и не вероломство:
        /// пакт и перемирие с этой империей снимаются без удара по репутации.
        /// </summary>
        public bool PlayerJoinWarAsAlly(AIEmpireManager ally)
        {
            if (AtWar || IsEliminated) return false;
            if (!Contacts.PlayerMet(OwnerId)) Contacts.Meet(0, OwnerId);
            if (HasPact) { HasPact = false; AddMemory("pactcancel", -10f); }
            TruceDays = 0;
            AddMemory("aggression", -5f);
            StartWar();
            NotificationCenter.Show(Loc.T("ВЫ ВСТУПИЛИ В ВОЙНУ"),
                Loc.F("Вы защищаете союзника {0} от империи {1}", ally != null ? ally.AIName : "", AIName),
                NotificationCenter.Kind.Danger, 8f);
            return true;
        }

        public void AcceptOffer()
        {
            var kind = PendingOffer;
            PendingOffer = OfferKind.None;
            PendingDeal = null;
            if (kind == OfferKind.Peace && AtWar) MakePeace();
            else if (kind == OfferKind.Pact && !AtWar)
            {
                HasPact = true;
                RecomputeOpinion();
                NotificationCenter.Show(Loc.T("Пакт подписан"), Loc.F("{0}: пакт о ненападении вступил в силу", AIName), NotificationCenter.Kind.Success, 6f);
            }
            else if (kind == OfferKind.Alliance) SignAlliance();
            RaiseDiplomacyChanged();
        }

        public void DeclineOffer()
        {
            if (PendingOffer == OfferKind.None) return;
            PendingOffer = OfferKind.None;
            PendingDeal = null;
            _offerCooldownDays = 180;
            AddMemory("rejected", -5f);
        }

        /// <summary>Торговая сделка: игрок отдал/получил выгоду (balance &gt; 0 — в пользу ИИ).</summary>
        public void RegisterTrade(float balance)
        {
            AddMemory("trade", Mathf.Clamp(balance, -4f, 4f) * Profile.TradeOpinionMult);
        }

        // ==================== СОХРАНЕНИЕ ====================

        private void CaptureDiplomacy(AISave s)
        {
            s.AtWar = AtWar; s.Pact = HasPact; s.Alliance = HasAlliance; s.TruceDays = TruceDays; s.Weariness = WarWeariness;
            s.WarMonths = _warMonths; s.SystemsLost = _systemsLostInWar; s.SystemsTaken = _systemsTakenInWar;
            s.ShipsLost = _shipsLostInWar; s.PlayerShipsLost = _playerShipsLostInWar; s.CapitalLost = _capitalLost;
            s.Offer = (int)PendingOffer; s.OfferReason = PendingOfferReason; s.OfferDays = _offerDays;
            s.OfferCooldown = _offerCooldownDays; s.WarCooldown = _warCooldownDays;
            foreach (var kv in _memory) { s.MemoryKeys.Add(kv.Key); s.MemoryValues.Add(kv.Value); }
            CaptureDeals(s);
        }

        private void RestoreDiplomacy(AISave s, int version)
        {
            _memory.Clear();
            if (version < 2)
            {
                // Раньше война = отрицательные отношения
                AtWar = s.Relations < 0f;
                HasPact = false; HasAlliance = false; TruceDays = 0; WarWeariness = 0f;
                _warCooldownDays = 360;
            }
            else
            {
                AtWar = s.AtWar; HasPact = s.Pact; HasAlliance = s.Alliance && s.Pact && !s.AtWar; TruceDays = s.TruceDays; WarWeariness = s.Weariness;
                _warMonths = s.WarMonths; _systemsLostInWar = s.SystemsLost; _systemsTakenInWar = s.SystemsTaken;
                _shipsLostInWar = s.ShipsLost; _playerShipsLostInWar = s.PlayerShipsLost; _capitalLost = s.CapitalLost;
                PendingOffer = (OfferKind)Mathf.Clamp(s.Offer, 0, (int)OfferKind.Alliance); PendingOfferReason = s.OfferReason ?? "";
                _offerDays = s.OfferDays; _offerCooldownDays = s.OfferCooldown; _warCooldownDays = s.WarCooldown;
                for (int i = 0; i < s.MemoryKeys.Count && i < s.MemoryValues.Count; i++)
                    if (MemoryTable.ContainsKey(s.MemoryKeys[i])) _memory[s.MemoryKeys[i]] = s.MemoryValues[i];
            }
            RestoreDeals(s);
            // Старое сохранение: предложение мира/пакта без стола переговоров
            if (PendingOffer == OfferKind.Peace || PendingOffer == OfferKind.Pact || PendingOffer == OfferKind.Alliance)
            {
                if (PendingDeal == null) { string r = PendingOfferReason; int days = _offerDays; Offer(PendingOffer, r); _offerDays = days; }
            }
            else if (PendingOffer != OfferKind.None && PendingDeal == null) PendingOffer = OfferKind.None;
            RecomputeOpinion();
            _lastOpinionBucket = Opinion;
        }
    }
}
