using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Аватар ИИ-советника обучения — робот на голографическом канале связи:
    /// светящиеся глаза (ярче, пока он «говорит»), строки развёртки, бегущая полоса сканирования,
    /// лёгкое мерцание, редкие сбои сигнала с разрывами кадра и звуковая волна под портретом.
    /// Картинка — Resources/UI/Tutor (1342×2000). Всё на unscaled time.
    /// </summary>
    public sealed class TutorAvatar : MonoBehaviour
    {
        private const float SrcW = 1342f, SrcH = 2000f;
        private static readonly Vector2 EyeL = new Vector2(566, 432), EyeR = new Vector2(766, 427);
        private static readonly Vector2 Face = new Vector2(666, 560);
        private const float EyeSize = 70f;                       // диаметр свечения в пикселях исходника
        private const int Bars = 14;

        private static readonly Color Cyan = new Color(0.35f, 0.95f, 0.92f);

        /// <summary>Пока true — глаза ярче, волна «говорит».</summary>
        public bool Speaking;

        private RectTransform _rt;
        private RawImage _art, _scan;
        private Image _eyeL, _eyeR, _sweep, _tint;
        private readonly Image[] _tears = new Image[3];
        private readonly Image[] _bars = new Image[Bars];
        private Rect _uv;
        private float _zoom = 1.25f;
        private float _speak, _seed, _nextGlitch, _glitchStart = -1f, _flash;

        private static Texture2D s_tex, s_scanTex;

        public static TutorAvatar Create(Transform parent, float zoom = 1.25f)
        {
            var rt = LGBuild.Rect(parent, "TutorAvatar");
            rt.Stretch();
            LG.Ignore(rt.gameObject);
            var a = rt.gameObject.AddComponent<TutorAvatar>();
            a._zoom = zoom;
            a.Build();
            return a;
        }

        /// <summary>Короткая вспышка канала — при смене шага обучения.</summary>
        public void Pulse() => _flash = 1f;

        private void Build()
        {
            _rt = (RectTransform)transform;
            _seed = Random.Range(0f, 100f);
            if (s_tex == null) s_tex = Resources.Load<Texture2D>("UI/Tutor");

            var bg = LGBuild.Panel(_rt, "Bg", new Color(0.01f, 0.04f, 0.05f, 1f));
            bg.rectTransform.Stretch();

            _art = new GameObject("Art").AddComponent<RawImage>();
            _art.transform.SetParent(_rt, false);
            _art.rectTransform.Stretch();
            _art.texture = s_tex;
            _art.raycastTarget = false;

            _eyeL = Glow("EyeL");
            _eyeR = Glow("EyeR");

            // Голографический оттенок и вспышки канала
            _tint = LGBuild.Panel(_rt, "Tint", new Color(Cyan.r, Cyan.g, Cyan.b, 0f));
            _tint.rectTransform.Stretch();

            // Строки развёртки
            _scan = new GameObject("Scanlines").AddComponent<RawImage>();
            _scan.transform.SetParent(_rt, false);
            _scan.rectTransform.Stretch();
            _scan.texture = ScanTex();
            _scan.color = new Color(1f, 1f, 1f, 0.16f);
            _scan.raycastTarget = false;

            // Полоса сканирования
            _sweep = LGBuild.Panel(_rt, "Sweep", Color.white);
            _sweep.sprite = MenuArt.SoftDisc;
            _sweep.rectTransform.anchorMin = new Vector2(-0.1f, 0f);
            _sweep.rectTransform.anchorMax = new Vector2(1.1f, 0f);
            _sweep.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _sweep.rectTransform.sizeDelta = new Vector2(0, 26f);

            // Разрывы кадра при сбое
            for (int i = 0; i < _tears.Length; i++)
            {
                _tears[i] = LGBuild.Panel(_rt, "Tear", new Color(Cyan.r, Cyan.g, Cyan.b, 0f));
                _tears[i].rectTransform.anchorMin = new Vector2(0, 0);
                _tears[i].rectTransform.anchorMax = new Vector2(1, 0);
                _tears[i].rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }

            // Снизу затемнение и звуковая волна
            var shade = LGBuild.Panel(_rt, "Shade", new Color(0.01f, 0.03f, 0.04f, 1f));
            shade.sprite = MenuArt.VerticalFade;
            shade.rectTransform.anchorMin = Vector2.zero;
            shade.rectTransform.anchorMax = new Vector2(1, 0.32f);
            shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;

            var wave = LGBuild.Rect(_rt, "Voice");
            wave.anchorMin = new Vector2(0.14f, 0);
            wave.anchorMax = new Vector2(0.86f, 0);
            wave.pivot = new Vector2(0.5f, 0);
            wave.offsetMin = new Vector2(0, 10);
            wave.offsetMax = new Vector2(0, 34);
            for (int i = 0; i < Bars; i++)
            {
                var b = LGBuild.Panel(wave, "Bar", Cyan);
                float x0 = i / (float)Bars, x1 = (i + 1) / (float)Bars;
                b.rectTransform.anchorMin = new Vector2(x0, 0.5f);
                b.rectTransform.anchorMax = new Vector2(x1, 0.5f);
                b.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                b.rectTransform.offsetMin = new Vector2(1.5f, -1f);
                b.rectTransform.offsetMax = new Vector2(-1.5f, 1f);
                _bars[i] = b;
            }

            _nextGlitch = Time.unscaledTime + Random.Range(5f, 10f);
            Frame();
        }

        private Image Glow(string name)
        {
            var g = LGBuild.Panel(_rt, name, Cyan);
            g.sprite = MenuArt.SoftDisc;
            g.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return g;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_art != null) Frame();
        }

        /// <summary>Кадрирование «cover» с приближением к лицу; глаза ставятся по координатам исходника.</summary>
        private void Frame()
        {
            var r = _rt.rect;
            if (r.width < 1f || r.height < 1f) return;
            float texAspect = SrcW / SrcH, aspect = r.width / r.height;
            float w, h;
            if (aspect >= texAspect) { w = 1f; h = texAspect / aspect; }
            else { h = 1f; w = aspect / texAspect; }
            w /= _zoom;
            h /= _zoom;
            float fx = Face.x / SrcW, fy = 1f - Face.y / SrcH;
            _uv = new Rect(Mathf.Clamp(fx - w * 0.5f, 0f, 1f - w), Mathf.Clamp(fy - h * 0.55f, 0f, 1f - h), w, h);
            _art.uvRect = _uv;

            float eye = EyeSize / SrcW / _uv.width * r.width;
            PlaceEye(_eyeL, EyeL, eye);
            PlaceEye(_eyeR, EyeR, eye);
        }

        private void PlaceEye(Image img, Vector2 px, float size)
        {
            var p = new Vector2((px.x / SrcW - _uv.x) / _uv.width, (1f - px.y / SrcH - _uv.y) / _uv.height);
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = p;
            img.rectTransform.anchoredPosition = Vector2.zero;
            img.rectTransform.sizeDelta = new Vector2(size, size);
        }

        private void Update()
        {
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            _speak = Mathf.MoveTowards(_speak, Speaking ? 1f : 0f, dt * 6f);
            _flash = Mathf.MoveTowards(_flash, 0f, dt * 2.2f);

            // Глаза: ровное свечение, при речи — ярче и «модулируется»
            float noise = Mathf.PerlinNoise(t * 7f, _seed);
            float eyeA = 0.35f + 0.12f * Mathf.Sin(t * 2.1f) + _speak * (0.25f + 0.25f * noise) + _flash * 0.4f;
            var ec = new Color(Cyan.r, Cyan.g, Cyan.b, Mathf.Clamp01(eyeA));
            _eyeL.color = ec;
            _eyeR.color = ec;

            // Строки развёртки медленно ползут вверх
            float rows = Mathf.Max(1f, _rt.rect.height / 3f);
            _scan.uvRect = new Rect(0f, -t * 0.6f, 1f, rows);

            // Полоса сканирования — раз в 4 секунды сверху вниз
            float s = Mathf.Repeat(t + _seed, 4f) / 1.6f;
            if (s <= 1f)
            {
                _sweep.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(_rt.rect.height + 20f, -20f, s));
                _sweep.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.22f * Mathf.Sin(s * Mathf.PI));
            }
            else _sweep.color = Color.clear;

            // Сбой сигнала: сдвиг кадра, разрывы, вспышка
            float glitch = 0f;
            if (_glitchStart < 0f && t >= _nextGlitch) _glitchStart = t;
            if (_glitchStart >= 0f)
            {
                float g = t - _glitchStart;
                if (g < 0.22f) glitch = Mathf.Sin(g / 0.22f * Mathf.PI);
                else { _glitchStart = -1f; _nextGlitch = t + Random.Range(6f, 13f); }
            }
            var uv = _uv;
            if (glitch > 0f) uv.x += (Mathf.PerlinNoise(t * 60f, _seed) - 0.5f) * 0.04f * glitch;
            _art.uvRect = uv;
            float flicker = 0.93f + 0.07f * Mathf.PerlinNoise(t * 11f, _seed + 3f);
            _art.color = new Color(flicker, flicker, flicker, 1f);
            for (int i = 0; i < _tears.Length; i++)
            {
                if (glitch <= 0f) { _tears[i].color = Color.clear; continue; }
                float y = Mathf.PerlinNoise(i * 3.1f, t * 9f) * _rt.rect.height;
                _tears[i].rectTransform.anchoredPosition = new Vector2((Mathf.PerlinNoise(t * 40f, i) - 0.5f) * 14f, y);
                _tears[i].rectTransform.sizeDelta = new Vector2(0f, 2f + 6f * Mathf.PerlinNoise(i, t * 20f));
                _tears[i].color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f * glitch);
            }
            _tint.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.05f + 0.12f * glitch + 0.18f * _flash);

            // Звуковая волна
            for (int i = 0; i < Bars; i++)
            {
                float mid = 1f - Mathf.Abs(i - (Bars - 1) * 0.5f) / (Bars * 0.5f);
                float n = Mathf.PerlinNoise(i * 0.7f + _seed, t * 9f);
                float amp = Mathf.Lerp(0.06f, 0.35f + 0.65f * n * (0.4f + 0.6f * mid), _speak);
                float half = Mathf.Max(1f, amp * 12f);
                _bars[i].rectTransform.offsetMin = new Vector2(1.5f, -half);
                _bars[i].rectTransform.offsetMax = new Vector2(-1.5f, half);
                _bars[i].color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f + 0.55f * _speak);
            }
        }

        /// <summary>Строка развёртки: 3 пикселя — один тёмный.</summary>
        private static Texture2D ScanTex()
        {
            if (s_scanTex != null) return s_scanTex;
            s_scanTex = new Texture2D(1, 3, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave
            };
            s_scanTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f));
            s_scanTex.SetPixel(0, 1, new Color(0f, 0f, 0f, 0f));
            s_scanTex.SetPixel(0, 2, new Color(0f, 0f, 0f, 0f));
            s_scanTex.Apply(false, true);
            return s_scanTex;
        }
    }
}
