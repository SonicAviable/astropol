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

        private void RequestShow(string text)
        {
            if (_root == null) BuildTooltipUI();
            if (_root == null) return;
            if (_isLocked) return;

            _pendingText = text;
            _hoverTimer = 0f;
        }

        private void RequestHide()
        {
            if (_isLocked) return;
            _pendingText = "";
            _hoverTimer = 0f;
            StartClosingAnimation();
        }

        private void TriggerOpen(string text, bool lockInPlace = false)
        {
            _text.text = text;
            _root.gameObject.SetActive(true);

            float preferredW = _text.preferredWidth + Pad * 2f + 16f;
            float w = Mathf.Clamp(preferredW, 240f, 400f);
            float h = _text.preferredHeight + Pad * 2f + 12f;
            _root.sizeDelta = new Vector2(w, h);

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

            if (Input.GetMouseButtonDown(1) && !string.IsNullOrEmpty(_pendingText))
            {
                TriggerOpen(_pendingText, true);
            }

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
        private SystemNameplate _nameplate;
        private bool _hovered;

        public void SetNameplate(SystemNameplate plate) => _nameplate = plate;
        public void SetText(string t) => text = t;

        private void Update()
        {
            if (Camera.main == null) return;

            var es = EventSystem.current;
            if (es != null && es.IsPointerOverGameObject())
            {
                if (_hovered) { _hovered = false; TooltipSystem.Hide(); }
                return;
            }

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            bool hitThis = false;
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                var trigger = hit.collider.GetComponentInParent<WorldTooltipTrigger>();
                if (trigger == this) hitThis = true;
            }

            if (hitThis)
            {
                string currentText = _nameplate != null ? _nameplate.GetTooltipContent() : text;

                if (!_hovered)
                {
                    _hovered = true;
                    TooltipSystem.Show(currentText);
                }
            }
            else if (_hovered)
            {
                _hovered = false;
                TooltipSystem.Hide();
            }
        }

        private void OnDisable()
        {
            if (_hovered) { _hovered = false; TooltipSystem.Hide(); }
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