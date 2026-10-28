using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Оформление обычной кнопки действия в стиле игры (как «Укрепить → крепость» в окне звёздной базы): тёмное
    /// стекло, светящаяся планка цвета действия у левого края, значок в своей ячейке, подпись по центру
    /// капслоком с разрядкой, кромка цвета действия с мягким свечением и изредка пробегающий блик.
    /// Вешается сам на кнопки из <see cref="LGBuild.Button"/>; применяется, когда кнопка получила размер,
    /// и только к кнопкам-полосам с подписью (ширина от 150, высота 28…58 px) — круглые, маленькие и кнопки-значки
    /// остаются как были.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LGGameButton : MonoBehaviour
    {
        private LGIcon? _icon;
        private Color _accent = new Color(0.40f, 0.86f, 0.82f);
        private bool _done;

        /// <summary>Цвет действия — из цвета кромки, а если она нейтральная — из заливки кнопки.</summary>
        public void Init(LGIcon? icon, Color rim, Color tint)
        {
            _icon = icon;
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
            if (_done) { enabled = false; return; }
            var r = ((RectTransform)transform).rect;
            if (r.width < 1f) return;                                   // размер ещё не выставлен
            if (r.width < 150f || r.height < 28f || r.height > 58f) { enabled = false; return; }
            Apply(r);
            _done = true;
            enabled = false;
        }

        private void Apply(Rect r)
        {
            // Подпись — та, что поставил LGBuild (или вызывающий код); без подписи кнопка-значок остаётся как есть
            var old = GetComponentInChildren<Text>(true);
            if (old == null || string.IsNullOrEmpty(old.text)) return;
            int size = Mathf.Max(11, old.fontSize);
            var btn = GetComponent<Button>();
            bool on = btn == null || btn.interactable;
            Color a = on ? _accent : new Color(0.45f, 0.52f, 0.58f);

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

            float h = r.height;
            var bar = LGBuild.Panel(transform, "Accent", new Color(a.r, a.g, a.b, on ? 0.95f : 0.4f));
            bar.raycastTarget = false;
            bar.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(3f, 0f), new Vector2(2.5f, h * 0.55f));
            LG.Line(bar.gameObject);

            float textLeft = 16f;
            if (_icon.HasValue)
            {
                float ts = Mathf.Clamp(h - 12f, 18f, 30f);
                var tile = LGBuild.Panel(transform, "IconTile", new Color(a.r * 0.16f, a.g * 0.16f, a.b * 0.16f, 0.95f));
                tile.raycastTarget = false;
                tile.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(ts, ts));
                LG.Chip(tile.gameObject, new Color(a.r, a.g, a.b, on ? 0.5f : 0.2f));
                var ic = LGIcons.Create(tile.transform, _icon.Value, ts * 0.55f, a);
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ts * 0.55f, ts * 0.55f));
                textLeft = 12f + ts + 12f;
            }

            var t = old;
            t.transform.SetAsLastSibling();
            t.alignment = TextAnchor.MiddleCenter;
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            t.rectTransform.localRotation = Quaternion.identity;
            t.rectTransform.localScale = Vector3.one;
            // По центру кнопки: справа такое же поле, как под значком слева (у узких кнопок — только отступ)
            t.rectTransform.Stretch(textLeft, 0, r.width >= 260f ? textLeft : 10f, 0);
            if (!on) t.color = UIManager.DS.TextMuted;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 9; t.resizeTextMaxSize = size;
            if (!t.TryGetComponent<LGButtonLabel>(out _)) t.gameObject.AddComponent<LGButtonLabel>();

            // Кромка цвета действия с мягким свечением, как у кнопки укрепления
            var fx = GetComponent<LiquidGlassEffect>();
            if (fx != null) fx.SetRim(new Color(a.r, a.g, a.b, on ? 0.65f : 0.2f));
            if (on) InspSweep.Add(gameObject, 6f, 0.12f);
        }
    }
}
