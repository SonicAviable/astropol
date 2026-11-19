using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Тело панели системы («Атлас», вариант «Схема»): таблица фактов 2×2 под схемой, строки меню действий
    /// (значок, название, значение, шеврон), раскрываемое снабжение.
    /// </summary>
    public partial class UIManager
    {
        private string _inspOwnerLine;
        private bool _inspSupplyOpen;

        private static readonly Color AtlasCream = new Color(0.925f, 0.898f, 0.827f);
        private static readonly Color AtlasMuted = new Color(0.663f, 0.639f, 0.573f);
        private static readonly Color AtlasSea = new Color(0.475f, 0.816f, 0.749f);
        private static readonly Color AtlasCoral = new Color(1f, 0.42f, 0.341f);

        private static Image Hairline(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 size, float alpha)
        {
            var l = LGBuild.Panel(parent, "Hair", new Color(AtlasCream.r, AtlasCream.g, AtlasCream.b, alpha));
            l.raycastTarget = false;
            l.rectTransform.anchorMin = aMin;
            l.rectTransform.anchorMax = aMax;
            l.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            l.rectTransform.sizeDelta = size;
            l.rectTransform.anchoredPosition = Vector2.zero;
            LG.Ignore(l.gameObject);
            return l;
        }

        /// <summary>Владелец системы для таблицы фактов: имя и цвет.</summary>
        private (string name, Color col) OwnerOf(StarSystem s)
        {
            if (s.OwnerId == 0) return (_selectedFaction?.Name ?? Loc.T("Ваша империя"), AtlasSea);
            if (s.OwnerId > 0) return (AIEmpireManager.NameOf(s.OwnerId, Loc.T("Соперник")), Color.Lerp(FleetIndicator.OwnerColor(s.OwnerId), AtlasCream, 0.25f));
            var nw = NeutralWorlds.At(s.Id);
            if (s.OwnerId == NeutralWorlds.Owner && nw != null) return (NeutralWorlds.CharacterName(nw.Kind), Color.Lerp(NeutralWorlds.ColorOf(nw.Kind), AtlasCream, 0.25f));
            return (Loc.T("Нет · фронтир"), AtlasMuted);
        }

        /// <summary>Таблица фактов: владелец, звезда, тела, коридоры — четыре ячейки с тонкими разделителями.</summary>
        private void InspFactsGrid(StarSystem s)
        {
            var grid = LGBuild.Rect(_inspStack, "Facts");
            LGBuild.Height(grid.gameObject, 96);
            var owner = OwnerOf(s);
            var cells = new (string cap, string val, Color col, bool mono, string tip)[]
            {
                (Loc.T("Владелец"), owner.name, owner.col, false, _inspOwnerLine),
                (Loc.T("Звезда"), StarClassLabel(s.SpectralClass), AtlasCream, false, StarClassHint(s.SpectralClass)),
                (Loc.T("Орбитальные тела"), s.IsSurveyed ? s.Planets.Count.ToString() : "?", AtlasCream, true, null),
                (Loc.T("Гиперкоридоры"), s.ConnectedSystemIds.Count.ToString(), AtlasCream, true, null),
            };
            for (int i = 0; i < 4; i++)
            {
                int cx = i % 2, cy = i / 2;
                var cell = LGBuild.Rect(grid, "Cell");
                cell.anchorMin = new Vector2(cx * 0.5f, 1f - (cy + 1) * 0.5f);
                cell.anchorMax = new Vector2((cx + 1) * 0.5f, 1f - cy * 0.5f);
                cell.offsetMin = cell.offsetMax = Vector2.zero;
                var cap = LGBuild.Label(cell, cells[i].cap, 11, AtlasMuted, TextAnchor.UpperLeft);
                cap.rectTransform.Stretch(cx == 0 ? 4 : 16, 0, 6, 9);
                var v = LGBuild.Label(cell, cells[i].val, cells[i].mono ? 16 : 13, cells[i].col, TextAnchor.LowerLeft);
                if (cells[i].mono) v.font = GameFont.MonoMedium;
                v.horizontalOverflow = HorizontalWrapMode.Wrap;
                v.resizeTextForBestFit = true; v.resizeTextMinSize = 10; v.resizeTextMaxSize = v.fontSize;
                v.rectTransform.Stretch(cx == 0 ? 4 : 16, 10, 6, 24);
                if (!string.IsNullOrEmpty(cells[i].tip))
                {
                    var hit = LGBuild.Panel(cell, "Tip", new Color(0f, 0f, 0f, 0f), raycast: true);
                    hit.rectTransform.Stretch();
                    LG.Ignore(hit.gameObject);
                    TooltipHelper.Attach(hit.gameObject, cells[i].tip);
                }
            }
            Hairline(grid, new Vector2(0.5f, 0.12f), new Vector2(0.5f, 0.88f), new Vector2(1f, 0f), 0.08f);
            Hairline(grid, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 1f), 0.08f);
            var bottom = Hairline(grid, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), 0.16f);
            bottom.rectTransform.pivot = new Vector2(0.5f, 0f);
        }

        /// <summary>
        /// Строка меню: значок, название, значение справа и шеврон. Наведение — подпись светлеет, значок золотеет,
        /// строка чуть сдвигается. Без действия — просто строка сведений, без шеврона.
        /// </summary>
        private RectTransform InspMenuRow(LGIcon icon, string label, string value, Color valueCol, System.Action onClick, string tip = null,
                                          bool open = false, bool first = false)
        {
            var row = LGBuild.Panel(_inspStack, "MenuRow", new Color(0f, 0f, 0f, 0f), raycast: onClick != null || tip != null);
            LGBuild.Height(row.gameObject, 42);
            LG.Ignore(row.gameObject);
            var inner = LGBuild.Rect(row.transform, "Inner");
            inner.Stretch();

            var ic = LGIcons.Create(inner, icon, 16, open ? DS.Gold : AtlasMuted);
            ic.raycastTarget = false;
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(12, 0), new Vector2(16, 16));
            var l = LGBuild.Label(inner, label, 13, AtlasCream, TextAnchor.MiddleLeft);
            l.rectTransform.Stretch(32, 0, 120, 0);
            l.horizontalOverflow = HorizontalWrapMode.Wrap;
            l.resizeTextForBestFit = true; l.resizeTextMinSize = 10; l.resizeTextMaxSize = 13;
            var v = LGBuild.Label(inner, value ?? "", 11, valueCol, TextAnchor.MiddleRight);
            v.font = GameFont.Mono;
            v.rectTransform.anchorMin = new Vector2(0.45f, 0); v.rectTransform.anchorMax = new Vector2(1, 1);
            v.rectTransform.offsetMin = Vector2.zero; v.rectTransform.offsetMax = new Vector2(onClick != null ? -24 : -4, 0);

            Image chev = null;
            if (onClick != null)
            {
                chev = LGIcons.Create(inner, LGIcon.Back, 10, AtlasMuted);
                chev.raycastTarget = false;
                chev.rectTransform.At(new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-9, 0), new Vector2(10, 10));
                chev.rectTransform.localRotation = Quaternion.Euler(0, 0, open ? 90f : 180f);
                var btn = row.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => { SFXManager.Play(StellarisClone.Core.Audio.Sfx.UiClick, 0.7f); onClick(); });
                var hv = row.gameObject.AddComponent<InspMenuHover>();
                hv.Init(inner, l, ic, chev, open);
            }
            if (first) { var top = Hairline(row.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), 0.08f); top.rectTransform.pivot = new Vector2(0.5f, 1); }
            var bl = Hairline(row.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), 0.08f);
            bl.rectTransform.pivot = new Vector2(0.5f, 0);
            if (!string.IsNullOrEmpty(tip)) TooltipHelper.Attach(row.gameObject, tip);
            return row.rectTransform;
        }

        /// <summary>Короткое состояние снабжения для строки меню.</summary>
        private static (string text, Color col) SupplyShort(StarSystem s)
        {
            var f = SupplyNetwork.Get(0)?.Of(s.Id);
            if (f == null) return ("—", AtlasMuted);
            switch (f.State)
            {
                case SupplyNetwork.NodeState.Hub: return (Loc.T("хаб"), DS.Gold);
                case SupplyNetwork.NodeState.Blockaded: return (Loc.T("блокада"), AtlasCoral);
                case SupplyNetwork.NodeState.CutOff: return (Loc.T("отрезана"), AtlasCoral);
                default:
                    return f.DeliveredShare >= 0.99f ? (Loc.T("в сети"), AtlasSea) : (Loc.F("{0:0}% доходит", f.DeliveredShare * 100f), DS.Gold);
            }
        }

        /// <summary>Меню действий системы: звёздная база, флоты на орбите, снабжение (раскрывается), обзор системы.</summary>
        private void InspSystemMenu(StarSystem system)
        {
            bool first = true;
            var cm = CombatManager.Instance;
            var sb = cm?.GetStarbase(system.Id);
            if (sb != null)
            {
                bool open = StarbaseWindowOpen && _sbSystemId == system.Id;
                float power = cm.StarbasePower(system.Id);
                InspMenuRow(LGIcon.Starbase, Loc.F("Звёздная база «{0}»", Fortifications.Name(system.Fortification)),
                    sb.Disabled ? Loc.T("выведена из строя") : Loc.F("мощь {0:N0}", power), sb.Disabled ? AtlasCoral : DS.Gold,
                    () => ToggleStarbaseWindow(system),
                    open ? Loc.T("Скрыть окно звёздной базы") : Loc.T("Открыть звёздную базу: прочность, укрепления и их стройка"), open, first);
                first = false;
            }

            var fleets = FleetsInOrbit(system.Id);
            if (fleets.Count > 0)
            {
                int total = 0;
                var tip = new System.Text.StringBuilder(Loc.T("<b>Флоты на орбите</b>"));
                foreach (var kv in fleets)
                {
                    total += kv.Value.count;
                    string who = kv.Key == 0 ? Loc.T("Ваши") : AIEmpireManager.NameOf(kv.Key, Loc.T("Чужие"));
                    tip.Append('\n').Append(Loc.F("{0}: {1} · мощь {2:N0}", who, kv.Value.count, kv.Value.power));
                }
                FleetView mine = null;
                var fm = FleetManager.Instance;
                if (fm != null)
                    foreach (var f in fm.AllFleets)
                        if (f != null && f.Data != null && !f.Data.Destroyed && f.Data.OwnerId == 0
                            && f.Data.CurrentSystemId == system.Id && f.Data.State != FleetState.InHyperlane) { mine = f; break; }
                bool hostile = false;
                foreach (var kv in fleets) if (kv.Key != 0 && kv.Key != system.OwnerId) hostile = true;
                InspMenuRow(LGIcon.Fleet, Loc.T("Флоты на орбите"), total.ToString(), hostile ? AtlasCoral : AtlasMuted,
                    mine != null ? () => FleetManager.Instance?.SelectFleet(mine) : (System.Action)null,
                    mine != null ? tip.Append(Loc.T("\n<color=#A9A392>Клик — выбрать свой флот</color>")).ToString() : tip.ToString(), false, first);
                first = false;
            }

            if (system.OwnerId == 0 && SupplyNetwork.Get(0)?.Of(system.Id) != null)
            {
                var st = SupplyShort(system);
                InspMenuRow(LGIcon.Trade, Loc.T("Снабжение"), st.text, st.col,
                    () => { _inspSupplyOpen = !_inspSupplyOpen; ShowSystemPanel(system); },
                    _inspSupplyOpen ? Loc.T("Свернуть снабжение") : Loc.T("Склад, груз и связь с хабом"), _inspSupplyOpen, first);
                first = false;
                if (_inspSupplyOpen) BuildSupplyCards(system);
            }

            if (!_isInSystemMode && system.IsSurveyed && SystemViewManager.Instance != null)
            {
                InspMenuRow(LGIcon.Globe, Loc.T("Обзор системы"), system.Planets.Count > 0 ? Loc.F("{0} тел", system.Planets.Count) : "", AtlasMuted,
                    () => SystemViewManager.Instance?.EnterSystemView(system), Loc.T("Открыть систему: планеты, колонии и станции на орбитах"), false, first);
            }
        }
    }

}
