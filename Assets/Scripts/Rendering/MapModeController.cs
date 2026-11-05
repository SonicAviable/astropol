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

        public enum MapMode { Simple, Political, Resources, Hyperlanes, Explored, Intel }

        /// <summary>Цвет шпионажа (как в окне шпионажа).</summary>
        public static readonly Color SpyPurple = new Color(0.74f, 0.56f, 1f);

        private Canvas _canvas;
        private Font _font;
        private GalaxyView _galaxyView;
        private MapMode _active = MapMode.Simple;
        public MapMode Active => _active;
        private readonly Dictionary<MapMode, Image> _buttonBgs = new Dictionary<MapMode, Image>();
        private readonly Dictionary<MapMode, Image> _buttonAccents = new Dictionary<MapMode, Image>();
        private readonly Dictionary<MapMode, Color> _buttonAccentColors = new Dictionary<MapMode, Color>();
        private readonly Dictionary<MapMode, Image> _buttonIcons = new Dictionary<MapMode, Image>();
        private readonly Dictionary<MapMode, string> _modeNames = new Dictionary<MapMode, string>();
        private Text _modeCaption;
        private static readonly Color BtnIdle = new Color(0.030f, 0.070f, 0.098f, 0.95f);
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

            // Режимы карты — круглые значки дугой вокруг круглой мини-карты слева внизу (как в референсе).
            // Корень совпадает с мини-картой (240 × 240, отступ 22 — как в GalaxyMinimap).
            var rt = root.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.sizeDelta = new Vector2(240, 240);
            rt.anchoredPosition = new Vector2(22, 22);
            UIAnchors.Register(UIAnchors.MapModes, rt);
            var motion = LG.Motion(root, LGAppear.Kind.SlideUp);
            motion.distance = 16f;

            // Подпись текущего режима над мини-картой (как у диска времени)
            var capGo = new GameObject("ModeCaption");
            capGo.transform.SetParent(root.transform, false);
            _modeCaption = capGo.AddComponent<Text>();
            _modeCaption.font = GameFont.Display;
            _modeCaption.fontSize = 10;
            _modeCaption.color = UIManager.DS.TextMuted;
            _modeCaption.alignment = TextAnchor.MiddleCenter;
            _modeCaption.raycastTarget = false;
            _modeCaption.horizontalOverflow = HorizontalWrapMode.Overflow;
            _modeCaption.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(240f, 16f));
            _modeCaption.Track(2.4f);

            CreateModeButton(root.transform, MapMode.Simple,     "★", Loc.T("ПРОСТОЙ"),  new Color(0.80f, 0.88f, 0.92f), 100f);
            CreateModeButton(root.transform, MapMode.Political,  "◆", Loc.T("ПОЛИТИКА"), UIManager.DS.NeonCyan,  75f);
            CreateModeButton(root.transform, MapMode.Resources,  "⬢", Loc.T("РЕСУРСЫ"),  UIManager.DS.Gold,      50f);
            CreateModeButton(root.transform, MapMode.Hyperlanes, "⌁", Loc.T("КОРИДОРЫ"), AccentOrange,           25f);
            CreateModeButton(root.transform, MapMode.Explored,   "◉", Loc.T("РАЗВЕДКА"), UIManager.DS.Green,     0f);
            CreateModeButton(root.transform, MapMode.Intel,      "✦", Loc.T("ШПИОНАЖ"),  SpyPurple,             -25f);
        }

        /// <summary>Круглая кнопка режима на дуге вокруг мини-карты; angleDeg — угол от центра карты (0 — вправо, 90 — вверх).</summary>
        private void CreateModeButton(Transform parent, MapMode mode, string icon, string label, Color accent, float angleDeg)
        {
            var btnObj = new GameObject($"MapModeBtn_{mode}");
            btnObj.transform.SetParent(parent, false);

            const float R = 156f, D = 34f;   // снаружи безеля мини-карты (радиус 127)
            var rt = btnObj.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(D, D);
            float a = angleDeg * Mathf.Deg2Rad;
            rt.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R;

            // Круглая голо-кнопка: тёмный диск и кольцо цвета режима (активный — залит цветом)
            var bg = btnObj.AddComponent<Image>();
            bg.sprite = HudArt.Disc();
            bg.color = BtnIdle;
            bg.raycastTarget = true;
            LG.Ignore(btnObj);

            var btn = btnObj.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            var captured = mode;
            btn.onClick.AddListener(() => ApplyMode(captured));
            _buttonAccentColors[mode] = accent;
            _modeNames[mode] = label;

            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(btnObj.transform, false);
            var sImg = ringGo.AddComponent<Image>();
            sImg.sprite = HudArt.Ring(0.88f, 1f);
            sImg.color = new Color(accent.r, accent.g, accent.b, 0.45f);
            sImg.raycastTarget = false;
            sImg.rectTransform.anchorMin = Vector2.zero;
            sImg.rectTransform.anchorMax = Vector2.one;
            sImg.rectTransform.offsetMin = sImg.rectTransform.offsetMax = Vector2.zero;

            LGIcon iconId = mode switch
            {
                MapMode.Political  => LGIcon.MapPolitical,
                MapMode.Resources  => LGIcon.MapResources,
                MapMode.Hyperlanes => LGIcon.MapLanes,
                MapMode.Explored   => LGIcon.MapExplored,
                MapMode.Intel      => LGIcon.Betrayal,
                _                  => LGIcon.MapSimple
            };
            var iconImg = LGIcons.Create(btnObj.transform, iconId, 17f, accent);
            iconImg.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(17, 17));
            iconImg.raycastTarget = false;
            _buttonIcons[mode] = iconImg;

            _buttonBgs[mode] = bg;
            _buttonAccents[mode] = sImg;

            string tip = mode switch
            {
                MapMode.Simple     => Loc.T("Простой режим — чистая карта: мягкий контур территорий, без названий империй, коридоры одного цвета"),
                MapMode.Political  => Loc.T("Политический режим — яркие границы и заливка территорий, названия империй, коридоры цветом владельца"),
                MapMode.Resources  => Loc.T("Режим ресурсов — сияние под изученными системами: чем больше, тем богаче; золото — гелий-3, бирюза — титан"),
                MapMode.Hyperlanes => Loc.T("Режим коридоров — яркие толстые гиперкоридоры, оранжевые кольца — узлы с 4 и более коридорами"),
                MapMode.Explored   => Loc.T("Режим разведки — зелёные кольца: системы изучены; янтарные: встречены, но не изучены — туда стоит отправить научный корабль"),
                MapMode.Intel      => Loc.T("Режим шпионажа — кольцо вокруг звезды показывает силу вашей разведсети в системе (полный круг — 100 %), потоки — куда сеть расползается. В остальных режимах на карте видны только портреты агентов"),
                _ => ""
            };
            TooltipHelper.Attach(btnObj, $"<b>{label}</b>\n{tip}");
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

        /// <summary>Переключить режим карты (как кнопкой).</summary>
        public void SetMode(MapMode mode) => ApplyMode(mode);

        private void ApplyMode(MapMode mode)
        {
            _active = mode;

            foreach (var kv in _buttonBgs)
            {
                bool active = kv.Key == mode;
                var ac = _buttonAccentColors.TryGetValue(kv.Key, out var c0) ? c0 : UIManager.DS.NeonCyan;
                kv.Value.color = active ? new Color(ac.r * 0.40f, ac.g * 0.40f, ac.b * 0.40f, 0.96f) : BtnIdle;
                if (_buttonAccents.TryGetValue(kv.Key, out var ring)) ring.color = new Color(ac.r, ac.g, ac.b, active ? 1f : 0.45f);
                if (_buttonIcons.TryGetValue(kv.Key, out var ic)) ic.color = active ? Color.white : ac;
            }
            if (_modeCaption != null && _modeNames.TryGetValue(mode, out var name))
                _modeCaption.text = Loc.F("РЕЖИМ · {0}", name);

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