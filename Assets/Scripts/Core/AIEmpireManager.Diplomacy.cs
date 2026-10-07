using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Дипломатия ИИ. Отношение к игроку — сумма понятных слагаемых (характер, угроза, граница,
    /// подарки, торговля, пакты, обиды), каждое видно в окне дипломатии с пояснением.
    /// Состояния: мир, пакт о ненападении, война, перемирие после войны.
    /// ИИ сам объявляет войну (плохое отношение + перевес сил), предлагает мир (усталость или поражение)
    /// и пакт (хорошее отношение и уважение к вашему флоту).
    /// </summary>
    public partial class AIEmpireManager
    {
        public enum OfferKind { None, Peace, Pact, Trade, Demand }

        public static event Action OnDiplomacyChanged;
        private static void RaiseDiplomacyChanged() => OnDiplomacyChanged?.Invoke();
        public static void NotifyDiplomacyChanged() => OnDiplomacyChanged?.Invoke();

        // ==================== СОСТОЯНИЕ ====================

        public bool AtWar { get; private set; }
        public bool HasPact { get; private set; }
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
            ["gifts"]      = new MemoryInfo { Label = "Подарки", Detail = "Щедрость помнят, но со временем забывают", Icon = LGIcon.Gift, MonthlyDecay = 0.08f, Min = 0f, Max = 40f },
            ["trade"]      = new MemoryInfo { Label = "Торговые сделки", Detail = "Выгодная торговля сближает, невыгодная — раздражает", Icon = LGIcon.Trade, MonthlyDecay = 0.08f, Min = -20f, Max = 30f },
            ["border"]     = new MemoryInfo { Label = "Нарушение границ", Detail = "Ваши военные корабли в их системах в мирное время", Icon = LGIcon.Border, MonthlyDecay = 0.25f, Min = -40f, Max = 0f },
            ["grudge"]     = new MemoryInfo { Label = "Обида за войну", Detail = "Недавняя война ещё не забыта", Icon = LGIcon.Swords, MonthlyDecay = 0.04f, Min = -40f, Max = 0f },
            ["betrayal"]   = new MemoryInfo { Label = "Вероломство", Detail = "Вы объявили войну, нарушив пакт о ненападении", Icon = LGIcon.Betrayal, MonthlyDecay = 0.03f, Min = -60f, Max = 0f },
            ["aggression"] = new MemoryInfo { Label = "Агрессия", Detail = "Вы первыми объявили им войну", Icon = LGIcon.Warning, MonthlyDecay = 0.05f, Min = -30f, Max = 0f },
            ["pactcancel"] = new MemoryInfo { Label = "Расторгнутый пакт", Detail = "Вы в одностороннем порядке вышли из пакта", Icon = LGIcon.Handshake, MonthlyDecay = 0.1f, Min = -20f, Max = 0f },
            ["rejected"]   = new MemoryInfo { Label = "Отвергнутое предложение", Detail = "Вы отклонили их дипломатическое предложение", Icon = LGIcon.Close, MonthlyDecay = 0.2f, Min = -15f, Max = 0f },
            ["incident"]   = new MemoryInfo { Label = "Инциденты", Detail = "Как вы повели себя в пограничных и дипломатических происшествиях", Icon = LGIcon.Info, MonthlyDecay = 0.06f, Min = -35f, Max = 30f },
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
            AtWar = false; HasPact = false; TruceDays = 0; WarWeariness = 0f;
            _memory.Clear();
            PendingOffer = OfferKind.None;
            // Первые годы — время на обустройство: ИИ не объявляет войну
            _warCooldownDays = GameSession.Settings.Difficulty == 2 ? 360 * 2 : 360 * 4;
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

            Add($"Характер: {Profile.Name.ToLower()} империя", Profile.Summary, Profile.BaseOpinion, LGIcon.Leader);

            int diff = GameSession.Settings.Difficulty;
            if (diff == 0) Add("Сложность: лёгкая", "Соперник изначально настроен миролюбиво", 10f, LGIcon.Info);
            else if (diff == 2) Add("Сложность: сложная", "Соперник изначально подозрителен", -10f, LGIcon.Info);

            float myPow = Mathf.Max(1f, EmpireStats.MilitaryPower(OwnerId));
            float playerPow = EmpireStats.MilitaryPower(0);
            float ratio = playerPow / myPow;
            if (ratio > 1.15f)
            {
                float v = -Mathf.Clamp((ratio - 1f) * 25f * Profile.ThreatSensitivity * AggressionFactor, 0f, 35f);
                Add($"Угроза: ваш флот сильнее (×{ratio:0.0})", "Чем сильнее ваш флот по сравнению с их, тем тревожнее соседу", v, LGIcon.Fleet);
            }
            else if (Personality == AIPersonality.Militarist && ratio < 0.67f && playerPow >= 0f)
            {
                Add("Вы выглядите слабыми", "Воинственная империя видит в слабом соседе добычу", -12f * AggressionFactor, LGIcon.Target);
            }

            int border = EmpireStats.SharedBorderCount(OwnerId, 0);
            if (border > 0)
                Add($"Общая граница ({border} сист.)", "Соседство порождает споры за территорию", -Mathf.Min(18f, border * 3f), LGIcon.Border);

            int mySys = EmpireStats.SystemCount(OwnerId), playerSys = EmpireStats.SystemCount(0);
            if (playerSys >= 5 && playerSys > mySys * 1.25f)
                Add("Вы расширяетесь быстрее", $"У вас {playerSys} систем против их {mySys}", Personality == AIPersonality.Militarist ? -8f : -5f, LGIcon.Starbase);

            int myTech = ResearchedCount, playerTech = EmpireStats.TechCount(0);
            if (Personality == AIPersonality.Scientific && playerTech > myTech + 3)
                Add("Научное соперничество", $"Вы опережаете их в науке ({playerTech} против {myTech})", -8f, LGIcon.Research);
            if (Personality == AIPersonality.Trader && !AtWar && border > 0 && _memory.TryGetValue("trade", out float tr) && tr > 5f)
                Add("Выгодный сосед", "Торговая империя ценит партнёров у самой границы", 6f, LGIcon.Trade);

            if (HasPact) Add("Пакт о ненападении", "Действующий договор укрепляет доверие", 15f, LGIcon.Handshake);
            foreach (var other in Alive)
            {
                if (other == this || !other.AtWar || !AIRelations.AtWar(OwnerId, other.OwnerId)) continue;
                Add($"Общий враг: {other.AIName}", "Вы оба воюете с этой империей", 12f, LGIcon.Swords);
            }
            if (TruceDays > 0 && !AtWar) Add($"Перемирие ({Mathf.CeilToInt(TruceDays / 30f)} мес.)", "Мирный договор ещё в силе", 5f, LGIcon.Peace);
            if (AtWar) Add("Идёт война", "Между вашими империями открытый конфликт", -40f, LGIcon.Swords);

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
              v <= -60f ? "Враждебность"
            : v <= -20f ? "Неприязнь"
            : v < 10f   ? "Настороженность"
            : v < 35f   ? "Расположение"
            : "Дружба";

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
                        NotificationCenter.Show("Нарушение границ", $"{AIName} требует увести ваш флот из их систем", NotificationCenter.Kind.Warning, 6f);
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
            if (now < was)
                NotificationCenter.Show("Отношения ухудшились", $"{AIName}: {OpinionLabel(Opinion).ToLower()} ({Opinion:+0;-0;0})",
                    NotificationCenter.Kind.Warning, 5f);
            else
                NotificationCenter.Show("Отношения улучшились", $"{AIName}: {OpinionLabel(Opinion).ToLower()} ({Opinion:+0;-0;0})",
                    NotificationCenter.Kind.Success, 4f);
            RaiseDiplomacyChanged();
        }

        private void MonthlyDiplomacy()
        {
            // Затухание памяти
            var keys = new List<string>(_memory.Keys);
            foreach (var k in keys)
            {
                float v = _memory[k] * (1f - MemoryTable[k].MonthlyDecay);
                if (Mathf.Abs(v) < 0.5f) _memory.Remove(k);
                else _memory[k] = v;
            }

            if (AtWar)
            {
                _warMonths++;
                WarWeariness = Mathf.Min(100f, WarWeariness + 2.5f * Profile.WearinessRate);
            }
            else WarWeariness = Mathf.Max(0f, WarWeariness - 5f);

            RecomputeOpinion();
            MonthlyAgreements();

            if (AtWar) ConsiderPeaceOffer();
            else
            {
                ConsiderWar();
                if (!AtWar) ConsiderPactOffer();
                if (!AtWar) ConsiderTradeOffer();
            }
            RaiseDiplomacyChanged();
        }

        // ==================== РЕШЕНИЯ ИИ ====================

        private float PowerRatio => EmpireStats.MilitaryPower(OwnerId) / Mathf.Max(1f, EmpireStats.MilitaryPower(0));

        /// <summary>Порог мнения для войны: на высокой сложности ИИ вспыльчивее.</summary>
        private float WarThreshold => Profile.WarOpinionThreshold / AggressionFactor;

        private void ConsiderWar()
        {
            if (TruceDays > 0 || _warCooldownDays > 0 || _bankrupt) return;
            // Уже воюет с другим ИИ — второй фронт откроет только при большом перевесе
            if (WarsWithAIs > 0 && PowerRatio < Profile.WarPowerRatio * 1.5f) return;
            if (EmpireStats.SharedBorderCount(OwnerId, 0) == 0 && Personality != AIPersonality.Militarist) return;
            float ratio = PowerRatio;
            if (Opinion > WarThreshold || ratio < Profile.WarPowerRatio) return;
            if (HasPact && !(Personality == AIPersonality.Militarist && Opinion <= -60f && ratio >= 1.6f)) return;
            if (UnityEngine.Random.value > 0.4f) return;

            var reasons = new List<string>();
            foreach (var m in OpinionBreakdown)
                if (m.Value < 0f && reasons.Count < 2) reasons.Add(m.Label.ToLower());
            reasons.Add($"перевес сил ×{ratio:0.0}");

            bool broke = HasPact;
            HasPact = false;
            StartWar();
            NotificationCenter.Show(broke ? "ВЕРОЛОМНОЕ НАПАДЕНИЕ" : "ВОЙНА ОБЪЯВЛЕНА",
                $"{AIName} объявляет вам войну. Причины: {string.Join(", ", reasons)}",
                NotificationCenter.Kind.Danger, 10f);
        }

        private void ConsiderPeaceOffer()
        {
            if (PendingOffer != OfferKind.None || _offerCooldownDays > 0) return;
            bool tired = WarWeariness >= Profile.PeaceWeariness;
            bool losing = _warMonths >= 3 && PowerRatio < 0.6f;
            if (!tired && !losing && !_capitalLost) return;

            string reason = _capitalLost ? "их столица пала"
                          : losing ? "их флот разбит и уступает вашему"
                          : $"страна устала от войны ({WarWeariness:0}%)";
            Offer(OfferKind.Peace, reason);
            NotificationCenter.Show("Предложение мира", $"{AIName} предлагает мир: {reason}. Ответьте в окне дипломатии",
                NotificationCenter.Kind.Info, 9f);
        }

        private void ConsiderPactOffer()
        {
            if (HasPact || PendingOffer != OfferKind.None || _offerCooldownDays > 0) return;
            if (Opinion < Profile.PactOpinion + 15f) return;
            if (PowerRatio > 1.2f && Personality == AIPersonality.Militarist) return;
            if (UnityEngine.Random.value > 0.25f) return;
            Offer(OfferKind.Pact, "они ценят добрососедство и хотят гарантий безопасности");
            NotificationCenter.Show("Предложение пакта", $"{AIName} предлагает пакт о ненападении. Ответьте в окне дипломатии",
                NotificationCenter.Kind.Info, 8f);
        }

        private void Offer(OfferKind kind, string reason)
        {
            // Мир и пакт тоже ложатся на стол переговоров как договор
            var d = new Deal();
            d.Treaties.Add(new DealItem(kind == OfferKind.Peace ? DealItemKind.Peace : DealItemKind.Pact, 0f));
            SetPendingDeal(d, kind, reason);
        }

        private void StartWar()
        {
            AtWar = true;
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
            AddMemory("grudge", -15f);
            SiegeManager.Instance?.ClearSieges(0, OwnerId);
            Mode = ArmyMode.Gather;
            ArmyTargetSystemId = -1;
            RecomputeOpinion();
            if (!silent)
                NotificationCenter.Show("Мир заключён", $"{AIName} прекращает военные действия. Перемирие — 3 года",
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
            if (!AtWar) { e.Blocker = "Вы не воюете"; return e; }

            Add("Усталость от войны", $"{WarWeariness:0}% — растёт со временем и потерями", WarWeariness * 0.6f, LGIcon.Clock);
            float myPow = Mathf.Max(1f, EmpireStats.MilitaryPower(OwnerId));
            float balance = Mathf.Clamp((EmpireStats.MilitaryPower(0) / myPow - 1f) * 40f, -40f, 40f);
            Add("Соотношение сил", balance >= 0 ? "Ваш флот сильнее — продолжать опасно" : "Их флот сильнее — они рассчитывают на победу", balance, LGIcon.Fleet);
            if (_systemsLostInWar > 0) Add("Потерянные системы", $"Захвачено вами: {_systemsLostInWar}", _systemsLostInWar * 6f, LGIcon.Starbase);
            if (_systemsTakenInWar > 0) Add("Успехи на фронте", $"Они захватили у вас: {_systemsTakenInWar}", -_systemsTakenInWar * 5f, LGIcon.Siege);
            if (_capitalLost) Add("Потеря столицы", "Правительство в изгнании", 25f, LGIcon.Warning);
            Add("Отношение к вам", OpinionLabel(Opinion), Opinion * 0.2f, LGIcon.Diplomacy);
            if (Mathf.Abs(Profile.PeaceBias) > 0.1f) Add($"Характер: {Profile.Name.ToLower()}", Profile.PeaceBias > 0 ? "Предпочитают договариваться" : "Не любят отступать", Profile.PeaceBias, LGIcon.Leader);
            if (_warMonths < 3) Add("Война только началась", "Первые месяцы стороны ещё надеются на победу", -20f, LGIcon.Calendar);
            return e;
        }

        /// <summary>Примут ли пакт о ненападении — с разбором причин.</summary>
        public Evaluation EvaluatePact()
        {
            var e = new Evaluation { Threshold = 0f, Reasons = new List<OpinionModifier>() };
            void Add(string l, string d, float v, LGIcon i) { e.Reasons.Add(new OpinionModifier(l, d, v, i)); e.Score += v; }
            if (AtWar) { e.Blocker = "Сначала нужно заключить мир"; return e; }
            if (HasPact) { e.Blocker = "Пакт уже действует"; return e; }

            Add("Отношение к вам", OpinionLabel(Opinion), Opinion, LGIcon.Diplomacy);
            Add("Требование характера", $"{Profile.Name} империя соглашается при отношении от {Profile.PactOpinion:+0;-0;0}", -Profile.PactOpinion, LGIcon.Leader);
            float ratio = PowerRatio;
            if (ratio < 0.85f && Personality != AIPersonality.Militarist)
                Add("Опасаются вашего флота", "Пакт защищает их от вашего нападения", 10f, LGIcon.Defense);
            if (ratio > 1.3f && Personality == AIPersonality.Militarist)
                Add("Не видят в вас угрозы", "Сильный не связывает себе руки договорами", -15f, LGIcon.Fleet);
            if (_memory.TryGetValue("betrayal", out float b) && b < -5f)
                Add("Вы уже нарушали пакт", "Вероломству не доверяют", b * 0.5f, LGIcon.Betrayal);
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
        public string WarBlocker => AtWar ? "Война уже идёт"
            : TruceDays > 0 ? $"Действует перемирие ещё {Mathf.CeilToInt(TruceDays / 30f)} мес."
            : null;

        public bool PlayerDeclareWar()
        {
            if (WarBlocker != null) return false;
            bool betrayal = HasPact;
            if (betrayal) { HasPact = false; AddMemory("betrayal", -40f); }
            AddMemory("aggression", -15f);
            StartWar();
            NotificationCenter.Show(betrayal ? "ВЫ НАРУШИЛИ ПАКТ" : "ВОЙНА ОБЪЯВЛЕНА",
                $"{AIName} переходит в наступление. Держите флот у границ и осаждайте их системы",
                NotificationCenter.Kind.Danger, 8f);
            return true;
        }

        public bool PlayerProposePeace(out Evaluation eval)
        {
            eval = EvaluatePeace();
            var eco = EconomyManager.Instance;
            if (eval.Blocker != null || eco == null || !eco.TrySpend(0f, 0f, 0f, PeaceInfluenceCost)) return false;
            if (eval.Accept) { MakePeace(); return true; }
            NotificationCenter.Show("Мир отвергнут", $"{AIName} продолжает войну: готовность {eval.Score:0} из {eval.Threshold:0}",
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
                NotificationCenter.Show("Пакт подписан", $"{AIName}: пакт о ненападении вступил в силу", NotificationCenter.Kind.Success, 6f);
                RaiseDiplomacyChanged();
                return true;
            }
            NotificationCenter.Show("Пакт отклонён", $"{AIName}: готовность {eval.Score:+0;-0;0} (нужно не меньше 0)",
                NotificationCenter.Kind.Warning, 6f);
            return false;
        }

        public void PlayerCancelPact()
        {
            if (!HasPact) return;
            HasPact = false;
            AddMemory("pactcancel", -10f);
            NotificationCenter.Show("Пакт расторгнут", $"Договор с {AIName} больше не действует", NotificationCenter.Kind.Warning, 5f);
            RaiseDiplomacyChanged();
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
                NotificationCenter.Show("Пакт подписан", $"{AIName}: пакт о ненападении вступил в силу", NotificationCenter.Kind.Success, 6f);
            }
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
            s.AtWar = AtWar; s.Pact = HasPact; s.TruceDays = TruceDays; s.Weariness = WarWeariness;
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
                HasPact = false; TruceDays = 0; WarWeariness = 0f;
                _warCooldownDays = 360;
            }
            else
            {
                AtWar = s.AtWar; HasPact = s.Pact; TruceDays = s.TruceDays; WarWeariness = s.Weariness;
                _warMonths = s.WarMonths; _systemsLostInWar = s.SystemsLost; _systemsTakenInWar = s.SystemsTaken;
                _shipsLostInWar = s.ShipsLost; _playerShipsLostInWar = s.PlayerShipsLost; _capitalLost = s.CapitalLost;
                PendingOffer = (OfferKind)Mathf.Clamp(s.Offer, 0, 4); PendingOfferReason = s.OfferReason ?? "";
                _offerDays = s.OfferDays; _offerCooldownDays = s.OfferCooldown; _warCooldownDays = s.WarCooldown;
                for (int i = 0; i < s.MemoryKeys.Count && i < s.MemoryValues.Count; i++)
                    if (MemoryTable.ContainsKey(s.MemoryKeys[i])) _memory[s.MemoryKeys[i]] = s.MemoryValues[i];
            }
            RestoreDeals(s);
            // Старое сохранение: предложение мира/пакта без стола переговоров
            if (PendingOffer == OfferKind.Peace || PendingOffer == OfferKind.Pact)
            {
                if (PendingDeal == null) { string r = PendingOfferReason; int days = _offerDays; Offer(PendingOffer, r); _offerDays = days; }
            }
            else if (PendingOffer != OfferKind.None && PendingDeal == null) PendingOffer = OfferKind.None;
            RecomputeOpinion();
            _lastOpinionBucket = Opinion;
        }
    }
}
