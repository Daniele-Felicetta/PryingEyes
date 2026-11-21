# Display controls and hotel menu

The main menu is a brass-and-patina hotel register with a procedural cathedral/eye engraving. Artwork is native UI mesh
geometry, not a fixed-size bitmap. Display type uses the existing Georgia/Palatino fallback stack; utility text uses
Bahnschrift/Segoe UI. Keyboard focus, mouse, pause, guide and co-op flows use the existing Unity UI/EventSystem controls.

Since 0.8.0 the engraving's iris looks at the mouse pointer (or at the entry picked with the keyboard or the controller), blinks every few
seconds and a little dust drifts in the room; every panel fades in and rises 14 units in 0.2 s (`MenuPanelFade`); the six main buttons slide
their text a few units when hovered or selected (`MenuButtonFx`). Everything that moves uses unscaled time and stands still when
**Oscillazione visuale** is at zero.

## Settings (0.8.0)

Settings has five tabs: **Schermo**, **Immagine**, **Audio**, **Comandi** and **Gioco**. Each tab is a list of rows (the label on the left,
the control on the right); a row is a slider or a choice with its values in a line (`MenuChoice`: ‹ value ›, left and right step it, Enter or A
steps it forward, a click steps back on its left edge and forward elsewhere). Moving onto a row writes a sentence about it in the line under the list, so nothing needs a manual. Every row saves at once
in `PlayerPrefs` (only the display is tried and confirmed, below); **Indietro** asks nothing and **Ripristina scheda** puts the open tab's
rows back to their defaults (the Schermo tab has no such button: it is confirmed with **Applica schermo**). A tab opens by selecting it (the
mouse, left and right, **PagSu** and **PagGiu** from any row), and the settings reopen on the tab last used. Code: `GameMenu.Settings.cs` (the rows and the tabs), `GameMenu.Display.cs` (the Schermo tab),
`MenuWidgets.cs` (`MenuChoice`, `MenuFeedback`), `GameSettings.cs` and `GameSettings.More.cs` (the stored values and what they do).

| Tab | Rows (default) |
| --- | --- |
| Schermo | Modalità schermo, Formato, Risoluzione, Frequenza monitor (exclusive fullscreen only), Sincronizzazione verticale, Limite fotogrammi (only without VSync), **Applica schermo** |
| Immagine | Luminosità, Campo visivo, Qualità delle ombre (Alta), Risoluzione di rendering (100%, 50% to 100%), Anti-aliasing (4×, off / 2× / 4×), Grana della pellicola, Vignettatura, Bagliore (the last three 100% of each theme's own amount, down to 0) |
| Audio | Volume generale, Effetti sonori, Musica, **Ambiente** (the hum, wind, murmurs, creaks and distant bells, which used to follow the effects), **Tema del menu** ([menu-music.md](menu-music.md)), Suoni dell'interfaccia (on) |
| Comandi | Sensibilità mira (mouse), Sensibilità controller (right stick, 0.25× to 3×), Asse Y del mouse and Asse Y del controller (normal or inverted), Mira, Abbassarsi and Corsa (held, or one tap switches), **Elenco dei comandi** (opens the guide on the keys of the device in use) |
| Gioco | **Prospettiva** (*Prima persona*; *Terza persona* puts the camera behind the shoulder, see [third-person.md](third-person.md), and switches at once, in a game too; saved as `video_thirdperson`), Mirino (classic, dot only, none), Oscillazione visuale, HUD dei compagni (60% to 180%), Contatore fotogrammi (top left: frames per second, ms per frame and the worst frame of the last quarter of a second; **on** since 0.9.1, and the **F3** key turns it on and off anywhere, menu included, remembering the choice), Pausa a finestra inattiva (on; it stops the game by itself when you switch window) |

The Immagine settings touch the render pipeline asset only in the player: not in the editor and not in the automated runs, because a
modified asset would stay modified. The defaults are what the asset held, so a game that never opens the tab looks as it did. The effect
rows scale what the theme asks for (`GameSettings.SetThemeEffects`, applied by `FloorDirector.Look`); the toggles for aim, crouch and
sprint live in `PlayerController.Toggles.cs` and `Weapon.cs` (aim down sights), the crosshair in `HUD.cs`, the frame counter in
`FpsCounter.cs`.

## Display behavior

The **Schermo** tab is six choice rows (`GameMenu.Display.cs`), then **Applica schermo** with a line saying what is applied now. Display
controls include:

- Borderless fullscreen, exclusive fullscreen and windowed mode.
- Resolution selection and aspect filters for all formats, 16:9, 21:9 and 16:10.
- Common windowed/borderless presets through 3440×1440 QHD ultrawide (including 2560×1080 and 2560×1440), plus resolutions
  reported by the connected monitor. Native monitor modes above QHD are preserved rather than artificially capped.
- Refresh rates reported by the monitor for exclusive fullscreen. Other modes use the desktop refresh rate. Fractional
  refresh rates retain Unity's native numerator/denominator when applied. The row is dimmed in the other modes.
- VSync and frame caps: unlimited, 30, 60, 90, 120, 144, 165 and 240 FPS. VSync takes precedence over the frame cap, and the cap row is
  dimmed while VSync is on.

Exclusive fullscreen offers only monitor-reported resolutions. Other modes may be constrained by the desktop/window
manager; the confirmation persists the resolution Unity actually applied. See the official
[Screen.SetResolution](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/screen/setresolution) and
[Screen.resolutions](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/screen/resolutions) contracts.

**Applica schermo** starts a 15-second preview measured in real time, including while the game is paused. Underlying
settings controls are disabled until confirmation. **Mantieni impostazioni** persists the applied configuration;
**Ripristina**, Escape, timeout and menu destruction restore the preceding configuration. Leaving settings without
applying discards the draft. The rows of the other four tabs keep their immediate behavior. **HUD dei compagni** (Gioco tab, since 0.7.2: 60% to
180%, saved as `hud_coop_scale`) is the size of the teammates panel in co-op and split screen; it applies at once.

The first ordinary launch uses native desktop resolution in borderless mode, overriding stale Unity Screenmanager
values left by earlier tests. Later launches apply the last explicitly confirmed display configuration. Automated runs
skip startup display changes and remain silent; they retain any requested command-line render size.

## Split-screen entry

The main menu has **Schermo condiviso** between **Cooperativa** and **Come si gioca** (`GameMenu.Split.cs`). Its panel offers **2 giocatori**,
**3 giocatori** and **4 giocatori**, a switch for player 1 (**tastiera e mouse** or **controller (e tastiera)**), the number of controllers it
sees and how many two players need, and a note on who uses what. The line of controllers also counts the **monitors** (*Controller collegati: 0 ·
Monitor collegati: 2*), and the row of the player-1 switch always has a second button, **Monitor: uno solo / due / ...**. With more than one
monitor connected it cycles up to the number of connected monitors, is remembered between runs and starts at all of them, and the line under
the title says how the players are divided (*Su 2 monitor: 2 giocatori 1+1 · 3 giocatori 2+1 · 4 giocatori 2+2*). With one monitor it says
**uno solo** and, pressed, the status line explains how to get two (*collegalo in modalità Estendi (Win+P), poi riavvia il gioco*). Unity
counts the monitors when the game starts (`Display.displays`; the extra windows exist only for monitors present then), so a monitor that is
switched on or extended afterwards is not usable until the game is restarted: when Windows reports more monitors than the game saw
(`SplitScreen.MonitorsAddedAfterStart`, from `Screen.GetDisplayLayout`) the line shows *(Windows ne vede N)* and the status line says to
close and reopen the game (with a single monitor seen, the button reads **Monitor: riavvia il gioco** and says the same when pressed). The log has the same facts (`monitor all'avvio: Unity ne vede 1 (...),
Windows 1`, and again when the panel is opened). The buttons are off, with the reason, outside the Windows player or while a
game is already running. A split screen is one game, so the display settings are the ordinary ones (they apply to the game window; any further
monitor is opened by Unity at its own size). The pause menu names who
uses what (*G1: tastiera e mouse   G2: controller 1 ...*), says that Esc or Start on any controller resumes, lists the controller's buttons
when a controller is in use, and **Torna al menu** warns that the split screen closes for every player. After the number of players,
**Chi gioca?** asks which character plays in each seat ([saves-and-characters.md](saves-and-characters.md#playing-together)). Layout, controls and checks:
[../multiplayer/split-screen.md](../multiplayer/split-screen.md).

## Choosing how to play

**Gioca** (and **Ricomincia dalla cattedrale** after the suspended-expedition question) opens the panel **Come vuoi giocare?** with two buttons,
**Standard** and **Il deserto**, and **Indietro** (`GameMenu.Mode.cs`). The focus starts on the mode played last time, so Enter or A repeats it; B
or **Indietro** goes back to the main menu. The choice is kept in `PlayerPrefs` (`PryingEyes.GameMode`; the automated tests never write it) and
`-deserto` forces the desert. Since 0.9.1 **Ospita una partita** (Cooperativa) asks it too: the same panel, titled **Che partita ospiti?**, whose choice opens the door
(`GameMenu.HostNow`) and whose **Indietro** and B go back to the co-op panel; a guest is never asked, it starts where the host plays. Split screen asks it too (0.9.1): after
**Chi gioca?** (the characters of the seats) **Comincia** opens the same panel, titled **Che partita giocate?**, whose choice starts the run for every seat (`GameMenu.SplitBegin`, which says
if it did not start and then **Chi gioca?** shows the reason); **Indietro** and B go back to **Chi gioca?**. The desert itself is in [the-desert.md](the-desert.md).

## Characters and the suspended expedition

Since 0.9.0 the main menu has **Personaggi** (`GameMenu.Characters.cs`): the list of the characters, the selected
one with its level bar and its figures, **Nuovo personaggio** (a name and the look chosen with **Aspetto**; with no character, **Gioca** and the
co-op panel open it first), and **Elimina** with a confirmation that says where the file goes. The character in use shows under **Gioca** and in the
co-op panel (*Il tuo personaggio*). In the pause menu **Torna al menu** opens *Tornare al menu?*, which offers **Salva e torna al menu** beside
**Annulla** and **Lascia la partita** when saving is possible (`GameMenu.Run.cs`; the panel says why not when it is not), and **Gioca** with a
suspended expedition asks *Una spedizione ti aspetta* with **Riprendi la spedizione** and **Ricomincia dalla cattedrale**. What is kept and the rules:
[saves-and-characters.md](saves-and-characters.md).

## Keyboard and controller navigation

The menu input is `PadMenuInput`, not Unity's default: arrows, WASD, D-pad and the left stick move the selection, Enter, Space and A confirm,
Esc and B go back, Start toggles the pause. A brass frame marks the selected item when you navigate without the mouse, the footer and the pause
text name the keys of the device in use, and a text field is entered only with Enter, A or a click. In the settings (explicit chains, rebuilt
by `RebuildNav` for the open tab and skipping dimmed rows) left and right on the tab strip pick a tab and down enters its first row; up and
down walk the rows, the last one leads to **Indietro** and **Ripristina scheda**; left and right on a row change its value. Details and
checks: [controller.md](controller.md).

## Guide, tips and the pause screen

**Come si gioca** opens the guide, now three pages with tabs (`GameMenu.Help.cs`): **Obiettivo** (what the game asks), **Tastiera** and
**Controller** (a three-column list of keys and actions each, written from the bindings found in the code). From the settings, **Elenco dei
comandi** opens it on the page of the device in use. Under the quote *Voi guardate. Lui vi vede.* the main menu shows a **CONSIGLIO**: one of
13 sentences about the game, changed every 8 seconds with a half-second fade (still when the visual motion is at zero), in its controller
wording when a controller is the device in use (`Tips`, `TipFor` in `GameMenu.Design.cs`; every sentence restates something the game says
elsewhere: the guide, the scoreboard, the systems' comments). The pause screen has a line under *In pausa* with the wave, floor, points,
kills and playing time, the same figures as the scoreboard (`PauseStatsText`).

## Menu sounds

The menu makes four short tones (`MenuSound`): a tick when an entry is lit or chosen, a step when a choice changes value (the same tone,
higher towards the maximum, while a slider moves), a confirm and a back. They follow **Effetti sonori**, are switched off by **Suoni
dell'interfaccia**, and moving **Volume generale**, **Effetti sonori** or **Ambiente** plays a sample of what is being set (a bell, or a
creak for the ambience; at most one every 0.35 s). The tones are generated (`SoundMenu.cs`, peaks −4 to −7 dBFS, 80 to 700 ms). The main
menu also has a theme: [menu-music.md](menu-music.md). Neither plays in the automated runs (`GameSettings.QuietRun`: any `-*test` argument,
or `-nomusic`), so an agent testing the game does not make noise on the maintainer's computer; the requests are still counted, which is how
the checks know a slider or a button "speaks".

## Version label

Every menu panel (main menu, pause, settings, co-op, split screen, guide) shows **VERSIONE x.y.z** at the bottom right, on the line of the key
hints (`GameMenu.VersionText`, built in `BuildAtmosphere` in `GameMenu.Design.cs`). The number is `Application.version`, which Unity fills from
Player Settings > Version, that is `bundleVersion` in `unity/PryingEyes/ProjectSettings/ProjectSettings.asset`: the only place to change it.
A release bumps it in a commit before the build, so the number in the menu, the tag (`vX.Y.Z`) and the zip name agree
([../engineering/building-and-shipping.md](../engineering/building-and-shipping.md#releases)). The menu also writes `[PryingEyes] VERSIONE x.y.z`
to the player log when it is built, which tells which version a log comes from. The builds that `tools/ship.ps1` makes between two releases
carry the number of the last bump, not one of their own. The label was added on 16 November 2026, for release 0.6.2; before that the game
showed no version anywhere.

## Verification

- `PryingEyes.EditorTools.MenuReview.Run` renders the main menu, the five settings tabs, the three guide pages, the co-op and split-screen
  panels and the pause screen at 1280×720, 1920×1080, 3440×1440 and 1024×768; also checks pause/resume, leave confirmation, text bounds and
  real engraving meshes. It checks ultrawide presets, filtering and input validation; for each tab that every row exists, has a hint, fits
  its column and sits on an explicit navigation chain (dimmed rows skipped) from the tab to **Indietro**; that each row writes the value it
  names (steps, wraps at both ends, sliders reach their limits) and that **Ripristina scheda** restores the defaults of that tab; that the
  guide's keys fit their column and line up with their actions; the 13 tips (each, in both wordings, longer than 20 characters and within two
  lines of its label); the pause summary; that the menu tones are requested (and not with **Suoni dell'interfaccia** off); that the version is
  `x.y.z` and the label says it, and that on every panel the label stays at least 24 units to the right of the longer of the two key-hint lines
  (keyboard and controller, measured with the real font). Output: `unity_logs/menu-review`. This runs in an empty editor scene without
  changing player display preferences; the settings it changes are put back at the end.
- `PryingEyes.EditorTools.MenuMusicReview.Run`: the theme and the tones, in numbers ([menu-music.md](menu-music.md)).
- `python tools/check_display.py`: explicit Windows acceptance check against `Builds/Audit`. Exercises actual window
  resolution changes, timeout rollback while paused, explicit cancellation, confirmation/persistence and audio muting.
  The harness restores the exact preceding `display_*`, Screenmanager and monitor-selection registry values in `finally`.
  Other game instances must be closed. It does not run `-uitest` or `check_presentation.py`.
- Standard suite boundaries and hermetic Python tests remain separate from the desktop acceptance check.

Verified on 15 November 2026: all editor layout/mesh checks passed at the four sizes, the rebuilt player's display
acceptance check passed 9/9 with registry restoration confirmed, suite boundaries passed with zero findings, and all
73 hermetic Python tests passed. No presentation test that changes the player's settings was run.

On 16 November 2026, with the version label: `MenuReview.Run` passed 757 of 757 checks (665 before; the new ones are the label's own and 60 that
measure its gap from the key hints, never below 253 units at the four sizes) and the photos of the main menu (1280×720) and of the pause
screen (1024×768) were looked at. The label was not checked in a real player window.

On 17 November 2026, with the 0.8.0 rework: `MenuReview.Run` passed 2187 of 2187 checks and the photos it saves were looked at in the
editor, and `-padtest -nowaves` on the built player passed 83 of 83 ([controller.md](controller.md#verification)). `DisplayTest.cs` and
`PresentationTest.cs` were adapted to the new rows (`SplitScreenTest.cs` to the controller sensitivity) and compile; `tools/check_display.py`
and the presentation test were not run (they change the real screen mode and the game's registry values: they are for the maintainer to
ask for). **Not verified**: the look and the animation in a real player window, how the new
tabs feel with a physical controller, and the music and the tones by ear ([menu-music.md](menu-music.md)).

On the integrated tree of 0.8.0 (this rework together with the third person, the seals, the grimoires, the map and the split screen over
several monitors) the **Prospettiva** row of the third person ([third-person.md](third-person.md)) is the first row of the **Gioco** tab,
with its hint and its reset (`ResetGame` turns the third person off), and `MenuReview.Run` checks it with the rest of the tab: the tab now
has 2 sliders, 4 choices and 6 rows. Result: 2369 of 2369 checks; `MenuMusicReview.Run` 24 of 24.

On 17 November 2026, after the first report of 0.8.0 on another PC (the second monitor was not seen): the monitor count line, the always-present
**Monitor** button and the explanation behind it are checked by `MenuReview.Run` (it passed, with one, two and four monitors pretended
connected; the photos at 1280×720 and 1024×768 were looked at and the explanation fits its line at the four sizes), and `-seatstest` passed
82 of 82 with two players and 94 of 94 with four players on four pretend monitors, `-splittest` 79 of 79. **Not verified**: a monitor
switched on after the game started (the `late` case needs a PC where that can be done; it is read from `Screen.GetDisplayLayout`, which
was not exercised against a real change), and what Unity does on its own when the display list changes (`Display.onDisplaysUpdated` is only
logged).

On 18 November 2026, with the characters of 0.9: `MenuReview.Run` passed 2790 of 2790 checks. The new ones cover the list of the characters (also with a
suspended expedition), the creation and deletion panels, *Chi gioca?*, the suspended-expedition panel with the worst-case figures (1,234,567 points and
eleven resources of 9999) and the leave panel with its three buttons: at the four sizes every text fits its box and sits on the screen, the buttons that
must exist exist, and **Indietro** returns to the main menu. The photographs of the list, the expedition panel, the creation panel and *Chi gioca?*
(1280x720) and of the leave panel (1024x768) were looked at. **Not verified**: these panels with a controller, and their look in a real player window.

On 18 November 2026, with the mode panel of the desert (`GameMenu.Mode.cs`): `MenuReview.Run` passed 2874 of 2874 checks (84 more than with the characters): at the
four sizes every text of **Come vuoi giocare?** fits its box and the version label keeps its gap from the key hints, *Gioca* opens the panel, the panel
describes both ways, and **Indietro** returns to the main menu. The photograph (1280x720) was looked at: it showed the two descriptions touching the lower edge of the
buttons, so they were moved 14 units down and the review was run again (same result, and the photograph was looked at again). `-padtest -nowaves` 89 of 89
([controller.md](controller.md#verification)). **Not verified**: the panel with a physical controller, and its look in a real player window.

Source: `GameMenu.cs`, `GameMenu.Design.cs`, `GameMenu.Display.cs`, `GameMenu.Settings.cs`, `GameMenu.Help.cs`, `GameMenu.Split.cs`,
`GameMenu.Characters.cs`, `GameMenu.Mode.cs`, `GameMenu.Run.cs`, `MenuWidgets.cs`, `MenuSound.cs`, `MenuMusic.cs`, `MenuEngraving.cs`, `GameSettings.cs`, `GameSettings.More.cs`, `VideoSettings.cs`.
