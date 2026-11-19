using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Случайные названия научных и строительных кораблей в стиле фракции владельца.
    /// Префикс и наборы имён свои у каждой фракции; повторы среди живых кораблей не выдаются
    /// (когда набор исчерпан — добавляется порядковый номер: «Колос II»).
    /// </summary>
    public static class ShipNames
    {
        private sealed class Style
        {
            public string SciPrefix, ConPrefix;
            public string[] Sci, Con;
        }

        private static readonly Dictionary<string, Style> Styles = new Dictionary<string, Style>
        {
            // Республика Астрея — учёные, первопроходцы, идеалы свободы
            ["astrea"] = new Style
            {
                SciPrefix = "НИС", ConPrefix = "СК",
                Sci = new[] { "Первопроходец", "Коперник", "Гипатия", "Любопытство", "Горизонт", "Кеплер", "Открытие", "Вояджер", "Ломоносов", "Новая Заря" },
                Con = new[] { "Фундамент", "Согласие", "Опора", "Содружество", "Мост", "Каменщик", "Новый Дом", "Свободный Причал", "Гавань", "Единство" }
            },
            // Доминион Ксарн — военная иерархия, культ силы
            ["xarn"] = new Style
            {
                SciPrefix = "РК", ConPrefix = "ИК",
                Sci = new[] { "Око Доминиона", "Следопыт", "Клинок Знания", "Дозорный", "Ищейка", "Холодный Взор", "Предвестник", "Разведчик Кхар", "Шпора", "Бдительный" },
                Con = new[] { "Цитадель", "Молот", "Бастион", "Наковальня", "Твердыня", "Железный Шаг", "Кузня Ксарн", "Оплот", "Плацдарм", "Тяжкий Труд" }
            },
            // Синдикат Аквила — корпорация, прибыль и контракты
            ["aquila"] = new Style
            {
                SciPrefix = "Зонд", ConPrefix = "Подрядчик",
                Sci = new[] { "Аудит", "Котировка", "Due Diligence", "Маркетолог", "Прогноз Рынка", "Инвестор", "Венчур", "Актив", "Оценщик", "Дивиденд" },
                Con = new[] { "Генподряд", "Франшиза", "Филиал", "Холдинг", "Слияние", "Концессия", "Монополия", "Сделка Века", "Лицензия", "Капитал" }
            },
            // Иридийский Оракул — провидцы, нити будущего
            ["iridia"] = new Style
            {
                SciPrefix = "Взор", ConPrefix = "Обитель",
                Sci = new[] { "Нить Судьбы", "Провидица", "Сияние", "Видение", "Звёздный Шёпот", "Третий Глаз", "Предзнаменование", "Прорицание", "Мерцание", "Сон Оракула" },
                Con = new[] { "Святилище", "Храм Зари", "Обет", "Тихая Келья", "Алтарь", "Светоч", "Благодать", "Купол Безмолвия", "Обитель Знамений", "Пророчество" }
            },
            // Тэрра'ан — живые корабли из биокоры
            ["terraan"] = new Style
            {
                SciPrefix = "Спора", ConPrefix = "Семя",
                Sci = new[] { "Пыльца", "Усик", "Росток Ветра", "Светолист", "Блуждающий Корень", "Мицелий", "Шёпот Листвы", "Побег", "Почка", "Летучее Семя" },
                Con = new[] { "Корень", "Древо Жизни", "Перегной", "Грибница", "Колыбель", "Старая Кора", "Лоза", "Плодородие", "Роща", "Сердцевина" }
            },
            // Консенсус Ноль — машинный разум, обозначения вместо имён
            ["nol"] = new Style
            {
                SciPrefix = "Узел", ConPrefix = "Сборщик",
                Sci = new[] { "Сканер-7", "Зонд Δ-12", "Анализ-3", "Сенсор-0x1F", "Наблюдатель-9", "Индекс-44", "Выборка-5", "Ω-Разведка", "Протокол-21", "Датчик-88" },
                Con = new[] { "Монтаж-1", "Сборка-Σ", "Каркас-6", "Конструктор-30", "Матрица-2", "Узел Связи-7", "Репликатор-4", "Модуль-0xA", "Сегмент-11", "Опора-99" }
            },
        };

        // Неизвестная или своя фракция — нейтральный набор
        private static readonly Style Default = new Style
        {
            SciPrefix = "НИС", ConPrefix = "СК",
            Sci = new[] { "Пионер", "Странник", "Компас", "Звездочёт", "Искатель", "Астролябия", "Маяк", "Дальний Путь", "Секстант", "Ориентир" },
            Con = new[] { "Строитель", "Зодчий", "Кладка", "Причал", "Опорный Пункт", "Каркас", "Форпост", "Монолит", "Арка", "Свод" }
        };

        private static readonly string[] Roman = { "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        /// <summary>Случайное незанятое название гражданского корабля для империи owner.</summary>
        public static string Civilian(FleetType type, int owner)
        {
            string key = FactionTraits.Key(owner);
            var st = key != null && Styles.TryGetValue(key, out var s) ? s : Default;
            bool sci = type == FleetType.Science;
            string prefix = sci ? st.SciPrefix : st.ConPrefix;
            string[] pool = sci ? st.Sci : st.Con;

            var used = new HashSet<string>();
            var fm = FleetManager.Instance;
            if (fm != null)
                foreach (var f in fm.AllFleets)
                    if (f != null && f.Data != null && f.Data.Name != null) used.Add(f.Data.Name);

            // Свободные имена из набора — в случайном порядке
            var free = new List<string>();
            foreach (var n in pool)
            {
                string full = $"{prefix} «{n}»";
                if (!used.Contains(full)) free.Add(full);
            }
            if (free.Count > 0) return free[Random.Range(0, free.Count)];

            // Набор исчерпан — то же имя с порядковым номером
            string baseName = pool[Random.Range(0, pool.Length)];
            foreach (var r in Roman)
            {
                string full = $"{prefix} «{baseName} {r}»";
                if (!used.Contains(full)) return full;
            }
            return $"{prefix} «{baseName} {Random.Range(11, 100)}»";
        }
    }
}
