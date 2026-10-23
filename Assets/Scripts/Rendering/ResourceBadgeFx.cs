using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Живая ячейка ресурса в верхней панели:
    ///   • число не скачет, а докручивается до нового значения;
    ///   • при заметном изменении (приход месяца, трата) над числом всплывает «+16» / «−120» и тает,
    ///     кольцо значка вспыхивает зелёным или красным, значок слегка «подпрыгивает»;
    ///   • свечение за значком и черта под ячейкой медленно «дышат» (у каждой ячейки своя фаза);
    ///   • при наведении ячейка подсвечивается, значок чуть увеличивается.
    /// Дыхание и наведение — через прозрачность и масштаб (без перестройки геометрии канваса),
    /// цвет меняется только на время вспышки.
    /// </summary>
    public sealed class ResourceBadgeFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Halo, Ring, Icon, Underline;
        public Text Value;
        public Color Accent;

        private static readonly Color Up = new Color(0.40f, 1f, 0.62f);
        private static readonly Color Down = new Color(1f, 0.42f, 0.40f);
        private const float TweenTime = 0.6f, FloatTime = 1.3f, HaloMax = 0.62f;

        private float _phase, _hover, _hoverTarget, _pulse;
        private Color _pulseCol = Up;
        private bool _tinted;
        private Color _ringBase, _haloBase;

        private bool _numeric;
        private float _shown = float.NaN, _from, _target, _tweenT = 1f;
        private string _suffix = "";
        private float _lastSignal = float.NaN;

        private sealed class Floater { public Text Text; public RectTransform Rt; public float T; public Vector2 Start; }
        private readonly List<Floater> _floaters = new List<Floater>();

        private void Start()
        {
            _phase = Random.value * 6.2832f;
            if (Ring != null) _ringBase = Ring.color;
            // Базовая прозрачность — максимальная; дальше она только приглушается множителем (≤ 1)
            if (Halo != null) { _haloBase = new Color(Accent.r, Accent.g, Accent.b, HaloMax); Halo.color = _haloBase; }
            if (Underline != null) Underline.color = new Color(Accent.r, Accent.g, Accent.b, 1f);
        }

        // ================================================================ Данные

        /// <summary>Запас ресурса: число докручивается, заметное изменение — вспышка и всплывающая разница.</summary>
        public void SetNumeric(float value, string suffix)
        {
            _numeric = true;
            bool suffixChanged = suffix != _suffix;
            _suffix = suffix ?? "";
            if (float.IsNaN(_shown))
            {
                _shown = _from = _target = value;
                Render();
                return;
            }
            float delta = value - _target;
            if (Mathf.Abs(delta) < 0.5f)
            {
                if (suffixChanged) Render();
                return;
            }
            _from = _shown;
            _target = value;
            _tweenT = 0f;
            if (Mathf.Abs(delta) >= 1f)
            {
                Pulse(delta > 0f);
                SpawnFloater(delta);
            }
        }

        /// <summary>Готовый текст (наука, стабильность): вспышка, когда показатель заметно изменился.</summary>
        public void SetText(string text, float signal)
        {
            _numeric = false;
            if (Value != null) Value.text = text;
            if (!float.IsNaN(_lastSignal) && Mathf.Abs(signal - _lastSignal) >= 1f) Pulse(signal > _lastSignal);
            _lastSignal = signal;
        }

        private void Render()
        {
            if (Value != null) Value.text = Mathf.RoundToInt(_shown).ToString() + _suffix;
        }

        private void Pulse(bool up)
        {
            _pulse = 1f;
            _pulseCol = up ? Up : Down;
        }

        private void SpawnFloater(float delta)
        {
            if (Value == null) return;
            var go = new GameObject("Delta");
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(90, 18);
            var start = new Vector2(58f, 6f);
            rt.anchoredPosition = start;
            var t = go.AddComponent<Text>();
            t.font = GameFont.Bold;
            t.fontSize = 13;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.color = delta > 0f ? Up : Down;
            t.text = (delta > 0f ? "+" : "−") + Mathf.RoundToInt(Mathf.Abs(delta));
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.7f);
            sh.effectDistance = new Vector2(0f, -1f);
            _floaters.Add(new Floater { Text = t, Rt = rt, T = 0f, Start = start });
        }

        // ================================================================ Анимация

        public void OnPointerEnter(PointerEventData e) => _hoverTarget = 1f;
        public void OnPointerExit(PointerEventData e) => _hoverTarget = 0f;

        private void OnDisable()
        {
            _hover = _hoverTarget = 0f;
            foreach (var f in _floaters) if (f.Text != null) Destroy(f.Text.gameObject);
            _floaters.Clear();
        }

        private static float OutCubic(float x) => 1f - Mathf.Pow(1f - Mathf.Clamp01(x), 3f);
        private static float OutBack(float x)
        {
            x = Mathf.Clamp01(x);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float time = Time.unscaledTime;

            if (_numeric && _tweenT < 1f)
            {
                _tweenT = Mathf.Min(1f, _tweenT + dt / TweenTime);
                _shown = Mathf.Lerp(_from, _target, OutCubic(_tweenT));
                Render();
            }

            _hover = Mathf.MoveTowards(_hover, _hoverTarget, dt * 6f);
            _pulse = Mathf.MoveTowards(_pulse, 0f, dt * 1.4f);
            float breathe = 0.5f + 0.5f * Mathf.Sin(time * 1.2f + _phase);
            float pop = _pulse > 0f ? Mathf.Sin(_pulse * Mathf.PI) : 0f;   // 0 → 1 → 0 за вспышку

            if (Halo != null)
            {
                float a = 0.15f + 0.08f * breathe + 0.17f * _hover + 0.30f * _pulse;   // желаемая прозрачность свечения
                Halo.canvasRenderer.SetAlpha(Mathf.Clamp01(a / HaloMax));
                Halo.rectTransform.localScale = Vector3.one * (1f + 0.06f * breathe + 0.10f * _hover + 0.30f * pop);
            }
            if (Underline != null) Underline.canvasRenderer.SetAlpha(Mathf.Clamp01(0.45f + 0.20f * breathe + 0.25f * _hover + 0.30f * _pulse));
            if (Icon != null) Icon.rectTransform.localScale = Vector3.one * (1f + 0.10f * _hover + 0.22f * pop);

            // Цвет — только пока идёт вспышка
            if (_pulse > 0.001f)
            {
                _tinted = true;
                if (Ring != null) Ring.color = Color.Lerp(_ringBase, new Color(_pulseCol.r, _pulseCol.g, _pulseCol.b, 1f), _pulse);
                if (Halo != null) Halo.color = Color.Lerp(_haloBase, new Color(_pulseCol.r, _pulseCol.g, _pulseCol.b, HaloMax), _pulse);
            }
            else if (_tinted)
            {
                _tinted = false;
                if (Ring != null) Ring.color = _ringBase;
                if (Halo != null) Halo.color = _haloBase;
            }

            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                var f = _floaters[i];
                f.T += dt / FloatTime;
                if (f.T >= 1f || f.Text == null)
                {
                    if (f.Text != null) Destroy(f.Text.gameObject);
                    _floaters.RemoveAt(i);
                    continue;
                }
                f.Rt.anchoredPosition = f.Start + new Vector2(0f, 22f * OutCubic(f.T));
                f.Text.canvasRenderer.SetAlpha(f.T < 0.35f ? 1f : 1f - (f.T - 0.35f) / 0.65f);
                f.Rt.localScale = Vector3.one * (0.8f + 0.2f * OutBack(Mathf.Min(1f, f.T * 4f)));
            }
        }
    }
}
