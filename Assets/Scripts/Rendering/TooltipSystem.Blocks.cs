using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Блочная разметка подсказки (кино-карта, вариант B). Строки-метки превращаются в элементы:
    ///   [[sec|ЗАГОЛОВОК|#RRGGBB]]                     — подзаголовок раздела (мелко, с разрядкой, цветом);
    ///   [[big|значение|подпись|значение|подпись|…]]   — до трёх крупных чисел с подписями в ряд;
    ///   [[kv|Значок|#RRGGBB|название|значение]]       — строка: значок и название слева, значение справа (Значок «-» — без значка);
    ///   [[ticks]]                                     — разделитель-шкала;
    ///   [[meter|#RRGGBB|доля 0..1|подпись слева|подпись справа]] — шкала заполнения (склад) с подписями под ней;
    ///   [[src|Значок|#RRGGBB|название|значение|доля 0..1]]   — источник: значок, название, полоска доли и значение;
    ///                                                   доля 0 — строка приглушена (источника пока нет);
    ///   [[total|подпись|значение|#RRGGBB]]           — итог: выделенная полоса во всю ширину.
    /// Остальные строки идут обычным текстом (со значками [[Icon:#hex]]). Подсказки без меток рисуются как раньше.
    /// </summary>
    public partial class TooltipSystem
    {
        private const float BlockGap = 6f;

        private sealed class TextBlock
        {
            public Text Text;
            public readonly List<(int full, int visible, LGIcon icon, Color c)> Marks = new List<(int, int, LGIcon, Color)>();
            public readonly List<Image> Icons = new List<Image>();
        }

        private RectTransform _blocks;
        private readonly List<TextBlock> _textPool = new List<TextBlock>();
        private readonly List<Text> _secPool = new List<Text>();
        private readonly List<(RectTransform rt, Text[] values, Text[] caps)> _bigPool = new List<(RectTransform, Text[], Text[])>();
        private readonly List<(RectTransform rt, Image icon, Text label, Text value)> _kvPool = new List<(RectTransform, Image, Text, Text)>();
        private readonly List<Image> _tickPool = new List<Image>();
        private int _usedText, _usedSec, _usedBig, _usedKv, _usedTick, _usedMeter, _usedSrc, _usedTotal;
        private readonly List<(RectTransform rt, Image track, Image fill, Text left, Text right)> _meterPool = new List<(RectTransform, Image, Image, Text, Text)>();
        private readonly List<(RectTransform rt, Image icon, Text label, Image track, Image fill, Text value)> _srcPool = new List<(RectTransform, Image, Text, Image, Image, Text)>();
        private readonly List<(RectTransform rt, Image bg, Text label, Text value)> _totalPool = new List<(RectTransform, Image, Text, Text)>();
        private const float SrcBarW = 86f, SrcValueW = 52f;

        private static bool HasBlocks(string body) =>
            body.Contains("[[sec|") || body.Contains("[[big|") || body.Contains("[[kv|") || body.Contains("[[ticks]]")
            || body.Contains("[[meter|") || body.Contains("[[src|") || body.Contains("[[total|");

        private void EnsureBlocksRoot()
        {
            if (_blocks != null) return;
            _blocks = Rt(_root, "Blocks");
            _blocks.anchorMin = _blocks.anchorMax = new Vector2(0f, 1f);
            _blocks.pivot = new Vector2(0f, 1f);
            _blocks.anchoredPosition = Vector2.zero;
            _blocks.sizeDelta = Vector2.zero;
            _band.SetAsLastSibling();
            _frame.transform.SetAsLastSibling();
        }

        private void HideBlocks()
        {
            if (_blocks != null) _blocks.gameObject.SetActive(false);
        }

        private struct Line { public int Kind; public string[] F; public string Text; }   // 0 текст, 1 sec, 2 big, 3 kv, 4 ticks, 5 meter, 6 src, 7 total

        private static List<Line> ParseBlocks(string body)
        {
            var res = new List<Line>();
            var plain = new System.Text.StringBuilder();
            void Flush()
            {
                if (plain.Length == 0) return;
                string t = plain.ToString().Trim('\n');
                if (t.Trim().Length > 0) res.Add(new Line { Kind = 0, Text = t });
                plain.Clear();
            }
            foreach (var raw in body.Split('\n'))
            {
                string l = raw.Trim();
                int kind = l == "[[ticks]]" ? 4
                         : l.StartsWith("[[sec|") && l.EndsWith("]]") ? 1
                         : l.StartsWith("[[big|") && l.EndsWith("]]") ? 2
                         : l.StartsWith("[[kv|") && l.EndsWith("]]") ? 3
                         : l.StartsWith("[[meter|") && l.EndsWith("]]") ? 5
                         : l.StartsWith("[[src|") && l.EndsWith("]]") ? 6
                         : l.StartsWith("[[total|") && l.EndsWith("]]") ? 7 : 0;
                if (kind == 0) { if (plain.Length > 0) plain.Append('\n'); plain.Append(raw); continue; }
                Flush();
                string inner = kind == 4 ? "" : l.Substring(2, l.Length - 4);
                var f = inner.Split('|');
                res.Add(new Line { Kind = kind, F = f });
            }
            Flush();
            return res;
        }

        /// <summary>Разложить блоки под шапкой; вернуть размер панели.</summary>
        private void LayoutBlocks(string body, float top, float headW, out float w, out float h)
        {
            EnsureBlocksRoot();
            _blocks.gameObject.SetActive(true);
            _usedText = _usedSec = _usedBig = _usedKv = _usedTick = _usedMeter = _usedSrc = _usedTotal = 0;
            var lines = ParseBlocks(body);

            // 1) содержимое и нужная ширина
            float need = headW;
            var built = new List<(int kind, RectTransform rt, float h, TextBlock tb)>();
            foreach (var ln in lines)
            {
                switch (ln.Kind)
                {
                    case 0:
                    {
                        var tb = TakeText();
                        tb.Text.text = ParseIcons(ln.Text, tb.Marks);
                        need = Mathf.Max(need, Mathf.Min(tb.Text.preferredWidth, 360f) + Pad * 2f + 16f);
                        built.Add((0, tb.Text.rectTransform, 0f, tb));
                        break;
                    }
                    case 1:
                    {
                        var t = TakeSec();
                        t.text = ln.F.Length > 1 ? ln.F[1].ToUpper() : "";
                        Color c = new Color(0.56f, 0.66f, 0.71f);
                        if (ln.F.Length > 2) ColorUtility.TryParseHtmlString(ln.F[2], out c);
                        t.color = c;
                        need = Mathf.Max(need, t.preferredWidth + t.text.Length * 2f + Pad * 2f);
                        built.Add((1, t.rectTransform, 16f, null));
                        break;
                    }
                    case 2:
                    {
                        var b = TakeBig();
                        float x = 0f;
                        for (int i = 0; i < 3; i++)
                        {
                            int vi = 1 + i * 2;
                            bool on = vi < ln.F.Length && ln.F[vi].Length > 0;
                            b.values[i].gameObject.SetActive(on);
                            b.caps[i].gameObject.SetActive(on);
                            if (!on) continue;
                            b.values[i].text = ln.F[vi];
                            b.caps[i].text = vi + 1 < ln.F.Length ? ln.F[vi + 1] : "";
                            float cw = Mathf.Max(b.values[i].preferredWidth, b.caps[i].preferredWidth);
                            b.values[i].rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, 0f), new Vector2(cw + 4f, 26f));
                            b.caps[i].rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -27f), new Vector2(cw + 4f, 14f));
                            x += cw + 20f;
                        }
                        need = Mathf.Max(need, x + Pad * 2f);
                        built.Add((2, b.rt, 42f, null));
                        break;
                    }
                    case 3:
                    {
                        var r = TakeKv();
                        Color c = UIManager.DS.TextPrimary;
                        if (ln.F.Length > 2) ColorUtility.TryParseHtmlString(ln.F[2], out c);
                        bool hasIcon = ln.F.Length > 1 && System.Enum.TryParse(ln.F[1], out LGIcon icon) && ln.F[1] != "-";
                        r.icon.gameObject.SetActive(hasIcon);
                        if (hasIcon) { System.Enum.TryParse(ln.F[1], out LGIcon ic2); r.icon.sprite = LGIcons.Get(ic2); r.icon.color = c; }
                        r.label.text = ln.F.Length > 3 ? ln.F[3] : "";
                        r.label.rectTransform.offsetMin = new Vector2(hasIcon ? 20f : 0f, 0f);
                        r.value.text = ln.F.Length > 4 ? ln.F[4] : "";
                        r.value.color = c;
                        need = Mathf.Max(need, (hasIcon ? 20f : 0f) + r.label.preferredWidth + r.value.preferredWidth + 30f + Pad * 2f);
                        built.Add((3, r.rt, 22f, null));
                        break;
                    }
                    case 5:
                    {
                        var m = TakeMeter();
                        Color c = UIManager.DS.NeonCyan;
                        if (ln.F.Length > 1) ColorUtility.TryParseHtmlString(ln.F[1], out c);
                        float k = ln.F.Length > 2 ? Mathf.Clamp01(ParseF(ln.F[2])) : 0f;
                        m.fill.color = c;
                        m.rt.GetComponent<MeterAnim>().SetTarget(k);
                        m.left.text = ln.F.Length > 3 ? ln.F[3] : "";
                        m.right.text = ln.F.Length > 4 ? ln.F[4] : "";
                        need = Mathf.Max(need, m.left.preferredWidth + m.right.preferredWidth + 30f + Pad * 2f);
                        built.Add((5, m.rt, 28f, null));
                        break;
                    }
                    case 6:
                    {
                        var r = TakeSrc();
                        Color c = UIManager.DS.TextPrimary;
                        if (ln.F.Length > 2) ColorUtility.TryParseHtmlString(ln.F[2], out c);
                        float k = ln.F.Length > 5 ? Mathf.Clamp01(ParseF(ln.F[5])) : 0f;
                        bool on = k > 0.0001f;
                        Color dim = new Color(0.33f, 0.40f, 0.43f, 1f);
                        bool hasIcon = ln.F.Length > 1 && ln.F[1] != "-" && System.Enum.TryParse(ln.F[1], out LGIcon icon);
                        r.icon.gameObject.SetActive(hasIcon);
                        if (hasIcon) { System.Enum.TryParse(ln.F[1], out LGIcon ic2); r.icon.sprite = LGIcons.Get(ic2); r.icon.color = on ? c : dim; }
                        r.label.text = ln.F.Length > 3 ? ln.F[3] : "";
                        r.label.color = on ? UIManager.DS.TextPrimary : dim;
                        r.value.text = ln.F.Length > 4 ? ln.F[4] : "";
                        r.value.color = on ? c : dim;
                        r.fill.color = c;
                        r.fill.gameObject.SetActive(on);
                        r.fill.rectTransform.anchorMax = new Vector2(Mathf.Max(0.02f, k), 1f);
                        r.label.rectTransform.offsetMin = new Vector2(hasIcon ? 22f : 0f, 0f);
                        r.label.rectTransform.offsetMax = new Vector2(-(SrcBarW + SrcValueW + 14f), 0f);
                        need = Mathf.Max(need, (hasIcon ? 22f : 0f) + r.label.preferredWidth + SrcBarW + SrcValueW + 24f + Pad * 2f);
                        built.Add((6, r.rt, 22f, null));
                        break;
                    }
                    case 7:
                    {
                        var t = TakeTotal();
                        Color c = UIManager.DS.Green;
                        if (ln.F.Length > 3) ColorUtility.TryParseHtmlString(ln.F[3], out c);
                        t.label.text = ln.F.Length > 1 ? ln.F[1] : "";
                        t.value.text = ln.F.Length > 2 ? ln.F[2] : "";
                        t.value.color = c;
                        t.bg.color = new Color(c.r, c.g, c.b, 0.08f);
                        need = Mathf.Max(need, t.label.preferredWidth + t.value.preferredWidth + 40f + Pad * 2f);
                        built.Add((7, t.rt, 34f, null));
                        break;
                    }
                    default:
                    {
                        var t = TakeTick();
                        built.Add((4, t.rectTransform, 5f, null));
                        break;
                    }
                }
            }
            w = Mathf.Clamp(need, 260f, 400f);
            float inner = w - Pad * 2f;

            // 2) вертикальная раскладка
            float y = top;
            int prev = -1;
            foreach (var b in built)
            {
                float gap = prev < 0 ? 0f : b.kind == 1 ? 10f : b.kind == 4 || prev == 4 ? 8f : BlockGap;
                if (prev == 1) gap = 4f;
                if (prev == 3 && b.kind == 3) gap = 0f;
                if (prev == 6 && b.kind == 6) gap = 0f;
                if (b.kind == 7) gap = 10f;
                y += gap;
                float bh = b.h;
                if (b.kind == 0)
                {
                    b.rt.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(inner, 10f));
                    bh = b.tb.Text.preferredHeight;
                    b.rt.sizeDelta = new Vector2(inner, bh);
                    PlaceIcons(b.tb.Text, b.tb.Marks, b.tb.Icons, new Vector2(inner, bh));
                }
                else b.rt.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(inner, bh));
                y += bh;
                prev = b.kind;
            }
            h = y + Pad + 2f;

            for (int i = _usedText; i < _textPool.Count; i++) _textPool[i].Text.gameObject.SetActive(false);
            for (int i = _usedSec; i < _secPool.Count; i++) _secPool[i].gameObject.SetActive(false);
            for (int i = _usedBig; i < _bigPool.Count; i++) _bigPool[i].rt.gameObject.SetActive(false);
            for (int i = _usedKv; i < _kvPool.Count; i++) _kvPool[i].rt.gameObject.SetActive(false);
            for (int i = _usedTick; i < _tickPool.Count; i++) _tickPool[i].gameObject.SetActive(false);
            for (int i = _usedMeter; i < _meterPool.Count; i++) _meterPool[i].rt.gameObject.SetActive(false);
            for (int i = _usedSrc; i < _srcPool.Count; i++) _srcPool[i].rt.gameObject.SetActive(false);
            for (int i = _usedTotal; i < _totalPool.Count; i++) _totalPool[i].rt.gameObject.SetActive(false);
        }

        private static float ParseF(string s)
            => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;

        private (RectTransform rt, Image track, Image fill, Text left, Text right) TakeMeter()
        {
            if (_usedMeter >= _meterPool.Count)
            {
                var rt = Rt(_blocks, "Meter");
                var track = Img(rt, "Track", null, new Color(1f, 1f, 1f, 0.07f));
                track.rectTransform.anchorMin = new Vector2(0f, 1f); track.rectTransform.anchorMax = new Vector2(1f, 1f);
                track.rectTransform.pivot = new Vector2(0.5f, 1f); track.rectTransform.sizeDelta = new Vector2(0f, 7f);
                track.rectTransform.anchoredPosition = Vector2.zero;
                var fill = Img(track.rectTransform, "Fill", null, Color.white);
                fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0.1f, 1f);
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                var l = Txt(rt, GameFont.Regular, 11, UIManager.DS.TextMuted, FontStyle.Normal);
                l.supportRichText = true;
                l.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(300f, 14f));
                var r = Txt(rt, GameFont.Regular, 11, UIManager.DS.TextMuted, FontStyle.Normal);
                r.supportRichText = true;
                r.alignment = TextAnchor.MiddleRight;
                r.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0), Vector2.zero, new Vector2(300f, 14f));
                rt.gameObject.AddComponent<MeterAnim>().Fill = fill;
                _meterPool.Add((rt, track, fill, l, r));
            }
            var m = _meterPool[_usedMeter++];
            m.rt.gameObject.SetActive(true);
            return m;
        }

        private (RectTransform rt, Image icon, Text label, Image track, Image fill, Text value) TakeSrc()
        {
            if (_usedSrc >= _srcPool.Count)
            {
                var rt = Rt(_blocks, "Src");
                var ic = Img(rt, "Icon", null, Color.white);
                ic.preserveAspect = true;
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(15f, 15f));
                var l = Txt(rt, GameFont.Regular, 13, UIManager.DS.TextPrimary, FontStyle.Normal);
                l.supportRichText = true;
                l.rectTransform.Stretch();
                var track = Img(rt, "Track", null, new Color(1f, 1f, 1f, 0.06f));
                track.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-(SrcValueW + 8f), 0f), new Vector2(SrcBarW, 6f));
                var fill = Img(track.rectTransform, "Fill", null, Color.white);
                fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                var v = Txt(rt, GameFont.Bold, 13, Color.white, FontStyle.Bold);
                v.supportRichText = true;
                v.alignment = TextAnchor.MiddleRight;
                v.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(SrcValueW, 18f));
                _srcPool.Add((rt, ic, l, track, fill, v));
            }
            var r = _srcPool[_usedSrc++];
            r.rt.gameObject.SetActive(true);
            return r;
        }

        private (RectTransform rt, Image bg, Text label, Text value) TakeTotal()
        {
            if (_usedTotal >= _totalPool.Count)
            {
                var rt = Rt(_blocks, "Total");
                var bg = Img(rt, "Bg", null, Color.white);
                bg.rectTransform.Stretch(-Pad, 0f, -Pad, 0f);   // во всю ширину панели, за поля
                var l = Txt(rt, GameFont.Bold, 13, UIManager.DS.TextPrimary, FontStyle.Bold);
                l.supportRichText = true;
                l.rectTransform.Stretch();
                var v = Txt(rt, GameFont.Display, 18, Color.white, FontStyle.Normal);
                v.supportRichText = true;
                v.alignment = TextAnchor.MiddleRight;
                v.rectTransform.Stretch();
                _totalPool.Add((rt, bg, l, v));
            }
            var t = _totalPool[_usedTotal++];
            t.rt.gameObject.SetActive(true);
            return t;
        }

        // ---------- пулы элементов ----------

        private TextBlock TakeText()
        {
            if (_usedText >= _textPool.Count)
            {
                var t = Rt(_blocks, "TextBlock").gameObject.AddComponent<Text>();
                t.font = GameFont.Regular;
                t.fontSize = 12;
                t.color = UIManager.DS.TextPrimary;
                t.alignment = TextAnchor.UpperLeft;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.lineSpacing = 1.25f;
                t.supportRichText = true;
                t.raycastTarget = false;
                var sh = t.gameObject.AddComponent<Outline>();
                sh.effectColor = UIManager.DS.TextShadow;
                sh.effectDistance = new Vector2(1f, -1f);
                _textPool.Add(new TextBlock { Text = t });
            }
            var tb = _textPool[_usedText++];
            tb.Text.gameObject.SetActive(true);
            return tb;
        }

        private Text TakeSec()
        {
            if (_usedSec >= _secPool.Count)
            {
                var t = Txt(_blocks, GameFont.Bold, 10, Color.white, FontStyle.Bold);
                t.Track(2.2f);
                _secPool.Add(t);
            }
            var s = _secPool[_usedSec++];
            s.gameObject.SetActive(true);
            return s;
        }

        private (RectTransform rt, Text[] values, Text[] caps) TakeBig()
        {
            if (_usedBig >= _bigPool.Count)
            {
                var rt = Rt(_blocks, "Big");
                var v = new Text[3];
                var c = new Text[3];
                for (int i = 0; i < 3; i++)
                {
                    v[i] = Txt(rt, GameFont.Display, 22, UIManager.DS.TextPrimary, FontStyle.Normal);
                    v[i].supportRichText = true;
                    v[i].alignment = TextAnchor.UpperLeft;
                    c[i] = Txt(rt, GameFont.Regular, 11, UIManager.DS.TextMuted, FontStyle.Normal);
                    c[i].alignment = TextAnchor.UpperLeft;
                }
                _bigPool.Add((rt, v, c));
            }
            var b = _bigPool[_usedBig++];
            b.rt.gameObject.SetActive(true);
            return b;
        }

        private (RectTransform rt, Image icon, Text label, Text value) TakeKv()
        {
            if (_usedKv >= _kvPool.Count)
            {
                var rt = Rt(_blocks, "Kv");
                var ic = Img(rt, "Icon", null, Color.white);
                ic.preserveAspect = true;
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(15f, 15f));
                var l = Txt(rt, GameFont.Regular, 13, UIManager.DS.TextPrimary, FontStyle.Normal);
                l.supportRichText = true;
                l.rectTransform.Stretch();
                var v = Txt(rt, GameFont.Bold, 13, Color.white, FontStyle.Bold);
                v.supportRichText = true;
                v.alignment = TextAnchor.MiddleRight;
                v.rectTransform.Stretch();
                _kvPool.Add((rt, ic, l, v));
            }
            var r = _kvPool[_usedKv++];
            r.rt.gameObject.SetActive(true);
            return r;
        }

        private Image TakeTick()
        {
            if (_usedTick >= _tickPool.Count)
            {
                var im = Img(_blocks, "Ticks", HudArt.TickRow(), new Color(0.37f, 0.82f, 0.75f, 0.45f));
                im.type = Image.Type.Tiled;
                _tickPool.Add(im);
            }
            var t = _tickPool[_usedTick++];
            t.gameObject.SetActive(true);
            return t;
        }
    }
}
