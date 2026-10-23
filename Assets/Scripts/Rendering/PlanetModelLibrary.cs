using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Детальные модели планет (Resources/Planets/Models): лавовый и огненный миры, зелёный мир с облаками,
    /// ледяной гигант с полосами атмосферы и обсидиановый мир из каменных плит.
    ///
    /// Модели вынуты из исходных GLB скриптом Tools/planets/export_planets.py в planets.bytes (формат PLM1:
    /// оси Unity, полюс вверх, радиус поверхности 1) и planets_config.json (материалы и какие типы планет они заменяют).
    /// Каждая модель — вариант своего типа: по имени планеты (стабильно) часть раскалённых, континентальных,
    /// газовых и безжизненных миров получает модель, остальные остаются прежними (Земля, Марс, Луна, процедурные).
    /// Поверхность — URP Lit (альбедо, металл/гладкость, нормали, свечение), облака и полосы — шейдер облаков,
    /// дымка — шейдер атмосферы; оболочки освещаются со стороны звезды (PlanetSunLink).
    /// </summary>
    public static class PlanetModelLibrary
    {
        private const string Folder = "Planets/Models/";
        private const int Variants = 6;   // 3 оттенка × 2 стороны

        [Serializable]
        private class Config { public List<ModelDef> models = new List<ModelDef>(); }

        [Serializable]
        private class ModelDef
        {
            public string key, title;
            public List<string> types = new List<string>();
            public List<float> weights = new List<float>();
            public float smoothness, metallic, emissionIntensity, normalScale = 1f;
            public float[] albedoTint, emission, cloudsTint, atmoColor;
            public float cloudsOpacity, cloudsShell = 1.01f, atmoIntensity, atmoShell = 1.05f;
            public string albedo, ms, emissionMap, normal, clouds;
        }

        private static Config s_config;
        private static readonly Dictionary<string, Mesh> s_meshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, GameObject> s_templates = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Material> s_materials = new Dictionary<string, Material>();
        private static Transform s_root;
        private static bool s_loaded, s_missing;

        /// <summary>Перезагрузка домена в Play Mode выключена — сбрасываем кэш на каждом запуске.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_config = null;
            s_meshes.Clear();
            s_templates.Clear();
            s_materials.Clear();
            s_root = null;
            s_loaded = s_missing = false;
        }

        /// <summary>Шаблон планеты для Instantiate или null — модель этой планете не досталась (или файлов нет).</summary>
        public static GameObject Template(PlanetData planet)
        {
            if (planet == null || ShaderCache.Lit == null) return null;
            if (!s_loaded) Load();
            if (s_missing || s_config == null) return null;

            var def = Pick(planet);
            if (def == null) return null;
            int variant = (int)(StableHash(planet.Name + "#v") % Variants);
            string id = def.key + "_" + variant;
            if (s_templates.TryGetValue(id, out var t) && t != null) return t;
            t = Build(def, variant);
            if (t != null) s_templates[id] = t;
            return t;
        }

        /// <summary>Какая модель достанется планете: доля планет типа по весам моделей, остальным — ничего.</summary>
        private static ModelDef Pick(PlanetData planet)
        {
            string type = planet.Type.ToString();
            float r = (StableHash(planet.Name + "#m") % 10000) / 10000f;
            float acc = 0f;
            foreach (var m in s_config.models)
            {
                int i = m.types.IndexOf(type);
                if (i < 0 || i >= m.weights.Count) continue;
                acc += m.weights[i];
                if (r < acc) return m;
            }
            return null;
        }

        private static uint StableHash(string s)
        {
            uint h = 2166136261;
            if (s != null) foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        // ================================================================ Загрузка

        private static void Load()
        {
            s_loaded = true;
            var json = Resources.Load<TextAsset>(Folder + "planets_config");
            var data = Resources.Load<TextAsset>(Folder + "planets");
            if (json == null || data == null)
            {
                s_missing = true;
                Debug.LogWarning("[PlanetModels] Нет Resources/" + Folder + "planets_config.json или planets.bytes — планеты остаются прежними.");
                return;
            }
            try
            {
                s_config = JsonUtility.FromJson<Config>(json.text);
                Parse(data.bytes);
            }
            catch (Exception e)
            {
                s_missing = true;
                Debug.LogWarning("[PlanetModels] Не удалось прочитать модели: " + e.Message);
            }
            Resources.UnloadAsset(data);
        }

        /// <summary>
        /// Формат PLM1 (little-endian): число слоёв; у слоя — имя («ключ/слой»), число вершин и индексов, есть ли UV;
        /// позиции float×3, нормали int8×3 (/127), UV float×2 (если есть), треугольники uint32.
        /// </summary>
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

                var mesh = new Mesh { name = "PlanetModel_" + name, hideFlags = HideFlags.DontSave };
                if (vc > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = pos;
                mesh.normals = nrm;
                if (uv != null) mesh.uv = uv;
                mesh.triangles = tris;
                if (uv != null) mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                s_meshes[name] = mesh;
            }
        }

        // ================================================================ Сборка

        private static Mesh MeshOf(string key, string layer) => s_meshes.TryGetValue(key + "/" + layer, out var m) ? m : null;

        private static GameObject Build(ModelDef def, int variant)
        {
            var surface = MeshOf(def.key, "surface") ?? MeshOf(def.key, "surface0");
            if (surface == null) return null;
            if (s_root == null)
            {
                var holder = new GameObject("[PlanetModelTemplates]");
                holder.SetActive(false);   // шаблоны не рисуются; клоны — активны
                UnityEngine.Object.DontDestroyOnLoad(holder);
                s_root = holder.transform;
            }

            var go = new GameObject("ModelPlanet_" + def.key + "_" + variant);
            go.transform.SetParent(s_root, false);
            go.transform.localRotation = Quaternion.Euler(0f, variant * 151f, 0f);   // своя «сторона» к камере
            var surfaceMat = Surface(def, variant);
            go.AddComponent<MeshFilter>().sharedMesh = surface;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = surfaceMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Остальные части поверхности (каменные плиты обсидианового мира)
            for (int i = 1; i < 8; i++)
            {
                var part = MeshOf(def.key, "surface" + i);
                if (part == null) break;
                var p = new GameObject("Surface" + i);
                p.transform.SetParent(go.transform, false);
                p.AddComponent<MeshFilter>().sharedMesh = part;
                var pr = p.AddComponent<MeshRenderer>();
                pr.sharedMaterial = surfaceMat;
                pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var cloudsMesh = MeshOf(def.key, "clouds");
            if (cloudsMesh != null && !string.IsNullOrEmpty(def.clouds) && def.cloudsOpacity > 0f)
            {
                var c = Shell(go.transform, "Clouds", def.cloudsShell, cloudsMesh, Clouds(def));
                c.AddComponent<PlanetCloudSpin>();
            }
            if (def.atmoIntensity > 0f)
                Shell(go.transform, "Atmosphere", def.atmoShell, RealPlanetModel.UnitSphere, Atmosphere(def));

            go.AddComponent<PlanetSunLink>();
            return go;
        }

        private static GameObject Shell(Transform parent, string name, float scale, Mesh mesh, Material mat)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localScale = Vector3.one * scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return g;
        }

        // ================================================================ Материалы

        private static Texture2D Tex(ModelDef def, string name)
            => string.IsNullOrEmpty(name) ? null : Resources.Load<Texture2D>(Folder + def.key + "/" + name);

        private static Color Col(float[] c, Color fallback)
            => c != null && c.Length >= 3 ? new Color(c[0], c[1], c[2], 1f) : fallback;

        /// <summary>Оттенки вариантов: исходный, теплее, холоднее.</summary>
        private static Color Tint(int variant)
        {
            int t = variant % 3;
            return t == 0 ? Color.white : t == 1 ? new Color(1.06f, 1.0f, 0.9f) : new Color(0.9f, 0.97f, 1.06f);
        }

        /// <summary>URP Lit: альбедо, металл/гладкость (R/A), нормали, свечение (лава, трещины, огни).</summary>
        private static Material Surface(ModelDef def, int variant)
        {
            string id = def.key + "_surface_" + (variant % 3);
            if (s_materials.TryGetValue(id, out var cached) && cached != null) return cached;

            var m = new Material(ShaderCache.Lit) { name = "PlanetModel_" + def.key, hideFlags = HideFlags.DontSave };
            var tint = Col(def.albedoTint, Color.white) * Tint(variant);
            tint.a = 1f;
            var albedo = Tex(def, def.albedo);
            if (albedo != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", def.metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", def.smoothness);

            var ms = Tex(def, def.ms);
            if (ms != null && m.HasProperty("_MetallicGlossMap"))
            {
                // С картой гладкость берётся из её альфы (× _Smoothness), металл — из красного канала
                m.SetTexture("_MetallicGlossMap", ms);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Clamp01(def.smoothness * 2f));
            }

            var normal = Tex(def, def.normal);
            if (normal != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
                if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", def.normalScale);
            }

            var emission = Tex(def, def.emissionMap);
            if (emission != null && def.emissionIntensity > 0f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emission);
                m.SetColor("_EmissionColor", Col(def.emission, Color.white) * def.emissionIntensity);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            s_materials[id] = m;
            return m;
        }

        private static Material Clouds(ModelDef def)
        {
            string id = def.key + "_clouds";
            if (s_materials.TryGetValue(id, out var cached) && cached != null) return cached;
            var sh = Resources.Load<Shader>("Shaders/PlanetClouds");
            if (sh == null) sh = Shader.Find("Astropolity/Planet/Clouds");
            var m = new Material(sh) { name = "PlanetModel_Clouds_" + def.key, hideFlags = HideFlags.DontSave };
            m.SetTexture("_MainTex", Tex(def, def.clouds));
            m.SetColor("_Color", Col(def.cloudsTint, Color.white));
            m.SetFloat("_Opacity", def.cloudsOpacity);
            s_materials[id] = m;
            return m;
        }

        private static Material Atmosphere(ModelDef def)
        {
            string id = def.key + "_atmo";
            if (s_materials.TryGetValue(id, out var cached) && cached != null) return cached;
            var sh = Resources.Load<Shader>("Shaders/PlanetAtmosphere");
            if (sh == null) sh = Shader.Find("Astropolity/Planet/Atmosphere");
            var m = new Material(sh) { name = "PlanetModel_Atmo_" + def.key, hideFlags = HideFlags.DontSave };
            m.SetColor("_Color", Col(def.atmoColor, new Color(0.35f, 0.65f, 1f)));
            m.SetFloat("_Intensity", def.atmoIntensity);
            m.SetFloat("_ShellRadius", def.atmoShell);
            s_materials[id] = m;
            return m;
        }
    }
}
