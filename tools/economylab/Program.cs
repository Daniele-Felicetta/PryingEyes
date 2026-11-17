using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Production.Economy;

// The economy rules outside Unity: the reusable package (ledger, constructions, market, loot, expeditions) and the game's catalog of the
// cathedral (resources, works, market, drop tables) compiled from the same files the game uses. Usage: dotnet run -c Release -- [report.json]
static partial class Program
{
    static readonly List<object> findings = new List<object>();
    static bool ok = true;
    static string subject = "economy";

    static void Check(string code, bool passed, string observed, string expected)
    {
        ok &= passed;
        findings.Add(new { code, subject, passed, observed, expected });
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {code}: {observed}");
    }

    static void Reject<T>(string code, Action action) where T : Exception
    {
        bool rejected = false;
        try { action(); } catch (T) { rejected = true; } catch (Exception) { rejected = false; }
        Check(code, rejected, rejected.ToString(), typeof(T).Name + " thrown");
    }

    static int Main(string[] args)
    {
        subject = "bundle"; Bundles();
        subject = "ledger"; Ledgers();
        subject = "work tree"; Works();
        subject = "market"; Markets();
        subject = "loot"; Loot();
        subject = "expedition"; Expeditions();
        subject = "catalog"; Catalog();
        var report = new { schemaVersion = 1, operation = "economy.rules", ok, findings };
        if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(ok ? "ECONOMY_RULES_PASS" : "ECONOMY_RULES_FAIL");
        return ok ? 0 : 1;
    }

    static void Bundles()
    {
        var b = Bundle.Of(("bone", 1), ("wood", 2), ("bone", 3));
        Check("BUNDLE_MERGES_DUPLICATES", b["bone"] == 4 && b["wood"] == 2 && b.Count == 2, b.ToString(), "bone:4, wood:2");
        Check("BUNDLE_ORDER_INDEPENDENT", b.Equals(Bundle.Of(("wood", 2), ("bone", 4))) && b.GetHashCode() == Bundle.Of(("wood", 2), ("bone", 4)).GetHashCode(),
            "same contents", "equal bundles and hashes");
        Check("BUNDLE_DROPS_ZERO", Bundle.Of(("bone", 0)).IsEmpty && Bundle.Empty.Total == 0, "zero amount", "empty bundle");
        Check("BUNDLE_SCALES_DOWN", Bundle.Of(("bone", 5), ("wood", 1)).Scaled(0.5).Equals(Bundle.Of(("bone", 2))), "5 and 1 halved", "bone:2 (floor)");
        Check("BUNDLE_MINUS_CLAMPS", Bundle.Of(("bone", 5), ("wood", 1)).Minus(Bundle.Of(("bone", 7))).Equals(Bundle.Of(("wood", 1))), "5-7 bones", "never below zero");
        Check("BUNDLE_PLUS", b.Plus(Bundle.Of(("steel", 1))).Total == 7, "4+2+1", "7");
        Reject<ArgumentException>("BUNDLE_REJECTS_NEGATIVE", () => Bundle.Of(("bone", -1)));
        Reject<ArgumentException>("BUNDLE_REJECTS_BLANK", () => Bundle.Of((" ", 1)));
        Reject<OverflowException>("BUNDLE_REJECTS_OVERFLOW", () => Bundle.Of(("bone", int.MaxValue), ("bone", 1)));
        Reject<ArgumentException>("BUNDLE_REJECTS_BAD_FACTOR", () => b.Scaled(double.NaN));
    }

    static void Ledgers()
    {
        var l = new Ledger(Bundle.Of(("bone", 5), ("wood", 1)));
        int events = 0; l.Changed += _ => events++;
        Check("LEDGER_PAYMENT_ATOMIC", !l.TryPay(Bundle.Of(("bone", 3), ("wood", 2))) && l.Get("bone") == 5 && l.Get("wood") == 1 && events == 0,
            l.Snapshot().ToString(), "unpayable cost changes nothing");
        Check("LEDGER_MISSING", l.Missing(Bundle.Of(("bone", 3), ("wood", 2), ("ember", 1))).Equals(Bundle.Of(("wood", 1), ("ember", 1))), "lack of wood and ember", "wood:1, ember:1");
        Check("LEDGER_EXACT_PAYMENT", l.TryPay(Bundle.Of(("bone", 5), ("wood", 1))) && l.Snapshot().IsEmpty && events == 1, l.Snapshot().ToString(), "empty after exact payment");
        Check("LEDGER_FREE_PAYMENT", l.TryPay(Bundle.Empty), "empty cost", "always payable");
        var full = new Ledger(Bundle.Of(("bone", int.MaxValue)));
        bool overflow = false;
        try { full.Deposit(Bundle.Of(("wood", 1), ("bone", 1))); } catch (OverflowException) { overflow = true; }
        Check("LEDGER_OVERFLOW_UNTOUCHED", overflow && full.Get("wood") == 0 && full.Get("bone") == int.MaxValue, $"wood {full.Get("wood")}", "overflow throws before any balance moves");
        var t = new Ledger(Bundle.Of(("bone", 2)));
        var all = t.TakeAll();
        Check("LEDGER_TAKE_ALL", all["bone"] == 2 && t.Get("bone") == 0, all.ToString(), "returns and clears");
        t.Restore(Bundle.Of(("wood", 3)));
        Check("LEDGER_RESTORE", t.Get("wood") == 3 && t.Get("bone") == 0, t.Snapshot().ToString(), "replaces every balance");
        Check("LEDGER_WITHDRAW", !t.TryWithdraw("wood", 4) && t.TryWithdraw("wood", 3) && t.Get("wood") == 0, "3 of 3", "withdraw only what exists");
    }

    static void Works()
    {
        var tree = new WorkTree(new[]
        {
            new WorkTree.Work("roof", Bundle.Of(("wood", 10))),
            new WorkTree.Work("altar", Bundle.Of(("bone", 5), ("heart", 1)), "roof"),
            new WorkTree.Work("window", Bundle.Of(("gem", 2)), "roof", "altar"),
        });
        var wallet = new Ledger(Bundle.Of(("wood", 9), ("bone", 5), ("heart", 1)));
        Check("WORK_LOCKED", tree.CanBuild("altar", wallet) == WorkTree.Check.Locked, tree.CanBuild("altar", wallet).ToString(), "prerequisite missing");
        Check("WORK_UNAFFORDABLE_NO_CHARGE", tree.TryBuild("roof", wallet) == WorkTree.Check.Unaffordable && wallet.Get("wood") == 9 && !tree.IsBuilt("roof"),
            wallet.Get("wood").ToString(), "nothing paid");
        wallet.Deposit("wood", 1);
        Check("WORK_BUILT_ONCE", tree.TryBuild("roof", wallet) == WorkTree.Check.Ok && wallet.Get("wood") == 0 && tree.TryBuild("roof", wallet) == WorkTree.Check.AlreadyBuilt,
            "roof", "paid once, then already built");
        Check("WORK_UNLOCKS", tree.Unlocked("altar") && !tree.Unlocked("window") && tree.TryBuild("altar", wallet) == WorkTree.Check.Ok && tree.Unlocked("window"),
            "altar after roof, window after both", "unlocked in order and built");
        Check("WORK_UNKNOWN", tree.CanBuild("tower", wallet) == WorkTree.Check.Unknown, "tower", "unknown");
        Check("WORK_ORDER", string.Join(",", tree.BuiltIds()) == "roof,altar", string.Join(",", tree.BuiltIds()), "declaration order");
        int skipped = tree.Restore(new[] { "altar", "ghost", "roof" });
        Check("WORK_RESTORE_SKIPS_UNKNOWN", skipped == 1 && tree.IsBuilt("altar") && tree.IsBuilt("roof") && tree.BuiltCount == 2, skipped.ToString(), "one unknown id skipped");
        Reject<ArgumentException>("WORK_REJECTS_CYCLE", () => new WorkTree(new[] { new WorkTree.Work("a", Bundle.Empty, "b"), new WorkTree.Work("b", Bundle.Empty, "a") }));
        Reject<ArgumentException>("WORK_REJECTS_SELF", () => new WorkTree(new[] { new WorkTree.Work("a", Bundle.Empty, "a") }));
        Reject<ArgumentException>("WORK_REJECTS_DUPLICATE", () => new WorkTree(new[] { new WorkTree.Work("a", Bundle.Empty), new WorkTree.Work("a", Bundle.Empty) }));
        Reject<ArgumentException>("WORK_REJECTS_UNKNOWN_REQUIREMENT", () => new WorkTree(new[] { new WorkTree.Work("a", Bundle.Empty, "z") }));
        Reject<ArgumentException>("WORK_REJECTS_MISSING_COST", () => new WorkTree(new[] { new WorkTree.Work("a", null) }));
    }

    static void Markets()
    {
        var m = new Market(new[]
        {
            new Market.Offer("buy-bone", Bundle.Of(("coin", 3)), Bundle.Of(("bone", 1)), 2),
            new Market.Offer("sell-bone", Bundle.Of(("bone", 10)), Bundle.Of(("coin", 12))),
            new Market.Offer("ampoule", Bundle.Of(("coin", 40)), Bundle.Of(("ampoule", 1)), 1),
        });
        var w = new Ledger(Bundle.Of(("coin", 7)));
        Check("MARKET_BUY", m.TryBuy("buy-bone", w) == Market.Check.Ok && w.Get("coin") == 4 && w.Get("bone") == 1 && m.Left("buy-bone") == 1, w.Snapshot().ToString(), "paid 3, got 1 bone");
        Check("MARKET_SOLD_OUT", m.TryBuy("buy-bone", w) == Market.Check.Ok && m.TryBuy("buy-bone", w) == Market.Check.SoldOut && w.Get("coin") == 1,
            m.Left("buy-bone").ToString(), "stock of 2 per restock");
        m.Restock();
        Check("MARKET_RESTOCK", m.Left("buy-bone") == 2 && m.Left("sell-bone") == -1, m.Left("buy-bone").ToString(), "stock refilled");
        Check("MARKET_UNAFFORDABLE", m.TryBuy("ampoule", w) == Market.Check.Unaffordable && w.Get("coin") == 1 && m.Left("ampoule") == 1, "1 coin", "nothing changes");
        var stash = new Ledger(Bundle.Of(("bone", 25)));
        var purse = new Ledger();
        Check("MARKET_SELL_INTO", m.TryBuy("sell-bone", stash, purse) == Market.Check.Ok && m.TryBuy("sell-bone", stash, purse) == Market.Check.Ok &&
            stash.Get("bone") == 5 && purse.Get("coin") == 24 && m.TryBuy("sell-bone", stash, purse) == Market.Check.Unaffordable, purse.Snapshot().ToString(), "two sales of ten bones");
        m.RestoreLeft("buy-bone", 9);
        Check("MARKET_RESTORE_CLAMPS", m.Left("buy-bone") == 2, m.Left("buy-bone").ToString(), "clamped to the declared stock");
        Check("MARKET_UNKNOWN", m.CanBuy("absent", w) == Market.Check.Unknown, "absent", "unknown");
        Reject<ArgumentException>("MARKET_REJECTS_EMPTY_OFFER", () => new Market(new[] { new Market.Offer("x", Bundle.Empty, Bundle.Empty) }));
        Reject<ArgumentException>("MARKET_REJECTS_BAD_STOCK", () => new Market(new[] { new Market.Offer("x", Bundle.Of(("c", 1)), Bundle.Empty, -2) }));
        Reject<ArgumentException>("MARKET_REJECTS_DUPLICATE", () => new Market(new[] { new Market.Offer("x", Bundle.Of(("c", 1)), Bundle.Empty), new Market.Offer("x", Bundle.Of(("c", 1)), Bundle.Empty) }));
        var huge = new Ledger(Bundle.Of(("coin", 5), ("bone", int.MaxValue)));
        bool thrown = false;
        try { m.TryBuy("buy-bone", huge); } catch (OverflowException) { thrown = true; }
        Check("MARKET_OVERFLOW_KEEPS_PAYMENT", thrown && huge.Get("coin") == 5 && m.Left("buy-bone") == 2, huge.Get("coin").ToString(), "an undeliverable purchase takes no payment");
    }

    static void Loot()
    {
        var table = new LootTable(0.3, 1, new[] { new LootTable.Entry("bone", 3, 1, 2), new LootTable.Entry("wood", 1, 1, 1) });
        var seqA = new Random(7); var seqB = new Random(7);
        var a = new List<string>(); var b = new List<string>();
        for (int i = 0; i < 50; i++) { a.Add(table.Roll(seqA.NextDouble).ToString()); b.Add(table.Roll(seqB.NextDouble).ToString()); }
        Check("LOOT_DETERMINISTIC", string.Join("|", a) == string.Join("|", b), "same seed twice", "identical drops");
        var rng = new Random(11);
        long bones = 0, woods = 0, hits = 0; const int N = 200000; int maxBone = 0, minBone = int.MaxValue;
        for (int i = 0; i < N; i++)
        {
            var r = table.Roll(rng.NextDouble);
            if (!r.IsEmpty) hits++;
            bones += r["bone"]; woods += r["wood"];
            if (r["bone"] > 0) { maxBone = Math.Max(maxBone, r["bone"]); minBone = Math.Min(minBone, r["bone"]); }
        }
        double meanBone = bones / (double)N, expBone = table.Expected("bone");
        Check("LOOT_MEAN_MATCHES_EXPECTED", Math.Abs(meanBone - expBone) / expBone < 0.02 && Math.Abs(woods / (double)N - table.Expected("wood")) / table.Expected("wood") < 0.04,
            $"bone {meanBone:F4} vs {expBone:F4}", "within 2% over 200k rolls");
        Check("LOOT_CHANCE", Math.Abs(hits / (double)N - 0.3) < 0.005, (hits / (double)N).ToString("F4"), "0.3");
        Check("LOOT_AMOUNT_RANGE", minBone == 1 && maxBone == 2, $"{minBone}..{maxBone}", "1..2");
        long doubled = 0; var rng2 = new Random(3);
        for (int i = 0; i < N; i++) if (!table.Roll(rng2.NextDouble, 2).IsEmpty) doubled++;
        Check("LOOT_CHANCE_MULTIPLIER", Math.Abs(doubled / (double)N - 0.6) < 0.006, (doubled / (double)N).ToString("F4"), "0.6 with chance x2");
        var sure = new LootTable(1, 3, new[] { new LootTable.Entry("gem", 1, 2, 2) });
        Check("LOOT_ROLLS_AND_AMOUNT_MULTIPLIER", sure.Roll(new Random(1).NextDouble, 1, 1.5)["gem"] == 9, sure.Roll(new Random(1).NextDouble, 1, 1.5).ToString(), "3 rolls of floor(2x1.5)=3");
        Check("LOOT_NONE", new LootTable(0, 1, new LootTable.Entry[0]).Roll(new Random(1).NextDouble).IsEmpty, "chance 0", "nothing");
        Reject<ArgumentException>("LOOT_RANDOM_OUT_OF_RANGE", () => table.Roll(() => 1.0));
        Reject<ArgumentException>("LOOT_REJECTS_BAD_CHANCE", () => new LootTable(1.5, 1, new[] { new LootTable.Entry("x", 1) }));
        Reject<ArgumentException>("LOOT_REJECTS_BAD_RANGE", () => new LootTable(1, 1, new[] { new LootTable.Entry("x", 1, 3, 2) }));
        Reject<ArgumentException>("LOOT_REJECTS_BAD_WEIGHT", () => new LootTable(1, 1, new[] { new LootTable.Entry("x", 0) }));
        Reject<ArgumentException>("LOOT_REJECTS_EMPTY_TABLE", () => new LootTable(0.5, 1, new LootTable.Entry[0]));
    }

    static void Expeditions()
    {
        var bank = new Ledger(Bundle.Of(("bone", 1)));
        var e = new Expedition(bank);
        Reject<InvalidOperationException>("EXPEDITION_PICK_INACTIVE", () => e.Pick(Bundle.Of(("bone", 1))));
        e.Begin();
        Reject<InvalidOperationException>("EXPEDITION_ONE_AT_A_TIME", () => e.Begin());
        e.Pick(Bundle.Of(("bone", 4), ("heart", 2)));
        e.Secure(Bundle.Of(("gem", 1)));
        Check("EXPEDITION_SECURE_IMMEDIATE", bank.Get("gem") == 1 && bank.Get("bone") == 1 && e.Carried.Get("bone") == 4, bank.Snapshot().ToString(), "secured loot is banked at once");
        var moved = e.Extract();
        Check("EXPEDITION_EXTRACT", moved.Equals(Bundle.Of(("bone", 4), ("heart", 2))) && bank.Get("bone") == 5 && bank.Get("heart") == 2 && e.Carried.Snapshot().IsEmpty && !e.Active &&
            e.Banked.Equals(Bundle.Of(("bone", 4), ("heart", 2), ("gem", 1))), bank.Snapshot().ToString(), "everything carried is banked");
        e.Begin();
        e.Pick(Bundle.Of(("bone", 5), ("heart", 2)));
        var kept = e.Fail(id => id == "bone" ? 0.5 : 0, out var lost);
        Check("EXPEDITION_FAIL_POLICY", kept.Equals(Bundle.Of(("bone", 2))) && lost.Equals(Bundle.Of(("bone", 3), ("heart", 2))) && bank.Get("bone") == 7 && bank.Get("heart") == 2 && !e.Active,
            $"kept {kept}, lost {lost}", "half the bones kept (floor), hearts lost");
        e.Begin();
        e.Pick(Bundle.Of(("bone", 1)));
        Reject<ArgumentException>("EXPEDITION_REJECTS_BAD_FRACTION", () => e.Fail(_ => 1.5, out _));
        Check("EXPEDITION_BAD_FRACTION_UNTOUCHED", e.Active && e.Carried.Get("bone") == 1, e.Carried.Snapshot().ToString(), "a rejected policy changes nothing");
    }
}
