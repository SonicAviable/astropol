using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Generation
{
    /// <summary>
    /// Реалистичные названия звёздных систем вместо «Система-N». Смесь, как на настоящих картах неба:
    ///   • собственные имена ярких звёзд (Альтаир, Ригель, Фомальгаут…)
    ///   • обозначения Байера — греческая буква + созвездие (Тау Кита, Эпсилон Эридана…)
    ///   • каталожные номера (Глизе 581, Кеплер-186, Вольф 359…)
    ///   • созвучные выдуманные имена из слогов (Ксенгара, Велорис…) — для разнообразия на больших картах
    /// Все имена в одной галактике уникальны. Использует UnityEngine.Random — та же генерация, что и у карты.
    /// </summary>
    public class StarNameGenerator
    {
        private static readonly string[] Proper =
        {
            Loc.T("Альтаир"), Loc.T("Вега"), Loc.T("Денеб"), Loc.T("Ригель"), Loc.T("Бетельгейзе"), Loc.T("Альдебаран"), Loc.T("Антарес"), Loc.T("Капелла"), Loc.T("Процион"),
            Loc.T("Арктур"), Loc.T("Канопус"), Loc.T("Спика"), Loc.T("Регул"), Loc.T("Поллукс"), Loc.T("Кастор"), Loc.T("Фомальгаут"), Loc.T("Мира"), Loc.T("Алголь"), Loc.T("Мирфак"),
            Loc.T("Хамаль"), Loc.T("Шедар"), Loc.T("Ахернар"), Loc.T("Менкар"), Loc.T("Алькор"), Loc.T("Мицар"), Loc.T("Дубхе"), Loc.T("Мерак"), Loc.T("Фекда"), Loc.T("Мегрец"), Loc.T("Алиот"),
            Loc.T("Бенетнаш"), Loc.T("Тубан"), Loc.T("Кохаб"), Loc.T("Эльнат"), Loc.T("Беллатрикс"), Loc.T("Минтака"), Loc.T("Альнилам"), Loc.T("Альнитак"), Loc.T("Саиф"),
            Loc.T("Акрукс"), Loc.T("Гакрукс"), Loc.T("Хадар"), Loc.T("Толиман"), Loc.T("Шаула"), Loc.T("Сабик"), Loc.T("Расальхаге"), Loc.T("Альфард"), Loc.T("Денебола"),
            Loc.T("Зосма"), Loc.T("Альгиеба"), Loc.T("Альхена"), Loc.T("Унук"), Loc.T("Альбирео"), Loc.T("Садр"), Loc.T("Гиенах"), Loc.T("Алькаид"), Loc.T("Менкалинан"),
            Loc.T("Мирах"), Loc.T("Альферац"), Loc.T("Шеат"), Loc.T("Маркаб"), Loc.T("Эниф"), Loc.T("Садалмелик"), Loc.T("Садалсууд"), Loc.T("Скат"), Loc.T("Нашира"), Loc.T("Денеб Альгеди"),
            Loc.T("Ахелоя"), Loc.T("Ассела"), Loc.T("Каус Аустралис"), Loc.T("Нунки"), Loc.T("Альнаир"), Loc.T("Пикок"), Loc.T("Атрия"), Loc.T("Суаалоцин"), Loc.T("Ротанев"),
            Loc.T("Векс"), Loc.T("Тарацед"), Loc.T("Альшаин"), Loc.T("Сарир"), Loc.T("Лесат"), Loc.T("Акраб"), Loc.T("Дшубба"), Loc.T("Зубен"), Loc.T("Брахиум"), Loc.T("Поррима"),
            Loc.T("Виндемиатрикс"), Loc.T("Альхайот"), Loc.T("Сирма"), Loc.T("Аркаб"), Loc.T("Альциона"), Loc.T("Электра"), Loc.T("Майя"), Loc.T("Меропа"), Loc.T("Тайгета"),
            Loc.T("Атлас"), Loc.T("Плейона"), Loc.T("Астеропа"), Loc.T("Талита"), Loc.T("Алула"), Loc.T("Мускида"), Loc.T("Нихал"), Loc.T("Арнеб"), Loc.T("Фуруд"), Loc.T("Везен"),
            Loc.T("Мирзам"), Loc.T("Адара"), Loc.T("Альудра"), Loc.T("Гомейза"), Loc.T("Асмидиске"), Loc.T("Наос"), Loc.T("Регор"), Loc.T("Авиор"), Loc.T("Миаплацидус"),
            Loc.T("Аспидиске"), Loc.T("Сухаиль"), Loc.T("Ацубенс"), Loc.T("Тегмин"), Loc.T("Прэсепа"), Loc.T("Альтаис"), Loc.T("Растабан"), Loc.T("Эльтанин"), Loc.T("Гиаусар"),
            Loc.T("Кейд"), Loc.T("Ахирд"), Loc.T("Каф"), Loc.T("Рукбах"), Loc.T("Сегин"), Loc.T("Альмак"), Loc.T("Мирфак"), Loc.T("Атик"), Loc.T("Менкиб"), Loc.T("Капх"), Loc.T("Хассалех")
        };

        private static readonly string[] Greek =
        {
            Loc.T("Альфа"), Loc.T("Бета"), Loc.T("Гамма"), Loc.T("Дельта"), Loc.T("Эпсилон"), Loc.T("Дзета"), Loc.T("Эта"), Loc.T("Тета"), Loc.T("Йота"), Loc.T("Каппа"),
            Loc.T("Лямбда"), Loc.T("Мю"), Loc.T("Ню"), Loc.T("Кси"), Loc.T("Омикрон"), Loc.T("Пи"), Loc.T("Ро"), Loc.T("Сигма"), Loc.T("Тау"), Loc.T("Ипсилон"), Loc.T("Фи"), Loc.T("Хи"), Loc.T("Пси"), Loc.T("Омега")
        };

        // Созвездия в родительном падеже
        private static readonly string[] Constellations =
        {
            Loc.T("Кита"), Loc.T("Эридана"), Loc.T("Лебедя"), Loc.T("Ориона"), Loc.T("Персея"), Loc.T("Лиры"), Loc.T("Орла"), Loc.T("Кассиопеи"), Loc.T("Андромеды"), Loc.T("Пегаса"),
            Loc.T("Дракона"), Loc.T("Волопаса"), Loc.T("Девы"), Loc.T("Скорпиона"), Loc.T("Стрельца"), Loc.T("Козерога"), Loc.T("Водолея"), Loc.T("Рыб"), Loc.T("Овна"), Loc.T("Тельца"),
            Loc.T("Близнецов"), Loc.T("Рака"), Loc.T("Льва"), Loc.T("Весов"), Loc.T("Змееносца"), Loc.T("Геркулеса"), Loc.T("Северной Короны"), Loc.T("Южного Креста"),
            Loc.T("Центавра"), Loc.T("Киля"), Loc.T("Парусов"), Loc.T("Кормы"), Loc.T("Большого Пса"), Loc.T("Малого Пса"), Loc.T("Единорога"), Loc.T("Гидры"), Loc.T("Ворона"),
            Loc.T("Чаши"), Loc.T("Секстанта"), Loc.T("Тукана"), Loc.T("Феникса"), Loc.T("Павлина"), Loc.T("Журавля"), Loc.T("Индейца"), Loc.T("Цефея"), Loc.T("Жирафа"), Loc.T("Рыси"),
            Loc.T("Возничего"), Loc.T("Голубя"), Loc.T("Зайца"), Loc.T("Печи"), Loc.T("Скульптора"), Loc.T("Часов"), Loc.T("Сетки"), Loc.T("Золотой Рыбы"), Loc.T("Хамелеона"),
            Loc.T("Райской Птицы"), Loc.T("Южной Гидры"), Loc.T("Октанта"), Loc.T("Столовой Горы"), Loc.T("Летучей Рыбы"), Loc.T("Живописца"), Loc.T("Резца")
        };

        private static readonly string[] Catalog = { Loc.T("Глизе"), Loc.T("Кеплер-"), Loc.T("Вольф"), Loc.T("Росс"), Loc.T("Лейтен"), Loc.T("Лаланд"), Loc.T("Струве"), Loc.T("Грумбридж"), "HD", "HIP", Loc.T("Козак"), Loc.T("Ван Маанен"), Loc.T("Тиго") };

        private static readonly string[] SylStart =
        {
            Loc.T("Ксе"), Loc.T("Ва"), Loc.T("Ко"), Loc.T("Су"), Loc.T("Та"), Loc.T("Ве"), Loc.T("Ка"), Loc.T("Ли"), Loc.T("Да"), Loc.T("Ме"), Loc.T("Ну"), Loc.T("Зе"), Loc.T("Ор"), Loc.T("Иш"), Loc.T("Эс"), Loc.T("Ал"), Loc.T("Ар"),
            Loc.T("Ин"), Loc.T("Ти"), Loc.T("Фе"), Loc.T("Хи"), Loc.T("Ке"), Loc.T("Са"), Loc.T("Ду"), Loc.T("Па"), Loc.T("Ге"), Loc.T("Мо"), Loc.T("Ра"), Loc.T("Се"), Loc.T("Ул"), Loc.T("Ан"), Loc.T("Эл"), Loc.T("Ир"), Loc.T("Ба")
        };
        private static readonly string[] SylMid =
        {
            Loc.T("нга"), Loc.T("би"), Loc.T("рбо"), Loc.T("льку"), Loc.T("те"), Loc.T("ло"), Loc.T("ри"), Loc.T("ссе"), Loc.T("кра"), Loc.T("ва"), Loc.T("ми"), Loc.T("нде"), Loc.T("ла"), Loc.T("та"), Loc.T("рго"),
            Loc.T("зи"), Loc.T("ла"), Loc.T("фе"), Loc.T("кса"), Loc.T("до"), Loc.T("лви"), Loc.T("ре"), Loc.T("ну"), Loc.T("га"), Loc.T("ри"), Loc.T("тха"), Loc.T("ле"), Loc.T("ма")
        };
        private static readonly string[] SylEnd =
        {
            Loc.T("ра"), Loc.T("р"), Loc.T("с"), Loc.T("н"), Loc.T("кс"), Loc.T("ан"), Loc.T("ис"), Loc.T("ор"), Loc.T("ия"), Loc.T("ус"), Loc.T("ах"), Loc.T("ет"), Loc.T("он"), Loc.T("ея"), Loc.T("ар"), Loc.T("ин"), Loc.T("ос"),
            Loc.T("уш"), Loc.T("ит"), Loc.T("ала"), Loc.T("ира"), Loc.T("енс"), Loc.T("ант"), Loc.T("аль")
        };

        private readonly HashSet<string> _used = new HashSet<string>();
        private readonly List<string> _properPool = new List<string>(Proper);

        public string Next()
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                string name;
                float roll = Random.value;
                if (roll < 0.40f && _properPool.Count > 0)
                {
                    int i = Random.Range(0, _properPool.Count);
                    name = _properPool[i];
                    _properPool.RemoveAt(i);
                }
                else if (roll < 0.62f)
                    name = $"{Greek[Random.Range(0, Greek.Length)]} {Constellations[Random.Range(0, Constellations.Length)]}";
                else if (roll < 0.74f)
                    name = CatalogName();
                else
                    name = Invented();

                name = Clean(name);
                if (name.Length > 0 && _used.Add(name)) return name;
            }
            // на всякий случай — гарантированно уникальное
            string fallback;
            do fallback = CatalogName(); while (!_used.Add(fallback));
            return fallback;
        }

        private static string CatalogName()
        {
            string c = Catalog[Random.Range(0, Catalog.Length)];
            int n = c switch
            {
                "HD" => Random.Range(1000, 240000),
                "HIP" => Random.Range(1000, 120000),
                _ when c.EndsWith("-") => Random.Range(10, 2000),   // Кеплер-
                _ => Random.Range(2, 1200)
            };
            return c.EndsWith("-") ? c + n : $"{c} {n}";
        }

        private static string Invented()
        {
            string s = SylStart[Random.Range(0, SylStart.Length)];
            if (Random.value < 0.7f) s += SylMid[Random.Range(0, SylMid.Length)];
            if (Random.value < 0.6f) s += SylEnd[Random.Range(0, SylEnd.Length)];
            return s;
        }

        /// <summary>Отсекает случайные символы не из кириллицы/латиницы (опечатки в словаре не попадут в игру).</summary>
        private static string Clean(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s)
                if (char.IsLetterOrDigit(ch) && (ch < 128 || (ch >= 'А' && ch <= 'я') || ch == 'ё' || ch == 'Ё') || ch == ' ' || ch == '-')
                    sb.Append(ch);
            return sb.ToString().Trim();
        }
    }
}
