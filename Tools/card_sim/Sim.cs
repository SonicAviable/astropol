using System;
using System.Collections.Generic;
using StellarisClone.Cards;

static class Sim
{
    static readonly string[] Factions = { "astrea", "xarn", "aquila", "iridia", "terraan", "nol" };
    static readonly string[] Common = { "U1", "U1", "U1", "U2", "U3", "U4", "U5", "U6", "U7", "U8", "U9", "U10", "U11", "U12", "O1", "O2", "O3", "O4", "O5", "O6", "O7", "O9" };

    static BCard Card(string id, ref long inst, Random rnd)
    {
        var d = CardCatalog.Get(id);
        int stars = rnd.NextDouble() < 0.25 ? rnd.Next(1, 4) : 0;
        return new BCard(d, inst++, stars);
    }

    static BattleSetup Build(int seed, int atkCards, int skill, out int fort)
    {
        var rnd = new Random(seed);
        string fa = Factions[rnd.Next(Factions.Length)], fd = Factions[rnd.Next(Factions.Length)];
        var s = new BattleSetup { Seed = seed, AiSkill = skill, Terrain = (Terrain)rnd.Next(4) };
        var mids = new[] { SectorType.Factory, SectorType.Mine, SectorType.City, SectorType.Port };
        s.Sectors = new[] { SectorType.Power, mids[rnd.Next(mids.Length)], SectorType.HQ };
        s.PlanetName = "T";
        s.Attacker.Faction = fa; s.Defender.Faction = fd;
        s.Attacker.FleetPower = 2000f; s.Attacker.PointsPerTurn = 1;
        s.Defender.Stability = 40 + rnd.Next(40);
        long inst = 1;
        var ad = new List<string>();
        for (int k = 0; k < atkCards - 3; k++) ad.Add(Common[rnd.Next(Common.Length)]);
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

    static void Main(string[] args)
    {
        int n = args.Length > 0 ? int.Parse(args[0]) : 2000;
        int atkCards = args.Length > 1 ? int.Parse(args[1]) : 16;
        int win = 0, breach = 0, retreat = 0, exhaust = 0, turnsSum = 0, plays = 0, playTurns = 0, unspent = 0, shots = 0, deaths = 0, controlFlips = 0;
        var turnHist = new int[30]; var retTurn = new int[30]; var breachTurn = new int[30];
        int deckLeftA = 0, deckLeftD = 0, handLeftA = 0, handLeftD = 0, emptyHandTurns = 0, sideTurns = 0, aliveEnd = 0;
        for (int i = 0; i < n; i++)
        {
            var s = Build(i * 7919 + 13, atkCards, 2, out _);
            var b = new BattleState(s);
            b.Begin();
            int guard = 0;
            while (!b.Over && guard++ < 30)
            {
                for (int side = 0; side < 2; side++)
                {
                    int before = b.Sides[side].Hand.Count;
                    BattleAI.PlanTurn(b, side, s.AiSkill);
                    if (b.Over) break;
                    plays += b.Sides[side].Pending.Count; playTurns++;
                    unspent += b.Sides[side].Points;
                    sideTurns++;
                    if (b.Sides[side].Hand.Count == 0 && b.Sides[side].Pending.Count == 0) emptyHandTurns++;
                }
                if (b.Over) break;
                b.Resolve();
            }
            var r = b.Result;
            if (r.AttackerWon) win++;
            if (r.Breach) { breach++; breachTurn[r.Turns]++; }
            if (r.Retreated) { retreat++; retTurn[r.Turns]++; }
            turnsSum += r.Turns; turnHist[r.Turns]++;
            deckLeftA += b.Sides[0].Deck.Count; deckLeftD += b.Sides[1].Deck.Count;
            handLeftA += b.Sides[0].Hand.Count; handLeftD += b.Sides[1].Hand.Count;
            foreach (var e in b.Events) { if (e.T == EvType.Shot) shots++; if (e.T == EvType.Death) deaths++; if (e.T == EvType.Control) controlFlips++; }
            for (int sec = 0; sec < 3; sec++) for (int sd = 0; sd < 2; sd++) aliveEnd += b.UnitsOf(sd, sec).Count;
        }
        Console.WriteLine($"боёв {n}, колода атаки {atkCards}, ходов в правилах {BattleState.Turns}");
        Console.WriteLine($"  атака побеждает {win * 100.0 / n:0}%, прорыв {breach * 100.0 / n:0}%, отступление {retreat * 100.0 / n:0}%");
        Console.WriteLine($"  средняя длина {turnsSum / (double)n:0.00} ход.; распределение: " + string.Join(" ", Range(turnHist)));
        Console.WriteLine($"  прорыв на ходу: " + string.Join(" ", Range(breachTurn)) + " | отступление на ходу: " + string.Join(" ", Range(retTurn)));
        Console.WriteLine($"  карт за ход на сторону {plays / (double)playTurns:0.00}, неистраченных очков {unspent / (double)playTurns:0.00}, пустая рука {emptyHandTurns * 100.0 / sideTurns:0}% ходов");
        Console.WriteLine($"  в конце: колода атака {deckLeftA / (double)n:0.0} / оборона {deckLeftD / (double)n:0.0}, рука {handLeftA / (double)n:0.0} / {handLeftD / (double)n:0.0}, живых на столе {aliveEnd / (double)n:0.0}");
        Console.WriteLine($"  за бой: выстрелов {shots / (double)n:0}, гибелей {deaths / (double)n:0.0}, смен контроля {controlFlips / (double)n:0.0}");
    }
    static IEnumerable<string> Range(int[] h) { for (int i = 1; i < h.Length; i++) if (h[i] > 0) yield return $"{i}:{h[i]}"; }
}
