using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Cam;
using StellarisClone.Core;
using StellarisClone.Generation;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Окно «Обзор империи» (в духе Stellaris):
    ///   • шапка с гербом, названием и датой;
    ///   • 5 карточек ресурсов с запасом и доходом;
    ///   • слева — правитель, статистика и прогресс к победе;
    ///   • справа — вкладки «Колонии / Флоты / Границы / Соперник» с кликабельными строками
    ///     (клик — камера к объекту, окно закрывается).
    /// </summary>
    public partial class EmpireOverviewWindow : MonoBehaviour
    {
        public static EmpireOverviewWindow Instance { get; private set; }

        private enum Tab { Colonies, Fleets, Leaders, Borders, Rival }

        private GameObject _root;
        private Text _title, _subtitle;
        private Image _emblem, _emblemIcon;
        private readonly Text[] _resLabel = new Text[5];
        private readonly Text[] _resValue = new Text[5];
        private readonly Text[] _resIncome = new Text[5];
        private Text _leaderName, _leaderTitle, _leaderPower, _leaderDoctrine;
        private Image _portrait, _portraitIcon;
        private RectTransform _portraitHost;
        private string _portraitKey;
        private readonly Dictionary<string, Text> _stats = new Dictionary<string, Text>();
        private RectTransform _domBar, _sciBar, _survBar;
        private Text _domText, _sciText, _survText, _warnText;
        private readonly Dictionary<Tab, (Image bg, LiquidGlassEffect fx, Text label)> _tabs =
            new Dictionary<Tab, (Image, LiquidGlassEffect, Text)>();
        private RectTransform _tableHeader;
        private RectTransform _list;
        private Text _emptyText;
        private Tab _tab = Tab.Colonies;
        private float _refreshTimer;
        private GalaxyGenerator _gen;

        private static readonly Color CEnergy = new Color(1.00f, 0.80f, 0.32f);
        private static readonly Color CMinerals = new Color(0.36f, 0.92f, 0.95f);
        private static readonly Color CAlloys = new Color(0.95f, 0.62f, 0.36f);
        private static readonly Color CInfluence = new Color(1.00f, 0.48f, 0.58f);
        private static readonly Color CResearch = new Color(0.40f, 0.96f, 0.62f);
        private static readonly Color CGold = new Color(1.00f, 0.80f, 0.32f);
        private static readonly Color WindowRim = new Color(0.60f, 0.92f, 0.95f, 0.30f);

        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // ================================================================ API

        public void Build(Canvas host)
        {
            if (_root != null || host == null) return;
            BuildWindow(host.transform);
        }

        public void Open()
        {
            if (_root == null) return;
            _gen = FindAnyObjectByType<GalaxyGenerator>();
            UIManager.Instance?.ShowModalDimPublic();
            MapModeController.HideGlobal();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            RefreshAll();
        }

        public void Close()
        {
            if (_root == null || !_root.activeSelf) return;
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            MapModeController.ShowGlobal();
        }

        private void Update()
        {
            if (_root == null || !_root.activeInHierarchy) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 1f;
                RefreshHeader();
                RefreshResources();
                RefreshLeftColumn();
            }
        }

        // ================================================================ Построение

        private void BuildWindow(Transform host)
        {
            var rootRt = LGBuild.Rect(host, "EmpireOverviewWindow");
            rootRt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1440, 860));
            _root = rootRt.gameObject;
            var bg = _root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            LG.Glass(_root, 26f).SetRim(WindowRim);
            LG.Motion(_root, LGAppear.Kind.Pop).inDuration = 0.45f;

            BuildHeader(rootRt);
            BuildResourceRow(rootRt);

            var body = LGBuild.Rect(rootRt, "Body");
            body.Stretch(20, 20, 20, 186);

            var left = LGBuild.Rect(body, "Left");
            left.anchorMin = new Vector2(0, 0);
            left.anchorMax = new Vector2(0, 1);
            left.pivot = new Vector2(0, 1);
            left.offsetMin = new Vector2(0, 0);
            left.offsetMax = new Vector2(420, 0);
            BuildLeaderCard(left);
            BuildStats(left);
            BuildVictory(left);

            var right = LGBuild.Rect(body, "Right");
            right.Stretch(436, 0, 0, 0);
            BuildTabs(right);

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        /// <summary>Строка у верхнего края родителя: отступ сверху top (вниз), высота height, поля слева/справа.</summary>
        private static RectTransform TopRow(Transform parent, float top, float height, float left, float right)
        {
            var rt = LGBuild.Rect(parent, "Row");
            rt.TopBand(top, height, left, right);
            return rt;
        }

        private static Text Caption(Transform parent, float top, string text, Color col)
            => LGBuild.Label(TopRow(parent, top, 18, 18, 18), text, 10, col, TextAnchor.MiddleLeft, bold: true);

        private void BuildHeader(RectTransform root)
        {
            var header = LGBuild.Panel(root, "Header", UIManager.DS.BgHeader);
            header.rectTransform.TopBand(0, 78);
            LG.Header(header.gameObject);

            _emblem = LGBuild.Panel(header.transform, "Emblem", new Color(0.2f, 0.5f, 0.6f));
            _emblem.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(54, 54));
            var eFx = LG.Platter(_emblem.gameObject, 27f);
            eFx.FillMultiplier = 2.4f;
            _emblemIcon = LGIcons.Create(_emblem.transform, LGIcon.Leader, 30, Color.white);

            var titleRt = LGBuild.Rect(header.transform, "TitleBox");
            titleRt.Stretch(92, 0, 80, 0);
            _title = LGBuild.Label(titleRt, "ИМПЕРИЯ", 24, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            _title.rectTransform.offsetMax = new Vector2(0, -12);
            _subtitle = LGBuild.Label(titleRt, "", 12, UIManager.DS.TextMuted, TextAnchor.LowerLeft);
            _subtitle.rectTransform.offsetMin = new Vector2(0, 14);

            var close = LGBuild.Button(header.transform, "Close", new Color(0.16f, 0.20f, 0.24f),
                new Color(1f, 0.45f, 0.48f, 0.5f), Close, LGIcon.Close, null, 12, 17f);
            ((RectTransform)close.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-22, 0), new Vector2(34, 34));

            var line = LGBuild.Panel(header.transform, "Line", new Color(1f, 1f, 1f, 0.18f));
            line.rectTransform.anchorMin = new Vector2(0, 0);
            line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.sizeDelta = new Vector2(-60, 1.2f);
            LG.Line(line.gameObject, hairline: true);
        }

        private void BuildResourceRow(RectTransform root)
        {
            var row = LGBuild.Rect(root, "Resources");
            row.TopBand(92, 80, 20, 20);

            var defs = new (LGIcon icon, string label, Color col)[]
            {
                (LGIcon.Energy, "ГЕЛИЙ-3", CEnergy),
                (LGIcon.Minerals, "ТИТАН", CMinerals),
                (LGIcon.Alloys, "СПЛАВЫ", CAlloys),
                (LGIcon.Influence, "ВЛИЯНИЕ", CInfluence),
                (LGIcon.Research, "НАУКА", CResearch),
            };

            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var card = LGBuild.Panel(row, "Res_" + i, UIManager.DS.BgSlot);
                card.rectTransform.Column(i / 5f, (i + 1) / 5f, i == 0 ? 0 : 6, i == 4 ? 0 : 6);
                LG.Platter(card.gameObject, 18f).SetRim(new Color(d.col.r, d.col.g, d.col.b, 0.28f));

                var iconBg = LGBuild.Panel(card.transform, "IconBg", new Color(d.col.r * 0.22f, d.col.g * 0.22f, d.col.b * 0.22f, 1f));
                iconBg.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(48, 48));
                var ibFx = LG.Platter(iconBg.gameObject, 24f);
                ibFx.SetRim(new Color(d.col.r, d.col.g, d.col.b, 0.5f));
                ibFx.FillMultiplier = 2.2f;
                LGIcons.Create(iconBg.transform, d.icon, 26, d.col);

                var text = LGBuild.Rect(card.transform, "Text");
                text.Stretch(74, 10, 14, 10);
                _resLabel[i] = LGBuild.Label(text, d.label, 10, UIManager.DS.TextMuted, TextAnchor.UpperLeft, bold: true);
                _resValue[i] = LGBuild.Label(text, "0", 22, UIManager.DS.TextPrimary, TextAnchor.LowerLeft, bold: true);
                _resIncome[i] = LGBuild.Label(text, "", 12, UIManager.DS.TextMuted, TextAnchor.LowerRight, bold: true);
            }
        }

        private void BuildLeaderCard(RectTransform left)
        {
            var card = LGBuild.Panel(left, "LeaderCard", UIManager.DS.BgSlot);
            card.rectTransform.TopBand(0, 196);
            LG.Platter(card.gameObject, 18f);

            Caption(card.transform, 12, "ПРАВИТЕЛЬ ИМПЕРИИ", UIManager.DS.TextMuted);

            _portrait = LGBuild.Panel(card.transform, "Portrait", new Color(0.2f, 0.4f, 0.5f));
            _portrait.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -38), new Vector2(104, 146));
            LG.Platter(_portrait.gameObject, 8f).FillMultiplier = 2.2f;
            _portraitIcon = LGIcons.Create(_portrait.transform, LGIcon.Leader, 56, Color.white);
            _portraitHost = LG.RoundedMask(_portrait.transform, 8f, 1f);

            var info = LGBuild.Rect(card.transform, "Info");
            info.Stretch(136, 14, 16, 40);
            _leaderName = LGBuild.Label(info, "", 16, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            _leaderTitle = LGBuild.Label(info, "", 11, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            _leaderTitle.rectTransform.offsetMax = new Vector2(0, -24);

            var powerRow = LGBuild.Rect(info, "Power");
            powerRow.TopBand(46, 22);
            var pic = LGIcons.Create(powerRow, LGIcon.Fleet, 16, CGold);
            pic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16));
            _leaderPower = LGBuild.Label(powerRow, "", 13, CGold, TextAnchor.MiddleLeft, bold: true);
            _leaderPower.rectTransform.offsetMin = new Vector2(22, 0);

            _leaderDoctrine = LGBuild.Label(info, "", 10, new Color(0.80f, 0.88f, 0.92f), TextAnchor.UpperLeft, wrap: true);
            _leaderDoctrine.rectTransform.offsetMax = new Vector2(0, -74);
            _leaderDoctrine.lineSpacing = 1.1f;
            _leaderDoctrine.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private void BuildStats(RectTransform left)
        {
            var block = LGBuild.Panel(left, "Stats", UIManager.DS.BgSlot);
            block.rectTransform.TopBand(208, 200);
            LG.Platter(block.gameObject, 18f);

            Caption(block.transform, 10, "СТАТИСТИКА", UIManager.DS.TextMuted);

            var grid = LGBuild.Rect(block.transform, "Grid");
            grid.Stretch(12, 12, 12, 36);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(92, 72);
            gl.spacing = new Vector2(6, 8);
            gl.childAlignment = TextAnchor.UpperCenter;
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 4;

            AddStatTile(grid, "systems", LGIcon.Star, "Системы", UIManager.DS.NeonCyan);
            AddStatTile(grid, "colonies", LGIcon.Planet, "Колонии", UIManager.DS.Green);
            AddStatTile(grid, "pop", LGIcon.Population, "Население", new Color(0.75f, 0.9f, 1f));
            AddStatTile(grid, "housing", LGIcon.Housing, "Своб. жильё", new Color(0.7f, 0.85f, 0.95f));
            AddStatTile(grid, "idle", LGIcon.Warning, "Безработные", CGold);
            AddStatTile(grid, "districts", LGIcon.Industry, "Районы", CAlloys);
            AddStatTile(grid, "outposts", LGIcon.Starbase, "Форпосты", CMinerals);
            AddStatTile(grid, "fleets", LGIcon.Fleet, "Флоты", UIManager.DS.Red);
        }

        private void AddStatTile(RectTransform grid, string key, LGIcon icon, string label, Color col)
        {
            var tile = LGBuild.Panel(grid, "Tile_" + key, UIManager.DS.BgVisor);
            LG.Platter(tile.gameObject, 14f).SetRim(new Color(col.r, col.g, col.b, 0.16f));
            var ic = LGIcons.Create(tile.transform, icon, 16, col);
            ic.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(16, 16));
            var val = LGBuild.Label(tile.transform, "0", 17, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, bold: true);
            val.rectTransform.Stretch(2, 14, 2, 22);
            var lbl = LGBuild.Label(tile.transform, label, 9, UIManager.DS.TextMuted, TextAnchor.LowerCenter);
            lbl.rectTransform.Stretch(2, 6, 2, 0);
            _stats[key] = val;
        }

        private void BuildVictory(RectTransform left)
        {
            var block = LGBuild.Panel(left, "Victory", UIManager.DS.BgSlot);
            var rt = block.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0, -420);
            LG.Platter(block.gameObject, 18f).SetRim(new Color(CGold.r, CGold.g, CGold.b, 0.22f));

            var titleRow = TopRow(block.transform, 10, 18, 18, 18);
            var tIcon = LGIcons.Create(titleRow, LGIcon.Trophy, 14, CGold);
            tIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(14, 14));
            var tl = LGBuild.Label(titleRow, "ПУТИ К ПОБЕДЕ", 10, CGold, TextAnchor.MiddleLeft, bold: true);
            tl.rectTransform.offsetMin = new Vector2(20, 0);

            _domText = VictoryRow(block.transform, 38, LGIcon.Starbase, "Доминирование", UIManager.DS.NeonCyan, out _domBar);
            _sciText = VictoryRow(block.transform, 88, LGIcon.Research, "Наука", CResearch, out _sciBar);
            _survText = VictoryRow(block.transform, 138, LGIcon.Trophy, $"Очки · {VictoryManager.EndYear}", CEnergy, out _survBar);

            _warnText = LGBuild.Label(block.transform, "", 10, UIManager.DS.TextMuted, TextAnchor.LowerLeft, wrap: true);
            _warnText.rectTransform.Stretch(18, 10, 18, 0);
        }

        private static Text VictoryRow(Transform parent, float top, LGIcon icon, string name, Color col, out RectTransform bar)
        {
            var row = TopRow(parent, top, 42, 18, 18);
            var ic = LGIcons.Create(row, icon, 18, col);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -1), new Vector2(18, 18));

            var n = LGBuild.Label(row, name, 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            n.rectTransform.offsetMin = new Vector2(28, 0);
            n.rectTransform.offsetMax = new Vector2(-110, 0);
            var val = LGBuild.Label(row, "0 / 0", 12, col, TextAnchor.UpperRight, bold: true);
            val.rectTransform.offsetMin = new Vector2(200, 0);

            var barHost = LGBuild.Rect(row, "BarHost");
            barHost.anchorMin = new Vector2(0, 0);
            barHost.anchorMax = new Vector2(1, 0);
            barHost.pivot = new Vector2(0.5f, 0);
            barHost.offsetMin = new Vector2(28, 4);
            barHost.offsetMax = new Vector2(0, 14);
            bar = LGBuild.Bar(barHost, col, 0f, 6f);
            return val;
        }

        private void BuildTabs(RectTransform right)
        {
            var tabsRow = LGBuild.Rect(right, "Tabs");
            tabsRow.TopBand(0, 40);
            AddTab(tabsRow, Tab.Colonies, LGIcon.Planet, "КОЛОНИИ", 0);
            AddTab(tabsRow, Tab.Fleets, LGIcon.Fleet, "ФЛОТЫ", 1);
            AddTab(tabsRow, Tab.Leaders, LGIcon.Leader, "ЛИДЕРЫ", 2);
            AddTab(tabsRow, Tab.Borders, LGIcon.Starbase, "ГРАНИЦЫ", 3);
            AddTab(tabsRow, Tab.Rival, LGIcon.Diplomacy, "СОПЕРНИКИ", 4);

            var table = LGBuild.Panel(right, "Table", UIManager.DS.BgVisor);
            table.rectTransform.Stretch(0, 0, 0, 50);
            LG.Platter(table.gameObject, 18f);

            _tableHeader = LGBuild.Rect(table.transform, "TableHeader");
            _tableHeader.TopBand(8, 26, 16, 16);

            var listHost = LGBuild.Rect(table.transform, "ListHost");
            listHost.Stretch(6, 6, 6, 40);
            _list = LGBuild.ScrollList(listHost, 6f, 6);

            _emptyText = LGBuild.Label(table.transform, "", 13, UIManager.DS.TextMuted, TextAnchor.MiddleCenter, wrap: true);
            _emptyText.rectTransform.Stretch(80, 40, 80, 40);
        }

        private void AddTab(RectTransform row, Tab tab, LGIcon icon, string label, int index)
        {
            var bgImg = LGBuild.Panel(row, "Tab_" + tab, UIManager.DS.BtnNeutral, raycast: true);
            const int TabCount = 5;
            bgImg.rectTransform.Column(index / (float)TabCount, (index + 1) / (float)TabCount, index == 0 ? 0 : 4, index == TabCount - 1 ? 0 : 4);
            var b = bgImg.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() =>
            {
                _tab = tab;
                _assignLeaderId = -1;
                RefreshTabs();
                RebuildList();
                SFXManager.Play(Sfx.UiTab);
            });
            var fx = LG.Button(bgImg.gameObject, new Color(0.45f, 0.95f, 0.9f, 0.25f), 20f);
            var t = LGIcons.IconLabel(bgImg.transform, icon, label, 12, Color.white, UIManager.DS.TextMuted, 15f);
            _tabs[tab] = (bgImg, fx, t);
        }

        // ================================================================ Данные

        private void RefreshAll()
        {
            _refreshTimer = 1f;
            RefreshHeader();
            RefreshResources();
            RefreshLeftColumn();
            RefreshTabs();
            RebuildList();
        }

        private FactionInfo Faction => UIManager.Instance != null ? UIManager.Instance.SelectedFaction : null;

        private void RefreshHeader()
        {
            var f = Faction;
            Color fc = f != null ? f.EmpireColor : UIManager.DS.NeonCyan;
            _title.text = (f?.Name ?? "Империя").ToUpper();
            string date = TimeManager.Instance != null ? TimeManager.Instance.GetFormattedDate() : "";
            _subtitle.text = $"<color={LGBuild.Hex(fc)}>{f?.Title ?? "Правительство"}</color>   ·   Обзор империи   ·   {date}";
            _emblem.color = new Color(fc.r * 0.45f, fc.g * 0.45f, fc.b * 0.45f, 1f);
            _emblemIcon.color = Color.Lerp(fc, Color.white, 0.35f);
        }

        private void RefreshResources()
        {
            var eco = EconomyManager.Instance;
            var tm = TechnologyManager.Instance;
            if (eco != null)
            {
                SetRes(0, eco.EnergyCredits, eco.MonthlyEnergyIncome);
                SetRes(1, eco.Minerals, eco.MonthlyMineralsIncome);
                SetRes(2, eco.Alloys, eco.MonthlyAlloysIncome);
                SetRes(3, eco.Influence, eco.MonthlyInfluenceIncome);
                _resLabel[0].text = eco.IsBankrupt ? "ГЕЛИЙ-3  <color=#FF5A5A>· БАНКРОТ</color>" : "ГЕЛИЙ-3";
            }
            if (tm != null)
            {
                int active = 0;
                foreach (var s in tm.Slots) if (s.CurrentTech != null && !s.IsPaused) active++;
                _resLabel[4].text = $"НАУКА  <color=#8AA2A8>· слоты {active}/{tm.Slots.Count}</color>";
                _resValue[4].text = $"{tm.MonthlyResearchIncome:0.#}";
                _resIncome[4].text = "<color=#8AA2A8>/ мес</color>";
            }
        }

        private void SetRes(int i, float stock, float income)
        {
            _resValue[i].text = ((int)stock).ToString("N0");
            _resIncome[i].text = $"{LGBuild.Signed(income)} <color=#8AA2A8>/ мес</color>";
        }

        private void RefreshLeftColumn()
        {
            var f = Faction;
            Color fc = f != null ? f.EmpireColor : UIManager.DS.NeonCyan;
            var leader = LeaderPortraits.ForFaction(f);
            _leaderName.text = leader != null ? leader.Name : f?.Name ?? "Империя";
            _leaderTitle.text = leader != null ? leader.Title : f?.Title ?? "";
            string key = leader?.Key ?? "";
            if (key != _portraitKey)
            {
                _portraitKey = key;
                LGBuild.Clear(_portraitHost);
                bool ok = LeaderPortraitView.Create(_portraitHost, leader, zoom: 1.5f) != null;
                _portraitIcon.gameObject.SetActive(!ok);
            }
            _leaderDoctrine.text = f?.Description ?? "";
            _portrait.color = new Color(fc.r * 0.4f, fc.g * 0.4f, fc.b * 0.4f, 1f);
            _portrait.GetComponent<LiquidGlassEffect>()?.SetRim(new Color(fc.r, fc.g, fc.b, 0.6f));
            _portraitIcon.color = Color.Lerp(fc, Color.white, 0.3f);
            int power = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(0) : 0;
            _leaderPower.text = $"Мощь флота: {power:N0}";

            // --- Статистика ---
            int systems = 0, colonies = 0, pop = 0, housing = 0, idle = 0, districts = 0, outposts = 0, fleets = 0;
            if (_gen == null) _gen = FindAnyObjectByType<GalaxyGenerator>();
            if (_gen != null)
            {
                foreach (var s in _gen.Systems)
                {
                    if (s.OwnerId != 0) continue;
                    systems++;
                    if (s.HasStarbase) outposts++;
                    foreach (var p in s.Planets)
                    {
                        if (p.Population <= 0) continue;
                        colonies++;
                        pop += p.Population;
                        housing += p.HousingCapacity;
                        idle += p.IdlePops;
                        districts += p.BuiltDistricts;
                    }
                }
            }
            if (FleetManager.Instance != null)
                foreach (var fv in FleetManager.Instance.AllFleets)
                    if (fv != null && fv.Data != null && !fv.Data.Destroyed && fv.Data.OwnerId == 0) fleets++;

            SetStat("systems", systems.ToString());
            SetStat("colonies", colonies.ToString());
            SetStat("pop", pop.ToString("N0"));
            int free = housing - pop;
            SetStat("housing", $"<color={LGBuild.Hex(free >= 0 ? UIManager.DS.Green : UIManager.DS.Red)}>{free}</color>");
            SetStat("idle", idle > 0 ? $"<color={LGBuild.Hex(CGold)}>{idle}</color>" : "0");
            SetStat("districts", districts.ToString());
            SetStat("outposts", outposts.ToString());
            SetStat("fleets", fleets.ToString());

            // --- Победа ---
            var vm = VictoryManager.Instance;
            if (vm != null)
            {
                int dom = vm.DominationProgress, sci = vm.ScienceProgress;
                int me = vm.PlayerScore, rival = vm.RivalScore;
                _domText.text = $"{dom} / {vm.DominationRequired} систем";
                _sciText.text = $"{sci} / {vm.ScienceRequired} техн.";
                _survText.text = me >= rival ? $"{me} : {rival}" : $"<color=#FF6A6A>{me} : {rival}</color>";
                LGBuild.SetBar(_domBar, dom / (float)Mathf.Max(1, vm.DominationRequired));
                LGBuild.SetBar(_sciBar, sci / (float)Mathf.Max(1, vm.ScienceRequired));
                LGBuild.SetBar(_survBar, me / (float)Mathf.Max(1, me + rival));
                _warnText.text = vm.BankruptMonths > 0
                    ? $"<color=#FF6A6A>Банкротство: {vm.BankruptMonths} из {vm.BankruptcyLimit} мес. до краха экономики</color>"
                    : $"Соперник: {vm.RivalDominationProgress} систем, {vm.RivalScienceProgress} техн. Подсчёт очков через {vm.YearsLeft} {VictoryManager.YearsWord(vm.YearsLeft)}.";
            }
        }

        private void SetStat(string key, string v)
        {
            if (_stats.TryGetValue(key, out var t)) t.text = v;
        }

        private void RefreshTabs()
        {
            foreach (var kv in _tabs)
            {
                bool active = kv.Key == _tab;
                kv.Value.bg.color = active ? UIManager.DS.BtnPrimary : UIManager.DS.BtnNeutral;
                kv.Value.fx.SetRim(new Color(0.45f, 0.95f, 0.9f, active ? 0.75f : 0.18f));
                kv.Value.fx.GlowMultiplier = active ? 1.2f : 0.3f;
                kv.Value.label.color = active ? Color.white : UIManager.DS.TextMuted;
            }
        }

        // ================================================================ Списки

        private void RebuildList()
        {
            LGBuild.Clear(_list);
            LGBuild.Clear(_tableHeader);
            _emptyText.text = "";
            _list.anchoredPosition = Vector2.zero;
            if (_gen == null) _gen = FindAnyObjectByType<GalaxyGenerator>();

            switch (_tab)
            {
                case Tab.Colonies: BuildColonies(); break;
                case Tab.Fleets: BuildFleets(); break;
                case Tab.Leaders: BuildLeaders(); break;
                case Tab.Borders: BuildBorders(); break;
                case Tab.Rival: BuildRival(); break;
            }
            LG.Skin(_list);
        }

        private void HeaderCols(params (float x0, float x1, string text, TextAnchor a)[] cols)
        {
            foreach (var c in cols)
            {
                var r = LGBuild.Rect(_tableHeader, "H");
                r.Column(c.x0, c.x1, 6, 6);
                LGBuild.Label(r, c.text, 10, UIManager.DS.TextMuted, c.a, bold: true);
            }
        }

        private RectTransform Row(System.Action onClick, Color rim)
        {
            var img = LGBuild.Panel(_list, "Row", UIManager.DS.BgRowEven, raycast: true);
            LGBuild.Height(img.gameObject, 48);
            var b = img.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => onClick?.Invoke());
            var fx = LG.Button(img.gameObject, rim, 14f);
            fx.FillMultiplier = 0.7f;
            fx.GlowMultiplier = 0.3f;
            fx.GetComponent<LGInteractive>().hoverScale = 1.01f;
            return img.rectTransform;
        }

        private static RectTransform Cell(RectTransform row, float x0, float x1)
        {
            var c = LGBuild.Rect(row, "Cell");
            c.Column(x0, x1, 6, 6);
            return c;
        }

        private static void IconCell(RectTransform row, float x0, float x1, LGIcon icon, Color col)
        {
            var c = Cell(row, x0, x1);
            var ic = LGIcons.Create(c, icon, 22, col);
            ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));
        }

        private static void TwoLine(RectTransform row, float x0, float x1, string top, string bottom)
        {
            var c = Cell(row, x0, x1);
            var t1 = LGBuild.Label(c, top, 13, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            t1.rectTransform.offsetMax = new Vector2(0, -6);
            var t2 = LGBuild.Label(c, bottom, 10, UIManager.DS.TextMuted, TextAnchor.LowerLeft);
            t2.rectTransform.offsetMin = new Vector2(0, 6);
        }

        private static Text TextCell(RectTransform row, float x0, float x1, string text, Color col,
                                     TextAnchor a = TextAnchor.MiddleLeft, int size = 12, bool bold = false)
            => LGBuild.Label(Cell(row, x0, x1), text, size, col, a, bold);

        private static void MiniStats(RectTransform row, float x0, float x1, params (LGIcon icon, Color col, string val)[] items)
        {
            var c = Cell(row, x0, x1);
            var hlg = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleRight;
            hlg.spacing = 4;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            foreach (var it in items)
            {
                var ic = LGIcons.Create(c, it.icon, 14, it.col);
                var le = ic.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.minWidth = 14;
                le.preferredHeight = le.minHeight = 14;

                var tGo = new GameObject("V");
                tGo.transform.SetParent(c, false);
                var t = tGo.AddComponent<Text>();
                t.font = GameFont.Bold;
                t.fontSize = 12;
                t.color = UIManager.DS.TextPrimary;
                t.text = it.val + "   ";
                t.alignment = TextAnchor.MiddleLeft;
                t.raycastTarget = false;
                t.supportRichText = true;
            }
        }

        private static void BarCell(RectTransform row, float x0, float x1, float value, Color col, string label)
        {
            var c = Cell(row, x0, x1);
            var lbl = LGBuild.Label(c, label, 11, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            lbl.rectTransform.offsetMax = new Vector2(0, -6);
            var host = LGBuild.Rect(c, "BarHost");
            host.anchorMin = new Vector2(0, 0);
            host.anchorMax = new Vector2(1, 0);
            host.pivot = new Vector2(0.5f, 0);
            host.offsetMin = new Vector2(0, 8);
            host.offsetMax = new Vector2(0, 16);
            LGBuild.Bar(host, col, value, 6f);
        }

        private void Empty(string msg) => _emptyText.text = msg;

        // ---------- Колонии ----------

        private void BuildColonies()
        {
            HeaderCols((0.06f, 0.30f, "КОЛОНИЯ", TextAnchor.MiddleLeft),
                       (0.30f, 0.44f, "КЛАСС", TextAnchor.MiddleLeft),
                       (0.44f, 0.62f, "НАСЕЛЕНИЕ", TextAnchor.MiddleLeft),
                       (0.62f, 0.72f, "РАЙОНЫ", TextAnchor.MiddleCenter),
                       (0.72f, 1f, "ПРОИЗВОДСТВО / МЕС", TextAnchor.MiddleRight));
            int n = 0;
            if (_gen != null)
            {
                foreach (var sys in _gen.Systems)
                {
                    if (sys.OwnerId != 0) continue;
                    foreach (var p in sys.Planets)
                    {
                        if (p.Population <= 0) continue;
                        n++;
                        var capturedSys = sys;
                        var row = Row(() => FocusSystem(capturedSys), new Color(p.PlanetColor.r, p.PlanetColor.g, p.PlanetColor.b, 0.3f));
                        IconCell(row, 0f, 0.06f, LGIcon.Planet, Color.Lerp(p.PlanetColor, Color.white, 0.25f));
                        TwoLine(row, 0.06f, 0.30f, p.Name, "система " + sys.Name);
                        TextCell(row, 0.30f, 0.44f, p.ClassDisplayName, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, 11);
                        float fillPop = p.HousingCapacity > 0 ? p.Population / (float)p.HousingCapacity : 1f;
                        Color popCol = p.Population <= p.HousingCapacity ? UIManager.DS.Green : UIManager.DS.Red;
                        BarCell(row, 0.44f, 0.62f, fillPop, popCol,
                            $"{p.Population} / {p.HousingCapacity}" + (p.IdlePops > 0 ? $"   <color=#F2C747>безраб. {p.IdlePops}</color>" : ""));
                        TextCell(row, 0.62f, 0.72f, $"{p.BuiltDistricts} / {p.MaxDistricts}", UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, 13, true);
                        MiniStats(row, 0.72f, 1f,
                            (LGIcon.Minerals, CMinerals, "+" + p.ProducedMineralsPerMonth),
                            (LGIcon.Energy, CEnergy, "+" + p.ProducedEnergyPerMonth),
                            (LGIcon.Alloys, CAlloys, "+" + p.ProducedAlloysPerMonth));
                    }
                }
            }
            if (n == 0)
                Empty("Колоний пока нет.\n\nВойдите в свою систему (двойной клик по звезде), выберите планету с пригодностью от 40% " +
                      "и нажмите «Основать колонию» — нужно 80 титана, 20 сплавов и 25 влияния.");
        }

        // ---------- Флоты ----------

        private void BuildFleets()
        {
            HeaderCols((0.06f, 0.32f, "ФЛОТ", TextAnchor.MiddleLeft),
                       (0.32f, 0.52f, "ЗАДАЧА", TextAnchor.MiddleLeft),
                       (0.52f, 0.68f, "РАСПОЛОЖЕНИЕ", TextAnchor.MiddleLeft),
                       (0.68f, 0.86f, "ПРОЧНОСТЬ", TextAnchor.MiddleLeft),
                       (0.86f, 1f, "МОЩЬ", TextAnchor.MiddleRight));
            int n = 0;
            var fm = FleetManager.Instance;
            if (fm != null)
            {
                foreach (var fv in fm.AllFleets)
                {
                    if (fv == null || fv.Data == null || fv.Data.Destroyed || fv.Data.OwnerId != 0) continue;
                    var d = fv.Data;
                    n++;
                    Color col = d.Type == FleetType.Military ? UIManager.DS.Red
                              : d.Type == FleetType.Science ? CResearch : CGold;
                    LGIcon icon = d.Type == FleetType.Military ? LGIcon.Fleet
                                : d.Type == FleetType.Science ? LGIcon.Sensors : LGIcon.Construction;
                    string type = d.Type == FleetType.Military ? "Боевой флот"
                                : d.Type == FleetType.Science ? "Научный корабль" : "Строительный корабль";
                    var captured = fv;
                    var row = Row(() => FocusFleet(captured), new Color(col.r, col.g, col.b, 0.28f));
                    IconCell(row, 0f, 0.06f, icon, col);
                    TwoLine(row, 0.06f, 0.32f, d.Name, type);
                    TextCell(row, 0.32f, 0.52f, FleetTask(d), UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, 11);
                    TextCell(row, 0.52f, 0.68f, SystemName(d.CurrentSystemId), UIManager.DS.TextMuted, TextAnchor.MiddleLeft, 11);
                    float hp = d.IntegrityNormalized;
                    BarCell(row, 0.68f, 0.86f, hp,
                        hp > 0.5f ? UIManager.DS.Green : hp > 0.25f ? CGold : UIManager.DS.Red, $"{hp * 100f:0}%");
                    TextCell(row, 0.86f, 1f, d.Type == FleetType.Military ? Mathf.RoundToInt(CombatMath.Power(d)).ToString("N0") : "—",
                        CGold, TextAnchor.MiddleRight, 14, true);
                }
            }
            if (n == 0) Empty("Флотов нет. Постройте корабли на орбитальной верфи столицы.");
        }

        private string FleetTask(FleetData d)
        {
            switch (d.State)
            {
                case FleetState.InHyperlane: return "Перелёт → " + SystemName(d.TargetSystemId);
                case FleetState.Surveying: return $"Разведка ({Mathf.Max(0, d.DaysRemainingSurvey):0} дн.)";
                case FleetState.Constructing: return $"Строительство ({Mathf.Max(0, d.DaysRemainingConstruction):0} дн.)";
            }
            if (d.InCombat) return "<color=#FF6060>В бою</color>";
            var fm = FleetManager.Instance;
            if (fm != null && fm.NeedsRepair(d))
            {
                float rate = fm.RepairRateAt(d.CurrentSystemId, d.OwnerId);
                return rate > 0f ? $"<color=#5CF59A>Ремонт · {rate * 100f:0}% в день</color>"
                                 : "<color=#FFAA55>Повреждён — ремонт только на своей территории</color>";
            }
            var siege = SiegeManager.Instance?.GetSiege(d.CurrentSystemId);
            if (siege != null && siege.Attacker == d.OwnerId && d.Type == FleetType.Military)
                return $"<color=#5CF59A>Осада · {siege.Progress:0} дн.</color>";
            return "На орбите — ждёт приказа";
        }

        private string SystemName(int id)
        {
            if (_gen == null || id < 0 || id >= _gen.Systems.Count) return "—";
            return _gen.Systems[id].Name;
        }

        // ---------- Границы ----------

        private void BuildBorders()
        {
            HeaderCols((0.06f, 0.34f, "СИСТЕМА", TextAnchor.MiddleLeft),
                       (0.34f, 0.50f, "ЗВЕЗДА", TextAnchor.MiddleLeft),
                       (0.50f, 0.64f, "ПЛАНЕТ", TextAnchor.MiddleCenter),
                       (0.64f, 0.76f, "КОРИДОРЫ", TextAnchor.MiddleCenter),
                       (0.76f, 1f, "ДОБЫЧА / МЕС", TextAnchor.MiddleRight));
            int n = 0;
            if (_gen != null)
            {
                foreach (var sys in _gen.Systems)
                {
                    if (sys.OwnerId != 0) continue;
                    n++;
                    bool capital = sys.Id == 0;
                    var captured = sys;
                    var row = Row(() => FocusSystem(captured), new Color(0.45f, 0.95f, 0.9f, capital ? 0.4f : 0.2f));
                    IconCell(row, 0f, 0.06f, capital ? LGIcon.Shipyard : LGIcon.Starbase, capital ? CGold : CMinerals);
                    TwoLine(row, 0.06f, 0.34f, sys.Name,
                        capital ? "Столица · орбитальная верфь" : sys.HasStarbase ? "Пограничный форпост" : "Контролируемая система");
                    TextCell(row, 0.34f, 0.50f, StarClass(sys.SpectralClass), UIManager.DS.TextMuted, TextAnchor.MiddleLeft, 11);
                    TextCell(row, 0.50f, 0.64f, sys.Planets.Count.ToString(), UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, 13, true);
                    TextCell(row, 0.64f, 0.76f, sys.ConnectedSystemIds.Count.ToString(), UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, 13, true);
                    if (sys.IsSurveyed)
                        MiniStats(row, 0.76f, 1f,
                            (LGIcon.Minerals, CMinerals, "+" + sys.HarvestedMinerals),
                            (LGIcon.Energy, CEnergy, "+" + sys.HarvestedEnergy));
                    else
                        TextCell(row, 0.76f, 1f, "не изучено", UIManager.DS.TextMuted, TextAnchor.MiddleRight, 11);
                }
            }
            if (n == 0) Empty("У империи не осталось систем.");
        }

        private static string StarClass(StarSpectralClass c)
        {
            switch (c)
            {
                case StarSpectralClass.ClassM: return "Красный карлик (M)";
                case StarSpectralClass.ClassK: return "Оранжевая (K)";
                case StarSpectralClass.ClassG: return "Жёлтая (G)";
                case StarSpectralClass.ClassA: return "Белая (A)";
                case StarSpectralClass.ClassB: return "Голубой гигант (B)";
                case StarSpectralClass.BlackHole: return "Чёрная дыра";
            }
            return c.ToString();
        }

        // ---------- Соперник ----------

        private void BuildRival()
        {
            if (AIEmpireManager.All.Count == 0) { Empty("Соперники пока не обнаружены."); return; }
            foreach (var ai in AIEmpireManager.All) BuildRivalCard(ai);
        }

        private void BuildRivalCard(AIEmpireManager ai)
        {
            Color ac = ai.MapColor;
            var card = LGBuild.Panel(_list, "RivalCard", UIManager.DS.BgSlot);
            LGBuild.Height(card.gameObject, 300);
            LG.Platter(card.gameObject, 18f).SetRim(new Color(ac.r, ac.g, ac.b, 0.4f));

            // Живой портрет правителя слева (как в дипломатии); без портрета — эмблема
            var leader = LeaderPortraits.ForFaction(ai.Faction);
            float x0 = 20f;
            if (leader != null && leader.Texture != null)
            {
                const float PortW = 128f, PortH = 214f;
                var frame = LGBuild.Panel(card.transform, "Portrait", new Color(0.01f, 0.02f, 0.03f, 1f));
                frame.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -20), new Vector2(PortW, PortH));
                var ffx = LG.Platter(frame.gameObject, 12f);
                ffx.SetRim(new Color(ac.r, ac.g, ac.b, 0.6f));
                ffx.FillMultiplier = 3f;
                var portHost = LG.RoundedMask(frame.transform, 12f, 1.5f);
                var portrait = LeaderPortraitView.Create(portHost, leader, zoom: 1.35f);
                if (portrait != null && ai.AtWar) portrait.GetComponent<RawImage>().color = new Color(1f, 0.86f, 0.84f, 1f);

                var nameShade = LGBuild.Panel(portHost, "Shade", new Color(0.01f, 0.02f, 0.03f, 1f));
                nameShade.sprite = MenuArt.VerticalFade;
                nameShade.rectTransform.anchorMin = Vector2.zero;
                nameShade.rectTransform.anchorMax = new Vector2(1, 0.35f);
                nameShade.rectTransform.offsetMin = nameShade.rectTransform.offsetMax = Vector2.zero;
                var ln = LGBuild.Label(portHost, leader.Name, 11, Color.Lerp(ac, Color.white, 0.5f), TextAnchor.LowerCenter, bold: true);
                ln.rectTransform.Stretch(4, 8, 4, 0);
                TooltipHelper.Attach(frame.gameObject, $"<b>{leader.Name}</b>\n{leader.Title}\n<i>«{leader.Quote}»</i>");
                x0 = 20f + PortW + 18f;
            }
            else
            {
                var emblem = LGBuild.Panel(card.transform, "Emblem", new Color(ac.r * 0.4f, ac.g * 0.4f, ac.b * 0.4f));
                emblem.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -20), new Vector2(64, 64));
                LG.Platter(emblem.gameObject, 32f).FillMultiplier = 2.4f;
                LGIcons.Create(emblem.transform, LGIcon.Leader, 34, Color.Lerp(ac, Color.white, 0.3f));
            }

            var head = TopRow(card.transform, 20, 64, x0 > 20f ? x0 : 100, 20);
            LGBuild.Label(head, ai.AIName.ToUpper(), 20, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            float rel = ai.Opinion;
            string status = ai.IsEliminated ? "<color=#F2C747>ПОВЕРЖЕН</color>"
                          : ai.AtWar ? "<color=#FF5A5A>ВОЙНА</color>"
                          : ai.HasPact ? "<color=#4DF2DB>Пакт о ненападении</color>"
                          : ai.TruceDays > 0 ? "<color=#5CF59A>Перемирие</color>"
                          : "<color=#8AA2A8>Мир</color>";
            if (AICoalition.IsMember(ai.OwnerId)) status += "   ·   <color=#FF9A5A>в коалиции</color>";
            var sub = LGBuild.Label(head, $"{ai.AITitle}   ·   {ai.Profile.Name.ToLower()} империя   ·   {status}   ·   {AIEmpireManager.OpinionLabel(rel).ToLower()}",
                          12, UIManager.DS.TextMuted, TextAnchor.LowerLeft);
            if (!string.IsNullOrEmpty(ai.TacticsNote))
            {
                sub.raycastTarget = true;
                TooltipHelper.Attach(sub.gameObject, ai.TacticsNote);
            }

            int myPow = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(0) : 0;
            int aiPow = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(ai.OwnerId) : 0;
            int mySys = 0, aiSys = 0;
            if (_gen != null)
                foreach (var s in _gen.Systems)
                {
                    if (s.OwnerId == 0) mySys++;
                    else if (s.OwnerId == ai.OwnerId) aiSys++;
                }

            float relN = Mathf.InverseLerp(AIEmpireManager.OpinionMin, AIEmpireManager.OpinionMax, rel);
            CompareRow(card.transform, 104, "Отношения", relN, Color.Lerp(UIManager.DS.Red, UIManager.DS.Green, relN), $"{rel:+0;-0;0}", x0);
            CompareRow(card.transform, 150, "Военная мощь: вы / они", myPow + aiPow > 0 ? myPow / (float)(myPow + aiPow) : 0.5f,
                UIManager.DS.NeonCyan, $"{myPow:N0} / {aiPow:N0}", x0);
            CompareRow(card.transform, 196, "Системы: вы / они", mySys + aiSys > 0 ? mySys / (float)(mySys + aiSys) : 0.5f,
                CGold, $"{mySys} / {aiSys}", x0);

            var btnRow = TopRow(card.transform, 246, 38, 20, 20);
            var dip = LGBuild.Button(btnRow, "Diplomacy", new Color(0.42f, 0.32f, 0.10f), CGold,
                () => { Close(); DiplomacyModal.Instance?.Open(ai.OwnerId); }, LGIcon.Diplomacy, "ДИПЛОМАТИЯ", 11);
            ((RectTransform)dip.transform).Column(0f, 0.49f);
            var trade = LGBuild.Button(btnRow, "Trade", UIManager.DS.BtnPrimary, UIManager.DS.NeonCyan,
                () => { Close(); TradeModal.Instance?.Open(ai.OwnerId); }, LGIcon.Trade, "ТОРГОВЛЯ", 11);
            ((RectTransform)trade.transform).Column(0.51f, 1f);
        }

        private static void CompareRow(Transform parent, float top, string label, float value, Color col, string valueText, float left = 20f)
        {
            var row = TopRow(parent, top, 38, left, 20);
            LGBuild.Label(row, label, 11, UIManager.DS.TextMuted, TextAnchor.UpperLeft, bold: true);
            LGBuild.Label(row, valueText, 12, UIManager.DS.TextPrimary, TextAnchor.UpperRight, bold: true);
            var host = LGBuild.Rect(row, "BarHost");
            host.anchorMin = new Vector2(0, 0);
            host.anchorMax = new Vector2(1, 0);
            host.pivot = new Vector2(0.5f, 0);
            host.offsetMin = new Vector2(0, 2);
            host.offsetMax = new Vector2(0, 12);
            LGBuild.Bar(host, col, value, 8f);
        }

        // ================================================================ Навигация

        private void FocusSystem(StarSystem sys)
        {
            if (sys == null) return;
            Close();
            var cam = FindAnyObjectByType<StrategyCameraController>();
            if (cam != null) cam.FocusOn(sys.Position);
            GalaxyView.Instance?.SelectSystem(sys.Id);
            UIManager.Instance?.ShowSystemPanel(sys);
        }

        private void FocusFleet(FleetView fv)
        {
            if (fv == null) return;
            Close();
            var cam = FindAnyObjectByType<StrategyCameraController>();
            if (cam != null) cam.FocusOn(fv.transform.position);
            FleetManager.Instance?.SelectFleet(fv);
        }
    }
}
