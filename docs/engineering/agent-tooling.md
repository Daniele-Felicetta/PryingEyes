# Tools and interfaces for AI agents: design

Most of this repository was written by AI agents working at the same time in separate worktrees, and it will keep being so. This page
designs the command line and the data formats that make that work faster and safer: how an agent finds what to run, runs it, reads
the result and stays out of the way of the others. It starts from what exists and from the findings of
[architecture-audit.md](architecture-audit.md) (F5 to F11), and adds as little as possible.

State: partly built. `tools/code_census.py` (written with the audit), `studio.py test desert` and `test plan` (T01, T02), the game's rules in
`check_suite.py` (T04), `tools/worktree.ps1` (T10; how it is used is in [building-and-shipping.md](building-and-shipping.md#branches-and-worktrees)),
`tools/player_tests.json` (T05) and, since T03, `studio.py suites` and the hazards (`--allow`) exist, and since T06 the suites of the
manifest run through `studio.py test`; the rest below is a design. Each addition has a task in
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
| Work beside other agents | tracked files rewritten; `tools/worktree.ps1` (T10) makes and removes the worktree; the co-op scripts take a port and a log folder per run (T11) | F11 | outputs outside git |
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
| `studio.py suites [--json]` | exists (T03) | every suite with what it needs, its hazards and its usual duration | one line each, or JSON |
| `studio.py test <suite> [--runtime EXE] [--allow HAZARD]` | exists; `--allow` since T03 | runs one suite as a job under the project lock | `unity_logs/studio/<job>/report.json` |
| `studio.py test desert` | exists (T01) | the desert lab: 107 checks, one finding each | `report.json` |
| `studio.py test plan` | exists (T02) | the fingerprint of the floor generator: 48 plans and the protocol revision, one finding each | `report.json` |
| `studio.py test compile` | new (T09) | the editor compiles the scripts and exits | compiler errors as findings |
| `studio.py test <in-player suite>` | exists (T06) | starts the verification build with the flags of the manifest (87 suites, [below](#running-the-suites-of-the-manifest)) | `report.json` with one finding per check, and `player`: the verdict, where it came from, how many checks |
| `studio.py catalog`, `asset`, `gallery`, `queue`, `job`, `report latest` | exist | content pipeline, evidence, queue ([production-studio.md](production-studio.md)) | as today |
| `python tools/check_suite.py` | exists; has the game rules since T04 | boundaries, the game's rules about tests, documentation | findings as text, exit code |
| `python tools/code_census.py [--json FILE]` | exists | sizes, references, flags, tests, global state | text and JSON |
| `tools/ship.ps1` | exists; `ship_last.json` new (T09) | gate, commit, build, smoke, push, the maintainer's game | text status; new: the same as data |
| `tools/worktree.ps1` | exists (T10) | a worktree ready for Unity; removing one safely | JSON summary |

The pages that list commands for people ([testing.md](testing.md), [building-and-shipping.md](building-and-shipping.md)) keep doing so.

### `suites`: what can be run

The engine's `Suite` has optional, free-text fields that the profile fills: `kind`, `needs`, `hazards`, `seconds`, `about` (empty by default,
so a suite written without them works as before). The engine interprets only `hazards`; `needs` is for whoever reads the list, the gate
first. `studio.py suites` prints a table, one line per suite in the order of the profile, and `--json` prints the same as data; neither
starts a job, takes the lock or writes a report. `seconds` is the usual duration on the gate's runner, `0` while nobody has measured it.
The shape below is the real one, the entries are an example (the durations are invented, `compile` is the kind of suite that T09 adds and
does not exist yet, and the suites of the player, which exist since T06, have `seconds: 0`: nobody has measured them):

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
now enforces it (a queued request is refused when it is added, and keeps its `--allow` words; an `--allow` that names a hazard the suite
does not declare changes nothing). `needs: gpu` makes the runner refuse (`PLAYER_GPU_BUSY`) while another `PryingEyes.exe` is running, as
`tools/coop/run_split.ps1` does; any run is refused (`PLAYER_IN_USE`) while a copy of the very build it would start is running. Other
programs are not looked for: before a run that draws, look at what else uses the machine (a game of the maintainer's, for one).

The suites that need nothing (`needs` empty) are the ones the gate runs. The workflow asks `suites --json` for them and compiles every
`tools/*lab` folder, instead of naming each lab, so a new lab is added in one place, its entry in the profile and its folder (F9).

### The manifest of the in-player suites

`tools/player_tests.json` (since T05): one entry for each check that runs inside the player, 87 of them. It runs nothing and replaces
nothing in the game; it writes down what today only [testing.md](testing.md) says. Its schema is `tools/player_tests.schema.json` and its
test is `tools/test_player_tests.py`. Few runs have confirmed it: the arguments come from the prose, from the comments of the test files
and from the scripts that start them, and the timeouts are upper bounds, not measurements. `checks` is there only for a suite that a run
has confirmed: the 9 that T06 ran on a verification build (`traptest` 18, `barreltest` 19, `minetest` 12, `chartest` 23,
`survivaltest` 91, `cattedraletest` 72, `pistamenutest` 46, `pistamenulicence` 30, `pistamenucup` 39).

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
      "checks": 18,
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
| `label` | what the test prints between `[PryingEyes]` and `ok`, `FALLITO` or `RISULTATO`, when that is not the name: a family of suites shares one test class and one label (`pistamenulicence` prints `pistamenutest`). Optional. The runner reads the lines of that label, and the test of the manifest checks it against the C# in both directions: a `label` that the source does not print, one that repeats the name, and a source that prints another label with no `label` to say so |
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

**From the player.** The harness (below) writes `result.json` into the folder given by `-testout DIR`, or into `unity_logs/<folder>/` of the
checkout when the flag is absent (the second check below is an invented failure, to show a `detail`):

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
  "seconds": 9.6,
  "version": "0.10.2",
  "args": ["-batchmode", "-nographics", "-autotest", "-nofire", "-noenemies", "-traptest", "-seed", "1000", "-floor", "1", "-logFile", "<path>"],
  "artifacts": ["accesa_fulmine.png"]
}
```

The file is plain ASCII: every character outside printable ASCII is a `\uXXXX` escape, so that any parser in any encoding reads the same
text (a check name of `TrapTest` holds an em dash). `detail` is there only when the test gave one. `args` is the command line without the
executable, and a word that is a path is written `<path>`, so that the file names no machine. A failure to write it is a warning in the
log: it changes neither the verdict nor the exit code.

**In the report.** The runner copies each check into the job's `report.json` as a finding (`code`: the suite's name and the number of the
check, `subject`: the name of the check, `observed`: its detail, `passed`), as the labs do, and adds `player`:
`{suite, verdictSource, checks, passed, declared, exceptions, logs}`. A suite passes when all of these hold: the process ended with 0
within its time; the run says it passed (`result.json` when it exists, the result line of the log otherwise); the number of checks is not
below `checks`; the log holds no exception other than the ones the renderer always prints without a graphics card (that filter is in
`tools/player_log.py`, which `tools/check_navigation.py` uses too).

**For a suite that does not derive from `PlayerTest` yet** (T08 ports them a pull request at a time) there is no `result.json`: the runner falls
back to the result line that the tests print today (`[PryingEyes] <label> RISULTATO SUPERATA: 18/18`, `<label>` being the `name` of the
entry or its `label`; the lines `ok <name> (<detail>)` and `FALLITO <name> (<detail>)` before it are the findings) and says so in the
report (`player.verdictSource` is `log-line`, and `result.json` when that file was read). Suites can therefore be registered before they
are ported, and ported one at a time. A `result.json` that exists but cannot be trusted (not JSON, the result of another suite, another
version) fails the suite (`PLAYER_RESULT_INVALID`): the lines are no way around a bug of the harness. A test that prints only its failures
(`gunplaytest`) has fewer findings than checks: `player.checks` is the number on its result line.

The runner reads three forms of that line, and `tools/player_log.py` is the only place that knows them: the standard one above;
`CoopTest`'s (`[PryingEyes] cooptest ospite: RISULTATO SUPERATA: 12/12 verifiche ok`, each check as `ok       <what>   [<detail>]`, the
process that speaks after the label); and `PistaCoopTest`'s (`[PryingEyes] pistacooptest ospite RISULTATO ...`, no colon, the detail in
parentheses). A test that derives from `PlayerTest` writes `result.json` and prints the standard line too. Counted from the sources on 24
November 2026, with the first pull request of T08 merged, 68 of the 87 entries say their result in a form the runner reads: the ones on
`PlayerTest`, the others that print the standard line (two of them runners: `seatstest`, `splitdesertotest`) and the 17 co-op scenarios,
which print the co-op forms. The other 19 print theirs in another form or not at all: `citytest`, `displaytest`, `guntest`, `leantest`,
`walltest`, `magictest`, `procbot`, `perftest` and the eleven of the stealth family (`stealthtest`, `roundtest`, `knifetest`, `droptest`,
`booktest`, `gridtest`, `pushtest`, `holescan`, `alientest`, `temitest`, `refshot`). The studio starts them and fails them with
`PLAYER_RESULT_MISSING`, whatever the process did, until they are ported to the harness (T08, whose pull requests shorten this list): the
runner does not guess a verdict from an exit code, and `padtest`, run without a window, quit with 0 and printed no result at all.

### Running the suites of the manifest

The profile turns each entry of `tools/player_tests.json` into a suite of kind `player`, so `studio.py suites` lists all 87 next to the
labs (`needs` as the entry says; they are not in the gate, which runs only what needs nothing). `studio.py test <name>` runs one as a job,
under the project lock: one in-player suite at a time in a checkout, and two checkouts can run at the same time. Without `--runtime` it
starts the verification build, `unity/PryingEyes/Builds/Audit/PryingEyes.exe` of the checkout it is run from, never the studio's
`Builds/CityPreview` and never the maintainer's game; `--runtime` names another build, which is how `asset build` runs `gunplaytest` on the
build it has just made. (`gunplaytest` is the manifest's name for what the studio used to call `gunplay`: there is one suite, with the
manifest's command line, which is the one of `tools/check_gunplay.py`.) The runner, in order:

1. refuses to start, launching nothing, when the build is not there (`PLAYER_RUNTIME_MISSING`), when a copy of that very build is running
   (`PLAYER_IN_USE`) or when the suite needs the graphics card and any other `PryingEyes.exe` is running (`PLAYER_GPU_BUSY`). Before it, the
   engine refused a suite with hazards that no `--allow` names (`SUITE_HAZARD_NOT_ALLOWED`);
2. for `hazards: registry`, exports the settings key (the one that `run_split.ps1` saves) and imports it again after the run, whatever
   happened: the finding `SETTINGS_RESTORED`, or `SETTINGS_KEY_ABSENT` when there was no key yet, which is then left as the run makes it,
   as that script does;
3. starts the player with the `args` of the entry and then `-logFile <job>/<name>-player.log`, `-report <job>/<name>-telemetry.txt` and
   `-testout <job>/testout`, within the entry's `timeout`. An entry with a `runner` is started through its script
   (`powershell -File <script> -<Parameter> <value> ... -LogDir <job folder>`), which runs the verification build of its checkout: `--runtime`
   is refused for it (`PLAYER_RUNTIME_FIXED`);
4. reads what the run left, and fails with the first code of this table that applies:

| Code | When |
| --- | --- |
| `PLAYER_TIMEOUT` | no result within `timeout`; the process tree has been stopped |
| `PLAYER_RESULT_INVALID` | `result.json` exists and cannot be trusted |
| `PLAYER_RESULT_MISSING` | the run left neither `result.json` nor a result line: it never started, crashed or stopped early. The message says how the process ended |
| `PLAYER_CHECK_FAILED` | the run says it failed; the message names the checks that did |
| `PLAYER_EXCEPTION` | the log holds an exception that is not the renderer's |
| `PLAYER_CHECKS_FEWER` | fewer checks than the entry's `checks`: part of the test did not run, or the manifest is out of date |
| `STEP_FAILED` | everything above is fine, but the process quit with a code other than 0 |
| `PLAYER_SETTINGS_NOT_RESTORED` | it passed, but the settings key could not be imported again |

Which suites need a graphics card was not known when the manifest was written: the prose omitted `-nographics` for some commands without
saying why. T06 ran them without a window, on a machine where nobody played. `pistamenutest`, `pistamenulicence` and `pistamenucup` pass
(46, 30 and 39 checks), so their entries carry `-nographics` and no `gpu`. `pistamenugarage` and `pistamenucareer` fail on
`RenderTexture.Create` (the showroom is drawn into a texture), and `padtest` fails twelve checks and quits with no result, so they keep `gpu`.
`displaytest` was not run: it declares `hazards: registry`, and it asserts the size of the real screen after changing it, which
`-nographics` cannot give.

For an entry with a `runner` the verdict is made of the logs that the script left in the job folder (every `.log` and `.txt` but the
script's own output). Of several processes (a co-op scenario) those that left a result must all say they passed, their checks add up and
`checks` is compared with the sum; a process that left nothing is not an error (a scenario kills one on purpose: the script owns that, and
its exit code is checked like any other), but at least one must have left a result.

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
script does both ends; its use, its options and the numbers measured on the real project are in
[building-and-shipping.md](building-and-shipping.md#branches-and-worktrees). What it decides, and why:

- `-New <name>`: a new branch from `origin/preview` and, unless `-NoLibrary`, a copy of the `Library` of a checkout that has the same Unity
  version and no editor open (no `Temp/UnityLockfile`), so that the first compile does not import every asset. It refuses, creating
  nothing, rather than copy a `Library` that may be inconsistent, and it keeps the copy out of sight until it is whole. It prints what it
  did as JSON, with the sizes: disk space is the scarce resource ([building-and-shipping.md](building-and-shipping.md#where-builds-live)).
- `-Remove <name>`: refuses while anything is uncommitted or unpushed, and never forces: `git worktree remove` without `--force`, and
  `git branch -D` only for a branch that is merged. The pull requests here are squash-merged and their branches deleted on `origin`,
  which leaves a local branch whose commits are on no branch of `origin`; `refs/pull/<n>/head` outlives the branch, so the script asks
  `origin` whether the commits are the head of a pull request.

## The in-player harness

State: built (T07) and on 22 suites: `TrapTest`, `BarrelTest` and `MineTest` (T07), the ten of the first pull request of T08 (`MedalTest`,
`FeelTest`, `EyesTest`, `FlinchTest`, `MantleTest`, `MagTest`, `LampTest`, `ShaftTest`, `RagdollTest`, `EquipTest`) and the nine of the second
(`LampadariTest`, `PowerTest`, `VariantiTest`, `ImpactTest`, `MapTest`, `MeleeTest`, `JellyTest`, `SplitScreenTest`, `MusicTest`); T08 ports the 29 others that
still carry their own copy of the same code (F6). The files are `Scripts/Testing/TestRun.cs`, compiled into every build because the game asks it a question, and
`Scripts/Testing/PlayerTest.cs`, which a distribution build leaves out. The interface as built (T07 fixed the first sketch of this page
against the three suites; the differences are listed below it):

```csharp
namespace PryingEyes.Testing
{
    /// What the game may ask about the run. No test type and no test flag is named outside the tests.
    public static class TestRun
    {
        public static bool Dedicated;                   // a test owns this process: the automatic run must not quit by itself
        public static string OutDir(string folder);     // -testout DIR, or unity_logs/<folder> of the checkout; created
    }

    public abstract class PlayerTest : MonoBehaviour
    {
        protected abstract string Suite { get; }        // "traptest": the name on the result line and in result.json
        protected abstract IEnumerator Run();           // the checks
        protected virtual string PhotoFlag { get; }     // "-trapshots", or null for a test that takes no photographs
        protected virtual string OutFolder { get; }     // under unity_logs/ without -testout: the suite's name, or the folder its page names ("trappole")
        protected virtual void Finish();                // after the sequence, however it ended, before the last check
        protected int Checks { get; }                   // made so far
        protected int Failed { get; }                   // failed so far
        protected bool Photos { get; }                  // the photo flag is on
        protected bool Check(string name, bool ok, string detail = "");
        protected void Photo(string name);              // into the output folder, listed under artifacts; nothing unless the photo flag is on
        protected string PhotoPath(string name);        // the file for a photograph the test frames itself (a hidden weapon, a camera of its own); listed under artifacts
        protected virtual void OnDestroy();             // stops counting exceptions; a test that owns something to release overrides it and calls base.OnDestroy()
        protected static void InstallWhen<T>(string flag) where T : PlayerTest;      // one line in the test's own load hook
    }
}
```

A test is then its checks and four lines of declaration (`Scripts/MineTest.cs` is the shortest):

```csharp
public sealed class MineTest : PlayerTest
{
    protected override string Suite { get { return "minetest"; } }
    protected override string PhotoFlag { get { return "-mineshots"; } }
    protected override string OutFolder { get { return "mine"; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { InstallWhen<MineTest>("-minetest"); }

    protected override IEnumerator Run() { /* ... Check("a un metro la fa scoppiare", ok); ... */ }
}
```

What the base class does, in the order of the old private copies: it counts the exceptions the game logs (`LogType.Exception`), runs
`Run()` (an exception in it ends the sequence, is logged and fails the check `sequenza completa`), calls `Finish()`, adds the check
`nessuna eccezione runtime`, writes `result.json`, prints the result line and quits with 0 when every check passed and 1 otherwise. Each
check is still the log line `[PryingEyes] <suite> ok <name>` or `... FALLITO <name>`, in the order of the calls. `InstallWhen` sets
`TestRun.Dedicated` when the flag is on the command line, in the load hook, before the first `GameManager.Update`; `GameManager` reads
`TestRun.Dedicated || trailer || HasAnyArg(LegacyDedicatedFlags)`, the legacy array being the flags of the suites that are not on the
harness yet, one per line, which T08 empties and deletes.

What it removed: the `Check`, the log hook, the loop and the exit of each test that is on it; the three flags of those tests in
`GameManager.cs`; the three literals that started with `unity_logs` and depended on the working directory. What it keeps: the result
line, so every existing script and habit still works, and the exit codes 0 and 1.

Where the first sketch was wrong or short:

- `OutDir` takes the name of a folder, not of a suite: the three suites keep the folders their pages name (`trappole`, `botti`, `mine`),
  and a new suite defaults to its own name. It resolves `unity_logs/` against the checkout (through `TestPaths.LogFolder`, which `TestPaths.Scratch` uses too, as `TestPaths.Out` does for `hotel_build/`), not
  against the working directory, so a run started from another folder still writes where the page says.
- `HoldFire` is not there: the three suites are started with `-nofire` and none of their flags is in the list that stops the bot from
  firing. The first suite that needs it adds it (T08).
- `Photo` is what the three suites' private `View` did (the tick of the light pool, the shot with the HUD); `PhotoFlag` and `Photos`
  replace their `shots` field; `Failed`, `Checks` and `Finish` are what they did with their private counters (`if (failed > 0) yield
  break;` and `PlayerController.botDir = Vector3.zero` after the sequence).
- `PhotoPath` and the virtual `OnDestroy` came with the second pull request of T08. Four of its suites frame their photographs themselves
  (`ImpactTest` hides the weapon and leaves the HUD out, `MapTest` shoots without the tick of the light pool, `MeleeTest` and `VariantiTest` add a
  camera of their own), so they ask the base class for the file and keep their `CamShot`; and `MeleeTest` owns a render texture to release, which a
  second `OnDestroy` would have hidden from the base class (Unity calls only the most derived one).
- The exit code 2 ("this floor cannot host the test") is not in the base class: none of the three needs it. The stealth suites call
  `Application.Quit(2)` themselves; T08 adds a call to the base class when it ports the first of them.
- What `Dedicated` stops is the automatic quit of `-autotest` and nothing else, as the chain it replaced did; the sketch said the run
  "must not report, photograph or quit", which was more than the code ever did.

## Hazards and isolation

| Hazard (F11) | Today | Proposed | Task |
| --- | --- | --- | --- |
| Settings in the registry, shared with the maintainer's game | rule 6; `run_split.ps1` saves and restores the key around a windowed run | declared as `hazards: registry`; the runner saves and restores the key for every such suite (T06, done); later, settings kept in memory during automated runs so that the hazard disappears | T14 |
| Graphics card and screen | "do not run while someone plays", in prose | `needs: gpu`, `hazards: screen`; refusal while another player is running (T06, done) | |
| UDP port 7807 and `$env:TEMP\pryingeyes-coop` for every checkout | `run_coop.ps1` takes a free UDP port for each run (`-Port`) and logs to `unity_logs/coop` of its checkout (`run_split.ps1`: `unity_logs/split`); two agents in two worktrees can run co-op checks at once | the log folder is the job folder when the studio runs the check | T11 (done), T06 (done) |
| Tracked files rewritten by runs (`hotel_build/autotest.txt`, `auto_shot*.png`) | the automatic run writes its report, its two timed screenshots and the bot's default log to `unity_logs/player/` (`TestPaths.Scratch`); `hotel_build/` is rewritten only by what a flag or an editor tool asks for | the tracked files and those outputs stay as they are until the maintainer decides ([modular-roadmap.md](modular-roadmap.md#decisions-that-are-the-maintainers)) | T12 (done) |
| Tracked files rewritten by builds (materials, `Torre.unity`, settings assets) | rules 4 and 10: stage exact paths | measure what a build rewrites and why, then decide | T13 |
| One editor per checkout | rule 9; the project lock | `tools/worktree.ps1` (exists); `doctor` | T09, T10 |
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
| `unity_logs/studio/<job>/report.json` | the engine; for an in-player suite the profile adds `player` (T06) | agents, the gallery, the gate's summary | `schemaVersion` 1, exists |
| `studio.py suites --json`, `doctor --json` | the engine and the profile | agents, the gate | 1, new |
| `tools/player_tests.json` | people and agents, checked by its test | the profile | `schemaVersion` 1, exists (T05) |
| `result.json` of a player run | the harness in the game | the profile's runner | `schemaVersion` 1, exists (T07, T08) |
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
