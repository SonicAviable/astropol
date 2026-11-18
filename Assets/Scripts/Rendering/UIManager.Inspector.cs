using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Левая панель выбранной системы: стопка карточек вместо одного текстового блока —
    /// плашки характеристик, ресурсы с иконками, полосы прочности звёздной базы, осада,
    /// флоты на орбите и подсказка, что делать. Высота панели подстраивается под содержимое;
    /// клик по пустому месту карты закрывает панель.
    /// </summary>
    public partial class UIManager
    {
        private RectTransform _inspStack;
        private CanvasGroup _inspBodyGroup;

        // ==================== ОТКРЫТИЕ / ЗАКРЫТИЕ ====================

        private void HideInspector()
        {
            _inspTargetAlpha = 0f;
            if (_inspectorGroup != null) _inspectorGroup.blocksRaycasts = false;
            if (!_isInSystemMode) GalaxyView.Instance?.SelectSystem(-1);
        }

        /// <summary>Клик по пустому месту галактики — панель системы закрывается.</summary>
        private void HandleEmptyMapClick()
        {
            if (_isInSystemMode || _inspTargetAlpha <= 0f) return;
            HideInspector();
        }

        /// <summary>Высота панели — по содержимому, но не ниже 240 и не до самого низа экрана.</summary>
        private void FitInspectorHeight(float bottomArea)
        {
            if (_inspStack == null || _inspectorRect == null || _canvas == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_inspStack);
            float content = LayoutUtility.GetPreferredHeight(_inspStack);
            float canvasH = ((RectTransform)_canvas.transform).rect.height;
            float maxH = Mathf.Max(320f, canvasH - InspectorTop - 196f);
            float h = Mathf.Clamp(InspHeaderH - 2f + content + bottomArea + 6f, 280f, maxH);
            _inspectorRect.sizeDelta = new Vector2(InspectorWidth, h);
        }

        // ==================== КАРТОЧКИ ====================

        private static Color InspMuted => DS.TextMuted;

        /// <summary>
        /// Заголовок секции: золотой ромб-маркер со свечением, значок, надпись с разрядкой и черта,
        /// тающая вправо (как разделы в меню больших игр).
        /// </summary>
        private void InspGap(float h) => LGBuild.Height(LGBuild.Rect(_inspStack, "Gap").gameObject, h);

        /// <summary>Заголовок раздела с пояснением справа (например, «добыча / залежи»).</summary>
        private void InspSection(string title, LGIcon icon, string hint)
        {
            InspSection(title, icon);
            var row = _inspStack.GetChild(_inspStack.childCount - 1);
            var h = LGBuild.Label(row, hint, 10, InspMuted, TextAnchor.LowerRight);
            h.font = GameFont.Mono;
            h.rectTransform.Stretch(0, 4, 2, 0);
        }

        private void InspSection(string title, LGIcon icon)
        {
            var row = LGBuild.Rect(_inspStack, "Section");
            LGBuild.Height(row.gameObject, 30);
            // «Атлас»: заголовок раздела — моноширинная золотая подпись с разрядкой и тонкая кремовая линия под ней
            Color g = DS.Gold;
            var t = LGBuild.Label(row, title, 11, g, TextAnchor.LowerLeft);
            t.font = GameFont.Mono;
            t.rectTransform.Stretch(2, 4, 0, 0);
            t.Track(1.6f);
            var line = LGBuild.Panel(row, "Line", new Color(DS.TextPrimary.r, DS.TextPrimary.g, DS.TextPrimary.b, 0.16f));
            line.raycastTarget = false;
            line.rectTransform.anchorMin = new Vector2(0, 0);
            line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.pivot = new Vector2(0, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 1.5f);
            line.rectTransform.anchoredPosition = Vector2.zero;
        }

        private void InspText(string text)
        {
            var t = LGBuild.Label(_inspStack, text, 11, DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            t.lineSpacing = 1.2f;
        }

        /// <summary>Ряд одинаковых плашек: иконка, крупное значение, подпись.</summary>
        private void InspChips(params (LGIcon icon, string value, string label, Color col)[] items)
        {
            var row = LGBuild.Rect(_inspStack, "Chips");
            LGBuild.Height(row.gameObject, 52);
            int n = items.Length;
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                var chip = LGBuild.Panel(row, "Chip", new Color(0.075f, 0.118f, 0.200f, 0.9f));
                chip.rectTransform.anchorMin = new Vector2(i / (float)n, 0);
                chip.rectTransform.anchorMax = new Vector2((i + 1) / (float)n, 1);
                chip.rectTransform.offsetMin = new Vector2(i == 0 ? 0 : 3, 0);
                chip.rectTransform.offsetMax = new Vector2(i == n - 1 ? 0 : -3, 0);
                // «Атлас»: плашка — чернила с едва видной кромкой; цвет смысла — только у значка
                var chipFx = LG.Platter(chip.gameObject, 3f);
                chipFx.AsSurface = true;
                chipFx.SetRim(new Color(0.925f, 0.898f, 0.827f, 0.10f));
                var ic = LGIcons.Create(chip.transform, it.icon, 16, LGTone.AtlasFill(it.col));
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(16, 16));
                var v = LGBuild.Label(chip.transform, it.value, 14, DS.TextPrimary, TextAnchor.UpperLeft);
                v.font = GameFont.MonoMedium;
                v.rectTransform.Stretch(34, 0, 4, 7);
                var l = LGBuild.Label(chip.transform, it.label, 9, InspMuted, TextAnchor.LowerLeft);
                l.rectTransform.Stretch(34, 7, 4, 0);
            }
        }

        private void InspBar(LGIcon icon, string label, float value, float max, Color col)
        {
            // «Атлас»: как шкала ресурса — значок и подпись слева, тонкая шкала, «значение / предел» моноширинным справа
            col = LGTone.AtlasFill(col);
            var row = LGBuild.Rect(_inspStack, "Bar_" + label);
            LGBuild.Height(row.gameObject, 26);
            var ic = LGIcons.Create(row, icon, 13, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(13, 13));
            var l = LGBuild.Label(row, label, 12, DS.TextPrimary, TextAnchor.MiddleLeft);
            l.rectTransform.Stretch(22, 0, 0, 0);
            var v = LGBuild.Label(row, $"<color=#ECE5D3>{value:0}</color> / {max:0}", 11, InspMuted, TextAnchor.MiddleRight);
            v.font = GameFont.Mono;
            v.rectTransform.Stretch(0, 0, 2, 0);
            var host = LGBuild.Rect(row, "Track");
            host.anchorMin = new Vector2(0, 0.5f); host.anchorMax = new Vector2(1, 0.5f);
            host.pivot = new Vector2(0.5f, 0.5f);
            host.offsetMin = new Vector2(132, -2); host.offsetMax = new Vector2(-92, 2);
            GrowBar(host, col, max > 0f ? value / max : 0f, 4f, false);
        }

        /// <summary>Строка с иконкой и текстом (статус, предупреждение, флот на орбите).</summary>
        private void InspNote(LGIcon icon, string text, Color col)
        {
            // «Атлас»: заметка — чернильная строка, цвет смысла у черты слева и значка
            var row = LGBuild.Panel(_inspStack, "Note", new Color(0.075f, 0.118f, 0.200f, 0.85f));
            LGBuild.Height(row.gameObject, 40);
            var noteFx = LG.Platter(row.gameObject, 3f);
            noteFx.AsSurface = true;
            noteFx.SetRim(new Color(0.925f, 0.898f, 0.827f, 0.08f));
            col = LGTone.AtlasFill(col);
            // Полоска цвета слева и значок в круглой ячейке со свечением
            var bar = LGBuild.Panel(row.transform, "Accent", new Color(col.r, col.g, col.b, 0.9f));
            bar.raycastTarget = false;
            bar.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(3, 0), new Vector2(2f, 24));
            var cg = LGBuild.Panel(row.transform, "IconGlow", new Color(0f, 0f, 0f, 0f));   // «Атлас»: без ореола
            cg.sprite = FactionSelectScreen.RadialSprite();
            cg.raycastTarget = false;
            cg.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 0), new Vector2(34, 34));
            var ic = LGIcons.Create(row.transform, icon, 15, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 0), new Vector2(15, 15));
            var t = LGBuild.Label(row.transform, text, 11, DS.TextPrimary, TextAnchor.MiddleLeft, wrap: true);
            t.rectTransform.Stretch(40, 0, 8, 0);
        }

        // ==================== СИСТЕМА ====================

        private static string StarClassLabel(StarSpectralClass c) => StarTypes.Label(c);

        private static string StarClassHint(StarSpectralClass c) => StarTypes.Hint(c);

        private void BuildSystemCards(StarSystem system)
        {
            // «Схема»: факты 2×2 сразу под схемой системы
            InspFactsGrid(system);

            // Ресурсы
            InspSection(Loc.T("РЕСУРСЫ"), LGIcon.Minerals, Loc.T("добыча / залежи"));
            if (system.IsSurveyed)
            {
                system.RecalculateHarvest();
                InspMeter(LGIcon.Minerals, Loc.T("Титан"), Loc.T("добыча / залежи"), system.HarvestedMinerals, system.TotalMinerals, new Color(0.30f, 0.94f, 0.55f));
                InspMeter(LGIcon.Energy, Loc.T("Гелий-3"), Loc.T("добыча / залежи"), system.HarvestedEnergy, system.TotalEnergy, new Color(1f, 0.80f, 0.32f));
            }
            else InspNote(LGIcon.Sensors, Loc.T("Состав системы неизвестен — отправьте научный корабль"), new Color(1f, 0.67f, 0.53f));

            // Меню системы: звёздная база (окно), флоты на орбите, снабжение (раскрывается), обзор системы
            InspGap(6f);
            InspSystemMenu(system);


            // Осада
            var siege = SiegeManager.Instance?.GetSiege(system.Id);
            if (siege != null && system.OwnerId >= 0)
            {
                float need = SiegeManager.RequiredDays(system);
                bool mine = siege.Attacker == 0;
                Color sc = mine ? DS.Green : DS.Red;
                InspSection(Loc.T("ОСАДА"), LGIcon.Siege);
                InspBar(LGIcon.Siege, mine ? Loc.T("Ваша осада") : Loc.T("Враг осаждает"), siege.Progress, need, sc);
                string state = siege.BaseHolding ? Loc.T("Приостановлена: звёздная база в строю")
                             : siege.Contested ? Loc.T("Приостановлена: на орбите флот защитника")
                             : siege.Active ? Loc.F("Идёт: {0:0} из {1:0} дн.", siege.Progress, need)
                             : Loc.T("Осаждающие ушли — прогресс спадает");
                InspText($"<color=#A9A392>{state}</color>");

                // Наземный штурм колонии: десант из резерва империи, сколько поместится на корабли на орбите
                if (mine && StellarisClone.Cards.AssaultLink.IsColony(system) && StellarisClone.Cards.ArmyManager.Instance != null)
                {
                    int deck = StellarisClone.Cards.ArmyManager.Instance.AttackDeck(0, system.Id).Count;
                    int places = StellarisClone.Cards.ArmyManager.LandingPlaces(0, system.Id);
                    string blocker = StellarisClone.Cards.AssaultLink.Blocker(0, system);
                    InspNote(LGIcon.Weapons, deck > 0
                        ? Loc.F("Десант: до {0} карт из резерва империи (мест на кораблях: {1}). Карты выбираются перед штурмом", deck, places)
                        : Loc.T("Резерв армии пуст — в бой пойдут только экипажи. Состав армии — в «Арсенале» (J)"), deck > 0 ? DS.Gold : DS.TextMuted);
                    string tip = Loc.F("<b>Наземный штурм</b>\nКарточный бой за колонию: 3 сектора, {0} ходов. Победа — колония ваша сразу, не дожидаясь конца осады. Погибшие карты теряются навсегда, выжившие получают опыт.\n<color=#A9A392>Сначала откроется «Сбор десанта» — выберите, какими картами нападать</color>", StellarisClone.Cards.BattleState.Turns);
                    InspButton(Loc.T("ШТУРМ"), LGIcon.Target, new Color(0.55f, 0.16f, 0.12f, 0.95f), blocker, tip,
                        () => StellarisClone.Cards.AssaultDeckWindow.Open(system), system);
                    InspButton(Loc.T("АВТОБОЙ"), LGIcon.Fast, DS.BtnNeutral, blocker, Loc.T("Быстрый штурм без экрана боя и без выбора карт: в десант пойдут лучшие карты резерва, исход рассчитают правила"),
                        () => StellarisClone.Cards.AssaultLink.StartPlayerAssault(system, true), system);
                }
            }

            // Вольный мир — свои карточки вместо управления
            var neutral = NeutralWorlds.At(system.Id);
            if (neutral != null && system.OwnerId == NeutralWorlds.Owner)
            {
                BuildNeutralCards(system, neutral);
                return;
            }

            // Секции «На орбите» и «Управление» убраны: флоты видны на карте, подсказки — в нижней кнопке действия
        }

        /// <summary>Флоты на орбите системы по владельцам: число кораблей и боевая мощь.</summary>
        private static SortedDictionary<int, (int count, float power)> FleetsInOrbit(int systemId)
        {
            var result = new SortedDictionary<int, (int count, float power)>();
            var fm = FleetManager.Instance;
            if (fm == null) return result;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.CurrentSystemId != systemId || d.State == FleetState.InHyperlane) continue;
                result.TryGetValue(d.OwnerId, out var cur);
                result[d.OwnerId] = (cur.count + 1, cur.power + CombatMath.Power(d));
            }
            return result;
        }
    }
}
