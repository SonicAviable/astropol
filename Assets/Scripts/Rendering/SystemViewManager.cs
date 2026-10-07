using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using StellarisClone.Core;
using StellarisClone.Cam;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    public class SystemViewManager : MonoBehaviour
    {
        public static SystemViewManager Instance { get; private set; }
        public static event Action<PlanetData, StarSystem> OnPlanetSelected;
        public static event Action<bool> OnViewModeChanged;

        public bool IsInSystemView { get; private set; }
        public StarSystem CurrentSystem { get; private set; }

        private GameObject _systemContainer;
        private Camera _mainCamera;
        private StrategyCameraController _cameraController;

        private Vector3 _savedCamPos;
        private Quaternion _savedCamRot;
        private bool _isTransitioningToGalaxy;

        // Масштаб процедурной планеты относительно planet.Size (подбирается)
        // Ассет обычно создаёт сферу диаметром 1 unit — при Size 1.1-3.0 надо уменьшать
        private const float ProceduralPlanetScale = 0.5f;

        [System.Serializable]
        public class PlanetInstance
        {
            public PlanetData Data;
            public Transform Pivot;
            public Transform PlanetTransform;
            public GameObject SelectionIndicator;
            public MeshRenderer Renderer;
            public int VisualSeed;
            public PlanetVisualType VisualType;
            public bool IsProcedural;
        }

        private readonly List<PlanetInstance> _activePlanets = new List<PlanetInstance>();
        private PlanetInstance _currentSelectedPlanet;

        private static Shader GetLitShader() => ShaderCache.Lit;
        private static Shader GetLineShader() => ShaderCache.Unlit;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                var cams = FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
                if (cams.Length > 0) _mainCamera = cams[0];
            }
            if (_mainCamera != null)
                _cameraController = _mainCamera.GetComponent<StrategyCameraController>();
        }

        private void Start()
        {
            StarSystemSelector.OnSystemEntered += EnterSystemView;
        }

        private void OnDestroy()
        {
            StarSystemSelector.OnSystemEntered -= EnterSystemView;
        }

        public void EnterSystemView(StarSystem system)
        {
            if (IsInSystemView || _isTransitioningToGalaxy) return;
            if (_mainCamera == null) return;

            CurrentSystem = system;
            system.GeneratePlanets();

            IsInSystemView = true;
            GalaxyView.Instance?.SetSystemIsolation(system.Id, true);

            _savedCamPos = _mainCamera.transform.position;
            _savedCamRot = _mainCamera.transform.rotation;

            _cameraController?.EnterSystemMode(system.Position);

            BuildSystemContent(system);
            SFXManager.Play(Sfx.SystemEnter);
            OnViewModeChanged?.Invoke(true);
        }

        public void ExitToGalaxyView()
        {
            if (!IsInSystemView || _isTransitioningToGalaxy) return;

            IsInSystemView = false;
            _isTransitioningToGalaxy = true;

            _cameraController?.ReturnToGalaxyView(_savedCamPos, _savedCamRot);
            SFXManager.Play(Sfx.SystemExit);

            OnViewModeChanged?.Invoke(false);

            if (_cameraController == null)
                CompleteReturnToGalaxy();
        }

        private void CompleteReturnToGalaxy()
        {
            _isTransitioningToGalaxy = false;
            ClearSystemContent();
            GalaxyView.Instance?.SetSystemIsolation(-1, false);
        }

        // ==================== ПОСТРОЕНИЕ СОДЕРЖИМОГО СИСТЕМЫ ====================

        private void BuildSystemContent(StarSystem system)
        {
            ClearSystemContent();

            _systemContainer = new GameObject($"SystemView_{system.Name}");
            _systemContainer.transform.position = system.Position;

            // Свет от центральной звезды
            var starLightObj = new GameObject("StarSystemLight");
            starLightObj.transform.SetParent(_systemContainer.transform, false);
            var starLight = starLightObj.AddComponent<Light>();
            starLight.type = LightType.Point;
            starLight.range = 250f;
            starLight.intensity = 2.5f;

            Shader litShader = GetLitShader();

            for (int i = 0; i < system.Planets.Count; i++)
            {
                var planet = system.Planets[i];

                // Pivot для орбиты
                GameObject pivotObj = new GameObject($"Pivot_{planet.Name}");
                pivotObj.transform.SetParent(_systemContainer.transform, false);
                CreateOrbitRing(pivotObj.transform, planet.OrbitRadius);

                // Создаём планету: префаб ассета или fallback-сфера
                var pInstance = CreatePlanetVisual(planet, system, i, pivotObj.transform, litShader);

                // Клик-нотификатор
                var clicker = pInstance.PlanetTransform.gameObject.AddComponent<PlanetClickNotifier>();
                clicker.Data = planet;
                clicker.ParentSystem = system;
                clicker.InstanceRef = pInstance;

                _activePlanets.Add(pInstance);

                // Если на планете уже стоит станция — восстановить
                if (planet.HasMiningStation)
                    SpawnStationVisual(pInstance.PlanetTransform, planet);
            }
        }

        /// <summary>
        /// Создаёт визуальный объект планеты: пробует загрузить префаб из
        /// Resources/Planets/, если не находит — строит fallback-сферу.
        /// </summary>
        private PlanetInstance CreatePlanetVisual(PlanetData planet, StarSystem system, int index,
                                                  Transform pivot, Shader litShader)
        {
            int seed = system.Id * 100 + index;
            PlanetVisualType visualType = (PlanetVisualType)(index % Enum.GetValues(typeof(PlanetVisualType)).Length);

            GameObject prefab = LoadPlanetPrefabFor(planet.Type, system.Id, index);
            GameObject planetObj;
            bool isProcedural = false;
            MeshRenderer rend = null;

            if (prefab != null)
            {
                // === Процедурная планета из ассета ===
                planetObj = Instantiate(prefab, pivot);
                planetObj.name = planet.Name;
                planetObj.transform.localPosition = new Vector3(planet.OrbitRadius, 0, 0);
                planetObj.transform.localScale = Vector3.one * planet.Size * ProceduralPlanetScale;

                // Убираем коллайдеры префаба, чтобы клик ловил наш SphereCollider
                foreach (var c in planetObj.GetComponentsInChildren<Collider>())
                    Destroy(c);

                isProcedural = true;
                rend = planetObj.GetComponentInChildren<MeshRenderer>();
            }
            else
            {
                // === Fallback — сфера с процедурной текстурой ===
                planetObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                planetObj.name = planet.Name;
                planetObj.transform.SetParent(pivot, false);
                planetObj.transform.localPosition = new Vector3(planet.OrbitRadius, 0, 0);
                planetObj.transform.localScale = Vector3.one * planet.Size;

                // Убираем встроенный коллайдер примитива — свой поставим ниже
                var builtInCol = planetObj.GetComponent<Collider>();
                if (builtInCol != null) Destroy(builtInCol);

                Texture2D planetTex = PlanetTextureFactory.GeneratePlanetTexture(visualType, seed);

                rend = planetObj.GetComponent<MeshRenderer>();
                if (litShader != null && rend != null)
                {
                    Material mat = new Material(litShader);
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", planetTex);
                    if (mat.HasProperty("_MainTex")) mat.mainTexture = planetTex;
                    if (mat.HasProperty("_Smoothness"))
                        mat.SetFloat("_Smoothness",
                            (visualType == PlanetVisualType.Ocean || visualType == PlanetVisualType.Ice) ? 0.65f : 0.15f);

                    if (visualType == PlanetVisualType.Molten)
                    {
                        mat.EnableKeyword("_EMISSION");
                        if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", planetTex);
                        if (mat.HasProperty("_EmissionColor"))
                            mat.SetColor("_EmissionColor", new Color(0.9f, 0.3f, 0.05f) * 0.7f);
                    }
                    rend.material = mat;
                }
            }

            // Общая логика для обоих вариантов
            planetObj.AddComponent<PlanetRotator>();

            // Прицел выделения
            GameObject selObj = new GameObject("PlanetSelectionReticle");
            selObj.transform.SetParent(planetObj.transform, false);
            selObj.transform.localPosition = Vector3.zero;
            selObj.transform.localScale = Vector3.one * 1.8f;

            var selSr = selObj.AddComponent<SpriteRenderer>();
            selSr.sprite = GalaxyView.Instance != null ? GalaxyView.Instance.TacticalReticleSprite : null;
            var spriteShader = ShaderCache.Sprite;
            if (spriteShader != null) selSr.material = new Material(spriteShader);
            var selCol = UIManager.DS.NeonCyan;
            selSr.color = new Color(selCol.r, selCol.g, selCol.b, 0.95f);
            selObj.AddComponent<BillboardLookAt>();
            selObj.SetActive(false);

            // Свой коллайдер на корне планеты для кликов
            var clickCol = planetObj.AddComponent<SphereCollider>();
            clickCol.radius = 1f;
            clickCol.isTrigger = false;

            return new PlanetInstance
            {
                Data = planet,
                Pivot = pivot,
                PlanetTransform = planetObj.transform,
                SelectionIndicator = selObj,
                Renderer = rend,
                VisualSeed = seed,
                VisualType = visualType,
                IsProcedural = isProcedural
            };
        }

        /// <summary>
        /// Загружает префаб планеты из Resources/Planets/ по типу.
        /// Возвращает null, если ни один файл не найден — тогда вызывающий код
        /// использует fallback.
        /// </summary>
        private static GameObject LoadPlanetPrefabFor(PlanetType type, int systemId, int planetIndex)
        {
            // Список вариантов для каждого типа. Можно добавить больше файлов —
            // код сам выберет один по seed-у.
            string[] candidates = type switch
            {
                PlanetType.Continental => new[] { "planet_continental", "planet_continental_2" },
                PlanetType.Desert      => new[] { "planet_desert", "planet_desert_2" },
                PlanetType.Molten      => new[] { "planet_molten", "planet_barren" },
                PlanetType.GasGiant    => new[] { "planet_gas", "planet_gas_2" },
                PlanetType.Barren      => new[] { "planet_barren", "planet_moon" },
                _                      => new[] { "planet_barren" }
            };

            // Стабильный seed — одна и та же планета в системе всегда одна
            int stableSeed = systemId * 31 + planetIndex * 7;
            var rng = new System.Random(stableSeed);

            // Пробуем случайный вариант из списка
            int firstPick = rng.Next(0, candidates.Length);
            for (int attempt = 0; attempt < candidates.Length; attempt++)
            {
                int idx = (firstPick + attempt) % candidates.Length;
                string path = "Planets/" + candidates[idx];
                var prefab = Resources.Load<GameObject>(path);
                if (prefab != null) return prefab;
            }

            return null;
        }

        // ==================== СТАНЦИИ И ВЫБОР ====================

        public PlanetInstance GetPlanetInstance(PlanetData planet)
        {
            if (planet == null) return null;
            return _activePlanets.Find(p => p.Data == planet);
        }

        public void SelectPlanet(PlanetInstance instance)
        {
            if (_currentSelectedPlanet != null && _currentSelectedPlanet.SelectionIndicator != null)
                _currentSelectedPlanet.SelectionIndicator.SetActive(false);

            _currentSelectedPlanet = instance;

            if (_currentSelectedPlanet != null && _currentSelectedPlanet.SelectionIndicator != null)
                _currentSelectedPlanet.SelectionIndicator.SetActive(true);
        }

        public void SpawnStationOnActivePlanet(PlanetData planet)
        {
            if (planet == null) return;
            var targetInstance = _activePlanets.Find(p => p.Data == planet);
            if (targetInstance == null) return;

            SpawnStationVisual(targetInstance.PlanetTransform, planet);
        }

        private void SpawnStationVisual(Transform parentTransform, PlanetData planet)
        {
            if (parentTransform.Find($"Station_{planet.Name}") != null) return;

            GameObject stationObj = new GameObject($"Station_{planet.Name}");
            stationObj.transform.SetParent(parentTransform, false);
            stationObj.transform.localPosition = new Vector3(planet.Size * 1.35f, 0.3f, 0f);
            stationObj.transform.localScale = Vector3.one * 0.45f;

            var hub = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hub.transform.SetParent(stationObj.transform, false);

            CreateSolarPanel(stationObj.transform, new Vector3( 1.2f, 0f, 0f));
            CreateSolarPanel(stationObj.transform, new Vector3(-1.2f, 0f, 0f));

            Shader litShader = GetLitShader();
            if (litShader != null)
            {
                Material stationMat = new Material(litShader);
                Color stationCol = UIManager.DS.NeonTeal;
                if (stationMat.HasProperty("_Color")) stationMat.color = stationCol;
                if (stationMat.HasProperty("_BaseColor")) stationMat.SetColor("_BaseColor", stationCol);
                stationMat.EnableKeyword("_EMISSION");
                if (stationMat.HasProperty("_EmissionColor"))
                {
                    var nc = UIManager.DS.NeonCyan;
                    stationMat.SetColor("_EmissionColor", new Color(nc.r, nc.g, nc.b, 1f) * 1.5f);
                }

                foreach (var r in stationObj.GetComponentsInChildren<MeshRenderer>())
                    r.material = stationMat;
            }
        }

        private void CreateSolarPanel(Transform parent, Vector3 localPos)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = localPos;
            panel.transform.localScale = new Vector3(1.2f, 0.08f, 0.6f);
        }

        // ==================== ОРБИТЫ / ОЧИСТКА / ОБНОВЛЕНИЕ ====================

        /// <summary>
        /// Орбита — тонкая едва заметная линия; за планетой тянется светлый «след», который гаснет по ходу
        /// орбиты (кольцо — ребёнок пивота и вращается вместе с планетой, поэтому след всегда позади неё).
        /// </summary>
        private void CreateOrbitRing(Transform parent, float radius)
        {
            GameObject ring = new GameObject("OrbitRing");
            ring.transform.SetParent(parent, false);

            const int N = 160;
            var lr = ring.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.startWidth = 0.07f;
            lr.endWidth = 0.07f;
            lr.numCapVertices = 0;
            lr.positionCount = N;
            // Планета стоит в (r, 0, 0) и движется в сторону убывания угла — след лежит при угле > 0
            for (int i = 0; i < N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            var c = new Color(0.78f, 0.86f, 1f);
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[]
                {
                    new GradientAlphaKey(0.42f, 0f),
                    new GradientAlphaKey(0.16f, 0.06f),
                    new GradientAlphaKey(0.06f, 0.22f),
                    new GradientAlphaKey(0.035f, 0.5f),
                    new GradientAlphaKey(0.035f, 1f)
                });
            lr.colorGradient = g;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            // Sprites/Default учитывает цвет и прозрачность вершин (URP Unlit — нет)
            var shader = ShaderCache.Sprite ?? Shader.Find("Sprites/Default");
            if (shader != null) lr.material = new Material(shader);
        }

        private void ClearSystemContent()
        {
            _activePlanets.Clear();
            _currentSelectedPlanet = null;
            if (_systemContainer != null) Destroy(_systemContainer);
            _systemContainer = null;
        }

        private void Update()
        {
            if (_mainCamera == null) return;

            if (_isTransitioningToGalaxy)
            {
                if (_cameraController == null || !_cameraController.IsReturningFromSystem)
                    CompleteReturnToGalaxy();
                return;
            }

            if (IsInSystemView)
            {
                for (int i = 0; i < _activePlanets.Count; i++)
                {
                    var p = _activePlanets[i];
                    if (p?.Pivot != null && p.Data != null)
                        p.Pivot.Rotate(Vector3.up, p.Data.OrbitSpeed * Time.deltaTime, Space.Self);
                }

                if (_currentSelectedPlanet != null && _currentSelectedPlanet.SelectionIndicator != null)
                    _currentSelectedPlanet.SelectionIndicator.transform.Rotate(Vector3.forward, 25f * Time.deltaTime);
            }
        }

        public class PlanetClickNotifier : MonoBehaviour
        {
            public PlanetData Data;
            public StarSystem ParentSystem;
            public PlanetInstance InstanceRef;

            private void OnMouseDown()
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return;

                if (Data != null)
                {
                    SFXManager.Play(Sfx.SystemSelect, 0.9f, 1.26f);
                    Instance.SelectPlanet(InstanceRef);
                    UIManager.Instance?.ShowPlanetInspector(Data, ParentSystem);
                    OnPlanetSelected?.Invoke(Data, ParentSystem);
                }
            }
        }
    }
}