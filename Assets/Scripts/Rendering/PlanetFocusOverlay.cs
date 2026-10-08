using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Полноэкранный обзор планеты — главный экран управления колонией.
    ///   • вверху — кнопка «Закрыть», название и класс; под ними владелец и губернатор;
    ///   • слева — крупная вращающаяся планета, под ней поверхность (пригодность, размер, климат)
    ///     и природные залежи;
    ///   • справа — всё управление одной колонкой: население и производство, ниже районы списком
    ///     (что даёт, сколько работает, стоимость, постройка в один клик) и действия —
    ///     добывающий комплекс, колонизация и терраформинг.
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
        private GameObject _govChip;
        private LeaderThumb _govFace;
        private Image _govIcon;
        private Text _govText;

        // Левая колонка
        private Text _surfaceText, _stationText;
        private ResLine[] _deposits;          // титан, гелий-3
        private RectTransform _habBar, _sizeBar;
        private Text _habValue, _sizeValue;

        // Правая колонка
        private Text _popBig, _popText, _growthText, _productionText;
        private ResLine[] _production;        // титан, гелий-3, сплавы, наука
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

        private const float PlanetSize = 520f;
        private static readonly Color CGold = UIManager.DS.Gold;
        private static readonly Color CMuted = UIManager.DS.TextMuted;
        private static readonly Color CMinerals = new Color(0.20f, 0.90f, 0.80f);
        private static readonly Color CEnergy = new Color(0.95f, 0.78f, 0.28f);
        private static readonly Color CAlloys = new Color(0.95f, 0.62f, 0.36f);

        private class DistrictCard
        {
            public DistrictType Type;
            public Text Count, Worked, Reason;
            public ResLine MinCost, AlloyCost;
            public Button Build, Cancel;
            public Image Bg;
            public RectTransform Progress;
            /// <summary>Клетки районов: заполненная — на районе работают, контур — простаивает, золото — строится.</summary>
            public RectTransform Pips;
            public int PipKey = -1;
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

            public void Hide() => Go.SetActive(false);
        }

        private static ResLine MakeResLine(RectTransform parent, LGIcon icon, Color col, float iconSize, int fontSize, bool bold = false)
        {
            var rt = LGBuild.Rect(parent, "Res_" + icon);
            var ic = LGIcons.Create(rt, icon, iconSize, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));
            ic.raycastTarget = false;
            var t = LGBuild.Label(rt, "", fontSize, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, bold);
            t.rectTransform.Stretch(iconSize + 7, 0, 0, 0);
            t.supportRichText = true;
            return new ResLine { Go = rt.gameObject, Icon = ic, Text = t };
        }

        /// <summary>Строки ресурсов столбиком в карточке: с отступа top, по step пикселей.</summary>
        private static ResLine[] ResColumn(RectTransform card, float top, float step, int fontSize, params (LGIcon icon, Color col)[] items)
        {
            var lines = new ResLine[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                lines[i] = MakeResLine(card, items[i].icon, items[i].col, fontSize + 5, fontSize);
                ((RectTransform)lines[i].Go.transform).TopBand(top + i * step, step - 2, 18, 18);
            }
            return lines;
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
            _title.text = PlanetNames.Title(_planet.Name);
            var gov = LeaderManager.Instance?.GovernorOf(_planet);
            string govText = gov != null ? $"   ·   <color=#FFCC52>губернатор {gov.Name}, ур. {gov.Level}</color>" : "";
            _subtitle.text = $"{_planet.ClassDisplayName}   ·   система {(_system != null ? _system.Name : "?")}{govText}";
            _govChip.SetActive(gov != null);
            if (gov != null)
            {
                bool face = _govFace.Set(gov, LeaderThumb.ClassColor(gov.Class));
                _govIcon.enabled = !face;
                _govText.text = $"<color=#8AA2A8>Губернатор</color>  {gov.Name} · ур. {gov.Level}";
            }

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
                foreach (var l in _deposits) l.Hide();
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
            _deposits[0].Set($"<color={LGBuild.Hex(CMinerals)}>Титан</color>   <b>{_planet.MineralDeposit}</b>   <color={m}>→ {_planet.MineralDeposit * fM * b.MineralsMult:0.#}/мес со станции</color>");
            _deposits[1].Set($"<color={LGBuild.Hex(CEnergy)}>Гелий-3</color>   <b>{_planet.EnergyDeposit}</b>   <color={m}>→ {_planet.EnergyDeposit * fE:0.#}/мес со станции</color>");
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
                if (_planet.HasMiningStation)
                {
                    StationProduction();
                    _productionText.text = "";
                }
                else
                {
                    foreach (var l in _production) l.Hide();
                    _productionText.text = $"<color={m}>Производства нет</color>";
                }
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
            _production[0].Set(ProdLine("Титан", CMinerals, minerals));
            _production[1].Set(ProdLine("Гелий-3", CEnergy, energy));
            _production[2].Set(ProdLine("Сплавы", CAlloys, alloys));
            _production[3].Set(ProdLine("Наука", UIManager.DS.Green, science));
            _productionText.text = bankrupt ? $"<color={LGBuild.Hex(UIManager.DS.Red)}>Банкротство: производство урезано вдвое</color>" : "";
        }

        private void StationProduction()
        {
            var b = Bonuses();
            _production[0].Set(ProdLine("Титан", CMinerals, _planet.MineralDeposit * FactionMult(DistrictType.Mining) * b.MineralsMult));
            _production[1].Set(ProdLine("Гелий-3", CEnergy, _planet.EnergyDeposit * FactionMult(DistrictType.Generator)));
            _production[2].Hide();
            _production[3].Hide();
        }

        private static string ProdLine(string name, Color c, float v)
            => $"<color={LGBuild.Hex(c)}>{name}</color>   " +
               (v > 0.01f ? $"<b>+{v:0.#}</b>" : $"<color={LGBuild.Hex(CMuted)}>0</color>");

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
                RebuildPips(c, count, c.Type == DistrictType.Urban ? count : worked, pending);

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

                float mc = DistrictInfo.MineralsCost(c.Type), ac = DistrictInfo.AlloysCost(c.Type);
                bool canPay = eco != null && eco.Minerals >= mc && eco.Alloys >= ac;
                c.MinCost.Set($"{mc:0}", eco != null && eco.Minerals >= mc ? CMinerals : UIManager.DS.Red);
                c.AlloyCost.Set($"{ac:0}", eco != null && eco.Alloys >= ac ? CAlloys : UIManager.DS.Red);

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

        private void RebuildPips(DistrictCard c, int count, int worked, int pending)
        {
            int key = count * 10000 + worked * 100 + pending;
            if (c.PipKey == key) return;
            c.PipKey = key;
            LGBuild.Clear(c.Pips);
            Color dc = DistrictInfo.Color(c.Type);
            int total = Mathf.Min(12, count + pending);
            const float S = 14f, G = 4f;
            for (int i = 0; i < total; i++)
            {
                bool building = i >= count;
                bool busy = !building && i < worked;
                var pip = LGBuild.Panel(c.Pips, "Pip",
                    building ? new Color(CGold.r, CGold.g, CGold.b, 0.30f)
                    : busy ? new Color(dc.r, dc.g, dc.b, 0.95f) : new Color(dc.r * 0.25f, dc.g * 0.25f, dc.b * 0.25f, 0.9f));
                var r = pip.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(1, 0.5f);
                r.pivot = new Vector2(1, 0.5f);
                r.sizeDelta = new Vector2(S, S);
                r.anchoredPosition = new Vector2(-(total - 1 - i) * (S + G), 0);
                LG.Platter(pip.gameObject, 3f).SetRim(building ? new Color(CGold.r, CGold.g, CGold.b, 0.9f)
                                                     : new Color(dc.r, dc.g, dc.b, busy ? 0.9f : 0.55f));
                LG.Ignore(pip.gameObject);
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
            ((RectTransform)close.transform).At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -30), new Vector2(170, 40));
            LG.Motion(close.gameObject, LGAppear.Kind.Fade).delay = 0.12f;

            // Название и класс — рядом с кнопкой, слева направо читается «где я»
            _title = LGBuild.Label(rt, "", 28, UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true);
            _title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(232, -24), new Vector2(1200, 36));
            _subtitle = LGBuild.Label(rt, "", 13, CMuted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(234, -62), new Vector2(1200, 20));

            var sep = LGBuild.Panel(rt, "Sep", new Color(0.18f, 0.65f, 0.60f, 0.35f));
            sep.rectTransform.anchorMin = new Vector2(0, 1);
            sep.rectTransform.anchorMax = new Vector2(1, 1);
            sep.rectTransform.pivot = new Vector2(0.5f, 1);
            sep.rectTransform.offsetMin = new Vector2(40, -97);
            sep.rectTransform.offsetMax = new Vector2(-40, -96);
            LG.Line(sep.gameObject, hairline: true);

            // Владелец и губернатор — под шапкой над планетой (справа сверху всплывают уведомления)
            var chip = LGBuild.Panel(rt, "Owner", UIManager.DS.BgSlot);
            chip.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -112), new Vector2(300, 34));
            LG.Chip(chip.gameObject, new Color(1f, 1f, 1f, 0.2f));
            _ownerIcon = LGIcons.Create(chip.transform, LGIcon.Starbase, 18, FleetIndicator.OwnColor);
            _ownerIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(18, 18));
            _ownerText = LGBuild.Label(chip.transform, "", 12, Color.white, TextAnchor.MiddleLeft, bold: true);
            _ownerText.rectTransform.Stretch(42, 0, 12, 0);

            var gov = LGBuild.Panel(rt, "Governor", UIManager.DS.BgSlot);
            gov.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, -112), new Vector2(250, 34));
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

        // ---------------------------------------------------------------- Раскладка
        // Слева (40% ширины) — планета и её природа: поверхность и залежи под ней.
        // Справа — всё управление одной колонкой: население и производство, районы списком, действия.

        private const float LeftShare = 0.4f;
        private const float BodyTop = 160f, Pad = 40f, Gap = 14f, NatureHeight = 250f;

        private void BuildPlanet(RectTransform rt)
        {
            var zone = LGBuild.Rect(rt, "PlanetZone");
            zone.anchorMin = new Vector2(0, 0);
            zone.anchorMax = new Vector2(LeftShare, 1);
            zone.offsetMin = new Vector2(Pad, 24 + NatureHeight + Gap + 26);
            zone.offsetMax = new Vector2(-Gap * 0.5f, -BodyTop);

            // Квадрат планеты вписан в зону — на любом экране планета круглая и целиком видна
            var box = LGBuild.Rect(zone, "PlanetBox");
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(PlanetSize, PlanetSize);
            var fit = box.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1f;

            var halo = LGBuild.Panel(box, "Halo", new Color(0.30f, 0.90f, 0.85f, 0.10f));
            halo.sprite = MakeHaloSprite(256);
            halo.rectTransform.Stretch(-60, -60, -60, -60);
            LG.Ignore(halo.gameObject);

            var maskRt = LGBuild.Rect(box, "PlanetMask");
            maskRt.Stretch(14, 14, 14, 14);
            var maskImg = maskRt.gameObject.AddComponent<Image>();
            maskImg.sprite = MakeCircleSprite(512);
            maskImg.raycastTarget = false;
            maskRt.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var host = LGBuild.Rect(maskRt, "PlanetImage");
            host.Stretch();
            _planetImage = host.gameObject.AddComponent<RawImage>();
            _planetImage.raycastTarget = true;
            host.gameObject.AddComponent<HoloDragCatcher>();

            // Тонкое круглое кольцо-орбита (стеклянная рамка не умеет круг — у неё радиус ограничен)
            var ring = LGBuild.Panel(box, "PlanetRing", new Color(0.45f, 0.95f, 0.90f, 0.35f));
            ring.sprite = MakeRingSprite(512, 2.2f);
            ring.rectTransform.Stretch();
            LG.Ignore(ring.gameObject);

            var hint = LGBuild.Label(rt, "Зажмите ЛКМ на планете и ведите мышью — вращение", 11, CMuted, TextAnchor.MiddleCenter);
            hint.rectTransform.anchorMin = new Vector2(0, 0);
            hint.rectTransform.anchorMax = new Vector2(LeftShare, 0);
            hint.rectTransform.pivot = new Vector2(0.5f, 0);
            hint.rectTransform.offsetMin = new Vector2(Pad, 24 + NatureHeight + Gap);
            hint.rectTransform.offsetMax = new Vector2(-Gap * 0.5f, 24 + NatureHeight + Gap + 20);
            hint.fontStyle = FontStyle.Italic;
        }

        /// <summary>Карточка-стекло с заголовком; размещает её вызывающий.</summary>
        private RectTransform Card(RectTransform parent, string name, LGIcon icon, string title, Color accent, LGAppear.Kind appear, float delay)
        {
            var card = LGBuild.Panel(parent, name, UIManager.DS.BgDeep);
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

        /// <summary>Разместить в прямоугольнике: доли ширины родителя x0..x1, отступы снизу и сверху.</summary>
        private static void Place(RectTransform r, float x0, float x1, float padL, float padR, float bottom, float top, bool fromTop, float height)
        {
            if (fromTop)
            {
                r.anchorMin = new Vector2(x0, 1);
                r.anchorMax = new Vector2(x1, 1);
                r.offsetMin = new Vector2(padL, -top - height);
                r.offsetMax = new Vector2(-padR, -top);
            }
            else
            {
                r.anchorMin = new Vector2(x0, 0);
                r.anchorMax = new Vector2(x1, 1);
                r.offsetMin = new Vector2(padL, bottom);
                r.offsetMax = new Vector2(-padR, -top);
            }
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

        /// <summary>Под планетой: поверхность и залежи бок о бок.</summary>
        private void BuildLeft(RectTransform rt)
        {
            var surface = Card(rt, "Surface", LGIcon.Planet, "ПОВЕРХНОСТЬ", CGold, LGAppear.Kind.SlideLeft, 0.06f);
            PlaceBottom(surface, 0f, LeftShare * 0.5f, Pad, Gap * 0.5f);
            _habBar = BarRow(surface, 44, "Пригодность для жизни", UIManager.DS.Green, out _habValue);
            _sizeBar = BarRow(surface, 82, "Районы (размер планеты)", UIManager.DS.NeonCyan, out _sizeValue);
            _surfaceText = Body(surface, 124, 12);

            var deposits = Card(rt, "Deposits", LGIcon.Minerals, "ПРИРОДНЫЕ ЗАЛЕЖИ", CMinerals, LGAppear.Kind.SlideLeft, 0.12f);
            PlaceBottom(deposits, LeftShare * 0.5f, LeftShare, Gap * 0.5f, Gap * 0.5f);
            _deposits = ResColumn(deposits, 48, 36, 13, (LGIcon.Minerals, CMinerals), (LGIcon.Energy, CEnergy));
            _stationText = LGBuild.Label(deposits, "", 12, CMuted, TextAnchor.UpperLeft, wrap: true);
            _stationText.rectTransform.Stretch(18, 16, 18, 130);
        }

        private static void PlaceBottom(RectTransform r, float x0, float x1, float padL, float padR)
        {
            r.anchorMin = new Vector2(x0, 0);
            r.anchorMax = new Vector2(x1, 0);
            r.pivot = new Vector2(0.5f, 0);
            r.offsetMin = new Vector2(padL, 24);
            r.offsetMax = new Vector2(-padR, 24 + NatureHeight);
        }

        /// <summary>Справа сверху: население и производство бок о бок.</summary>
        private void BuildRight(RectTransform rt)
        {
            var pop = Card(rt, "Population", LGIcon.Population, "НАСЕЛЕНИЕ", UIManager.DS.NeonCyan, LGAppear.Kind.SlideRight, 0.06f);
            Place(pop, LeftShare, LeftShare + (1f - LeftShare) * 0.55f, Gap * 0.5f, Gap * 0.5f, 0, 112, true, 250);
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

            var prod = Card(rt, "Production", LGIcon.Industry, "ПРОИЗВОДСТВО В МЕСЯЦ", CGold, LGAppear.Kind.SlideRight, 0.12f);
            Place(prod, LeftShare + (1f - LeftShare) * 0.55f, 1f, Gap * 0.5f, Pad, 0, 112, true, 250);
            _production = ResColumn(prod, 46, 34, 14,
                (LGIcon.Minerals, CMinerals), (LGIcon.Energy, CEnergy), (LGIcon.Alloys, CAlloys), (LGIcon.Research, UIManager.DS.Green));
            // Сообщения: «производства нет», банкротство
            _productionText = LGBuild.Label(prod, "", 11, UIManager.DS.TextPrimary, TextAnchor.LowerLeft, wrap: true);
            _productionText.rectTransform.Stretch(18, 12, 18, 0);
        }

        /// <summary>Справа под населением: районы списком (строка = район) и действия с планетой.</summary>
        private void BuildBottom(RectTransform rt)
        {
            var panel = LGBuild.Panel(rt, "Districts", UIManager.DS.BgDeep);
            Place(panel.rectTransform, LeftShare, 1f, Gap * 0.5f, Pad, 24, 112 + 250 + Gap, false, 0);
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
            _districtSlots = LGBuild.Rect(p, "Slots");
            _districtSlots.TopBand(44, 8, 20, 20);

            var rows = LGBuild.Rect(p, "Rows");
            rows.Stretch(16, 70, 16, 62);
            DistrictType[] order = { DistrictType.Urban, DistrictType.Mining, DistrictType.Generator, DistrictType.Industrial };
            for (int i = 0; i < order.Length; i++)
            {
                var rowRt = LGBuild.Rect(rows, "Row");
                float y0 = 1f - (i + 1) / 4f, y1 = 1f - i / 4f;
                rowRt.anchorMin = new Vector2(0, y0);
                rowRt.anchorMax = new Vector2(1, y1);
                rowRt.offsetMin = new Vector2(0, i == 3 ? 0 : 4);
                rowRt.offsetMax = new Vector2(0, i == 0 ? 0 : -4);
                _cards.Add(BuildDistrictCard(rowRt, order[i]));
            }

            var actions = LGBuild.Rect(p, "Actions");
            actions.anchorMin = new Vector2(0, 0);
            actions.anchorMax = new Vector2(1, 0);
            actions.pivot = new Vector2(0.5f, 0);
            actions.offsetMin = new Vector2(16, 14);
            actions.offsetMax = new Vector2(-16, 58);
            _mine = BuildAction(actions, 0f, 1f / 3f, UIManager.DS.BtnSuccess, OnMine);
            _colony = BuildAction(actions, 1f / 3f, 2f / 3f, UIManager.DS.BtnPrimary, OnColony);
            _terra = BuildAction(actions, 2f / 3f, 1f, new Color(0.22f, 0.20f, 0.36f), OnTerra);
        }

        /// <summary>
        /// Строка района: значок, название и эффект слева, количество и занятость в середине,
        /// стоимость и кнопка постройки справа, полоса стройки по низу строки.
        /// </summary>
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
            var mid = new Vector2(0, 0.5f);
            var badge = LGBuild.Panel(rt, "Badge", new Color(dc.r * 0.3f, dc.g * 0.3f, dc.b * 0.3f));
            badge.rectTransform.At(mid, mid, new Vector2(14, 0), new Vector2(46, 46));
            LG.Platter(badge.gameObject, 23f).FillMultiplier = 2f;
            LGIcons.Create(badge.transform, icon, 24, dc);

            // Левая часть: название, эффект, занятость
            var info = LGBuild.Rect(rt, "Info");
            info.anchorMin = new Vector2(0, 0);
            info.anchorMax = new Vector2(1, 1);
            info.offsetMin = new Vector2(74, 10);
            info.offsetMax = new Vector2(-650, -8);
            var name = LGBuild.Label(info, DistrictName(type), 13, Color.white, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.TopBand(0, 18);
            var effect = LGBuild.Label(info, DistrictEffect(type), 11, CMuted, TextAnchor.UpperLeft, wrap: true);
            effect.rectTransform.TopBand(20, 30);
            var worked = LGBuild.Label(info, "", 11, CMuted, TextAnchor.LowerLeft);
            worked.rectTransform.Stretch(0, 6, 0, 0);

            // Клетки районов — заполняют середину строки наглядной занятостью
            var pips = LGBuild.Rect(rt, "Pips");
            pips.anchorMin = pips.anchorMax = new Vector2(1, 0.5f);
            pips.pivot = new Vector2(1, 0.5f);
            pips.anchoredPosition = new Vector2(-424, 0);
            pips.sizeDelta = new Vector2(220, 16);

            // Количество — крупно, перед стоимостью
            var count = LGBuild.Label(rt, "", 22, dc, TextAnchor.MiddleRight, bold: true);
            count.rectTransform.anchorMin = new Vector2(1, 0);
            count.rectTransform.anchorMax = new Vector2(1, 1);
            count.rectTransform.pivot = new Vector2(1, 0.5f);
            count.rectTransform.offsetMin = new Vector2(-410, 0);
            count.rectTransform.offsetMax = new Vector2(-330, 0);

            // Стоимость столбиком: [титан] 60 / [сплавы] 20
            var right = new Vector2(1, 0.5f);
            var minCost = MakeResLine(rt, LGIcon.Minerals, CMinerals, 14, 11, bold: true);
            ((RectTransform)minCost.Go.transform).At(right, new Vector2(0, 0.5f), new Vector2(-316, 10), new Vector2(70, 16));
            var alloyCost = MakeResLine(rt, LGIcon.Alloys, CAlloys, 14, 11, bold: true);
            ((RectTransform)alloyCost.Go.transform).At(right, new Vector2(0, 0.5f), new Vector2(-316, -10), new Vector2(70, 16));

            var btn = LGBuild.Button(rt, "Build", new Color(dc.r * 0.45f, dc.g * 0.45f, dc.b * 0.45f), new Color(dc.r, dc.g, dc.b, 0.75f),
                                     () => OnBuildDistrict(type), LGIcon.Construction, "ПОСТРОИТЬ", 11);
            ((RectTransform)btn.transform).At(right, new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(190, 36));
            var cancel = LGBuild.Button(rt, "Cancel", UIManager.DS.BtnDanger, UIManager.DS.Red, () => OnCancelDistrict(type), LGIcon.Close, null, 10, 12f);
            ((RectTransform)cancel.transform).At(right, new Vector2(1, 0.5f), new Vector2(-210, 0), new Vector2(32, 32));
            TooltipHelper.Attach(cancel.gameObject, "<b>Отменить</b>\nПоследний заказанный район этого типа. Ресурсы вернутся полностью.");
            cancel.gameObject.SetActive(false);

            var progHost = LGBuild.Rect(rt, "ProgressHost");
            progHost.anchorMin = new Vector2(0, 0);
            progHost.anchorMax = new Vector2(1, 0);
            progHost.offsetMin = new Vector2(74, 4);
            progHost.offsetMax = new Vector2(-14, 8);
            var progress = LGBuild.Bar(progHost, CGold, 0f, 4f);

            TooltipHelper.Attach(btn.gameObject,
                $"<b>{DistrictName(type)}</b>\n{DistrictEffect(type)}\n\n" +
                $"Строительство: {ConstructionManager.DistrictDays(type):0} дн. (на планете — одна стройка за раз)\n" +
                $"Стоимость: {DistrictInfo.MineralsCost(type)} титана, {DistrictInfo.AlloysCost(type)} сплавов\n" +
                "<color=#8AA2A8>Каждый район — одно рабочее место. Производство идёт, только если на нём работают жители.</color>");

            return new DistrictCard
            {
                Type = type, Count = count, Worked = worked, MinCost = minCost, AlloyCost = alloyCost, Bg = bg, Build = btn, Cancel = cancel, Progress = progress, Pips = pips,
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

        /// <summary>Тонкое кольцо (толщина в пикселях текстуры) — круглая рамка вокруг планеты.</summary>
        private static Sprite MakeRingSprite(int res, float thickness)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Vector2 c = new Vector2(res * 0.5f, res * 0.5f);
            float r = res * 0.5f - thickness - 1f;
            var px = new Color[res * res];
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float d = Mathf.Abs(Vector2.Distance(new Vector2(x, y), c) - r);
                px[y * res + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(thickness * 0.5f + 0.5f - d));
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
        }

        /// <summary>Мягкий радиальный ореол — свечение атмосферы за планетой.</summary>
        private static Sprite MakeHaloSprite(int res)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Vector2 c = new Vector2(res * 0.5f, res * 0.5f);
            float r = res * 0.5f;
            var px = new Color[res * res];
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / r;
                float a = Mathf.Clamp01(1f - d);
                px[y * res + x] = new Color(1f, 1f, 1f, a * a);
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
        }

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
