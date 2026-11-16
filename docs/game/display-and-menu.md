# Display controls and hotel menu

The main menu is a brass-and-patina hotel register with a procedural cathedral/eye engraving. Artwork is native UI mesh
geometry, not a fixed-size bitmap. Display type uses the existing Georgia/Palatino fallback stack; utility text uses
Bahnschrift/Segoe UI. The iris drifts using unscaled time and stops when visual motion is disabled. Keyboard focus,
mouse, pause, guide and co-op flows use the existing Unity UI/EventSystem controls.

## Display behavior

Settings has separate **Schermo** and **Audio e comandi** tabs. Display controls include:

- Borderless fullscreen, exclusive fullscreen and windowed mode.
- Resolution selection and aspect filters for all formats, 16:9, 21:9 and 16:10.
- Common windowed/borderless presets through 3440×1440 QHD ultrawide (including 2560×1080 and 2560×1440), plus resolutions
  reported by the connected monitor. Native monitor modes above QHD are preserved rather than artificially capped.
- Refresh rates reported by the monitor for exclusive fullscreen. Other modes use the desktop refresh rate. Fractional
  refresh rates retain Unity's native numerator/denominator when applied.
- VSync and frame caps: unlimited, 30, 60, 90, 120, 144, 165 and 240 FPS. VSync takes precedence over the frame cap.

Exclusive fullscreen offers only monitor-reported resolutions. Other modes may be constrained by the desktop/window
manager; the confirmation persists the resolution Unity actually applied. See the official
[Screen.SetResolution](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/screen/setresolution) and
[Screen.resolutions](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/screen/resolutions) contracts.

**Applica schermo** starts a 15-second preview measured in real time, including while the game is paused. Underlying
settings controls are disabled until confirmation. **Mantieni impostazioni** persists the applied configuration;
**Ripristina**, Escape, timeout and menu destruction restore the preceding configuration. Leaving settings without
applying discards the draft. Comfort/audio controls retain their existing immediate behavior.

The first ordinary launch uses native desktop resolution in borderless mode, overriding stale Unity Screenmanager
values left by earlier tests. Later launches apply the last explicitly confirmed display configuration. Automated runs
skip startup display changes and remain silent; they retain any requested command-line render size.

## Split-screen entry

The main menu has **Schermo condiviso** between **Cooperativa** and **Come si gioca** (`GameMenu.Split.cs`). Its panel offers **2 giocatori**,
**3 giocatori** and **4 giocatori**, a switch for player 1 (**tastiera e mouse** or **controller (e tastiera)**), the number of controllers it
sees and how many two players need, and a note on who uses what. The buttons are off, with the reason, outside the Windows player or while a
game is already running. A split screen is one game in one window, so the display settings are the ordinary ones. The pause menu names who
uses what (*G1: tastiera e mouse   G2: controller 1 ...*), says that Esc or Start on any controller resumes, lists the controller's buttons
when a controller is in use, and **Torna al menu** warns that the split screen closes for every player. Layout, controls and checks:
[../multiplayer/split-screen.md](../multiplayer/split-screen.md).

## Keyboard and controller navigation

The menu input is `PadMenuInput`, not Unity's default: arrows, WASD, D-pad and the left stick move the selection, Enter, Space and A confirm,
Esc and B go back, Start toggles the pause. A brass frame marks the selected item when you navigate without the mouse, the footer and the pause
text name the keys of the device in use, the two settings tabs are chosen with left and right, sliders are adjusted with left and right, and a
text field is entered only with Enter, A or a click. Details and checks: [controller.md](controller.md).

## Verification

- `PryingEyes.EditorTools.MenuReview.Run` renders main menu, display, comfort, guide, co-op and split-screen panels at 1280×720,
  1920×1080, 3440×1440 and 1024×768; also checks pause/resume, leave confirmation, text bounds and real engraving meshes.
  It checks ultrawide presets, filtering, input validation and the seven existing comfort controls. Output:
  `unity_logs/menu-review`. This runs in an empty editor scene without changing player display preferences.
- `python tools/check_display.py`: explicit Windows acceptance check against `Builds/Audit`. Exercises actual window
  resolution changes, timeout rollback while paused, explicit cancellation, confirmation/persistence and audio muting.
  The harness restores the exact preceding `display_*`, Screenmanager and monitor-selection registry values in `finally`.
  Other game instances must be closed. It does not run `-uitest` or `check_presentation.py`.
- Standard suite boundaries and hermetic Python tests remain separate from the desktop acceptance check.

Verified on 15 November 2026: all editor layout/mesh checks passed at the four sizes, the rebuilt player's display
acceptance check passed 9/9 with registry restoration confirmed, suite boundaries passed with zero findings, and all
73 hermetic Python tests passed. No presentation test that changes the player's settings was run.

Source: `GameMenu.cs`, `GameMenu.Design.cs`, `GameMenu.Display.cs`, `GameMenu.Split.cs`, `MenuEngraving.cs`, `VideoSettings.cs`.
