using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Экран карточного штурма — полноэкранный стол поверх галактики (время стратегии на паузе).
    /// Сверху — враг, снизу — вы, при любой роли. Три сектора-колонки: шапка с правилом сектора, ряд врага,
    /// полоса сил с контролем, ваш ряд. Внизу — рука веером, слева — очки приказов и колода, справа — «Закончить ход».
    /// Наведение на любую карту — крупный предпросмотр слева. Карты перетаскиваются в сектор, клик по своей
    /// выложенной — вернуть в руку. Ход подводится анимациями из журнала событий правил (BattleScreen.Play.cs).
    /// </summary>
    public sealed partial class BattleScreen : MonoBehaviour
    {
        public static BattleScreen Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;

        // Размеры (канвас 1920×1080)
        private const float ColW = 440f, ColGap = 12f, ColTop = -112f, ColH = 560f;
        private static readonly Vector2 BoardCard = new Vector2(112, 156);
        private static readonly Vector2 HandCard = new Vector2(168, 236);
        private static readonly Vector2 PreviewCard = new Vector2(270, 378);

        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _board, _fxLayer, _overlay, _handRoot, _previewRoot;
        private UIFx _fx;
        private RawImage _bg;

        private BattleSetup _setup;
        private BattleState _state;
        private Action<BattleState> _onDone;
        private int _P, _E;
        private int _evCursor;
        private bool _busy;
        private int _savedSpeed = 1;
        private float _animSpeed = 1f;

        // Виды
        private readonly Dictionary<int, CardView> _units = new Dictionary<int, CardView>();
        private readonly Dictionary<BCard, CardView> _pending = new Dictionary<BCard, CardView>();
        private readonly List<CardView> _hand = new List<CardView>();
        private readonly Dictionary<CardView, Vector2> _targets = new Dictionary<CardView, Vector2>();
        private readonly Dictionary<CardView, float> _targetRot = new Dictionary<CardView, float>();
        private readonly RectTransform[,] _rows = new RectTransform[3, 2];   // [сектор, 0 — враг сверху / 1 — вы снизу]
        private readonly SectorUI[] _sec = new SectorUI[3];
        private CardView _preview;
        private CardView _dragging;
        private Vector2 _dragOffset;

        // Панели
        private Text _turnText, _scoreText, _weatherText, _pointsText, _deckText, _enemyHandText, _logText, _endLabel, _hintText, _speedLabel;
        private Image _endButtonImg;
        private Button _endButton, _retreatButton, _autoButton, _mercButton;
        private readonly List<Image> _pointPips = new List<Image>();
        private RectTransform _enemyHandRow;
        private readonly List<string> _log = new List<string>();
        private bool _mercPicking;

        private sealed class SectorUI
        {
            public RectTransform Col, Header, PowerBar;
            public Image Plate, Wash, Dome, ControlFill, HeaderGlow;
            public Text Name, Rule, PowE, PowP, Control, Stamp;
            public float WashA;
            public int LastControl = -2;

            /// <summary>Подсветка сектора при перетаскивании карты: 1 — можно сыграть, −1 — нельзя, 0 — нет.</summary>
            public void HeaderGlowSet(int state)
            {
                if (state == 0) return;
                var c = state > 0 ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 0.4f, 0.35f);
                Wash.color = new Color(c.r, c.g, c.b, 0.55f);
                WashA = 0.55f;
            }
        }

        // ==================== СОЗДАНИЕ ====================

        public static void Ensure()
        {
            if (Instance != null) return;
            new GameObject("[UI] CardBattleScreen").AddComponent<BattleScreen>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            AssaultLink.ScreenOpener = (setup, done) => Open(setup, done);
            AssaultLink.DefensePrompt = ShowDefensePrompt;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            AssaultLink.ScreenOpener = null;
            AssaultLink.DefensePrompt = null;
        }

        private void BuildCanvas()
        {
            var go = new GameObject("CardBattleCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 3000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            _root = go;
            go.SetActive(false);
        }

        // ==================== ОТКРЫТИЕ ====================

        public void Open(BattleSetup setup, Action<BattleState> onDone)
        {
            if (_canvas == null) BuildCanvas();
            _setup = setup;
            _onDone = onDone;
            _P = setup.Defender.IsPlayer && !setup.Attacker.IsPlayer ? BattleState.Defender : BattleState.Attacker;
            _E = 1 - _P;
            _state = new BattleState(setup);
            _evCursor = 0;
            _busy = false;
            _mercPicking = false;
            _units.Clear(); _pending.Clear(); _hand.Clear(); _targets.Clear(); _targetRot.Clear(); _log.Clear(); _pointPips.Clear();
            foreach (Transform c in _canvas.transform) Destroy(c.gameObject);

            var tm = TimeManager.Instance;
            if (tm != null) { _savedSpeed = tm.CurrentSpeed; tm.SetSpeed(0); }
            _animSpeed = PlayerPrefs.GetFloat("cards.speed", 1f);

            BuildScene();
            _root.SetActive(true);
            SFXManager.Play(Sfx.BattleStart);

            _state.Begin();
            // Сооружения и скрытые свои ловушки — на столе с начала
            for (int s = 0; s < 3; s++)
                foreach (var u in _state.Sectors[s].Units)
                {
                    if (u.IsTrap && u.Side != _P) continue;   // вражеские мины не видны
                    var v = MakeUnitView(u, revealed: !u.IsTrap);
                    v.SetAlpha(1f);
                }
            LayoutAll(true);
            RefreshPanels();
            StartCoroutine(Intro());
        }

        private void Close()
        {
            StopAllCoroutines();
            _fx?.ClearAll();
            if (_root != null) _root.SetActive(false);
            var tm = TimeManager.Instance;
            if (tm != null) tm.SetSpeed(_savedSpeed);
            var done = _onDone;
            var st = _state;
            _onDone = null;
            done?.Invoke(st);
        }

        // ==================== СЦЕНА ====================

        private void BuildScene()
        {
            var croot = (RectTransform)_canvas.transform;

            // Фон: поверхность планеты, затемнение, виньетка
            var bgRt = LGBuild.Rect(croot, "Bg");
            bgRt.Stretch();
            var bgBlock = bgRt.gameObject.AddComponent<Image>();
            bgBlock.color = new Color(0.01f, 0.015f, 0.025f, 1f);
            bgBlock.raycastTarget = true;   // клики не проходят на карту галактики
            _bg = LGBuild.Rect(bgRt, "Terrain").gameObject.AddComponent<RawImage>();
            _bg.rectTransform.Stretch(-40, -40, -40, -40);
            _bg.texture = CardArt.Table(_setup.Terrain);
            _bg.raycastTarget = false;
            if (_bg.texture != null)
            {
                float ta = _bg.texture.width / (float)_bg.texture.height, sa = 16f / 9f;
                _bg.uvRect = ta > sa ? new Rect((1 - sa / ta) * 0.5f, 0, sa / ta, 1) : new Rect(0, (1 - ta / sa) * 0.5f, 1, ta / sa);
            }
            _bg.color = new Color(0.55f, 0.55f, 0.6f, 1f);
            var dark = LGBuild.Panel(bgRt, "Dark", new Color(0.01f, 0.02f, 0.035f, 0.45f));
            dark.rectTransform.Stretch();
            var vig = LGBuild.Panel(bgRt, "Vignette", new Color(0, 0, 0, 0.92f));
            vig.sprite = CardSprites.Vignette;
            vig.rectTransform.Stretch();
            var bottomShade = LGBuild.Panel(bgRt, "BottomShade", new Color(0, 0, 0, 0.85f));
            bottomShade.sprite = CardSprites.Fade;
            bottomShade.rectTransform.anchorMin = new Vector2(0, 0);
            bottomShade.rectTransform.anchorMax = new Vector2(1, 0.32f);
            bottomShade.rectTransform.offsetMin = bottomShade.rectTransform.offsetMax = Vector2.zero;
            bottomShade.rectTransform.localRotation = Quaternion.Euler(0, 0, 180f);

            _board = LGBuild.Rect(croot, "Board");
            _board.Stretch();

            BuildTopBar();
            for (int i = 0; i < 3; i++) BuildSector(i);
            BuildBottom();

            _handRoot = LGBuild.Rect(_board, "Hand");
            _handRoot.anchorMin = _handRoot.anchorMax = new Vector2(0.5f, 0f);
            _handRoot.sizeDelta = new Vector2(1100, 260);
            _handRoot.anchoredPosition = new Vector2(0, 150);

            _previewRoot = LGBuild.Rect(croot, "Preview");
            _previewRoot.anchorMin = _previewRoot.anchorMax = new Vector2(0f, 0.5f);
            _previewRoot.sizeDelta = PreviewCard;
            _previewRoot.anchoredPosition = new Vector2(PreviewCard.x * 0.5f + 12f, 60f);

            _fxLayer = LGBuild.Rect(croot, "Fx");
            _fxLayer.Stretch();
            _fx = _fxLayer.gameObject.AddComponent<UIFx>();
            _fx.Init(_fxLayer, _board);
            _fx.Speed = _animSpeed;

            _overlay = LGBuild.Rect(croot, "Overlay");
            _overlay.Stretch();
        }

        private void BuildTopBar()
        {
            var a = _setup.Attacker; var d = _setup.Defender;
            var me = _P == BattleState.Attacker ? a : d;
            var en = _P == BattleState.Attacker ? d : a;
            SidePlate(new Vector2(0, 1), new Vector2(20, -16), en, _E, true);
            SidePlate(new Vector2(1, 1), new Vector2(-20, -16), me, _P, false);

            var center = LGBuild.Panel(_board, "Turn", new Color(0.02f, 0.04f, 0.06f, 0.88f));
            center.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -12), new Vector2(420, 84));
            LG.Platter(center.gameObject, 14f).SetRim(new Color(1f, 0.82f, 0.4f, 0.45f));
            var title = LGBuild.Label(center.rectTransform, Loc.F("ШТУРМ · {0}", _setup.PlanetName.ToUpper()), 15, new Color(1f, 0.85f, 0.5f), TextAnchor.UpperCenter, bold: true);
            title.rectTransform.Stretch(10, 0, 10, 8);
            title.Track(1.4f);
            _turnText = LGBuild.Label(center.rectTransform, "", 24, Color.white, TextAnchor.MiddleCenter, bold: true);
            _turnText.rectTransform.Stretch(10, 4, 10, 26);
            _scoreText = LGBuild.Label(center.rectTransform, "", 12, UIManager.DS.TextMuted, TextAnchor.LowerCenter);
            _scoreText.rectTransform.Stretch(10, 6, 10, 0);

            var weather = LGBuild.Panel(_board, "Weather", new Color(0.02f, 0.04f, 0.06f, 0.82f));
            weather.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -100), new Vector2(560, 26));
            LG.Platter(weather.gameObject, 10f).SetRim(new Color(0.6f, 0.8f, 1f, 0.25f));
            _weatherText = LGBuild.Label(weather.rectTransform, "", 11, new Color(0.75f, 0.85f, 0.95f), TextAnchor.MiddleCenter);

            // Рука врага — рубашки сверху
            _enemyHandRow = LGBuild.Rect(_board, "EnemyHand");
            _enemyHandRow.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(330, -26), new Vector2(200, 50));
            _enemyHandText = LGBuild.Label(_enemyHandRow, "", 11, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, bold: true);
            _enemyHandText.rectTransform.Stretch(0, 0, 0, 0);
        }

        private void SidePlate(Vector2 anchor, Vector2 pos, BSide side, int idx, bool left)
        {
            var c = CardArt.Faction(side.Faction);
            var p = LGBuild.Panel(_board, "Side" + idx, new Color(0.02f, 0.04f, 0.06f, 0.85f));
            p.rectTransform.At(anchor, anchor, pos, new Vector2(340, 74));
            LG.Platter(p.gameObject, 14f).SetRim(new Color(c.r, c.g, c.b, 0.5f));
            var port = LGBuild.Rect(p.rectTransform, "Portrait").gameObject.AddComponent<RawImage>();
            port.texture = CardArt.Leader(side.Faction);
            port.raycastTarget = false;
            if (port.texture != null)
            {
                float ta = port.texture.width / (float)port.texture.height;
                port.uvRect = ta > 1 ? new Rect((1 - 1 / ta) * 0.5f, 0, 1 / ta, 1) : new Rect(0, 1 - ta, 1, ta);
            }
            port.rectTransform.At(new Vector2(left ? 0 : 1, 0.5f), new Vector2(left ? 0 : 1, 0.5f), new Vector2(left ? 8 : -8, 0), new Vector2(60, 60));
            var t = LGBuild.Label(p.rectTransform, side.Name ?? "", 15, c, left ? TextAnchor.UpperLeft : TextAnchor.UpperRight, bold: true);
            t.rectTransform.Stretch(left ? 78 : 10, 0, left ? 10 : 78, 12);
            var role = LGBuild.Label(p.rectTransform, idx == BattleState.Attacker ? Loc.T("ДЕСАНТ · атака") : Loc.T("ГАРНИЗОН · оборона"), 11, UIManager.DS.TextMuted, left ? TextAnchor.LowerLeft : TextAnchor.LowerRight, bold: true);
            role.rectTransform.Stretch(left ? 78 : 10, 12, left ? 10 : 78, 0);
            role.Track(1.2f);
        }

        private void BuildSector(int i)
        {
            var s = new SectorUI();
            _sec[i] = s;
            var type = _state.Sectors[i].Type;
            float x = (i - 1) * (ColW + ColGap);
            var col = LGBuild.Rect(_board, "Sector" + i);
            col.anchorMin = col.anchorMax = new Vector2(0.5f, 1f);
            col.pivot = new Vector2(0.5f, 1f);
            col.sizeDelta = new Vector2(ColW, ColH);
            col.anchoredPosition = new Vector2(x, ColTop - 26);
            s.Col = col;

            // Плита: голографическая платформа
            s.Plate = LGBuild.Panel(col, "Plate", new Color(0.02f, 0.05f, 0.08f, 0.62f));
            s.Plate.rectTransform.Stretch();
            LG.Platter(s.Plate.gameObject, 18f).SetRim(new Color(0.5f, 0.8f, 1f, 0.28f));
            s.Wash = LGBuild.Panel(col, "Wash", new Color(1, 1, 1, 0));
            s.Wash.sprite = CardSprites.RoundGlow;
            s.Wash.type = Image.Type.Sliced;
            s.Wash.rectTransform.Stretch(-10, -10, -10, -10);

            // Шапка
            var head = LGBuild.Panel(col, "Header", new Color(0.03f, 0.07f, 0.11f, 0.95f));
            head.rectTransform.TopBand(0, 54, 0, 0);
            LG.Platter(head.gameObject, 14f).SetRim(new Color(1f, 0.82f, 0.42f, 0.35f));
            s.Header = head.rectTransform;
            var ic = LGIcons.Create(head.rectTransform, CardArt.SectorIcon(type), 22, new Color(1f, 0.82f, 0.42f));
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(22, 22));
            s.Name = LGBuild.Label(head.rectTransform, CardArt.SectorName(type), 16, Color.white, TextAnchor.UpperLeft, bold: true);
            s.Name.rectTransform.Stretch(46, 0, 8, 7);
            s.Name.Track(1.6f);
            s.Rule = LGBuild.Label(head.rectTransform, CardArt.SectorRule(type), 10, UIManager.DS.TextMuted, TextAnchor.LowerLeft);
            s.Rule.rectTransform.Stretch(46, 7, 8, 0);
            s.Control = LGBuild.Label(head.rectTransform, "", 11, Color.white, TextAnchor.MiddleRight, bold: true);
            s.Control.rectTransform.Stretch(0, 0, 12, 0);

            // Ряды: враг сверху, вы снизу
            float rowH = BoardCard.y + 14f;
            _rows[i, 0] = Row(col, "RowEnemy", 64, rowH);
            _rows[i, 1] = Row(col, "RowMine", 64 + rowH + 70, rowH);

            // Полоса сил между рядами
            var bar = LGBuild.Panel(col, "Power", new Color(0.01f, 0.03f, 0.05f, 0.9f));
            bar.rectTransform.TopBand(64 + rowH + 6, 58, 12, 12);
            LG.Platter(bar.gameObject, 12f).SetRim(new Color(1, 1, 1, 0.12f));
            s.PowerBar = bar.rectTransform;
            var track = LGBuild.Panel(bar.rectTransform, "Track", new Color(0.4f, 0.75f, 1f, 0.25f));
            track.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ColW - 170, 8));
            s.ControlFill = LGBuild.Panel(track.rectTransform, "Fill", new Color(1f, 0.4f, 0.35f, 0.95f));
            s.ControlFill.rectTransform.anchorMin = new Vector2(0, 0);
            s.ControlFill.rectTransform.anchorMax = new Vector2(0.5f, 1);
            s.ControlFill.rectTransform.offsetMin = s.ControlFill.rectTransform.offsetMax = Vector2.zero;
            s.PowE = LGBuild.Label(bar.rectTransform, "0", 30, CardArt.Faction(_state.Sides[_E].Faction), TextAnchor.MiddleLeft, bold: true);
            s.PowE.rectTransform.Stretch(18, 0, 0, 0);
            s.PowP = LGBuild.Label(bar.rectTransform, "0", 30, CardArt.Faction(_state.Sides[_P].Faction), TextAnchor.MiddleRight, bold: true);
            s.PowP.rectTransform.Stretch(0, 0, 18, 0);
            var lblE = LGBuild.Label(bar.rectTransform, Loc.T("ВРАГ"), 9, UIManager.DS.TextMuted, TextAnchor.UpperLeft, bold: true);
            lblE.rectTransform.Stretch(60, 0, 0, 8);
            var lblP = LGBuild.Label(bar.rectTransform, Loc.T("ВЫ"), 9, UIManager.DS.TextMuted, TextAnchor.UpperRight, bold: true);
            lblP.rectTransform.Stretch(0, 0, 60, 8);

            // Штамп «ВЗЯТ» / «ПОТЕРЯН»
            s.Stamp = LGBuild.Label(col, "", 44, new Color(1, 1, 1, 0), TextAnchor.MiddleCenter, bold: true);
            s.Stamp.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(ColW, 80));
            s.Stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, 8f);
            var ol = s.Stamp.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0, 0, 0, 0.7f);
            ol.effectDistance = new Vector2(3, -3);

            // Купол щита над рядом защитника в Штабе
            if (type == SectorType.HQ)
            {
                int defRow = _P == BattleState.Defender ? 1 : 0;
                s.Dome = LGBuild.Panel(_rows[i, defRow], "Dome", new Color(0.45f, 0.75f, 1f, 0f));
                s.Dome.sprite = CardSprites.Dome;
                s.Dome.rectTransform.anchorMin = new Vector2(0, defRow == 0 ? 0 : 0);
                s.Dome.rectTransform.anchorMax = new Vector2(1, 1);
                s.Dome.rectTransform.offsetMin = new Vector2(-6, -6);
                s.Dome.rectTransform.offsetMax = new Vector2(6, 26);
                if (defRow == 1) s.Dome.rectTransform.localRotation = Quaternion.Euler(0, 0, 0);
                else s.Dome.rectTransform.localRotation = Quaternion.Euler(0, 0, 180f);
            }

            // Клик по колонке — для наёмников и подсказки
            var hit = LGBuild.Panel(col, "Hit", new Color(0, 0, 0, 0), raycast: true);
            hit.rectTransform.Stretch();
            hit.transform.SetAsFirstSibling();
            int idx = i;
            var trig = hit.gameObject.AddComponent<SectorClick>();
            trig.OnClick = () => OnSectorClicked(idx);
        }

        private RectTransform Row(RectTransform col, string name, float top, float h)
        {
            var r = LGBuild.Rect(col, name);
            r.TopBand(top, h, 8, 8);
            return r;
        }

        private void BuildBottom()
        {
            // Очки приказов и колода — слева
            var left = LGBuild.Panel(_board, "Command", new Color(0.02f, 0.04f, 0.06f, 0.88f));
            left.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(20, 20), new Vector2(300, 196));
            LG.Platter(left.gameObject, 16f).SetRim(new Color(1f, 0.82f, 0.4f, 0.4f));
            var cap = LGBuild.Label(left.rectTransform, Loc.T("ОЧКИ ПРИКАЗОВ"), 11, new Color(1f, 0.82f, 0.42f), TextAnchor.UpperLeft, bold: true);
            cap.rectTransform.Stretch(16, 0, 10, 12);
            cap.Track(1.4f);
            _pointsText = LGBuild.Label(left.rectTransform, "0", 46, Color.white, TextAnchor.UpperLeft, bold: true);
            _pointsText.rectTransform.Stretch(16, 0, 10, 30);
            var pips = LGBuild.Rect(left.rectTransform, "Pips");
            pips.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -48), new Vector2(180, 20));
            for (int i = 0; i < 8; i++)
            {
                var pip = LGBuild.Panel(pips, "Pip", new Color(1f, 0.82f, 0.4f, 0.15f));
                pip.sprite = CardSprites.Hex;
                pip.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * 21, 0), new Vector2(18, 18));
                _pointPips.Add(pip);
            }
            _deckText = LGBuild.Label(left.rectTransform, "", 12, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            _deckText.rectTransform.Stretch(16, 0, 10, 92);
            _retreatButton = LGBuild.Button(left.rectTransform, "Retreat", new Color(0.35f, 0.08f, 0.08f, 0.95f), new Color(1f, 0.4f, 0.4f, 0.6f), OnRetreat, LGIcon.Back,
                _P == BattleState.Attacker ? Loc.T("ОТСТУПИТЬ") : Loc.T("СДАТЬСЯ"), 11);
            ((RectTransform)_retreatButton.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(14, 14), new Vector2(130, 34));
            _autoButton = LGBuild.Button(left.rectTransform, "Auto", UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.35f), OnAuto, LGIcon.Fastest, Loc.T("ДОСЧИТАТЬ"), 11);
            ((RectTransform)_autoButton.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(152, 14), new Vector2(134, 34));
            TooltipHelper.Attach(_autoButton.gameObject, Loc.T("Доиграть бой за обе стороны мгновенно (вашей стороной командует ИИ)"));

            // Наёмники Аквилы — купить прямо в бою
            if (_state.Sides[_P].Faction == "aquila")
            {
                _mercButton = LGBuild.Button(_board, "Merc", new Color(0.35f, 0.27f, 0.06f, 0.95f), new Color(1f, 0.8f, 0.35f, 0.7f), OnMerc, LGIcon.Energy, Loc.T("НАЁМНИКИ · 60"), 11);
                ((RectTransform)_mercButton.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(20, 226), new Vector2(180, 34));
                TooltipHelper.Attach(_mercButton.gameObject, Loc.T("<b>Наёмники «Сокол»</b> 4/4 за 60 гелия-3 из казны — без очков приказов. Затем щёлкните по сектору"));
            }

            // Кнопка хода — справа
            var endBtn = LGBuild.Button(_board, "EndTurn", new Color(0.06f, 0.32f, 0.22f, 0.98f), new Color(0.45f, 1f, 0.6f, 0.85f), OnEndTurn, null, null, 12, 18f);
            var ert = (RectTransform)endBtn.transform;
            ert.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 120), new Vector2(250, 86));
            _endButton = endBtn;
            _endButtonImg = endBtn.GetComponent<Image>();
            _endLabel = LGBuild.Label(ert, Loc.T("ЗАКОНЧИТЬ ХОД"), 20, Color.white, TextAnchor.MiddleCenter, bold: true);
            _endLabel.Track(1.2f);
            _hintText = LGBuild.Label(_board, "", 12, UIManager.DS.TextMuted, TextAnchor.LowerRight);
            _hintText.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 214), new Vector2(420, 40));
            _hintText.horizontalOverflow = HorizontalWrapMode.Wrap;

            var speed = LGBuild.Button(_board, "Speed", UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.3f), OnSpeed, LGIcon.Fast, SpeedText(), 11);
            ((RectTransform)speed.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 72), new Vector2(250, 38));
            _speedLabel = speed.GetComponentInChildren<Text>();

            // Журнал — над кнопкой хода
            var log = LGBuild.Panel(_board, "Log", new Color(0.02f, 0.04f, 0.06f, 0.75f));
            log.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 70), new Vector2(232, 300));
            LG.Platter(log.gameObject, 12f).SetRim(new Color(1, 1, 1, 0.1f));
            var lc = LGBuild.Label(log.rectTransform, Loc.T("ХОД БОЯ"), 10, new Color(1f, 0.82f, 0.42f), TextAnchor.UpperLeft, bold: true);
            lc.rectTransform.Stretch(12, 0, 8, 10);
            lc.Track(1.4f);
            _logText = LGBuild.Label(log.rectTransform, "", 11, new Color(0.82f, 0.88f, 0.94f), TextAnchor.UpperLeft);
            _logText.rectTransform.Stretch(12, 10, 10, 30);
            _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _logText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private string SpeedText() => Loc.F("АНИМАЦИИ ×{0:0.#}", _animSpeed);

        private void OnSpeed()
        {
            _animSpeed = _animSpeed >= 2f ? 1f : _animSpeed + 0.5f;
            PlayerPrefs.SetFloat("cards.speed", _animSpeed);
            if (_fx != null) _fx.Speed = _animSpeed;
            if (_speedLabel != null) _speedLabel.text = SpeedText();
            SFXManager.Play(Sfx.UiClick);
        }

        // ==================== ВИДЫ КАРТ ====================

        private string FactionOf(int side) => _state.Sides[side].Faction;

        private CardView MakeUnitView(BUnit u, bool revealed)
        {
            int row = u.Side == _P ? 1 : 0;
            var v = CardView.Create(_rows[u.Sector, row], u.Card, u.Side, FactionOf(u.Side), BoardCard, true);
            v.Uid = u.Uid;
            v.SetFace(revealed || u.Side == _P, true);
            if (u.IsTrap && u.Side == _P) { v.SetFace(true, true); v.SetStatus(Loc.T("ЛОВУШКА"), new Color(1f, 0.4f, 0.3f)); v.SetAlpha(0.8f); }
            v.RefreshNumbers(_state.Power(u), u.Armor, u.MaxArmor, false);
            Hook(v);
            _units[u.Uid] = v;
            return v;
        }

        private void Hook(CardView v)
        {
            v.OnHoverIn = ShowPreview;
            v.OnHoverOut = HidePreview;
            v.SetHoverable(false);
        }

        private void ShowPreview(CardView v)
        {
            if (v == null || v.Card == null) return;
            if (!v.FaceUp && v.Side != _P) return;   // рубашку врага не подсмотреть
            if (_preview != null) Destroy(_preview.gameObject);
            _preview = CardView.Create(_previewRoot, v.Card, v.Side, v.SideFaction, PreviewCard, false);
            _preview.Rt.anchoredPosition = Vector2.zero;
            if (v.Uid >= 0)
            {
                var u = _state.FindUnit(v.Uid);
                if (u != null) _preview.RefreshNumbers(_state.Power(u), u.Armor, u.MaxArmor, false);
            }
            _preview.Punch(0.04f);
            foreach (var g in _preview.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
        }

        private void HidePreview(CardView v)
        {
            if (_preview != null && (v == null || _preview.Card == v.Card)) { Destroy(_preview.gameObject); _preview = null; }
        }

        // ==================== РУКА ====================

        private void AddHandCard(BCard c, bool animateFromDeck)
        {
            var v = CardView.Create(_handRoot, c, _P, FactionOf(_P), HandCard, false);
            v.Interactive = true;
            v.SetHoverable(true);
            v.OnHoverIn = x => { ShowPreview(x); x.Rt.SetAsLastSibling(); };
            v.OnHoverOut = HidePreview;
            v.OnDragBegin = BeginDrag;
            v.OnDragMove = Drag;
            v.OnDragEnd = EndDrag;
            if (animateFromDeck)
            {
                v.Rt.anchoredPosition = new Vector2(-700, -40);
                v.Rt.localRotation = Quaternion.Euler(0, 0, 30f);
                v.SetFace(false, true);
                v.SetFace(true, false);
            }
            _hand.Add(v);
            LayoutHand(false);
        }

        private void LayoutHand(bool instant)
        {
            int n = _hand.Count;
            float spread = Mathf.Min(150f, n > 1 ? 960f / (n - 1) : 0f);
            for (int i = 0; i < n; i++)
            {
                float t = n > 1 ? i / (float)(n - 1) - 0.5f : 0f;
                var pos = new Vector2(t * spread * (n - 1), -Mathf.Abs(t) * Mathf.Abs(t) * 90f * (n > 4 ? 1f : 0.5f));
                float rot = -t * (n > 1 ? 14f : 0f);
                _targets[_hand[i]] = pos;
                _targetRot[_hand[i]] = rot;
                if (instant) { _hand[i].Rt.anchoredPosition = pos; _hand[i].Rt.localRotation = Quaternion.Euler(0, 0, rot); }
            }
        }

        private void BeginDrag(CardView v, PointerEventData e)
        {
            if (_busy || _state.Over) return;
            _dragging = v;
            v.Rt.SetParent(_board, true);
            v.Rt.SetAsLastSibling();
            _targets.Remove(v);
            HidePreview(null);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_board, e.position, null, out var lp);
            _dragOffset = (Vector2)v.Rt.localPosition - lp;
            SFXManager.Play(Sfx.UiTab);
        }

        private void Drag(CardView v, PointerEventData e)
        {
            if (_dragging != v) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_board, e.position, null, out var lp);
            var target = lp + _dragOffset * 0.3f;
            v.Rt.localPosition = Vector3.Lerp(v.Rt.localPosition, target, 0.6f);
            float vx = e.delta.x;
            v.Rt.localRotation = Quaternion.Euler(0, 0, Mathf.Clamp(-vx * 0.6f, -18f, 18f));
            int sec = SectorUnder(e.position);
            for (int i = 0; i < 3; i++)
                _sec[i].HeaderGlowSet(i == sec ? (_state.CanPlay(_P, v.Card, i) == null ? 1 : -1) : 0);
        }

        private void EndDrag(CardView v, PointerEventData e)
        {
            if (_dragging != v) return;
            _dragging = null;
            for (int i = 0; i < 3; i++) _sec[i].HeaderGlowSet(0);
            int sec = SectorUnder(e.position);
            string why = sec >= 0 ? _state.CanPlay(_P, v.Card, sec) : null;
            if (sec >= 0 && why == null && _state.Play(_P, v.Card, sec))
            {
                _hand.Remove(v);
                _targets.Remove(v);
                _targetRot.Remove(v);
                var pos = v.Rt.position;
                Destroy(v.gameObject);
                var pv = CardView.Create(_rows[sec, 1], v.Card, _P, FactionOf(_P), BoardCard, true);
                pv.Rt.position = pos;
                pv.SetAlpha(0.82f);
                pv.SetStatus(Loc.T("ПРИКАЗ ОТДАН · клик — отменить"), new Color(0.5f, 0.85f, 1f));
                pv.OnClick = UnplayView;
                pv.OnHoverIn = ShowPreview;
                pv.OnHoverOut = HidePreview;
                _pending[v.Card] = pv;
                pv.Punch(0.2f);
                _fx.Dust(_fx.Local(_rows[sec, 1]), 0.6f);
                SFXManager.Play(Sfx.DealPlace);
                LayoutAll(false);
                LayoutHand(false);
                RefreshPanels();
                return;
            }
            // Не вышло — карта возвращается в руку
            v.Rt.SetParent(_handRoot, true);
            v.Hit(0.4f);
            if (sec >= 0 && why != null) { Hint(why, true); SFXManager.Play(Sfx.UiDenied); }
            LayoutHand(false);
        }

        private void UnplayView(CardView pv)
        {
            if (_busy || pv == null) return;
            if (!_state.Unplay(_P, pv.Card)) return;
            _pending.Remove(pv.Card);
            HidePreview(null);
            Destroy(pv.gameObject);
            AddHandCard(pv.Card, false);
            SFXManager.Play(Sfx.DealRemove);
            LayoutAll(false);
            RefreshPanels();
        }

        private int SectorUnder(Vector2 screen)
        {
            for (int i = 0; i < 3; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(_sec[i].Col, screen, null)) return i;
            return -1;
        }

        // ==================== РАСКЛАДКА ====================

        private void LayoutAll(bool instant)
        {
            for (int s = 0; s < 3; s++)
                for (int r = 0; r < 2; r++)
                {
                    var row = _rows[s, r];
                    var list = new List<CardView>();
                    foreach (Transform t in row)
                    {
                        var v = t.GetComponent<CardView>();
                        if (v != null && !_dying.Contains(v)) list.Add(v);
                    }
                    int n = list.Count;
                    float w = ColW - 30f;
                    float step = n > 1 ? Mathf.Min(BoardCard.x + 8f, (w - BoardCard.x) / (n - 1)) : 0f;
                    for (int i = 0; i < n; i++)
                    {
                        var pos = new Vector2((i - (n - 1) * 0.5f) * step, 0f);
                        _targets[list[i]] = pos;
                        _targetRot[list[i]] = 0f;
                        if (instant) { list[i].Rt.anchoredPosition = pos; list[i].Rt.localRotation = Quaternion.identity; }
                    }
                }
        }

        private readonly HashSet<CardView> _dying = new HashSet<CardView>();

        private void Update()
        {
            if (!IsOpen) return;
            float dt = Time.unscaledDeltaTime;
            var dead = new List<CardView>();
            foreach (var kv in _targets)
            {
                var v = kv.Key;
                if (v == null) { dead.Add(v); continue; }
                if (v == _dragging) continue;
                v.Rt.anchoredPosition = Vector2.Lerp(v.Rt.anchoredPosition, kv.Value, 1f - Mathf.Exp(-dt * 11f * _animSpeed));
                float rot = _targetRot.TryGetValue(v, out var r) ? r : 0f;
                v.Rt.localRotation = Quaternion.Slerp(v.Rt.localRotation, Quaternion.Euler(0, 0, rot), 1f - Mathf.Exp(-dt * 10f));
            }
            foreach (var v in dead) _targets.Remove(v);

            // Подсветка: что можно сыграть
            if (!_busy && !_state.Over)
                foreach (var v in _hand) v.Playable = _state.CostOf(_P, v.Card) <= _state.Sides[_P].Points && v.Card.Def.Kind != CardKind.Structure;
            else foreach (var v in _hand) v.Playable = false;

            // Кнопка хода пульсирует, пока очки не потрачены
            if (_endButtonImg != null)
            {
                bool idle = !_busy && !_state.Over;
                float p = idle && _state.Sides[_P].Points == 0 ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f) : 0f;
                _endButtonImg.color = idle ? Color.Lerp(new Color(0.06f, 0.32f, 0.22f, 0.98f), new Color(0.12f, 0.55f, 0.36f, 1f), p) : new Color(0.08f, 0.1f, 0.12f, 0.9f);
                _endLabel.text = _busy ? Loc.T("БОЙ ИДЁТ…") : Loc.T("ЗАКОНЧИТЬ ХОД");
            }

            // Атмосфера: пыль/пепел/дождь
            AmbientTick(dt);

            // Плиты секторов: волна цвета при смене контроля
            foreach (var s in _sec)
            {
                if (s == null) continue;
                s.WashA = Mathf.MoveTowards(s.WashA, 0.18f, dt * 0.5f);
                var c = s.Wash.color; c.a = s.WashA; s.Wash.color = c;
                if (s.Stamp.color.a > 0f) { var sc = s.Stamp.color; sc.a = Mathf.Max(0, sc.a - dt * 0.6f); s.Stamp.color = sc; }
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) OnEndTurn();
        }

        private float _ambientT;

        private void AmbientTick(float dt)
        {
            _ambientT -= dt;
            if (_ambientT > 0f || _fx == null) return;
            _ambientT = 0.12f;
            var r = _fxLayer.rect;
            var pos = new Vector2(UnityEngine.Random.Range(-r.width * 0.5f, r.width * 0.5f), r.height * 0.5f + 20f);
            switch (_setup.Terrain)
            {
                case Terrain.Molten:
                    _fx.Burst(new Vector2(pos.x, -r.height * 0.5f), 1, CardSprites.Dot, new Color(1f, 0.55f, 0.2f, 0.9f), 40f, 120f, 4f, 7f, 6f, 2f, -30f, 0.2f, 90f, 40f);
                    break;
                case Terrain.Desert:
                    _fx.Burst(new Vector2(-r.width * 0.5f - 20f, UnityEngine.Random.Range(-r.height * 0.4f, r.height * 0.4f)), 1, CardSprites.Smoke, new Color(0.8f, 0.6f, 0.4f, 0.12f), 140f, 260f, 5f, 8f, 120f, 220f, 0f, 0.1f, 0f, 20f);
                    break;
                case Terrain.Continental:
                    if (_state.WeatherActive) _fx.Burst(pos, 3, CardSprites.Spark, new Color(0.7f, 0.8f, 0.95f, 0.35f), 900f, 1200f, 1.2f, 1.6f, 22f, 22f, 0f, 0f, 255f, 6f, align: true, stretch: 3f);
                    else _fx.Burst(pos, 1, CardSprites.Dot, new Color(0.8f, 0.85f, 0.9f, 0.25f), 20f, 50f, 8f, 12f, 4f, 4f, 6f, 0f, 270f, 60f);
                    break;
                default:
                    _fx.Burst(pos, 1, CardSprites.Dot, new Color(0.8f, 0.8f, 0.85f, 0.25f), 10f, 30f, 10f, 14f, 3f, 3f, 4f, 0f, 270f, 90f);
                    break;
            }
        }

        // ==================== ПАНЕЛИ ====================

        private void RefreshPanels()
        {
            var me = _state.Sides[_P];
            _turnText.text = _state.Over ? Loc.T("БОЙ ОКОНЧЕН") : Loc.F("ХОД {0} / {1}", Mathf.Max(1, _state.Turn), BattleState.Turns);
            int mine = 0, theirs = 0;
            for (int i = 0; i < 3; i++) { if (_state.Sectors[i].Control == _P) mine++; else theirs++; }
            _scoreText.text = Loc.F("Секторы: вы {0} : {1} враг · нужно 2 из 3{2}", mine, theirs, _P == BattleState.Attacker ? Loc.T(" (ничья — за обороной)") : "");
            _weatherText.text = _state.WeatherActive ? Loc.F("<b>{0}</b> · {1}", _state.WeatherName, _state.WeatherText)
                : Loc.T("Погода планеты откроется на 3-м ходу");
            _pointsText.text = me.Points.ToString();
            int total = Mathf.Max(me.Points, _state.Turn + me.PointsPerTurn);
            for (int i = 0; i < _pointPips.Count; i++)
            {
                _pointPips[i].gameObject.SetActive(i < Mathf.Max(total, me.Points));
                _pointPips[i].color = i < me.Points ? new Color(1f, 0.82f, 0.4f, 1f) : new Color(1f, 0.82f, 0.4f, 0.15f);
            }
            _deckText.text = Loc.F("Колода: {0} · рука: {1}\nВ бою потеряно: {2}", me.Deck.Count, me.Hand.Count, CountDead(_P));
            _enemyHandText.text = Loc.F("Рука врага: {0} · колода {1}", _state.Sides[_E].Hand.Count, _state.Sides[_E].Deck.Count);
            for (int i = 0; i < 3; i++) RefreshSector(i);
            _logText.text = string.Join("\n", _log.Count > 14 ? _log.GetRange(_log.Count - 14, 14) : _log);
            if (_mercButton != null) _mercButton.interactable = !_busy && !_state.Over && me.Treasury != null && me.Treasury() >= 60f;
        }

        private int CountDead(int side)
        {
            int n = 0;
            foreach (var s in _state.Sectors) foreach (var u in s.Units) if (u.Dead && u.Side == side && !u.IsTrap) n++;
            return n;
        }

        private void RefreshSector(int i)
        {
            var s = _sec[i];
            int pe = _state.SectorPower(_E, i), pp = _state.SectorPower(_P, i);
            s.PowE.text = pe.ToString();
            s.PowP.text = pp.ToString();
            float f = pe + pp <= 0 ? 0.5f : pe / (float)(pe + pp);
            s.ControlFill.rectTransform.anchorMax = new Vector2(f, 1f);
            s.ControlFill.color = CardArt.Faction(FactionOf(_E));
            int c = _state.Sectors[i].Control;
            s.Control.text = c == _P ? Loc.T("<color=#7CF29A>ВАШ</color>") : Loc.T("<color=#FF8A80>У ВРАГА</color>");
            if (s.Dome != null)
            {
                bool up = _state.ShieldUp;
                s.Dome.color = new Color(0.45f, 0.75f, 1f, up ? 0.35f + 0.08f * Mathf.Sin(Time.unscaledTime * 2f) : 0f);
            }
            if (s.LastControl != c)
            {
                if (s.LastControl != -2)
                {
                    var col = c == _P ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 0.4f, 0.35f);
                    s.Wash.color = new Color(col.r, col.g, col.b, 0.9f);
                    s.WashA = 0.9f;
                    s.Stamp.text = c == _P ? Loc.T("ВЗЯТ") : Loc.T("ПОТЕРЯН");
                    s.Stamp.color = new Color(col.r, col.g, col.b, 1f);
                }
                else s.Wash.color = c == _P ? new Color(0.45f, 1f, 0.6f, 0.18f) : new Color(1f, 0.4f, 0.35f, 0.18f);
                s.LastControl = c;
            }
        }

        private void Hint(string text, bool bad = false)
        {
            if (_hintText == null) return;
            _hintText.text = text;
            _hintText.color = bad ? new Color(1f, 0.55f, 0.5f) : UIManager.DS.TextMuted;
        }

        private void AddLog(string line)
        {
            _log.Add(line);
            if (_log.Count > 60) _log.RemoveAt(0);
        }

        // ==================== КНОПКИ ====================

        private void OnEndTurn()
        {
            if (_busy || _state == null || _state.Over || _dragging != null) return;
            SFXManager.Play(Sfx.UiConfirm);
            StartCoroutine(ResolveTurn());
        }

        private void OnRetreat()
        {
            if (_busy || _state.Over) return;
            _state.Retreat(_P);
            StartCoroutine(PlayEvents());
        }

        private void OnAuto()
        {
            if (_busy || _state.Over) return;
            StartCoroutine(AutoRest());
        }

        private void OnMerc()
        {
            if (_busy || _state.Over) return;
            _mercPicking = true;
            Hint(Loc.T("Щёлкните по сектору, куда высадить наёмников"));
            SFXManager.Play(Sfx.UiClick);
        }

        private void OnSectorClicked(int i)
        {
            if (!_mercPicking) return;
            _mercPicking = false;
            var def = CardCatalog.Get("Q1");
            if (def == null || !_state.BuyMerc(_P, def, i, 60f)) { Hint(Loc.T("Не хватает гелия-3"), true); return; }
            var sd = _state.Sides[_P];
            var bc = sd.Pending[sd.Pending.Count - 1].card;
            var pv = CardView.Create(_rows[i, 1], bc, _P, FactionOf(_P), BoardCard, true);
            pv.SetAlpha(0.82f);
            pv.SetStatus(Loc.T("НАЁМНИКИ"), new Color(1f, 0.8f, 0.35f));
            pv.OnHoverIn = ShowPreview; pv.OnHoverOut = HidePreview;
            _pending[bc] = pv;
            pv.Punch(0.3f);
            SFXManager.Play(Sfx.DealPlace);
            LayoutAll(false);
            RefreshPanels();
        }

        // ==================== ОКНО «ВРАГ ШТУРМУЕТ» ====================

        private void ShowDefensePrompt(int attacker, StarSystem sys)
        {
            if (_canvas == null) BuildCanvas();
            var tm = TimeManager.Instance;
            int saved = tm != null ? tm.CurrentSpeed : 1;
            if (tm != null) tm.SetSpeed(0);
            AssaultLink.BattleRunning = true;
            foreach (Transform c in _canvas.transform) Destroy(c.gameObject);
            _root.SetActive(true);
            var croot = (RectTransform)_canvas.transform;
            var dim = LGBuild.Panel(croot, "Dim", new Color(0, 0, 0, 0.7f), raycast: true);
            dim.rectTransform.Stretch();
            var box = LGBuild.Panel(croot, "Prompt", new Color(0.03f, 0.05f, 0.08f, 0.97f));
            box.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 300));
            LG.Glass(box.gameObject).SetRim(new Color(1f, 0.4f, 0.35f, 0.7f));
            LG.Motion(box.gameObject, LGAppear.Kind.Pop);
            var t = LGBuild.Label(box.rectTransform, Loc.T("ВРАГ ВЫСАЖИВАЕТСЯ!"), 30, new Color(1f, 0.5f, 0.45f), TextAnchor.UpperCenter, bold: true);
            t.rectTransform.Stretch(20, 0, 20, 26);
            t.Track(1.5f);
            var body = LGBuild.Label(box.rectTransform, Loc.F("{0} штурмует колонию {1}. Гарнизон, ополчение и укрепления готовы к бою.\nКомандовать обороной лично или довериться автобою?",
                AIEmpireManager.NameOf(attacker, Loc.T("Враг")), sys.Name), 15, Color.white, TextAnchor.UpperCenter, wrap: true);
            body.rectTransform.Stretch(40, 100, 40, 84);
            SFXManager.Play(Sfx.RedAlert);
            Button Btn(string label, Color tint, Vector2 pos, Action act)
            {
                var b = LGBuild.Button(box.rectTransform, label, tint, new Color(1, 1, 1, 0.5f), act, null, label, 14);
                ((RectTransform)b.transform).At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), pos, new Vector2(270, 54));
                return b;
            }
            Btn(Loc.T("ЗАЩИЩАТЬСЯ ЛИЧНО"), new Color(0.08f, 0.38f, 0.26f, 1f), new Vector2(-145, 26), () =>
            {
                AssaultLink.BattleRunning = false;
                _root.SetActive(false);
                if (tm != null) tm.SetSpeed(saved);
                AssaultLink.StartDefense(attacker, sys, false);
            });
            Btn(Loc.T("АВТОБОЙ"), UIManager.DS.BtnNeutral, new Vector2(145, 26), () =>
            {
                AssaultLink.BattleRunning = false;
                _root.SetActive(false);
                if (tm != null) tm.SetSpeed(saved);
                AssaultLink.StartDefense(attacker, sys, true);
            });
        }

        /// <summary>Щелчок по колонке сектора.</summary>
        private sealed class SectorClick : MonoBehaviour, IPointerClickHandler
        {
            public Action OnClick;
            public void OnPointerClick(PointerEventData e) => OnClick?.Invoke();
        }
    }
}
