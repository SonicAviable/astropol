using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Стеклянные элементы управления для меню и настроек:
    /// переключатель, ползунок, селектор со стрелками, вкладки, поле ввода, строка настройки.
    /// </summary>
    public static class LGControls
    {
        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Neon => UIManager.DS.NeonCyan;

        // ------------------------------------------------------------------ Компоновка

        /// <summary>Заголовок секции с тонкой линией.</summary>
        public static float Section(RectTransform parent, float top, string title, LGIcon? icon = null)
        {
            var row = LGBuild.Rect(parent, "Section_" + title);
            row.TopBand(top, 26);
            float x = 2f;
            if (icon.HasValue)
            {
                var ic = LGIcons.Create(row, icon.Value, 15, Neon);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(15, 15));
                x = 24f;
            }
            var t = LGBuild.Label(row, title, 12, Neon, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.offsetMin = new Vector2(x, 0);
            var line = LGBuild.Panel(row, "Line", new Color(Neon.r, Neon.g, Neon.b, 0.28f));
            line.rectTransform.anchorMin = new Vector2(0, 0);
            line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.offsetMin = new Vector2(0, 0);
            line.rectTransform.offsetMax = new Vector2(0, 1);
            LG.Line(line.gameObject, hairline: true);
            return top + 38f;
        }

        /// <summary>Строка настройки: подпись и пояснение слева, контрол справа. Возвращает хост контрола.</summary>
        public static RectTransform Row(RectTransform parent, ref float top, string label, string desc, float controlWidth = 300f, float height = 62f)
        {
            var bg = LGBuild.Panel(parent, "Row_" + label, new Color(0.05f, 0.10f, 0.135f, 0.55f), raycast: true);
            bg.rectTransform.TopBand(top, height);
            var fx = LG.Platter(bg.gameObject, 14f);
            fx.FillMultiplier = 0.7f;
            fx.GlowMultiplier = 0f;
            bg.gameObject.AddComponent<RowHover>();

            var text = LGBuild.Rect(bg.transform, "Text");
            text.Stretch(18, 8, controlWidth + 30, 8);
            bool hasDesc = !string.IsNullOrEmpty(desc);
            var l = LGBuild.Label(text, label, 14, Primary, hasDesc ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, bold: true);
            if (hasDesc)
            {
                l.rectTransform.offsetMax = new Vector2(0, -2);
                var d = LGBuild.Label(text, desc, 11, Muted, TextAnchor.LowerLeft, wrap: true);
                d.rectTransform.offsetMin = new Vector2(0, 1);
            }

            var host = LGBuild.Rect(bg.transform, "Control");
            host.anchorMin = new Vector2(1, 0.5f);
            host.anchorMax = new Vector2(1, 0.5f);
            host.pivot = new Vector2(1, 0.5f);
            host.anchoredPosition = new Vector2(-16, 0);
            host.sizeDelta = new Vector2(controlWidth, 36);

            top += height + 8f;
            return host;
        }

        /// <summary>Лёгкая подсветка строки настройки под курсором.</summary>
        private sealed class RowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            private LiquidGlassEffect _fx;
            private float _h, _v, _target;
            private void Awake() => _fx = GetComponent<LiquidGlassEffect>();
            public void OnPointerEnter(PointerEventData e) => _target = 1f;
            public void OnPointerExit(PointerEventData e) => _target = 0f;
            private void OnDisable() { _h = _v = _target = 0f; if (_fx != null) _fx.SetHover(0f); }
            private void Update()
            {
                if (_fx == null) return;
                if (Mathf.Abs(_h - _target) < 0.002f && Mathf.Abs(_v) < 0.002f) return;
                _h = LGEase.Spring(_h, _target, ref _v, 3f, 0.8f, Time.unscaledDeltaTime);
                _fx.SetHover(_h * 0.45f);
            }
        }

        // ------------------------------------------------------------------ Переключатель

        public static LGSwitch Switch(RectTransform host, bool value, Action<bool> onChange)
        {
            var go = new GameObject("Switch");
            go.transform.SetParent(host, false);
            var sw = go.AddComponent<LGSwitch>();
            sw.Build(value, onChange);
            return sw;
        }

        // ------------------------------------------------------------------ Ползунок

        public static LGSlider Slider(RectTransform host, float value, float min, float max,
                                      Func<float, string> format, Action<float> onChange)
        {
            var go = new GameObject("Slider");
            go.transform.SetParent(host, false);
            var s = go.AddComponent<LGSlider>();
            s.Build(value, min, max, format, onChange);
            return s;
        }

        // ------------------------------------------------------------------ Селектор

        public static LGSelector Selector(RectTransform host, IList<string> options, int index, Action<int> onChange, bool wrap = false)
        {
            var go = new GameObject("Selector");
            go.transform.SetParent(host, false);
            var s = go.AddComponent<LGSelector>();
            s.Build(options, index, onChange, wrap);
            return s;
        }

        // ------------------------------------------------------------------ Вкладки

        public static LGTabs Tabs(RectTransform host, IList<string> labels, IList<LGIcon> icons, int index, Action<int> onChange)
        {
            var go = new GameObject("Tabs");
            go.transform.SetParent(host, false);
            var t = go.AddComponent<LGTabs>();
            t.Build(labels, icons, index, onChange);
            return t;
        }

        // ------------------------------------------------------------------ Поле ввода

        public static InputField Input(RectTransform host, string value, string placeholder, int maxLength = 40)
        {
            var bg = LGBuild.Panel(host, "Input", new Color(0.02f, 0.05f, 0.07f, 0.95f), raycast: true);
            bg.rectTransform.Stretch();
            var fx = LG.Apply(bg.gameObject, LiquidGlassEffect.Role.Field, 14f);
            fx.SetRim(new Color(Neon.r, Neon.g, Neon.b, 0.35f));

            var area = LGBuild.Rect(bg.transform, "Text Area");
            area.Stretch(16, 4, 16, 4);
            area.gameObject.AddComponent<RectMask2D>();

            var ph = LGBuild.Label(area, placeholder, 14, new Color(Muted.r, Muted.g, Muted.b, 0.6f), TextAnchor.MiddleLeft);
            ph.fontStyle = FontStyle.Italic;
            var tx = LGBuild.Label(area, "", 14, Primary, TextAnchor.MiddleLeft);
            tx.supportRichText = false;

            var input = bg.gameObject.AddComponent<InputField>();
            input.textComponent = tx;
            input.placeholder = ph;
            input.characterLimit = maxLength;
            input.caretColor = Neon;
            input.customCaretColor = true;
            input.caretWidth = 2;
            input.selectionColor = new Color(Neon.r, Neon.g, Neon.b, 0.3f);
            input.transition = Selectable.Transition.None;
            input.text = value ?? "";
            return input;
        }

        // ------------------------------------------------------------------ Круглая иконка-кнопка

        public static Button IconButton(Transform parent, LGIcon icon, float size, Color tint, Color rim, Action onClick, float rotation = 0f)
        {
            var img = LGBuild.Panel(parent, "IconBtn_" + icon, tint, raycast: true);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            var b = img.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => { SFXManager.Play("ui_click", 0.45f, 1.2f); onClick?.Invoke(); });
            LG.Button(img.gameObject, rim, size * 0.5f);
            var ic = LGIcons.Create(img.transform, icon, size * 0.5f, Color.white);
            ic.rectTransform.localRotation = Quaternion.Euler(0, 0, rotation);
            return b;
        }
    }

    // ====================================================================== Переключатель

    public sealed class LGSwitch : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private const float W = 58f, H = 30f, Knob = 22f;

        private bool _value;
        private Action<bool> _onChange;
        private RectTransform _knob;
        private Image _track;
        private LiquidGlassEffect _trackFx, _knobFx;
        private Text _label;
        private float _t, _v;
        private bool _hover;

        public bool Value => _value;

        public void Build(bool value, Action<bool> onChange)
        {
            _value = value;
            _onChange = onChange;
            _t = value ? 1f : 0f;

            var rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(W + 70, H);
            rt.anchoredPosition = Vector2.zero;
            var hit = gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            LG.Ignore(gameObject, includeChildren: false);

            _track = LGBuild.Panel(transform, "Track", Color.black, raycast: false);
            _track.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(W, H));
            _trackFx = LG.Apply(_track.gameObject, LiquidGlassEffect.Role.Chip, H * 0.5f);

            var k = LGBuild.Panel(_track.transform, "Knob", new Color(0.85f, 0.95f, 1f, 1f));
            _knob = k.rectTransform;
            _knob.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Knob, Knob));
            _knobFx = LG.Platter(k.gameObject, Knob * 0.5f);
            _knobFx.FillMultiplier = 2.6f;
            _knobFx.GlowMultiplier = 0f;

            _label = LGBuild.Label(transform, "", 12, UIManager.DS.TextMuted, TextAnchor.MiddleRight, bold: true);
            _label.rectTransform.Stretch(0, 0, W + 12, 0);

            Apply(true);
        }

        public void SetValue(bool v, bool notify)
        {
            if (_value == v) return;
            _value = v;
            Apply(false);
            if (notify) _onChange?.Invoke(v);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            SFXManager.Play("ui_click", 0.5f, _value ? 0.95f : 1.15f);
            SetValue(!_value, true);
        }

        public void OnPointerEnter(PointerEventData e) { _hover = true; Apply(false); }
        public void OnPointerExit(PointerEventData e) { _hover = false; Apply(false); }

        private void Apply(bool instant)
        {
            var on = UIManager.DS.NeonCyan;
            _track.color = _value ? new Color(on.r * 0.35f, on.g * 0.35f, on.b * 0.35f, 1f) : new Color(0.04f, 0.07f, 0.09f, 1f);
            _trackFx.SetRim(_value ? new Color(on.r, on.g, on.b, _hover ? 0.95f : 0.75f)
                                   : new Color(1f, 1f, 1f, _hover ? 0.32f : 0.18f));
            _trackFx.SetHover(_hover ? 0.5f : 0f);
            _label.text = _value ? "ВКЛ" : "ВЫКЛ";
            _label.color = _value ? on : UIManager.DS.TextMuted;
            if (instant) { _t = _value ? 1f : 0f; _v = 0f; PlaceKnob(); }
        }

        private void PlaceKnob()
        {
            float pad = (H - Knob) * 0.5f + Knob * 0.5f;
            _knob.anchoredPosition = new Vector2(Mathf.Lerp(pad, W - pad, _t), 0f);
            float stretch = 1f + Mathf.Clamp(Mathf.Abs(_v) * 0.02f, 0f, 0.25f);
            _knob.localScale = new Vector3(stretch, 1f / Mathf.Sqrt(stretch), 1f);
        }

        private void Update()
        {
            float target = _value ? 1f : 0f;
            if (Mathf.Abs(_t - target) < 0.001f && Mathf.Abs(_v) < 0.001f) return;
            _t = LGEase.Spring(_t, target, ref _v, 4.2f, 0.62f, Time.unscaledDeltaTime);
            PlaceKnob();
        }
    }

    // ====================================================================== Ползунок

    public sealed class LGSlider : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler,
                                    IPointerEnterHandler, IPointerExitHandler
    {
        private float _min, _max, _value;
        private Func<float, string> _format;
        private Action<float> _onChange;
        private RectTransform _area, _fill, _knob;
        private LiquidGlassEffect _knobFx;
        private Text _valueText;
        private float _shown, _shownV;
        private float _hover, _hoverV;
        private bool _over, _drag;

        public void Build(float value, float min, float max, Func<float, string> format, Action<float> onChange)
        {
            _min = min; _max = max; _format = format; _onChange = onChange;
            _value = Mathf.Clamp(value, min, max);
            _shown = Normalized;

            var rt = gameObject.AddComponent<RectTransform>();
            rt.Stretch();
            var hit = gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            LG.Ignore(gameObject, includeChildren: false);

            _valueText = LGBuild.Label(transform, "", 13, UIManager.DS.TextPrimary, TextAnchor.MiddleRight, bold: true);
            _valueText.rectTransform.anchorMin = new Vector2(1, 0);
            _valueText.rectTransform.anchorMax = new Vector2(1, 1);
            _valueText.rectTransform.pivot = new Vector2(1, 0.5f);
            _valueText.rectTransform.sizeDelta = new Vector2(56, 0);
            _valueText.rectTransform.anchoredPosition = Vector2.zero;

            _area = LGBuild.Rect(transform, "Area");
            _area.Stretch(10, 0, 74, 0);

            var track = LGBuild.Panel(_area, "Track", new Color(0f, 0.02f, 0.04f, 0.9f));
            track.rectTransform.anchorMin = new Vector2(0, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0, 6);
            LG.Platter(track.gameObject, 3f).FillMultiplier = 2.2f;

            var fill = LGBuild.Panel(track.transform, "Fill", UIManager.DS.NeonCyan);
            _fill = fill.rectTransform;
            _fill.anchorMin = Vector2.zero;
            _fill.anchorMax = new Vector2(_shown, 1);
            _fill.offsetMin = new Vector2(1, 1);
            _fill.offsetMax = new Vector2(-1, -1);
            LG.Fill(fill.gameObject);

            var knob = LGBuild.Panel(_area, "Knob", new Color(0.85f, 0.97f, 1f, 1f));
            _knob = knob.rectTransform;
            _knob.anchorMin = _knob.anchorMax = new Vector2(_shown, 0.5f);
            _knob.pivot = new Vector2(0.5f, 0.5f);
            _knob.sizeDelta = new Vector2(18, 18);
            _knob.anchoredPosition = Vector2.zero;
            _knobFx = LG.Platter(knob.gameObject, 9f);
            _knobFx.FillMultiplier = 2.6f;
            _knobFx.SetRim(new Color(0.45f, 0.95f, 0.9f, 0.8f));

            Refresh();
        }

        private float Normalized => Mathf.InverseLerp(_min, _max, _value);

        public void OnPointerDown(PointerEventData e) { _drag = true; SetFromPointer(e); }
        public void OnDrag(PointerEventData e) => SetFromPointer(e);
        public void OnPointerUp(PointerEventData e) { _drag = false; SFXManager.Play("ui_click", 0.35f, 1.3f); }
        public void OnPointerEnter(PointerEventData e) => _over = true;
        public void OnPointerExit(PointerEventData e) => _over = false;

        private void SetFromPointer(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, e.position, e.pressEventCamera, out var local)) return;
            var r = _area.rect;
            float n = Mathf.Clamp01((local.x - r.xMin) / Mathf.Max(1f, r.width));
            float v = Mathf.Lerp(_min, _max, n);
            if (Mathf.Abs(v - _value) < (_max - _min) * 0.002f) return;
            _value = v;
            Refresh();
            _onChange?.Invoke(_value);
        }

        private void Refresh() => _valueText.text = _format != null ? _format(_value) : _value.ToString("0.##");

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float target = Normalized;
            _shown = _drag ? target : LGEase.Spring(_shown, target, ref _shownV, 6f, 0.9f, dt);
            _fill.anchorMax = new Vector2(Mathf.Clamp01(_shown), 1);
            _knob.anchorMin = _knob.anchorMax = new Vector2(Mathf.Clamp01(_shown), 0.5f);

            _hover = LGEase.Spring(_hover, _over || _drag ? 1f : 0f, ref _hoverV, 4f, 0.7f, dt);
            float s = 1f + _hover * 0.22f;
            _knob.localScale = new Vector3(s, s, 1f);
            _knobFx.SetHover(_hover);
        }
    }

    // ====================================================================== Селектор ◀ значение ▶

    public sealed class LGSelector : MonoBehaviour
    {
        private IList<string> _options;
        private int _index;
        private Action<int> _onChange;
        private bool _wrap;
        private Text _text;
        private RectTransform _textRt;
        private Button _prev, _next;
        private readonly List<Image> _pips = new List<Image>();
        private float _slide, _slideV;

        public int Index => _index;

        public void Build(IList<string> options, int index, Action<int> onChange, bool wrap)
        {
            _options = options;
            _onChange = onChange;
            _wrap = wrap;
            _index = Mathf.Clamp(index, 0, Mathf.Max(0, options.Count - 1));

            var rt = gameObject.AddComponent<RectTransform>();
            rt.Stretch();
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.05f, 0.07f, 0.9f);
            bg.raycastTarget = true;
            var fx = LG.Apply(gameObject, LiquidGlassEffect.Role.Field, 18f);
            fx.SetRim(new Color(1f, 1f, 1f, 0.16f));

            _prev = LGControls.IconButton(transform, LGIcon.Back, 28, UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.25f), () => Step(-1));
            ((RectTransform)_prev.transform).At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(28, 28));
            _next = LGControls.IconButton(transform, LGIcon.Back, 28, UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.25f), () => Step(1), 180f);
            ((RectTransform)_next.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-4, 0), new Vector2(28, 28));

            var clip = LGBuild.Rect(transform, "Clip");
            clip.Stretch(38, 0, 38, 0);
            clip.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(14, 0);
            _text = LGBuild.Label(clip, "", 13, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, bold: true);
            _textRt = _text.rectTransform;
            _textRt.offsetMax = new Vector2(0, 3);

            if (options.Count > 1 && options.Count <= 8)
            {
                var pipRow = LGBuild.Rect(transform, "Pips");
                pipRow.anchorMin = new Vector2(0.5f, 0);
                pipRow.anchorMax = new Vector2(0.5f, 0);
                pipRow.pivot = new Vector2(0.5f, 0);
                pipRow.anchoredPosition = new Vector2(0, 5);
                float w = options.Count * 10f;
                pipRow.sizeDelta = new Vector2(w, 3);
                for (int i = 0; i < options.Count; i++)
                {
                    var p = LGBuild.Panel(pipRow, "Pip", Color.white);
                    p.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * 10f + 1f, 0), new Vector2(8, 2));
                    _pips.Add(p);
                }
                LG.Ignore(pipRow.gameObject);
            }
            Refresh();
        }

        public void SetIndex(int i, bool notify)
        {
            i = Mathf.Clamp(i, 0, _options.Count - 1);
            if (i == _index) return;
            int dir = i > _index ? 1 : -1;
            _index = i;
            _slide = dir;          // текст «въезжает» со стороны нажатой стрелки
            Refresh();
            if (notify) _onChange?.Invoke(_index);
        }

        private void Step(int d)
        {
            int n = _options.Count;
            if (n == 0) return;
            int i = _index + d;
            if (_wrap) i = (i + n) % n;
            else i = Mathf.Clamp(i, 0, n - 1);
            SetIndex(i, true);
        }

        private void Refresh()
        {
            _text.text = _options.Count > 0 ? _options[_index] : "—";
            _prev.interactable = _wrap || _index > 0;
            _next.interactable = _wrap || _index < _options.Count - 1;
            for (int i = 0; i < _pips.Count; i++)
                _pips[i].color = i == _index ? UIManager.DS.NeonCyan : new Color(1f, 1f, 1f, 0.18f);
        }

        private void Update()
        {
            if (Mathf.Abs(_slide) < 0.001f && Mathf.Abs(_slideV) < 0.001f) return;
            _slide = LGEase.Spring(_slide, 0f, ref _slideV, 3.6f, 0.85f, Time.unscaledDeltaTime);
            _textRt.anchoredPosition = new Vector2(_slide * 40f, _textRt.anchoredPosition.y);
            var c = _text.color;
            c.a = 1f - Mathf.Clamp01(Mathf.Abs(_slide)) * 0.9f;
            _text.color = c;
        }
    }

    // ====================================================================== Вкладки

    public sealed class LGTabs : MonoBehaviour
    {
        private readonly List<RectTransform> _tabs = new List<RectTransform>();
        private readonly List<Text> _labels = new List<Text>();
        private readonly List<Image> _icons = new List<Image>();
        private RectTransform _thumb;
        private Action<int> _onChange;
        private int _index;
        private float _x, _xv, _w, _wv;

        public int Index => _index;

        public void Build(IList<string> labels, IList<LGIcon> icons, int index, Action<int> onChange)
        {
            _onChange = onChange;
            _index = index;

            var rt = gameObject.AddComponent<RectTransform>();
            rt.Stretch();
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.05f, 0.07f, 0.85f);
            var fx = LG.Apply(gameObject, LiquidGlassEffect.Role.Field, 20f);
            fx.SetRim(new Color(1f, 1f, 1f, 0.14f));

            var thumb = LGBuild.Panel(transform, "Thumb", new Color(0.10f, 0.40f, 0.46f, 1f));
            _thumb = thumb.rectTransform;
            _thumb.anchorMin = new Vector2(0, 0);
            _thumb.anchorMax = new Vector2(0, 1);
            _thumb.pivot = new Vector2(0, 0.5f);
            _thumb.offsetMin = new Vector2(0, 4);
            _thumb.offsetMax = new Vector2(0, -4);
            var tfx = LG.Chip(thumb.gameObject, new Color(0.45f, 0.95f, 0.9f, 0.85f), 16f);
            tfx.GlowMultiplier = 0.5f;

            int n = labels.Count;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var tab = LGBuild.Rect(transform, "Tab_" + labels[i]);
                tab.anchorMin = new Vector2(i / (float)n, 0);
                tab.anchorMax = new Vector2((i + 1) / (float)n, 1);
                tab.offsetMin = new Vector2(4, 4);
                tab.offsetMax = new Vector2(-4, -4);
                var hit = tab.gameObject.AddComponent<Image>();
                hit.color = new Color(0, 0, 0, 0);
                var b = tab.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => Select(idx, true));
                LG.Ignore(tab.gameObject, includeChildren: false);

                Text label;
                if (icons != null && i < icons.Count)
                {
                    label = LGIcons.IconLabel(tab, icons[i], labels[i], 12, Color.white, Color.white, 15f);
                    _icons.Add(label.transform.parent.GetChild(0).GetComponent<Image>());
                }
                else
                {
                    label = LGBuild.Label(tab, labels[i], 12, Color.white, TextAnchor.MiddleCenter, bold: true);
                    _icons.Add(null);
                }
                _labels.Add(label);
                _tabs.Add(tab);
            }
            Canvas.ForceUpdateCanvases();
            Paint();
        }

        public void Select(int i, bool notify)
        {
            if (i < 0 || i >= _tabs.Count) return;
            if (i != _index) SFXManager.Play("ui_click", 0.5f, 1.1f);
            _index = i;
            Paint();
            if (notify) _onChange?.Invoke(i);
        }

        private void Paint()
        {
            for (int i = 0; i < _labels.Count; i++)
            {
                var c = i == _index ? UIManager.DS.TextPrimary : UIManager.DS.TextMuted;
                _labels[i].color = c;
                if (_icons[i] != null) _icons[i].color = i == _index ? UIManager.DS.NeonCyan : UIManager.DS.TextMuted;
            }
        }

        private void Update()
        {
            if (_tabs.Count == 0) return;
            var parent = (RectTransform)transform;
            float pw = parent.rect.width;
            if (pw <= 1f) return;
            float slot = pw / _tabs.Count;
            float tx = slot * _index + 4f;
            float tw = slot - 8f;
            if (_w <= 0f) { _x = tx; _w = tw; }
            float dt = Time.unscaledDeltaTime;
            _x = LGEase.Spring(_x, tx, ref _xv, 3.4f, 0.72f, dt);
            _w = LGEase.Spring(_w, tw, ref _wv, 3.4f, 0.72f, dt);
            _thumb.anchoredPosition = new Vector2(_x, 0);
            _thumb.sizeDelta = new Vector2(_w, _thumb.sizeDelta.y);
        }
    }
}
