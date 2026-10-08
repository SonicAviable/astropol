using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Утилиты для форматирования чисел и модификаторов с авто-окраской:
    ///   положительные → зелёный
    ///   отрицательные → красный
    ///   ноль          → приглушённый
    /// Используется во всех UI, где показываются числовые модификаторы.
    /// </summary>
    public static class StatFormat
    {
        public const string GreenHex = "#4DF08C";
        public const string RedHex   = "#FF5555";
        public const string GoldHex  = "#F2C747";
        public const string CyanHex  = "#33E6CC";
        public const string MutedHex = "#8AA2A8";

        // ==================== ОКРАСКА СТРОК ====================

        public static string Positive(string s) => $"<color={GreenHex}>{s}</color>";
        public static string Negative(string s) => $"<color={RedHex}>{s}</color>";
        public static string Neutral(string s)  => $"<color={MutedHex}>{s}</color>";
        public static string Gold(string s)     => $"<color={GoldHex}>{s}</color>";
        public static string Cyan(string s)     => $"<color={CyanHex}>{s}</color>";

        /// <summary>Авто-окраска по знаку числа. Ноль — приглушённый.</summary>
        public static string Colored(float value, string format = "+0;-0;0")
        {
            // Вызовы вида Colored(x, "%/мес") попадают сюда (точное совпадение перегрузки):
            // строка без цифровых плейсхолдеров — это суффикс, а не формат числа
            if (string.IsNullOrEmpty(format) || (format.IndexOf('0') < 0 && format.IndexOf('#') < 0))
                return Colored(value, format ?? "", "+0;-0;0");
            string text = value.ToString(format);
            if (value > 0.001f) return Positive(text);
            if (value < -0.001f) return Negative(text);
            return Neutral(text);
        }

        /// <summary>Авто-окраска с суффиксом (%, /мес и т.п.).</summary>
        public static string Colored(float value, string suffix, string format = "+0;-0;0")
        {
            string text = value.ToString(format) + suffix;
            if (value > 0.001f) return Positive(text);
            if (value < -0.001f) return Negative(text);
            return Neutral(text);
        }

        /// <summary>Процентное изменение — «+15%» зелёным, «−10%» красным.</summary>
        public static string Percent(float multiplierDelta)
        {
            // multiplierDelta: 1.15 = +15%, 0.9 = -10%, 1.0 = 0%
            float pct = (multiplierDelta - 1f) * 100f;
            string text = pct.ToString("+0.#;-0.#;0") + "%";
            if (pct > 0.05f) return Positive(text);
            if (pct < -0.05f) return Negative(text);
            return Neutral(text);
        }

        // ==================== ГОТОВЫЕ СТРОКИ ====================

        /// <summary>Строка «Название   +15» с цветом значения по знаку.</summary>
        public static string Line(string label, float value, string suffix = "", string format = "+0;-0;0")
        {
            return $"{Neutral(label)}  {Colored(value, suffix, format)}";
        }

        /// <summary>Строка модификатора: «+15% урона» или «−10% щитов».</summary>
        public static string Modifier(float value, string suffix, string statName)
        {
            string sign = value >= 0 ? "+" : "−";
            string abs = Mathf.Abs(value).ToString("0.#");
            string text = $"{sign}{abs}{suffix}  {statName}";
            if (value > 0.001f) return Positive(text);
            if (value < -0.001f) return Negative(text);
            return Neutral(text);
        }

        /// <summary>Модификатор через множитель: 1.2 → «+20% к добыче», 0.85 → «−15% стоимости».</summary>
        public static string Multiplier(float mult, string statName)
        {
            return Modifier((mult - 1f) * 100f, "%", statName);
        }

        /// <summary>Число с принудительным знаком и цветом для конкретного показателя.</summary>
        public static string Stock(int value, bool emphasizeColor = false)
        {
            string text = value.ToString("N0");
            return emphasizeColor ? Cyan(text) : text;
        }

        /// <summary>Знаковый процент для UI-текста (например, «+8%/мес»).</summary>
        public static string Income(float value, string suffix = "/мес")
        {
            string text = value.ToString("+0.0;-0.0;0.0") + suffix;
            if (value > 0.01f) return Positive(text);
            if (value < -0.01f) return Negative(text);
            return Neutral(text);
        }

        /// <summary>Обёртка для «жильё: +N / -N» с автозакраской.</summary>
        public static string Housing(int freeHousing)
        {
            string text = freeHousing >= 0 ? $"+{freeHousing}" : freeHousing.ToString();
            return freeHousing >= 0 ? Positive(text) : Negative(text);
        }

        /// <summary>Обёртка для уведомлений типа «достаточно / не хватает».</summary>
        public static string Availability(bool ok, string okText, string failText)
        {
            return ok ? Positive(okText) : Negative(failText);
        }
    }
}