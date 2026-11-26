# Extracting reusable Unity packages: strategy

Which code of the game can become a package that another project could use, in what order, and what has to be true before each move.
It builds on the six packages that exist ([suite-architecture.md](suite-architecture.md)) and on the findings of
[architecture-audit.md](architecture-audit.md) (F8, F14). The boundaries it refers to (B1, B2) are defined in
[modularization.md](modularization.md); the tasks are in [modular-tasks.md](modular-tasks.md).

The short version: by the rule below no code should be made a package today, and on 25 November 2026 the maintainer decided on two
exceptions, the track rules and the weapon rules ([the two exceptions](#the-two-exceptions)); the first is done (T22, the package `Production.Racing`). There is a clear way to get there (the ladder below), one
structural step that makes every later extraction cheap (the game-rules assembly, B2), and a list of candidates with the event that
should trigger each one.

Verified for this page: which files of the game use each package (by search), and that `MapMath.cs`, `MapPen.cs` and `MapTextures.cs`
compile alone as a .NET library (a throwaway project, 0 errors and 0 warnings, SDK 10.0.403 targeting `net8.0`). Not verified: any of
the moves proposed. The two rows added on 25 November 2026 (the track rules and the weapon data) were checked at commit `839571c` by
reading the source, not with a compiler: the 20 track files hold no engine reference (the two that name `UnityEngine` do it in a comment, and
`PryingEyes.Rules` forbids the engine), the mechanism files use only each other and the career files use the mechanism, the desert code names four
types of the track rules (`BikeSpec`, `EngineKind`, `Bikes` and `BikeSheet`: the nine bikes and their sheet) and none of the riding model, and
what `WeaponDef` takes from Unity is three colour fields, the grade colours and two calls of `Mathf.Clamp`. T22 did it for the track rules (its outcome in [modular-tasks.md](modular-tasks.md#t22-the-track-rules-as-a-package) says what the compiler found), and the first step of T23 (26 November 2026) did it for the weapon data: the colours became the package's `Rgba`, the clamp became its own `WeaponMath.Clamp`, and the file reader stayed in the game, because it needs Newtonsoft.Json, which a package may not reference.

## What a package is here

A package under `unity/PryingEyes/Packages/` has four proofs, and the guard (`python tools/check_suite.py`) fails when one is missing:

1. an assembly definition with `noEngineReferences: true` and no references;
2. no game, engine, netcode or UI name in its code;
3. a .NET lab under `tools/` that compiles the same files from the same path and runs their checks without the editor;
4. `unity/LayoutDemo`, a project without the game, that consumes it through a `file:` reference.

This strategy keeps all four. It adds nothing to the definition.

## The ladder

Code climbs one rung at a time, and only when the rung's condition is met:

| Rung | Where the code is | Condition to get there | What it gains |
| --- | --- | --- | --- |
| 0 | gameplay (`Assembly-CSharp`) | none | |
| 1 | game rules, B2 (`Rules/`, `PryingEyes.Rules`; exists since T15) | it compiles without the engine, and a lab compiles it and checks it | checks in seconds without a build; the compiler keeps the engine out |
| 2 | a `Production.*` package, B1 | **two consumers** that are not wrappers of each other (two modes, or the game and a tool or another project), **and** a public API that can be written without a noun of the game | the four proofs; usable by another project |
| 3 | its own repository or registry | a second project exists that needs it | a history and a version of its own |

Why two consumers before rung 2: an interface drawn from one user copies that user. The clearest evidence is in the repository
([below](#packages-the-game-does-not-use)): the two packages that were shaped by the city prototype alone are not used by the game that
ships.

Why rung 1 exists at all: it gives the valuable part of a package (engine-free, checked by a lab, protected by the compiler) to code
whose vocabulary is the game's, without pretending it is reusable. Most of the pure code of the game belongs there and should stay there.

## The two exceptions

On 25 November 2026 the maintainer decided that the bikes and the gunplay become reusable packages before a second game asks for them
(T22 and T23). That is an exception to the condition of rung 2. This is what it accepts, and what keeps its cost low:

- **What is accepted.** An interface drawn from one user copies that user, as the packages of the city prototype show. The first version of each
  new package will carry some habits of this game, and the first other game that takes it will probably ask for a revision of the API. Each
  README says that one game has used the package so far, and a revision is expected, not a failure.
- **What keeps the cost low.** Only the mechanism moves, never the content: for the bikes the physics, the rider, the racing line, the race rules,
  the replay and the circuit generator; for the guns the numbers of a weapon and the arithmetic of its damage, spread and recoil. The nine bikes,
  the licences, the career, and the look, the feel and the sound of a gun stay in the game and consume the package. What is closest to the engine
  does not move: the Unity side of the track mode, the viewmodel, the hands, the effects.
- **The second consumer that exists.** For the bikes, the desert garage takes the nine bikes (`Bikes`, which stays in the game) and with them `BikeSpec` and `EngineKind`, the package's own types,
  though not the riding model, which the desert has of its own. The card of T22 said that the desert shows the sheet of the garage (`BikeSheet`) too; the code does not (one comment names it), so that reason to put the sheet in the package fell. For the guns there is none: every mode shares one `Weapon` class, and a second
  mode is not a second consumer of the package.
- **The order.** T22 first, because it is the cheaper test of the method: its files are already engine-free and have a lab. T23 starts when T22
  has merged and its page says that the method worked. T22 is done: its outcome in [modular-tasks.md](modular-tasks.md#t22-the-track-rules-as-a-package) says what it cost and whether T23 can use the same steps.

## Packages the game does not use

Which code consumes each package (searched on 20 November 2026; the row of the weapons again on 26 November 2026, after the first step of T23):

| Package | Used by the game that ships | Used only by the city prototype and the demo |
| --- | --- | --- |
| `Production.Weapons` | 19 files with a `using` of it: `Weapon.cs` and its parts (`Weapon.*.cs`), the catalogue (`Armi/WeaponDefs.cs`, `WeaponCatalog.cs`, `WeaponColors.cs`), the arms (`Braccia/GripHand.cs`, `OffHand.cs`, `ReloadAnims.cs`), `HUD.cs`, `WallBuy.cs`, `PlayerController.cs`, `Survivors/SurvivorBody.cs`, the suites `GunTest.cs` and `GunplayTest.cs`, and the importers `Editor/ArmiImport.cs` and `Editor/ArmiShots.cs`; before T23, only `Weapon.cs` and `Weapon.ReloadHands.cs` | |
| `Production.Economy` | the cathedral (`Cattedrale/`), the expedition, the desert director | |
| `Production.Saves` | characters, runs, the cathedral's file, weapons and magic of a suspended run | |
| `Production.Survival` | | `CityWaves.cs` (`WaveSchedule`), `CityAccess.cs` (`AreaAccess`) |
| `Production.Layout` | | `CityRoom.cs`, `CityAccess.cs`, `Editor/CityBuilder.cs`, `Editor/CityRoomBuilder.cs` |

The game has its own version of both rules of `Production.Survival`: paid areas are `Rules/Plan/RoomAccessState.cs` (prices by distance from
the start, lifts that open with their room, a snapshot for the co-op) and the pace of a wave is in `Proc/RoundDirector.Survival.cs`. The
package and the game can therefore drift apart, and the package is proven by a demo, not by play.

This is not a reason to rewrite either side now. It sets two triggers:

- **the next change to paid areas**, in the game or in the city, first asks whether `RoomAccessState` can become an adapter over
  `AreaAccess` (what is missing in the package: a price that can change after construction, areas that open together, a snapshot);
- **the next change to wave pacing** asks the same of `WaveSchedule`.

If the answer is no twice, the honest move is the other one: say in the package's README that it serves the city prototype, and stop
presenting it as the rule of the game.

## Inventory of candidates

Lines are those of the census at commit `4042709`, but for the rows of the weapons (added on 25 November 2026 with the census at commit `839571c`, and the data rows rewritten on 26 November 2026 with the files as they are in the first pull request of T23) and the two rows of the track rules (T22, 26 November 2026, with the census at the head of its pull request).

| Candidate | Files, lines | Engine-free today | Consumers | What is the game's in it | Rung now | Verdict |
| --- | --- | --- | --- | --- | --- | --- |
| Floor plans (`Rules/Plan/Plan*.cs`, `HubPlan.cs`, `RoundRegions.cs`, `RoomAccessState.cs`) | 9, 3,703 | yes, three labs | Tower, catacombs, desert buildings, co-op peers, Unreal export | themes, room roles, the cathedral | 0 | **rung 1** (T15). Not a package: its nouns are the game's |
| Ink pen (`Rules/Map/MapMath.cs`, `MapPen.cs`, `MapTextures.cs`) | 3, 788 | yes; compiles alone | the floor map | nothing in its dependencies | 0 | **rung 1** (T15); first in line for rung 2 when a second map uses it |
| Map painter (`MapScene.cs`, `MapPainter*.cs`) | 5, 1,102 | yes, `planlab` | the floor map, `planlab mapart` | rooms, props, glyphs of the game | 0 | **rung 1** (T15) |
| Desert generators (`Deserto/Desert{Noise,Clock,World,TerrainGen,MapGen,ScatterGen,Weather,Grade}.cs`) | 8, 1,625 | yes, `desertlab` | the desert | regions, places, the cathedral | 0 | **rung 1** (T15) |
| Character and run records (`Rules/Characters/*Record.cs`, `Leveling.cs`, `CharacterLooks.cs`, `CathedralJson.cs`) | 5, 465 | yes, `saveslab` | saves | the schema of this game | 0 | **rung 1** (T15); the generic part is already `Production.Saves` |
| Cathedral catalog, points, perks (`Catalogo.cs`, `Punti.cs`, `PlayerPerks.cs`) | 3, 545 | yes | cathedral, score | all of it | 0 | **rung 1** (T15) |
| Magic rules (`Magia/Grimoire.cs`, `MagicState.cs`) | 2, 389 | yes, but no lab | the grimoires | all of it | 0 | rung 1 once a lab compiles them; their only check today is `-magiatest` in the player |
| Procedural audio (`SoundGen*.cs`) | 18, 3,987 | almost: `Mathf` 405 times, two other calls | every sound | the sounds themselves; the filters and envelopes are generic | 0 | rung 1 when audio next needs a check that does not need the editor (T18) |
| Track mechanism (`Packages/com.pryingeyes.racing/Runtime/*.cs`; T22) | 14, 3,850 | yes: `noEngineReferences`, and `tracklab` compiles them | the track mode; the desert garage (`BikeSpec`, `EngineKind`); `unity/LayoutDemo` (`RacingDemo`) | the vocabulary of its cooperative race (`netSlot`, `friend`, `final`, `AddRemote`, `ApplyResults`), the view hints of `BikeSpec` (`meshStyle`, `meshScale`, `skin`), the five engine voices of `EngineKind`, the signature and the two kinds of its ghost files, the aid levels of `DrivingLine.Shows` | 2 | **done (T22)**. One game has used it: a revision is expected when a second one does, and the README of the package says so |
| Track content (`Rules/Track/*.cs`) | 11, 1,379 | yes, `tracklab` compiles them | the track mode, all of it; the desert garage, for the nine bikes (`Bikes`) | everything: the nine bikes, the catalogue of circuits and their names, the licences, the championship, the career, the workshop (`Tuning`), the sheet of the garage, the sound of the engines, the words of the braking sign, the roster of a race | 1 | stays at **rung 1**: it is the content of the package's only game |
| Weapon data (`Packages/com.pryingeyes.weapons/Runtime/{WeaponDef,Rgba,WeaponCatalogRules,WeaponMath}.cs`; T23, first step) | 4, 191 | yes: `noEngineReferences`, and `weaponslab` compiles them | every mode, through one `Weapon` class | the Italian words of `GradeName` and `ModeLabel`, the five grade colours, the columns `cityOnly`, `price`, `cls` and the hints `snd`, `sight`, `casing`, `fx` | 2 | **done for the data (T23, first step)**. One game has used it: a revision is expected when a second one does, and the README of the package says so |
| Weapon catalogue reader (`Armi/WeaponDefs.cs`, `Armi/WeaponCatalog.cs`, `Armi/WeaponColors.cs`) | 3, 139 | no: Newtonsoft.Json, `Resources` and `UnityEngine.Color` | every mode | the file, the content hash that the co-op admission compares, the conversion of an `Rgba` to a colour | 0 | stays at rung 0: a package may not reference a library, and the checks that need only numbers are in the package already (`WeaponCatalogRules`) |
| Weapon arithmetic (the number-only parts of `Weapon.cs`; `WeaponState` is a package already) | part of `Weapon.cs` (about 1,500 lines) | no: it calls `Mathf`, the camera and the enemies | every mode, through one `Weapon` class | the 30 weapons, their look and their sound | 0 (`WeaponState`: 2) | **rung 2 for the numbers by the maintainer's decision (T23, second step)**; the Unity layer (hands, viewmodel, effects) stays at rung 0 |
| Paid areas, wave pacing | see above | yes | the game and, separately, the package | prices, lifts | 0 and 2 | converge at the next change, or relabel the package |
| In-player test harness (`Testing/PlayerTest.cs`, `Testing/TestRun.cs`; T07) | 2, 217 | no (it is a `MonoBehaviour`) | this game | nothing by design | 0 | stays; its result file is documented so that it could be lifted later |
| Admission of a co-op session (`Rules/Admission/AdmissionGate.cs`, `SessionAdmission.cs`, `SessionRoster.cs`; T20) | 3, 154 | yes, `admissionlab` | the co-op | the content revision, the four seats of a connection | 0 | **rung 1** (T15) |
| Mesh and texture builders (`MeshBuilder.cs`, `ProcTextures.cs`, `PropMeshes*.cs`, `FacadeKit.cs`) | | no | one each | | 0 | stay. An engine-bound package would need a second kind of rule in the guard; not before a second consumer |
| Platform glue (`Coop/WinWindows.cs`, `PadControls.cs`) | | partly | one each | | 0 | stay |

## The step that makes extraction cheap

Moving the engine-free files that a lab compiles to `Rules/` (B2, task T15) was the only extraction work proposed, and it is done: the 56
files are there, in the assembly `PryingEyes.Rules`; the two packages that the maintainer decided on (T22 and T23) start from there. So now:

- the candidates for rung 2 are a folder, not a search;
- promoting one is a move between two engine-free assemblies that the compiler already keeps clean, plus the README and the demo;
- the guard's package rules apply to `Rules/` with two differences (game names allowed, the `Production.*` packages may be referenced), and
  the checking code is shared (`check_engine_free` in `tools/check_suite.py`).

## Recipes

### Rung 0 to rung 1: a file becomes a game rule

1. Remove what ties it to the engine. If that means a wrapper (the file takes numbers and returns numbers, the caller builds the Unity
   objects), the wrapper stays in gameplay.
2. Move the file and its `.meta` with `git mv` into the matching folder of `Rules/`. Keep its namespace.
3. Add it to the lab project that checks its area (`<Compile Include="...">` with the real path), and write its checks there as findings
   (`code`, `observed`, `expected`), like the other labs.
4. Run the lab, `python tools/check_suite.py`, the unit tests, then a scripts-only build and the smoke walk of `tools/ship.ps1`.
5. Update the pages that name the old path.

### Rung 1 to rung 2: a game rule becomes a package

Only when the two conditions of the ladder hold, or when the maintainer has decided an exception (T22, T23). Then, in one pull request:

1. Choose the package: an existing one if the rule belongs to its subject, a new `com.pryingeyes.<subject>` otherwise (the ids keep the
   game's name on purpose, see [suite-architecture.md](suite-architecture.md)).
2. Move the files to `Packages/<id>/Runtime/`, change the namespace to `Production.<Subject>` and remove every noun of the game from the
   public API. Game content that the rule needs arrives as data from the caller.
3. Give it the four proofs: the assembly definition, the lab project entry, a README that says what it is for and how to call it, and a
   script in `unity/LayoutDemo/Assets/` that uses it with invented data, plus the `file:` line in the demo's manifest.
4. Update the consumers in the game (`using`, the reference in `PryingEyes.Rules.asmdef`).
5. `python tools/check_suite.py` must pass: it is the acceptance test of the move.

### What a promotion must not do

- Leave a copy behind. The game uses the package or the move did not happen.
- Add an option for a consumer that does not exist.
- Change behaviour in the same pull request. A promotion is a move; the lab's checks are the same before and after.

## Hygiene of the existing packages

Observed, with the position taken:

- All six are at version `0.1.0` and have no changelog. With `file:` references nobody reads the version: keep it, and start a
  changelog on the day a package is consumed from outside this repository.
- The ids carry the game's name and the assemblies do not (`Production.*`). Deliberate and documented; unchanged.
- `unity/LayoutDemo` checks all six packages and is still named after the first. Cosmetic; rename it only together with a change that
  already touches it.
- No package has tests inside it: the labs are the tests, and the guard requires them. Unchanged.
