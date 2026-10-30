using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Единый дизайн кнопок игры — тот же, что у главной кнопки (<see cref="MenuPrimaryButton"/>): сплошная заливка
    /// со свечением, прямоугольник со скруглением 8 px, подпись капслоком по центру, пробегающий блик.
    /// Гамма одна: главная кнопка — яркая бирюза с тёмной подписью; обычные — глубокая бирюза со светлой подписью
    /// (при наведении светлеют к главной); от неё отличаются только смысловые цвета — опасное (красная) и
    /// успешное (зелёная) действие, тоже глубокие. Нейтральные («Назад», неактивная вкладка) — почти тёмные.
    /// Недоступная — тусклая серая. Без радуги на одном экране.
    /// Вешается сам на кнопки из <see cref="LGBuild.Button"/> (кнопки-полосы с подписью: ширина от 150, высота 28…58 px);
    /// круглые, маленькие и кнопки-значки остаются как были. Если код потом перекрашивает кнопку (например, «удалить?» —
    /// красным), новый цвет становится цветом заливки — дизайн не ломается.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGGameButton : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        private static readonly Color Off = new Color(0.22f, 0.27f, 0.31f, 0.9f);

        private Color _accent = new Color(0.40f, 0.86f, 0.82f);
        private Color _set;
        private bool _done, _applied, _over;
        private float _h, _hv;
        private Image _img;
        private Button _btn;
        private Text _label;
        private InspSweep _sweep;

        /// <summary>Цвет действия — из цвета кромки, а если она нейтральная — из заливки кнопки.</summary>
        public void Init(LGIcon? icon, Color rim, Color tint)
        {
            _accent = AccentOf(rim, tint);
            enabled = true;
        }

        public static Color AccentOf(Color rim, Color tint)
        {
            Color.RGBToHSV(rim, out float h, out float s, out float v);
            if (s < 0.25f || v < 0.3f) Color.RGBToHSV(tint, out h, out s, out v);
            if (s < 0.2f || v < 0.08f) return new Color(0.62f, 0.78f, 0.86f);   // нейтральная кнопка — холодный серо-голубой
            return Color.HSVToRGB(h, Mathf.Clamp01(s * 0.85f), 1f);
        }

        /// <summary>
        /// Оттенок кнопки: красный (опасное) и зелёный (успех) сохраняются, всё остальное — бирюза главной кнопки.
        /// </summary>
        private static float HueOf(Color accent)
        {
            Color.RGBToHSV(accent, out float h, out float s, out _);
            if (s < 0.3f) return Teal;
            if (h < 0.05f || h > 0.93f) return 0.015f;          // красный
            if (h > 0.25f && h < 0.42f) return 0.38f;           // зелёный
            return Teal;
        }

        private const float Teal = 0.475f;

        /// <summary>Нейтральная кнопка («Назад», неактивная вкладка) — тише обычной: почти тёмная заливка.</summary>
        private static bool Quiet(Color accent)
        {
            Color.RGBToHSV(accent, out _, out float s, out _);
            return s < 0.3f;
        }

        /// <summary>Заливка обычной кнопки: тот же оттенок, что у главной, но глубокий.</summary>
        private static Color FillOf(Color accent, float hover)
        {
            float h = HueOf(accent);
            if (Quiet(accent)) return Color.Lerp(Color.HSVToRGB(h, 0.45f, 0.17f), Color.HSVToRGB(h, 0.55f, 0.32f), hover);
            var deep = Color.HSVToRGB(h, 0.62f, 0.30f);
            var lit = Color.HSVToRGB(h, 0.55f, 0.50f);
            return Color.Lerp(deep, lit, hover);
        }

        private static Color InkOf(Color accent) => Quiet(accent) ? new Color(0.78f, 0.86f, 0.90f) : Color.HSVToRGB(HueOf(accent), 0.22f, 1f);

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => _over = true;
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => _over = false;

        private void OnRectTransformDimensionsChange()
        {
            if (!_done) enabled = true;
        }

        private void LateUpdate()
        {
            if (!_done)
            {
                var r = ((RectTransform)transform).rect;
                if (r.width < 1f) return;                                   // размер ещё не выставлен
                _done = true;
                if (r.width < 150f || r.height < 28f || r.height > 58f) { enabled = false; return; }
                Apply(r);
                if (!_applied) { enabled = false; return; }
            }
            Tick();
        }

        private void Tick()
        {
            if (_img == null) return;
            // Код перекрасил кнопку — новый цвет означает действие (опасное, успешное, нейтральное)
            if (_img.color != _set) _accent = AccentOf(Color.clear, _img.color);

            bool on = _btn == null || _btn.interactable;
            _h = LGEase.Spring(_h, on && _over ? 1f : 0f, ref _hv, 4f, 0.7f, Time.unscaledDeltaTime);
            var fill = on ? FillOf(_accent, Mathf.Clamp01(_h)) : Off;
            if (fill != _img.color) _img.color = fill;
            _set = fill;
            if (_label != null)
            {
                var lc = on ? InkOf(_accent) : new Color(0.62f, 0.68f, 0.72f, 0.8f);
                if (_label.color != lc) _label.color = lc;
            }
            if (_sweep != null && _sweep.enabled != on) _sweep.enabled = on;
        }

        private void Apply(Rect r)
        {
            // Подпись — та, что поставил LGBuild (или вызывающий код); без подписи кнопка-значок остаётся как есть
            var old = GetComponentInChildren<Text>(true);
            if (old == null || string.IsNullOrEmpty(old.text)) return;
            _applied = true;
            int size = Mathf.Max(11, old.fontSize);
            _btn = GetComponent<Button>();
            _img = GetComponent<Image>();

            // Подпись сохраняем (её ссылку держит вызывающий код и меняет текст) — переносим прямо на кнопку,
            // остальное (старый значок, обёртка «значок + подпись») убираем
            old.transform.SetParent(transform, false);
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c == old.transform) continue;
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }

            const float textLeft = 16f;
            var t = old;
            _label = t;
            t.transform.SetAsLastSibling();
            t.alignment = TextAnchor.MiddleCenter;
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            t.rectTransform.localRotation = Quaternion.identity;
            t.rectTransform.localScale = Vector3.one;
            t.rectTransform.Stretch(textLeft, 0, textLeft, 0);
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 9; t.resizeTextMaxSize = size;
            t.fontStyle = FontStyle.Bold;
            if (!t.TryGetComponent<LGButtonLabel>(out _)) t.gameObject.AddComponent<LGButtonLabel>();

            // Та же заливка, что у главной кнопки: сплошная, светящаяся, скругление 8 px
            var fx = LG.Apply(gameObject, LiquidGlassEffect.Role.Fill, 8f);
            if (fx != null) fx.Radius = 8f;
            _sweep = InspSweep.Add(gameObject, 6f, 0.14f);
            _set = _img != null ? _img.color : Color.clear;
            if (_img != null) _img.color = _set = FillOf(_accent, 0f);
            Tick();
        }
    }
}
