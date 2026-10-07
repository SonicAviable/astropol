using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    [RequireComponent(typeof(GalaxyGenerator))]
    public partial class GalaxyView : MonoBehaviour
    {
        public static GalaxyView Instance { get; private set; }

        private GalaxyGenerator _generator;
        private readonly List<GameObject> _starNodes = new List<GameObject>();
        private readonly Dictionary<int, SystemNameplate> _nameplates = new Dictionary<int, SystemNameplate>();
        private readonly List<LineRenderer> _hyperlaneRenderers = new List<LineRenderer>();
        private readonly List<Hyperlane> _hyperlaneData = new List<Hyperlane>();
        private GameObject _hyperlanesParent;

        private GameObject _organicBorderPlane;
        private Material _borderMat;
        private const float GalaxyPlaneSize = 600f;

        private readonly Dictionary<int, GameObject> _empireLabels = new Dictionary<int, GameObject>();

        private GameObject _beaconRoot;
        /// <summary>Маяк выбранной системы (главное меню прячет его).</summary>
        public GameObject BeaconRoot => _beaconRoot;
        private SpriteRenderer _beaconOuterSr;
        private SpriteRenderer _beaconInnerSr;
        private LineRenderer _beaconVerticalBeam;
        private Transform _camTransform;
        private int _selectedSystemId = -1;
        private float _animTimer;
        private float _deployProgress = 1f;
        private Color _currentBeaconColor = new Color(0.3f, 0.9f, 1f, 1f);

        private Sprite _starCoreSprite;
        private Sprite _starHotSprite;
        private Sprite _starRingSprite;
        private Sprite _starFlareSprite, _starCoronaSprite, _accretionSprite;
        private Sprite _beaconOuterSprite;
        private Sprite _beaconInnerSprite;

        private MapModeController.MapMode _currentMapMode = MapModeController.MapMode.Simple;
        public MapModeController.MapMode CurrentMapMode => _currentMapMode;
        public Sprite TacticalReticleSprite => _beaconOuterSprite;
        public IReadOnlyList<StarSystem> AllSystems => _generator != null ? _generator.Systems : null;

        private static Shader GetSpriteShader() => ShaderCache.Sprite ?? Shader.Find("Sprites/Default");
        private static Shader GetLineShader()   => ShaderCache.Unlit ?? Shader.Find("Sprites/Default");

        /// <summary>Карта построена (звёзды, коридоры, фон) — для экрана загрузки.</summary>
        public static bool IsBuilt { get; private set; }

        private void Awake()
        {
            IsBuilt = false;
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            _generator = GetComponent<GalaxyGenerator>();
            if (_generator == null) return;
            if (Camera.main != null) _camTransform = Camera.main.transform;

            if (_generator.Systems == null || _generator.Systems.Count == 0)
                _generator.GenerateGalaxy();

            CreateProceduralSprites();
            CreateOrganicBorderPlane();
            RenderGalaxy();
            CreateBackdrop();
            CreateHolographicBeacon();
            RefreshTerritoryVisuals();
            UIManager.Instance?.RefreshOutliner();
            IsBuilt = true;
        }

        // ==================== СПРАЙТЫ ====================

        private void CreateProceduralSprites()
        {
            _starCoreSprite    = GenerateStarSprite(256);
            _starHotSprite     = GenerateHotCoreSprite(64);
            _starRingSprite    = GenerateTwinkleRingSprite(256);
            _starFlareSprite   = StarFx.FlareSprite(256);
            _starCoronaSprite  = StarFx.CoronaSprite(256);
            _accretionSprite   = StarFx.AccretionSprite(256);
            _beaconOuterSprite = GenerateOuterBeaconSprite(256);
            _beaconInnerSprite = GenerateInnerRingSprite(256);
        }

        /// <summary>Раскалённое белое ядро звезды (рисуется поверх цветного ореола, как в Stellaris).</summary>
        private static Sprite GenerateHotCoreSprite(int res)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float c = (res - 1) * 0.5f;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float n = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;   // 0..1
                float a = Mathf.Exp(-n * n * 9f) + Mathf.Exp(-n * n * 40f) * 0.6f;
                a *= 1f - Mathf.SmoothStep(0.8f, 1f, n);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
        }

        /// <summary>
        /// Компактная звезда: яркое белое ядро + мягкое свечение.
        /// Край слегка неровный (perlin noise) — при вращении видно, что звезда «живая».
        /// Без лучей, без большого круга.
        /// </summary>
        private Sprite GenerateStarSprite(int res)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 c = new Vector2(res * 0.5f, res * 0.5f);
            float maxR = res * 0.5f;

            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float dx = x - c.x;
                float dy = y - c.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float norm = d / maxR;

                if (norm > 0.85f) { tex.SetPixel(x, y, Color.clear); continue; }

                // Угловая модуляция радиуса — неровный край (звезда «дышит» при вращении)
                float angle = Mathf.Atan2(dy, dx);
                float nx = Mathf.Cos(angle) * 2.2f + 5f;
                float ny = Mathf.Sin(angle) * 2.2f + 5f;
                float noise = Mathf.PerlinNoise(nx, ny);        // 0..1
                float radialWarp = 0.88f + noise * 0.24f;        // 0.88..1.12

                float warped = norm / radialWarp;

                // Яркое ядро
                float coreHot  = Mathf.Exp(-warped * warped * 200f) * 1.4f;
                float coreWarm = Mathf.Exp(-warped * warped * 60f)  * 1.0f;
                float core = Mathf.Clamp01(coreHot + coreWarm);

                // Мягкое свечение вокруг
                float glow = Mathf.Exp(-warped * warped * 14f) * 0.55f;

                // Еле заметная «дымка»
                float haze = Mathf.Exp(-warped * warped * 5f)  * 0.15f;

                // Плавный спад к краю спрайта — нет резкого круга
                float edgeFade = 1f - Mathf.SmoothStep(0.7f, 0.85f, norm);

                float alpha = Mathf.Clamp01((core + glow + haze) * edgeFade);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
        }

        /// <summary>
        /// Тонкое кольцо — расходится от звезды в момент «моргания» (twinkle).
        /// Невидимо в обычном состоянии.
        /// </summary>
        private Sprite GenerateTwinkleRingSprite(int res)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 c = new Vector2(res * 0.5f, res * 0.5f);
            float maxR = res * 0.5f;
            float ringCenter = 0.32f;
            float ringWidth = 0.10f;

            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float dx = x - c.x;
                float dy = y - c.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float norm = d / maxR;

                if (norm > 0.85f) { tex.SetPixel(x, y, Color.clear); continue; }

                float ring = Mathf.Exp(-Mathf.Pow((norm - ringCenter) / ringWidth, 2f));
                float edgeFade = 1f - Mathf.SmoothStep(0.75f, 0.85f, norm);
                float alpha = Mathf.Clamp01(ring * edgeFade * 0.7f);

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
        }

        private Sprite GenerateOuterBeaconSprite(int res)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Vector2 c = new Vector2(res * 0.5f, res * 0.5f);
            float rTarget = res * 0.42f;
            float lineW = res * 0.024f;

            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                Vector2 p = new Vector2(x, y);
                float d = Vector2.Distance(p, c);
                float delta = Mathf.Abs(d - rTarget);
                if (delta < lineW)
                {
                    float a = Mathf.Clamp01(1f - (delta / lineW));
                    float angle = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg;
                    if (angle < 0) angle += 360f;
                    float gapDist = Mathf.Min(
                        Mathf.Abs(Mathf.DeltaAngle(angle, 0f)),
                        Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(angle, 90f)),
                        Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(angle, 180f)), Mathf.Abs(Mathf.DeltaAngle(angle, 270f)))));
                    if (gapDist > 14f)
                    {
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * 0.95f));
                        continue;
                    }
                }
                tex.SetPixel(x, y, Color.clear);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
        }

        private Sprite GenerateInnerRingSprite(int res)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Vector2 c = new Vector2(res * 0.5f, res * 0.5f);
            float rTarget = res * 0.33f;
            float lineW = res * 0.014f;

            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                Vector2 p = new Vector2(x, y);
                float d = Vector2.Distance(p, c);
                float delta = Mathf.Abs(d - rTarget);
                if (delta < lineW)
                {
                    float a = Mathf.Clamp01(1f - (delta / lineW));
                    float angle = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg;
                    if (angle < 0) angle += 360f;
                    float dashPattern = Mathf.Repeat(angle, 30f);
                    if (dashPattern < 18f)
                    {
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * 0.85f));
                        continue;
                    }
                }
                tex.SetPixel(x, y, Color.clear);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
        }

        // ==================== ТЕРРИТОРИИ ====================

        private void CreateOrganicBorderPlane()
        {
            _organicBorderPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _organicBorderPlane.name = "StellarisOrganicBorderPlane";
            _organicBorderPlane.transform.SetParent(transform, false);
            _organicBorderPlane.transform.position = new Vector3(0, -0.45f, 0);
            _organicBorderPlane.transform.rotation = Quaternion.Euler(90f, 0, 0);
            _organicBorderPlane.transform.localScale = new Vector3(GalaxyPlaneSize, GalaxyPlaneSize, 1f);

            var col = _organicBorderPlane.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Шейдер лежит в Resources — попадает в сборку без ручной настройки «Always Included»
            Shader borderShader = Resources.Load<Shader>("Shaders/StellarisOrganicBorder");
            if (borderShader == null) borderShader = Shader.Find("Stellaris/OrganicBorders");
            if (borderShader == null) { Debug.LogWarning("[GalaxyView] Шейдер границ не найден"); Destroy(_organicBorderPlane); _organicBorderPlane = null; return; }
            _borderMat = new Material(borderShader);
            _claimRadius = ComputeClaimRadius();

            var mr = _organicBorderPlane.GetComponent<MeshRenderer>();
            mr.material = _borderMat;
            mr.sortingOrder = -5;
        }

        public void RefreshTerritoryVisuals()
        {
            if (_generator == null || _borderMat == null) return;

            var playerSystems = new List<StarSystem>();
            var enemySystems = new List<StarSystem>();
            var byOwner = new Dictionary<int, List<StarSystem>>();

            foreach (var s in _generator.Systems)
            {
                if (s.OwnerId == 0 && s.HasStarbase)
                    playerSystems.Add(s);
                else if (s.OwnerId > 0 && s.HasStarbase)
                {
                    enemySystems.Add(s);
                    if (!byOwner.TryGetValue(s.OwnerId, out var l)) byOwner[s.OwnerId] = l = new List<StarSystem>();
                    l.Add(s);
                }
            }

            if (playerSystems.Count == 0)
            {
                foreach (var s in _generator.Systems)
                {
                    if (s.OwnerId == 0) { playerSystems.Add(s); break; }
                }
            }

            // Территории: все системы со звёздной базой, z — владелец (0 игрок, 1 и 2 — империи ИИ).
            // Вдоль коридоров между своими системами добавляем промежуточные точки —
            // территория не рвётся на длинных переходах (как в Stellaris).
            const int MaxSys = 192;
            var sys = new Vector4[MaxSys];
            int count = 0;
            foreach (var s in playerSystems) if (count < MaxSys) sys[count++] = new Vector4(s.Position.x, s.Position.z, 0f, 0f);
            foreach (var s in enemySystems) if (count < MaxSys) sys[count++] = new Vector4(s.Position.x, s.Position.z, Mathf.Min(2, s.OwnerId), 0f);
            foreach (var lane in _generator.Hyperlanes)
            {
                var a = _generator.Systems[lane.SystemA];
                var b = _generator.Systems[lane.SystemB];
                if (!a.HasStarbase || !b.HasStarbase || a.OwnerId < 0 || a.OwnerId != b.OwnerId) continue;
                float owner = Mathf.Min(2, a.OwnerId);
                int steps = Mathf.FloorToInt(Vector3.Distance(a.Position, b.Position) / (_claimRadius * 0.9f));
                for (int k = 1; k <= steps && count < MaxSys; k++)
                {
                    Vector3 m = Vector3.Lerp(a.Position, b.Position, k / (float)(steps + 1));
                    sys[count++] = new Vector4(m.x, m.z, owner, 0f);
                }
            }
            _borderMat.SetVectorArray("_Sys", sys);
            _borderMat.SetFloat("_SysCount", count);
            _borderMat.SetFloat("_ClaimRadius", _claimRadius);
            _borderMat.SetFloat("_Smooth", _claimRadius);
            _borderMat.SetColor("_PlayerColor", FleetIndicator.OwnColor);
            _borderMat.SetColor("_EnemyColor", FleetIndicator.OwnerColor(1));
            _borderMat.SetColor("_Enemy2Color", FleetIndicator.OwnerColor(2));

            UpdateEmpireLabel(playerSystems);
            foreach (var ai in AIEmpireManager.All)
                UpdateAIEmpireLabel(ai, byOwner.TryGetValue(ai.OwnerId, out var owned) ? owned : null);

            foreach (var kvp in _nameplates)
                if (kvp.Value != null) kvp.Value.UpdateVisuals();

            UpdateHyperlaneColors();
        }

        public void RefreshSystemBorders() => RefreshTerritoryVisuals();

        private float _claimRadius = 15f;

        /// <summary>Радиус «владения» системы: ~70% медианной длины коридора (территория доходит примерно до середины пути).</summary>
        private float ComputeClaimRadius()
        {
            if (_generator == null || _generator.Hyperlanes.Count == 0) return 15f;
            var lens = new List<float>();
            foreach (var l in _generator.Hyperlanes)
                lens.Add(Vector3.Distance(_generator.Systems[l.SystemA].Position, _generator.Systems[l.SystemB].Position));
            lens.Sort();
            return Mathf.Clamp(lens[lens.Count / 2] * 0.7f, 10f, 24f);
        }

        private void UpdateEmpireLabel(List<StarSystem> playerSystems)
{
    if (playerSystems == null || playerSystems.Count == 0) return;

    Vector3 sum = Vector3.zero;
    for (int i = 0; i < playerSystems.Count; i++)
        sum += playerSystems[i].Position;
    Vector3 centerWorld = sum / playerSystems.Count;
    centerWorld.y = -0.15f;

    // Берём имя выбранной фракции, если она есть
    string empireName = "М О Я   И М П Е Р И Я";
    var faction = UIManager.Instance?.SelectedFaction;
    if (faction != null && !string.IsNullOrEmpty(faction.Name))
    {
        // Превращаем "Республика Астрея" → "Р Е С П У Б Л И К А   А С Т Р Е Я"
        var sb = new System.Text.StringBuilder(faction.Name.Length * 2);
        foreach (char c in faction.Name.ToUpper())
        {
            sb.Append(c);
            if (c != ' ') sb.Append(' ');
        }
        empireName = sb.ToString().Trim();
    }

    if (!_empireLabels.TryGetValue(0, out var labelObj) || labelObj == null)
    {
        labelObj = new GameObject("EmpireLabel_0");
        labelObj.transform.SetParent(transform, true);

        var tm = labelObj.AddComponent<TextMesh>();
        tm.text = empireName;
        tm.fontSize = 52;
        tm.characterSize = 0.38f;
        tm.fontStyle = FontStyle.Bold;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(0.35f, 0.95f, 1.0f, 0.65f);

        var mr = labelObj.GetComponent<MeshRenderer>();
        mr.sortingOrder = 6;

        labelObj.AddComponent<EmpireLabelScaler>();
        _empireLabels[0] = labelObj;
    }
    else
    {
        var tm = labelObj.GetComponent<TextMesh>();
        if (tm != null) tm.text = empireName;
    }

    labelObj.transform.position = centerWorld;
    labelObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
}

        // ==================== ОТРИСОВКА ====================

        private void RenderGalaxy()
        {
            Shader spriteShader = GetSpriteShader();

            foreach (var system in _generator.Systems)
            {
                system.GeneratePlanets(); 
                GameObject node = new GameObject($"SystemNode_{system.Name}");
                node.transform.position = system.Position;
                node.transform.SetParent(transform, false);

                Color starCol = GetStellarisColor(system.SpectralClass);
                bool blackHole = system.SpectralClass == StarSpectralClass.BlackHole;
                float sizeMul = StarFx.SizeMul(system.SpectralClass);
                float baseScale = 2.4f * sizeMul;

                // Корона: мягкие лучи вокруг звезды, медленно вращается (под ядром)
                var coronaObj = new GameObject("StarCorona");
                coronaObj.transform.SetParent(node.transform, false);
                var coronaSr = coronaObj.AddComponent<SpriteRenderer>();
                coronaSr.sprite = blackHole ? _accretionSprite : _starCoronaSprite;
                if (spriteShader != null) coronaSr.material = new Material(spriteShader);
                coronaSr.color = blackHole ? new Color(1f, 0.62f, 0.3f, 0.9f) : new Color(starCol.r, starCol.g, starCol.b, 0.4f);
                coronaSr.sortingOrder = 14;

                // Ядро — компактная точка с неровным краем
                var coreObj = new GameObject("StarCore");
                coreObj.transform.SetParent(node.transform, false);
                coreObj.transform.localScale = Vector3.one * baseScale;
                var coreSr = coreObj.AddComponent<SpriteRenderer>();
                coreSr.sprite = _starCoreSprite;
                if (spriteShader != null) coreSr.material = new Material(spriteShader);
                coreSr.color = starCol;
                coreSr.sortingOrder = 15;
                coreObj.AddComponent<BillboardLookAt>();

                // Кольцо-вспышка (скрыто, включается в момент «моргания»)
                var ringObj = new GameObject("StarRing");
                ringObj.transform.SetParent(node.transform, false);
                ringObj.transform.localScale = Vector3.one * 2.4f;
                var ringSr = ringObj.AddComponent<SpriteRenderer>();
                ringSr.sprite = _starRingSprite;
                if (spriteShader != null) ringSr.material = new Material(spriteShader);
                ringSr.color = new Color(starCol.r, starCol.g, starCol.b, 0f);
                ringSr.sortingOrder = 16;
                ringSr.enabled = false;
                ringObj.AddComponent<BillboardLookAt>();

                // Белое «горячее» ядро поверх цветного ореола — звезда читается с любого зума
                var hotObj = new GameObject("StarHot");
                hotObj.transform.SetParent(node.transform, false);
                var hotSr = hotObj.AddComponent<SpriteRenderer>();
                hotSr.sprite = _starHotSprite;
                if (spriteShader != null) hotSr.material = new Material(spriteShader);
                hotSr.sortingOrder = 17;
                hotObj.AddComponent<BillboardLookAt>();

                // Блик с лучами поверх (у чёрной дыры — нет)
                SpriteRenderer flareSr = null;
                if (!blackHole)
                {
                    var flareObj = new GameObject("StarFlare");
                    flareObj.transform.SetParent(node.transform, false);
                    flareSr = flareObj.AddComponent<SpriteRenderer>();
                    flareSr.sprite = _starFlareSprite;
                    if (spriteShader != null) flareSr.material = new Material(spriteShader);
                    flareSr.sortingOrder = 18;
                }

                // Анимация (вращение, дыхание, корона, блик)
                var anim = coreObj.AddComponent<StarAnimator>();
                anim.Initialize(coreSr, ringSr, baseScale, blackHole ? new Color(0.08f, 0.05f, 0.12f) : starCol);
                anim.Hot = hotSr;
                anim.Corona = coronaSr;
                anim.Flare = flareSr;
                anim.SizeMul = sizeMul;
                anim.IsBlackHole = blackHole;
                if (blackHole) hotSr.enabled = false;
                _starAnims.Add(anim);

                // Плашка с именем
                var plate = node.AddComponent<SystemNameplate>();
                plate.Initialize(system);
                _nameplates[system.Id] = plate;
                // Счётчики флотов показывают сами значки флотов (FleetIndicator сливает корабли
                // одной орбиты в один значок с цифрой), отдельный значок над звездой не нужен.
                // Коллайдер и селектор
                var col = node.AddComponent<SphereCollider>();
                col.radius = 3.5f;
                node.AddComponent<StarSystemSelector>().Initialize(system);

                // Тултип
                var hoverTrigger = node.AddComponent<WorldTooltipTrigger>();
                hoverTrigger.SetNameplate(plate);

                _starNodes.Add(node);
            }

            RenderHyperlanes();
            CreateSystemMarkers();
        }

        private void RenderHyperlanes()
        {
            _hyperlanesParent = new GameObject("Hyperlanes");
            _hyperlanesParent.transform.SetParent(transform, false);

            // Шейдер спрайтов: учитывает цвет вершин и прозрачность (URP Unlit непрозрачен
            // и игнорирует градиент — коридоры получались одинаково белыми)
            Shader lineShader = GetSpriteShader();
            Material lineMat = lineShader != null ? new Material(lineShader) : null;

            foreach (var lane in _generator.Hyperlanes)
            {
                StarSystem a = _generator.Systems[lane.SystemA];
                StarSystem b = _generator.Systems[lane.SystemB];

                Vector3 startPoint = a.Position; startPoint.y = -0.05f;
                Vector3 endPoint   = b.Position; endPoint.y   = -0.05f;

                GameObject lineObj = new GameObject($"Lane_{a.Id}_{b.Id}");
                lineObj.transform.SetParent(_hyperlanesParent.transform, false);

                LineRenderer lr = lineObj.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.SetPosition(0, startPoint);
                lr.SetPosition(1, endPoint);
                lr.startWidth = 0.12f;
                lr.endWidth = 0.12f;
                lr.numCapVertices = 2;
                lr.sortingOrder = 3;
                if (lineMat != null) lr.material = new Material(lineMat);

                _hyperlaneRenderers.Add(lr);
                _hyperlaneData.Add(lane);
            }

            UpdateHyperlaneColors();
        }


        // ==================== МАЯК ====================

        private void CreateHolographicBeacon()
        {
            _beaconRoot = new GameObject("TacticalHologramBeacon");
            _beaconRoot.transform.SetParent(transform, false);

            Shader spriteShader = GetSpriteShader();
            Shader lineShader = GetLineShader();

            var outerObj = new GameObject("OuterBeaconRing");
            outerObj.transform.SetParent(_beaconRoot.transform, false);
            outerObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _beaconOuterSr = outerObj.AddComponent<SpriteRenderer>();
            _beaconOuterSr.sprite = _beaconOuterSprite;
            if (spriteShader != null) _beaconOuterSr.material = new Material(spriteShader);
            _beaconOuterSr.sortingOrder = 25;

            var innerObj = new GameObject("InnerBeaconRing");
            innerObj.transform.SetParent(_beaconRoot.transform, false);
            innerObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _beaconInnerSr = innerObj.AddComponent<SpriteRenderer>();
            _beaconInnerSr.sprite = _beaconInnerSprite;
            if (spriteShader != null) _beaconInnerSr.material = new Material(spriteShader);
            _beaconInnerSr.sortingOrder = 26;

            var beamObj = new GameObject("VerticalBeaconBeam");
            beamObj.transform.SetParent(_beaconRoot.transform, false);
            _beaconVerticalBeam = beamObj.AddComponent<LineRenderer>();
            _beaconVerticalBeam.useWorldSpace = false;
            _beaconVerticalBeam.positionCount = 2;
            _beaconVerticalBeam.SetPosition(0, new Vector3(0, -8f, 0));
            _beaconVerticalBeam.SetPosition(1, new Vector3(0, 8f, 0));
            _beaconVerticalBeam.startWidth = 0.12f;
            _beaconVerticalBeam.endWidth = 0.12f;
            _beaconVerticalBeam.sortingOrder = 24;
            if (lineShader != null) _beaconVerticalBeam.material = new Material(lineShader);

            _beaconRoot.SetActive(false);
        }

        public void SelectSystem(int systemId)
        {
            if (_selectedSystemId != -1 && _nameplates.TryGetValue(_selectedSystemId, out var prev))
                prev.SetSelected(false);

            _selectedSystemId = systemId;

            if (_selectedSystemId != -1 && _nameplates.TryGetValue(_selectedSystemId, out var next))
            {
                next.SetSelected(true);

                if (_beaconRoot != null)
                {
                    _beaconRoot.transform.position = _generator.Systems[_selectedSystemId].Position;
                    _beaconRoot.SetActive(true);

                    _currentBeaconColor = next.Data.OwnerId == 0
                        ? new Color(0.20f, 0.95f, 1f, 1f)
                        : (next.Data.OwnerId > 0 ? FleetIndicator.OwnerColor(next.Data.OwnerId) : new Color(0.40f, 0.85f, 1f, 0.95f));

                    _deployProgress = 0f;
                    _animTimer = 0f;

                    var beamGradient = new Gradient();
                    beamGradient.SetKeys(
                        new[] { new GradientColorKey(_currentBeaconColor, 0f), new GradientColorKey(_currentBeaconColor, 1f) },
                        new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.5f), new GradientAlphaKey(0f, 1f) });
                    _beaconVerticalBeam.colorGradient = beamGradient;
                }
            }
            else if (_beaconRoot != null)
            {
                _beaconRoot.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            UpdateReadability();
            UpdateBeacon();
        }

        private void UpdateBeacon()
        {
            if (_beaconRoot == null || !_beaconRoot.activeSelf || _camTransform == null) return;

            _animTimer += Time.deltaTime;
            if (_deployProgress < 1f)
                _deployProgress = Mathf.Clamp01(_deployProgress + Time.deltaTime * 3.5f);

            float dist = Vector3.Distance(_beaconRoot.transform.position, _camTransform.position);
            float baseTargetScale = Mathf.Clamp(dist * 0.052f, 2.4f, 6.5f);
            float deployCurve = Mathf.Sin(_deployProgress * Mathf.PI * 0.5f);
            float currentScale = baseTargetScale * Mathf.Lerp(0.3f, 1.0f, deployCurve);

            _beaconOuterSr.transform.Rotate(Vector3.forward, 24f * Time.deltaTime, Space.Self);
            _beaconInnerSr.transform.Rotate(Vector3.forward, -38f * Time.deltaTime, Space.Self);
            _beaconOuterSr.transform.localScale = Vector3.one * currentScale;
            _beaconInnerSr.transform.localScale = Vector3.one * currentScale;

            float pulse = Mathf.Sin(_animTimer * 4.0f) * 0.18f + 0.82f;
            var cOuter = _currentBeaconColor; cOuter.a = 0.92f * pulse;
            _beaconOuterSr.color = cOuter;
            var cInner = _currentBeaconColor; cInner.a = 0.75f * pulse;
            _beaconInnerSr.color = cInner;

            float beamWidth = Mathf.Lerp(0.06f, 0.12f, (Mathf.Sin(_animTimer * 5f) + 1f) * 0.5f);
            _beaconVerticalBeam.startWidth = beamWidth;
            _beaconVerticalBeam.endWidth = beamWidth;
        }

        // ==================== РЕЖИМЫ ====================

        public void SetMapMode(MapModeController.MapMode mode)
        {
            _currentMapMode = mode;
            RefreshTerritoryVisuals();
        }

        public void SetSystemIsolation(int activeSystemId, bool isolate)
        {
            if (_hyperlanesParent != null) _hyperlanesParent.SetActive(!isolate);
            if (_organicBorderPlane != null) _organicBorderPlane.SetActive(!isolate);

            for (int i = 0; i < _generator.Systems.Count && i < _starNodes.Count; i++)
                _starNodes[i].SetActive(!isolate || _generator.Systems[i].Id == activeSystemId);

            foreach (var kvp in _nameplates)
                if (kvp.Value != null)
                    kvp.Value.SetVisible(!isolate || kvp.Key == activeSystemId);

            foreach (var kv in _empireLabels)
                if (kv.Value != null) kv.Value.SetActive(!isolate);

            if (_markersRoot != null) _markersRoot.SetActive(!isolate);
            if (_diskPlane != null) _diskPlane.SetActive(!isolate);
            if (_dustStars != null) _dustStars.SetActive(!isolate);
            if (_nebulaRoot != null) _nebulaRoot.SetActive(!isolate);
            if (_coreRoot != null) _coreRoot.SetActive(!isolate);
            if (_miniRoot != null) _miniRoot.SetActive(!isolate);
            ApplySkyMood(isolate);
        }

        private Color GetStellarisColor(StarSpectralClass c) => c switch
        {
            StarSpectralClass.ClassM => new Color(1.00f, 0.35f, 0.25f),
            StarSpectralClass.ClassK => new Color(1.00f, 0.65f, 0.30f),
            StarSpectralClass.ClassG => new Color(1.00f, 0.92f, 0.55f),
            StarSpectralClass.ClassA => new Color(0.95f, 0.98f, 1.00f),
            StarSpectralClass.ClassB => new Color(0.45f, 0.75f, 1.00f),
            _ => Color.white
        };
    }

    // ==================== ВСПОМОГАТЕЛЬНЫЕ ====================

    public class BillboardLookAt : MonoBehaviour
    {
        private Transform _cam;
        private void Start() { if (Camera.main != null) _cam = Camera.main.transform; }
        private void LateUpdate() { if (_cam != null) transform.rotation = _cam.rotation; }
    }

    public class EmpireLabelScaler : MonoBehaviour
    {
        private Transform _cam;

        private void Start()
        {
            if (Camera.main != null) _cam = Camera.main.transform;
        }

        private TextMesh _text;
        private Color _baseColor;

        private void LateUpdate()
        {
            if (_cam == null) return;
            float dist = Vector3.Distance(_cam.position, transform.position);
            float scale = Mathf.Clamp(dist * 0.008f, 0.85f, 2.6f);
            transform.localScale = Vector3.one * scale;

            // Как в Stellaris: название империи — для обзора издалека, вблизи плавно исчезает
            if (_text == null) { _text = GetComponent<TextMesh>(); if (_text != null) _baseColor = _text.color; }
            if (_text == null) return;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(95f, 150f, dist));
            var c = _baseColor; c.a *= a;
            _text.color = c;
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = a > 0.01f;
        }
    }

    /// <summary>
    /// Аниматор звезды («живые звёзды»):
    ///   • медленно вращает неровный край и дышит размером/яркостью — плавно, без миганий
    ///   • корона из мягких лучей медленно поворачивается и «дышит»
    ///   • блик с лучами поверх: виден сильнее вблизи, едва заметно поворачивается
    ///   • размер — по классу звезды (красные карлики меньше, голубые гиганты крупнее)
    ///   • чёрная дыра: тёмное ядро и вращающийся аккреционный диск
    ///   • у каждой звезды свой ритм и фаза (не пульсируют синхронно)
    /// </summary>
    public class StarAnimator : MonoBehaviour
    {
        private SpriteRenderer _core;
        private SpriteRenderer _ring;
        private float _baseScale;
        private Color _baseColor;
        private float _phase;
        private float _speed;

        private Camera _cam;
        private const float MinSpritePx = 40f;     // цветной ореол
        private const float HotPx = 10f;            // белое ядро
        private const float HotSpriteWorld = 0.64f; // спрайт ядра 64 px при 100 px/ед.
        private const float SpriteWorldSize = 2.56f;   // спрайт 256 px при 100 px/ед.
        public SpriteRenderer Hot;
        public SpriteRenderer Corona;
        public SpriteRenderer Flare;
        public float SizeMul = 1f;
        public bool IsBlackHole;

        /// <summary>Туман войны: 1 — изученная звезда, меньше — неизведанная (тусклее, мельче, без бликов).</summary>
        public float Dim = 1f;
        private float _dimShown = 1f;
        private float _scale, _near;

        public void Initialize(SpriteRenderer core, SpriteRenderer ring, float baseScale, Color starColor)
        {
            _core = core;
            _ring = ring;
            _baseScale = baseScale;
            _baseColor = starColor;
            _phase = Random.Range(0f, 6.28f);
            _speed = Random.Range(0.35f, 0.75f);
            if (_core != null) _core.color = _baseColor;
            if (_ring != null) _ring.enabled = false;
        }

        private void Update()
        {
            if (_core == null) return;

            float dt = Time.unscaledDeltaTime;
            float t = Time.unscaledTime * _speed + _phase;
            _dimShown = Mathf.MoveTowards(_dimShown, Dim, dt * 1.5f);

            // --- ДЫХАНИЕ (медленно, мягко) ---
            float breathe = 1f + Mathf.Sin(t * 0.9f) * 0.04f;
            float alphaBreathe = 1f + Mathf.Sin(t * 1.1f) * 0.06f;

            float scale = _baseScale * breathe * Mathf.Lerp(0.65f, 1f, _dimShown);
            if (_cam == null) _cam = Camera.main;
            float dist = 100f;
            if (_cam != null)
            {
                dist = Vector3.Distance(_cam.transform.position, transform.position);
                float wpp = 2f * dist * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
                float minScale = wpp * MinSpritePx * Mathf.Sqrt(SizeMul) / SpriteWorldSize * Mathf.Lerp(0.7f, 1f, _dimShown);
                scale = Mathf.Max(scale, minScale * breathe);
            }
            _scale = scale;
            _near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, 210f, dist));
            _core.transform.localScale = Vector3.one * scale;

            // Неизведанная звезда — блёклая, ближе к серому
            Color c = IsBlackHole ? _baseColor : Color.Lerp(new Color(0.62f, 0.66f, 0.74f), _baseColor, Mathf.Lerp(0.35f, 1f, _dimShown));
            c.a = Mathf.Clamp01(alphaBreathe) * Mathf.Lerp(0.45f, 1f, _dimShown);
            _core.color = c;

            // Белое ядро: постоянный экранный размер, лёгкий оттенок класса звезды
            if (Hot != null && Hot.enabled && _cam != null)
            {
                float w = 2f * dist * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
                float hs = Mathf.Max(_baseScale * 0.35f, w * HotPx * Mathf.Sqrt(SizeMul) / HotSpriteWorld) * Mathf.Lerp(0.8f, 1f, _dimShown);
                Hot.transform.localScale = Vector3.one * hs;
                var hc = Color.Lerp(Color.white, _baseColor, 0.22f);
                hc.a = Mathf.Lerp(0.7f, 1f, _dimShown);
                Hot.color = hc;
            }

            // Корона: больше ядра, медленно дышит (у чёрной дыры — аккреционный диск)
            if (Corona != null)
            {
                float cb = 1f + Mathf.Sin(t * 0.6f + 1.3f) * 0.07f;
                Corona.transform.localScale = Vector3.one * scale * (IsBlackHole ? 1.5f : 1.35f) * cb;
                var cc = IsBlackHole ? new Color(1f, 0.62f, 0.3f) : _baseColor;
                cc.a = (IsBlackHole ? 0.85f : 0.20f + 0.22f * _near) * Mathf.Lerp(0.35f, 1f, _dimShown) * (0.9f + 0.1f * cb);
                Corona.color = cc;
            }

            // Блик: заметнее вблизи, у неизведанных почти нет
            if (Flare != null)
            {
                float fb = 1f + Mathf.Sin(t * 0.7f + 2.1f) * 0.06f;
                Flare.transform.localScale = Vector3.one * scale * 1.25f * fb;
                var fc = Color.Lerp(Color.white, _baseColor, 0.45f);
                fc.a = (0.18f + 0.42f * _near) * _dimShown * _dimShown;
                Flare.color = fc;
            }
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            var rot = _cam.transform.rotation;
            float t = Time.unscaledTime;
            // повороты в плоскости экрана: корона и блик в разные стороны, медленно
            if (Corona != null)
                Corona.transform.rotation = rot * Quaternion.Euler(0f, 0f, IsBlackHole ? -t * 28f + _phase * 57f : t * 4f * _speed + _phase * 57f);
            if (Flare != null)
                Flare.transform.rotation = rot * Quaternion.Euler(0f, 0f, -t * 1.5f * _speed + _phase * 20f);
        }
    }

    /// <summary>Процедурные спрайты «живых звёзд»: корона, блик с лучами, аккреционный диск; размеры по классам.</summary>
    public static class StarFx
    {
        public static float SizeMul(StarSpectralClass c) => c switch
        {
            StarSpectralClass.ClassM => 0.80f,
            StarSpectralClass.ClassK => 0.90f,
            StarSpectralClass.ClassG => 1.00f,
            StarSpectralClass.ClassA => 1.12f,
            StarSpectralClass.ClassB => 1.32f,
            _ => 1.1f
        };

        private static Sprite Make(int res, System.Func<float, float, float> alpha, string name)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = name };
            var px = new Color32[res * res];
            float c = (res - 1) * 0.5f;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float a = Mathf.Clamp01(alpha(dx, dy));
                px[y * res + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
        }

        /// <summary>Блик: 4 длинных тонких луча + 4 коротких диагональных + маленькое свечение.</summary>
        public static Sprite FlareSprite(int res) => Make(res, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > 1f) return 0f;
            float fade = 1f - Mathf.SmoothStep(0.75f, 1f, r);
            float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
            float spikeH = Mathf.Exp(-ay * ay * 9000f) * Mathf.Pow(1f - ax, 3f);
            float spikeV = Mathf.Exp(-ax * ax * 9000f) * Mathf.Pow(1f - ay, 3f);
            float u = (x + y) * 0.7071f, v = (x - y) * 0.7071f;
            float au = Mathf.Abs(u), av = Mathf.Abs(v);
            float diag = (Mathf.Exp(-av * av * 14000f) * Mathf.Pow(Mathf.Max(0f, 1f - au * 1.8f), 3f)
                        + Mathf.Exp(-au * au * 14000f) * Mathf.Pow(Mathf.Max(0f, 1f - av * 1.8f), 3f)) * 0.45f;
            float glow = Mathf.Exp(-r * r * 60f) * 0.6f;
            return (spikeH + spikeV + diag + glow) * fade;
        }, "StarFlare");

        /// <summary>Корона: мягкие неровные лучи разной длины вокруг звезды.</summary>
        public static Sprite CoronaSprite(int res) => Make(res, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > 1f) return 0f;
            float ang = Mathf.Atan2(y, x);
            // бесшовный шум по углу: выборка по окружности
            float n = Mathf.PerlinNoise(Mathf.Cos(ang) * 3f + 10f, Mathf.Sin(ang) * 3f + 10f) * 0.6f
                    + Mathf.PerlinNoise(Mathf.Cos(ang) * 9f + 30f, Mathf.Sin(ang) * 9f + 30f) * 0.4f;
            float len = 0.35f + n * 0.55f;
            float rays = Mathf.Exp(-r / Mathf.Max(0.05f, len) * 3.2f);
            float inner = Mathf.Exp(-r * r * 18f) * 0.5f;
            float fade = 1f - Mathf.SmoothStep(0.8f, 1f, r);
            return (rays * (0.55f + 0.45f * n) + inner) * fade;
        }, "StarCorona");

        /// <summary>Аккреционный диск чёрной дыры: яркое кольцо с завихрениями и тёмный центр.</summary>
        public static Sprite AccretionSprite(int res) => Make(res, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > 1f) return 0f;
            float ang = Mathf.Atan2(y, x);
            float swirl = Mathf.PerlinNoise(Mathf.Cos(ang + r * 6f) * 4f + 5f, Mathf.Sin(ang + r * 6f) * 4f + 5f);
            float ring = Mathf.Exp(-Mathf.Pow((r - 0.36f) / 0.07f, 2f)) * (0.6f + 0.6f * swirl);
            float halo = Mathf.Exp(-Mathf.Pow((r - 0.36f) / 0.25f, 2f)) * 0.35f * swirl;
            float photon = Mathf.Exp(-Mathf.Pow((r - 0.27f) / 0.015f, 2f)) * 0.9f;
            float fade = 1f - Mathf.SmoothStep(0.8f, 1f, r);
            return (ring + halo + photon) * fade;
        }, "Accretion");
    }
}