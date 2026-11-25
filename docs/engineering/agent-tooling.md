# Tools and interfaces for AI agents: design

Most of this repository was written by AI agents working at the same time in separate worktrees, and it will keep being so. This page
designs the command line and the data formats that make that work faster and safer: how an agent finds what to run, runs it, reads
the result and stays out of the way of the others. It starts from what exists and from the findings of
[architecture-audit.md](architecture-audit.md) (F5 to F11), and adds as little as possible.

State: partly built. `tools/code_census.py` (written with the audit), `studio.py test desert` and `test plan` (T01, T02), the game's rules in
`check_suite.py` (T04), `tools/worktree.ps1` (T10; how it is used is in [building-and-shipping.md](building-and-shipping.md#branches-and-worktrees)),
`tools/player_tests.json` (T05) and, since T03, `studio.py suites` and the hazards (`--allow`) exist, since T06 the suites of the
manifest run through `studio.py test`, and since T09 `studio.py test compile`, `studio.py doctor --json` and `unity_logs/ship_last.json`;
the rest below is a design. Each addition has a task in
[modular-tasks.md](modular-tasks.md) with the checks that prove it; the order is in [modular-roadmap.md](modular-roadmap.md). Claims
about durations on this page were not measured for it and are marked.

## What an agent needs, and what it finds today

| Need | Today | Finding | Proposed |
| --- | --- | --- | --- |
| Know which checks exist, what each needs and how long it takes | 381 lines of prose in [testing.md](testing.md) | F6, F7 | a manifest of the suites and `studio.py suites` |
| Start any check the same way | five kinds of command, each with its options | F10 | every check is a suite: `studio.py test <name>` |
| Read a result as data | a sentence in a log, found by a regular expression | F6 | `result.json` from the player, findings in `report.json` |
| Know in seconds that the code compiles | `studio.py test compile` (T09); before it, a trick that was written nowhere | F10 | `studio.py test compile` |
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
| `studio.py doctor [--json]` | exists; `--json` since T09 | the tools are installed; with `--json` also: is the editor or a player open on this checkout, is there a `Library`, which commit is the verification build, how the last gate on `preview` ended ([below](#doctor-the-state-of-this-checkout)) | text, or JSON |
| `studio.py suites [--json]` | exists (T03) | every suite with what it needs, its hazards and its usual duration | one line each, or JSON |
| `studio.py test <suite> [--runtime EXE] [--allow HAZARD]` | exists; `--allow` since T03 | runs one suite as a job under the project lock | `unity_logs/studio/<job>/report.json` |
| `studio.py test desert` | exists (T01) | the desert lab: 107 checks, one finding each | `report.json` |
| `studio.py test plan` | exists (T02) | the fingerprint of the floor generator: 48 plans and the protocol revision, one finding each | `report.json` |
| `studio.py test compile` | exists (T09) | the editor compiles the scripts and exits ([below](#test-compile-does-it-compile)) | one finding per compiler error, with its file and line |
| `studio.py test <in-player suite>` | exists (T06) | starts the verification build with the flags of the manifest (87 suites, [below](#running-the-suites-of-the-manifest)) | `report.json` with one finding per check, and `player`: the verdict, where it came from, how many checks |
| `studio.py catalog`, `asset`, `gallery`, `queue`, `job`, `report latest` | exist | content pipeline, evidence, queue ([production-studio.md](production-studio.md)) | as today |
| `python tools/check_suite.py` | exists; has the game rules since T04 | boundaries, the game's rules about tests, documentation | findings as text, exit code |
| `python tools/code_census.py [--json FILE]` | exists | sizes, references, flags, tests, global state | text and JSON |
| `tools/ship.ps1` | exists; `ship_last.json` since T09 | gate, commit, build, smoke, push, the maintainer's game | text status, and the same as data ([building-and-shipping.md](building-and-shipping.md#shipping-a-change-toolsshipps1)) |
| `tools/worktree.ps1` | exists (T10) | a worktree ready for Unity; removing one safely | JSON summary |

The pages that list commands for people ([testing.md](testing.md), [building-and-shipping.md](building-and-shipping.md)) keep doing so.

### `suites`: what can be run

The engine's `Suite` has optional, free-text fields that the profile fills: `kind`, `needs`, `hazards`, `seconds`, `about` (empty by default,
so a suite written without them works as before). The engine interprets only `hazards`; `needs` is for whoever reads the list, the gate
first. `studio.py suites` prints a table, one line per suite in the order of the profile, and `--json` prints the same as data; neither
starts a job, takes the lock or writes a report. `seconds` is the usual duration, `0` while nobody has measured it: on the gate's runner for
a lab, on the maintainer's PC for `compile` (the runner has no editor).
The shape below is the real one, the entries are an example (the durations of `saves`, `traptest` and `uitest` are invented: the suites of
the player, which exist since T06, have `seconds: 0`, nobody has measured them; `compile` is as it is registered, with a measured duration,
[below](#test-compile-does-it-compile)):

```json
{
  "schemaVersion": 1,
  "suites": [
    {"name": "saves", "kind": "lab", "needs": [], "hazards": [], "seconds": 20, "about": "Save documents, migrations, character and run files"},
    {"name": "compile", "kind": "editor", "needs": ["editor"], "hazards": [], "seconds": 15, "about": "The scripts compile: the editor, started with no method, compiles them and quits; each compiler error is a finding with its file and line"},
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
parentheses: the standard line of the harness, with `Role` after the suite). A test that derives from `PlayerTest` writes `result.json` and prints the standard line too. Counted from the sources on 25
November 2026, with the seventh pull request of T08, 83 of the 87 entries say their result in a form the runner reads: the ones on
`PlayerTest`, the others that print the standard line (two of them runners: `seatstest`, `splitdesertotest`) and the 17 co-op scenarios,
which print the co-op forms. The other 4 print theirs in another form or not at all: `citytest`, `displaytest`, `procbot` and `perftest`. The studio starts them and fails them with
`PLAYER_RESULT_MISSING`, whatever the process did, until they are ported to the harness (T08, whose pull requests shorten this list): the
runner does not guess a verdict from an exit code (`padtest` quit with 0 and printed no result at all until its steps were repaired).

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
`RenderTexture.Create` (the showroom is drawn into a texture), so they keep `gpu`. `padtest` failed twelve checks and quit with no result, and T06 kept `gpu` for it; T08 found that the card had nothing to do with it: the main menu has a `Wiki` button between `Come si gioca` and `Impostazioni` (`GameMenu.Design.cs`), the steps did not count it, and they ended by pressing A on `Esci`, which quits the player (with `-nographics` and with the card the 53 lines of checks were the same, 41 ok and 12 failed). The steps now name the button they want instead of counting presses, and `padtest` passes 89 of 89 with `-nographics` and with the card, the same 89 checks in the same order: its entry carries `-nographics` and no `gpu`. T08 found two more while it ported them: `meleetest` renders a camera into a texture on every frame (without a window the render graph throws once per frame and 11 of its 120 checks fail) and `relictest` fails one of its 63 checks and throws in the render; both entries now run with the card and say `gpu`.
`displaytest` was not run: it declares `hazards: registry`, and it asserts the size of the real screen after changing it, which
`-nographics` cannot give.

For an entry with a `runner` the verdict is made of the logs that the script left in the job folder (every `.log` and `.txt` but the
script's own output). Of several processes (a co-op scenario) those that left a result must all say they passed, their checks add up and
`checks` is compared with the sum; a process that left nothing is not an error (a scenario kills one on purpose: the script owns that, and
its exit code is checked like any other), but at least one must have left a result.

### `test compile`: does it compile

The editor started in batch mode with no method (`Unity.exe -batchmode -nographics -projectPath unity/PryingEyes -quit -logFile <job>/compile-unity.log`)
imports what changed, compiles the scripts and quits, with exit code 1 when they do not compile. It is the fastest answer to "did I break the
build" that needs no player. As a suite (`kind: editor`, `needs: editor`) it runs under the project lock, so it waits for any other job on
this checkout, and it starts nothing while an editor has this project open (`PROJECT_IN_USE`): one editor per project, not per machine, so
the editor of another worktree is no obstacle. The gate does not run it, because the runner has no editor.

The editor prints every compiler error three times (in the compiler's own output, in the report of the failed compilation and alone), as
`Assets\_PryingEyes\Scripts\Foo.cs(7,21): error CS1002: ; expected`. The suite keeps each once, in the order of the log, and each is a finding
of `report.json` (the usual shape, plus `file`, `line` and `column`, which a tool would otherwise cut out of `subject`):

```json
{"code": "COMPILER_ERROR", "subject": "Assets/_PryingEyes/Scripts/Foo.cs:7", "observed": "CS1002: ; expected", "expected": "no compiler error",
 "passed": false, "evidence": "unity_logs/studio/<job>/compile-unity.log", "file": "Assets/_PryingEyes/Scripts/Foo.cs", "line": 7, "column": 21}
```

An error that names no file (a missing assembly) has `file: ""`, `line: 0` and the subject `(no file)`. The job then fails with
`COMPILE_ERRORS`, whose message names the first error and where the log is, and the command exits 1. A compile without errors is one finding,
`SCRIPTS_COMPILE`, passed, which carries the time the editor says it spent compiling. Warnings are not findings. The other ways to fail:

| Code | When |
| --- | --- |
| `PROJECT_IN_USE` | an editor has this project open; nothing was started |
| `TOOL_MISSING` | there is no editor at the path the profile works out from `ProjectVersion.txt` (`UNITY_EDITOR_EXE` overrides it) |
| `COMPILE_FAILED` | the editor quit with a code other than 0 and printed no compiler error: it did not get as far as compiling (a licence, a broken project). The message gives the last line of its log |
| `COMPILE_LOG_MISSING` | the editor quit with 0 and left no log |
| `STEP_TIMEOUT` | the engine stopped the editor after 1200 seconds |

What it takes, measured on 2026-11-24 on the maintainer's PC while it was shared (a `PryingEyes.exe` was running, the CPU was at 17 to 57 %
when each run started and no other editor ran; the times are the `seconds` of the step in `report.json`, the whole life of the editor):

| Case | Time |
| --- | --- |
| `test compile` after a change in some scripts (a merge, and a scratch file removed again) | 15.7 s and 13.3 s; the editor's own log says 9.6 s and 6.1 s of it were the compiler |
| `test compile` over a scratch file with a syntax error in `Assets/` | 4.4 s, exit 1 |
| the same command with nothing changed (not through the studio) | 6.1 s |
| the first compile in a new worktree with a copied `Library` (not through the studio; [building-and-shipping.md](building-and-shipping.md#measured-on-the-real-project) has the other measurement) | 90.8 s |

`seconds: 15` in the registry is the second case. On a worktree without a `Library` the same command imports every asset first, which takes
long and writes gigabytes: `doctor --json` says `"library": false`, and `tools/worktree.ps1` avoids it (not measured: it would write them).
A compile leaves `Assets/DefaultPrefabObjects.asset` and `ProjectSettings/ProjectAuditorSettings.asset` rewritten with other line endings
and the same text, like a build ([building-and-shipping.md](building-and-shipping.md#what-a-build-rewrites)): stage exact paths.

### `doctor`: the state of this checkout

`studio.py doctor` keeps its text form: a job that loads the catalogs, checks the tools and prints `STUDIO: ...`. With `--json` it starts no
job, takes no lock and writes nothing: it reads the state of the checkout now, prints it, and exits 1 while `problems` is not empty (the
catalogs are not loaded, so it works when one is broken). Rule 8 of `AGENTS.md` asks every session to look at the last gate before
working; this answers that and the other questions an agent asks before it starts. The output on a worktree that has no verification
build yet (the run id of the gate is shortened):

```json
{
  "schemaVersion": 1,
  "ok": true,
  "tools": {"unity": "found", "blender": "found", "dotnet": "10.0.403"},
  "editorOnProject": null,
  "playersRunning": [],
  "library": true,
  "verificationBuild": {"present": false, "commit": null, "mode": null, "sourcesNewerThanBuild": null},
  "gate": {"branch": "preview", "status": "completed", "conclusion": "success", "commit": "256ae32", "url": "https://github.com/<owner>/<repo>/actions/runs/<id>"},
  "worktree": {"linked": true, "branch": "claude/example"},
  "problems": []
}
```

| Field | What it says |
| --- | --- |
| `ok`, `problems` | `ok` is true when `problems` is empty. Each problem is `CODE: message`: `TOOL_MISSING` (Unity, Blender or dotnet), `PROJECT_IN_USE` (an editor has this project open: no compile, no build here), `GATE_NOT_GREEN` (the last gate on `preview` ended in anything but success: rule 8 says to fix that first; a run still going is not a problem), `PROCESS_LOOKUP_FAILED` (the running programs could not be listed). A library that is missing is a fact, not a problem: a worktree made with `-NoLibrary` is meant to have none |
| `tools` | `found` or `missing` for Unity and Blender; for dotnet the version it prints, or `missing` |
| `editorOnProject` | `{"pid": N}` when the editor has this checkout's project open, else `null` (also `null` when the lookup failed: `problems` says so) |
| `playersRunning` | every `PryingEyes.exe` of the machine: `pid`, `path` and `thisCheckout` (it runs from this checkout's `Builds/`). The suites that draw refuse to run while any is open; `null` when the lookup failed |
| `library` | whether `unity/PryingEyes/Library` exists; without it the first compile imports every asset |
| `verificationBuild` | the build the checks run on (`Builds/Audit`): `present`; `commit` and `mode` from `unity_logs/ship_built.json`, which `tools/ship.ps1` writes, when that marker is about this folder; `sourcesNewerThanBuild`, the number of files under `Assets`, `Packages` and `ProjectSettings` modified after the build's `Assembly-CSharp.dll` (the exe is only a stub: its date says nothing), derived files not counted. Without a build, `null` |
| `gate` | the last run of the gate on `preview`, by `gh run list` (rule 8): `status`, `conclusion` (`null` while it runs), `commit` and `url`. `null` without the `gh` command, without a network or when it answers nothing usable |
| `worktree` | `linked` (this checkout is a linked worktree, not the main one) and `branch` (`null` on a detached HEAD) |

A fact that cannot be learned is `null`, never an invented answer; no fact starts a tool that changes anything.

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

State: built (T07) and on 42 suites: `TrapTest`, `BarrelTest` and `MineTest` (T07), the ten of the first pull request of T08 (`MedalTest`,
`FeelTest`, `EyesTest`, `FlinchTest`, `MantleTest`, `MagTest`, `LampTest`, `ShaftTest`, `RagdollTest`, `EquipTest`), the nine of the second
(`LampadariTest`, `PowerTest`, `VariantiTest`, `ImpactTest`, `MapTest`, `MeleeTest`, `JellyTest`, `SplitScreenTest`, `MusicTest`), the nine of the third (`RelicReviewTest`, `CombatFeedbackTest`, `FlyingMeleeTest`, `PrefetchTest`, `CharacterTest`, `AnnexTest`, `SurvivalTest`, `GunplayTest`, `OpticsTest`), the three of the fourth (`PadMenuTest`, `ThirdPersonTest`, `TorchReviewTest`), the three of the fifth (`CattedraleTest`,
`SplitDesertTest` and `PistaCoopTest`, which stands for three entries of the manifest), the one of the sixth (`StealthTest`, which stands for eleven) and the four of the seventh (`GunTest`,
`LeanTest`, `WallTest` and `MagicTest`, which were procedures of `GameManager`); T08 ports the 12 others that
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
        public static bool HoldFire;                    // a test asks the bot of the automatic run not to shoot by itself (GameManager.noFire reads it)
        public static string OutDir(string folder);     // -testout DIR, or unity_logs/<folder> of the checkout; created
    }

    public abstract class PlayerTest : MonoBehaviour
    {
        protected abstract string Suite { get; }        // "traptest": the name on the result line and in result.json
        protected abstract IEnumerator Run();           // the checks
        protected virtual string PhotoFlag { get; }     // "-trapshots", or null for a test that takes no photographs
        protected virtual string OutFolder { get; }     // under unity_logs/ without -testout: the suite's name, or the folder its page names ("trappole")
        protected virtual void Finish();                // after the sequence, however it ended, before the last check
        protected virtual float TimeLimit { get; }      // seconds from the start of the player within which the run must end, 0 for no limit: past it the check "finita entro il tempo" fails and the run ends
        protected virtual bool HoldFire { get; }        // the bot of the automatic run must not shoot while this test runs (false by default); InstallWhen copies it into TestRun.HoldFire
        protected virtual string Role { get; }          // for a test that two processes run (the host and the friend of a co-op race): who speaks, after the suite name in every log line; null for the others
        protected virtual string ExceptionCheck { get; } // the last check: "nessuna eccezione runtime", the name a suite has always given it, or null for a suite that never made one
        protected virtual bool IsError(string message, LogType type);   // what the last check counts as an error: an exception, plus what a suite adds to it
        protected int Checks { get; }                   // made so far
        protected int Failed { get; }                   // failed so far
        protected bool Photos { get; }                  // the photo flag is on
        protected bool Check(string name, bool ok, string detail = "");
        protected void NotApplicable(string why);       // the floor cannot host this test: the check "il piano ospita la prova" fails with the reason and the exit code is 2 (1 if anything else failed); return from Run after it
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
`nessuna eccezione runtime` (or the name that `ExceptionCheck` gives, or none at all), writes `result.json`, prints the result line and quits with 0 when every check passed, with 2 when the test said `NotApplicable` and nothing else failed, and with 1 otherwise. Each
check is still the log line `[PryingEyes] <suite> ok <name>` or `... FALLITO <name>`, in the order of the calls. `InstallWhen` sets
`TestRun.Dedicated` when the flag is on the command line, in the load hook, before the first `GameManager.Update` (and `TestRun.HoldFire` when the test says
`HoldFire`: `GameManager.noFire` is `-nofire`, the flags of the suites that are not on the harness yet, or `TestRun.HoldFire`, asked every time); `GameManager` reads
`TestRun.Dedicated || trailer || HasAnyArg(LegacyDedicatedFlags)`, the legacy array being the flags of the suites that are not on the
harness yet, one per line, which T08 empties and deletes. A suite that says a `TimeLimit` is also watched by the clock, in a coroutine of the base class's own
(a suite's `Update` would hide one in the base class): past the limit the check `finita entro il tempo` fails and the run ends as above, whatever the sequence is still waiting for.

What it removed: the `Check`, the log hook, the loop and the exit of each test that is on it; the three flags of those tests in
`GameManager.cs`; the three literals that started with `unity_logs` and depended on the working directory. What it keeps: the result
line, so every existing script and habit still works, and the exit codes 0 and 1 (2 came with the sixth pull request of T08).

Where the first sketch was wrong or short:

- `OutDir` takes the name of a folder, not of a suite: the three suites keep the folders their pages name (`trappole`, `botti`, `mine`),
  and a new suite defaults to its own name. It resolves `unity_logs/` against the checkout (through `TestPaths.LogFolder`, which `TestPaths.Scratch` uses too, as `TestPaths.Out` does for `hotel_build/`), not
  against the working directory, so a run started from another folder still writes where the page says.
- `HoldFire` came with the seventh pull request, for the two that needed it: the stealth family and `GunTest` want the bot of the automatic run not to shoot
  (`Weapon` asks `GameManager.noFire`, which had a list of test flags). A test overrides the member, `InstallWhen` copies it into `TestRun.HoldFire` before the first frame, and
  the flag of the test leaves the list of `GameManager.Awake`: it still holds `-cooptest`, `-citytest` and `-desertotest` (not on the harness yet) and `-survivaltest` and
  `-cattedraletest` (on it, not moved yet).
- `Photo` is what the three suites' private `View` did (the tick of the light pool, the shot with the HUD); `PhotoFlag` and `Photos`
  replace their `shots` field; `Failed`, `Checks` and `Finish` are what they did with their private counters (`if (failed > 0) yield
  break;` and `PlayerController.botDir = Vector3.zero` after the sequence).
- `PhotoPath` and the virtual `OnDestroy` came with the second pull request of T08. Four of its suites frame their photographs themselves
  (`ImpactTest` hides the weapon and leaves the HUD out, `MapTest` shoots without the tick of the light pool, `MeleeTest` and `VariantiTest` add a
  camera of their own), so they ask the base class for the file and keep their `CamShot`; and `MeleeTest` owns a render texture to release, which a
  second `OnDestroy` would have hidden from the base class (Unity calls only the most derived one).
- `ExceptionCheck` and `IsError` came with the third. Four of its suites never counted exceptions (`RelicReviewTest`, `CharacterTest`, `AnnexTest` and
  `SurvivalTest`): they answer null, so that their checks stay the ones they always had (a port changes how a suite is written, not what it checks);
  `OpticsTest` called its last check `nessuna eccezione`; `PrefetchTest` calls it `nessuna eccezione e nessun thread fallito` and counts as an error also
  the warning that the sound threads print when a sample cannot be computed off the main thread. The exceptions are counted for all of them and
  `result.json` carries the number.
- Three small differences are accepted, all in what a log says and none in what it checks: when a sequence throws, the check that fails is
  `sequenza completa` for every suite (`GunplayTest` and `OpticsTest` called it `copione completo`); `GunplayTest` logged only its failures and now logs
  every check; `OpticsTest` printed `ok <name> <detail>` and now prints `ok <name> (<detail>)`, like the others.
- The fourth took three more suites with no new member. Two things stay as they were: `PadMenuTest` keeps its own `-padshots FOLDER` (a real window and
  `ScreenCapture`, outside the harness's folder and `artifacts`), and `TorchReviewTest` lost `-torchout`, a flag for the folder of its pictures that
  nothing used: they follow `-testout`, or `unity_logs/torch-review/` of the checkout, and are all listed under `artifacts`. Two suites of the plan are
  left out on purpose, because they cannot be run without the maintainer's word (`hazards: registry`) and a port by reading could not be shown equal:
  `DisplayTest` prints its own `DISPLAYTEST` lines, which `tools/check_display.py` reads, and puts the screen back and waits half a second after its
  sequence, which `Finish()` cannot do; `PresentationTest` (`-uitest`) puts the player's preferences back when the process quits and writes `result.txt`
  after the verdict, which the base class has no place for yet.
- The fifth took three suites and two members. `TimeLimit`: five suites (`DesertTest`, `SplitDesertTest`, `PistaCoopTest`, `PistaMenuTest`, `PistaTest`) fail the check
  `finita entro il tempo` and end the run when a number of seconds have passed since the player started, whatever their sequence is waiting for; the base class
  watches the clock in a coroutine of its own. Two are on the harness now. `PistaCoopTest` counted its 420 s from the start of its own sequence and now counts
  them from the start of the player, the few seconds the player takes to start less, and only a stuck run can reach either. `Role`: `PistaCoopTest` runs in two
  processes, the host and the friend, and prints who speaks after the suite name (`pistacooptest ospite ok ...`, the form that `tools/player_log.py` reads):
  `Role` puts it in every check line and in the result line, and the test gives each process its own `OutFolder` (`pistacooptest-ospite`,
  `pistacooptest-ospitato`), so that the two `result.json` do not overwrite each other. The closing check of `SplitDesertTest` and `PistaCoopTest` counts errors as well
  as exceptions (`IsError`; `SplitDesertTest` still lets through the harmless `Failed to capture screen shot` of a run without a window); when it failed it carried
  the count and the first message and now it fails bare, because the log holds the errors. `CattedraleTest` never counted exceptions: null. The photographs stay as
  they were: those of `CattedraleTest` (`-cattedraleshots`, 1600x900, `hotel_build/cattedrale`) and of `SplitDesertTest` (`hotel_build/deserto`, and the monitors'
  through `run_split.ps1`) are not on `PhotoPath`, only the flag of the first moved to `PhotoFlag`; the `Photo` of `SplitDesertTest` is now `MonitorPhoto`, so as
  not to hide the base class's.
- The sixth took one class, eleven suites and one member. `StealthTest` is one class over four files that runs eleven suites of the manifest: the flag after
  `-stealthtest` (`-roundtest`, `-knifetest`, `-droptest`, `-booktest`, `-gridtest`, `-pushtest`, `-holescan`, `-alientest`, `-temitest`, `-refshot`, or none) names
  the suite, so each has its own result line, its own `result.json` and the name that the manifest gives it. The four files are wrapped in
  `#if !PE_DISTRIBUTION_BUILD` (they were part of every release) and `StealthRig.cs` no longer adds the component: the test installs itself at load
  and waits for the floor and for the rig, as the others do. What was a `passed &=` is a named check, so each suite says what it checked (the main
  suite had no check at all: the twelve conditions that decided its exit are twelve checks); the private `Check` of six of its methods is the base
  class's now, and the lines that it wrote into `hotel_build/furtivo/*.txt` are in the log and in `result.json`, the files keeping the narrative and
  the closing verdict line. `ExceptionCheck` is null: the family never counted exceptions, and the three suites that take photographs without a graphics card
  (`stealthtest`, `alientest`, `temitest`) log the renderer's exceptions in every run, which the runner filters as noise, so the default closing check would
  fail runs in which nothing is wrong. That check is also what catches a nested sequence that throws (Unity runs a nested sequence by itself and only logs
  what it throws, so the base class sees the exceptions of its own `Run` only), and this family has none: each suite runs inside `RunSuite`, which fails
  the check `sequenza completa` when the suite's own sequence throws. A helper sequence that the suite yields (`Hit`, `Light`, `DriveRound`, `Face`, `HeadShot`) is
  still run by Unity itself: what it throws is logged and ends the sequence, and nothing quits the process before the runner's timeout, as before the port. Two things are not as they were: `-roundshots` used to quit with 0 whatever the checks said and now ends like any run, and a
  sequence that throws fails the check `sequenza completa` and exits with 1, where it used to leave the process waiting for the runner's timeout.
- `NotApplicable(why)` is the exit code 2 ("this floor cannot host the test"): the check `il piano ospita la prova` fails with the reason, the sequence
  returns, and the process quits with 2 unless another check failed too, which outranks it (exit 1). Six suites use it (`roundtest`: no sealable zone;
  `knifetest`, `droptest` and `pushtest`: no open spot; `booktest`: a sanctuary is missing; `gridtest`: not a grid plan) and `PerfProbe` exits 2 in the same
  way for `-hordeperf` without a real graphics card. A suite cannot keep it to itself: `Application.Quit(2)` from inside the sequence is overwritten by the
  base class's own `Application.Quit(0)` when the sequence returns (the same frame), and the run prints `SUPERATA` with nothing checked; the check and
  `result.json` are the base class's too. `booktest` now tests that its sanctuaries exist before it checks them, so that a floor without one is the
  plan's answer and not three failed lines (it used to exit 2 after them).
- What `Dedicated` stops is the automatic quit of `-autotest` and nothing else, as the chain it replaced did; the sketch said the run
  "must not report, photograph or quit", which was more than the code ever did.

## Hazards and isolation

| Hazard (F11) | Today | Proposed | Task |
| --- | --- | --- | --- |
| Settings in the registry, shared with the maintainer's game | rule 6; `run_split.ps1` saves and restores the key around a windowed run | declared as `hazards: registry`; the runner saves and restores the key for every such suite (T06, done); later, settings kept in memory during automated runs so that the hazard disappears | T14 |
| Graphics card and screen | "do not run while someone plays", in prose | `needs: gpu`, `hazards: screen`; refusal while another player is running (T06, done) | |
| UDP port 7807 and `$env:TEMP\pryingeyes-coop` for every checkout | `run_coop.ps1` takes a free UDP port for each run (`-Port`) and logs to `unity_logs/coop` of its checkout (`run_split.ps1`: `unity_logs/split`); two agents in two worktrees can run co-op checks at once | the log folder is the job folder when the studio runs the check | T11 (done), T06 (done) |
| Tracked files rewritten by runs (`hotel_build/autotest.txt`, `auto_shot*.png`) | the automatic run writes its report, its two timed screenshots and the bot's default log to `unity_logs/player/` (`TestPaths.Scratch`); `hotel_build/` is rewritten only by what a flag or an editor tool asks for | the tracked files and those outputs stay as they are until the maintainer decides ([modular-roadmap.md](modular-roadmap.md#decisions-that-are-the-maintainers)) | T12 (done) |
| Tracked files rewritten by builds (materials, `Torre.unity`, settings assets) | measured ([building-and-shipping.md](building-and-shipping.md#what-a-build-rewrites)): most of what `git status` lists after a build is a file Unity saved with LF over a CRLF checkout, with the bytes of the commit, and `git diff --name-only` lists what differs; rules 4 and 10: stage exact paths | the `.gitattributes` and the code changes proposed there, when the maintainer decides | T13 (done) |
| One editor per checkout | rule 9; the project lock | `tools/worktree.ps1`; `doctor --json` names the editor that holds a checkout and `test compile` refuses to start over it | T10 (done), T09 (done) |
| Two changes to the wire format at once | found at merge | one at a time, said in [modularization.md](modularization.md#rules-for-new-code) | none |

## A session with these tools

The path the tools should make the easy one, for a change to the game:

1. `python tools/studio.py doctor --json`: the gate on `preview` is green, nothing holds the checkout.
2. `tools/worktree.ps1 -New <name>` unless this session owns the editor's checkout.
3. Make the change. Logic that needs no engine goes under `Rules/` with its checks in a lab.
4. `python tools/studio.py test compile`, then the labs of what was touched (`test saves`, `test desert`...).
5. A scripts-only verification build: `tools/ship.ps1 -NoPush -NoExe -Mode scripts` builds into `Builds/Audit` without publishing. In a new
   worktree there is no complete build there yet, so the script falls back to `BuildPlayer.Windows`, which packs the committed `Torre.unity`:
   make the first build the full `BuildPlayer.Torre` ([the scene of a new worktree](building-and-shipping.md#the-scene-of-a-new-worktree)).
6. `python tools/studio.py suites --json`, then `test <suite>` for the in-player suites of the area. Read `report.json`, not the log.
7. `python tools/check_suite.py` and the unit tests; `tools/ship.ps1` with the exact paths; a pull request against `preview`.
8. After the merge: `tools/worktree.ps1 -Remove <name>`.

## Formats and who owns them

| File | Written by | Read by | Version |
| --- | --- | --- | --- |
| `unity_logs/studio/<job>/report.json` | the engine; for an in-player suite the profile adds `player` (T06) | agents, the gallery, the gate's summary | `schemaVersion` 1, exists |
| `studio.py suites --json`, `doctor --json` | the engine and the profile | agents, the gate | `schemaVersion` 1, exists (T03, T09) |
| `tools/player_tests.json` | people and agents, checked by its test | the profile | `schemaVersion` 1, exists (T05) |
| `result.json` of a player run | the harness in the game | the profile's runner | `schemaVersion` 1, exists (T07, T08) |
| `python tools/code_census.py --json` | the census | the audit, the gate's summary | 1, exists |
| `unity_logs/ship_last.json` | `tools/ship.ps1` | agents | `schemaVersion` 1, exists (T09) |

A change that removes or renames a field raises the version of that file; adding a field does not.

## Not proposed

| Not proposed | Why |
| --- | --- |
| An MCP server or any long-running service | The agents that work here run shell commands; a service adds a process to keep alive and nothing they cannot already do |
| A dashboard | The evidence gallery exists (`studio.py gallery build`) |
| Analysis with the C# compiler (Roslyn) | It cannot resolve types without Unity's assemblies, which the gate's checkout does not have. Name matching is enough to rank and to watch |
| Unity's own test framework for the in-player checks | They prove the built player from a real start, which is where the defects recorded in [../history/quality-audit.md](../history/quality-audit.md) were found. A second framework would mean two harnesses |
| Rewriting `tools/ship.ps1` in Python | Its lines are solved traps; it gained a data file (`ship_last.json`), not a new language |
| `studio.py test affected` (which suites a change touches) | Useful, but it needs a map from files to suites that somebody must keep. Reconsider when the manifest has been in use for a while and its `source` and `page` fields prove to be maintained |
