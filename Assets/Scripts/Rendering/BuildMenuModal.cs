using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Меню строительства планеты (как в Master of Orion, но в своём стиле):
    ///   • слева — вкладки категорий и сетка карточек: значок, название, эффект, срок и цена;
    ///     недоступное — приглушено, с замком и причиной; уже заказанное — со счётчиком «в очереди»;
    ///   • справа — подробности выбранного: что даст, цена по ресурсам (красным — чего не хватает),
    ///     срок с учётом губернатора, кнопки «Построить» и «Построить и купить»;
    ///   • под ней — очередь планеты: «Купить» за гелий-3 у текущей стройки, «первым», «отменить».
    /// Открывается из окна планеты и из «Управления колониями».
    /// </summary>
    public class BuildMenuModal : MonoBehaviour
    {
        public static BuildMenuModal Instance { get; private set; }
        public static bool IsOpenNow => Instance != null && Instance._open;

        private const float W = 1320f, H = 760f, HeaderH = 78f, RightW = 430f, Pad = 22f;
        private const float CardW = 196f, CardH = 150f;

        private GameObject _root;
        private bool _open, _ownsDim;
        private PlanetData _planet;
        private BuildCategory? _filter;
        private string _selectedId;
        private float _refresh;
        private bool _dirty;

        private Text _title, _subtitle;
        private RectTransform _grid, _cost, _queueBody;
        private readonly List<Tab> _tabs = new List<Tab>();
        private readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();
        private List<BuildOption> _options = new List<BuildOption>();
        private string _optionKey = "", _queueKey = "", _detailCostKey = "";
        private Text[] _stock;

        // Детали
        private Image _dIconBg, _dIcon;
        private Text _dTitle, _dEffect, _dText, _dTime, _dBlock;
        private Button _buildBtn, _buildBuyBtn;
        private Image _buildBg, _buildBuyBg;
        private Text _buildBuyText;
        private GameObject _detailEmpty, _detailBody;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static readonly Color CPanel = new Color(0.020f, 0.045f, 0.065f, 0.90f);
        public static readonly Color CMinerals = new Color(0.20f, 0.90f, 0.80f);
        public static readonly Color CEnergy = new Color(0.95f, 0.78f, 0.28f);
        public static readonly Color CAlloys = new Color(0.95f, 0.62f, 0.36f);
        public static readonly Color CInfluence = new Color(0.78f, 0.60f, 1f);

        private class Tab
        {
            public BuildCategory? Cat;
            public Image Bg;
            public Text Label;
        }

        private class Card
        {
            public BuildOption Opt;
            public GameObject Go;
            public Image Bg, IconBg, Icon, Lock, Sel;
            public Text Title, Effect, Days, Queued;
            public RectTransform Cost;
            public string CostKey;
        }

        // ================================================================ Открытие

        public static void Open(PlanetData planet)
        {
            if (planet == null) return;
            if (Instance == null) new GameObject("[UI] BuildMenuModal").AddComponent<BuildMenuModal>();
            Instance.Show(planet);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            ConstructionManager.OnQueuesChanged += MarkDirty;
        }

        private void OnDestroy()
        {
            ConstructionManager.OnQueuesChanged -= MarkDirty;
            if (Instance == this) Instance = null;
        }

        private void MarkDirty() => _dirty = true;

        private void Show(PlanetData planet)
        {
            if (_root == null) Build();
            if (_root == null) return;
            if (_planet != planet) { _selectedId = null; }
            _planet = planet;
            _open = true;
            Rebuild(true);
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            // Своё затемнение — только если под нами нет другого окна, которое его уже держит
            _ownsDim = !(PlanetFocusOverlay.Instance != null && PlanetFocusOverlay.Instance.IsOpen) && !ColonyManagerWindow.IsOpenNow;
            if (_ownsDim) UIManager.Instance?.ShowModalDimPublic();
            else SFXManager.Play(Sfx.WindowOpen);
            LG.Skin(_root.transform);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            LG.Hide(_root);
            TooltipSystem.Hide();
            if (_ownsDim) UIManager.Instance?.HideModalDimPublic();
            else SFXManager.Play(Sfx.WindowClose);
        }

        private void Update()
        {
            if (!_open) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Close(); return; }
            if (_planet == null || !_planet.IsPlayerOwned) { Close(); return; }   // систему потеряли — показывать нечего
            _refresh -= Time.unscaledDeltaTime;
            if (_dirty || _refresh <= 0f)
            {
                _refresh = 0.5f;
                _dirty = false;
                Rebuild(false);
            }
        }

        // ================================================================ Каркас

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            var rt = LGBuild.Rect(host.transform, "BuildMenu");
            rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.010f, 0.025f, 0.038f, 0.98f);
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.42f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.96f;

            // Шапка
            var accent = LGBuild.Panel(rt, "Accent", new Color(0.95f, 0.42f, 0.42f));
            accent.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -24), new Vector2(5, 30));
            _title = LGBuild.Label(rt, "", 24, Color.white, TextAnchor.UpperLeft, bold: true);
            _title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad + 16, -18), new Vector2(760, 34));
            _title.Track(1.4f);
            _subtitle = LGBuild.Label(rt, "", 12, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad + 17, -52), new Vector2(760, 18));

            // Запасы империи справа в шапке
            var stockRow = LGBuild.Rect(rt, "Stock");
            stockRow.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-70, -24), new Vector2(470, 30));
            _stock = new Text[4];
            LGIcon[] icons = { LGIcon.Energy, LGIcon.Minerals, LGIcon.Alloys, LGIcon.Influence };
            Color[] cols = { CEnergy, CMinerals, CAlloys, CInfluence };
            string[] tips = { Loc.T("Гелий-3: им же оплачивается «Купить»"), Loc.T("Титан"), Loc.T("Сплавы"), Loc.T("Влияние") };
            for (int i = 0; i < 4; i++)
            {
                var chip = LGBuild.Panel(stockRow, "Chip" + i, new Color(0.02f, 0.06f, 0.08f, 0.9f), raycast: true);
                chip.rectTransform.anchorMin = new Vector2(i / 4f, 0); chip.rectTransform.anchorMax = new Vector2((i + 1) / 4f, 1);
                chip.rectTransform.offsetMin = new Vector2(3, 0); chip.rectTransform.offsetMax = new Vector2(-3, 0);
                LG.Chip(chip.gameObject, new Color(cols[i].r, cols[i].g, cols[i].b, 0.45f));
                var ic = LGIcons.Create(chip.transform, icons[i], 14, cols[i]);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(9, 0), new Vector2(14, 14));
                _stock[i] = LGBuild.Label(chip.transform, "", 12, Color.white, TextAnchor.MiddleLeft, bold: true);
                _stock[i].rectTransform.Stretch(28, 0, 4, 0);
                TooltipHelper.Attach(chip.gameObject, tips[i]);
            }

            var close = LGBuild.Button(rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -20), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc)</color>"));

            // Левая часть: вкладки и сетка
            var left = LGBuild.Panel(rt, "Catalog", CPanel);
            left.rectTransform.anchorMin = new Vector2(0, 0); left.rectTransform.anchorMax = new Vector2(1, 1);
            left.rectTransform.offsetMin = new Vector2(Pad, Pad); left.rectTransform.offsetMax = new Vector2(-Pad - RightW - 14f, -HeaderH - 10f);
            LG.Platter(left.gameObject, 16f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.18f));

            var tabs = LGBuild.Rect(left.rectTransform, "Tabs");
            tabs.TopBand(12, 32, 14, 14);
            var cats = new BuildCategory?[] { null, BuildCategory.District, BuildCategory.Project, BuildCategory.Ship, BuildCategory.Society };
            for (int i = 0; i < cats.Length; i++)
            {
                var cat = cats[i];
                var b = LGBuild.Button(tabs, "Tab", UIManager.DS.BtnNeutral, new Color(Cyan.r, Cyan.g, Cyan.b, 0.4f), () => SetFilter(cat), null, null, 11, 10f);
                var brt = (RectTransform)b.transform;
                brt.anchorMin = new Vector2(i / (float)cats.Length, 0); brt.anchorMax = new Vector2((i + 1) / (float)cats.Length, 1);
                brt.offsetMin = new Vector2(3, 0); brt.offsetMax = new Vector2(-3, 0);
                var ic = LGIcons.Create(b.transform, cat.HasValue ? BuildCatalog.CategoryIcon(cat.Value) : LGIcon.Menu, 13, Color.white);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(13, 13));
                var t = LGBuild.Label(b.transform, (cat.HasValue ? BuildCatalog.CategoryName(cat.Value) : Loc.T("Все")).ToUpper(), 11, Color.white, TextAnchor.MiddleCenter, bold: true);
                t.rectTransform.Stretch(22, 0, 4, 0);
                _tabs.Add(new Tab { Cat = cat, Bg = b.GetComponent<Image>(), Label = t });
            }

            var gridHost = LGBuild.Rect(left.rectTransform, "GridHost");
            gridHost.Stretch(10, 10, 10, 54);
            gridHost.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 12);
            var sr = gridHost.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true; sr.scrollSensitivity = 36f;
            sr.movementType = ScrollRect.MovementType.Clamped;
            var hit = gridHost.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            _grid = LGBuild.Rect(gridHost, "Grid");
            _grid.anchorMin = new Vector2(0, 1); _grid.anchorMax = new Vector2(1, 1); _grid.pivot = new Vector2(0.5f, 1);
            _grid.sizeDelta = Vector2.zero;
            var gl = _grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(CardW, CardH);
            gl.spacing = new Vector2(10, 10);
            gl.padding = new RectOffset(4, 4, 4, 4);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 4;
            _grid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = gridHost;
            sr.content = _grid;

            BuildDetail(rt);
            _root.SetActive(false);
        }

        private void BuildDetail(RectTransform rt)
        {
            var right = LGBuild.Panel(rt, "Detail", CPanel);
            right.rectTransform.anchorMin = new Vector2(1, 0); right.rectTransform.anchorMax = new Vector2(1, 1);
            right.rectTransform.pivot = new Vector2(1, 0.5f);
            right.rectTransform.offsetMin = new Vector2(-Pad - RightW, Pad); right.rectTransform.offsetMax = new Vector2(-Pad, -HeaderH - 10f);
            LG.Platter(right.gameObject, 16f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.18f));
            var r = right.rectTransform;

            // Подробности выбранного (верхние ~58%)
            var body = LGBuild.Rect(r, "Body");
            body.anchorMin = new Vector2(0, 0.42f); body.anchorMax = new Vector2(1, 1);
            body.offsetMin = new Vector2(18, 0); body.offsetMax = new Vector2(-18, -16);
            _detailBody = body.gameObject;

            _dIconBg = LGBuild.Panel(body, "IconBg", new Color(0.05f, 0.1f, 0.12f));
            _dIconBg.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(64, 64));
            LG.Platter(_dIconBg.gameObject, 18f);
            _dIcon = LGIcons.Create(_dIconBg.transform, LGIcon.Construction, 34, Color.white);
            _dTitle = LGBuild.Label(body, "", 18, Color.white, TextAnchor.UpperLeft, bold: true, wrap: true);
            _dTitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(78, -2), new Vector2(316, 44));
            _dEffect = LGBuild.Label(body, "", 12, Cyan, TextAnchor.UpperLeft, bold: true, wrap: true);
            _dEffect.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(78, -46), new Vector2(316, 20));
            _dText = LGBuild.Label(body, "", 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            _dText.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -78), new Vector2(394, 132));
            _dText.lineSpacing = 1.15f;

            _cost = LGBuild.Rect(body, "Cost");
            _cost.anchorMin = new Vector2(0, 0); _cost.anchorMax = new Vector2(1, 0); _cost.pivot = new Vector2(0, 0);
            _cost.offsetMin = new Vector2(0, 92); _cost.offsetMax = new Vector2(0, 116);
            _dTime = LGBuild.Label(body, "", 12, Muted, TextAnchor.MiddleLeft, bold: true);
            _dTime.rectTransform.anchorMin = new Vector2(0, 0); _dTime.rectTransform.anchorMax = new Vector2(1, 0);
            _dTime.rectTransform.offsetMin = new Vector2(0, 70); _dTime.rectTransform.offsetMax = new Vector2(0, 90);
            _dBlock = LGBuild.Label(body, "", 11, new Color(1f, 0.55f, 0.55f), TextAnchor.MiddleLeft, wrap: true);
            _dBlock.rectTransform.anchorMin = new Vector2(0, 0); _dBlock.rectTransform.anchorMax = new Vector2(1, 0);
            _dBlock.rectTransform.offsetMin = new Vector2(0, 48); _dBlock.rectTransform.offsetMax = new Vector2(0, 70);

            _buildBtn = LGBuild.Button(body, "Build", UIManager.DS.BtnSuccess, UIManager.DS.Green, () => OrderSelected(false), LGIcon.Construction, Loc.T("ПОСТРОИТЬ"), 13);
            var bRt = (RectTransform)_buildBtn.transform;
            bRt.anchorMin = new Vector2(0, 0); bRt.anchorMax = new Vector2(0.56f, 0); bRt.pivot = new Vector2(0, 0);
            bRt.offsetMin = new Vector2(0, 2); bRt.offsetMax = new Vector2(-4, 42);
            _buildBg = _buildBtn.GetComponent<Image>();
            _buildBuyBtn = LGBuild.Button(body, "BuildBuy", new Color(0.36f, 0.28f, 0.06f), Gold, () => OrderSelected(true), null, null, 11);
            var bbRt = (RectTransform)_buildBuyBtn.transform;
            bbRt.anchorMin = new Vector2(0.56f, 0); bbRt.anchorMax = new Vector2(1, 0); bbRt.pivot = new Vector2(0, 0);
            bbRt.offsetMin = new Vector2(4, 2); bbRt.offsetMax = new Vector2(0, 42);
            _buildBuyBg = _buildBuyBtn.GetComponent<Image>();
            _buildBuyText = LGBuild.Label(_buildBuyBtn.transform, "", 11, Color.white, TextAnchor.MiddleCenter, bold: true);
            TooltipHelper.Attach(_buildBuyBtn.gameObject, Loc.T("<b>Построить и купить</b>\nЗаказ встаёт первым в очередь планеты и сразу оплачивается гелием-3 — готов сегодня же."));

            var empty = LGBuild.Label(r, Loc.T("Выберите постройку, чтобы увидеть, что она даст"), 13, Muted, TextAnchor.MiddleCenter, wrap: true);
            empty.rectTransform.anchorMin = new Vector2(0, 0.42f); empty.rectTransform.anchorMax = new Vector2(1, 1);
            empty.rectTransform.offsetMin = new Vector2(30, 0); empty.rectTransform.offsetMax = new Vector2(-30, 0);
            _detailEmpty = empty.gameObject;

            // Очередь планеты (нижние ~42%)
            var line = LGBuild.Panel(r, "Split", new Color(Gold.r, Gold.g, Gold.b, 0.22f));
            line.rectTransform.anchorMin = new Vector2(0, 0.42f); line.rectTransform.anchorMax = new Vector2(1, 0.42f);
            line.rectTransform.offsetMin = new Vector2(18, -1); line.rectTransform.offsetMax = new Vector2(-18, 0);
            var qTitle = LGBuild.Label(r, Loc.T("ОЧЕРЕДЬ ПЛАНЕТЫ"), 11, Gold, TextAnchor.UpperLeft, bold: true);
            qTitle.rectTransform.anchorMin = new Vector2(0, 0.42f); qTitle.rectTransform.anchorMax = new Vector2(1, 0.42f);
            qTitle.rectTransform.offsetMin = new Vector2(18, -30); qTitle.rectTransform.offsetMax = new Vector2(-18, -10);
            qTitle.Track(1.4f);
            _queueBody = LGBuild.Rect(r, "Queue");
            _queueBody.anchorMin = new Vector2(0, 0); _queueBody.anchorMax = new Vector2(1, 0.42f);
            _queueBody.offsetMin = new Vector2(14, 12); _queueBody.offsetMax = new Vector2(-14, -34);
        }

        // ================================================================ Обновление

        private void SetFilter(BuildCategory? c)
        {
            _filter = c;
            SFXManager.Play(Sfx.UiClick, 0.6f);
            Rebuild(true);
            LG.Skin(_root.transform);
        }

        private void Rebuild(bool force)
        {
            if (_planet == null) return;
            var sys = _planet.ParentSystem;
            _title.text = Loc.F("СТРОИТЕЛЬСТВО · {0}", PlanetNames.Title(_planet.Name).ToUpper());
            _subtitle.text = _planet.Population > 0
                ? Loc.F("{0} · система {1} · жителей {2}/{3} · районов {4}/{5} · стабильность {6:0}",
                    _planet.ClassDisplayName, sys != null ? sys.Name : "?", _planet.Population, _planet.HousingCapacity,
                    _planet.BuiltDistricts, _planet.MaxDistricts, _planet.Stability)
                : Loc.F("{0} · система {1} · колонии нет", _planet.ClassDisplayName, sys != null ? sys.Name : "?");

            var eco = EconomyManager.Instance;
            if (eco != null)
            {
                _stock[0].text = $"{eco.EnergyCredits:0}";
                _stock[1].text = $"{eco.Minerals:0}";
                _stock[2].text = $"{eco.Alloys:0}";
                _stock[3].text = $"{eco.Influence:0}";
            }

            foreach (var t in _tabs)
            {
                bool on = t.Cat == _filter;
                t.Bg.color = on ? UIManager.DS.BtnPrimary : UIManager.DS.BtnNeutral;
                t.Label.color = on ? Color.white : Muted;
            }

            _options = BuildCatalog.For(_planet);
            var shown = new List<BuildOption>();
            foreach (var o in _options) if (!_filter.HasValue || o.Category == _filter.Value) shown.Add(o);
            // Доступное — первым, затем заблокированное, сделанное — в конце
            shown.Sort((a, b) => Rank(a).CompareTo(Rank(b)));

            string key = _filter + ":";
            foreach (var o in shown) key += o.Id + ",";
            bool rebuildCards = force || key != _optionKey;
            if (rebuildCards)
            {
                _optionKey = key;
                LGBuild.Clear(_grid);
                _cards.Clear();
                foreach (var o in shown) _cards[o.Id] = MakeCard(o);
            }
            foreach (var o in shown) if (_cards.TryGetValue(o.Id, out var c)) { c.Opt = o; FillCard(c); }

            if (_selectedId == null || !_cards.ContainsKey(_selectedId))
            {
                _selectedId = null;
                foreach (var o in shown) if (o.Available) { _selectedId = o.Id; break; }
                if (_selectedId == null && shown.Count > 0) _selectedId = shown[0].Id;
            }
            foreach (var c in _cards.Values) c.Sel.enabled = c.Opt.Id == _selectedId;
            RefreshDetail();
            RefreshQueue();
            if (rebuildCards) LG.Skin(_grid);
        }

        private static int Rank(BuildOption o) => o.Done ? 2 : o.Blocker != null ? 1 : 0;

        private Card MakeCard(BuildOption o)
        {
            var c = new Card { Opt = o };
            var bg = LGBuild.Panel(_grid, "Card_" + o.Id, new Color(0.04f, 0.08f, 0.11f, 0.95f), raycast: true);
            c.Go = bg.gameObject;
            c.Bg = bg;
            LG.Platter(bg.gameObject, 14f).SetRim(new Color(o.Color.r, o.Color.g, o.Color.b, 0.30f));
            var btn = bg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            string id = o.Id;
            btn.onClick.AddListener(() => Select(id));

            c.Sel = LGBuild.Panel(bg.transform, "Selected", new Color(o.Color.r, o.Color.g, o.Color.b, 0.16f));
            c.Sel.rectTransform.Stretch();
            LG.Ignore(c.Sel.gameObject);

            // Цветная полоса и значок
            var stripe = LGBuild.Panel(bg.transform, "Stripe", new Color(o.Color.r, o.Color.g, o.Color.b, 0.85f));
            stripe.rectTransform.TopBand(0, 3, 14, 14);
            LG.Ignore(stripe.gameObject);
            c.IconBg = LGBuild.Panel(bg.transform, "IconBg", new Color(o.Color.r * 0.18f, o.Color.g * 0.18f, o.Color.b * 0.18f, 1f));
            c.IconBg.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -14), new Vector2(42, 42));
            LG.Chip(c.IconBg.gameObject, new Color(o.Color.r, o.Color.g, o.Color.b, 0.55f));
            c.Icon = LGIcons.Create(c.IconBg.transform, o.Icon, 22, o.Color);
            c.Lock = LGIcons.Create(bg.transform, LGIcon.Lock, 13, new Color(1f, 0.6f, 0.6f));
            c.Lock.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -12), new Vector2(13, 13));
            c.Queued = LGBuild.Label(bg.transform, "", 10, Gold, TextAnchor.UpperRight, bold: true);
            c.Queued.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -30), new Vector2(110, 14));

            c.Title = LGBuild.Label(bg.transform, o.Title, 12, Color.white, TextAnchor.UpperLeft, bold: true, wrap: true);
            c.Title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -62), new Vector2(CardW - 24, 32));
            c.Effect = LGBuild.Label(bg.transform, "", 10, Muted, TextAnchor.UpperLeft, wrap: true);
            c.Effect.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -94), new Vector2(CardW - 24, 26));

            // Низ: срок слева, цена справа
            var clock = LGIcons.Create(bg.transform, o.Instant ? LGIcon.Fast : LGIcon.Clock, 11, Muted);
            clock.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(12, 10), new Vector2(11, 11));
            c.Days = LGBuild.Label(bg.transform, "", 11, Color.white, TextAnchor.LowerLeft, bold: true);
            c.Days.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(27, 8), new Vector2(70, 16));
            c.Cost = LGBuild.Rect(bg.transform, "Cost");
            c.Cost.anchorMin = new Vector2(0, 0); c.Cost.anchorMax = new Vector2(1, 0); c.Cost.pivot = new Vector2(1, 0);
            c.Cost.offsetMin = new Vector2(70, 6); c.Cost.offsetMax = new Vector2(-10, 24);
            return c;
        }

        private void FillCard(Card c)
        {
            var o = c.Opt;
            bool avail = o.Available;
            c.Title.text = o.Title;
            c.Effect.text = o.Done ? Loc.F("<color=#9EF0C8>✓ {0}</color>", o.Effect) : o.Effect;
            c.Days.text = o.Instant ? Loc.T("сразу") : Loc.F("{0} дн.", Mathf.CeilToInt(o.Days));
            c.Queued.text = o.Queued > 0 ? Loc.F("в очереди: {0}", o.Queued) : "";
            c.Lock.enabled = !avail && !o.Done;
            float a = avail ? 1f : 0.45f;
            c.Icon.color = new Color(o.Color.r, o.Color.g, o.Color.b, a);
            c.Title.color = new Color(1f, 1f, 1f, avail || o.Done ? 1f : 0.55f);
            c.Bg.color = avail ? new Color(0.04f, 0.09f, 0.12f, 0.95f) : new Color(0.03f, 0.05f, 0.07f, 0.92f);
            string key = $"{o.Energy}|{o.Minerals}|{o.Alloys}|{o.Influence}|{CostCheck.Affordable(o.Energy, o.Minerals, o.Alloys, o.Influence)}";
            if (key != c.CostKey)
            {
                c.CostKey = key;
                CostStrip(c.Cost, o.Energy, o.Minerals, o.Alloys, o.Influence, 11, TextAnchor.MiddleRight);
            }
            var tip = c.Go.GetComponent<TooltipTrigger>();
            string t = $"<b>{o.Title}</b>\n{o.Effect}" + (o.Blocker != null && !o.Done ? $"\n<color=#FF8888>{o.Blocker}</color>" : "");
            if (tip == null) TooltipHelper.Attach(c.Go, t); else tip.SetText(t);
        }

        /// <summary>Строка цены: значки ресурсов с числами; чего не хватает — красным.</summary>
        public static void CostStrip(RectTransform host, float energy, float minerals, float alloys, float influence, int size, TextAnchor align)
        {
            LGBuild.Clear(host);
            var h = host.GetComponent<HorizontalLayoutGroup>();
            if (h == null)
            {
                h = host.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 8f;
                h.childControlWidth = true; h.childControlHeight = true;
                h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            }
            h.childAlignment = align;
            var eco = EconomyManager.Instance;
            void Item(LGIcon icon, Color col, float v, float have)
            {
                if (v <= 0f) return;
                var row = LGBuild.Rect(host, "Res");
                var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hl.spacing = 3f; hl.childControlWidth = true; hl.childControlHeight = true;
                hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
                hl.childAlignment = TextAnchor.MiddleLeft;
                var ic = LGIcons.Create(row, icon, size + 1, col);
                var le = ic.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.minWidth = size + 1; le.preferredHeight = size + 1;
                bool ok = eco == null || have >= v - 0.01f;
                var t = LGBuild.Label(row, $"{v:0}", size, ok ? Color.white : new Color(1f, 0.45f, 0.45f), TextAnchor.MiddleLeft, bold: true);
                var tle = t.gameObject.AddComponent<LayoutElement>();
                tle.preferredWidth = t.preferredWidth + 2f;
            }
            Item(LGIcon.Energy, CEnergy, energy, eco != null ? eco.EnergyCredits : 0f);
            Item(LGIcon.Minerals, CMinerals, minerals, eco != null ? eco.Minerals : 0f);
            Item(LGIcon.Alloys, CAlloys, alloys, eco != null ? eco.Alloys : 0f);
            Item(LGIcon.Influence, CInfluence, influence, eco != null ? eco.Influence : 0f);
        }

        private void Select(string id)
        {
            _selectedId = id;
            SFXManager.Play(Sfx.UiClick, 0.5f);
            foreach (var c in _cards.Values) c.Sel.enabled = c.Opt.Id == id;
            RefreshDetail();
        }

        private BuildOption Selected
        {
            get
            {
                if (_selectedId == null) return null;
                foreach (var o in _options) if (o.Id == _selectedId) return o;
                return null;
            }
        }

        private void RefreshDetail()
        {
            var o = Selected;
            _detailEmpty.SetActive(o == null);
            _detailBody.SetActive(o != null);
            if (o == null) return;

            _dIconBg.color = new Color(o.Color.r * 0.2f, o.Color.g * 0.2f, o.Color.b * 0.2f, 1f);
            _dIcon.sprite = LGIcons.Get(o.Icon);
            _dIcon.color = o.Color;
            _dTitle.text = o.Title;
            _dEffect.text = o.Done ? Loc.F("✓ {0}", o.Effect) : o.Effect;
            _dEffect.color = o.Done ? UIManager.DS.Green : o.Color;
            _dText.text = o.Detail ?? "";
            string ck = $"{o.Id}|{o.Energy}|{o.Minerals}|{o.Alloys}|{o.Influence}|{CostCheck.Affordable(o.Energy, o.Minerals, o.Alloys, o.Influence)}";
            if (ck != _detailCostKey) { _detailCostKey = ck; CostStrip(_cost, o.Energy, o.Minerals, o.Alloys, o.Influence, 13, TextAnchor.MiddleLeft); }
            _dTime.text = o.Instant ? Loc.T("Действует сразу") : o.Category == BuildCategory.Ship
                ? Loc.F("Срок: {0} дн. на стапеле верфи", Mathf.CeilToInt(o.Days))
                : Loc.F("Срок: {0} дн. (с учётом губернатора)", Mathf.CeilToInt(o.Days));
            _dBlock.text = o.Done ? "" : o.Blocker ?? "";

            bool can = o.Available;
            _buildBtn.interactable = can;
            _buildBg.color = can ? UIManager.DS.BtnSuccess : UIManager.DS.BtnDisabled;

            // «Построить и купить»: заказ + немедленная оплата гелием (только для заказов очереди)
            int buy = o.Instant ? 0 : Mathf.Max(5, Mathf.CeilToInt(o.RawDays * (o.Category == BuildCategory.Ship ? ConstructionManager.BuyPerDayShip : ConstructionManager.BuyPerDayPlanet) * ConstructionManager.BuyUnstartedMult));
            var eco = EconomyManager.Instance;
            bool canBuy = can && !o.Instant && eco != null && eco.EnergyCredits >= buy + o.Energy;
            _buildBuyBtn.gameObject.SetActive(!o.Instant);
            _buildBuyBtn.interactable = canBuy;
            _buildBuyBg.color = canBuy ? new Color(0.36f, 0.28f, 0.06f) : UIManager.DS.BtnDisabled;
            _buildBuyText.text = Loc.F("И КУПИТЬ · {0} гел.", buy);
        }

        private void OrderSelected(bool buy)
        {
            var o = Selected;
            if (o == null || !o.Available || o.Order == null) return;
            var cm = ConstructionManager.Instance;
            int before = cm != null ? cm.Jobs.Count : 0;
            if (!o.Order()) { SFXManager.Play(Sfx.UiDenied); return; }
            if (buy && cm != null && cm.Jobs.Count > before)
            {
                var job = cm.Jobs[cm.Jobs.Count - 1];
                if (job.IsPlanetJob) cm.Prioritize(job);
                if (!cm.TryBuy(job))
                    NotificationCenter.Show(Loc.T("Не удалось купить"), cm.BuyBlocker(job) ?? "", NotificationCenter.Kind.Warning, 4f);
            }
            SFXManager.Play(Sfx.UiConfirm, 0.8f);
            EconomyManager.Instance?.RecalculateAll();
            Rebuild(false);
        }

        // ================================================================ Очередь

        private void RefreshQueue()
        {
            var cm = ConstructionManager.Instance;
            if (cm == null) return;
            var q = cm.QueueFor(_planet);
            var ships = new List<ConstructionJob>();
            if (_planet.ParentSystem != null)
                foreach (var j in cm.ShipQueue(0)) if (j.SystemId == _planet.ParentSystem.Id) ships.Add(j);
            // Перестраиваем строки только когда что-то видимое изменилось (иначе кнопки «мигают» под курсором)
            var key = new System.Text.StringBuilder();
            foreach (var j in q) key.Append(j.Id).Append(':').Append(cm.IsActive(j)).Append(':').Append(Mathf.CeilToInt(cm.DaysUntilDone(j)))
                                    .Append(':').Append(Mathf.RoundToInt(j.Progress * 50f)).Append(':').Append(cm.BuyBlocker(j) == null).Append(';');
            foreach (var j in ships) key.Append('s').Append(j.Id).Append(':').Append(Mathf.CeilToInt(cm.DaysUntilDone(j))).Append(':').Append(cm.BuyBlocker(j) == null).Append(';');
            string k = key.ToString();
            if (k == _queueKey && _queueBody.childCount > 0) return;
            _queueKey = k;
            LGBuild.Clear(_queueBody);

            if (q.Count == 0 && ships.Count == 0)
            {
                var e = LGBuild.Label(_queueBody, Loc.T("Пусто. Планета простаивает — выберите постройку слева."), 12, Muted, TextAnchor.MiddleCenter, wrap: true);
                e.rectTransform.Stretch(10, 10, 10, 10);
                return;
            }
            float y = 0f;
            foreach (var j in q) { QueueRow(j, y, cm, true); y += 44f; }
            foreach (var j in ships) { if (y > 230f) break; QueueRow(j, y, cm, false); y += 44f; }
            LG.Skin(_queueBody);
        }

        private void QueueRow(ConstructionJob j, float y, ConstructionManager cm, bool planetJob)
        {
            bool active = cm.IsActive(j);
            var row = LGBuild.Panel(_queueBody, "Row", new Color(1f, 1f, 1f, active ? 0.06f : 0.025f), raycast: true);
            row.rectTransform.TopBand(y, 40f);
            LG.Platter(row.gameObject, 10f).SetRim(new Color(1f, 1f, 1f, active ? 0.14f : 0.05f));
            var ic = LGIcons.Create(row.transform, JobIcon(j), 16, active ? Gold : Muted);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 2), new Vector2(16, 16));
            var nm = LGBuild.Label(row.transform, JobTitle(j), 12, active ? Color.white : Muted, TextAnchor.MiddleLeft, bold: true);
            nm.rectTransform.Stretch(34, 8, 190, 0);
            var days = LGBuild.Label(row.transform, Loc.F("{0} дн.", Mathf.CeilToInt(cm.DaysUntilDone(j))), 11, Muted, TextAnchor.MiddleRight, bold: true);
            days.rectTransform.Stretch(0, 8, 136, 0);
            var ph = LGBuild.Rect(row.transform, "Prog");
            ph.anchorMin = new Vector2(0, 0); ph.anchorMax = new Vector2(1, 0);
            ph.offsetMin = new Vector2(34, 4); ph.offsetMax = new Vector2(-136, 8);
            LGBuild.Bar(ph, active ? Gold : Muted, j.Progress, 3f);

            // Кнопки справа: купить (только активный), первым (планета), отменить
            float x = -6f;
            var cancel = LGBuild.Button(row.transform, "Cancel", UIManager.DS.BtnDanger, UIManager.DS.Red, () => { cm.Cancel(j); Rebuild(false); }, LGIcon.Close, null, 8, 8f);
            ((RectTransform)cancel.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(x, 0), new Vector2(26, 26));
            TooltipHelper.Attach(cancel.gameObject, Loc.T("<b>Отменить</b>\nРесурсы вернутся полностью."));
            x -= 30f;
            if (planetJob && !active)
            {
                var up = LGBuild.Button(row.transform, "First", UIManager.DS.BtnNeutral, new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f), () => { cm.Prioritize(j); Rebuild(false); }, LGIcon.Fast, null, 8, 8f);
                ((RectTransform)up.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(x, 0), new Vector2(26, 26));
                up.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                TooltipHelper.Attach(up.gameObject, Loc.T("<b>Строить первым</b>\nНачатое раньше встанет на паузу и сохранит прогресс."));
            }
            if (active && j.Owner == 0)
            {
                int price = ConstructionManager.BuyCost(j);
                string block = cm.BuyBlocker(j);
                var buy = LGBuild.Button(row.transform, "Buy", block == null ? new Color(0.36f, 0.28f, 0.06f) : UIManager.DS.BtnDisabled, Gold,
                    () => { if (cm.TryBuy(j)) { SFXManager.Play(Sfx.UiConfirm); EconomyManager.Instance?.RecalculateAll(); } Rebuild(false); }, null, null, 9, 8f);
                ((RectTransform)buy.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(x, 0), new Vector2(76, 26));
                buy.interactable = block == null;
                var bi = LGIcons.Create(buy.transform, LGIcon.Energy, 11, CEnergy);
                bi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(11, 11));
                var bt = LGBuild.Label(buy.transform, $"{price}", 11, block == null ? Color.white : Muted, TextAnchor.MiddleLeft, bold: true);
                bt.rectTransform.Stretch(23, 0, 4, 0);
                TooltipHelper.Attach(buy.gameObject, BuyTip(j, block));
            }
        }

        public static string BuyTip(ConstructionJob j, string block)
        {
            return Loc.F("<b>Купить за {0} гелия-3</b>\nСтройка завершится сегодня же.\n", ConstructionManager.BuyCost(j)) +
                   Loc.F("<color=#8AA2A8>Цена: оставшиеся {0} дн. × {1:0.#} гелия-3", Mathf.CeilToInt(j.DaysLeft),
                         j.Kind == JobKind.Ship ? ConstructionManager.BuyPerDayShip : ConstructionManager.BuyPerDayPlanet) +
                   (j.Progress < 0.01f ? Loc.F(", ×{0:0.#} — работа ещё не начата", ConstructionManager.BuyUnstartedMult) : "") + "</color>" +
                   (block != null ? $"\n<color=#FF8888>{block}</color>" : "");
        }

        public static LGIcon JobIcon(ConstructionJob j) => j.Kind switch
        {
            JobKind.District => BuildCatalog.DistrictIcon(j.District),
            JobKind.MiningStation => LGIcon.Minerals,
            JobKind.Colony => LGIcon.Population,
            JobKind.Terraform => LGIcon.Planet,
            JobKind.Robot => LGIcon.Industry,
            JobKind.GeneAdapt => LGIcon.Research,
            _ => j.ShipType == FleetType.Science ? LGIcon.ScienceShip
               : j.ShipType == FleetType.Constructor ? LGIcon.ConstructorShip : ModuleVisuals.HullIcon(j.Hull)
        };

        public static string JobTitle(ConstructionJob j) => j.Kind switch
        {
            JobKind.District => BuildCatalog.DistrictTitle(j.District),
            JobKind.MiningStation => Loc.T("Добывающий комплекс"),
            JobKind.Colony => Loc.T("Колония"),
            JobKind.Terraform => Loc.T("Терраформинг"),
            JobKind.Robot => Loc.T("Сборка роботов"),
            JobKind.GeneAdapt => Loc.T("Генная адаптация"),
            _ => j.ShipType == FleetType.Science ? Loc.T("Научный корабль")
               : j.ShipType == FleetType.Constructor ? Loc.T("Строительный корабль")
               : j.Hull == ShipClass.Destroyer ? Loc.T("Эсминец") : j.Hull == ShipClass.Frigate ? Loc.T("Фрегат") : Loc.T("Корвет")
        };
    }
}
