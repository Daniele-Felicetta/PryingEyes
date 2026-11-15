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
**3 giocatori** and **4 giocatori**, a switch for player 1 (**tastiera e mouse** or **controller**), the number of controllers it sees, and a
reminder that a click or Alt+Tab moves the keyboard and the mouse to another window. The buttons are off, with the reason, outside the
Windows player or while a game is already running. In the other windows the menu is replaced by a waiting screen (*Cerco la partita del
giocatore 1...*). The pause menu of a split-screen window says **Chiudi lo split screen** (window 1) or **Lascia la partita**, and, when the
window uses a controller, lists the controller's buttons. Window layout, controls and checks: [../multiplayer/split-screen.md](../multiplayer/split-screen.md).
The windows of players 2 to 4 skip the saved display settings: each has the size of its part of the screen.

## Verification

- `PryingEyes.EditorTools.MenuReview.Run` renders main menu, display, comfort, guide and co-op panels at 1280×720,
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
