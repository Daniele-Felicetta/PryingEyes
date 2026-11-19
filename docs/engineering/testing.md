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
- **On GitHub**: the `gate` workflow (`.github/workflows/gate.yml`) runs the same two commands on every push to `preview` and every pull request against it, then the four .NET rule labs, `python tools/studio.py test weapon-state`, `python tools/studio.py test survival`, `python tools/studio.py test economy` (the economy package and the cathedral catalog, see [../game/cathedral-and-catacombs.md](../game/cathedral-and-catacombs.md#verification)) and `python tools/studio.py test saves` (the save library and the character and run files, 115 checks, see [save-system.md](save-system.md#how-it-is-checked)). The labs take the project lock, so they are not part of `ship.ps1`. See [building-and-shipping.md](building-and-shipping.md#the-gate).

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
| `PryingEyes.EditorTools.PropShots.Run [-prefix gu_] [-props a,b] [-tag v1] [-torch] [-theme guardaroba] [-dist 1.0] [-lod 0\|1\|2]` | Imports the kit (`TorreBuilder.FillKit`) and photographs each prop, three-quarter and front, in a small dark room with the theme's look; `-torch` adds the player's torch; `-lod N` forces a level of detail in the props that have a group ([props-phase5.md](../game/props-phase5.md)). | `hotel_build/props/<name>_3q<tag>.png`, `_fronte<tag>.png` |

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
dotnet run -c Release -- waves mapart <seed> <floor> map.png [all|walk|start] [width] [mini]   # a floor painted as the in-game map
dotnet run -c Release -- maptex <folder>           # the generated map textures as PNG
```

Add `old` anywhere on the line to use the previous generator (wing plans, `PlanGen.UseGrid = false`). Other words valid on any command: `waves` (the room layout of the wave cycle, the game's default), `theme=guardaroba` (or albergo, teatro, sanatorio: every plan of that theme), `guardaroba=N` (how many floors in a hundred, from the third up, are the Guardaroba; the default is 0) and `salt=N` (the salt of that second draw; `themes scan seed/floor ...` lists salts that keep the theme of the seeds you name). Output: stdout, or the `.ppm` or `.png` file. `mapart` paints the floor with the same files as the game's map ([../game/map.md](../game/map.md)), draws a mark where Unity writes a label and uses invented furniture.

The pure code of the desert ([../game/the-desert.md](../game/the-desert.md)) has a lab of its own, `tools/desertlab`, that compiles the game's own files from their paths (`Scripts/Deserto/Desert{Noise,Clock,World,TerrainGen,ScatterGen,Weather,Grade}.cs`, the generators and the cathedral's plan `HubPlan.cs` in `Scripts/Proc`):

```powershell
dotnet run --project tools/desertlab -c Release            # a few seconds once built
dotnet run --project tools/desertlab -c Release -- --quick # fewer samples
dotnet run --project tools/desertlab -c Release -- --map out.ppm 0 0 4000 800 [seed] [h]   # a hill-shaded map of the ground: centre x z, side in m, pixels
```

It prints `PASS`/`FAIL` for 98 checks and `ALL PASS` (exit 0) or the failures (exit 1). `NOISE_*`: the noise is repeatable and inside its ranges. `CLOCK_*`: the day starts in daylight, noon is the highest sun, midnight is dark with stars, the moon is opposite the sun, a day is 1,200 s. `GROUND_*`: flat around the cathedral, repeatable, never below the sand level, never steeper than 42 degrees on the samples a walker can reach, dunes taller with the distance, dunes with a slip face (along the wind the sand falls at least 1.25 times steeper than it rises) and badlands with a few cliffs (a handful of samples steeper than 45 degrees, not most of the land). `BRINE_*`: the film of water on the salt flats (`DesertWorld.Brine`) exists only in the middle of the flats (never where they are less than 90 % of the ground), covers between 15 % and 40 % of them (about a third), rides on the top fifth of the sand's vertex alpha (`BrineAlpha`: dry vertices keep the region's alpha, wet ones sit between 0.8 and 1, and the shader's decoding gives the depth back) and is the same for the same seed whatever the order of the questions. `--map` is not a check: it writes a binary `.ppm` (convert it with any viewer or PIL) that the eyes can judge before a Unity build does. `TERRAIN_*`: a sand block covers its square exactly, faces up, has unit normals, and a grid line goes through every edge of a well so the hole is cut exactly (59 wells in 1,593 blocks at three steps, error 0), and nothing of a block hangs inside a well either: the skirt that hides the crack between two blocks must not run across a well that straddles their edge (it did, as a wall of sand in the middle of a stair that stopped the walk down; 10 of the 61 holes checked straddle an edge). `BUILDING_*` and `STACK_*`: the buildings and the stacked levels of the towers and catacombs pass `BuildingFault`, have a door to the sand, fit their cell, lose their roofs when collapsing, drift apart when split, and every doorway lower than a person (`BUILDING_LOW_DOORWAYS_ARE_GAPS`: 1,783 of them in 233 of the 600 plans) is a gap, a doorway without lintel whose head is the crumbled wall it was cut to and which no soffit closes (it did: the doors of collapsed buildings were 1.5 m high under a ceiling slab, and the navmesh agent, 1.9 m tall, could not enter the building); the stair room is the same on every level, every room is reachable from it, only the last level has a boss hall. `WORLD_*`: the density of places, none overlapping or near the cathedral, flat pads, the welcome chapel and the landmark tower, towers up to 14 floors (76 m), and the same seed gives the same world. `BUTTES_*`: the isolated mesas (`DesertWorld.Buttes`) exist (a dozen or more in the 11 x 11 squares of 900 m around the origin, never one in every square), are 28 to 95 m tall, stand mostly in the badlands, keep 250 m away from the cathedral, are absent round the cathedral and the landmark tower, have a flat top and a wall steeper than 54 degrees, and no place of the world is cut into one. `COMPASS_*`: the sector the HUD names for a target (*davanti*, *a destra*, *dietro*...: `DesertWorld.Sector`) is right for 24 headings against 8 targets, is undefined on the spot or with no look, and the landmark is the cathedral's centre. `REGION_*`: the six regions exist in the proportions of their table, the cathedral's is always the hamlets (on forty seeds), the blend weights, the sand tint and the dune height stay in their ranges, and the places follow their region (many houses in the hamlets, big buildings in the dead town). `SCATTER_*`: the things on the sand are the same twice, never on a place's footprint nor on the cathedral, stay within 18 m of their square, are never too many or out of scale, follow their region (red rocks in the badlands, no cacti on the salt, columns in the holy land, bushes in the hamlets), the solid ones have a capsule, and every fire has one to three tents round it with their doors to it. `YARDS_*`: every building has a yard of props (about four each), none within 4 m of a door that opens outside, the door positions that the yards read lie on the wall of their own room, none stands inside a footprint, and a prop of a yard comes out of exactly one 64 m square (never of two). `HUB_*` and `OPEN_ROOMS_*`: the cathedral's plan (with and without the desert, with nothing built and with everything) passes `HubPlan.Validate`, its wide door onto the stairs is a gap in the parapet (`Opening.ParapetTop`, 1.55 m: it used to be 3 m high over a wall of 1.55, so its two reveals and its soffit hung in the air, a black beam of 14 m that, seen from below, crossed the sky as a dark diagonal in every view from the churchyard), and no opening of an open-air room onto the void, in the cathedral, the buildings or the towers, rises above the parapet. `WEATHER_*`: the weather is the same for the same seed and clock, stays in its ranges, never has a storm in the first morning, grows and fades smoothly, has a storm on about 55% of the days, and in a storm blows, thickens the fog and dims the sun (less by night). `GRADE_*`: the outdoor colour grade (`DesertGrade.Outdoors`) stays in its ranges over every hour and every weather, changes smoothly (no jump between two samples a minute apart), is warm at golden hour, cool at night, dusty in a storm, pushes colour and contrast by day, and keeps the bloom threshold high in the sun (`GRADE_BLOOM_IS_HIGH_IN_THE_SUN`: bright sand and sky must not glow into a veil) and keeps the day highlights almost neutral (`GRADE_DAY_HIGHLIGHTS_KEEP_THE_SKY_BLUE`: URP's split toning is a soft-light blend, so a strongly warm tint lifts the red of everything bright and a blue sky turns grey-lilac).

To prove that a change to the generator leaves the floors of the other themes alone, build the lab from the previous commit (`git archive <commit> tools/planlab unity/PryingEyes/Packages/com.pryingeyes.layout/Runtime unity/PryingEyes/Assets/_PryingEyes/Scripts/Proc` into an empty folder) and compare the output of `grid`, `survival`, `stats`, `regions`, `annex` and of `dump <seed> <floor>` between the two (only the timing line differs). Done on 16 November 2026 for the Guardaroba against `3938be9`: 2,000 plans per command and 48 dumps (12 seeds, floors 1, 3, 5, 8), identical with the theme off.

### 2.5 Procedural floor tools (Unity editor, edit mode)

```powershell
Unity -projectPath unity\PryingEyes -executeMethod PryingEyes.EditorTools.ProcShots.Run -seed 1000 -floor 1 [-rooms Navata,Camera] [-nav 1]
Unity -projectPath unity\PryingEyes -executeMethod PryingEyes.EditorTools.ProcCheck.Run -seeds 500 [-propstats]
Unity -projectPath unity\PryingEyes -executeMethod PryingEyes.EditorTools.NavMap.Run -seed 3017 -floor 2 -out navmap.ppm
```

- **`ProcShots`**: photographs one floor (or the rooms listed in `-rooms`); `-nav 1` draws the navmesh, `-look` applies the fog, light and grading the player sees. Output: `hotel_build/proc_<seme>_<piano>_<nome>.png`, with `_<tag>` before the extension when `-tag <tag>` is given (so a before and an after of the same seed do not overwrite each other). `-colshot` adds a close-up of an arena column (the first in creation order, from the first side nothing blocks, so the same seed gives the same shot) and `-only <shot>` keeps only the shot with that name, for example `-colshot -only colonna`. The texture change of 17 November 2026 was checked with it: see [../game/textures.md](../game/textures.md).
- **`ProcCheck`**: connectivity over hundreds of floors, plus an ASCII map of one room. Output: `hotel_build/proccheck.txt`, `hotel_build/procascii.txt`; with `-propstats` also `hotel_build/propstats.txt` (furniture of each kind by room type).
- Both read `-theme albergo|teatro|sanatorio|guardaroba` (forces the theme of every grid floor), `-guardaroba N` (percentage of Guardaroba floors from the third up) and `-survivallayout` (the room plans of the wave cycle, which is the game's default; without it the editor tools build the candle-cycle plan). See [../game/guardaroba.md](../game/guardaroba.md).
- **`NavMap`**: navmesh of one floor in PPM, walkable area, the component of the arrival, and the doors that separate it from the rest. Output: the `-out` file (default `hotel_build/navmap.ppm`).

### 2.6 Menu and menu music (Unity editor, edit mode)

```powershell
Unity -batchmode -projectPath unity\PryingEyes -executeMethod PryingEyes.EditorTools.MenuReview.Run
Unity -batchmode -nographics -projectPath unity\PryingEyes -executeMethod PryingEyes.EditorTools.MenuMusicReview.Run
```

- **`MenuReview`**: builds the menu in an empty scene and checks it at four sizes (1280×720, 1920×1080, 3440×1440, 1024×768): the five settings tabs (rows, hints, navigation chains, what each row writes, the reset of each tab), the three guide pages, the tips, the pause summary, the split-screen panel and the version label. It photographs the panels, so it needs graphics (no `-nographics`); about 15 s; it exits by itself and puts the settings it changed back. Output: `unity_logs/menu-review/` (the photos and `result.txt`). 2369 checks passed on 17 November 2026, on the integrated tree of 0.8.0 (2187 on the menu rework alone; the others come with the work merged beside it: the monitor button of the split-screen panel and the **Prospettiva** row that the third person puts in the Gioco tab). On 18 November 2026, with the character panels (the list, creation, deletion, *Chi gioca?*, the suspended expedition and the leave panel with its three buttons), 2790 checks passed. What it checks, in full: [../game/display-and-menu.md](../game/display-and-menu.md#verification).
- **`MenuMusicReview`**: computes the menu theme and the four menu tones, checks them (24 checks: length, finiteness, level, stereo, the loop seam, the score against its chords, the clip the game builds) and writes the WAV files and `result.txt` to `unity_logs/menu-music/`. About 25 s. It cannot say that the music sounds good: nobody has listened to it ([../game/menu-music.md](../game/menu-music.md)).

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
| `-stealthtest -gridtest` | Grid plan: roles, doors (1 to 4), frames, navmesh (all reachable except the secret room), sanctuaries, wall weapons, arenas, guards, boss, low passage of the secret room, node map. About 35 s. `-oldplan` turns it off. | `hotel_build/furtivo/griglia.txt` (with graphics also the map photos `griglia_mappa_inizio_ui.png`, `griglia_mappa_ui.png`, `griglia_minimappa_ui.png`). Exit 0 ok, 1 failed, 2 not a grid plan |
| `-stealthtest -pushtest [-bodiesclash]` | Ten flying enemies on a still player must not move it or push it under the floor. About 25 s. `-bodiesclash` brings back the old collider behaviour and the test must fail. | `hotel_build/furtivo/spinta.txt`. Exit 0 ok, 1 failed |
| `-stealthtest -holescan` (`-holeat X,Z` describes one point) | Ray-casts a 40 cm grid for points with no floor, then drops the player to -60 m and checks that `FloorDirector.FallGuard` brings them back. | `hotel_build/furtivo/buchi.txt`. Exit 0 ok, 1 failed |
| `-stealthtest -alientest` | Three aliens and the corridor at several exposures. | `hotel_build/furtivo/alieni.txt` and photos |
| `-stealthtest -temitest` | Maschera, Direttore, Infermiere and Primario at the end of the longest hall; their surprises (whistle, sedative, reinforcements, three darts). Seeds: `2961 -floor 3` (theatre), `3046 -floor 3` (sanatorium). | `hotel_build/furtivo/temi.txt`, photos `tema_<Nome>.png` and `_3q.png` |
| `-stealthtest -refshot` (with `-seed 1000`) | Visual comparison of the church: cloth fibres follow the pose, all five monk clips present. | `hotel_build/fotorealismo/` |

### 3.3 Survival, architecture and modes

| Command | What it checks | Output |
|---|---|---|
| `-batchmode -nographics -autotest -cattedraletest -nowaves -seed 1000` (`-cattedraleshots` without `-nographics` for photos in `hotel_build/cattedrale/`) | The two zones alone, on a test profile: the cathedral (plan, market, sites, gates, no waves), buying and building with the real interactions (the cathedral is rebuilt), a descent into the Ossario (gates instead of lifts, starting points and supplies), a drop picked up into the satchel, the poor box, the return by the stair (satchel banked, points to oboli), death (half the commons kept, the rest in the remains), the remains picked up, the third level with the branch boss, its trophy and the return, the save reloaded; since 18 November 2026 also the expedition that is suspended and resumed (after an area was bought: the file says where, with what and with how many points; resuming brings back the level and its seed, the satchel, the points, the perk, both weapons with their ammunition, the grenades, lures and mines, the magic and the essence, and the bought area with its doors up; the file is consumed and the resume counts neither a descent nor a trip) and the one that is abandoned (the satchel counts as a death, half of the commons are kept, the file goes to `runs/consumed`). 72 checks. | Unity log only; exit 0 ok, 1 failed |
| `-batchmode -nographics -autotest -chartest` | The characters in the real game, on the test profile (the folder `characters-prova` is emptied first, and the test refuses to run on any other folder): the test character at level 1, a new one (name tidied, the same name with other capitals refused, an empty name and one without letters refused), the cathedral of each character is its own (building and depositing for one does not touch the other), experience raises a level once, nothing changes when the files are read again, the file carries its format and version, a field and a work this version does not know survive a save, the extras of a future system survive, deleting a character moves its file to `deleted/`. 23 checks. | Unity log only; exit 0 ok, 1 failed |
| `-batchmode -nographics -autotest -desertotest -seed 1000` (`-desertshots` without `-nographics` for the photos in `hotel_build/deserto/` and the contrast checks of the halls; `-desertonly scalinata,torre,...` runs only those phases after the arrival, the others are `cielo`, `cattedrale`, `opere`, `cappella`, `oggetti`, `suono`, `boss`, `catacomba`, `notte`, `morte`, `lontano`, `caduta`, `foto`; `soak` (memory growth over three rounds of four far places), `varieta` (one place of every kind and state, entered on foot and walked to its farthest room, plus a second tower and catacomb), `prestazioni` (the cost of a frame at 1920x1080 in seven places, then on the sand of each of the six regions, at a night camp and in a sandstorm; needs a graphics card), `esterni` and `orizzonte` (the outsides of the places and their far skins, with photos), `cose` (the props on the sand of the six regions) and `vita` (animals, vultures and bats, camps, landmarks, the sounds and a sandstorm) are long and run only by name (so are `costi`, `orme`, `creste`, `salina`, `cammino`, `paesaggio`, `nuvole`, `rilievi`, `look` and `suolo`: what each part of the picture costs, the footprints, the smoking crests, the brine lakes, a walk of 309 m with every frame measured, and photographs of the landscape, of the sky with three covers of cloud, of the buttes, the light and the ground; the list is in [../game/the-desert.md](../game/the-desert.md), "How to check"), one or several separated by commas; `-desertnavlog` logs every navmesh tile; `-desertdiag` explains a hall that looks wrong) | The desert mode alone, from the cathedral, on a test profile ([../game/the-desert.md](../game/the-desert.md)): the mode, the camera (solid colour, 1,500 m far plane) and the cathedral with its armoury; the stair, the two braziers burning at its head on their tripods, the banners of the stair and of the landmark tower (a mast on its roof and one hanging on each long wall: the wind moves them and never pushes a wall banner into the wall) and the corner turrets of that tower (its facade rises over the battlements), the sand and the navmesh (tiles kept, none inside the sanctuary), the props of the sand sent to the card on the surface (and the heavy ones, rocks, cacti and bushes, far away on the reduced mesh) and not from a catacomb; the clock and the sky at five hours; the armoury at twice the price; the two desert-only works built with their resources, a book bought and the Library renewed, a weapon consecrated; the sand steps, the wind and the compass words; the welcome chapel built, furnished and entered on foot; coins, crates and a free weapon taken once; the landmark tower climbed on foot to its first floor (the navmesh joins the floors), its boss and the trophy banked; the nearest catacomb entered and walked down to its boss on foot, the boss and its trophy; wanderers on the open sand at night; loot around the player; death and rebirth at the top of the stair; the sand, the navmesh and a building at 5, 9 and 17 km and the edge of the world at 18 km; the fall net; no frame over 1.2 s (loading excluded) and no error in the log. With a window it also checks that the chapel, every level of the catacomb and the two boss halls are not a fog or a black room (the contrast of the image). 127 checks, about three minutes without a window. | Unity log only (and the photos with `-desertshots`); exit 0 ok, 1 failed |
| `-batchmode -nographics -autotest -survivaltest -seed 1000` | Wave cycle: first wave after 8 s with 20 enemies and the alive cap; enemies spawn only in open areas, never closer than 3 m, always attacking; the score rises by the wave prize once plus the kills and the medals (`Medals.Points`); three waves open the lift; going down does not cancel the wave, death (bleeding out) does; the fifth is the Processione. The result does not depend on what the game draws at random. Two things used to make it fail on most runs (10 of 14 on 17 November 2026, between 84/87 and 86/87): the Prova of waves 2 and 4 (`prova <tipo> obiettivo N` in the log) was drawn at random and Furia is completed by the test's mass kills (+520 or +640 points and an ammo box: "premio una volta sola" and "nessun regalo" failed), and the burst of a killed Appestato wounds a monk beside it for +10 points without killing it. The test now pins the Prova to Testa (`Prove.pinned`, in the style of `Drops.disabled`: the test's kills are never headshots), adds the points paid for non-lethal hits (`Punti.paidForHits`) to the expected score, and forces the burst case between waves 3 and 4 on every run (`BurstPays`: an Appestato killed 3 m from a monk, +10 for the hit and +60 for each kill). 91 checks. | Unity log only; exit 0 ok, 1 failed |
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
| `-nofire -noenemies -magiatest [-magiabooks] [-magiashots] [-magiaside] [-magiazoom] [-vfxdump]` | The secret book room (one per floor, the wall used with F, not crossable standing, crossable crouched, no enemy path, the floor's book), both grimoires (the real plate and its slots, the book inside its area, time running, rules, prices, all 38 nodes; `-magiabooks` stops after this part, 44 checks, about two minutes), the seven spells and seven seals on a rifle and a pistol (the free hand replaces the wrapped glove and gives it back, effects arrive, elbow, wrist and forearm within human ranges, the weapon wears the seal's skin and the sealed shots draw muzzle, tracer and impact) and the left-hand throw. `-sigilliview [-sigilliweapons a,b] [-sigillinodes 14,17] [-sigillishots N]` photographs only the seals (the gesture under the weapon, the weapon from the side and from below, shots at a wall, one in slow motion); `-magiaside` adds side and top views of the arms and a close-up of the left wrist, `-magiazoom` close-ups of the left hand, `-vfxdump` the effect textures; `-armview [-armids a,b] [-presavecchia]` photographs only the rest and aiming pose of every weapon and checks both wrists at rest against the human joint ranges (with `-magiazoom` also a close-up of each hand); `-reloadview [-armids a,b]` photographs the reloads done by the left hand (empty and tactical) and the manual cycle after them. See [arms-and-magic.md](../game/arms-and-magic.md). | `hotel_build/magia/` (`magia.txt`, photos) |
| `-barreltest [-barrelshots]` (19) | Holy-oil barrels chain-explode; wave enemies rise from the floor. | `unity_logs/botti/` |
| `-powertest [-powershots]` (16) | Floor power: emergency lights at 40 %, breaker in the farthest area; the lever turns the hotel's power back on (once per wave); perks, traps and altar need power. `-nopower` starts with power on. | `unity_logs/corrente/` |
| `-minetest [-mineshots]` (12) | Reliquary mine (key 5): placed 1 m ahead, armed in 1.2 s, explodes when an enemy comes near, not the player. | `unity_logs/mine/` |
| `-batchmode -autotest -nofire -noenemies -maptest [-mapshots]` (23) | The map of the floor ([map.md](../game/map.md)): the minimap and the big sheet exist and open and close (`M`, `N`); the start room is seen and the rooms behind its doors are rumors; every room entered becomes seen, the secret one only when entered; wall weapons, sealed doors and blood show up with their names, prices and seals; every part that should be visible has a material (uGUI never builds a culled graphic: a legend or a compass that stays empty is caught here); the cost of a frame (limits 2.5 ms and 6 kB for the minimap, 3.5 ms and 8 kB for the big sheet; measured about 0.13 and 0.22 ms and 0 bytes), of a full repaint (limit 600 ms, measured 5 ms) and of the main thread building the textures (limit 250 ms, measured 4 ms: the pixels are painted on a worker thread); one map per seat in split screen. `-mapshots` photographs the HUD. Seeds: `-seed 2995 -floor 3` (hotel), `2961 -floor 3` (theatre), `3046 -floor 3` (sanatorium); with `-catacombe` the same seeds are catacomb levels (the subtitle names the level, the legend says `Cancello`); with `-cattedrale -floor 0` the test only checks that the cathedral has no map. About 25 s. | `unity_logs/mappa/` (photos `01` to `05`) |
| `-ragdolltest [-ragdollshots]` (16) | Dead monks and abbots become ragdolls pushed by the killing hit; they stop and sink; at most 10 at once. `-noragdoll` compares. | `unity_logs/ragdoll/` |
| `-jellytest [-jellyshots]` | The flying jellyfish in the real game: skeleton, bell pulse, ropes, gather, thrust and contact effects, hit, death, co-op replica, cost of one animation step. Needs `-autotest -nofire -noenemies`. See [medusa.md](../game/medusa.md). | `unity_logs/jelly/` |
| `-padtest -nowaves` (83) | The controller in the menus and the normal game, without `-autotest` (the menu must exist; usually `-batchmode`): the XInput to menu translation, the automatic search for a controller with a fake XInput (`PadControls.TestPoll`), the real menu driven with a fake state (selection, frame, the five settings tabs that open on selection, sliders and choice rows, back to the button of origin, text field, first command with nothing selected), and in the game A on *Gioca* not jumping, Start and B and the pause. With `-padshots FOLDER` (a real window, plus `-menureview` so the saved display is not applied) it saves screenshots of the menus instead. | the log |
| `-splittest` (79) | The parts of the split screen that need no second player: the layout of the parts for 2 to 4 players (also over 1 to 4 monitors: who stands on which monitor and in which part of it), controller assignment, the field-of-view cap, the XInput mapping, and the real player driven by a fake XInput state. Needs `-autotest -nofire -noenemies`. | the log (`-report FILE` keeps the result out of `hotel_build/autotest.txt`) |
| `-seatstest -splitscreen N` (82 with two players, 87 with three, 92 with four; with `-splitmonitors`: 84 for two players on two monitors, 94 for four on two or on four) | The split screen itself, in one process, from the menu or with `-autotest`, over one monitor or several (`-splitmonitors N`): see section 5.1. The bleed-out check (*senza aiuto il giocatore 2 si dissangua*) failed once in 14 runs with four players, cause unknown; when it fails it now logs a `seatstest diagnosi` line saying which condition fell. | the log; `unity_logs/seats/` with `-seatsshots` |
| `-nofire -noenemies -tptest [-tpshots]` (20) | The third-person view (needs `-autotest`; a GPU for the camera pose): first person untouched, camera 1.2 to 2.7 m behind the shoulder, own survivor with the weapon, weapon layer hidden, eye ray and camera ray on the same wall point, the flash of a seal on the weapon at the muzzle of the body's weapon, aiming closer, a wall behind limits it, the fill light of the own body, switching off restores everything. A game with no window does not draw by itself: the test makes the camera draw before each check. See [../game/third-person.md](../game/third-person.md). | the log; `unity_logs/terza-persona/` with `-tpshots` (including two dark photos with and without the fill light) |

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
- **`run_all.ps1`**: all nine co-op scenarios in sequence (the first eight took 623 seconds on 17 November 2026). Exit `0` = all pass, `1` = some failed, `2` = no check selected.

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
| `i_aspetti` | `-cooptest -cooplook custode -cooplevel 7 -seed 2961` | `-cooptest -cooplook custode -cooplevel 3` | The characters of the players: the host keeps its look, the guest that asked for the same one is given the next free look (`dottoressa`), both see the same looks and levels, and each partner's body is built with them (`CoopTest.CheckCharacters`). The other scenarios send an empty look. |

The co-op code is in `Scripts/Coop/`. Protocol, limits and what is not yet verified are in `docs/multiplayer/README.md`.

### 5.1 Split screen (one game, one window)

The split screen on one PC is not a co-op any more: it is one process, with one window (and one more per extra monitor; design, controls and limits in
[../multiplayer/split-screen.md](../multiplayer/split-screen.md)). It has two checks besides `-splittest` (section 3.4):

- **`-seatstest -splitscreen N`** (N = 2 to 4) runs inside the game, with a fake XInput state injected per player, and exits `0` only if
  every check passes. Without `-autotest` the game starts from the menu (the menu presses *N giocatori* by itself; use `-menureview -nowaves`
  so the saved display settings are not applied and no wave starts): this covers the real path and the pause. With `-autotest -nofire
  -noenemies` it starts without a menu and skips the pause. It checks structure (a body, camera, HUD, target and controller per player; cameras
  in their parts of their monitors, `Camera.targetDisplay` and `Canvas.targetDisplay` per monitor; one `AudioListener`; weapon layers; HUD
  rectangles; the map and the minimap in the HUD of every player, also of one on another monitor: shown, opened, closed and switched off), the context swap of `Seats.Enter`, each controller walking, firing and
  jumping alone, points, kills and hits per player, enemy targeting (nearest at spawn, switching to a closer player; with `-candles` also
  `Provoke` and sight), damage, going down, revive by a teammate, self-revive, bleed-out death and respawn of one player, one player's
  grimoire (only their book open, only they blocked, time running, the book inside their part, another player walking meanwhile, each
  controller driving only its own player's book), the character of each seat (its own character, look and cathedral; the loot of one seat lands only in the store of its character), the pause with Start on any controller (with several monitors also the **IN PAUSA** veil on the other monitors), and the return to one player.
  `-seatsshots` (a real window, no `-batchmode`) saves photos of the whole screen in `unity_logs/seats/`.
  `-splitmonitors N` makes the test use N monitors (without the flag a test always uses one, whatever the menu saved on this PC): if the PC has
  fewer, the extra monitors are pretend ones (cameras, canvases and HUDs are placed all the same, nothing is shown), so the placement is
  checked without a second monitor. With `-seatsshots` on a PC with N real monitors the game also checks that Unity opened a window per
  monitor and that no other program covers it, near the end it puts a window of its own process (one that takes no focus) over the window of
  an extra monitor to see that the cover is noticed and that the window is raised again with the game still running, and at every photo
  moment it asks for the photos below and waits for them.
- **`tools/coop/run_split.ps1`** runs it from the verification build and reads the result. Parameters: `-Exe` (default `Builds\Audit`),
  `-LogDir` (default `$env:TEMP\pryingeyes-split`), `-Players` (2 to 4, default 3), `-Graphics` (a 1280x720 window and the photos), `-Candles`,
  `-NoMenu`, `-Monitors` (1 to 4, default 1: passes `-splitmonitors`), `-TimeoutSec` (240). Exit `0` only if the result is `SUPERATA`, `1` if a check fails or time runs out, `3` if a copy of the same
  exe is open (with `-Graphics`, if any game is open). With `-Graphics` the window rewrites the `Screenmanager` values of the game's registry
  key: the script saves them with `reg export` and restores them at the end. It uses the GPU and occupies the screen for about 40 seconds: do
  not run it while someone plays. With `-Graphics -Monitors N` on a PC with N or more monitors the game opens its own window on each of them
  (the extra ones full screen at their native size, so tell whoever uses those screens first) and, each time the game says
  `foto-richiesta: NAME`, the script runs **`tools/coop/capture_monitors.ps1`**, which photographs every monitor from the system (the game
  cannot read what Unity shows on the other monitors: `ScreenCapture` returns the last monitor drawn, cut to the size of the game window)
  into `unity_logs/seats/NAME-monitor1.png`, `NAME-monitor2.png`, and so on, and then tells the game to go on. That script can also be run by
  hand (`-Prefix monitor` by default).

`PryingEyes.EditorTools.MenuReview.Run` (see the Verification section of [../game/display-and-menu.md](../game/display-and-menu.md#verification))
also renders the split-screen panel at four sizes (with one, two and four monitors pretended connected, so the monitor button and the line
that says how the players are divided are checked too) and checks that its texts fit.

## 6. Performance probe

```powershell
& $exe -batchmode -autotest -perftest [-perfshots] [-perftag <nome>] [-perfdebug] -seed 2961 -floor 3
& $exe -batchmode -autotest -perftest -perfprops [-perftorch] [-perfgamma 0.6]
```

- Seeds: `2961` theatre, `2995` hotel, `3046` sanatorium, all at floor 3. Run **without** `-nographics`: the probe calls `cam.Render()` into a render texture and reads one pixel to wait for the GPU.
- Measures, for the largest room of each type viewed from the main threshold: logic time, CPU render time, GPU wait, p50, p95 and p99. Output: `hotel_build/perf.txt`.
- `-perfshots`: one photo per view, the player's real view with fog, lights and post-processing: `hotel_build/perf/<etichetta>_<tema>_<stanza>.png`.
- `-perfdebug`: lists the lights in each room.
- `-perfprops`: photographs each new prop close up; `-perftorch` turns the torch on; `-perfgamma 0.6` brightens the photo (photo only, not the game). Output: which rooms the props appear in, and how many. On a Guardaroba floor (`-theme guardaroba`, or the fourth branch of the catacombs: `-catacombe -floor 10`) it photographs the 17 `gu_*` props instead, and ends a line with `SEGNAPOSTO` for a prop that is still a grey box because the kit saved in `Torre.unity` lacks it ([../game/guardaroba.md](../game/guardaroba.md#how-the-props-reach-the-game)). Not every prop is in every floor; try another seed (3068, 1000 and 3046 at floor 3 cover all 17 together).
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
