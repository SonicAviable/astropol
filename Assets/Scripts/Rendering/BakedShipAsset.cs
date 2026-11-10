using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Общее для кораблей, которые строят и запекают скрипты Tools/ships/*.py (Blender): чтение слоёв PLM1
    /// (оси Unity, нос по +Z, длина 1) и материал обшивки URP Lit на запечённых картах —
    /// альбедо, металл (R) + затенение (G) + гладкость (A), нормали, свечение.
    /// </summary>
    public static class BakedShipAsset
    {
        /// <summary>Формат PLM1 — см. PlanetModelLibrary: именованные слои мешей.</summary>
        public static Dictionary<string, Mesh> ParsePlm(byte[] bytes, string meshPrefix)
        {
            var result = new Dictionary<string, Mesh>();
            using var r = new BinaryReader(new MemoryStream(bytes));
            if (new string(r.ReadChars(4)) != "PLM1") throw new InvalidDataException("PLM1");
            int count = r.ReadInt32();
            for (int l = 0; l < count; l++)
            {
                string name = System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadByte()));
                int vc = r.ReadInt32(), ic = r.ReadInt32();
                bool hasUv = r.ReadByte() != 0;
                var pos = new Vector3[vc];
                for (int i = 0; i < vc; i++) pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var nrm = new Vector3[vc];
                for (int i = 0; i < vc; i++) nrm[i] = new Vector3(r.ReadSByte(), r.ReadSByte(), r.ReadSByte()) / 127f;
                Vector2[] uv = null;
                if (hasUv)
                {
                    uv = new Vector2[vc];
                    for (int i = 0; i < vc; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                }
                var tris = new int[ic];
                for (int i = 0; i < ic; i++) tris[i] = (int)r.ReadUInt32();

                var mesh = new Mesh { name = meshPrefix + name, hideFlags = HideFlags.DontSave };
                if (vc > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = pos;
                mesh.normals = nrm;
                if (uv != null) mesh.uv = uv;
                mesh.triangles = tris;
                if (uv != null) mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                result[name] = mesh;
            }
            return result;
        }

        /// <summary>
        /// Обшивка на запечённых картах {prefix}_albedo / _ms / _normal / _emission из папки Resources.
        /// baseTint чуть больше 1 — корабль не теряется на чёрном фоне, как и остальные модели.
        /// </summary>
        public static Material HullMaterial(string folder, string prefix, string name, Color baseTint, float emission)
        {
            var m = new Material(ShaderCache.Lit) { name = name, hideFlags = HideFlags.DontSave };
            var albedo = Resources.Load<Texture2D>(folder + prefix + "_albedo");
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseTint);
            var ms = Resources.Load<Texture2D>(folder + prefix + "_ms");
            if (ms != null)
            {
                if (m.HasProperty("_MetallicGlossMap")) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                if (m.HasProperty("_OcclusionMap")) { m.SetTexture("_OcclusionMap", ms); m.EnableKeyword("_OCCLUSIONMAP"); }
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f);
            }
            var normal = Resources.Load<Texture2D>(folder + prefix + "_normal");
            if (normal != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            var glow = Resources.Load<Texture2D>(folder + prefix + "_emission");
            if (glow != null && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", glow);
                m.SetColor("_EmissionColor", Color.white * emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            return m;
        }

        /// <summary>Краска фракции: копия обшивки, окрашенная цветом владельца (без карты свечения — выбор подсвечивает её целиком).</summary>
        public static Material AccentFrom(Material hull, string name)
        {
            var m = new Material(hull) { name = name, hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", null);
            return m;
        }

        /// <summary>Дочерний объект с мешем; null — слоя нет.</summary>
        public static Transform Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (mesh == null) return go.transform;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>Тройки координат из конфига -> точки, умноженные на длину корабля.</summary>
        public static Vector3[] Points(float[] xyz, float length)
        {
            if (xyz == null || xyz.Length < 3) return null;
            var p = new Vector3[xyz.Length / 3];
            for (int i = 0; i < p.Length; i++) p[i] = new Vector3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]) * length;
            return p;
        }
    }
}
