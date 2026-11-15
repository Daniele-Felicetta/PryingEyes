# Split screen on one PC (2 to 4 players)

Added on 15 November 2026. Several people play on one computer: main menu, **Schermo condiviso**, then **2 giocatori**,
**3 giocatori** or **4 giocatori**. Each player gets a borderless window of their own and the windows tile the monitor.
It is the local co-op of [the co-op page](README.md) (one shared hotel, enemies simulated by the host, points, weapons and perks
per player), with the other players' windows opened by the game itself instead of by other machines.

## How to play

1. Player 1 starts the game, chooses **Schermo condiviso** and the number of players. The button **Giocatore 1** switches player 1
   between keyboard and mouse and a controller. The panel shows how many controllers it sees.
2. The game shrinks to its part of the screen, opens the other windows and starts player 1's run at once. The other windows show
   *Cerco la partita del giocatore 1...* and join as soon as they are ready (8 to 16 seconds for 2 to 4 players on the
   verification machine).
3. **Keyboard and mouse** go to the window that has focus (a Windows rule): player 1's at the start; a click on another window, or
   Alt+Tab, moves them there. **Controllers** are read straight from XInput, so they work in a window that has no focus. The connected
   controllers are given out in order: player 2 gets the first, player 3 the second, player 4 the third (when player 1 plays with a
   controller too, player 1 gets the first and the others shift by one). A player without a controller waits for it; plugging one in
   later works (a missing controller is looked for twice a second).
4. Pause (Esc, or Start on a controller) stops only that player's commands, as in the online co-op. The pause menu says
   **Chiudi lo split screen** in window 1 (the game closes, and the other windows close with it) and **Lascia la partita** in the
   others (only that window closes; the others go on).

## Layout

The area is the work area of the monitor that holds window 1 (the taskbar is left free). The parts cover it exactly, with no gaps and no
overlaps (`SplitScreen.TileOf`):

| Players | Window 1 | Window 2 | Window 3 | Window 4 |
| --- | --- | --- | --- | --- |
| 2 | left half | right half | | |
| 3 | top left | top right | bottom, full width | |
| 4 | top left | top right | bottom left | bottom right |

The windows are borderless (`WS_POPUP`) and the game puts them back every 0.25 s for the first minute (Unity can move a window
by itself while it starts), then every second. The game's field of view is vertical (75 by default, the setting); a wide window
would stretch it, so the horizontal angle is capped at 125 degrees (`SplitScreen.FovFor`, used only in a split screen).

## Controller

The mapping, the menus and the on-screen hints are on [the controller page](../game/controller.md); a split screen uses the same ones. What is
specific to a split screen: the slots are given out by `SplitScreen` and never searched for (player 2 gets the first connected controller, and so
on), the controllers are read straight from XInput so they work in a window that has no focus, and a window driven by a controller never locks the
cursor (the mouse belongs to player 1; the cursor shows only while a menu is open). The pause menu of such a window lists the controller's buttons,
and its menu is meant to answer to the controller even without focus (`PadEventSystem`; not observed, the windows of the visible test all reported focus).

## How it works

The game keeps the state of one local player per process (points, weapons, perks, HUD, torch and music are statics; `GameManager.I` is
used in 247 places), and the online co-op was already built and tested for one instance per player. So a split screen is not a second
camera in one process: it is **N game processes tiled on one screen**, connected by the existing co-op over loopback.

- Player 1 hosts a local co-op (`Coop.StartHost(port, true)`: UDP, loopback only, a free random port between 20000 and 40000), moves
  and resizes its window, and launches the other players with `SplitScreen.ChildArgs`: the position (`-splittile`), the controller
  (`-padindex`), the host's process id (`-splitparent`) and the address to join (`-coopjoin 127.0.0.1 -coopport N`, name
  `Giocatore N`).
- Windows 2 to 4 join like any guest. Everything the online co-op does applies, including the floor built on each process from the same seed.
- Music is muted in windows 2 to 4 (`GameSettings.Music` returns 0), they do not pause when they lose focus, they use vSync, and the
  display settings saved by the player are not applied to them (`VideoSettings`): each has the size of its part.
- Windows 2 to 4 leave by themselves when window 1 is gone (they watch its process, and the host closes them on a clean exit), when the
  co-op closes after they started, or if they cannot join within 120 seconds (they retry every 3 seconds, 25 times).
- When a new window appears and takes the focus, window 1 takes it back, so keyboard and mouse start with player 1.

Code (`Assets/_PryingEyes/Scripts/`): `Coop/SplitScreen.cs` (layout, controller assignment, field of view, start), `Coop/SplitScreen.Win.cs`
(Windows only: window placement, children, watchdogs), `PadControls.cs` (XInput and the mapping), `GameMenu.Split.cs` (the panel, the waiting
screen, the pause and leave texts), `SplitScreenTest.cs`. `TouchControls`, `PlayerController`, `Equipment`, `HUD.Score`, `GameSettings`,
`VideoSettings`, `GameManager` and `GameMenu` were touched. On other systems than Windows the menu button is disabled.

### Command line

| Flag | Meaning |
| --- | --- |
| `-splitscreen N` | Window 1 starts a split screen of N players (2 to 4) by itself, as if the button had been pressed |
| `-splitpad1` | With `-splitscreen`, player 1 uses a controller too |
| `-splitport N` | Use this UDP port instead of a random free one |
| `-splitlogdir DIR` | Where the other windows write their logs (`giocatore2.log`...; default `persistentDataPath/split`) |
| `-splitextra "ARGS"` | Extra arguments for the other windows (the tests pass `-batchmode -nographics -padsim giro`) |
| `-splitlog` | Every 4 seconds a line with where this window is and what it receives, and where the host sees each companion |
| `-padsim avanti` / `giro` | A fake controller that walks forward / walks forward and turns (not in distribution builds) |
| `-splitchild`, `-splitcount`, `-splittile`, `-splitparent`, `-padindex` | Set by window 1 for the others: not for hand use |

## Verification

- `-splittest` (`-autotest -nofire -noenemies -splittest`, no windows, no controller): the layout for 2, 3 and 4 players on several
  screens, including one with a negative origin; the controller assignment; the field-of-view cap; the arguments of the launched windows;
  the XInput mapping (dead zones, triggers, the LB layer, run toggle); and the real player, driven with a fake XInput state, walking,
  turning, firing, jumping, crouching and running. 85 of 85 checks passed on 15 November 2026. It reads XInput for real only as "answers
  without errors": no physical controller was available.
- `tools/coop/run_split.ps1` starts the whole thing from the verification build (`Builds/Audit`): window 1 with `-splitscreen N`, the other
  windows launched by the game, a fake controller on players 2 to 4. It checks that every player is admitted, that each window knows its
  part and controller, that no process logs an exception, that window 1 sees player 2 move, and that closing window 1 closes the others
  (within 3 seconds). `-Graphics` uses real windows and also checks each window's rectangle and borderless style with Win32 and, with
  `-Shot`, saves a picture of the screen (`split_N.png` in `-LogDir`, default `%TEMP%\pryingeyes-split`):

```powershell
.\tools\coop\run_split.ps1 -Players 2                 # no graphics: launch, join, move, exit together
.\tools\coop\run_split.ps1 -Players 4 -Graphics -Shot # real windows; uses the GPU and fills the screen for about a minute
```

Results on 15 November 2026 (a 1024x768 monitor, work area 1024x708): 2, 3 and 4 players, with and without graphics, all passed; the windows were at
exactly the expected rectangles and borderless, and the pictures show four different views with their own HUD. See
[testing.md](../engineering/testing.md#5-co-op-fishnet-udp-port-7807) for the other co-op checks.

## Not verified, limits

- A **physical controller**: none was connected. The XInput calls were checked to answer without errors, the mapping and the real player
  were checked with injected states, and the windows with a fake controller, but the real reading of a pad is untested.
- Keyboard and mouse in a window that is not window 1 were checked only as "the focus can move"; no one played a session with several
  people.
- One monitor: the windows use the work area of the monitor of window 1. Several monitors are not used. Display scaling other than the
  125% of the test machine is not verified (the placement uses real pixels).
- The performance with four players on a modest machine is not measured: four copies of the game run at once.
- A split screen cannot be combined with an online co-op: the menu button is disabled while a co-op game is on.
- Controllers also work in the normal game and the online co-op since 15 November 2026 (the game finds the controller by itself, see
  [the controller page](../game/controller.md)); in a split screen the slots stay under `SplitScreen`'s control, and player 1 without a
  controller does not search for one, because the controllers belong to the other players.
