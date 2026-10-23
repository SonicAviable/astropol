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

        /// <summary>Постоянный ключ фракции (xarn, astrea, aquila, iridia, terraan, nol) — не зависит от названия,
        /// поэтому своё название империи не ломает портреты, гербы, корабли, черты и сохранения.</summary>
        public string Key;

        // ---- Оформление империи игрока (экран «Оформление империи»); у базовых фракций пусто ----
        /// <summary>Своё имя правителя (null — имя с портрета фракции).</summary>
        public string RulerName;
        /// <summary>Герб другой фракции (ключ) — null: свой.</summary>
        public string EmblemKey;
        /// <summary>Цвет империи на карте (границы, корабли); альфа 0 — цвет по умолчанию.</summary>
        public Color MapColor;

        public FactionInfo(string name, string title, string desc, Color color,
                           float energy, float min, float alloy, float inf)
        {
            Name = name; Title = title; Description = desc; EmpireColor = color;
            EnergyBonus = energy; MineralBonus = min; AlloyBonus = alloy; InfluenceBonus = inf;
            Key = FactionRegistry.KeyOfName(name);
        }

        /// <summary>Копия для оформления: общие экземпляры AvailableFactions выбирает и ИИ — их не меняем.</summary>
        public FactionInfo Clone() => (FactionInfo)MemberwiseClone();
    }

    public static class FactionRegistry
    {
        /// <summary>Короткий ключ фракции: xarn, astrea, aquila, iridia, terraan, nol (null — неизвестна).</summary>
        public static string KeyOf(FactionInfo f)
        {
            if (f == null) return null;
            return !string.IsNullOrEmpty(f.Key) ? f.Key : KeyOfName(f.Name);
        }

        /// <summary>Базовая фракция по ключу (null — нет такой).</summary>
        public static FactionInfo ByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var f in AvailableFactions) if (KeyOf(f) == key) return f;
            return null;
        }

        /// <summary>Ключ фракции по названию — на любом языке игры (русское или английское название).</summary>
        public static string KeyOfName(string n)
        {
            if (string.IsNullOrEmpty(n)) return null;
            if (n.Contains("Ксарн") || n.Contains("Xarn")) return "xarn";
            if (n.Contains("Астре") || n.Contains("Astre")) return "astrea";
            if (n.Contains("Аквил") || n.Contains("Aquil")) return "aquila";
            if (n.Contains("Ирид") || n.Contains("Irid")) return "iridia";
            if (n.Contains("Тэрра") || n.Contains("Terra")) return "terraan";
            if (n.Contains("Консенсус") || n.Contains("Consensus")) return "nol";
            return null;
        }

        public static bool IsTerraan(FactionInfo f) => KeyOf(f) == "terraan";
        public static bool IsMachine(FactionInfo f) => KeyOf(f) == "nol";

        public static readonly FactionInfo[] AvailableFactions = new FactionInfo[]
        {
            new FactionInfo(
                Loc.T("Республика Астрея"),
                Loc.T("Свободная Федерация"),
                Loc.T("Республика, рождённая из союза колоний-первопроходцев. ") +
                Loc.T("Верит в дипломатию, равенство и научный прогресс. ") +
                Loc.T("Предпочитает строить мир убеждением, а не силой.\n\n") +
                Loc.T("<b>+10% к Энергии</b>\n") +
                Loc.T("<b>+15% к Влиянию</b>"),
                new Color(0.10f, 0.80f, 0.95f),
                1.10f, 1.00f, 1.00f, 1.15f),

            new FactionInfo(
                Loc.T("Доминион Ксарн"),
                Loc.T("Военная Иерархия"),
                Loc.T("Империя, выкованная в бесконечных войнах. Культ силы, дисциплина и тяжёлая ") +
                Loc.T("промышленность — три кита её могущества. Здесь правят те, кто умеет побеждать.\n\n") +
                Loc.T("<b>+35% к Сплавам</b>\n") +
                Loc.T("<b>+25% к Минералам</b>"),
                new Color(0.95f, 0.25f, 0.20f),
                1.00f, 1.25f, 1.35f, 1.00f),

            new FactionInfo(
                Loc.T("Синдикат Аквила"),
                Loc.T("Корпоративный Альянс"),
                Loc.T("Торговая мегакорпорация, разросшаяся до размеров государства. ") +
                Loc.T("Здесь правит прибыль, а дипломатия — лишь инструмент сделки. ") +
                Loc.T("Синдикат покупает то, что другие берут силой.\n\n") +
                Loc.T("<b>+30% к Минералам</b>\n") +
                Loc.T("<b>+15% к Энергии</b>"),
                new Color(1.00f, 0.75f, 0.15f),
                1.15f, 1.30f, 1.00f, 1.00f),

            new FactionInfo(
                Loc.T("Иридийский Оракул"),
                Loc.T("Теократия Провидцев"),
                Loc.T("Древняя раса провидцев с мира-сада Иридия. Их кожа светится в такт мыслям, ") +
                Loc.T("а правит ими Верховная Оракул, читающая нити будущего. ") +
                Loc.T("Оракул редко начинает войну первым — он заранее знает, чем она закончится.\n\n") +
                Loc.T("<b>+30% к Влиянию</b>\n") +
                Loc.T("<b>+5% к Энергии</b>"),
                new Color(0.66f, 0.42f, 1.00f),
                1.05f, 1.00f, 1.00f, 1.30f),

            new FactionInfo(
                Loc.T("Хлорофиловый Конклав Тэрра'ан"),
                Loc.T("Живой Конклав Корней"),
                Loc.T("Древнейшая из живых цивилизаций. Их миры — огромные сады, корабли выращены из биокоры, ") +
                Loc.T("а решения принимает весь Конклав через сеть мицелия. Тэрра'ан не спешит — и переживает тех, кто спешит.\n\n") +
                Loc.T("<b>Живая броня:</b> корпус сам заживает вне боя\n") +
                Loc.T("<b>+15% корпус, +10% щиты, +35% влияние, +5% энергия</b>\n") +
                Loc.T("<b>Разведка +20%, форпосты −20% сплавов</b>\n") +
                Loc.T("<color=#E7A0A0>Стройка +20% дольше · гиперскорость −10%\n") +
                Loc.T("уязвимы к энергии +15% · минералы −10%</color>"),
                new Color(0.44f, 0.86f, 0.34f),
                1.05f, 0.90f, 1.00f, 1.35f),

            new FactionInfo(
                Loc.T("Машинный Консенсус «Ноль»"),
                Loc.T("Единый Разум"),
                Loc.T("Когда-то их создатели называли их просто «Проект Ноль». Создателей давно нет, а миллиард машин ") +
                Loc.T("думает как один разум: без страха, усталости и споров. Консенсус не ненавидит органиков — он их просчитывает.\n\n") +
                Loc.T("<b>Сборка вместо рождения:</b> синтеты не размножаются — их собирают фабрики, не зависящие от климата\n") +
                Loc.T("<b>Без бунтов:</b> машинам не нужна стабильность\n") +
                Loc.T("<b>Роботы вдвое дешевле · ассимиляция чужих видов</b>\n") +
                Loc.T("<b>+20% титана, +10% сплавов, +10% корпус</b>\n") +
                Loc.T("<color=#E7A0A0>Влияние −25% · органики не доверяют машинам (−10 к отношению)\n") +
                Loc.T("рост населения −25%</color>"),
                new Color(0.86f, 0.91f, 0.97f),
                1.00f, 1.20f, 1.10f, 0.75f)
        };
    }
}