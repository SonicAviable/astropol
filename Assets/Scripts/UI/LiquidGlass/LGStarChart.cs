using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Фон окон «Звёздная карта»: поверх тёмного стекла — тонкая координатная сетка, туманность в углу, созвездие
    /// и пунктир склонений, по верхнему краю светится латунная линия, в углах — перекрестия визира.
    /// Вешается сам на крупные окна (стекло без родителя-поверхности, см. <see cref="LiquidGlassEffect"/>);
    /// слои лежат под содержимым окна, не ловят клики и не участвуют в раскладке. Спрайты рисуются в коде.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGStarChart : MonoBehaviour
    {
        private static readonly Color Brass = new Color(0.906f, 0.757f, 0.439f);
        private static readonly Color GridTint = new Color(0.50f, 0.78f, 0.91f, 0.05f);
        private static readonly Color NebulaTint = new Color(0.12f, 0.20f, 0.34f, 0.55f);

        private static Sprite s_grid, s_line, s_glow, s_cross;
        private static readonly Sprite[] s_charts = new Sprite[3];

        /// <summary>Окно, которое уже оформлено своим фоном, — не трогать.</summary>
        public static bool Skip(GameObject go)
        {
            if (go == null) return true;
            if (go.name.StartsWith("Tooltip")) return true;
            var c = go.GetComponentInParent<Canvas>();
            return c != null && c.rootCanvas != null && c.rootCanvas.name.StartsWith("Tooltip");
        }

        public static void Attach(GameObject window)
        {
            if (window == null || window.TryGetComponent<LGStarChart>(out _) || Skip(window)) return;
            window.AddComponent<LGStarChart>().Build();
            // Кромка у всех окон одна — холодная и тонкая (цвет окна, заданный кодом, уже не спорит с латунью)
            if (window.TryGetComponent<LiquidGlassEffect>(out var fx)) fx.SetRim(new Color(0.62f, 0.71f, 0.85f, 0.18f));
        }

        private void Build()
        {
            var root = Rect(transform, "SC_Backdrop");
            Stretch(root, 1f);
            root.SetAsFirstSibling();
            LG.Ignore(root.gameObject);

            // Координатная сетка
            var grid = Layer(root, "Grid", Grid, GridTint);
            grid.type = Image.Type.Tiled;
            grid.pixelsPerUnitMultiplier = 1f;
            Stretch(grid.rectTransform, 0f);

            // Туманность в правом верхнем углу
            var neb = Layer(root, "Nebula", MenuArt.SoftDisc, NebulaTint);
            var nrt = neb.rectTransform;
            nrt.anchorMin = new Vector2(0.35f, 0.25f);
            nrt.anchorMax = new Vector2(1.25f, 1.35f);
            nrt.offsetMin = nrt.offsetMax = Vector2.zero;

            // Созвездие и пунктир склонений (свой рисунок у разных окон)
            int k = Mathf.Abs(gameObject.name.GetHashCode()) % s_charts.Length;
            var chart = Layer(root, "Chart", Chart(k), new Color(Brass.r, Brass.g, Brass.b, 0.30f));
            Stretch(chart.rectTransform, 0f);

            // Латунная линия по верхнему краю с мягким свечением под ней
            var glow = Layer(root, "TopGlow", Glow, new Color(Brass.r, Brass.g, Brass.b, 0.22f));
            var grt = glow.rectTransform;
            grt.anchorMin = new Vector2(0f, 1f); grt.anchorMax = new Vector2(1f, 1f);
            grt.pivot = new Vector2(0.5f, 1f);
            grt.sizeDelta = new Vector2(0f, 18f);
            grt.anchoredPosition = Vector2.zero;
            var line = Layer(transform, "SC_TopLine", Line, Brass);
            var lrt = line.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f); lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.sizeDelta = new Vector2(0f, 2f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.SetSiblingIndex(1);

            // Перекрестия визира в углах (чуть за краем окна)
            for (int i = 0; i < 4; i++)
            {
                var x = Layer(transform, "SC_Cross", Cross, new Color(Brass.r, Brass.g, Brass.b, 0.8f));
                var rt = x.rectTransform;
                var a = new Vector2(i % 2, i / 2);
                rt.anchorMin = rt.anchorMax = a;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(13f, 13f);
                rt.anchoredPosition = Vector2.zero;
                rt.SetSiblingIndex(2 + i);
            }
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

        private static Sprite Grid => s_grid != null ? s_grid : (s_grid = Make(28, 28, TextureWrapMode.Repeat, (x, y) =>
            x == 0 || y == 27 ? Color.white : Color.clear, "SC_Grid"));

        private static Sprite Line => s_line != null ? s_line : (s_line = Make(128, 2, TextureWrapMode.Clamp, (x, y) =>
        {
            float u = (x + 0.5f) / 128f;
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) / 0.18f);
            float hot = 1f - Mathf.Abs(u - 0.5f) * 2f;
            var c = Color.Lerp(Color.white * 0.95f, new Color(1f, 1.1f, 1.25f), hot * 0.6f);
            return new Color(Mathf.Min(1f, c.r), Mathf.Min(1f, c.g), Mathf.Min(1f, c.b), edge);
        }, "SC_Line"));

        private static Sprite Glow => s_glow != null ? s_glow : (s_glow = Make(64, 32, TextureWrapMode.Clamp, (x, y) =>
        {
            float u = (x + 0.5f) / 64f, v = (y + 0.5f) / 32f;
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) / 0.25f);
            return new Color(1f, 1f, 1f, edge * v * v);
        }, "SC_Glow"));

        private static Sprite Cross => s_cross != null ? s_cross : (s_cross = Make(13, 13, TextureWrapMode.Clamp, (x, y) =>
            x == 6 || y == 6 ? Color.white : Color.clear, "SC_Cross"));

        /// <summary>Созвездие (звёзды и линии) и два пунктирных круга склонений.</summary>
        private static Sprite Chart(int k)
        {
            if (s_charts[k] != null) return s_charts[k];
            const int W = 256, H = 320;
            var rnd = new System.Random(17 + k * 31);
            var stars = new Vector2[9];
            var p = new Vector2(40 + rnd.Next(40), H - 60 - rnd.Next(30));
            for (int i = 0; i < stars.Length; i++)
            {
                stars[i] = p;
                p += new Vector2(rnd.Next(-20, 60), -rnd.Next(18, 38));
                p.x = Mathf.Clamp(p.x, 20, W - 20);
            }
            var tex = new float[W * H];
            // линии созвездия — цепочка с одной веткой
            for (int i = 0; i + 1 < stars.Length; i++) Seg(tex, W, H, stars[i], stars[i + 1], 0.45f);
            Seg(tex, W, H, stars[3], stars[3] + new Vector2(70, 14), 0.45f);
            for (int i = 0; i < stars.Length; i++) Dot(tex, W, H, stars[i], 1.2f + (float)rnd.NextDouble() * 1.4f);
            // пунктир склонений внизу
            for (int arc = 0; arc < 2; arc++)
            {
                float y0 = 70 - arc * 26, sag = 26;
                for (int x = 0; x < W; x++)
                {
                    if ((x / 4) % 2 == 1) continue;
                    float t = x / (float)W;
                    float y = y0 + sag * (1f - 4f * (t - 0.5f) * (t - 0.5f));
                    Plot(tex, W, H, x, Mathf.RoundToInt(y), arc == 0 ? 0.32f : 0.22f);
                }
            }
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 1f, 1f, Mathf.Clamp01(tex[i]));
            return s_charts[k] = FromPixels(W, H, TextureWrapMode.Clamp, px, "SC_Chart" + k);
        }

        private static void Plot(float[] t, int w, int h, int x, int y, float a)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            t[y * w + x] = Mathf.Max(t[y * w + x], a);
        }

        private static void Seg(float[] t, int w, int h, Vector2 a, Vector2 b, float alpha)
        {
            int n = Mathf.CeilToInt(Vector2.Distance(a, b) * 2f);
            for (int i = 0; i <= n; i++)
            {
                var q = Vector2.Lerp(a, b, i / (float)n);
                Plot(t, w, h, Mathf.RoundToInt(q.x), Mathf.RoundToInt(q.y), alpha);
            }
        }

        private static void Dot(float[] t, int w, int h, Vector2 c, float r)
        {
            int R = Mathf.CeilToInt(r + 1.5f);
            for (int y = -R; y <= R; y++)
                for (int x = -R; x <= R; x++)
                {
                    float d = Mathf.Sqrt(x * x + y * y);
                    float a = Mathf.Clamp01(r + 0.5f - d) + 0.35f * Mathf.Clamp01(1f - d / (r * 3f));
                    Plot(t, w, h, (int)c.x + x, (int)c.y + y, Mathf.Clamp01(a));
                }
        }

        private static Sprite Make(int w, int h, TextureWrapMode wrap, System.Func<int, int, Color> f, string name)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f(x, y);
            return FromPixels(w, h, wrap, px, name);
        }

        private static Sprite FromPixels(int w, int h, TextureWrapMode wrap, Color32[] px, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = wrap, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var s = Sprite.Create(tex, new UnityEngine.Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = name;
            return s;
        }
    }
}
