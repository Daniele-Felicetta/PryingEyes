# Modularization: boundaries and dependencies

The shape the code should grow into: five boundaries, what each may depend on and what makes a violation fail. Every choice
cites a finding of [architecture-audit.md](architecture-audit.md) (F1 to F14). This page proposes; it changes nothing. The work is cut
into tasks in [modular-tasks.md](modular-tasks.md) and ordered in [modular-roadmap.md](modular-roadmap.md). Which code can leave the game
as a reusable package is a separate question, answered in [package-extraction.md](package-extraction.md).

Not verified: nothing on this page was built or run. Each task carries the checks that prove its part; the ones that need the Unity
editor say so.

## Principles

1. **Enforce what is already true before changing anything.** 34 game files are engine-free and compiled by labs (F8), and 107 checks
   exist that nothing runs (F9). Making these hold by construction costs little and breaks nothing.
2. **A boundary goes where the evidence shows a seam and a payoff**: engine-free rules (F8), tests (F6), the game as a named assembly
   (F1). Not where it would only look tidy: the folders of the gameplay depend on each other in 33 pairs (F1) and cutting them apart
   is a rewrite with no measured gain.
3. **A boundary counts only if something fails when it is crossed**: the compiler (an assembly definition), a lab, or the guard
   (`tools/check_suite.py`). A sentence in a page is not a boundary; this is the lesson of
   [suite-architecture.md](suite-architecture.md).
4. **Remove the registration lists that hurt, keep the ones that are the design** (F5). Each list gets its own decision
   [below](#central-lists).
5. **Every step moves a number** that `python tools/code_census.py` prints, so progress is read, not claimed.

## The target in one table

| # | Boundary | Contains | May depend on | A violation fails in | State |
| --- | --- | --- | --- | --- | --- |
| B1 | Reusable rules: `Production.*` packages | rules that name neither the game nor the engine | nothing | the compiler, a lab, the guard, `unity/LayoutDemo` | exists |
| B2 | Game rules: `PryingEyes.Rules` | game logic that needs no engine: plans, map painter, desert generators, records, tables | B1 | the compiler, a lab, the guard | exists (T15) |
| B3 | Gameplay: one assembly | everything that needs Unity or FishNet | B1, B2 | the compiler, towards B4 only once it is a named assembly | exists as `Assembly-CSharp` |
| B4 | In-player tests | the 59 test files and their harness | B1, B2, B3 | first the guard, then the compiler | proposed (T04, T07, T17) |
| B5 | Editor tools | builders, importers, photo tools | B1, B2, B3 | the compiler (Unity keeps editor folders apart) | exists |

```text
Production.* packages  <--  PryingEyes.Rules  <--  gameplay (one assembly)  <--  in-player tests
        (B1)                     (B2)                    (B3)              <--  editor tools (B5)
```

The Python side already has this shape (engine, profile, guard) and is left as it is.

## B1. Reusable rule packages

Nothing changes. The five packages and the four proofs each one needs are described in [suite-architecture.md](suite-architecture.md).
What may be added to them, and when, is in [package-extraction.md](package-extraction.md).

## B2. Game rules: an engine-free assembly inside the game

**What.** A folder `unity/PryingEyes/Assets/_PryingEyes/Rules/` with one assembly definition, `PryingEyes.Rules`
(`noEngineReferences: true`, references only `Production.Economy` and `Production.Saves`, the two packages its files use), holding the 56 files that a
lab compiled without the engine when T15 moved them (25 November 2026). The card counted 36: the 34 files that F8 lists, changed by T20 (the draft's
`NetworkFloorPlan.cs` went; the three admission files of the co-op came in). The other 20 are the rules of the track mode, which `tracklab` compiles and
which the audit, written before that lab, does not list. They are grouped like this, each file with the folder it came from:

| Subfolder | Files | Compiled by |
| --- | --- | --- |
| `Rules/Plan` | `Proc/PlanTypes.cs`, `PlanGen.cs`, `PlanGen.Building.cs`, `PlanGrid.cs`, `PlanThemes.cs`, `PlanAnnex.cs`, `HubPlan.cs`, `RoundRegions.cs`, `RoomAccessState.cs` | `planlab`, `desertlab` |
| `Rules/Map` | `Proc/Map/MapMath.cs`, `MapPen.cs`, `MapTextures.cs`, `MapScene.cs`, `MapPainter.cs`, `MapPainter.Glyphs.cs`, `MapPainter.Props.cs`, `MapPainter.Shade.cs` | `planlab` |
| `Rules/Desert` | `Deserto/DesertNoise.cs`, `DesertClock.cs`, `DesertWorld.cs`, `DesertTerrainGen.cs`, `DesertMapGen.cs`, `DesertScatterGen.cs`, `DesertWeather.cs`, `DesertGrade.cs` | `desertlab` |
| `Rules/Characters` | `Characters/CharacterLooks.cs`, `Leveling.cs`, `CharacterRecord.cs`, `CathedralJson.cs`, `RunRecord.cs` | `saveslab` |
| `Rules/Cathedral` | `Cattedrale/Catalogo.cs` | `economylab` |
| `Rules/Admission` | `Coop/AdmissionGate.cs`, `SessionAdmission.cs`, `SessionRoster.cs` | `admissionlab` |
| `Rules/Score` | `PlayerPerks.cs`, `Punti.cs` (both in `Scripts/`) | `admissionlab` |
| `Rules/Track` | `Pista/PistaMath.cs`, `BikeSpec.cs`, `BikeModel.cs`, `Circuit.cs`, `CircuitGen.cs`, `Catalogue.cs`, `RacingLine.cs`, `BikeDriver.cs`, `RaceRules.cs`, `PoseTape.cs`, `EngineSynth.cs`, `RaceReplay.cs`, `Licences.cs`, `BikeSheet.cs`, `Championship.cs`, `KeyShaper.cs`, `Career.cs`, `Tuning.cs`, `DrivingLine.cs`, `BrakeCue.cs` | `tracklab` |

Namespaces did not change (`PryingEyes.Proc`, `PryingEyes.Deserto`...), so no other file changed: the move was a `git mv` of each file with its
`.meta` (the same GUIDs), plus the paths in six lab projects and in the pages that name them. Nothing outside `Rules/` needed an `internal` member to
become public: the 56 files declare one non-public type, a helper of `Championship.cs` that nothing else uses, and seven `internal` members that are used only
inside their own file. What no lab compiles stays where it is (`Magia/Grimoire.cs` and `MagicState.cs` need a lab first).

**Why an assembly, when the labs already compile these files.** Three reasons, in order of weight:

1. Unity itself refuses an engine reference in these files, in the editor and in every build, without waiting for a lab that may not be
   run (F9). Before T15 `using UnityEngine` in `DesertWorld.cs` compiled and shipped; now the editor refuses it.
2. The place tells the reader what the file is: code under `Rules/` is checked by a lab in seconds, without a build. That is the cheapest
   feedback an agent can get, and today it has to be discovered file by file from the lab projects.
3. It is the first boundary inside the game that the compiler checks, at almost no risk: the labs prove that the set compiles alone, the
   files import only `System`, `Production.*` and each other, and every top-level type in them is public (F8) but one helper that nothing else uses.

**Rules of B2.** The assembly references only `Production.*`. No file names `UnityEngine`, `UnityEditor`, `FishNet` or `TMPro`. Game names are
allowed (this is what separates B2 from B1). Every file is compiled by at least one lab from its path, and a game file that a lab compiles lives in
`Rules/`. The compiler keeps the first two, and the guard all of them, with the code it already has for packages
([suite-architecture.md](suite-architecture.md#the-games-engine-free-rules)).

**What it is not.** Not a package: its vocabulary is the game's (room roles, themes, the cathedral's works). Not a place for helpers
that happen to be pure: a file goes there because a lab checks it.

**What it gives to new work.** New logic that needs no engine type (a price table, a state machine, a generator) is written in `Rules/`
with its checks in a lab. This is how `WeaponState`, `WaveSchedule`, `Ledger` and the save formats were built; B2 makes it the default
for game-specific logic too.

## B3. Gameplay: one assembly, and why it stays one

The rest of the game (`Assembly-CSharp` holds 433 files, tests included, in the census after T15) stays in a single assembly. Splitting them by folder is not proposed:

- every folder of `Scripts/` is in one dependency cycle, with 33 mutual pairs (F1);
- `HUD`, `Enemy`, `SoundGen` and `SoundBank` are partial classes spread over up to seven folders, and a partial class cannot cross an
  assembly (F3);
- the global state that makes the split screen work is shared by all of it (F4);
- no cost of the single assembly has been measured (compile time was not part of the audit), so the gain is unknown.

What would reopen the question: a measured compile time that slows the work, or a second game that wants a gameplay part.

**One change is proposed: give the assembly a name.** In Unity an assembly definition cannot reference `Assembly-CSharp`, so the tests
cannot have an assembly of their own (B4) until the game has a named one. Whether this works with the project as it is (the FishNet
code generation, the Universal Render Pipeline and UI references, the editor folders, which stop being special inside a named assembly
and need their own definition) is not proven by anything in the repository: it is a spike (T16) that ends in a written decision, not a
commitment.

**Inside the assembly: orientation, not enforcement.** The groups the census prints are a map, not rules:

| Group | Role | Note |
| --- | --- | --- |
| `Scripts:core` (`GameManager`, `HotelNav`, `Elevator`, `TestPaths`) | the run, navigation, test paths | named by 152 and 95 files: every feature reaches it |
| `Scripts:player`, `Scripts:weapon`, `Scripts:enemy`, `Scripts/Armi`, `Scripts/Braccia` | the actors and what they hold | depend on each other in every direction |
| `Scripts/Proc` | plans to floors, the director, the features of a floor | also knows the modes (F2) |
| `Scripts/Cattedrale`, `Scripts/Deserto`, `Scripts:city`, `Scripts/Stealth` | ways to play and their systems | |
| `Scripts/Coop` | online co-op and the seats of the split screen | `Seats` is named by 75 files |
| `Scripts:ui` (`HUD`, `GameMenu`, settings) | interface | `HUD` has parts in 7 folders |
| `Scripts:audio` (`SoundGen`, `SoundBank`, `Sfx`, music) | sound | the nearest thing to a leaf: it names 6 types of other groups |

**The modes (F2).** The director of the floors chooses between four ways to start (a suspended expedition, the desert, the cathedral, a
floor) and names their types. An interface for "a mode" would be an abstraction drawn from too few cases to be trusted. The trigger is
the next mode: the change that adds it extracts the interface from the existing ones, in the same pull request. Until then a mode keeps
its code in its own folder and touches `FloorDirector` only where the choice is made.

## B4. In-player tests: the game never names a test

**The rule.** Tests depend on the game. The game names no test type and reads no test flag.

**Today (F6, F7).** Two production files name a test type; `GameManager.cs` still reads the flags of the tests that are not on the harness yet to know that a dedicated test owns the
process (34 flags before T07 put three suites on the harness, 31 when T08 started); 14 test files ship in releases; tests carried a private copy of the harness
51 times before T07 and 48 when T08 started (`python tools/code_census.py --section tests --section flags` prints today's numbers).

**In two steps.**

1. *Without touching assemblies* (T04 and T07 are done; T05 and T08 are not): a shared harness (`PlayerTest`) that owns `Check`, the exception count, the
   result and the exit; a flag-free signal (`TestRun.Dedicated`) that replaces the list in `GameManager`; a manifest that lists every
   suite ([agent-tooling.md](agent-tooling.md#the-manifest-of-the-in-player-suites)); a ratchet in the guard (done in T04): the two production files
   that name tests and the 14 unwrapped test files are the only ones allowed, and the list can only shrink.
2. *With an assembly* (T17, after T16): the tests move to `PryingEyes.Tests`, compiled only when `PE_DISTRIBUTION_BUILD` is not defined.
   One definition replaces the 45 per-file `#if` wrappers and takes the 14 unwrapped files out of the released game; the compiler then
   refuses any reference from the game to a test.

Hooks that tests need inside production code (`Enemy.debugFreeze`, `PlayerController.botDir`, `Prove.pinned`, `Drops.disabled`) are
part of the game's testability and stay. Test *procedures* that live inside production classes (the gun, lean and wall-buy tests in
`GameManager`, the bot in `FloorDirector.Bot.cs`) move out when their suite is ported to the harness, not before.

## B5. Editor tools

Already separate: Unity compiles `Editor/` folders into their own assembly and the player never contains them. Two things are worth
doing when a tool is next touched, neither urgent: one shared argument reader instead of 13 private copies of `Arg` (T19), and listing the
entry points that agents call (`-executeMethod` targets) in the manifest of [agent-tooling.md](agent-tooling.md), so they are found
without reading [testing.md](testing.md).

## Central lists

A feature that registers itself by editing a shared line is the main source of conflicts between parallel changes (F5). One decision
per list:

| List | Where | Evidence | Decision |
| --- | --- | --- | --- |
| Flags of dedicated tests | `GameManager.cs`, `LegacyDedicatedFlags` (31 when T08 started; one per line) | edited by 28 of 56 changes to the file | **remove**: the harness sets `TestRun.Dedicated` (T07 did it for three suites, T08 for the rest) |
| Flags that stop the bot firing | `GameManager.cs:113` | edited by 6 of 56 | **remove**, same mechanism: the test declares it |
| Catalogue of checks | [testing.md](testing.md) | 32 commits; rows merged by hand | **generate** the tables from the manifest (T05, T21) |
| Labs | `.gitignore`, `gate.yml`, the profile, `AGENTS.md`, three pages | `desertlab` was left out (F9) | **one registry**: the suites of the profile (done in T03) |
| Wire format | `CoopKind`, `CoopProtocol.Revision` in `CoopWire.cs` | 31 commits | **keep** one place, it is the point of the file. Move the history of the revisions out of the 857-character line, so that two changes collide only on the constant. Changes to the protocol merge one at a time |
| Reset of a run | `GameManager.ReturnToMenu`, `FloorDirector.GoTo` | edited by 3 of 56 | **keep**: the order matters and it rarely changes |
| Parts of the HUD | `HUD.EnsureBuilt` | 12 calls, one per line, in drawing order | **keep** |
| State of a player | the `Seat` class, `Seats.Install` | the design of the split screen (F4) | **keep** |
| Sounds | `SoundBank`, `SoundGen` | one file per feature already | **keep** |

A rule of style follows from the first row and costs nothing: **a list that several features extend has one item per line**. Two pull
requests that each add an item then merge without a conflict; the 725-character line of `GameManager.cs` is what happens otherwise.

## Rules for new code

These hold from now, before any task is done:

1. Logic that needs no engine type goes in an engine-free file under `Rules/`, compiled by a lab from that path, with its checks in that lab
   (the guard fails on a lab that compiles a game file outside `Rules/`).
2. A new in-player test is its own `*Test.cs` file, wrapped in `#if !PE_DISTRIBUTION_BUILD`, installing itself; production code never
   names it; the guard fails otherwise. It derives from `PlayerTest` (T07: its load hook is one line, `InstallWhen<T>("-flag")`) and is
   also listed in the manifest of the in-player suites (`tools/player_tests.json`: its unit test fails otherwise).
3. A flag is read in one place. Test flags are read only by tests.
4. A feature of a floor lives in its own files and touches a central file only to add its line to a list, one item per line.
5. A change to the wire format or to the rules of a floor raises `CoopProtocol.Revision`, and only one such change is open at a time.
6. No package for code with one consumer ([package-extraction.md](package-extraction.md#the-ladder)).

## What is deliberately not done

| Not done | Why |
| --- | --- |
| A dependency-injection container or service locator in place of the singletons | The globals are how four players share systems written for one (F4). Replacing them rewrites the game for no observed problem |
| An event bus between features | No finding asks for it; it would hide the dependencies the census can now see |
| Entities and systems (DOTS) | A different engine architecture; the performance work so far never needed it ([../history/quality-audit.md](../history/quality-audit.md)) |
| Gameplay split into one assembly per folder | F1, F3 and F4; no measured gain |
| An interface for game modes now | Too few cases; the trigger is the next mode (B3) |
| Renaming Italian identifiers or namespaces | A cost on every file and every open branch, no behaviour gained (F13). New identifiers are English (rule 7 of `AGENTS.md`) |
| A file that lists the modules | It would be one more hand-kept registry (F13). Folders, assembly definitions and the census already say it |
| Moving the packages to another repository | Only when a second project starts ([suite-architecture.md](suite-architecture.md)) |
| Rewriting the reset lists as a registration mechanism | 3 edits in 56; the risk is small and known |

## How progress is read

`python tools/code_census.py` at each step. The numbers that the plan moves (today: the census of 24 November 2026 at `4aaafa8` with T07
applied; four rows differ from the values the plan started from (34, 47, 51 and 39) because code arrived after it was written, the track
mode first of all, and T07 has since moved two of them). T20 (25 November 2026) moved the first two rows and nothing else, and T15 (the same day) the second to 0; the rest are
the values of that census:

| Number (section of the census) | Today | Target | Task |
| --- | --- | --- | --- |
| Hermetic suites in the gate (`python tools/studio.py suites --json`: the ones with an empty `needs`; before T03, the steps of the workflow) | 8 | every one | T01, T02, T03, T20 |
| Engine-free game files left in `Assembly-CSharp` and compiled by a lab (`pure`) | 0 (56 before T15) | 0 | T15, done |
| Production files that name a test type (`tests`) | 2 | 0 | T08, T17 |
| Test files not kept out of a release (`tests`) | 14 | 0 | T17 |
| Tests with a private `Check` (`tests`) | 48 | 0 | T08 (T07 took it from 51) |
| Flags read by `GameManager.cs` (`flags`) | 50 | under 20 | T08 (T07 took it from 53) |
| Private command-line helpers (`flags`) | 43 | 2 | T08, T19 |
| Literals of test output relative to the working directory (`tests`) | 33 | 0 | T08 (T07 took it from 36) |

The two rows about tests (2 and 14) cannot grow: the guard holds them as a baseline, `tools/fixtures/game-boundaries.json`, and fails on a new
violation and on an entry that no longer applies ([suite-architecture.md](suite-architecture.md#the-games-rules-about-tests)).

The number of mutual pairs between folders (39 in `python tools/code_census.py --by-folder --section cycles`; 33 when the plan was written) is
printed and watched, with no target: lowering it is not a goal in itself.
