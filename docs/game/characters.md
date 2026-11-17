# Survivors: the four characters

The players are four rigged and textured survivors, one per slot, made procedurally in Blender (`modelli/personaggi/`) and animated entirely by
code (`Scripts/Survivors/SurvivorBody.cs`). They replace the capsule mannequin that every co-op partner and every split-screen player used to be,
and since 0.8.0 they are also the player's own body in the [third-person view](third-person.md).

| Slot | Id | Who | What sets them apart |
| --- | --- | --- | --- |
| 1 (host, player 1) | `custode` | The Custode | stocky, grey hair and moustache, olive canvas jacket, navy trousers, red wool scarf, tweed cap |
| 2 | `dottoressa` | The Dottoressa | slight, brown hair in a bob, cream coat, charcoal trousers, teal scarf |
| 3 | `reporter` | The Reporter | fair, short blond hair, mustard jacket, blue trousers, grey scarf |
| 4 | `cacciatrice` | The Cacciatrice | strong legs, auburn hair in a long braid under a dark cap, oxblood jacket, olive trousers, amber scarf |

There is no character selection: the slot decides (`SurvivorBody.IdOf(slot)`; the slot is the seat of a split screen or the network slot
of online co-op), so on one screen nobody is taken for somebody else. A slot whose prefab is missing falls back to the Custode, and a
build without any model falls back to the old mannequin (`RemotePlayer.BuildBody`).

## Where they show

- **Online co-op** (`RemotePlayer`): every partner is a survivor with their weapon in hand, aiming, shooting and reloading as they do. The
  weapon, aiming and reload state travel in `PlayerNet` (`gun`, `ads`, `reloading`; protocol `pe-coop-29`), see
  [../multiplayer/README.md](../multiplayer/README.md). The name stays above the head.
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
- **Light layers.** `SurvivorLight.BodyMask(slot)` is rendering layer 2 (the one of arms and weapons, lit by the lamps of the world, `3u`) plus a
  bit per slot (`4u << slot`); `TorchMask(slot)` is every torch bit but the slot's own. A torch lights the other players' bodies, never the
  player's own weapon from a few centimetres. `Torch` applies it to its beam and spill each frame, and the beam of a partner too.

## How they are made (`modelli/personaggi/`)

Everything is Python for Blender 4.5, headless, from this folder (the folder's own `work/` and `render/` are not in git):

```
blender -b -P genera_personaggi.py -- body            the nude proxy, head, hands: clay previews
blender -b -P genera_personaggi.py -- head <id>       face, hair, cap and collar only, five sides, about a minute: for a face or a hairstyle
blender -b -P genera_personaggi.py -- build <id>      high-poly parts with procedural materials, previews from four sides and two close-ups
blender -b -P genera_personaggi.py -- export <id>     low-poly, UV atlas, bakes, skin weights, FBX and textures into Unity, previews of the result
```

Run one Blender at a time: each one wants every core (the voxel remesh), and three together took more than 17 minutes to get through what one
does in three. A `build` takes about three minutes and an `export` about two.

- `skeleton.py`: one skeleton for all four (1.75 m, A-pose, fingers included), the joint positions and the skin-weight rules, which look only at
  the rest-pose position of a vertex so they work on the body and on any garment built around it.
- `body.py`: the body proxy, the head with its face (every character has the same topology and a handful of proportions), ears, eyes, hands.
- `garments.py`, `mats.py`: jacket, trousers, scarf, boots, gloves, cap, belt kit, hair, moustache and brows; the procedural materials (skin with
  lips, brows, flush, stubble and weathering; hair with strands; woven and leather surfaces with wear).
- `characters.py`: `SPEC` (body proportions and face of each character) and `LOOKS` (skin, hair, eyes, the colour of every garment, what they
  wear). **A new character is a new entry in both and nothing else**; the colours are linear values.
- `export.py`: every part is decimated to its triangle budget (`BUDGET`), the parts of a group are joined into one mesh with one UV atlas, base
  colour, tangent normal and a mask (metallic in red, which is zero for all four groups, and smoothness in alpha: the layout of the
  `_MetallicGlossMap` of Unity's Lit shader) are baked from the high-poly parts, and the bone weights are computed. A group is `skin`,
  `outfit`, `hair` or `eyes`: four materials per character. The textures are 1024 (skin), 2048 (outfit), 512 (hair) and 256 (eyes) pixels, and
  each character is about 40 000 triangles on 52 bones.
- Hair: `('shell',)` is the short hair at the sides and back that shows under a cap; `('do', {...})` is a full head of hair (`garments.hair_do`:
  hairline, length at the nape, how far the shell is stretched down so a longer cut stands off the neck); `braid` adds the plait down the back
  (`garments.braid`), weighted from the head at the nape to the upper chest lower down so that it bends.

In Unity (`Editor/SurvivorImport.cs`, `PryingEyes.EditorTools.SurvivorImport.Run`, which does every FBX of `Models/Survivors/`): the FBX is
imported as a Generic rig without clips (the body animates it from code), its embedded materials are remapped by cleaned name (Blender names
them `<id>_skin.001`) to `Materials/Survivor_<id>_<group>.mat` (URP Lit with the three textures), and the prefab
`Resources/Survivors/<id>.prefab` is saved. `Editor/SurvivorShots.cs` (`PryingEyes.EditorTools.SurvivorShots.Poses`, with `-slot N`,
`-only a,b`, `-views lato,fronte,dietro`; it needs the GPU) renders contact sheets of poses (stand, walk, run, crouch, aim, shoot, reload,
downed, dead) to `hotel_build/survivors/`, which is not in git.

## Verification

SURVIVORS_PLACEHOLDER

## Traps

- The bones of the FBX do not have the axes of the Blender armature. Do not read or write a bone's local rotation by hand: use the rest-relative
  poses of `SurvivorBody`.
- Blender writes `.001` after the material names inside the FBX. The importer remaps by the cleaned name; a material that does not remap shows
  grey.
- A body on layer `Seats.FirstAvatarLayer + slot` is lit only by lights whose rendering layers include its bits: a test light needs
  `Stealth.SetLightLayers(light, 3u)`, a plain `Light` lights nothing of the survivors.
- The roughness maps baked for the previews are deleted at the end of `export`: Unity reads smoothness from the alpha of the mask.
- `PlayerNet.weapon` is the weapon *slot* (the magic replay uses it), not the index in `WeaponDefs`: the body reads `PlayerNet.gun`.
