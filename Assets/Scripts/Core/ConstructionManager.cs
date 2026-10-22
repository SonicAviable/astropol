using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Core
{
    public enum JobKind { District, MiningStation, Colony, Terraform, Ship, Robot, GeneAdapt }

    /// <summary>Строительный заказ: оплачен сразу, готов через несколько дней.</summary>
    [Serializable]
    public class ConstructionJob
    {
        public int Id;
        public JobKind Kind;
        public int Owner;
        public int SystemId = -1;
        public int PlanetIndex = -1;
        public DistrictType District;
        public FleetType ShipType;
        public ShipClass Hull;
        public string DesignId;
        public float DaysTotal;
        public float DaysLeft;
        // Возврат при отмене
        public float Energy, Minerals, Alloys, Influence;

        public float Progress => DaysTotal <= 0f ? 1f : Mathf.Clamp01(1f - DaysLeft / DaysTotal);
        public bool IsPlanetJob => Kind != JobKind.Ship;
    }

    /// <summary>
    /// Очереди строительства для всех империй (игрок и ИИ по одним правилам):
    ///   • на каждой планете — одна стройка за раз, остальные ждут в очереди (до 5);
    ///   • корабли — на верфи, параллельно <see cref="ShipyardSlots"/> корпуса;
    ///   • ресурсы списываются при заказе, отмена возвращает всё;
    ///   • если система захвачена — стройки в ней сгорают.
    /// </summary>
    public class ConstructionManager : MonoBehaviour
    {
        public static ConstructionManager Instance { get; private set; }

        public static event Action<ConstructionJob> OnJobCompleted;
        public static event Action OnQueuesChanged;

        public const int MaxPlanetQueue = 5;
        public const int ShipyardSlots = 2;

        // ---- Длительности, игровых дней ----
        public static float DistrictDays(DistrictType t) => t switch
        {
            DistrictType.Urban => 90f,
            DistrictType.Mining => 75f,
            DistrictType.Generator => 75f,
            _ => 120f
        };
        public const float MiningStationDays = 60f;
        public const float ColonyDays = 180f;
        public const float TerraformDays = 360f;
        public const float RobotDays = 90f;
        public const float GeneAdaptDays = 240f;

        public static float ShipDays(FleetType type, ShipClass hull)
        {
            if (type == FleetType.Science) return GamePace.CivilianShipDays;
            if (type == FleetType.Constructor) return GamePace.CivilianShipDays;
            return hull == ShipClass.Destroyer ? 150f : hull == ShipClass.Frigate ? 100f : 60f;
        }

        private readonly List<ConstructionJob> _jobs = new List<ConstructionJob>();
        public IReadOnlyList<ConstructionJob> Jobs => _jobs;
        private int _nextId = 1;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed += HandleDay;
            SiegeManager.OnSystemCaptured += HandleCaptured;
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
            SiegeManager.OnSystemCaptured -= HandleCaptured;
        }

        // ==================== ЗАПРОСЫ ====================

        public static int PlanetIndex(PlanetData p)
            => p?.ParentSystem != null ? p.ParentSystem.Planets.IndexOf(p) : -1;

        private static bool IsFor(ConstructionJob j, PlanetData p)
            => j.IsPlanetJob && p?.ParentSystem != null && j.SystemId == p.ParentSystem.Id && j.PlanetIndex == PlanetIndex(p);

        public List<ConstructionJob> QueueFor(PlanetData p)
        {
            var list = new List<ConstructionJob>();
            foreach (var j in _jobs) if (IsFor(j, p)) list.Add(j);
            return list;
        }

        public bool HasJob(PlanetData p, JobKind kind)
        {
            foreach (var j in _jobs) if (j.Kind == kind && IsFor(j, p)) return true;
            return false;
        }

        public bool HasAnyJob(PlanetData p)
        {
            foreach (var j in _jobs) if (IsFor(j, p)) return true;
            return false;
        }

        public int PendingDistricts(PlanetData p)
        {
            int n = 0;
            foreach (var j in _jobs) if (j.Kind == JobKind.District && IsFor(j, p)) n++;
            return n;
        }

        public int PendingDistricts(PlanetData p, DistrictType t)
        {
            int n = 0;
            foreach (var j in _jobs) if (j.Kind == JobKind.District && j.District == t && IsFor(j, p)) n++;
            return n;
        }

        public List<ConstructionJob> ShipQueue(int owner)
        {
            var list = new List<ConstructionJob>();
            foreach (var j in _jobs) if (j.Kind == JobKind.Ship && j.Owner == owner) list.Add(j);
            return list;
        }

        /// <summary>Дней до готовности (с учётом ожидания в очереди планеты или верфи).</summary>
        public float DaysUntilDone(ConstructionJob job)
        {
            if (job.Kind == JobKind.Ship)
            {
                var q = ShipQueue(job.Owner);
                int idx = q.IndexOf(job);
                if (idx < ShipyardSlots) return job.DaysLeft;
                // Ждёт свободного слота: примерно когда освободится (idx - слоты + 1)-й корпус
                var active = new List<float>();
                for (int i = 0; i < Mathf.Min(ShipyardSlots, q.Count); i++) active.Add(q[i].DaysLeft);
                float wait = 0f;
                for (int i = ShipyardSlots; i <= idx; i++)
                {
                    active.Sort();
                    wait = active[0];
                    active[0] = wait + q[i].DaysTotal;
                }
                return active[0];
            }
            float total = 0f;
            foreach (var j in _jobs)
            {
                if (!j.IsPlanetJob || j.SystemId != job.SystemId || j.PlanetIndex != job.PlanetIndex) continue;
                total += j.DaysLeft;
                if (j == job) break;
            }
            return total;
        }

        // ==================== ЗАКАЗЫ ====================

        public ConstructionJob EnqueuePlanetJob(JobKind kind, int owner, PlanetData p, DistrictType district,
                                                float energy, float minerals, float alloys, float influence)
        {
            if (p?.ParentSystem == null) return null;
            float days = kind switch
            {
                JobKind.District => DistrictDays(district),
                JobKind.MiningStation => MiningStationDays,
                JobKind.Colony => ColonyDays,
                JobKind.Robot => RobotDays,
                JobKind.GeneAdapt => GeneAdaptDays,
                _ => TerraformDays
            };
            var job = new ConstructionJob
            {
                Id = _nextId++, Kind = kind, Owner = owner, SystemId = p.ParentSystem.Id, PlanetIndex = PlanetIndex(p),
                District = district, DaysTotal = days, DaysLeft = days,
                Energy = energy, Minerals = minerals, Alloys = alloys, Influence = influence
            };
            if (kind == JobKind.Terraform) p.TerraformingInProgress = true;
            _jobs.Add(job);
            if (owner == 0) SFXManager.Play(Sfx.BuildQueued);
            OnQueuesChanged?.Invoke();
            return job;
        }

        /// <param name="systemId">Верфь, где заложен корабль: там он и появится (−1 — главная верфь владельца).</param>
        public ConstructionJob EnqueueShip(int owner, FleetType type, ShipClass hull, string designId, float energy, float alloys, int systemId = -1)
        {
            float days = ShipDays(type, hull) * FactionTraits.BuildTimeMult(owner);
            var job = new ConstructionJob
            {
                Id = _nextId++, Kind = JobKind.Ship, Owner = owner, SystemId = systemId, ShipType = type, Hull = hull, DesignId = designId,
                DaysTotal = days, DaysLeft = days, Energy = energy, Alloys = alloys
            };
            _jobs.Add(job);
            if (owner == 0) SFXManager.Play(Sfx.BuildQueued);
            OnQueuesChanged?.Invoke();
            return job;
        }

        // ==================== УСКОРЕНИЕ И ПРИОРИТЕТ ====================

        /// <summary>Гелия-3 за день оставшейся работы при покупке: планетарные стройки и корабли.</summary>
        public const float BuyPerDayPlanet = 1.0f, BuyPerDayShip = 1.4f;
        /// <summary>Работа ещё не начата — покупка дороже (бригады и материалы срочно, с нуля).</summary>
        public const float BuyUnstartedMult = 1.5f;

        /// <summary>Идёт ли работа над заказом прямо сейчас (первый в очереди планеты или занятый стапель верфи).</summary>
        public bool IsActive(ConstructionJob job)
        {
            if (job == null || !_jobs.Contains(job)) return false;
            if (job.Kind == JobKind.Ship)
            {
                int idx = ShipQueue(job.Owner).IndexOf(job);
                return idx >= 0 && idx < ShipyardSlots;
            }
            foreach (var j in _jobs)
                if (j.IsPlanetJob && j.SystemId == job.SystemId && j.PlanetIndex == job.PlanetIndex) return j == job;
            return false;
        }

        /// <summary>Сколько гелия-3 стоит завершить заказ немедленно.</summary>
        public static int BuyCost(ConstructionJob job)
        {
            if (job == null) return 0;
            float perDay = job.Kind == JobKind.Ship ? BuyPerDayShip : BuyPerDayPlanet;
            float price = Mathf.Max(1f, job.DaysLeft) * perDay;
            if (job.Progress < 0.01f) price *= BuyUnstartedMult;
            return Mathf.Max(5, Mathf.CeilToInt(price));
        }

        /// <summary>Можно ли купить заказ игрока сейчас (null — можно, иначе причина).</summary>
        public string BuyBlocker(ConstructionJob job)
        {
            if (job == null || job.Owner != 0) return Loc.T("Нет заказа");
            if (!IsActive(job)) return Loc.T("Купить можно только то, что строится сейчас — первый заказ планеты");
            var eco = EconomyManager.Instance;
            if (eco == null || eco.EnergyCredits < BuyCost(job)) return CostCheck.Full(BuyCost(job));
            return null;
        }

        /// <summary>Купить заказ игрока: оплатить гелием-3 и завершить сегодня же.</summary>
        public bool TryBuy(ConstructionJob job)
        {
            if (BuyBlocker(job) != null) return false;
            var eco = EconomyManager.Instance;
            int cost = BuyCost(job);
            if (!eco.TrySpend(cost, 0f, 0f, 0f)) return false;
            _jobs.Remove(job);
            job.DaysLeft = 0f;
            Complete(job);
            OnQueuesChanged?.Invoke();
            eco.RaiseResourcesChanged();
            return true;
        }

        /// <summary>Поставить планетарный заказ первым в очереди его планеты (прогресс прежнего первого сохраняется).</summary>
        public bool Prioritize(ConstructionJob job)
        {
            if (job == null || !job.IsPlanetJob || !_jobs.Contains(job)) return false;
            int first = _jobs.FindIndex(j => j.IsPlanetJob && j.SystemId == job.SystemId && j.PlanetIndex == job.PlanetIndex);
            if (first < 0 || _jobs[first] == job) return false;
            _jobs.Remove(job);
            _jobs.Insert(first, job);
            OnQueuesChanged?.Invoke();
            return true;
        }

        /// <summary>Отменить заказ и вернуть ресурсы владельцу.</summary>
        public void Cancel(ConstructionJob job)
        {
            if (job == null || !_jobs.Remove(job)) return;
            if (job.Owner == 0) SFXManager.Play(Sfx.UiBack);
            Refund(job);
            if (job.Kind == JobKind.Terraform)
            {
                var p = PlanetOf(job);
                if (p != null) p.TerraformingInProgress = false;
            }
            OnQueuesChanged?.Invoke();
            if (job.Owner == 0) EconomyManager.Instance?.RecalculateAll();
        }

        private static void Refund(ConstructionJob job)
        {
            if (job.Owner == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco == null) return;
                eco.EnergyCredits += job.Energy; eco.Minerals += job.Minerals;
                eco.Alloys += job.Alloys; eco.Influence += job.Influence;
                eco.RaiseResourcesChanged();
            }
            else if (AIEmpireManager.For(job.Owner) != null)
            {
                var ai = AIEmpireManager.For(job.Owner);
                ai.AddStock("energy", job.Energy); ai.AddStock("minerals", job.Minerals);
                ai.AddStock("alloys", job.Alloys); ai.AddStock("influence", job.Influence);
            }
        }

        private static PlanetData PlanetOf(ConstructionJob j)
        {
            var sys = EmpireStats.GetSystem(j.SystemId);
            if (sys == null || j.PlanetIndex < 0 || j.PlanetIndex >= sys.Planets.Count) return null;
            return sys.Planets[j.PlanetIndex];
        }

        // ==================== ПРОГРЕСС ====================

        private void HandleDay(int day, int month, int year)
        {
            if (_jobs.Count == 0) return;
            var done = new List<ConstructionJob>();

            // Планеты: идёт только первая стройка каждой планеты
            var started = new HashSet<long>();
            var shipSlots = new Dictionary<int, int>();
            foreach (var j in _jobs)
            {
                if (j.IsPlanetJob)
                {
                    long key = ((long)j.SystemId << 16) | (uint)j.PlanetIndex;
                    if (!started.Add(key)) continue;
                }
                else
                {
                    shipSlots.TryGetValue(j.Owner, out int used);
                    if (used >= ShipyardSlots) continue;
                    shipSlots[j.Owner] = used + 1;
                }
                // Губернатор планеты ускоряет стройку на ней
                j.DaysLeft -= j.IsPlanetJob ? LeaderManager.BuildSpeedMult(j.SystemId, j.PlanetIndex) : 1f;
                if (j.DaysLeft <= 0f) done.Add(j);
            }

            foreach (var j in done)
            {
                _jobs.Remove(j);
                Complete(j);
            }
            if (done.Count > 0) OnQueuesChanged?.Invoke();
        }

        private void Complete(ConstructionJob j)
        {
            if (j.Kind == JobKind.Ship) { CompleteShip(j); OnJobCompleted?.Invoke(j); return; }

            var p = PlanetOf(j);
            if (p == null || p.OwnerId != j.Owner) return;
            switch (j.Kind)
            {
                case JobKind.District:
                    if (p.BuiltDistricts < p.MaxDistricts) p.Districts.Add(new DistrictData(j.District));
                    Notify(j, Loc.T("Район построен"), $"{DistrictInfo.Name(j.District).ToLower()} · {p.Name}");
                    break;
                case JobKind.MiningStation:
                    p.HasMiningStation = true;
                    p.ParentSystem?.RecalculateHarvest();
                    if (j.Owner == 0) SystemViewManager.Instance?.SpawnStationOnActivePlanet(p);
                    Notify(j, Loc.T("Добывающий комплекс"), Loc.F("{0}: добыча началась", p.Name));
                    break;
                case JobKind.Colony:
                    if (p.Population <= 0) p.SettleColony();
                    Notify(j, Loc.T("Колония основана"), p.Name);
                    break;
                case JobKind.Terraform:
                    p.ApplyTerraformStep();
                    Notify(j, Loc.T("Терраформинг завершён"), $"{p.Name}: {p.ClassDisplayName.ToLower()}");
                    break;
                case JobKind.Robot:
                    p.AddPops(FactionTraits.AssembledSpecies(j.Owner), 1);
                    if (FactionTraits.IsMachine(j.Owner)) Notify(j, Loc.T("Синтет собран"), Loc.F("{0}: +1 синтет Консенсуса", p.Name));
                    else Notify(j, Loc.T("Роботы собраны"), Loc.F("{0}: +1 житель-робот", p.Name));
                    break;
                case JobKind.GeneAdapt:
                {
                    var src = SpeciesRegistry.Get(p.DominantOrganic);
                    var to = src != null ? SpeciesRegistry.Adapted(src.Id, p.Type) : null;
                    if (to != null)
                    {
                        p.ConvertPops(src.Id, to.Id, p.CountOf(s => s.Id == src.Id));
                        Notify(j, Loc.T("Генная адаптация завершена"), Loc.F("{0}: {1} — {2}", p.Name, to.DisplayName, to.TraitsLine));
                    }
                    break;
                }
            }
            if (j.Owner == 0)
            {
                EconomyManager.Instance?.RecalculateAll();
                GalaxyView.Instance?.RefreshTerritoryVisuals();
            }
            OnJobCompleted?.Invoke(j);
        }

        private static void Notify(ConstructionJob j, string title, string body)
        {
            if (j.Owner == 0) NotificationCenter.Show(title, body, NotificationCenter.Kind.Success, 4f);
        }

        private static void CompleteShip(ConstructionJob j)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            if (j.Owner == 0) fm.LaunchPlayerShip(j);
            else AIEmpireManager.For(j.Owner)?.LaunchShip(j);
        }

        /// <summary>Захваченная система: незавершённые стройки прежнего владельца сгорают.</summary>
        private void HandleCaptured(StarSystem sys, int oldOwner, int newOwner)
        {
            int removed = _jobs.RemoveAll(j => j.IsPlanetJob && j.SystemId == sys.Id && j.Owner == oldOwner);
            if (removed > 0)
            {
                foreach (var p in sys.Planets) p.TerraformingInProgress = false;
                if (oldOwner == 0)
                    NotificationCenter.Show(Loc.T("Стройки потеряны"), Loc.F("{0}: незавершённых заказов {1}", sys.Name, removed), NotificationCenter.Kind.Danger, 5f);
                OnQueuesChanged?.Invoke();
            }
        }

        // ==================== СОХРАНЕНИЕ ====================

        public List<ConstructionJob> CaptureState() => new List<ConstructionJob>(_jobs);

        public void RestoreState(List<ConstructionJob> jobs)
        {
            _jobs.Clear();
            if (jobs != null) _jobs.AddRange(jobs);
            foreach (var j in _jobs)
            {
                _nextId = Mathf.Max(_nextId, j.Id + 1);
                if (j.Kind == JobKind.Terraform)
                {
                    var p = PlanetOf(j);
                    if (p != null) p.TerraformingInProgress = true;
                }
            }
            OnQueuesChanged?.Invoke();
        }
    }
}
