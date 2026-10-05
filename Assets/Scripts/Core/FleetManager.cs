using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using StellarisClone.Generation;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    public class FleetManager : MonoBehaviour
    {
        public static FleetManager Instance { get; private set; }
        public static event Action<FleetView> OnFleetSelected;
        public static event Action OnStarbaseBuilt;
        public static event Action<StarSystem> OnSystemSurveyCompleted;

        private GalaxyGenerator _generator;
        public FleetView SelectedFleet { get; private set; }
        private readonly List<FleetView> _allFleets = new List<FleetView>();
        public IReadOnlyList<FleetView> AllFleets => _allFleets;

        private int _fleetIdCounter = 1;

        public const float StarbaseAlloysCost = 50f;
        public const float StarbaseInfluenceCost = 25f;

        public const float ScienceShipAlloys = 100f, ScienceShipEnergy = 50f;
        public const float ConstructorAlloys = 80f, ConstructorEnergy = 20f;
        public const float WarshipEnergy = 10f;

        /// <summary>Влияние на форпост с учётом технологий владельца.</summary>
        public static float OutpostInfluenceCost(int owner)
            => Mathf.Max(5f, StarbaseInfluenceCost - EmpireBonuses.For(owner).OutpostInfluenceDiscount);

        /// <summary>Цена корабля в сплавах с учётом технологий владельца.</summary>
        public static float ShipAlloyCost(float baseCost, int owner)
            => Mathf.Round(baseCost * Mathf.Max(0.3f, EmpireBonuses.For(owner).ShipCostMult));

        [Header("События и аномалии")]
        [Range(0f, 1f)] [SerializeField] private float surveyAnomalyChance = 0.7f;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(this); return; }
        }

        private void Start()
        {
            _generator = FindFirstObjectByType<GalaxyGenerator>();
            if (_generator == null)
            {
                Debug.LogError("[FleetManager] GalaxyGenerator не найден!");
                return;
            }

            if (_generator.Systems.Count == 0)
                _generator.GenerateGalaxy();

            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed += HandleDayPassed;

            // При загрузке флоты и экономику восстанавливает SaveLoader
            if (GameSession.IsLoading) return;

            SetupStartingEconomyAndFleets();
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed -= HandleDayPassed;
        }

        // ==================== РЕМОНТ ====================

        /// <summary>Щиты восстанавливаются вне боя везде.</summary>
        public const float ShieldRegenPerDay = 0.20f;
        /// <summary>Корпус и броня чинятся только на своей территории.</summary>
        public const float RepairPerDayOwn = 0.05f;
        /// <summary>У колонии (верфи, доки) — вдвое быстрее.</summary>
        public const float RepairPerDayColony = 0.10f;

        private void HandleDayPassed(int day, int month, int year)
        {
            if (_generator == null) return;
            foreach (var f in _allFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.InCombat) continue;

                d.ShieldPoints = Mathf.Min(d.MaxShieldPoints, d.ShieldPoints + d.MaxShieldPoints * ShieldRegenPerDay);

                if (d.State == FleetState.InHyperlane) continue;
                float rate = RepairRateAt(d.CurrentSystemId, d.OwnerId);
                if (rate <= 0f) continue;
                d.HullPoints = Mathf.Min(d.MaxHullPoints, d.HullPoints + d.MaxHullPoints * rate);
                d.ArmorPoints = Mathf.Min(d.MaxArmorPoints, d.ArmorPoints + d.MaxArmorPoints * rate);
            }
        }

        /// <summary>Скорость ремонта (доля в день) для корабля владельца в системе.</summary>
        public float RepairRateAt(int systemId, int ownerId)
        {
            if (_generator == null || systemId < 0 || systemId >= _generator.Systems.Count) return 0f;
            var sys = _generator.Systems[systemId];
            if (sys.OwnerId != ownerId) return 0f;
            foreach (var p in sys.Planets) if (p.Population > 0) return RepairPerDayColony;
            return RepairPerDayOwn;
        }

        public bool NeedsRepair(FleetData d)
            => d != null && (d.HullPoints < d.MaxHullPoints - 0.5f || d.ArmorPoints < d.MaxArmorPoints - 0.5f);

        /// <summary>Пересчитать прочность флота владельца после технологии брони/щитов.</summary>
        public void RescaleDurability(int owner, EmpireBonuses before, EmpireBonuses after)
        {
            foreach (var f in _allFleets)
                if (f?.Data != null && !f.Data.Destroyed && f.Data.OwnerId == owner)
                    CombatMath.RescaleDurability(f.Data, before, after);
        }

        public int NextFleetId => _fleetIdCounter;
        public void SetNextFleetId(int id) { if (id > _fleetIdCounter) _fleetIdCounter = id; }

        /// <summary>Флот из сохранения.</summary>
        public FleetView RestoreFleet(FleetSave save)
        {
            if (save == null || _generator == null) return null;
            if (save.Current < 0 || save.Current >= _generator.Systems.Count) return null;
            var view = CreateOwnedFleet(save.Name, save.Current, (FleetType)save.Type, save.Owner, (ShipClass)save.Hull);
            SaveSystem.ApplyFleet(view.Data, save);
            view.UpdatePathVisuals();
            if (save.Id >= _fleetIdCounter) _fleetIdCounter = save.Id + 1;
            return view;
        }

        private void SetupStartingEconomyAndFleets()
        {
            if (_generator.Systems.Count == 0) return;

            var eco = EconomyManager.Instance;
            if (eco != null)
            {
                float k = GameSession.Settings.PlayerStart;
                eco.EnergyCredits = 220f * k;
                eco.Minerals = 280f * k;
                eco.Alloys = 160f * k;
                eco.Influence = 60f * k;
                eco.RaiseResourcesChanged();
            }

            var capital = _generator.Systems[0];
            capital.OwnerId = 0;
            capital.HasStarbase = true;
            capital.IsSurveyed = true;
            capital.GeneratePlanets();

            foreach (var p in capital.Planets)
            {
                if (p.CanColonize)
                {
                    p.SetupAsStartingColony(pop: 6, urban: 3, mining: 1, generator: 1, industrial: 1);
                    break;
                }
            }

            CreateFleetObject("1-й Корветный Флот", 0, FleetType.Military);
            CreateFleetObject("НИС «Первопроходец»", 0, FleetType.Science);
            CreateFleetObject("СК «Фундамент»", 0, FleetType.Constructor);

            GalaxyView.Instance?.RefreshTerritoryVisuals();
        }

        public FleetView CreateFleetObject(string fleetName, int startSystemId, FleetType type)
        {
            GameObject fleetObj = new GameObject($"Fleet_{_fleetIdCounter}_{fleetName}");
            fleetObj.transform.SetParent(transform);

            var data = new FleetData(_fleetIdCounter++, fleetName, startSystemId, type);
            data.OwnerId = 0;
            data.ApplyDefaultCombatStats(type);
            var view = fleetObj.AddComponent<FleetView>();
            view.Initialize(data, _generator);
            _allFleets.Add(view);
            return view;
        }

        public FleetView CreateOwnedFleet(string fleetName, int startSystemId, FleetType type, int ownerId, ShipClass hull = ShipClass.Corvette)
        {
            GameObject fleetObj = new GameObject($"Fleet_{_fleetIdCounter}_{fleetName}");
            fleetObj.transform.SetParent(transform);

            var data = new FleetData(_fleetIdCounter++, fleetName, startSystemId, type);
            data.OwnerId = ownerId;
            data.HullClass = hull;
            data.ApplyDefaultCombatStats(type);
            var view = fleetObj.AddComponent<FleetView>();
            view.Initialize(data, _generator);
            _allFleets.Add(view);
            return view;
        }

        public void RegisterExternalFleet(FleetView view)
        {
            if (view != null && !_allFleets.Contains(view))
                _allFleets.Add(view);
        }

        public void NotifyFleetDestroyed(FleetView view)
        {
            if (view == null) return;
            _allFleets.Remove(view);
            if (SelectedFleet == view)
                SelectFleet(null);
        }

        /// <summary>Распустить корабль (банкротство, сокращение флота).</summary>
        public void DisbandFleet(FleetView view)
        {
            if (view?.Data == null) return;
            view.Data.Destroyed = true;
            NotifyFleetDestroyed(view);
            Destroy(view.gameObject);
        }

        /// <summary>Боевая мощь владельца с учётом технологий и текущих повреждений.</summary>
        public int GetMilitaryPower(int ownerId)
        {
            float sum = 0f;
            foreach (var f in _allFleets)
            {
                if (f?.Data == null || f.Data.Destroyed) continue;
                if (f.Data.OwnerId == ownerId && f.Data.Type == FleetType.Military)
                    sum += CombatMath.Power(f.Data);
            }
            return Mathf.RoundToInt(sum);
        }

        /// <summary>Боевая мощь владельца в конкретной системе (на орбите, не в пути).</summary>
        public float GetMilitaryPowerInSystem(int ownerId, int systemId)
        {
            float sum = 0f;
            foreach (var f in _allFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.OwnerId != ownerId || d.Type != FleetType.Military) continue;
                if (d.State == FleetState.InHyperlane || d.CurrentSystemId != systemId) continue;
                sum += CombatMath.Power(d);
            }
            return sum;
        }

        public List<FleetView> GetFleetsInSystem(int systemId)
        {
            var list = new List<FleetView>();
            foreach (var f in _allFleets)
            {
                if (f?.Data == null || f.Data.Destroyed) continue;
                if (f.Data.CurrentSystemId == systemId && f.Data.State != FleetState.InHyperlane)
                    list.Add(f);
            }
            return list;
        }

        public bool TryRetrofitSelectedFleet()
        {
            var fleet = SelectedFleet;
            if (fleet?.Data == null || fleet.Data.OwnerId != 0) return false;
            if (ShipDesignManager.Instance == null) return false;
            return ShipDesignManager.Instance.TryRetrofit(fleet.Data);
        }

        /// <summary>Где строятся корабли игрока: столица, а если она потеряна — самая населённая своя система.</summary>
        public int PlayerShipyardSystem()
        {
            if (_generator == null) return -1;
            var cap = _generator.Systems.Count > EconomyManager.PlayerCapitalId ? _generator.Systems[EconomyManager.PlayerCapitalId] : null;
            if (cap != null && cap.OwnerId == 0) return cap.Id;
            int best = -1, bestPop = -1;
            foreach (var s in _generator.Systems)
            {
                if (s.OwnerId != 0) continue;
                int pop = 0;
                foreach (var p in s.Planets) pop += Mathf.Max(0, p.Population);
                if (pop > bestPop) { bestPop = pop; best = s.Id; }
            }
            return best;
        }

        public bool BuildShip(FleetType type)
        {
            var eco = EconomyManager.Instance;
            if (eco == null) return false;

            float costAlloys = 0f;
            float costEnergy = 0f;
            string shipName = "";

            switch (type)
            {
                case FleetType.Science:
                    costAlloys = ScienceShipAlloys; costEnergy = ScienceShipEnergy;
                    shipName = $"НИС «Академик {AllFleets.Count + 1}»";
                    break;
                case FleetType.Constructor:
                    costAlloys = ConstructorAlloys; costEnergy = ConstructorEnergy;
                    shipName = $"Строитель {AllFleets.Count + 1}";
                    break;
                case FleetType.Military:
                    costAlloys = 60f; costEnergy = WarshipEnergy;
                    shipName = $"{AllFleets.Count + 1}-й Корвет";
                    break;
            }

            if (type == FleetType.Military && ShipDesignManager.Instance != null)
            {
                var design = ShipDesignManager.Instance.GetLatestDesign(ShipClass.Corvette);
                if (design != null) costAlloys = design.AlloyCost;
            }
            costAlloys = ShipAlloyCost(costAlloys, 0);

            if (!eco.CanAfford(costEnergy, 0f, costAlloys, 0f))
            {
                NotificationCenter.Show("Недостаточно ресурсов",
                    $"Нужно {costAlloys} сплавов и {costEnergy} гелия-3",
                    NotificationCenter.Kind.Warning, 4f);
                return false;
            }

            int yard = PlayerShipyardSystem();
            if (yard < 0) { NotificationCenter.Show("Нет верфи", "У вас не осталось своих систем", NotificationCenter.Kind.Danger, 4f); return false; }
            eco.TrySpend(costEnergy, 0f, costAlloys, 0f);
            var created = CreateFleetObject(shipName, yard, type);
            if (type == FleetType.Military)
            {
                var design = ShipDesignManager.Instance?.GetLatestDesign(ShipClass.Corvette);
                if (design != null) created.Data.ApplyDesign(design);
            }

            NotificationCenter.Show("Корабль построен", shipName, NotificationCenter.Kind.Success, 4f);
            eco.RecalculateAll();
            return true;
        }

        /// <summary>Проект игрока для корпуса: последний сохранённый или автоматический под изученные технологии.</summary>
        public ShipDesign PlayerDesignFor(ShipClass cls)
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null) return null;
            var d = dm.GetLatestDesign(cls);
            if (d != null) return d;
            var tm = TechnologyManager.Instance;
            string name = cls == ShipClass.Destroyer ? "Эсминец «Типовой»" : cls == ShipClass.Frigate ? "Фрегат «Типовой»" : "Корвет «Типовой»";
            return dm.CreateAutoDesign(cls, WeaponDamageType.Kinetic, WeaponDamageType.Energy,
                id => tm != null && tm.FindTech(id) != null && tm.FindTech(id).IsResearched, name);
        }

        /// <summary>Построить боевой корабль заданного класса (эсминцы — только после технологии).</summary>
        public bool BuildWarship(ShipClass cls)
        {
            if (cls == ShipClass.Destroyer && !(TechnologyManager.Instance?.DestroyerUnlocked ?? false))
            {
                NotificationCenter.Show("Эсминцы недоступны", "Изучите «Верфи класса „Эсминец“»", NotificationCenter.Kind.Warning, 4f);
                return false;
            }
            var design = PlayerDesignFor(cls);
            if (design == null) return false;
            if (!BuildShipFromDesign(design))
            {
                NotificationCenter.Show("Недостаточно ресурсов",
                    $"Нужно {ShipAlloyCost(design.AlloyCost, 0):0} сплавов и {WarshipEnergy:0} гелия-3", NotificationCenter.Kind.Warning, 4f);
                return false;
            }
            return true;
        }

        public bool BuildShipFromDesign(ShipDesign design)
        {
            var eco = EconomyManager.Instance;
            if (eco == null || design == null || !design.IsPowerValid) return false;
            int yard = PlayerShipyardSystem();
            if (yard < 0) return false;
            float cost = ShipAlloyCost(design.AlloyCost, 0);
            if (!eco.CanAfford(WarshipEnergy, 0f, cost, 0f)) return false;
            eco.TrySpend(WarshipEnergy, 0f, cost, 0f);
            string cls = design.HullClass switch
            {
                ShipClass.Frigate => "Фрегат",
                ShipClass.Destroyer => "Эсминец",
                _ => "Корвет"
            };
            var created = CreateFleetObject($"{AllFleets.Count + 1}-й {cls}", yard, FleetType.Military);
            created.Data.ApplyDesign(design);
            NotificationCenter.Show("Корабль построен", created.Data.Name, NotificationCenter.Kind.Success, 4f);
            EconomyManager.Instance?.RecalculateAll();
            return true;
        }

        private void Update()
        {
            if (!UIManager.IsGameStarted) return;
            HandleSelectionAndOrders();
        }

        private void HandleSelectionAndOrders()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (Camera.main == null) return;

            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);

                FleetView foundFleet = null;
                float bestDist = float.MaxValue;
                foreach (var h in hits)
                {
                    var fv = h.collider.GetComponentInParent<FleetView>();
                    if (fv != null && h.distance < bestDist) { foundFleet = fv; bestDist = h.distance; }
                }
                if (foundFleet != null)
                {
                    if (foundFleet.Data != null && foundFleet.Data.OwnerId != 0) return;
                    SelectFleet(foundFleet);
                    return;
                }
            }

            if (Input.GetMouseButtonDown(1) && SelectedFleet != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);

                StarSystem targetSystem = null;
                float bestDist = float.MaxValue;

                foreach (var h in hits)
                {
                    if (h.collider.GetComponentInParent<FleetView>() != null) continue;
                    var selector = h.collider.GetComponentInParent<StarSystemSelector>();
                    if (selector?.Data != null && h.distance < bestDist)
                    {
                        targetSystem = selector.Data;
                        bestDist = h.distance;
                    }
                }

                if (targetSystem == null) return;

                var fleet = SelectedFleet;

                if (fleet.Data.Type == FleetType.Science
                    && !targetSystem.IsSurveyed
                    && targetSystem.OwnerId == -1)
                {
                    if (!OrderSurveySystem(targetSystem.Id, fleet))
                        IssueMoveOrder(fleet, targetSystem.Id);
                    return;
                }

                if (fleet.Data.Type == FleetType.Constructor
                    && targetSystem.OwnerId == -1
                    && targetSystem.IsSurveyed)
                {
                    if (!OrderBuildStarbase(targetSystem.Id, fleet))
                        IssueMoveOrder(fleet, targetSystem.Id);
                    return;
                }

                IssueMoveOrder(fleet, targetSystem.Id);
            }
        }

        public void SelectFleet(FleetView fleet)
        {
            if (fleet == null)
            {
                if (SelectedFleet == null) return;
                SelectedFleet.SetSelected(false);
                SelectedFleet = null;
                SFXManager.Play("ui_deselect", 0.7f, 1f);
                OnFleetSelected?.Invoke(null);
                return;
            }

            if (SelectedFleet == fleet) return;

            if (SelectedFleet != null) SelectedFleet.SetSelected(false);
            SelectedFleet = fleet;
            SelectedFleet.SetSelected(true);
            SelectedFleet.UpdatePathVisuals();

            PlayFleetSelectSFX(fleet.Data.Type);
            OnFleetSelected?.Invoke(SelectedFleet);
        }

        private void PlayFleetSelectSFX(FleetType type)
        {
            string baseName = type switch
            {
                FleetType.Military    => "unit_select_military",
                FleetType.Science     => "unit_select_science",
                FleetType.Constructor => "unit_select_constructor",
                _                     => "unit_select"
            };

            string[] candidates = { $"{baseName}_1", $"{baseName}_2", $"{baseName}_3" };
            SFXManager.PlayRandom(candidates, 1f);

            if (!SFXManager.Has(baseName)) return;
            SFXManager.Play(baseName, 1f, type == FleetType.Constructor ? 1.0f : 1.05f);
        }

        public void IssueMoveOrder(FleetView fleet, int targetSystemId)
        {
            if (_generator == null || fleet?.Data == null) return;
            if (fleet.Data.InCombat) return;

            int startNode = fleet.Data.State == FleetState.InHyperlane
                ? fleet.Data.TargetSystemId
                : fleet.Data.CurrentSystemId;

            if (startNode == targetSystemId) return;

            var route = GalaxyPathfinder.FindPath(startNode, targetSystemId, _generator);
            if (route == null || route.Count == 0) return;

            fleet.Data.Path.Clear();
            foreach (int step in route) fleet.Data.Path.Enqueue(step);
            fleet.UpdatePathVisuals();
        }

        public bool OrderSurveySystem(int targetSystemId) => OrderSurveySystem(targetSystemId, null);

        public bool OrderSurveySystem(int targetSystemId, FleetView preferredShip)
        {
            if (_generator == null || targetSystemId < 0 || targetSystemId >= _generator.Systems.Count) return false;
            var system = _generator.Systems[targetSystemId];
            if (system.IsSurveyed) return false;

            FleetView scienceShip = null;

            if (preferredShip != null
                && preferredShip.Data != null
                && preferredShip.Data.OwnerId == 0
                && preferredShip.Data.Type == FleetType.Science)
            {
                scienceShip = preferredShip;
            }
            else
            {
                scienceShip = _allFleets.Find(f =>
                    f.Data != null
                    && f.Data.Type == FleetType.Science
                    && f.Data.OwnerId == 0
                    && f.Data.SurveyTargetSystemId == -1
                    && f.Data.State == FleetState.Orbiting);
            }

            if (scienceShip == null) return false;

            scienceShip.Data.SurveyTargetSystemId = targetSystemId;

            if (scienceShip.Data.CurrentSystemId == targetSystemId
                && scienceShip.Data.State == FleetState.Orbiting)
            {
                scienceShip.Data.State = FleetState.Surveying;
                scienceShip.Data.DaysRemainingSurvey = scienceShip.Data.TotalSurveyDays;
            }
            else
            {
                IssueMoveOrder(scienceShip, targetSystemId);
            }
            return true;
        }

        public void CompleteSystemSurvey(int systemId) => CompleteSystemSurvey(systemId, 0);

        public void CompleteSystemSurvey(int systemId, int ownerId)
        {
            if (systemId < 0 || systemId >= _generator.Systems.Count) return;
            var sys = _generator.Systems[systemId];
            sys.IsSurveyed = true;

            if (ownerId == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco != null)
                {
                    eco.Minerals += 75f;
                    eco.Alloys += 40f;
                    eco.Influence += 15f;
                    eco.EnergyCredits += 25f;
                    eco.RaiseResourcesChanged();
                }
                NotificationCenter.Show("Разведка завершена", sys.Name, NotificationCenter.Kind.Success, 4f);
            }
            else if (ownerId == AIEmpireManager.AIOwnerId && AIEmpireManager.Instance != null)
            {
                // ИИ получает за разведку те же трофеи, что и игрок
                var ai = AIEmpireManager.Instance;
                ai.AddStock("minerals", 75f);
                ai.AddStock("alloys", 40f);
                ai.AddStock("influence", 15f);
                ai.AddStock("energy", 25f);
            }

            GalaxyView.Instance?.RefreshTerritoryVisuals();
            OnSystemSurveyCompleted?.Invoke(sys);

            if (ownerId == 0)
            {
                if (AnomalyEventSystem.Instance != null)
                {
                    if (UnityEngine.Random.value < surveyAnomalyChance)
                        AnomalyEventSystem.Instance.TriggerEventForSurvey(sys);
                }
            }
        }

        public bool OrderBuildStarbase(int targetSystemId) => OrderBuildStarbase(targetSystemId, null);

        public bool OrderBuildStarbase(int targetSystemId, FleetView preferredBuilder)
        {
            if (_generator == null || targetSystemId < 0 || targetSystemId >= _generator.Systems.Count) return false;
            var system = _generator.Systems[targetSystemId];

            if (system.OwnerId != -1) return false;
            if (!system.IsSurveyed) return false;

            var eco = EconomyManager.Instance;
            float influence = OutpostInfluenceCost(0);
            if (eco == null || !eco.CanAfford(0f, 0f, StarbaseAlloysCost, influence))
            {
                NotificationCenter.Show("Недостаточно ресурсов",
                    $"Форпост: {StarbaseAlloysCost} сплавов + {influence} влияния",
                    NotificationCenter.Kind.Warning, 4f);
                return false;
            }

            FleetView builder = preferredBuilder;
            if (builder == null
                || builder.Data == null
                || builder.Data.Type != FleetType.Constructor
                || builder.Data.OwnerId != 0)
            {
                builder = _allFleets.Find(f =>
                    f.Data != null
                    && f.Data.Type == FleetType.Constructor
                    && f.Data.OwnerId == 0
                    && f.Data.BuildTargetSystemId == -1
                    && f.Data.State == FleetState.Orbiting);
            }

            if (builder == null) return false;

            eco.TrySpend(0f, 0f, StarbaseAlloysCost, influence);
            builder.Data.BuildTargetSystemId = targetSystemId;

            if (builder.Data.CurrentSystemId == targetSystemId
                && builder.Data.State == FleetState.Orbiting)
            {
                builder.Data.State = FleetState.Constructing;
                builder.Data.DaysRemainingConstruction = builder.Data.TotalConstructionDays;
            }
            else
            {
                IssueMoveOrder(builder, targetSystemId);
            }
            return true;
        }

        public bool ClaimSystem(int systemId, int ownerId)
        {
            if (_generator == null || systemId < 0 || systemId >= _generator.Systems.Count) return false;

            var system = _generator.Systems[systemId];
            // Пока строили, систему мог занять кто-то другой
            if (system.OwnerId >= 0 && system.OwnerId != ownerId) return false;
            system.OwnerId = ownerId;
            system.HasStarbase = true;
            system.IsSurveyed = true;
            system.GeneratePlanets();

            GalaxyView.Instance?.RefreshTerritoryVisuals();
            OnStarbaseBuilt?.Invoke();
            EconomyManager.Instance?.RecalculateAll();

            if (ownerId == 0)
                NotificationCenter.Show("Форпост построен", $"{system.Name} · содержание −{EmpireEconomy.OutpostUpkeep:0.#} Гелия-3/мес",
                    NotificationCenter.Kind.Success, 5f);

            return true;
        }

        public const float MiningStationMinerals = 50f;

        public bool BuildMiningStationOnPlanet(PlanetData planet)
        {
            if (planet == null || planet.HasMiningStation) return false;
            if (!planet.IsPlayerOwned)
            {
                NotificationCenter.Show("Станция недоступна", "Система не принадлежит вам", NotificationCenter.Kind.Warning, 4f);
                return false;
            }

            var eco = EconomyManager.Instance;
            if (eco == null) return false;
            if (!eco.CanAfford(0f, MiningStationMinerals, 0f, 0f)) return false;

            eco.TrySpend(0f, MiningStationMinerals, 0f, 0f);
            planet.HasMiningStation = true;

            if (_generator != null)
            {
                for (int i = 0; i < _generator.Systems.Count; i++)
                {
                    var sys = _generator.Systems[i];
                    if (sys.Planets.Contains(planet))
                    {
                        sys.RecalculateHarvest();
                        break;
                    }
                }
            }

            GalaxyView.Instance?.RefreshTerritoryVisuals();
            eco.RecalculateAll();
            NotificationCenter.Show("Добывающий комплекс", planet.Name + " активен", NotificationCenter.Kind.Success, 4f);
            return true;
        }
    }
}