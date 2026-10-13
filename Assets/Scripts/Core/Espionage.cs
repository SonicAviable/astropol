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
    }

    /// <summary>
    /// Шпионаж. Агент — четвёртый класс лидеров. Его посылают в контрразведку дома или внедряют в чужую империю.
    ///   • Внедрённость (прикрытие) растёт каждый месяц. С 20% агент даёт обзор всей территории цели и её флотов,
    ///     с 50% — полное досье (запасы, доходы, наука, мощь).
    ///   • Операции — «Кража технологии», «Диверсия», «Раздор»: стоят влияния, идут несколько месяцев, шанс успеха
    ///     зависит от агента, его уровня, черт, внедрённости, технологий и контрразведки цели. Провал — агент либо
    ///     схвачен (скандал, отношения падают), либо «сгорел» и теряет прикрытие.
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
        private const int MaxLog = 40;

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

        /// <summary>Есть ли у вас агент в этой империи с достаточным прикрытием.</summary>
        public static bool HasIntel(int owner, float minCover = IntelCover)
        {
            if (owner <= 0) return false;
            var a = AgentOn(owner);
            return a != null && a.Cover >= minCover;
        }

        /// <summary>Территория империй, где у вас внедрён агент (прикрытие ≥ 20%): обзор для карты.</summary>
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
            if (a.Cover < SpyInfo.MinCover(op)) { reason = $"Нужно прикрытие {SpyInfo.MinCover(op):0}% (сейчас {a.Cover:0}%)"; return false; }
            var eco = EconomyManager.Instance;
            if (eco == null || eco.Influence < SpyInfo.Cost(op)) { reason = $"Нужно {SpyInfo.Cost(op):0} влияния"; return false; }
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
            var tm = TechnologyManager.Instance;
            float speed = tm != null ? tm.Bonuses.CoverSpeed : 1f;
            bool changed = false;

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
                if (a.OpKind >= 0)
                {
                    if (--a.OpMonthsLeft <= 0) ResolveOp(a, ai);
                    continue;
                }

                float before = a.Cover;
                float gain = (3f + a.Level) * (a.Has("agt_infiltrator") ? 1.25f : 1f) * speed;
                a.Cover = Mathf.Min(100f, a.Cover + gain);
                if (before < IntelCover && a.Cover >= IntelCover)
                    NotificationCenter.Show("Агент внедрился",
                        $"{a.Name} закрепился в империи {ai.AIName}: вся их территория и флоты теперь у вас на виду.", NotificationCenter.Kind.Success, 7f);
                else if (before < DeepIntelCover && a.Cover >= DeepIntelCover)
                    NotificationCenter.Show("Глубокое внедрение",
                        $"{a.Name} получил доступ к сведениям высшего уровня: досье на {ai.AIName} открыто, доступны все операции.", NotificationCenter.Kind.Success, 7f);
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
            bool noticed = UnityEngine.Random.value < Mathf.Clamp01(0.15f + counter * 1.2f - (a.Has("agt_shadow") ? 0.15f : 0f));
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
                    a.Cover = Mathf.Max(10f, a.Cover - 15f);
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
                float catchChance = Mathf.Clamp(0.45f + counter * 0.4f - (a.Has("agt_shadow") ? 0.20f : 0f) - a.Level * 0.03f, 0.10f, 0.85f);
                if (UnityEngine.Random.value < catchChance)
                {
                    _s.AgentsLost++;
                    ai.AddMemory("espionage", -12f);
                    AddLog($"{a.Name} схвачен при операции «{SpyInfo.Name(op)}» в империи {ai.AIName}. Скандал: отношения ухудшились.", 2);
                    lm?.Retire(a, "Агент схвачен", $"{a.Name} попался контрразведке {ai.AIName}. Отношения с ними ухудшились.");
                    SFXManager.Play(StellarisClone.Core.Audio.Sfx.NotifyDanger);
                }
                else
                {
                    a.Cover = Mathf.Max(0f, a.Cover - 40f);
                    ai.AddMemory("espionage", -4f);
                    AddLog($"Операция «{SpyInfo.Name(op)}» против {ai.AIName} провалена. {a.Name} ушёл в тень, но прикрытие подорвано.", 2);
                    NotificationCenter.Show("Операция провалена", $"{SpyInfo.Name(op)}: {a.Name} ушёл от ареста, прикрытие подорвано", NotificationCenter.Kind.Warning, 7f);
                }
            }
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

        public EspionageSave CaptureState() => _s;

        public void RestoreState(EspionageSave s)
        {
            _s = s ?? new EspionageSave();
            _s.Log ??= new List<EspLogEntry>();
            _s.AIOwners ??= new List<int>();
            _s.AITimers ??= new List<int>();
            while (_s.AITimers.Count < _s.AIOwners.Count) _s.AITimers.Add(12);
            OnChanged?.Invoke();
        }
    }
}
