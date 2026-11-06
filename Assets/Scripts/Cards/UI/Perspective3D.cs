using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Перспектива для интерфейса. Канвас боя — Overlay и рисует без перспективы: карта, повёрнутая по X/Y, просто
    /// сплющивается. Эффект сдвигает вершины картинки так, как их увидела бы камера с фокусным расстоянием Focal
    /// (пиксели канваса) из точки над центром Root: дальний край меньше, ближний больше — карта наклоняется в 3D.
    /// Плоская (не повёрнутая по X/Y) картинка не меняется. Сетку надо перестраивать, пока наклон меняется, — это
    /// делает Perspective3D.Refresh для всех картинок объекта.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Perspective3D : BaseMeshEffect
    {
        /// <summary>Центр перспективы (середина карты или осколка); null — сама картинка.</summary>
        public RectTransform Root;
        /// <summary>Фокусное расстояние в пикселях канваса: меньше — сильнее перспектива.</summary>
        public static float Focal = 900f;

        /// <summary>Повёрнута ли картинка по X/Y (иначе перспектива не нужна).</summary>
        public static bool Tilted(Transform t) => Mathf.Abs(t.forward.z) < 0.99995f;

        /// <summary>Добавить перспективу всем картинкам внутри root (центр — root) и вернуть их список.</summary>
        public static Graphic[] Attach(RectTransform root)
        {
            var gs = root.GetComponentsInChildren<Graphic>(true);
            foreach (var g in gs)
            {
                var p = g.GetComponent<Perspective3D>();
                if (p == null) p = g.gameObject.AddComponent<Perspective3D>();
                p.Root = root;
            }
            return gs;
        }

        /// <summary>Перестроить сетки (наклон изменился).</summary>
        public static void Refresh(Graphic[] gs)
        {
            if (gs == null) return;
            foreach (var g in gs) if (g != null && g.isActiveAndEnabled) g.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var t = transform;
            if (!Tilted(t)) return;
            var root = Root != null ? Root : (RectTransform)t;
            var canvas = graphic.canvas;
            float f = Focal * (canvas != null ? canvas.rootCanvas.transform.lossyScale.x : 1f);   // пиксели канваса → мир
            Vector3 c = root.TransformPoint(root.rect.center);
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                Vector3 w = t.TransformPoint(v.position);
                // +z — от зрителя: дальний край сходится к центру, ближний расходится
                float k = f / Mathf.Max(f * 0.25f, f + (w.z - c.z));
                var w2 = new Vector3(c.x + (w.x - c.x) * k, c.y + (w.y - c.y) * k, w.z);
                v.position = t.InverseTransformPoint(w2);
                vh.SetUIVertex(v, i);
            }
        }
    }
}
