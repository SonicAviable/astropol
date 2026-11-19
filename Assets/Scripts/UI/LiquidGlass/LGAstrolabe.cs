using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Стиль кнопок «Астролябия»: латунная рамка, тёмно-синее эмалевое тело, гравированная линия внутри,
    /// шкала делений внизу (как на лимбе астролябии) и камни по бокам — цвет камня говорит, что сделает кнопка:
    /// жемчуг — обычное, изумруд — успех, янтарь — ресурсы, рубин — опасное, сапфир — главное действие.
    /// Главная кнопка — полированная латунь с тёмной подписью. Недоступная — латунь потускнела до патины.
    /// При наведении по рамке пробегает блик и загораются камни, при нажатии кнопка темнеет и подпись проседает.
    /// Все спрайты рисуются в коде (без ассетов). Вешается через <see cref="Attach"/>: из LGGameButton,
    /// LGPrimarySkin, MenuPrimaryButton и круглых кнопок-значков (медальон).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGAstrolabe : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public static readonly Color Pearl = new Color(0.925f, 0.890f, 0.800f);
        public static readonly Color Emerald = new Color(0.373f, 0.827f, 0.714f);
        public static readonly Color Amber = new Color(1f, 0.714f, 0.282f);
        public static readonly Color Ruby = new Color(1f, 0.353f, 0.282f);
        public static readonly Color Sapphire = new Color(0.478f, 0.635f, 1f);

        private static readonly Color Brass = new Color(0.906f, 0.757f, 0.439f);
        private static readonly Color LabelNormal = new Color(0.910f, 0.875f, 0.784f);
        private static readonly Color LabelLit = new Color(1f, 0.961f, 0.863f);
        private static readonly Color LabelDanger = new Color(1f, 0.882f, 0.855f);
        private static readonly Color LabelSelected = new Color(0.957f, 0.851f, 0.584f);
        private static readonly Color LabelPrimary = new Color(0.165f, 0.110f, 0.024f);
        private static readonly Color LabelOff = new Color(0.373f, 0.404f, 0.447f);
        private static readonly Color PatinaTint = new Color(0.55f, 0.62f, 0.58f);
        private static readonly Color StoneOff = new Color(0.227f, 0.255f, 0.298f);

        /// <summary>Главная кнопка экрана: полированная латунь, камни-сапфиры.</summary>
        public bool Primary;
        /// <summary>Круглый медальон для кнопок-значков (без камней и шкалы).</summary>
        public bool Round;
        /// <summary>Выбранная вкладка: подпись золотая, шкала горит.</summary>
        public bool Selected;
        /// <summary>Медальон не перекрашивает значок — у него свой цвет (например, цвет режима карты).</summary>
        public bool KeepIconColor;
        /// <summary>Плитка (ячейка сетки): рамка без камней и шкалы — их много рядом, иначе рябит.</summary>
        public bool Plain;
        /// <summary>Цвет камней — смысл действия.</summary>
        public Color Gem = Pearl;
        /// <summary>Код перекрашивает кнопку (Image.color) — новый цвет читается как смысл действия, рамка не красится.</summary>
        public bool WatchTint;
        /// <summary>Доступность, если кнопка — не Selectable (например, MenuPrimaryButton).</summary>
        public Func<bool> CanUse;

        private Image _frame, _glow, _hair, _sweep, _scale;
        private RectTransform _clip, _sweepRt;
        private readonly RectTransform[] _gems = new RectTransform[2];
        private readonly RectTransform[] _halosRt = new RectTransform[2];
        private readonly Image[] _stones = new Image[2], _gemFrames = new Image[2], _halos = new Image[2];
        private Text _label;
        private Shadow _labelShadow;
        private Graphic _icon;
        private Selectable _sel;
        private Vector2 _labelMin, _labelMax;
        private bool _built, _over, _down;
        private float _h, _hv, _p, _pv, _sw = -1f;
        private int _state = -1;
        private string _seen;
        private Color _setTint = Color.white;

        /// <summary>Оформить кнопку. host — объект с обработкой нажатий, frame — её тело (может быть дочерним).</summary>
        public static LGAstrolabe Attach(GameObject host, Image frame, Text label, bool primary = false, bool round = false, bool plain = false)
        {
            if (host == null || frame == null) return null;
            var a = host.TryGetComponent<LGAstrolabe>(out var e) ? e : host.AddComponent<LGAstrolabe>();
            a.Primary = primary;
            a.Round = round;
            a.Plain = plain;
            if (primary) a.Gem = Sapphire;
            a._frame = frame;
            a._label = label;
            a._sel = host.GetComponent<Selectable>();
            if (a._sel != null) a._sel.transition = Selectable.Transition.None;
            a.Build();
            return a;
        }

        /// <summary>Цвет камня по цвету действия: коралл → рубин, зелень → изумруд, золото → янтарь, остальное — жемчуг.</summary>
        public static Color GemOf(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.25f || v < 0.12f) return Pearl;
            if (h < 0.05f || h > 0.93f) return Ruby;
            if (h > 0.25f && h < 0.52f) return Emerald;
            if (h >= 0.05f && h <= 0.17f) return Amber;
            return Pearl;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _over = true;
            if (CanUseNow() && _sweep != null) { _sw = 0f; _sweep.enabled = true; }
        }

        public void OnPointerExit(PointerEventData e) { _over = false; _down = false; }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) _down = true; }
        public void OnPointerUp(PointerEventData e) => _down = false;

        private void OnDisable()
        {
            _over = _down = false;
            _h = _hv = _p = _pv = 0f;
            _sw = -1f;
            if (_sweep != null) _sweep.enabled = false;
        }

        private void OnRectTransformDimensionsChange() => Layout();

        private bool CanUseNow() => CanUse != null ? CanUse() : (_sel == null || _sel.IsInteractable());

        // ------------------------------------------------------------------ Сборка

        private void Build()
        {
            if (_built) return;
            _built = true;

            var fx = _frame.GetComponent<LiquidGlassEffect>();
            if (fx != null) { fx.enabled = false; Destroy(fx); }
            _frame.material = null;
            var sweepFx = _frame.GetComponent<InspSweep>();
            if (sweepFx != null) Destroy(sweepFx);
            LG.Ignore(_frame.gameObject);

            _frame.type = Round ? Image.Type.Simple : Image.Type.Sliced;
            _frame.preserveAspect = Round;   // медальон остаётся кругом и в неквадратной кнопке
            _frame.pixelsPerUnitMultiplier = 1f;
            _frame.color = _setTint = Color.white;
            var drop = _frame.GetComponent<Shadow>();
            if (drop == null) drop = _frame.gameObject.AddComponent<Shadow>();
            drop.effectColor = new Color(0f, 0f, 0f, 0.55f);
            drop.effectDistance = new Vector2(0f, -2f);

            // Свои дочерние элементы (подпись, значок) — поверх слоёв оформления
            var own = new List<Transform>();
            foreach (Transform c in _frame.transform) own.Add(c);

            _glow = Layer(_frame.transform, "AB_Glow", Round ? MenuArt.SoftDisc : Glow);
            Stretch(_glow.rectTransform, Round ? 4f : 2f);
            _glow.preserveAspect = Round;

            if (!Round)
            {
                _clip = NewRect(_frame.transform, "AB_Clip");
                Stretch(_clip, 2f);
                _clip.gameObject.AddComponent<RectMask2D>();
                _sweep = Layer(_clip, "AB_Sweep", Sweep);
                _sweepRt = _sweep.rectTransform;
                _sweepRt.anchorMin = new Vector2(0f, 0f);
                _sweepRt.anchorMax = new Vector2(0f, 1f);
                _sweepRt.pivot = new Vector2(0.5f, 0.5f);
                _sweep.enabled = false;
            }

            _hair = Layer(_frame.transform, "AB_Hair", Round ? Ring : Hair);
            if (!Round) _hair.type = Image.Type.Sliced;
            _hair.preserveAspect = Round;
            _hair.pixelsPerUnitMultiplier = 1f;

            if (!Round && !Plain)
            {
                _scale = Layer(_frame.transform, "AB_Scale", Ticks);
                var srt = _scale.rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0f);
                srt.pivot = new Vector2(0.5f, 0f);

                for (int i = 0; i < 2; i++)
                {
                    var g = NewRect(_frame.transform, i == 0 ? "AB_GemL" : "AB_GemR");
                    g.anchorMin = g.anchorMax = new Vector2(i, 0.5f);
                    g.pivot = new Vector2(0.5f, 0.5f);
                    g.anchoredPosition = Vector2.zero;
                    _gems[i] = g;
                    _halos[i] = Layer(g, "Halo", MenuArt.SoftDisc);
                    _halosRt[i] = _halos[i].rectTransform;
                    _halosRt[i].anchorMin = _halosRt[i].anchorMax = new Vector2(0.5f, 0.5f);
                    _gemFrames[i] = Layer(g, "Mount", GemMount);
                    Stretch(_gemFrames[i].rectTransform, 0f);
                    _stones[i] = Layer(g, "Stone", GemStone);
                    Stretch(_stones[i].rectTransform, 0f);
                    var spec = Layer(g, "Spec", GemSpec);
                    Stretch(spec.rectTransform, 0f);
                }
            }

            foreach (var t in own) if (t != null) t.SetAsLastSibling();

            if (_label != null) StyleLabel();
            else if (Round)
                foreach (var t in own)
                    if (t != null && t.TryGetComponent<Graphic>(out var gr)) { _icon = gr; break; }

            Layout();
            Refresh(true);
        }

        private void StyleLabel()
        {
            var old = _label.GetComponent<LGButtonLabel>();
            if (old != null) Destroy(old);
            _label.font = GameFont.Display;
            _label.fontStyle = FontStyle.Normal;
            _label.Track(1.8f);
            _labelShadow = _label.GetComponent<Shadow>();
            if (_labelShadow == null) _labelShadow = _label.gameObject.AddComponent<Shadow>();
            _labelShadow.effectDistance = new Vector2(0f, -1f);
            _labelMin = _label.rectTransform.offsetMin;
            _labelMax = _label.rectTransform.offsetMax;
        }

        private void Layout()
        {
            if (!_built || _frame == null) return;
            var r = _frame.rectTransform.rect;
            float h = r.height;
            if (h < 1f) return;

            // Медальон — строго квадратный: иначе круг (preserveAspect) прижимается к точке привязки, а кольцо и значок — по центру
            if (Round)
            {
                var frt = _frame.rectTransform;
                var sd = frt.sizeDelta;
                if (frt.anchorMin == frt.anchorMax && Mathf.Abs(sd.x - sd.y) > 0.5f)
                {
                    float m = Mathf.Min(sd.x, sd.y);
                    // центр медальона остаётся там же, где был центр кнопки
                    var shift = new Vector2((sd.x - m) * (0.5f - frt.pivot.x), (sd.y - m) * (0.5f - frt.pivot.y));
                    frt.sizeDelta = new Vector2(m, m);
                    frt.anchoredPosition += shift;
                    return;   // изменение размера снова вызовет Layout
                }
            }

            float inset = Round ? 3f : h >= 50f ? 4f : h < 34f ? 2.5f : 3f;
            Stretch(_hair.rectTransform, inset);
            if (Round) return;

            float gs = Mathf.Clamp(Mathf.Round(h * 0.26f), 8f, 14f);
            for (int i = 0; i < 2; i++)
            {
                if (_gems[i] == null) continue;
                _gems[i].sizeDelta = new Vector2(gs, gs);
                _halosRt[i].sizeDelta = new Vector2(gs * 3.2f, gs * 3.2f);
            }

            if (_scale != null)
            {
                var srt = _scale.rectTransform;
                srt.sizeDelta = new Vector2(Mathf.Min(96f, r.width * 0.55f), h < 34f ? 2f : 4f);
                srt.anchoredPosition = new Vector2(0f, h >= 50f ? 6f : h < 34f ? 3f : 4f);
            }

            _sweepRt.sizeDelta = new Vector2(h * 1.8f, 0f);
            if (_sw < 0f) _sweepRt.anchoredPosition = new Vector2(-h * 1.8f, 0f);
        }

        // ------------------------------------------------------------------ Отклик

        private void LateUpdate()
        {
            if (!_built || _frame == null) return;
            Refresh(false);
        }

        private void Refresh(bool force)
        {
            float dt = Time.unscaledDeltaTime;
            if (WatchTint && _frame.color != _setTint) Gem = GemOf(_frame.color);

            bool can = CanUseNow();
            _h = LGEase.Spring(_h, can && _over ? 1f : 0f, ref _hv, 4f, 0.8f, dt);
            _p = LGEase.Spring(_p, can && _down ? 1f : 0f, ref _pv, 7f, 0.85f, dt);
            float h = Mathf.Clamp01(_h), p = Mathf.Clamp01(_p);

            int state = !can ? 2 : Primary ? 1 : 0;
            if (state != _state || force)
            {
                _state = state;
                _frame.sprite = Frame(state, Round);
                if (_labelShadow != null)
                    _labelShadow.effectColor = state == 1 ? new Color(1f, 0.96f, 0.84f, 0.6f) : new Color(0f, 0f, 0f, 0.7f);
            }

            var tint = Color.Lerp(Color.white, new Color(0.78f, 0.78f, 0.82f), p);
            if (Primary && can) tint = Color.Lerp(tint, Color.white, h);
            _frame.color = _setTint = tint;

            float sel = Selected ? 1f : 0f;
            _glow.color = !can ? Color.clear
                : Primary ? new Color(1f, 0.98f, 0.9f, 0.10f + 0.28f * h)
                : new Color(Gem.r, Gem.g, Gem.b, 0.05f + 0.22f * h + 0.35f * sel);
            _hair.color = !can ? new Color(0.42f, 0.48f, 0.44f, 0.22f)
                : Primary ? new Color(0.27f, 0.19f, 0.05f, 0.45f)
                : new Color(Brass.r, Brass.g, Brass.b, Mathf.Lerp(0.26f, 0.55f, Mathf.Max(h, sel)));

            if (_scale != null)
            {
                var tc = !can ? new Color(0.30f, 0.33f, 0.38f) : Primary ? new Color(0.29f, 0.20f, 0.06f) : Brass;
                tc.a = Selected ? 1f : Mathf.Lerp(0.38f, 0.9f, h);
                _scale.color = tc;
            }

            for (int i = 0; i < 2; i++)
            {
                if (_stones[i] == null) continue;
                _stones[i].color = can ? Gem : StoneOff;
                _gemFrames[i].color = can ? Color.white : PatinaTint;
                _halos[i].color = new Color(Gem.r, Gem.g, Gem.b, can ? 0.6f * h : 0f);
            }

            var lc = !can ? LabelOff
                : Primary ? LabelPrimary
                : Selected ? LabelSelected
                : Color.Lerp(LabelNormal, Gem == Ruby ? LabelDanger : LabelLit, h);

            if (_label != null)
            {
                if (!ReferenceEquals(_label.text, _seen))
                {
                    string s = Upper(_label.text, Primary);
                    if (s != _label.text) _label.text = s;
                    _seen = _label.text;
                }
                _label.color = lc;
                var shift = new Vector2(0f, -1.5f * p);
                _label.rectTransform.offsetMin = _labelMin + shift;
                _label.rectTransform.offsetMax = _labelMax + shift;
            }
            if (_icon != null && !KeepIconColor) _icon.color = lc == LabelPrimary ? LabelNormal : lc;

            if (_sw >= 0f && _sweep != null)
            {
                _sw += dt / 0.9f;
                float w = _frame.rectTransform.rect.width, bw = _sweepRt.sizeDelta.x;
                float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(_sw), 3f);
                _sweepRt.anchoredPosition = new Vector2(Mathf.Lerp(-bw * 0.5f, w + bw * 0.5f, t), 0f);
                _sweep.color = new Color(1f, 0.93f, 0.77f, (Primary ? 0.45f : 0.17f) * Mathf.Clamp01((1f - _sw) * 4f));
                if (_sw >= 1f) { _sw = -1f; _sweep.enabled = false; }
            }
        }

        /// <summary>Подпись капслоком (вне rich-text тегов); у главной убирается служебный значок в начале («▶», «✕»).</summary>
        private static string Upper(string s, bool stripGlyph)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (stripGlyph)
            {
                string t = s.TrimStart();
                if (t.Length > 1 && !char.IsLetterOrDigit(t[0]) && t[0] != '(' && t[0] != '<') s = t.Substring(1).TrimStart();
            }
            var sb = new System.Text.StringBuilder(s.Length);
            bool tag = false;
            foreach (char c in s)
            {
                if (c == '<') tag = true;
                else if (c == '>') tag = false;
                sb.Append(tag || c == '>' ? c : char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ Слои

        private static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;   // слои оформления не участвуют в раскладке кнопки
            return rt;
        }

        private static Image Layer(Transform parent, string name, Sprite s)
        {
            var rt = NewRect(parent, name);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = s;
            im.raycastTarget = false;
            return im;
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        // ------------------------------------------------------------------ Спрайты (рисуются один раз)

        private struct Stop
        {
            public float T; public Color C;
            public Stop(float t, int hex) { T = t; C = new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1f); }
        }

        private static Color Grad(Stop[] s, float t)
        {
            t = Mathf.Clamp01(t);
            for (int i = 1; i < s.Length; i++)
                if (t <= s[i].T) return Color.Lerp(s[i - 1].C, s[i].C, Mathf.InverseLerp(s[i - 1].T, s[i].T, t));
            return s[s.Length - 1].C;
        }

        private static readonly Stop[] EdgeBrass = { new Stop(0f, 0x5b4119), new Stop(0.22f, 0xf0d690), new Stop(0.45f, 0x8f6c2f), new Stop(0.68f, 0xf6e1a4), new Stop(1f, 0x6b4d1e) };
        private static readonly Stop[] EdgeDark = { new Stop(0f, 0x3d2a0d), new Stop(0.4f, 0xa7823f), new Stop(0.62f, 0x3d2a0d), new Stop(1f, 0x8c6a2e) };
        private static readonly Stop[] EdgePatina = { new Stop(0f, 0x343e3a), new Stop(0.28f, 0x6c7a71), new Stop(0.52f, 0x3e4945), new Stop(0.76f, 0x616e66), new Stop(1f, 0x2f3734) };
        private static readonly Stop[] BodyEnamel = { new Stop(0f, 0x1f2742), new Stop(1f, 0x0f1428) };
        private static readonly Stop[] BodyBrass = { new Stop(0f, 0xf6e3aa), new Stop(0.42f, 0xddb969), new Stop(0.58f, 0xb88e40), new Stop(1f, 0xd2a95c) };
        private static readonly Stop[] BodyPatina = { new Stop(0f, 0x151a28), new Stop(1f, 0x0e121e) };
        private static readonly Stop[] Mount = { new Stop(0f, 0xf6e1a4), new Stop(0.55f, 0x8f6c2f), new Stop(1f, 0x5b4119) };

        private static readonly Sprite[] s_frames = new Sprite[6];
        private static Sprite s_hair, s_ring, s_glow, s_sweep, s_ticks, s_mount, s_stone, s_spec;

        private static Sprite Frame(int state, bool round)
        {
            int k = state + (round ? 3 : 0);
            if (s_frames[k] != null) return s_frames[k];
            var edge = state == 2 ? EdgePatina : state == 1 ? EdgeDark : EdgeBrass;
            var body = state == 2 ? BodyPatina : state == 1 ? BodyBrass : BodyEnamel;
            float sheen = state == 2 ? 0f : state == 1 ? 0.45f : 0.06f;
            return s_frames[k] = round ? RoundFrame(edge, body, sheen) : SquareFrame(edge, body, sheen);
        }

        private static Color Body(Stop[] body, float sheen, float u, float v)
        {
            var b = Grad(body, 1f - v);
            float dx = (u - 0.5f) * 1.2f, dy = (1.25f - v) * 1.6f;
            float g = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            return Color.Lerp(b, new Color(1f, 0.97f, 0.88f), sheen * g * g);
        }

        private static Sprite SquareFrame(Stop[] edge, Stop[] body, float sheen)
        {
            const int S = 64;
            return Make(S, S, (x, y) =>
            {
                int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(S - 1 - x, S - 1 - y));
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                Color c;
                if (d < 2)
                {
                    c = Grad(edge, (u + 1f - v) * 0.5f);
                    if (d == 0) c = Color.Lerp(c, Color.black, 0.3f);
                }
                else
                {
                    c = Body(body, sheen, u, v);
                    if (d == 2 && y == S - 3) c = Color.Lerp(c, Color.white, 0.08f);
                }
                c.a = 1f;
                return c;
            }, new Vector4(10, 10, 10, 10), "AB_Frame");
        }

        private static Sprite RoundFrame(Stop[] edge, Stop[] body, float sheen)
        {
            const int S = 64;
            const float R = S * 0.5f;
            return Make(S, S, (x, y) =>
            {
                float fx = x + 0.5f - R, fy = y + 0.5f - R;
                float dist = Mathf.Sqrt(fx * fx + fy * fy);
                float a = Mathf.Clamp01(R - dist);
                if (a <= 0f) return Color.clear;
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                Color c;
                if (dist > R - 3f)
                {
                    c = Grad(edge, (u + 1f - v) * 0.5f);
                    if (dist > R - 1f) c = Color.Lerp(c, Color.black, 0.3f);
                }
                else c = Body(body, sheen, u, v);
                c.a = a;
                return c;
            }, Vector4.zero, "AB_Round");
        }

        private static Sprite Hair => s_hair != null ? s_hair : (s_hair = Make(16, 16, (x, y) =>
        {
            int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(15 - x, 15 - y));
            return d == 0 ? Color.white : Color.clear;
        }, new Vector4(4, 4, 4, 4), "AB_Hair"));

        private static Sprite Ring => s_ring != null ? s_ring : (s_ring = Make(64, 64, (x, y) =>
        {
            float fx = x + 0.5f - 32f, fy = y + 0.5f - 32f;
            float d = Mathf.Sqrt(fx * fx + fy * fy);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(1.2f - Mathf.Abs(d - 30.5f)));
        }, Vector4.zero, "AB_Ring"));

        private static Sprite Glow => s_glow != null ? s_glow : (s_glow = Make(64, 32, (x, y) =>
        {
            float u = (x + 0.5f) / 64f, v = (y + 0.5f) / 32f;
            float dx = (u - 0.5f) * 1.6f, dy = 1.15f - v;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            return new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f));
        }, Vector4.zero, "AB_Glow"));

        private static Sprite Sweep => s_sweep != null ? s_sweep : (s_sweep = Make(64, 64, (x, y) =>
        {
            float u = (x + 0.5f) / 64f, v = (y + 0.5f) / 64f;
            float s = u + (v - 0.5f) * 0.27f;
            return new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(s - 0.5f) / 0.2f), 1.5f));
        }, Vector4.zero, "AB_Sweep"));

        private static Sprite Ticks => s_ticks != null ? s_ticks : (s_ticks = Make(96, 4, (x, y) =>
        {
            int dx = Mathf.Abs(x - 48);
            bool on = dx == 0 ? y <= 3 : (dx % 7 == 0 && y <= 1);
            float u = (x + 0.5f) / 96f;
            float fade = Mathf.SmoothStep(0f, 1f, u / 0.3f) * Mathf.SmoothStep(0f, 1f, (1f - u) / 0.3f);
            return new Color(1f, 1f, 1f, on ? fade : 0f);
        }, Vector4.zero, "AB_Ticks"));

        private static float Diamond(int x, int y) => Mathf.Abs(x + 0.5f - 16f) + Mathf.Abs(y + 0.5f - 16f);

        private static Sprite GemMount => s_mount != null ? s_mount : (s_mount = Make(32, 32, (x, y) =>
        {
            float m = Diamond(x, y);
            const float R = 15.5f;
            float a = Mathf.Clamp01(R - m + 0.5f);
            if (a <= 0f) return Color.clear;
            var c = R - m < 1.5f ? new Color(0.141f, 0.098f, 0.039f) : Grad(Mount, ((x + 0.5f) / 32f + 1f - (y + 0.5f) / 32f) * 0.5f);
            c.a = a;
            return c;
        }, Vector4.zero, "AB_GemMount"));

        private static Sprite GemStone => s_stone != null ? s_stone : (s_stone = Make(32, 32, (x, y) =>
        {
            float m = Diamond(x, y);
            float a = Mathf.Clamp01(10.5f - m + 0.5f);
            if (a <= 0f) return Color.clear;
            float t = ((x + 0.5f) / 32f + 1f - (y + 0.5f) / 32f) * 0.5f;
            float g = t < 0.45f ? 1f : Mathf.Lerp(1f, 0.5f, (t - 0.45f) / 0.55f);
            return new Color(g, g, g, a);
        }, Vector4.zero, "AB_GemStone"));

        private static Sprite GemSpec => s_spec != null ? s_spec : (s_spec = Make(32, 32, (x, y) =>
        {
            float m = Diamond(x, y);
            float a = Mathf.Clamp01(10.5f - m + 0.5f);
            if (a <= 0f) return Color.clear;
            float t = ((x + 0.5f) / 32f + 1f - (y + 0.5f) / 32f) * 0.5f;
            return new Color(1f, 1f, 1f, a * Mathf.Clamp01(0.6f * (1f - t * 2.2f)));
        }, Vector4.zero, "AB_GemSpec"));

        private static Sprite Make(int w, int h, Func<int, int, Color> px, Vector4 border, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            var data = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    data[y * w + x] = px(x, y);
            tex.SetPixels32(data);
            tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            s.name = name;
            return s;
        }
    }
}
