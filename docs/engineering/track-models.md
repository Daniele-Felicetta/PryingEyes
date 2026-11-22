# Track models: the rider and the bikes drawn in Blender

The track mode ([Track racing](../game/track-mode.md)) first drew its bikes and its rider by code: lofted shapes with a rounded section, a tube for every limb. They were judged, by the maintainer, to look "made
of Roblox" and not like a modern game. This page is about the way out: the models are drawn in **Blender** (a script for every model, headless, no hand editing), exported as FBX into
`unity/PryingEyes/Assets/_PryingEyes/Resources/Moto/` and read by the game at run time. They are shaped and shaded better than the code-built ones; they are **not photographic**, and the page says what is
and is not measured.

## What exists

| Model | Generator | File | Read by |
|---|---|---|---|
| the rider (torso, hump, helmet in five parts, two arms with an elbow and a shoulder protector, two legs with a knee cup and a knee slider, two gloves, two boots with a sole, straps and an ankle shell) | `modelli/moto/rider.py` (shapes), `modelli/moto/genera_moto.py` (materials, previews, export) | `Resources/Moto/pilota.fbx` | `Scripts/Pista/PistaRiderModel.cs` |
| the bikes: superbike, supersport (the shell of the superbike 3% narrower) and naked roadster | `modelli/moto/bike.py` (chassis and bodywork), `modelli/moto/genera_moto.py` (previews, export) | `Resources/Moto/moto0.fbx`, `moto1.fbx`, `moto2.fbx` | `Scripts/Pista/PistaBikeModel.cs` |

Without a file (a build that lacks it) `PistaBikeMesh` keeps drawing that bike or that rider by code, so nothing depends on the models being there (`PistaRiderModel.Disabled` and `PistaBikeModel.Disabled` force the old
ones for a comparison).

## Making and exporting

From `modelli/moto`, with Blender 4.5 (one at a time: each one wants every core; none while a timed test runs):

```powershell
blender -b -P genera_moto.py -- pilota            # the rider on a rough mock of the bike, five views -> render/pilota_*.png (git-ignored)
blender -b -P genera_moto.py -- export_pilota     # the FBX into Resources/Moto (the .blend of the run is in work/, git-ignored)
blender -b -P genera_moto.py -- moto 0 lato tre_quarti dietro_alto   # bike 0 (style 0, 1 or 2) with its rider, the views named -> render/moto0_*.png
blender -b -P genera_moto.py -- export_moto 0     # the FBX of that style into Resources/Moto
```

After an export the game has to import the file again: a full build (`BuildPlayer.Windows`, not `-scriptsOnly`) or the editor does it; a change of the generator that is not followed by an import is not seen.

`geom.py` holds what every model shares: the axes, the constants of the bike, the tube (`tube`), the loft (`loft3d`), smoothing, decimation, the FBX export (which also rewrites the absolute path of the
`.blend` that the exporter writes into the file: the repository is public, `modelli/personaggi/scrub_fbx.py`) and the occlusion baker (`bake_ao`: the bikes and the rider use it). The weapons' hard-surface kit
(`modelli/armi/kit.py`: box, prism, lathe, loft, sweep, rod, boolean cuts) is loaded by `geom.load_armikit()` with its weapon offset switched off, for the models that need hard edges.

**Axes.** The author's axes are x right, y forward, z up, in metres, the bike standing on z = 0 with the middle of its wheelbase at y = 0. The FBX goes out with forward -Z and up Y. Unity's importer then
leaves the conversion in the nodes (a turn of 90 degrees about x) and the vertices in the frame of Blender, x right, y forward, **z down**, so a shader that reads the position of a pixel would see the model on its
side. `Editor/PistaModelPostprocessor.cs` turns the vertices and the normals instead and leaves the nodes with a position only: the mesh of every piece is in the frame of the game (x right, y up, z forward)
round the origin of its node, and the Unity position of a point (x, y, z) of the Blender file is (x, z, y). It also sets the importer (scale 1, readable meshes, imported normals, no tangents, no animation, no
camera or light), and its `GetVersion()` is raised when it changes what it does, so that Unity imports the models again. It only straightens the nodes that hold a mesh: a file must not have an empty as the parent of
its pieces (see the traps).

**What the game and the file agree on.** The constants live twice, in `modelli/moto/geom.py` and in `Scripts/Pista/PistaBikeMesh.cs`, and must say the same thing:

| | value | where it matters |
|---|---|---|
| wheel radius, wheelbase | 0.31 m, 1.40 m | the axles at y = +-0.70, z = 0.31 |
| rake, trail of the steering axis | 24 degrees, 0.64 m from the front axle to the head | the head at (0, 0.440, 0.895) |
| swing arm pivot | (0, -0.30, 0.45) | the rear swing |
| grips | right (0.30, 0.40, 0.905) with clip-ons, (0.30, 0.34, 1.00) on the naked bike | the hands of the rider |
| foot pegs | (+-0.24, -0.22, 0.40) | the feet of the rider |
| hip of the rider | (0, -0.34, 0.945) in the file, `PistaBikeRig.hipBase` in the game | the torso and the legs |
| number plates | nose (0, 0.80, 0.80), tail sides (+-0.1135, -0.64, 0.89), tail end (0, -0.99, 0.865) (naked bike: nose plate at (0, 0.68, 0.99)) | built by code in `PistaBikeMesh`, so the tail of the model is shaped around them |

## The bikes

A bike is one **Gun** of the weapons' kit (primitives drawn in a part and a role) built by `bike.py`: the chassis that the three styles share (wheels, fork, swing arm, frame, engine, exhaust) and the bodywork of
each style. A **part** is an object of the file with its origin at the pivot of the node of the game's rig that moves it, a **role** is the name of a material, which the game turns into one of its own. The parts
leave the file flat (no parent, no empty): `stage_export_moto` unparents them before the export.

| Part | Pivot | Moves with | Holds |
|---|---|---|---|
| `body` | the origin | the whole bike (it leans and pitches) | frame, engine, exhaust, fairing and belly, tank, seat, seat cowl, lights, mirrors |
| `screen` | the origin | the body; hidden in the first-person view | the windscreen (a flyscreen on the naked bike) |
| `swing` | (0, -0.30, 0.45) | the swing arm (it turns a little with the shock) | the arm, its cross members, the chain |
| `rear` | the rear axle | the rear wheel (it spins) | tyre, rim and spokes, hub, disc, sprocket |
| `steer` | the steering head | the fork, the bars, the front mudguard (they turn); on the naked bike also the headlamp | the legs, the yokes, the bars, the calipers, the mudguard |
| `front` | the front axle | the front wheel (it spins, rises and falls with the fork) | tyre, rim and spokes, hub, two discs |

Roles: `paint` and `paint2` (the livery; `PistaBikeModel` sets their `_Part` to 0 and 1), `frame`, `metal`, `steel`, `titan`, `engine`, `black`, `carbon`, `seat`, `glass` (the mirrors), `smoke` (the windscreen), `gold`,
`caliper`, `grip`, `chain`, `radiator`, `tyre` (`_Part` 9), `rim` (the rim, the spokes and the hubs), `discF` and `discR` (the brake discs), `lens`, `taillight`. `PistaBikeModel.Role` maps `paint`, `paint2`, `lens` and
`taillight` to the livery's own materials, `rim` to the finish of the livery (`Livery.rim`: silver, black, gold, bronze or white, one material per bike, made in `PistaBikeMesh.Build`), the rest to the ones
`PistaBikeMesh` already makes for the code bike (frame, metal, steel, titan, seat, glass, smoke, gold, tyre) and, for the few that are new, to materials made in `PistaBikeModel.Shared`; a role nobody knows is magenta.
The plates and their backs stay built by code (they carry the picture of the number of the livery), and the rider is the other model.

**Drilled discs.** `discF` and `discR` are plain parts with `_Part` 31: `PistaPaint.shader` cuts two staggered rows of 22 holes out of the band between `_R0` and `_R1` metres from the axle (0.112 to 0.165 in front,
0.082 to 0.130 behind: the radii of the lathes in `wheel()`, which must say the same as `PistaBikeModel.Shared`) with `clip`, so the holes show the rim and the caliper behind them; the track the pads sweep is a little
duller. The shadow and the depth passes keep the whole disc, which cannot be told at that size.

**Plain parts and the baked occlusion.** Every role that is not the livery is a *plain part*: `PistaTex.Plain` draws it with `PistaPaint.shader` as part 30 (its colour, `_Smooth`, `_Metal`; no livery, no relief, no
clear coat), so that the occlusion baked into the file reaches it. `bake_ao` (called by `stage_export_moto`, 64 rays of 0.14 m per vertex against every part of the bike except the windscreen, written in the colour
attribute `Col` with the same value in the three channels, never below 0.10) says how much of the sky a vertex sees: the creases between the tank and the fairing, under the tail, round the engine and the swing arm are
darker. The shader multiplies the **ambient light and the reflections** with it (URP's `surfaceData.occlusion` leaves the sun alone) and reads it in the **green** channel of the vertex colour when the material's `_AO` is 1:
the meshes drawn by code tag their part in the *red* channel and keep the green at 255 (`PistaBikeMesh.Tag`), so a tagged mesh on a material that reads the occlusion (the backs of the number plates wear the paint of the
bike) stays lit. `PistaBikeMesh.Build` switches `_AO` on for the plain materials of the kit only when the bike wears a model.

**The windscreen** is the role `smoke`: `PistaGlass.shader`, smoked glass you see the rider and the road through. It blends premultiplied (what is behind is cut by `_Opacity`, 0.45, square on, and up to `_Rim`, 0.92,
at a grazing view, a Fresnel rise; the reflection is added whole), writes no depth and casts no shadow. The mirrors keep the opaque `glass`. Without the shader (or a machine that does not support it) it is an
opaque dark glass.

**Panel joints.** The fairing is one shell, so its joints are drawn on it: `seam` lays a thin dark pipe (radius 2.6 mm, half of it proud) along a line of `(y, z)` points on both flanks, sampled every 2 cm so that it keeps
to the surface; `ring_seam` closes one round a section (the nose cone, `NOSE_SEAM_Y`); `fasteners` puts the low steel domes of a race fairing along a line. `LOWER_SEAM` is the joint between the upper and the lower
fairing: it climbs toward the nose and runs level under the knee, below the vent and the gills.

**The bodywork.** The fairing and the belly pan are **one shell**, a loft of sections from a table of rows `(y, hwb, hwm, hwt, zb, zm, zt, rb, rm, rt)`: a bottom edge (half width `hwb`, height `zb`), a **shoulder**
(`hwm` at `zm`, the widest place, where the side has its crease) and a top edge (`hwt` at `zt`), the corners rounded with `rb`, `rm` and `rt` (`rpoly`: a quadratic arc of three points on every corner, so every section has
the same number of points and the rows can be blended with a Catmull-Rom). The same sections draw the seat cowl, with its shoulder high so that the sides are upright where the number plates are. Details that lie
on a flank (the side vent, the gills) are placed by `flank(rows, y, z, side)`, which finds the surface at that place and returns the position and the angles that lay a plate on it, so that they follow a table that
changes. The constraints that the tables carry: ahead of y = 0.40 the lower edge of the fairing stays above the front wheel and its mudguard (they rise with the fork and turn), behind the wheel it drops to the
belly, whose bottom is above the collector of the exhaust (z 0.28), and the rear end of the shell tapers to a point in height as well as in width, because it ends under the tank and the seat.

About 51 thousand triangles for a bike (body 26.0 thousand, front wheel 9.0, rear wheel 8.3, steering 5.6, swing arm 2.5, screen 0.2), the naked bike 45.7 thousand; the twelve riders of a grid are twelve bikes and twelve
riders of these parts, one mesh per part shared by every bike of the same style.

## The rider

The rider is built for a bike with clip-ons (the naked bike moves the gloves and the hands of the arms by the difference of the grips, `PistaRiderModel.GripDelta`) in a deep tuck: the torso 28 degrees above the horizontal,
a hump on the back, the head low behind the screen. It is 32 thousand triangles (torso 9000, boots 2300 each, gloves 1600 each, helmet 6200, arms 1700 each, legs 2400 each), shared by the twelve riders of a grid.

**Parts and roles.** Every piece is a mesh node, and the name of its material is its **role**: the game maps `sharedMaterials[i].name` to its own material, so the materials of the file only carry names.

| Node | Roles (materials) | In the game |
|---|---|---|
| `torso`, `hump` | `suit`, `hump` | children of `Busto` (the torso of the rig), hidden in the first-person view |
| `helmet_shell`, `helmet_seal`, `helmet_visor`, `helmet_black` | `helmet`, `seal`, `visor` | children of `Testa`, hidden in the first-person view; the livery stripes are `PistaPaint` part 8 |
| `glove_L`, `glove_R` | `glove` | children of the steering (they turn with it) |
| `boot_L`, `boot_R` | `boot`, `seal` (the sole), `slider` (the shell on the ankle) | children of the body of the bike |
| `arm_L`, `arm_R` | `arm_upper`, `arm_fore` (the elbow and the shoulder protector are in the second) | skinned over two bones each; hidden in the first-person view (the camera is between the shoulders and, at full tuck, an arm fills a fifth of the picture: the gloves stay) |
| `leg_L`, `leg_R` | `leg_thigh`, `leg_shin` (the knee cup is in the second), `slider` | skinned over two bones each (the knee cup and the slider are joined to the leg) |
| `J_*` (empties) | | the rest joints: `hip_c`, `head`, `neck`, `shoulder_c`, `shoulder/elbow/wrist/grip/hip/knee/ankle/peg` on both sides |

Roles that are distinct materials (a thigh, a shin and a slider are three; a boot, a sole and a shell are three) keep their material slots apart when the objects are joined; two equal materials would be merged by Blender and the slot lost.

**The livery.** `PistaPaint.shader` draws it from the position of the pixel in the part's own frame, so a part must have its node at the origin the shader expects (the torso and the hump at the hips, the helmet at the middle
of the head, the others at the origin of the bike). The mesh has no vertex tag: the part is the material's `_Part` (set from the role by `PistaRiderModel.Part`): 20 the torso, 21 the hump, 22 a thigh, 23 a shin, 24 an
upper arm, 25 a forearm, 26 a knee slider, 27 the leather of a glove or a boot, 8 the helmet. The limbs also carry texture coordinates (u round the limb, v along each part, restarting at the elbow and the knee), which the
patterns of the suit read. `_AO` (1) makes the green channel of a vertex colour the baked occlusion (see the bikes): the rider's file carries it too (below).

**Skinning and the pose.** Each limb has two bones (shoulder to elbow, elbow to wrist; hip to knee, knee to ankle). The weights are worked out in the game from the side of a plane at the joint that a vertex is on, blended
over 4.5 cm for an arm and 6 cm for a leg; the skinned meshes are made once and shared, because the rest pose is the same for every rider. Every frame `PistaRiderRig.Pose` puts the bones: the shoulder follows the torso, the
wrist the steering, the hip the pelvis, the ankle the peg, and a two-bone solution bends the elbow down and a little out and the knee forward (the knee of the inside leg goes out when the rider hangs off the bike),
stretching a bone when the target is out of reach. The elbow bends toward a pole 0.20 to the side, 0.05 forward and 0.55 down (`ELBOW_OUT` in `rider.py` and `PistaRiderRig.Pose` must say the same, or the arm of the file
is not the arm of the first frame): the first version had 0.42, which puts the elbows 0.36 m from the middle of the bike (worked out from the rest joints), wider than the grips, and the front photograph shows the
wings of a puppet; with 0.20 the elbows are at 0.269 m, which `-pistariderlog` writes both for the rest joints of the file and for the first frame of the game (the same 0.269, so the file and the game agree). The bones are
children of the body of the bike, so a bike that leans takes its rider along.

**Protectors, boots and the occlusion.** The first rider was tubes with a ball on the knee; the second one has the parts that make a racing suit read as one. Every addition reuses a role that already exists, so
the game needs no code for it (a new material name would fall back to the plain suit, `PistaRiderModel.Role`) and every piece is one more object joined into the node of its limb:

- `hug_pad` (`rider.py`) builds a **plate wrapped on a sphere round a joint**: an oval outline (a superellipse), a list of layers `(height above the surface, scale of the outline)`, the lowest ones buried in the
  limb. A sphere of the radius of the limb round the joint lies inside a tube that bends there, so the plate never hangs in the air where the limb falls away (a flat plate did, on the knee, which is bent by
  114 degrees). `DOME` (a soft shoulder, a rounded top: the knee cup and the elbow cup, in the material of the shin and of the forearm) and `PUCK` (a flat top with a chamfer: the knee slider, the plastic of part 26)
  are the two profiles. The cup is in front of the knee and a little to the outside, the slider on the outside, 66 degrees apart and 6 mm higher, so that the two never cut each other (when they did, the edge of the
  slider was a torn line).
- The **legs** have a calf and a narrower knee (seven control points), the **sleeves** a forearm that tapers to the wrist under the cuff of the glove (a forearm as thick as the upper arm was a sausage), and both
  start **inside** the torso and the pelvis: a tube that starts at the joint leaves a flat disc outside the body (the thigh showed a cut under the hump, the shoulder a ball where a cap of the torso stuck out).
  A shoulder protector in the same way made a ball joint of the shoulder and was dropped.
- The **boot** is no longer a union of blobs: a loft of sections for the foot (`FOOT`, from the heel to a round toe, read with a Catmull-Rom like the helmet's table), a slab for the sole (`seal`, dark rubber, a
  heel block 34 mm high, 4 mm wider than the foot), a shaft with a band at the top and a strap, and a plastic shell (`slider`) on the outside of the ankle. 2300 triangles, as many as before.
- The **helmet** has a visor that is a wedge (its lower edge rises and its upper edge falls toward the sides, the lowest row is shorter), no spoiler and no vents on the chin bar: the small fin read as an ear
  from the side and as a pig's tail from behind, three round vents in a row as nostrils and one slot as a mouth.
- **Occlusion.** `stage_export_pilota` bakes it (`bake_ao`, 48 rays of 0.10 m, never below 0.30) against the rider alone: arms against the trunk, thighs against the pelvis, the chin under the helmet, the fingers
  round the grip. The bike is not in it (the styles share this rider and the pose moves him), so the thighs do not darken where they touch the tank; the shader multiplies only the ambient light and the reflections.
- The knee slider is a plain grey with fine streaks in the shader (part 26, and part 5 for the one drawn by code): a coarse noise made it look like a ball of felt.

## How it is checked

- `-pistamenutest -pistamenugarage` (menu tests): every painted material of a rig wears the shader, every part has the number the shader expects (by vertex tag for what is drawn by code, by `_Part` for the models), the
  body mesh of a model, and the torso of the rider, carry their baked occlusion (at least one vertex in a hundred with a green channel under 235: a file that lost its colours on the way in would pass every other check; the torso of the rider has 292 of its 4,502 vertices under it, read by making the threshold impossible once), and the
  picture of the showroom has no magenta pixel. `-pistabikeshots -pistabikes 0,2,3` takes the photographs of the bikes (`hotel_build/prove/pista/bici_N_{lato,davanti,dietro,motore,pilota}.png`): it is the way to
  see whether a piece is where it should be, because the checks only count.
- `-pistariderlog` writes the nodes of the model (position, rotation, bounds in the frame of the body), the rest joints and the first pose in the log: what to read first when a limb is not where it should be.
- `-autotest -pistatest -pistaperf -pistabot -pistalaps 1 -pistarivals 11 -seed 777` (a graphics device, no `-nographics`; without `-autotest` the mode does not start and the test fails with "no director") measures
  a frame of a full race; see the cost in [Track racing](../game/track-mode.md). The race tests need `-pistabot` too: without it nobody drives the bike of the player, it never leaves the grid and about
  eight checks fail for that reason alone.
- The shape of the models is judged **only by photographs**: nobody has ridden a race with them, and nobody has compared them with a photograph of a real bike.

## Traps found

- A node dropped and rebuilt (`Destroy` and a new object) loses its rotation: the first skinned limbs lay on the ground behind the bike. The rotation is now baked into the mesh by the postprocessor, and the skinning
  also carries the vertices through the matrix of their node into the frame of the body, whatever the importer did.
- The postprocessor straightens only the nodes that hold a mesh. The first bike FBX had the Gun's empty (`moto0`) as the parent of every part: the empty kept the 90 degrees, and the parts, re-parented keeping their
  world position, came into the game standing on their edges, the front wheel under the floor. The parts are now exported with no parent.
- `bmesh` faces made by hand have no normal until `normal_update()`: `orient_outward` judged every patch inside out, so the visor and its seal were drawn with their faces turned in, invisible from outside with back-face culling
  (a Cycles preview shows both sides and hides the fault).
- A smoothstep between the rows of a table flattens the profile at every row: the helmet showed a ring at each of them. A Catmull-Rom through the rows is smooth.
- Two equal materials on one object are merged by the importer; give every role its own.
- A ring of metal behind a wheel (a carrier for the brake disc) hides the spokes from the side: the wheels looked like solid plates. Discs are rings with a hole, and nothing but the hub is round behind a spoke.
- A bodywork made of superellipse sections looked like an egg. Rounded-trapezoid and shoulder sections, with 45 degree facets that the smoothing angle (40 degrees) turns into hard edges, look like panels.
- The number of triangles of a part moves by a few from export to export (the boolean cuts), so two exports of the same script are not byte-identical files: do not compare them, look at the photographs.
- The baked occlusion is read from the **green** channel of the vertex colour, not the red: the meshes drawn by code tag their part in the red (the paint of a bike that wears a model also covers the backs of the number
  plates, which are code meshes tagged 1), and a first design that read the red would have drawn those backs black. A tagged mesh keeps its green at 255.
- A joint drawn as a pipe along a curved surface would sink into it between two samples (the sag of a straight piece over eight centimetres on a 30 cm curve is about its radius): `seam` samples the line every 2 cm and lifts
  the pipe by a third of its radius. A precaution taken from the start, not a fault that was met: the photographs show the joints whole.
