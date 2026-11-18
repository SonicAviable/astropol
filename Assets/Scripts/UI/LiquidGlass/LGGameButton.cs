using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Единый дизайн кнопок игры, стиль «Атлас»: капсула с чернильным телом и тонкой кромкой, подпись моноширинным
    /// шрифтом в исходном регистре, без полос и свечения. Цвет кромки — смысл действия: кремовая — обычное и нейтральное,
    /// морская зелень — успех, коралл — опасное. Недоступная — тусклая. При наведении тело светлеет, кромка ярче.
    /// Вешается сам на кнопки из <see cref="LGBuild.Button"/> (кнопки-полосы с подписью: ширина от 150, высота 28…58 px);
    /// круглые, маленькие и кнопки-значки остаются как были. Если код потом перекрашивает кнопку (например, «удалить?» —
    /// красным), новый цвет становится цветом кромки — дизайн не ломается.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGGameButton : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        /// <summary>Размер среза углов у всех кнопок.</summary>
        public const float Cut = 10f;

        // «Атлас»: тело — чернильное (тёмное, без насыщенности: кромку задаёт цвет действия)
        // «Атлас»: тело — сплошная тёмная сине-стальная капсула (цвет действия даёт кромка и подпись)
        private static readonly Color Plate = new Color(0.133f, 0.196f, 0.310f, 0.97f);
        private static readonly Color PlateLit = new Color(0.180f, 0.255f, 0.385f, 0.98f);
        private static readonly Color PlateQuiet = new Color(0.115f, 0.170f, 0.270f, 0.96f);
        private static readonly Color PlateOff = new Color(0.080f, 0.110f, 0.170f, 0.70f);
        private static readonly Color Ink = new Color(0.925f, 0.898f, 0.827f);
        private static readonly Color InkQuiet = new Color(0.663f, 0.639f, 0.573f);

        /// <summary>Узкая кнопка (от 60 px) тоже в общем стиле — для тесных мест вроде карточек.</summary>
        public bool Compact;

        private Color _accent = new Color(0.475f, 0.816f, 0.749f);
        private Color _set;
        private bool _done, _applied, _over;
        private float _h, _hv;
        private Image _img, _bar;
        private LiquidGlassEffect _fx;
        private Button _btn;
        private Text _label;

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

        private static bool Quiet(Color accent)
        {
            Color.RGBToHSV(accent, out _, out float s, out _);
            return s < 0.3f;
        }

        /// <summary>Цвет кромки: коралл (опасное), морская зелень (успех), золото (жёлтые/оранжевые), остальное — крем.</summary>
        private static Color BarOf(Color accent)
        {
            var cream = new Color(0.925f, 0.898f, 0.827f);
            if (Quiet(accent)) return cream;
            Color.RGBToHSV(accent, out float h, out _, out _);
            if (h < 0.05f || h > 0.93f) return new Color(1f, 0.42f, 0.341f);        // коралл
            if (h > 0.25f && h < 0.52f) return new Color(0.475f, 0.816f, 0.749f);   // морская зелень
            if (h >= 0.05f && h <= 0.17f) return new Color(0.906f, 0.757f, 0.439f); // золото
            return cream;
        }

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
                if (r.width < (Compact ? 60f : 150f) || r.height < (Compact ? 24f : 28f) || r.height > 58f) { enabled = false; return; }
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
            bool quiet = Quiet(_accent);
            _h = LGEase.Spring(_h, on && _over ? 1f : 0f, ref _hv, 4f, 0.7f, Time.unscaledDeltaTime);
            float h = Mathf.Clamp01(_h);
            var fill = !on ? PlateOff : Color.Lerp(quiet ? PlateQuiet : Plate, PlateLit, h);
            if (fill != _img.color) _img.color = fill;
            _set = fill;
            var bc = on ? BarOf(_accent) : new Color(0.45f, 0.44f, 0.40f);
            if (_fx != null) _fx.SetRim(new Color(bc.r, bc.g, bc.b, !on ? 0.10f : Mathf.Lerp(quiet ? 0.14f : 0.40f, 0.6f, h)));
            if (_label != null)
            {
                var lc = !on ? new Color(0.50f, 0.49f, 0.45f, 0.8f) : quiet ? Color.Lerp(InkQuiet, Ink, h) : Color.Lerp(bc, Ink, 0.35f + 0.4f * h);
                if (_label.color != lc) _label.color = lc;
            }
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
            t.fontStyle = FontStyle.Normal;
            if (!t.TryGetComponent<LGButtonLabel>(out _)) t.gameObject.AddComponent<LGButtonLabel>();

            // «Атлас»: капсула (форму и кромку рисует LiquidGlassEffect в роли кнопки), без свечения
            var fx = LG.Apply(gameObject, LiquidGlassEffect.Role.Button, -1f);
            if (fx != null)
            {
                fx.Chamfer = false;
                fx.GlowMultiplier = 0f;
                fx.SpecularMultiplier = 0.2f;
            }
            _fx = fx;
            var sweep = GetComponent<InspSweep>();
            if (sweep != null) Destroy(sweep);
            if (_img != null) _img.color = _set = Plate;
            Tick();
        }
    }
}
