# Production Racing

Engine-independent rules for racing motorcycles on closed circuits: the physics of a bike, a circuit made out of a seed, the racing line
that computer riders follow, the driving line that shows a person where to brake, the computer rider, the rules of a race (grid and lights,
laps, timing, contacts, falls, standings) and the recording of a race for replays and ghosts. The caller owns which bikes exist (their ids,
names, classes and roster numbers), what a circuit is called, how anything looks or sounds, the screens and the network; the package owns
the arithmetic and the rules, and gives the same ride and the same race on every machine.

```csharp
var circuit = CircuitGen.Generate(7);                          // the same seed makes the same circuit on every machine
var bike = new BikeSpec { id = "demo", name = "Demo" };         // SI units; the defaults are a big sports bike
var race = new Race(circuit, laps: 2, seed: 7);
var rider = race.AddAi("Ada", bike, skin: 0, seed: 1, pace: 0.96f, mistakes: 0f);
race.PlaceAll(new List<Racer> { rider });
race.StartCountdown();
while (race.phase != RacePhase.Over) race.Step(1f / 60f);       // small fixed steps: the same inputs give the same race
// rider.bestLap, rider.finishTime, rider.crashes ...
```

| Type | What it owns | Guarantees |
| --- | --- | --- |
| `RacingMath`, `Rng` | Small math (clamp, lerp, angles, table lookup) and a seeded random generator (xorshift32). | The same seed gives the same sequence; there is no clock and no global random source. |
| `BikeSpec`, `BikeFigures` | Everything that makes one bike different from another (mass and geometry, torque curve, gearbox, brakes, drag, tyres, lean), in SI units; and the figures of a bike worked out by riding it (power, top speed, 0-100, braking, agility, grip). | The figures are measured on the real model, not taken from a formula, so they cannot disagree with the ride. The package ships no bike: the caller keeps its own. |
| `BikeModel`, `BikeInputs`, `IGround`, `GroundSample`, `SurfaceKind`, `CrashKind`, `RiderStyle` | The physics of one bike: engine and gearbox, tyres that share their grip between braking, driving and cornering, weight that moves between the wheels (wheelies and stoppies), lean, slides and falls, ABS, traction control and anti-wheelie. | Small fixed steps inside every call, so the same inputs give the same ride on every machine (`BIKE_IS_DETERMINISTIC`); the ground is any `IGround`, a flat clean road included. |
| `Circuit`, `CircuitGen`, `Proj` | A closed circuit out of a seed: a centreline every two metres with width, hills, camber, kerbs, run-off, gravel traps and the barrier, and every question the physics and the riders ask of it. A `Circuit` is also the ground a bike rides on. | The same seed gives the same circuit (`CIRCUIT_SAME_SEED_SAME_CIRCUIT`); it closes, its roads keep apart and the grid sits on a straight (`CIRCUIT_CLOSES`, `CIRCUIT_ROADS_KEEP_APART`, `CIRCUIT_GRID_IS_ON_A_STRAIGHT`). The name comes from a function the caller passes; without one it is `Circuit <seed>`. |
| `RacingLine`, `BikeCurves` | The fastest line a given bike can ride on a given circuit, with a speed plan, from the curves the bike measures on itself. | A bike with other brakes or another top speed gets a line of its own; the line stays on the road (`RACE_LINE_STAYS_ON_THE_ROAD`). |
| `DrivingLine`, `BrakeCue`, `BrakeCues` | What a person is shown of the plan: go, lift or brake along the road ahead, and the cue for the next braking zone (the metres to it and the speed it is for), as data. | No random numbers: the same line gives the same advice. The words and the drawing are the caller's. |
| `BikeDriver`, `KeyShaper` | The computer rider (follows the line, holds the planned speed, goes around slower bikes, overcooks a corner now and then) and what a hand does to the keys of a keyboard (ramps, a cap on the steering at speed). | Same physics as the person's bike; a lone rider finishes within a tenth of what the plan promises (`RACE_AI_RIDES_THE_PLAN`); keys never leap whatever the frame rate (`KEYS_NEVER_LEAP_WHATEVER_THE_FRAME_RATE`). |
| `Race`, `Racer`, `Result` | The rules of a race: grid and lights, laps, timing loops for the gaps, slipstream, contacts, falls and the way back onto the road, standings. Riders on other machines are fed in as poses and ranked by the same rules. | A full grid finishes and keeps apart (`RACE_FULL_GRID_FINISHES`, `RACE_RIDERS_KEEP_APART`); a nudge is not a fall (`CONTACT_*`); the end of a shared race is decided once, on one machine, and applied on the others (`COOP_END_IS_THE_HOSTS_WORD`). |
| `PoseTape`, `PoseFrame`, `LapTrace`, `Ghost` | A race or a lap as a tape of poses, the clock against the distance of a lap, and the bytes of a ghost. | A ghost is read back or refused, never thrown at the caller: damaged or nonsense bytes and a ghost of another circuit are refused (`TIMING_GHOST_REFUSES_BAD_FILES`, `TIMING_GHOST_FITS_ONLY_ITS_CIRCUIT`). Memory streams only: the package never touches a file. |
| `RaceRecording`, `ReplayEdit`, `ShotCamera`, `Shot`, `CamPose` | A recorded race, the television director that cuts it into shots (the falls, the overtakes, the battles, the finish) and where the cameras stand. | The same race and seed give the same edit, which covers the race without a gap (`REPLAY_EDIT_IS_REPEATABLE_AND_DEPENDS_ON_THE_SEED`, `REPLAY_EDIT_COVERS_THE_RACE_WITHOUT_A_GAP`). |

No engine references, clocks, global random source or files. `tools/tracklab` compiles these exact files under .NET together with the
game's own content (its nine bikes, the circuits of its catalogue, licences, cups, workshop and career) and checks every guarantee above
(`python tools/studio.py test track`, also in the CI gate); `unity/LayoutDemo` consumes the package in a separate Unity project
(`RacingDemo.cs`: a bike that rides a circuit made out of a seed, with no content of any game, and prints its lap time).

**Status.** One game has used this package so far, the track mode of Prying Eyes, so its interface is shaped by that one game and a
revision is expected when a second one asks for it. What still looks like that game:

- `Racer` and `Race` speak the vocabulary of its cooperative race: `netSlot`, `friend`, `final`, `AddRemote`, `ApplyResults` and
  `CollectResults` assume one machine that decides the end and up to three friends, whose bikes arrive as poses.
- `BikeSpec.meshStyle`, `meshScale` and `skin` are hints for the game's drawing code (which of its bikes, how big, which colours); the
  physics never reads them. `Racer.bikeIndex` is the caller's roster number, kept for the views.
- `EngineKind` names five voices (a four-cylinder, a V-twin, a triple and two two-strokes): which sound each one means is the game's.
- A ghost file starts with the signature of that game's ghosts, and a ghost is either a lap from a standing start or a flying lap.
- The aid levels that `DrivingLine.Shows` and `BrakeCues` read (none, and two levels with aids) and the shaping of a keyboard are the
  numbers that game found right for its players.
