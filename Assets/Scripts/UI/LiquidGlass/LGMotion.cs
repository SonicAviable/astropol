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
    /// Окна (Pop/Fade от 360×220) включаются как голограмма: рамка появляется сразу, содержимое
    /// «печатается» сверху вниз за светящейся линией развёртки, по окну бегут гаснущие строки и короткое мерцание.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGAppear : MonoBehaviour
    {
        public enum Kind { Pop, Fade, SlideLeft, SlideRight, SlideUp, SlideDown }

        public Kind kind = Kind.Pop;
        public float inDuration = 0.38f;
        public float outDuration = 0.2f;
        public float distance = 28f;
        public float fromScale = 0.975f;
        [Tooltip("Задержка появления (для каскада панелей).")]
        public float delay = 0f;
        public bool playOnEnable = true;
        [Tooltip("Блокировать клики во время исчезновения.")]
        public bool manageRaycasts = true;
        [Tooltip("Голографическое включение окна (развёртка сверху вниз).")]
        public bool holoReveal = true;

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
        // Позицию покоя берём в первом кадре анимации: LG.Motion добавляют до того, как
        // вызывающий код расставит элемент, и ранний снимок возвращал его на старое место
        private bool _pendingCapture;
        private System.Action _onHidden;

        // ---- голографическое включение
        private const float HoloDuration = 0.5f;
        private float _holoT = -1f;
        private RectMask2D _holoMask;
        private bool _holoOwnMask;
        private RectTransform _holoFx, _holoLine;
        private RawImage _holoStripes;
        private Image _holoGlow, _holoCore;
        private static Texture2D s_stripes;

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
            EndHolo();
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
                _pendingCapture = true;
                _restCaptured = false;
            }

            _state = State.In;
            _t = 0f;
            _delayLeft = progress > 0f ? 0f : delay;
            _startProgress = progress;
            _startAlpha = progress <= 0f ? 0f : _cg.alpha;
            if (manageRaycasts) _cg.blocksRaycasts = true;
            if (progress <= 0f) StartHolo();
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
            if (_state == State.Idle || _pendingCapture) { CaptureRest(); _pendingCapture = false; }

            EndHolo();
            _state = State.Out;
            _t = 0f;
            _startAlpha = _cg.alpha;
            _onHidden = onHidden;
            if (manageRaycasts) _cg.blocksRaycasts = false;
        }

        private void Update()
        {
            UpdateState();
            // Голограмма — после обычного проявления, чтобы мерцание ложилось поверх прозрачности
            if (_holoT >= 0f) TickHolo(_state == State.In && _delayLeft > 0f ? 0f : Time.unscaledDeltaTime);
        }

        private void UpdateState()
        {
            if (_state == State.Idle) return;
            float dt = Time.unscaledDeltaTime;

            if (_state == State.In)
            {
                if (_pendingCapture) { CaptureRest(); _pendingCapture = false; }
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

        // ==================== Голографическое включение ====================

        private bool WantsHolo()
        {
            if (!holoReveal || _rt == null || (kind != Kind.Pop && kind != Kind.Fade)) return false;
            var r = _rt.rect;
            return r.width >= 360f && r.height >= 220f;
        }

        private void StartHolo()
        {
            EndHolo();
            if (!WantsHolo()) return;

            // Маска раскрытия: содержимое окна (дети) показывается сверху вниз; фон самого окна виден сразу
            _holoMask = GetComponent<RectMask2D>();
            _holoOwnMask = _holoMask == null;
            if (_holoOwnMask)
            {
                _holoMask = gameObject.AddComponent<RectMask2D>();
                _holoMask.softness = new Vector2Int(0, 26);
            }

            _holoFx = LGBuild.Rect(transform, "HoloReveal");
            _holoFx.Stretch();
            var le = _holoFx.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            LG.Ignore(_holoFx.gameObject);

            // Гаснущие строки развёртки по всему окну
            var st = LGBuild.Rect(_holoFx, "Stripes");
            st.Stretch();
            _holoStripes = st.gameObject.AddComponent<RawImage>();
            _holoStripes.texture = StripeTex();
            _holoStripes.raycastTarget = false;

            // Линия развёртки: мягкий ореол и яркая сердцевина
            _holoLine = LGBuild.Rect(_holoFx, "ScanLine");
            _holoLine.anchorMin = new Vector2(0f, 1f);
            _holoLine.anchorMax = new Vector2(1f, 1f);
            _holoLine.pivot = new Vector2(0.5f, 0.5f);
            _holoLine.sizeDelta = new Vector2(0f, 40f);
            _holoGlow = LGBuild.Panel(_holoLine, "Glow", Color.clear);
            _holoGlow.sprite = FactionSelectScreen.RadialSprite();
            _holoGlow.rectTransform.Stretch(-40f, 0f, -40f, 0f);
            _holoCore = LGBuild.Panel(_holoLine, "Core", Color.clear);
            _holoCore.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            _holoCore.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            _holoCore.rectTransform.sizeDelta = new Vector2(0f, 2f);

            _holoT = 0f;
            StellarisClone.Core.SFXManager.Play(StellarisClone.Core.Audio.Sfx.HoloScan);
            TickHolo(0f);
        }

        private void TickHolo(float dt)
        {
            if (_holoFx == null || _rt == null) { EndHolo(); return; }
            _holoT += dt / HoloDuration;
            float t = Mathf.Clamp01(_holoT);
            float h = _rt.rect.height;
            float edge = h * LGEase.OutCubic(t);                      // сколько уже «напечатано» сверху, px

            if (_holoOwnMask && _holoMask != null)
                _holoMask.padding = new Vector4(0f, Mathf.Max(0f, h - edge - 14f), 0f, 0f);

            _holoFx.SetAsLastSibling();                               // поверх содержимого, которое окно достроило после показа
            _holoLine.anchoredPosition = new Vector2(0f, -edge);
            Color c = UIManager.DS.NeonCyan;
            float lineA = 1f - Mathf.SmoothStep(0.75f, 1f, t);
            _holoGlow.color = new Color(c.r, c.g, c.b, 0.35f * lineA);
            _holoCore.color = new Color(Mathf.Lerp(c.r, 1f, 0.5f), Mathf.Lerp(c.g, 1f, 0.5f), Mathf.Lerp(c.b, 1f, 0.5f), 0.9f * lineA);
            _holoStripes.color = new Color(c.r, c.g, c.b, 0.16f * (1f - t));
            _holoStripes.uvRect = new Rect(0f, -Time.unscaledTime * 1.5f, 1f, Mathf.Max(1f, h / 3f));

            // Короткое цифровое мерцание в начале (поверх обычного проявления)
            if (t < 0.3f && _cg != null && Mathf.PerlinNoise(Time.unscaledTime * 38f, 0.37f) < 0.38f)
                _cg.alpha *= 0.55f;

            if (_holoT >= 1f) EndHolo();
        }

        private void EndHolo()
        {
            _holoT = -1f;
            if (_holoFx != null) Destroy(_holoFx.gameObject);
            _holoFx = null;
            if (_holoOwnMask && _holoMask != null) Destroy(_holoMask);
            _holoMask = null;
            _holoOwnMask = false;
        }

        /// <summary>Строки развёртки: одна светлая на три пикселя.</summary>
        private static Texture2D StripeTex()
        {
            if (s_stripes != null) return s_stripes;
            s_stripes = new Texture2D(1, 3, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, name = "HoloStripes", hideFlags = HideFlags.DontSave };
            s_stripes.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 0) });
            s_stripes.Apply(false);
            return s_stripes;
        }

        private void ApplyIn(float t)
        {
            float k = Mathf.Lerp(_startProgress, 1f, Mathf.Clamp01(t));
            float a = LGEase.OutCubic(Mathf.Clamp01(t * 1.6f));
            _cg.alpha = Mathf.Lerp(_startAlpha, 1f, a);

            if (_rt == null || _pendingCapture) return;
            float spring = LGEase.OutBack(k, 0.35f);
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
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        /// <summary>Озвучка: тихий «тик» при наведении, клик при нажатии, отказ — по неактивной кнопке.</summary>
        public bool sounds = true;
        public bool animateScale = true;
        public float hoverScale = 1f;      // без «пружинного» увеличения — кнопка отвечает светом кромки
        /// <summary>Длительность пробега блика по кнопке при наведении, с.</summary>
        public float sweepDuration = 0.55f;
        public float pressScale = 0.985f;

        private LiquidGlassEffect _fx;
        private Selectable _selectable;
        private bool _over, _down;
        private float _h, _hv, _p, _pv;
        private Vector3 _baseScale = Vector3.one;
        private bool _active;
        private float _sweepT = -1f;   // <0 — блик не идёт

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
            if (_fx != null) { _fx.SetHover(0f); _fx.SetSweep(0f); }
            _sweepT = -1f;
            if (animateScale && _active) transform.localScale = _baseScale;
            _active = false;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _over = true; Wake();
            bool can = _selectable == null || _selectable.IsInteractable();
            if (can && _selectable != null) _sweepT = 0f;   // по кнопке пробегает блик
            if (sounds && _selectable != null && can)
                StellarisClone.Core.SFXManager.Play(StellarisClone.Core.Audio.Sfx.UiHover);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (!sounds || _selectable == null || e.button != PointerEventData.InputButton.Left) return;
            if (_selectable.IsInteractable()) StellarisClone.Core.SFXManager.Play(StellarisClone.Core.Audio.Sfx.UiClick);
            else StellarisClone.Core.SFXManager.Play(StellarisClone.Core.Audio.Sfx.UiDenied, 0.7f);
        }
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

            _h = LGEase.Spring(_h, th, ref _hv, 3.2f, 0.95f, dt);
            _p = LGEase.Spring(_p, tp, ref _pv, 5.0f, 0.95f, dt);

            if (_fx == null) _fx = GetComponent<LiquidGlassEffect>();
            if (_fx != null) _fx.SetHover(Mathf.Clamp01(_h + _p * 0.4f));

            if (_sweepT >= 0f)
            {
                _sweepT += dt / Mathf.Max(0.05f, sweepDuration);
                float t = _sweepT >= 1f ? 0f : Mathf.SmoothStep(0f, 1f, _sweepT);
                if (_fx != null) _fx.SetSweep(_sweepT >= 1f ? 0f : Mathf.Max(0.001f, t));
                if (_sweepT >= 1f) _sweepT = -1f;
            }

            if (animateScale)
            {
                float s = 1f + (hoverScale - 1f) * _h + (pressScale - 1f) * _p;
                transform.localScale = _baseScale * s;
            }

            bool settled = Mathf.Abs(_h - th) < 0.002f && Mathf.Abs(_hv) < 0.002f
                        && Mathf.Abs(_p - tp) < 0.002f && Mathf.Abs(_pv) < 0.002f;
            if (settled && th == 0f && tp == 0f && _sweepT < 0f)
            {
                if (animateScale) transform.localScale = _baseScale;
                if (_fx != null) _fx.SetHover(0f);
                _active = false;
            }
        }
    }
}
