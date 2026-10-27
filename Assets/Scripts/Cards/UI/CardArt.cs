using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Арты и цвета карт. Арт ищется в Resources/UI/Cards/&lt;id&gt; (картинки из Gemini), иначе — подходящая иллюстрация
    /// событий игры, у героев — портрет лидера фракции. Цвета фракций, рамки по редкости, кровь по виду.
    /// </summary>
    public static class CardArt
    {
        private static readonly Dictionary<string, Texture> s_cache = new Dictionary<string, Texture>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => s_cache.Clear();

        /// <summary>Запасные иллюстрации (Resources/UI/Events), пока нет артов карт.</summary>
        private static readonly Dictionary<string, string> Fallback = new Dictionary<string, string>
        {
            ["U1"] = "LegionReview", ["U2"] = "Battlefield", ["U3"] = "Strike", ["U4"] = "Intercept", ["U5"] = "Quake",
            ["U6"] = "Plague", ["U7"] = "SalvageWreck", ["U8"] = "Probe", ["U9"] = "Refugees", ["U10"] = "BorderIncident",
            ["U11"] = "Meteor", ["U12"] = "Quake", ["O1"] = "Drills", ["O2"] = "Meteor", ["O3"] = "Meteor", ["O4"] = "PrecursorMap",
            ["O5"] = "GasStorm", ["O6"] = "LegionReview", ["O7"] = "Intercept", ["O8"] = "Summit", ["O9"] = "Rift",
            ["D1"] = "Strike", ["D2"] = "Mole", ["D3"] = "Quake", ["D4"] = "Drills", ["D5"] = "Meteor", ["D6"] = "PrecursorVault",
            ["A1"] = "Festival", ["A2"] = "Summit", ["A3"] = "RepublicCouncil", ["A4"] = "Refugees",
            ["X1"] = "Deserters", ["X2"] = "LegionReview", ["X3"] = "CrewUnrest", ["X4"] = "Battlefield",
            ["Q1"] = "Shareholders", ["Q2"] = "Broker", ["Q3"] = "DoubleAgent", ["Q4"] = "Investment",
            ["I1"] = "OracleVisions", ["I2"] = "Mind", ["I3"] = "OracleVisions", ["I4"] = "Rift",
            ["T1"] = "Brood", ["T2"] = "RootCouncil", ["T3"] = "BroodGrown", ["T4"] = "RootCouncil",
            ["N1"] = "Consensus", ["N2"] = "Heart", ["N3"] = "HeartVisitors", ["N4"] = "Consensus",
            ["Z_CREW"] = "CrewUnrest", ["Z_DRONE"] = "Consensus",
        };

        public static Texture Get(CardDef d, string sideFaction)
        {
            if (d == null) return null;
            string key = d.Id + "|" + (d.Kind == CardKind.Hero ? sideFaction : "");
            if (s_cache.TryGetValue(key, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>("UI/Cards/" + d.Id);
            if (t == null && d.Kind == CardKind.Hero && !string.IsNullOrEmpty(sideFaction))
                t = Resources.Load<Texture2D>("UI/Cards/" + d.Id + "_" + sideFaction) ?? Resources.Load<Texture2D>("Leaders/leader_" + sideFaction);
            if (t == null && Fallback.TryGetValue(d.Id, out var ev)) t = Resources.Load<Texture2D>("UI/Events/" + ev);
            if (t == null) t = Resources.Load<Texture2D>("UI/LoadingArt");
            s_cache[key] = t;
            return t;
        }

        /// <summary>Иллюстрация есть «настоящая» (из Resources/UI/Cards), а не запасная — у запасных другой кадр.</summary>
        public static bool HasOwnArt(CardDef d) => d != null && Resources.Load<Texture2D>("UI/Cards/" + d.Id) != null;

        public static Texture Table(Terrain t)
        {
            string id = t == Terrain.Desert ? "table_desert" : t == Terrain.Molten ? "table_molten" : t == Terrain.Barren ? "table_barren" : "table_continental";
            if (s_cache.TryGetValue(id, out var tex) && tex != null) return tex;
            tex = Resources.Load<Texture2D>("UI/Cards/" + id);
            if (tex == null)
            {
                string fb = t == Terrain.Desert ? "UI/LoadingArt_3" : t == Terrain.Molten ? "UI/Events/Quake" : t == Terrain.Barren ? "UI/Events/Meteor" : "UI/Events/Battlefield";
                tex = Resources.Load<Texture2D>(fb);
            }
            s_cache[id] = tex;
            return tex;
        }

        public static Texture Emblem(string faction)
        {
            if (string.IsNullOrEmpty(faction)) return null;
            string id = "emblem_" + faction;
            if (s_cache.TryGetValue(id, out var tex) && tex != null) return tex;
            tex = Resources.Load<Texture2D>("UI/Cards/back_" + faction) ?? Resources.Load<Texture2D>("Factions/" + id);
            s_cache[id] = tex;
            return tex;
        }

        public static Texture Leader(string faction)
        {
            if (string.IsNullOrEmpty(faction)) return null;
            string id = "leader_" + faction;
            if (s_cache.TryGetValue(id, out var tex) && tex != null) return tex;
            tex = Resources.Load<Texture2D>("Leaders/" + id);
            s_cache[id] = tex;
            return tex;
        }

        public static Color Faction(string key)
        {
            switch (key)
            {
                case "astrea": return new Color(0.45f, 0.86f, 0.92f);
                case "xarn": return new Color(0.92f, 0.30f, 0.30f);
                case "aquila": return new Color(0.98f, 0.78f, 0.32f);
                case "iridia": return new Color(0.72f, 0.48f, 1f);
                case "terraan": return new Color(0.45f, 0.92f, 0.45f);
                case "nol": return new Color(0.88f, 0.92f, 0.98f);
                default: return new Color(0.70f, 0.78f, 0.86f);
            }
        }

        public static BloodKind Blood(string faction) => faction == "iridia" ? BloodKind.Violet : faction == "terraan" ? BloodKind.Green : faction == "nol" ? BloodKind.Coolant : BloodKind.Red;

        /// <summary>Цвет рамки по редкости: обычная — сталь, ветеран — бронза, элита — золото, фракционная/герой — цвет фракции.</summary>
        public static Color Frame(CardDef d, int stars, string faction)
        {
            if (stars >= 3) return new Color(1f, 0.82f, 0.35f);
            if (d.Kind == CardKind.Hero) return new Color(1f, 0.86f, 0.45f);
            if (d.Faction != null) return Faction(d.Faction);
            if (stars >= 1) return new Color(0.82f, 0.55f, 0.32f);
            switch (d.Kind)
            {
                case CardKind.Order: return new Color(0.78f, 0.55f, 1f);
                case CardKind.Structure: return new Color(0.45f, 0.70f, 0.95f);
                case CardKind.Trap: return new Color(1f, 0.45f, 0.35f);
                default: return new Color(0.62f, 0.70f, 0.78f);
            }
        }

        /// <summary>«Фольга» — блик бежит по карте: элита, фракционные, герои.</summary>
        public static bool Foil(CardDef d, int stars) => stars >= 3 || d.Faction != null || d.Kind == CardKind.Hero;

        public static string SectorName(SectorType t)
        {
            switch (t)
            {
                case SectorType.Power: return Loc.T("ЭЛЕКТРОСТАНЦИЯ");
                case SectorType.Factory: return Loc.T("ЗАВОД");
                case SectorType.HQ: return Loc.T("ШТАБ");
                case SectorType.Mine: return Loc.T("ШАХТА");
                case SectorType.City: return Loc.T("ГОРОД");
                default: return Loc.T("КОСМОПОРТ");
            }
        }

        public static string SectorRule(SectorType t)
        {
            switch (t)
            {
                case SectorType.Power: return Loc.T("Пока у защитника — работают турели и щит Штаба");
                case SectorType.Factory: return Loc.T("Держащий получает +1 карту каждый ход");
                case SectorType.HQ: return Loc.T("Щит снят и 9+ силы — прорыв, планета пала");
                case SectorType.Mine: return Loc.T("Держащий получает +1 очко приказов");
                case SectorType.City: return Loc.T("Жители-ополченцы защищают дома");
                default: return Loc.T("Удобное место для высадки");
            }
        }

        public static LGIcon SectorIcon(SectorType t)
        {
            switch (t)
            {
                case SectorType.Power: return LGIcon.Energy;
                case SectorType.Factory: return LGIcon.Industry;
                case SectorType.HQ: return LGIcon.Starbase;
                case SectorType.Mine: return LGIcon.Minerals;
                case SectorType.City: return LGIcon.Population;
                default: return LGIcon.Shipyard;
            }
        }
    }
}
