using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Готовая модель левиафана (Resources/Models/Leviathan) — у флотов-угроз «Левиафан» вместо корабельного корпуса.
    ///
    /// FBX из Maya: скелет (позвоночник, хвост, щупальца), Y вверх, голова по +Z; одна анимация плавания на месте
    /// (~2 с, только повороты костей) — она крутится по кругу, неспешно. Ссылок на текстуры в FBX нет, поэтому
    /// материал собирается здесь: альбедо, нормали, металличность/гладкость (R/A) и свечение.
    /// Двигателей у существа нет — факелы и ионный след флоту не нужны (<see cref="ShipMeshFactory.ShipVisual.Organic"/>).
    /// Ориентация проверяется по кости головы: она должна оказаться спереди (+Z). Длина подгоняется под карту.
    /// </summary>
    public static class LeviathanModel
    {
        private const string Folder = "Models/Leviathan/";
        /// <summary>Длина на карте: заметно крупнее эсминца (≈3.6) — это чудовище, а не корабль.</summary>
        public const float DefaultLength = 7f;
        /// <summary>Скорость анимации плавания (1 — как в файле).</summary>
        private const float SwimSpeed = 0.55f;

        private static GameObject s_prefab;
        private static AnimationClip s_clip;
        private static Material s_mat;
        private static bool s_missing;

        public static bool Available
        {
            get
            {
                if (s_prefab == null && !s_missing)
                {
                    s_prefab = Resources.Load<GameObject>(Folder + "leviathan");
                    s_missing = s_prefab == null;
                    if (!s_missing)
                        foreach (var c in Resources.LoadAll<AnimationClip>(Folder + "leviathan"))
                            if (c != null && !c.name.StartsWith("__preview__")) { s_clip = c; break; }
                }
                return s_prefab != null && ShaderCache.Lit != null;
            }
        }

        /// <summary>Собрать модель под parent; null — если модели нет (тогда остаётся корабельный корпус).</summary>
        public static ShipMeshFactory.ShipVisual Build(Transform parent, float length = DefaultLength)
        {
            if (!Available) return null;

            var root = new GameObject("ShipModel").transform;
            root.SetParent(parent, false);
            var pivot = new GameObject("Leviathan").transform;
            pivot.SetParent(root, false);

            var model = Object.Instantiate(s_prefab, pivot, false);
            model.name = "LeviathanModel";
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            var animator = model.GetComponent<Animator>();
            if (animator != null) animator.enabled = false;   // анимацию ведём сами — без контроллера

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Object.Destroy(root.gameObject);
                return null;
            }
            var mat = SharedMaterial();
            foreach (var r in renderers)
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;   // хвост и щупальца выходят за исходные границы
            }

            if (s_clip != null) s_clip.SampleAnimation(model, 0f);
            Orient(root, pivot, model.transform, renderers, length);

            var v = new ShipMeshFactory.ShipVisual
            {
                Organic = true,
                AccentColor = new Color(0.55f, 0.9f, 1f),
                GlowColor = new Color(0.55f, 0.9f, 1f),
                Hull = mat
            };
            if (s_clip != null)
            {
                var clip = s_clip;
                float phase = Random.value * clip.length;   // у разных левиафанов — своя фаза
                v.Animate = t =>
                {
                    if (model != null) clip.SampleAnimation(model, Mathf.Repeat(t * SwimSpeed + phase, clip.length));
                };
            }
            return v;
        }

        // ================================================================ Ориентация и размер

        private static void Orient(Transform root, Transform pivot, Transform model, Renderer[] renderers, float length)
        {
            var b = BoundsIn(root, renderers);
            // Голова должна быть спереди (+Z); если кость головы оказалась сзади — разворачиваем
            var head = FindDeep(model, "head");
            if (head != null && root.InverseTransformPoint(head.position).z < b.center.z)
            {
                pivot.localRotation = Quaternion.Euler(0f, 180f, 0f) * pivot.localRotation;
                b = BoundsIn(root, renderers);
            }

            float len = Mathf.Max(b.size.z, 0.0001f);
            pivot.localScale *= length / len;
            b = BoundsIn(root, renderers);
            pivot.localPosition -= b.center;
        }

        private static Bounds BoundsIn(Transform root, Renderer[] renderers)
        {
            bool any = false;
            var b = new Bounds();
            var m = root.worldToLocalMatrix;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var wb = r.bounds;
                Vector3 c = wb.center, e = wb.extents;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        // ================================================================ Материал

        private static Texture2D Tex(string name) => Resources.Load<Texture2D>(Folder + name);

        /// <summary>URP Lit: альбедо, металличность (R) + гладкость (A), нормали, свечение (прожилки и глаза).</summary>
        private static Material SharedMaterial()
        {
            if (s_mat != null) return s_mat;
            var m = new Material(ShaderCache.Lit) { name = "Leviathan", hideFlags = HideFlags.DontSave };
            var albedo = Tex("lev_albedo");
            var ms = Tex("lev_ms");
            var normal = Tex("lev_normal");
            var emission = Tex("lev_emission");

            // Чуть светлее файла — существо не теряется на чёрном фоне космоса (как и корабли)
            var tint = new Color(1.2f, 1.2f, 1.25f, 1f);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);

            if (ms != null && m.HasProperty("_MetallicGlossMap"))
            {
                m.SetTexture("_MetallicGlossMap", ms);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 1f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.8f);
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
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                if (emission != null && m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emission);
                m.SetColor("_EmissionColor", emission != null ? Color.white * 1.6f : new Color(0.12f, 0.13f, 0.15f));
            }
            s_mat = m;
            return m;
        }
    }
}
