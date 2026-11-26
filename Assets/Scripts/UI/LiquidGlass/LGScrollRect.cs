using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// ScrollRect с плавной прокруткой колесом: стандартный сдвигает список скачком на scrollSensitivity за щелчок,
    /// здесь щелчки копятся в цель, а список догоняет её за ~0,15 с (на unscaled time — работает и на паузе).
    /// Перетаскивание, полоса прокрутки и инерция — как у обычного ScrollRect; если позицию поменял код
    /// (прокрутка к началу после перестройки списка) или игрок схватил список — анимация уступает.
    /// </summary>
    public class LGScrollRect : ScrollRect
    {
        /// <summary>Скорость догоняния: больше — резче. 18 ≈ 95% пути за 0,17 с.</summary>
        public float wheelSmoothing = 18f;

        private Vector2 _target;      // цель в нормализованных координатах (0..1)
        private Vector2 _lastSet;     // что выставили сами в прошлом кадре — чтобы заметить чужую прокрутку
        private bool _animating;

        public override void OnScroll(PointerEventData data)
        {
            if (!IsActive() || content == null) return;
            Vector2 range = Range();

            // Та же раскладка дельты, что у ScrollRect: вниз у колеса — плюс, в UI вверх — плюс
            Vector2 d = data.scrollDelta;
            d.y *= -1f;
            if (vertical && !horizontal) { if (Mathf.Abs(d.x) > Mathf.Abs(d.y)) d.y = d.x; d.x = 0f; }
            if (horizontal && !vertical) { if (Mathf.Abs(d.y) > Mathf.Abs(d.x)) d.x = d.y; d.y = 0f; }
            d *= scrollSensitivity;

            Vector2 from = _animating ? _target : normalizedPosition;
            Vector2 to = from;
            // Сдвиг содержимого на +px показывает то, что ниже/левее: нормализованная позиция уменьшается
            if (horizontal && range.x > 0.5f) to.x = Mathf.Clamp01(from.x - d.x / range.x);
            if (vertical && range.y > 0.5f) to.y = Mathf.Clamp01(from.y - d.y / range.y);
            if ((to - normalizedPosition).sqrMagnitude < 1e-10f) return;

            StopMovement();
            _target = to;
            if (!_animating) _lastSet = normalizedPosition;
            _animating = true;
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            _animating = false;
            base.OnBeginDrag(eventData);
        }

        protected override void OnDisable()
        {
            _animating = false;
            base.OnDisable();
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!_animating || content == null) return;

            Vector2 cur = normalizedPosition;
            if ((cur - _lastSet).sqrMagnitude > 1e-6f) { _animating = false; return; }   // позицию поменял кто-то другой

            Vector2 range = Range();
            float k = 1f - Mathf.Exp(-wheelSmoothing * Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            Vector2 next = Vector2.Lerp(cur, _target, k);
            Vector2 leftPx = Vector2.Scale(_target - next, range);
            if (Mathf.Abs(leftPx.x) < 0.5f && Mathf.Abs(leftPx.y) < 0.5f) { next = _target; _animating = false; }

            if (!horizontal || range.x <= 0.5f) next.x = cur.x;
            if (!vertical || range.y <= 0.5f) next.y = cur.y;
            normalizedPosition = next;
            velocity = Vector2.zero;
            _lastSet = normalizedPosition;
        }

        /// <summary>На сколько пикселей содержимое больше окна просмотра по каждой оси.</summary>
        private Vector2 Range()
        {
            var view = viewport != null ? viewport : (RectTransform)transform;
            Vector2 c = content.rect.size, v = view.rect.size;
            return new Vector2(Mathf.Max(0f, c.x - v.x), Mathf.Max(0f, c.y - v.y));
        }
    }
}
