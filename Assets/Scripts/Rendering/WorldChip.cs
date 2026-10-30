using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Плашки-ярлыки в мире карты (над кораблями, под названиями систем) — в стиле интерфейса:
    /// тёмная полупрозрачная пластина со срезанными углами и тонкой светящейся кромкой, полоска-акцент,
    /// значок и текст шрифтом игры. Всё — SpriteRenderer/TextMesh, повёрнутые к камере владельцем.
    /// Размеры — в локальных единицах корня ярлыка (корень масштабируется по дальности камеры).
    /// Отделка «как в больших стратегиях»: мягкое свечение цвета акцента за пластиной, стеклянный блик сверху,
    /// яркие уголки-скобы, значение после « · » — отдельным цветом за тонким разделителем, полоска прогресса снизу.
    /// </summary>
    public sealed class WorldChip
    {
        public Transform Root;
        public SpriteRenderer Plate, Frame, Accent, Icon;
        public TextMesh Text;
        private MeshRenderer _textMr;
        private Color _plateCol, _frameCol, _accentCol, _iconCol, _textCol;
        private float _alpha = 1f;

        // Отделка: всё содержимое лежит в Body — центрирование двигает его целиком
        private Transform _body;
        private SpriteRenderer _glow, _sheen, _corners, _sep, _barTrack, _barFill;
        private TextMesh _value;
        private MeshRenderer _valueMr;
        private int _order;
        private float _progress = -1f;
        private float _pulse = 1f;

        /// <summary>Сила свечения за пластиной (0 — нет); для важных ярлыков (стройки) — ярче.</summary>
        public float GlowStrength = 0.22f;

        /// <summary>Высота пластины, ед.; текст и значок подстраиваются под неё.</summary>
        public float Height = 0.62f;
        public float Width { get; private set; }

        private static Sprite s_fill, s_edge, s_bar, s_glow, s_sheen, s_corners, s_px;
        private const int TextFontSize = 64;

        public static WorldChip Create(Transform parent, string name, int sortingOrder, bool withAccent, bool withIcon)
        {
            var c = new WorldChip();
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            c.Root = root.transform;
            c._order = sortingOrder;
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            c._body = body;

            c._glow = MakeRenderer(body, "Glow", GlowSprite(), sortingOrder - 1);
            c._glow.drawMode = SpriteDrawMode.Sliced;
            c.Plate = MakeRenderer(body, "Plate", ChamferSprite(false), sortingOrder);
            c.Plate.drawMode = SpriteDrawMode.Sliced;
            c._sheen = MakeRenderer(body, "Sheen", SheenSprite(), sortingOrder);
            c._sheen.drawMode = SpriteDrawMode.Sliced;
            c.Frame = MakeRenderer(body, "Frame", ChamferSprite(true), sortingOrder + 1);
            c.Frame.drawMode = SpriteDrawMode.Sliced;
            c._corners = MakeRenderer(body, "Corners", CornerSprite(), sortingOrder + 1);
            c._corners.drawMode = SpriteDrawMode.Sliced;
            if (withAccent) c.Accent = MakeRenderer(body, "Accent", BarSprite(), sortingOrder + 1);
            if (withIcon) c.Icon = MakeRenderer(body, "Icon", null, sortingOrder + 2);

            c.Text = MakeText(body, "Text", sortingOrder + 2, out c._textMr);
            var nc = UIManager.DS.NeonCyan;
            c.SetColors(new Color(0.02f, 0.05f, 0.07f, 0.82f), new Color(nc.r, nc.g, nc.b, 0.6f), nc, nc, Color.white);
            return c;
        }

        private static TextMesh MakeText(Transform parent, string name, int order, out MeshRenderer mr)
        {
            var tObj = new GameObject(name);
            tObj.transform.SetParent(parent, false);
            var t = tObj.AddComponent<TextMesh>();
            t.font = GameFont.Bold;
            t.fontSize = TextFontSize;
            t.anchor = TextAnchor.MiddleLeft;
            t.alignment = TextAlignment.Left;
            t.richText = false;
            mr = tObj.GetComponent<MeshRenderer>();
            if (t.font != null) mr.sharedMaterial = t.font.material;
            mr.sortingOrder = order;
            return t;
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

        /// <summary>
        /// Текст и раскладка: [полоска] [значок] подпись | значение, пластина — по ширине содержимого.
        /// «ФОРПОСТ · 79 дней» делится на подпись и значение: значение — цветом акцента за тонким разделителем.
        /// </summary>
        public void Set(string text, float textHeight = 0.36f)
        {
            string label = text ?? "", val = null;
            int cut = label.IndexOf(" · ", System.StringComparison.Ordinal);
            if (cut > 0 && cut + 3 < label.Length) { val = label.Substring(cut + 3); label = label.Substring(0, cut); }
            if (Text.text != label) Text.text = label;
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
            float tw = MeasureText(label, charSize);
            Text.transform.localPosition = new Vector3(x, 0f, -0.01f);
            x += tw;

            if (val != null)
            {
                if (_value == null)
                {
                    _value = MakeText(_body, "Value", _order + 2, out _valueMr);
                    _sep = MakeRenderer(_body, "Sep", PixelSprite(), _order + 1);
                }
                _value.characterSize = charSize;
                if (_value.text != val) _value.text = val;
                float sg = h * 0.24f;
                x += sg;
                float sh = h * 0.5f, sw = Mathf.Max(0.012f, h * 0.035f);
                float px = PixelSprite().bounds.size.x;
                _sep.transform.localPosition = new Vector3(x, 0f, -0.006f);
                _sep.transform.localScale = new Vector3(sw / px, sh / px, 1f);
                x += sg;
                _value.transform.localPosition = new Vector3(x, 0f, -0.01f);
                x += MeasureText(val, charSize);
            }
            if (_value != null)
            {
                bool on = val != null;
                if (_value.gameObject.activeSelf != on) _value.gameObject.SetActive(on);
                if (_sep.gameObject.activeSelf != on) _sep.gameObject.SetActive(on);
            }
            x += pad;

            Width = x;
            Plate.size = new Vector2(Width, h);
            Plate.transform.localPosition = new Vector3(Width * 0.5f, 0f, 0f);
            _sheen.size = new Vector2(Width, h);
            _sheen.transform.localPosition = new Vector3(Width * 0.5f, 0f, -0.002f);
            Frame.size = new Vector2(Width, h);
            Frame.transform.localPosition = new Vector3(Width * 0.5f, 0f, -0.004f);
            _corners.size = new Vector2(Width, h);
            _corners.transform.localPosition = new Vector3(Width * 0.5f, 0f, -0.005f);
            float gp = Mathf.Max(h * 0.55f, 0.34f);   // свечение выходит за пластину (не меньше двух рамок девятисреза)
            _glow.size = new Vector2(Width + gp * 2f, h + gp * 2f);
            _glow.transform.localPosition = new Vector3(Width * 0.5f, 0f, 0.004f);
            if (Accent != null)
            {
                float bw = h * 0.07f, bh = h * 0.56f;
                Accent.transform.localPosition = new Vector3(h * 0.16f, 0f, -0.005f);
                Accent.transform.localScale = new Vector3(bw / Mathf.Max(0.0001f, Accent.sprite.bounds.size.x), bh / Mathf.Max(0.0001f, Accent.sprite.bounds.size.y), 1f);
            }
            LayoutBar();
            ApplyAlpha();
        }

        /// <summary>Сдвинуть содержимое так, чтобы ярлык был по центру корня (по умолчанию он растёт вправо).</summary>
        public void CenterOn(float x, float y)
        {
            _body.localPosition = new Vector3(x - Width * 0.5f, y, 0f);
        }

        /// <summary>Полоска прогресса по низу пластины: 0..1; меньше нуля — скрыть.</summary>
        public void SetProgress(float p)
        {
            p = p < 0f ? -1f : Mathf.Clamp01(p);
            if (Mathf.Abs(p - _progress) < 0.002f) return;
            if (_barTrack == null && p >= 0f)
            {
                _barTrack = MakeRenderer(_body, "BarTrack", PixelSprite(), _order + 1);
                _barFill = MakeRenderer(_body, "BarFill", PixelSprite(), _order + 1);
            }
            _progress = p;
            LayoutBar();
            ApplyAlpha();
        }

        /// <summary>«Дыхание» свечения у активной стройки; вызывать каждый кадр, пока ярлык виден.</summary>
        public void Pulse(float time)
        {
            float k = 0.72f + 0.28f * Mathf.Sin(time * 2.4f);
            if (Mathf.Abs(k - _pulse) < 0.01f) return;
            _pulse = k;
            ApplyGlow();
        }

        private void LayoutBar()
        {
            if (_barTrack == null) return;
            bool on = _progress >= 0f && Width > 0f;
            if (_barTrack.gameObject.activeSelf != on) _barTrack.gameObject.SetActive(on);
            if (_barFill.gameObject.activeSelf != on) _barFill.gameObject.SetActive(on);
            if (!on) return;
            float h = Height;
            float x0 = h * 0.30f, x1 = Width - h * 0.42f;   // правый нижний угол срезан — полоска короче справа
            float len = Mathf.Max(0.01f, x1 - x0);
            float th = Mathf.Max(0.014f, h * 0.06f), y = -h * 0.5f + h * 0.13f;
            float px = PixelSprite().bounds.size.x;
            _barTrack.transform.localPosition = new Vector3(x0 + len * 0.5f, y, -0.006f);
            _barTrack.transform.localScale = new Vector3(len / px, th / px, 1f);
            float fl = Mathf.Max(0.0001f, len * _progress);
            _barFill.transform.localPosition = new Vector3(x0 + fl * 0.5f, y, -0.007f);
            _barFill.transform.localScale = new Vector3(fl / px, th / px, 1f);
        }

        /// <summary>Курсор над ярлыком: экранный прямоугольник пластины (по углам её габаритов).</summary>
        public bool ContainsScreenPoint(Camera cam, Vector2 screen)
        {
            if (cam == null || Plate == null || !Plate.gameObject.activeInHierarchy || _alpha < 0.1f) return false;
            var b = Plate.bounds;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var sp = cam.WorldToScreenPoint(c);
                if (sp.z <= 0f) return false;
                minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
                minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
            }
            return screen.x >= minX && screen.x <= maxX && screen.y >= minY && screen.y <= maxY;
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
            // Значение — цветом акцента, высветленным для читаемости; разделитель — цветом акцента потише
            if (_value != null)
            {
                Color v = Color.Lerp(_accentCol, Color.white, 0.38f);
                _value.color = new Color(v.r, v.g, v.b, _textCol.a * _alpha);
                _sep.color = new Color(_accentCol.r, _accentCol.g, _accentCol.b, 0.55f * _accentCol.a * _alpha);
            }
            // Блик — белый, едва заметный; скобы — яркий акцент (у блёклых ярлыков — тоже блёклые)
            _sheen.color = new Color(1f, 1f, 1f, 0.10f * _plateCol.a * _alpha);
            Color cc = Color.Lerp(_accentCol, Color.white, 0.2f);
            _corners.color = new Color(cc.r, cc.g, cc.b, Mathf.Clamp01(_frameCol.a * 1.4f) * _alpha);
            if (_barTrack != null)
            {
                _barTrack.color = new Color(_accentCol.r, _accentCol.g, _accentCol.b, 0.20f * _alpha);
                Color bf = Color.Lerp(_accentCol, Color.white, 0.25f);
                _barFill.color = new Color(bf.r, bf.g, bf.b, 0.95f * _alpha);
            }
            ApplyGlow();
            bool on = _alpha > 0.01f;
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
        }

        /// <summary>Ширина строки TextMesh в его локальных единицах (для подписей с другим кеглем и начертанием).</summary>
        public static float MeasureLine(string s, int fontSize, float charSize, FontStyle style)
        {
            var font = GameFont.Bold;
            if (font == null || string.IsNullOrEmpty(s)) return 0f;
            font.RequestCharactersInTexture(s, fontSize, style);
            float adv = 0f;
            foreach (char ch in s)
                if (font.GetCharacterInfo(ch, out var ci, fontSize, style)) adv += ci.advance;
            return adv * charSize * 0.1f;
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

        private void ApplyGlow()
        {
            _glow.color = new Color(_accentCol.r, _accentCol.g, _accentCol.b, GlowStrength * _pulse * Mathf.Clamp01(_frameCol.a * 1.4f) * _alpha);
        }

        // ---------------------------------------------------------------- Спрайты

        private static Sprite PixelSprite()
        {
            if (s_px != null) return s_px;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "WorldChipPx", hideFlags = HideFlags.DontSave };
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false);
            s_px = UnityEngine.Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            s_px.hideFlags = HideFlags.DontSave;
            return s_px;
        }

        /// <summary>Мягкое свечение: размытый прямоугольник, девятисрезный — растягивается под любую ширину.</summary>
        public static Sprite GlowSprite()
        {
            if (s_glow != null) return s_glow;
            const int S = 64, B = 26;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "WorldChipGlow", hideFlags = HideFlags.DontSave };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Min(Mathf.Min(x + 0.5f, S - x - 0.5f), Mathf.Min(y + 0.5f, S - y - 0.5f));   // до края, px
                float t = Mathf.Clamp01(d / B);
                float a = t * t * (3f - 2f * t);
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            s_glow = UnityEngine.Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 48f, 0, SpriteMeshType.FullRect, new Vector4(B, B, B, B));
            s_glow.hideFlags = HideFlags.DontSave;
            return s_glow;
        }

        /// <summary>Стеклянный блик: светлая верхняя часть пластины и тонкая световая линия у верхней кромки.</summary>
        private static Sprite SheenSprite()
        {
            if (s_sheen != null) return s_sheen;
            const int W = 64, H = 32, cut = 9;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "WorldChipSheen", hideFlags = HideFlags.DontSave };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float d = Mathf.Min(Mathf.Min(fx, W - fx), Mathf.Min(fy, H - fy));
                d = Mathf.Min(d, (fx + (H - fy) - cut) * 0.7071f);
                d = Mathf.Min(d, ((W - fx) + fy - cut) * 0.7071f);
                float inside = Mathf.Clamp01(d - 1.5f);
                float v = fy / H;                                   // 0 — низ, 1 — верх
                float a = inside * (Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.95f, v)) + (v > 0.86f && v < 0.93f ? 0.9f : 0f));
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            s_sheen = UnityEngine.Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 48f, 0, SpriteMeshType.FullRect, new Vector4(12, 12, 12, 12));
            s_sheen.hideFlags = HideFlags.DontSave;
            return s_sheen;
        }

        /// <summary>
        /// Уголки-скобы: яркие засечки на прямых углах (справа сверху, слева снизу) и вдоль срезов —
        /// углы в девятисрезе не растягиваются, середина пустая.
        /// </summary>
        private static Sprite CornerSprite()
        {
            if (s_corners != null) return s_corners;
            const int W = 64, H = 32, cut = 9, L = 9;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "WorldChipCorners", hideFlags = HideFlags.DontSave };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float a = 0f;
                // справа сверху — «┐»
                if (fx > W - L && fy > H - 2.2f) a = 1f;
                if (fy > H - L && fx > W - 2.2f) a = 1f;
                // слева снизу — «└»
                if (fx < L && fy < 2.2f) a = 1f;
                if (fy < L && fx < 2.2f) a = 1f;
                // вдоль срезов — яркие черты
                float dTL = Mathf.Abs(fx + (H - fy) - cut) * 0.7071f;
                if (dTL < 1.1f && fx < cut) a = Mathf.Max(a, 1f - dTL / 1.1f);
                float dBR = Mathf.Abs((W - fx) + fy - cut) * 0.7071f;
                if (dBR < 1.1f && W - fx < cut) a = Mathf.Max(a, 1f - dBR / 1.1f);
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            s_corners = UnityEngine.Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 48f, 0, SpriteMeshType.FullRect, new Vector4(12, 12, 12, 12));
            s_corners.hideFlags = HideFlags.DontSave;
            return s_corners;
        }

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
