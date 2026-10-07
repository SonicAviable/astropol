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
            float maxH = Mathf.Max(320f, canvasH - (TopBarMargin + TopBarHeight + 14f) - 200f);
            float h = Mathf.Clamp(62f + content + bottomArea + 6f, 240f, maxH);
            _inspectorRect.sizeDelta = new Vector2(InspectorWidth, h);
        }

        // ==================== КАРТОЧКИ ====================

        private static Color InspMuted => DS.TextMuted;

        private void InspSection(string title, LGIcon icon)
        {
            var row = LGBuild.Rect(_inspStack, "Section");
            LGBuild.Height(row.gameObject, 24);
            var ic = LGIcons.Create(row, icon, 13, DS.Gold);
            ic.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(2, 4), new Vector2(13, 13));
            var t = LGBuild.Label(row, title, 10, DS.Gold, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.Stretch(20, 2, 0, 0);
            var line = LGBuild.Panel(row, "Line", new Color(DS.Gold.r, DS.Gold.g, DS.Gold.b, 0.2f));
            line.rectTransform.anchorMin = new Vector2(0, 0);
            line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 1);
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
                var chip = LGBuild.Panel(row, "Chip", new Color(it.col.r * 0.10f, it.col.g * 0.10f, it.col.b * 0.10f, 0.95f));
                chip.rectTransform.anchorMin = new Vector2(i / (float)n, 0);
                chip.rectTransform.anchorMax = new Vector2((i + 1) / (float)n, 1);
                chip.rectTransform.offsetMin = new Vector2(i == 0 ? 0 : 3, 0);
                chip.rectTransform.offsetMax = new Vector2(i == n - 1 ? 0 : -3, 0);
                LG.Platter(chip.gameObject, 12f).SetRim(new Color(it.col.r, it.col.g, it.col.b, 0.3f));
                var ic = LGIcons.Create(chip.transform, it.icon, 18, it.col);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(18, 18));
                var v = LGBuild.Label(chip.transform, it.value, 14, DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
                v.rectTransform.Stretch(34, 0, 4, 7);
                var l = LGBuild.Label(chip.transform, it.label, 9, InspMuted, TextAnchor.LowerLeft);
                l.rectTransform.Stretch(34, 7, 4, 0);
            }
        }

        private void InspBar(LGIcon icon, string label, float value, float max, Color col)
        {
            var row = LGBuild.Rect(_inspStack, "Bar_" + label);
            LGBuild.Height(row.gameObject, 22);
            var ic = LGIcons.Create(row, icon, 13, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(13, 13));
            var l = LGBuild.Label(row, label, 10, DS.TextPrimary, TextAnchor.MiddleLeft);
            l.rectTransform.Stretch(22, 0, 0, 0);
            var v = LGBuild.Label(row, $"{value:0} / {max:0}", 10, col, TextAnchor.MiddleRight, bold: true);
            v.rectTransform.anchorMin = new Vector2(0, 0); v.rectTransform.anchorMax = new Vector2(0, 1);
            v.rectTransform.offsetMin = new Vector2(84, 0); v.rectTransform.offsetMax = new Vector2(176, 0);
            var host = LGBuild.Rect(row, "Track");
            host.Stretch(186, 0, 2, 0);
            LGBuild.Bar(host, col, max > 0f ? value / max : 0f, 7f);
        }

        /// <summary>Строка с иконкой и текстом (статус, предупреждение, флот на орбите).</summary>
        private void InspNote(LGIcon icon, string text, Color col)
        {
            var row = LGBuild.Panel(_inspStack, "Note", new Color(col.r * 0.08f, col.g * 0.08f, col.b * 0.08f, 0.9f));
            LGBuild.Height(row.gameObject, 34);
            LG.Platter(row.gameObject, 10f).SetRim(new Color(col.r, col.g, col.b, 0.25f));
            var ic = LGIcons.Create(row.transform, icon, 15, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(15, 15));
            var t = LGBuild.Label(row.transform, text, 10, DS.TextPrimary, TextAnchor.MiddleLeft, wrap: true);
            t.rectTransform.Stretch(32, 0, 8, 0);
        }

        // ==================== СИСТЕМА ====================

        private static string StarClassLabel(StarSpectralClass c) => c switch
        {
            StarSpectralClass.BlackHole => "Дыра",
            _ => c.ToString().Replace("Class", "")
        };

        private static string StarClassHint(StarSpectralClass c) => c switch
        {
            StarSpectralClass.ClassM => "красный карлик",
            StarSpectralClass.ClassK => "оранжевая звезда",
            StarSpectralClass.ClassG => "жёлтая звезда",
            StarSpectralClass.ClassA => "белая звезда",
            StarSpectralClass.ClassB => "голубой гигант",
            _ => "чёрная дыра"
        };

        private void BuildSystemCards(StarSystem system)
        {
            var cyan = DS.NeonCyan;
            InspChips(
                (LGIcon.Star, StarClassLabel(system.SpectralClass), StarClassHint(system.SpectralClass), DS.Gold),
                (LGIcon.Planet, system.Planets.Count.ToString(), "орбитальных тел", cyan),
                (LGIcon.MapLanes, system.ConnectedSystemIds.Count.ToString(), "гиперкоридоров", new Color(0.62f, 0.78f, 1f)));

            // Ресурсы
            InspSection("РЕСУРСЫ", LGIcon.Minerals);
            if (system.IsSurveyed)
            {
                system.RecalculateHarvest();
                InspChips(
                    (LGIcon.Minerals, $"{system.HarvestedMinerals} / {system.TotalMinerals}", "титан: добыча / всего", new Color(0.30f, 0.94f, 0.55f)),
                    (LGIcon.Energy, $"{system.HarvestedEnergy} / {system.TotalEnergy}", "гелий-3: добыча / всего", new Color(1f, 0.80f, 0.32f)));
            }
            else InspNote(LGIcon.Sensors, "Состав системы неизвестен — отправьте научный корабль", new Color(1f, 0.67f, 0.53f));

            // Звёздная база
            var sb = CombatManager.Instance?.GetStarbase(system.Id);
            if (sb != null)
            {
                InspSection("ЗВЁЗДНАЯ БАЗА", LGIcon.Starbase);
                if (sb.Disabled)
                    InspNote(LGIcon.Warning, $"Выведена из строя · ремонт {sb.Hull / Mathf.Max(1f, sb.MaxHull) * 100f:0}% из 50%", DS.Red);
                InspBar(LGIcon.Ship, "Корпус", sb.Hull, sb.MaxHull, new Color(0.75f, 0.85f, 0.9f));
                InspBar(LGIcon.ArmorPlate, "Броня", sb.Armor, sb.MaxArmor, new Color(0.88f, 0.78f, 0.62f));
                InspBar(LGIcon.ShieldDome, "Щиты", sb.Shields, sb.MaxShields, new Color(0.45f, 0.76f, 1f));
                InspText($"<color=#8AA2A8>Огневая мощь</color> {sb.Damage:0} урона  ·  <color=#8AA2A8>мощь</color> {CombatManager.Instance.StarbasePower(system.Id):N0}");
            }

            // Осада
            var siege = SiegeManager.Instance?.GetSiege(system.Id);
            if (siege != null && system.OwnerId >= 0)
            {
                float need = SiegeManager.RequiredDays(system);
                bool mine = siege.Attacker == 0;
                Color sc = mine ? DS.Green : DS.Red;
                InspSection("ОСАДА", LGIcon.Siege);
                InspBar(LGIcon.Siege, mine ? "Ваша осада" : "Враг осаждает", siege.Progress, need, sc);
                string state = siege.BaseHolding ? "Приостановлена: звёздная база в строю"
                             : siege.Contested ? "Приостановлена: на орбите флот защитника"
                             : siege.Active ? $"Идёт: {siege.Progress:0} из {need:0} дн."
                             : "Осаждающие ушли — прогресс спадает";
                InspText($"<color=#8AA2A8>{state}</color>");
            }

            // Флоты на орбите
            var fleets = FleetsInOrbit(system.Id);
            if (fleets.Count > 0)
            {
                InspSection("НА ОРБИТЕ", LGIcon.Fleet);
                foreach (var kv in fleets)
                {
                    int owner = kv.Key;
                    string who = owner == 0 ? "Ваши корабли" : AIEmpireManager.NameOf(owner, "Чужие корабли");
                    string power = kv.Value.power > 0f ? $"  ·  мощь {kv.Value.power:N0}" : "";
                    string war = owner > 0 && Diplomacy.AtWar(0, owner) ? "  ·  <color=#FF6A6A>враг</color>" : "";
                    InspNote(LGIcon.Fleet, $"{who}: {kv.Value.count}{power}{war}", FleetIndicator.OwnerColor(owner));
                }
            }

            // Что делать
            InspSection("УПРАВЛЕНИЕ", LGIcon.Gear);
            if (!_isInSystemMode && system.OwnerId < 0)
                InspText(!system.IsSurveyed ? "Отправьте научный корабль, чтобы изучить систему."
                                            : "Заложите форпост строительным кораблём — система станет вашей.");
            else if (system.OwnerId == 0)
            {
                InspText("Система под вашим контролем.");
                var fm = FleetManager.Instance;
                if (fm != null) InspText($"<color=#8AA2A8>Ремонт кораблей здесь:</color> {fm.RepairRateAt(system.Id, 0) * 100f:0}% в день");
            }
            else
            {
                var ai = AIEmpireManager.For(system.OwnerId);
                InspText(ai != null && ai.AtWar
                    ? $"Чтобы захватить систему, держите здесь военный флот без защитников ({SiegeManager.RequiredDays(system):0} дн. осады)."
                    : "Чужая система. Захват возможен только во время войны — осадой.");
            }
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
