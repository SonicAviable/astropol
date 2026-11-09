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
        private Renderer[] _renderers;
        private bool _isSelected;

        private LineRenderer _laserBeam;
        private Light _workLight;
        private float _animTime;

        // Плашка над кораблём: капсула со значком класса, именем и «+N» выделенных в строю
        private Transform _tagRoot;
        private FleetPill _tag;
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
        // Строй на орбите (FleetFormation): базовая точка типа флота, место в группе и общий курс группы
        private Vector3 _anchor, _formHeading;
        private int _formIndex = -1, _formSystem = -1;
        // Бой: скорость манёвра, собственные часы «танца» и флаг, чтобы после боя вернуться на орбиту
        private Vector3 _combatVel;
        private float _combatClock, _workClock;
        private bool _wasInCombat;
        private ShipWorkFx _work;
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
            if (Data.Type != FleetType.Military)
            {
                _work = ShipWorkFx.Attach(transform);
                _work.SystemRing = Data.OwnerId != 0;   // у ваших кораблей кольцо разведки — у подписи системы
            }
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
                _visual = ShipMeshFactory.Build(transform, Data.Type, Data.HullClass, FleetIndicator.OwnerColor(Data.OwnerId), Data.OwnerId);
            }

            // Собираем рендереры ПОСЛЕ создания/инстанса модели
            _renderers = GetComponentsInChildren<Renderer>();   // и скелетные модели (левиафан)

            // Коллайдер на корне — по нему FleetManager ловит клики (при пересборке модели — тот же)
            var col = GetComponent<BoxCollider>();
            if (col == null) col = gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(3.5f, 2f, 3.5f);
        }

        /// <summary>
        /// Пересобрать модель корабля и двигатели. Стартовые флоты появляются раньше, чем игрок выбирает фракцию
        /// и цвет империи, — после выбора их корпуса и окраска приводятся к ней (UIManager.ApplyPlayerIdentity).
        /// </summary>
        public void RebuildModel()
        {
            if (Data == null) return;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                string n = c.name;
                if (n != "ShipModel" && n != "Plume" && n != "RetroPlume" && n != "EngineLight" && n != "EngineTrail") continue;
                c.SetParent(null, false);   // сразу из иерархии: новые рендереры собираются без старых
                Destroy(c.gameObject);
            }
            _plumes.Clear();
            _retroPlumes.Clear();
            if (_plumeMat != null) Destroy(_plumeMat);
            if (_retroMat != null) Destroy(_retroMat);
            _plumeMat = _retroMat = null;
            _engineLight = null;
            _visual = null;
            _damageAnchors = null;
            CreateShipMesh();
            CreateEngineTrail();
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

            // У живых существ (левиафан) двигателей нет: без следа, факелов и отсвета сопел
            if (_visual != null && _visual.Organic)
            {
                _engineTrail.enabled = false;
                return;
            }

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
            GameObject badgeObj = new GameObject("FleetTag");
            badgeObj.transform.SetParent(transform, false);
            badgeObj.transform.localPosition = new Vector3(0, 4.55f, 0);
            _tagRoot = badgeObj.transform;

            Color oc = Data.OwnerId == 0 ? UIManager.DS.NeonCyan : FleetIndicator.OwnerColor(Data.OwnerId);
            _tag = FleetPill.Create(_tagRoot, oc, Data.Type == FleetType.Science ? LGIcon.ScienceShip
                       : Data.Type == FleetType.Constructor ? LGIcon.ConstructorShip
                       : ModuleVisuals.HullIcon(Data.HullClass), 60);
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
            // Подсказка живая у всех кораблей: состояние, цель и прогресс меняются
            _worldTooltip.Dynamic = BuildTooltip;
        }

        private string TypeName() => Data.Type switch
        {
            FleetType.Science => Loc.T("Научный корабль"),
            FleetType.Constructor => Loc.T("Строительный корабль"),
            _ => Loc.T("Боевой корабль")
        };

        private string TypeHex() => Data.Type switch
        {
            FleetType.Science => "#66FF88",
            FleetType.Constructor => "#FFAA44",
            _ => "#66E0FF"
        };

        private string TypeIcon() => Data.Type switch
        {
            FleetType.Science => "ScienceShip",
            FleetType.Constructor => "ConstructorShip",
            _ => "Ship"
        };

        private string StateName() => Data.State switch
        {
            FleetState.InHyperlane => Loc.T("В пути"),
            FleetState.Constructing => Loc.T("Строит"),
            FleetState.Surveying => Loc.T("Исследует"),
            _ => Loc.T("На орбите")
        };

        private static string SysName(int id)
        {
            var s = EmpireStats.GetSystem(id);
            return s != null ? s.Name : "—";
        }

        /// <summary>Цель текущего действия и его прогресс 0..1 (-1 — действия нет).</summary>
        private float Progress(out string target)
        {
            var d = Data;
            switch (d.State)
            {
                case FleetState.InHyperlane:
                    target = SysName(d.TargetSystemId);
                    return 1f - d.DaysRemainingInTransit / Mathf.Max(0.01f, d.TotalDaysForTransit);
                case FleetState.Constructing:
                    target = SysName(d.BuildTargetSystemId);
                    return 1f - d.DaysRemainingConstruction / Mathf.Max(0.01f, d.TotalConstructionDays);
                case FleetState.Surveying:
                    target = SysName(d.SurveyTargetSystemId);
                    return 1f - d.DaysRemainingSurvey / Mathf.Max(0.01f, d.TotalSurveyDays);
            }
            target = null;
            return -1f;
        }

        private string RmbHint() => Data.Type switch
        {
            FleetType.Science => Loc.T("исследовать систему"),
            FleetType.Constructor => Loc.T("построить аванпост"),
            _ => Loc.T("двигаться")
        };

        private string BuildTooltip()
        {
            var d = Data;
            if (d == null) return "";
            var sb = new System.Text.StringBuilder(256);
            string hex = TypeHex();
            bool mine = d.OwnerId == 0;
            bool mil = d.Type == FleetType.Military;
            float p = Progress(out string target);
            string troops = mil ? StellarisClone.Cards.ArmyManager.TroopSummary(d) : null;

            // Карточка: шапка с иконкой и бейджем состояния, строки «ключ — значение»
            sb.Append($"[[hdr|{hex}|{TypeName()}|{StateName()}|-]]\n[[{TypeIcon()}:{hex}]]<b>{d.Name}</b>\n");
            sb.Append($"[[kv|Star|#8AA2A8|{Loc.T("Местоположение")}|{SysName(d.CurrentSystemId)}]]\n");
            if (target != null)
                sb.Append($"[[kv|Target|{hex}|{Loc.T("Цель")}|{target} · {Mathf.RoundToInt(p * 100f)}%]]\n");
            if (mil)
            {
                sb.Append($"[[kv|Swords|#FF7A6B|{Loc.T("Мощь")}|{d.MilitaryPower}]]\n");
                sb.Append($"[[kv|ArmorPlate|#C8D2D6|{Loc.T("Корпус / броня")}|{d.HullPoints:0}/{d.MaxHullPoints:0} · {d.ArmorPoints:0}]]\n");
                sb.Append($"[[kv|ShieldDome|#66E0FF|{Loc.T("Щиты")}|{d.ShieldPoints:0}]]\n");
            }
            if (troops != null) sb.Append(troops).Append('\n');
            if (mine)
                sb.Append($"[[ticks]]\n<color=#8AA2A8>{Loc.T("ЛКМ — выбрать")}</color>\n<color=#8AA2A8>{Loc.T("ПКМ")} — {RmbHint()}</color>");
            return sb.ToString().TrimEnd('\n');
        }

        public bool IsSelected => _isSelected;

        public void SetSelected(bool selected)
        {
            _isSelected = selected;

            Color baseCol = GetBaseColorForType();
            Color selectedCol = new Color(1f, 0.95f, 0.3f);

            if (_visual != null)
            {
                // Готовые и процедурные корпуса не перекрашиваются: выбор показывает голографический прицел под кораблём
                SetSelectionMarker(selected);
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
            _anchor = pos + new Vector3(offsetX, 0.5f, offsetZ);
            // Группа смотрит к звезде с доворотом по орбите — клин тянется наружу, от звезды и соседних групп
            Vector3 inward = new Vector3(-offsetX, 0f, -offsetZ).normalized;
            _formHeading = Quaternion.Euler(0f, 30f, 0f) * inward;
            _formIndex = Mathf.Max(0, FleetFormation.Get(this).Index);
            _formSystem = Data.CurrentSystemId;
            _orbitSpot = FormationSpot(_formIndex);
            _gliding = !instant;
            if (instant) transform.position = _orbitSpot;
        }

        private Vector3 FormationSpot(int index)
        {
            float spacing = Data.Type == FleetType.Military ? 4.0f : 2.6f;
            Vector3 off = FleetFormation.LocalOffset(index, spacing);
            return _anchor + Quaternion.LookRotation(_formHeading, Vector3.up) * off;
        }

        /// <summary>Флоты прибыли или ушли — корабль переходит на новое место в строю своей группы.</summary>
        private void UpdateFormation()
        {
            if (_formSystem != Data.CurrentSystemId) { SnapToCurrentSystem(instant: false); return; }
            int index = FleetFormation.Get(this).Index;
            if (index == _formIndex) return;
            _formIndex = index;
            _orbitSpot = FormationSpot(index);
            _gliding = true;
        }

        private void HandleDayPassed(int day, int month, int year)
        {
            if (Data.InCombat) return;
            if (Data.Destroyed) return;
            // Перехват: из системы с вооружённым врагом военный корабль не уходит, пока не выйдет из боя
            if (Data.State == FleetState.Orbiting && Data.Path.Count > 0 && !Pinned())
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
            // Граница закрылась по пути (истёк договор, заключён мир) — флот останавливается перед ней
            var here = EmpireStats.GetSystem(Data.CurrentSystemId);
            var next = EmpireStats.GetSystem(Data.Path.Peek());
            if (!BorderRules.CanStep(Data.OwnerId, here, next))
            {
                int closed = next != null ? next.OwnerId : -1;
                int dest = Data.Path.ToArray()[Data.Path.Count - 1];
                Data.Path.Clear();
                Data.OrderQueue.Clear();
                if (Data.OwnerId == 0)
                {
                    Data.HasPlayerOrder = false;
                    BorderRules.NotifyBlocked(dest, closed);
                }
                UpdatePathVisuals();
                return;
            }
            Data.TargetSystemId = Data.Path.Dequeue();
            Data.State = FleetState.InHyperlane;
            SFXManager.PlayAt(Sfx.FtlJump, transform.position, Data.OwnerId == 0 ? 1f : 0.6f);
            if (ShowJumpFx()) JumpFx.Departure(transform.position, LaneDir(), FleetIndicator.OwnerColor(Data.OwnerId));
            Data.DaysRemainingInTransit = Data.TotalDaysForTransit;

            _gliding = false;
        }

        private void ArriveAtTargetSystem()
        {
            if (ShowJumpFx())
            {
                JumpFx.Arrival(transform.position, LaneDir(), FleetIndicator.OwnerColor(Data.OwnerId));
                if (Data.Path.Count == 0) SFXManager.PlayAt(Sfx.FtlArrive, transform.position, Data.OwnerId == 0 ? 1f : 0.6f);   // пролётом — без звука
            }
            Data.CurrentSystemId = Data.TargetSystemId;
            Data.TargetSystemId = -1;
            Data.State = FleetState.Orbiting;
            if (Data.Path.Count > 0 && Pinned())
            {
                // Враг в системе — флот останавливается и принимает бой, маршрут продолжит после
                SnapToCurrentSystem(instant: false);
            }
            else if (Data.Path.Count > 0)
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
                    if (Data.OwnerId == 0) SFXManager.Play(Sfx.Construction);
                }

                if (Data.Type == FleetType.Science && Data.SurveyTargetSystemId == Data.CurrentSystemId)
                {
                    Data.State = FleetState.Surveying;
                    Data.DaysRemainingSurvey = Data.TotalSurveyDays;
                }
                else if (Data.Type == FleetType.Science && Data.Path.Count == 0)
                    FleetManager.Instance?.ClearStaleSurveyTarget(Data);   // прилетел не туда (увели вручную) — цель снята

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

        /// <summary>
        /// Перехват в коридоре: флот выходит из прыжка в системе systemId — у цели (прилетел раньше)
        /// или у точки отправления (возвращается; цель снова становится первым пунктом маршрута).
        /// Там его держит перехват, а маршрут продолжится после боя.
        /// </summary>
        public void DropOutOfLane(int systemId)
        {
            if (Data == null || Data.State != FleetState.InHyperlane) return;
            if (ShowJumpFx()) JumpFx.Arrival(transform.position, LaneDir(), FleetIndicator.OwnerColor(Data.OwnerId));   // выбит из коридора
            int target = Data.TargetSystemId;
            if (systemId == target)
            {
                Data.CurrentSystemId = target;
            }
            else if (target >= 0)
            {
                var rest = new List<int>(Data.Path);
                Data.Path.Clear();
                Data.Path.Enqueue(target);
                foreach (int step in rest) Data.Path.Enqueue(step);
            }
            Data.TargetSystemId = -1;
            Data.State = FleetState.Orbiting;
            Data.DaysRemainingInTransit = 0f;
            SnapToCurrentSystem(instant: false);
            if (Data.Path.Count == 0 && Data.OrderQueue.Count > 0) FleetManager.Instance?.AdvanceQueue(this);
            else if (Data.Path.Count == 0 && Data.OwnerId == 0) Data.HasPlayerOrder = false;
        }

        /// <summary>
        /// Переход через Врата Провидения: флот на орбите (не в бою) мгновенно оказывается в системе systemId.
        /// Маршрут и очередь приказов сбрасываются. false — флот сейчас не может пройти.
        /// </summary>
        public bool GateTransfer(int systemId)
        {
            if (Data == null || Data.Destroyed || Data.State != FleetState.Orbiting || Data.InCombat) return false;
            if (_generator == null || systemId < 0 || systemId >= _generator.Systems.Count) return false;
            Color c = FleetIndicator.OwnerColor(Data.OwnerId);
            Vector3 from = transform.position;
            Vector3 dir = (_generator.Systems[systemId].Position - from).normalized;
            if (ShowJumpFx()) JumpFx.Departure(from, dir, c);
            Data.Path.Clear();
            Data.OrderQueue.Clear();
            Data.HasPlayerOrder = false;
            Data.CurrentSystemId = systemId;
            Data.TargetSystemId = -1;
            SnapToCurrentSystem(instant: true);
            if (ShowJumpFx()) JumpFx.Arrival(transform.position, dir, c);
            return true;
        }

        /// <summary>Плашка над кораблём: пересобирается только при смене текста; появление и исчезновение анимирует сама.</summary>
        private void UpdateTag(string label, int more, bool near)
        {
            if (_tag == null) return;
            bool show = near && !string.IsNullOrEmpty(label);
            if (show) _tag.Set(label, more);
            _tag.Show(show, _selAge < 0.3f ? 0.12f : 0f);
            _tag.Tick(Time.unscaledDeltaTime);
        }

        /// <summary>Эффекты прыжка видны на карте галактики — для своих флотов и тех чужих, кого мы видим.</summary>
        private bool ShowJumpFx()
        {
            if (Data == null || Data.Destroyed) return false;
            if (SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView) return false;
            return Data.OwnerId == 0 || Vision.CanSeeFleet(0, Data);
        }

        /// <summary>Направление текущего коридора (откуда → куда).</summary>
        private Vector3 LaneDir()
        {
            int a = Data.CurrentSystemId, b = Data.TargetSystemId;
            if (a < 0 || b < 0 || a >= _generator.Systems.Count || b >= _generator.Systems.Count) return transform.forward;
            return _generator.Systems[b].Position - _generator.Systems[a].Position;
        }

        private bool Pinned() => CombatManager.Instance != null && CombatManager.Instance.IsInterdicted(Data, Data.CurrentSystemId);

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
                if (eco != null) { eco.Alloys += FactionTraits.StarbaseAlloys(0) * 0.5f; eco.RaiseResourcesChanged(); }
                NotificationCenter.Show(Loc.T("Форпост не построен"), Loc.T("Систему уже заняли. Возвращено 50% сплавов"), NotificationCenter.Kind.Warning, 5f);
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

        // Повреждения: дым и огонь из точек на корпусе, пока корабль не починят
        private float _damageTimer;
        private Vector3[] _damageAnchors;

        private void TickDamageFx()
        {
            if (Data == null || Data.Destroyed || _fogHidden || Data.MaxHullPoints <= 0f) return;
            float frac = Data.HullPoints / Data.MaxHullPoints;
            if (frac >= 0.6f) return;
            if ((_damageTimer -= Time.unscaledDeltaTime) > 0f) return;
            float severity = Mathf.Clamp01((0.6f - frac) / 0.6f);
            _damageTimer = Mathf.Lerp(0.32f, 0.09f, severity);
            if (!CombatFx.CanShow(transform.position)) return;
            if (SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView) return;
            if (_damageAnchors == null)
            {
                var b = new Bounds(transform.position, Vector3.zero);
                if (_renderers != null) foreach (var r in _renderers) if (r != null && r.enabled) b.Encapsulate(r.bounds);
                _damageAnchors = new Vector3[3];
                for (int i = 0; i < _damageAnchors.Length; i++)
                {
                    var p = b.center + Vector3.Scale(Random.insideUnitSphere, b.extents * 0.55f);
                    _damageAnchors[i] = transform.InverseTransformPoint(p);
                }
            }
            // Чем тяжелее повреждения, тем больше очагов
            int fires = severity > 0.66f ? 3 : severity > 0.33f ? 2 : 1;
            float size = Data.Type != FleetType.Military ? 0.8f
                       : Data.HullClass == ShipClass.Destroyer ? 1.5f : Data.HullClass == ShipClass.Frigate ? 1.15f : 0.85f;
            CombatFx.Instance.ShipDamage(transform, _damageAnchors[Random.Range(0, fires)], severity, size);
        }

        // ==================== ПРИЦЕЛ ВЫБОРА («Орбита») ====================
        // Под выбранным кораблём — плоское кольцо (лежит горизонтально, не кренится вместе с корпусом).
        // При выборе кольцо прорисовывается по кругу, дуги «встают» пружиной, по кольцу бегает огонёк,
        // шевроны показывают курс в полёте. От плашки к кораблю тянется тонкий стебель.

        private Transform _selMarker, _selArcs, _selArc1, _selArc2, _selOrb, _selChev;
        private LineRenderer _selRing, _selStem;
        private readonly List<(LineRenderer lr, Color c, int g)> _selParts = new List<(LineRenderer, Color, int)>();
        private float _selAge, _selOut = -1f, _selMove;
        private static Material s_selMat;
        private static readonly Color SelColor = new Color(0.45f, 0.95f, 1f);
        private const float SelRadius = 1.3f;
        private const int SelRingPts = 97;

        private void SetSelectionMarker(bool on)
        {
            if (on && _selMarker == null) BuildSelectionMarker();
            if (_selMarker == null) return;
            if (on)
            {
                _selMarker.gameObject.SetActive(true);
                _selAge = 0f;
                _selOut = -1f;
            }
            else if (_selMarker.gameObject.activeSelf && _selOut < 0f) _selOut = 0f;   // короткое затухание
        }

        private void BuildSelectionMarker()
        {
            if (s_selMat == null)
            {
                var add = Resources.Load<Shader>("Shaders/CombatAdditive") ?? ShaderCache.Sprite;
                s_selMat = new Material(add) { name = "ShipSelection", hideFlags = HideFlags.DontSave, renderQueue = 3050 };
            }
            float R = SelRadius;
            Color c = SelColor;
            _selParts.Clear();
            _selMarker = new GameObject("SelectionMarker").transform;
            _selMarker.SetParent(transform, false);

            // 0 — подложка и кольцо
            SelPart(SelArc(_selMarker, "Fill", 0f, 360f, R * 0.5f, R, Color.white), new Color(c.r, c.g, c.b, 0.06f), 0);
            SelPart(SelArc(_selMarker, "RingGlow", 0f, 360f, R, 0.2f, Color.white), new Color(c.r, c.g, c.b, 0.12f), 0);
            _selRing = SelArc(_selMarker, "Ring", 90f, -270f, R, 0.05f, Color.white, SelRingPts);
            SelPart(_selRing, new Color(c.r, c.g, c.b, 0.95f), 0);

            // 1 — вращающиеся дуги: пара сверху/снизу и пара тоньше по бокам
            _selArcs = SelGroup(_selMarker, "Arcs");
            _selArc1 = SelGroup(_selArcs, "Arc1");
            _selArc2 = SelGroup(_selArcs, "Arc2");
            SelPart(SelArc(_selArc1, "A", 60f, 120f, R * 1.19f, 0.09f, Color.white), c, 1);
            SelPart(SelArc(_selArc1, "B", 240f, 300f, R * 1.19f, 0.09f, Color.white), c, 1);
            SelPart(SelArc(_selArc2, "C", -10f, 10f, R * 1.29f, 0.05f, Color.white), new Color(c.r, c.g, c.b, 0.5f), 1);
            SelPart(SelArc(_selArc2, "D", 170f, 190f, R * 1.29f, 0.05f, Color.white), new Color(c.r, c.g, c.b, 0.5f), 1);

            // 2 — огонёк на орбите: хвост гаснет назад, голова ярче и толще
            _selOrb = SelGroup(_selMarker, "Orbit");
            var comet = SelArc(_selOrb, "Comet", 114f, 90f, R, 0.1f, Color.white, 14);
            comet.widthCurve = new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(0.85f, 0.6f), new Keyframe(1f, 1f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 1f) });
            comet.colorGradient = g;
            SelPart(comet, Color.white, 2);

            // 3/4 — шевроны курса (снаружи кольца, по направлению полёта)
            _selChev = SelGroup(_selMarker, "Heading");
            for (int i = 0; i < 2; i++)
            {
                float tip = R * (i == 0 ? 1.45f : 1.565f), hw = R * 0.097f;
                var lr = SelLine(_selChev, "Chevron", new[] { new Vector3(-hw, tip - hw, 0f), new Vector3(0f, tip, 0f), new Vector3(hw, tip - hw, 0f) }, 0.045f);
                SelPart(lr, c, 3 + i);
            }

            // Стебель от плашки к кораблю — в мировых координатах, сверху ярче
            _selStem = SelLine(_selMarker, "Stem", new[] { Vector3.zero, Vector3.up }, 0.02f);
            _selStem.useWorldSpace = true;
            _selStem.alignment = LineAlignment.View;
            _selStem.transform.localRotation = Quaternion.identity;
            var sg = new Gradient();
            sg.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                       new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            _selStem.colorGradient = sg;

            _selMarker.gameObject.SetActive(false);
        }

        private void SelPart(LineRenderer lr, Color c, int group) => _selParts.Add((lr, c, group));

        private static Transform SelGroup(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        private static LineRenderer SelArc(Transform parent, string name, float fromDeg, float toDeg, float r, float width, Color col, int n = 0)
        {
            bool full = Mathf.Abs(toDeg - fromDeg) >= 359f && n == 0;
            if (n == 0) n = full ? 72 : 12;
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, full ? i / (float)n : i / (float)(n - 1));
                pts[i] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            }
            var lr = SelLine(parent, name, pts, width);
            lr.loop = full;
            lr.startColor = lr.endColor = col;
            return lr;
        }

        /// <summary>Лента в плоскости XY объекта; объект повёрнут на 90° по X — фигура лежит горизонтально.</summary>
        private static LineRenderer SelLine(Transform parent, string name, Vector3[] pts, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.alignment = LineAlignment.TransformZ;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.sharedMaterial = s_selMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private static float BackOut(float t)
        {
            const float c1 = 1.9f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        private void LateUpdate()
        {
            if (_selMarker == null || !_selMarker.gameObject.activeSelf) return;
            float dt = Time.unscaledDeltaTime;
            _selAge += dt;
            float t = _selAge;

            // Исчезновение: всё гаснет и чуть сжимается
            float master = 1f, shrink = 1f;
            if (_selOut >= 0f)
            {
                _selOut += dt;
                float k = Mathf.Clamp01(_selOut / 0.22f);
                master = 1f - k;
                shrink = 1f - 0.08f * k;
                if (k >= 1f) { _selMarker.gameObject.SetActive(false); _selOut = -1f; return; }
            }

            // Горизонтально под кораблём, независимо от крена и тангажа
            _selMarker.position = transform.position + Vector3.down * 0.35f;
            _selMarker.rotation = Quaternion.identity;
            _selMarker.localScale = Vector3.one * shrink;

            // Кольцо прорисовывается от верхней точки по часовой
            float rp = Mathf.Clamp01(t / 0.5f);
            float re = 1f - Mathf.Pow(1f - rp, 3f);
            int cnt = Mathf.Clamp(Mathf.RoundToInt(re * SelRingPts), 2, SelRingPts);
            if (_selRing.positionCount != cnt) _selRing.positionCount = cnt;
            float baseA = Mathf.Clamp01(t / 0.3f);

            // Дуги — пружиной из уменьшенного и повёрнутого состояния
            float ap = Mathf.Clamp01((t - 0.12f) / 0.45f);
            float ae = BackOut(ap);
            _selArcs.localScale = Vector3.one * (0.7f + 0.3f * ae);
            _selArcs.localRotation = Quaternion.Euler(0f, -60f * (1f - ae), 0f);
            _selArc1.localRotation = Quaternion.Euler(0f, t * 40f, 0f);
            _selArc2.localRotation = Quaternion.Euler(0f, -t * 27.7f, 0f);
            float arcsA = Mathf.Clamp01(ap * 2f);

            _selOrb.localRotation = Quaternion.Euler(0f, t * 106f, 0f);
            float orbA = Mathf.Clamp01((t - 0.4f) / 0.3f);

            // Шевроны — только в полёте, по направлению носа
            Vector3 fwd = transform.forward; fwd.y = 0f;
            bool moving = Data != null && Data.State == FleetState.InHyperlane && fwd.sqrMagnitude > 0.0001f;
            _selMove = Mathf.MoveTowards(_selMove, moving ? 1f : 0f, dt * 4f);
            if (fwd.sqrMagnitude > 0.0001f) _selChev.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            float chev0 = 0.15f + 0.85f * (0.5f - 0.5f * Mathf.Cos((t / 1.2f) * Mathf.PI * 2f));
            float chev1 = 0.15f + 0.85f * (0.5f - 0.5f * Mathf.Cos(((t - 0.2f) / 1.2f) * Mathf.PI * 2f));

            foreach (var p in _selParts)
            {
                float a = (p.g switch { 0 => baseA, 1 => arcsA, 2 => orbA, 3 => chev0 * _selMove, _ => chev1 * _selMove }) * master;
                if (p.g == 2) continue;
                var col = new Color(p.c.r, p.c.g, p.c.b, p.c.a * a);
                p.lr.startColor = p.lr.endColor = col;
            }
            SetCometAlpha(orbA * master);

            // Стебель: от кончика уголка плашки вниз к кораблю, вырастает после появления плашки
            bool stem = _tag != null && _tag.Shown && _tag.Alpha > 0.01f && _tagRoot != null;
            _selStem.enabled = stem;
            if (stem)
            {
                Vector3 tip = _tag.Root.TransformPoint(_tag.TipLocal);
                Vector3 shipTop = transform.position + Vector3.up * 0.5f;
                float gp = Mathf.Clamp01((t - 0.3f) / 0.22f);
                _selStem.SetPosition(0, tip);
                _selStem.SetPosition(1, Vector3.Lerp(tip, shipTop, gp));
                _selStem.widthMultiplier = 0.02f * _tagRoot.localScale.x;
                var sg = _selStem.colorGradient;
                sg.SetKeys(sg.colorKeys, new[] { new GradientAlphaKey(0.9f * _tag.Alpha * master, 0f), new GradientAlphaKey(0f, 1f) });
                _selStem.colorGradient = sg;
            }
        }

        private float _cometA = -1f;
        private void SetCometAlpha(float a)
        {
            if (Mathf.Abs(a - _cometA) < 0.01f) return;
            _cometA = a;
            foreach (var p in _selParts)
            {
                if (p.g != 2) continue;
                var g = p.lr.colorGradient;
                g.SetKeys(g.colorKeys, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(a, 1f) });
                p.lr.colorGradient = g;
            }
        }

        private void Update()
        {
            ApplyFogVisibility();
            TickDamageFx();
            _animTime += Time.deltaTime;
            _visual?.Tick(_animTime);


            bool tagNear = false;
            if (_camTransform != null && _tagRoot != null)
            {
                _tagRoot.rotation = _camTransform.rotation;
                float dist = Vector3.Distance(transform.position, _camTransform.position);
                _tagRoot.localScale = Vector3.one * Mathf.Clamp(dist / 40f, 0.8f, 3.5f);
                // Издалека состояние показывает значок флота — ярлык только мешал бы
                tagNear = dist < 70f;
            }

            // Бой или работа закончились — корабль возвращается на своё место на орбите
            bool working = Data.State == FleetState.Surveying || Data.State == FleetState.Constructing;
            if (_wasInCombat && !Data.InCombat && !working)
            {
                _wasInCombat = false;
                _combatVel = Vector3.zero;
                if (Data.State != FleetState.InHyperlane) SnapToCurrentSystem(instant: false);
            }

            // Над кораблём — только имя выделенного; состояние показывают значок флота, панель флота и эффекты.
            // В строю подпись одна на группу: над первым выделенным флотом, с числом остальных выделенных
            string label = "";
            int more = 0;
            if (_isSelected && !Data.InCombat)
            {
                var slot = FleetFormation.Holds(Data) || FleetFormation.Travels(Data) ? FleetFormation.Get(this) : default;
                if (slot.SelectedCount <= 1) label = Data.Name;
                else if (slot.FirstSelected == this) { label = Data.Name; more = slot.SelectedCount - 1; }
            }
            UpdateTag(label, more, tagNear);
            _laserBeam.enabled = false;
            _workLight.intensity = 0f;

            if (Data.State == FleetState.InHyperlane && Data.TargetSystemId != -1)
            {
                _work?.Stop();
                Fly();
            }
            else if (Data.InCombat)
            {
                _wasInCombat = true;
                _work?.Stop();
                CombatManeuver();
            }
            else if (Data.State == FleetState.Surveying)
            {
                _wasInCombat = true;
                Vector3 star = _generator.Systems[Data.CurrentSystemId].Position;
                SurveyPatrol(star);
                _work?.Survey(star, 1f - Data.DaysRemainingSurvey / Mathf.Max(0.01f, Data.TotalSurveyDays));
            }
            else if (Data.State == FleetState.Constructing)
            {
                _wasInCombat = true;
                Vector3 star = _generator.Systems[Data.CurrentSystemId].Position;
                Vector3 site = ConstructionSite(star);
                ConstructionHold(star, site);
                _work?.Construct(site, 1f - Data.DaysRemainingConstruction / Mathf.Max(0.01f, Data.TotalConstructionDays));
            }
            else
            {
                _work?.Stop();
                Hold();
            }
        }

        // ==================== ПОЛЁТ ====================

        /// <summary>Скорость перелёта в днях пути за игровой день (как в HandleDayPassed).</summary>
        private float JumpRate()
        {
            float rate = EmpireBonuses.For(Data.OwnerId).HyperlaneSpeed * Landmarks.HyperSpeedMult(Data.OwnerId);
            rate *= Mathf.Max(0.5f, Data.HyperSpeed);
            rate *= LeaderManager.HyperSpeedMult(Data);
            // Пробуждение сада: мицелий Конклава опутывает вражеские корабли, уходящие из его системы
            var cm = CombatManager.Instance;
            if (cm != null && Data.CurrentSystemId >= 0 && cm.IsGardenAwake(Data.CurrentSystemId))
            {
                int sysOwner = _generator.Systems[Data.CurrentSystemId].OwnerId;
                if (sysOwner != Data.OwnerId && Diplomacy.AtWar(Data.OwnerId, sysOwner)) rate *= FactionTraits.GardenSlow;
            }
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

            // Разворот на курс: нос — по коридору, крен — в сторону поворота
            Vector3 dir = destination - origin;
            dir.y = 0f;

            // Попутчики по коридору идут клином за ведущим, а не друг в друге
            var slot = FleetFormation.Get(this);
            if (slot.Index > 0 && dir.sqrMagnitude > 0.0001f)
            {
                float spacing = Data.Type == FleetType.Military ? 4.0f : 2.6f;
                targetPos += Quaternion.LookRotation(dir.normalized, Vector3.up) * FleetFormation.LocalOffset(slot.Index, spacing);
            }
            transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-10f * Time.deltaTime));
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
            UpdateFormation();
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

            // Выравниваем крен и тангаж — корабль стоит ровно; на месте разворачивается по курсу своей группы
            Vector3 fwd = Flat(transform.forward);
            if (!_gliding && _formHeading.sqrMagnitude > 0.0001f && fwd.sqrMagnitude > 0.0001f)
                fwd = Vector3.RotateTowards(fwd.normalized, _formHeading, TurnRateDeg * 0.5f * Mathf.Deg2Rad * Time.deltaTime, 0f);
            if (fwd.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(fwd.normalized, Vector3.up), 1f - Mathf.Exp(-2.5f * Time.deltaTime));
            _bank = Mathf.Lerp(_bank, 0f, 1f - Mathf.Exp(-3f * Time.deltaTime));

            _throttle = Mathf.MoveTowards(_throttle, main, 2f * Time.deltaTime);
            _retro = Mathf.MoveTowards(_retro, 0f, 2f * Time.deltaTime);
            ApplyThrottle(_throttle, _retro);
            _engineTrail.emitting = _throttle > 0.05f;
        }

        /// <summary>
        /// Манёвры в бою: корабль выходит на своё место в строю и там не стоит столбом — смещается вбок,
        /// вперёд-назад и по высоте (корветы резво, эсминцы степенно), держа нос на цели. На переходах
        /// смотрит по ходу, кренится в разворотах, работает маршевыми и тормозными. На паузе замирает.
        /// </summary>
        private void CombatManeuver()
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.TryGetCombatStation(Data, out Vector3 station, out Vector3 aim)) { EnginesIdle(); return; }
            _gliding = false;

            var tm = TimeManager.Instance;
            float dt = tm != null && tm.CurrentSpeed == 0 ? 0f : Time.deltaTime;
            if (dt <= 0f) { ApplyThrottle(_throttle, _retro); return; }
            _combatClock += dt;

            bool military = Data.Type == FleetType.Military;
            var hull = Data.HullClass;
            float amp = !military ? 0.35f : hull == ShipClass.Corvette ? 1.3f : hull == ShipClass.Frigate ? 0.9f : 0.55f;
            float freq = !military ? 0.25f : hull == ShipClass.Corvette ? 0.55f : hull == ShipClass.Frigate ? 0.4f : 0.28f;
            float smooth = !military ? 1.4f : hull == ShipClass.Corvette ? 0.8f : hull == ShipClass.Frigate ? 1.1f : 1.5f;
            float maxSpeed = !military ? 4f : hull == ShipClass.Corvette ? 7f : hull == ShipClass.Frigate ? 5.5f : 4f;
            float turn = !military ? 70f : hull == ShipClass.Corvette ? 130f : hull == ShipClass.Frigate ? 95f : 60f;

            Vector3 axis = Flat(aim - station);
            axis = axis.sqrMagnitude > 0.01f ? axis.normalized : Vector3.forward;
            Vector3 side = new Vector3(-axis.z, 0f, axis.x);
            // Звено одного класса маневрирует синхронно (общая фаза), каждый корабль — с небольшим сдвигом:
            // соседи в строю качаются вместе и не налетают друг на друга
            int group = Data.OwnerId * 8 + (military ? (int)hull : 3);
            float seed = Mathf.Repeat(group * 0.618034f, 1f) * 6.2832f + Mathf.Repeat(Data.Id * 0.381966f, 1f) * 0.35f;
            float t = _combatClock * freq;
            Vector3 goal = station
                         + side * (Mathf.Sin(t + seed) * amp)
                         + axis * (Mathf.Sin(t * 0.7f + seed * 1.3f) * amp * 0.45f)
                         + Vector3.up * (Mathf.Sin(t * 1.3f + seed * 2.1f) * 0.18f);

            Steer(goal, aim, smooth, maxSpeed, turn, 0.08f, dt);
        }

        /// <summary>
        /// Плавный выход в точку goal: скорость по SmoothDamp, на переходе нос по ходу, на месте — на aim;
        /// крен в разворотах и при боковом смещении, маршевые вперёд, тормозные назад, idle — тяга на месте.
        /// </summary>
        private void Steer(Vector3 goal, Vector3 aim, float smooth, float maxSpeed, float turn, float idle, float dt)
        {
            transform.position = Vector3.SmoothDamp(transform.position, goal, ref _combatVel, smooth, maxSpeed, dt);

            Vector3 vel = Flat(_combatVel);
            bool transit = (goal - transform.position).sqrMagnitude > 4f && vel.sqrMagnitude > 0.5f;
            Vector3 look = transit ? vel : Flat(aim - transform.position);
            Vector3 fwdBefore = Flat(transform.forward);
            if (look.sqrMagnitude > 0.0001f && fwdBefore.sqrMagnitude > 0.0001f)
            {
                Quaternion level = Quaternion.LookRotation(fwdBefore.normalized, Vector3.up);
                Quaternion want = Quaternion.LookRotation(look.normalized, Vector3.up);
                Quaternion next = Quaternion.RotateTowards(level, want, turn * dt);
                float yawRate = Vector3.SignedAngle(level * Vector3.forward, next * Vector3.forward, Vector3.up) / dt;
                float lateral = Vector3.Dot(vel, next * Vector3.right);
                float targetBank = Mathf.Clamp(-yawRate * 0.3f - lateral * 5f, -MaxBankDeg, MaxBankDeg);
                _bank = Mathf.Lerp(_bank, targetBank, 1f - Mathf.Exp(-3.5f * dt));
                transform.rotation = next * Quaternion.Euler(0f, 0f, _bank);
            }

            float along = Vector3.Dot(_combatVel, transform.forward);
            float main = idle + Mathf.Clamp01(along / 3f) * 0.8f;
            float retro = Mathf.Clamp01(-along / 2.5f);
            _throttle = Mathf.MoveTowards(_throttle, main, 2.5f * dt);
            _retro = Mathf.MoveTowards(_retro, retro, 2.5f * dt);
            ApplyThrottle(_throttle, _retro);
            _engineTrail.emitting = _combatVel.sqrMagnitude > 1.4f;
        }

        private float WorkDt()
        {
            var tm = TimeManager.Instance;
            return tm != null && tm.CurrentSpeed == 0 ? 0f : Time.deltaTime;
        }

        /// <summary>
        /// Разведка: корабль неспешно облетает звезду по широкой орбите носом по курсу, слегка меняя высоту;
        /// сенсоры (ShipWorkFx) направлены на центр системы.
        /// </summary>
        private void SurveyPatrol(Vector3 star)
        {
            _gliding = false;
            float dt = WorkDt();
            if (dt <= 0f) { ApplyThrottle(_throttle, _retro); return; }
            _workClock += dt;
            float seed = Mathf.Repeat(Data.Id * 0.618034f, 1f) * 6.2832f;
            float a = seed + _workClock * 0.16f;
            const float r = 4.6f;
            Vector3 goal = star + new Vector3(Mathf.Cos(a) * r, 0.8f + 0.2f * Mathf.Sin(_workClock * 0.5f), Mathf.Sin(a) * r);
            Vector3 ahead = star + new Vector3(Mathf.Cos(a + 0.6f) * r, 0.8f, Mathf.Sin(a + 0.6f) * r);
            Steer(goal, ahead, 1.3f, 3f, 55f, 0.12f, dt);
        }

        /// <summary>Строительная площадка станции: своя точка у каждой системы, недалеко от звезды.</summary>
        private static Vector3 ConstructionSite(Vector3 star) => StarbaseVisuals.SiteFor(star);

        /// <summary>Монтаж: корабль висит рядом с площадкой носом к ней и понемногу подруливает, удерживая позицию.</summary>
        private void ConstructionHold(Vector3 star, Vector3 site)
        {
            _gliding = false;
            float dt = WorkDt();
            if (dt <= 0f) { ApplyThrottle(_throttle, _retro); return; }
            _workClock += dt;
            Vector3 outward = Flat(site - star);
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
            Vector3 side = new Vector3(-outward.z, 0f, outward.x);
            Vector3 goal = site + outward * 2.5f + Vector3.up * 0.6f
                         + side * (Mathf.Sin(_workClock * 0.35f) * 0.45f)
                         + Vector3.up * (Mathf.Sin(_workClock * 0.6f) * 0.12f);
            Steer(goal, site, 1.4f, 3f, 50f, 0.06f, dt);
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