using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Rendering;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Скин карточного боя: «дорогие» текстуры из Resources/UI/CardBattle (металл с фасками, самоцветы, гнёзда,
    /// кнопка хода, ленты, флипбуки взрывов) и шрифт заголовков Russo One. Спрайты с 9-slice создаются здесь —
    /// настройки импорта текстур не важны. Если текстуры нет, отдаётся процедурный спрайт из <see cref="CardSprites"/>.
    /// </summary>
    public static class CardSkin
    {
        private const string Root = "UI/CardBattle/";
        private static readonly Dictionary<string, Texture2D> s_tex = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Sprite> s_spr = new Dictionary<string, Sprite>();
        private static Font s_title;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { s_tex.Clear(); s_spr.Clear(); s_title = null; }

        // Бордюры 9-slice в пикселях текстуры (лево, низ, право, верх)
        private static Vector4 BorderOf(string n)
        {
            switch (n)
            {
                case "frame_card": return new Vector4(70, 70, 70, 70);
                case "frame_thin": return new Vector4(40, 40, 40, 40);
                case "panel_dark": return new Vector4(40, 40, 40, 40);
                case "panel_leather": return new Vector4(60, 60, 60, 60);
                case "panel_plates": return new Vector4(70, 70, 70, 70);
                case "panel_text": return new Vector4(30, 30, 30, 30);
                case "ribbon": return new Vector4(60, 0, 60, 0);
                case "btn_end_green": case "btn_end_amber": case "btn_end_gray": return new Vector4(100, 0, 100, 0);
                default: return Vector4.zero;
            }
        }

        public static Texture2D Tex(string name)
        {
            if (s_tex.TryGetValue(name, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(Root + name);
            s_tex[name] = t;
            return t;
        }

        public static Sprite Spr(string name)
        {
            if (s_spr.TryGetValue(name, out var s) && s != null) return s;
            var t = Tex(name);
            if (t == null) return Fallback(name);
            t.wrapMode = TextureWrapMode.Clamp;
            s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, BorderOf(name));
            s.name = name;
            s_spr[name] = s;
            return s;
        }

        private static Sprite Fallback(string name)
        {
            if (name.StartsWith("frame")) return CardSprites.RoundEdge;
            if (name.StartsWith("panel") || name.StartsWith("btn") || name == "slot") return CardSprites.Round;
            if (name.StartsWith("gem_power")) return CardSprites.Diamond;
            if (name.StartsWith("gem_armor")) return CardSprites.ShieldShape;
            if (name.StartsWith("gem") || name.StartsWith("crystal")) return CardSprites.Hex;
            if (name.StartsWith("orb") || name == "disc") return CardSprites.Dot;
            if (name == "ring" || name == "k_circle") return CardSprites.Ring;
            if (name.StartsWith("k_smoke") || name == "fx_smoke") return CardSprites.Smoke;
            if (name.StartsWith("k_spark") || name.StartsWith("k_trace")) return CardSprites.Spark;
            return CardSprites.Glow;
        }

        /// <summary>Шрифт заголовков и цифр (Russo One); если нет — жирный шрифт игры.</summary>
        public static Font Title
        {
            get
            {
                if (s_title == null) s_title = Resources.Load<Font>("Fonts/RussoOne-Regular");
                return s_title;
            }
        }

        public static bool HasArt => Tex("frame_card") != null;

        // ---------------------------------------------------------------- сборка элементов

        /// <summary>Картинка-9-slice; scale — во сколько раз ужать бордюр на экране (0.4 — тонкая рамка у маленьких карт).</summary>
        public static Image Sliced(Transform parent, string name, string sprite, Color c, float scale = 1f)
        {
            var img = LGBuild.Panel(parent, name, c);
            img.sprite = Spr(sprite);
            img.type = BorderOf(sprite) != Vector4.zero && Tex(sprite) != null ? Image.Type.Sliced : (Tex(sprite) == null ? Image.Type.Sliced : Image.Type.Simple);
            img.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.05f, scale);
            img.raycastTarget = false;
            return img;
        }

        public static Image Simple(Transform parent, string name, string sprite, Color c)
        {
            var img = LGBuild.Panel(parent, name, c);
            img.sprite = Spr(sprite);
            img.type = Image.Type.Simple;
            img.preserveAspect = false;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Надпись шрифтом заголовков с тенью и обводкой (цифры на самоцветах, заголовки).</summary>
        public static Text TitleText(Transform parent, string text, int size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, float outline = 2f)
        {
            var t = LGBuild.Label(parent, text, size, c, anchor, bold: Title == null);
            if (Title != null) { t.font = Title; t.fontStyle = FontStyle.Normal; }
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (outline > 0f)
            {
                var ol = t.gameObject.AddComponent<Outline>();
                ol.effectColor = new Color(0, 0, 0, 0.9f);
                ol.effectDistance = new Vector2(outline, -outline);
                var sh = t.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0, 0, 0, 0.6f);
                sh.effectDistance = new Vector2(0, -outline - 1f);
            }
            return t;
        }

        /// <summary>Самоцвет с числом (цена, сила, броня, сила сектора).</summary>
        public static Text Gem(Transform parent, string sprite, Vector2 anchor, Vector2 pos, float size, string value, out RectTransform rt)
        {
            var g = Simple(parent, "Gem_" + sprite, sprite, Color.white);
            g.rectTransform.At(anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            rt = g.rectTransform;
            var t = TitleText(g.rectTransform, value, Mathf.RoundToInt(size * 0.46f), Color.white, TextAnchor.MiddleCenter, Mathf.Max(1.5f, size / 28f));
            t.rectTransform.Stretch(0, 0, 0, -1);
            return t;
        }

        public static readonly Color Gold = new Color(1f, 0.82f, 0.45f);
        public static readonly Color Steel = new Color(0.85f, 0.88f, 0.92f);
        public static readonly Color Bronze = new Color(0.9f, 0.62f, 0.4f);
        public static readonly Color Teal = new Color(0.3f, 0.95f, 0.85f);
        public static readonly Color Red = new Color(1f, 0.35f, 0.28f);
        public static readonly Color Parchment = new Color(1f, 0.93f, 0.8f);
    }
}
