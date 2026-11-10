using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Научное судно Республики Астрея AS/07 (Resources/Models/Ships/Astrea) — у научных кораблей фракции «astrea»
    /// вместо зонда «Марс-3».
    ///
    /// FBX из Blender: Y-up, нос +Z, три LOD (меши *_LOD0..2), слоты материалов Hull / Accent / Glow и Blink у маяка.
    /// Служебные узлы: Nozzle_01..03 (маршевые), Retro_01..02 (тормозные), Blink_01. Кости Anim_Ring_01 (кольцо-сенсор)
    /// и Anim_Dish_Open (тарелка); клип idle (кольцо — оборот за 16 с) крутится по кругу через SampleAnimation.
    /// Текстуры: albedo с AO, MS (R metallic, A smoothness), normal, emission.
    /// </summary>
    public static class AstreaScienceModel
    {
        private const string Folder = "Models/Ships/Astrea/";
        private const string Stem = "astrea_science";
        /// <summary>Наибольший размер в плане — как у «Марс-3».</summary>
        public const float DefaultSpan = 3.2f;

        private static GameObject s_prefab;
        private static AnimationClip s_idle;
        private static Material s_hull;
        private static bool s_missing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_prefab = null;
            s_idle = null;
            s_hull = null;
            s_missing = false;
        }

        public static bool Available
        {
            get
            {
                if (s_prefab == null && !s_missing)
                {
                    s_prefab = Resources.Load<GameObject>(Folder + Stem);
                    s_missing = s_prefab == null;
                    if (s_missing) Debug.LogWarning("[AstreaScience] Не найдена модель Resources/" + Folder + Stem + ".fbx");
                    else
                        foreach (var c in Resources.LoadAll<AnimationClip>(Folder + Stem))
                        {
                            if (c == null || c.name.StartsWith("__preview__")) continue;
                            if (c.name.EndsWith("idle")) { s_idle = c; break; }
                            if (s_idle == null && !c.name.EndsWith("scan")) s_idle = c;
                        }
                }
                return s_prefab != null && ShaderCache.Lit != null;
            }
        }

        public static ShipMeshFactory.ShipVisual Build(Transform parent, Color owner, float span = DefaultSpan)
        {
            if (!Available) return null;

            var root = new GameObject("ShipModel").transform;
            root.SetParent(parent, false);
            var pivot = new GameObject("AstreaScience").transform;
            pivot.SetParent(root, false);

            var model = Object.Instantiate(s_prefab, pivot, false);
            model.name = "AstreaScienceModel";
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            var animator = model.GetComponent<Animator>();
            if (animator != null) animator.enabled = false;   // анимацию ведём сами — без контроллера

            var all = model.GetComponentsInChildren<Renderer>(true);
            if (all.Length == 0)
            {
                Object.Destroy(root.gameObject);
                return null;
            }

            var v = new ShipMeshFactory.ShipVisual
            {
                AccentColor = Color.Lerp(owner, Color.white, 0.1f),
                GlowColor = new Color(0.15f, 0.78f, 1f),
                BlinkColor = new Color(1f, 0.25f, 0.08f) * 2.6f
            };
            v.Hull = HullMat();
            v.Accent = ShipMeshFactory.LitMat(v.AccentColor, v.AccentColor * 0.45f, 0.32f, 0.7f);
            v.Glow = ShipMeshFactory.GlowMat(v.GlowColor * 2.4f);
            v.BlinkMat = ShipMeshFactory.GlowMat(v.BlinkColor);

            var lods = new List<Renderer>[3] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
            var lod0 = new List<Renderer>();
            foreach (var r in all)
            {
                r.gameObject.SetActive(true);
                r.enabled = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;

                var src = r.sharedMaterials;
                var mats = new Material[Mathf.Max(1, src.Length)];
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = MaterialFor(i < src.Length && src[i] != null ? src[i].name : "", v);
                r.sharedMaterials = mats;

                int lod = LodOf(r.name);
                if (lod >= 0) lods[lod].Add(r);
                if (lod <= 0) lod0.Add(r);   // маяк без суффикса — виден всегда
            }

            if (s_idle != null) s_idle.SampleAnimation(model, 0f);

            // Нос должен смотреть по +Z: маршевые сопла — сзади
            var nozzles = FindAll(model.transform, "Nozzle_");
            var b = BoundsIn(root, lod0);
            if (nozzles.Count > 0 && AvgZ(root, nozzles) > b.center.z)
            {
                pivot.localRotation = Quaternion.Euler(0f, 180f, 0f);
                b = BoundsIn(root, lod0);
            }
            float size = Mathf.Max(b.size.x, b.size.z, 0.0001f);
            pivot.localScale *= span / size;
            b = BoundsIn(root, lod0);
            pivot.localPosition -= b.center;
            b = BoundsIn(root, lod0);

            if (lods[1].Count > 0 && lods[2].Count > 0)
            {
                var group = model.GetComponent<LODGroup>() ?? model.AddComponent<LODGroup>();
                group.SetLODs(new[]
                {
                    new LOD(0.32f, lods[0].ToArray()),
                    new LOD(0.12f, lods[1].ToArray()),
                    new LOD(0f, lods[2].ToArray())   // последний уровень не пропадает при отдалении карты
                });
                group.RecalculateBounds();
            }

            // Сопла — по служебным узлам модели, запасной вариант — три у кормы
            v.Nozzles = PointsIn(root, nozzles);
            v.RetroNozzles = PointsIn(root, FindAll(model.transform, "Retro_"));
            if (v.Nozzles.Length == 0)
                v.Nozzles = new[]
                {
                    new Vector3(-b.size.x * 0.08f, b.center.y, b.min.z), new Vector3(0f, b.center.y, b.min.z),
                    new Vector3(b.size.x * 0.08f, b.center.y, b.min.z)
                };
            v.NozzleRadius = Mathf.Max(0.04f, span * 0.025f);
            v.RetroRadius = v.NozzleRadius * 0.5f;

            if (s_idle != null)
            {
                var clip = s_idle;
                float phase = Random.value * clip.length;   // кольца разных кораблей крутятся вразнобой
                v.Animate = t =>
                {
                    if (model != null) clip.SampleAnimation(model, Mathf.Repeat(t + phase, clip.length));
                };
            }
            return v;
        }

        private static int LodOf(string name)
        {
            int i = name.LastIndexOf("_LOD", System.StringComparison.Ordinal);
            if (i < 0 || i + 4 >= name.Length) return -1;
            char c = name[i + 4];
            return c >= '0' && c <= '2' ? c - '0' : -1;
        }

        private static Material MaterialFor(string slot, ShipMeshFactory.ShipVisual v)
        {
            if (slot.StartsWith("Accent")) return v.Accent;
            if (slot.StartsWith("Glow")) return v.Glow;
            if (slot.StartsWith("Blink")) return v.BlinkMat;
            return v.Hull;
        }

        private static List<Transform> FindAll(Transform t, string prefix)
        {
            var list = new List<Transform>();
            foreach (var c in t.GetComponentsInChildren<Transform>(true))
                if (c.name.StartsWith(prefix)) list.Add(c);
            return list;
        }

        private static float AvgZ(Transform root, List<Transform> ts)
        {
            float z = 0f;
            foreach (var t in ts) z += root.InverseTransformPoint(t.position).z;
            return z / ts.Count;
        }

        private static Vector3[] PointsIn(Transform root, List<Transform> ts)
        {
            var p = new Vector3[ts.Count];
            for (int i = 0; i < ts.Count; i++) p[i] = root.InverseTransformPoint(ts[i].position);
            return p;
        }

        private static Bounds BoundsIn(Transform root, List<Renderer> renderers)
        {
            bool any = false;
            var b = new Bounds();
            var m = root.worldToLocalMatrix;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                Bounds lb;
                Matrix4x4 toRoot;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) { lb = smr.sharedMesh.bounds; toRoot = m * smr.transform.localToWorldMatrix; }
                else if (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null) { lb = mf.sharedMesh.bounds; toRoot = m * r.transform.localToWorldMatrix; }
                else continue;
                Vector3 c = lb.center, e = lb.extents;
                for (int i = 0; i < 8; i++)
                {
                    var p = toRoot.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>Общая PBR-обшивка URP Lit (как у остальных кораблей — чуть светлее, чтобы не теряться на чёрном).</summary>
        private static Material HullMat()
        {
            if (s_hull != null) return s_hull;
            var m = new Material(ShaderCache.Lit) { hideFlags = HideFlags.DontSave, name = "AstreaScience_Hull" };
            var albedo = Resources.Load<Texture2D>(Folder + Stem + "_albedo");
            var ms = Resources.Load<Texture2D>(Folder + Stem + "_ms");
            var normal = Resources.Load<Texture2D>(Folder + Stem + "_normal");
            var emission = Resources.Load<Texture2D>(Folder + Stem + "_emission");

            var tint = new Color(1.15f, 1.15f, 1.17f, 1f);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            if (ms != null && m.HasProperty("_MetallicGlossMap"))
            {
                m.SetTexture("_MetallicGlossMap", ms);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 1f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f);
            }
            if (normal != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
            }
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                if (emission != null && m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emission);
                m.SetColor("_EmissionColor", emission != null ? Color.white : new Color(0.05f, 0.05f, 0.06f));
            }
            s_hull = m;
            return s_hull;
        }
    }
}
