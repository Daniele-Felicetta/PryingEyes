# Reflections, scoreboard, inspection and game feel

Added on 15 November 2026.

- **Room reflections** (`Scripts/Proc/RoomProbes.cs`): one box-projected reflection probe per room, sized to the room from
  floor to ceiling, rendered once right after the floor is built (one per frame, with the room's lights forced on through
  `LightPool.HoldPoint`) and then left static. Glossy surfaces (checkered marble, brass, glass, lacquered wood) now reflect
  their own room instead of the bright fallback environment, which flattened the marble halls. URP Forward+ probe blending,
  box projection and the probe atlas were already enabled in `HotelURP`. 128 px, 256 px for rooms over 400 m². Cost measured
  with the frame probe at 1080p on seed 2995 floor 3: 4.8 ms with and without probes; baking 9 probes takes about 120 ms
  during the arrival. `-noprobes` disables them for comparisons.
- **Scoreboard** (`Scripts/HUD.Score.cs`): hold Tab for wave and floor, points, kills and headshot share, accuracy, trials
  completed, deaths, time, throwables and, in co-op, the number of players. New counters `GameManager.headshots` and
  `GameManager.deaths`.
- **Weapon inspection** (`Weapon.Inspect`, key I): 2.6 s, the weapon rises and turns to show its left side with a slow
  sway, then returns; firing, aiming, reloading, switching or the knife interrupt it.
- **Explosion shake** (`PlayerController.Shake`): flasks, carillons, Appestato bursts and the Cardinal's fire ring kick the
  view and the field of view, stronger when closer (scaled by the motion comfort setting).
- **Corpses** (`Scripts/CorpseSink.cs`): monks and abbots finish their fall, then sink into the floor over 1.4 s with a puff of
  dust before being removed, instead of vanishing.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -feeltest [-feelshots] -seed 1000 -floor 1`
(`Scripts/FeelTest.cs`, 11 checks: probes rendered for every room and the light pool released, scoreboard opens with the
right numbers and closes, inspection runs and ends, a killed monk sinks and disappears). Photos in `unity_logs/rifiniture`.

## Ragdoll deaths

Added on 15 November 2026 (`Scripts/EnemyRagdoll.cs`, `Scripts/Enemy.Ragdoll.cs`).

- When a monk (any costume) or an abbot dies, the Animator stops and eleven bones become rigid bodies: pelvis, chest, head,
  upper arms, forearms, thighs and shins, with capsules sized from the bone to the next one and character joints toward the
  parent (knees bend only backwards, elbows only forwards, thighs up to 90 degrees forward). About 65 kg in all.
- The killing hit decides the fall. `Enemy.Damage` notes where the hit landed, where it came from (the shooter's eye) and how
  much damage arrived in that frame (shotgun pellets add up). The part closest to the hit gets the impulse (pistol about
  20 N*s, shotgun and sniper 45, capped at 3.5 m/s on the part) and the pelvis a quarter of it, so a headshot snaps the head
  back and the body follows. A running enemy keeps its velocity and falls forward. Knife and takedowns push gently; the Bell,
  fire traps and poison clouds just drop the body.
- Explosions push from their centre: barrels, the Appestato burst and thrown flasks call `Enemy.NoteBlast(centre)` before
  damaging, and whoever dies in that frame flies away from it (5.5 m/s at the centre, less with distance, the same for every
  part so the body does not tear at the joints).
- The staff is a parentless bone in the monk rig (`genera_monaco.py`): it gets its own capsule (from the floor to 1.48 m along
  the bone) and body without a joint, so the monk lets go of it and it falls on its own.
- Ragdoll parts live on the Ignore Raycast layer: bullets and sight lines pass through corpses, so a body never shields the
  enemy behind it, and the player's controller ignores them. Parts of one body ignore each other.
- Bodies freeze after at most 2.8 s (earlier when they stop moving); the blood pool appears under the pelvis at that moment and
  `CorpseSink` lowers the frozen body into the floor as before, with its dust under the pelvis.
- At most 10 bodies move at once; beyond that (the Bell killing a whole horde) the old fall animation plays. Enemies killed
  while rising from the floor and the stone Statue keep the animation too. `-noragdoll` turns the feature off for comparisons.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -ragdolltest [-ragdollshots] -seed 1000 -floor 1`
(`Scripts/RagdollTest.cs`, 16 checks: chest shot pushes the body back, a headshot throws the head back more than the pelvis,
an explosion launches the body, parts stay attached and above the floor, the player is not blocked, a bullet through a corpse
hits the enemy behind it, bodies freeze, the staff lies on the floor, the corpse disappears, at most 10 ragdolls at once).
Photos in `unity_logs/ragdoll`.

## Vaulting and climbing

Added on 15 November 2026 (`Scripts/PlayerController.Mantle.cs`).

The jump reaches 60 cm, so tables, pews, sarcophagi, low walls and fallen chandeliers used to be walls. Now Jump in front of an
obstacle 42-135 cm high (or against its edge shortly after a normal jump while pressing forward) gets over it:

- **Vault** when it is thin and there is floor behind it at the same height (within 35 cm), on the navmesh, inside a room and
  with room to stand: the body goes up to the edge and over (0.30 s + 0.12 s per metre of height) and lands on the other side
  keeping its speed (at least 3.6 m/s).
- **Climb** otherwise, when its top is flat and there is room to stand (or to crouch, then the player stays crouched): up to the
  edge and onto it (0.36 s + 0.14 s per metre).
- Never down from a balcony (the floor behind must be at the same height), never through a window out of the rooms, never under
  a low ceiling (the crouched body must fit above the edge).
- During the move the weapon lowers as when sprinting (no shooting), the view dips by 7 degrees, rolls by 5 (alternating side)
  and drops 12 cm, all scaled by the motion comfort setting; a footstep at the start and a louder one on landing. Teleports and
  respawns cancel it.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -mantletest [-mantleshots] -seed 1000 -floor 1`
(`Scripts/MantleTest.cs`, 9 checks with real obstacles placed in an arena: climbing a 90 cm table in 0.48 s and standing on it,
vaulting a 1 m wall 25 cm thick in 0.41 s and landing beyond it, no vault for a 2.2 m wall or a 25 cm step). Photos in
`unity_logs/scavalco`.

## Hit reactions

Added on 15 November 2026 (`Scripts/EnemyFlinch.cs`).

A hit that does not kill a monk (any costume) or the abbot pushes pelvis, chest and head away from the shot with a damped spring
on top of the animation (stiffness 120, damping 15: back in about half a second). The impulse grows with the damage (6 + 0.22 per
point, up to 16 degrees of hit), shotgun pellets in the same frame add up to a cap of 22, a body shot bends the chest and the head
follows at 60%, a headshot throws the head back about twice as much as the chest, with a little random twist. It is applied in
`LateUpdate` after the Animator as a world rotation around the horizontal axis perpendicular to the shot; each bone remembers the
Animator pose, so when the Animator does not rewrite it in a frame the bend does not accumulate. The spring is integrated in 8 ms
sub-steps (one long frame would otherwise eat the impulse). Beyond 35 m from the camera it stops; the stone Statue does not react;
the abbot reacts at 70%. The killing hit disables it before the ragdoll takes the body. Measured: 4 degrees of chest for a
12-damage body hit, 9 degrees of head for a headshot, 12 for eight pellets at once.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -flinchtest [-flinchshots] -seed 1000 -floor 1`
(`Scripts/FlinchTest.cs`, 9 checks). Photos in `unity_logs/reazioni`.

## Light shafts

Added on 15 November 2026 (`Scripts/Proc/LightShafts.cs`).

Every window light built by `RoomKits.WindowLights` ("Luce della vetrata", "Chiarore del nartece", and now "Luce della
finestra" for the plain windows) gets a visible shaft: an additive trapezoid along the light axis, from the glass to the floor
(or 80% of the range), 0.75-0.95 m wide at the window and widening with a quarter of the spot cone, turned every frame around its
own axis to face the camera. Its texture is generated at runtime: soft gaussian edges, brighter near the glass and fading toward
the floor, with soft vertical streaks (the light between the leads). It takes the colour of its light (stained glass or moonlight),
follows its intensity and the light pool (far lights and their shafts switch off), and fades when the camera gets within about
2.7 m of the axis, so standing in a beam never shows a wall of fog. Inside each shaft a few dust motes drift slowly (a particle
system aligned with the axis, 7 per second, 5-8 s, growing in and out). At most 24 per floor; alpha 0.11 for stained glass and
0.07 for moonlight (the first tuning, 0.15, made the nave 37% brighter on average; now about 25%). `-noshafts` removes them.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -shafttest [-shaftshots] -seed 1000 -floor 1`
(`Scripts/ShaftTest.cs`, 9 checks: shafts exist, one is visible from the side, lit with its light, facing the camera, visible,
hidden when hidden, dust inside, faded near the axis). Photos with and without from the same point in `unity_logs/fasci`.

