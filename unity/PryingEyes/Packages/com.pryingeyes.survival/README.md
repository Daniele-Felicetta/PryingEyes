# Production Survival

Engine-independent wave scheduling. The package owns unspent spawn budget, pacing
and a concurrent population limit. The caller owns enemy creation, death tracking,
spawn locations, difficulty, rewards and the break between waves.

```csharp
var wave = new WaveSchedule(total: 48, populationLimit: 24, spawnInterval: .4f);

// Once per simulation tick; living is the actual current population.
if (wave.Advance(deltaSeconds, living) && TryCreateEnemy())
    wave.CommitSpawn();

bool finished = wave.Complete(living);
int enemiesRemaining = wave.Pending + living;
```

Call `CommitSpawn` only after a successful creation and once per eligible tick.
Failed creation leaves budget available for a subsequent attempt. A full
population pauses spawning; a long delay does not accumulate a burst when a slot
opens. Completion requires no pending budget and no living enemies. Supply finite
nonnegative elapsed seconds and a nonnegative population; total, population limit
and interval must be positive. Invalid values throw before changing state.

No engine references, global clocks, random source, game IDs, enemy types or
currency rules are required. `tools/survivallab` compiles this exact source under
.NET; `unity/LayoutDemo` consumes the package in a separate Unity project with an
interactive population simulation. The city's profile and its Unity adapter live
in the game rather than in this package.

## Paid area access

`AreaAccess` owns an adjacency graph and the set of open areas. Supply unique area
IDs, nonnegative prices, bidirectional links and at least one initially open area.
Every declared area must be reachable from an initial area. Cycles and multiple
entrances are allowed; unknown references, duplicate IDs and disconnected areas
are rejected during construction. Input collections are copied into owned state.

`TryBuy(from, destination, balance, out charged)` opens an adjacent closed area
only when the source is open and the supplied balance covers its price. Success
returns the charge exactly once. Failure changes neither ownership nor the output
charge, which is zero. The caller deducts currency, checks interaction distance,
view direction and visibility, and opens every physical entrance for that area.
`IsOpen`, `CanBuy` and `Price` expose the resulting state; querying an unknown price
throws. This model is used on a single simulation thread. Network replication,
save storage, rendering, colliders and game-specific initial/free-area policy are
outside it.

The city adapter registers every priced entrance before the navigation build and
uses the existing animated shutter with explicitly supplied city materials. The
independent Unity consumer has a paid-access simulation using the same model,
with a shop-to-roof progression and a caller-owned points balance.
