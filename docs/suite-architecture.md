# Production suite: what is reusable, where the boundary is, how a second project plugs in

The goal of the engineering work is a suite that outlives Prying Eyes. This page is the map: which parts are already
independent of the game, what enforces that, and what is still game-specific. It is written to be checked, not admired:
`python tools/check_suite.py` fails when a boundary below is crossed, and `tools/ship.ps1` runs it before every commit.

## Layers

| Layer | Where | Depends on | Proof it is independent |
| --- | --- | --- | --- |
| Rule packages (C#) | `unity/PryingEyes/Packages/com.pryingeyes.{layout,weapons,survival}` | nothing: `noEngineReferences`, no assembly references, no `UnityEngine`/`PryingEyes` in code | the same `.cs` files are compiled by the .NET labs `tools/{planlab,weaponslab,survivallab}` (no editor) and consumed by `unity/LayoutDemo` through `file:` references |
| Production engine (Python) | `tools/studio_engine.py`, `studio_process.py`, `studio_queue.py`, `asset_graph.py` | the standard library | `test_studio_engine.py` runs it on a toy project; a test imports it with `python -I` and asserts no game module, `jsonschema` or Pillow is loaded |
| Project profile | `tools/studio_pryingeyes.py` + the catalogs under `Resources/Production` | the engine, the game | this is the *only* place that knows weapons, rooms, Blender, Unity, the city recipe |
| Guard | `tools/check_suite.py`, `tools/test_check_suite.py` | the repository layout | the guard is itself tested against a fixture repository with every kind of violation |

The C# packages already use neutral assembly and namespace names (`Production.*`); only the package ids carry the game name.

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
about forty lines, and `tools/test_studio_engine.py` is its test. The contract:

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
