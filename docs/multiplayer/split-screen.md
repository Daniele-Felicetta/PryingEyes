# Split screen on one PC (2 to 4 players)

Several people play **in the same game and the same window**: main menu, **Schermo condiviso**, then **2 giocatori**, **3 giocatori** or
**4 giocatori**. The screen is divided into equal parts. Every player has their own view, HUD, body, weapons, points and controller; the
hotel, the enemies, the waves and the lift are the same for everyone. There is no network and no second process: it is the single-player
game with more players in it.

This is the second version, first released in 0.6.2. The first one (15 November 2026, releases 0.6.0 and 0.6.1, which contain it) opened one borderless window
per player and joined them with the online co-op over loopback. It was replaced on 16 November 2026 because several windows are not one
game: the keyboard and the mouse follow the focus of the windows, the windows have to be kept in place, and each one simulates its own copy
of the hotel. The files of that design (`SplitScreen.Win.cs`, the window launcher, `-padsim`) are gone; `Coop.Active` stays false in a
split screen.

## How to play

1. Main menu, **Schermo condiviso**, then the number of players. The button **Giocatore 1** switches player 1 between *tastiera e mouse*
   and *controller (e tastiera)*. The panel shows how many controllers the game sees and how many two players need.
2. The run starts at once. Everyone arrives side by side in the cabin of floor 1, and the screen is divided as in the table below.
3. Everybody can play with a controller; player 1 can also use the keyboard and the mouse. See [Controls](#controls).
4. **Pause**: Esc, or Start on any controller. It stops the whole world for everyone and hides every HUD; Esc, Start on any controller or
   **Riprendi** resumes. The pause menu names who uses what (*G1: tastiera e mouse   G2: controller 1 ...*). **Torna al menu** ends the
   split screen for everyone (the confirmation says so). The number of players is chosen at the start: nobody joins or leaves in the
   middle, and a player who stops playing stays in the game. If the window loses focus the game pauses, as it does for one player.

## Layout

| Players | Player 1 | Player 2 | Player 3 | Player 4 |
| --- | --- | --- | --- | --- |
| 2 | left half | right half | | |
| 3 | top left | top right | bottom, full width | |
| 4 | top left | top right | bottom left | bottom right |

The parts cover the window exactly, with no gaps and no overlaps (`SplitScreen.ViewOf`, used as `Camera.rect`): on 1920x1080 two players
get 960x1080 each, three get 960x540, 960x540 and 1920x540, four get 960x540 each. Two black lines of 3 pixels mark the borders. Each HUD is
the normal one, laid out for 1280x720 and scaled to fit its part (`min(width / 1280, height / 720)`), so the reading size follows the part and
not the window. The vertical field of view is the player's setting; in a part much wider than tall it is shortened so that the horizontal
angle never passes 125 degrees (`SplitScreen.FovFor`). The window can be resized; the parts follow.

## Controls

The mapping, the menus and the on-screen hints are on [the controller page](../game/controller.md); a split screen uses the same ones.

- **Player 1** has the keyboard and the mouse (the other players never receive them) and, if a controller is assigned or adopted, that
  controller too. With *tastiera e mouse* on the panel, player 1 adopts by itself the first free controller on which a button is pressed.
- **Players 2 to 4** each have a controller (XInput: Xbox 360, One, Series and compatible; a PlayStation or Switch controller needs a
  wrapper such as Steam Input). The connected controllers are given out in order: player 2 gets the first, player 3 the second, player 4 the
  third; when player 1 plays with a controller as well, player 1 gets the first and the others shift by one. If fewer are connected than
  needed, the missing players get the first free slots, so a controller plugged in later works at once (`SplitScreen.AssignPads`).
- Every controller drives the menus and can pause or resume; the menu is shared (the first one that moves or presses leads).
- While a menu is open no controller gives game commands.

## What is shared and what is each player's

| Shared | Each player's own |
| --- | --- |
| The floor (seed, rooms, doors, shutters, power, lights), the waves and the enemies | Body, health, shield, camera, HUD and map |
| The lift: when any player uses it, everyone goes down together; everyone arrives side by side | Points, kills, shots, medals; what is bought at walls and doors is paid with the buyer's points |
| Pickups: whoever walks into a drop takes it, and its effect applies to everyone | Weapons, ammunition, equipment, perks (books), knife, torch, stealth state, guide arrow and ping |
| Barrels, chandeliers, lamps, traps and mines in the world (the points of a kill go to whoever caused it) | Death and respawn: a dead player returns to the entrance after 2.5 s and loses half the points and the last perk; the others go on |
| The round and the pause | The reward for lighting a candle goes to who lit it; the reward for a floor, a wave or a round goes to everyone |

Enemies use the multi-target behaviour of the online co-op (`Targets`): they appear on the nearest player and, in the wave cycle (where they
know where everyone is), switch to a player who is clearly closer; in the old candle cycle they follow whoever they see, hear or provoke.

## How it works

The rest of the game was written for one player: `GameManager.I.player`, `.hud`, `.score`, `Equipment.I`, `Torch.I`, `Stealth`, `Medals`
and so on are read in hundreds of places. Instead of duplicating all of it, each player is a **seat** (`Seat`, `Coop/Seats.cs`) that holds
the body, the camera, the HUD, the controller channel and every one of those per-player values, and `Seats.Enter(seat)` puts the seat's
values where the game reads them, then puts seat 1 back. At rest seat 1 is installed, so with one player nothing changes and nothing costs.

Rules for code that touches players (the reasons are in the comments of `Seats.cs`):

1. A component of seats 2 to 4 wraps its Unity entry points: `void Update() { using (Seats.Enter(this)) UpdateBody(); }`. It finds its seat
   through the root of its transform (`Posto2` ... `Posto4`).
2. World code that means "every player" goes through `Targets` (each seat is an `ITarget`: seat 1 is `Targets.Local`, the others a
   `SeatTarget`) or the helpers of `Seats` (`ForEach`, `EachOrOnce`, `ForHuds`, `Players`, `Eyes`, `NearestEye`, `NearestPlayer`, `ShakeAll`).
3. Anything that remembers who started it (a grenade, a mine, a barrel, a chandelier) keeps the `Seat` and enters it around the damage, so
   points and kills go to the right player.
4. A new per-player value that the game reads as a global belongs in `Seat` and in `Seats.Save` / `Seats.Load`.

How the screen is made: each seat camera is a copy of the main one (URP settings included) with its own `Camera.rect`. The weapon and hands of
seat n live on layer 24 + n, and a camera draws only its own, so nobody sees another player's gun. There is one `AudioListener`, on player 1's
camera. The HUDs hang under one overlay canvas, each in its rectangle. Lights, shadows, level of detail and billboards that depend on "the
camera" use the nearest player's eye (`Seats.NearestEye`). `SeatInput` gives the keyboard and the mouse only to the seat that has them, and each
seat reads its own `PadControls.Channel`. The pause freezes time for everyone (`Time.timeScale`, as in single player, unlike the online co-op).

Code (`Assets/_PryingEyes/Scripts/`): `Coop/Seats.cs` (seat, context swap, helpers), `Coop/Seats.Rig.cs` (birth of seats 2 to 4, layout, HUD
placement, borders), `Coop/SeatInput.cs`, `Coop/SeatTarget.cs`, `Coop/SplitScreen.cs` (pure rules: layout, controller assignment, field of view,
start), `PadControls.cs` (XInput and the channels), `GameMenu.Split.cs` (panel, pause and leave texts), `SeatsTest.cs` and `SplitScreenTest.cs`.
About fifty other files wrap an entry point or ask for "every player": `Enemy*`, `FloorDirector`, `RoundDirector`, `Drops`, `DropPickup`,
`Equipment`, `Mine`, `Barrels`, `Chandeliers`, `HUD*`, `PlayerController`, `Weapon*`, `Fx`, `LightPool`, `Power` and others. The split screen
exists only in the Windows player (XInput) and only on the hotel floors; elsewhere the menu button is off.

### Command line

| Flag | Meaning |
| --- | --- |
| `-splitscreen N` | Start a split screen of N players (2 to 4) by itself, as if the menu button had been pressed |
| `-splitpad1` | With `-splitscreen`, player 1 uses a controller too |
| `-seatstest` | The in-process test below (not in distribution builds) |
| `-seatsshots` | With `-seatstest` in a real window (no `-batchmode`), save photos of the whole screen in `unity_logs/seats/` |

## Verification

`-seatstest` runs inside the game, with a fake XInput state injected per player (`PadControls.Inject(channel, state)`), and exits `0` only when
every check passes. It has two ways to start the game: from the menu (no `-autotest`, with `-menureview -nowaves`: the menu presses the
button, so the real path *Schermo condiviso*, **Gioca** and the pause are covered) or with `-autotest -nofire -noenemies` (no menu, no pause test).
It checks:

- **Structure**: a body, a camera, a HUD, a target and a controller per player; cameras in their parts, covering the screen without
  overlaps; one `AudioListener`, on player 1's camera; each weapon on its own layer; each HUD inside its part; side-by-side arrival.
- **Context swap**: inside a seat the globals are that seat's; equipment, knife, torch, guide and stealth net are different per seat; seat 1
  is installed again afterwards.
- **Controllers**: each player walks with their own controller while the others stand still; a trigger fires only that player's weapon; one
  player walks while another jumps, with no command crossing over.
- **Points and enemies**: points, hits and kills of one player are only theirs; an enemy appears on the nearest player and switches to a
  closer one; with `-candles` also `Provoke` and sight.
- **Damage, death and respawn** of one player, with the others untouched.
- **Pause**: Start on any controller pauses everyone (time stopped, HUDs hidden, no walking or shooting even with the sticks held); Start on
  another controller resumes.
- **End**: back to one player, one body, one listener, full-screen camera, and the controller still works.

Run it from the verification build (`Builds/Audit`) with `tools/coop/run_split.ps1` (`-Players 2` to `4`, `-Graphics`, `-Candles`, `-NoMenu`;
exit `0` only if the result is `SUPERATA`):

```powershell
.\tools\coop\run_split.ps1 -Players 3               # no window, no GPU, about 30 seconds
.\tools\coop\run_split.ps1 -Players 4 -Graphics     # a 1280x720 window for about 40 seconds; photos in unity_logs/seats/
```

With `-Graphics` the window rewrites the `Screenmanager` values of the game's registry key; the script saves them first and restores them
afterwards. Do not run it while someone plays (GPU and screen), and never against `Builds/PryingEyes.exe`.

Results on 16 November 2026, from the menu: 2 players 55 of 55 checks, 3 players 56 of 56, 4 players 57 of 57 (also in a 1280x720 window);
3 players with `-candles` 58 of 58; 3 players without the menu 51 of 51. The photos of the window runs show each player's view, HUD and
weapon in their part, the borders, the pause screen with the line that says who uses what, and a dead player's view. Also run on the same day:
`-splittest` (63 of 63: layout of the parts for 2 to 4 players, controller assignment, field-of-view cap, XInput mapping, and the real player
with a fake XInput state) and `PryingEyes.EditorTools.MenuReview.Run` (665 checks, the panel at four sizes included: texts fit, nothing
overlaps the footer). The parameters of the script and the other checks are in
[testing.md](../engineering/testing.md#51-split-screen-one-game-one-window).

The refactor touched about fifty files of the single-player game, so the default (wave) cycle was checked again the same day, from the same
build: `-padtest` 73 of 73, `tools/check_gunplay.py` 297 of 297, `-barreltest` 19, `-lampadaritest` 15, `-equiptest` 19, `-minetest` 12,
`-flinchtest` 9, `-shafttest` 9, `-powertest` 16, `-survivaltest` 86, `-ragdolltest` 16, and scenario `a` of the online co-op
(`tools/coop/run_all.ps1`: 50 checks on the host, 38 on the guest), all passing. The first run of two of them failed for reasons older than
the split screen, and both were fixed: `-survivaltest` did not count the points that the medals pay during a wave, and `-ragdolltest` failed
by chance because the ragdoll froze a monk's body while its staff was still falling (the staff stayed in mid-air). The checks of the old candle
and stealth cycle (`-stealthtest`, `-roundtest`, `-knifetest`, `-droptest`, `-booktest`, `-gridtest`, `-holescan`, `-pushtest`,
`check_navigation.py --legacy`) were not run again: that cycle is off unless `-candles` is given.

## Not verified, limits

- A **physical controller**: none was connected. The XInput calls answer without errors and everything else ran on injected states.
- Nobody has played a session with several people: the tests drive the controllers, not a human.
- **Audio**: there is one listener, on player 1's camera, so every sound is heard from where player 1 stands; far from player 1 the
  others' shots and steps are faint. Music and interface sounds are not affected. A fair mix needs a custom mixer (one listener per scene
  is a Unity rule).
- **Performance** with four views on a large monitor (for example 3440x1440, four parts of 1720x720) is not measured: the tests ran without
  a window or in 1280x720. Four cameras draw the world four times; lights and shadows follow the nearest eye to keep it down.
- The keyboard and the mouse belong to player 1 and cannot be handed to another player. There is no joining or leaving in the middle of a run
  and no lobby ("press A to join"); the number of players is chosen on the panel.
- One window, one monitor; the players cannot have different display settings. The guest-style choices of the online co-op (names,
  addresses) do not exist here.
- It cannot be combined with the online co-op: the button is off while a co-op game is on, and `-splitscreen` with `-coophost` does not start.
- The old candle cycle (`-candles`) works but is the less used path; the wave cycle is the default.
