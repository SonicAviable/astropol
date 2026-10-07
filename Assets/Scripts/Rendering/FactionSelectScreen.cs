using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Выбор цивилизации в духе Master of Orion: слева — плитки фракций с портретами правителей,
    /// в центре — правитель во весь рост на фоне космоса, справа — герб, лидер, особенности и описание.
    /// Внизу: «Назад», «Случайная империя», «Дальше».
    /// Фон: Resources/Factions/bg_&lt;ключ правителя&gt; (если есть), иначе общая заставка UI/LoadingArt.
    /// </summary>
    public class FactionSelectScreen : MonoBehaviour
    {
        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static readonly Color PanelBg = new Color(0.016f, 0.03f, 0.044f, 0.97f);

        private Action<FactionInfo> _onChoose;
        private Action _onBack;
        private int _index;

        private RawImage _bg;
        private RectTransform _leaderHost, _tiles, _traits;
        private Image _glow, _emblem;
        private Image _emblemIcon;
        private Text _name, _leader, _tagline, _desc, _quote;
        private readonly List<(Image bg, LiquidGlassEffect fx, Text label, FactionInfo f)> _tileUi = new List<(Image, LiquidGlassEffect, Text, FactionInfo)>();
        private CanvasGroup _infoGroup, _leaderGroup;
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
            }
        };

        // ==================== ПОСТРОЕНИЕ ====================

        public void Build(Action<FactionInfo> onChoose, Action onBack)
        {
            _onChoose = onChoose;
            _onBack = onBack;

            var rt = (RectTransform)transform;
            rt.Stretch();
            var root = gameObject;
            root.AddComponent<CanvasGroup>();
            var bgImg = root.AddComponent<Image>();
            bgImg.color = new Color(0.005f, 0.01f, 0.02f, 1f);
            bgImg.raycastTarget = true;
            LG.Ignore(root, includeChildren: false);
            LG.Motion(root, LGAppear.Kind.Fade);

            // Фон: космос и планета
            var bgRt = LGBuild.Rect(rt, "Background");
            bgRt.Stretch();
            _bg = bgRt.gameObject.AddComponent<RawImage>();
            _bg.raycastTarget = false;
            LG.Ignore(bgRt.gameObject);
            var dim = LGBuild.Panel(rt, "Dim", new Color(0.005f, 0.01f, 0.02f, 0.35f));
            dim.rectTransform.Stretch();
            LG.Ignore(dim.gameObject);

            // Свечение за правителем
            _glow = LGBuild.Panel(rt, "Glow", Color.white);
            _glow.sprite = RadialSprite();
            _glow.rectTransform.anchorMin = _glow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _glow.rectTransform.sizeDelta = new Vector2(1100f, 1100f);
            _glow.rectTransform.anchoredPosition = new Vector2(-40f, 20f);
            LG.Ignore(_glow.gameObject);

            // Правитель
            _leaderHost = LGBuild.Rect(rt, "Leader");
            _leaderHost.anchorMin = _leaderHost.anchorMax = new Vector2(0.5f, 0f);
            _leaderHost.pivot = new Vector2(0.5f, 0f);
            _leaderHost.sizeDelta = new Vector2(660f, 984f);
            _leaderHost.anchoredPosition = new Vector2(-40f, 0f);
            _leaderGroup = _leaderHost.gameObject.AddComponent<CanvasGroup>();

            BuildLeft(rt);
            BuildRight(rt);
            BuildBottom(rt);

            Select(0, instant: true);
        }

        private void BuildLeft(RectTransform rt)
        {
            var title = LGBuild.Label(rt, "НОВАЯ ИГРА / ВЫБОР ИМПЕРИИ", 30, Primary, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -36), new Vector2(760, 40));
            var sub = LGBuild.Label(rt, "Выберите цивилизацию, которую поведёте к звёздам", 14, Muted, TextAnchor.UpperLeft);
            sub.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(48, -78), new Vector2(760, 22));

            _tiles = LGBuild.Rect(rt, "Tiles");
            _tiles.anchorMin = _tiles.anchorMax = new Vector2(0, 1);
            _tiles.pivot = new Vector2(0, 1);
            _tiles.sizeDelta = new Vector2(380f, 210f);
            _tiles.anchoredPosition = new Vector2(44f, -122f);

            var factions = FactionRegistry.AvailableFactions;
            const float tw = 122f, th = 200f, gap = 7f;
            for (int i = 0; i < factions.Length; i++)
            {
                var f = factions[i];
                int idx = i;
                var tile = LGBuild.Panel(_tiles, "Tile_" + i, PanelBg, raycast: true);
                tile.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(i * (tw + gap), 0f), new Vector2(tw, th));
                var fx = LG.Platter(tile.gameObject, 4f);
                fx.FillMultiplier = 2.6f;
                fx.SpecularMultiplier = 0.2f;

                var label = LGBuild.Label(tile.transform, ShortName(f), 15, Cyan, TextAnchor.UpperLeft, bold: true);
                label.rectTransform.TopBand(8, 22, 10, 6);
                // Эмблема-«печать» за головой правителя — как в референсе
                var crest = LGIcons.Create(tile.transform, LoreFor(f)?.Emblem ?? LGIcon.Leader, 92, new Color(f.EmpireColor.r, f.EmpireColor.g, f.EmpireColor.b, 0.25f));
                crest.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -38), new Vector2(92, 92));
                var host = LGBuild.Rect(tile.transform, "Leader");
                host.Stretch(2, 2, 2, 34);
                LeaderPortraitView.Create(host, LeaderPortraits.ForFaction(f), 1.9f, new Vector4(0f, 0f, 0.1f, 0f), false, false, true);

                var btn = tile.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => { if (idx != _index) { SFXManager.Play(Sfx.UiTab); Select(idx); } });
                _tileUi.Add((tile, fx, label, f));
            }

            // Параметры партии — под плитками
            var gs = GameSession.Settings;
            var info = LGBuild.Panel(rt, "GameInfo", PanelBg);
            info.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(44f, -344f), new Vector2(380f, 112f));
            var ifx = LG.Platter(info.gameObject, 4f);
            ifx.FillMultiplier = 3f;
            var ih = LGBuild.Label(info.transform, "<color=#F2A33A>■</color> ПАРАМЕТРЫ ГАЛАКТИКИ", 13, Primary, TextAnchor.UpperLeft, bold: true);
            ih.rectTransform.TopBand(10, 20, 14, 10);
            var it = LGBuild.Label(info.transform,
                $"Размер: <b>{NewGameSettings.SizeNames[Mathf.Clamp(gs.GalaxySize, 0, 2)]}</b>  ·  систем: <b>{gs.StarCount}</b>\n" +
                $"Сложность: <b>{NewGameSettings.DifficultyNames[Mathf.Clamp(gs.Difficulty, 0, 2)]}</b>  ·  сид: <b>{gs.Seed}</b>\n" +
                $"Обучение: <b>{(gs.Tutorial ? "включено" : "выключено")}</b>",
                13, Muted, TextAnchor.UpperLeft, wrap: true);
            it.lineSpacing = 1.2f;
            it.rectTransform.Stretch(14, 8, 10, 36);
        }

        private void BuildRight(RectTransform rt)
        {
            var panel = LGBuild.Panel(rt, "Info", PanelBg, raycast: true);
            var pr = panel.rectTransform;
            pr.anchorMin = new Vector2(1, 0);
            pr.anchorMax = new Vector2(1, 1);
            pr.pivot = new Vector2(1, 0.5f);
            pr.offsetMin = new Vector2(-560f, 110f);
            pr.offsetMax = new Vector2(-34f, -48f);
            var pfx = LG.Platter(panel.gameObject, 4f);
            pfx.FillMultiplier = 3f;
            pfx.SpecularMultiplier = 0.2f;
            _infoGroup = panel.gameObject.AddComponent<CanvasGroup>();

            // Шапка: герб, название, лидер
            var head = LGBuild.Panel(pr, "Head", new Color(0f, 0f, 0f, 0.35f));
            head.rectTransform.TopBand(0, 112);
            LG.Ignore(head.gameObject);
            _emblem = LGBuild.Panel(head.transform, "Emblem", Color.white);
            _emblem.sprite = HexSprite();
            _emblem.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(76, 76));
            LG.Ignore(_emblem.gameObject);
            _emblemIcon = LGIcons.Create(_emblem.transform, LGIcon.Leader, 40, Color.white);
            _name = LGBuild.Label(head.transform, "", 30, Primary, TextAnchor.UpperLeft, bold: true);
            _name.rectTransform.Stretch(116, 0, 14, 22);
            _leader = LGBuild.Label(head.transform, "", 15, Primary, TextAnchor.UpperLeft);
            _leader.rectTransform.Stretch(118, 0, 14, 64);

            _tagline = LGBuild.Label(pr, "", 15, new Color(0.82f, 0.88f, 0.92f), TextAnchor.UpperLeft);
            _tagline.rectTransform.TopBand(132, 22, 24, 20);

            var th = LGBuild.Label(pr, "<color=#F2A33A>■</color> ОСОБЕННОСТИ:", 16, Primary, TextAnchor.UpperLeft, bold: true);
            th.rectTransform.TopBand(174, 22, 22, 20);
            Line(pr, 200f);
            _traits = LGBuild.Rect(pr, "Traits");
            _traits.TopBand(210, 230, 22, 20);

            var dh = LGBuild.Label(pr, "<color=#F2A33A>■</color> ОПИСАНИЕ:", 16, Primary, TextAnchor.UpperLeft, bold: true);
            dh.rectTransform.TopBand(450, 22, 22, 20);
            Line(pr, 476f);
            _desc = LGBuild.Label(pr, "", 14, new Color(0.80f, 0.86f, 0.90f), TextAnchor.UpperLeft, wrap: true);
            _desc.lineSpacing = 1.15f;
            _desc.rectTransform.Stretch(24, 70, 22, 490);
            _quote = LGBuild.Label(pr, "", 14, Muted, TextAnchor.LowerLeft, wrap: true);
            _quote.fontStyle = FontStyle.Italic;
            _quote.rectTransform.Stretch(24, 18, 22, 0);
            _quote.rectTransform.anchorMax = new Vector2(1, 0);
            _quote.rectTransform.offsetMax = new Vector2(-22, 62);
        }

        private static void Line(RectTransform parent, float top)
        {
            var l = LGBuild.Panel(parent, "Line", new Color(0.36f, 0.86f, 0.82f, 0.35f));
            l.rectTransform.TopBand(top, 1.5f, 22, 20);
            LG.Ignore(l.gameObject);
        }

        private void BuildBottom(RectTransform rt)
        {
            Btn(rt, "Back", LGIcon.Back, "НАЗАД", new Vector2(0, 0), new Vector2(0, 0), new Vector2(34, 34), 190f, () => _onBack?.Invoke());
            Btn(rt, "Random", LGIcon.Dice, "СЛУЧАЙНАЯ ИМПЕРИЯ", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-282, 34), 280f, () =>
            {
                int n = FactionRegistry.AvailableFactions.Length;
                int pick = (_index + UnityEngine.Random.Range(1, Mathf.Max(2, n))) % n;
                SFXManager.Play(Sfx.UiTab);
                Select(pick);
            });
            Btn(rt, "Next", LGIcon.Play, "ДАЛЬШЕ", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-34, 34), 236f, () =>
            {
                SFXManager.Play(Sfx.UiConfirm);
                _onChoose?.Invoke(FactionRegistry.AvailableFactions[_index]);
            });
        }

        private static void Btn(RectTransform rt, string name, LGIcon icon, string label, Vector2 anchor, Vector2 pivot, Vector2 pos, float w, Action act)
        {
            var b = LGBuild.Button(rt, name, new Color(0.06f, 0.30f, 0.30f, 0.95f), new Color(0.36f, 0.86f, 0.82f, 0.45f), act, icon, label, 17, 4f);
            ((RectTransform)b.transform).At(anchor, pivot, pos, new Vector2(w, 54f));
            var fx = b.GetComponent<LiquidGlassEffect>();
            if (fx != null) { fx.SpecularMultiplier = 0.3f; fx.FillMultiplier = 2.4f; }
        }

        // ==================== ВЫБОР ====================

        private static Lore LoreFor(FactionInfo f)
        {
            var l = LeaderPortraits.ForFaction(f);
            return l != null && LoreByKey.TryGetValue(l.Key, out var lore) ? lore : null;
        }

        private static string ShortName(FactionInfo f)
        {
            var parts = f.Name.Split(' ');
            return parts[parts.Length - 1].ToUpper();
        }

        private void Select(int index, bool instant = false)
        {
            var factions = FactionRegistry.AvailableFactions;
            _index = Mathf.Clamp(index, 0, factions.Length - 1);
            var f = factions[_index];
            var leader = LeaderPortraits.ForFaction(f);
            var lore = LoreFor(f);
            Color ec = f.EmpireColor;

            for (int i = 0; i < _tileUi.Count; i++)
            {
                bool on = i == _index;
                var t = _tileUi[i];
                Color c = t.f.EmpireColor;
                t.bg.color = on ? new Color(0.03f + c.r * 0.12f, 0.06f + c.g * 0.12f, 0.08f + c.b * 0.12f, 0.95f) : PanelBg;
                t.fx.SetRim(on ? new Color(c.r, c.g, c.b, 0.95f) : new Color(1f, 1f, 1f, 0.10f));
                t.fx.GlowMultiplier = on ? 1f : 0f;
                t.label.color = on ? Color.Lerp(c, Color.white, 0.2f) : Cyan;
            }

            // Фон: родной мир фракции, если художник его положил
            var tex = leader != null ? Resources.Load<Texture2D>("Factions/bg_" + leader.Key) : null;
            if (tex == null) tex = Resources.Load<Texture2D>("UI/LoadingArt");
            _bg.texture = tex;
            _bg.enabled = tex != null;
            if (tex != null) Cover(_bg, tex);
            _glow.color = new Color(ec.r, ec.g, ec.b, 0.22f);

            // Правитель
            LGBuild.Clear(_leaderHost);
            LeaderPortraitView.Create(_leaderHost, leader, 1f, new Vector4(0.14f, 0.14f, 0.30f, 0f), false, false, true);

            // Информация
            _emblem.color = Color.Lerp(ec, new Color(0.6f, 0.2f, 0.05f), 0.25f);
            _emblemIcon.sprite = LGIcons.Get(lore?.Emblem ?? LGIcon.Leader);
            _emblemIcon.color = new Color(0.06f, 0.04f, 0.03f, 0.9f);
            _name.text = f.Name.ToUpper();
            _name.color = Color.Lerp(ec, Color.white, 0.15f);
            _leader.text = leader != null
                ? $"<color=#8AA2A8>Лидер:</color> <b>{leader.Name}</b>  <color=#8AA2A8>· {leader.Title.ToLower()}</color>"
                : $"<color=#8AA2A8>{f.Title}</color>";
            _tagline.text = lore?.Tagline ?? f.Title;

            LGBuild.Clear(_traits);
            float y = 0f;
            foreach (var (icon, text) in BonusTraits(f)) Trait(ref y, icon, text, new Color(0.44f, 0.88f, 0.60f));
            if (lore != null) foreach (var (icon, text) in lore.Traits) Trait(ref y, icon, text, Color.Lerp(ec, Color.white, 0.3f));

            _desc.text = lore?.Story ?? StripBonuses(f.Description);
            _quote.text = leader != null ? $"«{leader.Quote}»  — {leader.Name}" : "";

            _fade = instant ? 1f : 0f;
        }

        /// <summary>Бонусы фракции из её числовых множителей.</summary>
        private static IEnumerable<(LGIcon, string)> BonusTraits(FactionInfo f)
        {
            if (f.EnergyBonus > 1.001f) yield return (LGIcon.Energy, $"+{(f.EnergyBonus - 1f) * 100f:0}% к добыче гелия-3");
            if (f.MineralBonus > 1.001f) yield return (LGIcon.Minerals, $"+{(f.MineralBonus - 1f) * 100f:0}% к добыче титана");
            if (f.AlloyBonus > 1.001f) yield return (LGIcon.Alloys, $"+{(f.AlloyBonus - 1f) * 100f:0}% к производству сплавов");
            if (f.InfluenceBonus > 1.001f) yield return (LGIcon.Influence, $"+{(f.InfluenceBonus - 1f) * 100f:0}% к влиянию");
        }

        private void Trait(ref float y, LGIcon icon, string text, Color col)
        {
            var row = LGBuild.Rect(_traits, "Trait");
            row.TopBand(y, 28);
            var badge = LGBuild.Panel(row, "Badge", new Color(col.r * 0.2f, col.g * 0.2f, col.b * 0.2f, 1f));
            badge.sprite = HexSprite();
            badge.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(24, 24));
            LG.Ignore(badge.gameObject);
            var ic = LGIcons.Create(badge.transform, icon, 13, col);
            var t = LGBuild.Label(row, text.ToUpper(), 14, new Color(0.84f, 0.90f, 0.94f), TextAnchor.MiddleLeft);
            t.rectTransform.Stretch(34, 0, 0, 0);
            y += 31f;
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
            if (_fade >= 1f) return;
            _fade = Mathf.MoveTowards(_fade, 1f, Time.unscaledDeltaTime * 3.5f);
            float k = Mathf.SmoothStep(0f, 1f, _fade);
            if (_leaderGroup != null) _leaderGroup.alpha = k;
            if (_infoGroup != null) _infoGroup.alpha = Mathf.Lerp(0.35f, 1f, k);
            _leaderHost.anchoredPosition = new Vector2(-40f + (1f - k) * 30f, 0f);
        }

        private void OnEnable()
        {
            if (_leaderGroup != null) _leaderGroup.alpha = 1f;
            if (_infoGroup != null) _infoGroup.alpha = 1f;
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
