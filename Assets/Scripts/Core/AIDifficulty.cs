using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Уровни сложности: не только «бонус к доходам», но и умение ИИ — как часто он думает, планирует ли войны,
    /// охотится ли за флотами, перехватывает ли врага в пути, ошибается ли с целями.
    ///
    ///   0 Новичок      — доходы ×0,6; думает медленно, ошибается с целями, на игрока нападает не раньше 15-го года,
    ///                    к войне готовится 10 месяцев и всегда выдаёт себя; бьётся до последнего (поздно отступает).
    ///   1 Лёгкая       — доходы ×0,8; иногда ошибается, на игрока — не раньше 8-го года, подготовка 8 месяцев.
    ///   2 Нормальная   — честная игра: доходы ×1; подстраивает оружие под вашу защиту, охотится за флотами,
    ///                    не лезет под главный флот врага, готовится к войне полгода, обычно выдаёт себя.
    ///   3 Сложная      — доходы ×1,25; перехват флотов в пути, быстрая подготовка (4 мес.), бьёт, когда вы заняты
    ///                    другой войной, редко выдаёт планы.
    ///   4 Безжалостная — доходы ×1,5; думает чаще всех, 3 месяца на подготовку, почти не выдаёт себя,
    ///                    охотно объединяется против лидера.
    ///
    /// Грубая шкала прежних версий (0 лёгкая · 1 нормальная · 2 сложная) по-прежнему управляет пиратами,
    /// шпионажем и коалициями — она выводится из уровня (NewGameSettings.SetLevel).
    /// </summary>
    public static class AIDifficulty
    {
        public const int Count = 5;

        public static int Tier => GameSession.Settings != null ? GameSession.Settings.Tier : 2;

        private static T Pick<T>(T[] v) => At(v, Tier);
        private static T At<T>(T[] v, int tier) => v[Mathf.Clamp(tier, 0, v.Length - 1)];

        private static readonly float[] IncomeTable = { 0.6f, 0.8f, 1f, 1.25f, 1.5f };
        private static readonly float[] PlayerStartTable = { 1.4f, 1.2f, 1f, 0.9f, 0.8f };
        private static readonly float[] AggressionTable = { 0.5f, 0.7f, 1f, 1.3f, 1.6f };
        public static float IncomeFor(int tier) => At(IncomeTable, tier);
        public static float PlayerStartFor(int tier) => At(PlayerStartTable, tier);
        public static float AggressionFor(int tier) => At(AggressionTable, tier);

        /// <summary>Множитель доходов ИИ.</summary>
        public static float Income => IncomeFor(Tier);
        /// <summary>Темп исследований ИИ (поверх доходов).</summary>
        public static float ResearchPace => Pick(new[] { 0.7f, 0.75f, 0.8f, 0.85f, 0.9f });
        /// <summary>Стартовые ресурсы игрока.</summary>
        public static float PlayerStart => PlayerStartFor(Tier);
        /// <summary>Как быстро ИИ злится на сильного игрока и насколько легко начинает войну.</summary>
        public static float Aggression => AggressionFor(Tier);
        /// <summary>Тактическое умение прежних версий: 0 — базовое, 1 — подбор оружия, 2 — и перехват флотов.</summary>
        public static int Smart => Pick(new[] { 0, 0, 1, 2, 2 });
        /// <summary>Сдвиг отношения ИИ к игроку.</summary>
        public static float OpinionBias => Pick(new[] { 15f, 10f, 0f, -10f, -15f });

        /// <summary>Как часто ИИ принимает решения (дней между «ходами»).</summary>
        public static float ThinkDays => Pick(new[] { Random.Range(7f, 9f), Random.Range(5f, 7f), Random.Range(3f, 5f), Random.Range(2.5f, 4f), Random.Range(2f, 3f) });
        /// <summary>Сколько лет после старта ИИ не объявляет войну никому.</summary>
        public static float PeaceYears => Pick(new[] { 8f, 6f, 4f, 2.5f, 2f });
        /// <summary>С какого года ИИ вообще планирует войну против игрока.</summary>
        public static int PlayerWarMinYear => Pick(new[] { 15, 8, 0, 0, 0 });
        /// <summary>Сколько месяцев ИИ готовится к войне (стягивает флот к границе, достраивает корабли).</summary>
        public static int WarPrepMonths => Pick(new[] { 10, 8, 6, 4, 3 });
        /// <summary>Шанс, что подготовку к войне против игрока заметит разведка.</summary>
        public static float TelegraphChance => Pick(new[] { 1f, 1f, 0.8f, 0.5f, 0.25f });
        /// <summary>Множитель порога отступления: больше — ИИ держится в проигранном бою дольше.</summary>
        public static float RetreatMult => Pick(new[] { 1.5f, 1.25f, 1f, 0.95f, 0.9f });
        /// <summary>Охота за вражескими флотами и обход главного флота врага при выборе цели.</summary>
        public static bool HuntsFleets => Tier >= 2;
        /// <summary>Нужный перевес для атаки на вражеский флот.</summary>
        public static float HuntEdge => Pick(new[] { 9f, 9f, 1.3f, 1.2f, 1.15f });
        /// <summary>Шанс выбрать не лучшую цель (ошибки новичка).</summary>
        public static float MistakeChance => Pick(new[] { 0.45f, 0.25f, 0f, 0f, 0f });
        /// <summary>Насколько ИИ ценит момент, когда игрок уже воюет с кем-то.</summary>
        public static float DogpileMult => Pick(new[] { 1f, 1f, 1.2f, 1.5f, 1.8f });
        /// <summary>Добавка к желанию воевать именно с игроком (на высокой сложности ИИ видит в вас главного соперника).</summary>
        public static float PlayerTargetMult => Pick(new[] { 0.6f, 0.8f, 1f, 1.1f, 1.25f });

        public static string Description(int tier) => Mathf.Clamp(tier, 0, Count - 1) switch
        {
            0 => Loc.T("Для знакомства с жанром: соседи бедные и миролюбивые, ошибаются, о войне предупреждают заранее"),
            1 => Loc.T("Спокойная игра: ИИ слабее вас и иногда ошибается, нападает не раньше 8-го года"),
            2 => Loc.T("Честная игра: равные доходы, ИИ готовится к войнам, охотится за флотами и подбирает оружие против вашей защиты"),
            3 => Loc.T("Для опытных: ИИ богаче, перехватывает флоты в пути, нападает, когда вы заняты другой войной"),
            _ => Loc.T("Вызов: ИИ в полтора раза богаче, думает быстрее, почти не выдаёт планы и охотно объединяется против лидера")
        };
    }
}
