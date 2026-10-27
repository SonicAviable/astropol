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
    /// Окно «Арсенал» (левое меню, клавиша J) — откуда берутся карты для наземных штурмов. Оформлено как стол боя:
    /// тёмная броня в бронзовой раме, ленты-заголовки, самоцветы.
    ///   • слева — витрина производства выбранной колонии: настоящие карты сеткой (как коллекция в CCG), под каждой цена
    ///     значками и кнопка-самоцвет «ЗАКАЗАТЬ»; недоступные затемнены, причина — в подсказке. Сверху — очередь с прогрессом;
    ///   • справа — войска: корабли с десантными местами-выемками (карты в них), автопогрузка, высадка; резерв и гарнизоны колоний.
    /// Клик по карте резерва — погрузить на корабль у этой колонии, клик по карте на корабле — высадить.
    /// Наведение на любую карту — крупный предпросмотр рядом с курсором.
    /// </summary>
    public sealed class ArsenalWindow : MonoBehaviour
    {
        public static ArsenalWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);
        public static bool IsOpenNow => Instance != null && Instance.IsOpen;

        private static readonly Vector2 ShopCard = new Vector2(150, 210);
        private static readonly Vector2 MiniCard = new Vector2(84, 118);
        private static readonly Vector2 BigCard = new Vector2(280, 392);
        private const float ShopCellH = 286f;

        private GameObject _root;
        private RectTransform _rt, _prodList, _forceList, _previewRoot, _statsRow;
        private Text _colonyName, _colonyInfo;
        private ScrollRect _prodScroll, _forceScroll;
        private CardView _preview;
        private int _colony = -1;
        private string _sig;
        private float _refresh;
        private bool _dirty;

        private static readonly Color Ink = new Color(0.86f, 0.82f, 0.74f);
        private static readonly Color Dim = new Color(0.6f, 0.62f, 0.64f);

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
                float x = lp.x + (lp.x > 0 ? -BigCard.x * 0.5f - 46f : BigCard.x * 0.5f + 46f);
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
            // Сначала корабли с десантом, потом стоящие у колоний
            var am = ArmyManager.Instance;
            l.Sort((a, b) => (am != null ? am.Used(b.Id) : 0).CompareTo(am != null ? am.Used(a.Id) : 0));
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
            foreach (var f in TroopShips()) sb.Append(f.Id).Append('@').Append(f.CurrentSystemId).Append(f.State == FleetState.InHyperlane ? 'h' : 'o').Append(am != null && am.AutoLoad(f.Id) ? 'a' : 'm');
            return sb.ToString();
        }

        // ================================================================ Построение

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            _rt = LGBuild.Rect(host.transform, "ArsenalWindow");
            _rt.Stretch(26, 26, 26, 26);
            _root = _rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.sprite = CardSkin.Spr("panel_plates");
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f / 0.7f;
            bg.color = Color.white;
            bg.raycastTarget = true;
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            // Фон: поверхность планеты сквозь тёмное стекло, тяжёлая виньетка
            var art = LGBuild.Rect(_rt, "Art").gameObject.AddComponent<RawImage>();
            art.rectTransform.Stretch(20, 20, 20, 20);
            art.texture = CardArt.Table(Terrain.Desert);
            art.color = new Color(0.32f, 0.3f, 0.3f, 0.5f);
            art.raycastTarget = false;
            var vign = LGBuild.Panel(_rt, "Vignette", new Color(0, 0, 0, 0.85f));
            vign.rectTransform.Stretch(20, 20, 20, 20);
            vign.sprite = CardSprites.Vignette;
            vign.raycastTarget = false;
            var frame = CardSkin.Sliced(_rt, "Frame", "frame_card", CardSkin.Bronze, 0.7f);
            frame.rectTransform.Stretch();

            // Шапка: крест-мечи в гнезде, заголовок, плашки со сводкой
            var orb = CardSkin.Simple(_rt, "HeadOrb", "orb_red", Color.white);
            orb.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(76, -62), new Vector2(76, 76));
            var ic = LGIcons.Create(orb.rectTransform, LGIcon.Swords, 30, new Color(1f, 0.92f, 0.8f));
            ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30));
            var title = CardSkin.TitleText(_rt, Loc.T("АРСЕНАЛ"), 38, new Color(1f, 0.88f, 0.66f), TextAnchor.MiddleLeft, 2.5f);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(126, -50), new Vector2(500, 46));
            title.Track(5f);
            var sub = LGBuild.Label(_rt, Loc.T("Наземные войска для штурма колоний: производство, десант на кораблях, гарнизоны"), 13, Dim, TextAnchor.MiddleLeft);
            sub.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(128, -82), new Vector2(900, 20));
            _statsRow = LGBuild.Rect(_rt, "Stats");
            _statsRow.At(new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-92, -62), new Vector2(900, 52));

            var close = LGBuild.Rect(_rt, "Close");
            close.At(new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-48, -50), new Vector2(46, 46));
            var cimg = close.gameObject.AddComponent<Image>();
            cimg.sprite = CardSkin.Spr("orb_gray");
            var cbtn = close.gameObject.AddComponent<Button>();
            cbtn.targetGraphic = cimg;
            cbtn.onClick.AddListener(Close);
            var cx = LGIcons.Create(close, LGIcon.Close, 16, new Color(1f, 0.92f, 0.85f));
            cx.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc / J)</color>"));

            // ---- Слева: производство ----
            Column(0f, 0.56f, Loc.T("ПРОИЗВОДСТВО"), out var leftBody);
            var chooser = CardSkin.Sliced(leftBody, "Colony", "panel_dark", Color.white, 0.4f);
            chooser.rectTransform.TopBand(0, 74, 0, 0);
            var prev = OrbButton(chooser.rectTransform, new Vector2(0, 0.5f), new Vector2(38, 0), LGIcon.Back, () => Cycle(-1), Loc.T("Предыдущая колония"));
            var next = OrbButton(chooser.rectTransform, new Vector2(1, 0.5f), new Vector2(-38, 0), LGIcon.Back, () => Cycle(1), Loc.T("Следующая колония"));
            next.localRotation = Quaternion.Euler(0, 0, 180f);
            _colonyName = CardSkin.TitleText(chooser.rectTransform, "", 22, new Color(1f, 0.9f, 0.72f), TextAnchor.UpperCenter, 2f);
            _colonyName.rectTransform.Stretch(80, 0, 80, 10);
            _colonyInfo = LGBuild.Label(chooser.rectTransform, "", 12, Dim, TextAnchor.LowerCenter);
            _colonyInfo.rectTransform.Stretch(80, 11, 80, 0);
            _colonyInfo.supportRichText = true;

            var prodHost = LGBuild.Rect(leftBody, "ProdHost");
            prodHost.Stretch(0, 0, 0, 84);
            _prodList = LGBuild.ScrollList(prodHost, 8f, 6);
            _prodScroll = prodHost.GetComponentInChildren<ScrollRect>();

            // ---- Справа: войска ----
            Column(0.56f, 1f, Loc.T("ВОЙСКА"), out var rightBody);
            _forceList = LGBuild.ScrollList(rightBody, 12f, 6);
            _forceScroll = rightBody.GetComponentInChildren<ScrollRect>();

            _previewRoot = LGBuild.Rect(_rt, "Preview");
            _previewRoot.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, BigCard);
        }

        /// <summary>Колонка: кожаная панель в тонкой бронзовой раме, лента-заголовок сверху.</summary>
        private void Column(float x0, float x1, string head, out RectTransform body)
        {
            var col = CardSkin.Sliced(_rt, head, "panel_leather", new Color(0.8f, 0.8f, 0.8f, 0.92f), 0.35f);
            col.rectTransform.anchorMin = new Vector2(x0, 0);
            col.rectTransform.anchorMax = new Vector2(x1, 1);
            col.rectTransform.offsetMin = new Vector2(x0 == 0f ? 34 : 10, 34);
            col.rectTransform.offsetMax = new Vector2(x1 == 1f ? -34 : -10, -122);
            var rim = CardSkin.Sliced(col.rectTransform, "Rim", "frame_thin", CardSkin.Bronze, 0.4f);
            rim.rectTransform.Stretch(-3, -3, -3, -3);
            var rb = CardSkin.Sliced(col.rectTransform, "Ribbon", "ribbon", Color.white, 0.5f);
            rb.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(300, 34));
            var ht = CardSkin.TitleText(rb.rectTransform, head, 15, new Color(1f, 0.88f, 0.66f), TextAnchor.MiddleCenter, 1.5f);
            ht.rectTransform.Stretch(30, 0, 30, 1);
            ht.Track(3f);
            body = LGBuild.Rect(col.rectTransform, "Body");
            body.Stretch(18, 16, 18, 30);
        }

        private RectTransform OrbButton(RectTransform parent, Vector2 anchor, Vector2 pos, LGIcon icon, System.Action act, string tip, float size = 48f)
        {
            var rt = LGBuild.Rect(parent, "OrbBtn");
            rt.At(anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = CardSkin.Spr("orb_gray");
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors; cb.highlightedColor = new Color(1.35f, 1.3f, 1.15f, 1f); cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f); b.colors = cb;
            b.onClick.AddListener(() => act());
            var ic = LGIcons.Create(rt, icon, size * 0.36f, new Color(1f, 0.94f, 0.84f));
            ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.36f, size * 0.36f));
            if (!string.IsNullOrEmpty(tip)) TooltipHelper.Attach(rt.gameObject, tip);
            return rt;
        }

        /// <summary>Кнопка-самоцвет (как «Закончить ход»): янтарная — можно, серая — нельзя.</summary>
        private Button GemButton(RectTransform parent, string label, bool can, System.Action act, Vector2 anchor, Vector2 pos, Vector2 size, string sprite = null)
        {
            var rt = LGBuild.Rect(parent, label);
            rt.At(anchor, new Vector2(0.5f, 0.5f), pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = CardSkin.Spr(sprite ?? (can ? "btn_end_amber" : "btn_end_gray"));
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 192f / Mathf.Max(20f, size.y);   // концы-полукруги без искажений
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors; cb.highlightedColor = new Color(1.25f, 1.2f, 1.1f, 1f); cb.disabledColor = new Color(0.75f, 0.75f, 0.75f, 1f); b.colors = cb;
            b.interactable = can;
            b.onClick.AddListener(() => act());
            var t = CardSkin.TitleText(rt, label, Mathf.RoundToInt(Mathf.Clamp(size.y * 0.38f, 10f, 16f)), can ? Color.white : new Color(0.75f, 0.75f, 0.75f), TextAnchor.MiddleCenter, 1.5f);
            t.rectTransform.Stretch(8, 0, 8, 1);
            return b;
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
            BuildStats(mine.Count, onShips, reserve, garrison, wounded, am.Upkeep(0));

            float sp = _prodScroll != null ? _prodScroll.verticalNormalizedPosition : 1f;
            float sf = _forceScroll != null ? _forceScroll.verticalNormalizedPosition : 1f;
            Canvas.ForceUpdateCanvases();
            BuildProduction();
            BuildForces();
            Canvas.ForceUpdateCanvases();
            if (_prodScroll != null) _prodScroll.verticalNormalizedPosition = sp;
            if (_forceScroll != null) _forceScroll.verticalNormalizedPosition = sf;
        }

        /// <summary>Сводка в шапке — плашки «значение + подпись».</summary>
        private void BuildStats(int total, int ships, int reserve, int garrison, int wounded, float upkeep)
        {
            LGBuild.Clear(_statsRow);
            var items = new List<(string val, string cap, Color c, string tip)>
            {
                (total.ToString(), Loc.T("КАРТ"), new Color(1f, 0.88f, 0.66f), Loc.T("Всего карт наземных войск")),
                (ships.ToString(), Loc.T("НА КОРАБЛЯХ"), new Color(0.45f, 0.9f, 1f), Loc.T("Десант на кораблях — пойдёт в бой при штурме")),
                (reserve.ToString(), Loc.T("В РЕЗЕРВЕ"), new Color(0.8f, 0.85f, 0.9f), Loc.T("Готовые карты на колониях — ждут погрузки")),
                (garrison.ToString(), Loc.T("ГАРНИЗОНЫ"), new Color(0.55f, 0.85f, 1f), Loc.T("Защитники колоний — не покидают их")),
                (upkeep.ToString("0.#"), Loc.T("ГЕЛИЯ/МЕС"), new Color(1f, 0.82f, 0.35f), Loc.T("Содержание армии в месяц")),
            };
            if (wounded > 0) items.Insert(4, (wounded.ToString(), Loc.T("РАНЕНЫ"), new Color(1f, 0.5f, 0.45f), Loc.T("Раненые не грузятся на корабли, пока не вылечатся")));
            float x = 0f;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var it = items[i];
                var p = CardSkin.Sliced(_statsRow, "Stat", "panel_dark", Color.white, 0.3f);
                p.raycastTarget = true;
                p.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-x, 0), new Vector2(128, 52));
                var v = CardSkin.TitleText(p.rectTransform, it.val, 22, it.c, TextAnchor.UpperCenter, 1.5f);
                v.rectTransform.Stretch(4, 0, 4, 5);
                var cap = CardSkin.TitleText(p.rectTransform, it.cap, 9, Dim, TextAnchor.LowerCenter, 0f);
                cap.rectTransform.Stretch(4, 7, 4, 0);
                cap.Track(1.5f);
                TooltipHelper.Attach(p.gameObject, it.tip);
                x += 136f;
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

        private static string SiteHint(ProdSite s)
        {
            switch (s)
            {
                case ProdSite.City: return Loc.T("Набираются из жителей колонии: нужен городской район, призывников не больше половины жителей");
                case ProdSite.Industry: return Loc.T("Собираются на заводах: нужен промышленный район");
                case ProdSite.Command: return Loc.T("Выдаёт штаб в столице — сразу, без очереди");
                default: return Loc.T("Строятся на месте и защищают колонию при штурме — на корабли не грузятся");
            }
        }

        private void BuildProduction()
        {
            LGBuild.Clear(_prodList);
            var am = ArmyManager.Instance;
            var sys = EmpireStats.GetSystem(_colony);
            if (sys == null || sys.OwnerId != 0)
            {
                _colonyName.text = Loc.T("НЕТ КОЛОНИЙ");
                _colonyInfo.text = Loc.T("Карты производят колонии с жителями");
                return;
            }
            int pop = ArmyManager.SystemPopulation(sys);
            _colonyName.text = sys.Name.ToUpper();
            _colonyInfo.text = Loc.F("жители <color=#F2E2C4>{0}</color>   ·   городских районов <color=#F2E2C4>{1}</color>   ·   промышленных <color=#F2E2C4>{2}</color>   ·   укрепления <color=#F2E2C4>{3}</color>",
                pop, ArmyManager.Districts(sys, DistrictType.Urban), ArmyManager.Districts(sys, DistrictType.Industrial), sys.Fortification);

            // Очередь производства — мини-карты с полоской готовности
            var cm = ConstructionManager.Instance;
            if (cm != null)
            {
                var jobs = new List<ConstructionJob>();
                foreach (var j in cm.Jobs) if (j.Owner == 0 && j.Kind == JobKind.Troop && j.SystemId == sys.Id) jobs.Add(j);
                if (jobs.Count > 0)
                {
                    Header(_prodList, Loc.F("В ПРОИЗВОДСТВЕ · {0}", jobs.Count), CardSkin.Gold, null);
                    QueueRow(jobs);
                }
            }

            string faction = FactionTraits.Key(0);
            var groups = new SortedDictionary<int, List<CardDef>>();
            foreach (var d in CardCatalog.ForFaction(faction))
            {
                if (d.Site == ProdSite.None) continue;
                if (!groups.TryGetValue((int)d.Site, out var l)) groups[(int)d.Site] = l = new List<CardDef>();
                l.Add(d);
            }
            float width = ListWidth(_prodList, 820f);
            int perRow = Mathf.Max(1, Mathf.FloorToInt((width + 14f) / (ShopCard.x + 18f)));
            foreach (var kv in groups)
            {
                var site = (ProdSite)kv.Key;
                kv.Value.Sort((a, b) => a.Cost != b.Cost ? a.Cost.CompareTo(b.Cost) : string.CompareOrdinal(a.Id, b.Id));
                Header(_prodList, SiteHeader(site), new Color(0.95f, 0.8f, 0.55f), SiteHint(site));
                for (int i = 0; i < kv.Value.Count; i += perRow)
                {
                    var row = LGBuild.Rect(_prodList, "ShopRow");
                    LGBuild.Height(row.gameObject, ShopCellH);
                    for (int k = 0; k < perRow && i + k < kv.Value.Count; k++)
                        ShopCell(row, kv.Value[i + k], sys, am, new Vector2(-width * 0.5f + ShopCard.x * 0.5f + k * (ShopCard.x + 18f) + (width - (perRow * (ShopCard.x + 18f) - 18f)) * 0.5f, 0));
                }
            }
        }

        private static float ListWidth(RectTransform list, float fallback)
        {
            float w = list.rect.width - 16f;
            return w > 200f ? w : fallback;
        }

        /// <summary>Заголовок раздела: золотая надпись с разрядкой и тонкая линия, справа — пояснение.</summary>
        private void Header(RectTransform list, string text, Color c, string hint)
        {
            var row = LGBuild.Rect(list, "H");
            LGBuild.Height(row.gameObject, hint != null ? 44 : 32);
            var t = CardSkin.TitleText(row, text, 14, c, TextAnchor.UpperLeft, 1.5f);
            t.rectTransform.Stretch(4, 0, 4, hint != null ? 4 : 8);
            t.Track(2.5f);
            if (hint != null)
            {
                var h = LGBuild.Label(row, hint, 11, Dim, TextAnchor.LowerLeft);
                h.rectTransform.Stretch(4, 6, 4, 0);
            }
            var line = LGBuild.Panel(row, "Line", new Color(c.r, c.g, c.b, 0.35f));
            line.sprite = CardSprites.Beam;
            line.raycastTarget = false;
            line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 2);
        }

        private void QueueRow(List<ConstructionJob> jobs)
        {
            var row = LGBuild.Rect(_prodList, "Queue");
            LGBuild.Height(row.gameObject, MiniCard.y + 34);
            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i];
                var d = CardCatalog.Get(j.DesignId);
                if (d == null) continue;
                float x = 8 + MiniCard.x * 0.5f + i * (MiniCard.x + 14f);
                var cv = CardView.Create(row, new BCard(d), 0, FactionTraits.Key(0), MiniCard, true);
                cv.Rt.anchorMin = cv.Rt.anchorMax = new Vector2(0, 1);
                cv.Rt.anchoredPosition = new Vector2(x, -MiniCard.y * 0.5f - 2);
                cv.SetAlpha(0.55f + 0.45f * j.Progress);
                HookPreview(cv);
                var track = LGBuild.Panel(row, "Track", new Color(0, 0, 0, 0.7f));
                track.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(x, -MiniCard.y - 12), new Vector2(MiniCard.x - 6, 6));
                var fill = LGBuild.Panel(track.rectTransform, "Fill", CardSkin.Gold);
                fill.rectTransform.anchorMin = Vector2.zero;
                fill.rectTransform.anchorMax = new Vector2(j.Progress, 1);
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                var days = CardSkin.TitleText(row, Loc.F("{0:0} дн.", Mathf.Ceil(j.DaysLeft)), 11, Ink, TextAnchor.MiddleCenter, 1f);
                days.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(x, -MiniCard.y - 26), new Vector2(MiniCard.x, 16));
            }
        }

        /// <summary>Ячейка витрины: полная карта, цена значками, кнопка-самоцвет «ЗАКАЗАТЬ».</summary>
        private void ShopCell(RectTransform row, CardDef d, StarSystem sys, ArmyManager am, Vector2 pos)
        {
            string blocker = am.ProduceBlocker(0, d, d.Site == ProdSite.Command ? null : sys);
            ArmyManager.Cost(d, out float e, out float m, out float a, out float inf);
            var eco = EconomyManager.Instance;
            bool afford = eco != null && eco.EnergyCredits >= e && eco.Minerals >= m && eco.Alloys >= a && eco.Influence >= inf;
            bool can = blocker == null && afford;

            var cell = LGBuild.Rect(row, d.Id);
            cell.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(pos.x, 0), new Vector2(ShopCard.x + 10, ShopCellH));

            var cv = CardView.Create(cell, new BCard(d), 0, FactionTraits.Key(0), ShopCard, false);
            cv.Rt.anchorMin = cv.Rt.anchorMax = new Vector2(0.5f, 1f);
            cv.Rt.anchoredPosition = new Vector2(0, -ShopCard.y * 0.5f - 6);
            cv.SetHoverable(true);
            cv.HoverLift = 8f;
            cv.HoverScale = 0.05f;
            cv.Playable = can;
            HookPreview(cv);
            if (blocker != null) cv.SetAlpha(0.5f);

            // Цена: значки ресурсов в строку, по центру
            var price = LGBuild.Rect(cell, "Price");
            price.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -ShopCard.y - 22), new Vector2(ShopCard.x + 30, 18));
            var parts = new List<(LGIcon ic, string v, bool ok, Color c)>();
            if (e > 0) parts.Add((LGIcon.Energy, e.ToString("0"), eco != null && eco.EnergyCredits >= e, new Color(1f, 0.85f, 0.3f)));
            if (m > 0) parts.Add((LGIcon.Minerals, m.ToString("0"), eco != null && eco.Minerals >= m, new Color(0.55f, 0.85f, 1f)));
            if (a > 0) parts.Add((LGIcon.Alloys, a.ToString("0"), eco != null && eco.Alloys >= a, new Color(0.85f, 0.7f, 1f)));
            if (inf > 0) parts.Add((LGIcon.Influence, inf.ToString("0"), eco != null && eco.Influence >= inf, new Color(0.75f, 0.6f, 1f)));
            parts.Add((LGIcon.Clock, d.Days > 0 && d.Site != ProdSite.Command ? Loc.F("{0}д", d.Days) : Loc.T("сразу"), true, Dim));
            float w = 0f;
            foreach (var p in parts) w += 14 + p.v.Length * 7.5f + 8;
            float px = -w * 0.5f;
            foreach (var p in parts)
            {
                var i = LGIcons.Create(price, p.ic, 12, p.c);
                i.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(px, 0), new Vector2(12, 12));
                var t = CardSkin.TitleText(price, p.v, 12, p.ok ? Ink : CardSkin.Red, TextAnchor.MiddleLeft, 1f);
                t.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(px + 15, 0), new Vector2(60, 16));
                px += 14 + p.v.Length * 7.5f + 8;
            }

            var btn = GemButton(cell, can ? Loc.T("ЗАКАЗАТЬ") : blocker != null ? Loc.T("НЕДОСТУПНО") : Loc.T("НЕ ХВАТАЕТ"), can, () => Order(d),
                new Vector2(0.5f, 1f), new Vector2(0, -ShopCard.y - 52), new Vector2(ShopCard.x, 36));
            string tip = $"<b>{d.Name}</b>\n{d.Text}";
            if (d.Upkeep > 0f) tip += Loc.F("\n<color=#8AA2A8>Содержание: {0:0.#} гелия в месяц</color>", d.Upkeep);
            if (d.Size > 0) tip += Loc.F("\n<color=#8AA2A8>Мест на корабле: {0}</color>", d.Size);
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
            Header(_forceList, Loc.F("ДЕСАНТ НА КОРАБЛЯХ · {0}", ships.Count), CardSkin.Gold,
                Loc.T("Корвет берёт 1 место, фрегат — 2, эсминец — 4. Приказы места не занимают"));
            if (ships.Count == 0) Note(_forceList, Loc.T("Нет военных кораблей — постройте корвет, фрегат или эсминец"));
            foreach (var f in ships) FleetBlock(f, am);

            Header(_forceList, Loc.T("КОЛОНИИ: РЕЗЕРВ И ГАРНИЗОНЫ"), CardSkin.Gold, Loc.T("Клик по карте резерва — погрузить на корабль у этой колонии"));
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
            var t = LGBuild.Label(row, text, 12, Dim, TextAnchor.MiddleLeft, wrap: true);
            t.rectTransform.Stretch(6, 0, 6, 0);
            LGBuild.Height(row.gameObject, 34);
        }

        private RectTransform Block(string name, Color rim, out float width)
        {
            var b = CardSkin.Sliced(_forceList, name, "panel_dark", Color.white, 0.4f);
            var r = CardSkin.Sliced(b.rectTransform, "Rim", "frame_thin", rim, 0.35f);
            r.rectTransform.Stretch(-2, -2, -2, -2);
            width = ListWidth(_forceList, 640f);
            return b.rectTransform;
        }

        /// <summary>Места под карты: выемки по числу мест, карты ложатся в них по порядку.</summary>
        private float Slots(RectTransform parent, List<CardInstance> cards, int slots, float top, float width,
                            System.Action<CardView, CardInstance> hook, System.Func<CardInstance, string> status)
        {
            int perRow = Mathf.Max(1, Mathf.FloorToInt((width - 24f) / (MiniCard.x + 10f)));
            int n = Mathf.Max(slots, cards.Count);
            for (int i = 0; i < n; i++)
            {
                var pos = new Vector2(16 + MiniCard.x * 0.5f + (i % perRow) * (MiniCard.x + 10f), -top - MiniCard.y * 0.5f - (i / perRow) * (MiniCard.y + 12f));
                if (i >= cards.Count)
                {
                    var sl = CardSkin.Simple(parent, "Slot", "slot", Color.white);
                    sl.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), pos, MiniCard);
                    continue;
                }
                var c = cards[i];
                if (c.D == null) continue;
                var cv = CardView.Create(parent, new BCard(c.D, c.Id, c.Stars), 0, FactionTraits.Key(0), MiniCard, true);
                cv.Rt.anchorMin = cv.Rt.anchorMax = new Vector2(0, 1);
                cv.Rt.anchoredPosition = pos;
                string st = status?.Invoke(c);
                if (st != null) cv.SetStatus(st, new Color(1f, 0.55f, 0.45f));
                if (c.Wounded) cv.SetAlpha(0.6f);
                HookPreview(cv);
                cv.HoverLift = 6f;
                cv.HoverScale = 0.08f;
                hook?.Invoke(cv, c);
            }
            int rows = n == 0 ? 0 : (n + perRow - 1) / perRow;
            return rows * (MiniCard.y + 12f);
        }

        private void FleetBlock(FleetData f, ArmyManager am)
        {
            var cards = am.OnFleet(f.Id);
            int used = am.Used(f.Id), cap = ArmyManager.Capacity(f);
            var b = Block("Fleet" + f.Id, used > 0 ? CardSkin.Gold : CardSkin.Steel, out float w);
            var sys = EmpireStats.GetSystem(f.CurrentSystemId);
            bool moving = f.State == FleetState.InHyperlane;
            string where = moving ? Loc.T("в пути") : sys != null ? sys.Name : "?";

            var ic = CardSkin.Simple(b, "ShipOrb", used > 0 ? "orb_teal" : "orb_gray", Color.white);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(34, -32), new Vector2(42, 42));
            var si = LGIcons.Create(ic.rectTransform, LGIcon.Ship, 18, Color.white);
            si.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 18));
            var t = CardSkin.TitleText(b, f.Name, 16, new Color(1f, 0.92f, 0.78f), TextAnchor.MiddleLeft, 1.5f);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(64, -22), new Vector2(w - 330, 22));
            var loc = LGBuild.Label(b, Loc.F("{0}   ·   мест <b>{1}/{2}</b>", where, used, cap), 12, Dim, TextAnchor.MiddleLeft);
            loc.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(64, -43), new Vector2(w - 330, 18));
            loc.supportRichText = true;

            bool auto = am.AutoLoad(f.Id);
            var ab = GemButton(b, auto ? Loc.T("АВТО: ВКЛ") : Loc.T("АВТО: ВЫКЛ"), true, () => { am.SetAutoLoad(f.Id, !auto); SFXManager.Play(Sfx.UiClick); },
                new Vector2(1, 1), new Vector2(-206, -32), new Vector2(128, 34), auto ? "btn_end_green" : "btn_end_gray");
            TooltipHelper.Attach(ab.gameObject, Loc.T("<b>Автопогрузка</b>\nКорабль у своей колонии сам забирает готовые карты резерва — сильнейшие первыми"));
            bool canUnload = !moving && sys != null && sys.OwnerId == 0 && sys.HasStarbase && cards.Count > 0;
            var un = GemButton(b, Loc.T("ВЫСАДИТЬ"), canUnload, () => { if (am.Unload(f) > 0) SFXManager.Play(Sfx.DealRemove); },
                new Vector2(1, 1), new Vector2(-72, -32), new Vector2(124, 34));
            TooltipHelper.Attach(un.gameObject, canUnload ? Loc.T("Весь десант корабля — в резерв этой колонии") : Loc.T("Высадить можно у своей колонии со звёздной базой"));

            float h = 62f;
            h += Slots(b, cards, Mathf.Max(0, cap - used + cards.Count), h, w, (cv, c) =>
            {
                cv.OnClick = _ =>
                {
                    if (moving || sys == null || sys.OwnerId != 0 || !sys.HasStarbase) { SFXManager.Play(Sfx.UiDenied); return; }
                    am.UnloadOne(c);
                    SFXManager.Play(Sfx.DealRemove);
                };
            }, null) + 8f;
            LGBuild.Height(b.gameObject, h);
        }

        private void ColonyBlock(StarSystem s, List<CardInstance> here, ArmyManager am, List<FleetData> ships)
        {
            var b = Block("Sys" + s.Id, new Color(0.55f, 0.85f, 1f), out float w);
            var reserve = new List<CardInstance>();
            var gar = new List<CardInstance>();
            foreach (var c in here) (c.Garrison ? gar : reserve).Add(c);

            var ic = CardSkin.Simple(b, "ColOrb", "orb_gray", Color.white);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(34, -32), new Vector2(42, 42));
            var pi = LGIcons.Create(ic.rectTransform, LGIcon.Planet, 18, Color.white);
            pi.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 18));
            var t = CardSkin.TitleText(b, s.Name, 16, new Color(1f, 0.92f, 0.78f), TextAnchor.MiddleLeft, 1.5f);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(64, -22), new Vector2(w - 90, 22));

            var docked = new List<FleetData>();
            foreach (var f in ships) if (f.State != FleetState.InHyperlane && f.CurrentSystemId == s.Id) docked.Add(f);
            string hint = Loc.F("резерв {0} · гарнизон {1}", reserve.Count, gar.Count) +
                          (reserve.Count == 0 ? "" : docked.Count > 0 ? Loc.T("   ·   клик по карте — погрузить") : Loc.T("   ·   у колонии нет кораблей"));
            var hl = LGBuild.Label(b, hint, 12, Dim, TextAnchor.MiddleLeft);
            hl.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(64, -43), new Vector2(w - 90, 18));

            float h = 62f;
            h += Slots(b, reserve, 0, h, w, (cv, c) =>
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
                var gl = CardSkin.TitleText(b, Loc.T("ГАРНИЗОН — ЗАЩИЩАЕТ КОЛОНИЮ ПРИ ШТУРМЕ"), 11, new Color(0.55f, 0.85f, 1f), TextAnchor.MiddleLeft, 1f);
                gl.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(18, -h - 10), new Vector2(w - 36, 16));
                gl.Track(1.5f);
                h += 22f;
                h += Slots(b, gar, 0, h, w, null, c => c.Wounded ? Loc.F("РАНЕН {0} дн.", c.WoundDays) : null);
            }
            LGBuild.Height(b.gameObject, h + 8f);
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
