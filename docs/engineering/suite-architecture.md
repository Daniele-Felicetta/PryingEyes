# Production suite: what is reusable, where the boundary is, how a second project plugs in

The goal of the engineering work is a suite that outlives Prying Eyes. This page is the map: which parts are already
independent of the game, what enforces that, and what is still game-specific. It is written to be checked, not admired:
`python tools/check_suite.py` fails when a boundary below is crossed; `tools/ship.ps1` runs it, with the Python unit tests, before every
commit, and the `gate` workflow (`.github/workflows/gate.yml`) runs both, plus the six .NET rule labs and the fingerprint of the floor generator, on every push to `preview` and every
pull request against it. How a change is built, shipped and gated is in [building-and-shipping.md](building-and-shipping.md).

## Layers

| Layer | Where | Depends on | Proof it is independent |
| --- | --- | --- | --- |
| Rule packages (C#) | `unity/PryingEyes/Packages/com.pryingeyes.{layout,weapons,survival,economy,saves}` | nothing: `noEngineReferences`, no assembly references, no `UnityEngine`/`PryingEyes` in code | the same `.cs` files are compiled by the .NET labs `tools/{planlab,weaponslab,survivallab,economylab,saveslab}` (no editor) and consumed by `unity/LayoutDemo` through `file:` references |
| Production engine (Python) | `tools/studio_engine.py`, `studio_process.py`, `studio_queue.py`, `asset_graph.py` | the standard library | `test_studio_engine.py` runs it on a toy project; a test imports it with `python -I` and asserts no game module, `jsonschema` or Pillow is loaded |
| Project profile | `tools/studio_pryingeyes.py` + the catalogs under `Resources/Production` | the engine, the game | this is the *only* place that knows weapons, rooms, Blender, Unity, the city recipe |
| Guard | `tools/check_suite.py`, `tools/test_check_suite.py` | the repository layout, the census `tools/code_census.py` for the game's sources, and the wiki generator `tools/wiki_site.py` for the wiki | the guard is itself tested against a fixture repository with every kind of violation |

The C# packages already use neutral assembly and namespace names (`Production.*`); only the package ids carry the game name.

The guard also keeps the documentation honest: relative links and anchors must resolve, every page under `docs/` must be listed in
`docs/README.md`, and no page may name a machine-specific path (rule 13 of `AGENTS.md`). The repository is public, so the guard treats a
path of one machine in a page as a defect, like a broken link.

The guard also builds the wiki in `wiki/` with `tools/wiki_site.py` and fails on any finding in its pages, links, images or concepts, and
on any generated file that is out of date. A tree with no `wiki/` folder has nothing to check; `docs/README.md` links to the wiki, so
removing it breaks a link there instead.

## The game's rules about tests

The guard reads the game's C# sources (`unity/PryingEyes/Assets/_PryingEyes`) through the census (`tools/code_census.py`) and fails when
a test is where it should not be. A tree without the game folder has nothing to check.

| Code | Fails when | What to do |
| --- | --- | --- |
| `GAME_PRODUCTION_NAMES_TEST` | a file that is not a test names a test type | Remove the reference. A test installs itself when its flag is on the command line and nothing else starts it, so a distribution build can leave it out |
| `GAME_TEST_NOT_EXCLUDED` | a test file (`Test` in its file name, `TestPaths` and the `Editor/` folders apart) has no `PE_DISTRIBUTION_BUILD` in it | Wrap the whole file in `#if !PE_DISTRIBUTION_BUILD`, every file of a partial class alike |
| `GAME_PURE_FILE_NAMES_ENGINE` | a game file that an engine-free lab compiles names `UnityEngine` or `UnityEditor` | Take the engine out of the file, or stop compiling it in the lab: the lab is what proves that the rules run without the editor |
| `GAME_BASELINE_STALE` | an entry of the baseline no longer applies | Delete the entry: the violation it tolerated is gone |
| `GAME_BASELINE_INVALID` | the baseline is not JSON, names a rule that does not exist, or has an entry without its file or its reason | Correct the fixture |

An *engine-free lab* is a .NET project under `tools/` that reaches no Unity assembly: it has no `Reference` to one, neither in the
project nor in a props file it imports nor in a project it references. The projects of `tools/multiplayer-check` that do
(`unity-compile`, `fishnet-compile`) compile engine code against the editor's own libraries; they are compile checks, and the rule leaves them out.
What a lab compiles is read from its `Compile` items, patterns with `*` and `**` included, so the files of `Scripts/Pista` that `tracklab` compiles are covered
like any other.

What is wrong today is listed, not hidden, in `tools/fixtures/game-boundaries.json`: each violation with the reason it is tolerated (today two
production files that name a test type, `Coop/CoopGame.cs` and `Stealth/StealthRig.cs`, and the 14 test files that are not wrapped:
`ArchitectureTest*`, `CityTest*`, `CoopTest*` and `StealthTest*`; none for the engine rule). The list can only shrink. A violation that is
not in it fails the guard, and so does an entry whose violation is gone, so an entry is deleted in the commit that fixes it. Adding one
needs a reason that a reviewer accepts, and the entry carries it. The numbers are in
[modularization.md](modularization.md#how-progress-is-read).

## Where the gate runs

| Where | What | Needs |
| --- | --- | --- |
| `tools/ship.ps1`, before every commit | `check_suite.py` and the Python unit tests | Python, no Unity |
| the `gate` workflow: every push to `preview`, every pull request against it, or by hand | the same two, then `studio.py test weapon-state`, `studio.py test survival`, `studio.py test economy`, `studio.py test saves`, `studio.py test track`, `studio.py test desert` and `studio.py test plan`; the labs' job folders (`report.json` included) are kept as the `lab-reports` artifact | a Windows runner, a sparse checkout of about 20 MiB |

The labs are not part of `ship.ps1`: they take the project lock and would fail with `PROJECT_BUSY` whenever another agent has a
production running on the same checkout. The runner is Windows because the engine owns process trees with Job Objects and its POSIX
variant has never been verified. After the guard the gate prints the census of the game's code in the run summary (`code_census.py`: sizes,
cycles between folders, tests, flags) and keeps its whole result as `code-census.json` in the `lab-reports` artifact; that step reports and never fails.
The checkout lists the only paths the gate reads (the `sparse-checkout` block of the workflow). It includes `wiki/`, which the wiki
check reads, the `.py` scripts of `modelli/`, which the wiki's pages name, `.github/workflows/gate.yml`, which the CI page cites, and the `.asmdef`
files of the game, which the census reads to know the assembly of each file.
Every source a wiki page cites must be in that list, or the gate fails with `WIKI_SOURCE_MISSING`. To check the list after a change,
make a throwaway sparse copy with exactly those patterns and run the gate's commands there. There is no branch
protection (`ship.ps1` pushes straight to `preview`), so the gate reports rather than blocks: a red run on `preview` is fixed before
anything else (rule 8 of `AGENTS.md`).

## What the engine gives any project

A *Job* writes one `report.json`: operation, subject, the hash of every source file it ran against, each step's command, log,
exit code and timing, findings as `code / observed / expected`, evidence artifacts, and the command that reproduces it. Around
jobs the engine adds:

- a cross-process lock on one resource (for Unity: the project folder, because one editor can open it), so direct commands
  refuse a busy project (`PROJECT_BUSY`) and queued ones wait and stay cancellable;
- a source guard: if any hashed file changes while a job waits or runs, it fails with `SOURCE_CHANGED_DURING_JOB` and names the files;
- owned process trees: a step that times out or is cancelled stops the tool it started and every child of it;
- a persistent FIFO queue (SQLite) whose requests carry the source snapshot they were submitted against;
- the command line: `doctor`, `catalog list|affected`, `asset inspect|preview|build [--publish]`, `test <suite> [--runtime]`,
  `report latest`, `gallery build`, `queue add|run|status|cancel`, `job list|status|cancel`.

Behaviour was preserved by construction when the engine was split from the game: the output (stdout, exit code, reports'
source sets, gallery manifest, dependency queries) of thirteen read-only commands was captured before the split and compared
after it; the only difference is the two new modules appearing in each job's hashed sources.

## Plugging in a second project

A project is a `Project` subclass (`tools/studio_engine.py`). `tools/fixtures/toy_project.py` is a complete working one in
about sixty lines, and `tools/test_studio_engine.py` is its test. The contract:

```python
class Sprites(ContentType):                 # one kind of content = one catalog
    name = "sprite"
    def load(self, strict=True): ...        # parse the catalog; strict also validates schema and sources
    def entries(self, data): ...            # the list of entries
    def entry_id(self, entry): ...          # ids must be unique across ALL content types (CONTENT_ID_COLLISION)
    def files(self): ...                    # catalog, schema, validators, generators: every job is bound to them
    def build(self, ctx, entry): ...        # produce into ctx.build_dir with ctx.job.run(...) steps; return the artifact

class MyProject(Project):
    root, default_output, entry_script = ...
    content_types = (Sprites(...),)
    suites = {"smoke": Suite(lambda ctx, runtime: ctx.job.run("smoke", [...]))}
    # optional hooks: tools(), lock_resource(), ensure_free(), tool_sources(command), doctor(), before_build(),
    #                 after_build(), graph(), gallery(), publish_dir
```

Then `tools/<yourproject>.py` is three lines: build the project and call `studio_engine.main(project)`.

## Rules that keep it reusable

1. A package never names the game or the engine and is always compiled by a lab from the same file path (a copy proves nothing).
2. The engine never names a game, a content type or a tool. New content or a new check goes in the profile.
3. Derived artifacts stay out of git (`Citta.unity`, `Labs/`, regenerated `.mat`, builds, `hotel_build`). Tracked, they changed in
   every commit and were swept into unrelated ones by `git add -A`. List the paths you changed instead of adding everything.
4. A test that needs a machine's Blender outputs or a built player is an *acceptance* check behind an explicit flag
   (`citylab.py --require-assets`), never part of the hermetic unit suite.

## Still game-specific, deliberately not hidden

- `studio_gallery.py` takes assets, graph and reports as data, but its evidence conventions (`<id>_<view>.png`,
  `weapon-state.json`, `city-runtime/run.json`) are the Prying Eyes pipeline's. A second project with different evidence needs the
  conventions passed in; that refactor was not done because there is no second consumer to shape it yet.
- `content_catalog.py` and `room_catalog.py` validate two catalogs with parallel code; a shared helper is the obvious next cut.
- `citylab.py` checks the city's native report; that contract is the game's.
- `Weapon.cs` still mixes input and presentation with the extracted mechanical state (`WeaponState`).
- Package ids (`com.pryingeyes.*`) and the physical location under `unity/PryingEyes/Packages` are the game's. Moving the packages
  and the engine under a neutral `suite/` folder with its own repository history is cheap now (the guard shows nothing crosses
  the line) and worth doing when a second project actually starts, not before.
- Missing labs for materials, lighting and performance: visual quality still depends on running the player.
