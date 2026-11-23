# Modular architecture: roadmap

The order in which the game becomes modular, reusable and easy for AI agents to work on, and the state of each step. This is the page
to open first, and the one to keep up to date: whoever finishes a task changes its row [below](#status) in the same pull request.

It does not need the conversation that produced it. Everything it relies on is in the repository:

| Read | For |
| --- | --- |
| [architecture-audit.md](architecture-audit.md) | the facts: what the code is today, measured, as findings F1 to F14 |
| [modularization.md](modularization.md) | the target: five boundaries and the decision for every central list |
| [package-extraction.md](package-extraction.md) | what can become a reusable package, and when |
| [agent-tooling.md](agent-tooling.md) | the commands and data formats for agents |
| [modular-tasks.md](modular-tasks.md) | the brief of each task: steps, acceptance criteria, verification |

Written on 20 November 2026 at commit `4042709` (version 0.9.4). At that date nothing in the plan had been carried out except the
census tool (`tools/code_census.py`) that the audit needed; the [status](#status) table is the live part.

## What the plan is, in five sentences

1. Run the checks that already exist and that nothing runs, and make new ones appear in one place (phase 0).
2. Give every in-player check one shape: listed in a manifest, started by one command, answered with data (phase 1).
3. Let agents work at the same time without stepping on each other: worktrees, ports, files (phase 2).
4. Draw the first boundaries the compiler checks: the engine-free rules of the game, then the tests (phase 3).
5. Extract or converge the rest only when an event asks for it (phase 4).

No phase rewrites gameplay. The largest move of code is 34 files that change folder without changing a line.

## Why this order

| Phase | Cost | Risk | What it buys | Needs Unity |
| --- | --- | --- | --- | --- |
| 0. Gate what is true | hours | none for the game: no game code changes | 107 more checks on every push; a generator change cannot pass unseen; new labs cannot be forgotten | no |
| 1. One shape for checks | days | low: the harness keeps every check and its result line | agents discover and run suites and read results without prose; the most edited line of the project disappears | T07, T08 |
| 2. Parallel work | days | low to medium (T14 touches the settings) | agents stop colliding on ports, files and worktrees; rule 6 can be relaxed | T12 to T14 |
| 3. Boundaries | days | medium: assemblies change how Unity compiles | the compiler keeps rules pure and tests out of releases | yes |
| 4. On a trigger | varies | varies | nothing until the trigger | varies |

The order follows cost and risk, not importance: phase 0 is first because it is nearly free and because every later phase is safer
with more checks in the gate. Phase 3 is late because it is the only one that can break the build, and it should be done with the
checks of phases 0 and 1 in place.

## Status

State is one of: `todo`, `done` (with the pull request), `blocked` (with the reason), `dropped` (with the reason). A task in progress
is the one with an open pull request that carries its id in the title: this table lives on `preview` and cannot say it sooner. The briefs
are in [modular-tasks.md](modular-tasks.md).

| Task | Title | Phase | Depends on | Needs | State |
| --- | --- | --- | --- | --- | --- |
| T01 | The desert lab in the gate | 0 | | .NET | done (#39) |
| T02 | A fingerprint of the floor generator | 0 | | .NET | todo |
| T03 | One registry of suites, and the gate asks it | 0 | | .NET | todo |
| T04 | The guard learns the rules about tests | 0 | | | done (#41) |
| T05 | The manifest of the in-player suites | 1 | | | todo |
| T06 | In-player suites through the studio | 1 | T03, T05 | a verification build | todo |
| T07 | The in-player harness, proven on three suites | 1 | | editor, build | done (#50) |
| T08 | The other suites on the harness | 1 | T07 | editor, build | todo |
| T09 | Compile check, doctor and ship status as data | 1 | T03 | editor | todo |
| T10 | A worktree ready for Unity | 2 | | editor to measure | todo |
| T11 | A port and a log folder per run | 2 | | a verification build | todo |
| T12 | Automated runs leave tracked files alone | 2 | | editor, build | todo |
| T13 | What a build rewrites: measure, then decide | 2 | | editor | todo |
| T14 | Settings that automated runs cannot touch | 2 | T06 | editor, build, the maintainer's go-ahead | todo |
| T15 | The game-rules assembly | 3 | T01 | editor, full build | todo |
| T16 | Spike: can the game be a named assembly? | 3 | T15 | editor, full build | todo |
| T17 | The tests in their own assembly | 3 | T08, T16 says go | editor, full build | todo |
| T18 | An engine-free audio core and its lab | 4 | T15 | editor, build | todo, on its trigger |
| T19 | One reader of the command line | 4 | T08 | editor, build | todo, on its trigger |
| T20 | Decide the fate of the multiplayer draft | 4 | | the maintainer | todo, on its trigger |
| T21 | The tables of testing.md from the manifest | 4 | T05, T06 | | todo, on its trigger |

## What can run at the same time

Rule 9 of `AGENTS.md` allows one writer per Unity checkout; each task that needs the editor needs its own worktree with a `Library`
(T10 makes that cheap). Beyond that, tasks collide when they edit the same files:

| Group | Tasks | Why they must not overlap |
| --- | --- | --- |
| The gate and the profile | T01, T02, T03 | `.github/workflows/gate.yml`, `tools/studio_pryingeyes.py` and its test: one after the other, in any order |
| The guard | T04, then T15 | `tools/check_suite.py` |
| `GameManager.cs` | T07, T08, T12, T19 | the same lines of the automatic run |
| The engine | T03, then T06 and T09 | `tools/studio_engine.py` |
| Moving files | T15, T17 | every open branch that touches the moved files conflicts: merge them first, or announce the move |

Free to run beside anything: T05, T10, T11, T13, T20.

A good first wave with three agents: T01 (then T02, T03), T04 (then T05), T10.

## How each phase ends

A phase is over when its numbers have moved and stay moved. The numbers are printed by `python tools/code_census.py` and by the gate.

| Phase | It is over when |
| --- | --- |
| 0 | the gate runs seven hermetic suites and none is named in the workflow; the guard fails on a new production reference to a test and on a new unwrapped test; a change to `PlanGrid.cs` turns the gate red until the fingerprint and the protocol revision are updated |
| 1 | `studio.py suites` lists every in-player suite; at least the suites that need no graphics card produce a `report.json` with one finding per check; no test defines its own `Check` (census: 47 to 0); `GameManager.cs` names no test flag |
| 2 | two agents in two worktrees run a co-op scenario at the same time and both pass; after a smoke walk and a suite `git status --porcelain hotel_build` is empty; a new worktree compiles without importing the assets; the maintainer has decided about rule 6 on the evidence of T14 |
| 3 | no engine-free file compiled by a lab is left in the game assembly (census: 34 to 0); a distribution build contains no test type (census: 14 to 0), or T16 has recorded why not |
| 4 | never "over": each task waits for its trigger |

The table of numbers with today's values is in [modularization.md](modularization.md#how-progress-is-read). Update it there when a task
moves one.

## Triggers of phase 4

| Task or move | Do it when |
| --- | --- |
| T18, audio | the next audio feature, or the first audio defect that a check without the editor would have caught |
| T19, command line | after T08, when no pull request that touches many game files is open |
| T20, multiplayer draft | the maintainer has ten minutes to decide: it is a question, not work |
| T21, generated tables | the manifest has been used by T06 and its fields have stayed true through a few merges |
| Paid areas or wave pacing on the package ([package-extraction.md](package-extraction.md#packages-the-game-does-not-use)) | the next change to either rule |
| A first new package (the ink pen is first in line) | a second consumer appears |
| An interface for game modes ([modularization.md](modularization.md#b3-gameplay-one-assembly-and-why-it-stays-one)) | the next mode is added |

## Decisions that are the maintainer's

The plan stops at these; an agent does not take them:

1. **Rule 6** (no `-uitest` and no `check_presentation.py` unprompted): T14 can make the hazard go away. Relaxing the rule is a
   decision to take on its result.
2. **The evidence under `hotel_build/`** (485 tracked files): T12 only stops automated runs from rewriting the most common ones. Whether
   the folder stays tracked, is pruned or moves out of git is not decided here, and nothing is deleted.
3. **The multiplayer draft** (`Assets/_PryingEyes/Multiplayer/`): keep it as the base of a future rewrite, freeze it, or retire it (T20).
4. **A named game assembly**: T16 ends with a recommendation; going ahead (T17) is a choice.
5. **`Production.Survival` and `Production.Layout`**: converge the game onto them at the next change, or say plainly that they serve
   the city prototype ([package-extraction.md](package-extraction.md#packages-the-game-does-not-use)).

## Risks, and what stops the plan

| Risk | Guard |
| --- | --- |
| A refactor changes behaviour unnoticed | every task that touches game code compares the same suites before and after, check by check; none changes logic and structure in one pull request |
| The plan becomes a second system to maintain | no new registry is added without removing a hand-kept one: the manifest replaces prose, the suites replace the lists of labs |
| Moves of files break open branches | T15 and T17 are announced, done in one pull request each, and merged when few branches are open |
| The numbers are gamed | the census is name matching and says so; the numbers are a thermometer, the acceptance criteria of each task are the proof |
| Disk space on the maintainer's machine | no task needs more than one full build; T10 reports sizes; build folders of finished jobs are disposable (rule 11) |

Stop and ask the maintainer when: a task cannot meet an acceptance criterion; a suite that passed before the task fails after it for a
reason that is not the task's; the gate on `preview` is red (rule 8); a step would delete something that is not derived.

## What is not planned

Listed with the reasons in [modularization.md](modularization.md#what-is-deliberately-not-done) and
[agent-tooling.md](agent-tooling.md#not-proposed): no dependency injection, no event bus, no assembly per folder, no renaming of
identifiers, no service for agents, no second test framework, no package without two consumers.
