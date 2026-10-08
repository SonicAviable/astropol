using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Готовая модель линейного крейсера «Харбингер» (Resources/Models/Ships/Harbinger) — тяжёлый
    /// военный корпус вместо процедурного силуэта.
    ///
    /// В FBX нет ссылок на текстуры, поэтому материалы собираются здесь по именам слотов:
    ///   • обшивка, техника, двигатели — трим-листы 011…032 (albedo + metallic/smoothness + нормали 01…03);
    ///   • yellowDecal — полосы цвета владельца (они же подсвечиваются при выборе флота);
    ///   • огни, окна и сопла — светятся сами; белый маяк мигает.
    /// Ориентация проверяется по самой модели: сопла должны оказаться сзади (−Z),
    /// красный бортовой огонь — слева. Длина подгоняется под остальные корабли карты.
    /// </summary>
    public static class HarbingerModel
    {
        private const string Folder = "Models/Ships/Harbinger/";
        /// <summary>Длина корабля в единицах карты (процедурный эсминец ≈ 3.5).</summary>
        public const float Length = 3.6f;

        private static GameObject s_prefab;
        private static bool s_missing;
        private static readonly Dictionary<string, Material> s_shared = new Dictionary<string, Material>();

        public static bool Available
        {
            get
            {
                if (s_prefab == null && !s_missing)
                {
                    s_prefab = Resources.Load<GameObject>(Folder + "harb");
                    s_missing = s_prefab == null;
                }
                return s_prefab != null && ShaderCache.Lit != null;
            }
        }

        /// <summary>Собрать модель под parent; null — если модели нет (тогда остаётся процедурный корпус).</summary>
        public static ShipMeshFactory.ShipVisual Build(Transform parent, Color owner)
        {
            if (!Available) return null;

            var root = new GameObject("ShipModel").transform;
            root.SetParent(parent, false);
            // Модель из 3ds Max: после импорта нос смотрит по −X — поворачиваем носом на +Z
            var pivot = new GameObject("Harbinger").transform;
            pivot.SetParent(root, false);
            pivot.localRotation = Quaternion.Euler(0f, 90f, 0f);

            var model = Object.Instantiate(s_prefab, pivot, false);
            model.name = "HarbingerModel";
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);

            var mr = model.GetComponentInChildren<MeshRenderer>();
            var mf = mr != null ? mr.GetComponent<MeshFilter>() : null;
            if (mr == null || mf == null || mf.sharedMesh == null)
            {
                Object.Destroy(root.gameObject);
                return null;
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var v = new ShipMeshFactory.ShipVisual
            {
                AccentColor = Color.Lerp(owner, Color.white, 0.1f),
                GlowColor = new Color(0.55f, 0.9f, 1f),
                BlinkColor = new Color(1f, 0.95f, 0.9f) * 2.2f
            };
            v.Accent = ShipMeshFactory.LitMat(v.AccentColor, v.AccentColor * 0.45f, 0.3f, 0.6f);
            v.Glow = ShipMeshFactory.GlowMat(v.GlowColor * 2.4f);
            v.BlinkMat = ShipMeshFactory.GlowMat(v.BlinkColor);

            var src = mr.sharedMaterials;
            var mats = new Material[src.Length];
            int engineSub = -1, redSub = -1;
            for (int i = 0; i < src.Length; i++)
            {
                string n = src[i] != null ? src[i].name : "";
                if (n.StartsWith("MainEngineGlow")) engineSub = i;
                if (n.StartsWith("redLight")) redSub = i;
                mats[i] = MaterialFor(n, v);
            }
            mr.sharedMaterials = mats;
            v.Hull = mats.Length > 0 ? mats[0] : null;

            Orient(root, pivot, mr, mf.sharedMesh, engineSub, redSub);
            return v;
        }

        // ================================================================ Ориентация и размер

        private static void Orient(Transform root, Transform pivot, Renderer mr, Mesh mesh, int engineSub, int redSub)
        {
            try
            {
                if (engineSub >= 0 && engineSub < mesh.subMeshCount && SubCenter(root, mr, mesh, engineSub).z > 0f)
                    pivot.localRotation = Quaternion.Euler(0f, 180f, 0f) * pivot.localRotation;
                // Красный огонь — по левому борту (−X); иначе модель перевёрнута — крутим вокруг продольной оси
                if (redSub >= 0 && redSub < mesh.subMeshCount && SubCenter(root, mr, mesh, redSub).x > 0f)
                    pivot.localRotation = Quaternion.Euler(0f, 0f, 180f) * pivot.localRotation;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Harbinger] Не удалось проверить ориентацию модели: " + e.Message);
            }

            var b = BoundsIn(root, mr, mesh.bounds);
            float len = Mathf.Max(b.size.z, 0.0001f);
            pivot.localScale *= Length / len;
            b = BoundsIn(root, mr, mesh.bounds);
            pivot.localPosition -= b.center;
        }

        private static Vector3 SubCenter(Transform root, Renderer mr, Mesh mesh, int sub)
            => (root.worldToLocalMatrix * mr.localToWorldMatrix).MultiplyPoint3x4(mesh.GetSubMesh(sub).bounds.center);

        private static Bounds BoundsIn(Transform root, Renderer mr, Bounds local)
        {
            var m = root.worldToLocalMatrix * mr.localToWorldMatrix;
            Vector3 c = local.center, e = local.extents;
            var b = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                b.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return b;
        }

        // ================================================================ Материалы

        private static Material MaterialFor(string slot, ShipMeshFactory.ShipVisual v)
        {
            switch (slot)
            {
                case "panels":      return Shared(slot, () => Pbr("011", "01"));
                case "panelsDark":  return Shared(slot, () => Pbr("012", "01"));
                case "tech":        return Shared(slot, () => Pbr("021", "02"));
                case "techDark":    return Shared(slot, () => Pbr("022", "02"));
                case "techLight":   return Shared(slot, () => Pbr("023", "02"));
                case "engine":      return Shared(slot, () => Pbr("031", "03"));
                case "engineDark":  return Shared(slot, () => Pbr("032", "03"));

                case "yellowDecal": return v.Accent;
                case "MainEngineGlow": return v.Glow;
                case "whiteLight":  return v.BlinkMat;
                case "RevEngineGlow": return Shared(slot, () => ShipMeshFactory.GlowMat(new Color(0.55f, 0.9f, 1f) * 0.6f));
                case "greenLight":  return Shared(slot, () => ShipMeshFactory.GlowMat(new Color(0.3f, 1f, 0.45f) * 2f));
                case "redLight":    return Shared(slot, () => ShipMeshFactory.GlowMat(new Color(1f, 0.25f, 0.2f) * 2f));
                case "windows":     return Shared(slot, () => ShipMeshFactory.GlowMat(new Color(1f, 0.85f, 0.6f) * 1.4f));
                case "whiteDecal":  return Shared(slot, () => ShipMeshFactory.LitMat(new Color(0.85f, 0.87f, 0.9f), new Color(0.05f, 0.05f, 0.06f), 0.2f, 0.5f));
                case "redDecal":    return Shared(slot, () => ShipMeshFactory.LitMat(new Color(0.75f, 0.12f, 0.1f), new Color(0.08f, 0.01f, 0.01f), 0.2f, 0.5f));
                case "nameDecal":   return Shared(slot, NameDecal);
                default:            return Shared("hull", () => ShipMeshFactory.LitMat(new Color(0.5f, 0.54f, 0.58f), new Color(0.05f, 0.05f, 0.06f), 0.4f, 0.5f));
            }
        }

        private static Material Shared(string key, System.Func<Material> make)
        {
            if (s_shared.TryGetValue(key, out var m) && m != null) return m;
            m = make();
            m.name = "Harbinger_" + key;
            s_shared[key] = m;
            return m;
        }

        private static Texture2D Tex(string name) => Resources.Load<Texture2D>(Folder + name);

        /// <summary>PBR-обшивка URP Lit: albedo, metallic (R) + smoothness (A), нормали.</summary>
        private static Material Pbr(string set, string normalSet)
        {
            var m = new Material(ShaderCache.Lit) { hideFlags = HideFlags.DontSave };
            var albedo = Tex(set + "_albedo");
            var ms = Tex(set + "_ms");
            var normal = Tex(normalSet + "_normal");

            // Текстуры тёмные — чуть осветляем и даём слабое собственное свечение,
            // чтобы корабль не терялся на чёрном фоне (как и процедурные корпуса)
            var tint = new Color(1.35f, 1.38f, 1.42f, 1f);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);

            if (ms != null && m.HasProperty("_MetallicGlossMap"))
            {
                m.SetTexture("_MetallicGlossMap", ms);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 1f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.9f);
            }
            if (normal != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
                if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 1f);
            }
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", albedo);
                m.SetColor("_EmissionColor", new Color(0.22f, 0.24f, 0.27f));
            }
            return m;
        }

        /// <summary>Надпись с именем корабля: белые буквы по альфе names.png, остальное отсечено.</summary>
        private static Material NameDecal()
        {
            var m = ShipMeshFactory.LitMat(new Color(0.9f, 0.92f, 0.95f), new Color(0.15f, 0.15f, 0.16f), 0.1f, 0.4f);
            var tex = Tex("names");
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            return m;
        }
    }
}
