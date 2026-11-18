using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Пункт левого меню HUD «Атласа»: круглая кнопка со значком. При наведении кнопка чуть растёт и
    /// выдвигает справа флажок с названием и клавишей; пока окно пункта открыто — кольцо и значок коралловые
    /// и флажок виден постоянно.
    /// </summary>
    public sealed class HexNavItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Fill, Rim, Icon, Glow;
        public RectTransform Flag;
        public CanvasGroup FlagGroup;
        public Color Accent;
        public System.Func<bool> IsOpen;

        // «Атлас»: все пункты одного вида — приглушённый значок без кольца; наведение — кремовое кольцо,
        // открытое окно — коралловое кольцо и коралловый значок. Цвет пункта (Accent) остаётся только у флажка.
        private static readonly Color FillIdle = new Color(0.035f, 0.067f, 0.118f, 0.85f);
        private static readonly Color IconIdle = new Color(0.55f, 0.53f, 0.48f, 1f);
        private static readonly Color Cream = new Color(0.925f, 0.898f, 0.827f, 1f);
        private static readonly Color Coral = new Color(1f, 0.42f, 0.341f, 1f);

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
            if (Fill != null) Fill.color = FillIdle;
            if (Rim != null)
            {
                var rc = Color.Lerp(Cream, Coral, _open);
                rc.a = Mathf.Max(_hover * 0.45f, _open);
                Rim.color = rc;
            }
            if (Icon != null) Icon.color = Color.Lerp(Color.Lerp(IconIdle, Cream, _hover), Coral, _open);
            if (Glow != null) Glow.color = new Color(0f, 0f, 0f, 0f);
            if (FlagGroup != null)
            {
                FlagGroup.alpha = LGEase.OutCubic(hi);
                Flag.anchoredPosition = new Vector2(Mathf.Lerp(-6f, 8f, LGEase.OutCubic(hi)), 0f);
            }
            if (!Mathf.Approximately(_hover, _hoverTarget) || !Mathf.Approximately(_open, openTarget)) _dirty = true;
        }
    }
}
