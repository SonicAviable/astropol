using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Голографические эффекты работы гражданских кораблей — без текста над кораблём.
    ///   • Разведка: веер сенсоров плавно прочёсывает систему, по нему бежит яркая линия сканирования;
    ///     от звезды расходятся волны сенсорного импульса, на орбитах вспыхивают отметки обнаруженного;
    ///     вокруг системы тонкой дугой заполняется кольцо прогресса.
    ///   • Монтаж: золотой тяговый луч с бегущими импульсами тянется к строительной площадке, где
    ///     собирается голограмма каркаса станции (спицы появляются по мере работы, кольца вращаются),
    ///     по каркасу сыплются сварочные искры и мерцает тёплый свет; кольцо каркаса — это и прогресс.
    /// Все части плавно появляются и гаснут, работают в реальном времени, рисуются только вблизи камеры.
    /// Объекты живут в мировых координатах (не вращаются вместе с кораблём), слой — как у корабля (туман войны).
    /// </summary>
    public class ShipWorkFx : MonoBehaviour
    {
        public enum Mode { None, Survey, Construct }

        private const int ArcPoints = 72;
        private const int FanSegments = 16;
        private const int Spokes = 8;
        private const float MaxCameraDistance = 170f;

        private static readonly Color SurveyCol = new Color(0.36f, 1f, 0.66f);
        private static readonly Color BuildCol = new Color(1f, 0.74f, 0.30f);

        private Mode _mode, _lastMode = Mode.Survey;
        private Color[] _fanColors;
        private float _fade, _clock, _shown, _pulseTimer, _blipTimer, _sparkTimer, _lightPhase;
        private Vector3 _center;
        private Color _col;

        private LineRenderer _ringBg, _ringFill, _scanLine, _beam, _spire;
        private readonly LineRenderer[] _spokes = new LineRenderer[Spokes];
        private SpriteRenderer _head, _frameOuter, _frameInner, _siteGlow;
        private readonly SpriteRenderer[] _pulses = new SpriteRenderer[3];
        private readonly float[] _pulseAge = { 9f, 9f, 9f };
        private MeshFilter _fanFilter;
        private MeshRenderer _fanRenderer;
        private Mesh _fan;
        private Material _beamMat;
        private Light _light;
        private Transform _ship;
        private bool _built;

        public static ShipWorkFx Attach(Transform ship)
        {
            var go = new GameObject("WorkFx");
            go.transform.SetParent(ship, false);
            var fx = go.AddComponent<ShipWorkFx>();
            fx._ship = ship;
            return fx;
        }

        // ==================== API ====================

        /// <summary>Разведка системы с центром center; progress 0..1.</summary>
        public void Survey(Vector3 center, float progress) => Set(Mode.Survey, center, progress, SurveyCol);

        /// <summary>Монтаж станции на площадке site; progress 0..1.</summary>
        public void Construct(Vector3 site, float progress) => Set(Mode.Construct, site, progress, BuildCol);

        /// <summary>Работа закончена или прервана — эффекты гаснут.</summary>
        public void Stop() => _mode = Mode.None;

        private void Set(Mode mode, Vector3 center, float progress, Color col)
        {
            if (mode != _mode && _fade < 0.05f) _shown = progress;   // новая работа — без «догоняния» с нуля
            _mode = mode;
            _lastMode = mode;
            _center = center;
            _col = col;
            // Прогресс растёт рывками раз в день — показываем плавно
            _shown = Mathf.MoveTowards(_shown, progress, Mathf.Max(0.02f, Mathf.Abs(progress - _shown) * 2.5f) * Time.unscaledDeltaTime);
            if (progress < _shown - 0.2f) _shown = progress;
        }

        // ==================== ПОСТРОЕНИЕ ====================

        private void Build()
        {
            _built = true;
            var fx = CombatFx.Instance;
            _ringBg = Line("RingBg", ArcPoints + 1, true);
            _ringFill = Line("RingFill", ArcPoints + 1, true);
            _scanLine = Line("ScanLine", 2, false);
            _beam = Line("TractorBeam", 2, false);
            _spire = Line("Spire", 2, false);
            for (int i = 0; i < Spokes; i++) _spokes[i] = Line("Spoke", 2, false);

            // Тяговый луч — свой материал: по нему бегут импульсы (смещение текстуры)
            _beamMat = new Material(fx.LineMaterial) { name = "TractorBeam" };
            _beamMat.mainTexture = PulseTexture();
            _beamMat.mainTextureScale = new Vector2(3f, 1f);
            _beam.sharedMaterial = _beamMat;
            _beam.textureMode = LineTextureMode.Tile;

            _head = Sprite("Head", fx.GlowSprite);
            _frameOuter = Sprite("FrameOuter", fx.RingSprite);
            _frameInner = Sprite("FrameInner", fx.RingSprite);
            _siteGlow = Sprite("SiteGlow", fx.GlowSprite);
            for (int i = 0; i < _pulses.Length; i++) _pulses[i] = Sprite("Pulse", fx.RingSprite);

            var fanGo = new GameObject("SensorFan");
            fanGo.transform.SetParent(transform, false);
            _fanFilter = fanGo.AddComponent<MeshFilter>();
            _fanRenderer = fanGo.AddComponent<MeshRenderer>();
            _fanRenderer.sharedMaterial = fx.LineMaterial;
            _fanRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _fanRenderer.receiveShadows = false;
            _fan = BuildFan();
            _fanFilter.sharedMesh = _fan;

            var lightGo = new GameObject("WorkLight");
            lightGo.transform.SetParent(transform, false);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;
            _light.range = 6f;
            _light.intensity = 0f;
            _light.enabled = false;

            int layer = _ship != null ? _ship.gameObject.layer : gameObject.layer;
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            SetAll(false);
        }

        private LineRenderer Line(string name, int points, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = points;
            lr.useWorldSpace = true;
            lr.loop = false;
            lr.numCapVertices = 2;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sharedMaterial = CombatFx.Instance.LineMaterial;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = loop ? LineAlignment.TransformZ : LineAlignment.View;
            if (loop) go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // плоское кольцо смотрит вверх
            return lr;
        }

        private SpriteRenderer Sprite(string name, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = CombatFx.Instance.SpriteMaterial;
            sr.sortingOrder = 58;
            return sr;
        }

        /// <summary>Веер: вершина в нуле, дуга радиуса 1 на ±1 рад вокруг +Z. Края по ширине мягкие (v текстуры), к дуге — затухание.</summary>
        private static Mesh BuildFan()
        {
            var m = new Mesh { name = "SensorFan" };
            var v = new Vector3[FanSegments + 2];
            var uv = new Vector2[v.Length];
            var c = new Color[v.Length];
            var tri = new int[FanSegments * 3];
            v[0] = Vector3.zero;
            uv[0] = new Vector2(0f, 0.5f);
            c[0] = new Color(1f, 1f, 1f, 0.55f);
            for (int i = 0; i <= FanSegments; i++)
            {
                float f = i / (float)FanSegments;
                float a = Mathf.Lerp(-1f, 1f, f);
                v[i + 1] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                uv[i + 1] = new Vector2(1f, f);
                c[i + 1] = new Color(1f, 1f, 1f, 0f);
            }
            for (int i = 0; i < FanSegments; i++) { tri[i * 3] = 0; tri[i * 3 + 1] = i + 1; tri[i * 3 + 2] = i + 2; }
            m.vertices = v; m.uv = uv; m.colors = c; m.triangles = tri;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return m;
        }

        /// <summary>Текстура с тремя яркими импульсами вдоль луча и мягкими краями по ширине.</summary>
        private static Texture2D PulseTexture()
        {
            const int W = 64, H = 16;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H * 2f - 1f;
                float across = Mathf.Exp(-v * v * 3.2f);
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W;
                    float pulse = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(u * Mathf.PI * 2f), 6f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, across * (0.35f + 0.65f * pulse)));
                }
            }
            tex.Apply();
            return tex;
        }

        private void SetAll(bool on)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            if (_light != null) _light.enabled = on && _light.intensity > 0.01f;
        }

        // ==================== КАДР ====================

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var cam = Camera.main;
            bool near = cam != null && (cam.transform.position - _center).sqrMagnitude < MaxCameraDistance * MaxCameraDistance;
            bool hidden = _ship != null && _ship.gameObject.layer == Core.Vision.HiddenLayer;
            float want = _mode != Mode.None && near && !hidden ? 1f : 0f;
            _fade = Mathf.MoveTowards(_fade, want, dt * (want > 0f ? 1.6f : 2.4f));
            if (_fade <= 0f)
            {
                if (_built && _ringBg.enabled) SetAll(false);
                return;
            }
            if (!_built) Build();
            if (!_ringBg.enabled) SetAll(true);
            if (_ship != null && gameObject.layer != _ship.gameObject.layer)
                foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = _ship.gameObject.layer;

            _clock += dt;
            Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
            if (_lastMode == Mode.Survey) TickSurvey(dt, face);
            else TickConstruct(dt, face);
        }

        private static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Кольцо прогресса: тусклое полное кольцо и яркая дуга от «12 часов» по часовой, со светящейся головой.</summary>
        private void DrawProgress(Vector3 center, float radius, float width, float progress, Quaternion face)
        {
            float y = center.y + 0.05f;
            var flat = Quaternion.Euler(90f, 0f, 0f);   // кольца лежат в плоскости системы, крен корабля их не наклоняет
            _ringBg.transform.rotation = flat;
            _ringFill.transform.rotation = flat;
            for (int i = 0; i <= ArcPoints; i++)
            {
                float a = i / (float)ArcPoints * Mathf.PI * 2f;
                _ringBg.SetPosition(i, new Vector3(center.x + Mathf.Sin(a) * radius, y, center.z + Mathf.Cos(a) * radius));
                float b = a * Mathf.Clamp01(progress);
                _ringFill.SetPosition(i, new Vector3(center.x + Mathf.Sin(b) * radius, y, center.z + Mathf.Cos(b) * radius));
            }
            _ringBg.widthMultiplier = width * 0.7f;
            _ringFill.widthMultiplier = width;
            _ringBg.startColor = _ringBg.endColor = A(_col, 0.13f * _fade);
            _ringFill.startColor = A(_col, 0.35f * _fade);
            _ringFill.endColor = A(Color.Lerp(_col, Color.white, 0.3f), 0.95f * _fade);
            _ringFill.enabled = progress > 0.004f;

            float h = Mathf.PI * 2f * Mathf.Clamp01(progress);
            _head.transform.position = new Vector3(center.x + Mathf.Sin(h) * radius, y, center.z + Mathf.Cos(h) * radius);
            _head.transform.rotation = face;
            _head.transform.localScale = Vector3.one * width * (5f + 1.2f * Mathf.Sin(_clock * 5f));
            _head.color = A(Color.Lerp(_col, Color.white, 0.4f), 0.9f * _fade);
            _head.enabled = progress > 0.004f;
        }

        private void TickSurvey(float dt, Quaternion face)
        {
            foreach (var s in _spokes) s.enabled = false;
            _beam.enabled = _spire.enabled = _frameOuter.enabled = _frameInner.enabled = _siteGlow.enabled = false;
            _fanRenderer.enabled = _scanLine.enabled = true;

            Vector3 ship = _ship != null ? _ship.position : transform.position;
            DrawProgress(_center, 6.2f, 0.11f, _shown, face);

            // Веер сенсоров: направлен на центр системы и плавно прочёсывает её из стороны в сторону
            Vector3 toCenter = _center - ship;
            float dist = Mathf.Max(1f, toCenter.magnitude);
            float sweep = Mathf.Sin(_clock * 0.8f) * 32f;
            var rot = Quaternion.LookRotation(toCenter.normalized, Vector3.up) * Quaternion.Euler(0f, sweep, 0f);
            const float spread = 0.32f;   // половина раствора веера, рад
            var fanT = _fanRenderer.transform;
            fanT.position = ship;
            fanT.rotation = rot;
            fanT.localScale = new Vector3((dist + 2.5f) * Mathf.Sin(spread), 1f, dist + 2.5f);
            if (_fanColors == null) _fanColors = new Color[_fan.vertexCount];
            _fanColors[0] = A(_col, 0.5f * _fade);
            for (int i = 1; i < _fanColors.Length; i++) _fanColors[i] = A(_col, 0f);
            _fan.colors = _fanColors;

            // Линия сканирования бежит поперёк веера
            float phase = Mathf.Sin(_clock * 2.3f) * spread;
            Vector3 dir = rot * new Vector3(Mathf.Sin(phase), 0f, Mathf.Cos(phase));
            _scanLine.SetPosition(0, ship);
            _scanLine.SetPosition(1, ship + dir * (dist + 2.3f));
            _scanLine.startWidth = 0.05f;
            _scanLine.endWidth = 0.16f;
            _scanLine.startColor = A(Color.Lerp(_col, Color.white, 0.5f), 0.9f * _fade);
            _scanLine.endColor = A(_col, 0f);

            // Сенсорные импульсы: волны расходятся от звезды
            if ((_pulseTimer -= dt) <= 0f && _mode == Mode.Survey)
            {
                _pulseTimer = 1.1f;
                for (int i = 0; i < _pulseAge.Length; i++) if (_pulseAge[i] >= 2.4f) { _pulseAge[i] = 0f; break; }
            }
            for (int i = 0; i < _pulses.Length; i++)
            {
                _pulseAge[i] += dt;
                float t = _pulseAge[i] / 2.4f;
                var p = _pulses[i];
                p.enabled = t < 1f;
                if (t >= 1f) continue;
                p.transform.position = _center + Vector3.up * 0.04f;
                p.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                p.transform.localScale = Vector3.one * Mathf.Lerp(1.5f, 13f, 1f - (1f - t) * (1f - t));
                p.color = A(_col, (1f - t) * 0.55f * _fade);
            }

            // Отметки обнаруженного: вспышки на орбитах
            if ((_blipTimer -= dt) <= 0f && _mode == Mode.Survey)
            {
                _blipTimer = Random.Range(0.35f, 0.8f);
                float a = Random.value * Mathf.PI * 2f, r = Random.Range(1.5f, 6f);
                CombatFx.Instance.Blip(_center + new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r),
                                       Color.Lerp(_col, Color.white, 0.5f), Random.Range(0.35f, 0.6f), 0.7f);
            }

            float li = 0.9f + 0.25f * Mathf.Sin(_clock * 3f);
            SetLight(ship + rot * Vector3.forward * 1.5f, _col, li * _fade, 5f);
        }

        private void TickConstruct(float dt, Quaternion face)
        {
            _fanRenderer.enabled = _scanLine.enabled = false;
            foreach (var p in _pulses) p.enabled = false;
            _beam.enabled = _spire.enabled = _frameOuter.enabled = _frameInner.enabled = _siteGlow.enabled = true;

            Vector3 site = _center;
            Vector3 ship = _ship != null ? _ship.position : transform.position;
            float prog = Mathf.Clamp01(_shown);
            const float R = 1.35f;

            // Каркас станции: кольцо-прогресс, спицы появляются по мере работы, два вращающихся контура
            DrawProgress(site, R, 0.08f, prog, face);
            for (int i = 0; i < Spokes; i++)
            {
                var s = _spokes[i];
                float appear = Mathf.Clamp01(prog * Spokes - i);
                s.enabled = appear > 0f;
                if (!s.enabled) continue;
                float a = (i / (float)Spokes) * Mathf.PI * 2f + _clock * 0.15f;
                Vector3 outer = site + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * R;
                s.SetPosition(0, site + Vector3.up * 0.05f);
                s.SetPosition(1, Vector3.Lerp(site, outer, appear) + Vector3.up * 0.05f);
                s.widthMultiplier = 0.05f;
                s.startColor = A(_col, 0.25f * _fade);
                s.endColor = A(Color.Lerp(_col, Color.white, 0.3f), 0.7f * _fade);
            }
            _spire.SetPosition(0, site);
            _spire.SetPosition(1, site + Vector3.up * (0.4f + 1.4f * prog));
            _spire.widthMultiplier = 0.07f;
            _spire.startColor = A(_col, 0.7f * _fade);
            _spire.endColor = A(_col, 0.05f * _fade);

            float holo = (0.35f + 0.5f * prog) * _fade * (0.85f + 0.15f * Mathf.Sin(_clock * 9f));
            _frameOuter.transform.position = site + Vector3.up * 0.06f;
            _frameOuter.transform.rotation = Quaternion.Euler(90f, _clock * 18f, 0f);
            _frameOuter.transform.localScale = Vector3.one * R * 2.6f;
            _frameOuter.color = A(_col, holo * 0.55f);
            _frameInner.transform.position = site + Vector3.up * (0.25f + 0.6f * prog);
            _frameInner.transform.rotation = Quaternion.Euler(90f, -_clock * 30f, 0f);
            _frameInner.transform.localScale = Vector3.one * R * 1.3f;
            _frameInner.color = A(Color.Lerp(_col, Color.white, 0.3f), holo * 0.7f);
            _siteGlow.transform.position = site + Vector3.up * 0.3f;
            _siteGlow.transform.rotation = face;
            _siteGlow.transform.localScale = Vector3.one * (1.6f + 0.6f * prog);
            _siteGlow.color = A(_col, 0.22f * _fade);

            // Тяговый луч: от корабля к площадке, по нему бегут импульсы
            Vector3 from = ship + (site - ship).normalized * 0.5f;
            _beam.SetPosition(0, from);
            _beam.SetPosition(1, site + Vector3.up * 0.3f);
            _beam.startWidth = 0.1f;
            _beam.endWidth = 0.32f;
            _beam.startColor = A(Color.Lerp(_col, Color.white, 0.4f), 0.85f * _fade);
            _beam.endColor = A(_col, 0.55f * _fade);
            _beamMat.mainTextureOffset = new Vector2(-_clock * 1.6f, 0f);

            // Сварка: искры по каркасу, тёплый мерцающий свет
            if ((_sparkTimer -= dt) <= 0f && _mode == Mode.Construct)
            {
                _sparkTimer = Random.Range(0.18f, 0.5f);
                float a = Random.value * Mathf.PI * 2f;
                float r = R * Random.Range(0.3f, 1f) * Mathf.Max(0.3f, prog);
                Vector3 at = site + new Vector3(Mathf.Sin(a) * r, Random.Range(0.05f, 0.5f), Mathf.Cos(a) * r);
                CombatFx.Instance.Sparks(at, new Color(1f, 0.78f, 0.4f), Random.Range(3, 7), 0.6f);
                _lightPhase = 1f;
            }
            _lightPhase = Mathf.MoveTowards(_lightPhase, 0f, dt * 4f);
            SetLight(site + Vector3.up * 0.6f, new Color(1f, 0.72f, 0.38f), (0.9f + 1.6f * _lightPhase) * _fade, 6f);
        }

        private void SetLight(Vector3 pos, Color c, float intensity, float range)
        {
            _light.transform.position = pos;
            _light.color = c;
            _light.range = range;
            _light.intensity = intensity;
            _light.enabled = intensity > 0.01f;
        }

        private void OnDestroy()
        {
            if (_fan != null) Destroy(_fan);
            if (_beamMat != null) Destroy(_beamMat);
        }
    }
}
