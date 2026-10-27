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
    /// Экран карточного штурма — полноэкранный стол поверх галактики (время стратегии на паузе), в духе Hearthstone:
    ///   • в центре — стол в массивной бронзовой раме, сквозь него видна поверхность планеты; три сектора-дорожки:
    ///     сверху ряд врага, посередине плашка сектора с артом и правилом, по краям плашки — гнёзда силы (враг/вы),
    ///     снизу ваш ряд; пустые места под карты — вдавленные выемки;
    ///   • портрет врага в золотой раме над столом, его рука — рубашками; ваш портрет слева от руки;
    ///   • рука веером снизу: наведение поднимает и увеличивает карту, карта перетаскивается в сектор;
    ///   • справа — счётчик хода, большая кнопка «Закончить ход» (янтарная — есть что сыграть, зелёная — всё сыграно),
    ///     счёт секторов, погода, скорость/автобой/отступление; внизу справа — кристаллы очков приказов;
    ///   • слева — колоды обеих сторон и хроника боя.
    /// Ход подводится анимациями из журнала событий правил (BattleScreen.Play.cs).
    /// </summary>
    public sealed partial class BattleScreen : MonoBehaviour
    {
        public static BattleScreen Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;

        // Раскладка (центр экрана 1920×1080, y вверх)
        private const float TrayY = 80f, TrayW = 1480f, TrayH = 600f, Lane = 482f, LaneW = 470f, RowOff = 200f;
        private const float PlaqueW = 440f, PlaqueH = 118f, OrbX = 170f, OrbY = 66f;
        private const float RX = 848f, LX = -848f, HandY = -470f;
        private static readonly Vector2 BoardCard = new Vector2(128, 178);
        private static readonly Vector2 HandCard = new Vector2(200, 280);
        private static readonly Vector2 PreviewCard = new Vector2(300, 420);

        private Canvas _canvas;
        private CanvasScaler _scaler;
        private GameObject _root;
        private RectTransform _board, _fxLayer, _overlay, _handRoot, _previewRoot, _dragLayer;
        private UIFx _fx;
        private CardParticles _particles;
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
        private CardView _handHover;
        private Vector2 _dragOffset;

        // Панели
        private Text _turnText, _scoreText, _weatherTitle, _weatherText, _pointsText, _logText, _endLabel, _hintText, _speedLabel, _deckE, _deckP;
        private Image _endButtonImg, _endGlow;
        private Button _endButton, _retreatButton, _autoButton, _mercButton;
        private readonly List<Image> _crystals = new List<Image>();
        private RectTransform _enemyHandRow;
        private int _enemyHandShown = -1;
        private readonly List<string> _log = new List<string>();
        private bool _mercPicking;
        private string _endState;

        private sealed class SectorUI
        {
            public RectTransform Col, Header, PowerBar;
            public Image Plate, Wash, Dome, Highlight, HighlightFill, OrbGlowE, OrbGlowP, DomeBadge;
            public LiquidGlassEffect PlateFx, ChipFx;
            public Image Chip;
            public Text ChipText;
            public Text DomeText;
            public bool DomeWasUp = true;
            public Text Name, Rule, PowE, PowP, Stamp;
            public float WashA, HighA, HighT;
            public Color WashC, HighC;
            public int LastControl = -2;
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
            _scaler = go.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920, 1080);
            go.AddComponent<GraphicRaycaster>();
            _root = go;
            go.SetActive(false);
        }

        /// <summary>Стол рассчитан на 1920×1080: на любом экране он целиком помещается (узкий — по ширине, широкий — по высоте).</summary>
        private void FitScaler()
        {
            if (_scaler == null) return;
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            _scaler.matchWidthOrHeight = aspect >= 16f / 9f ? 1f : 0f;
        }

        // ==================== ОТКРЫТИЕ ====================

        public void Open(BattleSetup setup, Action<BattleState> onDone)
        {
            if (_canvas == null) BuildCanvas();
            FitScaler();
            _setup = setup;
            _onDone = onDone;
            _P = setup.Defender.IsPlayer && !setup.Attacker.IsPlayer ? BattleState.Defender : BattleState.Attacker;
            _E = 1 - _P;
            _state = new BattleState(setup);
            _evCursor = 0;
            _busy = false;
            _mercPicking = false;
            _enemyHandShown = -1;
            _endState = null;
            _pointsShown = -1;
            _handHover = null;
            _units.Clear(); _pending.Clear(); _hand.Clear(); _targets.Clear(); _targetRot.Clear(); _log.Clear(); _crystals.Clear(); _dying.Clear();
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
                    if (u.Has(Kw.Shield)) continue;           // щит Штаба — купол над рядом, а не карта
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
            _particles?.SetActive(false);
            if (_root != null) _root.SetActive(false);
            var tm = TimeManager.Instance;
            if (tm != null) tm.SetSpeed(_savedSpeed);
            var done = _onDone;
            var st = _state;
            _onDone = null;
            done?.Invoke(st);
        }

        // ==================== СЦЕНА ====================

        private static RectTransform At(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var r = LGBuild.Rect(parent, name);
            r.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            return r;
        }

        private static RawImage Raw(RectTransform parent, string name, Texture tex, Color c)
        {
            var r = LGBuild.Rect(parent, name).gameObject.AddComponent<RawImage>();
            r.texture = tex;
            r.color = c;
            r.raycastTarget = false;
            return r;
        }

        private static void Cover(RawImage img, float frameAspect, float yBias = 0.5f)
        {
            if (img.texture == null) return;
            float ta = img.texture.width / (float)img.texture.height;
            img.uvRect = ta > frameAspect ? new Rect((1 - frameAspect / ta) * 0.5f, 0, frameAspect / ta, 1)
                                          : new Rect(0, (1 - ta / frameAspect) * yBias, 1, ta / frameAspect);
        }

        private void BuildScene()
        {
            var croot = (RectTransform)_canvas.transform;

            // Фон: поверхность планеты, приглушённая, и тяжёлая виньетка
            var bgRt = LGBuild.Rect(croot, "Bg");
            bgRt.Stretch();
            var bgBlock = bgRt.gameObject.AddComponent<Image>();
            bgBlock.color = new Color(0.01f, 0.012f, 0.02f, 1f);
            bgBlock.raycastTarget = true;   // клики не проходят на карту галактики
            var table = CardArt.Table(_setup.Terrain);
            _bg = Raw(bgRt, "Terrain", table, new Color(0.36f, 0.36f, 0.4f, 1f));
            _bg.rectTransform.Stretch(-40, -40, -40, -40);
            Cover(_bg, 16f / 9f);
            var vig = LGBuild.Panel(bgRt, "Vignette", new Color(0, 0, 0, 0.95f));
            vig.sprite = CardSprites.Vignette;
            vig.raycastTarget = false;
            vig.rectTransform.Stretch();

            _board = LGBuild.Rect(croot, "Board");
            _board.Stretch();

            BuildTray(table);
            for (int i = 0; i < 3; i++) BuildSector(i);
            BuildPortraits();
            BuildRight();
            BuildLeft();

            _hintText = Caption(_board, "", 15, new Color(1f, 0.93f, 0.8f), TextAnchor.MiddleCenter, 0.8f);
            var hol = _hintText.gameObject.AddComponent<Outline>();
            hol.effectColor = new Color(0f, 0.02f, 0.04f, 0.85f);
            hol.effectDistance = new Vector2(1.5f, -1.5f);
            _hintText.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -255), new Vector2(900, 30));

            // Рука, перетаскиваемая карта, предпросмотр, эффекты и баннеры — отдельными слоями поверх стола:
            // порядок отрисовки задан явно, карта из руки никогда не уйдёт «под стол»
            _handRoot = At(_board, "Hand", new Vector2(0, HandY), new Vector2(1100, 300));
            Layer(_handRoot, 1, true);
            _dragLayer = LGBuild.Rect(croot, "Drag");
            _dragLayer.Stretch();
            Layer(_dragLayer, 2, false);
            _previewRoot = At(croot, "Preview", Vector2.zero, PreviewCard);
            Layer(_previewRoot, 3, false);

            _fxLayer = LGBuild.Rect(croot, "Fx");
            _fxLayer.Stretch();
            Layer(_fxLayer, 4, false);
            _fx = _fxLayer.gameObject.AddComponent<UIFx>();
            _fx.Init(_fxLayer, _board);
            _fx.Speed = _animSpeed;
            // Настоящие частицы (Particle Pack) — своей камерой в текстуру под спрайтовыми эффектами
            if (_particles != null) Destroy(_particles.gameObject);
            _particles = CardParticles.Create(_fxLayer);
            _particles.Speed = _animSpeed;
            _particles.SetActive(true);
            _fx.Particles = _particles;

            _overlay = LGBuild.Rect(croot, "Overlay");
            _overlay.Stretch();
            Layer(_overlay, 5, true);
        }

        /// <summary>Вложенный канвас со своим порядком отрисовки поверх стола (и своим лучом для кликов, если нужен).</summary>
        private void Layer(RectTransform rt, int order, bool raycast)
        {
            var c = rt.gameObject.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = _canvas.sortingOrder + order;
            if (raycast) rt.gameObject.AddComponent<GraphicRaycaster>();
        }

        // ==================== ОФОРМЛЕНИЕ В СТИЛЕ ИГРЫ ====================
        // Рамки, пульт, хроника и таблички — то же «жидкое стекло», что в окнах стратегии:
        // тёмное стекло с бирюзовой кромкой, шрифт игры, золотые заголовки разрядкой. Карты остаются картами.

        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);
        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static readonly Color PanelTint = new Color(0.010f, 0.025f, 0.038f, 0.92f);
        private static readonly Color SubTint = new Color(0.020f, 0.045f, 0.065f, 0.90f);
        private static readonly Color RimCyan = new Color(0.45f, 0.95f, 0.90f, 0.42f);
        private static readonly Color RimSoft = new Color(0.45f, 0.95f, 0.90f, 0.18f);

        /// <summary>Стеклянная панель (окно) или подложка (platter) с цветной кромкой.</summary>
        private static Image GlassAt(RectTransform parent, string name, Vector2 pos, Vector2 size, Color tint, Color rim, bool platter = false, float radius = -1f, bool raycast = false)
        {
            var img = LGBuild.Panel(parent, name, tint, raycast);
            img.rectTransform.At(Mid, Mid, pos, size);
            var fx = platter ? LG.Platter(img.gameObject, radius) : LG.Glass(img.gameObject, radius);
            if (fx != null) fx.SetRim(rim);
            return img;
        }

        /// <summary>Капсула-индикатор с неоновой кромкой (круглая при равных сторонах).</summary>
        private static Image ChipAt(RectTransform parent, string name, Vector2 pos, Vector2 size, Color tint, Color rim, bool raycast = false)
        {
            var img = LGBuild.Panel(parent, name, tint, raycast);
            img.rectTransform.At(Mid, Mid, pos, size);
            LG.Chip(img.gameObject, rim, Mathf.Min(size.x, size.y) * 0.5f);
            return img;
        }

        /// <summary>Подпись шрифтом игры (жирный Play) с разрядкой — заголовки, числа, кнопки.</summary>
        private static Text Caption(Transform parent, string text, int size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, float track = 1.4f)
        {
            var t = LGBuild.Label(parent, text, size, c, anchor, bold: true);
            if (track > 0f) t.Track(track);
            return t;
        }

        /// <summary>Заголовок панели как в окнах игры: золотые капители с разрядкой и тонкая линия под ними.</summary>
        private static Text PanelHeader(RectTransform panel, string text, float top = 12f)
        {
            var h = Caption(panel, text, 11, Gold, TextAnchor.UpperCenter, 1.4f);
            h.rectTransform.TopBand(top, 16, 12, 12);
            var line = LGBuild.Panel(panel, "HeaderLine", new Color(RimCyan.r, RimCyan.g, RimCyan.b, 0.22f));
            line.raycastTarget = false;
            line.rectTransform.TopBand(top + 22, 1, 18, 18);
            return h;
        }

        /// <summary>Крупный штамп поверх дорожки («ВЗЯТ» / «ПОТЕРЯН»): шрифт игры с плотной обводкой.</summary>
        private static Text Stamp(RectTransform parent, int size)
        {
            var t = Caption(parent, "", size, new Color(1, 1, 1, 0), TextAnchor.MiddleCenter, 4f);
            var ol = t.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0f, 0.02f, 0.04f, 0.9f);
            ol.effectDistance = new Vector2(2.5f, -2.5f);
            return t;
        }

        /// <summary>Стол: тень, поверхность планеты под тёмным стеклом в стеклянной оправе с бирюзовой кромкой.</summary>
        private void BuildTray(Texture table)
        {
            var shadow = LGBuild.Panel(_board, "TrayShadow", new Color(0, 0, 0, 0.8f));
            shadow.sprite = CardSprites.RoundGlow;
            shadow.type = Image.Type.Sliced;
            shadow.raycastTarget = false;
            shadow.rectTransform.At(Mid, Mid, new Vector2(0, TrayY - 14), new Vector2(TrayW + 90, TrayH + 90));

            // Оправа — стеклянная панель игры; поверхность вставлена внутрь с отступом, по краю видна кромка
            GlassAt(_board, "TrayBezel", new Vector2(0, TrayY), new Vector2(TrayW, TrayH), PanelTint, RimCyan, false, 26f);

            var mask = LGBuild.Panel(_board, "TrayMask", Color.white);
            mask.sprite = CardSprites.Round;
            mask.type = Image.Type.Sliced;
            mask.raycastTarget = false;
            mask.rectTransform.At(Mid, Mid, new Vector2(0, TrayY), new Vector2(TrayW - 28, TrayH - 28));
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var surf = Raw(mask.rectTransform, "Surface", table, new Color(0.62f, 0.62f, 0.66f, 1f));
            surf.rectTransform.Stretch();
            Cover(surf, (TrayW - 28) / (TrayH - 28));
            var glass = LGBuild.Panel(mask.rectTransform, "Glass", new Color(0.01f, 0.035f, 0.055f, 0.5f));
            glass.rectTransform.Stretch();
            glass.raycastTarget = false;
            // Голосетка: еле заметные соты по всему столу
            var grid = CardSkin.Simple(mask.rectTransform, "Grid", "hexgrid", new Color(0.45f, 0.95f, 0.9f, 0.05f));
            grid.rectTransform.Stretch();
            var inner = LGBuild.Panel(mask.rectTransform, "InnerShade", new Color(0, 0, 0, 0.7f));
            inner.sprite = CardSprites.Vignette;
            inner.raycastTarget = false;
            inner.rectTransform.Stretch();

            // Разделители дорожек — тонкие бирюзовые линии, как в окнах игры
            for (int i = 1; i < 3; i++)
            {
                var dv = LGBuild.Panel(_board, "Divider", new Color(RimCyan.r, RimCyan.g, RimCyan.b, 0.16f));
                dv.raycastTarget = false;
                dv.rectTransform.At(Mid, Mid, new Vector2((i - 1.5f) * Lane, TrayY), new Vector2(1, TrayH - 80));
            }
        }

        private void BuildSector(int i)
        {
            var s = new SectorUI();
            _sec[i] = s;
            var type = _state.Sectors[i].Type;
            float lx = (i - 1) * Lane;
            var col = At(_board, "Sector" + i, new Vector2(lx, TrayY), new Vector2(LaneW, TrayH - 30));
            s.Col = col;

            // Клик по дорожке — для наёмников
            var hit = LGBuild.Panel(col, "Hit", new Color(0, 0, 0, 0), raycast: true);
            hit.rectTransform.Stretch();
            int idx = i;
            hit.gameObject.AddComponent<SectorClick>().OnClick = () => OnSectorClicked(idx);

            // Подсветка дорожки при перетаскивании
            s.HighlightFill = LGBuild.Panel(col, "HighlightFill", new Color(1, 1, 1, 0));
            s.HighlightFill.sprite = CardSprites.Round;
            s.HighlightFill.type = Image.Type.Sliced;
            s.HighlightFill.raycastTarget = false;
            s.HighlightFill.rectTransform.Stretch(4, 4, 4, 4);
            s.Highlight = CardSkin.Sliced(col, "Highlight", "glow_edge", new Color(1, 1, 1, 0), 0.35f);
            s.Highlight.rectTransform.Stretch(-64 * 0.35f + 4, -64 * 0.35f + 4, -64 * 0.35f + 4, -64 * 0.35f + 4);

            // Места под карты — стеклянные гнёзда с еле заметной кромкой
            for (int r = 0; r < 2; r++)
                for (int k = 0; k < 3; k++)
                {
                    var sl = LGBuild.Panel(col, "Slot", new Color(0.02f, 0.06f, 0.08f, 0.35f));
                    sl.raycastTarget = false;
                    sl.rectTransform.At(Mid, Mid, new Vector2((k - 1) * 138f, r == 0 ? RowOff : -RowOff), BoardCard);
                    LG.Chip(sl.gameObject, new Color(RimCyan.r, RimCyan.g, RimCyan.b, 0.14f), 12f);
                }

            // Ряды: враг сверху, вы снизу
            _rows[i, 0] = At(col, "RowEnemy", new Vector2(0, RowOff), new Vector2(LaneW - 10, BoardCard.y + 10));
            _rows[i, 1] = At(col, "RowMine", new Vector2(0, -RowOff), new Vector2(LaneW - 10, BoardCard.y + 10));

            // Плашка сектора: мягкое свечение цвета контроля, стеклянная табличка с артом сектора внутри
            s.Wash = CardSkin.Sliced(col, "Wash", "glow_edge", new Color(1, 1, 1, 0), 0.4f);
            s.Wash.rectTransform.At(Mid, Mid, Vector2.zero, new Vector2(PlaqueW + 52, PlaqueH + 52));

            var plate = GlassAt(col, "Plaque", Vector2.zero, new Vector2(PlaqueW, PlaqueH), PanelTint, RimCyan, false, 16f);
            s.Plate = plate;
            s.PlateFx = plate.GetComponent<LiquidGlassEffect>();
            var am = LG.RoundedMask(plate.rectTransform, 14f, 3f);
            var art = Raw(am, "Art", CardArt.Sector(type), new Color(0.62f, 0.66f, 0.7f, 0.9f));
            art.rectTransform.Stretch();
            Cover(art, (PlaqueW - 6) / (PlaqueH - 6));
            // Затемнение слева направо: текст читается, справа видна картинка
            var dim = LGBuild.Panel(am, "Dim", new Color(0.01f, 0.025f, 0.04f, 0.62f));
            dim.raycastTarget = false;
            dim.rectTransform.Stretch();
            s.Header = s.PowerBar = plate.rectTransform;

            var ic = LGIcons.Create(plate.rectTransform, CardArt.SectorIcon(type), 18, Cyan);
            ic.rectTransform.At(new Vector2(0, 0.5f), Mid, new Vector2(28, 26), new Vector2(18, 18));
            s.Name = Caption(plate.rectTransform, CardArt.SectorName(type).ToUpper(), 17, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, 1.6f);
            s.Name.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(44, 26), new Vector2(260, 26));
            var acc = LGBuild.Panel(plate.rectTransform, "Accent", new Color(Gold.r, Gold.g, Gold.b, 0.85f));
            acc.raycastTarget = false;
            acc.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 10), new Vector2(36, 2));
            s.Rule = LGBuild.Label(plate.rectTransform, CardArt.SectorRule(type), 12, Muted, TextAnchor.UpperLeft, wrap: true);
            s.Rule.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 1f), new Vector2(20, 2), new Vector2(PlaqueW - 150, 44));
            s.Rule.raycastTarget = false;

            // Гнёзда силы: враг сверху (красное), вы снизу (бирюзовое) — круглые стеклянные чипы с неоновой кромкой
            s.OrbGlowE = OrbGlow(col, new Vector2(OrbX, OrbY), CardSkin.Red);
            s.OrbGlowP = OrbGlow(col, new Vector2(OrbX, -OrbY), CardSkin.Teal);
            s.PowE = Orb(col, CardSkin.Red, new Vector2(OrbX, OrbY), 64f, 28);
            s.PowP = Orb(col, CardSkin.Teal, new Vector2(OrbX, -OrbY), 64f, 28);
            TooltipHelper.Attach(s.PowE.transform.parent.gameObject, Loc.T("Сила врага в секторе"));
            TooltipHelper.Attach(s.PowP.transform.parent.gameObject, Loc.T("Ваша сила в секторе"));

            // Штамп «ВЗЯТ» / «ПОТЕРЯН»
            s.Stamp = Stamp(col, 50);
            s.Stamp.rectTransform.At(Mid, Mid, Vector2.zero, new Vector2(LaneW, 90));
            s.Stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, 7f);

            // Купол щита над рядом защитника в Штабе: соты в полусфере
            if (type == SectorType.HQ)
            {
                int defRow = _P == BattleState.Defender ? 1 : 0;
                // Основание купола — у плашки, свод — над картами защитника (полупрозрачный, поверх них)
                s.Dome = CardSkin.Simple(col, "Dome", "dome", new Color(0.6f, 0.85f, 1f, 0f));
                float baseY = defRow == 0 ? RowOff - BoardCard.y * 0.5f - 8f : -RowOff + BoardCard.y * 0.5f + 8f;
                const float domeH = 192f;
                s.Dome.rectTransform.At(Mid, new Vector2(0.5f, 0f), new Vector2(0, baseY), new Vector2(LaneW - 6, domeH));
                if (defRow == 1) s.Dome.rectTransform.localRotation = Quaternion.Euler(0, 0, 180f);

                // Табличка на своде купола: что это и почему держится
                float apex = baseY + (defRow == 0 ? domeH - 2f : -domeH + 2f);
                s.DomeBadge = ChipAt(col, "DomeBadge", new Vector2(0, apex), new Vector2(172, 28), SubTint, new Color(0.55f, 0.85f, 1f, 0.6f), raycast: true);
                var bic = LGIcons.Create(s.DomeBadge.rectTransform, LGIcon.ShieldDome, 13, new Color(0.6f, 0.88f, 1f));
                bic.rectTransform.At(new Vector2(0, 0.5f), Mid, new Vector2(18, 0), new Vector2(13, 13));
                s.DomeText = Caption(s.DomeBadge.rectTransform, Loc.T("ЩИТ ШТАБА"), 11, new Color(0.7f, 0.92f, 1f), TextAnchor.MiddleCenter, 1.4f);
                s.DomeText.rectTransform.Stretch(26, 0, 8, 0);
                TooltipHelper.Attach(s.DomeBadge.gameObject, Loc.T("<b>Щит Штаба</b>\nЭнергетический купол над защитниками Штаба. Пока он стоит, прорыв в Штаб невозможен.\nКупол держится, пока электростанция у защитника. Снять его: захватить сектор «Электростанция» или применить «Диверсию» / ЭМИ."));
            }
        }

        private Image OrbGlow(RectTransform parent, Vector2 pos, Color c)
        {
            var g = CardSkin.Simple(parent, "OrbGlow", "glow", new Color(c.r, c.g, c.b, 0f));
            g.rectTransform.At(Mid, Mid, pos, new Vector2(140, 140));
            return g;
        }

        /// <summary>Круглый стеклянный чип с числом (сила в секторе, карты в колоде).</summary>
        private Text Orb(RectTransform parent, Color rim, Vector2 pos, float size, int font)
        {
            var o = ChipAt(parent, "Orb", pos, new Vector2(size, size), SubTint, new Color(rim.r, rim.g, rim.b, 0.75f), raycast: true);
            var t = Caption(o.rectTransform, "0", font, Color.white, TextAnchor.MiddleCenter, 0f);
            t.rectTransform.Stretch(0, 0, 0, -1);
            var ol = t.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(rim.r * 0.3f, rim.g * 0.3f, rim.b * 0.3f, 0.8f);
            ol.effectDistance = new Vector2(1.2f, -1.2f);
            return t;
        }

        /// <summary>Портреты в стеклянных кольцах цвета фракции: враг над столом (его рука рубашками рядом), вы — слева от руки.</summary>
        private void BuildPortraits()
        {
            var a = _setup.Attacker; var d = _setup.Defender;
            var me = _P == BattleState.Attacker ? a : d;
            var en = _P == BattleState.Attacker ? d : a;
            Portrait(en, _E, new Vector2(0, 455), true);
            Portrait(me, _P, new Vector2(-640, -430), false);

            _enemyHandRow = At(_board, "EnemyHand", new Vector2(205, 470), new Vector2(260, 100));
        }

        private void Portrait(BSide side, int idx, Vector2 pos, bool enemy)
        {
            var fc = CardArt.Faction(side.Faction);
            const float S = 120f;
            var glow = CardSkin.Simple(_board, "PortraitGlow", "glow", new Color(fc.r, fc.g, fc.b, 0.4f));
            glow.rectTransform.At(Mid, Mid, pos, new Vector2(220, 220));
            ChipAt(_board, "PortraitRing", pos, new Vector2(S + 14, S + 14), PanelTint, new Color(fc.r, fc.g, fc.b, 0.85f));
            var disc = CardSkin.Simple(_board, "Portrait", "disc", Color.white);
            disc.rectTransform.At(Mid, Mid, pos, new Vector2(S, S));
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var p = Raw(disc.rectTransform, "Leader", CardArt.Leader(side.Faction), Color.white);
            p.rectTransform.Stretch();
            if (p.texture != null)
            {
                float ta = p.texture.width / (float)p.texture.height;
                p.uvRect = ta > 1 ? new Rect((1 - 1 / ta) * 0.5f, 0, 1 / ta, 1) : new Rect(0, 1 - ta - (1 - ta) * 0.12f, 1, ta);
            }

            // Имя — стеклянная капсула под портретом (у врага) или над ним (у вас); роль — подпись цветом фракции
            var rpos = pos + new Vector2(0, enemy ? -78 : 80);
            var rb = ChipAt(_board, "NameChip", rpos, new Vector2(250, 30), PanelTint, new Color(fc.r, fc.g, fc.b, 0.55f));
            var nm = Caption(rb.rectTransform, (side.Name ?? side.Faction ?? "").ToUpper(), 13, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, 1.2f);
            nm.rectTransform.Stretch(16, 0, 16, 0);
            nm.horizontalOverflow = HorizontalWrapMode.Wrap;
            nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 8; nm.resizeTextMaxSize = 13;
            var role = Caption(_board, idx == BattleState.Attacker ? Loc.T("ДЕСАНТ") : Loc.T("ГАРНИЗОН"), 10, new Color(fc.r, fc.g, fc.b, 1f), TextAnchor.MiddleCenter, 2.4f);
            role.rectTransform.At(Mid, Mid, rpos + new Vector2(0, enemy ? -24 : 24), new Vector2(200, 16));
        }

        /// <summary>
        /// Правая колонка — пульт боя в стеклянной панели игры, сверху вниз:
        ///   • номер хода в круглом чипе с кольцом прогресса (заполняется к 6-му ходу; на последнем — красная надпись);
        ///   • большая кнопка «Закончить ход» (бирюзовая — ещё есть что сыграть, зелёная с пульсом — всё сыграно);
        ///   • счёт и три мини-индикатора секторов: значок, «ваша сила : сила врага», кромка — кто держит;
        ///   • погода; очки приказов — крупная цифра и кристаллы; кнопки скорости, автобоя и отступления.
        /// </summary>
        private void BuildRight()
        {
            bool merc = _state.Sides[_P].Faction == "aquila";
            const float W = 196f, Inner = 180f;
            float bottom = merc ? -330f : -264f, top = 372f;
            var console = GlassAt(_board, "Console", new Vector2(RX, (top + bottom) * 0.5f), new Vector2(W, top - bottom), PanelTint, RimCyan, false, 18f);
            PanelHeader(console.rectTransform, _P == BattleState.Attacker ? Loc.T("ШТУРМ") : Loc.T("ОБОРОНА"));

            // Ход: кольцо прогресса вокруг чипа с номером
            var ringBack = CardSkin.Simple(_board, "TurnRingBack", "ring", new Color(Cyan.r, Cyan.g, Cyan.b, 0.12f));
            ringBack.sprite = CardSprites.Ring;
            ringBack.rectTransform.At(Mid, Mid, new Vector2(RX, 286), new Vector2(104, 104));
            _turnRing = CardSkin.Simple(_board, "TurnRing", "ring", new Color(Cyan.r, Cyan.g, Cyan.b, 0.95f));
            _turnRing.sprite = CardSprites.Ring;
            _turnRing.type = Image.Type.Filled;
            _turnRing.fillMethod = Image.FillMethod.Radial360;
            _turnRing.fillOrigin = (int)Image.Origin360.Top;
            _turnRing.fillClockwise = true;
            _turnRing.fillAmount = 0f;
            _turnRing.rectTransform.At(Mid, Mid, new Vector2(RX, 286), new Vector2(104, 104));
            var turnChip = ChipAt(_board, "TurnChip", new Vector2(RX, 286), new Vector2(80, 80), SubTint, new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f));
            _turnText = Caption(turnChip.rectTransform, "1", 38, Color.white, TextAnchor.MiddleCenter, 0f);
            _turnText.rectTransform.Stretch(0, 0, 0, -2);
            _turnCap = Caption(_board, Loc.F("ХОД ИЗ {0}", BattleState.Turns), 11, Gold, TextAnchor.MiddleCenter, 1.4f);
            _turnCap.rectTransform.At(Mid, Mid, new Vector2(RX, 222), new Vector2(Inner, 18));

            // Кнопка хода: мягкий ореол цвета состояния и стеклянная кнопка игры
            _endGlow = CardSkin.Simple(_board, "EndGlow", "glow", new Color(0.3f, 1f, 0.6f, 0f));
            _endGlow.rectTransform.At(Mid, Mid, new Vector2(RX, 166), new Vector2(280, 150));
            _endButton = LGBuild.Button(_board, "EndTurn", UIManager.DS.BtnPrimary, new Color(Cyan.r, Cyan.g, Cyan.b, 0.8f), OnEndTurn, null, Loc.T("ЗАКОНЧИТЬ\nХОД"), 17, 16f);
            _endButtonImg = _endButton.GetComponent<Image>();
            _endButtonImg.rectTransform.At(Mid, Mid, new Vector2(RX, 166), new Vector2(Inner, 74));
            _endFx = _endButton.GetComponent<LiquidGlassEffect>();
            var eit = _endButton.GetComponent<LGInteractive>();
            if (eit != null) eit.animateScale = false;   // своя «пружина» при нажатии
            UiSound.Mark(_endButton, UiPress.Confirm);
            _endLabel = _endButton.GetComponentInChildren<Text>();
            _endLabel.lineSpacing = 0.9f;
            _endLabel.Track(1.6f);
            TooltipHelper.Attach(_endButton.gameObject, Loc.T("Закончить ход  <color=#8AA2A8>(Enter)</color>\nЗелёная и пульсирует — всё, что можно, сыграно"));

            // Счёт и мини-индикаторы секторов
            _scoreText = Caption(_board, "", 14, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, 0.8f);
            _scoreText.rectTransform.At(Mid, Mid, new Vector2(RX, 106), new Vector2(Inner, 40));
            _scoreText.supportRichText = true;
            for (int i = 0; i < 3; i++)
            {
                var s = _sec[i];
                var type = _state.Sectors[i].Type;
                s.Chip = GlassAt(_board, "SectorChip", new Vector2(RX + (i - 1) * 61f, 58), new Vector2(56, 46), SubTint, RimSoft, true, 12f, raycast: true);
                s.ChipFx = s.Chip.GetComponent<LiquidGlassEffect>();
                var sic = LGIcons.Create(s.Chip.rectTransform, CardArt.SectorIcon(type), 13, Muted);
                sic.rectTransform.At(new Vector2(0.5f, 1f), Mid, new Vector2(0, -12), new Vector2(13, 13));
                s.ChipText = Caption(s.Chip.rectTransform, "0:0", 13, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, 0f);
                s.ChipText.rectTransform.At(new Vector2(0.5f, 0f), Mid, new Vector2(0, 13), new Vector2(56, 18));
                s.ChipText.supportRichText = true;
                TooltipHelper.Attach(s.Chip.gameObject, CardArt.SectorName(type) + "\n" + CardArt.SectorRule(type));
            }

            // Погода — подложка со значком
            var wp = GlassAt(_board, "Weather", new Vector2(RX, -10), new Vector2(Inner, 66), SubTint, RimSoft, true, 12f);
            _weatherIcon = LGIcons.Create(wp.rectTransform, LGIcon.Globe, 13, new Color(0.65f, 0.82f, 1f));
            _weatherIcon.rectTransform.At(new Vector2(0, 1f), Mid, new Vector2(16, -15), new Vector2(13, 13));
            _weatherTitle = Caption(wp.rectTransform, Loc.T("ПОГОДА"), 11, Gold, TextAnchor.MiddleLeft, 1.4f);
            _weatherTitle.rectTransform.TopBand(6, 18, 28, 8);
            _weatherTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            _weatherTitle.resizeTextForBestFit = true; _weatherTitle.resizeTextMinSize = 8; _weatherTitle.resizeTextMaxSize = 11;
            _weatherText = LGBuild.Label(wp.rectTransform, "", 11, Muted, TextAnchor.MiddleCenter, wrap: true);
            _weatherText.rectTransform.Stretch(8, 4, 8, 24);
            _weatherText.resizeTextForBestFit = true; _weatherText.resizeTextMinSize = 8; _weatherText.resizeTextMaxSize = 11;
            _weatherText.raycastTarget = false;

            // Очки приказов — на виду: крупная цифра и кристаллы в два ряда
            var pp = GlassAt(_board, "Points", new Vector2(RX, -104), new Vector2(Inner, 100), SubTint, new Color(0.45f, 0.75f, 1f, 0.35f), true, 12f, raycast: true);
            var pcap = Caption(pp.rectTransform, Loc.T("ОЧКИ ПРИКАЗОВ"), 11, Gold, TextAnchor.UpperCenter, 1.4f);
            pcap.rectTransform.TopBand(8, 16, 6, 6);
            _pointsGlow = CardSkin.Simple(pp.rectTransform, "PointsGlow", "glow", new Color(0.45f, 0.75f, 1f, 0f));
            _pointsGlow.rectTransform.At(new Vector2(0, 0.5f), Mid, new Vector2(36, -10), new Vector2(110, 90));
            _pointsText = Caption(pp.rectTransform, "0/0", 28, Color.white, TextAnchor.MiddleCenter, 0f);
            _pointsText.rectTransform.At(new Vector2(0, 0.5f), Mid, new Vector2(36, -10), new Vector2(70, 40));
            var pol = _pointsText.gameObject.AddComponent<Outline>();
            pol.effectColor = new Color(0.05f, 0.15f, 0.35f, 0.9f);
            pol.effectDistance = new Vector2(1.5f, -1.5f);
            for (int k = 0; k < 10; k++)
            {
                var c = CardSkin.Simple(pp.rectTransform, "Crystal", "crystal_empty", Color.white);
                c.rectTransform.At(new Vector2(0, 0.5f), Mid, new Vector2(84 + (k % 5) * 21f, k < 5 ? 2 : -21), new Vector2(22, 22));
                _crystals.Add(c);
            }
            TooltipHelper.Attach(pp.gameObject, Loc.T("Очки приказов: столько стоят карты. Каждый ход их становится больше"));

            // Кнопки управления боем
            _speedLabel = SquareButton(new Vector2(RX - 60, -196), null, SpeedText(), Loc.T("СКОРОСТЬ"), OnSpeed, Loc.T("Скорость анимаций боя"), out _);
            SquareButton(new Vector2(RX, -196), LGIcon.Fastest, null, Loc.T("ДОСЧИТАТЬ"), OnAuto, Loc.T("Доиграть бой за обе стороны мгновенно (вашей стороной командует ИИ)"), out _autoButton);
            SquareButton(new Vector2(RX + 60, -196), LGIcon.Back, null, _P == BattleState.Attacker ? Loc.T("ОТСТУПИТЬ") : Loc.T("СДАТЬСЯ"), OnRetreat,
                _P == BattleState.Attacker ? Loc.T("Отступить: десант возвращается на корабли, погибшие потеряны") : Loc.T("Сдать колонию"), out _retreatButton, UIManager.DS.Red);

            // Наёмники Аквилы — купить прямо в бою
            if (merc)
            {
                _mercButton = LGBuild.Button(_board, "Merc", UIManager.DS.BtnNeutral, new Color(Gold.r, Gold.g, Gold.b, 0.6f), OnMerc, LGIcon.Trade, Loc.T("НАЁМНИКИ · 60"), 12, 14f);
                _mercButton.GetComponent<Image>().rectTransform.At(Mid, Mid, new Vector2(RX, -292), new Vector2(Inner, 42));
                TooltipHelper.Attach(_mercButton.gameObject, Loc.T("<b>Наёмники «Сокол»</b> 4/4 за 60 гелия-3 из казны — без очков приказов. Затем щёлкните по сектору"));
            }
        }

        private LiquidGlassEffect _endFx;
        private Image _turnRing, _pointsGlow, _weatherIcon;
        private Text _turnCap;
        private int _pointsShown = -1;

        /// <summary>Квадратная кнопка пульта — стеклянная кнопка игры с иконкой (или текстом) и подписью под ней.</summary>
        private Text SquareButton(Vector2 pos, LGIcon? icon, string label, string caption, Action act, string tip, out Button btn, Color? accent = null)
        {
            var img = LGBuild.Panel(_board, "CmdBtn", UIManager.DS.BtnNeutral, raycast: true);
            img.rectTransform.At(Mid, Mid, pos, new Vector2(56, 56));
            btn = img.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => act());
            var ac = accent ?? Cyan;
            LG.Button(img.gameObject, new Color(ac.r, ac.g, ac.b, accent.HasValue ? 0.55f : 0.3f), 14f);
            Text t = null;
            if (icon.HasValue)
            {
                var ic = LGIcons.Create(img.rectTransform, icon.Value, 22, accent ?? UIManager.DS.TextPrimary);
                ic.rectTransform.At(Mid, Mid, Vector2.zero, new Vector2(22, 22));
            }
            else
            {
                t = Caption(img.rectTransform, label ?? "", 16, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, 0f);
                t.rectTransform.Stretch();
            }
            if (!string.IsNullOrEmpty(caption))
            {
                var cap = Caption(_board, caption, 9, Muted, TextAnchor.MiddleCenter, 1f);
                cap.rectTransform.At(Mid, Mid, pos + new Vector2(0, -40), new Vector2(76, 14));
            }
            if (!string.IsNullOrEmpty(tip)) TooltipHelper.Attach(img.gameObject, tip);
            return t;
        }

        /// <summary>Левая колонка: колоды врага и ваша, между ними хроника боя — стеклянная панель, сворачивается щелчком по заголовку.</summary>
        private void BuildLeft()
        {
            _deckE = Deck(new Vector2(LX, 300), FactionOf(_E));
            _deckP = Deck(new Vector2(LX, -168), FactionOf(_P));

            var hp = GlassAt(_board, "Log", Vector2.zero, new Vector2(LogW, LogH), PanelTint, RimCyan, false, 18f, raycast: true);
            hp.rectTransform.At(Mid, new Vector2(0.5f, 1f), new Vector2(LX, LogTop), new Vector2(LogW, LogH));
            _logPanel = hp.rectTransform;
            PanelHeader(hp.rectTransform, Loc.T("ХРОНИКА БОЯ"));

            // Заголовок — кнопка: стрелка показывает, куда сложится панель; точка — новые записи в свёрнутой хронике
            var hit = LGBuild.Panel(hp.rectTransform, "Toggle", new Color(0, 0, 0, 0), raycast: true);
            hit.rectTransform.TopBand(0, LogMin, 0, 0);
            var tb = hit.gameObject.AddComponent<Button>();
            tb.transition = Selectable.Transition.None;
            tb.onClick.AddListener(ToggleLog);
            TooltipHelper.Attach(hit.gameObject, Loc.T("Свернуть / развернуть хронику"));
            _logChevron = LGIcons.Create(hp.rectTransform, LGIcon.Back, 11, Muted);
            _logChevron.rectTransform.At(new Vector2(1f, 1f), Mid, new Vector2(-16, -19), new Vector2(11, 11));
            _logDot = LGBuild.Panel(hp.rectTransform, "NewDot", new Color(Cyan.r, Cyan.g, Cyan.b, 0f));
            _logDot.sprite = CardSprites.Dot;
            _logDot.raycastTarget = false;
            _logDot.rectTransform.At(new Vector2(0f, 1f), Mid, new Vector2(16, -19), new Vector2(9, 9));

            // Текст — в обрезающем «теле» под заголовком: при сворачивании он прячется вместе с панелью
            var body = LGBuild.Rect(hp.rectTransform, "Body");
            body.Stretch(0, 0, 0, LogMin + 2);
            body.gameObject.AddComponent<RectMask2D>();
            _logText = LGBuild.Label(body, "", 12, Muted, TextAnchor.UpperLeft, wrap: true);
            _logText.rectTransform.Stretch(14, 12, 12, 4);
            _logText.verticalOverflow = VerticalWrapMode.Truncate;
            _logText.raycastTarget = false;
            _logText.supportRichText = true;

            _logCollapsed = PlayerPrefs.GetInt("cards.logCollapsed", 0) == 1;
            _logH = _logCollapsed ? LogMin : LogH;
            _logPanel.sizeDelta = new Vector2(LogW, _logH);
            _logChevron.rectTransform.localRotation = Quaternion.Euler(0, 0, _logCollapsed ? 90f : -90f);
            _logSeen = _logTotal;
        }

        private const float LogW = 196f, LogH = 258f, LogMin = 40f, LogTop = 195f;
        private RectTransform _logPanel;
        private Image _logChevron, _logDot;
        private bool _logCollapsed;
        private float _logH;
        private int _logTotal, _logSeen;

        private void ToggleLog()
        {
            _logCollapsed = !_logCollapsed;
            PlayerPrefs.SetInt("cards.logCollapsed", _logCollapsed ? 1 : 0);
            if (!_logCollapsed) _logSeen = _logTotal;
            SFXManager.Play(Sfx.PanelSlide, 0.7f);
        }

        /// <summary>Сворачивание хроники: высота плавно к цели, стрелка поворачивается, точка новых записей мигает.</summary>
        private void LogTick(float dt)
        {
            if (_logPanel == null) return;
            float want = _logCollapsed ? LogMin : LogH;
            _logH = Mathf.Lerp(_logH, want, 1f - Mathf.Exp(-dt * 14f));
            if (Mathf.Abs(_logH - want) < 0.5f) _logH = want;
            _logPanel.sizeDelta = new Vector2(LogW, _logH);
            var rot = Quaternion.Euler(0, 0, _logCollapsed ? 90f : -90f);
            _logChevron.rectTransform.localRotation = Quaternion.Slerp(_logChevron.rectTransform.localRotation, rot, 1f - Mathf.Exp(-dt * 14f));
            if (!_logCollapsed) _logSeen = _logTotal;
            float da = _logCollapsed && _logTotal != _logSeen ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;
            _logDot.color = new Color(Cyan.r, Cyan.g, Cyan.b, da);
        }

        private Text Deck(Vector2 pos, string faction)
        {
            for (int k = 0; k < 3; k++)
            {
                var b = CardView.Create(_board, new BCard(CardCatalog.Crew ?? CardCatalog.Militia), 0, faction, new Vector2(92, 128), true);
                b.SetFace(false, true);
                b.Rt.anchoredPosition = pos + new Vector2(-4 + k * 4, k * 3);
                foreach (var g in b.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            }
            var t = Orb(_board, Cyan, pos + new Vector2(50, -54), 44f, 17);
            TooltipHelper.Attach(t.transform.parent.gameObject, Loc.T("Карт в колоде"));
            return t;
        }

        private string SpeedText() => Loc.F("×{0:0.#}", _animSpeed);

        private void OnSpeed()
        {
            _animSpeed = _animSpeed >= 2f ? 1f : _animSpeed + 0.5f;
            PlayerPrefs.SetFloat("cards.speed", _animSpeed);
            if (_fx != null) _fx.Speed = _animSpeed;
            if (_particles != null) _particles.Speed = _animSpeed;
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
            v.SetHoverable(true);
            v.HoverLift = 6f;
            v.HoverScale = 0.06f;
        }

        /// <summary>Крупная карта рядом с наведённой картой стола (с той стороны, где больше места).</summary>
        private void ShowPreview(CardView v)
        {
            if (v == null || v.Card == null || _dragging != null) return;
            if (!v.FaceUp && v.Side != _P) return;   // рубашку врага не подсмотреть
            if (_preview != null) Destroy(_preview.gameObject);
            _preview = CardView.Create(_previewRoot, v.Card, v.Side, v.SideFaction, PreviewCard, false);
            _preview.Rt.anchoredPosition = Vector2.zero;
            if (v.Uid >= 0)
            {
                var u = _state.FindUnit(v.Uid);
                if (u != null) _preview.RefreshNumbers(_state.Power(u), u.Armor, u.MaxArmor, false);
            }
            var croot = (RectTransform)_canvas.transform;
            var screen = RectTransformUtility.WorldToScreenPoint(null, v.Rt.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(croot, screen, null, out var lp);
            float side = lp.x < 300f ? 1f : -1f;
            var pos = new Vector2(lp.x + side * (PreviewCard.x * 0.5f + v.Rt.rect.width * 0.5f + 24f), Mathf.Clamp(lp.y, -300f, 300f));
            _previewRoot.anchoredPosition = pos;
            _previewRoot.SetAsLastSibling();
            _preview.Punch(0.05f);
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
            v.HoverLift = 150f;
            v.HoverScale = 0.28f;
            v.OnHoverIn = x => { if (_dragging != null) return; _handHover = x; x.Rt.SetAsLastSibling(); LayoutHand(false); SFXManager.Play(Sfx.CardHover, 1f, UnityEngine.Random.Range(0.95f, 1.1f)); };
            v.OnHoverOut = x => { if (_handHover == x) _handHover = null; LayoutHand(false); };
            v.OnDragBegin = BeginDrag;
            v.OnDragMove = Drag;
            v.OnDragEnd = EndDrag;
            if (animateFromDeck)
            {
                // Из колоды слева — переворотом в руку
                var deckPos = _deckP != null ? (Vector2)_handRoot.InverseTransformPoint(_deckP.transform.position) : new Vector2(-700, 100);
                v.Rt.anchoredPosition = deckPos;
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
            float spread = Mathf.Min(150f, n > 1 ? 900f / (n - 1) : 0f);
            int hi = _handHover != null ? _hand.IndexOf(_handHover) : -1;
            for (int i = 0; i < n; i++)
            {
                float t = n > 1 ? i / (float)(n - 1) - 0.5f : 0f;
                var pos = new Vector2(t * spread * (n - 1), -Mathf.Abs(t) * Mathf.Abs(t) * 70f * (n > 4 ? 1f : 0.5f));
                float rot = -t * (n > 1 ? 12f : 0f);
                if (hi >= 0 && i != hi) pos.x += (i < hi ? -1f : 1f) * 46f;   // соседи расступаются
                if (i == hi) rot = 0f;
                _targets[_hand[i]] = pos;
                _targetRot[_hand[i]] = rot;
                if (i != hi) _hand[i].Rt.SetSiblingIndex(i);
                if (instant) { _hand[i].Rt.anchoredPosition = pos; _hand[i].Rt.localRotation = Quaternion.Euler(0, 0, rot); }
            }
            if (hi >= 0) _hand[hi].Rt.SetAsLastSibling();
        }

        private void BeginDrag(CardView v, PointerEventData e)
        {
            if (_busy || _state.Over) return;
            _dragging = v;
            _handHover = null;
            v.SetHoverable(false);
            v.Rt.SetParent(_dragLayer, true);
            v.Rt.SetAsLastSibling();
            _targets.Remove(v);
            HidePreview(null);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_dragLayer, e.position, null, out var lp);
            _dragOffset = (Vector2)v.Rt.localPosition - lp;
            v.Rt.localScale = Vector3.one * 0.8f;
            LayoutHand(false);
            SFXManager.Play(Sfx.CardPick);
        }

        private void Drag(CardView v, PointerEventData e)
        {
            if (_dragging != v) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_dragLayer, e.position, null, out var lp);
            var target = lp + _dragOffset * 0.3f;
            v.Rt.localPosition = Vector3.Lerp(v.Rt.localPosition, target, 0.6f);
            float vx = e.delta.x;
            v.Rt.localRotation = Quaternion.Euler(0, 0, Mathf.Clamp(-vx * 0.6f, -18f, 18f));
        }

        private void EndDrag(CardView v, PointerEventData e)
        {
            if (_dragging != v) return;
            _dragging = null;
            v.SetHoverable(true);
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
                pv.SetAlpha(0.85f);
                pv.SetStatus(Loc.T("ПРИКАЗ ОТДАН · клик — отменить"), new Color(0.5f, 0.85f, 1f));
                Hook(pv);
                pv.OnClick = UnplayView;
                _pending[v.Card] = pv;
                pv.Punch(0.25f);
                var p = _fx.Local(pv.Rt);
                _fx.Dust(p + new Vector2(0, -60), 0.7f);
                _fx.Ring(p, new Color(0.5f, 0.85f, 1f, 0.7f), 40f, 220f, 0.35f);
                SFXManager.Play(Sfx.CardPlace);
                LayoutAll(false);
                LayoutHand(false);
                RefreshPanels();
                return;
            }
            // Не вышло — карта возвращается в руку
            v.Rt.SetParent(_handRoot, true);
            v.Rt.localScale = Vector3.one;
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
            SFXManager.Play(Sfx.CardPick, 0.8f, 0.9f);
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
                    float w = LaneW - 24f;
                    float step = n > 1 ? Mathf.Min(BoardCard.x + 10f, (w - BoardCard.x) / (n - 1)) : 0f;
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
            if (!IsOpen || _state == null) return;
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
            bool anyPlayable = false;
            if (!_busy && !_state.Over)
                foreach (var v in _hand)
                {
                    v.Playable = _state.CostOf(_P, v.Card) <= _state.Sides[_P].Points && v.Card.Def.Kind != CardKind.Structure;
                    anyPlayable |= v.Playable;
                }
            else foreach (var v in _hand) v.Playable = false;

            // Кольцо хода плавно дотягивается до текущего хода; на последнем ходу надпись пульсирует
            if (_turnRing != null)
            {
                float want = Mathf.Clamp01(Mathf.Clamp(_state.Turn, 1, BattleState.Turns) / (float)BattleState.Turns);
                _turnRing.fillAmount = Mathf.MoveTowards(_turnRing.fillAmount, want, dt * 0.6f);
                bool last = _state.Turn >= BattleState.Turns;
                var rc = last ? UIManager.DS.Red : Cyan;
                _turnRing.color = new Color(rc.r, rc.g, rc.b, 0.75f + 0.2f * Mathf.Sin(Time.unscaledTime * (last ? 5f : 1.5f)));
                if (last && _turnCap != null) _turnCap.color = new Color(_turnCap.color.r, _turnCap.color.g, _turnCap.color.b, 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f));
            }
            if (_pointsGlow != null)
            {
                float pg = _state.Sides[_P].Points > 0 ? 0.25f : 0f;
                _pointsGlow.color = new Color(0.45f, 0.75f, 1f, Mathf.MoveTowards(_pointsGlow.color.a, pg, dt * 0.8f));
            }

            // Кнопка хода: бирюзовая — ещё есть что сыграть, зелёная с пульсом — всё сыграно, приглушённая — идёт бой
            if (_endButtonImg != null)
            {
                bool idle = !_busy && !_state.Over;
                string key = !idle ? "busy" : anyPlayable ? "play" : "done";
                if (_state.Over) key = "over";
                if (key != _endState)
                {
                    _endState = key;
                    _endLabel.text = _state.Over ? Loc.T("БОЙ\nОКОНЧЕН") : _busy ? Loc.T("БОЙ\nИДЁТ…") : Loc.T("ЗАКОНЧИТЬ\nХОД");
                    _endLabel.color = idle ? UIManager.DS.TextPrimary : Muted;
                    var rim = !idle ? new Color(1f, 1f, 1f, 0.16f) : anyPlayable ? new Color(Cyan.r, Cyan.g, Cyan.b, 0.8f) : new Color(UIManager.DS.Green.r, UIManager.DS.Green.g, UIManager.DS.Green.b, 0.95f);
                    if (_endFx != null) _endFx.SetRim(rim);
                }
                var tint = !idle ? UIManager.DS.BtnDisabled : anyPlayable ? UIManager.DS.BtnPrimary : UIManager.DS.BtnSuccess;
                _endButtonImg.color = Color.Lerp(_endButtonImg.color, tint, 1f - Mathf.Exp(-dt * 8f));
                float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.6f);
                if (_endFx != null) _endFx.SetPulse(idle && !anyPlayable ? wave : 0f);
                float halo = idle && !anyPlayable ? 0.3f + 0.3f * wave : idle ? 0.14f : 0f;
                var gc = anyPlayable ? Cyan : UIManager.DS.Green;
                _endGlow.color = new Color(gc.r, gc.g, gc.b, Mathf.Lerp(_endGlow.color.a, halo, 1f - Mathf.Exp(-dt * 8f)));
                _endButtonImg.rectTransform.localScale = Vector3.Lerp(_endButtonImg.rectTransform.localScale, Vector3.one, 1f - Mathf.Exp(-dt * 12f));
            }

            // Атмосфера: пыль/пепел/дождь
            AmbientTick(dt);
            LogTick(dt);

            // Дорожки. Пока карта в руке над столом, подсветка держится каждый кадр (даже если мышь стоит):
            // все дорожки, куда карту можно положить, — мягко зелёным, дорожка под картой — ярко и с пульсом
            // (зелёная — можно, красная — нельзя). Без перетаскивания подсветка гаснет; свечение плашки — к цвету контроля.
            int under = _dragging != null ? SectorUnder(Input.mousePosition) : -1;
            for (int si = 0; si < _sec.Length; si++)
            {
                var s = _sec[si];
                if (s == null) continue;
                if (_dragging != null)
                {
                    bool ok = _state.CanPlay(_P, _dragging.Card, si) == null;
                    s.HighT = si == under ? 1f : ok ? 0.32f : 0f;
                    if (si == under || ok) s.HighC = ok ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 0.4f, 0.35f);
                }
                else s.HighT = 0f;
                s.HighA = Mathf.MoveTowards(s.HighA, s.HighT, dt * (s.HighA < s.HighT ? 7f : 2.5f));
                float hp = s.HighT >= 1f ? 0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * 7f) : 1f;
                s.Highlight.color = new Color(s.HighC.r, s.HighC.g, s.HighC.b, Mathf.Clamp01(s.HighA * 1.3f * hp));
                s.HighlightFill.color = new Color(s.HighC.r, s.HighC.g, s.HighC.b, s.HighA * 0.2f * hp);
                s.WashA = Mathf.MoveTowards(s.WashA, 0.55f, dt * 0.7f);
                s.Wash.color = new Color(s.WashC.r, s.WashC.g, s.WashC.b, Mathf.Clamp01(s.WashA * (0.9f + 0.1f * Mathf.Sin(Time.unscaledTime * 1.6f))));
                if (s.Stamp.color.a > 0f) { var sc = s.Stamp.color; sc.a = Mathf.Max(0, sc.a - dt * 0.55f); s.Stamp.color = sc; }
                if (s.Dome != null)
                {
                    bool up = _state.ShieldUp;
                    if (up != s.DomeWasUp)
                    {
                        s.DomeWasUp = up;
                        s.DomeText.text = up ? Loc.T("ЩИТ ШТАБА") : Loc.T("ЩИТ СНЯТ");
                        s.DomeText.color = up ? new Color(0.7f, 0.92f, 1f) : new Color(1f, 0.55f, 0.48f);
                    }
                    float a = up ? 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 2.2f) : 0f;
                    s.Dome.color = new Color(0.6f, 0.85f, 1f, Mathf.MoveTowards(s.Dome.color.a, a, dt * 1.5f));
                }
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
                    _fx.Burst(new Vector2(pos.x, -r.height * 0.5f), 1, CardSkin.Spr("k_spark1"), new Color(1f, 0.55f, 0.2f, 0.9f), 40f, 120f, 4f, 7f, 10f, 4f, -30f, 0.2f, 90f, 40f);
                    break;
                case Terrain.Desert:
                    _fx.Burst(new Vector2(-r.width * 0.5f - 20f, UnityEngine.Random.Range(-r.height * 0.4f, r.height * 0.4f)), 1, CardSkin.Spr("k_smoke1"), new Color(0.8f, 0.6f, 0.4f, 0.1f), 140f, 260f, 5f, 8f, 160f, 300f, 0f, 0.1f, 0f, 20f);
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
            var en = _state.Sides[_E];
            _turnText.text = Mathf.Clamp(_state.Turn, 1, BattleState.Turns).ToString();
            int mine = 0, theirs = 0;
            for (int i = 0; i < 3; i++) { if (_state.Sectors[i].Control == _P) mine++; else theirs++; }
            _scoreText.text = Loc.F("СЕКТОРЫ  <color=#7CF2D8>{0}</color> : <color=#FF8A80>{1}</color>", mine, theirs) + "\n" +
                              Loc.T("<size=11><color=#9AA6B0>нужно 2 из 3</color></size>");
            _weatherTitle.text = _state.WeatherActive ? (_state.WeatherName ?? "").ToUpper() : Loc.T("ПОГОДА");
            if (_weatherIcon != null) _weatherIcon.color = _state.WeatherActive ? new Color(1f, 0.72f, 0.4f) : new Color(0.65f, 0.82f, 1f);
            bool last = _state.Turn >= BattleState.Turns;
            if (_turnCap != null)
            {
                _turnCap.text = last ? Loc.T("ПОСЛЕДНИЙ ХОД") : Loc.F("ХОД ИЗ {0}", BattleState.Turns);
                _turnCap.color = last ? UIManager.DS.Red : Gold;
            }
            _weatherText.text = _state.WeatherActive ? _state.WeatherText : Loc.T("откроется на 3-м ходу");

            int total = Mathf.Max(me.Points, _state.Turn + me.PointsPerTurn);
            _pointsText.text = Loc.F("{0}/{1}", me.Points, total);
            if (_pointsShown >= 0 && me.Points > _pointsShown && _pointsGlow != null) _pointsGlow.color = new Color(0.45f, 0.75f, 1f, 0.9f);   // прибавились — вспышка
            _pointsShown = me.Points;
            for (int i = 0; i < _crystals.Count; i++)
            {
                _crystals[i].gameObject.SetActive(i < Mathf.Max(total, me.Points));
                var spr = i < me.Points ? "crystal_full" : "crystal_empty";
                if (_crystals[i].sprite == null || _crystals[i].sprite.name != spr) _crystals[i].sprite = CardSkin.Spr(spr);
            }
            if (_deckP != null) _deckP.text = me.Deck.Count.ToString();
            if (_deckE != null) _deckE.text = en.Deck.Count.ToString();
            RefreshEnemyHand(en.Hand.Count);
            for (int i = 0; i < 3; i++) RefreshSector(i);
            _logText.text = string.Join("\n", _log.Count > 10 ? _log.GetRange(_log.Count - 10, 10) : _log);
            if (_mercButton != null) _mercButton.interactable = !_busy && !_state.Over && me.Treasury != null && me.Treasury() >= 60f;
        }

        /// <summary>Рука врага — рубашки веером рядом с его портретом.</summary>
        private void RefreshEnemyHand(int n)
        {
            if (n == _enemyHandShown || _enemyHandRow == null) return;
            _enemyHandShown = n;
            for (int i = _enemyHandRow.childCount - 1; i >= 0; i--) Destroy(_enemyHandRow.GetChild(i).gameObject);
            for (int k = 0; k < n; k++)
            {
                var b = CardView.Create(_enemyHandRow, new BCard(CardCatalog.Crew ?? CardCatalog.Militia), _E, FactionOf(_E), new Vector2(60, 84), true);
                b.SetFace(false, true);
                float t = n > 1 ? k / (float)(n - 1) - 0.5f : 0f;
                b.Rt.anchoredPosition = new Vector2(-90 + k * Mathf.Min(34f, 200f / Mathf.Max(1, n - 1)), Mathf.Abs(t) * 6f);
                b.Rt.localRotation = Quaternion.Euler(0, 0, -t * 16f);
                foreach (var g in b.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            }
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
            s.OrbGlowE.color = new Color(CardSkin.Red.r, CardSkin.Red.g, CardSkin.Red.b, pe > pp ? 0.85f : 0f);
            s.OrbGlowP.color = new Color(CardSkin.Teal.r, CardSkin.Teal.g, CardSkin.Teal.b, pp > pe ? 0.85f : 0f);
            int c = _state.Sectors[i].Control;
            var col = c == _P ? CardSkin.Teal : CardSkin.Red;
            if (s.ChipText != null)
            {
                s.ChipText.text = string.Format("<color=#7CF2D8>{0}</color>:<color=#FF8A80>{1}</color>", pp, pe);
                if (s.ChipFx != null) s.ChipFx.SetRim(new Color(col.r, col.g, col.b, 0.7f));
            }
            if (s.LastControl != c)
            {
                s.WashC = col;
                if (s.PlateFx != null) s.PlateFx.SetRim(new Color(col.r, col.g, col.b, 0.75f));
                if (s.LastControl != -2)
                {
                    s.WashA = 1f;
                    s.Stamp.text = c == _P ? Loc.T("ВЗЯТ") : Loc.T("ПОТЕРЯН");
                    s.Stamp.color = new Color(col.r, col.g, col.b, 1f);
                }
                else s.WashA = 0.5f;
                s.LastControl = c;
            }
        }

        private void Hint(string text, bool bad = false)
        {
            if (_hintText == null) return;
            _hintText.text = text;
            _hintText.color = bad ? new Color(1f, 0.55f, 0.5f) : new Color(1f, 0.93f, 0.8f);
        }

        private void AddLog(string line)
        {
            _log.Add(line);
            _logTotal++;
            if (_log.Count > 60) _log.RemoveAt(0);
        }

        // ==================== КНОПКИ ====================

        private void OnEndTurn()
        {
            if (_busy || _state == null || _state.Over || _dragging != null) return;
            SFXManager.Play(Sfx.UiConfirm);
            _endButtonImg.rectTransform.localScale = Vector3.one * 0.93f;
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
            pv.SetAlpha(0.85f);
            pv.SetStatus(Loc.T("НАЁМНИКИ"), new Color(1f, 0.8f, 0.35f));
            Hook(pv);
            _pending[bc] = pv;
            pv.Punch(0.3f);
            SFXManager.Play(Sfx.CardPlace);
            LayoutAll(false);
            RefreshPanels();
        }

        // ==================== ОКНО «ВРАГ ШТУРМУЕТ» ====================

        private void ShowDefensePrompt(int attacker, StarSystem sys)
        {
            if (_canvas == null) BuildCanvas();
            FitScaler();
            var tm = TimeManager.Instance;
            int saved = tm != null ? tm.CurrentSpeed : 1;
            if (tm != null) tm.SetSpeed(0);
            AssaultLink.BattleRunning = true;
            foreach (Transform c in _canvas.transform) Destroy(c.gameObject);
            _root.SetActive(true);
            _state = null;
            var croot = (RectTransform)_canvas.transform;
            var dim = LGBuild.Panel(croot, "Dim", new Color(0, 0, 0, 0.75f), raycast: true);
            dim.rectTransform.Stretch();
            var box = GlassAt(croot, "Prompt", Vector2.zero, new Vector2(700, 330), PanelTint, new Color(1f, 0.45f, 0.42f, 0.6f), false, 20f, raycast: true);
            LG.Motion(box.gameObject, LGAppear.Kind.Pop);
            var acc = LGBuild.Panel(box.rectTransform, "Accent", UIManager.DS.Red);
            acc.raycastTarget = false;
            acc.rectTransform.TopBand(26, 3, 300, 300);
            var t = Caption(box.rectTransform, Loc.T("ВРАГ ВЫСАЖИВАЕТСЯ!"), 28, new Color(1f, 0.6f, 0.55f), TextAnchor.UpperCenter, 2f);
            t.rectTransform.Stretch(20, 0, 20, 42);
            var body = LGBuild.Label(box.rectTransform, Loc.F("{0} штурмует колонию {1}. Гарнизон, ополчение и укрепления готовы к бою.\nКомандовать обороной лично или довериться автобою?",
                AIEmpireManager.NameOf(attacker, Loc.T("Враг")), sys.Name), 15, Muted, TextAnchor.UpperCenter, wrap: true);
            body.rectTransform.Stretch(50, 110, 50, 100);
            SFXManager.Play(Sfx.RedAlert);
            void Btn(string label, Color tint, Color rim, Vector2 pos, Action act)
            {
                var b = LGBuild.Button(box.rectTransform, label, tint, rim, act, null, label, 15, 14f);
                b.GetComponent<Image>().rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), pos, new Vector2(270, 58));
                var l = b.GetComponentInChildren<Text>();
                if (l != null) l.Track(1.4f);
            }
            Btn(Loc.T("ЗАЩИЩАТЬСЯ ЛИЧНО"), UIManager.DS.BtnSuccess, new Color(UIManager.DS.Green.r, UIManager.DS.Green.g, UIManager.DS.Green.b, 0.8f), new Vector2(-148, 30), () =>
            {
                AssaultLink.BattleRunning = false;
                _root.SetActive(false);
                if (tm != null) tm.SetSpeed(saved);
                AssaultLink.StartDefense(attacker, sys, false);
            });
            Btn(Loc.T("АВТОБОЙ"), UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), new Vector2(148, 30), () =>
            {
                AssaultLink.BattleRunning = false;
                _root.SetActive(false);
                if (tm != null) tm.SetSpeed(saved);
                AssaultLink.StartDefense(attacker, sys, true);
            });
        }

        /// <summary>Щелчок по дорожке сектора.</summary>
        private sealed class SectorClick : MonoBehaviour, IPointerClickHandler
        {
            public Action OnClick;
            public void OnPointerClick(PointerEventData e) => OnClick?.Invoke();
        }
    }
}
