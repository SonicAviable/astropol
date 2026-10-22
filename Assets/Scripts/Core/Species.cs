using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>Черты вида. Положительные и отрицательные; «Механические» — только у роботов, «Синтетические» — у синтетов «Ноля».</summary>
    public enum SpeciesTrait
    {
        Industrious,    // Трудолюбивые: +15% титана и гелия-3
        Intelligent,    // Умные: +20% науки
        Resilient,      // Живучие: война, осада и завоевание бьют по стабильности вдвое слабее; +5 на суровых мирах
        RapidBreeders,  // Плодовитые: рост +25%
        Engineers,      // Инженеры: +15% сплавов
        Agreeable,      // Уживчивые: +6 стабильности
        Adaptive,       // Приспособленные: чужой климат не раздражает
        Slow,           // Медлительные: −10% ко всему производству
        SlowBreeders,   // Медленно размножаются: рост −25%
        Quarrelsome,    // Сварливые: −8 стабильности
        Mechanical,     // Механические: не растут сами, не ведут науку; +10% добычи, вне политики
        Synthetic,      // Синтетические (Консенсус «Ноль»): растут сборкой, ведут науку, вне политики, климат безразличен
    }

    public enum SpeciesKind { Founder, Robot, Hybrid, Adapted }

    public static class TraitInfo
    {
        public static readonly SpeciesTrait[] Negative = { SpeciesTrait.Slow, SpeciesTrait.SlowBreeders, SpeciesTrait.Quarrelsome };

        public static bool IsNegative(SpeciesTrait t) => t == SpeciesTrait.Slow || t == SpeciesTrait.SlowBreeders || t == SpeciesTrait.Quarrelsome;

        public static string Name(SpeciesTrait t) => t switch
        {
            SpeciesTrait.Industrious   => Loc.T("Трудолюбивые"),
            SpeciesTrait.Intelligent   => Loc.T("Умные"),
            SpeciesTrait.Resilient     => Loc.T("Живучие"),
            SpeciesTrait.RapidBreeders => Loc.T("Плодовитые"),
            SpeciesTrait.Engineers     => Loc.T("Инженеры"),
            SpeciesTrait.Agreeable     => Loc.T("Уживчивые"),
            SpeciesTrait.Adaptive      => Loc.T("Приспособленные"),
            SpeciesTrait.Slow          => Loc.T("Медлительные"),
            SpeciesTrait.SlowBreeders  => Loc.T("Медленно размножаются"),
            SpeciesTrait.Quarrelsome   => Loc.T("Сварливые"),
            SpeciesTrait.Mechanical    => Loc.T("Механические"),
            SpeciesTrait.Synthetic     => Loc.T("Синтетические"),
            _ => "?"
        };

        public static string Desc(SpeciesTrait t) => t switch
        {
            SpeciesTrait.Industrious   => Loc.T("+15% титана и гелия-3"),
            SpeciesTrait.Intelligent   => Loc.T("+20% науки"),
            SpeciesTrait.Resilient     => Loc.T("война, осада и завоевание снижают стабильность вдвое слабее; +5 стабильности на суровых мирах"),
            SpeciesTrait.RapidBreeders => Loc.T("рост населения +25%"),
            SpeciesTrait.Engineers     => Loc.T("+15% сплавов"),
            SpeciesTrait.Agreeable     => Loc.T("+6 стабильности"),
            SpeciesTrait.Adaptive      => Loc.T("чужой климат не снижает стабильность"),
            SpeciesTrait.Slow          => Loc.T("−10% ко всему производству"),
            SpeciesTrait.SlowBreeders  => Loc.T("рост населения −25%"),
            SpeciesTrait.Quarrelsome   => Loc.T("−8 стабильности"),
            SpeciesTrait.Mechanical    => Loc.T("не размножаются и не ведут науку; +10% титана и гелия-3; не бунтуют"),
            SpeciesTrait.Synthetic     => Loc.T("не рождаются, а собираются на фабриках; ведут науку; не бунтуют; любой климат — родной"),
            _ => ""
        };
    }

    /// <summary>Вид разумных существ: основатели фракций, роботы, гибриды и генетически адаптированные линии.</summary>
    [Serializable]
    public class Species
    {
        public int Id;
        public SpeciesKind Kind;
        /// <summary>Ключ фракции основателя (xarn, astrea…; generic — поселенцы без фракции).</summary>
        public string Faction;
        /// <summary>Русское название (ключ перевода) — у основателей и роботов.</summary>
        public string Name;
        public int ParentA = -1, ParentB = -1;
        /// <summary>Родной климат: на мирах другого типа вид чувствует себя чужим (кроме «Приспособленных»).</summary>
        public PlanetType Home;
        public List<SpeciesTrait> Traits = new List<SpeciesTrait>();
        public Color Color = Color.white;

        public bool Robot => Kind == SpeciesKind.Robot;
        /// <summary>Машина (робот или синтет): вне политики, не скрещивается и не меняет гены.</summary>
        public bool Machine => Robot || Has(SpeciesTrait.Synthetic);
        public bool Has(SpeciesTrait t) => Traits.Contains(t);

        public string DisplayName
        {
            get
            {
                switch (Kind)
                {
                    case SpeciesKind.Hybrid:
                    {
                        var a = SpeciesRegistry.Root(ParentA);
                        var b = SpeciesRegistry.Root(ParentB);
                        return Loc.F("{0}-{1}", SpeciesRegistry.HybridPrefix(a), SpeciesRegistry.HybridTail(b));
                    }
                    case SpeciesKind.Adapted:
                    {
                        var p = SpeciesRegistry.Get(ParentA);
                        return Loc.F("{0} ({1})", p != null ? p.DisplayName : "?", SpeciesRegistry.LineName(Home));
                    }
                    default:
                        return Loc.T(Name ?? "?");
                }
            }
        }

        /// <summary>Название внутри фразы: по-русски со строчной буквы, по-английски как есть.</summary>
        public string InText => Loc.English ? DisplayName : DisplayName.ToLower();

        public string TraitsLine
        {
            get
            {
                var parts = new List<string>();
                foreach (var t in Traits)
                    parts.Add(TraitInfo.IsNegative(t) ? $"<color=#FF9A8A>{TraitInfo.Name(t)}</color>" : $"<color=#9EF0C8>{TraitInfo.Name(t)}</color>");
                return string.Join(", ", parts);
            }
        }

        // ---- Множители черт ----

        public float OutputMult(string resource)
        {
            float m = 1f;
            if (Has(SpeciesTrait.Industrious) && (resource == "minerals" || resource == "energy")) m += 0.15f;
            if (Has(SpeciesTrait.Mechanical) && (resource == "minerals" || resource == "energy")) m += 0.10f;
            if (Has(SpeciesTrait.Engineers) && resource == "alloys") m += 0.15f;
            if (Has(SpeciesTrait.Slow)) m -= 0.10f;
            return m;
        }

        public float ResearchMult
        {
            get
            {
                if (Has(SpeciesTrait.Mechanical)) return 0f;
                float m = 1f;
                if (Has(SpeciesTrait.Intelligent)) m += 0.20f;
                if (Has(SpeciesTrait.Slow)) m -= 0.10f;
                return m;
            }
        }

        public float GrowthMult
        {
            get
            {
                if (Has(SpeciesTrait.Mechanical)) return 0f;
                float m = 1f;
                if (Has(SpeciesTrait.RapidBreeders)) m += 0.25f;
                if (Has(SpeciesTrait.SlowBreeders)) m -= 0.25f;
                return m;
            }
        }

        /// <summary>Чувствует ли себя вид дома на планете этого типа.</summary>
        public bool AtHome(PlanetType t) => Machine || Has(SpeciesTrait.Adaptive) || Home == t;
    }

    /// <summary>Группа жителей одного вида на планете.</summary>
    [Serializable]
    public class PopGroup
    {
        public int Species;
        public int Count;
        public PopGroup() { }
        public PopGroup(int species, int count) { Species = species; Count = count; }
    }

    /// <summary>
    /// Все виды галактики. Основатели создаются по требованию (по фракции владельца),
    /// гибриды и адаптированные линии — в ходе игры; список сохраняется вместе с партией.
    /// </summary>
    public static class SpeciesRegistry
    {
        private static readonly List<Species> _all = new List<Species>();
        public static IReadOnlyList<Species> All => _all;

        /// <summary>Сколько разных гибридов может появиться за партию (чтобы галактика не утонула в видах).</summary>
        public const int MaxHybrids = 12;

        public static void Reset() => _all.Clear();

        public static List<Species> Capture() => new List<Species>(_all);

        public static void Restore(List<Species> list)
        {
            _all.Clear();
            if (list == null) return;
            foreach (var s in list) if (s != null) _all.Add(s);
            for (int i = 0; i < _all.Count; i++) _all[i].Id = i;
        }

        public static Species Get(int id) => id >= 0 && id < _all.Count ? _all[id] : null;

        /// <summary>Основатель, от которого пошла линия (у гибрида — его первый родитель).</summary>
        public static Species Root(int id)
        {
            var s = Get(id);
            for (int guard = 0; s != null && s.Kind != SpeciesKind.Founder && s.Kind != SpeciesKind.Robot && guard < 16; guard++)
                s = Get(s.ParentA);
            return s;
        }

        /// <summary>Вид связан с основателем (сам основатель, его адаптированная линия или гибрид с ним).</summary>
        public static bool Related(int id, int founderId)
        {
            if (id == founderId) return true;
            var s = Get(id);
            for (int guard = 0; s != null && guard < 16; guard++)
            {
                if (s.Id == founderId) return true;
                if (s.Kind == SpeciesKind.Hybrid && (Related(s.ParentA, founderId) || Related(s.ParentB, founderId))) return true;
                if (s.Kind != SpeciesKind.Adapted) return false;
                s = Get(s.ParentA);
            }
            return false;
        }

        private static Species Add(Species s)
        {
            s.Id = _all.Count;
            _all.Add(s);
            return s;
        }

        // ==================== ОСНОВАТЕЛИ ====================

        public static string FactionKeyOf(int owner)
        {
            if (owner < 0) return "generic";
            return FactionRegistry.KeyOf(FactionTraits.FactionOf(owner)) ?? "generic";
        }

        /// <summary>Вид-основатель империи владельца (по фракции).</summary>
        public static int FounderOf(int owner) => Founder(FactionKeyOf(owner)).Id;

        public static Species Founder(string key)
        {
            key ??= "generic";
            foreach (var s in _all) if (s.Kind == SpeciesKind.Founder && s.Faction == key) return s;
            var n = new Species { Kind = SpeciesKind.Founder, Faction = key };
            switch (key)
            {
                case "xarn":
                    n.Name = "Ксарнийцы"; n.Home = PlanetType.Desert; n.Color = new Color(0.92f, 0.42f, 0.30f);
                    n.Traits.AddRange(new[] { SpeciesTrait.Resilient, SpeciesTrait.Industrious, SpeciesTrait.Quarrelsome });
                    break;
                case "astrea":
                    n.Name = "Астрейцы"; n.Home = PlanetType.Continental; n.Color = new Color(0.40f, 0.70f, 1.00f);
                    n.Traits.AddRange(new[] { SpeciesTrait.Intelligent, SpeciesTrait.Agreeable });
                    break;
                case "aquila":
                    // Родной мир — пустыня: бесплодные миры не колонизируют, и климат «чужой» был бы на любой колонии
                    n.Name = "Аквилианцы"; n.Home = PlanetType.Desert; n.Color = new Color(0.95f, 0.78f, 0.36f);
                    n.Traits.AddRange(new[] { SpeciesTrait.Industrious, SpeciesTrait.Engineers, SpeciesTrait.SlowBreeders });
                    break;
                case "iridia":
                    n.Name = "Иридийцы"; n.Home = PlanetType.Continental; n.Color = new Color(0.78f, 0.52f, 1.00f);
                    n.Traits.AddRange(new[] { SpeciesTrait.Intelligent, SpeciesTrait.Adaptive, SpeciesTrait.Slow });
                    break;
                case "terraan":
                    n.Name = "Тэрра'ан"; n.Home = PlanetType.Continental; n.Color = new Color(0.46f, 0.90f, 0.46f);
                    n.Traits.AddRange(new[] { SpeciesTrait.Resilient, SpeciesTrait.Adaptive, SpeciesTrait.SlowBreeders });
                    break;
                case "nol":
                    n.Name = "Синтеты"; n.Home = PlanetType.Barren; n.Color = new Color(0.82f, 0.90f, 0.96f);
                    n.Traits.AddRange(new[] { SpeciesTrait.Synthetic, SpeciesTrait.Engineers });
                    break;
                default:
                    n.Name = "Поселенцы"; n.Home = PlanetType.Continental; n.Color = new Color(0.75f, 0.80f, 0.84f);
                    n.Traits.AddRange(new[] { SpeciesTrait.Industrious, SpeciesTrait.RapidBreeders });
                    break;
            }
            return Add(n);
        }

        // ==================== РОБОТЫ ====================

        public static int RobotId
        {
            get
            {
                foreach (var s in _all) if (s.Kind == SpeciesKind.Robot) return s.Id;
                var r = new Species
                {
                    Kind = SpeciesKind.Robot, Faction = "robot", Name = "Роботы", Home = PlanetType.Barren,
                    Color = new Color(0.62f, 0.74f, 0.82f)
                };
                r.Traits.Add(SpeciesTrait.Mechanical);
                return Add(r).Id;
            }
        }

        // ==================== ГИБРИДЫ ====================

        public static int HybridCount
        {
            get { int n = 0; foreach (var s in _all) if (s.Kind == SpeciesKind.Hybrid) n++; return n; }
        }

        /// <summary>Найти или вывести гибрид двух органических видов (null — нельзя: роботы, тот же корень, лимит).</summary>
        public static Species Hybrid(int a, int b, out bool created)
        {
            created = false;
            var sa = Get(a); var sb = Get(b);
            if (sa == null || sb == null || sa.Machine || sb.Machine) return null;
            var ra = Root(a); var rb = Root(b);
            if (ra == null || rb == null || ra.Id == rb.Id) return null;
            foreach (var s in _all)
                if (s.Kind == SpeciesKind.Hybrid && ((s.ParentA == a && s.ParentB == b) || (s.ParentA == b && s.ParentB == a))) return s;
            if (HybridCount >= MaxHybrids) return null;

            // Гибрид берёт сильные стороны обоих родителей (до трёх), слабые уходят; гибкость — от смешанной крови
            var h = new Species
            {
                Kind = SpeciesKind.Hybrid, Faction = sa.Faction, ParentA = a, ParentB = b, Home = sa.Home,
                Color = Color.Lerp(sa.Color, sb.Color, 0.5f)
            };
            foreach (var t in sa.Traits) if (!TraitInfo.IsNegative(t) && !h.Has(t) && h.Traits.Count < 2) h.Traits.Add(t);
            foreach (var t in sb.Traits) if (!TraitInfo.IsNegative(t) && !h.Has(t) && h.Traits.Count < 3) h.Traits.Add(t);
            if (!h.Has(SpeciesTrait.Adaptive)) h.Traits.Add(SpeciesTrait.Adaptive);
            created = true;
            return Add(h);
        }

        public static string HybridPrefix(Species s) => s?.Faction switch
        {
            "xarn" => Loc.T("Ксарно"),
            "astrea" => Loc.T("Астрео"),
            "aquila" => Loc.T("Аквило"),
            "iridia" => Loc.T("Иридо"),
            "terraan" => Loc.T("Тэрра"),
            "nol" => Loc.T("Синто"),
            _ => Loc.T("Ново")
        };

        public static string HybridTail(Species s) => s?.Faction switch
        {
            "xarn" => Loc.T("ксарнийцы"),
            "astrea" => Loc.T("астрейцы"),
            "aquila" => Loc.T("аквилианцы"),
            "iridia" => Loc.T("иридийцы"),
            "terraan" => Loc.T("тэрра'ан"),
            "nol" => Loc.T("синтеты"),
            _ => Loc.T("поселенцы")
        };

        // ==================== ГЕННАЯ ИНЖЕНЕРИЯ ====================

        public static string LineName(PlanetType t) => t switch
        {
            PlanetType.Continental => Loc.T("континентальная линия"),
            PlanetType.Desert => Loc.T("пустынная линия"),
            PlanetType.Barren => Loc.T("линия пустошей"),
            PlanetType.Molten => Loc.T("огненная линия"),
            _ => Loc.T("газовая линия")
        };

        /// <summary>Какая черта уйдёт при генной адаптации (первая отрицательная; null — нечего исправлять).</summary>
        public static SpeciesTrait? FixableTrait(Species s)
        {
            if (s == null) return null;
            foreach (var t in s.Traits) if (TraitInfo.IsNegative(t)) return t;
            return null;
        }

        /// <summary>
        /// Найти или создать линию вида, выведенную под климат планеты: родной мир — этот тип,
        /// одна отрицательная черта вычищена; если исправлять нечего — вид становится плодовитым.
        /// </summary>
        public static Species Adapted(int baseId, PlanetType home)
        {
            var b = Get(baseId);
            if (b == null || b.Machine) return null;
            foreach (var s in _all)
                if (s.Kind == SpeciesKind.Adapted && s.ParentA == baseId && s.Home == home) return s;
            var n = new Species
            {
                Kind = SpeciesKind.Adapted, Faction = b.Faction, ParentA = baseId, Home = home,
                Color = Color.Lerp(b.Color, Color.white, 0.25f)
            };
            n.Traits.AddRange(b.Traits);
            var fix = FixableTrait(b);
            if (fix.HasValue) n.Traits.Remove(fix.Value);
            else if (!n.Has(SpeciesTrait.RapidBreeders)) n.Traits.Add(SpeciesTrait.RapidBreeders);
            return Add(n);
        }

        /// <summary>Можно ли адаптировать вид к этому климату (новая линия будет отличаться от исходной).</summary>
        public static bool CanAdapt(Species s, PlanetType home)
        {
            if (s == null || s.Machine) return false;
            if (s.Kind == SpeciesKind.Adapted && s.Home == home) return false;
            return s.Home != home || FixableTrait(s).HasValue;
        }
    }
}
