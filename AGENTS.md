# Working agreements (Codex, Claude and any other agent)

This repository holds a game (Prying Eyes) **and** a production suite meant to outlive it. Keep the suite reusable.
The map and the reasons are in `docs/engineering/suite-architecture.md`; this page is the short version. The map of all the
documentation is `docs/README.md`; building, shipping and CI are in `docs/engineering/building-and-shipping.md`.

1. **Packages** (`unity/PryingEyes/Packages/*`): no `UnityEngine`, `UnityEditor` or game names; `noEngineReferences: true`; no assembly
   references. Every `.cs` is compiled by a lab under `tools/` from the *same file path* (never a copy) and the package is consumed
   by `unity/LayoutDemo`.
2. **Engine** (`tools/studio_engine.py`, `studio_process.py`, `studio_queue.py`, `asset_graph.py`): standard library only; names no
   game, content type or tool. New content kinds and checks go in the profile, `tools/studio_pryingeyes.py`, as `ContentType`/`Suite`.
   A second project is a `Project` subclass; `tools/fixtures/toy_project.py` is the smallest example.
3. **Before committing** run `python tools/check_suite.py` and `python -m unittest discover -s tools -p "test_*.py"` (about 30 s, no
   Unity). `tools/ship.ps1` runs both and refuses to commit when they fail. `check_suite.py` also checks the documentation (rule 13).
4. **Derived files stay out of git**: `Citta.unity`, `Labs/*`, regenerated `.mat`, `Torre.unity` churn, `hotel_build`, builds. Do not
   `git add -A` / `git add .`: stage the exact paths you changed.
5. Unit tests are hermetic. Anything that needs this machine's Blender outputs or a built player is an acceptance check behind an
   explicit flag (for example `citylab.py --require-assets`).
6. Do not run `tools/check_presentation.py` or the player's `-uitest` unprompted: they change the game's registry settings.
7. The maintainer writes in Italian; reply to them in Italian. Keep code, identifiers and docs in English.
8. **CI** (`.github/workflows/gate.yml`): every push to `preview` and every pull request against it runs the rule-3 gate plus the
   .NET rule labs (`python tools/studio.py test weapon-state`, `... test survival`, `... test economy`, `... test saves`, `... test track` and `... test desert`) on a sparse checkout. Before starting work look at
   the latest run on `preview` (`gh run list --branch preview --limit 1`); if it is red, fix that before anything else.
9. **One writer per Unity checkout**: in the checkout the editor opens, one agent writes at a time. Anyone who does not need the editor
   works in a separate git worktree on its own branch and reaches `preview` through a pull request.
10. **What `tools/ship.ps1` records**: it builds the working tree, not the commit. After committing it lists the uncommitted sources under
    `unity/PryingEyes/{Assets,Packages,ProjectSettings}` (derived files and line-ending-only changes excluded) in the `build:` and `exe:`
    lines, an AVVISO, `unity_logs/ship_built.json` and `Builds/build_info.json`. It never blocks on a dirty tree; `-RequireClean`
    refuses before the build and is for releases.
11. **Builds** (`unity/PryingEyes/Builds`, git-ignored): `PryingEyes.exe` with its `PryingEyes_Data`, `D3D12` and `MonoBleedingEdge` at the top
    level is the game the maintainer plays (the taskbar pin) and must always be the latest build. `tools/ship.ps1` refreshes it from `Audit`
    after every build and, when the game is open, finishes the copy by itself as soon as it closes; never close the game yourself.
    `Audit` is where builds and checks run: tests and agents use it, never the top-level game. `CityPreview` is where the studio publishes
    the city prototype and `Releases/` holds the released zips. Anything else (`*Candidate`, `Web`, per-release folders) is disposable:
    delete it when the task ends, and the per-job `unity_logs/studio/*/build` folders once the job is over.
12. **Branches and worktrees**: `preview` is the only long-lived branch. Any other branch goes through a pull request and is deleted as soon
    as it merges (a squash merge makes git call it unmerged: delete it anyway, its commits stay on the pull request). Make a worktree with
    `tools/worktree.ps1 -New <name>` (a new branch from `origin/preview` and a copy of a `Library`, so that Unity compiles without importing
    the assets; `-NoLibrary` when the work never opens Unity) and remove it with `-Remove <name>` when its task ends: it refuses while
    anything is uncommitted or unpushed, and deletes the branch once it is merged. A worktree made another way (`.codex/worktrees/*`,
    `prying-eyes-*`) is removed with `git worktree remove`. Nothing is lost: every commit of a merged branch stays reachable from `preview`.
13. **Documentation** (`README.md`, `docs/`): one topic per page, written in English, every page listed in `docs/README.md`, paths
    relative to the repository root (never this machine's). `python tools/check_suite.py` fails on a broken link, a page missing from
    the index and a machine-specific path. When a change alters what a page says, update the page in the same commit.
14. **Saves** (`unity/PryingEyes/Packages/com.pryingeyes.saves`, `unity/PryingEyes/Assets/_PryingEyes/Scripts/Characters/`): the characters and the
    suspended expedition of a player must survive every version. A change to the shape or the meaning of what a save holds bumps its `Version` and
    adds one tested migration (never edit a released step); new data usually needs none. The recipe, the files and the version history are in
    `docs/engineering/save-system.md`; `python tools/studio.py test saves` is part of the CI gate. Automated runs never touch the player's characters
    (`Roster.IsTest`: `characters-prova/`).
