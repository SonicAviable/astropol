using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Generation;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    public class AIEmpireManager : MonoBehaviour
    {
        public static AIEmpireManager Instance { get; private set; }

        public const int AIOwnerId = 1;
        private GalaxyGenerator _generator;

        public int CapitalSystemId { get; private set; } = -1;

        // === ИМЯ AI-ИМПЕРИИ (выбирается при старте из фракций, не занятых игроком) ===
        public string AIName { get; private set; } = "Неизвестная империя";
        public string AITitle { get; private set; } = "";
        public Color AIEmpireColor { get; private set; } = new Color(0.95f, 0.25f, 0.20f);

        public float EnergyCredits = 200f;
        public float Minerals = 300f;
        public float Alloys = 80f;
        public float Influence = 35f;
        public float MonthlyAlloysIncome = 7f;
        public float MonthlyInfluenceIncome = 3f;

        public float RelationsWithPlayer = 8f;
        public bool IsHostileToPlayer => RelationsWithPlayer < 0f;

        private const float StarbaseAlloysCost = 50f;
        private const float StarbaseInfluenceCost = 25f;
        private const float CorvetteAlloysCost = 60f;
        private const float MiningAlloysCost = 35f;

        private FleetView _aiScienceShip;
        private FleetView _aiConstructorShip;
        private readonly List<FleetView> _warFleets = new List<FleetView>();

        private int _surveyTargetSysId = -1;
        private int _buildTargetSysId = -1;
        private int _warTargetSysId = -1;
        private float _decisionTimer = 5f;
        private int _warFleetSerial = 1;

        private float _lastRelationBucket = 8f;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            _generator = FindFirstObjectByType<GalaxyGenerator>();
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed += HandleDayPassed;
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed -= HandleDayPassed;
        }

        // ==================== ВЫБОР ИМЕНИ ====================

        private void PickAIName()
        {
            FactionInfo playerFaction = null;
            if (UIManager.Instance != null) playerFaction = UIManager.Instance.SelectedFaction;

            var candidates = new List<FactionInfo>();
            foreach (var f in FactionRegistry.AvailableFactions)
            {
                if (playerFaction != null && f.Name == playerFaction.Name) continue;
                candidates.Add(f);
            }

            if (candidates.Count == 0)
            {
                // На всякий случай — если игрок ещё не выбрал фракцию
                foreach (var f in FactionRegistry.AvailableFactions) candidates.Add(f);
            }

            var pick = candidates[Random.Range(0, candidates.Count)];
            AIName = pick.Name;
            AITitle = pick.Title;
            AIEmpireColor = pick.EmpireColor;

            Debug.Log($"<color=#FFAA66>[AI]</color> Империя-противник: <b>{AIName}</b> ({AITitle})");
        }

        public void InitializeAIEmpire()
        {
            if (_generator == null || _generator.Systems.Count == 0) return;

            // Название выбираем в момент спавна
            PickAIName();

            // Сложность: доходы, запасы и стартовое отношение
            var st = GameSession.Settings;
            MonthlyAlloysIncome *= st.AIIncome;
            MonthlyInfluenceIncome *= st.AIIncome;
            Alloys *= st.AIIncome;
            if (st.Difficulty == 0) RelationsWithPlayer = 20f;
            else if (st.Difficulty == 2) RelationsWithPlayer = 2f;
            _lastRelationBucket = RelationsWithPlayer;

            int bestSysId = -1;
            float maxDist = -1f;
            Vector3 playerPos = _generator.Systems[0].Position;

            for (int i = 1; i < _generator.Systems.Count; i++)
            {
                float d = Vector3.Distance(playerPos, _generator.Systems[i].Position);
                if (d > maxDist)
                {
                    maxDist = d;
                    bestSysId = i;
                }
            }

            if (bestSysId != -1)
            {
                CapitalSystemId = bestSysId;
                var aiCapital = _generator.Systems[CapitalSystemId];

                aiCapital.OwnerId = AIOwnerId;
                aiCapital.HasStarbase = true;
                aiCapital.IsSurveyed = true;

                _aiScienceShip = CreateAIFleet($"{AIName} · Разведчик", CapitalSystemId, FleetType.Science);
                _aiConstructorShip = CreateAIFleet($"{AIName} · Строитель", CapitalSystemId, FleetType.Constructor);
                _warFleets.Add(CreateAIFleet($"{AIName} · Авангард", CapitalSystemId, FleetType.Military));

                GalaxyView.Instance?.RefreshTerritoryVisuals();
            }
        }

        private FleetView CreateAIFleet(string fleetName, int startSystemId, FleetType type)
        {
            FleetView view;
            if (FleetManager.Instance != null)
            {
                view = FleetManager.Instance.CreateOwnedFleet(fleetName, startSystemId, type, AIOwnerId);
            }
            else
            {
                GameObject fleetObj = new GameObject($"AIFleet_{fleetName}");
                fleetObj.transform.SetParent(transform);
                var data = new FleetData(900 + _warFleetSerial, fleetName, startSystemId, type);
                data.OwnerId = AIOwnerId;
                data.ApplyDefaultCombatStats(type);
                view = fleetObj.AddComponent<FleetView>();
                view.Initialize(data, _generator);
            }
            return view;
        }

        private void HandleDayPassed(int day, int month, int year)
        {
            if (CapitalSystemId == -1)
            {
                InitializeAIEmpire();
                return;
            }

            Alloys += MonthlyAlloysIncome / 30f;
            Influence += MonthlyInfluenceIncome / 30f;
            EnergyCredits += 6f / 30f;
            Minerals += 8f / 30f;

            UpdateRelations();

            _decisionTimer -= 1f;
            if (_decisionTimer <= 0f)
            {
                _decisionTimer = Random.Range(3f, 5f);
                ThinkAndAct();
            }
        }

        private void UpdateRelations()
        {
            int playerPow = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(0) : 0;
            int aiPow = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(AIOwnerId) : 120;

            if (playerPow > aiPow * 1.15f)
                RelationsWithPlayer -= 1.6f * GameSession.Settings.AIAggression;
            else
                RelationsWithPlayer += 0.15f;

            RelationsWithPlayer = Mathf.Clamp(RelationsWithPlayer, -100f, 40f);

            int bucket = RelationsWithPlayer < -60f ? 0
                       : RelationsWithPlayer < -30f ? 1
                       : RelationsWithPlayer < 0f   ? 2
                       : RelationsWithPlayer < 20f  ? 3
                       : 4;

            int oldBucket = _lastRelationBucket < -60f ? 0
                          : _lastRelationBucket < -30f ? 1
                          : _lastRelationBucket < 0f   ? 2
                          : _lastRelationBucket < 20f  ? 3
                          : 4;

            if (bucket != oldBucket)
            {
                if (bucket < oldBucket)
                {
                    string msg = bucket switch
                    {
                        0 => $"{AIName} готовится к войне",
                        1 => $"{AIName} враждебен",
                        2 => $"{AIName} охладел к вам",
                        _ => "Отношения меняются"
                    };
                    NotificationCenter.Show("Отношения ухудшились", msg, NotificationCenter.Kind.Warning, 5f);
                }
                else
                {
                    NotificationCenter.Show("Отношения улучшились",
                        $"Текущее значение: {RelationsWithPlayer:0}",
                        NotificationCenter.Kind.Success, 4f);
                }
            }
            _lastRelationBucket = RelationsWithPlayer;

            DetectLocalEncounters();
        }

        private void DetectLocalEncounters()
        {
            if (_generator == null || FleetManager.Instance == null) return;
            foreach (var sys in _generator.Systems)
            {
                if (sys.OwnerId == 0) continue;
                var fleets = FleetManager.Instance.GetFleetsInSystem(sys.Id);
                bool hasPlayer = false, hasAi = false;
                foreach (var f in fleets)
                {
                    if (f.Data.OwnerId == 0) hasPlayer = true;
                    if (f.Data.OwnerId == AIOwnerId) hasAi = true;
                }
                if (hasPlayer && hasAi && sys.OwnerId != 0)
                    RelationsWithPlayer -= 4f;
            }
        }

        private void ThinkAndAct()
        {
            if (_generator == null) return;
            PruneDeadWarFleets();

            ThinkSurvey();
            ThinkClaim();
            ThinkEconomy();
            ThinkMilitarize();
            ThinkWar();
        }

        private void ThinkSurvey()
        {
            if (_aiScienceShip == null || _aiScienceShip.Data == null || _aiScienceShip.Data.Destroyed)
            {
                _aiScienceShip = CreateAIFleet($"{AIName} · Разведчик", CapitalSystemId, FleetType.Science);
                _surveyTargetSysId = -1;
            }

            if (_aiScienceShip.Data.State == FleetState.Orbiting && _surveyTargetSysId == -1 && !_aiScienceShip.Data.InCombat)
            {
                int bestSurveyTarget = FindBestSystemToSurvey();
                if (bestSurveyTarget != -1)
                {
                    _surveyTargetSysId = bestSurveyTarget;
                    _aiScienceShip.Data.SurveyTargetSystemId = bestSurveyTarget;
                    FleetManager.Instance?.IssueMoveOrder(_aiScienceShip, bestSurveyTarget);
                }
            }

            if (_surveyTargetSysId != -1 && _surveyTargetSysId < _generator.Systems.Count
                && _generator.Systems[_surveyTargetSysId].IsSurveyed)
            {
                _surveyTargetSysId = -1;
            }
        }

        private void ThinkClaim()
        {
            if (_aiConstructorShip == null || _aiConstructorShip.Data == null || _aiConstructorShip.Data.Destroyed)
            {
                _aiConstructorShip = CreateAIFleet($"{AIName} · Строитель", CapitalSystemId, FleetType.Constructor);
                _buildTargetSysId = -1;
            }

            if (_aiConstructorShip.Data.State == FleetState.Orbiting && _buildTargetSysId == -1 && !_aiConstructorShip.Data.InCombat)
            {
                if (Alloys >= StarbaseAlloysCost && Influence >= StarbaseInfluenceCost)
                {
                    int bestBuildTarget = FindBestSystemToClaim();
                    if (bestBuildTarget != -1)
                    {
                        Alloys -= StarbaseAlloysCost;
                        Influence -= StarbaseInfluenceCost;
                        _buildTargetSysId = bestBuildTarget;
                        _aiConstructorShip.Data.BuildTargetSystemId = bestBuildTarget;
                        FleetManager.Instance?.IssueMoveOrder(_aiConstructorShip, bestBuildTarget);
                    }
                }
            }

            if (_buildTargetSysId != -1 && _buildTargetSysId < _generator.Systems.Count)
            {
                var targetSys = _generator.Systems[_buildTargetSysId];
                if (targetSys.OwnerId == AIOwnerId)
                    _buildTargetSysId = -1;
            }
        }

        private void ThinkEconomy()
        {
            if (Alloys < MiningAlloysCost) return;

            foreach (var sys in _generator.Systems)
            {
                if (sys.OwnerId != AIOwnerId) continue;
                sys.GeneratePlanets();
                foreach (var p in sys.Planets)
                {
                    if (p.HasMiningStation) continue;
                    if (p.EnergyDeposit <= 0 && p.MineralDeposit <= 0) continue;
                    Alloys -= MiningAlloysCost;
                    p.HasMiningStation = true;
                    MonthlyAlloysIncome += 1.2f + p.MineralDeposit * 0.15f;
                    sys.RecalculateHarvest();
                    return;
                }
            }
        }

        private void ThinkMilitarize()
        {
            int playerPow = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(0) : 0;
            int aiPow = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(AIOwnerId) : 0;
            if (playerPow <= aiPow + 40) return;
            if (Alloys < CorvetteAlloysCost) return;
            if (_warFleets.Count >= 6) return;

            Alloys -= CorvetteAlloysCost;
            var fleet = CreateAIFleet($"{AIName} · Крыло {_warFleetSerial++}", CapitalSystemId, FleetType.Military);
            _warFleets.Add(fleet);
        }

        private void ThinkWar()
        {
            if (!IsHostileToPlayer) return;
            PruneDeadWarFleets();
            var hunter = GetIdleWarFleet();
            if (hunter == null) return;

            int target = FindPlayerBorderOutpost();
            if (target < 0) return;
            _warTargetSysId = target;
            FleetManager.Instance?.IssueMoveOrder(hunter, target);

            if (hunter.Data.CurrentSystemId == target && hunter.Data.State == FleetState.Orbiting)
            {
                var sys = _generator.Systems[target];
                bool playerMilitaryPresent = false;
                if (FleetManager.Instance != null)
                {
                    foreach (var f in FleetManager.Instance.GetFleetsInSystem(target))
                    {
                        if (f.Data.OwnerId == 0 && f.Data.Type == FleetType.Military)
                            playerMilitaryPresent = true;
                    }
                }
                if (!playerMilitaryPresent)
                {
                    sys.OwnerId = AIOwnerId;
                    sys.HasStarbase = true;
                    GalaxyView.Instance?.RefreshTerritoryVisuals();
                    MonthlyAlloysIncome += 1.5f;
                    NotificationCenter.Show("Система потеряна",
                        $"{sys.Name} захвачена: {AIName}",
                        NotificationCenter.Kind.Danger, 8f);
                }
            }
        }

        private FleetView GetIdleWarFleet()
        {
            foreach (var f in _warFleets)
            {
                if (f == null || f.Data == null || f.Data.Destroyed) continue;
                if (f.Data.InCombat) continue;
                if (f.Data.State == FleetState.Orbiting && f.Data.Path.Count == 0)
                    return f;
            }
            return null;
        }

        private void PruneDeadWarFleets()
        {
            _warFleets.RemoveAll(f => f == null || f.Data == null || f.Data.Destroyed);
        }

        private int FindPlayerBorderOutpost()
        {
            int best = -1;
            float bestDist = float.MaxValue;
            Vector3 origin = _generator.Systems[CapitalSystemId].Position;

            foreach (var s in _generator.Systems)
            {
                if (s.OwnerId != 0 || !s.HasStarbase) continue;
                bool adjacentToAi = false;
                foreach (int n in s.ConnectedSystemIds)
                {
                    if (n >= 0 && n < _generator.Systems.Count && _generator.Systems[n].OwnerId == AIOwnerId)
                    {
                        adjacentToAi = true;
                        break;
                    }
                }
                if (!adjacentToAi) continue;
                float d = Vector3.Distance(origin, s.Position);
                if (d < bestDist) { bestDist = d; best = s.Id; }
            }
            return best;
        }

        private int FindBestSystemToSurvey()
        {
            List<int> borderSystems = GetAIBorderConnectedSystems();
            int best = -1;
            float highestScore = -1f;

            foreach (int sysId in borderSystems)
            {
                var sys = _generator.Systems[sysId];
                if (!sys.IsSurveyed && sys.OwnerId == -1)
                {
                    float score = sys.ConnectedSystemIds.Count * 10f - Vector3.Distance(sys.Position, Vector3.zero) * 0.1f;
                    if (score > highestScore)
                    {
                        highestScore = score;
                        best = sysId;
                    }
                }
            }
            return best;
        }

        private int FindBestSystemToClaim()
        {
            List<int> borderSystems = GetAIBorderConnectedSystems();
            int best = -1;
            int bestResources = -1;

            foreach (int sysId in borderSystems)
            {
                var sys = _generator.Systems[sysId];
                if (sys.IsSurveyed && sys.OwnerId == -1)
                {
                    int resSum = sys.TotalMinerals + sys.TotalEnergy;
                    if (resSum > bestResources)
                    {
                        bestResources = resSum;
                        best = sysId;
                    }
                }
            }
            return best;
        }

        private List<int> GetAIBorderConnectedSystems()
        {
            List<int> list = new List<int>();
            foreach (var s in _generator.Systems)
            {
                if (s.OwnerId == AIOwnerId)
                {
                    foreach (int neighbor in s.ConnectedSystemIds)
                    {
                        if (!list.Contains(neighbor) && _generator.Systems[neighbor].OwnerId == -1)
                            list.Add(neighbor);
                    }
                }
            }
            return list;
        }

        // ==================== СОХРАНЕНИЕ ====================

        public AISave CaptureState() => new AISave
        {
            Capital = CapitalSystemId, Name = AIName, Title = AITitle, Color = AIEmpireColor,
            Energy = EnergyCredits, Minerals = Minerals, Alloys = Alloys, Influence = Influence,
            AlloysIncome = MonthlyAlloysIncome, InfluenceIncome = MonthlyInfluenceIncome,
            Relations = RelationsWithPlayer, LastRelationBucket = _lastRelationBucket,
            WarFleetSerial = _warFleetSerial
        };

        /// <summary>Восстановить империю ИИ; флоты уже созданы FleetManager — привязываем их по типу.</summary>
        public void RestoreState(AISave s)
        {
            if (s == null) return;
            if (_generator == null) _generator = FindFirstObjectByType<GalaxyGenerator>();
            CapitalSystemId = s.Capital;
            if (!string.IsNullOrEmpty(s.Name)) AIName = s.Name;
            AITitle = s.Title ?? "";
            AIEmpireColor = s.Color;
            EnergyCredits = s.Energy; Minerals = s.Minerals; Alloys = s.Alloys; Influence = s.Influence;
            MonthlyAlloysIncome = s.AlloysIncome; MonthlyInfluenceIncome = s.InfluenceIncome;
            RelationsWithPlayer = s.Relations;
            _lastRelationBucket = s.LastRelationBucket;
            _warFleetSerial = Mathf.Max(1, s.WarFleetSerial);

            _aiScienceShip = null;
            _aiConstructorShip = null;
            _warFleets.Clear();
            _surveyTargetSysId = _buildTargetSysId = _warTargetSysId = -1;
            if (FleetManager.Instance == null) return;
            foreach (var f in FleetManager.Instance.AllFleets)
            {
                if (f?.Data == null || f.Data.OwnerId != AIOwnerId) continue;
                switch (f.Data.Type)
                {
                    case FleetType.Science:
                        if (_aiScienceShip == null) { _aiScienceShip = f; _surveyTargetSysId = f.Data.SurveyTargetSystemId; }
                        break;
                    case FleetType.Constructor:
                        if (_aiConstructorShip == null) { _aiConstructorShip = f; _buildTargetSysId = f.Data.BuildTargetSystemId; }
                        break;
                    default:
                        _warFleets.Add(f);
                        break;
                }
            }
        }

        // ==================== API ДЛЯ ТОРГОВЛИ ====================

        public float GetStock(string resource)
        {
            return resource switch
            {
                "energy"    => EnergyCredits,
                "minerals"  => Minerals,
                "alloys"    => Alloys,
                "influence" => Influence,
                _           => 0f
            };
        }

        public void SpendStock(string resource, float amount)
        {
            switch (resource)
            {
                case "energy":    EnergyCredits = Mathf.Max(0f, EnergyCredits - amount); break;
                case "minerals":  Minerals = Mathf.Max(0f, Minerals - amount); break;
                case "alloys":    Alloys = Mathf.Max(0f, Alloys - amount); break;
                case "influence": Influence = Mathf.Max(0f, Influence - amount); break;
            }
        }

        public void AddStock(string resource, float amount)
        {
            switch (resource)
            {
                case "energy":    EnergyCredits += amount; break;
                case "minerals":  Minerals += amount; break;
                case "alloys":    Alloys += amount; break;
                case "influence": Influence += amount; break;
            }
        }
    }
}