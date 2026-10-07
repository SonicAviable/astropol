using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Атмосфера главного меню: за галактикой — живое небо (туманности медленно меняют цвет, звёзды,
    /// мягкие цветные облака), поверх — пылинки и боке. Игровые подсказки карты (гиперкоридоры, флоты,
    /// подписи систем, маяк) в меню прячутся — остаётся сама галактика. При уходе из меню всё возвращается.
    /// </summary>
    public class MenuAtmosphere : MonoBehaviour
    {
        private Canvas _sky;
        private TechTreeBackdrop _backdrop;
        private readonly List<(RectTransform rt, Image img, Vector2 basePos, float phase, Color col)> _clouds =
            new List<(RectTransform, Image, Vector2, float, Color)>();
        private struct Mote { public RectTransform Rt; public Image Img; public Vector2 Pos, Vel; public float Size, Phase; }
        private readonly List<Mote> _motes = new List<Mote>();
        private RectTransform _overlay;
        private readonly List<GameObject> _hidden = new List<GameObject>();
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
            var root = (RectTransform)go.transform;
            LG.Ignore(go);

            var bd = LGBuild.Rect(root, "Nebula");
            bd.Stretch();
            _backdrop = bd.gameObject.AddComponent<TechTreeBackdrop>();
            _backdrop.Strength = 2.6f;
            _backdrop.ShowGrid = false;
            _backdrop.Initialize(bd);
            _backdrop.SetColors(Palette[0], Palette[1]);
            _backdrop.SetActive(true);

            // Крупные мягкие облака цвета — медленно плывут
            var rnd = new System.Random(11);
            for (int i = 0; i < 5; i++)
            {
                var img = LGBuild.Panel(root, "Cloud", Color.white);
                img.sprite = FactionSelectScreen.RadialSprite();
                float size = 900f + (float)rnd.NextDouble() * 700f;
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                img.rectTransform.sizeDelta = new Vector2(size * 1.4f, size);
                var basePos = new Vector2(((float)rnd.NextDouble() - 0.3f) * 1600f, ((float)rnd.NextDouble() - 0.5f) * 800f);
                img.rectTransform.anchoredPosition = basePos;
                _clouds.Add((img.rectTransform, img, basePos, (float)rnd.NextDouble() * 10f, Palette[i % Palette.Length]));
            }
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
                c.img.color = new Color(c.col.r, c.col.g, c.col.b, 0.07f + 0.04f * Mathf.Sin(_t * 0.2f + c.phase));
            }

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

        private void HideMapClutter()
        {
            foreach (var name in new[] { "Hyperlanes", "TacticalHologramBeacon", "EmpireLabel_0", "EmpireLabel_1", "EmpireLabel_2", "EmpireLabel_3" })
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
        }

        private void Hide(GameObject go)
        {
            if (go == null || !go.activeSelf) return;
            go.SetActive(false);
            _hidden.Add(go);
        }

        private void OnDestroy()
        {
            foreach (var go in _hidden) if (go != null) go.SetActive(true);
            _hidden.Clear();
        }
    }
}
