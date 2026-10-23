using UnityEngine;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    public class SystemNameplate : MonoBehaviour
    {
        public StarSystem Data => _data;

        private StarSystem _data;
        private Transform _cam;
        private GameObject _plateRoot;
        private TextMesh _titleMesh;
        private TextMesh _titleShadow;
        // Добыча системы — плашки со значками: титан (бирюзовый) и гелий-3 (золотой)
        private WorldChip _chipMin, _chipEn;
        // Узел силы: плашка над названием («★ Врата Предтеч»)
        private WorldChip _chipLm;
        private float _lmAlpha;
        // Форпост, который строит (или летит строить) ваш строительный корабль: сколько дней до готовности
        private WorldChip _chipBuild;
        private float _buildAlpha, _buildScan;
        private bool _buildOn;
        private string _buildKey = "";
        // Колония игрока (как в Master of Orion): слева от названия — жители и рост, справа — текущая стройка
        private WorldChip _chipPop, _chipJob;
        private Transform _starRow;          // ряд у самой звезды: жители слева, стройка справа
        private SpriteRenderer _starCore;
        private bool _popOn, _jobOn;
        private float _colScan;
        private string _popKey = "", _jobKey = "";
        private static StellarisClone.Generation.GalaxyGenerator s_gen;
        private int _lmOwner = -2;
        private string _resKey;
        private bool _hasMin, _hasEn;
        private bool _isSelected;
        private bool _isHovered;
        private bool _tooltipShown;
        private bool _hiddenByIsolation;

        // Уровни детализации по расстоянию камеры (как в Stellaris: издалека — только империи,
        // ближе — свои и чужие владения, вплотную — все системы и добыча)
        private const float NearFrom = 85f, NearTo = 115f;    // все системы
        private const float MidFrom = 165f, MidTo = 205f;     // владения и системы с вашими флотами
        private const float ResFrom = 60f, ResTo = 85f;       // строка добычи
        private float _alpha = 1f, _resAlpha = 1f;
        private Color _titleBase = Color.white;
        private MeshRenderer _titleMr, _shadowMr;

        public void Initialize(StarSystem data)
        {
            _data = data;
            if (Camera.main != null) _cam = Camera.main.transform;

            CreateStellarisNameplate();
            UpdateVisuals();

            if (SystemNameplateHover.Instance != null)
                SystemNameplateHover.Instance.Register(this);
        }

        private void CreateStellarisNameplate()
        {
            _plateRoot = new GameObject("Nameplate_Visual");
            _plateRoot.transform.SetParent(transform, false);
            _plateRoot.transform.localPosition = new Vector3(0, -3.2f, 0);

            // Шрифт игры (Play), как во всём интерфейсе; запасной — встроенный
            Font font = GameFont.Bold ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var shadowObj = new GameObject("TitleShadow");
            shadowObj.transform.SetParent(_plateRoot.transform, false);
            shadowObj.transform.localPosition = new Vector3(0.08f, -0.08f, 0.02f);

            _titleShadow = shadowObj.AddComponent<TextMesh>();
            _titleShadow.font = font;
            _titleShadow.fontSize = 52;
            _titleShadow.characterSize = 0.16f;
            _titleShadow.fontStyle = FontStyle.Bold;
            _titleShadow.anchor = TextAnchor.MiddleCenter;
            _titleShadow.alignment = TextAlignment.Center;
            _titleShadow.text = _data.Name;
            _titleShadow.color = new Color(0f, 0f, 0f, 0.95f);

            var mrShadow = shadowObj.GetComponent<MeshRenderer>();
            if (font != null) mrShadow.sharedMaterial = font.material;
            mrShadow.sortingOrder = 49;
            _shadowMr = mrShadow;

            var titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(_plateRoot.transform, false);
            titleObj.transform.localPosition = Vector3.zero;

            _titleMesh = titleObj.AddComponent<TextMesh>();
            _titleMesh.font = font;
            _titleMesh.fontSize = 52;
            _titleMesh.characterSize = 0.16f;
            _titleMesh.fontStyle = FontStyle.Bold;
            _titleMesh.anchor = TextAnchor.MiddleCenter;
            _titleMesh.alignment = TextAlignment.Center;
            _titleMesh.text = _data.Name;

            var mrTitle = titleObj.GetComponent<MeshRenderer>();
            if (font != null) mrTitle.sharedMaterial = font.material;
            mrTitle.sortingOrder = 50;
            _titleMr = mrTitle;

            var resRow = new GameObject("Resources");
            resRow.transform.SetParent(_plateRoot.transform, false);
            resRow.transform.localPosition = new Vector3(0, -1.15f, 0);
            _chipMin = WorldChip.Create(resRow.transform, "Titan", 46, false, true);
            _chipMin.SetIcon(LGIcon.Minerals);
            _chipEn = WorldChip.Create(resRow.transform, "Helium", 46, false, true);
            _chipEn.SetIcon(LGIcon.Energy);
            _chipMin.SetAlpha(0f);
            _chipEn.SetAlpha(0f);

            CreateLandmarkChip();
            CreateBuildChip();
            CreateColonyChips();
            UpdateResourceBadge();
        }

        private void CreateBuildChip()
        {
            var row = new GameObject("OutpostBuild");
            row.transform.SetParent(_plateRoot.transform, false);
            row.transform.localPosition = new Vector3(0, _chipLm != null ? 2.6f : 1.45f, 0);
            _chipBuild = WorldChip.Create(row.transform, "OutpostChip", 53, true, true);
            _chipBuild.SetIcon(LGIcon.Starbase);
            Color g = UIManager.DS.Green;
            _chipBuild.SetColors(new Color(0.02f, 0.07f, 0.05f, 0.90f), new Color(g.r, g.g, g.b, 0.85f), g, g, Color.white);
            _chipBuild.SetAlpha(0f);
            _buildScan = Random.value * 0.25f;   // подписи обновляются вразнобой, не все в один кадр
        }

        private void CreateColonyChips()
        {
            Color g = UIManager.DS.Green, o = new Color(1f, 0.66f, 0.30f);
            _starRow = new GameObject("ColonyRow").transform;
            _starRow.SetParent(transform, false);
            _starRow.localPosition = Vector3.zero;
            var core = transform.Find("StarCore");
            if (core != null) _starCore = core.GetComponent<SpriteRenderer>();
            _chipPop = WorldChip.Create(_starRow, "ColonyPop", 51, false, true);
            _chipPop.SetIcon(LGIcon.Population);
            _chipPop.SetColors(new Color(0.02f, 0.07f, 0.05f, 0.88f), new Color(g.r, g.g, g.b, 0.75f), g, g, Color.white);
            _chipPop.SetAlpha(0f);
            _chipJob = WorldChip.Create(_starRow, "ColonyJob", 51, false, true);
            _chipJob.SetIcon(LGIcon.Construction);
            _chipJob.SetColors(new Color(0.08f, 0.05f, 0.02f, 0.88f), new Color(o.r, o.g, o.b, 0.75f), o, o, Color.white);
            _chipJob.SetAlpha(0f);
            _colScan = Random.value * 0.5f;
        }

        private ConstructionJob _chipJobRef;

        /// <summary>Подсказка ярлыка под курсором (жители или стройка); null — курсор не над ярлыком.</summary>
        public string ChipTooltipAt(Camera cam, Vector2 screen)
        {
            if (_data == null || _data.OwnerId != 0) return null;
            if (_popOn && _chipPop != null && _chipPop.ContainsScreenPoint(cam, screen)) return PopTooltip();
            if (_jobOn && _chipJob != null && _chipJob.ContainsScreenPoint(cam, screen)) return JobTooltip();
            return null;
        }

        private string PopTooltip()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Loc.F("<b>Население системы {0}</b>", _data.Name));
            sb.Append(Loc.T("\n<color=#8AA2A8>Число — жители всех колоний; срок — когда появится новый житель в самой большой колонии</color>"));
            foreach (var p in _data.Planets)
            {
                if (p.Population <= 0) continue;
                sb.Append(Loc.F("\n\n<b>{0}</b> — жителей {1} из {2}", p.Name, p.Population, p.HousingCapacity));
                int months = p.MonthsToNextPop;
                if (months > 0)
                    sb.Append(Loc.F("\nРост +{0:0.#}% в месяц · новый житель через ~{1} мес.", p.PopGrowthBaseSpeedPctPerMonth, months));
                else if (p.Population >= p.HousingCapacity)
                    sb.Append(Loc.T("\n<color=#F2C747>Рост остановлен: нет жилья — постройте городской район</color>"));
                else
                    sb.Append(Loc.T("\n<color=#F2C747>Рост остановлен</color>"));
                var tier = p.Tier;
                sb.Append(Loc.F("\nСтабильность {0:0} · <color=#{1}>{2}</color>", p.Stability,
                    PlanetStability.Hex(PlanetStability.TierColor(tier)), PlanetStability.TierName(tier)));
                if (p.IdlePops > 0) sb.Append(Loc.F("\n<color=#FF9A8A>Без работы: {0}</color>", p.IdlePops));
            }
            sb.Append(Loc.T("\n\n<color=#8AA2A8>Нажмите на систему, чтобы открыть колонию</color>"));
            return sb.ToString();
        }

        private string JobTooltip()
        {
            var j = _chipJobRef;
            if (j == null) return null;
            var sb = new System.Text.StringBuilder();
            sb.Append(Loc.F("<b>Стройка: {0}</b>", BuildMenuModal.JobTitle(j)));
            if (j.PlanetIndex >= 0 && j.PlanetIndex < _data.Planets.Count) sb.Append(Loc.F("\nПланета {0}", _data.Planets[j.PlanetIndex].Name));
            sb.Append(Loc.F("\nГотово через {0} дн. ({1:0}%)", Mathf.CeilToInt(j.DaysLeft), j.Progress * 100f));
            int more = 0;
            var cm = ConstructionManager.Instance;
            if (cm != null)
                foreach (var o in cm.Jobs)
                    if (o != null && o != j && o.Owner == 0 && o.SystemId == _data.Id && o.Kind != JobKind.Colony && o.Kind != JobKind.Terraform) more++;
            if (more > 0) sb.Append(Loc.F("\nЕщё в очереди системы: {0}", more));
            sb.Append(Loc.T("\n<color=#8AA2A8>На ярлыке — стройка, которая закончится раньше всех</color>"));
            return sb.ToString();
        }

        /// <summary>Жители всех ваших колоний системы, рост главной и ближайшая к завершению текущая стройка (районы, проекты, корабли).</summary>
        private void UpdateColonyChips()
        {
            _colScan -= Time.unscaledDeltaTime;
            if (_colScan > 0f) return;
            _colScan = 0.5f;
            _popOn = _jobOn = false;
            if (_data.OwnerId != 0) return;

            int pop = 0;
            PlanetData main = null;
            foreach (var p in _data.Planets)
            {
                if (p.Population <= 0) continue;
                pop += p.Population;
                if (main == null || p.Population > main.Population) main = p;
            }

            ConstructionJob best = null;
            float bestDays = float.MaxValue;
            var cm = ConstructionManager.Instance;
            if (cm != null)
                foreach (var j in cm.Jobs)
                {
                    if (j == null || j.Owner != 0 || j.SystemId != _data.Id || !cm.IsActive(j)) continue;
                    if (j.Kind == JobKind.Colony || j.Kind == JobKind.Terraform) continue;   // эти — на плашке сверху
                    if (j.DaysLeft < bestDays) { bestDays = j.DaysLeft; best = j; }
                }

            // Радиус звезды в единицах ряда (звезда на карте не мельче нескольких пикселей — берём её спрайт)
            float rowScale = Mathf.Max(0.0001f, _starRow.lossyScale.x);
            float starR = _starCore != null ? _starCore.bounds.extents.x / rowScale * 0.55f : 0.9f;
            starR = Mathf.Clamp(starR, 0.6f, 3f);
            const float gap = 0.35f;
            if (main != null)
            {
                _popOn = true;
                int months = main.MonthsToNextPop;
                string t = months > 0 ? Loc.F("{0} · {1} мес.", pop, months) : pop.ToString();
                if (t != _popKey)
                {
                    _popKey = t;
                    _chipPop.Set(t, 0.36f);
                    _chipPop.CenterOn(0f, 0f);
                }
                _chipPop.Root.localPosition = new Vector3(-(starR + gap + _chipPop.Width * 0.5f), 0f, -0.02f);
            }
            _chipJobRef = best;
            if (best != null)
            {
                _jobOn = true;
                string t = Loc.F("{0} дн.", Mathf.CeilToInt(bestDays));
                string key = t + (int)best.Kind + best.District + best.Hull;
                if (key != _jobKey)
                {
                    _jobKey = key;
                    _chipJob.SetIcon(BuildMenuModal.JobIcon(best));
                    _chipJob.Set(t, 0.36f);
                    _chipJob.CenterOn(0f, 0f);
                }
                _chipJob.Root.localPosition = new Vector3(starR + gap + _chipJob.Width * 0.5f, 0f, -0.02f);
            }
        }

        /// <summary>
        /// Ваш форпост в системе: строится сейчас (building) или строительный корабль ещё в пути — тогда days включает полёт.
        /// false — никто из ваших строителей сюда не назначен.
        /// </summary>
        public static bool PlayerOutpostEta(int systemId, out bool building, out float days)
        {
            building = false;
            days = 0f;
            var fm = FleetManager.Instance;
            if (fm == null) return false;
            float best = float.MaxValue;
            foreach (var f in fm.AllFleets)
            {
                var d = f != null ? f.Data : null;
                if (d == null || d.Destroyed || d.OwnerId != 0 || d.Type != FleetType.Constructor || d.BuildTargetSystemId != systemId) continue;
                bool b = d.State == FleetState.Constructing && d.CurrentSystemId == systemId;
                float t = b ? d.DaysRemainingConstruction : ArrivalDays(d, systemId) + FactionTraits.OutpostDays(0);
                if (t < best) { best = t; building = b; }
            }
            if (best == float.MaxValue) return false;
            days = Mathf.Max(0f, best);
            return true;
        }

        private static float ArrivalDays(FleetData d, int systemId)
        {
            if (d.CurrentSystemId == systemId && d.State != FleetState.InHyperlane) return 0f;
            if (s_gen == null) s_gen = FindAnyObjectByType<StellarisClone.Generation.GalaxyGenerator>();
            var nodes = FleetRoute.Build(d, s_gen);
            foreach (var n in nodes) if (n.SystemId == systemId) return n.Days;
            return nodes.Count > 0 ? nodes[nodes.Count - 1].Days : 0f;
        }

        private void UpdateBuildChip()
        {
            _buildScan -= Time.unscaledDeltaTime;
            if (_buildScan > 0f) return;
            _buildScan = 0.25f;
            string text;
            LGIcon icon;
            if (PlayerOutpostEta(_data.Id, out bool building, out float days))
            {
                text = building ? Loc.F("ФОРПОСТ · {0}", FleetRoute.FormatDays(days)) : Loc.F("ФОРПОСТ · ~{0}", FleetRoute.FormatDays(days));
                icon = LGIcon.Starbase;
            }
            else if (PlayerPlanetProject(_data.Id, out var job, out float jdays))
            {
                text = $"{ProjectName(job.Kind)} · {FleetRoute.FormatDays(jdays)}";
                icon = job.Kind == JobKind.Colony ? LGIcon.Planet : LGIcon.Globe;
            }
            else { _buildOn = false; return; }
            _buildOn = true;
            if (text == _buildKey) return;
            _buildKey = text;
            _chipBuild.SetIcon(icon);
            _chipBuild.Set(text, 0.40f);
            _chipBuild.CenterOn(0f, 0f);
        }

        /// <summary>
        /// Ваш планетарный проект в системе — колония или терраформинг (ближайший к завершению).
        /// Районы и станции на карту не выносятся: их много, они видны в окне планеты.
        /// </summary>
        public static bool PlayerPlanetProject(int systemId, out ConstructionJob job, out float days)
        {
            job = null;
            days = float.MaxValue;
            var cm = ConstructionManager.Instance;
            if (cm == null) return false;
            foreach (var j in cm.Jobs)
            {
                if (j == null || j.Owner != 0 || j.SystemId != systemId) continue;
                if (j.Kind != JobKind.Colony && j.Kind != JobKind.Terraform) continue;
                float d = cm.DaysUntilDone(j);
                if (d < days) { days = d; job = j; }
            }
            if (job == null) { days = 0f; return false; }
            return true;
        }

        private static string ProjectName(JobKind k) => k == JobKind.Colony ? Loc.T("КОЛОНИЯ") : Loc.T("ТЕРРАФОРМИНГ");

        private static string PlanetName(ConstructionJob j)
        {
            var sys = EmpireStats.GetSystem(j.SystemId);
            return sys != null && j.PlanetIndex >= 0 && j.PlanetIndex < sys.Planets.Count ? sys.Planets[j.PlanetIndex].Name : Loc.T("планета");
        }

        public static LGIcon LandmarkIcon(int type) => type switch
        {
            (int)LandmarkType.Gateway => LGIcon.Propulsion,
            (int)LandmarkType.Pulsar => LGIcon.Research,
            (int)LandmarkType.Monument => LGIcon.Influence,
            (int)LandmarkType.Bastion => LGIcon.Starbase,
            (int)LandmarkType.Beacon => LGIcon.Sensors,
            _ => LGIcon.Minerals
        };

        private void CreateLandmarkChip()
        {
            if (_data.Landmark < 0) return;
            var row = new GameObject("Landmark");
            row.transform.SetParent(_plateRoot.transform, false);
            row.transform.localPosition = new Vector3(0, 1.45f, 0);
            _chipLm = WorldChip.Create(row.transform, "LandmarkChip", 52, true, true);
            _chipLm.SetIcon(LandmarkIcon(_data.Landmark));
            _chipLm.Set(Landmarks.Name(_data.Landmark), 0.40f);
            _chipLm.CenterOn(0f, 0f);
            _chipLm.SetAlpha(0f);
            RefreshLandmarkColors();
        }

        /// <summary>Цвет плашки: цвет узла, а если им владеет империя — кромка её цвета.</summary>
        private void RefreshLandmarkColors()
        {
            if (_chipLm == null) return;
            _lmOwner = _data.HasStarbase ? _data.OwnerId : -1;
            Color lc = Landmarks.Color(_data.Landmark);
            Color rim = _lmOwner >= 0 ? FleetIndicator.OwnerColor(_lmOwner) : lc;
            _chipLm.SetColors(new Color(0.02f, 0.05f, 0.07f, 0.88f),
                              new Color(rim.r, rim.g, rim.b, _lmOwner >= 0 ? 0.9f : 0.55f),
                              lc, lc, Color.white);
        }

        public void UpdateResourceBadge()
        {
            if (_chipMin == null || _data == null) return;

            // Добыча, а если ещё ничего не добывается — залежи (бледнее)
            int min = 0, en = 0;
            bool potential = false;
            if (_data.IsSurveyed)
            {
                min = _data.HarvestedMinerals;
                en = _data.HarvestedEnergy;
                if (min <= 0 && en <= 0)
                {
                    min = _data.PotentialMinerals;
                    en = _data.PotentialEnergy;
                    potential = min > 0 || en > 0;
                }
            }

            _hasMin = min > 0;
            _hasEn = en > 0;
            string key = $"{min}|{en}|{potential}";
            if (key == _resKey) { ApplyAlpha(); return; }
            _resKey = key;

            Color cyan = UIManager.DS.NeonCyan, gold = UIManager.DS.Gold;
            float dim = potential ? 0.55f : 1f;
            StyleChip(_chipMin, cyan, dim);
            StyleChip(_chipEn, gold, dim);

            float gap = 0.14f, total = 0f;
            if (min > 0) { _chipMin.Set(min.ToString(), 0.38f); total += _chipMin.Width; }
            if (en > 0) { _chipEn.Set(en.ToString(), 0.38f); total += (total > 0f ? gap : 0f) + _chipEn.Width; }
            float x = -total * 0.5f;
            if (min > 0) { _chipMin.CenterOn(x + _chipMin.Width * 0.5f, 0f); x += _chipMin.Width + gap; }
            if (en > 0) _chipEn.CenterOn(x + _chipEn.Width * 0.5f, 0f);
            ApplyAlpha();
        }

        private static void StyleChip(WorldChip c, Color col, float dim)
        {
            c.SetColors(new Color(0.015f, 0.04f, 0.055f, 0.80f * dim),
                        new Color(col.r, col.g, col.b, 0.45f * dim),
                        col,
                        new Color(col.r, col.g, col.b, dim),
                        new Color(Mathf.Lerp(col.r, 1f, 0.55f), Mathf.Lerp(col.g, 1f, 0.55f), Mathf.Lerp(col.b, 1f, 0.55f), dim));
        }

        public void UpdateVisuals()
        {
            if (_titleMesh == null) return;

            if (_isSelected)
                _titleBase = UIManager.DS.Gold;
            else if (_data.OwnerId == 0)
                _titleBase = UIManager.DS.NeonCyan;
            else if (_data.OwnerId > 0)
                _titleBase = Color.Lerp(FleetIndicator.OwnerColor(_data.OwnerId), Color.white, 0.12f);
            else if (_data.OwnerId == NeutralWorlds.Owner && NeutralWorlds.At(_data.Id) != null)
                _titleBase = Color.Lerp(NeutralWorlds.ColorOf(NeutralWorlds.At(_data.Id).Kind), Color.white, 0.15f);   // вольный мир — цвет характера
            else if (!GalaxyView.IsKnownToPlayer(_data))
                _titleBase = new Color(0.58f, 0.64f, 0.72f, 0.8f);   // неизведанная — блёклая
            else
            {
                var nc = UIManager.DS.TextPrimary;
                _titleBase = new Color(nc.r, nc.g, nc.b, 0.95f);
            }

            RefreshLandmarkColors();
            UpdateResourceBadge();
            ApplyAlpha();
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            UpdateVisuals();
        }

        public void SetHovered(bool hovered)
        {
            if (hovered && !_isHovered) SFXManager.Play(Sfx.MapHover);   // тихий «сонар»
            _isHovered = hovered;
            UpdateHoverTooltip();
        }

        public void SetVisible(bool visible)
        {
            _hiddenByIsolation = !visible;
            if (_plateRoot != null) _plateRoot.SetActive(visible);
            if (_starRow != null) _starRow.gameObject.SetActive(visible);
        }

        private void ApplyAlpha()
        {
            if (_titleMesh == null) return;
            var t = _titleBase; t.a *= _alpha;
            _titleMesh.color = t;
            if (_titleShadow != null) _titleShadow.color = new Color(0f, 0f, 0f, 0.95f * _alpha);
            bool on = _alpha > 0.01f;
            if (_titleMr != null && _titleMr.enabled != on) _titleMr.enabled = on;
            if (_shadowMr != null && _shadowMr.enabled != on) _shadowMr.enabled = on;
            if (_chipMin != null)
            {
                _chipMin.SetAlpha(_hasMin ? _resAlpha : 0f);
                _chipEn.SetAlpha(_hasEn ? _resAlpha : 0f);
            }
            _chipLm?.SetAlpha(_lmAlpha);
            _chipBuild?.SetAlpha(_buildAlpha);
            _chipPop?.SetAlpha(_popOn ? _alpha : 0f);
            _chipJob?.SetAlpha(_jobOn ? _alpha : 0f);
        }

        private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        /// <summary>Отладочный предпросмотр чёрной дыры (F8): класс звезды настоящий, подменена только картинка.</summary>
        private string PreviewNote()
        {
            var m = GetComponent<MapBlackHole>();
            return m != null && m.Preview ? Loc.T(" <color=#FFB070>(предпросмотр чёрной дыры, F8)</color>") : "";
        }

        public string GetTooltipContent()
        {
            string owner = _data.OwnerId == 0 ? Loc.T("Ваша империя")
                         : _data.OwnerId > 0 ? Contacts.NameForPlayer(_data.OwnerId, Loc.T("Другая империя"))   // до контакта — без имени
                         : _data.OwnerId == NeutralWorlds.Owner ? Loc.T("Вольный мир")
                         : Loc.T("Нейтральная");

            string tip = $"<b>{_data.Name}</b>\n" +
                         Loc.F("<color=#AAB4C0>Класс:</color> {0}{1}\n", EmpireOverviewWindow.StarClass(_data.SpectralClass), PreviewNote()) +
                         Loc.F("<color=#AAB4C0>Владелец:</color> {0}\n", owner);
            // Звёздная база: её мощь в привычных единицах — сколько ваших боевых кораблей она стоит
            var cm = CombatManager.Instance;
            if (cm != null && _data.HasStarbase && _data.OwnerId >= 0 && (_data.OwnerId == 0 || Vision.PlayerSees(_data.Id)))
            {
                var sb = cm.GetStarbase(_data.Id);
                if (sb != null)
                {
                    float pw = cm.StarbasePower(_data.Id);
                    string cmpText = CombatManager.PowerInShips(pw);
                    tip += sb.Disabled ? Loc.T("<color=#FF7A7A>Звёздная база выведена из строя</color>\n")
                                       : Loc.F("<color=#AAB4C0>Звёздная база:</color> мощь {0:N0}", pw) + (cmpText.Length > 0 ? $" ({cmpText})" : "") + "\n";
                }
            }

            var nw = NeutralWorlds.At(_data.Id);
            if (nw != null)
            {
                string c = LGBuild.Hex(NeutralWorlds.ColorOf(nw.Kind));
                tip += Loc.F("\n<color={0}>◆ {1} · {2}</color>\n", c, NeutralWorlds.Title(nw), NeutralWorlds.CharacterName(nw.Kind).ToLower()) +
                       $"<color=#AAB4C0>{NeutralWorlds.Description(nw.Kind)}</color>\n";
                if (!nw.Met(0)) tip += Loc.T("<color=#FFAA88>Нет контакта: подведите корабль или границу ближе</color>\n");
                else if (nw.AtWarWith(0)) tip += Loc.T("<color=#FF7A7A>Война</color>\n");
                else tip += Loc.F("<color=#AAB4C0>Ваше расположение:</color> {0:+0;-0;0}\n", nw.FavorOf(0));
                if (nw.Patron >= 0)
                    tip += Loc.F("<color=#AAB4C0>Покровитель:</color> {0}\n", nw.Patron == 0 ? Loc.T("вы") : Contacts.NameForPlayer(nw.Patron, Loc.T("другая империя")));
                tip += Loc.T("<color=#8AA2A8>Выберите систему — торговля, посольства и просьбы</color>\n");
                return tip + FleetsLine() + EtaLine();
            }

            if (_data.IsSurveyed)
            {
                tip += Loc.T("\n<color=#66FF88>ДОБЫЧА</color>\n") +
                       Loc.F("◆ Титан: +{0}\n", _data.HarvestedMinerals) +
                       Loc.F("⚡ Гелий-3: +{0}\n", _data.HarvestedEnergy);
            }
            else
            {
                tip += Loc.T("\n<color=#FFAA88>Требуется разведка</color>\n");
            }

            if (_data.Landmark >= 0)
            {
                string c = LGBuild.Hex(Landmarks.Color(_data.Landmark));
                bool mine = Landmarks.Controls(0, _data);
                tip += Loc.F("\n<color={0}>★ УЗЕЛ СИЛЫ · {1}</color>\n", c, Landmarks.Name(_data.Landmark)) +
                       $"{Landmarks.Effect(_data.Landmark)}\n" +
                       (mine ? Loc.T("<color=#66FF88>Под вашим контролем</color>\n")
                             : _data.HasStarbase && _data.OwnerId >= 0 ? Loc.T("<color=#FF8866>Удерживает ") + AIEmpireManager.NameOf(_data.OwnerId, Loc.T("другая империя")) + "</color>\n"
                             : Loc.T("<color=#8AA2A8>Постройте здесь форпост, чтобы получить бонус</color>\n"));
            }

            if (PlayerOutpostEta(_data.Id, out bool building, out float bdays))
                tip += building
                    ? Loc.F("\n<color=#5CE68A>Форпост строится · готов через {0}</color>\n", FleetRoute.FormatDays(bdays))
                    : Loc.F("\n<color=#5CE68A>Строитель летит сюда · форпост будет через ~{0}</color>\n", FleetRoute.FormatDays(bdays)) +
                      Loc.F("<color=#8AA2A8>полёт + {0:0} дн. стройки</color>\n", FactionTraits.OutpostDays(0));

            if (PlayerPlanetProject(_data.Id, out var job, out float jdays))
                tip += job.Kind == JobKind.Colony
                    ? Loc.F("\n<color=#5CE68A>Колония «{0}» основывается · готова через {1}</color>\n", PlanetName(job), FleetRoute.FormatDays(jdays))
                    : Loc.F("\n<color=#5CE68A>Терраформинг «{0}» · завершится через {1}</color>\n", PlanetName(job), FleetRoute.FormatDays(jdays));

            tip += FleetsLine() + EtaLine();
            return tip;
        }

        /// <summary>Флоты на орбите (свои и чужие).</summary>
        private string FleetsLine()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return "";
            int own = 0, enemy = 0;
            foreach (var f in fm.GetFleetsInSystem(_data.Id))
                if (f.Data.OwnerId == 0) own++; else enemy++;
            if (own == 0 && enemy == 0) return "";
            return Loc.T("\n<color=#AAB4C0>Флоты:</color> ") +
                   (own > 0 ? Loc.F("<color=#39EBDB>ваши {0}</color>", own) : "") +
                   (own > 0 && enemy > 0 ? "  ·  " : "") +
                   (enemy > 0 ? Loc.F("<color=#FF6666>чужие {0}</color>", enemy) : "") + "\n";
        }

        /// <summary>Сколько лететь сюда выделенному флоту.</summary>
        private string EtaLine()
        {
            var fm = FleetManager.Instance;
            var f = fm != null ? fm.SelectedFleet : null;
            if (f?.Data == null) return "";
            var gen = FindAnyObjectByType<StellarisClone.Generation.GalaxyGenerator>();
            int from = f.Data.State == FleetState.InHyperlane ? f.Data.TargetSystemId : f.Data.CurrentSystemId;
            if (from == _data.Id) return "";
            var path = GalaxyPathfinder.FindPath(from, _data.Id, gen, f.Data.OwnerId);
            if (path == null)
            {
                return GalaxyPathfinder.FindPath(from, _data.Id, gen) != null
                    ? "\n" + Loc.T("<color=#FF9A6A>Граница закрыта: нужен договор о границах, союз или война</color>") + "\n"
                    : "";
            }
            float days = path.Count * FleetRoute.DaysPerJump(f.Data);
            if (f.Data.State == FleetState.InHyperlane) days += f.Data.DaysRemainingInTransit / FleetRoute.JumpSpeed(f.Data);
            return Loc.F("\n<color=#4DF2DB>{0}: ~{1} ({2} прыжк.)</color>\n", f.Data.Name, FleetRoute.FormatDays(days), path.Count) +
                   Loc.T("<color=#8AA2A8>ПКМ — лететь, Shift+ПКМ — в очередь</color>");
        }

        private void UpdateHoverTooltip()
        {
            if (TooltipSystem.Instance == null) return;

            if (_isHovered)
            {
                if (!_tooltipShown)
                {
                    _tooltipShown = true;
                    TooltipSystem.Show(GetTooltipContent());
                }
            }
            else if (_tooltipShown)
            {
                _tooltipShown = false;
                TooltipSystem.Hide();
            }
        }

        private void LateUpdate()
        {
            if (_cam == null || _plateRoot == null) return;

            if (_hiddenByIsolation) return;
            if (!_plateRoot.activeSelf) _plateRoot.SetActive(true);
            if (_starRow != null && !_starRow.gameObject.activeSelf) _starRow.gameObject.SetActive(true);

            _plateRoot.transform.rotation = _cam.rotation;

            float dist = Vector3.Distance(_cam.position, transform.position);
            float scale = Mathf.Clamp(dist * 0.012f, 1.1f, 3.2f);
            _plateRoot.transform.localScale = Vector3.one * scale;
            if (_starRow != null)
            {
                _starRow.rotation = _cam.rotation;
                _starRow.localScale = Vector3.one * scale;
            }

            // Видимость подписи по зуму
            bool known = GalaxyView.IsKnownToPlayer(_data);
            float baseA = known || _data.OwnerId > 0 ? 1f : 0.5f;
            float near = 1f - Smooth(NearFrom, NearTo, dist);
            float mid = 1f - Smooth(MidFrom, MidTo, dist);
            float target = baseA * near;
            if (_data.OwnerId >= 0 || _data.OwnerId == NeutralWorlds.Owner) target = Mathf.Max(target, mid);
            if (GalaxyView.PlayerFleetSystems.Contains(_data.Id)) target = Mathf.Max(target, mid);
            if (_isSelected || _isHovered) target = 1f;
            float resTarget = known ? (1f - Smooth(ResFrom, ResTo, dist)) : 0f;
            if (_isSelected && known) resTarget = Mathf.Max(resTarget, near);

            // Плашка форпоста: видна издалека, пока ваш строитель работает или летит сюда
            if (_chipBuild != null)
            {
                UpdateBuildChip();
                float bTarget = _buildOn ? Mathf.Max(1f - Smooth(MidFrom + 90f, MidTo + 160f, dist), _isSelected || _isHovered ? 1f : 0f) : 0f;
                float nb = Mathf.MoveTowards(_buildAlpha, bTarget, Time.unscaledDeltaTime * 5f);
                if (Mathf.Abs(nb - _buildAlpha) > 0.0005f) { _buildAlpha = nb; _chipBuild.SetAlpha(_buildAlpha); }
            }

            if (_chipPop != null)
            {
                bool wasPop = _popOn, wasJob = _jobOn;
                UpdateColonyChips();
                if (wasPop != _popOn || wasJob != _jobOn) ApplyAlpha();
            }

            // Узлы силы видны издалека: это цели на карте
            if (_chipLm != null)
            {
                float lmTarget = known ? 1f - Smooth(MidFrom + 70f, MidTo + 110f, dist) : 0f;
                if (_isSelected || _isHovered) lmTarget = known ? 1f : 0f;
                float nl = Mathf.MoveTowards(_lmAlpha, lmTarget, Time.unscaledDeltaTime * 5f);
                if (Mathf.Abs(nl - _lmAlpha) > 0.0005f) { _lmAlpha = nl; _chipLm.SetAlpha(_lmAlpha); }
            }

            float k = Time.unscaledDeltaTime * 5f;
            float na = Mathf.MoveTowards(_alpha, target, k);
            float nr = Mathf.MoveTowards(_resAlpha, Mathf.Min(resTarget, target), k);
            if (Mathf.Abs(na - _alpha) > 0.0005f || Mathf.Abs(nr - _resAlpha) > 0.0005f)
            {
                _alpha = na; _resAlpha = nr;
                ApplyAlpha();
            }
        }

        private void OnDestroy()
        {
            if (SystemNameplateHover.Instance != null)
                SystemNameplateHover.Instance.Unregister(this);

            if (_plateRoot != null) Destroy(_plateRoot);
            if (_starRow != null) Destroy(_starRow.gameObject);
        }
    }
}