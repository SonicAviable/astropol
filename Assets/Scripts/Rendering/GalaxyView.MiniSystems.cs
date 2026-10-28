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
    /// Планеты живые: при наведении — кольцо подсветки и карточка с характеристиками (PlanetHoverCard),
    /// клик открывает экран планеты сразу с карты, без захода в систему. У колоний — значок с числом жителей.
    /// </summary>
    public partial class GalaxyView
    {
        private const float MiniShowHeight = 90f;    // выше — мини-систем нет
        private const float MiniFullHeight = 52f;    // ниже — видны полностью
        private const float MiniInnerOrbit = 2.1f, MiniOrbitStep = 0.82f;

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
            public TextMesh[] PopTags;
        }

        /// <summary>Планета мини-системы под курсором (null — нет). Звёзды и выбор флотов уступают ей клик.</summary>
        public static PlanetData HoveredMiniPlanet { get; private set; }

        private SpriteRenderer _miniHoverRing;
        private PlanetData _miniPressed;
        private Vector2 _miniPressPos;

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

            if (off && _miniLive.Count == 0) { ClearMiniHover(); return; }
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

            UpdateMiniHover(cam, off);
        }

        // ==================== НАВЕДЕНИЕ И КЛИК ====================

        private void UpdateMiniHover(Camera cam, bool off)
        {
            MiniSys hs = null;
            int hi = -1;
            bool blocked = off || cam == null
                           || (PlanetFocusOverlay.Instance != null && PlanetFocusOverlay.Instance.IsOpen)
                           || (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());
            Vector2 mouse = Input.mousePosition;
            if (!blocked && !FleetUnderCursor(mouse))
            {
                float best = float.MaxValue;
                foreach (var m in _miniLive)
                {
                    if (m.Alpha < 0.6f) continue;
                    float wpp = WorldPerPixel(cam, Vector3.Distance(cam.transform.position, m.Sys.Position));
                    for (int i = 0; i < m.Planets.Length; i++)
                    {
                        var sp = cam.WorldToScreenPoint(m.Planets[i].transform.position);
                        if (sp.z <= 0f) continue;
                        float rPx = Mathf.Max(m.Planets[i].transform.localScale.x * 0.32f / wpp, 9f) + 3f;
                        float d = Vector2.Distance(mouse, sp);
                        if (d <= rPx && d < best) { best = d; hs = m; hi = i; }
                    }
                }
            }

            var planet = hs != null ? hs.Sys.Planets[hi] : null;
            HoveredMiniPlanet = planet;
            if (planet == null)
            {
                PlanetHoverCard.Hide();
                if (_miniHoverRing != null) _miniHoverRing.enabled = false;
                if (Input.GetMouseButtonUp(0)) _miniPressed = null;
                return;
            }

            var tr = hs.Planets[hi].transform;
            Vector3 scr = cam.WorldToScreenPoint(tr.position);
            PlanetHoverCard.Show(planet, hs.Sys.Id * 100 + hi, scr);

            if (_miniHoverRing == null)
            {
                var go = new GameObject("MiniHoverRing");
                go.transform.SetParent(_miniRoot.transform, false);
                _miniHoverRing = go.AddComponent<SpriteRenderer>();
                _miniHoverRing.sprite = HoverRingSprite();
                _miniHoverRing.sharedMaterial = _miniSpriteMat;
                _miniHoverRing.sortingOrder = 13;
            }
            _miniHoverRing.enabled = true;
            var rt = _miniHoverRing.transform;
            rt.position = tr.position;
            rt.rotation = cam.transform.rotation;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 5f) * 0.05f;
            rt.localScale = tr.localScale * 1.55f * pulse;
            Color rc = planet.OwnerId >= 0 ? Color.Lerp(FleetIndicator.OwnerColor(planet.OwnerId), Color.white, 0.3f) : new Color(0.8f, 0.9f, 1f);
            rc.a = 0.9f;
            _miniHoverRing.color = rc;

            // Клик (без перетаскивания камеры) — экран планеты прямо с карты
            if (Input.GetMouseButtonDown(0)) { _miniPressed = planet; _miniPressPos = mouse; }
            if (Input.GetMouseButtonUp(0))
            {
                bool click = _miniPressed == planet && Vector2.Distance(_miniPressPos, mouse) < 8f;
                _miniPressed = null;
                if (click && PlanetFocusOverlay.Instance != null)
                {
                    SFXManager.Play(StellarisClone.Core.Audio.Sfx.SystemSelect);
                    PlanetHoverCard.Hide();
                    _miniHoverRing.enabled = false;
                    PlanetFocusOverlay.Instance.Open(planet, hs.Sys);
                }
            }
        }

        private void ClearMiniHover()
        {
            if (HoveredMiniPlanet == null) return;
            HoveredMiniPlanet = null;
            _miniPressed = null;
            PlanetHoverCard.Hide();
            if (_miniHoverRing != null) _miniHoverRing.enabled = false;
        }

        /// <summary>Значок флота под курсором — флоты выбираются раньше планет.</summary>
        private static bool FleetUnderCursor(Vector2 mouse)
        {
            foreach (var ind in FleetIndicator.All)
            {
                if (ind == null || !ind.IsLeader || !ind.OnScreen) continue;
                if (Vector2.Distance(mouse, ind.ScreenPos) <= ind.ScreenRadius + 3f) return true;
            }
            return false;
        }

        private static Sprite s_hoverRing;

        private static Sprite HoverRingSprite()
        {
            if (s_hoverRing != null) return s_hoverRing;
            const int R = 64;
            var tex = new Texture2D(R, R, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "MiniHoverRing" };
            var px = new Color32[R * R];
            float c = (R - 1) * 0.5f;
            for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;   // 0 — центр, 1 — край
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.06f);
                float glow = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.16f) * 0.35f;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.Max(ring, glow)) * 255f);
                px[y * R + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            s_hoverRing = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), 100);
            return s_hoverRing;
        }

        /// <summary>Шарик планеты для интерфейса (тот же, что на карте).</summary>
        public static Sprite PlanetIcon(PlanetType type, int seed) => PlanetSphereSprite(type, seed);

        /// <summary>Шарик конкретной планеты: у планет с детальной моделью — по её развёртке (как в виде системы).</summary>
        public static Sprite PlanetIcon(PlanetData planet, int seed)
            => planet != null ? PlanetSphereSprite(planet, seed) : PlanetSphereSprite(PlanetType.Barren, seed);

        private MiniSys CreateMini(StarSystem s)
        {
            int n = s.Planets.Count;
            var m = new MiniSys
            {
                Sys = s,
                Orbits = new LineRenderer[n],
                Planets = new SpriteRenderer[n],
                Tints = new Color[n],
                Radius = new float[n], Speed = new float[n], Phase = new float[n], Size = new float[n],
                PopTags = new TextMesh[n]
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
                // Планеты крупные — видно тип мира без захода в систему (газовые гиганты заметно больше)
                m.Size[i] = p.Type == PlanetType.GasGiant ? Mathf.Clamp(p.Size * 0.32f, 0.72f, 0.98f)
                                                          : Mathf.Clamp(p.Size * 0.36f, 0.40f, 0.62f);

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
                sr.sprite = PlanetSphereSprite(p, s.Id * 100 + i);
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
                // Орбиты — фон, а не главное на карте: тонкие и полупрозрачные, чтобы не спорить с коридорами и ярлыками
                lr.startWidth = lr.endWidth = wpp * 1.0f;
                lr.startColor = lr.endColor = new Color(orbitCol.r, orbitCol.g, orbitCol.b, 0.10f * a);

                float ang = (m.Phase[i] + time * m.Speed[i]) * Mathf.Deg2Rad;
                var local = new Vector3(Mathf.Cos(ang) * m.Radius[i], 0.1f, Mathf.Sin(ang) * m.Radius[i]);
                var sr = m.Planets[i];
                sr.transform.localPosition = local;
                float size = Mathf.Max(m.Size[i], wpp * 12f);   // издалека — не меньше 12 пикселей
                sr.transform.localScale = Vector3.one * (size / 0.64f);

                // освещённая сторона — к звезде (в плоскости экрана)
                Vector3 ps = cam.WorldToScreenPoint(c + local);
                Vector2 d = new Vector2(starScreen.x - ps.x, starScreen.y - ps.y);
                float rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                sr.transform.rotation = camRot * Quaternion.Euler(0f, 0f, rot);
                var tint = m.Tints[i];
                tint.a = a;
                sr.color = tint;

                PopTag(m, i, local, size, wpp, camRot, a);
            }
        }

        /// <summary>У колонии — число жителей справа сверху от шарика, цветом хозяина, постоянного экранного размера.</summary>
        private void PopTag(MiniSys m, int i, Vector3 local, float size, float wpp, Quaternion camRot, float a)
        {
            var p = m.Sys.Planets[i];
            var tag = m.PopTags[i];
            if (p.Population <= 0)
            {
                if (tag != null && tag.gameObject.activeSelf) tag.gameObject.SetActive(false);
                return;
            }
            if (tag == null)
            {
                var go = new GameObject("PopTag");
                go.transform.SetParent(m.Root.transform, false);
                tag = go.AddComponent<TextMesh>();
                tag.font = GameFont.Bold;
                tag.fontSize = 64;
                tag.characterSize = 0.1f;
                tag.fontStyle = FontStyle.Bold;
                tag.anchor = TextAnchor.LowerLeft;
                var mr = go.GetComponent<MeshRenderer>();
                if (tag.font != null) mr.sharedMaterial = tag.font.material;
                mr.sortingOrder = 14;
                m.PopTags[i] = tag;
            }
            if (!tag.gameObject.activeSelf) tag.gameObject.SetActive(true);
            string txt = p.Population.ToString();
            if (tag.text != txt) tag.text = txt;
            Color c = Color.Lerp(FleetIndicator.OwnerColor(p.OwnerId), Color.white, 0.35f);
            c.a = a;
            tag.color = c;
            float r = size * 0.5f;
            var t = tag.transform;
            t.localPosition = local + camRot * new Vector3(r * 0.7f + wpp * 2f, r * 0.7f, 0f);
            t.rotation = camRot;
            t.localScale = Vector3.one * (13f * wpp / 0.64f);
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

        private static readonly Dictionary<string, Sprite> s_modelPlanetSprites = new Dictionary<string, Sprite>();

        /// <summary>Шарик планеты: с детальной моделью — по её развёртке и дымке, иначе — по типу.</summary>
        /// <summary>Портрет планеты (тот же шарик, что у звезды на карте) — для шапки панели системы.</summary>
        internal static Sprite PlanetPortrait(StarSystem s, PlanetData planet)
            => s == null || planet == null ? null : PlanetSphereSprite(planet, s.Id * 100 + s.Planets.IndexOf(planet));

        private static Sprite PlanetSphereSprite(PlanetData planet, int seed)
        {
            var src = PlanetModelLibrary.IconSource(planet, out string model, out Color atmo);
            if (src == null) return PlanetSphereSprite(planet.Type, seed);
            int variant = Mathf.Abs(seed) % 3;
            string id = model + "_" + variant;
            if (s_modelPlanetSprites.TryGetValue(id, out var cached) && cached != null) return cached;
            var sp = BuildSphereSprite(src, atmo, planet.Type == PlanetType.Molten, variant, "MiniPlanet_" + id);
            s_modelPlanetSprites[id] = sp;
            return sp;
        }

        /// <summary>Спрайт-шарик 128×128 (0,64 ед. в мире): текстура планеты на сфере, свет справа, мягкий край атмосферы.</summary>
        private static Sprite PlanetSphereSprite(PlanetType type, int seed)
        {
            var vt = VisualFor(type, seed);
            int variant = Mathf.Abs(seed) % 3;
            // Земля, Марс, Луна — по настоящим картам (те же, что у планет в системе), остальные — процедурные
            var real = RealPlanetModel.IconSource(type);
            int key = real != null ? 1000 + (int)type * 8 + variant : (int)vt * 8 + variant;
            if (s_miniPlanetSprites.TryGetValue(key, out var cached) && cached != null) return cached;

            var src = real != null ? real : PlanetTextureFactory.GeneratePlanetTexture(vt, 4242 + key * 97);
            Color atmo = real != null ? RealPlanetModel.IconAtmosphere(type) : vt switch
            {
                PlanetVisualType.Continental => new Color(0.45f, 0.75f, 1f),
                PlanetVisualType.Ocean => new Color(0.4f, 0.7f, 1f),
                PlanetVisualType.Ice => new Color(0.75f, 0.9f, 1f),
                PlanetVisualType.GasGiant => new Color(1f, 0.85f, 0.7f),
                PlanetVisualType.Molten => new Color(1f, 0.45f, 0.2f),
                _ => new Color(1f, 0.8f, 0.6f)
            };
            var sp = BuildSphereSprite(src, atmo, vt == PlanetVisualType.Molten, variant, "MiniPlanet_" + key);
            if (real == null) Object.Destroy(src);   // настоящая карта — общий ресурс, её не трогаем
            s_miniPlanetSprites[key] = sp;
            return sp;
        }

        /// <summary>Шарик из развёртки: свет справа, ночная сторона раскалённых миров тлеет, дымка по краю.</summary>
        private static Sprite BuildSphereSprite(Texture2D src, Color atmo, bool molten, int variant, string name)
        {
            const int R = 128;
            var tex = new Texture2D(R, R, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = name };
            var px = new Color32[R * R];
            var L = new Vector3(1f, 0.25f, 0.45f).normalized;
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
                if (molten) o += new Color(0.5f, 0.12f, 0.02f) * (1f - diff) * col.r;   // светится ночная сторона
                float rim = Mathf.Pow(1f - nz, 3f) * (0.25f + 0.75f * Mathf.Clamp01(diff * 1.5f));
                o = Color.Lerp(o, atmo, rim * 0.6f);
                o.a = edge;
                px[y * R + x] = o;
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), R / 0.64f);
        }
    }
}
