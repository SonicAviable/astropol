using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Боевые выкрики отрядов (пока текстом над отрядом; озвучка — отдельным пакетом).
    /// У атакующих и защитников свои фразы, у фракций — свой боевой клич.
    /// </summary>
    public static class GroundCallouts
    {
        private static string Pick(params string[] v) => v[Random.Range(0, v.Length)];

        private static bool Attacker(Squad s) => s.Side.Index == 0;

        public static string BattleCry(Squad s)
        {
            switch (s.Side.FactionKey)
            {
                case "xarn": return Loc.T("За Доминион!");
                case "astrea": return Loc.T("За Республику!");
                case "aquila": return Loc.T("Контракт превыше всего!");
                case "iridia": return Loc.T("Так предсказано!");
                case "terraan": return Loc.T("Корни держат!");
                case "nol": return Loc.T("ЦЕЛЬ ПОДТВЕРЖДЕНА.");
                default: return Loc.T("За империю!");
            }
        }

        public static string Combat(Squad s)
        {
            if (s.Def.Role == UnitRole.Swarm || s.Side.FactionKey == "nol") return Pick(Loc.T("УСТРАНЕНИЕ."), Loc.T("ЦЕЛЬ ПОДТВЕРЖДЕНА."), Loc.T("СОПРОТИВЛЕНИЕ НЕЭФФЕКТИВНО."));
            if (Attacker(s))
                return Pick(BattleCry(s), Loc.T("Держать строй!"), Loc.T("Гранату им!"), Loc.T("Вперёд, вперёд!"), Loc.T("Подавить огнём!"), Loc.T("Они повсюду!"));
            return Pick(Loc.T("Это наш дом!"), Loc.T("Держите улицу!"), Loc.T("Не пропускать!"), BattleCry(s), Loc.T("Они прорвались!"));
        }

        public static string Pinned(Squad s) => s.Side.FactionKey == "nol" ? null : Pick(Loc.T("Головы не поднять!"), Loc.T("Прижали!"), Loc.T("Ложись!"));
        public static string Retreat(Squad s) => s.Side.FactionKey == "nol" ? Loc.T("ПЕРЕГРУППИРОВКА.") : Pick(Loc.T("Отходим к маяку!"), Loc.T("Назад, назад!"), Loc.T("Отступаем!"));
        public static string SergeantDown(Squad s) => s.Side.FactionKey == "nol" ? null : Pick(Loc.T("Сержант убит!"), Loc.T("Медика сюда!"), Loc.T("Кто командует?!"));
        public static string Landed(Squad s) => Attacker(s) ? Pick(Loc.T("Капсулы пошли!"), Loc.T("Высадка!"), BattleCry(s)) : Pick(Loc.T("Подкрепление на месте!"), BattleCry(s));
        public static string Home(Squad s) => Pick(Loc.T("Это наш дом!"), Loc.T("Колонисты, к оружию!"), Loc.T("Держите улицу!"));
        public static string Execution(Squad s) => s.Def.Role == UnitRole.Walker ? Loc.T("СОКРУШИТЬ!") : Pick(Loc.T("Сдохни!"), BattleCry(s), Loc.T("На колени!"));
        public static string ShieldDown() => Loc.T("Щит падает!");
    }
}
