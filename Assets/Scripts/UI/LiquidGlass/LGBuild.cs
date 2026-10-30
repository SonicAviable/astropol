using System;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Короткие хелперы вёрстки для окон Liquid Glass: прямоугольники, тексты, кнопки,
    /// полосы прогресса, прокручиваемые списки. Всё на явных якорях — без "плывущих" размеров.
    /// </summary>
    public static class LGBuild
    {
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        /// <summary>Растянуть по якорям с отступами (left, bottom, right, top — положительные внутрь).</summary>
        public static RectTransform Stretch(this RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Полоса у верхнего края: отступ сверху и высота, по ширине — с полями.</summary>
        public static RectTransform TopBand(this RectTransform rt, float top, float height, float left = 0, float right = 0)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(left, -top - height);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Горизонтальная колонка (доли ширины родителя), по высоте — целиком.</summary>
        public static RectTransform Column(this RectTransform rt, float x0, float x1, float padL = 0, float padR = 0)
        {
            rt.anchorMin = new Vector2(x0, 0);
            rt.anchorMax = new Vector2(x1, 1);
            rt.offsetMin = new Vector2(padL, 0);
            rt.offsetMax = new Vector2(-padR, 0);
            return rt;
        }

        /// <summary>Фиксированный размер у точки якоря.</summary>
        public static RectTransform At(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color tint, bool raycast = false)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = tint;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color,
                                 TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false, bool wrap = false)
        {
            var rt = Rect(parent, "Txt");
            rt.Stretch();
            var t = rt.gameObject.AddComponent<Text>();
            t.font = bold ? GameFont.Bold : GameFont.Regular;
            t.fontSize = size;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = color;
            t.text = text;
            t.alignment = anchor;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0.02f, 0.04f, 0.5f);
            sh.effectDistance = new Vector2(0f, -1f);
            return t;
        }

        /// <summary>Стеклянная кнопка с иконкой и подписью.</summary>
        public static Button Button(Transform parent, string name, Color tint, Color rim, Action onClick,
                                    LGIcon? icon = null, string label = null, int fontSize = 11, float radius = -1f)
        {
            var img = Panel(parent, name, tint, raycast: true);
            var btn = img.gameObject.AddComponent<UnityEngine.UI.Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            LG.Button(img.gameObject, rim, radius);
            // Кнопки-полосы с подписью — в общем стиле игры (тёмное стекло, подпись по центру, без значка); см. LGGameButton
            img.gameObject.AddComponent<LGGameButton>().Init(icon, rim, tint);

            if (icon.HasValue && string.IsNullOrEmpty(label))
            {
                var ic = LGIcons.Create(img.transform, icon.Value, fontSize + 6, Color.white);
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(fontSize + 6, fontSize + 6));
            }
            else if (icon.HasValue)
            {
                LGIcons.IconLabel(img.transform, icon.Value, label, fontSize, Color.white, Color.white, fontSize + 3);
            }
            else if (!string.IsNullOrEmpty(label))
            {
                Label(img.transform, label, fontSize, Color.white, TextAnchor.MiddleCenter, bold: true);
            }
            return btn;
        }

        /// <summary>Полоса прогресса: стеклянный жёлоб + светящаяся заливка. Возвращает заливку.</summary>
        public static RectTransform Bar(Transform parent, Color fill, float value, float height = 6f)
        {
            var track = Panel(parent, "BarTrack", new Color(0f, 0.02f, 0.04f, 0.8f));
            track.rectTransform.anchorMin = new Vector2(0, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0, height);
            LG.Platter(track.gameObject, height * 0.5f).FillMultiplier = 2.2f;

            var f = Panel(track.transform, "BarFill", fill);
            var frt = f.rectTransform;
            frt.anchorMin = new Vector2(0, 0);
            frt.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            frt.offsetMin = new Vector2(1, 1);
            frt.offsetMax = new Vector2(-1, -1);
            LG.Fill(f.gameObject);
            if (value <= 0.001f) f.enabled = false;
            return frt;
        }

        public static void SetBar(RectTransform fill, float value, Color? color = null)
        {
            if (fill == null) return;
            // Заливка доезжает до нового значения плавно (LGBarFill), цвет меняется сразу
            LGFx.SmoothBar(fill, value);
            if (color.HasValue)
            {
                var img = fill.GetComponent<Image>();
                if (img != null) img.color = color.Value;
            }
        }

        /// <summary>Вертикальный прокручиваемый список. Возвращает контейнер строк.</summary>
        public static RectTransform ScrollList(Transform parent, float spacing = 6f, int padding = 6)
        {
            var view = Rect(parent, "Scroll");
            view.Stretch();
            view.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 12);
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.elasticity = 0.08f;
            sr.inertia = true;
            sr.decelerationRate = 0.12f;
            sr.scrollSensitivity = 36f;
            // прозрачная подложка ловит колесо мыши по всей области
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);

            var content = Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = spacing;
            vlg.padding = new RectOffset(padding, padding, padding, padding);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = view;
            sr.content = content;
            return content;
        }

        public static void Clear(Transform t)
        {
            if (t == null) return;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }
        }

        public static LayoutElement Height(GameObject go, float h)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = h;
            return le;
        }

        /// <summary>Разрядка заголовка (px между буквами) — см. LGTracking.</summary>
        public static Text Track(this Text t, float spacing = 1.6f)
        {
            if (t == null) return null;
            var tr = t.GetComponent<LGTracking>();
            if (tr == null) tr = t.gameObject.AddComponent<LGTracking>();
            tr.Spacing = spacing;
            return t;
        }

        /// <summary>
        /// Заголовок секции в общем стиле игры (как в окне звёздной базы): ромб со свечением, значок, надпись капслоком
        /// с разрядкой и черта, тающая вправо. Заполняет готовую строку row (её размер и место задаёт вызывающий).
        /// </summary>
        public static void SectionDecor(RectTransform row, string title, LGIcon? icon, Color? accent = null, int size = 11)
        {
            Color g = accent ?? UIManager.DS.Gold;
            var glow = Panel(row, "Glow", new Color(g.r, g.g, g.b, 0.35f));
            glow.sprite = FactionSelectScreen.RadialSprite();
            glow.raycastTarget = false;
            glow.rectTransform.At(new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(5, 9), new Vector2(24, 24));
            var dia = Panel(row, "Diamond", g);
            dia.raycastTarget = false;
            dia.rectTransform.At(new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(5, 9), new Vector2(7, 7));
            dia.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
            float x = 16f;
            if (icon.HasValue)
            {
                var ic = LGIcons.Create(row, icon.Value, 13, new Color(g.r, g.g, g.b, 0.85f));
                ic.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 3), new Vector2(13, 13));
                x += 18f;
            }
            var t = Label(row, title.ToUpper(), size, g, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.Stretch(x, 1, 0, 0);
            t.Track(2f);
            var line = Panel(row, "Line", new Color(g.r, g.g, g.b, 0.55f));
            line.sprite = UIManager.FadeRightSprite;
            line.raycastTarget = false;
            line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.pivot = new Vector2(0, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 1.5f);
            line.rectTransform.anchoredPosition = Vector2.zero;
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        public static string Signed(float v, string fmt = "0.#")
        {
            string s = v.ToString(fmt);
            if (v > 0.001f) return $"<color={Hex(UIManager.DS.Green)}>+{s}</color>";
            if (v < -0.001f) return $"<color={Hex(UIManager.DS.Red)}>{s}</color>";
            return $"<color={Hex(UIManager.DS.TextMuted)}>0</color>";
        }
    }
}
