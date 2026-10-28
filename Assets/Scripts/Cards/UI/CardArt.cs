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
    /// <summary>Редкость карты в оформлении (см. CardArt.Rarity).</summary>
    public enum CardRarity { Common, Rare, Epic, Legendary, Commander }

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
            // Герой: сначала версия под фракцию (H1_xarn), потом общая (H1), потом портрет лидера
            if (d.Kind == CardKind.Hero && !string.IsNullOrEmpty(sideFaction))
                t = Resources.Load<Texture2D>("UI/Cards/" + d.Id + "_" + sideFaction);
            if (t == null) t = Resources.Load<Texture2D>("UI/Cards/" + d.Id);
            if (t == null && d.Kind == CardKind.Hero && !string.IsNullOrEmpty(sideFaction))
                t = Resources.Load<Texture2D>("Leaders/leader_" + sideFaction);
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

        /// <summary>Арт плашки сектора: Resources/UI/Cards/sector_&lt;тип&gt;, иначе подходящая иллюстрация событий.</summary>
        public static Texture Sector(SectorType t)
        {
            string id = "sector_" + t.ToString().ToLower();
            if (s_cache.TryGetValue(id, out var tex) && tex != null) return tex;
            tex = Resources.Load<Texture2D>("UI/Cards/" + id);
            if (tex == null)
            {
                string ev = t == SectorType.Power ? "Strike" : t == SectorType.Factory ? "Drills" : t == SectorType.HQ ? "PrecursorVault"
                          : t == SectorType.Mine ? "Quake" : t == SectorType.City ? "Festival" : "Trade";
                tex = Resources.Load<Texture2D>("UI/Events/" + ev);
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

        /// <summary>«Фольга» — голографический перелив и бегущий блик: эпические, легендарные, командиры.</summary>
        public static bool Foil(CardDef d, int stars) => Rarity(d, stars) >= CardRarity.Epic;

        /// <summary>
        /// Редкость для оформления (рамка, свечение, фольга): командир — герой; легендарная — ветеран ★★★ или карта
        /// ценой от 5; эпическая — фракционная; редкая — ветеран или цена от 4; остальное — обычная.
        /// </summary>
        public static CardRarity Rarity(CardDef d, int stars)
        {
            if (d == null) return CardRarity.Common;
            if (d.Kind == CardKind.Hero) return CardRarity.Commander;
            if (stars >= 3 || d.Cost >= 5) return CardRarity.Legendary;
            if (d.Faction != null) return CardRarity.Epic;
            if (stars >= 1 || d.Cost >= 4) return CardRarity.Rare;
            return CardRarity.Common;
        }

        /// <summary>Суффикс текстур оформления (cardframe_&lt;key&gt;, card_cost_&lt;key&gt;…).</summary>
        public static string RarityKey(CardRarity r)
        {
            switch (r)
            {
                case CardRarity.Rare: return "rare";
                case CardRarity.Epic: return "epic";
                case CardRarity.Legendary: return "legend";
                case CardRarity.Commander: return "cmd";
                default: return "common";
            }
        }

        /// <summary>Цвет редкости: кромка рамки, свечение, ключевые слова в тексте.</summary>
        public static Color RarityColor(CardRarity r)
        {
            switch (r)
            {
                case CardRarity.Rare: return new Color(0.37f, 0.78f, 1f);
                case CardRarity.Epic: return new Color(0.77f, 0.55f, 1f);
                case CardRarity.Legendary: return new Color(1f, 0.81f, 0.35f);
                case CardRarity.Commander: return new Color(1f, 0.36f, 0.29f);
                default: return new Color(0.66f, 0.72f, 0.78f);
            }
        }

        public static string RarityName(CardRarity r)
        {
            switch (r)
            {
                case CardRarity.Rare: return Loc.T("РЕДКАЯ");
                case CardRarity.Epic: return Loc.T("ЭПИЧЕСКАЯ");
                case CardRarity.Legendary: return Loc.T("ЛЕГЕНДАРНАЯ");
                case CardRarity.Commander: return Loc.T("КОМАНДИР");
                default: return Loc.T("ОБЫЧНАЯ");
            }
        }

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
