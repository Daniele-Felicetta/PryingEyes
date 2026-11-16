# Arms, the second hand and the grimoires

Request of 16 November 2026: make the player's arms biomechanically sensible and usable in many ways; use the second hand for
magic abilities and for magic that enhances the weapon, each with its own animation (the weapon ones touching the weapon); put
exactly one secret book room on every floor, holding at random one of two books inspired by the maintainer's two plates (perks
and design): "Perk del Dodecaedro Arcano Maledetto - Le 19 Virtù della Stregoneria" and "Potenziamenti delle Armi - I 19 Sigilli
dell'Arsenale Maledetto".

## How it plays

| Key | Controller | Action |
| --- | --- | --- |
| `3` (tap) or middle mouse | LB + RB (tap) | Cast the selected spell with the left hand. |
| `3` (hold 0.35 s) | LB + RB (hold) | Trace the selected seal on the weapon in hand (lasts 45 s on that weapon). |
| `6` / `7` | LB + D-pad right / LB + R3 | Next spell / next seal. |
| `F` at the secret wall, then `C` | RB, then B | Open the secret wall (an eye carved in the stone, whispering), crawl through the 1.3 m tunnel. |
| `F` at the grimoire | RB | Read the book: mouse or arrows/WASD or D-pad to choose a node, click/Enter/F/A to buy (or to make an owned spell or seal the active one), Esc/B to close. |

The left hand pays with **essence**: 60 at the start, 100 maximum, 2 per second back, 3 per kill. Spells and seals cannot start while
reloading, swapping, sprinting, with the knife out, or during another gesture. With LB held on the controller, RB and R3 belong to the
left hand (they no longer use or stab). Holy water and the music box are now thrown with the left hand too (it takes the object, winds
back and throws; the object leaves from the palm).

To try arms, gestures and effects by hand without finding the books, launch the game with `-provamagia`: every spell and seal with
their outer nodes (except Gesto Rapido and Maneggevolezza, which speed the gestures up), full essence, 10,000 points and the AR-9
Malleus in the second slot (with the starting P9 Pieta it is one of the two weapons with the realistic hold). It is applied again at
every new game.

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
  neutral pose with a pivot at every joint (4 per finger, 4 on the thumb, wrist, palm, back). At runtime the glove is skinned on 17
  bones (palm, 15 phalanges, cuff) with weights from the distance to the phalanx capsules, blended at the joints and between
  neighbouring fingers. The cuff behind the wrist belongs to the forearm, not to the hand: its bone sits on the wrist, along the
  forearm and turned with the hand around it (`HandRig.UpdateCuff`, after the arm solve), and the skin passes from palm to cuff
  across 3 cm around the wrist, so the hand bends at the wrist. Before, the cuff was rigid with the palm and the sleeve hem followed
  the hand: the bend happened 6 cm back inside the sleeve and the hand looked like it had a long neck. Poses are anatomical angles (`HandPose`: spread, MCP, PIP, DIP; thumb abduction, opposition, MCP, IP) limited to human
  ranges (MCP -30..92, PIP 0..110, DIP never more than PIP + 12, no fanning with a closed fist). Fifteen named poses: relaxed, open,
  flat, fist, claw, cup, point, horns, snap-ready, snapped, pinch, grip, blessing, thrust.
- **Invisible swap**: `genera_braccia.py -- pose` writes, weapon by weapon, where the joints of the left hand wrapped on the handguard
  are (`Resources/ManiArmi/presa_sinistra.json`); Unity finds the author-to-Unity axes by itself from the wrist, elbow and shoulder
  empties and puts the free hand exactly on the wrapped glove, with the same finger angles, before switching.
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
- **Realistic hold** (prototype on `ar9_malleus` and `p9_pieta`, `Weapon.RealHoldTable`; `-presavecchia` brings back the old one):
  the weapon at human distance (the rifle's right hand 31 cm from the eye instead of 63), the torso turned like a shooter's (35
  degrees with the rifle, 10 with the pistol; it straightens while the left hand works), body shoulders and hanging elbows on both
  arms. The per-weapon left glove was modelled for the old fake shoulder (forearm arriving horizontally from the left) and on a real
  arm would need 120-180 degrees of forearm rotation, so on two-handed weapons the articulated hand replaces it for good
  (`OffHand.SetNaturalRest`): palm under the handguard, hand diagonal with the thumb along the left side, forearm rising from below
  (at rest pro-supination 73, flexion -11, deviation -9 degrees). The other 28 weapons keep the old hold for now.
- **Gestures** (`OffHand`, `OffHandAnims`): keys of a contact point of the hand (wrist, palm, index, middle, thumb, little finger) in
  camera space, weapon space with its parts (muzzle, magazine and its base, ejection port, sight, handguard, receiver, a point along the
  barrel or on the left side, measured from the parts' bounds by `WeaponSurface`), the rest grip, or below the frame; the wrist follows
  a Catmull-Rom curve, rotations and poses blend with each key's easing; weapon keys and timed events.

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

`Scripts/Braccia/`: `HandPose.cs`, `HandRig.cs`, `GripPoses.cs`, `OffHand.cs` (and `WeaponSurface`). `Scripts/ArmRig.cs`.
`Scripts/Magia/`: `Grimoire.cs`, `MagicState.cs`, `MagicCaster.cs` (input, effects, the `Magic` facade), `OffHandAnims.cs`, `MagicFx.cs`
(effects, `Fireball`, `EnemyStatus`, `WeaponAura`), `MagicVfx.cs` and `VfxTextures.cs` (particles, lightning, circles, textures),
`Resources/Shaders/MagicParticle.shader`, `SoundMagia.cs`, `HUD.Magia.cs`, `GrimoireIcons.cs`, `GrimoirePage.cs`,
`GrimoireUI.cs`, `GrimoireAltar.cs`, `MagiaTest.cs`. `Scripts/Weapon.Sigilli.cs` (seals on hits). `Scripts/Proc/PlanAnnex.cs`
(placement), `Proc/FloorBuilder.Sacrario.cs` (geometry), `Proc/AnnexRuntime.cs` (the secret wall). Assets:
`Models/Mani/libera/mano_libera.fbx`, `Resources/ManoLibera.prefab` (`ManiImport.Run`), `Resources/ManiArmi/presa_sinistra.json`.

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
- Diagnostic flags of the same test: `-magiaside` (with every photo a side and a top view of the arms with the solved joints, and
  their measures in the report), `-magiazoom` (two close-ups of the left hand with the real scene and effects: from the eye with a
  telephoto lens and three-quarters from the left), `-vfxdump` (the effect textures as images), `-armview [-armids a,b]
  [-presavecchia]` (only the rest and aiming pose of every weapon, with arm measures).

## Known limits

- Balance (essence, prices, damage, durations) is untested by a human player.
- Spell and seal effects are not replicated to the other co-op players' screens.
- The gestures were authored by numbers and checked on photographs, not motion-captured.
- The realistic hold covers two weapons; the view model shares the world camera's field of view (75 degrees by default), so a
  forearm reaching forward 30 cm from the eye looks large.
