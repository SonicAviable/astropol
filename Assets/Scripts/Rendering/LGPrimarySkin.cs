using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Главная кнопка «Астролябии» поверх старой кнопки, которую код красит и подписывает сам: доступная — полированная
    /// латунь с тёмной подписью и камнями-сапфирами, недоступная — патина с приглушённой подписью. Подпись — капслоком,
    /// служебный значок в начале («▶», «✕»…) убирается. Цвета, которые ставит код, перекрываются (см. <see cref="LGAstrolabe"/>).
    /// </summary>
    public sealed class LGPrimarySkin : MonoBehaviour
    {
        public static LGPrimarySkin Add(GameObject go, Text label)
        {
            var s = go.TryGetComponent<LGPrimarySkin>(out var existing) ? existing : go.AddComponent<LGPrimarySkin>();
            if (label != null)
            {
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 9;
                label.resizeTextMaxSize = Mathf.Max(13, label.fontSize);
                label.fontSize = Mathf.Max(13, label.fontSize);
            }
            LGAstrolabe.Attach(go, go.GetComponent<Image>(), label, primary: true);
            return s;
        }
    }
}
