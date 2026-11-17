# Furniture, phase 5: sixteen props rebuilt

Added on 17 November 2026. The request was that some of the assets were made badly and in a hurry and should be redone to a much higher,
realistic standard. No asset was named, so the ones to rebuild were chosen from evidence: the furniture the rooms place most often
(`hotel_build/propstats.txt`, 500 generated floors) that was still made of a handful of boxes and spheres (192 to 1,868 triangles). Eleven props
were rebuilt first and, in a second round the same day, the five that were left on that list (the choir stall, the altar, the range cooker, the floor
lamp and the font): sixteen in all. They were rebuilt in Blender 4.5 with new tools (`modelli/hotel/arredi5_lib.py`), keep their names, their meshes'
names, their roles and their footprints, and replace the old FBX one for one. Each now also carries two lower levels of detail.

Not rebuilt: the other 46 props of the kit. By placements (not checked for how crude each still is) the biggest are `chi_lampadario` (3,975),
`chi_candelabro` (3,574), `arr_divano` (1,660), `chi_ceri` (1,508) and `arr_palma` (1,132).

## The sixteen props

Front on +Z, origin at the centre of the base, like the rest of the kit. The collider is the `PropDefs` box, which did not change
(see "How the rooms use them"). Triangles are the three levels of the FBX; "before" is the FBX that was replaced; placements are counted by
`-propstats` over 500 floors (23,504 for these sixteen).

| Prop | What it is now | Size (m) | Before | Level 0 / 1 / 2 | Placements |
| --- | --- | --- | --- | --- | --- |
| `arr_sedia` | Dining chair: turned front legs, rear posts that curve back into the backrest (swept profile), arched crest rail, vase-shaped splat, three turned spindles, turned stretchers, upholstered seat with crown, piping and brass studs. | 0.42 x 0.89 x 0.48 | 952 | 8,404 / 2,350 / 754 | 5,163 |
| `arr_tavolo` | Square dining table under a long draped cloth (a generated shell with folds and a hem), four turned legs under it, a brass candlestick with a burnt candle and wax drips, two place settings (plate, glass, cutlery). | 1.09 x 1.00 x 1.09 | 464 | 8,508 / 2,376 / 758 | 4,917 |
| `arr_scaffale` | Warehouse shelf: angle-iron uprights, five wooden shelves with steel edges, cross braces, and a varied load (taped cardboard boxes with labels, glass jars, tins, folded linen, books). | 1.20 x 2.08 x 0.45 | 1,232 | 6,760 / 1,884 / 610 | 2,406 |
| `arr_comodino` | Bedside table: moulded top and base, four turned tapering legs, a drawer and a door with raised panel fields, a bail pull, a knob and a keyhole plate in brass. | 0.50 x 0.55 x 0.46 | 576 | 2,566 / 702 / 224 | 1,254 |
| `arr_armadio` | Two-door wardrobe: moulded plinth and cornice, a segmental pediment, raised panels, brass hinges, knobs and escutcheons. The left door stands ajar on a red cloth lining, a hanging rail and three dark coats. | 1.35 x 2.30 x 1.04 (body 0.62 deep) | 848 | 6,056 / 1,690 / 558 | 1,144 |
| `arr_scrivania` | Writing desk: moulded top with an inset leather writing surface edged in brass wire, a pedestal of three drawers with bail pulls and a keyhole, turned legs, an inkwell, a sheet of paper, a pile of books, a candlestick. | 1.25 x 0.94 x 0.65 | 636 | 5,726 / 1,582 / 504 | 611 |
| `arr_tavolo_acciaio` | Steel work table: top with a rolled edge, a drawer with a bar handle, a lower shelf, four legs with feet. | 1.90 x 0.90 x 0.84 | 192 | 1,620 / 450 / 144 | 531 |
| `chi_panca` | Church pew: side panels with a curved profile and two raised Gothic arches, a turned finial, a rounded seat edge, a back of panel fields under a row of arched tracery, a hymn rack with books, a padded kneeler on brackets. | 2.63 x 1.08 x 0.90 | 1,868 | 8,500 / 2,378 / 760 | 2,910 |
| `chi_sarcofago` | Stone sarcophagus: moulded plinth, corner piers, a blind arcade on each long side (relief arches, colonnettes, dark niches), a medallion cross on each end, a crown moulding, a rounded lid with a ridge band and cross slid aside to show the inside: dark floor, a skull, two femurs, ribs and vertebrae. | 2.37 x 1.00 x 1.00 | 788 | 13,432 / 3,758 / 1,206 | 1,188 |
| `chi_statua` | Hooded monk on a stone plinth with a brass plate: robe with organ-pipe folds (a lathe with the radius modulated), wide sleeves and clasped hands, rope belt with a knot and two tails, a hood open at the front whose inside is a dark shell holding the single golden eye. | 0.74 x 2.11 x 0.74 | 1,316 | 11,976 / 3,351 / 1,075 | 993 |
| `chi_inginocchiatoio` | Prie-dieu: moulded plinth, side cheeks with a curved profile, a padded kneeler with piping and studs under a panel front, a back with a raised pointed arch and a brass cross, a sloped lectern with a lip, an open book. | 0.56 x 0.96 x 0.46 | 604 | 3,358 / 936 / 296 | 412 |
| `arr_fornello` | Steel range cooker: dark plinth, rounded body, hob with a rim, a control strip with six knobs (dark bodies, brass rings, pointers) and a clock, two doors with moulded window frames (glass over a dark panel) and bar handles on standoffs, six burners (drip bowl, brass crown, cap, cast-iron grate with four feet), a stock pot with its lid, a kettle, a copper saucepan and a frying pan, an upstand with a lipped top, a hood (a lofted pyramid with a rolled lip and slatted filters) with its chimney, and a pot rail with a ladle, a turner and a cleaver. | 1.74 x 2.21 x 0.95 | 1,528 | 11,486 / 3,206 / 1,040 | 634 |
| `arr_lampada` | Floor lamp in turned brass: weighted stepped base, knops and collars, a rope-twist stem (a lathe whose radius follows three helical lobes), a socket with a rotary switch, a bulb, a wire harp and a finial, an empire shade of 24 accordion pleats (a closed double sheet) with a brass ring and a three-spoke spider, and a cord with its plug lying on the floor. The shade and the bulb are in the same role (`A_paralume`) and glow together. | 0.61 x 1.48 x 0.52 | 1,256 | 8,766 / 2,440 / 782 | 485 |
| `chi_stallo` | Choir stall for three: four oak standards, each cut as one plate with a concave sweep from the arm up to the tall back post and a round poppyhead, with panel fields on every face; a base with panelled fronts, three seats, a panelled back with two pointed-arch niches and a colonnette per seat and a frieze rail, a canopy with a valance of 24 small pointed arches, a cornice, a crest with cones and a gold band, four pinnacles. | 2.82 x 2.33 x 0.89 | 1,492 | 8,958 / 2,504 / 804 | 473 |
| `chi_altare` | Stone altar: plinth and moulded base, an ashlar body with a blind arcade of five pointed arches (relief frames, dark niches, colonnettes; in the middle one the golden Eye in a medallion with sixteen rays), a cornice, a slab with consecration crosses, a velvet cloth hanging in folds from both short ends with a gold border and fringe (a closed thin shell), a missal on a cushion, a chalice and a paten, and a monstrance with the Eye and twenty rays. | 2.47 x 1.35 x 1.00 | 1,152 | 11,758 / 3,286 / 1,052 | 347 |
| `chi_fonte` | Octagonal baptismal font: stepped plinth with a moulded base, a pier with eight colonnettes and a band, a capital, a lofted bowl with a rim and eight quatrefoil reliefs, black water, and a pyramidal oak lid (concave faces, eight corner ribs, a brass band, rods and a finial) hinged at the back and propped open at 30 degrees by a brass stay. | 0.90 x 1.47 x 0.92 | 470 | 5,648 / 1,712 / 924 | 36 |

The photos in the game's own look (kit materials, theme fog, a lamp and the player's torch), the old FBX on the left and the new on the right, are
`hotel_build/props5_1.jpg` to `props5_4.jpg` for the first eleven and `props5_6.jpg` and `props5_7.jpg` for the second round; `props5_5.jpg` is the pew
in a photo of the hotel's nave taken by the performance probe (see "Verification"), the old build on the left and the new one on the right. The Blender
previews are `modelli/hotel/arredi5/render/<name>.png`.

On average a floor places 45,900 triangles of these sixteen props before, 380,000 if every one of them is drawn at level 0, and 106,000 at level 1
(counted from the placements above; a floor is never drawn all at once).

## How they are modelled

`modelli/hotel/arredi5_lib.py` is the shared toolbox; `genera_arredi5.py` (the eight hotel and pew props) and `genera_chiesa5.py` (the three church
props) use it. Still the same pipeline as the other sets: one mesh per material role, UVs in metres, `Asset5` is an `Asset` with a different build step.

- **Profiles swept along a path** (`sweep`): the mouldings of plinths, cornices and panel frames are 2D profiles (`crown_profile`, `base_profile`,
  `mould_bead`, `mould_ogee`) swept around a rectangle with mitred corners; the curved rear posts of the chair are one profile swept along a spline
  and tapered. Rooms read as built when the light catches a moulding, and a box with a bevel cannot do that.
- **Turned parts** are lathes with several rings and a few beads and coves, not a cylinder with a sphere on top.
- **Upholstery** (`cushion`): a grid with a crown, a sag and a slight wrinkle, a piping tube around the edge and brass studs.
- **A draped cloth** (`tablecloth`): a thin shell with folds that grow towards the hem, softened corners and a hem band.
- **Hardware** (`bail_pull`, `knob`, `keyhole_plate`, `hinge`, `stud_row`) is small and in the brass role, so it shines apart from the wood.
- **Grain follows the piece** (`grain_uv`): the wood textures have the grain along U. The box projection of the old props put it across half of
  the pieces. Each connected piece of a mesh now gets its UVs rotated by 90 degrees (a rotation, never a mirror) so that the grain runs along its long
  axis, and a squat piece is decided face by face.
- **Stone** (`stone`, `weather`, `ring_frame`, in `genera_chiesa5.py`): blocks are grids with rounded edges whose vertices are displaced by a slow
  noise that depends only on the position, so coincident vertices move together and the faces are never flat; arcade frames are extruded
  between two Gothic outlines; the statue's robe is a lathe whose radius is modulated by two incommensurate cosines that grow towards the hem.
- **Twisted and pleated lathes** (`lathe_fn`, in `arredi5_lib.py`): a lathe whose radius is a function of angle and height. The lamp's stem is three helical
  lobes whose phase follows the height (a rope twist, 24 segments, a row every 12.5 mm); the shade has 24 accordion pleats made with a triangle wave and four
  segments per pleat, so every peak and every valley lands on a vertex and stays sharp when the normals are split (`sharpen`), and its inner sheet is the same
  function at a radius 3.5 mm smaller, joined at both rims into one closed shell.
- **Lofts** (`g4.loft`: rings of the same number of points): the range's hood and chimney, the font's bowl and the font's lid. The octagon helper puts a flat
  side on +Y and takes the apothem, so the font's footprint is exactly the 0.9 m box (apothem 0.45).
- **Closed thin shells on a grid** (`shell`, in `genera_chiesa5.py`): the altar's velvet and its gold border follow one function of position (folds that grow
  towards the hem); an outer and an inner sheet and closed edges make a cloth with a thickness, so the normals can be recalculated like any solid.
- **Pointed-arch tracery** (`gothic_pts`, `ring_frame`, a dark `poly_prism` for the niche) for the altar's arcade and the stall's niches; the stall's valance is
  one polygon (a row of 24 small pointed arches hanging from the canopy) extruded 26 mm, which costs a tenth of 24 framed arches.
- **Cut plates**: each standard of the stall is one polygon (the seat's side, the concave sweep of the arm as a spline, the tall back post) extruded 0.1 m with
  its edges bevelled (`prism_bevel`), with a panel field built in the XZ plane and turned onto each face.
- n-gons are triangulated before export (Blender refuses to write tangents for them) and every prop is checked at the same footprint as the old one.

## Levels of detail

Every FBX carries three versions of each mesh: `<prop>_<role>` (level 0, the model), `<prop>_LOD1_<role>` (28 % of the triangles) and
`<prop>_LOD2_<role>` (9 %), made with Blender's Decimate (collapse). A mesh under 60 triangles has no lower levels (it disappears at a distance), unless
its role is in the asset's `KEEP_LOD` set: then it is copied whole to both levels (the font's water, 28 triangles, which would otherwise vanish and show the
stone floor of the bowl). An asset can also set its own `LODS`: the font keeps 30 % and 16 % because its eight colonnettes break into spikes at 9 %.
The names do not end in `_LODn` on purpose: Unity would group such names by itself with its own thresholds. `TorreBuilder.AddLods` (called by
`MakePropPrefab`) does it instead: one `LODGroup` on the prefab root, no cross-fade, screen heights **0.25** (level 0 above), **0.085** (level 1) and
**0.013** (level 2; below it the prop is culled). Unity divides the distance by the `lodBias` of the quality level, and the game runs on **Ultra**
(`QualitySettings.asset`, `m_CurrentQuality: 5`, `lodBias` 2) with a 75 degree field of view by default (`GameSettings`, 60 to 105): a prop one metre tall stays
at level 0 up to about 5 m, is level 1 up to about 15 m, level 2 up to about 100 m and is culled beyond; the pew, 2.6 m wide, switches at about 13, 38 and
250 m. The first thresholds (0.12, 0.04, 0.012) kept almost every prop of a 10 m room at level 0 under that bias and were tightened. A narrower field of
view or a lower quality level moves the switches (Very Low has a `lodBias` of 0.3: three times closer than 1). An FBX without the lower meshes (every
other prop) gets no group.

The shadow passes of the pooled lights use the same selection (distance from the player's camera), so the shadows of a crowded room are mostly drawn
from level 1.

## Materials

The props reuse the existing roles (`A_noce`, `A_legno`, `A_ottone`, `A_velluto`, `A_tessuto`, `A_lenzuolo`, `A_cuoio`, `A_vetro`, `A_porcellana`,
`A_cera`, `A_acciaio`, `A_ferro`, `A_scuro`, `A_panno`, `A_pietra`, `A_osso`, `A_oro`, `A_paralume`, `A_lacca`) and add two, in `ga.ROLE` (`arredi5_lib.py`,
the Blender look) and in the `roles` dictionary of `TorreBuilder.FillKit` (the kit material). The second round added none:

| Role | Kit material | Used for |
| --- | --- | --- |
| `A_cartone` | `kit_cartone` (flat, 0.58 / 0.44 / 0.28, smoothness 0.10) | The cardboard boxes of the shelf |
| `A_carta` | `kit_carta` (flat, 0.74 / 0.71 / 0.62, smoothness 0.08) | The labels, the sheet on the desk, the pages of the books |

The wardrobe's lining uses `A_tessuto` (red velvet, the same as the pew's kneeler); it used `A_fondo` (green felt) in a first version and read as
a pale blue-grey in the player's light.

## Making them again

Blender 4.5 from `modelli/hotel/`, the verbs and the names as separate arguments (none: all of the file's props). `build` writes the FBX into
`modelli/hotel/arredi5/` (git-ignored) with the three levels; `render` writes the previews (CPU Cycles) with level 0 only.

```powershell
blender -b -P genera_arredi5.py -- build [names]       # arr_sedia ... chi_panca, arr_lampada, arr_fornello
blender -b -P genera_arredi5.py -- render [names]
blender -b -P genera_chiesa5.py -- build [names]       # chi_sarcofago, chi_statua, chi_inginocchiatoio, chi_altare, chi_fonte, chi_stallo
blender -b -P genera_chiesa5.py -- render [names]
Copy-Item modelli\hotel\arredi5\arr_*.fbx unity\PryingEyes\Assets\_PryingEyes\Models\Arredi\
Copy-Item modelli\hotel\arredi5\chi_*.fbx unity\PryingEyes\Assets\_PryingEyes\Models\Chiesa\
```

Environment variables for looking: `ARREDI_LOD=1|2` with `render` shows only that level; `ARREDI_VIEW=x,y,z,tx,ty,tz,focal` moves the camera; `ARREDI_NOLOD=1`
with `build` leaves the lower levels out; `ARREDI_FLAT=1` uses flat colours instead of the game's textures in the previews; `ARREDI_GPU=1` renders on the GPU.

Then Unity: `TorreBuilder.FillKit` imports the FBX, rebuilds every prop prefab (with the `LODGroup`) and the kit materials. To see the props as the
player sees them, see `PropShots` in [guardaroba.md](guardaroba.md#making-them-again); the photos here were taken with
`-theme albergo -torch -props <names>`, `-tag prima` before copying the FBX and `-tag dopo` after. `-lod N` (0, 1 or 2) forces a level of detail in the
props that have a group, so the far levels can be looked at from close up in the game's own look; `-dist` scales the camera's distance.

## How the rooms use them

Nothing in the rooms changed. `PropDefs` has the same boxes, so the layouts of every seed are the same as before (the plan generator was not
touched) and the collider is the old box. What is visible beyond the box, and solid to nothing:

- `arr_armadio`: the ajar door sticks out about 0.4 m in front of the 0.62 m body.
- `arr_tavolo`: the cloth is 1.09 m wide against the 0.95 m box; chairs placed 0.78 m from the centre (`RoomKitsAlbergo`) stand 3 cm from the cloth,
  and a chair turned by the random yaw of up to 30 degrees can touch it by a few centimetres.
- `arr_scrivania`: the candlestick makes it 0.94 m tall against the 0.76 m box.
- `chi_sarcofago`: the lid overhangs the body by 3 cm and the opening shows the bones; the lid is not a separate object and does not move.
- `arr_lampada`: the shade is 0.52 m across against the 0.42 m box (the old one was as wide); the cord and the plug lie on the floor up to 0.34 m from the
  axis, 0.13 m beyond the box on the +X side. The pooled light of the room sits 1.25 m above the lamp's origin (`RoomKitsAlbergo.FloorLampProp`) and
  `BreakableLamps` hangs a 0.17 m sphere on that light's parent, the prop root: both are inside the shade and the bulb.
- `arr_fornello`: knobs and handles stand 0.08 m in front of the body, so the mesh is 0.95 m deep against the 0.85 m box; the kitchens place it 0.45 m from the
  far wall. The hood's lower edge is 1.56 m up, as it was, and the box is solid up to 2.2 m, so nobody walks under it.
- `chi_altare`: the monstrance reaches 1.35 m against the 1.05 m box and the cloth hangs 3 cm beyond each end. The annexes and the hub place it at 0.8 to 1.0
  of its size.
- `chi_fonte`: the lid is propped open at 30 degrees and reaches 1.47 m against the 1.1 m box (inside the 0.9 m footprint). The rooms scale the font by
  1.3 (the spaces), 1.5 (the garden fountain) and 2.4 (the centre of the winter garden), where the open lid is about 3.5 m up. `CattedraleTest` only looks it
  up by name.
- `chi_stallo`: the pinnacles reach 2.33 m against the 2.3 m box.

The wardrobe, the cloth and the font's open lid are the three props worth a look in a real playtest (not done).

## Traps

- **A new role exists in two places**, `ga.ROLE` in Blender and the `roles` dictionary in `TorreBuilder.cs`. A missing one gets a warning
  ("ruolo senza materiale") and the FBX default.
- **`FillKit` rewrites every prop prefab and kit material** on every Unity run, so dozens of untouched prefabs (line endings) and materials
  (float noise) show as modified. Stage only the prefabs of the props you rebuilt, their FBX and any new material (`AGENTS.md` rule 4).
- **The decimated meshes are in the FBX but not in the preview**: `render` turns them off, or the three levels would overlap and flicker.
- **`render` after `build` in the same Blender run** is fine since the flag is set per verb, but `build` overwrites the FBX every time: copy them again.
- **Decimate eats thin parts**: at level 2 the steel uprights of the shelf break into segments and the thin tracery of the pew merges. It is below 4 % of
  the screen height, where the posts are less than a pixel, and it is the reason level 2 keeps 9 % and not 5 %.
- **Concurrent Unity runs**: other worktrees run Unity at the same time. The runs of this page were made at below-normal CPU, disk and memory priority.
- **A broken floor lamp probably keeps glowing** (seen in the code, never tried in the game): `BreakableLamp.Break` swaps a material only when its name
  contains "paralume", "lampadina" or "neon", while `A_paralume` and `A_lampadina` map to the existing `LampShade` and `Bulb` assets, so the shade of a
  shot floor lamp is not swapped, and `LampTest` does not look at the prop's meshes. The old lamp had the same shade role, so this is not new. It is also why
  the bulb is in `A_paralume`: in `A_lampadina` it would stay at emission 4 inside a shade that goes dark.
- **A hand-built outline that crosses itself is a bow tie**: the first valance of the stall walked each arch's two arcs in the wrong order (the arc centred on
  the left springing point is the one on the right) and came out as a row of dark figure-eights. Render any outline you build by hand.
- **A bevel on a tiny box costs 44 triangles instead of 12** (`bx` with a radius): the altar's 36 rays and its crosses, a few millimetres wide, cost 1,400
  triangles until they were made flat.
- **`part()` frees its temporary mesh** (`g3.merge` frees the source), so a motif that is placed eight times (the font's reliefs) is built eight times, with
  `part(dst, builder, M)` in a loop, never one temporary merged repeatedly.
- **Anything that must meet a moved part is moved with the same matrix**: the font's stay is drawn from the rim to `Mlid @ point`, the lid's own matrix, so that
  changing the opening angle keeps it attached.
- **Decimate turns thin columns into spikes**: at 9 % the font's eight colonnettes became dark spikes, so the font keeps 16 % at level 2 (see "Levels of detail").

## Verification

- The first eleven FBX were imported and photographed in the game's look, before and after replacing them (`PropShots`, `hotel_build/props5_1.jpg` to
  `props5_4.jpg`), and the eight hotel props again after the wardrobe's lining and the level ratios changed. No "ruolo senza materiale" warning, no compile
  error.
- The prefabs carry a `LODGroup` with three levels (checked in the prefab files). All eleven were photographed with each level forced (`-lod 0`, `1`,
  `2`) in the game's look: level 1 is indistinguishable from level 0 at the distances where it is used; level 2 shows darker shading on the sarcophagus's
  plinth and the pew's tracery and loses thin parts (the shelf's uprights), which is below a pixel at 15 m and more.
- **Second round (the five)**: imported and photographed in the game's look before and after replacing them (`hotel_build/props5_6.jpg` and `props5_7.jpg`),
  and with each level forced (`-lod 1`, `2`). Level 1 reads like level 0; level 2 flattens the stall's niches and the font's reliefs, below about 46 px of
  screen height (0.085 over the `lodBias` of 2, at 1080p). No "ruolo senza materiale" warning and no compile error in the four Unity runs; each prefab has the
  `LODGroup` with the three thresholds and as many renderers as the FBX has meshes (10, 13, 18, 15 and 9). The Blender previews and the Unity photos agree on
  every part; the lid of the font is cut by the frame of the standard photo (the camera is placed from the 1.1 m box), so `props5_7.jpg` uses `-dist 1.35`.
- The player build passed the navigation smoke of `tools/ship.ps1` (the bot, seed 1000, two floors) and ran the performance probe below with no error in the log.
- **Frame time in the player, first round (the eleven props)** (`-autotest -perftest -perfshots`, 1920x1080, RTX 5060 Ti, floor 3 of seeds 2995, 2961 and 3046): the same plans were timed in the
  new build and in a copy of the game build the maintainer had been playing (the same code plus the rebuilt flying jellyfish, with its own models, textures
  and scripts, and the old props; the probe clears the enemies before every view). Plans, rooms and lights are identical in both (9 rooms, 228, 156 and
  175 lights; the older `g4_*` blocks of `hotel_build/perf.txt` are from the previous layout and cannot be compared). 16 views, one run per build and theme
  (`ab_old_*` and `ab_new_*` in `hotel_build/perf.txt`). The median frame (p50, mean over the views) is 5.15 to 5.18 ms in the hotel, 4.86 to 4.70 ms in the
  theatre and 4.76 to 4.80 ms in the sanatorium (old build to new build): **-0.03 ms** on average over the 16 views with a standard error of 0.04 ms, so no
  measurable difference. The mean frame is 0.36 ms lower in the new build, but only because the old runs met more one-frame hitches (100 to 260 ms in five
  views of the old runs and one of the new; the hitch of the hotel's atrium is in both) which the props do not cause. CPU render time -0.16 ms, GPU wait -0.03 ms.
- What the probe shows and what it does not: it looks at the biggest room of each kind from its main doorway, and in the current layouts those are halls up
  to 45 x 40 m, so the props are a small part of each picture (the hotel's nave shows its pews, most other views none close up). It shows that the
  new props and their level-of-detail groups did not slow these views down; it does not time a room packed with them. Differences under 0.1 ms are noise: one
  run per build and theme, the sanatorium run of the new build started with the CPU 94 % busy with another session's work, and one run of the old build was
  repeated because another session started a game. To repeat it, run the probe (see [testing.md](../engineering/testing.md)) from the Audit build and from
  a copy of an older build in a disposable folder inside the checkout (`TestPaths` finds `hotel_build/` by walking up from the data folder), the same `-seed`
  and `-floor`, one after the other with no other game running, and compare the p50 of each view.
- The photos of the same views are `hotel_build/perf/ab_old_*.png` and `ab_new_*.png` (git-ignored; they differ mostly by the muzzle flash of the bot's
  shots). The pew of the nave in the game's own light and at playing distance, old and new, is `hotel_build/props5_5.jpg`: the curved side, the raised arches
  and the tracery of the back that the Blender previews show are there.
- The switch distances above are computed from the thresholds, the quality level's `lodBias` and the field of view; they were not measured in a playing
  game.

Not verified: any playtest by a person, co-op; the look of the levels' switch in motion (popping); the look under the other themes' fog; the cost of a room packed
with these props; the font's open lid and the kitchens in a real game; whether a shot floor lamp goes dark (probably not, see "Traps"). The Blender previews and
the Unity photos differ in tone mapping: the Unity photos are the reference.
