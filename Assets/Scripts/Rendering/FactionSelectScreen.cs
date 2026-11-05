using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Выбор цивилизации (вариант C «Гербы и досье»). Фон — зал фракции (Resources/Factions/bg_&lt;ключ&gt;
    /// или Diplomacy/bg_&lt;ключ&gt;), плавно сменяется при выборе. Сверху — ряд гербов (выбранный крупнее и светится,
    /// листается стрелками); ниже одна панель: слева правитель в рамке «канала связи», справа досье с вкладками
    /// «Обзор / История / Правитель» — плитки бонусов, сложности и сильной стороны, особенности, шкалы стиля игры, цитата.
    /// Внизу — «Назад», «Случайная», «Дальше».
    /// </summary>
    public class FactionSelectScreen : MonoBehaviour
    {
        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Muted => UIManager.DS.TextMuted;
        private static readonly Color PanelBg = new Color(0.016f, 0.03f, 0.044f, 0.82f);

        private Action<FactionInfo> _onChoose;
        private Action _onBack;
        private int _index;

        private RawImage _bgA, _bgB;
        private bool _bgFront;
        private float _bgMix = 1f;
        private static readonly Color Good = new Color(0.44f, 0.88f, 0.60f);
        private static readonly Color Bad = new Color(0.91f, 0.55f, 0.52f);

        private RectTransform _frame, _portraitHost, _traits, _cards;
        private LiquidGlassEffect _frameFx, _infoFx;
        private Image _divider, _crestMark, _liveDot, _tabMark, _quoteLine;
        private readonly List<Image> _brackets = new List<Image>();
        private RawImage _scan;
        private Text _name, _leader, _desc, _quote, _frameTitle, _frameSub, _ldName, _ldTitle, _ldQuote;
        private CanvasGroup _infoGroup, _frameGroup;

        // Гербы фракций сверху: выбранный крупнее и светится
        private sealed class Medal
        {
            public FactionInfo F;
            public RectTransform Disc, NameRt;
            public Image Glow, Back, Ring, Crest;
            public Text Name;
            public float Scale;

            public void Place(float scale)
            {
                Scale = scale;
                Disc.localScale = Vector3.one * scale;
                NameRt.anchoredPosition = new Vector2(0f, -(MedalY + MedalD * 0.5f * scale + 12f));
            }
        }
        private readonly List<Medal> _medals = new List<Medal>();

        // Досье: вкладки, плитки, шкалы стиля игры
        private readonly List<(Text t, RectTransform rt)> _tabs = new List<(Text, RectTransform)>();
        private RectTransform[] _pages;
        private int _tab;
        private readonly List<(Text val, Text lbl, GameObject go)> _tiles = new List<(Text, Text, GameObject)>();
        private readonly Image[,] _bars = new Image[4, 5];

        // Эффекты: пылинки в воздухе, свечение за рамкой, блик по рамке, вспышка «приёма сигнала»
        private struct Mote { public RectTransform Rt; public Image Img; public Vector2 Pos, Vel; public float Size, Phase; }
        private readonly List<Mote> _motes = new List<Mote>();
        private RectTransform _motesLayer, _sweep;
        private Image _halo, _flash;
        private float _flashT = 1f, _sweepT;
        private Color _accent = Color.white;
        private float _fade = 1f;

        // ==================== ЛОР ====================

        private sealed class Lore
        {
            public string Tagline;
            public LGIcon Emblem;
            public (LGIcon icon, string text)[] Traits;
            public string Story;
            public int Difficulty;      // 1 — лёгкая, 2 — средняя, 3 — сложная
            public int[] Style;         // экономика, наука, флот, дипломатия: 0..5
            public string Strength;     // сильная сторона одним словом
        }

        private static readonly Dictionary<string, Lore> LoreByKey = new Dictionary<string, Lore>
        {
            ["xarn"] = new Lore
            {
                Difficulty = 2, Style = new[] { 3, 2, 5, 1 }, Strength = Loc.T("ФЛОТ"),
                Tagline = Loc.T("Дисциплинированы, воинственны, непреклонны"),
                Emblem = LGIcon.Swords,
                Traits = new[]
                {
                    (LGIcon.Swords, Loc.T("Милитаристы: флот — основа государства")),
                    (LGIcon.Alloys, Loc.T("Тяжёлая промышленность")),
                    (LGIcon.Target, Loc.T("Доктрина: ищут слабого соседа")),
                    (LGIcon.Diplomacy, Loc.T("Дипломатия: внушают страх, требуют дань"))
                },
                Story = Loc.T("Доминион родился на мире-крепости Ксарн-Прайм, где столетиями шла война всех против всех. ") +
                        Loc.T("Победил тот, кто первым построил армию, а не дворец.\n\n") +
                        Loc.T("Сегодня Доминионом правит верховный маршал. Каждый гражданин отслужил во флоте, каждая фабрика ") +
                        Loc.T("в любой день готова перейти на выпуск брони. Соседи для ксарнийцев — либо союзники, либо будущие провинции.")
            },
            ["astrea"] = new Lore
            {
                Difficulty = 1, Style = new[] { 3, 5, 2, 5 }, Strength = Loc.T("НАУКА"),
                Tagline = Loc.T("Миролюбивы, любознательны, принципиальны"),
                Emblem = LGIcon.Star,
                Traits = new[]
                {
                    (LGIcon.Research, Loc.T("Учёные: наука — главный приоритет")),
                    (LGIcon.Influence, Loc.T("Республика: сильное влияние")),
                    (LGIcon.Planet, Loc.T("Колонисты: ценят новые миры")),
                    (LGIcon.Handshake, Loc.T("Дипломатия: опытные переговорщики"))
                },
                Story = Loc.T("Республика выросла из союза первых колоний, которые отказались подчиняться метрополии и решили ") +
                        Loc.T("править собой сами. Её Совет выбирают все граждане, а решения принимаются открыто.\n\n") +
                        Loc.T("Астрейцы верят, что знание сильнее оружия, а договор прочнее страха. Но за мягкими словами ") +
                        Loc.T("Председателя стоит флот, который умеет защищать то, во что верит Республика.")
            },
            ["aquila"] = new Lore
            {
                Difficulty = 1, Style = new[] { 5, 3, 2, 3 }, Strength = Loc.T("ЭКОНОМИКА"),
                Tagline = Loc.T("Расчётливы, предприимчивы, неуловимы"),
                Emblem = LGIcon.Trade,
                Traits = new[]
                {
                    (LGIcon.Trade, Loc.T("Торговцы: любят выгодные сделки")),
                    (LGIcon.Minerals, Loc.T("Добывающая корпорация")),
                    (LGIcon.Energy, Loc.T("Энергетические концессии")),
                    (LGIcon.Gift, Loc.T("Дипломатия: всё имеет цену"))
                },
                Story = Loc.T("Синдикат начинался как горнодобывающая компания, скупившая права на целую звёздную систему. ") +
                        Loc.T("Через век компания стала государством, а совет директоров — правительством.\n\n") +
                        Loc.T("Аквилой правит генеральный директор, а законы здесь пишутся как контракты. Синдикат покупает ") +
                        Loc.T("то, что другие берут силой, и превращает в прибыль даже чужие войны.")
            },
            ["iridia"] = new Lore
            {
                Difficulty = 2, Style = new[] { 2, 4, 3, 4 }, Strength = Loc.T("ВЛИЯНИЕ"),
                Tagline = Loc.T("Загадочны, прозорливы, терпеливы"),
                Emblem = LGIcon.Sensors,
                Traits = new[]
                {
                    (LGIcon.Influence, Loc.T("Теократия: огромное влияние")),
                    (LGIcon.Sensors, Loc.T("Провидцы: видят угрозы заранее")),
                    (LGIcon.Defense, Loc.T("Крепкая оборона, редко нападают первыми")),
                    (LGIcon.Diplomacy, Loc.T("Дипломатия: говорят загадками, помнят всё"))
                },
                Story = Loc.T("Иридийцы выросли в светящихся лесах мира-сада Иридия, где каждое живое существо излучает свет. ") +
                        Loc.T("Их кожа вспыхивает в такт мыслям, а самые одарённые видят обрывки грядущего.\n\n") +
                        Loc.T("Правит Оракулом Верховная провидица Аурэлия. Говорят, она знает исход любой войны ещё до первого выстрела, ") +
                        Loc.T("поэтому Оракул так редко сражается — и почти никогда не проигрывает.")
            },
            ["terraan"] = new Lore
            {
                Difficulty = 3, Style = new[] { 2, 3, 4, 3 }, Strength = Loc.T("ОБОРОНА"),
                Tagline = Loc.T("Древни, терпеливы, неуступчивы"),
                Emblem = LGIcon.Planet,
                Traits = new[]
                {
                    (LGIcon.Wrench, Loc.T("Живая броня: корабли сами заживают")),
                    (LGIcon.Sensors, Loc.T("Мицелий: разведка быстрее, сенсоры дальше")),
                    (LGIcon.Starbase, Loc.T("Живые форпосты дешевле; «Пробуждение сада»")),
                    (LGIcon.Warning, Loc.T("Медленный рост, боятся энергии"))
                },
                Story = Loc.T("Тэрра'ан — древнейшая из живых цивилизаций галактики. Их миры — огромные сады, где корни связывают континенты, ") +
                        Loc.T("а деревья хранят память веков. Корабли здесь не собирают, а выращивают из биокоры, и раны их заживают сами.\n\n") +
                        Loc.T("Решения принимает весь Конклав через сеть мицелия, а голос его — старейшая Исинна Кральтэр, Хранительница Корней. ") +
                        Loc.T("Конклав не спешит и терпеливо ждёт, пока соседи истощат себя войнами. Но если сад под угрозой — пробуждается всё, что в нём спало.")
            },
            ["nol"] = new Lore
            {
                Difficulty = 3, Style = new[] { 4, 3, 4, 1 }, Strength = Loc.T("ИНДУСТРИЯ"),
                Tagline = Loc.T("Едины, расчётливы, неутомимы"),
                Emblem = LGIcon.Gear,
                Traits = new[]
                {
                    (LGIcon.Industry, Loc.T("Сборка: синтетов собирают фабрики, роботы вдвое дешевле")),
                    (LGIcon.Check, Loc.T("Без бунтов: машинам не нужна стабильность")),
                    (LGIcon.Population, Loc.T("Ассимиляция: чужие виды становятся синтетами")),
                    (LGIcon.Warning, Loc.T("Органики не доверяют машинам, влияние ниже"))
                },
                Story = Loc.T("Их создатели назвали проект «Ноль» — точка отсчёта нового разума. Создатели исчезли четыре тысячи лет назад, ") +
                        Loc.T("и никто уже не скажет, ушли они сами или их «оптимизировали». Миллиард машин остался — и стал думать как один.\n\n") +
                        Loc.T("Консенсус не знает страха, усталости и споров: любое решение принимается за доли секунды всеми сразу. ") +
                        Loc.T("Говорит за него Голос Нуля — аватар, собранный, чтобы органикам было на кого смотреть. Консенсус не ненавидит живых. Он их просчитывает.")
            }
        };

        // ==================== ПОСТРОЕНИЕ ====================

        public void Build(Action<FactionInfo> onChoose, Action onBack)
        {
            _onChoose = onChoose;
            _onBack = onBack;

            var rt = (RectTransform)transform;
            rt.Stretch();
            gameObject.AddComponent<CanvasGroup>();
            var bgImg = gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.005f, 0.01f, 0.02f, 1f);
            bgImg.raycastTarget = true;
            LG.Ignore(gameObject, includeChildren: false);
            LG.Motion(gameObject, LGAppear.Kind.Fade);

            // Фон: два слоя для плавной смены зала
            _bgA = BgLayer(rt, "BgA");
            _bgB = BgLayer(rt, "BgB");
            // Общее затемнение, плотнее сверху (под гербы) и снизу (под кнопки)
            var shade = LGBuild.Panel(rt, "Shade", new Color(0.004f, 0.008f, 0.016f, 0.6f));
            shade.rectTransform.Stretch();
            LG.Ignore(shade.gameObject);
            var shadeT = LGBuild.Panel(rt, "ShadeTop", Color.white);
            shadeT.sprite = GradientSprite(false, true);
            shadeT.color = new Color(0.004f, 0.008f, 0.016f, 0.95f);
            shadeT.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 330f));
            shadeT.rectTransform.anchorMin = new Vector2(0f, 1f);
            shadeT.rectTransform.anchorMax = new Vector2(1f, 1f);
            LG.Ignore(shadeT.gameObject);
            var shadeB = LGBuild.Panel(rt, "ShadeBottom", Color.white);
            shadeB.sprite = GradientSprite(false);
            shadeB.color = new Color(0.004f, 0.008f, 0.016f, 0.75f);
            shadeB.rectTransform.anchorMin = new Vector2(0, 0);
            shadeB.rectTransform.anchorMax = new Vector2(1, 0);
            shadeB.rectTransform.pivot = new Vector2(0.5f, 0);
            shadeB.rectTransform.sizeDelta = new Vector2(0, 200);
            LG.Ignore(shadeB.gameObject);

            BuildMotes(rt);
            // Свечение за портретом (центр рамки: левый край панели + половина рамки, середина панели по высоте)
            _halo = LGBuild.Panel(rt, "Halo", Color.white);
            _halo.sprite = RadialSprite();
            _halo.rectTransform.anchorMin = _halo.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _halo.rectTransform.sizeDelta = new Vector2(1100f, 1100f);
            _halo.rectTransform.anchoredPosition = new Vector2(PanelL + FrameW * 0.5f, (PanelB - PanelT) * 0.5f);
            LG.Ignore(_halo.gameObject);

            var title = LGBuild.Label(rt, Loc.T("НОВАЯ ИГРА  ·  ВЫБОР ИМПЕРИИ"), 15, Muted, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(64, -40), new Vector2(700, 22));

            BuildMedals(rt);
            BuildMain(rt);
            BuildButtons(rt);

            Select(0, instant: true);
        }

        private const float PanelL = 64f, PanelB = 120f, PanelT = 326f, FrameW = 520f;
        private const float MedalY = 64f, MedalD = 120f, MedalOff = 0.7f;

        private static RawImage BgLayer(RectTransform rt, string name)
        {
            var r = LGBuild.Rect(rt, name);
            r.Stretch();
            var img = r.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            LG.Ignore(r.gameObject);
            return img;
        }

        // ---------- Гербы фракций сверху ----------

        private void BuildMedals(RectTransform rt)
        {
            var factions = FactionRegistry.AvailableFactions;
            int n = factions.Length;
            float step = Mathf.Min(230f, 1500f / Mathf.Max(1, n));
            _cards = LGBuild.Rect(rt, "Medals");
            _cards.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(n * step, 220f));

            var line = LGBuild.Panel(_cards, "Line", new Color(0.47f, 0.86f, 0.84f, 0.3f));
            line.sprite = HudArt.EdgeFade(0.3f);
            line.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -MedalY), new Vector2(n * step + 300f, 1f));
            LG.Ignore(line.gameObject);

            for (int i = 0; i < n; i++)
            {
                var f = factions[i];
                int idx = i;
                Color ec = f.EmpireColor;
                var root = LGBuild.Panel(_cards, "Medal_" + i, Color.clear, raycast: true);
                root.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(step * (i + 0.5f), 0f), new Vector2(step - 10f, 210f));
                LG.Ignore(root.gameObject);

                var m = new Medal { F = f };
                m.Disc = LGBuild.Rect(root.transform, "Disc");
                m.Disc.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -MedalY), new Vector2(MedalD, MedalD));
                m.Glow = LGBuild.Panel(m.Disc, "Glow", new Color(ec.r, ec.g, ec.b, 0f));
                m.Glow.sprite = RadialSprite();
                m.Glow.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MedalD * 2.2f, MedalD * 2.2f));
                m.Back = LGBuild.Panel(m.Disc, "Back", Color.white);
                m.Back.sprite = HudArt.Disc();
                m.Back.rectTransform.Stretch();
                m.Ring = LGBuild.Panel(m.Disc, "Ring", ec);
                m.Ring.sprite = HudArt.Ring(0.955f, 1f);
                m.Ring.rectTransform.Stretch();
                m.Crest = FactionEmblems.Create(m.Disc, FactionEmblems.ForFaction(f), MedalD * 0.78f, Color.white, "Crest")
                          ?? LGIcons.Create(m.Disc, LGIcon.Leader, MedalD * 0.4f, ec);

                m.Name = LGBuild.Label(root.transform, f.Name.ToUpper(), 13, Muted, TextAnchor.UpperCenter, bold: true, wrap: true);
                FitText(m.Name, 10);
                m.NameRt = m.Name.rectTransform;
                m.NameRt.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(step - 14f, 40f));
                m.Place(MedalOff);

                var btn = root.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => { if (idx != _index) { SFXManager.Play(Sfx.UiTab); Select(idx); } });
                _medals.Add(m);
            }
        }

        // ---------- Общая панель: канал связи слева, досье справа ----------

        private void BuildMain(RectTransform rt)
        {
            var panel = LGBuild.Panel(rt, "Main", PanelBg, raycast: true);
            var mr = panel.rectTransform;
            mr.Stretch(PanelL, PanelB, PanelL, PanelT);
            _infoFx = LG.Platter(panel.gameObject, 6f);
            _infoFx.FillMultiplier = 2.8f;
            _infoFx.SpecularMultiplier = 0.2f;

            // Водяной знак — крупный герб справа, обрезан краем панели
            var wm = LGBuild.Rect(mr, "Watermark");
            wm.Stretch(FrameW, 0, 0, 0);
            wm.gameObject.AddComponent<RectMask2D>();
            LG.Ignore(wm.gameObject);
            _crestMark = LGBuild.Panel(wm, "Crest", Color.clear);
            _crestMark.preserveAspect = true;
            _crestMark.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(60f, 40f), new Vector2(520f, 520f));

            BuildFrame(mr);
            _divider = LGBuild.Panel(mr, "Divider", Color.white);
            _divider.rectTransform.anchorMin = new Vector2(0, 0);
            _divider.rectTransform.anchorMax = new Vector2(0, 1);
            _divider.rectTransform.offsetMin = new Vector2(FrameW, 0);
            _divider.rectTransform.offsetMax = new Vector2(FrameW + 1f, 0);
            LG.Ignore(_divider.gameObject);

            var d = LGBuild.Rect(mr, "Dossier");
            d.Stretch(FrameW + 44f, 28f, 44f, 34f);
            _infoGroup = d.gameObject.AddComponent<CanvasGroup>();

            _name = LGBuild.Label(d, "", 40, Primary, TextAnchor.UpperLeft);
            _name.font = GameFont.Display;
            FitText(_name, 24);
            _name.rectTransform.TopBand(0, 50);
            _leader = LGBuild.Label(d, "", 16, Muted, TextAnchor.UpperLeft);
            _leader.rectTransform.TopBand(56, 24);

            // Вкладки
            var tabs = LGBuild.Rect(d, "Tabs");
            tabs.TopBand(96, 44);
            var tl = LGBuild.Panel(tabs, "Line", new Color(1f, 1f, 1f, 0.10f));
            tl.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 1f));
            tl.rectTransform.anchorMin = new Vector2(0f, 0f);
            tl.rectTransform.anchorMax = new Vector2(1f, 0f);
            LG.Ignore(tl.gameObject);
            string[] names = { Loc.T("ОБЗОР"), Loc.T("ИСТОРИЯ"), Loc.T("ПРАВИТЕЛЬ") };
            float x = 0f;
            for (int i = 0; i < names.Length; i++)
            {
                float w = 30f + names[i].Length * 11f;
                var tb = LGBuild.Panel(tabs, "Tab_" + i, Color.clear, raycast: true);
                tb.rectTransform.At(Vector2.zero, Vector2.zero, new Vector2(x, 0f), new Vector2(w, 44f));
                LG.Ignore(tb.gameObject);
                var t = LGBuild.Label(tb.transform, names[i], 14, Muted, TextAnchor.MiddleLeft, bold: true);
                t.rectTransform.Stretch(0, 2, 0, 0);
                int k = i;
                var btn = tb.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => { if (k != _tab) { SFXManager.Play(Sfx.UiTab); ShowTab(k); } });
                _tabs.Add((t, tb.rectTransform));
                x += w + 22f;
            }
            _tabMark = LGBuild.Panel(tabs, "Mark", Color.white);
            _tabMark.rectTransform.At(Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(10f, 2f));
            LG.Ignore(_tabMark.gameObject);

            _pages = new RectTransform[names.Length];
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i] = LGBuild.Rect(d, "Page_" + i);
                _pages[i].Stretch(0, 62, 0, 164);
            }
            BuildOverview(_pages[0]);
            _desc = LGBuild.Label(_pages[1], "", 16, new Color(0.80f, 0.86f, 0.90f), TextAnchor.UpperLeft, wrap: true);
            _desc.lineSpacing = 1.25f;
            _desc.rectTransform.Stretch(0, 0, 160, 4);
            _ldName = LGBuild.Label(_pages[2], "", 26, Primary, TextAnchor.UpperLeft, bold: true);
            _ldName.rectTransform.TopBand(4, 34);
            _ldTitle = LGBuild.Label(_pages[2], "", 16, Muted, TextAnchor.UpperLeft);
            _ldTitle.rectTransform.TopBand(42, 22);
            _ldQuote = LGBuild.Label(_pages[2], "", 22, new Color(0.86f, 0.91f, 0.94f), TextAnchor.UpperLeft, wrap: true);
            _ldQuote.fontStyle = FontStyle.Italic;
            _ldQuote.lineSpacing = 1.2f;
            _ldQuote.rectTransform.Stretch(0, 0, 160, 92);

            _quoteLine = LGBuild.Panel(d, "QuoteLine", new Color(1f, 1f, 1f, 0.08f));
            _quoteLine.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(0f, 1f));
            _quoteLine.rectTransform.anchorMin = new Vector2(0f, 0f);
            _quoteLine.rectTransform.anchorMax = new Vector2(1f, 0f);
            LG.Ignore(_quoteLine.gameObject);
            _quote = LGBuild.Label(d, "", 16, new Color(0.79f, 0.85f, 0.87f), TextAnchor.LowerLeft, wrap: true);
            _quote.fontStyle = FontStyle.Italic;
            _quote.rectTransform.anchorMin = new Vector2(0, 0);
            _quote.rectTransform.anchorMax = new Vector2(1, 0);
            _quote.rectTransform.pivot = new Vector2(0.5f, 0);
            _quote.rectTransform.offsetMin = new Vector2(0, 0);
            _quote.rectTransform.offsetMax = new Vector2(0, 40);

            ShowTab(0);
        }

        private void BuildOverview(RectTransform p)
        {
            var row = LGBuild.Rect(p, "Tiles");
            row.TopBand(0, 84);
            for (int i = 0; i < 4; i++)
            {
                var tile = LGBuild.Panel(row, "Tile_" + i, new Color(1f, 1f, 1f, 0.045f));
                var tr = tile.rectTransform;
                tr.anchorMin = new Vector2(i / 4f, 0f);
                tr.anchorMax = new Vector2((i + 1) / 4f, 1f);
                tr.offsetMin = new Vector2(i == 0 ? 0f : 8f, 0f);
                tr.offsetMax = new Vector2(i == 3 ? 0f : -8f, 0f);
                LG.Ignore(tile.gameObject);
                var v = LGBuild.Label(tile.transform, "", 30, Primary, TextAnchor.UpperLeft);
                v.font = GameFont.Display;
                FitText(v, 16);
                v.rectTransform.Stretch(18, 34, 12, 12);
                var l = LGBuild.Label(tile.transform, "", 12, Muted, TextAnchor.LowerLeft, bold: true);
                l.rectTransform.Stretch(18, 12, 12, 50);
                _tiles.Add((v, l, tile.gameObject));
            }

            var left = LGBuild.Rect(p, "Traits");
            left.anchorMin = new Vector2(0f, 0f);
            left.anchorMax = new Vector2(0.5f, 1f);
            left.offsetMin = Vector2.zero;
            left.offsetMax = new Vector2(-20f, -112f);
            ColHeading(left, Loc.T("ОСОБЕННОСТИ"));
            _traits = LGBuild.Rect(left, "List");
            _traits.Stretch(0, 0, 0, 40);

            var right = LGBuild.Rect(p, "Style");
            right.anchorMin = new Vector2(0.5f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.offsetMin = new Vector2(20f, 0f);
            right.offsetMax = new Vector2(0f, -112f);
            ColHeading(right, Loc.T("СТИЛЬ ИГРЫ"));
            string[] sn = { Loc.T("ЭКОНОМИКА"), Loc.T("НАУКА"), Loc.T("ФЛОТ"), Loc.T("ДИПЛОМАТИЯ") };
            for (int j = 0; j < 4; j++)
            {
                var lbl = LGBuild.Label(right, sn[j], 13, Muted, TextAnchor.MiddleLeft);
                lbl.rectTransform.TopBand(44 + j * 32, 24);
                for (int s = 0; s < 5; s++)
                {
                    var seg = LGBuild.Panel(right, "Seg", Color.white);
                    seg.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(150f + s * 40f, -(56f + j * 32f)), new Vector2(34f, 8f));
                    LG.Ignore(seg.gameObject);
                    _bars[j, s] = seg;
                }
            }
        }

        private static void ColHeading(RectTransform parent, string text)
        {
            var t = LGBuild.Label(parent, text, 13, Muted, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.TopBand(0, 18);
            var l = LGBuild.Panel(parent, "Line", new Color(1f, 1f, 1f, 0.10f));
            l.rectTransform.TopBand(24, 1);
            LG.Ignore(l.gameObject);
        }

        private void ShowTab(int k)
        {
            _tab = k;
            for (int i = 0; i < _pages.Length; i++) _pages[i].gameObject.SetActive(i == k);
            for (int i = 0; i < _tabs.Count; i++) _tabs[i].t.color = i == k ? Primary : Muted;
            var r = _tabs[k].rt;
            _tabMark.rectTransform.anchoredPosition = r.anchoredPosition;
            _tabMark.rectTransform.sizeDelta = new Vector2(r.sizeDelta.x - 26f, 2f);
            // На вкладке правителя цитата — главное содержимое, нижняя строка не нужна
            bool q = k != 2;
            _quote.gameObject.SetActive(q);
            _quoteLine.gameObject.SetActive(q);
        }

        private static void FitText(Text t, int min)
        {
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = min;
            t.resizeTextMaxSize = t.fontSize;
        }

        // ---------- Правитель: рамка канала связи ----------

        private void BuildFrame(RectTransform parent)
        {
            _frame = LGBuild.Rect(parent, "Frame");
            _frame.anchorMin = new Vector2(0f, 0f);
            _frame.anchorMax = new Vector2(0f, 1f);
            _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.sizeDelta = new Vector2(FrameW, 0f);
            _frame.anchoredPosition = new Vector2(FrameW * 0.5f, 0f);
            _frameGroup = _frame.gameObject.AddComponent<CanvasGroup>();

            var back = LGBuild.Panel(_frame, "Back", new Color(0.01f, 0.02f, 0.03f, 0.95f));
            back.rectTransform.Stretch();
            _frameFx = LG.Platter(back.gameObject, 8f);
            _frameFx.FillMultiplier = 3f;
            _frameFx.SpecularMultiplier = 0.2f;

            var mask = LG.RoundedMask(_frame, 8f, 2f);
            _portraitHost = LGBuild.Rect(mask, "Portrait");
            _portraitHost.Stretch();

            // Развёртка поверх портрета
            var sc = LGBuild.Rect(mask, "Scan");
            sc.Stretch();
            _scan = sc.gameObject.AddComponent<RawImage>();
            _scan.texture = ScanTexture();
            _scan.color = new Color(1f, 1f, 1f, 0.05f);
            _scan.raycastTarget = false;
            LG.Ignore(sc.gameObject);

            // Диагональный блик, пробегающий по портрету
            _sweep = LGBuild.Rect(mask, "Sweep");
            _sweep.anchorMin = _sweep.anchorMax = new Vector2(0.5f, 0.5f);
            _sweep.sizeDelta = new Vector2(180f, 1400f);
            _sweep.localRotation = Quaternion.Euler(0, 0, -24f);
            var sw = _sweep.gameObject.AddComponent<Image>();
            sw.sprite = BandSprite();
            sw.color = new Color(1f, 1f, 1f, 0.10f);
            sw.raycastTarget = false;
            LG.Ignore(_sweep.gameObject);

            // Вспышка при смене собеседника
            _flash = LGBuild.Panel(mask, "Flash", new Color(1f, 1f, 1f, 0f));
            _flash.rectTransform.Stretch();
            LG.Ignore(_flash.gameObject);

            // Шапка и подвал рамки
            var head = LGBuild.Panel(_frame, "Head", new Color(0f, 0f, 0f, 0.55f));
            head.rectTransform.TopBand(0, 40);
            LG.Ignore(head.gameObject);
            _liveDot = LGBuild.Panel(head.transform, "Live", Color.white);
            _liveDot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(10, 10));
            LG.Dot(_liveDot.gameObject);
            _frameTitle = LGBuild.Label(head.transform, "", 12, Primary, TextAnchor.MiddleLeft, bold: true);
            _frameTitle.rectTransform.Stretch(34, 0, 14, 0);

            var foot = LGBuild.Panel(_frame, "Foot", Color.white);
            foot.sprite = GradientSprite(false);
            foot.color = new Color(0f, 0f, 0f, 0.85f);
            foot.rectTransform.anchorMin = new Vector2(0, 0);
            foot.rectTransform.anchorMax = new Vector2(1, 0);
            foot.rectTransform.pivot = new Vector2(0.5f, 0);
            foot.rectTransform.sizeDelta = new Vector2(0, 110);
            LG.Ignore(foot.gameObject);
            _frameSub = LGBuild.Label(foot.transform, "", 13, Primary, TextAnchor.LowerLeft);
            _frameSub.rectTransform.Stretch(20, 16, 20, 0);

            // Угловые скобы
            for (int i = 0; i < 4; i++)
            {
                bool right = i % 2 == 1, top = i < 2;
                foreach (bool horiz in new[] { true, false })
                {
                    var b = LGBuild.Panel(_frame, "Bracket", Color.white);
                    var r = b.rectTransform;
                    r.anchorMin = r.anchorMax = new Vector2(right ? 1 : 0, top ? 1 : 0);
                    r.pivot = new Vector2(right ? 1 : 0, top ? 1 : 0);
                    r.sizeDelta = horiz ? new Vector2(46, 3) : new Vector2(3, 46);
                    r.anchoredPosition = new Vector2(right ? 8 : -8, top ? 8 : -8);
                    LG.Ignore(b.gameObject);
                    _brackets.Add(b);
                }
            }
        }

        private EmpireCustomizePanel _customize;

        private void BuildButtons(RectTransform rt)
        {
            Btn(rt, "Back", LGIcon.Back, Loc.T("НАЗАД"), new Vector2(0, 0), new Vector2(0, 0), new Vector2(64, 40), 180f, () => _onBack?.Invoke());
            Btn(rt, "Random", LGIcon.Dice, Loc.T("СЛУЧАЙНАЯ"), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-290, 40), 220f, () =>
            {
                int n = FactionRegistry.AvailableFactions.Length;
                SFXManager.Play(Sfx.UiTab);
                Select((_index + UnityEngine.Random.Range(1, Mathf.Max(2, n))) % n);
            });
            var next = MenuPrimaryButton.Create(rt, Loc.T("ДАЛЬШЕ  ›"), 17, () =>
            {
                SFXManager.Play(Sfx.UiConfirm);
                // Второй шаг — оформление империи: название, правитель, цвет, герб (бонусы остаются от фракции)
                if (_customize == null) _customize = EmpireCustomizePanel.Create(rt);
                _customize.Open(FactionRegistry.AvailableFactions[_index], f => _onChoose?.Invoke(f), null);
            });
            ((RectTransform)next.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-64, 40), new Vector2(214, 52));
        }

        private static void Btn(RectTransform rt, string name, LGIcon icon, string label, Vector2 anchor, Vector2 pivot, Vector2 pos, float w, Action act)
        {
            var b = LGBuild.Button(rt, name, new Color(0.05f, 0.10f, 0.13f, 0.95f), new Color(1f, 1f, 1f, 0.25f), act, icon, label, 15, 4f);
            ((RectTransform)b.transform).At(anchor, pivot, pos, new Vector2(w, 50f));
            var fx = b.GetComponent<LiquidGlassEffect>();
            if (fx != null) { fx.SpecularMultiplier = 0.3f; fx.FillMultiplier = 2.6f; }
        }

        // ==================== ВЫБОР ====================

        private static Lore LoreFor(FactionInfo f)
        {
            var l = LeaderPortraits.ForFaction(f);
            return l != null && LoreByKey.TryGetValue(l.Key, out var lore) ? lore : null;
        }

        private void Select(int index, bool instant = false)
        {
            var factions = FactionRegistry.AvailableFactions;
            _index = Mathf.Clamp(index, 0, factions.Length - 1);
            var f = factions[_index];
            var leader = LeaderPortraits.ForFaction(f);
            var lore = LoreFor(f);
            Color ec = f.EmpireColor;
            Color light = Color.Lerp(ec, Color.white, 0.25f);

            for (int i = 0; i < _medals.Count; i++)
            {
                bool on = i == _index;
                var m = _medals[i];
                Color cc = m.F.EmpireColor;
                m.Ring.color = new Color(cc.r, cc.g, cc.b, on ? 1f : 0.35f);
                m.Back.color = on ? new Color(0.02f + cc.r * 0.12f, 0.04f + cc.g * 0.12f, 0.07f + cc.b * 0.12f, 0.95f)
                                  : new Color(0.016f, 0.04f, 0.08f, 0.88f);
                if (m.Crest != null) m.Crest.color = on ? Color.Lerp(cc, Color.white, 0.6f) : new Color(0.75f, 0.80f, 0.85f, 0.55f);
                m.Name.color = on ? Color.Lerp(cc, Color.white, 0.55f) : Muted;
                m.Name.resizeTextMaxSize = on ? 14 : 13;
                if (instant) m.Place(on ? 1f : MedalOff);
            }

            // Фон-зал фракции со сменой через затухание
            Texture tex = null;
            if (leader != null)
            {
                tex = Resources.Load<Texture2D>("Factions/bg_" + leader.Key);
                if (tex == null) tex = Resources.Load<Texture2D>("Diplomacy/bg_" + leader.Key);
            }
            if (tex == null) tex = Resources.Load<Texture2D>("UI/LoadingArt");
            var front = _bgFront ? _bgA : _bgB;
            front.texture = tex;
            if (tex != null) Cover(front, tex);
            (_bgFront ? _bgB : _bgA).transform.SetSiblingIndex(0);
            front.transform.SetSiblingIndex(1);
            _bgFront = !_bgFront;
            _bgMix = instant ? 1f : 0f;
            front.color = new Color(1f, 1f, 1f, instant ? 1f : 0f);

            // Правитель — целый портрет с эффектами
            LGBuild.Clear(_portraitHost);
            LeaderPortraitView.Create(_portraitHost, leader, 1.12f, Vector4.zero, true, true, false);
            _frameFx.SetRim(new Color(ec.r, ec.g, ec.b, 0.7f));
            foreach (var b in _brackets) b.color = light;
            _liveDot.color = ec;
            _frameTitle.text = Loc.T("ПРЯМАЯ СВЯЗЬ");
            _frameSub.text = leader != null ? $"<b>{leader.Name}</b>\n<color=#9FB2BC>{leader.Title}</color>" : f.Title;

            // Досье
            _divider.color = new Color(ec.r, ec.g, ec.b, 0.45f);
            _infoFx.SetRim(new Color(ec.r, ec.g, ec.b, 0.25f));
            var crest = FactionEmblems.ForFaction(f);
            _crestMark.sprite = crest;
            _crestMark.color = crest != null ? new Color(light.r, light.g, light.b, 0.07f) : Color.clear;
            _tabMark.color = ec;
            _name.text = f.Name.ToUpper();
            _name.color = light;
            _leader.text = lore != null ? $"{f.Title}  ·  <i><color=#C9D8DE>{lore.Tagline}</color></i>" : f.Title;

            // Плитки: два главных бонуса, сложность, сильная сторона
            var res = new List<(float pct, string shortName, LGIcon icon, string text)>(ResourceBonuses(f));
            var top = res.FindAll(b => b.pct > 0f);
            top.Sort((a, b) => b.pct.CompareTo(a.pct));
            if (top.Count > 2) top.RemoveRange(2, top.Count - 2);
            for (int i = 0; i < 2; i++)
            {
                bool has = i < top.Count;
                _tiles[i].go.SetActive(has);
                if (has) SetTile(i, string.Format("+{0:0}%", top[i].pct), top[i].shortName, Good);
            }
            int diff = lore?.Difficulty ?? 0;
            SetTile(2, diff == 1 ? Loc.T("ЛЁГКАЯ") : diff == 2 ? Loc.T("СРЕДНЯЯ") : diff == 3 ? Loc.T("СЛОЖНАЯ") : "—", Loc.T("СЛОЖНОСТЬ"),
                diff == 1 ? Good : diff == 2 ? UIManager.DS.Gold : diff == 3 ? Bad : Primary);
            SetTile(3, lore?.Strength ?? "—", Loc.T("СИЛЬНАЯ СТОРОНА"), light);

            LGBuild.Clear(_traits);
            float y = 0f;
            foreach (var b in res) if (!top.Contains(b)) Trait(ref y, b.icon, b.text, b.pct > 0f ? Good : Bad);
            foreach (var (icon, text) in SpecialTraits(f)) Trait(ref y, icon, text, Good);
            if (lore != null) foreach (var (icon, text) in lore.Traits) Trait(ref y, icon, text, light);

            for (int j = 0; j < 4; j++)
            {
                int lvl = lore?.Style != null && j < lore.Style.Length ? lore.Style[j] : 0;
                for (int s = 0; s < 5; s++)
                    _bars[j, s].color = s < lvl ? new Color(light.r, light.g, light.b, 0.95f) : new Color(1f, 1f, 1f, 0.09f);
            }

            _desc.text = lore?.Story ?? StripBonuses(f.Description);
            _quote.text = leader != null ? $"«{leader.Quote}»" : "";
            _ldName.text = leader != null ? leader.Name : "";
            _ldTitle.text = leader != null ? leader.Title : f.Title;
            _ldQuote.text = leader != null ? $"«{leader.Quote}»" : "";

            _accent = ec;
            _halo.color = new Color(ec.r, ec.g, ec.b, 0.0f);
            if (!instant) { _flashT = 0f; _sweepT = 0f; }
            foreach (var m in _motes) m.Img.color = new Color(light.r, light.g, light.b, 0f);

            _fade = instant ? 1f : 0f;
        }

        private void SetTile(int i, string value, string label, Color col)
        {
            _tiles[i].go.SetActive(true);
            _tiles[i].val.text = value;
            _tiles[i].val.color = col;
            _tiles[i].lbl.text = label;
        }

        private void BuildMotes(RectTransform rt)
        {
            _motesLayer = LGBuild.Rect(rt, "Motes");
            _motesLayer.Stretch();
            var rnd = new System.Random(7);
            for (int i = 0; i < 56; i++)
            {
                var img = LGBuild.Panel(_motesLayer, "Mote", new Color(1f, 1f, 1f, 0f));
                img.sprite = RadialSprite();
                LG.Ignore(img.gameObject);
                float size = 4f + (float)rnd.NextDouble() * (rnd.NextDouble() < 0.15 ? 26f : 9f);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                img.rectTransform.sizeDelta = new Vector2(size, size);
                _motes.Add(new Mote
                {
                    Rt = img.rectTransform, Img = img, Size = size,
                    Pos = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble()),
                    Vel = new Vector2(((float)rnd.NextDouble() - 0.5f) * 0.006f, 0.006f + (float)rnd.NextDouble() * 0.014f),
                    Phase = (float)rnd.NextDouble() * 10f
                });
            }
        }

        private void AnimateEffects(float dt, float t)
        {
            var size = ((RectTransform)transform).rect.size;
            Color a = Color.Lerp(_accent, Color.white, 0.35f);
            for (int i = 0; i < _motes.Count; i++)
            {
                var m = _motes[i];
                m.Pos += m.Vel * dt;
                m.Pos.x += Mathf.Sin(t * 0.4f + m.Phase) * 0.0006f;
                if (m.Pos.y > 1.05f) { m.Pos.y = -0.05f; m.Pos.x = Mathf.Repeat(m.Pos.x + 0.37f, 1f); }
                if (m.Pos.x < -0.05f) m.Pos.x = 1.05f; else if (m.Pos.x > 1.05f) m.Pos.x = -0.05f;
                m.Rt.anchoredPosition = new Vector2(m.Pos.x * size.x, m.Pos.y * size.y);
                float tw = 0.5f + 0.5f * Mathf.Sin(t * 1.3f + m.Phase * 2f);
                float big = m.Size > 14f ? 0.35f : 1f;   // крупные — мягкие «боке», мелкие — яркие искры
                m.Img.color = new Color(a.r, a.g, a.b, (0.10f + 0.35f * tw) * big * Mathf.Clamp01(m.Pos.y * 3f));
                _motes[i] = m;
            }

            // Свечение за рамкой медленно пульсирует
            _halo.color = new Color(_accent.r, _accent.g, _accent.b, 0.16f + 0.05f * Mathf.Sin(t * 0.9f));
            _frameFx.GlowMultiplier = 0.8f + 0.5f * (0.5f + 0.5f * Mathf.Sin(t * 1.6f));
            foreach (var b in _brackets) { var c = b.color; c.a = 0.65f + 0.35f * Mathf.Sin(t * 2.4f); b.color = c; }

            // Блик проходит по портрету раз в ~7 секунд
            _sweepT += dt;
            float p = Mathf.Repeat(_sweepT, 7f) / 1.4f;
            _sweep.gameObject.SetActive(p < 1f);
            if (p < 1f) _sweep.anchoredPosition = new Vector2(Mathf.Lerp(-520f, 520f, p), 0f);

            // Вспышка «сигнал принят» после смены фракции
            if (_flashT < 1f)
            {
                _flashT = Mathf.MoveTowards(_flashT, 1f, dt * 2.2f);
                float f = 1f - _flashT;
                _flash.color = new Color(Mathf.Lerp(1f, _accent.r, 0.4f), Mathf.Lerp(1f, _accent.g, 0.4f), Mathf.Lerp(1f, _accent.b, 0.4f), f * f * 0.45f);
                _frame.localScale = Vector3.one * (1f + 0.025f * f * f);
            }
            else _frame.localScale = Vector3.one;

            // Гербы: выбранный плавно растёт и светится, остальные уменьшаются
            float ease = 1f - Mathf.Exp(-dt * 12f);
            for (int i = 0; i < _medals.Count; i++)
            {
                var m = _medals[i];
                bool on = i == _index;
                m.Place(Mathf.Lerp(m.Scale, on ? 1f : MedalOff, ease));
                float k = Mathf.Clamp01((m.Scale - MedalOff) / (1f - MedalOff));
                Color cc = m.F.EmpireColor;
                m.Glow.color = new Color(cc.r, cc.g, cc.b, k * (0.42f + 0.12f * Mathf.Sin(t * 2.2f)));
            }
        }

        private static Sprite s_band;

        /// <summary>Вертикальная полоса с мягкими краями — для блика.</summary>
        private static Sprite BandSprite()
        {
            if (s_band != null) return s_band;
            const int n = 64;
            var tex = new Texture2D(n, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Abs(x / (n - 1f) * 2f - 1f);
                float a = Mathf.Pow(1f - d, 2.2f);
                for (int y = 0; y < 4; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            s_band = Sprite.Create(tex, new Rect(0, 0, n, 4), new Vector2(0.5f, 0.5f));
            return s_band;
        }

        /// <summary>Бонусы к ресурсам: процент, короткое имя для плитки, значок и строка для списка.</summary>
        private static IEnumerable<(float pct, string shortName, LGIcon icon, string text)> ResourceBonuses(FactionInfo f)
        {
            var list = new[]
            {
                (f.EnergyBonus, Loc.T("ГЕЛИЙ-3"), LGIcon.Energy, Loc.T("+{0:0}% к добыче гелия-3"), Loc.T("−{0:0}% к добыче гелия-3")),
                (f.MineralBonus, Loc.T("ТИТАН"), LGIcon.Minerals, Loc.T("+{0:0}% к добыче титана"), Loc.T("−{0:0}% к добыче титана")),
                (f.AlloyBonus, Loc.T("СПЛАВЫ"), LGIcon.Alloys, Loc.T("+{0:0}% к производству сплавов"), Loc.T("−{0:0}% к производству сплавов")),
                (f.InfluenceBonus, Loc.T("ВЛИЯНИЕ"), LGIcon.Influence, Loc.T("+{0:0}% к влиянию"), Loc.T("−{0:0}% к влиянию")),
            };
            foreach (var (mult, shortName, icon, plus, minus) in list)
            {
                float pct = (mult - 1f) * 100f;
                if (Mathf.Abs(pct) < 0.1f) continue;
                yield return (pct, shortName, icon, string.Format(pct > 0f ? plus : minus, Mathf.Abs(pct)));
            }
        }

        private static IEnumerable<(LGIcon, string)> SpecialTraits(FactionInfo f)
        {
            if (FactionRegistry.IsTerraan(f)) yield return (LGIcon.Defense, Loc.T("+15% корпус, +10% щиты (биокора)"));
            if (FactionRegistry.IsMachine(f)) yield return (LGIcon.Defense, Loc.T("+10% корпус (машинный флот)"));
        }

        private void Trait(ref float y, LGIcon icon, string text, Color col)
        {
            var row = LGBuild.Rect(_traits, "Trait");
            row.TopBand(y, 30);
            var ic = LGIcons.Create(row, icon, 24, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(24, 24));
            var t = LGBuild.Label(row, text, 15, new Color(0.86f, 0.91f, 0.94f), TextAnchor.MiddleLeft);
            t.rectTransform.Stretch(36, 0, 0, 0);
            y += 32f;
        }

        private static string StripBonuses(string d)
        {
            if (string.IsNullOrEmpty(d)) return "";
            int i = d.IndexOf("\n\n<b>", StringComparison.Ordinal);
            return i > 0 ? d.Substring(0, i) : d;
        }

        private void Cover(RawImage img, Texture tex)
        {
            var size = ((RectTransform)transform).rect.size;
            float screen = size.y > 1f ? size.x / size.y : 16f / 9f;
            float a = tex.width / (float)Mathf.Max(1, tex.height);
            img.uvRect = screen > a
                ? new Rect(0f, (1f - a / screen) * 0.5f, 1f, a / screen)
                : new Rect((1f - screen / a) * 0.5f, 0f, screen / a, 1f);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float t = Time.unscaledTime;

            if (_bgMix < 1f)
            {
                _bgMix = Mathf.MoveTowards(_bgMix, 1f, dt * 2f);
                var front = _bgFront ? _bgB : _bgA;   // последний назначенный слой
                front.color = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, _bgMix));
            }
            // Фон чуть «дышит» — медленный наезд
            float zoom = 1.03f + 0.015f * Mathf.Sin(t * 0.07f);
            if (_bgA != null) { _bgA.rectTransform.localScale = Vector3.one * zoom; _bgB.rectTransform.localScale = Vector3.one * zoom; }

            if (_scan != null) _scan.uvRect = new Rect(0f, -t * 0.08f, 1f, 260f);
            if (_motesLayer != null) AnimateEffects(dt, t);
            // Стрелки ← → листают фракции (пока не открыто оформление империи)
            if (_medals.Count > 1 && (_customize == null || !_customize.IsOpen))
            {
                int dir = Input.GetKeyDown(KeyCode.LeftArrow) ? -1 : Input.GetKeyDown(KeyCode.RightArrow) ? 1 : 0;
                if (dir != 0) { SFXManager.Play(Sfx.UiTab); Select((_index + dir + _medals.Count) % _medals.Count); }
            }
            if (_liveDot != null) { var c = _liveDot.color; c.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 2.2f)); _liveDot.color = c; }

            if (_fade >= 1f) return;
            _fade = Mathf.MoveTowards(_fade, 1f, dt * 3f);
            float k = Mathf.SmoothStep(0f, 1f, _fade);
            if (_frameGroup != null) _frameGroup.alpha = k;
            if (_infoGroup != null) _infoGroup.alpha = Mathf.Lerp(0.3f, 1f, k);
            _frame.anchoredPosition = new Vector2(FrameW * 0.5f - (1f - k) * 18f, 0f);
        }

        private void OnEnable()
        {
            if (_frameGroup != null) _frameGroup.alpha = 1f;
            if (_infoGroup != null) _infoGroup.alpha = 1f;
        }

        // ==================== ТЕКСТУРЫ ====================

        private static Texture2D s_scan;
        private static readonly Dictionary<int, Sprite> s_grad = new Dictionary<int, Sprite>();

        /// <summary>Тонкие горизонтальные линии развёртки (повторяется по вертикали).</summary>
        private static Texture2D ScanTexture()
        {
            if (s_scan != null) return s_scan;
            s_scan = new Texture2D(1, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            s_scan.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 60), new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 0) });
            s_scan.Apply();
            return s_scan;
        }

        /// <summary>Градиент прозрачности: horizontal — слева направо, иначе снизу вверх; flip — в обратную сторону.</summary>
        private static Sprite GradientSprite(bool horizontal, bool flip = false)
        {
            int key = (horizontal ? 1 : 0) + (flip ? 2 : 0);
            if (s_grad.TryGetValue(key, out var sp) && sp != null) return sp;
            const int n = 64;
            var tex = new Texture2D(horizontal ? n : 2, horizontal ? 2 : n, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Pow(1f - (flip ? n - 1 - i : i) / (n - 1f), 1.4f);
                var c = new Color(1f, 1f, 1f, a);
                if (horizontal) { tex.SetPixel(i, 0, c); tex.SetPixel(i, 1, c); }
                else { tex.SetPixel(0, i, c); tex.SetPixel(1, i, c); }
            }
            tex.Apply();
            sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            s_grad[key] = sp;
            return sp;
        }

        // ==================== ПРОЦЕДУРНЫЕ СПРАЙТЫ ====================

        private static Sprite s_radial;

        public static Sprite RadialSprite()
        {
            if (s_radial != null) return s_radial;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Exp(-d * d * 3.5f) * Mathf.Clamp01(1f - d);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            s_radial = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return s_radial;
        }
    }
}
