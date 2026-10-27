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
            t.Track(1.6f);
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
            InspStatLine(
                (LGIcon.Star, StarClassLabel(system.SpectralClass), StarClassHint(system.SpectralClass), StarTint(system.SpectralClass)),
                (LGIcon.Planet, system.Planets.Count.ToString(), Loc.T("орбитальных тел"), DS.NeonCyan),
                (LGIcon.MapLanes, system.ConnectedSystemIds.Count.ToString(), Loc.T("гиперкоридоров"), new Color(0.62f, 0.78f, 1f)));

            // Ресурсы
            InspSection(Loc.T("РЕСУРСЫ"), LGIcon.Minerals);
            if (system.IsSurveyed)
            {
                system.RecalculateHarvest();
                InspMeter(LGIcon.Minerals, Loc.T("Титан"), Loc.T("добыча / залежи"), system.HarvestedMinerals, system.TotalMinerals, new Color(0.30f, 0.94f, 0.55f));
                InspMeter(LGIcon.Energy, Loc.T("Гелий-3"), Loc.T("добыча / залежи"), system.HarvestedEnergy, system.TotalEnergy, new Color(1f, 0.80f, 0.32f));
            }
            else InspNote(LGIcon.Sensors, Loc.T("Состав системы неизвестен — отправьте научный корабль"), new Color(1f, 0.67f, 0.53f));

            // Звёздная база
            var sb = CombatManager.Instance?.GetStarbase(system.Id);
            if (sb != null)
            {
                InspSection(Loc.T("ЗВЁЗДНАЯ БАЗА"), LGIcon.Starbase);
                InspStarbaseCard(system, sb);
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

                // Наземный штурм колонии: десант из резерва империи, сколько поместится на корабли на орбите
                if (mine && StellarisClone.Cards.AssaultLink.IsColony(system) && StellarisClone.Cards.ArmyManager.Instance != null)
                {
                    int deck = StellarisClone.Cards.ArmyManager.Instance.AttackDeck(0, system.Id).Count;
                    int places = StellarisClone.Cards.ArmyManager.LandingPlaces(0, system.Id);
                    string blocker = StellarisClone.Cards.AssaultLink.Blocker(0, system);
                    InspNote(LGIcon.Weapons, deck > 0
                        ? Loc.F("Десант: до {0} карт из резерва империи (мест на кораблях: {1}). Карты выбираются перед штурмом", deck, places)
                        : Loc.T("Резерв армии пуст — в бой пойдут только экипажи. Состав армии — в «Арсенале» (J)"), deck > 0 ? DS.Gold : DS.TextMuted);
                    string tip = Loc.F("<b>Наземный штурм</b>\nКарточный бой за колонию: 3 сектора, {0} ходов. Победа — колония ваша сразу, не дожидаясь конца осады. Погибшие карты теряются навсегда, выжившие получают опыт.\n<color=#8AA2A8>Сначала откроется «Сбор десанта» — выберите, какими картами нападать</color>", StellarisClone.Cards.BattleState.Turns);
                    InspButton(Loc.T("ШТУРМ"), LGIcon.Target, new Color(0.55f, 0.16f, 0.12f, 0.95f), blocker, tip,
                        () => StellarisClone.Cards.AssaultDeckWindow.Open(system), system);
                    InspButton(Loc.T("АВТОБОЙ"), LGIcon.Fast, DS.BtnNeutral, blocker, Loc.T("Быстрый штурм без экрана боя и без выбора карт: в десант пойдут лучшие карты резерва, исход рассчитают правила"),
                        () => StellarisClone.Cards.AssaultLink.StartPlayerAssault(system, true), system);
                }
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

            // Вольный мир — свои карточки вместо управления
            var neutral = NeutralWorlds.At(system.Id);
            if (neutral != null && system.OwnerId == NeutralWorlds.Owner)
            {
                BuildNeutralCards(system, neutral);
                return;
            }

            // Что делать
            InspSection(Loc.T("УПРАВЛЕНИЕ"), LGIcon.Gear);
            if (!_isInSystemMode && system.OwnerId == -1)
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
