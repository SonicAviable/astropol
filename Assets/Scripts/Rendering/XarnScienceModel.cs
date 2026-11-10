using System;
using System.IO;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Научный корабль Доминиона Ксарн «Око Доминиона» (Resources/Models/Ships/XarnScience) — у научных судов ксарнийцев
    /// вместо зонда «Марс-3». Бронированный клин с раздвоенным носом и сенсорным «оком», тарелка дальнего сканирования
    /// на турели, крылья с сенсорными гондолами, двигательный блок с тремя соплами и радиаторами.
    ///
    /// Модель строит и запекает скрипт Tools/ships/xarn_science.py (Blender): xarn_science.bytes (формат PLM1, оси Unity,
    /// нос по +Z, длина 1; слои hull / accent / beacon) и xarn_science_config.json (сопла). Обшивка — URP Lit: альбедо
    /// (с запечённой AO), металл (R) + затенение (G) + гладкость (A), нормали, свечение (окна, сопла, сенсоры, огни).
    /// Слой accent — те же карты, но окрашен цветом владельца и подсвечивается при выборе; beacon — мигающие маяки.
    /// </summary>
    public static class XarnScienceModel
    {
        private const string Folder = "Models/Ships/XarnScience/";
        /// <summary>Длина корпуса (нос — сопла) — под коллайдер FleetView, как у остальных научных судов.</summary>
        public const float DefaultLength = 2.9f;

        [Serializable]
        private class Config { public float[] nozzles; public float nozzleRadius = 0.03f; }

        private static Mesh s_hull, s_accent, s_beacon;
        private static Material s_hullMat;
        private static Config s_config;
        private static bool s_loaded, s_missing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_hull = s_accent = s_beacon = null;
            s_hullMat = null;
            s_config = null;
            s_loaded = s_missing = false;
        }

        /// <summary>Владелец — Доминион Ксарн.</summary>
        public static bool IsXarn(int ownerId) => FactionRegistry.KeyOf(FactionTraits.FactionOf(ownerId)) == "xarn";

        /// <summary>Собрать модель под parent (нос по +Z); null — модели нет, остаётся обычный научный корабль.</summary>
        public static ShipMeshFactory.ShipVisual Build(Transform parent, Color owner, float length = DefaultLength)
        {
            if (!s_loaded) Load();
            if (s_missing || ShaderCache.Lit == null) return null;

            var root = new GameObject("ShipModel").transform;
            root.SetParent(parent, false);
            var model = new GameObject("XarnScience").transform;
            model.SetParent(root, false);
            model.localScale = Vector3.one * length;

            var v = new ShipMeshFactory.ShipVisual
            {
                AccentColor = Color.Lerp(owner, Color.white, 0.1f),
                GlowColor = new Color(1f, 0.55f, 0.3f),
                BlinkColor = new Color(1f, 0.95f, 0.9f) * 2.2f
            };
            v.Hull = HullMaterial();
            v.Glow = ShipMeshFactory.GlowMat(v.GlowColor * 2.2f);
            v.BlinkMat = ShipMeshFactory.GlowMat(v.BlinkColor);
            // Краска фракции — свой материал у каждого корабля: цвет владельца поверх атласа, выбор подсвечивает её
            v.Accent = new Material(v.Hull) { name = "XarnScience_Accent", hideFlags = HideFlags.DontSave };
            if (v.Accent.HasProperty("_EmissionMap")) v.Accent.SetTexture("_EmissionMap", null);
            v.SetSelected(false);

            Part(model, "Hull", s_hull, v.Hull);
            Part(model, "Accent", s_accent, v.Accent);
            Part(model, "Beacons", s_beacon, v.BlinkMat);

            // Сопла — три раструба на корме (из конвертера); в координатах корня, как у остальных моделей
            var n = s_config?.nozzles;
            if (n != null && n.Length >= 3)
            {
                v.Nozzles = new Vector3[n.Length / 3];
                for (int i = 0; i < v.Nozzles.Length; i++)
                    v.Nozzles[i] = new Vector3(n[i * 3], n[i * 3 + 1], n[i * 3 + 2]) * length;
            }
            else v.Nozzles = new[] { new Vector3(0f, 0f, -0.5f) * length };
            v.NozzleRadius = Mathf.Max(0.05f, s_config != null ? s_config.nozzleRadius : 0.03f) * length;
            return v;
        }

        private static void Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            if (mesh == null) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ================================================================ Загрузка

        private static void Load()
        {
            s_loaded = true;
            var data = Resources.Load<TextAsset>(Folder + "xarn_science");
            if (data == null)
            {
                s_missing = true;
                Debug.LogWarning("[XarnScience] Нет Resources/" + Folder + "xarn_science.bytes — ксарнийские учёные летают на общем корпусе.");
                return;
            }
            try
            {
                Parse(data.bytes);
                var cfg = Resources.Load<TextAsset>(Folder + "xarn_science_config");
                if (cfg != null) s_config = JsonUtility.FromJson<Config>(cfg.text);
                s_missing = s_hull == null;
            }
            catch (Exception e)
            {
                s_missing = true;
                Debug.LogWarning("[XarnScience] Не удалось прочитать модель: " + e.Message);
            }
            Resources.UnloadAsset(data);
        }

        /// <summary>Формат PLM1 — см. PlanetModelLibrary: слои «hull», «accent», «beacon».</summary>
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

                var mesh = new Mesh { name = "XarnScience_" + name, hideFlags = HideFlags.DontSave };
                if (vc > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = pos;
                mesh.normals = nrm;
                if (uv != null) mesh.uv = uv;
                mesh.triangles = tris;
                if (uv != null) mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                if (name == "hull") s_hull = mesh;
                else if (name == "accent") s_accent = mesh;
                else if (name == "beacon") s_beacon = mesh;
            }
        }

        private static Texture2D Tex(string name) => Resources.Load<Texture2D>(Folder + name);

        /// <summary>Обшивка — одна на все корабли: URP Lit с запечёнными картами модели.</summary>
        private static Material HullMaterial()
        {
            if (s_hullMat != null) return s_hullMat;
            var m = new Material(ShaderCache.Lit) { name = "XarnScience_Hull", hideFlags = HideFlags.DontSave };
            var albedo = Tex("xarn_albedo");
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
            // Чуть светлее атласа — корабль не теряется на чёрном фоне, как и остальные модели
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(1.25f, 1.25f, 1.28f, 1f));
            var ms = Tex("xarn_ms");
            if (ms != null)
            {
                if (m.HasProperty("_MetallicGlossMap")) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                if (m.HasProperty("_OcclusionMap")) { m.SetTexture("_OcclusionMap", ms); m.EnableKeyword("_OCCLUSIONMAP"); }
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f);
            }
            var normal = Tex("xarn_normal");
            if (normal != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            var emission = Tex("xarn_emission");
            if (emission != null && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emission);
                m.SetColor("_EmissionColor", Color.white * 2.2f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            s_hullMat = m;
            return m;
        }
    }
}
