# Track racing (Moto su pista)

The third way to play, chosen in the menu next to the standard game and the desert: a motorcycle race on a circuit that the game draws from a
seed, against computer riders or with up to three friends, on a physics model that behaves like a real bike (it leans, slides, wheelies, runs wide
and falls) and with a map of the circuit that shows every rider as a dot. The goal that started it was "the closest thing to Tourist Trophy":
a simulation to be learned, not the arcade riding of the desert's motorbike, which is left as it is ([the-desert.md](the-desert.md#the-motorbike)).

Written on 21 November 2026 on the branch `claude/moto-pista-mode-1435d9`. Nobody has ridden it by hand yet: every statement below comes from
the code, from the rules lab and from the headless runs listed under "How it is checked", and what no run covers says "(not verified)".

## How to play

`Gioca` asks how to play with three modes: the standard game, `Il deserto` and `Su pista`. `Su pista` opens a setup panel with a picture of the
circuit that will be raced; `Altra pista` draws another one. The rows are remembered between sessions.

| Row | Choices |
| --- | --- |
| `Giri` | 1, 2, 3 (default), 5, 8 |
| `Avversari` | 0 (a timed lap alone), 3, 7, 11 (default), 15 |
| `Livello` | `Principiante`, `Esperto` (default), `Campione`: how fast the computer riders go and how often they overdo a corner |
| `Moto` | Argo 1000 (superbike), Argo 600 (supersport), Vigile 750 (roadster) |
| `Aiuti alla guida` | `Completi` (ABS, traction control, anti-wheelie, anti-stoppie, automatic gears, lean assist), `Sportivi` (default: ABS, traction control, anti-wheelie, anti-stoppie, automatic gears; the lean is yours), `Nessuno` (only ABS; manual gears `E` / `Q`) |
| `Ora del giorno` | `Mattino`, `Mezzogiorno` (default), `Pomeriggio`: the sky of the desert, frozen at that hour |

`Via!` starts. From the hosting path (`Ospita una partita`) the same panel opens and `Via!` opens the door for friends instead (see "Co-op").
Split screen on one PC does not offer this mode: its button is disabled there.

A race is: the grid and a flight of the camera, five red lights one second apart, then dark after a random hold; laps counted by timing loops every
50 m; a fall that is cleared by itself (the rider is put back on the asphalt facing the race) or with `R`; the end when every person has finished,
or at the latest 45 s after the first rider crossed the line (whoever is still out there is ranked by the distance ridden), then a table of results.
`INVIO` / `A` races again on the same circuit, `N` / `X` on a new one, `ESC` / `START` goes back to the menu.

| Action | Keyboard | Controller |
| --- | --- | --- |
| Throttle | `W` / Up | right trigger |
| Front brake / rear brake | `S` / Down, `Space` | left trigger, `X` |
| Lean in and out of the corners | `A` `D` / Left Right | left stick |
| Shift the body | (automatic) | right stick |
| Tuck behind the screen | `Shift` | `A` |
| Gears (only with `Nessuno`) | `E` up, `Q` down | `RB`, `LB` |
| Camera (behind, far, helmet) | `C` | `Y` |
| Look back (held) | `V` | (none) |
| Back on the track after a fall | `R` | `B` |

The keys ramp the way a hand does (the throttle opens in a third of a second, the front brake builds up, the steering grows the longer the key is held
and is slower the faster the bike goes), so a tap is a small correction and not a fall. The screen shows the speed in km/h with the gear and a bar of
lights for the engine speed, the position, the lap and the times, the gap to the rider ahead and behind, the five lights, messages in the middle, and in a
corner the map of the circuit with a dot per rider.

## The bike

`BikeModel` is a rigid frame that leans, an engine with a gearbox, two wheels with slip, tyres that share their grip between braking, driving and cornering
(the friction ellipse), weight that moves between the wheels, air drag and the road underneath. It is deterministic: the same inputs give the same ride on every
machine. The laws, in the order the code applies them:

1. The rider asks for a path curvature; the bike leans to the angle that curvature needs at this speed, tan(lean) = v² k / g, rolling there with a limited roll rate;
   the rider's body shift can carry the centre of gravity beyond the frame's own lean.
2. The yaw rate follows the lean, r = g tan(lean) / v, the textbook rule of a motorcycle.
3. The tyres must supply m v r of side force. Each has mu N of grip, shared with its braking or driving force. Past the limit the front pushes wide (and tucks if the rider keeps on),
   the rear steps out (and highsides if the grip comes back).
4. Along the bike: the torque curve, gears, rev limiter and slipping clutch at launch, wheel inertia, brakes with optional ABS, traction control and anti-wheelie, drag, rolling resistance, slope.
5. Weight moves from wheel to wheel with the acceleration, m a h / L: a light front rotates the bike about the rear tyre (wheelie), and the other way round for a stoppie.
6. A crest unloads the tyres (u² times the vertical curvature) and a dip loads them; kerbs and gravel shake; barriers stop the bike and above a limit knock the rider off.

The physics runs at a fixed 60 steps a second and the bikes are drawn between the last two steps, so any refresh rate looks smooth. What the rules lab measured on the Argo 1000
on 21 November 2026: 190 hp at 13,000 rpm; 0-100 km/h in 3.29 s and 0-200 km/h in 8.35 s; 296 km/h at the top; 100 km/h to a stop in 39.8 m (1.35 g) and 200 km/h in 156 m; a steady
turn at 90 km/h on a 70 m radius needs 42.3° of lean (36.7° the frame, 5.8° the body); the 600 and the 750 are slower (264 and 216 km/h). The numbers are those of a real machine of the class; whether they
*feel* like Tourist Trophy is not verified.

## The circuit

`CircuitGen` makes a circuit from a seed (the same seed gives the same circuit on every machine, so a host only has to send a number): a main straight with the start line and room for the grid
behind it, then seven to eleven groups of corners (a single corner from a kink to a hairpin, a chicane, an esse, a double apex that tightens) joined by straights, scaled so that the lap turns exactly
360°, with hills (flat at the grid), camber, kerbs, paved run-off, gravel traps and a barrier. Over 40 seeds the lab sees laps of 2.45 to 4.15 km, closed to under a centimetre, at most 8 % grade, at least 1.2 m
of run-off everywhere and no two roads closer than 6 m; every one of the twenty grid places is on flat straight asphalt.

The ground under the wheels is not the meshes of the scenery: the physics reads `Circuit.SurfaceY` and the surface (asphalt, kerb, apron, gravel, grass), and the ribbon of the road is drawn from the same
function, so what the wheels touch is what the eye sees. Around it `PistaWorld` builds, a few pieces per frame behind the loading screen: the road, the guard rails, the countryside as a height field, belts
of trees, the pit building, a grandstand, the starting gantry with its five lights, braking boards and a ring of hills; the sky is the desert's, frozen at the chosen hour.

## The riders

A computer rider (`BikeDriver`) follows a racing line (`RacingLine`: the fastest line that stays on the asphalt, planned with the speeds the bike really delivers, measured by riding the model itself) by
pure pursuit, holds the planned speed with throttle and brakes, moves over to pass slower bikes and now and then overcooks a corner. It rides the same physics as the player. The lab sees six circuits of two laps
alone with no fall and a lap within 3 % of the plan, and a grid of twelve finishing in order.

Standings are by the same rules for everybody (`Race`): the finishers by their time, the others by the distance ridden, a rider who has left last. Gaps come from the timing loops.

## Co-op

Up to four people race together; the others are computer riders on the host's machine. Every machine rides its own bike with its own physics (nobody waits for the network to steer) and sends its pose twenty
times a second; the host relays everybody's pose to everybody, gives the go and says how the race ended.

1. The host picks `Su pista` from `Ospita una partita` and `Via!`. The `Welcome` tells a friend that the game is a race (and the seed); the friend builds the circuit and the scenery from the seed, says it is ready (`FloorReady`,
   as in the rest of the co-op) and receives the roster: laps, bike, aids and hour of the **host**, and every rider in the host's order with colours and a place on the grid. A friend's bike is a rider without a physics
   model here, drawn easing toward the pose that arrives; the computer riders of the host are the same on a friend's machine.
2. The host waits for every friend to have built the circuit and for `INVIO` / `A` (the status line at the bottom says who is still loading), then the lights start on every machine.
3. Whoever joins while the race is on the grid takes the next place at the back; whoever comes in after the start waits for the next race.
4. The end is the host's: the host's rules say when the race is over and send the results (places, times, falls, who retired), and every machine shows that order. A friend who leaves retires and the race does not wait for them;
   if the host goes, the race ends where it is ("Connessione persa") and a new one can be started alone.
5. After the results the host chooses another race on the same circuit or a new one; the friends build it again (a new epoch of the same game).

The map shows a dot for every rider; a friend has the colour of their bike and a white ring. The messages (protocol `pe-coop-35`) are `PistaPose` (a friend's pose, to the host), `PistaRoster`, `PistaStart`,
`PistaPoses` (everybody's poses, unreliable) and `PistaOver` (the results), all in `Scripts/Pista/PistaNet.cs` and `Scripts/Coop/CoopWire.cs`. A pose is about 35 bytes, so the nineteen riders of a full grid
(fifteen computer riders, the host and three friends) fit in one packet of 665 bytes; whatever arrives is checked by `NetReader` and a pose outside the world is thrown away.

## Where the code is

| What | Where (`unity/PryingEyes/Assets/_PryingEyes/Scripts/`) |
| --- | --- |
| The pure core, with no `UnityEngine` (compiled by the lab from the same paths) | `Pista/PistaMath.cs`, `BikeSpec.cs`, `BikeModel.cs`, `Circuit.cs`, `CircuitGen.cs`, `RacingLine.cs`, `BikeDriver.cs`, `RaceRules.cs` |
| The shell in the game | `Pista/PistaDirector*.cs` (the race, its setup, the co-op), `PistaWorld*.cs`, `PistaBikeMesh.cs`, `PistaBikeView.cs`, `PistaCamera.cs`, `PistaHud.cs`, `PistaMap.cs`, `PistaInput.cs`, `PistaSound.cs`, `PistaMode.cs`, `PistaNet.cs` |
| The way in | `GameMenu.Mode.cs` (the three modes), `GameMenu.Pista.cs` (the setup panel), `Proc/FloorDirector.Pista.cs` (the start from the game's director), `Coop/CoopGame.Pista.cs` (the host's side) |
| The checks | `tools/tracklab` (the rules lab), `Pista/PistaTest.cs`, `PistaMenuTest.cs`, `PistaCoopTest.cs` |

## How it is checked

Run on 21 November 2026 on this PC, headless (`-batchmode`, and `-nographics` in the co-op runs), no exception in any log.

- **The rules lab**, `python tools/studio.py test track` (also `dotnet run -c Release --project tools/tracklab`, and part of the CI gate): 59 of 59 checks in four groups: the bike (25: power, gearing,
  acceleration, braking, ABS, wheelies and stoppies, steady turns, the grip limit, crests, surfaces, determinism), the circuit (13: forty seeds, closure, grade, run-off, the twenty grid places), the race (11: the racing line, the
  riders, a full grid, the lights, falls, gaps, wrong way) and the co-op rules (10: a friend as a pose is ranked and awaited, the host's word ends the race and its order wins, a leaver does not block the end and is last, a newcomer
  takes the next place, a lost connection ends the race).
- **In the game, alone**: `-autotest -pistatest -pistabot -pistalaps 1 -pistarivals 5 -seed 777` (add `-nographics` to run it without a window): 35 of 35 in 60 s. It starts a race by the same path as the menu and checks the scene (the player on foot
  and the usual HUD are off, the race camera and the sky are up), the grid, the five lights, the start, a whole race ridden by the computer on the player's bike, the finish, the table of results, a new race on the same circuit and one on a new
  circuit, and the messages of the co-op (a pose, the roster and the results go through the wire unchanged, a short or hostile message is refused). With `-pistashots` (it needs a graphics device) it also photographs the grid, the start, the
  three views and the results into `hotel_build/prove/pista`.
- **From the menu**: `-pistamenutest` (without `-autotest`, which has no menu): 31 of 31 in 9 s: `Gioca`, the three modes, the setup panel with the circuit, the rows stepping, `Altra pista`, back and forward without losing the choices, `Via!` on
  that very circuit with the chosen laps, rivals and bike. It touches no preferences.
- **Two processes, over loopback**, `tools/coop/run_coop.ps1` (host `-coophost`, friend `-coopjoin`; the host's arguments `-pistacooptest -pista -pistabot -pistalaps 1 -pistarivals 3 -pistacoopwait 1 -seed 4242 -pistacoopseed 4242 -pistacooplaps 1`, the friend's
  `-pistacooptest -pistabot -pistarivals 3 -pistacoopseed 4242 -pistacooplaps 1`, plus `-pistacoopscenario` and the name of a scenario on both):
  - `race` (default): the friend has the host's circuit, laps and roster (itself, the host, three computer riders), the go comes from the host, everybody moves, and the race ends with the host's results, in the same order on both sides.
  - `again`: after the results the host starts another race, both build it, the friend takes its place on the grid, and the friend leaves while it is on: the host sees a retired rider, its bike gone from the scene and the race still running (friend 39 of 39, host 38 of 38, 128 s).
  - `hostquits`: the host leaves in the middle of the race: the friend's race ends where it is with the results on the screen, and it can start a race of its own that starts by itself (24 of 24 and 16 of 16, 54 s).
  Seed 4242 is the circuit *Autodromo di Montesole*, 2,732 m; the race of one lap takes 83 s.

## Not verified, and decisions left to the developer

- Not played by hand: the feel of the physics, of the cameras and of the controls on a keyboard and on a controller is judged only by numbers and by pictures. The controller mapping has never been tried with a physical controller.
- The sounds (engine, tyres, kerbs, wind, the start beeps) are made by code and were not listened to.
- The co-op was run only between two processes on one PC, never over a real network, and with the computer riding the bikes of both machines; three friends at once (a full table) is covered by the rules lab and the size of a packet, not by a run.
- The way into a co-op race through the menu (`Ospita una partita`, `Su pista`, `Via!`) is read from the code but no test walks it: the co-op runs choose the mode with `-pista`.
- Split screen on one PC does not offer this mode.
- The desert's motorbike was not changed: the new bike model, mesh and sound belong to this mode alone. Whether the desert should use them is a decision for the developer.
- The mode has no saves, no records and no points: a race leaves nothing behind.
