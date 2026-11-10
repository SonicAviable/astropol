using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Научный корабль Доминиона Ксарн «Око Доминиона» (Resources/Models/Ships/XarnScience) — у научных судов ксарнийцев
    /// вместо зонда «Марс-3». Бронированный клин с раздвоенным носом и сенсорным «оком», тарелка дальнего сканирования
    /// на турели, крылья с сенсорными гондолами, двигательный блок с тремя соплами и радиаторами.
    ///
    /// Модель строит и запекает скрипт Tools/ships/xarn_science.py (Blender): xarn_science.bytes (PLM1; слои
    /// hull / accent / beacon) и xarn_science_config.json (сопла). Загрузка и материал — BakedShipAsset.
    /// Слой accent окрашен цветом владельца и подсвечивается при выборе; beacon — мигающие маяки.
    /// </summary>
    public static class XarnScienceModel
    {
        private const string Folder = "Models/Ships/XarnScience/";
        /// <summary>Длина корпуса (нос — сопла) — под коллайдер FleetView, как у остальных научных судов.</summary>
        public const float DefaultLength = 2.9f;

        [Serializable]
        private class Config { public float[] nozzles; public float nozzleRadius = 0.03f; }

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
            if (s_hullMat == null)
                s_hullMat = BakedShipAsset.HullMaterial(Folder, "xarn", "XarnScience_Hull", new Color(1.25f, 1.25f, 1.28f, 1f), 2.2f);
            v.Hull = s_hullMat;
            v.Glow = ShipMeshFactory.GlowMat(v.GlowColor * 2.2f);
            v.BlinkMat = ShipMeshFactory.GlowMat(v.BlinkColor);
            v.Accent = BakedShipAsset.AccentFrom(v.Hull, "XarnScience_Accent");
            v.SetSelected(false);

            s_meshes.TryGetValue("hull", out var hull);
            s_meshes.TryGetValue("accent", out var accent);
            s_meshes.TryGetValue("beacon", out var beacon);
            BakedShipAsset.Part(model, "Hull", hull, v.Hull);
            BakedShipAsset.Part(model, "Accent", accent, v.Accent);
            BakedShipAsset.Part(model, "Beacons", beacon, v.BlinkMat);

            // Сопла — три раструба на корме (из конвертера); в координатах корня, как у остальных моделей
            v.Nozzles = BakedShipAsset.Points(s_config?.nozzles, length) ?? new[] { new Vector3(0f, 0f, -0.5f) * length };
            v.NozzleRadius = Mathf.Max(0.05f, s_config != null ? s_config.nozzleRadius : 0.03f) * length;
            return v;
        }

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
                s_meshes = BakedShipAsset.ParsePlm(data.bytes, "XarnScience_");
                var cfg = Resources.Load<TextAsset>(Folder + "xarn_science_config");
                if (cfg != null) s_config = JsonUtility.FromJson<Config>(cfg.text);
                s_missing = !s_meshes.ContainsKey("hull");
            }
            catch (Exception e)
            {
                s_missing = true;
                Debug.LogWarning("[XarnScience] Не удалось прочитать модель: " + e.Message);
            }
            Resources.UnloadAsset(data);
        }
    }
}
