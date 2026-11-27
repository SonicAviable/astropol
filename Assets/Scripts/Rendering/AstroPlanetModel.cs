using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Планеты на шейдере Astropolity/Planet/Astro: фото-карты Solar System Scope (Resources/Planets/Astro, CC BY 4.0)
    /// плюс процедурная вариативность. Игровой тип не меняется — у каждого типа несколько обликов (подвидов),
    /// облик и вариант выбираются по имени планеты, поэтому в системе и на экране колонии планета одна и та же:
    ///   Continental → материковая (Земля), океаническая, тропическая, саванна, альпийская, тундровая, гайя;
    ///   Desert → пустынная (Марс), аридная; Molten → вулканическая, токсичная;
    ///   Barren → безжизненная, ледяная, опустошённая; GasGiant → юпитерианский, сатурнианский, ледяной, нептунианский.
    /// Шаблоны собираются один раз и клонируются (SystemViewManager, PlanetHoloStudio); оболочки освещает PlanetSunLink.
    /// </summary>
    public static class AstroPlanetModel
    {
        private const string Folder = "Planets/Astro/";
        private const int Variants = 8;

        public enum Look { Continental, Ocean, Tropical, Savanna, Alpine, Tundra, Gaia, Arid, Desert, Toxic, Volcanic, Ice, Barren, Tomb, Gas }

        private static Transform s_root;
        private static Shader s_planet, s_ring, s_atmo;
        private static bool s_checked, s_ok;
        private static Mesh s_ringMesh;
        private static readonly Dictionary<int, GameObject> s_templates = new Dictionary<int, GameObject>();
        private static readonly Dictionary<string, Texture2D> s_tex = new Dictionary<string, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_root = null; s_checked = s_ok = false; s_ringMesh = null;
            s_templates.Clear(); s_tex.Clear();
        }

        /// <summary>Шаблон планеты для Instantiate или null (нет шейдера/текстур — остаются прежние модели).</summary>
        public static GameObject Template(PlanetData planet)
        {
            if (planet == null || !Available()) return null;
            uint h = StableHash(planet.Name);
            Look look = LookFor(planet.Type, h);
            int variant = (int)((h >> 8) % Variants);
            int key = (int)look * 32 + variant;
            if (s_templates.TryGetValue(key, out var t) && t != null) return t;
            t = Build(look, variant);
            s_templates[key] = t;
            return t;
        }

        /// <summary>Шаблон собран здесь (радиус ровно 1 — вписывать по рамкам не нужно, оболочки и кольца раздувают их).</summary>
        public static bool IsTemplate(GameObject t) => t != null && s_templates.ContainsValue(t);

        /// <summary>Облик планеты (для подписей и подсказок).</summary>
        public static Look LookOf(PlanetData planet) => LookFor(planet.Type, StableHash(planet.Name));

        public static string LookName(Look l) => l switch
        {
            Look.Continental => Loc.T("Континентальная"), Look.Ocean => Loc.T("Океаническая"), Look.Tropical => Loc.T("Тропическая"),
            Look.Savanna => Loc.T("Саванна"), Look.Alpine => Loc.T("Альпийская"), Look.Tundra => Loc.T("Тундровая"),
            Look.Gaia => Loc.T("Гайя"), Look.Arid => Loc.T("Аридная"), Look.Desert => Loc.T("Пустынная"),
            Look.Toxic => Loc.T("Токсичная"), Look.Volcanic => Loc.T("Вулканическая"), Look.Ice => Loc.T("Ледяная"),
            Look.Barren => Loc.T("Безжизненная"), Look.Tomb => Loc.T("Опустошённая"), _ => Loc.T("Газовый гигант")
        };

        private static Look LookFor(PlanetType type, uint h)
        {
            float x = (h & 0xFF) / 256f;
            switch (type)
            {
                case PlanetType.Continental:
                    return x < 0.25f ? Look.Continental : x < 0.38f ? Look.Ocean : x < 0.51f ? Look.Tropical : x < 0.64f ? Look.Savanna
                         : x < 0.76f ? Look.Alpine : x < 0.88f ? Look.Tundra : Look.Gaia;
                case PlanetType.Desert: return x < 0.5f ? Look.Desert : Look.Arid;
                case PlanetType.Molten: return x < 0.7f ? Look.Volcanic : Look.Toxic;
                case PlanetType.Barren: return x < 0.45f ? Look.Barren : x < 0.8f ? Look.Ice : Look.Tomb;
                default: return Look.Gas;
            }
        }

        private static bool Available()
        {
            if (s_checked) return s_ok;
            s_checked = true;
            s_planet = Resources.Load<Shader>("Shaders/AstroPlanet") ?? Shader.Find("Astropolity/Planet/Astro");
            s_ring = Resources.Load<Shader>("Shaders/AstroRing") ?? Shader.Find("Astropolity/Planet/AstroRing");
            s_atmo = Resources.Load<Shader>("Shaders/PlanetAtmosphere") ?? Shader.Find("Astropolity/Planet/Atmosphere");
            s_ok = s_planet != null && s_planet.isSupported && Tex("earth_daymap") != null && RealPlanetModel.UnitSphere != null;
            if (!s_ok) Debug.LogWarning("[AstroPlanet] Нет шейдера Astropolity/Planet/Astro или карт " + Folder + " — остаются прежние планеты.");
            return s_ok;
        }

        private static Texture2D Tex(string name)
        {
            if (s_tex.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>(Folder + name);
            if (t != null) { t.wrapModeU = TextureWrapMode.Repeat; t.wrapModeV = TextureWrapMode.Clamp; t.anisoLevel = Mathf.Max(t.anisoLevel, 4); }
            s_tex[name] = t;
            return t;
        }

        private static uint StableHash(string s)
        {
            uint h = 2166136261u;
            if (s != null) foreach (char c in s) { h ^= c; h *= 16777619u; }
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return h;
        }

        // ================================================================ Сборка

        private sealed class Cfg
        {
            public float kind, seed, spin = 0.03f, cloud, city, hue, sat = 1f, off, flip = 1f, bump, cap = 2f, sea, temp, atmK = 1f;
            public Vector4 a, b, c, d, tint = Vector4.one, cloudCol = new Vector4(0.92f, 0.92f, 0.92f, 0), cityCol = new Vector4(1f, 0.62f, 0.3f, 0);
            public Vector3 atm = new Vector3(0.4f, 0.65f, 1f);
            public float atmIntensity = 1f, atmShell = 1.05f, tilt;
            public string t0 = "earth_daymap", t1, t2;
            public bool ring;
        }

        private static GameObject Build(Look look, int variant)
        {
            if (s_root == null)
            {
                var holder = new GameObject("[AstroPlanetTemplates]");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                s_root = holder.transform;
            }
            var r = new System.Random((int)look * 7919 + variant * 104729 + 17);
            var cfg = Configure(look, r);

            var go = new GameObject("AstroPlanet_" + look + "_" + variant);
            go.transform.SetParent(s_root, false);

            var tilt = new GameObject("Tilt").transform;
            tilt.SetParent(go.transform, false);
            tilt.localRotation = Quaternion.Euler(0f, variant * 137.5f, cfg.tilt);

            var surf = new GameObject("Surface");
            surf.transform.SetParent(tilt, false);
            surf.AddComponent<MeshFilter>().sharedMesh = RealPlanetModel.UnitSphere;
            var mr = surf.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SurfaceMaterial(look, variant, cfg);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            if (cfg.ring && s_ring != null && Tex("saturn_ring_alpha") != null)
            {
                var ring = new GameObject("Rings");
                ring.transform.SetParent(tilt, false);
                ring.AddComponent<MeshFilter>().sharedMesh = RingMesh();
                var rr = ring.AddComponent<MeshRenderer>();
                var rm = new Material(s_ring) { name = "AstroRing_" + variant, hideFlags = HideFlags.DontSave };
                rm.SetTexture("_MainTex", Tex("saturn_ring_alpha"));
                rm.SetFloat("_Hue", cfg.hue);
                rm.SetFloat("_Sat", cfg.sat);
                rr.sharedMaterial = rm;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rr.receiveShadows = false;
            }

            if (s_atmo != null && cfg.atmIntensity > 0.01f)
            {
                var shell = new GameObject("Atmosphere");
                shell.transform.SetParent(go.transform, false);
                shell.transform.localScale = Vector3.one * cfg.atmShell;
                shell.AddComponent<MeshFilter>().sharedMesh = RealPlanetModel.UnitSphere;
                var ar = shell.AddComponent<MeshRenderer>();
                var am = new Material(s_atmo) { name = "AstroAtmo_" + look + "_" + variant, hideFlags = HideFlags.DontSave };
                am.SetColor("_Color", new Color(cfg.atm.x, cfg.atm.y, cfg.atm.z, 1f));
                am.SetFloat("_Intensity", cfg.atmIntensity);
                am.SetFloat("_ShellRadius", cfg.atmShell);
                ar.sharedMaterial = am;
                ar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ar.receiveShadows = false;
            }

            go.AddComponent<PlanetSunLink>();
            return go;
        }

        private static Material SurfaceMaterial(Look look, int variant, Cfg c)
        {
            var m = new Material(s_planet) { name = "AstroPlanet_" + look + "_" + variant, hideFlags = HideFlags.DontSave };
            m.SetTexture("_MainTex", Tex(c.t0));
            if (c.t1 != null) m.SetTexture("_AuxTex", Tex(c.t1));
            if (c.t2 != null) m.SetTexture("_CloudTex", Tex(c.t2));
            m.SetFloat("_Kind", c.kind); m.SetFloat("_Seed", c.seed); m.SetFloat("_Spin", c.spin);
            m.SetFloat("_Cloud", c.cloud); m.SetFloat("_City", c.city); m.SetFloat("_Hue", c.hue); m.SetFloat("_Sat", c.sat);
            m.SetFloat("_Off", c.off); m.SetFloat("_Flip", c.flip); m.SetFloat("_Bump", c.bump); m.SetFloat("_Cap", c.cap);
            m.SetFloat("_Sea", c.sea); m.SetFloat("_Temp", c.temp); m.SetFloat("_AtmK", c.atmK);
            m.SetVector("_A", c.a); m.SetVector("_B", c.b); m.SetVector("_C", c.c); m.SetVector("_D", c.d);
            m.SetVector("_Atm", new Vector4(c.atm.x, c.atm.y, c.atm.z, 0)); m.SetVector("_Tint", c.tint);
            m.SetVector("_CloudCol", c.cloudCol); m.SetVector("_CityCol", c.cityCol);
            return m;
        }

        // ================================================================ Облики (цвета — sRGB, как в превью)

        private static float R(System.Random r, float a, float b) => a + (b - a) * (float)r.NextDouble();

        private static Vector4 Hsl(float h, float s, float l)
        {
            h = ((h % 360f) + 360f) % 360f / 360f;
            float F(float n) { float k = (n + h * 12f) % 12f; float a = s * Mathf.Min(l, 1f - l); return l - a * Mathf.Max(-1f, Mathf.Min(k - 3f, Mathf.Min(9f - k, 1f))); }
            return new Vector4(F(0), F(8), F(4), 0);
        }
        private static Vector3 V3(Vector4 v) => new Vector3(v.x, v.y, v.z);

        // Процедурные миры: уровень моря, температура, океан, равнины (сухо/влажно), высокогорья, облака, лёд
        private struct World { public float sea0, sea1, temp0, temp1, cl0, cl1, cap, bump; public Vector3 A, B, C, D; public Vector3? cloud, atm, city; public int cities; public string det; }

        private static World WorldOf(Look l)
        {
            switch (l)
            {
                case Look.Ocean: return new World { sea0 = .2f, sea1 = .3f, temp0 = 0, temp1 = .1f, A = new Vector3(210, .6f, .16f), B = new Vector3(45, .3f, .5f), C = new Vector3(100, .32f, .24f), D = new Vector3(30, .2f, .35f), cl0 = .7f, cl1 = .95f, cap = 1, cities = -1 };
                case Look.Tropical: return new World { sea0 = -.02f, sea1 = .08f, temp0 = .3f, temp1 = .4f, A = new Vector3(195, .6f, .2f), B = new Vector3(70, .4f, .3f), C = new Vector3(115, .45f, .16f), D = new Vector3(90, .25f, .25f), cl0 = .75f, cl1 = 1f, cap = 0, cities = -1 };
                case Look.Savanna: return new World { sea0 = -.1f, sea1 = 0, temp0 = .25f, temp1 = .35f, A = new Vector3(205, .55f, .18f), B = new Vector3(48, .32f, .46f), C = new Vector3(75, .25f, .28f), D = new Vector3(30, .25f, .36f), cl0 = .4f, cl1 = .65f, cap = .3f, cities = -1 };
                case Look.Arid: return new World { sea0 = -.25f, sea1 = -.15f, temp0 = .3f, temp1 = .45f, A = new Vector3(200, .5f, .2f), B = new Vector3(32, .32f, .45f), C = new Vector3(38, .25f, .36f), D = new Vector3(20, .25f, .3f), cl0 = .2f, cl1 = .4f, cap = 0, cities = -1 };
                case Look.Alpine: return new World { sea0 = -.08f, sea1 = 0, temp0 = -.12f, temp1 = 0, A = new Vector3(212, .5f, .15f), B = new Vector3(95, .18f, .35f), C = new Vector3(140, .22f, .25f), D = new Vector3(210, .06f, .55f), cl0 = .5f, cl1 = .8f, cap = 1, bump = 12, cities = -1 };
                case Look.Tundra: return new World { sea0 = -.05f, sea1 = .05f, temp0 = -.4f, temp1 = -.28f, A = new Vector3(215, .4f, .14f), B = new Vector3(35, .18f, .38f), C = new Vector3(60, .15f, .3f), D = new Vector3(30, .08f, .45f), cl0 = .5f, cl1 = .75f, cap = 1, cities = -1 };
                case Look.Gaia: return new World { sea0 = 0, sea1 = .08f, temp0 = .1f, temp1 = .2f, A = new Vector3(190, .65f, .2f), B = new Vector3(85, .5f, .32f), C = new Vector3(125, .55f, .2f), D = new Vector3(100, .35f, .3f), cl0 = .6f, cl1 = .85f, cap = .6f, atm = new Vector3(185, .9f, .6f), cities = 0 };
                case Look.Toxic: return new World { sea0 = -.05f, sea1 = .05f, temp0 = .2f, temp1 = .3f, A = new Vector3(70, .5f, .16f), B = new Vector3(45, .35f, .35f), C = new Vector3(80, .4f, .25f), D = new Vector3(30, .15f, .25f), cl0 = .85f, cl1 = 1f, cloud = new Vector3(60, .45f, .7f), atm = new Vector3(75, .8f, .5f), cap = 0, cities = 0 };
                default: return new World { sea0 = -.8f, sea1 = -.8f, temp0 = 0, temp1 = .1f, A = Vector3.zero, B = new Vector3(30, .06f, .3f), C = new Vector3(25, .08f, .22f), D = new Vector3(0, 0, .4f), cl0 = .3f, cl1 = .5f, cloud = new Vector3(30, .1f, .5f), atm = new Vector3(40, .3f, .5f), cap = 0, cities = 1, city = new Vector3(110, .9f, .55f), det = "moon" };
            }
        }

        private static Cfg Configure(Look look, System.Random r)
        {
            var c = new Cfg
            {
                seed = R(r, 0f, 170f), off = (float)r.NextDouble(), flip = r.NextDouble() < 0.5 ? -1f : 1f,
                tilt = R(r, 8f, 26f), spin = R(r, 0.02f, 0.04f)
            };
            switch (look)
            {
                case Look.Continental:
                {
                    bool arid = r.NextDouble() < 0.3;
                    c.kind = 0; c.t0 = "earth_daymap"; c.t1 = "earth_nightmap"; c.t2 = "earth_clouds";
                    c.hue = arid ? R(r, 0.35f, 0.6f) : R(r, -0.18f, 0.22f); c.sat = arid ? R(r, 0.55f, 0.75f) : R(r, 0.85f, 1.1f);
                    c.tint = arid ? new Vector4(1.08f, 1f, 0.86f, 0) : Vector4.one;
                    c.a = Hsl(R(r, 205, 222), 0.6f, R(r, 0.16f, 0.22f)); c.atm = V3(Hsl(R(r, 200, 212), 0.75f, 0.62f));
                    c.cloud = R(r, 0.55f, 0.95f); c.city = r.NextDouble() < 0.65 ? 1 : 0; c.bump = 7; c.atmIntensity = 1.2f; c.atmShell = 1.06f;
                    break;
                }
                case Look.Desert:
                    c.kind = 5; c.t0 = "mars"; c.hue = R(r, -0.25f, 0.3f); c.sat = R(r, 0.6f, 1.1f); c.bump = 9;
                    c.tint = new Vector4(R(r, 0.92f, 1.08f), 1f, R(r, 0.9f, 1.05f), 0);
                    c.cap = r.NextDouble() < 0.55 ? R(r, 0.8f, 0.9f) : 2f; c.atm = V3(Hsl(R(r, 18, 32), 0.45f, 0.65f));
                    c.atmIntensity = 0.7f; c.atmShell = 1.035f;
                    break;
                case Look.Barren:
                {
                    string[] t = { "moon", "ceres_fictional", "makemake_fictional", "haumea_fictional" };
                    c.kind = 5; c.t0 = t[r.Next(t.Length)]; c.hue = R(r, -0.2f, 0.2f); c.sat = R(r, 0.3f, 0.8f);
                    c.tint = new Vector4(R(r, 0.95f, 1.1f), 1f, R(r, 0.85f, 1f), 0); c.bump = 10; c.cap = 2; c.atmK = 0; c.atmIntensity = 0;
                    break;
                }
                case Look.Gas:
                {
                    string[] g = { "jupiter", "saturn", "uranus", "neptune", "jupiter", "saturn" };
                    string pick = g[r.Next(g.Length)];
                    c.kind = 1; c.t0 = pick; c.spin = R(r, 0.04f, 0.07f); c.tilt = R(r, 3f, 14f);
                    c.hue = pick == "jupiter" || pick == "saturn" ? R(r, -0.35f, 0.35f) : R(r, -0.5f, 0.5f); c.sat = R(r, 0.8f, 1.15f);
                    c.ring = pick == "saturn" || r.NextDouble() < 0.3;
                    Vector3 b = pick == "jupiter" ? new Vector3(30, .4f, .7f) : pick == "saturn" ? new Vector3(42, .35f, .75f) : pick == "uranus" ? new Vector3(185, .4f, .75f) : new Vector3(218, .5f, .62f);
                    c.atm = V3(Hsl(b.x + c.hue * 57.3f, b.y, b.z)); c.atmIntensity = 0.8f; c.atmShell = 1.04f;
                    break;
                }
                case Look.Volcanic:
                    c.kind = 2; c.t0 = "venus_surface"; c.bump = 8;
                    c.a = Hsl(R(r, 15, 30), 0.1f, 0.07f); c.b = Hsl(R(r, 20, 35), 0.1f, 0.34f); c.c = Hsl(R(r, 8, 16), 0.95f, 0.45f);
                    c.atm = V3(Hsl(R(r, 10, 20), 0.6f, 0.4f)); c.atmIntensity = 0.9f; c.atmShell = 1.04f;
                    break;
                case Look.Ice:
                {
                    string[] t = { "eris_fictional", "haumea_fictional", "makemake_fictional", "ceres_fictional", "moon" };
                    c.kind = 3; c.t0 = t[r.Next(t.Length)]; c.bump = 8;
                    c.a = Hsl(R(r, 195, 215), 0.18f, 0.92f); c.c = Hsl(R(r, 205, 220), 0.3f, 0.3f); c.b = Hsl(R(r, 15, 30), 0.35f, 0.45f);
                    c.atm = V3(Hsl(205, 0.5f, 0.8f)); c.atmIntensity = 0.45f; c.atmShell = 1.03f;
                    break;
                }
                default:
                {
                    var w = WorldOf(look);
                    Vector4 H(Vector3 x) => Hsl(x.x + R(r, -6, 6), x.y, x.z * R(r, 0.92f, 1.08f));
                    c.kind = 6; c.t0 = w.det ?? "mars"; c.t2 = "earth_clouds";
                    c.sea = R(r, w.sea0, w.sea1); c.temp = R(r, w.temp0, w.temp1);
                    c.a = H(w.A); c.b = H(w.B); c.c = H(w.C); c.d = H(w.D);
                    c.cloud = R(r, w.cl0, w.cl1); c.cap = w.cap; c.bump = w.bump > 0 ? w.bump : 8;
                    if (w.cloud.HasValue) c.cloudCol = Hsl(w.cloud.Value.x, w.cloud.Value.y, w.cloud.Value.z);
                    if (w.city.HasValue) c.cityCol = Hsl(w.city.Value.x, w.city.Value.y, w.city.Value.z);
                    c.city = w.cities >= 0 ? w.cities : (r.NextDouble() < 0.5 ? 1 : 0);
                    c.atm = w.atm.HasValue ? V3(Hsl(w.atm.Value.x, w.atm.Value.y, w.atm.Value.z)) : V3(Hsl(R(r, 200, 212), 0.75f, 0.62f));
                    c.atmIntensity = look == Look.Tomb ? 0.5f : 1.1f; c.atmShell = 1.055f;
                    break;
                }
            }
            return c;
        }

        /// <summary>Плоское кольцо 1.24…2.3 радиуса планеты в плоскости XZ; uv.x — радиальная координата.</summary>
        private static Mesh RingMesh()
        {
            if (s_ringMesh != null) return s_ringMesh;
            const int seg = 128; const float ri = 1.24f, ro = 2.3f;
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var tri = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                v[i * 2] = new Vector3(cs * ri, 0, sn * ri); uv[i * 2] = new Vector2(0, i / (float)seg);
                v[i * 2 + 1] = new Vector3(cs * ro, 0, sn * ro); uv[i * 2 + 1] = new Vector2(1, i / (float)seg);
                if (i < seg)
                {
                    int k = i * 6, b = i * 2;
                    tri[k] = b; tri[k + 1] = b + 1; tri[k + 2] = b + 2; tri[k + 3] = b + 1; tri[k + 4] = b + 3; tri[k + 5] = b + 2;
                }
            }
            s_ringMesh = new Mesh { name = "AstroRing", vertices = v, uv = uv, triangles = tri, hideFlags = HideFlags.DontSave };
            s_ringMesh.RecalculateNormals();
            s_ringMesh.RecalculateBounds();
            return s_ringMesh;
        }
    }
}
