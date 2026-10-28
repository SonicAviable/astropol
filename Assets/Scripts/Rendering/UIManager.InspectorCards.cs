using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Карточки окна системы в духе больших стратегий: строка характеристик с разделителями,
    /// тонкие шкалы ресурсов и карточка звёздной базы — крупная мощь, полосы щитов, брони и корпуса,
    /// знак укрепления с «пипсами» уровня, прогресс стройки и кнопка с ценой. Пояснения — в подсказках.
    /// </summary>
    public partial class UIManager
    {
        private static readonly Color InspCardBg = new Color(0.015f, 0.04f, 0.06f, 0.96f);
        private static readonly Color InspLabelCol = new Color(0.56f, 0.67f, 0.73f);
        private static readonly Color ShieldCol = new Color(0.45f, 0.76f, 1f);
        private static readonly Color ArmorCol = new Color(0.88f, 0.76f, 0.56f);
        private static readonly Color HullCol = new Color(0.78f, 0.86f, 0.90f);

        /// <summary>Поставить элемент в полосу карточки: доли ширины x0…x1, отступ сверху и высота в пикселях.</summary>
        private static RectTransform Place(RectTransform rt, float x0, float x1, float top, float h, float padL = 0f, float padR = 0f)
        {
            rt.anchorMin = new Vector2(x0, 1);
            rt.anchorMax = new Vector2(x1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(padL, -top - h);
            rt.offsetMax = new Vector2(-padR, -top);
            return rt;
        }

        private static Text CardLabel(Transform card, string text, int size, Color col, TextAnchor anchor, bool bold,
                                      float x0, float x1, float top, float h, float padL = 0f, float padR = 0f, float track = 0f)
        {
            var t = LGBuild.Label(card, text, size, col, anchor, bold);
            Place(t.rectTransform, x0, x1, top, h, padL, padR);
            if (track > 0f) t.Track(track);
            return t;
        }

        // ==================== СТРОКА ХАРАКТЕРИСТИК ====================

        /// <summary>Одна плашка на несколько характеристик: значок, значение, подпись; между ячейками — тонкие разделители.</summary>
        private void InspStatLine(params (LGIcon icon, string value, string label, Color col)[] items)
        {
            var row = LGBuild.Panel(_inspStack, "Stats", InspCardBg);
            LGBuild.Height(row.gameObject, 54);
            LGFx.HoverGlow(row.gameObject);
            LG.Platter(row.gameObject, 10f).SetRim(new Color(1f, 1f, 1f, 0.08f));
            int n = items.Length;
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                var cell = LGBuild.Rect(row.transform, "Cell");
                cell.anchorMin = new Vector2(i / (float)n, 0);
                cell.anchorMax = new Vector2((i + 1) / (float)n, 1);
                cell.offsetMin = cell.offsetMax = Vector2.zero;
                var glow = LGBuild.Panel(cell, "Glow", new Color(it.col.r, it.col.g, it.col.b, 0.3f));
                glow.sprite = FactionSelectScreen.RadialSprite();
                glow.raycastTarget = false;
                glow.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 0), new Vector2(40, 40));
                var chip = LGBuild.Panel(cell, "IconChip", new Color(it.col.r * 0.12f, it.col.g * 0.12f, it.col.b * 0.12f, 0.95f));
                chip.raycastTarget = false;
                chip.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 0), new Vector2(28, 28));
                LG.Chip(chip.gameObject, new Color(it.col.r, it.col.g, it.col.b, 0.55f), 14f);
                var ic = LGIcons.Create(cell, it.icon, 15, it.col);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 0), new Vector2(15, 15));
                var v = LGBuild.Label(cell, it.value, 16, DS.TextPrimary, TextAnchor.LowerLeft, bold: true);
                v.rectTransform.Stretch(42, 24, 4, 4);
                var l = LGBuild.Label(cell, it.label, 9, InspLabelCol, TextAnchor.UpperLeft);
                l.rectTransform.Stretch(42, 4, 4, 27);
                if (i < n - 1)
                {
                    var sep = LGBuild.Panel(cell, "Sep", new Color(1f, 1f, 1f, 0.08f));
                    sep.rectTransform.anchorMin = new Vector2(1, 0.2f);
                    sep.rectTransform.anchorMax = new Vector2(1, 0.8f);
                    sep.rectTransform.sizeDelta = new Vector2(1, 0);
                }
            }
        }

        // ==================== ШКАЛА РЕСУРСА ====================

        /// <summary>Название и «добыча / залежи» в строку, под ними — тонкая шкала. Пустые залежи — серая строка.</summary>
        private void InspMeter(LGIcon icon, string label, string hint, float value, float max, Color col)
        {
            var row = LGBuild.Rect(_inspStack, "Meter_" + label);
            LGBuild.Height(row.gameObject, 30);
            bool empty = max <= 0f;
            Color c = empty ? InspLabelCol : col;
            var ic = LGIcons.Create(row, icon, 14, c);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(2, -2), new Vector2(14, 14));
            CardLabel(row, label, 11, empty ? InspLabelCol : DS.TextPrimary, TextAnchor.MiddleLeft, true, 0f, 0.6f, 0f, 18f, 22f);
            CardLabel(row, empty ? Loc.T("залежей нет") : $"{value:0} / {max:0}  <color=#{ColorUtility.ToHtmlStringRGB(InspLabelCol)}><size=9>{hint}</size></color>",
                11, c, TextAnchor.MiddleRight, true, 0.35f, 1f, 0f, 18f, 0f, 2f);
            var track = LGBuild.Rect(row, "Track");
            Place(track, 0f, 1f, 21f, 6f, 22f, 2f);
            GrowBar(track, c, empty ? 0f : value / max, 4f, value > 0f);
        }

        // ==================== ЗВЁЗДНАЯ БАЗА ====================

        private void InspStarbaseCard(StarSystem system, CombatManager.Starbase sb)
        {
            var cm = CombatManager.Instance;
            int lvl = system.Fortification;
            bool mine = system.OwnerId == 0;
            Color owner = FleetIndicator.OwnerColor(system.OwnerId);
            Color gold = DS.Gold;

            var card = LGBuild.Panel(_inspStack, "StarbaseCard", InspCardBg).rectTransform;
            LG.Platter(card.gameObject, 12f).SetRim(new Color(owner.r, owner.g, owner.b, 0.32f));
            LGFx.HoverGlow(card.gameObject);
            // Свет цвета владельца из-за эмблемы — карточка «подсвечена» своей станцией
            var cardGlow = LGBuild.Panel(card, "OwnerLight", new Color(owner.r, owner.g, owner.b, 0.14f));
            cardGlow.sprite = FactionSelectScreen.RadialSprite();
            cardGlow.raycastTarget = false;
            cardGlow.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(40, -40), new Vector2(260, 160));
            float y = 12f;

            // Эмблема станции: значок в ячейке и голографическое кольцо цвета владельца
            var emb = LGBuild.Rect(card, "Emblem");
            emb.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(38, -(y + 26f)), new Vector2(60, 60));
            var ringSp = ResSprite("UI/CardBattle/holo_ring_a");
            if (ringSp != null)
            {
                var ring = LGBuild.Panel(emb, "Ring", new Color(owner.r, owner.g, owner.b, sb.Disabled ? 0.25f : 0.75f));
                ring.sprite = ringSp;
                ring.raycastTarget = false;
                ring.material = StellarisClone.Cards.UIFx.HoloMat;
                ring.rectTransform.Stretch();
                LG.Ignore(ring.gameObject);
                StellarisClone.Cards.HoloSpin.Add(ring, sb.Disabled ? 0f : 12f, 0.15f);
            }
            var ec = LGBuild.Panel(emb, "Cell", new Color(owner.r * 0.12f, owner.g * 0.12f, owner.b * 0.12f, 0.96f));
            ec.raycastTarget = false;
            ec.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38, 38));
            LG.Chip(ec.gameObject, new Color(owner.r, owner.g, owner.b, 0.6f), 19f);
            var ei = LGIcons.Create(emb, LGIcon.Starbase, 20, sb.Disabled ? DS.Red : Color.Lerp(owner, Color.white, 0.35f));
            ei.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            const float px = 74f;   // текст — правее эмблемы

            // Мощь — крупно («набегает» при открытии); сравнение с вашими кораблями — под ней
            float power = cm != null ? cm.StarbasePower(system.Id) : 0f;
            string inShips = CombatManager.PowerInShips(power);
            CardLabel(card, Loc.T("МОЩЬ БАЗЫ"), 9, InspLabelCol, TextAnchor.UpperLeft, true, 0f, 0.6f, y, 12f, px, 0f, 1.6f);
            var powerText = CardLabel(card, power.ToString("N0"), 28, sb.Disabled ? DS.Red : DS.TextPrimary, TextAnchor.UpperLeft, true, 0f, 0.6f, y + 11f, 34f, px);
            var psh = powerText.gameObject.AddComponent<Shadow>();
            psh.effectColor = new Color(owner.r, owner.g, owner.b, 0.35f);
            psh.effectDistance = new Vector2(0f, -2f);
            InspCountUp.Run(powerText, power);
            if (inShips.Length > 0)
                CardLabel(card, inShips, 10, InspLabelCol, TextAnchor.UpperLeft, false, 0f, 0.7f, y + 46f, 14f, px);
            var powerHit = LGBuild.Panel(card, "PowerTip", new Color(0f, 0f, 0f, 0f), raycast: true);
            Place(powerHit.rectTransform, 0f, 0.55f, y, 60f);
            TooltipHelper.Attach(powerHit.gameObject,
                Loc.F("<b>Мощь звёздной базы: {0:N0}</b>\nЗалп {1:0} урона. Мощь считается так же, как у кораблей (значок мечей в панели флота): ", power, sb.Damage) +
                Loc.T("сравните с силой своего флота перед атакой.\nПока база в строю, осада системы не начнётся."));

            // Укрепление — знак справа: название и три «пипса» уровня
            CardLabel(card, Fortifications.Name(lvl).ToUpper(), 11, lvl > 0 ? gold : InspLabelCol, TextAnchor.UpperRight, true, 0.45f, 1f, y + 2f, 14f, 0f, 14f, 1.2f);
            // Уровень — ромбы: построенные горят золотом со свечением, остальные — пустые грани
            var pips = LGBuild.Rect(card, "Pips");
            Place(pips, 0.5f, 1f, y + 20f, 12f, 0f, 14f);
            for (int i = 0; i < Fortifications.MaxLevel; i++)
            {
                bool on = i < lvl;
                float x = -(Fortifications.MaxLevel - 1 - i) * 20f - 6f;
                if (on)
                {
                    var pg = LGBuild.Panel(pips, "PipGlow", new Color(gold.r, gold.g, gold.b, 0.45f));
                    pg.sprite = FactionSelectScreen.RadialSprite();
                    pg.raycastTarget = false;
                    pg.rectTransform.At(new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(26, 26));
                    StellarisClone.Cards.HoloSpin.Add(pg, 0f, 0.25f, 0f, i * 0.9f);
                }
                var pip = LGBuild.Panel(pips, "Pip", on ? gold : new Color(0.04f, 0.07f, 0.09f, 1f));
                pip.raycastTarget = false;
                pip.rectTransform.At(new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(9, 9));
                pip.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
                if (!on)
                {
                    var rim = pip.gameObject.AddComponent<Outline>();
                    rim.effectColor = new Color(gold.r, gold.g, gold.b, 0.35f);
                    rim.effectDistance = new Vector2(1f, -1f);
                }
            }
            CardLabel(card, Loc.F("УКРЕПЛЕНИЕ {0}/{1}", lvl, Fortifications.MaxLevel), 9, InspLabelCol, TextAnchor.UpperRight, true, 0.45f, 1f, y + 32f, 12f, 0f, 14f, 1.2f);
            var fortHit = LGBuild.Panel(card, "FortTip", new Color(0f, 0f, 0f, 0f), raycast: true);
            Place(fortHit.rectTransform, 0.55f, 1f, y, 46f);
            TooltipHelper.Attach(fortHit.gameObject, FortTooltip(lvl));
            y += 64f;

            if (sb.Disabled)
            {
                CardLabel(card, Loc.F("ВЫВЕДЕНА ИЗ СТРОЯ · ремонт {0:0}% из 50%", sb.Hull / Mathf.Max(1f, sb.MaxHull) * 100f), 10, DS.Red,
                    TextAnchor.MiddleLeft, true, 0f, 1f, y, 16f, 14f, 14f);
                y += 20f;
            }

            // Прочность: щиты → броня → корпус (в этом порядке их и снимает враг)
            DurabilityRow(card, ref y, LGIcon.ShieldDome, Loc.T("Щиты"), sb.Shields, sb.MaxShields, ShieldCol);
            DurabilityRow(card, ref y, LGIcon.ArmorPlate, Loc.T("Броня"), sb.Armor, sb.MaxArmor, ArmorCol);
            DurabilityRow(card, ref y, LGIcon.Ship, Loc.T("Корпус"), sb.Hull, sb.MaxHull, HullCol);
            y += 4f;

            if (mine) FortificationAction(card, system, lvl, ref y);
            LGBuild.Height(card.gameObject, y + 10f);
        }

        private static void DurabilityRow(RectTransform card, ref float y, LGIcon icon, string label, float value, float max, Color col)
        {
            var ic = LGIcons.Create(card, icon, 11, col);
            Place(ic.rectTransform, 0f, 0f, y + 3f, 11f, 14f, -25f);
            CardLabel(card, label, 10, InspLabelCol, TextAnchor.MiddleLeft, false, 0f, 0.3f, y, 17f, 31f);
            var track = LGBuild.Rect(card, "Track");
            Place(track, 0.27f, 0.75f, y, 17f, 0f, 6f);
            GrowBar(track, col, max > 0f ? value / max : 0f, 5f, icon == LGIcon.ShieldDome);
            CardLabel(card, $"{value:0} / {max:0}", 10, col, TextAnchor.MiddleRight, true, 0.7f, 1f, y, 17f, 0f, 14f);
            y += 20f;
        }

        private static string FortTooltip(int lvl)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Loc.F("<b>{0}</b> · уровень {1} из {2}", Fortifications.Name(lvl), lvl, Fortifications.MaxLevel));
            for (int i = 1; i <= Fortifications.MaxLevel; i++)
            {
                string c = i <= lvl ? "#F2C766" : "#8AA2A8";
                sb.Append(Loc.F("\n<color={0}>{1}</color>: прочность ×{2:0.##}, урон ×{3:0.##}, осада дольше ×{4:0.##}",
                    c, Fortifications.Name(i), Fortifications.DurabilityMult(i), Fortifications.DamageMult(i), Fortifications.SiegeMult(i)));
            }
            sb.Append(Loc.F("\n<color=#8AA2A8>Содержание +{0:0} гелия-3 за уровень. Штурм разрушает один уровень, передача по договору — нет.</color>",
                Fortifications.UpkeepPerLevel));
            return sb.ToString();
        }

        /// <summary>Стройка укрепления (прогресс) или кнопка следующего уровня с ценой.</summary>
        private void FortificationAction(RectTransform card, StarSystem system, int lvl, ref float y)
        {
            Color gold = DS.Gold;
            var line = LGBuild.Panel(card, "Divider", new Color(1f, 1f, 1f, 0.07f));
            Place(line.rectTransform, 0f, 1f, y, 1f, 14f, 14f);
            y += 10f;

            var job = Fortifications.PendingJob(system.Id);
            if (job != null)
            {
                CardLabel(card, Loc.F("СТРОИТСЯ · {0}", Fortifications.Name(lvl + 1).ToUpper()), 10, gold, TextAnchor.MiddleLeft, true, 0f, 0.75f, y, 14f, 14f, 0f, 1.2f);
                CardLabel(card, Loc.F("{0} дн.", Mathf.CeilToInt(job.DaysLeft)), 10, DS.TextPrimary, TextAnchor.MiddleRight, true, 0.6f, 1f, y, 14f, 0f, 14f);
                var track = LGBuild.Rect(card, "Progress");
                Place(track, 0f, 1f, y + 17f, 8f, 14f, 14f);
                LGFx.Shimmer(LGBuild.Bar(track, gold, job.Progress, 5f));
                y += 30f;
                return;
            }
            if (lvl >= Fortifications.MaxLevel)
            {
                CardLabel(card, Loc.T("Цитадель — наивысший уровень укреплений"), 10, InspLabelCol, TextAnchor.MiddleLeft, false, 0f, 1f, y, 14f, 14f, 14f);
                y += 18f;
                return;
            }

            int to = lvl + 1;
            float m = Fortifications.Minerals(to), a = Fortifications.Alloys(to), d = Fortifications.Days(to);
            string block = Fortifications.Blocker(system, 0);
            var eco = EconomyManager.Instance;
            if (block == null && (eco == null || !eco.CanAfford(0f, m, a, 0f))) block = CostCheck.Full(0f, m, a);
            bool can = block == null;

            var btn = LGBuild.Button(card, "Fortify", new Color(gold.r * (can ? 0.22f : 0.08f), gold.g * (can ? 0.18f : 0.07f), gold.b * (can ? 0.08f : 0.05f), 0.98f),
                new Color(gold.r, gold.g, gold.b, can ? 0.7f : 0.18f), () =>
                {
                    if (Fortifications.TryStartPlayer(system)) ShowSystemPanel(system);
                }, LGIcon.Starbase, Loc.F("УКРЕПИТЬ → {0}", Fortifications.Name(to).ToUpper()), 11);
            Place((RectTransform)btn.transform, 0f, 1f, y, 34f, 14f, 14f);
            btn.interactable = can;
            if (can) InspSweep.Add(btn.gameObject, 4.5f, 0.16f);
            TooltipHelper.Attach(btn.gameObject, FortTooltip(to) + (can ? "" : $"\n<color=#FF8888>{block}</color>"));
            y += 40f;

            // Цена значками: титан, сплавы, срок, содержание
            var cost = LGBuild.Rect(card, "Cost");
            Place(cost, 0f, 1f, y, 16f, 14f, 14f);
            float x = 0f;
            void Item(LGIcon icon, Color col, string text)
            {
                var ic = LGIcons.Create(cost, icon, 12, col);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(12, 12));
                var t = LGBuild.Label(cost, text, 10, DS.TextPrimary, TextAnchor.MiddleLeft, bold: true);
                t.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 16f, 0), new Vector2(70, 16));
                x += 16f + t.preferredWidth + 14f;
            }
            Item(LGIcon.Minerals, new Color(0.30f, 0.94f, 0.55f), m.ToString("0"));
            Item(LGIcon.Alloys, new Color(0.95f, 0.62f, 0.36f), a.ToString("0"));
            Item(LGIcon.Clock, InspLabelCol, Loc.F("{0:0} дн.", d));
            Item(LGIcon.Energy, new Color(1f, 0.80f, 0.32f), Loc.F("−{0:0}/мес", Fortifications.UpkeepPerLevel));
            y += 18f;
            if (!can)
            {
                CardLabel(card, block, 10, DS.Red, TextAnchor.UpperLeft, false, 0f, 1f, y + 2f, 28f, 14f, 14f).horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 30f;
            }
        }
    }
}
