using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>Вертикальный градиент по всей строке текста (верх → низ), умножается на цвет Text.</summary>
    [RequireComponent(typeof(Text))]
    public sealed class LGTextGradient : BaseMeshEffect
    {
        public Color Top = Color.white;
        public Color Bottom = Color.white;

        private static readonly List<UIVertex> s_verts = new List<UIVertex>();

        public static LGTextGradient Apply(Text t, Color top, Color bottom)
        {
            var g = t.GetComponent<LGTextGradient>() ?? t.gameObject.AddComponent<LGTextGradient>();
            g.Top = top; g.Bottom = bottom;
            t.SetVerticesDirty();
            return g;
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            s_verts.Clear();
            vh.GetUIVertexStream(s_verts);
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in s_verts) { min = Mathf.Min(min, v.position.y); max = Mathf.Max(max, v.position.y); }
            float h = Mathf.Max(max - min, 1e-3f);
            for (int i = 0; i < s_verts.Count; i++)
            {
                var v = s_verts[i];
                Color g = Color.Lerp(Bottom, Top, (v.position.y - min) / h);
                v.color = (Color)v.color * g;
                s_verts[i] = v;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(s_verts);
        }
    }
}
