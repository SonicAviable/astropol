using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Превращает обычный Image (без спрайта) в элемент Liquid Glass.
    /// Цвет Image = оттенок стекла, альфа = общая непрозрачность.
    /// Если на объекте есть Outline — он отключается, а его цвет/толщина становятся
    /// неоновой обводкой стекла (код, который анимирует Outline, продолжает работать).
    ///
    /// Роль Auto определяет вид по геометрии и контексту:
    ///   тонкая полоса → неоновая линия; маленький квадрат → светящаяся точка;
    ///   кнопка → стеклянная капсула; верхнеуровневая панель → стекло с блюром;
    ///   вложенный контейнер → полупрозрачная пластина; яркая узкая полоса → заливка-прогресс;
    ///   полноэкранный фон → размытый скрим.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class LiquidGlassEffect : BaseMeshEffect
    {
        public enum Role { Auto, Glass, Platter, Header, Button, Field, Chip, Line, Dot, Fill, Scrim, Border }

        [SerializeField] private Role role = Role.Auto;
        [Tooltip("Радиус скругления; < 0 — автоматически по роли и размеру.")]
        [SerializeField] private float radius = -1f;
        [SerializeField] private bool useRimOverride;
        [SerializeField] private Color rimOverride = new Color(1f, 1f, 1f, 0.2f);
        [SerializeField] private float fillMultiplier = 1f;
        [SerializeField] private float glowMultiplier = 1f;
        [SerializeField] private float shadowMultiplier = 1f;
        [SerializeField] private float specularMultiplier = 1f;
        [SerializeField, Range(0f, 1f)] private float intensity = 0.5f;
        [Tooltip("Доп. тень для элементов, \"парящих\" над соседями (поповеры внутри окон).")]
        [SerializeField, Range(0f, 1f)] private float elevation;
        [SerializeField] private bool forceHairline;

        private float _hover;
        private float _pulse;
        private Image _image;
        private Selectable _selectable;
        private Outline _outline;
        private Color _outlineColor;
        private Vector2 _outlineDistance;
        private float _outlineBaseDistance = 1f;

        private static readonly List<LiquidGlassEffect> s_All = new List<LiquidGlassEffect>(256);
        private static readonly Vector3[] s_Corners = new Vector3[4];
        private static readonly Vector3[] s_ParentCorners = new Vector3[4];

        private const AdditionalCanvasShaderChannels RequiredChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3;

        // ---------------------------------------------------------------- API

        public Role StyleRole
        {
            get => role;
            set { if (role == value) return; role = value; Dirty(); }
        }

        /// <summary>Радиус скругления (&lt; 0 — авто).</summary>
        public float Radius
        {
            get => radius;
            set { if (Mathf.Approximately(radius, value)) return; radius = value; Dirty(); }
        }

        public float FillMultiplier { get => fillMultiplier; set { fillMultiplier = value; Dirty(); } }
        public float GlowMultiplier { get => glowMultiplier; set { glowMultiplier = value; Dirty(); } }
        public float ShadowMultiplier { get => shadowMultiplier; set { shadowMultiplier = value; Dirty(); } }
        public float SpecularMultiplier { get => specularMultiplier; set { specularMultiplier = value; Dirty(); } }
        public float Intensity { get => intensity; set { intensity = Mathf.Clamp01(value); Dirty(); } }
        public bool Hairline { get => forceHairline; set { forceHairline = value; Dirty(); } }
        public float Elevation { get => elevation; set { elevation = Mathf.Clamp01(value); Dirty(); } }

        public void SetRim(Color c)
        {
            if (useRimOverride && rimOverride == c) return;
            useRimOverride = true;
            rimOverride = c;
            Dirty();
        }

        public void ClearRim()
        {
            if (!useRimOverride) return;
            useRimOverride = false;
            Dirty();
        }

        /// <summary>0..1 — подсветка при наведении (неон + блик под курсором).</summary>
        public void SetHover(float h)
        {
            h = Mathf.Clamp01(h);
            if (Mathf.Abs(_hover - h) < 0.002f) return;
            _hover = h;
            Dirty();
        }

        /// <summary>0..1 — дополнительная "пульсация" свечения (активные элементы, уведомления).</summary>
        public void SetPulse(float p)
        {
            p = Mathf.Clamp01(p);
            if (Mathf.Abs(_pulse - p) < 0.004f) return;
            _pulse = p;
            Dirty();
        }

        public Role ResolvedRole => ResolveRole();

        // ---------------------------------------------------------------- Lifecycle

        protected override void OnEnable()
        {
            CacheComponents();
            ApplyMaterial();
            BindOutline();
            EnsureCanvasChannels();
            if (!s_All.Contains(this)) s_All.Add(this);
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            s_All.Remove(this);
            // Сам компонент выключили (а не весь объект) — возвращаем обычный вид.
            if (gameObject.activeInHierarchy)
            {
                if (graphic != null && graphic.material == LiquidGlassResources.UIMaterial)
                    graphic.material = null;
                if (_outline != null) _outline.enabled = true;
            }
            base.OnDisable();
        }

        private void CacheComponents()
        {
            if (_image == null) _image = GetComponent<Image>();
            if (_selectable == null) _selectable = GetComponent<Selectable>();
        }

        private void ApplyMaterial()
        {
            var mat = LiquidGlassResources.UIMaterial;
            if (graphic != null && mat != null && graphic.material != mat)
                graphic.material = mat;
        }

        private void BindOutline()
        {
            if (_outline == null) _outline = GetComponent<Outline>();
            if (_outline == null) return;
            _outline.enabled = false;
            _outlineColor = _outline.effectColor;
            _outlineDistance = _outline.effectDistance;
            _outlineBaseDistance = Mathf.Max(0.25f, Mathf.Max(Mathf.Abs(_outlineDistance.x), Mathf.Abs(_outlineDistance.y)));
        }

        private void EnsureCanvasChannels()
        {
            var g = graphic;
            if (g == null) return;
            var c = g.canvas;
            if (c == null) return;
            if ((c.additionalShaderChannels & RequiredChannels) != RequiredChannels)
                c.additionalShaderChannels |= RequiredChannels;
            var root = c.rootCanvas;
            if (root != null && root != c && (root.additionalShaderChannels & RequiredChannels) != RequiredChannels)
                root.additionalShaderChannels |= RequiredChannels;
        }

        private void Dirty()
        {
            if (graphic != null && isActiveAndEnabled) graphic.SetVerticesDirty();
        }

        /// <summary>Опрос раз в кадр (вызывает LiquidGlassSystem).</summary>
        internal static void TickAll()
        {
            for (int i = s_All.Count - 1; i >= 0; i--)
            {
                var fx = s_All[i];
                if (fx == null) { s_All.RemoveAt(i); continue; }
                fx.Tick();
            }
        }

        private void Tick()
        {
            // Кому-то назначили спрайт (водяные знаки, иконки) — это уже не стекло.
            if (_image != null && _image.sprite != null)
            {
                enabled = false;
                return;
            }

            if (_outline != null)
            {
                if (_outline.enabled) _outline.enabled = false;
                if (_outline.effectColor != _outlineColor || _outline.effectDistance != _outlineDistance)
                {
                    _outlineColor = _outline.effectColor;
                    _outlineDistance = _outline.effectDistance;
                    Dirty();
                }
            }
        }

        // ---------------------------------------------------------------- Style

        private struct Style
        {
            public int Mode;          // 0 glass, 1 platter, 2 emissive, 3 scrim
            public Vector4 Radii;     // TL TR BR BL
            public float Fill, Glow, Shadow, Specular, Refraction, Border, GlowRadius, ShadowRadius;
            public Color Rim;
            public int Flags;         // 1 hairline, 2 border-only, 4 no-specular
            public bool ForceOpaqueVertex;
        }

        private Role ResolveRole()
        {
            if (role != Role.Auto) return role;

            var rt = rectTransform;
            Rect r = rt.rect;
            float minS = Mathf.Min(r.width, r.height);
            float maxS = Mathf.Max(r.width, r.height);
            Color c = graphic != null ? graphic.color : Color.white;

            if (c.a < 0.02f) return Role.Border;
            if (minS <= 4.5f) return Role.Line;
            if (maxS <= 12.5f) return Role.Dot;

            CacheComponents();
            if (_selectable is InputField) return Role.Field;
            if (_selectable != null) return Role.Button;
            if (CoversScreen(r)) return Role.Scrim;
            if (IsBright(c) && (minS <= 18f || transform.childCount == 0)) return Role.Fill;

            var parent = FindParentSurface();
            if (parent == null) return Role.Glass;
            return Role.Platter;
        }

        private RectTransform rectTransform => (RectTransform)transform;

        private bool CoversScreen(Rect r)
        {
            var g = graphic;
            var c = g != null ? g.canvas : null;
            if (c == null) return false;
            var root = c.rootCanvas != null ? c.rootCanvas.transform as RectTransform : null;
            if (root == null) return false;
            Rect rr = root.rect;
            return r.width >= rr.width * 0.95f && r.height >= rr.height * 0.95f;
        }

        private static bool IsBright(Color c)
        {
            float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            return c.a >= 0.5f && mx >= 0.45f && (mx - mn) >= 0.25f;
        }

        /// <summary>Ближайший стеклянный родитель-поверхность (до границы Canvas).</summary>
        private LiquidGlassEffect FindParentSurface()
        {
            Transform t = transform.parent;
            while (t != null)
            {
                if (t.TryGetComponent<LiquidGlassEffect>(out var fx) && fx.enabled)
                {
                    var rr = fx.ResolveRole();
                    if (rr == Role.Scrim) return null;
                    if (rr != Role.Line && rr != Role.Dot && rr != Role.Fill && rr != Role.Border)
                        return fx;
                }
                if (t.TryGetComponent<Canvas>(out _)) return null;
                t = t.parent;
            }
            return null;
        }

        private float BaseRadius(Role r, float w, float h)
        {
            float br = RawRadius(r, w, h);
            // Линии и точки остаются круглыми; всё остальное — сдержанные, «приборные» углы
            if (r == Role.Line || r == Role.Dot) return br;
            return Mathf.Min(br * LGTone.RadiusScale, LGTone.MaxRadius);
        }

        private float RawRadius(Role r, float w, float h)
        {
            float minS = Mathf.Min(w, h);
            float maxS = Mathf.Max(w, h);
            if (radius >= 0f) return Mathf.Min(radius, minS * 0.5f);
            switch (r)
            {
                case Role.Glass:   return Mathf.Min(minS * 0.5f, maxS >= 500f ? 24f : 18f);
                case Role.Platter:
                case Role.Header:  return Mathf.Clamp(minS * 0.25f, 6f, 14f);
                case Role.Button:  return h <= 48f ? h * 0.5f : 14f;
                case Role.Field:
                case Role.Chip:    return minS * 0.5f;
                case Role.Line:
                case Role.Dot:     return minS * 0.5f;
                case Role.Fill:    return Mathf.Min(minS * 0.5f, 8f);
                case Role.Border:  return Mathf.Min(minS * 0.25f, 10f);
                case Role.Scrim:   return 0f;
            }
            return 12f;
        }

        /// <summary>Радиусы углов (TL TR BR BL) — нужны детям для подгонки "впритык" к краям.</summary>
        internal Vector4 ResolveRadii()
        {
            Rect r = rectTransform.rect;
            float br = BaseRadius(ResolveRole(), r.width, r.height);
            return new Vector4(br, br, br, br);
        }

        private Style Resolve(Rect r, Color tint)
        {
            var rr = ResolveRole();
            float w = r.width, h = r.height;
            float minS = Mathf.Min(w, h), maxS = Mathf.Max(w, h);
            float br = BaseRadius(rr, w, h);

            bool hasOutline = _outline != null;
            Color outlineCol = hasOutline ? _outlineColor : Color.clear;
            float distBoost = hasOutline
                ? Mathf.Clamp(Mathf.Max(Mathf.Abs(_outlineDistance.x), Mathf.Abs(_outlineDistance.y)) / _outlineBaseDistance, 0.5f, 3f)
                : 1f;

            var s = new Style
            {
                Radii = new Vector4(br, br, br, br),
                Fill = 0.35f, Glow = 0f, Shadow = 0f, Specular = 0.6f, Refraction = 0f,
                Border = 1f, GlowRadius = 10f, ShadowRadius = 14f,
                Rim = new Color(1f, 1f, 1f, 0.12f)
            };

            switch (rr)
            {
                case Role.Glass:
                    s.Mode = 0;
                    s.Fill = 0.42f;
                    s.Shadow = 1f;
                    s.ShadowRadius = maxS >= 500f ? 30f : 18f;
                    s.Specular = 1f;
                    s.Refraction = 1f;
                    s.Border = 1.2f;
                    s.GlowRadius = 12f;
                    s.Rim = hasOutline ? outlineCol : new Color(1f, 1f, 1f, 0.22f);
                    s.Glow = hasOutline ? 0.30f * distBoost : 0f;
                    break;

                case Role.Platter:
                    s.Mode = 1;
                    s.Fill = 0.32f;
                    s.Specular = 0.55f;
                    s.Border = 1f;
                    s.Rim = hasOutline ? WithAlpha(outlineCol, outlineCol.a * 0.55f) : new Color(1f, 1f, 1f, 0.09f);
                    s.Glow = hasOutline ? 0.10f * (distBoost - 1f) : 0f;
                    s.GlowRadius = 8f;
                    break;

                case Role.Header:
                    s.Mode = 1;
                    s.Fill = 0.30f;
                    s.Specular = 0.25f;
                    s.Border = 0f;
                    s.Rim = hasOutline ? outlineCol : new Color(1f, 1f, 1f, 0.08f);
                    break;

                case Role.Button:
                    s.Mode = 1;
                    s.Fill = 0.58f;
                    s.Shadow = 0.28f;
                    s.ShadowRadius = 8f;
                    s.Specular = 0.95f;
                    s.Border = 1f;
                    s.GlowRadius = 9f;
                    s.Rim = hasOutline ? outlineCol : LightRim(tint, 0.55f);
                    s.Glow = (hasOutline ? 0.22f : 0.14f) * distBoost;
                    break;

                case Role.Field:
                    s.Mode = 1;
                    s.Fill = 0.48f;
                    s.Specular = 0.3f;
                    s.Border = 1f;
                    s.Rim = hasOutline ? WithAlpha(outlineCol, outlineCol.a * 0.8f) : new Color(1f, 1f, 1f, 0.14f);
                    s.Glow = 0.08f;
                    s.GlowRadius = 6f;
                    break;

                case Role.Chip:
                    s.Mode = 1;
                    s.Fill = 0.40f;
                    s.Specular = 0.65f;
                    s.Border = 1f;
                    s.Rim = hasOutline ? outlineCol : new Color(1f, 1f, 1f, 0.14f);
                    s.Glow = 0.10f;
                    s.GlowRadius = 7f;
                    break;

                case Role.Line:
                    s.Mode = 2;
                    s.Fill = 1f;
                    s.Specular = 0f;
                    s.Border = 0f;
                    s.Glow = 0.45f;
                    s.GlowRadius = Mathf.Clamp(minS * 2.2f, 3f, 7f);
                    s.Rim = WithAlpha(tint, 1f);
                    if (forceHairline || tint.a < 0.7f || minS <= 1.6f) s.Flags |= 1;
                    break;

                case Role.Dot:
                    s.Mode = 2;
                    s.Fill = 1f;
                    s.Specular = 0.25f;
                    s.Border = 0f;
                    s.Glow = 0.4f;
                    s.GlowRadius = Mathf.Clamp(minS * 0.4f, 1.5f, 3.5f);
                    s.Rim = WithAlpha(tint, 1f);
                    break;

                case Role.Fill:
                    s.Mode = 2;
                    s.Fill = 1f;
                    s.Specular = 0.55f;
                    s.Border = 0f;
                    s.Glow = 0.5f;
                    s.GlowRadius = 7f;
                    s.Rim = WithAlpha(tint, 1f);
                    break;

                case Role.Scrim:
                    s.Mode = 3;
                    s.Fill = 0.30f;
                    s.Specular = 0f;
                    s.Border = 0f;
                    s.Rim = Color.clear;
                    break;

                case Role.Border:
                    s.Mode = 1;
                    s.Flags |= 2;
                    s.Specular = 0.4f;
                    s.Border = hasOutline ? Mathf.Clamp(_outlineBaseDistance, 1f, 2.5f) : 1f;
                    s.Rim = hasOutline ? outlineCol : Color.clear;
                    s.Glow = hasOutline ? 0.3f * distBoost : 0f;
                    s.GlowRadius = 6f;
                    s.ForceOpaqueVertex = true;
                    break;
            }

            if (useRimOverride)
            {
                s.Rim = rimOverride;
                if (rr == Role.Glass && s.Glow <= 0f && rimOverride.a > 0.4f) s.Glow = 0.28f;
            }

            // Вложенные пластины, прижатые к углам родителя, наследуют его скругление,
            // а прилегающие к краю полосы-шапки — теряют обводку (иначе двойная линия).
            if (rr == Role.Platter || rr == Role.Header)
                FitToParentCorners(ref s, rr);

            if (elevation > 0f)
            {
                s.Shadow = Mathf.Max(s.Shadow, elevation);
                s.ShadowRadius = Mathf.Max(s.ShadowRadius, 22f);
            }

            // Общий тон интерфейса: меньше бликов, свечения и преломления, панели плотнее
            if (s.Mode == 0) { s.Fill = Mathf.Max(s.Fill, LGTone.GlassFill); s.Refraction *= LGTone.Refraction; }
            else if (s.Mode == 1) s.Fill *= LGTone.PlatterFill;
            s.Specular *= LGTone.Specular;
            s.Glow *= LGTone.Glow;

            s.Fill *= fillMultiplier;
            s.Glow = Mathf.Clamp01(s.Glow * glowMultiplier + _pulse * 0.6f);
            s.Shadow *= shadowMultiplier;
            s.Specular *= specularMultiplier;
            return s;
        }

        private void FitToParentCorners(ref Style s, Role rr)
        {
            var parentFx = FindParentSurface();
            if (parentFx == null) return;

            var prt = parentFx.transform as RectTransform;
            Rect pr = prt.rect;
            Vector4 pRadii = parentFx.ResolveRadii();

            rectTransform.GetWorldCorners(s_Corners); // BL TL TR BR
            for (int i = 0; i < 4; i++) s_ParentCorners[i] = prt.InverseTransformPoint(s_Corners[i]);

            const float tol = 1.6f;
            bool left   = Mathf.Abs(s_ParentCorners[0].x - pr.xMin) <= tol;
            bool right  = Mathf.Abs(s_ParentCorners[2].x - pr.xMax) <= tol;
            bool bottom = Mathf.Abs(s_ParentCorners[0].y - pr.yMin) <= tol;
            bool top    = Mathf.Abs(s_ParentCorners[1].y - pr.yMax) <= tol;

            bool flush = false;
            if (top && left)     { s.Radii.x = pRadii.x; flush = true; }
            if (top && right)    { s.Radii.y = pRadii.y; flush = true; }
            if (bottom && right) { s.Radii.z = pRadii.z; flush = true; }
            if (bottom && left)  { s.Radii.w = pRadii.w; flush = true; }

            // Полоса-шапка во всю ширину: нижние углы прямые, обводки нет.
            if (top && left && right && !bottom) { s.Radii.z = 0f; s.Radii.w = 0f; }
            if (bottom && left && right && !top) { s.Radii.x = 0f; s.Radii.y = 0f; }

            if (flush)
            {
                s.Border = 0f;
                s.Specular *= 0.4f;
                if (rr == Role.Platter) s.Fill *= 0.85f;
            }
        }

        private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        private static Color LightRim(Color tint, float a)
        {
            Color l = Color.Lerp(tint, Color.white, 0.55f);
            l.a = a;
            return l;
        }

        // ---------------------------------------------------------------- Mesh

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || graphic == null) return;
            if (_image != null && _image.sprite != null) return;

            EnsureCanvasChannels();

            Rect r = rectTransform.rect;
            if (r.width <= 0.01f || r.height <= 0.01f) { vh.Clear(); return; }

            Color32 c32 = graphic.color;
            if (vh.currentVertCount > 0)
            {
                UIVertex first = default;
                vh.PopulateUIVertex(ref first, 0);
                c32 = first.color;
            }

            Style s = Resolve(r, (Color)c32);
            if (s.ForceOpaqueVertex) c32.a = 255;

            Color rim = s.Rim;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear && !IsGammaVertexCanvas())
            {
                float a = rim.a;
                rim = rim.linear;
                rim.a = a;
            }

            float pad = 2f;
            if (s.Glow > 0.001f) pad = Mathf.Max(pad, s.GlowRadius * 3.2f + 2f);
            if (s.Shadow > 0.001f) pad = Mathf.Max(pad, s.ShadowRadius * 1.45f + 2f);

            Vector2 center = r.center;
            Vector2 half = r.size * 0.5f;
            Vector2 outer = half + new Vector2(pad, pad);

            var uv1 = s.Radii;
            var uv2 = new Vector4(rim.r, rim.g, rim.b, rim.a);
            var uv3 = new Vector4(
                Pack3(s.Fill, s.Glow, s.Shadow),
                Pack3(s.Specular, s.Refraction, _hover),
                PackRaw(s.Mode, Mathf.RoundToInt(s.Border * 32f), s.Flags),
                PackRaw(Mathf.RoundToInt(s.GlowRadius), Mathf.RoundToInt(s.ShadowRadius), Mathf.RoundToInt(intensity * 255f)));

            vh.Clear();
            AddVert(vh, center, new Vector2(-outer.x, -outer.y), half, c32, uv1, uv2, uv3);
            AddVert(vh, center, new Vector2(-outer.x,  outer.y), half, c32, uv1, uv2, uv3);
            AddVert(vh, center, new Vector2( outer.x,  outer.y), half, c32, uv1, uv2, uv3);
            AddVert(vh, center, new Vector2( outer.x, -outer.y), half, c32, uv1, uv2, uv3);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        private bool IsGammaVertexCanvas()
        {
            var c = graphic != null ? graphic.canvas : null;
            return c != null && c.vertexColorAlwaysGammaSpace;
        }

        private static void AddVert(VertexHelper vh, Vector2 center, Vector2 offset, Vector2 half, Color32 col,
                                    Vector4 uv1, Vector4 uv2, Vector4 uv3)
        {
            var v = new UIVertex
            {
                position = new Vector3(center.x + offset.x, center.y + offset.y, 0f),
                normal = Vector3.back,
                tangent = new Vector4(1f, 0f, 0f, -1f),
                color = col,
                uv0 = new Vector4(offset.x, offset.y, half.x, half.y),
                uv1 = uv1,
                uv2 = uv2,
                uv3 = uv3
            };
            vh.AddVert(v);
        }

        private static float Q(float v) => Mathf.Clamp(Mathf.Round(v * 255f), 0f, 255f);
        private static float Pack3(float a, float b, float c) => Q(a) * 65536f + Q(b) * 256f + Q(c);
        private static float PackRaw(int a, int b, int c)
            => Mathf.Clamp(a, 0, 255) * 65536f + Mathf.Clamp(b, 0, 255) * 256f + Mathf.Clamp(c, 0, 255);

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            Dirty();
        }
#endif
    }

    /// <summary>Общий материал стекла (один на все элементы — батчинг сохраняется).</summary>
    public static class LiquidGlassResources
    {
        private static Material s_UIMaterial;

        public static Material UIMaterial
        {
            get
            {
                if (s_UIMaterial != null) return s_UIMaterial;
                var shader = Resources.Load<Shader>("LiquidGlass/LiquidGlassUI");
                if (shader == null) shader = Shader.Find("Astropolity/UI/LiquidGlass");
                if (shader == null)
                {
                    Debug.LogError("[LiquidGlass] Не найден шейдер Astropolity/UI/LiquidGlass.");
                    return null;
                }
                s_UIMaterial = new Material(shader)
                {
                    name = "LiquidGlass UI (runtime)",
                    hideFlags = HideFlags.DontSave
                };
                return s_UIMaterial;
            }
        }
    }

    /// <summary>Пометка: не превращать этот Image (и, опционально, его детей) в стекло.</summary>
    [DisallowMultipleComponent]
    /// <summary>
    /// Общий «тон» стекла для всей игры. Меньше значения — строже и «приборнее» интерфейс,
    /// больше — мягче и игрушечнее. Меняется в одном месте.
    /// </summary>
    public static class LGTone
    {
        /// <summary>Множитель скругления углов (кроме линий и точек).</summary>
        public const float RadiusScale = 0.45f;
        /// <summary>Предельный радиус угла, px.</summary>
        public const float MaxRadius = 10f;
        /// <summary>Множитель бликов (френель, свет сверху, кромка).</summary>
        public const float Specular = 0.4f;
        /// <summary>Множитель неонового свечения вокруг элементов.</summary>
        public const float Glow = 0.5f;
        /// <summary>Множитель преломления и хроматической аберрации стекла.</summary>
        public const float Refraction = 0.35f;
        /// <summary>Минимальная плотность больших стеклянных окон (меньше просвечивают).</summary>
        public const float GlassFill = 0.62f;
        /// <summary>Множитель плотности вложенных пластин.</summary>
        public const float PlatterFill = 1.3f;
    }

    public sealed class LGIgnore : MonoBehaviour
    {
        public bool includeChildren = true;
    }
}
