using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Оформление панели системы «как в больших играх»: живой портрет звезды в шапке (свечение звезды, два встречных
    /// голографических кольца цвета владельца), уголки-скобы и тонкая сетка по стеклу, свет звезды из-под шапки,
    /// блик, изредка пробегающий по шапке и главным кнопкам, каскадное появление карточек, счётчик мощи «набегает».
    /// Всё на unscaled time — живёт и на паузе.
    /// </summary>
    public partial class UIManager
    {
        private RectTransform _inspPortrait;
        private Image _inspStarHalo, _inspStarCore, _inspStarHot, _inspPlanet, _inspRingA, _inspRingB, _inspHeaderGlow, _inspOwnerGlow;
        private Image _inspStarCorona, _inspStarFlare, _inspStarFlare2, _inspDisc;
        private int _inspCascadeKey = int.MinValue;
        // Фон шапки — «кадр системы»: космос, большая главная планета справа, свет звезды, затемнение под текстом
        private RectTransform _inspBanner;
        private RawImage _inspBannerSky;
        private Image _inspBannerPlanet, _inspBannerStar;

        // ==================== СПРАЙТЫ ====================

        private static Sprite s_fadeRight, s_hexTile;
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> s_resSprites = new System.Collections.Generic.Dictionary<string, Sprite>();

        private static Sprite s_sunDisc, s_starCorona, s_starFlare;
        private static Sprite StarCorona => s_starCorona != null ? s_starCorona : (s_starCorona = StarFx.CoronaSprite(256));
        private static Sprite StarFlare => s_starFlare != null ? s_starFlare : (s_starFlare = StarFx.FlareSprite(256));

        /// <summary>
        /// Тело звезды как на карте: раскалённое ядро, мягкое свечение и дымка — без твёрдого края (иначе это «шарик»);
        /// край свечения чуть неровный. Белая — красится цветом звезды через Image.color.
        /// </summary>
        private static Sprite SunDiscSprite
        {
            get
            {
                if (s_sunDisc != null) return s_sunDisc;
                const int res = 128;
                var tex = new Texture2D(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "StarBody" };
                var px = new Color32[res * res];
                float c = (res - 1) * 0.5f;
                for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float warp = 0.88f + Mathf.PerlinNoise(Mathf.Cos(ang) * 2.2f + 5f, Mathf.Sin(ang) * 2.2f + 5f) * 0.24f;
                    float w = r / warp;
                    float core = Mathf.Clamp01(Mathf.Exp(-w * w * 200f) * 1.4f + Mathf.Exp(-w * w * 45f));
                    float a = (core + Mathf.Exp(-w * w * 12f) * 0.55f + Mathf.Exp(-w * w * 4f) * 0.15f) * (1f - Mathf.SmoothStep(0.75f, 1f, r));
                    px[y * res + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
                tex.SetPixels32(px);
                tex.Apply();
                s_sunDisc = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
                return s_sunDisc;
            }
        }

        /// <summary>Полоса, тающая вправо: подчёркивание заголовков секций.</summary>
        internal static Sprite FadeRightSprite
        {
            get
            {
                if (s_fadeRight != null) return s_fadeRight;
                var tex = new Texture2D(128, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                for (int x = 0; x < 128; x++)
                {
                    float t = x / 127f;
                    tex.SetPixel(x, 0, new Color(1f, 1f, 1f, Mathf.Pow(1f - t, 1.6f)));
                }
                tex.Apply();
                s_fadeRight = Sprite.Create(tex, new Rect(0, 0, 128, 1), new Vector2(0.5f, 0.5f));
                s_fadeRight.name = "InspFadeRight";
                return s_fadeRight;
            }
        }

        /// <summary>Картинка из Resources как спрайт (кольца голограммы — общие с карточным боем).</summary>
        private static Sprite ResSprite(string path)
        {
            if (s_resSprites.TryGetValue(path, out var s)) return s;
            var tex = Resources.Load<Texture2D>(path);
            s = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)) : null;
            s_resSprites[path] = s;
            return s;
        }

        // ==================== ДЕКОР ПАНЕЛИ ====================

        /// <summary>Свет звезды из-под шапки и уголки-скобы (фон — чистое тёмное стекло, без сетки).</summary>
        private void BuildInspectorDecor(RectTransform insp)
        {
            // Тонкая шестигранная сетка по всему стеклу (едва заметна — «голографический планшет»)
            // Свет звезды: мягкое пятно её цвета, выходящее из шапки вниз
            _inspHeaderGlow = LGBuild.Panel(insp, "StarLight", new Color(1f, 0.9f, 0.6f, 0.0f));
            _inspHeaderGlow.sprite = FactionSelectScreen.RadialSprite();
            _inspHeaderGlow.raycastTarget = false;
            _inspHeaderGlow.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(60, -40), new Vector2(520, 260));
            LG.Ignore(_inspHeaderGlow.gameObject);

            // Уголки-скобы цвета интерфейса
            Color cc = new Color(DS.NeonCyan.r, DS.NeonCyan.g, DS.NeonCyan.b, 0.75f);
            foreach (var corner in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) })
            {
                float sx = corner.x < 0.5f ? 1f : -1f, sy = corner.y < 0.5f ? 1f : -1f;
                var h = LGBuild.Panel(insp, "Bracket", cc);
                h.raycastTarget = false;
                h.rectTransform.At(corner, new Vector2(corner.x, corner.y), new Vector2(sx * 3f, sy * 3f), new Vector2(18, 2));
                var v = LGBuild.Panel(insp, "Bracket", cc);
                v.raycastTarget = false;
                v.rectTransform.At(corner, new Vector2(corner.x, corner.y), new Vector2(sx * 3f, sy * 3f), new Vector2(2, 18));
                LG.Ignore(h.gameObject); LG.Ignore(v.gameObject);
            }
        }

        /// <summary>
        /// Портрет в шапке: для системы — сама звезда (ореол, тело, раскалённое ядро) в двух встречных голографических
        /// кольцах цвета владельца; для планеты — значок в тех же кольцах.
        /// </summary>
        private void BuildInspectorPortrait(Transform header)
        {
            BuildInspectorBanner(header);
            _inspPortrait = LGBuild.Rect(header, "Portrait");
            _inspPortrait.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(52, -2), new Vector2(84, 84));

            Image Layer(string name, Sprite sp, float size, Color c, Material mat = null)
            {
                var img = LGBuild.Panel(_inspPortrait, name, c);
                img.sprite = sp;
                img.raycastTarget = false;
                img.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
                if (mat != null) img.material = mat;
                LG.Ignore(img.gameObject);
                return img;
            }

            var radial = FactionSelectScreen.RadialSprite();
            _inspStarHalo = Layer("Halo", radial, 150f, new Color(1f, 0.9f, 0.6f, 0.55f));
            var holo = StellarisClone.Cards.UIFx.HoloMat;
            var ringB = ResSprite("UI/CardBattle/holo_ring_b");
            var ringA = ResSprite("UI/CardBattle/holo_ring_a");
            if (ringB != null) { _inspRingB = Layer("RingB", ringB, 96f, new Color(0.3f, 0.95f, 0.85f, 0.7f), holo); StellarisClone.Cards.HoloSpin.Add(_inspRingB, -10f, 0.18f); }
            if (ringA != null) { _inspRingA = Layer("RingA", ringA, 80f, new Color(1f, 1f, 1f, 0.5f), holo); StellarisClone.Cards.HoloSpin.Add(_inspRingA, 16f, 0.18f, 0f, 1.7f); }
            // Тёмная подложка — только под значком планеты/объекта; звезда светит прямо в космосе
            _inspDisc = Layer("Disc", FleetIndicator.GetDiscSprite(), 58f, new Color(0.01f, 0.02f, 0.035f, 0.92f));
            // Звезда как на карте: неровная корона, светило с потемнением к краю и «зерном», раскалённое ядро, блик-лучи
            _inspStarCorona = Layer("Corona", StarCorona, 112f, new Color(1f, 0.9f, 0.6f, 0.75f));
            _inspStarCore = Layer("Star", SunDiscSprite, 70f, new Color(1f, 0.92f, 0.6f, 1f));
            _inspStarHot = Layer("Core", radial, 24f, new Color(1f, 1f, 1f, 0.9f));
            _inspStarFlare2 = Layer("Flare2", StarFlare, 70f, new Color(1f, 0.95f, 0.85f, 0.35f));
            _inspStarFlare2.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
            _inspStarFlare = Layer("Flare", StarFlare, 104f, new Color(1f, 0.97f, 0.9f, 0.85f));
            StellarisClone.Cards.HoloSpin.Add(_inspStarCorona, 3f, 0.12f, 0.04f);
            StellarisClone.Cards.HoloSpin.Add(_inspStarFlare, 0f, 0.18f, 0.05f, 0.4f);
            // Картинка планеты — главная планета системы (или выбранная); звезда тогда светит из-за неё ореолом
            _inspPlanet = Layer("Planet", null, 60f, Color.white);
            _inspPlanet.preserveAspect = true;
            _inspPlanet.gameObject.SetActive(false);
            StellarisClone.Cards.HoloSpin.Add(_inspStarHalo, 0f, 0.22f, 0.06f);
            StellarisClone.Cards.HoloSpin.Add(_inspStarHot, 0f, 0.12f, 0.08f, 0.8f);

            // Значок (для планеты) — поверх диска
            _inspTileIcon = LGIcons.Create(_inspPortrait, LGIcon.Star, 30, DS.Gold, "TileIcon");
            _inspTileIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30));
        }

        /// <summary>Фон шапки: обрезан по шапке, лежит под светом, портретом и надписями.</summary>
        private void BuildInspectorBanner(Transform header)
        {
            _inspBanner = LGBuild.Rect(header, "Banner");
            _inspBanner.Stretch(1, 1, 1, 1);
            _inspBanner.gameObject.AddComponent<RectMask2D>();
            _inspBanner.SetAsFirstSibling();
            LG.Ignore(_inspBanner.gameObject);

            var skyTex = Resources.Load<Texture2D>("Menu/menu_bg");
            var skyGo = new GameObject("Sky", typeof(RectTransform));
            skyGo.transform.SetParent(_inspBanner, false);
            _inspBannerSky = skyGo.AddComponent<RawImage>();
            _inspBannerSky.texture = skyTex;
            _inspBannerSky.color = skyTex != null ? new Color(0.75f, 0.8f, 0.9f, 1f) : new Color(0f, 0f, 0f, 0f);
            _inspBannerSky.raycastTarget = false;
            _inspBannerSky.rectTransform.Stretch();

            _inspBannerStar = LGBuild.Panel(_inspBanner, "StarLight", new Color(1f, 0.9f, 0.6f, 0.5f));
            _inspBannerStar.sprite = FactionSelectScreen.RadialSprite();
            _inspBannerStar.raycastTarget = false;
            _inspBannerStar.rectTransform.At(new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-250, 30), new Vector2(360, 300));

            _inspBannerPlanet = LGBuild.Panel(_inspBanner, "Planet", Color.white);
            _inspBannerPlanet.raycastTarget = false;
            _inspBannerPlanet.preserveAspect = true;
            _inspBannerPlanet.rectTransform.At(new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-70, -18), new Vector2(230, 230));

            // Затемнение слева (под названием) — текст читается на любом кадре
            var shadeL = LGBuild.Panel(_inspBanner, "ShadeLeft", new Color(0.01f, 0.02f, 0.03f, 0.92f));
            shadeL.sprite = FadeRightSprite;
            shadeL.raycastTarget = false;
            shadeL.rectTransform.anchorMin = new Vector2(0, 0); shadeL.rectTransform.anchorMax = new Vector2(0.85f, 1);
            shadeL.rectTransform.offsetMin = shadeL.rectTransform.offsetMax = Vector2.zero;
            foreach (var g in new Graphic[] { _inspBannerSky, _inspBannerStar, _inspBannerPlanet, shadeL }) LG.Ignore(g.gameObject);
        }

        /// <summary>Кадр системы в фоне шапки: свой кусок туманности и свет звезды её цвета.</summary>
        private void PaintInspectorBanner(Color star, Sprite planet, int seed)
        {
            if (_inspBanner == null) return;
            if (_inspBannerSky != null)
            {
                // Каждой системе — свой кусок неба (кадр стабилен для системы)
                float u = Mathf.Abs(seed * 0.137f) % 0.6f, v = Mathf.Abs(seed * 0.291f) % 0.55f;
                _inspBannerSky.uvRect = new Rect(u, v, 0.4f, 0.4f * 96f / InspectorWidth * 2.2f);
            }
            // Фон шапки — только туманность и свет звезды; планеты в кадре нет
            if (_inspBannerStar != null) _inspBannerStar.color = new Color(star.r, star.g, star.b, 0.6f);
            if (_inspBannerPlanet != null) _inspBannerPlanet.gameObject.SetActive(false);
        }

        /// <summary>Цвета портрета: звезда (свет — её цвета) или значок объекта; кольца — цвета владельца.</summary>
        private void PaintInspectorPortrait(bool star, LGIcon icon, Color tint, Color owner, Sprite planet = null)
        {
            if (_inspPortrait == null) return;
            PaintInspectorBanner(tint, planet, planet != null ? planet.GetHashCode() : Mathf.RoundToInt(tint.r * 1000 + tint.b * 37));
            bool hasPlanet = planet != null && !star;
            if (_inspPlanet != null) { _inspPlanet.sprite = planet; _inspPlanet.gameObject.SetActive(hasPlanet); }
            if (hasPlanet) icon = LGIcon.Planet;
            if (_inspStarCore != null) { _inspStarCore.gameObject.SetActive(star); _inspStarCore.color = new Color(tint.r, tint.g, tint.b, 1f); }
            if (_inspStarHot != null) _inspStarHot.gameObject.SetActive(star);
            if (_inspDisc != null) _inspDisc.gameObject.SetActive(!star);
            if (_inspStarCorona != null) { _inspStarCorona.gameObject.SetActive(star); _inspStarCorona.color = new Color(tint.r, tint.g, tint.b, 0.85f); }
            Color flare = Color.Lerp(tint, Color.white, 0.65f);
            if (_inspStarFlare != null) { _inspStarFlare.gameObject.SetActive(star); _inspStarFlare.color = new Color(flare.r, flare.g, flare.b, 0.85f); }
            if (_inspStarFlare2 != null) { _inspStarFlare2.gameObject.SetActive(star); _inspStarFlare2.color = new Color(flare.r, flare.g, flare.b, 0.35f); }
            if (_inspStarHalo != null) _inspStarHalo.color = new Color(tint.r, tint.g, tint.b, star || hasPlanet ? 0.55f : 0.3f);
            if (_inspTileIcon != null)
            {
                _inspTileIcon.gameObject.SetActive(!star && !hasPlanet);
                _inspTileIcon.sprite = LGIcons.Get(icon);
                _inspTileIcon.color = tint;
            }
            if (_inspRingB != null) _inspRingB.color = new Color(owner.r, owner.g, owner.b, 0.7f);
            // Звезда — просто светило в космосе, без голографических колец: кольца с эмблемой — знак звёздной базы,
            // и звезда в кольцах путалась с ней. Кольца остаются у планет и прочих объектов
            if (_inspRingA != null) _inspRingA.gameObject.SetActive(!star);
            if (_inspRingB != null) _inspRingB.gameObject.SetActive(!star);
            if (_inspHeaderGlow != null) _inspHeaderGlow.color = new Color(tint.r, tint.g, tint.b, star || hasPlanet ? 0.16f : 0.08f);
            if (_inspOwnerGlow != null) _inspOwnerGlow.color = new Color(owner.r, owner.g, owner.b, 0.35f);
        }

        /// <summary>Главная планета системы для шапки: колония с наибольшим населением, иначе самая крупная планета.</summary>
        private static PlanetData HeaderPlanet(StarSystem s)
        {
            if (s == null || !s.IsSurveyed || s.Planets == null || s.Planets.Count == 0) return null;
            PlanetData best = null;
            foreach (var p in s.Planets)
                if (p.Population > 0 && (best == null || p.Population > best.Population)) best = p;
            if (best != null) return best;
            foreach (var p in s.Planets)
                if (best == null || p.Size > best.Size) best = p;
            return best;
        }

        /// <summary>
        /// Кнопка в стиле пунктов левого меню игры: тёмное стекло, светящаяся планка цвета действия слева, значок в своей
        /// ячейке, подпись капслоком с разрядкой. Недоступная — приглушена (её причина — в подсказке).
        /// </summary>
        private Button InspGameButton(Transform parent, string name, LGIcon icon, string label, Color accent, bool enabled, System.Action act, Vector2 size)
        {
            var go = CreateButton(parent, name, size, new Color(0.012f, 0.040f, 0.055f, 0.94f), enabled ? accent : new Color(0.5f, 0.55f, 0.6f), act);
            var btn = go.GetComponent<Button>();
            btn.interactable = enabled;
            Color a = enabled ? accent : new Color(0.45f, 0.52f, 0.58f);

            var bar = LGBuild.Panel(go.transform, "Accent", new Color(a.r, a.g, a.b, enabled ? 0.95f : 0.4f));
            bar.raycastTarget = false;
            bar.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(3f, 0f), new Vector2(2.5f, size.y * 0.55f));
            LG.Line(bar.gameObject);

            var tile = LGBuild.Panel(go.transform, "IconTile", new Color(a.r * 0.16f, a.g * 0.16f, a.b * 0.16f, 0.95f));
            tile.raycastTarget = false;
            tile.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(size.y - 12f, size.y - 12f));
            LG.Chip(tile.gameObject, new Color(a.r, a.g, a.b, enabled ? 0.5f : 0.2f));
            var ic = LGIcons.Create(tile.transform, icon, 15f, a);
            ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(15, 15));

            // Подпись по центру кнопки (поля симметричны значку слева, чтобы текст стоял ровно посередине)
            var t = CreateText(go.transform, label.ToUpper(), 11, FontStyle.Bold, enabled ? DS.TextPrimary : DS.TextMuted, TextAnchor.MiddleCenter);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = new Vector2(size.y + 12f, 0);
            t.rectTransform.offsetMax = new Vector2(-(size.x >= 260f ? size.y + 12f : 10f), 0);
            t.Track(1.2f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 8; t.resizeTextMaxSize = 11;
            if (enabled) InspSweep.Add(go, 5.5f, 0.12f);
            return btn;
        }

        /// <summary>Цвет кнопки из её заливки: тёмный оттенок вытягивается до яркого неона того же тона.</summary>
        private static Color AccentOf(Color fill)
        {
            float m = Mathf.Max(0.001f, Mathf.Max(fill.r, Mathf.Max(fill.g, fill.b)));
            var c = new Color(fill.r / m, fill.g / m, fill.b / m);
            return Color.Lerp(c, Color.white, 0.25f);
        }

        /// <summary>
        /// Карточки новой системы появляются каскадом — сверху вниз, одна за другой. Обновление той же системы
        /// (после стройки, действия) — без каскада, чтобы панель не «мигала».
        /// </summary>
        private void InspCascade(int key)
        {
            if (_inspStack == null) return;
            bool fresh = key != _inspCascadeKey || _inspShow < 0.5f;
            _inspCascadeKey = key;
            if (!fresh) return;
            for (int i = 0; i < _inspStack.childCount; i++)
                LGFx.CascadeIn(_inspStack.GetChild(i).gameObject, i, 0.045f);
        }

        /// <summary>Заливка полосы растёт от нуля до значения (при первом показе карточки).</summary>
        private static RectTransform GrowBar(RectTransform host, Color col, float value, float height, bool shimmer = false)
        {
            var fill = LGBuild.Bar(host, col, 0f, height);
            LGBuild.SetBar(fill, value);
            if (shimmer && value > 0.001f) LGFx.Shimmer(fill);
            return fill;
        }
    }

    /// <summary>Блик, изредка пробегающий по элементу (шапка панели, главная кнопка): широкая косая полоса света.</summary>
    public sealed class InspSweep : MonoBehaviour
    {
        public float Period = 6f, Run = 1.1f, Alpha = 0.10f;
        private RectTransform _band;
        private Image _img;
        private float _t;

        public static InspSweep Add(GameObject host, float period = 6f, float alpha = 0.10f)
        {
            var s = host.GetComponent<InspSweep>() ?? host.AddComponent<InspSweep>();
            s.Period = period; s.Alpha = alpha;
            return s;
        }

        private void Start()
        {
            var clip = new GameObject("SweepClip", typeof(RectTransform));
            clip.transform.SetParent(transform, false);
            var crt = (RectTransform)clip.transform;
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(2, 2); crt.offsetMax = new Vector2(-2, -2);
            clip.AddComponent<RectMask2D>();
            LG.Ignore(clip);
            var go = new GameObject("Sweep", typeof(RectTransform));
            go.transform.SetParent(clip.transform, false);
            _band = (RectTransform)go.transform;
            _band.anchorMin = new Vector2(0f, 0.5f); _band.anchorMax = new Vector2(0f, 0.5f);
            _band.localRotation = Quaternion.Euler(0, 0, -18f);
            _img = go.AddComponent<Image>();
            _img.sprite = LGFx.Glint;
            _img.raycastTarget = false;
            _img.color = new Color(1f, 1f, 1f, 0f);
            _t = Random.Range(0f, Period * 0.6f);
        }

        private void Update()
        {
            if (_band == null) return;
            _t += Time.unscaledDeltaTime;
            if (_t > Period) _t -= Period;
            var r = ((RectTransform)transform).rect;
            if (_t > Run) { if (_img.color.a > 0f) _img.color = new Color(1f, 1f, 1f, 0f); return; }
            float p = Mathf.SmoothStep(0f, 1f, _t / Run);
            float w = Mathf.Clamp(r.width * 0.3f, 40f, 160f);
            _band.sizeDelta = new Vector2(w, r.height * 2.2f);
            _band.anchoredPosition = new Vector2(Mathf.Lerp(-w, r.width + w, p), 0f);
            _img.color = new Color(1f, 1f, 1f, Alpha * Mathf.Sin(p * Mathf.PI));
        }
    }

    /// <summary>Число «набегает» от нуля до значения за ~0.7 с (мощь базы и т. п.).</summary>
    public sealed class InspCountUp : MonoBehaviour
    {
        private Text _t;
        private float _target, _time;
        private string _format;
        private const float Duration = 0.7f;

        public static void Run(Text t, float value, string format = "N0")
        {
            if (t == null) return;
            var c = t.GetComponent<InspCountUp>() ?? t.gameObject.AddComponent<InspCountUp>();
            c._t = t; c._target = value; c._format = format; c._time = 0f;
            c.enabled = true;
            t.text = 0f.ToString(format);
        }

        private void Update()
        {
            if (_t == null) { enabled = false; return; }
            _time += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(_time / Duration);
            float e = 1f - Mathf.Pow(1f - p, 3f);
            _t.text = (_target * e).ToString(_format);
            if (p >= 1f) enabled = false;
        }
    }
}
