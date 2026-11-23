# Modular architecture: task briefs

Twenty-one tasks, each written so that an agent can carry it out alone: what to do, why, what must be true at the end and how to
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
  1. Add to `planlab` the command `fingerprint`: for seeds 1000, 2961, 3046, 777, 114 and 4242, floors 1, 3, 5 and 8, in the lab's
     default layout and with `waves` (the layout the game uses), compute the SHA-256 of the text that `dump` writes; print one line per plan and write them with
     `--report FILE`.
  2. `fingerprint --update tools/fixtures/plan-fingerprint.json` rewrites the fixture, which also records the revision read from
     `CoopWire.cs`. It refuses (`PLAN_CHANGED_WITHOUT_REVISION`) when a hash changed and the revision did not.
  3. Add the suite `plan`: one finding per plan (`PLAN_FINGERPRINT`: observed and expected hash) and one that the fixture's revision is
     the one in `CoopWire.cs` (`PLAN_REVISION`).
  4. Add it to the gate, and say in the two pages what to do when it fails.
- **Done when**
  - The command printed twice is identical, and the gate's result equals the committed fixture.
  - Negative control, all local and reverted: a constant changed in `Scripts/Proc/PlanGrid.cs` makes `test plan` fail and name the
    plans; `--update` refuses; after raising the revision it accepts.
  - The suite takes under 30 seconds on the gate's runner.
- **Verify**: `dotnet run --project tools/planlab -c Release -- fingerprint`; `python tools/studio.py test plan`; the gate.
- **Not in scope**: asserting what a plan must look like (`-gridtest` does); the furniture and the building of a floor, which need the
  engine and stay covered by the co-op scenarios; the desert generators (their lab checks repeatability).

### T03. One registry of suites, and the gate asks it

- **Why**: F9, F10. A lab has to be added in ten places; `desertlab` was left out of most.
- **Needs**: Python, .NET. No Unity.
- **Touches**: the engine and `tools/test_studio_engine.py`, `tools/fixtures/toy_project.py`, the profile, `gate.yml`, `.gitignore`,
  rule 8 of `AGENTS.md`, the pages that list the labs.
- **Steps**
  1. Engine: `Suite` gains `kind`, `needs`, `hazards`, `seconds`, `about` (defaults empty). New command `suites [--json]`
     ([agent-tooling.md](agent-tooling.md#suites-what-can-be-run)). `test` gains `--allow HAZARD` (repeatable) and fails a suite whose
     hazards are not all allowed with `SUITE_HAZARD_NOT_ALLOWED`. The engine names no game, tool or content: the guard checks it.
  2. Profile: fill the fields for the existing suites (the labs need nothing; `gunplay` needs `runtime`).
  3. `gate.yml`: compile every `tools/*lab` project and run every suite whose `needs` is empty by reading `suites --json`; check each
     exit code; build the summary from the same list. No lab is named in the workflow.
  4. `.gitignore`: one pattern for the lab projects and their `bin` and `obj` folders instead of a block per lab.
  5. Rule 8 and the pages point to `python tools/studio.py suites` instead of listing names.
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
     that reaches no Unity assembly. The projects of `tools/multiplayer-check` that do (`unity-compile`, `fishnet-compile`) compile engine
     code against the editor's own libraries, and 14 of the files they compile name `UnityEngine` or `UnityEditor` on purpose; counted as labs they would
     be 14 violations that are not one, so the rule leaves them out. A fifth code, `GAME_BASELINE_INVALID`, reports a baseline that is
     not valid or has an entry without its reason.
  2. The baseline is the fixture: today's two files and 14 files, each with a reason (none for the engine rule). An entry can be removed, never added without a
     reason a reviewer accepts.
  3. In the gate, after the guard, print `python tools/code_census.py --section sizes --section cycles --section tests --section flags`
     into the run summary and upload its JSON with the lab reports. It reports; it does not fail. The census reads the `.asmdef` files of
     the game, which the sparse checkout of the gate did not have (without `Multiplayer/Core`'s, five of its files count as `Assembly-CSharp`
     there): the pattern is added.
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
  3. Leave `checks` empty unless the number was confirmed by a run in this task.
- **Done when**: the test passes; the pull request states how many suites are listed and how many are `unlisted` and why.
- **Verify**: `python -m unittest discover -s tools -p "test_player_tests.py"`.
- **Not in scope**: running a suite; editing [testing.md](testing.md) (T21).

### T06. In-player suites through the studio

- **Why**: F6, F10. Only `gunplaytest` returns a report today.
- **Needs**: **build**. No graphics card for the acceptance runs.
- **Depends on**: T03, T05.
- **Touches**: the profile and its test, a new small module shared with `tools/check_navigation.py` for reading a player's log, the
  engine if a suite cannot yet name its own default runtime.
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

- **Why**: F5, F6. 47 copies of one harness, and a list of 32 flags in `GameManager.cs` that half the changes to that file edit.
- **Needs**: **editor**, **build**.
- **Touches**: new `Scripts/Testing/TestRun.cs` and `Scripts/Testing/PlayerTest.cs`; `Scripts/TrapTest.cs`, `BarrelTest.cs`,
  `MineTest.cs`; `Scripts/GameManager.cs`; [testing.md](testing.md); [agent-tooling.md](agent-tooling.md).
- **Steps**
  1. Before anything: run the three suites on the current build and keep their logs (the list of checks, in order, with results).
  2. Write `TestRun` (compiled always) and `PlayerTest` (compiled out of distribution builds), following
     [agent-tooling.md](agent-tooling.md#the-in-player-harness). `result.json` must escape text correctly.
  3. Port the three suites: delete their private `Check`, log hook, loop and exit; keep every check's name and order.
  4. In `GameManager.cs` replace the `||` chain with `TestRun.Dedicated` or a legacy array of the flags not yet ported, **one flag per
     line**. Remove the three ported flags from it.
  5. Write `-testout` and `result.json` into the conventions of [testing.md](testing.md); mark the harness as existing in
     [agent-tooling.md](agent-tooling.md).
- **Done when**
  - For each of the three suites the checks, their order, their results and the result line are the same as before.
  - Each `result.json` is valid JSON (parse it with Python) with as many checks as the log.
  - A suite that was not ported still passes (`-survivaltest`), and the smoke walk passes.
  - No file outside the tests names `PlayerTest`: a distribution build would still compile.
  - Census: tests with their own `Check` 47 to 44; flags read by `GameManager.cs` down by three.
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

### T09. Compile check, doctor and ship status as data

- **Why**: F10. The fastest check is written nowhere; the state of a checkout is found by hand; `ship.ps1` answers in text.
- **Needs**: **editor** for the acceptance run.
- **Depends on**: T03.
- **Touches**: the profile and its test; the engine (`doctor --json`); `tools/ship.ps1` and `tools/test_ship_sources.py`;
  [building-and-shipping.md](building-and-shipping.md), [testing.md](testing.md).
- **Steps**
  1. Suite `compile` ([agent-tooling.md](agent-tooling.md#test-compile-does-it-compile)): the editor in batch mode with no method, under the
     project lock; each `error CS` line becomes a finding with file and line.
  2. `doctor --json` with the fields of [agent-tooling.md](agent-tooling.md#doctor-the-state-of-this-checkout). The text form stays.
  3. `ship.ps1` writes `unity_logs/ship_last.json` when it finishes, whatever the outcome: the step reached, the commit, the build mode,
     what was pushed, the uncommitted sources, the warnings. The script stays ASCII.
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
- **Touches**: new `tools/worktree.ps1`, `tools/test_worktree.py`, [building-and-shipping.md](building-and-shipping.md), rule 12 of `AGENTS.md`.
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
- **Touches**: `Scripts/TestPaths.cs`, `Scripts/GameManager.cs`, `Scripts/Proc/FloorDirector.cs`; [testing.md](testing.md).
- **Steps**: add `TestPaths.Scratch(name)` (the `unity_logs/player/` of the checkout, found as `TestPaths` finds `hotel_build`). Send
  there what the automatic run writes by itself: the report when `-report` is absent, the two timed screenshots, the bot's default log.
  Leave every output that a flag exists to ask for (`-camshots`, the photos of each suite).
- **Done when**: after the smoke walk and after one suite, `git status --porcelain hotel_build` prints nothing; the files appear under
  `unity_logs/player/`; the pages say the new paths.
- **Not in scope**: the 485 tracked files themselves and the other outputs under `hotel_build/` (a decision for the maintainer,
  [modular-roadmap.md](modular-roadmap.md#decisions-that-are-the-maintainers)). Delete nothing.

### T13. What a build rewrites: measure, then decide

- **Why**: F11. After a build the tree is never clean; nobody has written down which files change and why.
- **Needs**: **editor**, one full build.
- **Touches**: [building-and-shipping.md](building-and-shipping.md) only, unless the last step applies.
- **Steps**
  1. On a clean worktree: a full build (`BuildPlayer.Torre`), then a scripts-only one. After each, `git status --porcelain`, and each
     changed tracked file put in one class: only line endings (`git diff --ignore-cr-at-eol` is empty); order or identifiers but the same
     content; real content.
  2. For the first class, try a `.gitattributes` on a throwaway branch and measure again. Propose it only if renormalizing changes no
     content.
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

- **Why**: F8, B2. 34 engine-free files are kept pure by labs alone.
- **Needs**: **editor**, one **full build**.
- **Depends on**: T01 (so that the desert files are under a lab in the gate).
- **Touches**: the 34 files and their `.meta` (moved, not edited), a new `Rules/PryingEyes.Rules.asmdef`, the lab projects that name
  the old paths (`planlab`, `desertlab`, `saveslab`, `economylab`, the three projects of `tools/multiplayer-check`), the guard and its
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

### T16. Spike: can the game be a named assembly?

- **Why**: F1, B3, B4. The tests cannot have their own assembly while the game is `Assembly-CSharp`; nothing in the repository proves
  that the game compiles and plays as a named one.
- **Needs**: **editor**, **full build**. A throwaway branch.
- **Depends on**: T15.
- **Steps**
  1. On a branch that is not meant to merge: one assembly definition for the game at `Assets/_PryingEyes/` (it must cover `Scripts/`
     and `Multiplayer/`, which name each other), with the references the code needs (the render pipeline, the UI, FishNet, `Production.*`,
     `PryingEyes.Rules`, `PryingEyes.Multiplayer.Core`); one for the editor folders, editor only.
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
each one will say more than can be written now.

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

### T21. The tables of testing.md from the manifest

- **Why**: F5, F13. The catalogue of checks changed in 32 commits and is merged by hand.
- **Depends on**: T05, T06.
- **Steps**: a command that prints, from the manifest, the tables of sections 3 to 5 of [testing.md](testing.md) (name, command line, what
  it checks in one line, output, page). The long descriptions move to the page of each feature, where most already are. The guard
  fails when the tables in the page are not the ones the command prints.
- **Done when**: no sentence of the old tables is lost (each is in the manifest or in a linked page: state where); adding a suite means
  one manifest entry and one generated table.
