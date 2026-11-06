using System;
using System.Collections.Generic;
using StellarisClone.Core;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Дополнения к правилам штурма:
    /// • Доктрины фракций — 2+ фракционные карты в одном секторе дают бонус перед перестрелкой;
    /// • Приказ героя — раз за бой герой на столе применяет свою способность (бесплатно);
    /// • События боя — на 5-м ходу что-то меняет расклад (как погода на 3-м);
    /// • Прогноз хода — копия стола прогоняется несколько раз с ответом ИИ (только открытая информация).
    /// </summary>
    public sealed partial class BattleState
    {
        public const int EventTurn = 5;

        /// <summary>Событие боя (для экрана): открывается на <see cref="EventTurn"/>-м ходу.</summary>
        public string EventId, EventName, EventText;

        // ==================== ДОКТРИНЫ ФРАКЦИЙ ====================

        public static string DoctrineName(string faction)
        {
            switch (faction)
            {
                case "astrea": return Loc.T("Братство");
                case "xarn": return Loc.T("Железная дисциплина");
                case "aquila": return Loc.T("Выгодный контракт");
                case "iridia": return Loc.T("Единое сознание");
                case "terraan": return Loc.T("Симбиоз");
                case "nol": return Loc.T("Сеть роя");
                default: return null;
            }
        }

        public static string DoctrineText(string faction)
        {
            switch (faction)
            {
                case "astrea": return Loc.T("2+ карты Республики в секторе: все ваши в секторе укрытие 2, карты Республики +1 к силе");
                case "xarn": return Loc.T("2+ карты Доминиона в секторе: они +2 к силе");
                case "aquila": return Loc.T("2+ карты Синдиката в секторе: они +2 к силе и +1 очко приказов в следующем ходу");
                case "iridia": return Loc.T("2+ карты Оракула в секторе: враги сектора −1 к силе");
                case "terraan": return Loc.T("2+ карты Конклава в секторе: все ваши в секторе +2 брони, карты Конклава +1 к силе");
                case "nol": return Loc.T("2+ дрона в секторе: каждый дрон +1 к силе");
                default: return null;
            }
        }

        /// <summary>Сколько карт нужно для доктрины фракции в секторе (для подсказок на экране).</summary>
        public int DoctrineCount(int side, int sector)
        {
            string f = Sides[side].Faction;
            int n = 0;
            foreach (var u in UnitsOf(side, sector))
            {
                if (!u.Revealed || u.IsStructure) continue;
                if (f == "nol" ? u.Has(Kw.Drone) : u.Def.Faction == f) n++;
            }
            return n;
        }

        public int DoctrineNeed(int side) => 2;

        /// <summary>Переключатели новых правил (песочница баланса).</summary>
        public static bool EnableDoctrines = true, EnableHeroPowers = true;

        private void ApplyDoctrines()
        {
            foreach (var sec in Sectors) foreach (var u in sec.Units) u.Doctrine = 0;
            if (!EnableDoctrines) return;
            for (int s = 0; s < 2; s++)
            {
                string f = Sides[s].Faction;
                if (DoctrineName(f) == null) continue;
                bool pointGiven = false;
                for (int i = 0; i < 3; i++)
                {
                    int n = DoctrineCount(s, i);
                    if (n < DoctrineNeed(s)) continue;
                    Log(EvType.Synergy, s, i, amount: n, text: f);
                    switch (f)
                    {
                        case "astrea":
                            foreach (var a in UnitsOf(s, i)) if (a.Revealed) { a.CoverLeft += 2; if (a.Def.Faction == f) DoctrineBuff(a, 1); }
                            break;
                        case "xarn":
                            foreach (var a in UnitsOf(s, i)) if (a.Revealed && a.Def.Faction == f) DoctrineBuff(a, 2);
                            break;
                        case "aquila":
                            if (!pointGiven) { Sides[s].BonusNext += 1; pointGiven = true; }
                            foreach (var a in UnitsOf(s, i)) if (a.Revealed && a.Def.Faction == f) DoctrineBuff(a, 2);
                            break;
                        case "iridia":
                            foreach (var e in Enemies(s, i)) if (!e.IsStructure) DoctrineBuff(e, -1);
                            break;
                        case "terraan":
                            foreach (var a in UnitsOf(s, i)) if (a.Revealed) { Heal(a, 2); if (a.Def.Faction == f) DoctrineBuff(a, 1); }
                            break;
                        case "nol":
                            foreach (var a in UnitsOf(s, i)) if (a.Revealed && a.Has(Kw.Drone)) DoctrineBuff(a, 1);
                            break;
                    }
                }
            }
        }

        private void DoctrineBuff(BUnit u, int amount)
        {
            if (u == null || u.Dead || amount == 0) return;
            u.Doctrine += amount;
            Log(EvType.Buff, u.Side, u.Sector, u.Uid, amount: amount, text: "doctrine");
        }

        // ==================== ПРИКАЗ ГЕРОЯ ====================

        public BUnit HeroOnBoard(int side)
        {
            foreach (var sec in Sectors)
                foreach (var u in sec.Units)
                    if (u.Side == side && u.Alive && u.Revealed && u.Def.Kind == CardKind.Hero) return u;
            return null;
        }

        public static string HeroPowerName(CardDef hero)
        {
            if (hero == null) return null;
            return hero.Id == "H2" ? Loc.T("Держать строй!") : Loc.T("В атаку!");
        }

        public static string HeroPowerText(CardDef hero)
        {
            if (hero == null) return null;
            return hero.Id == "H2"
                ? Loc.T("Раз за бой: союзники в секторе героя получают укрытие 3 и +2 брони на этот ход")
                : Loc.T("Раз за бой: союзники в секторе героя +2 к силе на этот ход, герой бьёт первым");
        }

        /// <summary>Почему нельзя отдать приказ героя (null — можно).</summary>
        public string CanHeroPower(int side)
        {
            if (Over) return Loc.T("Бой окончен");
            var sd = Sides[side];
            if (!EnableHeroPowers) return Loc.T("Приказы героев отключены");
            if (sd.HeroPowerUsed) return Loc.T("Приказ героя уже отдан в этом бою");
            if (HeroOnBoard(side) == null) return Loc.T("Герой ещё не на столе");
            return null;
        }

        /// <summary>Отдать (или отменить) приказ героя: сработает при подведении хода, после раскрытия карт.</summary>
        public bool ToggleHeroPower(int side)
        {
            var sd = Sides[side];
            if (sd.HeroPowerQueued) { sd.HeroPowerQueued = false; return true; }
            if (CanHeroPower(side) != null) return false;
            sd.HeroPowerQueued = true;
            return true;
        }

        private void ApplyHeroPowers()
        {
            for (int s = 0; s < 2; s++)
            {
                var sd = Sides[s];
                if (!sd.HeroPowerQueued) continue;
                sd.HeroPowerQueued = false;
                var hero = HeroOnBoard(s);
                if (hero == null || sd.HeroPowerUsed) continue;
                sd.HeroPowerUsed = true;
                Log(EvType.HeroPower, s, hero.Sector, hero.Uid, text: hero.Def.Id);
                if (hero.Def.Id == "H2")
                {
                    foreach (var a in UnitsOf(s, hero.Sector)) { a.CoverLeft += 3; Heal(a, 2); }
                }
                else
                {
                    foreach (var a in UnitsOf(s, hero.Sector)) if (a.Revealed) Buff(a, 2, false, "hero");
                    var t = StrongestEnemy(s, hero.Sector);
                    if (t != null) Damage(hero, t, Power(hero));
                }
            }
        }

        // ==================== СОБЫТИЯ БОЯ ====================

        private void RollBattleEvent()
        {
            var ids = new List<string> { "debris", "supply", "jam" };
            if (Sides[Attacker].FleetPower > 0f) ids.Add("orbit");
            bool militiaOnBoard = false;
            for (int i = 0; i < 3 && !militiaOnBoard; i++) foreach (var u in UnitsOf(Defender, i)) if (u.Has(Kw.Militia)) { militiaOnBoard = true; break; }
            if (militiaOnBoard && Sides[Defender].Stability < 45f) ids.Add("riot");
            if (Sides[Defender].Stability >= 60f) ids.Add("uprising");

            EventId = ids[Rng.Next(ids.Count)];
            int sec = Rng.Next(3);
            switch (EventId)
            {
                case "debris":
                    EventName = Loc.T("Обломки с орбиты");
                    EventText = Loc.T("В один сектор падают обломки кораблей: по 2 урона случайному юниту каждой стороны");
                    break;
                case "supply":
                    EventName = Loc.T("Сброс снабжения");
                    EventText = Loc.T("Обе стороны берут по карте");
                    break;
                case "jam":
                    EventName = Loc.T("Глушение связи");
                    EventText = Loc.T("В этом ходу у обеих сторон на 1 очко приказов меньше");
                    break;
                case "orbit":
                    EventName = Loc.T("Подкрепление с орбиты");
                    EventText = Loc.T("Флот десанта сбрасывает отряд морской пехоты в самый тяжёлый сектор");
                    break;
                case "riot":
                    EventName = Loc.T("Мятеж ополчения");
                    EventText = Loc.T("Недовольное ополчение колонии переходит на сторону десанта");
                    break;
                default:
                    EventName = Loc.T("Народное ополчение");
                    EventText = Loc.T("Жители верной колонии берут оружие: ополчение в Штабе");
                    break;
            }
            Log(EvType.BattleEvent, -1, sec, text: EventId);

            switch (EventId)
            {
                case "debris":
                    for (int s = 0; s < 2; s++)
                    {
                        var l = new List<BUnit>();
                        foreach (var u in UnitsOf(s, sec)) if (u.Revealed && !u.Has(Kw.Shield)) l.Add(u);
                        if (l.Count > 0) Damage(null, l[Rng.Next(l.Count)], 2, true);
                    }
                    break;
                case "supply":
                    DrawCards(Attacker, 1);
                    DrawCards(Defender, 1);
                    break;
                case "jam":
                    Sides[Attacker].Points = Math.Max(0, Sides[Attacker].Points - 1);
                    Sides[Defender].Points = Math.Max(0, Sides[Defender].Points - 1);
                    break;
                case "orbit":
                {
                    int worst = 0, gap = int.MinValue;
                    for (int i = 0; i < 3; i++)
                    {
                        int g = SectorPower(Defender, i) - SectorPower(Attacker, i);
                        if (g > gap) { gap = g; worst = i; }
                    }
                    Spawn(CardCatalog.Crew, Attacker, worst);
                    break;
                }
                case "riot":
                    for (int i = 0; i < 3; i++)
                        foreach (var u in UnitsOf(Defender, i))
                            if (u.Has(Kw.Militia)) { Convert(u, Attacker); return; }
                    break;
                default:
                {
                    int hq = SectorIndex(SectorType.HQ);
                    Spawn(CardCatalog.Militia, Defender, hq >= 0 ? hq : sec);
                    break;
                }
            }
        }

        // ==================== ПРОГНОЗ ====================

        public sealed class TurnForecast
        {
            public int Samples;
            /// <summary>Доля прогонов, где сектор после хода у стороны прогноза.</summary>
            public readonly float[] Hold = new float[3];
            /// <summary>Сколько в среднем погибнет своих / вражеских юнитов в секторе.</summary>
            public readonly float[] MyLoss = new float[3], EnemyLoss = new float[3];
            /// <summary>Бой закончится победой / поражением прямо в этот ход.</summary>
            public float WinNow, LoseNow;
        }

        /// <summary>
        /// Прогноз хода для стороны <paramref name="side"/> с её уже выложенными картами: соперник отвечает ИИ
        /// (его карты этого хода неизвестны), случайность — разная в каждом прогоне. Сам стол не меняется.
        /// </summary>
        public TurnForecast Forecast(int side, int samples = 10, int skill = 2)
        {
            var f = new TurnForecast { Samples = samples };
            if (Over || samples <= 0) return f;
            int enemy = Enemy(side);
            var aliveBefore = new HashSet<int>();
            foreach (var sec in Sectors) foreach (var u in sec.Units) if (u.Alive && !u.IsTrap) aliveBefore.Add(u.Uid);

            for (int k = 0; k < samples; k++)
            {
                var c = new BattleState(this, Setup.Seed * 31 + Turn * 977 + k * 7919);
                if (c.Sides[enemy].Pending.Count == 0) BattleAI.PlanTurn(c, enemy, skill);
                if (!c.Over) c.Resolve();
                for (int i = 0; i < 3; i++)
                {
                    if (c.Sectors[i].Control == side) f.Hold[i] += 1f;
                    foreach (var u in c.Sectors[i].Units)
                    {
                        if (!u.Dead || !aliveBefore.Contains(u.Uid)) continue;
                        if (u.Side == side) f.MyLoss[i] += 1f; else f.EnemyLoss[i] += 1f;
                    }
                }
                if (c.Over) { if (c.Winner == side) f.WinNow += 1f; else f.LoseNow += 1f; }
            }
            float inv = 1f / samples;
            for (int i = 0; i < 3; i++) { f.Hold[i] *= inv; f.MyLoss[i] *= inv; f.EnemyLoss[i] *= inv; }
            f.WinNow *= inv; f.LoseNow *= inv;
            return f;
        }

        /// <summary>Копия стола для прогноза: свои стороны, юниты и генератор случайных чисел; казна недоступна.</summary>
        private BattleState(BattleState src, int seed)
        {
            Setup = src.Setup;
            Rng = new Random(seed);
            for (int s = 0; s < 2; s++) Sides[s] = CloneSide(src.Sides[s]);
            var map = new Dictionary<BUnit, BUnit>();
            for (int i = 0; i < 3; i++)
            {
                var o = src.Sectors[i];
                var n = new BSector { Type = o.Type, Control = o.Control, Smoke = o.Smoke, Ruined = o.Ruined };
                foreach (var u in o.Units)
                {
                    var cu = new BUnit
                    {
                        Uid = u.Uid, Card = u.Card, Side = u.Side, Sector = u.Sector,
                        BasePower = u.BasePower, Armor = u.Armor, MaxArmor = u.MaxArmor,
                        PowerBonus = u.PowerBonus, TempPower = u.TempPower, CoverLeft = u.CoverLeft, Doctrine = u.Doctrine,
                        Suppressed = u.Suppressed, Disabled = u.Disabled, Immobile = u.Immobile,
                        Dead = u.Dead, Revealed = u.Revealed, Hidden = u.Hidden
                    };
                    map[u] = cu;
                    n.Units.Add(cu);
                }
                Sectors[i] = n;
            }
            foreach (var u in src._everOnBoard) if (map.TryGetValue(u, out var cu)) _everOnBoard.Add(cu);
            Turn = src.Turn;
            Over = src.Over;
            Winner = src.Winner;
            WeatherActive = src.WeatherActive;
            BreachWarned = src.BreachWarned;
            WeatherName = src.WeatherName;
            WeatherText = src.WeatherText;
            EventId = src.EventId; EventName = src.EventName; EventText = src.EventText;
            _uid = src._uid;
            Result = new BattleResult();
            IsForecast = true;
        }

        /// <summary>Это копия для прогноза (экран не показывает её события).</summary>
        public bool IsForecast { get; private set; }

        private static BSide CloneSide(BSide o)
        {
            var n = new BSide
            {
                Owner = o.Owner, Faction = o.Faction, Name = o.Name, IsPlayer = o.IsPlayer,
                Points = o.Points, BonusNext = o.BonusNext, PointsPerTurn = o.PointsPerTurn, FleetPower = o.FleetPower,
                OrderCostDiscount = o.OrderCostDiscount, Stability = o.Stability, SeesEnemyHand = o.SeesEnemyHand,
                Mulliganed = o.Mulliganed, HeroPowerUsed = o.HeroPowerUsed, HeroPowerQueued = o.HeroPowerQueued,
                Treasury = null, SpendTreasury = null
            };
            n.Deck.AddRange(o.Deck);
            n.Hand.AddRange(o.Hand);
            n.Spent.AddRange(o.Spent);
            n.Pending.AddRange(o.Pending);
            return n;
        }
    }
}
