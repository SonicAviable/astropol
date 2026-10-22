using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Вкладка «Решения» окна законов: пять колонок — особые решения фракции (с великим проектом) и общие
    /// экономика, наука, армия, общество. На карточке — что даст решение, цена (чего не хватает — красным),
    /// срок и перезарядка; идущее решение показывает полосу выполнения, причина «нельзя» — прямо на кнопке.
    /// </summary>
    public partial class LawsWindow
    {
        private static readonly Color GreatGold = new Color(1f, 0.80f, 0.36f);

        private void RefreshDecisions()
        {
            var eco = EconomyManager.Instance;
            float ti = eco != null ? eco.Minerals : 0f, cap = eco != null ? eco.MineralStorage : 0f;
            _subtitle.text = Loc.F("Титан: <b><color=#33E6CC>{0:0}</color></b> / {1:N0}   ·   идёт решений: <b>{2}</b> из {3}   ·   цены растут с размером империи: ×{4:0.00}",
                ti, cap, EmpireDecisions.ActiveCount, EmpireDecisions.MaxActive, EmpireDecisions.Scale);

            var sb = new System.Text.StringBuilder("dec|");
            sb.Append(FactionTraits.Key(0)).Append('|').Append(EmpireDecisions.Scale.ToString("0.00")).Append('|');
            foreach (var c in EmpireDecisions.Categories)
                foreach (var d in EmpireDecisions.Visible(c))
                    sb.Append(d.Id).Append(':').Append(EmpireDecisions.Blocker(d)).Append(':').Append(EmpireDecisions.IsDone(d)).Append('|');
            string key = sb.ToString();
            if (key == _key) return;
            _key = key;

            _title.text = Loc.T("РЕШЕНИЯ ИМПЕРИИ");
            _hint.text = Loc.F("Решение стоит ресурсов (больше всего — титана), идёт несколько дней и потом закрыто на перезарядку. Одновременно — не больше {0}. Великий проект — один раз за партию, навсегда.",
                EmpireDecisions.MaxActive);
            BuildTabs();
            LGBuild.Clear(_cols);

            // Карточки одной высоты во всех колонках — по самой длинной колонке
            int maxCards = 1;
            foreach (var c in EmpireDecisions.Categories)
            {
                int k = 0;
                foreach (var _ in EmpireDecisions.Visible(c)) k++;
                maxCards = Mathf.Max(maxCards, k);
            }
            int n = EmpireDecisions.Categories.Length;
            for (int i = 0; i < n; i++)
            {
                var col = LGBuild.Rect(_cols, "Col");
                col.anchorMin = new Vector2(i / (float)n, 0); col.anchorMax = new Vector2((i + 1) / (float)n, 1);
                col.offsetMin = new Vector2(i == 0 ? 0 : ColGap * 0.5f, 0); col.offsetMax = new Vector2(i == n - 1 ? 0 : -ColGap * 0.5f, 0);
                BuildDecisionColumn(col, EmpireDecisions.Categories[i], maxCards);
            }
            LG.Skin(_cols);
        }

        private static LGIcon DecisionCategoryIcon(DecisionCategory c) => c switch
        {
            DecisionCategory.Faction => LGIcon.Star,
            DecisionCategory.Economy => LGIcon.Industry,
            DecisionCategory.Science => LGIcon.Research,
            DecisionCategory.Military => LGIcon.Weapons,
            _ => LGIcon.Society
        };

        private static Color DecisionCategoryColor(DecisionCategory c) => c switch
        {
            DecisionCategory.Faction => FactionColor(),
            DecisionCategory.Economy => new Color(0.95f, 0.62f, 0.36f),
            DecisionCategory.Science => new Color(0.50f, 0.92f, 0.62f),
            DecisionCategory.Military => new Color(1f, 0.45f, 0.42f),
            _ => new Color(0.45f, 0.76f, 1f)
        };

        /// <summary>Цвет фракции игрока, подтянутый к яркости остальных колонок.</summary>
        private static Color FactionColor()
        {
            var f = FactionTraits.FactionOf(0);
            Color c = f != null ? f.EmpireColor : GreatGold;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Min(s, 0.7f), Mathf.Max(v, 0.85f));
        }

        private void BuildDecisionColumn(RectTransform col, DecisionCategory cat, int maxCards)
        {
            Color cc = DecisionCategoryColor(cat);
            var head = LGBuild.Rect(col, "Head");
            head.TopBand(0, 40);
            var ic = LGIcons.Create(head, DecisionCategoryIcon(cat), 20, cc);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(20, 20));
            var name = LGBuild.Label(head, EmpireDecisions.CategoryName(cat).ToUpper(), cat == DecisionCategory.Faction ? 13 : 15, Color.white, TextAnchor.MiddleLeft, bold: true);
            name.rectTransform.Stretch(32, 0, 0, 0);
            name.Track(1.4f);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            var line = LGBuild.Panel(col, "Line", new Color(cc.r, cc.g, cc.b, 0.5f));
            line.rectTransform.TopBand(42, 2);
            if (cat == DecisionCategory.Faction)
                TooltipHelper.Attach(head.gameObject, Loc.T("Особые решения вашей фракции — у других империй их нет. Последнее — великий проект."));

            var list = new List<DecisionDef>(EmpireDecisions.Visible(cat));
            float top = 54f;
            float avail = col.rect.height > 10f ? col.rect.height - top : H - 112f - 20f - top;
            float cardH = (avail - CardGap * (maxCards - 1)) / Mathf.Max(1, maxCards);
            for (int i = 0; i < list.Count; i++)
            {
                var card = LGBuild.Rect(col, "Card");
                card.TopBand(top + i * (cardH + CardGap), cardH);
                BuildDecisionCard(card, list[i], cc, cardH);
            }
        }

        private void BuildDecisionCard(RectTransform card, DecisionDef d, Color cc, float cardH)
        {
            bool active = EmpireDecisions.IsActive(d), done = EmpireDecisions.IsDone(d);
            Color accent = d.Once ? GreatGold : cc;
            var bg = LGBuild.Panel(card, "Bg", active || done ? new Color(accent.r * 0.13f, accent.g * 0.13f, accent.b * 0.13f, 0.95f)
                                                             : new Color(0.03f, 0.055f, 0.075f, 0.9f));
            bg.rectTransform.Stretch();
            LG.Platter(bg.gameObject, 8f).SetRim(active || done ? new Color(accent.r, accent.g, accent.b, 0.85f)
                                                 : d.Once ? new Color(accent.r, accent.g, accent.b, 0.30f) : new Color(1f, 1f, 1f, 0.10f));

            // Значок решения — крупный бледный водяной знак справа
            float side = Mathf.Min(cardH - 20f, 96f);
            var wm = LGIcons.Create(card, d.Icon, side, new Color(accent.r, accent.g, accent.b, 0.07f), "Watermark");
            var shadow = wm.GetComponent<Shadow>();
            if (shadow != null) Destroy(shadow);
            wm.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8f, 8f), new Vector2(side, side));

            float y = 10f;
            var ic = LGIcons.Create(card, d.Icon, 16, accent);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -y - 1f), new Vector2(16, 16));
            var tag = LGBuild.Label(card, d.Once ? Loc.T("ВЕЛИКИЙ ПРОЕКТ") : d.Days > 0 ? Loc.F("{0} дн.", d.Days) : Loc.T("СРАЗУ"), 9,
                d.Once ? GreatGold : Muted, TextAnchor.UpperRight, bold: true);
            tag.rectTransform.TopBand(y + 2f, 14, 12, 12);
            float tagW = tag.preferredWidth + 8f;
            var name = LGBuild.Label(card, d.Name, 14, Color.white, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.TopBand(y, 18, 34, 12f + tagW);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            y += 22f;

            // Низ карточки: кнопка (34) и строка цены (16) — текст сверху не должен на них заезжать
            const float bottom = 62f;
            float limit = cardH - bottom;

            var desc = LGBuild.Label(card, d.Desc, 10, Primary, TextAnchor.UpperLeft, wrap: true);
            desc.lineSpacing = 1.02f;
            desc.verticalOverflow = VerticalWrapMode.Truncate;
            float effH = d.Effects.Count * 14f;
            float descH = Mathf.Clamp(limit - y - effH - 4f, 0f, 38f);
            if (descH >= 12f) { desc.rectTransform.TopBand(y, descH, 12, 12); y += descH + 3f; }
            else desc.gameObject.SetActive(false);

            foreach (var (text, good) in d.Effects)
            {
                if (y + 12f > limit) break;
                var e = LGBuild.Label(card, (good ? "▲ " : "▼ ") + text(), 10, good ? Good : Bad, TextAnchor.UpperLeft, bold: true, wrap: true);
                e.verticalOverflow = VerticalWrapMode.Truncate;
                float eh = Mathf.Min(Mathf.Clamp(e.preferredHeight, 13f, 26f), limit - y);
                e.rectTransform.TopBand(y, eh, 12, 12);
                y += eh + 1f;
            }

            // Цена и перезарядка
            var cost = LGBuild.Rect(card, "Cost");
            cost.anchorMin = new Vector2(0, 0); cost.anchorMax = new Vector2(1, 0); cost.pivot = new Vector2(0, 0);
            cost.offsetMin = new Vector2(12, 44); cost.offsetMax = new Vector2(-90, 58);
            if (!done) BuildMenuModal.CostStrip(cost, d.EnergyCost, d.MineralsCost, d.AlloysCost, d.Influence, 11, TextAnchor.MiddleLeft);
            var cdText = LGBuild.Label(card, d.Once ? Loc.T("один раз") : Loc.F("перезарядка {0} дн.", d.Cooldown), 9, Muted, TextAnchor.MiddleRight, bold: true);
            cdText.rectTransform.anchorMin = new Vector2(0, 0); cdText.rectTransform.anchorMax = new Vector2(1, 0);
            cdText.rectTransform.offsetMin = new Vector2(12, 44); cdText.rectTransform.offsetMax = new Vector2(-12, 58);

            string blocker = EmpireDecisions.Blocker(d);
            if (active)
            {
                // Идёт: полоса выполнения вместо кнопки
                var box = LGBuild.Rect(card, "Running");
                box.anchorMin = new Vector2(0, 0); box.anchorMax = new Vector2(1, 0); box.pivot = new Vector2(0.5f, 0);
                box.offsetMin = new Vector2(12, 10); box.offsetMax = new Vector2(-12, 40);
                var lbl = LGBuild.Label(box, (d.Once ? Loc.F("СТРОИТСЯ · ещё {0} дн.", EmpireDecisions.DaysLeft(d))
                                                     : d.IsModifier ? Loc.F("ДЕЙСТВУЕТ · ещё {0} дн.", EmpireDecisions.DaysLeft(d))
                                                     : Loc.F("ИДЁТ · ещё {0} дн.", EmpireDecisions.DaysLeft(d))), 10, accent, TextAnchor.UpperLeft, bold: true);
                lbl.rectTransform.TopBand(0, 14);
                lbl.Track(0.8f);
                var track = LGBuild.Rect(box, "Bar");
                track.TopBand(18, 10);
                LGBuild.Bar(track, accent, EmpireDecisions.Progress(d), 6f);
            }
            else
            {
                Button btn;
                if (done)
                {
                    btn = LGBuild.Button(card, "Done", new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f), new Color(accent.r, accent.g, accent.b, 0.8f),
                        null, LGIcon.Check, Loc.T("ПОСТРОЕНО — ДЕЙСТВУЕТ НАВСЕГДА"), 10);
                    btn.interactable = false;
                }
                else
                {
                    bool can = blocker == null;
                    btn = LGBuild.Button(card, "Take", can ? new Color(0.05f, 0.20f, 0.15f) : new Color(0.04f, 0.05f, 0.06f),
                        can ? new Color(0.36f, 0.96f, 0.68f, 0.8f) : new Color(1f, 1f, 1f, 0.12f), () => Take(d), null,
                        can ? (d.Once ? Loc.T("НАЧАТЬ ПРОЕКТ") : Loc.T("ПРИНЯТЬ РЕШЕНИЕ")) : blocker, 10);
                    btn.interactable = can;
                }
                var br = (RectTransform)btn.transform;
                br.anchorMin = new Vector2(0, 0); br.anchorMax = new Vector2(1, 0); br.pivot = new Vector2(0.5f, 0);
                br.offsetMin = new Vector2(12, 10); br.offsetMax = new Vector2(-12, 40);
            }
            TooltipHelper.Attach(bg.gameObject, DecisionTooltip(d, blocker, active, done));
        }

        private static string DecisionTooltip(DecisionDef d, string blocker, bool active, bool done)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(d.Name).Append("</b>");
            if (d.Once) sb.Append(Loc.T("  <color=#FFCC5C>· великий проект</color>"));
            sb.Append('\n').Append(d.Desc).Append('\n');
            foreach (var (text, good) in d.Effects)
                sb.Append(good ? "<color=#66F29E>▲ " : "<color=#FF7A73>▼ ").Append(text()).Append("</color>\n");

            var parts = new List<string>();
            if (d.MineralsCost > 0f) parts.Add(Loc.F("{0:0} титана", d.MineralsCost));
            if (d.EnergyCost > 0f) parts.Add(Loc.F("{0:0} гелия-3", d.EnergyCost));
            if (d.AlloysCost > 0f) parts.Add(Loc.F("{0:0} сплавов", d.AlloysCost));
            if (d.Influence > 0f) parts.Add(Loc.F("{0:0} влияния", d.Influence));
            if (parts.Count > 0) sb.Append(Loc.F("<color=#8AA2A8>Цена:</color> {0}", string.Join(", ", parts))).Append('\n');
            sb.Append("<color=#8AA2A8>");
            sb.Append(d.Days > 0 ? Loc.F("Срок: {0} дн.", d.Days) : Loc.T("Срабатывает сразу."));
            sb.Append(d.Once ? Loc.T("  Принимается один раз за партию.") : Loc.F("  Перезарядка: {0} дн.", d.Cooldown));
            sb.Append("</color>\n");

            if (done) sb.Append(Loc.T("<color=#FFCC5C>Построено — бонусы действуют до конца партии.</color>"));
            else if (active) sb.Append(Loc.F("<color=#8AA2A8>Идёт: ещё {0} дн.</color>", EmpireDecisions.DaysLeft(d)));
            else if (blocker != null) sb.Append("<color=#FF8A8A>").Append(blocker).Append("</color>");
            else sb.Append(Loc.T("<color=#66F29E>Можно принять.</color>"));
            return sb.ToString();
        }

        private void Take(DecisionDef d)
        {
            if (EmpireDecisions.TryTake(d)) SFXManager.Play(Sfx.UiConfirm, 0.7f);
            else SFXManager.Play(Sfx.UiDenied, 0.7f);
            _key = "";
            Refresh();
        }
    }
}
