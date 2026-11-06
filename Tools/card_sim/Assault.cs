using System;
using System.Collections.Generic;
using StellarisClone.Cards;

// Бои, собранные как в игре (AssaultLink): десант — состав армии по доктрине, оборона — резерв по местам колонии,
// ополчение от жителей, щит и турели укреплений.
//   mono assault.exe table [боёв]     — доля побед атаки: укрепления × размер десанта (начало и середина партии, 8 и 20 жителей)
//   mono assault.exe factions [боёв]  — доля побед каждой фракции в атаке и обороне
// EXCL=X4,I2 — убрать фракционные карты из составов (проверить вклад одной карты).
public static class Assault
{
    static readonly string[] FactionKeys = { "astrea", "xarn", "aquila", "iridia", "terraan", "nol" };
    static readonly string[] EarlyInf = { "U1", "U2" }, MidInf = { "U1", "U2", "U3", "U4", "U5", "U6", "U7" };
    static readonly string[] EarlyVeh = { }, MidVeh = { "U8", "U9", "U10", "U11" };
    static readonly string[] EarlyOrd = { "O5", "O6" }, MidOrd = { "O1", "O3", "O4", "O5", "O6", "O9" };

    static string Pick(Random r, string[] a) => a[r.Next(a.Length)];

    // Армия по доктрине: техника 25%, приказы 15%, оборона 15% (здесь — только то, что летит), остальное пехота
    static List<string> Army(Random r, string fac, int size, bool mid)
    {
        var l = new List<string>();
        var facCards = new List<CardDef>();
        var ex = (Environment.GetEnvironmentVariable("EXCL") ?? "").Split(',');
        foreach (var d in CardCatalog.ForFaction(fac)) if (d.Faction == fac && Array.IndexOf(ex, d.Id) < 0) facCards.Add(d);
        int veh = mid ? (int)Math.Round(size * 0.25) : 0, ord = (int)Math.Round(size * 0.15);
        for (int i = 0; i < veh; i++)
        {
            var fv = facCards.FindAll(d => d.Kind == CardKind.Unit && d.Site == ProdSite.Industry);
            l.Add(fv.Count > 0 && r.NextDouble() < 0.35 ? fv[r.Next(fv.Count)].Id : Pick(r, MidVeh));
        }
        for (int i = 0; i < ord; i++)
        {
            var fo = facCards.FindAll(d => d.Kind == CardKind.Order);
            l.Add(fo.Count > 0 && r.NextDouble() < 0.4 ? fo[r.Next(fo.Count)].Id : Pick(r, mid ? MidOrd : EarlyOrd));
        }
        while (l.Count < size)
        {
            var fu = facCards.FindAll(d => d.Kind == CardKind.Unit && d.Site == ProdSite.City);
            l.Add(fu.Count > 0 && r.NextDouble() < 0.35 ? fu[r.Next(fu.Count)].Id : Pick(r, mid ? MidInf : EarlyInf));
        }
        return l;
    }

    public static BattleSetup Build(int seed, int atk, int fort, int pop, bool mid, int defReserve)
    {
        var r = new Random(seed);
        string fa = FactionKeys[r.Next(6)], fd = FactionKeys[r.Next(6)];
        var s = new BattleSetup { Seed = seed, AiSkill = 2, Terrain = (Terrain)r.Next(4), PlanetName = "T" };
        var mids = new[] { SectorType.Factory, SectorType.Mine, SectorType.Port };
        s.Sectors = new[] { SectorType.Power, mids[r.Next(3)], SectorType.HQ };
        s.Attacker.Faction = fa; s.Defender.Faction = fd;
        s.Attacker.FleetPower = 1500f; s.Attacker.PointsPerTurn = 1;
        s.Defender.Stability = 40 + r.Next(40);
        long inst = 1;
        foreach (var id in Army(r, fa, atk, mid)) s.AttackerCards.Add(new BCard(CardCatalog.Get(id), inst++, r.NextDouble() < 0.2 ? 1 : 0));
        for (int i = s.AttackerCards.Count; i < 8; i++) s.AttackerCards.Add(new BCard(CardCatalog.Crew));   // экипажи до минимума
        // Оборона: резерв защитника (доктрина) — сначала оборонительные, затем лучшие отряды по местам колонии
        int places = Math.Min(14, Math.Max(3, 3 + pop / 4 + fort * 2));
        var reserve = Army(r, fd, defReserve, mid);
        int garrisons = (int)Math.Round(defReserve * 0.15);
        var gl = new List<string>();
        for (int i = 0; i < garrisons; i++) gl.Add(i % 3 == 0 ? "D3" : i % 3 == 1 ? "D2" : (mid ? "D5" : "D2"));
        int used = 0, structs = 0, orders = 0;
        foreach (var id in gl) { if (used >= places) break; if (id == "D3") { if (structs >= fort + 1) continue; structs++; } s.DefenderCards.Add(new BCard(CardCatalog.Get(id), inst++)); used++; }
        foreach (var id in reserve)
        {
            var d = CardCatalog.Get(id);
            if (d.Kind == CardKind.Order) { if (orders < 3) { orders++; s.DefenderCards.Add(new BCard(d, inst++)); } continue; }
            if (used >= places) continue;
            used++; s.DefenderCards.Add(new BCard(d, inst++));
        }
        int militia = Math.Min(8, Math.Max(1, pop / 5));
        for (int i = 0; i < militia; i++) s.DefenderCards.Add(new BCard(CardCatalog.Militia));
        if (fort >= 1) s.DefenderCards.Add(new BCard(CardCatalog.ShieldHQ));
        for (int i = 0; i < fort; i++) s.DefenderCards.Add(new BCard(CardCatalog.FortTurret));   // турели укреплений, как в AssaultLink
        s.AttackerHero = new BCard(CardCatalog.HeroAttack);
        s.DefenderHero = new BCard(CardCatalog.HeroDefense);
        return s;
    }

    static double Rate(int atk, int fort, int pop, bool mid, int defRes, int n)
    {
        int win = 0;
        for (int i = 0; i < n; i++)
        {
            var s = Build(i * 7919 + atk * 31 + fort * 101 + pop, atk, fort, pop, mid, defRes);
            BattleState st; BattleAI.AutoResolve(s, out st);
            if (st.Result.AttackerWon) win++;
        }
        return win * 100.0 / n;
    }

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "table";
        int n = args.Length > 1 ? int.Parse(args[1]) : 300;
        if (mode == "factions") { Factions(n); return; }
        foreach (bool mid in new[] { false, true })
        foreach (int pop in new[] { 8, 20 })
        {
            Console.WriteLine($"== {(mid ? "середина партии" : "начало партии")}, жителей {pop}, резерв защитника 12 ==");
            Console.WriteLine("десант:   " + string.Join("", Array.ConvertAll(new[] { 4, 8, 12, 16, 20 }, a => $"{a,6}")));
            for (int fort = 0; fort <= 3; fort++)
            {
                var row = $"укр. {fort}:  ";
                foreach (int a in new[] { 4, 8, 12, 16, 20 }) row += $"{Rate(a, fort, pop, mid, 12, n),5:0}%";
                Console.WriteLine(row);
            }
        }
    }

    static void Factions(int n)
    {
        string[] facs = { "astrea", "xarn", "aquila", "iridia", "terraan", "nol" };
        var aw = new Dictionary<string, int>(); var an = new Dictionary<string, int>(); var dw = new Dictionary<string, int>(); var dn = new Dictionary<string, int>();
        foreach (var f in facs) { aw[f] = an[f] = dw[f] = dn[f] = 0; }
        for (int i = 0; i < n; i++)
        {
            var s = Build(i * 17 + 3, 14, 1, 12, true, 12);   // десант 14, укрепления 1, 12 жителей
            BattleState st; BattleAI.AutoResolve(s, out st);
            string a = s.Attacker.Faction, d = s.Defender.Faction;
            an[a]++; dn[d]++; if (st.Result.AttackerWon) aw[a]++; else dw[d]++;
        }
        foreach (var f in facs) Console.WriteLine($"{f,-8} атака {aw[f] * 100.0 / an[f],5:0.0}%   оборона {dw[f] * 100.0 / dn[f],5:0.0}%");
    }
}
