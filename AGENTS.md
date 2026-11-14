# Working agreements (Codex, Claude and any other agent)

This repository holds a game (Prying Eyes) **and** a production suite meant to outlive it. Keep the suite reusable.
The map and the reasons are in `docs/suite-architecture.md`; this page is the short version.

1. **Packages** (`unity/PryingEyes/Packages/*`): no `UnityEngine`, `UnityEditor` or game names; `noEngineReferences: true`; no assembly
   references. Every `.cs` is compiled by a lab under `tools/` from the *same file path* (never a copy) and the package is consumed
   by `unity/LayoutDemo`.
2. **Engine** (`tools/studio_engine.py`, `studio_process.py`, `studio_queue.py`, `asset_graph.py`): standard library only; names no
   game, content type or tool. New content kinds and checks go in the profile, `tools/studio_pryingeyes.py`, as `ContentType`/`Suite`.
   A second project is a `Project` subclass; `tools/fixtures/toy_project.py` is the smallest example.
3. **Before committing** run `python tools/check_suite.py` and `python -m unittest discover -s tools -p "test_*.py"` (about 30 s, no
   Unity). `tools/ship.ps1` runs both and refuses to commit when they fail.
4. **Derived files stay out of git**: `Citta.unity`, `Labs/*`, regenerated `.mat`, `Torre.unity` churn, `hotel_build`, builds. Do not
   `git add -A` / `git add .`: stage the exact paths you changed.
5. Unit tests are hermetic. Anything that needs this machine's Blender outputs or a built player is an acceptance check behind an
   explicit flag (for example `citylab.py --require-assets`).
6. Do not run `tools/check_presentation.py` or the player's `-uitest` unprompted: they change the game's registry settings.
7. The maintainer writes in Italian; reply to them in Italian. Keep code, identifiers and docs in English.
