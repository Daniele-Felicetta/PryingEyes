# Unreal Engine prototype: the same room in two engines

A feasibility test, not a port. It takes one room of a generated floor (same seed, same camera), rebuilds it in Unreal Engine 5.8 from
what the Unity game produced, and photographs it in both engines so that the two pictures and the two frame times can be compared.
Unity stays the game; nothing in the game depends on this folder. The rule of the prototype is **no Blueprints**: C++ for the
runtime (`APEGameMode`, driven from the command line) and Python run inside the editor for the imports. It is not the hand-built,
Blueprint-driven hotel hall described in [legacy-versions.md](../history/legacy-versions.md): this one is generated from the Unity
game, so the two engines draw the same geometry.

## Pieces

| Piece | Where | What it does |
| --- | --- | --- |
| Exporter | `unity/PryingEyes/Assets/_PryingEyes/Editor/UnrealExport.cs`, `PEEquirect.shader` | `UnrealExport.Run`: builds a floor from a seed, takes one room, writes `scene.json` (nodes, lights, fog, look), the raw meshes, the textures and the reflection cube as a lat-long `env.exr`; renders the Unity photo and times it. `-ramp 1` writes the tone ramp instead |
| Unreal project | `unreal/PryingEyes/` (`PryingEyes.uproject`, `Config/`, `Source/`, `Python/`) | only these are tracked; `Content/`, `Saved/`, `Binaries/`, `Intermediate/` and the derived-data cache are rebuilt (see `.gitignore`) |
| Director | `unreal/PryingEyes/Source/PryingEyes/PEGameMode.*` | builds the scene named by `-pe_scene` (`monk`, `room`, `ramp`, `lab`), waits until every shader and asset is ready, takes the photo, times the frames, writes a report and exits. Its arguments are listed at the top of `PEGameMode.h` |
| Mesh import | `Source/PryingEyes/PEImportLibrary.*` | editor-only `UPEImportLibrary::BuildStaticMeshFromFile`: a static mesh from the raw vertex and index buffers, callable from Python |
| Imports | `Python/pe_unity.py`, `import_monk.py`, `import_scene.py`, `import_grade.py` | the master material `M_UnityLit` (URP Lit's rules as a node graph), the grade material `M_UnityGrade`, textures, the environment, the monk (skeleton and animations), a whole scene |
| Runner | `tools/unreal/ue.ps1` | `build`, `python <script>`, `photo` (hidden window), `game` (visible), all at low CPU and I/O priority; logs go to `unity_logs/unreal` |
| Grade fit | `tools/unreal/fit_ramp.py` | `show` prints Unreal's tone ramp next to Unity's; `fit` runs parallel photo runs and walks the film-curve parameters downhill (needs numpy and Pillow) |

The tools are Unreal's own: `Build.bat` (UnrealBuildTool), `UnrealEditor-Cmd.exe` with the `pythonscript` commandlet, and
`UnrealEditor.exe -game -RenderOffscreen`. No plug-in and no editor window is needed. Set `UE_ROOT` if the engine is not in
`%ProgramFiles%\Epic Games\UE_5.8`.

## Running it

From the repository root, with the game and any other Unity closed (the exporter needs the real GPU, so no `-nographics`):

```
# 1. Unity: export the room and take its photo
Unity.exe -batchmode -projectPath unity/PryingEyes -executeMethod PryingEyes.EditorTools.UnrealExport.Run -seed 1000 -floor 1 -room Ristorante,SalaBallo,Camera -weapon ar_trinita -monk cammina:0.3 -quit -logFile unity_logs/unreal_export/export.log
# 2. Unreal: build the module, import the monk and the room
powershell -NoProfile -File tools/unreal/ue.ps1 build
powershell -NoProfile -File tools/unreal/ue.ps1 python unreal/PryingEyes/Python/import_monk.py
powershell -NoProfile -File tools/unreal/ue.ps1 python unreal/PryingEyes/Python/import_scene.py export=unity_logs/unreal_export/1000_1
# 3. the photo and the frame times
powershell -NoProfile -File tools/unreal/ue.ps1 photo -pe_scene=room -pe_file=unreal/PryingEyes/Saved/PE/scene_1000_1_Ristorante.json -pe_out=unity_logs/unreal/photos
```

`UnrealExport` options: `-seed`, `-floor`, `-room <kinds>`, `-weapon <id>`, `-monk <take>:<seconds>`, `-radius <m>` (how far around the
room is exported), `-w/-h`, `-out <folder>`, `-perf 0` (skip the frame timing), and for calibration `-lightfilter <name>`,
`-ambient 0`, `-fog 0`, `-reflect 0`, `-ramp 1`. The photo is `unity.png` and the Unity frame times are `unity_perf.txt`, both in the
output folder (`unity_logs/unreal_export/<seed>_<floor>` by default).

## From a Unity room to an Unreal room

Nothing is redrawn: the exporter reads what `FloorBuilder` and the room kits already built, and the importer translates it.

| Unity | Unreal |
| --- | --- |
| axes (x right, y up, z forward), metres | X forward, Y right, Z up, centimetres (an even permutation: no mirroring) |
| UV `v`, triangle winding | `1 - v`, winding reversed |
| vertical field of view | horizontal one, through the window's aspect ratio |
| URP Lit / Unlit materials | instances of one master material that mirrors URP Lit's maths (base, metallic-smoothness, normal, occlusion, emission, detail, alpha clip, tiling) |
| light intensity (Unity units) | candelas, times K = pi x 1.2 x 2^9.9 = 3600 (the camera's own EV 9.9; Unity's post exposure is passed to Unreal's exposure bias), spot cones halved |
| emission, fog colour, flat ambient | multiplied by K / pi (the radiance of albedo 1 under one Unity unit) so that the exposure brings them back |
| exponential-squared fog | exponential height fog, density x 10 |
| trilight ambient | the same three colours as a flat term in the master material, times albedo and occlusion |
| reflection probe (`HallProbe`, intensity 0.30) | the cube as a lat-long image, sampled in the master material the way URP's `GlossyEnvironmentReflection` does |
| ColorAdjustments, SplitToning, ACES, bloom, vignette | exposure, film curve and saturation of a post-process volume, plus `M_UnityGrade` (below) |

**The grade.** URP applies contrast, Split Toning, saturation and the ACES curve, in that order, to the exposed image. `M_UnityGrade` is
a post-process material (scene colour before bloom) that does the contrast and the Split Toning with URP's own formulas (soft-light
in gamma 2.2 inside ACEScg); the curve and the saturation are Unreal's, fitted against Unity: `UnrealExport -ramp 1` photographs 30
unlit patches (values 0.0005 to 8.7, a factor 1.4 apart) through the real camera and writes `ramp_unity.txt`; `-pe_scene=ramp` does
the same in Unreal, and the film curve is fitted so that the two tables agree (`tools/unreal/fit_ramp.py`). The fitted values are
constants in `SpawnGrade` and `SpawnPostProcess`: exposure bias = Unity's post exposure - 0.6 EV, toe 0.7, slope 0.6, shoulder 0.46,
white clip 0, the look's own saturation. They are overridable on the command line (`-pe_ev`, `-pe_toe`, `-pe_slope`, `-pe_shoulder`,
`-pe_blackclip`, `-pe_whiteclip`, `-pe_saturation`, `-pe_gamma`, `-pe_split=0`); run the fit again, and copy the numbers over, whenever
the game's look changes.

**Measuring frames.** Unreal times the tick delta and reads the GPU cycles (`RHIGetGPUFrameCycles`); the photo waits for shader and
asset compilation first. Unity times `Camera.Render` into a RenderTexture of the same size in the editor's batch mode, once with a
one-pixel read after every frame (waits for the GPU each time: pessimistic) and once with all frames queued. This is not the player
loop: compare it with the player's own `-perftest` numbers, not as an exact ratio.

## Results

One room (seed 1000, floor 1, the Ristorante, 17.6 x 19.2 m), the entry camera, `ar_trinita` with arms, the monk frozen at 0.3 s of
`cammina`, 1920x1080, an RTX 5060 Ti. The grade was fitted once (`fit_ramp.py`) and not touched afterwards.

| | frame | fps | what it is |
| --- | --- | --- | --- |
| Unity, editor batch mode, wait for the GPU after every frame | 7.76 ms | 129 | `Camera.Render` into a RenderTexture, one-pixel read; forward+, URP, no global illumination |
| Unity, same, all frames queued | 8.43 ms | 119 | |
| Unreal, Lumen global illumination and reflections, virtual shadow maps | 4.65 ms (GPU 4.43) | 215 | the project's defaults (`Config/DefaultEngine.ini`) |
| Unreal, `-pe_gi=0` | 3.47 ms (GPU 3.33) | 288 | direct light, flat ambient and reflection cube only, as in Unity |

Read the first two rows with care. The Unity figures come from the editor in batch mode, which is slower than the player; the
player's own `-perftest` probe, with the same technique, measures about 5 ms (4.9 to 5.4) on Ristorante views of other seeds (not the
same view). The honest reading is "the same order of magnitude, Unreal paying about 1.2 ms for Lumen", not a ratio.

**Tone.** On the 30-patch ramp Unreal is within 1.4 display levels of Unity, colour included (a mean weighted by how many pixels of
the room photo have that level). The residual is up to 8 levels between values 0.03 and 0.1, and 8 to 12 levels at the bright end
(below Unity from 0.2 to 1.1, above it from 1.6, where Unity clips at 243 and Unreal keeps rising). The blue shadows of the game are
its Split Toning, not its ambient light. 60 % of the pixels of the room photo are below 8/255 and 95 % below 50, which is why the fit
is weighted by the photo's histogram.

**Light.** `-pe_scene=lab` in Unreal and `UnrealExport -lab 1` in Unity are the same experiment: a white plane at 3 m, one light of
Unity intensity 1 at the camera, no ambient, no screen effects (`-labi`, `-labd`, `-labspot 1` for a spot). Theory (albedo x I / d^2)
says a scene value of 0.111. Read through each engine's own tone ramp, Unity's centre pixel (132 137 141) is 0.107 and Unreal's
(141 144 148) is 0.112, point or spot alike: both follow the inverse-square law and K is right.
In the room, however, lit surfaces come out brighter in Unreal than in Unity: the median ratio of scene values is 1.3 to 1.5 (a
point light alone, the window spots alone), with a spread (10th to 90th percentile 0.8 to 2.5) that says it is not one constant.
The whole photo is brighter too: mean pixel value 0.107 against Unity's 0.056 (about 1.9 times; 0.095 with `-pe_gi=0`), mean linear
luminance 0.0223, 0.0187 and 0.0076.
Not explained. Ruled out: the light scale (above), the light colour (the exporter converts it to linear), the detail-map factor (it
applies only to materials that have a detail texture) and global illumination (switching it off lowers the mean pixel value by 11 %
and the mean linear luminance by 16 %, a small part of the gap). Candidates, none checked: the normal maps (the importer flips the
green channel and maps `_BumpScale` by hand), the mask maps (smoothness and metallic), the specular term (a rough estimate finds the
two formulas alike, nothing measured), the viewmodel's shaders, the exported light parameters (cone, range) and the ambient terms (an
ambient-only photo came out black in both engines, so it compared nothing). The two `-pe_gi` variants and the Unity photo are in
`unity_logs/unreal/photos` (not committed).

## Things that cost time

- `ASpotLight`'s component points down by default: set the world rotation of the component, not the actor's.
- Material pin names are the *short* ones (`UMaterialGraphNode::GetShortenPinName`): `""` for `Input`, `"Level"` for `MipLevel`,
  `"UVs"` for `Coordinates`. `_link` in `pe_unity.py` warns when a connection fails.
- Enum names in Python are not the C++ ones: the post-process location "before tonemapper" is `BL_SCENE_COLOR_BEFORE_BLOOM`.
  Post-process materials get their scene input already divided by the pre-exposure and multiplied back on output, so a Custom node
  sees the engine's lighting units.
- A game run cannot add material usage flags: the master sets `used_with_skeletal_mesh`, `used_with_instanced_static_meshes`,
  `used_with_nanite` when it is built.
- Static components attach only to a static root: the room root is set to static mobility before the instanced components are added.
- `UnrealExport` must find `MainCamera` before it deactivates the player (the camera hangs from the player), and must set the same
  look the game sets (`GameManager.proc`, `GameSettings.BindBrightness`, `FloorDirector.ApplyLook`) or the Unity photo is ungraded.
- PowerShell 5.1 splits an unquoted `-name=0.3` at the dot; pass such arguments in single quotes or through an array.
- Editor-side clang diagnostics ("`CoreMinimal.h` not found") in an IDE are noise: the editor has no Unreal include paths and the
  build is the arbiter.

## Not translated

The haze cards, dust motes, grime and lens shaders of the game have no equivalent here (two nodes and one material are dropped).
The weapon is a static pose, the monk a frozen frame of its walk (no hand rig, no animation blending), and there is no gameplay.
