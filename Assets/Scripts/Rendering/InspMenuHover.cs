using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace StellarisClone.Rendering
{
    /// <summary>Отклик строки меню: подпись светлеет, значок становится золотым, содержимое сдвигается вправо.</summary>
    public sealed class InspMenuHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private RectTransform _inner;
        private Text _label;
        private Image _icon, _chev;
        private bool _over, _open;
        private float _h, _v;
        private static readonly Color Cream = new Color(0.925f, 0.898f, 0.827f);
        private static readonly Color Muted = new Color(0.663f, 0.639f, 0.573f);
        private static readonly Color Gold = new Color(0.906f, 0.757f, 0.439f);

        public void Init(RectTransform inner, Text label, Image icon, Image chev, bool open)
        {
            _inner = inner; _label = label; _icon = icon; _chev = chev; _open = open;
        }

        public void OnPointerEnter(PointerEventData e) { _over = true; enabled = true; }
        public void OnPointerExit(PointerEventData e) { _over = false; enabled = true; }

        private void Update()
        {
            float target = _over ? 1f : 0f;
            _h = LGEase.Spring(_h, target, ref _v, 5f, 0.85f, Time.unscaledDeltaTime);
            float h = Mathf.Clamp01(_h);
            if (_inner != null) { _inner.offsetMin = new Vector2(5f * h, 0f); _inner.offsetMax = new Vector2(5f * h, 0f); }
            if (_label != null) _label.color = Color.Lerp(Cream, Color.white, h);
            if (_icon != null) _icon.color = Color.Lerp(_open ? Gold : Muted, Gold, h);
            if (_chev != null) _chev.color = Color.Lerp(Muted, Cream, h);
            if (Mathf.Abs(_h - target) < 0.002f && Mathf.Abs(_v) < 0.002f) enabled = false;
        }
    }
}
