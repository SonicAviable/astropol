using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Окно «Арсенал» (левое меню, клавиша J) — откуда берутся карты для наземных штурмов:
    ///   • слева — производство в выбранной колонии: пехоту набирают в городских районах, технику собирают
    ///     в промышленных, гарнизон и укрепления строят на месте, приказы выдаёт штаб в столице;
    ///   • справа — войска: десант на кораблях (места, автопогрузка, высадка), резерв и гарнизоны колоний.
    /// Клик по карте резерва — погрузить на корабль у этой колонии, клик по карте на корабле — высадить.
    /// Наведение на любую карту — крупный предпросмотр.
    /// </summary>
    public sealed class ArsenalWindow : MonoBehaviour
    {
        public static ArsenalWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);
        public static bool IsOpenNow => Instance != null && Instance.IsOpen;

        private static readonly Vector2 MiniCard = new Vector2(76, 106);
        private static readonly Vector2 ListCard = new Vector2(70, 98);
        private static readonly Vector2 BigCard = new Vector2(250, 350);

        private GameObject _root;
        private RectTransform _rt, _prodList, _forceList, _previewRoot;
        private Text _subtitle, _colonyName, _colonyInfo;
        private ScrollRect _prodScroll, _forceScroll;
        private CardView _preview;
        private int _colony = -1;
        private string _sig;
        private float _refresh;
        private bool _dirty;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Gold => UIManager.DS.Gold;
        private static readonly Color Accent = new Color(0.95f, 0.45f, 0.35f);
        private static readonly Color CPanel = new Color(0.020f, 0.045f, 0.065f, 0.92f);

        public static void Toggle()
        {
            if (Instance == null) new GameObject("[UI] ArsenalWindow").AddComponent<ArsenalWindow>();
            if (Instance.IsOpen) Instance.Close();
            else Instance.Open();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            ArmyManager.OnChanged += MarkDirty;
        }

        private void OnDestroy()
        {
            ArmyManager.OnChanged -= MarkDirty;
            if (Instance == this) Instance = null;
        }

        private void MarkDirty() => _dirty = true;

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (_preview != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, Input.mousePosition, null, out var lp);
                var half = _rt.rect.size * 0.5f;
                float x = lp.x + (lp.x > 0 ? -BigCard.x * 0.5f - 40f : BigCard.x * 0.5f + 40f);
                float y = Mathf.Clamp(lp.y, -half.y + BigCard.y * 0.5f + 10f, half.y - BigCard.y * 0.5f - 10f);
                _previewRoot.anchoredPosition = new Vector2(x, y);
            }
            if ((_refresh -= Time.unscaledDeltaTime) <= 0f || _dirty)
            {
                _refresh = 1f;
                string sig = Signature();
                if (_dirty || sig != _sig) { _dirty = false; _sig = sig; Rebuild(); }
            }
        }

        public void Open()
        {
            if (ArmyManager.Instance == null) return;
            if (_root == null) Build();
            if (_root == null) return;
            var cols = Colonies();
            if (_colony < 0 || cols.Find(s => s.Id == _colony) == null)
            {
                var sel = UIManager.Instance != null ? UIManager.Instance.ActiveSystemPublic : null;
                _colony = sel != null && sel.OwnerId == 0 && ArmyManager.SystemPopulation(sel) > 0 ? sel.Id
                        : cols.Count > 0 ? (cols.Find(s => s.Id == ArmyManager.CapitalOf(0)) ?? cols[0]).Id : -1;
            }
            _sig = null;
            _dirty = true;
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            SFXManager.Play(Sfx.WindowOpen);
        }

        public void Close()
        {
            if (_root == null) return;
            HidePreview();
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            TooltipSystem.Hide();
            SFXManager.Play(Sfx.WindowClose);
        }

        // ================================================================ Данные

        private static List<StarSystem> Colonies()
        {
            var l = new List<StarSystem>();
            foreach (var s in EmpireStats.Systems)
                if (s != null && s.OwnerId == 0 && s.HasStarbase && ArmyManager.SystemPopulation(s) > 0) l.Add(s);
            l.Sort((a, b) => ArmyManager.SystemPopulation(b).CompareTo(ArmyManager.SystemPopulation(a)));
            return l;
        }

        private static List<FleetData> TroopShips()
        {
            var l = new List<FleetData>();
            var fm = FleetManager.Instance;
            if (fm == null) return l;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d != null && !d.Destroyed && d.OwnerId == 0 && ArmyManager.Capacity(d) > 0) l.Add(d);
            }
            return l;
        }

        private string Signature()
        {
            var eco = EconomyManager.Instance;
            var sb = new StringBuilder();
            sb.Append(_colony).Append('|');
            if (eco != null) sb.Append((int)eco.EnergyCredits / 5).Append(',').Append((int)eco.Minerals / 5).Append(',').Append((int)eco.Alloys / 5).Append(',').Append((int)eco.Influence / 5).Append('|');
            var am = ArmyManager.Instance;
            if (am != null) foreach (var c in am.Of(0)) sb.Append(c.Id).Append(c.Fleet).Append(c.System).Append(c.WoundDays > 0 ? 'w' : '-');
            sb.Append('|');
            var cm = ConstructionManager.Instance;
            if (cm != null) foreach (var j in cm.Jobs) if (j.Owner == 0 && j.Kind == JobKind.Troop) sb.Append(j.Id).Append(':').Append((int)(j.Progress * 20)).Append(',');
            foreach (var f in TroopShips()) sb.Append(f.Id).Append('@').Append(f.CurrentSystemId).Append(f.State == FleetState.InHyperlane ? 'h' : 'o');
            return sb.ToString();
        }

        // ================================================================ Построение

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            _rt = LGBuild.Rect(host.transform, "ArsenalWindow");
            _rt.Stretch(28, 28, 28, 28);
            _root = _rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.010f, 0.022f, 0.032f, 0.97f);
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(Accent.r, Accent.g, Accent.b, 0.42f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            // Фон — стол боя, приглушённый
            var art = LGBuild.Rect(_rt, "Art").gameObject.AddComponent<RawImage>();
            art.rectTransform.Stretch();
            art.texture = CardArt.Table(Terrain.Continental);
            art.color = new Color(0.35f, 0.32f, 0.3f, 0.22f);
            art.raycastTarget = false;
            var vign = LGBuild.Panel(_rt, "Vignette", new Color(0, 0, 0, 0.75f));
            vign.rectTransform.Stretch();
            vign.sprite = CardSprites.Vignette;
            vign.raycastTarget = false;

            var icon = LGIcons.Create(_rt, LGIcon.Swords, 26, Accent);
            icon.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -24), new Vector2(26, 26));
            var title = LGBuild.Label(_rt, Loc.T("АРСЕНАЛ"), 28, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(64, -18), new Vector2(700, 36));
            title.Track(3f);
            _subtitle = LGBuild.Label(_rt, "", 12, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(66, -56), new Vector2(1400, 18));

            var close = LGBuild.Button(_rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc / J)</color>"));

            // ---- Слева: производство ----
            var left = Column(0f, 0.5f, Loc.T("ПРОИЗВОДСТВО"), LGIcon.Industry, out var leftBody);
            var chooser = LGBuild.Panel(leftBody, "Colony", new Color(0.03f, 0.07f, 0.1f, 0.9f));
            chooser.rectTransform.anchorMin = new Vector2(0, 1); chooser.rectTransform.anchorMax = new Vector2(1, 1);
            chooser.rectTransform.pivot = new Vector2(0.5f, 1);
            chooser.rectTransform.offsetMin = new Vector2(0, -64); chooser.rectTransform.offsetMax = Vector2.zero;
            LG.Platter(chooser.gameObject, 12f).SetRim(new Color(1f, 1f, 1f, 0.1f));
            var prev = LGBuild.Button(chooser.rectTransform, "Prev", UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.2f), () => Cycle(-1), LGIcon.Back, null, 11);
            ((RectTransform)prev.transform).At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(40, 40));
            var next = LGBuild.Button(chooser.rectTransform, "Next", UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.2f), () => Cycle(1), LGIcon.Back, null, 11);
            ((RectTransform)next.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(40, 40));
            next.transform.GetChild(next.transform.childCount - 1).localRotation = Quaternion.Euler(0, 0, 180f);
            _colonyName = LGBuild.Label(chooser.rectTransform, "", 18, Color.white, TextAnchor.UpperCenter, bold: true);
            _colonyName.rectTransform.Stretch(60, 0, 60, 8);
            _colonyInfo = LGBuild.Label(chooser.rectTransform, "", 11, Muted, TextAnchor.LowerCenter);
            _colonyInfo.rectTransform.Stretch(60, 9, 60, 0);

            var prodHost = LGBuild.Rect(leftBody, "ProdHost");
            prodHost.Stretch(0, 0, 0, 72);
            _prodList = LGBuild.ScrollList(prodHost, 6f, 4);
            _prodScroll = prodHost.GetComponentInChildren<ScrollRect>();

            // ---- Справа: войска ----
            Column(0.5f, 1f, Loc.T("ВОЙСКА"), LGIcon.Fleet, out var rightBody);
            _forceList = LGBuild.ScrollList(rightBody, 10f, 4);
            _forceScroll = rightBody.GetComponentInChildren<ScrollRect>();

            _previewRoot = LGBuild.Rect(_rt, "Preview");
            _previewRoot.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, BigCard);
        }

        private RectTransform Column(float x0, float x1, string head, LGIcon ic, out RectTransform body)
        {
            var col = LGBuild.Panel(_rt, head, CPanel);
            col.rectTransform.anchorMin = new Vector2(x0, 0);
            col.rectTransform.anchorMax = new Vector2(x1, 1);
            col.rectTransform.offsetMin = new Vector2(x0 == 0f ? 24 : 8, 24);
            col.rectTransform.offsetMax = new Vector2(x1 == 1f ? -24 : -8, -86);
            LG.Platter(col.gameObject, 16f).SetRim(new Color(1f, 1f, 1f, 0.08f));
            var hi = LGIcons.Create(col.rectTransform, ic, 14, Accent);
            hi.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -16), new Vector2(14, 14));
            var ht = LGBuild.Label(col.rectTransform, head, 13, Accent, TextAnchor.UpperLeft, bold: true);
            ht.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -14), new Vector2(500, 18));
            ht.Track(3f);
            body = LGBuild.Rect(col.rectTransform, "Body");
            body.Stretch(14, 12, 14, 44);
            return col.rectTransform;
        }

        private void Cycle(int dir)
        {
            var cols = Colonies();
            if (cols.Count == 0) return;
            int i = cols.FindIndex(s => s.Id == _colony);
            i = ((i < 0 ? 0 : i + dir) % cols.Count + cols.Count) % cols.Count;
            _colony = cols[i].Id;
            SFXManager.Play(Sfx.UiTab);
            _dirty = true;
        }

        // ================================================================ Наполнение

        private void Rebuild()
        {
            var am = ArmyManager.Instance;
            if (am == null) return;
            HidePreview();
            var mine = am.Of(0);
            int onShips = 0, reserve = 0, garrison = 0, wounded = 0;
            foreach (var c in mine) { if (c.Fleet >= 0) onShips++; else if (c.Garrison) garrison++; else reserve++; if (c.Wounded) wounded++; }
            _subtitle.text = Loc.F("Карт: {0}  ·  на кораблях {1}  ·  в резерве {2}  ·  гарнизоны {3}{4}  ·  содержание {5:0.#} гелия в месяц",
                mine.Count, onShips, reserve, garrison, wounded > 0 ? Loc.F("  ·  ранены {0}", wounded) : "", am.Upkeep(0));

            float sp = _prodScroll != null ? _prodScroll.verticalNormalizedPosition : 1f;
            float sf = _forceScroll != null ? _forceScroll.verticalNormalizedPosition : 1f;
            BuildProduction();
            BuildForces();
            Canvas.ForceUpdateCanvases();
            if (_prodScroll != null) _prodScroll.verticalNormalizedPosition = sp;
            if (_forceScroll != null) _forceScroll.verticalNormalizedPosition = sf;
        }

        private static string SiteName(ProdSite s)
        {
            switch (s)
            {
                case ProdSite.City: return Loc.T("набор в городских районах");
                case ProdSite.Industry: return Loc.T("сборка в промышленных районах");
                case ProdSite.Command: return Loc.T("штаб в столице, сразу");
                case ProdSite.Garrison: return Loc.T("строится на месте, колонию не покидает");
                default: return "";
            }
        }

        private void BuildProduction()
        {
            LGBuild.Clear(_prodList);
            var am = ArmyManager.Instance;
            var sys = EmpireStats.GetSystem(_colony);
            if (sys == null || sys.OwnerId != 0)
            {
                _colonyName.text = Loc.T("Нет колоний");
                _colonyInfo.text = Loc.T("Карты производят колонии с жителями");
                return;
            }
            int pop = ArmyManager.SystemPopulation(sys);
            _colonyName.text = sys.Name.ToUpper();
            _colonyInfo.text = Loc.F("жители {0}  ·  городских районов {1}  ·  промышленных {2}  ·  укрепления {3}",
                pop, ArmyManager.Districts(sys, DistrictType.Urban), ArmyManager.Districts(sys, DistrictType.Industrial), sys.Fortification);

            // Очередь
            var cm = ConstructionManager.Instance;
            if (cm != null)
            {
                var jobs = new List<ConstructionJob>();
                foreach (var j in cm.Jobs) if (j.Owner == 0 && j.Kind == JobKind.Troop && j.SystemId == sys.Id) jobs.Add(j);
                if (jobs.Count > 0)
                {
                    Header(_prodList, Loc.F("В ПРОИЗВОДСТВЕ · {0}", jobs.Count), Gold);
                    foreach (var j in jobs) JobRow(j);
                }
            }

            string faction = FactionTraits.Key(0);
            ProdSite? last = null;
            var list = new List<CardDef>();
            foreach (var d in CardCatalog.ForFaction(faction)) if (d.Site != ProdSite.None) list.Add(d);
            list.Sort((a, b) => a.Site != b.Site ? a.Site.CompareTo(b.Site) : a.Cost != b.Cost ? a.Cost.CompareTo(b.Cost) : string.CompareOrdinal(a.Id, b.Id));
            foreach (var d in list)
            {
                if (last != d.Site)
                {
                    last = d.Site;
                    Header(_prodList, SiteHeader(d.Site), Muted);
                }
                ProdRow(d, sys, am);
            }
        }

        private static string SiteHeader(ProdSite s)
        {
            switch (s)
            {
                case ProdSite.City: return Loc.T("ПЕХОТА · ГОРОДСКИЕ РАЙОНЫ");
                case ProdSite.Industry: return Loc.T("ТЕХНИКА · ПРОМЫШЛЕННЫЕ РАЙОНЫ");
                case ProdSite.Command: return Loc.T("ПРИКАЗЫ И ГЕРОИ · ШТАБ В СТОЛИЦЕ");
                default: return Loc.T("ОБОРОНА КОЛОНИИ");
            }
        }

        private void Header(RectTransform list, string text, Color c)
        {
            var row = LGBuild.Rect(list, "H");
            LGBuild.Height(row.gameObject, 26);
            var t = LGBuild.Label(row, text, 11, c, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.Stretch(6, 3, 6, 0);
            t.Track(2.5f);
            var line = LGBuild.Panel(row, "Line", new Color(c.r, c.g, c.b, 0.25f));
            line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 1);
        }

        private void JobRow(ConstructionJob j)
        {
            var d = CardCatalog.Get(j.DesignId);
            if (d == null) return;
            var row = LGBuild.Panel(_prodList, "Job", new Color(0.05f, 0.09f, 0.12f, 0.85f));
            LGBuild.Height(row.gameObject, 34);
            LG.Platter(row.gameObject, 8f);
            var fill = LGBuild.Panel(row.rectTransform, "Fill", new Color(Gold.r, Gold.g, Gold.b, 0.18f));
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(j.Progress, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var t = LGBuild.Label(row.rectTransform, Loc.F("{0}  <color=#8AA2A8>· {1:0} дн.</color>", d.Name, Mathf.Ceil(j.DaysLeft)), 13, Color.white, TextAnchor.MiddleLeft);
            t.rectTransform.Stretch(12, 0, 12, 0);
            t.supportRichText = true;
        }

        private void ProdRow(CardDef d, StarSystem sys, ArmyManager am)
        {
            string blocker = am.ProduceBlocker(0, d, d.Site == ProdSite.Command ? null : sys);
            ArmyManager.Cost(d, out float e, out float m, out float a, out float inf);
            var eco = EconomyManager.Instance;
            bool afford = eco != null && eco.EnergyCredits >= e && eco.Minerals >= m && eco.Alloys >= a && eco.Influence >= inf;
            bool can = blocker == null && afford;

            var row = LGBuild.Panel(_prodList, d.Id, new Color(0.04f, 0.08f, 0.11f, 0.88f));
            LGBuild.Height(row.gameObject, ListCard.y + 12);
            LG.Platter(row.gameObject, 10f).SetRim(new Color(1, 1, 1, can ? 0.12f : 0.05f));

            var cv = CardView.Create(row.rectTransform, new BCard(d), 0, FactionTraits.Key(0), ListCard, true);
            cv.Rt.anchorMin = cv.Rt.anchorMax = new Vector2(0, 0.5f);
            cv.Rt.anchoredPosition = new Vector2(ListCard.x * 0.5f + 8, 0);
            HookPreview(cv);

            float x0 = ListCard.x + 22;
            var name = LGBuild.Label(row.rectTransform, d.Name, 15, Color.white, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.anchorMin = new Vector2(0, 1); name.rectTransform.anchorMax = new Vector2(1, 1);
            name.rectTransform.pivot = new Vector2(0, 1);
            name.rectTransform.offsetMin = new Vector2(x0, -26); name.rectTransform.offsetMax = new Vector2(-150, -6);
            string stats = d.Kind == CardKind.Order ? d.KindName : Loc.F("{0}  ·  сила {1}  ·  броня {2}", d.KindName, d.Power, d.Armor);
            string size = d.Size > 0 ? Loc.F("  ·  мест на корабле: {0}", d.Size) : "";
            var sub = LGBuild.Label(row.rectTransform, $"<color=#{ColorUtility.ToHtmlStringRGB(CardArt.Frame(d, 0, FactionTraits.Key(0)))}>{stats}</color>{size}\n<color=#8AA2A8>{SiteName(d.Site)}</color>", 11, Muted, TextAnchor.UpperLeft, wrap: true);
            sub.rectTransform.anchorMin = new Vector2(0, 0); sub.rectTransform.anchorMax = new Vector2(1, 1);
            sub.rectTransform.offsetMin = new Vector2(x0, 30); sub.rectTransform.offsetMax = new Vector2(-150, -28);
            sub.supportRichText = true;

            // Цена: значки ресурсов
            var price = LGBuild.Rect(row.rectTransform, "Price");
            price.anchorMin = new Vector2(0, 0); price.anchorMax = new Vector2(1, 0);
            price.pivot = new Vector2(0, 0);
            price.offsetMin = new Vector2(x0, 8); price.offsetMax = new Vector2(-150, 28);
            float px = 0f;
            void Res(LGIcon ic, float v, float have, Color c)
            {
                if (v <= 0f) return;
                var i = LGIcons.Create(price, ic, 13, c);
                i.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(px, 0), new Vector2(13, 13));
                var t = LGBuild.Label(price, v.ToString("0"), 13, have >= v ? Color.white : UIManager.DS.Red, TextAnchor.MiddleLeft, bold: true);
                t.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(px + 17, 0), new Vector2(50, 18));
                px += 17 + t.preferredWidth + 12;
            }
            if (eco != null)
            {
                Res(LGIcon.Energy, e, eco.EnergyCredits, new Color(1f, 0.85f, 0.3f));
                Res(LGIcon.Minerals, m, eco.Minerals, new Color(0.55f, 0.85f, 1f));
                Res(LGIcon.Alloys, a, eco.Alloys, new Color(0.85f, 0.7f, 1f));
                Res(LGIcon.Influence, inf, eco.Influence, new Color(0.75f, 0.6f, 1f));
            }
            var clock = LGIcons.Create(price, LGIcon.Clock, 12, Muted);
            clock.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(px, 0), new Vector2(12, 12));
            var days = LGBuild.Label(price, d.Days > 0 && d.Site != ProdSite.Command ? Loc.F("{0} дн.", d.Days) : Loc.T("сразу"), 12, Muted, TextAnchor.MiddleLeft);
            days.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(px + 16, 0), new Vector2(80, 18));
            if (d.Upkeep > 0f)
            {
                var up = LGBuild.Label(price, Loc.F("· {0:0.#}/мес.", d.Upkeep), 11, Muted, TextAnchor.MiddleLeft);
                up.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(px + 16 + days.preferredWidth + 8, 0), new Vector2(90, 18));
            }

            var btn = LGBuild.Button(row.rectTransform, "Order", can ? new Color(0.45f, 0.16f, 0.12f, 0.95f) : UIManager.DS.BtnDisabled,
                new Color(1f, 1f, 1f, can ? 0.35f : 0.1f), () => Order(d), LGIcon.Check, Loc.T("ЗАКАЗАТЬ"), 11);
            ((RectTransform)btn.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(128, 38));
            btn.interactable = can;
            string tip = $"<b>{d.Name}</b>\n{d.Text}";
            if (blocker != null) tip += $"\n<color=#FF8080>{blocker}</color>";
            else if (!afford) tip += "\n<color=#FF8080>" + Loc.T("Не хватает ресурсов") + "</color>";
            TooltipHelper.Attach(btn.gameObject, tip);
        }

        private void Order(CardDef d)
        {
            var sys = EmpireStats.GetSystem(_colony);
            var am = ArmyManager.Instance;
            if (am == null) return;
            if (am.TryProducePlayer(d, d.Site == ProdSite.Command ? null : sys))
            {
                SFXManager.Play(Sfx.BuildQueued);
                UIManager.Instance?.RefreshResourceBarPublic();
            }
            else SFXManager.Play(Sfx.UiDenied);
            _dirty = true;
        }

        // ---------------------------------------------------------------- войска

        private void BuildForces()
        {
            LGBuild.Clear(_forceList);
            var am = ArmyManager.Instance;

            var ships = TroopShips();
            Header(_forceList, Loc.F("ДЕСАНТ НА КОРАБЛЯХ · {0}", ships.Count), Gold);
            if (ships.Count == 0) Note(_forceList, Loc.T("Нет военных кораблей с десантными местами: корвет берёт 1 место, фрегат 2, эсминец 4"));
            foreach (var f in ships) FleetBlock(f, am);

            Header(_forceList, Loc.T("КОЛОНИИ: РЕЗЕРВ И ГАРНИЗОНЫ"), Gold);
            bool any = false;
            foreach (var s in Colonies())
            {
                var here = am.AtSystem(0, s.Id);
                if (here.Count == 0) continue;
                any = true;
                ColonyBlock(s, here, am, ships);
            }
            if (!any) Note(_forceList, Loc.T("Резерв пуст — закажите карты слева. Готовые карты ждут на колонии, корабли с автопогрузкой забирают их сами"));
        }

        private void Note(RectTransform list, string text)
        {
            var row = LGBuild.Rect(list, "Note");
            var t = LGBuild.Label(row, text, 12, Muted, TextAnchor.UpperLeft, wrap: true);
            t.rectTransform.Stretch(6, 0, 6, 0);
            LGBuild.Height(row.gameObject, 34);
        }

        private RectTransform Block(string name, out float width)
        {
            var b = LGBuild.Panel(_forceList, name, new Color(0.04f, 0.08f, 0.11f, 0.88f));
            LG.Platter(b.gameObject, 12f).SetRim(new Color(1f, 1f, 1f, 0.07f));
            width = _forceList.rect.width - 8f;
            if (width < 200f) width = 600f;
            return b.rectTransform;
        }

        /// <summary>Разложить мини-карты рядами; вернуть высоту.</summary>
        private float Lay(RectTransform parent, List<CardInstance> cards, float top, float width, System.Action<CardView, CardInstance> hook, System.Func<CardInstance, string> status)
        {
            int perRow = Mathf.Max(1, Mathf.FloorToInt((width - 20f) / (MiniCard.x + 8f)));
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c.D == null) continue;
                var cv = CardView.Create(parent, new BCard(c.D, c.Id, c.Stars), 0, FactionTraits.Key(0), MiniCard, true);
                cv.Rt.anchorMin = cv.Rt.anchorMax = new Vector2(0, 1);
                cv.Rt.anchoredPosition = new Vector2(12 + MiniCard.x * 0.5f + (i % perRow) * (MiniCard.x + 8f), -top - MiniCard.y * 0.5f - (i / perRow) * (MiniCard.y + 8f));
                string st = status?.Invoke(c);
                if (st != null) cv.SetStatus(st, new Color(1f, 0.55f, 0.45f));
                if (c.Wounded) cv.SetAlpha(0.6f);
                HookPreview(cv);
                hook?.Invoke(cv, c);
            }
            int rows = cards.Count == 0 ? 0 : (cards.Count + perRow - 1) / perRow;
            return rows * (MiniCard.y + 8f);
        }

        private void FleetBlock(FleetData f, ArmyManager am)
        {
            var b = Block("Fleet" + f.Id, out float w);
            var cards = am.OnFleet(f.Id);
            int used = am.Used(f.Id), cap = ArmyManager.Capacity(f);
            var sys = EmpireStats.GetSystem(f.CurrentSystemId);
            bool moving = f.State == FleetState.InHyperlane;
            string where = moving ? Loc.T("в пути") : sys != null ? sys.Name : "?";

            var t = LGBuild.Label(b, $"{f.Name}  <color=#8AA2A8>· {where}</color>", 14, Color.white, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -8), new Vector2(w - 260, 20));
            t.supportRichText = true;
            // Места: полоска
            var bar = LGBuild.Panel(b, "Cap", new Color(1, 1, 1, 0.08f));
            bar.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -34), new Vector2(160, 6));
            var fill = LGBuild.Panel(bar.rectTransform, "Fill", used >= cap ? Accent : Gold);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(cap > 0 ? Mathf.Clamp01((float)used / cap) : 0f, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var capT = LGBuild.Label(b, Loc.F("мест {0}/{1}", used, cap), 11, Muted, TextAnchor.MiddleLeft);
            capT.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(180, -37), new Vector2(120, 16));

            bool auto = am.AutoLoad(f.Id);
            var autoB = LGBuild.Button(b, "Auto", auto ? UIManager.DS.BtnSuccess : UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.25f),
                () => { am.SetAutoLoad(f.Id, !auto); SFXManager.Play(Sfx.UiClick); }, auto ? LGIcon.Check : (LGIcon?)null, Loc.T("АВТО"), 10);
            ((RectTransform)autoB.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-122, -8), new Vector2(76, 28));
            TooltipHelper.Attach(autoB.gameObject, Loc.T("<b>Автопогрузка</b>\nКорабль у своей колонии сам забирает готовые карты резерва — сильнейшие первыми"));
            bool canUnload = !moving && sys != null && sys.OwnerId == 0 && sys.HasStarbase && cards.Count > 0;
            var un = LGBuild.Button(b, "Unload", canUnload ? UIManager.DS.BtnPrimary : UIManager.DS.BtnDisabled, new Color(1, 1, 1, 0.25f),
                () => { if (am.Unload(f) > 0) SFXManager.Play(Sfx.DealRemove); }, null, Loc.T("ВЫСАДИТЬ"), 10);
            ((RectTransform)un.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -8), new Vector2(104, 28));
            un.interactable = canUnload;
            TooltipHelper.Attach(un.gameObject, canUnload ? Loc.T("Весь десант корабля — в резерв этой колонии") : Loc.T("Высадить можно у своей колонии со звёздной базой"));

            float h = 52f;
            if (cards.Count == 0)
            {
                var e = LGBuild.Label(b, Loc.T("Трюм пуст"), 12, Muted, TextAnchor.UpperLeft);
                e.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -h), new Vector2(400, 18));
                h += 26f;
            }
            else
            {
                h += Lay(b, cards, h, w, (cv, c) =>
                {
                    cv.OnClick = _ =>
                    {
                        if (moving || sys == null || sys.OwnerId != 0 || !sys.HasStarbase) { SFXManager.Play(Sfx.UiDenied); return; }
                        am.UnloadOne(c);
                        SFXManager.Play(Sfx.DealRemove);
                    };
                }, null) + 4f;
            }
            LGBuild.Height(b.gameObject, h);
        }

        private void ColonyBlock(StarSystem s, List<CardInstance> here, ArmyManager am, List<FleetData> ships)
        {
            var b = Block("Sys" + s.Id, out float w);
            var reserve = new List<CardInstance>();
            var gar = new List<CardInstance>();
            foreach (var c in here) (c.Garrison ? gar : reserve).Add(c);
            var t = LGBuild.Label(b, Loc.F("{0}  <color=#8AA2A8>· резерв {1} · гарнизон {2}</color>", s.Name, reserve.Count, gar.Count), 14, Color.white, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -8), new Vector2(w - 24, 20));
            t.supportRichText = true;

            var docked = new List<FleetData>();
            foreach (var f in ships) if (f.State != FleetState.InHyperlane && f.CurrentSystemId == s.Id) docked.Add(f);
            string hint = reserve.Count == 0 ? null : docked.Count > 0 ? Loc.T("клик по карте резерва — погрузить на корабль") : Loc.T("у колонии нет кораблей — карты ждут погрузки");
            float h = 34f;
            if (hint != null)
            {
                var hl = LGBuild.Label(b, hint, 11, Muted, TextAnchor.UpperLeft);
                hl.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -30), new Vector2(w - 24, 16));
                h = 50f;
            }

            h += Lay(b, reserve, h, w, (cv, c) =>
            {
                cv.OnClick = _ =>
                {
                    foreach (var f in docked)
                        if (am.LoadBlocker(f, c) == null) { am.Load(f, c); SFXManager.Play(Sfx.DealPlace); return; }
                    string why = docked.Count == 0 ? Loc.T("У колонии нет кораблей с десантными местами") : am.LoadBlocker(docked[0], c);
                    NotificationCenter.Show(Loc.T("Не погрузить"), why, NotificationCenter.Kind.Warning, 3f);
                    SFXManager.Play(Sfx.UiDenied);
                };
            }, c => c.Wounded ? Loc.F("РАНЕН {0} дн.", c.WoundDays) : null);
            if (gar.Count > 0)
            {
                var gl = LGBuild.Label(b, Loc.T("ГАРНИЗОН — защищает колонию при штурме"), 10, new Color(0.55f, 0.8f, 1f), TextAnchor.UpperLeft, bold: true);
                gl.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -h - 4), new Vector2(w - 24, 14));
                h += 20f;
                h += Lay(b, gar, h, w, null, c => c.Wounded ? Loc.F("РАНЕН {0} дн.", c.WoundDays) : null);
            }
            LGBuild.Height(b.gameObject, h + 6f);
        }

        // ---------------------------------------------------------------- предпросмотр

        private void HookPreview(CardView cv)
        {
            cv.SetHoverable(true);
            cv.OnHoverIn = ShowPreview;
            cv.OnHoverOut = _ => HidePreview();
        }

        private void ShowPreview(CardView v)
        {
            HidePreview();
            if (v == null || v.Card == null) return;
            _preview = CardView.Create(_previewRoot, v.Card, 0, FactionTraits.Key(0), BigCard, false);
            _preview.Rt.anchoredPosition = Vector2.zero;
            _preview.Punch(0.04f);
            foreach (var g in _preview.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            _previewRoot.SetAsLastSibling();
        }

        private void HidePreview()
        {
            if (_preview != null) Destroy(_preview.gameObject);
            _preview = null;
        }
    }
}
