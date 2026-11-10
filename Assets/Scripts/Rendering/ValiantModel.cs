using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Эсминец Доминиона Ксарн «Валиант» (Resources/Models/Ships/Valiant) — у ксарнийских эсминцев вместо корпуса
    /// Star Sparrow. Тёмный бронированный корпус, десять орудийных башен, оранжевые огни и три маршевых сопла.
    ///
    /// Модель запекает Tools/ships/export_valiant.py (Blender) из Valiant.fbx: поза покоя скелета, упрощённая сетка,
    /// valiant.bytes (PLM1; слои по материалам: hull / accent / dark / steel / glow / glass) и valiant_config.json (сопла).
    /// В исходнике материалы однотонные, без текстур, — цвета задаются здесь (чуть светлее оригинала, чтобы корабль
    /// не терялся на чёрном фоне). Слой accent — вторичная броня с оттенком цвета владельца, подсвечивается при выборе.
    /// </summary>
    public static class ValiantModel
    {
        private const string Folder = "Models/Ships/Valiant/";
        /// <summary>Длина корпуса — как у остальных эсминцев (корпус узкий, укорачивать не нужно).</summary>
        public const float DefaultLength = 3.6f;

        [Serializable]
        private class Config { public float[] nozzles; public float nozzleRadius = 0.015f; }

        private static Dictionary<string, Mesh> s_meshes;
        private static Config s_config;
        private static bool s_loaded, s_missing;
        private static readonly Dictionary<string, Material> s_shared = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_meshes = null;
            s_config = null;
            s_loaded = s_missing = false;
            s_shared.Clear();
        }

        /// <summary>Эсминец Доминиона Ксарн.</summary>
        public static bool Applies(ShipClass hull, int ownerId) => hull == ShipClass.Destroyer && XarnScienceModel.IsXarn(ownerId);

        /// <summary>Собрать модель под parent (нос по +Z); null — модели нет, остаётся корпус Star Sparrow.</summary>
        public static ShipMeshFactory.ShipVisual Build(Transform parent, Color owner, float length = DefaultLength)
        {
            if (!s_loaded) Load();
            if (s_missing || ShaderCache.Lit == null) return null;

            var root = new GameObject("ShipModel").transform;
            root.SetParent(parent, false);
            var model = new GameObject("Valiant").transform;
            model.SetParent(root, false);
            model.localScale = Vector3.one * length;

            var v = new ShipMeshFactory.ShipVisual
            {
                AccentColor = Color.Lerp(new Color(0.42f, 0.45f, 0.5f), owner, 0.55f),
                GlowColor = new Color(1f, 0.55f, 0.3f),
                BlinkColor = new Color(1f, 0.95f, 0.9f) * 2.2f
            };
            v.Hull = Shared("hull", () => Metal(new Color(0.30f, 0.33f, 0.37f), 0.65f, 0.6f));
            v.Accent = ShipMeshFactory.LitMat(v.AccentColor, v.AccentColor * 0.45f, 0.6f, 0.6f);
            TwoSided(v.Accent);
            v.Glow = Shared("glow", () => ShipMeshFactory.GlowMat(new Color(1f, 0.4f, 0.08f) * 2.2f));
            v.BlinkMat = ShipMeshFactory.GlowMat(v.BlinkColor);
            v.SetSelected(false);

            Part(model, "hull", v.Hull);
            Part(model, "accent", v.Accent);
            Part(model, "dark", Shared("dark", () => TwoSided(Metal(new Color(0.12f, 0.13f, 0.15f), 0.75f, 0.55f))));
            Part(model, "steel", Shared("steel", () => Metal(new Color(0.44f, 0.47f, 0.52f), 0.85f, 0.7f)));
            Part(model, "glow", v.Glow);
            Part(model, "glass", Shared("glass", () => ShipMeshFactory.GlowMat(new Color(1f, 0.5f, 0.15f) * 1.4f)));

            // Сопла — три раструба на корме (из конвертера); в координатах корня, как у остальных моделей
            v.Nozzles = BakedShipAsset.Points(s_config?.nozzles, length) ?? new[] { new Vector3(0f, -0.065f, -0.5f) * length };
            v.NozzleRadius = Mathf.Max(0.04f, (s_config != null ? s_config.nozzleRadius : 0.015f) * length);
            return v;
        }

        private static void Part(Transform model, string layer, Material mat)
        {
            if (s_meshes.TryGetValue(layer, out var mesh)) BakedShipAsset.Part(model, layer, mesh, mat);
        }

        /// <summary>Однотонный металл URP Lit со слабым собственным свечением.</summary>
        private static Material Metal(Color c, float metallic, float smooth)
            => ShipMeshFactory.LitMat(c, c * 0.18f, metallic, smooth);

        /// <summary>Тонкие пластины и вырезы после упрощения сетки видны с обеих сторон.</summary>
        private static Material TwoSided(Material m)
        {
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            return m;
        }

        private static Material Shared(string key, Func<Material> make)
        {
            if (s_shared.TryGetValue(key, out var m) && m != null) return m;
            m = make();
            m.name = "Valiant_" + key;
            s_shared[key] = m;
            return m;
        }

        private static void Load()
        {
            s_loaded = true;
            var data = Resources.Load<TextAsset>(Folder + "valiant");
            if (data == null)
            {
                s_missing = true;
                Debug.LogWarning("[Valiant] Нет Resources/" + Folder + "valiant.bytes — ксарнийские эсминцы летают на корпусе Star Sparrow.");
                return;
            }
            try
            {
                s_meshes = BakedShipAsset.ParsePlm(data.bytes, "Valiant_");
                var cfg = Resources.Load<TextAsset>(Folder + "valiant_config");
                if (cfg != null) s_config = JsonUtility.FromJson<Config>(cfg.text);
                s_missing = !s_meshes.ContainsKey("hull");
            }
            catch (Exception e)
            {
                s_missing = true;
                Debug.LogWarning("[Valiant] Не удалось прочитать модель: " + e.Message);
            }
            Resources.UnloadAsset(data);
        }
    }
}
