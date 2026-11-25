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
| `6` / `7` | D-pad right / D-pad left | Next spell / next seal (the seal icon is the left one in the HUD, like the D-pad button; until 16 November 2026 the seal was *D-pad right held*). Since 0.7.2 a line under the spell and seal icons says so (`CAMBIA: 6 magia · 7 sigillo`, or `CAMBIA: ← sigillo · magia →` on the controller), and changing says which one is now active, or that the book has only one. |
| `F` at the secret wall, then `C` | X held, then B | Open the secret wall (an eye carved in the stone, whispering), crawl through the 1.3 m tunnel. |
| `F` at the grimoire | X held | Read the book: mouse or arrows/WASD or D-pad to choose a node, click/Enter/F/A to buy (or to make an owned spell or seal the active one), Esc/B (or right click) to close. The game does not stop while you read: only you stand still. |

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

Each book is a dodecahedron of 19 nodes, as in the plates: 12 outer nodes on a ring, 6 inner nodes on a hexagon (each touching
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
| Danni Critici | one hit in four is critical (x2.5) | the index finger, under the weapon, stabs the metal three times (receiver, middle, barrel); each stab lights a red crack under the fingertip that climbs the flank, then the cracks spread over the whole weapon and beat |
| Sanguinamento | bleeding, 8/s per stack, up to 3 | the hand grips the barrel from below and wrings it back towards the receiver: dark glossy blood rises from the metal where it passes and drips from the underside; under the receiver the hand cups the drops |
| Incendiario | burning 10/s for 4 s | a snap under the receiver lights the palm (facing up); the flat palm touches the underside and slides forward under the barrel (26 cm at most, as far as the arm reaches): the metal heats from below behind it and the heat climbs the flanks in tongues |
| Corrosivo | +30% damage taken from everything for 5 s, 4/s | the cupped palm rises under the receiver and fills with green ichor; the acid climbs the flanks from the cup (bubbling sludge) and drips from the underside while the hand slides under the barrel |
| Elettrificato | stun 0.4 s and an arc to two neighbours at 40% | the hand grips the magazine, the fingers splay with arcs, then slaps the base up: veins of current climb the weapon from the magazine base |
| Gelido | slow; four hits freeze 2 s | the flat palm presses up under the receiver and slides forward under the barrel: frost grows from the underside up the flanks in tongues, cold mist falls from below |
| Arma Maledetta (60 essence, 90 s cooldown) | 15 s: double damage, all six effects, infinite ammunition | the hand grips the barrel from below, the weapon shudders while black fire with red cracks climbs from the bottom, the hand is thrown off smoking |

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
left side), so the elbow bends while the hand works.

**The seal on the weapon and on the shots (17 November 2026).** Until then a sealed weapon took one flat tint of the element's colour on
every metal part, with a few flames floating above it, and the bullets looked like the bullets of any weapon. Now the seal acts from
below and shows on every shot:

- *From below.* The hand works under the weapon (`Part.Under`: palm up, a few millimetres under the underside, in front of the magazine)
  and the element is lit there: the metal heats from the underside and the heat climbs the flanks, so a weapon ends hottest at the
  bottom and coolest on top. You see the front of the heat go up (a bright, ragged edge with tongues), not a colour switch: fire is
  glowing metal with a flowing flame pattern (white-yellow at the bottom, orange, dull red on top), frost is a creeping coat of
  crystals with glints, lightning is flickering veins with pulses running up them, acid is bubbling green sludge, blood is a glossy dark
  coat, the critical seal is red cracks that beat, the Cursed Weapon is black fire with red cracks. Particles come from the underside
  too: flames lick the flanks and embers rise, cold mist and drops fall, arcs jump along the bottom edge. A weak light of the element's
  colour sits below and to the left of the weapon and lights the glove (only the view-model layer).
- *Where the heat comes from.* While the hand slides under the weapon it deposits heat where the palm is (`WeaponAura.Deposit`, every
  frame); at the end of the gesture the heat rises everywhere (`Flood`, 0.7 s), so the front reaches the top. The magazine seal starts
  at the magazine base when the palm slaps it, the critical seal at the fingertip at each stab.
- *The shots.* A sealed weapon shows its element on every shot, not one in three: a flash of the element at the muzzle (the weapon's own
  muzzle flash and its light take the colour, and the weapon flares for a moment), a short thick dart of the element's colour along the
  line with what the element leaves where the bullet passed (flame tongues and embers, ice shards and mist, arcs of current, drops of
  acid or blood, red sparks, black fire), a burst of the element where it lands and its mark on the wall (scorch, frost, green stain,
  blood, black fire). Shotgun pellets thin the effects out. The statuses on the enemy are still `Weapon.SigilHit`'s.

### The open book (`GrimoireUI`)

Since 17 November 2026 the open book is the maintainer's plate itself: `Resources/Grimori/tavola_arsenale.bytes` and
`tavola_stregoneria.bytes`, PNGs stored as text assets and decoded by `GrimoirePage` (Unity leaves them alone), prepared from the two
sources in `modelli/grimori/sorgenti/` by `modelli/grimori/prepara_tavole.py`. The script empties the 19 medallions (the icons baked
into the art did not match the game's nodes), rebuilds the three inner medallions of the Witchcraft plate that the ram-headed demon hides
(six identical thorn wreaths on a hexagon around it) and feathers the border into transparency. `tavole.json` keeps the centre and the
radius of every slot in plate pixels: outer 0..11 clockwise from the medallion left of the top one, inner 0..5 clockwise from the top,
the supreme at the centre, where the plate already has its emblem (the skull with the rifles, the demon). Without the files the book
still works, on a dark placeholder with the same layout.

On the plate the game draws, for every node, the icon engraved in gilded bronze (`GrimoireIcons.Engraved`: the signed-distance shapes of
the HUD icons with a bevel lit from the top left, a highlight and a soft shadow), a glow and a ring for its state (owned: red; one you can
buy: amber; closed: dark and dim; the active spell or seal: a gold ring), a gold ring that breathes on the chosen node, and thin lines
between nodes: red between two owned ones, amber between an owned one and one you can buy (the rest is the plate's own art). The supreme
has no icon: its emblem is dimmed until you own it. Beside the plate a parchment card (`GrimoirePage.Card`) shows the chosen node: icon,
name, class, effect, motto, price or what is missing, the key to press, the message after a purchase and, at the foot, points, essence
and keys.

The book is a child of the reader's HUD, so it lives inside the reader's part of the screen and scales with it. It takes at most 88% of
the height and arranges itself in the room it has: plate and card side by side in a wide area (1280x720: an 844x633 plate and a 330 wide
card), one above the other in a tall one (each half of a two-player split screen). The game stays visible around it, only dimmed. `Esc`,
`Tab`, `Backspace`, right click or B close it; the pause, a hit or the reader's death close it too.

The 3D book on the altar uses the same art: `altare_*.bytes` (the open book of the plate, cropped, as a JPEG) and `luce_*.bytes` (a mask
of its hot reds, which glow).

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

## The seal on the weapon (`WeaponAura`, `WeaponInfusion.shader`, `SigilFx`)

Every lit piece of the first-person weapon gets a second renderer (a child called `Sigillo`, the same mesh; glass and scope
submeshes get a blank material) drawn 0.6 mm off the surface by `Resources/Shaders/WeaponInfusion.shader` (in `Resources` for
`Shader.Find`, so it needs a full build). Its blend is premultiplied and it writes no depth: one output does both additive light (fire,
cracks, veins) and covering matter (frost, sludge, blood). The shader does not know the weapon: `WeaponAura` gives it, every frame, a
48-texel half-float profile along the weapon's length (R: height of the underside, G: height of the top, both from `WeaponSurface`;
B: the heat level) and the world-to-weapon matrix. For each fragment `h` says how high it is between underside and top at that point
of the length (0 under, 1 on top), `front = level - h` is positive where the heat has arrived, and every element is a function of
`front` and `h` plus noise in weapon space (so the pattern sticks to the metal): fire is a temperature (dark red on top, amber at the
bottom, a bright thin line on the front, hot pockets low down), frost a coat that is thicker at the bottom and ends in ragged tongues,
lightning veins with pulses running up, acid dark mud with glowing drips and bubbles, blood a dark wet coat with streaks and gloss,
the critical seal and the Cursed Weapon cracks on the edges of a cellular (Voronoi) pattern. The coatings (blood, acid, frost) are lit
by one fixed light in the eye's space (up, left and towards the viewer) with a narrow highlight that breaks up in patches, so the
weapon keeps its shape and looks wet instead of a flat silhouette; fire, cracks and veins are their own light and ignore it. The scene
is HDR with ACES, a bloom threshold of 1 and a high exposure in the lit halls (about 2.5 times, measured on the photographs), so
colours above 0.5 already fade towards yellow-white and a coat of 0.1 already looks bright: the body of each look stays between 0.02
and 0.6 and only the thin lines (front, veins, cracks) go over 1. Green weighs three times red in brightness, so the acid wants about
a third of the blood's value for the same look.

The level is written by the left hand: while the palm slides under the weapon `MagicCaster.HeatTick` calls `WeaponAura.Deposit` at the
palm every frame (a Gaussian along the length), the contact events (`stab`, `cut`, `trail`, `pour`, `press`, `runes`, `slap`) start the
element at the touched point (`WeaponAura.Set`), and the `cast` event calls `Flood` (0.7 s; 0.9 s for the Cursed Weapon) so the front
climbs the flanks to the top. At rest (`Set` without a point) the weapon is lit at the steady level, hotter at the bottom than on top.
`WeaponAura.Emit` sends the particles from the underside and the lower flank where the heat is (small flame tongues and embers that
hug the metal, mist and shards falling, arcs along the bottom edge, drips), and one weak point light of the element's colour below
and to the left lights the glove (view-model layer only). `Weapon.DropMag` removes the skin from the dropped magazine copy.

`SigilFx` is what a sealed weapon shows on each shot (`Weapon.SigilShot`, `Weapon.ShootRay`): the element's flash at the muzzle, the
weapon's own flash and light tinted, a short thick dart plus a thin line of the element's colour that fades in 0.2 s and the trail
particles of the element (`Tracer`), and at the end point a burst, a short coloured light on the world and the mark on the wall
(`Impact`). Shotgun pellets draw the effects on a third of the pellets.

The gestures that work under the weapon (`Part.Under`, `WeaponSurface.UnderAlong`, `OffHandAnims.Un`) use their own presentation of
the weapon (`UnderP`, `UnderR`: turned on its side and raised so that the underside, the hand and the climbing heat are in view; with
the realistic hold the reach target is not used, only this offset). `-sigilpose roll,yaw,pitch,x,y,z` and `-sigilreach x,y,z`
change them from the command line for tuning without a rebuild.

## Code map

`Scripts/Braccia/`: `HandPose.cs`, `HandRig.cs`, `GripPoses.cs`, `GripHand.cs`, `HandMirror.cs`, `OffHand.cs` (and `WeaponSurface`),
`ReloadAnims.cs`. `Scripts/ArmRig.cs`, `Scripts/Weapon.ReloadHands.cs`, `Scripts/Weapon.WorkHand.cs`.
`Scripts/Magia/`: `Grimoire.cs`, `MagicState.cs`, `MagicCaster.cs` (input, effects, the `Magic` facade), `OffHandAnims.cs`, `MagicFx.cs`
(effects, `Fireball`, `EnemyStatus`), `WeaponAura.cs` (the weapon's skin and its particles), `SigilFx.cs` (muzzle, tracer and impact of
a sealed shot), `Resources/Shaders/WeaponInfusion.shader`, `MagicVfx.cs` and `VfxTextures.cs` (particles, lightning, circles, textures),
`Resources/Shaders/MagicParticle.shader`, `SoundMagia.cs`, `HUD.Magia.cs`, `GrimoireIcons.cs`, `GrimoirePage.cs`,
`GrimoireUI.cs`, `GrimoireAltar.cs`, `MagiaTest.cs`. `Scripts/Weapon.Sigilli.cs` (seals on hits). `Rules/Plan/PlanAnnex.cs`
(placement), `Proc/FloorBuilder.Sacrario.cs` (geometry), `Proc/AnnexRuntime.cs` (the secret wall). The book art: `Resources/Grimori/`
(`tavola_*.bytes`, `altare_*.bytes`, `luce_*.bytes`, `tavole.json`), made by `python -I modelli/grimori/prepara_tavole.py` (needs numpy,
opencv-python and Pillow; the sources are in `modelli/grimori/sorgenti/`; run it again only if a plate changes). Assets:
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
caster runs inside its player's seat (`Seats.Enter`, keys through `SeatInput`). Every player has their own open book (`GrimoireUI` keeps
one per seat, under that seat's HUD), drawn inside that player's part of the screen. It blocks only the reader's commands:
`GameMenu.Overlay` belongs to the seat (`Seat.overlay`, saved and restored by `Seats.Install`, set with `Seats.SetOverlay`) and the
reader's controller is the only one silenced (`PadControls.BlockedFor`). The others keep playing, time does not stop, and their HUDs
and maps are untouched; the reader's own map is hidden under the book. Purchases, essence and points are the reader's; the reader's
controller (or the keyboard and the mouse, for player 1) drives the book. In the online co-op the book works the same way: it does not
stop the game and takes at most 88% of the window.

## Verification

- `dotnet run --project tools/planlab -c Release -- annex 4000`: one secret book room on every floor, zero faults, deterministic.
- `-batchmode -autotest -nofire -noenemies -magiatest [-magiashots] -seed S -floor F` (`MagiaTest.cs`): the secret room (one per
  floor, wall usable with F, closed and standing it cannot be crossed, crouched it can, no enemy path, the book of the floor), both
  books (rules, prices, all 38 nodes), the seven spells and seven seals on a rifle and a pistol (they start, the free hand takes the
  wrapped glove's place and gives it back, effects arrive, the elbow stays in range, the wrist and forearm stay within human
  ranges) and the left-hand throw. Results in the log and in `unity_logs/magiatest/result.json`, photos with `-magiashots` in `hotel_build/magia/`. For every seal it also checks
  that the weapon is lit (`WeaponAura`: the element, its skins, the heat above 0.75 once the gesture ends) and that the shots drew
  their muzzle flash, tracer and impact (`SigilFx.Shots`, `Tracers`, `Impacts`).
- `-sigilliview [-sigilliweapons a,b] [-sigillinodes 14,17] [-sigillishots N] [-magiazoom]` with the same flags (`MagiaTest.SigilView`):
  only the seven seals, to judge how they look. For each seal the gesture in six photos, the weapon from the side and from below (close
  ups), a burst on a target with photos two frames after a shot, and four shots at a wall 3-9 m away (an enemy-free dark backdrop), the
  second in slow motion (`Time.timeScale` 0.04) with one photo of the dart in flight and one at the impact. Photos in
  `hotel_build/magia/sg_<weapon>_<node>_*.png`. A good set is `-sigilliweapons ar9_malleus,p9_pieta` (a rifle and a one-handed pistol).
- `-magiabooks` (with the flags above) stops after the room and the two books, about two minutes instead of the full run. For each
  book it opens the real `GrimoireUI` and checks: the real plate and its 19 slots are loaded (the report gives its size and mip levels), the book stays inside its
  area and leaves the game visible, time keeps running, an inner node cannot be bought without an outer neighbour, the supreme is closed
  until four inner nodes are owned, the prices (1,250, 1,500, 3,000 and 9,000), the purchase of all 19 nodes and that closing the book
  gives the commands back (44 of 44 on 17 November 2026). Photos `libro_<book>_{vuoto,quasi,pieno}.png` (nothing owned, all but the
  supreme, everything) and `sacrario_altare.png` go to `hotel_build/magia/`.
- The book of one player in the split screen is covered by `-seatstest`
  ([split-screen.md](../multiplayer/split-screen.md#verification)): only the reader's book is open, only the reader is blocked, time
  runs, the book fits inside the reader's part, another player walks while the reader stays still, and a controller drives only its
  own player's book.
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
- With a scripts-only build over an older player the new textures and materials are missing: they need a full build. The seal
  shader (`WeaponInfusion.shader`) is in `Resources`, so without it the weapon simply shows no skin.
- The seven looks of the seals were tuned on photographs of two weapons (the rifle `ar9_malleus` and the pistol `p9_pieta`) in one
  lit hall (`-seed 1000`), by eye, and not played by a human. Other weapons use the same shader with the profile of their own shape
  (`WeaponSurface`), so a weapon whose top and underside are far from the rifle's (the flamethrower's tank, the scoped rifles) may
  show the climbing front less cleanly.
- The exposure of the looks is tuned for the hotel's ACES tonemapping and bloom threshold 1 (`HotelVolume.asset`); if either changes
  the bodies of the looks (0.05 to 0.6) and the thin lines (over 1) need to be tuned again.
- The cost of the skin was not measured: it adds one draw per lit piece of the weapon, with noise in the fragment shader (the critical
  and Cursed looks use a 27-cell cellular pattern) on the pixels of the weapon only.
- The seal on the weapon and on the shots is drawn by the shooter's own game: in co-op the other players do not see the skin or the
  coloured shots of a remote player, only the glow on the mannequin's hand.
