using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>Тайные операции агента, внедрённого в чужую империю.</summary>
    public enum SpyOp { Steal = 0, Sabotage = 1, Discord = 2 }

    public static class SpyInfo
    {
        public static string Name(SpyOp op) => op switch
        {
            SpyOp.Steal => "Кража технологии",
            SpyOp.Sabotage => "Диверсия",
            _ => "Раздор"
        };

        public static string Desc(SpyOp op) => op switch
        {
            SpyOp.Steal => "Агент выкрадывает чертежи: изучает технологию, которая есть у них и нет у вас. Если она вам пока недоступна, вы получаете половину её исследования.",
            SpyOp.Sabotage => "Диверсия на верфях и складах: цель теряет часть запасов сплавов и гелия-3.",
            _ => "Агент стравливает две чужие империи: их отношения портятся, а война между ними становится вероятнее."
        };

        public static float Cost(SpyOp op) => op switch { SpyOp.Steal => 20f, SpyOp.Sabotage => 15f, _ => 25f };
        public static float MinCover(SpyOp op) => op switch { SpyOp.Steal => 40f, SpyOp.Sabotage => 25f, _ => 50f };
        public static int Months(SpyOp op) => op switch { SpyOp.Steal => 3, SpyOp.Sabotage => 2, _ => 3 };
        public static float BaseChance(SpyOp op) => op switch { SpyOp.Steal => 0.40f, SpyOp.Sabotage => 0.55f, _ => 0.50f };
        public static LGIcon Icon(SpyOp op) => op switch { SpyOp.Steal => LGIcon.Research, SpyOp.Sabotage => LGIcon.Wrench, _ => LGIcon.Swords };
    }

    [Serializable]
    public class EspLogEntry
    {
        public string Date;
        public string Text;
        /// <summary>0 — сведения, 1 — успех, 2 — провал, 3 — чужая операция против вас.</summary>
        public int Kind;
    }

    [Serializable]
    public class EspionageSave
    {
        public List<EspLogEntry> Log = new List<EspLogEntry>();
        public List<int> AIOwners = new List<int>();
        public List<int> AITimers = new List<int>();
        public int Succeeded, Failed, AgentsLost, Foiled, Breaches;
        /// <summary>Ваша разведсеть по системам: id системы и сила 0–100.</summary>
        public List<int> NetSys = new List<int>();
        public List<float> NetVal = new List<float>();
    }

    /// <summary>Разбор месячного прироста разведсети в системе агента (для подсказки на карте).</summary>
    public struct NetGain
    {
        public float Base, Level, TraitMul, TechMul, CounterMul, Total;
    }

    /// <summary>
    /// Шпионаж. Агент — четвёртый класс лидеров. Его посылают в контрразведку дома или внедряют в чужую империю.
    ///   • Разведсеть, как в Hearts of Iron: агент стоит в конкретной системе цели и каждый месяц строит сеть —
    ///     сильнее всего в своей системе, слабее у соседей по гиперкоридорам (×0,6) и у их соседей (×0,3).
    ///     Без агента сеть тает. Сила сети в империи — среднее по её системам (столица весит втрое, колонии вдвое),
    ///     поэтому важно, откуда строить. Награды по порогам: 15% в системе — она видна; 20% — обзор всей территории
    ///     и флотов цели; 25% — «Диверсия»; 30% — «перехват чертежей» (известные цели технологии +25% к скорости);
    ///     40% — «Кража технологии»; 50% — досье (запасы, доходы, наука, мощь) и «Раздор».
    ///   • Операции — «Кража технологии», «Диверсия», «Раздор»: стоят влияния, идут несколько месяцев, шанс успеха
    ///     зависит от агента, его уровня, черт, внедрённости, технологий и контрразведки цели. Провал — агент либо
    ///     схвачен (скандал, отношения падают), либо «сгорел» и половина сети потеряна.
    ///   • Контрразведка: агент дома и технологии (шифрование) снижают шансы чужих агентов и раскрывают их.
    ///   • ИИ тоже шпионит за вами: изредка пытается украсть технологию, устроить диверсию или похитить запасы.
    ///     О раскрытой операции вы узнаёте, чьих она рук дело; нераскрытая остаётся безымянной.
    /// </summary>
    public class EspionageManager : MonoBehaviour
    {
        public static EspionageManager Instance { get; private set; }

        /// <summary>Состояние шпионажа изменилось (интерфейс перестраивается).</summary>
        public static event Action OnChanged;

        public const float AIOpInfluence = 15f;
        public const float IntelCover = 20f, DeepIntelCover = 50f;
        /// <summary>С этой силы сети — «перехват чертежей»: технологии, известные цели, изучаются быстрее.</summary>
        public const float BlueprintCover = 30f, BlueprintBoost = 0.25f;
        /// <summary>С какой силы сети система видна на карте сама по себе.</summary>
        public const float LocalIntel = 15f;
        public const float NetDecayPerMonth = 2f;
        /// <summary>Доля прироста: система агента, соседи, соседи соседей.</summary>
        public static readonly float[] NetRings = { 1f, 0.6f, 0.3f };
        private const int MaxLog = 40;

        private readonly Dictionary<int, float> _net = new Dictionary<int, float>();

        private EspionageSave _s = new EspionageSave();
        public IReadOnlyList<EspLogEntry> Log => _s.Log;
        public int Succeeded => _s.Succeeded;
        public int Failed => _s.Failed;
        public int AgentsLost => _s.AgentsLost;
        public int Foiled => _s.Foiled;
        public int Breaches => _s.Breaches;

        private static LeaderManager Lm => LeaderManager.Instance;

        // ==================== ЖИЗНЕННЫЙ ЦИКЛ ====================

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed += HandleDay;
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
            if (Instance == this) Instance = null;
        }

        private static void Changed()
        {
            Lm?.Touch();
            OnChanged?.Invoke();
        }

        // ==================== ЗАПРОСЫ ====================

        public static IEnumerable<Leader> PlayerAgents()
        {
            var lm = Lm;
            if (lm == null) yield break;
            foreach (var l in lm.Of(0)) if (l.Class == LeaderClass.Agent) yield return l;
        }

        /// <summary>Агент игрока, внедрённый в империю target (0 — контрразведка дома).</summary>
        public static Leader AgentOn(int target) => Lm?.SpyOn(0, target);

        /// <summary>Есть ли у вас агент в этой империи с достаточной разведсетью.</summary>
        public static bool HasIntel(int owner, float minCover = IntelCover)
        {
            if (owner <= 0) return false;
            var a = AgentOn(owner);
            return a != null && a.Cover >= minCover;
        }

        /// <summary>Обзор для карты: территория империй с сетью ≥ 20% и отдельные системы с сетью ≥ 15%.</summary>
        public static void AddIntelVision(HashSet<int> set)
        {
            var lm = Lm;
            if (lm == null) return;
            foreach (var a in lm.Of(0))
            {
                if (a.Class != LeaderClass.Agent || a.Post != LeaderPost.Spy || a.TargetOwner <= 0 || a.Cover < IntelCover) continue;
                foreach (var s in EmpireStats.Systems)
                    if (s.OwnerId == a.TargetOwner) set.Add(s.Id);
            }
            var es = Instance;
            if (es != null)
                foreach (var kv in es._net)
                    if (kv.Value >= LocalIntel) set.Add(kv.Key);
        }

        // ==================== РАЗВЕДСЕТЬ ====================

        /// <summary>
        /// Множитель скорости исследования: технология известна империи, где ваша сеть ≥ 30%, — +25%
        /// (агенты пересылают чертежи). Один и тот же бонус, сколько бы таких империй ни было.
        /// </summary>
        public static float ResearchBoost(Technology tech)
        {
            if (tech == null) return 1f;
            foreach (var a in PlayerAgents())
            {
                if (a.Post != LeaderPost.Spy || a.TargetOwner <= 0 || a.Cover < BlueprintCover) continue;
                var ai = AIEmpireManager.For(a.TargetOwner);
                if (ai != null && !ai.IsEliminated && ai.HasTech(tech.Id)) return 1f + BlueprintBoost;
            }
            return 1f;
        }

        /// <summary>Сила вашей разведсети в системе, 0–100.</summary>
        public static float NetworkAt(int systemId)
        {
            var es = Instance;
            return es != null && es._net.TryGetValue(systemId, out float v) ? v : 0f;
        }

        /// <summary>Все системы с вашей сетью (id → сила).</summary>
        public static IReadOnlyDictionary<int, float> Network => Instance != null ? Instance._net : null;

        /// <summary>Вес системы в силе сети империи: столица — 3, колония — 2, остальные — 1.</summary>
        public static float SystemWeight(StarSystem s, int owner)
        {
            var ai = AIEmpireManager.For(owner);
            if (ai != null && ai.CapitalSystemId == s.Id) return 3f;
            foreach (var p in s.Planets) if (p.Population > 0) return 2f;
            return 1f;
        }

        /// <summary>Сила вашей разведсети в империи owner, 0–100: взвешенное среднее по её системам.</summary>
        public static float NetworkStrength(int owner)
        {
            if (owner <= 0) return 0f;
            float sum = 0f, w = 0f;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != owner) continue;
                float k = SystemWeight(s, owner);
                sum += NetworkAt(s.Id) * k;
                w += k;
            }
            return w > 0f ? sum / w : 0f;
        }

        /// <summary>Откуда строить сеть по умолчанию: столица цели, иначе её самая весомая система.</summary>
        public static int SuggestSystem(int owner)
        {
            var ai = AIEmpireManager.For(owner);
            var cap = ai != null ? EmpireStats.GetSystem(ai.CapitalSystemId) : null;
            if (cap != null && cap.OwnerId == owner) return cap.Id;
            int best = -1;
            float bw = -1f;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != owner) continue;
                float w = SystemWeight(s, owner) + s.ConnectedSystemIds.Count * 0.1f;
                if (w > bw) { bw = w; best = s.Id; }
            }
            return best;
        }

        /// <summary>Месячный прирост сети в системе агента и из чего он складывается.</summary>
        public static NetGain BuildRate(Leader a)
        {
            var tm = TechnologyManager.Instance;
            var ai = a != null ? AIEmpireManager.For(a.TargetOwner) : null;
            var g = new NetGain
            {
                Base = 4f,
                Level = a != null ? a.Level : 0,
                TraitMul = a != null && a.Has("agt_infiltrator") ? 1.25f : 1f,
                TechMul = tm != null ? tm.Bonuses.CoverSpeed : 1f,
                CounterMul = 1f - TargetCounter(ai) * 0.5f
            };
            g.Total = (g.Base + g.Level) * g.TraitMul * g.TechMul * g.CounterMul;
            return g;
        }

        /// <summary>Системы в пределах двух прыжков от center: (id, кольцо 0/1/2).</summary>
        public static List<KeyValuePair<int, int>> NetworkReach(int center)
        {
            var res = new List<KeyValuePair<int, int>>();
            var start = EmpireStats.GetSystem(center);
            if (start == null) return res;
            var seen = new HashSet<int> { center };
            var frontier = new List<StarSystem> { start };
            res.Add(new KeyValuePair<int, int>(center, 0));
            for (int ring = 1; ring < NetRings.Length; ring++)
            {
                var next = new List<StarSystem>();
                foreach (var s in frontier)
                    foreach (int id in s.ConnectedSystemIds)
                    {
                        if (!seen.Add(id)) continue;
                        var n = EmpireStats.GetSystem(id);
                        if (n == null) continue;
                        next.Add(n);
                        res.Add(new KeyValuePair<int, int>(id, ring));
                    }
                frontier = next;
            }
            return res;
        }

        /// <summary>Перенести агента в другую систему той же империи: строить сеть оттуда (готовая сеть не пропадает).</summary>
        public bool Relocate(Leader a, int systemId)
        {
            var s = EmpireStats.GetSystem(systemId);
            if (a == null || a.Post != LeaderPost.Spy || a.TargetOwner <= 0 || s == null || s.OwnerId != a.TargetOwner) return false;
            if (a.OpKind >= 0)
            {
                NotificationCenter.Show("Агент занят", "Нельзя сменить систему во время операции", NotificationCenter.Kind.Warning, 3f);
                return false;
            }
            if (a.TargetSystem == systemId) return true;
            a.TargetSystem = systemId;
            AddLog($"{a.Name} перебрался в систему {s.Name}: сеть строится оттуда.", 0);
            SFXManager.Play(StellarisClone.Core.Audio.Sfx.UiConfirm);
            Changed();
            return true;
        }

        private void GrowNetworks()
        {
            var touched = new HashSet<int>();
            foreach (var a in PlayerAgents())
            {
                if (a.Post != LeaderPost.Spy || a.TargetOwner <= 0) continue;
                var center = EmpireStats.GetSystem(a.TargetSystem);
                if (center == null || center.OwnerId != a.TargetOwner)
                {
                    a.TargetSystem = SuggestSystem(a.TargetOwner);
                    center = EmpireStats.GetSystem(a.TargetSystem);
                    if (center == null) continue;
                }
                // Во время операции сеть не растёт, но и не тает
                float gain = a.OpKind >= 0 ? 0f : BuildRate(a).Total;
                foreach (var kv in NetworkReach(center.Id))
                {
                    touched.Add(kv.Key);
                    if (gain <= 0f) continue;
                    _net.TryGetValue(kv.Key, out float v);
                    _net[kv.Key] = Mathf.Min(100f, v + gain * NetRings[kv.Value]);
                }
            }
            // Без агента сеть понемногу тает
            foreach (int id in new List<int>(_net.Keys))
            {
                if (touched.Contains(id)) continue;
                float v = _net[id] - NetDecayPerMonth;
                if (v <= 0f) _net.Remove(id); else _net[id] = v;
            }
        }

        /// <summary>Изменить вашу сеть во всех системах империи owner (операции её «засвечивают»).</summary>
        private void HurtNetwork(int owner, float minus, float mul)
        {
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != owner || !_net.TryGetValue(s.Id, out float v)) continue;
                v = Mathf.Max(0f, (v - minus) * mul);
                if (v <= 0f) _net.Remove(s.Id); else _net[s.Id] = v;
            }
        }

        /// <summary>Сила сети в империи — в поле Cover каждого агента (по нему работают обзор, досье, операции).</summary>
        private static void SyncCover()
        {
            foreach (var a in PlayerAgents())
                a.Cover = a.Post == LeaderPost.Spy && a.TargetOwner > 0 ? NetworkStrength(a.TargetOwner) : 0f;
        }

        /// <summary>Защита вашей империи от чужих агентов, 0–0,85: базовая + технологии + агент-контрразведчик.</summary>
        public static float PlayerCounter()
        {
            var tm = TechnologyManager.Instance;
            float v = 0.10f + (tm != null ? tm.Bonuses.CounterIntel : 0f);
            var ca = AgentOn(0);
            if (ca != null) v += 0.10f + ca.Level * 0.04f + (ca.Has("agt_shadow") ? 0.08f : 0f);
            return Mathf.Clamp(v, 0.05f, 0.85f);
        }

        /// <summary>Защита чужой империи от ваших агентов.</summary>
        public static float TargetCounter(AIEmpireManager ai)
        {
            if (ai == null) return 0.12f;
            float v = 0.12f + ai.Bonuses.CounterIntel;
            switch (ai.Personality)
            {
                case AIPersonality.Mystic: v += 0.08f; break;          // видят заранее
                case AIPersonality.Scientific: v += 0.05f; break;
                case AIPersonality.Trader: v += 0.03f; break;
            }
            return Mathf.Clamp(v, 0.05f, 0.85f);
        }

        private static float AISpyPower(AIEmpireManager ai)
        {
            float v = ai.Bonuses.SpyPower;
            switch (ai.Personality)
            {
                case AIPersonality.Trader: v += 0.10f; break;          // Синдикат живёт чужими секретами
                case AIPersonality.Scientific: v += 0.04f; break;
                case AIPersonality.Mystic: v += 0.02f; break;
            }
            return v;
        }

        /// <summary>Шанс успеха операции (0,10–0,92) с учётом всего.</summary>
        public static float SuccessChance(Leader agent, SpyOp op, AIEmpireManager target)
        {
            var tm = TechnologyManager.Instance;
            float p = SpyInfo.BaseChance(op)
                    + agent.Level * 0.04f
                    + (tm != null ? tm.Bonuses.SpyPower : 0f)
                    + (agent.Cover - SpyInfo.MinCover(op)) * 0.003f
                    - TargetCounter(target);
            if (agent.Has("agt_saboteur") && op == SpyOp.Sabotage) p += 0.15f;
            if (agent.Has("agt_thief") && op == SpyOp.Steal) p += 0.15f;
            if (agent.Has("agt_manipulator") && op == SpyOp.Discord) p += 0.15f;
            if (agent.Has("agt_shadow")) p += 0.05f;
            return Mathf.Clamp(p, 0.10f, 0.92f);
        }

        /// <summary>Какую технологию украл бы агент (null — красть нечего); available — изучается целиком.</summary>
        public static Technology StealTarget(AIEmpireManager ai, out bool available)
        {
            available = false;
            var tm = TechnologyManager.Instance;
            if (tm == null || ai == null) return null;
            Technology best = null;
            foreach (var t in tm.AllTechs)
            {
                if (t.IsResearched || !ai.HasTech(t.Id)) continue;
                bool av = tm.IsTechAvailable(t);
                if (av)
                {
                    if (!available || t.Tier > best.Tier || (t.Tier == best.Tier && UnityEngine.Random.value < 0.5f)) { best = t; available = true; }
                }
                else if (!available && (best == null || t.Cost < best.Cost)) best = t;
            }
            return best;
        }

        public bool CanStart(Leader a, SpyOp op, int arg, out string reason)
        {
            reason = null;
            if (a == null || a.Class != LeaderClass.Agent || a.Post != LeaderPost.Spy || a.TargetOwner <= 0) { reason = "Агент не внедрён в империю"; return false; }
            if (a.OpKind >= 0) { reason = "Агент уже выполняет операцию"; return false; }
            var ai = AIEmpireManager.For(a.TargetOwner);
            if (ai == null || ai.IsEliminated) { reason = "Империя пала"; return false; }
            if (a.Cover < SpyInfo.MinCover(op)) { reason = $"Нужна разведсеть {SpyInfo.MinCover(op):0}% (сейчас {a.Cover:0}%)"; return false; }
            var eco = EconomyManager.Instance;
            if (eco == null || eco.Influence < SpyInfo.Cost(op)) { reason = $"{CostCheck.Full(influence: SpyInfo.Cost(op))} (операция стоит {SpyInfo.Cost(op):0})"; return false; }
            if (op == SpyOp.Steal && StealTarget(ai, out _) == null) { reason = "У них нет технологий, которых нет у вас"; return false; }
            if (op == SpyOp.Discord)
            {
                var third = AIEmpireManager.For(arg);
                if (third == null || third.IsEliminated || arg == a.TargetOwner || !Contacts.PlayerMet(arg)) { reason = "Выберите, кого стравить с ними"; return false; }
            }
            return true;
        }

        // ==================== КОМАНДЫ ИГРОКА ====================

        public bool StartOp(Leader a, SpyOp op, int arg)
        {
            if (!CanStart(a, op, arg, out string reason))
            {
                NotificationCenter.Show("Операция невозможна", reason, NotificationCenter.Kind.Warning, 3f);
                return false;
            }
            var eco = EconomyManager.Instance;
            if (!eco.TrySpend(0f, 0f, 0f, SpyInfo.Cost(op))) return false;
            a.OpKind = (int)op;
            a.OpMonthsLeft = SpyInfo.Months(op);
            a.OpArg = arg;
            var ai = AIEmpireManager.For(a.TargetOwner);
            AddLog($"{a.Name} начал операцию «{SpyInfo.Name(op)}» против {ai?.AIName}.", 0);
            SFXManager.Play(StellarisClone.Core.Audio.Sfx.UiConfirm);
            Changed();
            return true;
        }

        public void CancelOp(Leader a)
        {
            if (a == null || a.OpKind < 0) return;
            a.OpKind = -1; a.OpMonthsLeft = 0; a.OpArg = -1;
            AddLog($"{a.Name} прервал операцию. Затраты не возвращаются.", 0);
            Changed();
        }

        // ==================== МЕСЯЦ ====================

        private void HandleDay(int day, int month, int year)
        {
            if (day != 1 || !UIManager.IsGameStarted) return;
            var lm = Lm;
            if (lm == null) return;
            bool changed = false;
            var before = new Dictionary<int, float>();
            foreach (var a in PlayerAgents())
                if (a.Post == LeaderPost.Spy && a.TargetOwner > 0) before[a.Id] = a.Cover;

            foreach (var a in new List<Leader>(PlayerAgents()))
            {
                if (a.Post != LeaderPost.Spy || a.TargetOwner <= 0) continue;
                var ai = AIEmpireManager.For(a.TargetOwner);
                if (ai == null || ai.IsEliminated)
                {
                    AddLog($"Империя, куда был внедрён {a.Name}, пала. Агент ждёт нового назначения.", 0);
                    lm.Unassign(a);
                    changed = true;
                    continue;
                }

                changed = true;
                if (a.OpKind >= 0 && --a.OpMonthsLeft <= 0) ResolveOp(a, ai);
            }

            GrowNetworks();
            SyncCover();
            if (_net.Count > 0) changed = true;

            foreach (var a in PlayerAgents())
            {
                if (a.Post != LeaderPost.Spy || a.TargetOwner <= 0 || !before.TryGetValue(a.Id, out float was)) continue;
                var ai = AIEmpireManager.For(a.TargetOwner);
                if (ai == null) continue;
                if (was < IntelCover && a.Cover >= IntelCover)
                    NotificationCenter.Show("Разведсеть раскинута",
                        $"Сеть {a.Name} в империи {ai.AIName} достигла {IntelCover:0}%: вся их территория и флоты теперь у вас на виду.", NotificationCenter.Kind.Success, 7f);
                else if (was < BlueprintCover && a.Cover >= BlueprintCover)
                    NotificationCenter.Show("Перехват чертежей",
                        $"Сеть {a.Name} достигла {BlueprintCover:0}%: технологии, известные {ai.AIName}, вы изучаете на {BlueprintBoost * 100f:0}% быстрее.", NotificationCenter.Kind.Success, 7f);
                else if (was < DeepIntelCover && a.Cover >= DeepIntelCover)
                    NotificationCenter.Show("Глубокое внедрение",
                        $"Сеть {a.Name} достигла {DeepIntelCover:0}%: досье на {ai.AIName} открыто, доступны все операции.", NotificationCenter.Kind.Success, 7f);
            }

            RunAIOps(year);
            if (changed) Changed();
        }

        // ==================== ОПЕРАЦИИ ИГРОКА ====================

        private void ResolveOp(Leader a, AIEmpireManager ai)
        {
            var op = (SpyOp)a.OpKind;
            int arg = a.OpArg;
            a.OpKind = -1; a.OpMonthsLeft = 0; a.OpArg = -1;

            float chance = SuccessChance(a, op, ai);
            bool ok = UnityEngine.Random.value < chance;
            float counter = TargetCounter(ai);
            bool noticed = UnityEngine.Random.value < NoticeChance(a, ai);
            var lm = Lm;

            if (ok)
            {
                string result = op switch
                {
                    SpyOp.Steal => ApplySteal(ai),
                    SpyOp.Sabotage => ApplySabotage(ai),
                    _ => ApplyDiscord(ai, arg)
                };
                if (result == null)
                {
                    AddLog($"{a.Name}: операция «{SpyInfo.Name(op)}» против {ai.AIName} не нашла цели — влияние потрачено впустую.", 2);
                    NotificationCenter.Show("Операция впустую", $"{SpyInfo.Name(op)}: цель исчезла к моменту удара", NotificationCenter.Kind.Warning, 5f);
                }
                else
                {
                    _s.Succeeded++;
                    HurtNetwork(ai.OwnerId, 15f, 1f);          // часть сети засвечена
                    lm?.GrantXp(a, 40f);
                    string tail = "";
                    if (noticed)
                    {
                        ai.AddMemory("espionage", -8f);
                        tail = " Но следы заметили: отношение к вам ухудшилось.";
                    }
                    AddLog($"{SpyInfo.Name(op)} против {ai.AIName}: {result}.{tail}", 1);
                    NotificationCenter.Show($"Операция удалась · {SpyInfo.Name(op)}", $"{result}.{tail}", NotificationCenter.Kind.Success, 8f);
                    SFXManager.Play(StellarisClone.Core.Audio.Sfx.NotifySuccess);
                }
            }
            else
            {
                _s.Failed++;
                if (UnityEngine.Random.value < CatchChance(a, ai))
                {
                    _s.AgentsLost++;
                    ai.AddMemory("espionage", -12f);
                    HurtNetwork(ai.OwnerId, 0f, 0.7f);         // контрразведка вскрыла часть сети
                    AddLog($"{a.Name} схвачен при операции «{SpyInfo.Name(op)}» в империи {ai.AIName}. Скандал: отношения ухудшились.", 2);
                    lm?.Retire(a, "Агент схвачен", $"{a.Name} попался контрразведке {ai.AIName}. Отношения с ними ухудшились.");
                    SFXManager.Play(StellarisClone.Core.Audio.Sfx.NotifyDanger);
                }
                else
                {
                    HurtNetwork(ai.OwnerId, 0f, 0.5f);         // агент «сгорел»: полсети потеряно
                    ai.AddMemory("espionage", -4f);
                    AddLog($"Операция «{SpyInfo.Name(op)}» против {ai.AIName} провалена. {a.Name} ушёл в тень, но половина разведсети потеряна.", 2);
                    NotificationCenter.Show("Операция провалена", $"{SpyInfo.Name(op)}: {a.Name} ушёл от ареста, половина разведсети потеряна", NotificationCenter.Kind.Warning, 7f);
                }
            }
            SyncCover();
            Changed();
        }

        /// <summary>Шанс, что успешную операцию заметят (−8 к отношению).</summary>
        public static float NoticeChance(Leader a, AIEmpireManager ai)
            => Mathf.Clamp01(0.15f + TargetCounter(ai) * 1.2f - (a.Has("agt_shadow") ? 0.15f : 0f));

        /// <summary>Шанс ареста агента при провале.</summary>
        public static float CatchChance(Leader a, AIEmpireManager ai)
            => Mathf.Clamp(0.45f + TargetCounter(ai) * 0.4f - (a.Has("agt_shadow") ? 0.20f : 0f) - a.Level * 0.03f, 0.10f, 0.85f);

        /// <summary>
        /// Подсказка операции до запуска: что именно получите при успехе и чем рискуете при провале — с цифрами.
        /// Точные запасы цели видны только с досье (сеть от 50%).
        /// </summary>
        public static string OpPreview(Leader a, SpyOp op, AIEmpireManager ai)
        {
            if (a == null || ai == null) return SpyInfo.Desc(op);
            const string M = "#8AA2A8", Good = "#5CE68A", Bad = "#FF8888";
            float p = SuccessChance(a, op, ai);
            var sb = new System.Text.StringBuilder();
            sb.Append($"<b>{SpyInfo.Name(op)}</b> против {ai.AIName}\n");
            sb.Append($"<color={M}>Цена:</color> {SpyInfo.Cost(op):0} влияния · {SpyInfo.Months(op)} мес. · успех <b>{p * 100f:0}%</b>\n\n");
            sb.Append($"<color={Good}>Если удастся:</color> ");
            switch (op)
            {
                case SpyOp.Steal:
                {
                    var tech = StealTarget(ai, out bool available);
                    sb.Append(tech == null ? "красть нечего — у них нет технологий, которых нет у вас."
                        : available ? $"технология «{tech.Name}» будет изучена сразу."
                        : $"половина исследования «{tech.Name}» ({tech.Cost * 0.5f:0} науки).");
                    break;
                }
                case SpyOp.Sabotage:
                    if (HasIntel(ai.OwnerId, DeepIntelCover))
                    {
                        float al = ai.GetStock("alloys"), en = ai.GetStock("energy");
                        sb.Append($"уничтожит ≈ {Mathf.Min(al, Mathf.Max(30f, al * 0.35f)):0} сплавов и ≈ {Mathf.Min(en, Mathf.Max(20f, en * 0.20f)):0} гелия-3 (по досье).");
                    }
                    else sb.Append("уничтожит 35% их сплавов (не меньше 30) и 20% гелия-3 (не меньше 20). Точные запасы видны в досье — от 50% сети.");
                    break;
                default:
                    sb.Append("две чужие империи поссорятся: −35 к отношениям между ними, война между ними вероятнее.");
                    break;
            }
            sb.Append($"\nСеть частично засветится (−15%). С шансом {NoticeChance(a, ai) * 100f:0}% следы заметят: −8 к отношению с вами.\n");
            sb.Append($"<color={Bad}>Если провалится:</color> с шансом {CatchChance(a, ai) * 100f:0}% агента арестуют (−12 к отношению, сеть −30%), " +
                      "иначе он уйдёт, но половина разведсети пропадёт.");
            return sb.ToString();
        }

        /// <summary>
        /// Для событий: усилить (delta &gt; 0) или ослабить разведсеть в империи owner — во всех её системах,
        /// где сеть уже есть; если сети нет и delta &gt; 0 — зацепка в столице.
        /// </summary>
        public static void AdjustNetwork(int owner, float delta)
        {
            var es = Instance;
            if (es == null || owner <= 0 || Mathf.Approximately(delta, 0f)) return;
            var touched = new List<int>();
            foreach (var kv in es._net)
            {
                var sys = EmpireStats.GetSystem(kv.Key);
                if (sys != null && sys.OwnerId == owner) touched.Add(kv.Key);
            }
            if (touched.Count == 0)
            {
                var ai = AIEmpireManager.For(owner);
                if (delta > 0f && ai != null && ai.CapitalSystemId >= 0) es._net[ai.CapitalSystemId] = Mathf.Min(100f, delta);
            }
            foreach (int id in touched)
            {
                float v = Mathf.Clamp(es._net[id] + delta, 0f, 100f);
                if (v <= 0f) es._net.Remove(id); else es._net[id] = v;
            }
            SyncCover();
            Changed();
        }

        private string ApplySteal(AIEmpireManager ai)
        {
            var tm = TechnologyManager.Instance;
            var tech = StealTarget(ai, out bool available);
            if (tm == null || tech == null) return null;
            if (available)
            {
                tm.GrantTech(tech);
                return $"похищена технология «{tech.Name}» — она уже изучена в вашей империи";
            }
            tm.AddTechProgress(tech, tech.Cost * 0.5f);
            return $"украдены чертежи «{tech.Name}»: половина исследования выполнена";
        }

        private string ApplySabotage(AIEmpireManager ai)
        {
            float alloys = ai.GetStock("alloys"), energy = ai.GetStock("energy");
            float lostA = Mathf.Min(alloys, Mathf.Max(30f, alloys * 0.35f));
            float lostE = Mathf.Min(energy, Mathf.Max(20f, energy * 0.20f));
            ai.SpendStock("alloys", lostA);
            ai.SpendStock("energy", lostE);
            return $"уничтожено {lostA:0} сплавов и {lostE:0} гелия-3";
        }

        private string ApplyDiscord(AIEmpireManager ai, int arg)
        {
            var third = AIEmpireManager.For(arg);
            if (third == null || third.IsEliminated) return null;
            AIRelations.AddGrudge(ai.OwnerId, arg, -35f);
            return $"{ai.AIName} и {third.AIName} поссорились — отношения между ними резко ухудшились";
        }

        // ==================== ШПИОНАЖ ИИ ПРОТИВ ВАС ====================

        private int NextInterval(AIEmpireManager ai)
        {
            float months = ai.Personality switch
            {
                AIPersonality.Trader => 9f,
                AIPersonality.Scientific => 12f,
                AIPersonality.Militarist => 14f,
                AIPersonality.Mystic => 14f,
                _ => 18f
            };
            int d = GameSession.Settings.Difficulty;
            months *= d == 0 ? 1.5f : d == 2 ? 0.8f : 1f;
            return Mathf.Max(6, Mathf.RoundToInt(months * UnityEngine.Random.Range(0.8f, 1.3f)));
        }

        private void RunAIOps(int year)
        {
            foreach (var ai in AIEmpireManager.Alive)
            {
                if (!Contacts.PlayerMet(ai.OwnerId)) continue;
                int idx = _s.AIOwners.IndexOf(ai.OwnerId);
                if (idx < 0)
                {
                    _s.AIOwners.Add(ai.OwnerId);
                    _s.AITimers.Add(UnityEngine.Random.Range(18, 30));
                    continue;
                }
                if (--_s.AITimers[idx] > 0) continue;
                _s.AITimers[idx] = NextInterval(ai);
                if (ai.GetStock("influence") < AIOpInfluence) continue;
                ai.SpendStock("influence", AIOpInfluence);
                RunAIOp(ai);
            }
        }

        private void RunAIOp(AIEmpireManager ai)
        {
            float counter = PlayerCounter();
            float chance = Mathf.Clamp(0.50f + AISpyPower(ai) - counter, 0.10f, 0.90f);
            bool success = UnityEngine.Random.value < chance;
            bool detected = UnityEngine.Random.value < Mathf.Clamp(counter * 1.6f + 0.10f, 0.10f, 0.85f);
            bool foiled = detected && UnityEngine.Random.value < 0.65f;

            // Что задумал ИИ: кража технологии (если есть что красть), диверсия или хищение запасов
            Technology theft = AIStealTarget(ai);
            int kind;
            float r = UnityEngine.Random.value;
            if (theft != null && r < 0.34f) kind = 0;
            else kind = r < 0.67f ? 1 : 2;
            string what = kind == 0 ? "кража технологий" : kind == 1 ? "диверсия на верфях" : "хищение запасов";

            var counterAgent = AgentOn(0);
            if (foiled)
            {
                _s.Foiled++;
                Lm?.GrantXp(counterAgent, 25f);
                string msg = $"Контрразведка сорвала операцию {ai.AIName}: {what}.";
                AddLog(msg, 3);
                NotificationCenter.Show("Шпионаж сорван", msg, NotificationCenter.Kind.Success, 7f);
                Changed();
                return;
            }
            if (!success)
            {
                if (detected)
                {
                    string msg = $"{ai.AIName} пытались провернуть: {what}, но агенты ничего не добились.";
                    AddLog(msg, 3);
                    NotificationCenter.Show("Попытка шпионажа", msg, NotificationCenter.Kind.Info, 6f);
                    Changed();
                }
                return;
            }

            _s.Breaches++;
            var eco = EconomyManager.Instance;
            string effect;
            switch (kind)
            {
                case 0:
                    ai.GrantTechFromSpy(theft);
                    effect = $"скопирована технология «{theft.Name}»";
                    break;
                case 1:
                {
                    float lost = eco != null ? Mathf.Min(eco.Alloys, Mathf.Max(20f, eco.Alloys * 0.25f)) : 0f;
                    if (eco != null) { eco.Alloys -= lost; eco.RaiseResourcesChanged(); }
                    effect = $"уничтожено {lost:0} сплавов";
                    break;
                }
                default:
                {
                    float e = eco != null ? Mathf.Min(eco.EnergyCredits, Mathf.Max(15f, eco.EnergyCredits * 0.08f)) : 0f;
                    float m = eco != null ? Mathf.Min(eco.Minerals, Mathf.Max(15f, eco.Minerals * 0.08f)) : 0f;
                    if (eco != null) { eco.EnergyCredits -= e; eco.Minerals -= m; eco.RaiseResourcesChanged(); }
                    ai.AddStock("energy", e);
                    ai.AddStock("minerals", m);
                    effect = $"похищено {e:0} гелия-3 и {m:0} титана";
                    break;
                }
            }

            if (detected)
            {
                string msg = $"{ai.AIName}: {what} удалась — {effect}.";
                AddLog(msg, 3);
                NotificationCenter.Show("Операция против вас раскрыта", msg, NotificationCenter.Kind.Danger, 9f);
            }
            else if (kind != 0)
            {
                string msg = $"Неизвестные агенты: {effect}. Контрразведка не смогла определить, кто за этим стоит.";
                AddLog(msg, 3);
                NotificationCenter.Show("Диверсия", msg, NotificationCenter.Kind.Warning, 8f);
            }
            Changed();
        }

        /// <summary>Что мог бы украсть у вас ИИ: изученное вами, чего нет у него, и он готов изучить.</summary>
        private static Technology AIStealTarget(AIEmpireManager ai)
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return null;
            Technology best = null;
            foreach (var t in tm.AllTechs)
            {
                if (!t.IsResearched || ai.HasTech(t.Id) || !ai.CanResearch(t)) continue;
                if (best == null || t.Tier > best.Tier) best = t;
            }
            return best;
        }

        // ==================== ЖУРНАЛ ====================

        private void AddLog(string text, int kind)
        {
            _s.Log.Insert(0, new EspLogEntry
            {
                Date = TimeManager.Instance != null ? TimeManager.Instance.GetFormattedDate() : "",
                Text = text,
                Kind = kind
            });
            if (_s.Log.Count > MaxLog) _s.Log.RemoveRange(MaxLog, _s.Log.Count - MaxLog);
        }

        // ==================== СОХРАНЕНИЕ ====================

        public EspionageSave CaptureState()
        {
            _s.NetSys = new List<int>(_net.Keys);
            _s.NetVal = new List<float>();
            foreach (int id in _s.NetSys) _s.NetVal.Add(_net[id]);
            return _s;
        }

        public void RestoreState(EspionageSave s)
        {
            _s = s ?? new EspionageSave();
            _s.Log ??= new List<EspLogEntry>();
            _s.AIOwners ??= new List<int>();
            _s.AITimers ??= new List<int>();
            while (_s.AITimers.Count < _s.AIOwners.Count) _s.AITimers.Add(12);
            _s.NetSys ??= new List<int>();
            _s.NetVal ??= new List<float>();
            _net.Clear();
            for (int i = 0; i < _s.NetSys.Count && i < _s.NetVal.Count; i++) _net[_s.NetSys[i]] = _s.NetVal[i];

            // Сохранения до разведсети: у агента было «прикрытие» на всю империю — раскладываем его по её системам
            foreach (var a in PlayerAgents())
            {
                if (a.Post != LeaderPost.Spy || a.TargetOwner <= 0) continue;
                if (a.TargetSystem < 0) a.TargetSystem = SuggestSystem(a.TargetOwner);
                if (_s.NetSys.Count > 0 || a.Cover <= 0f) continue;
                foreach (var sys in EmpireStats.Systems)
                    if (sys.OwnerId == a.TargetOwner) _net[sys.Id] = a.Cover;
            }
            SyncCover();
            OnChanged?.Invoke();
        }
    }
}
