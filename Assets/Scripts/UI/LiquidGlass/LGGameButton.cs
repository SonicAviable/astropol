using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Единый дизайн кнопок игры — «приборная панель»: плотная тёмно-синяя плашка со срезанными углами
    /// (слева сверху и справа снизу), полоса цвета действия по левому краю, светлая подпись капслоком по центру,
    /// без свечения. Цвет полосы — смысл действия: синяя — обычное, зелёная — успех, красная — опасное,
    /// серая — нейтральное («Назад», неактивная вкладка). Недоступная — тусклая. При наведении плашка светлеет,
    /// полоса расширяется. Главная кнопка экрана (<see cref="MenuPrimaryButton"/>) той же формы, но сплошь бирюзовая.
    /// Вешается сам на кнопки из <see cref="LGBuild.Button"/> (кнопки-полосы с подписью: ширина от 150, высота 28…58 px);
    /// круглые, маленькие и кнопки-значки остаются как были. Если код потом перекрашивает кнопку (например, «удалить?» —
    /// красным), новый цвет становится цветом полосы — дизайн не ломается.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGGameButton : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        /// <summary>Размер среза углов у всех кнопок.</summary>
        public const float Cut = 10f;

        private static readonly Color Plate = new Color(0.086f, 0.141f, 0.235f, 0.97f);      // #16243C
        private static readonly Color PlateLit = new Color(0.13f, 0.21f, 0.34f, 0.97f);
        private static readonly Color PlateQuiet = new Color(0.059f, 0.094f, 0.157f, 0.95f); // #0F1828
        private static readonly Color PlateOff = new Color(0.05f, 0.07f, 0.10f, 0.85f);
        private static readonly Color Ink = new Color(0.84f, 0.89f, 1f);
        private static readonly Color InkQuiet = new Color(0.62f, 0.70f, 0.75f);

        /// <summary>Узкая кнопка (от 60 px) тоже в общем стиле — для тесных мест вроде карточек.</summary>
        public bool Compact;

        private Color _accent = new Color(0.40f, 0.86f, 0.82f);
        private Color _set;
        private bool _done, _applied, _over;
        private float _h, _hv;
        private Image _img, _bar;
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

        /// <summary>Цвет полосы: красный (опасное), зелёный (успех), серый (нейтральное), остальное — синий.</summary>
        private static Color BarOf(Color accent)
        {
            if (Quiet(accent)) return new Color(0.23f, 0.29f, 0.37f);                // #3A4A5E
            Color.RGBToHSV(accent, out float h, out _, out _);
            if (h < 0.05f || h > 0.93f) return new Color(1f, 0.46f, 0.39f);         // #FF7564
            if (h > 0.25f && h < 0.42f) return new Color(0.31f, 0.85f, 0.54f);      // #4FD98A
            return new Color(0.43f, 0.61f, 1f);                                      // #6E9BFF
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
            if (_bar != null)
            {
                var bc = on ? BarOf(_accent) : new Color(0.2f, 0.24f, 0.28f);
                if (_bar.color != bc) _bar.color = bc;
                _bar.rectTransform.sizeDelta = new Vector2(3f + 3f * h, -Cut);
            }
            if (_label != null)
            {
                var lc = !on ? new Color(0.45f, 0.52f, 0.56f, 0.8f) : quiet ? Color.Lerp(InkQuiet, Ink, h) : Ink;
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

            // Полоса цвета действия по левому краю (ниже среза верхнего левого угла)
            _bar = LGBuild.Panel(transform, "Bar", BarOf(_accent));
            var br = _bar.rectTransform;
            br.anchorMin = new Vector2(0, 0);
            br.anchorMax = new Vector2(0, 1);
            br.pivot = new Vector2(0, 0);
            br.anchoredPosition = Vector2.zero;
            br.sizeDelta = new Vector2(3f, -Cut);
            _bar.raycastTarget = false;
            LG.Ignore(_bar.gameObject);

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

            // Плотная плашка со срезанными углами, без свечения
            var fx = LG.Apply(gameObject, LiquidGlassEffect.Role.Fill, Cut);
            if (fx != null)
            {
                fx.Radius = Cut;
                fx.Chamfer = true;
                fx.GlowMultiplier = 0f;
                fx.SpecularMultiplier = 0.2f;
            }
            var sweep = GetComponent<InspSweep>();
            if (sweep != null) Destroy(sweep);
            if (_img != null) _img.color = _set = Plate;
            Tick();
        }
    }
}
