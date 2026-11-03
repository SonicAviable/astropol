using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;
using DS = StellarisClone.Rendering.UIManager.DS;

namespace StellarisClone.Station3D
{
    /// <summary>Пульт на 3D-станции: подойти, навести взгляд, [E] — открывается то же окно, что и на карте.</summary>
    public class StationTerminal : MonoBehaviour
    {
        public StationActions.Entry Entry;
        internal Image Glow;
        internal Transform Panel;
        private float _focus, _focusTarget;
        private Vector3 _panelBase;
        private float _phase;

        public void Init(StationActions.Entry e, Transform panel, Image glow)
        {
            Entry = e; Panel = panel; Glow = glow;
            _panelBase = panel != null ? panel.localPosition : Vector3.zero;
            _phase = UnityEngine.Random.value * 6.283f;
        }

        public void SetFocused(bool on) => _focusTarget = on ? 1f : 0f;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _focus = Mathf.MoveTowards(_focus, _focusTarget, dt * 6f);
            if (Panel != null)
            {
                float bob = Mathf.Sin(Time.unscaledTime * 1.3f + _phase) * 0.015f;
                Panel.localPosition = _panelBase + new Vector3(0f, bob + _focus * 0.04f, 0f);
                Panel.localScale = Vector3.one * (1f + _focus * 0.06f);
            }
            if (Glow != null)
            {
                var c = Entry.Color;
                c.a = Mathf.Lerp(0.16f, 0.42f, _focus);
                Glow.color = c;
            }
        }
    }

    /// <summary>Что умеют пульты станции — те же окна, что открываются кнопками меню на карте.</summary>
    public static class StationActions
    {
        public class Entry
        {
            public string Title;
            public string Key;          // клавиша этого окна на карте (подсказка)
            public LGIcon Icon;
            public Color Color;
            public Action Use;
            public Func<string> Blocked; // причина недоступности или null
        }

        private static UIManager UI => UIManager.Instance;

        private static StarSystem Capital
        {
            get
            {
                var s = EmpireStats.GetSystem(EconomyManager.PlayerCapitalId);
                return s != null && s.OwnerId == 0 ? s : null;
            }
        }

        private static PlanetData CapitalPlanet
        {
            get
            {
                var s = Capital;
                if (s == null) return null;
                PlanetData best = null;
                foreach (var p in s.Planets)
                    if (p != null && p.Population > 0 && (best == null || p.Population > best.Population)) best = p;
                return best;
            }
        }

        public static PlanetData HomePlanet => CapitalPlanet;

        private static string NoCapital() => Capital == null ? Loc.T("Столица потеряна") : null;
        private static string NoContacts() => Contacts.PlayerMetAnyEmpire ? null : Loc.T("Пока нет контактов с другими империями");

        private static void Nav(UIManager.NavWindow w) => UI?.OpenNavWindow(w);

        public static List<Entry> All()
        {
            var gold = DS.Gold;
            var cyan = DS.NeonCyan;
            return new List<Entry>
            {
                new Entry { Title = Loc.T("Обзор империи"), Key = "I", Icon = LGIcon.Globe, Color = cyan, Use = () => Nav(UIManager.NavWindow.Empire) },
                new Entry { Title = Loc.T("Колонии"), Key = "C", Icon = LGIcon.Population, Color = DS.Green, Use = () => Nav(UIManager.NavWindow.Colonies) },
                new Entry { Title = Loc.T("Столица"), Key = "", Icon = LGIcon.Planet, Color = DS.Green, Blocked = () => CapitalPlanet == null ? Loc.T("Столица потеряна") : null,
                            Use = () => PlanetOverviewModal.Instance?.Open(CapitalPlanet, Capital) },
                new Entry { Title = Loc.T("Строительство"), Key = "B", Icon = LGIcon.Construction, Color = new Color(0.95f, 0.62f, 0.36f),
                            Blocked = () => CapitalPlanet == null ? Loc.T("Столица потеряна") : null, Use = () => BuildMenuModal.Open(CapitalPlanet) },
                new Entry { Title = Loc.T("Технологии"), Key = "T", Icon = LGIcon.Research, Color = cyan, Use = () => Nav(UIManager.NavWindow.Tech) },
                new Entry { Title = Loc.T("Законы и решения"), Key = "L", Icon = LGIcon.Menu, Color = new Color(0.95f, 0.62f, 0.36f), Use = () => Nav(UIManager.NavWindow.Laws) },
                new Entry { Title = Loc.T("Дипломатия"), Key = "P", Icon = LGIcon.Diplomacy, Color = gold, Blocked = NoContacts, Use = () => Nav(UIManager.NavWindow.Diplomacy) },
                new Entry { Title = Loc.T("Торговля"), Key = "", Icon = LGIcon.Trade, Color = gold, Blocked = NoContacts, Use = () => TradeModal.Instance?.Open() },
                new Entry { Title = Loc.T("Шпионаж"), Key = "K", Icon = LGIcon.Betrayal, Color = new Color(0.74f, 0.56f, 1f), Use = () => Nav(UIManager.NavWindow.Espionage) },
                new Entry { Title = Loc.T("Звёздная база"), Key = "", Icon = LGIcon.Starbase, Color = cyan,
                            Blocked = () => NoCapital() ?? (CombatManager.Instance?.GetStarbase(EconomyManager.PlayerCapitalId) == null ? Loc.T("В столице нет звёздной базы") : null),
                            Use = () => UI?.OpenStarbaseWindowPublic(Capital) },
                new Entry { Title = Loc.T("Верфь"), Key = "", Icon = LGIcon.Shipyard, Color = cyan, Use = () => ShipyardWindow.Instance?.Open(EconomyManager.PlayerCapitalId) },
                new Entry { Title = Loc.T("Конструктор судов"), Key = "N", Icon = LGIcon.Gear, Color = cyan, Use = () => Nav(UIManager.NavWindow.Designer) },
                new Entry { Title = Loc.T("Арсенал"), Key = "J", Icon = LGIcon.Swords, Color = new Color(0.95f, 0.45f, 0.35f), Use = () => Nav(UIManager.NavWindow.Army) },
                new Entry { Title = Loc.T("Чудо фракции"), Key = "U", Icon = LGIcon.Wonder, Color = new Color(1f, 0.78f, 0.35f), Use = () => Nav(UIManager.NavWindow.Wonder) },
                new Entry { Title = Loc.T("Путь к победе"), Key = "V", Icon = LGIcon.Trophy, Color = gold, Use = () => Nav(UIManager.NavWindow.Victory) },
                new Entry { Title = Loc.T("Журнал событий"), Key = "O", Icon = LGIcon.Calendar, Color = gold, Use = NotificationLog.Toggle },
            };
        }
    }
}
