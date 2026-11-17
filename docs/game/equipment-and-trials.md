# Throwables and wave trials

Added on 15 November 2026. Code: `Scripts/Equipment.cs` (throwables and the lure target), `Scripts/Prove.cs` (trials),
`Scripts/HUD.Equip.cs` (counters and the trial line).

## Throwables

| Item | Key | Stock | Behaviour |
| --- | --- | --- | --- |
| Acquasanta (holy water flask) | Z | 2 at start, +2 per wave, max 4 | Thrown from the right hand at 13 m/s plus an upward push and the player's velocity, real physics (bounces, ignores the thrower and enemies). 2 s fuse, then a blue-white burst: 420 damage at the centre falling to 90 at 5 m, line of sight required; the thrower takes up to 30. |
| Carillon (music box) | 4 | 1 at start, +1 every third wave, max 2 | Starts playing its lullaby once it settles (or after 2.5 s). For 7 s every enemy within 35 m except the boss and the statue drops its target and walks to it (`Lure`, an `ITarget`), then it explodes (650 at the centre, 5 m). |

The weapon dips for half a second while throwing (`Weapon.ThrowDip`). Kills pay like any explosion
(`DamageSource.Esplosione`). Stock refills whenever the wave number increases, on every machine.

Co-op: each player owns their throwables. Damage goes through `Enemy.Damage`, which on clients becomes a `HitClaim` to the
host. A client's carillon is announced to the host as `NoiseEvt` with `NoiseKind.Lure`, and the host starts an invisible
lure at that point so its enemies are attracted. Other players do not see a client's flask or box (only their effects
on enemies). Protocol `pe-coop-21`.

## Trials (Prove)

From wave 2, every even wave that is not a procession offers one trial for that wave, shown in gold at the bottom centre
with its counter:

| Trial | Goal |
| --- | --- |
| Testa | headshot kills, 6 + wave/2 (max 14) |
| Coltello | knife kills, takedowns included, 3 + wave/4 (max 6), from wave 3 |
| Esplosivi | kills by flasks, carillons, burst Appestati or door traps, 4 + wave/3 (max 10) |
| Intatto | 40 seconds in a row without taking damage |
| Furia | 8 + wave/3 kills within 15 seconds (max 14) |

Completing it pays 400 + 60 per wave points and, solo or on the host, drops a Max Ammo, Double Points or Insta-Kill at the
player's feet. If the wave ends first the trial is lost. Each player has their own trial and counts their own kills
(`GameManager.CreditKill` runs on clients too).

The trial of a wave is drawn at random among the kinds above. An automatic test can fix it with `Prove.pinned` (null in the
game, like `Drops.disabled` for the random drops): `-survivaltest` pins Testa, because its kills are never headshots and Furia, by
contrast, would complete itself and pay.

## Verification

`PryingEyes.exe -batchmode -autotest -nofire -noenemies -equiptest [-equipshots] -seed 1000 -floor 1`
(`Scripts/EquipTest.cs`, 19 checks): the throw spends a flask and sends it forward; a flask among three monks kills them,
pays, and leaves the thrower 6 m away unharmed; a carillon pulls three monks away from the player and kills them; three waves
refill the stock; three headshot kills complete a trial and pay; the Intatto trial counts seconds and resets on a hit.
Photos in `unity_logs/equipaggiamento` (launch from the repository root).

## Glass eyes (hidden hunt)

`Scripts/Proc/HiddenEyes.cs`: three glass eyes per floor, first inside the annexes (tower, crypt, garden), then on the
galleries, then in the corners farthest from the arrival lift, at least 12 m apart, from the plan seed. They float at face
height, turn to watch the player within 9 m, glow faintly green and breathe (audible within a few metres). Take them with F
or shoot them. With all three, the weapon in hand is consecrated for free (or +3500 points if it already is) plus 1000
points. Each player hunts their own (local state, like annex treasures). The scoreboard shows the count.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -eyetest [-eyeshots] -seed 1000 -floor 3`
(`Scripts/EyesTest.cs`, 13 checks: three eyes, far apart, above the navmesh; F and a bullet both collect; all three
consecrate the weapon and pay).

## Reliquary mine

Added on 15 November 2026 (`Scripts/Mine.cs`), the third piece of equipment (key 5).

- A brass disc with a cross and a red glass dome, placed on the floor one metre ahead (flat floor near the navmesh, with a clear
  line from the player; otherwise at the feet). It arms in 1.2 s (slow blink), then waits with a slow blink.
- When a walking enemy comes within 1.7 m (and 1.2 m of height) it blinks fast, beeps and explodes a quarter of a second later:
  520 damage at the centre down to 110 at 4.5 m with a clear line, bodies fly away (`Enemy.NoteBlast`), nearby holy-oil barrels
  chain. Players never trigger it and are never hurt by it. Flying eyes do not trigger it.
- One at the start, one more every four waves, at most two in the pocket and three placed (placing a fourth sets off the oldest).
  Mines are children of the floor, so they disappear when you go down. In co-op everyone has their own and the damage goes
  through the usual hits (hit claims on guests), as for the flasks.
- The count is `[NonSerialized]` on `Equipment`: adding a serialized field to a MonoBehaviour changes its data layout and
  forces a full player build instead of the scripts-only one.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -minetest [-mineshots] -seed 1000 -floor 1`
(`Scripts/MineTest.cs`, 12 checks: one at the start, placed one metre ahead on the floor, the count goes down and an empty pocket
places nothing, not armed at 0.3 s and armed after 1.2 s, a monk at 4 m does not trigger it, one at 1 m does within 0.8 s and
dies, the player next to it is unharmed). Photos in `unity_logs/mine`.

