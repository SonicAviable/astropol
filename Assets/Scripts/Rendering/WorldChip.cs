using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Плашки-ярлыки в мире карты (над кораблями, под названиями систем) — в стиле интерфейса:
    /// тёмная полупрозрачная пластина со срезанными углами и тонкой светящейся кромкой, полоска-акцент,
    /// значок и текст шрифтом игры. Всё — SpriteRenderer/TextMesh, повёрнутые к камере владельцем.
    /// Размеры — в локальных единицах корня ярлыка (корень масштабируется по дальности камеры).
    /// </summary>
    public sealed class WorldChip
    {
        public Transform Root;
        public SpriteRenderer Plate, Frame, Accent, Icon;
        public TextMesh Text;
        private MeshRenderer _textMr;
        private Color _plateCol, _frameCol, _accentCol, _iconCol, _textCol;
        private float _alpha = 1f;

        /// <summary>Высота пластины, ед.; текст и значок подстраиваются под неё.</summary>
        public float Height = 0.62f;
        public float Width { get; private set; }

        private static Sprite s_fill, s_edge, s_bar;
        private const int TextFontSize = 64;

        public static WorldChip Create(Transform parent, string name, int sortingOrder, bool withAccent, bool withIcon)
        {
            var c = new WorldChip();
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            c.Root = root.transform;

            c.Plate = MakeRenderer(root.transform, "Plate", ChamferSprite(false), sortingOrder);
            c.Plate.drawMode = SpriteDrawMode.Sliced;
            c.Frame = MakeRenderer(root.transform, "Frame", ChamferSprite(true), sortingOrder + 1);
            c.Frame.drawMode = SpriteDrawMode.Sliced;
            if (withAccent) c.Accent = MakeRenderer(root.transform, "Accent", BarSprite(), sortingOrder + 1);
            if (withIcon) c.Icon = MakeRenderer(root.transform, "Icon", null, sortingOrder + 2);

            var tObj = new GameObject("Text");
            tObj.transform.SetParent(root.transform, false);
            c.Text = tObj.AddComponent<TextMesh>();
            c.Text.font = GameFont.Bold;
            c.Text.fontSize = TextFontSize;
            c.Text.anchor = TextAnchor.MiddleLeft;
            c.Text.alignment = TextAlignment.Left;
            c.Text.richText = false;
            c._textMr = tObj.GetComponent<MeshRenderer>();
            if (c.Text.font != null) c._textMr.sharedMaterial = c.Text.font.material;
            c._textMr.sortingOrder = sortingOrder + 2;
            var nc = UIManager.DS.NeonCyan;
            c.SetColors(new Color(0.02f, 0.05f, 0.07f, 0.82f), new Color(nc.r, nc.g, nc.b, 0.6f), nc, nc, Color.white);
            return c;
        }

        private static SpriteRenderer MakeRenderer(Transform parent, string name, Sprite sp, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.sortingOrder = order;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        public void SetColors(Color plate, Color frame, Color accent, Color icon, Color text)
        {
            _plateCol = plate; _frameCol = frame; _accentCol = accent; _iconCol = icon; _textCol = text;
            ApplyAlpha();
        }

        public void SetIcon(LGIcon icon)
        {
            if (Icon != null) Icon.sprite = LGIcons.Get(icon);
        }

        /// <summary>Текст и раскладка: [полоска] [значок] текст, пластина — по ширине содержимого.</summary>
        public void Set(string text, float textHeight = 0.36f)
        {
            if (Text.text != text) Text.text = text;
            float h = Height;
            float charSize = textHeight / (TextFontSize * 0.1f) * 1.25f;   // высота строки TextMesh ≈ fontSize·characterSize/10
            Text.characterSize = charSize;

            float pad = h * 0.32f;
            float x = pad + (Accent != null ? h * 0.12f : 0f);
            if (Icon != null && Icon.sprite != null)
            {
                float isz = h * 0.62f;
                float spriteW = Icon.sprite.bounds.size.x;
                Icon.transform.localScale = Vector3.one * (isz / Mathf.Max(0.0001f, spriteW));
                Icon.transform.localPosition = new Vector3(x + isz * 0.5f, 0f, -0.01f);
                x += isz + h * 0.2f;
            }
            float tw = MeasureText(text, charSize);
            Text.transform.localPosition = new Vector3(x, 0f, -0.01f);
            x += tw + pad;

            Width = x;
            Plate.size = new Vector2(Width, h);
            Plate.transform.localPosition = new Vector3(Width * 0.5f, 0f, 0f);
            Frame.size = new Vector2(Width, h);
            Frame.transform.localPosition = new Vector3(Width * 0.5f, 0f, -0.004f);
            if (Accent != null)
            {
                float bw = h * 0.07f, bh = h * 0.56f;
                Accent.transform.localPosition = new Vector3(h * 0.16f, 0f, -0.005f);
                Accent.transform.localScale = new Vector3(bw / Mathf.Max(0.0001f, Accent.sprite.bounds.size.x), bh / Mathf.Max(0.0001f, Accent.sprite.bounds.size.y), 1f);
            }
        }

        /// <summary>Сдвинуть содержимое так, чтобы ярлык был по центру корня (по умолчанию он растёт вправо).</summary>
        public void CenterOn(float x, float y)
        {
            foreach (Transform t in Root) t.localPosition = new Vector3(t.localPosition.x - Width * 0.5f + x, y, t.localPosition.z);
        }

        public void SetAlpha(float a)
        {
            if (Mathf.Approximately(_alpha, a)) return;
            _alpha = a;
            ApplyAlpha();
        }

        private void ApplyAlpha()
        {
            Plate.color = new Color(_plateCol.r, _plateCol.g, _plateCol.b, _plateCol.a * _alpha);
            Frame.color = new Color(_frameCol.r, _frameCol.g, _frameCol.b, _frameCol.a * _alpha);
            if (Accent != null) Accent.color = new Color(_accentCol.r, _accentCol.g, _accentCol.b, _accentCol.a * _alpha);
            if (Icon != null) Icon.color = new Color(_iconCol.r, _iconCol.g, _iconCol.b, _iconCol.a * _alpha);
            Text.color = new Color(_textCol.r, _textCol.g, _textCol.b, _textCol.a * _alpha);
            bool on = _alpha > 0.01f;
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
        }

        private static float MeasureText(string s, float charSize)
        {
            var font = GameFont.Bold;
            if (font == null || string.IsNullOrEmpty(s)) return 0f;
            font.RequestCharactersInTexture(s, TextFontSize, FontStyle.Normal);
            float adv = 0f;
            foreach (char ch in s)
                if (font.GetCharacterInfo(ch, out var ci, TextFontSize, FontStyle.Normal)) adv += ci.advance;
            return adv * charSize * 0.1f;
        }

        // ---------------------------------------------------------------- Спрайты

        /// <summary>
        /// Пластина 64×32 со срезанными углами (слева сверху и справа снизу), девятисрезная:
        /// edgeOnly = false — сплошная заливка (чуть плотнее к верху), true — только кромка 1.5 px.
        /// Цвет задаёт SpriteRenderer, поэтому заливка и кромка — два слоя.
        /// </summary>
        private static Sprite ChamferSprite(bool edgeOnly)
        {
            if (!edgeOnly && s_fill != null) return s_fill;
            if (edgeOnly && s_edge != null) return s_edge;
            const int W = 64, H = 32, cut = 9;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = edgeOnly ? "WorldChipEdge" : "WorldChipFill", hideFlags = HideFlags.DontSave };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float d = Mathf.Min(Mathf.Min(fx, W - fx), Mathf.Min(fy, H - fy));          // до ближайшего края, px
                d = Mathf.Min(d, (fx + (H - fy) - cut) * 0.7071f);                           // срез слева сверху
                d = Mathf.Min(d, ((W - fx) + fy - cut) * 0.7071f);                           // срез справа снизу
                float inside = Mathf.Clamp01(d + 0.5f);
                float a = edgeOnly
                    ? inside * Mathf.Clamp01(1.75f - Mathf.Abs(d - 1.0f))
                    : inside * Mathf.Lerp(0.85f, 1f, fy / H);
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            var sp = UnityEngine.Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 48f, 0, SpriteMeshType.FullRect, new Vector4(12, 12, 12, 12));   // рамки 0.25 ед. — помещаются в пластину от 0.5 ед.
            sp.hideFlags = HideFlags.DontSave;
            if (edgeOnly) s_edge = sp; else s_fill = sp;
            return sp;
        }

        private static Sprite BarSprite()
        {
            if (s_bar != null) return s_bar;
            var tex = new Texture2D(4, 16, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "WorldChipBar", hideFlags = HideFlags.DontSave };
            var px = new Color32[4 * 16];
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 4; x++)
                px[y * 4 + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Sin((y + 0.5f) / 16f * Mathf.PI)));
            tex.SetPixels32(px);
            tex.Apply(false);
            s_bar = UnityEngine.Sprite.Create(tex, new Rect(0, 0, 4, 16), new Vector2(0.5f, 0.5f), 16);
            s_bar.hideFlags = HideFlags.DontSave;
            return s_bar;
        }
    }
}
