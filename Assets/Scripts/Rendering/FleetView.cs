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

        private float _currentRollAngle;
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

        private bool _usesCustomModel;
        // =============================================================

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

            GameObject prefab = LoadShipPrefab(Data.Type);
            if (prefab != null)
            {
                _usesCustomModel = true;
                InstantiateShipPrefab(prefab, shipColor);
            }
            else
            {
                _usesCustomModel = false;
                CreateProceduralShipMesh(litShader, shipColor);
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

        private void CreateProceduralShipMesh(Shader litShader, Color shipColor)
        {
            if (Data.Type == FleetType.Constructor)
            {
                var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hull.transform.SetParent(transform, false);
                hull.transform.localScale = new Vector3(1.5f, 0.6f, 2.2f);
                CreatePod(new Vector3(-1.1f, 0f, 0f));
                CreatePod(new Vector3( 1.1f, 0f, 0f));
            }
            else if (Data.Type == FleetType.Science)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(transform, false);
                sphere.transform.localScale = new Vector3(1.2f, 1.2f, 1.5f);

                var dish = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                dish.transform.SetParent(transform, false);
                dish.transform.localPosition = new Vector3(0f, 0.6f, -0.4f);
                dish.transform.localScale = new Vector3(0.9f, 0.1f, 0.9f);
                dish.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            }
            else
            {
                var hull = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                hull.transform.SetParent(transform, false);
                hull.transform.localScale = new Vector3(0.8f, 1.0f, 0.8f);
                hull.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

                var bridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bridge.transform.SetParent(transform, false);
                bridge.transform.localPosition = new Vector3(0f, 0.35f, -0.4f);
                bridge.transform.localScale = new Vector3(0.5f, 0.3f, 0.7f);
            }

            // Убираем коллайдеры у примитивов (на корне свой BoxCollider)
            foreach (var c in GetComponentsInChildren<Collider>())
                if (c.gameObject != gameObject) Destroy(c);

            _renderers = GetComponentsInChildren<MeshRenderer>();
            Material mat = new Material(litShader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", shipColor);
            if (mat.HasProperty("_Color")) mat.color = shipColor;
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", shipColor * 0.6f);

            foreach (var r in _renderers) r.material = mat;
        }

        private void CreatePod(Vector3 localPos)
        {
            var pod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pod.transform.SetParent(transform, false);
            pod.transform.localPosition = localPos;
            pod.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            pod.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private void CreateEngineTrail()
        {
            GameObject trailObj = new GameObject("EngineTrail");
            trailObj.transform.SetParent(transform, false);
            trailObj.transform.localPosition = new Vector3(0, 0, -1.2f);

            _engineTrail = trailObj.AddComponent<TrailRenderer>();
            _engineTrail.time = 0.8f;
            _engineTrail.startWidth = 0.7f;
            _engineTrail.endWidth = 0.05f;

            Color glowColor = Data.Type switch
            {
                FleetType.Constructor => new Color(1f, 0.55f, 0.1f, 0.8f),
                FleetType.Science     => new Color(0.2f, 1f, 0.6f, 0.8f),
                _                     => new Color(0.2f, 0.85f, 1f, 0.8f)
            };

            var shader = FindUnlitShader();
            if (shader != null)
            {
                Material trailMat = new Material(shader);
                trailMat.color = glowColor;
                _engineTrail.material = trailMat;
            }

            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(glowColor, 0f), new GradientColorKey(Color.white, 0.3f), new GradientColorKey(glowColor, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.4f, 0.6f), new GradientAlphaKey(0f, 1f) }
            );
            _engineTrail.colorGradient = g;
            _engineTrail.emitting = false;
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

            if (_usesCustomModel)
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

        private void SnapToCurrentSystem()
        {
            if (_generator == null || _generator.Systems.Count == 0) return;
            Vector3 pos = _generator.Systems[Data.CurrentSystemId].Position;
            float offsetX = Data.Type == FleetType.Constructor ? -2.8f : (Data.Type == FleetType.Science ? 0f : 2.8f);
            float offsetZ = Data.Type == FleetType.Science ? -3.0f : 2.0f;
            transform.position = pos + new Vector3(offsetX, 0.5f, offsetZ);
        }

        private void HandleDayPassed(int day, int month, int year)
        {
            if (Data.InCombat) return;
            if (Data.Destroyed) return;
            if (Data.State == FleetState.Orbiting && Data.Path.Count > 0)
                StartNextJump();

            if (Data.State == FleetState.InHyperlane)
            {
                float speedBonus = EmpireBonuses.For(Data.OwnerId).HyperlaneSpeed;
                speedBonus *= Mathf.Max(0.5f, Data.HyperSpeed);
                speedBonus *= LeaderManager.HyperSpeedMult(Data);
                Data.DaysRemainingInTransit -= 1f * speedBonus;
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

            _engineTrail.emitting = true;
            transform.localScale = new Vector3(0.7f, 0.7f, 1.5f);
        }

        private void ArriveAtTargetSystem()
        {
            Data.CurrentSystemId = Data.TargetSystemId;
            Data.TargetSystemId = -1;
            Data.State = FleetState.Orbiting;
            _engineTrail.emitting = false;
            transform.localScale = Vector3.one;

            if (Data.Path.Count > 0)
            {
                StartNextJump();
            }
            else
            {
                SnapToCurrentSystem();

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

        private void Update()
        {
            _animTime += Time.deltaTime;

            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, 6f * Time.deltaTime);

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
                _engineTrail.emitting = true;

                Vector3 origin = _generator.Systems[Data.CurrentSystemId].Position;
                Vector3 destination = _generator.Systems[Data.TargetSystemId].Position;

                float linearProgress = Mathf.Clamp01(1f - Data.DaysRemainingInTransit / Data.TotalDaysForTransit);
                float smoothProgress = Mathf.SmoothStep(0f, 1f, linearProgress);

                Vector3 targetPos = Vector3.Lerp(origin, destination, smoothProgress);
                targetPos.y += 0.5f;

                transform.position = Vector3.Lerp(transform.position, targetPos, 12f * Time.deltaTime);

                Vector3 travelDir = (destination - origin).normalized;
                if (travelDir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetLook = Quaternion.LookRotation(travelDir);
                    float targetRoll = Mathf.Sin(_animTime * 3f) * 12f;
                    _currentRollAngle = Mathf.Lerp(_currentRollAngle, targetRoll, 5f * Time.deltaTime);
                    Quaternion rollRot = Quaternion.Euler(0, 0, _currentRollAngle);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetLook * rollRot, 10f * Time.deltaTime);
                }

                string targetSysName = _generator.Systems[Data.TargetSystemId].Name;
                _statusBadge.text = $"<color=#FE3>Прыжок ➔ {targetSysName}</color>\n<color=#FFF>{Mathf.Max(0, (int)Data.DaysRemainingInTransit)} дн.</color>";
            }
            else if (Data.State == FleetState.Surveying)
            {
                _engineTrail.emitting = false;
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
                _engineTrail.emitting = false;
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
                _engineTrail.emitting = false;
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
                transform.position += new Vector3(0, Mathf.Sin(_animTime * 2f) * 0.003f, 0);

                if (_isSelected)
                    _statusBadge.text = $"<color=#00FFFF>{Data.Name}</color>";
                else
                    _statusBadge.text = "";
            }
        }

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