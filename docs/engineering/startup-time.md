# Startup time

How long the player takes to show its menu, what used to hold it up, how the delay is kept off the main thread and how to read it from a
`Player.log`. Written on 17 November 2026 after the first report of 0.8.0 on another PC (the window did not appear for a long while, then
showed a frozen window). Checked against `StartupTrace.cs`, `BackgroundWork.cs`, `SoundPrefetch.cs` and `PrefetchTest.cs`.

## What the player does before the menu

Times are seconds since the engine started, from the `[PryingEyes] avvio:` lines of the log, on the development PC (a fast desktop).

| stage | 0.8.0 | now |
|---|---|---|
| first game code (`primo codice del gioco`; it includes Unity's splash screen) | 2.05 | 2.03 |
| scene loaded (`GameManager.Awake`) | 2.51 | 2.43 |
| menu built | 2.56 | 2.47 |
| player started (`giocatore pronto`) | 3.12 | 3.01 |
| sounds computed on the main thread | 2.9 s (3.12 to 6.05) | none |
| **first frame drawn** | **6.10** | **3.05** |
| background jobs started (third frame) | - | 3.30 |
| ambience loops playing (`suoni: ambiente avviato`) | 6.05 | 3.79 |
| menu theme ready (`musica del menu pronta`) | not logged | 7.6 |

With the process limited to two logical processors (a way to imitate a weaker PC, `ProcessorAffinity` from a script, also lowering the
process priority) the first frame came at **9.2 s** in 0.8.0 and Windows marked the window **not responding** from 5 s to 11 s; now it comes
at **3.1 s** and the window answers all the time (ambience at 6.1 s). Two intermediate versions showed what matters: with the sounds only
moved to background threads the first frame came at 4.5 s on two processors, because the threads built the menu slower than the main
thread alone would (1.1 s instead of 0.03 s: they were all running while the menu was being built); the startup gate below removed that. On a
PC several times slower than the development one the same shape stretches: in 0.8.0 the window stayed black and then ghosted for as long as it
took to compute the sounds.

## What was wrong

`ProcAmbience.Start` called `SoundBank.Warm()` and `SoundBank.WarmThemes()`, which synthesise about two hundred sounds (guns, mechanisms,
impacts, theme sounds, the ambience loops) one after the other **in the first frame**, before Unity could draw anything. The four layers of
the wave music, the menu theme, the litanies, the waltz and the map textures were `Task.Run` jobs on thread-pool threads at normal priority,
so on a PC with few processors they shared the CPU equally with the main thread.

## How it works now

- **`SoundBank.PrefetchAll()`** (called at the top of `GameManager.Awake`) queues the computation of every sound the game uses in its first
  minutes: the six ambience loops first, then the `Warm*` lists (guns, mechanisms, casings, impacts, theme sounds, stealth, knife, perks,
  relics). One to three worker threads (`processorCount / 2`) at below-normal priority take the queue in order.
- The `SoundBank` getters (`One`, `Pick`, `Gun`, `Mech`, `Casing`, `Organ`, `Impact`) do two things. While a batch is being queued
  (`prefetching`) they queue the sound by its clip name and return null, so the old `Warm*` methods work unchanged as the queue. Otherwise
  they build the clip from the samples a worker already computed (microseconds), or, if the computation has not started, compute it right
  there as before, so nothing waits for a worker that has not begun. A sound that is half computed is waited for.
- **`ProcAmbience`** no longer creates its six looping sources in `Start`: `Update` creates them when `SoundBank.AmbienceReady` says their
  samples are there (about half a second after the first frame on the development PC).
- **`BackgroundWork.Run`** replaces `Task.Run` for the long jobs (music layers, menu theme, litanies, waltz, map and magic textures): a
  dedicated thread at below-normal priority, started from the thread pool (Mono's `Thread.Start` waits for the new thread to run, which on a full
  CPU would stall the main thread). The sample generators use only `System.Random` with a fixed seed and plain maths, so the result is the same
  whichever thread runs them.
- **The startup gate.** Jobs flagged `afterFirstFrame` (the prefetch workers, music, menu theme, litanies, waltz) do not start until the third
  frame of the scene (`StartupTrace` opens the gate), so the main thread builds the menu and starts the player without sharing the CPU with
  them. Jobs the main thread may have to wait for (`MapAssets`, `VfxTextures`) are not gated: they would wait for a gate only the main thread
  can open. Outside the running game (editor tools) the gate is never armed.

## Reading a `Player.log`

The log is `%USERPROFILE%\AppData\LocalLow\DefaultCompany\PryingEyes\Player.log` (`-logFile <path>` puts it elsewhere). Each stage writes a line
with the engine time and the time since the previous stage:

```
[PryingEyes] avvio: primo codice del gioco (processo avviato 2.79 s fa) a 2.03 s (+2.03)
[PryingEyes] avvio: scena caricata (GameManager.Awake) a 2.43 s (+0.40)
[PryingEyes] avvio: menu: costruito a 2.47 s (+0.04)
[PryingEyes] avvio: giocatore pronto a 3.01 s (+0.00)
[PryingEyes] avvio: primo fotogramma disegnato a 3.05 s (+0.04)
[PryingEyes] avvio: Update 1 ... Update 2 ... Update 3: lavori di sottofondo avviati (suoni e musica) a 3.30 s (+0.25)
[PryingEyes] avvio: suoni: ambiente avviato a 3.79 s (+0.49)
[PryingEyes] avvio: musica del menu pronta a 7.59 s (+3.80)
```

The gap between `Update 1` and `Update 2` is the cost of the first frame, which includes the first requests to the graphics card (on a PC
whose driver has no shader cache yet it can be long: it happens inside Unity and is only visible there).

- *processo avviato X s fa* minus the engine time is what happened before Unity started counting (loading the player DLL, the antivirus
  scanning the executable and the data files, the graphics driver). About 1.7 s on the development PC.
- The first stage up to `primo codice del gioco` is the engine start plus Unity's splash screen (`m_ShowUnitySplashScreen`).
- `scena caricata` minus the first stage is the loading of the first scene's data (about 0.4 s here, from disk).
- Everything between `scena caricata` and `primo fotogramma disegnato` is the game's own work on the main thread: that is the part this page
  keeps short. Fixed costs left in it are the player's `Start` (0.5 s here: equipment, magic, the first HUD) and the window mode change
  (`finestra: WxH ... applicata`, the first launch on a PC applies native borderless).
- `monitor all'avvio: Unity ne vede N (...), Windows M` says how many monitors Unity opened its displays on and how many Windows has;
  see [../game/display-and-menu.md](../game/display-and-menu.md#split-screen-entry).

## What is not known

The slow start was reported on a PC this was never run on. The numbers above come from the development PC, with two processors as the only
imitation of a weaker one; the cold disk, an antivirus scanning 590 MB of data, a laptop with two graphics chips or a first run that fills the
graphics driver's shader cache all happen before or around the stages above and are visible only in a log from that PC. The new log lines are
there for that: ask for the `Player.log` and compare the stages.

## Checks

`PrefetchTest` (`PryingEyes.exe -batchmode -autotest -nofire -noenemies -prefetchtest -seed 1000 -floor 1`) checks that the sounds are queued
at the start of the scene, that frames keep coming while they are computed, that every sample computed by a worker is **identical bit by bit**
to the one the main thread recomputes (that is the check that no generator depends on the thread), that the six ambience sources exist with
their clips, that the clips of the guns, mechanisms and steps are created from the ready samples in a few milliseconds, and that no worker
failed. Everything else that listens (`-musictest`, `-impacttest`, `-seatstest`) goes through the same getters.
