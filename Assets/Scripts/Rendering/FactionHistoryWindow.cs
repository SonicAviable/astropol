using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Летопись фракции — кинематографичное вступление перед партией в духе Civilization VI.
    ///   • 4 главы истории народа и финальное обращение к правителю (данные — FactionHistory), голос рассказчика,
    ///     своя музыкальная тема фракции, приглушаемая под голосом;
    ///   • иллюстрации глав в медленном движении камеры (Ken Burns) со сменой через наплыв, тёплая виньетка, плёночное зерно,
    ///     пылинки в луче; кадр обведён двойной золотой рамкой, как страница манускрипта;
    ///   • титр главы по центру: «Глава вторая» между золотыми орнаментами, название книжным шрифтом с засечками
    ///     (PT Serif) с «разъезжающейся» разрядкой, дата, анаморфная вспышка и низкий удар;
    ///   • субтитры курсивом в кавычках проявляются по словам синхронно с голосом; внизу — римские цифры глав в ромбах;
    ///   • музыка — оркестровая тема фракции (Resources/Music/Chronicle, Tools/chronicle_music);
    ///   • финал — панорама фракции, правитель в полный рост в своей сцене, герб и кнопка «Начать правление».
    /// Пробел / Enter / щелчок — следующая глава; Esc или «Пропустить» — сразу к финалу (в финале — начать).
    /// Всё на unscaled time: до старта партии время стоит.
    /// </summary>
    public sealed class FactionHistoryWindow : MonoBehaviour, IPointerClickHandler
    {
        private const float CrossFade = 1.6f, FirstLineDelay = 1.7f, LineGap = 0.55f, ChapterHold = 1.4f;
        /// <summary>Золото орнаментов, рамки и ленты глав — общее для всех фракций (цвет фракции — во вспышке и гербе).</summary>
        private static readonly Color Gold = new Color(0.90f, 0.79f, 0.54f);
        /// <summary>Тёплый тон иллюстраций — «старая бумага».</summary>
        private static readonly Color Warm = new Color(1f, 0.97f, 0.9f);
        /// <summary>Центр титра главы относительно середины экрана.</summary>
        private const float TitleY = -150f;

        private System.Action _onDone;
        private FactionHistoryEntry _entry;
        private FactionInfo _faction;
        private LeaderPortraits.Entry _leader;
        private Color _accent = UIManager.DS.Gold;

        private RectTransform _rt, _artRoot, _titleBlock, _ornL, _ornR, _ornDiamondL, _ornDiamondR, _flare, _flareCore;
        private RectTransform _leaderHost, _finaleBlock, _timeline, _timelineFill, _subtitleRt;
        private CanvasGroup _group, _titleGroup, _finaleGroup, _leaderGroup, _buttonGroup, _chromeGroup, _subGroup;
        private Text _chapterNo, _chapterTitle, _chapterDate, _subtitle, _header, _hint;
        private Text _finaleName, _finaleTitle, _finaleLeader;
        private Image _flareImg, _flareCoreImg, _leaderGlow, _tint, _sideShade, _centerShade;
        private RawImage _grain;
        private MenuPrimaryButton _startBtn;
        private readonly ArtLayer[] _layers = new ArtLayer[2];
        private int _front;
        private readonly List<Mote> _motes = new List<Mote>();
        private readonly List<Node> _nodes = new List<Node>();

        private AudioSource _music, _voice, _fx;
        private float _musicFade, _musicTarget;

        // Состояние показа
        private int _seg = -1;                 // 0..3 — главы, 4 — финал
        private float _t, _segT;               // общее время и время с начала сегмента
        private int _line;                     // текущая фраза сегмента
        private float _lineStart, _lineDur;    // начало (по _segT) и длительность фразы
        private bool _lineVoiced, _finaleReady, _closing, _riserPlayed;
        private float _segEndAt = -1f;
        private float _closeT;
        private string _lineText = "";
        private List<(int start, int len)> _words = new List<(int, int)>();

        private class ArtLayer
        {
            public RectTransform Rt;
            public RawImage Img;
            public float Born, Duration, S0, S1;
            public Vector2 P0, P1;
        }

        /// <summary>Глава на ленте: ромб с римской цифрой (рамка, заливка, ореол текущей).</summary>
        private class Node
        {
            public Image Border, Inner, Glow;
            public Text Num;
        }

        private class Mote
        {
            public RectTransform Rt;
            public Image Img;
            public float Speed, Phase, X, Depth;
        }

        private HistoryChapter Current => _seg < 4 ? _entry.Chapters[_seg] : _entry.Finale;
        private bool IsFinale => _seg >= 4;

        // ================================================================ Создание

        public static FactionHistoryWindow Create(Transform modalCanvas, System.Action onDone)
        {
            var rt = LGBuild.Rect(modalCanvas, "FactionHistory");
            rt.Stretch();
            var w = rt.gameObject.AddComponent<FactionHistoryWindow>();
            w._onDone = onDone;
            w.Build(rt);
            rt.gameObject.SetActive(false);
            return w;
        }

        /// <summary>Есть ли летопись для фракции и включена ли она в настройках.</summary>
        public static bool Available(FactionInfo f) => GameSettings.FactionHistory && FactionHistory.For(f) != null;

        private void Build(RectTransform rt)
        {
            _rt = rt;
            _group = rt.gameObject.AddComponent<CanvasGroup>();
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = Color.black;
            bg.raycastTarget = true;

            // Иллюстрации: два слоя для наплыва
            _artRoot = LGBuild.Rect(rt, "Art");
            _artRoot.Stretch();
            _artRoot.gameObject.AddComponent<RectMask2D>();
            for (int i = 0; i < 2; i++)
            {
                var a = LGBuild.Rect(_artRoot, "Layer" + i);
                a.anchorMin = a.anchorMax = a.pivot = new Vector2(0.5f, 0.5f);
                var img = a.gameObject.AddComponent<RawImage>();
                img.raycastTarget = false;
                img.color = new Color(1f, 1f, 1f, 0f);
                _layers[i] = new ArtLayer { Rt = a, Img = img };
            }

            // Цветокоррекция: оттенок фракции снизу и тёмный градиент под субтитры
            _tint = LGBuild.Panel(rt, "Tint", Color.white);
            _tint.sprite = MenuArt.VerticalFade;
            _tint.rectTransform.Stretch();
            var shade = LGBuild.Panel(rt, "Shade", new Color(0f, 0f, 0f, 0.92f));
            shade.sprite = MenuArt.VerticalFade;
            shade.rectTransform.anchorMin = Vector2.zero;
            shade.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;
            // Слева — только в финале (под текстом обращения); в главах — мягкое пятно тени под титром по центру
            _sideShade = LGBuild.Panel(rt, "SideShade", new Color(0f, 0f, 0f, 0f));
            _sideShade.sprite = MenuArt.HorizontalFade;
            _sideShade.rectTransform.anchorMin = Vector2.zero;
            _sideShade.rectTransform.anchorMax = new Vector2(0.55f, 1f);
            _sideShade.rectTransform.offsetMin = _sideShade.rectTransform.offsetMax = Vector2.zero;
            _centerShade = LGBuild.Panel(rt, "CenterShade", new Color(0f, 0f, 0f, 0.6f));
            _centerShade.sprite = MenuArt.SoftDisc;
            _centerShade.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, TitleY + 10f), new Vector2(1500, 560));

            // Правитель (финал) — стоит в своей сцене
            var leaderRoot = LGBuild.Rect(rt, "Leader");
            leaderRoot.anchorMin = new Vector2(0.44f, 0f);
            leaderRoot.anchorMax = new Vector2(1f, 1f);
            leaderRoot.offsetMin = leaderRoot.offsetMax = Vector2.zero;
            _leaderGroup = leaderRoot.gameObject.AddComponent<CanvasGroup>();
            _leaderGroup.blocksRaycasts = false;
            _leaderGlow = LGBuild.Panel(leaderRoot, "Glow", Color.white);
            _leaderGlow.sprite = MenuArt.SoftDisc;
            _leaderGlow.rectTransform.At(new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100, 1100));
            _leaderHost = LGBuild.Rect(leaderRoot, "Host");
            _leaderHost.Stretch();

            BuildMotes(rt);

            var vig = LGBuild.Panel(rt, "Vignette", new Color(0f, 0f, 0f, 0.9f));
            vig.sprite = MenuArt.Vignette;
            vig.rectTransform.Stretch(-30, -30, -30, -30);

            _grain = LGBuild.Rect(rt, "Grain").gameObject.AddComponent<RawImage>();
            _grain.rectTransform.Stretch();
            _grain.texture = GrainTexture();
            _grain.color = new Color(1f, 1f, 1f, 0.055f);
            _grain.raycastTarget = false;

            // Двойная золотая рамка страницы
            Frame(rt, 28f, new Color(Gold.r, Gold.g, Gold.b, 0.25f));
            Frame(rt, 36f, new Color(Gold.r, Gold.g, Gold.b, 0.11f));

            BuildFlare(rt);
            BuildTitle(rt);
            BuildFinale(rt);
            BuildSubtitles(rt);
            BuildChrome(rt);

            _music = gameObject.AddComponent<AudioSource>();
            _voice = gameObject.AddComponent<AudioSource>();
            _fx = gameObject.AddComponent<AudioSource>();
            foreach (var s in new[] { _music, _voice, _fx })
            {
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.ignoreListenerPause = true;
            }
        }

        /// <summary>Рамка в 1 пиксель с отступом inset от краёв экрана.</summary>
        private static void Frame(RectTransform rt, float inset, Color c)
        {
            var root = LGBuild.Rect(rt, "Frame");
            root.Stretch(inset, inset, inset, inset);
            for (int i = 0; i < 4; i++)
            {
                var l = LGBuild.Panel(root, "Edge", c).rectTransform;
                bool horizontal = i < 2;
                l.anchorMin = horizontal ? new Vector2(0f, i) : new Vector2(i - 2, 0f);
                l.anchorMax = horizontal ? new Vector2(1f, i) : new Vector2(i - 2, 1f);
                l.pivot = new Vector2(0.5f, 0.5f);
                l.anchoredPosition = Vector2.zero;
                l.sizeDelta = horizontal ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
            }
        }

        private void BuildMotes(RectTransform rt)
        {
            var root = LGBuild.Rect(rt, "Motes");
            root.Stretch();
            for (int i = 0; i < 46; i++)
            {
                var img = LGBuild.Panel(root, "Mote", Color.white);
                img.sprite = MenuArt.SoftDisc;
                float depth = Random.Range(0.25f, 1f);
                float s = Mathf.Lerp(3f, 11f, depth * depth);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                img.rectTransform.sizeDelta = new Vector2(s, s);
                _motes.Add(new Mote
                {
                    Rt = img.rectTransform, Img = img, Depth = depth,
                    Speed = Mathf.Lerp(6f, 26f, depth), Phase = Random.Range(0f, 100f), X = Random.value
                });
            }
        }

        /// <summary>Анаморфная вспышка: длинная мягкая полоса света с яркой серединой — на титре главы.</summary>
        private void BuildFlare(RectTransform rt)
        {
            _flareImg = LGBuild.Panel(rt, "Flare", Color.white);
            _flareImg.sprite = MenuArt.SoftDisc;
            _flare = _flareImg.rectTransform;
            _flare.anchorMin = _flare.anchorMax = new Vector2(0.5f, 0.5f);
            _flare.pivot = new Vector2(0.5f, 0.5f);
            _flareCoreImg = LGBuild.Panel(_flare, "Core", Color.white);
            _flareCoreImg.sprite = MenuArt.SoftDisc;
            _flareCore = _flareCoreImg.rectTransform;
            _flareCore.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 3));
            _flareImg.color = _flareCoreImg.color = Color.clear;
        }

        /// <summary>Титр по центру: орнамент «◇ ГЛАВА ВТОРАЯ ◇» между золотыми линиями, название с засечками, дата.</summary>
        private void BuildTitle(RectTransform rt)
        {
            _titleBlock = LGBuild.Rect(rt, "Title");
            _titleBlock.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, TitleY), new Vector2(1500, 240));
            _titleGroup = _titleBlock.gameObject.AddComponent<CanvasGroup>();
            _titleGroup.blocksRaycasts = false;

            _chapterNo = LGBuild.Label(_titleBlock, "", 21, Gold, TextAnchor.MiddleCenter).Track(9f);
            _chapterNo.font = GameFont.Serif;
            _chapterNo.rectTransform.TopBand(0, 34);
            Deepen(_chapterNo, 2f);
            _ornL = Ornament(_chapterNo.rectTransform, true);
            _ornR = Ornament(_chapterNo.rectTransform, false);
            _ornDiamondL = Diamond(_chapterNo.rectTransform);
            _ornDiamondR = Diamond(_chapterNo.rectTransform);

            _chapterTitle = LGBuild.Label(_titleBlock, "", 96, Color.white, TextAnchor.MiddleCenter);
            _chapterTitle.font = GameFont.Serif;
            _chapterTitle.rectTransform.TopBand(40, 124);
            _chapterTitle.Track(3f);
            Deepen(_chapterTitle, 3f);

            _chapterDate = LGBuild.Label(_titleBlock, "", 26, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter).Track(9f);
            _chapterDate.font = GameFont.Serif;
            _chapterDate.rectTransform.TopBand(176, 36);
            Deepen(_chapterDate, 2f);
        }

        /// <summary>Золотая линия орнамента — гаснет к краю, от подписи наружу.</summary>
        private static RectTransform Ornament(RectTransform host, bool left)
        {
            var img = LGBuild.Panel(host, left ? "OrnL" : "OrnR", Gold);
            img.sprite = MenuArt.HorizontalFade;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.sizeDelta = new Vector2(0f, 1f);
            if (left) r.localScale = new Vector3(-1f, 1f, 1f);   // яркий край — у подписи
            return r;
        }

        /// <summary>Маленький контурный ромб орнамента.</summary>
        private static RectTransform Diamond(RectTransform host)
        {
            var outer = LGBuild.Panel(host, "Diamond", Gold).rectTransform;
            outer.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(9, 9));
            outer.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var inner = LGBuild.Panel(outer, "Hole", new Color(0.02f, 0.02f, 0.02f, 0.9f)).rectTransform;
            inner.Stretch(1.4f, 1.4f, 1.4f, 1.4f);
            return outer;
        }

        private void BuildFinale(RectTransform rt)
        {
            _finaleBlock = LGBuild.Rect(rt, "Finale");
            _finaleBlock.At(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150, 30), new Vector2(980, 330));
            _finaleGroup = _finaleBlock.gameObject.AddComponent<CanvasGroup>();
            _finaleGroup.blocksRaycasts = false;

            var tag = LGBuild.Label(_finaleBlock, Loc.T("ВАШЕ ПРАВЛЕНИЕ НАЧИНАЕТСЯ"), 19, Gold, TextAnchor.LowerLeft).Track(8f);
            tag.font = GameFont.Serif;
            tag.rectTransform.TopBand(0, 26);
            tag.name = "Tag";
            _finaleName = LGBuild.Label(_finaleBlock, "", 72, Color.white, TextAnchor.UpperLeft, wrap: true);
            _finaleName.font = GameFont.Serif;
            _finaleName.lineSpacing = 0.92f;
            _finaleName.rectTransform.TopBand(40, 150);
            Deepen(_finaleName, 3f);
            _finaleTitle = LGBuild.Label(_finaleBlock, "", 26, new Color(0.92f, 0.89f, 0.83f), TextAnchor.UpperLeft);
            _finaleTitle.font = GameFont.SerifItalic;
            _finaleTitle.rectTransform.TopBand(200, 30);
            _finaleLeader = LGBuild.Label(_finaleBlock, "", 19, Gold, TextAnchor.UpperLeft).Track(2f);
            _finaleLeader.font = GameFont.Serif;
            _finaleLeader.rectTransform.TopBand(240, 26);

            _startBtn = MenuPrimaryButton.Create(rt, Loc.T("НАЧАТЬ ПРАВЛЕНИЕ  ›"), 17, Finish);
            ((RectTransform)_startBtn.transform).At(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(150, 0), new Vector2(330, 52));
            _buttonGroup = _startBtn.gameObject.AddComponent<CanvasGroup>();
        }

        private void BuildSubtitles(RectTransform rt)
        {
            _subtitleRt = LGBuild.Rect(rt, "Subtitles");
            _subtitleRt.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1260, 120));
            _subGroup = _subtitleRt.gameObject.AddComponent<CanvasGroup>();
            _subGroup.blocksRaycasts = false;
            _subtitle = LGBuild.Label(_subtitleRt, "", 33, new Color(0.96f, 0.93f, 0.86f), TextAnchor.LowerCenter, wrap: true);
            _subtitle.font = GameFont.SerifItalic;
            _subtitle.lineSpacing = 1.08f;
            Deepen(_subtitle, 2f);
        }

        /// <summary>Герб и заголовок по центру сверху, «Пропустить», лента глав из ромбов и подсказка.</summary>
        private void BuildChrome(RectTransform rt)
        {
            var chrome = LGBuild.Rect(rt, "Chrome");
            chrome.Stretch();
            _chromeGroup = chrome.gameObject.AddComponent<CanvasGroup>();

            // Герб ставит Play (под фракцию) — над заголовком
            _header = LGBuild.Label(chrome, "", 12, new Color(1f, 1f, 1f, 0.68f), TextAnchor.MiddleCenter, bold: true).Track(6f);
            _header.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -142), new Vector2(1000, 24));
            Deepen(_header, 1.5f);

            // «Пропустить» — тихая надпись в углу; светлеет под курсором
            var skip = LGBuild.Label(chrome, Loc.T("ПРОПУСТИТЬ  ›"), 12, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleRight, bold: true).Track(4f);
            skip.name = "Skip";
            skip.raycastTarget = true;
            skip.rectTransform.At(new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-80, -78), new Vector2(240, 34));
            var skipBtn = skip.gameObject.AddComponent<Button>();
            skipBtn.targetGraphic = skip;
            var cb = skipBtn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = cb.selectedColor = new Color(1f, 0.92f, 0.72f, 1.8f);
            cb.pressedColor = new Color(0.9f, 0.8f, 0.6f, 1.6f);
            cb.fadeDuration = 0.12f;
            skipBtn.colors = cb;
            skipBtn.onClick.AddListener(Skip);
            TooltipHelper.Attach(skip.gameObject, Loc.T("<b>Пропустить летопись</b>\nСразу к началу правления  (Esc)"));

            _hint = LGBuild.Label(chrome, Loc.T("ПРОБЕЛ — ДАЛЬШЕ"), 11, new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleRight, bold: true).Track(4f);
            _hint.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0.5f), new Vector2(-80, 80), new Vector2(300, 24));

            // Лента глав: тонкая линия, золотая заливка до текущей главы, ромбы с римскими цифрами I…V
            _timeline = LGBuild.Rect(chrome, "Timeline");
            _timeline.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0, 82), new Vector2(560, 48));
            var track = LGBuild.Panel(_timeline, "Track", new Color(1f, 1f, 1f, 0.15f));
            track.rectTransform.anchorMin = new Vector2(0, 0.5f); track.rectTransform.anchorMax = new Vector2(1, 0.5f);
            track.rectTransform.pivot = new Vector2(0, 0.5f);
            track.rectTransform.anchoredPosition = Vector2.zero;
            track.rectTransform.sizeDelta = new Vector2(0, 1);
            var fill = LGBuild.Panel(_timeline, "Fill", Gold);
            _timelineFill = fill.rectTransform;
            _timelineFill.anchorMin = _timelineFill.anchorMax = new Vector2(0, 0.5f);
            _timelineFill.pivot = new Vector2(0, 0.5f);
            _timelineFill.anchoredPosition = Vector2.zero;
            _timelineFill.sizeDelta = new Vector2(0, 1);
            for (int i = 0; i < 5; i++)
            {
                var at = new Vector2(i / 4f, 0.5f);
                var glow = LGBuild.Panel(_timeline, "Glow", Color.clear);
                glow.sprite = MenuArt.SoftDisc;
                glow.rectTransform.At(at, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110, 110));
                var border = LGBuild.Panel(_timeline, "Diamond", Color.white);
                border.rectTransform.At(at, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(33, 33));
                border.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var inner = LGBuild.Panel(border.rectTransform, "Fill", Color.black);
                inner.rectTransform.Stretch(1.5f, 1.5f, 1.5f, 1.5f);
                var num = LGBuild.Label(_timeline, Roman(i + 1), 17, Color.white, TextAnchor.MiddleCenter);
                num.font = GameFont.Serif;
                num.rectTransform.At(at, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(48, 48));
                _nodes.Add(new Node { Border = border, Inner = inner, Glow = glow, Num = num });
            }
        }

        /// <summary>Мягкая глубокая тень под крупным текстом — читается на любой иллюстрации.</summary>
        private static void Deepen(Text t, float d)
        {
            var sh = t.GetComponent<Shadow>();
            if (sh != null) { sh.effectColor = new Color(0f, 0f, 0f, 0.85f); sh.effectDistance = new Vector2(0f, -d); }
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.35f);
            o.effectDistance = new Vector2(d * 0.8f, -d * 0.8f);
        }

        private static Texture2D s_grain;

        private static Texture2D GrainTexture()
        {
            if (s_grain != null) return s_grain;
            const int n = 192;
            s_grain = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color32[n * n];
            var rng = new System.Random(17);
            for (int i = 0; i < px.Length; i++)
            {
                byte v = (byte)rng.Next(0, 256);
                px[i] = new Color32(v, v, v, (byte)rng.Next(60, 256));
            }
            s_grain.SetPixels32(px);
            s_grain.Apply();
            return s_grain;
        }

        // ================================================================ Запуск

        public void Play(FactionInfo faction)
        {
            _faction = faction;
            _entry = FactionHistory.For(faction);
            if (_entry == null) { _onDone?.Invoke(); return; }
            _leader = LeaderPortraits.ForFaction(faction);
            _accent = _leader != null ? _leader.Accent : faction != null ? faction.EmpireColor : UIManager.DS.Gold;

            Color soft = Color.Lerp(_accent, Color.white, 0.35f);
            _tint.color = new Color(_accent.r, _accent.g, _accent.b, 0.1f);
            _flareImg.color = _flareCoreImg.color = Color.clear;
            _leaderGlow.color = new Color(_accent.r, _accent.g, _accent.b, 0.22f);
            foreach (var m in _motes) m.Img.color = Color.Lerp(_accent, Color.white, 0.6f);

            string name = faction != null ? faction.Name : "";
            _header.text = Loc.F("ЛЕТОПИСЬ  ·  {0}", name.ToUpper());
            for (int i = 0; i < _nodes.Count; i++)
                TooltipHelper.Attach(_nodes[i].Border.gameObject, i < 4 ? _entry.Chapters[i].Title : Loc.T("Ваше правление"));

            _finaleName.text = name;
            _finaleTitle.text = faction != null ? faction.Title : "";
            _finaleLeader.text = _leader != null ? $"{_leader.Name}  ·  {_leader.Title}" : "";

            LGBuild.Clear(_leaderHost);
            var portrait = LeaderPortraitView.Create(_leaderHost, _leader, 1f, new Vector4(0.06f, 0.04f, 0.26f, 0f), false, true, true);
            if (portrait != null) portrait.Glow = 0.35f;

            // Герб по центру над заголовком, с мягким ореолом цвета фракции
            var chrome = _header.transform.parent;
            foreach (var n in new[] { "HeaderEmblem", "HeaderGlow" })
            {
                var old = chrome.Find(n);
                if (old != null) Destroy(old.gameObject);
            }
            var halo = LGBuild.Panel(chrome, "HeaderGlow", new Color(_accent.r, _accent.g, _accent.b, 0.32f));
            halo.sprite = MenuArt.SoftDisc;
            halo.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -92), new Vector2(130, 130));
            var em = FactionEmblems.Create(chrome, FactionEmblems.ForFaction(faction), 56, soft, "HeaderEmblem");
            if (em != null) em.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -92), new Vector2(56, 56));

            foreach (var l in _layers) { l.Img.texture = null; l.Img.color = new Color(1f, 1f, 1f, 0f); }
            _titleGroup.alpha = _finaleGroup.alpha = _leaderGroup.alpha = _subGroup.alpha = 0f;
            _buttonGroup.alpha = 0f;
            _buttonGroup.interactable = _buttonGroup.blocksRaycasts = false;
            _chromeGroup.alpha = 0f;
            _subtitle.text = "";
            _group.alpha = 1f;
            _group.interactable = _group.blocksRaycasts = true;

            _t = 0f;
            _seg = -1;
            _closing = false;
            _finaleReady = false;
            _riserPlayed = false;

            // Музыка фракции: игровая музыка молчит, пока идёт летопись
            SFXManager.ExternalDuck = 1f;
            _music.clip = Resources.Load<AudioClip>(_entry.Music);
            _music.loop = true;
            _music.volume = 0f;
            _musicFade = 0f;
            _musicTarget = 1f;
            if (_music.clip != null) _music.Play();

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            EnterSegment(0);
        }

        // ================================================================ Сегменты и фразы

        private void EnterSegment(int seg)
        {
            _seg = seg;
            _segT = 0f;
            _line = -1;
            _segEndAt = -1f;
            _voice.Stop();
            _subtitle.text = "";
            _lineText = "";

            var c = Current;
            var tex = !string.IsNullOrEmpty(c.Art) ? Resources.Load<Texture2D>(c.Art) : null;
            if (tex == null && IsFinale && _leader != null) tex = Resources.Load<Texture2D>("Diplomacy/bg_" + _leader.Key);
            if (tex != null) PushArt(tex, seg);

            if (!IsFinale)
            {
                _chapterNo.text = ChapterWord(seg + 1);
                _chapterTitle.text = c.Title;
                _chapterDate.text = "—   " + c.Date + "   —";
                PlayFx(Cine(Clip.Impact), 0.85f);
            }
            else
            {
                PlayFx(Cine(Clip.Impact), 1f);
            }
            SFXManager.Play(Sfx.Whoosh, 0.25f, 0.7f);
            _flareT = 0f;
        }

        private float _flareT = 99f;

        /// <summary>Новая иллюстрация поверх прежней: наплыв и своё движение камеры.</summary>
        private void PushArt(Texture2D tex, int seg)
        {
            _front = 1 - _front;
            var l = _layers[_front];
            l.Img.texture = tex;
            l.Born = _t;
            l.Duration = 26f;
            l.Rt.SetAsLastSibling();
            // Чередуем наезд и отъезд, направление панорамы — по номеру главы
            bool zoomIn = seg % 2 == 0 || seg == 4;
            l.S0 = zoomIn ? 1.04f : 1.17f;
            l.S1 = zoomIn ? 1.17f : 1.05f;
            if (seg == 4) { l.S0 = 1.16f; l.S1 = 1.03f; }
            float dir = seg % 2 == 0 ? 1f : -1f;
            l.P0 = new Vector2(-0.35f * dir, 0.15f);
            l.P1 = new Vector2(0.35f * dir, -0.1f);
            if (seg == 4) { l.P0 = new Vector2(0.25f, 0.1f); l.P1 = new Vector2(-0.2f, 0f); }
        }

        private int _renderedLine = -1;

        private void StartLine(int i)
        {
            _renderedLine = -1;
            var c = Current;
            _line = i;
            _lineStart = _segT;
            _lineText = "«" + c.Lines[i] + "»";
            var clip = FactionHistory.Voice(_entry.Key, IsFinale ? -1 : _seg, i);
            _lineVoiced = clip != null;
            if (_lineVoiced)
            {
                _voice.clip = clip;
                _voice.volume = Mathf.Clamp01(GameSettings.SfxVolume * 1.3f);
                _voice.Play();
                _lineDur = clip.length;
            }
            else _lineDur = 1.2f + _lineText.Length * 0.058f;   // темп чтения без голоса
            _words = SplitWords(_lineText);
        }

        private static List<(int, int)> SplitWords(string s)
        {
            var list = new List<(int, int)>();
            int i = 0;
            while (i < s.Length)
            {
                while (i < s.Length && s[i] == ' ') i++;
                int st = i;
                while (i < s.Length && s[i] != ' ') i++;
                if (i > st) list.Add((st, i - st));
            }
            return list;
        }

        /// <summary>Субтитр: слова проявляются по ходу голоса (каждое — за ~0,3 с), раскладка строк не прыгает.</summary>
        private string RenderSubtitle(float progress)
        {
            if (_words.Count == 0) return "";
            float total = _lineText.Length;
            float head = progress * total;
            var sb = new StringBuilder(_lineText.Length + _words.Count * 24);
            int pos = 0;
            foreach (var (st, len) in _words)
            {
                if (st > pos) sb.Append(_lineText, pos, st - pos);
                float a = Mathf.Clamp01((head - st) / Mathf.Max(6f, len + 4f));
                if (a >= 0.999f) sb.Append(_lineText, st, len);
                else
                {
                    byte al = (byte)Mathf.RoundToInt(a * 255f);
                    sb.Append("<color=#F5EDDB").Append(al.ToString("X2")).Append('>').Append(_lineText, st, len).Append("</color>");
                }
                pos = st + len;
            }
            return sb.ToString();
        }

        private void TickLines()
        {
            var c = Current;
            if (_line < 0)
            {
                if (_segT >= FirstLineDelay) StartLine(0);
                return;
            }

            float k = Mathf.Clamp01((_segT - _lineStart) / Mathf.Max(0.1f, _lineDur * 0.9f));
            if (k < 1f || _renderedLine != _line) { _subtitle.text = RenderSubtitle(k); if (k >= 1f) _renderedLine = _line; }

            bool ended = _lineVoiced ? !_voice.isPlaying && _segT - _lineStart > 0.2f : _segT - _lineStart >= _lineDur;
            if (!ended) return;
            if (_line + 1 < c.Lines.Length)
            {
                if (_segT >= _lineStart + _lineDur + LineGap) StartLine(_line + 1);
                return;
            }

            if (_segEndAt < 0f) _segEndAt = _segT + (IsFinale ? 0.5f : ChapterHold);
        }

        // ================================================================ Управление

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) Next();
        }

        /// <summary>Пробел, Enter, щелчок — следующая глава (в финале — начать, когда кнопка видна).</summary>
        private void Next()
        {
            if (_closing || _seg < 0 || _segT < 0.5f) return;
            if (IsFinale) { if (_finaleReady) Finish(); else RevealFinale(); return; }
            SFXManager.Play(Sfx.UiTab, 0.5f);
            EnterSegment(_seg + 1);
        }

        private void Skip()
        {
            EventSystem.current?.SetSelectedGameObject(null);   // иначе Пробел «нажмёт» кнопку ещё раз
            if (_closing) return;
            if (IsFinale) { if (_finaleReady) Finish(); else RevealFinale(); return; }
            SFXManager.Play(Sfx.UiConfirm, 0.5f);
            EnterSegment(4);
        }

        /// <summary>В финале нетерпеливому игроку — сразу показать кнопку (голос дочитывает фоном).</summary>
        private void RevealFinale()
        {
            _finaleReady = true;
            _buttonGroup.interactable = _buttonGroup.blocksRaycasts = true;
        }

        private void Finish()
        {
            if (_closing) return;
            _closing = true;
            _closeT = 0f;
            _group.interactable = _group.blocksRaycasts = false;
            _musicTarget = 0f;
            SFXManager.Play(Sfx.UiConfirm, 0.8f, 0.9f);
            _onDone?.Invoke();
        }

        // ================================================================ Кадр

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _t += dt;
            _segT += dt;

            if (_closing)
            {
                _closeT += dt;
                _group.alpha = Mathf.Clamp01(1f - _closeT / 0.7f);
                _voice.volume = Mathf.MoveTowards(_voice.volume, 0f, dt * 2f);
                TickMusic(dt);
                // Музыка фракции уходит, игровая плавно возвращается (MusicManager поднимает громкость сам)
                SFXManager.ExternalDuck = _closeT < 0.9f ? 1f : 0f;
                if (_closeT > 1.6f)
                {
                    _music.Stop(); _voice.Stop();
                    gameObject.SetActive(false);
                }
                AnimateArt();
                return;
            }

            SFXManager.ExternalDuck = 1f;
            if (Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Skip(); }
            else if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                     Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.RightArrow)) Next();

            TickLines();

            // Переход к следующей главе; перед финалом — нарастание, которое обрывается ударом финала
            if (!IsFinale && _segEndAt > 0f)
            {
                if (_seg == 3 && !_riserPlayed)
                {
                    // Нарастание (3,2 с) достигает пика ровно в момент удара финала
                    _riserPlayed = true;
                    PlayFx(Cine(Clip.Riser), 0.55f);
                    _segEndAt = _segT + 3.1f;
                }
                if (_segT >= _segEndAt) EnterSegment(_seg + 1);
            }
            if (IsFinale && !_finaleReady && _segEndAt > 0f && _segT >= _segEndAt) RevealFinale();

            TickMusic(dt);
            AnimateArt();
            AnimateText();
            AnimateChrome();
            AnimateAmbient();
        }

        private void TickMusic(float dt)
        {
            if (_music.clip == null) return;
            _musicFade = Mathf.MoveTowards(_musicFade, _musicTarget, dt / (_musicTarget > _musicFade ? 3f : 1.4f));
            bool talking = _voice.isPlaying && !_closing;
            _duck = Mathf.MoveTowards(_duck, talking ? 0.5f : 1f, dt * (talking ? 2.5f : 0.8f));
            _music.volume = GameSettings.MusicVolume * _entry.MusicGain * _musicFade * _duck * 1.15f;
        }

        private float _duck = 1f;

        private void AnimateArt()
        {
            var size = _rt.rect.size;
            if (size.x < 1f) return;
            for (int i = 0; i < 2; i++)
            {
                var l = _layers[i];
                if (l.Img.texture == null) continue;
                float age = _t - l.Born;
                float k = Mathf.Clamp01(age / l.Duration);
                float e = k * k * (3f - 2f * k) * 0.55f + k * 0.45f;   // мягкий старт, ровный ход
                float s = Mathf.Lerp(l.S0, l.S1, e);
                // Заполнение кадра с сохранением пропорций
                float ta = (float)l.Img.texture.width / Mathf.Max(1, l.Img.texture.height);
                Vector2 cover = size.x / size.y > ta ? new Vector2(size.x, size.x / ta) : new Vector2(size.y * ta, size.y);
                Vector2 sz = cover * s;
                l.Rt.sizeDelta = sz;
                Vector2 slack = (sz - size) * 0.5f;
                Vector2 p = Vector2.Lerp(l.P0, l.P1, e);
                l.Rt.anchoredPosition = new Vector2(p.x * slack.x, p.y * slack.y);

                if (i == _front)
                {
                    float a = Mathf.Clamp01(age / CrossFade);
                    l.Img.color = new Color(Warm.r, Warm.g, Warm.b, a * a * (3f - 2f * a));
                }
                else if (_layers[_front].Img.color.a >= 0.999f && l.Img.color.a > 0f)
                    l.Img.color = new Color(Warm.r, Warm.g, Warm.b, 0f);
            }
        }

        private void AnimateText()
        {
            // Титр главы: номер → название с «разъезжающейся» разрядкой → черта → дата; в конце главы гаснет
            if (!IsFinale)
            {
                float a = Mathf.Clamp01((_segT - 0.25f) / 0.9f);
                float fadeOut = _segEndAt > 0f ? Mathf.Clamp01((_segEndAt - _segT) / 0.8f) : 1f;
                _titleGroup.alpha = Mathf.SmoothStep(0f, 1f, a) * fadeOut;
                float tr = Mathf.Lerp(14f, 3f, 1f - Mathf.Pow(1f - Mathf.Clamp01((_segT - 0.25f) / 2.6f), 3f));
                _chapterTitle.GetComponent<LGTracking>().Spacing = tr;
                // Орнамент расходится от подписи «Глава …» в обе стороны
                float lk = Mathf.Clamp01((_segT - 0.5f) / 1.3f);
                float ornW = 190f * (1f - Mathf.Pow(1f - lk, 3f));
                float half = (_chapterNo.preferredWidth + Mathf.Max(0, _chapterNo.text.Length - 1) * 9f) * 0.5f + 22f;   // + разрядка LGTracking
                _ornDiamondL.anchoredPosition = new Vector2(-half, 0f);
                _ornDiamondR.anchoredPosition = new Vector2(half, 0f);
                _ornL.anchoredPosition = new Vector2(-half - 12f, 0f);
                _ornR.anchoredPosition = new Vector2(half + 12f, 0f);
                _ornL.sizeDelta = _ornR.sizeDelta = new Vector2(ornW, 1f);
                _chapterDate.color = new Color(1f, 1f, 1f, 0.7f * Mathf.Clamp01((_segT - 1.0f) / 0.8f));
                _titleBlock.anchoredPosition = new Vector2(0f, TitleY + Mathf.Lerp(-18f, 0f, Mathf.SmoothStep(0, 1, a)));
                _finaleGroup.alpha = Mathf.MoveTowards(_finaleGroup.alpha, 0f, Time.unscaledDeltaTime * 3f);
                _leaderGroup.alpha = Mathf.MoveTowards(_leaderGroup.alpha, 0f, Time.unscaledDeltaTime * 3f);
            }
            else
            {
                // Финал: обращение слева, правитель справа — тень переезжает с центра налево
                _titleGroup.alpha = Mathf.MoveTowards(_titleGroup.alpha, 0f, Time.unscaledDeltaTime * 3f);
                float la = Mathf.Clamp01((_segT - 0.5f) / 2.2f);
                _leaderGroup.alpha = la * la * (3f - 2f * la);
                float rise = 1f - Mathf.Pow(1f - la, 3f);
                _leaderHost.anchoredPosition = new Vector2(0f, Mathf.Lerp(-40f, 0f, rise));
                _leaderHost.localScale = Vector3.one * Mathf.Lerp(1.05f, 1f, rise);
                _leaderGlow.transform.localScale = Vector3.one * (1f + Mathf.Sin(_t * 0.8f) * 0.04f);
                float fa = Mathf.Clamp01((_segT - 1.1f) / 1.2f);
                _finaleGroup.alpha = Mathf.SmoothStep(0f, 1f, fa);
                _finaleName.GetComponent<RectTransform>().anchoredPosition = new Vector2(Mathf.Lerp(-24f, 0f, fa), -40f);
            }

            float fin = IsFinale ? 1f : 0f;
            _sideShade.color = new Color(0f, 0f, 0f, Mathf.MoveTowards(_sideShade.color.a, 0.75f * fin, Time.unscaledDeltaTime * 1.5f));
            _centerShade.color = new Color(0f, 0f, 0f, Mathf.MoveTowards(_centerShade.color.a, 0.6f * (1f - fin), Time.unscaledDeltaTime * 1.5f));

            // Субтитры гаснут в конце главы
            float subTarget = _line >= 0 && (_segEndAt < 0f || _segT < _segEndAt - 0.6f || IsFinale) ? 1f : 0f;
            _subGroup.alpha = Mathf.MoveTowards(_subGroup.alpha, subTarget, Time.unscaledDeltaTime * 2.5f);

            // Кнопка финала
            float bt = _finaleReady ? 1f : 0f;
            _buttonGroup.alpha = Mathf.MoveTowards(_buttonGroup.alpha, bt, Time.unscaledDeltaTime * 2f);

            // Анаморфная вспышка на титре
            _flareT += Time.unscaledDeltaTime;
            float f = _flareT < 0.12f ? _flareT / 0.12f : Mathf.Exp(-(_flareT - 0.12f) * 2.6f);
            float w = Mathf.Lerp(900f, 2600f, Mathf.Clamp01(_flareT / 1.5f));
            _flare.anchoredPosition = IsFinale ? new Vector2(560f - _rt.rect.width * 0.5f, 130f) : new Vector2(0f, TitleY + 22f);
            _flare.sizeDelta = new Vector2(w, 46f);
            _flareCore.sizeDelta = new Vector2(w * 0.55f, 3f);
            var fc = Color.Lerp(_accent, Gold, 0.5f);
            _flareImg.color = new Color(fc.r, fc.g, fc.b, 0.35f * f);
            _flareCoreImg.color = new Color(1f, 1f, 1f, 0.8f * f);
        }

        private void AnimateChrome()
        {
            _chromeGroup.alpha = Mathf.Clamp01((_t - 0.8f) / 1f);
            ((RectTransform)_startBtn.transform).anchoredPosition = new Vector2(150, 250f);
            _hint.text = IsFinale && _finaleReady ? Loc.T("ENTER — НАЧАТЬ") : Loc.T("ПРОБЕЛ — ДАЛЬШЕ");

            // Лента глав: золото до текущего ромба, текущий залит золотом и мерцает ореолом
            float segProgress = IsFinale ? 0f : Mathf.Clamp01(_segT / Mathf.Max(6f, _segEndAt > 0f ? _segEndAt : _segT + 6f));
            float target = Mathf.Clamp01((_seg + segProgress) / 4f);
            float width = _timeline.rect.width;
            float cur = _timelineFill.sizeDelta.x;
            _timelineFill.sizeDelta = new Vector2(Mathf.Lerp(cur, target * width, Time.unscaledDeltaTime * 3f), 1f);
            var ink = new Color(0.03f, 0.03f, 0.03f);
            for (int i = 0; i < _nodes.Count; i++)
            {
                var n = _nodes[i];
                bool now = i == _seg, past = i < _seg;
                n.Border.color = now || past ? new Color(Gold.r, Gold.g, Gold.b, now ? 1f : 0.6f) : new Color(1f, 1f, 1f, 0.18f);
                n.Inner.color = now ? Gold : new Color(ink.r, ink.g, ink.b, past ? 0.82f : 0.6f);
                n.Num.color = now ? ink : past ? Gold : new Color(1f, 1f, 1f, 0.38f);
                n.Glow.color = now ? new Color(Gold.r, Gold.g, Gold.b, 0.32f + Mathf.Sin(_t * 2.4f) * 0.1f) : Color.clear;
                n.Border.rectTransform.localScale = Vector3.one * Mathf.MoveTowards(n.Border.rectTransform.localScale.x, now ? 1.08f : 1f, Time.unscaledDeltaTime);
            }
        }

        private void AnimateAmbient()
        {
            var size = _rt.rect.size;
            foreach (var m in _motes)
            {
                float y = Mathf.Repeat(m.Phase * 37f + _t * m.Speed, size.y + 40f) - 20f;
                float x = m.X * size.x + Mathf.Sin(_t * 0.25f * (0.5f + m.Depth) + m.Phase) * 30f * m.Depth;
                m.Rt.anchoredPosition = new Vector2(x, y);
                float tw = 0.5f + 0.5f * Mathf.Sin(_t * (0.7f + m.Depth) + m.Phase * 3f);
                var c = m.Img.color;
                c.a = (0.06f + 0.22f * tw) * m.Depth;
                m.Img.color = c;
            }
            _grain.uvRect = new Rect(Random.value, Random.value, size.x / 192f / 1.5f, size.y / 192f / 1.5f);
        }

        // ================================================================ Звук

        private enum Clip { Impact, Riser }

        private static AudioClip s_impact, s_riser;

        private static AudioClip Cine(Clip c)
        {
            if (c == Clip.Impact) return s_impact != null ? s_impact : (s_impact = Resources.Load<AudioClip>(FactionHistory.VoicePath + "sfx_impact"));
            return s_riser != null ? s_riser : (s_riser = Resources.Load<AudioClip>(FactionHistory.VoicePath + "sfx_riser"));
        }

        private void PlayFx(AudioClip clip, float volume)
        {
            if (clip == null) return;
            _fx.PlayOneShot(clip, Mathf.Clamp01(GameSettings.SfxVolume * volume));
        }

        private void OnDisable()
        {
            if (_music != null) _music.Stop();
            if (_voice != null) _voice.Stop();
            if (_closing || _seg >= 0) SFXManager.ExternalDuck = 0f;
        }

        private static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString() };

        private static string ChapterWord(int n) => n switch
        {
            1 => Loc.T("ГЛАВА ПЕРВАЯ"),
            2 => Loc.T("ГЛАВА ВТОРАЯ"),
            3 => Loc.T("ГЛАВА ТРЕТЬЯ"),
            4 => Loc.T("ГЛАВА ЧЕТВЁРТАЯ"),
            _ => Loc.F("ГЛАВА {0}", Roman(n))
        };
    }
}
