using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using StellarisClone.Core;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Тестовый карточный бой. В любом месте игры (и в главном меню):
    ///   F10 — штурм (вы атакуете), Shift+F10 — оборона (вы защищаете колонию),
    ///   Ctrl+F10 — 200 автобоёв без экрана, сводка баланса в консоль (только в редакторе);
    ///   Ctrl+Shift+F10 — то же для набора вариантов настроек укреплений и десанта (RunBalanceSweep).
    /// ВРЕМЕННО клавиша работает и в обычной сборке игры (<see cref="InBuild"/>) — для показа и проверки боя;
    /// перед релизом поставить InBuild = false. Тестовый бой не трогает партию: колоды случайные, итог никуда не пишется.
    /// Колоды и укрепления случайные; фракция игрока — из партии, если она начата, иначе случайная.
    /// Пункты меню «Astropolity → Тестовый бой» (Editor/CardBattleTestMenu.cs) запускают то же и из режима правки.
    /// </summary>
    public sealed class BattleSandbox : MonoBehaviour
    {
        private static readonly string[] Factions = { "astrea", "xarn", "aquila", "iridia", "terraan", "nol" };
        private static readonly string[] Common = { "U1", "U1", "U1", "U2", "U3", "U4", "U5", "U6", "U7", "U8", "U9", "U10", "U11", "U12", "O1", "O2", "O3", "O4", "O5", "O6", "O7", "O9" };

        /// <summary>ВРЕМЕННО: F10 открывает тестовый бой и в сборке игры. Перед релизом — false.</summary>
        public static readonly bool InBuild = true;

        /// <summary>Отложенный запуск после входа в Play Mode из меню редактора: 1 — штурм, 2 — оборона.</summary>
        public const string PendingKey = "Astropolity.TestBattle";

        private static BattleSandbox s_instance;
        private float _treasury = 500f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (s_instance != null) return;
            if (!Application.isEditor && !InBuild) return;
            var go = new GameObject("[Test] CardBattleSandbox");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<BattleSandbox>();
#if UNITY_EDITOR
            int pending = UnityEditor.SessionState.GetInt(PendingKey, 0);
            UnityEditor.SessionState.EraseInt(PendingKey);
            if (pending > 0) s_instance.StartCoroutine(s_instance.Delayed(pending == 2));
#endif
        }

        private IEnumerator Delayed(bool defend)
        {
            // Дать игре собрать интерфейс и меню
            float end = Time.realtimeSinceStartup + 1.0f;
            while (Time.realtimeSinceStartup < end) yield return null;
            StartTest(defend);
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F10)) return;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ctrl) { if (Application.isEditor) { if (shift) RunBalanceSweep(200); else RunAutoBatch(200); } }
            else StartTest(shift);
        }

        /// <summary>Открыть экран боя со случайной тестовой расстановкой.</summary>
        public static void StartTest(bool playerDefends)
        {
            if (s_instance == null) Boot();
            if (BattleScreen.IsOpen) { Debug.Log("[Тестовый бой] Бой уже открыт"); return; }
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            BattleScreen.Ensure();
            var setup = Build(playerDefends, Random.Range(1, 100000), out int fort);
            Debug.Log($"[Тестовый бой] {(playerDefends ? "оборона" : "штурм")}: {setup.Attacker.Faction} → {setup.Defender.Faction}, " +
                      $"местность {setup.Terrain}, укрепления {fort}, карт {setup.AttackerCards.Count} против {setup.DefenderCards.Count}");
            AssaultLink.BattleRunning = true;
            BattleScreen.Instance.Open(setup, st =>
            {
                AssaultLink.BattleRunning = false;
                var r = st?.Result;
                if (r == null) { Debug.Log("[Тестовый бой] закрыт без итога"); return; }
                Debug.Log($"[Тестовый бой] {(r.AttackerWon ? "атакующий победил" : "защитник устоял")}{(r.Breach ? " (прорыв)" : "")}{(r.Retreated ? " (отступление)" : "")}, " +
                          $"ходов {r.Turns}, потери: атака {r.LostCount[0]}, оборона {r.LostCount[1]}, событий {st.Events.Count}");
            });
        }

        /// <summary>Пачка автобоёв без экрана: проверка баланса после правок карт.</summary>
        public static void RunAutoBatch(int n)
        {
            int win = 0, breach = 0, retreat = 0, turns = 0;
            var byFort = new int[4]; var winsByFort = new int[4];
            for (int i = 0; i < n; i++)
            {
                var s = Build(false, i * 7919 + 13, out int fort);
                s.Attacker.IsPlayer = false;
                var r = BattleAI.AutoResolve(s, out _);
                byFort[fort]++;
                if (r.AttackerWon) { win++; winsByFort[fort]++; }
                if (r.Breach) breach++;
                if (r.Retreated) retreat++;
                turns += r.Turns;
            }
            var sb = new System.Text.StringBuilder();
            sb.Append($"[Автобой ×{n}] атакующий побеждает {win * 100f / n:0}%, прорывов {breach}, отступлений {retreat}, в среднем ходов {turns / (float)n:0.0}\n");
            for (int f = 0; f < 4; f++)
                if (byFort[f] > 0) sb.Append($"  укрепления {f}: {winsByFort[f] * 100f / byFort[f]:0}% побед атаки из {byFort[f]}\n");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Подбор баланса: те же 200 расстановок (одинаковые сиды) для каждого варианта настроек
        /// (BattleState.BarrageBase, BunkerCover, BunkerCoverTroopsOnly, TurretPowerDelta, AttackerBonusPoints).
        /// Таблица — в консоль: процент побед атаки всего и по уровню укреплений. Настройки после прогона возвращаются.
        /// </summary>
        public static void RunBalanceSweep(int n)
        {
            int b0 = BattleState.BarrageBase, c0 = BattleState.BunkerCover, t0 = BattleState.TurretPowerDelta, p0 = BattleState.AttackerBonusPoints;
            bool o0 = BattleState.BunkerCoverTroopsOnly;
            var variants = new (string name, int barrage, int cover, bool troops, int turret, int points)[]
            {
                ("сейчас", b0, c0, o0, t0, p0),
                ("артподготовка 3", 3, c0, o0, t0, p0),
                ("артподготовка 4", 4, c0, o0, t0, p0),
                ("бункер укрывает только пехоту", b0, c0, true, t0, p0),
                ("бункер не укрывает других", b0, 0, o0, t0, p0),
                ("турели сила 1", b0, c0, o0, -1, p0),
                ("десант +1 очко", b0, c0, o0, t0, 1),
                ("арт 3 + бункер пехоте", 3, c0, true, t0, p0),
                ("арт 3 + турели 1", 3, c0, o0, -1, p0),
                ("арт 3 + бункер пехоте + турели 1", 3, c0, true, -1, p0),
            };
            var sb = new System.Text.StringBuilder();
            sb.Append($"[Подбор баланса ×{n}] вариант: всего | укр.0 | укр.1 | укр.2 | укр.3 | ходов\n");
            try
            {
                foreach (var v in variants)
                {
                    BattleState.BarrageBase = v.barrage; BattleState.BunkerCover = v.cover; BattleState.BunkerCoverTroopsOnly = v.troops;
                    BattleState.TurretPowerDelta = v.turret; BattleState.AttackerBonusPoints = v.points;
                    int win = 0, turns = 0;
                    var byFort = new int[4]; var winsByFort = new int[4];
                    for (int i = 0; i < n; i++)
                    {
                        var st = Build(false, i * 7919 + 13, out int fort);
                        st.Attacker.IsPlayer = false;
                        var r = BattleAI.AutoResolve(st, out _);
                        byFort[fort]++;
                        if (r.AttackerWon) { win++; winsByFort[fort]++; }
                        turns += r.Turns;
                    }
                    sb.Append($"  {v.name}: {win * 100f / n:0}%");
                    for (int f = 0; f < 4; f++) sb.Append(byFort[f] > 0 ? $" | {winsByFort[f] * 100f / byFort[f]:0}%" : " | —");
                    sb.Append($" | {turns / (float)n:0.0}\n");
                }
            }
            finally
            {
                BattleState.BarrageBase = b0; BattleState.BunkerCover = c0; BattleState.BunkerCoverTroopsOnly = o0;
                BattleState.TurretPowerDelta = t0; BattleState.AttackerBonusPoints = p0;
            }
            Debug.Log(sb.ToString());
        }

        private static BCard Card(string id, ref long inst, System.Random rnd)
        {
            var d = CardCatalog.Get(id);
            int stars = rnd.NextDouble() < 0.25 ? rnd.Next(1, 4) : 0;
            return new BCard(d, inst++, stars);
        }

        /// <summary>Случайная расстановка: колода десанта как у флота на 2000 мощи, оборона колонии с укреплениями 0–3.</summary>
        private static BattleSetup Build(bool playerDefends, int seed, out int fort)
        {
            var rnd = new System.Random(seed);
            string mine = UnityEngine.Application.isPlaying && FactionTraits.Key(0) != null && System.Array.IndexOf(Factions, FactionTraits.Key(0)) >= 0
                ? FactionTraits.Key(0) : Factions[rnd.Next(Factions.Length)];
            string other = Factions[rnd.Next(Factions.Length)];
            string fa = playerDefends ? other : mine, fd = playerDefends ? mine : other;

            var s = new BattleSetup { Seed = seed, AiSkill = 2, Terrain = (Terrain)rnd.Next(4) };
            var mids = new[] { SectorType.Factory, SectorType.Mine, SectorType.City, SectorType.Port };
            s.Sectors = new[] { SectorType.Power, mids[rnd.Next(mids.Length)], SectorType.HQ };
            s.PlanetName = "Тест-" + seed % 1000;
            s.Attacker.Faction = fa; s.Defender.Faction = fd;
            s.Attacker.Name = fa; s.Defender.Name = fd;
            s.Attacker.IsPlayer = !playerDefends; s.Defender.IsPlayer = playerDefends;
            s.Attacker.FleetPower = 2000f;
            s.Attacker.PointsPerTurn = 1;   // как в кампании (AssaultLink): господство флота на орбите
            s.Defender.Stability = 40 + rnd.Next(40);
            var me = playerDefends ? s.Defender : s.Attacker;
            me.Treasury = () => s_instance != null ? s_instance._treasury : 500f;
            me.SpendTreasury = v => { if (s_instance != null) s_instance._treasury -= v; };
            if (s_instance != null) s_instance._treasury = 500f;

            long inst = 1;
            var ad = new List<string>();
            for (int k = 0; k < 13; k++) ad.Add(Common[rnd.Next(Common.Length)]);
            var fac = new List<string>();
            foreach (var d in CardCatalog.ForFaction(fa)) if (d.Faction == fa) fac.Add(d.Id);
            for (int k = 0; k < 3 && fac.Count > 0; k++) ad.Add(fac[rnd.Next(fac.Count)]);
            foreach (var id in ad) s.AttackerCards.Add(Card(id, ref inst, rnd));

            fort = rnd.Next(4);
            var dd = new List<string> { "D2", "D2", "D2", "U1", "U3", "U10", "O1", "O5", "D1", "D1", "D1", "D1", "D5" };
            for (int k = 0; k < fort + (fort > 0 ? 1 : 0); k++) dd.Add(k % 2 == 0 ? "D3" : "D4");
            if (fort >= 1) dd.Add("D6");
            var facD = new List<string>();
            foreach (var d in CardCatalog.ForFaction(fd)) if (d.Faction == fd) facD.Add(d.Id);
            for (int k = 0; k < 2 && facD.Count > 0; k++) dd.Add(facD[rnd.Next(facD.Count)]);
            foreach (var id in dd) s.DefenderCards.Add(Card(id, ref inst, rnd));

            s.AttackerHero = new BCard(CardCatalog.HeroAttack);
            s.DefenderHero = new BCard(CardCatalog.HeroDefense);
            return s;
        }
    }
}
