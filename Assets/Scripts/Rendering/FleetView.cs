using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;
using StellarisClone.Generation;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    public class FleetView : MonoBehaviour
    {
        public FleetData Data { get; private set; }
        private GalaxyGenerator _generator;
        private LineRenderer _pathLine;
        private TrailRenderer _engineTrail;
        private MeshRenderer[] _renderers;
        private bool _isSelected;

        private LineRenderer _laserBeam;
        private Light _workLight;
        private float _animTime;

        private TextMesh _statusBadge;
        private Transform _camTransform;

        private WorldTooltipTrigger _worldTooltip;
        private GameObject _typeIcon;

        // ==================== НАСТРОЙКИ 3D-МОДЕЛЕЙ ====================
        // Пути в Resources БЕЗ слова "Resources" и БЕЗ ".prefab"
        private const string PrefabPathScience     = "Prefabs/Ships/ScienceShip";
        private const string PrefabPathConstructor = "Prefabs/Ships/ConstructorShip";
        private const string PrefabPathMilitary    = "Prefabs/Ships/MilitaryShip";

        // Если модель в префабе смотрит не туда — крути тут (в градусах)
        private const float ModelYawOffset   = 0f;
        private const float ModelYOffset     = 0f;

        // Масштаб модели относительно стандартного размера (1 = как есть)
        private const float ModelScaleMultiplier = 1f;

        // Подкрашивать материалы модели в цвет фракции (текстуры сохраняются)
        private const bool TintPrefabMaterials = true;

        // false — процедурные модели ShipMeshFactory (корабли узнаются по силуэту);
        // true — префабы из Resources/Prefabs/Ships, если они есть
        private const bool UsePrefabModels = false;
        private ShipMeshFactory.ShipVisual _visual;

        private bool _usesCustomModel;
        // =============================================================

        // ==================== ПОЛЁТ ====================
        // Профиль перелёта: разгон → инерция → торможение (трапеция скорости, доли пути по времени)
        private const float AccelShare = 0.22f;
        private const float TurnRateDeg = 95f;           // как быстро корабль разворачивается на курс
        private const float MaxBankDeg = 28f;            // крен в развороте
        private const float CruiseThrottle = 0.12f;      // подруливание на инерционном участке

        private readonly List<Transform> _plumes = new List<Transform>();
        private readonly List<Transform> _retroPlumes = new List<Transform>();
        private Material _plumeMat, _retroMat;
        private Light _engineLight;
        private float _throttle, _retro, _bank, _nozzleRadius = 0.12f, _retroRadius = 0.06f;
        private Vector3 _orbitSpot;
        private bool _gliding;
        private static Mesh s_plumeQuad;
        private static Shader s_plumeShader;
        private static readonly int IdThrottle = Shader.PropertyToID("_Throttle");

        public void Initialize(FleetData data, GalaxyGenerator generator)
        {
            Data = data;
            _generator = generator;
            if (Camera.main != null) _camTransform = Camera.main.transform;

            CreateShipMesh();
            CreatePathRenderer();
            CreateEngineTrail();
            CreateStatusBadge();
            CreateTypeIcon();
            CreateWorkVFX();
            SnapToCurrentSystem();
            SetupTooltip();

            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed += HandleDayPassed;
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed -= HandleDayPassed;
            if (_typeIcon != null) Destroy(_typeIcon);
            if (_plumeMat != null) Destroy(_plumeMat);
            if (_retroMat != null) Destroy(_retroMat);
        }

        private static Shader FindLitShader() => ShaderCache.Lit;
        private static Shader FindUnlitShader() => ShaderCache.Unlit;

        private Color GetBaseColorForType() => Data.Type switch
        {
            FleetType.Constructor => new Color(1f, 0.6f, 0.1f),
            FleetType.Science     => new Color(0.2f, 0.95f, 0.5f),
            _                     => new Color(0.2f, 0.85f, 0.95f)
        };

        private void CreateShipMesh()
        {
            Shader litShader = FindLitShader();
            if (litShader == null) return;

            Color shipColor = GetBaseColorForType();

            GameObject prefab = UsePrefabModels ? LoadShipPrefab(Data.Type) : null;
            if (prefab != null)
            {
                _usesCustomModel = true;
                InstantiateShipPrefab(prefab, shipColor);
            }
            else
            {
                _usesCustomModel = false;
                _visual = ShipMeshFactory.Build(transform, Data.Type, Data.HullClass, FleetIndicator.OwnerColor(Data.OwnerId));
            }

            // Собираем рендереры ПОСЛЕ создания/инстанса модели
            _renderers = GetComponentsInChildren<MeshRenderer>();

            // Коллайдер на корне — по нему FleetManager ловит клики
            var col = gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(3.5f, 2f, 3.5f);
        }

        private static GameObject LoadShipPrefab(FleetType type)
        {
            string path = type switch
            {
                FleetType.Science     => PrefabPathScience,
                FleetType.Constructor => PrefabPathConstructor,
                _                     => PrefabPathMilitary
            };
            return Resources.Load<GameObject>(path);
        }

        private void InstantiateShipPrefab(GameObject prefab, Color shipColor)
        {
            var model = Instantiate(prefab, transform);
            model.name = "ShipModel";
            model.transform.localPosition = new Vector3(0, ModelYOffset, 0);
            model.transform.localRotation = Quaternion.Euler(0, ModelYawOffset, 0);
            model.transform.localScale = Vector3.one * ModelScaleMultiplier;

            // Сносим дочерние коллайдеры — иначе они перехватывают raycast'ы
            foreach (var c in model.GetComponentsInChildren<Collider>())
                Destroy(c);

            if (!TintPrefabMaterials) return;

            // Подкрашиваем материалы модели в цвет фракции.
            // Базовые текстуры/карты остаются, а цвет (BaseColor) слегка миксуется.
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null) continue;

                    if (mat.HasProperty("_BaseColor"))
                    {
                        Color c = mat.GetColor("_BaseColor");
                        mat.SetColor("_BaseColor", Color.Lerp(c, shipColor, 0.35f));
                    }
                    if (mat.HasProperty("_Color"))
                        mat.color = Color.Lerp(mat.color, shipColor, 0.35f);

                    mat.EnableKeyword("_EMISSION");
                    if (mat.HasProperty("_EmissionColor"))
                        mat.SetColor("_EmissionColor", shipColor * 0.4f);
                }
                r.materials = mats;
            }
        }

        private Color EngineColor() => Data.Type switch
        {
            FleetType.Constructor => new Color(1f, 0.62f, 0.25f),
            FleetType.Science     => new Color(0.35f, 1f, 0.7f),
            _                     => new Color(0.45f, 0.78f, 1f)
        };

        /// <summary>
        /// Тонкий ионный след за кораблём (виден только на ходу) и факелы у каждого сопла:
        /// маршевые — назад, тормозные — вперёд. Плюс отсвет двигателей на корпусе.
        /// </summary>
        private void CreateEngineTrail()
        {
            Color glowColor = EngineColor();
            Vector3[] nozzles = _visual != null && _visual.Nozzles.Length > 0 ? _visual.Nozzles : new[] { new Vector3(0, 0, -1.2f) };
            Vector3 rear = Vector3.zero;
            foreach (var n in nozzles) rear += n;
            rear /= nozzles.Length;

            GameObject trailObj = new GameObject("EngineTrail");
            trailObj.transform.SetParent(transform, false);
            trailObj.transform.localPosition = rear;

            _engineTrail = trailObj.AddComponent<TrailRenderer>();
            _engineTrail.time = 1.4f;
            _engineTrail.minVertexDistance = 0.08f;
            _engineTrail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.15f, 0.7f), new Keyframe(1f, 0f));
            _engineTrail.widthMultiplier = Data.Type == FleetType.Military ? 0.32f : 0.24f;
            _engineTrail.numCapVertices = 2;
            _engineTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _engineTrail.receiveShadows = false;

            // Sprites/Default учитывает цвет и прозрачность вершин — след тает к хвосту
            var shader = ShaderCache.Sprite ?? FindUnlitShader();
            if (shader != null) _engineTrail.material = new Material(shader);

            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.Lerp(glowColor, Color.white, 0.6f), 0f), new GradientColorKey(glowColor, 0.25f), new GradientColorKey(glowColor * 0.6f, 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0.22f, 0.3f), new GradientAlphaKey(0f, 1f) }
            );
            _engineTrail.colorGradient = g;
            _engineTrail.emitting = false;

            CreatePlumes(nozzles, glowColor);
        }

        private void CreatePlumes(Vector3[] nozzles, Color color)
        {
            if (s_plumeShader == null) s_plumeShader = Resources.Load<Shader>("Shaders/EnginePlume");
            if (s_plumeShader == null || !s_plumeShader.isSupported) return;
            if (s_plumeQuad == null)
            {
                s_plumeQuad = new Mesh { name = "PlumeQuad" };
                s_plumeQuad.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 0, -1f), new Vector3(0.5f, 0, -1f) };
                s_plumeQuad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                // Квад разворачивается к камере в шейдере — границы с запасом, чтобы его не отсекало
                s_plumeQuad.bounds = new Bounds(new Vector3(0, 0, -0.5f), new Vector3(2f, 2f, 2f));
            }

            _nozzleRadius = _visual != null ? _visual.NozzleRadius : 0.12f;
            _retroRadius = _visual != null ? _visual.RetroRadius : 0.06f;

            _plumeMat = new Material(s_plumeShader) { name = "EnginePlume" };
            _plumeMat.SetColor("_Color", color);
            _plumeMat.SetFloat("_Seed", Random.Range(0f, 50f));
            _retroMat = new Material(s_plumeShader) { name = "RetroPlume" };
            _retroMat.SetColor("_Color", Color.Lerp(color, Color.white, 0.3f));
            _retroMat.SetFloat("_Seed", Random.Range(0f, 50f));
            _retroMat.SetFloat("_Intensity", 1.6f);

            foreach (var n in nozzles) _plumes.Add(Plume("Plume", n, Quaternion.identity, _plumeMat));
            if (_visual != null)
                foreach (var n in _visual.RetroNozzles) _retroPlumes.Add(Plume("RetroPlume", n, Quaternion.Euler(0f, 180f, 0f), _retroMat));

            var lightGo = new GameObject("EngineLight");
            lightGo.transform.SetParent(transform, false);
            Vector3 rear = Vector3.zero;
            foreach (var n in nozzles) rear += n;
            lightGo.transform.localPosition = rear / nozzles.Length + new Vector3(0, 0, -0.6f);
            _engineLight = lightGo.AddComponent<Light>();
            _engineLight.type = LightType.Point;
            _engineLight.color = color;
            _engineLight.range = 3.5f;
            _engineLight.intensity = 0f;
            _engineLight.shadows = LightShadows.None;
            _engineLight.enabled = false;

            ApplyThrottle(0f, 0f);
        }

        private Transform Plume(string name, Vector3 at, Quaternion rot, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = s_plumeQuad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>Тяга маршевых и тормозных двигателей → длина и яркость факелов, отсвет на корпусе.</summary>
        private void ApplyThrottle(float main, float retro)
        {
            float flick = 1f + 0.05f * Mathf.Sin(_animTime * 41f) + 0.04f * Mathf.Sin(_animTime * 17.3f);
            SetPlumes(_plumes, _plumeMat, main, _nozzleRadius, 0.25f, 1.5f, flick);
            SetPlumes(_retroPlumes, _retroMat, retro, _retroRadius, 0.15f, 0.7f, flick);
            if (_engineLight != null)
            {
                float li = Mathf.Max(main, retro * 0.5f);
                _engineLight.enabled = li > 0.02f;
                _engineLight.intensity = li * 2.2f * flick;
            }
        }

        /// <summary>minLen / maxLen — длина факела (в единицах карты) на холостом и на полной тяге.</summary>
        private static void SetPlumes(List<Transform> plumes, Material mat, float throttle, float radius, float minLen, float maxLen, float flick)
        {
            bool on = throttle > 0.01f;
            if (mat != null) mat.SetFloat(IdThrottle, throttle);
            foreach (var p in plumes)
            {
                if (p == null) continue;
                if (p.gameObject.activeSelf != on) p.gameObject.SetActive(on);
                if (!on) continue;
                float len = Mathf.Lerp(minLen, maxLen, throttle) * flick;
                float width = radius * Mathf.Lerp(2.2f, 3.2f, throttle);
                p.localScale = new Vector3(width, 1f, len);
            }
        }

        private void CreateStatusBadge()
        {
            GameObject badgeObj = new GameObject("FleetStatusBadge");
            badgeObj.transform.SetParent(transform, false);
            badgeObj.transform.localPosition = new Vector3(0, 4.55f, 0);

            _statusBadge = badgeObj.AddComponent<TextMesh>();
            _statusBadge.fontSize = 22;
            _statusBadge.characterSize = 0.12f;
            _statusBadge.alignment = TextAlignment.Center;
            _statusBadge.anchor = TextAnchor.MiddleCenter;
            _statusBadge.color = Color.white;
            _statusBadge.text = "";
        }

        private void CreateTypeIcon()
        {
            _typeIcon = new GameObject($"FleetIcon_{Data.Name}");
            Transform host = transform.parent != null ? transform.parent : transform;
            _typeIcon.transform.SetParent(host, false);
            _typeIcon.AddComponent<FleetIndicator>().Init(this);
        }

        private void CreatePathRenderer()
        {
            GameObject lineObj = new GameObject("FleetPathLine");
            lineObj.transform.SetParent(transform, false);

            _pathLine = lineObj.AddComponent<LineRenderer>();
            _pathLine.startWidth = 0.28f;
            _pathLine.endWidth = 0.15f;
            _pathLine.positionCount = 0;
            _pathLine.useWorldSpace = true;

            var shader = FindUnlitShader();
            if (shader != null)
            {
                Material lineMat = new Material(shader);
                lineMat.color = new Color(0.2f, 0.85f, 1f, 0.9f);
                _pathLine.material = lineMat;
            }
            _pathLine.enabled = false;
        }

        private void CreateWorkVFX()
        {
            GameObject beamObj = new GameObject("ActionBeam");
            beamObj.transform.SetParent(transform, false);

            _laserBeam = beamObj.AddComponent<LineRenderer>();
            _laserBeam.startWidth = 0.12f;
            _laserBeam.endWidth = 0.45f;
            _laserBeam.positionCount = 2;
            _laserBeam.useWorldSpace = true;

            var shader = FindUnlitShader();
            if (shader != null)
            {
                Material beamMat = new Material(shader);
                beamMat.color = Color.cyan;
                _laserBeam.material = beamMat;
            }
            _laserBeam.enabled = false;

            GameObject lightObj = new GameObject("WorkLight");
            lightObj.transform.SetParent(transform, false);
            _workLight = lightObj.AddComponent<Light>();
            _workLight.type = LightType.Point;
            _workLight.range = 14f;
            _workLight.intensity = 0f;
        }

        private void SetupTooltip()
        {
            _worldTooltip = gameObject.GetComponent<WorldTooltipTrigger>();
            if (_worldTooltip == null)
                _worldTooltip = gameObject.AddComponent<WorldTooltipTrigger>();

            _worldTooltip.text = Data.Type switch
            {
                FleetType.Science =>
                    $"<b>{Data.Name}</b>\n" +
                    $"<color=#66FF88>Научный корабль</color>\n\n" +
                    $"ЛКМ — выбрать\n" +
                    $"ПКМ по системе — исследовать\n\n" +
                    $"<i>Состояние: {Data.State}</i>",

                FleetType.Constructor =>
                    $"<b>{Data.Name}</b>\n" +
                    $"<color=#FFAA44>Строительный корабль</color>\n\n" +
                    $"ЛКМ — выбрать\n" +
                    $"ПКМ по изученной системе — построить аванпост\n\n" +
                    $"<i>Состояние: {Data.State}</i>",

                _ =>
                    $"<b>{Data.Name}</b>\n" +
                    $"<color=#66E0FF>Боевой корабль</color> · ⚔ {Data.MilitaryPower}\n" +
                    $"Корпус {Data.HullPoints:0}/{Data.MaxHullPoints:0}  Броня {Data.ArmorPoints:0}  Щиты {Data.ShieldPoints:0}\n\n" +
                    $"ЛКМ — выбрать\n" +
                    $"ПКМ по системе — двигаться\n\n" +
                    $"<i>Состояние: {Data.State}</i>"
            };
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;

            Color baseCol = GetBaseColorForType();
            Color selectedCol = new Color(1f, 0.95f, 0.3f);

            if (_visual != null)
            {
                _visual.SetSelected(selected);
            }
            else if (_usesCustomModel)
            {
                // Для 3D-модели меняем только свечение — базовый цвет и текстуры сохраняются.
                Color glow = selected ? selectedCol : baseCol;
                foreach (var r in _renderers)
                {
                    if (r == null || r.material == null) continue;
                    r.material.EnableKeyword("_EMISSION");
                    if (r.material.HasProperty("_EmissionColor"))
                        r.material.SetColor("_EmissionColor", glow * 0.7f);
                }
            }
            else
            {
                Color finalCol = selected ? selectedCol : baseCol;
                foreach (var r in _renderers)
                {
                    if (r == null || r.material == null) continue;
                    if (r.material.HasProperty("_Color")) r.material.color = finalCol;
                    if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", finalCol);
                    if (r.material.HasProperty("_EmissionColor"))
                        r.material.SetColor("_EmissionColor", finalCol * 0.7f);
                }
            }

            // Маршрут рисует FleetRouteOverlay (пунктир, стрелки, дни); старая сплошная линия не нужна
            _pathLine.enabled = false;
        }

        /// <summary>Место на орбите системы; instant — сразу туда, иначе корабль доходит сам.</summary>
        private void SnapToCurrentSystem(bool instant = true)
        {
            if (_generator == null || _generator.Systems.Count == 0) return;
            Vector3 pos = _generator.Systems[Data.CurrentSystemId].Position;
            float offsetX = Data.Type == FleetType.Constructor ? -2.8f : (Data.Type == FleetType.Science ? 0f : 2.8f);
            float offsetZ = Data.Type == FleetType.Science ? -3.0f : 2.0f;
            _orbitSpot = pos + new Vector3(offsetX, 0.5f, offsetZ);
            _gliding = !instant;
            if (instant) transform.position = _orbitSpot;
        }

        private void HandleDayPassed(int day, int month, int year)
        {
            if (Data.InCombat) return;
            if (Data.Destroyed) return;
            if (Data.State == FleetState.Orbiting && Data.Path.Count > 0)
                StartNextJump();

            if (Data.State == FleetState.InHyperlane)
            {
                Data.DaysRemainingInTransit -= JumpRate();
                if (Data.DaysRemainingInTransit <= 0f) ArriveAtTargetSystem();
            }

            if (Data.State == FleetState.Constructing)
            {
                Data.DaysRemainingConstruction -= 1f;
                if (Data.DaysRemainingConstruction <= 0f) CompleteConstruction();
            }

            if (Data.State == FleetState.Surveying)
            {
                Data.DaysRemainingSurvey -= EmpireBonuses.For(Data.OwnerId).SurveySpeed * LeaderManager.SurveySpeedMult(Data);
                LeaderManager.Instance?.OnSurveyDay(Data);
                if (Data.DaysRemainingSurvey <= 0f) CompleteSurvey();
            }

            UpdatePathVisuals();
        }

        private void StartNextJump()
        {
            if (Data.Path.Count == 0) return;
            Data.TargetSystemId = Data.Path.Dequeue();
            Data.State = FleetState.InHyperlane;
            SFXManager.PlayAt(Sfx.FtlJump, transform.position, Data.OwnerId == 0 ? 1f : 0.6f);
            Data.DaysRemainingInTransit = Data.TotalDaysForTransit;

            _gliding = false;
        }

        private void ArriveAtTargetSystem()
        {
            Data.CurrentSystemId = Data.TargetSystemId;
            Data.TargetSystemId = -1;
            Data.State = FleetState.Orbiting;
            if (Data.Path.Count > 0)
            {
                StartNextJump();
            }
            else
            {
                SnapToCurrentSystem(instant: false);   // доходит до своего места на орбите на маневровых

                if (Data.Type == FleetType.Constructor && Data.BuildTargetSystemId == Data.CurrentSystemId)
                {
                    Data.State = FleetState.Constructing;
                    Data.DaysRemainingConstruction = Data.TotalConstructionDays;
                }

                if (Data.Type == FleetType.Science && Data.SurveyTargetSystemId == Data.CurrentSystemId)
                {
                    Data.State = FleetState.Surveying;
                    Data.DaysRemainingSurvey = Data.TotalSurveyDays;
                }

                if (Data.State == FleetState.Orbiting)
                {
                    bool more = Data.OrderQueue.Count > 0;
                    Data.MarkEvent(FleetData.FleetEvent.Arrived);
                    if (more) FleetManager.Instance?.AdvanceQueue(this);
                    else if (Data.HasPlayerOrder && Data.OwnerId == 0)
                        Data.HasPlayerOrder = false;   // прибытие видно по значку флота — без уведомления
                }
            }
        }

        private void CompleteConstruction()
        {
            Data.State = FleetState.Orbiting;
            int sysId = Data.BuildTargetSystemId;
            Data.BuildTargetSystemId = -1;

            bool claimed = FleetManager.Instance != null && sysId >= 0 && FleetManager.Instance.ClaimSystem(sysId, Data.OwnerId);
            if (claimed && Data.OwnerId == 0) SFXManager.Play(Sfx.OutpostBuilt);
            if (FleetManager.Instance != null && sysId >= 0 && !claimed && Data.OwnerId == 0)
            {
                // Систему успели занять — половина сплавов возвращается
                var eco = EconomyManager.Instance;
                if (eco != null) { eco.Alloys += FleetManager.StarbaseAlloysCost * 0.5f; eco.RaiseResourcesChanged(); }
                NotificationCenter.Show("Форпост не построен", "Систему уже заняли. Возвращено 50% сплавов", NotificationCenter.Kind.Warning, 5f);
            }

            _laserBeam.enabled = false;
            _workLight.intensity = 0f;
            Data.MarkEvent(FleetData.FleetEvent.Built);
            FleetManager.Instance?.AdvanceQueue(this);
        }

        private void CompleteSurvey()
        {
            Data.State = FleetState.Orbiting;
            int sysId = Data.SurveyTargetSystemId;
            Data.SurveyTargetSystemId = -1;

            if (FleetManager.Instance != null && sysId >= 0)
            {
                LeaderManager.Instance?.OnSurveyComplete(Data);
                FleetManager.Instance.CompleteSystemSurvey(sysId, Data.OwnerId, LeaderManager.SurveyRewardMult(Data.Id), Data);
            }

            _laserBeam.enabled = false;
            _workLight.intensity = 0f;
            Data.MarkEvent(FleetData.FleetEvent.Surveyed);
            FleetManager.Instance?.AdvanceQueue(this);
        }

        // Туман войны: чужой флот вне видимости игрока целиком уходит на невидимый слой, его огни гаснут
        private bool _fogHidden;
        private Dictionary<GameObject, int> _fogLayers;
        private Dictionary<Light, int> _fogLights;

        private void ApplyFogVisibility()
        {
            bool hide = Data != null && Data.OwnerId != 0 && !Vision.CanSeeFleet(0, Data);
            if (hide == _fogHidden) return;
            _fogHidden = hide;
            if (hide)
            {
                _fogLayers = new Dictionary<GameObject, int>();
                foreach (var t in GetComponentsInChildren<Transform>(true))
                {
                    _fogLayers[t.gameObject] = t.gameObject.layer;
                    t.gameObject.layer = Vision.HiddenLayer;
                }
                _fogLights = new Dictionary<Light, int>();
                foreach (var l in GetComponentsInChildren<Light>(true))
                {
                    _fogLights[l] = l.cullingMask;
                    l.cullingMask = 0;
                }
            }
            else
            {
                if (_fogLayers != null)
                    foreach (var kv in _fogLayers) if (kv.Key != null) kv.Key.layer = kv.Value;
                if (_fogLights != null)
                    foreach (var kv in _fogLights) if (kv.Key != null) kv.Key.cullingMask = kv.Value;
                _fogLayers = null;
                _fogLights = null;
            }
        }

        private void Update()
        {
            ApplyFogVisibility();
            _animTime += Time.deltaTime;
            _visual?.Tick(_animTime);


            if (_camTransform != null && _statusBadge != null)
            {
                _statusBadge.transform.rotation = _camTransform.rotation;
                float dist = Vector3.Distance(transform.position, _camTransform.position);
                _statusBadge.transform.localScale = Vector3.one * Mathf.Clamp(dist / 40f, 0.8f, 3.5f);
                // Издалека состояние показывает значок флота — текст только мешал бы
                bool near = dist < 70f;
                if (_statusBadge.gameObject.activeSelf != near) _statusBadge.gameObject.SetActive(near);
            }

            if (Data.State == FleetState.InHyperlane && Data.TargetSystemId != -1)
            {
                _laserBeam.enabled = false;
                _workLight.intensity = 0f;
                Fly();
                string targetSysName = _generator.Systems[Data.TargetSystemId].Name;
                _statusBadge.text = $"<color=#FE3>Прыжок ➔ {targetSysName}</color>\n<color=#FFF>{Mathf.Max(0, (int)Data.DaysRemainingInTransit)} дн.</color>";
            }
            else if (Data.State == FleetState.Surveying)
            {
                EnginesIdle();
                transform.Rotate(Vector3.up, 24f * Time.deltaTime, Space.World);

                Vector3 sysCenter = _generator.Systems[Data.CurrentSystemId].Position;
                Vector3 scanPoint = sysCenter + new Vector3(Mathf.Cos(_animTime * 2.5f) * 4f, 0, Mathf.Sin(_animTime * 2.5f) * 4f);

                _laserBeam.enabled = true;
                _laserBeam.startColor = new Color(0.2f, 1f, 0.5f, 0.8f);
                _laserBeam.endColor = new Color(0.2f, 1f, 0.5f, 0.05f);
                _laserBeam.SetPosition(0, transform.position);
                _laserBeam.SetPosition(1, scanPoint);

                _workLight.color = new Color(0.2f, 1f, 0.5f);
                _workLight.intensity = 1.8f + Mathf.PingPong(_animTime * 4f, 1.2f);

                _statusBadge.text = $"<color=#3FE>Разведка системы</color>\n<color=#FFF>{(int)Data.DaysRemainingSurvey} дн.</color>";
            }
            else if (Data.State == FleetState.Constructing)
            {
                EnginesIdle();
                transform.Rotate(Vector3.up, 15f * Time.deltaTime, Space.World);

                Vector3 sysCenter = _generator.Systems[Data.CurrentSystemId].Position;
                Vector3 sparkOffset = Random.insideUnitSphere * 0.8f;

                _laserBeam.enabled = true;
                _laserBeam.startColor = new Color(1f, 0.6f, 0.1f, 0.9f);
                _laserBeam.endColor = new Color(1f, 0.9f, 0.3f, 0.6f);
                _laserBeam.SetPosition(0, transform.position);
                _laserBeam.SetPosition(1, sysCenter + sparkOffset);

                _workLight.color = new Color(1f, 0.7f, 0.2f);
                _workLight.intensity = Random.Range(1.0f, 3.5f);

                _statusBadge.text = $"<color=#FE4>Монтаж аванпоста</color>\n<color=#FFF>{(int)Data.DaysRemainingConstruction} дн.</color>";
            }
            else if (Data.InCombat)
            {
                EnginesIdle();
                _laserBeam.enabled = false;
                _workLight.intensity = 0f;
                _statusBadge.text =
                    $"<color=#FF5555>БОЙ</color>\n" +
                    $"<color=#FFF>Корпус {Data.HullPoints:0} · Щиты {Data.ShieldPoints:0} · Броня {Data.ArmorPoints:0}</color>";
            }
            else
            {
                _engineTrail.emitting = false;
                _laserBeam.enabled = false;
                _workLight.intensity = 0f;
                Hold();

                if (_isSelected)
                    _statusBadge.text = $"<color=#00FFFF>{Data.Name}</color>";
                else
                    _statusBadge.text = "";
            }
        }

        // ==================== ПОЛЁТ ====================

        /// <summary>Скорость перелёта в днях пути за игровой день (как в HandleDayPassed).</summary>
        private float JumpRate()
        {
            float rate = EmpireBonuses.For(Data.OwnerId).HyperlaneSpeed;
            rate *= Mathf.Max(0.5f, Data.HyperSpeed);
            rate *= LeaderManager.HyperSpeedMult(Data);
            return rate;
        }

        /// <summary>Пройденная доля пути по профилю «разгон — инерция — торможение» (t — доля времени).</summary>
        private static float TravelDistance(float t)
        {
            const float a = AccelShare;
            float vmax = 1f / (1f - a);
            if (t < a) return 0.5f * vmax * t * t / a;
            if (t > 1f - a) return 1f - 0.5f * vmax * (1f - t) * (1f - t) / a;
            return 0.5f * vmax * a + vmax * (t - a);
        }

        /// <summary>
        /// Перелёт по гиперкоридору: корабль разворачивается на курс с креном, разгоняется на маршевых,
        /// идёт по инерции, затем гасит скорость тормозными двигателями. Позиция плавная между дневными тиками.
        /// </summary>
        private void Fly()
        {
            Vector3 origin = _generator.Systems[Data.CurrentSystemId].Position;
            Vector3 destination = _generator.Systems[Data.TargetSystemId].Position;
            float total = Mathf.Max(0.01f, Data.TotalDaysForTransit);

            float frac = 0f;
            var tm = TimeManager.Instance;
            if (tm != null && !Data.InCombat) frac = tm.DayFraction * JumpRate();   // на паузе доля дня замирает
            float t = Mathf.Clamp01((total - Data.DaysRemainingInTransit + frac) / total);

            Vector3 targetPos = Vector3.Lerp(origin, destination, TravelDistance(t));
            targetPos.y += 0.5f;
            transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-10f * Time.deltaTime));

            // Разворот на курс: нос — по коридору, крен — в сторону поворота
            Vector3 dir = destination - origin;
            dir.y = 0f;
            float alignment = 1f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion heading = Quaternion.LookRotation(dir.normalized, Vector3.up);
                Vector3 fwdBefore = Flat(transform.forward);
                Quaternion level = Quaternion.LookRotation(fwdBefore.sqrMagnitude > 0.0001f ? fwdBefore : dir.normalized, Vector3.up);
                Quaternion next = Quaternion.RotateTowards(level, heading, TurnRateDeg * Time.deltaTime);

                float yawRate = Time.deltaTime > 0f ? Vector3.SignedAngle(level * Vector3.forward, next * Vector3.forward, Vector3.up) / Time.deltaTime : 0f;
                float targetBank = Mathf.Clamp(-yawRate * 0.35f, -MaxBankDeg, MaxBankDeg);
                _bank = Mathf.Lerp(_bank, targetBank, 1f - Mathf.Exp(-4f * Time.deltaTime));
                transform.rotation = next * Quaternion.Euler(0f, 0f, _bank);

                alignment = Mathf.Clamp01(1f - Quaternion.Angle(next, heading) / 25f);
            }

            // Тяга по фазе перелёта; маршевые включаются, только когда нос уже на курсе
            float main, retro;
            if (t < AccelShare) { main = alignment; retro = 0f; }
            else if (t > 1f - AccelShare) { main = 0f; retro = 1f; }
            else { main = CruiseThrottle; retro = 0f; }
            if (tm != null && tm.CurrentSpeed == 0) { main *= 0.35f; retro *= 0.35f; }   // пауза — двигатели на холостом

            _throttle = Mathf.MoveTowards(_throttle, main, 2.5f * Time.deltaTime);
            _retro = Mathf.MoveTowards(_retro, retro, 2.5f * Time.deltaTime);
            ApplyThrottle(_throttle, _retro);
            _engineTrail.emitting = true;
        }

        /// <summary>На орбите: дойти до своего места на маневровых, выровнять крен, слегка «дышать».</summary>
        private void Hold()
        {
            float main = 0f;
            if (_gliding)
            {
                Vector3 to = _orbitSpot - transform.position;
                float dist = to.magnitude;
                if (dist < 0.02f) _gliding = false;
                else
                {
                    transform.position = Vector3.MoveTowards(transform.position, _orbitSpot, Mathf.Max(0.6f, dist * 1.6f) * Time.deltaTime);
                    Vector3 flat = Flat(to);
                    if (flat.sqrMagnitude > 0.01f && dist > 0.6f)
                    {
                        Quaternion want = Quaternion.LookRotation(flat.normalized, Vector3.up);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, TurnRateDeg * 0.6f * Time.deltaTime);
                    }
                    main = Mathf.Clamp01(dist * 0.25f) * 0.3f;
                }
            }
            else
            {
                transform.position += new Vector3(0, Mathf.Sin(_animTime * 1.3f) * 0.0015f, 0);
            }

            // Выравниваем крен и тангаж — корабль стоит ровно
            Vector3 fwd = Flat(transform.forward);
            if (fwd.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(fwd.normalized, Vector3.up), 1f - Mathf.Exp(-2.5f * Time.deltaTime));
            _bank = Mathf.Lerp(_bank, 0f, 1f - Mathf.Exp(-3f * Time.deltaTime));

            _throttle = Mathf.MoveTowards(_throttle, main, 2f * Time.deltaTime);
            _retro = Mathf.MoveTowards(_retro, 0f, 2f * Time.deltaTime);
            ApplyThrottle(_throttle, _retro);
            _engineTrail.emitting = _throttle > 0.05f;
        }

        private void EnginesIdle()
        {
            _gliding = false;
            _throttle = Mathf.MoveTowards(_throttle, 0f, 2f * Time.deltaTime);
            _retro = Mathf.MoveTowards(_retro, 0f, 2f * Time.deltaTime);
            ApplyThrottle(_throttle, _retro);
            _engineTrail.emitting = false;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public void UpdatePathVisuals()
        {
            if (!_isSelected || _pathLine == null) return;

            var points = new List<Vector3> { transform.position };
            if (Data.TargetSystemId != -1)
                points.Add(_generator.Systems[Data.TargetSystemId].Position + new Vector3(0, 0.5f, 0));
            foreach (var systemId in Data.Path)
                points.Add(_generator.Systems[systemId].Position + new Vector3(0, 0.5f, 0));

            _pathLine.positionCount = points.Count;
            _pathLine.SetPositions(points.ToArray());
        }
    }
}