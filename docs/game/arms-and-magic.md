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
in front of the eye, `OffHand.ReachTarget`, and turned 36 degrees to show the left side), so the elbow bends while the hand works. A weapon with a seal glows in the element's colour (per-part emission, so frost and
runes run from the hand outwards) and emits its particles.

## Arms and hands

- **Free hand** (`HandRig`): the left glove of the per-weapon hands, rebuilt by `modelli/hotel/genera_braccia.py -- libera` in a
  neutral pose with a pivot at every joint (4 per finger, 4 on the thumb, wrist, palm, back). At runtime the glove is skinned on 16
  bones (palm, 15 phalanges) with weights from the distance to the phalanx capsules, blended at the joints and between neighbouring
  fingers. Poses are anatomical angles (`HandPose`: spread, MCP, PIP, DIP; thumb abduction, opposition, MCP, IP) limited to human
  ranges (MCP -30..92, PIP 0..110, DIP never more than PIP + 12, no fanning with a closed fist). Fifteen named poses: relaxed, open,
  flat, fist, claw, cup, point, horns, snap-ready, snapped, pinch, grip, blessing, thrust.
- **Invisible swap**: `genera_braccia.py -- pose` writes, weapon by weapon, where the joints of the left hand wrapped on the handguard
  are (`Resources/ManiArmi/presa_sinistra.json`); Unity finds the author-to-Unity axes by itself from the wrist, elbow and shoulder
  empties and puts the free hand exactly on the wrapped glove, with the same finger angles, before switching.
- **Arm** (`ArmRig`): a forearm twist bone takes half of the hand's roll around the forearm, so supination no longer wrings the cuff;
  the elbow stays between 28 and 176 degrees; with the free arm the elbow swivels around the shoulder-wrist axis to keep the wrist
  within about 32 degrees of the forearm (spring-damped). The free arm has human proportions: the shoulder 19 cm to the side of and
  24 cm below the eye, protracting up to 12 cm on long reaches, and a 30 cm upper arm (the sleeve's upper bone is scaled to it). At
  rest, on the weapon, the arm is exactly the one of before.
- **Gestures** (`OffHand`, `OffHandAnims`): keys of a contact point of the hand (wrist, palm, index, middle, thumb, little finger) in
  camera space, weapon space with its parts (muzzle, magazine and its base, ejection port, sight, handguard, receiver, a point along the
  barrel or on the left side, measured from the parts' bounds by `WeaponSurface`), the rest grip, or below the frame; the wrist follows
  a Catmull-Rom curve, rotations and poses blend with each key's easing; weapon keys and timed events.

## Code map

`Scripts/Braccia/`: `HandPose.cs`, `HandRig.cs`, `GripPoses.cs`, `OffHand.cs` (and `WeaponSurface`). `Scripts/ArmRig.cs`.
`Scripts/Magia/`: `Grimoire.cs`, `MagicState.cs`, `MagicCaster.cs` (input, effects, the `Magic` facade), `OffHandAnims.cs`, `MagicFx.cs`
(effects, `Fireball`, `EnemyStatus`, `WeaponAura`), `SoundMagia.cs`, `HUD.Magia.cs`, `GrimoireIcons.cs`, `GrimoirePage.cs`,
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
  wrapped glove's place and gives it back, effects arrive, the elbow stays in range) and the left-hand throw. Results in
  `hotel_build/magia/magia.txt`, photos with `-magiashots`.

## Known limits

- Balance (essence, prices, damage, durations) is untested by a human player.
- Spell and seal effects are not replicated to the other co-op players' screens.
- The gestures were authored by numbers and checked on photographs, not motion-captured.
