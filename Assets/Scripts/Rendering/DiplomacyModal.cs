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
    /// Переговоры в духе Master of Orion: правитель во весь рост в центре, его реплика,
    /// отношение и действующие соглашения, а внизу — стол переговоров: что может дать он,
    /// что можете дать вы и сама сделка. Кнопка «Что вас устроит?» просит ИИ дописать условия.
    /// Предложения ИИ (обмен, договоры, требование дани, мир, пакт) открываются здесь же.
    /// Сцена — комната переговоров: два правителя голограммами друг напротив друга (вы — слева, собеседник — справа)
    /// на светящихся площадках-проекторах, между ними — голографический стол с вращающейся проекцией.
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
        private static readonly Color PanelBg = new Color(0.020f, 0.038f, 0.055f, 0.90f);
        private static readonly Color RowBg = new Color(0.045f, 0.075f, 0.100f, 0.92f);

        private const float SidePanelW = 440f, CenterW = 680f, BottomH = 340f, BottomY = 104f;

        private Canvas _host;
        private GameObject _root;
        private TechTreeBackdrop _backdrop;
        private RawImage _scene;
        private Image _sceneShade, _leaderGlow;
        private RectTransform _portraitHost, _infoLeft, _infoRight, _tabs;
        // Комната переговоров: наш правитель, свечения и площадки обеих сторон, голостол
        private RectTransform _playerHost, _tableSpinA, _tableSpinB;
        private Image _playerGlow, _padAI, _padPlayer, _coneAI, _conePlayer, _tableGlow, _tableRingA, _tableRingB;
        private string _playerKey;
        // Референс-раскладка: статус отношений (кольца), портрет в диалоге, быстрые действия у голостола
        private Image _gaugeTrust, _gaugeChance;
        private Text _gaugeTrustVal, _gaugeChanceVal;
        private RawImage _dialogPortrait;
        private readonly List<(DealItemKind kind, Button btn, Image bg)> _quick = new List<(DealItemKind, Button, Image)>();
        private float _holoT;
        private const float LeaderX = 290f, LeaderW = 480f, LeaderH = 716f;
        private RectTransform _aiList, _playerList, _tableAI, _tablePlayer, _centerPanel;
        private Text _aiListTitle, _speechName, _speechText, _speechState, _reaction, _fee, _tableTitle, _colAI;
        private Image _speechStateBg;
        private RectTransform _reactionBar;
        private Button _btnBalance, _btnBack, _btnPropose;
        private Text _lblBalance, _lblBack, _lblPropose;
        private Image _imgBalance, _imgBack, _imgPropose;

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
        private string _portraitKey;

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
                    NotificationCenter.Show("Контактов нет",
                        "Мы ещё не встретили другие цивилизации. Исследуйте галактику — контакт случится, когда наши корабли или границы увидят чужую империю",
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
            Color ec = ai.AIEmpireColor;
            _backdrop.SetColors(ec, Color.Lerp(ec, new Color(0.3f, 0.4f, 1f), 0.5f));
            ApplyScene(ai);
            _leaderGlow.color = new Color(ec.r, ec.g, ec.b, 0.20f);
            Color oc = FleetIndicator.OwnColor;
            _playerGlow.color = new Color(oc.r, oc.g, oc.b, 0.16f);
            _padAI.color = new Color(ec.r, ec.g, ec.b, 0.85f);
            _padPlayer.color = new Color(oc.r, oc.g, oc.b, 0.85f);
            _coneAI.color = new Color(ec.r, ec.g, ec.b, 0.10f);
            _conePlayer.color = new Color(oc.r, oc.g, oc.b, 0.10f);
            Color mix = Color.Lerp(oc, ec, 0.5f);
            _tableGlow.color = new Color(mix.r, mix.g, mix.b, 0.30f);
            _tableRingA.color = new Color(mix.r, mix.g, mix.b, 0.70f);
            _tableRingB.color = new Color(ec.r, ec.g, ec.b, 0.55f);
            BuildPortrait(ai);
            BuildPlayerPortrait();
            var face = LeaderPortraits.ForFaction(ai.Faction);
            _dialogPortrait.texture = face != null ? face.Texture : null;
            _dialogPortrait.enabled = _dialogPortrait.texture != null;
            _dialogPortrait.uvRect = new Rect(0.30f, 0.54f, 0.40f, 0.40f);   // лицо — верхняя середина кадра
            var leader = LeaderPortraits.ForFaction(ai.Faction);
            if (_viewingOffer) Say(DiplomacyLines.Get(leader, DiplomacyLines.OfferLine(ai.PendingOffer, ai.PendingDeal)));
            else Say(DiplomacyLines.Get(leader, DiplomacyLines.GreetingFor(ai)));
            RefreshAll();
        }

        private void Update()
        {
            if (!IsOpen) return;

            // Голостол вращается, площадки чуть «дышат»
            _holoT += Time.unscaledDeltaTime;
            if (_tableSpinA != null) _tableSpinA.localRotation = Quaternion.Euler(0, 0, _holoT * 14f);
            if (_tableSpinB != null) _tableSpinB.localRotation = Quaternion.Euler(0, 0, -_holoT * 22f);
            float breathe = 1f + Mathf.Sin(_holoT * 2.2f) * 0.03f;
            if (_padAI != null) _padAI.rectTransform.localScale = new Vector3(breathe, breathe, 1f);
            if (_padPlayer != null) _padPlayer.rectTransform.localScale = new Vector3(breathe, breathe, 1f);

            // Реплика печатается
            if (_speechShown < _speechFull.Length)
            {
                _speechShown = Mathf.Min(_speechFull.Length, _speechShown + Time.unscaledDeltaTime * 55f);
                _speechText.text = _speechFull.Substring(0, Mathf.FloorToInt(_speechShown));
            }

            if (_confirmAction != null && Time.unscaledTime > _confirmUntil) { _confirmAction = null; RefreshInfo(); }

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
            return $"{ai.OwnerId}|{Mathf.RoundToInt(ai.Opinion)}|{ai.AtWar}|{ai.HasPact}|{ai.TruceDays / 30}|{ai.PendingOffer}|{ai.PendingOfferDays / 10}|" +
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

        private static Image GlassPanel(Transform parent, string name, Color rim)
        {
            var p = LGBuild.Panel(parent, name, PanelBg, raycast: true);
            var fx = LG.Platter(p.gameObject, 6f);
            fx.SetRim(rim);
            fx.FillMultiplier = 2.6f;
            fx.SpecularMultiplier = 0.2f;
            return p;
        }

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
            _sceneShade = LGBuild.Panel(rt, "SceneShade", new Color(0.005f, 0.01f, 0.018f, 0.35f));
            _sceneShade.rectTransform.Stretch();
            LG.Ignore(_sceneShade.gameObject);

            // Комната переговоров: свет за фигурами, площадки-проекторы, конусы света и голостол
            _playerGlow = Glow(rt, "PlayerGlow", new Vector2(-LeaderX, -380f), 900f);
            _leaderGlow = Glow(rt, "LeaderGlow", new Vector2(LeaderX, -380f), 900f);
            float padY = BottomY + BottomH + 18f;
            _conePlayer = Holo(rt, "ConePlayer", ConeSprite(), new Vector2(-LeaderX, padY), new Vector2(340f, 520f), new Vector2(0.5f, 0f));
            _coneAI = Holo(rt, "ConeAI", ConeSprite(), new Vector2(LeaderX, padY), new Vector2(340f, 520f), new Vector2(0.5f, 0f));

            // Правители: мы — слева (зеркально, лицом к собеседнику), собеседник — справа
            _playerHost = LGBuild.Rect(rt, "PlayerLeader");
            _playerHost.anchorMin = _playerHost.anchorMax = new Vector2(0.5f, 1f);
            _playerHost.pivot = new Vector2(0.5f, 1f);
            _playerHost.sizeDelta = new Vector2(LeaderW, LeaderH);
            _playerHost.anchoredPosition = new Vector2(-LeaderX, -40f);
            _playerHost.localScale = new Vector3(-1f, 1f, 1f);
            _portraitHost = LGBuild.Rect(rt, "Leader");
            _portraitHost.anchorMin = _portraitHost.anchorMax = new Vector2(0.5f, 1f);
            _portraitHost.pivot = new Vector2(0.5f, 1f);
            _portraitHost.sizeDelta = new Vector2(LeaderW, LeaderH);
            _portraitHost.anchoredPosition = new Vector2(LeaderX, -40f);

            // Площадки-проекторы под фигурами (эллипсы над столом сделок)
            _padPlayer = Holo(rt, "PadPlayer", PadSprite(), new Vector2(-LeaderX, padY), new Vector2(360f, 70f), new Vector2(0.5f, 0.5f));
            _padAI = Holo(rt, "PadAI", PadSprite(), new Vector2(LeaderX, padY), new Vector2(360f, 70f), new Vector2(0.5f, 0.5f));

            // Голостол между правителями: «диск» в перспективе (круги в сплюснутом контейнере), вращаются навстречу
            var table = LGBuild.Rect(rt, "HoloTable");
            table.anchorMin = table.anchorMax = new Vector2(0.5f, 0f);
            table.pivot = new Vector2(0.5f, 0.5f);
            table.sizeDelta = new Vector2(440f, 440f);
            table.anchoredPosition = new Vector2(0f, padY + 80f);
            table.localScale = new Vector3(1f, 0.30f, 1f);
            _tableGlow = Holo(table, "Glow", FactionSelectScreen.RadialSprite(), Vector2.zero, new Vector2(520f, 520f), new Vector2(0.5f, 0.5f), true);
            _tableSpinA = LGBuild.Rect(table, "SpinA");
            _tableSpinA.Stretch();
            _tableRingA = Holo(_tableSpinA, "RingA", DashRingSprite(), Vector2.zero, new Vector2(440f, 440f), new Vector2(0.5f, 0.5f), true);
            _tableSpinB = LGBuild.Rect(table, "SpinB");
            _tableSpinB.Stretch();
            _tableRingB = Holo(_tableSpinB, "RingB", PadSprite(), Vector2.zero, new Vector2(300f, 300f), new Vector2(0.5f, 0.5f), true);

            // Затемнение снизу — под панелями стола
            var shade = LGBuild.Panel(rt, "BottomShade", new Color(0f, 0.01f, 0.02f, 0.55f));
            shade.rectTransform.anchorMin = new Vector2(0f, 0f);
            shade.rectTransform.anchorMax = new Vector2(1f, 0f);
            shade.rectTransform.pivot = new Vector2(0.5f, 0f);
            shade.rectTransform.sizeDelta = new Vector2(0f, BottomY + BottomH + 30f);
            LG.Ignore(shade.gameObject);

            BuildInfoLeft(rt);
            BuildInfoRight(rt);
            BuildStatus(rt);
            BuildBottom(rt);
            BuildSpeech(rt);          // диалог — в шапке центральной панели, как в референсе
            BuildQuickActions(rt, padY);

            var close = LGBuild.Button(rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -20), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, "Закрыть  <color=#8AA2A8>(Esc)</color>");

            _root.SetActive(false);
        }

        private void BuildInfoLeft(RectTransform rt)
        {
            var frame = FramedPanel(rt, "InfoLeftFrame", new Vector2(0, 1), new Vector2(20f, -14f), new Vector2(458f, 452f), "ОТНОШЕНИЯ", TextAnchor.MiddleLeft);
            _infoLeft = LGBuild.Rect(rt, "InfoLeft");
            _infoLeft.anchorMin = _infoLeft.anchorMax = new Vector2(0, 1);
            _infoLeft.pivot = new Vector2(0, 1);
            _infoLeft.sizeDelta = new Vector2(430f, 396f);
            _infoLeft.anchoredPosition = new Vector2(34f, -60f);
        }

        private void BuildInfoRight(RectTransform rt)
        {
            FramedPanel(rt, "InfoRightFrame", new Vector2(1, 1), new Vector2(-62f, -14f), new Vector2(468f, 392f), "ИМПЕРИЯ", TextAnchor.MiddleRight);
            _infoRight = LGBuild.Rect(rt, "InfoRight");
            _infoRight.anchorMin = _infoRight.anchorMax = new Vector2(1, 1);
            _infoRight.pivot = new Vector2(1, 1);
            _infoRight.sizeDelta = new Vector2(440f, 340f);
            _infoRight.anchoredPosition = new Vector2(-76f, -58f);

            _tabs = LGBuild.Rect(rt, "Tabs");
            _tabs.anchorMin = _tabs.anchorMax = new Vector2(1, 1);
            _tabs.pivot = new Vector2(1, 1);
            _tabs.sizeDelta = new Vector2(440f, 64f);
            _tabs.anchoredPosition = new Vector2(-76f, -416f);
        }

        /// <summary>
        /// «Диалог» — шапка центральной панели (как в референсе): портрет собеседника, имя, состояние и реплика;
        /// ниже — сама сделка.
        /// </summary>
        private void BuildSpeech(RectTransform rt)
        {
            var b = LGBuild.Rect(_centerPanel, "Dialog");
            b.TopBand(0, 98, 0, 0);

            var pf = LGBuild.Panel(b, "PortraitFrame", new Color(0.03f, 0.07f, 0.09f, 1f));
            pf.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -12), new Vector2(78, 78));
            LG.Platter(pf.gameObject, 8f).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f));
            var pm = LGBuild.Rect(pf.transform, "Mask");
            pm.Stretch(3, 3, 3, 3);
            pm.gameObject.AddComponent<RectMask2D>();
            _dialogPortrait = pm.gameObject.AddComponent<RawImage>();
            _dialogPortrait.raycastTarget = false;
            LG.Ignore(pm.gameObject);

            _speechName = LGBuild.Label(b, "", 13, Primary, TextAnchor.UpperLeft, bold: true);
            _speechName.rectTransform.TopBand(12, 20, 104, 150);
            _speechStateBg = LGBuild.Panel(b, "State", new Color(0.1f, 0.2f, 0.2f, 1f));
            _speechStateBg.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14, -10), new Vector2(128, 22));
            var sfx = LG.Platter(_speechStateBg.gameObject, 4f);
            sfx.FillMultiplier = 2.6f;
            _speechState = LGBuild.Label(_speechStateBg.transform, "", 11, Primary, TextAnchor.MiddleCenter, bold: true);
            _speechText = LGBuild.Label(b, "", 13, new Color(0.88f, 0.93f, 0.96f), TextAnchor.UpperLeft, wrap: true);
            _speechText.supportRichText = false;
            _speechText.rectTransform.Stretch(104, 6, 16, 36);

            var line = LGBuild.Panel(_centerPanel, "DialogLine", new Color(Cyan.r, Cyan.g, Cyan.b, 0.22f));
            line.rectTransform.TopBand(98, 1, 14, 14);
            LG.Ignore(line.gameObject);
        }

        /// <summary>Рамка-панель со шапкой (заголовок и линия под ним) — как панели референса.</summary>
        private static Image FramedPanel(RectTransform rt, string name, Vector2 anchor, Vector2 pos, Vector2 size, string title, TextAnchor align)
        {
            var p = LGBuild.Panel(rt, name, PanelBg, raycast: false);
            var r = p.rectTransform;
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = anchor;
            r.sizeDelta = size;
            r.anchoredPosition = pos;
            LG.Glass(p.gameObject, 18f).SetRim(new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f));
            var head = LGBuild.Rect(r, "Head");
            head.TopBand(8, 28, 18, 18);
            LGBuild.Label(head, title, 14, Primary, align, bold: true);
            var line = LGBuild.Panel(r, "Line", new Color(Cyan.r, Cyan.g, Cyan.b, 0.25f));
            line.rectTransform.TopBand(38, 1, 12, 12);
            LG.Ignore(line.gameObject);
            return p;
        }

        /// <summary>«Статус отношений»: два кольца — доверие (отношение) и шанс согласия на сделку на столе.</summary>
        private void BuildStatus(RectTransform rt)
        {
            var p = FramedPanel(rt, "Status", new Vector2(0, 1), new Vector2(20f, -476f), new Vector2(458f, 150f), "СТАТУС ОТНОШЕНИЙ", TextAnchor.MiddleLeft);
            (_gaugeTrust, _gaugeTrustVal) = Gauge(p.rectTransform, new Vector2(120f, -94f), "ДОВЕРИЕ");
            (_gaugeChance, _gaugeChanceVal) = Gauge(p.rectTransform, new Vector2(338f, -94f), "ШАНС СОГЛАСИЯ");
        }

        private static (Image, Text) Gauge(RectTransform parent, Vector2 center, string label)
        {
            var track = LGBuild.Panel(parent, "Track", new Color(1f, 1f, 1f, 0.08f));
            track.sprite = RingSprite();
            track.raycastTarget = false;
            track.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), center, new Vector2(84, 84));
            LG.Ignore(track.gameObject);
            var fill = LGBuild.Panel(parent, "Fill", Cyan);
            fill.sprite = RingSprite();
            fill.raycastTarget = false;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Bottom;
            fill.fillClockwise = true;
            fill.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), center, new Vector2(84, 84));
            LG.Ignore(fill.gameObject);
            var val = LGBuild.Label(parent, "", 15, Color.white, TextAnchor.MiddleCenter, bold: true);
            val.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), center, new Vector2(84, 30));
            var lbl = LGBuild.Label(parent, label, 10, Muted, TextAnchor.MiddleLeft, bold: true);
            lbl.rectTransform.At(new Vector2(0, 1), new Vector2(0f, 0.5f), center + new Vector2(50f, 0f), new Vector2(160, 20));
            return (fill, val);
        }

        /// <summary>Кнопки быстрых действий дугой у переднего края голостола — кладут договор на стол (или убирают).</summary>
        private void BuildQuickActions(RectTransform rt, float padY)
        {
            (DealItemKind k, LGIcon icon, string label)[] defs =
            {
                (DealItemKind.Pact, LGIcon.Handshake, "ПАКТ"),
                (DealItemKind.TradeTreaty, LGIcon.Trade, "ТОРГОВЫЙ ДОГОВОР"),
                (DealItemKind.ResearchTreaty, LGIcon.Research, "НАУЧНЫЙ ДОГОВОР"),
                (DealItemKind.Charts, LGIcon.MapExplored, "ЗВЁЗДНЫЕ КАРТЫ"),
                (DealItemKind.Peace, LGIcon.Peace, "МИР"),
            };
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                float t = (i - (defs.Length - 1) * 0.5f) / ((defs.Length - 1) * 0.5f);   // −1..1
                var b = LGBuild.Button(rt, "Quick", new Color(0.06f, 0.16f, 0.20f), new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f),
                                       () => AddToTable(d.k, false), d.icon, d.label, 9);
                var r = (RectTransform)b.transform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(150f, 30f);
                r.anchoredPosition = new Vector2(t * 330f, padY + 26f + t * t * 26f);
                r.localRotation = Quaternion.Euler(0, 0, -t * 7f);   // повторяют изгиб края стола
                TooltipHelper.Attach(b.gameObject, $"<b>{DealCatalog.Name(d.k)}</b>\nПоложить на стол переговоров (повторный щелчок — убрать)");
                _quick.Add((d.k, b, b.GetComponent<Image>()));
            }
        }

        private void RefreshQuickActions()
        {
            var ai = Current;
            foreach (var q in _quick)
            {
                bool show = ai != null && !ai.IsEliminated && !_viewingOffer &&
                            (q.kind == DealItemKind.Peace ? ai.AtWar : !ai.AtWar && !(q.kind == DealItemKind.Pact && ai.HasPact));
                q.btn.gameObject.SetActive(show);
                bool on = _deal.HasTreaty(q.kind);
                q.bg.color = on ? new Color(0.40f, 0.30f, 0.08f) : new Color(0.06f, 0.16f, 0.20f);
            }
        }

        /// <summary>Кольца статуса: доверие из отношения, шанс — из реакции на текущую сделку.</summary>
        private void RefreshStatus(AIEmpireManager ai)
        {
            if (_gaugeTrust == null || ai == null) return;
            float trust = Mathf.InverseLerp(AIEmpireManager.OpinionMin, AIEmpireManager.OpinionMax, ai.Opinion);
            Color oc = OpinionColor(ai.Opinion);
            _gaugeTrust.fillAmount = trust;
            _gaugeTrust.color = oc;
            _gaugeTrustVal.text = $"{ai.Opinion:+0;-0;0}";
            _gaugeTrustVal.color = oc;
            bool hasChance = _reactionBar != null && _reactionBar.parent.gameObject.activeSelf && !_deal.IsEmpty;
            float chance = hasChance ? Mathf.Clamp01(_reactionBar.anchorMax.x) : 0f;
            _gaugeChance.fillAmount = chance;
            _gaugeChance.color = Color.Lerp(Red, Green, chance);
            _gaugeChanceVal.text = hasChance ? $"{chance * 100f:0}%" : "—";
            _gaugeChanceVal.color = hasChance ? Color.white : Muted;
        }

        private static Sprite s_ring;

        private static Sprite RingSprite()
        {
            if (s_ring != null) return s_ring;
            s_ring = MakeSprite(256, 256, (u, v) =>
            {
                float d = Mathf.Sqrt(u * u + v * v);
                return Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.07f * 1.2f);
            }, "DiploGauge");
            return s_ring;
        }

        private void BuildBottom(RectTransform rt)
        {
            // Слева — что может дать ИИ
            var left = GlassPanel(rt, "AIItems", new Color(1f, 1f, 1f, 0.10f));
            Place(left.rectTransform, -(CenterW * 0.5f + 20f + SidePanelW * 0.5f), SidePanelW);
            _aiListTitle = PanelHeader(left.rectTransform, "", TextAnchor.MiddleLeft);
            var lh = LGBuild.Rect(left.rectTransform, "List");
            lh.Stretch(6, 6, 6, 50);
            _aiList = LGBuild.ScrollList(lh, 4f, 4);

            // Справа — что можете дать вы
            var right = GlassPanel(rt, "PlayerItems", new Color(1f, 1f, 1f, 0.10f));
            Place(right.rectTransform, CenterW * 0.5f + 20f + SidePanelW * 0.5f, SidePanelW);
            var rtt = PanelHeader(right.rectTransform, "ВЫ", TextAnchor.MiddleRight);
            rtt.color = Gold;
            var rh = LGBuild.Rect(right.rectTransform, "List");
            rh.Stretch(6, 6, 6, 50);
            _playerList = LGBuild.ScrollList(rh, 4f, 4);

            // Центр — стол переговоров
            var center = GlassPanel(rt, "Table", new Color(Cyan.r, Cyan.g, Cyan.b, 0.25f));
            _centerPanel = center.rectTransform;
            Place(_centerPanel, 0f, CenterW);
            _tableTitle = LGBuild.Label(_centerPanel, "СТОЛ ПЕРЕГОВОРОВ", 10, Primary, TextAnchor.UpperCenter, bold: true);
            _tableTitle.rectTransform.TopBand(104, 16, 14, 14);

            var cols = LGBuild.Rect(_centerPanel, "Cols");
            cols.Stretch(10, 66, 10, 122);
            var cl = LGBuild.Rect(cols, "ColAI");
            cl.Column(0f, 0.5f, 0, 6);
            var cr = LGBuild.Rect(cols, "ColPlayer");
            cr.Column(0.5f, 1f, 6, 0);
            _colAI = ColumnHead(cl, "ОНИ ДАЮТ");
            ColumnHead(cr, "ВЫ ДАЁТЕ");
            var divider = LGBuild.Panel(cols, "Divider", new Color(1f, 1f, 1f, 0.08f));
            divider.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            divider.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            divider.rectTransform.sizeDelta = new Vector2(1f, 0f);
            LG.Ignore(divider.gameObject);
            var clh = LGBuild.Rect(cl, "List");
            clh.Stretch(0, 0, 0, 24);
            _tableAI = LGBuild.ScrollList(clh, 4f, 2);
            var crh = LGBuild.Rect(cr, "List");
            crh.Stretch(0, 0, 0, 24);
            _tablePlayer = LGBuild.ScrollList(crh, 4f, 2);

            // Реакция собеседника и стоимость переговоров
            var foot = LGBuild.Rect(_centerPanel, "Reaction");
            foot.anchorMin = new Vector2(0, 0);
            foot.anchorMax = new Vector2(1, 0);
            foot.pivot = new Vector2(0.5f, 0);
            foot.offsetMin = new Vector2(16, 10);
            foot.offsetMax = new Vector2(-16, 58);
            _reaction = LGBuild.Label(foot, "", 13, Primary, TextAnchor.UpperLeft, bold: true);
            _reaction.rectTransform.TopBand(0, 20);
            _fee = LGBuild.Label(foot, "", 11, Muted, TextAnchor.UpperRight);
            _fee.rectTransform.TopBand(2, 18);
            var barHost = LGBuild.Rect(foot, "Bar");
            barHost.TopBand(28, 8);
            _reactionBar = LGBuild.Bar(barHost, Green, 0f, 6f);

            // Кнопки
            (_btnBalance, _imgBalance, _lblBalance) = ActionButton(rt, "Balance", new Vector2(0f, BottomY - 52f), new Vector2(CenterW, 40f), OnBalanceClicked);
            (_btnBack, _imgBack, _lblBack) = ActionButton(rt, "Back", new Vector2(-(CenterW * 0.25f + 4f), BottomY - 100f), new Vector2(CenterW * 0.5f - 8f, 40f), OnBackClicked);
            (_btnPropose, _imgPropose, _lblPropose) = ActionButton(rt, "Propose", new Vector2(CenterW * 0.25f + 4f, BottomY - 100f), new Vector2(CenterW * 0.5f - 8f, 40f), OnProposeClicked);
        }

        private static void Place(RectTransform r, float x, float w)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.sizeDelta = new Vector2(w, BottomH);
            r.anchoredPosition = new Vector2(x, BottomY);
        }

        private static Text PanelHeader(RectTransform panel, string text, TextAnchor anchor)
        {
            var head = LGBuild.Rect(panel, "Header");
            head.TopBand(0, 44, 18, 18);
            var t = LGBuild.Label(head, text, 20, Primary, anchor, bold: true);
            var line = LGBuild.Panel(panel, "Line", new Color(1f, 1f, 1f, 0.10f));
            line.rectTransform.TopBand(44, 1, 14, 14);
            LG.Ignore(line.gameObject);
            return t;
        }

        private static Text ColumnHead(RectTransform col, string text)
        {
            var t = LGBuild.Label(col, text, 11, Muted, TextAnchor.UpperCenter, bold: true);
            t.rectTransform.TopBand(0, 18);
            return t;
        }

        private static (Button, Image, Text) ActionButton(RectTransform parent, string name, Vector2 pos, Vector2 size, System.Action onClick)
        {
            var b = LGBuild.Button(parent, name, new Color(0.08f, 0.30f, 0.32f, 0.96f), new Color(1f, 1f, 1f, 0.18f), onClick, null, " ", 14, 4f);
            var r = (RectTransform)b.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.sizeDelta = size;
            r.anchoredPosition = pos;
            var fx = b.GetComponent<LiquidGlassEffect>();
            if (fx != null) { fx.SpecularMultiplier = 0.3f; fx.GlowMultiplier = 0.3f; fx.FillMultiplier = 2.4f; }
            return (b, b.GetComponent<Image>(), b.GetComponentInChildren<Text>());
        }

        // ================================================================ Обновление

        private void BuildPortrait(AIEmpireManager ai)
        {
            var leader = LeaderPortraits.ForFaction(ai.Faction);
            string key = leader?.Key ?? "";
            if (key == _portraitKey) return;
            _portraitKey = key;
            LGBuild.Clear(_portraitHost);
            // Здесь правитель не моргает и стоит без фона своей картинки — в сцене своей империи
            var v = LeaderPortraitView.Create(_portraitHost, leader, 1f, new Vector4(0.10f, 0.10f, 0.32f, 0f), false, false, true);
            if (v == null) LGIcons.Create(_portraitHost, LGIcon.Leader, 260, new Color(1f, 1f, 1f, 0.15f));
            else v.SetHologram(0.45f, Color.Lerp(ai.AIEmpireColor, Color.white, 0.35f));
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
            var v = LeaderPortraitView.Create(_playerHost, leader, 1f, new Vector4(0.10f, 0.10f, 0.32f, 0f), false, false, true);
            if (v == null) LGIcons.Create(_playerHost, LGIcon.Leader, 220, new Color(1f, 1f, 1f, 0.12f));
            else v.SetHologram(0.6f, Color.Lerp(FleetIndicator.OwnColor, Color.white, 0.3f));
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

        private static Sprite s_pad, s_cone, s_dash;

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

        private void RefreshAll()
        {
            var ai = Current;
            if (ai == null) return;
            _signature = Signature(ai);
            RefreshInfo();
            RefreshTabs();
            RefreshSpeechHeader(ai);
            RefreshItemLists();
            RefreshTable();
            RefreshQuickActions();
            RefreshStatus(ai);
        }

        // ---------- Левый верх: отношение и соглашения ----------

        private void RefreshInfo()
        {
            var ai = Current;
            LGBuild.Clear(_infoLeft);
            LGBuild.Clear(_infoRight);
            if (ai == null) return;

            float y = 0f;
            Color oc = OpinionColor(ai.Opinion);
            var cap = LGBuild.Label(_infoLeft, "ОТНОШЕНИЕ", 15, Primary, TextAnchor.UpperLeft, bold: true);
            cap.rectTransform.TopBand(y, 20, 0, 0);
            var val = LGBuild.Label(_infoLeft, $"{AIEmpireManager.OpinionLabel(ai.Opinion).ToUpper()}  {ai.Opinion:+0;-0;0}", 12, oc, TextAnchor.UpperRight, bold: true);
            val.rectTransform.TopBand(y + 2, 18, 0, 110);
            var barHost = LGBuild.Rect(_infoLeft, "Bar");
            barHost.TopBand(y + 26, 8, 0, 110);
            LGBuild.Bar(barHost, oc, Mathf.InverseLerp(AIEmpireManager.OpinionMin, AIEmpireManager.OpinionMax, ai.Opinion), 6f);
            var hit = LGBuild.Panel(_infoLeft, "Hit", new Color(0, 0, 0, 0.01f), raycast: true);
            hit.rectTransform.TopBand(y, 40, 0, 110);
            LG.Ignore(hit.gameObject);
            TooltipHelper.Attach(hit.gameObject, OpinionTooltip(ai));
            y += 50f;

            // Состояние
            (string state, Color sc, LGIcon si) = StateOf(ai);
            var chip = LGBuild.Panel(_infoLeft, "State", new Color(sc.r * 0.15f, sc.g * 0.15f, sc.b * 0.15f, 0.95f));
            chip.rectTransform.TopBand(y, 30, 0, 110);
            var cfx = LG.Platter(chip.gameObject, 4f);
            cfx.SetRim(new Color(sc.r, sc.g, sc.b, 0.5f));
            cfx.FillMultiplier = 2.6f;
            var sic = LGIcons.Create(chip.transform, si, 15, sc);
            sic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(15, 15));
            var st = LGBuild.Label(chip.transform, state, 12, sc, TextAnchor.MiddleLeft, bold: true);
            st.rectTransform.Stretch(32, 0, 8, 0);
            y += 40f;

            // Действующие соглашения
            var ah = LGBuild.Label(_infoLeft, "СОГЛАШЕНИЯ", 11, Muted, TextAnchor.UpperLeft, bold: true);
            ah.rectTransform.TopBand(y, 16, 0, 0);
            y += 20f;
            if (ai.Agreements.Count == 0 && !ai.HasPact)
            {
                var none = LGBuild.Label(_infoLeft, "Нет действующих договоров", 12, new Color(Muted.r, Muted.g, Muted.b, 0.7f), TextAnchor.UpperLeft);
                none.rectTransform.TopBand(y, 18, 0, 0);
                y += 22f;
            }
            if (ai.HasPact) { InfoLine(ref y, LGIcon.Handshake, Cyan, "Пакт о ненападении  ·  бессрочно"); }
            foreach (var a in ai.Agreements)
            {
                string text = DealCatalog.IsPayment(a.Kind)
                    ? $"{(a.PlayerPays ? "Вы платите" : "Вам платят")} {a.Amount:0} {DealCatalog.Name(DealCatalog.ResourceBase(a.Kind)).ToLower()}/мес  ·  {DealCatalog.Months(a.MonthsLeft)}"
                    : $"{DealCatalog.Name(a.Kind)}  ·  {DealCatalog.Months(a.MonthsLeft)}";
                InfoLine(ref y, DealCatalog.Icon(a.Kind), a.PlayerPays ? Red : DealCatalog.Tint(a.Kind), text);
            }

            if (AICoalition.IsActive)
            {
                string role = AICoalition.IsMember(ai.OwnerId) ? "участник" : AICoalition.IsTarget(ai.OwnerId) ? "цель" : "вне коалиции";
                string you = AICoalition.PlayerMember ? "  ·  вы в союзе" : "";
                InfoLine(ref y, LGIcon.Swords, CoalitionTone, $"Коалиция против {AICoalition.TargetPhrase}: {role}{you}");
            }
            if (!string.IsNullOrEmpty(ai.TacticsNote))
                InfoLine(ref y, LGIcon.Target, Muted, ai.TacticsNote);

            // Соседи-ИИ
            y += 6f;
            foreach (var other in AIEmpireManager.All)
            {
                if (other == ai || other.IsEliminated || ai.IsEliminated) continue;
                if (!Contacts.PlayerMet(other.OwnerId)) continue;   // о незнакомых нам империях не рассказываем
                if (!Contacts.Met(ai.OwnerId, other.OwnerId))
                {
                    InfoLine(ref y, LGIcon.Diplomacy, Muted, $"С <color={LGBuild.Hex(other.MapColor)}>{other.AIName}</color>: не знакомы");
                    continue;
                }
                bool war = AIRelations.AtWar(ai.OwnerId, other.OwnerId);
                int truce = AIRelations.TruceDays(ai.OwnerId, other.OwnerId);
                string s = war ? "воюют" : truce > 0 ? "перемирие" : "мир";
                InfoLine(ref y, war ? LGIcon.Swords : LGIcon.Diplomacy, war ? Red : Muted,
                    $"С <color={LGBuild.Hex(other.MapColor)}>{other.AIName}</color>: {s}");
            }

            // Правый верх: империя
            var leader = LeaderPortraits.ForFaction(ai.Faction);
            float ry = 0f;
            var nm = LGBuild.Label(_infoRight, ai.AIName.ToUpper(), 26, Color.Lerp(ai.AIEmpireColor, Color.white, 0.25f), TextAnchor.UpperRight, bold: true);
            nm.rectTransform.TopBand(ry, 34);
            ry += 36f;
            var ti = LGBuild.Label(_infoRight, ai.AITitle, 13, Muted, TextAnchor.UpperRight);
            ti.rectTransform.TopBand(ry, 18);
            ry += 26f;
            if (leader != null)
            {
                var ln = LGBuild.Label(_infoRight, $"{leader.Title}  <b>{leader.Name}</b>", 13, Primary, TextAnchor.UpperRight);
                ln.rectTransform.TopBand(ry, 18);
                ry += 22f;
            }
            var pers = LGBuild.Label(_infoRight, $"Характер: {ai.Profile.Name.ToLower()}", 12, Gold, TextAnchor.UpperRight, bold: true);
            pers.rectTransform.TopBand(ry, 18);
            TooltipHelper.Attach(pers.gameObject, ai.Profile.Summary);
            pers.raycastTarget = true;
            ry += 24f;
            var cmp = LGBuild.Label(_infoRight,
                $"Флот {EmpireStats.MilitaryPower(ai.OwnerId):0}  ·  систем {EmpireStats.SystemCount(ai.OwnerId)}  ·  технологий {ai.ResearchedCount}",
                12, Muted, TextAnchor.UpperRight);
            cmp.rectTransform.TopBand(ry, 18);
            ry += 30f;

            // Война / пакт — с подтверждением повторным щелчком
            if (!ai.IsEliminated)
            {
                if (ai.HasPact)
                    DangerButton(ref ry, "cancelpact", "РАСТОРГНУТЬ ПАКТ", "Отношение ухудшится; после этого войну можно объявить без вероломства",
                        () => { ai.PlayerCancelPact(); });
                if (!ai.AtWar)
                {
                    string blocker = ai.WarBlocker;
                    DangerButton(ref ry, "war", ai.HasPact ? "ОБЪЯВИТЬ ВОЙНУ (НАРУШИТЬ ПАКТ)" : "ОБЪЯВИТЬ ВОЙНУ",
                        blocker ?? (ai.HasPact ? "Нарушение пакта — тяжёлое вероломство: −40 к отношению надолго" : "Объявление войны рвёт все соглашения и ухудшает отношение"),
                        blocker != null ? null : () =>
                        {
                            if (ai.PlayerDeclareWar())
                            {
                                SFXManager.Play(Sfx.WarDeclared);
                                _deal = new Deal();
                                Say(DiplomacyLines.Get(leader, DiplomacyLines.Kind.AtWar));
                            }
                        });
                }
                BuildCoalitionActions(ref ry, ai);
            }
        }

        private static readonly Color CoalitionTone = new Color(1f, 0.62f, 0.36f);

        /// <summary>Коалиция: вступить в действующую, собрать свою против этой империи или выйти.</summary>
        private void BuildCoalitionActions(ref float ry, AIEmpireManager ai)
        {
            if (GameSession.Settings.Difficulty < 1) return;
            if (AICoalition.PlayerMember)
            {
                bool owner = AICoalition.PlayerOrganized;
                DangerButton(ref ry, "coalleave", owner ? "РАСПУСТИТЬ КОАЛИЦИЮ" : "ПОКИНУТЬ КОАЛИЦИЮ",
                    "Союзники запомнят, что вы бросили общее дело: отношение ухудшится",
                    () => { SFXManager.Play(Sfx.UiTab); AICoalition.PlayerLeave(); }, LGIcon.Close, CoalitionTone);
            }
            else if (AICoalition.IsActive && AICoalition.TargetOwner != 0)
            {
                string b = AICoalition.JoinBlocker();
                DangerButton(ref ry, "coaljoin", "ВСТУПИТЬ В КОАЛИЦИЮ",
                    b ?? $"Присоединитесь к союзу против {AICoalition.TargetPhrase}: союзники лучше к вам относятся, но отношения с целью ухудшатся. Стоит {AICoalition.JoinCost:0} влияния",
                    b != null ? null : () => { SFXManager.Play(Sfx.UiConfirm); AICoalition.PlayerJoin(); }, LGIcon.Handshake, CoalitionTone);
            }
            else if (!AICoalition.IsActive && AICoalition.TargetOwner < 0)
            {
                string b = AICoalition.OrganizeBlocker(ai);
                DangerButton(ref ry, "coalorg", "СОЗДАТЬ КОАЛИЦИЮ ПРОТИВ НИХ",
                    b ?? $"Соберёт соперников, которым эта империя неприятна. Стоит {AICoalition.OrganizeCost:0} влияния; союзники нападут сами, когда накопят силы",
                    b != null ? null : () => { SFXManager.Play(Sfx.UiConfirm); AICoalition.PlayerOrganize(ai); }, LGIcon.Swords, CoalitionTone);
            }
        }

        private void InfoLine(ref float y, LGIcon icon, Color col, string text)
        {
            var row = LGBuild.Rect(_infoLeft, "Line");
            row.TopBand(y, 20, 0, 0);
            var ic = LGIcons.Create(row, icon, 14, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(14, 14));
            var t = LGBuild.Label(row, text, 12, Primary, TextAnchor.MiddleLeft);
            t.rectTransform.Stretch(22, 0, 0, 0);
            y += 22f;
        }

        private void DangerButton(ref float y, string id, string label, string tip, System.Action act,
                                  LGIcon icon = LGIcon.Swords, Color? tone = null)
        {
            bool confirming = _confirmAction == id && Time.unscaledTime <= _confirmUntil;
            Color tc = tone ?? Red;
            Color idle = tone.HasValue ? new Color(tc.r * 0.20f, tc.g * 0.20f, tc.b * 0.20f, 0.9f) : new Color(0.22f, 0.06f, 0.08f, 0.9f);
            Color sure = tone.HasValue ? new Color(tc.r * 0.45f, tc.g * 0.45f, tc.b * 0.45f, 0.95f) : UIManager.DS.BtnDanger;
            var b = LGBuild.Button(_infoRight, "Danger_" + id,
                act == null ? UIManager.DS.BtnDisabled : confirming ? sure : idle,
                new Color(tc.r, tc.g, tc.b, act == null ? 0.15f : 0.45f), () =>
                {
                    if (act == null) return;
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
                        RefreshInfo();
                    }
                }, icon, confirming ? "ТОЧНО? НАЖМИТЕ ЕЩЁ РАЗ" : label, 11, 4f);
            var r = (RectTransform)b.transform;
            r.anchorMin = new Vector2(1, 1);
            r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(1, 1);
            r.sizeDelta = new Vector2(300f, 32f);
            r.anchoredPosition = new Vector2(0f, -y);
            b.interactable = act != null;
            TooltipHelper.Attach(b.gameObject, $"<b>{label}</b>\n{tip}");
            y += 38f;
        }

        private void RefreshTabs()
        {
            LGBuild.Clear(_tabs);
            var all = new List<AIEmpireManager>();
            foreach (var a in AIEmpireManager.All) if (Contacts.PlayerMet(a.OwnerId)) all.Add(a);
            if (all.Count <= 1) return;
            float x = 0f;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var ai = all[i];
                bool on = ai.OwnerId == _owner;
                Color mc = ai.MapColor;
                var b = LGBuild.Panel(_tabs, "Tab_" + ai.OwnerId, on ? new Color(mc.r * 0.25f, mc.g * 0.25f, mc.b * 0.25f, 1f) : PanelBg, raycast: true);
                b.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-x, 0), new Vector2(64, 64));
                var fx = LG.Platter(b.gameObject, 6f);
                fx.SetRim(new Color(mc.r, mc.g, mc.b, on ? 0.9f : 0.3f));
                fx.FillMultiplier = 2.6f;
                var mask = LG.RoundedMask(b.transform, 6f, 2f);
                var v = LeaderPortraitView.Create(mask, LeaderPortraits.ForFaction(ai.Faction), 2.2f, Vector4.zero, false, false, false);
                if (v == null) LGIcons.Create(b.transform, LGIcon.Leader, 30, mc);
                else if (ai.IsEliminated) v.GetComponent<RawImage>().color = new Color(0.4f, 0.4f, 0.4f, 1f);
                var btn = b.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                int owner = ai.OwnerId;
                btn.onClick.AddListener(() => { if (owner != _owner) { SFXManager.Play(Sfx.UiTab); SwitchTo(owner); } });
                string st = ai.IsEliminated ? "повержена" : ai.AtWar ? "война" : ai.HasPact ? "пакт" : "мир";
                TooltipHelper.Attach(b.gameObject, $"<b>{ai.AIName}</b>\n{st} · отношение {ai.Opinion:+0;-0;0}" +
                    (ai.PendingOffer != AIEmpireManager.OfferKind.None ? "\n<color=#F2C747>Ждёт ответа на предложение</color>" : ""));
                if (ai.PendingOffer != AIEmpireManager.OfferKind.None)
                {
                    var dot = LGBuild.Panel(b.transform, "Dot", Gold);
                    dot.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-4, -4), new Vector2(10, 10));
                    LG.Dot(dot.gameObject);
                }
                x += 72f;
            }
        }

        private void RefreshSpeechHeader(AIEmpireManager ai)
        {
            var leader = LeaderPortraits.ForFaction(ai.Faction);
            _speechName.text = $"{(leader != null ? leader.Name : ai.AIName)} говорит:";
            (string state, Color sc, _) = StateOf(ai);
            _speechState.text = state.Split(' ')[0];
            _speechState.color = sc;
            _speechStateBg.color = new Color(sc.r * 0.15f, sc.g * 0.15f, sc.b * 0.15f, 1f);
        }

        private static (string, Color, LGIcon) StateOf(AIEmpireManager ai)
        {
            if (ai.IsEliminated) return ("ПОВЕРЖЕНА", Muted, LGIcon.Trophy);
            if (ai.AtWar) return ($"ВОЙНА · {ai.WarMonths} мес. · усталость {ai.WarWeariness:0}%", Red, LGIcon.Swords);
            if (ai.HasPact) return ("ПАКТ О НЕНАПАДЕНИИ", Cyan, LGIcon.Handshake);
            if (ai.TruceDays > 0) return ($"ПЕРЕМИРИЕ · {Mathf.CeilToInt(ai.TruceDays / 30f)} мес.", Green, LGIcon.Peace);
            return ("МИР", Green, LGIcon.Peace);
        }

        private static Color OpinionColor(float o) =>
            o <= -60f ? Red : o <= -20f ? new Color(1f, 0.55f, 0.35f) : o < 10f ? Gold : o < 35f ? new Color(0.6f, 0.9f, 0.5f) : Green;

        private static string OpinionTooltip(AIEmpireManager ai)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>Отношение {ai.Opinion:+0;-0;0}</b>  ·  {AIEmpireManager.OpinionLabel(ai.Opinion)}\n");
            foreach (var m in ai.OpinionBreakdown)
            {
                string c = m.Value >= 0 ? "#6FE09A" : "#FF8080";
                sb.Append($"<color={c}>{m.Value:+0;-0;0}</color>  {m.Label}\n");
            }
            sb.Append("<color=#8AA2A8>Чем лучше отношение, тем охотнее они идут на сделки</color>");
            return sb.ToString();
        }

        // ---------- Списки предметов торга ----------

        private void RefreshItemLists()
        {
            var ai = Current;
            LGBuild.Clear(_aiList);
            LGBuild.Clear(_playerList);
            if (ai == null) return;
            _aiListTitle.text = ai.AIName.ToUpper();
            _aiListTitle.color = Color.Lerp(ai.AIEmpireColor, Color.white, 0.25f);
            _colAI.text = $"{ai.AIName.ToUpper()} ДАЁТ";

            var eco = EconomyManager.Instance;
            bool frozen = _viewingOffer || ai.IsEliminated;

            foreach (var k in DealCatalog.Resources)
            {
                string r = DealCatalog.ResourceKey(k);
                float aiHas = ai.Stock(r);
                float pHas = PlayerStock(r);
                ItemRow(_aiList, k, true, $"{aiHas:0}", !frozen && aiHas >= DealCatalog.Step(k),
                    aiHas < DealCatalog.Step(k) ? "У них почти ничего нет" : null);
                ItemRow(_playerList, k, false, $"{pHas:0}", !frozen && pHas >= DealCatalog.Step(k),
                    pHas < DealCatalog.Step(k) ? "У вас почти ничего нет" : null);
            }
            foreach (var k in DealCatalog.Payments)
            {
                ItemRow(_aiList, k, true, "в месяц", !frozen && !ai.AtWar, ai.AtWar ? "Во время войны — только мир" : null);
                ItemRow(_playerList, k, false, "в месяц", !frozen && !ai.AtWar, ai.AtWar ? "Во время войны — только мир" : null);
            }
            foreach (var k in DealCatalog.TreatyKinds)
            {
                if (k == DealItemKind.Peace && !ai.AtWar) continue;
                if (k == DealItemKind.Pact && ai.HasPact) continue;
                ai.TreatyValue(k, out string block);
                string status = ai.HasAgreement(k) ? "действует" : "договор";
                ItemRow(_aiList, k, true, status, !frozen && block == null, block);
                ItemRow(_playerList, k, false, status, !frozen && block == null, block);
            }
        }

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

        private void ItemRow(RectTransform list, DealItemKind k, bool fromAI, string right, bool enabled, string blocker)
        {
            bool onTable = DealCatalog.IsTreaty(k) ? _deal.HasTreaty(k) : _deal.Find(fromAI, k) != null;
            Color tint = DealCatalog.Tint(k);
            var row = LGBuild.Panel(list, "Item_" + k, onTable ? new Color(0.06f, 0.14f, 0.16f, 0.95f) : RowBg, raycast: true);
            LGBuild.Height(row.gameObject, 30f);
            LG.Ignore(row.gameObject, includeChildren: false);
            float a = enabled ? 1f : 0.4f;
            var ic = LGIcons.Create(row.transform, DealCatalog.Icon(k), 16, new Color(tint.r, tint.g, tint.b, a));
            ic.rectTransform.At(new Vector2(fromAI ? 0 : 1, 0.5f), new Vector2(fromAI ? 0 : 1, 0.5f), new Vector2(fromAI ? 10 : -10, 0), new Vector2(16, 16));
            var name = LGBuild.Label(row.transform, DealCatalog.Name(k), 14, new Color(Primary.r, Primary.g, Primary.b, a),
                fromAI ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
            name.rectTransform.Stretch(fromAI ? 34 : 90, 0, fromAI ? 90 : 34, 0);
            var r = LGBuild.Label(row.transform, right, 11, new Color(Muted.r, Muted.g, Muted.b, a),
                fromAI ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            r.rectTransform.Stretch(fromAI ? 0 : 10, 0, fromAI ? 10 : 0, 0);
            if (onTable)
            {
                var mark = LGBuild.Panel(row.transform, "OnTable", Cyan);
                mark.rectTransform.anchorMin = new Vector2(fromAI ? 0 : 1, 0);
                mark.rectTransform.anchorMax = new Vector2(fromAI ? 0 : 1, 1);
                mark.rectTransform.pivot = new Vector2(fromAI ? 0 : 1, 0.5f);
                mark.rectTransform.sizeDelta = new Vector2(3f, -8f);
                LG.Ignore(mark.gameObject);
            }

            var btn = row.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.interactable = enabled;
            btn.onClick.AddListener(() => AddToTable(k, fromAI));
            string tip = $"<b>{DealCatalog.Name(k)}</b>\n{DealCatalog.Hint(k)}";
            if (blocker != null) tip += $"\n<color=#FF8080>{blocker}</color>";
            else if (!enabled && _viewingOffer) tip += "\n<color=#8AA2A8>Сначала ответьте на их предложение или измените его</color>";
            TooltipHelper.Attach(row.gameObject, tip);
        }

        private void AddToTable(DealItemKind k, bool fromAI)
        {
            if (_viewingOffer) return;
            if (DealCatalog.IsTreaty(k))
            {
                if (_deal.HasTreaty(k)) _deal.Treaties.RemoveAll(t => t.Kind == k);
                else _deal.Treaties.Add(new DealItem(k, 0f));
            }
            else
            {
                var have = _deal.Find(fromAI, k);
                if (have != null) Adjust(have, fromAI, +1);
                else _deal.Side(fromAI).Add(new DealItem(k, DealCatalog.Step(k)));
            }
            SFXManager.Play(Sfx.UiClick);
            RefreshItemLists();
            RefreshTable();
            RefreshQuickActions();
            RefreshStatus(Current);
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

        // ---------- Стол переговоров ----------

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
            if (d.FromAI.Count == 0 && d.Treaties.Count == 0) EmptyNote(_tableAI, ro ? "ничего" : "щёлкните по их списку слева");
            if (d.FromPlayer.Count == 0 && d.Treaties.Count == 0) EmptyNote(_tablePlayer, ro ? "ничего" : "щёлкните по своему списку справа");

            if (_viewingOffer)
            {
                var kind = ai.PendingOffer;
                _tableTitle.text = kind == AIEmpireManager.OfferKind.Demand ? "ТРЕБОВАНИЕ" : "ИХ ПРЕДЛОЖЕНИЕ";
                _tableTitle.color = kind == AIEmpireManager.OfferKind.Demand ? Red : Gold;
                _reaction.text = $"<color={LGBuild.Hex(Gold)}>Ответ ждут ещё {ai.PendingOfferDays} дн.</color>  ·  {ai.PendingOfferReason}";
                _fee.text = kind == AIEmpireManager.OfferKind.Demand ? "<color=#FF8080>Отказ ухудшит отношение</color>" : "";
                _reactionBar.parent.gameObject.SetActive(false);

                SetButton(_btnBalance, _imgBalance, _lblBalance, "ИЗМЕНИТЬ УСЛОВИЯ", new Color(0.08f, 0.22f, 0.30f, 0.96f), kind == AIEmpireManager.OfferKind.Trade);
                SetButton(_btnBack, _imgBack, _lblBack, "ОТКЛОНИТЬ", new Color(0.36f, 0.10f, 0.12f, 0.96f), true);
                SetButton(_btnPropose, _imgPropose, _lblPropose, "ПРИНЯТЬ", new Color(0.10f, 0.38f, 0.24f, 0.96f), true);
                return;
            }

            _tableTitle.text = "СТОЛ ПЕРЕГОВОРОВ";
            _tableTitle.color = Primary;
            _reactionBar.parent.gameObject.SetActive(true);
            float cost = AIEmpireManager.ProposalCost(d);
            _fee.text = cost > 0f ? $"Переговоры: −{cost:0} влияния" : "";

            if (d.IsEmpty)
            {
                _reaction.text = "<color=#8AA2A8>Положите на стол то, что хотите получить или отдать</color>";
                LGBuild.SetBar(_reactionBar, 0f);
            }
            else
            {
                var v = ai.Evaluate(d);
                string verdict;
                Color vc;
                if (v.Blocker != null) { verdict = v.Blocker; vc = Red; }
                else if (d.IsGift) { verdict = "Примут с благодарностью"; vc = Green; }
                else if (v.Accept) { verdict = v.Balance - v.Required > 25f ? "Согласятся с радостью" : "Согласятся"; vc = Green; }
                else if (v.Willingness > 0.35f) { verdict = "Колеблются — добавьте что-нибудь"; vc = Gold; }
                else { verdict = "Откажут"; vc = Red; }
                _reaction.text = $"Реакция: <color={LGBuild.Hex(vc)}>{verdict}</color>";
                LGBuild.SetBar(_reactionBar, v.Blocker != null ? 0.05f : v.Willingness, vc);
            }

            bool alive = !ai.IsEliminated;
            SetButton(_btnBalance, _imgBalance, _lblBalance, "ЧТО ВАС УСТРОИТ?", new Color(0.08f, 0.30f, 0.32f, 0.96f), alive);
            SetButton(_btnBack, _imgBack, _lblBack, d.IsEmpty ? "НАЗАД" : "ОЧИСТИТЬ", new Color(0.08f, 0.22f, 0.26f, 0.96f), true);
            SetButton(_btnPropose, _imgPropose, _lblPropose, d.IsGift ? "ПОДАРИТЬ" : "ПРЕДЛОЖИТЬ", new Color(0.08f, 0.30f, 0.32f, 0.96f), alive && !d.IsEmpty);
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
            var t = LGBuild.Label(list, text, 12, new Color(Muted.r, Muted.g, Muted.b, 0.55f), TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Italic;
            LGBuild.Height(t.gameObject, 30f);
        }

        private void TableRow(RectTransform list, DealItem item, bool fromAI, bool readOnly, bool treaty)
        {
            Color tint = DealCatalog.Tint(item.Kind);
            var row = LGBuild.Panel(list, "Row", RowBg);
            LGBuild.Height(row.gameObject, 32f);
            var fx = LG.Platter(row.gameObject, 4f);
            fx.SetRim(new Color(tint.r, tint.g, tint.b, 0.25f));
            fx.FillMultiplier = 2.6f;
            fx.SpecularMultiplier = 0.2f;

            var ic = LGIcons.Create(row.transform, treaty ? LGIcon.Handshake : DealCatalog.Icon(item.Kind), 15, tint);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(15, 15));
            string text = treaty ? DealCatalog.Name(item.Kind) : DealCatalog.Describe(item);
            var t = LGBuild.Label(row.transform, text, 13, Primary, TextAnchor.MiddleLeft);
            t.rectTransform.Stretch(30, 0, readOnly ? 8 : (treaty ? 34 : 86), 0);
            TooltipHelper.Attach(row.gameObject, $"<b>{DealCatalog.Name(item.Kind)}</b>\n{DealCatalog.Hint(item.Kind)}" + (treaty ? "\n<color=#8AA2A8>Договор взаимный — действует для обеих сторон</color>" : ""));
            row.raycastTarget = true;
            if (readOnly) return;

            float x = -6f;
            SmallButton(row.rectTransform, LGIcon.Close, ref x, () =>
            {
                if (treaty) _deal.Treaties.Remove(item); else _deal.Side(fromAI).Remove(item);
                SFXManager.Play(Sfx.UiBack);
                RefreshItemLists(); RefreshTable();
            });
            if (treaty) return;
            SmallText(row.rectTransform, "+", ref x, () => { Adjust(item, fromAI, +1); RefreshTable(); });
            SmallText(row.rectTransform, "−", ref x, () => { Adjust(item, fromAI, -1); RefreshItemLists(); RefreshTable(); });
        }

        private static void SmallButton(RectTransform row, LGIcon icon, ref float x, System.Action act)
        {
            var b = LGBuild.Button(row, "Btn", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.12f), act, icon, null, 6, 3f);
            ((RectTransform)b.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(x, 0), new Vector2(22, 22));
            x -= 26f;
        }

        private static void SmallText(RectTransform row, string label, ref float x, System.Action act)
        {
            var b = LGBuild.Button(row, "Btn", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.12f), act, null, label, 14, 3f);
            ((RectTransform)b.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(x, 0), new Vector2(22, 22));
            x -= 26f;
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
                RefreshItemLists(); RefreshTable();
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
            RefreshItemLists(); RefreshTable();
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
                RefreshItemLists(); RefreshTable();
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
                        : kind == AIEmpireManager.OfferKind.Pact ? Sfx.PactSigned : Sfx.UiConfirm);
                    Say(DiplomacyLines.Get(leader, DiplomacyLines.Kind.Accept));
                }
                else SFXManager.Play(Sfx.UiDenied);
                RefreshAll();
                return;
            }

            if (_deal.IsEmpty) return;
            bool gift = _deal.IsGift;
            bool peace = _deal.HasTreaty(DealItemKind.Peace), pact = _deal.HasTreaty(DealItemKind.Pact);
            if (ai.PlayerPropose(_deal, out var verdict))
            {
                SFXManager.Play(peace ? Sfx.PeaceSigned : pact ? Sfx.PactSigned : Sfx.UiConfirm);
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
