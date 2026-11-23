# Tools and interfaces for AI agents: design

Most of this repository was written by AI agents working at the same time in separate worktrees, and it will keep being so. This page
designs the command line and the data formats that make that work faster and safer: how an agent finds what to run, runs it, reads
the result and stays out of the way of the others. It starts from what exists and from the findings of
[architecture-audit.md](architecture-audit.md) (F5 to F11), and adds as little as possible.

State: a design. Of everything below only `tools/code_census.py` exists (it was written with the audit). Each addition has a task in
[modular-tasks.md](modular-tasks.md) with the checks that prove it; the order is in [modular-roadmap.md](modular-roadmap.md). Claims
about durations on this page were not measured for it and are marked.

## What an agent needs, and what it finds today

| Need | Today | Finding | Proposed |
| --- | --- | --- | --- |
| Know which checks exist, what each needs and how long it takes | 381 lines of prose in [testing.md](testing.md) | F6, F7 | a manifest of the suites and `studio.py suites` |
| Start any check the same way | five kinds of command, each with its options | F10 | every check is a suite: `studio.py test <name>` |
| Read a result as data | a sentence in a log, found by a regular expression | F6 | `result.json` from the player, findings in `report.json` |
| Know in seconds that the code compiles | a trick that is written nowhere | F10 | `studio.py test compile` |
| Not start a check that is forbidden or that disturbs someone | rule 6 of `AGENTS.md`, warnings in prose | F11 | hazards declared in data and refused by the tool |
| Work beside other agents | one port, one temporary folder, tracked files rewritten, no tool for worktrees | F11 | per-run ports and folders, outputs outside git, `tools/worktree.ps1` |
| Add a check without editing shared lines | a flag added by hand to `GameManager.cs`, a row to `testing.md` | F5 | a harness that registers the test, a manifest that generates the table |

## Rules of the design

1. **One entry point**: `python tools/studio.py`. A new capability is a suite or a line of data in the profile
   (`tools/studio_pryingeyes.py`); the engine gets only features that any project could use (rule 2 of `AGENTS.md`).
2. **Data out.** Whatever an agent has to read is JSON with a `schemaVersion`. A check is a finding: `code`, `subject`, `observed`,
   `expected`, `passed`, the shape the engine and the labs already use. Exit codes stay the engine's: 0 passed, 1 findings.
3. **Text stays for people.** The result line in the log, the Italian messages and the summaries are not removed. They stop being what
   a tool parses.
4. **Hazards are data.** What a check needs (a built player, the editor, a graphics card) and what it can disturb (the settings in the
   registry, the screen) is declared next to it, and the tool refuses to run it without an explicit `--allow`.
5. **A run writes under its job folder or under `unity_logs/`**, which git ignores. Never a tracked file.
6. **No service.** No daemon, no MCP server, no web interface. Agents here run commands in a worktree; a long operation is already a
   job that can be queued, polled and cancelled (`studio.py queue`, `studio.py job`).
7. **The scripts that hold solved traps are wrapped, not rewritten**: `tools/ship.ps1`, `tools/release.ps1`, `tools/coop/*.ps1`.

## The command surface

| Command | State | What it does | Result |
| --- | --- | --- | --- |
| `studio.py doctor [--json]` | exists; `--json` and the extra facts are new (T09) | the tools are installed; new: is the editor or a player open on this checkout, is there a `Library`, which commit is the verification build, how the last gate on `preview` ended | text, or JSON |
| `studio.py suites [--json]` | new (T03) | every suite with what it needs, its hazards and its usual duration | one line each, or JSON |
| `studio.py test <suite> [--runtime EXE] [--allow HAZARD]` | exists; `--allow` new (T03) | runs one suite as a job under the project lock | `unity_logs/studio/<job>/report.json` |
| `studio.py test desert` | exists (T01) | the desert lab: 107 checks, one finding each | `report.json` |
| `studio.py test plan` | new (T02) | the fingerprint of the floor generator | `report.json` |
| `studio.py test compile` | new (T09) | the editor compiles the scripts and exits | compiler errors as findings |
| `studio.py test <in-player suite>` | new (T06; the manifest it reads exists since T05) | starts the verification build with the flags of the manifest | `report.json` with one finding per check |
| `studio.py catalog`, `asset`, `gallery`, `queue`, `job`, `report latest` | exist | content pipeline, evidence, queue ([production-studio.md](production-studio.md)) | as today |
| `python tools/check_suite.py` | exists; has the game rules since T04 | boundaries, the game's rules about tests, documentation | findings as text, exit code |
| `python tools/code_census.py [--json FILE]` | exists | sizes, references, flags, tests, global state | text and JSON |
| `tools/ship.ps1` | exists; `ship_last.json` new (T09) | gate, commit, build, smoke, push, the maintainer's game | text status; new: the same as data |
| `tools/worktree.ps1` | new (T10) | a worktree ready for Unity; removing one safely | JSON summary |

The pages that list commands for people ([testing.md](testing.md), [building-and-shipping.md](building-and-shipping.md)) keep doing so.

### `suites`: what can be run

The engine's `Suite` gains optional, free-text fields that the profile fills: `kind`, `needs`, `hazards`, `seconds`, `about`. The engine
interprets only `hazards`. Example of `studio.py suites --json` (the durations are invented):

```json
{
  "schemaVersion": 1,
  "suites": [
    {"name": "saves", "kind": "lab", "needs": [], "hazards": [], "seconds": 20, "about": "Save documents, migrations, character and run files"},
    {"name": "compile", "kind": "editor", "needs": ["editor"], "hazards": [], "seconds": 30, "about": "The scripts compile"},
    {"name": "traptest", "kind": "player", "needs": ["runtime"], "hazards": [], "seconds": 60, "about": "Door traps on a real floor"},
    {"name": "uitest", "kind": "player", "needs": ["runtime", "gpu"], "hazards": ["registry", "screen"], "seconds": 180, "about": "Menus, touch input, party HUD"}
  ]
}
```

| Word | Meaning |
| --- | --- |
| `needs: runtime` | a built player; by default the verification build `unity/PryingEyes/Builds/Audit/PryingEyes.exe`, never the maintainer's game |
| `needs: editor` | the Unity editor on this checkout (one at a time: the project lock) |
| `needs: gpu` | draws: no `-nographics`, a graphics card, and results that are wrong while someone is playing |
| `hazards: registry` | writes the settings key that the maintainer's game reads (rule 6 of `AGENTS.md`) |
| `hazards: screen` | opens a window or takes a monitor |

A suite with hazards runs only with `--allow registry --allow screen`; otherwise the job fails at once with `SUITE_HAZARD_NOT_ALLOWED`
and names them. An agent passes `--allow` only when the maintainer asked for that check: the rule of `AGENTS.md` is unchanged, the tool
now enforces it. `needs: gpu` makes the runner refuse (`PLAYER_GPU_BUSY`) while another `PryingEyes.exe` is running, as
`tools/coop/run_split.ps1` already does.

The suites that need nothing (`needs` empty) are the ones the gate runs. The workflow asks `suites --json` for them instead of naming
each lab, so a new lab is added in one place (F9).

### The manifest of the in-player suites

`tools/player_tests.json` (since T05): one entry for each check that runs inside the player, 87 of them. It runs nothing and replaces
nothing in the game; it writes down what today only [testing.md](testing.md) says. Its schema is `tools/player_tests.schema.json` and its
test is `tools/test_player_tests.py`. No run has confirmed it yet: the arguments come from the prose, from the comments of the test files
and from the scripts that start them, and the timeouts are upper bounds, not measurements.

```json
{
  "schemaVersion": 1,
  "suites": [
    {
      "name": "traptest",
      "source": "unity/PryingEyes/Assets/_PryingEyes/Scripts/TrapTest.cs",
      "args": ["-batchmode", "-nographics", "-autotest", "-nofire", "-noenemies", "-traptest", "-seed", "1000", "-floor", "1"],
      "mode": "tower",
      "timeout": 120,
      "needs": ["runtime"],
      "hazards": [],
      "photos": {"args": ["-trapshots"], "needs": ["gpu"], "folder": "unity_logs/trappole"},
      "page": "docs/game/door-traps.md"
    }
  ],
  "unlisted": []
}
```

| Field | Why it is there |
| --- | --- |
| `name` | what `studio.py test` will call it: the flag without its dash (`traptest`), or `coop-<scenario>` for a scenario of `tools/coop/run_all.ps1` |
| `source` | the C# file that implements the check or reads its flag |
| `args` | the whole command line, so that nobody has to assemble it from prose |
| `mode` | which game the flags select: `tower`, `catacombs`, `cathedral`, `desert`, `city`, `track` or `menu` (no `-autotest`). Under `-autotest` the catacombs are off unless a flag asks for them (F7): the manifest has to say it, and its test works out the game that `args` start and fails if it is not the declared one |
| `timeout` | seconds after which a runner gives up: an upper bound taken from the prose or from the script that runs the suite |
| `checks` | how many checks the suite ran when it was last confirmed by a run. Optional, and left out until a run has confirmed it. When present, fewer checks is a failure: a test that skips half of itself passes today |
| `needs`, `hazards` | as above; `gpu` is there exactly when `-nographics` is not in `args` |
| `photos` | the optional run that takes pictures: the flags to add (`-nographics` goes), what it needs and the folder where the pictures land |
| `runner` | for the suites made of several processes (co-op, split screen): the script that starts them and the parameters it is given. Such an entry has no `args`: the script owns them |
| `page` | the page that says what the suite proves |

`unlisted` holds the test files that install themselves and are the source of no entry, each with the reason why. It is empty today.

Its unit test is hermetic and is what keeps it true: the schema; names are unique; `source` exists; every flag in `args` and `photos` that is
not Unity's own is a literal in the code of the player; the game that the flags start is the declared `mode`; `gpu` is in `needs` when the run
draws; every test file that installs itself is the `source` of an entry, or is `unlisted` with the reason why (the census counts these files
and the test derives the list, checking its count against the census's); every scenario of `run_all.ps1` has exactly one entry; a runner script exists and
takes the parameters it is given; `page` exists. A new in-player test is therefore listed here or the unit tests fail. The tables of
[testing.md](testing.md) can then be generated from it (T21).

### Results as data

**From the player.** The harness (below) writes `result.json` into the folder given by `-testout DIR`:

```json
{
  "schemaVersion": 1,
  "suite": "traptest",
  "ok": false,
  "checks": [
    {"name": "le trappole delle soglie ci sono", "ok": true},
    {"name": "la leva accesa costa 1000 punti", "ok": false, "detail": "punti 1500 -> 1500"}
  ],
  "exceptions": 0,
  "seconds": 41.2,
  "version": "0.9.4",
  "args": ["-batchmode", "-nographics", "-autotest", "-traptest", "-seed", "1000", "-floor", "1"],
  "artifacts": ["01_trappola_accesa.png"]
}
```

**In the report.** The runner copies each check into the job's `report.json` as a finding (`code`: the suite's name and the index of the
check, `observed` and `expected` from `detail`, `passed`), as the labs do. A suite passes when all of these hold: the process ended with
0 within its time; `result.json` exists and says `ok`; the number of checks is not below `checks`; the log holds no exception other than
the ones the renderer always prints without a graphics card (the filter of `tools/check_navigation.py`).

**Until a suite uses the harness** there is no `result.json`: the runner falls back to the line the tests print today
(`<name> RISULTATO SUPERATA: 18/18`) and says so in the report (`"verdictSource": "log-line"`). Suites can therefore be registered
before they are ported, and ported one at a time.

### `test compile`: does it compile

The editor started in batch mode with no method (`-batchmode -nographics -projectPath unity/PryingEyes -quit -logFile ...`) compiles
the scripts and exits. As a suite it runs under the project lock, and each `error CS....` line of the log becomes a finding with its
file and line. It is the fastest answer to "did I break the build" that needs no player (seconds with an imported project; not measured
for this page). On a worktree without a `Library` the same command imports every asset first, which takes long and writes gigabytes:
`doctor` says when the `Library` is missing, and `tools/worktree.ps1` avoids it.

### `doctor`: the state of this checkout

Rule 8 of `AGENTS.md` asks every session to look at the last gate before working. `doctor --json` answers that and the other questions
an agent asks before it starts:

```json
{
  "schemaVersion": 1,
  "tools": {"unity": "found", "blender": "found", "dotnet": "10.0.403"},
  "editorOnProject": null,
  "playersRunning": [],
  "library": true,
  "verificationBuild": {"present": true, "commit": "4042709", "mode": "scripts", "sourcesNewerThanBuild": 0},
  "gate": {"branch": "preview", "conclusion": "success"},
  "worktree": {"linked": true, "branch": "claude/example"}
}
```

`gate` needs the `gh` command and is `null` without it. `verificationBuild` reads `unity_logs/ship_built.json`, which `tools/ship.ps1`
already writes.

### `tools/worktree.ps1`: a place to work

Rules 9 and 12 of `AGENTS.md` send every agent that does not own the editor to a worktree and ask that it be removed afterwards. The
script does both ends:

- `-New <name> [-From <checkout>]`: `git worktree add` on a new branch from `origin/preview`, then copies `unity/PryingEyes/Library`
  from the donor checkout when its `ProjectVersion.txt` is the same and no editor has it open (no `Temp/UnityLockfile`), at low
  priority. Copying an imported `Library` instead of importing was observed to take about a minute for 3.5 GB (not verified for this
  page). It prints what it did as JSON, with the sizes: disk space is the scarce resource
  ([building-and-shipping.md](building-and-shipping.md#where-builds-live)).
- `-Remove <name>`: lists uncommitted and unpushed work and refuses if there is any; otherwise `git worktree remove` and, when the
  branch is merged, deletes it. It never forces.

## The in-player harness

One base class replaces the 47 private copies of the same code (F6). Sketch of the interface; T07 fixes it against three real suites:

```csharp
namespace PryingEyes.Testing
{
    /// What the game may ask about the run. No test type and no test flag is named outside the tests.
    public static class TestRun
    {
        public static bool Dedicated;       // a test owns this process: the automatic run must not report, photograph or quit by itself
        public static bool HoldFire;        // the bot must not shoot by itself
        public static string OutDir(string suite);      // -testout DIR, or unity_logs/<suite> of the checkout
    }

    public abstract class PlayerTest : MonoBehaviour
    {
        protected abstract string Suite { get; }        // "traptest": the name on the result line and in result.json
        protected abstract IEnumerator Run();           // the checks
        protected bool Check(string name, bool ok, string detail = "");
        protected void Photo(string name);              // into OutDir, listed under artifacts
        protected static void InstallWhen<T>(string flag) where T : PlayerTest;      // one line in the test's own load hook
        // Start(): waits for the level, counts exceptions, runs Run(), writes result.json, prints the result line, quits with 0 or 1
    }
}
```

What it removes: the `Check`, the log hook, the loop and the exit of each test; the list of 32 flags in `GameManager.cs`
(`TestRun.Dedicated`); the literals that start with `unity_logs` and depend on the working directory. What it keeps: the result line,
so every existing script and habit still works; the exit codes (0, 1, and 2 for "this floor cannot host the test").

## Hazards and isolation

| Hazard (F11) | Today | Proposed | Task |
| --- | --- | --- | --- |
| Settings in the registry, shared with the maintainer's game | rule 6; `run_split.ps1` saves and restores the key around a windowed run | declared as `hazards: registry`; the runner saves and restores the key for every such suite; later, settings kept in memory during automated runs so that the hazard disappears | T06, T14 |
| Graphics card and screen | "do not run while someone plays", in prose | `needs: gpu`, `hazards: screen`; refusal while another player is running | T06 |
| UDP port 7807 and `$env:TEMP\pryingeyes-coop` for every checkout | two agents cannot run co-op checks at once | a port and a log folder per run, under the job folder | T11 |
| Tracked files rewritten by runs (`hotel_build/autotest.txt`, `auto_shot*.png`) | restored by hand after each run | automated runs write under `unity_logs/`; `hotel_build/` only when evidence is asked for | T12 |
| Tracked files rewritten by builds (materials, `Torre.unity`, settings assets) | rules 4 and 10: stage exact paths | measure what a build rewrites and why, then decide | T13 |
| One editor per checkout | rule 9; the project lock | `tools/worktree.ps1`; `doctor` | T09, T10 |
| Two changes to the wire format at once | found at merge | one at a time, said in [modularization.md](modularization.md#rules-for-new-code) | none |

## A session with these tools

The path the tools should make the easy one, for a change to the game:

1. `python tools/studio.py doctor --json`: the gate on `preview` is green, nothing holds the checkout.
2. `tools/worktree.ps1 -New <name>` unless this session owns the editor's checkout.
3. Make the change. Logic that needs no engine goes under `Rules/` with its checks in a lab.
4. `python tools/studio.py test compile`, then the labs of what was touched (`test saves`, `test desert`...).
5. A scripts-only verification build: `tools/ship.ps1 -NoPush -NoExe -Mode scripts` builds into `Builds/Audit` without publishing.
6. `python tools/studio.py suites --json`, then `test <suite>` for the in-player suites of the area. Read `report.json`, not the log.
7. `python tools/check_suite.py` and the unit tests; `tools/ship.ps1` with the exact paths; a pull request against `preview`.
8. After the merge: `tools/worktree.ps1 -Remove <name>`.

## Formats and who owns them

| File | Written by | Read by | Version |
| --- | --- | --- | --- |
| `unity_logs/studio/<job>/report.json` | the engine | agents, the gallery, the gate's summary | `schemaVersion` 1, exists |
| `studio.py suites --json`, `doctor --json` | the engine and the profile | agents, the gate | 1, new |
| `tools/player_tests.json` | people and agents, checked by its test | the profile | `schemaVersion` 1, exists (T05) |
| `result.json` of a player run | the harness in the game | the profile's runner | 1, new |
| `python tools/code_census.py --json` | the census | the audit, the gate's summary | 1, exists |
| `unity_logs/ship_last.json` | `tools/ship.ps1` | agents | 1, new |

A change that removes or renames a field raises the version of that file; adding a field does not.

## Not proposed

| Not proposed | Why |
| --- | --- |
| An MCP server or any long-running service | The agents that work here run shell commands; a service adds a process to keep alive and nothing they cannot already do |
| A dashboard | The evidence gallery exists (`studio.py gallery build`) |
| Analysis with the C# compiler (Roslyn) | It cannot resolve types without Unity's assemblies, which the gate's checkout does not have. Name matching is enough to rank and to watch |
| Unity's own test framework for the in-player checks | They prove the built player from a real start, which is where the defects recorded in [../history/quality-audit.md](../history/quality-audit.md) were found. A second framework would mean two harnesses |
| Rewriting `tools/ship.ps1` in Python | Its 787 lines are solved traps; it gains a data file, not a new language |
| `studio.py test affected` (which suites a change touches) | Useful, but it needs a map from files to suites that somebody must keep. Reconsider when the manifest has been in use for a while and its `source` and `page` fields prove to be maintained |
