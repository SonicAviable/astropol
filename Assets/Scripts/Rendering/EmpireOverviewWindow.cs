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
    ///   • справа — вкладки «Системы / Флоты / Лидеры / Узлы / Соперники / Эффекты» с кликабельными строками
    ///     (клик — камера к объекту, окно закрывается; клик по колонии в системе — экран колонии).
    /// </summary>
    public partial class EmpireOverviewWindow : MonoBehaviour
    {
        public static EmpireOverviewWindow Instance { get; private set; }

        private enum Tab { Systems, Fleets, Leaders, Landmarks, Rival, Effects }

        private GameObject _root;
        private Text _title, _subtitle;
        private Image _emblem, _emblemIcon;
        private readonly Text[] _resLabel = new Text[5];
        private readonly Text[] _resValue = new Text[5];
        private readonly Text[] _resIncome = new Text[5];
        private Text _leaderName, _leaderTitle, _leaderPower;
        private TooltipTrigger _portraitTip, _lawsTip;
        private RectTransform _attention;
        private string _attentionKey = "";
        private int _rowIndex;
        private const float LeftW = 400f;          // ширина правой колонки (цифры, внимание, победа)
        private const float BannerH = 212f;
        // Строки — полупрозрачные светлые слои поверх панели (а не плотный тёмный цвет: панели шейдер подсвечивает,
        // и непрозрачные строки на их фоне выглядели чёрными провалами)
        private static readonly Color RowA = new Color(0.60f, 0.85f, 0.95f, 0.06f);
        private static readonly Color RowB = new Color(0.60f, 0.85f, 0.95f, 0.045f);
        private static readonly Color RowSub = new Color(0.60f, 0.85f, 0.95f, 0.015f);
        private static readonly Color RowHover = new Color(0.55f, 0.92f, 1f, 0.13f);
        private readonly Dictionary<Tab, string> _tabNames = new Dictionary<Tab, string>();
        private Image _portrait, _portraitIcon;
        private RectTransform _portraitHost;
        private string _portraitKey;
        private readonly Dictionary<string, Text> _stats = new Dictionary<string, Text>();
        private RectTransform _domBar, _sciBar, _survBar;
        private Text _domText, _sciText, _survText, _warnText;
        private readonly Dictionary<Tab, (Image bg, Image line, Text label, Image icon)> _tabs =
            new Dictionary<Tab, (Image, Image, Text, Image)>();
        private RectTransform _tableHeader;
        private RectTransform _list;
        private Text _emptyText;
        private Tab _tab = Tab.Systems;
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
            if (Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Close(); return; }

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 1f;
                RefreshHeader();
                RefreshResources();
                RefreshLeftColumn();
                RefreshTabs();
            }
        }

        // ================================================================ Построение

        private void BuildWindow(Transform host)
        {
            var rootRt = LGBuild.Rect(host, "EmpireOverviewWindow");
            rootRt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1520, 900));
            _root = rootRt.gameObject;
            var bg = _root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(WindowRim);
            LG.Motion(_root, LGAppear.Kind.Pop).inDuration = 0.45f;

            // «Штаб»: шапка с правителем и гербом, полоса ресурсов, слева вкладки с таблицей,
            // справа — империя в цифрах, требует внимания и путь к победе
            BuildBanner(rootRt);
            BuildResourceRow(rootRt);

            var body = LGBuild.Rect(rootRt, "Body");
            body.Stretch(24, 22, 24, BannerH + 88);

            var side = LGBuild.Rect(body, "Side");
            side.anchorMin = new Vector2(1, 0);
            side.anchorMax = new Vector2(1, 1);
            side.pivot = new Vector2(1, 1);
            side.offsetMin = new Vector2(-LeftW, 0);
            side.offsetMax = Vector2.zero;
            BuildStats(side);
            BuildAttention(side);
            BuildVictory(side);

            var main = LGBuild.Rect(body, "Main");
            main.Stretch(0, 0, LeftW + 18, 0);
            BuildTabs(main);

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
        {
            var t = LGBuild.Label(TopRow(parent, top, 18, 16, 16), text, 10, col, TextAnchor.MiddleLeft, bold: true);
            t.Track(1.4f);
            return t;
        }

        /// <summary>Блок левой колонки / вкладок: ровная пластина.</summary>
        private static Image Block(Transform parent, string name)
        {
            var b = LGBuild.Panel(parent, name, UIManager.DS.BgSlot);
            LG.Platter(b.gameObject, 10f);
            return b;
        }

        /// <summary>Прозрачная кликабельная область с подсветкой при наведении (без «кнопочного» вида).</summary>
        private static Button HoverArea(Image img, System.Action act, Color hover)
        {
            LG.Ignore(img.gameObject, includeChildren: false);
            Color stored = default;
            var hv = img.gameObject.AddComponent<UIHover>();
            hv.Enter = () => { stored = img.color; img.color = hover; };
            hv.Exit = () => { if (stored != default) img.color = stored; };
            if (act == null) return null;
            var b = img.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => act());
            return b;
        }

        /// <summary>
        /// Шапка «штаба»: слева живой портрет правителя, растворяющийся вправо; герб; крупно империя,
        /// строй, правитель и дата; кнопка «Законы империи» и мощь флота; справа — закрыть.
        /// </summary>
        private void BuildBanner(RectTransform root)
        {
            var banner = LGBuild.Panel(root, "Banner", UIManager.DS.BgHeader);
            banner.rectTransform.TopBand(0, BannerH);
            LG.Header(banner.gameObject);

            // Портрет (кадр по пояс) — фон левой части шапки
            _portrait = LGBuild.Panel(banner.transform, "Portrait", new Color(0.2f, 0.4f, 0.5f));
            var prt = _portrait.rectTransform;
            prt.anchorMin = new Vector2(0, 0);
            prt.anchorMax = new Vector2(0, 1);
            prt.pivot = new Vector2(0, 0.5f);
            prt.offsetMin = new Vector2(0, 0);
            prt.offsetMax = new Vector2(560, 0);
            LG.Ignore(_portrait.gameObject);
            _portraitIcon = LGIcons.Create(_portrait.transform, LGIcon.Leader, 64, Color.white);
            _portraitHost = LG.RoundedMask(_portrait.transform, 18f, 0f);
            TooltipHelper.Attach(_portrait.gameObject, "");
            _portraitTip = _portrait.GetComponent<TooltipTrigger>();
            var fade = LGBuild.Panel(banner.transform, "Fade", UIManager.DS.BgHeader);
            fade.sprite = MenuArt.HorizontalFade;
            fade.rectTransform.anchorMin = new Vector2(0, 0);
            fade.rectTransform.anchorMax = new Vector2(0, 1);
            fade.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            fade.rectTransform.offsetMin = new Vector2(260, 0);
            fade.rectTransform.offsetMax = new Vector2(562, 0);
            fade.rectTransform.localRotation = Quaternion.Euler(0, 0, 180f);   // прозрачно у лица, плотно к правому краю портрета
            fade.raycastTarget = false;
            LG.Ignore(fade.gameObject);
            var floor = LGBuild.Panel(banner.transform, "Floor", UIManager.DS.BgHeader);
            floor.sprite = MenuArt.VerticalFade;
            floor.rectTransform.Stretch();
            floor.raycastTarget = false;
            LG.Ignore(floor.gameObject);

            // Герб
            _emblem = LGBuild.Panel(banner.transform, "Emblem", new Color(0.2f, 0.5f, 0.6f));
            _emblem.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(470, 14), new Vector2(104, 104));
            LG.Ignore(_emblem.gameObject);
            _emblemIcon = LGIcons.Create(_emblem.transform, LGIcon.Leader, 92, Color.white);

            // Империя
            var box = LGBuild.Rect(banner.transform, "TitleBox");
            box.Stretch(548, 0, 90, 0);
            var cap = LGBuild.Label(box, Loc.T("ОБЗОР ИМПЕРИИ"), 12, UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true);
            cap.rectTransform.TopBand(40, 18);
            cap.Track(2.5f);
            _title = LGBuild.Label(box, Loc.T("ИМПЕРИЯ"), 40, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            _title.rectTransform.TopBand(60, 52);
            _title.resizeTextForBestFit = true; _title.resizeTextMinSize = 24; _title.resizeTextMaxSize = 40;
            _subtitle = LGBuild.Label(box, "", 15, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.TopBand(114, 22);
            _leaderTitle = LGBuild.Label(box, "", 12, new Color(0.55f, 0.65f, 0.70f), TextAnchor.UpperLeft);
            _leaderTitle.rectTransform.TopBand(136, 18);
            _leaderName = LGBuild.Label(box, "", 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            _leaderName.gameObject.SetActive(false);   // имя правителя — в строке под названием

            var laws = LGBuild.Button(box, "Laws", UIManager.DS.BtnNeutral, new Color(CGold.r, CGold.g, CGold.b, 0.55f),
                () => { Close(); LawsWindow.Toggle(); }, null, Loc.T("ЗАКОНЫ ИМПЕРИИ"), 12);
            ((RectTransform)laws.transform).At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -160), new Vector2(220, 40));
            TooltipHelper.Attach(laws.gameObject, "");
            _lawsTip = laws.GetComponent<TooltipTrigger>();

            var powerRow = LGBuild.Rect(box, "Power");
            powerRow.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(240, -170), new Vector2(300, 20));
            var pic = LGIcons.Create(powerRow, LGIcon.Fleet, 16, CGold);
            pic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16));
            _leaderPower = LGBuild.Label(powerRow, "", 14, CGold, TextAnchor.MiddleLeft, bold: true);
            _leaderPower.rectTransform.offsetMin = new Vector2(24, 0);

            var close = LGControls.IconButton(banner.transform, LGIcon.Close, 42, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.3f), Close);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -22), new Vector2(42, 42));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc / I)</color>"));
        }

        /// <summary>Ресурсы — одна тонкая полоса: запас и доход за месяц (подробности — в подсказках верхней панели).</summary>
        private void BuildResourceRow(RectTransform root)
        {
            var stripRt = LGBuild.Rect(root, "Resources");
            stripRt.TopBand(BannerH + 12, 62, 24, 24);
            var strip = stripRt;

            var defs = new (LGIcon icon, string label, Color col)[]
            {
                (LGIcon.Energy, Loc.T("ГЕЛИЙ-3"), CEnergy),
                (LGIcon.Minerals, Loc.T("ТИТАН"), CMinerals),
                (LGIcon.Alloys, Loc.T("СПЛАВЫ"), CAlloys),
                (LGIcon.Influence, Loc.T("ВЛИЯНИЕ"), CInfluence),
                (LGIcon.Research, Loc.T("НАУКА"), CResearch),
            };

            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                // Каждый ресурс — своя плитка
                var tile = Block(strip, "Res_" + i);
                tile.rectTransform.Column(i / 5f, (i + 1) / 5f, i == 0 ? 0 : 6, i == 4 ? 0 : 6);
                var cell = LGBuild.Rect(tile.transform, "Cell");
                cell.Stretch(14, 0, 12, 0);
                var ic = LGIcons.Create(cell, d.icon, 22, d.col);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(22, 22));
                _resLabel[i] = LGBuild.Label(cell, d.label, 9, UIManager.DS.TextMuted, TextAnchor.UpperLeft, bold: true);
                _resLabel[i].rectTransform.Stretch(34, 0, 0, 8);
                _resValue[i] = LGBuild.Label(cell, "0", 17, UIManager.DS.TextPrimary, TextAnchor.LowerLeft, bold: true);
                _resValue[i].rectTransform.Stretch(34, 7, 0, 0);
                _resIncome[i] = LGBuild.Label(cell, "", 12, UIManager.DS.TextMuted, TextAnchor.LowerRight, bold: true);
                _resIncome[i].rectTransform.Stretch(34, 8, 0, 0);

            }
        }

        /// <summary>«Империя в цифрах»: строки-ссылки — клик ведёт туда, где с этим работают.</summary>
        private void BuildStats(RectTransform left)
        {
            var block = Block(left, "Stats");
            block.rectTransform.TopBand(0, 150);
            Caption(block.transform, 10, Loc.T("ИМПЕРИЯ В ЦИФРАХ"), UIManager.DS.TextMuted);

            AddStatRow(block.transform, 0, 0, "systems", LGIcon.Star, Loc.T("Системы"), UIManager.DS.NeonCyan, () => SwitchTab(Tab.Systems));
            AddStatRow(block.transform, 1, 0, "colonies", LGIcon.Planet, Loc.T("Колонии"), UIManager.DS.Green, OpenColonies);
            AddStatRow(block.transform, 0, 1, "pop", LGIcon.Population, Loc.T("Население"), new Color(0.75f, 0.9f, 1f), OpenColonies);
            AddStatRow(block.transform, 1, 1, "housing", LGIcon.Housing, Loc.T("Свободное жильё"), new Color(0.7f, 0.85f, 0.95f), OpenColonies);
            AddStatRow(block.transform, 0, 2, "idle", LGIcon.Warning, Loc.T("Безработные"), CGold, OpenColonies);
            AddStatRow(block.transform, 1, 2, "districts", LGIcon.Industry, Loc.T("Районы"), CAlloys, OpenColonies);
            AddStatRow(block.transform, 0, 3, "outposts", LGIcon.Starbase, Loc.T("Форпосты"), CMinerals, () => SwitchTab(Tab.Systems));
            AddStatRow(block.transform, 1, 3, "fleets", LGIcon.Fleet, Loc.T("Флоты"), UIManager.DS.Red, () => SwitchTab(Tab.Fleets));
        }

        private void AddStatRow(Transform block, int col, int row, string key, LGIcon icon, string label, Color c, System.Action act)
        {
            var r = LGBuild.Panel(block, "Stat_" + key, new Color(1f, 1f, 1f, 0f), raycast: true);
            var rt = r.rectTransform;
            rt.anchorMin = new Vector2(col * 0.5f, 1); rt.anchorMax = new Vector2(col * 0.5f + 0.5f, 1);
            rt.pivot = new Vector2(0.5f, 1);
            float top = 32f + row * 28f;
            rt.offsetMin = new Vector2(col == 0 ? 10 : 4, -top - 26f);
            rt.offsetMax = new Vector2(col == 0 ? -4 : -10, -top);
            HoverArea(r, act, new Color(1f, 1f, 1f, 0.06f));
            var ic = LGIcons.Create(rt, icon, 14, c);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(14, 14));
            var l = LGBuild.Label(rt, label, 11, UIManager.DS.TextMuted, TextAnchor.MiddleLeft);
            l.rectTransform.Stretch(28, 0, 44, 0);
            var v = LGBuild.Label(rt, "0", 13, UIManager.DS.TextPrimary, TextAnchor.MiddleRight, bold: true);
            v.rectTransform.Stretch(0, 0, 8, 0);
            _stats[key] = v;
        }

        /// <summary>«Требует внимания»: те же оповещения, что над картой (и скрытые там), — клик сразу к делу.</summary>
        private void BuildAttention(RectTransform left)
        {
            var block = Block(left, "Attention");
            block.rectTransform.TopBand(162, 210);
            block.GetComponent<LiquidGlassEffect>()?.SetRim(new Color(CGold.r, CGold.g, CGold.b, 0.20f));
            var row = TopRow(block.transform, 10, 18, 16, 16);
            var ic = LGIcons.Create(row, LGIcon.Warning, 13, CGold);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(13, 13));
            var t = LGBuild.Label(row, Loc.T("ТРЕБУЕТ ВНИМАНИЯ"), 10, CGold, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.offsetMin = new Vector2(20, 0);
            t.Track(1.4f);
            _attention = LGBuild.Rect(block.transform, "List");
            _attention.Stretch(10, 10, 10, 36);
        }

        private const int AttentionMax = 5;

        private void RefreshAttention()
        {
            if (_attention == null) return;
            var list = AlertBar.Instance != null ? AlertBar.Instance.All : null;
            var sb = new System.Text.StringBuilder();
            if (list != null) foreach (var a in list) sb.Append(a.Id).Append(a.Count).Append(a.Title).Append('|');
            string key = sb.ToString();
            if (key == _attentionKey) return;
            _attentionKey = key;
            LGBuild.Clear(_attention);

            if (list == null || list.Count == 0)
            {
                var ok = LGIcons.Create(_attention, LGIcon.Check, 16, UIManager.DS.Green);
                ok.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -8), new Vector2(16, 16));
                var t = LGBuild.Label(_attention, Loc.T("Всё спокойно: срочных дел нет."), 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
                t.rectTransform.Stretch(32, 0, 6, 8);
                return;
            }

            float y = 0f;
            for (int i = 0; i < list.Count && i < AttentionMax; i++)
            {
                var a = list[i];
                var row = LGBuild.Panel(_attention, "Alert", new Color(a.Color.r, a.Color.g, a.Color.b, 0.08f), raycast: true);
                row.rectTransform.TopBand(y, 30);
                y += 33f;
                var act = a.Action;
                HoverArea(row, act == null ? null : (System.Action)(() => { Close(); act(); }),
                    new Color(a.Color.r, a.Color.g, a.Color.b, 0.18f));
                var bar = LGBuild.Panel(row.transform, "Accent", a.Color);
                bar.raycastTarget = false;
                bar.rectTransform.anchorMin = new Vector2(0, 0); bar.rectTransform.anchorMax = new Vector2(0, 1);
                bar.rectTransform.offsetMin = new Vector2(0, 4); bar.rectTransform.offsetMax = new Vector2(3, -4);
                LG.Ignore(bar.gameObject);
                var ic = LGIcons.Create(row.transform, a.Icon, 14, a.Color);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(14, 14));
                string title = a.Count > 1 ? $"{a.Title}  <color=#8AA2A8>×{a.Count}</color>" : a.Title;
                var tl = LGBuild.Label(row.transform, title, 11, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, bold: true);
                tl.rectTransform.Stretch(34, 0, 24, 0);
                if (act != null)
                {
                    var chev = LGIcons.Create(row.transform, LGIcon.Back, 10, UIManager.DS.TextMuted);
                    chev.rectTransform.At(new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-12, 0), new Vector2(10, 10));
                    chev.rectTransform.localRotation = Quaternion.Euler(0, 0, 180f);
                }
                string tip = $"<b>{a.Title}</b>\n{a.Body}";
                if (!string.IsNullOrEmpty(a.ActionHint)) tip += $"\n<color=#8AA2A8>{a.ActionHint}</color>";
                TooltipHelper.Attach(row.gameObject, tip);
            }
            if (list.Count > AttentionMax)
            {
                var more = LGBuild.Label(_attention, Loc.F("и ещё {0} — значки над картой", list.Count - AttentionMax), 10, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
                more.rectTransform.TopBand(y + 2f, 14, 6, 6);
            }
        }

        /// <summary>Путь к победе — коротко; подробно — окно «Путь к победе» (V).</summary>
        private void BuildVictory(RectTransform left)
        {
            var block = Block(left, "Victory");
            var rt = block.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = Vector2.zero; rt.offsetMax = new Vector2(0, 132);
            block.GetComponent<LiquidGlassEffect>()?.SetRim(new Color(CGold.r, CGold.g, CGold.b, 0.22f));

            var titleRow = TopRow(block.transform, 8, 22, 16, 12);
            var tIcon = LGIcons.Create(titleRow, LGIcon.Trophy, 13, CGold);
            tIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(13, 13));
            var tl = LGBuild.Label(titleRow, Loc.T("ПУТЬ К ПОБЕДЕ"), 10, CGold, TextAnchor.MiddleLeft, bold: true);
            tl.rectTransform.offsetMin = new Vector2(20, 0);
            tl.Track(1.4f);
            var more = LGControls.IconButton(titleRow, LGIcon.Back, 24, UIManager.DS.BgDeep, new Color(CGold.r, CGold.g, CGold.b, 0.45f),
                () => { Close(); VictoryPathWindow.Toggle(); }, 180f);
            ((RectTransform)more.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(24, 24));
            TooltipHelper.Attach(more.gameObject, Loc.T("Подробнее о путях к победе  <color=#8AA2A8>(V)</color>"));

            _domText = VictoryRow(block.transform, 36, LGIcon.Starbase, Loc.T("Доминирование"), UIManager.DS.NeonCyan, out _domBar);
            _sciText = VictoryRow(block.transform, 62, LGIcon.Research, Loc.T("Наука"), CResearch, out _sciBar);
            _survText = VictoryRow(block.transform, 88, LGIcon.Trophy, Loc.F("Очки · {0}", VictoryManager.EndYear), CEnergy, out _survBar);

            _warnText = LGBuild.Label(block.transform, "", 9, UIManager.DS.TextMuted, TextAnchor.LowerLeft, wrap: true);
            _warnText.rectTransform.Stretch(16, 6, 16, 0);
        }

        private static Text VictoryRow(Transform parent, float top, LGIcon icon, string name, Color col, out RectTransform bar)
        {
            var row = TopRow(parent, top, 24, 16, 16);
            var ic = LGIcons.Create(row, icon, 12, col);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -1), new Vector2(12, 12));
            var n = LGBuild.Label(row, name, 11, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            n.rectTransform.offsetMin = new Vector2(20, 0);
            n.rectTransform.offsetMax = new Vector2(-120, 0);
            var val = LGBuild.Label(row, "0 / 0", 11, col, TextAnchor.UpperRight, bold: true);
            val.rectTransform.offsetMin = new Vector2(160, 0);

            var barHost = LGBuild.Rect(row, "BarHost");
            barHost.anchorMin = new Vector2(0, 0);
            barHost.anchorMax = new Vector2(1, 0);
            barHost.pivot = new Vector2(0.5f, 0);
            barHost.offsetMin = new Vector2(20, 0);
            barHost.offsetMax = new Vector2(0, 6);
            bar = LGBuild.Bar(barHost, col, 0f, 4f);
            return val;
        }

        /// <summary>Вкладки — одна полоса: значок, название и число записей; активная подчёркнута.</summary>
        private void BuildTabs(RectTransform right)
        {
            var strip = Block(right, "Tabs");
            strip.rectTransform.TopBand(0, 50);
            AddTab(strip.rectTransform, Tab.Systems, LGIcon.Star, Loc.T("СИСТЕМЫ"), 0);
            AddTab(strip.rectTransform, Tab.Fleets, LGIcon.Fleet, Loc.T("ФЛОТЫ"), 1);
            AddTab(strip.rectTransform, Tab.Leaders, LGIcon.Leader, Loc.T("ЛИДЕРЫ"), 2);
            AddTab(strip.rectTransform, Tab.Landmarks, LGIcon.Target, Loc.T("УЗЛЫ"), 3);
            AddTab(strip.rectTransform, Tab.Rival, LGIcon.Diplomacy, Loc.T("СОПЕРНИКИ"), 4);
            AddTab(strip.rectTransform, Tab.Effects, LGIcon.Clock, Loc.T("ЭФФЕКТЫ"), 5);

            var table = Block(right, "Table");
            table.color = UIManager.DS.BgVisor;
            table.rectTransform.Stretch(0, 0, 0, 62);

            _tableHeader = LGBuild.Rect(table.transform, "TableHeader");
            _tableHeader.TopBand(8, 26, 16, 16);
            var hl = LGBuild.Panel(table.transform, "HeadLine", new Color(1f, 1f, 1f, 0.08f));
            hl.rectTransform.TopBand(36, 1, 12, 12);
            LG.Ignore(hl.gameObject);

            var listHost = LGBuild.Rect(table.transform, "ListHost");
            listHost.Stretch(8, 8, 8, 42);
            _list = LGBuild.ScrollList(listHost, 6f, 4);

            _emptyText = LGBuild.Label(table.transform, "", 13, UIManager.DS.TextMuted, TextAnchor.MiddleCenter, wrap: true);
            _emptyText.rectTransform.Stretch(80, 40, 80, 40);
        }

        private void AddTab(RectTransform strip, Tab tab, LGIcon icon, string label, int index)
        {
            const int TabCount = 6;
            var hit = LGBuild.Panel(strip, "Tab_" + tab, new Color(1f, 1f, 1f, 0f), raycast: true);
            hit.rectTransform.Column(index / (float)TabCount, (index + 1) / (float)TabCount, index == 0 ? 6 : 2, index == TabCount - 1 ? 6 : 2);
            HoverArea(hit, () => SwitchTab(tab), new Color(1f, 1f, 1f, 0.05f));
            // Активная вкладка — подсвеченная плашка (как сегментный переключатель в меню)
            var line = LGBuild.Panel(hit.transform, "Plate", new Color(0.17f, 0.38f, 0.40f, 0.95f));
            line.raycastTarget = false;
            line.rectTransform.Stretch(2, 5, 2, 5);
            var pfx = LG.Chip(line.gameObject, new Color(0.44f, 0.90f, 0.85f, 0.75f), 9f);
            pfx.FillMultiplier = 1.8f;
            var t = LGIcons.IconLabel(hit.transform, icon, label, 12, UIManager.DS.TextMuted, UIManager.DS.TextMuted, 14f);
            var iconImg = t.transform.parent.GetChild(0).GetComponent<Image>();
            _tabs[tab] = (hit, line, t, iconImg);
            _tabNames[tab] = label;
        }

        private void SwitchTab(Tab tab)
        {
            _tab = tab;
            _assignLeaderId = -1;
            RefreshTabs();
            RebuildList();
            SFXManager.Play(Sfx.UiTab);
        }

        private void OpenColonies()
        {
            Close();
            ColonyManagerWindow.Toggle();
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
            _title.text = f?.Name ?? Loc.T("Империя");
            string date = TimeManager.Instance != null ? TimeManager.Instance.GetFormattedDate() : "";
            var ruler = LeaderPortraits.ForFaction(f);
            _subtitle.text = ruler != null
                ? Loc.F("<color={0}>{1}</color>   ·   правитель {2}   ·   {3}", LGBuild.Hex(fc), f?.Title ?? Loc.T("Правительство"), ruler.Name, date)
                : Loc.F("<color={0}>{1}</color>   ·   {2}", LGBuild.Hex(fc), f?.Title ?? Loc.T("Правительство"), date);
            _emblemIcon.color = Color.Lerp(fc, Color.white, 0.35f);
            // Герб фракции вместо общего значка правителя — крупно, без подложки
            var crest = FactionEmblems.ForFaction(f);
            _emblem.color = crest != null ? Color.clear : new Color(fc.r * 0.45f, fc.g * 0.45f, fc.b * 0.45f, 1f);
            if (crest != null && _emblemIcon.sprite != crest)
            {
                _emblemIcon.sprite = crest;
                _emblemIcon.preserveAspect = true;
                _emblemIcon.rectTransform.sizeDelta = new Vector2(100, 100);
            }
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
                _resLabel[0].text = eco.IsBankrupt ? Loc.T("ГЕЛИЙ-3  <color=#FF5A5A>· БАНКРОТ</color>") : Loc.T("ГЕЛИЙ-3");
            }
            if (tm != null)
            {
                int active = 0;
                foreach (var s in tm.Slots) if (s.CurrentTech != null && !s.IsPaused) active++;
                _resLabel[4].text = Loc.F("НАУКА  <color=#8AA2A8>· слоты {0}/{1}</color>", active, tm.Slots.Count);
                _resValue[4].text = $"{tm.MonthlyResearchIncome:0.#}";
                _resIncome[4].text = Loc.T("<color=#8AA2A8>/ мес</color>");
            }
        }

        private void SetRes(int i, float stock, float income)
        {
            _resValue[i].text = ((int)stock).ToString("N0");
            _resIncome[i].text = Loc.F("{0} <color=#8AA2A8>/ мес</color>", LGBuild.Signed(income));
        }

        private void RefreshLeftColumn()
        {
            var f = Faction;
            Color fc = f != null ? f.EmpireColor : UIManager.DS.NeonCyan;
            var leader = LeaderPortraits.ForFaction(f);
            _leaderName.text = leader != null ? leader.Name : f?.Name ?? Loc.T("Империя");
            _leaderTitle.text = leader != null ? leader.Title : f?.Title ?? "";
            string key = leader?.Key ?? "";
            if (key != _portraitKey)
            {
                _portraitKey = key;
                LGBuild.Clear(_portraitHost);
                // Без «сбоев сигнала»: в широкой шапке они режут портрет на полосы
                bool ok = LeaderPortraitView.Create(_portraitHost, leader, 1.25f, Vector4.zero, false, true, false) != null;
                _portraitIcon.gameObject.SetActive(!ok);
            }
            _portraitTip?.SetText(f != null ? $"<b>{f.Name}</b>\n{f.Description}" : "");
            _lawsTip?.SetText(LawsSummary());
            _portrait.color = new Color(fc.r * 0.15f, fc.g * 0.15f, fc.b * 0.18f, 1f);
            _portraitIcon.color = Color.Lerp(fc, Color.white, 0.3f);
            int power = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(0) : 0;
            _leaderPower.text = Loc.F("Мощь флота: {0:N0}", power);
            RefreshAttention();

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
                _domText.text = Loc.F("{0} / {1} систем", dom, vm.DominationRequired);
                _sciText.text = Loc.F("{0} / {1} техн.", sci, vm.ScienceRequired);
                _survText.text = me >= rival ? $"{me} : {rival}" : $"<color=#FF6A6A>{me} : {rival}</color>";
                LGBuild.SetBar(_domBar, dom / (float)Mathf.Max(1, vm.DominationRequired));
                LGBuild.SetBar(_sciBar, sci / (float)Mathf.Max(1, vm.ScienceRequired));
                LGBuild.SetBar(_survBar, me / (float)Mathf.Max(1, me + rival));
                _warnText.text = vm.BankruptMonths > 0
                    ? Loc.F("<color=#FF6A6A>Банкротство: {0} из {1} мес. до краха экономики</color>", vm.BankruptMonths, vm.BankruptcyLimit)
                    : Loc.F("Соперник: {0} систем, {1} техн. Подсчёт очков через {2} {3}.", vm.RivalDominationProgress, vm.RivalScienceProgress, vm.YearsLeft, VictoryManager.YearsWord(vm.YearsLeft));
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
                var v = kv.Value;
                v.line.enabled = active;
                v.label.color = active ? Color.white : UIManager.DS.TextMuted;
                if (v.icon != null) v.icon.color = active ? UIManager.DS.NeonCyan : UIManager.DS.TextMuted;
                int n = TabCount(kv.Key);
                string name = _tabNames.TryGetValue(kv.Key, out var nm) ? nm : "";
                v.label.text = n > 0 ? $"{name}  <color=#{(active ? "4DF2DB" : "6F8A92")}>{n}</color>" : name;
            }
        }

        /// <summary>Сколько записей во вкладке (число рядом с названием).</summary>
        private int TabCount(Tab tab)
        {
            int n = 0;
            switch (tab)
            {
                case Tab.Systems:
                    if (_gen != null) foreach (var s in _gen.Systems) if (s.OwnerId == 0) n++;
                    break;
                case Tab.Fleets:
                    if (FleetManager.Instance != null)
                        foreach (var fv in FleetManager.Instance.AllFleets)
                            if (fv != null && fv.Data != null && !fv.Data.Destroyed && fv.Data.OwnerId == 0) n++;
                    break;
                case Tab.Leaders:
                    if (LeaderManager.Instance != null) n = LeaderManager.Instance.All.Count;
                    break;
                case Tab.Landmarks:
                    foreach (var s in Landmarks.All) if (Vision.PlayerKnows(s.Id) || s.OwnerId == 0) n++;
                    break;
                case Tab.Rival:
                    foreach (var ai in AIEmpireManager.All) if (Contacts.PlayerMet(ai.OwnerId)) n++;
                    break;
                case Tab.Effects:
                    n = EmpireEffects.Active.Count;
                    foreach (var c in EmpireLaws.Categories) { var l = EmpireLaws.Active(c); if (l != null && !l.Default) n++; }
                    break;
            }
            return n;
        }

        /// <summary>Подсказка кнопки «Законы»: действующие законы по направлениям.</summary>
        private static string LawsSummary()
        {
            var sb = new System.Text.StringBuilder(Loc.T("<b>Законы империи</b>  <color=#8AA2A8>(L)</color>\n"));
            foreach (var c in EmpireLaws.Categories)
            {
                var l = EmpireLaws.Active(c);
                if (l == null) continue;
                sb.Append("<color=#8AA2A8>").Append(EmpireLaws.CategoryName(c)).Append(":</color> ").Append(l.Name);
                if (l.Upkeep > 0.01f) sb.Append(Loc.F("  <color=#C9A6FF>−{0:0.#} влияния/мес</color>", l.Upkeep));
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        // ================================================================ Списки

        private void RebuildList()
        {
            LGBuild.Clear(_list);
            LGBuild.Clear(_tableHeader);
            _emptyText.text = "";
            _rowIndex = 0;
            _list.anchoredPosition = Vector2.zero;
            if (_gen == null) _gen = FindAnyObjectByType<GalaxyGenerator>();

            switch (_tab)
            {
                case Tab.Systems: BuildSystems(); break;
                case Tab.Fleets: BuildFleets(); break;
                case Tab.Leaders: BuildLeaders(); break;
                case Tab.Landmarks: BuildLandmarks(); break;
                case Tab.Rival: BuildRival(); break;
                case Tab.Effects: BuildEffects(); break;
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

        /// <summary>Строка таблицы: ровная, через одну чуть темнее, слева — полоска цвета записи, подсветка при наведении.</summary>
        private RectTransform Row(System.Action onClick, Color rim)
        {
            var img = LGBuild.Panel(_list, "Row", (_rowIndex++ & 1) == 0 ? RowA : RowB, raycast: true);
            LGBuild.Height(img.gameObject, 46);
            HoverArea(img, onClick, RowHover);
            var accent = LGBuild.Panel(img.transform, "Accent", new Color(rim.r, rim.g, rim.b, Mathf.Clamp01(rim.a * 2.4f)));
            accent.raycastTarget = false;
            accent.rectTransform.anchorMin = new Vector2(0, 0); accent.rectTransform.anchorMax = new Vector2(0, 1);
            accent.rectTransform.offsetMin = new Vector2(0, 6); accent.rectTransform.offsetMax = new Vector2(3, -6);
            LG.Ignore(accent.gameObject);
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

        // ---------- Системы и их колонии ----------

        /// <summary>
        /// Все системы империи: столица первой, затем по числу жителей. Под каждой системой — её колонии
        /// (клик по колонии — экран колонии, по системе — камера к ней).
        /// </summary>
        private void BuildSystems()
        {
            HeaderCols((0.06f, 0.30f, Loc.T("СИСТЕМА"), TextAnchor.MiddleLeft),
                       (0.30f, 0.52f, Loc.T("ЗВЁЗДНАЯ БАЗА"), TextAnchor.MiddleLeft),
                       (0.52f, 0.66f, Loc.T("ЖИТЕЛИ"), TextAnchor.MiddleLeft),
                       (0.66f, 0.74f, Loc.T("РАЙОНЫ"), TextAnchor.MiddleCenter),
                       (0.74f, 1f, Loc.T("ПРОИЗВОДСТВО / МЕС"), TextAnchor.MiddleRight));
            if (_gen == null) { Empty(Loc.T("У империи не осталось систем.")); return; }

            var systems = new List<StarSystem>();
            foreach (var sys in _gen.Systems) if (sys.OwnerId == 0) systems.Add(sys);
            int Pop(StarSystem sy) { int n = 0; foreach (var p in sy.Planets) n += Mathf.Max(0, p.Population); return n; }
            systems.Sort((a, b) =>
            {
                bool ca = a.Id == EconomyManager.PlayerCapitalId, cb = b.Id == EconomyManager.PlayerCapitalId;
                if (ca != cb) return ca ? -1 : 1;
                int c = Pop(b).CompareTo(Pop(a));
                return c != 0 ? c : string.Compare(a.Name, b.Name, System.StringComparison.CurrentCulture);
            });

            foreach (var sys in systems)
            {
                bool capital = sys.Id == EconomyManager.PlayerCapitalId;
                var colonies = new List<PlanetData>();
                foreach (var p in sys.Planets) if (p.Population > 0) colonies.Add(p);

                float mi = 0f, en = 0f, al = 0f;
                foreach (var p in sys.Planets)
                {
                    if (p.Population > 0)
                    {
                        mi += ColonyManagerWindow.Output(p, "minerals");
                        en += ColonyManagerWindow.Output(p, "energy");
                        al += ColonyManagerWindow.Output(p, "alloys");
                    }
                    else if (p.HasMiningStation) { mi += p.MineralDeposit; en += p.EnergyDeposit; }
                }

                var captured = sys;
                var row = Row(() => FocusSystem(captured), new Color(0.45f, 0.95f, 0.9f, capital ? 0.4f : 0.2f));
                IconCell(row, 0f, 0.06f, capital ? LGIcon.Shipyard : LGIcon.Starbase, capital ? CGold : CMinerals);
                string kind = capital ? Loc.T("Столица · орбитальная верфь")
                            : colonies.Count > 0 ? Loc.F("Колоний: {0}", colonies.Count)
                            : sys.HasStarbase ? Loc.T("Пограничный форпост") : Loc.T("Контролируемая система");
                if (sys.Landmark >= 0) kind += " · ★ " + Landmarks.Name(sys.Landmark);
                kind += " · " + StarClass(sys.SpectralClass);
                TwoLine(row, 0.06f, 0.30f, sys.Name, kind);
                BaseCell(row, 0.30f, 0.52f, sys);
                int pop = Pop(sys);
                TextCell(row, 0.52f, 0.66f, pop > 0 ? Loc.F("{0} жит.", pop) : Loc.F("планет: {0}", sys.Planets.Count),
                    pop > 0 ? UIManager.DS.TextPrimary : UIManager.DS.TextMuted, TextAnchor.MiddleLeft, 12, pop > 0);
                if (sys.IsSurveyed)
                    MiniStats(row, 0.74f, 1f,
                        (LGIcon.Minerals, CMinerals, "+" + mi.ToString("0.#")),
                        (LGIcon.Energy, CEnergy, "+" + en.ToString("0.#")),
                        (LGIcon.Alloys, CAlloys, "+" + al.ToString("0.#")));
                else
                    TextCell(row, 0.74f, 1f, Loc.T("не изучено"), UIManager.DS.TextMuted, TextAnchor.MiddleRight, 11);

                // Колонии системы — строками с отступом
                foreach (var p in colonies)
                {
                    var planet = p;
                    var sub = Row(() => OpenColony(planet), new Color(p.PlanetColor.r, p.PlanetColor.g, p.PlanetColor.b, 0.25f));
                    LGBuild.Height(sub.gameObject, 40);
                    sub.GetComponent<Image>().color = RowSub;
                    IconCell(sub, 0.04f, 0.09f, LGIcon.Planet, Color.Lerp(p.PlanetColor, Color.white, 0.25f));
                    var tier = p.Tier;
                    TwoLine(sub, 0.09f, 0.30f, PlanetNames.Title(p.Name),
                        p.ClassDisplayName + Loc.F(" · стабильность <color=#{0}>{1:0}</color>", PlanetStability.Hex(PlanetStability.TierColor(tier)), p.Stability));
                    TextCell(sub, 0.30f, 0.52f, Loc.T("колония"), UIManager.DS.TextMuted, TextAnchor.MiddleLeft, 11);
                    float fillPop = p.HousingCapacity > 0 ? p.Population / (float)p.HousingCapacity : 1f;
                    Color popCol = p.Population < p.HousingCapacity ? UIManager.DS.Green : CGold;
                    BarCell(sub, 0.52f, 0.66f, fillPop, popCol,
                        $"{p.Population} / {p.HousingCapacity}" + (p.IdlePops > 0 ? Loc.F("   <color=#F2C747>безраб. {0}</color>", p.IdlePops) : ""));
                    TextCell(sub, 0.66f, 0.74f, $"{p.BuiltDistricts} / {p.MaxDistricts}", UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, 12, true);
                    MiniStats(sub, 0.74f, 1f,
                        (LGIcon.Minerals, CMinerals, "+" + ColonyManagerWindow.Output(p, "minerals").ToString("0.#")),
                        (LGIcon.Energy, CEnergy, "+" + ColonyManagerWindow.Output(p, "energy").ToString("0.#")),
                        (LGIcon.Alloys, CAlloys, "+" + ColonyManagerWindow.Output(p, "alloys").ToString("0.#")));
                }
            }
            if (systems.Count == 0) Empty(Loc.T("У империи не осталось систем."));
        }

        /// <summary>
        /// Звёздная база в списке систем: уровень (ромбы), что строится и кнопка «Улучшить» прямо в строке —
        /// не нужно искать систему на карте.
        /// </summary>
        private void BaseCell(RectTransform row, float x0, float x1, StarSystem sys)
        {
            var c = Cell(row, x0, x1);
            if (!sys.HasStarbase)
            {
                LGBuild.Label(c, Loc.T("нет базы"), 11, UIManager.DS.TextMuted, TextAnchor.MiddleLeft);
                return;
            }
            int lv = sys.Fortification;
            var pips = new System.Text.StringBuilder();
            for (int i = 1; i <= Fortifications.MaxLevel; i++) pips.Append(i <= lv ? "◆" : "◇");
            var t1 = LGBuild.Label(c, $"{Fortifications.Name(lv)}  <color=#F2C747>{pips}</color>", 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            t1.supportRichText = true;
            t1.rectTransform.offsetMax = new Vector2(-40, -6);

            var job = Fortifications.PendingJob(sys.Id);
            string blocker = Fortifications.Blocker(sys, 0);
            int to = lv + 1;
            string sub = job != null ? Loc.F("строится: {0}, {1:0} дн.", Fortifications.Name(to).ToLower(), Mathf.Ceil(job.DaysLeft))
                       : lv >= Fortifications.MaxLevel ? Loc.T("наивысший уровень")
                       : Loc.F("улучшение: {0:0} мин. · {1:0} спл.", Fortifications.Minerals(to), Fortifications.Alloys(to));
            var t2 = LGBuild.Label(c, sub, 10, job != null ? CGold : UIManager.DS.TextMuted, TextAnchor.LowerLeft);
            t2.rectTransform.offsetMin = new Vector2(0, 6);
            t2.rectTransform.offsetMax = new Vector2(-40, 0);
            if (job != null || lv >= Fortifications.MaxLevel) return;

            var eco = EconomyManager.Instance;
            bool afford = eco != null && eco.CanAfford(0f, Fortifications.Minerals(to), Fortifications.Alloys(to), 0f);
            bool can = blocker == null && afford;
            var captured = sys;
            var btn = LGBuild.Button(c, "Upgrade", can ? UIManager.DS.BtnSuccess : UIManager.DS.BtnDisabled, new Color(1f, 1f, 1f, 0.25f), () =>
            {
                if (Fortifications.TryStartPlayer(captured))
                {
                    SFXManager.Play(StellarisClone.Core.Audio.Sfx.BuildQueued);
                    UIManager.Instance?.RefreshResourceBarPublic();
                    RebuildList();
                }
                else SFXManager.Play(StellarisClone.Core.Audio.Sfx.UiDenied);
            }, LGIcon.Construction, null, 11);
            ((RectTransform)btn.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(34, 30));
            btn.interactable = can;
            string tip = Loc.F("<b>Улучшить до «{0}»</b>\n{1:0} минералов, {2:0} сплавов, {3:0} дней.\nБаза крепче и бьёт сильнее, осада идёт дольше; содержание +{4:0.#} гелия-3 в месяц",
                Fortifications.Name(to), Fortifications.Minerals(to), Fortifications.Alloys(to), Fortifications.Days(to), Fortifications.UpkeepPerLevel);
            if (blocker != null) tip += $"\n<color=#FF8080>{blocker}</color>";
            else if (!afford) tip += "\n<color=#FF8080>" + Loc.T("Не хватает ресурсов") + "</color>";
            TooltipHelper.Attach(btn.gameObject, tip);
        }

        private void OpenColony(PlanetData p)
        {
            if (p == null) return;
            Close();
            PlanetFocusOverlay.Instance?.Open(p, p.ParentSystem);
        }

        // ---------- Флоты ----------

        private void BuildFleets()
        {
            HeaderCols((0.06f, 0.32f, Loc.T("ФЛОТ"), TextAnchor.MiddleLeft),
                       (0.32f, 0.52f, Loc.T("ЗАДАЧА"), TextAnchor.MiddleLeft),
                       (0.52f, 0.68f, Loc.T("РАСПОЛОЖЕНИЕ"), TextAnchor.MiddleLeft),
                       (0.68f, 0.86f, Loc.T("ПРОЧНОСТЬ"), TextAnchor.MiddleLeft),
                       (0.86f, 1f, Loc.T("МОЩЬ"), TextAnchor.MiddleRight));
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
                                : d.Type == FleetType.Science ? LGIcon.ScienceShip : LGIcon.ConstructorShip;
                    string type = d.Type == FleetType.Military ? Loc.T("Боевой флот")
                                : d.Type == FleetType.Science ? Loc.T("Научный корабль") : Loc.T("Строительный корабль");
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
            if (n == 0) Empty(Loc.T("Флотов нет. Постройте корабли на орбитальной верфи столицы."));
        }

        private string FleetTask(FleetData d)
        {
            switch (d.State)
            {
                case FleetState.InHyperlane: return Loc.T("Перелёт → ") + SystemName(d.TargetSystemId);
                case FleetState.Surveying: return Loc.F("Разведка ({0:0} дн.)", Mathf.Max(0, d.DaysRemainingSurvey));
                case FleetState.Constructing: return Loc.F("Строительство ({0:0} дн.)", Mathf.Max(0, d.DaysRemainingConstruction));
            }
            if (d.InCombat) return Loc.T("<color=#FF6060>В бою</color>");
            var fm = FleetManager.Instance;
            if (fm != null && fm.NeedsRepair(d))
            {
                float rate = fm.RepairRateAt(d.CurrentSystemId, d.OwnerId);
                return rate > 0f ? Loc.F("<color=#5CF59A>Ремонт · {0:0}% в день</color>", rate * 100f)
                                 : Loc.T("<color=#FFAA55>Повреждён — ремонт только на своей территории</color>");
            }
            var siege = SiegeManager.Instance?.GetSiege(d.CurrentSystemId);
            if (siege != null && siege.Attacker == d.OwnerId && d.Type == FleetType.Military)
                return Loc.F("<color=#5CF59A>Осада · {0:0} дн.</color>", siege.Progress);
            return Loc.T("На орбите — ждёт приказа");
        }

        private string SystemName(int id)
        {
            if (_gen == null || id < 0 || id >= _gen.Systems.Count) return "—";
            return _gen.Systems[id].Name;
        }

        // ---------- Границы ----------

        // ---------- Узлы силы ----------

        private void BuildLandmarks()
        {
            HeaderCols((0.06f, 0.30f, Loc.T("УЗЕЛ"), TextAnchor.MiddleLeft),
                       (0.30f, 0.46f, Loc.T("СИСТЕМА"), TextAnchor.MiddleLeft),
                       (0.46f, 0.80f, Loc.T("БОНУС ПРИ КОНТРОЛЕ"), TextAnchor.MiddleLeft),
                       (0.80f, 1f, Loc.T("ВЛАДЕЛЕЦ"), TextAnchor.MiddleRight));
            int total = 0, known = 0, mine = 0;
            foreach (var sys in Landmarks.All) total++;
            foreach (var sys in Landmarks.All)
            {
                if (!(Vision.PlayerKnows(sys.Id) || sys.OwnerId == 0)) continue;
                known++;
                if (Landmarks.Controls(0, sys)) mine++;
                Color lc = Landmarks.Color(sys.Landmark);
                var captured = sys;
                var row = Row(() => FocusSystem(captured), new Color(lc.r, lc.g, lc.b, 0.32f));
                IconCell(row, 0f, 0.06f, SystemNameplate.LandmarkIcon(sys.Landmark), lc);
                TwoLine(row, 0.06f, 0.30f, Landmarks.Name(sys.Landmark), Loc.T("Узел силы"));
                TextCell(row, 0.30f, 0.46f, sys.Name, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, 12, true);
                TextCell(row, 0.46f, 0.80f, Landmarks.Effect(sys.Landmark), UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, 11);
                bool held = sys.HasStarbase && sys.OwnerId >= 0;
                string who = !held ? Loc.T("свободен") : sys.OwnerId == 0 ? Loc.T("вы") : AIEmpireManager.NameOf(sys.OwnerId, Loc.T("империя"));
                Color wc = !held ? UIManager.DS.TextMuted : sys.OwnerId == 0 ? UIManager.DS.Green : FleetIndicator.OwnerColor(sys.OwnerId);
                TextCell(row, 0.80f, 1f, who, wc, TextAnchor.MiddleRight, 12, true);
            }
            if (total == 0) { Empty(Loc.T("На этой карте нет узлов силы.")); return; }
            if (known == 0)
            {
                Empty(Loc.F("Узлы силы пока не найдены — на карте их {0}.\n\nУзел виден, когда его система разведана. Постройте в ней форпост, и бонус станет вашим.", total));
                return;
            }
            SectionRow(Loc.F("НАЙДЕНО {0} ИЗ {1}  ·  ПОД ВАШИМ КОНТРОЛЕМ {2}  ·  КЛИК — ПЕРЕЙТИ К СИСТЕМЕ", known, total, mine), UIManager.DS.TextMuted);
        }

        internal static string StarClass(StarSpectralClass c)
        {
            switch (c)
            {
                case StarSpectralClass.ClassM: return Loc.T("Красный карлик (M)");
                case StarSpectralClass.ClassK: return Loc.T("Оранжевая (K)");
                case StarSpectralClass.ClassG: return Loc.T("Жёлтая (G)");
                case StarSpectralClass.ClassA: return Loc.T("Белая (A)");
                case StarSpectralClass.ClassB: return Loc.T("Голубой гигант (B)");
                case StarSpectralClass.BlackHole: return Loc.T("Чёрная дыра");
            }
            return c.ToString();
        }

        // ---------- Эффекты и угрозы ----------

        private void BuildEffects()
        {
            HeaderCols((0.06f, 0.42f, Loc.T("ЭФФЕКТ"), TextAnchor.MiddleLeft),
                       (0.42f, 0.84f, Loc.T("ДЕЙСТВИЕ"), TextAnchor.MiddleLeft),
                       (0.84f, 1f, Loc.T("ОСТАЛОСЬ"), TextAnchor.MiddleRight));
            int n = 0;
            // Действующие законы (не исходные) — клик открывает окно законов
            foreach (var c in EmpireLaws.Categories)
            {
                var law = EmpireLaws.Active(c);
                if (law == null || law.Default) continue;
                n++;
                var lrow = Row(() => { Close(); LawsWindow.Toggle(); }, new Color(CAlloys.r, CAlloys.g, CAlloys.b, 0.35f));
                IconCell(lrow, 0f, 0.06f, LGIcon.Menu, CAlloys);
                TwoLine(lrow, 0.06f, 0.42f, law.Name, Loc.F("Закон · {0}", EmpireLaws.CategoryName(c).ToLower()));
                var fx = new System.Text.StringBuilder();
                foreach (var (text, good) in law.Effects)
                {
                    if (fx.Length > 0) fx.Append("   ");
                    fx.Append(good ? "<color=#66F29E>" : "<color=#FF7A73>").Append(text).Append("</color>");
                }
                TextCell(lrow, 0.42f, 0.84f, fx.ToString(), UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, 11);
                TextCell(lrow, 0.84f, 1f, law.Upkeep > 0.01f ? Loc.F("−{0:0.#} влияния/мес", law.Upkeep) : Loc.T("пока действует"),
                    UIManager.DS.TextPrimary, TextAnchor.MiddleRight, 12, true);
            }
            foreach (var e in EmpireEffects.Active)
            {
                n++;
                var row = Row(null, new Color(0.95f, 0.78f, 0.35f, 0.3f));
                IconCell(row, 0f, 0.06f, LGIcon.Clock, CGold);
                TwoLine(row, 0.06f, 0.42f, e.Title, e.Detail);
                TextCell(row, 0.42f, 0.84f, EmpireEffects.Summary(e), UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, 11);
                TextCell(row, 0.84f, 1f, Loc.F("{0} мес.", e.MonthsLeft), UIManager.DS.TextPrimary, TextAnchor.MiddleRight, 13, true);
            }

            // Хроника: главы цепочек событий, которые ещё впереди (срок — примерный, суть — туманная подсказка)
            var aes = AnomalyEventSystem.Instance;
            if (aes != null)
                foreach (var pc in aes.PendingChains)
                {
                    n++;
                    var row = Row(null, new Color(0.85f, 0.65f, 0.95f, 0.3f));
                    IconCell(row, 0f, 0.06f, LGIcon.Research, new Color(0.85f, 0.65f, 0.95f));
                    TwoLine(row, 0.06f, 0.42f, pc.Chain, Loc.T("цепочка событий · продолжение следует"));
                    var hint = TextCell(row, 0.42f, 0.84f, $"<i>{pc.Hint}</i>", UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, 11);
                    hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                    hint.lineSpacing = 0.95f;
                    TextCell(row, 0.84f, 1f, ChainEta(pc.Days), UIManager.DS.TextPrimary, TextAnchor.MiddleRight, 13, true);
                }

            var tm = ThreatManager.Instance;
            if (tm != null && tm.RaidsSoFar > 0)
            {
                Color pc = Threats.ColorOf(Threats.PirateOwner);
                void Threat(string title, string sub, string what, string left)
                {
                    n++;
                    var row = Row(null, new Color(pc.r, pc.g, pc.b, 0.3f));
                    IconCell(row, 0f, 0.06f, LGIcon.Warning, pc);
                    TwoLine(row, 0.06f, 0.42f, title, sub);
                    TextCell(row, 0.42f, 0.84f, what, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, 11);
                    TextCell(row, 0.84f, 1f, left, UIManager.DS.TextPrimary, TextAnchor.MiddleRight, 13, true);
                }
                Threat(Loc.T("Пиратская угроза"), Loc.F("налётов: {0}", tm.RaidsSoFar), Loc.T("Банды грабят слабо защищённые пограничные колонии"), "");
                if (tm.TributeActive) Threat(Loc.T("Дань пиратам"), Loc.T("пираты"), Loc.T("Пираты не нападают на ваши системы"), Loc.F("{0} мес.", tm.TributeMonths));
                if (tm.BountyActive) Threat(Loc.T("Награда за пиратов"), Loc.T("охота"), Loc.T("+40 энергии и +4 влияния за каждый уничтоженный корабль"), Loc.F("{0} мес.", tm.BountyMonths));
                if (tm.PrivateerRaids > 0) Threat(Loc.T("Каперские грамоты"), Loc.T("пираты"), Loc.T("Налёты направлены на соперников"), Loc.F("{0} нал.", tm.PrivateerRaids));
            }

            if (n == 0)
                Empty(Loc.T("Действующих эффектов нет.\n\nСвой курс задают законы империи (клавиша L); указы и контракты появляются из решений фракции и событий."));
        }

        /// <summary>Примерный срок до главы цепочки: точная дата — спойлер, поэтому округляем.</summary>
        private static string ChainEta(int days)
        {
            if (days <= 45) return Loc.T("скоро");
            if (days < 300) return Loc.F("≈ {0} мес.", Mathf.Max(1, Mathf.RoundToInt(days / 30f)));
            float years = days / 360f;
            return years < 1.25f ? Loc.T("≈ год") : Loc.F("≈ {0:0.#} г.", years);
        }

        // ---------- Соперник ----------

        private void BuildRival()
        {
            int met = 0;
            foreach (var ai in AIEmpireManager.All)
                if (Contacts.PlayerMet(ai.OwnerId)) { BuildRivalCard(ai); met++; }
            int unknown = Contacts.UnmetByPlayer;
            if (met == 0)
                Empty(Loc.T("Других цивилизаций мы пока не встречали.\n\nОтправляйте научные корабли в неизведанные системы: ") +
                      Loc.T("контакт случится, как только наши корабли или границы увидят чужую империю.") +
                      (unknown > 0 ? Loc.F("\n\nПо данным дальней разведки в галактике есть ещё цивилизаций: {0}.", unknown) : ""));
            else if (unknown > 0)
            {
                // Под карточками, а не по центру таблицы: центральная надпись ложилась поверх кнопок первой карточки
                var note = LGBuild.Rect(_list, "UnmetNote");
                LGBuild.Height(note.gameObject, 36);
                var t = LGBuild.Label(note, Loc.F("Ещё не встречено цивилизаций: {0}. Продолжайте разведку.", unknown), 13,
                    UIManager.DS.TextMuted, TextAnchor.MiddleCenter, wrap: true);
                t.rectTransform.Stretch(10, 0, 10, 0);
            }
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
            string status = ai.IsEliminated ? Loc.T("<color=#F2C747>ПОВЕРЖЕН</color>")
                          : ai.AtWar ? Loc.T("<color=#FF5A5A>ВОЙНА</color>")
                          : ai.HasPact ? Loc.T("<color=#4DF2DB>Пакт о ненападении</color>")
                          : ai.TruceDays > 0 ? Loc.T("<color=#5CF59A>Перемирие</color>")
                          : Loc.T("<color=#8AA2A8>Мир</color>");
            if (AICoalition.IsMember(ai.OwnerId)) status += Loc.T("   ·   <color=#FF9A5A>в коалиции</color>");
            var sub = LGBuild.Label(head, Loc.F("{0}   ·   {1} империя   ·   {2}   ·   {3}", ai.AITitle, ai.Profile.Name.ToLower(), status, AIEmpireManager.OpinionLabel(rel).ToLower()),
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
            CompareRow(card.transform, 104, Loc.T("Отношения"), relN, Color.Lerp(UIManager.DS.Red, UIManager.DS.Green, relN), $"{rel:+0;-0;0}", x0);
            CompareRow(card.transform, 150, Loc.T("Военная мощь: вы / они"), myPow + aiPow > 0 ? myPow / (float)(myPow + aiPow) : 0.5f,
                UIManager.DS.NeonCyan, $"{myPow:N0} / {aiPow:N0}", x0);
            CompareRow(card.transform, 196, Loc.T("Системы: вы / они"), mySys + aiSys > 0 ? mySys / (float)(mySys + aiSys) : 0.5f,
                CGold, $"{mySys} / {aiSys}", x0);

            var btnRow = TopRow(card.transform, 246, 38, 20, 20);
            var dip = LGBuild.Button(btnRow, "Diplomacy", new Color(0.42f, 0.32f, 0.10f), CGold,
                () => { Close(); DiplomacyModal.Instance?.Open(ai.OwnerId); }, LGIcon.Diplomacy, Loc.T("ДИПЛОМАТИЯ"), 11);
            ((RectTransform)dip.transform).Column(0f, 0.49f);
            var trade = LGBuild.Button(btnRow, "Trade", UIManager.DS.BtnPrimary, UIManager.DS.NeonCyan,
                () => { Close(); TradeModal.Instance?.Open(ai.OwnerId); }, LGIcon.Trade, Loc.T("ТОРГОВЛЯ"), 11);
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
            UIManager.Instance?.FocusSystem(sys);
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
