using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Liquid Glass — единая дизайн-система интерфейса Astropolity.
    ///
    ///   LG.Glass(go)    — парящая стеклянная панель с блюром фона, тенью и бликами
    ///   LG.Platter(go)  — вложенная полупрозрачная пластина (секции, строки)
    ///   LG.Button(go)   — стеклянная капсула-кнопка с живым hover/press
    ///   LG.Chip(go, c)  — капсула-индикатор с цветной неоновой кромкой
    ///   LG.Line(go)     — светящаяся линия/разделитель
    ///   LG.Scrim(go)    — полноэкранный размытый затемнитель под модалками
    ///   LG.Show/Hide    — плавное появление/исчезновение вместо SetActive
    ///   LG.Skin(root)   — превратить всё дерево в стекло по правилам (то же делает страховочный скан)
    /// </summary>
    public static class LG
    {
        // ------------------------------------------------------------------ Палитра

        public static class Palette
        {
            public static readonly Color GlassRim      = new Color(1f, 1f, 1f, 0.22f);
            public static readonly Color GlassRimSoft  = new Color(1f, 1f, 1f, 0.10f);
            public static readonly Color NeonRim       = new Color(0.36f, 0.95f, 0.88f, 0.70f);
            public static readonly Color GoldRim       = new Color(1.00f, 0.82f, 0.36f, 0.75f);
            public static readonly Color DangerRim     = new Color(1.00f, 0.40f, 0.42f, 0.75f);
            public static readonly Color SuccessRim    = new Color(0.40f, 1.00f, 0.62f, 0.70f);
            public static readonly Color ScrimTint     = new Color(0.010f, 0.025f, 0.040f, 1f);
        }

        // ------------------------------------------------------------------ Стили

        public static LiquidGlassEffect Apply(GameObject go, LiquidGlassEffect.Role role, float radius = -1f)
        {
            if (go == null) return null;
            LiquidGlassSystem.Ensure();

            if (go.GetComponent<Graphic>() == null)
            {
                var img = go.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0f);
                img.raycastTarget = false;
            }

            var fx = go.GetComponent<LiquidGlassEffect>();
            if (fx == null) fx = go.AddComponent<LiquidGlassEffect>();
            if (!fx.enabled) fx.enabled = true;
            fx.StyleRole = role;
            fx.Radius = radius;
            return fx;
        }

        public static LiquidGlassEffect Glass(GameObject go, float radius = -1f) => Apply(go, LiquidGlassEffect.Role.Glass, radius);
        public static LiquidGlassEffect Glass(Component c, float radius = -1f) => Glass(c != null ? c.gameObject : null, radius);

        public static LiquidGlassEffect Platter(GameObject go, float radius = -1f) => Apply(go, LiquidGlassEffect.Role.Platter, radius);
        public static LiquidGlassEffect Platter(Component c, float radius = -1f) => Platter(c != null ? c.gameObject : null, radius);

        public static LiquidGlassEffect Header(GameObject go) => Apply(go, LiquidGlassEffect.Role.Header);

        public static LiquidGlassEffect Line(GameObject go, bool hairline = false)
        {
            var fx = Apply(go, LiquidGlassEffect.Role.Line);
            if (fx != null) fx.Hairline = hairline;
            return fx;
        }

        public static LiquidGlassEffect Fill(GameObject go) => Apply(go, LiquidGlassEffect.Role.Fill);
        public static LiquidGlassEffect Dot(GameObject go) => Apply(go, LiquidGlassEffect.Role.Dot);

        public static LiquidGlassEffect Scrim(GameObject go)
        {
            var fx = Apply(go, LiquidGlassEffect.Role.Scrim, 0f);
            var g = go.GetComponent<Graphic>();
            if (g != null && g.color.a < 0.05f) g.color = Palette.ScrimTint;
            return fx;
        }

        public static LiquidGlassEffect Chip(GameObject go, Color rim, float radius = -1f)
        {
            var fx = Apply(go, LiquidGlassEffect.Role.Chip, radius);
            if (fx != null) fx.SetRim(rim);
            return fx;
        }

        public static LiquidGlassEffect Button(GameObject go, Color? rim = null, float radius = -1f, bool animateScale = true)
        {
            var fx = Apply(go, LiquidGlassEffect.Role.Button, radius);
            if (fx == null) return null;
            if (rim.HasValue) fx.SetRim(rim.Value);
            var sel = go.GetComponent<Selectable>();
            if (sel != null) TuneSelectable(sel);
            var it = go.GetComponent<LGInteractive>();
            if (it == null) it = go.AddComponent<LGInteractive>();
            it.animateScale = animateScale && !HasOwnScaleAnimator(go);
            return fx;
        }

        public static LiquidGlassEffect Border(GameObject go, Color rim)
        {
            var fx = Apply(go, LiquidGlassEffect.Role.Border);
            if (fx != null) fx.SetRim(rim);
            return fx;
        }

        /// <summary>Не трогать этот объект (и детей) авто-скиннингом.</summary>
        public static void Ignore(GameObject go, bool includeChildren = true)
        {
            if (go == null) return;
            var ig = go.GetComponent<LGIgnore>();
            if (ig == null) ig = go.AddComponent<LGIgnore>();
            ig.includeChildren = includeChildren;
        }

        // ------------------------------------------------------------------ Скруглённая маска

        private static readonly Dictionary<int, Sprite> s_RoundSprites = new Dictionary<int, Sprite>();

        /// <summary>
        /// Создаёт растянутый дочерний контейнер со скруглённой маской (stencil).
        /// Всё, что положено внутрь (видео, голограммы), обрезается по скруглению панели.
        /// </summary>
        public static RectTransform RoundedMask(Transform parent, float radius, float inset = 0f)
        {
            var go = new GameObject("RoundedMask");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);

            var img = go.AddComponent<Image>();
            img.sprite = RoundedSprite(Mathf.Max(1, Mathf.RoundToInt(radius)));
            img.type = Image.Type.Sliced;
            img.color = Color.white;
            img.raycastTarget = false;
            var mask = go.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            return rt;
        }

        /// <summary>Переносит существующие дочерние объекты внутрь скруглённой маски.</summary>
        public static RectTransform WrapInRoundedMask(Transform parent, float radius, float inset, params Transform[] children)
        {
            var mask = RoundedMask(parent, radius, inset);
            mask.SetSiblingIndex(0);
            foreach (var c in children)
            {
                if (c == null) continue;
                c.SetParent(mask, false);
            }
            return mask;
        }

        private static Sprite RoundedSprite(int r)
        {
            if (s_RoundSprites.TryGetValue(r, out var cached) && cached != null) return cached;

            int size = r * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var px = new Color32[size * size];
            float c = size * 0.5f;
            float half = c - 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - c) - (half - r);
                float qy = Mathf.Abs(y + 0.5f - c) - (half - r);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                                + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - outside) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            float b = r + 1f;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            sprite.hideFlags = HideFlags.DontSave;
            s_RoundSprites[r] = sprite;
            return sprite;
        }

        // ------------------------------------------------------------------ Анимации

        public static LGAppear Motion(GameObject go, LGAppear.Kind kind = LGAppear.Kind.Pop)
        {
            if (go == null) return null;
            var a = go.GetComponent<LGAppear>();
            if (a == null) a = go.AddComponent<LGAppear>();
            a.kind = kind;
            return a;
        }

        /// <summary>Показать с анимацией (если объект уже скрывается — плавно развернуть обратно).</summary>
        public static void Show(GameObject go)
        {
            if (go == null) return;
            var a = go.GetComponent<LGAppear>();
            if (!go.activeSelf) { go.SetActive(true); return; }
            if (a != null && a.IsHiding) a.PlayIn();
        }

        /// <summary>Скрыть с анимацией, затем SetActive(false).</summary>
        public static void Hide(GameObject go, System.Action onHidden = null)
        {
            if (go == null) { onHidden?.Invoke(); return; }
            var a = go.GetComponent<LGAppear>();
            if (a != null && go.activeInHierarchy) { a.PlayOut(onHidden); return; }
            go.SetActive(false);
            onHidden?.Invoke();
        }

        public static bool IsVisible(GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return false;
            var a = go.GetComponent<LGAppear>();
            return a == null || !a.IsHiding;
        }

        // ------------------------------------------------------------------ Авто-скиннинг

        private static readonly List<Image> s_Images = new List<Image>(512);

        /// <summary>
        /// Проходит по дереву и превращает в стекло все "плоские" Image (без спрайта).
        /// Уже стилизованные элементы, маски, спрайты и помеченные LGIgnore — пропускаются.
        /// </summary>
        public static void Skin(Transform root)
        {
            if (root == null) return;
            LiquidGlassSystem.Ensure();

            s_Images.Clear();
            root.GetComponentsInChildren(true, s_Images);
            for (int i = 0; i < s_Images.Count; i++)
            {
                var img = s_Images[i];
                if (img == null) continue;
                if (img.TryGetComponent<LiquidGlassEffect>(out _)) continue;
                if (img.sprite != null || img.type != Image.Type.Simple) continue;
                if (img.TryGetComponent<Mask>(out _)) continue;
                if (img.color.a < 0.02f && !img.TryGetComponent<Outline>(out _)) continue;
                if (IsIgnored(img.transform)) continue;

                var canvas = img.canvas;
                if (canvas != null && canvas.renderMode == RenderMode.WorldSpace) continue;

                img.gameObject.AddComponent<LiquidGlassEffect>();

                var sel = img.GetComponent<Selectable>();
                if (sel != null && (sel.targetGraphic == null || sel.targetGraphic == img))
                {
                    TuneSelectable(sel);
                    if (!img.TryGetComponent<LGInteractive>(out _))
                    {
                        var it = img.gameObject.AddComponent<LGInteractive>();
                        it.animateScale = !HasOwnScaleAnimator(img.gameObject) && !(sel is InputField);
                    }
                }
            }
            s_Images.Clear();
        }

        private static bool IsIgnored(Transform t)
        {
            bool self = true;
            while (t != null)
            {
                if (t.TryGetComponent<LGIgnore>(out var ig) && (self || ig.includeChildren)) return true;
                if (t.TryGetComponent<Canvas>(out var c) && c.isRootCanvas) return false;
                self = false;
                t = t.parent;
            }
            return false;
        }

        private static bool HasOwnScaleAnimator(GameObject go)
        {
            return go.GetComponent<ButtonPunch>() != null
                || go.GetComponent<TechTreePanel.TechCardFX>() != null
                || go.GetComponent<FactionCardHover>() != null;
        }

        /// <summary>Плоский ColorTint заменяется стеклянным откликом LGInteractive.</summary>
        public static void TuneSelectable(Selectable sel)
        {
            if (sel == null || sel.transition != Selectable.Transition.ColorTint) return;
            var cb = sel.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = Color.white;
            cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            cb.disabledColor = new Color(0.85f, 0.85f, 0.85f, 0.42f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.12f;
            sel.colors = cb;
        }
    }
}
