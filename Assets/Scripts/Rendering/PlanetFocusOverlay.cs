using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Полноэкранный обзор планеты — главный экран управления колонией.
    ///   • вверху — название, класс, владелец; кнопка «Закрыть»;
    ///   • слева — поверхность (пригодность, размер, климат, орбита) и природные залежи;
    ///   • в центре — вращающаяся планета;
    ///   • справа — население (жильё, рост до следующего жителя, занятость, наука) и производство;
    ///   • внизу — районы: что даёт каждый, сколько работает, стоимость и постройка в один клик;
    ///     под ними — добывающий комплекс, колонизация и терраформинг.
    /// Все значки — векторные иконки LGIcons, все числа — с учётом бонусов фракции и технологий.
    /// </summary>
    public class PlanetFocusOverlay : MonoBehaviour
    {
        public static PlanetFocusOverlay Instance { get; private set; }
        public bool IsOpen => _isOpen;

        private Canvas _host;
        private GameObject _root;
        private RawImage _planetImage;

        private Text _title, _subtitle, _ownerText;
        private Image _ownerIcon;

        // Левая колонка
        private Text _surfaceText, _depositText, _stationText;
        private RectTransform _habBar, _sizeBar;
        private Text _habValue, _sizeValue;

        // Правая колонка
        private Text _popBig, _popText, _growthText, _productionText;
        private RectTransform _housingBar, _growthBar;

        // Районы
        private Text _districtHeader;
        private RectTransform _districtSlots;
        private readonly List<DistrictCard> _cards = new List<DistrictCard>();

        // Действия
        private ActionButton _mine, _colony, _terra;

        private PlanetData _planet;
        private StarSystem _system;
        private bool _isOpen;
        private float _refreshTimer;

        private const float PlanetSize = 420f;
        private static readonly Color CGold = UIManager.DS.Gold;
        private static readonly Color CMuted = UIManager.DS.TextMuted;
        private static readonly Color CMinerals = new Color(0.20f, 0.90f, 0.80f);
        private static readonly Color CEnergy = new Color(0.95f, 0.78f, 0.28f);
        private static readonly Color CAlloys = new Color(0.95f, 0.62f, 0.36f);

        private class DistrictCard
        {
            public DistrictType Type;
            public Text Count, Worked, Cost, Reason;
            public Button Build, Cancel;
            public Image Bg;
            public RectTransform Progress;
        }

        private class ActionButton
        {
            public Button Btn;
            public Text Label;
            public Image Bg;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void BindHost(Canvas modalCanvas) { _host = modalCanvas; }

        public void Open(PlanetData planet, StarSystem system)
        {
            if (planet == null) return;
            if (_host == null)
            {
                var modal = GameObject.Find("ModalCanvas");
                if (modal != null) _host = modal.GetComponent<Canvas>();
            }
            if (_host == null) return;
            if (_root == null) Build();

            _planet = planet;
            _system = system ?? planet.ParentSystem;

            PlanetOverviewModal.Instance?.Close();
            TooltipSystem.Hide();

            Refresh();
            BindHolo();

            _isOpen = true;
            LG.Show(_root);
            _root.transform.SetAsLastSibling();

            UIManager.Instance?.ShowModalDimPublic();
            MapModeController.HideGlobal();
            GalaxyMinimap.HideGlobal();
        }

        public void Close()
        {
            if (_root == null) return;
            _isOpen = false;
            LG.Hide(_root);
            TooltipSystem.Hide();

            PlanetHoloStudio.Instance?.Hide();
            MapModeController.ShowGlobal();
            UIManager.Instance?.HideModalDimPublic();
            GalaxyMinimap.ShowGlobal();
        }

        private void Update()
        {
            if (!_isOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 0.5f;
                Refresh();
            }
        }

        private void BindHolo()
        {
            var studio = PlanetHoloStudio.Instance;
            if (studio == null || _planet == null) return;

            Material src = null;
            var inst = SystemViewManager.Instance?.GetPlanetInstance(_planet);
            if (inst?.Renderer != null) src = inst.Renderer.sharedMaterial;

            studio.ShowPlanet(_planet, src);
            if (_planetImage != null) _planetImage.texture = studio.Target;
        }

        // ================================================================ Обновление

        private bool Surveyed => _system != null && _system.IsSurveyed;
        private bool Owned => _planet != null && _planet.IsPlayerOwned;

        private void Refresh()
        {
            if (_planet == null || _root == null) return;
            RefreshHeader();
            RefreshLeft();
            RefreshRight();
            RefreshDistricts();
            RefreshActions();
        }

        private void RefreshHeader()
        {
            _title.text = _planet.Name.ToUpper();
            var gov = LeaderManager.Instance?.GovernorOf(_planet);
            string govText = gov != null ? $"   ·   <color=#FFCC52>губернатор {gov.Name}, ур. {gov.Level}</color>" : "";
            _subtitle.text = $"{_planet.ClassDisplayName}   ·   система {(_system != null ? _system.Name : "?")}{govText}";

            int owner = _system != null ? _system.OwnerId : -1;
            Color oc = owner >= 0 ? FleetIndicator.OwnerColor(owner) : CMuted;
            string on = owner == 0 ? (UIManager.Instance?.SelectedFaction?.Name ?? "Ваша империя")
                      : owner > 0 ? AIEmpireManager.NameOf(owner)
                      : "Нейтральный фронтир";
            _ownerText.text = on;
            _ownerText.color = oc;
            _ownerIcon.color = oc;
            _ownerIcon.sprite = LGIcons.Get(owner >= 0 ? LGIcon.Starbase : LGIcon.Star);
        }

        private void RefreshLeft()
        {
            if (!Surveyed)
            {
                _surfaceText.text = $"<color={LGBuild.Hex(new Color(1f, 0.67f, 0.53f))}>Данные засекречены.</color>\n\nОтправьте научный корабль, чтобы изучить систему.";
                _depositText.text = "";
                _stationText.text = "";
                LGBuild.SetBar(_habBar, 0f);
                LGBuild.SetBar(_sizeBar, 0f);
                _habValue.text = "?";
                _sizeValue.text = "?";
                return;
            }

            int hab = _planet.HabitabilityPercent;
            Color habCol = hab >= 70 ? UIManager.DS.Green : hab >= 40 ? CGold : UIManager.DS.Red;
            LGBuild.SetBar(_habBar, hab / 100f, habCol);
            _habValue.text = $"<color={LGBuild.Hex(habCol)}>{hab}%</color>";
            LGBuild.SetBar(_sizeBar, _planet.BuiltDistricts / (float)Mathf.Max(1, _planet.MaxDistricts), UIManager.DS.NeonCyan);
            _sizeValue.text = $"{_planet.BuiltDistricts} / {_planet.MaxDistricts}";

            // Климат — детерминированно по имени планеты
            var rng = new System.Random(_planet.Name.GetHashCode());
            float gravity = 0.6f + (float)rng.NextDouble() * 0.9f;
            string atmo = gravity < 0.85f ? "разрежённая" : gravity > 1.25f ? "плотная" : "стандартная";
            int temperature = -40 + rng.Next(0, 90);
            int tilt = rng.Next(0, 45);
            string m = LGBuild.Hex(CMuted);
            _surfaceText.text =
                $"<color={m}>Гравитация</color>   {gravity:0.00} g\n" +
                $"<color={m}>Атмосфера</color>   {atmo}\n" +
                $"<color={m}>Средняя температура</color>   {temperature:+#;-#;0} °C\n" +
                $"<color={m}>Наклон оси</color>   {tilt}°\n" +
                $"<color={m}>Орбита</color>   {_planet.OrbitRadius:0.0} а.е.";

            var b = Bonuses();
            float fM = FactionMult(DistrictType.Mining), fE = FactionMult(DistrictType.Generator);
            _depositText.text =
                $"<color={LGBuild.Hex(CMinerals)}>Титан</color>   <b>{_planet.MineralDeposit}</b>   <color={m}>→ {_planet.MineralDeposit * fM * b.MineralsMult:0.#}/мес со станции</color>\n" +
                $"<color={LGBuild.Hex(CEnergy)}>Гелий-3</color>   <b>{_planet.EnergyDeposit}</b>   <color={m}>→ {_planet.EnergyDeposit * fE:0.#}/мес со станции</color>";
            _stationText.text = _planet.HasMiningStation
                ? $"<color={LGBuild.Hex(UIManager.DS.Green)}>Добывающий комплекс работает</color>"
                : _planet.MineralDeposit + _planet.EnergyDeposit > 0
                    ? $"<color={m}>Комплекс не построен — залежи простаивают</color>"
                    : $"<color={m}>Промышленных залежей нет</color>";
        }

        private void RefreshRight()
        {
            string m = LGBuild.Hex(CMuted);
            if (!Surveyed || _planet.Population <= 0)
            {
                _popBig.text = "—";
                _popText.text = !Surveyed ? "Нет данных"
                              : _planet.CanColonize ? "Планета не заселена. Основайте колонию, чтобы получить жителей, науку и районы."
                              : "Планета непригодна для жизни. Её можно разрабатывать только добывающим комплексом.";
                _growthText.text = "";
                LGBuild.SetBar(_housingBar, 0f);
                LGBuild.SetBar(_growthBar, 0f);
                _productionText.text = _planet.HasMiningStation
                    ? StationProduction()
                    : $"<color={m}>Производства нет</color>";
                return;
            }

            int pop = _planet.Population, housing = _planet.HousingCapacity, idle = _planet.IdlePops;
            bool crowded = pop >= housing;
            _popBig.text = $"{pop}<size=16><color={m}> / {housing}</color></size>";
            _popText.text =
                $"<color={m}>Работают</color>  {pop - idle}" +
                (idle > 0 ? $"     <color={LGBuild.Hex(CGold)}>Безработные  {idle}</color>" : $"     <color={m}>Безработных нет</color>") +
                $"\n<color={m}>Рабочих мест</color>  {_planet.WorkerSlots}     <color={m}>Жилья</color>  {housing}";
            LGBuild.SetBar(_housingBar, pop / (float)Mathf.Max(1, housing), crowded ? CGold : UIManager.DS.NeonCyan);

            float speed = _planet.PopGrowthBaseSpeedPctPerMonth;
            int months = _planet.MonthsToNextPop;
            LGBuild.SetBar(_growthBar, crowded ? 0f : Mathf.Clamp01(_planet.PopGrowthProgress / 100f), UIManager.DS.Green);
            _growthText.text = crowded
                ? $"<color={LGBuild.Hex(CGold)}>Рост остановлен — нет свободного жилья. Постройте городской район.</color>"
                : $"Рост <b>+{speed:0.#}%</b> в месяц   ·   новый житель через ~{months} мес.";

            var b = Bonuses();
            float minerals = _planet.ProducedMineralsPerMonth * FactionMult(DistrictType.Mining) * b.MineralsMult;
            float energy = _planet.ProducedEnergyPerMonth * FactionMult(DistrictType.Generator);
            float alloys = _planet.ProducedAlloysPerMonth * FactionMult(DistrictType.Industrial) * b.AlloysMult;
            if (_planet.HasMiningStation)
            {
                minerals += _planet.MineralDeposit * FactionMult(DistrictType.Mining) * b.MineralsMult;
                energy += _planet.EnergyDeposit * FactionMult(DistrictType.Generator);
            }
            var tm = TechnologyManager.Instance;
            float science = pop * TechnologyManager.SciencePerPop * (tm != null ? tm.GlobalResearchSpeedMultiplier : 1f);
            bool bankrupt = EconomyManager.Instance != null && EconomyManager.Instance.IsBankrupt && Owned;
            _productionText.text =
                ProdLine("Титан", CMinerals, minerals) +
                ProdLine("Гелий-3", CEnergy, energy) +
                ProdLine("Сплавы", CAlloys, alloys) +
                ProdLine("Наука", UIManager.DS.Green, science) +
                (bankrupt ? $"\n<color={LGBuild.Hex(UIManager.DS.Red)}>Банкротство: производство урезано вдвое</color>" : "");
        }

        private string StationProduction()
        {
            var b = Bonuses();
            return ProdLine("Титан", CMinerals, _planet.MineralDeposit * FactionMult(DistrictType.Mining) * b.MineralsMult) +
                   ProdLine("Гелий-3", CEnergy, _planet.EnergyDeposit * FactionMult(DistrictType.Generator));
        }

        private static string ProdLine(string name, Color c, float v)
            => $"<color={LGBuild.Hex(c)}>{name}</color>   " +
               (v > 0.01f ? $"<b>+{v:0.#}</b>" : $"<color={LGBuild.Hex(CMuted)}>0</color>") + "\n";

        private EmpireBonuses Bonuses() => EmpireBonuses.For(_system != null ? _system.OwnerId : 0);

        private float FactionMult(DistrictType t)
        {
            if (!Owned) return 1f;
            var eco = EconomyManager.Instance;
            if (eco == null) return 1f;
            return t == DistrictType.Mining ? eco.FactionMineralMult
                 : t == DistrictType.Generator ? eco.FactionEnergyMult
                 : t == DistrictType.Industrial ? eco.FactionAlloyMult : 1f;
        }

        private static ConstructionManager Builds => ConstructionManager.Instance;

        /// <summary>Первый заказ этого типа в очереди планеты (идёт сейчас или ждёт).</summary>
        private ConstructionJob FirstJob(JobKind kind, DistrictType? district = null)
        {
            if (Builds == null) return null;
            foreach (var j in Builds.QueueFor(_planet))
                if (j.Kind == kind && (district == null || j.District == district.Value)) return j;
            return null;
        }

        private ConstructionJob LastJob(JobKind kind, DistrictType district)
        {
            if (Builds == null) return null;
            ConstructionJob last = null;
            foreach (var j in Builds.QueueFor(_planet))
                if (j.Kind == kind && j.District == district) last = j;
            return last;
        }

        private void RefreshDistricts()
        {
            int pendingAll = _planet.PendingDistricts;
            var queue = Builds != null ? Builds.QueueFor(_planet) : new List<ConstructionJob>();
            string queueText = "";
            if (queue.Count > 0)
            {
                var cur = queue[0];
                string what = cur.Kind switch
                {
                    JobKind.District => DistrictName(cur.District).ToLower() + " район",
                    JobKind.MiningStation => "добывающий комплекс",
                    JobKind.Colony => "колония",
                    _ => "терраформинг"
                };
                queueText = $"   <color={LGBuild.Hex(CGold)}>строится {what} · {Mathf.CeilToInt(cur.DaysLeft)} дн." +
                            (queue.Count > 1 ? $" · в очереди ещё {queue.Count - 1}" : "") + "</color>";
            }
            _districtHeader.text = $"РАЙОНЫ   <color={LGBuild.Hex(CMuted)}>{_planet.BuiltDistricts}" +
                                   (pendingAll > 0 ? $"+{pendingAll}" : "") + $" / {_planet.MaxDistricts}</color>{queueText}";
            RebuildSlots();

            var eco = EconomyManager.Instance;
            foreach (var c in _cards)
            {
                int count = _planet.GetDistrictCount(c.Type);
                int worked = _planet.WorkedCount(c.Type);
                int pending = Builds != null ? Builds.PendingDistricts(_planet, c.Type) : 0;
                c.Count.text = pending > 0 ? $"×{count}<size=12><color={LGBuild.Hex(CGold)}> +{pending}</color></size>" : $"×{count}";

                var job = FirstJob(JobKind.District, c.Type);
                if (job != null)
                {
                    float days = Builds.DaysUntilDone(job);
                    bool active = queue.Count > 0 && queue[0] == job;
                    c.Worked.text = active ? $"строится · готов через {Mathf.CeilToInt(days)} дн." : $"в очереди · ~{Mathf.CeilToInt(days)} дн.";
                    c.Worked.color = CGold;
                    LGBuild.SetBar(c.Progress, active ? job.Progress : 0f, CGold);
                }
                else
                {
                    c.Worked.text = c.Type == DistrictType.Urban
                        ? $"жильё +{count * DistrictInfo.HousingPopCapacity(c.Type)}"
                        : count > 0 ? $"работают {worked} из {count}" : "не построены";
                    c.Worked.color = worked < count && c.Type != DistrictType.Urban ? CGold : CMuted;
                    LGBuild.SetBar(c.Progress, 0f);
                }
                c.Progress.parent.parent.gameObject.SetActive(job != null);

                // Кнопка отмены последнего заказа этого типа
                bool canCancel = pending > 0 && Owned;
                c.Cancel.gameObject.SetActive(canCancel);
                var brt = (RectTransform)c.Build.transform;
                brt.offsetMax = new Vector2(canCancel ? -48 : -10, 38);

                float mc = DistrictInfo.MineralsCost(c.Type), ac = DistrictInfo.AlloysCost(c.Type);
                bool canPay = eco != null && eco.Minerals >= mc && eco.Alloys >= ac;
                c.Cost.text = $"<color={(eco != null && eco.Minerals >= mc ? LGBuild.Hex(CMinerals) : LGBuild.Hex(UIManager.DS.Red))}>{mc:0} титана</color>  ·  " +
                              $"<color={(eco != null && eco.Alloys >= ac ? LGBuild.Hex(CAlloys) : LGBuild.Hex(UIManager.DS.Red))}>{ac:0} сплавов</color>";

                string reason = !Surveyed ? "Нужна разведка"
                              : !Owned ? "Не ваша система"
                              : _planet.Population <= 0 ? "Сначала колония"
                              : _planet.BuiltDistricts + pendingAll >= _planet.MaxDistricts ? "Нет места"
                              : queue.Count >= ConstructionManager.MaxPlanetQueue ? "Очередь заполнена"
                              : !canPay ? "Не хватает ресурсов"
                              : null;
                c.Build.interactable = reason == null;
                c.Reason.text = reason ?? (queue.Count > 0 ? "В ОЧЕРЕДЬ" : $"ПОСТРОИТЬ · {ConstructionManager.DistrictDays(c.Type):0} ДН.");
                c.Reason.color = reason == null ? Color.white : CMuted;
            }
        }

        private int _slotsBuilt = -1, _slotsMax = -1;

        /// <summary>Сегментная полоска слотов районов: занятые — цветом района, свободные — пустые.</summary>
        private void RebuildSlots()
        {
            int pendingN = _planet.PendingDistricts;
            int key = _planet.BuiltDistricts * 100 + pendingN;
            if (_slotsBuilt == key && _slotsMax == _planet.MaxDistricts && _districtSlots.childCount > 0) return;
            _slotsBuilt = key;
            _slotsMax = _planet.MaxDistricts;
            LGBuild.Clear(_districtSlots);
            int max = Mathf.Max(1, _planet.MaxDistricts);
            float w = 1f / max;
            for (int i = 0; i < max; i++)
            {
                Color col = i < _planet.Districts.Count ? DistrictInfo.Color(_planet.Districts[i].Type)
                          : i < _planet.Districts.Count + pendingN ? new Color(CGold.r, CGold.g, CGold.b, 0.35f)
                          : new Color(1f, 1f, 1f, 0.08f);
                var seg = LGBuild.Panel(_districtSlots, "Slot", col);
                var rt = seg.rectTransform;
                rt.anchorMin = new Vector2(i * w, 0);
                rt.anchorMax = new Vector2((i + 1) * w, 1);
                rt.offsetMin = new Vector2(1.5f, 0);
                rt.offsetMax = new Vector2(-1.5f, 0);
                LG.Ignore(seg.gameObject);
            }
        }

        private void RefreshActions()
        {
            var eco = EconomyManager.Instance;
            bool surveyed = Surveyed, owned = Owned;

            ConstructionJob mineJob = FirstJob(JobKind.MiningStation), colJob = FirstJob(JobKind.Colony), terJob = FirstJob(JobKind.Terraform);

            // Добывающий комплекс
            if (_planet.HasMiningStation) SetAction(_mine, false, LGIcon.Check, "КОМПЛЕКС РАБОТАЕТ", true);
            else if (mineJob != null) SetAction(_mine, true, LGIcon.Clock, $"СТРОИТСЯ · {Mathf.CeilToInt(Builds.DaysUntilDone(mineJob))} ДН.  ·  ОТМЕНИТЬ");
            else if (_planet.MineralDeposit + _planet.EnergyDeposit <= 0) SetAction(_mine, false, LGIcon.Minerals, "ЗАЛЕЖЕЙ НЕТ");
            else if (!surveyed) SetAction(_mine, false, LGIcon.Lock, "НУЖНА РАЗВЕДКА");
            else if (!owned) SetAction(_mine, false, LGIcon.Lock, "НЕ ВАША СИСТЕМА");
            else
            {
                bool can = eco != null && eco.Minerals >= FleetManager.MiningStationMinerals;
                SetAction(_mine, can, LGIcon.Industry, $"ДОБЫВАЮЩИЙ КОМПЛЕКС  ·  {FleetManager.MiningStationMinerals:0} титана · {ConstructionManager.MiningStationDays:0} дн.");
            }

            // Колония
            if (_planet.Population > 0) SetAction(_colony, false, LGIcon.Check, "КОЛОНИЯ ОСНОВАНА", true);
            else if (colJob != null) SetAction(_colony, true, LGIcon.Clock, $"КОЛОНИСТЫ В ПУТИ · {Mathf.CeilToInt(Builds.DaysUntilDone(colJob))} ДН.  ·  ОТМЕНИТЬ");
            else if (!surveyed) SetAction(_colony, false, LGIcon.Lock, "НУЖНА РАЗВЕДКА");
            else if (!owned) SetAction(_colony, false, LGIcon.Lock, "НЕ ВАША СИСТЕМА");
            else if (!_planet.CanColonize) SetAction(_colony, false, LGIcon.Close, "НЕПРИГОДНА ДЛЯ ЖИЗНИ");
            else
            {
                bool can = eco != null && eco.CanAfford(0f, PlanetData.ColonyMineralsCost, PlanetData.ColonyAlloysCost, PlanetData.ColonyInfluenceCost);
                SetAction(_colony, can, LGIcon.Population,
                    $"ОСНОВАТЬ КОЛОНИЮ  ·  {PlanetData.ColonyMineralsCost:0} тит., {PlanetData.ColonyAlloysCost:0} спл., {PlanetData.ColonyInfluenceCost:0} вл. · {ConstructionManager.ColonyDays:0} дн.");
            }

            // Терраформинг
            if (terJob != null) SetAction(_terra, true, LGIcon.Clock, $"ТЕРРАФОРМИНГ · {Mathf.CeilToInt(Builds.DaysUntilDone(terJob))} ДН.  ·  ОТМЕНИТЬ");
            else if (!_planet.CanTerraform) SetAction(_terra, false, LGIcon.Close, "ТЕРРАФОРМИНГ НЕВОЗМОЖЕН");
            else if (!surveyed) SetAction(_terra, false, LGIcon.Lock, "НУЖНА РАЗВЕДКА");
            else if (!owned) SetAction(_terra, false, LGIcon.Lock, "НЕ ВАША СИСТЕМА");
            else if (_planet.HabitabilityPercent >= 70) SetAction(_terra, false, LGIcon.Check, "ПЛАНЕТА УЖЕ ПРИГОДНА", true);
            else
            {
                bool can = eco != null && eco.EnergyCredits >= 120f && eco.Minerals >= 90f && eco.Influence >= 10f;
                SetAction(_terra, can, LGIcon.Planet, $"ТЕРРАФОРМИРОВАТЬ  ·  120 гел., 90 тит., 10 вл. · {ConstructionManager.TerraformDays:0} дн.");
            }
        }

        private void SetAction(ActionButton a, bool interactable, LGIcon icon, string label, bool done = false)
        {
            a.Btn.interactable = interactable;
            a.Label.text = label;
            var ic = a.Btn.GetComponentInChildren<IconLabelRef>();
            if (ic != null && ic.Icon != null)
            {
                ic.Icon.sprite = LGIcons.Get(icon);
                ic.Icon.color = done ? UIManager.DS.Green : Color.white;
            }
        }

        // ================================================================ Действия

        private void OnBuildDistrict(DistrictType t)
        {
            if (_planet == null || _planet.Population <= 0) return;
            if (_planet.TryBuildDistrict(t))
            {
                Refresh();
            }
        }

        private void OnCancelDistrict(DistrictType t)
        {
            var job = LastJob(JobKind.District, t);
            if (job == null) return;
            Builds.Cancel(job);   // звук отмены — в ConstructionManager
            Refresh();
        }

        /// <summary>Отменить идущую стройку станции/колонии/терраформинга (кнопка действия во время стройки).</summary>
        private bool TryCancel(JobKind kind)
        {
            var job = FirstJob(kind);
            if (job == null) return false;
            Builds.Cancel(job);
            NotificationCenter.Show("Заказ отменён", "Ресурсы возвращены", NotificationCenter.Kind.Info, 3f);
            Refresh();
            return true;
        }

        private void OnMine()
        {
            if (_planet == null) return;
            if (TryCancel(JobKind.MiningStation)) return;
            if (FleetManager.Instance != null && FleetManager.Instance.BuildMiningStationOnPlanet(_planet))
            {
                Refresh();
                BindHolo();
            }
        }

        private void OnColony()
        {
            if (_planet == null || TryCancel(JobKind.Colony)) return;
            if (_planet.TryFoundColony())
            {
                NotificationCenter.Show("Колонисты отправлены", $"{_planet.Name} · колония через {ConstructionManager.ColonyDays:0} дн.", NotificationCenter.Kind.Info, 4f);
                Refresh();
            }
        }

        private void OnTerra()
        {
            if (_planet == null || TryCancel(JobKind.Terraform)) return;
            if (_planet.TryStartTerraform())
            {
                NotificationCenter.Show("Терраформинг начат", $"{_planet.Name} · {ConstructionManager.TerraformDays:0} дн.", NotificationCenter.Kind.Info, 4f);
                Refresh();
            }
        }

        // ================================================================ Построение

        private void Build()
        {
            var rt = LGBuild.Rect(_host.transform, "PlanetFocusOverlay");
            rt.Stretch();
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.006f, 0.018f, 0.028f, 1f);
            bg.raycastTarget = true;
            LG.Scrim(_root).FillMultiplier = 1.9f;
            var motion = LG.Motion(_root, LGAppear.Kind.Fade);
            motion.inDuration = 0.4f;
            motion.outDuration = 0.25f;

            BuildHeader(rt);
            BuildPlanet(rt);
            BuildLeft(rt);
            BuildRight(rt);
            BuildBottom(rt);

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        private void BuildHeader(RectTransform rt)
        {
            var close = LGBuild.Button(rt, "Close", new Color(0.10f, 0.28f, 0.32f), UIManager.DS.NeonCyan, Close, LGIcon.Back, "ЗАКРЫТЬ  (ESC)", 12);
            ((RectTransform)close.transform).At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -36), new Vector2(180, 40));
            LG.Motion(close.gameObject, LGAppear.Kind.Fade).delay = 0.12f;

            _title = LGBuild.Label(rt, "", 30, UIManager.DS.NeonCyan, TextAnchor.UpperCenter, bold: true);
            _title.rectTransform.TopBand(30, 40, 260, 260);
            _subtitle = LGBuild.Label(rt, "", 13, CMuted, TextAnchor.UpperCenter);
            _subtitle.rectTransform.TopBand(74, 20, 260, 260);

            var sep = LGBuild.Panel(rt, "Sep", new Color(0.18f, 0.65f, 0.60f, 0.45f));
            sep.rectTransform.anchorMin = new Vector2(0.3f, 1);
            sep.rectTransform.anchorMax = new Vector2(0.7f, 1);
            sep.rectTransform.pivot = new Vector2(0.5f, 1);
            sep.rectTransform.sizeDelta = new Vector2(0, 1);
            sep.rectTransform.anchoredPosition = new Vector2(0, -102);
            LG.Line(sep.gameObject, hairline: true);

            var chip = LGBuild.Panel(rt, "Owner", UIManager.DS.BgSlot);
            // Под кнопкой «Закрыть» — справа сверху всплывают уведомления
            chip.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -84), new Vector2(300, 34));
            LG.Chip(chip.gameObject, new Color(1f, 1f, 1f, 0.2f));
            _ownerIcon = LGIcons.Create(chip.transform, LGIcon.Starbase, 18, FleetIndicator.OwnColor);
            _ownerIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(18, 18));
            _ownerText = LGBuild.Label(chip.transform, "", 12, Color.white, TextAnchor.MiddleLeft, bold: true);
            _ownerText.rectTransform.Stretch(42, 0, 12, 0);
        }

        private void BuildPlanet(RectTransform rt)
        {
            var maskRt = LGBuild.Rect(rt, "PlanetMask");
            maskRt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(PlanetSize, PlanetSize));
            var maskImg = maskRt.gameObject.AddComponent<Image>();
            maskImg.sprite = MakeCircleSprite(512);
            maskImg.raycastTarget = false;
            maskRt.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var host = LGBuild.Rect(maskRt, "PlanetImage");
            host.Stretch();
            _planetImage = host.gameObject.AddComponent<RawImage>();
            _planetImage.raycastTarget = true;
            host.gameObject.AddComponent<HoloDragCatcher>();

            var ring = LGBuild.Panel(rt, "PlanetRing", new Color(0, 0, 0, 0));
            ring.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(PlanetSize + 28f, PlanetSize + 28f));
            var ringFx = LG.Border(ring.gameObject, new Color(0.45f, 0.95f, 0.90f, 0.35f));
            ringFx.Radius = (PlanetSize + 28f) * 0.5f;
            ringFx.GlowMultiplier = 1.4f;

            var hint = LGBuild.Label(rt, "Зажмите ЛКМ на планете и ведите мышью — вращение", 11, CMuted, TextAnchor.MiddleCenter);
            hint.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60 - PlanetSize * 0.5f - 30f), new Vector2(600, 20));
            hint.fontStyle = FontStyle.Italic;
        }

        private RectTransform Column(RectTransform parent, string name, bool left)
        {
            var col = LGBuild.Rect(parent, name);
            col.anchorMin = new Vector2(left ? 0 : 1, 0);
            col.anchorMax = new Vector2(left ? 0 : 1, 1);
            col.pivot = new Vector2(left ? 0 : 1, 0.5f);
            col.offsetMin = new Vector2(left ? 40 : -440, 300);
            col.offsetMax = new Vector2(left ? 440 : -40, -120);
            return col;
        }

        private RectTransform Card(RectTransform parent, string name, float top, float height, LGIcon icon, string title, Color accent, LGAppear.Kind appear, float delay)
        {
            var card = LGBuild.Panel(parent, name, UIManager.DS.BgDeep);
            card.rectTransform.TopBand(top, height);
            LG.Glass(card.gameObject, 20f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.32f));
            var mo = LG.Motion(card.gameObject, appear);
            mo.delay = delay;
            mo.distance = 30f;

            var head = LGBuild.Rect(card.rectTransform, "Head");
            head.TopBand(14, 20, 18, 18);
            var ic = LGIcons.Create(head, icon, 16, accent);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16));
            var t = LGBuild.Label(head, title, 12, accent, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.offsetMin = new Vector2(24, 0);
            return card.rectTransform;
        }

        private static RectTransform BarRow(RectTransform card, float top, string label, Color col, out Text value)
        {
            var row = LGBuild.Rect(card, "BarRow");
            row.TopBand(top, 30, 18, 18);
            LGBuild.Label(row, label, 11, CMuted, TextAnchor.UpperLeft);
            value = LGBuild.Label(row, "", 12, UIManager.DS.TextPrimary, TextAnchor.UpperRight, bold: true);
            var host = LGBuild.Rect(row, "Bar");
            host.anchorMin = new Vector2(0, 0);
            host.anchorMax = new Vector2(1, 0);
            host.offsetMin = new Vector2(0, 0);
            host.offsetMax = new Vector2(0, 8);
            return LGBuild.Bar(host, col, 0f, 6f);
        }

        private static Text Body(RectTransform card, float top, float bottom, int size = 12)
        {
            var t = LGBuild.Label(card, "", size, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            t.rectTransform.Stretch(18, bottom, 18, top);
            t.lineSpacing = 1.3f;
            return t;
        }

        private void BuildLeft(RectTransform rt)
        {
            var col = Column(rt, "Left", true);
            var surface = Card(col, "Surface", 0, 300, LGIcon.Planet, "ПОВЕРХНОСТЬ", CGold, LGAppear.Kind.SlideLeft, 0.06f);
            _habBar = BarRow(surface, 44, "Пригодность для жизни", UIManager.DS.Green, out _habValue);
            _sizeBar = BarRow(surface, 82, "Районы (размер планеты)", UIManager.DS.NeonCyan, out _sizeValue);
            _surfaceText = Body(surface, 124, 12);

            var deposits = Card(col, "Deposits", 312, 180, LGIcon.Minerals, "ПРИРОДНЫЕ ЗАЛЕЖИ", CMinerals, LGAppear.Kind.SlideLeft, 0.12f);
            _depositText = Body(deposits, 44, 40);
            _stationText = LGBuild.Label(deposits, "", 11, CMuted, TextAnchor.LowerLeft, wrap: true);
            _stationText.rectTransform.Stretch(18, 14, 18, 0);
        }

        private void BuildRight(RectTransform rt)
        {
            var col = Column(rt, "Right", false);
            var pop = Card(col, "Population", 0, 300, LGIcon.Population, "НАСЕЛЕНИЕ", UIManager.DS.NeonCyan, LGAppear.Kind.SlideRight, 0.06f);
            _popBig = LGBuild.Label(pop, "", 34, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            _popBig.rectTransform.Stretch(18, 0, 18, 42);

            var hHost = LGBuild.Rect(pop, "Housing");
            hHost.TopBand(92, 8, 18, 18);
            _housingBar = LGBuild.Bar(hHost, UIManager.DS.NeonCyan, 0f, 6f);
            _popText = Body(pop, 110, 90, 11);

            var gLabel = LGBuild.Label(pop, "Рост населения", 11, CMuted, TextAnchor.LowerLeft);
            gLabel.rectTransform.Stretch(18, 70, 18, 0);
            var gHost = LGBuild.Rect(pop, "Growth");
            gHost.anchorMin = new Vector2(0, 0);
            gHost.anchorMax = new Vector2(1, 0);
            gHost.offsetMin = new Vector2(18, 56);
            gHost.offsetMax = new Vector2(-18, 64);
            _growthBar = LGBuild.Bar(gHost, UIManager.DS.Green, 0f, 6f);
            _growthText = LGBuild.Label(pop, "", 11, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            _growthText.rectTransform.anchorMin = new Vector2(0, 0);
            _growthText.rectTransform.anchorMax = new Vector2(1, 0);
            _growthText.rectTransform.offsetMin = new Vector2(18, 8);
            _growthText.rectTransform.offsetMax = new Vector2(-18, 50);

            var prod = Card(col, "Production", 312, 180, LGIcon.Industry, "ПРОИЗВОДСТВО В МЕСЯЦ", CGold, LGAppear.Kind.SlideRight, 0.12f);
            _productionText = Body(prod, 44, 10, 13);
        }

        private void BuildBottom(RectTransform rt)
        {
            var panel = LGBuild.Panel(rt, "Districts", UIManager.DS.BgDeep);
            panel.rectTransform.anchorMin = new Vector2(0.5f, 0);
            panel.rectTransform.anchorMax = new Vector2(0.5f, 0);
            panel.rectTransform.pivot = new Vector2(0.5f, 0);
            panel.rectTransform.sizeDelta = new Vector2(1180, 262);
            panel.rectTransform.anchoredPosition = new Vector2(0, 24);
            LG.Glass(panel.gameObject, 24f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.38f));
            var mo = LG.Motion(panel.gameObject, LGAppear.Kind.SlideUp);
            mo.delay = 0.18f;
            mo.distance = 30f;
            var p = panel.rectTransform;

            var head = LGBuild.Rect(p, "Head");
            head.TopBand(14, 22, 20, 20);
            var hic = LGIcons.Create(head, LGIcon.Construction, 16, CGold);
            hic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16));
            _districtHeader = LGBuild.Label(head, "", 13, CGold, TextAnchor.MiddleLeft, bold: true);
            _districtHeader.rectTransform.offsetMin = new Vector2(24, 0);
            _districtSlots = LGBuild.Rect(head, "Slots");
            _districtSlots.anchorMin = new Vector2(0.35f, 0.5f);
            _districtSlots.anchorMax = new Vector2(1f, 0.5f);
            _districtSlots.sizeDelta = new Vector2(0, 8);

            var cards = LGBuild.Rect(p, "Cards");
            cards.Stretch(16, 62, 16, 46);
            DistrictType[] order = { DistrictType.Urban, DistrictType.Mining, DistrictType.Generator, DistrictType.Industrial };
            for (int i = 0; i < order.Length; i++)
            {
                var cardRt = LGBuild.Rect(cards, "Card");
                cardRt.Column(i / 4f, (i + 1) / 4f, i == 0 ? 0 : 6, i == 3 ? 0 : 6);
                _cards.Add(BuildDistrictCard(cardRt, order[i]));
            }

            var actions = LGBuild.Rect(p, "Actions");
            actions.anchorMin = new Vector2(0, 0);
            actions.anchorMax = new Vector2(1, 0);
            actions.pivot = new Vector2(0.5f, 0);
            actions.offsetMin = new Vector2(16, 12);
            actions.offsetMax = new Vector2(-16, 52);
            _mine = BuildAction(actions, 0f, 0.3f, UIManager.DS.BtnSuccess, OnMine);
            _colony = BuildAction(actions, 0.3f, 0.7f, UIManager.DS.BtnPrimary, OnColony);
            _terra = BuildAction(actions, 0.7f, 1f, new Color(0.22f, 0.20f, 0.36f), OnTerra);
        }

        private DistrictCard BuildDistrictCard(RectTransform rt, DistrictType type)
        {
            Color dc = DistrictInfo.Color(type);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(dc.r * 0.12f, dc.g * 0.12f, dc.b * 0.12f, 0.95f);
            bg.raycastTarget = false;
            LG.Platter(rt.gameObject, 16f).SetRim(new Color(dc.r, dc.g, dc.b, 0.45f));

            LGIcon icon = type switch
            {
                DistrictType.Urban => LGIcon.Housing,
                DistrictType.Mining => LGIcon.Minerals,
                DistrictType.Generator => LGIcon.Energy,
                _ => LGIcon.Alloys
            };
            var badge = LGBuild.Panel(rt, "Badge", new Color(dc.r * 0.3f, dc.g * 0.3f, dc.b * 0.3f));
            badge.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -12), new Vector2(40, 40));
            LG.Platter(badge.gameObject, 20f).FillMultiplier = 2f;
            LGIcons.Create(badge.transform, icon, 22, dc);

            var name = LGBuild.Label(rt, DistrictName(type), 12, Color.white, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.Stretch(62, 0, 50, 14);
            var count = LGBuild.Label(rt, "", 18, dc, TextAnchor.UpperRight, bold: true);
            count.rectTransform.Stretch(0, 0, 14, 10);
            var effect = LGBuild.Label(rt, DistrictEffect(type), 10, CMuted, TextAnchor.UpperLeft, wrap: true);
            effect.rectTransform.Stretch(62, 0, 12, 32);
            var worked = LGBuild.Label(rt, "", 10, CMuted, TextAnchor.UpperLeft);
            worked.rectTransform.Stretch(12, 0, 12, 62);
            var cost = LGBuild.Label(rt, "", 10, Color.white, TextAnchor.UpperLeft);
            cost.rectTransform.Stretch(12, 0, 12, 78);

            var btn = LGBuild.Button(rt, "Build", new Color(dc.r * 0.45f, dc.g * 0.45f, dc.b * 0.45f), new Color(dc.r, dc.g, dc.b, 0.75f),
                                     () => OnBuildDistrict(type), LGIcon.Construction, "ПОСТРОИТЬ", 11);
            var brt = (RectTransform)btn.transform;
            brt.anchorMin = new Vector2(0, 0);
            brt.anchorMax = new Vector2(1, 0);
            brt.pivot = new Vector2(0.5f, 0);
            brt.offsetMin = new Vector2(10, 8);
            brt.offsetMax = new Vector2(-10, 38);
            var cancel = LGBuild.Button(rt, "Cancel", UIManager.DS.BtnDanger, UIManager.DS.Red, () => OnCancelDistrict(type), LGIcon.Close, null, 10, 12f);
            var crt = (RectTransform)cancel.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1, 0);
            crt.pivot = new Vector2(1, 0);
            crt.sizeDelta = new Vector2(32, 30);
            crt.anchoredPosition = new Vector2(-10, 8);
            TooltipHelper.Attach(cancel.gameObject, "<b>Отменить</b>\nПоследний заказанный район этого типа. Ресурсы вернутся полностью.");
            cancel.gameObject.SetActive(false);

            var progHost = LGBuild.Rect(rt, "ProgressHost");
            progHost.anchorMin = new Vector2(0, 0);
            progHost.anchorMax = new Vector2(1, 0);
            progHost.offsetMin = new Vector2(12, 42);
            progHost.offsetMax = new Vector2(-12, 48);
            var progress = LGBuild.Bar(progHost, CGold, 0f, 4f);

            TooltipHelper.Attach(btn.gameObject,
                $"<b>{DistrictName(type)}</b>\n{DistrictEffect(type)}\n\n" +
                $"Строительство: {ConstructionManager.DistrictDays(type):0} дн. (на планете — одна стройка за раз)\n" +
                $"Стоимость: {DistrictInfo.MineralsCost(type)} титана, {DistrictInfo.AlloysCost(type)} сплавов\n" +
                "<color=#8AA2A8>Каждый район — одно рабочее место. Производство идёт, только если на нём работают жители.</color>");

            return new DistrictCard
            {
                Type = type, Count = count, Worked = worked, Cost = cost, Bg = bg, Build = btn, Cancel = cancel, Progress = progress,
                Reason = btn.GetComponentInChildren<Text>()
            };
        }

        private ActionButton BuildAction(RectTransform parent, float x0, float x1, Color tint, System.Action onClick)
        {
            var b = LGBuild.Button(parent, "Action", tint, new Color(Mathf.Min(1, tint.r * 2.4f), Mathf.Min(1, tint.g * 2.4f), Mathf.Min(1, tint.b * 2.4f), 0.7f),
                                   onClick, LGIcon.Check, " ", 11);
            ((RectTransform)b.transform).Column(x0, x1, x0 > 0 ? 5 : 0, x1 < 1 ? 5 : 0);
            var label = b.GetComponentInChildren<Text>();
            var refs = b.gameObject.AddComponent<IconLabelRef>();
            refs.Icon = label.transform.parent.GetComponentInChildren<Image>();
            return new ActionButton { Btn = b, Label = label, Bg = b.GetComponent<Image>() };
        }

        private static string DistrictName(DistrictType t) => t switch
        {
            DistrictType.Urban => "Городской",
            DistrictType.Mining => "Горнодобывающий",
            DistrictType.Generator => "Энергетический",
            _ => "Промышленный"
        };

        private static string DistrictEffect(DistrictType t) => t switch
        {
            DistrictType.Urban => "+2 жилья и рабочее место. Без жилья рост встаёт.",
            DistrictType.Mining => "+4 титана в месяц за каждый занятый район.",
            DistrictType.Generator => "+4 гелия-3 в месяц — оплачивает содержание флота и форпостов.",
            _ => "+3 сплава в месяц — на корабли, форпосты и районы."
        };

        private static Sprite MakeCircleSprite(int res)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Vector2 c = new Vector2(res * 0.5f, res * 0.5f);
            float r = res * 0.5f;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = Mathf.Clamp01(1f - (d - (r - 2f)));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
        }
    }

    /// <summary>Ссылка на иконку внутри кнопки (чтобы менять её вместе с подписью).</summary>
    public class IconLabelRef : MonoBehaviour
    {
        public Image Icon;
    }
}
