using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Выбор цивилизации. Фон — зал фракции (Resources/Factions/bg_&lt;ключ&gt; или Diplomacy/bg_&lt;ключ&gt;),
    /// плавно сменяется при выборе. Слева — досье фракции; справа — правитель в рамке «канала связи»
    /// (целый портрет с живыми эффектами, угловые скобы, развёртка, индикатор эфира);
    /// внизу по центру — карточки фракций, по краям — «Назад», «Случайная империя», «Дальше».
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
        private RectTransform _frame, _portraitHost, _traits, _cards;
        private LiquidGlassEffect _frameFx, _infoFx;
        private Image _accentBar, _emblem, _emblemIcon, _liveDot;
        private readonly List<Image> _brackets = new List<Image>();
        private RawImage _scan;
        private Text _name, _leader, _tagline, _desc, _quote, _frameTitle, _frameSub;
        private readonly List<(Image bg, LiquidGlassEffect fx, Text name, FactionInfo f, RectTransform rt)> _cardUi =
            new List<(Image, LiquidGlassEffect, Text, FactionInfo, RectTransform)>();
        private CanvasGroup _infoGroup, _frameGroup;

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
        }

        private static readonly Dictionary<string, Lore> LoreByKey = new Dictionary<string, Lore>
        {
            ["xarn"] = new Lore
            {
                Tagline = "Дисциплинированы, воинственны, непреклонны",
                Emblem = LGIcon.Swords,
                Traits = new[]
                {
                    (LGIcon.Swords, "Милитаристы: флот — основа государства"),
                    (LGIcon.Alloys, "Тяжёлая промышленность"),
                    (LGIcon.Target, "Доктрина: ищут слабого соседа"),
                    (LGIcon.Diplomacy, "Дипломатия: внушают страх, требуют дань")
                },
                Story = "Доминион родился на мире-крепости Ксарн-Прайм, где столетиями шла война всех против всех. " +
                        "Победил тот, кто первым построил армию, а не дворец.\n\n" +
                        "Сегодня Доминионом правит верховный маршал. Каждый гражданин отслужил во флоте, каждая фабрика " +
                        "в любой день готова перейти на выпуск брони. Соседи для ксарнийцев — либо союзники, либо будущие провинции."
            },
            ["astrea"] = new Lore
            {
                Tagline = "Миролюбивы, любознательны, принципиальны",
                Emblem = LGIcon.Star,
                Traits = new[]
                {
                    (LGIcon.Research, "Учёные: наука — главный приоритет"),
                    (LGIcon.Influence, "Республика: сильное влияние"),
                    (LGIcon.Planet, "Колонисты: ценят новые миры"),
                    (LGIcon.Handshake, "Дипломатия: опытные переговорщики")
                },
                Story = "Республика выросла из союза первых колоний, которые отказались подчиняться метрополии и решили " +
                        "править собой сами. Её Совет выбирают все граждане, а решения принимаются открыто.\n\n" +
                        "Астрейцы верят, что знание сильнее оружия, а договор прочнее страха. Но за мягкими словами " +
                        "Председателя стоит флот, который умеет защищать то, во что верит Республика."
            },
            ["aquila"] = new Lore
            {
                Tagline = "Расчётливы, предприимчивы, неуловимы",
                Emblem = LGIcon.Trade,
                Traits = new[]
                {
                    (LGIcon.Trade, "Торговцы: любят выгодные сделки"),
                    (LGIcon.Minerals, "Добывающая корпорация"),
                    (LGIcon.Energy, "Энергетические концессии"),
                    (LGIcon.Gift, "Дипломатия: всё имеет цену")
                },
                Story = "Синдикат начинался как горнодобывающая компания, скупившая права на целую звёздную систему. " +
                        "Через век компания стала государством, а совет директоров — правительством.\n\n" +
                        "Аквилой правит генеральный директор, а законы здесь пишутся как контракты. Синдикат покупает " +
                        "то, что другие берут силой, и превращает в прибыль даже чужие войны."
            },
            ["iridia"] = new Lore
            {
                Tagline = "Загадочны, прозорливы, терпеливы",
                Emblem = LGIcon.Sensors,
                Traits = new[]
                {
                    (LGIcon.Influence, "Теократия: огромное влияние"),
                    (LGIcon.Sensors, "Провидцы: видят угрозы заранее"),
                    (LGIcon.Defense, "Крепкая оборона, редко нападают первыми"),
                    (LGIcon.Diplomacy, "Дипломатия: говорят загадками, помнят всё")
                },
                Story = "Иридийцы выросли в светящихся лесах мира-сада Иридия, где каждое живое существо излучает свет. " +
                        "Их кожа вспыхивает в такт мыслям, а самые одарённые видят обрывки грядущего.\n\n" +
                        "Правит Оракулом Верховная провидица Аурэлия. Говорят, она знает исход любой войны ещё до первого выстрела, " +
                        "поэтому Оракул так редко сражается — и почти никогда не проигрывает."
            },
            ["terraan"] = new Lore
            {
                Tagline = "Древни, терпеливы, неуступчивы",
                Emblem = LGIcon.Planet,
                Traits = new[]
                {
                    (LGIcon.Wrench, "Живая броня: корабли сами заживают"),
                    (LGIcon.Sensors, "Мицелий: разведка быстрее, сенсоры дальше"),
                    (LGIcon.Starbase, "Живые форпосты дешевле; «Пробуждение сада»"),
                    (LGIcon.Warning, "Медленный рост, боятся энергии")
                },
                Story = "Тэрра'ан — древнейшая из живых цивилизаций галактики. Их миры — огромные сады, где корни связывают континенты, " +
                        "а деревья хранят память веков. Корабли здесь не собирают, а выращивают из биокоры, и раны их заживают сами.\n\n" +
                        "Решения принимает весь Конклав через сеть мицелия, а голос его — старейшая Исинна Кральтэр, Хранительница Корней. " +
                        "Конклав не спешит и терпеливо ждёт, пока соседи истощат себя войнами. Но если сад под угрозой — пробуждается всё, что в нём спало."
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
            // Затемнение слева (под текст) и снизу (под карточки) — фон остаётся красивым, текст читается
            var shadeL = LGBuild.Panel(rt, "ShadeLeft", Color.white);
            shadeL.sprite = GradientSprite(true);
            shadeL.color = new Color(0.004f, 0.008f, 0.016f, 0.92f);
            shadeL.rectTransform.anchorMin = new Vector2(0, 0);
            shadeL.rectTransform.anchorMax = new Vector2(0.62f, 1);
            shadeL.rectTransform.offsetMin = shadeL.rectTransform.offsetMax = Vector2.zero;
            LG.Ignore(shadeL.gameObject);
            var shadeB = LGBuild.Panel(rt, "ShadeBottom", Color.white);
            shadeB.sprite = GradientSprite(false);
            shadeB.color = new Color(0.004f, 0.008f, 0.016f, 0.9f);
            shadeB.rectTransform.anchorMin = new Vector2(0, 0);
            shadeB.rectTransform.anchorMax = new Vector2(1, 0);
            shadeB.rectTransform.pivot = new Vector2(0.5f, 0);
            shadeB.rectTransform.sizeDelta = new Vector2(0, 300);
            LG.Ignore(shadeB.gameObject);

            BuildMotes(rt);
            _halo = LGBuild.Panel(rt, "Halo", Color.white);
            _halo.sprite = RadialSprite();
            _halo.rectTransform.anchorMin = _halo.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            _halo.rectTransform.sizeDelta = new Vector2(1250f, 1250f);
            _halo.rectTransform.anchoredPosition = new Vector2(-420f, 70f);
            LG.Ignore(_halo.gameObject);

            BuildInfo(rt);
            BuildFrame(rt);
            BuildCards(rt);
            BuildButtons(rt);

            Select(0, instant: true);
        }

        private static RawImage BgLayer(RectTransform rt, string name)
        {
            var r = LGBuild.Rect(rt, name);
            r.Stretch();
            var img = r.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            LG.Ignore(r.gameObject);
            return img;
        }

        // ---------- Досье слева ----------

        private void BuildInfo(RectTransform rt)
        {
            var title = LGBuild.Label(rt, "НОВАЯ ИГРА  ·  ВЫБОР ИМПЕРИИ", 15, Muted, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(64, -40), new Vector2(700, 22));

            var panel = LGBuild.Panel(rt, "Info", PanelBg, raycast: true);
            var pr = panel.rectTransform;
            pr.anchorMin = new Vector2(0, 0);
            pr.anchorMax = new Vector2(0, 1);
            pr.pivot = new Vector2(0, 0.5f);
            pr.offsetMin = new Vector2(64f, 190f);
            pr.offsetMax = new Vector2(724f, -76f);
            _infoFx = LG.Platter(panel.gameObject, 6f);
            _infoFx.FillMultiplier = 2.8f;
            _infoFx.SpecularMultiplier = 0.2f;
            _infoGroup = panel.gameObject.AddComponent<CanvasGroup>();

            _accentBar = LGBuild.Panel(pr, "Accent", Color.white);
            _accentBar.rectTransform.anchorMin = new Vector2(0, 0);
            _accentBar.rectTransform.anchorMax = new Vector2(0, 1);
            _accentBar.rectTransform.offsetMin = new Vector2(0, 18);
            _accentBar.rectTransform.offsetMax = new Vector2(4, -18);
            LG.Ignore(_accentBar.gameObject);

            _emblem = LGBuild.Panel(pr, "Emblem", Color.white);
            _emblem.sprite = HexSprite();
            _emblem.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -28), new Vector2(70, 70));
            LG.Ignore(_emblem.gameObject);
            _emblemIcon = LGIcons.Create(_emblem.transform, LGIcon.Leader, 36, Color.white);

            _name = LGBuild.Label(pr, "", 34, Primary, TextAnchor.UpperLeft, bold: true);
            FitText(_name, 22);
            _name.rectTransform.TopBand(26, 42, 118, 24);
            _leader = LGBuild.Label(pr, "", 15, Primary, TextAnchor.UpperLeft);
            _leader.rectTransform.TopBand(72, 22, 120, 24);
            _tagline = LGBuild.Label(pr, "", 15, new Color(0.80f, 0.86f, 0.90f), TextAnchor.UpperLeft);
            _tagline.fontStyle = FontStyle.Italic;
            _tagline.rectTransform.TopBand(120, 22, 32, 24);

            Heading(pr, "ОСОБЕННОСТИ", 160f);
            _traits = LGBuild.Rect(pr, "Traits");
            _traits.TopBand(196, 196, 32, 24);

            Heading(pr, "ИСТОРИЯ", 400f);
            _desc = LGBuild.Label(pr, "", 14, new Color(0.80f, 0.86f, 0.90f), TextAnchor.UpperLeft, wrap: true);
            _desc.lineSpacing = 1.18f;
            _desc.rectTransform.Stretch(32, 74, 28, 436);
            _quote = LGBuild.Label(pr, "", 14, Muted, TextAnchor.LowerLeft, wrap: true);
            _quote.fontStyle = FontStyle.Italic;
            _quote.rectTransform.anchorMin = new Vector2(0, 0);
            _quote.rectTransform.anchorMax = new Vector2(1, 0);
            _quote.rectTransform.pivot = new Vector2(0.5f, 0);
            _quote.rectTransform.offsetMin = new Vector2(32, 18);
            _quote.rectTransform.offsetMax = new Vector2(-28, 62);
        }

        private static void Heading(RectTransform parent, string text, float top)
        {
            var t = LGBuild.Label(parent, text, 13, Muted, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.TopBand(top, 18, 32, 24);
            var l = LGBuild.Panel(parent, "Line", new Color(1f, 1f, 1f, 0.10f));
            l.rectTransform.TopBand(top + 24, 1, 32, 24);
            LG.Ignore(l.gameObject);
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

        private void BuildFrame(RectTransform rt)
        {
            _frame = LGBuild.Rect(rt, "Frame");
            _frame.anchorMin = _frame.anchorMax = new Vector2(1, 0.5f);
            _frame.pivot = new Vector2(1, 0.5f);
            _frame.sizeDelta = new Vector2(540f, 780f);
            _frame.anchoredPosition = new Vector2(-150f, 70f);
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

        // ---------- Карточки фракций ----------

        private void BuildCards(RectTransform rt)
        {
            _cards = LGBuild.Rect(rt, "Cards");
            _cards.anchorMin = _cards.anchorMax = new Vector2(0.5f, 0f);
            _cards.pivot = new Vector2(0.5f, 0f);
            _cards.anchoredPosition = new Vector2(-150f, 30f);
            var factions = FactionRegistry.AvailableFactions;
            bool compact = factions.Length > 3;
            bool tight = factions.Length > 4;
            float w = tight ? 214f : compact ? 236f : 270f, gap = tight ? 10f : compact ? 12f : 16f, thumbSize = tight ? 72f : compact ? 80f : 96f;
            float textX = 10f + thumbSize + 12f;
            const float h = 116f;
            _cards.sizeDelta = new Vector2(factions.Length * (w + gap) - gap, h + 12f);
            for (int i = 0; i < factions.Length; i++)
            {
                var f = factions[i];
                int idx = i;
                var card = LGBuild.Panel(_cards, "Card_" + i, PanelBg, raycast: true);
                card.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(i * (w + gap), 0f), new Vector2(w, h));
                var fx = LG.Platter(card.gameObject, 6f);
                fx.FillMultiplier = 2.8f;
                fx.SpecularMultiplier = 0.2f;

                var thumb = LGBuild.Panel(card.transform, "Thumb", new Color(0f, 0f, 0f, 0.6f));
                thumb.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(thumbSize, thumbSize));
                LG.Platter(thumb.gameObject, 4f).FillMultiplier = 3f;
                var tm = LG.RoundedMask(thumb.transform, 4f, 1f);
                LeaderPortraitView.Create(tm, LeaderPortraits.ForFaction(f), 2.1f, Vector4.zero, false, true, false);

                var name = LGBuild.Label(card.transform, f.Name.ToUpper(), compact ? 14 : 15, Primary, TextAnchor.UpperLeft, bold: true, wrap: true);
                FitText(name, 10);
                name.rectTransform.Stretch(textX, 46, 10, 14);
                var title = LGBuild.Label(card.transform, f.Title, compact ? 11 : 12, Muted, TextAnchor.LowerLeft, wrap: true);
                FitText(title, 9);
                title.rectTransform.Stretch(textX, 14, 10, 70);

                var btn = card.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => { if (idx != _index) { SFXManager.Play(Sfx.UiTab); Select(idx); } });
                _cardUi.Add((card, fx, name, f, card.rectTransform));
            }
        }

        private void BuildButtons(RectTransform rt)
        {
            Btn(rt, "Back", LGIcon.Back, "НАЗАД", new Vector2(0, 0), new Vector2(0, 0), new Vector2(64, 40), 180f, () => _onBack?.Invoke());
            Btn(rt, "Random", LGIcon.Dice, "СЛУЧАЙНАЯ", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-290, 40), 220f, () =>
            {
                int n = FactionRegistry.AvailableFactions.Length;
                SFXManager.Play(Sfx.UiTab);
                Select((_index + UnityEngine.Random.Range(1, Mathf.Max(2, n))) % n);
            });
            Btn(rt, "Next", LGIcon.Play, "ДАЛЬШЕ", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-64, 40), 214f, () =>
            {
                SFXManager.Play(Sfx.UiConfirm);
                _onChoose?.Invoke(FactionRegistry.AvailableFactions[_index]);
            });
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

            for (int i = 0; i < _cardUi.Count; i++)
            {
                bool on = i == _index;
                var c = _cardUi[i];
                Color cc = c.f.EmpireColor;
                c.bg.color = on ? new Color(0.02f + cc.r * 0.10f, 0.04f + cc.g * 0.10f, 0.06f + cc.b * 0.10f, 0.94f) : PanelBg;
                c.fx.SetRim(on ? new Color(cc.r, cc.g, cc.b, 0.95f) : new Color(1f, 1f, 1f, 0.10f));
                c.fx.GlowMultiplier = on ? 1f : 0f;
                c.name.color = on ? Color.Lerp(cc, Color.white, 0.3f) : Primary;
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
            front.transform.SetSiblingIndex(_bgFront ? 1 : 0);
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
            _frameTitle.text = leader != null ? $"ПРЯМАЯ СВЯЗЬ  ·  {leader.Name.ToUpper()}" : "ПРЯМАЯ СВЯЗЬ";
            _frameSub.text = leader != null ? $"<b>{leader.Name}</b>\n<color=#9FB2BC>{leader.Title}</color>" : f.Title;

            // Досье
            _accentBar.color = ec;
            _infoFx.SetRim(new Color(ec.r, ec.g, ec.b, 0.25f));
            _emblem.color = Color.Lerp(ec, new Color(0.6f, 0.2f, 0.05f), 0.2f);
            _emblemIcon.sprite = LGIcons.Get(lore?.Emblem ?? LGIcon.Leader);
            _emblemIcon.color = new Color(0.05f, 0.04f, 0.03f, 0.9f);
            _name.text = f.Name.ToUpper();
            _name.color = light;
            _leader.text = leader != null ? $"<color=#8AA2A8>{f.Title}  ·  правитель:</color> <b>{leader.Name}</b>" : f.Title;
            _tagline.text = lore?.Tagline ?? "";

            LGBuild.Clear(_traits);
            float y = 0f;
            foreach (var (icon, text) in BonusTraits(f)) Trait(ref y, icon, text, new Color(0.44f, 0.88f, 0.60f));
            if (lore != null) foreach (var (icon, text) in lore.Traits) Trait(ref y, icon, text, light);

            _desc.text = lore?.Story ?? StripBonuses(f.Description);
            _quote.text = leader != null ? $"«{leader.Quote}»" : "";

            _accent = ec;
            _halo.color = new Color(ec.r, ec.g, ec.b, 0.0f);
            if (!instant) { _flashT = 0f; _sweepT = 0f; }
            foreach (var m in _motes) m.Img.color = new Color(light.r, light.g, light.b, 0f);

            _fade = instant ? 1f : 0f;
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

            // Выбранная карточка мерцает рамкой
            for (int i = 0; i < _cardUi.Count; i++)
            {
                if (i != _index) continue;
                Color cc = _cardUi[i].f.EmpireColor;
                _cardUi[i].fx.SetRim(new Color(cc.r, cc.g, cc.b, 0.6f + 0.35f * Mathf.Sin(t * 3f)));
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

        private static IEnumerable<(LGIcon, string)> BonusTraits(FactionInfo f)
        {
            if (f.EnergyBonus > 1.001f) yield return (LGIcon.Energy, $"+{(f.EnergyBonus - 1f) * 100f:0}% к добыче гелия-3");
            if (f.MineralBonus > 1.001f) yield return (LGIcon.Minerals, $"+{(f.MineralBonus - 1f) * 100f:0}% к добыче титана");
            if (f.AlloyBonus > 1.001f) yield return (LGIcon.Alloys, $"+{(f.AlloyBonus - 1f) * 100f:0}% к производству сплавов");
            if (f.InfluenceBonus > 1.001f) yield return (LGIcon.Influence, $"+{(f.InfluenceBonus - 1f) * 100f:0}% к влиянию");
            if (FactionRegistry.IsTerraan(f)) yield return (LGIcon.Defense, "+15% корпус, +10% щиты (биокора)");
            if (f.MineralBonus < 0.999f) yield return (LGIcon.Minerals, $"−{(1f - f.MineralBonus) * 100f:0}% к добыче титана");
        }

        private void Trait(ref float y, LGIcon icon, string text, Color col)
        {
            var row = LGBuild.Rect(_traits, "Trait");
            row.TopBand(y, 26);
            var ic = LGIcons.Create(row, icon, 16, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16));
            var t = LGBuild.Label(row, text, 15, new Color(0.86f, 0.91f, 0.94f), TextAnchor.MiddleLeft);
            t.rectTransform.Stretch(28, 0, 0, 0);
            y += 30f;
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
            if (_liveDot != null) { var c = _liveDot.color; c.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 2.2f)); _liveDot.color = c; }

            if (_fade >= 1f) return;
            _fade = Mathf.MoveTowards(_fade, 1f, dt * 3f);
            float k = Mathf.SmoothStep(0f, 1f, _fade);
            if (_frameGroup != null) _frameGroup.alpha = k;
            if (_infoGroup != null) _infoGroup.alpha = Mathf.Lerp(0.3f, 1f, k);
            _frame.anchoredPosition = new Vector2(-150f + (1f - k) * 24f, 70f);
        }

        private void OnEnable()
        {
            if (_frameGroup != null) _frameGroup.alpha = 1f;
            if (_infoGroup != null) _infoGroup.alpha = 1f;
        }

        // ==================== ТЕКСТУРЫ ====================

        private static Texture2D s_scan;
        private static readonly Dictionary<bool, Sprite> s_grad = new Dictionary<bool, Sprite>();

        /// <summary>Тонкие горизонтальные линии развёртки (повторяется по вертикали).</summary>
        private static Texture2D ScanTexture()
        {
            if (s_scan != null) return s_scan;
            s_scan = new Texture2D(1, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            s_scan.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 60), new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 0) });
            s_scan.Apply();
            return s_scan;
        }

        /// <summary>Градиент прозрачности: horizontal — слева направо, иначе снизу вверх.</summary>
        private static Sprite GradientSprite(bool horizontal)
        {
            if (s_grad.TryGetValue(horizontal, out var sp) && sp != null) return sp;
            const int n = 64;
            var tex = new Texture2D(horizontal ? n : 2, horizontal ? 2 : n, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Pow(1f - i / (n - 1f), 1.4f);
                var c = new Color(1f, 1f, 1f, a);
                if (horizontal) { tex.SetPixel(i, 0, c); tex.SetPixel(i, 1, c); }
                else { tex.SetPixel(0, i, c); tex.SetPixel(1, i, c); }
            }
            tex.Apply();
            sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            s_grad[horizontal] = sp;
            return sp;
        }

        // ==================== ПРОЦЕДУРНЫЕ СПРАЙТЫ ====================

        private static Sprite s_radial, s_hex;

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

        /// <summary>Шестиугольник для герба и значков особенностей.</summary>
        private static Sprite HexSprite()
        {
            if (s_hex != null) return s_hex;
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float px0 = Mathf.Abs((x + 0.5f) / n * 2f - 1f), py0 = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
                // Шестиугольник «на ребре»: |y| ≤ 0.866 и |x|·0.866 + |y|·0.5 ≤ 0.866
                float d = Mathf.Max(py0 - 0.86f, px0 * 0.866f + py0 * 0.5f - 0.86f);
                float a = Mathf.Clamp01(0.5f - d * n * 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            s_hex = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return s_hex;
        }
    }
}
