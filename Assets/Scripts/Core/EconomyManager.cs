using System;
using UnityEngine;
using StellarisClone.Generation;

namespace StellarisClone.Core
{
    public class EconomyManager : MonoBehaviour
    {
        public static EconomyManager Instance { get; private set; }

        public event Action OnResourcesChanged;
        public void RaiseResourcesChanged() => OnResourcesChanged?.Invoke();

        [Header("Текущие запасы")]
        public float EnergyCredits = 400f;
        public float Minerals = 500f;
        public float Alloys = 250f;
        public float Influence = 100f;

        [Header("Базовый доход (без планет)")]
        public float BaseEnergyIncome = 15f;
        public float BaseMineralsIncome = 10f;
        public float BaseAlloysIncome = 5f;
        public float BaseInfluenceIncome = 3f;

        [Header("Отладка")]
        [SerializeField] private bool logMonthlyReport = false;

        private float _planetEnergy;
        private float _planetMinerals;
        private float _planetAlloys;
        private float _upkeepEnergy;

        public float MonthlyEnergyIncome    => BaseEnergyIncome + _planetEnergy - _upkeepEnergy;
        public float MonthlyMineralsIncome  => BaseMineralsIncome + _planetMinerals;
        public float MonthlyAlloysIncome    => BaseAlloysIncome + _planetAlloys;
        public float MonthlyInfluenceIncome => BaseInfluenceIncome;

        public float MonthlyUpkeep          => _upkeepEnergy;
        public float PlanetEnergyOutput     => _planetEnergy;
        public float PlanetMineralsOutput   => _planetMinerals;
        public float PlanetAlloysOutput     => _planetAlloys;
        public bool  IsBankrupt             => _bankrupt;

        private GalaxyGenerator _generator;

        private float _factionEnergyMult = 1f;
        private float _factionMineralMult = 1f;
        private float _factionAlloyMult = 1f;
        private float _factionInfluenceMult = 1f;

        private bool _bankrupt;
        private float _lowEnergyAccum;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            _generator = FindAnyObjectByType<GalaxyGenerator>();
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed += HandleDayPassed;

            RecalculatePlanetOutput();
            RecalculateUpkeep();
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed -= HandleDayPassed;
        }

        private void HandleDayPassed(int day, int month, int year)
        {
            RecalculatePlanetOutput();
            RecalculateUpkeep();

            if (day != 1) return;

            EnergyCredits += MonthlyEnergyIncome;
            Minerals      += MonthlyMineralsIncome;
            Alloys        += MonthlyAlloysIncome;
            Influence     += MonthlyInfluenceIncome;

            if (EnergyCredits < 0f)
            {
                EnergyCredits = 0f;
                _lowEnergyAccum += 1f;
                if (_lowEnergyAccum >= 2f && !_bankrupt)
                {
                    _bankrupt = true;
                    Debug.LogWarning("<color=#F55>[Экономика]</color> БАНКРОТСТВО: производство снижено на 50%.");
                    NotificationCenter.Show("Банкротство", "Производство урезано вдвое", NotificationCenter.Kind.Danger, 8f);
                }
            }
            else if (_bankrupt && EnergyCredits > 60f)
            {
                _bankrupt = false;
                _lowEnergyAccum = 0f;
                Debug.Log("<color=#5F5>[Экономика]</color> Кризис преодолён.");
                NotificationCenter.Show("Кризис преодолён", "Экономика восстанавливается", NotificationCenter.Kind.Success, 5f);
            }
            else if (EnergyCredits > 5f)
            {
                _lowEnergyAccum = 0f;
            }

            ProcessColonyGrowth();

            if (logMonthlyReport)
            {
                Debug.Log($"<color=#5F5>[Экономика]</color> ⚡{MonthlyEnergyIncome:+0;-0} " +
                          $"◆{MonthlyMineralsIncome:+0;-0} ⬢{MonthlyAlloysIncome:+0;-0} " +
                          $"★{MonthlyInfluenceIncome:+0;-0}  (содержание ⚡{-_upkeepEnergy:0})");
            }

            OnResourcesChanged?.Invoke();
        }

        // ==================== ПРОИЗВОДСТВО ПЛАНЕТ ====================

        private void RecalculatePlanetOutput()
        {
            if (_generator == null) return;

            int min = 0, en = 0, al = 0;
            foreach (var sys in _generator.Systems)
            {
                if (sys.OwnerId != 0) continue;
                foreach (var p in sys.Planets)
                {
                    if (p.Type == PlanetType.GasGiant || p.Type == PlanetType.Molten) continue;
                    if (p.Population <= 0) continue;
                    min += p.ProducedMineralsPerMonth;
                    en  += p.ProducedEnergyPerMonth;
                    al  += p.ProducedAlloysPerMonth;
                }
            }

            float penalty = _bankrupt ? 0.5f : 1f;
            _planetMinerals = min * _factionMineralMult * penalty;
            _planetEnergy   = en  * _factionEnergyMult  * penalty;
            _planetAlloys   = al  * _factionAlloyMult   * penalty;
        }

        // ==================== СОДЕРЖАНИЕ ====================

        private void RecalculateUpkeep()
        {
            float upkeep = 0f;

            var fm = FleetManager.Instance;
            if (fm != null)
            {
                foreach (var f in fm.AllFleets)
                {
                    if (f?.Data == null || f.Data.Destroyed) continue;
                    if (f.Data.OwnerId != 0) continue;
                    upkeep += f.Data.UpkeepEnergy;
                }
            }

            if (_generator != null)
            {
                foreach (var sys in _generator.Systems)
                {
                    if (sys.OwnerId != 0 || !sys.HasStarbase) continue;
                    if (sys.Id == 0) continue;
                    upkeep += 1.5f;
                }
            }

            _upkeepEnergy = upkeep;
        }

        // ==================== РОСТ НАСЕЛЕНИЯ ====================

        private void ProcessColonyGrowth()
        {
            if (_generator == null) return;

            foreach (var sys in _generator.Systems)
            {
                if (sys.OwnerId != 0) continue;
                foreach (var p in sys.Planets)
                {
                    if (p.Type == PlanetType.GasGiant || p.Type == PlanetType.Molten) continue;
                    if (p.Population <= 0) continue;

                    int housing = p.HousingCapacity;
                    if (housing <= p.Population) { p.PopGrowthProgress = 0f; continue; }

                    float growth = 4f + (housing - p.Population) * 0.4f;
                    p.PopGrowthProgress += growth;

                    while (p.PopGrowthProgress >= 100f && p.Population < housing)
                    {
                        p.PopGrowthProgress -= 100f;
                        p.Population++;
                    }
                }
            }
        }

        // ==================== API ====================

        public bool CanAfford(float energy, float minerals, float alloys, float influence)
            => EnergyCredits >= energy && Minerals >= minerals
            && Alloys >= alloys && Influence >= influence;

        public bool TrySpend(float energy, float minerals, float alloys, float influence)
        {
            if (!CanAfford(energy, minerals, alloys, influence)) return false;
            EnergyCredits -= energy;
            Minerals -= minerals;
            Alloys -= alloys;
            Influence -= influence;
            OnResourcesChanged?.Invoke();
            return true;
        }

        public void AddIncome(float energy, float minerals, float alloys, float influence)
        {
            BaseEnergyIncome    += energy;
            BaseMineralsIncome  += minerals;
            BaseAlloysIncome    += alloys;
            BaseInfluenceIncome += influence;
            OnResourcesChanged?.Invoke();
        }

        // ==================== СОХРАНЕНИЕ ====================

        public EconomySave CaptureState() => new EconomySave
        {
            Energy = EnergyCredits, Minerals = Minerals, Alloys = Alloys, Influence = Influence,
            BaseEnergy = BaseEnergyIncome, BaseMinerals = BaseMineralsIncome,
            BaseAlloys = BaseAlloysIncome, BaseInfluence = BaseInfluenceIncome,
            MultEnergy = _factionEnergyMult, MultMinerals = _factionMineralMult,
            MultAlloys = _factionAlloyMult, MultInfluence = _factionInfluenceMult,
            Bankrupt = _bankrupt, LowEnergyAccum = _lowEnergyAccum
        };

        public void RestoreState(EconomySave s)
        {
            if (s == null) return;
            EnergyCredits = s.Energy; Minerals = s.Minerals; Alloys = s.Alloys; Influence = s.Influence;
            BaseEnergyIncome = s.BaseEnergy; BaseMineralsIncome = s.BaseMinerals;
            BaseAlloysIncome = s.BaseAlloys; BaseInfluenceIncome = s.BaseInfluence;
            _factionEnergyMult = s.MultEnergy; _factionMineralMult = s.MultMinerals;
            _factionAlloyMult = s.MultAlloys; _factionInfluenceMult = s.MultInfluence;
            _bankrupt = s.Bankrupt;
            _lowEnergyAccum = s.LowEnergyAccum;
            RecalculateAll();
        }

        /// <summary>Пересчитать производство и содержание немедленно (после загрузки).</summary>
        public void RecalculateAll()
        {
            if (_generator == null) _generator = FindAnyObjectByType<GalaxyGenerator>();
            RecalculatePlanetOutput();
            RecalculateUpkeep();
            OnResourcesChanged?.Invoke();
        }

        public void ApplyFactionBonuses(FactionInfo faction)
        {
            if (faction == null) return;

            _factionEnergyMult    = faction.EnergyBonus;
            _factionMineralMult   = faction.MineralBonus;
            _factionAlloyMult     = faction.AlloyBonus;
            _factionInfluenceMult = faction.InfluenceBonus;

            BaseInfluenceIncome *= _factionInfluenceMult;

            OnResourcesChanged?.Invoke();
        }
    }
}