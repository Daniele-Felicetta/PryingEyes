# Modular architecture: task briefs

Twenty-three tasks, each written so that an agent can carry it out alone: what to do, why, what must be true at the end and how to
prove it. Their order, their state and what can run in parallel are in [modular-roadmap.md](modular-roadmap.md). The reasons are in
[architecture-audit.md](architecture-audit.md) (findings F1 to F14), [modularization.md](modularization.md) (boundaries B1 to B5),
[package-extraction.md](package-extraction.md) and [agent-tooling.md](agent-tooling.md).

The briefs were written from the code at commit `4042709` and nothing in them has been tried. Where a brief names a file, a line or a
number, check it against the code first: if the code has moved, the code wins and the brief is corrected in the same pull request.

## How to carry out a task

1. Read `AGENTS.md`, the brief, and the sections it links. Look at the last gate on `preview` (`gh run list --branch preview --limit 1`):
   if it is red, that comes first (rule 8).
2. Check the task's row in [modular-roadmap.md](modular-roadmap.md#status): its dependencies are `done`. Check that nobody else has it:
   `gh pr list --state open` shows no pull request with the task's id in its title. Put the id (`T07`) in the name of your branch and in
   the title of your pull request, and open the pull request as a draft with your first commit, so that the claim is visible.
3. Work in your own worktree and branch (rule 9). A task marked **editor** needs a worktree with an imported `Library` and no other
   Unity on it; one marked **build** needs a verification build in `unity/PryingEyes/Builds/Audit` of the same worktree.
4. Do what the brief says and nothing else. What you find on the way goes in the pull request under "Found, not done".
5. A task that touches game code records the named suites **before** the change (check names and results) and compares them **after**.
   Structure and behaviour never change in the same pull request.
6. Run every command under *Verify* and put the lines that prove each acceptance criterion in the pull request.
7. `python tools/check_suite.py` and `python -m unittest discover -s tools -p "test_*.py"` pass. Ship with `tools/ship.ps1` and exact
   paths (rule 4), open a pull request against `preview`, wait for its gate.
8. In the same pull request: the task's row becomes `done` with the pull request number; every page the change made wrong is corrected
   (rule 13); if a number of [modularization.md](modularization.md#how-progress-is-read) moved, its table is updated from a fresh
   `python tools/code_census.py`.
9. If an acceptance criterion cannot be met, stop. Merge nothing partial; write what blocked it and set the row to `blocked`.

Never, whatever the brief seems to allow: `tools/check_presentation.py` or `-uitest` without the maintainer's word (rule 6); the
maintainer's game (`Builds/PryingEyes.exe`) for a check, or closing it (rule 11); `git add -A`; a forced push; deleting anything that
cannot be rebuilt or recovered.

Words used in the briefs: *lab* = a .NET project under `tools/` that compiles game files without Unity; *profile* =
`tools/studio_pryingeyes.py`; *engine* = `tools/studio_engine.py` and the modules it imports; *guard* = `tools/check_suite.py`;
*census* = `tools/code_census.py`; *smoke walk* = `python tools/check_navigation.py --seeds 1000 --floors 2`.

---

## Phase 0: gate what is already true

### T01. The desert lab in the gate

- **Why**: F9. `tools/desertlab` has 107 checks and no runner starts it.
- **Needs**: Python, the .NET 8 SDK. No Unity.
- **Touches**: `tools/desertlab/Program.cs`, the profile and `tools/test_studio_pryingeyes.py`, `.github/workflows/gate.yml`, rule 8 of
  `AGENTS.md`, [testing.md](testing.md), [building-and-shipping.md](building-and-shipping.md),
  [suite-architecture.md](suite-architecture.md).
- **Steps**
  1. In `Program.cs` make `Check` also collect each result (`code`, `subject`, `passed`, `observed`, `expected`), and add `--report FILE`,
     which writes `{schemaVersion, operation, ok, findings}` as `tools/survivallab/Program.cs` does. What it prints does not change.
  2. In the profile add `desert_state(job)` on the model of `saves_state`, the suite `desert`, and `tools/desertlab` to `tool_sources`.
  3. In `test_studio_pryingeyes.py` cover the new suite as the others are covered (the command it issues; a missing or failed report fails it).
  4. In `gate.yml` add `desertlab` to the labs that are compiled, a step that runs `python tools/studio.py test desert` with its own
     `STUDIO_OUTPUT`, and the lab to the summary.
  5. Correct the pages and rule 8: six labs, by name (the brief said five; `track` joined the gate after it was written).
- **Done when**
  - `python tools/studio.py test desert` exits 0 and its `report.json` holds one passed finding per check (107 at the audit; state the number).
  - Negative control: with one check broken on purpose in a local copy, the suite exits 1 and the report names that check. Revert it.
  - A sparse checkout made with exactly the patterns of `gate.yml` runs the new step (procedure: [suite-architecture.md](suite-architecture.md#where-the-gate-runs)).
  - The gate of the pull request is green and its summary has a row for the desert lab.
- **Verify**: `dotnet run --project tools/desertlab -c Release`; `python tools/studio.py test desert`; `python tools/studio.py report latest`; the gate.
- **Not in scope**: changing a check; adding the lab to `before_build` of the asset pipeline.

### T02. A fingerprint of the floor generator

- **Why**: F9. In co-op every player builds the floor from the seed, so a change to the generator must raise `CoopProtocol.Revision`
  (`Scripts/Coop/CoopWire.cs`). Today that is remembered, and a change is detected by comparing outputs by hand
  ([testing.md](testing.md), end of section 2.4).
- **Needs**: Python, .NET. No Unity.
- **Touches**: `tools/planlab/Program.cs`, a new `tools/fixtures/plan-fingerprint.json`, the profile and its test, `gate.yml`,
  [testing.md](testing.md), the protocol section of [../multiplayer/README.md](../multiplayer/README.md).
- **Steps**
  1. Add to `planlab` the command `fingerprint` (a new file, `Fingerprint.cs`; `Program.cs` routes to it): for seeds 1000, 2961, 3046,
     777, 114 and 4242, floors 1, 3, 5 and 8, in the lab's default layout and with `waves` (the layout the game uses), compute the SHA-256
     of the text that `dump` writes, in the invariant culture (`dump` prints its numbers in the culture of the machine: a decimal comma on
     the maintainer's PC, a point on the runner; the brief did not say); print one line per plan and write them with `--report FILE`.
  2. `fingerprint --update tools/fixtures/plan-fingerprint.json` rewrites the fixture, which also records the revision read from
     `CoopWire.cs` (a string such as `pe-coop-37`, not a number). It refuses (`PLAN_CHANGED_WITHOUT_REVISION`) when a hash changed and the
     revision did not.
  3. Add the suite `plan`: one finding per plan (`PLAN_FINGERPRINT`: observed and expected hash) and one that the fixture's revision is
     the one in `CoopWire.cs` (`PLAN_REVISION`).
  4. Add it to the gate, and say in the two pages what to do when it fails.
- **Done when**
  - The command printed twice is identical, and the gate's result equals the committed fixture.
  - Negative control, all local and reverted: a constant changed in `Rules/Plan/PlanGrid.cs` makes `test plan` fail and name the
    plans; `--update` refuses; after raising the revision it accepts.
  - The suite takes under 30 seconds on the gate's runner.
- **Verify**: `dotnet run --project tools/planlab -c Release -- fingerprint`; `python tools/studio.py test plan`; the gate.
- **Not in scope**: asserting what a plan must look like (`-gridtest` does); the furniture and the building of a floor, which need the
  engine and stay covered by the co-op scenarios; the desert generators (their lab checks repeatability).

### T03. One registry of suites, and the gate asks it

- **Why**: F9, F10. A lab has to be added in ten places; `desertlab` was left out of most. (Counted again before the change, seven labs
  were named in: the profile, twice besides each lab's own suite (the registry and the list of hashed sources); the workflow, three times
  (the compile step, one step per suite, the summary); `.gitignore`, a block of three lines each; rule 8, the README row, the comment of
  `tools/ship.ps1`, three pages of `docs/` and two of the wiki.)
- **Needs**: Python, .NET. No Unity.
- **Touches**: the engine and `tools/test_studio_engine.py`, `tools/fixtures/toy_project.py`, the profile, `gate.yml`, `.gitignore`,
  rule 8 of `AGENTS.md`, the pages that list the labs.
- **Steps**
  1. Engine: `Suite` gains `kind`, `needs`, `hazards`, `seconds`, `about` (defaults empty). New command `suites [--json]`
     ([agent-tooling.md](agent-tooling.md#suites-what-can-be-run)). `test` gains `--allow HAZARD` (repeatable) and fails a suite whose
     hazards are not all allowed with `SUITE_HAZARD_NOT_ALLOWED`. The engine names no game, tool or content: the guard checks it.
     (`queue add test` takes `--allow` too and refuses at submission, so a queued run cannot do what a direct one may not.)
  2. Profile: fill the fields for the existing suites (the labs need nothing; `gunplay` needs `runtime`). (`seconds` are what the
     gate's runner measured: 3 for the small labs, 20 for `desert`, 70 for `track`; `gunplay` has none yet. The list of hashed sources
     takes every `tools/*lab` folder instead of seven.)
  3. `gate.yml`: compile every `tools/*lab` project and run every suite whose `needs` is empty by reading `suites --json`; check each
     exit code; build the summary from the same list. No lab is named in the workflow. (One step runs the suites, each in its own log
     group; a failed one does not stop the others, and the step fails at the end naming them. The per-lab steps of the run page are
     gone: the summary has a row for each suite, with its seconds. A test of the profile reads the workflow to prove it names none.)
  4. `.gitignore`: one pattern for the lab projects and their `bin` and `obj` folders instead of a block per lab. (The folder of a lab
     must end in `lab`; any other project stays ignored by `*.csproj`. A test builds a repository in a temporary folder with the real
     `.gitignore` and asks `git check-ignore` about a lab nobody has written yet; the gate's sparse checkout lists `/.gitignore` for it.)
  5. Rule 8 and the pages point to `python tools/studio.py suites` instead of listing names. (`before_build` of the profile still runs
     five labs by name, without `desert` and `plan`: what runs before a build is a decision that was not part of this task.)
- **Done when**
  - `suites --json` lists every suite with its fields; the toy project's test covers the listing, a refused hazard and an allowed one.
  - On a throwaway branch, a check broken in one lab turns the workflow red and the summary shows which lab; revert.
  - `git status` is clean after building every lab, and `git check-ignore -v` gives the same answers as before for each lab's
    `.csproj`, `bin` and `obj`.
- **Verify**: the unit tests; `python tools/studio.py suites --json`; the gate on the pull request.
- **Not in scope**: the in-player suites (T05, T06).

### T04. The guard learns the rules about tests

- **Why**: F6, F8. Two production files name a test type, 14 test files ship in releases, and nothing stops either number from growing.
- **Needs**: Python.
- **Touches**: the guard and `tools/test_check_suite.py`, a new `tools/fixtures/game-boundaries.json`, `gate.yml`,
  [suite-architecture.md](suite-architecture.md), [testing.md](testing.md).
- **Steps**
  1. Add `check_game(root)` to the guard, built on `census()` of the census (absent game folder: no findings):
     `GAME_PRODUCTION_NAMES_TEST` (a file that is not a test names a test type), `GAME_TEST_NOT_EXCLUDED` (a test file without
     `PE_DISTRIBUTION_BUILD`), `GAME_PURE_FILE_NAMES_ENGINE` (a game file that a lab compiles names `UnityEngine` or `UnityEditor`),
     `GAME_BASELINE_STALE` (an entry of the baseline that no longer applies). Found when the rule was first run: a *lab* is a project
     that reaches no Unity assembly. The projects of `tools/multiplayer-check` that did (`unity-compile`, `fishnet-compile`; T20 retired the folder) compiled
     engine code against the editor's own libraries, and 14 of the files they compiled named `UnityEngine` or `UnityEditor` on purpose; counted as labs they would
     have been 14 violations that are not one, so the rule leaves them out. A fifth code, `GAME_BASELINE_INVALID`, reports a baseline that is
     not valid or has an entry without its reason.
  2. The baseline is the fixture: today's two files and 14 files, each with a reason (none for the engine rule). An entry can be removed, never added without a
     reason a reviewer accepts.
  3. In the gate, after the guard, print `python tools/code_census.py --section sizes --section cycles --section tests --section flags`
     into the run summary and upload its JSON with the lab reports. It reports; it does not fail. The census reads the `.asmdef` files of
     the game, which the sparse checkout of the gate did not have (without `Multiplayer/Core`'s, five of its files counted as `Assembly-CSharp`
     there; the assembly went with T20, the pattern stays for the ones to come): the pattern is added.
- **Done when**
  - The guard passes on the repository; each new code is produced by a fixture violation in the unit tests; removing a baseline entry
    whose violation still exists fails; fixing a violation and leaving its entry fails with `GAME_BASELINE_STALE`.
  - The new rules add at most about three seconds to `python tools/check_suite.py`. Time it before and after the change on an idle PC,
    alternating the two runs so that the load of the machine hits both alike, and state both times (at T04: 5.18 s and 7.84 s, +2.66 s).
    There is no ceiling on the total: an earlier version of this brief asked for less than five seconds, and that number fell when the
    wiki check, added after the brief was written, came to about 4.5 s by itself; making that check faster is a task of its own, not T04's.
- **Verify**: the unit tests; `python tools/check_suite.py`; the summary of the gate.
- **Not in scope**: failing on the number of references between folders; changing any game file.

---

## Phase 1: one shape for every check

### T05. The manifest of the in-player suites

- **Why**: F6, F7. Which flags a check needs, which game they select and what it disturbs is written only in prose.
- **Needs**: Python. No Unity: nothing is run.
- **Touches**: new `tools/player_tests.json`, `tools/player_tests.schema.json`, `tools/test_player_tests.py`.
- **Steps**
  1. Write one entry per suite from sections 3 to 6 of [testing.md](testing.md) and from the scenarios of `tools/coop/run_all.ps1`, with the
     fields of [agent-tooling.md](agent-tooling.md#the-manifest-of-the-in-player-suites). `hazards` from what the pages say (`-uitest`:
     registry and screen; a run "without `-nographics`": `needs: gpu`; a window: `screen`).
  2. The test checks: the schema; unique names; `source` exists; each flag in `args` that is not Unity's own (`-batchmode`,
     `-nographics`, `-logFile`...) is a literal in the game code; the flags of the declared `mode` are present; every test file that
     installs itself (the census gives the list) is the `source` of an entry or appears under `unlisted` with a reason; `page` exists.
     Found when it was done: the census gives the *number* of such files (`selfInstalling`), not the list, so the test derives the list
     with the census's own `read_sources`, `is_test` and `is_editor` and checks that its count is the census's. The track mode and the
     menus came after the brief: `mode` also takes `track` (`-pista`, `-pistatest`) and `menu` (no `-autotest`), and the test works out the
     one game that a command line starts (F7) instead of looking only for the flags of the declared mode. A suite that a script starts (the
     co-op scenarios, the split screen) has a `runner` and no `args`: the test reads the script's scenarios and parameters. 87 suites are
     listed and none is `unlisted`; the 25 test files that do not install themselves are parts of a suite or are installed by production code.
  3. Leave `checks` empty unless the number was confirmed by a run in this task.
- **Done when**: the test passes; the pull request states how many suites are listed and how many are `unlisted` and why.
- **Verify**: `python -m unittest discover -s tools -p "test_player_tests.py"`.
- **Not in scope**: running a suite; editing [testing.md](testing.md) (T21).

### T06. In-player suites through the studio

- **Why**: F6, F10. Only `gunplaytest` returns a report today.
- **Needs**: **build**. No graphics card for the acceptance runs.
- **Depends on**: T03, T05.
- **Touches**: the profile and its test, a new small module shared with `tools/check_navigation.py` for reading a player's log, the
  engine if a suite cannot yet name its own default runtime (none was needed) and, which the brief did not foresee, the manifest, its
  schema and its test: an entry may name the label its test prints (the five `pistamenu*` suites share `pistamenutest`), the manifest
  holds the counts that the runs confirmed, and `needs: gpu` is off the three suites that pass without a card.
- **Steps**
  1. The profile turns each entry of the manifest into a `Suite` of kind `player`. The runner starts the runtime (default: the
     verification build) with the entry's `args` plus `-logFile`, `-report` and `-testout` pointing into the job folder, within `timeout`.
  2. Verdict as in [agent-tooling.md](agent-tooling.md#results-as-data): `result.json` when it exists, the result line otherwise
     (`verdictSource`), the count against `checks`, exceptions in the log except the renderer's noise without a graphics card.
  3. `needs: gpu` refuses while another `PryingEyes.exe` runs. `hazards: registry` saves the settings key before and restores it after,
     as `tools/coop/run_split.ps1` does. Entries with a `runner` call that script with the job folder as log folder.
- **Done when**
  - Unit tests, with a recording job and invented logs: the command issued; pass; a failed line; no line; fewer checks than declared;
    an exception in the log; a refused hazard.
  - On a verification build, `python tools/studio.py test survivaltest`, `test cattedraletest` and `test chartest` (the names the
    manifest gives them) pass, and each `report.json` holds the number of checks that the log shows.
  - Negative control: with `checks` raised above the real number in a local copy, the suite fails and says why.
- **Verify**: the unit tests; the three runs; `python tools/studio.py report latest`.
- **Not in scope**: porting tests to the harness (T07, T08); suites that need a graphics card; ports for the co-op (T11).

### T07. The in-player harness, proven on three suites

- **Why**: F5, F6. 51 copies of one harness (47 when the brief was written), and a list of 34 flags in `GameManager.cs` (it was 32) that
  half the changes to that file edit.
- **Needs**: **editor**, **build**.
- **Touches**: new `Scripts/Testing/TestRun.cs` and `Scripts/Testing/PlayerTest.cs`; `Scripts/TrapTest.cs`, `BarrelTest.cs`,
  `MineTest.cs`; `Scripts/GameManager.cs`; [testing.md](testing.md); [agent-tooling.md](agent-tooling.md); and, which the brief did not
  foresee, `tools/code_census.py` with its test: the census, and the guard that reads it, call every file with `Test` in its name a
  test, so `TestRun.cs` (which every build compiles and `GameManager.cs` names) would have been an unwrapped test named by production
  code, and the `Check` of the base class a private copy of it.
- **Steps**
  1. Before anything: run the three suites on the current build and keep their logs (the list of checks, in order, with results).
  2. Write `TestRun` (compiled always) and `PlayerTest` (compiled out of distribution builds), following
     [agent-tooling.md](agent-tooling.md#the-in-player-harness). `result.json` must escape text correctly. The interface as built
     is on that page; it differs from the sketch where the three suites asked for it.
  3. Port the three suites: delete their private `Check`, log hook, loop and exit; keep every check's name and order.
  4. In `GameManager.cs` replace the `||` chain with `TestRun.Dedicated` or a legacy array of the flags not yet ported, **one flag per
     line**. Remove the three ported flags from it (31 are left in `LegacyDedicatedFlags`).
  5. Write `-testout` and `result.json` into the conventions of [testing.md](testing.md); mark the harness as existing in
     [agent-tooling.md](agent-tooling.md).
- **Done when**
  - For each of the three suites the checks, their order, their results and the result line are the same as before.
  - Each `result.json` is valid JSON (parse it with Python) with as many checks as the log.
  - A suite that was not ported still passes (`-survivaltest`), and the smoke walk passes.
  - No file outside the tests names `PlayerTest`: a distribution build would still compile.
  - Census: tests with their own `Check` 51 to 48 (it was 47 to 44 when the brief was written); flags read by `GameManager.cs` down by
    three (53 to 50).
- **Verify**: the three suites and `-survivaltest` on the new build; the smoke walk; `python tools/code_census.py --section tests --section flags`.
- **Not in scope**: other suites; moving tests to another folder or assembly; changing what a check checks.

### T08. The other suites on the harness

- **Why**: as T07, for the remaining 44.
- **Needs**: **editor**, **build**; a free graphics card only for the suites that cannot run without one.
- **Depends on**: T07.
- **Steps**: pull requests of at most ten suites. The single-file tests first; then the ones in several files (`StealthTest`,
  `DesertTest`, `CoopTest`, `ArchitectureTest`, `CityTest`, `MagiaTest`, `SeatsTest`). For each: the before and after of T07. Test
  procedures that live in production classes (the gun, lean and wall-buy tests in `GameManager`) move to test files when their suite is
  ported. When the legacy array is empty, delete it. Replace `unity_logs` literals with `TestRun.OutDir`.
- **Done when**: per pull request, every ported suite that can run without a graphics card is identical before and after; the others are
  listed with the reason. At the end: no test defines `Check`; `GameManager.cs` names no test flag; census `tests` and `flags` updated.
- **Not in scope**: an assembly for the tests (T17).
- **Result** (25 November 2026, fourteen pull requests): every suite that can be run without touching the registry is on the harness ([agent-tooling.md](agent-tooling.md#the-in-player-harness)); the legacy array, its
  reader and the no-fire flags of the suites are gone; `GameManager.cs` reads nine flags, eight of them the automatic run's and the trailer recorder's, and `-guntest`, the documented alias of `-autotest`; the
  `unity_logs` literals of the suites on the harness are `TestRun.OutDir` (one is left, in a probe). Left out on purpose: `DisplayTest` and `PresentationTest`, which keep their own `Check` because
  proving them touches the registry (rule 6 of `AGENTS.md`; they wait for T14 or the maintainer's word), and the two probes. The census is in [modularization.md](modularization.md#how-progress-is-read).

### T09. Compile check, doctor and ship status as data

- **Why**: F10. The fastest check is written nowhere; the state of a checkout is found by hand; `ship.ps1` answers in text.
- **Needs**: **editor** for the acceptance run.
- **Depends on**: T03.
- **Touches**: the profile and its test; the engine (`doctor --json`); `tools/ship.ps1` and `tools/test_ship_sources.py`;
  [building-and-shipping.md](building-and-shipping.md), [testing.md](testing.md).
- **Steps**
  1. Suite `compile` ([agent-tooling.md](agent-tooling.md#test-compile-does-it-compile)): the editor in batch mode with no method, under the
     project lock; each `error CS` line becomes a finding with file and line. (The editor prints each error three times, and the suite
     keeps each once. The lock does not look at processes, so the suite itself refuses to start while an editor has the project open
     (`PROJECT_IN_USE`). Unity quits with 1 when the scripts do not compile; the findings come from the log whatever the code is. Its
     `seconds` in the registry, 15, is measured on the maintainer's PC: the gate's runner, which the other suites' `seconds` come from,
     has no editor.)
  2. `doctor --json` with the fields of [agent-tooling.md](agent-tooling.md#doctor-the-state-of-this-checkout). The text form stays.
     (The engine gets one hook, `doctor_facts()`, and prints what it returns; no job, no lock, no catalogs, so it works when one is broken.
     Added to the fields of the page: `ok` and `problems`, so that the exit code says something (1 while an editor has the project open,
     a tool is missing or the last gate is not green), `status`, `commit` and `url` in `gate`, and `pid`, `path` and `thisCheckout` for
     each player. `sourcesNewerThanBuild` is a count by modification time against the build's `Assembly-CSharp.dll`; since T15, against the newest of that and
     `PryingEyes.Rules.dll`.)
  3. `ship.ps1` writes `unity_logs/ship_last.json` when it finishes, whatever the outcome: the step reached, the commit, the build mode,
     what was pushed, the uncommitted sources, the warnings. The script stays ASCII. (It is written from `Finish`, so also after a
     refusal, an exception and a dry run. The hidden refresh writes `ship_refresh.json` instead, so that it does not replace the record of
     the ship that started it. It also says what became of the maintainer's game (`exe`) and keeps the lines of the text summary.)
- **Done when**
  - Unit tests cover the command and the parsing of an invented log, and the JSON of a `ship.ps1 -DryRun` on a throwaway repository.
  - On a worktree with a `Library`: `test compile` passes, and the time is written in the pull request; with a syntax error in a
    scratch file under `Assets/` (then removed) it exits 1 and names the file and the line.
- **Not in scope**: a build command in the studio (`ship.ps1 -NoPush -NoExe` builds).

---

## Phase 2: agents side by side

### T10. A worktree ready for Unity

- **Why**: F11. Rules 9 and 12 ask for worktrees; a new one cannot compile until every asset is imported.
- **Needs**: PowerShell, Python for the test; **editor** only to measure.
- **Touches**: new `tools/worktree.ps1`, `tools/test_worktree.py`, [building-and-shipping.md](building-and-shipping.md), rule 12 of `AGENTS.md`;
  and, because they described the script as a design, [agent-tooling.md](agent-tooling.md) and the list of unit tests in [testing.md](testing.md).
- **Steps**: write the script described in [agent-tooling.md](agent-tooling.md#toolsworktreeps1-a-place-to-work), ASCII, with `-DryRun`,
  and a test in the style of `tools/test_release.py` (throwaway repositories, an invented `Library`).
- **Done when**
  - The test covers: a new worktree and branch from `origin/preview`; the `Library` copied when the versions match; refusal when the
    donor has `Temp/UnityLockfile` or another version; `-Remove` refusing with uncommitted or unpushed work and working otherwise;
    no other worktree touched.
  - Measured once on the real project and written in [building-and-shipping.md](building-and-shipping.md): the time of the copy, its size,
    and the time of the first compile in the new worktree.
- **Not in scope**: sharing one `Library` between worktrees; cleaning old worktrees in bulk.

### T11. A port and a log folder per run

- **Why**: F11. `tools/coop/run_coop.ps1` uses UDP 7807 and `$env:TEMP\pryingeyes-coop` whatever the checkout.
- **Needs**: **build**. No graphics card.
- **Touches**: `tools/coop/run_coop.ps1`, `run_all.ps1`, `run_split.ps1`; the profile's runner if T06 is done; [testing.md](testing.md), section 5.
- **Steps**: `-Port N` passed to host and guests as `-coopport` (the game reads it in `Coop.BootFromArgs`), with `-Port 0` choosing a
  free port; the default log folder becomes `unity_logs/coop` (and `unity_logs/split`) of the checkout.
- **Done when**
  - While another process holds UDP 7807, scenario `a_griglia` passes on another port.
  - With two worktrees that each have a build: the same scenario started in both at once passes in both. With one build only, say so.
  - `run_all.ps1` still runs every scenario.
- **Not in scope**: the protocol; the scenarios themselves.

### T12. Automated runs leave tracked files alone

- **Why**: F11. Every `-autotest` run rewrites tracked files (`hotel_build/autotest.txt`, `auto_shot1.png`, `auto_shot2.png`, and
  `proctest.txt` when the bot runs without `-botlog`).
- **Needs**: **editor**, **build**.
- **Touches**: `Scripts/TestPaths.cs`, `Scripts/GameManager.cs`, `Scripts/Proc/FloorDirector.cs`; [testing.md](testing.md); and, which
  the brief did not foresee, `Scripts/Testing/TestRun.cs` (its `OutDir` worked out where `unity_logs/` is with an expression of its own, so
  it asks `TestPaths.LogFolder` now, which `Scratch` uses too: no second way to find the checkout), `tools/code_census.py` with its test
  (the census counts every `"unity_logs` literal outside the tests and exempted the one file that held it, `TestRun.cs`; the finder moved
  to `TestPaths.cs`, which is the file it exempts now, so the count stays 33) and [agent-tooling.md](agent-tooling.md).
- **Steps**: add `TestPaths.Scratch(name)` (the `unity_logs/player/` of the checkout, found as `TestPaths` finds `hotel_build`). Send
  there what the automatic run writes by itself: the report when `-report` is absent, the two timed screenshots, the bot's default log.
  Leave every output that a flag exists to ask for (`-camshots`, the photos of each suite). As built: `TestPaths.LogFolder(folder)` finds
  `unity_logs/<folder>/` and `Scratch(name)` is `LogFolder("player")` and the name; [testing.md](testing.md#conventions) says what goes where.
- **Done when**: after the smoke walk and after one suite, `git status --porcelain hotel_build` prints nothing; the files appear under
  `unity_logs/player/`; the pages say the new paths.
- **Not in scope**: the 485 tracked files themselves and the other outputs under `hotel_build/` (a decision for the maintainer,
  [modular-roadmap.md](modular-roadmap.md#decisions-that-are-the-maintainers)). Delete nothing.

### T13. What a build rewrites: measure, then decide

- **Why**: F11. After a build the tree is never clean; nobody has written down which files change and why.
- **Needs**: **editor**, one full build.
- **Touches**: [building-and-shipping.md](building-and-shipping.md) (the section
  [What a build rewrites](building-and-shipping.md#what-a-build-rewrites)) and, which the brief did not foresee, one row of the hazards table
  of [agent-tooling.md](agent-tooling.md), which said this task was still to do, and the row of T13 in [modular-roadmap.md](modular-roadmap.md).
  The last step did not apply: the `.gitattributes` is proposed in the page, not added to the repository.
- **Steps**
  1. On a clean worktree: a full build (`BuildPlayer.Torre`), then a scripts-only one. After each, `git status --porcelain`, and each
     changed tracked file put in one class: only line endings (`git diff --ignore-cr-at-eol` is empty); order or identifiers but the same
     content; real content.
  2. For the first class, try a `.gitattributes` on a throwaway branch and measure again. Propose it only if renormalizing changes no
     content. As built: `text eol=lf` changed two binary `.asset` blobs and `text=auto eol=lf` none, so the second is the one proposed.
  3. For the others, find the writer (`Editor/TorreBuilder.cs` creates and updates the kit materials and saves the scene) and say what
     would make it write only on a real change.
- **Done when**: the page has a section with the table (files per class, per kind of build), the cause of each class and a
  recommendation. No game or editor code is changed by this task.
- **Not in scope**: applying the recommendations beyond a proven-harmless `.gitattributes`.

### T14. Settings that automated runs cannot touch

- **Why**: F11. `PlayerPrefs` (81 calls in 13 files) is the registry key of the maintainer's game: the reason for rule 6.
- **Needs**: **editor**, **build**, and **the maintainer's go-ahead**, because proving it means running `-uitest` once, with their game closed.
- **Depends on**: T06 (the runner saves and restores the key, so the proof is safe).
- **Touches**: a new `Scripts/Prefs.cs`; the 13 files that call `PlayerPrefs`.
- **Steps**: `Prefs` mirrors the calls in use and keeps the values in memory when the run is automated (`VideoSettings.Automated` or a
  dedicated test), unless `-realprefs` is given. Replace the calls, nothing else.
- **Done when**
  - An export of the key before and after `-uitest` in batch mode is identical, byte for byte.
  - `PryingEyes.EditorTools.MenuReview.Run` passes with the same number of checks as before.
  - A normal start still keeps its settings: `MenuReview` exercises the real store in the editor.
  - The pull request states what still writes to the registry (Unity's own window values) and asks the maintainer about rule 6.
- **Not in scope**: changing rule 6; any setting's meaning.

---

## Phase 3: boundaries the compiler checks

### T15. The game-rules assembly

- **Why**: F8, B2. 34 engine-free files are kept pure by labs alone (the number of the audit: T20 took `NetworkFloorPlan.cs` out and
  brought the three admission files of `Scripts/Coop` in, 36 in the table of B2; the census says how many there are the day this starts).
- **Needs**: **editor**, one **full build**.
- **Depends on**: T01 (so that the desert files are under a lab in the gate).
- **Touches**: the files of the B2 table and their `.meta` (moved, not edited), a new `Rules/PryingEyes.Rules.asmdef`, the lab projects that name
  the old paths (`planlab`, `desertlab`, `saveslab`, `economylab`, `admissionlab`), the guard and its
  test, every page that names a moved path.
- **Steps**
  1. Before: build the labs and keep the output of `planlab grid 200`, of `desertlab` and of the four lab suites.
  2. Create `unity/PryingEyes/Assets/_PryingEyes/Rules/` with the subfolders and the files listed in
     [modularization.md](modularization.md#b2-game-rules-an-engine-free-assembly-inside-the-game). `git mv` each file with its `.meta`.
     Namespaces do not change. The assembly definition: `noEngineReferences: true`, references only the `Production.*` assemblies it needs.
  3. Update the lab projects, then the pages (search each file name under `docs/`).
  4. Guard: the package rules applied to `Rules/` with game names allowed (`GAME_RULES_ENGINE_REFERENCES`, `GAME_RULES_FORBIDDEN_NAME`,
     `GAME_RULES_NOT_COMPILED_OUTSIDE_UNITY`), with their fixture tests. `GAME_PURE_FILE_NAMES_ENGINE` of T04 can then go.
- **Done when**
  - Unity compiles with no error; a **full** build succeeds (a new assembly is not proven by a scripts-only build).
  - Every lab builds; the saved outputs are identical; the lab suites pass.
  - The smoke walk, `-survivaltest`, `-cattedraletest -nowaves` and `-desertotest` (all without graphics) pass as before; the co-op
    scenario `a_griglia` passes (both players build the same floor).
  - Negative control, local and reverted: `using UnityEngine;` in a moved file makes Unity's compile fail and the guard fail.
  - Census: `pure` shows no lab-compiled engine-free file left in `Assembly-CSharp`.
- **Verify**: the editor's compile; `tools/ship.ps1 -Mode full`; the suites above; `python tools/check_suite.py`; the census.
- **Not in scope**: renaming a namespace or a type; moving a file that no lab compiles (`Magia/Grimoire.cs`, `MagicState.cs`: they need a lab first).
- **Announce it**: every open branch that edits a moved file will conflict. Merge those first.
- **Outcome** (25 November 2026, #68): `Assets/_PryingEyes/Rules/` exists, with the assembly `PryingEyes.Rules` (`noEngineReferences: true`; it references `Production.Economy`
  and `Production.Saves`, the two packages its files use). The census of the day, not the card, set the size of the move: **56** files went, each with its `.meta` (same
  GUIDs; no line of code and no namespace changed), not 36. The card counted the 34 files of the audit as T20 left them; the other 20 are the rules of the track mode, which
  `tracklab` compiles and the audit, written before that lab, does not list ([B2](modularization.md#b2-game-rules-an-engine-free-assembly-inside-the-game) has the table).
  The move is a commit of its own, and the floor barrier left `SessionRoster.cs` in another one before it (see T20 above). No member had to change from `internal` to `public`
  (a search of the 56 files for every non-public declaration found one helper type of `Championship.cs` that nothing else uses and seven `internal` members used only inside
  their own file), so there is no visibility commit. The guard checks `Rules/` with the code it already had for packages, so the card's three codes are five
  (`GAME_RULES_ASMDEF_MISSING`, `GAME_RULES_ENGINE_REFERENCES`, `GAME_RULES_ASSEMBLY_REFERENCES`, `GAME_RULES_FORBIDDEN_NAME`, `GAME_RULES_NOT_COMPILED_OUTSIDE_UNITY`), and the rule
  of T04 that "can then go", `GAME_PURE_FILE_NAMES_ENGINE`, did not go: it became `GAME_LAB_FILE_OUTSIDE_RULES` (a game file that an engine-free lab compiles lives in `Rules/`),
  which is the census criterion of the card held by the gate.
  Proofs, measured on `8c20b8d` and on the head in the same checkout, one after the other, on a free machine and inside the lock of the sessions: the editor compiles with no error at
  each commit (at the move it builds `PryingEyes.Rules.dll` first); a full build succeeds (`PryingEyes.Rules.dll` 340,992 bytes, `Assembly-CSharp.dll` 4,028,416 to 3,705,344); the
  saved outputs of the eight labs are the same (timings and the random ids of `saveslab` apart; the PNG files by SHA-256) but for the 12 assertions of the barrier; the smoke walk walks the same two floors, and
  `-survivaltest` (91 checks), `-cattedraletest -nowaves` (72) and `-desertotest` (150) pass with the same checks in the same order; `a_griglia` passes before and after (63/63 and 49/49);
  with `using UnityEngine;` added to `Rules/Desert/DesertClock.cs` the editor's compile fails (`error CS0246`) and so does the guard (`GAME_RULES_FORBIDDEN_NAME`), and after
  `git checkout` of the file the tree is as it was; census `pure`: 56 to 0. The table of every measure is in the description of the pull request.
  Found on the way: `doctor` dated a build by `Assembly-CSharp.dll` alone, which a change to a rule leaves as it was (a scripts-only build that had the change was reported with
  `sourcesNewerThanBuild: 2`). It now takes the newest of the game's two assemblies, with a test that fails on the old rule, and `tools/citylab.py` records the hash of the second
  assembly beside the first.

### T16. Spike: can the game be a named assembly?

- **Why**: F1, B3, B4. The tests cannot have their own assembly while the game is `Assembly-CSharp`; nothing in the repository proves
  that the game compiles and plays as a named one.
- **Needs**: **editor**, **full build**. A throwaway branch.
- **Depends on**: T15.
- **Steps**
  1. On a branch that is not meant to merge: one assembly definition for the game at `Assets/_PryingEyes/` (it must cover `Scripts/`;
     the `Multiplayer/` folder that once named it back is gone, T20), with the references the code needs (the render pipeline, the UI,
     FishNet, `Production.*`, `PryingEyes.Rules`); one for the editor folders, editor only.
  2. Find out and write down: does it compile; does FishNet's code generation still run on it (the co-op scenario `a_griglia` passes);
     do the `-executeMethod` entry points still resolve; does a full build work and the smoke walk pass; do scenes and prefabs keep
     their scripts.
  3. Measure the time of a compile after a one-line change in one game file, three times, before and after.
- **Done when**: a new page under `docs/engineering/`, listed in the index, records what was done, the exact definitions, what broke,
  the measures and a recommendation: go, or no, with the reason. The branch is pushed for reference. Only the page is merged.
- **Not in scope**: merging the assemblies; moving tests.

### T17. The tests in their own assembly

- **Why**: F6, B4. 14 test files ship in releases; 45 others stay out one `#if` at a time.
- **Needs**: **editor**, **full build**, a distribution build.
- **Depends on**: T08, and T16 recommending go with the maintainer's agreement.
- **Steps**: apply the definitions of T16. Move the test files and `PlayerTest` to a `Tests/` folder with a definition compiled only
  when `PE_DISTRIBUTION_BUILD` is not defined. Remove the per-file wrappers. Remove the two production references to tests
  (`Coop/CoopGame.cs`, `Stealth/StealthRig.cs`): the tests install themselves. Empty the baseline of T04 and replace its rules with one:
  no file outside `Tests/` is named `*Test*`.
- **Done when**
  - A distribution build has no test assembly and no test type in any managed assembly (search the built files for three test class names).
  - The verification build has it, and the suites that T06 runs pass.
  - If the definition's constraint does not see the define that `BuildPlayer` passes: stop and record it; the per-file wrappers stay
    and the guard's rule keeps them complete.
  - Census: production files naming a test 0; test files not excluded 0.

---

## Phase 4: on a trigger

The triggers are in [modular-roadmap.md](modular-roadmap.md#triggers-of-phase-4). The briefs are shorter on purpose: the event that starts
each one will say more than can be written now. The last two, T22 and T23, are longer: the maintainer decided them on 25 November 2026,
and what was checked that day is worth keeping.

### T18. An engine-free audio core and its lab

- **Why**: F8. The 18 `SoundGen` files use the engine only for `Mathf` (405 times) and two other calls; audio is checked only inside
  the editor or the player ([../game/menu-music.md](../game/menu-music.md)).
- **Steps**: replace the engine calls with engine-free ones in the generators; move them under `Rules/Audio`; a lab `tools/audiolab` that
  computes each sound and checks what `MenuMusicReview` checks without Unity (length, level, no clipping, the loop seam).
- **Done when**: every generated sound is the same before and after, sample by sample, or within a tolerance stated and justified
  (single-precision functions can differ in the last bit); the lab is a suite in the gate; the in-player audio checks pass unchanged.

### T19. One reader of the command line

- **Why**: F7. 39 private helpers, 177 reads.
- **Steps**: a `CommandLine` class in the game (`Has`, `Int`, `Float`, `Str`) and an `EditorArgs` for the editor tools; replace the
  private helpers, folder by folder, with no change to which flags exist or what they mean.
- **Done when**: census `flags`: two helpers; the suites of each touched folder pass as before.

### T20. Decide the fate of the multiplayer draft

- **Why**: F12. 21 files and a check project that the game does not use, next to the stack it does use.
- **Steps**: write for the maintainer, in the pull request, what the draft contains, what uses it (the probe scene and prefabs under
  `Multiplayer/GeneratedProbe 1/`, `tools/multiplayer-check`, one type used by `Scripts/Coop`), and the three options: keep as the base
  of a future rewrite, freeze with a README that says so, retire. Carry out the chosen one. Nothing is removed without the choice, and
  everything removed stays in git history.
- **Done when**: the folder has a README that states its status, or is gone by decision, and
  [../multiplayer/README.md](../multiplayer/README.md) says the same.
- **Outcome** (25 November 2026, #66): the maintainer chose to retire it. The brief of the options was #48, closed in favour of #66.
  `Assets/_PryingEyes/Multiplayer/` no longer exists. The four files the game used, `FishNetSeatAuthenticator.cs`, `AdmissionGate.cs`,
  `SessionAdmission.cs` and `SessionRoster.cs`, moved to `Scripts/Coop/` with their `.meta` files (same GUIDs; no line of code and no namespace
  changed). The other 17 C# files, the probe scene and its prefabs, `tools/multiplayer-check` and the lines of `.gitignore` for it were removed;
  the draft as it was imported stays in git, at `36e0a3a`. Its live checks (the admission gate, the token, the roster and `PlayerPerks`,
  with the same sentences) are the lab `tools/admissionlab`, a suite of the gate (`python tools/studio.py test admission`); the checks of the old
  project that proved code that is gone (the floor plan of the draft, its seat command, its screen layout, its workspace guard) were not
  carried over. `Assets/DefaultPrefabObjects.asset`, which listed the two probe prefabs, is now empty.
  Found on the way, and done afterwards by [T15](#t15-the-game-rules-assembly) in a commit of its own: `SessionRoster.cs` held the floor barrier of the draft
  (`Epoch`, `FloorDigest`, `AllReady`, `WaitingPeers`, `BeginFloor`, `Acknowledge` and the set of peers that had acknowledged) that nothing in the
  game called, and its checks were in the lab because the file had moved as it was. Removing the barrier is a change of code, not a move, so it was
  not part of #66. The code is gone with the 12 assertions of the lab that proved it (the roster block went from 19 to 7, the lab from 83 to 71;
  the other 71 are the same sentences, word for word).

### T21. The tables of testing.md from the manifest

- **Why**: F5, F13. The catalogue of checks changed in 32 commits and is merged by hand.
- **Depends on**: T05, T06.
- **Steps**: a command that prints, from the manifest, the tables of sections 3 to 5 of [testing.md](testing.md) (name, command line, what
  it checks in one line, output, page). The long descriptions move to the page of each feature, where most already are. The guard
  fails when the tables in the page are not the ones the command prints.
- **Done when**: no sentence of the old tables is lost (each is in the manifest or in a linked page: state where); adding a suite means
  one manifest entry and one generated table.

### T22. The track rules as a package

- **Why**: the maintainer decided on 25 November 2026 that the bikes become a package that another game can take, before a second game
  asks for it ([the two exceptions](package-extraction.md#the-two-exceptions) say what that accepts). The 20 files of `Rules/Track` are
  already engine-free and checked by `tracklab` (rung 1 of the ladder); rung 2 adds neutral names, a cut between mechanism and content,
  a project outside the game that consumes it, and the four proofs of the guard.
- **Needs**: **editor**, one **full build** (a new assembly is not proven by a scripts-only build), the .NET 8 SDK.
- **Depends on**: T08 (its last pull request is the last to edit the tests of the track mode: `Scripts/Pista/` should be quiet) and T15 (done).
- **Touches**: the files of `Rules/Track/` and their `.meta`; a new `unity/PryingEyes/Packages/com.pryingeyes.racing/` (the name is a
  proposal: the guard's list of forbidden names is its test); `tools/tracklab/TrackLab.csproj` (the paths); `unity/LayoutDemo` (a
  `RacingDemo.cs` and a line of `Packages/manifest.json`); `PryingEyes.Rules.asmdef` (a reference to the package); the `using` lines of the
  gameplay files that name a moved type (`Scripts/Pista/`, and in `Scripts/Deserto/` `DesertBikeDef.cs`, `DesertTest.Garage.cs` and `SplitDesertTest.cs`);
  [package-extraction.md](package-extraction.md), [../game/track-mode.md](../game/track-mode.md), [modularization.md](modularization.md)
  (B1 and the census), [suite-architecture.md](suite-architecture.md).
- **What goes and what stays** (from a search of the type names of each file in the others, at commit `839571c`; the compiler is the proof, in step 1):
  - the mechanism, which uses only itself: `PistaMath` (as `RacingMath`), `BikeModel`, the shape of a bike in `BikeSpec.cs`, `Tuning`,
    `Circuit`, `CircuitGen`, `RacingLine`, `DrivingLine`, `BrakeCue`, `BikeDriver`, `RaceRules`, `PoseTape`, `RaceReplay`, `KeyShaper`;
  - the game's content, which uses the mechanism and which nothing of the mechanism uses: the nine bikes (the class `Bikes`, in the same
    file as the shape of a bike, so the file is split), `Catalogue`, `Licences`, `Championship`, `Career`. They stay in `Rules/Track`
    and consume the package;
  - to decide in step 1, with the reason in the pull request: `BikeSheet` (the bars of the garage, computed from the model; the desert garage
    shows it too, which argues for the package) and `EngineSynth` (the sound of the engines).
- **Steps**
  1. Prove the cut. A throwaway project, as `tracklab` is, compiles the mechanism files alone; the compiler lists what it still needs from a
     content file. What it needs is moved, or passed in as a parameter or an interface. Nothing else changes: the same expressions, the same numbers.
  2. Names. The namespace becomes `Production.Racing`, `PistaMath` becomes `RacingMath`, and no game, engine or netcode word stays in the code.
     The texts that the mechanism writes for people (if `CircuitGen` names circuits, for one) come in as a parameter or stay in the game.
  3. Move with `git mv` and the `.meta`. The assembly definition has `noEngineReferences: true` and no references; add `package.json` and a
     `README.md` that says what the package is for, how to consume it, and that one game has used it so far. `tracklab` compiles the package
     files from their new path.
  4. The gameplay files get the `using` of the new namespace and the new name of what was renamed, and `PryingEyes.Rules` references the
     package. No behaviour changes.
  5. `RacingDemo.cs` in `unity/LayoutDemo` rides a bike on a generated circuit with the package alone and prints the lap time.
  6. Correct the pages (rule 13): the inventory row of [package-extraction.md](package-extraction.md#inventory-of-candidates), which says
     honestly what in the interface still looks like this game; `modularization.md`; the census tables.
- **Done when**
  - Unity compiles with no error and a **full** build succeeds; the guard passes with the new package and its four proofs.
  - `tracklab` passes with the same checks, and what it saves (the race simulations, the pictures by SHA-256) is the same before and after:
    record both before step 1.
  - In the player, with the method of T08, the suites of the track and of the desert bikes pass with the same checks in the same order as
    before: `pistatest`, `pistacuponly`, the five `pistamenu*` and the bike phases of `desertotest`.
  - `RacingDemo` runs in `unity/LayoutDemo`, which the guard checks consumes the package.
  - Census: no engine-free file that a lab compiles is left in `Assembly-CSharp`, and `Rules/Track` holds only the game's content.
- **Not in scope**: the Unity side of the track mode (`Scripts/Pista/`: meshes, views, menus, garage, the replay on screen): a package with
  the engine inside needs a second kind of rule in the guard, and a second game to shape it. The desert bike (`DesertBike*.cs`): it rides on
  its own arcade code and shares only the data of the nine bikes and their sheet; making it ride on `BikeModel` is a design choice of the
  maintainer, not a refactor. Any rule, number or word that a player sees.
- **Announce it**: every open branch that edits a file of `Rules/Track/` or of `Scripts/Pista/` conflicts. Merge those first.
- **Outcome** (26 November 2026, #78): `Packages/com.pryingeyes.racing/` exists, with the assembly `Production.Racing` (`noEngineReferences: true`, no references); `PryingEyes.Rules`
  references it and `unity/LayoutDemo` consumes it (`RacingDemo` rides a bike on a generated circuit with the package alone). **14** files (3,850 lines) are the package and **11** (1,379 lines) stay in
  `Rules/Track` as the game's content: the card's 20 files (5,158 lines) became 25 (5,229), because five are new (`BikeFigures` in the package; `Bikes`, `BrakeCueWords`, `Circuits` and `RaceRoster` in the game).
  One commit per step, the move itself a commit of renames only. **The cut was one name.** With `Bikes` taken out of `BikeSpec.cs`, the card's mechanism files compiled alone ask the content for nothing
  else (10 errors, all `Bikes`: `BikeSheet` 5, `RaceRules` 4, `Tuning` 1). So `Race` became spec-first (`AddHuman`, `AddAi` and `AddRemote` take the `BikeSpec`; the game's calls by roster number are extension
  methods in `RaceRoster`), and the two texts the mechanism wrote for people left by parameter: `CircuitGen` takes the function that names a circuit (it draws from the state of the generator after the circuit, so
  seed, shape and name are as before) and the Italian words of the braking sign are `BrakeCueWords`. No member went from `internal` to `public`. Nine of the 13 files that existed changed no line of code;
  the other four changed 221, 158 of them the class `Bikes` leaving `BikeSpec.cs`; the 27 game files that changed are `using Production.Racing;` and renames, nothing else (checked file by file).
  The three decisions the card left open, with their reasons in the pull request: `BikeSheet` is split (what rides the model, `BikeFigures.Ride`, is the package's; the bars, the words and the blurbs of the nine bikes
  stay), `EngineSynth` stays (the shape of the four voices is the sound of this game; the package keeps the label `EngineKind`), and `Tuning`, which the card listed as mechanism, stays too (it is the workshop of
  the nine bikes, by roster number, and the one thing the compiler asked of it). **Where the card and the code disagree:** the card says the desert garage shows the sheet, which argued for the package;
  `Scripts/Deserto` names `BikeSheet` once, in a comment (the desert takes `Bikes`, `BikeSpec` and `EngineKind`), and of the three desert files the card names only `DesertBikeDef.cs` needed a line. The guard needed
  no change: it finds a package by its `package.json`, so its four proofs applied to the new package as they were.
  Proofs, measured on `7a07cc9` and on the head: the track lab prints the same 207 checks in the same order and saves the same 10 pictures and 4 sounds (same SHA-256) after every code commit; 2,000 circuits (name,
  attempts, length, a hash of every array) and 120 races (the poses every half second, the finishing orders and times, the best laps, the falls) were run through the code of `7a07cc9` and through the package: 2,120
  lines of output, identical byte for byte; no string literal changed in any of the 66 files of the names commit; the guard passes with no finding on every commit, the rename-only one included, and the 296 Python tests pass at the cut, the names, the package and the head. One machine
  window of 47 minutes of the lock of the sessions, in three runs of the same script (the first two stopped at faults of the script, not of the branch): the editor compiles (66 s, no error); a full build succeeds (`Production.Racing.dll` 76,288 bytes, `PryingEyes.Rules.dll` 340,992 to 272,896,
  `Assembly-CSharp.dll` 3,719,680 both) and the smoke walk walks the same two floors; the eight suites (`pistatest` 121, `pistacuponly` 23, the five `pistamenu*` 46, 30, 39, 35 and 47, `desertotest` 150) pass on the
  build of `7a07cc9` and on this one with the same checks, in the same order, with the same verdicts (what differs are the circuits that the menus and the second race draw at random, in both builds);
  with `using UnityEngine;` added to `Runtime/KeyShaper.cs` the editor's compile fails (`error CS0246`) and so does the guard (`PACKAGE_FORBIDDEN_NAME`), and after the file is written back byte for byte no file of
  the branch is modified; `RacingDemo.Verify` runs in `unity/LayoutDemo` (Unity exits 0 and prints `RACING_DEMO_OK Circuit 7: 2817 m, best lap 79.46 s, 0 falls`, where the same run on .NET prints 79.44 s: single-precision arithmetic is not bit for bit the same on the two runtimes); census: `PryingEyes.Rules` 56 files to 47, `Rules/Track` 20 to 11, a package of 14 files, and no engine-free file that a lab compiles in `Assembly-CSharp`.
  What was not proved is in the description of the pull request (nobody rode a bike on this build; the cooperative race over the network was not run; no distribution build).
  Found on the way: the editor rewrites `ProjectSettings/ProjectAuditorSettings.asset` (line endings only) every time it starts, so `git status` is never empty after a compile, and a "the tree is clean" check
  has to name that file; `studio.py test compile` prints only the path of its report when the compile fails, and the compiler's words (`error CS0246 ...`) are in `compile-unity.log` of the job and in the findings of
  its `report.json`. **For T23:** the order of the steps, the proofs and the window script (a list of suites, the lock, the stages; it is in the notes of the sessions, not in the repository) carry over as they are, and so does the way of proving
  that nothing changed (the code of the old commit and the new code, run side by side over thousands of inputs, and the diff of the code with the comments and the renames left out). What does not carry over is
  the premise: here two engine-free halves lived in one folder and the compiler found the one name that joined them, whereas `Weapon.cs` is one class that talks to the engine, so each step of T23 is an extraction
  (the colours become a type of the package, the arithmetic leaves a class that calls `Mathf`), where the call sites change and the lab's table of inputs has to carry the proof. Expect more than one name to settle.

### T23. The weapon rules in the weapons package

- **Why**: the maintainer decided on 25 November 2026 that the gunplay becomes reusable ([the two exceptions](package-extraction.md#the-two-exceptions)).
  `Production.Weapons` holds one file today (`WeaponState`: the reload and the cadence, with `weaponslab`). The rest is `Weapon.cs`, one class of about
  1,500 lines that talks to the camera, the hands, the effects, the enemies, the shared screen and the player, and the files around it (`Armi/`,
  `Braccia/`), which use Unity. This task moves what is only numbers, and nothing else.
- **Needs**: **editor**, **build**; a free graphics card for the suites that cannot run without one; the .NET 8 SDK.
- **Depends on**: T08 (the gun suites on the harness) and T22: it starts when T22 has merged and its page says that the method worked. If T22
  stopped, this stops with it and the maintainer decides.
- **Touches**: `Scripts/Armi/WeaponDefs.cs`, `Scripts/Armi/WeaponCatalog.cs`, `Scripts/Weapon.cs` and its parts; `Packages/com.pryingeyes.weapons`;
  `tools/weaponslab`; `unity/LayoutDemo/Assets/WeaponDemo.cs`; the pages. `Resources/Production/weapons.json` does not change.
- **Steps**: one pull request each, in this order. None changes a number that a player feels.
  1. The data. `WeaponDef` (about sixty fields: damage and its fall with range, pellets, cone, pierce, rate, magazine, reload, bloom, recoil,
     handling) and the validation of the catalogue move to the package. Today they use `UnityEngine.Color` for the tracer, the flash, the reticle and
     the grade colours, and `Mathf.Clamp` twice, and the reader uses Newtonsoft.Json, which a package may not reference. The colours become a small
     engine-free type that the game converts at its edge. The reader either stays in the game and hands the package a plain list, or the package carries
     its own: decide with the guard and write why. The file, the append-only indices and `WeaponDefs.ContentHash` are the same before and after.
  2. The arithmetic. In `Weapon.cs` find what takes numbers and returns numbers: the damage at a distance and through enemies, the cone of the
     pellets, the bloom and its recovery, the recoil of the view and its recovery, the spread with movement and aim. Move each as a function or a
     small state machine like `WeaponState`, with the same expressions. `weaponslab` keeps a copy of the old code, as `LegacyReload.cs` does for the
     reload, and compares the two over a table of inputs. `Weapon.cs` calls the package; its behaviour is unchanged.
  3. The demo. `WeaponDemo.cs` in `unity/LayoutDemo` fires a weapon of the catalogue at a target with the package alone and prints what each shot did.
- **Done when**
  - Every moved computation equals the old one in the lab over a table of inputs (state its size), bit for bit or within a stated tolerance.
  - The guard passes; `weaponslab` passes; Unity compiles; a scripts-only build and the smoke walk pass.
  - In the player, with the method of T08, the gun suites (`guntest`, `gunplaytest`, `opticstest`, `leantest`, `meleetest`; `python tools/studio.py suites`
    lists them with what each needs) pass with the same checks in the same order as before, and what they measure (spread, recoil, damage) is the same.
  - The hash of the catalogue is the same.
- **Not in scope**, written down so that nobody does it by accident: the Unity layer of the gun. The viewmodel and its sway, the hands and the arms
  (`Braccia/`), the impact effects (`Fx.cs`, `Surfaces.cs`), the sounds and the magic seals need sockets (who takes the damage, where the camera is, who
  plays the effects, where the command comes from), and sockets are drawn well only with a second game that has guns. When that game exists it is a
  new task with its own page. Also not here: a new weapon, a rebalanced number, a change to the JSON schema.
- **Outcome** (27 November 2026, #79, #80 and #81): `Production.Weapons` went from 1 file (137 lines) to 9 (563) in one pull request for each step, and `unity/LayoutDemo` fires weapons with it alone.
  **The data** (#79): `WeaponDef`, `FireMode` and `ReloadStyle` (the 63 columns of `weapons.json`, in the order of the file), `Rgba` and `WeaponCatalogRules`: 3 files, 176 lines. **The arithmetic** (#80): `WeaponBallistics` and `Pierce`, `AimStance`,
  `WeaponSpread` and `WeaponBloom`, `WeaponRecoil`, `ViewRecoil`, and `WeaponMath`, the 14 helpers that mean what `Mathf` means (internal): 5 files, 250 lines. **The demo** (#81): `WeaponRange` and `WeaponDemo.Verify` fire four weapons of a catalogue
  that belongs to no game (a rifle, a shotgun, a bolt action that goes through targets, a machine gun whose view climbs to its ceiling) at rows of targets with the package alone, and print what every shot did; it moves nothing. `Assembly-CSharp` went from 443 files (111,450 lines) to 444 (111,345),
  and no member went from `internal` to `public`.
  **What was settled.** The reader of the file stayed in the game: it is Newtonsoft.Json from its first line and a package may not reference a library, so the game checks the shape of the file and hands the package the records and their indices in file order
  (a contract resolver in the game keeps `[JsonObject(MemberSerialization.Fields)]`, which the package cannot name). The colours became `Rgba`, turned into a `Color` at the edge (22 call sites). Every random draw, the direction of a bullet (a `Vector3`), the ray casts
  and the factors that perks, upgrades, magic and the vehicle put on a result stay in the game and are passed in, so the rules draw nothing; the six numbers of the view and its two copies of the recovery (on foot, on a bike) became one `ViewRecoil`. The Italian words
  of `GradeName` and `ModeLabel` and the tuning (a run counts in full at 5.6 m/s, a crouch narrows the cone by 28 %, the air widens it by 160 %, the view climbs at most 20 degrees) stay as they were, and the README says that they are one game's. `OwnerPerks.HeadMult`
  is now read at every hit, where the old line read it only for a head: the same value. **Where the card and the code disagree:** the card says the demo fires "a weapon of the catalogue"; the 30 weapons are the game's content and their reader stayed in the game,
  so the demo has a catalogue of four of its own, which the rules of the package accept (and refuse, with `WEAPON_STATS_INVALID`, a shotgun that does no damage).
  **Proofs.** `weaponslab` passes 1,480 checks where it passed 1,454: the old ones word for word and in the same order, and 26 new, 14 for the data (over 4,563 inputs) and 12 for the gunplay (over 6,502,871 inputs, the same float to the last bit as `LegacyGunplay.cs`,
  a frozen copy of the old expressions); it was tried against 19 deliberate mistakes in the package (a constant, the order of a product, `>` for `>=`, a flipped sign) and caught each one. Each of the 42 statements of the old expressions is found word for word in the three
  game files at the base, and each function of the package is the text of the statements it replaces, after the renames (a throwaway script: its output is not in the repository). A probe in the editor compares each of the 14 helpers with its `Mathf` counterpart over
  6,163,717 inputs: 0 differ. Three machine windows of the lock (the base, step 1 and step 2: 27, 29 and 29 minutes) ran the stages of one script: the editor compiles; a full build succeeds (the player grew 4,608 bytes at step 1 and 3,072 at step 2; `Production.Weapons.dll`
  7,680, 12,288, 15,872) and the smoke walk walks the same two floors (47 s, 45 s, 45 s); the content hash of the catalogue (`975b657ac2f5`, 30 weapons), the catalogue as JSON (68,631 characters) and the hash of each weapon are the same on every head; the incremental compile
  after a changed line rose by about 2 s at step 1 (the cause was not looked for) and did not at step 2; with `using UnityEngine;` added to a file of the package the editor's compile fails (`error CS0246`) and so does the guard (`PACKAGE_FORBIDDEN_NAME`);
  `guntest` 31/31, `leantest` 6/6, `meleetest` 110/120 and `magtest` 9/9 on every head, as on the base. `gunplaytest` and `opticstest` fail now and then on preview itself (two damage checks together; up to three of the 37 reticle checks), so each was run 8 and 3 times
  in a window and read as a rate: the checks, their names and their order were the same in every run and no check failed that did not also fail without these steps. The rates are as alike as the noise allows: the two damage checks of `gunplaytest` failed in 7 of 20 runs
  before step 2 and in 7 of 24 after it (5 of 8 in the window of step 2, 2 of 16 on the build that preview shipped), and why they fail now and then on preview itself is still not explained; one reticle check of `opticstest` (`lmg_campana249` with sway) failed in 5 of 9 runs before step 2
  and in 6 of 9 after it. One check of `magtest` carries a measured number in its name (the height at which the dropped magazine rests: 0.01 m in three runs and 0.06 m in one, on the same build), so the names of that suite cannot be compared word for word.
  **The demo.** `WeaponRange` is plain C# with no engine type, so the same file runs in `unity/LayoutDemo` and in a throwaway .NET project (the nine files of the package as an assembly of their own and a stub of `UnityEngine`): Unity and .NET print the same text, line by line
  (138 lines), and two runs print the same again. `WeaponDemo.Verify` checks what the package promises of a shot: a hit inside the near range is the damage of the weapon, at the far range the far multiplier of it, on a head the head multiplier; a shotgun fires its pellets; the bolt action goes through
  three targets and each takes the pierce multiplier less; the bloom reaches its ceiling and goes away; the view climbs to its ceiling of 20 degrees, does not pass it and comes back; rounds are conserved; the same seed gives the same shots. It was tried against 11 deliberate mistakes in a copy of the package
  (the far multiplier, the head multiplier, one target too many, the pierce multiplier, the ceiling of the bloom and its going away, the ceiling of the view and its return, a round that is never consumed, a catalogue that accepts a weapon that does no damage) and caught each one; it does not check the values of the cone,
  the spread or the kick, which the lab proves. A window of the lock (80 seconds, six of them the editor's) opened `unity/LayoutDemo` in batch mode with the package: Unity exits 0 with no compiler error and `-executeMethod WeaponDemo.Verify` prints `WEAPON_DEMO_OK` and the verdict line (the rifle's 40 shots:
  32 hits of 30.00 and one reload; the machine gun's view at 20.0 degrees). No new build of the game was made for this step: the game's tree does not change, and the full build and the smoke walk of preview at the commit of step 2 stand for it.
  **Not proved:** nobody fired these weapons by hand on a build (the feel of the recoil and the spread is argued from numbers); speed was not measured (no suite measures a frame); `-uitest`, `-displaytest` and `check_presentation.py` were not run (rule 6),
  nor any cooperative run (the content hash that the admission compares is the same).
  **Found on the way:** `ProductionCatalog.VerifyMigration` and its fixture `tools/fixtures/weapon-prefix-v1.json` are stale on preview itself (23 of the 29 weapons of the fixture keep their serialisation, then `WEAPON_MIGRATION_CHANGED: index 0`) and were left alone;
  the gun suites write four tracked files under `hotel_build/` (the reports of `guntest` and `leantest`, two screenshots), so a window puts them back by exact path after keeping a copy, as it does with the project settings that Unity rewrites at each start; a squash merge
  makes the branch of the next step conflict on lines that both sides rewrote, so it was brought onto preview with `git merge -s ours` (the tree is the same), and the list of commits of a step is longer than the step, whose diff is only the step; `opticstest` writes
  its 42 photos in `Validation/Optics` next to the player's executable and not in the folder of the run (`TestRun.OutDir`), so the shipping script lists it as a folder it does not copy (a point for the census of T12, not touched here).
  **Left out, as the card said:** the Unity layer of the gun (`Weapon.cs` is still 1,625 lines, `Braccia/` has 2,418 and `Fx.cs` 1,171, and there are the viewmodel, the sounds and the magic seals: they need sockets, and sockets are drawn well only with a second game that has guns),
  and a real second game (the demo shows that the package can be taken without Prying Eyes, not that its interface suits another game; the README says that one game has used it and a revision is expected).
  **The method.** What T22 handed over carried over: the order of the steps; the proof by a frozen copy of the old code run side by side with the new over millions of inputs, and by the diff of the text; the window of stages under one lock, with a negative control and a probe
  in the editor; a draft pull request that says what is not proved. What did not carry over was the premise: here each step was an extraction at the call sites of `Weapon.cs`, not the move of two halves that were already apart, so the table of inputs of the lab had to carry the proof,
  the lab needed a test of its own (the mutations) and several names settled (the colours, the clamp, the reader, every draw, the view). What worked less well: the suites that guard the gun are noisy on preview itself, so "the same checks in the same order" could be shown and
  "the same result" could only be "not shown to differ"; step 2 had no "before" of its own (it used the "after" of step 1, measured 41 minutes earlier), so when a suite flakes put both sides in one window, with enough runs; and the window script lives in the notes of the sessions,
  not in the repository, so its faults (a launch that opened a console on the maintainer's screen, a suite that left four tracked files modified) were found by using it. All were fixed in the script and none reached a branch.
