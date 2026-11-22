# Split screen on one PC (2 to 4 players)

Several people play **in the same game**: main menu, **Schermo condiviso**, then **2 giocatori**, **3 giocatori** or
**4 giocatori**. The screen is divided into equal parts, in the game window or, with more than one monitor connected, **over the monitors**
(see [Several monitors](#several-monitors)). Every player has their own view, HUD, body, weapons, points and controller; the
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
   With two or more monitors connected the same row has a second button, **Monitor**, that picks how many monitors to use (see below).
2. **Chi gioca?** asks which character plays in each seat ([../game/saves-and-characters.md](../game/saves-and-characters.md#playing-together)), and
   **Comincia** opens **Che partita giocate?** (0.9.1): *Standard* (the floors: everyone arrives side by side in the cabin of floor 1) or *Il deserto* ([../game/the-desert.md](../game/the-desert.md):
   everyone arrives on the churchyard of the cathedral). The choice holds for every seat and is remembered for the next time; **Indietro** goes back to **Chi gioca?**. The screen is divided as in the table below.
3. Everybody can play with a controller; player 1 can also use the keyboard and the mouse. See [Controls](#controls).
4. **Pause**: Esc, or Start on any controller. It stops the whole world for everyone and hides every HUD; Esc, Start on any controller or
   **Riprendi** resumes. The pause menu names who uses what (*G1: tastiera e mouse   G2: controller 1 ...*). **Torna al menu** ends the
   split screen for everyone (the confirmation says so). The number of players is chosen at the start: nobody joins or leaves in the
   middle, and a player who stops playing stays in the game. If the window loses focus the game pauses, as it does for one player.
5. **Grimoires** (the secret room's two books): a player who opens one reads it inside their own part of the screen, never over the whole
   window, and nobody else is stopped: time keeps running, the others keep walking and shooting, and only the reader's commands (and
   their controller) go to the book. Esc, Backspace, right click or B closes it. See
   [arms-and-magic.md](../game/arms-and-magic.md#the-open-book-grimoireui).

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

## Several monitors

The panel always says how many monitors the game counts (*Monitor collegati: N*, with *Windows ne vede M* when Windows has more) and shows
**Monitor: uno solo / due / ...** next to the player-1 switch. With more than one monitor connected the button cycles from one monitor up to
the number connected (at most four), the choice is remembered between runs, the first time it uses all the connected ones, and the line under
the title says how the players are divided (*Su 2 monitor: 2 giocatori 1+1 · 3 giocatori 2+1 · 4 giocatori 2+2*). With one monitor there is
nothing to choose and play is unchanged: the button says how to get two (extend the desktop with Win+P, then restart the game, because Unity
counts the monitors when the game starts) or, when Windows already has more monitors than the game counted, to close and reopen the game
(`SplitScreen.SystemMonitors`, `MonitorsAddedAfterStart`). The players are divided over the chosen monitors as evenly as possible, the first
monitors taking the extra ones, and each monitor shows its players with the same layout as a whole screen (`SplitScreen.PlayersOn`,
`DisplayOf`, `ViewOn`):

| Players | 2 monitors | 3 monitors | 4 monitors |
| --- | --- | --- | --- |
| 2 | one each, full screen | (2 are enough) | (2 are enough) |
| 3 | 1 and 2 side by side on monitor 1, 3 full screen on monitor 2 | one each | (3 are enough) |
| 4 | 1 and 2 side by side on monitor 1, 3 and 4 side by side on monitor 2 | 1 and 2 on monitor 1, 3 on monitor 2, 4 on monitor 3 | one each |

Monitor 1 is the monitor of the game window: it keeps the menu, the pause menu and player 1, who has the keyboard and the mouse. A player's
HUD, camera and the book of a grimoire they open are on their own monitor. In the pause the other monitors show the frozen world under an
**IN PAUSA** veil (the menu is on monitor 1, and its line that says who uses what also says how many monitors: *Split screen a 2 giocatori su 2
monitor ...*). Every monitor is used at its own size and the HUD scales to the part it fills, as in one window. The map (M) and the minimap
(N) of a player on another monitor are drawn with uGUI under their HUD, because Unity shows IMGUI, which the map of the first monitor uses,
only on the first monitor. In the desert `M` (or Select on the pad) opens the Select menu of that seat instead, drawn the same way and on the part of the screen of the seat that opened it
([the-desert.md](../game/the-desert.md#the-select-menu)).

How the windows behave (seen on Windows 11, D3D12, Unity 6000.6): turning a monitor on makes Unity open a borderless window on it at its
native size, and the game window itself takes the size of its own monitor (borderless) as soon as that happens. A program in front of the new
window would hide the game from the player on that monitor, so for the first three seconds the game raises a covered window of an extra
monitor, without taking the keyboard from the game window (`WinWindows.RaiseSecondary`), and gives the keyboard back to the game window if
Windows gave it to the new one (`FocusMainIfStolen`; losing it also pauses the game, except during those three seconds). The covered windows
are raised again whenever the game gets its focus back, because coming back from another program brings forward only the window that was
clicked (`SplitScreen.OnFocusGained`). Unity cannot close the window of a monitor it has opened: when the split screen ends the windows of
the extra monitors are hidden, and they come back with the next one.

## The desert (0.9.1)

The desert mode can be played in split screen, on one monitor or several. After *Chi gioca?* the menu asks *Che partita giocate?* (`GameMenu.Mode.cs`, the same panel as the single player's);
`-splitscreen N -deserto -catacombe` starts it without the menu. Everyone arrives on the churchyard of the cathedral, side by side, and then each seat walks where it wants: one can be two kilometres
from another. What the desert makes for that:

- **A world round every seat**: the sand with its collider and the navmesh, the places and their guards, the props, the loot on the sand and the far skins of the buildings are made round each seat and
  dropped only when far from all of them (`DesertAnchors` lists the other seats as *viewers*).
- **A sky for each camera**: the fog, the ambient and the grading are global, so the look is one (outdoors if any seat is outdoors); every seat's camera has its own background and far plane, and the one dome
  moves to the camera that is rendering (`DesertSky.AttachExtra`, `beginCameraRendering`).
- **Effects, objective, minimap and pick-ups per seat**: flying sand, animals, veils off the crests and dust devils once per seat; the objective line says where *that* seat is and the way home; the minimap
  shows the other seats as coloured dots when they are inside its window; the golden arrow points at the cathedral; what a seat picks up from the sand is that seat's (points, ammunition, health).
- **Shared**: the enemies, the hour and the weather, the places, the bosses and the one audio listener.

How it works and what was checked are in [../game/the-desert.md](../game/the-desert.md) (section *Split screen*).

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
| Barrels, chandeliers, lamps, traps and mines in the world (the points of a kill go to whoever caused it) | Going down at zero health (since 0.7.2; a teammate revives by holding Use for 3 s next to the body), and death after bleeding out: a dead player returns to the entrance after 2.5 s and loses half the points and the last perk; the others go on |
| The round and the pause | The reward for lighting a candle goes to who lit it; the reward for a floor, a wave or a round goes to everyone |

**Characters** (0.9). Each seat plays its own character: its experience, kills, play time and the loot it picks up go to that character's own file,
and its cathedral state is part of the seat (`Seat.cathedral`, put in place by `Seats.Enter` with the other per-player values; `Progress.Mine` and
`Fabbrica` follow the seat that is installed). Two characters never share a look in one game. A suspended expedition (*Salva e torna al menu*) is
for the single player: with several players *Torna al menu* closes the game for everyone, as before
([../game/saves-and-characters.md](../game/saves-and-characters.md#a-suspended-expedition)).

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

**How the players see each other** (0.7.1; before, every player had only their first-person arms and nobody saw anybody). Each seat has
a `SeatAvatar`: since 0.8.0 the same survivor as the online co-op partners ([../game/characters.md](../game/characters.md): one of four looks by seat,
walking legs, the weapon in hand, aiming, shooting, reloading, sitting when downed and lying when dead; before, a capsule mannequin made by
`RemotePlayer.BuildBody`, still the fallback when the models are missing) with the name above the head, turned towards each camera just before it
renders. It is driven by the seat's body at the end of the frame and sits on the seat's own avatar layer (`Seats.FirstAvatarLayer` + index,
layers 20 to 23): `Seats.Layout` gives every camera its own weapon layer and the avatar layers of the others, never its own, so nobody
sees their own body in front of the eyes (unless that player chose the [third-person view](../game/third-person.md), which adds their own
layer back to their own camera). The survivor has no collider (bullets and enemies ignore it) and no light of its own (each seat's real
torch lights the world and the others' bodies, never its own weapon: `SurvivorLight`). On the desert's motorbike ([../game/the-desert.md](../game/the-desert.md), "The motorbike") the
avatar looks where the bike points (`PlayerController.RideHeading`) and is seated on it (pelvis on the saddle, feet on the footrests, hands on the grips, leaning with the bike: the `seated` pose of
`SurvivorBody`), and the first-person arms of the bike are not shown (the others would see them floating beside the avatar). The weapon stays hidden while the rider only rides and is in the right hand when it fires from the saddle (`SurvivorBody.PoseRideGun`, the arm aimed where the bike points, the left hand on the grip).

How the screen is made: each seat camera is a copy of the main one (URP settings included) with its own `Camera.rect`. The weapon and hands of
seat n live on layer 24 + n, and a camera draws only its own, so nobody sees another player's gun. There is one `AudioListener`, on player 1's
camera. The HUDs hang under one overlay canvas, each in its rectangle. Lights, shadows, level of detail and billboards that depend on "the
camera" use the nearest player's eye (`Seats.NearestEye`). `SeatInput` gives the keyboard and the mouse only to the seat that has them, and each
seat reads its own `PadControls.Channel`. The pause freezes time for everyone (`Time.timeScale`, as in single player, unlike the online co-op).
A grimoire does not: `GameMenu.Overlay` belongs to the seat (`Seat.overlay`, swapped by `Seats.Install`), `PadControls.BlockedFor(channel)`
silences only the reader's controller, `GrimoireUI` keeps one book per seat under that seat's HUD (so it is drawn, scaled and kept inside
that seat's rectangle), and the reader's IMGUI minimap is hidden while the others' stay.

Code (`Assets/_PryingEyes/Scripts/`): `Coop/Seats.cs` (seat, context swap, helpers), `Coop/Seats.Rig.cs` (birth of seats 2 to 4, layout, HUD
placement, borders), `Coop/SeatInput.cs`, `Coop/SeatTarget.cs`, `Coop/SplitScreen.cs` (pure rules: layout, controller assignment, field of view,
start), `Coop/SplitScreen.Monitors.cs` and `Coop/WinWindows.cs` (monitors and the Windows windows), `PadControls.cs` (XInput and the
channels), `GameMenu.Split.cs` (panel, pause and leave texts), `SeatsTest.cs` and `SplitScreenTest.cs`.
About fifty other files wrap an entry point or ask for "every player": `Enemy*`, `FloorDirector`, `RoundDirector`, `Drops`, `DropPickup`,
`Equipment`, `Mine`, `Barrels`, `Chandeliers`, `HUD*`, `PlayerController`, `Weapon*`, `Fx`, `LightPool`, `Power` and others. The split screen
exists only in the Windows player (XInput) and only on the hotel floors; elsewhere the menu button is off.

### Command line

| Flag | Meaning |
| --- | --- |
| `-splitscreen N` | Start a split screen of N players (2 to 4) by itself, as if the menu button had been pressed |
| `-splitpad1` | With `-splitscreen`, player 1 uses a controller too |
| `-splitmonitors N` | Use N monitors (1 to 4) instead of the choice saved by the menu. With fewer real monitors the extra ones are pretend ones: cameras, canvases and HUDs are placed all the same and nothing is shown (this is what the tests use). Without the flag, `-autotest` and the tests use one monitor |
| `-seatstest` | The in-process test below (not in distribution builds) |
| `-seatsshots` | With `-seatstest` in a real window (no `-batchmode`), save photos of the whole screen in `unity_logs/seats/` |
| `-splitdesertotest` | The in-process test of the desert in split screen (below; with `-splitscreen N -deserto -catacombe -autotest`; not in distribution builds). `tools/coop/run_split.ps1 -Desert` passes it |

## Verification

`-seatstest` runs inside the game, with a fake XInput state injected per player (`PadControls.Inject(channel, state)`), and exits `0` only when
every check passes. It never reads a physical controller: a process started with `-batchmode` skips XInput (`PadControls.PollReal`), so a pad in use at
the same moment cannot move the seats (it did, 19 November 2026: the 3 and 4 player runs failed with an Xbox controller in someone's hands). It has two ways to start the game: from the menu (no `-autotest`, with `-menureview -nowaves`: the menu presses the
button, so the real path *Schermo condiviso*, **Gioca** and the pause are covered) or with `-autotest -nofire -noenemies` (no menu, no pause test).
It checks:

- **Structure**: a body, a camera, a HUD, a target and a controller per player; cameras in their parts, covering the screen without
  overlaps; one `AudioListener`, on player 1's camera; each weapon on its own layer; each HUD inside its part; side-by-side arrival.
- **Seeing each other** (0.7.1): each player has a mannequin on their own avatar layer, each camera draws the others' and not its own,
  the mannequin stands where its player stands; with a GPU (batchmode without `-nographics`) player 2 stands three metres in front of
  player 1, player 1's camera renders the scene with and without player 2's layer (more than 1% of the pixels must change; 2.1-2.6% with
  2, 3 and 4 players) and the picture is saved as `hotel_build/seats/vedersi_N.png`.
- **Context swap**: inside a seat the globals are that seat's; equipment, knife, torch, guide and stealth net are different per seat; seat 1
  is installed again afterwards.
- **Characters** (0.9, 7 checks): every seat has its own character, cathedral and look, all different; seat 1 is the local character and has its
  cathedral at rest; the name of a seat is its character's; inside a seat the cathedral is that seat's; 3 bones picked up by one seat land only in the
  store of its character; afterwards the cathedral of seat 1 is installed again.
- **Controllers**: each player walks with their own controller while the others stand still; a trigger fires only that player's weapon; one
  player walks while another jumps, with no command crossing over.
- **Points and enemies**: points, hits and kills of one player are only theirs; an enemy appears on the nearest player and switches to a
  closer one; with `-candles` also `Provoke` and sight.
- **Damage, going down, revive, death and respawn** of one player, with the others untouched: at zero health player 2 goes down (alive,
  ignored by enemies, no death counted), player 1 holds Use next to them and they get up with half health, then they go down again and get
  up by themselves (once per floor), then they bleed out and die and respawn. Each HUD lists the other players in its teammates panel
  (number, colour, health, distance and direction, downed state).
- **Grimoire** (17 November 2026): player 2 opens the arsenal book: only their book is open, only their overlay flag is set, time is
  not stopped, and the book's rectangle on the screen is inside their part (the log prints both sizes); player 1
  walks with their controller while the reader, even with the stick held, does not move; the D-pad of player 1 does not move the reader's
  choice and the reader's does; B of the reader's controller closes the book and gives the commands back.
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

Results on 17 November 2026 for several monitors (Windows 11, RTX 5060 Ti, a 3440x1440 monitor at 175% scale and a 3840x2160 one at 300%):
`-splittest` 79 of 79 (the rules of who stands on which monitor added 16 checks); `-seatstest` from the menu without a window: 3 players on
one monitor 70 of 70 (the old path, unchanged), 2 players on 2 monitors 72 of 72, 3 on 2 74 of 74, 4 on 2 78 of 78, 4 on 4 80 of 80 (the extra
monitors are pretend ones: cameras, canvases, HUDs and the maps of the other monitors are built and checked, nothing is shown);
`MenuReview.Run` 921 checks with no failure (the panel with one, two and four monitors). With a window on the two real monitors, 2 players,
80 of 80 checks in 58 seconds: Unity opened a window on the second monitor at its native size, no other program covered either window, the
game held 60 frames per second (its limit) on an idle floor, the map of player 2 on the second monitor drew the six visited rooms with their
labels, the door marks and the markers, a window put over the one of the second monitor (a window of the game's own process that takes no
focus) was noticed and the covered window was raised again with the game still running, and the extra window was hidden at the end. The
photos taken from the system at eight moments (`unity_logs/seats/NAME-monitor1.png`, `NAME-monitor2.png`) show player 1 filling the first
monitor and player 2 the second, each with their own HUD scaled to the monitor, the open map and the minimap of player 2 on the second
monitor, player 2 downed (red view, *A terra*, 30 seconds) on the second monitor, the pause menu on the first and the **IN PAUSA** veil on
the second.

On the integrated tree of 0.8.0 (the split screen together with the map in each player's HUD, the grimoire of each player, the third person
and the survivors' bodies, the new menu and the seals), on the evening of the same day, from the verification build and with no window:
`-splittest` 79 of 79; `-seatstest` from the menu on one monitor 82 of 82 with two players, 87 of 87 with three and 92 of 92 with four; with
pretend monitors 84 of 84 (two players on two monitors) and 94 of 94 (four players on two, and on four). The counts are higher than the ones
above because the pieces of work merged here added checks to the same test (the map of each seat, the grimoire of each player, the bodies of
the survivors). `-padtest -nowaves` 83 of 83. What was not repeated on the integrated tree: the run with a window on the two real monitors
and the photos taken from the system (they need the screen and the maintainer's attention).

The refactor touched about fifty files of the single-player game, so the default (wave) cycle was checked again the same day, from the same
build: `-padtest` 73 of 73, `tools/check_gunplay.py` 297 of 297, `-barreltest` 19, `-lampadaritest` 15, `-equiptest` 19, `-minetest` 12,
`-flinchtest` 9, `-shafttest` 9, `-powertest` 16, `-survivaltest` 86, `-ragdolltest` 16, and scenario `a` of the online co-op
(`tools/coop/run_all.ps1`: 50 checks on the host, 38 on the guest), all passing. The first run of two of them failed for reasons older than
the split screen, and both were fixed: `-survivaltest` did not count the points that the medals pay during a wave, and `-ragdolltest` failed
by chance because the ragdoll froze a monk's body while its staff was still falling (the staff stayed in mid-air). The checks of the old candle
and stealth cycle (`-stealthtest`, `-roundtest`, `-knifetest`, `-droptest`, `-booktest`, `-gridtest`, `-holescan`, `-pushtest`,
`check_navigation.py --legacy`) were not run again: that cycle is off unless `-candles` is given.

On 18 November 2026, with the characters of 0.9 (the build of that day, from the menu, no window): `-seatstest` passed 89 of 89 with two players and
99 of 99 with four, among them the 7 checks on the characters of the seats; `Chi gioca?` was checked by `MenuReview` (2790 checks, the panel at
the four sizes and its photograph at 1280x720). Not run on that build: three players, `-splittest`, several monitors.

### The desert in split screen (0.9.1)

`-splitdesertotest` (`Scripts/Deserto/SplitDesertTest.cs`, started by `tools/coop/run_split.ps1 -Players N -Desert [-Graphics] [-Monitors M]`) starts the desert with N seats and checks that every seat camera has the 1,500 m far plane
and the solid background; that there is one instance of the sand, the animals, the veils and the dust devils per seat and one of the fires; that the seats arrive 0.36 m apart and each HUD has the desert objective; that there is one motorbike for each seat, each at its own home, and that seat 2 gets on its own and rides it while the others stay on foot (no arms of the bike on the screen, its body seen seated on it by the others with its hands on the grips) and leaves it parked where it gets off, each seat's minimap marking the parked bike of that seat and not another's; that **seat 2, taken 2.6 km
away** (to a real chapel), stands on sand with a collider, has navmesh round it and round seat 1, has props and loot, has the chapel next to it built with its skin and an outdoor sky, and is not on seat 1's minimap (the seats that stayed home are); that 70 points picked up
by seat 2 are seat 2's and not seat 1's; that the objective lines differ (seat 2's says *cattedrale a 2550 m*); that a seat sent beyond the edge of the world is brought back and the other does not move; and that, brought back together, each minimap shows the
others. With a graphics card it renders each camera at noon and checks that the top of the picture is sky and not the fog-coloured background (`hotel_build/deserto/split_posto1.png`, `split_posto2.png`), and with `-seatsshots` over real monitors it asks the script for the
system photos of every monitor (`unity_logs/seats/deserto-lontano-monitor1.png` and so on). Results of 19 November 2026 on the tree that became 0.9.1: two seats 31 of 31, three 33 of 33, four 35 of 35 without a window (about 25 s each); two seats on two real monitors
(the 3440x1440 primary and the 4K one) 33 of 33 in 30 s at 60 frames per second, with the four photos looked at. The motorbike step (7 checks) was added on 19 November 2026, with the
motorbike: two seats 38 of 38, three 40 of 40 and four 42 of 42 (about 28 s each) without a window, and two seats 40 of 40 with the graphics card and no window, which also photographs seat 2 riding away from the stair as seat 1 sees it
(`hotel_build/deserto/split_moto_posto2_in_sella.png`, looked at). The same day a second round (the rider seated with its hands on the grips, the disc of each seat's own bike on its minimap) made the step 10 checks: two seats **41 of 41**, three **43 of 43** and
four **45 of 45** (28 to 30 s each) without a window, and two seats **43 of 43** with the graphics card and no window, which photographs seat 2 straight and in a bend (`split_moto_posto2_in_sella.png`, `split_moto_posto2_in_curva.png`, looked at: seated, banking with the bike);
a third round (0.9.3: each seat's own speedometer, and seat 2 firing from the saddle while the others see the weapon in its right hand) made the step 15 checks: two seats **46 of 46**, three **48 of 48** and four **50 of 50** (28 to 31 s each) without a window, and two seats **48 of 48** with the graphics card (a 1280x720 window, 37 s),
which also photographs seat 2 at a standstill firing the rifle, seen by seat 1 from the front on the right (`split_moto_posto2_spara.png`, looked at: the rifle is in the right hand and points where the bike points; the first version of that photograph, with the pistol and from the left while riding, showed no weapon at all, since the pistol is small and the right hand was on the far side, so it was replaced);
the other counts above are those of 0.9.1, before the step. On 20 November 2026 a step of 5 checks followed, *una torre*: the first two seats go to the landmark tower (8 floors), seat 1 to the entrance hall and seat 2 five floors up (29 m), and both floors must be furnished, since what is furnished in a tower or a catacomb is wanted by every seat and no longer only by the nearest one (`SiteRt.UpdateWant`): two seats **51 of 51** in 32 s, three **53 of 53** (33 s) and four **55 of 55** (34 s) without a window; with `-legacydress`, the old rule, two seats 50 of 51 (seat 2's floor still empty after 40 s, 2 floors furnished out of 8).

## Not verified, limits

- A **physical controller**: none was connected. The XInput calls answer without errors and everything else ran on injected states.
- Nobody has played a session with several people: the tests drive the controllers, not a human.
- **Audio**: there is one listener, on player 1's camera, so every sound is heard from where player 1 stands; far from player 1 the
  others' shots and steps are faint. Music and interface sounds are not affected. A fair mix needs a custom mixer (one listener per scene
  is a Unity rule).
- **The desert in split screen** ran with real windows only for two seats on two real monitors; three and four ran without a window. Nobody played it with a second person. The sound is the single listener's (a seat
  kilometres from player 1 hears nothing of what happens round it), the look is global (a seat indoors beside one outdoors gets the outdoor look) and the cost of a frame with four seats looking at four different parts of the
  desert was not measured.
- **Performance** with four views on a large monitor (for example 3440x1440, four parts of 1720x720) is not measured: the tests ran without
  a window or in 1280x720. Four cameras draw the world four times; lights and shadows follow the nearest eye to keep it down.
- The keyboard and the mouse belong to player 1 and cannot be handed to another player. There is no joining or leaving in the middle of a run
  and no lobby ("press A to join"); the number of players is chosen on the panel.
- **Several monitors**: the map and the minimap are drawn by `Proc/Map/MapView` inside the HUD of every player, and the HUD of each seat
  sits on the canvas of its own monitor, so a player on another monitor has the same map as the others (no second copy: the uGUI stop-gap
  that drew it for the monitors after the first, `SeatMapUi`, was removed when the map moved into the HUD). The register of the Factory in the
  cathedral hub (`HubInteract`) is IMGUI, and Unity draws IMGUI only on the first monitor: it is drawn over the whole first monitor, whoever
  opened it, and it closes by the distance of
  player 1 (on one monitor too, for the players after the first). The extra windows take the monitor's native size and the display settings
  of the menu (resolution, quality) apply to the game window only. The players cannot have different display settings. The guest-style
  choice of the online co-op (an address) does not exist here: the players' names are those of their characters.
- **Several monitors, not tried for real**: three and four monitors (tested only with pretend ones); a program of another process covering a
  game window (the test puts a window of the game's own process over it; on the first real run a browser covered the window of the second
  monitor, before the raising existed); clicking in the window of the second monitor and what happens to the keyboard there (the test
  drives controllers, not the keyboard and the mouse); a monitor that is switched off in the middle of a run; the speed with four players on
  two large monitors (the 60 frames per second above are one idle floor with two players).
- The windows of the extra monitors stay open (hidden) after the split screen ends: Unity cannot close the window of a monitor it has opened.
- It cannot be combined with the online co-op: the button is off while a co-op game is on, and `-splitscreen` with `-coophost` does not start.
- The old candle cycle (`-candles`) works but is the less used path; the wave cycle is the default.
