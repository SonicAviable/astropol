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

        /// <summary>Столица игрока — её форпост бесплатен.</summary>
        public const int PlayerCapitalId = 0;

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

        private EmpireEconomy.Report _report;

        public float MonthlyEnergyIncome    => BaseEnergyIncome + _report.Energy - _report.Upkeep;
        public float MonthlyMineralsIncome  => BaseMineralsIncome + _report.Minerals;
        public float MonthlyAlloysIncome    => BaseAlloysIncome + _report.Alloys;
        public float MonthlyInfluenceIncome => BaseInfluenceIncome;

        public EmpireEconomy.Report Report  => _report;
        public float MonthlyUpkeep          => _report.Upkeep;
        public float PlanetEnergyOutput     => _report.DistrictEnergy;
        public float PlanetMineralsOutput   => _report.DistrictMinerals;
        public float PlanetAlloysOutput     => _report.DistrictAlloys;
        public float StationEnergyOutput    => _report.StationEnergy;
        public float StationMineralsOutput  => _report.StationMinerals;
        public float TechEnergyOutput       => _report.TechEnergy;
        public float FleetUpkeep            => _report.FleetUpkeep + _report.CivilianUpkeep;
        public float OutpostUpkeep          => _report.OutpostUpkeepTotal;
        public int   NavalUsed              => _report.NavalUsed;
        public int   NavalCapacity          => _report.NavalCapacity;
        public bool  IsBankrupt             => _bankrupt;
        public float FactionEnergyMult      => _factionEnergyMult;
        public float FactionMineralMult     => _factionMineralMult;
        public float FactionAlloyMult       => _factionAlloyMult;

        /// <summary>Через сколько месяцев казна опустеет при текущем балансе (∞ — если доход положительный).</summary>
        public float MonthsUntilEmpty
        {
            get
            {
                float net = MonthlyEnergyIncome;
                if (net >= 0f) return float.PositiveInfinity;
                return Mathf.Max(0f, EnergyCredits) / -net;
            }
        }

        /// <summary>Порог предупреждения о скором банкротстве (месяцев).</summary>
        public const float BankruptcyWarnMonths = 6f;
        public bool BankruptcyLooming => !_bankrupt && MonthsUntilEmpty <= BankruptcyWarnMonths;

        private GalaxyGenerator _generator;

        private float _factionEnergyMult = 1f;
        private float _factionMineralMult = 1f;
        private float _factionAlloyMult = 1f;
        private float _factionInfluenceMult = 1f;

        private bool _bankrupt;
        private float _lowEnergyAccum;
        private int _warnLevel;   // 0 — нет, 1 — ≤6 мес, 2 — ≤3 мес, 3 — ≤1 мес

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

            Recalculate();
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed -= HandleDayPassed;
        }

        private void HandleDayPassed(int day, int month, int year)
        {
            Recalculate();
            CheckBankruptcyForecast();

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
                    NotificationCenter.Show("Банкротство",
                        "Производство урезано вдвое. Распустите часть флота или стройте генераторы — иначе экономика рухнет",
                        NotificationCenter.Kind.Danger, 10f);
                }
                else if (!_bankrupt)
                {
                    NotificationCenter.Show("Казна пуста",
                        "Ещё месяц дефицита — и наступит банкротство (производство ×0.5)",
                        NotificationCenter.Kind.Danger, 8f);
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
                          $"★{MonthlyInfluenceIncome:+0;-0}  (содержание ⚡{-_report.Upkeep:0})");
            }

            OnResourcesChanged?.Invoke();
        }

        // ==================== ПРОИЗВОДСТВО И СОДЕРЖАНИЕ ====================

        private void Recalculate()
        {
            var bonuses = TechnologyManager.Instance != null ? TechnologyManager.Instance.Bonuses : null;
            _report = EmpireEconomy.Compute(0, PlayerCapitalId, _factionEnergyMult, _factionMineralMult, _factionAlloyMult,
                                            bonuses, _bankrupt);
        }

        // ==================== ПРЕДУПРЕЖДЕНИЕ О БАНКРОТСТВЕ ====================

        private void CheckBankruptcyForecast()
        {
            if (_bankrupt) { _warnLevel = 0; return; }
            float months = MonthsUntilEmpty;
            int level = months <= 1f ? 3 : months <= 3f ? 2 : months <= BankruptcyWarnMonths ? 1 : 0;

            if (level > _warnLevel)
            {
                string when = months < 1f ? "меньше чем через месяц" : $"примерно через {Mathf.CeilToInt(months)} мес.";
                NotificationCenter.Show(level >= 3 ? "Банкротство неизбежно" : "Угроза банкротства",
                    $"Расход Гелия-3 превышает доход на {-MonthlyEnergyIncome:0.#}/мес — казна опустеет {when}. " +
                    "Сократите флот, откажитесь от лишних форпостов или стройте генераторы.",
                    level >= 2 ? NotificationCenter.Kind.Danger : NotificationCenter.Kind.Warning, 8f);
            }
            _warnLevel = level;
        }

        // ==================== РОСТ НАСЕЛЕНИЯ ====================

        /// <summary>Рост населения во всех колониях галактики (у игрока и у ИИ одинаковые правила).</summary>
        private void ProcessColonyGrowth()
        {
            if (_generator == null) return;

            foreach (var sys in _generator.Systems)
            {
                if (sys.OwnerId < 0) continue;
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

        /// <summary>
        /// Версия 1 вписывала доход добывающих станций и реакторных технологий в базовый доход;
        /// теперь они считаются от территории и технологий — вычитаем их из базы.
        /// </summary>
        public void RestoreState(EconomySave s, int version)
        {
            if (s == null) return;
            EnergyCredits = s.Energy; Minerals = s.Minerals; Alloys = s.Alloys; Influence = s.Influence;
            BaseEnergyIncome = s.BaseEnergy; BaseMineralsIncome = s.BaseMinerals;
            BaseAlloysIncome = s.BaseAlloys; BaseInfluenceIncome = s.BaseInfluence;
            _factionEnergyMult = s.MultEnergy; _factionMineralMult = s.MultMinerals;
            _factionAlloyMult = s.MultAlloys; _factionInfluenceMult = s.MultInfluence;
            _bankrupt = s.Bankrupt;
            _lowEnergyAccum = s.LowEnergyAccum;

            if (version < 2)
            {
                if (_generator == null) _generator = FindAnyObjectByType<GalaxyGenerator>();
                if (_generator != null)
                    foreach (var sys in _generator.Systems)
                    {
                        if (sys.OwnerId != 0) continue;
                        foreach (var p in sys.Planets)
                            if (p.HasMiningStation) { BaseEnergyIncome -= p.EnergyDeposit; BaseMineralsIncome -= p.MineralDeposit; }
                    }
                var tm = TechnologyManager.Instance;
                if (tm != null) BaseEnergyIncome -= tm.Bonuses.EnergyFlat;
                BaseEnergyIncome = Mathf.Max(0f, BaseEnergyIncome);
                BaseMineralsIncome = Mathf.Max(0f, BaseMineralsIncome);
            }
            RecalculateAll();
        }

        /// <summary>Пересчитать производство и содержание немедленно (после загрузки, постройки, захвата).</summary>
        public void RecalculateAll()
        {
            if (_generator == null) _generator = FindAnyObjectByType<GalaxyGenerator>();
            Recalculate();
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
