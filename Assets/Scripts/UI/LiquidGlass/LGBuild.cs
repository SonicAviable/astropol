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

        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> s_bg =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        /// <summary>
        /// Задник окна — затемнённая туманность (Menu/menu_bg) внутри скруглённой маски окна. Не ловит клики,
        /// стекло её не размывает (LG.Ignore). dim — сила затемнения, 0..1. bg — картинка из Resources/WindowBg
        /// (если её нет — туманность меню).
        /// </summary>
        public static RawImage Nebula(Transform root, float radius, float dim = 0.55f, Rect? uv = null, string bg = null)
        {
            string key = bg ?? "";
            if (!s_bg.TryGetValue(key, out var tex) || tex == null)
            {
                tex = bg != null ? Resources.Load<Texture2D>("WindowBg/" + bg) : null;
                if (tex != null) uv = uv ?? new Rect(0f, 0f, 1f, 1f);
                else tex = Resources.Load<Texture2D>("Menu/menu_bg");
                s_bg[key] = tex;
            }
            else if (bg != null && tex.name == bg) uv = uv ?? new Rect(0f, 0f, 1f, 1f);
            var mask = LG.RoundedMask(root, radius);
            mask.SetAsFirstSibling();
            LG.Ignore(mask.gameObject);
            var go = new GameObject("Nebula", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(mask, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<RawImage>();
            img.texture = tex;
            img.uvRect = uv ?? new Rect(0.1f, 0.15f, 0.8f, 0.7f);
            float k = 1f - Mathf.Clamp01(dim);
            img.color = new Color(k, k, k * 1.05f, 1f);
            img.raycastTarget = false;
            // «Атлас»: окна без фоновых картинок — ровный чернильный фон и роза ветров шейдера; объект остаётся
            // (на него ссылается код окон), но невидим
            img.enabled = false;
            return img;
        }

        /// <summary>Цвет искр и падающих звёзд под картинку задника.</summary>
        private static Color FxTint(string bg)
        {
            switch (bg)
            {
                case "espionage":    return new Color(0.85f, 0.60f, 1.00f);
                case "empire":       return new Color(1.00f, 0.86f, 0.55f);
                case "shipdesigner": return new Color(0.475f, 0.816f, 0.749f);
                case "colonies":     return new Color(0.60f, 1.00f, 0.85f);
                default:             return new Color(0.75f, 0.90f, 1.00f);
            }
        }

        public static Image Panel(Transform parent, string name, Color tint, bool raycast = false)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = tint;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>
        /// «Атлас»: заголовок, набранный целиком заглавными («ЗАКОНЫ ИМПЕРИИ»), — обычным регистром («Законы империи»);
        /// заглавная остаётся у первой буквы и после «. Текст с тегами и смешанным регистром не меняется.
        /// </summary>
        public static string Sentence(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('<') >= 0) return s;
            int letters = 0;
            foreach (char ch in s)
            {
                if (!char.IsLetter(ch)) continue;
                if (char.IsLower(ch)) return s;
                letters++;
            }
            if (letters < 4) return s;
            bool cyr = false;
            foreach (char ch in s) if (ch >= '\u0400' && ch <= '\u04FF') { cyr = true; break; }
            var sb = new System.Text.StringBuilder(s.Length);
            bool cap = true;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (char.IsLetter(ch))
                {
                    // Отдельная буква («(L)», «Ф1») и слово с цифрами («F10», «T2») — клавиши и обозначения: остаются заглавными
                    // В русской подписи латинское сокращение («ESC», «HP», «ИИ» не трогаем) тоже остаётся заглавным
                    bool keep = !cap && (IsolatedLetter(s, i) || TokenHasDigit(s, i) || (cyr && ch < 128 && LatinToken(s, i) <= 4));
                    sb.Append(cap || keep ? ch : char.ToLowerInvariant(ch));
                    cap = false;
                }
                else { sb.Append(ch); if (ch == '«' || ch == '.' || ch == '!' || ch == '?') cap = true; }
            }
            return sb.ToString();
        }

        private static bool IsolatedLetter(string s, int i) =>
            (i == 0 || !char.IsLetterOrDigit(s[i - 1])) && (i == s.Length - 1 || !char.IsLetterOrDigit(s[i + 1]));

        private static int LatinToken(string s, int i)
        {
            int a = i, b = i;
            while (a > 0 && char.IsLetter(s[a - 1]) && s[a - 1] < 128) a--;
            while (b < s.Length - 1 && char.IsLetter(s[b + 1]) && s[b + 1] < 128) b++;
            return b - a + 1;
        }

        private static bool TokenHasDigit(string s, int i)
        {
            int a = i, b = i;
            while (a > 0 && char.IsLetterOrDigit(s[a - 1])) a--;
            while (b < s.Length - 1 && char.IsLetterOrDigit(s[b + 1])) b++;
            for (int k = a; k <= b; k++) if (char.IsDigit(s[k])) return true;
            return false;
        }

        private static readonly int[] TypeScale = { 9, 10, 11, 12, 13, 14, 16, 18, 22, 26, 30, 34, 40, 48 };

        /// <summary>
        /// «Атлас»: единая шкала кеглей интерфейса. Промежуточный размер округляется вниз до ближайшей ступени
        /// (15 → 14, 17 → 16, 21 → 18, 24 → 22…), поэтому текст не растёт и не вылезает из рамок; крупнее 48 — как есть.
        /// </summary>
        public static int SnapSize(int size)
        {
            if (size <= TypeScale[0] || size > TypeScale[TypeScale.Length - 1]) return size;
            int best = TypeScale[0];
            foreach (int s in TypeScale) if (s <= size) best = s;
            return best;
        }

        public static Text Label(Transform parent, string text, int size, Color color,
                                 TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false, bool wrap = false)
        {
            var rt = Rect(parent, "Txt");
            rt.Stretch();
            var t = rt.gameObject.AddComponent<Text>();
            // «Атлас»: крупные жирные подписи (заголовки окон, большие числа) — заголовочным шрифтом Spectral;
            // жирный шрифт — настоящий SemiBold-файл, синтетический «жирный» поверх не нужен
            size = SnapSize(size);
            t.font = bold ? (size >= 18 ? GameFont.Display : GameFont.Bold) : GameFont.Regular;
            t.fontSize = size;
            t.fontStyle = FontStyle.Normal;
            if (bold && size >= 18) text = Sentence(text);
            // «Атлас»: белый текст окон — кремовый
            if (color.r > 0.95f && color.g > 0.95f && color.b > 0.95f) color = new Color(0.925f, 0.898f, 0.827f, color.a);
            t.color = color;
            t.text = text;
            t.alignment = anchor;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0.02f, 0.04f, 0.08f, 0.28f);   // «Атлас»: тень едва заметна — текст на ровных панелях чёткий
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
            var track = Panel(parent, "BarTrack", new Color(0.055f, 0.094f, 0.157f, 0.8f));
            track.rectTransform.anchorMin = new Vector2(0, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0, height);
            LG.Platter(track.gameObject, height * 0.5f).FillMultiplier = 2.2f;

            var f = Panel(track.transform, "BarFill", fill);
            var frt = f.rectTransform;
            frt.anchorMin = new Vector2(0, 0);
            frt.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            // Тонкие полоски (до 4 px) — заливка во всю высоту: отступ 1 px съедал её целиком
            float inset = height <= 4f ? 0f : 1f;
            frt.offsetMin = new Vector2(inset, inset);
            frt.offsetMax = new Vector2(-inset, -inset);
            // Тонкие полоски — простая заливка: стеклянный шейдер со скруглением почти не рисовал полосу в 2–3 px
            if (height <= 4f) LG.Ignore(f.gameObject); else LG.Fill(f.gameObject);
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
            var sr = view.gameObject.AddComponent<LGScrollRect>();
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

            // «Атлас»: тонкий индикатор прокрутки у правого края — виден, только когда список длиннее окна
            var sbRt = Rect(view, "ScrollIndicator");
            sbRt.anchorMin = new Vector2(1, 0); sbRt.anchorMax = new Vector2(1, 1);
            sbRt.pivot = new Vector2(1, 0.5f);
            sbRt.sizeDelta = new Vector2(3f, -18f);
            sbRt.anchoredPosition = new Vector2(-1f, 0f);
            var track = sbRt.gameObject.AddComponent<Image>();
            track.color = new Color(0.925f, 0.898f, 0.827f, 0.06f);
            var area = Rect(sbRt, "Area");
            area.Stretch();
            var handle = Rect(area, "Handle");
            handle.Stretch();
            var hImg = handle.gameObject.AddComponent<Image>();
            hImg.color = new Color(0.925f, 0.898f, 0.827f, 0.36f);
            var sb = sbRt.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.handleRect = handle;
            sb.targetGraphic = hImg;
            sb.transition = Selectable.Transition.None;
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            LG.Ignore(sbRt.gameObject);
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
        /// <summary>«Атлас»: подпись над заголовком («ПАРАМЕТРЫ», «ЭКРАН») — моноширинная, золотая, 12 px, с разрядкой.</summary>
        public static Text Eyebrow(this Text t)
        {
            if (t == null) return null;
            t.font = GameFont.MonoMedium;
            t.fontStyle = FontStyle.Normal;
            t.fontSize = 12;
            t.color = new Color(0.906f, 0.757f, 0.439f);
            return t.Track(1.8f);
        }

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
