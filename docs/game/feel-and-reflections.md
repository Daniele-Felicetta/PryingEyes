# Reflections, scoreboard, inspection and game feel

Added on 15 November 2026.

- **Room reflections** (`Scripts/Proc/RoomProbes.cs`): one box-projected reflection probe per room, sized to the room from
  floor to ceiling, rendered once right after the floor is built (one per frame, with the room's lights forced on through
  `LightPool.HoldPoint`) and then left static until a blackout ends: when the power comes back they are photographed again (`RoomProbes.Rebake`, called from `Proc/Power.cs`). Glossy surfaces (checkered marble, brass, glass, lacquered wood) now reflect
  their own room instead of the bright fallback environment, which flattened the marble halls. URP Forward+ probe blending,
  box projection and the probe atlas were already enabled in `HotelURP`. 128 px, 256 px for rooms over 400 m². Cost measured
  with the frame probe at 1080p on seed 2995 floor 3: 4.8 ms with and without probes; baking 9 probes takes about 120 ms
  during the arrival. `-noprobes` disables them for comparisons.
- **Scoreboard** (`Scripts/HUD.Score.cs`): hold Tab (F8 on the desert surface) for wave and floor, points, kills and headshot share, accuracy, trials
  completed, medals, deaths, time, equipment (holy water, carillons, mines), glass eyes found and, in co-op, the number of players. New counters `GameManager.headshots` and
  `GameManager.deaths`.
- **Weapon inspection** (`Weapon.Inspect`, key I): 2.6 s, the weapon rises and turns to show its left side with a slow
  sway, then returns; firing, aiming, reloading, switching or the knife interrupt it.
- **Explosion shake** (`PlayerController.Shake`): flasks, carillons, Appestato bursts, barrels, chandeliers, mines and the Cardinal's fire ring kick the
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
  back and the body follows. A running enemy keeps its velocity and falls forward. Takedowns push with 5 N*s and knife kills with 18 N*s; the Bell,
  fire traps and poison clouds just drop the body.
- Explosions push from their centre: barrels, the Appestato burst, thrown flasks, mines and chandeliers call `Enemy.NoteBlast(centre)` before
  damaging, and whoever dies in that frame flies away from it (5.5 m/s at the centre, less with distance, the same for every
  part so the body does not tear at the joints).
- The staff is a parentless bone in the monk rig (`genera_monaco.py`): it gets its own capsule (from the floor to 1.48 m along
  the bone) and body without a joint, so the monk lets go of it and it falls on its own.
- Ragdoll parts live on the Ignore Raycast layer: bullets and sight lines pass through corpses, so a body never shields the
  enemy behind it, and the player's controller ignores them. Parts of one body ignore each other.
- Bodies freeze after at most 2.8 s (earlier when they stop moving, the staff included: a body is not frozen while its staff is still
  falling, or the staff would hang in the air); the blood pool appears under the pelvis at that moment and
  `CorpseSink` lowers the frozen body into the floor as before, with its dust under the pelvis.
- At most 10 bodies move at once; beyond that (the Bell killing a whole horde) the old fall animation plays. Enemies killed
  while rising from the floor and the stone Statue keep the animation too. `-noragdoll` turns the feature off for comparisons.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -ragdolltest [-ragdollshots] -seed 1000 -floor 1`
(`Scripts/RagdollTest.cs`, 16 checks: chest shot pushes the body back, a headshot throws the head back more than the pelvis,
an explosion launches the body, parts stay attached and above the floor, the player is not blocked, a bullet through a corpse
hits the enemy behind it, bodies freeze, the staff of a lone monk lies on the floor (next to another monk it may rest against him, which is
fine), the corpse disappears, at most 10 ragdolls at once).
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
follows at 60%, a headshot kicks the head about 3.5 times as hard as the chest (1.9 against 0.55), with a little random twist. It is applied in
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

## Empty magazines

Added on 15 November 2026 (`Scripts/Weapon.MagDrop.cs`).

The magazine reload choreography now has four beats: the magazine comes out with the left hand (12-28% of the reload), the hand
takes it down to the belt out of view and comes back with a fresh one (28-50%), then inserts it (52-68%), followed by the charging
handle or bolt on empty reloads as before. On an empty reload, as soon as the magazine leaves the well a real-scale copy of it
moves into the world (world layer and lights, shadows on), falls with physics in front of the player's feet, bounces with a
metallic clatter (the heavy casing sound pitched down, louder on the first hit) and disappears after 15 s; at most ten lie on
the floor. The real magazine is hidden while the hand goes to the belt and reappears with the fresh one. Tactical reloads keep
the magazine (the hand stows it and comes back with another); tube-fed shotguns, revolvers and break-actions drop nothing.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -magtest [-magshots] -seed 1000 -floor 1`
(`Scripts/MagTest.cs`, 9 checks with the AR-9, the P9 pistol and the Breacher shotgun). Photos in `unity_logs/caricatori`.

## Medals

Added on 15 November 2026 (`Scripts/Medals.cs`, `Scripts/HUD.Medals.cs`).

Instant feats, rewarded with a gold medal at the top centre (name between two growing rules, points below, a scale pop, 1.9 s), a
two-note chime (`SoundGen.Medal`) and a few points, counted on the scoreboard ("Medaglie"). They complement the wave Trials
and are computed on the kills credited to this player (`GameManager.CreditKill`; in co-op each player has their own):

- chained kills less than 0.75 s apart: Doppietta +20, Tripletta +50, Quaterna +80, Strage (five or more) +120;
- Mira divina: five kills in a row to the head +100 (Occhio di Dio at ten, +250);
- Intoccabile: twenty-five kills without being hurt +150 (any hurt resets the streak);
- Ultimo respiro: a kill with health under 15% +50 (at most every 20 s);
- Fuoco purificatore: three kills or more in the same blast (same frame, explosion damage) +75;
- Lama santa: three knife kills within 5 s +60.

Counts reset with the run (`GameManager` and `FloorDirector` resets). The scoreboard equipment line now also lists mines.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -medaltest -seed 1000 -floor 1` (`Scripts/MedalTest.cs`,
11 checks: chain medals with their points and the banner, the chain restarting after a pause, the headshot streak, the blast
triple, the untouchable streak at exactly 25 after a hurt, the last-breath kill, the knife triple).

## Adaptive music

Added on 15 November 2026 (`Scripts/MusicDirector.cs`; hooks in `RoundDirector`, `ProcAmbience` and `GameManager`).

Procedural like every other sound: four clips computed on worker threads when the procedural scene starts (`SoundBank.WarmMusic`,
about a second of work in total, `Task.FromResult` on Web), attached to the director's sources as soon as each is ready. Between
waves there is only the ambience. While a wave fights (`RoundDirector.Fighting`, on hosts and clients alike) three layers play in
sync, 24 s loops (8 bars at 80 bpm, every partial an integer number of cycles in the loop, reverb and high-pass computed over two
passes so the tail of the loop falls back on its start):

- drums (32 kHz): low taiko on beats 1 and 3, frame drum on 2 and 4, ticks on the eighths with a little swing, a tom roll on bars
  4 and 8, a metal hit at the head of each phrase; 0.40 with few enemies, 0.80 at 14 alive, x0.4 when 3 or fewer remain;
- ostinato (22 kHz): staccato bowed D in eighths, down to B flat and A on bar 4 and through C on bar 8, doubled an octave below, over
  a D-A drone; 0.34 to 0.44;
- strings (22 kHz): a D, A, D, E flat, A cluster on two detuned desks with vibrato, measured tremolo on the high voices and slow
  alternating swells; 0.42 from 10 alive, 0.60 while the Cardinal lives.

Layers rise in 1.5-3 s and fall in 3.5-4 s (0.8 s when the player dies). A wave won with its reward (`MusicDirector.WaveCleared`,
also on clients through `CoopComplete`) plays an organ chord, D minor opening into D major with a soft chime. The music bus is
0.65 of the "Musica" setting (0.35 of that in the pause menu); the sources bypass the listener reverb and have high priority.
While it plays, `MusicDirector.Duck` lowers the ambient organ and theatre pad by up to 60%. `-nomusic` turns it off.

Measured on the exported clips (`-musicwav`, demo mixed with the same rules): about -22 LUFS per layer at full level, -21.8 LUFS
with few enemies, -18.3 with many, -17.6 with the Cardinal, -23.8 with the last ones, peak -1.4 dBFS; the ambient organ in a church
is -28.8 LUFS. Not yet listened to by a person.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -musictest [-musicwav] -seed 1000 -floor 1`
(`Scripts/MusicTest.cs`, 34 checks: clips from the threads, lengths, valid samples and seamless loop joins, levels between the
ambience and gunfire, then with the wave state forced: silent between waves, drums and ostinato with the fight and growing with the
enemies, strings with many enemies and louder with the Cardinal, drums stepping back for the last ones, fast drop on death, the
chord on a won wave and not on an aborted one, the ambient organ lowered, the Musica volume applied, the three layers aligned; last
a real wave turning the music on by itself). `-musicwav` writes the clips to `unity_logs/music`.

## Surface impacts

Added on 15 November 2026 (`Scripts/Armi/Surfaces.cs`, `Fx.Impact`, `Scripts/SoundImpatti.cs`).

Every bullet that hits the level now reacts to what it hits. `Surfaces.MaterialAt` finds the visible material at the hit: the
sub-mesh of the hit triangle on the merged room shells (floor, wall, dado and ceiling are one mesh), the renderer of the object or
its nearest part, or, for colliders without a renderer (the floor blocks), the visible surface at the same point. The name is read
as the generator asks for it (the ProcKit keys: `pav_parquet`, `mur_pietra`...) or else as the material and object are called,
against keyword lists in Italian and English (chosen so that they do not hide inside other words: `_osso` and not `osso`, which is
in `rosso`; `vetro` and not `vetr`, which is in `VelvetR`; `_oro$` only at the end). Eight families (the eighth, sand, was added on 18 November 2026 for the desert: it is
recognised by the material `DesertSand` and the blocks `Sabbia_x_z` of `DesertTerrain`, and no floor has it; its numbers are not in the census below):

| family | particles | hole | sound |
|---|---|---|---|
| Plaster | wide light dust, white crumbs | crater with chipped rim and dust halo | crumbling crack |
| Stone | grey chips, a spark now and then, rare ricochet | small crater with light chipped rim | sharp snap |
| Tile | white chips flying far | crater with radial cracks | ceramic tink |
| Wood | long light splinters, dark sawdust | torn fibres along the grain | knock of the board |
| Metal | dense sparks, a flash, a wisp of smoke, ricochets (25%) | dent with a bright ring and scorch | ring of the metal |
| Fabric | a puff in the cloth colour, floating fibres | small frayed hole | dull thump |
| Glass | glinting shards | star crack | crack and falling tinkle |
| Sand | a wide, slow cloud of sand-coloured dust, twelve grains that fly up and fall; no ricochet | a soft dimple with a lighter ring of loose sand, no crack | a muffled thud, the hiss of the grains |

Chips fly off the wall, fall, bounce twice on the floor found under the impact and rest there until they fade. Dust and chips are
Unlit and take the colour of the light at the point (`Surfaces.LightAt`: ambient, pool lights with inverse square and the URP range
window, the torch cone), so they no longer glow in dark rooms; blood mist does the same. Holes and blood stains stay Unlit (the
project's transparent Lit material preserves specular where alpha is zero and drew a light square around every hole) and are
re-tinted every 0.15 s by `Surfaces.LightColorAt` with the surface normal, so a torch sweeping over a wall lights its holes. One
impact sound every 45 ms at most and only within 45 m; ricochets add a short reflected tracer and a whine. Barrels, lamps and
chandeliers pass their own family (wood, glass, metal).

Census on seed 1000 floor 1 (224 rays from every room): plaster 25%, stone 38%, wood 23%, fabric 11%, metal 2%, recognised 100%.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -impacttest [-impactshots] -seed 1000 -floor 1`
(`Scripts/ImpactTest.cs`, 7 checks: kit keys and trap words, the floor census, one impact per family with its hole and sound, chips
resting about 4 mm above the floor, the light estimate under the strongest lamp against the darkest point, the real weapon firing at
a wall of every family). `-impactshots` writes close-ups to `unity_logs/impatti`, including `catalogo.png`, the seven holes side by
side on the most lit wall.
