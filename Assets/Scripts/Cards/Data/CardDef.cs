using System;

namespace StellarisClone.Cards
{
    /// <summary>Тип карты: юнит, приказ (одноразовый), сооружение обороны, ловушка, герой-лидер.</summary>
    public enum CardKind { Unit, Order, Structure, Trap, Hero }

    /// <summary>Где карту производят: город (пехота), промышленный район (техника), штаб (приказы за гелий-3 и влияние), гарнизон колонии.</summary>
    public enum ProdSite { None, City, Industry, Command, Garrison }

    /// <summary>Ключевые слова карт (см. правила в BattleState).</summary>
    [Flags]
    public enum Kw
    {
        None = 0,
        Pierce = 1 << 0,      // Бронебой: по технике и сооружениям ×2
        Indirect = 1 << 1,    // Навесной: бьёт и соседний сектор половиной силы
        Crush = 1 << 2,       // Давит: при раскрытии уничтожает до двух пехотинцев врага с бронёй ≤ 2
        Swarm = 1 << 3,       // Рой: сила не больше оставшейся брони (теряет дронов)
        Sniper = 1 << 4,      // Прицельно: бьёт самого хрупкого, по пехоте ×2
        Flame = 1 << 5,       // Огонь: игнорирует укрытие, по сооружениям ×2
        Fragile = 1 << 6,     // Погибает в конце хода
        Vehicle = 1 << 7,     // Техника
        Infantry = 1 << 8,    // Пехота
        Militia = 1 << 9,     // Ополчение (жители колонии)
        Drone = 1 << 10,      // Дрон (для карт «Ноля»)
        NeedsPower = 1 << 11, // Работает, пока электростанция у защитника
        Shield = 1 << 12,     // Щит Штаба
        Merc = 1 << 13,       // Наёмники: покупаются прямо в бою
    }

    /// <summary>
    /// Описание карты (одно на вид карты). Конкретные экземпляры — <see cref="BCard"/> в бою и CardInstance в резерве империи.
    /// Эффекты — делегаты над состоянием боя: правила остаются в одном месте (BattleState), карта лишь вызывает их.
    /// </summary>
    public sealed class CardDef
    {
        public string Id;
        /// <summary>Ключ фракции (astrea, xarn, aquila, iridia, terraan, nol); null — общая карта.</summary>
        public string Faction;
        public string Name, Text;
        public CardKind Kind;
        public int Cost, Power, Armor;
        /// <summary>Укрытие: столько урона за ход карта игнорирует.</summary>
        public int Cover;
        /// <summary>Залп: столько урона случайному врагу сектора при раскрытии.</summary>
        public int Salvo;
        public Kw Kw;

        // ---- Империя: производство, перевозка, содержание
        /// <summary>Десантных мест во флоте (приказы — 0).</summary>
        public int Size = 1;
        public ProdSite Site;
        public float Minerals, Alloys, Energy, Influence;
        public int Days;
        /// <summary>Гелий-3 в месяц за карту в резерве.</summary>
        public float Upkeep;
        /// <summary>Нельзя произвести обычным путём (ополчение, экипаж, щит, герой) — появляется само.</summary>
        public bool Virtual;
        /// <summary>Как открыть карту (для подсказки); null — доступна сразу.</summary>
        public string Unlock;

        // ---- Бой
        public Action<BattleState, BUnit> OnReveal, OnEndTurn, OnDeath;
        /// <summary>Перед каждой перестрелкой (ауры на этот ход: укрытие, бонус дронам).</summary>
        public Action<BattleState, BUnit> OnPreCombat;
        /// <summary>Приказ: эффект на сектор, куда его сыграли.</summary>
        public Action<BattleState, int, int> OnOrder;
        /// <summary>Ловушка: срабатывает на первом раскрытом враге в секторе.</summary>
        public Action<BattleState, BUnit, BUnit> OnTrap;

        public bool Has(Kw k) => (Kw & k) != 0;
        public bool IsBoardUnit => Kind == CardKind.Unit || Kind == CardKind.Hero || Kind == CardKind.Structure;
        public bool Consumable => Kind == CardKind.Order || Kind == CardKind.Trap;

        /// <summary>Тег типа для рамки и подписи («ЮНИТ», «ПРИКАЗ»…).</summary>
        public string KindName => CardText.KindName(Kind);
    }

    /// <summary>Подписи, не зависящие от Unity (тексты — через Loc.T).</summary>
    public static class CardText
    {
        public static string KindName(CardKind k)
        {
            switch (k)
            {
                case CardKind.Order: return StellarisClone.Core.Loc.T("ПРИКАЗ");
                case CardKind.Structure: return StellarisClone.Core.Loc.T("СООРУЖЕНИЕ");
                case CardKind.Trap: return StellarisClone.Core.Loc.T("ЛОВУШКА");
                case CardKind.Hero: return StellarisClone.Core.Loc.T("ГЕРОЙ");
                default: return StellarisClone.Core.Loc.T("ЮНИТ");
            }
        }
    }
}
