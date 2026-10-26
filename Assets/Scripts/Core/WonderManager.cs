using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Core
{
    /// <summary>Этап чуда: стоимость, срок, требование по технологиям и что он даёт.</summary>
    public sealed class WonderStage
    {
        public string Name, Desc, Effect;
        public float Energy, Minerals, Alloys, Influence;
        public int Days, Techs;
        /// <summary>Ежемесячный доход (вписывается в базу один раз — сохранение его помнит).</summary>
        public float IncEnergy, IncMinerals, IncAlloys, IncInfluence, Science;
        /// <summary>Множители бонусов империи (накладываются заново после загрузки).</summary>
        public Action<EmpireBonuses> Bonus;
        /// <summary>Разовое действие при завершении — только для игрока (население, карта, отношения).</summary>
        public Action OnPlayerComplete;
    }

    /// <summary>Чудо фракции: одно на империю, три этапа.</summary>
    public sealed class WonderDef
    {
        public string Key, Id, Name, Title, Lore, Site, Art;
        public Color Color;
        public Func<StarSystem, int, bool> SiteOk;
        /// <summary>Множитель дохода от места (Сфера Дайсона: яркая звезда даёт больше).</summary>
        public Func<StarSystem, float> SiteYield;
        public WonderStage[] Stages;
    }

    [Serializable]
    public class WonderSave
    {
        public int Owner;
        public string Key;
        public int SystemId = -1;
        /// <summary>Сколько этапов завершено (0…3).</summary>
        public int Built;
        public bool InProgress;
        public float DaysLeft, DaysTotal;
        /// <summary>Перезарядка Сети Врат, дней.</summary>
        public int GateCooldown;
        /// <summary>Отдача места, зафиксированная при закладке (доход этапов умножается на неё).</summary>
        public float Yield = 1f;
        [NonSerialized] public bool PauseNoted;
    }

    /// <summary>
    /// Чудеса фракций — у каждой из шести фракций своё, строится в три этапа по году-два:
    ///   • Доминион Ксарн — «Наковальня Ксарна» (звёздная кузня: сплавы, дешёвые корабли, урон флота);
    ///   • Республика Астрея — «Кольцо Согласия» (кольцо-форум: влияние, наука, исследования);
    ///   • Синдикат Аквила — «Сфера Дайсона» (гелий-3; яркая звезда даёт больше);
    ///   • Иридийский Оракул — «Врата Провидения» (гиперскорость, карта, мгновенная переброска флотов);
    ///   • Конклав Тэрра'ан — «Древо Миров» (титан, наука, население);
    ///   • Машинный Консенсус «Ноль» — «Мозг-Матрёшка» (наука и исследования).
    /// Этап требует изученных технологий, оплачивается сразу и идёт, пока система в руках строителя.
    /// Империи ИИ строят свои чудеса по тем же правилам — о встреченных соперниках приходят вести.
    /// Цепочки событий дают скидку: «Сердце Тишины» (−25% цена и срок), «Пробуждённый разум» (−20% срок).
    /// </summary>
    public class WonderManager : MonoBehaviour
    {
        public static WonderManager Instance { get; private set; }

        public static event Action OnChanged;

        public const int ScorePerStage = 40, ScoreComplete = 60;
        public const int GateRecharge = 60;

        private readonly Dictionary<int, WonderSave> _states = new Dictionary<int, WonderSave>();
        private bool _subscribed;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start() => TrySubscribe();

        private void TrySubscribe()
        {
            if (_subscribed || TimeManager.Instance == null) return;
            TimeManager.Instance.OnDayPassed += HandleDay;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (_subscribed && TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
            if (Instance == this) Instance = null;
        }

        // ==================== ОПРЕДЕЛЕНИЯ ====================

        private static Dictionary<string, WonderDef> s_defs;

        public static IEnumerable<WonderDef> AllDefs { get { Init(); return s_defs.Values; } }

        public static WonderDef DefByKey(string key)
        {
            Init();
            return key != null && s_defs.TryGetValue(key, out var d) ? d : null;
        }

        public static string KeyOf(int owner) => FactionRegistry.KeyOf(FactionTraits.FactionOf(owner)) ?? (owner == 0 ? "astrea" : null);

        public static WonderDef DefOf(int owner) => DefByKey(KeyOf(owner));

        private static float StarYield(StarSystem s)
        {
            if (s == null) return 1f;
            switch (s.SpectralClass)
            {
                case StarSpectralClass.ClassB: return 1.5f;
                case StarSpectralClass.ClassA: return 1.25f;
                case StarSpectralClass.ClassK: return 0.85f;
                case StarSpectralClass.ClassM: return 0.7f;
                default: return 1f;
            }
        }

        private static bool Owned(StarSystem s, int owner) => s != null && s.OwnerId == owner;

        private static bool HasColony(StarSystem s)
        {
            foreach (var p in s.Planets) if (p.Population > 0) return true;
            return false;
        }

        private static WonderStage Stage(string name, string desc, string effect, int techs, int days,
                                         float alloys, float minerals = 0f, float energy = 0f, float influence = 0f)
            => new WonderStage { Name = name, Desc = desc, Effect = effect, Techs = techs, Days = days,
                                 Alloys = alloys, Minerals = minerals, Energy = energy, Influence = influence };

        private static void Init()
        {
            if (s_defs != null) return;
            s_defs = new Dictionary<string, WonderDef>();

            // ---- Доминион Ксарн ----
            var forge = new WonderDef
            {
                Key = "xarn", Id = "forge", Name = "Наковальня Ксарна", Title = "ЗВЁЗДНАЯ КУЗНЯ", Art = "Wonder_Forge|Drills",
                Color = new Color(1f, 0.42f, 0.22f),
                Lore = "Доминион не просит у звёзд света — он берёт у них металл. Три гироскопических кольца вокруг светила " +
                       "выдирают из короны потоки плазмы и гонят их в кузню размером с луну. Говорят, отблеск Наковальни видно " +
                       "за сотню световых лет — и каждый, кто его видит, знает, чьи это звёзды.",
                Site = "Своя система со звёздной базой (не чёрная дыра)",
                SiteOk = (s, o) => Owned(s, o) && s.HasStarbase && s.SpectralClass != StarSpectralClass.BlackHole,
                Stages = new[]
                {
                    Stage("Первое кольцо", "Гироскоп из тяжёлых сегментов ложится на орбиту и начинает черпать корону.",
                          "+5 сплавов в месяц", 5, 360, 250f, 350f),
                    Stage("Звёздный подъёмник", "Второе кольцо и кузня-наковальня: плазма течёт прямо в литейные цеха.",
                          "+6 сплавов в месяц · корабли дешевле на 10%", 13, 540, 450f, 500f, 0f, 80f),
                    Stage("Сердце Наковальни", "Третье кольцо замыкает гироскоп. Звезда работает на флот Доминиона.",
                          "+8 сплавов в месяц · урон всего флота +10%", 22, 720, 700f, 0f, 600f, 150f),
                }
            };
            forge.Stages[0].IncAlloys = 5f;
            forge.Stages[1].IncAlloys = 6f; forge.Stages[1].Bonus = b => b.ShipCostMult -= 0.10f;
            forge.Stages[2].IncAlloys = 8f; forge.Stages[2].Bonus = b => b.AllDamage += 0.10f;
            s_defs[forge.Key] = forge;

            // ---- Республика Астрея ----
            var ring = new WonderDef
            {
                Key = "astrea", Id = "ring", Name = "Кольцо Согласия", Title = "ОРБИТАЛЬНОЕ КОЛЬЦО-ФОРУМ", Art = "Wonder_Ring|Festival",
                Color = new Color(0.35f, 0.85f, 1f),
                Lore = "Республика строит не крепость, а площадь. Кольцо Согласия опоясывает светило лентой жилых палуб, садов и залов, " +
                       "где представители сотни миров спорят, торгуют и учатся. Его свет виден с каждой колонии Республики — " +
                       "как напоминание, что они говорят одним голосом.",
                Site = "Своя система с колонией",
                SiteOk = (s, o) => Owned(s, o) && HasColony(s),
                Stages = new[]
                {
                    Stage("Опорные сегменты", "Три якорных сегмента и спицы, на которых повиснет кольцо.",
                          "+2 влияния в месяц", 5, 360, 250f, 400f),
                    Stage("Жилые палубы", "Палубы смыкаются в дуги; первые жители переезжают на кольцо.",
                          "+6 науки в месяц · +3 населения", 13, 540, 400f, 500f, 0f, 100f),
                    Stage("Замкнутое кольцо", "Кольцо замыкается. Зал Согласия открывает двери для всей галактики.",
                          "исследования +15% · +2 влияния в месяц · +10 к отношению всех империй", 22, 720, 650f, 0f, 700f, 150f),
                }
            };
            ring.Stages[0].IncInfluence = 2f;
            ring.Stages[1].Science = 6f; ring.Stages[1].OnPlayerComplete = () => GrowPlayerPop(3);
            ring.Stages[2].IncInfluence = 2f; ring.Stages[2].Bonus = b => b.ResearchMult += 0.15f;
            ring.Stages[2].OnPlayerComplete = () => { foreach (var ai in AIEmpireManager.Alive) ai.AddMemory("incident", 10f); };
            s_defs[ring.Key] = ring;

            // ---- Синдикат Аквила ----
            var dyson = new WonderDef
            {
                Key = "aquila", Id = "dyson", Name = "Сфера Дайсона", Title = "ЭНЕРГОКОНЦЕРН «ДАЙСОН»", Art = "Wonder_Dyson|Trade",
                Color = new Color(1f, 0.78f, 0.25f),
                Lore = "Синдикат Аквила выставил звезде счёт. Миллионы зеркальных коллекторов, выведенных на орбиту роем, превращают " +
                       "светило в электростанцию, а саму сферу — в самый дорогой актив галактики. Акции концерна «Дайсон» котируются " +
                       "выше, чем валюты половины империй.",
                Site = "Своя система (не чёрная дыра). Голубые и белые звёзды дают до +50% гелия-3, красные карлики — −30%",
                SiteOk = (s, o) => Owned(s, o) && s.SpectralClass != StarSpectralClass.BlackHole,
                SiteYield = StarYield,
                Stages = new[]
                {
                    Stage("Рой коллекторов", "Первые тысячи зеркал выходят на орбиту и разворачиваются к звезде.",
                          "+10 гелия-3 в месяц × класс звезды", 5, 360, 250f, 400f),
                    Stage("Экваториальный пояс", "Рой сплетается в сплошной пояс по экватору звезды.",
                          "+14 гелия-3 в месяц × класс звезды · +2 влияния в месяц", 13, 540, 450f, 600f, 0f, 80f),
                    Stage("Замкнутая сфера", "Сфера смыкается. Звезда гаснет для всей галактики — и горит только для Синдиката.",
                          "+22 гелия-3 в месяц × класс звезды · титан +10%", 22, 720, 700f, 0f, 500f, 150f),
                }
            };
            dyson.Stages[0].IncEnergy = 10f;
            dyson.Stages[1].IncEnergy = 14f; dyson.Stages[1].IncInfluence = 2f;
            dyson.Stages[2].IncEnergy = 22f; dyson.Stages[2].Bonus = b => b.MineralsMult += 0.10f;
            s_defs[dyson.Key] = dyson;

            // ---- Иридийский Оракул ----
            var gate = new WonderDef
            {
                Key = "iridia", Id = "gate", Name = "Врата Провидения", Title = "ВРАТА МЕЖДУ ЗВЁЗДАМИ", Art = "Wonder_Gate|Rift",
                Color = new Color(0.72f, 0.5f, 1f),
                Lore = "Верховная Оракул видела их во сне задолго до того, как инженеры поняли, что это возможно: кольцо из живого кристалла, " +
                       "внутри которого пространство складывается, как ткань. Иридийцы не называют Врата машиной. " +
                       "Для них это дверь, которую будущее держит открытой.",
                Site = "Своя система, из которой ведут хотя бы 3 гиперкоридора",
                SiteOk = (s, o) => Owned(s, o) && s.ConnectedSystemIds.Count >= 3,
                Stages = new[]
                {
                    Stage("Кристальное кольцо", "Кольцо из выращенного кристалла собирается вокруг точки фокуса.",
                          "+3 влияния в месяц", 5, 360, 250f, 350f),
                    Stage("Зеркало горизонта", "Кольцо настроено: пространство внутри дрожит, как поверхность воды.",
                          "скорость в гиперкоридорах +15% · открыта карта на 5 прыжков", 13, 540, 400f, 500f, 0f, 100f),
                    Stage("Открытие Врат", "Врата открыты. Флоты Оракула шагают сквозь них в любую свою систему.",
                          "Сеть Врат: флоты у Врат мгновенно переходят в любую свою систему (перезарядка 60 дней) · гиперскорость ещё +10% · +3 влияния",
                          22, 720, 650f, 0f, 700f, 150f),
                }
            };
            gate.Stages[0].IncInfluence = 3f;
            gate.Stages[1].Bonus = b => b.HyperlaneSpeed += 0.15f; gate.Stages[1].OnPlayerComplete = () => RevealPlayer(5);
            gate.Stages[2].IncInfluence = 3f; gate.Stages[2].Bonus = b => b.HyperlaneSpeed += 0.10f;
            s_defs[gate.Key] = gate;

            // ---- Конклав Тэрра'ан ----
            var tree = new WonderDef
            {
                Key = "terraan", Id = "tree", Name = "Древо Миров", Title = "ЖИВАЯ МЕГАСТРУКТУРА", Art = "Wonder_Tree|Amoeba",
                Color = new Color(0.45f, 1f, 0.45f),
                Lore = "Конклав не строит — он сажает. Семя, выращенное за тысячу лет в сердце мира-сада, укореняется в поясе астероидов " +
                       "и тянет к звезде побеги длиной в миллионы километров. Древо Миров пьёт свет, дышит пылью и поёт на частоте мицелия — " +
                       "и каждый тэрра'ан в галактике слышит эту песню.",
                Site = "Своя система с колонией",
                SiteOk = (s, o) => Owned(s, o) && HasColony(s),
                Stages = new[]
                {
                    Stage("Корни в поясе", "Семя пускает корни в астероиды и начинает пить металл.",
                          "+5 титана в месяц · +1 влияния в месяц", 5, 360, 200f, 450f),
                    Stage("Ствол к звезде", "Побеги тянутся к светилу, по ним течёт свет и сок.",
                          "+6 науки в месяц · +3 населения", 13, 540, 400f, 550f, 0f, 100f),
                    Stage("Крона", "Крона распускается вокруг звезды. Песня Древа слышна во всей галактике.",
                          "исследования +10% · +3 влияния и +4 гелия-3 в месяц", 22, 720, 600f, 0f, 700f, 150f),
                }
            };
            tree.Stages[0].IncMinerals = 5f; tree.Stages[0].IncInfluence = 1f;
            tree.Stages[1].Science = 6f; tree.Stages[1].OnPlayerComplete = () => GrowPlayerPop(3);
            tree.Stages[2].IncInfluence = 3f; tree.Stages[2].IncEnergy = 4f; tree.Stages[2].Bonus = b => b.ResearchMult += 0.10f;
            s_defs[tree.Key] = tree;

            // ---- Машинный Консенсус «Ноль» ----
            var brain = new WonderDef
            {
                Key = "nol", Id = "brain", Name = "Мозг-Матрёшка", Title = "ВЫЧИСЛИТЕЛЬНЫЙ РАЗУМ ЗВЕЗДЫ", Art = "Wonder_Brain|Breakthrough",
                Color = new Color(0.7f, 0.92f, 1f),
                Lore = "Консенсусу тесно в планетах. Вложенные друг в друга оболочки вычислителей окружают звезду: внутренняя пьёт её свет, " +
                       "каждая следующая питается теплом предыдущей. Миллиард машин становится одной мыслью размером с солнечную систему — " +
                       "и эта мысль уже просчитала, чем закончится галактика.",
                Site = "Своя система (не чёрная дыра)",
                SiteOk = (s, o) => Owned(s, o) && s.SpectralClass != StarSpectralClass.BlackHole,
                Stages = new[]
                {
                    Stage("Внутренняя оболочка", "Первая сфера вычислителей ложится на ближнюю орбиту и начинает пить свет звезды.",
                          "+6 науки в месяц", 5, 360, 250f, 400f),
                    Stage("Средняя оболочка", "Вторая оболочка питается теплом первой: мощность растёт вдвое.",
                          "исследования +10% · +2 сплава в месяц", 13, 540, 450f, 500f, 0f, 80f),
                    Stage("Внешняя оболочка", "Третья оболочка замыкает каскад. Звезда думает.",
                          "+10 науки в месяц · исследования ещё +10%", 22, 720, 700f, 0f, 600f, 120f),
                }
            };
            brain.Stages[0].Science = 6f;
            brain.Stages[1].IncAlloys = 2f; brain.Stages[1].Bonus = b => b.ResearchMult += 0.10f;
            brain.Stages[2].Science = 10f; brain.Stages[2].Bonus = b => b.ResearchMult += 0.10f;
            s_defs[brain.Key] = brain;
        }

        // ==================== СОСТОЯНИЕ ====================

        public WonderSave StateOf(int owner)
        {
            string key = KeyOf(owner);
            if (_states.TryGetValue(owner, out var s))
            {
                // Фракция выбрана позже первого обращения — пока ничего не построено, чудо следует за ней
                if (key != null && s.Key != key && s.Built == 0 && !s.InProgress) s.Key = key;
                return s;
            }
            if (key == null) return null;
            s = new WonderSave { Owner = owner, Key = key };
            _states[owner] = s;
            return s;
        }

        /// <summary>Все чудеса с выбранным местом (для отрисовки).</summary>
        public IEnumerable<WonderSave> Sited
        {
            get { foreach (var s in _states.Values) if (s.SystemId >= 0 && (s.Built > 0 || s.InProgress)) yield return s; }
        }

        public static int ScoreOf(int owner)
        {
            var wm = Instance;
            if (wm == null || !wm._states.TryGetValue(owner, out var s)) return 0;
            return s.Built * ScorePerStage + (s.Built >= 3 ? ScoreComplete : 0);
        }

        public static bool IsComplete(WonderSave s) => s != null && s.Built >= 3;

        public float Progress(WonderSave s)
            => s == null || !s.InProgress || s.DaysTotal <= 0f ? 0f : Mathf.Clamp01(1f - s.DaysLeft / s.DaysTotal);

        // ---- Скидки цепочек ----

        public static float CostMult(int owner) => owner == 0 && EventDatabase.HasChainFlag("wonder_key") ? 0.75f : 1f;

        public static float TimeMult(int owner)
        {
            if (owner != 0) return 1f;
            float k = 1f;
            if (EventDatabase.HasChainFlag("wonder_key")) k *= 0.75f;
            if (EventDatabase.HasChainFlag("wonder_mind")) k *= 0.8f;
            return k;
        }

        public static void Cost(WonderStage st, int owner, out float e, out float m, out float a, out float i)
        {
            float k = CostMult(owner);
            e = st.Energy * k; m = st.Minerals * k; a = st.Alloys * k; i = st.Influence * k;
        }

        public static int StageDays(WonderStage st, int owner) => Mathf.Max(30, Mathf.RoundToInt(st.Days * TimeMult(owner)));

        private static int TechCount(int owner) => EmpireStats.TechCount(owner);

        /// <summary>Можно ли начать следующий этап в системе systemId; reason — почему нет.</summary>
        public bool CanStart(int owner, int systemId, out string reason)
        {
            reason = null;
            var st = StateOf(owner);
            var def = DefOf(owner);
            if (st == null || def == null) { reason = "Чудо фракции неизвестно"; return false; }
            if (st.Built >= def.Stages.Length) { reason = "Чудо завершено"; return false; }
            if (st.InProgress) { reason = "Этап уже строится"; return false; }
            var stage = def.Stages[st.Built];
            int sysId = st.SystemId >= 0 ? st.SystemId : systemId;
            var sys = EmpireStats.GetSystem(sysId);
            if (sys == null) { reason = "Выберите систему для чуда"; return false; }
            if (!def.SiteOk(sys, owner))
            {
                reason = st.SystemId >= 0 && sys.OwnerId != owner ? $"Система {sys.Name} потеряна — верните её" : $"Место не подходит: {def.Site.ToLower()}";
                return false;
            }
            int techs = TechCount(owner);
            if (techs < stage.Techs) { reason = $"Нужно изученных технологий: {stage.Techs} (сейчас {techs})"; return false; }
            Cost(stage, owner, out float e, out float m, out float a, out float i);
            if (owner == 0)
            {
                var eco = EconomyManager.Instance;
                if (eco == null || !eco.CanAfford(e, m, a, i)) { reason = "Не хватает ресурсов"; return false; }
            }
            else
            {
                var ai = AIEmpireManager.For(owner);
                if (ai == null || ai.EnergyCredits < e || ai.Minerals < m || ai.Alloys < a || ai.Influence < i) { reason = "Не хватает ресурсов"; return false; }
            }
            return true;
        }

        /// <summary>Заложить следующий этап (первый — в systemId, дальше место закреплено).</summary>
        public bool StartStage(int owner, int systemId)
        {
            if (!CanStart(owner, systemId, out _)) return false;
            var st = StateOf(owner);
            var def = DefOf(owner);
            var stage = def.Stages[st.Built];
            Cost(stage, owner, out float e, out float m, out float a, out float i);
            if (owner == 0)
            {
                if (!EconomyManager.Instance.TrySpend(e, m, a, i)) return false;
            }
            else
            {
                var ai = AIEmpireManager.For(owner);
                ai.EnergyCredits -= e; ai.Minerals -= m; ai.Alloys -= a; ai.Influence -= i;
            }
            if (st.SystemId < 0)
            {
                st.SystemId = systemId;
                st.Yield = def.SiteYield != null ? def.SiteYield(EmpireStats.GetSystem(systemId)) : 1f;
            }
            st.InProgress = true;
            st.DaysTotal = st.DaysLeft = StageDays(stage, owner);
            if (owner == 0)
            {
                SFXManager.Play(Sfx.BuildQueued);
                NotificationCenter.Show(def.Name, $"Заложен этап «{stage.Name}» — {FormatDays(st.DaysTotal)}", NotificationCenter.Kind.Success, 5f);
            }
            else NotifyRival(owner, def, $"начинает строить «{def.Name}»", st.Built == 0);
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Отменить строящийся этап: возвращается половина ресурсов. Место первого этапа освобождается.</summary>
        public void CancelStage(int owner)
        {
            var st = StateOf(owner);
            var def = DefOf(owner);
            if (st == null || def == null || !st.InProgress) return;
            Cost(def.Stages[st.Built], owner, out float e, out float m, out float a, out float i);
            if (owner == 0)
            {
                var eco = EconomyManager.Instance;
                eco.EnergyCredits += e * 0.5f; eco.Minerals += m * 0.5f; eco.Alloys += a * 0.5f; eco.Influence += i * 0.5f;
                eco.RaiseResourcesChanged();
            }
            st.InProgress = false;
            st.DaysLeft = st.DaysTotal = 0f;
            if (st.Built == 0) st.SystemId = -1;
            OnChanged?.Invoke();
        }

        public static string FormatDays(float days)
        {
            if (days >= 330f) { float y = days / 360f; return y < 1.1f ? "≈ 1 год" : $"≈ {y:0.#} {(y < 5f ? "года" : "лет")}"; }
            return $"≈ {Mathf.Max(1, Mathf.RoundToInt(days / 30f))} мес.";
        }

        // ==================== КАЛЕНДАРЬ ====================

        private void HandleDay(int day, int month, int year)
        {
            if (!UIManager.IsGameStarted) return;
            bool changed = false;
            foreach (var st in _states.Values)
            {
                if (st.GateCooldown > 0) st.GateCooldown--;
                if (!st.InProgress) continue;
                var sys = EmpireStats.GetSystem(st.SystemId);
                if (sys == null || sys.OwnerId != st.Owner)
                {
                    // Система потеряна — стройка замирает до возвращения
                    if (!st.PauseNoted && st.Owner == 0)
                        NotificationCenter.Show("Стройка чуда остановлена", $"Система {sys?.Name ?? "?"} больше не наша", NotificationCenter.Kind.Danger, 6f);
                    st.PauseNoted = true;
                    continue;
                }
                st.PauseNoted = false;
                st.DaysLeft -= 1f;
                if (st.DaysLeft <= 0f) { Complete(st); changed = true; }
            }
            if (day == 1) ThinkAI();
            if (changed) OnChanged?.Invoke();
        }

        private void Complete(WonderSave st)
        {
            var def = DefByKey(st.Key);
            if (def == null) return;
            var stage = def.Stages[st.Built];
            st.InProgress = false;
            st.DaysLeft = 0f;
            st.Built++;

            ApplyIncome(st.Owner, stage, st.Yield);
            var b = EmpireBonuses.For(st.Owner);
            ApplyBonus(st.Owner, stage, b, 1f);
            if (st.Owner == 0) { stage.OnPlayerComplete?.Invoke(); EconomyManager.Instance?.RecalculateAll(); }

            bool done = st.Built >= def.Stages.Length;
            if (st.Owner == 0) PresentStage(def, stage, st.Built, done);
            else NotifyRival(st.Owner, def, done ? $"завершает чудо «{def.Name}»!" : $"завершает этап «{stage.Name}» чуда «{def.Name}»", done);
        }

        private static void ApplyIncome(int owner, WonderStage stage, float yield)
        {
            float e = stage.IncEnergy * yield, m = stage.IncMinerals, a = stage.IncAlloys, i = stage.IncInfluence;
            if (owner == 0)
            {
                EconomyManager.Instance?.AddIncome(e, m, a, i);
                if (stage.Science > 0f) TechnologyManager.Instance?.AddMonthlyScience(stage.Science);
                EconomyManager.Instance?.RaiseResourcesChanged();
            }
            else
            {
                var ai = AIEmpireManager.For(owner);
                if (ai == null) return;
                ai.BaseEnergyIncome += e; ai.BaseMineralsIncome += m; ai.BaseAlloysIncome += a; ai.BaseInfluenceIncome += i;
            }
        }

        /// <summary>Множители этапа. У ИИ нет «плоской» науки — она превращается в +1,2% к исследованиям за единицу.</summary>
        private static void ApplyBonus(int owner, WonderStage stage, EmpireBonuses b, float sign)
        {
            if (b == null) return;
            if (sign > 0f) stage.Bonus?.Invoke(b);
            if (owner != 0 && stage.Science > 0f) b.ResearchMult += stage.Science * 0.012f * sign;
        }

        /// <summary>Торжественное окно игроку: иллюстрация чуда, что дал этап.</summary>
        private static void PresentStage(WonderDef def, WonderStage stage, int built, bool done)
        {
            string title = done ? $"{def.Name.ToUpper()} ЗАВЕРШЕНО" : $"{def.Name.ToUpper()}: ЭТАП {built} ИЗ 3";
            string text = done
                ? $"{stage.Desc}\n\nПоследний сегмент встал на место. О чуде нашей фракции говорит вся галактика — и каждый, кто смотрит на " +
                  $"наше небо, знает, на что мы способны.\n\n<b>Этап «{stage.Name}»:</b> {stage.Effect}."
                : $"{stage.Desc}\n\n<b>Этап «{stage.Name}» завершён:</b> {stage.Effect}.\n\nСледующий этап можно заложить в окне чуда.";
            var ev = new GameEventData("wonder_" + def.Id, title, text, AnomalyType.PrecursorRelic, new List<EventOption>
            {
                new EventOption(done ? "СЛАВА ИМПЕРИИ" : "ПРОДОЛЖАТЬ СТРОЙКУ", stage.Effect, () => { })
            })
            { Art = def.Art, Subtitle = def.Title };
            if (AnomalyEventSystem.Instance != null) AnomalyEventSystem.Instance.Present(ev);
            else NotificationCenter.Show(title, stage.Effect, NotificationCenter.Kind.Success, 8f);
        }

        private static void NotifyRival(int owner, WonderDef def, string what, bool important)
        {
            if (!Contacts.PlayerMet(owner)) return;
            NotificationCenter.Show(def.Name, $"{AIEmpireManager.NameOf(owner)} {what}",
                important ? NotificationCenter.Kind.Warning : NotificationCenter.Kind.Info, important ? 7f : 5f);
        }

        // ==================== ИИ ====================

        /// <summary>
        /// Раз в месяц: ИИ закладывает следующий этап, когда хватает технологий и ресурсы покрывают цену с запасом
        /// (чудо не должно оставить империю без флота). Место — столица, иначе любая подходящая своя система.
        /// </summary>
        private void ThinkAI()
        {
            foreach (var ai in AIEmpireManager.Alive)
            {
                var st = StateOf(ai.OwnerId);
                var def = DefOf(ai.OwnerId);
                if (st == null || def == null || st.InProgress || st.Built >= def.Stages.Length) continue;
                var stage = def.Stages[st.Built];
                if (TechCount(ai.OwnerId) < stage.Techs) continue;
                if (ai.AtWar && UnityEngine.Random.value < 0.7f) continue;
                Cost(stage, ai.OwnerId, out float e, out float m, out float a, out float i);
                if (ai.EnergyCredits < e + 80f || ai.Minerals < m + 100f || ai.Alloys < a * 1.3f + 60f || ai.Influence < i + 20f) continue;
                int site = st.SystemId >= 0 ? st.SystemId : PickSite(ai.OwnerId, def, ai.CapitalSystemId);
                if (site < 0) continue;
                StartStage(ai.OwnerId, site);
            }
        }

        public static int PickSite(int owner, WonderDef def, int preferred)
        {
            var pref = EmpireStats.GetSystem(preferred);
            if (pref != null && def.SiteOk(pref, owner)) return preferred;
            int best = -1;
            float bestScore = float.MinValue;
            foreach (var s in EmpireStats.Systems)
            {
                if (!def.SiteOk(s, owner)) continue;
                float score = (def.SiteYield != null ? def.SiteYield(s) * 10f : 0f) + s.Planets.Count;
                if (score > bestScore) { bestScore = score; best = s.Id; }
            }
            return best;
        }

        // ==================== СЕТЬ ВРАТ ====================

        public bool GateOpen(int owner)
        {
            var st = StateOf(owner);
            return st != null && st.Key == "iridia" && st.Built >= 3 && Owned(EmpireStats.GetSystem(st.SystemId), owner);
        }

        /// <summary>Перебросить все свои флоты, стоящие у Врат, в систему dest (своя система).</summary>
        public int GateJump(int dest)
        {
            var st = StateOf(0);
            var target = EmpireStats.GetSystem(dest);
            if (!GateOpen(0) || st.GateCooldown > 0 || target == null || target.OwnerId != 0 || dest == st.SystemId) return 0;
            var fm = FleetManager.Instance;
            if (fm == null) return 0;
            int moved = 0;
            foreach (var f in new List<FleetView>(fm.AllFleets))
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.OwnerId != 0 || d.CurrentSystemId != st.SystemId) continue;
                if (f.GateTransfer(dest)) moved++;
            }
            if (moved > 0)
            {
                st.GateCooldown = GateRecharge;
                SFXManager.Play(Sfx.FtlJump);
                NotificationCenter.Show("Врата Провидения", $"Флотов переброшено: {moved} → {target.Name}", NotificationCenter.Kind.Success, 5f);
                OnChanged?.Invoke();
            }
            return moved;
        }

        public int FleetsAtGate()
        {
            var st = StateOf(0);
            var fm = FleetManager.Instance;
            if (st == null || st.SystemId < 0 || fm == null) return 0;
            int n = 0;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d != null && !d.Destroyed && d.OwnerId == 0 && d.CurrentSystemId == st.SystemId && d.State == FleetState.Orbiting && !d.InCombat) n++;
            }
            return n;
        }

        // ==================== ПОМОЩНИКИ ====================

        private static void GrowPlayerPop(int n)
        {
            for (int k = 0; k < n; k++)
            {
                PlanetData pick = null;
                foreach (var s in EmpireStats.Systems)
                {
                    if (s.OwnerId != 0) continue;
                    foreach (var p in s.Planets)
                        if (p.Population > 0 && p.Population < p.HousingCapacity
                            && (pick == null || p.HousingCapacity - p.Population > pick.HousingCapacity - pick.Population))
                            pick = p;
                }
                if (pick == null) break;
                pick.Population++;
            }
        }

        private static void RevealPlayer(int jumps)
        {
            var dist = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (var s in EmpireStats.Systems)
                if (s.OwnerId == 0) { dist[s.Id] = 0; q.Enqueue(s.Id); }
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                if (dist[cur] >= jumps) continue;
                foreach (int n in EmpireStats.Systems[cur].ConnectedSystemIds)
                    if (n >= 0 && n < EmpireStats.Systems.Count && !dist.ContainsKey(n)) { dist[n] = dist[cur] + 1; q.Enqueue(n); }
            }
            foreach (var id in dist.Keys) Vision.MarkExplored(0, id);
        }

        // ==================== СОХРАНЕНИЕ ====================

        public List<WonderSave> CaptureState() => new List<WonderSave>(_states.Values);

        /// <summary>
        /// Доход этапов уже вписан в сохранённую экономику; множители бонусов пересобираются вместе с технологиями,
        /// поэтому накладываем их заново (вызывать после восстановления технологий и ИИ).
        /// </summary>
        public void RestoreState(List<WonderSave> list)
        {
            _states.Clear();
            if (list != null)
                foreach (var s in list)
                {
                    if (s == null) continue;
                    _states[s.Owner] = s;
                    var def = DefByKey(s.Key);
                    if (def == null) continue;
                    var b = EmpireBonuses.For(s.Owner);
                    for (int k = 0; k < s.Built && k < def.Stages.Length; k++) ApplyBonus(s.Owner, def.Stages[k], b, 1f);
                }
            EconomyManager.Instance?.RecalculateAll();
            OnChanged?.Invoke();
        }

        /// <summary>Новая партия — чудес нет.</summary>
        public void ResetState()
        {
            _states.Clear();
            OnChanged?.Invoke();
        }
    }
}
