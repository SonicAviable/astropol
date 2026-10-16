using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Generation;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Экран колонии — по мотивам экрана колонии Master of Orion, в стиле игры:
    ///   • слева сверху — строка показателей: жители, производство за месяц, стабильность, занятость;
    ///   • под ней — приоритет колонии (выпадающий список) и автостройка;
    ///   • ряды работ: добыча титана, энергетика, промышленность, город — фигурки жителей на рабочих местах
    ///     (цвет — их вид), над каждым местом — сколько оно даёт; клик по ряду — приоритет на этот ресурс,
    ///     «+» в конце ряда — построить ещё район этого вида;
    ///   • под рядами — производство: текущая стройка, «Купить» за гелий-3, очередь и «+» (меню строительства);
    ///   • внизу слева — проекты планеты: добывающий комплекс, колония, терраформинг, роботы, генная адаптация;
    ///   • справа — крупная вращающаяся планета, сверху справа — информация о планете, под ней — общество;
    ///   • справа внизу — предыдущая / следующая колония и «Готово».
    /// Переключение колоний — также Tab / Shift+Tab, меню строительства — B, закрыть — Esc.
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

        // Дуги-шкалы вокруг планеты: население и рост
        private const float PopArc = 0.30f, GrowArc = 0.20f;

        // Шапка
        private Text _title, _subtitle, _ownerText;
        private Image _ownerIcon, _titleAccent;
        private GameObject _govChip;
        private LeaderThumb _govFace;
        private Image _govIcon;
        private Text _govText;

        // Строка показателей
        private StatCell[] _stats;
        private Text _bankruptText;

        // Приоритет и автостройка
        private Text _focusText, _autoText;
        private Image _autoBg, _autoIcon;
        private GameObject _focusList, _priorityRow;
        private Image[] _focusOptBg;

        // Ряды работ
        private JobRow[] _jobs;
        private Text _idleText, _noColonyText;

        // Производство
        private GameObject _prodRoot;
        private Image _curIcon, _curTileBg;
        private Text _curDays, _curName, _curSub, _buyText;
        private RectTransform _curBar, _queueTiles;
        private Button _buyBtn, _addBtn;
        private Image _buyBg;
        private TooltipTrigger _buyTip, _curTip;
        private string _queueKey = "";

        // Информация о планете
        private Seg[] _dossier;            // пригодность, размер, титан, гелий-3
        private Text _climate, _stationText;

        // Общество: стабильность и виды
        private GameObject _society;
        private Text _stabValue, _stabEffect, _compCaption, _propText;
        private RectTransform _stabBar;
        private Image[] _compSegs;
        private Text[] _legend;
        private TooltipTrigger _stabTip, _compTip, _propTip;
        private Button _propBtn;
        private Image _propBg;

        // Проекты
        private ActionButton _mine, _colony, _terra, _robots, _gene;
        private const int ProjectCount = 5;

        // Навигация по колониям
        private Text _navText;
        private Button _prevBtn, _nextBtn;

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
        private static readonly Color CRedAccent = new Color(0.95f, 0.36f, 0.36f);

        // Раскладка (в единицах канваса 1920×1080)
        private const float Pad = 40f, Gap = 14f, HeaderH = 104f;
        private const float LeftW = 800f;                       // левая колонна: показатели, ряды работ, производство
        private const float StatsTop = HeaderH + 14f, StatsH = 74f;
        private const float PriorityTop = StatsTop + StatsH + 18f;
        private const float JobsTop = PriorityTop + 58f, JobRowH = 84f;
        private const float ProdTop = JobsTop + 4 * JobRowH + 34f, ProdTileS = 92f;
        private const float InfoW = 470f, InfoH = 300f;
        private const int MaxSlots = 7;

        // ================================================================ Типы

        private class StatCell
        {
            public Text Caption, Value;
            public Image Icon;
            public TooltipTrigger Tip;
        }

        /// <summary>Ряд работ: район одного вида, его рабочие места и жители на них.</summary>
        private class JobRow
        {
            public DistrictType Type;
            public ColonyFocus Focus;
            public Image Bg, Rim, IconBg;
            public Text Value, Sub;
            public RectTransform Bar, Slots;
            public Button Add;
            public Image AddBg;
            public TooltipTrigger AddTip, InfoTip;
            public string SlotKey = "";
        }

        private class Seg
        {
            public Text Value;
            public Image[] Cells;
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

        /// <summary>Сцена планеты — квадрат: 62% высоты экрана, но не шире 40% ширины.</summary>
        private void LayoutStage()
        {
            if (_stage == null || _rootRt == null) return;
            var r = _rootRt.rect;
            float s = Mathf.Min(r.height * 0.86f, r.width * 0.50f);
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

        // ================================================================ Обновление (экран колонии)

        private bool Surveyed => _system != null && _system.IsSurveyed;
        private bool Owned => _planet != null && _planet.IsPlayerOwned;
        private bool IsColony => Surveyed && _planet != null && _planet.Population > 0;

        private void Refresh()
        {
            if (_planet == null || _root == null) return;
            RefreshHeader();
            RefreshStats();
            RefreshPriority();
            RefreshJobs();
            RefreshProduction();
            RefreshInfo();
            RefreshSociety();
            RefreshActions();
            RefreshNav();
        }

        /// <summary>Производство района одного вида на одного работника за месяц (с фракцией, технологиями, губернатором, видами и стабильностью).</summary>
        private float PerWorker(DistrictType t)
        {
            if (t == DistrictType.Urban) return 0f;
            var b = Bonuses();
            string res = t == DistrictType.Mining ? "minerals" : t == DistrictType.Generator ? "energy" : "alloys";
            float baseOut = t == DistrictType.Industrial ? 3f : 4f;
            float tech = t == DistrictType.Mining ? b.MineralsMult : t == DistrictType.Generator ? b.EnergyMult : b.AlloysMult;
            return baseOut * FactionMult(t) * tech * LeaderManager.PlanetOutputMult(_planet, res) * _planet.OutputMult(res);
        }

        private float Produced(DistrictType t)
        {
            if (!IsColony) return 0f;
            return PerWorker(t) * _planet.WorkedCount(t);
        }

        private float StationOutput(DistrictType t)
        {
            if (!Surveyed || !_planet.HasMiningStation) return 0f;
            var b = Bonuses();
            return t == DistrictType.Mining ? _planet.MineralDeposit * FactionMult(t) * b.MineralsMult
                 : t == DistrictType.Generator ? _planet.EnergyDeposit * FactionMult(t) * b.EnergyMult : 0f;
        }

        private float ScienceOut()
        {
            if (!IsColony) return 0f;
            var tm = TechnologyManager.Instance;
            return _planet.ResearchPops * TechnologyManager.SciencePerPop * (tm != null ? tm.GlobalResearchSpeedMultiplier : 1f);
        }

        private static string Plus(float v) => v > 0.05f ? $"+{v:0.#}" : "0";

        // ---------------- Строка показателей ----------------

        private void RefreshStats()
        {
            string m = LGBuild.Hex(CMuted);
            bool colony = IsColony;
            int pop = _planet.Population, housing = _planet.HousingCapacity;

            // Жители
            var s0 = _stats[0];
            if (colony)
            {
                s0.Value.text = $"{pop}<size=14><color={m}> / {housing}</color></size>";
                int months = _planet.MonthsToNextPop;
                s0.Tip.SetText(Loc.F("<b>Жители: {0} из {1}</b>\n", pop, housing) +
                    (pop >= housing ? Loc.T("<color=#F2C46B>Рост остановлен — нет жилья. Постройте городской район.</color>")
                     : months < 0 ? Loc.T("Рост остановлен.")
                     : Loc.F("Рост +{0:0.#}% в месяц · новый житель через ~{1} мес.", _planet.PopGrowthBaseSpeedPctPerMonth, months)));
            }
            else
            {
                s0.Value.text = "—";
                s0.Tip.SetText(!Surveyed ? Loc.T("Нет данных — нужна разведка") : _planet.IsColonizing ? Loc.T("Колонисты в пути")
                    : _planet.CanColonize ? Loc.T("Планета пригодна для колонии") : Loc.T("Непригодна для жизни — только добыча"));
            }

            float mi = Produced(DistrictType.Mining) + StationOutput(DistrictType.Mining);
            float en = Produced(DistrictType.Generator) + StationOutput(DistrictType.Generator);
            float al = Produced(DistrictType.Industrial);
            float sci = ScienceOut();
            SetStat(_stats[1], mi, Loc.T("<b>Титан</b> в месяц: районы и добывающий комплекс"));
            SetStat(_stats[2], en, Loc.T("<b>Гелий-3</b> в месяц: энергорайоны и добывающий комплекс"));
            SetStat(_stats[3], al, Loc.T("<b>Сплавы</b> в месяц: промышленные районы"));
            SetStat(_stats[4], sci, Loc.T("<b>Наука</b> в месяц от жителей (умные виды дают больше, роботы — ничего)"));

            // Стабильность
            var s5 = _stats[5];
            if (colony)
            {
                var tier = _planet.Tier;
                s5.Value.text = $"<color=#{PlanetStability.Hex(PlanetStability.TierColor(tier))}>{_planet.Stability:0}</color><size=12><color={m}> {PlanetStability.TierName(tier).ToLower()}</color></size>";
                s5.Tip.SetText(PlanetStability.Tooltip(_planet));
            }
            else { s5.Value.text = "—"; s5.Tip.SetText(Loc.T("Стабильность появится с колонией")); }

            // Занятость
            var s6 = _stats[6];
            if (colony)
            {
                int idle = _planet.IdlePops;
                s6.Value.text = idle > 0
                    ? $"{_planet.EmployedPops}<size=14><color={m}> / {_planet.WorkerSlots}</color></size> <size=12><color={LGBuild.Hex(CGold)}>+{idle}</color></size>"
                    : $"{_planet.EmployedPops}<size=14><color={m}> / {_planet.WorkerSlots}</color></size>";
                s6.Tip.SetText(Loc.F("<b>Работают {0} из {1} мест</b>\n", _planet.EmployedPops, _planet.WorkerSlots) +
                    (idle > 0 ? Loc.F("<color=#F2C46B>Безработных {0}: постройте районы — они сразу выйдут на смену.</color>", idle) : Loc.T("Безработных нет.")));
            }
            else { s6.Value.text = "—"; s6.Tip.SetText(Loc.T("Занятость")); }

            // Дуги у планеты: население и рост
            _popFill.fillAmount = colony ? PopArc * Mathf.Clamp01(pop / (float)Mathf.Max(1, housing)) : 0f;
            _popFill.color = colony && pop >= housing ? CGold : CCyan;
            _growFill.fillAmount = colony && pop < housing ? GrowArc * Mathf.Clamp01(_planet.PopGrowthProgress / 100f) : 0f;

            bool bankrupt = EconomyManager.Instance != null && EconomyManager.Instance.IsBankrupt && Owned;
            _bankruptText.gameObject.SetActive(bankrupt);
        }

        private void SetStat(StatCell c, float v, string tip)
        {
            bool any = v > 0.05f;
            c.Value.text = Plus(v);
            c.Value.color = any ? Color.white : CMuted;
            c.Tip.SetText(tip + "\n" + (any ? Loc.F("Сейчас: <b>+{0:0.#}</b>", v) : Loc.T("Сейчас ничего не производится")));
        }

        // ---------------- Приоритет и автостройка ----------------

        private void RefreshPriority()
        {
            bool show = Owned && _planet.Population > 0;
            _priorityRow.SetActive(show);
            if (!show) { _focusList.SetActive(false); return; }
            _focusText.text = ColonyFocusInfo.Name(_planet.Focus).ToUpper();
            for (int i = 0; i < _focusOptBg.Length; i++)
                _focusOptBg[i].color = ColonyFocusInfo.All[i] == _planet.Focus ? new Color(0.16f, 0.52f, 0.56f) : new Color(0.03f, 0.07f, 0.09f, 0.98f);
            bool auto = _planet.AutoBuild;
            _autoBg.color = auto ? new Color(0.14f, 0.46f, 0.30f) : new Color(0.07f, 0.13f, 0.17f);
            _autoIcon.sprite = LGIcons.Get(auto ? LGIcon.Check : LGIcon.Construction);
            _autoIcon.color = auto ? UIManager.DS.Green : CMuted;
            _autoText.text = auto ? Loc.T("АВТОСТРОЙКА ВКЛ.") : Loc.T("АВТОСТРОЙКА ВЫКЛ.");
            _autoText.color = auto ? Color.white : CMuted;
        }

        private void ToggleFocusList()
        {
            if (!Owned) return;
            _focusList.SetActive(!_focusList.activeSelf);
            _focusList.transform.SetAsLastSibling();
            SFXManager.Play(Sfx.UiClick, 0.5f);
        }

        private void PickFocus(ColonyFocus f)
        {
            _focusList.SetActive(false);
            SetFocus(f);
        }

        // ---------------- Ряды работ ----------------

        private static ColonyFocus FocusOf(DistrictType t) => t switch
        {
            DistrictType.Mining => ColonyFocus.Minerals,
            DistrictType.Generator => ColonyFocus.Energy,
            DistrictType.Industrial => ColonyFocus.Industry,
            _ => ColonyFocus.Balanced
        };

        private static string ResName(DistrictType t) => t switch
        {
            DistrictType.Mining => Loc.T("ТИТАН"),
            DistrictType.Generator => Loc.T("ГЕЛИЙ-3"),
            DistrictType.Industrial => Loc.T("СПЛАВЫ"),
            _ => Loc.T("ЖИЛЬЁ")
        };

        /// <summary>Цвета жителей по порядку работы: так же, как их распределяет колония (сначала район приоритета).</summary>
        private Dictionary<DistrictType, List<Color>> WorkerColors()
        {
            var colors = new List<Color>();
            foreach (var g in _planet.Pops)
            {
                var sp = SpeciesRegistry.Get(g.Species);
                for (int i = 0; i < g.Count; i++) colors.Add(sp != null ? sp.Color : CCyan);
            }
            var order = new List<DistrictType>();
            var first = ColonyFocusInfo.District(_planet.Focus);
            if (first.HasValue) order.Add(first.Value);
            foreach (var t in new[] { DistrictType.Generator, DistrictType.Mining, DistrictType.Industrial, DistrictType.Urban })
                if (!order.Contains(t)) order.Add(t);
            var result = new Dictionary<DistrictType, List<Color>>();
            int k = 0;
            foreach (var t in order)
            {
                var list = new List<Color>();
                int worked = _planet.WorkedCount(t);
                for (int i = 0; i < worked && k < colors.Count; i++) list.Add(colors[k++]);
                result[t] = list;
            }
            return result;
        }

        private void RefreshJobs()
        {
            bool colony = IsColony;
            _noColonyText.gameObject.SetActive(!colony);
            _noColonyText.text = !Surveyed ? Loc.T("Нет данных о планете — отправьте научный корабль.")
                               : _planet.IsColonizing ? Loc.T("Колонисты в пути. Когда колония будет основана, здесь появятся рабочие места.")
                               : _planet.CanColonize ? Loc.T("Колонии нет. Основайте её проектом «Колония» внизу.")
                               : Loc.T("Планета непригодна для жизни. Доход даст только добывающий комплекс.");
            foreach (var r in _jobs) r.Bg.gameObject.SetActive(colony);
            if (!colony) { _idleText.text = ""; return; }

            var colors = WorkerColors();
            var queue = Builds != null ? Builds.QueueFor(_planet) : new List<ConstructionJob>();
            int pendingAll = _planet.PendingDistricts;
            var eco = EconomyManager.Instance;
            foreach (var r in _jobs)
            {
                var t = r.Type;
                int count = _planet.GetDistrictCount(t);
                int worked = _planet.WorkedCount(t);
                int pending = Builds != null ? Builds.PendingDistricts(_planet, t) : 0;
                bool focused = Owned && _planet.Focus == r.Focus && t != DistrictType.Urban;
                Color dc = DistrictInfo.Color(t);

                // Значение и подпись
                if (t == DistrictType.Urban)
                {
                    r.Value.text = Loc.F("{0} <size=14>{1}</size>", _planet.HousingCapacity, ResName(t));
                    r.Sub.text = Loc.F("Городских районов {0} · жителей {1} из {2}", count, _planet.Population, _planet.HousingCapacity);
                }
                else
                {
                    r.Value.text = $"{Plus(Produced(t))} <size=14>{ResName(t)}</size>";
                    r.Sub.text = Loc.F("{0} · работают {1} из {2}", DistrictName(t), worked, count) + (focused ? Loc.T("  · приоритет") : "");
                }
                r.Value.color = Color.Lerp(dc, Color.white, 0.25f);

                // Полоса: стройка района этого вида, а у города — рост населения
                var job = FirstJob(JobKind.District, t);
                if (job != null)
                {
                    bool active = queue.Count > 0 && queue[0] == job;
                    LGBuild.SetBar(r.Bar, active ? job.Progress : 0f, CGold);
                    r.Sub.text += active ? Loc.F("  ·  <color=#F2C46B>строится, {0} дн.</color>", Mathf.CeilToInt(job.DaysLeft))
                                         : Loc.F("  ·  <color=#F2C46B>в очереди +{0}</color>", pending);
                }
                else if (t == DistrictType.Urban)
                {
                    bool full = _planet.Population >= _planet.HousingCapacity;
                    LGBuild.SetBar(r.Bar, full ? 1f : _planet.PopGrowthProgress / 100f, full ? CGold : UIManager.DS.Green);
                    int months = _planet.MonthsToNextPop;
                    r.Sub.text += full ? Loc.T("  ·  <color=#F2C46B>нет жилья — рост стоит</color>")
                                : months > 0 ? Loc.F("  ·  рост: +1 через {0} мес.", months) : "";
                }
                else LGBuild.SetBar(r.Bar, 0f);

                r.Rim.color = focused ? new Color(dc.r, dc.g, dc.b, 0.9f) : new Color(dc.r, dc.g, dc.b, 0.25f);
                r.Bg.color = focused ? new Color(dc.r * 0.10f, dc.g * 0.10f, dc.b * 0.10f, 0.92f) : new Color(0.02f, 0.045f, 0.065f, 0.82f);
                r.InfoTip.SetText(t == DistrictType.Urban
                    ? Loc.T("<b>Город</b>\nЖильё для жителей и по рабочему месту в каждом районе. Нажмите, чтобы работа распределялась равномерно.")
                    : Loc.F("<b>{0}</b>\n{1}\nКаждое занятое место даёт ≈ {2:0.#} в месяц.\n", DistrictName(t), DistrictEffect(t), PerWorker(t)) +
                      (focused ? Loc.T("<color=#9EF0C8>Это приоритет колонии: жители идут сюда первыми.</color>")
                               : Loc.T("Нажмите, чтобы сделать этот ресурс приоритетом: жители пойдут сюда первыми.")));

                RebuildSlots(r, count, worked, pending, colors.TryGetValue(t, out var cl) ? cl : new List<Color>());

                // «+»: ещё один район этого вида
                float mc = DistrictInfo.MineralsCost(t), ac = DistrictInfo.AlloysCost(t);
                string why = !Owned ? Loc.T("Система не ваша: сначала постройте здесь форпост.")
                           : _planet.BuiltDistricts + pendingAll >= _planet.MaxDistricts ? Loc.F("Все {0} мест под районы заняты (с учётом очереди).", _planet.MaxDistricts)
                           : queue.Count >= ConstructionManager.MaxPlanetQueue ? Loc.F("Очередь планеты заполнена: не больше {0} заказов.", ConstructionManager.MaxPlanetQueue)
                           : eco == null || eco.Minerals < mc || eco.Alloys < ac ? CostCheck.Full(0f, mc, ac, 0f) + "."
                           : null;
                r.Add.gameObject.SetActive(Owned);
                r.Add.interactable = why == null;
                r.AddBg.color = why == null ? new Color(dc.r * 0.35f, dc.g * 0.35f, dc.b * 0.35f, 1f) : new Color(0.06f, 0.09f, 0.12f);
                r.AddTip.SetText(DistrictTip(t, why));
            }

            int idle = _planet.IdlePops;
            _idleText.text = idle > 0
                ? Loc.F("<color=#F2C46B>Безработных {0}</color> — нажмите «+» в ряду, чтобы дать им работу.", idle)
                : _planet.BuiltDistricts >= _planet.MaxDistricts ? Loc.F("<color=#8AA2A8>Все {0} мест под районы застроены.</color>", _planet.MaxDistricts)
                : Loc.F("<color=#8AA2A8>Свободных мест под районы: {0} из {1}.</color>", _planet.MaxDistricts - _planet.BuiltDistricts - pendingAll, _planet.MaxDistricts);
        }

        /// <summary>
        /// Рабочие места ряда: подставка, фигурка жителя цвета его вида (тусклая — место свободно,
        /// золотая подставка — район строится, клик по ней — отменить), над местом — его выработка.
        /// </summary>
        private void RebuildSlots(JobRow r, int count, int worked, int pending, List<Color> colors)
        {
            var key = new System.Text.StringBuilder();
            key.Append(count).Append('|').Append(worked).Append('|').Append(pending).Append('|').Append(PerWorker(r.Type).ToString("0.0"));
            foreach (var c in colors) key.Append(ColorUtility.ToHtmlStringRGB(c));
            string k = key.ToString();
            if (k == r.SlotKey) return;
            r.SlotKey = k;
            LGBuild.Clear(r.Slots);

            Color dc = DistrictInfo.Color(r.Type);
            const float W = 44f, G = 6f;
            int total = count + pending;
            int shown = Mathf.Min(MaxSlots, total);
            float per = PerWorker(r.Type);
            for (int i = 0; i < shown; i++)
            {
                bool building = i >= count;
                bool busy = !building && i < worked;
                float x = i * (W + G);

                // Подставка-параллелограмм
                var ped = LGBuild.Panel(r.Slots, "Pedestal", building ? new Color(CGold.r, CGold.g, CGold.b, 0.55f)
                                                              : busy ? new Color(dc.r, dc.g, dc.b, 0.55f) : new Color(dc.r, dc.g, dc.b, 0.16f), raycast: building);
                ped.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 2), new Vector2(W, 9));
                ped.rectTransform.localRotation = Quaternion.Euler(0, 0, 0);
                LG.Chip(ped.gameObject, new Color(dc.r, dc.g, dc.b, busy ? 0.7f : 0.25f), 3f);

                if (building)
                {
                    var clock = LGIcons.Create(r.Slots, LGIcon.Clock, 18, CGold);
                    clock.rectTransform.At(new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(x + W * 0.5f, 16), new Vector2(18, 18));
                    var b = ped.gameObject.AddComponent<Button>();
                    b.transition = Selectable.Transition.None;
                    var t = r.Type;
                    b.onClick.AddListener(() => OnCancelDistrict(t));
                    TooltipHelper.Attach(ped.gameObject, Loc.T("<b>Район строится</b>\nНажмите, чтобы отменить (ресурсы вернутся)."));
                    continue;
                }

                Color figure = busy && i < colors.Count ? colors[i] : busy ? dc : new Color(1f, 1f, 1f, 0.13f);
                var ic = LGIcons.Create(r.Slots, r.Type == DistrictType.Urban && !busy ? LGIcon.Housing : LGIcon.Population, 30, figure);
                ic.rectTransform.At(new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(x + W * 0.5f, 10), new Vector2(30, 30));
                ic.raycastTarget = false;
                LG.Ignore(ic.gameObject);

                // Выработка места — над ним
                if (per > 0.05f)
                {
                    var num = LGBuild.Label(r.Slots, per.ToString("0.#"), 11, busy ? Color.Lerp(dc, Color.white, 0.4f) : new Color(1f, 1f, 1f, 0.25f), TextAnchor.LowerCenter, bold: true);
                    num.rectTransform.At(new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(x + W * 0.5f, 42), new Vector2(W, 16));
                    LG.Ignore(num.gameObject);
                }
            }
            if (total > shown)
            {
                var more = LGBuild.Label(r.Slots, $"+{total - shown}", 13, CMuted, TextAnchor.MiddleLeft, bold: true);
                more.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(shown * (W + G), 12), new Vector2(40, 24));
            }
            if (total == 0)
            {
                var none = LGBuild.Label(r.Slots, Loc.T("районов нет — «+», чтобы построить"), 11, CMuted, TextAnchor.MiddleLeft);
                none.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 12), new Vector2(300, 24));
            }
            LG.Skin(r.Slots);
        }

        // ---------------- Производство ----------------

        private void RefreshProduction()
        {
            var queue = Builds != null ? Builds.QueueFor(_planet) : new List<ConstructionJob>();
            bool owned = Owned;
            _prodRoot.SetActive(owned && Surveyed);
            if (!owned || !Surveyed) return;

            var job = queue.Count > 0 ? queue[0] : null;
            if (job != null)
            {
                Color jc = JobColor(job);
                _curIcon.sprite = LGIcons.Get(BuildMenuModal.JobIcon(job));
                _curIcon.color = jc;
                _curTileBg.color = new Color(jc.r * 0.16f, jc.g * 0.16f, jc.b * 0.16f, 0.95f);
                _curDays.text = Loc.F("{0} дн.", Mathf.CeilToInt(job.DaysLeft));
                _curName.text = BuildMenuModal.JobTitle(job);
                _curSub.text = Loc.F("готово через {0} дн. · {1:0}%", Mathf.CeilToInt(job.DaysLeft), job.Progress * 100f);
                LGBuild.SetBar(_curBar, job.Progress, jc);
                _curTip.SetText(Loc.F("<b>{0}</b>\nОсталось {1} дн. Нажмите, чтобы открыть меню строительства.", BuildMenuModal.JobTitle(job), Mathf.CeilToInt(job.DaysLeft)));

                string block = Builds.BuyBlocker(job);
                _buyBtn.gameObject.SetActive(true);
                _buyBtn.interactable = block == null;
                _buyBg.color = block == null ? new Color(0.36f, 0.28f, 0.06f) : new Color(0.08f, 0.10f, 0.12f);
                _buyText.text = Loc.F("КУПИТЬ  {0}", ConstructionManager.BuyCost(job));
                _buyText.color = block == null ? Color.white : CMuted;
                _buyTip.SetText(BuildMenuModal.BuyTip(job, block));
            }
            else
            {
                _curIcon.sprite = LGIcons.Get(LGIcon.Construction);
                _curIcon.color = new Color(CGold.r, CGold.g, CGold.b, 0.35f);
                _curTileBg.color = new Color(0.03f, 0.06f, 0.08f, 0.95f);
                _curDays.text = "";
                _curName.text = _planet.Population > 0 && _planet.AutoBuild ? Loc.T("Автостройка выберет сама") : Loc.T("Выберите производство");
                _curSub.text = _planet.Population > 0 && _planet.AutoBuild ? Loc.T("закажет нужный район в начале месяца")
                             : Loc.T("планета простаивает — нажмите «+»");
                LGBuild.SetBar(_curBar, 0f);
                _curTip.SetText(Loc.T("Очередь пуста. Нажмите, чтобы открыть меню строительства  (B)"));
                _buyBtn.gameObject.SetActive(false);
            }

            // Очередь: следующие заказы плитками (клик — отменить)
            var key = new System.Text.StringBuilder();
            for (int i = 1; i < queue.Count; i++) key.Append(queue[i].Id).Append(':').Append(Mathf.CeilToInt(Builds.DaysUntilDone(queue[i]))).Append(';');
            string k = key.ToString();
            if (k == _queueKey && (_queueTiles.childCount > 0 || queue.Count <= 1)) return;
            _queueKey = k;
            LGBuild.Clear(_queueTiles);
            const float S = 58f;
            for (int i = 1; i < queue.Count && i <= 4; i++)
            {
                var j = queue[i];
                Color jc = JobColor(j);
                var tile = LGBuild.Panel(_queueTiles, "Queued", new Color(jc.r * 0.12f, jc.g * 0.12f, jc.b * 0.12f, 0.95f), raycast: true);
                tile.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2((i - 1) * (S + 8f), 0), new Vector2(S, S));
                LG.Platter(tile.gameObject, 10f).SetRim(new Color(jc.r, jc.g, jc.b, 0.35f));
                var ic = LGIcons.Create(tile.transform, BuildMenuModal.JobIcon(j), 22, jc);
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(22, 22));
                var d = LGBuild.Label(tile.transform, Loc.F("{0} дн.", Mathf.CeilToInt(Builds.DaysUntilDone(j))), 9, CMuted, TextAnchor.LowerCenter, bold: true);
                d.rectTransform.Stretch(0, 4, 0, 0);
                var b = tile.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                var captured = j;
                b.onClick.AddListener(() => { if (Builds != null) { Builds.Cancel(captured); Refresh(); } });
                TooltipHelper.Attach(tile.gameObject, Loc.F("<b>{0}</b>\nВ очереди · готов через ~{1} дн.\n<color=#8AA2A8>Нажмите, чтобы отменить (ресурсы вернутся)</color>",
                    BuildMenuModal.JobTitle(j), Mathf.CeilToInt(Builds.DaysUntilDone(j))));
            }
            if (queue.Count > 5)
            {
                var more = LGBuild.Label(_queueTiles, $"+{queue.Count - 5}", 13, CMuted, TextAnchor.MiddleLeft, bold: true);
                more.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(4 * (S + 8f), 0), new Vector2(40, 24));
            }
            LG.Skin(_queueTiles);
        }

        private static Color JobColor(ConstructionJob j) => j.Kind == JobKind.District ? DistrictInfo.Color(j.District) : CGold;

        private void OnBuyCurrent()
        {
            if (_planet == null || Builds == null) return;
            var q = Builds.QueueFor(_planet);
            if (q.Count > 0 && Builds.TryBuy(q[0]))
            {
                SFXManager.Play(Sfx.UiConfirm);
                EconomyManager.Instance?.RecalculateAll();
                Refresh();
            }
        }

        private void OpenBuildMenu()
        {
            if (_planet != null && Owned) BuildMenuModal.Open(_planet);
        }

        // ---------------- Информация о планете ----------------

        private void RefreshInfo()
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
                Loc.F("<color={0}>Класс</color>   {1}\n", m, _planet.ClassDisplayName) +
                Loc.F("<color={0}>Гравитация</color>   {1:0.00} g\n", m, gravity) +
                Loc.F("<color={0}>Атмосфера</color>   {1}\n", m, atmo) +
                Loc.F("<color={0}>Температура</color>   {1:+#;-#;0} °C\n", m, temperature) +
                Loc.F("<color={0}>Наклон оси</color>   {1}°\n", m, tilt) +
                Loc.F("<color={0}>Орбита</color>   {1:0.0} а.е.", m, _planet.OrbitRadius);

            float st = StationOutput(DistrictType.Mining) + StationOutput(DistrictType.Generator);
            var b = Bonuses();
            float potential = _planet.MineralDeposit * FactionMult(DistrictType.Mining) * b.MineralsMult
                            + _planet.EnergyDeposit * FactionMult(DistrictType.Generator) * b.EnergyMult;
            string station = _planet.HasMiningStation ? Loc.F("<color={0}>Добывающий комплекс: +{1:0.#} в месяц</color>", LGBuild.Hex(UIManager.DS.Green), st)
                           : _planet.StationUnderConstruction ? Loc.F("<color={0}>Добывающий комплекс строится</color>", LGBuild.Hex(CGold))
                           : potential > 0f ? Loc.F("<color={0}>Комплекс дал бы +{1:0.#} в месяц</color>", m, potential)
                           : Loc.F("<color={0}>Промышленных залежей нет</color>", m);
            string starbase = _system != null && _system.HasStarbase && _system.OwnerId >= 0
                ? Loc.F("\n<color={0}>Звёздная база охраняет систему</color>", m) : "";
            _stationText.text = station + starbase;
        }

        private static void SetSeg(Seg s, int lit, string value, Color c)
        {
            s.Value.text = value;
            s.Value.color = lit > 0 ? Color.Lerp(c, Color.white, 0.35f) : CMuted;
            for (int i = 0; i < s.Cells.Length; i++) s.Cells[i].color = i < lit ? c : CSegOff;
        }

        // ---------------- Навигация ----------------

        private void RefreshNav()
        {
            CollectColonies();
            int idx = _colonies.IndexOf(_planet);
            bool many = _colonies.Count > 1 || (_colonies.Count == 1 && idx < 0);
            _prevBtn.interactable = many;
            _nextBtn.interactable = many;
            _navText.text = idx >= 0 ? Loc.F("КОЛОНИЯ  {0} / {1}", idx + 1, _colonies.Count) : Loc.F("КОЛОНИЙ  {0}", _colonies.Count);
        }

        // ================================================================ Построение (экран колонии)

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
            BuildStats(_rootRt);
            BuildJobs(_rootRt);
            BuildProduction(_rootRt);
            BuildProjects(_rootRt);
            BuildInfo(_rootRt);
            BuildSociety(_rootRt);
            BuildNav(_rootRt);
            BuildPriority(_rootRt);   // последним — выпадающий список поверх рядов

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        /// <summary>Строка показателей колонии под названием (как шапка колонии в Master of Orion).</summary>
        private void BuildStats(RectTransform rt)
        {
            var strip = LGBuild.Panel(rt, "Stats", CPanel);
            strip.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -StatsTop), new Vector2(LeftW, StatsH));
            LG.Glass(strip.gameObject, 14f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.28f));
            LG.Motion(strip.gameObject, LGAppear.Kind.SlideDown).distance = 20f;

            (LGIcon icon, Color col, string cap)[] defs =
            {
                (LGIcon.Population, UIManager.DS.Green, Loc.T("ЖИТЕЛИ")),
                (LGIcon.Minerals, CMinerals, Loc.T("ТИТАН")),
                (LGIcon.Energy, CEnergy, Loc.T("ГЕЛИЙ-3")),
                (LGIcon.Alloys, CAlloys, Loc.T("СПЛАВЫ")),
                (LGIcon.Research, CScience, Loc.T("НАУКА")),
                (LGIcon.Society, new Color(0.98f, 0.55f, 0.85f), Loc.T("СТАБИЛЬНОСТЬ")),
                (LGIcon.Industry, CCyan, Loc.T("ЗАНЯТОСТЬ")),
            };
            float[] widths = { 0.15f, 0.12f, 0.12f, 0.12f, 0.12f, 0.21f, 0.16f };
            _stats = new StatCell[defs.Length];
            float x = 0f;
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var cell = LGBuild.Panel(strip.transform, "Cell", new Color(1f, 1f, 1f, 0f), raycast: true);
                var cr = cell.rectTransform;
                cr.anchorMin = new Vector2(x, 0); cr.anchorMax = new Vector2(x + widths[i], 1);
                cr.offsetMin = new Vector2(i == 0 ? 14 : 8, 6); cr.offsetMax = new Vector2(-4, -6);
                x += widths[i];
                var cap = LGBuild.Label(cell.transform, d.cap, 10, Color.Lerp(d.col, CMuted, 0.35f), TextAnchor.UpperLeft, bold: true);
                cap.rectTransform.TopBand(2, 14);
                cap.Track(1.0f);
                var ic = LGIcons.Create(cell.transform, d.icon, 20, d.col);
                ic.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 8), new Vector2(20, 20));
                var val = LGBuild.Label(cell.transform, "", 21, Color.white, TextAnchor.LowerLeft, bold: true);
                val.rectTransform.Stretch(26, 4, 0, 20);
                TooltipHelper.Attach(cell.gameObject, d.cap);
                _stats[i] = new StatCell { Caption = cap, Value = val, Icon = ic, Tip = cell.GetComponent<TooltipTrigger>() };
                if (i > 0)
                {
                    var sep = LGBuild.Panel(strip.transform, "Sep", new Color(1f, 1f, 1f, 0.08f));
                    sep.rectTransform.anchorMin = new Vector2(x - widths[i], 0.2f); sep.rectTransform.anchorMax = new Vector2(x - widths[i], 0.8f);
                    sep.rectTransform.offsetMin = new Vector2(0, 0); sep.rectTransform.offsetMax = new Vector2(1, 0);
                    LG.Ignore(sep.gameObject);
                }
            }

            _bankruptText = LGBuild.Label(rt, Loc.T("БАНКРОТСТВО: ПРОИЗВОДСТВО УРЕЗАНО ВДВОЕ"), 11, UIManager.DS.Red, TextAnchor.UpperRight, bold: true);
            _bankruptText.rectTransform.At(new Vector2(0, 1), new Vector2(1, 1), new Vector2(Pad + LeftW, -StatsTop - StatsH - 4), new Vector2(400, 16));
            _bankruptText.gameObject.SetActive(false);
        }

        /// <summary>«Приоритет колонии» — выпадающий список и кнопка автостройки.</summary>
        private void BuildPriority(RectTransform rt)
        {
            var row = LGBuild.Rect(rt, "Priority");
            row.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -PriorityTop), new Vector2(LeftW, 44));
            _priorityRow = row.gameObject;

            var mark = LGBuild.Panel(row, "Mark", CRedAccent);
            mark.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -3), new Vector2(6, 6));
            LG.Ignore(mark.gameObject);
            var label = LGBuild.Label(row, Loc.T("ПРИОРИТЕТ КОЛОНИИ"), 12, Color.white, TextAnchor.UpperLeft, bold: true);
            label.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, 0), new Vector2(300, 16));
            label.Track(1.4f);

            var drop = LGBuild.Button(row, "FocusDrop", new Color(0.10f, 0.36f, 0.40f), CCyan, ToggleFocusList, null, null, 12, 8f);
            ((RectTransform)drop.transform).At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -20), new Vector2(330, 32));
            _focusText = LGBuild.Label(drop.transform, "", 12, Color.white, TextAnchor.MiddleCenter, bold: true);
            _focusText.rectTransform.Stretch(10, 0, 30, 0);
            var chev = LGIcons.Create(drop.transform, LGIcon.Back, 12, Color.white);
            chev.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(12, 12));
            chev.rectTransform.localRotation = Quaternion.Euler(0, 0, 90f);
            TooltipHelper.Attach(drop.gameObject, Loc.T("<b>Приоритет колонии</b>\nКуда жители идут работать в первую очередь и что заказывает автостройка. Также — клик по ряду работ."));

            var auto = LGBuild.Button(row, "Auto", new Color(0.07f, 0.13f, 0.17f), new Color(0.36f, 0.88f, 0.60f, 0.5f), ToggleAutoBuild, null, null, 11, 8f);
            ((RectTransform)auto.transform).At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(344, -20), new Vector2(220, 32));
            _autoBg = auto.GetComponent<Image>();
            _autoIcon = LGIcons.Create(auto.transform, LGIcon.Construction, 13, CMuted);
            _autoIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(13, 13));
            _autoText = LGBuild.Label(auto.transform, "", 11, Color.white, TextAnchor.MiddleLeft, bold: true);
            _autoText.rectTransform.Stretch(32, 0, 6, 0);
            TooltipHelper.Attach(auto.gameObject, Loc.T("<b>Автостройка</b>\nКогда очередь пуста, колония сама заказывает нужный район в начале месяца: жильё, когда тесно, рабочие места по приоритету."));

            // Выпадающий список приоритетов
            var list = LGBuild.Panel(rt, "FocusList", new Color(0.02f, 0.05f, 0.07f, 0.98f), raycast: true);
            list.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -PriorityTop - 54), new Vector2(330, ColonyFocusInfo.All.Length * 36 + 8));
            LG.Platter(list.gameObject, 10f).SetRim(new Color(CCyan.r, CCyan.g, CCyan.b, 0.45f));
            _focusList = list.gameObject;
            _focusOptBg = new Image[ColonyFocusInfo.All.Length];
            for (int i = 0; i < ColonyFocusInfo.All.Length; i++)
            {
                var f = ColonyFocusInfo.All[i];
                var opt = LGBuild.Button(list.transform, "Opt", new Color(0.03f, 0.07f, 0.09f, 0.98f), new Color(CCyan.r, CCyan.g, CCyan.b, 0.3f), () => PickFocus(f), null, null, 12, 6f);
                ((RectTransform)opt.transform).TopBand(4 + i * 36, 34, 4, 4);
                var t = LGBuild.Label(opt.transform, ColonyFocusInfo.Name(f), 13, Color.white, TextAnchor.MiddleCenter, bold: true);
                _focusOptBg[i] = opt.GetComponent<Image>();
                TooltipHelper.Attach(opt.gameObject, ColonyFocusInfo.Desc(f));
            }
            _focusList.SetActive(false);
        }

        private static readonly DistrictType[] RowOrder = { DistrictType.Mining, DistrictType.Generator, DistrictType.Industrial, DistrictType.Urban };

        /// <summary>Ряды работ — по ряду на вид района.</summary>
        private void BuildJobs(RectTransform rt)
        {
            var host = LGBuild.Rect(rt, "Jobs");
            host.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -JobsTop), new Vector2(LeftW, 4 * JobRowH + 30));
            LG.Motion(host.gameObject, LGAppear.Kind.SlideLeft).delay = 0.08f;

            _noColonyText = LGBuild.Label(host, "", 14, CMuted, TextAnchor.MiddleCenter, wrap: true);
            _noColonyText.rectTransform.TopBand(40, 120, 40, 40);

            _jobs = new JobRow[RowOrder.Length];
            for (int i = 0; i < RowOrder.Length; i++)
            {
                var t = RowOrder[i];
                Color dc = DistrictInfo.Color(t);
                var r = new JobRow { Type = t, Focus = FocusOf(t) };
                r.Bg = LGBuild.Panel(host, "Row_" + t, new Color(0.02f, 0.045f, 0.065f, 0.82f), raycast: false);
                r.Bg.rectTransform.TopBand(i * JobRowH, JobRowH - 8);
                LG.Platter(r.Bg.gameObject, 12f).SetRim(new Color(dc.r, dc.g, dc.b, 0.18f));

                r.Rim = LGBuild.Panel(r.Bg.transform, "Edge", dc);
                r.Rim.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(3, JobRowH - 26));
                LG.Ignore(r.Rim.gameObject);

                // Левая часть — значение, подпись, полоса; клик — приоритет
                var info = LGBuild.Panel(r.Bg.transform, "Info", new Color(1f, 1f, 1f, 0f), raycast: true);
                info.rectTransform.anchorMin = new Vector2(0, 0); info.rectTransform.anchorMax = new Vector2(0, 1);
                info.rectTransform.offsetMin = new Vector2(10, 6); info.rectTransform.offsetMax = new Vector2(300, -6);
                var ib = info.gameObject.AddComponent<Button>();
                ib.transition = Selectable.Transition.None;
                var focus = r.Focus;
                ib.onClick.AddListener(() => { if (Owned) SetFocus(focus); });
                TooltipHelper.Attach(info.gameObject, "");
                r.InfoTip = info.GetComponent<TooltipTrigger>();

                r.IconBg = LGBuild.Panel(info.transform, "IconBg", new Color(dc.r * 0.18f, dc.g * 0.18f, dc.b * 0.18f, 1f));
                r.IconBg.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(4, -4), new Vector2(34, 34));
                LG.Chip(r.IconBg.gameObject, new Color(dc.r, dc.g, dc.b, 0.6f), 17f);
                var ic = LGIcons.Create(r.IconBg.transform, BuildCatalog.DistrictIcon(t), 18, dc);
                ic.raycastTarget = false;

                r.Value = LGBuild.Label(info.transform, "", 22, Color.white, TextAnchor.UpperLeft, bold: true);
                r.Value.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(48, -2), new Vector2(240, 28));
                r.Sub = LGBuild.Label(info.transform, "", 10, CMuted, TextAnchor.UpperLeft);
                r.Sub.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(48, -32), new Vector2(250, 14));
                var barHost = LGBuild.Rect(info.transform, "Bar");
                barHost.anchorMin = new Vector2(0, 0); barHost.anchorMax = new Vector2(1, 0);
                barHost.offsetMin = new Vector2(48, 4); barHost.offsetMax = new Vector2(-6, 9);
                r.Bar = LGBuild.Bar(barHost, CGold, 0f, 4f);

                // Рабочие места
                r.Slots = LGBuild.Rect(r.Bg.transform, "Slots");
                r.Slots.anchorMin = new Vector2(0, 0); r.Slots.anchorMax = new Vector2(1, 1);
                r.Slots.offsetMin = new Vector2(312, 6); r.Slots.offsetMax = new Vector2(-58, -8);

                // «+» — ещё район
                r.Add = LGBuild.Button(r.Bg.transform, "Add", new Color(dc.r * 0.35f, dc.g * 0.35f, dc.b * 0.35f, 1f), dc, () => OnBuildDistrict(t), null, null, 16, 10f);
                ((RectTransform)r.Add.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(40, 40));
                var plus = LGBuild.Label(r.Add.transform, "+", 24, Color.white, TextAnchor.MiddleCenter, bold: true);
                r.AddBg = r.Add.GetComponent<Image>();
                TooltipHelper.Attach(r.Add.gameObject, "");
                r.AddTip = r.Add.GetComponent<TooltipTrigger>();
                _jobs[i] = r;
            }
            _idleText = LGBuild.Label(host, "", 11, CMuted, TextAnchor.UpperLeft);
            _idleText.rectTransform.TopBand(4 * JobRowH - 2, 18, 4, 4);
        }

        /// <summary>Производство: текущая стройка, «Купить», очередь и «+» (меню строительства).</summary>
        private void BuildProduction(RectTransform rt)
        {
            var host = LGBuild.Rect(rt, "Production");
            host.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -ProdTop), new Vector2(LeftW, ProdTileS + 30));
            _prodRoot = host.gameObject;
            LG.Motion(_prodRoot, LGAppear.Kind.SlideLeft).delay = 0.12f;

            var mark = LGBuild.Panel(host, "Mark", CRedAccent);
            mark.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -3), new Vector2(6, 6));
            LG.Ignore(mark.gameObject);
            var title = LGBuild.Label(host, Loc.T("ПРОИЗВОДСТВО"), 12, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, 0), new Vector2(300, 16));
            title.Track(1.4f);

            // Текущая стройка — квадратная плитка
            _curTileBg = LGBuild.Panel(host, "Current", new Color(0.03f, 0.06f, 0.08f, 0.95f), raycast: true);
            _curTileBg.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -24), new Vector2(ProdTileS, ProdTileS));
            LG.Platter(_curTileBg.gameObject, 12f).SetRim(new Color(CGold.r, CGold.g, CGold.b, 0.45f));
            var cb = _curTileBg.gameObject.AddComponent<Button>();
            cb.transition = Selectable.Transition.None;
            cb.onClick.AddListener(OpenBuildMenu);
            TooltipHelper.Attach(_curTileBg.gameObject, "");
            _curTip = _curTileBg.GetComponent<TooltipTrigger>();
            _curIcon = LGIcons.Create(_curTileBg.transform, LGIcon.Construction, 40, CGold);
            _curIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(40, 40));
            var clock = LGIcons.Create(_curTileBg.transform, LGIcon.Clock, 12, CMuted);
            clock.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(10, 9), new Vector2(12, 12));
            _curDays = LGBuild.Label(_curTileBg.transform, "", 12, Color.white, TextAnchor.LowerLeft, bold: true);
            _curDays.rectTransform.Stretch(26, 6, 4, 0);

            // Название, срок, полоса и «Купить»
            _curName = LGBuild.Label(host, "", 15, Color.white, TextAnchor.UpperLeft, bold: true);
            _curName.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(ProdTileS + 14, -26), new Vector2(270, 22));
            _curSub = LGBuild.Label(host, "", 11, CMuted, TextAnchor.UpperLeft);
            _curSub.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(ProdTileS + 14, -50), new Vector2(270, 16));
            var barHost = LGBuild.Rect(host, "Bar");
            barHost.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(ProdTileS + 14, -70), new Vector2(256, 6));
            _curBar = LGBuild.Bar(barHost, CGold, 0f, 4f);
            _buyBtn = LGBuild.Button(host, "Buy", new Color(0.36f, 0.28f, 0.06f), CGold, OnBuyCurrent, null, null, 11, 8f);
            ((RectTransform)_buyBtn.transform).At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(ProdTileS + 14, -82), new Vector2(170, 34));
            _buyBg = _buyBtn.GetComponent<Image>();
            var bi = LGIcons.Create(_buyBtn.transform, LGIcon.Energy, 14, CEnergy);
            bi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(14, 14));
            _buyText = LGBuild.Label(_buyBtn.transform, "", 12, Color.white, TextAnchor.MiddleLeft, bold: true);
            _buyText.rectTransform.Stretch(32, 0, 6, 0);
            TooltipHelper.Attach(_buyBtn.gameObject, "");
            _buyTip = _buyBtn.GetComponent<TooltipTrigger>();

            // Очередь — плитки следующих заказов
            _queueTiles = LGBuild.Rect(host, "QueueTiles");
            _queueTiles.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(ProdTileS + 300, -24 - (ProdTileS - 58f) * 0.5f), new Vector2(4 * 66f + 44f, 58f));

            // «+» — меню строительства
            _addBtn = LGBuild.Button(host, "BuildMenu", new Color(0.40f, 0.26f, 0.06f), CGold, OpenBuildMenu, null, null, 14, 12f);
            ((RectTransform)_addBtn.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -24), new Vector2(ProdTileS, ProdTileS));
            var add = LGIcons.Create(_addBtn.transform, LGIcon.Construction, 30, Color.white);
            add.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(30, 30));
            var addT = LGBuild.Label(_addBtn.transform, Loc.T("СТРОИТЬ"), 11, Color.white, TextAnchor.LowerCenter, bold: true);
            addT.rectTransform.Stretch(0, 10, 0, 0);
            TooltipHelper.Attach(_addBtn.gameObject, Loc.T("<b>Меню строительства</b>\nВсе постройки, проекты и корабли этой планеты с ценой и сроком  <color=#8AA2A8>(B)</color>"));
        }

        /// <summary>Информация о планете — справа сверху: шкалы, климат, сооружения.</summary>
        private void BuildInfo(RectTransform rt)
        {
            var card = Glass(rt, "Info", LGIcon.Planet, Loc.T("ИНФОРМАЦИЯ О ПЛАНЕТЕ"), CGold, LGAppear.Kind.SlideRight, 0.10f, out var body);
            var r = card.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(1, 1);
            r.anchoredPosition = new Vector2(-Pad, -StatsTop);
            r.sizeDelta = new Vector2(InfoW, InfoH);

            _dossier = new Seg[4];
            string[] labels = { Loc.T("Пригодность"), Loc.T("Районы · размер"), Loc.T("Титан"), Loc.T("Гелий-3") };
            for (int i = 0; i < _dossier.Length; i++) _dossier[i] = MakeSeg(body, labels[i], 4 + i * 34, 220f);

            var vline = LGBuild.Panel(body, "Split", new Color(0.45f, 0.95f, 0.90f, 0.16f));
            vline.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(238, -4), new Vector2(1, 136));
            LG.Ignore(vline.gameObject);

            _climate = LGBuild.Label(body, "", 11, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            _climate.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(252, -2), new Vector2(178, 150));
            _climate.lineSpacing = 1.2f;

            var structs = LGBuild.Label(body, Loc.T("СООРУЖЕНИЯ"), 10, CGold, TextAnchor.LowerLeft, bold: true);
            structs.rectTransform.anchorMin = new Vector2(0, 0); structs.rectTransform.anchorMax = new Vector2(1, 0);
            structs.rectTransform.offsetMin = new Vector2(0, 44); structs.rectTransform.offsetMax = new Vector2(0, 60);
            structs.Track(1.2f);
            _stationText = LGBuild.Label(body, "", 11, CMuted, TextAnchor.UpperLeft, wrap: true);
            _stationText.rectTransform.anchorMin = new Vector2(0, 0); _stationText.rectTransform.anchorMax = new Vector2(1, 0);
            _stationText.rectTransform.offsetMin = new Vector2(0, 2); _stationText.rectTransform.offsetMax = new Vector2(0, 42);
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

        /// <summary>Справа внизу: предыдущая / следующая колония и «Готово».</summary>
        private void BuildNav(RectTransform rt)
        {
            var host = LGBuild.Rect(rt, "Nav");
            host.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-Pad, 24), new Vector2(720, 74));
            LG.Motion(host.gameObject, LGAppear.Kind.SlideUp).delay = 0.16f;

            _navText = LGBuild.Label(host, "", 11, CMuted, TextAnchor.UpperRight, bold: true);
            _navText.rectTransform.TopBand(0, 16);
            _navText.Track(1.2f);

            _prevBtn = LGBuild.Button(host, "Prev", new Color(0.08f, 0.24f, 0.28f), CCyan, () => StepColony(-1), null, null, 12, 8f);
            ((RectTransform)_prevBtn.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(262, 46));
            var pi = LGIcons.Create(_prevBtn.transform, LGIcon.Back, 14, Color.white);
            pi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(14, 14));
            var pt = LGBuild.Label(_prevBtn.transform, Loc.T("ПРЕДЫДУЩАЯ КОЛОНИЯ"), 12, Color.white, TextAnchor.MiddleCenter, bold: true);
            pt.rectTransform.Stretch(26, 0, 6, 0);
            TooltipHelper.Attach(_prevBtn.gameObject, Loc.T("<b>Предыдущая колония</b>\nShift+Tab"));

            _nextBtn = LGBuild.Button(host, "Next", new Color(0.08f, 0.24f, 0.28f), CCyan, () => StepColony(1), null, null, 12, 8f);
            ((RectTransform)_nextBtn.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(272, 0), new Vector2(262, 46));
            var nt = LGBuild.Label(_nextBtn.transform, Loc.T("СЛЕДУЮЩАЯ КОЛОНИЯ"), 12, Color.white, TextAnchor.MiddleCenter, bold: true);
            nt.rectTransform.Stretch(6, 0, 26, 0);
            var ni = LGIcons.Create(_nextBtn.transform, LGIcon.Back, 14, Color.white);
            ni.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(14, 14));
            ni.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            TooltipHelper.Attach(_nextBtn.gameObject, Loc.T("<b>Следующая колония</b>\nTab"));

            var done = LGBuild.Button(host, "Done", new Color(0.12f, 0.42f, 0.30f), UIManager.DS.Green, Close, LGIcon.Check, Loc.T("ГОТОВО"), 12, 8f);
            ((RectTransform)done.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(170, 46));
            TooltipHelper.Attach(done.gameObject, Loc.T("Вернуться на карту  <color=#8AA2A8>(Esc)</color>"));
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

        /// <summary>Фон: подсветка цвета мира за планетой, виньетка по краям, линия под шапкой.</summary>
        private void BuildBackdrop(RectTransform rt)
        {
            _worldTint = Deco(rt, "WorldTint", HaloSprite(), Color.clear);
            var t = _worldTint.rectTransform;
            t.anchorMin = new Vector2(0.30f, -0.30f);
            t.anchorMax = new Vector2(1.20f, 1.30f);
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
            _stage.anchorMin = _stage.anchorMax = new Vector2(0.735f, 0.42f);   // справа, как в Master of Orion
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
        }

        /// <summary>Общество колонии: шкала стабильности, состав по видам и кампания пропаганды.</summary>
        private void BuildSociety(RectTransform rt)
        {
            var card = Glass(rt, "Society", LGIcon.Population, Loc.T("ОБЩЕСТВО"), new Color(0.98f, 0.55f, 0.85f), LGAppear.Kind.SlideRight, 0.14f, out var body);
            var r = card.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(1, 1);
            r.anchoredPosition = new Vector2(-Pad, -StatsTop - InfoH - 12f);
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

        /// <summary>Проекты планеты — внизу слева, пять плиток в ряд.</summary>
        private void BuildProjects(RectTransform rt)
        {
            var card = Glass(rt, "Projects", LGIcon.Planet, Loc.T("ПРОЕКТЫ ПЛАНЕТЫ"), CCyan, LGAppear.Kind.SlideUp, 0.20f, out var body);
            var r = card.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0, 0);
            r.pivot = new Vector2(0, 0);
            r.anchoredPosition = new Vector2(Pad, 24);
            r.sizeDelta = new Vector2(LeftW, 156);

            _mine = BuildAction(body, 0, Loc.T("ДОБЫВАЮЩИЙ КОМПЛЕКС"), new Color(0.10f, 0.36f, 0.26f), UIManager.DS.Green, OnMine);
            _colony = BuildAction(body, 1, Loc.T("КОЛОНИЯ"), new Color(0.10f, 0.32f, 0.40f), CCyan, OnColony);
            _terra = BuildAction(body, 2, Loc.T("ТЕРРАФОРМИНГ"), new Color(0.24f, 0.20f, 0.42f), new Color(0.72f, 0.60f, 1f), OnTerra);
            _robots = BuildAction(body, 3, Loc.T("СБОРКА РОБОТОВ"), new Color(0.16f, 0.24f, 0.32f), new Color(0.62f, 0.78f, 0.92f), OnRobots);
            _gene = BuildAction(body, 4, Loc.T("ГЕННАЯ АДАПТАЦИЯ"), new Color(0.30f, 0.14f, 0.30f), new Color(0.98f, 0.55f, 0.85f), OnGene);
        }

        /// <summary>Проект планеты: значок, название и строка состояния (цена, срок или итог).</summary>
        private static ActionButton BuildAction(RectTransform parent, int index, string title, Color tint, Color accent, System.Action onClick)
        {
            var b = LGBuild.Button(parent, "Project", tint, new Color(accent.r, accent.g, accent.b, 0.55f), onClick, null, null, 11, 12f);
            var r = (RectTransform)b.transform;
            r.anchorMin = new Vector2(index / (float)ProjectCount, 0);
            r.anchorMax = new Vector2((index + 1) / (float)ProjectCount, 1);
            r.offsetMin = new Vector2(index == 0 ? 0 : 4, 0);
            r.offsetMax = new Vector2(index == ProjectCount - 1 ? 0 : -4, 0);

            var badge = LGBuild.Panel(b.transform, "Badge", new Color(0.05f, 0.1f, 0.12f));
            badge.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -8), new Vector2(30, 30));
            LG.Platter(badge.gameObject, 15f).SetRim(new Color(accent.r, accent.g, accent.b, 0.6f));
            var ic = LGIcons.Create(badge.transform, LGIcon.Check, 15, Color.white);

            var t = LGBuild.Label(b.transform, title, 11, accent, TextAnchor.MiddleLeft, bold: true, wrap: true);
            t.rectTransform.anchorMin = new Vector2(0, 1);
            t.rectTransform.anchorMax = new Vector2(1, 1);
            t.rectTransform.offsetMin = new Vector2(44, -40);
            t.rectTransform.offsetMax = new Vector2(-6, -6);
            var st = LGBuild.Label(b.transform, "", 10, Color.white, TextAnchor.UpperLeft, wrap: true);
            st.rectTransform.anchorMin = new Vector2(0, 0);
            st.rectTransform.anchorMax = new Vector2(1, 1);
            st.rectTransform.offsetMin = new Vector2(8, 4);
            st.rectTransform.offsetMax = new Vector2(-6, -44);
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
