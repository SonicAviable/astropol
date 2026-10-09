using System;
using UnityEngine;

namespace StellarisClone.Core
{
    [Serializable]
    public class FactionInfo
    {
        public string Name;
        public string Title;
        public string Description;
        public Color EmpireColor;
        public float EnergyBonus;
        public float MineralBonus;
        public float AlloyBonus;
        public float InfluenceBonus;

        public FactionInfo(string name, string title, string desc, Color color,
                           float energy, float min, float alloy, float inf)
        {
            Name = name; Title = title; Description = desc; EmpireColor = color;
            EnergyBonus = energy; MineralBonus = min; AlloyBonus = alloy; InfluenceBonus = inf;
        }
    }

    public static class FactionRegistry
    {
        /// <summary>Короткий ключ фракции: xarn, astrea, aquila, iridia, terraan (null — неизвестна).</summary>
        public static string KeyOf(FactionInfo f)
        {
            string n = f?.Name;
            if (string.IsNullOrEmpty(n)) return null;
            if (n.Contains("Ксарн")) return "xarn";
            if (n.Contains("Астре")) return "astrea";
            if (n.Contains("Аквил")) return "aquila";
            if (n.Contains("Ирид")) return "iridia";
            if (n.Contains("Тэрра")) return "terraan";
            return null;
        }

        public static bool IsTerraan(FactionInfo f) => KeyOf(f) == "terraan";

        public static readonly FactionInfo[] AvailableFactions = new FactionInfo[]
        {
            new FactionInfo(
                "Республика Астрея",
                "Свободная Федерация",
                "Республика, рождённая из союза колоний-первопроходцев. " +
                "Верит в дипломатию, равенство и научный прогресс. " +
                "Предпочитает строить мир убеждением, а не силой.\n\n" +
                "<b>+20% к Энергии</b>\n" +
                "<b>+25% к Влиянию</b>",
                new Color(0.10f, 0.80f, 0.95f),
                1.20f, 1.00f, 1.00f, 1.25f),

            new FactionInfo(
                "Доминион Ксарн",
                "Военная Иерархия",
                "Империя, выкованная в бесконечных войнах. Культ силы, дисциплина и тяжёлая " +
                "промышленность — три кита её могущества. Здесь правят те, кто умеет побеждать.\n\n" +
                "<b>+35% к Сплавам</b>\n" +
                "<b>+10% к Минералам</b>",
                new Color(0.95f, 0.25f, 0.20f),
                1.00f, 1.10f, 1.35f, 1.00f),

            new FactionInfo(
                "Синдикат Аквила",
                "Корпоративный Альянс",
                "Торговая мегакорпорация, разросшаяся до размеров государства. " +
                "Здесь правит прибыль, а дипломатия — лишь инструмент сделки. " +
                "Синдикат покупает то, что другие берут силой.\n\n" +
                "<b>+30% к Минералам</b>\n" +
                "<b>+15% к Энергии</b>",
                new Color(1.00f, 0.75f, 0.15f),
                1.15f, 1.30f, 1.00f, 1.00f),

            new FactionInfo(
                "Иридийский Оракул",
                "Теократия Провидцев",
                "Древняя раса провидцев с мира-сада Иридия. Их кожа светится в такт мыслям, " +
                "а правит ими Верховная Оракул, читающая нити будущего. " +
                "Оракул редко начинает войну первым — он заранее знает, чем она закончится.\n\n" +
                "<b>+40% к Влиянию</b>\n" +
                "<b>+10% к Энергии</b>",
                new Color(0.66f, 0.42f, 1.00f),
                1.10f, 1.00f, 1.00f, 1.40f),

            new FactionInfo(
                "Хлорофиловый Конклав Тэрра'ан",
                "Живой Конклав Корней",
                "Древнейшая из живых цивилизаций. Их миры — огромные сады, корабли выращены из биокоры, " +
                "а решения принимает весь Конклав через сеть мицелия. Тэрра'ан не спешит — и переживает тех, кто спешит.\n\n" +
                "<b>Живая броня:</b> корпус сам заживает вне боя\n" +
                "<b>+15% корпус, +10% щиты, +25% влияние</b>\n" +
                "<b>Разведка +20%, форпосты −20% сплавов</b>\n" +
                "<color=#E7A0A0>Стройка +20% дольше · гиперскорость −10%\n" +
                "уязвимы к энергии +15% · минералы −15%</color>",
                new Color(0.44f, 0.86f, 0.34f),
                1.00f, 0.85f, 1.00f, 1.25f)
        };
    }
}