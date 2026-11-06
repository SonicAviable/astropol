using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Плашка выделенного флота (вариант «Орбита»): капсула [значок в круге] имя | +N, уголок вниз на корабль,
    /// по рамке бежит блик. Появляется «пружиной» снизу, исчезает коротким затуханием.
    /// Корень смотрит в камеру (его поворачивает FleetView), все координаты — от центра капсулы.
    /// </summary>
    public sealed class FleetPill
    {
        public Transform Root;
        public const float H = 0.5f;
        public float Width { get; private set; }
        /// <summary>Кончик уголка в локальных координатах корня — от него вниз идёт стебель к кораблю.</summary>
        public Vector3 TipLocal => new Vector3(0f, -H * 0.5f - CaretH, 0f);
        public float Alpha => _alpha;
        public bool Shown => _shown;

        private const float CaretH = 0.1f, CaretW = 0.2f;
        private const int FontSize = 64;
        private Transform _body;
        private SpriteRenderer _glow, _plate, _edge, _iconBg, _icon, _sep, _caretFill, _caretEdge, _shineA, _shineB;
        private TextMesh _name, _count;
        private MeshRenderer _nameMr, _countMr;
        private Color _accent, _plateCol, _textCol;
        private string _label;
        private int _more = -1;
        private bool _shown;
        private float _t, _alpha, _anim, _shineT;

        private static Sprite s_capsule, s_capsuleEdge, s_disc, s_dot, s_px, s_caretFill, s_caretEdge;

        public static FleetPill Create(Transform parent, Color accent, LGIcon icon, int order)
        {
            var p = new FleetPill { _accent = accent };
            p.Root = new GameObject("FleetPill").transform;
            p.Root.SetParent(parent, false);
            p._body = new GameObject("Body").transform;
            p._body.SetParent(p.Root, false);

            p._glow = Spr(p._body, "Glow", WorldChip.GlowSprite(), order - 1, true);
            p._plate = Spr(p._body, "Plate", Capsule(false), order, true);
            p._edge = Spr(p._body, "Edge", Capsule(true), order + 1, true);
            p._caretFill = Spr(p._body, "CaretFill", Caret(false), order + 2, false);
            p._caretEdge = Spr(p._body, "CaretEdge", Caret(true), order + 3, false);
            p._iconBg = Spr(p._body, "IconBg", Disc(), order + 2, false);
            p._icon = Spr(p._body, "Icon", LGIcons.Get(icon), order + 3, false);
            p._sep = Spr(p._body, "Sep", Px(), order + 2, false);
            p._shineB = Spr(p._body, "ShineB", Dot(), order + 4, false);
            p._shineA = Spr(p._body, "ShineA", Dot(), order + 4, false);
            p._name = Text(p._body, "Name", order + 3, out p._nameMr);
            p._count = Text(p._body, "Count", order + 3, out p._countMr);

            p._plateCol = new Color(0.03f, 0.065f, 0.085f, 0.86f);
            p._textCol = new Color(0.92f, 0.97f, 1f);
            p.Root.gameObject.SetActive(false);
            return p;
        }

        public void SetIcon(LGIcon icon) => _icon.sprite = LGIcons.Get(icon);

        /// <summary>Текст и раскладка; пересобирается только при смене имени или числа.</summary>
        public void Set(string label, int more)
        {
            if (label == _label && more == _more) return;
            _label = label; _more = more;

            float th = 0.27f, cs = th / (FontSize * 0.1f) * 1.25f;
            _name.characterSize = cs;
            _name.text = label;
            float tw = WorldChip.MeasureLine(label, FontSize, cs, FontStyle.Normal);

            bool extra = more > 0;
            float cs2 = 0.23f / (FontSize * 0.1f) * 1.25f;
            string cnt = extra ? $"+{more}" : "";
            _count.characterSize = cs2;
            _count.text = cnt;
            float cw = extra ? WorldChip.MeasureLine(cnt, FontSize, cs2, FontStyle.Normal) : 0f;

            const float padL = 0.06f, iconD = 0.38f, gap = 0.12f, padR = 0.2f;
            Width = padL + iconD + gap + tw + (extra ? gap * 2f + cw : 0f) + padR;
            float x = -Width * 0.5f + padL;

            _iconBg.transform.localPosition = new Vector3(x + iconD * 0.5f, 0f, -0.004f);
            _iconBg.transform.localScale = Vector3.one * (iconD / s_disc.bounds.size.x);
            float isz = iconD * 0.6f;
            if (_icon.sprite != null)
                _icon.transform.localScale = Vector3.one * (isz / Mathf.Max(0.0001f, _icon.sprite.bounds.size.x));
            _icon.transform.localPosition = new Vector3(x + iconD * 0.5f, 0f, -0.008f);
            x += iconD + gap;

            _name.transform.localPosition = new Vector3(x, 0f, -0.01f);
            x += tw;
            _sep.gameObject.SetActive(extra);
            _count.gameObject.SetActive(extra);
            if (extra)
            {
                x += gap;
                float px = Px().bounds.size.x;
                _sep.transform.localPosition = new Vector3(x, 0f, -0.006f);
                _sep.transform.localScale = new Vector3(0.012f / px, 0.22f / px, 1f);
                x += gap;
                _count.transform.localPosition = new Vector3(x, 0f, -0.01f);
            }

            _plate.size = _edge.size = new Vector2(Width, H);
            _plate.transform.localPosition = Vector3.zero;
            _edge.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            float gp = 0.34f;
            _glow.size = new Vector2(Width + gp * 2f, H + gp * 2f);
            _glow.transform.localPosition = new Vector3(0f, 0f, 0.004f);

            float cy = -H * 0.5f - CaretH * 0.5f + 0.012f;
            Vector3 cs3 = new Vector3(CaretW / s_caretFill.bounds.size.x, (CaretH + 0.024f) / s_caretFill.bounds.size.y, 1f);
            _caretFill.transform.localPosition = new Vector3(0f, cy, -0.012f);
            _caretFill.transform.localScale = cs3;
            _caretEdge.transform.localPosition = new Vector3(0f, cy, -0.013f);
            _caretEdge.transform.localScale = cs3;
        }

        /// <summary>Показать/скрыть с анимацией; delay — пауза перед появлением (кольцо успевает начать прорисовку).</summary>
        public void Show(bool on, float delay = 0f)
        {
            if (on == _shown) return;
            _shown = on;
            _t = on ? -delay : 0f;
            if (on) Root.gameObject.SetActive(true);
        }

        public void Tick(float dt)
        {
            if (!Root.gameObject.activeSelf) return;
            _t += dt;
            float scale, y, a;
            if (_shown)
            {
                float p = Mathf.Clamp01(_t / 0.45f);
                float e = BackOut(p);
                a = Mathf.Clamp01(p * 2.4f);
                scale = 0.82f + 0.18f * e;
                y = -0.24f * (1f - e);
                _anim = a;
            }
            else
            {
                float k = Mathf.Clamp01(_t / 0.2f);
                a = _anim * (1f - k);
                scale = 1f - 0.08f * k;
                y = 0f;
                if (k >= 1f) { Root.gameObject.SetActive(false); _alpha = 0f; return; }
            }
            _body.localPosition = new Vector3(0f, y, 0f);
            _body.localScale = Vector3.one * scale;
            _alpha = a;

            _shineT = (_shineT + dt / 4f) % 1f;
            PlaceShine(_shineA, _shineT, 1f);
            PlaceShine(_shineB, (_shineT + 0.97f) % 1f, 0.45f);
            ApplyColors();
        }

        private void PlaceShine(SpriteRenderer s, float t, float k)
        {
            float r = H * 0.5f - 0.006f, L = Mathf.Max(0f, Width - H);
            float arc = Mathf.PI * r, P = 2f * L + 2f * arc, d = t * P;
            Vector2 pos; float ang;
            if (d < L) { pos = new Vector2(-L * 0.5f + d, r); ang = 0f; }
            else if ((d -= L) < arc) { float a = Mathf.PI * 0.5f - d / r; pos = new Vector2(L * 0.5f + Mathf.Cos(a) * r, Mathf.Sin(a) * r); ang = a * Mathf.Rad2Deg - 90f; }
            else if ((d -= arc) < L) { pos = new Vector2(L * 0.5f - d, -r); ang = 180f; }
            else { d -= L; float a = -Mathf.PI * 0.5f - d / r; pos = new Vector2(-L * 0.5f + Mathf.Cos(a) * r, Mathf.Sin(a) * r); ang = a * Mathf.Rad2Deg - 90f; }
            s.transform.localPosition = new Vector3(pos.x, pos.y, -0.003f);
            s.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
            float sz = s_dot.bounds.size.x;
            s.transform.localScale = new Vector3(0.42f * k / sz, 0.07f / sz, 1f);
        }

        private void ApplyColors()
        {
            float a = _alpha;
            _plate.color = Mul(_plateCol, a);
            _caretFill.color = Mul(new Color(_plateCol.r, _plateCol.g, _plateCol.b, 0.95f), a);
            _edge.color = Mul(new Color(_accent.r, _accent.g, _accent.b, 0.32f), a);
            _caretEdge.color = Mul(new Color(_accent.r, _accent.g, _accent.b, 0.65f), a);
            _glow.color = Mul(new Color(_accent.r, _accent.g, _accent.b, 0.16f), a);
            _iconBg.color = Mul(new Color(_accent.r, _accent.g, _accent.b, 0.17f), a);
            _icon.color = Mul(_accent, a);
            _sep.color = Mul(new Color(_accent.r, _accent.g, _accent.b, 0.35f), a);
            _name.color = Mul(_textCol, a);
            Color v = Color.Lerp(_accent, Color.white, 0.2f);
            _count.color = Mul(v, a);
            Color sh = Color.Lerp(_accent, Color.white, 0.45f);
            _shineA.color = Mul(sh, a);
            _shineB.color = Mul(sh, a * 0.6f);
        }

        private static Color Mul(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        private static float BackOut(float t)
        {
            const float c1 = 1.9f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        // ---------------------------------------------------------------- Построение

        private static SpriteRenderer Spr(Transform parent, string name, Sprite sp, int order, bool sliced)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.sortingOrder = order;
            if (sliced) r.drawMode = SpriteDrawMode.Sliced;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private static TextMesh Text(Transform parent, string name, int order, out MeshRenderer mr)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMesh>();
            t.font = GameFont.Bold;
            t.fontSize = FontSize;
            t.anchor = TextAnchor.MiddleLeft;
            t.alignment = TextAlignment.Left;
            t.richText = false;
            mr = go.GetComponent<MeshRenderer>();
            if (t.font != null) mr.sharedMaterial = t.font.material;
            mr.sortingOrder = order;
            return t;
        }

        private static Texture2D Tex(int w, int h, string name) => new Texture2D(w, h, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name, hideFlags = HideFlags.DontSave };

        private static Sprite Finish(Texture2D t, Color32[] px, float ppu, Vector4 border)
        {
            t.SetPixels32(px);
            t.Apply(false);
            var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect, border);
            s.hideFlags = HideFlags.DontSave;
            return s;
        }

        /// <summary>Капсула высотой H (64 px при 128 px/ед.), концы — полукруги в девятисрезе.</summary>
        private static Sprite Capsule(bool edge)
        {
            if (edge ? s_capsuleEdge != null : s_capsule != null) return edge ? s_capsuleEdge : s_capsule;
            const int W = 96, S = 64; const float R = 32f;
            var t = Tex(W, S, edge ? "FleetPillEdge" : "FleetPillFill");
            var px = new Color32[W * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float cx = Mathf.Clamp(fx, R, W - R);
                float d = R - Vector2.Distance(new Vector2(fx, fy), new Vector2(cx, R));   // до края внутрь, px
                float a = edge ? Mathf.Clamp01(1f - Mathf.Abs(d - 1.4f) / 1.2f) : Mathf.Clamp01(d);
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            var s = Finish(t, px, S / H, new Vector4(R, R, R, R));
            if (edge) s_capsuleEdge = s; else s_capsule = s;
            return s;
        }

        private static Sprite Disc()
        {
            if (s_disc != null) return s_disc;
            const int S = 64;
            var t = Tex(S, S, "FleetPillDisc");
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = S * 0.5f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S * 0.5f, S * 0.5f));
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(d) * 255f));
            }
            return s_disc = Finish(t, px, S, Vector4.zero);
        }

        private static Sprite Dot()
        {
            if (s_dot != null) return s_dot;
            const int S = 32;
            var t = Tex(S, S, "FleetPillDot");
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S * 0.5f, S * 0.5f)) / (S * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * a * 255f));
            }
            return s_dot = Finish(t, px, S, Vector4.zero);
        }

        private static Sprite Px()
        {
            if (s_px != null) return s_px;
            var t = Tex(4, 4, "FleetPillPx");
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            return s_px = Finish(t, px, 4, Vector4.zero);
        }

        /// <summary>Уголок вниз: заливка — треугольник, кромка — только две нижние стороны (верх сливается с капсулой).</summary>
        private static Sprite Caret(bool edge)
        {
            if (edge ? s_caretEdge != null : s_caretFill != null) return edge ? s_caretEdge : s_caretFill;
            const int W = 40, S = 24;
            var t = Tex(W, S, edge ? "FleetPillCaretEdge" : "FleetPillCaretFill");
            var px = new Color32[W * S];
            float len = Mathf.Sqrt(W * W * 0.25f + S * S);
            for (int y = 0; y < S; y++)
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f - W * 0.5f, fy = y + 0.5f;      // кончик в (0,0), основание сверху
                float d = (S * Mathf.Abs(fx) * -1f + W * 0.5f * fy) / len;   // >0 — внутри боковых сторон
                float a = edge ? Mathf.Clamp01(1f - Mathf.Abs(d - 1f) / 1.1f) * Mathf.Clamp01((S - 3f - fy) / 3f)
                               : Mathf.Clamp01(d + 0.5f);
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            var s = Finish(t, px, S, Vector4.zero);
            if (edge) s_caretEdge = s; else s_caretFill = s;
            return s;
        }
    }
}
