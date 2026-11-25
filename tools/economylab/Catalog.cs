using System;
using System.Collections.Generic;
using System.Linq;
using Production.Economy;
using PryingEyes.Cattedrale;

// The game's catalog of the cathedral, checked from the same source file the game compiles (Rules/Cathedral/Catalogo.cs).
static partial class Program
{
    // The enemy mix of a wave per branch, roughly what RoundDirector.PickSurvivalType draws (themed walkers replace monks and abbots).
    static readonly Dictionary<string, double>[] Mix =
    {
        new Dictionary<string, double> { ["Monaco"] = .55, ["Abate"] = .10, ["Occhio"] = .10, ["Veglia"] = .06, ["Medusa"] = .04, ["Portachiavi"] = .05, ["Flagellante"] = .06, ["Appestato"] = .04 },
        new Dictionary<string, double> { ["Maschera"] = .55, ["Direttore"] = .10, ["Occhio"] = .10, ["Veglia"] = .06, ["Medusa"] = .04, ["Portachiavi"] = .05, ["Flagellante"] = .06, ["Appestato"] = .04 },
        new Dictionary<string, double> { ["Infermiere"] = .55, ["Primario"] = .10, ["Occhio"] = .10, ["Veglia"] = .06, ["Medusa"] = .04, ["Portachiavi"] = .05, ["Flagellante"] = .06, ["Appestato"] = .04 },
        new Dictionary<string, double> { ["Monaco"] = .55, ["Abate"] = .10, ["Occhio"] = .10, ["Veglia"] = .06, ["Medusa"] = .04, ["Portachiavi"] = .05, ["Flagellante"] = .06, ["Appestato"] = .04 },
    };

    /// <summary>Kills of one expedition that clears three waves per level (nine waves) plus the boss wave: the wave sizes of the game's
    /// RoundDirector.SurvivalTarget for one player.</summary>
    static int KillsPerExpedition()
    {
        int kills = 0;
        for (int round = 1; round <= 9; round++) kills += Math.Max(20, Math.Min(100, 16 + round * 4));
        return kills + 20;
    }

    static void Catalog()
    {
        WorkTree tree = null;
        try { tree = Opere.Albero(); } catch (Exception ex) { Check("CATALOG_WORKS_VALID", false, ex.Message, "the works form a valid tree"); return; }
        Check("CATALOG_WORKS_VALID", tree.All.Count == Opere.Tutte.Length && tree.All.Count >= 16, tree.All.Count + " works", "a valid tree of all the works");

        var unknown = Opere.Tutte.SelectMany(o => o.costo.Ids).Where(id => !Risorse.IsMaterial(id)).Distinct().ToList();
        Check("CATALOG_COSTS_USE_MATERIALS", unknown.Count == 0, unknown.Count == 0 ? "all known" : string.Join(",", unknown), "every cost is in known materials");

        var exclusives = new HashSet<string>(Rami.Tutti.Where(r => r.esclusiva != null).Select(r => r.esclusiva));
        var dropped = new HashSet<string>();
        foreach (var tipo in Bottino.Tipi) foreach (var r in Risorse.Tutte) if (Bottino.Atteso(tipo, r.id) > 0) dropped.Add(r.id);
        var unobtainable = Opere.Tutte.SelectMany(o => o.costo.Ids).Distinct()
            .Where(id => Risorse.Of(id).grado == Grado.Boss ? !exclusives.Contains(id) : !dropped.Contains(id)).ToList();
        Check("CATALOG_RESOURCES_OBTAINABLE", unobtainable.Count == 0, unobtainable.Count == 0 ? $"{dropped.Count} dropped, {exclusives.Count} from bosses" : string.Join(",", unobtainable),
            "every material in a cost drops from enemies, or from a boss when it is a boss resource");
        var bossDropped = dropped.Where(id => Risorse.Of(id).grado == Grado.Boss).ToList();
        Check("CATALOG_BOSS_RESOURCES_EXCLUSIVE", bossDropped.Count == 0 && exclusives.Count == 4, bossDropped.Count == 0 ? "only bosses" : string.Join(",", bossDropped),
            "the four boss resources never drop from ordinary enemies");

        // each branch opens with a work that costs the trophy of the boss before it
        bool gates = true; var gateLog = new List<string>();
        for (int i = 1; i < 4; i++)
        {
            var gate = Opere.Of(Rami.Tutti[i].cancello);
            bool okGate = gate != null && gate.costo[Rami.Tutti[i - 1].esclusiva] > 0;
            gates &= okGate; gateLog.Add($"{Rami.Tutti[i].cancello}:{(okGate ? "ok" : "MISSING")}");
        }
        gates &= Opere.Of(Rami.Tutti[Rami.Abisso].cancello) != null && Rami.Tutti[0].cancello == null;
        Check("CATALOG_BRANCH_GATES", gates, string.Join(" ", gateLog), "branch k opens with a work that costs the trophy of boss k-1; the first is open");

        Check("CATALOG_LEVELS", Rami.Piano(1, 1) == 4 && Rami.RamoDi(3) == 0 && Rami.RamoDi(4) == 1 && Rami.LivelloDi(6) == 3 && Rami.LivelloDelBoss(9) && !Rami.LivelloDelBoss(10) &&
            Rami.RamoDi(40) == Rami.Abisso && Rami.BossDi(13).indice == 0 && Rami.BossDi(16).indice == 1, Rami.NomeLivello(5), "three levels per branch, boss on the third");

        Market market = null;
        try { market = Listino.Nuovo(); } catch (Exception ex) { Check("CATALOG_MARKET_VALID", false, ex.Message, "valid offers"); return; }
        var missingOffers = Listino.Banchi.SelectMany(b => b.offerte).Where(id => !market.Exists(id)).ToList();
        bool everyOfferOnAStall = market.All.All(o => Listino.Banchi.Any(b => b.offerte.Contains(o.Id)));
        Check("CATALOG_MARKET_VALID", missingOffers.Count == 0 && everyOfferOnAStall, missingOffers.Count == 0 ? market.All.Count + " offers" : string.Join(",", missingOffers), "every stall offer exists and every offer is on a stall");
        var bossTrade = market.All.Where(o => o.Price.Ids.Concat(o.Goods.Ids).Any(id => Risorse.Of(id)?.grado == Grado.Boss)).Select(o => o.Id).ToList();
        Check("CATALOG_MARKET_NO_BOSS_TRADE", bossTrade.Count == 0, bossTrade.Count == 0 ? "none" : string.Join(",", bossTrade), "boss resources are never bought or sold");
        var arbitrage = new List<string>();
        foreach (var r in Risorse.Tutte.Where(x => x.grado == Grado.Comune || x.grado == Grado.Rara))
        {
            var buy = market.All.Where(o => o.Goods[r.id] > 0 && o.Price[Risorse.Oboli] > 0).Select(o => o.Price[Risorse.Oboli] / (double)o.Goods[r.id]).DefaultIfEmpty(double.MaxValue).Min();
            var sell = market.All.Where(o => o.Price[r.id] > 0 && o.Goods[Risorse.Oboli] > 0).Select(o => o.Goods[Risorse.Oboli] / (double)o.Price[r.id]).DefaultIfEmpty(0).Max();
            if (sell >= buy) arbitrage.Add($"{r.id} buy {buy:F2} sell {sell:F2}");
        }
        Check("CATALOG_MARKET_NO_ARBITRAGE", arbitrage.Count == 0, arbitrage.Count == 0 ? "buy > sell for every resource" : string.Join("; ", arbitrage), "selling never pays more than buying costs");

        Check("CATALOG_POINTS_TO_COINS", Listino.OboliDaPunti(3000, 10) == 300 && Listino.OboliDaPunti(3000, 6) == 500 && Listino.OboliDaPunti(-5, 10) == 0 && Listino.OboliDaPunti(100, 0) == 0,
            $"{Listino.OboliDaPunti(3000, 10)} / {Listino.OboliDaPunti(3000, 6)}", "300 at 1/10, 500 at 1/6, nothing for nothing");
        Check("CATALOG_DEATH_POLICY", Risorse.Tenuta(Risorse.Ossa, false) == 0.5 && Risorse.Tenuta(Risorse.Brace, false) == 0 && Risorse.Tenuta(Risorse.Ossa, true) == 1 &&
            Risorse.Tenuta(Risorse.Cuori, true) == 0.5, "commons 1/2, rares 0 (ossuary 1 and 1/2)", "the documented policy");
        var three = Benedizioni.TreA(new Random(5).NextDouble);
        Check("CATALOG_BLESSINGS", three.Distinct().Count() == 3 && three.All(id => Benedizioni.Of(id) != null), string.Join(",", three), "three different known blessings");

        // ---- the balance: how many expeditions a methodical player needs to finish the cathedral
        var rng = new Random(2026);
        var bank = new Ledger();
        var built = new WorkTree(tree.All);
        var bossKills = new int[5];
        int expeditions = 0, kills = KillsPerExpedition();
        var timeline = new List<string>();
        double commonsPerExp = 0, raresPerExp = 0;
        while (built.BuiltCount < built.All.Count && expeditions < 200)
        {
            // the deepest open branch whose boss resource is still needed, otherwise the deepest open one
            int branch = 0;
            for (int b = 3; b >= 0; b--)
                if (Rami.Aperto(b, built.IsBuilt)) { branch = b; break; }
            for (int b = 0; b <= 3; b++)
            {
                if (!Rami.Aperto(b, built.IsBuilt)) continue;
                string ex = Rami.Tutti[b].esclusiva;
                int need = built.All.Where(w => !built.IsBuilt(w.Id)).Sum(w => w.Cost[ex]) - bank.Get(ex);
                if (need > 0) { branch = b; break; }
            }
            expeditions++;
            var loot = Bundle.Empty;
            var mix = Mix[branch];
            for (int k = 0; k < kills; k++)
            {
                double pick = rng.NextDouble(); string type = mix.Keys.Last();
                foreach (var m in mix) { pick -= m.Value; if (pick < 0) { type = m.Key; break; } }
                loot = loot.Plus(Bottino.Uccisione(type, branch, false, 1, rng.NextDouble));
            }
            int piano = Rami.Piano(branch, Rami.LivelliPerRamo);
            loot = loot.Plus(Bottino.Boss(piano, bossKills[branch] == 0, rng.NextDouble));
            bossKills[branch]++;
            commonsPerExp += loot[Risorse.Ossa] + loot[Risorse.Legno] + loot[Risorse.Acciaio];
            raresPerExp += loot[Risorse.Brace] + loot[Risorse.Cuori] + loot[Risorse.Sangue];
            bank.Deposit(loot);
            bank.Deposit(Risorse.Oboli, Listino.OboliDaPunti(2500, built.IsBuilt("tesoro") ? 6 : 10));
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var w in built.All)
                {
                    if (built.TryBuild(w.Id, bank) == WorkTree.Check.Ok)
                    { timeline.Add($"{expeditions}:{w.Id}"); progress = true; }
                }
                // sell surplus commons for coins, then buy missing commons and rares
                foreach (var w in built.All.Where(x => !built.IsBuilt(x.Id) && built.Unlocked(x.Id)))
                    foreach (var lack in bank.Missing(w.Cost))
                    {
                        string offer = "compra_" + lack.Key;
                        if (!market.Exists(offer)) continue;
                        for (int n = 0; n < 3 && bank.Missing(w.Cost)[lack.Key] > 0; n++)
                            if (market.TryBuy(offer, bank) == Market.Check.Ok) progress = true;
                    }
            }
            market.Restock();
        }
        commonsPerExp /= Math.Max(1, expeditions); raresPerExp /= Math.Max(1, expeditions);
        Console.WriteLine($"  balance: {kills} kills per expedition, {commonsPerExp:F0} commons and {raresPerExp:F1} rares on average; boss kills {string.Join("/", bossKills.Take(4))}");
        Console.WriteLine("  timeline: " + string.Join(" ", timeline));
        Check("CATALOG_CATHEDRAL_COMPLETES", built.BuiltCount == built.All.Count && expeditions >= 8 && expeditions <= 30, $"{expeditions} expeditions for {built.BuiltCount}/{built.All.Count} works",
            "a methodical player finishes the cathedral in 8 to 30 expeditions");
        int firstWork = timeline.Count > 0 ? int.Parse(timeline[0].Split(':')[0]) : 99;
        Check("CATALOG_FIRST_WORK_EARLY", firstWork <= 2, $"first work after expedition {firstWork}", "something can be built after one or two expeditions");
    }
}
