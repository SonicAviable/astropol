using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Готовая модель научного корабля — зонд «Марс-3» (Resources/Models/Ships/Mars3) вместо процедурного
    /// научного судна.
    ///
    /// Текстуры перепечены в одну развёртку (второй UV-канал исходника, импортёр меняет каналы местами —
    /// swapUVChannels в .meta): цвет атласа × запечённая AO, metallic (R) + smoothness (A), нормали.
    /// Надпись и флаг с атласа убраны — у фракций игры свои гербы.
    /// Модель Y-up: солнечные панели лежат в плоскости XZ (сверху видны во всю ширину), антенны смотрят вверх,
    /// аэрощит спускаемого аппарата — нос (+Z), двигательный блок — корма (−Z).
    /// </summary>
    public static class Mars3Model
    {
        private const string Folder = "Models/Ships/Mars3/";
        /// <summary>Наибольший размер в плане (по размаху панелей) — под коллайдер FleetView, как у процедурных судов.</summary>
        public const float DefaultSpan = 3.0f;
        /// <summary>Разворот носа, если после импорта аэрощит окажется сзади (0 или 180).</summary>
        private const float NoseYaw = 0f;

        private static GameObject s_prefab;
        private static bool s_missing;
        private static Texture2D s_albedo, s_ms, s_normal;

        /// <summary>Перезагрузка домена в Play Mode выключена — сбрасываем кэш на каждом запуске.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_prefab = null;
            s_missing = false;
            s_albedo = s_ms = s_normal = null;
        }

        public static bool Available
        {
            get
            {
                if (s_prefab == null && !s_missing)
                {
                    s_prefab = Resources.Load<GameObject>(Folder + "mars3");
                    s_missing = s_prefab == null;
                    if (s_missing) Debug.LogWarning("[Mars3] Не найдена модель Resources/" + Folder + "mars3.fbx — научные корабли остаются процедурными.");
                }
                return s_prefab != null && ShaderCache.Lit != null;
            }
        }

        /// <summary>Собрать модель под parent; null — если модели нет (тогда остаётся процедурный корпус).</summary>
        public static ShipMeshFactory.ShipVisual Build(Transform parent, Color owner, float span = DefaultSpan)
        {
            if (!Available) return null;

            var root = new GameObject("ShipModel").transform;
            root.SetParent(parent, false);
            var pivot = new GameObject("Mars3").transform;
            pivot.SetParent(root, false);
            pivot.localRotation = Quaternion.Euler(0f, NoseYaw, 0f);

            var model = Object.Instantiate(s_prefab, pivot, false);
            model.name = "Mars3Model";
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
                // Полос цвета владельца на зонде нет — выбор подсвечивает саму обшивку
                SelectTintsHull = true,
                HullEmission = new Color(0.2f, 0.21f, 0.23f)
            };
            v.Hull = Hull(owner, v.HullEmission);
            v.Glow = ShipMeshFactory.GlowMat(v.GlowColor * 2.4f);

            var mats = new Material[Mathf.Max(1, mr.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = v.Hull;
            mr.sharedMaterials = mats;

            // Размер — по наибольшему размеру в плане, модель — по центру
            var b = BoundsIn(root, mr, mf.sharedMesh.bounds);
            float size = Mathf.Max(b.size.x, b.size.z, 0.0001f);
            pivot.localScale *= span / size;
            b = BoundsIn(root, mr, mf.sharedMesh.bounds);
            pivot.localPosition -= b.center;
            b = BoundsIn(root, mr, mf.sharedMesh.bounds);

            // Маршевый двигатель — в центре кормы двигательного блока; тормозных нет
            v.Nozzles = new[] { new Vector3(0f, b.center.y - b.size.y * 0.08f, b.min.z + b.size.z * 0.04f) };
            v.NozzleRadius = Mathf.Max(0.05f, b.size.x * 0.035f);
            return v;
        }

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

        private static Texture2D Tex(ref Texture2D cache, string name)
        {
            if (cache == null) cache = Resources.Load<Texture2D>(Folder + name);
            return cache;
        }

        /// <summary>
        /// PBR-обшивка URP Lit. Материал свой у каждого корабля (выбор меняет свечение), текстуры общие.
        /// Как и у остальных кораблей — чуть светлее и со слабым собственным свечением, чтобы не теряться на чёрном.
        /// </summary>
        private static Material Hull(Color owner, Color emission)
        {
            var m = new Material(ShaderCache.Lit) { hideFlags = HideFlags.DontSave, name = "Mars3_Hull" };
            var albedo = Tex(ref s_albedo, "mars3_albedo");
            var ms = Tex(ref s_ms, "mars3_ms");
            var normal = Tex(ref s_normal, "mars3_normal");

            var tint = Color.Lerp(new Color(1.2f, 1.22f, 1.25f, 1f), owner, 0.08f);
            tint.a = 1f;
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
            else
            {
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.3f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.5f);
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
                m.SetColor("_EmissionColor", emission);
            }
            return m;
        }
    }
}
