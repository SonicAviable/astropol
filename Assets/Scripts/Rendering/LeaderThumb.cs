using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Миниатюра лица лидера: скруглённая рамка цвета класса и вырезка лица из портрета (LeaderFaces).
    /// Корень растянут по родителю; если у лидера нет портрета, миниатюра скрывается.
    /// </summary>
    public sealed class LeaderThumb
    {
        public readonly RectTransform Root;
        private readonly Image _ring;
        private readonly RawImage _art;

        private LeaderThumb(RectTransform root, Image ring, RawImage art)
        {
            Root = root;
            _ring = ring;
            _art = art;
        }

        public static LeaderThumb Create(Transform parent, float radius, float ring = 1.5f)
        {
            var root = LGBuild.Rect(parent, "LeaderThumb");
            root.Stretch();
            LG.Ignore(root.gameObject);                          // свои слои, без авто-стекла

            var outer = LG.RoundedMask(root, radius);
            var ringImg = LGBuild.Panel(outer, "Ring", Color.white);
            ringImg.rectTransform.Stretch();
            var inner = LG.RoundedMask(outer, Mathf.Max(1f, radius - ring), ring);
            var art = new GameObject("Face").AddComponent<RawImage>();
            art.transform.SetParent(inner, false);
            art.rectTransform.Stretch();
            art.raycastTarget = false;
            return new LeaderThumb(root, ringImg, art);
        }

        /// <summary>Показать лицо лидера. false — портрета нет (миниатюра скрыта).</summary>
        public bool Set(Leader l, Color ring)
        {
            var face = LeaderFaces.For(l);
            bool has = face != null;
            Root.gameObject.SetActive(has);
            if (!has) return false;
            _ring.color = new Color(ring.r, ring.g, ring.b, 0.85f);
            if (_art.texture != face.Texture) _art.texture = face.Texture;
            _art.uvRect = LeaderFaces.FaceUv(face);
            return true;
        }

        public void Hide() => Root.gameObject.SetActive(false);

        /// <summary>Цвет рамки по классу лидера (как во вкладке «Лидеры»).</summary>
        public static Color ClassColor(LeaderClass c) => c == LeaderClass.Scientist ? new Color(0.40f, 0.96f, 0.62f)
                                                       : c == LeaderClass.Admiral ? new Color(1f, 0.42f, 0.42f)
                                                       : new Color(1f, 0.80f, 0.32f);
    }
}
