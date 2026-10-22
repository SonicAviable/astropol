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
            StarSpectralClass.BlackHole => Loc.T("Дыра"),
            _ => c.ToString().Replace("Class", "")
        };

        private static string StarClassHint(StarSpectralClass c) => c switch
        {
            StarSpectralClass.ClassM => Loc.T("красный карлик"),
            StarSpectralClass.ClassK => Loc.T("оранжевая звезда"),
            StarSpectralClass.ClassG => Loc.T("жёлтая звезда"),
            StarSpectralClass.ClassA => Loc.T("белая звезда"),
            StarSpectralClass.ClassB => Loc.T("голубой гигант"),
            _ => Loc.T("чёрная дыра")
        };

        private void BuildSystemCards(StarSystem system)
        {
            var cyan = DS.NeonCyan;
            InspChips(
                (LGIcon.Star, StarClassLabel(system.SpectralClass), StarClassHint(system.SpectralClass), DS.Gold),
                (LGIcon.Planet, system.Planets.Count.ToString(), Loc.T("орбитальных тел"), cyan),
                (LGIcon.MapLanes, system.ConnectedSystemIds.Count.ToString(), Loc.T("гиперкоридоров"), new Color(0.62f, 0.78f, 1f)));

            // Ресурсы
            InspSection(Loc.T("РЕСУРСЫ"), LGIcon.Minerals);
            if (system.IsSurveyed)
            {
                system.RecalculateHarvest();
                InspChips(
                    (LGIcon.Minerals, $"{system.HarvestedMinerals} / {system.TotalMinerals}", Loc.T("титан: добыча / всего"), new Color(0.30f, 0.94f, 0.55f)),
                    (LGIcon.Energy, $"{system.HarvestedEnergy} / {system.TotalEnergy}", Loc.T("гелий-3: добыча / всего"), new Color(1f, 0.80f, 0.32f)));
            }
            else InspNote(LGIcon.Sensors, Loc.T("Состав системы неизвестен — отправьте научный корабль"), new Color(1f, 0.67f, 0.53f));

            // Звёздная база
            var sb = CombatManager.Instance?.GetStarbase(system.Id);
            if (sb != null)
            {
                InspSection(Loc.T("ЗВЁЗДНАЯ БАЗА"), LGIcon.Starbase);
                if (sb.Disabled)
                    InspNote(LGIcon.Warning, Loc.F("Выведена из строя · ремонт {0:0}% из 50%", sb.Hull / Mathf.Max(1f, sb.MaxHull) * 100f), DS.Red);
                InspBar(LGIcon.Ship, Loc.T("Корпус"), sb.Hull, sb.MaxHull, new Color(0.75f, 0.85f, 0.9f));
                InspBar(LGIcon.ArmorPlate, Loc.T("Броня"), sb.Armor, sb.MaxArmor, new Color(0.88f, 0.78f, 0.62f));
                InspBar(LGIcon.ShieldDome, Loc.T("Щиты"), sb.Shields, sb.MaxShields, new Color(0.45f, 0.76f, 1f));
                float sbPower = CombatManager.Instance.StarbasePower(system.Id);
                string inShips = CombatManager.PowerInShips(sbPower);
                InspText(Loc.F("<color=#8AA2A8>Мощь базы</color> <b>{0:N0}</b>", sbPower) + (inShips.Length > 0 ? $"  ·  {inShips}" : "") +
                         Loc.F("\n<color=#8AA2A8>Залп</color> {0:0} урона  ·  <color=#8AA2A8>мощь считается как у кораблей (значок мечей в панели флота) — сравните с силой своего флота перед атакой</color>", sb.Damage));
                BuildFortification(system);
            }

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
                InspText($"<color=#8AA2A8>{state}</color>");
            }

            // Флоты на орбите
            var fleets = FleetsInOrbit(system.Id);
            if (fleets.Count > 0)
            {
                InspSection(Loc.T("НА ОРБИТЕ"), LGIcon.Fleet);
                foreach (var kv in fleets)
                {
                    int owner = kv.Key;
                    string who = owner == 0 ? Loc.T("Ваши корабли") : AIEmpireManager.NameOf(owner, Loc.T("Чужие корабли"));
                    string power = kv.Value.power > 0f ? Loc.F("  ·  мощь {0:N0}", kv.Value.power) : "";
                    string war = owner > 0 && Diplomacy.AtWar(0, owner) ? Loc.T("  ·  <color=#FF6A6A>враг</color>") : "";
                    InspNote(LGIcon.Fleet, $"{who}: {kv.Value.count}{power}{war}", FleetIndicator.OwnerColor(owner));
                }
            }

            // Что делать
            InspSection(Loc.T("УПРАВЛЕНИЕ"), LGIcon.Gear);
            if (!_isInSystemMode && system.OwnerId < 0)
                InspText(!system.IsSurveyed ? Loc.T("Отправьте научный корабль, чтобы изучить систему.")
                                            : Loc.T("Заложите форпост строительным кораблём — система станет вашей."));
            else if (system.OwnerId == 0)
            {
                InspText(Loc.T("Система под вашим контролем."));
                var fm = FleetManager.Instance;
                if (fm != null) InspText(Loc.F("<color=#8AA2A8>Ремонт кораблей здесь:</color> {0:0}% в день", fm.RepairRateAt(system.Id, 0) * 100f));
            }
            else
            {
                var ai = AIEmpireManager.For(system.OwnerId);
                InspText(ai != null && ai.AtWar
                    ? Loc.F("Чтобы захватить систему, держите здесь военный флот без защитников ({0:0} дн. осады).", SiegeManager.RequiredDays(system))
                    : Loc.T("Чужая система. Захват возможен только во время войны — осадой."));
            }
        }

        /// <summary>Укрепление базы: уровень, что он даёт, и кнопка следующего уровня (только для своих систем).</summary>
        private void BuildFortification(StarSystem system)
        {
            int lvl = system.Fortification;
            Color gold = DS.Gold;
            InspText(Loc.F("<color=#8AA2A8>Укрепление</color> <b>{0}</b>  ·  уровень {1} из {2}", Fortifications.Name(lvl), lvl, Fortifications.MaxLevel) +
                     (lvl > 0 ? Loc.F("\n<color=#8AA2A8>Прочность ×{0:0.##}, урон ×{1:0.##}, осада дольше ×{2:0.##}</color>",
                         Fortifications.DurabilityMult(lvl), Fortifications.DamageMult(lvl), Fortifications.SiegeMult(lvl)) : ""));
            if (system.OwnerId != 0) return;

            var job = Fortifications.PendingJob(system.Id);
            if (job != null)
            {
                InspNote(LGIcon.Clock, Loc.F("Строится: {0} · {1} дн.", Fortifications.Name(lvl + 1), Mathf.CeilToInt(job.DaysLeft)), gold);
                return;
            }
            if (lvl >= Fortifications.MaxLevel) return;

            int to = lvl + 1;
            float m = Fortifications.Minerals(to), a = Fortifications.Alloys(to), d = Fortifications.Days(to);
            string block = Fortifications.Blocker(system, 0);
            var eco = EconomyManager.Instance;
            if (block == null && (eco == null || !eco.CanAfford(0f, m, a, 0f))) block = CostCheck.Full(0f, m, a);

            var btn = LGBuild.Button(_inspStack, "Fortify", new Color(gold.r * 0.12f, gold.g * 0.12f, gold.b * 0.12f, 0.95f),
                new Color(gold.r, gold.g, gold.b, block == null ? 0.6f : 0.2f), () =>
                {
                    if (Fortifications.TryStartPlayer(system)) ShowSystemPanel(system);
                }, LGIcon.Starbase, Loc.F("УКРЕПИТЬ → {0}", Fortifications.Name(to).ToUpper()), 11);
            LGBuild.Height(btn.gameObject, 34);
            btn.interactable = block == null;
            TooltipHelper.Attach(btn.gameObject,
                Loc.F("<b>{0}</b>\nЦена: {1:0} титана, {2:0} сплавов · {3:0} дн.\n", Fortifications.Name(to), m, a, d) +
                Loc.F("Прочность базы ×{0:0.##}, урон ×{1:0.##}, осада дольше ×{2:0.##}\nСодержание +{3:0} гелия-3 в месяц\n",
                    Fortifications.DurabilityMult(to), Fortifications.DamageMult(to), Fortifications.SiegeMult(to), Fortifications.UpkeepPerLevel) +
                Loc.T("<color=#8AA2A8>Если систему захватят штурмом, один уровень укреплений будет разрушен</color>") +
                (block != null ? $"\n<color=#FF8888>{block}</color>" : ""));
            InspText(Loc.F("<color=#8AA2A8>{0:0} титана, {1:0} сплавов · {2:0} дн. · +{3:0} гелия-3/мес содержания</color>", m, a, d, Fortifications.UpkeepPerLevel) +
                     (block != null ? $"\n<color=#FF8888>{block}</color>" : ""));
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
