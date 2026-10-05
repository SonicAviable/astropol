using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Читаемость галактической карты (как в Stellaris):
    ///   • гиперкоридоры — ровные линии постоянной толщины на экране (не «рассыпаются» в пунктир при отдалении);
    ///     свои ярче и толще, чужие — цвета империи;
    ///   • туман войны — неизведанные звёзды тусклые, коридоры между ними едва видны и гаснут к неизвестному концу;
    ///   • значки поселений над звёздами — столица, колония, форпост — постоянного экранного размера,
    ///     мелкие исчезают при отдалении;
    ///   • набор систем, где стоят флоты игрока, — подписи этих систем видны дольше при отдалении.
    /// </summary>
    public partial class GalaxyView
    {
        private readonly List<StarAnimator> _starAnims = new List<StarAnimator>();
        private readonly List<float> _laneWidthPx = new List<float>();

        /// <summary>Системы, где сейчас находятся (или куда летят) флоты игрока.</summary>
        public static readonly HashSet<int> PlayerFleetSystems = new HashSet<int>();
        private float _fleetScanTimer, _markerDataTimer;

        private GameObject _markersRoot;
        private readonly List<SysMarker> _markers = new List<SysMarker>();

        private enum MarkerKind { None, Outpost, Colony, Capital }

        private class SysMarker
        {
            public StarSystem Sys;
            public Transform Root;
            public SpriteRenderer Back, Rim, Icon;
            public MarkerKind Kind;
            public float Alpha;
        }

        /// <summary>Игрок знает систему: изучил её сам или владеет ею.</summary>
        public static bool IsKnownToPlayer(StarSystem s) => s != null && (s.IsSurveyed || s.OwnerId == 0);

        private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        /// <summary>Мировых единиц на пиксель экрана на заданном расстоянии от камеры.</summary>
        private static float WorldPerPixel(Camera cam, float dist)
            => 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);

        // ==================== ТУМАН ВОЙНЫ И КОРИДОРЫ ====================

        /// <summary>Звёзды и коридоры по знаниям игрока. Вызывается при каждом изменении территорий и разведки.</summary>
        private void UpdateHyperlaneColors()
        {
            if (_generator == null) return;

            for (int i = 0; i < _starAnims.Count && i < _generator.Systems.Count; i++)
            {
                var s = _generator.Systems[i];
                if (_starAnims[i] == null) continue;
                _starAnims[i].Dim = IsKnownToPlayer(s) ? 1f : s.OwnerId > 0 ? 0.6f : 0.25f;
            }

            while (_laneWidthPx.Count < _hyperlaneRenderers.Count) _laneWidthPx.Add(1.8f);

            for (int i = 0; i < _hyperlaneRenderers.Count; i++)
            {
                var lane = _hyperlaneData[i];
                var lr = _hyperlaneRenderers[i];
                StarSystem a = _generator.Systems[lane.SystemA];
                StarSystem b = _generator.Systems[lane.SystemB];
                bool ka = IsKnownToPlayer(a), kb = IsKnownToPlayer(b);

                Color c;
                float alphaA, alphaB, px;
                if (a.OwnerId == 0 && b.OwnerId == 0)
                {
                    c = new Color(0.40f, 0.95f, 1.00f);          // внутренние коридоры империи игрока
                    alphaA = alphaB = 0.78f;
                    px = 2.4f;
                }
                else if (a.OwnerId > 0 && a.OwnerId == b.OwnerId)
                {
                    c = new Color(1.00f, 0.52f, 0.46f);          // коридоры чужой империи
                    alphaA = ka ? 0.55f : 0.28f;
                    alphaB = kb ? 0.55f : 0.28f;
                    px = 2.0f;
                }
                else
                {
                    c = new Color(0.60f, 0.78f, 1.00f);          // нейтральные / пограничные
                    alphaA = ka ? 0.45f : 0.05f;
                    alphaB = kb ? 0.45f : 0.05f;
                    px = 1.7f;
                }
                _laneWidthPx[i] = px;

                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                    new[] { new GradientAlphaKey(alphaA, 0f), new GradientAlphaKey(alphaB, 1f) });
                lr.colorGradient = g;
            }

            RefreshMarkerData();
        }

        // ==================== ЗНАЧКИ ПОСЕЛЕНИЙ ====================

        private void CreateSystemMarkers()
        {
            _markersRoot = new GameObject("SystemMarkers");
            _markersRoot.transform.SetParent(transform, false);
            var mat = FleetIndicator.SpriteMaterial;

            foreach (var sys in _generator.Systems)
            {
                var root = new GameObject($"Marker_{sys.Id}").transform;
                root.SetParent(_markersRoot.transform, false);
                root.position = sys.Position;

                SpriteRenderer Make(string name, Sprite sprite, int order, float size)
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(root, false);
                    go.transform.localScale = Vector3.one * size;
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = sprite;
                    if (mat != null) sr.sharedMaterial = mat;
                    sr.sortingOrder = order;
                    return sr;
                }

                var m = new SysMarker
                {
                    Sys = sys,
                    Root = root,
                    Back = Make("Back", FleetIndicator.GetDiscSprite(), 30, 1f),
                    Rim = Make("Rim", FleetIndicator.GetRingSprite(), 31, 1f),
                    Icon = Make("Icon", LGIcons.Get(LGIcon.Planet), 32, 0.62f),
                };
                root.gameObject.SetActive(false);
                _markers.Add(m);
            }
            RefreshMarkerData();
        }

        private static bool IsCapital(StarSystem s)
        {
            if (s.OwnerId == 0) return s.Id == EconomyManager.PlayerCapitalId;
            var ai = AIEmpireManager.Instance;
            return ai != null && s.OwnerId == AIEmpireManager.AIOwnerId && s.Id == ai.CapitalSystemId;
        }

        /// <summary>Тип значка для каждой системы (столица / колония / форпост).</summary>
        private void RefreshMarkerData()
        {
            foreach (var m in _markers)
            {
                var s = m.Sys;
                MarkerKind kind = MarkerKind.None;
                if (s.OwnerId >= 0 && s.HasStarbase)
                {
                    bool colony = false;
                    if (s.Planets != null)
                        foreach (var p in s.Planets) if (p.Population > 0) { colony = true; break; }
                    kind = IsCapital(s) ? MarkerKind.Capital : colony ? MarkerKind.Colony : MarkerKind.Outpost;
                }
                if (kind == m.Kind && kind != MarkerKind.None) { ApplyMarkerStyle(m); continue; }
                m.Kind = kind;
                ApplyMarkerStyle(m);
            }
        }

        private static void ApplyMarkerStyle(SysMarker m)
        {
            if (m.Kind == MarkerKind.None) return;
            Color owner = m.Sys.OwnerId == 0 ? FleetIndicator.OwnColor : FleetIndicator.EnemyColor;
            m.Icon.sprite = LGIcons.Get(m.Kind == MarkerKind.Capital ? LGIcon.Star : m.Kind == MarkerKind.Colony ? LGIcon.Planet : LGIcon.Starbase);
            m.Icon.transform.localScale = Vector3.one * (m.Kind == MarkerKind.Outpost ? 0.5f : 0.62f);
            // Прозрачность сохраняем — ею управляет плавное появление по зуму
            Color rim = m.Kind == MarkerKind.Capital ? new Color(1f, 0.82f, 0.35f) : owner;
            Color icon = m.Kind == MarkerKind.Capital ? new Color(1f, 0.9f, 0.55f) : Color.Lerp(owner, Color.white, 0.35f);
            rim.a = m.Alpha; icon.a = m.Alpha;
            m.Rim.color = rim;
            m.Icon.color = icon;
            m.Back.color = new Color(owner.r * 0.12f, owner.g * 0.12f, owner.b * 0.12f, 0.92f * m.Alpha);
        }

        // ==================== КАЖДЫЙ КАДР ====================

        private void UpdateReadability()
        {
            var cam = Camera.main;
            if (cam == null || _generator == null) return;
            Vector3 camPos = cam.transform.position;
            float dt = Time.unscaledDeltaTime;

            // Флоты игрока — раз в 0,25 с
            _fleetScanTimer -= dt;
            if (_fleetScanTimer <= 0f)
            {
                _fleetScanTimer = 0.25f;
                PlayerFleetSystems.Clear();
                var fm = FleetManager.Instance;
                if (fm != null)
                    foreach (var f in fm.AllFleets)
                    {
                        var d = f?.Data;
                        if (d == null || d.Destroyed || d.OwnerId != 0) continue;
                        if (d.CurrentSystemId >= 0) PlayerFleetSystems.Add(d.CurrentSystemId);
                        if (d.State == FleetState.InHyperlane && d.TargetSystemId >= 0) PlayerFleetSystems.Add(d.TargetSystemId);
                    }
            }

            // Колонии появляются и исчезают — значки пересчитываем раз в секунду
            _markerDataTimer -= dt;
            if (_markerDataTimer <= 0f) { _markerDataTimer = 1f; RefreshMarkerData(); }

            // Коридоры: толщина в пикселях экрана, отдельно для каждого конца (камера наклонена)
            if (_hyperlanesParent != null && _hyperlanesParent.activeInHierarchy)
            {
                for (int i = 0; i < _hyperlaneRenderers.Count; i++)
                {
                    var lr = _hyperlaneRenderers[i];
                    if (lr == null) continue;
                    float px = i < _laneWidthPx.Count ? _laneWidthPx[i] : 1.8f;
                    var lane = _hyperlaneData[i];
                    float da = Vector3.Distance(camPos, _generator.Systems[lane.SystemA].Position);
                    float db = Vector3.Distance(camPos, _generator.Systems[lane.SystemB].Position);
                    lr.startWidth = WorldPerPixel(cam, da) * px;
                    lr.endWidth = WorldPerPixel(cam, db) * px;
                }
            }

            // Значки поселений: постоянный размер на экране, мелкие гаснут при отдалении
            if (_markersRoot == null || !_markersRoot.activeInHierarchy) return;
            Quaternion rot = cam.transform.rotation;
            Vector3 up = cam.transform.up;
            foreach (var m in _markers)
            {
                if (m.Kind == MarkerKind.None)
                {
                    if (m.Root.gameObject.activeSelf) m.Root.gameObject.SetActive(false);
                    m.Alpha = 0f;
                    continue;
                }
                float dist = Vector3.Distance(camPos, m.Sys.Position);
                float target = m.Kind switch
                {
                    MarkerKind.Capital => 1f,
                    MarkerKind.Colony => 1f - Smooth(230f, 290f, dist),
                    _ => 1f - Smooth(120f, 170f, dist)
                };
                m.Alpha = Mathf.MoveTowards(m.Alpha, target, dt * 4f);
                bool show = m.Alpha > 0.01f;
                if (m.Root.gameObject.activeSelf != show) m.Root.gameObject.SetActive(show);
                if (!show) continue;

                float wpp = WorldPerPixel(cam, dist);
                float sizePx = m.Kind == MarkerKind.Outpost ? 13f : m.Kind == MarkerKind.Colony ? 17f : 21f;
                m.Root.position = m.Sys.Position + up * (wpp * (sizePx * 0.5f + 9f));
                m.Root.rotation = rot;
                m.Root.localScale = Vector3.one * (wpp * sizePx);

                SetAlpha(m.Back, 0.92f * m.Alpha);
                SetAlpha(m.Rim, m.Alpha);
                SetAlpha(m.Icon, m.Alpha);
            }
        }

        private static void SetAlpha(SpriteRenderer sr, float a)
        {
            var c = sr.color;
            if (Mathf.Abs(c.a - a) < 0.004f) return;
            c.a = a;
            sr.color = c;
        }
    }
}
