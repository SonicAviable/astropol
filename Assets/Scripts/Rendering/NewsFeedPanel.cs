using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StellarisClone.Cam;
using StellarisClone.Core;
using DS = StellarisClone.Rendering.UIManager.DS;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// «Эфир» — бегущая строка новостей галактики внизу экрана (между мини-картой и директивами).
    /// Строка медленно прокручивается, при наведении замирает; клик по новости с системой — камера к ней.
    /// Кнопка справа раскрывает над строкой список из девяти последних новостей. Свежая новость
    /// подсвечивается; новости о нас — с золотой кромкой.
    /// </summary>
    public class NewsFeedPanel : MonoBehaviour
    {
        private const float W = 420f, HeadH = 28f, RowH = 38f, Pad = 6f;
        private const float TickerH = 30f, TickerLeft = 280f, TickerRight = 330f, TickerBottom = 8f;
        private const float ScrollSpeed = 42f, ItemGap = 34f;
        private const int Expanded = 9, TickerItems = 8;

        private RectTransform _root, _list, _body, _viewport, _content;
        private static NewsFeedPanel s_instance;

        /// <summary>
        /// Верхний край ленты в правой колонке (над ней встают «Директивы»). Лента теперь внизу по центру —
        /// правую колонку не занимает, поэтому 0.
        /// </summary>
        public static float TopEdge => 0f;
        private Text _counter;
        private Image _chevron, _liveDot;
        private bool _expanded;
        private int _unread;
        private float _scroll, _contentW, _flash;
        private Hover _hover;
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<GameObject> _tickerItems = new List<GameObject>();

        private class Row
        {
            public RectTransform Rt;
            public Image Bg, Edge, Icon;
            public Text Text;
            public NewsItem Item;
            public float Flash;
        }

        private sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public bool On;
            public void OnPointerEnter(PointerEventData e) => On = true;
            public void OnPointerExit(PointerEventData e) => On = false;
            private void OnDisable() => On = false;
        }

        public void Init(Canvas hud)
        {
            s_instance = this;
            _expanded = PlayerPrefs.GetInt("news.list", 0) == 1;
            GalacticNews.Attach();
            GalacticNews.OnPosted += OnPosted;

            // ---- Бегущая строка
            _root = LGBuild.Rect(hud.transform, "[UI] NewsFeed");
            _root.anchorMin = new Vector2(0f, 0f);
            _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(0.5f, 0f);
            _root.offsetMin = new Vector2(TickerLeft, TickerBottom);
            _root.offsetMax = new Vector2(-TickerRight, TickerBottom + TickerH);
            // Свой холст: строка прокручивается и точка «в эфире» пульсирует каждый кадр — без него
            // каждый кадр перестраивался бы весь HUD (пересборка батчей и отсечение маской всех элементов)
            var tickerCanvas = _root.gameObject.AddComponent<Canvas>();
            tickerCanvas.additionalShaderChannels = hud.additionalShaderChannels;
            _root.gameObject.AddComponent<GraphicRaycaster>();
            LG.Ignore(_root.gameObject);
            LG.Motion(_root.gameObject, LGAppear.Kind.SlideUp).distance = 20f;
            _hover = _root.gameObject.AddComponent<Hover>();

            var bg = LGBuild.Panel(_root, "Bg", new Color(0.020f, 0.048f, 0.068f, 0.92f), raycast: true);
            bg.sprite = HudArt.EdgeFade(0.05f);
            bg.rectTransform.Stretch();
            var line = LGBuild.Panel(_root, "Line", new Color(0.37f, 0.82f, 0.75f, 0.45f));
            line.sprite = HudArt.EdgeFade(0.12f);
            line.rectTransform.anchorMin = new Vector2(0f, 1f);
            line.rectTransform.anchorMax = new Vector2(1f, 1f);
            line.rectTransform.pivot = new Vector2(0.5f, 1f);
            line.rectTransform.sizeDelta = new Vector2(0f, 1f);
            line.rectTransform.anchoredPosition = Vector2.zero;

            _liveDot = LGBuild.Panel(_root, "Live", DS.Red);
            _liveDot.sprite = HudArt.Disc();
            _liveDot.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(26f, 0f), new Vector2(7f, 7f));
            var live = LGBuild.Label(_root, Loc.T("ЭФИР"), 12, DS.Gold, TextAnchor.MiddleLeft);
            live.font = GameFont.Display;
            live.Track(2f);
            live.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(36f, 0f), new Vector2(70f, TickerH));

            _viewport = LGBuild.Rect(_root, "Viewport");
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(100f, 0f);
            _viewport.offsetMax = new Vector2(-64f, 0f);
            _viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(24, 0);
            _content = LGBuild.Rect(_viewport, "Content");
            _content.anchorMin = _content.anchorMax = new Vector2(0f, 0.5f);
            _content.pivot = new Vector2(0f, 0.5f);
            _content.sizeDelta = new Vector2(10f, TickerH);

            _counter = LGBuild.Label(_root, "", 10, DS.Gold, TextAnchor.MiddleRight, bold: true);
            _counter.rectTransform.At(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(40f, TickerH));
            var btn = LGBuild.Button(_root, "Expand", DS.BgVisor, new Color(DS.NeonCyan.r, DS.NeonCyan.g, DS.NeonCyan.b, 0.45f), Toggle, null, null, 10, 10f);
            ((RectTransform)btn.transform).At(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(28f, 22f));
            _chevron = LGIcons.Create(btn.transform, LGIcon.Menu, 12f, DS.TextMuted);
            _chevron.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f));
            TooltipHelper.Attach(btn.gameObject, Loc.T("Все последние новости галактики"));

            // ---- Раскрытый список над строкой (у её правого края)
            _list = LGBuild.Rect(_root, "List");
            _list.anchorMin = _list.anchorMax = new Vector2(1f, 1f);
            _list.pivot = new Vector2(1f, 0f);
            _list.anchoredPosition = new Vector2(0f, 8f);
            var lbg = _list.gameObject.AddComponent<Image>();
            lbg.color = new Color(0.012f, 0.026f, 0.040f, 0.92f);
            lbg.raycastTarget = true;
            LG.Glass(_list.gameObject, 12f).SetRim(new Color(0.55f, 0.95f, 0.92f, 0.40f));

            var head = LGBuild.Rect(_list, "Head").TopBand(0f, HeadH);
            var ic = LGIcons.Create(head, LGIcon.Globe, 14f, DS.NeonCyan);
            ic.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(14f, 14f));
            var title = LGBuild.Label(head, Loc.T("НОВОСТИ ГАЛАКТИКИ"), 11, DS.TextMuted, TextAnchor.MiddleLeft, bold: true);
            title.rectTransform.Stretch(30f, 0f, 10f, 0f);

            _body = LGBuild.Rect(_list, "Body");
            _body.anchorMin = new Vector2(0f, 0f);
            _body.anchorMax = new Vector2(1f, 1f);
            _body.offsetMin = new Vector2(Pad, Pad);
            _body.offsetMax = new Vector2(-Pad, -HeadH);

            for (int i = 0; i < Expanded; i++) _rows.Add(MakeRow(i));
            Rebuild();
        }

        private void OnDestroy()
        {
            GalacticNews.OnPosted -= OnPosted;
            if (_root != null) Destroy(_root.gameObject);
        }

        private Row MakeRow(int i)
        {
            var r = new Row();
            r.Bg = LGBuild.Panel(_body, "Row" + i, new Color(1f, 1f, 1f, 0f), raycast: true);
            r.Rt = r.Bg.rectTransform;
            r.Rt.anchorMin = new Vector2(0f, 1f);
            r.Rt.anchorMax = new Vector2(1f, 1f);
            r.Rt.pivot = new Vector2(0.5f, 1f);
            r.Rt.sizeDelta = new Vector2(0f, RowH - 2f);
            r.Rt.anchoredPosition = new Vector2(0f, -i * RowH);

            r.Edge = LGBuild.Panel(r.Rt, "Edge", DS.Gold);
            r.Edge.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(2f, RowH - 10f));

            r.Icon = LGIcons.Create(r.Rt, LGIcon.Globe, 14f, DS.NeonCyan);
            r.Icon.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(14f, 14f));

            r.Text = LGBuild.Label(r.Rt, "", 11, DS.TextPrimary, TextAnchor.MiddleLeft, wrap: true);
            r.Text.rectTransform.Stretch(30f, 1f, 6f, 1f);
            r.Text.verticalOverflow = VerticalWrapMode.Truncate;
            r.Text.lineSpacing = 0.92f;

            var b = r.Bg.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => Focus(r.Item));
            return r;
        }

        private void Toggle()
        {
            _expanded = !_expanded;
            PlayerPrefs.SetInt("news.list", _expanded ? 1 : 0);
            _unread = 0;
            Rebuild();
        }

        private void OnPosted(NewsItem item)
        {
            if (!_expanded) _unread++;
            Rebuild();
            if (_rows.Count > 0) _rows[0].Flash = 1f;
            _flash = 1f;
        }

        private void Rebuild()
        {
            if (_root == null) return;
            var items = GalacticNews.Items;

            // Список (раскрыт)
            _list.gameObject.SetActive(_expanded);
            int filled = Mathf.Max(1, Mathf.Min(Expanded, items.Count));
            _list.sizeDelta = new Vector2(W, HeadH + Pad + filled * RowH);
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                bool on = _expanded && (i < items.Count || (i == 0 && items.Count == 0));
                r.Rt.gameObject.SetActive(on);
                if (!on) continue;
                if (items.Count == 0)
                {
                    r.Item = null;
                    r.Text.text = Loc.T("<color=#7F98A3>В галактике пока тихо. Здесь появятся войны, открытия и слухи соседей.</color>");
                    r.Icon.sprite = LGIcons.Get(LGIcon.Globe);
                    r.Icon.color = DS.TextMuted;
                    r.Edge.enabled = false;
                    continue;
                }
                var it = items[i];
                r.Item = it;
                string date = $"{it.Day:00}.{it.Month:00}.{it.Year}";
                r.Text.text = $"<color=#6F8C97><size=9>{date}</size></color>  {it.Text}";
                r.Icon.sprite = LGIcons.Get(IconOf(it.Kind));
                r.Icon.color = ColorOf(it.Kind);
                r.Edge.enabled = it.AboutPlayer;
            }

            RebuildTicker(items);
            _counter.text = _unread > 0 && !_expanded ? $"+{Mathf.Min(_unread, 99)}" : "";
            _chevron.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _expanded ? 90f : 0f);
        }

        /// <summary>Бегущая строка: последние новости подряд; если не влезают — дублируются для бесшовной прокрутки.</summary>
        private void RebuildTicker(IReadOnlyList<NewsItem> items)
        {
            foreach (var g in _tickerItems) if (g != null) Destroy(g);
            _tickerItems.Clear();
            int n = Mathf.Min(TickerItems, items.Count);
            float x = 0f;
            if (n == 0)
            {
                x = AddTickerItem(null, x, Loc.T("<color=#7F98A3>В галактике пока тихо. Здесь появятся войны, открытия и слухи соседей.</color>"));
                _contentW = 0f;
                _scroll = 0f;
                _content.anchoredPosition = Vector2.zero;
                return;
            }
            for (int i = 0; i < n; i++) x = AddTickerItem(items[i], x, null);
            _contentW = x;
            // Влезает целиком — стоит на месте; иначе второй проход для бесшовного круга
            if (_contentW > _viewport.rect.width + 1f)
                for (int i = 0; i < n; i++) x = AddTickerItem(items[i], x, null);
            else { _contentW = 0f; _scroll = 0f; _content.anchoredPosition = Vector2.zero; }
            _content.sizeDelta = new Vector2(x, TickerH);
        }

        private float AddTickerItem(NewsItem it, float x, string overrideText)
        {
            var rt = LGBuild.Rect(_content, "Item");
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            Color c = it != null ? ColorOf(it.Kind) : DS.TextMuted;
            var ic = LGIcons.Create(rt, it != null ? IconOf(it.Kind) : LGIcon.Globe, 13f, c);
            ic.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(13f, 13f));
            ic.raycastTarget = false;
            string text = overrideText ?? (it.AboutPlayer ? $"<color=#F2C766>{it.Text}</color>" : it.Text);
            var t = LGBuild.Label(rt, text, 12, DS.TextPrimary, TextAnchor.MiddleLeft);
            t.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(10f, TickerH));
            float tw = t.preferredWidth;
            t.rectTransform.sizeDelta = new Vector2(tw, TickerH);
            float w = 20f + tw;
            rt.sizeDelta = new Vector2(w, TickerH);
            rt.anchoredPosition = new Vector2(x, 0f);
            if (it != null)
            {
                var b = rt.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                var captured = it;
                b.onClick.AddListener(() => Focus(captured));
            }
            _tickerItems.Add(rt.gameObject);
            return x + w + ItemGap;
        }

        private void Update()
        {
            GalacticNews.Attach();
            float dt = Time.unscaledDeltaTime;
            foreach (var r in _rows)
            {
                if (r.Flash <= 0f) continue;
                r.Flash = Mathf.Max(0f, r.Flash - dt * 0.6f);
                var c = ColorOf(r.Item != null ? r.Item.Kind : NewsKind.Society);
                r.Bg.color = new Color(c.r, c.g, c.b, 0.22f * r.Flash);
            }

            // Точка «в эфире» дышит; свежая новость — вспышка
            if (_liveDot != null)
            {
                _flash = Mathf.Max(0f, _flash - dt * 0.5f);
                float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
                _liveDot.color = new Color(DS.Red.r, DS.Red.g, DS.Red.b, Mathf.Max(pulse, _flash));
                _liveDot.rectTransform.localScale = Vector3.one * (1f + 0.6f * _flash);
            }

            // Прокрутка (замирает при наведении)
            if (_contentW > 0f && (_hover == null || !_hover.On))
            {
                _scroll += dt * ScrollSpeed;
                if (_scroll >= _contentW) _scroll -= _contentW;
                _content.anchoredPosition = new Vector2(-_scroll, 0f);
            }
        }

        private static void Focus(NewsItem it)
        {
            if (it == null || it.SystemId < 0) return;
            var sys = EmpireStats.GetSystem(it.SystemId);
            if (sys == null || (Vision.Active && !Vision.PlayerKnows(sys.Id))) return;
            var cam = FindAnyObjectByType<StrategyCameraController>();
            if (cam != null && !cam.IsSystemMode) cam.FocusOn(sys.Position);
        }

        private static LGIcon IconOf(NewsKind k) => k switch
        {
            NewsKind.War => LGIcon.Swords,
            NewsKind.Peace => LGIcon.Peace,
            NewsKind.Conquest => LGIcon.Siege,
            NewsKind.Battle => LGIcon.Target,
            NewsKind.Science => LGIcon.Research,
            NewsKind.Colony => LGIcon.Planet,
            NewsKind.Threat => LGIcon.Warning,
            NewsKind.Contact => LGIcon.Handshake,
            _ => LGIcon.Globe
        };

        private static Color ColorOf(NewsKind k) => k switch
        {
            NewsKind.War => DS.Red,
            NewsKind.Battle => new Color(1f, 0.6f, 0.35f),
            NewsKind.Conquest => new Color(1f, 0.6f, 0.35f),
            NewsKind.Peace => DS.Green,
            NewsKind.Colony => DS.Green,
            NewsKind.Science => DS.NeonCyan,
            NewsKind.Threat => new Color(1f, 0.38f, 0.72f),
            NewsKind.Contact => DS.Gold,
            _ => DS.TextMuted
        };
    }
}
