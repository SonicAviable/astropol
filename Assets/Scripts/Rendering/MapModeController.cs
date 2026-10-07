using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    public class MapModeController : MonoBehaviour
    {
        public static MapModeController Instance { get; private set; }

        public enum MapMode { Simple, Political, Resources, Hyperlanes, Explored }

        private Canvas _canvas;
        private Font _font;
        private GalaxyView _galaxyView;
        private MapMode _active = MapMode.Simple;
        private readonly Dictionary<MapMode, Image> _buttonBgs = new Dictionary<MapMode, Image>();
        private readonly Dictionary<MapMode, Image> _buttonAccents = new Dictionary<MapMode, Image>();
        private readonly Dictionary<MapMode, LiquidGlassEffect> _buttonFx = new Dictionary<MapMode, LiquidGlassEffect>();
        private readonly Dictionary<MapMode, Color> _buttonAccentColors = new Dictionary<MapMode, Color>();
        private bool _built;

        // === Поля для скрытия при открытых модалках ===
        private GameObject _rootObj;
        private CanvasGroup _group;

        private static readonly Color AccentOrange = new Color(1.00f, 0.62f, 0.25f);

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            if (!UIManager.IsGameStarted)
            {
                UIManager.OnGameStarted += BuildWhenReady;
                return;
            }
            BuildNow();
        }

        private void OnDestroy()
        {
            UIManager.OnGameStarted -= BuildWhenReady;
            if (Instance == this) Instance = null;
        }

        private void BuildWhenReady()
        {
            UIManager.OnGameStarted -= BuildWhenReady;
            BuildNow();
        }

        private void BuildNow()
{
    if (_built) return;
    _built = true;

    // Сначала ищем игровой Canvas, чтобы не подцепить TooltipRootCanvas
    _canvas = GameObject.Find("GalaxyCanvas")?.GetComponent<Canvas>();
    if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
    if (_canvas == null) return;

    _galaxyView = FindFirstObjectByType<GalaxyView>();

    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
            ?? Font.CreateDynamicFontFromOSFont("Consolas", 13);

    BuildPanel();
    ApplyMode(MapMode.Simple);
}

        private void BuildPanel()
        {
            var root = new GameObject("MapModePanel");
            root.transform.SetParent(_canvas.transform, false);
            _rootObj = root;

            // CanvasGroup — для возможного fade в будущем
            _group = root.AddComponent<CanvasGroup>();

            var rt = root.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(424, 42);
            rt.anchoredPosition = new Vector2(UIManager.TopBarLeft, -66);   // под верхней панелью, правее карточки правителя

            var bg = root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = false;
            LG.Glass(root, 21f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.30f));
            var motion = LG.Motion(root, LGAppear.Kind.SlideUp);
            motion.distance = 16f;

            CreateModeButton(root.transform, MapMode.Simple,     "★", "ПРОСТОЙ",  new Color(0.80f, 0.88f, 0.92f), 6);
            CreateModeButton(root.transform, MapMode.Political,  "◆", "ПОЛИТИКА", UIManager.DS.NeonCyan,  89);
            CreateModeButton(root.transform, MapMode.Resources,  "⬢", "РЕСУРСЫ",  UIManager.DS.Gold,      172);
            CreateModeButton(root.transform, MapMode.Hyperlanes, "⌁", "КОРИДОРЫ", AccentOrange,           255);
            CreateModeButton(root.transform, MapMode.Explored,   "◉", "РАЗВЕДКА", UIManager.DS.Green,     338);
        }

        private void CreateModeButton(Transform parent, MapMode mode, string icon, string label, Color accent, float xPos)
        {
            var btnObj = new GameObject($"MapModeBtn_{mode}");
            btnObj.transform.SetParent(parent, false);

            var rt = btnObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(80, 30);
            rt.anchoredPosition = new Vector2(xPos, 0);

            var bg = btnObj.AddComponent<Image>();
            bg.color = UIManager.DS.BtnNeutral;
            bg.raycastTarget = true;

            var btn = btnObj.AddComponent<Button>();
            var captured = mode;
            btn.onClick.AddListener(() => ApplyMode(captured));
            _buttonFx[mode] = LG.Button(btnObj, new Color(accent.r, accent.g, accent.b, 0.3f));
            _buttonAccentColors[mode] = accent;

            // Светящаяся "капля"-индикатор под активным режимом
            var accentStrip = new GameObject("Accent");
            accentStrip.transform.SetParent(btnObj.transform, false);
            var sRt = accentStrip.AddComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0.5f, 0);
            sRt.anchorMax = new Vector2(0.5f, 0);
            sRt.pivot = new Vector2(0.5f, 0.5f);
            sRt.sizeDelta = new Vector2(26f, 2.2f);
            sRt.anchoredPosition = new Vector2(0, 3.5f);
            var sImg = accentStrip.AddComponent<Image>();
            sImg.color = accent;
            sImg.raycastTarget = false;
            LG.Line(accentStrip);

            LGIcon iconId = mode switch
            {
                MapMode.Political  => LGIcon.MapPolitical,
                MapMode.Resources  => LGIcon.MapResources,
                MapMode.Hyperlanes => LGIcon.MapLanes,
                MapMode.Explored   => LGIcon.MapExplored,
                _                  => LGIcon.MapSimple
            };
            var iconImg = LGIcons.Create(btnObj.transform, iconId, 15f, accent);
            iconImg.rectTransform.anchorMin = iconImg.rectTransform.anchorMax = new Vector2(0, 0.5f);
            iconImg.rectTransform.pivot = new Vector2(0, 0.5f);
            iconImg.rectTransform.anchoredPosition = new Vector2(11, 1);

            var lbl = MakeText(btnObj.transform, label, 9, FontStyle.Bold, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
            lbl.rectTransform.anchorMin = new Vector2(0, 0.5f);
            lbl.rectTransform.anchorMax = new Vector2(1, 0.5f);
            lbl.rectTransform.pivot = new Vector2(0, 0.5f);
            lbl.rectTransform.offsetMin = new Vector2(32, 0);
            lbl.rectTransform.offsetMax = new Vector2(-4, 0);
            lbl.rectTransform.sizeDelta = new Vector2(-36, 20);

            _buttonBgs[mode] = bg;
            _buttonAccents[mode] = sImg;

            string tip = mode switch
            {
                MapMode.Simple     => "Простой режим — чистая карта со спиральными галактиками и тонкими линиями",
                MapMode.Political  => "Политический режим — границы и принадлежность систем",
                MapMode.Resources  => "Режим ресурсов — подсветка залежей энергии и минералов",
                MapMode.Hyperlanes => "Режим коридоров — выделенные гиперлинии",
                MapMode.Explored   => "Режим разведки — изученные и неизученные секторы",
                _ => ""
            };
            TooltipHelper.Attach(btnObj, tip);
        }

        private Text MakeText(Transform parent, string txt, int size, FontStyle style, Color col, TextAnchor anchor)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = style == FontStyle.Bold ? GameFont.Bold : GameFont.Regular;
            t.text = txt;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = col;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.rectTransform.sizeDelta = new Vector2(60, 20);
            var o = go.AddComponent<Outline>();
            o.effectColor = UIManager.DS.TextShadow;
            o.effectDistance = new Vector2(1f, -1f);
            return t;
        }

        private void ApplyMode(MapMode mode)
        {
            _active = mode;

            foreach (var kv in _buttonBgs)
            {
                bool active = kv.Key == mode;
                kv.Value.color = active ? UIManager.DS.BtnPrimary : UIManager.DS.BtnNeutral;
                if (_buttonAccents.TryGetValue(kv.Key, out var acc))
                {
                    var c = acc.color;
                    c.a = active ? 1f : 0.25f;
                    acc.color = c;
                }
                if (_buttonFx.TryGetValue(kv.Key, out var fx) && fx != null
                    && _buttonAccentColors.TryGetValue(kv.Key, out var ac))
                {
                    // Активный режим — яркая неоновая кромка со свечением, остальные — приглушённое стекло
                    fx.SetRim(new Color(ac.r, ac.g, ac.b, active ? 0.85f : 0.22f));
                    fx.GlowMultiplier = active ? 2.2f : 0.6f;
                    fx.FillMultiplier = active ? 1.2f : 0.7f;
                }
            }

            _galaxyView?.SetMapMode(mode);
        }

        // ==================== СКРЫТИЕ ПРИ МОДАЛКАХ ====================

        /// <summary>Показать или скрыть панель фильтров.</summary>
        public void SetVisible(bool visible)
        {
            if (_rootObj == null) return;
            if (visible) LG.Show(_rootObj);
            else LG.Hide(_rootObj);
        }

        /// <summary>Скрыть панель (вызывается из модалок при открытии).</summary>
        public static void HideGlobal()
        {
            if (Instance != null) Instance.SetVisible(false);
        }

        /// <summary>Вернуть панель (вызывается из модалок при закрытии).</summary>
        public static void ShowGlobal()
        {
            if (Instance != null) Instance.SetVisible(true);
        }
    }
}