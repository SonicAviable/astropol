using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    public partial class TooltipSystem : MonoBehaviour
    {
        public static TooltipSystem Instance { get; private set; }

        private RectTransform _root;
        private CanvasGroup _group;
        private Text _text;
        private Image _bgImage;
        private Image _frame;
        private LiquidGlassEffect _fx;
        private RectTransform _textRt, _band;
        private StarPortrait _starPic;
        private Image _bandBg, _bandLine, _orb, _orbGlow, _orbIcon, _badge, _orbCore;
        private Text _title, _sub, _badgeText;
        private static readonly Color PanelTint = new Color(0.022f, 0.052f, 0.074f, 0.97f);

        private static readonly Color TipRim = new Color(0.45f, 0.95f, 0.90f, 0.50f);

        private float _targetAlpha = 0f;
        private Vector3 _targetScale = Vector3.one;
        private float _scale = 1f, _scaleV;
        private float _lockPulse;
        private bool _isLocked = false;
        private string _pendingText = "";
        private float _hoverTimer = 0f;
        private const float AutoShowDelay = 0.25f;
        private const float Pad = 12f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            BuildTooltipUI();
        }

        private void OnDestroy()
        {
            // Канвас живёт в DontDestroyOnLoad — убираем его вместе с системой (перезапуск партии)
            if (_root != null && _root.parent != null) Destroy(_root.parent.gameObject);
            if (Instance == this) Instance = null;
        }

        private void BuildTooltipUI()
        {
            if (_root != null) return;

            var canvasObj = new GameObject("TooltipRootCanvas");
            DontDestroyOnLoad(canvasObj);

            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 3500;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObj.AddComponent<UIScaleTarget>();

            canvasObj.AddComponent<GraphicRaycaster>();

            var panelObj = new GameObject("TooltipPanel");
            panelObj.transform.SetParent(canvasObj.transform, false);

            _root = panelObj.AddComponent<RectTransform>();
            _root.pivot = new Vector2(0f, 1f);
            _root.sizeDelta = new Vector2(300, 80);

            _group = panelObj.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            // Кино-карта (вариант B): тёмная панель с тонкой рамкой, сверху — цветная шапка
            _bgImage = panelObj.AddComponent<Image>();
            _bgImage.color = PanelTint;
            _bgImage.raycastTarget = true;
            LG.Ignore(panelObj);
            var shadow = panelObj.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(4f, -6f);

            _band = Rt(panelObj.transform, "Band");
            _band.anchorMin = new Vector2(0f, 1f);
            _band.anchorMax = new Vector2(1f, 1f);
            _band.pivot = new Vector2(0.5f, 1f);
            _bandBg = Img(_band, "Bg", MenuArt.HorizontalFade, Color.white);
            _bandBg.rectTransform.Stretch();
            _orbGlow = Img(_band, "OrbGlow", FactionSelectScreen.RadialSprite(), Color.white);
            _orb = Img(_band, "Orb", HudArt.Star(), Color.white);
            _orbCore = Img(_band, "OrbCore", FactionSelectScreen.RadialSprite(), Color.white);
            _orbIcon = Img(_band, "OrbIcon", null, Color.white);
            _orbIcon.preserveAspect = true;
            _starPic = StarPortrait.Create(_band, "Tooltip");
            _bandLine = Img(_band, "Line", null, Color.white);
            _bandLine.rectTransform.anchorMin = new Vector2(0f, 0f);
            _bandLine.rectTransform.anchorMax = new Vector2(1f, 0f);
            _bandLine.rectTransform.pivot = new Vector2(0.5f, 0f);
            _bandLine.rectTransform.sizeDelta = new Vector2(0f, 1f);
            _title = Txt(_band, GameFont.Display, 16, UIManager.DS.TextPrimary, FontStyle.Normal);
            _sub = Txt(_band, GameFont.Bold, 10, Color.white, FontStyle.Bold);
            _badge = Img(_band, "Badge", null, Color.white);
            _badgeText = Txt(_badge.transform, GameFont.Bold, 10, new Color(0.01f, 0.06f, 0.08f), FontStyle.Bold);
            _badgeText.alignment = TextAnchor.MiddleCenter;
            _badgeText.rectTransform.Stretch();

            _frame = Img(panelObj.transform, "Frame", HudArt.CutFrame(3, 1.2f), TipRim);
            _frame.type = Image.Type.Sliced;
            _frame.rectTransform.Stretch();

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(panelObj.transform, false);
            var tRt = textObj.AddComponent<RectTransform>();
            _textRt = tRt;
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.offsetMin = new Vector2(Pad, Pad);
            tRt.offsetMax = new Vector2(-Pad, -Pad - 4f);

            _text = textObj.AddComponent<Text>();
            _text.font = GameFont.Regular;
            _text.fontSize = 12;
            _text.fontStyle = FontStyle.Normal;
            _text.color = UIManager.DS.TextPrimary;
            _text.alignment = TextAnchor.UpperLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.lineSpacing = 1.25f;
            _text.supportRichText = true;
            _text.raycastTarget = false;

            var textShadow = textObj.AddComponent<Outline>();
            textShadow.effectColor = UIManager.DS.TextShadow;
            textShadow.effectDistance = new Vector2(1f, -1f);

            panelObj.SetActive(false);
        }

        public static void Show(string text)
        {
            if (Instance == null || string.IsNullOrEmpty(text)) return;
            GameSettings.EnsureLoaded();
            if (!GameSettings.Tooltips) return;
            Instance.RequestShow(text);
        }

        public static void Hide()
        {
            if (Instance == null) return;
            Instance.RequestHide();
        }

        /// <summary>
        /// Спрятать подсказку даже закреплённую: элемент под ней исчез (например, алерт скрыт правым кликом —
        /// тот же ПКМ при нажатии успел закрепить его подсказку, а скрытие приходит при отпускании).
        /// </summary>
        public static void ForceHide()
        {
            if (Instance == null) return;
            Instance._isLocked = false;
            Instance.RequestHide();
        }

        /// <summary>
        /// Спрятать подсказку, только если это та самая, что показывал вызывающий (text). Нужно подсказкам
        /// объектов карты: курсор ушёл со звезды на значок интерфейса — значок уже попросил свою подсказку,
        /// и звезда не должна её погасить.
        /// </summary>
        public static void HideIf(string text)
        {
            if (Instance == null || string.IsNullOrEmpty(text)) return;
            Instance.RequestHideIf(text);
        }

        private void RequestShow(string text)
        {
            if (_root == null) BuildTooltipUI();
            if (_root == null) return;
            if (_isLocked) return;
            if (_suppressed) return;

            _pendingText = text;
            _hoverTimer = 0f;
        }

        // После клика по карте подсказка не всплывает, пока мышь не сдвинется:
        // ПКМ по системе — это приказ флоту, а не просьба показать справку.
        private bool _suppressed;
        private Vector2 _suppressPos;
        private const float SuppressMovePx = 14f;

        private void RequestHide()
        {
            if (_isLocked) return;
            _pendingText = "";
            _hoverTimer = 0f;
            StartClosingAnimation();
        }

        private void RequestHideIf(string text)
        {
            if (_isLocked) return;
            if (_pendingText == text) { RequestHide(); return; }
            // На экране ещё наша подсказка, а своей очереди ждёт чужая — гасим нашу, чужая откроется следом
            if (_text != null && _shownRaw == text && _targetAlpha > 0.1f) StartClosingAnimation();
        }

        // ---------- значки в тексте: [[Minerals]] или [[Energy:#F2C747]] → настоящий значок интерфейса ----------
        private const string IconGap = "\u2002\u2002\u2002";   // место под значок: три полукруглые шпации
        private static readonly System.Text.RegularExpressions.Regex IconToken =
            new System.Text.RegularExpressions.Regex(@"\[\[(\w+)(?::(#[0-9A-Fa-f]{6}))?\]\]");
        private static readonly System.Text.RegularExpressions.Regex RichTag =
            new System.Text.RegularExpressions.Regex("<[^>]+>");
        private readonly System.Collections.Generic.List<(int full, int visible, LGIcon icon, Color c)> _iconMarks =
            new System.Collections.Generic.List<(int, int, LGIcon, Color)>();
        /// <summary>Текст подсказки как его передали (с метками значков) — по нему узнаём «свою» подсказку.</summary>
        private string _shownRaw;
        private readonly System.Collections.Generic.List<Image> _iconPool = new System.Collections.Generic.List<Image>();

        /// <summary>Заменить метки значков местом под них и запомнить, где они стоят (с тегами и без).</summary>
        private string ParseIcons(string text) => ParseIcons(text, _iconMarks);

        private static string ParseIcons(string text, System.Collections.Generic.List<(int full, int visible, LGIcon icon, Color c)> marks)
        {
            marks.Clear();
            if (text.IndexOf("[[", System.StringComparison.Ordinal) < 0) return text;
            var sb = new System.Text.StringBuilder();
            int last = 0;
            foreach (System.Text.RegularExpressions.Match m in IconToken.Matches(text))
            {
                sb.Append(text, last, m.Index - last);
                last = m.Index + m.Length;
                if (!System.Enum.TryParse(m.Groups[1].Value, out LGIcon icon)) continue;
                Color c = Color.white;
                if (m.Groups[2].Success) ColorUtility.TryParseHtmlString(m.Groups[2].Value, out c);
                int full = sb.Length;
                int visible = RichTag.Replace(sb.ToString(), "").Length;
                marks.Add((full, visible, icon, c));
                sb.Append(IconGap);
            }
            sb.Append(text, last, text.Length - last);
            return sb.ToString();
        }

        /// <summary>
        /// Поставить значки на места меток. Позиции символов — из генератора текста под итоговый размер; в разных
        /// версиях Unity он считает символы с тегами разметки или без, поэтому берётся тот индекс, что подходит по длине.
        /// </summary>
        private void PlaceIcons(Vector2 textSize) => PlaceIcons(_text, _iconMarks, _iconPool, textSize);

        private static void PlaceIcons(Text _text, System.Collections.Generic.List<(int full, int visible, LGIcon icon, Color c)> _iconMarks,
                                       System.Collections.Generic.List<Image> _iconPool, Vector2 textSize)
        {
            int used = 0;
            if (_iconMarks.Count > 0)
            {
                var gen = new TextGenerator();
                gen.Populate(_text.text, _text.GetGenerationSettings(textSize));
                float ppu = _text.pixelsPerUnit > 0f ? _text.pixelsPerUnit : 1f;
                var chars = gen.characters;
                var lines = gen.lines;
                int visibleLen = RichTag.Replace(_text.text, "").Length;
                bool byVisible = gen.characterCount <= visibleLen + 1;
                float size = _text.fontSize * 1.15f;
                foreach (var mk in _iconMarks)
                {
                    int idx = byVisible ? mk.visible : mk.full;
                    if (idx < 0 || idx >= chars.Count) continue;
                    var ci = chars[idx];
                    float lineH = _text.fontSize * 1.2f;
                    for (int l = lines.Count - 1; l >= 0; l--)
                        if (lines[l].startCharIdx <= idx) { lineH = lines[l].height; break; }
                    if (used >= _iconPool.Count)
                    {
                        var go = new GameObject("InlineIcon", typeof(RectTransform));
                        go.transform.SetParent(_text.transform, false);
                        var im = go.AddComponent<Image>();
                        im.raycastTarget = false;
                        im.preserveAspect = true;
                        _iconPool.Add(im);
                    }
                    var img = _iconPool[used++];
                    img.sprite = LGIcons.Get(mk.icon);
                    img.color = mk.c;
                    var rt = img.rectTransform;
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(size, size);
                    rt.anchoredPosition = new Vector2(ci.cursorPos.x / ppu + size * 0.5f + 1f, (ci.cursorPos.y - lineH * 0.5f) / ppu);
                    img.gameObject.SetActive(true);
                }
            }
            for (int i = used; i < _iconPool.Count; i++) _iconPool[i].gameObject.SetActive(false);
        }

        // ---------- Шапка подсказки (кино-карта) ----------
        // Первая строка «<b>Заголовок</b>» (можно со значком перед ней: [[Energy:#F2C747]]<b>Гелий-3</b>) становится шапкой.
        // Необязательная метка в самом начале добавляет превью и подробности:
        //   [[hdr|#RRGGBB|подзаголовок|метка справа|orb]]   (orb — светящийся шар цвета #RRGGBB, «-» — без шара;
        //   orb:Id — живая звезда системы Id, как на карте)

        private struct Head
        {
            public bool On, Orb, HasIcon;
            public int StarId;
            public string Title, Sub, Badge;
            public Color Accent;
            public LGIcon Icon;
        }

        private static readonly System.Text.RegularExpressions.Regex HeadToken =
            new System.Text.RegularExpressions.Regex(@"^\[\[hdr\|(#[0-9A-Fa-f]{6})\|([^|\]]*)\|([^|\]]*)\|(orb(?::\d+)?|-)\]\]\n?");
        private static readonly System.Text.RegularExpressions.Regex TitleLine =
            new System.Text.RegularExpressions.Regex(@"^(?:\[\[(\w+)(?::(#[0-9A-Fa-f]{6}))?\]\])?<b>([^<\n]+)</b>[ \t]*(?:\n|$)");

        private static string SplitHeader(string text, out Head h)
        {
            h = new Head { StarId = -1, Accent = new Color(0.37f, 0.82f, 0.75f, 1f), Sub = "", Badge = "" };
            var m = HeadToken.Match(text);
            if (m.Success)
            {
                if (ColorUtility.TryParseHtmlString(m.Groups[1].Value, out var c)) h.Accent = c;
                h.Sub = m.Groups[2].Value;
                h.Badge = m.Groups[3].Value;
                var orb = m.Groups[4].Value;
                h.Orb = orb.StartsWith("orb");
                if (orb.Length > 4 && int.TryParse(orb.Substring(4), out int sid)) h.StarId = sid;
                text = text.Substring(m.Length);
            }
            var t = TitleLine.Match(text);
            if (!t.Success) return text;
            h.On = true;
            h.Title = t.Groups[3].Value.Trim();
            if (t.Groups[1].Success && System.Enum.TryParse(t.Groups[1].Value, out LGIcon icon))
            {
                h.HasIcon = true;
                h.Icon = icon;
                if (t.Groups[2].Success && ColorUtility.TryParseHtmlString(t.Groups[2].Value, out var ic) && !m.Success) h.Accent = ic;
            }
            return text.Substring(t.Length).TrimStart('\n');
        }

        /// <summary>Расставить шапку; вернуть её высоту и нужную ей ширину панели.</summary>
        private float LayoutHeader(Head h, out float needW)
        {
            needW = 0f;
            _band.gameObject.SetActive(h.On);
            if (!h.On) return 0f;
            var a = h.Accent;
            bool visual = h.Orb || h.HasIcon;
            bool sub = !string.IsNullOrEmpty(h.Sub);
            float bandH = visual ? 60f : (sub ? 48f : 36f);
            _band.sizeDelta = new Vector2(0f, bandH);
            _band.anchoredPosition = Vector2.zero;
            _bandBg.color = new Color(a.r * 0.50f, a.g * 0.45f, a.b * 0.40f, 0.92f);
            _bandLine.color = new Color(a.r, a.g, a.b, 0.65f);

            float x = 14f;
            // Живая звезда системы (та же модель, что на карте); нет её — нарисованная
            bool live = h.Orb && h.StarId >= 0 && _starPic != null && _starPic.Show(EmpireStats.GetSystem(h.StarId));
            if (!live && _starPic != null) _starPic.Hide();
            _orb.gameObject.SetActive(h.Orb && !live);
            _orbCore.gameObject.SetActive(h.Orb && !live);
            _orbGlow.gameObject.SetActive(visual);
            _orbIcon.gameObject.SetActive(!h.Orb && h.HasIcon);
            if (visual)
            {
                var c = new Vector2(x + 20f, -bandH * 0.5f);
                _orbGlow.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), c, new Vector2(96f, 96f));
                _orbGlow.color = new Color(a.r, a.g, a.b, 0.45f);
                if (h.Orb)
                {
                    // Нарисованная звезда: корона и лучи цвета звезды, белое ядро
                    _orb.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), c, new Vector2(62f, 62f));
                    _orb.color = Color.Lerp(a, Color.white, 0.15f);
                    _orbCore.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), c, new Vector2(16f, 16f));
                    _orbCore.color = new Color(1f, 1f, 1f, 0.95f);
                    if (live) _starPic.Image.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), c, new Vector2(84f, 84f));
                }
                else
                {
                    _orbIcon.sprite = LGIcons.Get(h.Icon);
                    _orbIcon.color = Color.Lerp(a, Color.white, 0.2f);
                    _orbIcon.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), c, new Vector2(30f, 30f));
                }
                x += 40f + 12f;
            }

            _title.text = h.Title.ToUpper();
            float titleY = sub ? -bandH * 0.5f + 8f : -bandH * 0.5f;
            float tw = _title.preferredWidth + _title.text.Length * 1.0f;
            _title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(x, titleY), new Vector2(tw + 4f, 22f));
            _sub.gameObject.SetActive(sub);
            float sw = 0f;
            if (sub)
            {
                _sub.text = h.Sub.ToUpper();
                _sub.color = Color.Lerp(a, Color.white, 0.45f);
                sw = _sub.preferredWidth + _sub.text.Length * 1.8f;
                _sub.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(x, -bandH * 0.5f - 11f), new Vector2(sw + 4f, 14f));
            }

            bool badge = !string.IsNullOrEmpty(h.Badge);
            _badge.gameObject.SetActive(badge);
            float bw = 0f;
            if (badge)
            {
                _badgeText.text = h.Badge.ToUpper();
                bw = _badgeText.preferredWidth + 14f;
                _badge.color = new Color(0.37f, 0.82f, 0.75f, 1f);
                _badge.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10f, -9f), new Vector2(bw, 16f));
            }
            needW = x + Mathf.Max(tw, sw) + (badge ? bw + 24f : 18f);
            return bandH;
        }

        private static RectTransform Rt(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image Img(Transform parent, string name, Sprite sp, Color c)
        {
            var im = Rt(parent, name).gameObject.AddComponent<Image>();
            im.sprite = sp;
            im.color = c;
            im.raycastTarget = false;
            return im;
        }

        private static Text Txt(Transform parent, Font f, int size, Color c, FontStyle st)
        {
            var t = Rt(parent, "Txt").gameObject.AddComponent<Text>();
            t.font = f;
            t.fontSize = size;
            t.fontStyle = st;
            t.color = c;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = false;
            t.Track(1.2f);
            return t;
        }

        private void TriggerOpen(string text, bool lockInPlace = false)
        {
            _shownRaw = text;
            string body = SplitHeader(text, out var head);
            _text.text = ParseIcons(body);
            _root.gameObject.SetActive(true);

            float bandH = LayoutHeader(head, out float headW);
            if (HasBlocks(body))
            {
                _text.gameObject.SetActive(false);
                float topB = head.On ? bandH + 8f : Pad;
                LayoutBlocks(body, topB, headW, out float bw, out float bh);
                _root.sizeDelta = new Vector2(bw, bh);
                PlaceIcons(new Vector2(1f, 1f));      // основной текст скрыт — его значки тоже
                FinishOpen(lockInPlace);
                return;
            }
            HideBlocks();
            bool hasBody = body.Trim().Length > 0;
            _text.gameObject.SetActive(hasBody);
            float preferredW = (hasBody ? _text.preferredWidth : 0f) + Pad * 2f + 16f;
            float w = Mathf.Clamp(Mathf.Max(preferredW, headW), 240f, 400f);
            float top = head.On ? bandH + 10f : Pad;
            _textRt.offsetMin = new Vector2(Pad, Pad);
            _textRt.offsetMax = new Vector2(-Pad, -top);
            _root.sizeDelta = new Vector2(w, 400f);                 // ширина задана — высота текста считается с переносами
            float bodyH = hasBody ? _text.preferredHeight : 0f;
            float h = hasBody ? top + bodyH + Pad + 4f : bandH + 2f;
            _root.sizeDelta = new Vector2(w, h);
            PlaceIcons(new Vector2(w - Pad * 2f, h - top - Pad));
            FinishOpen(lockInPlace);
        }

        private void FinishOpen(bool lockInPlace)
        {
            UpdatePosition();

            bool wasHidden = _group.alpha < 0.05f;
            _targetAlpha = 1f;
            _targetScale = Vector3.one;
            if (wasHidden)
            {
                _scale = 0.9f;
                _scaleV = 0f;
                _root.localScale = Vector3.one * _scale;
            }

            _isLocked = lockInPlace;
            _group.blocksRaycasts = _isLocked;

            _frame.color = _isLocked
                ? new Color(UIManager.DS.Gold.r, UIManager.DS.Gold.g, UIManager.DS.Gold.b, 0.9f)
                : TipRim;
            if (_isLocked) _lockPulse = 1f;
        }

        private void StartClosingAnimation()
        {
            _targetAlpha = 0f;
            _targetScale = Vector3.one * 0.92f;
            _isLocked = false;
            if (_group != null) _group.blocksRaycasts = false;
        }

        private void Update()
        {
            if (_root == null) return;

            if (!string.IsNullOrEmpty(_pendingText) && !_isLocked)
            {
                _hoverTimer += Time.unscaledDeltaTime;
                if (_hoverTimer >= AutoShowDelay && _targetAlpha < 0.1f)
                {
                    TriggerOpen(_pendingText, false);
                }
            }

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool click = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);

            // Закрепление ПКМ — только для подсказок интерфейса (на карте ПКМ отдаёт приказ)
            if (Input.GetMouseButtonDown(1) && overUi && !string.IsNullOrEmpty(_pendingText))
            {
                TriggerOpen(_pendingText, true);
            }
            else if (click && !overUi && !_isLocked)
            {
                _suppressed = true;
                _suppressPos = Input.mousePosition;
                _pendingText = "";
                _hoverTimer = 0f;
                StartClosingAnimation();
            }

            if (_suppressed && Vector2.Distance(_suppressPos, Input.mousePosition) > SuppressMovePx)
                _suppressed = false;

            if (_isLocked && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Escape)))
            {
                Vector2 mousePos = Input.mousePosition;
                if (!RectTransformUtility.RectangleContainsScreenPoint(_root, mousePos))
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) UIEsc.Consume();   // Esc закрыл закреплённую подсказку — паузу не открываем
                    _isLocked = false;
                    _pendingText = "";
                    StartClosingAnimation();
                }
            }

            if (_root.gameObject.activeSelf)
            {
                float dt = Time.unscaledDeltaTime;
                float alphaSpeed = _targetAlpha > _group.alpha ? 9f : 7f;
                _group.alpha = Mathf.MoveTowards(_group.alpha, _targetAlpha, dt * alphaSpeed);
                _scale = LGEase.Spring(_scale, _targetScale.x, ref _scaleV, 3.6f, 0.6f, dt);
                _root.localScale = Vector3.one * _scale;

                if (!_isLocked) UpdatePosition();

                // Фиксация (ПКМ) — короткая вспышка неона
                if (_fx != null)
                {
                    _lockPulse = Mathf.MoveTowards(_lockPulse, 0f, dt * 2.2f);
                    _fx.SetPulse(_lockPulse);
                }

                if (_group.alpha <= 0.01f && _targetAlpha == 0f)
                {
                    _root.gameObject.SetActive(false);
                }
            }
        }

        private void UpdatePosition()
        {
            Vector2 mouse = Input.mousePosition;
            Vector2 pos = mouse + new Vector2(16f, -14f);

            if (pos.x + _root.sizeDelta.x > Screen.width)
                pos.x = mouse.x - _root.sizeDelta.x - 12f;
            if (pos.y - _root.sizeDelta.y < 0)
                pos.y = mouse.y + _root.sizeDelta.y + 12f;

            _root.position = pos;
        }
    }

    public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [TextArea(2, 6)] public string text;

        public void SetText(string t) => text = t;

        public void OnPointerEnter(PointerEventData e)
        {
            if (!string.IsNullOrEmpty(text)) TooltipSystem.Show(text);
        }

        public void OnPointerExit(PointerEventData e)
        {
            TooltipSystem.Hide();
        }

        private void OnDisable()
        {
            TooltipSystem.Hide();
        }
    }

    public class WorldTooltipTrigger : MonoBehaviour
    {
        [TextArea(2, 6)] public string text;
        /// <summary>Живой текст (пересчитывается при каждом наведении) — вместо статичного text.</summary>
        public System.Func<string> Dynamic;
        private SystemNameplate _nameplate;
        private bool _hovered;
        private string _shown;

        public void SetNameplate(SystemNameplate plate) => _nameplate = plate;
        public void SetText(string t) => text = t;

        private void Update()
        {
            if (Camera.main == null) return;

            var es = EventSystem.current;
            if (es != null && es.IsPointerOverGameObject())
            {
                // Курсор ушёл на интерфейс (например, на значок агента у звезды) — гасим только свою подсказку
                if (_hovered) { _hovered = false; TooltipSystem.HideIf(_shown); }
                return;
            }

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            bool hitThis = false;
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                var trigger = hit.collider.GetComponentInParent<WorldTooltipTrigger>();
                if (trigger == this) hitThis = true;
            }
            // Над планетой мини-системы своя карточка — подсказка звезды не нужна
            if (hitThis && GalaxyView.HoveredMiniPlanet != null) hitThis = false;

            if (hitThis)
            {
                string currentText = _nameplate != null ? _nameplate.GetTooltipContent() : Dynamic != null ? Dynamic() : text;

                if (!_hovered)
                {
                    _hovered = true;
                    _shown = currentText;
                    TooltipSystem.Show(currentText);
                }
            }
            else if (_hovered)
            {
                _hovered = false;
                TooltipSystem.HideIf(_shown);
            }
        }

        private void OnDisable()
        {
            if (_hovered) { _hovered = false; TooltipSystem.HideIf(_shown); }
        }
    }

    public static class TooltipHelper
    {
        public static void Attach(GameObject go, string text)
        {
            if (go == null) return;
            var t = go.GetComponent<TooltipTrigger>();
            if (t == null) t = go.AddComponent<TooltipTrigger>();
            t.text = text;

            var img = go.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;
        }
    }
}