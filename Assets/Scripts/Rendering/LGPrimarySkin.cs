using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Главная кнопка «Атласа» поверх старой кнопки, которую код красит и подписывает сам: доступная — кремовая капсула
    /// с тёмной моноширинной подписью, недоступная — тёмная сталь с приглушённой подписью. Подпись приводится к обычному
    /// регистру, служебный значок в начале («▶», «✕»…) убирается.
    /// </summary>
    public sealed class LGPrimarySkin : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private static readonly Color Cream = new Color(0.925f, 0.898f, 0.827f, 1f);
        private static readonly Color CreamLit = new Color(1f, 0.973f, 0.902f, 1f);
        private static readonly Color Off = new Color(0.133f, 0.196f, 0.310f, 0.85f);
        private static readonly Color InkText = new Color(0.086f, 0.125f, 0.184f);
        private static readonly Color Muted = new Color(0.663f, 0.639f, 0.573f);
        private Image _img;
        private Button _btn;
        private Text _label;
        private string _seen;
        private bool _over;
        private float _h, _v;

        public static LGPrimarySkin Add(GameObject go, Text label)
        {
            var s = (go.TryGetComponent<LGPrimarySkin>(out var existing) ? existing : go.AddComponent<LGPrimarySkin>());
            s._img = go.GetComponent<Image>();
            s._btn = go.GetComponent<Button>();
            s._label = label;
            var fx = LG.Fill(go);
            if (fx != null) { fx.Radius = 999f; fx.Chamfer = false; fx.GlowMultiplier = 0f; fx.SpecularMultiplier = 0f; }
            var sweep = go.GetComponent<InspSweep>();
            if (sweep != null) Destroy(sweep);
            if (label != null)
            {
                label.font = GameFont.MonoMedium;
                label.fontStyle = FontStyle.Normal;
                label.fontSize = 12;
                label.Track(0.4f);
            }
            return s;
        }

        public void OnPointerEnter(PointerEventData e) => _over = true;
        public void OnPointerExit(PointerEventData e) => _over = false;

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.TrimStart();
            if (s.Length > 1 && !char.IsLetterOrDigit(s[0]) && s[0] != '(' && s[0] != '<') s = s.Substring(1).TrimStart();
            return LGBuild.Sentence(s);
        }

        private void LateUpdate()
        {
            bool on = _btn == null || _btn.interactable;
            _h = LGEase.Spring(_h, on && _over ? 1f : 0f, ref _v, 4f, 0.8f, Time.unscaledDeltaTime);
            if (_img != null)
            {
                var c = on ? Color.Lerp(Cream, CreamLit, Mathf.Clamp01(_h)) : Off;
                if (_img.color != c) _img.color = c;
            }
            if (_label == null) return;
            if (_label.text != _seen) { _label.text = Clean(_label.text); _seen = _label.text; }
            var lc = on ? InkText : Muted;
            if (_label.color != lc) _label.color = lc;
        }
    }
}
