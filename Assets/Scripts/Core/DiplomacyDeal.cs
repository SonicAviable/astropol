using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>Что можно положить на стол переговоров.</summary>
    public enum DealItemKind
    {
        // Разовая передача ресурсов
        Energy, Minerals, Alloys, Influence,
        // Ежемесячные выплаты (DealCatalog.PaymentMonths месяцев)
        MonthlyEnergy, MonthlyMinerals, MonthlyAlloys,
        // Договоры — взаимные, лежат сразу на обеих сторонах стола
        Charts, ResearchTreaty, TradeTreaty, Pact, Peace
    }

    [Serializable]
    public class DealItem
    {
        public DealItemKind Kind;
        public float Amount;

        public DealItem() { }
        public DealItem(DealItemKind kind, float amount) { Kind = kind; Amount = amount; }
        public DealItem Clone() => new DealItem(Kind, Amount);
    }

    /// <summary>
    /// Сделка: что отдаёт ИИ, что отдаёт игрок и какие договоры заключаются (взаимно).
    /// Одна и та же структура и для предложений игрока, и для предложений ИИ.
    /// </summary>
    [Serializable]
    public class Deal
    {
        public List<DealItem> FromAI = new List<DealItem>();
        public List<DealItem> FromPlayer = new List<DealItem>();
        public List<DealItem> Treaties = new List<DealItem>();
        /// <summary>Сделку предложил ИИ (ультиматум — когда игрок только платит).</summary>
        public bool ByAI;
        public bool IsDemand;

        public bool IsEmpty => FromAI.Count == 0 && FromPlayer.Count == 0 && Treaties.Count == 0;
        public bool IsGift => FromAI.Count == 0 && Treaties.Count == 0 && FromPlayer.Count > 0;

        public bool HasTreaty(DealItemKind k) => Treaties.Exists(t => t.Kind == k);

        public List<DealItem> Side(bool fromAI) => fromAI ? FromAI : FromPlayer;

        public DealItem Find(bool fromAI, DealItemKind k) => Side(fromAI).Find(i => i.Kind == k);

        public Deal Clone()
        {
            var d = new Deal { ByAI = ByAI, IsDemand = IsDemand };
            foreach (var i in FromAI) d.FromAI.Add(i.Clone());
            foreach (var i in FromPlayer) d.FromPlayer.Add(i.Clone());
            foreach (var i in Treaties) d.Treaties.Add(i.Clone());
            return d;
        }

        public void Clear()
        {
            FromAI.Clear(); FromPlayer.Clear(); Treaties.Clear();
            ByAI = false; IsDemand = false;
        }
    }

    /// <summary>Действующее соглашение: выплаты или договор со сроком.</summary>
    [Serializable]
    public class Agreement
    {
        public DealItemKind Kind;
        public float Amount;
        public int MonthsLeft;
        /// <summary>Для выплат: платит игрок (иначе платит ИИ).</summary>
        public bool PlayerPays;
    }

    /// <summary>Названия, иконки, шаги и сроки предметов торга.</summary>
    public static class DealCatalog
    {
        public const int PaymentMonths = 60;     // ежемесячные выплаты — 5 лет
        public const int TreatyMonths = 120;     // научный и торговый договор — 10 лет
        public const float ResearchTreatyBonus = 0.10f;
        public const float TradeTreatyEnergy = 6f;

        public static readonly DealItemKind[] Resources =
            { DealItemKind.Energy, DealItemKind.Minerals, DealItemKind.Alloys, DealItemKind.Influence };
        public static readonly DealItemKind[] Payments =
            { DealItemKind.MonthlyEnergy, DealItemKind.MonthlyMinerals, DealItemKind.MonthlyAlloys };
        public static readonly DealItemKind[] TreatyKinds =
            { DealItemKind.Peace, DealItemKind.Pact, DealItemKind.ResearchTreaty, DealItemKind.TradeTreaty, DealItemKind.Charts };

        public static bool IsTreaty(DealItemKind k) => k >= DealItemKind.Charts;
        public static bool IsPayment(DealItemKind k) => k >= DealItemKind.MonthlyEnergy && k <= DealItemKind.MonthlyAlloys;
        public static bool IsResource(DealItemKind k) => k <= DealItemKind.Influence;

        /// <summary>Ресурс, к которому относится предмет (energy/minerals/alloys/influence).</summary>
        public static string ResourceKey(DealItemKind k) => k switch
        {
            DealItemKind.Energy or DealItemKind.MonthlyEnergy => "energy",
            DealItemKind.Minerals or DealItemKind.MonthlyMinerals => "minerals",
            DealItemKind.Alloys or DealItemKind.MonthlyAlloys => "alloys",
            DealItemKind.Influence => "influence",
            _ => null
        };

        public static string Name(DealItemKind k) => k switch
        {
            DealItemKind.Energy => "Гелий-3",
            DealItemKind.Minerals => "Титан",
            DealItemKind.Alloys => "Сплавы",
            DealItemKind.Influence => "Влияние",
            DealItemKind.MonthlyEnergy => "Гелий-3 ежемесячно",
            DealItemKind.MonthlyMinerals => "Титан ежемесячно",
            DealItemKind.MonthlyAlloys => "Сплавы ежемесячно",
            DealItemKind.Charts => "Обмен звёздными картами",
            DealItemKind.ResearchTreaty => "Научный договор",
            DealItemKind.TradeTreaty => "Торговое соглашение",
            DealItemKind.Pact => "Пакт о ненападении",
            DealItemKind.Peace => "Мирный договор",
            _ => "?"
        };

        public static string Hint(DealItemKind k) => k switch
        {
            DealItemKind.Energy => "Разовая передача гелия-3",
            DealItemKind.Minerals => "Разовая передача титана",
            DealItemKind.Alloys => "Разовая передача сплавов",
            DealItemKind.Influence => "Разовая передача влияния",
            DealItemKind.MonthlyEnergy or DealItemKind.MonthlyMinerals or DealItemKind.MonthlyAlloys
                => $"Выплата каждый месяц в течение {PaymentMonths / 12} лет. Война отменяет выплаты",
            DealItemKind.Charts => "Обе стороны получают разведданные о системах, изученных другой стороной",
            DealItemKind.ResearchTreaty => $"+{ResearchTreatyBonus * 100f:0}% к науке обеим сторонам на {TreatyMonths / 12} лет",
            DealItemKind.TradeTreaty => $"+{TradeTreatyEnergy:0} гелия-3 в месяц обеим сторонам на {TreatyMonths / 12} лет",
            DealItemKind.Pact => "Стороны обязуются не нападать друг на друга. Нарушение — тяжёлое вероломство",
            DealItemKind.Peace => "Прекращение войны и перемирие на 3 года",
            _ => ""
        };

        public static LGIcon Icon(DealItemKind k) => k switch
        {
            DealItemKind.Energy or DealItemKind.MonthlyEnergy => LGIcon.Energy,
            DealItemKind.Minerals or DealItemKind.MonthlyMinerals => LGIcon.Minerals,
            DealItemKind.Alloys or DealItemKind.MonthlyAlloys => LGIcon.Alloys,
            DealItemKind.Influence => LGIcon.Influence,
            DealItemKind.Charts => LGIcon.MapExplored,
            DealItemKind.ResearchTreaty => LGIcon.Research,
            DealItemKind.TradeTreaty => LGIcon.Trade,
            DealItemKind.Pact => LGIcon.Handshake,
            DealItemKind.Peace => LGIcon.Peace,
            _ => LGIcon.Info
        };

        public static Color Tint(DealItemKind k) => k switch
        {
            DealItemKind.Energy or DealItemKind.MonthlyEnergy => new Color(1f, 0.80f, 0.32f),
            DealItemKind.Minerals or DealItemKind.MonthlyMinerals => new Color(0.55f, 0.80f, 1f),
            DealItemKind.Alloys or DealItemKind.MonthlyAlloys => new Color(0.13f, 0.92f, 0.69f),
            DealItemKind.Influence => new Color(0.85f, 0.55f, 1f),
            DealItemKind.Peace => new Color(0.44f, 0.88f, 0.60f),
            DealItemKind.Pact => new Color(0.40f, 0.86f, 0.82f),
            DealItemKind.ResearchTreaty => new Color(0.42f, 0.66f, 1f),
            DealItemKind.TradeTreaty => new Color(0.95f, 0.78f, 0.40f),
            _ => new Color(0.75f, 0.85f, 0.92f)
        };

        /// <summary>Сколько кладётся по щелчку и каким шагом меняется.</summary>
        public static float Step(DealItemKind k) => k switch
        {
            DealItemKind.Energy => 50f,
            DealItemKind.Minerals => 50f,
            DealItemKind.Alloys => 25f,
            DealItemKind.Influence => 10f,
            DealItemKind.MonthlyEnergy => 3f,
            DealItemKind.MonthlyMinerals => 3f,
            DealItemKind.MonthlyAlloys => 2f,
            _ => 0f
        };

        public static string Describe(DealItem i)
        {
            if (IsTreaty(i.Kind)) return Name(i.Kind);
            if (IsPayment(i.Kind)) return $"{Name(ResourceBase(i.Kind))}: {i.Amount:0} / мес.";
            return $"{Name(i.Kind)}: {i.Amount:0}";
        }

        public static DealItemKind ResourceBase(DealItemKind k) => k switch
        {
            DealItemKind.MonthlyEnergy => DealItemKind.Energy,
            DealItemKind.MonthlyMinerals => DealItemKind.Minerals,
            DealItemKind.MonthlyAlloys => DealItemKind.Alloys,
            _ => k
        };

        /// <summary>Остаток срока по-человечески: «4 г. 2 мес.».</summary>
        public static string Months(int m)
        {
            if (m < 12) return $"{Mathf.Max(0, m)} мес.";
            int y = m / 12, r = m % 12;
            return r == 0 ? $"{y} г." : $"{y} г. {r} мес.";
        }
    }
}
