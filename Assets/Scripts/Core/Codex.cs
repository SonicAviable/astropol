using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using StellarisClone.Cards;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Кодекс — энциклопедия галактики: фракции, технологии, карты штурма, узлы силы и чудеса природы.
    /// Записи открываются по ходу игры (встреча с империей, изученная технология, карта в бою или в арсенале,
    /// найденный узел или чудо) и остаются открытыми навсегда — между партиями (PlayerPrefs).
    /// Неоткрытые записи видны как «???»: ясно, сколько ещё предстоит найти.
    /// </summary>
    public static class Codex
    {
        public enum Cat { Factions, Tech, Cards, Landmarks, Wonders }
        public static readonly Cat[] Cats = { Cat.Factions, Cat.Tech, Cat.Cards, Cat.Landmarks, Cat.Wonders };

        public sealed class Entry
        {
            public string Id;
            public Cat Cat;
            public string Title, Sub, Body;
            public LGIcon Icon;
            public Color Color;
            public int Order;
        }

        private const string FoundKey = "codex.found.v1", SeenKey = "codex.seen.v1";
        private static HashSet<string> s_found, s_seen;

        /// <summary>Открыта новая запись (или прочитана): обновить значки и окно.</summary>
        public static event Action OnChanged;

        public static string CatName(Cat c) => c switch
        {
            Cat.Factions => Loc.T("Фракции"),
            Cat.Tech => Loc.T("Технологии"),
            Cat.Cards => Loc.T("Карты штурма"),
            Cat.Landmarks => Loc.T("Узлы силы"),
            _ => Loc.T("Чудеса природы")
        };

        public static LGIcon CatIcon(Cat c) => c switch
        {
            Cat.Factions => LGIcon.Globe,
            Cat.Tech => LGIcon.Research,
            Cat.Cards => LGIcon.Swords,
            Cat.Landmarks => LGIcon.Star,
            _ => LGIcon.Sensors
        };

        // ==================== ОТКРЫТИЯ ====================

        private static void Load()
        {
            if (s_found != null) return;
            s_found = Split(PlayerPrefs.GetString(FoundKey, ""));
            s_seen = Split(PlayerPrefs.GetString(SeenKey, ""));
        }

        private static HashSet<string> Split(string s)
        {
            var h = new HashSet<string>();
            if (!string.IsNullOrEmpty(s)) foreach (var p in s.Split(';')) if (p.Length > 0) h.Add(p);
            return h;
        }

        private static void Save()
        {
            PlayerPrefs.SetString(FoundKey, string.Join(";", s_found));
            PlayerPrefs.SetString(SeenKey, string.Join(";", s_seen));
        }

        public static bool Known(string id) { Load(); return s_found.Contains(id); }
        public static bool IsNew(string id) { Load(); return s_found.Contains(id) && !s_seen.Contains(id); }

        public static int NewCount { get { Load(); int n = 0; foreach (var id in s_found) if (!s_seen.Contains(id)) n++; return n; } }

        public static void Discover(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            Load();
            if (!s_found.Add(id)) return;
            Save();
            OnChanged?.Invoke();
        }

        public static void MarkSeen(string id)
        {
            Load();
            if (!s_found.Contains(id) || !s_seen.Add(id)) return;
            Save();
            OnChanged?.Invoke();
        }

        /// <summary>Открыть всё, что игрок уже видел в этой партии (вызывается раз в неделю и при открытии окна).</summary>
        public static void ScanGame()
        {
            if (!UIManager.IsGameStarted) return;
            Load();
            int before = s_found.Count;
            bool quiet = true;
            void D(string id) { if (s_found.Add(id)) quiet = false; }

            string mine = SpeciesRegistry.FactionKeyOf(0);
            if (!string.IsNullOrEmpty(mine) && mine != "generic") D("faction." + mine);
            foreach (var ai in AIEmpireManager.All)
            {
                if (ai == null || !Contacts.PlayerMet(ai.OwnerId)) continue;
                var k = FactionRegistry.KeyOf(ai.Faction);
                if (!string.IsNullOrEmpty(k)) D("faction." + k);
            }

            var tm = TechnologyManager.Instance;
            if (tm != null) foreach (var t in tm.AllTechs) if (t.IsResearched) D("tech." + t.Id);

            var am = ArmyManager.Instance;
            if (am != null) foreach (var c in am.Of(0)) if (c != null && !string.IsNullOrEmpty(c.Def)) D("card." + c.Def);

            foreach (var s in Landmarks.All) if (s.Landmark >= 0 && (Vision.PlayerKnows(s.Id) || s.OwnerId == 0)) D("landmark." + s.Landmark);
            foreach (var s in NaturalWonders.All) if (s.Phenomenon >= 0 && (Vision.PlayerKnows(s.Id) || s.OwnerId == 0)) D("wonder." + s.Phenomenon);

            if (!quiet && s_found.Count != before) { Save(); OnChanged?.Invoke(); }
        }

        /// <summary>Карты, которые появились в бою (свои и вражеские).</summary>
        public static void DiscoverBattle(BattleSetup setup)
        {
            if (setup == null) return;
            Load();
            bool any = false;
            foreach (var c in setup.AttackerCards) if (c?.Def != null && s_found.Add("card." + c.Def.Id)) any = true;
            foreach (var c in setup.DefenderCards) if (c?.Def != null && s_found.Add("card." + c.Def.Id)) any = true;
            foreach (var h in new[] { setup.AttackerHero, setup.DefenderHero }) if (h?.Def != null && s_found.Add("card." + h.Def.Id)) any = true;
            if (any) { Save(); OnChanged?.Invoke(); }
        }

        // ==================== ЗАПИСИ ====================

        public static List<Entry> Entries(Cat cat)
        {
            var list = new List<Entry>();
            switch (cat)
            {
                case Cat.Factions:
                    int fi = 0;
                    foreach (var f in FactionRegistry.AvailableFactions)
                    {
                        var key = FactionRegistry.KeyOf(f);
                        if (string.IsNullOrEmpty(key)) continue;
                        var sb = new StringBuilder();
                        sb.Append(f.Description);
                        var founder = FindFounder(key);
                        if (founder != null)
                        {
                            sb.Append("\n\n<b>").Append(Loc.T("Народ-основатель")).Append(":</b> ").Append(founder.DisplayName);
                            foreach (var t in founder.Traits) sb.Append("\n• <b>").Append(TraitInfo.Name(t)).Append("</b> — ").Append(TraitInfo.Desc(t));
                        }
                        string doc = BattleState.DoctrineName(key);
                        if (doc != null) sb.Append("\n\n<b>").Append(Loc.T("Доктрина штурма")).Append(": ").Append(doc).Append("</b>\n").Append(BattleState.DoctrineText(key));
                        list.Add(new Entry { Id = "faction." + key, Cat = cat, Title = f.Name, Sub = f.Title, Body = sb.ToString(), Icon = LGIcon.Globe, Color = f.EmpireColor, Order = fi++ });
                    }
                    break;

                case Cat.Tech:
                {
                    var techs = TechnologyManager.Instance != null ? new List<Technology>(TechnologyManager.Instance.AllTechs) : TechnologyManager.BuildTechTree();
                    techs.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : string.CompareOrdinal(a.Name, b.Name));
                    int ti = 0;
                    foreach (var t in techs)
                    {
                        string flavor = TechFlavorDatabase.Has(t.Id) ? TechFlavorDatabase.Get(t.Id) : null;
                        string body = (string.IsNullOrEmpty(flavor) ? "" : "<i>" + flavor + "</i>\n\n") + t.Description;
                        LGIcon icon = Enum.TryParse(t.Category.ToString(), out LGIcon ic) ? ic : LGIcon.Research;
                        list.Add(new Entry { Id = "tech." + t.Id, Cat = cat, Title = t.Name, Sub = TechCategoryInfo.Name(t.Category) + "  ·  " + (t.Tier == 0 ? Loc.T("базовая") : Loc.F("уровень {0}", t.Tier)), Body = body, Icon = icon,
                                             Color = UIManager.DS.NeonCyan, Order = ti++ });
                    }
                    break;
                }

                case Cat.Cards:
                {
                    int ci = 0;
                    foreach (var d in CardCatalog.All)
                    {
                        if (d.Virtual && d.Kind != CardKind.Hero) continue;
                        string fac = d.Faction != null ? FactionRegistry.ByKey(d.Faction)?.Name : Loc.T("Общая карта");
                        var sb = new StringBuilder();
                        sb.Append("<b>").Append(d.KindName).Append("</b>");
                        if (d.Kind != CardKind.Order && d.Kind != CardKind.Trap) sb.Append(Loc.F("  ·  сила {0}  ·  броня {1}", d.Power, d.Armor));
                        sb.Append(Loc.F("  ·  стоит {0} очк.", d.Cost)).Append("\n\n").Append(d.Text);
                        var tech = CardCatalog.TechOf(d);
                        if (tech != null)
                        {
                            string tn = tech;
                            if (TechnologyManager.Instance != null) foreach (var t in TechnologyManager.Instance.AllTechs) if (t.Id == tech) { tn = t.Name; break; }
                            sb.Append("\n\n<color=#8AA2A8>").Append(Loc.F("Открывает технология «{0}»", tn)).Append("</color>");
                        }
                        LGIcon icon = d.Kind == CardKind.Order ? LGIcon.Target : d.Kind == CardKind.Structure ? LGIcon.Starbase : d.Kind == CardKind.Trap ? LGIcon.Warning
                                    : d.Kind == CardKind.Hero ? LGIcon.Leader : d.Has(Kw.Vehicle) ? LGIcon.ArmorPlate : LGIcon.Swords;
                        list.Add(new Entry { Id = "card." + d.Id, Cat = cat, Title = d.Name, Sub = fac, Body = sb.ToString(), Icon = icon,
                                             Color = d.Faction != null ? (FactionRegistry.ByKey(d.Faction)?.EmpireColor ?? UIManager.DS.Gold) : UIManager.DS.Gold, Order = ci++ });
                    }
                    break;
                }

                case Cat.Landmarks:
                    for (int i = 0; i < Landmarks.TypeCount; i++)
                        list.Add(new Entry { Id = "landmark." + i, Cat = cat, Title = Landmarks.Name(i), Sub = Loc.T("Узел силы"),
                                             Body = "<i>" + Landmarks.Lore(i) + "</i>\n\n<b>" + Loc.T("Бонус владельцу") + ":</b> " + Landmarks.Effect(i) +
                                                    "\n\n<color=#8AA2A8>" + Loc.T("Действует, пока в системе стоит ваш форпост. Технология «Археотехнология» усиливает бонус на 50%.") + "</color>",
                                             Icon = SystemNameplate.LandmarkIcon(i), Color = Landmarks.Color(i), Order = i });
                    break;

                default:
                    for (int i = 0; i < NaturalWonders.TypeCount; i++)
                        list.Add(new Entry { Id = "wonder." + i, Cat = cat, Title = NaturalWonders.Name(i), Sub = Loc.T("Чудо природы"),
                                             Body = "<i>" + NaturalWonders.Lore(i) + "</i>\n\n<b>" + Loc.T("Закон системы") + ":</b> " + NaturalWonders.Rule(i) +
                                                    "\n\n<color=#8AA2A8>" + Loc.T("Действует на всех, кто находится в системе, — и на вас, и на врага.") + "</color>",
                                             Icon = NaturalWonders.Icon(i), Color = NaturalWonders.Color(i), Order = i });
                    break;
            }
            return list;
        }

        public static (int found, int total) Progress(Cat cat)
        {
            int f = 0, n = 0;
            foreach (var e in Entries(cat)) { n++; if (Known(e.Id)) f++; }
            return (f, n);
        }

        private static Species FindFounder(string key)
        {
            foreach (var s in SpeciesRegistry.All) if (s.Kind == SpeciesKind.Founder && s.Faction == key) return s;
            return null;
        }
    }
}
