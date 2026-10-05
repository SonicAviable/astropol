using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Cam;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    public class EmpireOutliner : MonoBehaviour
    {
        public static EmpireOutliner Instance { get; private set; }

        private RectTransform _root;
        private RectTransform _content;
        private Font _uiFont;
        private bool _isCollapsed = false;

        private const float OutlinerWidth = 270f;
        private const float RowHeight = 28f;
        private const float CategoryHeight = 24f;



        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

           _uiFont = GameFont.Regular;
        }

        private void Start()
        {
            // Панель обзора империи на правой границе отключена.
            // Единственный интерфейс аналитики империи — кнопка «★  ОБЗОР ИМПЕРИИ» в левом верхнем HUD.
            enabled = false;
            var canvas = GameObject.Find("GalaxyCanvas")?.GetComponent<Canvas>();
            if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
            if (canvas != null)
            {
                var old = canvas.transform.Find("[UI] StellarisTinyOutliner");
                if (old != null) Destroy(old.gameObject);
            }
        }

        private void BuildOutlinerUI()
        {
            var canvas = GameObject.Find("GalaxyCanvas")?.GetComponent<Canvas>();
            if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) return;

            var old = canvas.transform.Find("[UI] StellarisTinyOutliner");
            if (old != null) Destroy(old.gameObject);

            var outlinerObj = new GameObject("[UI] StellarisTinyOutliner");
            outlinerObj.transform.SetParent(canvas.transform, false);

            _root = outlinerObj.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(1, 0);
            _root.anchorMax = new Vector2(1, 1);
            _root.pivot = new Vector2(1, 1);
            _root.sizeDelta = new Vector2(OutlinerWidth, -50f);
            _root.anchoredPosition = new Vector2(0f, -46f);

            var bgImg = outlinerObj.AddComponent<Image>();
            bgImg.color = UIManager.DS.BgDeep;

            var outline = outlinerObj.AddComponent<Outline>();
            outline.effectColor = UIManager.DS.NeonTeal;
            outline.effectDistance = new Vector2(-1.5f, -1.5f);

            // Шапка Outliner
            var header = new GameObject("Header");
            header.transform.SetParent(outlinerObj.transform, false);
            var hRt = header.AddComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0, 1);
            hRt.anchorMax = new Vector2(1, 1);
            hRt.pivot = new Vector2(0.5f, 1);
            hRt.sizeDelta = new Vector2(0, 26f);
            hRt.anchoredPosition = Vector2.zero;

            var hBg = header.AddComponent<Image>();
            hBg.color = UIManager.DS.BgHeader;

            var title = CreateText(header.transform, "◆  О Б З О Р   И М П Е Р И И", 11, FontStyle.Bold, UIManager.DS.NeonCyan, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = new Vector2(12, 0);

            var collapseBtn = new GameObject("CollapseBtn");
            collapseBtn.transform.SetParent(header.transform, false);
            var cbRt = collapseBtn.AddComponent<RectTransform>();
            cbRt.anchorMin = new Vector2(1, 0.5f);
            cbRt.anchorMax = new Vector2(1, 0.5f);
            cbRt.pivot = new Vector2(1, 0.5f);
            cbRt.sizeDelta = new Vector2(24, 20);
            cbRt.anchoredPosition = new Vector2(-4, 0);

            var btn = collapseBtn.AddComponent<Button>();
            var cbTxt = CreateText(collapseBtn.transform, "▶", 10, FontStyle.Bold, UIManager.DS.NeonCyan, TextAnchor.MiddleCenter);
            cbTxt.rectTransform.sizeDelta = cbRt.sizeDelta;
            btn.onClick.AddListener(() =>
            {
                _isCollapsed = !_isCollapsed;
                _root.anchoredPosition = _isCollapsed ? new Vector2(OutlinerWidth - 26f, -46f) : new Vector2(0f, -46f);
                cbTxt.text = _isCollapsed ? "◀" : "▶";
            });

            // Скролл-контейнер
            var scrollObj = new GameObject("ScrollArea");
            scrollObj.transform.SetParent(outlinerObj.transform, false);
            var sRt = scrollObj.AddComponent<RectTransform>();
            sRt.anchorMin = Vector2.zero;
            sRt.anchorMax = Vector2.one;
            sRt.offsetMin = new Vector2(4, 4);
            sRt.offsetMax = new Vector2(-4, -28);

            var scrollRect = scrollObj.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 24f;

            scrollObj.AddComponent<RectMask2D>();

            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(scrollObj.transform, false);
            _content = contentObj.AddComponent<RectTransform>();
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = new Vector2(1, 1);
            _content.pivot = new Vector2(0.5f, 1);
            _content.sizeDelta = new Vector2(0, 0);

            var vlg = contentObj.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.spacing = 1.5f;

            var csf = contentObj.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = _content;
        }

        public void Refresh()
        {
            if (_content == null) BuildOutlinerUI();
            if (_content == null) return;

            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            var gen = FindAnyObjectByType<GalaxyGenerator>();
            if (gen == null || gen.Systems == null) return;

            var colonies = new List<StarSystem>();
            var shipyards = new List<StarSystem>();
            var starbases = new List<StarSystem>();

            foreach (var s in gen.Systems)
            {
                if (s.OwnerId == 0)
                {
                    if (s.Planets.Count > 0)
                        colonies.Add(s);

                    if (s.HasStarbase)
                    {
                        if (s.Id == 0 || s.HarvestedMinerals >= 10)
                            shipyards.Add(s);
                        else
                            starbases.Add(s);
                    }
                }
            }

            int rowIndex = 0;

            // 1. Колонии / Системы
            if (colonies.Count > 0)
            {
                CreateCategoryHeader("Системы и планеты", colonies.Count);
                foreach (var col in colonies)
                {
                    string sub = $"{col.Planets.Count} небесных тел";
                    string right = (col.HarvestedMinerals > 0 || col.HarvestedEnergy > 0)
                        ? $"◆{col.HarvestedMinerals} ⚡{col.HarvestedEnergy}"
                        : "";
                    CreateCompactRow("●", col.Name, sub, right, rowIndex++, () => SelectSystem(col));
                }
            }

            // 2. Верфи флота
            if (shipyards.Count > 0)
            {
                CreateCategoryHeader("Верфи флота", shipyards.Count);
                foreach (var sy in shipyards)
                {
                    CreateCompactRow("⚙", sy.Name, "Стапели готовы", "⇧ Ур.1", rowIndex++, () => SelectSystem(sy));
                }
            }

            // 3. Форпосты
            if (starbases.Count > 0)
            {
                CreateCategoryHeader("Форпосты границы", starbases.Count);
                foreach (var sb in starbases)
                {
                    CreateCompactRow("⬡", sb.Name, "Орбитальный форпост", $"◆{sb.HarvestedMinerals}", rowIndex++, () => SelectSystem(sb));
                }
            }

            // 4. Военные флоты (безопасный поиск без ошибок компиляции)
            var fleetViews = FindObjectsByType<FleetView>(FindObjectsSortMode.None);
            if (fleetViews != null && fleetViews.Length > 0)
            {
                CreateCategoryHeader("Флоты империи", fleetViews.Length);
                foreach (var fv in fleetViews)
                {
                    CreateCompactRow("▲", fv.gameObject.name, "Боевая эскадра", "⚔ 1.2K", rowIndex++, () =>
                    {
                        var camCtrl = FindAnyObjectByType<StrategyCameraController>();
                        if (camCtrl != null) camCtrl.FocusOn(fv.transform.position);
                    });
                }
            }
        }

        private void CreateCategoryHeader(string title, int count)
        {
            var catObj = new GameObject($"Cat_{title}");
            catObj.transform.SetParent(_content, false);

            var le = catObj.AddComponent<LayoutElement>();
            le.preferredHeight = CategoryHeight;

            catObj.AddComponent<Image>().color = UIManager.DS.BgSlot;
            var catOutline = catObj.AddComponent<Outline>();
            catOutline.effectColor = UIManager.DS.NeonTeal;
            catOutline.effectDistance = new Vector2(0.8f, -0.8f);

            var tLeft = CreateText(catObj.transform, $"▼  {title}", 10, FontStyle.Bold, UIManager.DS.NeonCyan, TextAnchor.MiddleLeft);
            tLeft.rectTransform.anchorMin = Vector2.zero;
            tLeft.rectTransform.anchorMax = new Vector2(0.8f, 1);
            tLeft.rectTransform.offsetMin = new Vector2(8, 0);

            var tRight = CreateText(catObj.transform, count.ToString(), 10, FontStyle.Bold, UIManager.DS.TextMuted, TextAnchor.MiddleRight);
            tRight.rectTransform.anchorMin = new Vector2(0.8f, 0);
            tRight.rectTransform.anchorMax = Vector2.one;
            tRight.rectTransform.offsetMax = new Vector2(-8, 0);

            TooltipHelper.Attach(catObj, $"<b>{title}</b>\nЭлементов: {count}");
        }

        private void CreateCompactRow(string icon, string title, string subtitle, string rightStat, int rowIdx, System.Action onClick)
        {
            var rowObj = new GameObject($"Row_{title}");
            rowObj.transform.SetParent(_content, false);

            var le = rowObj.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;

            var img = rowObj.AddComponent<Image>();
            img.color = (rowIdx % 2 == 0) ? UIManager.DS.BgRowEven : UIManager.DS.BgRowOdd;

            var rowOutline = rowObj.AddComponent<Outline>();
            rowOutline.effectColor = UIManager.DS.NeonTeal;
            rowOutline.effectDistance = new Vector2(0.7f, -0.7f);

            var btn = rowObj.AddComponent<Button>();
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(UIManager.DS.BtnPrimaryHi.r, UIManager.DS.BtnPrimaryHi.g, UIManager.DS.BtnPrimaryHi.b, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var tIcon = CreateText(rowObj.transform, icon, 11, FontStyle.Normal, UIManager.DS.NeonCyan, TextAnchor.MiddleCenter);
            tIcon.rectTransform.anchorMin = tIcon.rectTransform.anchorMax = new Vector2(0, 0.5f);
            tIcon.rectTransform.pivot = new Vector2(0, 0.5f);
            tIcon.rectTransform.sizeDelta = new Vector2(20, 20);
            tIcon.rectTransform.anchoredPosition = new Vector2(6, 0);

            var tName = CreateText(rowObj.transform, title, 10, FontStyle.Bold, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
            tName.rectTransform.anchorMin = new Vector2(0, 0.5f);
            tName.rectTransform.anchorMax = new Vector2(0.65f, 1f);
            tName.rectTransform.offsetMin = new Vector2(26, 0);
            tName.rectTransform.offsetMax = Vector2.zero;

            var tSub = CreateText(rowObj.transform, subtitle, 8, FontStyle.Normal, UIManager.DS.TextMuted, TextAnchor.MiddleLeft);
            tSub.rectTransform.anchorMin = new Vector2(0, 0f);
            tSub.rectTransform.anchorMax = new Vector2(0.65f, 0.55f);
            tSub.rectTransform.offsetMin = new Vector2(26, 0);
            tSub.rectTransform.offsetMax = Vector2.zero;

            if (!string.IsNullOrEmpty(rightStat))
            {
                var tRight = CreateText(rowObj.transform, rightStat, 9, FontStyle.Bold, UIManager.DS.Gold, TextAnchor.MiddleRight);
                tRight.rectTransform.anchorMin = new Vector2(0.65f, 0);
                tRight.rectTransform.anchorMax = Vector2.one;
                tRight.rectTransform.offsetMax = new Vector2(-6, 0);
            }

            TooltipHelper.Attach(rowObj, $"<b>{title}</b>\n{subtitle}\n{rightStat}");
        }

        private void SelectSystem(StarSystem s)
        {
            if (s == null) return;
            var cam = FindAnyObjectByType<StrategyCameraController>();
            if (cam != null) cam.FocusOn(s.Position);
            GalaxyView.Instance?.SelectSystem(s.Id);
            UIManager.Instance?.ShowSystemPanel(s);
        }

        private Text CreateText(Transform parent, string val, int size, FontStyle style, Color col, TextAnchor anchor)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _uiFont;
            t.text = val;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = col;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }
    }
}