using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Живой задник окна: медленный дрейф и «дыхание» картинки (наезд камеры), мерцающие искры
    /// и редкие падающие звёзды. Всё на unscaledTime — работает и на паузе.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WindowBgFx : MonoBehaviour
    {
        public Color sparkTint = new Color(0.75f, 0.9f, 1f, 1f);
        public int sparkCount = 26;

        private RawImage _img;
        private Rect _baseUv;
        private Color _baseColor;
        private float _seed;

        private RectTransform[] _sparks;
        private Image[] _sparkImg;
        private float[] _phase, _speed, _size;

        private RectTransform _meteor;
        private Image _meteorImg;
        private float _meteorT = -1f, _nextMeteor;
        private Vector2 _meteorFrom, _meteorDir;

        private static Sprite s_dot, s_streak;

        public void Init(RawImage img, Color tint)
        {
            _img = img;
            _baseUv = img.uvRect;
            _baseColor = img.color;
            sparkTint = tint;
            _seed = Random.value * 100f;
            BuildSparks();
            BuildMeteor();
            _nextMeteor = Time.unscaledTime + Random.Range(3f, 7f);
        }

        private void BuildSparks()
        {
            var parent = (RectTransform)_img.transform;
            _sparks = new RectTransform[sparkCount];
            _sparkImg = new Image[sparkCount];
            _phase = new float[sparkCount]; _speed = new float[sparkCount]; _size = new float[sparkCount];
            for (int i = 0; i < sparkCount; i++)
            {
                var go = new GameObject("Spark", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                var rt = (RectTransform)go.transform;
                var a = new Vector2(Random.value, Random.value);
                rt.anchorMin = rt.anchorMax = a;
                _size[i] = Random.Range(3f, 9f);
                rt.sizeDelta = Vector2.one * _size[i];
                var im = go.GetComponent<Image>();
                im.sprite = Dot();
                im.raycastTarget = false;
                _sparks[i] = rt; _sparkImg[i] = im;
                _phase[i] = Random.value * 10f;
                _speed[i] = Random.Range(0.4f, 1.3f);
            }
        }

        private void BuildMeteor()
        {
            var go = new GameObject("Meteor", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_img.transform, false);
            _meteor = (RectTransform)go.transform;
            _meteor.anchorMin = _meteor.anchorMax = Vector2.zero;
            _meteor.sizeDelta = new Vector2(140f, 3f);
            _meteor.pivot = new Vector2(1f, 0.5f);
            _meteorImg = go.GetComponent<Image>();
            _meteorImg.sprite = Streak();
            _meteorImg.raycastTarget = false;
            _meteorImg.color = Color.clear;
        }

        private void Update()
        {
            if (_img == null) return;
            float t = Time.unscaledTime + _seed;

            // Медленный наезд и дрейф: картинка «дышит» на 4%, центр гуляет по эллипсу
            float zoom = 1f - 0.04f * (0.5f + 0.5f * Mathf.Sin(t * 0.05f));
            var uv = _baseUv;
            float w = uv.width * zoom, h = uv.height * zoom;
            float dx = (uv.width - w) * (0.5f + 0.5f * Mathf.Sin(t * 0.031f));
            float dy = (uv.height - h) * (0.5f + 0.5f * Mathf.Cos(t * 0.023f));
            _img.uvRect = new Rect(uv.x + dx, uv.y + dy, w, h);

            // Лёгкое «дыхание» яркости
            float b = 1f + 0.06f * Mathf.Sin(t * 0.35f);
            _img.color = new Color(_baseColor.r * b, _baseColor.g * b, _baseColor.b * b, _baseColor.a);

            var rect = ((RectTransform)_img.transform).rect;
            for (int i = 0; i < _sparks.Length; i++)
            {
                float k = Mathf.Sin(t * _speed[i] + _phase[i]);
                k = Mathf.Max(0f, k); k *= k * k;   // короткие вспышки, большую часть времени тускло
                var c = sparkTint; c.a = 0.08f + 0.75f * k;
                _sparkImg[i].color = c;
                _sparks[i].sizeDelta = Vector2.one * _size[i] * (0.7f + 0.8f * k);
                // Искры медленно всплывают и возвращаются снизу
                var a = _sparks[i].anchorMin;
                a.y += Time.unscaledDeltaTime * 0.004f * _speed[i];
                if (a.y > 1.02f) { a.y = -0.02f; a.x = Random.value; }
                _sparks[i].anchorMin = _sparks[i].anchorMax = a;
            }

            // Падающая звезда раз в несколько секунд
            if (_meteorT < 0f && Time.unscaledTime >= _nextMeteor)
            {
                _meteorT = 0f;
                _meteorFrom = new Vector2(Random.Range(0.2f, 1f) * rect.width, Random.Range(0.55f, 1f) * rect.height);
                _meteorDir = new Vector2(-Random.Range(0.7f, 1f), -Random.Range(0.25f, 0.5f)).normalized;
                _meteor.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(_meteorDir.y, _meteorDir.x) * Mathf.Rad2Deg + 180f);
            }
            if (_meteorT >= 0f)
            {
                _meteorT += Time.unscaledDeltaTime / 0.9f;
                _meteor.anchoredPosition = _meteorFrom + _meteorDir * (_meteorT * rect.width * 0.35f);
                var c = sparkTint; c.a = Mathf.Sin(Mathf.Clamp01(_meteorT) * Mathf.PI) * 0.8f;
                _meteorImg.color = c;
                if (_meteorT >= 1f) { _meteorT = -1f; _meteorImg.color = Color.clear; _nextMeteor = Time.unscaledTime + Random.Range(5f, 11f); }
            }
        }

        private static Sprite Dot()
        {
            if (s_dot != null) return s_dot;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d); a = a * a * a + Mathf.Clamp01(1f - d * 4f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(a)));
                }
            tex.Apply();
            return s_dot = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        private static Sprite Streak()
        {
            if (s_streak != null) return s_streak;
            const int w = 64, h = 8;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float along = x / (float)(w - 1);                 // хвост слева, голова справа
                    float across = 1f - Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, along * along * across));
                }
            tex.Apply();
            return s_streak = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(1f, 0.5f));
        }
    }
}
