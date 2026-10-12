using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Гербы фракций: Resources/Factions/emblem_&lt;ключ правителя&gt;.png — белый силуэт на прозрачном фоне,
    /// цвет подставляется по месту (обычно — цвет империи). Ключи: xarn, astrea, aquila, iridia, terraan.
    /// </summary>
    public static class FactionEmblems
    {
        private static readonly Dictionary<string, Sprite> s_cache = new Dictionary<string, Sprite>();

        public static Sprite ForKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (s_cache.TryGetValue(key, out var sp)) return sp;
            var tex = Resources.Load<Texture2D>("Factions/emblem_" + key);
            if (tex != null)
            {
                tex.wrapMode = TextureWrapMode.Clamp;
                sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
                sp.name = "Emblem_" + key;
                sp.hideFlags = HideFlags.DontSave;
            }
            s_cache[key] = sp;
            return sp;
        }

        public static Sprite ForFaction(FactionInfo f) => ForKey(LeaderPortraits.ForFaction(f)?.Key);

        /// <summary>Герб империи по владельцу: 0 — фракция игрока, иначе — ИИ.</summary>
        public static Sprite ForOwner(int owner)
        {
            if (owner == 0) return ForFaction(UIManager.Instance != null ? UIManager.Instance.SelectedFaction : null);
            var ai = AIEmpireManager.For(owner);
            return ai != null ? ForFaction(ai.Faction) : null;
        }

        /// <summary>UI-герб заданного размера (по центру родителя, если не перемещать). null — герба нет.</summary>
        public static Image Create(Transform parent, Sprite emblem, float size, Color color, string name = "Emblem")
        {
            if (emblem == null) return null;
            var rt = LGBuild.Rect(parent, name);
            rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = emblem;
            img.preserveAspect = true;
            img.color = color;
            img.raycastTarget = false;
            LG.Ignore(rt.gameObject);
            return img;
        }
    }
}
