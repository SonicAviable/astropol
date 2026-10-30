using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Единый дизайн кнопок игры — как у главной кнопки (<see cref="MenuPrimaryButton"/>): сплошная светящаяся заливка
    /// цвета действия, тёмная подпись капслоком по центру, прямоугольник со скруглением 8 px, пробегающий блик;
    /// при наведении кнопка светлеет. Цвет говорит о действии: бирюзовая — обычное, золотая — важное, красная — опасное,
    /// серо-голубая — нейтральное («Назад», «Управление»); недоступная — тусклая серая. Главная кнопка экрана
    /// ярче остальных и дышит ореолом.
    /// Вешается сам на кнопки из <see cref="LGBuild.Button"/> (кнопки-полосы с подписью: ширина от 150, высота 28…58 px);
    /// круглые, маленькие и кнопки-значки остаются как были. Если код потом перекрашивает кнопку (например, «удалить?» —
    /// красным), новый цвет становится цветом заливки — дизайн не ломается.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGGameButton : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        private static readonly Color Ink = new Color(0.02f, 0.07f, 0.09f);
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

        /// <summary>Заливка обычной кнопки: цвет действия чуть глубже, чем у главной.</summary>
        private static Color FillOf(Color accent) => Color.Lerp(accent, new Color(0.05f, 0.12f, 0.16f), 0.22f);

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
            var fill = on ? Color.Lerp(FillOf(_accent), Color.white, 0.22f * Mathf.Clamp01(_h)) : Off;
            if (fill != _img.color) _img.color = fill;
            _set = fill;
            if (_label != null)
            {
                var lc = on ? Ink : new Color(0.62f, 0.68f, 0.72f, 0.8f);
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
            var sh = t.GetComponent<Shadow>();
            if (sh != null) sh.enabled = false;   // тёмная подпись на светлой заливке — без тени
            if (!t.TryGetComponent<LGButtonLabel>(out _)) t.gameObject.AddComponent<LGButtonLabel>();

            // Та же заливка, что у главной кнопки: сплошная, светящаяся, скругление 8 px
            var fx = LG.Apply(gameObject, LiquidGlassEffect.Role.Fill, 8f);
            if (fx != null) fx.Radius = 8f;
            _sweep = InspSweep.Add(gameObject, 6f, 0.25f);
            _set = _img != null ? _img.color : Color.clear;
            if (_img != null) _img.color = _set = FillOf(_accent);
            Tick();
        }
    }
}
