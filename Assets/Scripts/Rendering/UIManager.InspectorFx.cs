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
        private int _inspCascadeKey = int.MinValue;

        // ==================== СПРАЙТЫ ====================

        private static Sprite s_fadeRight, s_hexTile;
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> s_resSprites = new System.Collections.Generic.Dictionary<string, Sprite>();

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
            var disc = Layer("Disc", FleetIndicator.GetDiscSprite(), 58f, new Color(0.01f, 0.02f, 0.035f, 0.92f));
            _inspStarCore = Layer("Star", radial, 64f, new Color(1f, 0.92f, 0.6f, 1f));
            _inspStarHot = Layer("Core", radial, 26f, new Color(1f, 1f, 1f, 0.95f));
            // Картинка планеты — главная планета системы (или выбранная); звезда тогда светит из-за неё ореолом
            _inspPlanet = Layer("Planet", null, 60f, Color.white);
            _inspPlanet.preserveAspect = true;
            _inspPlanet.gameObject.SetActive(false);
            StellarisClone.Cards.HoloSpin.Add(_inspStarHalo, 0f, 0.22f, 0.06f);
            StellarisClone.Cards.HoloSpin.Add(_inspStarHot, 0f, 0.12f, 0.08f, 0.8f);

            // Значок (для планеты) — поверх диска
            _inspTileIcon = LGIcons.Create(_inspPortrait, LGIcon.Star, 30, DS.Gold, "TileIcon");
            _inspTileIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30));
            _ = disc;
        }

        /// <summary>Цвета портрета: звезда (свет — её цвета) или значок объекта; кольца — цвета владельца.</summary>
        private void PaintInspectorPortrait(bool star, LGIcon icon, Color tint, Color owner, Sprite planet = null)
        {
            if (_inspPortrait == null) return;
            bool hasPlanet = planet != null;
            if (_inspPlanet != null) { _inspPlanet.sprite = planet; _inspPlanet.gameObject.SetActive(hasPlanet); }
            if (hasPlanet) icon = LGIcon.Planet;
            star = star && !hasPlanet;
            if (_inspStarCore != null) { _inspStarCore.gameObject.SetActive(star); _inspStarCore.color = new Color(tint.r, tint.g, tint.b, 1f); }
            if (_inspStarHot != null) _inspStarHot.gameObject.SetActive(star);
            if (_inspStarHalo != null) _inspStarHalo.color = new Color(tint.r, tint.g, tint.b, star || hasPlanet ? 0.55f : 0.3f);
            if (_inspTileIcon != null)
            {
                _inspTileIcon.gameObject.SetActive(!star && !hasPlanet);
                _inspTileIcon.sprite = LGIcons.Get(icon);
                _inspTileIcon.color = tint;
            }
            if (_inspRingB != null) _inspRingB.color = new Color(owner.r, owner.g, owner.b, 0.7f);
            if (_inspHeaderGlow != null) _inspHeaderGlow.color = new Color(tint.r, tint.g, tint.b, star || hasPlanet ? 0.16f : 0.08f);
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
            if (_inspOwnerGlow != null) _inspOwnerGlow.color = new Color(owner.r, owner.g, owner.b, 0.35f);
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
