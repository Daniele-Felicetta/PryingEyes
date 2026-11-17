# Production Economy

Engine-independent rules for a game economy built on resources: a ledger of balances, one-off constructions with
prerequisites, a market of fixed-price exchanges with stock, weighted loot tables and the extraction rules of a trip away from
a safe place. The caller owns what each resource is called, what a construction does and looks like, the coins and the save
file; the package owns the arithmetic and refuses anything inconsistent before it changes state.

```csharp
var stash = new Ledger(Bundle.Of(("coin", 50)));
var works = new WorkTree(new[] {
    new WorkTree.Work("wall", Bundle.Of(("stone", 10))),
    new WorkTree.Work("tower", Bundle.Of(("stone", 20), ("gem", 1)), "wall") });
var shop = new Market(new[] { new Market.Offer("stone", Bundle.Of(("coin", 10)), Bundle.Of(("stone", 5)), stock: 2) });

shop.TryBuy("stone", stash);                 // Ok: 10 coins out, 5 stone in, 1 left until shop.Restock()
works.TryBuild("wall", stash);               // Unaffordable until 10 stone: nothing is paid
var trip = new Expedition(stash);
trip.Begin();
trip.Pick(new LootTable(.3, 1, new[] { new LootTable.Entry("stone", 1, 1, 2) }).Roll(rng.NextDouble));
trip.Fail(id => id == "stone" ? .5 : 0, out Bundle lost);   // or trip.Extract() to bank everything
```

| Type | What it owns | Guarantees |
| --- | --- | --- |
| `Bundle` | An immutable multiset of ordinal string ids with positive amounts. | Duplicates merge, zeros drop, blank ids and negative amounts are rejected (`ECONOMY_BUNDLE_INVALID`), sums above `int.MaxValue` throw `OverflowException`; equality ignores order. |
| `Ledger` | Mutable balances, never negative. | `TryPay` pays all or nothing; a deposit that would overflow throws before any balance moves; `Changed` reports what moved. |
| `WorkTree` | One-off constructions with a cost and prerequisites. | Unique ids, known prerequisites and no cycles at construction (`ECONOMY_WORK_*`); `TryBuild` pays exactly once; `Restore` skips unknown ids so an old save loads. |
| `Market` | Exchanges at fixed prices with a stock per restock (`-1` unlimited). | A purchase pays, delivers and counts the stock together or not at all, also when the goods would overflow; selling is an offer whose goods are coins. |
| `LootTable` | Weighted drops: a chance per roll, an entry by weight, an amount in `[Min, Max]`. | The random source is the caller's function returning `[0, 1)` (values outside throw), so the same sequence gives the same loot; `Expected` gives the mean for balancing. |
| `Expedition` | What is carried during one trip, at risk until it ends. | `Extract` banks everything; `Fail` keeps a per-resource fraction (rounded down) and returns the rest as lost; `Secure` banks at once; one trip at a time. |

No engine references, clocks, global random source, game ids or currency rules. `tools/economylab` compiles these exact files under
.NET and checks every guarantee above (`python tools/studio.py test economy`, also in the CI gate); `unity/LayoutDemo` consumes the
package in a separate Unity project (`EconomyDemo.cs`: a base to rebuild, a market with stock, a raid that is safe only when you come
back). The game that uses it keeps its catalog (resources, constructions, offers, loot tables) in its own code; the lab compiles that
catalog too and simulates how many trips a methodical player needs to finish everything.
