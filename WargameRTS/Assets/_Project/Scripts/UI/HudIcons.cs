using System.Collections.Generic;
using UnityEngine;
using WargameRTS.Units;

namespace WargameRTS.UI
{
    /// <summary>
    /// Loads HUD icons from Assets/_Project/Resources/UI. Order icons are white glyphs
    /// (tinted at draw time); unit icons are NATO-style glyphs with amber accents;
    /// status icons are full-colour.
    /// </summary>
    public static class HudIcons
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        static readonly HashSet<string> warned = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cache.Clear();
            warned.Clear();
        }

        public static Texture2D Order(string name) => Load("UI/Orders/" + name);

        /// <summary>Full-colour status/resource icons (Timer, Armor, Ammo, CommandPoints, Income, ...).</summary>
        public static Texture2D Status(string name) => Load("UI/Status/" + name);

        public static Texture2D Unit(UnitCategory category)
        {
            switch (category)
            {
                case UnitCategory.Tank: return Load("UI/Units/Tank");
                case UnitCategory.InfantryFightingVehicle: return Load("UI/Units/Ifv");
                case UnitCategory.Infantry: return Load("UI/Units/Infantry");
                case UnitCategory.Recon: return Load("UI/Units/Recon");
                case UnitCategory.AntiTank: return Load("UI/Units/AntiTank");
                case UnitCategory.AntiAir: return Load("UI/Units/AntiAir");
                case UnitCategory.Artillery: return Load("UI/Units/Artillery");
                default: return null;
            }
        }

        static Texture2D Load(string path)
        {
            if (cache.TryGetValue(path, out var texture) && texture != null)
                return texture;

            // Missing icons are not cached: they may still be importing right after a pull.
            texture = Resources.Load<Texture2D>(path);
            if (texture != null)
                cache[path] = texture;
            else if (warned.Add(path))
                Debug.LogWarning($"[WargameRTS] HUD icon not found (yet): Resources/{path}.png");
            return texture;
        }
    }
}
