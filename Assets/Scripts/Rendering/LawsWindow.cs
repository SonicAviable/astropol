using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// «Законы империи» (клавиша L, пункт левого меню): пять направлений — экономика, армия, наука, общество,
    /// разведка — по три закона в каждом. У закона — описание, плюсы (зелёным) и минусы (красным), плата влиянием
    /// в месяц и влияние на стабильность колоний. Действующий закон подсвечен; «Принять» стоит влияния и закрывает
    /// направление на время — причина, почему нельзя, написана прямо на кнопке.
    /// Вторая вкладка — «Решения» (EmpireDecisions): особые решения фракции и общие, за титан и другие ресурсы.
    /// </summary>
    public partial class LawsWindow : MonoBehaviour
    {
        public static LawsWindow Instance { get; private set; }
        public static bool IsOpenNow => Instance != null && Instance.IsOpen;
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private const float W = 1400f, H = 800f, ColGap = 14f, CardGap = 12f;

        private GameObject _root;
        private RectTransform _cols, _tabs;
        private Text _title, _subtitle, _hint;
        private string _key = "";
        /// <summary>0 — законы, 1 — решения.</summary>
        private int _tab;
        /// <summary>Следующая перестройка — с каскадным появлением карточек (после открытия окна и смены вкладки, не при ежедневном обновлении).</summary>
        private bool _cascade;
        private int _cascadeIndex;
        private float _refresh;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static readonly Color Good = new Color(0.40f, 0.95f, 0.62f);
        private static readonly Color Bad = new Color(1f, 0.48f, 0.45f);

        public static void Toggle()
        {
            if (Instance == null) new GameObject("[UI] LawsWindow").AddComponent<LawsWindow>();
            if (Instance.IsOpen) Instance.Close(); else Instance.Open();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            EmpireLaws.OnChanged += MarkDirty;
            EmpireDecisions.OnChanged += MarkDirty;
        }

        private void OnDestroy()
        {
            EmpireLaws.OnChanged -= MarkDirty;
            EmpireDecisions.OnChanged -= MarkDirty;
            if (Instance == this) Instance = null;
        }

        private void MarkDirty() => _key = "";

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Close(); return; }
            if ((_refresh -= Time.unscaledDeltaTime) <= 0f) { _refresh = 0.4f; Refresh(); }
        }

        public void Open()
        {
            if (_root == null) Build();
            if (_root == null) return;
            if (EmpireDecisions.UnseenCount > 0) _tab = 1;   // есть новые решения — сразу показываем их
            _key = "";
            _cascade = true; _cascadeIndex = 0;
            Refresh();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            SFXManager.Play(Sfx.UiTab, 0.6f);
        }

        public void Close()
        {
            if (_root == null) return;
            if (_tab == 1) EmpireDecisions.MarkSeen();
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            TooltipSystem.Hide();
        }

        // ================================================================ Каркас

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            var rt = LGBuild.Rect(host.transform, "Laws");
            rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.014f, 0.030f, 0.044f, 0.97f);
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.40f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            _title = LGBuild.Label(rt, Loc.T("ЗАКОНЫ ИМПЕРИИ"), 26, Color.white, TextAnchor.UpperLeft, bold: true);
            _title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -20), new Vector2(700, 36));
            _title.Track(1.6f);
            _subtitle = LGBuild.Label(rt, "", 13, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(33, -58), new Vector2(W - 140, 20));
            _hint = LGBuild.Label(rt, "", 12, Muted, TextAnchor.UpperLeft);
            _hint.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(33, -80), new Vector2(W - 140, 18));

            _tabs = LGBuild.Rect(rt, "Tabs");
            _tabs.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-72, -18), new Vector2(330, 38));

            var close = LGBuild.Button(rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc / L)</color>"));

            _cols = LGBuild.Rect(rt, "Columns");
            _cols.Stretch(20, 20, 20, 112);
            _root.SetActive(false);
        }

        /// <summary>Открыть окно сразу на вкладке решений.</summary>
        public static void ShowDecisions()
        {
            if (Instance == null) new GameObject("[UI] LawsWindow").AddComponent<LawsWindow>();
            Instance._tab = 1;
            if (!Instance.IsOpen) Instance.Open(); else Instance.MarkDirty();
        }

        private void SetTab(int tab)
        {
            if (_tab == tab) return;
            if (_tab == 1) EmpireDecisions.MarkSeen();
            _tab = tab;
            _key = "";
            _cascade = true; _cascadeIndex = 0;
            SFXManager.Play(Sfx.UiTab, 0.5f);
            TooltipSystem.Hide();
            Refresh();
        }

        private void BuildTabs()
        {
            LGBuild.Clear(_tabs);
            int running = EmpireDecisions.ActiveCount, fresh = EmpireDecisions.UnseenCount;
            string[] labels =
            {
                Loc.T("ЗАКОНЫ"),
                fresh > 0 ? Loc.F("РЕШЕНИЯ · {0} НОВ.", fresh)
                : running > 0 ? Loc.F("РЕШЕНИЯ · {0}/{1}", running, EmpireDecisions.MaxActive) : Loc.T("РЕШЕНИЯ")
            };
            LGIcon[] icons = { LGIcon.Society, LGIcon.Target };
            for (int i = 0; i < 2; i++)
            {
                int tab = i;
                bool on = _tab == i;
                var b = LGBuild.Button(_tabs, i == 0 ? "TabLaws" : "TabDecisions",
                    on ? new Color(0.06f, 0.20f, 0.22f) : new Color(0.03f, 0.05f, 0.07f),
                    on ? new Color(0.45f, 0.95f, 0.90f, 0.85f) : new Color(1f, 1f, 1f, 0.14f),
                    () => SetTab(tab), null, labels[i], 12);
                var br = (RectTransform)b.transform;
                br.anchorMin = new Vector2(i * 0.5f, 0); br.anchorMax = new Vector2((i + 1) * 0.5f, 1);
                br.offsetMin = new Vector2(i == 0 ? 0 : 4, 0); br.offsetMax = new Vector2(i == 0 ? -4 : 0, 0);
                var ic = LGIcons.Create(b.transform, icons[i], 14, on ? Cyan : Muted);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(14, 14));
                TooltipHelper.Attach(b.gameObject, i == 0
                    ? Loc.T("Законы империи: постоянные правила с платой влиянием")
                    : Loc.T("Решения империи: разовые проекты за титан и другие ресурсы — особые для вашей фракции и общие для всех."));
            }
        }

        private void Refresh()
        {
            if (_cols == null) return;
            if (_tab == 1) { RefreshDecisions(); return; }
            var eco = EconomyManager.Instance;
            float infl = eco != null ? eco.Influence : 0f;
            float income = eco != null ? eco.MonthlyInfluenceIncome : 0f;
            _subtitle.text = Loc.F("Влияние: <b><color=#C9A6FF>{0:0}</color></b>  ({1:+0.#;−0.#} в месяц)   ·   законы забирают <b>{2:0.#}</b> влияния в месяц",
                infl, income, EmpireLaws.InfluenceUpkeep);

            // Перестраиваем только при изменениях: законы, сроки, хватает ли влияния
            var sb = new System.Text.StringBuilder();
            sb.Append("laws|").Append(EmpireDecisions.ActiveCount).Append('|').Append(EmpireDecisions.UnseenCount).Append('|');
            foreach (var c in EmpireLaws.Categories) sb.Append(EmpireLaws.Active(c)?.Id).Append(EmpireLaws.CooldownLeft(c)).Append('|');
            sb.Append(infl >= EmpireLaws.SwitchCost);
            string key = sb.ToString();
            if (key == _key) return;
            _key = key;

            _title.text = Loc.T("ЗАКОНЫ ИМПЕРИИ");
            _hint.text = Loc.F("Смена закона — {0:0} влияния, после неё направление закрыто на {1} дней. Законы действуют, пока вы их не смените.",
                EmpireLaws.SwitchCost, EmpireLaws.CooldownDays);
            BuildTabs();
            LGBuild.Clear(_cols);
            int n = EmpireLaws.Categories.Length;
            for (int i = 0; i < n; i++)
            {
                var col = LGBuild.Rect(_cols, "Col");
                col.anchorMin = new Vector2(i / (float)n, 0); col.anchorMax = new Vector2((i + 1) / (float)n, 1);
                col.offsetMin = new Vector2(i == 0 ? 0 : ColGap * 0.5f, 0); col.offsetMax = new Vector2(i == n - 1 ? 0 : -ColGap * 0.5f, 0);
                BuildColumn(col, EmpireLaws.Categories[i]);
            }
            LG.Skin(_cols);
            _cascade = false;
        }

        private static LGIcon CategoryIcon(LawCategory c) => c switch
        {
            LawCategory.Economy => LGIcon.Industry,
            LawCategory.Military => LGIcon.Weapons,
            LawCategory.Science => LGIcon.Research,
            LawCategory.Society => LGIcon.Society,
            _ => LGIcon.Betrayal
        };

        private static Color CategoryColor(LawCategory c) => c switch
        {
            LawCategory.Economy => new Color(0.95f, 0.62f, 0.36f),
            LawCategory.Military => new Color(1f, 0.45f, 0.42f),
            LawCategory.Science => new Color(0.50f, 0.92f, 0.62f),
            LawCategory.Society => new Color(0.45f, 0.76f, 1f),
            _ => new Color(0.78f, 0.62f, 1f)
        };

        private void BuildColumn(RectTransform col, LawCategory cat)
        {
            Color cc = CategoryColor(cat);
            var head = LGBuild.Rect(col, "Head");
            head.TopBand(0, 40);
            var catArt = LGIcons.LawCategoryArt(cat);
            float csz = catArt != null ? 28f : 20f;
            var ic = LGIcons.Create(head, CategoryIcon(cat), csz, cc);
            if (catArt != null) ic.sprite = catArt;
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(csz, csz));
            var name = LGBuild.Label(head, EmpireLaws.CategoryName(cat).ToUpper(), 15, Color.white, TextAnchor.MiddleLeft, bold: true);
            name.rectTransform.Stretch(csz + 12f, 0, 0, 0);
            name.Track(1.6f);
            int cd = EmpireLaws.CooldownLeft(cat);
            var cdText = LGBuild.Label(head, cd > 0 ? Loc.F("закрыто {0} дн.", cd) : "", 11, Muted, TextAnchor.MiddleRight, bold: true);
            cdText.rectTransform.Stretch(0, 0, 0, 0);
            var line = LGBuild.Panel(col, "Line", new Color(cc.r, cc.g, cc.b, 0.5f));
            line.rectTransform.TopBand(42, 2);

            var laws = new List<LawDef>();
            foreach (var l in EmpireLaws.All) if (l.Category == cat) laws.Add(l);
            float top = 54f;
            float avail = col.rect.height > 10f ? col.rect.height - top : H - 112f - 20f - top;
            float cardH = (avail - CardGap * (laws.Count - 1)) / Mathf.Max(1, laws.Count);
            for (int i = 0; i < laws.Count; i++)
            {
                var card = LGBuild.Rect(col, "Card");
                card.TopBand(top + i * (cardH + CardGap), cardH);
                BuildCard(card, laws[i], cc);
            }
        }

        private void BuildCard(RectTransform card, LawDef law, Color cc)
        {
            bool active = EmpireLaws.IsActive(law);
            var bg = LGBuild.Panel(card, "Bg", active ? new Color(cc.r * 0.14f, cc.g * 0.14f, cc.b * 0.14f, 0.95f) : new Color(0.03f, 0.055f, 0.075f, 0.9f));
            bg.rectTransform.Stretch();
            LG.Platter(bg.gameObject, 8f).SetRim(active ? new Color(cc.r, cc.g, cc.b, 0.85f) : new Color(1f, 1f, 1f, 0.10f));

            float y = 12f;
            // Рисованный значок закона — крупный бледный водяной знак справа, под текстом
            var art = LGIcons.LawArt(law.Id);
            if (art != null)
            {
                float side = Mathf.Min(card.rect.height > 10f ? card.rect.height - 24f : 130f, 130f);
                var li = LGIcons.Create(card, CategoryIcon(law.Category), side, new Color(cc.r, cc.g, cc.b, active ? 0.24f : 0.16f), "Watermark");
                li.sprite = art;
                Destroy(li.GetComponent<Shadow>());
                li.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8f, 6f), new Vector2(side, side));
            }
            var name = LGBuild.Label(card, law.Name, 15, Color.white, TextAnchor.UpperLeft, bold: true, wrap: true);
            name.rectTransform.TopBand(y, 40, 14, law.Default ? 70f : 14f);
            y += Mathf.Clamp(name.preferredHeight, 20f, 40f) + 4f;
            if (law.Default)
            {
                var tag = LGBuild.Label(card, Loc.T("ИСХОДНЫЙ"), 9, Muted, TextAnchor.UpperRight, bold: true);
                tag.rectTransform.TopBand(14, 14, 14, 14);
            }
            var desc = LGBuild.Label(card, law.Desc, 11, Primary, TextAnchor.UpperLeft, wrap: true);
            desc.lineSpacing = 1.05f;
            float descH = Mathf.Clamp(desc.preferredHeight, 14f, 48f);

            // Текст не должен заезжать на строку платы: при тесноте эффекты мельче, затем описание короче
            // (полный текст закона — во всплывающей подсказке)
            float limit = card.rect.height > 10f ? card.rect.height - 70f : float.MaxValue;
            int rows = Mathf.Max(1, law.Effects.Count);
            bool compact = y + descH + 6f + rows * 16f > limit;
            float step = compact ? 13f : 16f;
            if (y + descH + 6f + rows * step > limit)
            {
                descH = Mathf.Max(14f, limit - y - 6f - rows * step);
                desc.verticalOverflow = VerticalWrapMode.Truncate;
            }
            desc.rectTransform.TopBand(y, descH, 14, 14);
            y += descH + 6f;

            foreach (var (text, good) in law.Effects)
            {
                var e = LGBuild.Label(card, (good ? "▲ " : "▼ ") + text, compact ? 10 : 11, good ? Good : Bad, TextAnchor.UpperLeft, bold: true, wrap: true);
                e.rectTransform.TopBand(y, compact ? 13f : 30f, 14, 14);
                y += compact ? step : Mathf.Clamp(e.preferredHeight, 14f, 30f) + 2f;
            }
            if (law.Effects.Count == 0)
            {
                var e = LGBuild.Label(card, Loc.T("Без особых эффектов"), 11, Muted, TextAnchor.UpperLeft);
                e.rectTransform.TopBand(y, 16, 14, 14);
            }

            // Низ карточки: плата и кнопка
            var upkeep = LGBuild.Label(card, law.Upkeep > 0.01f ? Loc.F("−{0:0.#} влияния в месяц", law.Upkeep) : Loc.T("бесплатно"), 11,
                law.Upkeep > 0.01f ? new Color(0.78f, 0.65f, 1f) : Muted, TextAnchor.LowerLeft, bold: true);
            upkeep.rectTransform.anchorMin = new Vector2(0, 0); upkeep.rectTransform.anchorMax = new Vector2(1, 0);
            upkeep.rectTransform.offsetMin = new Vector2(14, 50); upkeep.rectTransform.offsetMax = new Vector2(-14, 68);

            string blocker = EmpireLaws.Blocker(law);
            Button btn;
            if (active)
            {
                btn = LGBuild.Button(card, "Active", new Color(cc.r * 0.25f, cc.g * 0.25f, cc.b * 0.25f), new Color(cc.r, cc.g, cc.b, 0.8f), null, LGIcon.Check, Loc.T("ДЕЙСТВУЕТ"), 11);
                btn.interactable = false;
            }
            else
            {
                bool can = blocker == null;
                btn = LGBuild.Button(card, "Enact", can ? new Color(0.05f, 0.20f, 0.15f) : new Color(0.04f, 0.05f, 0.06f),
                    can ? new Color(0.36f, 0.96f, 0.68f, 0.8f) : new Color(1f, 1f, 1f, 0.12f), () => Enact(law), null,
                    can ? Loc.F("ПРИНЯТЬ · {0:0} влияния", EmpireLaws.SwitchCost) : blocker, 10);
                btn.interactable = can;
            }
            var br = (RectTransform)btn.transform;
            br.anchorMin = new Vector2(0, 0); br.anchorMax = new Vector2(1, 0); br.pivot = new Vector2(0.5f, 0);
            br.offsetMin = new Vector2(12, 12); br.offsetMax = new Vector2(-12, 46);
            TooltipHelper.Attach(bg.gameObject, LawTooltip(law, blocker, active));   // подложка ловит наведение
            LGFx.HoverGlow(bg.gameObject);
            if (_cascade) LGFx.CascadeIn(card.gameObject, _cascadeIndex++);
        }

        private static string LawTooltip(LawDef law, string blocker, bool active)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(law.Name).Append("</b>\n").Append(law.Desc).Append('\n');
            foreach (var (text, good) in law.Effects)
                sb.Append(good ? "<color=#66F29E>▲ " : "<color=#FF7A73>▼ ").Append(text).Append("</color>\n");
            if (law.Upkeep > 0.01f) sb.Append(Loc.F("Плата: {0:0.#} влияния в месяц", law.Upkeep)).Append('\n');
            if (active) sb.Append(Loc.T("<color=#8AA2A8>Действует сейчас.</color>"));
            else if (blocker != null) sb.Append("<color=#FF8A8A>").Append(blocker).Append("</color>");
            else sb.Append(Loc.F("<color=#8AA2A8>Принять за {0:0} влияния; направление закроется на {1} дней.</color>", EmpireLaws.SwitchCost, EmpireLaws.CooldownDays));
            return sb.ToString();
        }

        private void Enact(LawDef law)
        {
            if (EmpireLaws.TryEnact(law)) SFXManager.Play(Sfx.UiConfirm, 0.7f);
            else SFXManager.Play(Sfx.UiDenied, 0.7f);
            _key = "";
            Refresh();
        }
    }
}
