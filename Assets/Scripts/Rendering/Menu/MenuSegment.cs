using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Сегментный переключатель: все варианты видны сразу, выбранный подсвечен «плашкой»,
    /// которая на пружине переезжает к новому варианту. Под названием варианта может быть подпись.
    /// </summary>
    public sealed class MenuSegment : MonoBehaviour
    {
        private static readonly Color Neon = new Color(0.44f, 0.90f, 0.85f);

        private readonly List<Option> _opts = new List<Option>();
        private RectTransform _rt, _plate;
        private Action<int> _onChange;
        private int _value;
        private float _x, _xv;
        private bool _placed;

        private sealed class Option : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public MenuSegment Owner;
            public int Index;
            public Text Title, Sub;
            public bool Over;
            public float H, Hv;

            public void OnPointerEnter(PointerEventData e)
            {
                Over = true;
                if (SFXManager.Has("ui_hover")) SFXManager.Play("ui_hover", 0.25f, 1.25f);
            }

            public void OnPointerExit(PointerEventData e) => Over = false;

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left) Owner.Set(Index, true);
            }
        }

        public int Value => _value;

        public static MenuSegment Create(RectTransform parent, IList<string> labels, IList<string> subs, int value, Action<int> onChange)
        {
            var bg = LGBuild.Panel(parent, "Segment", new Color(1f, 1f, 1f, 0.035f));
            var s = bg.gameObject.AddComponent<MenuSegment>();
            s._rt = bg.rectTransform;
            s._onChange = onChange;
            s._value = Mathf.Clamp(value, 0, labels.Count - 1);
            var fx = LG.Platter(bg.gameObject, 14f);
            fx.SetRim(new Color(1f, 1f, 1f, 0.08f));

            var plate = LGBuild.Panel(bg.transform, "Plate", new Color(Neon.r * 0.35f, Neon.g * 0.35f, Neon.b * 0.35f, 0.85f));
            s._plate = plate.rectTransform;
            s._plate.anchorMin = new Vector2(0, 0);
            s._plate.anchorMax = new Vector2(0, 1);
            s._plate.pivot = new Vector2(0, 0.5f);
            LG.Chip(plate.gameObject, new Color(Neon.r, Neon.g, Neon.b, 0.75f), 10f);

            int n = labels.Count;
            for (int i = 0; i < n; i++)
            {
                var o = LGBuild.Rect(bg.transform, "Opt" + i);
                o.anchorMin = new Vector2(i / (float)n, 0);
                o.anchorMax = new Vector2((i + 1) / (float)n, 1);
                o.offsetMin = o.offsetMax = Vector2.zero;
                var hit = o.gameObject.AddComponent<Image>();
                hit.color = new Color(0, 0, 0, 0);
                LG.Ignore(o.gameObject, includeChildren: false);
                var opt = o.gameObject.AddComponent<Option>();
                opt.Owner = s;
                opt.Index = i;
                bool hasSub = subs != null && i < subs.Count && !string.IsNullOrEmpty(subs[i]);
                opt.Title = LGBuild.Label(o, labels[i], 15, Color.white, hasSub ? TextAnchor.LowerCenter : TextAnchor.MiddleCenter, bold: true);
                opt.Title.rectTransform.Stretch(4, hasSub ? 0 : 0, 4, hasSub ? 0 : 0);
                if (hasSub)
                {
                    opt.Title.rectTransform.anchorMin = new Vector2(0, 0.5f);
                    opt.Title.rectTransform.offsetMin = new Vector2(4, -2);
                    opt.Sub = LGBuild.Label(o, subs[i], 11, Color.white, TextAnchor.UpperCenter);
                    opt.Sub.rectTransform.Stretch(4, 0, 4, 0);
                    opt.Sub.rectTransform.anchorMax = new Vector2(1, 0.5f);
                    opt.Sub.rectTransform.offsetMax = new Vector2(-4, -2);
                }
                opt.Title.resizeTextForBestFit = true;
                opt.Title.resizeTextMinSize = 10;
                opt.Title.resizeTextMaxSize = 15;
                s._opts.Add(opt);
            }
            return s;
        }

        public void Set(int i, bool notify)
        {
            i = Mathf.Clamp(i, 0, _opts.Count - 1);
            if (i == _value) return;
            _value = i;
            if (notify)
            {
                SFXManager.Play("ui_click", 0.55f, 1.15f);
                _onChange?.Invoke(i);
            }
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            var r = _rt.rect;
            if (r.width < 2f || _opts.Count == 0) return;
            const float pad = 5f;
            float w = (r.width - pad * 2f) / _opts.Count;
            float target = pad + w * _value;
            if (!_placed) { _x = target; _placed = true; }
            _x = LGEase.Spring(_x, target, ref _xv, 5f, 0.8f, dt);
            _plate.anchoredPosition = new Vector2(_x, 0f);
            _plate.sizeDelta = new Vector2(w, -pad * 2f);

            for (int i = 0; i < _opts.Count; i++)
            {
                var o = _opts[i];
                o.H = LGEase.Spring(o.H, o.Over ? 1f : 0f, ref o.Hv, 4f, 0.7f, dt);
                bool on = i == _value;
                var c = on ? Color.white : Color.Lerp(new Color(0.62f, 0.72f, 0.76f), new Color(0.90f, 0.96f, 0.98f), Mathf.Clamp01(o.H));
                o.Title.color = c;
                if (o.Sub != null) o.Sub.color = on ? new Color(0.66f, 0.92f, 0.88f) : new Color(0.45f, 0.54f, 0.58f);
            }
        }
    }

    /// <summary>Въезд элемента при каждом показе экрана: сдвиг + проявление через Delay секунд.</summary>
    public sealed class MenuSlideIn : MonoBehaviour
    {
        public float Delay;
        public Vector2 From = new Vector2(-40f, 0f);
        private CanvasGroup _cg;
        private RectTransform _rt;
        private Vector2 _rest;
        private bool _known;
        private float _t;

        public static MenuSlideIn Add(GameObject go, float delay, Vector2 from)
        {
            var s = go.AddComponent<MenuSlideIn>();
            s.Delay = delay;
            s.From = from;
            return s;
        }

        private void Awake()
        {
            _rt = (RectTransform)transform;
            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;
        }

        public void Restart()
        {
            _t = 0f;
            enabled = true;
            if (_cg != null) _cg.alpha = 0f;
        }

        private void Update()
        {
            if (!_known) { _rest = _rt.anchoredPosition; _known = true; }
            _t += Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            float k = LGEase.OutCubic(Mathf.Clamp01((_t - Delay) / 0.5f));
            _cg.alpha = k;
            _rt.anchoredPosition = _rest + From * (1f - k);
            if (k >= 1f) enabled = false;
        }
    }
}
