# Controller (gamepad)

Added on 15 November 2026, after release 0.6.0. In 0.6.0 a controller worked only in a split screen and the menus ignored most of it.
Now an XInput controller plays the normal game, the online co-op and the split screen, and drives every menu.

Only **XInput** controllers are read (Xbox 360, One, Series and compatible; `xinput1_4.dll`, `xinput1_3.dll` as a fallback). A PlayStation or
Switch controller needs an XInput wrapper such as Steam Input or DS4Windows (not verified). Windows only.

## How the game finds the controller

- **Normal game and online co-op**: the game looks at the four XInput slots twice a second and adopts the first controller on which a
  button is pressed, a trigger is pulled or a stick is pushed past 55%. A controller that is only plugged in, or one whose stick does not
  return to the centre, therefore moves nobody. If the controller is gone for more than two seconds the slot is let go and the search
  starts again (another controller can take over). Nothing is read while the game window has no focus.
- **Split screen**: the slots are given out by `SplitScreen` and never searched (see [split-screen.md](../multiplayer/split-screen.md));
  those windows keep reading their controller without focus.
- The on-screen hints name the keys of the device you used last: `RB` instead of `F`, the controller line in the guide and the pause menu,
  the footer of the menus. A key press or a mouse click switches them back to the keyboard names; touching the controller switches them again.
- Automated runs do not search (`-batchmode`, `-autotest` and the like) unless `-padauto` is given, so a controller on the desk does not
  disturb a test.

## Playing

| Control | Action |
| --- | --- |
| Left stick | Move (pressed: run on/off; it also stops when you stop, aim or fire) |
| Right stick | Look (pressed: knife) |
| RT / LT | Fire / aim |
| A / B / X / Y | Jump / crouch (hold) / reload / change weapon |
| RB | Use, buy, pick up (`F`) |
| D-pad left, right | Lean |
| D-pad up / down | Torch / holy water |
| LB + A, B, X, Y | Veil / fire mode / music box / reliquary mine |
| LB + D-pad up, down, left | Map / ping the doors / replace the battery |
| Start / Back (hold) | Pause / scoreboard |

The sticks have dead zones (7849 and 8689 of 32767), the triggers switch on at half a stroke and off at 30%; look speed follows a curve, so
small movements are fine and a full push turns fast (3.2 rad/s at most horizontally at sensitivity 1). The controller goes through the same
`TouchControls` actions as the touch buttons (`TouchControls.Held`, `Down`, `Move`), so every action has exactly one code path.
**While a menu is open the controller gives no game commands** (A on *Gioca* or *Riprendi* does not jump, the triggers do not fire, the
sticks do not move you); only Start, which the menu uses, gets through.

## In the menus

| Control | Action |
| --- | --- |
| D-pad or left stick | Move the selection (one axis at a time; the stick switches on at 60% and off at 40%, so it does not flutter) |
| A | Confirm. On a slider nothing; on a text field it starts typing |
| B | Back. In the pause menu it resumes. In the game B is *crouch* and never opens the pause |
| Start | Open or close the pause, or go back, like Esc |
| D-pad left / right on a slider | Lower / raise it by 10% of its range |
| A, B or the D-pad in a text field being edited | Stop editing (the D-pad also moves on) |

- A brass frame marks the selected item whenever you navigate with the controller or the keyboard.
- Going back lands on the button that opened the screen, not on the first one.
- The two tabs of the settings are chosen with left and right and lead down to the first control of the open tab.
- After a click in empty space nothing is selected; the first controller or keyboard command then only selects a button, and the next one moves.
- Selecting a text field does not start typing (a mouse click, Enter or A does), so the controller cannot get stuck in a field.
- The mouse pointer is hidden while you navigate with the controller or the keyboard, and the buttons ignore a resting pointer; moving the mouse
  brings it back.
- The display confirmation (*L'immagine è nitida?*) takes A to keep and B to restore (read in the code, not run with the controller: it changes the
  real screen mode).

The pause menu lists the controller's buttons when the controller is the device in use.

## How it works

Unity's own UI input reads the left stick and A through its joystick axes (no D-pad, no B), and reading XInput as well would press a button
twice. `PadMenuInput` replaces the input of the uGUI `StandaloneInputModule` (a `BaseInput` override) for the keyboard, the mouse pointer and
the controller together, reading the controller from `PadControls`. It also hides the pointer while navigating (it reports a position off the
screen) and selects a button when nothing is selected. `PadEventSystem` keeps the UI listening in a split-screen window that has no focus (by design: in the visible test every window reported focus,
so the unfocused case itself was not observed).
`PadControls.EnableAuto` is called by `GameManager.Awake`; `Mapper` holds the translation from the raw XInput state, including the menu
direction, confirm and back; `GameMenu.Pad.cs` has the frame, the hints and the selection rules.

Code (`Assets/_PryingEyes/Scripts/`): `PadControls.cs`, `PadMenuInput.cs`, `GameMenu.Pad.cs`, `GameMenu.cs`, `GameMenu.Design.cs`,
`GameMenu.Display.cs`, `GameMenu.Split.cs`, `TouchControls.cs`, `PadMenuTest.cs`.

## Verification

- `-padtest -nowaves` (no `-autotest`: the menu must exist), 73 checks passed on 15 November 2026: the XInput to menu translation (D-pad and
  stick with hysteresis, one axis, A and B as single presses, gameplay blocked while a menu is open); the automatic search with a fake XInput
  (`PadControls.TestPoll`): a plugged-in idle controller is not taken, the first press is, it is let go after two seconds, another slot takes
  over; the real menu driven with a fake state (`PadControls.Inject`): selection with D-pad and stick, the frame on the selection, settings tabs,
  sliders adjusted left and right, back to the button of origin, the guide, the co-op text field (no trap), the first command with nothing
  selected; and in the game: A on *Gioca* does not jump, Start opens the pause, B resumes it, B in the game does not pause, A on *Riprendi* does
  not jump, A in the game jumps.
- `-padtest -padshots FOLDER` (a real window, with `-menureview` so the saved display is not applied) saves screenshots of the menus driven the
  same way: the frame, the hints, the guide, the pause. Used on 15 November 2026 to review the frame and the pause sheet; the registry
  `Screenmanager` values were restored afterwards.
- `-splittest` (85 of 85) and `tools/coop/run_split.ps1 -Players 2` still pass; `MenuReview.Run` (609 checks) too.
- **Not verified**: a physical controller (none was available; the real XInput read answers without errors and the rest runs on fake
  states), PlayStation and Switch controllers through a wrapper, several controllers in the normal game (the first one pressed wins),
  Steam Input, controller hot-plug during a real game, and the look of the menus at resolutions other than 1024x720.
