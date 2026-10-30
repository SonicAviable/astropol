using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    public class TooltipSystem : MonoBehaviour
    {
        public static TooltipSystem Instance { get; private set; }

        private RectTransform _root;
        private CanvasGroup _group;
        private Text _text;
        private Image _bgImage;
        private Outline _outline;
        private LiquidGlassEffect _fx;

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

            _bgImage = panelObj.AddComponent<Image>();
            _bgImage.color = UIManager.DS.BgDeep;
            _bgImage.raycastTarget = true;

            // Outline — источник цвета неоновой кромки (бирюза / золото при фиксации)
            _outline = panelObj.AddComponent<Outline>();
            _outline.effectColor = TipRim;
            _outline.effectDistance = new Vector2(1.5f, -1.5f);
            _fx = LG.Glass(panelObj, 14f);
            _fx.ShadowMultiplier = 0.8f;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(panelObj.transform, false);
            var tRt = textObj.AddComponent<RectTransform>();
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
        private string ParseIcons(string text)
        {
            _iconMarks.Clear();
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
                _iconMarks.Add((full, visible, icon, c));
                sb.Append(IconGap);
            }
            sb.Append(text, last, text.Length - last);
            return sb.ToString();
        }

        /// <summary>
        /// Поставить значки на места меток. Позиции символов — из генератора текста под итоговый размер; в разных
        /// версиях Unity он считает символы с тегами разметки или без, поэтому берётся тот индекс, что подходит по длине.
        /// </summary>
        private void PlaceIcons(Vector2 textSize)
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

        private void TriggerOpen(string text, bool lockInPlace = false)
        {
            _shownRaw = text;
            _text.text = ParseIcons(text);
            _root.gameObject.SetActive(true);

            float preferredW = _text.preferredWidth + Pad * 2f + 16f;
            float w = Mathf.Clamp(preferredW, 240f, 400f);
            float h = _text.preferredHeight + Pad * 2f + 12f;
            _root.sizeDelta = new Vector2(w, h);
            PlaceIcons(new Vector2(w - Pad * 2f, h - Pad * 2f - 4f));

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

            _outline.effectColor = _isLocked
                ? new Color(UIManager.DS.Gold.r, UIManager.DS.Gold.g, UIManager.DS.Gold.b, 0.85f)
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