using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Пункт левого меню голо-HUD: шестиугольная сота со значком. При наведении сота чуть растёт и
    /// выдвигает справа флажок с названием и клавишей; пока окно пункта открыто — сота залита цветом
    /// пункта и флажок виден постоянно.
    /// </summary>
    public sealed class HexNavItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Fill, Rim, Icon, Glow;
        public RectTransform Flag;
        public CanvasGroup FlagGroup;
        public Color Accent;
        public System.Func<bool> IsOpen;

        private static readonly Color FillIdle = new Color(0.030f, 0.070f, 0.098f, 0.95f);

        private float _hover, _hoverTarget, _open;
        private float _checkTimer;
        private bool _openNow;
        private bool _dirty = true;

        public void OnPointerEnter(PointerEventData e) { _hoverTarget = 1f; _dirty = true; }
        public void OnPointerExit(PointerEventData e) { _hoverTarget = 0f; _dirty = true; }

        private void OnDisable() { _hover = _hoverTarget = 0f; _dirty = true; }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if ((_checkTimer -= dt) <= 0f)
            {
                _checkTimer = 0.2f;
                bool open = IsOpen != null && IsOpen();
                if (open != _openNow) { _openNow = open; _dirty = true; }
            }

            float openTarget = _openNow ? 1f : 0f;
            if (!_dirty && Mathf.Approximately(_hover, _hoverTarget) && Mathf.Approximately(_open, openTarget)) return;
            _dirty = false;
            _hover = Mathf.MoveTowards(_hover, _hoverTarget, dt * 7f);
            _open = Mathf.MoveTowards(_open, openTarget, dt * 5f);

            float hi = Mathf.Max(_hover, _open);
            transform.localScale = Vector3.one * (1f + 0.06f * LGEase.OutCubic(_hover));
            if (Fill != null)
            {
                var tint = new Color(Accent.r * 0.45f, Accent.g * 0.45f, Accent.b * 0.45f, 0.96f);
                Fill.color = Color.Lerp(FillIdle, tint, _open * 0.85f + _hover * 0.15f);
            }
            if (Rim != null) Rim.color = new Color(Accent.r, Accent.g, Accent.b, Mathf.Lerp(0.55f, 1f, hi));
            if (Icon != null) Icon.color = Color.Lerp(Accent, Color.white, _open * 0.8f);
            if (Glow != null) Glow.color = new Color(Accent.r, Accent.g, Accent.b, 0.30f * hi);
            if (FlagGroup != null)
            {
                FlagGroup.alpha = LGEase.OutCubic(hi);
                Flag.anchoredPosition = new Vector2(Mathf.Lerp(-6f, 8f, LGEase.OutCubic(hi)), 0f);
            }
            if (!Mathf.Approximately(_hover, _hoverTarget) || !Mathf.Approximately(_open, openTarget)) _dirty = true;
        }
    }
}
