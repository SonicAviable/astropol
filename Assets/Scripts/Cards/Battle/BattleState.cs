using System;
using System.Collections.Generic;
using StellarisClone.Core;

namespace StellarisClone.Cards
{
    /// <summary>Сектор стола — один из районов колонии.</summary>
    public enum SectorType { Power, Factory, HQ, Mine, City, Port }

    /// <summary>Местность планеты: задаёт погоду 3-го хода и фон стола.</summary>
    public enum Terrain { Continental, Desert, Molten, Barren }

    /// <summary>Карта в бою: вид, связь с экземпляром резерва (−1 — временная: ополчение, экипаж, герой) и ветеранство.</summary>
    public sealed class BCard
    {
        public CardDef Def;
        public long InstId = -1;
        public int Stars;
        /// <summary>Карта в бою появилась ради этого боя (ополчение, экипаж, герой) — в резерв не возвращается.</summary>
        public bool Temporary => InstId < 0;
        public BCard(CardDef def, long inst = -1, int stars = 0) { Def = def; InstId = inst; Stars = stars; }
    }

    /// <summary>Юнит на столе (или скрытая ловушка).</summary>
    public sealed class BUnit
    {
        public int Uid;
        public BCard Card;
        public int Side, Sector;
        public int BasePower, Armor, MaxArmor;
        /// <summary>Бонус к силе до конца боя и только на этот ход.</summary>
        public int PowerBonus, TempPower;
        public int CoverLeft;
        public int Suppressed, Disabled, Immobile;
        public bool Dead, Revealed, Hidden;
        public CardDef Def => Card.Def;
        public bool Has(Kw k) => Card.Def.Has(k);
        public bool IsStructure => Card.Def.Kind == CardKind.Structure;
        public bool IsTrap => Card.Def.Kind == CardKind.Trap;
        public bool Alive => !Dead;
        public float HpFrac => MaxArmor <= 0 ? 0f : Math.Max(0f, Armor / (float)MaxArmor);
    }

    public sealed class BSector
    {
        public SectorType Type;
        public readonly List<BUnit> Units = new List<BUnit>();
        /// <summary>Кто держит сектор: 0 — атакующий, 1 — защитник. В начале боя вся планета у защитника.</summary>
        public int Control = 1;
        public bool Smoke;
        /// <summary>По сектору бил орбитальный удар — район колонии разрушен после боя.</summary>
        public bool Ruined;
    }

    public sealed class BSide
    {
        public int Owner = -1;
        public string Faction, Name;
        public bool IsPlayer;
        public readonly List<BCard> Deck = new List<BCard>(), Hand = new List<BCard>(), Spent = new List<BCard>();
        public int Points, BonusNext;
        /// <summary>Очки приказов сверх номера хода каждый ход (атакующему — господство флота на орбите).</summary>
        public int PointsPerTurn;
        /// <summary>Сила обстрела с орбиты (мощь флота): допускает «Орбитальный удар».</summary>
        public float FleetPower;
        /// <summary>Карты, выложенные рубашкой вверх в этом ходу (раскроются при подведении хода).</summary>
        public readonly List<(BCard card, int sector)> Pending = new List<(BCard, int)>();
        public int OrderCostDiscount;
        /// <summary>Стабильность планеты защитника (ополчение может перейти на сторону атакующего).</summary>
        public float Stability = 60f;
        /// <summary>Гелий-3 казны (наёмники Аквилы покупаются в бою).</summary>
        public Func<float> Treasury;
        public Action<float> SpendTreasury;
        public bool SeesEnemyHand;
        /// <summary>Стартовая рука уже пересдана (муллиган — один раз за бой).</summary>
        public bool Mulliganed;
    }

    /// <summary>Что произошло — экран боя проигрывает эти события по порядку (анимации), автобой их игнорирует.</summary>
    public enum EvType
    {
        TurnStart, Draw, Reveal, OrderFx, TrapSet, TrapFire, Shot, Damage, Cover, Heal, Death, Spawn, Move,
        Status, Buff, Control, ShieldDown, Weather, Breach, Retreat, End, Log, Steal
    }

    public sealed class BEvent
    {
        public EvType T;
        public int Side = -1, Sector = -1, Uid = -1, Uid2 = -1, Amount;
        public string Text;
        public CardDef Def;
        public override string ToString() => $"{T} s{Side} sec{Sector} u{Uid}->{Uid2} {Amount} {Text}";
    }

    /// <summary>Итог боя для стратегии.</summary>
    public sealed class BattleResult
    {
        public bool AttackerWon, Retreated, Breach;
        public int Turns;
        /// <summary>Погибшие и потраченные (приказы) экземпляры резерва — удалить из империи.</summary>
        public readonly List<long> Lost = new List<long>();
        /// <summary>Выжившие: экземпляр, доля брони, участвовал ли (получает ветеранство).</summary>
        public readonly List<(long inst, int side, float hp, bool fought)> Survivors = new List<(long, int, float, bool)>();
        public int MilitiaKilled;
        public readonly List<SectorType> Ruined = new List<SectorType>();
        public CardDef Trophy;
        public long TrophyInst = -1;
        public int[] Killed = new int[2];
        public int[] LostCount = new int[2];
    }

    /// <summary>Исходные данные боя (собираются из стратегии или песочницы).</summary>
    public sealed class BattleSetup
    {
        public int SystemId = -1, PlanetIndex = -1;
        public string PlanetName = "";
        public Terrain Terrain;
        public SectorType[] Sectors = { SectorType.Power, SectorType.Factory, SectorType.HQ };
        public BSide Attacker = new BSide(), Defender = new BSide();
        /// <summary>Карты атакующего и защитника (сооружения и ловушки защитника выставятся сами).</summary>
        public readonly List<BCard> AttackerCards = new List<BCard>(), DefenderCards = new List<BCard>();
        public BCard AttackerHero, DefenderHero;
        public int Seed = 1;
        /// <summary>Мастерство ИИ 0…4 (из сложности).</summary>
        public int AiSkill = 2;
    }

    /// <summary>
    /// Карточный штурм: 3 сектора, 8 ходов. Обе стороны выкладывают карты рубашкой вверх, потом всё раскрывается,
    /// и в каждом секторе идёт перестрелка: сила наносит урон, броня его принимает, погибшие уходят навсегда.
    /// Перед первым ходом флот десанта бьёт с орбиты по укреплениям колонии (артподготовка).
    /// Очки приказов растут с номером хода до <see cref="PointsCap"/>; кончилась колода — приходят подкрепления
    /// (ополчение у обороны, экипажи с кораблей у десанта). Победа — 2 сектора из 3 после 8-го хода или прорыв
    /// в Штаб при снятом щите, если перевес продержался два хода подряд (после первого — тревога). Ничья — защитнику.
    /// Чистый C# без Unity: экран боя только показывает события из <see cref="Events"/>; автобой и ИИ против ИИ
    /// гоняют те же правила мгновенно.
    /// </summary>
    public sealed partial class BattleState
    {
        public const int Turns = 8, StartHand = 4, HandLimit = 8, BreachPower = 9, PointsCap = 6;
        /// <summary>Сколько пехотинцев врага давит машина при раскрытии (Kw.Crush).</summary>
        public const int CrushLimit = 2;
        public const int Attacker = 0, Defender = 1;

        public readonly BSide[] Sides = new BSide[2];
        public readonly BSector[] Sectors = new BSector[3];
        public readonly List<BEvent> Events = new List<BEvent>();
        public readonly BattleSetup Setup;
        public readonly Random Rng;
        public int Turn;
        public bool Over;
        public int Winner = -1;
        public bool WeatherActive;
        /// <summary>Штаб под угрозой: перевес для прорыва уже был в конце прошлого хода — ещё ход, и прорыв.</summary>
        public bool BreachWarned;
        /// <summary>Погода (описание для экрана), открывается на 3-м ходу.</summary>
        public string WeatherName, WeatherText;
        public BattleResult Result;

        private int _uid = 1;
        private readonly List<BUnit> _everOnBoard = new List<BUnit>();

        public BattleState(BattleSetup s)
        {
            Setup = s;
            Rng = new Random(s.Seed);
            Sides[Attacker] = s.Attacker;
            Sides[Defender] = s.Defender;
            for (int i = 0; i < 3; i++) Sectors[i] = new BSector { Type = s.Sectors[i] };
        }

        public static int Enemy(int side) => 1 - side;
        public int SectorIndex(SectorType t) { for (int i = 0; i < 3; i++) if (Sectors[i].Type == t) return i; return -1; }

        // ==================== СОБЫТИЯ ====================

        public BEvent Log(EvType t, int side = -1, int sector = -1, int uid = -1, int uid2 = -1, int amount = 0, string text = null, CardDef def = null)
        {
            var e = new BEvent { T = t, Side = side, Sector = sector, Uid = uid, Uid2 = uid2, Amount = amount, Text = text, Def = def };
            Events.Add(e);
            return e;
        }

        // ==================== ЗАПРОСЫ ====================

        /// <summary>Живые карты стороны в секторе — копией списка: эффекты могут менять стол, пока по нему идут.</summary>
        public List<BUnit> UnitsOf(int side, int sector)
        {
            var l = new List<BUnit>();
            foreach (var u in Sectors[sector].Units)
                if (u.Side == side && u.Alive && !u.IsTrap) l.Add(u);
            return l;
        }

        public BUnit FindUnit(int uid)
        {
            foreach (var s in Sectors) foreach (var u in s.Units) if (u.Uid == uid) return u;
            return null;
        }

        public bool HoldsSector(int side, SectorType t)
        {
            int i = SectorIndex(t);
            return i >= 0 && Sectors[i].Control == side;
        }

        /// <summary>Работает ли электричество защитника (турели и щит Штаба).</summary>
        public bool DefenderPowered
        {
            get
            {
                int p = SectorIndex(SectorType.Power);
                return p < 0 || Sectors[p].Control != Attacker;
            }
        }

        public int Power(BUnit u)
        {
            if (u == null || u.Dead || u.IsTrap) return 0;
            int p = u.BasePower + u.PowerBonus + u.TempPower;
            if (u.Has(Kw.Swarm)) p = Math.Min(p, u.Armor);
            if (WeatherActive && Setup.Terrain == Terrain.Continental && u.Has(Kw.Vehicle)) p -= 1;
            return Math.Max(0, p);
        }

        /// <summary>Сила стороны в секторе (для контроля): все живые раскрытые карты.</summary>
        public int SectorPower(int side, int sector)
        {
            int s = 0;
            foreach (var u in UnitsOf(side, sector)) if (u.Revealed) s += Power(u);
            return s;
        }

        public bool CanShoot(BUnit u)
        {
            if (u.Dead || !u.Revealed || u.IsTrap) return false;
            if (u.Suppressed > 0 || u.Disabled > 0 || Power(u) <= 0) return false;
            if (u.Has(Kw.NeedsPower) && !DefenderPowered) return false;
            if (Sectors[u.Sector].Smoke) return false;
            return true;
        }

        public BUnit ShieldUnit
        {
            get
            {
                int hq = SectorIndex(SectorType.HQ);
                if (hq < 0) return null;
                foreach (var u in Sectors[hq].Units) if (u.Has(Kw.Shield) && u.Alive) return u;
                return null;
            }
        }

        /// <summary>Щит Штаба держится: генератор цел, не отключён, и электростанция у защитника.</summary>
        public bool ShieldUp
        {
            get { var s = ShieldUnit; return s != null && s.Disabled <= 0 && DefenderPowered; }
        }

        public int CostOf(int side, BCard c)
        {
            int cost = c.Def.Cost;
            if (c.Def.Kind == CardKind.Order) cost -= Sides[side].OrderCostDiscount;
            return Math.Max(0, cost);
        }

        // ==================== ДЕЙСТВИЯ НАД ЮНИТАМИ ====================

        public BUnit Place(BCard card, int side, int sector, bool revealed, bool hidden = false)
        {
            var d = card.Def;
            int stars = Math.Min(3, card.Stars);
            var u = new BUnit
            {
                Uid = _uid++, Card = card, Side = side, Sector = sector,
                BasePower = Math.Max(0, d.Power + (stars >= 2 ? 1 : 0) + (stars >= 3 ? 1 : 0) + (d.Kind == CardKind.Structure && d.Has(Kw.NeedsPower) ? TurretPowerDelta : 0)),
                MaxArmor = d.Armor + (stars >= 1 ? 1 : 0) + (stars >= 3 ? 1 : 0),
                Revealed = revealed, Hidden = hidden
            };
            u.Armor = u.MaxArmor;
            u.CoverLeft = d.Cover;
            Sectors[sector].Units.Add(u);
            if (!u.IsTrap) _everOnBoard.Add(u);
            return u;
        }

        /// <summary>Нанести урон с учётом бронебоя, огня, снайпера и укрытия. Возвращает снятую броню.</summary>
        public int Damage(BUnit src, BUnit tgt, int amount, bool ignoreCover = false)
        {
            if (tgt == null || tgt.Dead || amount <= 0) return 0;
            int amt = amount;
            if (src != null)
            {
                bool hard = tgt.Has(Kw.Vehicle) || tgt.IsStructure;
                if (src.Has(Kw.Pierce) && hard) amt *= 2;
                if (src.Has(Kw.Flame)) { ignoreCover = true; if (tgt.IsStructure) amt *= 2; }
                if (src.Has(Kw.Sniper) && tgt.Has(Kw.Infantry) && !(WeatherActive && Setup.Terrain == Terrain.Desert)) amt *= 2;
            }
            if (!ignoreCover && tgt.CoverLeft > 0)
            {
                int absorb = Math.Min(tgt.CoverLeft, amt);
                tgt.CoverLeft -= absorb;
                amt -= absorb;
                Log(EvType.Cover, tgt.Side, tgt.Sector, tgt.Uid, amount: absorb);
            }
            if (amt <= 0) return 0;
            int dealt = Math.Min(amt, tgt.Armor);
            tgt.Armor -= amt;
            Log(EvType.Damage, tgt.Side, tgt.Sector, tgt.Uid, src != null ? src.Uid : -1, amt);
            if (tgt.Armor <= 0) Kill(tgt, src);
            return dealt;
        }

        public void Kill(BUnit u, BUnit by = null)
        {
            if (u.Dead) return;
            u.Dead = true;
            u.Armor = 0;
            Log(EvType.Death, u.Side, u.Sector, u.Uid, by != null ? by.Uid : -1);
            if (by != null) Result_Kill(by.Side);
            u.Def.OnDeath?.Invoke(this, u);
        }

        public void Heal(BUnit u, int amount)
        {
            if (u == null || u.Dead || amount <= 0 || u.Armor >= u.MaxArmor) return;
            int h = Math.Min(amount, u.MaxArmor - u.Armor);
            u.Armor += h;
            Log(EvType.Heal, u.Side, u.Sector, u.Uid, amount: h);
        }

        public void Buff(BUnit u, int power, bool permanent, string why = null)
        {
            if (u == null || u.Dead) return;
            if (permanent) u.PowerBonus += power; else u.TempPower += power;
            Log(EvType.Buff, u.Side, u.Sector, u.Uid, amount: power, text: why);
        }

        public void SetStatus(BUnit u, string what, int turns)
        {
            if (u == null || u.Dead) return;
            if (what == "suppress")
            {
                if (u.Card.Stars >= 3 && u.Suppressed == 0 && !u.Has(Kw.Vehicle)) { Log(EvType.Status, u.Side, u.Sector, u.Uid, text: "immune"); return; }
                u.Suppressed = Math.Max(u.Suppressed, turns);
            }
            else if (what == "disable") u.Disabled = Math.Max(u.Disabled, turns);
            else if (what == "immobile") { u.Immobile = Math.Max(u.Immobile, turns); u.Suppressed = Math.Max(u.Suppressed, turns); }
            Log(EvType.Status, u.Side, u.Sector, u.Uid, amount: turns, text: what);
        }

        public BUnit Spawn(CardDef def, int side, int sector)
        {
            var u = Place(new BCard(def), side, sector, true);
            Log(EvType.Spawn, side, sector, u.Uid, def: def);
            return u;
        }

        public void Move(BUnit u, int toSector)
        {
            if (u == null || u.Dead || toSector == u.Sector || toSector < 0 || toSector > 2) return;
            int from = u.Sector;
            Sectors[from].Units.Remove(u);
            u.Sector = toSector;
            Sectors[toSector].Units.Add(u);
            Log(EvType.Move, u.Side, toSector, u.Uid, amount: from);
        }

        /// <summary>Убрать юнит со стола живым (сдался, вернулся в руку) — в потери не идёт.</summary>
        public void RemoveFromBoard(BUnit u, string why)
        {
            if (u == null || u.Dead) return;
            Sectors[u.Sector].Units.Remove(u);
            Log(EvType.Move, u.Side, u.Sector, u.Uid, amount: -1, text: why);
        }

        /// <summary>Перевести юнит на другую сторону (пропаганда, ассимиляция).</summary>
        public void Convert(BUnit u, int toSide)
        {
            if (u == null || u.Dead || u.Side == toSide) return;
            u.Side = toSide;
            Log(EvType.Steal, toSide, u.Sector, u.Uid);
        }

        public List<BUnit> Enemies(int side, int sector)
        {
            var l = new List<BUnit>();
            foreach (var u in UnitsOf(Enemy(side), sector)) if (u.Revealed && !u.Has(Kw.Shield)) l.Add(u);   // купол щита — не цель
            return l;
        }

        public BUnit RandomEnemy(int side, int sector)
        {
            var l = Enemies(side, sector);
            return l.Count == 0 ? null : l[Rng.Next(l.Count)];
        }

        public BUnit StrongestEnemy(int side, int sector)
        {
            BUnit best = null;
            foreach (var u in Enemies(side, sector))
                if (best == null || Power(u) > Power(best) || (Power(u) == Power(best) && u.Armor < best.Armor)) best = u;
            return best;
        }

        /// <summary>Распределить урон по врагам сектора: сначала самые сильные (орбитальный удар).</summary>
        public void SpreadDamage(int side, int sector, int total, bool ignoreCover)
        {
            int guard = 20;
            while (total > 0 && guard-- > 0)
            {
                var t = StrongestEnemy(side, sector);
                if (t == null) break;
                int need = t.Armor + (ignoreCover ? 0 : t.CoverLeft);
                int hit = Math.Min(total, need);
                Damage(null, t, hit, ignoreCover);
                total -= hit;
            }
        }

        public void DrawCards(int side, int n)
        {
            var s = Sides[side];
            for (int i = 0; i < n; i++)
            {
                if (s.Hand.Count >= HandLimit) return;
                if (s.Deck.Count == 0)
                {
                    // Колода кончилась — подкрепления: жители колонии берут оружие, флот спускает экипажи
                    var rd = side == Defender ? CardCatalog.Militia : s.FleetPower > 0f ? CardCatalog.Crew : null;
                    if (Turn < 2 || rd == null) return;
                    var rc = new BCard(rd);
                    s.Hand.Add(rc);
                    Log(EvType.Draw, side, def: rd, text: "reinforce");
                    return;
                }
                var c = s.Deck[0];
                s.Deck.RemoveAt(0);
                s.Hand.Add(c);
                Log(EvType.Draw, side, def: c.Def);
            }
        }

        private void Result_Kill(int bySide) { if (Result != null) Result.Killed[bySide]++; }
    }
}
