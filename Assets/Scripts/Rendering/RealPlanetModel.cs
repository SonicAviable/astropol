using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Фотореалистичные планеты по настоящим картам (Resources/Planets/Real): материковые — Земля
    /// (облака, огни городов на ночной стороне, голубая атмосфера, блестящий океан), пустынные — Марс
    /// (рыжая дымка), безжизненные — Луна. Раскалённые и газовые гиганты остаются процедурными.
    ///
    /// Шаблоны собираются один раз и дальше клонируются, как префабы (SystemViewManager, PlanetHoloStudio).
    /// Сфера своя: равнопромежуточная развёртка (u — долгота, v — широта), радиус 1 — как у процедурных
    /// префабов. Чтобы одинаковые типы не были близнецами, у каждой планеты свой вариант: зеркальная или
    /// перевёрнутая карта (отдельный меш — касательные считаются заново, рельеф не выворачивается), оттенок
    /// и начальный поворот. Вариант — по имени планеты, поэтому в системе и на экране колонии она одна и та же.
    /// </summary>
    public static class RealPlanetModel
    {
        private const string Folder = "Planets/Real/";
        private const int Variants = 12;   // 4 развёртки × 3 оттенка

        private enum Kind { Earth, Mars, Moon }

        private static Transform s_root;
        private static readonly Mesh[] s_spheres = new Mesh[4];
        private static readonly Dictionary<int, GameObject> s_templates = new Dictionary<int, GameObject>();
        private static readonly Dictionary<int, Material> s_surface = new Dictionary<int, Material>();
        private static Material s_clouds, s_lights, s_atmoEarth, s_atmoMars;
        private static readonly Dictionary<Kind, bool> s_available = new Dictionary<Kind, bool>();

        /// <summary>Шаблон планеты для Instantiate или null — тогда остаётся процедурный префаб.</summary>
        public static GameObject Template(PlanetData planet)
        {
            if (planet == null || ShaderCache.Lit == null) return null;
            Kind kind;
            switch (planet.Type)
            {
                case PlanetType.Continental: kind = Kind.Earth; break;
                case PlanetType.Desert: kind = Kind.Mars; break;
                case PlanetType.Barren: kind = Kind.Moon; break;
                default: return null;
            }
            if (!Available(kind)) return null;

            int variant = (int)(StableHash(planet.Name) % Variants);
            int key = (int)kind * 64 + variant;
            if (s_templates.TryGetValue(key, out var t) && t != null) return t;
            t = Build(kind, variant);
            s_templates[key] = t;
            return t;
        }

        private static readonly Dictionary<PlanetType, Texture2D> s_icons = new Dictionary<PlanetType, Texture2D>();

        /// <summary>
        /// Маленькая читаемая развёртка (256×128, у Земли — с облаками) для шариков планет на карте и в подсказках;
        /// null — у типа нет настоящей карты (раскалённые, газовые гиганты) или файла нет.
        /// </summary>
        public static Texture2D IconSource(PlanetType type)
        {
            if (s_icons.TryGetValue(type, out var t)) return t;
            string n = type == PlanetType.Continental ? "earth" : type == PlanetType.Desert ? "mars" : type == PlanetType.Barren ? "moon" : null;
            t = n != null ? Tex(n + "_icon") : null;
            if (t != null && !t.isReadable) t = null;   // без Read/Write шарик не нарисовать — останется процедурный
            s_icons[type] = t;
            return t;
        }

        /// <summary>Цвет дымки по краю шарика-иконки.</summary>
        public static Color IconAtmosphere(PlanetType type) => type == PlanetType.Continental ? new Color(0.4f, 0.68f, 1f)
            : type == PlanetType.Desert ? new Color(1f, 0.62f, 0.42f) : new Color(0.55f, 0.56f, 0.6f);

        private static bool Available(Kind kind)
        {
            if (s_available.TryGetValue(kind, out bool ok)) return ok;
            ok = Tex(Prefix(kind) + "_albedo") != null;
            if (!ok) Debug.LogWarning("[RealPlanet] Нет текстур " + Folder + Prefix(kind) + "_albedo — остаётся процедурная планета.");
            s_available[kind] = ok;
            return ok;
        }

        private static string Prefix(Kind k) => k == Kind.Earth ? "earth" : k == Kind.Mars ? "mars" : "moon";

        private static uint StableHash(string s)
        {
            uint h = 2166136261;
            if (s != null) foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        // ================================================================ Сборка

        private static GameObject Build(Kind kind, int variant)
        {
            if (s_root == null)
            {
                var holder = new GameObject("[RealPlanetTemplates]");
                holder.SetActive(false);   // шаблоны не рисуются и не обновляются; клоны — активны
                Object.DontDestroyOnLoad(holder);
                s_root = holder.transform;
            }

            var go = new GameObject("RealPlanet_" + kind + "_" + variant);
            go.transform.SetParent(s_root, false);
            go.transform.localRotation = Quaternion.Euler(0f, variant * 137.5f, 0f);   // своя «сторона» к камере
            go.AddComponent<MeshFilter>().sharedMesh = Sphere(variant & 3);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Surface(kind, variant);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (kind == Kind.Earth)
            {
                Shell(go.transform, "NightLights", 1.002f, Lights(), variant & 3);
                var clouds = Shell(go.transform, "Clouds", 1.009f, Clouds(), (variant + 1) & 3);
                clouds.AddComponent<PlanetCloudSpin>();
                Shell(go.transform, "Atmosphere", 1.06f, AtmoEarth(), 0);
            }
            else if (kind == Kind.Mars)
            {
                Shell(go.transform, "Atmosphere", 1.035f, AtmoMars(), 0);
            }
            go.AddComponent<PlanetSunLink>();
            return go;
        }

        private static GameObject Shell(Transform parent, string name, float scale, Material mat, int sphere)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localScale = Vector3.one * scale;
            g.AddComponent<MeshFilter>().sharedMesh = Sphere(sphere);
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return g;
        }

        // ================================================================ Материалы

        private static Texture2D Tex(string name) => Resources.Load<Texture2D>(Folder + name);

        /// <summary>Оттенки вариантов: исходный, теплее/суше, холоднее.</summary>
        private static Color Tint(Kind kind, int variant)
        {
            int t = (variant >> 2) % 3;
            switch (kind)
            {
                case Kind.Earth: return t == 0 ? Color.white : t == 1 ? new Color(1.06f, 1.0f, 0.88f) : new Color(0.9f, 1.0f, 1.06f);
                case Kind.Mars:  return t == 0 ? Color.white : t == 1 ? new Color(1.08f, 0.92f, 0.78f) : new Color(0.92f, 0.86f, 0.86f);
                default:         return t == 0 ? Color.white : t == 1 ? new Color(1.05f, 1.0f, 0.92f) : new Color(0.9f, 0.94f, 1.0f);
            }
        }

        /// <summary>URP Lit: альбедо, нормали; у Земли гладкость — в альфе альбедо (океан блестит, суша матовая).</summary>
        private static Material Surface(Kind kind, int variant)
        {
            int key = (int)kind * 64 + (variant >> 2);
            if (s_surface.TryGetValue(key, out var cached) && cached != null) return cached;

            string p = Prefix(kind);
            var m = new Material(ShaderCache.Lit) { name = "RealPlanet_" + p, hideFlags = HideFlags.DontSave };
            var albedo = Tex(p + "_albedo");
            var normal = Tex(p + "_normal");
            var tint = Tint(kind, variant);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 1f);

            if (kind == Kind.Earth)
            {
                // Гладкость из альфы альбедо: океан — блик звезды, суша — матовая
                if (m.HasProperty("_SmoothnessTextureChannel")) m.SetFloat("_SmoothnessTextureChannel", 1f);
                m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);
            }
            else if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", kind == Kind.Mars ? 0.12f : 0.06f);

            if (normal != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
                if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", kind == Kind.Earth ? 0.8f : 1f);
            }
            s_surface[key] = m;
            return m;
        }

        private static Material Clouds()
        {
            if (s_clouds != null) return s_clouds;
            var sh = Resources.Load<Shader>("Shaders/PlanetClouds");
            if (sh == null) sh = Shader.Find("Astropolity/Planet/Clouds");
            s_clouds = new Material(sh) { name = "RealPlanet_Clouds", hideFlags = HideFlags.DontSave };
            s_clouds.SetTexture("_MainTex", Tex("earth_clouds"));
            s_clouds.SetFloat("_Opacity", 0.92f);
            return s_clouds;
        }

        private static Material Lights()
        {
            if (s_lights != null) return s_lights;
            var sh = Resources.Load<Shader>("Shaders/PlanetNightLights");
            if (sh == null) sh = Shader.Find("Astropolity/Planet/NightLights");
            s_lights = new Material(sh) { name = "RealPlanet_NightLights", hideFlags = HideFlags.DontSave };
            s_lights.SetTexture("_MainTex", Tex("earth_lights"));
            return s_lights;
        }

        private static Material Atmosphere(string name, Color color, float intensity, float shell)
        {
            var sh = Resources.Load<Shader>("Shaders/PlanetAtmosphere");
            if (sh == null) sh = Shader.Find("Astropolity/Planet/Atmosphere");
            var m = new Material(sh) { name = name, hideFlags = HideFlags.DontSave };
            m.SetColor("_Color", color);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_ShellRadius", shell);
            return m;
        }

        private static Material AtmoEarth() => s_atmoEarth != null ? s_atmoEarth
            : (s_atmoEarth = Atmosphere("RealPlanet_AtmoEarth", new Color(0.32f, 0.62f, 1f), 1.25f, 1.06f));

        private static Material AtmoMars() => s_atmoMars != null ? s_atmoMars
            : (s_atmoMars = Atmosphere("RealPlanet_AtmoMars", new Color(1f, 0.58f, 0.38f), 0.55f, 1.035f));

        // ================================================================ Сфера

        /// <summary>
        /// UV-сфера радиуса 1: u — долгота (0.5 — нулевой меридиан, восток вправо при взгляде снаружи), v — широта.
        /// variant: бит 1 — зеркально по долготе, бит 2 — перевёрнуто по широте (своя развёртка — своя планета).
        /// </summary>
        private static Mesh Sphere(int variant)
        {
            variant &= 3;
            if (s_spheres[variant] != null) return s_spheres[variant];
            const int Lon = 128, Lat = 64;
            var verts = new Vector3[(Lon + 1) * (Lat + 1)];
            var normals = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            bool flipU = (variant & 1) != 0, flipV = (variant & 2) != 0;
            for (int j = 0; j <= Lat; j++)
            {
                float v = j / (float)Lat;
                float lat = (v - 0.5f) * Mathf.PI;
                for (int i = 0; i <= Lon; i++)
                {
                    float u = i / (float)Lon;
                    float lon = (u - 0.5f) * 2f * Mathf.PI;
                    var p = new Vector3(-Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon));
                    int k = j * (Lon + 1) + i;
                    verts[k] = p;
                    normals[k] = p;
                    uvs[k] = new Vector2(flipU ? 1f - u : u, flipV ? 1f - v : v);
                }
            }
            var tris = new int[Lon * Lat * 6];
            int t = 0;
            for (int j = 0; j < Lat; j++)
                for (int i = 0; i < Lon; i++)
                {
                    int a = j * (Lon + 1) + i, b = a + 1, c = a + Lon + 1, d = c + 1;
                    // Внешние грани смотрят наружу (обход по часовой стрелке при взгляде снаружи — как в Unity)
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            var mesh = new Mesh { name = "RealPlanetSphere_" + variant };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            s_spheres[variant] = mesh;
            return mesh;
        }
    }

    /// <summary>Облака плывут чуть быстрее поверхности.</summary>
    public sealed class PlanetCloudSpin : MonoBehaviour
    {
        public float degreesPerSecond = 1.6f;
        private void Update() => transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.Self);
    }

    /// <summary>
    /// Направление на «солнце» для оболочек планеты (облака, огни, атмосфера) — они светятся без URP-освещения.
    /// Источник: направленный свет только этого слоя (студия экрана колонии), иначе ближайший точечный свет
    /// в пределах радиуса (звезда системы), иначе любой направленный.
    /// </summary>
    public sealed class PlanetSunLink : MonoBehaviour
    {
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static Light[] s_lights = new Light[0];
        private static float s_refreshAt;

        private Renderer[] _shells;
        private MaterialPropertyBlock _mpb;

        private void Start()
        {
            var own = GetComponent<Renderer>();
            var list = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r != own) list.Add(r);
            _shells = list.ToArray();
            _mpb = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (_shells == null || _shells.Length == 0) return;
            if (Time.unscaledTime >= s_refreshAt)
            {
                s_refreshAt = Time.unscaledTime + 0.5f;
                s_lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            }
            Vector3 dir = SunDirection();
            for (int i = 0; i < _shells.Length; i++)
            {
                var r = _shells[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetVector(SunDirId, dir);
                r.SetPropertyBlock(_mpb);
            }
        }

        private Vector3 SunDirection()
        {
            int bit = 1 << gameObject.layer;
            Vector3 pos = transform.position;
            Light exclusive = null, point = null, anyDir = null;
            float bestPoint = float.MaxValue;
            foreach (var l in s_lights)
            {
                if (l == null || !l.isActiveAndEnabled || (l.cullingMask & bit) == 0) continue;
                if (l.type == LightType.Directional)
                {
                    if (l.cullingMask == bit && (exclusive == null || l.intensity > exclusive.intensity)) exclusive = l;
                    if (anyDir == null || l.intensity > anyDir.intensity) anyDir = l;
                }
                else if (l.type == LightType.Point)
                {
                    float d = (l.transform.position - pos).sqrMagnitude;
                    if (d < l.range * l.range && d < bestPoint) { bestPoint = d; point = l; }
                }
            }
            if (exclusive != null) return -exclusive.transform.forward;
            if (point != null) return (point.transform.position - pos).normalized;
            if (anyDir != null) return -anyDir.transform.forward;
            return Vector3.forward;
        }
    }
}
