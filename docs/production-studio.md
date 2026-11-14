# Production Studio: first working content pipeline

Run from the repository root with the system Python. The verified environment uses
`tools/requirements-studio.txt`, Blender 4.5.3 LTS and Unity 6000.6.7f1. Override
`BLENDER_EXE` and `UNITY_EDITOR_EXE` when those applications are installed elsewhere.

```powershell
python tools/studio.py doctor
python tools/studio.py catalog list --type weapon
python tools/studio.py asset inspect p45_argine
python tools/studio.py asset preview p45_argine
python tools/studio.py asset build p45_argine --publish
python tools/studio.py test gunplay
python tools/studio.py report latest
```

`asset build` bakes and packs the model, renders previews, imports it, compiles a
separate City build, runs gunplay and the city acceptance loop. `--publish` copies
that verified build to `Builds/CityPreview` and compares every copied file by SHA256.
Without it, the executable stays in the job's own `build` directory. This command
does not update Torre's main/Prova executables or push Git changes.

Each operation writes `unity_logs/studio/<timestamp>-<id>/report.json`: operation,
subject, result, problem code/observed/expected, command to reproduce, child process
IDs, logs, timings, source hashes and artifacts. Source hashes are captured before
production and checked again; changing those sources during a job fails the job.
Missing city family exports are generated before compiling the city. On a clean
checkout the first build can therefore generate more than the requested asset.

An OS file lock serializes this project's Studio jobs and releases automatically
when the process exits. A second job is refused with `PROJECT_BUSY`. Studio also
checks for a live Editor using this project or a running CityPreview before building
or publishing. It does not close the user's applications. Timeouts stop only the
child owned by the job. Ctrl+C cancels the current child and releases the lock.

This is currently a lock/refusal workflow, not a persistent queue. Independent asset
generation is also serialized. Persistent queued jobs, a cancellation command for a
detached job and parallel asset generation remain future work.

## One source for weapon data

`Assets/_PryingEyes/Resources/Production/weapons.json` is the authoritative source of
all 30 weapon definitions and their production metadata. `WeaponDefs.All` loads it;
the old C# initializer table was removed. All 63 instance fields are required, so
class defaults cannot silently supply missing production values. Fire/reload modes
are named strings, colors are explicit RGBA arrays, and nullable reticles stay null.

`weapons.schema.json` is used by the Python tool. Runtime parsing additionally
rejects missing/unknown fields, wrong primitive types, unsupported modes, duplicate
IDs, nonfinite stats/colors, bad indices and invalid ammo budgets. The source records
identify the generator, recipe, required sockets and declared production dependencies.
The importer checks required sockets before saving the prefab.

The original 29 IDs retain their indices and exact field values. The migration was
compared against values exported from the running old definitions. The compact
reference `tools/fixtures/weapon-prefix-v1.json` preserves their hashes for
`ProductionCatalog.VerifyMigration`, and Studio prevents reordering/removing those
IDs. This historical hash check is a migration audit, not a second editable set of
weapon stats. Intentional future balance changes can change the catalog without
changing the runtime code; their compatibility fingerprint changes accordingly.

Co-op admission now uses `pe-coop-16` plus a fingerprint of the normalized catalog.
Peers with different production/balance data are refused instead of sharing
incompatible weapon indices or stats. This does not fingerprint every compiled mesh
or texture yet; full content provenance remains unfinished.

## Adding another municipal pistol

Append a new entry with the next index, a unique stable ID and a complete definition.
Choose `source.family = "municipal_pistol"` and supply the schema's parameters:
texture resolution, bore height, barrel start/end, serrations and polymer color.
The Blender registry creates these variants from catalog data. For this family,
`recipe.entry` names the generated registry entry rather than a separate Python
function for each variant. Add the ID to a city
shop in `citta.json` when it should be sold there, then run `asset build <id>`.
No weapon ID needs to be added to the runtime, importer or generator logic for another
variant of this family. A genuinely new model family still needs a generator and its
parameter contract; new gameplay mechanics can require runtime work.

RONDA-9 was migrated into this family. ARGINE .45 is the second data-driven variant:
a longer, warm-colored body, ten-round magazine, 58 base damage and 330 RPM. The
side/three-quarter previews automatically move the camera back until the complete
model fits with a margin, rejecting `PREVIEW_CLIPPED` instead of silently cropping.
ADS and player-view cameras retain their intended viewpoint.

## Recorded verification

- Complete build job: `unity_logs/studio/20261114T075933Z-596e5ebe/report.json`.
  Bake, pack, previews, Unity import/build, **297/297 gunplay checks**, **45/45 city
  checks**, and **192 copied files** verified. On this machine these steps totalled
  about 146 seconds with warm caches; this is not a cold-checkout benchmark.
- Existing-weapon build job: `unity_logs/studio/20261114T081804Z-984e1bd3/report.json`.
  RONDA-9 was regenerated through the same data-driven family and complete pipeline;
  all stages passed and CityPreview was updated after verification.
- Preview framing follow-up: `unity_logs/studio/20261114T080510Z-53fe4f41/report.json`.
  The earlier three-quarter image clipped the grip; the corrected image fits it.
- Native migration: `unity_logs/catalog-final-migration.log`: all fields of the
  original 29 definitions preserved; nine invalid catalogs rejected.
- Torre regression: `unity_logs/catalog-coop`: two native processes completed,
  **37/37 host** and **50/50 client** checks, including purchases, waves and descent.
- Native presentation: `unity_logs/catalog-presentation.log`, with hand/ADS/fire/
  reload images for both city pistols. Measured 420 RPM for RONDA-9 and 330 for
  ARGINE .45. This is a controlled firing test, not a combat balance assessment.
- `python tools/test_studio.py`: schema failures, ID stability, two-process locking
  and timeout reporting. `python tools/test_citylab.py`: layout and provenance gates.

## Scope still open

The catalog covers weapons and the first furnished-room recipe. Enemies still need
equivalent production records; the room palette is embedded in the room catalog, not yet a general material catalog. Dependency impact queries and a local gallery are not
implemented. Native gunplay/optics still primarily report through their existing logs,
and the native presentation lab still uses its legacy output directory. Studio's
common report wraps the checks that are integrated today.

Weapon state/input/presentation remain largely in `Weapon.cs`; this work extracts
definition data and production, not the full weapon runtime. The reusable district
package and independent Unity demo are documented separately in `city-prototype.md`.
The city's visual identity, paid-area progression, enemy art/animation, actual combat
balance, performance and urban co-op remain part of the active objective.


## Furnished-room pipeline

```powershell
python tools/studio.py catalog list --type room
python tools/studio.py asset inspect room_workshop_station
python tools/studio.py asset preview room_workshop_station
python tools/studio.py asset build room_workshop_station --publish
python -m unittest discover -s tools -p "test_rooms.py"
```

`Resources/Production/rooms.json` is the editable source, validated by
`rooms.schema.json`: palette, dimensions, portals, furniture, labels, lights and
route points. Each solid has explicit dimensions and collision; the schema bounds
the content budget to 500 parts. IDs are stable and unique within their scope.
The current recipe is a 25 × 15 m workshop with 43 parts and two exits.

`RoomPlan` belongs to the same engine-free `Production.Layout` assembly as the
district planner. PlanLab compiles that exact source for the tools. It resizes the
recipe, checks solid bounds and rejects furniture in the door clearance bands.
`CityRoomBuilder` is the Unity adapter: primitive geometry, materials, lighting,
colliders and labels. It remains game-specific; this is not yet an extracted
rendering or lighting framework. The floor material must currently be named
`concrete`, and the first adapter supports boxes, cylinders, lights and labels.

Bind a recipe ID to a block in `City/citta.json` to test it in the city. Unknown
blocks and duplicate bindings fail rather than disappearing from the result.
Builds store the source hash and complete room plan; CityLab compares both with
the requested recipe. An omitted room or stale furniture layout cannot pass just
because the executable exits successfully. The common Studio report now includes
the native findings and links their evidence, including on native test failure.

The lab produces an isolated cutaway preview, a local plan, a playable city build
and in-game interior/exterior images. Native checks compare placements, bounds,
collision and palette, walk the route with the real controller, and require an
enemy to pursue the player through the side exit. The preview uses the existing
ShotScene warm-up approach; its first render otherwise showed every surface with
the same material. A passing geometry test is not an artistic approval.

`unity/LayoutDemo` consumes `RoomPlan` with its own `Assets/room-demo.json`, without
Prying Eyes scripts, catalog or materials. Its native verification produces eight
checks, including a resized room, a rejected blocked exit and a separate room
scene. The recorded proof is `unity_logs/workshop-independent/report.json`.

Room contract checks resize the real workshop to 20 × 12 and 32 × 20 m and reject
a deliberately blocked entrance, missing material, misspelled field and nonfinite
coordinate. These are planner checks; native traversal is separately verified
in the full pipeline at the dimensions actually used by the city.

Complete room job: `unity_logs/studio/20261114T092523Z-94b35b15/report.json`.
All stages passed: **297/297 gunplay**, **67/67 native city/room findings**,
including sign visibility, and **192 published files** verified by SHA256 in
`Builds/CityPreview`. `Builds/Prova` remains the separately verified Torre build.
This proves the first furnished-room pipeline; the rest of the suite and the
polished urban survival game remain open work.
