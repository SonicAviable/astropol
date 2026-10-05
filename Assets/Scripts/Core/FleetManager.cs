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

        private const float StarbaseAlloysCost = 50f;
        private const float StarbaseInfluenceCost = 25f;

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

            // При загрузке флоты и экономику восстанавливает SaveLoader
            if (GameSession.IsLoading) return;

            SetupStartingEconomyAndFleets();
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

        public int GetMilitaryPower(int ownerId)
        {
            int sum = 0;
            foreach (var f in _allFleets)
            {
                if (f?.Data == null || f.Data.Destroyed) continue;
                if (f.Data.OwnerId == ownerId && f.Data.Type == FleetType.Military)
                    sum += f.Data.MilitaryPower;
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
                    costAlloys = 100f; costEnergy = 50f;
                    shipName = $"НИС «Академик {AllFleets.Count + 1}»";
                    break;
                case FleetType.Constructor:
                    costAlloys = 80f; costEnergy = 20f;
                    shipName = $"Строитель {AllFleets.Count + 1}";
                    break;
                case FleetType.Military:
                    costAlloys = 60f; costEnergy = 10f;
                    shipName = $"{AllFleets.Count + 1}-й Корвет";
                    break;
            }

            if (type == FleetType.Military && ShipDesignManager.Instance != null)
            {
                var design = ShipDesignManager.Instance.GetLatestDesign(ShipClass.Corvette);
                if (design != null) costAlloys = design.AlloyCost;
            }

            if (!eco.CanAfford(costEnergy, 0f, costAlloys, 0f))
            {
                NotificationCenter.Show("Недостаточно ресурсов",
                    $"Нужно {costAlloys} ⬢  и  {costEnergy} ⚡",
                    NotificationCenter.Kind.Warning, 4f);
                return false;
            }

            eco.TrySpend(costEnergy, 0f, costAlloys, 0f);
            var created = CreateFleetObject(shipName, 0, type);
            if (type == FleetType.Military)
            {
                var design = ShipDesignManager.Instance?.GetLatestDesign(ShipClass.Corvette);
                if (design != null) created.Data.ApplyDesign(design);
            }

            NotificationCenter.Show("Корабль построен", shipName, NotificationCenter.Kind.Success, 4f);
            return true;
        }

        public bool BuildShipFromDesign(ShipDesign design)
        {
            var eco = EconomyManager.Instance;
            if (eco == null || design == null || !design.IsPowerValid) return false;
            if (!eco.CanAfford(10f, 0f, design.AlloyCost, 0f)) return false;
            eco.TrySpend(10f, 0f, design.AlloyCost, 0f);
            string cls = design.HullClass switch
            {
                ShipClass.Frigate => "Фрегат",
                ShipClass.Destroyer => "Эсминец",
                _ => "Корвет"
            };
            var created = CreateFleetObject($"{AllFleets.Count + 1}-й {cls}", 0, FleetType.Military);
            created.Data.ApplyDesign(design);
            NotificationCenter.Show("Корабль построен", created.Data.Name, NotificationCenter.Kind.Success, 4f);
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
            if (eco == null || !eco.CanAfford(0f, 0f, StarbaseAlloysCost, StarbaseInfluenceCost))
            {
                NotificationCenter.Show("Недостаточно ресурсов",
                    $"Форпост: {StarbaseAlloysCost} ⬢ + {StarbaseInfluenceCost} ★",
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

            eco.TrySpend(0f, 0f, StarbaseAlloysCost, StarbaseInfluenceCost);
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
            system.OwnerId = ownerId;
            system.HasStarbase = true;
            system.IsSurveyed = true;

            GalaxyView.Instance?.RefreshTerritoryVisuals();
            OnStarbaseBuilt?.Invoke();

            if (ownerId == 0)
                NotificationCenter.Show("Форпост построен", system.Name, NotificationCenter.Kind.Success, 5f);

            return true;
        }

        public bool BuildMiningStationOnPlanet(PlanetData planet)
        {
            if (planet == null || planet.HasMiningStation) return false;

            var eco = EconomyManager.Instance;
            if (eco == null) return false;
            if (!eco.CanAfford(0f, 50f, 0f, 0f)) return false;

            eco.TrySpend(0f, 50f, 0f, 0f);
            planet.HasMiningStation = true;
            eco.AddIncome(planet.EnergyDeposit, planet.MineralDeposit, 0f, 0f);

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
            NotificationCenter.Show("Добывающий комплекс", planet.Name + " активен", NotificationCenter.Kind.Success, 4f);
            return true;
        }
    }
}