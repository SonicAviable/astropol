using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Единый дизайн кнопок игры, стиль «Астролябия» (см. <see cref="LGAstrolabe"/>): латунная рамка, эмалевое тело,
    /// подпись антиквой капслоком, камни по бокам — цвет смысла действия (из цвета кромки, а если она нейтральная —
    /// из заливки кнопки). Вешается сам на кнопки из <see cref="LGBuild.Button"/> (кнопки-полосы с подписью: ширина от 150,
    /// высота 28…58 px); круглые, маленькие и кнопки-значки остаются как были. Если код потом перекрашивает кнопку
    /// (например, «удалить?» — красным), новый цвет становится цветом камней — дизайн не ломается.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGGameButton : MonoBehaviour
    {
        /// <summary>Размер среза углов у всех кнопок.</summary>
        public const float Cut = 10f;

        /// <summary>Узкая кнопка (от 60 px) тоже в общем стиле — для тесных мест вроде карточек.</summary>
        public bool Compact;

        private Color _accent = new Color(0.475f, 0.816f, 0.749f);
        private Color _tint;
        private bool _done;

        /// <summary>Цвет действия — из цвета кромки, а если она нейтральная — из заливки кнопки.</summary>
        public void Init(LGIcon? icon, Color rim, Color tint)
        {
            _accent = AccentOf(rim, tint);
            _tint = tint;
            enabled = true;
        }

        public static Color AccentOf(Color rim, Color tint)
        {
            Color.RGBToHSV(rim, out float h, out float s, out float v);
            if (s < 0.25f || v < 0.3f) Color.RGBToHSV(tint, out h, out s, out v);
            if (s < 0.2f || v < 0.08f) return new Color(0.62f, 0.78f, 0.86f);   // нейтральная кнопка — холодный серо-голубой
            return Color.HSVToRGB(h, Mathf.Clamp01(s * 0.85f), 1f);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!_done) enabled = true;
        }

        private void LateUpdate()
        {
            if (_done) { enabled = false; return; }
            var r = ((RectTransform)transform).rect;
            if (r.width < 1f) return;                                   // размер ещё не выставлен
            _done = true;
            enabled = false;
            if (r.width < (Compact ? 60f : 150f) || r.height < (Compact ? 24f : 28f) || r.height > 58f) return;
            Apply();
        }

        private void Apply()
        {
            // Подпись — та, что поставил LGBuild (или вызывающий код); без подписи кнопка-значок остаётся как есть
            var old = GetComponentInChildren<Text>(true);
            if (old == null || string.IsNullOrEmpty(old.text)) return;
            int size = Mathf.Max(11, old.fontSize);
            var img = GetComponent<Image>();
            if (img == null) return;
            // Код успел перекрасить кнопку до оформления — цвет действия берём из него
            if (img.color != _tint) _accent = AccentOf(Color.clear, img.color);

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

            float pad = Compact ? 14f : 22f;   // место под камни по бокам
            var t = old;
            t.alignment = TextAnchor.MiddleCenter;
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            t.rectTransform.localRotation = Quaternion.identity;
            t.rectTransform.localScale = Vector3.one;
            t.rectTransform.offsetMin = new Vector2(pad, 1f);
            t.rectTransform.offsetMax = new Vector2(-pad, 0f);
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 9; t.resizeTextMaxSize = size;

            var a = LGAstrolabe.Attach(gameObject, img, t);
            if (a == null) return;
            a.Gem = LGAstrolabe.GemOf(_accent);
            a.WatchTint = true;
        }
    }
}
