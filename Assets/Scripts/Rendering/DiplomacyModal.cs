using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Комната переговоров (по референсу дизайна):
    ///   • в центре — круглый голостол-платформа, на нём голограммами стоят правители: вы — слева, собеседник — справа;
    ///     над столом светится проекция сделки — что лежит на столе с каждой стороны;
    ///   • вдоль переднего края стола — вкладки действий: война/мир, пакт, договоры, коалиция;
    ///   • слева сверху — империя собеседника и сравнение сил, слева снизу — «Статус отношений»
    ///     (кольца доверия и влияния, действующие соглашения);
    ///   • справа — сетки того, что могут дать они и вы, и шкала «Шанс согласия»;
    ///   • внизу по центру — «Диалог»: портрет собеседника, его реплика и варианты ответа.
    /// Предложения ИИ (обмен, договоры, требование дани, мир, пакт) открываются здесь же.
    /// </summary>
    public class DiplomacyModal : MonoBehaviour
    {
        public static DiplomacyModal Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Green => UIManager.DS.Green;
        private static Color Red => UIManager.DS.Red;
        private static readonly Color PanelBg = new Color(0.012f, 0.045f, 0.060f, 0.84f);
        private static readonly Color HeadBg = new Color(0.05f, 0.17f, 0.20f, 0.80f);
        private static readonly Color CellBg = new Color(0.030f, 0.075f, 0.095f, 0.94f);
        private static readonly Color RowBg = new Color(0.040f, 0.090f, 0.110f, 0.92f);
        private static readonly Color SlotBg = new Color(0.035f, 0.100f, 0.120f, 0.96f);
        private static readonly Color MetalDark = new Color(0.025f, 0.040f, 0.050f, 0.97f);
        private static readonly Color CoalitionTone = new Color(1f, 0.62f, 0.36f);

        // Раскладка (в единицах эталонного экрана 1920×1080)
        private const float SideW = 360f, Edge = 20f;
        private const float TableY = 350f, TableA = 490f, TableB = 118f, TableSkirt = 36f;
        private const float LeaderX = 410f, LeaderW = 400f, LeaderH = 600f;
        private const float DialogW = 780f, DialogH = 184f, DealW = 420f, DealH = 250f;
        private const float CellSize = 76f, CellGap = 8f;
        private const int ChanceSegs = 16, SlotCount = 7;

        private Canvas _host;
        private GameObject _root;
        private TechTreeBackdrop _backdrop;
        private RawImage _scene;
        private Image _sceneShade, _leaderGlow, _playerGlow;
        private RectTransform _portraitHost, _playerHost, _infoLeft, _agreements, _tabs;
        // Голостол: крышка, кромки, вращающиеся кольца, площадки и конусы проекторов, луч к проекции сделки
        private RectTransform _tableSpinA, _tableSpinB;
        private Image _padAI, _padPlayer, _coneAI, _conePlayer, _tableGlow, _tableRim, _tableBaseRim, _tableRingA, _tableRingB, _beam;
        private string _playerKey, _portraitKey;
        // Гербы: у названия империи и «проекции» на площадках под правителями
        private Image _empireCrest, _padCrestAI, _padCrestPlayer;
        // Статус отношений: кольца доверия и влияния
        private Image _gaugeTrust, _gaugeInfluence, _trustHit;
        private Text _gaugeTrustVal, _gaugeTrustSub, _gaugeInfluenceVal, _gaugeInfluenceSub;
        // Шанс согласия — сегментная шкала
        private Image[] _chanceSegs;
        private Text _chanceVal;
        // Диалог
        private RawImage _dialogPortrait;
        private Text _speechName, _speechText, _speechState;
        private Image _speechStateBg;
        // Сделка
        private RectTransform _aiGrid, _playerGrid, _tableAI, _tablePlayer, _centerPanel;
        private Text _empireTitle, _aiGridTitle, _reaction, _fee, _tableTitle, _colAI;
        private Button _btnBalance, _btnBack, _btnPropose;
        private Text _lblBalance, _lblBack, _lblPropose;
        private Image _imgBalance, _imgBack, _imgPropose;

        /// <summary>Вкладка действия на кромке голостола; содержимое меняется от состояния отношений.</summary>
        private sealed class Slot
        {
            public Button Btn;
            public Image Bg, Icon;
            public Text Label;
            public System.Action Act;
        }
        private readonly List<Slot> _slots = new List<Slot>();

        // Выбор систем для стола переговоров
        private GameObject _picker;
        private RectTransform _pickerList;
        private Text _pickerTitle;
        private bool _pickerFromAI;

        private int _owner = -1;
        private Deal _deal = new Deal();
        private bool _viewingOffer;
        private string _speechFull = "";
        private float _speechShown;
        private string _signature = "";
        private float _refreshTimer;
        private bool _dirty;
        private float _confirmUntil;
        private string _confirmAction;
        private float _holoT;

        private AIEmpireManager Current => AIEmpireManager.For(_owner) ?? AIEmpireManager.Instance;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            AIEmpireManager.OnDiplomacyChanged += MarkDirty;
        }

        private void OnDestroy()
        {
            AIEmpireManager.OnDiplomacyChanged -= MarkDirty;
            if (Instance == this) Instance = null;
        }

        private void MarkDirty() => _dirty = true;

        public void BindHost(Canvas modalCanvas) { _host = modalCanvas; }

        public void Open() => Open(-1);

        /// <summary>Открыть переговоры с конкретной империей (-1 — с последней).</summary>
        public void Open(int owner)
        {
            if (owner > 0) _owner = owner;
            if (Current == null) return;
            // До первого контакта империи в дипломатии нет — открываем первую знакомую
            if (!Contacts.PlayerMet(Current.OwnerId))
            {
                int known = -1;
                foreach (var a in AIEmpireManager.All) if (Contacts.PlayerMet(a.OwnerId)) { known = a.OwnerId; break; }
                if (known < 0)
                {
                    SFXManager.Play(Sfx.UiDenied);
                    NotificationCenter.Show(Loc.T("Контактов нет"),
                        Loc.T("Мы ещё не встретили другие цивилизации. Исследуйте галактику — контакт случится, когда наши корабли или границы увидят чужую империю"),
                        NotificationCenter.Kind.Info, 6f);
                    return;
                }
                _owner = known;
            }
            _owner = Current.OwnerId;
            if (_host == null)
            {
                var modal = GameObject.Find("ModalCanvas");
                if (modal != null) _host = modal.GetComponent<Canvas>();
            }
            if (_host == null) return;
            if (_root == null) Build();

            bool wasOpen = IsOpen;
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            if (!wasOpen)
            {
                UIManager.Instance?.SetOverlayMode(true);
                MapModeController.HideGlobal();
                SFXManager.Play(Sfx.WindowOpen);
            }
            _backdrop.SetActive(true);
            SwitchTo(_owner);
        }

        /// <summary>Первый контакт: правитель выходит на связь с особым приветствием.</summary>
        public void OpenFirstContact(int owner)
        {
            Open(owner);
            if (!IsOpen || _owner != owner) return;
            var ai = Current;
            if (ai != null && ai.PendingDeal == null)
                Say(DiplomacyLines.Get(LeaderPortraits.ForFaction(ai.Faction), DiplomacyLines.Kind.FirstContact));
        }

        public void Close()
        {
            if (_root == null || !IsOpen) return;
            if (_picker != null) _picker.SetActive(false);
            LG.Hide(_root);
            _backdrop.SetActive(false);
            UIManager.Instance?.SetOverlayMode(false);
            MapModeController.ShowGlobal();
            SFXManager.Play(Sfx.WindowClose);
        }

        private void SwitchTo(int owner)
        {
            _owner = owner;
            var ai = Current;
            if (ai == null) return;
            _deal = new Deal();
            _viewingOffer = ai.PendingDeal != null;
            _confirmAction = null;
            if (_picker != null) _picker.SetActive(false);
            Color ec = ai.AIEmpireColor;
            _backdrop.SetColors(ec, Color.Lerp(ec, new Color(0.3f, 0.4f, 1f), 0.5f));
            ApplyScene(ai);
            _leaderGlow.color = new Color(ec.r, ec.g, ec.b, 0.20f);
            Color oc = FleetIndicator.OwnColor;
            _playerGlow.color = new Color(oc.r, oc.g, oc.b, 0.16f);
            _padAI.color = new Color(ec.r, ec.g, ec.b, 0.85f);
            _padPlayer.color = new Color(oc.r, oc.g, oc.b, 0.85f);
            _coneAI.color = new Color(ec.r, ec.g, ec.b, 0.12f);
            _conePlayer.color = new Color(oc.r, oc.g, oc.b, 0.12f);
            Color mix = Color.Lerp(Cyan, Color.Lerp(oc, ec, 0.5f), 0.35f);
            _tableGlow.color = new Color(mix.r, mix.g, mix.b, 0.30f);
            _tableRim.color = new Color(mix.r, mix.g, mix.b, 0.90f);
            _tableBaseRim.color = new Color(mix.r, mix.g, mix.b, 0.35f);
            _tableRingA.color = new Color(mix.r, mix.g, mix.b, 0.60f);
            _tableRingB.color = new Color(mix.r, mix.g, mix.b, 0.40f);
            _beam.color = new Color(mix.r, mix.g, mix.b, 0.22f);
            SetCrest(_empireCrest, FactionEmblems.ForFaction(ai.Faction), Color.Lerp(ec, Color.white, 0.25f));
            SetCrest(_padCrestAI, FactionEmblems.ForFaction(ai.Faction), new Color(ec.r, ec.g, ec.b, 0.75f));
            SetCrest(_padCrestPlayer, FactionEmblems.ForOwner(0), new Color(oc.r, oc.g, oc.b, 0.75f));
            BuildPortrait(ai);
            BuildPlayerPortrait();
            var leader = LeaderPortraits.ForFaction(ai.Faction);
            _dialogPortrait.texture = leader != null ? leader.Texture : null;
            _dialogPortrait.enabled = _dialogPortrait.texture != null;
            _dialogPortrait.uvRect = LeaderPortraits.FaceUv(leader, 0.44f);   // лицо крупно, без растяжения (окно квадратное)
            if (_viewingOffer) Say(DiplomacyLines.Get(leader, DiplomacyLines.OfferLine(ai.PendingOffer, ai.PendingDeal)));
            else Say(DiplomacyLines.Get(leader, DiplomacyLines.GreetingFor(ai)));
            RefreshAll();
        }

        private void Update()
        {
            if (!IsOpen) return;

            // Голостол вращается, площадки чуть «дышат»
            _holoT += Time.unscaledDeltaTime;
            if (_tableSpinA != null) _tableSpinA.localRotation = Quaternion.Euler(0, 0, _holoT * 10f);
            if (_tableSpinB != null) _tableSpinB.localRotation = Quaternion.Euler(0, 0, -_holoT * 18f);
            float breathe = 1f + Mathf.Sin(_holoT * 2.2f) * 0.03f;
            if (_padAI != null) _padAI.rectTransform.localScale = new Vector3(breathe, breathe, 1f);
            if (_padPlayer != null) _padPlayer.rectTransform.localScale = new Vector3(breathe, breathe, 1f);

            // Реплика печатается
            if (_speechShown < _speechFull.Length)
            {
                int before = Mathf.FloorToInt(_speechShown);
                _speechShown = Mathf.Min(_speechFull.Length, _speechShown + Time.unscaledDeltaTime * 55f);
                int now = Mathf.FloorToInt(_speechShown);
                _speechText.text = _speechFull.Substring(0, now);
                // Телетайп: тихий тик на каждую третью букву (пробелы молчат)
                if (now / 3 != before / 3 && now > 0 && !char.IsWhiteSpace(_speechFull[now - 1])) SFXManager.Play(Sfx.TextTick);
            }

            if (_confirmAction != null && Time.unscaledTime > _confirmUntil) { _confirmAction = null; RefreshActions(); }

            _refreshTimer -= Time.unscaledDeltaTime;
            if (!_dirty && _refreshTimer > 0f) return;
            _refreshTimer = 1f;
            var ai = Current;
            if (ai == null) return;
            string sig = Signature(ai);
            if (!_dirty && sig == _signature) return;
            _dirty = false;
            // Предложение ИИ пропало (истекло/исполнено) — возвращаемся к обычному столу
            if (_viewingOffer && ai.PendingDeal == null) _viewingOffer = false;
            if (!_viewingOffer && ai.PendingDeal != null && _deal.IsEmpty)
            {
                _viewingOffer = true;
                Say(DiplomacyLines.Get(LeaderPortraits.ForFaction(ai.Faction), DiplomacyLines.OfferLine(ai.PendingOffer, ai.PendingDeal)));
            }
            RefreshAll();
        }

        private string Signature(AIEmpireManager ai)
        {
            var eco = EconomyManager.Instance;
            return $"{ai.OwnerId}|{Mathf.RoundToInt(ai.Opinion)}|{ai.AtWar}|{ai.HasPact}|{ai.HasAlliance}|{Mathf.RoundToInt(Reputation.Value)}|{ai.TruceDays / 30}|{ai.PendingOffer}|{ai.PendingOfferDays / 10}|" +
                   $"{ai.Agreements.Count}|{ai.IsEliminated}|{(int)ai.EnergyCredits / 25}|{(int)ai.Minerals / 25}|{(int)ai.Alloys / 25}|{(int)ai.Influence / 10}|" +
                   (eco != null ? $"{(int)eco.EnergyCredits / 25}|{(int)eco.Minerals / 25}|{(int)eco.Alloys / 25}|{(int)eco.Influence / 10}" : "");
        }

        private void Say(string line)
        {
            _speechFull = line ?? "";
            _speechShown = 0f;
            if (_speechText != null) _speechText.text = "";
        }

        // ================================================================ Каркас

        private void Build()
        {
            var rt = LGBuild.Rect(_host.transform, "DiplomacyModal");
            rt.Stretch();
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.008f, 0.014f, 0.022f, 1f);
            bg.raycastTarget = true;
            LG.Ignore(_root, includeChildren: false);
            LG.Motion(_root, LGAppear.Kind.Fade);

            var bd = LGBuild.Rect(rt, "Backdrop");
            bd.Stretch();
            _backdrop = bd.gameObject.AddComponent<TechTreeBackdrop>();
            _backdrop.Initialize(bd);

            // Сцена империи (зал переговоров) — Resources/Diplomacy/bg_<ключ правителя>; если нет — процедурный фон
            var sc = LGBuild.Rect(rt, "Scene");
            sc.Stretch();
            _scene = sc.gameObject.AddComponent<RawImage>();
            _scene.raycastTarget = false;
            LG.Ignore(sc.gameObject);
            _sceneShade = LGBuild.Panel(rt, "SceneShade", new Color(0.005f, 0.012f, 0.020f, 0.45f));
            _sceneShade.rectTransform.Stretch();
            LG.Ignore(_sceneShade.gameObject);

            // Свет за фигурами
            _playerGlow = Glow(rt, "PlayerGlow", new Vector2(-LeaderX, -430f), 820f);
            _leaderGlow = Glow(rt, "LeaderGlow", new Vector2(LeaderX, -430f), 820f);

            BuildTable(rt);

            // Правители стоят на столе: конус света проектора, фигура, площадка поверх её нижнего края
            _conePlayer = Holo(rt, "ConePlayer", ConeSprite(), new Vector2(-LeaderX, TableY + 6f), new Vector2(300f, 470f), new Vector2(0.5f, 0f));
            _coneAI = Holo(rt, "ConeAI", ConeSprite(), new Vector2(LeaderX, TableY + 6f), new Vector2(300f, 470f), new Vector2(0.5f, 0f));
            _playerHost = LeaderHost(rt, "PlayerLeader", -LeaderX);
            _playerHost.localScale = new Vector3(-1f, 1f, 1f);   // лицом к собеседнику
            _portraitHost = LeaderHost(rt, "Leader", LeaderX);
            _padPlayer = Holo(rt, "PadPlayer", PadSprite(), new Vector2(-LeaderX, TableY + 10f), new Vector2(250f, 54f), new Vector2(0.5f, 0.5f));
            _padAI = Holo(rt, "PadAI", PadSprite(), new Vector2(LeaderX, TableY + 10f), new Vector2(250f, 54f), new Vector2(0.5f, 0.5f));
            // Гербы лежат на площадках (сплюснуты, как и стол, — в перспективе)
            _padCrestPlayer = Holo(rt, "PadCrestPlayer", null, new Vector2(-LeaderX, TableY + 10f), new Vector2(118f, 30f), new Vector2(0.5f, 0.5f));
            _padCrestAI = Holo(rt, "PadCrestAI", null, new Vector2(LeaderX, TableY + 10f), new Vector2(118f, 30f), new Vector2(0.5f, 0.5f));

            BuildDealHolo(rt);
            BuildEmpirePanel(rt);
            BuildStatus(rt);
            BuildGrids(rt);
            BuildSystemPicker(rt);
            BuildDialog(rt);
            BuildActions(rt);

            // Вкладки других империй — сверху по центру
            _tabs = LGBuild.Rect(rt, "Tabs");
            _tabs.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(720f, 54f));

            var close = LGBuild.Button(rt, "Close", UIManager.DS.BtnNeutral, new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Edge, -16f), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc)</color>"));

            _root.SetActive(false);
        }

        private static void SetCrest(Image img, Sprite crest, Color c)
        {
            if (img == null) return;
            img.sprite = crest;
            img.color = c;
            img.enabled = crest != null;
        }

        private static RectTransform LeaderHost(RectTransform rt, string name, float x)
        {
            var h = LGBuild.Rect(rt, name);
            h.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, TableY + 2f), new Vector2(LeaderW, LeaderH));
            return h;
        }

        /// <summary>
        /// Голостол-платформа в перспективе: нижний эллипс и боковина дают толщину «шайбы», крышка их перекрывает;
        /// по крышке — свечение, светящаяся кромка и два кольца, вращающиеся навстречу.
        /// </summary>
        private void BuildTable(RectTransform rt)
        {
            float w = TableA * 2f, h = TableB * 2f;
            Ellipse(rt, "TableBase", DiscSprite(), new Vector2(0f, TableY - TableSkirt), new Vector2(w, h), MetalDark);
            var side = LGBuild.Panel(rt, "TableSide", MetalDark);
            side.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, TableY - TableSkirt), new Vector2(w, TableSkirt));
            LG.Ignore(side.gameObject);
            _tableBaseRim = Ellipse(rt, "TableBaseRim", RimSprite(), new Vector2(0f, TableY - TableSkirt), new Vector2(w, h), Color.clear);
            Ellipse(rt, "TableTop", DiscSprite(), new Vector2(0f, TableY), new Vector2(w, h), new Color(0.030f, 0.085f, 0.100f, 0.97f));
            _tableGlow = Ellipse(rt, "TableGlow", FactionSelectScreen.RadialSprite(), new Vector2(0f, TableY), new Vector2(w * 0.96f, h * 0.96f), Color.clear);
            _tableRim = Ellipse(rt, "TableRim", RimSprite(), new Vector2(0f, TableY), new Vector2(w, h), Color.clear);

            // Вращающиеся кольца — круги в сплюснутом контейнере (так вращение не «плывёт»)
            var table = LGBuild.Rect(rt, "HoloTable");
            table.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, TableY), new Vector2(w, w));
            table.localScale = new Vector3(1f, h / w, 1f);
            _tableSpinA = LGBuild.Rect(table, "SpinA");
            _tableSpinA.Stretch();
            _tableRingA = Holo(_tableSpinA, "RingA", DashRingSprite(), Vector2.zero, new Vector2(w * 0.84f, w * 0.84f), new Vector2(0.5f, 0.5f), true);
            _tableSpinB = LGBuild.Rect(table, "SpinB");
            _tableSpinB.Stretch();
            _tableRingB = Holo(_tableSpinB, "RingB", PadSprite(), Vector2.zero, new Vector2(w * 0.46f, w * 0.46f), new Vector2(0.5f, 0.5f), true);
        }

        /// <summary>Проекция сделки над столом: что кладут на стол они и вы.</summary>
        private void BuildDealHolo(RectTransform rt)
        {
            _beam = Holo(rt, "Beam", ConeSprite(), new Vector2(0f, TableY - 10f), new Vector2(DealW + 40f, 170f), new Vector2(0.5f, 0f));

            var p = LGBuild.Panel(rt, "Deal", new Color(0.02f, 0.10f, 0.12f, 0.62f), raycast: true);
            _centerPanel = p.rectTransform;
            _centerPanel.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, TableY + 136f), new Vector2(DealW, DealH));
            LG.Glass(p.gameObject, 14f).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f));
            _tableTitle = LGBuild.Label(_centerPanel, Loc.T("НА СТОЛЕ ПЕРЕГОВОРОВ"), 13, Primary, TextAnchor.MiddleCenter, bold: true);
            _tableTitle.rectTransform.TopBand(8, 22, 14, 14);
            _tableTitle.Track(2f);
            Line(_centerPanel, 34f, 0.3f, 12f);

            var cols = LGBuild.Rect(_centerPanel, "Cols");
            cols.Stretch(10, 10, 10, 42);
            var cl = LGBuild.Rect(cols, "ColAI");
            cl.Column(0f, 0.5f, 0, 5);
            var cr = LGBuild.Rect(cols, "ColPlayer");
            cr.Column(0.5f, 1f, 5, 0);
            _colAI = ColumnHead(cl, Loc.T("ОНИ ДАЮТ"));
            ColumnHead(cr, Loc.T("ВЫ ДАЁТЕ")).color = Gold;
            var divider = LGBuild.Panel(cols, "Divider", new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f));
            divider.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            divider.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            divider.rectTransform.sizeDelta = new Vector2(1f, 0f);
            LG.Ignore(divider.gameObject);
            var clh = LGBuild.Rect(cl, "List");
            clh.Stretch(0, 0, 0, 22);
            _tableAI = LGBuild.ScrollList(clh, 4f, 2);
            var crh = LGBuild.Rect(cr, "List");
            crh.Stretch(0, 0, 0, 22);
            _tablePlayer = LGBuild.ScrollList(crh, 4f, 2);
        }

        /// <summary>Левый верх: империя собеседника, сравнение сил, состояние и соседи.</summary>
        private void BuildEmpirePanel(RectTransform rt)
        {
            RectTransform p;
            (p, _empireTitle) = FramedPanel(rt, "Empire", new Vector2(0, 1), new Vector2(Edge, -Edge), new Vector2(SideW, 540f), "");
            _empireTitle.fontSize = 16;
            _empireTitle.rectTransform.TopBand(4, 32, 46, 40);
            var crt = LGBuild.Rect(p, "Crest");
            crt.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(14, -20), new Vector2(26, 26));
            _empireCrest = crt.gameObject.AddComponent<Image>();
            _empireCrest.preserveAspect = true;
            _empireCrest.raycastTarget = false;
            _empireCrest.enabled = false;
            LG.Ignore(crt.gameObject);
            _infoLeft = LGBuild.Rect(p, "Info");
            _infoLeft.Stretch(16, 12, 16, 48);
        }

        /// <summary>Левый низ: кольца доверия и влияния, действующие соглашения.</summary>
        private void BuildStatus(RectTransform rt)
        {
            var (p, _) = FramedPanel(rt, "Status", new Vector2(0, 0), new Vector2(Edge, 14f), new Vector2(SideW, 340f), Loc.T("СТАТУС ОТНОШЕНИЙ"));
            var link = LGBuild.Panel(p, "Link", new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f));
            link.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(SideW * 0.5f, -116f), new Vector2(SideW - 96f * 2f - 116f, 2f));
            LG.Ignore(link.gameObject);
            (_gaugeTrust, _gaugeTrustVal, _gaugeTrustSub, _trustHit) = Gauge(p, new Vector2(96f, -116f), Loc.T("ДОВЕРИЕ"));
            Image influenceHit;
            (_gaugeInfluence, _gaugeInfluenceVal, _gaugeInfluenceSub, influenceHit) = Gauge(p, new Vector2(SideW - 96f, -116f), Loc.T("ВЛИЯНИЕ"));
            TooltipHelper.Attach(influenceHit.gameObject,
                Loc.T("<b>Влияние</b>\nВаш запас влияния. Каждое предложение сделки стоит влияния — чем крупнее сделка, тем дороже переговоры"));

            var ah = LGBuild.Label(p, Loc.T("ДЕЙСТВУЮЩИЕ СОГЛАШЕНИЯ"), 12, Muted, TextAnchor.MiddleLeft, bold: true).Track(1.2f);
            ah.rectTransform.TopBand(206, 18, 16, 16);
            Line(p, 228f, 0.22f, 16f);
            _agreements = LGBuild.Rect(p, "Agreements");
            _agreements.TopBand(236, 96, 16, 16);
        }

        /// <summary>Правая сторона: что могут дать они (сверху) и вы (снизу), шанс согласия и стоимость переговоров.</summary>
        private void BuildGrids(RectTransform rt)
        {
            RectTransform a;
            (a, _aiGridTitle) = FramedPanel(rt, "TheyGive", new Vector2(1, 1), new Vector2(-Edge, -300f), new Vector2(SideW, 222f), "");   // выше — место уведомлений
            _aiGrid = LGBuild.Rect(a, "Grid");
            _aiGrid.TopBand(50f, CellSize * 2f + CellGap, 16, 16);

            var (b, bt) = FramedPanel(rt, "YouGive", new Vector2(1, 0), new Vector2(-Edge, 14f), new Vector2(SideW, 340f), Loc.T("ВЫ ДАЁТЕ"));
            bt.color = Gold;
            _playerGrid = LGBuild.Rect(b, "Grid");
            _playerGrid.TopBand(50f, CellSize * 2f + CellGap, 16, 16);

            var cl = LGBuild.Label(b, Loc.T("ШАНС СОГЛАСИЯ"), 12, Primary, TextAnchor.MiddleLeft, bold: true).Track(1.2f);
            cl.rectTransform.TopBand(222, 18, 16, 16);
            _chanceVal = LGBuild.Label(b, "—", 14, Primary, TextAnchor.MiddleRight, bold: true);
            _chanceVal.rectTransform.TopBand(222, 18, 16, 16);
            var segs = LGBuild.Rect(b, "Chance");
            segs.TopBand(246, 14, 16, 16);
            _chanceSegs = new Image[ChanceSegs];
            for (int i = 0; i < ChanceSegs; i++)
            {
                var s = LGBuild.Panel(segs, "Seg", new Color(1f, 1f, 1f, 0.07f));
                var r = s.rectTransform;
                r.anchorMin = new Vector2(i / (float)ChanceSegs, 0f);
                r.anchorMax = new Vector2((i + 1) / (float)ChanceSegs, 1f);
                r.offsetMin = new Vector2(1.5f, 0f);
                r.offsetMax = new Vector2(-1.5f, 0f);
                LG.Ignore(s.gameObject);
                _chanceSegs[i] = s;
            }
            _reaction = LGBuild.Label(b, "", 13, Primary, TextAnchor.UpperLeft, bold: true, wrap: true);
            _reaction.rectTransform.TopBand(268, 36, 16, 16);
            _fee = LGBuild.Label(b, "", 11, Muted, TextAnchor.UpperLeft);
            _fee.rectTransform.TopBand(308, 18, 16, 16);
        }

        /// <summary>«Диалог» внизу по центру: портрет, реплика и варианты ответа.</summary>
        private void BuildDialog(RectTransform rt)
        {
            var p = LGBuild.Panel(rt, "Dialog", PanelBg, raycast: true);
            var b = p.rectTransform;
            b.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(DialogW, DialogH));
            LG.Glass(p.gameObject, 16f).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f));
            var head = LGBuild.Panel(b, "HeadBg", HeadBg);
            head.rectTransform.TopBand(4, 26, 4, 4);
            LG.Platter(head.gameObject, 10f).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.15f));
            var ht = LGBuild.Label(b, Loc.T("ДИАЛОГ"), 13, Primary, TextAnchor.MiddleCenter, bold: true);
            ht.rectTransform.TopBand(4, 26, 0, 0);
            ht.Track(2.5f);

            _speechStateBg = LGBuild.Panel(b, "State", new Color(0.1f, 0.2f, 0.2f, 1f));
            _speechStateBg.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -7), new Vector2(120, 20));
            var sfx = LG.Platter(_speechStateBg.gameObject, 4f);
            sfx.FillMultiplier = 2.6f;
            _speechState = LGBuild.Label(_speechStateBg.transform, "", 11, Primary, TextAnchor.MiddleCenter, bold: true);

            var pf = LGBuild.Panel(b, "PortraitFrame", new Color(0.03f, 0.07f, 0.09f, 1f));
            pf.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -38), new Vector2(136, 136));
            LG.Platter(pf.gameObject, 8f).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f));
            var pm = LGBuild.Rect(pf.transform, "Mask");
            pm.Stretch(3, 3, 3, 3);
            pm.gameObject.AddComponent<RectMask2D>();
            _dialogPortrait = pm.gameObject.AddComponent<RawImage>();
            _dialogPortrait.raycastTarget = false;
            LG.Ignore(pm.gameObject);

            _speechName = LGBuild.Label(b, "", 13, Gold, TextAnchor.UpperLeft, bold: true);
            _speechName.rectTransform.TopBand(38, 18, 162, 16);
            _speechText = LGBuild.Label(b, "", 14, new Color(0.90f, 0.95f, 0.97f), TextAnchor.UpperLeft, wrap: true);
            _speechText.supportRichText = false;
            _speechText.rectTransform.TopBand(58, 40, 162, 16);

            (_btnPropose, _imgPropose, _lblPropose) = Reply(b, 100f, LGIcon.Handshake, OnProposeClicked);
            (_btnBalance, _imgBalance, _lblBalance) = Reply(b, 127f, LGIcon.Diplomacy, OnBalanceClicked);
            (_btnBack, _imgBack, _lblBack) = Reply(b, 154f, LGIcon.Back, OnBackClicked);
        }

        private static (Button, Image, Text) Reply(RectTransform parent, float y, LGIcon icon, System.Action onClick)
        {
            var b = LGBuild.Button(parent, "Reply", RowBg, new Color(Cyan.r, Cyan.g, Cyan.b, 0.30f), onClick, null, null, 13, 4f);
            ((RectTransform)b.transform).TopBand(y, 24, 162, 14);
            var fx = b.GetComponent<LiquidGlassEffect>();
            if (fx != null) { fx.SpecularMultiplier = 0.25f; fx.GlowMultiplier = 0.4f; fx.FillMultiplier = 2.4f; }
            var ic = LGIcons.Create(b.transform, icon, 13, Color.white);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(13, 13));
            var t = LGBuild.Label(b.transform, "", 13, Color.white, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch(32, 0, 10, 0);
            return (b, b.GetComponent<Image>(), t);
        }

        /// <summary>Вкладки действий вдоль переднего края стола: ровный шаг по горизонтали, высота и наклон — по кромке.</summary>
        private void BuildActions(RectTransform rt)
        {
            const float span = 450f, a = TableA * 0.97f;
            for (int i = 0; i < SlotCount; i++)
            {
                float t = (i - (SlotCount - 1) * 0.5f) / ((SlotCount - 1) * 0.5f);   // −1..1 слева направо
                float x = t * span;
                float c = x / a, s = -Mathf.Sqrt(Mathf.Max(0f, 1f - c * c));
                float y = TableY - TableSkirt * 0.3f + s * TableB;
                float slope = Mathf.Atan2(TableB * c, -a * s) * Mathf.Rad2Deg;

                var slot = new Slot();
                int idx = i;
                slot.Btn = LGBuild.Button(rt, "Action", SlotBg, new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f), () => _slots[idx].Act?.Invoke(), null, null, 12, 6f);
                var r = (RectTransform)slot.Btn.transform;
                r.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(142f, 36f));
                r.localRotation = Quaternion.Euler(0f, 0f, slope * 0.55f);
                slot.Bg = slot.Btn.GetComponent<Image>();
                slot.Icon = LGIcons.Create(slot.Btn.transform, LGIcon.Handshake, 14, Color.white);
                slot.Icon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(14, 14));
                slot.Label = LGBuild.Label(slot.Btn.transform, "", 11, Color.white, TextAnchor.MiddleCenter, bold: true, wrap: true);
                slot.Label.rectTransform.Stretch(28, 0, 8, 0);
                _slots.Add(slot);
            }
        }

        // ================================================================ Строительные блоки

        /// <summary>Панель в рамке со шапкой (полоса, заголовок, значок и линия) — как панели референса.</summary>
        private static (RectTransform, Text) FramedPanel(RectTransform rt, string name, Vector2 anchor, Vector2 pos, Vector2 size, string title)
        {
            var p = LGBuild.Panel(rt, name, PanelBg, raycast: true);
            var r = p.rectTransform;
            r.At(anchor, anchor, pos, size);
            LG.Glass(p.gameObject, 16f).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f));
            var head = LGBuild.Panel(r, "HeadBg", HeadBg);
            head.rectTransform.TopBand(4, 32, 4, 4);
            LG.Platter(head.gameObject, 12f).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.15f));
            var t = LGBuild.Label(r, title, 14, Primary, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.TopBand(4, 32, 16, 40);
            t.Track(1.5f);
            var mark = LGIcons.Create(r, LGIcon.Diplomacy, 14, new Color(Cyan.r, Cyan.g, Cyan.b, 0.7f));
            mark.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14, -13), new Vector2(14, 14));
            Line(r, 37f, 0.45f, 4f);
            return (r, t);
        }

        private static void Line(RectTransform parent, float y, float alpha, float pad)
        {
            var l = LGBuild.Panel(parent, "Line", new Color(Cyan.r, Cyan.g, Cyan.b, alpha));
            l.rectTransform.TopBand(y, 1, pad, pad);
            LG.Ignore(l.gameObject);
        }

        /// <summary>Кольцо-индикатор: дорожка, радиальная заливка, значение и подпись внутри, название под кольцом.</summary>
        private static (Image, Text, Text, Image) Gauge(RectTransform parent, Vector2 center, string label)
        {
            const float s = 112f;
            var track = LGBuild.Panel(parent, "Track", new Color(1f, 1f, 1f, 0.07f), raycast: true);
            track.sprite = RingSprite();
            track.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), center, new Vector2(s, s));
            LG.Ignore(track.gameObject);
            var fill = LGBuild.Panel(parent, "Fill", Cyan);
            fill.sprite = RingSprite();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Bottom;
            fill.fillClockwise = true;
            fill.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), center, new Vector2(s, s));
            LG.Ignore(fill.gameObject);
            var val = LGBuild.Label(parent, "", 22, Color.white, TextAnchor.MiddleCenter, bold: true);
            val.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), center + new Vector2(0f, 6f), new Vector2(s, 28));
            var sub = LGBuild.Label(parent, "", 9, Muted, TextAnchor.MiddleCenter, bold: true);
            sub.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), center + new Vector2(0f, -16f), new Vector2(s - 26f, 14));
            var lbl = LGBuild.Label(parent, label, 12, Primary, TextAnchor.MiddleCenter, bold: true).Track(1.5f);
            lbl.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), center + new Vector2(0f, -s * 0.5f - 14f), new Vector2(150, 18));
            return (fill, val, sub, track);
        }

        private static Text ColumnHead(RectTransform col, string text)
        {
            var t = LGBuild.Label(col, text, 11, Muted, TextAnchor.UpperCenter, bold: true);
            t.rectTransform.TopBand(0, 18);
            t.Track(1.2f);
            return t;
        }

        private void BuildPortrait(AIEmpireManager ai)
        {
            var leader = LeaderPortraits.ForFaction(ai.Faction);
            string key = leader?.Key ?? "";
            if (key == _portraitKey) return;
            _portraitKey = key;
            LGBuild.Clear(_portraitHost);
            // Правитель — голограмма над своей площадкой: без фона картинки, низ растворяется в луче проектора
            var v = LeaderPortraitView.Create(_portraitHost, leader, 1f, new Vector4(0.10f, 0.10f, 0.30f, 0f), false, false, true);
            if (v == null) LGIcons.Create(_portraitHost, LGIcon.Leader, 240, new Color(1f, 1f, 1f, 0.15f));
            else v.SetHologram(0.7f, Color.Lerp(ai.AIEmpireColor, Color.white, 0.35f));
        }

        /// <summary>Наш правитель — голограмма слева, лицом к собеседнику (строится один раз на фракцию).</summary>
        private void BuildPlayerPortrait()
        {
            var faction = UIManager.Instance?.SelectedFaction;
            var leader = LeaderPortraits.ForFaction(faction);
            string key = leader?.Key ?? "";
            if (key == _playerKey) return;
            _playerKey = key;
            LGBuild.Clear(_playerHost);
            var v = LeaderPortraitView.Create(_playerHost, leader, 1f, new Vector4(0.10f, 0.10f, 0.30f, 0f), false, false, true);
            if (v == null) LGIcons.Create(_playerHost, LGIcon.Leader, 220, new Color(1f, 1f, 1f, 0.12f));
            else v.SetHologram(0.7f, Color.Lerp(FleetIndicator.OwnColor, Color.white, 0.3f));
        }

        // ================================================================ Голо-элементы комнаты

        private static Image Glow(RectTransform rt, string name, Vector2 pos, float size)
        {
            var g = LGBuild.Panel(rt, name, Color.clear);
            g.sprite = FactionSelectScreen.RadialSprite();
            g.raycastTarget = false;
            g.rectTransform.anchorMin = g.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            g.rectTransform.sizeDelta = new Vector2(size, size);
            g.rectTransform.anchoredPosition = pos;
            LG.Ignore(g.gameObject);
            return g;
        }

        /// <summary>Голо-слой: у стола (inParent) — по центру контейнера, иначе — от низа экрана.</summary>
        private static Image Holo(RectTransform parent, string name, Sprite sp, Vector2 pos, Vector2 size, Vector2 pivot, bool inParent = false)
        {
            var img = LGBuild.Panel(parent, name, Color.clear);
            img.sprite = sp;
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = inParent ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0f);
            r.pivot = pivot;
            r.sizeDelta = size;
            r.anchoredPosition = pos;
            LG.Ignore(img.gameObject);
            return img;
        }

        private static Image Ellipse(RectTransform rt, string name, Sprite sp, Vector2 pos, Vector2 size, Color c)
        {
            var img = Holo(rt, name, sp, pos, size, new Vector2(0.5f, 0.5f));
            img.color = c;
            return img;
        }

        private static Sprite s_pad, s_cone, s_dash, s_disc, s_rim, s_ring;

        private static Sprite MakeSprite(int w, int h, System.Func<float, float, float> alpha, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = name, hideFlags = HideFlags.DontSave };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(u, v)) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100);
            sp.hideFlags = HideFlags.DontSave;
            return sp;
        }

        /// <summary>Площадка проектора: два кольца и мягкое свечение внутри.</summary>
        private static Sprite PadSprite()
        {
            if (s_pad != null) return s_pad;
            s_pad = MakeSprite(256, 256, (u, v) =>
            {
                float d = Mathf.Sqrt(u * u + v * v);
                float ring1 = Mathf.Exp(-Mathf.Pow((d - 0.94f) / 0.022f, 2f));
                float ring2 = Mathf.Exp(-Mathf.Pow((d - 0.70f) / 0.014f, 2f)) * 0.7f;
                float fill = Mathf.Clamp01(1f - d) * 0.35f;
                return Mathf.Max(Mathf.Max(ring1, ring2), fill);
            }, "DiploPad");
            return s_pad;
        }

        /// <summary>Конус света проектора: ярче у основания, гаснет вверх и к краям.</summary>
        private static Sprite ConeSprite()
        {
            if (s_cone != null) return s_cone;
            s_cone = MakeSprite(64, 128, (u, v) =>
            {
                float up = (v + 1f) * 0.5f;                    // 0 — низ, 1 — верх
                float width = Mathf.Lerp(0.55f, 1f, up);       // конус расширяется вверх
                float side = Mathf.Clamp01(1f - Mathf.Abs(u) / width);
                return side * side * Mathf.Pow(1f - up, 1.6f);
            }, "DiploCone");
            return s_cone;
        }

        /// <summary>Пунктирное кольцо голостола с делениями.</summary>
        private static Sprite DashRingSprite()
        {
            if (s_dash != null) return s_dash;
            s_dash = MakeSprite(512, 512, (u, v) =>
            {
                float d = Mathf.Sqrt(u * u + v * v);
                float ang = (Mathf.Atan2(v, u) * Mathf.Rad2Deg + 360f) % 360f;
                float ring = Mathf.Exp(-Mathf.Pow((d - 0.95f) / 0.012f, 2f));
                float dash = Mathf.Repeat(ang, 6f) < 3.6f ? 1f : 0.15f;
                float ticks = Mathf.Repeat(ang + 1f, 30f) < 2f && d > 0.84f && d < 0.93f ? 0.8f : 0f;
                float inner = Mathf.Exp(-Mathf.Pow((d - 0.55f) / 0.01f, 2f)) * 0.6f;
                return Mathf.Max(ring * dash, Mathf.Max(ticks, inner));
            }, "DiploDash");
            return s_dash;
        }

        /// <summary>Сплошной диск с мягким краем — крышка и основание стола.</summary>
        private static Sprite DiscSprite()
        {
            if (s_disc != null) return s_disc;
            s_disc = MakeSprite(256, 256, (u, v) => Mathf.Clamp01((1f - Mathf.Sqrt(u * u + v * v)) / 0.02f), "DiploDisc");
            return s_disc;
        }

        /// <summary>Светящаяся кромка стола: яркая линия по краю и мягкий отсвет внутрь.</summary>
        private static Sprite RimSprite()
        {
            if (s_rim != null) return s_rim;
            s_rim = MakeSprite(512, 512, (u, v) =>
            {
                float d = Mathf.Sqrt(u * u + v * v);
                if (d > 1f) return 0f;
                float edge = Mathf.Exp(-Mathf.Pow((d - 0.975f) / 0.012f, 2f));
                float inner = Mathf.Pow(Mathf.Clamp01((d - 0.72f) / 0.26f), 3f) * 0.35f;
                float groove = Mathf.Exp(-Mathf.Pow((d - 0.90f) / 0.006f, 2f)) * 0.45f;
                return Mathf.Max(edge, Mathf.Max(inner, groove));
            }, "DiploRim");
            return s_rim;
        }

        /// <summary>Толстое кольцо для индикаторов статуса.</summary>
        private static Sprite RingSprite()
        {
            if (s_ring != null) return s_ring;
            s_ring = MakeSprite(256, 256, (u, v) =>
            {
                float d = Mathf.Sqrt(u * u + v * v);
                return Mathf.Clamp01((0.11f - Mathf.Abs(d - 0.84f)) / 0.02f);
            }, "DiploGauge");
            return s_ring;
        }

        /// <summary>Фон-сцена империи, кадрированная «cover» под экран.</summary>
        private void ApplyScene(AIEmpireManager ai)
        {
            var leader = LeaderPortraits.ForFaction(ai.Faction);
            var tex = leader != null ? Resources.Load<Texture2D>("Diplomacy/bg_" + leader.Key) : null;
            _scene.texture = tex;
            _scene.enabled = tex != null;
            _sceneShade.enabled = tex != null;
            if (tex == null) return;
            var size = ((RectTransform)_root.transform).rect.size;
            float screen = size.y > 1f ? size.x / size.y : 16f / 9f;
            float img = tex.width / (float)Mathf.Max(1, tex.height);
            _scene.uvRect = screen > img
                ? new Rect(0f, (1f - img / screen) * 0.5f, 1f, img / screen)
                : new Rect((1f - screen / img) * 0.5f, 0f, screen / img, 1f);
        }

        // ================================================================ Обновление

        private void RefreshAll()
        {
            var ai = Current;
            if (ai == null) return;
            _signature = Signature(ai);
            RefreshInfo();
            RefreshAgreements(ai);
            RefreshTabs();
            RefreshSpeechHeader(ai);
            RefreshDeal();
        }

        /// <summary>Всё, что зависит от сделки на столе.</summary>
        private void RefreshDeal()
        {
            RefreshItemLists();
            RefreshTable();
            RefreshActions();
            RefreshStatus(Current);
            RefreshPicker();
        }

        // ---------- Левый верх: империя, сравнение сил, состояние, соседи ----------

        private void RefreshInfo()
        {
            var ai = Current;
            LGBuild.Clear(_infoLeft);
            if (ai == null) return;

            _empireTitle.text = ai.AIName.ToUpper();
            _empireTitle.color = Color.Lerp(ai.AIEmpireColor, Color.white, 0.25f);

            float y = 0f;
            var leader = LeaderPortraits.ForFaction(ai.Faction);
            if (leader != null)
            {
                var ln = LGBuild.Label(_infoLeft, $"{leader.Title}  <b>{leader.Name}</b>", 13, Primary, TextAnchor.UpperLeft);
                ln.rectTransform.TopBand(y, 18);
                y += 20f;
            }
            var ti = LGBuild.Label(_infoLeft, ai.AITitle, 12, Muted, TextAnchor.UpperLeft);
            ti.rectTransform.TopBand(y, 16);
            y += 18f;
            var pers = LGBuild.Label(_infoLeft, Loc.F("Характер: {0}", ai.Profile.Name.ToLower()), 12, Gold, TextAnchor.UpperLeft, bold: true);
            pers.rectTransform.TopBand(y, 18);
            TooltipHelper.Attach(pers.gameObject, ai.Profile.Summary);
            pers.raycastTarget = true;
            y += 28f;

            // Сравнение сил: доля собеседника — цветом его империи, наша — нашим цветом
            CompareBar(ref y, LGIcon.Fleet, Loc.T("ФЛОТ"), EmpireStats.MilitaryPower(ai.OwnerId), EmpireStats.MilitaryPower(0), ai.AIEmpireColor);
            CompareBar(ref y, LGIcon.Research, Loc.T("ТЕХНОЛОГИИ"), ai.ResearchedCount, EmpireStats.TechCount(0), ai.AIEmpireColor);
            CompareBar(ref y, LGIcon.Star, Loc.T("СИСТЕМЫ"), EmpireStats.SystemCount(ai.OwnerId), EmpireStats.SystemCount(0), ai.AIEmpireColor);
            y += 4f;

            // Состояние
            (string state, Color sc, LGIcon si) = StateOf(ai);
            var chip = LGBuild.Panel(_infoLeft, "State", new Color(sc.r * 0.15f, sc.g * 0.15f, sc.b * 0.15f, 0.95f));
            chip.rectTransform.TopBand(y, 30, 0, 0);
            var cfx = LG.Platter(chip.gameObject, 4f);
            cfx.SetRim(new Color(sc.r, sc.g, sc.b, 0.5f));
            cfx.FillMultiplier = 2.6f;
            var sic = LGIcons.Create(chip.transform, si, 15, sc);
            sic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(15, 15));
            var st = LGBuild.Label(chip.transform, state, 12, sc, TextAnchor.MiddleLeft, bold: true);
            st.rectTransform.Stretch(32, 0, 8, 0);
            y += 40f;

            // Ваша репутация и поводы для войны против этой империи
            InfoLineTip(_infoLeft, ref y, Reputation.Value >= 0f ? LGIcon.Handshake : LGIcon.Betrayal, Reputation.ColorOf(Reputation.Value),
                Loc.F("Ваша репутация: <b>{0}</b> ({1:+0;-0;0})", Reputation.Current, Reputation.Value), Reputation.Tooltip());
            var causes = CasusBelli.For(ai.OwnerId);
            InfoLineTip(_infoLeft, ref y, LGIcon.Swords, causes.Count > 0 ? Gold : Muted,
                causes.Count > 0 ? Loc.F("Поводы для войны: {0}", causes.Count == 1 ? causes[0].Name : Loc.F("{0} и ещё {1}", causes[0].Name, causes.Count - 1))
                                 : Loc.T("Повода для войны нет"),
                CasusBelli.Tooltip(ai.OwnerId));

            if (AICoalition.IsActive)
            {
                string role = AICoalition.IsMember(ai.OwnerId) ? Loc.T("участник") : AICoalition.IsTarget(ai.OwnerId) ? Loc.T("цель") : Loc.T("вне коалиции");
                string you = AICoalition.PlayerMember ? Loc.T("  ·  вы в союзе") : "";
                InfoLine(_infoLeft, ref y, LGIcon.Swords, CoalitionTone, Loc.F("Коалиция против {0}: {1}{2}", AICoalition.TargetPhrase, role, you));
            }
            if (!string.IsNullOrEmpty(ai.TacticsNote))
                InfoLine(_infoLeft, ref y, LGIcon.Target, Muted, ai.TacticsNote);

            // Соседи-ИИ
            foreach (var other in AIEmpireManager.All)
            {
                if (other == ai || other.IsEliminated || ai.IsEliminated) continue;
                if (!Contacts.PlayerMet(other.OwnerId)) continue;   // о незнакомых нам империях не рассказываем
                if (!Contacts.Met(ai.OwnerId, other.OwnerId))
                {
                    InfoLine(_infoLeft, ref y, LGIcon.Diplomacy, Muted, Loc.F("С <color={0}>{1}</color>: не знакомы", LGBuild.Hex(other.MapColor), other.AIName));
                    continue;
                }
                bool war = AIRelations.AtWar(ai.OwnerId, other.OwnerId);
                int truce = AIRelations.TruceDays(ai.OwnerId, other.OwnerId);
                string s = war ? Loc.T("воюют") : truce > 0 ? Loc.T("перемирие") : Loc.T("мир");
                InfoLine(_infoLeft, ref y, war ? LGIcon.Swords : LGIcon.Diplomacy, war ? Red : Muted,
                    Loc.F("С <color={0}>{1}</color>: {2}", LGBuild.Hex(other.MapColor), other.AIName, s));
            }
        }

        /// <summary>Строка сравнения: подпись, «их · наши» и двухцветная полоса (их доля слева).</summary>
        private void CompareBar(ref float y, LGIcon icon, string label, float theirs, float ours, Color theirColor)
        {
            var ic = LGIcons.Create(_infoLeft, icon, 13, Muted);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(0, -y - 9f), new Vector2(13, 13));
            var l = LGBuild.Label(_infoLeft, label, 12, Primary, TextAnchor.MiddleLeft, bold: true);
            l.rectTransform.TopBand(y, 18, 20, 0);
            Color tc = Color.Lerp(theirColor, Color.white, 0.2f), oc = FleetIndicator.OwnColor;
            var v = LGBuild.Label(_infoLeft, Loc.F("<color={0}>их {1:0}</color>  ·  <color={2}>наши {3:0}</color>", LGBuild.Hex(tc), theirs, LGBuild.Hex(oc), ours),
                12, Primary, TextAnchor.MiddleRight);
            v.rectTransform.TopBand(y, 18, 0, 0);

            var bar = LGBuild.Rect(_infoLeft, "Compare");
            bar.TopBand(y + 22f, 6, 0, 0);
            var back = LGBuild.Panel(bar, "Ours", new Color(oc.r, oc.g, oc.b, 0.75f));
            back.rectTransform.Stretch();
            LG.Ignore(back.gameObject);
            float share = theirs + ours > 0.01f ? theirs / (theirs + ours) : 0.5f;
            var fill = LGBuild.Panel(bar, "Theirs", new Color(tc.r, tc.g, tc.b, 0.95f));
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(share, 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            LG.Ignore(fill.gameObject);
            var tick = LGBuild.Panel(bar, "Mid", new Color(1f, 1f, 1f, 0.6f));
            tick.rectTransform.anchorMin = new Vector2(0.5f, -0.5f);
            tick.rectTransform.anchorMax = new Vector2(0.5f, 1.5f);
            tick.rectTransform.sizeDelta = new Vector2(1f, 0f);
            LG.Ignore(tick.gameObject);
            y += 38f;
        }

        /// <summary>Строка с подсказкой при наведении.</summary>
        private static void InfoLineTip(RectTransform parent, ref float y, LGIcon icon, Color col, string text, string tip)
        {
            var row = LGBuild.Panel(parent, "Line", new Color(1f, 1f, 1f, 0f), raycast: true);
            row.rectTransform.TopBand(y, 20, 0, 0);
            LG.Ignore(row.gameObject);
            var ic = LGIcons.Create(row.rectTransform, icon, 14, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(14, 14));
            var t = LGBuild.Label(row.rectTransform, text, 12, Primary, TextAnchor.MiddleLeft);
            t.rectTransform.Stretch(22, 0, 0, 0);
            TooltipHelper.Attach(row.gameObject, tip);
            y += 22f;
        }

        private static void InfoLine(RectTransform parent, ref float y, LGIcon icon, Color col, string text)
        {
            var row = LGBuild.Rect(parent, "Line");
            row.TopBand(y, 20, 0, 0);
            var ic = LGIcons.Create(row, icon, 14, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(14, 14));
            var t = LGBuild.Label(row, text, 12, Primary, TextAnchor.MiddleLeft);
            t.rectTransform.Stretch(22, 0, 0, 0);
            y += 22f;
        }

        // ---------- Левый низ: статус отношений ----------

        private void RefreshAgreements(AIEmpireManager ai)
        {
            LGBuild.Clear(_agreements);
            float y = 0f;
            if (ai.Agreements.Count == 0 && !ai.HasPact && !ai.HasAlliance)
            {
                var none = LGBuild.Label(_agreements, Loc.T("Нет действующих договоров"), 12, new Color(Muted.r, Muted.g, Muted.b, 0.7f), TextAnchor.UpperLeft);
                none.rectTransform.TopBand(y, 18, 0, 0);
                return;
            }
            if (ai.HasAlliance) InfoLine(_agreements, ref y, LGIcon.Defense, DealCatalog.Tint(DealItemKind.Alliance), Loc.T("Оборонительный союз  ·  бессрочно"));
            if (ai.HasPact) InfoLine(_agreements, ref y, LGIcon.Handshake, Cyan, Loc.T("Пакт о ненападении  ·  бессрочно"));
            foreach (var a in ai.Agreements)
            {
                string text = DealCatalog.IsPayment(a.Kind)
                    ? Loc.F("{0} {1:0} {2}/мес  ·  {3}", (a.PlayerPays ? Loc.T("Вы платите") : Loc.T("Вам платят")), a.Amount, DealCatalog.Name(DealCatalog.ResourceBase(a.Kind)).ToLower(), DealCatalog.Months(a.MonthsLeft))
                    : $"{DealCatalog.Name(a.Kind)}  ·  {DealCatalog.Months(a.MonthsLeft)}";
                InfoLine(_agreements, ref y, DealCatalog.Icon(a.Kind), a.PlayerPays ? Red : DealCatalog.Tint(a.Kind), text);
            }
        }

        /// <summary>Кольца: доверие — из отношения, влияние — наш запас (и цена переговоров о сделке на столе).</summary>
        private void RefreshStatus(AIEmpireManager ai)
        {
            if (_gaugeTrust == null || ai == null) return;
            float trust = Mathf.InverseLerp(AIEmpireManager.OpinionMin, AIEmpireManager.OpinionMax, ai.Opinion);
            Color oc = OpinionColor(ai.Opinion);
            _gaugeTrust.fillAmount = trust;
            _gaugeTrust.color = oc;
            _gaugeTrustVal.text = $"{ai.Opinion:+0;-0;0}";
            _gaugeTrustVal.color = oc;
            _gaugeTrustSub.text = AIEmpireManager.OpinionLabel(ai.Opinion).ToUpper();
            TooltipHelper.Attach(_trustHit.gameObject, OpinionTooltip(ai));

            float inf = EconomyManager.Instance != null ? EconomyManager.Instance.Influence : 0f;
            float cost = _viewingOffer || _deal.IsEmpty ? 0f : AIEmpireManager.ProposalCost(_deal);
            Color ic = cost > inf ? Red : new Color(0.78f, 0.55f, 1f);
            _gaugeInfluence.fillAmount = Mathf.Clamp01(inf / 250f);
            _gaugeInfluence.color = ic;
            _gaugeInfluenceVal.text = $"{inf:0}";
            _gaugeInfluenceVal.color = Color.white;
            _gaugeInfluenceSub.text = cost > 0f ? Loc.F("СДЕЛКА −{0:0}", cost) : Loc.T("ЗАПАС");
            _gaugeInfluenceSub.color = cost > inf ? Red : Muted;
        }

        private void RefreshTabs()
        {
            LGBuild.Clear(_tabs);
            var all = new List<AIEmpireManager>();
            foreach (var a in AIEmpireManager.All) if (Contacts.PlayerMet(a.OwnerId)) all.Add(a);
            if (all.Count <= 1) return;
            for (int i = 0; i < all.Count; i++)
            {
                var ai = all[i];
                bool on = ai.OwnerId == _owner;
                Color mc = ai.MapColor;
                var b = LGBuild.Panel(_tabs, "Tab_" + ai.OwnerId, on ? new Color(mc.r * 0.25f, mc.g * 0.25f, mc.b * 0.25f, 1f) : PanelBg, raycast: true);
                float x = (i - (all.Count - 1) * 0.5f) * 60f;
                b.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, 0f), new Vector2(52, 52));
                var fx = LG.Platter(b.gameObject, 6f);
                fx.SetRim(new Color(mc.r, mc.g, mc.b, on ? 0.9f : 0.3f));
                fx.FillMultiplier = 2.6f;
                var mask = LG.RoundedMask(b.transform, 6f, 2f);
                var v = LeaderPortraitView.Create(mask, LeaderPortraits.ForFaction(ai.Faction), 2.2f, Vector4.zero, false, false, false);
                if (v == null) LGIcons.Create(b.transform, LGIcon.Leader, 26, mc);
                else if (ai.IsEliminated) v.GetComponent<RawImage>().color = new Color(0.4f, 0.4f, 0.4f, 1f);
                var btn = b.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                int owner = ai.OwnerId;
                btn.onClick.AddListener(() => { if (owner != _owner) { SFXManager.Play(Sfx.UiTab); SwitchTo(owner); } });
                string st = ai.IsEliminated ? Loc.T("повержена") : ai.AtWar ? Loc.T("война") : ai.HasAlliance ? Loc.T("союз") : ai.HasPact ? Loc.T("пакт") : Loc.T("мир");
                TooltipHelper.Attach(b.gameObject, Loc.F("<b>{0}</b>\n{1} · отношение {2:+0;-0;0}", ai.AIName, st, ai.Opinion) +
                    (ai.PendingOffer != AIEmpireManager.OfferKind.None ? Loc.T("\n<color=#F2C747>Ждёт ответа на предложение</color>") : ""));
                if (ai.PendingOffer != AIEmpireManager.OfferKind.None)
                {
                    var dot = LGBuild.Panel(b.transform, "Dot", Gold);
                    dot.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-4, -4), new Vector2(10, 10));
                    LG.Dot(dot.gameObject);
                }
            }
        }

        private void RefreshSpeechHeader(AIEmpireManager ai)
        {
            var leader = LeaderPortraits.ForFaction(ai.Faction);
            _speechName.text = Loc.F("{0} говорит:", (leader != null ? leader.Name : ai.AIName));
            (string state, Color sc, _) = StateOf(ai);
            _speechState.text = state.Split(' ')[0];
            _speechState.color = sc;
            _speechStateBg.color = new Color(sc.r * 0.15f, sc.g * 0.15f, sc.b * 0.15f, 1f);
        }

        private static (string, Color, LGIcon) StateOf(AIEmpireManager ai)
        {
            if (ai.IsEliminated) return (Loc.T("ПОВЕРЖЕНА"), Muted, LGIcon.Trophy);
            if (ai.AtWar) return (Loc.F("ВОЙНА · {0} мес. · усталость {1:0}%", ai.WarMonths, ai.WarWeariness), Red, LGIcon.Swords);
            if (ai.HasAlliance) return (Loc.T("ОБОРОНИТЕЛЬНЫЙ СОЮЗ"), DealCatalog.Tint(DealItemKind.Alliance), LGIcon.Defense);
            if (ai.HasPact) return (Loc.T("ПАКТ О НЕНАПАДЕНИИ"), Cyan, LGIcon.Handshake);
            if (ai.TruceDays > 0) return (Loc.F("ПЕРЕМИРИЕ · {0} мес.", Mathf.CeilToInt(ai.TruceDays / 30f)), Green, LGIcon.Peace);
            return (Loc.T("МИР"), Green, LGIcon.Peace);
        }

        private static Color OpinionColor(float o) =>
            o <= -60f ? Red : o <= -20f ? new Color(1f, 0.55f, 0.35f) : o < 10f ? Gold : o < 35f ? new Color(0.6f, 0.9f, 0.5f) : Green;

        private static string OpinionTooltip(AIEmpireManager ai)
        {
            var sb = new StringBuilder();
            sb.Append(Loc.F("<b>Отношение {0:+0;-0;0}</b>  ·  {1}\n", ai.Opinion, AIEmpireManager.OpinionLabel(ai.Opinion)));
            foreach (var m in ai.OpinionBreakdown)
            {
                string c = m.Value >= 0 ? "#6FE09A" : "#FF8080";
                sb.Append($"<color={c}>{m.Value:+0;-0;0}</color>  {m.Label}\n");
            }
            sb.Append(Loc.T("<color=#8AA2A8>Чем лучше отношение, тем охотнее они идут на сделки</color>"));
            return sb.ToString();
        }

        // ---------- Вкладки действий на кромке стола ----------

        private void RefreshActions()
        {
            var ai = Current;
            if (ai == null || _slots.Count < SlotCount) return;
            bool alive = !ai.IsEliminated;
            var leader = LeaderPortraits.ForFaction(ai.Faction);

            // Война / мир
            if (ai.AtWar) TreatySlot(_slots[0], ai, DealItemKind.Peace, Loc.T("МИР"), LGIcon.Peace);
            else
            {
                string blocker = ai.WarBlocker;
                string warTip = ai.HasAlliance ? Loc.T("Нападение на союзника — худшее вероломство: −60 к их отношению и −40 к репутации у всех")
                              : ai.HasPact ? Loc.T("Нарушение пакта — тяжёлое вероломство: −40 к отношению надолго и удар по репутации")
                              : Loc.T("Объявление войны рвёт все соглашения и ухудшает отношение");
                DangerSlot(_slots[0], "war", Loc.T("ОБЪЯВИТЬ ВОЙНУ"), LGIcon.Swords, Red,
                    blocker ?? warTip + "\n\n" + CasusBelli.Tooltip(ai.OwnerId),
                    !alive || blocker != null ? null : () =>
                    {
                        if (ai.PlayerDeclareWar())
                        {
                            SFXManager.Play(Sfx.WarDeclared);
                            _deal = new Deal();
                            Say(DiplomacyLines.Get(leader, DiplomacyLines.Kind.AtWar));
                        }
                    });
            }
            // Пакт
            if (ai.HasPact)
                DangerSlot(_slots[1], "cancelpact", Loc.T("РАСТОРГНУТЬ ПАКТ"), LGIcon.Betrayal, Gold,
                    Loc.T("Отношение ухудшится; после этого войну можно объявить без вероломства"),
                    alive ? () => ai.PlayerCancelPact() : (System.Action)null);
            else TreatySlot(_slots[1], ai, DealItemKind.Pact, Loc.T("ПАКТ"), LGIcon.Handshake);
            // Союз
            if (ai.HasAlliance)
                DangerSlot(_slots[2], "cancelalliance", Loc.T("РАСТОРГНУТЬ СОЮЗ"), LGIcon.Betrayal, DealCatalog.Tint(DealItemKind.Alliance),
                    Loc.T("Обязательства защищать друг друга снимаются, пакт о ненападении остаётся. Отношение и репутация немного ухудшатся"),
                    alive ? () => ai.PlayerCancelAlliance() : (System.Action)null);
            else TreatySlot(_slots[2], ai, DealItemKind.Alliance, Loc.T("СОЮЗ"), LGIcon.Defense);
            TreatySlot(_slots[3], ai, DealItemKind.TradeTreaty, Loc.T("ТОРГОВЫЙ ДОГОВОР"), LGIcon.Trade);
            TreatySlot(_slots[4], ai, DealItemKind.ResearchTreaty, Loc.T("НАУЧНЫЙ ДОГОВОР"), LGIcon.Research);
            TreatySlot(_slots[5], ai, DealItemKind.Charts, Loc.T("ЗВЁЗДНЫЕ КАРТЫ"), LGIcon.MapExplored);
            CoalitionSlot(_slots[6], ai);
        }

        /// <summary>Договор: щелчок кладёт его на стол (или убирает). Действующий — подсвечен зелёным.</summary>
        private void TreatySlot(Slot s, AIEmpireManager ai, DealItemKind k, string label, LGIcon icon)
        {
            ai.TreatyValue(k, out string block);
            bool active = ai.HasAgreement(k) || (k == DealItemKind.Alliance && ai.HasAlliance);
            bool on = _deal.HasTreaty(k);
            bool can = !ai.IsEliminated && !_viewingOffer && (block == null || on);
            string tip = $"<b>{DealCatalog.Name(k)}</b>\n{DealCatalog.Hint(k)}";
            string mine = TreatyForYou(k, ai);
            if (mine != null) tip += "\n" + mine;
            if (active) tip += Loc.T("\n<color=#6FE09A>Договор действует</color>");
            else if (block != null) tip += $"\n<color=#FF8080>{block}</color>";
            else if (_viewingOffer) tip += Loc.T("\n<color=#8AA2A8>Сначала ответьте на их предложение</color>");
            else tip += on ? Loc.T("\n<color=#F2C747>Лежит на столе — щёлкните, чтобы убрать</color>") : Loc.T("\nЩелчок — положить на стол переговоров");
            if (active && !on)
            {
                SetSlot(s, label, LGIcon.Check, Green, new Color(0.04f, 0.14f, 0.09f, 0.96f), false, tip, null, lit: true);
                return;
            }
            SetSlot(s, label, icon, on ? Gold : Cyan, on ? new Color(0.30f, 0.22f, 0.05f, 0.97f) : SlotBg, can, tip, () => AddToTable(k, false));
        }

        /// <summary>Что договор даст именно вам — в цифрах вашей империи.</summary>
        private static string TreatyForYou(DealItemKind k, AIEmpireManager ai)
        {
            const string G = "#6FE09A";
            int years = DealCatalog.TreatyMonths / 12;
            switch (k)
            {
                case DealItemKind.TradeTreaty:
                    return Loc.F("<color={0}>Для вас:</color> +{1:0} гелия-3 в месяц, за {2} лет ≈ +{3:0}", G, DealCatalog.TradeTreatyEnergy, years, DealCatalog.TradeTreatyEnergy * DealCatalog.TreatyMonths);
                case DealItemKind.ResearchTreaty:
                {
                    float inc = TechnologyManager.Instance != null ? TechnologyManager.Instance.MonthlyResearchIncome : 0f;
                    if (inc <= 0f) return null;
                    float add = inc * DealCatalog.ResearchTreatyBonus;
                    return Loc.F("<color={0}>Для вас:</color> ≈ +{1:0} науки в месяц (сейчас {2:0}/мес), за {3} лет ≈ +{4:0}", G, add, inc, years, add * DealCatalog.TreatyMonths);
                }
                case DealItemKind.Alliance:
                {
                    if (ai == null) return null;
                    var e = ai.EvaluateAlliance();
                    if (e.Blocker != null || e.Reasons == null) return null;
                    var sb = new StringBuilder(Loc.F("<color={0}>Их готовность:</color> {1:+0;-0;0}", G, e.Score));
                    foreach (var r in e.Reasons)
                        sb.Append($"\n<color={(r.Value >= 0 ? "#6FE09A" : "#FF8080")}>{r.Value:+0;-0;0}</color>  {r.Label}");
                    return sb.ToString();
                }
                case DealItemKind.Charts:
                {
                    if (ai == null) return null;
                    int toYou = 0, toThem = 0;
                    foreach (var sys in EmpireStats.Systems)
                    {
                        bool me = sys.IsSurveyedBy(0), they = sys.IsSurveyedBy(ai.OwnerId);
                        if (they && !me) toYou++;
                        if (me && !they) toThem++;
                    }
                    return Loc.F("<color={0}>Для вас:</color> откроется ≈ {1} изученных ими систем; они получат ≈ {2} ваших", G, toYou, toThem);
                }
                default:
                    return null;
            }
        }

        /// <summary>Решительное действие (война, разрыв пакта, коалиция) — с подтверждением повторным щелчком.</summary>
        private void DangerSlot(Slot s, string id, string label, LGIcon icon, Color tone, string tip, System.Action act)
        {
            bool confirming = _confirmAction == id && Time.unscaledTime <= _confirmUntil;
            System.Action click = act == null ? null : () =>
            {
                if (_confirmAction == id && Time.unscaledTime <= _confirmUntil)
                {
                    _confirmAction = null;
                    act();
                    RefreshAll();
                }
                else
                {
                    _confirmAction = id;
                    _confirmUntil = Time.unscaledTime + 3f;
                    SFXManager.Play(Sfx.UiClick);
                    RefreshActions();
                }
            };
            Color bg = confirming ? new Color(tone.r * 0.45f, tone.g * 0.45f, tone.b * 0.45f, 0.97f)
                                  : new Color(tone.r * 0.18f, tone.g * 0.18f, tone.b * 0.18f, 0.96f);
            SetSlot(s, confirming ? Loc.T("ТОЧНО? ЕЩЁ РАЗ") : label, icon, tone, bg, act != null,
                $"<b>{label}</b>\n{tip}" + (act != null ? Loc.T("\n<color=#8AA2A8>Нужно подтвердить повторным щелчком</color>") : ""), click);
        }

        /// <summary>Коалиция: вступить в действующую, собрать свою против этой империи или выйти.</summary>
        private void CoalitionSlot(Slot s, AIEmpireManager ai)
        {
            if (GameSession.Settings.Difficulty < 1 || ai.IsEliminated) { s.Btn.gameObject.SetActive(false); return; }
            if (AICoalition.PlayerMember)
            {
                bool owner = AICoalition.PlayerOrganized;
                DangerSlot(s, "coalleave", owner ? Loc.T("РАСПУСТИТЬ КОАЛИЦИЮ") : Loc.T("ПОКИНУТЬ КОАЛИЦИЮ"), LGIcon.Close, CoalitionTone,
                    Loc.T("Союзники запомнят, что вы бросили общее дело: отношение ухудшится"),
                    () => { SFXManager.Play(Sfx.UiTab); AICoalition.PlayerLeave(); });
            }
            else if (AICoalition.IsActive && AICoalition.TargetOwner != 0)
            {
                string b = AICoalition.JoinBlocker();
                DangerSlot(s, "coaljoin", Loc.T("ВСТУПИТЬ В КОАЛИЦИЮ"), LGIcon.Handshake, CoalitionTone,
                    b ?? Loc.F("Присоединитесь к союзу против {0}: союзники лучше к вам относятся, но отношения с целью ухудшатся. Стоит {1:0} влияния", AICoalition.TargetPhrase, AICoalition.JoinCost),
                    b != null ? null : () => { SFXManager.Play(Sfx.UiConfirm); AICoalition.PlayerJoin(); });
            }
            else if (!AICoalition.IsActive && AICoalition.TargetOwner < 0)
            {
                string b = AICoalition.OrganizeBlocker(ai);
                DangerSlot(s, "coalorg", Loc.T("КОАЛИЦИЯ ПРОТИВ НИХ"), LGIcon.Swords, CoalitionTone,
                    b ?? Loc.F("Соберёт соперников, которым эта империя неприятна. Стоит {0:0} влияния; союзники нападут сами, когда накопят силы", AICoalition.OrganizeCost),
                    b != null ? null : () => { SFXManager.Play(Sfx.UiConfirm); AICoalition.PlayerOrganize(ai); });
            }
            else s.Btn.gameObject.SetActive(false);
        }

        private static void SetSlot(Slot s, string label, LGIcon icon, Color tone, Color bg, bool enabled, string tip, System.Action act, bool lit = false)
        {
            bool bright = enabled || lit;
            s.Btn.gameObject.SetActive(true);
            s.Label.text = label;
            s.Label.color = bright ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            s.Icon.sprite = LGIcons.Get(icon);
            s.Icon.color = bright ? tone : new Color(tone.r, tone.g, tone.b, 0.35f);
            s.Bg.color = bright ? bg : UIManager.DS.BtnDisabled;
            var fx = s.Btn.GetComponent<LiquidGlassEffect>();
            if (fx != null) fx.SetRim(new Color(tone.r, tone.g, tone.b, bright ? 0.65f : 0.2f));
            s.Btn.interactable = enabled;
            s.Act = enabled ? act : null;
            TooltipHelper.Attach(s.Btn.gameObject, tip);
        }

        // ---------- Сетки предметов торга (справа) ----------

        private void RefreshItemLists()
        {
            var ai = Current;
            LGBuild.Clear(_aiGrid);
            LGBuild.Clear(_playerGrid);
            if (ai == null) return;
            _aiGridTitle.text = Loc.F("{0} ДАЁТ", ai.AIName.ToUpper());
            _aiGridTitle.color = Color.Lerp(ai.AIEmpireColor, Color.white, 0.25f);
            _colAI.text = Loc.T("ОНИ ДАЮТ");

            bool frozen = _viewingOffer || ai.IsEliminated;
            int i = 0;
            foreach (var k in DealCatalog.Resources)
            {
                string r = DealCatalog.ResourceKey(k);
                float aiHas = ai.Stock(r);
                float pHas = PlayerStock(r);
                Cell(_aiGrid, i, k, true, Short(aiHas), !frozen && aiHas >= DealCatalog.Step(k),
                    aiHas < DealCatalog.Step(k) ? Loc.T("У них почти ничего нет") : null);
                Cell(_playerGrid, i, k, false, Short(pHas), !frozen && pHas >= DealCatalog.Step(k),
                    pHas < DealCatalog.Step(k) ? Loc.T("У вас почти ничего нет") : null);
                i++;
            }
            foreach (var k in DealCatalog.Payments)
            {
                Cell(_aiGrid, i, k, true, Loc.T("в месяц"), !frozen && !ai.AtWar, ai.AtWar ? Loc.T("Во время войны — только мир") : null);
                Cell(_playerGrid, i, k, false, Loc.T("в месяц"), !frozen && !ai.AtWar, ai.AtWar ? Loc.T("Во время войны — только мир") : null);
                i++;
            }
            SystemCell(_aiGrid, i, true, !frozen);
            SystemCell(_playerGrid, i, false, !frozen);
        }

        /// <summary>Ячейка «Системы»: открывает список систем стороны; число — сколько систем уже на столе.</summary>
        private void SystemCell(RectTransform grid, int index, bool fromAI, bool enabled)
        {
            int col = index % 4, row = index / 4;
            int onTable = _deal.Side(fromAI).FindAll(x => DealCatalog.IsSystem(x.Kind)).Count;
            bool open = _picker != null && _picker.activeSelf && _pickerFromAI == fromAI;
            Color tint = DealCatalog.Tint(DealItemKind.System);
            float a = enabled ? 1f : 0.35f;
            var c = LGBuild.Panel(grid, "Cell_System", open || onTable > 0 ? new Color(0.22f, 0.16f, 0.04f, 0.96f) : CellBg, raycast: true);
            c.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(col * (CellSize + CellGap), -row * (CellSize + CellGap)), new Vector2(CellSize, CellSize));
            var fx = LG.Platter(c.gameObject, 6f);
            fx.SetRim(open || onTable > 0 ? new Color(Gold.r, Gold.g, Gold.b, 0.9f) : new Color(tint.r, tint.g, tint.b, enabled ? 0.40f : 0.12f));
            fx.FillMultiplier = 2.4f;
            var ic = LGIcons.Create(c.transform, LGIcon.Starbase, 30, new Color(tint.r, tint.g, tint.b, a));
            ic.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(30, 30));
            var cap = LGBuild.Label(c.transform, onTable > 0 ? Loc.F("на столе {0}", onTable) : Loc.T("системы"), 12, new Color(Primary.r, Primary.g, Primary.b, a), TextAnchor.MiddleCenter, bold: true);
            cap.rectTransform.anchorMin = new Vector2(0f, 0f);
            cap.rectTransform.anchorMax = new Vector2(1f, 0f);
            cap.rectTransform.pivot = new Vector2(0.5f, 0f);
            cap.rectTransform.sizeDelta = new Vector2(0f, 18f);
            cap.rectTransform.anchoredPosition = new Vector2(0f, 7f);
            var btn = c.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.interactable = enabled;
            btn.onClick.AddListener(() => TogglePicker(fromAI));
            string tip = Loc.T("<b>Звёздные системы</b>\n") + DealCatalog.Hint(DealItemKind.System) +
                         (fromAI ? Loc.T("\nСвои системы отдают неохотно: территория дороже ресурсов. Системы, отнятые у вас (повод «Вернуть утраченное»), уступают легче")
                                 : Loc.T("\nСоседу интереснее системы у его границы"));
            tip += !enabled && _viewingOffer ? Loc.T("\n<color=#8AA2A8>Сначала ответьте на их предложение или измените его</color>")
                 : Loc.T("\n<color=#8AA2A8>Щелчок — открыть список систем</color>");
            TooltipHelper.Attach(c.gameObject, tip);
        }

        private static string Short(float v) => v >= 10000f ? $"{v / 1000f:0.#}k" : $"{v:0}";

        private static float PlayerStock(string r)
        {
            var eco = EconomyManager.Instance;
            if (eco == null) return 0f;
            return r switch
            {
                "energy" => eco.EnergyCredits,
                "minerals" => eco.Minerals,
                "alloys" => eco.Alloys,
                "influence" => eco.Influence,
                _ => 0f
            };
        }

        /// <summary>Ячейка сетки: иконка, запас (или «в месяц»), значок суммы на столе. Щелчок — положить ещё шаг.</summary>
        private void Cell(RectTransform grid, int index, DealItemKind k, bool fromAI, string caption, bool enabled, string blocker)
        {
            int col = index % 4, row = index / 4;
            var put = _deal.Find(fromAI, k);
            bool onTable = put != null;
            Color tint = DealCatalog.Tint(k);
            float a = enabled ? 1f : 0.35f;

            var c = LGBuild.Panel(grid, "Cell_" + k, onTable ? new Color(0.22f, 0.16f, 0.04f, 0.96f) : CellBg, raycast: true);
            c.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(col * (CellSize + CellGap), -row * (CellSize + CellGap)), new Vector2(CellSize, CellSize));
            var fx = LG.Platter(c.gameObject, 6f);
            fx.SetRim(onTable ? new Color(Gold.r, Gold.g, Gold.b, 0.9f) : new Color(tint.r, tint.g, tint.b, enabled ? 0.40f : 0.12f));
            fx.FillMultiplier = 2.4f;

            var ic = LGIcons.Create(c.transform, DealCatalog.Icon(k), 30, new Color(tint.r, tint.g, tint.b, a));
            ic.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(30, 30));
            var cap = LGBuild.Label(c.transform, caption, 12, new Color(Primary.r, Primary.g, Primary.b, a), TextAnchor.MiddleCenter, bold: true);
            cap.rectTransform.anchorMin = new Vector2(0f, 0f);
            cap.rectTransform.anchorMax = new Vector2(1f, 0f);
            cap.rectTransform.pivot = new Vector2(0.5f, 0f);
            cap.rectTransform.sizeDelta = new Vector2(0f, 18f);
            cap.rectTransform.anchoredPosition = new Vector2(0f, 7f);

            if (onTable)
            {
                var badge = LGBuild.Panel(c.transform, "OnTable", new Color(0.55f, 0.40f, 0.08f, 1f));
                badge.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-3, -3), new Vector2(40, 16));
                LG.Platter(badge.gameObject, 3f).FillMultiplier = 2.6f;
                var bt = LGBuild.Label(badge.transform, Short(put.Amount), 10, Color.white, TextAnchor.MiddleCenter, bold: true);
                bt.raycastTarget = false;
            }

            var btn = c.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.interactable = enabled;
            btn.onClick.AddListener(() => AddToTable(k, fromAI));
            string tip = $"<b>{DealCatalog.Name(k)}</b>\n{DealCatalog.Hint(k)}";
            if (DealCatalog.IsResource(k)) tip += $"\n{(fromAI ? Loc.T("У них") : Loc.T("У вас"))}: {caption}";
            if (onTable) tip += Loc.F("\n<color=#F2C747>На столе: {0}</color>", DealCatalog.Describe(put));
            if (blocker != null) tip += $"\n<color=#FF8080>{blocker}</color>";
            else if (!enabled && _viewingOffer) tip += Loc.T("\n<color=#8AA2A8>Сначала ответьте на их предложение или измените его</color>");
            else tip += Loc.F("\n<color=#8AA2A8>Щелчок — положить на стол (+{0:0})</color>", DealCatalog.Step(k));
            TooltipHelper.Attach(c.gameObject, tip);
        }

        private void AddToTable(DealItemKind k, bool fromAI)
        {
            if (_viewingOffer) return;
            bool removed = false;
            if (DealCatalog.IsTreaty(k))
            {
                if (_deal.HasTreaty(k)) { _deal.Treaties.RemoveAll(t => t.Kind == k); removed = true; }
                else _deal.Treaties.Add(new DealItem(k, 0f));
            }
            else
            {
                var have = _deal.Find(fromAI, k);
                if (have != null) Adjust(have, fromAI, +1);
                else _deal.Side(fromAI).Add(new DealItem(k, DealCatalog.Step(k)));
            }
            SFXManager.Play(removed ? Sfx.DealRemove : Sfx.DealPlace);
            RefreshDeal();
        }

        private void Adjust(DealItem item, bool fromAI, int dir)
        {
            float step = DealCatalog.Step(item.Kind);
            float max = float.MaxValue;
            if (DealCatalog.IsResource(item.Kind))
            {
                string r = DealCatalog.ResourceKey(item.Kind);
                max = fromAI ? Current.Stock(r) : PlayerStock(r);
                max = Mathf.Floor(max / step) * step;
            }
            item.Amount = Mathf.Clamp(item.Amount + dir * step, 0f, Mathf.Max(step, max));
            if (item.Amount <= 0f) _deal.Side(fromAI).Remove(item);
        }

        // ---------- Выбор систем ----------

        private void BuildSystemPicker(RectTransform rt)
        {
            var (p, t) = FramedPanel(rt, "SystemPicker", new Vector2(1, 0.5f), new Vector2(-Edge - SideW - 14f, 40f), new Vector2(360f, 520f), "");
            _picker = p.gameObject;
            _pickerTitle = t;
            var close = LGBuild.Button(p, "Close", UIManager.DS.BtnNeutral, new Color(Cyan.r, Cyan.g, Cyan.b, 0.4f), () => { _picker.SetActive(false); RefreshItemLists(); }, LGIcon.Close, null, 10);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -8), new Vector2(24, 24));
            var hint = LGBuild.Label(p, Loc.T("Щелчок — положить систему на стол или убрать её"), 11, Muted, TextAnchor.MiddleLeft);
            hint.rectTransform.TopBand(42, 18, 16, 16);
            var host = LGBuild.Rect(p, "List");
            host.Stretch(12, 12, 12, 66);
            _pickerList = LGBuild.ScrollList(host, 4f, 2);
            _picker.SetActive(false);
        }

        private void TogglePicker(bool fromAI)
        {
            if (_picker == null) return;
            bool show = !(_picker.activeSelf && _pickerFromAI == fromAI);
            _pickerFromAI = fromAI;
            _picker.SetActive(show);
            _picker.transform.SetAsLastSibling();
            SFXManager.Play(show ? Sfx.UiClick : Sfx.UiBack);
            RefreshItemLists();
            RefreshPicker();
        }

        /// <summary>Список систем стороны: сначала пограничные с другой стороной, затем самые ценные.</summary>
        private void RefreshPicker()
        {
            if (_picker == null || !_picker.activeSelf) return;
            var ai = Current;
            LGBuild.Clear(_pickerList);
            if (ai == null) return;
            int giver = _pickerFromAI ? ai.OwnerId : 0, taker = _pickerFromAI ? 0 : ai.OwnerId;
            _pickerTitle.text = _pickerFromAI ? Loc.F("СИСТЕМЫ: {0}", ai.AIName.ToUpper()) : Loc.T("ВАШИ СИСТЕМЫ");
            _pickerTitle.color = _pickerFromAI ? Color.Lerp(ai.AIEmpireColor, Color.white, 0.25f) : Gold;

            var list = new List<StarSystem>();
            foreach (var sys in EmpireStats.Systems) if (sys.OwnerId == giver) list.Add(sys);
            list.Sort((a, b) =>
            {
                bool ba = AIEmpireManager.Borders(a, taker), bb = AIEmpireManager.Borders(b, taker);
                if (ba != bb) return ba ? -1 : 1;
                return AIEmpireManager.SystemWorth(b).CompareTo(AIEmpireManager.SystemWorth(a));
            });
            if (list.Count == 0) { EmptyNote(_pickerList, Loc.T("систем нет")); return; }

            bool frozen = _viewingOffer || ai.IsEliminated;
            int shown = 0;
            foreach (var sys in list)
            {
                if (++shown > 40) break;
                PickerRow(ai, sys, giver, taker, frozen);
            }
        }

        private void PickerRow(AIEmpireManager ai, StarSystem sys, int giver, int taker, bool frozen)
        {
            bool fromAI = _pickerFromAI;
            bool on = _deal.HasSystem(sys.Id);
            string block = ai.SystemBlocker(sys.Id, giver);
            int pop = 0, colonies = 0;
            foreach (var pl in sys.Planets) if (pl.Population > 0) { pop += pl.Population; colonies++; }
            bool border = AIEmpireManager.Borders(sys, taker);
            bool claimed = fromAI && CasusBelli.IsClaimed(sys.Id, ai.OwnerId);
            float worth = AIEmpireManager.SystemWorth(sys);

            var row = LGBuild.Panel(_pickerList, "Sys", on ? new Color(0.22f, 0.16f, 0.04f, 0.96f) : new Color(0.03f, 0.10f, 0.12f, 0.85f), raycast: true);
            LGBuild.Height(row.gameObject, 40f);
            var fx = LG.Platter(row.gameObject, 4f);
            fx.SetRim(on ? new Color(Gold.r, Gold.g, Gold.b, 0.85f) : new Color(1f, 1f, 1f, 0.10f));
            fx.FillMultiplier = 2.6f;
            float a = block == null && !frozen ? 1f : 0.45f;
            var ic = LGIcons.Create(row.transform, colonies > 0 ? LGIcon.Planet : LGIcon.Starbase, 16, new Color(1f, 0.85f, 0.55f, a));
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(16, 16));
            var name = LGBuild.Label(row.transform, sys.Name + (claimed ? Loc.T("  <color=#F2C747>· ваша бывшая</color>") : ""), 13, new Color(1f, 1f, 1f, a), TextAnchor.UpperLeft, bold: true);
            name.rectTransform.Stretch(34, 18, 64, 3);
            string info = colonies > 0 ? Loc.F("колоний {0} · население {1}", colonies, pop) : Loc.T("форпост");
            if (border) info += Loc.T(" · у границы");
            var sub = LGBuild.Label(row.transform, info, 11, new Color(Muted.r, Muted.g, Muted.b, a), TextAnchor.LowerLeft);
            sub.rectTransform.Stretch(34, 3, 64, 20);
            var w = LGBuild.Label(row.transform, Loc.F("≈{0:0}", worth), 12, new Color(Gold.r, Gold.g, Gold.b, a), TextAnchor.MiddleRight, bold: true);
            w.rectTransform.Stretch(0, 0, 10, 0);

            string tip = $"<b>{sys.Name}</b>\n" + (colonies > 0 ? Loc.F("Колоний: {0}, население {1}", colonies, pop) : Loc.T("Форпост без колоний")) +
                         Loc.F("\nЦенность ≈ {0:0} гелия-3", worth) +
                         (fromAI ? Loc.T("\n<color=#8AA2A8>Свою систему отдадут примерно за полторы её цены</color>") : (border ? Loc.T("\n<color=#8AA2A8>Граничит с ними — для них это ценно</color>") : Loc.T("\n<color=#8AA2A8>Далеко от их границ — для них стоит вдвое меньше</color>"))) +
                         (claimed ? Loc.T("\n<color=#F2C747>Отнята у вас — по поводу «Вернуть утраченное» уступят дешевле</color>") : "") +
                         (block != null ? $"\n<color=#FF8080>{block}</color>" : on ? Loc.T("\n<color=#F2C747>На столе — щёлкните, чтобы убрать</color>") : Loc.T("\nЩелчок — положить на стол"));
            TooltipHelper.Attach(row.gameObject, tip);
            if (frozen || (block != null && !on)) return;
            var btn = row.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            int id = sys.Id;
            btn.onClick.AddListener(() => ToggleSystem(id, fromAI));
        }

        private void ToggleSystem(int systemId, bool fromAI)
        {
            if (_viewingOffer) return;
            var side = _deal.Side(fromAI);
            int idx = side.FindIndex(x => DealCatalog.IsSystem(x.Kind) && (int)x.Amount == systemId);
            if (idx >= 0) side.RemoveAt(idx);
            else side.Add(new DealItem(DealItemKind.System, systemId));
            SFXManager.Play(idx >= 0 ? Sfx.DealRemove : Sfx.DealPlace);
            RefreshDeal();
        }

        // ---------- Проекция сделки и шанс согласия ----------

        private Deal Shown => _viewingOffer && Current?.PendingDeal != null ? Current.PendingDeal : _deal;

        private void RefreshTable()
        {
            var ai = Current;
            LGBuild.Clear(_tableAI);
            LGBuild.Clear(_tablePlayer);
            if (ai == null) return;
            var d = Shown;
            bool ro = _viewingOffer;

            foreach (var t in d.Treaties) { TableRow(_tableAI, t, true, ro, true); TableRow(_tablePlayer, t, false, ro, true); }
            foreach (var i in d.FromAI) TableRow(_tableAI, i, true, ro, false);
            foreach (var i in d.FromPlayer) TableRow(_tablePlayer, i, false, ro, false);
            if (d.FromAI.Count == 0 && d.Treaties.Count == 0) EmptyNote(_tableAI, ro ? Loc.T("ничего") : Loc.T("их ресурсы — справа вверху"));
            if (d.FromPlayer.Count == 0 && d.Treaties.Count == 0) EmptyNote(_tablePlayer, ro ? Loc.T("ничего") : Loc.T("ваши — справа внизу"));

            if (_viewingOffer)
            {
                var kind = ai.PendingOffer;
                _tableTitle.text = kind == AIEmpireManager.OfferKind.Demand ? Loc.T("ТРЕБОВАНИЕ") : Loc.T("ИХ ПРЕДЛОЖЕНИЕ");
                _tableTitle.color = kind == AIEmpireManager.OfferKind.Demand ? Red : Gold;
                _reaction.text = Loc.F("<color={0}>Ответ ждут ещё {1} дн.</color>  ·  {2}", LGBuild.Hex(Gold), ai.PendingOfferDays, ai.PendingOfferReason);
                _fee.text = kind == AIEmpireManager.OfferKind.Demand ? Loc.T("<color=#FF8080>Отказ ухудшит отношение</color>") : "";
                SetChance(-1f, Muted);

                SetButton(_btnPropose, _imgPropose, _lblPropose, Loc.T("Принять"), new Color(0.10f, 0.32f, 0.20f, 0.96f), true);
                SetButton(_btnBalance, _imgBalance, _lblBalance, Loc.T("Изменить условия"), RowBg, kind == AIEmpireManager.OfferKind.Trade);
                SetButton(_btnBack, _imgBack, _lblBack, Loc.T("Отклонить"), new Color(0.30f, 0.08f, 0.10f, 0.96f), true);
                return;
            }

            _tableTitle.text = Loc.T("НА СТОЛЕ ПЕРЕГОВОРОВ");
            _tableTitle.color = Primary;
            float cost = AIEmpireManager.ProposalCost(d);
            _fee.text = cost > 0f ? Loc.F("Переговоры: −{0:0} влияния", cost) : "";

            if (d.IsEmpty)
            {
                _reaction.text = Loc.T("<color=#8AA2A8>Положите на стол то, что хотите получить или отдать, или выберите договор на краю стола</color>");
                SetChance(-1f, Muted);
            }
            else
            {
                var v = ai.Evaluate(d);
                string verdict;
                Color vc;
                if (v.Blocker != null) { verdict = v.Blocker; vc = Red; }
                else if (d.IsGift) { verdict = Loc.T("Примут с благодарностью"); vc = Green; }
                else if (v.Accept) { verdict = v.Balance - v.Required > 25f ? Loc.T("Согласятся с радостью") : Loc.T("Согласятся"); vc = Green; }
                else if (v.Willingness > 0.35f) { verdict = Loc.T("Колеблются — добавьте что-нибудь"); vc = Gold; }
                else { verdict = Loc.T("Откажут"); vc = Red; }
                _reaction.text = Loc.F("Реакция: <color={0}>{1}</color>", LGBuild.Hex(vc), verdict);
                SetChance(v.Blocker != null ? 0.03f : d.IsGift ? 1f : v.Willingness, vc);
            }

            bool alive = !ai.IsEliminated;
            SetButton(_btnPropose, _imgPropose, _lblPropose, d.IsGift ? Loc.T("Подарить") : Loc.T("Предложить сделку"), new Color(0.30f, 0.22f, 0.05f, 0.96f), alive && !d.IsEmpty);
            SetButton(_btnBalance, _imgBalance, _lblBalance, Loc.T("Что вас устроит?"), RowBg, alive);
            SetButton(_btnBack, _imgBack, _lblBack, d.IsEmpty ? Loc.T("Завершить переговоры") : Loc.T("Очистить стол"), RowBg, true);
        }

        /// <summary>Сегментная шкала шанса согласия: красный → золотой → зелёный; меньше нуля — «нет оценки».</summary>
        private void SetChance(float chance, Color col)
        {
            int lit = chance < 0f ? 0 : Mathf.Clamp(Mathf.RoundToInt(chance * ChanceSegs), chance > 0f ? 1 : 0, ChanceSegs);
            for (int i = 0; i < ChanceSegs; i++)
            {
                float t = i / (float)(ChanceSegs - 1);
                Color c = t < 0.5f ? Color.Lerp(Red, Gold, t * 2f) : Color.Lerp(Gold, Green, (t - 0.5f) * 2f);
                _chanceSegs[i].color = i < lit ? c : new Color(1f, 1f, 1f, 0.07f);
            }
            _chanceVal.text = chance < 0f ? "—" : $"{Mathf.Clamp01(chance) * 100f:0}%";
            _chanceVal.color = chance < 0f ? Muted : col;
        }

        private static void SetButton(Button b, Image img, Text lbl, string text, Color tint, bool enabled)
        {
            lbl.text = text;
            img.color = enabled ? tint : UIManager.DS.BtnDisabled;
            lbl.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            b.interactable = enabled;
        }

        private static void EmptyNote(RectTransform list, string text)
        {
            var t = LGBuild.Label(list, text, 12, new Color(Muted.r, Muted.g, Muted.b, 0.6f), TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Italic;
            LGBuild.Height(t.gameObject, 30f);
        }

        private void TableRow(RectTransform list, DealItem item, bool fromAI, bool readOnly, bool treaty)
        {
            Color tint = DealCatalog.Tint(item.Kind);
            var row = LGBuild.Panel(list, "Row", new Color(0.03f, 0.10f, 0.12f, 0.85f));
            LGBuild.Height(row.gameObject, 30f);
            var fx = LG.Platter(row.gameObject, 4f);
            fx.SetRim(new Color(tint.r, tint.g, tint.b, 0.35f));
            fx.FillMultiplier = 2.6f;
            fx.SpecularMultiplier = 0.2f;

            bool system = DealCatalog.IsSystem(item.Kind);
            var ic = LGIcons.Create(row.transform, treaty ? LGIcon.Handshake : DealCatalog.Icon(item.Kind), 14, tint);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(14, 14));
            string text = treaty ? DealCatalog.Name(item.Kind) : DealCatalog.Describe(item);
            var t = LGBuild.Label(row.transform, text, 12, Primary, TextAnchor.MiddleLeft);
            t.rectTransform.Stretch(28, 0, readOnly ? 8 : (treaty || system ? 32 : 82), 0);
            TooltipHelper.Attach(row.gameObject, $"<b>{DealCatalog.Name(item.Kind)}</b>\n{DealCatalog.Hint(item.Kind)}" + (treaty ? Loc.T("\n<color=#8AA2A8>Договор взаимный — действует для обеих сторон</color>") : ""));
            row.raycastTarget = true;
            if (readOnly) return;

            float x = -5f;
            SmallButton(row.rectTransform, LGIcon.Close, ref x, () =>
            {
                if (treaty) _deal.Treaties.Remove(item); else _deal.Side(fromAI).Remove(item);
                SFXManager.Play(Sfx.DealRemove);
                RefreshDeal();
            });
            if (treaty || system) return;
            SmallText(row.rectTransform, "+", ref x, () => { Adjust(item, fromAI, +1); RefreshDeal(); });
            SmallText(row.rectTransform, "−", ref x, () => { Adjust(item, fromAI, -1); RefreshDeal(); });
        }

        private static void SmallButton(RectTransform row, LGIcon icon, ref float x, System.Action act)
        {
            var b = LGBuild.Button(row, "Btn", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.12f), act, icon, null, 6, 3f);
            ((RectTransform)b.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(x, 0), new Vector2(22, 22));
            x -= 25f;
        }

        private static void SmallText(RectTransform row, string label, ref float x, System.Action act)
        {
            var b = LGBuild.Button(row, "Btn", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.12f), act, null, label, 14, 3f);
            ((RectTransform)b.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(x, 0), new Vector2(22, 22));
            x -= 25f;
        }

        // ================================================================ Действия

        private void OnBalanceClicked()
        {
            var ai = Current;
            if (ai == null) return;
            var leader = LeaderPortraits.ForFaction(ai.Faction);
            if (_viewingOffer)
            {
                // Взять их предложение за основу встречного
                _deal = ai.PendingDeal.Clone();
                _deal.ByAI = false;
                _viewingOffer = false;
                SFXManager.Play(Sfx.UiClick);
                RefreshDeal();
                return;
            }
            var result = ai.BalanceDeal(_deal, out string reason);
            if (result == null)
            {
                Say(DiplomacyLines.Get(leader, DiplomacyLines.Kind.NoDeal) + (reason != null ? $" ({reason.ToLower()})" : ""));
                SFXManager.Play(Sfx.UiDenied);
                StartCoroutine(Shake(_centerPanel));
                return;
            }
            _deal = result;
            Say(DiplomacyLines.Get(leader, DiplomacyLines.Kind.Counter));
            SFXManager.Play(Sfx.UiConfirm);
            RefreshDeal();
        }

        private void OnBackClicked()
        {
            var ai = Current;
            if (ai == null) return;
            if (_viewingOffer)
            {
                bool demand = ai.PendingOffer == AIEmpireManager.OfferKind.Demand;
                ai.DeclinePendingDeal();
                _viewingOffer = false;
                SFXManager.Play(Sfx.UiBack);
                Say(DiplomacyLines.Get(LeaderPortraits.ForFaction(ai.Faction), demand ? DiplomacyLines.Kind.AtWar : DiplomacyLines.Kind.Reject));
                RefreshAll();
                return;
            }
            if (!_deal.IsEmpty)
            {
                _deal.Clear();
                SFXManager.Play(Sfx.UiBack);
                RefreshDeal();
                return;
            }
            Close();
        }

        private void OnProposeClicked()
        {
            var ai = Current;
            if (ai == null) return;
            var leader = LeaderPortraits.ForFaction(ai.Faction);

            if (_viewingOffer)
            {
                var kind = ai.PendingOffer;
                if (ai.AcceptPendingDeal())
                {
                    _viewingOffer = false;
                    SFXManager.Play(kind == AIEmpireManager.OfferKind.Peace ? Sfx.PeaceSigned
                        : kind == AIEmpireManager.OfferKind.Pact || kind == AIEmpireManager.OfferKind.Alliance ? Sfx.PactSigned : Sfx.DealAccepted);
                    if (kind == AIEmpireManager.OfferKind.Trade) Achievements.Unlock(Ach.Deal);
                    Say(DiplomacyLines.Get(leader, DiplomacyLines.Kind.Accept));
                }
                else SFXManager.Play(Sfx.UiDenied);
                RefreshAll();
                return;
            }

            if (_deal.IsEmpty) return;
            bool gift = _deal.IsGift;
            bool peace = _deal.HasTreaty(DealItemKind.Peace), pact = _deal.HasTreaty(DealItemKind.Pact) || _deal.HasTreaty(DealItemKind.Alliance);
            if (ai.PlayerPropose(_deal, out var verdict))
            {
                SFXManager.Play(peace ? Sfx.PeaceSigned : pact ? Sfx.PactSigned : Sfx.DealAccepted);
                if (!gift && !peace && !pact) Achievements.Unlock(Ach.Deal);
                Say(DiplomacyLines.Get(leader, gift ? DiplomacyLines.Kind.Gift : DiplomacyLines.Kind.Accept));
                _deal = new Deal();
            }
            else
            {
                SFXManager.Play(Sfx.UiDenied);
                string line = DiplomacyLines.Get(leader, DiplomacyLines.Kind.Reject);
                if (verdict.Blocker != null) line += $" ({verdict.Blocker.ToLower()})";
                Say(line);
                StartCoroutine(Shake(_centerPanel));
            }
            RefreshAll();
        }

        private static IEnumerator Shake(RectTransform r)
        {
            if (r == null) yield break;
            Vector2 basePos = r.anchoredPosition;
            float t = 0f;
            while (t < 0.3f)
            {
                t += Time.unscaledDeltaTime;
                r.anchoredPosition = basePos + new Vector2(Mathf.Sin(t * 70f) * 7f * (1f - t / 0.3f), 0f);
                yield return null;
            }
            r.anchoredPosition = basePos;
        }
    }
}
