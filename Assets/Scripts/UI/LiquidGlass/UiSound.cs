using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>Смысл нажатия — от него зависит звук кнопки.</summary>
    public enum UiPress { Auto, Click, Confirm, Danger, Back, Tab, Silent }

    /// <summary>Явно заданный смысл кнопки (если угадывать по виду нельзя).</summary>
    [DisallowMultipleComponent]
    public sealed class UiPressTag : MonoBehaviour
    {
        public UiPress Kind = UiPress.Auto;
    }

    /// <summary>
    /// Звук по смыслу. Одинаковый щелчок на всё ослабляет сигнал, поэтому кнопка звучит так, что делает:
    ///   • обычное действие — короткий сухой щелчок;
    ///   • подтверждение (зелёная кнопка) — светлая стеклянная пара нот;
    ///   • опасное решение (красная: война, роспуск, снос) — глухой низкий удар;
    ///   • закрыть / назад / отмена — нисходящая пара нот;
    ///   • вкладки и переключатели — мягкий «шорох».
    /// Смысл берётся из UiPressTag, иначе угадывается по имени и цвету кнопки.
    /// Если обработчик нажатия сам сыграл точный звук (объявление войны, открытие окна), общий не звучит —
    /// см. SFXManager.Press.
    /// </summary>
    public static class UiSound
    {
        /// <summary>Явно задать смысл кнопки.</summary>
        public static T Mark<T>(T c, UiPress kind) where T : Component
        {
            if (c == null) return c;
            var tag = c.GetComponent<UiPressTag>();
            if (tag == null) tag = c.gameObject.AddComponent<UiPressTag>();
            tag.Kind = kind;
            return c;
        }

        public static void Press(GameObject go)
        {
            switch (Classify(go))
            {
                case UiPress.Silent: return;
                case UiPress.Confirm: SFXManager.Press(Sfx.UiConfirm, 0.75f); break;
                case UiPress.Danger: SFXManager.Press(Sfx.UiDanger); break;
                case UiPress.Back: SFXManager.Press(Sfx.UiBack, 0.85f); break;
                case UiPress.Tab: SFXManager.Press(Sfx.UiTab); break;
                default: SFXManager.Press(Sfx.UiClick); break;
            }
        }

        public static UiPress Classify(GameObject go)
        {
            if (go == null) return UiPress.Click;
            var tag = go.GetComponent<UiPressTag>();
            if (tag != null && tag.Kind != UiPress.Auto) return tag.Kind;

            if (go.GetComponent<Toggle>() != null) return UiPress.Tab;

            // По имени: кнопки закрытия часто красные — имя важнее цвета
            string n = go.name;
            if (Has(n, "Close") || Has(n, "Back") || Has(n, "Cancel") || Has(n, "Exit")) return UiPress.Back;
            if (n.StartsWith("Tab") || n.EndsWith("Tab") || Has(n, "TabBtn") || Has(n, "Tab_")) return UiPress.Tab;

            // По цвету: кромка (если задана), иначе заливка
            Color c = default;
            bool any = false;
            var fx = go.GetComponent<LiquidGlassEffect>();
            if (fx != null && fx.TryGetRim(out var rim) && rim.a > 0.05f) { c = rim; any = true; }
            if (!any)
            {
                var g = go.GetComponent<Graphic>();
                if (g != null) { c = g.color; any = true; }
            }
            if (any)
            {
                Color.RGBToHSV(c, out float h, out float s, out float v);
                if (s > 0.42f && v > 0.12f)
                {
                    if (h < 0.035f || h > 0.94f) return UiPress.Danger;
                    if (h > 0.25f && h < 0.47f) return UiPress.Confirm;
                }
            }
            return UiPress.Click;
        }

        private static bool Has(string s, string part) => s.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
