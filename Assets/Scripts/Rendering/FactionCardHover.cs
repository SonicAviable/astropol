using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Hover + фокус карточки фракции.
    ///   • Hover — плавное усиление яркости, видео играет.
    ///   • Фокус — карточка максимально увеличена, остальные гаснут.
    /// Управляется через SetFocused() из UIManager.
    /// </summary>
    public class FactionCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private RectTransform _rect;
        private Outline _outline;
        private CanvasGroup _group;
        private FactionVideoBackground _video;

        private Color _accentColor;
        private Color _baseBorderColor;
        private Vector2 _baseBorderDistance;
        private Vector3 _baseScale = Vector3.one;

        private bool _hovered;
        private bool _focused;

        [SerializeField] private float hoverScale = 1.025f;
        [SerializeField] private float focusScale = 1.06f;
        [SerializeField] private float unfocusedScale = 0.96f;
       [SerializeField] private float unfocusedAlpha = 0.35f;
        [SerializeField] private float lerpSpeed = 14f;
        [SerializeField] private float borderBoost = 2.2f;

        public void Configure(RectTransform rect, Outline outline, CanvasGroup group,
                              Color accent, FactionVideoBackground video)
        {
            _rect = rect;
            _outline = outline;
            _group = group;
            _accentColor = accent;
            _video = video;

            _baseScale = Vector3.one;

            if (_outline != null)
            {
                _baseBorderDistance = _outline.effectDistance;
                _baseBorderColor = _outline.effectColor;
            }
        }

        public void SetFocused(bool focused)
        {
            _focused = focused;
            if (focused) _video?.Play();
            else if (!_hovered) _video?.Pause();
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _hovered = true;
            _video?.Play();
        }

        public void OnPointerExit(PointerEventData e)
        {
            _hovered = false;
            if (!_focused) _video?.Pause();
        }

        private void Update()
        {
            if (_rect == null) return;

            float dt = Time.unscaledDeltaTime * lerpSpeed;

            // ----- Scale -----
            float targetScale;
            if (_focused) targetScale = focusScale;
            else if (_hovered) targetScale = hoverScale;
            else targetScale = unfocusedScale;

            _rect.localScale = Vector3.Lerp(_rect.localScale, Vector3.one * targetScale, dt);

            // ----- Alpha -----
            if (_group != null)
{
    float targetAlpha = _focused ? 1f : (_hovered ? 0.9f : unfocusedAlpha);
    _group.alpha = Mathf.Lerp(_group.alpha, targetAlpha, dt);
}

            // ----- Outline -----
            if (_outline != null)
            {
                bool active = _focused || _hovered;
                Color targetColor = active
                    ? _accentColor
                    : new Color(_baseBorderColor.r, _baseBorderColor.g, _baseBorderColor.b, 0.45f);
                _outline.effectColor = Color.Lerp(_outline.effectColor, targetColor, dt);

                Vector2 targetDist = active
                    ? _baseBorderDistance * borderBoost
                    : _baseBorderDistance;
                _outline.effectDistance = Vector2.Lerp(_outline.effectDistance, targetDist, dt);
            }
        }
    }
}