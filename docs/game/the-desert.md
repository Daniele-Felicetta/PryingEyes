# The desert

Request of 18 November 2026: when a new game starts, the menu offers two ways to play, the standard one the game already has and **the
desert**. A huge, almost endless, procedural desert; the game becomes open world, with a sky that goes through day and night. You start in
the cathedral you already have, go down its steps and put your feet on the sand at the beginning of a day. Around you stand small and large
buildings made of the rooms of the floors, but pulled apart: some whole, some broken, some split in pieces, some falling apart. There are towers
with more floors than any branch of the standard game, seen from below as they really are, and entrances that lead down to catacombs. The
bosses wait on the last floor of the towers and the catacombs. The perk books and the weapon consecration are in the cathedral, to unlock with
enough cathedral resources. Out there only poor, common weapons are found; every other weapon is in the cathedral, for twice the points. In the
rooms, the buildings and out in the open there are only weapons, points, ammunition, medikits and essence.

This page says how it plays, what is found where, how the world is made, the code, the decisions that were left to the developer, and what is
verified. The cathedral, the works, the resources and the satchel are those of [cathedral-and-catacombs.md](cathedral-and-catacombs.md); the
rooms are those of the floors ([tower.md](tower.md)).

## How it plays

1. *Gioca* (and *Ricomincia dalla cattedrale*) opens **Come vuoi giocare?**: **Standard** or **Il deserto**. The choice is remembered for the
   next time (`PlayerPrefs` key `PryingEyes.GameMode`; the automated tests never write it). The command line `-deserto` forces the desert.
   Co-op and split screen stay in the standard mode (the desert is for one player).
2. You wake on the churchyard (*il sagrato*) of the cathedral, at **07:00**. The cathedral stands on a stone plinth; from its door a wide
   stair of twelve steps goes down to the sand. Nothing attacks in the cathedral: the walkers have no navmesh there (they stay at the foot of
   the stair) and whatever flies over the cathedral disappears.
3. **A day lasts twenty minutes** (about twelve of daylight, eight of night; `DesertClock`). The sun, the moon, the stars, the fog and the
   exposure follow the clock; indoors (anything within 12 m above the head) the look goes back to that of the rooms. The top of the screen
   counts the days, and the objective is on two lines: first the hour, the phase (dawn, day, dusk, night), the nearest place and, when you are
   more than 60 m from the cathedral, the way home, each with the side it lies on as seen from where you look (*davanti*, *a destra*, *dietro a
   sinistra*...: sectors of 45 degrees, `DesertWorld.Sector`); then, dimmer, what to look for.
4. **The places** stand on the sand, in six cells out of ten (cells of 200 m), at least 100 m from the cathedral: houses, chapels, inns,
   churches, hotels, theatres, sanatoria and complexes, made of the same rooms as the floors, but **whole (30%), ruined (26%), split in pieces
   or collapsing (22% each)**; **towers** of 7 to 14 floors (up to 76 m) and **mausoleums** whose stair goes down through a well in the sand to
   **catacombs** of 3 to 5 levels. Each place is built when you come within 280 m (a tower, 520 m) and furnished when you are close, a room
   at a time.
5. **Towers and catacombs are stacks of levels** joined by a stair room with two flights; the **boss** is in the hall of the last level (the top
   of a tower, the bottom of the catacombs). Which branch's boss it is, and so which trophy it leaves, depends on the place. When it falls its
   **trophy goes straight into the cathedral's store** (three of its resource and the usual common and rare ones) and the place never puts it
   back in this game.
6. **What you find.** In rooms and in the open: **points** (gold coins), **ammunition** (a crate: half a magazine reserve), **medikits** (50
   health) and **essence** (a crystal: 30 of the left hand's essence). Walk within 1.5 m to take them; what you took stays taken. **Weapons**
   are free panels on a wall (*DA PRENDERE*) or on a wooden stand in the open, and only the poorest (grade I; grade II in a boss's hall).
   Ammunition for a weapon you own costs as always (150 points).
7. **The cathedral keeps the rest.** Its new **armoury** (the room east of the side aisle) sells every weapon except the starting pistol, one
   panel each, **at twice the price** of the standard game. The **Library of Vows** (perk books) and the **Altar of Consecration** are works
   that exist only in the desert: build them in the cathedral with resources (they show as construction sites like any other work). The
   Library offers three perk books on three lecterns in the nave (2000 points the first, then 2500, 3000, 4000; four at most); take one and the
   other two close, and 45 seconds later, with you away from the lecterns, it offers three new ones (`DesertDirector.LibraryRenewDelay`; the
   other floors have a library each, the desert has one, so it renews). The Altar consecrates the weapon in your hand for 3000 points, once per
   weapon. The store, the market and the other works are the usual ones, and everything you pick up goes **directly to the store** (as in
   direct play): there is no satchel to bring home.
8. **Enemies.** Monks mostly, and the farther from the cathedral the tougher and the more varied (abbots, eyes, flagellants, plague bearers,
   statues; theatres and sanatoria have their own): their health and speed grow with the distance (`DesertMode.Danger`: 1 at the cathedral,
   4 at 4.5 km). They use the stealth AI of the floors: they start on patrol and at night go to look where you are. In the open there are few by
   day and many at night (target 1 + 0.8 danger by day, 4 + 2 danger at night, at most 12).
9. **Dying** costs what it costs in the floors (half the points, the last perk) and you wake at the top of the cathedral stair. A safety net
   brings back whoever falls under the sand. Nothing of the desert is saved: the world comes from a seed, a new game makes another one, and
   *Salva e torna al menu* says so.
10. **Sound.** On the sand the steps are those of sand (`SoundGen.Step` surface 3: a soft rub and a few grains; to the monks' ears 20% quieter
    than marble, a little louder than carpet); under the open sky there is wind in slow gusts, no hum of a room, no organ, no creaking, and
    almost no echo (0.45 s); in the nave the echo is the church's (3.6 s). Inside a place (a roof within 12 m above the head) the hum of a room and
    a room's echo come back (`ProcAmbience`).

## What is found where

| | Cathedral | Places and open sand |
| --- | --- | --- |
| Weapons | all, in the armoury, at **twice** the price | only grade I (II in a boss hall), free, a few |
| Perk books, consecration | yes (works to build with resources) | no |
| Points, ammunition, medikits, essence | no | yes |
| Resources, trophies | the store, the market, the works | the bosses' trophies go to the store; enemies' drops go to the store |
| Enemies | none | patrols, guards, bosses |

## The world

A pure function of one seed (`Scripts/Deserto/DesertWorld.cs`, checked without Unity by `tools/desertlab`): nothing is stored, a place is
rebuilt identical every time you come back.

- **The ground.** `DesertWorld.Height(x, z)`: dunes made of noise (taller with the distance: 7 m near, 20 m far), flat for the first 70 m
  around the cathedral (the sand level is -2.4 m, the church floor 0), flattened to a pad under every place and calmed down within 90 m of
  one. The world ends at 18 km (`DesertWorld.WorldRadius`; floats lose precision beyond it): from 17.5 km a line says the desert ends ahead,
  and whoever goes past 18 km is put back 100 m inside, with a banner (`DesertDirector.EdgeTick`).
- **Below the sand.** The levels of a catacomb are 5.4 m apart, down to 21.6 m under its entrance, so the world height of a room is negative.
  Anything of the rooms that was tuned for a floor at height 0 must use the height above the room's floor, never the world height: the haze
  of the church naves (`ChurchHaze.shader`) did not, and in the catacombs it was e^(19 x 0.48) = 9,000 times denser, so the boss hall was a solid
  grey fog (found with `-desertdiag`, fixed by measuring from the volume's floor; in the floors the floor is at 0 and nothing changed).
- **The places.** The plane is split in cells of 200 m; each cell has a place with probability 0.6, with a kind, a state, a footprint and a
  seed, all from the cell and the world seed. The cell (0, -1) is always a ruined chapel (the welcome chapel, at 100 m) and (0, -2) a tower of at
  least seven floors (the landmark).
- **The generator.** `PlanGen.Building` (`Scripts/Proc/PlanGen.Building.cs`) joins rooms of the floors in a building of a given state;
  `PlanGen.StackLevel` makes one level of a tower or a catacomb (a stair room that is identical on every level, a hall, five other rooms).
  Both are checked on hundreds of plans (`BuildingFault`).
- **The sand** (`DesertTerrain`, `DesertTerrainGen`). Square blocks of 64 m around the player with three levels of detail (a vertex every 2 m
  up to 150 m, every 4 m up to 380 m, every 8 m up to 740 m, then fog), computed on worker threads and turned into meshes two per frame.
  Only the near blocks have colliders. A grid line goes through every edge of a well, so the hole is cut exactly; skirts hang under the edges
  so two neighbours of different detail never show a crack.
- **The navmesh** (`DesertNav`). One `NavMeshData` filled in tiles of 64 m (the same step as the blocks) around the player, each tile built
  asynchronously and redone when a place appears or goes away in its area; the far tiles are emptied. The furniture roots are obstacles, the
  cathedral is one big obstacle (the sanctuary). `HotelNav.Ready` follows the first tile. A tile is only built when the sand under it (and
  around it, for the border) has its collider, and the update says `preserveTilesOutsideBounds`: without it Unity throws away every tile
  outside the bounds of the update, so each new tile erased all the others.
- **Streaming of a place** (`SiteRt`, `DesertDirector`). Every place is a root built at height zero (the furniture of the floors is placed in
  absolute coordinates) and then moved to the height of the site; to furnish a room later the root goes back to zero for a moment. First the
  shells of all the levels (walls, floors, ceilings, stairs), then, only near the player, the furniture, the loot and the guards, a piece at a
  time within 7 ms of a frame; far away the furniture is taken off again and, farther still, the place disappears.
- **Memory** (`DesertDirector.CleanTick`). The meshes and materials of a place are made while playing and belong to nobody, so destroying the
  place leaves them in memory until something sweeps the unused assets; Unity does that only when a scene loads, and here the scene never changes.
  The soak test (`-desertonly soak`) showed it: every round of four places left about 1,800 meshes, 360 materials and 120 MB behind, which an hour of
  walking would have turned into gigabytes. Now, 6 seconds after a place is unloaded, the director calls `Resources.UnloadUnusedAssets` (at most
  once every 60 seconds, and not with an enemy within 30 m: `CleanDelay`, `CleanInterval`, `CleanCalm`; only what nothing refers to goes). A sweep
  took 4 to 6 ms here and the log says how long each one took and the longest frame in it.

## The code

| File (`unity/PryingEyes/Assets/_PryingEyes/Scripts/`) | What it does |
| --- | --- |
| `Deserto/DesertMode.cs` | The state of the mode: active, seed, clock, what was taken and which bosses fell, the choice of the menu, danger by distance, the branch of a place. |
| `Deserto/DesertWorld.cs`, `DesertNoise.cs`, `DesertClock.cs` | The pure world (ground, sites), the noise, the clock and the sky at an hour. Compiled by `tools/desertlab` too. |
| `Deserto/DesertTerrainGen.cs`, `DesertTerrain.cs` | The sand mesh as plain arrays (also in the lab) and its streaming. |
| `Deserto/DesertSky.cs`, `Resources/Shaders/DesertSand.shader`, `DesertSky.shader` | The sun and moon light, fog, exposure and the sky dome (stars, clouds, the sun's disc); the sand with its grain and ripples. |
| `Deserto/DesertNav.cs` | The sliding navmesh. |
| `Deserto/DesertDirector.cs`, `DesertSites.cs`, `DesertStairs.cs` | The director (places, walkers in the open, loot in the open, bosses, the edge of the world, the renewal of the Library, the sweep of unused assets, the compass words), a loaded place and the stairs of a stack. |
| `Deserto/DesertPickup.cs` | Points, ammunition, medikit and essence. |
| `Deserto/DesertCathedral.cs`, `Proc/RoomKits.HubDeserto.cs`, `Proc/HubPlan.cs` | The cathedral in the desert: plinth and stair, the armoury and its prices, the places of the two works. |
| `Proc/FloorDirector.Desert.cs`, `FloorDirector.Catacombe.cs` | Starting the game, the wait for the ground, the HUD and the objective, the fall guard, what a rebuilt cathedral must not wipe. |
| `GameMenu.Mode.cs` | The choice *Standard* / *Il deserto*. |
| `DesertTest.cs` | The in-game check (below). |

Changes elsewhere: `Stealth.Enabled` is on in the desert, `Enemy.FloorDirectorCeiling` and `MoveFlying` follow the ground, `WallBuy` has free
panels, `Catacombe.OnKill` takes the branch of a drop from the place, the standard gates of the descent are not built, `HUD` has a shade at the top
and outlined texts and a two-line objective while the desert is on, `ProcAmbience` and `PlayerController.Surface` give the wind, the dry echo and
the sand steps (`SoundGen.Step` surface 3), the menu's *Gioca* asks first (`GameMenu.Mode.cs`; `PadMenuTest` and `PresentationTest` choose
*Standard*), and `Shaders/ChurchHaze.shader` measures the height of its haze from the floor of the room instead of from the world's zero.

## Decisions that were left to the developer

Said here so they can be changed.

- **The boss's reward** is the existing trophy of its branch (`Fabbrica.Trofeo`), banked at once: a branch is drawn by the place.
- **"Scarce, common weapons"** was read as grade I panels, free; grade II in a boss's hall. Every other weapon is in the armoury.
- **"Twice the points"** is `RoomKits.ArmoryPriceMul = 2` on the standard prices.
- **The perk books and the consecration** are two works, **only in the desert**, built with the resources the places give; the rest of the
  cathedral and its works are unchanged.
- **The size of the world** is 18 km of radius (the distance a walker can reach in about an hour of play), not infinite; the edge is a toast and a
  push back inside, not a wall.
- **The cathedral is a sanctuary** (no navmesh in it, nothing flies over it).
- **The AI** is the stealth AI of the floors, not the omniscient one of the wave cycle (which would send every enemy of the desert at you).
- **The Library renews** its three books 45 seconds after one is taken, and tries again when the number of perks changes (death takes one); building
  another work rebuilds the cathedral and so also draws three new books.
- **Sand quiets the steps** (0.8 of what marble sounds like to the monks), so that sneaking over dunes is a little easier than over tiles.
- **Death** uses the penalty of the floors and a respawn at the stair.
- **No map**: the cathedral has never had one (`M` does nothing there) and the desert does not add one; the objective names the nearest place and the
  way home instead. A map of the places already seen is the obvious next step if the compass words are not enough.
- **Not saved**: the desert, its seed, the clock, what you took. The cathedral, the store and the works are saved as always.
- **Not in the desert**: co-op, split screen (they stay standard).

## How to check

```powershell
dotnet run --project tools/desertlab -c Release            # the pure code, no Unity (a few seconds once built); add -- --quick for fewer samples
& $exe -batchmode -nographics -autotest -desertotest -seed 1000        # the real game, from the cathedral
& $exe -batchmode -autotest -desertotest -desertshots -seed 1000       # the same with a window and the photos of hotel_build/deserto
& $exe -batchmode -autotest -desertotest -desertonly varieta -desertshots -seed 1000   # one of the long phases (below): soak, varieta or prestazioni
```

Three more flags help when something fails: `-desertonly scalinata,cappella` runs only those phases (the arrival always runs; the others are
`cielo`, `cattedrale`, `opere`, `oggetti`, `suono`, `torre`, `boss`, `catacomba`, `notte`, `morte`, `lontano`, `caduta`, `foto`), `-desertnavlog` writes, for
every navmesh tile, how many sources it collected and how many triangles the navmesh has when it is done, and `-desertdiag` (with `-desertshots`) says why
a hall looks wrong: ambient light, fog, the six nearest lights, the nearest wall and what it is made of, how many renderers are around, and three more
photos (towards the wall, at the floor). With a window the test also measures the contrast of what the camera sees in the chapel, in every level of the
catacomb and in the two boss halls (`FrameSpread`: a fog or a black room gives almost none).

Three phases are long and run only when asked by name (`-desertonly soak`, `varieta` or `prestazioni`, one per run), since the default run would
double in time. **`soak`** visits two far towers and two far catacombs three times, going back to the open sand in between, and checks that the number of
live meshes, materials and textures and the memory do not grow from one round to the next (and that the director's sweep of unused assets ran and did
not freeze the game); with a window it also looks at the sand and at the nave afterwards, to see that the sweep took nothing from the scene. **`varieta`**
(add `-desertshots` for the contrast checks and the photos) builds one place of every kind and state that the seed has near the cathedral (house, inn, church,
hotel, theatre, sanatorium, complex; whole, ruined, split, collapsing) and a second tower and catacomb, walks into each from the sand through the room that has
the door, and with a window checks the contrast of the view; for the tower and the catacomb it also moves to the boss hall and checks that the boss appears
and that the hall can be seen. **`prestazioni`** (needs a graphics card, the window stays hidden) renders the player's camera by hand at 1920x1080 into a
texture, waits for the GPU and takes the median of seven frames in seven places: the churchyard facing the sand, the open sand with the landmark tower by day
and by night, the foot of the tower looking up, the nave, inside the chapel and the boss hall of a catacomb; a place over 33 ms fails.

## What is verified

Everything below was run on 18 November 2026 on the developer's PC (seed 1000).

- **The pure code**: `tools/desertlab`, 43 checks, all pass (the list is in [../engineering/testing.md](../engineering/testing.md), section 2.4).
- **The real game**: `-desertotest`, 119 checks, all pass, in about three minutes without a window. In order: the game is in the desert and alone; the
  camera clears to a solid colour with a 1,500 m far plane (without it the scene's own sky is drawn over the dome); the cathedral is the usual plan with
  its armoury; the sand, the sky and the navmesh exist and the navmesh keeps every tile it has built; you start on the churchyard; no enemy in the
  cathedral; the stair has twelve steps of 20 cm, the sand at its foot is at the right height and does not cover the steps, you walk down it
  and the walkers get a navmesh on the sand but not on the stair or inside the cathedral; the clock starts at 07:00 and runs, noon is bright, midnight
  is dark with stars, the sun is low at dusk and under the horizon after it; the armoury sells every good weapon at twice the price and not the
  starting pistol; **the Library of Vows and the Altar of Consecration are built with the resources** (the exact payment), the Library offers three
  different books on three lecterns, one is bought for its price, the other two close and, after the renewal delay, three new books that you do not have
  stand on the lecterns; the altar is built after the vaults, the pistol is consecrated once for 3,000 points, the desert is intact afterwards, and both
  works hide when the mode is off and come back with it; the sand makes sand steps (clips of 0.15 to 0.3 s), the open air has wind and no hum, no organ
  and a short echo, the nave has its long one; the objective names the nearest place and the way home with the right side (ahead when you face the
  cathedral, behind when you face away); the welcome chapel is where it should be, is built when you come near, is furnished, is entered on foot and has
  things to take; a coin, a crate and a free weapon are taken by walking over them and do not come back; the landmark tower has 12 floors and is 65 m
  high, you walk in and climb its stair on foot to the first floor, and its navmesh joins the floors; the boss is on the top floor, appears, falls and
  its trophy goes to the store; the nearest catacomb (5 levels) has a well in the sand, you go in from the sand, walk down four stairs to 20.7 m under the
  entrance, reach the boss hall, the boss appears and its trophy goes to the store; at night enemies wander on the open sand, not too many; loot is born
  around you; **death** takes half the points and the last perk, wakes you at the top of the stair and leaves the same seed, sand, sky and a running
  clock; at **5, 9 and 17 km** the sand holds, the navmesh is under your feet and the building next to you loads with every level, and past 18 km you
  are put back inside; whoever falls under the sand is brought back; no frame longer than 1.2 s and no error in the log.
- **With a window** (`-desertshots`, 44 checks of the halls: the chapel, the tower, the catacomb): the contrast of what the camera sees is 0.4 to 0.8 in
  the chapel, in the tower's boss hall, in the entrance hall of the catacomb, in each of its four levels down to -21 m and in its boss hall (a solid fog or
  a black room gives under 0.05). This check exists because of a bug that no other check saw: the haze of the church naves grew with the world height,
  so in the catacombs, below zero, every nave was a wall of grey and the boss hall a uniform blue picture; the photos were taken and the checks were
  green. Photos: 13 of the same run (arrival, dawn, noon, dusk, night, the chapel inside, the tower from far and from its foot, its first floor and its
  boss hall, a catacomb entrance and its boss hall), plus one per catacomb level, looked at one by one.
- **The memory** (`-desertonly soak`, 19 checks, with a window). Three rounds of two far towers and two far catacombs, going home in between: live
  meshes 1,917, 1,935 and 1,941, materials 435 each time, textures 387, memory 661, 712 and 725 MB. Before the sweep of unused assets was added the same
  test failed with 3,761, 5,545 and 7,317 meshes, 824, 1,328 and 1,688 materials and 711, 860 and 983 MB: about 120 MB left behind by every four places.
  Four sweeps in the three rounds, each 4 to 6 ms with a 1 ms longest frame; after them the sand and the nave still have their contrast (0.33 and 0.67).
- **Every kind and state of place** (`-desertonly varieta -desertshots`, 51 checks). Ten buildings near the cathedral, seven kinds in four states
  (whole, ruined, split, collapsing; from a one-room house to a 24-room sanatorium): each is built, furnished, entered on foot from the sand through its door
  and has a view with contrast (0.6 to 0.9 of the luminance range); a tower of 13 levels (entrance hall 0.23, the darkest of all, boss hall 0.68) and a
  catacomb of 4 (0.61 and 0.73): the shell of every level, the walk in from the sand, the boss in its hall. The photos of the theatre and of the tower's hall
  were looked at.
- **The cost of a frame** (`-desertonly prestazioni`, 17 checks) on an RTX 5060 Ti at 1920x1080: 2.5 ms on the churchyard facing the sand, 3.6 ms (day) and
  3.5 ms (night) on the open sand with the tower in the distance, 7.1 ms at the foot of the tower looking up, 4.0 ms in the nave, 5.6 ms in the chapel, 7.6 ms
  in the boss hall of a catacomb (the worst place): all far from 33 ms.
- **The rest of the game** was run on the same working tree: `-cattedraletest` 72 of 72, `-chartest` 23 of 23, `-survivaltest` 91 of 91, `-padtest` 89 of
  89 (it goes through the mode panel), the split screen with two players `-seatstest` 89 of 89, three co-op scenarios (a, c, i: 62 and 49, 11 and 7, 66 and 53
  checks), `MenuReview` 2874 of 2874, the four rule labs of the CI gate and the Python tests (see [../engineering/testing.md](../engineering/testing.md),
  [display-and-menu.md](display-and-menu.md) and [controller.md](controller.md)).

What is **not** verified:

- Nobody has played it: the feel, the difficulty, the pace and the balance (prices, danger by distance, how much there is to find, the strength of the
  bosses, how long a day should be) are guesses. The sound (steps, wind, echo) was measured, never listened to.
- Only this PC was measured (one graphics card, a window in some runs and none in the others), and only with the seed 1000: the lab checks the generators
  on hundreds of seeds, but the game itself was run on one world. The soak test is three rounds of four places (minutes), not an hour of walking, and the
  far places (5 to 17 km) are reached by teleport, so the streaming along a long walk is known only up to a few hundred metres. The cost of a frame is a
  manual render of the camera without the HUD, not a game played: real frames cost a little more.
- No controller in the desert, no multi-monitor, no co-op or split screen (they stay standard).
- Of the places in the open the test walks in and looks at one of each kind and state, a second tower and a second catacomb, but it climbs the stairs only
  of the landmark tower (to its first floor) and of the nearest catacomb (all the way down); the boss halls of the other two are reached by teleport, and the
  rooms between the entrance and the boss hall of the other towers have not been seen.
