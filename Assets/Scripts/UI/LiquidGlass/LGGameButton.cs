using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Единый дизайн кнопок игры. Форма у всех одна — прямоугольник со скруглением 8 px, подпись по центру капслоком.
    ///   • Главная (<see cref="MenuPrimaryButton"/>) — сплошная светящаяся заливка, тёмная подпись: одно главное действие экрана.
    ///   • Обычная (этот компонент) — тёмная заливка, кромка и подпись цвета действия; опасные действия — красные.
    ///   • Недоступная — серая, без блика.
    /// Вешается сам на кнопки из <see cref="LGBuild.Button"/> (кнопки-полосы с подписью: ширина от 150, высота 28…58 px);
    /// круглые, маленькие и кнопки-значки остаются как были. Если код потом перекрашивает кнопку (например, «удалить?» —
    /// красным), новый цвет становится цветом кромки и подписи, а заливка остаётся тёмной — дизайн не ломается.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGGameButton : MonoBehaviour
    {
        /// <summary>Тёмная заливка обычной кнопки — одна на всю игру.</summary>
        public static readonly Color Body = new Color(0.035f, 0.07f, 0.11f, 0.96f);
        private static readonly Color BodyOff = new Color(0.03f, 0.045f, 0.06f, 0.85f);
        private static readonly Color Off = new Color(0.45f, 0.52f, 0.58f);

        private Color _accent = new Color(0.40f, 0.86f, 0.82f);
        private bool _done, _applied, _wasOn;
        private Image _img;
        private Button _btn;
        private Text _label;
        private LiquidGlassEffect _fx;
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
            if (s < 0.2f || v < 0.08f) return new Color(0.62f, 0.80f, 0.86f);   // нейтральная кнопка — холодный серо-голубой
            return Color.HSVToRGB(h, Mathf.Clamp01(s * 0.85f), 1f);
        }

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
            Watch();
        }

        /// <summary>Код перекрасил кнопку или выключил её — пересобрать вид в том же дизайне.</summary>
        private void Watch()
        {
            if (_img == null) return;
            bool on = _btn == null || _btn.interactable;
            var want = on ? Body : BodyOff;
            if (_img.color != want)
            {
                // Новый цвет из кода — это смысл действия (опасное, успешное, нейтральное): он идёт в кромку и подпись
                _accent = AccentOf(Color.clear, _img.color);
                _img.color = want;
                Restyle(on);
            }
            else if (on != _wasOn) Restyle(on);
        }

        private void Restyle(bool on)
        {
            _wasOn = on;
            Color a = on ? _accent : Off;
            if (_fx != null) _fx.SetRim(new Color(a.r, a.g, a.b, on ? 0.7f : 0.2f));
            if (_label != null) _label.color = on ? Color.Lerp(a, Color.white, 0.55f) : UIManager.DS.TextMuted;
            if (_sweep != null) _sweep.enabled = on;
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
            _fx = GetComponent<LiquidGlassEffect>();
            bool on = _btn == null || _btn.interactable;

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
            if (!t.TryGetComponent<LGButtonLabel>(out _)) t.gameObject.AddComponent<LGButtonLabel>();

            if (_fx != null)
            {
                _fx.Radius = 8f;               // та же форма, что у главной кнопки
                _fx.FillMultiplier = 1.6f;     // тёмная заливка читается как плотная, а не прозрачная
            }
            _sweep = InspSweep.Add(gameObject, 6f, 0.12f);
            if (_img != null) _img.color = on ? Body : BodyOff;
            Restyle(on);
        }
    }
}
