using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Общие материалы и меши наземного боя: один материал на цвет (с GPU Instancing), примитивные меши без коллайдеров.
    /// Всё создаётся кодом и уничтожается вместе с боем (Release).
    /// </summary>
    public static class GroundMats
    {
        private static readonly Dictionary<int, Material> s_lit = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Material> s_glow = new Dictionary<int, Material>();
        private static readonly Dictionary<PrimitiveType, Mesh> s_prims = new Dictionary<PrimitiveType, Mesh>();
        private static Shader s_add;
        private static Material s_additive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { s_lit.Clear(); s_glow.Clear(); s_prims.Clear(); s_rings.Clear(); s_add = null; s_additive = null; }

        private static int Key(Color c, float a = 0f, float b = 0f)
            => (Mathf.RoundToInt(c.r * 255f) << 24) ^ (Mathf.RoundToInt(c.g * 255f) << 16) ^ (Mathf.RoundToInt(c.b * 255f) << 8)
               ^ Mathf.RoundToInt(a * 31f) ^ (Mathf.RoundToInt(b * 31f) << 5);

        /// <summary>Матовый/металлический материал цвета c (общий на цвет).</summary>
        public static Material Lit(Color c, float metallic = 0.1f, float smooth = 0.3f, Color? emission = null)
        {
            int k = Key(c, metallic, smooth) ^ (emission.HasValue ? Key(emission.Value) * 31 : 0);
            if (s_lit.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(ShaderCache.Lit) { name = "Ground_" + k, enableInstancing = true, hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (emission.HasValue && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
            }
            s_lit[k] = m;
            return m;
        }

        /// <summary>Светящийся материал (визоры, огни, лава).</summary>
        public static Material Glow(Color c)
        {
            int k = Key(c, 0.7f, 0.9f);
            if (s_glow.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(ShaderCache.Unlit) { name = "GroundGlow_" + k, enableInstancing = true, hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            s_glow[k] = m;
            return m;
        }

        /// <summary>Аддитивный материал эффектов (трассеры, вспышки, лучи) — цвет из вершин/LineRenderer.</summary>
        public static Material Additive
        {
            get
            {
                if (s_additive != null) return s_additive;
                if (s_add == null) s_add = Resources.Load<Shader>("Shaders/CombatAdditive");
                s_additive = s_add != null ? new Material(s_add) { name = "GroundAdditive", hideFlags = HideFlags.DontSave }
                                           : new Material(ShaderCache.Sprite) { name = "GroundAdditive", hideFlags = HideFlags.DontSave };
                // В бою вспышки и трассеры ярче порога свечения — светятся, как в кино
                if (s_additive.HasProperty("_Intensity")) s_additive.SetFloat("_Intensity", 2.6f);
                return s_additive;
            }
        }

        public static Mesh Prim(PrimitiveType t)
        {
            if (s_prims.TryGetValue(t, out var m) && m != null) return m;
            var go = GameObject.CreatePrimitive(t);
            m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            s_prims[t] = m;
            return m;
        }

        /// <summary>Часть модели: примитив без коллайдера.</summary>
        public static Transform Part(Transform parent, PrimitiveType t, Vector3 pos, Vector3 scale, Material mat, string name = "Part", Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (rot.HasValue) go.transform.localRotation = rot.Value;
            go.AddComponent<MeshFilter>().sharedMesh = Prim(t);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        /// <summary>Часть модели: параллелепипед со скруглёнными рёбрами (размер — в метрах, масштаб 1).</summary>
        public static Transform Box(Transform parent, Vector3 pos, Vector3 size, Material mat, string name = "Box", Quaternion? rot = null, float bevel = 0.04f)
            => MeshPart(parent, ProcMesh.Box(size, bevel), pos, Vector3.one, mat, name, rot);

        /// <summary>Часть модели из готового меша.</summary>
        public static Transform MeshPart(Transform parent, Mesh mesh, Vector3 pos, Vector3 scale, Material mat, string name = "Part", Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (rot.HasValue) go.transform.localRotation = rot.Value;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        private static readonly Dictionary<int, Material> s_rings = new Dictionary<int, Material>();

        /// <summary>Материал кольца под бойцом (цвет стороны; выделенные — ярче).</summary>
        public static Material Ring(Color c, bool selected)
        {
            int k = Key(c) ^ (selected ? 0x5a5a5a5 : 0);
            if (s_rings.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(ShaderCache.Sprite) { name = "GroundRing", hideFlags = HideFlags.DontSave, mainTexture = FxTextures.Ring };
            Color col = selected ? Color.Lerp(c, Color.white, 0.35f) : c;
            col.a = selected ? 1f : 0.55f;
            m.color = col;
            m.renderQueue = 2460;
            s_rings[k] = m;
            return m;
        }

        /// <summary>Освободить материалы боя (после его окончания).</summary>
        public static void Release()
        {
            foreach (var m in s_lit.Values) if (m != null) Object.Destroy(m);
            foreach (var m in s_glow.Values) if (m != null) Object.Destroy(m);
            foreach (var m in s_rings.Values) if (m != null) Object.Destroy(m);
            s_lit.Clear();
            s_glow.Clear();
            s_rings.Clear();
            ProcMesh.Release();
            if (s_additive != null) Object.Destroy(s_additive);
            s_additive = null;
        }
    }
}
