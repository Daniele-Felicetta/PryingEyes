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

An OS file lock serializes production for a project, including when reports are
written to different output directories. Direct commands refuse a busy project;
queued commands wait for that Studio lock and remain cancellable. A live Editor
using the project or a running CityPreview still causes `PROJECT_IN_USE`: Studio
does not close the user's applications or silently build over them.

Requests can now be persisted in a SQLite FIFO queue, drained in the foreground
or by a background worker. Job reports are replaced atomically and record owner
PID plus process-creation token. Cancellation is a request observed by that owner;
it never kills an arbitrary recorded PID. Owned process trees are contained before
the actual command is allowed to launch, so cancelling CityLab also stops the game
it started. Completed generation/import writes are not rolled back on cancellation.

Independent generation is still serialized with the rest of a project job. A
separate generation pool remains open work; this is deliberately not a claim of
parallel generation throughput.

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

Weapon input, inventory, fire-mode selection and presentation remain in `Weapon.cs`.
A first mechanical-state module now owns ammunition, reload transitions and cadence;
the rest of the weapon runtime is not yet extracted. The reusable district
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


## Mechanical weapon-state module

`Packages/com.pryingeyes.weapons` is a second engine-free runtime assembly. The game
uses one `WeaponState` per inventory slot; it no longer maintains a separate reload
clock, shell counters, transfer algorithm or cadence timers inside `Weapon.cs`.
Game-specific rules calculate capacities, timings and bonuses, while the adapter
maps state events to audio, animation and HUD. No weapon ID or upgrade concept is
part of the package. Its README documents the caller's ordering and restoration rules.

```powershell
python tools/studio.py test weapon-state
python tools/studio.py test gunplay --runtime path/to/PryingEyes.exe
```

The managed lab has 1,454 comparisons/contracts and 89,076 per-frame samples.
It compiles the package source directly and compares against the frozen old reload
algorithm from `6fcbecf`, including all 30 weapon definitions, upgraded capacities,
scarce reserve, several time steps and reload bonuses. It intentionally preserves
the old event ordering, including one presentation stage per coarse tick. The
extra contracts prevent cancelled or externally refilled reloads from adding ammo.

Every asset build now runs the state lab before producing assets. Its findings
join the common report. Source capture also includes both local packages, assembly
definitions/manifests and the PlanLab/WeaponsLab programs and project files.

The independent `unity/LayoutDemo` now includes an interactive `WeaponDemo` in
the generated `RoomDemo` scene: Fire, Reload and Reset buttons. It references the
local package and built-in Unity modules, without the Prying Eyes runtime. Native
verification passes 12 checks across layout, rooms and weapon state, recorded in
`unity_logs/weapon-state-independent/report.json`.


## Weapon-state extraction: verified integration

Full pipeline job: `unity_logs/studio/20261114T123907Z-990ed0ba/report.json`.
The new package is compiled into the game and the full job passes 1,454 state
checks, 297/297 native gunplay checks and 67/67 city checks. It publishes and
verifies 193 files in `Builds/CityPreview`, including `Production.Weapons.dll`.
The independent Unity consumer passes 12 checks, with an interactive Fire/Reload/Reset
demo in the generated RoomDemo scene (`unity_logs/weapon-state-independent`).

The Torre build also passes 161/161 presentation checks (`unity_logs/weapon-state-ui`),
including reload HUD state, ammo exhaustion, inventory changes and a reload that
continues inside the cooperative menu. Preferences are restored by the existing
presentation test. This preserves the visible integration in addition to the pure
state comparison. Full logs are local evidence, not versioned artifacts.

Torre cooperative regression: host **38/38**, client **50/50**, both exit 0
in 103 seconds (`unity_logs/weapon-state-coop`). The Torre package DLL matches
the one verified in the city. Published **193 files** to `Builds/Prova`, all
compared by SHA256; provenance in `build-info.json` and
`unity_logs/weapon-state-torre-publish.json`. No network message format or balance
rule changes were introduced. Build-generated scene IDs and numerically identical
material rewrites are archived locally rather than included in the refactor.


## Persistent queue and cancellation

```powershell
python tools/studio.py queue add asset build room_workshop_station --publish
python tools/studio.py queue add test weapon-state
python tools/studio.py queue run --background
python tools/studio.py queue status
python tools/studio.py queue status JOB_ID
python tools/studio.py queue cancel JOB_ID
python tools/studio.py job list
python tools/studio.py job status JOB_ID
python tools/studio.py job cancel JOB_ID
```

`queue add` validates a supported asset/test request and captures source hashes.
The worker runs the stored request with its own report directory, source evidence,
logs and artifacts. If those sources changed before execution, it fails with
`QUEUED_SOURCES_CHANGED` before launching any step: resubmit deliberately against
the new version. The captured source set is checked again after acquiring the
project resource, including newly added files. This avoids compiling changed code
while reporting evidence for an older request.

`queue run` drains available requests and exits. `--background` returns the worker
PID and log path immediately. It is not a permanent service: requests submitted
after it exits need another `queue run`. SQLite preserves pending requests across
CLI/worker exits. One worker owns a queue at a time; jobs also share the project
resource lock with direct invocations and with queues using other report stores.
`STUDIO_OUTPUT` can isolate a report/queue store without bypassing that resource.

States are `queued`, `running`, `complete`, `failed`, `cancelled` and `interrupted`.
The report may additionally show `waiting` for the project lock or `cancelling`.
Cancelling a queued request prevents it from launching. Cancelling a running one
stops only its contained command tree. Ctrl+C also stops draining the queue and
leaves untouched requests pending. A failed/cancelled request does not prevent
other queued requests from running.

Recovery observes the recorded process identity; elapsed time or a stale lock file
is not enough to declare a worker dead. A reused PID is a different identity. If a
worker disappeared before finishing, the request becomes `interrupted`, without
automatically retrying partially executed production. If its complete report was
already written before it disappeared, recovery preserves that completed outcome.

`studio_queue.py` is a project-independent coordinator accepting a request payload,
a source snapshot and an execution callback. `studio_process.py` supplies the
OS-backed locks, atomic report writes and contained commands. They have no Unity
or Prying Eyes content dependency. The Windows containment uses
[Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects);
a waiting launcher joins the job before starting the requested command. The
Windows implementation is verified here; POSIX process-group support has not yet
received the same native acceptance run.

Recorded acceptance:

- `unity_logs/queue-tests.log`: 14 checks across existing Studio contracts and queue
  lifecycle integration. Real subprocesses verify FIFO order, worker exclusion,
  queued/running cancellation, nested child cleanup, timeout, abrupt worker death,
  pending-request resume, crash after report completion, PID-token distinction,
  source pinning and CLI rejection of invalid requests. A separately launched
  process stays alive during cancellation.
- `unity_logs/queue-source-lock-tests.log`: project lock still excludes another
  process even when its report output directory differs; source-file additions
  during the wait are rejected before execution.
- Real background requests in `unity_logs/queue-final`: a 1,454-check weapon-state
  run followed by the Unity workshop preview, each with its own report, captured
  sources, supervisor PID and artifacts.

The queue supports existing operations; it does not yet provide dependency impact
queries, a gallery, general material/enemy catalogs or the rest of the game runtime
refactoring. Those remain part of the active production-suite objective.


### Material persistence regression found during queue acceptance

The queued preview exposed two saved materials losing `_EMISSION` after the
Editor session ended. A same-session check initially passed; a fresh Unity
session correctly failed with `ROOM_EMISSION_IMPORT: paper` in
`unity_logs/queue-material-stored-red.log`. This is why a successful render is
not sufficient evidence of durable material settings.

`CityRoomBuilder` now marks emissive palette materials as `BakedEmissive`, instead
of leaving `EmissiveIsBlack`. The installed URP importer derives the shader
keyword from those flags. Non-emissive materials retain the black-emission flag.
Room preview/build operations add a separate Unity session that imports and
verifies the saved materials; native room checks also verify emission keyword
and RGB intensity. This is material persistence, not a new real-time GI feature.

Source capture currently covers the declared recipes, generators, C# sources,
local package definitions and production-tool sources. A complete transitive
dependency model for all shaders, textures and other imported inputs remains
open work; source pinning is not a claim that this graph already exists.


Final queued full-production proof: `unity_logs/queue-production/20261114T131900Z-5d41b9d7/report.json`.
The background worker completes **1,454 state checks**, **297/297 native gunplay**,
**68/68 city/room checks**, and a fresh-session import check of **7 materials**.
All **193 files** published to CityPreview are verified. The source snapshots match
the implementation at verification. The extra city check covers emissive keyword
and intensity. Before the material fix, fresh-session verification failed with
`ROOM_EMISSION_IMPORT: paper` (`unity_logs/queue-material-stored-red.log`); the
corrected flags and saved keywords survive the new session. The final queue
regression suite remains **14/14** (`unity_logs/queue-tests-final.log`).
