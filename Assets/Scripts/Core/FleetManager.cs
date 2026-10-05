using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using StellarisClone.Generation;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Core
{
    public class FleetManager : MonoBehaviour
    {
        public static FleetManager Instance { get; private set; }
        public static event Action<FleetView> OnFleetSelected;
        public static event Action OnStarbaseBuilt;
        public static event Action<StarSystem> OnSystemSurveyCompleted;

        private GalaxyGenerator _generator;
        /// <summary>Главный выделенный флот (первый в выделении).</summary>
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
            if (_selection.Contains(view))
            {
                var next = new List<FleetView>(_selection);
                next.Remove(view);
                ApplySelection(next);
            }
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
            string designId = type == FleetType.Military ? ShipDesignManager.Instance?.GetLatestDesign(ShipClass.Corvette)?.Id : null;
            QueueShip(type, ShipClass.Corvette, designId, costEnergy, costAlloys, shipName);
            return true;
        }

        /// <summary>Заложить корабль игрока на верфи (готов через ShipDays дней).</summary>
        private void QueueShip(FleetType type, ShipClass hull, string designId, float energy, float alloys, string what)
        {
            var cm = ConstructionManager.Instance;
            if (cm == null) { LaunchPlayerShip(new ConstructionJob { ShipType = type, Hull = hull, DesignId = designId }); return; }
            var job = cm.EnqueueShip(0, type, hull, designId, energy, alloys);
            NotificationCenter.Show("Корабль заложен", $"{what} · готов через ~{Mathf.CeilToInt(cm.DaysUntilDone(job))} дн.",
                NotificationCenter.Kind.Info, 4f);
        }

        /// <summary>Спуск на воду: корабль появляется у верфи (или в любой своей системе).</summary>
        public FleetView LaunchPlayerShip(ConstructionJob job)
        {
            int yard = PlayerShipyardSystem();
            if (yard < 0) return null;
            FleetView created;
            if (job.ShipType == FleetType.Military)
            {
                var design = (ShipDesignManager.Instance != null ? ShipDesignManager.Instance.GetDesign(job.DesignId) : null)
                             ?? PlayerDesignFor(job.Hull);
                string cls = job.Hull switch { ShipClass.Frigate => "Фрегат", ShipClass.Destroyer => "Эсминец", _ => "Корвет" };
                created = CreateFleetObject($"{AllFleets.Count + 1}-й {cls}", yard, FleetType.Military);
                if (design != null) created.Data.ApplyDesign(design);
            }
            else
            {
                string name = job.ShipType == FleetType.Science ? $"НИС «Академик {AllFleets.Count + 1}»" : $"Строитель {AllFleets.Count + 1}";
                created = CreateFleetObject(name, yard, job.ShipType);
            }
            NotificationCenter.Show("Корабль спущен на воду", $"{created.Data.Name} · {_generator.Systems[yard].Name}", NotificationCenter.Kind.Success, 4f);
            EconomyManager.Instance?.RecalculateAll();
            return created;
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
            QueueShip(FleetType.Military, design.HullClass, design.Id, WarshipEnergy, cost, $"{cls} «{design.Name}»");
            return true;
        }

        // ==================== ВЫДЕЛЕНИЕ ====================
        // Ввод мышью (клик, рамка, Shift, ПКМ) — в FleetSelectionController; здесь — состояние и приказы.

        private readonly List<FleetView> _selection = new List<FleetView>();
        public IReadOnlyList<FleetView> SelectedFleets => _selection;
        public static event Action OnSelectionChanged;

        public bool IsSelected(FleetView f) => f != null && _selection.Contains(f);

        public void SelectFleet(FleetView fleet)
        {
            if (fleet == null) { SetSelection(null); return; }
            SetSelection(new[] { fleet });
        }

        /// <summary>Заменить (или дополнить) выделение. Выделять можно только свои флоты.</summary>
        public void SetSelection(IEnumerable<FleetView> fleets, bool additive = false)
        {
            var next = new List<FleetView>();
            if (additive) next.AddRange(_selection);
            if (fleets != null)
                foreach (var f in fleets)
                    if (f?.Data != null && !f.Data.Destroyed && f.Data.OwnerId == 0 && !next.Contains(f)) next.Add(f);
            ApplySelection(next);
        }

        public void ToggleInSelection(FleetView f)
        {
            if (f?.Data == null || f.Data.OwnerId != 0) return;
            var next = new List<FleetView>(_selection);
            if (!next.Remove(f)) next.Add(f);
            ApplySelection(next);
        }

        private void ApplySelection(List<FleetView> next)
        {
            var oldPrimary = SelectedFleet;
            foreach (var f in _selection)
                if (f != null && !next.Contains(f)) f.SetSelected(false);
            foreach (var f in next)
                if (!_selection.Contains(f)) f.SetSelected(true);

            bool changed = next.Count != _selection.Count;
            if (!changed)
                for (int i = 0; i < next.Count; i++) if (next[i] != _selection[i]) { changed = true; break; }

            _selection.Clear();
            _selection.AddRange(next);
            SelectedFleet = _selection.Count > 0 ? _selection[0] : null;

            if (!changed) return;
            if (SelectedFleet == null) SFXManager.Play(Sfx.UiBack, 0.6f);
            else if (SelectedFleet != oldPrimary) PlayFleetSelectSFX(SelectedFleet.Data.Type);
            if (SelectedFleet != oldPrimary) OnFleetSelected?.Invoke(SelectedFleet);
            OnSelectionChanged?.Invoke();
        }

        private void PlayFleetSelectSFX(FleetType type)
        {
            SFXManager.Play(type switch
            {
                FleetType.Military    => Sfx.SelectMilitary,
                FleetType.Science     => Sfx.SelectScience,
                FleetType.Constructor => Sfx.SelectConstructor,
                _                     => Sfx.UiConfirm
            });
        }

        // ==================== ПРИКАЗЫ ====================

        /// <summary>
        /// ПКМ по системе для всех выделенных флотов. queue — Shift: добавить пункт в очередь.
        /// Научные корабли сами начинают разведку неизученной системы, строитель (один) — форпост.
        /// </summary>
        public void CommandSelection(int systemId, bool queue)
        {
            if (_generator == null || systemId < 0 || systemId >= _generator.Systems.Count) return;
            var target = _generator.Systems[systemId];
            bool builderAssigned = false;
            if (_selection.Count > 0) SFXManager.Play(queue ? Sfx.OrderQueue : Sfx.OrderMove);

            foreach (var fleet in new List<FleetView>(_selection))
            {
                if (fleet?.Data == null || fleet.Data.Destroyed) continue;
                var d = fleet.Data;

                if (!queue && d.Type == FleetType.Science && !target.IsSurveyed && target.OwnerId == -1)
                {
                    d.OrderQueue.Clear();
                    d.HasPlayerOrder = false;
                    if (OrderSurveySystem(systemId, fleet)) continue;
                }
                if (!queue && !builderAssigned && d.Type == FleetType.Constructor && target.OwnerId == -1 && target.IsSurveyed)
                {
                    d.OrderQueue.Clear();
                    d.HasPlayerOrder = false;
                    if (OrderBuildStarbase(systemId, fleet)) { builderAssigned = true; continue; }
                }
                CommandMove(fleet, systemId, queue);
            }
        }

        public void CommandMove(FleetView fleet, int systemId, bool queue)
        {
            if (fleet?.Data == null) return;
            var d = fleet.Data;
            if (queue && (d.IsBusy || d.OrderQueue.Count > 0))
            {
                // Повтор той же точки в конце очереди не нужен
                int lastQueued = d.OrderQueue.Count > 0 ? d.OrderQueue[d.OrderQueue.Count - 1] : d.CurrentDestination;
                if (lastQueued != systemId) d.OrderQueue.Add(systemId);
                d.HasPlayerOrder = d.OwnerId == 0;
                fleet.UpdatePathVisuals();
                return;
            }
            d.OrderQueue.Clear();
            if (d.InCombat) return;   // из боя — только экстренный прыжок
            d.Path.Clear();           // новый приказ отменяет старый маршрут (в т.ч. «остаться здесь»)
            IssueMoveOrder(fleet, systemId);
            d.HasPlayerOrder = d.OwnerId == 0 && d.Path.Count > 0;
            fleet.UpdatePathVisuals();
        }

        /// <summary>Стоп: сбросить маршрут и очередь (корабль в гиперкоридоре долетит до ближайшей системы).</summary>
        public void StopFleet(FleetView fleet)
        {
            if (fleet?.Data == null) return;
            if (fleet.Data.OwnerId == 0 && (fleet.Data.Path.Count > 0 || fleet.Data.OrderQueue.Count > 0)) SFXManager.Play(Sfx.OrderStop);
            fleet.Data.Path.Clear();
            fleet.Data.OrderQueue.Clear();
            fleet.Data.HasPlayerOrder = false;
            fleet.UpdatePathVisuals();
        }

        public void ClearQueue(FleetView fleet)
        {
            if (fleet?.Data == null) return;
            fleet.Data.OrderQueue.Clear();
            fleet.UpdatePathVisuals();
        }

        /// <summary>Пропустить текущую цель и сразу лететь к следующей из очереди.</summary>
        public void SkipToNext(FleetView fleet)
        {
            if (fleet?.Data == null) return;
            var d = fleet.Data;
            if (d.OrderQueue.Count == 0) { StopFleet(fleet); return; }
            int next = d.OrderQueue[0];
            d.OrderQueue.RemoveAt(0);
            d.Path.Clear();
            StartLeg(fleet, next);
        }

        /// <summary>Флот освободился (прибыл, закончил разведку/стройку) — следующий пункт очереди.</summary>
        public void AdvanceQueue(FleetView fleet)
        {
            var d = fleet?.Data;
            if (d == null || d.Destroyed || d.OrderQueue.Count == 0) return;
            if (d.State != FleetState.Orbiting || d.Path.Count > 0) return;
            int next = d.OrderQueue[0];
            d.OrderQueue.RemoveAt(0);
            StartLeg(fleet, next);
        }

        private void StartLeg(FleetView fleet, int target)
        {
            var d = fleet.Data;
            if (_generator != null && d.Type == FleetType.Science && target >= 0 && target < _generator.Systems.Count)
            {
                var sys = _generator.Systems[target];
                if (!sys.IsSurveyed && sys.OwnerId == -1) d.SurveyTargetSystemId = target;
            }
            if (d.CurrentSystemId == target && d.State == FleetState.Orbiting)
            {
                if (d.SurveyTargetSystemId == target)
                {
                    d.State = FleetState.Surveying;
                    d.DaysRemainingSurvey = d.TotalSurveyDays;
                }
                else AdvanceQueue(fleet);
                return;
            }
            IssueMoveOrder(fleet, target);
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

        public void CompleteSystemSurvey(int systemId, int ownerId, float rewardMult = 1f)
        {
            if (systemId < 0 || systemId >= _generator.Systems.Count) return;
            var sys = _generator.Systems[systemId];
            // Разведка у каждой стороны своя: исследование ИИ не делает систему изученной для игрока
            if (ownerId == 0) sys.IsSurveyed = true;
            else sys.SurveyedByAI = true;

            if (ownerId == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco != null)
                {
                    // Учёный на корабле увеличивает трофеи разведки
                    eco.Minerals += 75f * rewardMult;
                    eco.Alloys += 40f * rewardMult;
                    eco.Influence += 15f * rewardMult;
                    eco.EnergyCredits += 25f * rewardMult;
                    eco.RaiseResourcesChanged();
                }
                SFXManager.Play(Sfx.SurveyComplete);
                NotificationCenter.Show("Разведка завершена", sys.Name, NotificationCenter.Kind.Success, 4f);
            }
            else if (ownerId == AIEmpireManager.AIOwnerId && AIEmpireManager.Instance != null)
            {
                // ИИ получает за разведку те же трофеи, что и игрок
                var ai = AIEmpireManager.Instance;
                ai.AddStock("minerals", 75f * rewardMult);
                ai.AddStock("alloys", 40f * rewardMult);
                ai.AddStock("influence", 15f * rewardMult);
                ai.AddStock("energy", 25f * rewardMult);
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
            system.SurveyedByAI = true;
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

            if (planet.StationUnderConstruction) return false;
            eco.TrySpend(0f, MiningStationMinerals, 0f, 0f);
            var cm = ConstructionManager.Instance;
            if (cm != null)
            {
                cm.EnqueuePlanetJob(JobKind.MiningStation, 0, planet, DistrictType.Mining, 0f, MiningStationMinerals, 0f, 0f);
                NotificationCenter.Show("Комплекс заложен", $"{planet.Name} · {ConstructionManager.MiningStationDays:0} дн.", NotificationCenter.Kind.Info, 3f);
                return true;
            }
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