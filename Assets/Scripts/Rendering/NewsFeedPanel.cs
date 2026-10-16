using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Cam;
using StellarisClone.Core;
using DS = StellarisClone.Rendering.UIManager.DS;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Лента «Новости галактики» в правом нижнем углу (под планировщиком, справа от панели флота).
    /// Свёрнута — три последние новости, развёрнута — девять. Свежая новость въезжает сверху и подсвечивается;
    /// новости о нас — с золотой кромкой. Клик по новости с системой — камера к системе.
    /// </summary>
    public class NewsFeedPanel : MonoBehaviour
    {
        private const float W = 360f, HeadH = 28f, RowH = 38f, Pad = 6f;
        private const int Collapsed = 3, Expanded = 9;

        private RectTransform _root, _body;
        private Text _counter;
        private Image _chevron;
        private bool _expanded;
        private int _unread;
        private readonly List<Row> _rows = new List<Row>();

        private class Row
        {
            public RectTransform Rt;
            public Image Bg, Edge, Icon;
            public Text Text;
            public NewsItem Item;
            public float Flash;
        }

        public void Init(Canvas hud)
        {
            _expanded = PlayerPrefs.GetInt("news.expanded", 0) == 1;
            GalacticNews.Attach();
            GalacticNews.OnPosted += OnPosted;

            _root = LGBuild.Rect(hud.transform, "[UI] NewsFeed");
            _root.anchorMin = _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 0f);
            _root.anchoredPosition = new Vector2(-10f, 12f);
            var bg = _root.gameObject.AddComponent<Image>();
            bg.color = new Color(0.012f, 0.026f, 0.040f, 0.90f);
            bg.raycastTarget = true;
            LG.Glass(_root.gameObject, 12f).SetRim(new Color(0.55f, 0.95f, 0.92f, 0.40f));
            LG.Motion(_root.gameObject, LGAppear.Kind.SlideRight).distance = 30f;

            // Заголовок: значок, название, счётчик непрочитанного, кнопка «развернуть»
            var head = LGBuild.Rect(_root, "Head").TopBand(0f, HeadH);
            var ic = LGIcons.Create(head, LGIcon.Globe, 14f, DS.NeonCyan);
            ic.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(14f, 14f));
            var title = LGBuild.Label(head, "НОВОСТИ ГАЛАКТИКИ", 11, DS.TextMuted, TextAnchor.MiddleLeft, bold: true);
            title.rectTransform.Stretch(30f, 0f, 60f, 0f);
            _counter = LGBuild.Label(head, "", 10, DS.Gold, TextAnchor.MiddleRight, bold: true);
            _counter.rectTransform.Stretch(0f, 0f, 40f, 0f);

            var btn = LGBuild.Button(head, "Expand", DS.BgVisor, new Color(DS.NeonCyan.r, DS.NeonCyan.g, DS.NeonCyan.b, 0.45f), Toggle, null, null, 10, 10f);
            ((RectTransform)btn.transform).At(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(26f, 20f));
            _chevron = LGIcons.Create(btn.transform, LGIcon.Menu, 12f, DS.TextMuted);
            _chevron.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f));
            TooltipHelper.Attach(btn.gameObject, "Показать больше или меньше новостей");

            _body = LGBuild.Rect(_root, "Body");
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
            PlayerPrefs.SetInt("news.expanded", _expanded ? 1 : 0);
            _unread = 0;
            Rebuild();
        }

        private void OnPosted(NewsItem item)
        {
            _unread++;
            Rebuild();
            if (_rows.Count > 0) _rows[0].Flash = 1f;
        }

        private void Rebuild()
        {
            if (_root == null) return;
            var items = GalacticNews.Items;
            int shown = _expanded ? Expanded : Collapsed;
            int filled = Mathf.Max(1, Mathf.Min(shown, items.Count));
            _root.sizeDelta = new Vector2(W, HeadH + Pad + filled * RowH);

            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                bool on = i < shown && (i < items.Count || (i == 0 && items.Count == 0));
                r.Rt.gameObject.SetActive(on);
                if (!on) continue;
                if (items.Count == 0)
                {
                    r.Item = null;
                    r.Text.text = "<color=#7F98A3>В галактике пока тихо. Здесь появятся войны, открытия и слухи соседей.</color>";
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

            _counter.text = _unread > 0 && !_expanded && items.Count > Collapsed ? $"+{Mathf.Min(_unread, 99)}" : "";
            _chevron.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _expanded ? 90f : 0f);
        }

        private void Update()
        {
            GalacticNews.Attach();
            foreach (var r in _rows)
            {
                if (r.Flash <= 0f) continue;
                r.Flash = Mathf.Max(0f, r.Flash - Time.unscaledDeltaTime * 0.6f);
                var c = ColorOf(r.Item != null ? r.Item.Kind : NewsKind.Society);
                r.Bg.color = new Color(c.r, c.g, c.b, 0.22f * r.Flash);
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
