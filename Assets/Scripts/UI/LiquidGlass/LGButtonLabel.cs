using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Подпись кнопки в общем стиле игры (как «Предыдущая колония» / «Готово»): заглавные буквы, жирный шрифт,
    /// разрядка, кегль не меньше 11 — длинная подпись при нехватке места ужимается, а не вылезает.
    /// Текст можно менять из кода как угодно — заглавными он становится сам (rich-text теги не трогаются).
    /// Вешается автоматически на подписи простых кнопок (см. <see cref="Apply"/>), руками добавлять не нужно.
    /// </summary>
    [RequireComponent(typeof(Text))]
    [DisallowMultipleComponent]
    public sealed class LGButtonLabel : MonoBehaviour
    {
        private const float Tracking = 1.3f;
        private const int MinSize = 11;

        private Text _text;
        private string _last;
        private bool _styled;

        private static readonly List<Text> s_Texts = new List<Text>(8);
        private static readonly StringBuilder s_Sb = new StringBuilder(64);

        /// <summary>
        /// Оформить подписи кнопки, если это простая кнопка действия: не выше 60 px, не больше трёх коротких подписей.
        /// Строки списков и плитки с описаниями (много текста) не трогаются.
        /// </summary>
        public static void Apply(Component button)
        {
            if (button == null) return;
            var rt = button.transform as RectTransform;
            if (rt == null) return;
            Rect r = rt.rect;
            if (r.height > 60f || r.height < 16f) return;
            var sel = button.GetComponent<Selectable>();
            if (sel == null) return;

            s_Texts.Clear();
            button.GetComponentsInChildren(true, s_Texts);
            int n = 0;
            for (int i = 0; i < s_Texts.Count; i++)
            {
                var t = s_Texts[i];
                if (t.GetComponentInParent<Selectable>(true) != sel) { s_Texts[i] = null; continue; }   // вложенная кнопка
                if (VisibleLength(t.text) > 40) { s_Texts.Clear(); return; }                          // описание, а не подпись
                n++;
            }
            if (n == 0 || n > 3) { s_Texts.Clear(); return; }
            for (int i = 0; i < s_Texts.Count; i++)
            {
                var t = s_Texts[i];
                if (t != null && !t.TryGetComponent<LGButtonLabel>(out _)) t.gameObject.AddComponent<LGButtonLabel>();
            }
            s_Texts.Clear();
        }

        private void OnEnable()
        {
            if (_text == null) _text = GetComponent<Text>();
            Style();
            Sync();
        }

        private void LateUpdate() => Sync();

        private void Style()
        {
            if (_styled || _text == null) return;
            _styled = true;
            _text.font = GameFont.Bold;
            _text.fontStyle = FontStyle.Bold;
            int size = _text.fontSize;
            int target = Mathf.Max(size, MinSize);
            // Ужимать длинные подписи под ширину кнопки (кроме подписей внутри раскладки — там ширину задаёт сам текст)
            var parent = transform.parent;
            bool inLayout = parent != null && parent.GetComponent<LayoutGroup>() != null;
            if (!inLayout && target > size)
            {
                _text.horizontalOverflow = HorizontalWrapMode.Wrap;
                _text.resizeTextForBestFit = true;
                _text.resizeTextMinSize = Mathf.Max(8, size);
                _text.resizeTextMaxSize = target;
            }
            _text.fontSize = target;
        }

        private void Sync()
        {
            if (_text == null) return;
            string s = _text.text;
            if (ReferenceEquals(s, _last)) return;
            string up = Upper(s);
            if (up != s) _text.text = up;
            _last = _text.text;

            // Разрядка — у однострочных подписей без тегов (своя разрядка, заданная в коде, сохраняется)
            bool plain = up.IndexOf('<') < 0 && up.IndexOf('\n') < 0;
            var tr = GetComponent<LGTracking>();
            if (tr == null && plain)
            {
                tr = gameObject.AddComponent<LGTracking>();
                tr.Spacing = Tracking;
            }
            else if (tr != null) tr.enabled = plain;
        }

        /// <summary>Заглавные буквы вне rich-text тегов.</summary>
        private static string Upper(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s_Sb.Clear();
            bool tag = false, changed = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') tag = true;
                else if (c == '>') tag = false;
                else if (!tag)
                {
                    char u = char.ToUpperInvariant(c);
                    if (u != c) { c = u; changed = true; }
                }
                s_Sb.Append(c);
            }
            return changed ? s_Sb.ToString() : s;
        }

        private static int VisibleLength(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            bool tag = false;
            foreach (char c in s)
            {
                if (c == '<') tag = true;
                else if (c == '>') tag = false;
                else if (!tag) n++;
            }
            return n;
        }
    }
}
