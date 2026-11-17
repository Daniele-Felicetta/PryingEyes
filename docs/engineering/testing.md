# Testing and verification

This page lists every verification the project has: the hermetic checks that gate a commit, the Unity acceptance checks, the in-game headless tests, the navigation and co-op checks, and the performance probe. Each entry gives the command line, what it checks, and where the output goes.

The catalogue was compiled from the former Italian README (`git show b77eca5:README.md`) and checked against the code: every `-flag` was grepped in `unity/PryingEyes/Assets/_PryingEyes` (Scripts and Editor) and in `tools/`, every Python and PowerShell entry point and every `-executeMethod` target was confirmed to exist. Where that README and the code disagree, the code is followed and the difference is noted. How the builds are made and shipped, and what the CI gate runs, is in [building-and-shipping.md](building-and-shipping.md).

## Conventions

- Run everything from the repository root. Paths such as `unity_logs/...` are relative to the working directory of the process.
- `$exe` is the verification build, `unity\PryingEyes\Builds\Audit\PryingEyes.exe`. Build it with `BuildPlayer.Torre -buildOut <folder>` (section 2.2). The game the maintainer plays is `unity\PryingEyes\Builds\PryingEyes.exe`, which `tools/ship.ps1` keeps up to date; never use it for tests, a running player locks its files.
- Output folders: `hotel_build/` (screenshots, text reports, generated assets) and `unity_logs/<area>/` (per-suite logs and photos). Both are derived files and stay out of git (AGENTS.md rule 4).
- The test hooks of the player (`GameManager`, `FloorDirector`, `PerfProbe`, `StealthTest`) write into the `hotel_build/` of the checkout they run in. `TestPaths` finds it by walking up from the data folder of the build (or of the editor project) until a folder with that name exists; outside a checkout it falls back to `hotel_build` under the persistent data path. No path of any machine is compiled into the game. The scripts under `Assets/_PryingEyes/Editor` still use absolute paths (known issue, see [../game/weapons.md](../game/weapons.md)).
- Exit codes: in-game tests exit `0` when every check passes and `1` when one fails. Some tests exit `2` when the current plan cannot host them (documented below). Unity's own options (`-batchmode`, `-nographics`, `-logFile`, `-projectPath`) are not defined by this project.

## 1. Before every commit (hermetic)

These run with no Unity, no Blender and no built player. `tools/ship.ps1` runs both and refuses to commit when either fails (AGENTS.md rule 3).

```powershell
python tools/check_suite.py
python -m unittest discover -s tools -p "test_*.py"
```

- **`tools/check_suite.py`**: boundary guard for the reusable suite. Packages under `unity/PryingEyes/Packages` have no game, engine or netcode names in code and compile through an engine-free lab. The Python engine (`studio_engine.py`, `studio_process.py`, `studio_queue.py`, dependency graph) names no game, content type or tool. It also checks the documentation: every relative link and anchor resolves, every page under `docs/` is listed in `docs/README.md`, every page starts with a title and is valid UTF-8, and no page names a machine-specific path (AGENTS.md rule 13). Exit `0` = PASS, `1` = findings. Output: stdout.
- **`unittest discover`**: unit tests for the Python tools in `tools/` (`test_asset_graph`, `test_check_suite`, `test_citylab`, `test_release`, `test_rooms`, `test_ship_sources`, `test_studio`, `test_studio_engine`, `test_studio_gallery`, `test_studio_pryingeyes`, `test_studio_queue`, `test_trailer`). About a minute. Exit `0` = all pass. Output: stdout.
- **On GitHub**: the `gate` workflow (`.github/workflows/gate.yml`) runs the same two commands on every push to `preview` and every pull request against it, then the three .NET rule labs, `python tools/studio.py test weapon-state`, `python tools/studio.py test survival` and `python tools/studio.py test economy` (the economy package and the cathedral catalog, see [../game/cathedral-and-catacombs.md](../game/cathedral-and-catacombs.md#verification)). The labs take the project lock, so they are not part of `ship.ps1`. See [building-and-shipping.md](building-and-shipping.md#the-gate).

## 2. Unity acceptance checks

These need an output that only exists on this machine (Blender bakes, a built player, the Unity editor) and are behind explicit commands. They are not run by the hermetic gate.

### 2.1 Asset pipeline (produces the inputs of the tests)

Blender 4.5 with the system Python (numpy, PIL). Commands are run from the folder named in the header.

```powershell
# modelli/armi/
blender -b -P genera_armi.py -- render <nomi> rapido lato     # previews in render/
blender -b -P genera_armi.py -- bake [nomi]                   # UV, PBR bake in tex/raw, FBX in export/ (~25 s per weapon)
python pack_tex.py                                            # base.jpg, mask.png, normal.jpg in tex/
python sheet_pil.py lato <nomi>                               # contact sheet of one view
python sheet_game.py hotel_build/armi lato 4                  # contact sheet (jpg) of game photos: mano, mira, lato, sparo, ricarica

# modelli/hotel/   (hands and furniture; these scripts are not in modelli/armi/)
blender -b -P genera_braccia.py -- build [nomi]               # arms per weapon, FBX in braccia/<id>.fbx
blender -b -P genera_braccia.py -- prova [giocatore destra sotto ...] <id>   # CPU previews
blender -b -P genera_braccia.py -- libera                     # the articulated hand, braccia/mano_libera.fbx
blender -b -P genera_braccia.py -- manica                     # the neutral sleeve, braccia/manica_libera.fbx
blender -b -P genera_braccia.py -- pose [nomi]                # grip tables presa_sinistra.json and presa_destra.json
blender -b -P genera_braccia.py -- vedi                       # previews of hand and sleeve in braccia/render/
python -I genera_texture_braccia.py [guanto] [manica]         # textures painted on hand and sleeve, Textures/Braccia/
blender -b -P genera_arredi3.py -- render [nomi]              # theatre/sanatorium furniture previews (CPU)
blender -b -P genera_arredi3.py -- build                      # FBX in arredi3/
blender -b -P genera_arredi4.py -- render [nomi]              # Guardaroba props (17, gu_*): CPU previews in arredi4/render/
blender -b -P genera_arredi4.py -- build [nomi]               # FBX in arredi4/, then copy them to unity\PryingEyes\Assets\_PryingEyes\Models\Arredi

# modelli/temi/
blender -b -P genera_accessori.py -- build                    # modelli/temi/export/accessori_temi.fbx
```

Unity steps (`Unity -batchmode -projectPath unity\PryingEyes -executeMethod ... -quit`, where `Unity` is the editor executable of version 6000.6.7f1; the commands below abbreviate this form):

| Method | What it does | Output |
|---|---|---|
| `PryingEyes.EditorTools.ArmiImport.Run` | Copies FBX and textures, materials and prefabs into `Resources/Armi`. | Assets only |
| `PryingEyes.EditorTools.ArmiShots.Run -armi p9_pieta` | Photographs one weapon in hand, aimed, from the side. | `hotel_build/armi/` |
| `PryingEyes.EditorTools.ManiImport.RunAndShots [-armi p9_pieta] [-viste mano,mira,ricarica]` | Imports `mani.fbx` and the per-weapon FBX (prefabs in `Resources/Mani` and `Resources/ManiArmi/<id>`), photographs the requested views without building the player. | `hotel_build/armi/` |
| `PryingEyes.EditorTools.NemiciTemiImport.RunAndShot` | Builds the theme enemy prefabs (`Resources/`) and photographs them in the hall. | `hotel_build/shot_temi_*.png` |
| `PryingEyes.EditorTools.PropShots.Run [-prefix gu_] [-props a,b] [-tag v1] [-torch] [-theme guardaroba] [-dist 1.0]` | Imports the kit (`TorreBuilder.FillKit`) and photographs each prop, three-quarter and front, in a small dark room with the theme's look; `-torch` adds the player's torch. | `hotel_build/props/<name>_3q<tag>.png`, `_fronte<tag>.png` |

### 2.2 Builds

Build methods, their flags, the `Builds/` folders and `tools/ship.ps1` are in [building-and-shipping.md](building-and-shipping.md). For verification, build into `Builds\Audit` and run the checks against that folder:

```powershell
Unity -batchmode -projectPath unity\PryingEyes -executeMethod PryingEyes.EditorTools.BuildPlayer.Torre -buildOut unity\PryingEyes\Builds\Audit -quit
```

Add `-scriptsOnly` after a change that touches scripts only (a full build has to be in that folder already). Always pass `-buildOut`: without it the build overwrites the game the maintainer plays.

### 2.3 Gunplay, presentation and city (need a built player)

```powershell
python tools/check_gunplay.py            # -batchmode -autotest -nofire -gunplaytest -seed 1000 -nographics
python tools/check_gunplay.py --optics   # -opticstest, needs graphics
python tools/check_gunplay.py --exe <exe> --out <cartella>
python tools/citylab.py --runtime <exe> --require-assets   # AGENTS.md example of an acceptance flag
```

- **`check_gunplay.py`**: runs `gunplaytest` (weapons, movement). Exit = the player's exit code if non-zero, else `0` only when the line `gunplaytest RISULTATO SUPERATA:` appears. Output: `unity_logs/gunplay/` (`player.log`).
- **`check_gunplay.py --optics`**: renders the reticles against the hitscan axis. Output: `unity_logs/optics/` (`-opticout`).
- **`citylab.py`**: city preview and report (`unity_logs/city_preview.svg`, `unity_logs/city_report.json`). `--build-assets` bakes the city weapons first; `--require-assets` makes missing assets a failure. Taken from AGENTS.md rule 5.

> **Do not run unprompted.** `tools/check_presentation.py` and the player's `-uitest` change the game's registry settings (AGENTS.md rule 6). Run them only when the maintainer asks.
>
> ```powershell
> python tools/check_presentation.py --shots     # -uitest, menus, touch input, party HUD
> ```
>
> Options: `--width`, `--height` (at least 320x180), `--shots`. Output: `unity_logs/presentation/` (or `presentation-WxH`). Exit = the player's exit code if non-zero, else `0` only when `uitest RISULTATO SUPERATA:` appears. Timeout 180 s.

### 2.4 Plan labs (.NET, no Unity)

```powershell
cd tools/planlab
dotnet run -c Release -- stats 3000        # statistics of the wing plans
dotnet run -c Release -- grid 2000         # grid plan: roles, doors, sizes, arenas, shutters
dotnet run -c Release -- regions 2000      # sealable round zones: candles with a zone, by theme and floor
dotnet run -c Release -- sheet <seed> 6 file.ppm   # plan map in PPM
dotnet run -c Release -- themes 3068/5 1000/3      # the theme of each seed/floor with the current settings
```

Add `old` anywhere on the line to use the previous generator (wing plans, `PlanGen.UseGrid = false`). Other words valid on any command: `waves` (the room layout of the wave cycle, the game's default), `theme=guardaroba` (or albergo, teatro, sanatorio: every plan of that theme), `guardaroba=N` (how many floors in a hundred, from the third up, are the Guardaroba; the default is 0) and `salt=N` (the salt of that second draw; `themes scan seed/floor ...` lists salts that keep the theme of the seeds you name). Output: stdout, or the `.ppm` file.

To prove that a change to the generator leaves the floors of the other themes alone, build the lab from the previous commit (`git archive <commit> tools/planlab unity/PryingEyes/Packages/com.pryingeyes.layout/Runtime unity/PryingEyes/Assets/_PryingEyes/Scripts/Proc` into an empty folder) and compare the output of `grid`, `survival`, `stats`, `regions`, `annex` and of `dump <seed> <floor>` between the two (only the timing line differs). Done on 16 November 2026 for the Guardaroba against `3938be9`: 2,000 plans per command and 48 dumps (12 seeds, floors 1, 3, 5, 8), identical with the theme off.

### 2.5 Procedural floor tools (Unity editor, edit mode)

```powershell
Unity -projectPath unity\PryingEyes -executeMethod PryingEyes.EditorTools.ProcShots.Run -seed 1000 -floor 1 [-rooms Navata,Camera] [-nav 1]
Unity -projectPath unity\PryingEyes -executeMethod PryingEyes.EditorTools.ProcCheck.Run -seeds 500 [-propstats]
Unity -projectPath unity\PryingEyes -executeMethod PryingEyes.EditorTools.NavMap.Run -seed 3017 -floor 2 -out navmap.ppm
```

- **`ProcShots`**: photographs one floor (or the rooms listed in `-rooms`); `-nav 1` draws the navmesh, `-look` applies the fog, light and grading the player sees. Output: `hotel_build/proc_<seme>_<piano>_<nome>.png`.
- **`ProcCheck`**: connectivity over hundreds of floors, plus an ASCII map of one room. Output: `hotel_build/proccheck.txt`, `hotel_build/procascii.txt`; with `-propstats` also `hotel_build/propstats.txt` (furniture of each kind by room type).
- Both read `-theme albergo|teatro|sanatorio|guardaroba` (forces the theme of every grid floor), `-guardaroba N` (percentage of Guardaroba floors from the third up) and `-survivallayout` (the room plans of the wave cycle, which is the game's default; without it the editor tools build the candle-cycle plan). See [../game/guardaroba.md](../game/guardaroba.md).
- **`NavMap`**: navmesh of one floor in PPM, walkable area, the component of the arrival, and the doors that separate it from the rest. Output: the `-out` file (default `hotel_build/navmap.ppm`).

## 3. In-game headless tests

The in-game tests run the real game with the autopilot. Common form:

```powershell
& $exe -batchmode -nographics -autotest <flag> -seed 1000 -floor 1
```

Use `-nographics` for logic only. Drop it for the tests that take photos (the ones with a `<flag>shots` option or a photo in their output). Most feel and enemy tests use the form `-batchmode -autotest -nofire -noenemies <flag> [<flag>shots] -seed 1000 -floor 1`. Stealth-family tests need `-stealthtest` as well (`StealthRig.cs`). The check counts in parentheses are the ones the former README gave; they were not recounted.

### 3.1 Guns, sights and feel

| Command (after `-batchmode -autotest`) | What it checks | Output |
|---|---|---|
| `-guntest [-armi id1,id2] [-fxtest] [-espositori]` | Fire rate, maximum recoil and spread for every gun (or the listed ones). `-fxtest` adds isolated effect quads; `-espositori` photographs the wall-buy panels. | `hotel_build/armi_gioco/` (photos, `guntest.txt`); `fxtest0-2.png` |
| `-leantest -camshots` | Lean in the elevator stops at a wall (wall at 38 cm: lean stops at 0.64). | `hotel_build/auto_cam*.png` |
| `-walltest` | Wall-buy prompt, purchase, point cost, reserve refill at half price. | Unity log; the former README also names `hotel_build/wallbuy.png`, a photo of the display made with `-camshots` |
| `-enemyshot` | Enemy photo. | `hotel_build/enemies.png` |
| `-magictest` | Monk ranged darts: distance, line of sight, damage. | Unity log |
| `-impacttest [-impactshots]` (7) | Impacts per surface: dust, bouncing debris, sparks, hole, sound; light on effects. | `unity_logs/impatti/` |
| `-magtest [-magshots]` (9) | Empty magazine falls and bounces; the hand brings the new one. | `unity_logs/caricatori/` |
| `-medaltest` (11) | Chain kills and feats (Doppietta, Tripletta, Quaterna, Strage, Mira divina, Intoccabile, Ultimo respiro, Fuoco purificatore, Lama santa). | Unity log |
| `-musictest [-musicwav]` (34) | Adaptive wave music: layers follow alive enemies and the Cardinale; organ chord on wave clear; `-nomusic` turns it off. `-musicwav` writes WAV files. | `unity_logs/music/` |
| `-lamptest [-lampshots]` (10) | Lamps break with one hit and the room darkens. | `unity_logs/lampade/` |
| `-lampadaritest [-lampadarishots]` (15) | Hanging chandeliers fall when shot (60 damage), chain breaks, crush whoever is below. | `unity_logs/lampadari/` |
| `-feeltest [-feelshots]` (11) | Room reflections, scoreboard (Tab), weapon inspection (I), explosion shake, sinking bodies. `-noprobes` compares without reflections. | `unity_logs/rifiniture/` |
| `-flinchtest [-flinchshots]` (9) | A non-lethal hit bends the monk's torso and head, and a spring brings them back. | `unity_logs/reazioni/` |
| `-mantletest [-mantleshots]` (9) | Jump at an obstacle of 42 to 135 cm: vault or climb. | `unity_logs/scavalco/` |
| `-shafttest [-shaftshots]` (9) | Moonlight and stained-glass light become visible shafts; they fade when entered. `-noshafts` compares. | `unity_logs/fasci/` |

### 3.2 Stealth, rounds, knife and sanctuaries (`-stealthtest` family)

| Command (after `-batchmode -autotest`) | What it checks | Output and exit |
|---|---|---|
| `-stealthtest` | Sight, hearing, torch, batteries, ping, exit opening. | `hotel_build/furtivo/furtivo.txt` and photos in `hotel_build/furtivo/` |
| `-stealthtest -roundtest [-roundshots]` | A round in a sealable zone: cancelled by death; full round with a shutter and an outside enemy; a second round with a counting formula; an expired round. `-roundshots` stops after the shutter and spawn photos. | `hotel_build/furtivo/round.txt` (photos `round_*.png`). Exit 0 ok, 1 failed, 2 floor has no sealable zone |
| `-stealthtest -knifetest` | Back takedown (prompt, silent, 130 points), facing stab, miss, cases without a takedown. | `hotel_build/furtivo/coltello.txt` (photos `coltello_*.png`). Exit 0 ok, 1 failed, 2 no open spot |
| `-stealthtest -droptest` | Ghost bonus, drops from kills, four per floor, the four effects, 30 s expiry, Processione dei Lumi. About one minute. | `hotel_build/furtivo/drop.txt` (photos `drop_*.png`) |
| `-stealthtest -booktest` | Library, chest and altar: reachable on foot, prompts and prices, buying one book closes the others, eight perks, consecration, chest offer and nine seconds, death penalty. About 45 s. | `hotel_build/furtivo/libri.txt` (photos `libri_*.png`). Exit 2 if a sanctuary is missing |
| `-stealthtest -gridtest` | Grid plan: roles, doors (1 to 4), frames, navmesh (all reachable except the secret room), sanctuaries, wall weapons, arenas, guards, boss, low passage of the secret room, node map. About 35 s. `-oldplan` turns it off. | `hotel_build/furtivo/griglia.txt` (maps `griglia_mappa_*.png`). Exit 0 ok, 1 failed, 2 not a grid plan |
| `-stealthtest -pushtest [-bodiesclash]` | Ten flying enemies on a still player must not move it or push it under the floor. About 25 s. `-bodiesclash` brings back the old collider behaviour and the test must fail. | `hotel_build/furtivo/spinta.txt`. Exit 0 ok, 1 failed |
| `-stealthtest -holescan` (`-holeat X,Z` describes one point) | Ray-casts a 40 cm grid for points with no floor, then drops the player to -60 m and checks that `FloorDirector.FallGuard` brings them back. | `hotel_build/furtivo/buchi.txt`. Exit 0 ok, 1 failed |
| `-stealthtest -alientest` | Three aliens and the corridor at several exposures. | `hotel_build/furtivo/alieni.txt` and photos |
| `-stealthtest -temitest` | Maschera, Direttore, Infermiere and Primario at the end of the longest hall; their surprises (whistle, sedative, reinforcements, three darts). Seeds: `2961 -floor 3` (theatre), `3046 -floor 3` (sanatorium). | `hotel_build/furtivo/temi.txt`, photos `tema_<Nome>.png` and `_3q.png` |
| `-stealthtest -refshot` (with `-seed 1000`) | Visual comparison of the church: cloth fibres follow the pose, all five monk clips present. | `hotel_build/fotorealismo/` |

### 3.3 Survival, architecture and modes

| Command | What it checks | Output |
|---|---|---|
| `-batchmode -nographics -autotest -cattedraletest -nowaves -seed 1000` (`-cattedraleshots` without `-nographics` for photos in `hotel_build/cattedrale/`) | The two zones alone, on a test profile: the cathedral (plan, market, sites, gates, no waves), buying and building with the real interactions (the cathedral is rebuilt), a descent into the Ossario (gates instead of lifts, starting points and supplies), a drop picked up into the satchel, the poor box, the return by the stair (satchel banked, points to oboli), death (half the commons kept, the rest in the remains), the remains picked up, the third level with the branch boss, its trophy and the return, the save reloaded. 49 checks. | Unity log only; exit 0 ok, 1 failed |
| `-batchmode -nographics -autotest -survivaltest -seed 1000` | Wave cycle: first wave after 8 s with 20 enemies and the alive cap; enemies spawn only in open areas, never closer than 3 m, always attacking; the score rises by the wave prize once plus the kills and the medals (`Medals.Points`); three waves open the lift; going down does not cancel the wave, death (bleeding out) does; the fifth is the Processione. | Unity log only; exit 0 ok, 1 failed |
| `-batchmode -autotest -architecturetest [-architectureaccess]` | Paid doors through the real controller: colliders, enemy navmesh, selection with F, insufficient points, purchase once, passing through. | `unity_logs/architecture/` |
| `-architecturesupply` | A candle-cycle test (it implies that cycle). Its checks are described in its test class header. | `unity_logs/supply/` |

Mode flags used by these tests and the bot:

- `-candles` (or `-legacycycle`): old candle cycle, no waves.
- `-oldplan`: the old wing plans instead of the room grid (implies the candle cycle).
- `-zombies`: enables the new wave cycle even when another flag selects the old one.
- `-nowaves`: keeps the waves still (plan, doors and points stay new): for photos and measurements without enemies.
- `-noenemies`: no enemies at all.

### 3.4 Enemies, traps, equipment and the horde

| Command | What it checks | Output |
|---|---|---|
| `-variantitest [-variantishots]` (24) | Appestato (toxic cloud), Flagellante (leaps), Statua (moves only when unseen), Cardinale (head of the late waves, health bar); blood and stains. Run from the repository root. | `unity_logs/varianti/` |
| `-traptest [-trapshots]` (18) | Door traps: a lever makes fire or lightning in the doorway for 20 s (1000 points, 40 s recharge); enemies that pass die, the player is scorched. | `unity_logs/trappole/` |
| `-equiptest [-equipshots]` (19) | Holy water (Z, grenade, 2 + 2 per wave), music box (4, lures the horde for 7 s then explodes), one Trial per wave with a prize. | `unity_logs/equipaggiamento/` |
| `-eyetest [-eyeshots]` (13) | Three glass eyes hidden in the annexes, balconies and far corners; taking all three consecrates the weapon in hand. Use `-floor 3`. | `unity_logs/occhi/` |
| `-nofire -noenemies -magiatest [-magiabooks] [-magiashots] [-magiaside] [-magiazoom] [-vfxdump]` | The secret book room (one per floor, the wall used with F, not crossable standing, crossable crouched, no enemy path, the floor's book), both grimoires (the real plate and its slots, the book inside its area, time running, rules, prices, all 38 nodes; `-magiabooks` stops after this part, 44 checks, about two minutes), the seven spells and seven seals on a rifle and a pistol (the free hand replaces the wrapped glove and gives it back, effects arrive, elbow, wrist and forearm within human ranges) and the left-hand throw. `-magiaside` adds side and top views of the arms and a close-up of the left wrist, `-magiazoom` close-ups of the left hand, `-vfxdump` the effect textures; `-armview [-armids a,b] [-presavecchia]` photographs only the rest and aiming pose of every weapon and checks both wrists at rest against the human joint ranges (with `-magiazoom` also a close-up of each hand); `-reloadview [-armids a,b]` photographs the reloads done by the left hand (empty and tactical) and the manual cycle after them. See [arms-and-magic.md](../game/arms-and-magic.md). | `hotel_build/magia/` (`magia.txt`, photos) |
| `-barreltest [-barrelshots]` (19) | Holy-oil barrels chain-explode; wave enemies rise from the floor. | `unity_logs/botti/` |
| `-powertest [-powershots]` (16) | Floor power: emergency lights at 40 %, breaker in the farthest area; the lever turns the hotel's power back on (once per wave); perks, traps and altar need power. `-nopower` starts with power on. | `unity_logs/corrente/` |
| `-minetest [-mineshots]` (12) | Reliquary mine (key 5): placed 1 m ahead, armed in 1.2 s, explodes when an enemy comes near, not the player. | `unity_logs/mine/` |
| `-ragdolltest [-ragdollshots]` (16) | Dead monks and abbots become ragdolls pushed by the killing hit; they stop and sink; at most 10 at once. `-noragdoll` compares. | `unity_logs/ragdoll/` |
| `-padtest -nowaves` (73) | The controller in the menus and the normal game, without `-autotest` (the menu must exist; usually `-batchmode`): the XInput to menu translation, the automatic search for a controller with a fake XInput (`PadControls.TestPoll`), the real menu driven with a fake state (selection, frame, tabs, sliders, back to the button of origin, text field, first command with nothing selected), and in the game A on *Gioca* not jumping, Start and B and the pause. With `-padshots FOLDER` (a real window, plus `-menureview` so the saved display is not applied) it saves screenshots of the menus instead. | the log |
| `-splittest` (63) | The parts of the split screen that need no second player: the layout of the parts for 2 to 4 players, controller assignment, the field-of-view cap, the XInput mapping, and the real player driven by a fake XInput state. Needs `-autotest -nofire -noenemies`. | the log (`-report FILE` keeps the result out of `hotel_build/autotest.txt`) |
| `-seatstest -splitscreen N` (70 with two players, 71 with three, 72 with four) | The split screen itself, in one process and one window, from the menu or with `-autotest`: see section 5.1. | the log; `unity_logs/seats/` with `-seatsshots` |

### 3.5 Further flags in the code

These flags exist in the code and have a test class, but the former README did not describe them either. They are listed here for completeness; their checks are described in the test class headers.

- `-citytest`, `-cityshots`: city runtime test (`unity_logs/city-runtime/`).
- `-combatfeedbacktest`, `-combatshots`: combat feedback (`unity_logs/combat-feedback/`).
- `-meleetest`, `-meleeshots`: melee (`unity_logs/melee/`).
- `-flyingmeleetest`, `-flyingshots`: flying melee (`unity_logs/flying-melee/`).
- `-menureview`: menu review (`unity_logs/menu/`).
- `-relictest`: relics (`unity_logs/relics/`).
- `-torchreview`: torch review (`unity_logs/torch-review/`).
- `-annextest`, `-architectureshots`, `-hitflashtest`, `-shrineprompttest`, `-displaytest`: exist in the code; output not checked.

## 4. Navigation and procedural floors

The strict bot walks the generated floors with the real `CharacterController`, buys the doors, takes the lift through the same selection the F key uses, and writes one line per floor plus one line per problem. Problems are: `INCASTRATO` (stuck for 4 s), `VARCO` (a gap the navmesh promises but the capsule cannot pass), `IRRAGGIUNGIBILE`, `NAVMESH`, `NONSELEZIONABILE`, `CADUTA`, `MORTE`, `TEMPO`, `SOFTLOCK`. The last line is `ESITO: ok` or `ESITO: FALLITO`, and it decides the exit code.

### 4.1 Batch runner (all seeds, in parallel)

```powershell
python tools/check_navigation.py [--seeds 1000 2961 3046 777 114 4242] [--floor 1] [--floors 5] [--jobs 6]
```

- Runs the bot on `unity\PryingEyes\Builds\Audit\PryingEyes.exe` for each seed. Options: `--exe`, `--out`, `--scale` (game time multiplier, 1 to 8, default 4), `--cap` (game seconds per floor, default 420), `--timeout` (real seconds per process, default 1500), `--legacy` (candle cycle), `--enemies` (enables waves in the new cycle), `--oldplan`, `--extra FLAG` (repeatable).
- Output: `unity_logs/navigation/nav_<seed>.txt` (bot) and `nav_<seed>_player.log` (Unity), plus a summary on stdout.
- Exit `0` only if every run ends with `ESITO: ok`.

### 4.2 One bot run by hand

```powershell
& $exe -batchmode -nographics -autotest -procbot -botstrict -noenemies -seed 2961 -floor 1 -botfloors 2 [-botscale 4 -botcap 420 -procsecs 240 -botlog <file> -report <file>]
```

- `-botstrict`: no recovery teleports, real controller. `-botfloors K`: complete K floors in a row. `-botscale N`: game time multiplier. `-botcap`: game seconds per floor. `-botlog`: log file (default `hotel_build/proctest.txt`). `-report`: separate report file, so runs in parallel do not write the same `hotel_build/autotest.txt`.
- `-botfreeze`: negative control. A player that stands still must make the run fail.
- Without `-botstrict`, `-procbot -procsecs 240 -seed 1000` runs the older autopilot that uses teleports. Add `-procshots` (without `-nographics`) to photograph the view with HUD: `hotel_build/gioco_p<piano>_<stanza>.png`.
- Exit `0` = `ESITO: ok`, `1` = failed (`FloorDirector.Bot.cs`).
- The bot buys the doors one by one, nearest first, on the enemies' navmesh (without the closed shutters), with 20 000 points and a 2.4 s pause after each purchase. Without enemies, waves do not run, so the bot opens the lift itself once the areas are done.

## 5. Co-op (FishNet, UDP port 7807)

Several Unity processes on one machine, with no graphics: one host and up to three guests. Each process has its own log. The scripts use the verification build by default.

```powershell
.\tools\coop\run_coop.ps1 -Clients 1 -HostArgs '-cooptest','-seed','2961' -ClientArgs '-cooptest'
.\tools\coop\run_all.ps1 [-Only a,c]
```

- **`run_coop.ps1`**: one host (`-coophost -cooplocal`) and `-Clients` guests (`-coopjoin 127.0.0.1`). Exit `0` = every check passed, `1` = a process hung or exited with an unexpected code (the process started with `-coopcrash` may exit non-zero), `3` = a copy of the same exe is already running.
  - Parameters: `-Exe` (default `unity\PryingEyes\Builds\Audit\PryingEyes.exe`), `-LogDir` (default `$env:TEMP\pryingeyes-coop`), `-Name`, `-TimeoutSec` (180), `-JoinDelaySec` (8), `-ClientDelays`, `-Pattern`, `-HostGraphics` (uses the GPU for photos with `-cooppics`).
  - Logs: `coop_<Name>_host.txt`, `coop_<Name>_c1.txt`, ... in `-LogDir`. The script defaults to `Builds\Audit`, never to the game the maintainer plays.
- **`run_all.ps1`**: all eight co-op checks in sequence, about 12 minutes. Exit `0` = all pass, `1` = some failed, `2` = no check selected.

| Name | Host args | Guest args | Checks |
|---|---|---|---|
| `a_griglia` | `-cooptest -seed 2961` | `-cooptest` | Grid plan, one guest. |
| `b_ali` | `-cooptest -oldplan -seed 1000 -floor 2` | `-cooptest -oldplan` | Wing plan. |
| `c_tardi` | `-cooptest -cooplate -seed 2961` | `-cooptest -cooplate` | Late join receives the floor state. |
| `d_esce` | `-cooptest -coopleave -seed 2961` | `-cooptest -coopleave` | Clean exit of a guest. |
| `e_ucciso` | `-cooptest -coopcrash -seed 2961` | `-cooptest -coopcrash` | A process is killed. |
| `f_ospite` | `-cooptest -coophostleave -seed 2961` | `-cooptest -coophostleave` | The host leaves. |
| `g_tre` | `-cooptest -coopplayers 3 -seed 2961` | `-cooptest -coopplayers 3` | Three players. |
| `h_quattro` | `-cooptest -coopplayers 4 -seed 2961` | `-cooptest -coopplayers 4` | Four players. |

The co-op code is in `Scripts/Coop/`. Protocol, limits and what is not yet verified are in `docs/multiplayer/README.md`.

### 5.1 Split screen (one game, one window)

The split screen on one PC is not a co-op any more: it is one process with one window (design, controls and limits in
[../multiplayer/split-screen.md](../multiplayer/split-screen.md)). It has two checks besides `-splittest` (section 3.4):

- **`-seatstest -splitscreen N`** (N = 2 to 4) runs inside the game, with a fake XInput state injected per player, and exits `0` only if
  every check passes. Without `-autotest` the game starts from the menu (the menu presses *N giocatori* by itself; use `-menureview -nowaves`
  so the saved display settings are not applied and no wave starts): this covers the real path and the pause. With `-autotest -nofire
  -noenemies` it starts without a menu and skips the pause. It checks structure (a body, camera, HUD, target and controller per player; cameras
  in their parts; one `AudioListener`; weapon layers; HUD rectangles), the context swap of `Seats.Enter`, each controller walking, firing and
  jumping alone, points, kills and hits per player, enemy targeting (nearest at spawn, switching to a closer player; with `-candles` also
  `Provoke` and sight), damage, going down, revive by a teammate, self-revive, bleed-out death and respawn of one player, one player's
  grimoire (only their book open, only they blocked, time running, the book inside their part, another player walking meanwhile, each
  controller driving only its own player's book), the pause with Start on any controller, and the return to one player.
  `-seatsshots` (a real window, no `-batchmode`) saves photos of the whole screen in `unity_logs/seats/`.
- **`tools/coop/run_split.ps1`** runs it from the verification build and reads the result. Parameters: `-Exe` (default `Builds\Audit`),
  `-LogDir` (default `$env:TEMP\pryingeyes-split`), `-Players` (2 to 4, default 3), `-Graphics` (a 1280x720 window and the photos), `-Candles`,
  `-NoMenu`, `-TimeoutSec` (240). Exit `0` only if the result is `SUPERATA`, `1` if a check fails or time runs out, `3` if a copy of the same
  exe is open (with `-Graphics`, if any game is open). With `-Graphics` the window rewrites the `Screenmanager` values of the game's registry
  key: the script saves them with `reg export` and restores them at the end. It uses the GPU and occupies the screen for about 40 seconds: do
  not run it while someone plays.

`PryingEyes.EditorTools.MenuReview.Run` (see the Verification section of [../game/display-and-menu.md](../game/display-and-menu.md#verification))
also renders the split-screen panel at four sizes and checks that its texts fit.

## 6. Performance probe

```powershell
& $exe -batchmode -autotest -perftest [-perfshots] [-perftag <nome>] [-perfdebug] -seed 2961 -floor 3
& $exe -batchmode -autotest -perftest -perfprops [-perftorch] [-perfgamma 0.6]
```

- Seeds: `2961` theatre, `2995` hotel, `3046` sanatorium, all at floor 3. Run **without** `-nographics`: the probe calls `cam.Render()` into a render texture and reads one pixel to wait for the GPU.
- Measures, for the largest room of each type viewed from the main threshold: logic time, CPU render time, GPU wait, p50, p95 and p99. Output: `hotel_build/perf.txt`.
- `-perfshots`: one photo per view, the player's real view with fog, lights and post-processing: `hotel_build/perf/<etichetta>_<tema>_<stanza>.png`.
- `-perfdebug`: lists the lights in each room.
- `-perfprops`: photographs each new prop close up; `-perftorch` turns the torch on; `-perfgamma 0.6` brightens the photo (photo only, not the game). Output: which rooms the props appear in, and how many.
- Measure with a free GPU. The probe warms the GPU for 3 s; if another program or the game uses it, numbers rise three to four times. Check with `nvidia-smi`, and do not run while someone plays.
- The former README gave reference numbers for the RTX 5060 Ti (for example, hotel 4.8 to 5.3 ms average, sanatorium 4.9 to 5.2 ms, theatre 3.9 to 4.4 ms, with the themed models, over three runs) and warned that GPU frequency varies by about 10 % between runs.

**Look tuning** (no rebuild): `-lookexp EV`, `-lookamb x`, `-lookfog x` (density), `-lookfogc x` (fog colour brightness), `-lookdim x` (floor lights), `-looksat d`, `-lookcon d`, `-lookbloom x`. Use them with the probe photos. The history of these passes is in `docs/history/quality-audit.md`.

## 7. Not catalogued

- `tools/check_display.py` (output `unity_logs/display/`) and `tools/multiplayer-check/Run-NetworkSmoke.ps1` exist and are not catalogued here. Read their headers before using them.

## Related documents

- [building-and-shipping.md](building-and-shipping.md): build methods, the `Builds/` folders, `tools/ship.ps1`, the gate and CI.
- [suite-architecture.md](suite-architecture.md): why the suite is split into packages and engine.
- [../game/tower.md](../game/tower.md) and [../game/weapons.md](../game/weapons.md): what the in-game tests exercise.
- [../multiplayer/README.md](../multiplayer/README.md): co-op protocol and limits (Italian).
- [../history/quality-audit.md](../history/quality-audit.md): which areas were verified and what was left open (Italian).
