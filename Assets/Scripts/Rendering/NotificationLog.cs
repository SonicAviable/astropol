using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// «Журнал событий» (левое меню, клавиша O) — все всплывающие сообщения партии, новые сверху: дата, заголовок,
    /// текст. Клик по записи с местом — камера к системе (журнал остаётся открытым). Фильтры: все, важные, с местом.
    /// Счётчик непрочитанного — кружок на значке в меню.
    /// </summary>
    public sealed class NotificationLog : MonoBehaviour
    {
        public static NotificationLog Instance { get; private set; }
        public static bool IsOpenNow => Instance != null && Instance._root != null && Instance._root.activeSelf;

        private enum Filter { All, Important, Located }

        private const float W = 460f;
        private GameObject _root;
        private RectTransform _list;
        private ScrollRect _scroll;
        private Text _count;
        private Filter _filter = Filter.All;
        private bool _dirty;
        private readonly List<(Image bg, Text t, Filter f)> _chips = new List<(Image, Text, Filter)>();

        private static Image s_pill;
        private static Text s_badge;

        public static void Toggle()
        {
            if (Instance == null) new GameObject("[UI] NotificationLog").AddComponent<NotificationLog>();
            if (IsOpenNow) Instance.Close(); else Instance.Open();
        }

        /// <summary>Кружок непрочитанного на значке пункта меню.</summary>
        public static void AttachBadge(Transform tile)
        {
            if (tile == null) return;
            s_pill = LGBuild.Panel(tile, "LogBadge", UIManager.DS.Gold);
            s_pill.rectTransform.At(new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-1, -1), new Vector2(18, 14));
            LG.Platter(s_pill.gameObject, 7f);
            s_badge = LGBuild.Label(s_pill.transform, "", 9, new Color(0.08f, 0.05f, 0.0f), TextAnchor.MiddleCenter, bold: true);
            s_badge.rectTransform.Stretch();
            NotificationCenter.OnLogged -= RefreshBadge;
            NotificationCenter.OnLogged += RefreshBadge;
            RefreshBadge(null);
        }

        private static void RefreshBadge(NotificationCenter.Entry _)
        {
            if (s_pill == null) return;
            int n = IsOpenNow ? 0 : NotificationCenter.Unread;
            s_pill.gameObject.SetActive(n > 0);
            if (s_badge != null) s_badge.text = n > 99 ? "99+" : n.ToString();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            NotificationCenter.OnLogged += OnLogged;
        }

        private void OnDestroy()
        {
            NotificationCenter.OnLogged -= OnLogged;
            if (Instance == this) Instance = null;
        }

        private void OnLogged(NotificationCenter.Entry e) { if (IsOpenNow) { _dirty = true; NotificationCenter.Unread = 0; } }

        private void Update()
        {
            if (!IsOpenNow) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Close(); return; }
            if (_dirty) { _dirty = false; Rebuild(); }
        }

        public void Open()
        {
            if (_root == null) Build();
            if (_root == null) return;
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            NotificationCenter.Unread = 0;
            RefreshBadge(null);
            _dirty = true;
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
            SFXManager.Play(Sfx.WindowOpen, 0.7f);
        }

        public void Close()
        {
            if (_root == null || !_root.activeSelf) return;
            _root.SetActive(false);
            TooltipSystem.Hide();
            RefreshBadge(null);
            SFXManager.Play(Sfx.WindowClose, 0.7f);
        }

        // ================================================================ Построение

        private void Build()
        {
            var canvasObj = GameObject.Find("GalaxyCanvas");
            if (canvasObj == null) return;
            var rt = LGBuild.Rect(canvasObj.transform, "[UI] NotificationLog");
            // Слева, сразу за левым меню (оно открывает журнал) — справа места занимают панель системы и планировщик
            float left = UIManager.NavMenuW + 34f;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(left, 130f); rt.offsetMax = new Vector2(left + W, -70f);
            _root = rt.gameObject;
            var sub = _root.AddComponent<Canvas>();
            sub.overrideSorting = true;
            sub.sortingOrder = 850;   // под всплывающими уведомлениями (900)
            _root.AddComponent<GraphicRaycaster>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.010f, 0.025f, 0.038f, 0.96f);
            bg.raycastTarget = true;
            LG.Glass(_root, 16f).SetRim(new Color(0.55f, 0.95f, 0.92f, 0.40f));
            LG.Motion(_root, LGAppear.Kind.SlideLeft).distance = 26f;

            var ic = LGIcons.Create(rt, LGIcon.Calendar, 16, UIManager.DS.NeonCyan);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(26, -26), new Vector2(16, 16));
            var title = LGBuild.Label(rt, Loc.T("ЖУРНАЛ СОБЫТИЙ"), 15, Color.white, TextAnchor.MiddleLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(44, -26), new Vector2(260, 24));
            title.Track(1.4f);
            _count = LGBuild.Label(rt, "", 11, UIManager.DS.TextMuted, TextAnchor.MiddleRight);
            _count.rectTransform.At(new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-56, -26), new Vector2(140, 20));
            var close = LGBuild.Button(rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 11);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -12), new Vector2(30, 30));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc / O)</color>"));
            var hint = LGBuild.Label(rt, Loc.T("Все сообщения партии. Клик по записи с прицелом — камера к месту события"), 11, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, wrap: true);
            hint.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(20, -54), new Vector2(W - 40, 30));

            var bar = LGBuild.Rect(rt, "Filters");
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(0, 1);
            bar.offsetMin = new Vector2(16, -106); bar.offsetMax = new Vector2(-16, -78);
            float x = 0f;
            x += Chip(bar, Loc.T("Все"), Filter.All, x, 80f);
            x += Chip(bar, Loc.T("Важные"), Filter.Important, x, 104f);
            x += Chip(bar, Loc.T("С местом"), Filter.Located, x, 110f);

            var panel = LGBuild.Rect(rt, "ListHost");
            panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one;
            panel.offsetMin = new Vector2(10, 10); panel.offsetMax = new Vector2(-10, -112);
            _list = LGBuild.ScrollList(panel, 6f, 6);
            _scroll = panel.GetComponentInChildren<ScrollRect>();
            _root.SetActive(false);
        }

        private float Chip(RectTransform parent, string text, Filter f, float x, float w)
        {
            var bg = LGBuild.Panel(parent, "Chip_" + f, new Color(0.05f, 0.11f, 0.14f, 0.9f), raycast: true);
            bg.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(w, 28));
            LG.Chip(bg.gameObject, new Color(1f, 1f, 1f, 0.14f), 14f);
            var t = LGBuild.Label(bg.transform, text, 11, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, bold: true);
            t.rectTransform.Stretch(6, 0, 6, 0);
            var b = bg.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { _filter = f; _dirty = true; SFXManager.Play(Sfx.UiTab, 0.6f); });
            _chips.Add((bg, t, f));
            return w + 8f;
        }

        // ================================================================ Наполнение

        private static Color KindColor(NotificationCenter.Kind k) => k switch
        {
            NotificationCenter.Kind.Success => UIManager.DS.Green,
            NotificationCenter.Kind.Warning => UIManager.DS.Gold,
            NotificationCenter.Kind.Danger => UIManager.DS.Red,
            _ => UIManager.DS.NeonCyan
        };

        private bool Pass(NotificationCenter.Entry e) => _filter switch
        {
            Filter.Important => e.Kind == NotificationCenter.Kind.Warning || e.Kind == NotificationCenter.Kind.Danger,
            Filter.Located => e.SystemId >= 0,
            _ => true
        };

        private void Rebuild()
        {
            foreach (var c in _chips)
            {
                bool on = c.f == _filter;
                c.bg.color = on ? new Color(0.10f, 0.38f, 0.42f, 0.95f) : new Color(0.05f, 0.11f, 0.14f, 0.9f);
                c.t.color = on ? Color.white : UIManager.DS.TextMuted;
            }
            LGBuild.Clear(_list);
            var h = NotificationCenter.History;
            int shown = 0;
            for (int i = h.Count - 1; i >= 0; i--)
            {
                var e = h[i];
                if (e == null || !Pass(e)) continue;
                Row(e);
                shown++;
            }
            _count.text = Loc.F("записей: {0}", h.Count);
            if (shown == 0)
            {
                var row = LGBuild.Rect(_list, "Empty");
                LGBuild.Height(row.gameObject, 40);
                var t = LGBuild.Label(row, h.Count == 0 ? Loc.T("Пока ничего не случилось") : Loc.T("Под фильтр ничего не подходит"), 12, UIManager.DS.TextMuted, TextAnchor.MiddleCenter);
                t.rectTransform.Stretch();
            }
        }

        private void Row(NotificationCenter.Entry e)
        {
            bool located = e.SystemId >= 0;
            bool hasBody = !string.IsNullOrEmpty(e.Body);
            Color accent = KindColor(e.Kind);
            var bg = LGBuild.Panel(_list, "Entry", new Color(0.60f, 0.85f, 0.95f, 0.045f), raycast: true);
            LGBuild.Height(bg.gameObject, hasBody ? 58f : 36f);
            var edge = LGBuild.Panel(bg.transform, "Edge", new Color(accent.r, accent.g, accent.b, 0.85f));
            edge.rectTransform.anchorMin = new Vector2(0, 0); edge.rectTransform.anchorMax = new Vector2(0, 1);
            edge.rectTransform.offsetMin = new Vector2(0, 6); edge.rectTransform.offsetMax = new Vector2(3, -6);

            var t1 = LGBuild.Label(bg.transform, e.Title, 12, accent, hasBody ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, bold: true);
            t1.supportRichText = true;
            t1.rectTransform.Stretch(14, 0, located ? 108 : 84, hasBody ? 8 : 0);
            if (hasBody)
            {
                var t2 = LGBuild.Label(bg.transform, e.Body, 11, UIManager.DS.TextPrimary, TextAnchor.LowerLeft, wrap: true);
                t2.supportRichText = true;
                t2.verticalOverflow = VerticalWrapMode.Truncate;
                t2.rectTransform.Stretch(14, 6, located ? 34 : 12, 26);
            }
            var date = LGBuild.Label(bg.transform, e.Date, 10, UIManager.DS.TextMuted, TextAnchor.UpperRight);
            date.rectTransform.Stretch(0, 0, located ? 34 : 12, 9);

            if (located)
            {
                var pin = LGIcons.Create(bg.transform, LGIcon.Target, 14, accent);
                pin.rectTransform.At(new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-17, 0), new Vector2(14, 14));
                var btn = bg.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.ColorTint;
                var cb = btn.colors;
                cb.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
                btn.colors = cb;
                int id = e.SystemId;
                btn.onClick.AddListener(() =>
                {
                    var sys = EmpireStats.GetSystem(id);
                    if (sys == null) return;
                    SFXManager.Play(Sfx.UiClick, 0.6f);
                    UIManager.Instance?.FocusSystem(sys);
                });
                var sysName = EmpireStats.GetSystem(id)?.Name;
                if (sysName != null) TooltipHelper.Attach(bg.gameObject, Loc.F("Камера к системе {0}", sysName));
            }
        }
    }
}
