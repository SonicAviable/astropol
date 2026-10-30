using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using StellarisClone.Cards;

namespace StellarisClone.Core
{
    /// <summary>
    /// Советник ОРАКУЛ-7 по ходу партии. В отличие от разовых советов новичку (StarterGuide), следит за империей всю игру
    /// и подсказывает, когда что-то идёт не так или открылась возможность: простаивают корабли, гелий-3 уходит в минус,
    /// слот исследований пуст, враг в своей системе, колония на грани бунта, можно штурмовать.
    ///   • Совет — уведомление «Советник ОРАКУЛА-7»; если оно про место, клик ведёт к системе.
    ///   • У каждого правила своя перезарядка в игровых днях, а между любыми советами — пауза: советник не болтлив.
    ///   • Молчит во время обучения, на паузе и если выключен в настройках (Игра → Советник).
    /// Правила одинаковы для всех партий; состояние перезарядок не сохраняется — после загрузки советы начнутся заново.
    /// </summary>
    public class Advisor : MonoBehaviour
    {
        public static Advisor Instance { get; private set; }

        private const float CheckEvery = 3f;     // реальных секунд между проверками
        private const float MinGapReal = 45f;    // реальных секунд между любыми советами
        private const int MinGapDays = 20;       // игровых дней между любыми советами

        private sealed class Rule
        {
            public string Id;
            public int CooldownDays;
            public NotificationCenter.Kind Kind;
            /// <summary>Проверка: текст совета и система (−1 — без места) или null — совет не нужен.</summary>
            public Func<(string text, int system)?> Check;
        }

        private readonly List<Rule> _rules = new List<Rule>();
        private readonly Dictionary<string, int> _lastDay = new Dictionary<string, int>();
        /// <summary>Сколько дней корабль стоит без дела (Id флота → дней).</summary>
        private readonly Dictionary<int, int> _idleDays = new Dictionary<int, int>();
        private float _timer = 20f, _realGap;
        private int _lastAnyDay = int.MinValue / 2, _lastIdleCheckDay = -1;
        private int _cursor;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            BuildRules();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private static int Today => AssaultLink.Today;

        private void Update()
        {
            if (!UIManager.IsGameStarted || GameAudio.Suppressed) return;
            var time = TimeManager.Instance;
            if (time == null || time.CurrentSpeed <= 0) return;
            float dt = Time.unscaledDeltaTime;
            _realGap -= dt;
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = CheckEvery;

            TrackIdle();
            if (!GameSettings.Advisor) return;
            if (TutorialManager.Instance != null && TutorialManager.Instance.IsActive) return;
            if (_realGap > 0f || Today - _lastAnyDay < MinGapDays) return;

            // Правила по кругу: каждое получает шанс, никто не «застревает» первым
            for (int k = 0; k < _rules.Count; k++)
            {
                var r = _rules[(_cursor + k) % _rules.Count];
                if (_lastDay.TryGetValue(r.Id, out int last) && Today - last < r.CooldownDays) continue;
                (string text, int system)? res;
                try { res = r.Check(); }
                catch (Exception) { res = null; }
                if (res == null) continue;
                _lastDay[r.Id] = Today;
                _lastAnyDay = Today;
                _realGap = MinGapReal;
                _cursor = (_cursor + k + 1) % _rules.Count;
                NotificationCenter.Show(Loc.T("Советник ОРАКУЛ-7"), res.Value.text, r.Kind, 11f, res.Value.system);
                return;
            }
        }

        // ==================== ПРАВИЛА ====================

        private void BuildRules()
        {
            Add("constructor", 90, NotificationCenter.Kind.Info, IdleShip(FleetType.Constructor, 20,
                Loc.T("Строительный корабль «{0}» простаивает уже месяц. Форпосты расширяют границы — щелкните, чтобы найти его, и отправьте к соседней системе (ПКМ).")));
            Add("science", 90, NotificationCenter.Kind.Info, IdleScience);
            Add("energy", 120, NotificationCenter.Kind.Warning, EnergyDeficit);
            Add("research", 60, NotificationCenter.Kind.Info, ResearchIdle);
            Add("influence", 180, NotificationCenter.Kind.Info, InfluencePile);
            Add("enemy", 30, NotificationCenter.Kind.Danger, EnemyInTerritory);
            Add("unrest", 120, NotificationCenter.Kind.Warning, Unrest);
            Add("naval", 180, NotificationCenter.Kind.Warning, NavalOver);
            Add("army", 180, NotificationCenter.Kind.Warning, NoArmyAtWar);
            Add("assault", 45, NotificationCenter.Kind.Success, AssaultReady);
            Add("colony", 240, NotificationCenter.Kind.Info, ColonyChance);
        }

        private void Add(string id, int cooldown, NotificationCenter.Kind kind, Func<(string, int)?> check)
            => _rules.Add(new Rule { Id = id, CooldownDays = cooldown, Kind = kind, Check = check });

        /// <summary>Раз в игровой день — сколько дней каждый корабль игрока стоит без приказа.</summary>
        private void TrackIdle()
        {
            int today = Today;
            if (today == _lastIdleCheckDay) return;
            int passed = _lastIdleCheckDay < 0 ? 1 : Mathf.Clamp(today - _lastIdleCheckDay, 1, 30);
            _lastIdleCheckDay = today;
            var fm = FleetManager.Instance;
            if (fm == null) return;
            var seen = new HashSet<int>();
            foreach (var f in fm.AllFleets)
            {
                var d = f != null ? f.Data : null;
                if (d == null || d.Destroyed || d.OwnerId != 0 || d.Type == FleetType.Military) continue;
                seen.Add(d.Id);
                bool idle = d.State == FleetState.Orbiting && d.TargetSystemId < 0 && d.BuildTargetSystemId < 0 && d.SurveyTargetSystemId < 0 && !d.AutoExplore;
                _idleDays[d.Id] = idle ? (_idleDays.TryGetValue(d.Id, out int n) ? n : 0) + passed : 0;
            }
            var gone = new List<int>();
            foreach (var kv in _idleDays) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) _idleDays.Remove(id);
        }

        private FleetData FirstIdle(FleetType type, int minDays)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return null;
            foreach (var f in fm.AllFleets)
            {
                var d = f != null ? f.Data : null;
                if (d == null || d.Destroyed || d.OwnerId != 0 || d.Type != type) continue;
                if (_idleDays.TryGetValue(d.Id, out int n) && n >= minDays) return d;
            }
            return null;
        }

        private Func<(string, int)?> IdleShip(FleetType type, int days, string text) => () =>
        {
            var d = FirstIdle(type, days);
            if (d == null) return null;
            return (Loc.F(text, d.Name), d.CurrentSystemId);
        };

        private (string, int)? IdleScience()
        {
            var d = FirstIdle(FleetType.Science, 20);
            if (d == null) return null;
            // Изучать ещё есть что? Иначе простой — нормально
            bool left = false;
            foreach (var s in EmpireStats.Systems) if (s != null && !s.IsSurveyedBy(0)) { left = true; break; }
            if (!left) return null;
            return (Loc.F("Научный корабль «{0}» без дела. Включите авторазведку на панели флота — он сам найдёт неизученные системы.", d.Name), d.CurrentSystemId);
        }

        private static (string, int)? EnergyDeficit()
        {
            var eco = EconomyManager.Instance;
            if (eco == null) return null;
            float inc = eco.MonthlyEnergyIncome;
            if (inc >= 0f) return null;
            float months = eco.EnergyCredits / -inc;
            if (months > 12f) return null;
            return (Loc.F("Гелий-3 уходит в минус: {0:0.#} в месяц, запасов хватит примерно на {1:0} мес. Постройте генераторы в колониях или распустите лишние корабли — при банкротстве империя рушится.", inc, Mathf.Max(0f, months)), -1);
        }

        private static (string, int)? ResearchIdle()
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return null;
            foreach (var s in tm.Slots)
                if (s != null && s.CurrentTech == null && !s.IsPaused)
                    return (Loc.T("Один из слотов исследований пустует — наука копится впустую. Откройте технологии (T) и выберите проект."), -1);
            return null;
        }

        private static (string, int)? InfluencePile()
        {
            var eco = EconomyManager.Instance;
            if (eco == null || eco.Influence < 200f) return null;
            return (Loc.F("Влияния накопилось {0:0}. Его стоит пустить в дело: законы и решения (L), форпосты, договоры с соседями.", eco.Influence), -1);
        }

        private static bool Hostile(int owner)
        {
            if (owner == 0) return false;
            if (Threats.IsThreat(owner)) return true;
            var ai = AIEmpireManager.For(owner);
            return ai != null && ai.AtWar;
        }

        private static (string, int)? EnemyInTerritory()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return null;
            foreach (var f in fm.AllFleets)
            {
                var d = f != null ? f.Data : null;
                if (d == null || d.Destroyed || d.Type != FleetType.Military || !Hostile(d.OwnerId)) continue;
                if (d.State == FleetState.InHyperlane) continue;
                var sys = EmpireStats.GetSystem(d.CurrentSystemId);
                if (sys == null || sys.OwnerId != 0) continue;
                return (Loc.F("Враг в системе {0}! Соберите флот и встретьте его, пока не началась осада.", sys.Name), sys.Id);
            }
            return null;
        }

        private static (string, int)? Unrest()
        {
            foreach (var s in EmpireStats.Systems)
            {
                if (s == null || s.OwnerId != 0) continue;
                foreach (var p in s.Planets)
                    if (p.Population > 0 && p.Stability < 25f)
                        return (Loc.F("На планете {0} стабильность {1:0}% — грозит бунт. Помогут законы порядка, жильё и решения империи.", p.Name, p.Stability), s.Id);
            }
            return null;
        }

        private static (string, int)? NavalOver()
        {
            var eco = EconomyManager.Instance;
            if (eco == null || eco.NavalCapacity <= 0 || eco.NavalUsed <= eco.NavalCapacity) return null;
            return (Loc.F("Флот больше лимита ({0} из {1}): содержание кораблей дорожает. Новые колонии и технологии поднимают лимит.", eco.NavalUsed, eco.NavalCapacity), -1);
        }

        private static (string, int)? NoArmyAtWar()
        {
            bool war = false;
            foreach (var ai in AIEmpireManager.All) if (ai != null && ai.AtWar) { war = true; break; }
            var am = ArmyManager.Instance;
            if (!war || am == null) return null;
            if (am.AttackPool(0).Count >= 6) return null;
            return (Loc.T("Идёт война, а армии для штурмов почти нет. В Арсенале (J) поднимите военный бюджет — колонии врага берут только десантом."), -1);
        }

        private static (string, int)? AssaultReady()
        {
            foreach (var s in EmpireStats.Systems)
            {
                if (s == null || s.OwnerId == 0 || s.OwnerId < 0 || !AssaultLink.IsColony(s)) continue;
                if (AssaultLink.Blocker(0, s) != null) continue;
                return (Loc.F("Орбита над колонией {0} ваша — можно идти на штурм. Выберите систему и нажмите «ШТУРМ».", s.Name), s.Id);
            }
            return null;
        }

        private static (string, int)? ColonyChance()
        {
            var t = TimeManager.Instance;
            if (t == null || t.Year < 2208) return null;
            int target = -1;
            foreach (var s in EmpireStats.Systems)
            {
                if (s == null || s.OwnerId != 0 || !s.IsSurveyed) continue;
                foreach (var p in s.Planets)
                {
                    if (p.IsColonizing) return null;   // колония уже основывается — подождём
                    if (target < 0 && p.CanColonize) target = s.Id;
                }
            }
            if (target < 0) return null;
            var sys = EmpireStats.GetSystem(target);
            return (Loc.F("В системе {0} есть пригодная планета. Новая колония — это жители, ресурсы и голоса в совете.", sys != null ? sys.Name : ""), target);
        }
    }
}
