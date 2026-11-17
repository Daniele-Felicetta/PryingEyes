# Furniture, phase 5: eleven props rebuilt

Added on 17 November 2026. The request was that some of the assets were made badly and in a hurry and should be redone to a much higher,
realistic standard. No asset was named, so the ones to rebuild were chosen from evidence: the furniture the rooms place most often
(`hotel_build/propstats.txt`, 500 generated floors) that was still made of a handful of boxes and spheres (192 to 1,868 triangles). Eleven props
were rebuilt in Blender 4.5 with new tools (`modelli/hotel/arredi5_lib.py`), keep their names, their meshes' names, their roles and their
footprints, and replace the old FBX one for one. Each now also carries two lower levels of detail.

Not rebuilt: the other 51 props of the kit. The ones that would pay most next, by placements and by how crude they still are, are
`chi_stallo` (473), `chi_altare` (347), `arr_fornello` (634), `arr_lampada` (485) and `chi_fonte` (36).

## The eleven props

Front on +Z, origin at the centre of the base, like the rest of the kit. The collider is the `PropDefs` box, which did not change
(see "How the rooms use them"). Triangles are the three levels of the FBX; "before" is the FBX that was replaced; placements are counted by
`-propstats` over 500 floors (21,529 for these eleven).

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

The photos in the game's own look (kit materials, theme fog, a lamp and the player's torch), the old FBX on the left and the new on the right, are
`hotel_build/props5_1.jpg` to `props5_4.jpg`. The Blender previews are `modelli/hotel/arredi5/render/<name>.png`.

On average a floor places 40,500 triangles of these eleven props before, 340,000 if every one of them is drawn at level 0, and 95,000 at level 1
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
- n-gons are triangulated before export (Blender refuses to write tangents for them) and every prop is checked at the same footprint as the old one.

## Levels of detail

Every FBX carries three versions of each mesh: `<prop>_<role>` (level 0, the model), `<prop>_LOD1_<role>` (28 % of the triangles) and
`<prop>_LOD2_<role>` (9 %), made with Blender's Decimate (collapse). A mesh under 60 triangles has no lower levels (it disappears at a distance).
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
`A_cera`, `A_acciaio`, `A_ferro`, `A_scuro`, `A_panno`, `A_pietra`, `A_osso`, `A_oro`) and add two, in `ga.ROLE` (`arredi5_lib.py`, the Blender look) and in
the `roles` dictionary of `TorreBuilder.FillKit` (the kit material):

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
blender -b -P genera_arredi5.py -- build [names]       # arr_sedia ... chi_panca
blender -b -P genera_arredi5.py -- render [names]
blender -b -P genera_chiesa5.py -- build [names]       # chi_sarcofago, chi_statua, chi_inginocchiatoio
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

The wardrobe and the cloth are the two props worth a look in a real playtest (not done).

## Traps

- **A new role exists in two places**, `ga.ROLE` in Blender and the `roles` dictionary in `TorreBuilder.cs`. A missing one gets a warning
  ("ruolo senza materiale") and the FBX default.
- **`FillKit` rewrites every prop prefab and kit material** on every Unity run, so dozens of untouched prefabs (line endings) and materials
  (float noise) show as modified. Stage only the eleven prefabs, the FBX and the two new materials (`AGENTS.md` rule 4).
- **The decimated meshes are in the FBX but not in the preview**: `render` turns them off, or the three levels would overlap and flicker.
- **`render` after `build` in the same Blender run** is fine since the flag is set per verb, but `build` overwrites the FBX every time: copy them again.
- **Decimate eats thin parts**: at level 2 the steel uprights of the shelf break into segments and the thin tracery of the pew merges. It is below 4 % of
  the screen height, where the posts are less than a pixel, and it is the reason level 2 keeps 9 % and not 5 %.
- **Concurrent Unity runs**: other worktrees run Unity at the same time. The runs of this page were made at below-normal CPU, disk and memory priority.

## Verification

- All eleven FBX were imported and photographed in the game's look, before and after replacing them (`PropShots`, `hotel_build/props5_*.jpg`), and
  the eight hotel props again after the wardrobe's lining and the level ratios changed. No "ruolo senza materiale" warning, no compile error.
- The prefabs carry a `LODGroup` with three levels (checked in the prefab files). All eleven were photographed with each level forced (`-lod 0`, `1`,
  `2`) in the game's look: level 1 is indistinguishable from level 0 at the distances where it is used; level 2 shows darker shading on the sarcophagus's
  plinth and the pew's tracery and loses thin parts (the shelf's uprights), which is below a pixel at 15 m and more.
- The switch distances above are computed from the thresholds, the quality level's `lodBias` and the field of view; they were not measured in a playing
  game.

Not verified: any playtest, co-op, or the player build; the look of the levels' switch in motion (popping); the look under the other themes' fog. The Blender
previews and the Unity photos differ in tone mapping: the Unity photos are the reference.
