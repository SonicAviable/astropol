using System;
using System.Collections.Generic;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Тактический ИИ карточного штурма. Видит только открытый стол (как игрок): карты врага этого хода ещё рубашкой вверх.
    /// Жадно выбирает «карта → сектор» по приросту шанса удержать сектор с учётом его ценности:
    /// атакующему важнее электростанция (снять щит), потом Штаб; защитнику — Штаб и электростанция.
    /// Мастерство 0…4: на низком — шум в оценках и пропуски, на высоком — держит приказы до нужного сектора.
    /// </summary>
    public static class BattleAI
    {
        public static void PlanTurn(BattleState b, int side, int skill)
        {
            if (b.Over) return;
            var sd = b.Sides[side];
            int enemy = BattleState.Enemy(side);
            var mine = new float[3];
            var theirs = new float[3];
            for (int i = 0; i < 3; i++) { mine[i] = b.SectorPower(side, i); theirs[i] = b.SectorPower(enemy, i); }

            if (ShouldRetreat(b, side, mine, theirs)) { b.Retreat(side); return; }

            float noise = (4 - Math.Max(0, Math.Min(4, skill))) * 0.12f;
            for (int guard = 0; guard < 12 && sd.Points > 0; guard++)
            {
                BCard bestCard = null;
                int bestSec = -1;
                float best = 0.04f;
                foreach (var c in new List<BCard>(sd.Hand))
                {
                    if (b.CostOf(side, c) > sd.Points) continue;
                    for (int s = 0; s < 3; s++)
                    {
                        if (b.CanPlay(side, c, s) != null) continue;
                        float v = Value(b, side, c, s, mine, theirs);
                        if (noise > 0) v *= 1f + noise * (float)(b.Rng.NextDouble() - 0.5) * 2f;
                        // Карта подешевле при равной пользе — экономим очки на следующую
                        v -= b.CostOf(side, c) * 0.004f;
                        if (v > best) { best = v; bestCard = c; bestSec = s; }
                    }
                }
                if (bestCard == null) break;
                b.Play(side, bestCard, bestSec);
                Apply(b, side, bestCard, bestSec, mine, theirs);
            }
        }

        /// <summary>
        /// Муллиган ИИ: сдать дорогие карты (5+), если их больше одной, и самую дорогую, когда в руке нет
        /// дешёвого юнита на первые ходы (до 2 очков).
        /// </summary>
        public static void Mulligan(BattleState b, int side)
        {
            var hand = b.Sides[side].Hand;
            var back = new List<BCard>();
            var pricey = new List<BCard>();
            foreach (var c in hand) if (c.Def.Kind != CardKind.Hero && b.CostOf(side, c) >= 5) pricey.Add(c);
            pricey.Sort((x, y) => b.CostOf(side, y).CompareTo(b.CostOf(side, x)));
            for (int i = 0; i + 1 < pricey.Count; i++) back.Add(pricey[i]);   // одну дорогую оставить на финал
            bool cheapUnit = false;
            foreach (var c in hand) if (c.Def.IsBoardUnit && b.CostOf(side, c) <= 2) { cheapUnit = true; break; }
            if (!cheapUnit)
            {
                BCard worst = null;
                foreach (var c in hand) if (c.Def.Kind != CardKind.Hero && !back.Contains(c) && (worst == null || b.CostOf(side, c) > b.CostOf(side, worst))) worst = c;
                if (worst != null) back.Add(worst);
            }
            b.Mulligan(side, back);
        }

        /// <summary>Атакующий уводит десант, когда штурм безнадёжен (карты сохранятся во флоте).</summary>
        /// <summary>Отладка баланса: ИИ никогда не отступает.</summary>
        public static bool NoRetreat;

        private static bool ShouldRetreat(BattleState b, int side, float[] mine, float[] theirs)
        {
            if (NoRetreat || side != BattleState.Attacker || b.Turn < 4) return false;
            int held = 0; float my = 0, th = 0;
            for (int i = 0; i < 3; i++) { if (b.Sectors[i].Control == side) held++; my += mine[i]; th += theirs[i]; }
            int left = BattleState.Turns - b.Turn + 1;
            float handPower = 0;
            foreach (var c in b.Sides[side].Hand) handPower += c.Def.Power;
            return held == 0 && th > (my + handPower) * 2.2f + 6 * left;
        }

        private static float Importance(BattleState b, int side, int sector)
        {
            var t = b.Sectors[sector].Type;
            bool atk = side == BattleState.Attacker;
            switch (t)
            {
                // Победа — 2 сектора из 3, поэтому все сектора важны; Штаб чуть важнее (прорыв), электростанция —
                // пока защищает щит и турели
                case SectorType.HQ:
                    // «Штаб под угрозой» — ещё ход, и прорыв: атакующий давит, защитник бросает туда всё
                    if (b.BreachWarned) return atk ? 1.6f : 1.8f;
                    return atk ? (b.ShieldUp ? 1.0f : 1.35f) : 1.2f;
                case SectorType.Power:
                    if (!atk) return 1.12f;
                    // Каждая турель защитника работает от электростанции — тем ценнее её взять
                    int turrets = 0;
                    for (int i = 0; i < 3; i++) foreach (var u in b.UnitsOf(BattleState.Defender, i)) if (u.Has(Kw.NeedsPower)) turrets++;
                    return (b.ShieldUp ? 1.2f : 1.0f) + 0.08f * turrets;
                case SectorType.Factory: return b.Turn <= 4 ? 1.15f : 1.0f;
                case SectorType.Mine: return b.Turn <= 4 ? 1.1f : 1.0f;
                default: return 1f;
            }
        }

        /// <summary>Шанс удержать сектор при перевесе a над b (гладкая ступенька).</summary>
        private static float Hold(float a, float bb) => (float)(1.0 / (1.0 + Math.Exp(-(a - bb + 0.5) / 2.4)));

        private static float Value(BattleState b, int side, BCard c, int s, float[] mine, float[] theirs)
        {
            var d = c.Def;
            int enemy = BattleState.Enemy(side);
            float imp = Importance(b, side, s);
            float before = Hold(mine[s], theirs[s]);
            float gainMine = 0, cutTheirs = 0, extra = 0;

            if (d.IsBoardUnit || d.Kind == CardKind.Hero)
            {
                gainMine = d.Power + (c.Stars >= 2 ? 1 : 0);
                if (d.Has(Kw.Fragile)) gainMine *= 0.6f;
                if (d.Salvo > 0) cutTheirs += KillValue(b, side, s, d.Salvo, 1);
                if (d.Has(Kw.Crush)) foreach (var e in b.Enemies(side, s)) if (e.Has(Kw.Infantry) && e.Armor <= 2) cutTheirs += b.Power(e);
                extra += d.Armor * 0.012f;   // живучесть — в перестрелке
                if (d.Id == "U6") extra += Wounded(b, side, s) * 0.04f;
                if (d.Id == "X2" || d.Id == "H1") gainMine += CountAllies(b, side, s) * 0.8f;
                if (d.Id == "A1") gainMine += CountAllies(b, side, s);
                if (d.Id == "I3") extra += b.Turn < BattleState.Turns ? 0.08f : 0f;
                if (d.Id == "I1") extra += 0.05f;
                if (d.Id == "U9") gainMine += 2f;
                if (d.Id == "U7" || d.Id == "U8") extra += 0.03f;
                if (d.Has(Kw.Indirect)) extra += 0.04f;
            }
            else if (d.Kind == CardKind.Trap)
            {
                // Ловушка — туда, где враг, вероятно, будет наступать
                extra = 0.06f + (theirs[s] > 0 ? 0.04f : 0f) + imp * 0.03f;
            }
            else
            {
                switch (d.Id)
                {
                    case "O1": cutTheirs = KillValue(b, side, s, 2, 99); break;
                    case "O2": cutTheirs = SpreadValue(b, side, s, 7); extra -= 0.02f; break;
                    case "O3": gainMine = 3f; break;
                    case "O4": extra = mine[s] > theirs[s] + 4 ? 0.08f : 0f; break;
                    case "O5": extra = theirs[s] > mine[s] + 3 && mine[s] > 0 ? 0.07f : 0f; break;
                    case "O6": extra = b.Sides[side].Hand.Count <= 2 ? 0.12f : 0.02f; break;
                    case "O7": foreach (var e in b.Enemies(side, s)) if (e.IsStructure) cutTheirs += b.Power(e) + (e.Has(Kw.Shield) ? 6 : 0); break;
                    case "O8": foreach (var e in b.Enemies(side, s)) if (e.Has(Kw.Militia)) cutTheirs += b.Power(e) * (b.Sides[enemy].Stability < 50 ? 2f : 0.5f); break;
                    case "O9": foreach (var e in b.Enemies(side, s)) if (e.Has(Kw.Vehicle) || e.IsStructure || e.Has(Kw.Drone)) cutTheirs += b.Power(e); break;
                    case "A4": gainMine = 2f; break;
                    case "X3": gainMine = Math.Max(0, CountAllies(b, side, s) - 1) * 2f - 1f; break;
                    case "Q2": cutTheirs = Math.Min(3f, Strongest(b, side, s)); break;
                    case "Q3": cutTheirs = HeroOrStrongest(b, side, s); break;
                    case "I2": cutTheirs = Strongest(b, side, s); break;
                    case "I4":
                        cutTheirs = KillValue(b, side, s, 3, 99);
                        gainMine = -KillValue(b, enemy, s, 3, 99);   // свои тоже пострадают
                        break;
                    case "T1": cutTheirs = b.Enemies(side, s).Count; break;
                    case "N3": gainMine = 2f * Math.Min(2, DeadDrones(b, side)) * 0.8f; break;
                    case "N4": cutTheirs = Weakest(b, side, s) + 0.5f; gainMine = 1f; break;
                }
            }
            float after = Hold(mine[s] + gainMine, Math.Max(0, theirs[s] - cutTheirs));
            return imp * (after - before) + extra;
        }

        private static void Apply(BattleState b, int side, BCard c, int s, float[] mine, float[] theirs)
        {
            var d = c.Def;
            if (d.IsBoardUnit || d.Kind == CardKind.Hero) mine[s] += d.Power;
            else if (d.Id == "O1" || d.Id == "O2" || d.Id == "O9" || d.Id == "O7") theirs[s] = Math.Max(0, theirs[s] - 3);
            else if (d.Id == "O3" || d.Id == "A4") mine[s] += 2;
        }

        private static float KillValue(BattleState b, int side, int s, int dmg, int targets)
        {
            float v = 0;
            int n = 0;
            foreach (var e in b.Enemies(side, s))
            {
                if (n++ >= targets) break;
                int hp = e.Armor + e.CoverLeft;
                v += hp <= dmg ? b.Power(e) : b.Power(e) * 0.15f;
            }
            return v;
        }

        private static float SpreadValue(BattleState b, int side, int s, int total)
        {
            var list = b.Enemies(side, s);
            list.Sort((x, y) => b.Power(y).CompareTo(b.Power(x)));
            float v = 0;
            foreach (var e in list)
            {
                if (total <= 0) break;
                if (e.Armor <= total) { v += b.Power(e); total -= e.Armor; }
                else { v += b.Power(e) * 0.25f; total = 0; }
            }
            return v;
        }

        private static int CountAllies(BattleState b, int side, int s) { int n = 0; foreach (var u in b.UnitsOf(side, s)) if (u.Revealed) n++; return n; }
        private static int Wounded(BattleState b, int side, int s) { int n = 0; foreach (var u in b.UnitsOf(side, s)) n += u.MaxArmor - u.Armor; return n; }
        private static float Strongest(BattleState b, int side, int s) { var t = b.StrongestEnemy(side, s); return t == null ? 0 : b.Power(t); }
        private static float Weakest(BattleState b, int side, int s)
        {
            float w = 0; bool any = false;
            foreach (var e in b.Enemies(side, s)) if (!e.IsStructure && (!any || b.Power(e) < w)) { w = b.Power(e); any = true; }
            return w;
        }
        private static float HeroOrStrongest(BattleState b, int side, int s)
        {
            foreach (var e in b.Enemies(side, s)) if (e.Def.Kind == CardKind.Hero) return b.Power(e) + 2;
            return Math.Min(4, Strongest(b, side, s));
        }
        private static int DeadDrones(BattleState b, int side)
        {
            int n = 0;
            foreach (var sec in b.Sectors) foreach (var u in sec.Units) if (u.Dead && !u.Hidden && u.Side == side && u.Has(Kw.Drone)) n++;
            return n;
        }

        /// <summary>Автобой: те же правила, обе стороны — ИИ. Мгновенно; годится для ИИ против ИИ и кнопки «Автобой».</summary>
        public static BattleResult AutoResolve(BattleSetup setup, out BattleState state)
        {
            var b = new BattleState(setup);
            b.Begin();
            int guard = 0;
            while (!b.Over && guard++ < 20)
            {
                PlanTurn(b, BattleState.Attacker, setup.AiSkill);
                if (b.Over) break;
                PlanTurn(b, BattleState.Defender, setup.AiSkill);
                b.Resolve();
            }
            state = b;
            return b.Result;
        }
    }
}
