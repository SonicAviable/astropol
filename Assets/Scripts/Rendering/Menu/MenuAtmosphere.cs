using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Атмосфера главного меню. За галактикой — небо: картинка туманности (Menu/menu_bg) оживлена шейдером
    /// (облака медленно текут и дышат, звёзды мерцают), медленный наезд камеры и параллакс от мыши;
    /// справа — газовый гигант с кольцами (Menu/menu_planet, премультиплицированная альфа) со своим,
    /// более сильным параллаксом, дышащим светом лимба и полярными сияниями; изредка пролетают метеоры.
    /// Без картинки — процедурная туманность. Поверх — пылинки. Игровые подсказки карты в меню прячутся.
    /// </summary>
    [DefaultExecutionOrder(10000)]   // LateUpdate — после всех игровых скриптов, чтобы спрятанное не успело отрисоваться
    public class MenuAtmosphere : MonoBehaviour
    {
        /// <summary>Главное меню открыто: клики по звёздам карты под ним игнорируются.</summary>
        public static bool IsActive { get; private set; }
        private void OnEnable() => IsActive = true;
        private void OnDisable() => IsActive = false;

        private Canvas _sky;
        private RectTransform _skyRoot;
        private TechTreeBackdrop _backdrop;
        private RawImage _nebula, _planet;
        private Material _nebulaMat;
        private float _nebulaAspect = 16f / 9f, _planetAspect = 16f / 9f;
        private Image _limb, _auroraLow, _auroraHigh, _meteor;
        private float _meteorT = -1f, _nextMeteor = 7f;
        private Vector2 _meteorFrom, _meteorTo;
        private Vector2 _mouse;
        private float _cloudAlpha = 1f;
        private readonly List<(RectTransform rt, Image img, Vector2 basePos, float phase, Color col)> _clouds =
            new List<(RectTransform, Image, Vector2, float, Color)>();
        private struct Mote { public RectTransform Rt; public Image Img; public Vector2 Pos, Vel; public float Size, Phase; }
        private readonly List<Mote> _motes = new List<Mote>();
        private RectTransform _overlay;
        private readonly List<GameObject> _hidden = new List<GameObject>();
        private readonly HashSet<GameObject> _hiddenSet = new HashSet<GameObject>();
        private bool _beaconWasOn;
        private float _hideTimer, _t;

        private static readonly Color[] Palette =
        {
            new Color(0.20f, 0.85f, 0.80f),   // бирюза
            new Color(0.45f, 0.30f, 1.00f),   // фиолет
            new Color(1.00f, 0.55f, 0.25f),   // янтарь
            new Color(0.25f, 0.45f, 1.00f)    // синий
        };

        /// <summary>overlay — слой меню, поверх которого (но под кнопками) летают пылинки.</summary>
        public void Init(RectTransform overlay)
        {
            _overlay = overlay;
            BuildSky();
            BuildMotes();
        }

        private void BuildSky()
        {
            var cam = Camera.main;
            var go = new GameObject("[Menu] Sky");
            go.transform.SetParent(transform, false);
            _sky = go.AddComponent<Canvas>();
            if (cam != null)
            {
                // Холст на дальнем плане камеры — рисуется ЗА галактикой
                _sky.renderMode = RenderMode.ScreenSpaceCamera;
                _sky.worldCamera = cam;
                _sky.planeDistance = Mathf.Min(cam.farClipPlane * 0.92f, 4000f);
            }
            else _sky.renderMode = RenderMode.ScreenSpaceOverlay;
            _sky.sortingOrder = -100;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _skyRoot = (RectTransform)go.transform;
            LG.Ignore(go);

            var nebTex = Resources.Load<Texture2D>("Menu/menu_bg");
            var nebShader = Resources.Load<Shader>("Shaders/MenuNebula");
            if (nebTex != null)
            {
                var rt = LGBuild.Rect(_skyRoot, "Nebula");
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                _nebula = rt.gameObject.AddComponent<RawImage>();
                _nebula.texture = nebTex;
                _nebula.raycastTarget = false;
                _nebulaAspect = nebTex.width / (float)Mathf.Max(1, nebTex.height);
                if (nebShader != null)
                {
                    _nebulaMat = new Material(nebShader);
                    _nebula.material = _nebulaMat;
                }
                _cloudAlpha = 0.45f;   // поверх фотографии — только лёгкая цветная дымка
            }
            else
            {
                var bd = LGBuild.Rect(_skyRoot, "Nebula");
                bd.Stretch();
                _backdrop = bd.gameObject.AddComponent<TechTreeBackdrop>();
                _backdrop.Strength = 2.6f;
                _backdrop.ShowGrid = false;
                _backdrop.Initialize(bd);
                _backdrop.SetColors(Palette[0], Palette[1]);
                _backdrop.SetActive(true);
            }

            // Крупные мягкие облака цвета — медленно плывут
            var rnd = new System.Random(11);
            for (int i = 0; i < 5; i++)
            {
                var img = LGBuild.Panel(_skyRoot, "Cloud", Color.white);
                img.sprite = FactionSelectScreen.RadialSprite();
                float size = 900f + (float)rnd.NextDouble() * 700f;
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                img.rectTransform.sizeDelta = new Vector2(size * 1.4f, size);
                var basePos = new Vector2(((float)rnd.NextDouble() - 0.3f) * 1600f, ((float)rnd.NextDouble() - 0.5f) * 800f);
                img.rectTransform.anchoredPosition = basePos;
                _clouds.Add((img.rectTransform, img, basePos, (float)rnd.NextDouble() * 10f, Palette[i % Palette.Length]));
            }

            BuildPlanet();
            BuildMeteor();
        }

        private void BuildPlanet()
        {
            var tex = Resources.Load<Texture2D>("Menu/menu_planet");
            var shader = Resources.Load<Shader>("Shaders/UIPremultiplied");
            if (tex == null || shader == null) return;
            var rt = LGBuild.Rect(_skyRoot, "Planet");
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 0);
            _planet = rt.gameObject.AddComponent<RawImage>();
            _planet.texture = tex;
            _planet.material = new Material(shader);
            _planet.raycastTarget = false;
            _planetAspect = tex.width / (float)Mathf.Max(1, tex.height);

            // Свет на освещённом крае и полярные сияния — мягкие пятна, медленно дышат (координаты — доли картинки)
            _limb = Glow(rt, new Vector2(0.60f, 0.70f), new Vector2(0.20f, 0.30f), new Color(0.75f, 0.92f, 1f, 0f));
            _auroraHigh = Glow(rt, new Vector2(0.70f, 0.74f), new Vector2(0.10f, 0.10f), new Color(0.35f, 1f, 0.8f, 0f));
            _auroraLow = Glow(rt, new Vector2(0.82f, 0.09f), new Vector2(0.16f, 0.14f), new Color(0.35f, 1f, 0.8f, 0f));
        }

        private static Image Glow(RectTransform parent, Vector2 uv, Vector2 size, Color col)
        {
            var img = LGBuild.Panel(parent, "Glow", col);
            img.sprite = FactionSelectScreen.RadialSprite();
            img.raycastTarget = false;
            img.rectTransform.anchorMin = uv - size * 0.5f;
            img.rectTransform.anchorMax = uv + size * 0.5f;
            img.rectTransform.offsetMin = img.rectTransform.offsetMax = Vector2.zero;
            return img;
        }

        private void BuildMeteor()
        {
            const int w = 256, h = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MeteorStreak" };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float dy = (y - (h - 1) * 0.5f) / (h * 0.5f);
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)(w - 1);
                    float a = Mathf.Pow(u, 2.2f) * Mathf.Exp(-dy * dy * (6f + 10f * u));
                    if (u > 0.96f) a = Mathf.Max(a, Mathf.Exp(-dy * dy * 4f) * (1f - (u - 0.96f) * 10f));
                    px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _meteor = LGBuild.Panel(_skyRoot, "Meteor", new Color(0.8f, 0.95f, 1f, 0f));
            _meteor.sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(1f, 0.5f));
            _meteor.raycastTarget = false;
            _meteor.rectTransform.anchorMin = _meteor.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _meteor.rectTransform.pivot = new Vector2(1f, 0.5f);
            _meteor.rectTransform.sizeDelta = new Vector2(260f, 5f);
        }

        private void BuildMotes()
        {
            if (_overlay == null) return;
            var layer = LGBuild.Rect(_overlay, "Motes");
            layer.Stretch();
            LG.Ignore(layer.gameObject);
            var rnd = new System.Random(5);
            for (int i = 0; i < 46; i++)
            {
                var img = LGBuild.Panel(layer, "Mote", new Color(1f, 1f, 1f, 0f));
                img.sprite = FactionSelectScreen.RadialSprite();
                float size = 3f + (float)rnd.NextDouble() * (rnd.NextDouble() < 0.18 ? 30f : 7f);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                img.rectTransform.sizeDelta = new Vector2(size, size);
                _motes.Add(new Mote
                {
                    Rt = img.rectTransform, Img = img, Size = size,
                    Pos = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble()),
                    Vel = new Vector2(((float)rnd.NextDouble() - 0.5f) * 0.004f, 0.004f + (float)rnd.NextDouble() * 0.010f),
                    Phase = (float)rnd.NextDouble() * 10f
                });
            }
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _t += dt;

            // Туманность медленно перетекает между цветами
            float cyc = _t * 0.035f;
            int i0 = Mathf.FloorToInt(cyc) % Palette.Length;
            float f = Mathf.SmoothStep(0f, 1f, cyc - Mathf.Floor(cyc));
            Color a = Color.Lerp(Palette[i0], Palette[(i0 + 1) % Palette.Length], f);
            Color b = Color.Lerp(Palette[(i0 + 1) % Palette.Length], Palette[(i0 + 2) % Palette.Length], f);
            _backdrop?.SetColors(a, b);

            foreach (var c in _clouds)
            {
                c.rt.anchoredPosition = c.basePos + new Vector2(Mathf.Sin(_t * 0.03f + c.phase) * 120f, Mathf.Cos(_t * 0.025f + c.phase) * 60f);
                c.img.color = new Color(c.col.r, c.col.g, c.col.b, (0.07f + 0.04f * Mathf.Sin(_t * 0.2f + c.phase)) * _cloudAlpha);
            }

            AnimateSky(dt);

            if (_overlay != null)
            {
                var size = _overlay.rect.size;
                for (int i = 0; i < _motes.Count; i++)
                {
                    var m = _motes[i];
                    m.Pos += m.Vel * dt;
                    m.Pos.x += Mathf.Sin(_t * 0.3f + m.Phase) * 0.0004f;
                    if (m.Pos.y > 1.05f) { m.Pos.y = -0.05f; m.Pos.x = Mathf.Repeat(m.Pos.x + 0.41f, 1f); }
                    m.Rt.anchoredPosition = new Vector2(m.Pos.x * size.x, m.Pos.y * size.y);
                    float tw = 0.5f + 0.5f * Mathf.Sin(_t * 1.1f + m.Phase * 2f);
                    float big = m.Size > 14f ? 0.25f : 0.8f;
                    m.Img.color = new Color(0.75f, 0.95f, 1f, (0.08f + 0.3f * tw) * big * Mathf.Clamp01(m.Pos.y * 3f));
                    _motes[i] = m;
                }
            }

            // Игровые подсказки карты появляются по ходу загрузки — прячем их периодически
            _hideTimer -= dt;
            if (_hideTimer <= 0f) { _hideTimer = 1f; HideMapClutter(); }
        }

        private void AnimateSky(float dt)
        {
            if (_skyRoot == null) return;
            var size = _skyRoot.rect.size;
            if (size.x < 1f || size.y < 1f) return;

            // мышь сглаженно: -1..1 от центра экрана
            Vector2 m = new Vector2(Input.mousePosition.x / Mathf.Max(1, Screen.width), Input.mousePosition.y / Mathf.Max(1, Screen.height)) * 2f - Vector2.one;
            m = Vector2.ClampMagnitude(m, 1.4f);
            _mouse = Vector2.Lerp(_mouse, m, 1f - Mathf.Exp(-dt * 2.5f));

            if (_nebula != null)
            {
                // медленный наезд и дрейф + лёгкий параллакс
                float zoom = 1.09f + 0.035f * Mathf.Sin(_t * 0.021f);
                float w = Mathf.Max(size.x, size.y * _nebulaAspect) * zoom;
                _nebula.rectTransform.sizeDelta = new Vector2(w, w / _nebulaAspect);
                _nebula.rectTransform.anchoredPosition = -_mouse * 14f +
                    new Vector2(Mathf.Sin(_t * 0.013f) * 26f, Mathf.Cos(_t * 0.011f) * 14f);
                _nebula.rectTransform.localEulerAngles = new Vector3(0, 0, Mathf.Sin(_t * 0.009f) * 0.6f);
                if (_nebulaMat != null) _nebulaMat.SetFloat("_T", _t);
            }

            if (_planet != null)
            {
                // ближе к зрителю — параллакс сильнее; медленно «плывёт»
                float h = size.y * 0.9f;
                float w = h * _planetAspect;
                var rt = _planet.rectTransform;
                rt.sizeDelta = new Vector2(w, h);
                rt.anchoredPosition = new Vector2(w * 0.11f, -h * 0.12f) - _mouse * 38f +
                    new Vector2(Mathf.Sin(_t * 0.04f) * 10f, Mathf.Sin(_t * 0.055f) * 7f);
                rt.localEulerAngles = new Vector3(0, 0, Mathf.Sin(_t * 0.03f) * 0.8f);

                float limb = 0.5f + 0.5f * Mathf.Sin(_t * 0.45f);
                _limb.color = new Color(0.75f, 0.92f, 1f, 0.05f + 0.07f * limb);
                float au = 0.5f + 0.5f * Mathf.Sin(_t * 0.33f + 1.7f);
                float au2 = 0.5f + 0.5f * Mathf.Sin(_t * 0.27f + 4.1f);
                _auroraLow.color = new Color(0.35f, 1f, 0.8f, 0.04f + 0.10f * au);
                _auroraHigh.color = new Color(0.35f, 1f, 0.8f, 0.03f + 0.08f * au2);
            }

            // редкий метеор
            if (_meteor != null)
            {
                if (_meteorT < 0f)
                {
                    _nextMeteor -= dt;
                    if (_nextMeteor <= 0f)
                    {
                        _meteorT = 0f;
                        _nextMeteor = Random.Range(9f, 20f);
                        float x0 = Random.Range(-0.45f, 0.35f) * size.x;
                        float y0 = Random.Range(0.15f, 0.48f) * size.y;
                        var dir = new Vector2(Random.Range(0.6f, 1f), -Random.Range(0.25f, 0.6f)).normalized;
                        _meteorFrom = new Vector2(x0, y0);
                        _meteorTo = _meteorFrom + dir * Random.Range(320f, 560f);
                        _meteor.rectTransform.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                    }
                }
                else
                {
                    _meteorT += dt / 1.1f;
                    float k = Mathf.Clamp01(_meteorT);
                    _meteor.rectTransform.anchoredPosition = Vector2.Lerp(_meteorFrom, _meteorTo, 1f - (1f - k) * (1f - k));
                    float fade = Mathf.Sin(k * Mathf.PI);
                    _meteor.color = new Color(0.8f, 0.95f, 1f, 0.75f * fade);
                    _meteor.rectTransform.sizeDelta = new Vector2(120f + 200f * fade, 4f);
                    if (_meteorT >= 1f) { _meteorT = -1f; _meteor.color = new Color(1, 1, 1, 0); }
                }
            }
        }

        private void HideMapClutter()
        {
            foreach (var name in new[] { "Hyperlanes", "EmpireLabel_0", "EmpireLabel_1", "EmpireLabel_2", "EmpireLabel_3" })
            {
                var go = GameObject.Find(name);
                if (go != null) Hide(go);
            }
            foreach (var fv in FindObjectsByType<FleetView>(FindObjectsSortMode.None)) Hide(fv.gameObject);
            foreach (var np in FindObjectsByType<SystemNameplate>(FindObjectsSortMode.None)) Hide(np.gameObject);
            foreach (var fi in FindObjectsByType<FleetIndicator>(FindObjectsSortMode.None)) Hide(fi.gameObject);
            foreach (var fb in FindObjectsByType<SystemFleetBadge>(FindObjectsSortMode.None)) Hide(fb.gameObject);
            foreach (var pr in FindObjectsByType<SystemProgressRing>(FindObjectsSortMode.None)) Hide(pr.gameObject);
            var fim = FindAnyObjectByType<FleetIndicatorManager>();
            if (fim != null) Hide(fim.gameObject);
            var fro = FindAnyObjectByType<FleetRouteOverlay>();
            if (fro != null) Hide(fro.gameObject);
            var markers = GameObject.Find("SystemMarkers");
            if (markers != null) Hide(markers);
        }

        private void Hide(GameObject go)
        {
            if (go == null || !go.activeSelf) return;
            go.SetActive(false);
            if (_hiddenSet.Add(go)) _hidden.Add(go);
        }

        /// <summary>Игра может снова включить спрятанное (выбор системы, обновление маркеров) — держим выключенным каждый кадр.</summary>
        private void LateUpdate()
        {
            for (int i = 0; i < _hidden.Count; i++)
                if (_hidden[i] != null && _hidden[i].activeSelf) _hidden[i].SetActive(false);
            var beacon = GalaxyView.Instance != null ? GalaxyView.Instance.BeaconRoot : null;
            if (beacon != null && beacon.activeSelf) { beacon.SetActive(false); _beaconWasOn = true; }
        }

        private void OnDestroy()
        {
            foreach (var go in _hidden) if (go != null) go.SetActive(true);
            _hidden.Clear();
            _hiddenSet.Clear();
            if (_beaconWasOn && GalaxyView.Instance != null && GalaxyView.Instance.BeaconRoot != null)
                GalaxyView.Instance.BeaconRoot.SetActive(true);
            if (_nebulaMat != null) Destroy(_nebulaMat);
            if (_planet != null && _planet.material != null) Destroy(_planet.material);
            if (_meteor != null && _meteor.sprite != null) { Destroy(_meteor.sprite.texture); Destroy(_meteor.sprite); }
        }
    }
}
