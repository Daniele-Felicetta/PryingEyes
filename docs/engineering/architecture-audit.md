# Architecture audit (November 2026)

A measured description of how the repository is built today: its parts, how they depend on each other, where a change costs the most
and what already works. It is the evidence for the plan that makes the game modular, reusable and easy for AI agents to work on:
[modularization.md](modularization.md), [package-extraction.md](package-extraction.md), [agent-tooling.md](agent-tooling.md),
[modular-roadmap.md](modular-roadmap.md) and [modular-tasks.md](modular-tasks.md). Nothing in the game was changed to write it.

Snapshot: commit `4042709` on `preview` (version 0.9.4), 20 November 2026. A finding is numbered (F1 to F14) so the other pages can name it.

## How it was measured

Every number below can be taken again:

```powershell
python tools/code_census.py                 # sizes, references between groups, partial types, flags, tests, global state
python tools/code_census.py --by-folder     # the same with plain folders as groups
python tools/code_census.py --section tests --section flags
git log --name-only --pretty=format: -- "*.cs" "*.md" | sort | uniq -c | sort -rn    # commits per file (in a POSIX shell)
git log --first-parent --oneline -- <file>                                              # the same along preview only
```

[`tools/code_census.py`](../../tools/code_census.py) was written for this audit (unit test: `tools/test_code_census.py`). It reads the C#
files under `unity/PryingEyes/Assets/_PryingEyes` and `unity/PryingEyes/Packages`, blanks comments and strings, and counts a reference
when a file names a type that another group declares. It matches names, it does not compile: a type and a value with the same name can be
confused, so its counts rank the coupling and show it moving, they do not prove that a dependency is absent. Assemblies that cannot see
each other (a package and the game) are never counted as dependent. Without `--by-folder` the tool puts the test files in a group of their
own and splits the root `Scripts` folder, a third of the files, into seven groups by file name (`Scripts:enemy`, `Scripts:weapon`,
`Scripts:player`, `Scripts:ui`, `Scripts:audio`, `Scripts:city`, `Scripts:core`): that grouping is the tool's reading aid, not a structure
that exists in the project.

Not measured: compile times, build times, memory, frame cost. Nothing in this audit needed Unity, Blender or a built player. (T15 measured the editor's compile time
after a one-line change, before and after the move of the rules: [modularization.md](modularization.md#b3-gameplay-one-assembly-and-why-it-stays-one). The rest is still not measured.)

## The repository in one table

| Part | Where | Size | State |
| --- | --- | --- | --- |
| The game (Unity 6000.6.7f1) | `unity/PryingEyes/Assets/_PryingEyes` | 447 C# files, 111,120 lines; 2,831 tracked files with assets | the product |
| Rule packages | `unity/PryingEyes/Packages/com.pryingeyes.*` | 5 packages, 14 files, 2,030 lines | reusable, guarded |
| Independent consumer | `unity/LayoutDemo` | 6 scripts | proof that the packages work without the game |
| .NET labs | `tools/{planlab,weaponslab,survivallab,economylab,saveslab,desertlab}`, `tools/multiplayer-check` | 6 labs and one compile check | four labs run in the gate |
| Production engine (Python) | `tools/studio_engine.py`, `studio_process.py`, `studio_queue.py`, `asset_graph.py` | 736 lines | reusable, guarded |
| Game profile and tools (Python) | `tools/studio_pryingeyes.py`, catalogs, `check_*.py`, `citylab.py`, `trailer/` | with the engine, 30 files at the top of `tools/`; 13 of them are test modules (115 tests) | game-specific |
| Shipping (PowerShell) | `tools/ship.ps1` (787 lines), `tools/release.ps1` (426), `tools/coop/*.ps1` (262) | | game-specific |
| Model sources | `modelli/` (Blender scripts) | 252 tracked files | inputs of the asset pipeline |
| Evidence | `hotel_build/` | 485 tracked files | photos and reports written by test runs (F11) |
| Documentation | `README.md`, `AGENTS.md`, `docs/` | 38 pages under `docs/` before this audit | checked by the guard |
| Earlier prototypes | `index.html`, `js/`, `style.css`, `unreal/`, `Editor/HotelBuilder.cs` (1,683 lines) | | kept for reference, outside the build |

The history is short and dense: 183 commits between 30 October and 20 November 2026, 143 of them on the first-parent line of `preview`,
most written by AI agents working in separate worktrees and merged through pull requests.

## The C# code in numbers

Assemblies (what Unity compiles together):

| Assembly | Files | Lines | What it is |
| --- | --- | --- | --- |
| `Assembly-CSharp` (predefined) | 401 | 100,703 | everything under `Scripts/` and `Multiplayer/` except `Multiplayer/Core`: the whole game and its in-player tests |
| `Assembly-CSharp-Editor` (predefined) | 41 | 10,184 | `Editor/` and `Multiplayer/FishNet/Editor/`: builders, importers, photo tools |
| `PryingEyes.Multiplayer.Core` | 5 | 233 | an engine-free draft (F12) |
| `Production.Layout`, `.Weapons`, `.Survival`, `.Economy`, `.Saves` | 14 | 2,030 | the five rule packages |

Folders of the game:

| Folder | Files | Lines | Folder | Files | Lines |
| --- | --- | --- | --- | --- | --- |
| `Scripts` (root) | 144 | 28,277 | `Scripts/Armi` | 5 | 1,673 |
| `Scripts/Proc` | 84 | 23,542 | `Scripts/Characters` | 10 | 1,178 |
| `Scripts/Deserto` | 54 | 16,077 | `Scripts/Jelly` | 3 | 1,126 |
| `Editor` | 37 | 9,828 | `Scripts/Survivors` | 2 | 904 |
| `Scripts/Coop` | 29 | 7,172 | `Multiplayer/FishNet` | 9 | 1,229 |
| `Scripts/Magia` | 17 | 7,025 | `Multiplayer/FishNet/Editor` | 4 | 356 |
| `Scripts/Stealth` | 13 | 4,792 | `Multiplayer/Core` | 5 | 233 |
| `Scripts/Proc/Map` | 14 | 3,456 | `Multiplayer/Procedural` | 1 | 79 |
| `Scripts/Braccia` | 7 | 2,173 | `Multiplayer` | 2 | 73 |
| `Scripts/Cattedrale` | 7 | 1,927 | | | |

52 files have more than 500 lines and 9 more than 1,000: `Deserto/DesertTest.cs` (3,143 lines, 235 kB), `Editor/HotelBuilder.cs` (1,683),
`Weapon.cs` (1,609), `Proc/FloorDirector.cs` (1,384), `Coop/CoopTest.cs` (1,203), `Armi/Fx.cs` (1,171), `Proc/FloorBuilder.cs` (1,150),
`Magia/MagiaTest.cs` (1,068), `Stealth/StealthTest.cs` (1,003). Lines are long: 782 lines exceed 200 characters. The game has ten
namespaces, and 235 of the 447 files share the root one (`PryingEyes`), so a `using` directive says little about what a file depends on.

## Findings

### F1. The game is one assembly: no boundary is checked by the compiler

All 401 runtime files compile into `Assembly-CSharp`. Folders are a filing system, not modules: by folder, the census finds 119
folder-to-folder edges, 33 pairs of folders that depend on each other, and one cycle that holds every folder of `Scripts/` together with
`Multiplayer/FishNet` and `Multiplayer/Procedural` (15 groups). With the tests set apart and the root folder split, it is 246 edges, 69
mutual pairs and one cycle of 22 groups. Only the packages, the two editor folders, `Multiplayer/Core` and the two files at the root of
`Multiplayer/` are outside it.

The types everything leans on, by the number of other files that name them: `GameManager` 152, `Enemy` 95, `HotelNav` 95,
`FloorDirector` 86, `PlayerController` 78, `Seats` 75, `SoundBank` 66, `Coop` 62, `LightPool` 61, `Sfx` 60.

Consequence: nothing stops a new dependency, a change anywhere recompiles the whole game, and neither a person nor an agent can tell
from where a file sits what it is allowed to use.

### F2. The floor layer depends on the modes built on top of it

`Scripts/Proc` generates and runs a floor; the cathedral (`Scripts/Cattedrale`) and the desert (`Scripts/Deserto`) are ways to play
that use floors. The dependency also runs the other way: `Proc` names 16 types of `Cattedrale` from 7 files and 12 types of `Deserto`
from 19 files (`DesertMode`, `DesertDirector`, `DesertWorld`, `Fabbrica`, `Catacombe`, `Rami`...). `Proc/FloorDirector.cs` opens with
`using PryingEyes.Cattedrale; using PryingEyes.Deserto;` and its `StartRun` chooses between the suspended expedition, the desert, the
cathedral and a plain floor. Each new mode has been added by editing the director of the floors.

### F3. Four types are written across several folders

Partial classes keep files small, and four of them cross folders: `HUD` (16 files, 2,750 lines, 7 folders), `Enemy` (15 files, 3,292
lines, 5 folders), `SoundGen` (18 files, 3,987 lines, 4 folders) and `SoundBank` (17 files, 2,842 lines, 4 folders). A partial class
cannot span two assemblies, so `Deserto`, `Coop`, `Stealth`, `Magia`, `Cattedrale` and `Proc` cannot become assemblies of their own while
they hold a part of `HUD`, `Enemy` or the sound bank. The largest single-folder partials are `FloorBuilder` (11 files, 3,432 lines),
`FloorDirector` (7 files, 3,291) and `RoomKits` (12 files, 3,287).

### F4. State is global, on purpose

The game code holds about 369 mutable static fields in 161 files and 40 singletons named `I` or `Instance` (tests and editor tools left
out). The split screen is built on this: `Seats.Enter` (`Scripts/Coop/Seats.cs`) installs the state of one player (score, weapons, HUD,
stealth, medals, controller) into those globals while that player's code runs, so that systems written for one player work for four
([../multiplayer/split-screen.md](../multiplayer/split-screen.md)). It is a deliberate design and it works; replacing it would mean
rewriting the game. Its costs are concrete and local: the `Seat` class lists every per-player system by hand, a run is reset by a
hand-written list of calls (`GameManager.ReturnToMenu`, repeated in part in `FloorDirector.GoTo`), and a check that needs a clean game
needs a new process.

### F5. A few files and a few lines absorb most changes

Commits that touched each file, out of 183 (in brackets, out of the 143 on the first-parent line of `preview`):

| File | Commits | File | Commits |
| --- | --- | --- | --- |
| `Scripts/GameManager.cs` | 57 (56) | `docs/engineering/testing.md` | 32 |
| `Scripts/Proc/FloorDirector.cs` | 43 (40) | `Scripts/Coop/CoopWire.cs` | 31 (31) |
| `Scripts/PlayerController.cs` | 43 (41) | `Scripts/HUD.cs` | 26 (25) |
| `Scripts/Enemy.cs` | 43 (42) | `Scripts/GameMenu.cs` | 25 (21) |
| `Scripts/Weapon.cs` | 40 (37) | `Scripts/Proc/RoomKits.cs` | 20 |

The cause is a pattern: a feature registers itself by editing a list inside a central file.

- `GameManager.Update` names 32 test flags in one `||` chain, 28 of them on a single line of 725 characters (`GameManager.cs:238` and
  `239`), so that the automatic run does not close the game under a dedicated test. 28 of the 56 changes to `GameManager.cs` edited that
  chain, and 40 edited a line that reads a flag.
- `CoopProtocol.Revision` (`CoopWire.cs:74`, one line of 857 characters) and the `CoopKind` enum are edited by every change to the co-op.
- `HUD.EnsureBuilt` calls 12 `Build...UI()` methods that live in 7 folders; `FloorDirector.GoTo` clears and builds each feature of a floor
  by name; the `Seat` class has a field for each per-player system.
- `docs/engineering/testing.md` is the hand-written catalogue of every check and changed in 32 commits.

When ten pull requests were integrated for 0.8.0 (`682f1f6`, pull request 20) the conflicts fell exactly there. The merge commits
record them: `aaacf56` (`FloorDirector.cs`), `4d93b19` (`GameMenu.Update`), `271815a` (`GameMenu.OnApplicationFocus`, `GrimoireUI`,
`MenuReview`, `FloorDirector`), `a2b06ed` (`GameMenu.cs`, `GameMenu.Design.cs`, `GameSettings.cs`). They were resolved by reading both
sides, which no tool does for an agent.

### F6. A fifth of the player is test code, each test with its own harness

59 files named `*Test*`, 19,851 lines, are compiled into the player: 19.7 % of `Assembly-CSharp`. They are the main proof that the game
works (see [testing.md](testing.md)), and they are built one by one:

- 47 define their own `Check(name, ok)` and count their own failures; 42 install themselves with `RuntimeInitializeOnLoadMethod` when
  their flag is on the command line. `Scripts/TrapTest.cs` is the typical one: a private `Check`, a log hook that counts exceptions, a
  loop that runs the sequence, a result line, `Application.Quit(failed == 0 ? 0 : 1)`.
- The result is a sentence in the Unity log (`[PryingEyes] traptest RISULTATO SUPERATA: 18/18`). The tools find it with a regular
  expression (`tools/check_gunplay.py`, `tools/studio_pryingeyes.py`); `tools/check_navigation.py` looks for `ESITO: ok`.
- 14 test files are not wrapped in `#if !PE_DISTRIBUTION_BUILD` and are part of every released build (`ArchitectureTest*`, `CityTest*`,
  `CoopTest*`, `StealthTest*`). Two production files name a test type: `Coop/CoopGame.cs` (`CoopTest`) and `Stealth/StealthRig.cs`
  (`StealthTest`). Test code also lives inside production classes: `GameManager` holds the gun, lean and wall-buy tests,
  `FloorDirector.Bot.cs` the navigation bot.
- Outputs go to two places by two mechanisms: `TestPaths.Out` (46 calls, the `hotel_build/` of the checkout) and 36 literals that start
  with `unity_logs`, relative to whatever folder the process was started in.

### F7. 271 command-line flags, no registry

The game and its editor tools read 271 distinct flags: 227 the player (60 contain `test`, 35 end in `shots`), 44 only the editor tools.
The command line is read 177 times in 98 files through 39 private helpers (`HasArg`, `Arg`, `ArgInt`, `ArgStr`, `Flag`...), each a copy
of the same loop. 201 flags are read in exactly one file. 37 files that are neither tests nor editor tools read 141 flags;
`GameManager.cs` alone reads 51.

Flags also decide what the game is, silently. `Catacombe.Attive` (`Scripts/Cattedrale/Catacombe.cs:21`) is false in an automated run
(`-autotest`, `-batchmode` and the other flags of `VideoSettings.Automated`) unless `-catacombe`, `-cattedrale`, `-cattedraletest` or
`-desertotest` is given: a check started without one of them runs on the Tower, not on the cathedral and catacombs that a player gets
by default. The only place that says which flags a check needs is the
prose of [testing.md](testing.md).

### F8. Engine-free code is already there, kept pure by convention

> **Done (T15, 25 November 2026).** The files of the table below, and the 20 of the track mode that came after the audit, are in
> `unity/PryingEyes/Assets/_PryingEyes/Rules/`, an assembly that cannot name the engine ([modularization.md](modularization.md#b2-game-rules-an-engine-free-assembly-inside-the-game)).
> The text that follows is the audit as it was written, with the old paths.

48 game files (9,474 lines) name neither `UnityEngine` nor `UnityEditor`. 42 of them (9,197 lines) are in `Assembly-CSharp`, and 34 of
those (8,307 lines) are compiled without the engine by a lab, from the same path:

| Area | Files | Lab |
| --- | --- | --- |
| Floor plans: `Proc/Plan{Types,Gen,Gen.Building,Grid,Themes,Annex}.cs`, `HubPlan.cs`, `RoundRegions.cs`, `RoomAccessState.cs` | 9 | `planlab`, `desertlab`, `multiplayer-check` |
| The map painter: `Proc/Map/Map{Math,Pen,Textures,Scene}.cs`, `MapPainter*.cs` | 8 | `planlab` |
| The desert generators: `Deserto/Desert{Noise,Clock,World,TerrainGen,MapGen,ScatterGen,Weather,Grade}.cs` | 8 | `desertlab` |
| Characters and runs: `Characters/{CharacterLooks,Leveling,CharacterRecord,CathedralJson,RunRecord}.cs` | 5 | `saveslab` |
| The cathedral catalog: `Cattedrale/Catalogo.cs` | 1 | `economylab` |
| Points and perks: `PlayerPerks.cs`, `Punti.cs`; `Multiplayer/Procedural/NetworkFloorPlan.cs` | 3 | `multiplayer-check` |

They import only `System`, the `Production.*` packages and each other, and all their top-level types are public. Engine-free but
compiled by no lab: `Magia/Grimoire.cs` and `Magia/MagicState.cs` (389 lines of magic rules), `Coop/WinWindows.cs`, `Proc/TrailerJob.cs`.

The procedural audio is close to the same state: the 18 `SoundGen` files (3,987 lines of filters, envelopes and the sounds themselves)
use the engine for `Mathf` (405 uses) and two other calls.

Nothing but the labs keeps these files pure, and not every lab is run (F9). In Unity they are ordinary files of the game assembly: a
`using UnityEngine` added to `DesertWorld.cs` compiles and ships.

### F9. Checks that exist but that nothing runs

- `tools/desertlab` has 107 checks ([testing.md](testing.md), section 2.4). No runner starts it: it is not a suite of `tools/studio.py`,
  not in `.github/workflows/gate.yml`, not hashed by `tool_sources` in `tools/studio_pryingeyes.py`, not in `tools/ship.ps1`.
- `tools/planlab` is compiled by the gate, but its commands print statistics and return 0: a change to the floor generator is caught only
  by comparing outputs by hand (the procedure at the end of section 2.4 of [testing.md](testing.md)). Co-op needs every player to build
  the same floor from the same seed, and the comment on `CoopProtocol.Revision` asks to raise it at every change to the rules of the
  floor: a rule kept by memory.
- `tools/multiplayer-check` compiles `PlayerPerks.cs` and `Punti.cs`; it is not in the gate.
- Of the 59 in-player test files only `gunplaytest` is a suite of the studio (`suites` in `tools/studio_pryingeyes.py`); the others are
  started by a hand-typed command line or by `tools/coop/run_all.ps1`.
- A new lab has to be added by hand to `.gitignore`, to three lists in `gate.yml`, to `suites` and `tool_sources` in the profile, to rule
  8 of `AGENTS.md` and to three documentation pages. `desertlab` reached `.gitignore` and the documentation.

### F10. Many entry points, each with its own conventions

To verify a change an agent has to know five kinds of command: Python scripts that parse their arguments at import time
(`check_navigation.py`, `check_gunplay.py`, `check_presentation.py`), PowerShell scripts (`ship.ps1`, `release.ps1`, `coop/*.ps1`),
`dotnet run` on a lab, the Unity editor with `-executeMethod` (the two editor folders expose 77 public static methods without
parameters; 13 editor files carry their own copy of `Arg`), and the player with flags. Results come back as English or Italian text,
exit codes, files in two folders and, only from the studio, a `report.json`. The only fast compile check, the editor started in batch mode with no method, is
written down nowhere in the repository.

The studio engine already has the uniform shape (a job, a report with findings as `code / observed / expected`, a lock, a queue): five
suites use it.

### F11. Hazards for agents that work at the same time

- **One editor per checkout** (rule 9 of `AGENTS.md`): every agent that needs Unity needs its own worktree, and a new worktree has no
  `Library`, so the editor reimports every asset before it can compile. No tool prepares a worktree.
- **Tracked files rewritten by runs.** 485 files under `hotel_build/` are tracked as evidence, and any `-autotest` run rewrites some of
  them (`autotest.txt`, `auto_shot1.png`, `auto_shot2.png`, `proctest.txt`). Builds regenerate tracked materials (284 `.mat` are tracked)
  and `Torre.unity`. A working tree is therefore never clean after a build or a check: rules 4 and 10 of `AGENTS.md` exist to live with
  it, and there is no `.gitattributes` to pin line endings.
- **Shared machine state.** The player's settings are `PlayerPrefs` (81 calls in 13 files), the same registry key the maintainer's game
  uses: rule 6 forbids `-uitest` and `check_presentation.py` unprompted for this reason. `tools/coop/run_coop.ps1` uses UDP port 7807 and
  the log folder `$env:TEMP\pryingeyes-coop` for every checkout, so two agents cannot run co-op checks at once (the game does read
  `-coopport`).
- **GPU and screen.** Photo and performance checks need the graphics card and some take the screen; the documentation says to check by
  hand that nobody is playing ([testing.md](testing.md), section 6).

### F12. Two multiplayer stacks and other parallel code

`Assets/_PryingEyes/Multiplayer/` (21 files, 1,970 lines: an engine-free `Core` assembly, a FishNet adapter, editor tools) is the first
draft of the co-op; the game uses `Scripts/Coop` (29 files, 7,172 lines). Of the 28 types the draft declares, `Scripts/` names one
(`FishNetSeatAuthenticator`); the probe scene and prefabs under `Multiplayer/GeneratedProbe 1/` and `tools/multiplayer-check` use the
others. The page of the co-op explains its origin
([../multiplayer/README.md](../multiplayer/README.md), last section). The city prototype (`City*.cs`, `CityTest*.cs`, two editor builders)
and the classic hall (`HotelBuilder.cs`) are in the same position: alive, compiled, outside the game a player starts. None of this is
wrong, but an agent reading the code cannot tell which stack is the real one.

**Closed for the draft on 25 November 2026 (T20, #66).** The maintainer chose to retire it: `Multiplayer/` is gone, the four files the
game used live in `Scripts/Coop/`, and `tools/multiplayer-check` is replaced by `tools/admissionlab`. The tables of this page that name the
draft or that project are those of the snapshot. The city prototype and the classic hall are as described.

### F13. Documentation and identifiers

- The documentation is unusually complete and is checked (links, index, machine paths). It is also where registries live: the list of
  checks ([testing.md](testing.md), 381 lines) and the list of labs are maintained by hand, and they drift:
  [suite-architecture.md](suite-architecture.md) said "the three .NET rule labs" in its first paragraph and listed four in its table until
  the commit that added this page.
- Code comments, many identifiers (`Fabbrica`, `Catacombe`, `Deserto`, `Magia`, `Braccia`) and every log line are Italian; documentation
  and new identifiers are English (rule 7). This is a property of the project, not a defect. It becomes one only where a tool parses a
  sentence (`RISULTATO SUPERATA`, `ESITO: ok`): a machine contract should be data, in any language.

### F14. Two of the five packages are used only by the city prototype

`Production.Weapons`, `Production.Economy` and `Production.Saves` are used by the game that ships. `Production.Survival` and
`Production.Layout` are used only by the city prototype (`CityWaves.cs`, `CityAccess.cs`, `CityRoom.cs`, the two city builders) and by
`unity/LayoutDemo`. For the same two rules the game has its own code: paid areas in `Proc/RoomAccessState.cs` (the package has
`AreaAccess`) and the pace of a wave in `Proc/RoundDirector.Survival.cs` (the package has `WaveSchedule`). A package that the product
does not use is proven by its demo only, and can drift from what the product needs.

## What already works and must be kept

- **The rule-package pattern**, proven five times: an engine-free assembly, a .NET lab that compiles the same files and writes findings,
  a consumer outside the game, a guard that fails when the boundary is crossed ([suite-architecture.md](suite-architecture.md)).
- **The engine and profile split** in Python, with a toy project as its test.
- **The gate**: hermetic, about a minute locally, three to four minutes on GitHub from a checkout of a few megabytes, green on `preview`
  at this commit.
- **The in-player tests**: about sixty suites that drive the real game with the real controller, deterministic by seed. Their breadth is
  rare; what they lack is a common shape (F6, F7).
- **`tools/ship.ps1`**: one script with the traps already solved, which says what the build contained.
- **The save discipline** (rule 14) and its lab; **`TestPaths`**, `VideoSettings.Automated` and `Roster.IsTest`, which already keep
  automated runs away from the machine's paths, display settings and the player's characters.
- **Determinism**: floors and deserts come from a seed through the project's own generator (`Rng` in `PlanGen.cs`), so results can be
  compared across runs and machines.

## Where each finding is addressed

| Finding | Addressed by |
| --- | --- |
| F1, F2, F3 | [modularization.md](modularization.md): boundaries B2, B4 and B5; tasks T15 to T17 |
| F4 | kept as it is, with the reasons in [modularization.md](modularization.md#what-is-deliberately-not-done) |
| F5 | [modularization.md](modularization.md#central-lists): one decision per list; tasks T07, T08, T21 |
| F6, F7 | [agent-tooling.md](agent-tooling.md): manifest, harness, results as data; tasks T04 to T08, T19 |
| F8 | [package-extraction.md](package-extraction.md) and boundary B2; tasks T15, T18 |
| F9 | tasks T01 to T03 |
| F10 | [agent-tooling.md](agent-tooling.md#the-command-surface); tasks T03, T06, T09 |
| F11 | [agent-tooling.md](agent-tooling.md#hazards-and-isolation); tasks T10 to T14 |
| F12 | task T20 |
| F13 | tasks T03 and T21 |
| F14 | [package-extraction.md](package-extraction.md#packages-the-game-does-not-use): two triggers, no task yet |

The order and the reasons are in [modular-roadmap.md](modular-roadmap.md); the briefs of the tasks in [modular-tasks.md](modular-tasks.md).
