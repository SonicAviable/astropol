using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// «Почему кнопка серая»: точная нехватка ресурсов игрока, а не общее «не хватает ресурсов».
    /// Full — для подсказок («не хватает 30 влияния и 12 сплавов»), Short — для надписи на кнопке.
    /// </summary>
    public static class CostCheck
    {
        private static void Collect(List<string> list, float need, float have, string name)
        {
            if (need <= 0f || have >= need) return;
            list.Add($"{Mathf.CeilToInt(need - have)} {name}");
        }

        private static List<string> Missing(float energy, float minerals, float alloys, float influence)
        {
            var list = new List<string>();
            var eco = EconomyManager.Instance;
            if (eco == null) return list;
            Collect(list, energy, eco.EnergyCredits, "гелия-3");
            Collect(list, minerals, eco.Minerals, "титана");
            Collect(list, alloys, eco.Alloys, "сплавов");
            Collect(list, influence, eco.Influence, "влияния");
            return list;
        }

        /// <summary>Всё ли по карману.</summary>
        public static bool Affordable(float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f)
            => EconomyManager.Instance != null && Missing(energy, minerals, alloys, influence).Count == 0;

        /// <summary>«Не хватает 30 влияния и 12 сплавов» — null, если всего хватает.</summary>
        public static string Full(float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f)
        {
            var l = Missing(energy, minerals, alloys, influence);
            if (l.Count == 0) return null;
            string body = l.Count == 1 ? l[0] : string.Join(", ", l.GetRange(0, l.Count - 1)) + " и " + l[l.Count - 1];
            return "Не хватает " + body;
        }

        /// <summary>Для кнопки: «НЕ ХВАТАЕТ 30 ВЛИЯНИЯ» (если не хватает нескольких — первое и «…»).</summary>
        public static string Short(float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f)
        {
            var l = Missing(energy, minerals, alloys, influence);
            if (l.Count == 0) return null;
            return ("НЕ ХВАТАЕТ " + l[0] + (l.Count > 1 ? " …" : "")).ToUpper();
        }

        /// <summary>Сколько месяцев до окупаемости (вложение и доход в одном ресурсе); -1 — не окупится.</summary>
        public static int PaybackMonths(float cost, float perMonth) => perMonth <= 0f ? -1 : Mathf.CeilToInt(cost / perMonth);
    }
}
