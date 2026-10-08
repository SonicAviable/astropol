using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Мини-системы на карте галактики (как в Master of Orion): при приближении камеры вокруг изученных
    /// звёзд проступают тонкие орбиты, по ним медленно идут планеты системы. Планеты — маленькие шарики
    /// с текстурой из генератора (континентальные, пустынные, газовые гиганты…), освещённой стороной к звезде.
    /// Издалека всё плавно гаснет — остаются точки звёзд. Создаётся лениво, только для видимых систем.
    /// </summary>
    public partial class GalaxyView
    {
        private const float MiniShowHeight = 90f;    // выше — мини-систем нет
        private const float MiniFullHeight = 52f;    // ниже — видны полностью
        private const float MiniInnerOrbit = 1.75f, MiniOrbitStep = 0.62f;

        private class MiniSys
        {
            public StarSystem Sys;
            public GameObject Root;
            public LineRenderer[] Orbits;
            public SpriteRenderer[] Planets;
            public Color[] Tints;
            public float[] Radius, Speed, Phase, Size;
            public float Alpha;
            public bool Wanted;
        }

        private GameObject _miniRoot;
        private readonly Dictionary<int, MiniSys> _minis = new Dictionary<int, MiniSys>();
        private readonly List<MiniSys> _miniLive = new List<MiniSys>();
        private float _miniScanTimer;
        private Material _miniLineMat, _miniSpriteMat;
        private static readonly Dictionary<int, Sprite> s_miniPlanetSprites = new Dictionary<int, Sprite>();

        private void UpdateMiniSystems(Camera cam)
        {
            if (_generator == null || cam == null) return;
            float dt = Time.unscaledDeltaTime;
            float h = cam.transform.position.y;
            float zoomK = 1f - Smooth(MiniFullHeight, MiniShowHeight, h);
            bool off = zoomK <= 0.001f || MenuAtmosphere.IsActive
                       || (SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView);

            if (off && _miniLive.Count == 0) return;
            if (_miniRoot == null)
            {
                _miniRoot = new GameObject("MiniSystems");
                _miniRoot.transform.SetParent(transform, false);
                _miniLineMat = new Material(GetSpriteShader());   // Sprites/Default — учитывает цвет и прозрачность вершин
                _miniLineMat.renderQueue = 2990;
                _miniSpriteMat = new Material(GetSpriteShader());
            }

            // Какие системы нужны — раз в 0,3 с (видимые на экране и изученные)
            _miniScanTimer -= dt;
            if (_miniScanTimer <= 0f)
            {
                _miniScanTimer = 0.3f;
                foreach (var m in _miniLive) m.Wanted = false;
                if (!off)
                {
                    int created = 0;
                    foreach (var s in _generator.Systems)
                    {
                        if (!IsKnownToPlayer(s) || s.Planets == null || s.Planets.Count == 0) continue;
                        var vp = cam.WorldToViewportPoint(s.Position);
                        if (vp.z <= 0f || vp.x < -0.2f || vp.x > 1.2f || vp.y < -0.2f || vp.y > 1.2f) continue;
                        if (!_minis.TryGetValue(s.Id, out var m))
                        {
                            if (created >= 3) continue;      // не больше трёх новых за раз — без рывков
                            m = CreateMini(s);
                            _minis[s.Id] = m;
                            created++;
                        }
                        m.Wanted = true;
                        if (!m.Root.activeSelf) m.Root.SetActive(true);
                        if (!_miniLive.Contains(m)) _miniLive.Add(m);
                    }
                }
            }

            Vector3 camPos = cam.transform.position;
            Quaternion camRot = cam.transform.rotation;
            float time = Time.unscaledTime;
            for (int i = _miniLive.Count - 1; i >= 0; i--)
            {
                var m = _miniLive[i];
                float target = m.Wanted && !off ? zoomK : 0f;
                m.Alpha = Mathf.MoveTowards(m.Alpha, target, dt * 1.8f);
                if (m.Alpha <= 0.001f && target <= 0f)
                {
                    m.Root.SetActive(false);
                    _miniLive.RemoveAt(i);
                    continue;
                }
                AnimateMini(m, cam, camPos, camRot, time);
            }
        }

        private MiniSys CreateMini(StarSystem s)
        {
            int n = s.Planets.Count;
            var m = new MiniSys
            {
                Sys = s,
                Orbits = new LineRenderer[n],
                Planets = new SpriteRenderer[n],
                Tints = new Color[n],
                Radius = new float[n], Speed = new float[n], Phase = new float[n], Size = new float[n]
            };
            m.Root = new GameObject($"Mini_{s.Id}");
            m.Root.transform.SetParent(_miniRoot.transform, false);
            m.Root.transform.position = s.Position;
            var rng = new System.Random(s.Id * 7919 + 13);

            for (int i = 0; i < n; i++)
            {
                var p = s.Planets[i];
                float r = MiniInnerOrbit + i * MiniOrbitStep;
                m.Radius[i] = r;
                m.Speed[i] = Mathf.Clamp(p.OrbitSpeed, 4f, 25f) * 0.22f / Mathf.Sqrt(i + 1f);   // град/с
                m.Phase[i] = (float)rng.NextDouble() * 360f;
                m.Size[i] = p.Type == PlanetType.GasGiant ? Mathf.Clamp(p.Size * 0.13f, 0.28f, 0.42f)
                                                          : Mathf.Clamp(p.Size * 0.13f, 0.15f, 0.24f);

                // орбита
                var og = new GameObject("Orbit");
                og.transform.SetParent(m.Root.transform, false);
                var lr = og.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.positionCount = 72;
                for (int k = 0; k < 72; k++)
                {
                    float a = k / 72f * Mathf.PI * 2f;
                    lr.SetPosition(k, new Vector3(Mathf.Cos(a) * r, 0.05f, Mathf.Sin(a) * r));
                }
                lr.sharedMaterial = _miniLineMat;
                lr.numCapVertices = 0;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sortingOrder = 8;
                m.Orbits[i] = lr;

                // планета
                var pg = new GameObject("Planet");
                pg.transform.SetParent(m.Root.transform, false);
                var sr = pg.AddComponent<SpriteRenderer>();
                sr.sprite = PlanetSphereSprite(p.Type, s.Id * 100 + i);
                sr.sharedMaterial = _miniSpriteMat;
                sr.sortingOrder = 12;
                m.Planets[i] = sr;
                m.Tints[i] = p.Type == PlanetType.Barren ? new Color(0.78f, 0.78f, 0.82f) : Color.white;
            }
            return m;
        }

        private void AnimateMini(MiniSys m, Camera cam, Vector3 camPos, Quaternion camRot, float time)
        {
            Vector3 c = m.Sys.Position;
            float dist = Vector3.Distance(camPos, c);
            float wpp = WorldPerPixel(cam, dist);
            float a = m.Alpha * m.Alpha * (3f - 2f * m.Alpha);
            Vector3 starScreen = cam.WorldToScreenPoint(c);
            bool owned = m.Sys.OwnerId >= 0;
            Color orbitCol = owned
                ? Color.Lerp(new Color(0.65f, 0.8f, 1f), FleetIndicator.OwnerColor(m.Sys.OwnerId), 0.35f)
                : new Color(0.65f, 0.8f, 1f);

            for (int i = 0; i < m.Planets.Length; i++)
            {
                var lr = m.Orbits[i];
                lr.startWidth = lr.endWidth = wpp * 1.1f;
                lr.startColor = lr.endColor = new Color(orbitCol.r, orbitCol.g, orbitCol.b, 0.16f * a);

                float ang = (m.Phase[i] + time * m.Speed[i]) * Mathf.Deg2Rad;
                var local = new Vector3(Mathf.Cos(ang) * m.Radius[i], 0.1f, Mathf.Sin(ang) * m.Radius[i]);
                var sr = m.Planets[i];
                sr.transform.localPosition = local;
                float size = Mathf.Max(m.Size[i], wpp * 6f);
                sr.transform.localScale = Vector3.one * (size / 0.64f);

                // освещённая сторона — к звезде (в плоскости экрана)
                Vector3 ps = cam.WorldToScreenPoint(c + local);
                Vector2 d = new Vector2(starScreen.x - ps.x, starScreen.y - ps.y);
                float rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                sr.transform.rotation = camRot * Quaternion.Euler(0f, 0f, rot);
                var tint = m.Tints[i];
                tint.a = a;
                sr.color = tint;
            }
        }

        // ==================== ШАРИКИ ПЛАНЕТ ====================

        private static PlanetVisualType VisualFor(PlanetType t, int seed) => t switch
        {
            PlanetType.Continental => (seed & 1) == 0 ? PlanetVisualType.Continental : PlanetVisualType.Ocean,
            PlanetType.Desert => PlanetVisualType.Desert,
            PlanetType.Molten => PlanetVisualType.Molten,
            PlanetType.GasGiant => PlanetVisualType.GasGiant,
            _ => PlanetVisualType.Ice
        };

        /// <summary>Спрайт-шарик 64×64: текстура планеты на сфере, свет справа, мягкий край атмосферы.</summary>
        private static Sprite PlanetSphereSprite(PlanetType type, int seed)
        {
            var vt = VisualFor(type, seed);
            int variant = Mathf.Abs(seed) % 3;
            int key = (int)vt * 8 + variant;
            if (s_miniPlanetSprites.TryGetValue(key, out var cached) && cached != null) return cached;

            var src = PlanetTextureFactory.GeneratePlanetTexture(vt, 4242 + key * 97);
            const int R = 64;
            var tex = new Texture2D(R, R, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "MiniPlanet_" + key };
            var px = new Color32[R * R];
            var L = new Vector3(1f, 0.25f, 0.45f).normalized;
            Color atmo = vt switch
            {
                PlanetVisualType.Continental => new Color(0.45f, 0.75f, 1f),
                PlanetVisualType.Ocean => new Color(0.4f, 0.7f, 1f),
                PlanetVisualType.Ice => new Color(0.75f, 0.9f, 1f),
                PlanetVisualType.GasGiant => new Color(1f, 0.85f, 0.7f),
                PlanetVisualType.Molten => new Color(1f, 0.45f, 0.2f),
                _ => new Color(1f, 0.8f, 0.6f)
            };
            float spin = variant * 2.1f;
            float c = (R - 1) * 0.5f;
            for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                float nx = (x - c) / (c - 1.5f), ny = (y - c) / (c - 1.5f);
                float rr = nx * nx + ny * ny;
                float edge = Mathf.Clamp01((1f - Mathf.Sqrt(rr)) * (c - 1.5f));      // сглаженный край
                if (rr >= 1.08f) { px[y * R + x] = new Color32(0, 0, 0, 0); continue; }
                float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - rr));
                var n = new Vector3(nx, ny, nz);
                float lon = Mathf.Atan2(nx, nz) + spin;
                float lat = Mathf.Asin(Mathf.Clamp(ny, -1f, 1f));
                Color col = src.GetPixelBilinear(lon / (Mathf.PI * 2f) + 0.5f, lat / Mathf.PI + 0.5f);
                float diff = Mathf.Max(0f, Vector3.Dot(n, L));
                float light = 0.05f + 1.15f * Mathf.Pow(diff, 0.85f);
                Color o = col * light;
                if (vt == PlanetVisualType.Molten) o += new Color(0.5f, 0.12f, 0.02f) * (1f - diff) * col.r;   // светится ночная сторона
                float rim = Mathf.Pow(1f - nz, 3f) * (0.25f + 0.75f * Mathf.Clamp01(diff * 1.5f));
                o = Color.Lerp(o, atmo, rim * 0.6f);
                o.a = edge;
                px[y * R + x] = o;
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            Object.Destroy(src);
            var sp = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), 100);
            s_miniPlanetSprites[key] = sp;
            return sp;
        }
    }
}
