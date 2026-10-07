using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StellarisClone.Core;
using StellarisClone.Cam;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    public class GalaxyMinimap : MonoBehaviour
    {
        public static GalaxyMinimap Instance { get; private set; }

        [SerializeField] private float galaxyRadius = 180f;
        [SerializeField] private float mapSize = 240f;
        [SerializeField] private float padding = 22f;

        private RectTransform _root;
        private RectTransform _dotsArea;
        private RectTransform _viewRect;
        private CanvasGroup _group;
        private readonly Dictionary<int, Image> _dots = new Dictionary<int, Image>();
        private GalaxyGenerator _gen;
        private StrategyCameraController _cam;
        private Canvas _canvas;
        private int _lastSystemCount = -1;
        private bool _built;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            // ИСПРАВЛЕНО: строим сразу, не ждём OnGameStarted
            BuildNow();

            if (!UIManager.IsGameStarted)
            {
                SetVisible(false);
                UIManager.OnGameStarted += OnGameStartHandler;
            }
            else
            {
                SetVisible(true);
            }
        }

        private void OnGameStartHandler()
        {
            UIManager.OnGameStarted -= OnGameStartHandler;
            SetVisible(true);
        }

        public void SetVisible(bool v)
        {
            if (_root == null) return;
            // Плавное появление/исчезновение (LGAppear) вместо мгновенного SetActive
            if (v) LG.Show(_root.gameObject);
            else LG.Hide(_root.gameObject);
        }
public static void HideGlobal()
{
    if (Instance != null) Instance.SetVisible(false);
}

public static void ShowGlobal()
{
    if (Instance != null) Instance.SetVisible(true);
}
        private void OnDestroy()
        {
            UIManager.OnGameStarted -= OnGameStartHandler;
        }

        private void BuildNow()
{
    if (_built) return;
    _built = true;

    _cam = FindFirstObjectByType<StrategyCameraController>();

    // Сначала пробуем конкретный игровой Canvas, потом любой
    _canvas = GameObject.Find("GalaxyCanvas")?.GetComponent<Canvas>();
    if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();

    if (_canvas == null) { Debug.LogWarning("[GalaxyMinimap] Canvas не найден."); return; }
    BuildUI();
}

        private void BuildUI()
        {
            var root = new GameObject("GalaxyMinimap");
            root.transform.SetParent(_canvas.transform, false);
            _root = root.AddComponent<RectTransform>();
            _root.anchorMin = _root.anchorMax = new Vector2(1, 0);
            _root.pivot = new Vector2(1, 0);
            _root.sizeDelta = new Vector2(mapSize, mapSize);
            _root.anchoredPosition = new Vector2(-padding, padding);

            _group = root.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = true;

            var bg = root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            LG.Glass(root, 24f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.40f));
            var motion = LG.Motion(root, LGAppear.Kind.SlideDown);
            motion.distance = 30f;

            // Внутренний "колодец" карты — чуть темнее, с мягкой кромкой
            var well = new GameObject("MapWell");
            well.transform.SetParent(root.transform, false);
            var wRt = well.AddComponent<RectTransform>();
            wRt.anchorMin = Vector2.zero; wRt.anchorMax = Vector2.one;
            wRt.offsetMin = new Vector2(8, 8); wRt.offsetMax = new Vector2(-8, -30);
            var wImg = well.AddComponent<Image>();
            wImg.color = UIManager.DS.BgVisor;
            wImg.raycastTarget = false;
            LG.Platter(well, 16f);

            var titleBgGO = new GameObject("TitleBg");
            titleBgGO.transform.SetParent(root.transform, false);
            var tbRt = titleBgGO.AddComponent<RectTransform>();
            tbRt.anchorMin = new Vector2(0, 1);
            tbRt.anchorMax = new Vector2(1, 1);
            tbRt.pivot = new Vector2(0.5f, 1);
            tbRt.sizeDelta = new Vector2(0, 28);
            tbRt.anchoredPosition = Vector2.zero;
            var tbImg = titleBgGO.AddComponent<Image>();
            tbImg.color = new Color(0, 0, 0, 0);
            tbImg.raycastTarget = false;
            LG.Ignore(titleBgGO, includeChildren: false);

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(titleBgGO.transform, false);
            var tRt = titleGO.AddComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = Vector2.zero;

            var t = titleGO.AddComponent<Text>();
            t.font = GameFont.Bold;
            t.text = "◆  КАРТА ГАЛАКТИКИ";
            t.fontSize = 11;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = UIManager.DS.NeonCyan;
            t.raycastTarget = false;
            var outline = titleGO.AddComponent<Outline>();
            outline.effectColor = UIManager.DS.TextShadow;
            outline.effectDistance = new Vector2(1f, -1f);

            var dotsRoot = new GameObject("Dots");
            dotsRoot.transform.SetParent(_root.transform, false);
            _dotsArea = dotsRoot.AddComponent<RectTransform>();
            _dotsArea.anchorMin = Vector2.zero;
            _dotsArea.anchorMax = Vector2.one;
            _dotsArea.offsetMin = new Vector2(10, 10);
            _dotsArea.offsetMax = new Vector2(-10, -28);

            var v = new GameObject("ViewRect");
            v.transform.SetParent(_dotsArea.transform, false);
            _viewRect = v.AddComponent<RectTransform>();
            _viewRect.anchorMin = _viewRect.anchorMax = new Vector2(0.5f, 0.5f);
            _viewRect.pivot = new Vector2(0.5f, 0.5f);
            _viewRect.sizeDelta = new Vector2(24, 24);

            // Рамка обзора камеры — стеклянная линза с неоновой кромкой
            var vBg = v.AddComponent<Image>();
            var nc = UIManager.DS.NeonCyan;
            vBg.color = new Color(nc.r * 0.4f, nc.g * 0.4f, nc.b * 0.4f, 0.9f);
            vBg.raycastTarget = false;
            var vFx = LG.Apply(v, LiquidGlassEffect.Role.Chip, 6f);
            vFx.SetRim(new Color(nc.r, nc.g, nc.b, 0.95f));
            vFx.FillMultiplier = 0.45f;
            vFx.GlowMultiplier = 1f;

            var clicker = root.AddComponent<MinimapClickHandler>();
            clicker.Init(this);
        }

        private void TryBuildDots()
        {
            if (_gen == null)
            {
                _gen = FindFirstObjectByType<GalaxyGenerator>();
                if (_gen == null) return;
                // Масштаб карты под размер галактики (малая/средняя/большая)
                galaxyRadius = Mathf.Max(60f, _gen.GalaxyRadius * 1.125f);
            }
            if (_gen.Systems == null || _gen.Systems.Count == 0) return;
            if (_gen.Systems.Count == _lastSystemCount) return;

            foreach (var kv in _dots) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _dots.Clear();

            foreach (var sys in _gen.Systems)
            {
                var dot = new GameObject($"Dot_{sys.Id}");
                dot.transform.SetParent(_dotsArea.transform, false);
                dot.transform.SetSiblingIndex(0); // точки — под рамкой обзора
                var rt = dot.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(6, 6);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);

                var img = dot.AddComponent<Image>();
                img.color = GetColorForSystem(sys);
                img.raycastTarget = false;
                LG.Dot(dot);

                _dots[sys.Id] = img;
                UpdateDotPosition(sys.Id);
            }

            _lastSystemCount = _gen.Systems.Count;
        }

        private Color GetColorForSystem(StarSystem sys)
        {
            if (sys.OwnerId == 0) return UIManager.DS.NeonCyan;
            if (sys.OwnerId > 0) return FleetIndicator.OwnerColor(sys.OwnerId);
            if (sys.IsSurveyed) return UIManager.DS.TextMuted;
            return new Color(0.45f, 0.60f, 0.75f, 1f);
        }

        private void UpdateDotPosition(int sysId)
        {
            if (!_dots.TryGetValue(sysId, out var img)) return;
            var sys = _gen.Systems[sysId];
            var rt = img.rectTransform;

            float half = (mapSize * 0.5f) - 28f;
            Vector2 norm = new Vector2(sys.Position.x / galaxyRadius, sys.Position.z / galaxyRadius);
            norm = Vector2.ClampMagnitude(norm, 1f);
            rt.anchoredPosition = norm * half;
        }

        private void LateUpdate()
        {
            if (!_built) return;
            TryBuildDots();

            if (_cam == null || _viewRect == null || _gen == null || _gen.Systems.Count == 0) return;

            foreach (var sys in _gen.Systems)
                if (_dots.TryGetValue(sys.Id, out var img))
                    img.color = GetColorForSystem(sys);

            Vector3 camPos = _cam.transform.position;
            float half = (mapSize * 0.5f) - 28f;
            Vector2 norm = new Vector2(camPos.x / galaxyRadius, camPos.z / galaxyRadius);
            norm = Vector2.ClampMagnitude(norm, 1f);
            _viewRect.anchoredPosition = norm * half;

            float zoom = Mathf.InverseLerp(18f, 240f, camPos.y);
            float viewSize = Mathf.Lerp(64f, 16f, zoom);
            _viewRect.sizeDelta = new Vector2(viewSize, viewSize);
        }

        public void HandleClick(Vector2 localPos)
        {
            if (_cam == null) return;
            float half = (mapSize * 0.5f) - 28f;
            Vector2 norm = new Vector2(localPos.x / half, localPos.y / half);
            norm = Vector2.ClampMagnitude(norm, 1f);

            float x = norm.x * galaxyRadius;
            float z = norm.y * galaxyRadius;
            _cam.transform.position = new Vector3(x, _cam.transform.position.y, z - 25f);
        }
    }

    public class MinimapClickHandler : MonoBehaviour, IPointerClickHandler
    {
        private GalaxyMinimap _map;
        public void Init(GalaxyMinimap map) => _map = map;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            var rt = transform as RectTransform;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out local);
            _map?.HandleClick(local);
        }
    }
}