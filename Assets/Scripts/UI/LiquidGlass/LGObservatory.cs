using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Фон окон «Обсерватория»: тёмно-синяя эмаль цвета окон игры с лёгкой тенью снизу, двойная латунная линия
    /// по краю, латунные уголки с ромбами; у больших окон (от 640×480) — едва заметная гравировка астрономического инструмента,
    /// уходящая за нижний край. Чтобы окна не были одинаковыми, у каждого — своя гравировка
    /// (астролябия, армиллярная сфера, орбиты, роза ветров, небесный глобус), свой нижний угол и наклон;
    /// выбор стабилен для окна (по имени), так что одно и то же окно всегда выглядит одинаково.
    /// Вешается сам на крупные окна (см. <see cref="LiquidGlassEffect"/>); слои лежат под содержимым,
    /// не ловят клики и не участвуют в раскладке. Гравировки рисуются в коде в высоком разрешении и не растягиваются.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGObservatory : MonoBehaviour
    {
        private static readonly Color Brass = new Color(0.906f, 0.757f, 0.439f);
        private static readonly Color Enamel = new Color(0.055f, 0.094f, 0.157f, 0.9f);   // = UIManager.DS.BgDeep

        private const int Motifs = 5;
        private static readonly Sprite[] s_engr = new Sprite[Motifs];
        private static readonly Dictionary<int, Sprite> s_body = new Dictionary<int, Sprite>();
        private static Sprite s_corner, s_light;   // s_light — мягкий градиент для тени снизу

        /// <summary>Подсказки и окна, у которых уже свой фон, не трогаются.</summary>
        public static bool Skip(GameObject go)
        {
            if (go == null) return true;
            if (go.name.StartsWith("Tooltip")) return true;
            var c = go.GetComponentInParent<Canvas>();
            return c != null && c.rootCanvas != null && c.rootCanvas.name.StartsWith("Tooltip");
        }

        public static void Attach(GameObject window, float radius)
        {
            if (window == null || window.TryGetComponent<LGObservatory>(out _) || Skip(window)) return;
            window.AddComponent<LGObservatory>().Build(radius);
            if (window.TryGetComponent<LiquidGlassEffect>(out var fx)) fx.SetRim(new Color(0f, 0f, 0f, 0f));   // кромку рисует эмаль
        }

        private void Build(float radius)
        {
            var r = ((RectTransform)transform).rect;
            uint h = Hash(gameObject.name + "/" + (transform.parent != null ? transform.parent.name : ""));

            var root = Rect(transform, "OB_Backdrop");
            Stretch(root, 0f);
            root.SetAsFirstSibling();
            LG.Ignore(root.gameObject);

            // Эмаль с двойной латунной линией по краю
            var body = Layer(root, "Body", Body(Mathf.Clamp(Mathf.RoundToInt(radius), 0, 24)), Color.white);
            body.type = Image.Type.Sliced;
            body.pixelsPerUnitMultiplier = 1f;
            Stretch(body.rectTransform, 0f);

            var clip = Rect(root, "Clip");
            Stretch(clip, 1.5f);
            clip.gameObject.AddComponent<RectMask2D>();

            // Лёгкая тень снизу (без подсветки сверху — шапки окон со своим фоном не должны отличаться от тела)
            int corner = (int)(h % 2);   // гравировка — в нижнем углу: вверху у окон шапки со своим рисунком
            var shade = Layer(clip, "Shade", LightSprite, new Color(0f, 0f, 0f, 0.18f));
            var srt = shade.rectTransform;
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0f);
            srt.pivot = new Vector2(0.5f, 0f);
            srt.sizeDelta = new Vector2(0f, Mathf.Min(220f, r.height * 0.5f));
            srt.localScale = new Vector3(1f, -1f, 1f);
            srt.anchoredPosition = new Vector2(0f, srt.sizeDelta.y);

            // Гравировка инструмента — своя у окна, частично за краем; только у крупных окон (на мелких панелях — шум)
            if (r.width >= 640f && r.height >= 480f)
            {
                int motif = (int)((h / 16) % Motifs);
                var engr = Layer(clip, "Engraving", Engraving(motif), new Color(Brass.r, Brass.g, Brass.b, 0.05f));
                float es = Mathf.Clamp(Mathf.Min(r.width, r.height) * 0.9f, 300f, 440f);
                Place(engr.rectTransform, corner, es, 0.28f);
                engr.rectTransform.localRotation = Quaternion.Euler(0f, 0f, ((h / 80) % 31) - 15f);
            }

            // Латунные уголки с ромбами
            for (int i = 0; i < 4; i++)
            {
                var c = Layer(transform, "OB_Corner", Corner, Brass);
                var crt = c.rectTransform;
                var a = new Vector2(i % 2, i / 2);
                crt.anchorMin = crt.anchorMax = a;
                crt.pivot = new Vector2(0f, 1f);   // вершина «Г»; отражение разворачивает уголок внутрь окна
                crt.sizeDelta = new Vector2(22f, 22f);
                crt.anchoredPosition = Vector2.zero;
                crt.localScale = new Vector3(i % 2 == 0 ? 1f : -1f, i / 2 == 1 ? 1f : -1f, 1f);
                crt.SetSiblingIndex(1 + i);
            }
        }

        /// <summary>Квадрат размера size в углу (0 — левый нижний, 1 — правый нижний, 2 — левый верхний, 3 — правый верхний),
        /// центр — на доле inside от края внутрь окна.</summary>
        private static void Place(RectTransform rt, int corner, float size, float inside)
        {
            var a = new Vector2(corner % 2, corner / 2);
            rt.anchorMin = rt.anchorMax = a;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2((a.x > 0.5f ? -1f : 1f) * size * inside, (a.y > 0.5f ? -1f : 1f) * size * inside);
        }

        private static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        // ------------------------------------------------------------------ Слои

        private static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            return rt;
        }

        private static Image Layer(Transform parent, string name, Sprite s, Color c)
        {
            var im = Rect(parent, name).gameObject.AddComponent<Image>();
            im.sprite = s;
            im.color = c;
            im.raycastTarget = false;
            LG.Ignore(im.gameObject);
            return im;
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        // ------------------------------------------------------------------ Спрайты

        /// <summary>Эмаль со скруглением radius: внешняя латунная линия по краю и тонкая внутренняя на 5 px от края.</summary>
        private static Sprite Body(int radius)
        {
            if (s_body.TryGetValue(radius, out var cached)) return cached;
            int b = radius + 9, T = 2 * b + 4;
            float half = T * 0.5f;
            var px = new Color32[T * T];
            for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float d = -outside;                                    // расстояние внутрь от края
                    float cover = Mathf.Clamp01(d + 0.5f);
                    if (cover <= 0f) { px[y * T + x] = new Color32(0, 0, 0, 0); continue; }
                    var c = Enamel;
                    float outer = Mathf.Clamp01(1.3f - d) * 0.42f;
                    float inner = Mathf.Clamp01(1f - Mathf.Abs(d - 5.5f)) * 0.14f;
                    c = Color.Lerp(c, new Color(Brass.r, Brass.g, Brass.b, 1f), Mathf.Max(outer, inner));
                    c.a = Mathf.Lerp(Enamel.a, 1f, outer) * cover;
                    px[y * T + x] = c;
                }
            var s = FromPixels(T, T, px, "OB_Body" + radius, new Vector4(b, b, b, b));
            s_body[radius] = s;
            return s;
        }

        /// <summary>Уголок (левый верхний): латунная «Г» и ромб в вершине.</summary>
        private static Sprite Corner => s_corner != null ? s_corner : (s_corner = Make(22, 22, (x, y) =>
        {
            float fx = x + 0.5f, fy = 22f - (y + 0.5f);                    // от левого верхнего угла
            float l = (fx <= 2f && fy <= 18f) || (fy <= 2f && fx <= 18f) ? 1f : 0f;
            l *= Mathf.Clamp01(1f - Mathf.Max(fx, fy) / 18f) * 0.6f + 0.4f;   // к концам линии гаснут
            float dm = Mathf.Abs(fx - 3f) + Mathf.Abs(fy - 3f);
            float dia = Mathf.Clamp01(3.6f - dm);
            return new Color(1f, 1f, 1f, Mathf.Max(l, dia));
        }, "OB_Corner"));

        private static Sprite LightSprite => s_light != null ? s_light : (s_light = Make(64, 64, (x, y) =>
        {
            float u = (x + 0.5f) / 64f, v = (y + 0.5f) / 64f;
            float side = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) / 0.35f);
            return new Color(1f, 1f, 1f, side * v * v * v);
        }, "OB_Light"));

        // ------------------------------------------------------------------ Гравировки

        private const int E = 512;

        private static Sprite Engraving(int motif)
        {
            if (s_engr[motif] != null) return s_engr[motif];
            var a = new float[E * E];
            var p = new Pen(a);
            switch (motif)
            {
                case 0: Astrolabe(p); break;
                case 1: Armillary(p); break;
                case 2: Orrery(p); break;
                case 3: CompassRose(p); break;
                default: Globe(p); break;
            }
            var px = new Color32[E * E];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 1f, 1f, Mathf.Clamp01(a[i]));
            return s_engr[motif] = FromPixels(E, E, px, "OB_Engraving" + motif, Vector4.zero);
        }

        private static void Astrolabe(Pen p)
        {
            p.Circle(0, 0, 0.96f, 2.2f);
            p.Ticks(0, 0, 0.86f, 0.92f, 72, 1.4f);
            p.Ticks(0, 0, 0.80f, 0.92f, 12, 2f);
            p.Circle(0, 0, 0.80f);
            p.Circle(0, 0, 0.56f);
            p.Circle(0, 0, 0.32f);
            p.Ellipse(0, -0.12f, 0.64f, 0.50f, 0f);
            p.Ellipse(0, -0.24f, 0.44f, 0.34f, 0f);
            p.Ellipse(0, -0.34f, 0.26f, 0.20f, 0f);
            p.Line(-0.80f, 0, 0.80f, 0); p.Line(0, -0.80f, 0, 0.80f);
            p.Line(-0.56f, -0.56f, 0.56f, 0.56f); p.Line(-0.56f, 0.56f, 0.56f, -0.56f);
            p.Circle(0, 0.18f, 0.58f, 2.4f);
            p.Disc(0, 0, 0.03f);
        }

        private static void Armillary(Pen p)
        {
            p.Circle(0, 0, 0.92f, 2.4f);
            p.Circle(0, 0, 0.87f);
            p.Ellipse(0, 0, 0.87f, 0.22f, -23f, 2f);
            p.Ellipse(0, 0, 0.87f, 0.32f, 18f);
            p.Ellipse(0, 0, 0.20f, 0.87f, 0f);
            p.Ellipse(0, 0, 0.55f, 0.87f, 0f);
            p.Ticks(0, 0, 0.92f, 0.98f, 36, 1.4f);
            float c = Mathf.Cos(-23f * Mathf.Deg2Rad), s = Mathf.Sin(-23f * Mathf.Deg2Rad);
            p.Line(-s * 0.99f, -c * 0.99f, s * 0.99f, c * 0.99f, 1.8f);
            p.Circle(0, 0, 0.12f, 2f);
            p.Disc(0, 0, 0.05f);
        }

        private static void Orrery(Pen p)
        {
            float[] a = { 0.26f, 0.45f, 0.66f, 0.92f };
            float[] t = { 0.6f, 2.4f, 4.1f, 5.3f };
            for (int i = 0; i < a.Length; i++)
            {
                p.Ellipse(0, 0, a[i], a[i] * 0.74f, -14f, i == 2 ? 2f : 1.4f);
                float rot = -14f * Mathf.Deg2Rad;
                float ex = a[i] * Mathf.Cos(t[i]), ey = a[i] * 0.74f * Mathf.Sin(t[i]);
                float x = ex * Mathf.Cos(rot) - ey * Mathf.Sin(rot), y = ex * Mathf.Sin(rot) + ey * Mathf.Cos(rot);
                p.Disc(x, y, 0.022f + i * 0.006f);
                if (i == 2) p.Ellipse(x, y, 0.07f, 0.025f, -14f);
                if (i == 3) p.Circle(x, y, 0.06f);
            }
            p.Disc(0, 0, 0.06f);
            p.Circle(0, 0, 0.10f);
            p.Ticks(0, 0, 0.97f, 1f, 48, 1.2f);
        }

        private static void CompassRose(Pen p)
        {
            p.Circle(0, 0, 0.95f, 2.2f);
            p.Circle(0, 0, 0.90f);
            p.Ticks(0, 0, 0.78f, 0.86f, 36, 1.4f);
            p.Circle(0, 0, 0.30f);
            for (int k = 0; k < 8; k++)
            {
                float ang = k * Mathf.PI / 4f + Mathf.PI / 2f;
                float len = k % 2 == 0 ? 0.76f : 0.46f;
                float tip = ang, l = ang - Mathf.PI / 8f, rgt = ang + Mathf.PI / 8f;
                Vector2 T = new Vector2(Mathf.Cos(tip), Mathf.Sin(tip)) * len;
                Vector2 L = new Vector2(Mathf.Cos(l), Mathf.Sin(l)) * 0.13f;
                Vector2 R = new Vector2(Mathf.Cos(rgt), Mathf.Sin(rgt)) * 0.13f;
                p.Line(L.x, L.y, T.x, T.y, k % 2 == 0 ? 1.8f : 1.3f);
                p.Line(R.x, R.y, T.x, T.y, k % 2 == 0 ? 1.8f : 1.3f);
                p.Line(0, 0, T.x, T.y, 1f);
            }
            p.Disc(0, 0, 0.025f);
        }

        private static void Globe(Pen p)
        {
            const float R = 0.92f, tilt = 20f;
            p.Circle(0, 0, R, 2.4f);
            p.Ellipse(0, 0, R * 0.80f, R, tilt);
            p.Ellipse(0, 0, R * 0.46f, R, tilt);
            p.Ellipse(0, 0, R * 0.10f, R, tilt, 1.8f);
            float c = Mathf.Cos(tilt * Mathf.Deg2Rad), s = Mathf.Sin(tilt * Mathf.Deg2Rad);
            foreach (float yy in new[] { -0.6f, -0.3f, 0f, 0.3f, 0.6f })
            {
                float hw = Mathf.Sqrt(Mathf.Max(0f, R * R - (yy * R) * (yy * R)));
                float x0 = -hw, x1 = hw, y = yy * R;
                p.Line(x0 * c - y * s, x0 * s + y * c, x1 * c - y * s, x1 * s + y * c, yy == 0f ? 2f : 1.2f);
            }
        }

        /// <summary>Сглаженные линии в квадрате E×E, координаты от −1 до 1, ширина — в пикселях текстуры.</summary>
        private sealed class Pen
        {
            private readonly float[] _a;
            public Pen(float[] a) { _a = a; }

            private static float ToPx(float v) => (v * 0.5f + 0.5f) * E;

            private void Shape(float x0, float y0, float x1, float y1, float w, System.Func<float, float, float> dist)
            {
                int ax = Mathf.Max(0, Mathf.FloorToInt(ToPx(x0) - w - 2)), bx = Mathf.Min(E - 1, Mathf.CeilToInt(ToPx(x1) + w + 2));
                int ay = Mathf.Max(0, Mathf.FloorToInt(ToPx(y0) - w - 2)), by = Mathf.Min(E - 1, Mathf.CeilToInt(ToPx(y1) + w + 2));
                for (int y = ay; y <= by; y++)
                    for (int x = ax; x <= bx; x++)
                    {
                        float d = dist(x + 0.5f, y + 0.5f);
                        float a = Mathf.Clamp01(w * 0.5f + 0.5f - d);
                        if (a > _a[y * E + x]) _a[y * E + x] = a;
                    }
            }

            public void Circle(float cx, float cy, float r, float w = 1.4f)
            {
                float px = ToPx(cx), py = ToPx(cy), pr = r * 0.5f * E;
                Shape(cx - r, cy - r, cx + r, cy + r, w, (x, y) => Mathf.Abs(Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py)) - pr));
            }

            public void Disc(float cx, float cy, float r)
            {
                float px = ToPx(cx), py = ToPx(cy), pr = r * 0.5f * E;
                Shape(cx - r, cy - r, cx + r, cy + r, 1f, (x, y) => Mathf.Max(0f, Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py)) - pr));
            }

            public void Ellipse(float cx, float cy, float ra, float rb, float deg, float w = 1.4f)
            {
                float px = ToPx(cx), py = ToPx(cy), a = ra * 0.5f * E, b = rb * 0.5f * E;
                float c = Mathf.Cos(-deg * Mathf.Deg2Rad), s = Mathf.Sin(-deg * Mathf.Deg2Rad);
                float m = Mathf.Max(ra, rb);
                Shape(cx - m, cy - m, cx + m, cy + m, w, (x, y) =>
                {
                    float dx = x - px, dy = y - py;
                    float u = dx * c - dy * s, v = dx * s + dy * c;
                    float f = u * u / (a * a) + v * v / (b * b) - 1f;
                    float gx = 2f * u / (a * a), gy = 2f * v / (b * b);
                    return Mathf.Abs(f) / Mathf.Max(1e-4f, Mathf.Sqrt(gx * gx + gy * gy));
                });
            }

            public void Line(float x0, float y0, float x1, float y1, float w = 1.4f)
            {
                var A = new Vector2(ToPx(x0), ToPx(y0));
                var B = new Vector2(ToPx(x1), ToPx(y1));
                var AB = B - A;
                float len2 = Mathf.Max(1e-4f, AB.sqrMagnitude);
                Shape(Mathf.Min(x0, x1), Mathf.Min(y0, y1), Mathf.Max(x0, x1), Mathf.Max(y0, y1), w, (x, y) =>
                {
                    var P = new Vector2(x, y);
                    float t = Mathf.Clamp01(Vector2.Dot(P - A, AB) / len2);
                    return Vector2.Distance(P, A + AB * t);
                });
            }

            public void Ticks(float cx, float cy, float r0, float r1, int n, float w = 1.4f)
            {
                for (int i = 0; i < n; i++)
                {
                    float ang = i * Mathf.PI * 2f / n;
                    float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
                    Line(cx + c * r0, cy + s * r0, cx + c * r1, cy + s * r1, w);
                }
            }
        }

        private static Sprite Make(int w, int h, System.Func<int, int, Color> f, string name)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f(x, y);
            return FromPixels(w, h, px, name, Vector4.zero);
        }

        private static Sprite FromPixels(int w, int h, Color32[] px, string name, Vector4 border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            var s = Sprite.Create(tex, new UnityEngine.Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            s.name = name;
            return s;
        }
    }
}
