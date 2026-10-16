using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Прицел выбранной системы на карте галактики — голографическая «захватка цели».
    ///   • размер — по системе: кольцо охватывает все орбиты мини-системы (планеты внутри, а не под кольцом),
    ///     издалека — не меньше ~34 пикселей;
    ///   • слои (всё лежит в плоскости галактики, под планетами и звездой):
    ///       световое пятно под системой · вращающийся луч сканера · тонкое основное кольцо ·
    ///       кольцо-шкала с делениями (медленно вращается) · четыре скобки захвата · волны-пинги ·
    ///       тонкий световой столб над звездой;
    ///   • выбор — «захват»: кольца раскрываются с лёгким перелётом, скобки сходятся снаружи к цели,
    ///     от звезды расходится волна; потом волна повторяется раз в несколько секунд, скобки «дышат».
    /// Цвет — владельца системы (своя — бирюзовая). Текстуры процедурные, с мипмапами — края чёткие на любом зуме.
    /// </summary>
    public partial class GalaxyView
    {
        private SpriteRenderer _selPool, _selSweep, _selTicks, _selBrackets, _selPing;
        private LineRenderer _selBeam, _selRing, _selRingGlow;
        private float _selRingDrawn = -1f;
        private const int SelRingSegments = 160;
        private float _selRadius = 3f, _selPingT = -1f, _selNextPing;
        private Material _selAdditive;

        private const int SelOrderPool = 4, SelOrderSweep = 6, SelOrderRing = 9, SelOrderPing = 10, SelOrderBrackets = 11;

        // Радиусы колец в спрайтах (доля половины ширины спрайта)
        private const float TicksRn = 0.95f, BracketsRn = 0.95f;

        private static Sprite s_selPool, s_selSweep, s_selTicks, s_selBrackets, s_selPing;

        private void CreateHolographicBeacon()
        {
            _beaconRoot = new GameObject("TacticalHologramBeacon");
            _beaconRoot.transform.SetParent(transform, false);

            var alphaMat = new Material(GetSpriteShader());
            var add = Resources.Load<Shader>("Shaders/CombatAdditive");
            _selAdditive = add != null ? new Material(add) : alphaMat;
            _selAdditive.renderQueue = 3000;   // сортировка по sortingOrder, вместе со спрайтами карты

            _selPool = SelLayer("Pool", PoolSprite(), _selAdditive, SelOrderPool);
            _selSweep = SelLayer("Sweep", SweepSprite(), _selAdditive, SelOrderSweep);
            // Основное кольцо — линией постоянной экранной толщины (чёткое на любом зуме) и мягким ореолом
            _selRingGlow = SelCircle("RingGlow", _selAdditive, SelOrderRing);
            _selRing = SelCircle("Ring", alphaMat, SelOrderRing);
            _selTicks = SelLayer("Ticks", SelTicksSprite(), alphaMat, SelOrderRing);
            _selPing = SelLayer("Ping", PingSprite(), _selAdditive, SelOrderPing);
            _selBrackets = SelLayer("Brackets", BracketsSprite(), alphaMat, SelOrderBrackets);

            var beamObj = new GameObject("Beam");
            beamObj.transform.SetParent(_beaconRoot.transform, false);
            _selBeam = beamObj.AddComponent<LineRenderer>();
            _selBeam.useWorldSpace = false;
            _selBeam.positionCount = 2;
            _selBeam.SetPosition(0, Vector3.zero);
            _selBeam.SetPosition(1, new Vector3(0f, 9f, 0f));
            _selBeam.numCapVertices = 0;
            _selBeam.sortingOrder = SelOrderRing;
            _selBeam.material = _selAdditive;
            _selBeam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _beaconRoot.SetActive(false);
        }

        private LineRenderer SelCircle(string name, Material mat, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_beaconRoot.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = SelRingSegments;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 0;
            lr.alignment = LineAlignment.TransformZ;   // лента лежит в плоскости галактики
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            lr.sharedMaterial = mat;
            lr.sortingOrder = order;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        /// <summary>Перестроить окружность кольца (только когда радиус заметно изменился).</summary>
        private void DrawSelRing(float r)
        {
            if (Mathf.Abs(r - _selRingDrawn) < r * 0.002f) return;
            _selRingDrawn = r;
            for (int i = 0; i < SelRingSegments; i++)
            {
                float a = i / (float)SelRingSegments * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);   // в осях объекта, повёрнутого плашмя
                _selRing.SetPosition(i, p);
                _selRingGlow.SetPosition(i, p);
            }
        }

        private SpriteRenderer SelLayer(string name, Sprite sp, Material mat, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_beaconRoot.transform, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // плашмя в плоскости галактики
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sharedMaterial = mat;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Показать прицел на системе: радиус по её орбитам, цвет владельца, анимация захвата.</summary>
        private void ShowSelection(StarSystem sys)
        {
            if (_beaconRoot == null || sys == null) return;
            _beaconRoot.transform.position = sys.Position + new Vector3(0f, 0.02f, 0f);
            _beaconRoot.SetActive(true);

            int n = sys.Planets != null ? sys.Planets.Count : 0;
            _selRadius = n > 0 && IsKnownToPlayer(sys) ? MiniInnerOrbit + (n - 1) * MiniOrbitStep + 1.0f : 2.8f;

            _currentBeaconColor = sys.OwnerId == 0 ? new Color(0.24f, 0.95f, 1f, 1f)
                                : sys.OwnerId > 0 ? FleetIndicator.OwnerColor(sys.OwnerId)
                                : new Color(0.62f, 0.84f, 1f, 1f);

            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(_currentBeaconColor, 0f), new GradientColorKey(_currentBeaconColor, 1f) },
                      new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0.18f, 0.35f), new GradientAlphaKey(0f, 1f) });
            _selBeam.colorGradient = g;

            _deployProgress = 0f;
            _animTimer = 0f;
            _selPingT = 0f;                       // волна при захвате
            _selNextPing = 3.2f;
            _selTicks.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _selRingDrawn = -1f;
            _selSweep.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        private void UpdateBeacon()
        {
            if (_beaconRoot == null || !_beaconRoot.activeSelf || _camTransform == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            float dt = Time.unscaledDeltaTime;
            _animTimer += dt;
            _deployProgress = Mathf.Clamp01(_deployProgress + dt / 0.45f);
            float deploy = EaseOutBack(_deployProgress);
            float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_deployProgress * 2.2f));

            // Радиус: по системе, но издалека — не меньше ~34 пикселей
            float dist = Vector3.Distance(_beaconRoot.transform.position, _camTransform.position);
            float wpp = WorldPerPixel(cam, dist);
            float R = Mathf.Max(_selRadius, wpp * 34f);
            Color c = _currentBeaconColor;

            // Основное кольцо и шкала
            DrawSelRing(R * Mathf.Lerp(0.75f, 1f, deploy));
            _selRing.startWidth = _selRing.endWidth = wpp * 1.7f;
            _selRing.startColor = _selRing.endColor = new Color(c.r, c.g, c.b, 0.9f * fadeIn);
            _selRingGlow.startWidth = _selRingGlow.endWidth = wpp * 7f;
            _selRingGlow.startColor = _selRingGlow.endColor = new Color(c.r, c.g, c.b, 0.16f * fadeIn);
            SetRing(_selTicks, R * 1.09f * Mathf.Lerp(0.8f, 1f, deploy), TicksRn);
            _selTicks.transform.Rotate(Vector3.forward, 6f * dt, Space.Self);
            _selTicks.color = new Color(c.r, c.g, c.b, 0.55f * fadeIn);

            // Скобки сходятся снаружи (захват) и затем слегка «дышат»
            float lockOn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_deployProgress * 1.4f));
            float breathe = 1f + Mathf.Sin(_animTimer * 2.4f) * 0.012f;
            SetRing(_selBrackets, R * Mathf.Lerp(1.75f, 1.2f, lockOn) * breathe, BracketsRn);
            _selBrackets.transform.localRotation = Quaternion.Euler(90f, 0f, Mathf.Lerp(-35f, 0f, lockOn));
            _selBrackets.color = new Color(Mathf.Lerp(c.r, 1f, 0.25f), Mathf.Lerp(c.g, 1f, 0.25f), Mathf.Lerp(c.b, 1f, 0.25f), 0.95f * fadeIn);

            // Световое пятно и луч сканера
            SetRing(_selPool, R * 1.35f, 1f);
            _selPool.color = new Color(c.r, c.g, c.b, (0.10f + Mathf.Sin(_animTimer * 1.6f) * 0.02f) * fadeIn);
            SetRing(_selSweep, R * 0.985f, 1f);
            _selSweep.transform.Rotate(Vector3.forward, 70f * dt, Space.Self);   // кромка впереди, хвост позади
            _selSweep.color = new Color(c.r, c.g, c.b, 0.22f * fadeIn);

            // Волна-пинг: расходится от центра за кольцо и гаснет
            if (_selPingT < 0f && _animTimer >= _selNextPing) { _selPingT = 0f; _selNextPing = _animTimer + 3.2f; }
            if (_selPingT >= 0f)
            {
                _selPingT += dt / 1.25f;
                float k = Mathf.Clamp01(_selPingT);
                float e = 1f - (1f - k) * (1f - k);
                SetRing(_selPing, R * Mathf.Lerp(0.25f, 1.55f, e), 1f);
                _selPing.color = new Color(c.r, c.g, c.b, 0.55f * (1f - k) * (1f - k));
                if (_selPingT >= 1f) _selPingT = -1f;
            }
            else _selPing.color = Color.clear;

            // Световой столб — тонкий, мягкий, высота растёт при захвате
            float h = Mathf.Clamp(R * 1.4f, 3f, 14f) * fadeIn;
            _selBeam.SetPosition(1, new Vector3(0f, h, 0f));
            float w = wpp * 2.2f;
            _selBeam.startWidth = w;
            _selBeam.endWidth = w * 0.3f;
        }

        /// <summary>Масштаб слоя: ring — нужный радиус в мире, rn — радиус кольца в спрайте (доля половины ширины 1 ед.).</summary>
        private static void SetRing(SpriteRenderer sr, float radius, float rn)
        {
            float s = radius / (0.5f * rn);
            sr.transform.localScale = new Vector3(s, s, 1f);
        }

        // ==================== СПРАЙТЫ ПРИЦЕЛА (1 ед. шириной) ====================

        private static Sprite SelSprite(int res, System.Func<float, float, float, float> alpha, string name)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = name, hideFlags = HideFlags.DontSave };
            var px = new Color32[res * res];
            float c = (res - 1) * 0.5f;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = (Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 360f) % 360f;
                px[y * res + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(d, ang, c)) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            var sp = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
            sp.hideFlags = HideFlags.DontSave;
            return sp;
        }

        /// <summary>Линия толщиной wPx пикселей текстуры на радиусе rn, со сглаженными краями.</summary>
        private static float Line(float d, float rn, float wPx, float half) => Mathf.Clamp01(wPx * 0.5f + 0.5f - Mathf.Abs(d - rn) * half);

        /// <summary>Шкала: короткие деления каждые 5°, длинные — каждые 45°, тонкая линия-основа с разрывами у длинных.</summary>
        private static Sprite SelTicksSprite()
        {
            if (s_selTicks != null) return s_selTicks;
            s_selTicks = SelSprite(1024, (d, ang, half) =>
            {
                float near5 = Mathf.Abs(Mathf.Repeat(ang + 2.5f, 5f) - 2.5f);
                float near45 = Mathf.Abs(Mathf.Repeat(ang + 22.5f, 45f) - 22.5f);
                float tickW = 0.45f;                                            // градусы
                float minor = near5 < tickW && d > TicksRn - 0.035f && d < TicksRn ? 0.65f : 0f;
                float major = near45 < 0.85f && d > TicksRn - 0.075f && d < TicksRn + 0.012f ? 1f : 0f;
                float baseLine = Line(d, TicksRn, 2.6f, half) * (near45 > 4f ? 0.55f : 0f);
                return Mathf.Max(baseLine, Mathf.Max(minor, major));
            }, "SelTicks");
            return s_selTicks;
        }

        /// <summary>Четыре скобки захвата: дуги по 22° на диагоналях с короткими зубцами внутрь на концах.</summary>
        private static Sprite BracketsSprite()
        {
            if (s_selBrackets != null) return s_selBrackets;
            s_selBrackets = SelSprite(1024, (d, ang, half) =>
            {
                float diag = Mathf.Abs(Mathf.Repeat(ang, 90f) - 45f);       // 0 — на диагонали (45°, 135°…)
                float arc = diag < 11f ? Line(d, BracketsRn, 8f, half) : 0f;
                // Зубцы внутрь на концах дуги
                float tooth = Mathf.Abs(diag - 10.3f) < 0.9f && d > BracketsRn - 0.06f && d < BracketsRn + 0.006f ? 1f : 0f;
                return Mathf.Max(arc, tooth);
            }, "SelBrackets");
            return s_selBrackets;
        }

        /// <summary>Мягкое световое пятно: ярче к центру, плавно гаснет к краю.</summary>
        private static Sprite PoolSprite()
        {
            if (s_selPool != null) return s_selPool;
            s_selPool = SelSprite(256, (d, ang, half) =>
            {
                float k = Mathf.Clamp01(1f - d);
                return k * k * (3f - 2f * k);
            }, "SelPool");
            return s_selPool;
        }

        /// <summary>Луч сканера: яркая передняя кромка и длинный гаснущий хвост (110°), к центру прозрачнее.</summary>
        private static Sprite SweepSprite()
        {
            if (s_selSweep != null) return s_selSweep;
            s_selSweep = SelSprite(512, (d, ang, half) =>
            {
                if (d > 1f) return 0f;
                float tail = Mathf.Repeat(ang, 360f);                          // 0° — передняя кромка, хвост — по возрастанию угла
                float trail = tail < 110f ? Mathf.Pow(1f - tail / 110f, 2.2f) : 0f;
                float edge = Mathf.Exp(-tail * tail / 2f);                      // тонкая яркая кромка
                float radial = Mathf.SmoothStep(0.15f, 0.6f, d) * (1f - Mathf.SmoothStep(0.92f, 1f, d));
                return (trail * 0.75f + edge) * radial;
            }, "SelSweep");
            return s_selSweep;
        }

        /// <summary>Волна-пинг: мягкое кольцо с ярким краем.</summary>
        private static Sprite PingSprite()
        {
            if (s_selPing != null) return s_selPing;
            s_selPing = SelSprite(512, (d, ang, half) =>
            {
                float k = Mathf.Exp(-Mathf.Pow((d - 0.94f) / 0.035f, 2f));
                float inner = d < 0.94f ? Mathf.Exp(-Mathf.Pow((d - 0.94f) / 0.12f, 2f)) * 0.35f : 0f;
                return Mathf.Max(k, inner);
            }, "SelPing");
            return s_selPing;
        }
    }
}
