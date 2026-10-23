using System;
using System.IO;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Готовая модель пиратского корабля (Resources/Models/Ships/Pirate): красно-белый корпус с дугами крыльев,
    /// стволами и светящимся кристаллом — у всех военных кораблей пиратов вместо корпусов Star Sparrow.
    ///
    /// Вынута из исходного GLB скриптом Tools/ships/export_pirate.py в pirate.bytes (формат PLM1, как у моделей
    /// планет: оси Unity, нос по +Z, длина 1) и pirate_config.json (где сопло). Обшивка — URP Lit: альбедо,
    /// металл (R) + затенение (G) + гладкость (A) одной картой, нормали, огни; крылья тонкие — материал двусторонний.
    /// Кристалл светится цветом владельца (у пиратов — кислотно-зелёным) и подсвечивается при выборе.
    /// Модель «Space ship», автор jkazulyblanco, CC BY 4.0.
    /// </summary>
    public static class PirateShipModel
    {
        private const string Folder = "Models/Ships/Pirate/";

        [Serializable]
        private class Config { public float[] nozzle; public float nozzleRadius = 0.05f; }

        private static Mesh s_hull, s_crystal;
        private static Material s_hullMat;
        private static Config s_config;
        private static bool s_loaded, s_missing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_hull = s_crystal = null;
            s_hullMat = null;
            s_config = null;
            s_loaded = s_missing = false;
        }

        /// <summary>Собрать модель под parent (нос по +Z); null — модели нет, остаётся обычный корпус.</summary>
        public static ShipMeshFactory.ShipVisual Build(Transform parent, Color owner, float length)
        {
            if (!s_loaded) Load();
            if (s_missing || ShaderCache.Lit == null) return null;

            var root = new GameObject("ShipModel").transform;
            root.SetParent(parent, false);
            var model = new GameObject("PirateShip").transform;
            model.SetParent(root, false);
            model.localScale = Vector3.one * length;

            var hull = new GameObject("Hull");
            hull.transform.SetParent(model, false);
            hull.AddComponent<MeshFilter>().sharedMesh = s_hull;
            var hr = hull.AddComponent<MeshRenderer>();
            hr.sharedMaterial = HullMaterial();
            hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var v = new ShipMeshFactory.ShipVisual
            {
                AccentColor = Color.Lerp(owner, Color.white, 0.1f),
                GlowColor = new Color(0.45f, 0.95f, 1f)
            };
            // Кристалл — свой материал у каждого корабля: выбор подсвечивает только его
            v.Accent = ShipMeshFactory.LitMat(new Color(0.57f, 0.57f, 0.03f), v.AccentColor * 0.45f, 0.1f, 0.85f);
            v.Accent.name = "PirateShip_Crystal";
            v.Glow = ShipMeshFactory.GlowMat(v.GlowColor * 2.2f);
            v.Hull = hr.sharedMaterial;
            if (s_crystal != null)
            {
                var crystal = new GameObject("Crystal");
                crystal.transform.SetParent(model, false);
                crystal.AddComponent<MeshFilter>().sharedMesh = s_crystal;
                var cr = crystal.AddComponent<MeshRenderer>();
                cr.sharedMaterial = v.Accent;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Сопло — кольцо двигателя у кормы (из конвертера); в координатах корня, как у остальных моделей
            var n = s_config != null && s_config.nozzle != null && s_config.nozzle.Length >= 3
                ? new Vector3(s_config.nozzle[0], s_config.nozzle[1], s_config.nozzle[2]) : new Vector3(0f, 0f, -0.5f);
            v.Nozzles = new[] { n * length };
            v.NozzleRadius = Mathf.Max(0.06f, s_config != null ? s_config.nozzleRadius : 0.05f) * length;
            return v;
        }

        // ================================================================ Загрузка

        private static void Load()
        {
            s_loaded = true;
            var data = Resources.Load<TextAsset>(Folder + "pirate");
            if (data == null)
            {
                s_missing = true;
                Debug.LogWarning("[PirateShip] Нет Resources/" + Folder + "pirate.bytes — пираты летают на обычных корпусах.");
                return;
            }
            try
            {
                Parse(data.bytes);
                var cfg = Resources.Load<TextAsset>(Folder + "pirate_config");
                if (cfg != null) s_config = JsonUtility.FromJson<Config>(cfg.text);
                s_missing = s_hull == null;
            }
            catch (Exception e)
            {
                s_missing = true;
                Debug.LogWarning("[PirateShip] Не удалось прочитать модель: " + e.Message);
            }
            Resources.UnloadAsset(data);
        }

        /// <summary>Формат PLM1 — см. PlanetModelLibrary: слои «hull» и «crystal».</summary>
        private static void Parse(byte[] bytes)
        {
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

                var mesh = new Mesh { name = "PirateShip_" + name, hideFlags = HideFlags.DontSave };
                if (vc > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = pos;
                mesh.normals = nrm;
                if (uv != null) mesh.uv = uv;
                mesh.triangles = tris;
                if (uv != null) mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                if (name == "hull") s_hull = mesh;
                else if (name == "crystal") s_crystal = mesh;
            }
        }

        private static Texture2D Tex(string name) => Resources.Load<Texture2D>(Folder + name);

        /// <summary>Обшивка — одна на всех пиратов: URP Lit с картами модели, двусторонняя (тонкие крылья).</summary>
        private static Material HullMaterial()
        {
            if (s_hullMat != null) return s_hullMat;
            var m = new Material(ShaderCache.Lit) { name = "PirateShip_Hull", hideFlags = HideFlags.DontSave };
            var albedo = Tex("hull_albedo");
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            var ms = Tex("hull_ms");
            if (ms != null)
            {
                if (m.HasProperty("_MetallicGlossMap")) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                if (m.HasProperty("_OcclusionMap")) { m.SetTexture("_OcclusionMap", ms); m.EnableKeyword("_OCCLUSIONMAP"); }
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.9f);
            }
            var normal = Tex("hull_normal");
            if (normal != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            var emission = Tex("hull_emission");
            if (emission != null && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emission);
                m.SetColor("_EmissionColor", Color.white * 1.6f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // крылья — тонкие пластины: видны с обеих сторон
            m.doubleSidedGI = true;
            s_hullMat = m;
            return m;
        }
    }
}
