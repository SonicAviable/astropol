using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Generation;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Полноэкранный экран планеты — главный экран управления колонией, в «кинематографичной» подаче.
    ///   • слева — крупная вращающаяся планета с мягким краем и подсветкой атмосферы цвета её мира
    ///     (шейдер UIPlanetDisc), тонкие медленно вращающиеся орбитальные кольца; дуги-шкалы у планеты:
    ///     слева — население (жители / жильё), справа — рост до следующего жителя;
    ///   • поверх планеты слева сверху — население и рост, слева снизу — досье планеты
    ///     (пригодность, размер, залежи сегментными шкалами и климат);
    ///   • справа сверху — плитки производства за месяц и занятость;
    ///   • справа в центре — районы сеткой 2×2: эмблема, эффект, фигурки рабочих
    ///     (светятся занятые, тусклые — свободные места, золотые — строятся), стоимость и постройка;
    ///     в шапке районов — приоритет колонии и автостройка;
    ///   • справа снизу — очередь строительства (с отменой любого заказа) и проекты планеты:
    ///     добывающий комплекс, колония, терраформинг.
    /// Переключение между колониями — стрелки в шапке или Tab / Shift+Tab.
    /// Открывается и из системы, и прямо с карты галактики кликом по планете.
    /// </summary>
    public class PlanetFocusOverlay : MonoBehaviour
    {
        public static PlanetFocusOverlay Instance { get; private set; }
        public bool IsOpen => _isOpen;

        private Canvas _host;
        private GameObject _root;
        private RectTransform _rootRt;

        // Планета и её «сцена»
        private RectTransform _stage;
        private RawImage _planetImage;
        private Image _halo, _haloOuter, _ringDash, _ringTicks, _popFill, _growFill;
        private Image _worldTint;
        private Material _discMat;

        // Дуги-шкалы вокруг планеты: население — слева (9 часов), рост — справа (3 часа)
        private const float PopArc = 0.30f, GrowArc = 0.20f;

        // Шапка
        private Text _title, _subtitle, _ownerText;
        private Image _ownerIcon, _titleAccent;
        private GameObject _govChip;
        private LeaderThumb _govFace;
        private Image _govIcon;
        private Text _govText;
        private GameObject _switcher;
        private Text _switchText;

        // Население (поверх планеты)
        private Text _popBig, _popCaption, _growthText;

        // Досье
        private Seg[] _dossier;            // пригодность, размер, титан, гелий-3
        private Text _climate, _stationText;

        // Плитки производства
        private Tile[] _tiles;             // титан, гелий-3, сплавы, наука, занятость
        private Text _bankruptText;

        // Районы
        private Text _districtHeader, _queueHint;
        private RectTransform _districtSlots;
        private readonly List<DistrictCard> _cards = new List<DistrictCard>();
        private GameObject _colonyControls;
        private Image[] _focusBg;
        private Image _autoBg, _autoIcon;
        private Text _autoText;

        // Очередь и проекты
        private QueueRow[] _queueRows;
        private GameObject _queueEmptyGo;
        private Text _queueEmpty;
        private Button _suggestBtn;
        private Text _suggestText;
        private DistrictType? _suggest;
        private ActionButton _mine, _colony, _terra, _robots, _gene;
        private Button _buildMenuBtn;
        private const int ProjectCount = 5;

        // Общество: стабильность и виды
        private GameObject _society;
        private Text _stabValue, _stabEffect, _compCaption, _propText;
        private RectTransform _stabBar;
        private Image[] _compSegs;
        private Text[] _legend;
        private TooltipTrigger _stabTip, _compTip, _propTip;
        private Button _propBtn;
        private Image _propBg;

        private PlanetData _planet;
        private StarSystem _system;
        private bool _isOpen;
        private float _refreshTimer, _anim;
        private readonly List<PlanetData> _colonies = new List<PlanetData>();
        private GalaxyGenerator _gen;

        private static readonly Color CGold = UIManager.DS.Gold;
        private static readonly Color CMuted = UIManager.DS.TextMuted;
        private static readonly Color CCyan = UIManager.DS.NeonCyan;
        private static readonly Color CMinerals = new Color(0.20f, 0.90f, 0.80f);
        private static readonly Color CEnergy = new Color(0.95f, 0.78f, 0.28f);
        private static readonly Color CAlloys = new Color(0.95f, 0.62f, 0.36f);
        private static readonly Color CScience = new Color(0.50f, 0.92f, 0.62f);
        private static readonly Color CPanel = new Color(0.020f, 0.045f, 0.065f, 0.88f);
        private static readonly Color CSegOff = new Color(1f, 1f, 1f, 0.09f);

        // Раскладка (в единицах канваса 1920×1080)
        private const float Pad = 40f, Gap = 14f, HeaderH = 104f, RightX = 0.52f;
        private const float TilesTop = 116f, TilesH = 92f, DistrictsTop = TilesTop + TilesH + Gap;
        private const float LowerShare = 0.36f;   // доля высоты под очередь и проекты

        // ================================================================ Типы

        private class DistrictCard
        {
            public DistrictType Type;
            public Text Count, Status, Reason;
            public ResLine MinCost, AlloyCost;
            public Button Build, Cancel;
            public Image BuildBg;
            public Color BuildTint;
            public RectTransform Progress, Workers;
            public int WorkerKey = -1;
            public TooltipTrigger Tip;
        }

        /// <summary>Строка «иконка ресурса + текст» (обычный Text не умеет картинки внутри строки).</summary>
        private class ResLine
        {
            public GameObject Go;
            public Image Icon;
            public Text Text;

            public void Set(string text, Color? textColor = null)
            {
                Go.SetActive(true);
                Text.text = text;
                Text.color = textColor ?? UIManager.DS.TextPrimary;
            }
        }

        private class Tile
        {
            public Text Value, Caption;
        }

        private class Seg
        {
            public Text Value;
            public Image[] Cells;
        }

        private class QueueRow
        {
            public GameObject Go;
            public Image Icon;
            public Text Name, Days;
            public RectTransform Progress;
            public Button Cancel, Buy;
            public Image BuyBg;
            public Text BuyText;
            public TooltipTrigger BuyTip;
            public ConstructionJob Job;
        }

        private class ActionButton
        {
            public Button Btn;
            public Image Bg, Icon, Badge;
            public Text Status;
            public Color Tint, Accent;
            public TooltipTrigger Tip;
        }

        // ================================================================ Жизненный цикл

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
            UIManager.Instance?.SetOverlayMode(true);
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
            UIManager.Instance?.SetOverlayMode(false);
            GalaxyMinimap.ShowGlobal();
        }

        private void Update()
        {
            if (!_isOpen) return;
            if (BuildMenuModal.IsOpenNow) return;   // меню строительства поверх — клавиши его
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.B) && Owned) { BuildMenuModal.Open(_planet); return; }
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                bool back = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                StepColony(back ? -1 : 1);
                return;
            }

            float dt = Time.unscaledDeltaTime;
            _anim += dt;
            LayoutStage();
            AnimateStage();

            _refreshTimer -= dt;
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

        // ================================================================ Сцена планеты

        /// <summary>Сцена планеты — квадрат: 62% высоты экрана, но не шире 40% ширины.</summary>
        private void LayoutStage()
        {
            if (_stage == null || _rootRt == null) return;
            var r = _rootRt.rect;
            float s = Mathf.Min(r.height * 0.62f, r.width * 0.40f);
            if (Mathf.Abs(_stage.sizeDelta.x - s) > 0.5f) _stage.sizeDelta = new Vector2(s, s);
        }

        /// <summary>Кольца медленно вращаются навстречу друг другу, атмосфера чуть «дышит».</summary>
        private void AnimateStage()
        {
            if (_ringDash != null) _ringDash.rectTransform.localRotation = Quaternion.Euler(0, 0, _anim * 4f);
            if (_ringTicks != null) _ringTicks.rectTransform.localRotation = Quaternion.Euler(0, 0, -_anim * 2.2f);
            if (_halo != null)
            {
                float k = 1f + Mathf.Sin(_anim * 0.8f) * 0.015f;
                _halo.rectTransform.localScale = new Vector3(k, k, 1f);
            }
        }

        /// <summary>Цвет мира: свечение атмосферы, акценты шапки и колец.</summary>
        private static Color WorldColor(PlanetType t) => t switch
        {
            PlanetType.Continental => new Color(0.32f, 0.86f, 0.92f),
            PlanetType.Desert => new Color(0.98f, 0.72f, 0.38f),
            PlanetType.Molten => new Color(1.00f, 0.42f, 0.20f),
            PlanetType.GasGiant => new Color(0.74f, 0.58f, 1.00f),
            _ => new Color(0.70f, 0.76f, 0.84f)
        };

        // ================================================================ Обновление

        private bool Surveyed => _system != null && _system.IsSurveyed;
        private bool Owned => _planet != null && _planet.IsPlayerOwned;

        private void Refresh()
        {
            if (_planet == null || _root == null) return;
            RefreshHeader();
            RefreshPopulation();
            RefreshSociety();
            RefreshDossier();
            RefreshTiles();
            RefreshDistricts();
            RefreshQueue();
            RefreshActions();
            RefreshColonyControls();
        }

        private void RefreshHeader()
        {
            Color wc = WorldColor(_planet.Type);
            _title.text = PlanetNames.Title(_planet.Name);   // без капса: «Тубан b», а не «ТУБАН B»
            _subtitle.text = Loc.F("{0}   <color={1}>·   система</color>  {2}", _planet.ClassDisplayName.ToUpper(), LGBuild.Hex(CMuted), (_system != null ? _system.Name : "?"));
            _titleAccent.color = new Color(wc.r, wc.g, wc.b, 0.9f);

            var gov = LeaderManager.Instance?.GovernorOf(_planet);
            _govChip.SetActive(gov != null);
            if (gov != null)
            {
                bool face = _govFace.Set(gov, LeaderThumb.ClassColor(gov.Class));
                _govIcon.enabled = !face;
                _govText.text = Loc.F("<color=#8AA2A8>Губернатор</color>  {0} · ур. {1}", gov.Name, gov.Level);
            }

            int owner = _system != null ? _system.OwnerId : -1;
            Color oc = owner >= 0 ? FleetIndicator.OwnerColor(owner) : CMuted;
            string on = owner == 0 ? (UIManager.Instance?.SelectedFaction?.Name ?? Loc.T("Ваша империя"))
                      : owner > 0 ? AIEmpireManager.NameOf(owner)
                      : Loc.T("Нейтральный фронтир");
            _ownerText.text = on;
            _ownerText.color = oc;
            _ownerIcon.color = oc;
            _ownerIcon.sprite = LGIcons.Get(owner >= 0 ? LGIcon.Starbase : LGIcon.Star);

            // Атмосфера сцены — цветом мира
            _halo.color = new Color(wc.r, wc.g, wc.b, 0.20f);
            _haloOuter.color = new Color(wc.r, wc.g, wc.b, 0.05f);
            _ringDash.color = new Color(wc.r, wc.g, wc.b, 0.22f);
            _ringTicks.color = new Color(1f, 1f, 1f, 0.10f);
            _worldTint.color = new Color(wc.r * 0.2f, wc.g * 0.2f, wc.b * 0.2f, 0.10f);
            if (_discMat != null) _discMat.SetColor("_RimColor", Color.Lerp(wc, Color.white, 0.25f));
        }

        private void RefreshPopulation()
        {
            string m = LGBuild.Hex(CMuted);
            if (!Surveyed || _planet.Population <= 0)
            {
                _popBig.text = "—";
                _popCaption.text = Loc.T("ЖИТЕЛЕЙ НЕТ");
                _growthText.text = !Surveyed ? Loc.T("Нет данных — нужна разведка")
                                 : _planet.IsColonizing ? Loc.F("<color={0}>Колонисты в пути</color>", LGBuild.Hex(CGold))
                                 : _planet.CanColonize ? Loc.T("Планета пригодна для колонии")
                                 : Loc.T("Непригодна для жизни — только добыча");
                _popFill.fillAmount = 0f;
                _growFill.fillAmount = 0f;
                return;
            }

            int pop = _planet.Population, housing = _planet.HousingCapacity;
            bool crowded = pop >= housing;
            _popBig.text = $"{pop}<size=26><color={m}> / {housing}</color></size>";
            _popCaption.text = Loc.T("ЖИТЕЛИ  ·  ЖИЛЬЁ");
            _popFill.fillAmount = PopArc * Mathf.Clamp01(pop / (float)Mathf.Max(1, housing));
            _popFill.color = crowded ? CGold : CCyan;
            _growFill.fillAmount = crowded ? 0f : GrowArc * Mathf.Clamp01(_planet.PopGrowthProgress / 100f);

            float speed = _planet.PopGrowthBaseSpeedPctPerMonth;
            int months = _planet.MonthsToNextPop;
            _growthText.text = crowded
                ? Loc.F("<color={0}>Рост остановлен — нет жилья.\nПостройте городской район.</color>", LGBuild.Hex(CGold))
                : speed <= 0f && _planet.OrganicPops == 0
                ? Loc.F("<color={0}>Роботы не размножаются.\nНовых жителей даст только сборка.</color>", LGBuild.Hex(CGold))
                : speed <= 0f
                ? Loc.F("<color={0}>Рост остановлен — на планете бунт.</color>", LGBuild.Hex(UIManager.DS.Red))
                : Loc.F("Рост <b>+{0:0.#}%</b> в месяц\nновый житель через ~{1} мес.", speed, months);
        }

        private void RefreshDossier()
        {
            string m = LGBuild.Hex(CMuted);
            if (!Surveyed)
            {
                foreach (var s in _dossier) SetSeg(s, 0, "?", CMuted);
                _climate.text = Loc.F("<color={0}>Данные засекречены.\nОтправьте научный корабль.</color>", m);
                _stationText.text = "";
                return;
            }

            int hab = _planet.HabitabilityPercent;
            Color hc = hab >= 70 ? UIManager.DS.Green : hab >= 40 ? CGold : UIManager.DS.Red;
            SetSeg(_dossier[0], hab <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(hab / 20f), 1, 5), $"{hab}%", hc);
            int size = _planet.MaxDistricts;
            int sizeSeg = size <= 5 ? 1 : size <= 6 ? 2 : size <= 7 ? 3 : size <= 9 ? 4 : 5;
            SetSeg(_dossier[1], sizeSeg, $"{_planet.BuiltDistricts} / {size}", CCyan);
            int mi = _planet.MineralDeposit, en = _planet.EnergyDeposit;
            SetSeg(_dossier[2], mi <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(mi * 5f / 7f), 1, 5), mi > 0 ? mi.ToString() : Loc.T("нет"), CMinerals);
            SetSeg(_dossier[3], Mathf.Clamp(en, 0, 5), en > 0 ? en.ToString() : Loc.T("нет"), CEnergy);

            // Климат — детерминированно по имени планеты
            var rng = new System.Random(_planet.Name.GetHashCode());
            float gravity = 0.6f + (float)rng.NextDouble() * 0.9f;
            string atmo = gravity < 0.85f ? Loc.T("разрежённая") : gravity > 1.25f ? Loc.T("плотная") : Loc.T("стандартная");
            int temperature = -40 + rng.Next(0, 90);
            int tilt = rng.Next(0, 45);
            _climate.text =
                Loc.F("<color={0}>Гравитация</color>   {1:0.00} g\n", m, gravity) +
                Loc.F("<color={0}>Атмосфера</color>   {1}\n", m, atmo) +
                Loc.F("<color={0}>Температура</color>   {1:+#;-#;0} °C\n", m, temperature) +
                Loc.F("<color={0}>Наклон оси</color>   {1}°\n", m, tilt) +
                Loc.F("<color={0}>Орбита</color>   {1:0.0} а.е.", m, _planet.OrbitRadius);

            var b = Bonuses();
            float st = _planet.MineralDeposit * FactionMult(DistrictType.Mining) * b.MineralsMult
                     + _planet.EnergyDeposit * FactionMult(DistrictType.Generator) * b.EnergyMult;
            _stationText.text = _planet.HasMiningStation
                ? Loc.F("<color={0}>Добывающий комплекс работает</color>", LGBuild.Hex(UIManager.DS.Green))
                : _planet.StationUnderConstruction ? Loc.F("<color={0}>Добывающий комплекс строится</color>", LGBuild.Hex(CGold))
                : st > 0f ? Loc.F("<color={0}>Комплекс дал бы +{1:0.#} ресурсов в месяц</color>", m, st)
                : Loc.F("<color={0}>Промышленных залежей нет</color>", m);
        }

        private void RefreshTiles()
        {
            var b = Bonuses();
            bool colony = Surveyed && _planet.Population > 0;
            float minerals = 0f, energy = 0f, alloys = 0f, science = 0f;
            if (colony)
            {
                minerals = _planet.ProducedMineralsPerMonth * FactionMult(DistrictType.Mining) * b.MineralsMult * _planet.OutputMult("minerals");
                energy = _planet.ProducedEnergyPerMonth * FactionMult(DistrictType.Generator) * b.EnergyMult * _planet.OutputMult("energy");
                alloys = _planet.ProducedAlloysPerMonth * FactionMult(DistrictType.Industrial) * b.AlloysMult * _planet.OutputMult("alloys");
                var tm = TechnologyManager.Instance;
                science = _planet.ResearchPops * TechnologyManager.SciencePerPop * (tm != null ? tm.GlobalResearchSpeedMultiplier : 1f);
            }
            if (Surveyed && _planet.HasMiningStation)
            {
                minerals += _planet.MineralDeposit * FactionMult(DistrictType.Mining) * b.MineralsMult;
                energy += _planet.EnergyDeposit * FactionMult(DistrictType.Generator) * b.EnergyMult;
            }
            SetTile(_tiles[0], minerals);
            SetTile(_tiles[1], energy);
            SetTile(_tiles[2], alloys);
            SetTile(_tiles[3], science);

            var work = _tiles[4];
            if (colony)
            {
                int idle = _planet.IdlePops;
                work.Value.text = $"{_planet.EmployedPops}<size=16><color={LGBuild.Hex(CMuted)}> / {_planet.WorkerSlots}</color></size>";
                work.Value.color = UIManager.DS.TextPrimary;
                work.Caption.text = idle > 0 ? Loc.F("<color={0}>БЕЗРАБОТНЫХ {1}</color>", LGBuild.Hex(CGold), idle) : Loc.T("РАБОТАЮТ / МЕСТ");
            }
            else
            {
                work.Value.text = "—";
                work.Value.color = CMuted;
                work.Caption.text = Loc.T("ЗАНЯТОСТЬ");
            }

            bool bankrupt = EconomyManager.Instance != null && EconomyManager.Instance.IsBankrupt && Owned;
            _bankruptText.gameObject.SetActive(bankrupt);
        }

        private static void SetTile(Tile t, float v)
        {
            bool any = v > 0.01f;
            t.Value.text = any ? $"+{v:0.#}" : "0";
            t.Value.color = any ? UIManager.DS.TextPrimary : CMuted;
        }

        private static void SetSeg(Seg s, int lit, string value, Color c)
        {
            s.Value.text = value;
            s.Value.color = lit > 0 ? Color.Lerp(c, Color.white, 0.35f) : CMuted;
            for (int i = 0; i < s.Cells.Length; i++) s.Cells[i].color = i < lit ? c : CSegOff;
        }

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
            _districtHeader.text = Loc.F("РАЙОНЫ   <color={0}>{1}", LGBuild.Hex(CMuted), _planet.BuiltDistricts) +
                                   (pendingAll > 0 ? $"<color={LGBuild.Hex(CGold)}>+{pendingAll}</color>" : "") + $" / {_planet.MaxDistricts}</color>";
            RebuildSlots();

            var eco = EconomyManager.Instance;
            foreach (var c in _cards)
            {
                int count = _planet.GetDistrictCount(c.Type);
                int worked = _planet.WorkedCount(c.Type);
                int pending = Builds != null ? Builds.PendingDistricts(_planet, c.Type) : 0;
                c.Count.text = pending > 0 ? $"×{count}<size=14><color={LGBuild.Hex(CGold)}> +{pending}</color></size>" : $"×{count}";
                RebuildWorkers(c, count, c.Type == DistrictType.Urban ? count : worked, pending);

                var job = FirstJob(JobKind.District, c.Type);
                if (job != null)
                {
                    float days = Builds.DaysUntilDone(job);
                    bool active = queue.Count > 0 && queue[0] == job;
                    c.Status.text = active ? Loc.F("строится · готов через {0} дн.", Mathf.CeilToInt(days)) : Loc.F("в очереди · ~{0} дн.", Mathf.CeilToInt(days));
                    c.Status.color = CGold;
                    LGBuild.SetBar(c.Progress, active ? job.Progress : 0f, CGold);
                }
                else
                {
                    c.Status.text = c.Type == DistrictType.Urban
                        ? (count > 0 ? Loc.F("жильё +{0}  ·  рабочих мест {1}", count * DistrictInfo.HousingPopCapacity(c.Type), count) : Loc.T("не построены"))
                        : count > 0 ? Loc.F("работают {0} из {1}", worked, count) : Loc.T("не построены");
                    c.Status.color = worked < count && c.Type != DistrictType.Urban ? CGold : CMuted;
                    LGBuild.SetBar(c.Progress, 0f);
                }

                c.Cancel.gameObject.SetActive(pending > 0 && Owned);

                float mc = DistrictInfo.MineralsCost(c.Type), ac = DistrictInfo.AlloysCost(c.Type);
                bool canPay = eco != null && eco.Minerals >= mc && eco.Alloys >= ac;
                c.MinCost.Set($"{mc:0}", eco != null && eco.Minerals >= mc ? CMinerals : UIManager.DS.Red);
                c.AlloyCost.Set($"{ac:0}", eco != null && eco.Alloys >= ac ? CAlloys : UIManager.DS.Red);

                string reason = !Surveyed ? Loc.T("НУЖНА РАЗВЕДКА")
                              : !Owned ? Loc.T("НЕ ВАША СИСТЕМА")
                              : _planet.Population <= 0 ? Loc.T("СНАЧАЛА КОЛОНИЯ")
                              : _planet.BuiltDistricts + pendingAll >= _planet.MaxDistricts ? Loc.T("НЕТ МЕСТА")
                              : queue.Count >= ConstructionManager.MaxPlanetQueue ? Loc.T("ОЧЕРЕДЬ ПОЛНА")
                              : !canPay ? CostCheck.Short(0f, mc, ac, 0f)
                              : null;
                string why = !Surveyed ? Loc.T("Сначала изучите систему научным кораблём.")
                           : !Owned ? Loc.T("Система не ваша: сначала постройте здесь форпост.")
                           : _planet.Population <= 0 ? Loc.T("Сначала основайте колонию — проект «Колония» справа.")
                           : _planet.BuiltDistricts + pendingAll >= _planet.MaxDistricts ? Loc.F("Все {0} мест под районы заняты (с учётом очереди).", _planet.MaxDistricts)
                           : queue.Count >= ConstructionManager.MaxPlanetQueue ? Loc.F("Очередь планеты заполнена: не больше {0} заказов.", ConstructionManager.MaxPlanetQueue)
                           : !canPay ? CostCheck.Full(0f, mc, ac, 0f) + "."
                           : null;
                c.Tip?.SetText(DistrictTip(c.Type, why));
                c.Build.interactable = reason == null;
                c.BuildBg.color = reason == null ? c.BuildTint : new Color(0.06f, 0.09f, 0.12f);
                c.Reason.text = reason ?? (queue.Count > 0 ? Loc.T("В ОЧЕРЕДЬ") : Loc.F("ПОСТРОИТЬ · {0:0} ДН.", ConstructionManager.DistrictDays(c.Type)));
                c.Reason.color = reason == null ? Color.white : CMuted;
            }
        }

        /// <summary>Фигурки рабочих: яркие — работают, тусклые — свободные места, золотые — район строится.</summary>
        private void RebuildWorkers(DistrictCard c, int count, int worked, int pending)
        {
            int key = count * 10000 + worked * 100 + pending;
            if (c.WorkerKey == key) return;
            c.WorkerKey = key;
            LGBuild.Clear(c.Workers);
            Color dc = DistrictInfo.Color(c.Type);
            int total = Mathf.Min(14, count + pending);
            const float S = 18f, G = 3f;
            for (int i = 0; i < total; i++)
            {
                bool building = i >= count;
                bool busy = !building && i < worked;
                Color col = building ? new Color(CGold.r, CGold.g, CGold.b, 0.75f)
                          : busy ? dc : new Color(dc.r, dc.g, dc.b, 0.22f);
                var ic = LGIcons.Create(c.Workers, c.Type == DistrictType.Urban ? LGIcon.Housing : LGIcon.Population, S, col);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * (S + G), 0), new Vector2(S, S));
                ic.raycastTarget = false;
                LG.Ignore(ic.gameObject);
            }
            if (total == 0)
            {
                var none = LGBuild.Label(c.Workers, "—", 12, CMuted, TextAnchor.MiddleLeft);
                LG.Ignore(none.gameObject);
            }
        }

        private int _slotsBuilt = -1, _slotsMax = -1;

        /// <summary>Сегментная полоска слотов районов: занятые — цветом района, строящиеся — золотом, свободные — пустые.</summary>
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
                          : i < _planet.Districts.Count + pendingN ? new Color(CGold.r, CGold.g, CGold.b, 0.45f)
                          : new Color(1f, 1f, 1f, 0.07f);
                var seg = LGBuild.Panel(_districtSlots, "Slot", col);
                var rt = seg.rectTransform;
                rt.anchorMin = new Vector2(i * w, 0);
                rt.anchorMax = new Vector2((i + 1) * w, 1);
                rt.offsetMin = new Vector2(1.5f, 0);
                rt.offsetMax = new Vector2(-1.5f, 0);
                LG.Ignore(seg.gameObject);
            }
        }

        private void RefreshQueue()
        {
            var queue = Builds != null ? Builds.QueueFor(_planet) : new List<ConstructionJob>();
            _queueEmptyGo.SetActive(queue.Count == 0);
            if (queue.Count == 0)
            {
                bool colony = Owned && _planet.Population > 0;
                _suggest = colony ? _planet.AutoBuildChoice() : null;
                _queueEmpty.text = !Owned ? Loc.T("Стройка доступна только в ваших системах")
                                 : !colony ? Loc.T("Районы строятся после основания колонии")
                                 : _planet.BuiltDistricts >= _planet.MaxDistricts ? Loc.T("Все районы планеты построены")
                                 : _planet.AutoBuild ? Loc.T("Автостройка закажет район в начале месяца")
                                 : _suggest == null ? Loc.T("Жилья и рабочих мест хватает — можно строить про запас")
                                 : Loc.T("Колонии нужен новый район:");
                bool show = _suggest.HasValue && !_planet.AutoBuild;
                _suggestBtn.gameObject.SetActive(show);
                if (show)
                {
                    var t = _suggest.Value;
                    bool can = _planet.CanBuildDistrict(t);
                    _suggestBtn.interactable = can;
                    _suggestText.text = Loc.F("{0} РАЙОН  ·  {1} тит., {2} спл.", DistrictName(t).ToUpper(), DistrictInfo.MineralsCost(t), DistrictInfo.AlloysCost(t)) +
                                        (can ? "" : Loc.T("  ·  НЕ ХВАТАЕТ"));
                    _suggestText.color = can ? Color.white : CMuted;
                }
            }
            _queueHint.text = queue.Count > 0 ? $"{queue.Count} / {ConstructionManager.MaxPlanetQueue}" : "";
            _buildMenuBtn.gameObject.SetActive(Owned);

            for (int i = 0; i < _queueRows.Length; i++)
            {
                var row = _queueRows[i];
                if (i >= queue.Count) { row.Go.SetActive(false); row.Job = null; continue; }
                var j = queue[i];
                row.Job = j;
                row.Go.SetActive(true);
                bool active = i == 0;
                Color jc = JobColor(j);
                row.Icon.sprite = LGIcons.Get(JobIcon(j));
                row.Icon.color = active ? jc : new Color(jc.r, jc.g, jc.b, 0.55f);
                row.Name.text = JobTitle(j);
                row.Name.color = active ? Color.white : UIManager.DS.TextPrimary;
                float days = Builds.DaysUntilDone(j);
                row.Days.text = active ? Loc.F("{0} дн.", Mathf.CeilToInt(j.DaysLeft)) : Loc.F("ждёт · ~{0} дн.", Mathf.CeilToInt(days));
                row.Days.color = active ? CGold : CMuted;
                LGBuild.SetBar(row.Progress, active ? j.Progress : 0f, jc);
                row.Cancel.gameObject.SetActive(Owned);
                bool showBuy = active && Owned;
                row.Buy.gameObject.SetActive(showBuy);
                if (showBuy)
                {
                    string block = Builds.BuyBlocker(j);
                    row.Buy.interactable = block == null;
                    row.BuyBg.color = block == null ? new Color(0.36f, 0.28f, 0.06f) : new Color(0.08f, 0.10f, 0.12f);
                    row.BuyText.text = Loc.F("КУПИТЬ {0}", ConstructionManager.BuyCost(j));
                    row.BuyText.color = block == null ? Color.white : CMuted;
                    row.BuyTip.SetText(BuildMenuModal.BuyTip(j, block));
                }
            }
        }

        private static LGIcon JobIcon(ConstructionJob j) => j.Kind switch
        {
            JobKind.District => j.District switch
            {
                DistrictType.Urban => LGIcon.Housing,
                DistrictType.Mining => LGIcon.Minerals,
                DistrictType.Generator => LGIcon.Energy,
                _ => LGIcon.Alloys
            },
            JobKind.MiningStation => LGIcon.Industry,
            JobKind.Colony => LGIcon.Population,
            JobKind.Terraform => LGIcon.Planet,
            JobKind.Robot => LGIcon.Industry,
            JobKind.GeneAdapt => LGIcon.Research,
            _ => LGIcon.Construction
        };

        private static Color JobColor(ConstructionJob j) => j.Kind == JobKind.District ? DistrictInfo.Color(j.District) : CGold;

        private static string JobTitle(ConstructionJob j) => j.Kind switch
        {
            JobKind.District => Loc.F("{0} район", DistrictName(j.District)),
            JobKind.MiningStation => Loc.T("Добывающий комплекс"),
            JobKind.Colony => Loc.T("Колония"),
            JobKind.Terraform => Loc.T("Терраформинг"),
            JobKind.Robot => Loc.T("Сборка роботов"),
            JobKind.GeneAdapt => Loc.T("Генная адаптация"),
            _ => Loc.T("Заказ")
        };

        private void RefreshActions()
        {
            var eco = EconomyManager.Instance;
            bool surveyed = Surveyed, owned = Owned;

            ConstructionJob mineJob = FirstJob(JobKind.MiningStation), colJob = FirstJob(JobKind.Colony), terJob = FirstJob(JobKind.Terraform);

            // Добывающий комплекс
            if (_planet.HasMiningStation) SetAction(_mine, false, LGIcon.Check, Loc.T("Работает — залежи разрабатываются"), true);
            else if (mineJob != null) SetAction(_mine, true, LGIcon.Clock, Loc.F("Строится · {0} дн. — нажмите, чтобы отменить", Mathf.CeilToInt(Builds.DaysUntilDone(mineJob))));
            else if (_planet.MineralDeposit + _planet.EnergyDeposit <= 0) SetAction(_mine, false, LGIcon.Minerals, Loc.T("Промышленных залежей нет"));
            else if (!surveyed) SetAction(_mine, false, LGIcon.Lock, Loc.T("Нужна разведка системы"));
            else if (!owned) SetAction(_mine, false, LGIcon.Lock, Loc.T("Не ваша система"));
            else
            {
                bool can = eco != null && eco.Minerals >= FleetManager.MiningStationMinerals;
                SetAction(_mine, can, LGIcon.Industry, Loc.F("{0:0} титана · {1:0} дн.", FleetManager.MiningStationMinerals, ConstructionManager.MiningStationDays));
            }

            // Колония
            if (_planet.Population > 0) SetAction(_colony, false, LGIcon.Check, Loc.F("Основана · жителей {0}", _planet.Population), true);
            else if (colJob != null) SetAction(_colony, true, LGIcon.Clock, Loc.F("Колонисты в пути · {0} дн. — отменить", Mathf.CeilToInt(Builds.DaysUntilDone(colJob))));
            else if (!surveyed) SetAction(_colony, false, LGIcon.Lock, Loc.T("Нужна разведка системы"));
            else if (!owned) SetAction(_colony, false, LGIcon.Lock, Loc.T("Не ваша система"));
            else if (!_planet.CanColonize) SetAction(_colony, false, LGIcon.Close, Loc.T("Планета непригодна для жизни"));
            else
            {
                bool can = eco != null && eco.CanAfford(0f, PlanetData.ColonyMineralsCost, PlanetData.ColonyAlloysCost, PlanetData.ColonyInfluenceCost);
                SetAction(_colony, can, LGIcon.Population,
                    Loc.F("{0:0} тит., {1:0} спл., {2:0} вл. · {3:0} дн.", PlanetData.ColonyMineralsCost, PlanetData.ColonyAlloysCost, PlanetData.ColonyInfluenceCost, ConstructionManager.ColonyDays));
            }

            // Терраформинг
            if (terJob != null) SetAction(_terra, true, LGIcon.Clock, Loc.F("Идёт · {0} дн. — нажмите, чтобы отменить", Mathf.CeilToInt(Builds.DaysUntilDone(terJob))));
            else if (_planet.HabitabilityPercent >= PlanetData.ComfortHabitability)
                SetAction(_terra, false, LGIcon.Check, Loc.F("Лучший класс мира · рост +{0:0}%", (PlanetData.ComfortGrowthBonus - 1f) * 100f), true);
            else if (!_planet.CanTerraform) SetAction(_terra, false, LGIcon.Close, Loc.T("Невозможен для этого мира"));
            else if (!surveyed) SetAction(_terra, false, LGIcon.Lock, Loc.T("Нужна разведка системы"));
            else if (!owned) SetAction(_terra, false, LGIcon.Lock, Loc.T("Не ваша система"));
            else
            {
                bool can = eco != null && eco.EnergyCredits >= PlanetData.TerraformEnergyCost && eco.Minerals >= PlanetData.TerraformMineralsCost
                           && eco.Influence >= PlanetData.TerraformInfluenceCost;
                var next = _planet.TerraformNextType;
                string to = next.HasValue ? $"→ {PlanetData.ClassName(next.Value)} · " : "";
                SetAction(_terra, can, LGIcon.Planet,
                    Loc.F("{0}{1:0} гел., {2:0} тит., {3:0} вл. · {4:0} дн.", to, PlanetData.TerraformEnergyCost, PlanetData.TerraformMineralsCost, PlanetData.TerraformInfluenceCost, ConstructionManager.TerraformDays));
            }

            RefreshActionTips(mineJob, colJob, terJob, surveyed, owned);
            RefreshSocietyActions(surveyed, owned);
        }

        /// <summary>Подсказки проектов: что именно даст проект и выгоден ли он — с цифрами этой планеты.</summary>
        private void RefreshActionTips(ConstructionJob mineJob, ConstructionJob colJob, ConstructionJob terJob, bool surveyed, bool owned)
        {
            var eco = EconomyManager.Instance;
            const string Muted = "#8AA2A8", Bad = "#FF8888";
            string lockLine = !surveyed ? Loc.F("\n<color={0}>Сначала изучите систему научным кораблём.</color>", Muted)
                            : !owned ? Loc.F("\n<color={0}>Сначала постройте в системе форпост.</color>", Muted) : "";

            // Добывающий комплекс
            int mi = _planet.MineralDeposit, en = _planet.EnergyDeposit;
            string mineGain = (mi > 0 ? Loc.F("<color=#4DF2DB>+{0} титана</color>", mi) : "") + (mi > 0 && en > 0 ? Loc.T(" и ") : "") + (en > 0 ? Loc.F("<color=#F2C46B>+{0} гелия-3</color>", en) : "");
            string mineTip;
            if (_planet.HasMiningStation) mineTip = Loc.F("<b>Добывающий комплекс</b>\nРаботает: {0} в месяц.", mineGain);
            else if (mi + en <= 0) mineTip = Loc.T("<b>Добывающий комплекс</b>\nНа этой планете нет залежей — строить нечего.");
            else
            {
                int perMonth = Mathf.Max(1, mi + en);
                int payback = Mathf.CeilToInt(FleetManager.MiningStationMinerals / perMonth);
                mineTip = Loc.F("<b>Добывающий комплекс</b>\nДаст {0} в месяц — без жителей и колонии.\n", mineGain) +
                          Loc.F("<color={0}>Цена:</color> {1:0} титана · {2:0} дн.\n", Muted, FleetManager.MiningStationMinerals, ConstructionManager.MiningStationDays) +
                          Loc.F("<color={0}>Окупаемость:</color> ~{1} мес.", Muted, payback) +
                          (mineJob != null ? Loc.F("\n\nСтроится, осталось {0} дн. Нажмите, чтобы отменить.", Mathf.CeilToInt(Builds.DaysUntilDone(mineJob))) : lockLine) +
                          (mineJob == null && surveyed && owned && !CostCheck.Affordable(0f, FleetManager.MiningStationMinerals)
                              ? $"\n<color={Bad}>{CostCheck.Full(0f, FleetManager.MiningStationMinerals)}.</color>" : "");
            }
            _mine.Tip.SetText(mineTip);

            // Колония
            string colTip;
            if (_planet.Population > 0)
                colTip = Loc.F("<b>Колония</b>\nЖителей {0} / жильё {1} · районов {2} / {3}\n", _planet.Population, _planet.HousingCapacity, _planet.BuiltDistricts, _planet.MaxDistricts) +
                         Loc.F("Даёт в месяц: <color=#4DF2DB>+{0} титана</color>, <color=#F2C46B>+{1} гелия-3</color>, ", _planet.ProducedMineralsPerMonth, _planet.ProducedEnergyPerMonth) +
                         Loc.F("<color=#F29E5C>+{0} сплавов</color>", _planet.ProducedAlloysPerMonth) +
                         (_planet.HabitabilityGrowthMult > 1f ? Loc.F("\nКомфортный мир: рост населения +{0:0}%", (PlanetData.ComfortGrowthBonus - 1f) * 100f) : "");
            else if (colJob != null)
                colTip = Loc.F("<b>Колония</b>\nКолонисты в пути, прибудут через {0} дн. Нажмите, чтобы отменить (ресурсы вернутся).\n\n", Mathf.CeilToInt(Builds.DaysUntilDone(colJob))) + _planet.ColonyBenefitText();
            else if (!_planet.CanColonize)
            {
                int steps = _planet.TerraformStepsToColonize;
                colTip = Loc.F("<b>Колония</b>\nПланета непригодна: пригодность {0}%, нужно от 40%.", _planet.HabitabilityPercent) +
                         (steps > 0 ? Loc.F("\nТерраформинг сделает её пригодной за {0} шаг{1}.", steps, (steps == 1 ? "" : steps < 5 ? Loc.T("а") : Loc.T("ов"))) : "");
            }
            else
            {
                bool can = eco != null && eco.CanAfford(0f, PlanetData.ColonyMineralsCost, PlanetData.ColonyAlloysCost, PlanetData.ColonyInfluenceCost);
                colTip = Loc.T("<b>Основать колонию</b>\n") + _planet.ColonyBenefitText() + lockLine +
                         (can || !owned || !surveyed ? "" : $"\n<color={Bad}>{CostCheck.Full(0f, PlanetData.ColonyMineralsCost, PlanetData.ColonyAlloysCost, PlanetData.ColonyInfluenceCost)}.</color>");
            }
            _colony.Tip.SetText(colTip);

            // Терраформинг
            string terTip;
            if (terJob != null)
                terTip = Loc.F("<b>Терраформинг идёт</b>\nОсталось {0} дн. Нажмите, чтобы отменить (ресурсы вернутся).\n\n", Mathf.CeilToInt(Builds.DaysUntilDone(terJob))) + _planet.TerraformBenefitText();
            else if (_planet.HabitabilityPercent >= PlanetData.ComfortHabitability)
                terTip = Loc.F("<b>Терраформинг</b>\nМир уже {0} — лучший класс. Население здесь растёт на {1:0}% быстрее.", PlanetData.ClassName(_planet.Type), (PlanetData.ComfortGrowthBonus - 1f) * 100f);
            else if (!_planet.CanTerraform)
                terTip = Loc.F("<b>Терраформинг</b>\n{0}: этот класс мира изменить нельзя.", _planet.ClassDisplayName);
            else
            {
                bool can = eco != null && eco.EnergyCredits >= PlanetData.TerraformEnergyCost && eco.Minerals >= PlanetData.TerraformMineralsCost
                           && eco.Influence >= PlanetData.TerraformInfluenceCost;
                terTip = Loc.T("<b>Терраформинг</b>\n") + _planet.TerraformBenefitText() + lockLine +
                         (can || !owned || !surveyed ? "" : $"\n<color={Bad}>{CostCheck.Full(PlanetData.TerraformEnergyCost, PlanetData.TerraformMineralsCost, 0f, PlanetData.TerraformInfluenceCost)}.</color>");
            }
            _terra.Tip.SetText(terTip);
        }

        /// <summary>
        /// Состояние проекта: доступен — цветная кнопка; выполнен — тёмная с зелёной галочкой;
        /// недоступен — тёмная приглушённая.
        /// </summary>
        private static void SetAction(ActionButton a, bool interactable, LGIcon icon, string status, bool done = false)
        {
            a.Btn.interactable = interactable;
            a.Status.text = status;
            a.Status.color = done ? new Color(0.62f, 0.92f, 0.72f) : interactable ? Color.white : CMuted;
            a.Icon.sprite = LGIcons.Get(icon);
            a.Icon.color = done ? UIManager.DS.Green : interactable ? Color.white : CMuted;
            a.Bg.color = done ? new Color(0.04f, 0.12f, 0.09f) : interactable ? a.Tint : new Color(0.05f, 0.08f, 0.11f);
            Color ac = done ? UIManager.DS.Green : interactable ? a.Accent : CMuted;
            a.Badge.color = new Color(ac.r * 0.22f, ac.g * 0.22f, ac.b * 0.22f, 1f);
        }

        // ================================================================ Колонии: переключение, приоритет, автостройка

        /// <summary>Колонии игрока по порядку систем — для стрелок «предыдущая / следующая».</summary>
        private void CollectColonies()
        {
            _colonies.Clear();
            if (_gen == null) _gen = FindAnyObjectByType<GalaxyGenerator>();
            if (_gen == null) return;
            foreach (var sys in _gen.Systems)
            {
                if (sys.OwnerId != 0) continue;
                foreach (var p in sys.Planets)
                    if (p.Population > 0) _colonies.Add(p);
            }
        }

        private void StepColony(int dir)
        {
            CollectColonies();
            if (_colonies.Count == 0) return;
            int i = _colonies.IndexOf(_planet);
            int next = i < 0 ? (dir > 0 ? 0 : _colonies.Count - 1) : (i + dir + _colonies.Count) % _colonies.Count;
            var p = _colonies[next];
            if (p == _planet) return;
            SFXManager.Play(Sfx.SystemSelect);
            _planet = p;
            _system = p.ParentSystem;
            TooltipSystem.Hide();
            Refresh();
            BindHolo();
        }

        private void SetFocus(ColonyFocus f)
        {
            if (_planet == null || !Owned) return;
            _planet.Focus = f;
            EconomyManager.Instance?.RaiseResourcesChanged();
            Refresh();
        }

        private void ToggleAutoBuild()
        {
            if (_planet == null || !Owned) return;
            _planet.AutoBuild = !_planet.AutoBuild;
            // Включили — первый заказ сразу, если очередь пуста и ресурсы есть
            if (_planet.AutoBuild && Builds != null && !Builds.HasAnyJob(_planet))
            {
                var t = _planet.AutoBuildChoice();
                if (t.HasValue) _planet.TryBuildDistrict(t.Value);
            }
            Refresh();
        }

        private void RefreshColonyControls()
        {
            CollectColonies();
            int idx = _colonies.IndexOf(_planet);
            _switcher.SetActive(_colonies.Count > 1 || (_colonies.Count == 1 && idx < 0));
            _switchText.text = idx >= 0
                ? Loc.F("КОЛОНИЯ  {0} / {1}", idx + 1, _colonies.Count)
                : Loc.F("КОЛОНИЙ  {0}", _colonies.Count);

            bool show = Owned && _planet.Population > 0;
            _colonyControls.SetActive(show);
            if (!show) return;
            for (int i = 0; i < _focusBg.Length; i++)
            {
                bool on = ColonyFocusInfo.All[i] == _planet.Focus;
                _focusBg[i].color = on ? new Color(0.16f, 0.52f, 0.56f) : new Color(0.07f, 0.13f, 0.17f);
            }
            bool auto = _planet.AutoBuild;
            _autoBg.color = auto ? new Color(0.14f, 0.46f, 0.30f) : new Color(0.07f, 0.13f, 0.17f);
            _autoIcon.sprite = LGIcons.Get(auto ? LGIcon.Check : LGIcon.Construction);
            _autoIcon.color = auto ? UIManager.DS.Green : CMuted;
            _autoText.text = auto ? Loc.T("АВТОСТРОЙКА") : Loc.T("АВТОСТРОЙКА ВЫКЛ.");
            _autoText.color = auto ? Color.white : CMuted;
        }

        // ================================================================ Действия

        private void OnBuildDistrict(DistrictType t)
        {
            if (_planet == null || _planet.Population <= 0) return;
            if (_planet.TryBuildDistrict(t)) Refresh();
        }

        private void OnCancelDistrict(DistrictType t)
        {
            var job = LastJob(JobKind.District, t);
            if (job == null) return;
            Builds.Cancel(job);   // звук отмены — в ConstructionManager
            Refresh();
        }

        private void OnSuggest()
        {
            if (_planet == null || !_suggest.HasValue) return;
            if (_planet.TryBuildDistrict(_suggest.Value)) Refresh();
        }

        private void OnCancelQueued(int row)
        {
            var job = _queueRows[row].Job;
            if (job == null || Builds == null || !Owned) return;
            Builds.Cancel(job);
            NotificationCenter.Show(Loc.T("Заказ отменён"), Loc.T("Ресурсы возвращены"), NotificationCenter.Kind.Info, 3f);
            Refresh();
        }

        /// <summary>Отменить идущую стройку станции/колонии/терраформинга (кнопка проекта во время стройки).</summary>
        private bool TryCancel(JobKind kind)
        {
            var job = FirstJob(kind);
            if (job == null) return false;
            Builds.Cancel(job);
            NotificationCenter.Show(Loc.T("Заказ отменён"), Loc.T("Ресурсы возвращены"), NotificationCenter.Kind.Info, 3f);
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
                NotificationCenter.Show(Loc.T("Колонисты отправлены"), Loc.F("{0} · колония через {1:0} дн.", _planet.Name, ConstructionManager.ColonyDays), NotificationCenter.Kind.Info, 4f);
                Refresh();
            }
        }

        private void OnBuyQueued(int idx)
        {
            if (_planet == null || Builds == null || idx < 0 || idx >= _queueRows.Length) return;
            var job = _queueRows[idx].Job;
            if (job != null && Builds.TryBuy(job))
            {
                SFXManager.Play(Sfx.UiConfirm);
                EconomyManager.Instance?.RecalculateAll();
                Refresh();
            }
        }

        private void OnRobots()
        {
            if (_planet == null || TryCancel(JobKind.Robot)) return;
            if (_planet.TryBuildRobot())
            {
                NotificationCenter.Show(Loc.T("Сборка роботов начата"), Loc.F("{0} · {1:0} дн.", _planet.Name, ConstructionManager.RobotDays), NotificationCenter.Kind.Info, 4f);
                Refresh();
            }
        }

        private void OnGene()
        {
            if (_planet == null || TryCancel(JobKind.GeneAdapt)) return;
            if (_planet.TryGeneAdapt())
            {
                NotificationCenter.Show(Loc.T("Генная адаптация начата"), Loc.F("{0} · {1:0} дн.", _planet.Name, ConstructionManager.GeneAdaptDays), NotificationCenter.Kind.Info, 4f);
                Refresh();
            }
        }

        private void OnPropaganda()
        {
            if (_planet == null) return;
            if (PlanetStability.TryPropaganda(_planet))
            {
                NotificationCenter.Show(Loc.T("Кампания пропаганды"), Loc.F("{0}: +{1:0} стабильности на {2} мес.", _planet.Name, PlanetStability.PropagandaBonus, PlanetStability.PropagandaDuration),
                    NotificationCenter.Kind.Success, 4f);
                Refresh();
            }
        }

        private void OnTerra()
        {
            if (_planet == null || TryCancel(JobKind.Terraform)) return;
            if (_planet.TryStartTerraform())
            {
                NotificationCenter.Show(Loc.T("Терраформинг начат"), Loc.F("{0} · {1:0} дн.", _planet.Name, ConstructionManager.TerraformDays), NotificationCenter.Kind.Info, 4f);
                Refresh();
            }
        }

        // ================================================================ Построение

        private void Build()
        {
            _rootRt = LGBuild.Rect(_host.transform, "PlanetFocusOverlay");
            _rootRt.Stretch();
            _root = _rootRt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.004f, 0.012f, 0.020f, 1f);
            bg.raycastTarget = true;
            LG.Scrim(_root).FillMultiplier = 2.2f;
            var motion = LG.Motion(_root, LGAppear.Kind.Fade);
            motion.inDuration = 0.4f;
            motion.outDuration = 0.25f;

            BuildBackdrop(_rootRt);
            BuildStage(_rootRt);
            BuildHeader(_rootRt);
            BuildPopulation(_rootRt);
            BuildSociety(_rootRt);
            BuildDossier(_rootRt);
            BuildTiles(_rootRt);
            BuildDistricts(_rootRt);
            BuildQueue(_rootRt);
            BuildProjects(_rootRt);

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        /// <summary>Фон: подсветка цвета мира за планетой, виньетка по краям, линия под шапкой.</summary>
        private void BuildBackdrop(RectTransform rt)
        {
            _worldTint = Deco(rt, "WorldTint", HaloSprite(), Color.clear);
            var t = _worldTint.rectTransform;
            t.anchorMin = new Vector2(-0.20f, -0.30f);
            t.anchorMax = new Vector2(0.76f, 1.30f);
            t.offsetMin = t.offsetMax = Vector2.zero;

            var vig = Deco(rt, "Vignette", VignetteSprite(), new Color(0f, 0f, 0f, 0.85f));
            vig.rectTransform.Stretch();

            var line = LGBuild.Panel(rt, "HeaderLine", new Color(0.45f, 0.95f, 0.90f, 0.16f));
            line.rectTransform.anchorMin = new Vector2(0, 1);
            line.rectTransform.anchorMax = new Vector2(1, 1);
            line.rectTransform.offsetMin = new Vector2(Pad, -HeaderH - 1);
            line.rectTransform.offsetMax = new Vector2(-Pad, -HeaderH);
            LG.Line(line.gameObject, hairline: true);
        }

        /// <summary>Сцена планеты: ореолы, кольца-приборы, дуги населения и роста, сама планета.</summary>
        private void BuildStage(RectTransform rt)
        {
            _stage = LGBuild.Rect(rt, "PlanetStage");
            _stage.anchorMin = _stage.anchorMax = new Vector2(0.28f, 0.49f);
            _stage.pivot = new Vector2(0.5f, 0.5f);
            _stage.sizeDelta = new Vector2(670, 670);

            _haloOuter = Deco(_stage, "HaloOuter", HaloSprite(), Color.clear);
            Fit(_haloOuter.rectTransform, -0.45f);
            _halo = Deco(_stage, "Halo", HaloSprite(), Color.clear);
            Fit(_halo.rectTransform, -0.20f);

            _ringTicks = Deco(_stage, "RingTicks", TickRingSprite(), Color.clear);
            Fit(_ringTicks.rectTransform, -0.10f);
            _ringDash = Deco(_stage, "RingDash", DashRingSprite(), Color.clear);
            Fit(_ringDash.rectTransform, -0.035f);

            // Население — дуга слева, по часовой снизу вверх; рост — короткая дуга справа
            Arc("PopTrack", 5f, PopArc, 0.75f, new Color(1f, 1f, 1f, 0.08f), PopArc);
            _popFill = Arc("PopFill", 5f, PopArc, 0.75f, CCyan, 0f);
            Arc("GrowTrack", 3f, GrowArc, 0.25f, new Color(1f, 1f, 1f, 0.07f), GrowArc);
            _growFill = Arc("GrowFill", 3f, GrowArc, 0.25f, UIManager.DS.Green, 0f);

            var host = LGBuild.Rect(_stage, "PlanetImage");
            host.Stretch();
            _planetImage = host.gameObject.AddComponent<RawImage>();
            _planetImage.raycastTarget = true;
            var discShader = Resources.Load<Shader>("Shaders/UIPlanetDisc");
            if (discShader == null) discShader = Shader.Find("Astropolity/UI/PlanetDisc");
            if (discShader != null)
            {
                _discMat = new Material(discShader) { name = "PlanetDisc" };
                _planetImage.material = _discMat;
            }
            host.gameObject.AddComponent<HoloDragCatcher>();
            LG.Ignore(host.gameObject);
            TooltipHelper.Attach(host.gameObject, Loc.T("Зажмите ЛКМ и ведите мышью — вращение планеты"));
        }

        /// <summary>
        /// Дуга-шкала у планеты: кольцо с радиальной заливкой, доля окружности share,
        /// центр дуги — в точке center (доля оборота по часовой от 12 часов).
        /// </summary>
        private Image Arc(string name, float thickness, float share, float center, Color c, float fill)
        {
            var img = Deco(_stage, name, RingSprite(512, thickness), c);
            Fit(img.rectTransform, -0.02f);
            Radial(img);
            img.fillAmount = fill;
            float start = center - share * 0.5f;          // заливка идёт по часовой от «12 часов»…
            img.rectTransform.localRotation = Quaternion.Euler(0, 0, -start * 360f);   // …повёрнутых к началу дуги
            return img;
        }

        /// <summary>Растянуть по сцене с запасом k (доля размера, отрицательная — наружу).</summary>
        private static void Fit(RectTransform r, float k)
        {
            r.anchorMin = new Vector2(k, k);
            r.anchorMax = new Vector2(1f - k, 1f - k);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        private static void Radial(Image img)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = true;
            img.fillAmount = 0f;
        }

        private static Image Deco(Transform parent, string name, Sprite sp, Color c)
        {
            var img = LGBuild.Panel(parent, name, c);
            img.sprite = sp;
            img.raycastTarget = false;
            LG.Ignore(img.gameObject);
            return img;
        }

        private void BuildHeader(RectTransform rt)
        {
            var close = LGBuild.Button(rt, "Close", new Color(0.10f, 0.28f, 0.32f), CCyan, Close, LGIcon.Back, Loc.T("ЗАКРЫТЬ  (ESC)"), 12);
            ((RectTransform)close.transform).At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -30), new Vector2(170, 40));
            LG.Motion(close.gameObject, LGAppear.Kind.Fade).delay = 0.12f;

            // Акцентная черта цвета мира перед названием
            _titleAccent = LGBuild.Panel(rt, "TitleAccent", CCyan);
            _titleAccent.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(232, -24), new Vector2(4, 58));
            LG.Ignore(_titleAccent.gameObject);

            _title = LGBuild.Label(rt, "", 40, Color.white, TextAnchor.UpperLeft, bold: true);
            _title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(250, -16), new Vector2(560, 48));
            var glow = _title.gameObject.AddComponent<Outline>();
            glow.effectColor = new Color(0.30f, 0.90f, 0.85f, 0.18f);
            glow.effectDistance = new Vector2(1.5f, -1.5f);
            _subtitle = LGBuild.Label(rt, "", 13, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(252, -64), new Vector2(560, 20));

            // Владелец и губернатор — по центру шапки, в одну строку
            var chip = LGBuild.Panel(rt, "Owner", UIManager.DS.BgSlot);
            chip.rectTransform.At(new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(0, -34), new Vector2(300, 34));
            LG.Chip(chip.gameObject, new Color(1f, 1f, 1f, 0.2f));
            _ownerIcon = LGIcons.Create(chip.transform, LGIcon.Starbase, 18, FleetIndicator.OwnColor);
            _ownerIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(18, 18));
            _ownerText = LGBuild.Label(chip.transform, "", 12, Color.white, TextAnchor.MiddleLeft, bold: true);
            _ownerText.rectTransform.Stretch(42, 0, 12, 0);

            var gov = LGBuild.Panel(rt, "Governor", UIManager.DS.BgSlot);
            gov.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(10, -34), new Vector2(260, 34));
            LG.Chip(gov.gameObject, new Color(1f, 0.80f, 0.32f, 0.3f));
            _govChip = gov.gameObject;
            var faceHost = LGBuild.Rect(gov.transform, "Face");
            faceHost.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(28, 28));
            _govFace = LeaderThumb.Create(faceHost, 14f);
            _govIcon = LGIcons.Create(faceHost, LGIcon.Leader, 18, CGold);
            _govText = LGBuild.Label(gov.transform, "", 11, Color.white, TextAnchor.MiddleLeft, bold: true);
            _govText.rectTransform.Stretch(40, 0, 10, 0);
            _govText.supportRichText = true;
            _govChip.SetActive(false);

            // Переключение между колониями — справа в шапке
            var sw = LGBuild.Rect(rt, "ColonySwitch");
            sw.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Pad, -30), new Vector2(270, 40));
            _switcher = sw.gameObject;
            var prev = LGBuild.Button(sw, "Prev", new Color(0.10f, 0.28f, 0.32f), CCyan, () => StepColony(-1), LGIcon.Back, null, 12);
            ((RectTransform)prev.transform).At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(40, 40));
            var nextBtn = LGBuild.Button(sw, "Next", new Color(0.10f, 0.28f, 0.32f), CCyan, () => StepColony(1), LGIcon.Back, null, 12);
            ((RectTransform)nextBtn.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(40, 40));
            foreach (var img in nextBtn.GetComponentsInChildren<Image>(true))      // стрелка «назад», зеркально — «вперёд»
                if (img.gameObject != nextBtn.gameObject) img.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            _switchText = LGBuild.Label(sw, "", 12, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, bold: true);
            _switchText.rectTransform.Stretch(48, 0, 48, 0);
            TooltipHelper.Attach(prev.gameObject, Loc.T("<b>Предыдущая колония</b>\nShift+Tab"));
            TooltipHelper.Attach(nextBtn.gameObject, Loc.T("<b>Следующая колония</b>\nTab"));
            LG.Motion(sw.gameObject, LGAppear.Kind.Fade).delay = 0.12f;
        }

        /// <summary>Население — крупно поверх планеты слева сверху, как приборная надпись.</summary>
        private void BuildPopulation(RectTransform rt)
        {
            var host = LGBuild.Rect(rt, "Population");
            host.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -HeaderH - 22), new Vector2(330, 150));
            LG.Motion(host.gameObject, LGAppear.Kind.SlideLeft).delay = 0.08f;

            var ic = LGIcons.Create(host, LGIcon.Population, 18, CCyan);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -4), new Vector2(18, 18));
            _popCaption = LGBuild.Label(host, "", 11, CCyan, TextAnchor.UpperLeft, bold: true);
            _popCaption.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -5), new Vector2(300, 16));
            _popBig = LGBuild.Label(host, "", 52, Color.white, TextAnchor.UpperLeft, bold: true);
            _popBig.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(-2, -22), new Vector2(330, 62));
            _growthText = LGBuild.Label(host, "", 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            _growthText.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -88), new Vector2(320, 44));
            _growthText.lineSpacing = 1.15f;
        }

        /// <summary>Общество колонии: шкала стабильности, состав по видам и кампания пропаганды.</summary>
        private void BuildSociety(RectTransform rt)
        {
            var card = Glass(rt, "Society", LGIcon.Population, Loc.T("ОБЩЕСТВО"), new Color(0.98f, 0.55f, 0.85f), LGAppear.Kind.SlideLeft, 0.12f, out var body);
            var r = card.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(Pad, -HeaderH - 22 - 150 - 6);
            r.sizeDelta = new Vector2(290, 272);
            _society = card.gameObject;

            // Стабильность: подпись, число и уровень, шкала, действие уровня (всё под одной подсказкой)
            var stab = LGBuild.Panel(body, "Stability", new Color(1f, 1f, 1f, 0f), raycast: true);
            stab.rectTransform.TopBand(0, 62);
            var sl = LGBuild.Label(stab.rectTransform, Loc.T("Стабильность"), 11, CMuted, TextAnchor.UpperLeft);
            sl.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(120, 16));
            _stabValue = LGBuild.Label(stab.rectTransform, "", 12, Color.white, TextAnchor.UpperRight, bold: true);
            _stabValue.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(170, 16));
            var barHost = LGBuild.Rect(stab.rectTransform, "BarHost");
            barHost.TopBand(20, 8);
            _stabBar = LGBuild.Bar(barHost, CCyan, 0.5f, 7f);
            _stabEffect = LGBuild.Label(stab.rectTransform, "", 10, CMuted, TextAnchor.UpperLeft, wrap: true);
            _stabEffect.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -32), new Vector2(250, 30));
            TooltipHelper.Attach(stab.gameObject, Loc.T("Стабильность"));
            _stabTip = stab.GetComponent<TooltipTrigger>();

            // Состав по видам: полоса долей и легенда
            var comp = LGBuild.Panel(body, "Composition", new Color(1f, 1f, 1f, 0f), raycast: true);
            comp.rectTransform.TopBand(68, 92);
            _compCaption = LGBuild.Label(comp.rectTransform, "", 11, CMuted, TextAnchor.UpperLeft);
            _compCaption.rectTransform.TopBand(0, 16);
            var segHost = LGBuild.Rect(comp.rectTransform, "Segments");
            segHost.TopBand(19, 8);
            _compSegs = new Image[6];
            for (int i = 0; i < _compSegs.Length; i++)
            {
                _compSegs[i] = LGBuild.Panel(segHost, "Seg" + i, Color.white);
                LG.Ignore(_compSegs[i].gameObject);
            }
            _legend = new Text[3];
            for (int i = 0; i < _legend.Length; i++)
            {
                _legend[i] = LGBuild.Label(comp.rectTransform, "", 11, UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
                _legend[i].rectTransform.TopBand(33 + i * 19, 18);
                _legend[i].horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            TooltipHelper.Attach(comp.gameObject, Loc.T("Виды"));
            _compTip = comp.GetComponent<TooltipTrigger>();

            // Пропаганда
            _propBtn = LGBuild.Button(body, "Propaganda", new Color(0.30f, 0.14f, 0.30f), new Color(0.98f, 0.55f, 0.85f, 0.55f), OnPropaganda, null, null, 11, 10f);
            var prt = (RectTransform)_propBtn.transform;
            prt.anchorMin = new Vector2(0, 0); prt.anchorMax = new Vector2(1, 0);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.offsetMin = Vector2.zero; prt.offsetMax = new Vector2(0, 30);
            _propBg = _propBtn.GetComponent<Image>();
            var pi = LGIcons.Create(_propBtn.transform, LGIcon.Influence, 14, Color.white);
            pi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(14, 14));
            _propText = LGBuild.Label(_propBtn.transform, "", 11, Color.white, TextAnchor.MiddleLeft, bold: true);
            _propText.rectTransform.Stretch(34, 0, 8, 0);
            TooltipHelper.Attach(_propBtn.gameObject, Loc.T("Пропаганда"));
            _propTip = _propBtn.GetComponent<TooltipTrigger>();
        }

        private void RefreshSociety()
        {
            bool show = Surveyed && _planet.Population > 0;
            _society.SetActive(show);
            if (!show) return;
            _planet.SyncPops();

            var tier = _planet.Tier;
            Color tc = PlanetStability.TierColor(tier);
            float target = PlanetStability.Target(_planet);
            string arrow = target > _planet.Stability + 0.5f ? " ▲" : target < _planet.Stability - 0.5f ? " ▼" : "";
            _stabValue.text = $"<color=#{PlanetStability.Hex(tc)}>{_planet.Stability:0} · {PlanetStability.TierName(tier)}{arrow}</color>";
            LGBuild.SetBar(_stabBar, _planet.Stability / 100f, tc);
            _stabEffect.text = _planet.RiotMonths > 0
                ? Loc.F("<color=#FF6B6B>Бунт {0} из {1} мес. — затем отделение</color>", _planet.RiotMonths, PlanetStability.SecessionMonths)
                : PlanetStability.TierEffect(tier);
            _stabTip.SetText(PlanetStability.Tooltip(_planet));

            // Состав: группы по убыванию
            var groups = new List<PopGroup>(_planet.Pops);
            groups.Sort((a, b) => b.Count.CompareTo(a.Count));
            int total = Mathf.Max(1, _planet.Population);
            _compCaption.text = Loc.F("Виды · {0}", groups.Count) + (_planet.RobotPops > 0 ? Loc.F("  ·  роботов {0}", _planet.RobotPops) : "");
            float x = 0f;
            for (int i = 0; i < _compSegs.Length; i++)
            {
                var seg = _compSegs[i];
                bool on = i < groups.Count;
                seg.gameObject.SetActive(on);
                if (!on) continue;
                var sp = SpeciesRegistry.Get(groups[i].Species);
                float w = i == groups.Count - 1 || i == _compSegs.Length - 1 ? 1f - x : groups[i].Count / (float)total;
                var rt = seg.rectTransform;
                rt.anchorMin = new Vector2(x, 0f);
                rt.anchorMax = new Vector2(Mathf.Min(1f, x + w), 1f);
                rt.offsetMin = new Vector2(i > 0 ? 1f : 0f, 0f);
                rt.offsetMax = Vector2.zero;
                seg.color = sp != null ? sp.Color : CMuted;
                x += w;
            }

            var tip = new System.Text.StringBuilder(Loc.T("<b>Виды планеты</b>"));
            int founder = SpeciesRegistry.FounderOf(_planet.OwnerId);
            for (int i = 0; i < groups.Count; i++)
            {
                var sp = SpeciesRegistry.Get(groups[i].Species);
                if (sp == null) continue;
                string hex = PlanetStability.Hex(sp.Color);
                if (i < _legend.Length)
                    _legend[i].text = i == _legend.Length - 1 && groups.Count > _legend.Length
                        ? Loc.F("<color={0}>· ещё видов: {1}</color>", LGBuild.Hex(CMuted), groups.Count - i)
                        : $"<color=#{hex}>■</color> {sp.DisplayName} <color={LGBuild.Hex(CMuted)}>· {groups[i].Count}</color>";
                bool alien = !sp.Robot && !SpeciesRegistry.Related(sp.Id, founder);
                tip.Append($"\n\n<color=#{hex}>■</color> <b>{sp.DisplayName}</b> · {groups[i].Count}");
                if (alien) tip.Append(Loc.T(" <color=#FF9A8A>(чужой вид)</color>"));
                if (!sp.AtHome(_planet.Type)) tip.Append(Loc.T(" <color=#F2C46B>(чужой климат)</color>"));
                foreach (var t in sp.Traits)
                    tip.Append($"\n  {(TraitInfo.IsNegative(t) ? "<color=#FF9A8A>" : "<color=#9EF0C8>")}{TraitInfo.Name(t)}</color> — {TraitInfo.Desc(t)}");
            }
            for (int i = groups.Count; i < _legend.Length; i++) _legend[i].text = "";
            _compTip.SetText(tip.ToString());

            // Пропаганда
            var eco = EconomyManager.Instance;
            bool owned = Owned;
            bool running = _planet.PropagandaMonths > 0;
            bool can = owned && !running && eco != null && eco.Influence >= PlanetStability.PropagandaInfluence;
            _propBtn.gameObject.SetActive(owned);
            _propBtn.interactable = can;
            _propBg.color = can ? new Color(0.30f, 0.14f, 0.30f) : new Color(0.05f, 0.08f, 0.11f);
            _propText.text = running ? Loc.F("ПРОПАГАНДА ИДЁТ · {0} мес.", _planet.PropagandaMonths)
                                     : Loc.F("ПРОПАГАНДА · {0:0} влияния", PlanetStability.PropagandaInfluence);
            _propText.color = can || running ? Color.white : CMuted;
            _propTip.SetText(Loc.F("<b>Кампания пропаганды</b>\nПлакаты, трансляции и праздники: +{0:0} стабильности на {1} мес. (и +5 сразу).\n", PlanetStability.PropagandaBonus, PlanetStability.PropagandaDuration) +
                             Loc.F("<color=#8AA2A8>Цена:</color> {0:0} влияния", PlanetStability.PropagandaInfluence) +
                             (running ? Loc.F("\n\nУже идёт: осталось {0} мес.", _planet.PropagandaMonths)
                              : !can && owned ? $"\n<color=#FF8888>{CostCheck.Full(0f, 0f, 0f, PlanetStability.PropagandaInfluence)}.</color>" : ""));
        }

        /// <summary>Проекты общества: сборка роботов и генная адаптация.</summary>
        private void RefreshSocietyActions(bool surveyed, bool owned)
        {
            var eco = EconomyManager.Instance;
            const string Muted = "#8AA2A8", Bad = "#FF8888";
            bool colony = surveyed && _planet.Population > 0;
            ConstructionJob robJob = FirstJob(JobKind.Robot), geneJob = FirstJob(JobKind.GeneAdapt);
            bool queueFull = Builds != null && Builds.QueueFor(_planet).Count >= ConstructionManager.MaxPlanetQueue;

            // Роботы
            string robCost = Loc.F("{0:0} спл., {1:0} гел. · {2:0} дн.", PlanetData.RobotAlloysCost, PlanetData.RobotEnergyCost, ConstructionManager.RobotDays);
            if (robJob != null) SetAction(_robots, true, LGIcon.Clock, Loc.F("Собираются · {0} дн. — нажмите, чтобы отменить", Mathf.CeilToInt(Builds.DaysUntilDone(robJob))));
            else if (!owned) SetAction(_robots, false, LGIcon.Lock, Loc.T("Не ваша система"));
            else if (!colony) SetAction(_robots, false, LGIcon.Lock, Loc.T("Нужна колония"));
            else if (!_planet.HasRoomForRobot) SetAction(_robots, false, LGIcon.Close, Loc.T("Нет свободного жилья"));
            else if (queueFull) SetAction(_robots, false, LGIcon.Clock, Loc.T("Очередь планеты заполнена"));
            else
            {
                bool can = eco != null && eco.CanAfford(PlanetData.RobotEnergyCost, 0f, PlanetData.RobotAlloysCost, 0f);
                SetAction(_robots, can, LGIcon.Industry, robCost);
            }
            var robot = SpeciesRegistry.Get(SpeciesRegistry.RobotId);
            _robots.Tip.SetText(Loc.T("<b>Сборка роботов</b>\n+1 житель-робот: занимает рабочее место, как обычный житель.\n") +
                Loc.F("<color=#9EF0C8>{0}</color> — {1}\n", TraitInfo.Name(SpeciesTrait.Mechanical), TraitInfo.Desc(SpeciesTrait.Mechanical)) +
                Loc.T("Роботы не бунтуют и не считаются чужим видом, но и не размножаются: на мире из одних роботов роста нет.\n") +
                Loc.F("<color={0}>Цена:</color> {1}", Muted, robCost) +
                (robJob == null && owned && colony && eco != null && !eco.CanAfford(PlanetData.RobotEnergyCost, 0f, PlanetData.RobotAlloysCost, 0f)
                    ? $"\n<color={Bad}>{CostCheck.Full(PlanetData.RobotEnergyCost, 0f, PlanetData.RobotAlloysCost)}.</color>" : "") +
                (robot != null && _planet.RobotPops > 0 ? Loc.F("\n\nРоботов на планете: {0}", _planet.RobotPops) : ""));

            // Генная адаптация
            var target = colony ? _planet.GeneTarget : null;
            var dom = colony ? SpeciesRegistry.Get(_planet.DominantOrganic) : null;
            string geneCost = Loc.F("{0:0} гел., {1:0} вл. · {2:0} дн.", PlanetData.GeneEnergyCost, PlanetData.GeneInfluenceCost, ConstructionManager.GeneAdaptDays);
            if (geneJob != null) SetAction(_gene, true, LGIcon.Clock, Loc.F("Идёт · {0} дн. — нажмите, чтобы отменить", Mathf.CeilToInt(Builds.DaysUntilDone(geneJob))));
            else if (!owned) SetAction(_gene, false, LGIcon.Lock, Loc.T("Не ваша система"));
            else if (!colony) SetAction(_gene, false, LGIcon.Lock, Loc.T("Нужна колония"));
            else if (dom == null) SetAction(_gene, false, LGIcon.Close, Loc.T("Нет органических жителей"));
            else if (target == null) SetAction(_gene, false, LGIcon.Check, Loc.T("Вид уже идеально подходит этому миру"), true);
            else if (queueFull) SetAction(_gene, false, LGIcon.Clock, Loc.T("Очередь планеты заполнена"));
            else
            {
                bool can = eco != null && eco.CanAfford(PlanetData.GeneEnergyCost, 0f, 0f, PlanetData.GeneInfluenceCost);
                SetAction(_gene, can, LGIcon.Research, geneCost);
            }
            string geneTip = Loc.T("<b>Генная адаптация</b>\nВыводит линию самого многочисленного вида колонии под климат этой планеты.\n");
            if (target != null)
            {
                var fix = SpeciesRegistry.FixableTrait(target);
                geneTip += Loc.F("Вид: <b>{0}</b>\n• родной мир — {1}: штраф «чужой климат» исчезнет\n", target.DisplayName, PlanetData.ClassName(_planet.Type));
                geneTip += fix.HasValue ? Loc.F("• уйдёт слабость «{0}»\n", TraitInfo.Name(fix.Value))
                                        : Loc.F("• вид станет «{0}» (рост +25%)\n", TraitInfo.Name(SpeciesTrait.RapidBreeders));
            }
            geneTip += Loc.F("<color={0}>Цена:</color> {1}", Muted, geneCost);
            if (geneJob == null && target != null && owned && eco != null && !eco.CanAfford(PlanetData.GeneEnergyCost, 0f, 0f, PlanetData.GeneInfluenceCost))
                geneTip += $"\n<color={Bad}>{CostCheck.Full(PlanetData.GeneEnergyCost, 0f, 0f, PlanetData.GeneInfluenceCost)}.</color>";
            _gene.Tip.SetText(geneTip);
        }

        /// <summary>Досье планеты — стеклянная пластина поверх низа планеты: шкалы слева, климат справа.</summary>
        private void BuildDossier(RectTransform rt)
        {
            var card = Glass(rt, "Dossier", LGIcon.Planet, Loc.T("ДОСЬЕ ПЛАНЕТЫ"), CGold, LGAppear.Kind.SlideUp, 0.10f, out var body);
            var r = card.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0, 0);
            r.pivot = new Vector2(0, 0);
            r.anchoredPosition = new Vector2(Pad, 24);
            r.sizeDelta = new Vector2(560, 236);

            _dossier = new Seg[4];
            string[] labels = { Loc.T("Пригодность"), Loc.T("Районы · размер"), Loc.T("Титан"), Loc.T("Гелий-3") };
            for (int i = 0; i < _dossier.Length; i++) _dossier[i] = MakeSeg(body, labels[i], 4 + i * 34, 290f);

            var vline = LGBuild.Panel(body, "Split", new Color(0.45f, 0.95f, 0.90f, 0.16f));
            vline.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(312, -4), new Vector2(1, 136));
            LG.Ignore(vline.gameObject);

            _climate = LGBuild.Label(body, "", 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            _climate.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(328, -2), new Vector2(200, 140));
            _climate.lineSpacing = 1.25f;

            _stationText = LGBuild.Label(body, "", 11, CMuted, TextAnchor.LowerLeft, wrap: true);
            _stationText.rectTransform.Stretch(0, 2, 0, 0);
        }

        private static Seg MakeSeg(RectTransform parent, string label, float top, float width)
        {
            var s = new Seg { Cells = new Image[5] };
            var l = LGBuild.Label(parent, label, 11, CMuted, TextAnchor.UpperLeft);
            l.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -top), new Vector2(width, 16));
            s.Value = LGBuild.Label(parent, "", 12, UIManager.DS.TextPrimary, TextAnchor.UpperRight, bold: true);
            s.Value.rectTransform.At(new Vector2(0, 1), new Vector2(1, 1), new Vector2(width, -top), new Vector2(120, 16));
            float cw = (width - 4f * 4f) / 5f;
            for (int i = 0; i < 5; i++)
            {
                var c = LGBuild.Panel(parent, "Cell", CSegOff);
                c.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(i * (cw + 4f), -top - 19), new Vector2(cw, 6));
                LG.Ignore(c.gameObject);
                s.Cells[i] = c;
            }
            return s;
        }

        /// <summary>Пять плиток над районами: производство за месяц и занятость.</summary>
        private void BuildTiles(RectTransform rt)
        {
            var row = LGBuild.Rect(rt, "Tiles");
            row.anchorMin = new Vector2(RightX, 1);
            row.anchorMax = new Vector2(1, 1);
            row.pivot = new Vector2(0.5f, 1);
            row.offsetMin = new Vector2(0, -TilesTop - TilesH);
            row.offsetMax = new Vector2(-Pad, -TilesTop);

            (LGIcon icon, Color col, string cap)[] defs =
            {
                (LGIcon.Minerals, CMinerals, Loc.T("ТИТАН / МЕС")),
                (LGIcon.Energy, CEnergy, Loc.T("ГЕЛИЙ-3 / МЕС")),
                (LGIcon.Alloys, CAlloys, Loc.T("СПЛАВЫ / МЕС")),
                (LGIcon.Research, CScience, Loc.T("НАУКА / МЕС")),
                (LGIcon.Population, CCyan, Loc.T("ЗАНЯТОСТЬ")),
            };
            _tiles = new Tile[defs.Length];
            float w = 1f / defs.Length;
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var tile = LGBuild.Panel(row, "Tile", CPanel);
                var tr = tile.rectTransform;
                tr.anchorMin = new Vector2(i * w, 0);
                tr.anchorMax = new Vector2((i + 1) * w, 1);
                tr.offsetMin = new Vector2(i == 0 ? 0 : Gap * 0.5f, 0);
                tr.offsetMax = new Vector2(i == defs.Length - 1 ? 0 : -Gap * 0.5f, 0);
                LG.Glass(tile.gameObject, 16f).SetRim(new Color(d.col.r, d.col.g, d.col.b, 0.35f));
                var mo = LG.Motion(tile.gameObject, LGAppear.Kind.SlideDown);
                mo.delay = 0.05f + i * 0.04f;
                mo.distance = 20f;

                var accent = LGBuild.Panel(tile.transform, "Accent", d.col);
                accent.rectTransform.anchorMin = new Vector2(0, 1);
                accent.rectTransform.anchorMax = new Vector2(1, 1);
                accent.rectTransform.offsetMin = new Vector2(18, -3);
                accent.rectTransform.offsetMax = new Vector2(-18, -1);
                LG.Ignore(accent.gameObject);

                var ic = LGIcons.Create(tile.transform, d.icon, 26, d.col);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 6), new Vector2(26, 26));
                var val = LGBuild.Label(tile.transform, "", 26, Color.white, TextAnchor.MiddleLeft, bold: true);
                val.rectTransform.Stretch(52, 30, 8, 12);
                var cap = LGBuild.Label(tile.transform, d.cap, 10, CMuted, TextAnchor.LowerLeft, bold: true);
                cap.rectTransform.Stretch(52, 12, 8, 0);
                _tiles[i] = new Tile { Value = val, Caption = cap };
            }

            _bankruptText = LGBuild.Label(rt, Loc.T("БАНКРОТСТВО: ПРОИЗВОДСТВО УРЕЗАНО ВДВОЕ"), 11, UIManager.DS.Red, TextAnchor.LowerRight, bold: true);
            _bankruptText.rectTransform.anchorMin = new Vector2(RightX, 1);
            _bankruptText.rectTransform.anchorMax = new Vector2(1, 1);
            _bankruptText.rectTransform.offsetMin = new Vector2(0, -TilesTop - 2);
            _bankruptText.rectTransform.offsetMax = new Vector2(-Pad, -TilesTop + 16);
            _bankruptText.gameObject.SetActive(false);
        }

        /// <summary>Районы: шапка с приоритетом и автостройкой, полоса слотов, четыре карточки сеткой 2×2.</summary>
        private void BuildDistricts(RectTransform rt)
        {
            var panel = LGBuild.Panel(rt, "Districts", CPanel);
            var p = panel.rectTransform;
            p.anchorMin = new Vector2(RightX, LowerShare);
            p.anchorMax = new Vector2(1, 1);
            p.offsetMin = new Vector2(0, Gap * 0.5f);
            p.offsetMax = new Vector2(-Pad, -DistrictsTop);
            LG.Glass(panel.gameObject, 22f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.36f));
            var mo = LG.Motion(panel.gameObject, LGAppear.Kind.SlideRight);
            mo.delay = 0.12f;
            mo.distance = 30f;

            var head = LGBuild.Rect(p, "Head");
            head.TopBand(14, 28, 20, 20);
            var hic = LGIcons.Create(head, LGIcon.Construction, 18, CGold);
            hic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(18, 18));
            _districtHeader = LGBuild.Label(head, "", 15, CGold, TextAnchor.MiddleLeft, bold: true);
            _districtHeader.rectTransform.offsetMin = new Vector2(26, 0);
            _districtHeader.rectTransform.offsetMax = new Vector2(-380, 0);
            BuildColonyControls(head);

            _districtSlots = LGBuild.Rect(p, "Slots");
            _districtSlots.TopBand(52, 6, 20, 20);

            var grid = LGBuild.Rect(p, "Grid");
            grid.Stretch(16, 16, 16, 70);
            DistrictType[] order = { DistrictType.Urban, DistrictType.Mining, DistrictType.Generator, DistrictType.Industrial };
            for (int i = 0; i < order.Length; i++)
            {
                int col = i % 2, row = i / 2;
                var cell = LGBuild.Rect(grid, "Cell");
                cell.anchorMin = new Vector2(col * 0.5f, 1f - (row + 1) * 0.5f);
                cell.anchorMax = new Vector2((col + 1) * 0.5f, 1f - row * 0.5f);
                cell.offsetMin = new Vector2(col == 0 ? 0 : 6, row == 1 ? 0 : 6);
                cell.offsetMax = new Vector2(col == 0 ? -6 : 0, row == 0 ? 0 : -6);
                _cards.Add(BuildDistrictCard(cell, order[i]));
            }
        }

        /// <summary>Справа в шапке районов: приоритет колонии (четыре кнопки-значка) и переключатель автостройки.</summary>
        private void BuildColonyControls(RectTransform head)
        {
            var host = LGBuild.Rect(head, "ColonyControls");
            host.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(370, 28));
            _colonyControls = host.gameObject;

            var lbl = LGBuild.Label(host, Loc.T("ПРИОРИТЕТ"), 10, CMuted, TextAnchor.MiddleLeft, bold: true);
            lbl.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(70, 28));

            LGIcon[] icons = { LGIcon.Globe, LGIcon.Minerals, LGIcon.Energy, LGIcon.Alloys };
            Color[] cols = { CCyan, CMinerals, CEnergy, CAlloys };
            _focusBg = new Image[ColonyFocusInfo.All.Length];
            for (int i = 0; i < _focusBg.Length; i++)
            {
                var f = ColonyFocusInfo.All[i];
                var b = LGBuild.Button(host, "Focus", new Color(0.07f, 0.13f, 0.17f), new Color(cols[i].r, cols[i].g, cols[i].b, 0.6f),
                                       () => SetFocus(f), icons[i], null, 10, 8f);
                ((RectTransform)b.transform).At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(72 + i * 34, 0), new Vector2(30, 28));
                foreach (var img in b.GetComponentsInChildren<Image>(true)) if (img.gameObject != b.gameObject) img.color = cols[i];
                _focusBg[i] = b.GetComponent<Image>();
                TooltipHelper.Attach(b.gameObject, Loc.F("<b>Приоритет: {0}</b>\n{1}", ColonyFocusInfo.Name(f), ColonyFocusInfo.Desc(f)));
            }

            var auto = LGBuild.Button(host, "Auto", new Color(0.07f, 0.13f, 0.17f), new Color(0.40f, 1f, 0.62f, 0.55f), ToggleAutoBuild, null, null, 10, 8f);
            ((RectTransform)auto.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(150, 28));
            _autoBg = auto.GetComponent<Image>();
            _autoIcon = LGIcons.Create(auto.transform, LGIcon.Construction, 13, CMuted);
            _autoIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(13, 13));
            _autoText = LGBuild.Label(auto.transform, "", 10, Color.white, TextAnchor.MiddleLeft, bold: true);
            _autoText.rectTransform.Stretch(29, 0, 6, 0);
            TooltipHelper.Attach(auto.gameObject,
                Loc.T("<b>Автостройка</b>\nКогда очередь пуста, колония раз в месяц сама заказывает район:\n") +
                Loc.T("• тесно — городской район (жильё);\n• есть безработные — район по приоритету колонии.\n") +
                Loc.T("<color=#8AA2A8>Ресурсы списываются как при обычном заказе. Ручные заказы не мешают.</color>"));
        }

        /// <summary>
        /// Карточка района: эмблема, название и эффект, крупное количество; посередине — фигурки рабочих
        /// и состояние с полосой стройки; внизу — стоимость и кнопка постройки.
        /// </summary>
        private DistrictCard BuildDistrictCard(RectTransform rt, DistrictType type)
        {
            Color dc = DistrictInfo.Color(type);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(dc.r * 0.10f, dc.g * 0.10f, dc.b * 0.10f, 0.95f);
            bg.raycastTarget = false;
            LG.Platter(rt.gameObject, 16f).SetRim(new Color(dc.r, dc.g, dc.b, 0.45f));

            // Свечение цвета района из левого верхнего угла (за край карточки не выходит)
            var glowHost = LGBuild.Rect(rt, "GlowClip");
            glowHost.Stretch(2, 2, 2, 2);
            glowHost.gameObject.AddComponent<RectMask2D>();
            var glow = Deco(glowHost, "Glow", HaloSprite(), new Color(dc.r, dc.g, dc.b, 0.12f));
            glow.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(40, -40), new Vector2(280, 280));

            LGIcon icon = type switch
            {
                DistrictType.Urban => LGIcon.Housing,
                DistrictType.Mining => LGIcon.Minerals,
                DistrictType.Generator => LGIcon.Energy,
                _ => LGIcon.Alloys
            };
            var badge = LGBuild.Panel(rt, "Badge", new Color(dc.r * 0.28f, dc.g * 0.28f, dc.b * 0.28f));
            badge.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -14), new Vector2(52, 52));
            LG.Platter(badge.gameObject, 26f).SetRim(new Color(dc.r, dc.g, dc.b, 0.8f));
            LGIcons.Create(badge.transform, icon, 28, dc);

            var name = LGBuild.Label(rt, DistrictName(type).ToUpper(), 14, Color.white, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.anchorMin = new Vector2(0, 1);
            name.rectTransform.anchorMax = new Vector2(1, 1);
            name.rectTransform.offsetMin = new Vector2(78, -34);
            name.rectTransform.offsetMax = new Vector2(-80, -14);
            var effect = LGBuild.Label(rt, DistrictEffect(type), 11, CMuted, TextAnchor.UpperLeft, wrap: true);
            effect.rectTransform.anchorMin = new Vector2(0, 1);
            effect.rectTransform.anchorMax = new Vector2(1, 1);
            effect.rectTransform.offsetMin = new Vector2(78, -68);
            effect.rectTransform.offsetMax = new Vector2(-16, -34);

            var count = LGBuild.Label(rt, "", 28, dc, TextAnchor.UpperRight, bold: true);
            count.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -10), new Vector2(110, 34));

            // Рабочие и состояние — середина карточки
            var workers = LGBuild.Rect(rt, "Workers");
            workers.anchorMin = new Vector2(0, 0.5f);
            workers.anchorMax = new Vector2(1, 0.5f);
            workers.pivot = new Vector2(0, 0.5f);
            workers.offsetMin = new Vector2(16, -12);
            workers.offsetMax = new Vector2(-16, 8);
            var status = LGBuild.Label(rt, "", 11, CMuted, TextAnchor.UpperLeft);
            status.rectTransform.anchorMin = new Vector2(0, 0.5f);
            status.rectTransform.anchorMax = new Vector2(1, 0.5f);
            status.rectTransform.offsetMin = new Vector2(16, -32);
            status.rectTransform.offsetMax = new Vector2(-16, -14);
            var progHost = LGBuild.Rect(rt, "ProgressHost");
            progHost.anchorMin = new Vector2(0, 0.5f);
            progHost.anchorMax = new Vector2(1, 0.5f);
            progHost.offsetMin = new Vector2(16, -40);
            progHost.offsetMax = new Vector2(-16, -34);
            var progress = LGBuild.Bar(progHost, CGold, 0f, 4f);

            // Низ: стоимость, отмена, постройка
            var minCost = MakeResLine(rt, LGIcon.Minerals, CMinerals, 14, 12, bold: true);
            ((RectTransform)minCost.Go.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(16, 16), new Vector2(64, 30));
            var alloyCost = MakeResLine(rt, LGIcon.Alloys, CAlloys, 14, 12, bold: true);
            ((RectTransform)alloyCost.Go.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(80, 16), new Vector2(64, 30));

            var right = new Vector2(1, 0);
            var btn = LGBuild.Button(rt, "Build", new Color(dc.r * 0.42f, dc.g * 0.42f, dc.b * 0.42f), new Color(dc.r, dc.g, dc.b, 0.8f),
                                     () => OnBuildDistrict(type), LGIcon.Construction, Loc.T("ПОСТРОИТЬ"), 11);
            ((RectTransform)btn.transform).At(right, new Vector2(1, 0), new Vector2(-14, 14), new Vector2(200, 34));
            var cancel = LGBuild.Button(rt, "Cancel", UIManager.DS.BtnDanger, UIManager.DS.Red, () => OnCancelDistrict(type), LGIcon.Close, null, 10, 12f);
            ((RectTransform)cancel.transform).At(right, new Vector2(1, 0), new Vector2(-222, 15), new Vector2(32, 32));
            TooltipHelper.Attach(cancel.gameObject, Loc.T("<b>Отменить</b>\nПоследний заказанный район этого типа. Ресурсы вернутся полностью."));
            cancel.gameObject.SetActive(false);

            TooltipHelper.Attach(btn.gameObject,
                Loc.F("<b>{0} район</b>\n{1}\n\n", DistrictName(type), DistrictEffect(type)) +
                Loc.F("Строительство: {0:0} дн. (на планете — одна стройка за раз)\n", ConstructionManager.DistrictDays(type)) +
                Loc.F("Стоимость: {0} титана, {1} сплавов\n", DistrictInfo.MineralsCost(type), DistrictInfo.AlloysCost(type)) +
                Loc.T("<color=#8AA2A8>Каждый район — одно рабочее место. Производство идёт, только если на нём работают жители.</color>"));

            return new DistrictCard
            {
                Type = type, Count = count, Status = status, MinCost = minCost, AlloyCost = alloyCost,
                Build = btn, Cancel = cancel, Progress = progress, Workers = workers,
                BuildBg = btn.GetComponent<Image>(), BuildTint = new Color(dc.r * 0.42f, dc.g * 0.42f, dc.b * 0.42f),
                Reason = btn.GetComponentInChildren<Text>(),
                Tip = btn.GetComponent<TooltipTrigger>()
            };
        }

        /// <summary>Очередь строительства планеты: до пяти строк, у первой — полоса прогресса.</summary>
        private void BuildQueue(RectTransform rt)
        {
            var card = Glass(rt, "Queue", LGIcon.Clock, Loc.T("ОЧЕРЕДЬ СТРОИТЕЛЬСТВА"), CGold, LGAppear.Kind.SlideUp, 0.16f, out var body);
            var r = card.rectTransform;
            r.anchorMin = new Vector2(RightX, 0);
            r.anchorMax = new Vector2(RightX + (1f - RightX) * 0.58f, LowerShare);
            r.offsetMin = new Vector2(0, 24);
            r.offsetMax = new Vector2(-Gap * 0.5f, -Gap * 0.5f);

            _queueHint = LGBuild.Label(card.rectTransform, "", 11, CMuted, TextAnchor.UpperRight, bold: true);
            _queueHint.rectTransform.TopBand(16, 18, 20, 178);
            _buildMenuBtn = LGBuild.Button(card.rectTransform, "BuildMenu", new Color(0.12f, 0.36f, 0.40f), CCyan,
                () => { if (_planet != null && Owned) BuildMenuModal.Open(_planet); }, LGIcon.Construction, Loc.T("СТРОИТЬ…"), 11, 10f);
            ((RectTransform)_buildMenuBtn.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -10), new Vector2(150, 30));
            TooltipHelper.Attach(_buildMenuBtn.gameObject, Loc.T("<b>Меню строительства</b>\nВсе постройки, проекты и корабли этой планеты с ценой и сроком  <color=#8AA2A8>(B)</color>"));

            // Пустая очередь: значок, подпись и кнопка «заказать то, что нужно колонии»
            var empty = LGBuild.Rect(body, "Empty");
            empty.Stretch();
            _queueEmptyGo = empty.gameObject;
            var eIcon = LGIcons.Create(empty, LGIcon.Construction, 40, new Color(CGold.r, CGold.g, CGold.b, 0.30f));
            eIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 58), new Vector2(40, 40));
            var eTitle = LGBuild.Label(empty, Loc.T("ОЧЕРЕДЬ ПУСТА"), 13, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, bold: true);
            eTitle.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 22), new Vector2(500, 20));
            _queueEmpty = LGBuild.Label(empty, "", 12, CMuted, TextAnchor.MiddleCenter, wrap: true);
            _queueEmpty.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(500, 20));
            _suggestBtn = LGBuild.Button(empty, "Suggest", new Color(0.12f, 0.36f, 0.40f), CCyan, OnSuggest, null, null, 11);
            ((RectTransform)_suggestBtn.transform).At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -44), new Vector2(380, 36));
            var sIcon = LGIcons.Create(_suggestBtn.transform, LGIcon.Construction, 14, Color.white);
            sIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(14, 14));
            _suggestText = LGBuild.Label(_suggestBtn.transform, "", 11, Color.white, TextAnchor.MiddleCenter, bold: true);
            _suggestText.rectTransform.Stretch(30, 0, 10, 0);
            TooltipHelper.Attach(_suggestBtn.gameObject, Loc.T("<b>Рекомендуемый район</b>\nТот же выбор, что делает автостройка: тесно — жильё, есть безработные — район по приоритету колонии."));

            _queueRows = new QueueRow[ConstructionManager.MaxPlanetQueue];
            for (int i = 0; i < _queueRows.Length; i++)
            {
                int idx = i;
                var row = LGBuild.Rect(body, "Row");
                row.anchorMin = new Vector2(0, 1f - (i + 1) / (float)_queueRows.Length);
                row.anchorMax = new Vector2(1, 1f - i / (float)_queueRows.Length);
                row.offsetMin = new Vector2(0, 2);
                row.offsetMax = new Vector2(0, -2);
                var rowBg = row.gameObject.AddComponent<Image>();
                rowBg.color = new Color(1f, 1f, 1f, i == 0 ? 0.05f : 0.025f);
                rowBg.raycastTarget = false;
                LG.Platter(row.gameObject, 10f).SetRim(new Color(1f, 1f, 1f, i == 0 ? 0.14f : 0.06f));

                var ic = LGIcons.Create(row, LGIcon.Construction, 18, CGold);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 2), new Vector2(18, 18));
                var nm = LGBuild.Label(row, "", 12, Color.white, TextAnchor.MiddleLeft, bold: true);
                nm.rectTransform.Stretch(40, 6, 230, 0);
                var days = LGBuild.Label(row, "", 11, CMuted, TextAnchor.MiddleRight, bold: true);
                days.rectTransform.Stretch(0, 6, 128, 0);
                var progHost = LGBuild.Rect(row, "ProgressHost");
                progHost.anchorMin = new Vector2(0, 0);
                progHost.anchorMax = new Vector2(1, 0);
                progHost.offsetMin = new Vector2(40, 4);
                progHost.offsetMax = new Vector2(-128, 8);
                var prog = LGBuild.Bar(progHost, CGold, 0f, 3f);
                var cancel = LGBuild.Button(row, "Cancel", UIManager.DS.BtnDanger, UIManager.DS.Red, () => OnCancelQueued(idx), LGIcon.Close, null, 9, 10f);
                ((RectTransform)cancel.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(28, 28));
                TooltipHelper.Attach(cancel.gameObject, Loc.T("<b>Отменить заказ</b>\nРесурсы вернутся полностью."));

                // Купить текущую стройку за гелий-3 (как в Master of Orion)
                var buy = LGBuild.Button(row, "Buy", new Color(0.36f, 0.28f, 0.06f), CGold, () => OnBuyQueued(idx), null, null, 9, 10f);
                ((RectTransform)buy.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-42, 0), new Vector2(80, 28));
                var bIc = LGIcons.Create(buy.transform, LGIcon.Energy, 12, CEnergy);
                bIc.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(9, 0), new Vector2(12, 12));
                var bText = LGBuild.Label(buy.transform, "", 11, Color.white, TextAnchor.MiddleLeft, bold: true);
                bText.rectTransform.Stretch(25, 0, 4, 0);
                TooltipHelper.Attach(buy.gameObject, Loc.T("Купить"));

                _queueRows[i] = new QueueRow { Go = row.gameObject, Icon = ic, Name = nm, Days = days, Progress = prog, Cancel = cancel,
                                               Buy = buy, BuyBg = buy.GetComponent<Image>(), BuyText = bText, BuyTip = buy.GetComponent<TooltipTrigger>() };
            }
        }

        /// <summary>Проекты планеты: три крупные кнопки столбиком.</summary>
        private void BuildProjects(RectTransform rt)
        {
            var card = Glass(rt, "Projects", LGIcon.Planet, Loc.T("ПРОЕКТЫ ПЛАНЕТЫ"), CCyan, LGAppear.Kind.SlideUp, 0.20f, out var body);
            var r = card.rectTransform;
            r.anchorMin = new Vector2(RightX + (1f - RightX) * 0.58f, 0);
            r.anchorMax = new Vector2(1, LowerShare);
            r.offsetMin = new Vector2(Gap * 0.5f, 24);
            r.offsetMax = new Vector2(-Pad, -Gap * 0.5f);

            _mine = BuildAction(body, 0, Loc.T("ДОБЫВАЮЩИЙ КОМПЛЕКС"), new Color(0.10f, 0.36f, 0.26f), UIManager.DS.Green, OnMine);
            _colony = BuildAction(body, 1, Loc.T("КОЛОНИЯ"), new Color(0.10f, 0.32f, 0.40f), CCyan, OnColony);
            _terra = BuildAction(body, 2, Loc.T("ТЕРРАФОРМИНГ"), new Color(0.24f, 0.20f, 0.42f), new Color(0.72f, 0.60f, 1f), OnTerra);
            _robots = BuildAction(body, 3, Loc.T("СБОРКА РОБОТОВ"), new Color(0.16f, 0.24f, 0.32f), new Color(0.62f, 0.78f, 0.92f), OnRobots);
            _gene = BuildAction(body, 4, Loc.T("ГЕННАЯ АДАПТАЦИЯ"), new Color(0.30f, 0.14f, 0.30f), new Color(0.98f, 0.55f, 0.85f), OnGene);
        }

        /// <summary>Проект планеты: круглый значок, название и строка состояния (цена, срок или итог).</summary>
        private static ActionButton BuildAction(RectTransform parent, int index, string title, Color tint, Color accent, System.Action onClick)
        {
            var b = LGBuild.Button(parent, "Project", tint, new Color(accent.r, accent.g, accent.b, 0.55f), onClick, null, null, 11, 14f);
            var r = (RectTransform)b.transform;
            r.anchorMin = new Vector2(0, 1f - (index + 1) / (float)ProjectCount);
            r.anchorMax = new Vector2(1, 1f - index / (float)ProjectCount);
            r.offsetMin = new Vector2(0, 3);
            r.offsetMax = new Vector2(0, -3);

            var badge = LGBuild.Panel(b.transform, "Badge", new Color(0.05f, 0.1f, 0.12f));
            badge.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(38, 38));
            LG.Platter(badge.gameObject, 19f).SetRim(new Color(accent.r, accent.g, accent.b, 0.6f));
            var ic = LGIcons.Create(badge.transform, LGIcon.Check, 18, Color.white);

            var t = LGBuild.Label(b.transform, title, 12, accent, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.anchorMin = new Vector2(0, 0.5f);
            t.rectTransform.anchorMax = new Vector2(1, 1);
            t.rectTransform.offsetMin = new Vector2(62, 1);
            t.rectTransform.offsetMax = new Vector2(-10, 0);
            var st = LGBuild.Label(b.transform, "", 11, Color.white, TextAnchor.UpperLeft, wrap: true);
            st.rectTransform.anchorMin = new Vector2(0, 0);
            st.rectTransform.anchorMax = new Vector2(1, 0.5f);
            st.rectTransform.offsetMin = new Vector2(62, 0);
            st.rectTransform.offsetMax = new Vector2(-10, -2);
            TooltipHelper.Attach(b.gameObject, title);
            return new ActionButton { Btn = b, Bg = b.GetComponent<Image>(), Icon = ic, Badge = badge, Status = st, Tint = tint, Accent = accent,
                                      Tip = b.GetComponent<TooltipTrigger>() };
        }

        /// <summary>Стеклянная карточка с заголовком; тело — область под заголовком.</summary>
        private static Image Glass(RectTransform parent, string name, LGIcon icon, string title, Color accent, LGAppear.Kind appear, float delay, out RectTransform body)
        {
            var card = LGBuild.Panel(parent, name, CPanel);
            LG.Glass(card.gameObject, 20f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.32f));
            var mo = LG.Motion(card.gameObject, appear);
            mo.delay = delay;
            mo.distance = 30f;

            var head = LGBuild.Rect(card.rectTransform, "Head");
            head.TopBand(14, 22, 20, 20);
            var ic = LGIcons.Create(head, icon, 16, accent);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16));
            var t = LGBuild.Label(head, title, 12, accent, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.offsetMin = new Vector2(24, 0);

            body = LGBuild.Rect(card.rectTransform, "Body");
            body.Stretch(20, 14, 20, 46);
            return card;
        }

        private static ResLine MakeResLine(RectTransform parent, LGIcon icon, Color col, float iconSize, int fontSize, bool bold = false)
        {
            var rt = LGBuild.Rect(parent, "Res_" + icon);
            var ic = LGIcons.Create(rt, icon, iconSize, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));
            ic.raycastTarget = false;
            var t = LGBuild.Label(rt, "", fontSize, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, bold);
            t.rectTransform.Stretch(iconSize + 6, 0, 0, 0);
            t.supportRichText = true;
            return new ResLine { Go = rt.gameObject, Icon = ic, Text = t };
        }

        private static string DistrictName(DistrictType t) => t switch
        {
            DistrictType.Urban => Loc.T("Городской"),
            DistrictType.Mining => Loc.T("Горнодобывающий"),
            DistrictType.Generator => Loc.T("Энергетический"),
            _ => Loc.T("Промышленный")
        };

        /// <summary>
        /// Подсказка кнопки района: нужен ли он сейчас, будет ли кому на нём работать, сколько даст и когда окупится,
        /// а если кнопка серая — почему именно.
        /// </summary>
        private string DistrictTip(DistrictType t, string blocked)
        {
            const string M = "#8AA2A8", Good = "#5CE68A", Warn = "#FFB070", Bad = "#FF8888";
            float mc = DistrictInfo.MineralsCost(t), ac = DistrictInfo.AlloysCost(t);
            var sb = new System.Text.StringBuilder();
            sb.Append(Loc.F("<b>{0} район</b>\n{1}\n", DistrictName(t), DistrictEffect(t)));

            int pop = _planet.Population, housing = _planet.HousingCapacity;
            if (pop > 0)
            {
                sb.Append('\n');
                if (t == DistrictType.Urban)
                {
                    if (pop >= housing)
                        sb.Append(Loc.F("<color={0}>Нужен сейчас:</color> жильё заполнено ({1}/{2}), население не растёт. Район даст +{3} места — рост продолжится.\n", Good, pop, housing, DistrictInfo.HousingPopCapacity(t)));
                    else
                        sb.Append(Loc.F("Жильё {0}/{1}: свободно {2} мест, население растёт. Этот район понадобится позже.\n", pop, housing, housing - pop));
                }
                else
                {
                    int jobs = _planet.WorkerSlots + _planet.PendingDistricts;   // заказанные районы тоже займут жителей
                    int free = pop - jobs;
                    float output = t == DistrictType.Industrial ? 3f : 4f;
                    string res = t == DistrictType.Mining ? Loc.T("титана") : t == DistrictType.Generator ? Loc.T("гелия-3") : Loc.T("сплавов");
                    if (free > 0)
                        sb.Append(Loc.F("<color={0}>Заработает сразу:</color> свободных жителей — {1}.\n", Good, free));
                    else if (pop >= housing)
                        sb.Append(Loc.F("<color={0}>Пока некому работать:</color> все жители заняты, а жильё заполнено. Сначала постройте городской район.\n", Warn));
                    else
                    {
                        int months = _planet.MonthsToNextPop;
                        sb.Append(Loc.F("<color={0}>Пока некому работать:</color> все жители заняты. Район начнёт давать доход с новым жителем", Warn) +
                                  (months > 0 ? Loc.F(" (~{0} мес.)", months) : "") + ".\n");
                    }
                    sb.Append(Loc.F("Даст ≈ +{0:0} {1} в месяц, за 2 года ≈ +{2:0}.", output, res, output * 24f));
                    if (t == DistrictType.Mining)
                        sb.Append(Loc.F(" Окупится по титану за ~{0} мес. работы.", CostCheck.PaybackMonths(mc, output)));
                    sb.Append('\n');
                }
            }

            sb.Append(Loc.F("\n<color={0}>Цена:</color> {1:0} титана, {2:0} сплавов · {3:0} дн. (на планете одна стройка за раз)", M, mc, ac, ConstructionManager.DistrictDays(t)));
            if (blocked != null) sb.Append($"\n<color={Bad}>{blocked}</color>");
            return sb.ToString();
        }

        private static string DistrictEffect(DistrictType t) => t switch
        {
            DistrictType.Urban => Loc.T("+2 жилья и рабочее место. Без жилья рост встаёт."),
            DistrictType.Mining => Loc.T("+4 титана в месяц за каждый занятый район."),
            DistrictType.Generator => Loc.T("+4 гелия-3 в месяц — оплачивает флот и форпосты."),
            _ => Loc.T("+3 сплава в месяц — на корабли, форпосты и районы.")
        };

        // ================================================================ Процедурные спрайты

        private static Sprite s_halo, s_vignette, s_dash, s_ticks;
        private static readonly Dictionary<int, Sprite> s_rings = new Dictionary<int, Sprite>();

        /// <summary>Белая текстура с альфой alpha(dx, dy) — координаты от −1 до 1 от центра.</summary>
        private static Sprite MakeSprite(int res, System.Func<float, float, float> alpha, string name)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = name, hideFlags = HideFlags.DontSave };
            var px = new Color32[res * res];
            float c = (res - 1) * 0.5f;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(dx, dy)) * 255f);
                px[y * res + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            var sp = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
            sp.hideFlags = HideFlags.DontSave;
            return sp;
        }

        /// <summary>Тонкое кольцо (толщина в пикселях текстуры) — для шкал вокруг планеты.</summary>
        private static Sprite RingSprite(int res, float thickness)
        {
            int key = res * 100 + Mathf.RoundToInt(thickness * 10f);
            if (s_rings.TryGetValue(key, out var sp) && sp != null) return sp;
            float half = res * 0.5f;
            float r = 1f - (thickness + 2f) / half;
            float w = thickness / half;
            sp = MakeSprite(res, (dx, dy) =>
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.Clamp01((w * 0.5f - Mathf.Abs(d - r)) * half + 0.5f);
            }, "PlanetRing");
            s_rings[key] = sp;
            return sp;
        }

        /// <summary>Мягкий радиальный ореол — атмосфера за планетой и подсветка карточек.</summary>
        private static Sprite HaloSprite()
        {
            if (s_halo != null) return s_halo;
            s_halo = MakeSprite(256, (dx, dy) =>
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                return a * a * (3f - 2f * a);
            }, "PlanetHalo");
            return s_halo;
        }

        /// <summary>Виньетка: края экрана темнее, центр прозрачный.</summary>
        private static Sprite VignetteSprite()
        {
            if (s_vignette != null) return s_vignette;
            s_vignette = MakeSprite(256, (dx, dy) =>
            {
                float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 1.1f);
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.35f, d));
            }, "PlanetVignette");
            return s_vignette;
        }

        /// <summary>Пунктирное кольцо с тремя длинными просветами — медленно вращается вокруг планеты.</summary>
        private static Sprite DashRingSprite()
        {
            if (s_dash != null) return s_dash;
            const int res = 512;
            float half = res * 0.5f;
            s_dash = MakeSprite(res, (dx, dy) =>
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01((1.2f / half - Mathf.Abs(d - 0.985f)) * half);
                float ang = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 0.5f;
                float dash = Mathf.Repeat(ang * 90f, 1f) < 0.55f ? 1f : 0f;
                float gap = Mathf.Repeat(ang * 3f, 1f);
                float gapK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.06f, gap))
                           * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.34f, gap)));
                return ring * dash * gapK;
            }, "PlanetDashRing");
            return s_dash;
        }

        /// <summary>Внешнее кольцо с делениями: короткие штрихи каждые 5°, длинные — каждые 30°.</summary>
        private static Sprite TickRingSprite()
        {
            if (s_ticks != null) return s_ticks;
            const int res = 512;
            float half = res * 0.5f;
            s_ticks = MakeSprite(res, (dx, dy) =>
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = (Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 360f) % 360f;
                float line = Mathf.Clamp01((0.9f / half - Mathf.Abs(d - 0.93f)) * half);
                float minor = Mathf.Abs(Mathf.Repeat(ang + 2.5f, 5f) - 2.5f) < 0.35f && d > 0.945f && d < 0.975f ? 0.7f : 0f;
                float major = Mathf.Abs(Mathf.Repeat(ang + 15f, 30f) - 15f) < 0.5f && d > 0.94f && d < 0.995f ? 1f : 0f;
                return Mathf.Max(line * 0.6f, Mathf.Max(minor, major));
            }, "PlanetTickRing");
            return s_ticks;
        }
    }
}
