using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Научный корабль Иридийского Оракула (Resources/Models/Ships/IridiaScience): корпус-капля из перламутровых пластин
    /// на фиолетовом основании с филигранью, кристаллы-«глаза» в оправах, перистые лопасти, две гондолы двигателей.
    /// На спине — гироскоп-оракул: три вложенных кольца вокруг парящего кристалла.
    ///
    /// Модель строит и запекает скрипт Tools/ships/iridia_science.py (Blender): iridia_science.bytes (PLM1; слои
    /// hull / accent / ring0..2 / crystal / gems; кольца и кристалл — относительно центра гироскопа) и
    /// iridia_science_config.json (сопла, центр гироскопа).
    ///
    /// Анимация: внешнее кольцо крутится вокруг вертикали, среднее — вокруг продольной оси внутри внешнего,
    /// внутреннее — вокруг поперечной внутри среднего (карданный подвес); кристалл вращается, покачивается и
    /// пульсирует светом вместе с кристаллами корпуса. Фаза своя у каждого корабля — флот не мигает в такт.
    /// </summary>
    public static class IridiaScienceModel
    {
        private const string Folder = "Models/Ships/IridiaScience/";
        public const float DefaultLength = 2.9f;

        [Serializable]
        private class Config { public float[] nozzles; public float nozzleRadius = 0.017f; public float[] gyro; }

        private static Dictionary<string, Mesh> s_meshes;
        private static Material s_hullMat;
        private static Config s_config;
        private static bool s_loaded, s_missing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_meshes = null;
            s_hullMat = null;
            s_config = null;
            s_loaded = s_missing = false;
        }

        /// <summary>Владелец — Иридийский Оракул.</summary>
        public static bool IsIridia(int ownerId) => FactionRegistry.KeyOf(FactionTraits.FactionOf(ownerId)) == "iridia";

        /// <summary>Собрать модель под parent (нос по +Z); null — модели нет, остаётся обычный научный корабль.</summary>
        public static ShipMeshFactory.ShipVisual Build(Transform parent, Color owner, float length = DefaultLength)
        {
            if (!s_loaded) Load();
            if (s_missing || ShaderCache.Lit == null) return null;

            var root = new GameObject("ShipModel").transform;
            root.SetParent(parent, false);
            var model = new GameObject("IridiaScience").transform;
            model.SetParent(root, false);
            model.localScale = Vector3.one * length;

            var v = new ShipMeshFactory.ShipVisual
            {
                AccentColor = Color.Lerp(owner, Color.white, 0.1f),
                GlowColor = new Color(0.75f, 0.5f, 1f)
            };
            if (s_hullMat == null)
                s_hullMat = BakedShipAsset.HullMaterial(Folder, "iridia", "IridiaScience_Hull", new Color(1.15f, 1.15f, 1.18f, 1f), 2.0f);
            v.Hull = s_hullMat;
            v.Glow = ShipMeshFactory.GlowMat(v.GlowColor * 2.2f);
            v.Accent = BakedShipAsset.AccentFrom(v.Hull, "IridiaScience_Accent");
            v.SetSelected(false);
            // Кристаллы — своя копия обшивки у каждого корабля: пульсирует только её свечение
            var crystalMat = new Material(v.Hull) { name = "IridiaScience_Crystal", hideFlags = HideFlags.DontSave };

            BakedShipAsset.Part(model, "Hull", Layer("hull"), v.Hull);
            BakedShipAsset.Part(model, "Accent", Layer("accent"), v.Accent);
            BakedShipAsset.Part(model, "Gems", Layer("gems"), crystalMat);

            var gyro = BakedShipAsset.Points(s_config?.gyro, 1f);
            var pivot = new GameObject("Gyro").transform;
            pivot.SetParent(model, false);
            pivot.localPosition = gyro != null ? gyro[0] : new Vector3(0f, 0.15f, 0.04f);
            var ring0 = BakedShipAsset.Part(pivot, "Ring0", Layer("ring0"), v.Hull);
            var ring1 = BakedShipAsset.Part(ring0, "Ring1", Layer("ring1"), v.Hull);
            var ring2 = BakedShipAsset.Part(ring1, "Ring2", Layer("ring2"), v.Hull);
            var crystal = BakedShipAsset.Part(pivot, "Crystal", Layer("crystal"), crystalMat);

            v.Nozzles = BakedShipAsset.Points(s_config?.nozzles, length) ?? new[] { new Vector3(0f, 0f, -0.5f) * length };
            v.NozzleRadius = Mathf.Max(0.05f, s_config != null ? s_config.nozzleRadius : 0.017f) * length;

            float phase = UnityEngine.Random.value * 20f;
            bool hasEmission = crystalMat.HasProperty("_EmissionColor");
            v.Animate = t =>
            {
                if (ring0 == null) return;   // корабль уничтожен
                float x = t + phase;
                ring0.localRotation = Quaternion.Euler(0f, 60f * x, 0f);
                ring1.localRotation = Quaternion.Euler(0f, 0f, -60f * x);
                ring2.localRotation = Quaternion.Euler(120f * x, 0f, 0f);
                crystal.localRotation = Quaternion.Euler(0f, -60f * x, 0f);
                crystal.localPosition = new Vector3(0f, 0.008f * Mathf.Sin(x * 1.7f), 0f);
                if (hasEmission) crystalMat.SetColor("_EmissionColor", Color.white * (2.2f + 1.3f * Mathf.Sin(x * 2.4f)));
            };
            return v;
        }

        private static Mesh Layer(string layer) => s_meshes != null && s_meshes.TryGetValue(layer, out var m) ? m : null;

        private static void Load()
        {
            s_loaded = true;
            var data = Resources.Load<TextAsset>(Folder + "iridia_science");
            if (data == null)
            {
                s_missing = true;
                Debug.LogWarning("[IridiaScience] Нет Resources/" + Folder + "iridia_science.bytes — провидцы летают на общем корпусе.");
                return;
            }
            try
            {
                s_meshes = BakedShipAsset.ParsePlm(data.bytes, "IridiaScience_");
                var cfg = Resources.Load<TextAsset>(Folder + "iridia_science_config");
                if (cfg != null) s_config = JsonUtility.FromJson<Config>(cfg.text);
                s_missing = !s_meshes.ContainsKey("hull");
            }
            catch (Exception e)
            {
                s_missing = true;
                Debug.LogWarning("[IridiaScience] Не удалось прочитать модель: " + e.Message);
            }
            Resources.UnloadAsset(data);
        }
    }
}
