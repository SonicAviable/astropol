using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Разрядка (tracking) для UI Text: равный дополнительный шаг между буквами — заголовки «как в больших играх».
    /// Работает построчно и учитывает выравнивание строки (слева / по центру / справа).
    /// Тень (Shadow) от LGBuild.Label идёт копией геометрии перед основным текстом — она получает ту же разрядку,
    /// потому что строка определяется по «возврату» каретки влево.
    /// Подходит для однострочных заголовков без rich-text тегов.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class LGTracking : BaseMeshEffect
    {
        [SerializeField] private float spacing = 1.5f;

        public float Spacing
        {
            get => spacing;
            set
            {
                if (Mathf.Approximately(spacing, value)) return;
                spacing = value;
                if (graphic != null) graphic.SetVerticesDirty();
            }
        }

        private static readonly List<UIVertex> s_Verts = new List<UIVertex>();
        private static readonly List<int> s_LineStart = new List<int>();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0 || Mathf.Approximately(spacing, 0f)) return;
            var text = graphic as Text;
            if (text == null) return;

            s_Verts.Clear();
            vh.GetUIVertexStream(s_Verts);           // по 6 вершин на букву (два треугольника)
            int quads = s_Verts.Count / 6;
            if (quads < 2) return;

            // Строки: новая начинается, когда буква оказалась левее предыдущей
            s_LineStart.Clear();
            s_LineStart.Add(0);
            float prevX = float.MinValue;
            for (int q = 0; q < quads; q++)
            {
                float x = s_Verts[q * 6].position.x;
                if (q > 0 && x < prevX - 0.5f) s_LineStart.Add(q);
                prevX = x;
            }
            s_LineStart.Add(quads);

            float align = 0f;   // доля «лишней» ширины, на которую строку сдвигают влево
            switch (text.alignment)
            {
                case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter: align = 0.5f; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: align = 1f; break;
            }

            for (int l = 0; l + 1 < s_LineStart.Count; l++)
            {
                int from = s_LineStart[l], to = s_LineStart[l + 1];
                float extra = (to - from - 1) * spacing;
                for (int q = from; q < to; q++)
                {
                    float dx = (q - from) * spacing - extra * align;
                    for (int v = 0; v < 6; v++)
                    {
                        int i = q * 6 + v;
                        var vert = s_Verts[i];
                        vert.position.x += dx;
                        s_Verts[i] = vert;
                    }
                }
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(s_Verts);
        }
    }
}
