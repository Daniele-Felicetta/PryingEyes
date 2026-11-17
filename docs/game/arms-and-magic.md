# Arms, the second hand and the grimoires

Request of 16 November 2026: make the player's arms biomechanically sensible and usable in many ways; use the second hand for
magic abilities and for magic that enhances the weapon, each with its own animation (the weapon ones touching the weapon); put
exactly one secret book room on every floor, holding at random one of two books inspired by the maintainer's two plates (perks
and design): "Perk del Dodecaedro Arcano Maledetto - Le 19 Virtù della Stregoneria" and "Potenziamenti delle Armi - I 19 Sigilli
dell'Arsenale Maledetto".

## How it plays

| Key | Controller | Action |
| --- | --- | --- |
| `3` (tap) or middle mouse | LB (tap) | Cast the selected spell with the left hand. |
| `3` (hold 0.35 s) | LB (hold) | Trace the selected seal on the weapon in hand (lasts 45 s on that weapon). |
| `6` / `7` | D-pad right / D-pad right held | Next spell / next seal. Since 0.7.2 a line under the spell and seal icons says so (`CAMBIA: 6 magia · 7 sigillo`, or the controller version), and changing says which one is now active, or that the book has only one. |
| `F` at the secret wall, then `C` | X held, then B | Open the secret wall (an eye carved in the stone, whispering), crawl through the 1.3 m tunnel. |
| `F` at the grimoire | X held | Read the book: mouse or arrows/WASD or D-pad to choose a node, click/Enter/F/A to buy (or to make an owned spell or seal the active one), Esc/B to close. |

The left hand pays with **essence**: 60 at the start, 100 maximum, 2 per second back, 3 per kill. Spells and seals cannot start while
reloading, swapping, sprinting, with the knife out, or during another gesture. On the controller the left hand is LB alone (the whole
mapping is in [controller.md](controller.md)). Holy water and the music box are now thrown with the left hand too (it takes the object,
winds back and throws; the object leaves from the palm).

To try arms, gestures and effects by hand without finding the books, launch the game with `-provamagia`: every spell and seal with
their outer nodes (except Gesto Rapido and Maneggevolezza, which speed the gestures up), full essence, 10,000 points and the AR-9
Malleus in the second slot (a rifle next to the starting P9 Pieta). It is applied again at every new game.

## The secret book room (`AnnexKind.Sacrario`)

One per floor of the survival layout, generated after the other annexes with its own random stream (towers, crypts and gardens are
unchanged). It sits outside an exterior wall of a hall: from the hall you see only wall, with an eye carved in it that glows faintly
red and whispers. Press F: the wall pushes in and slides aside, revealing a tunnel 1.2 m wide and 1.3 m high (`OpenStyle.Passaggio`),
so it is crossed crouching; the navmesh agent is 1.9 m tall, so enemies have no path inside. The chamber (5.8 x 6.6 m, or 4.6 x 5.4 m
when nothing bigger fits) has crypt stone, a barrel vault, a red pentacle cut in the floor with candles on its points, two sarcophagi,
hanging chains, the Tower's eye on the back wall and the altar with the open grimoire. The book is the Witchcraft one or the Arsenal
one, from the floor seed (the same for every co-op player). No enemy spawns inside (`AnnexRuntime.Forbidden`).

Placement (`PlanAnnexGen.TrySacrario`): a stretch of exterior wall long enough, the tunnel 40 cm away from any opening, not touching
rooms or other outdoor annexes; first with no windows in front of the chamber, then allowing them, then also the arrival hall, then a
smaller chamber and halls that already have a garden. `planlab annex 4000`: 4,000 floors, one room each, zero faults; books
2,029 / 1,971.

## The two grimoires (`Grimoire.cs`, `MagicState.cs`)

Each book is a dodecahedron of 19 nodes, drawn like the plates: 12 outer nodes on a ring, 6 inner nodes on a hexagon (each touching
the two outer nodes beside it), the supreme in the centre. Outer nodes can always be bought; an inner node needs one of its two
neighbouring outer nodes; the supreme needs four inner nodes. Prices: outer 1,250 + 250 per outer node already owned in that book,
inner 3,000 + 500 per inner node owned, supreme 9,000. Purchases are kept for the whole run (death empties the essence and turns off
the seals on the weapons, it does not take nodes).

**Book of Witchcraft** (inner nodes are the left-hand spells):

| Outer | Effect |
| --- | --- |
| Lingua di Serpe | spells +25% damage |
| Occhio del Corvo | spell range and radius +35% |
| Clessidra Rotta | spell cooldowns -30% |
| Gesto Rapido | gestures 25% faster |
| Luna Calante | enemies hit by a spell slowed 30% for 2 s |
| Radici Cave | essence back twice as fast when still or crouched |
| Patto di Sangue | missing essence is paid with health (never below 20) |
| Mietitura | +5 essence per kill |
| Eco del Rito | one spell in five is free |
| Pelle di Runa | -35% damage taken for 2.5 s after a spell |
| Pozzo Profondo | essence maximum +40 |
| Sorgente Nera | essence back 60% faster |

| Spell (cost, cooldown) | Effect | Gesture |
| --- | --- | --- |
| Fiamma Sacrilega (30, 2.5 s) | fireball, 140 damage falling to 36% at 3.5 m, burning 4 s | palm up, the flame is born in the cupped fingers; wind-up to the shoulder, push-throw with the fingers opening, follow-through |
| Catena del Fulmine (35, 4 s) | 90 damage to the target in the sight, jumps to four more within 6 m at 70% each, stuns 0.6 s | claw raised, trembling, sparks between the fingertips; a jerk forward with the fingers splayed, recoil, shake |
| Morsa di Gelo (30, 5 s) | 7 m cone: 60 damage, slow; a second hit freezes 2.5 s | palm toward the face gathering frost, then turned forward flat and pushed; held while the cone leaves |
| Sigillo di Sangue (25, 7 s) | 1.5 s drain of the target in the sight (40/s), heals half; +20 essence if it kills | horns toward the target, then a claw pulled slowly to the chest while blood flows to the palm; the fist squeezes |
| Passo d'Ombra (30, 6 s) | teleport up to 8 m ahead, stopping before walls | the hand rises with the thumb on the middle finger and snaps |
| Onda del Dodecaedro (35, 6 s) | 200 degree wave, 70 damage, throws enemies back and stuns 0.5 s | fist pulled to the chest, then an explosive open-palm thrust |
| Essenza Maledetta (60, 90 s) | 12 s: spells free with a quarter cooldown, +60% damage, 6 health/s, enemies within 3 m burn | palm before the face, red veins climbing, fingers closing slowly into a claw, the fist clenches |

**Book of the Arsenal** (outer nodes work on every weapon, inner nodes are seals traced on the weapon):

| Outer | Effect |
| --- | --- |
| Mira del Corvo | spread -30% |
| Colpo di Grazia | +40% damage to enemies under a third of their health |
| Perforazione | bullets pierce one more enemy |
| Forza d'Impatto | +15% damage |
| Raffica Mortale | fire rate +12% |
| Detonazione | one shot in sixteen explodes (2.5 m) |
| Munizioni Fantasma | one shot in eight does not use a round |
| Caricatore Esteso | magazines +25% |
| Maneggevolezza | aim and reload 20% faster |
| Ricarica Arcana | the holstered weapon reloads itself after 3 s |
| Stabilità Oscura | recoil -30% |
| Clessidra di Piombo | each kill adds 1.5 s to the seal on the weapon (up to 60 s) |

| Seal (40 essence, 45 s) | Effect on hits | Gesture on the weapon |
| --- | --- | --- |
| Danni Critici | one hit in four is critical (x2.5) | the index finger traces a circle with an eye on the left of the receiver, the red trail stays, then taps the sight twice |
| Sanguinamento | bleeding, 8/s per stack, up to 3 | the hand grips the barrel and slides back to the receiver, then opens above it and blood drips |
| Incendiario | burning 10/s for 4 s | a snap over the ejection port lights the palm, which slides along the top of the barrel towards the muzzle (36 cm at most, as far as the arm reaches); the barrel glows |
| Corrosivo | +30% damage taken from everything for 5 s, 4/s | the cupped palm fills with green ichor, tilts and pours it into the receiver, shakes off the drops |
| Elettrificato | stun 0.4 s and an arc to two neighbours at 40% | the hand grips the magazine, the fingers splay with arcs, then slaps the base up |
| Gelido | slow; four hits freeze 2 s | the flat palm presses on the left side; frost runs over the weapon from the palm |
| Arma Maledetta (60 essence, 90 s cooldown) | 15 s: double damage, all six effects, infinite ammunition | the hand grips the barrel, the weapon shudders while runes light from the muzzle back, the hand is thrown off smoking |

**Feel (0.7.2).** The charge is easier to see: the fire grows in the palm with a hot halo and embers that circle it and tighten, the
lightning gathers in a crackling ball held between the claws (above the fingers, where the hand does not hide it), the frost condenses in a
pale core with circling crystals. Each release kicks the view and widens the field of view for a moment, differently per spell
(`MagicCaster.CastPunch`, scaled by the *Oscillazione visuale* setting; the Passo d'Ombra is a strong field-of-view pull). Fire and
lightning light the room for a moment as they leave the hand. After the gesture the hand keeps a trace: smoke from the fingers after the
fire, current jumping between the fingertips after the lightning, cold vapour falling from the fingers after the frost. The red flash of
the Essenza Maledetta covers the screen less than before. Checked by eye on the `-magiatest -magiashots` photos (`hotel_build/magia/`).

Spells sent from the hand go where the crosshair points: the first enemy under it or the wall behind. A fireball hits the whole body
of an enemy (a vertical capsule from the feet to the head, `MagicFx.Body`), not only its centre.

During a spell the weapon is held by the right hand only (lowered and tilted); during a seal it is turned to show its left side and
brought closer, so the hand works on it in view. At the hip a rifle sits 62 cm from the eye and its barrel ends past a metre, out of
reach of a human arm: two-handed weapons are also brought within reach of the shoulder (the point a third along the barrel goes 44 cm
in front of and 11 cm below the eye, `OffHand.ReachTarget`, higher for the magazine seal, and turned 36 degrees to show the
left side), so the elbow bends while the hand works. A weapon with a seal glows in the element's colour (per-part emission, so frost and
runes run from the hand outwards) and emits its particles.

## Arms and hands

- **Free hand** (`HandRig`): the left glove of the per-weapon hands, rebuilt by `modelli/hotel/genera_braccia.py -- libera` in a
  neutral pose with a pivot at every joint (4 per finger, 4 on the thumb, wrist, palm, back). The second version has no rubber
  knuckle plate (it looked like a paw), knuckle padding and tendons under the leather, and only a strap with a tab around the cuff;
  it is remeshed at 1.3 mm and decimated to about 29,000 vertices. At runtime the glove is skinned on 17
  bones (palm, 15 phalanges, cuff) with weights from the distance to the phalanx capsules, blended at the joints and between
  neighbouring fingers. Bones and skin are built in the hand root's own frame, so they come out the same on every build and the skin
  cached per mesh fits every hand built later. The cuff behind the wrist belongs to the forearm, not to the hand: its bone sits on the wrist, along the
  forearm and turned with the hand around it (`HandRig.UpdateCuff`, after the arm solve), and the skin passes from palm to cuff
  across 3 cm around the wrist, so the hand bends at the wrist. Before, the cuff was rigid with the palm and the sleeve hem followed
  the hand: the bend happened 6 cm back inside the sleeve and the hand looked like it had a long neck. Poses are anatomical angles (`HandPose`: spread, MCP, PIP, DIP; thumb abduction, opposition, MCP, IP) limited to human
  ranges (MCP -30..92, PIP 0..110, DIP never more than PIP + 12, no fanning with a closed fist). Fifteen named poses: relaxed, open,
  flat, fist, claw, cup, point, horns, snap-ready, snapped, pinch, grip, blessing, thrust.
- **Textures painted on the surface** (`modelli/hotel/genera_texture_braccia.py`): the articulated glove (with its strap) and the
  neutral sleeve have their own UV atlas (`genera_braccia.py -- libera / -- manica`: smart projection of the glove and strap together
  with one texel density; the sleeve unrolled from its inner seam) and the stages also write `braccia/<name>_uv.npz`, the surface
  triangle by triangle with points, normals, atlas coordinates, a cavity value per vertex and, for the sleeve, the length from the hem
  and the turn from the inner seam. The texture script rasterises the atlas, knows for every texel where it lies on the hand, and
  computes colour, height and smoothness there: dark brown pebbled leather on the back, suede on the palm and the palm side of the
  fingers, the seam between them along the sides of every finger and around the fingertips with a row of stitches, the three
  stitched points on the back, the stitched outline of the knuckle pads, soft wrinkles over and under the joints and the palm creases,
  wear on knuckles, fingertips and the side of the index, scratches, grime in the creases, a little dried blood, the cuff's hem and
  wrist seam with their stitches, the gathered elastic under the wrist and the nylon strap with stitched edges; on the sleeve faded
  canvas on the outer side, the topstitched cuff band, the felled inner seam, an elbow patch, ridges worn and folds darker, grime and
  mud towards the wrist. The normal map comes from the height with finite differences in texel space and each triangle's u and v
  directions; islands are grown by 16 texels for the mipmaps. Unity materials `Mani_GuantoLibero` and `Mani_ManicaLibera`
  (`ManiImport.Atlas`: base, normal, mask with metal, occlusion and smoothness; the sleeve keeps the jacket's twill as a tiled detail
  map, `tela_dettaglio_base.png` and `Hotel/giacca_normal.png`). The per-weapon gloves that remain (the knife, `-presavecchia`) are tinted
  to the same average colour.
- **Invisible swap**: `genera_braccia.py -- pose` writes, weapon by weapon, where the joints of the left hand wrapped on the handguard
  are (`Resources/ManiArmi/presa_sinistra.json`); Unity finds the author-to-Unity axes by itself from the wrist, elbow and shoulder
  empties and puts the free hand exactly on the wrapped glove, with the same finger angles, before switching.
- **Right hand** (`GripHand`, with the realistic hold): the same articulated hand, mirrored (`HandMirror`: local positions and
  rotations conjugated by the reflection, mesh copies with x negated and triangles flipped), put exactly where the modelled right
  glove of each weapon was (`presa_destra.json`, also from `-- pose`, with the index finger's trigger pose). The modelled glove and its
  sausage fingers are switched off: both hands are the same glove. The index moves from along the frame to the trigger and closes a
  little more on the shot; the other fingers tighten on the grip with the recoil. The right arm's anatomy is measured like the left.
- **Arm** (`ArmRig`): a forearm twist bone takes half of the hand's roll around the forearm, so supination no longer wrings the cuff;
  the elbow stays between 28 and 176 degrees; with the free arm the elbow swivels around the shoulder-wrist axis to keep the wrist
  comfortable, scored with the real joint ranges when the hand's frame is known (pro-supination, flexion-extension and radial-ulnar
  deviation of the wrist) and kept low: an elbow higher than 8 cm below the shoulder or closer than 27 cm to the eye costs, because a
  real arm turns the forearm before it lifts the elbow in front of the face. The swivel follows a critically damped spring integrated
  exactly, stable at any frame time (the explicit one diverged past 0.13 s and a hitch threw the elbow in front of the eyes). During
  gestures the hand is brought back within human ranges by turning it about the wrist (`ClampHand`: pro-supination 85, flexion 75,
  deviation 30 degrees), as a Humanoid's muscle limits do. While the articulated hand is shown the sleeve hem follows the forearm and
  does not twist (`FreeSleeve`); with the modelled glove it follows the hand, whose rigid cuff it must cover. The free arm has human
  proportions: the shoulder 19 cm to the side of and 24 cm below the eye, protracting up to 12 cm on long reaches, and a 30 cm upper
  arm (the sleeve's upper bone is scaled to it).
- **Neutral sleeve** (with the realistic hold): one sleeve for every weapon, `Resources/ManicaLibera` from `genera_braccia.py --
  manica`, modelled on the real proportions (30 cm upper arm, 26 cm forearm, elbow at 140 degrees) with diamond-shaped compression
  folds, spirals and deeper elbow folds; mirrored for the right arm. The per-weapon sleeves were modelled on the fake arm (upper arm up
  to 75 cm) and on a real arm were stretched and twisted a lot. Its hem bone is moved to the weapon's wrist after binding, so the
  sleeve reaches the glove's cuff wherever the hand holds the weapon.
- **The body moves the shoulders** (`ArmRig.BodyMotion`): breathing lifts both shoulders, walking swings them alternately forward and
  back with the step, the recoil pushes the right shoulder back and the left follows a little. The shoulders move with the torso and
  the weapon with the hands: elbows and wrists live on what lies between. The left hand regrips the handguard every 4-8 seconds and
  tightens at every shot (`OffHand.Idle`); the right fingers tighten with the recoil.
- **Realistic hold** (every weapon but the knives, `Weapon.RealHold`; `-presavecchia` brings back the old one): the weapon at human
  distance (the rifle's right hand 31 cm from the eye instead of 63), the torso turned like a shooter's (35 degrees with long guns, 25
  with submachine guns, 10 with pistols; it straightens while the left hand works), body shoulders and hanging elbows on both arms.
  `Weapon.HoldOf` gives each weapon its pose: `ar9_malleus` and `p9_pieta` were tuned by hand (`RealHoldTable`), the others take their
  class's (pistols and revolvers like the P9, submachine guns in between, everything else like the AR-9). Straight stocks without a
  pistol grip (lever, bolt, double barrel, pump: `StraightStock`) sit 6 cm lower, turned 13 degrees inwards, with the right elbow
  further out: the right hand holds the stock's neck palm down, and with the rifle's pose the forearm would have to turn past 90
  degrees. On long handguards the left hand slides back to 28 cm in front of the grip, where a real arm reaches. The per-weapon left
  glove was modelled for the old fake shoulder (forearm arriving horizontally from the left) and on a real arm would need 120-180
  degrees of forearm rotation, so on two-handed weapons the articulated hand replaces it for good (`OffHand.SetNaturalRest`): palm
  under the handguard, hand diagonal with the thumb along the left side, forearm rising from below. At rest all 30 weapons keep both
  wrists within human ranges (`-armview`: pro-supination up to 90, flexion-extension up to 85, deviation up to 35 degrees).
- **Aiming** (0.7.1, `ArmRig.AdsSwivel`, `AdsElbowRise`, `AdsShoulder`, set per weapon class in `Weapon.cs`): before, the right elbow
  stayed low as at rest while the weapon rose to the eye, and the right wrist went out of human ranges on every weapon (deviation up to
  120 degrees). Now, on weapons with a stock, the right shoulder goes under the stock (6 cm towards the centre, 16 up as the head
  drops onto the stock, 6 forward) and the elbow may turn freely around the shoulder-wrist axis, out to the side if needed; the left
  shoulder moves 12 cm forward and 10 up towards the handguard. Pistols keep their shoulders. The elbow search weighs the joint ranges
  and, while aiming, how much of the arm enters the middle of the screen (`ArmRig.ArmOcclusion`, which counts the sleeve's thickness:
  a forearm a few centimetres from the eye fills the screen even when its axis is off to the side). The search remembers the last
  elbow only lightly while aiming (`AdsStickiness`), otherwise the elbow stayed stuck low in a local minimum, and the right arm now
  has the same critically damped spring as the left. The values were chosen with `-armview -adsprobe` on all 30 weapons.
- **Gestures** (`OffHand`, `OffHandAnims`): keys of a contact point of the hand (wrist, palm, index, middle, thumb, little finger) in
  camera space, weapon space with its parts (muzzle, magazine and its base, ejection port, sight, handguard, receiver, a point along the
  barrel or on the left side, measured from the parts' bounds by `WeaponSurface`), the rest grip, or below the frame; the wrist follows
  a Catmull-Rom curve, rotations and poses blend with each key's easing; weapon keys and timed events.
- **Reloads done by the left hand** (`ReloadAnims`, `Weapon.ReloadHands.cs`; with the realistic hold, `-ricaricavecchia` brings back the
  old one): every reload style has its choreography, written with the same keys but tied to the reload clock of `WeaponState`
  (`OffHand.PlayExternal` and `Seek`), so perks that shorten a reload shorten the gesture and the state's sounds fall on it. The weapon is
  brought to the chest and canted (`OffHand.OffsetToBring` moves the point the hand works on to a fixed place in front of the chest,
  where the left shoulder reaches and the screen shows it). Magazine: the hand leaves the handguard, takes the magazine from the front
  (palm on its front, fingers round the side), strips it along its axis, drops it when empty or takes it to the belt, comes back with
  the new one, aligns it under the well, inserts it, slaps the base with the palm and, when empty, pulls a charging handle that is in
  reach (in front or on the side) or slaps the bolt catch above the well. The magazine travels in the hand: it is attached to the palm
  in the frame of the insert pose, so it lands exactly in its seat, and inside the well it slides along its axis; when empty a physical
  copy falls (`DropMag`) and the real one reappears in the hand when it comes back from the belt. Pistol: the empty magazine drops
  by itself, a tactical one is caught; the new one goes in from below with a palm slap and the left thumb presses the slide stop.
  Tube (pump, lever): one shell at a time from the belt, held between thumb and fingers (`AttachProp`), pushed into the port.
  Double barrel: the barrels open around the hinge (`Weapon.breakHinge`), the extractor throws the empty shells, two new shells go in
  from above under the palm, the hand closes the barrels from the forend. Revolver: the fingers push the cylinder out, muzzle up and
  the thumb on the ejector rod, a speedloader from below, the fingers close the cylinder. Belt: the hand lifts the cover, changes the
  box like a magazine, lays the belt and closes the cover with a slap. Weapon switch and the knife still interrupt a reload
  (`Weapon.GestureBusy` counts only spells, seals and throws).
- **The right hand works the action** (`Weapon.WorkHand.cs`): with a turn bolt, after every shot (and after an empty reload) the right
  hand leaves the grip, takes the bolt knob between thumb and index, follows it up, back, forward and down (the handle is moved by
  `AnimateCycle` with the same times) and goes back to the grip and the trigger; with a lever the hand turns with the lever around its
  pivot (60 percent of it) and the fingers open a little in the loop.
- **Idle**: besides regripping every 4-8 seconds, every 10-18 seconds without shooting the left hand drums on the handguard, the
  fingers lifting and falling from the little finger to the index, twice (`OffHand.Idle`).

## Effects (`MagicVfx`, `VfxTextures`)

Spell and seal effects are Unity particle systems shared by kind, in world space, drawn by `Resources/Shaders/MagicParticle.shader`
(in `Resources` so that `Shader.Find` finds it in a build). One premultiplied blend does both additive light (fire, sparks, frost)
and covering matter (smoke, blood); colours above 1 go to the bloom; flipbooks blend between frames; alpha erosion (the vertex alpha
is the threshold under which the texture disappears) makes a flame burn away from its edges instead of fading; particles are soft
against the scene depth and fade next to the eye; Unity's fog is applied by hand (`_PEMagicFog`), so the shader has no keyword
variants to keep. The textures are made at run time in a worker thread when the player is created (`VfxTextures.Begin`): an
animated flame and an animated smoke puff (64 frames of 128 px each, looping, from a periodic Perlin noise), glow, spark, shock
ring, ice shard, droplet, sixteen runes, the circle of the Dodecahedron (rings, ticks, a band of twelve glyphs, the {12/5} star, the
inner dodecagon and the Tower's eye), the lightning profile and the blood ribbon.

Kinds: small fire (hand and weapon) and big fire (fireball, explosions, burning enemies), smoke, sparks that bounce off walls,
wandering embers, glows, frost mist, ice shards, droplets that die where they land, runes, facing and ground shock rings. Lightning is
a jagged polyline (midpoint displacement) with branches, redrawn every 35-55 ms while it lives, white core and coloured halo, thin
where it leaves the finger. The circle of the Dodecahedron opens in front of the hand for the frost cone, the blood seal, the wave,
the Cursed Essence and every seal; the finger that traces a seal leaves a glowing trail; the Blood Sigil draws a ribbon of blood
flowing from the enemy to the palm. While a gesture glows, a flickering light lights the world around the hand and a second, small
one reaches only the view model, so the fire in the palm lights the glove and the weapon. The fireball is a white-hot core in an orange glow wrapped in flame tongues that trail behind,
with smoke and embers, and explodes in a flash, a swelling fireball, rising black smoke, sparks, embers and a ground ring. Cursed fire
uses its own red-orange tint (`MagicVfx.CursedFire`): the interface crimson has some blue and made the fire pink. `MagicFx` keeps its
API (flame, glow, sparks, mist, drip, arc, burst, ring), so weapon auras and enemy statuses use the new particles too; without the
shader (a scripts-only build over an old player) it falls back to the `Fx` quads.

## Code map

`Scripts/Braccia/`: `HandPose.cs`, `HandRig.cs`, `GripPoses.cs`, `GripHand.cs`, `HandMirror.cs`, `OffHand.cs` (and `WeaponSurface`),
`ReloadAnims.cs`. `Scripts/ArmRig.cs`, `Scripts/Weapon.ReloadHands.cs`, `Scripts/Weapon.WorkHand.cs`.
`Scripts/Magia/`: `Grimoire.cs`, `MagicState.cs`, `MagicCaster.cs` (input, effects, the `Magic` facade), `OffHandAnims.cs`, `MagicFx.cs`
(effects, `Fireball`, `EnemyStatus`, `WeaponAura`), `MagicVfx.cs` and `VfxTextures.cs` (particles, lightning, circles, textures),
`Resources/Shaders/MagicParticle.shader`, `SoundMagia.cs`, `HUD.Magia.cs`, `GrimoireIcons.cs`, `GrimoirePage.cs`,
`GrimoireUI.cs`, `GrimoireAltar.cs`, `MagiaTest.cs`. `Scripts/Weapon.Sigilli.cs` (seals on hits). `Scripts/Proc/PlanAnnex.cs`
(placement), `Proc/FloorBuilder.Sacrario.cs` (geometry), `Proc/AnnexRuntime.cs` (the secret wall). Assets:
`Models/Mani/libera/mano_libera.fbx` and `manica_libera.fbx`, `Resources/ManoLibera.prefab` and `ManicaLibera.prefab` (`ManiImport.Run`,
`-mani libera` / `-mani manica`), `Resources/ManiArmi/presa_sinistra.json` and `presa_destra.json`, `Textures/Braccia/` (the atlases
and the detail) with the materials `Models/Mani/Materials/Mani_GuantoLibero.mat` and `Mani_ManicaLibera.mat`. The Blender stages of
`modelli/hotel/genera_braccia.py`: `libera` (the hand), `manica` (the neutral sleeve), `pose [id ...]` (both grip tables), `vedi`
(previews of hand and sleeve in `braccia/render/`, with the atlases when they exist); then `python -I genera_texture_braccia.py
[guanto] [manica] [dettaglio]` paints the atlases (about two and a half minutes).

## Co-op

Book purchases, essence, spells and seals are personal, like perks. Damage goes through `Enemy.Damage` with the new source
`DamageSource.Magia` (guests send hit claims as for bullets); slows, stuns and freezes apply only on the host, which simulates the
enemies. Spell effects are not shown on the other players' screens. The plan gained an annex and a damage source, so the protocol
revision is `pe-coop-26`.

Split screen ([split-screen.md](../multiplayer/split-screen.md)): every player has its own caster and grimoire state, and the
caster runs inside its player's seat (`Seats.Enter`, keys through `SeatInput`). The grimoire page covers the whole window and
belongs to the player who opened it (purchases, essence and points are theirs); while it is open the game pauses for everybody,
because it blocks every player's commands.

## Verification

- `dotnet run --project tools/planlab -c Release -- annex 4000`: one secret book room on every floor, zero faults, deterministic.
- `-batchmode -autotest -nofire -noenemies -magiatest [-magiashots] -seed S -floor F` (`MagiaTest.cs`): the secret room (one per
  floor, wall usable with F, closed and standing it cannot be crossed, crouched it can, no enemy path, the book of the floor), both
  books (rules, prices, all 38 nodes), the seven spells and seven seals on a rifle and a pistol (they start, the free hand takes the
  wrapped glove's place and gives it back, effects arrive, the elbow stays in range, the wrist and forearm stay within human
  ranges) and the left-hand throw. Results in `hotel_build/magia/magia.txt`, photos with `-magiashots`.
- Diagnostic flags of the same test: `-magiaside` (with every photo a side and a top view of the arms with the solved joints, a
  close-up of the left wrist where sleeve and glove cuff meet, and the measures in the report), `-magiazoom` (two close-ups of the
  left hand with the real scene and effects: from the eye with a telephoto lens and three-quarters from the left), `-vfxdump` (the
  effect textures as images), `-armview [-armids a,b] [-presavecchia] [-adsprobe] [-armdump]` (only the rest and aiming pose of
  every weapon, with arm measures; at rest it checks both wrists against the joint ranges, in aim that the wrist does not bend out of
  range (flexion 85, deviation 52) and that no arm covers the middle of the view; `-adsprobe` re-solves the aiming arm with many
  combinations of the aiming parameters and ranks them per weapon class, `-armdump` lists the renderers of the first-person model;
  with `-magiazoom` also a close-up of each hand from the eye),
  `-reloadview [-armids a,b]` (for every weapon an empty and a tactical reload done by the hand, photographed at thirteen fixed points
  with side and top views when empty, then the manual cycle after it; the report has, for every photo, the key the hand is going to,
  where the palm should be and where it is). `-seed 1000` gives the lit hall used for the photos.
- The gesture photos are taken at the end of the frame: Unity skins sleeves and gloves after `LateUpdate`, and a photo taken earlier
  in the frame mixes the skinned meshes of the previous frame with the bones and rigid parts of this one (the glove's cuff looked
  detached from the sleeve, and a sleeve could look like a sheet near the eye).

## Known limits

- Balance (essence, prices, damage, durations) is untested by a human player.
- In co-op the other players' spells start from the left hand of their mannequin, which has no weapon: a seal shows as a glow of
  its colour on the mannequin's right hand while it lasts and that weapon is in hand (see
  [../multiplayer/README.md](../multiplayer/README.md)).
- The gestures were authored by numbers and checked on photographs, not motion-captured.
- The view model shares the world camera's field of view (75 degrees by default), so a forearm reaching forward 30 cm from the eye
  looks large.
- Aiming on the four straight-stock weapons (`lv_predicatore`, `bolt_rosario`, `sg_cappellano`, `sg_breacher12`): the right hand's
  modelled hold on the stock asks the forearm for 130-170 degrees of rotation (a real one gives about 90), and no elbow keeps both the
  wrist in range and the view clear, so their right arm keeps the 0.7.0 aiming pose; the fix is a new hold for those four in
  `genera_braccia.py`. On pistols the aiming wrist deviation is 45-51 degrees (the real ulnar limit is about 45), the price of keeping
  the arms out of the view.
- The reloads of all 30 weapons were checked on photographs (0.7.1). The double barrel's breech and the machine gun's feed cover are
  brought lower than before, so the left forearm works in the lower part of the screen; on the belt-fed `lmg_campana249` it still
  crosses the lower left while lifting the cover. Big models (the flamethrower's tank, the scoped rifles) fill one side of the screen
  while they are at the chest. On weapons with unusual magazines (the flamethrower, the alien spine launcher, the eye rifle) the hand
  goes where the magazine's bounds say.
- With a scripts-only build over an older player the new textures and materials are missing: they need a full build.
