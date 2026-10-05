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
                1.15f, 1.30f, 1.00f, 1.00f)
        };
    }
}