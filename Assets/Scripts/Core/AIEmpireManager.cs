using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Generation;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Империя-соперник. Играет по тем же правилам, что и игрок:
    ///   • своя экономика — колонии, районы, добывающие станции, содержание флота и форпостов;
    ///   • свои исследования и бонусы технологий (EmpireBonuses);
    ///   • разведка и экспансия по ценности систем;
    ///   • флот, который собирается в группу, защищает границы, осаждает и отступает при слабости
    ///     (AIEmpireManager.Military.cs);
    ///   • дипломатия с объяснимым отношением, пактами, войной и миром (AIEmpireManager.Diplomacy.cs).
    /// Характер (воинственный / научный / торговый) зависит от фракции.
    /// </summary>
    public partial class AIEmpireManager : MonoBehaviour
    {
        public static AIEmpireManager Instance { get; private set; }

        public const int AIOwnerId = 1;
        private GalaxyGenerator _generator;

        public int CapitalSystemId { get; private set; } = -1;
        public bool IsEliminated { get; private set; }

        public string AIName { get; private set; } = "Неизвестная империя";
        public string AITitle { get; private set; } = "";
        public Color AIEmpireColor { get; private set; } = new Color(0.95f, 0.25f, 0.20f);
        public FactionInfo Faction { get; private set; }

        public AIPersonality Personality { get; private set; } = AIPersonality.Militarist;
        public AIProfile Profile { get; private set; } = AIProfile.Get(AIPersonality.Militarist);

        /// <summary>Бонусы технологий, изученных ИИ.</summary>
        public EmpireBonuses Bonuses { get; private set; } = new EmpireBonuses();

        // ==================== ЭКОНОМИКА ====================

        public float EnergyCredits = 220f;
        public float Minerals = 280f;
        public float Alloys = 160f;
        public float Influence = 60f;

        public float BaseEnergyIncome = 15f;
        public float BaseMineralsIncome = 10f;
        public float BaseAlloysIncome = 5f;
        public float BaseInfluenceIncome = 3f;

        private EmpireEconomy.Report _report;
        private bool _bankrupt;

        /// <summary>Множитель доходов ИИ от сложности.</summary>
        private static float IncomeMult => GameSession.Settings.AIIncome;

        private float FactionEnergy => Faction != null ? Faction.EnergyBonus : 1f;
        private float FactionMinerals => Faction != null ? Faction.MineralBonus : 1f;
        private float FactionAlloys => Faction != null ? Faction.AlloyBonus : 1f;
        private float FactionInfluence => Faction != null ? Faction.InfluenceBonus : 1f;

        public EmpireEconomy.Report Report => _report;
        public bool IsBankrupt => _bankrupt;
        public float MonthlyEnergyIncome => (BaseEnergyIncome + _report.Energy) * IncomeMult - _report.Upkeep;
        public float MonthlyMineralsIncome => (BaseMineralsIncome + _report.Minerals) * IncomeMult;
        public float MonthlyAlloysIncome => (BaseAlloysIncome + _report.Alloys) * IncomeMult;
        public float MonthlyInfluenceIncome => BaseInfluenceIncome * FactionInfluence * IncomeMult;

        // ==================== НАУКА ====================

        private readonly HashSet<string> _researched = new HashSet<string>();
        private List<Technology> _techTree;
        public Technology CurrentTech { get; private set; }
        public float TechProgress { get; private set; }
        private int _researchSlots = 3;

        public int ResearchedCount => _researched.Count;
        public bool HasTech(string id) => _researched.Contains(id);

        public float MonthlyScience =>
            (TechnologyManager.BaseScience + EmpireStats.Population(AIOwnerId) * TechnologyManager.SciencePerPop)
            * Profile.ScienceMult * Bonuses.ResearchMult * IncomeMult;

        /// <summary>Параллельные исследования дают тот же выигрыш, что и у игрока: N^0.3.</summary>
        private float ResearchThroughput => Mathf.Pow(_researchSlots, 1f - TechnologyManager.SlotSplitExponent);

        private int _scienceWarnLevel;

        // ==================== ПРОЧЕЕ ====================

        private float _decisionTimer = 3f;
        private int _warFleetSerial = 1;
        private readonly Dictionary<ShipClass, ShipDesign> _designs = new Dictionary<ShipClass, ShipDesign>();

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
            CombatManager.OnShipDestroyed += HandleShipDestroyed;
            SiegeManager.OnSystemCaptured += HandleSystemCaptured;
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed -= HandleDayPassed;
            CombatManager.OnShipDestroyed -= HandleShipDestroyed;
            SiegeManager.OnSystemCaptured -= HandleSystemCaptured;
        }

        private List<Technology> TechTree
        {
            get
            {
                if (_techTree == null) _techTree = TechnologyManager.BuildTechTree();
                return _techTree;
            }
        }

        // ==================== СТАРТ ====================

        private void PickFaction()
        {
            FactionInfo playerFaction = UIManager.Instance != null ? UIManager.Instance.SelectedFaction : null;

            var candidates = new List<FactionInfo>();
            foreach (var f in FactionRegistry.AvailableFactions)
            {
                if (playerFaction != null && f.Name == playerFaction.Name) continue;
                candidates.Add(f);
            }
            if (candidates.Count == 0) candidates.AddRange(FactionRegistry.AvailableFactions);

            SetFaction(candidates[Random.Range(0, candidates.Count)]);
            Debug.Log($"<color=#FFAA66>[AI]</color> Империя-противник: <b>{AIName}</b> ({AITitle}), характер: {Profile.Name}");
        }

        private void SetFaction(FactionInfo f)
        {
            Faction = f;
            AIName = f.Name;
            AITitle = f.Title;
            AIEmpireColor = f.EmpireColor;
            Personality = AIProfile.FromFactionName(f.Name);
            Profile = AIProfile.Get(Personality);
        }

        public void InitializeAIEmpire()
        {
            if (_generator == null || _generator.Systems.Count == 0) return;

            PickFaction();

            int bestSysId = -1;
            float maxDist = -1f;
            Vector3 playerPos = _generator.Systems[0].Position;
            for (int i = 1; i < _generator.Systems.Count; i++)
            {
                float d = Vector3.Distance(playerPos, _generator.Systems[i].Position);
                if (d > maxDist) { maxDist = d; bestSysId = i; }
            }
            if (bestSysId == -1) return;

            CapitalSystemId = bestSysId;
            var capital = _generator.Systems[CapitalSystemId];
            capital.OwnerId = AIOwnerId;
            capital.HasStarbase = true;
            capital.IsSurveyed = true;
            capital.SurveyedByAI = true;
            capital.GeneratePlanets();
            foreach (var p in capital.Planets)
            {
                if (!p.CanColonize) continue;
                p.SetupAsStartingColony(pop: 6, urban: 3, mining: 1, generator: 1, industrial: 1);
                break;
            }

            var st = GameSession.Settings;
            EnergyCredits *= st.AIIncome;
            Minerals *= st.AIIncome;
            Alloys *= st.AIIncome;
            InitDiplomacy();

            CreateCivilian(FleetType.Science);
            CreateCivilian(FleetType.Constructor);
            CreateWarship(ShipClass.Corvette);
            if (Personality == AIPersonality.Militarist || st.Difficulty == 2) CreateWarship(ShipClass.Corvette);

            RecalculateEconomy();
            GalaxyView.Instance?.RefreshTerritoryVisuals();
        }

        // ==================== ЕЖЕДНЕВНЫЙ ЦИКЛ ====================

        private void HandleDayPassed(int day, int month, int year)
        {
            if (!UIManager.IsGameStarted) return;
            if (CapitalSystemId == -1)
            {
                InitializeAIEmpire();
                return;
            }
            if (IsEliminated) return;
            if (EmpireStats.SystemCount(AIOwnerId) == 0)
            {
                IsEliminated = true;
                if (AtWar) MakePeace(silent: true);
                NotificationCenter.Show("Соперник повержен", $"{AIName} потерял все системы", NotificationCenter.Kind.Success, 10f);
                RaiseDiplomacyChanged();
                return;
            }

            RecalculateEconomy();
            TickResearch(year);
            DailyDiplomacy();

            if (day == 1) MonthlyTick();

            _decisionTimer -= 1f;
            if (_decisionTimer <= 0f)
            {
                _decisionTimer = Random.Range(3f, 5f);
                ThinkAndAct();
            }
        }

        private void MonthlyTick()
        {
            EnergyCredits += MonthlyEnergyIncome;
            Minerals += MonthlyMineralsIncome;
            Alloys += MonthlyAlloysIncome;
            Influence += MonthlyInfluenceIncome;

            if (EnergyCredits < 0f)
            {
                EnergyCredits = 0f;
                _bankrupt = true;
                // Казна пуста — распускаем самый дорогой в содержании корабль
                if (MonthlyEnergyIncome < 0f) DisbandCostliestShip();
            }
            else if (_bankrupt && EnergyCredits > 60f) _bankrupt = false;

            MonthlyDiplomacy();
        }

        private void ThinkAndAct()
        {
            if (_generator == null) return;
            ThinkSurvey();
            ThinkClaim();
            ThinkEconomy();
            ThinkCivilianShips();
            ThinkShipbuilding();
            ThinkMilitary();
        }

        private void RecalculateEconomy()
        {
            _report = EmpireEconomy.Compute(AIOwnerId, CapitalSystemId, FactionEnergy, FactionMinerals, FactionAlloys,
                                            Bonuses, _bankrupt);
        }

        // ==================== ФЛОТЫ ====================

        private IEnumerable<FleetView> OwnFleets(FleetType type)
        {
            var fm = FleetManager.Instance;
            if (fm == null) yield break;
            foreach (var f in fm.AllFleets)
            {
                if (f?.Data == null || f.Data.Destroyed || f.Data.OwnerId != AIOwnerId) continue;
                if (f.Data.Type == type) yield return f;
            }
        }

        private List<FleetView> WarShips()
        {
            var list = new List<FleetView>();
            foreach (var f in OwnFleets(FleetType.Military)) list.Add(f);
            return list;
        }

        private int CountOwn(FleetType type)
        {
            int n = 0;
            foreach (var _ in OwnFleets(type)) n++;
            return n;
        }

        private static bool IsIdle(FleetData d)
            => d.State == FleetState.Orbiting && d.Path.Count == 0 && !d.InCombat;

        private int SpawnSystem
        {
            get
            {
                var cap = EmpireStats.GetSystem(CapitalSystemId);
                return cap != null && cap.OwnerId == AIOwnerId ? CapitalSystemId : -1;
            }
        }

        private FleetView CreateCivilian(FleetType type)
        {
            if (FleetManager.Instance == null || SpawnSystem < 0) return null;
            string name = type == FleetType.Science ? $"{AIName} · Разведчик" : $"{AIName} · Строитель";
            return FleetManager.Instance.CreateOwnedFleet(name, SpawnSystem, type, AIOwnerId);
        }

        private FleetView CreateWarship(ShipClass hull)
        {
            if (FleetManager.Instance == null || SpawnSystem < 0) return null;
            var fv = FleetManager.Instance.CreateOwnedFleet($"{AIName} · Крыло {_warFleetSerial++}", SpawnSystem,
                                                             FleetType.Military, AIOwnerId, hull);
            fv.Data.ApplyDesign(GetDesign(hull));
            return fv;
        }

        /// <summary>Проект корабля ИИ под его технологии и любимое оружие.</summary>
        public ShipDesign GetDesign(ShipClass hull)
        {
            if (_designs.TryGetValue(hull, out var d) && d != null) return d;
            var dm = ShipDesignManager.Instance;
            if (dm == null) return null;
            string cls = hull == ShipClass.Destroyer ? "Эсминец" : hull == ShipClass.Frigate ? "Фрегат" : "Корвет";
            d = dm.CreateAutoDesign(hull, Profile.PreferredWeapon, Profile.SecondaryWeapon, HasTech, $"{AIName} · {cls}");
            _designs[hull] = d;
            return d;
        }

        private void DisbandCostliestShip()
        {
            FleetView worst = null;
            foreach (var f in OwnFleets(FleetType.Military))
            {
                if (f.Data.InCombat) continue;
                if (worst == null || f.Data.UpkeepEnergy > worst.Data.UpkeepEnergy) worst = f;
            }
            if (worst != null) FleetManager.Instance?.DisbandFleet(worst);
        }

        // ==================== РАЗВЕДКА ====================

        private void ThinkSurvey()
        {
            var claimed = new HashSet<int>();
            foreach (var f in OwnFleets(FleetType.Science))
                if (f.Data.SurveyTargetSystemId >= 0) claimed.Add(f.Data.SurveyTargetSystemId);

            foreach (var ship in OwnFleets(FleetType.Science))
            {
                var d = ship.Data;
                if (d.SurveyTargetSystemId >= 0)
                {
                    var t = EmpireStats.GetSystem(d.SurveyTargetSystemId);
                    if (t == null || t.SurveyedByAI) { d.SurveyTargetSystemId = -1; if (d.State == FleetState.Surveying) d.State = FleetState.Orbiting; }
                    else continue;
                }
                if (!IsIdle(d)) continue;

                int target = FindBestSurveyTarget(d.CurrentSystemId, claimed);
                if (target < 0) continue;
                claimed.Add(target);
                d.SurveyTargetSystemId = target;
                if (d.CurrentSystemId == target)
                {
                    d.State = FleetState.Surveying;
                    d.DaysRemainingSurvey = d.TotalSurveyDays;
                }
                else FleetManager.Instance?.IssueMoveOrder(ship, target);
            }
        }

        /// <summary>
        /// Куда лететь разведчику: ближайшие неизученные системы, богатые связями (ворота дальше),
        /// и в первую очередь — соседи собственной территории (кандидаты в форпосты).
        /// </summary>
        private int FindBestSurveyTarget(int from, HashSet<int> claimed)
        {
            var dist = Distances(from, 8);
            int best = -1;
            float bestScore = float.MinValue;
            foreach (var kv in dist)
            {
                var sys = EmpireStats.GetSystem(kv.Key);
                if (sys == null || sys.SurveyedByAI || claimed.Contains(sys.Id)) continue;
                if (sys.OwnerId == 0 && AtWar) continue;
                float score = sys.ConnectedSystemIds.Count * 1.5f - kv.Value * 3f;
                if (BordersOwn(sys)) score += 6f;
                if (sys.OwnerId >= 0) score -= 4f;
                if (score > bestScore) { bestScore = score; best = sys.Id; }
            }
            return best;
        }

        // ==================== ЭКСПАНСИЯ ====================

        private void ThinkClaim()
        {
            float influence = FleetManager.OutpostInfluenceCost(AIOwnerId);
            var claimed = new HashSet<int>();
            foreach (var f in OwnFleets(FleetType.Constructor))
                if (f.Data.BuildTargetSystemId >= 0) claimed.Add(f.Data.BuildTargetSystemId);

            foreach (var ship in OwnFleets(FleetType.Constructor))
            {
                var d = ship.Data;
                if (d.BuildTargetSystemId >= 0)
                {
                    var t = EmpireStats.GetSystem(d.BuildTargetSystemId);
                    if (t == null || t.OwnerId >= 0)
                    {
                        // Цель заняли раньше — часть ресурсов возвращается
                        if (t != null && t.OwnerId != AIOwnerId) Alloys += FleetManager.StarbaseAlloysCost * 0.5f;
                        d.BuildTargetSystemId = -1;
                        if (d.State == FleetState.Constructing) d.State = FleetState.Orbiting;
                    }
                    else continue;
                }
                if (!IsIdle(d)) continue;
                if (Alloys < FleetManager.StarbaseAlloysCost || Influence < influence) continue;
                // Не уходим в минус по энергии ради нового форпоста
                if (MonthlyEnergyIncome - EmpireEconomy.OutpostUpkeep < 0f && EnergyCredits < 150f) continue;

                int target = FindBestClaimTarget(claimed);
                if (target < 0) continue;
                claimed.Add(target);

                Alloys -= FleetManager.StarbaseAlloysCost;
                Influence -= influence;
                d.BuildTargetSystemId = target;
                if (d.CurrentSystemId == target)
                {
                    d.State = FleetState.Constructing;
                    d.DaysRemainingConstruction = d.TotalConstructionDays;
                }
                else FleetManager.Instance?.IssueMoveOrder(ship, target);
            }
        }

        private int FindBestClaimTarget(HashSet<int> claimed)
        {
            int best = -1;
            float bestScore = float.MinValue;
            var fromCapital = Distances(CapitalSystemId, 30);
            foreach (var sys in EmpireStats.Systems)
            {
                if (sys.OwnerId != -1 || !sys.SurveyedByAI || claimed.Contains(sys.Id)) continue;
                if (!BordersOwn(sys)) continue;
                if (AtWar && FleetManager.Instance != null && FleetManager.Instance.GetMilitaryPowerInSystem(0, sys.Id) > 0f) continue;
                float score = SystemValue(sys);
                if (fromCapital.TryGetValue(sys.Id, out int jumps)) score -= jumps * 1.2f;
                if (score > bestScore) { bestScore = score; best = sys.Id; }
            }
            return best;
        }

        /// <summary>
        /// Ценность системы для ИИ: залежи, пригодные для жизни миры, звезда, число гиперкоридоров
        /// и соседство с игроком (воинственные тянутся к фронту, остальные — избегают).
        /// </summary>
        public float SystemValue(StarSystem sys)
        {
            if (sys == null) return 0f;
            sys.GeneratePlanets();
            float v = 0f;
            foreach (var p in sys.Planets)
            {
                v += (p.EnergyDeposit * 1.1f + p.MineralDeposit) * Profile.ResourceWeight;
                if (p.CanColonize) v += (6f + p.HabitabilityPercent * 0.12f) * Profile.ColonyWeight;
            }
            v += sys.SpectralClass switch
            {
                StarSpectralClass.ClassG => 2f,
                StarSpectralClass.ClassK => 1f,
                StarSpectralClass.ClassB => 1.5f,
                StarSpectralClass.BlackHole => 3f,
                _ => 0f
            };
            v += Mathf.Max(0, sys.ConnectedSystemIds.Count - 2) * 1.2f;

            bool nearPlayer = false;
            foreach (int id in sys.ConnectedSystemIds)
            {
                var n = EmpireStats.GetSystem(id);
                if (n != null && n.OwnerId == 0) { nearPlayer = true; break; }
            }
            if (nearPlayer) v += Profile.FrontierBias * 2f;
            return v;
        }

        private bool BordersOwn(StarSystem sys)
        {
            foreach (int id in sys.ConnectedSystemIds)
            {
                var n = EmpireStats.GetSystem(id);
                if (n != null && n.OwnerId == AIOwnerId) return true;
            }
            return false;
        }

        /// <summary>Расстояния в прыжках от системы (BFS по гиперкоридорам).</summary>
        private Dictionary<int, int> Distances(int from, int maxDepth)
        {
            var dist = new Dictionary<int, int>();
            var systems = EmpireStats.Systems;
            if (from < 0 || from >= systems.Count) return dist;
            var q = new Queue<int>();
            dist[from] = 0;
            q.Enqueue(from);
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                int dcur = dist[cur];
                if (dcur >= maxDepth) continue;
                foreach (int n in systems[cur].ConnectedSystemIds)
                {
                    if (n < 0 || n >= systems.Count || dist.ContainsKey(n)) continue;
                    dist[n] = dcur + 1;
                    q.Enqueue(n);
                }
            }
            return dist;
        }

        /// <summary>BFS сразу от многих систем: расстояние до ближайшей из них.</summary>
        private Dictionary<int, int> DistancesFromOwner(int owner, int maxDepth)
        {
            var dist = new Dictionary<int, int>();
            var systems = EmpireStats.Systems;
            var q = new Queue<int>();
            foreach (var s in systems)
                if (s.OwnerId == owner) { dist[s.Id] = 0; q.Enqueue(s.Id); }
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                int dcur = dist[cur];
                if (dcur >= maxDepth) continue;
                foreach (int n in systems[cur].ConnectedSystemIds)
                {
                    if (n < 0 || n >= systems.Count || dist.ContainsKey(n)) continue;
                    dist[n] = dcur + 1;
                    q.Enqueue(n);
                }
            }
            return dist;
        }

        // ==================== ЭКОНОМИЧЕСКИЕ РЕШЕНИЯ ====================

        private void ThinkEconomy()
        {
            // За один «ход» — до двух построек, чтобы ИИ не тратил всё разом
            for (int i = 0; i < 2; i++)
                if (!TryEconomyAction()) break;
        }

        private bool TryEconomyAction()
        {
            bool energyLow = MonthlyEnergyIncome < 4f || (_bankrupt && MonthlyEnergyIncome < 10f);
            const float mineralReserve = 40f;

            PlanetData bestColony = null, bestStation = null, bestDistrictPlanet = null;
            float colonyScore = float.MinValue, stationScore = float.MinValue, districtScore = float.MinValue;
            DistrictType bestDistrict = DistrictType.Urban;

            foreach (var sys in EmpireStats.Systems)
            {
                if (sys.OwnerId != AIOwnerId) continue;
                foreach (var p in sys.Planets)
                {
                    // На планете уже что-то строится — ждём (одна стройка за раз, как у игрока)
                    if (Builds != null && Builds.HasAnyJob(p)) continue;
                    if (p.Population <= 0 && p.CanColonize)
                    {
                        float s = p.HabitabilityPercent + p.MaxDistricts;
                        if (s > colonyScore) { colonyScore = s; bestColony = p; }
                    }
                    if (!p.HasMiningStation && (p.EnergyDeposit > 0 || p.MineralDeposit > 0))
                    {
                        float s = p.EnergyDeposit * (energyLow ? 3f : 1.1f) + p.MineralDeposit * Profile.ResourceWeight;
                        if (s > stationScore) { stationScore = s; bestStation = p; }
                    }
                    if (p.Population > 0 && p.BuiltDistricts + p.PendingDistricts < p.MaxDistricts)
                    {
                        if (!NeedsDistrict(p, energyLow, out var type, out float urgency)) continue;
                        if (urgency > districtScore) { districtScore = urgency; bestDistrict = type; bestDistrictPlanet = p; }
                    }
                }
            }

            // 1. Энергетический кризис — сначала генераторы и станции на гелии
            if (energyLow)
            {
                if (bestDistrictPlanet != null && bestDistrict == DistrictType.Generator && TryBuildDistrict(bestDistrictPlanet, bestDistrict)) return true;
                if (bestStation != null && bestStation.EnergyDeposit > 0 && TryBuildStation(bestStation)) return true;
            }

            // 2. Колония — главный источник населения (наука, рабочие руки, флотский лимит)
            if (bestColony != null && Minerals >= PlanetData.ColonyMineralsCost + mineralReserve
                && Alloys >= PlanetData.ColonyAlloysCost && Influence >= PlanetData.ColonyInfluenceCost)
            {
                Minerals -= PlanetData.ColonyMineralsCost;
                Alloys -= PlanetData.ColonyAlloysCost;
                Influence -= PlanetData.ColonyInfluenceCost;
                if (Builds != null)
                    Builds.EnqueuePlanetJob(JobKind.Colony, AIOwnerId, bestColony, DistrictType.Urban, 0f,
                        PlanetData.ColonyMineralsCost, PlanetData.ColonyAlloysCost, PlanetData.ColonyInfluenceCost);
                else bestColony.SettleColony();
                return true;
            }

            // 3. Районы на колониях
            if (bestDistrictPlanet != null && TryBuildDistrict(bestDistrictPlanet, bestDistrict)) return true;

            // 4. Добывающие станции
            if (bestStation != null && Minerals >= FleetManager.MiningStationMinerals + mineralReserve && TryBuildStation(bestStation)) return true;

            return false;
        }

        /// <summary>Какой район нужен колонии: жильё, если тесно; иначе — то, чего не хватает империи.</summary>
        private bool NeedsDistrict(PlanetData p, bool energyLow, out DistrictType type, out float urgency)
        {
            type = DistrictType.Urban;
            urgency = 0f;
            int housing = p.HousingCapacity;
            int idle = p.IdlePops;

            if (p.Population >= housing) { type = DistrictType.Urban; urgency = 6f; }
            else if (idle > 0)
            {
                urgency = 4f + idle;
                if (energyLow) type = DistrictType.Generator;
                else if (MonthlyMineralsIncome < 15f) type = DistrictType.Mining;
                else if (MonthlyAlloysIncome < 10f) type = DistrictType.Industrial;
                else type = Profile.PreferredDistrict == DistrictType.Urban ? DistrictType.Generator : Profile.PreferredDistrict;
            }
            else return false;
            return true;
        }

        private bool TryBuildDistrict(PlanetData p, DistrictType t)
        {
            float m = DistrictInfo.MineralsCost(t), a = DistrictInfo.AlloysCost(t);
            if (Minerals < m || Alloys < a || p.BuiltDistricts >= p.MaxDistricts) return false;
            Minerals -= m;
            Alloys -= a;
            if (Builds != null) Builds.EnqueuePlanetJob(JobKind.District, AIOwnerId, p, t, 0f, m, a, 0f);
            else p.Districts.Add(new DistrictData(t));
            return true;
        }

        private static ConstructionManager Builds => ConstructionManager.Instance;

        /// <summary>Корабль ИИ заложен на верфи (по тем же срокам, что у игрока).</summary>
        private void QueueShip(FleetType type, ShipClass hull, float energy, float alloys)
        {
            if (Builds == null)
            {
                if (type == FleetType.Military) CreateWarship(hull); else CreateCivilian(type);
                return;
            }
            Builds.EnqueueShip(AIOwnerId, type, hull, null, energy, alloys);
        }

        /// <summary>Корабль готов — появляется у столицы.</summary>
        public void LaunchShip(ConstructionJob job)
        {
            if (job.ShipType == FleetType.Military) CreateWarship(job.Hull);
            else CreateCivilian(job.ShipType);
            RecalculateEconomy();
        }

        private int QueuedShips(FleetType type)
        {
            if (Builds == null) return 0;
            int n = 0;
            foreach (var j in Builds.ShipQueue(AIOwnerId)) if (j.ShipType == type) n++;
            return n;
        }

        private bool TryBuildStation(PlanetData p)
        {
            if (Minerals < FleetManager.MiningStationMinerals) return false;
            Minerals -= FleetManager.MiningStationMinerals;
            if (Builds != null)
                Builds.EnqueuePlanetJob(JobKind.MiningStation, AIOwnerId, p, DistrictType.Mining, 0f, FleetManager.MiningStationMinerals, 0f, 0f);
            else
            {
                p.HasMiningStation = true;
                p.ParentSystem?.RecalculateHarvest();
            }
            return true;
        }

        /// <summary>Разведчики и строители: научная фракция держит двух разведчиков, всем нужен второй строитель при росте влияния.</summary>
        private void ThinkCivilianShips()
        {
            int science = CountOwn(FleetType.Science) + QueuedShips(FleetType.Science);
            int builders = CountOwn(FleetType.Constructor) + QueuedShips(FleetType.Constructor);
            int wantScience = Personality == AIPersonality.Scientific ? 2 : 1;
            int wantBuilders = Influence > 90f ? 2 : 1;

            float sciCost = FleetManager.ShipAlloyCost(FleetManager.ScienceShipAlloys, AIOwnerId);
            float conCost = FleetManager.ShipAlloyCost(FleetManager.ConstructorAlloys, AIOwnerId);

            if (science < wantScience && HasUnsurveyedNearby() && Alloys >= sciCost + 30f && EnergyCredits >= FleetManager.ScienceShipEnergy
                && MonthlyEnergyIncome > 2f)
            {
                Alloys -= sciCost;
                EnergyCredits -= FleetManager.ScienceShipEnergy;
                QueueShip(FleetType.Science, ShipClass.Corvette, FleetManager.ScienceShipEnergy, sciCost);
            }
            else if (builders < wantBuilders && Alloys >= conCost + 30f && EnergyCredits >= FleetManager.ConstructorEnergy
                     && MonthlyEnergyIncome > 2f)
            {
                Alloys -= conCost;
                EnergyCredits -= FleetManager.ConstructorEnergy;
                QueueShip(FleetType.Constructor, ShipClass.Corvette, FleetManager.ConstructorEnergy, conCost);
            }
        }

        private bool HasUnsurveyedNearby()
        {
            if (CapitalSystemId < 0) return false;
            foreach (var kv in Distances(CapitalSystemId, 8))
            {
                var s = EmpireStats.GetSystem(kv.Key);
                if (s != null && !s.SurveyedByAI) return true;
            }
            return false;
        }

        // ==================== ИССЛЕДОВАНИЯ ====================

        private void TickResearch(int year)
        {
            if (CurrentTech == null) PickNextTech();
            if (CurrentTech == null) return;

            float gain = MonthlyScience * LeaderManager.ResearchMult(AIOwnerId) * ResearchThroughput / 30f
                       / TechnologyManager.YearPenaltyMultiplier(CurrentTech, year);
            TechProgress += gain;
            if (TechProgress < CurrentTech.Cost) return;

            var done = CurrentTech;
            _researched.Add(done.Id);
            CurrentTech = null;
            TechProgress = 0f;

            var before = Bonuses.Clone();
            if (TechEffects.Apply(Bonuses, done.BonusKey)) _researchSlots = Mathf.Min(6, _researchSlots + 1);
            if (TechEffects.AffectsDurability(done.BonusKey)) FleetManager.Instance?.RescaleDurability(AIOwnerId, before, Bonuses);
            _designs.Clear();   // новые модули и корпуса

            WarnAboutScience();
        }

        private void WarnAboutScience()
        {
            var vm = VictoryManager.Instance;
            if (vm == null) return;
            int left = vm.ScienceRequired - ResearchedCount;
            int level = left <= 2 ? 2 : left <= 5 ? 1 : 0;
            if (level > _scienceWarnLevel)
                NotificationCenter.Show("Научная гонка", $"{AIName}: до научной победы осталось {Mathf.Max(0, left)} техн.",
                    NotificationCenter.Kind.Warning, 7f);
            _scienceWarnLevel = level;
        }

        /// <summary>Выбор технологии: вес категории по характеру, делённый на трудоёмкость.</summary>
        private void PickNextTech()
        {
            int year = TimeManager.Instance != null ? TimeManager.Instance.Year : 2200;
            bool threatened = AtWar || EmpireStats.MilitaryPower(0) > EmpireStats.MilitaryPower(AIOwnerId) * 1.2f;
            Technology best = null;
            float bestScore = float.MinValue;
            foreach (var t in TechTree)
            {
                if (_researched.Contains(t.Id)) continue;
                bool ok = true;
                foreach (var req in t.RequiredTechIds) if (!_researched.Contains(req)) { ok = false; break; }
                if (!ok) continue;

                float w = Profile.TechWeight(t.Category);
                if (threatened && (t.Category == TechCategory.Weapons || t.Category == TechCategory.Defense)) w *= 1.6f;
                if (t.BonusKey == "slot+1") w *= Personality == AIPersonality.Scientific ? 1.6f : 1.2f;
                float score = w * 1000f / (t.Cost * TechnologyManager.YearPenaltyMultiplier(t, year)) * Random.Range(0.85f, 1.15f);
                if (score > bestScore) { bestScore = score; best = t; }
            }
            CurrentTech = best;
            TechProgress = 0f;
        }

        // ==================== СОБЫТИЯ ====================

        private void HandleSystemCaptured(StarSystem sys, int oldOwner, int newOwner)
        {
            if (oldOwner == AIOwnerId)
            {
                if (AtWar) { _systemsLostInWar++; WarWeariness += 8f * Profile.WearinessRate; }
                if (sys.Id == CapitalSystemId) RelocateCapital();
            }
            else if (newOwner == AIOwnerId && AtWar) _systemsTakenInWar++;
            RaiseDiplomacyChanged();
        }

        private void HandleShipDestroyed(FleetData ship, int killer)
        {
            if (!AtWar || ship == null) return;
            if (ship.OwnerId == AIOwnerId) { _shipsLostInWar++; WarWeariness += 2f * Profile.WearinessRate; }
            else if (ship.OwnerId == 0) { _playerShipsLostInWar++; WarWeariness = Mathf.Max(0f, WarWeariness - 0.5f); }
        }

        /// <summary>Столица пала — правительство переезжает в самую населённую систему.</summary>
        private void RelocateCapital()
        {
            int best = -1, bestPop = -1;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != AIOwnerId) continue;
                int pop = 0;
                foreach (var p in s.Planets) pop += Mathf.Max(0, p.Population);
                if (pop > bestPop) { bestPop = pop; best = s.Id; }
            }
            if (best < 0) return;
            CapitalSystemId = best;
            _capitalLost = true;
            NotificationCenter.Show("Столица врага пала", $"{AIName} переносит столицу в {EmpireStats.GetSystem(best)?.Name}",
                NotificationCenter.Kind.Success, 7f);
        }

        // ==================== СОХРАНЕНИЕ ====================

        public AISave CaptureState()
        {
            var s = new AISave
            {
                Capital = CapitalSystemId, Name = AIName, Title = AITitle, Color = AIEmpireColor,
                Personality = (int)Personality, Eliminated = IsEliminated,
                Energy = EnergyCredits, Minerals = Minerals, Alloys = Alloys, Influence = Influence,
                BaseEnergy = BaseEnergyIncome, BaseMinerals = BaseMineralsIncome,
                BaseAlloys = BaseAlloysIncome, BaseInfluence = BaseInfluenceIncome,
                Bankrupt = _bankrupt,
                Relations = Opinion, LastRelationBucket = _lastOpinionBucket,
                WarFleetSerial = _warFleetSerial,
                CurrentTech = CurrentTech?.Id, TechProgress = TechProgress, ResearchSlots = _researchSlots,
                ScienceWarnLevel = _scienceWarnLevel
            };
            s.Researched.AddRange(_researched);
            CaptureDiplomacy(s);
            CaptureMilitary(s);
            return s;
        }

        /// <summary>Восстановить империю ИИ; флоты уже созданы FleetManager из сохранения.</summary>
        public void RestoreState(AISave s, int version)
        {
            if (s == null) return;
            if (_generator == null) _generator = FindFirstObjectByType<GalaxyGenerator>();
            CapitalSystemId = s.Capital;

            FactionInfo faction = null;
            foreach (var f in FactionRegistry.AvailableFactions) if (f.Name == s.Name) faction = f;
            if (faction != null) SetFaction(faction);
            else if (!string.IsNullOrEmpty(s.Name)) AIName = s.Name;
            AITitle = s.Title ?? AITitle;
            AIEmpireColor = s.Color;
            IsEliminated = s.Eliminated;

            EnergyCredits = s.Energy; Minerals = s.Minerals; Alloys = s.Alloys; Influence = s.Influence;
            _warFleetSerial = Mathf.Max(1, s.WarFleetSerial);

            Bonuses = new EmpireBonuses();
            _researched.Clear();
            _researchSlots = 3;
            CurrentTech = null;
            TechProgress = 0f;

            if (version >= 2)
            {
                BaseEnergyIncome = s.BaseEnergy; BaseMineralsIncome = s.BaseMinerals;
                BaseAlloysIncome = s.BaseAlloys; BaseInfluenceIncome = s.BaseInfluence;
                _bankrupt = s.Bankrupt;
                foreach (var id in s.Researched)
                {
                    var t = TechTree.Find(x => x.Id == id);
                    if (t == null) continue;
                    _researched.Add(id);
                    if (TechEffects.Apply(Bonuses, t.BonusKey)) _researchSlots++;
                }
                _researchSlots = Mathf.Max(s.ResearchSlots, Mathf.Min(6, _researchSlots));
                if (!string.IsNullOrEmpty(s.CurrentTech))
                {
                    CurrentTech = TechTree.Find(x => x.Id == s.CurrentTech);
                    TechProgress = s.TechProgress;
                }
                _scienceWarnLevel = s.ScienceWarnLevel;
            }
            RestoreDiplomacy(s, version);
            RestoreMilitary(s);
            _designs.Clear();
            RecalculateEconomy();
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
