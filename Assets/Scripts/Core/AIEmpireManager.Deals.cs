using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Переговоры за столом: оценка сделок, «что вас устроит», исполнение, действующие соглашения
    /// (выплаты, научный и торговый договоры) и собственные предложения ИИ — торговые обмены,
    /// договоры и требования дани.
    /// </summary>
    public partial class AIEmpireManager
    {
        public Deal PendingDeal { get; private set; }
        public readonly List<Agreement> Agreements = new List<Agreement>();

        private int _tradeOfferCooldown = 240;

        // ==================== ДЕЙСТВУЮЩИЕ СОГЛАШЕНИЯ ====================

        public bool HasAgreement(DealItemKind k) => Agreements.Exists(a => a.Kind == k);

        /// <summary>Месячный поток ресурса для ИИ по соглашениям с игроком (+ получает, − платит).</summary>
        public float DealIncome(string resource)
        {
            float v = 0f;
            foreach (var a in Agreements)
            {
                if (a.Kind == DealItemKind.TradeTreaty && resource == "energy") v += DealCatalog.TradeTreatyEnergy;
                if (!DealCatalog.IsPayment(a.Kind) || DealCatalog.ResourceKey(a.Kind) != resource) continue;
                v += a.PlayerPays ? a.Amount : -a.Amount;
            }
            return v;
        }

        public float TreatyResearchMult => HasAgreement(DealItemKind.ResearchTreaty) ? 1f + DealCatalog.ResearchTreatyBonus : 1f;

        /// <summary>Месячный поток ресурса для игрока по всем соглашениям со всеми империями.</summary>
        public static float PlayerDealIncome(string resource)
        {
            float v = 0f;
            foreach (var ai in All)
            {
                if (ai.IsEliminated) continue;
                foreach (var a in ai.Agreements)
                {
                    if (a.Kind == DealItemKind.TradeTreaty && resource == "energy") v += DealCatalog.TradeTreatyEnergy;
                    if (!DealCatalog.IsPayment(a.Kind) || DealCatalog.ResourceKey(a.Kind) != resource) continue;
                    v += a.PlayerPays ? -a.Amount : a.Amount;
                }
            }
            return v;
        }

        /// <summary>Множитель науки игрока от научных договоров.</summary>
        public static float PlayerTreatyResearchMult
        {
            get
            {
                float m = 1f;
                foreach (var ai in All)
                    if (!ai.IsEliminated && ai.HasAgreement(DealItemKind.ResearchTreaty)) m += DealCatalog.ResearchTreatyBonus;
                return m;
            }
        }

        private void MonthlyAgreements()
        {
            for (int i = Agreements.Count - 1; i >= 0; i--)
            {
                var a = Agreements[i];
                if (--a.MonthsLeft > 0) continue;
                Agreements.RemoveAt(i);
                string what = DealCatalog.IsPayment(a.Kind) ? "Выплаты" : DealCatalog.Name(a.Kind);
                NotificationCenter.Show("Соглашение истекло", $"{what} с {AIName}: срок вышел", NotificationCenter.Kind.Info, 5f);
            }
        }

        /// <summary>Война рвёт все соглашения и выплаты.</summary>
        private void BreakAgreements()
        {
            if (Agreements.Count == 0) return;
            Agreements.Clear();
            NotificationCenter.Show("Соглашения расторгнуты", $"Война с {AIName} отменила договоры и выплаты", NotificationCenter.Kind.Warning, 6f);
        }

        // ==================== ЦЕННОСТЬ ПРЕДМЕТОВ ДЛЯ ИИ ====================

        private static float BaseValue(string res) => res switch
        {
            "energy" => 1f,
            "minerals" => 1.2f,
            "alloys" => 3f,
            "influence" => 4f,
            _ => 1f
        };

        public float Stock(string res) => res switch
        {
            "energy" => EnergyCredits,
            "minerals" => Minerals,
            "alloys" => Alloys,
            "influence" => Influence,
            _ => 0f
        };

        private float Income(string res) => res switch
        {
            "energy" => MonthlyEnergyIncome,
            "minerals" => MonthlyMineralsIncome,
            "alloys" => MonthlyAlloysIncome,
            "influence" => MonthlyInfluenceIncome,
            _ => 0f
        };

        /// <summary>Нужда в ресурсе: 1.5 — не хватает, 0.7 — избыток.</summary>
        public float Need(string res)
        {
            float stock = Stock(res), income = Income(res);
            (float low, float high) = res switch
            {
                "energy" => (120f, 700f),
                "minerals" => (150f, 900f),
                "alloys" => (80f, 450f),
                _ => (40f, 220f)
            };
            if (stock < low || income < 1f) return 1.5f;
            if (stock > high && income > 0f) return 0.7f;
            return 1f;
        }

        private static float PlayerStock(string res)
        {
            var eco = EconomyManager.Instance;
            if (eco == null) return 0f;
            return res switch
            {
                "energy" => eco.EnergyCredits,
                "minerals" => eco.Minerals,
                "alloys" => eco.Alloys,
                "influence" => eco.Influence,
                _ => 0f
            };
        }

        /// <summary>Сколько этот предмет стоит для ИИ: + если ИИ получает, − если отдаёт.</summary>
        public float ItemValue(DealItem item, bool aiGives, out string blocker)
        {
            blocker = null;
            string res = DealCatalog.ResourceKey(item.Kind);
            float trader = Personality == AIPersonality.Trader ? 1.1f : 1f;

            if (DealCatalog.IsResource(item.Kind))
            {
                float v = BaseValue(res) * item.Amount * Need(res);
                if (aiGives)
                {
                    if (item.Amount > Stock(res) + 0.01f) blocker = $"У них нет столько: {DealCatalog.Name(item.Kind).ToLower()} {Stock(res):0}";
                    return -v / trader;
                }
                if (item.Amount > PlayerStock(res) + 0.01f) blocker = $"У вас нет столько: {DealCatalog.Name(item.Kind).ToLower()} {PlayerStock(res):0}";
                return v * trader;
            }
            if (DealCatalog.IsPayment(item.Kind))
            {
                float v = BaseValue(res) * item.Amount * DealCatalog.PaymentMonths * 0.5f * Need(res);
                if (aiGives && item.Amount > Mathf.Max(0f, Income(res)) * 0.6f + 1f)
                    blocker = "Они не потянут такие выплаты";
                return aiGives ? -v / trader : v * trader;
            }
            return 0f;
        }

        /// <summary>Сколько договор стоит для ИИ (взаимный; + выгоден им).</summary>
        public float TreatyValue(DealItemKind k, out string blocker)
        {
            blocker = null;
            switch (k)
            {
                case DealItemKind.Peace:
                {
                    var e = EvaluatePeace();
                    blocker = e.Blocker;
                    return (e.Score - e.Threshold) * 2f;
                }
                case DealItemKind.Pact:
                {
                    var e = EvaluatePact();
                    blocker = e.Blocker;
                    return e.Score >= 0f ? 15f + e.Score * 0.3f : e.Score * 2.5f;
                }
            }
            if (AtWar) { blocker = "Идёт война — сначала мир"; return 0f; }
            switch (k)
            {
                case DealItemKind.ResearchTreaty:
                {
                    if (HasAgreement(k)) { blocker = "Договор уже действует"; return 0f; }
                    if (Opinion < -25f) { blocker = "Слишком плохие отношения"; return 0f; }
                    float v = 10f + (EmpireStats.TechCount(0) - ResearchedCount) * 2.5f + Opinion * 0.15f;
                    if (Personality == AIPersonality.Scientific) v += 10f;
                    return v;
                }
                case DealItemKind.TradeTreaty:
                {
                    if (HasAgreement(k)) { blocker = "Соглашение уже действует"; return 0f; }
                    if (Opinion < -35f) { blocker = "Слишком плохие отношения"; return 0f; }
                    float v = 8f + Opinion * 0.1f;
                    if (Personality == AIPersonality.Trader) v += 15f;
                    return v;
                }
                case DealItemKind.Charts:
                {
                    int theyGet = 0, weGet = 0;
                    foreach (var s in EmpireStats.Systems)
                    {
                        bool p = s.IsSurveyedBy(0), me = s.IsSurveyedBy(OwnerId);
                        if (p && !me) theyGet++;
                        if (me && !p) weGet++;
                    }
                    if (theyGet == 0 && weGet == 0) { blocker = "Обмениваться нечем — карты совпадают"; return 0f; }
                    float v = theyGet * 2f - weGet * 1.2f;
                    if (Personality == AIPersonality.Militarist) v -= 10f;
                    return v;
                }
            }
            return 0f;
        }

        // ==================== ОЦЕНКА СДЕЛКИ ====================

        public struct DealVerdict
        {
            public float Balance;      // выгода ИИ
            public float Required;     // сколько выгоды им нужно
            public string Blocker;     // непреодолимое препятствие
            public bool Accept => Blocker == null && Balance >= Required;
            /// <summary>Готовность 0..1 для индикатора.</summary>
            public float Willingness => Blocker != null ? 0f
                : Mathf.Clamp01(0.5f + (Balance - Required) / (Mathf.Abs(Required) + 40f));
        }

        /// <summary>Порог: друзья соглашаются и на чуть невыгодное, недруги хотят премию.</summary>
        private float RequiredGain => Mathf.Clamp(6f - Opinion * 0.2f, -12f, 30f);

        public DealVerdict Evaluate(Deal d)
        {
            var v = new DealVerdict { Required = RequiredGain };
            if (d == null || d.IsEmpty) { v.Blocker = "Стол пуст"; return v; }
            if (IsEliminated) { v.Blocker = "Империя повержена"; return v; }

            if (AtWar && !d.HasTreaty(DealItemKind.Peace))
            { v.Blocker = "Во время войны говорят только о мире"; return v; }

            string block;
            foreach (var i in d.FromAI) { v.Balance += ItemValue(i, true, out block); v.Blocker ??= block; }
            foreach (var i in d.FromPlayer) { v.Balance += ItemValue(i, false, out block); v.Blocker ??= block; }
            foreach (var t in d.Treaties)
            {
                if (AtWar && t.Kind != DealItemKind.Peace) { v.Blocker ??= "Сначала мир — договоры потом"; continue; }
                v.Balance += TreatyValue(t.Kind, out block);
                v.Blocker ??= block;
            }
            if (d.IsGift && !AtWar) v.Required = 0f;   // подарок принимают всегда
            return v;
        }

        /// <summary>
        /// «Что вас устроит?»: ИИ дописывает со стороны игрока то, чего ему не хватает (самые нужные ресурсы),
        /// а если у игрока нечего взять — урезает свою часть. Возвращает null, если сделка невозможна.
        /// </summary>
        public Deal BalanceDeal(Deal src, out string reason)
        {
            reason = null;
            if (src == null || src.IsEmpty) { reason = "Сначала положите на стол то, что хотите получить"; return null; }
            var d = src.Clone();
            d.ByAI = false;
            var v = Evaluate(d);
            if (v.Blocker != null) { reason = v.Blocker; return null; }
            float deficit = v.Required - v.Balance + 1f;
            if (deficit <= 0f) return d;

            // 1) Просим ресурсы, которые им нужнее всего и которых у игрока хватает
            var order = new List<DealItemKind>(DealCatalog.Resources);
            order.Sort((a, b) => (BaseValue(DealCatalog.ResourceKey(b)) * Need(DealCatalog.ResourceKey(b)))
                .CompareTo(BaseValue(DealCatalog.ResourceKey(a)) * Need(DealCatalog.ResourceKey(a))));
            foreach (var k in order)
            {
                if (deficit <= 0f) break;
                string res = DealCatalog.ResourceKey(k);
                float unit = BaseValue(res) * Need(res) * (Personality == AIPersonality.Trader ? 1.1f : 1f);
                var have = d.Find(false, k);
                float already = have != null ? have.Amount : 0f;
                float room = PlayerStock(res) - already;
                if (room < DealCatalog.Step(k)) continue;
                float step = DealCatalog.Step(k);
                float add = Mathf.Min(room, Mathf.Ceil(deficit / unit / step) * step);
                add = Mathf.Floor(add / step) * step;
                if (add <= 0f) continue;
                if (have != null) have.Amount += add; else d.FromPlayer.Add(new DealItem(k, add));
                deficit -= add * unit;
            }

            // 2) Урезаем свою часть
            for (int i = d.FromAI.Count - 1; i >= 0 && deficit > 0f; i--)
            {
                var it = d.FromAI[i];
                if (DealCatalog.IsTreaty(it.Kind)) continue;
                float per = -ItemValue(new DealItem(it.Kind, 1f), true, out _);
                float step = DealCatalog.Step(it.Kind);
                while (it.Amount > 0f && deficit > 0f)
                {
                    it.Amount -= step;
                    deficit -= per * step;
                }
                if (it.Amount <= 0f) d.FromAI.RemoveAt(i);
            }

            if (deficit > 0f) { reason = "Вам нечего добавить — они не видят выгоды"; return null; }
            return d;
        }

        // ==================== ИСПОЛНЕНИЕ ====================

        public const float PactProposalCost = PactInfluenceCost;
        public const float PeaceProposalCost = PeaceInfluenceCost;

        /// <summary>Сколько влияния стоит игроку выдвинуть такое предложение.</summary>
        public static float ProposalCost(Deal d)
        {
            if (d == null || d.ByAI) return 0f;
            float c = 0f;
            if (d.HasTreaty(DealItemKind.Peace)) c += PeaceProposalCost;
            if (d.HasTreaty(DealItemKind.Pact)) c += PactProposalCost;
            return c;
        }

        /// <summary>Игрок предлагает сделку. true — принята и исполнена.</summary>
        public bool PlayerPropose(Deal d, out DealVerdict verdict)
        {
            verdict = Evaluate(d);
            var eco = EconomyManager.Instance;
            if (eco == null || verdict.Blocker != null) return false;
            float cost = ProposalCost(d);
            if (cost > 0f && eco.Influence < cost) { verdict.Blocker = $"Нужно {cost:0} влияния на переговоры"; return false; }
            if (cost > 0f) eco.TrySpend(0f, 0f, 0f, cost);

            if (!verdict.Accept)
            {
                RegisterTrade(-1f);
                return false;
            }
            Execute(d);
            if (d.IsGift) AddMemory("gifts", Mathf.Clamp(verdict.Balance / 12f, 2f, 20f) * Profile.GiftOpinionMult);
            else RegisterTrade(1.5f + Mathf.Max(0f, verdict.Balance - verdict.Required) / 30f);
            return true;
        }

        /// <summary>Передать ресурсы, оформить выплаты и договоры.</summary>
        private void Execute(Deal d)
        {
            var eco = EconomyManager.Instance;
            if (eco == null) return;

            foreach (var i in d.FromPlayer)
            {
                if (DealCatalog.IsResource(i.Kind))
                {
                    string r = DealCatalog.ResourceKey(i.Kind);
                    float amt = Mathf.Min(i.Amount, PlayerStock(r));
                    eco.TrySpend(r == "energy" ? amt : 0f, r == "minerals" ? amt : 0f, r == "alloys" ? amt : 0f, r == "influence" ? amt : 0f);
                    AddStock(r, amt);
                }
                else if (DealCatalog.IsPayment(i.Kind))
                    Agreements.Add(new Agreement { Kind = i.Kind, Amount = i.Amount, MonthsLeft = DealCatalog.PaymentMonths, PlayerPays = true });
            }
            foreach (var i in d.FromAI)
            {
                if (DealCatalog.IsResource(i.Kind))
                {
                    string r = DealCatalog.ResourceKey(i.Kind);
                    float amt = Mathf.Min(i.Amount, Stock(r));
                    SpendStock(r, amt);
                    switch (r)
                    {
                        case "energy": eco.EnergyCredits += amt; break;
                        case "minerals": eco.Minerals += amt; break;
                        case "alloys": eco.Alloys += amt; break;
                        case "influence": eco.Influence += amt; break;
                    }
                }
                else if (DealCatalog.IsPayment(i.Kind))
                    Agreements.Add(new Agreement { Kind = i.Kind, Amount = i.Amount, MonthsLeft = DealCatalog.PaymentMonths, PlayerPays = false });
            }
            eco.RaiseResourcesChanged();

            foreach (var t in d.Treaties)
            {
                switch (t.Kind)
                {
                    case DealItemKind.Peace:
                        if (AtWar) MakePeace();
                        break;
                    case DealItemKind.Pact:
                        if (!AtWar && !HasPact)
                        {
                            HasPact = true;
                            NotificationCenter.Show("Пакт подписан", $"{AIName}: пакт о ненападении вступил в силу", NotificationCenter.Kind.Success, 6f);
                        }
                        break;
                    case DealItemKind.ResearchTreaty:
                    case DealItemKind.TradeTreaty:
                        if (!HasAgreement(t.Kind))
                        {
                            int months = DealCatalog.TreatyMonths;
                            // Дипломатия корней: договоры с Конклавом держатся дольше
                            if (FactionTraits.IsTerraan(OwnerId) || FactionTraits.IsTerraan(0)) months = Mathf.RoundToInt(months * FactionTraits.TreatyLength);
                            Agreements.Add(new Agreement { Kind = t.Kind, MonthsLeft = months });
                        }
                        break;
                    case DealItemKind.Charts:
                        ShareCharts();
                        break;
                }
            }

            bool generic = d.FromAI.Count + d.FromPlayer.Count > 0
                           || d.Treaties.Exists(t => t.Kind != DealItemKind.Peace && t.Kind != DealItemKind.Pact);
            if (generic) NotificationCenter.Show("Сделка заключена", $"{AIName}: {Summary(d)}", NotificationCenter.Kind.Success, 6f);
            EconomyManager.Instance?.RecalculateAll();
            RecomputeOpinion();
            RaiseDiplomacyChanged();
        }

        private void ShareCharts()
        {
            int toPlayer = 0, toAI = 0;
            foreach (var s in EmpireStats.Systems)
            {
                bool p = s.IsSurveyedBy(0), me = s.IsSurveyedBy(OwnerId);
                if (me && !p) { s.MarkSurveyedBy(0); toPlayer++; }
                if (p && !me) { s.MarkSurveyedBy(OwnerId); toAI++; }
                // Карты открывают и туман войны: всё, что знала одна сторона, теперь знает и другая
                if (Vision.IsExplored(OwnerId, s.Id)) Vision.MarkExplored(0, s.Id);
                if (Vision.IsExplored(0, s.Id)) Vision.MarkExplored(OwnerId, s.Id);
            }
            GalaxyView.Instance?.RefreshTerritoryVisuals();
            NotificationCenter.Show("Обмен картами", $"Получены данные о {toPlayer} сист., передано {toAI}", NotificationCenter.Kind.Info, 6f);
        }

        public static string Summary(Deal d)
        {
            var parts = new List<string>();
            foreach (var t in d.Treaties) parts.Add(DealCatalog.Name(t.Kind).ToLower());
            if (d.FromAI.Count > 0)
            {
                var g = new List<string>();
                foreach (var i in d.FromAI) g.Add(DealCatalog.Describe(i).ToLower());
                parts.Add("вы получаете " + string.Join(", ", g));
            }
            if (d.FromPlayer.Count > 0)
            {
                var g = new List<string>();
                foreach (var i in d.FromPlayer) g.Add(DealCatalog.Describe(i).ToLower());
                parts.Add("вы отдаёте " + string.Join(", ", g));
            }
            return string.Join("; ", parts);
        }

        // ==================== ПРЕДЛОЖЕНИЯ ИИ ====================

        private void SetPendingDeal(Deal d, OfferKind kind, string reason)
        {
            d.ByAI = true;
            PendingDeal = d;
            PendingOffer = kind;
            PendingOfferReason = reason;
            _offerDays = 60;
        }

        /// <summary>Раз в месяц ИИ может сам выйти с предложением: обмен, договор или требование.</summary>
        private void ConsiderTradeOffer()
        {
            if (_tradeOfferCooldown > 0) { _tradeOfferCooldown -= 30; return; }
            if (AtWar || IsEliminated || PendingOffer != OfferKind.None || _offerCooldownDays > 0) return;
            if (Opinion < -45f) return;
            if (Random.value > 0.35f) return;

            var d = TryDemand() ?? TrySwap() ?? TryTreatyOffer();
            if (d == null) return;

            bool demand = d.IsDemand;
            SetPendingDeal(d, demand ? OfferKind.Demand : OfferKind.Trade,
                demand ? "они считают, что сильный вправе требовать" : "они видят в сделке взаимную выгоду");
            _tradeOfferCooldown = Personality == AIPersonality.Trader ? 150 : 240;
            NotificationCenter.Show(demand ? "Требование дани" : "Предложение сделки",
                $"{AIName}: {Summary(d)}. Ответьте в окне дипломатии",
                demand ? NotificationCenter.Kind.Warning : NotificationCenter.Kind.Info, 9f);
            RaiseDiplomacyChanged();
        }

        /// <summary>Воинственная империя при большом перевесе требует дань.</summary>
        private Deal TryDemand()
        {
            if (Personality != AIPersonality.Militarist) return null;
            if (HasPact || PowerRatio < 1.6f || Opinion > -5f || Random.value > 0.5f) return null;
            string res = PlayerStock("alloys") >= 60f ? "alloys" : "energy";
            var kind = res == "alloys" ? DealItemKind.Alloys : DealItemKind.Energy;
            float step = DealCatalog.Step(kind);
            float amt = Mathf.Floor(PlayerStock(res) * 0.25f / step) * step;
            if (amt < step) return null;
            var d = new Deal { IsDemand = true };
            d.FromPlayer.Add(new DealItem(kind, amt));
            return d;
        }

        /// <summary>Обмен избытка на то, чего не хватает.</summary>
        private Deal TrySwap()
        {
            DealItemKind? surplus = null, need = null;
            foreach (var k in DealCatalog.Resources)
            {
                string r = DealCatalog.ResourceKey(k);
                float n = Need(r);
                if (n <= 0.7f && Stock(r) > DealCatalog.Step(k) * 3f && (surplus == null || Stock(r) > Stock(DealCatalog.ResourceKey(surplus.Value)))) surplus = k;
                if (n >= 1.5f && PlayerStock(r) > DealCatalog.Step(k) * 2f && need == null) need = k;
            }
            if (surplus == null || need == null || surplus == need) return null;

            string sr = DealCatalog.ResourceKey(surplus.Value), nr = DealCatalog.ResourceKey(need.Value);
            float giveStep = DealCatalog.Step(surplus.Value), askStep = DealCatalog.Step(need.Value);
            float give = Mathf.Max(giveStep, Mathf.Floor(Stock(sr) * 0.2f / giveStep) * giveStep);
            // По честной базовой цене плюс небольшая наценка в свою пользу
            float ask = give * BaseValue(sr) / BaseValue(nr) * 0.95f;
            ask = Mathf.Clamp(Mathf.Round(ask / askStep) * askStep, askStep, Mathf.Floor(PlayerStock(nr) / askStep) * askStep);
            var d = new Deal();
            d.FromAI.Add(new DealItem(surplus.Value, give));
            d.FromPlayer.Add(new DealItem(need.Value, ask));
            return Evaluate(d).Accept ? d : null;
        }

        private Deal TryTreatyOffer()
        {
            DealItemKind[] order = Personality == AIPersonality.Trader
                ? new[] { DealItemKind.TradeTreaty, DealItemKind.Charts, DealItemKind.ResearchTreaty }
                : Personality == AIPersonality.Scientific
                    ? new[] { DealItemKind.ResearchTreaty, DealItemKind.Charts, DealItemKind.TradeTreaty }
                    : Personality == AIPersonality.Mystic
                        ? new[] { DealItemKind.Charts, DealItemKind.ResearchTreaty }
                    : Personality == AIPersonality.Gardener
                        ? new[] { DealItemKind.ResearchTreaty, DealItemKind.TradeTreaty, DealItemKind.Charts }
                        : new[] { DealItemKind.Charts, DealItemKind.TradeTreaty };
            foreach (var k in order)
            {
                float v = TreatyValue(k, out string block);
                if (block != null || v < 8f) continue;
                var d = new Deal();
                d.Treaties.Add(new DealItem(k, 0f));
                return d;
            }
            return null;
        }

        /// <summary>Принять предложение ИИ (торговое, требование, мир или пакт).</summary>
        public bool AcceptPendingDeal()
        {
            var d = PendingDeal;
            if (d == null) { AcceptOffer(); return true; }
            // Хватает ли ресурсов прямо сейчас
            foreach (var i in d.FromPlayer)
                if (DealCatalog.IsResource(i.Kind) && PlayerStock(DealCatalog.ResourceKey(i.Kind)) < i.Amount)
                {
                    NotificationCenter.Show("Не хватает ресурсов", $"Для сделки нужно {DealCatalog.Describe(i).ToLower()}", NotificationCenter.Kind.Warning, 5f);
                    return false;
                }
            bool demand = d.IsDemand;
            PendingDeal = null;
            PendingOffer = OfferKind.None;
            Execute(d);
            if (demand) AddMemory("incident", 6f);
            else RegisterTrade(2f);
            return true;
        }

        public void DeclinePendingDeal()
        {
            bool demand = PendingDeal != null && PendingDeal.IsDemand;
            PendingDeal = null;
            DeclineOffer();
            if (demand)
            {
                AddMemory("incident", -12f);
                // Отказ сильному соседу приближает войну
                _warCooldownDays = Mathf.Min(_warCooldownDays, 30);
            }
        }

        // ==================== СОХРАНЕНИЕ ====================

        private void CaptureDeals(AISave s)
        {
            s.Agreements = new List<Agreement>(Agreements);
            s.PendingDeal = PendingDeal;
            s.TradeOfferCooldown = _tradeOfferCooldown;
        }

        private void RestoreDeals(AISave s)
        {
            Agreements.Clear();
            if (s.Agreements != null) Agreements.AddRange(s.Agreements);
            PendingDeal = s.PendingDeal != null && !s.PendingDeal.IsEmpty ? s.PendingDeal : null;
            if (PendingDeal != null) PendingDeal.ByAI = true;
            _tradeOfferCooldown = s.TradeOfferCooldown;
        }
    }
}
