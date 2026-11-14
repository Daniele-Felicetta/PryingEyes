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
