# Survivors: the four characters

The players are four rigged and textured survivors, made procedurally in Blender (`modelli/personaggi/`) and animated entirely by
code (`Scripts/Survivors/SurvivorBody.cs`). They replace the capsule mannequin that every co-op partner and every split-screen player used to be,
and since 0.8.0 they are also the player's own body in the [third-person view](third-person.md).

| Slot | Id | Who | What sets them apart |
| --- | --- | --- | --- |
| 1 (host, player 1) | `custode` | The Custode | stocky, grey hair and moustache, olive canvas jacket, navy trousers, red wool scarf, tweed cap |
| 2 | `dottoressa` | The Dottoressa | slight, brown bob that hangs free to the jaw and covers the ears, parted at the side, cream coat, charcoal trousers, teal scarf |
| 3 | `reporter` | The Reporter | fair, short dark-blond hair with a fringe swept to the side, mustard jacket, blue trousers, grey scarf |
| 4 | `cacciatrice` | The Cacciatrice | strong legs, auburn hair in a long braid under a dark cap, oxblood jacket, olive trousers, amber scarf |

Since 0.9 (not released when this was written) the look is part of the player's **character** ([saves-and-characters.md](saves-and-characters.md)):
the player picks one of these four, with a name, when making the character, and it stays with the character. The slot only decides when there
is no character to say (`SurvivorBody.IdOf(slot)`: the automated tests, whose processes send no look, and the body of a seat or partner with
an unknown look); the slot is the seat of a split screen or the network slot of online co-op, and the table above is the order of the looks
(`CharacterLooks.Ids`) and what the slot gave before. Two characters never share a look in one game: whoever comes second takes the first free one
(`CharacterLooks.Resolve`, done by the host for online co-op and when the seats are made for a split screen), so on one screen nobody is taken
for somebody else; the character itself is not changed. A look whose prefab is missing falls back to the Custode, and a build without any model
falls back to the old mannequin (`RemotePlayer.BuildBody`).

## Where they show

- **Online co-op** (`RemotePlayer`): every partner is a survivor with their weapon in hand, aiming, shooting and reloading as they do. The
  weapon, aiming and reload state travel in `PlayerNet` (`gun`, `ads`, `reloading`; protocol `pe-coop-29`), and each player's look and level
  in the greeting (`Hello`, `Welcome`, `PlayerJoined`; protocol `pe-coop-30`), see [../multiplayer/README.md](../multiplayer/README.md). The
  name stays above the head, with the level when it is above 1.
- **Split screen** (`SeatAvatar`): every player sees the others' survivors, never their own body in front of the eyes
  ([../multiplayer/split-screen.md](../multiplayer/split-screen.md)).
- **Third person** (`ThirdPersonView`): the player's own survivor, with the same code as the others.

## The body at runtime (`SurvivorBody`)

The owner (`RemotePlayer` from the network, `SeatAvatar` from the player of a seat) places the root at the feet, turned like the body, and fills
a `SurvivorInput` every frame: velocity in the world, pitch (positive looks down), lean, crouch, sprint, alive, downed, grounded, climb, the
index of the weapon in `WeaponDefs.All` (-1 for none), seconds since the last shot, aiming 0..1 and reloading. The body does the rest in
`LateUpdate` (execution order 1000, after everything that moves the owner); the tests call `Step(dt)` themselves.

- **Pose without the FBX axes.** Every pose is a rotation relative to the rest pose, computed in the frame of the root, so it does not depend on
  how Blender oriented the bones. The Animator of the FBX is switched off; the pose is reset every frame before it is built.
- **Legs.** Two-bone analytic IK with a pole at the knee. The gait is procedural (stance and swing, stride `0.60 + 0.16 x speed` m, shorter
  crouched, the feet raycast on the environment layers and follow stairs and slopes). The hips lower so a leg never has to lock.
  Sprinting, crouching, jumping and landing change the stride and the pelvis.
- **Torso and head** follow the pitch and the lean; the head looks where the player looks.
- **Arms and weapon.** The weapon is the same prefab as the first-person one (`Resources/Armi/<id>`). It sits in the hands with the grips that
  `GripPoses` models for the first-person hand (wrist point, palm frame, the joints of every finger, the thumb, the trigger finger): the
  wrists are placed by IK and every finger bone is aimed at its registered joint. The index goes to the trigger while aiming and just after
  a shot. Aiming puts the weapon's sight line (`WeaponRig.AdsLocal`) on the eye line at the weapon's `adsDist`. A shot kicks the weapon back
  with the same `shotAge` curve as the firing. A reload moves the free hand from the belt pouch to the magazine well and back to the grip.
- **Downed and dead.** Downed sits the survivor on the ground (an IK blend of the legs and the torso); dead lays the whole model down.
- **Light layers.** The project defines six rendering layers (`ProjectSettings/TagManager.asset`): `Default` (bit 0, the world), `Arma` (bit 1, arms
  and weapons, lit by the lamps of the world, `3u`) and `Corpo 1` to `Corpo 4` (bits 2 to 5, one per slot).
  `SurvivorLight.BodyMask(slot)` is `Arma` plus the slot's `Corpo` bit (`4u << slot`); `TorchMask(slot)` is every torch bit but the slot's own. A torch lights the other players' bodies, never the
  player's own weapon from a few centimetres. `Torch` applies it to its beam and spill each frame, and the beam of a partner too.
  `FillMask(slot)` is the slot's bit alone: the [third-person view](third-person.md) uses it for a weak light that lights only the player's
  own survivor.

## How they are made (`modelli/personaggi/`)

Everything is Python for Blender 4.5, headless, from this folder (the folder's own `work/` and `render/` are not in git):

```
blender -b -P genera_personaggi.py -- body            the nude proxy, head, hands: clay previews
blender -b -P genera_personaggi.py -- face <id> [s9] [skin] [hair] [fine]   the sculpted head alone in a few seconds: clay, or with the skin
                                                      (skin), with the hair, brows, moustache and braid (hair), at the production
                                                      resolution (fine); dbg shows the backs of faces in magenta, proxy the low-poly hair
blender -b -P genera_personaggi.py -- head <id>       face, hair, cap and collar only, five sides, about a minute: for a face or a hairstyle
blender -b -P genera_personaggi.py -- build <id>      high-poly parts with procedural materials, previews from four sides and two close-ups
blender -b -P genera_personaggi.py -- export <id>     low-poly, UV atlas, bakes, skin weights, FBX and textures into Unity, previews of the result
```

Run one Blender at a time: each one wants every core (the voxel remesh), and three together took more than 17 minutes to get through what one
does in three. A `build` takes about three minutes and an `export` about two.

- `skeleton.py`: one skeleton for all four (1.75 m, A-pose, fingers included), the joint positions and the skin-weight rules, which look only at
  the rest-pose position of a vertex so they work on the body and on any garment built around it.
- `body.py`: the body proxy and the hands (`build_head`, `build_ears` and `build_eyes` are the ones of `face.py`).
- `face.py`: the head. A face is one signed-distance field made of anatomical blobs and grooves (skull, brow ridge, cheekbones, nose, lips, mouth
  line, nasolabial folds, eye sockets, lids), each a primitive with a few numbers; the field is marched from inside the skull onto an icosphere
  (`build_head`, 160 000 vertices, about a millimetre), the eye openings are holes whose rims hug the eyeballs, and the ears and the eyeballs come
  from the same file. The four characters differ in a dozen scalars (`face` of `characters.SPEC`: width, jaw, chin, brow, cheek, hollow, nose,
  lip, mouth width, age, eye). `paint` writes the vertex colours that the skin material reads (`fa`: lips, mouth line, brow zone, lashes; `fb`:
  nostrils, sockets, flush, beard; `fc`: the tint of the roots under the hairline, the lids), so the painted features sit on the sculpted ones.
  The lower face stops behind the jaw angle (`JAW_BACK`): its back half used to hang behind the neck as a ball, which is what a short haircut
  showed from behind.
- `hair.py`: the hair, the brows, the moustache and the braid. The hair is a sheet laid over the head, not a shell cut from a sphere: its columns
  are the flow lines of the hair, which follow the real surface of the head (the same field, so the hair always clears the brow, the temples and
  the ears) from the crown (or from the edge of a cap) to the lower edge of the style, or leave it at a release angle and fall straight down
  hugging the outline of the head (the bob). Every column has its own length (a slow wave, plus a pointed tip for every lock) and thickness
  (thicker at the crown, a lift at the front, a parting, ridges of unequal width that wave a little). Rows are a constant distance apart, not a
  fraction of each column, so neighbouring columns of very different lengths never shear; there is a row exactly where the hair leaves the head.
  One description gives two meshes: the high-poly sheet with the relief of the locks (the source of the bake) and a coarse sheet of the same
  shape without relief (the low-poly the game uses): the part carries the second in its `lowproxy` property. Per-vertex attributes tell the
  hair material where the strands run (`hflow`) and which lock a point belongs to and how far it is from the root (`hf`). Brows and moustache
  are patches cut from the skin of the head and lifted off it; the braid is three woven strands (high-poly) with a tube as its low-poly proxy.
- `garments.py`, `mats.py`: jacket, trousers, scarf, boots, gloves, cap, belt kit; the procedural materials (skin with lips, brows, flush,
  stubble, wrinkles by age and weathering; hair with strands that follow the flow, lock tones and lighter tips; woven and leather surfaces with
  wear; the eye with shade under the lids).
- `characters.py`: `SPEC` (body proportions and face of each character) and `LOOKS` (skin, hair colours and `hair_style`, brows, moustache,
  braid, eyes, the colour of every garment, what they wear). **A new character is a new entry in both and nothing else**; the colours are linear
  values. A `hair_style` is a dict for `hair.py`: the height of the lower edge around the head (`edge`), `release` for hair that hangs free,
  `start` for short hair under a cap, thickness (`top`, `side`, `front`), the size of the locks (`clump`, `relief`, `tips`, `saw`).
- `export.py`: every part is decimated to its triangle budget (`BUDGET`; a part with a `lowproxy`, the hair and the braid, takes its proxy
  instead, because decimation wrecks a sheet), the parts of a group are joined into one mesh with one UV atlas, base
  colour, tangent normal and a mask (metallic in red, which is zero for all four groups, and smoothness in alpha: the layout of the
  `_MetallicGlossMap` of Unity's Lit shader) are baked from the high-poly parts, and the bone weights are computed. A group is `skin`,
  `outfit`, `hair` or `eyes`: four materials per character. The textures are 1024 (skin), 2048 (outfit), 1024 (hair) and 256 (eyes) pixels, and
  each character is about 40 000 triangles on 52 bones.
- The braid is weighted from the head at the nape to the upper chest lower down so that it bends; every other part of the head is rigid on `Head`.

In Unity (`Editor/SurvivorImport.cs`, `PryingEyes.EditorTools.SurvivorImport.Run`, which does every FBX of `Models/Survivors/`): the FBX is
imported as a Generic rig without clips (the body animates it from code), its embedded materials are remapped by cleaned name (Blender names
them `<id>_skin.001`) to `Materials/Survivor_<id>_<group>.mat` (URP Lit with the three textures), and the prefab
`Resources/Survivors/<id>.prefab` is saved (its bounds follow the mesh, so the four prefabs are committed with the FBX). `Editor/SurvivorShots.cs`
(`PryingEyes.EditorTools.SurvivorShots.Poses`, with `-slot N` or `-slot all`, `-only a,b`, `-views lato,fronte,dietro,faccia,faccia3q,nuca`; it
needs the GPU) renders contact sheets of poses (stand, walk, run, crouch, aim, shoot, reload, downed, dead) to `hotel_build/survivors/`, which is
not in git.

## Verification

`Editor/SurvivorShots.cs` renders the four characters in every pose of its contact sheets (stand, walk, run, crouch, aim with several weapons, shoot, reload,
downed, dead) from the side, the front and the back: every pose works with every character, the weapon sits in both hands and the feet stay on the
ground. In the game, `tools/coop/run_split.ps1 -Players 4` (64/64) builds the four survivors in one split screen, and `tools/coop/run_all.ps1 -Only a,h`
(two and four processes) and `run_coop.ps1 -Name foto -HostGraphics ... -cooppics` (13/13) show a partner with the weapon in hand, from the side, close
and from behind. The own survivor in the third person is in [third-person.md](third-person.md).

The faces and the hair were judged on three kinds of photo: `run_coop.ps1 -Name foto -HostGraphics ... -cooppics` (`coop_vicino`: the partner a
metre and a half away, in the light of a torch), the third-person photos of `-tptest -tpshots` (the player's own survivor from behind), and three
views of `SurvivorShots` that look at the head alone (`faccia`, a metre in front; `faccia3q`, three quarters; `nuca`, from behind):
`-slot all -only idle -views faccia,faccia3q,nuca` renders the four faces and the four napes in one run.

Known limits:

- The look is stylised (about 40 000 triangles with procedural, baked textures): the four are recognisable and distinct, but not photo-real.
- The four faces are made the same way (a dozen scalars on one anatomical field), so they share a long nose and gaunt cheeks: at a glance the four
  are told apart by hair, cap, moustache and braid more than by the face. Under a torch the skin is a warm, matte brown (the baked skin has no
  subsurface scattering), and from a metre and a half the irises are only a few pixels.
- The hair has volume (a sheet with a relief of locks, baked into a 1024 pixel normal map), a parting, a hairline without steps and no gap at the
  temples, but it is not strands: there is no transparency and no wisp, the crown is a plain radial fan, and the hair is rigid on the head (the
  braid bends). The Dottoressa's bob is a helmet-like volume that covers the ears.
- The braid reads as a straight woven rope, not as a plait with a swing.
- The animation was only judged on still images (the contact sheets and the photos), never watched in motion on a monitor, and the faces have no
  expressions or blinking.
- The torch of a player lights the bodies of the others but nobody checked a partner who aims with the torch on, close to a wall.

## Traps

- The bones of the FBX do not have the axes of the Blender armature. Do not read or write a bone's local rotation by hand: use the rest-relative
  poses of `SurvivorBody`.
- Blender writes `.001` after the material names inside the FBX. The importer remaps by the cleaned name; a material that does not remap shows
  grey.
- A body on layer `Seats.FirstAvatarLayer + slot` is lit only by lights whose rendering layers include its bits: a test light needs
  `Stealth.SetLightLayers(light, 3u)`, a plain `Light` lights nothing of the survivors.
- **A light layer that the project does not define does not exist.** URP clears from a light's mask every layer without a name in
  `ProjectSettings/TagManager.asset` (`RenderingLayerUtils.ToValidRenderingLayers`), so a light that only carries an undefined bit lights
  nothing, with no warning: the first version of the per-slot bits did exactly that (dark photos identical at every intensity). A new bit
  needs a new name there and a full build, not a scripts-only one, because it is a ProjectSettings change. `-tptest` checks it.
- The roughness maps baked for the previews are deleted at the end of `export`: Unity reads smoothness from the alpha of the mask.
- `PlayerNet.weapon` is the weapon *slot* (the magic replay uses it), not the index in `WeaponDefs`: the body reads `PlayerNet.gun`.
- **Faces and hair** (`face.py`, `hair.py`):
  - The face field is marched from inside the skull, first crossing only. Never carve a void with it (a hole inside the head is not reached by
    any ray): dent the surface instead, with an offset on the field. Do not put a feature behind the jaw angle (`JAW_BACK`).
  - A rim of an eye opening is a window in angle space snapped onto the eyeball. At the inner corner a gap between lid and eyeball shows as a
    black slit: the window is short there and a small plug (`eye_L` and `eye_R` get `eye_Lc`, `eye_Rc`) closes the rest. A plug at the outer corner
    pokes out as a white bulb.
  - The hair sheet has a pole at the crown, where every azimuth is one point: anything that depends on the azimuth (the lift at the front, a
    parting, the relief of the locks) has to die out towards it, or the crown gets a crease and a spike. The relief starts 12 mm from the pole.
  - Rows are a constant distance apart, never a fraction of each column's own length (neighbouring columns of very different lengths shear and
    crack at the temples), and the part that hangs free is offset horizontally exactly like the last row on the skull (a different offset makes
    a ledge).
  - The columns follow one ray per degree from `HAIR_O` down to 176 degrees, so a hairline can only go as low as the head surface does: the nape
    of short hair is at about -0.05 (head-local, the eye line is 0), where the neck takes over. A lower value makes the sheet wrap under the head.
  - Decimation wrecks a sheet and a braid: they carry a coarse proxy of the same shape in `lowproxy`, and the export takes it instead.
  - The skin and hair materials read the vertex colour attributes `fa`, `fb`, `fc` and `hf`, `hflow`: a part added to the skin group needs them
    (`paint`), or it bakes without its features.
  - The importer rewrites every `.mat` and `.prefab` of the characters and a few settings assets with other line endings: restore them by path
    (`git checkout -- <path>`) and commit only the sources, the FBX and the textures that really changed.
