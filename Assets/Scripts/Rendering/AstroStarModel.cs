using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Объёмная звезда для вида системы: сфера с кипящей грануляцией (Astropolity/Star/Surface) и корона-билборд
    /// (Astropolity/Star/Corona). Цвет — по спектральному классу, рисунок — по Id системы. Цвета HDR, под Bloom светятся.
    /// </summary>
    public static class AstroStarModel
    {
        public const string ModelName = "AstroStar";
        private const float CoronaExtent = 4f;

        private static Shader s_surface, s_corona;
        private static bool s_checked;
        private static Mesh s_quad;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { s_checked = false; s_surface = s_corona = null; s_quad = null; }

        private static bool Available()
        {
            if (!s_checked)
            {
                s_checked = true;
                s_surface = Resources.Load<Shader>("Shaders/AstroStar") ?? Shader.Find("Astropolity/Star/Surface");
                s_corona = Resources.Load<Shader>("Shaders/AstroCorona") ?? Shader.Find("Astropolity/Star/Corona");
                if (s_surface == null || !s_surface.isSupported) Debug.LogWarning("[AstroStar] Нет шейдера Astropolity/Star/Surface — остаётся спрайт звезды.");
            }
            return s_surface != null && s_surface.isSupported && RealPlanetModel.UnitSphere != null;
        }

        /// <summary>Тёмный и горячий цвета поверхности (sRGB) по классу.</summary>
        private static void Palette(StarSpectralClass c, out Color cool, out Color hot)
        {
            switch (c)
            {
                case StarSpectralClass.ClassM: cool = Hex(0x6e1a06); hot = Hex(0xff7434); break;
                case StarSpectralClass.ClassK: cool = Hex(0x9c3a0b); hot = Hex(0xffb05e); break;
                case StarSpectralClass.ClassA: cool = Hex(0xc98a30); hot = Hex(0xfff4dc); break;
                case StarSpectralClass.ClassB: cool = Hex(0x2f5fc8); hot = Hex(0xd2e6ff); break;
                default: cool = Hex(0xb8530e); hot = Hex(0xffe4a0); break;
            }
        }

        private static Color Hex(int v) => new Color((v >> 16 & 255) / 255f, (v >> 8 & 255) / 255f, (v & 255) / 255f, 1f);

        /// <summary>Радиус звезды в виде системы.</summary>
        public static float Radius(StarSpectralClass c) => 2.6f * StarFx.SizeMul(c);

        /// <summary>Строит звезду в центре parent (radius &lt;= 0 — как в виде системы); null — шейдера нет или это чёрная дыра.</summary>
        public static GameObject Build(Transform parent, StarSystem system, float radius = -1f)
        {
            if (system == null || system.SpectralClass == StarSpectralClass.BlackHole || !Available()) return null;
            Palette(system.SpectralClass, out var cool, out var hot);
            if (radius <= 0f) radius = Radius(system.SpectralClass);
            float seed = (system.Id * 37 % 997) * 0.173f;

            var root = new GameObject(ModelName);
            root.transform.SetParent(parent, false);

            var body = new GameObject("Surface");
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = Vector3.one * radius;
            body.AddComponent<MeshFilter>().sharedMesh = RealPlanetModel.UnitSphere;
            var mr = body.AddComponent<MeshRenderer>();
            var m = new Material(s_surface) { name = "AstroStar_" + system.Id, hideFlags = HideFlags.DontSave };
            m.SetVector("_A", (Vector4)cool);
            m.SetVector("_B", (Vector4)hot);
            m.SetFloat("_Seed", seed);
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            if (s_corona != null && s_corona.isSupported)
            {
                var corona = new GameObject("Corona");
                corona.transform.SetParent(root.transform, false);
                corona.transform.localScale = Vector3.one * (2f * CoronaExtent * radius);
                corona.AddComponent<MeshFilter>().sharedMesh = Quad();
                var cr = corona.AddComponent<MeshRenderer>();
                var cm = new Material(s_corona) { name = "AstroCorona_" + system.Id, hideFlags = HideFlags.DontSave };
                cm.SetVector("_Color", (Vector4)hot);
                cm.SetFloat("_Extent", CoronaExtent);
                cm.SetFloat("_Seed", seed);
                cr.sharedMaterial = cm;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cr.receiveShadows = false;
                corona.AddComponent<BillboardLookAt>();
            }
            return root;
        }

        private static Mesh Quad()
        {
            if (s_quad != null) return s_quad;
            s_quad = new Mesh
            {
                name = "AstroCoronaQuad", hideFlags = HideFlags.DontSave,
                vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            s_quad.RecalculateBounds();
            return s_quad;
        }
    }
}
