using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>Пружины и кривые для UI-анимаций (всё на unscaled time — UI живёт и на паузе).</summary>
    public static class LGEase
    {
        public static float OutCubic(float t) { t = Mathf.Clamp01(t); float u = 1f - t; return 1f - u * u * u; }
        public static float InCubic(float t) { t = Mathf.Clamp01(t); return t * t * t; }
        public static float InOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        /// <summary>Мягкий "перелёт" — как у iOS-пружины.</summary>
        public static float OutBack(float t, float overshoot = 1.1f)
        {
            t = Mathf.Clamp01(t);
            float c3 = overshoot + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + overshoot * u * u;
        }

        /// <summary>Неявная (устойчивая) затухающая пружина.</summary>
        public static float Spring(float x, float target, ref float v, float frequencyHz, float damping, float dt)
        {
            if (dt <= 0f) return x;
            float w = 2f * Mathf.PI * frequencyHz;
            float f = 1f + 2f * dt * damping * w;
            float ww = w * w;
            float hww = dt * ww;
            float hhww = dt * hww;
            float detInv = 1f / (f + hhww);
            float detX = f * x + dt * v + hhww * target;
            float detV = v + hww * (target - x);
            v = detV * detInv;
            return detX * detInv;
        }
    }

    /// <summary>
    /// Плавное появление/исчезновение панели: прозрачность + пружинный масштаб (+ сдвиг).
    /// Появление запускается само в OnEnable — любой SetActive(true) уже анимирован.
    /// Для исчезновения используйте LG.Hide(go) вместо SetActive(false).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGAppear : MonoBehaviour
    {
        public enum Kind { Pop, Fade, SlideLeft, SlideRight, SlideUp, SlideDown }

        public Kind kind = Kind.Pop;
        public float inDuration = 0.38f;
        public float outDuration = 0.2f;
        public float distance = 28f;
        public float fromScale = 0.94f;
        [Tooltip("Задержка появления (для каскада панелей).")]
        public float delay = 0f;
        public bool playOnEnable = true;
        [Tooltip("Блокировать клики во время исчезновения.")]
        public bool manageRaycasts = true;

        private enum State { Idle, In, Out }

        private CanvasGroup _cg;
        private RectTransform _rt;
        private State _state = State.Idle;
        private float _t;
        private float _startAlpha;
        private float _startProgress;
        private float _delayLeft;
        private Vector2 _restPos;
        private Vector3 _restScale = Vector3.one;
        private bool _restCaptured;
        private System.Action _onHidden;

        public bool IsHiding => _state == State.Out;
        public bool IsAnimating => _state != State.Idle;

        private void Awake()
        {
            _rt = transform as RectTransform;
            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            if (playOnEnable) PlayIn();
        }

        private void OnDisable()
        {
            if (_state != State.Idle) RestoreRest();
            _state = State.Idle;
            var cb = _onHidden;
            _onHidden = null;
            cb?.Invoke();
        }

        private void CaptureRest()
        {
            if (_rt == null) return;
            _restPos = _rt.anchoredPosition;
            _restScale = _rt.localScale == Vector3.zero ? Vector3.one : _rt.localScale;
            _restCaptured = true;
        }

        private void RestoreRest()
        {
            if (_cg != null)
            {
                _cg.alpha = 1f;
                if (manageRaycasts) _cg.blocksRaycasts = true;
            }
            // Восстанавливаем всё безусловно: вид анимации мог смениться после старта
            // (LG.Motion задаёт kind уже после AddComponent → OnEnable успевает запустить Pop).
            if (_rt != null && _restCaptured)
            {
                _rt.anchoredPosition = _restPos;
                _rt.localScale = _restScale;
            }
        }

        private bool UsesPosition => kind == Kind.SlideLeft || kind == Kind.SlideRight || kind == Kind.SlideUp || kind == Kind.SlideDown;
        private bool UsesScale => kind == Kind.Pop || UsesPosition;

        private Vector2 SlideOffset
        {
            get
            {
                switch (kind)
                {
                    case Kind.SlideLeft:  return new Vector2(-distance, 0f);
                    case Kind.SlideRight: return new Vector2(distance, 0f);
                    case Kind.SlideUp:    return new Vector2(0f, distance);
                    case Kind.SlideDown:  return new Vector2(0f, -distance);
                }
                return Vector2.zero;
            }
        }

        public void PlayIn()
        {
            if (_cg == null) Awake();
            float progress = 0f;
            if (_state == State.Out)
            {
                // Разворот из середины исчезновения — без рывка.
                progress = Mathf.Clamp01(1f - _t);
                _onHidden = null;
            }
            else
            {
                CaptureRest();
            }

            _state = State.In;
            _t = 0f;
            _delayLeft = progress > 0f ? 0f : delay;
            _startProgress = progress;
            _startAlpha = progress <= 0f ? 0f : _cg.alpha;
            if (manageRaycasts) _cg.blocksRaycasts = true;
            ApplyIn(0f);
        }

        public void PlayOut(System.Action onHidden = null)
        {
            if (!gameObject.activeInHierarchy)
            {
                gameObject.SetActive(false);
                onHidden?.Invoke();
                return;
            }
            if (_cg == null) Awake();

            if (_state == State.Out)
            {
                _onHidden += onHidden;
                return;
            }
            if (_state == State.Idle) CaptureRest();

            _state = State.Out;
            _t = 0f;
            _startAlpha = _cg.alpha;
            _onHidden = onHidden;
            if (manageRaycasts) _cg.blocksRaycasts = false;
        }

        private void Update()
        {
            if (_state == State.Idle) return;
            float dt = Time.unscaledDeltaTime;

            if (_state == State.In)
            {
                if (_delayLeft > 0f)
                {
                    _delayLeft -= dt;
                    return;
                }
                _t += dt / Mathf.Max(0.01f, inDuration * (1f - _startProgress * 0.7f));
                ApplyIn(_t);
                if (_t >= 1f)
                {
                    _state = State.Idle;
                    RestoreRest();
                }
            }
            else
            {
                _t += dt / Mathf.Max(0.01f, outDuration);
                ApplyOut(_t);
                if (_t >= 1f)
                {
                    _state = State.Idle;
                    var cb = _onHidden;
                    _onHidden = null;
                    RestoreRest();               // объект уже невидим — возвращаем исходное состояние
                    gameObject.SetActive(false);
                    cb?.Invoke();
                }
            }
        }

        private void ApplyIn(float t)
        {
            float k = Mathf.Lerp(_startProgress, 1f, Mathf.Clamp01(t));
            float a = LGEase.OutCubic(Mathf.Clamp01(t * 1.6f));
            _cg.alpha = Mathf.Lerp(_startAlpha, 1f, a);

            if (_rt == null) return;
            float spring = LGEase.OutBack(k, 1.15f);
            if (UsesScale)
                _rt.localScale = _restScale * Mathf.LerpUnclamped(kind == Kind.Pop ? fromScale : 0.985f, 1f, spring);
            if (UsesPosition)
                _rt.anchoredPosition = _restPos + SlideOffset * (1f - LGEase.OutCubic(k));
        }

        private void ApplyOut(float t)
        {
            float k = LGEase.InCubic(Mathf.Clamp01(t));
            _cg.alpha = Mathf.Lerp(_startAlpha, 0f, LGEase.OutCubic(Mathf.Clamp01(t)));
            if (_rt == null) return;
            if (UsesScale)
                _rt.localScale = _restScale * Mathf.Lerp(1f, kind == Kind.Pop ? 0.965f : 0.99f, k);
            if (UsesPosition)
                _rt.anchoredPosition = _restPos + SlideOffset * 0.6f * k;
        }
    }

    /// <summary>
    /// Живой отклик кнопки: при наведении стекло "поднимается" (лёгкий масштаб на пружине,
    /// усиливается неон и блик под курсором), при нажатии — мягко проминается.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGInteractive : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public bool animateScale = true;
        public float hoverScale = 1.035f;
        public float pressScale = 0.955f;

        private LiquidGlassEffect _fx;
        private Selectable _selectable;
        private bool _over, _down;
        private float _h, _hv, _p, _pv;
        private Vector3 _baseScale = Vector3.one;
        private bool _active;

        private void Awake()
        {
            _fx = GetComponent<LiquidGlassEffect>();
            _selectable = GetComponent<Selectable>();
        }

        private void OnEnable()
        {
            _baseScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
        }

        private void OnDisable()
        {
            _over = _down = false;
            _h = _hv = _p = _pv = 0f;
            if (_fx != null) _fx.SetHover(0f);
            if (animateScale && _active) transform.localScale = _baseScale;
            _active = false;
        }

        public void OnPointerEnter(PointerEventData e) { _over = true; Wake(); }
        public void OnPointerExit(PointerEventData e) { _over = false; _down = false; Wake(); }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) { _down = true; Wake(); } }
        public void OnPointerUp(PointerEventData e) { _down = false; Wake(); }

        private void Wake()
        {
            if (!_active && animateScale) _baseScale = transform.localScale;
            _active = true;
        }

        private void Update()
        {
            if (!_active) return;
            float dt = Time.unscaledDeltaTime;
            bool can = _selectable == null || _selectable.IsInteractable();
            float th = can && _over ? 1f : 0f;
            float tp = can && _down ? 1f : 0f;

            _h = LGEase.Spring(_h, th, ref _hv, 3.2f, 0.55f, dt);
            _p = LGEase.Spring(_p, tp, ref _pv, 5.0f, 0.6f, dt);

            if (_fx == null) _fx = GetComponent<LiquidGlassEffect>();
            if (_fx != null) _fx.SetHover(Mathf.Clamp01(_h + _p * 0.4f));

            if (animateScale)
            {
                float s = 1f + (hoverScale - 1f) * _h + (pressScale - 1f) * _p;
                transform.localScale = _baseScale * s;
            }

            bool settled = Mathf.Abs(_h - th) < 0.002f && Mathf.Abs(_hv) < 0.002f
                        && Mathf.Abs(_p - tp) < 0.002f && Mathf.Abs(_pv) < 0.002f;
            if (settled && th == 0f && tp == 0f)
            {
                if (animateScale) transform.localScale = _baseScale;
                if (_fx != null) _fx.SetHover(0f);
                _active = false;
            }
        }
    }
}
