# Track racing (Moto su pista)

The third way to play, chosen in the menu next to the standard game and the desert: a motorcycle race on a circuit that the game draws from a
seed, against computer riders or with up to three friends, on a physics model that behaves like a real bike (it leans, slides, wheelies, runs wide
and falls), with a garage of nine bikes in six classes (a showroom where the chosen bike turns on a plate beside seven bars of figures measured on the physics, and a choice of colours), three riding styles, a body that shifts on the seat, a slipstream, the best lap kept as a ghost, twelve
named circuits, a map that shows every rider as a dot, a television replay of the race with a camera for the photographs, nine licence tests with bronze, silver and gold medals, and a championship of three cups (four rounds each, the same eleven rivals all season, a table of points and trophies). The goal that started it was "the
closest thing to Tourist Trophy": a simulation to be learned, not the arcade riding of the desert's motorbike, which is left as it is ([the-desert.md](the-desert.md#the-motorbike)).

Written on 21 November 2026 on the branch `claude/moto-pista-mode-1435d9`, in five rounds the same day (the mode, then everything that came after it with the
aim of being as close to Tourist Trophy as it can get, then the licence tests, then the garage, then the championship). Nobody has ridden it by hand yet: every statement below comes from the code, from the rules lab and from the
headless runs listed under "How it is checked", and what no run covers says "(not verified)".

## How to play

`Gioca` asks how to play with three modes: the standard game, `Il deserto` and `Su pista`. `Su pista` opens a setup panel with a picture of the
circuit that will be raced; `Altra pista` draws another one. The rows are remembered between sessions.

| Row | Choices |
| --- | --- |
| `Circuito` | `Casuale` (a circuit of one kind, from a seed: nothing about it is remembered) or one of the twelve named circuits (2.5 to 4.1 km, each with a line about its character: smooth, fast, technical, hilly) |
| `Giri` | 1, 2, 3 (default), 5, 8 |
| `Avversari` | 0 (a timed lap alone), 3, 7, 11 (default), 15 |
| `Livello` | `Principiante`, `Esperto` (default), `Campione`: how fast the computer riders go and how often they overdo a corner |
| `Moto` | the nine bikes of "The garage" (the garage screen has the same row) |
| `Stile di guida` | `Peso fuori`, `Neutro` (default), `Moto in piega` |
| `Aiuti alla guida` | `Completi` (ABS, traction control, anti-wheelie, anti-stoppie, automatic gears, lean assist), `Sportivi` (default: ABS, traction control, anti-wheelie, anti-stoppie, automatic gears; the lean is yours), `Nessuno` (only ABS; manual gears `E` / `Q`) |
| `Ora del giorno` | `Mattino`, `Mezzogiorno` (default), `Pomeriggio`: the sky of the desert, frozen at that hour |

`Campionato` at the top left opens the championship (see "The championship"), `Patenti` at the top right the licence tests (see below) and `Garage`, just under it, the garage screen (see "The garage"). `Via` starts (the button reads `Via`: the font of the menu draws an exclamation mark like an `l`). From the hosting path (`Ospita una partita`) the same panel opens, without `Campionato` and `Patenti` (both are for one player), and `Via` opens the door for friends instead (see "Co-op").
Split screen on one PC does not offer this mode: its button is disabled there.

A race is: the grid and a flight of the camera, five red lights one second apart, then dark after a random hold; laps counted by timing loops every
50 m; a fall that is cleared by itself (the rider is put back on the asphalt facing the race) or with `R`; the end when every person has finished,
or at the latest 45 s after the first rider crossed the line (whoever is still out there is ranked by the distance ridden), then a table of results.
On the results `INVIO` / `A` races again on the same circuit, `N` / `X` on a new one, `T` / `Y` watches the replay, `ESC` / `START` goes back to the menu.

| Action | Keyboard | Controller |
| --- | --- | --- |
| Throttle | `W` / Up | right trigger |
| Front brake / rear brake | `S` / Down, `Space` | left trigger, `X` |
| Lean in and out of the corners | `A` `D` / Left Right | left stick |
| Shift the body (forward and back, left and right) | `I` `K`, `J` `L` | right stick |
| Tuck behind the screen | `Shift` | `A` |
| Gears (only with `Nessuno`) | `E` up, `Q` down | `RB`, `LB` |
| Camera (behind, far, helmet) | `C` | `Y` |
| Look back (held) | `V` | (none) |
| The ghost, on and off | `G` | (none) |
| Back on the track after a fall | `R` | `B` |

The keys ramp the way a hand does (the throttle opens in a third of a second, the front brake builds up, the steering grows the longer the key is held
and is slower the faster the bike goes), so a tap is a small correction and not a fall. The screen shows the speed in km/h with the gear and a bar of
lights for the engine speed, `SCIA` when another bike is sheltering you from the wind, the position, the lap and the times (the best and the last), the gap to the rider ahead and behind, the gap to the ghost,
the five lights, messages in the middle (`CADUTO`, `CONTROMANO`, a new record), and in a corner the map of the circuit with a dot per rider.

## The garage

Nine machines, from a 125 two-stroke that weighs as much as its rider to a 1300 that tops 300 km/h. Their numbers are those of real bikes of the class (mass, power curve, gearing, tyres), not those of a game;
the lab measured them on 21 November 2026 on the same physics, on flat asphalt, alone:

| Bike | Class | Engine | Power | Mass (with rider) | Top speed | 0-100 km/h |
| --- | --- | --- | --- | --- | --- | --- |
| Argo 1000 | Superbike | inline four | 190 hp at 13,000 rpm | 275 kg | 296 km/h | 3.29 s |
| Argo 600 | Supersport | inline four | 129 hp at 13,500 rpm | 262 kg | 265 km/h | 3.77 s |
| Vigile 750 | Roadster | inline four | 112 hp at 10,500 rpm | 280 kg | 216 km/h | 4.05 s |
| Scintilla 125 | 125 GP | two-stroke single | 50 hp at 11,500 rpm | 133 kg | 207 km/h | 4.23 s |
| Tigre 675 | Supersport | triple | 126 hp at 11,500 rpm | 256 kg | 253 km/h | 3.48 s |
| Duca 1200 | Superbike | V twin | 187 hp at 9,500 rpm | 267 kg | 283 km/h | 3.22 s |
| Gigante 1300 | Hypersport | inline four | 195 hp at 9,800 rpm | 305 kg | 304 km/h | 3.17 s |
| Rombo 500 | GP 500 | two-stroke V4 | 190 hp at 12,000 rpm | 198 kg | 297 km/h | 3.17 s |
| Corsaro 1000 | Roadster | inline four | 161 hp at 11,000 rpm | 261 kg | 240 km/h | 3.43 s |

Every bike has its own livery and one of three body shapes, and a size (the 125 is drawn at 90 %, the 1300 at 106 %). **The rivals ride the class of the player's bike**, so a race is between machines that are a match for each other,
and the grid puts the quickest of the class in front. In the lab every bike rides two laps alone on two circuits without a fall.

**The garage screen.** `Garage` (under `Patenti` in the setup panel) opens a showroom: the chosen bike with its rider, turning on a plate at 24 degrees a second, its name and class, a sentence about it, and seven bars with their figures. The figures are not written by hand: `BikeSheet` rides every bike once on flat asphalt with the real model and reads the power (in hp), the torque (in Nm), the top speed, the time from 0 to 100 km/h, the distance from 100 km/h to a stop, the mass and a word for the handling (`Pesante`, `Stabile`, `Maneggevole`, `Agilissima`: from how fast the bike rolls into a corner and how much grip its tyres give). The bars have fixed scales, never relative to the other bikes, so that a bar means the same on every screen; the lab checks that each bar spreads over at least a quarter of its length across the nine bikes and that no two bikes draw nearly the same picture (the torque is a bar of its own because the 1000 four and the 1200 twin, with the same power and top speed, drew almost the same picture without it).
`Moto` steps the bike (it is the row of the setup) and `Colori` the colours: `Di serie` (the livery the bike comes in) or one of the twelve liveries of the game, worn by the bike on the plate and shown as two swatches. The race is ridden in the colours chosen (they are remembered with the other rows) and the computer riders never take them. `Fatto` (`Esc`, `B`) goes back to the setup with the `Garage` button still chosen. The showroom is a camera of its own with its own lights, drawn into a texture far below the scene, and it draws only while the screen is open.

**The engines have voices.** The five kinds of engine sound different, and none is a recording: `EngineSynth` (pure code, measured in the lab) builds each as a train of firing pulses that excite a few resonances of the exhaust, plus
a texture of growl, hiss and rumble, in a loop that closes on itself without a click; the game plays it faster or slower with the engine speed. The centres of the spectrum of the four made this way are 516 Hz (the twin: dark and fat), 846 (the triple), 1,787
(the two-stroke single) and 2,673 Hz (the two-stroke V4: a rasp); the inline four is the sound of the first version. Whether they *sound* like those engines is not verified, because nobody listened.

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

The physics runs at a fixed 60 steps a second and the bikes are drawn between the last two steps, so any refresh rate looks smooth. What the rules lab measured on the Argo 1000:
100 km/h to a stop in 39.8 m (1.35 g) and 200 km/h in 156 m; a steady turn at 90 km/h on a 70 m radius needs 42.3° of lean (36.7° the frame, 5.8° the body). The numbers are those of a real machine of the class;
whether they *feel* like Tourist Trophy is not verified.

## The rider

The body of the rider is part of the model, not a picture on top of it.

- **The riding style** (`Stile di guida`, the way the weight sits in a corner): `Peso fuori` hangs the body out and keeps the bike more upright (25° of lean reached in 0.242 s, 32.4° of frame in the 70 m corner at 90 km/h, a bit more grip at full lock),
  `Moto in piega` leans the bike under a body that stays on it (0.300 s, 42.3°, less grip at depth), `Neutro` is in between (0.267 s, 36.7°). The numbers are the lab's.
- **The body shift** (`I J K L`, the right stick): forward loads the front wheel (the lightest the front gets in a launch: 498 N forward, 190 N in the middle, 41 N back) and back loads the rear under braking (409 N forward, 520 N middle, 620 N back); sideways moves the centre of gravity by up to 6.8 cm
  of a 140 cm wheelbase. Without keys the rider does it by himself.
- **The slipstream**: a bike within about 60 m behind another, in its line and not too slow, is sheltered from the wind (0.90 at 5 m, 0.62 at 18 m, nothing at 60 m, nothing from the side, from a rider who has fallen or head on) and up to a third comes off its drag; the
  `SCIA` light shows it. In the lab a grid of ten for 140 s spends a third of its time sheltered (33 %), so packs form.

## The circuit

`CircuitGen` makes a circuit from a seed (the same seed gives the same circuit on every machine, so a host only has to send a number): a main straight with the start line and room for the grid
behind it, then seven to eleven groups of corners (a single corner from a kink to a hairpin, a chicane, an esse, a double apex that tightens) joined by straights, scaled so that the lap turns exactly
360°, with hills (flat at the grid), camber, kerbs, paved run-off, gravel traps and a barrier. Over 40 seeds the lab sees laps of 2.45 to 4.15 km, closed to under a centimetre, at most 8 % grade, at least 1.2 m
of run-off everywhere and no two roads closer than 6 m; every one of the twenty grid places is on flat straight asphalt.

**The named circuits** (`Catalogue`) are twelve seeds picked by scanning a range of them (`tools/tracklab --scan FROM TO`) for their character, from the 2,508 m short, fast and hilly one to the 4,139 m long one, so that a record, a ghost
and a time mean something: a circuit is the same every time. The lab checks that every one still has the length listed (a change to the generator that moves one must be a decision, because it would orphan the records)
and that the list covers fast, technical and hilly ones (5, 7 and 5). `Autodromo di Montesole`, seed 4242, 2,732 m, is the first, the one to begin with.

The ground under the wheels is not the meshes of the scenery: the physics reads `Circuit.SurfaceY` and the surface (asphalt, kerb, apron, gravel, grass), and the ribbon of the road is drawn from the same
function, so what the wheels touch is what the eye sees. Around it `PistaWorld` builds, a few pieces per frame behind the loading screen: the road, the guard rails, the countryside as a height field, belts
of trees, the pit building, a grandstand, the starting gantry with its five lights, braking boards and a ring of hills; the sky is the desert's, frozen at the chosen hour.

Two faults of that scenery were found only when the replay put a camera where the bikes are not (21 November 2026), and are fixed:

- **A third of the lap was under the grass.** The plain that carries the countryside to the horizon was one flat sheet at a height of its own, under the whole terrain, and on a circuit with hills (the road of seed 4242 runs from -7.4 m to +10.3 m) every stretch of road lower than the average
  height minus two metres, 549 of the 1,366 samples of that lap, was *under* the sheet: no asphalt, no kerbs, the wheels sunk in the lawn. The plain is now a frame around the terrain, level with its edge, and the in-game test asks that no point of the road (the middle and both edges)
  lies under a piece of it. From above the whole ribbon is now there (`giro_0_dall_alto.png` of the photographs).
- **A wall of green streaks along the barriers.** The skirt that closes the crack between the ribbon and the ground had a plan-view texture smeared down a vertical face; it has texture coordinates of its own, along the wall and down it.

## The riders

A computer rider (`BikeDriver`) follows a racing line (`RacingLine`: the fastest line that stays on the asphalt, planned with the speeds the bike really delivers, measured by riding the model itself) by
pure pursuit, holds the planned speed with throttle and brakes, moves over to pass slower bikes and now and then overcooks a corner. It rides the same physics as the player. The lab sees six circuits of two laps
alone with no fall and a lap within 3 % of the plan, and a grid of twelve finishing in order.

Standings are by the same rules for everybody (`Race`): the finishers by their time, the others by the distance ridden, a rider who has left last. Gaps come from the timing loops.

## Timing, the ghost and the records

Every lap of the player is timed against itself: a trace (the clock against the distance, every ten metres) and a recording of the bike (a frame every 1/20 s). A lap that is over and clean (no fall, no cut) and is the best of this bike on this circuit becomes the
**ghost**, kept apart for the lap that starts from the grid and for a flying lap; in a race the ghost rides translucent on the circuit and the screen shows the gap to it (`G` turns it off and on). On a named circuit the ghost is also written to a file of its own under the game's data
folder (`pista/fantasma-<seed>-<bike>-<kind>.bin`), so it is still there tomorrow; a circuit of one kind remembers nothing. A damaged, truncated or foreign file is ignored (the lab feeds 213 truncated copies of a real one and none is accepted), and **the automated runs never read or write the
player's files**. A lap that ends in a fall is not kept.

## The licence tests

`Patenti`, the button at the top right of the setup panel, opens nine tests in three licences, named like the categories of a real motorcycle licence: **A1** (the small bikes: a 125, all the aids, at noon), **A2** (the middle ones, 600 to 750,
the sporty aids, in the morning) and **A** (the big ones, 1000 to 1300, the sporty aids, in the afternoon). Each test fixes the bike, the circuit (a named one), the aids, the hour of the day, three laps and nobody else on the track, and
they are not locked: any can be taken at any time. A click or `INVIO` starts it, and what the setup panel had chosen is put aside until the game is over. It is for one player: the button is not there when hosting a co-op game.

The lap that counts is the fastest **flying lap with no fall in it**. The first lap starts from the grid and never counts, and a lap with a fall is left out; with three laps there are two chances, so one mistake can be made up for. The three times to beat stay on the screen under the record line, each in the
colour of its medal; the banner at the end of a lap says the medal it earned (or why it did not count), and the table of results says `PROVA FINITA` with the medal in big letters, the three times, how many seconds would earn the next medal, the best time of the book and, when the third test of a licence has just got a medal, the licence (`PATENTE A1 OTTENUTA`).
On the results `INVIO` / `A` is the same test again (there is no new circuit to ask for). The best time of every test is kept in the preferences of the player, as one line of text, and the medal is worked out from it, so it follows the targets; the automated runs keep their book in memory only.
A licence is obtained when its three tests have a medal.

| Licence | Test | Bike | Circuit | Aids, hour | Gold | Silver | Bronze |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A1 | `a1-1` Il primo giro | Scintilla 125 | Autodromo di Montesole (2.7 km) | Completi, Mezzogiorno | 1:25.5 | 1:31.0 | 1:40.5 |
| A1 | `a1-2` Tornanti | Scintilla 125 | Anello dei Cipressi (2.9 km) | Completi, Mezzogiorno | 1:48.0 | 1:55.0 | 2:07.5 |
| A1 | `a1-3` Saliscendi | Scintilla 125 | Pista di Valle Alta (2.8 km) | Completi, Mezzogiorno | 1:35.5 | 1:41.5 | 1:52.5 |
| A2 | `a2-1` Il rettilineo | Argo 600 | Circuito dei Tre Ponti (2.9 km) | Sportivi, Mattino | 1:30.0 | 1:35.5 | 1:45.5 |
| A2 | `a2-2` In salita | Tigre 675 | Circuito del Vento (2.9 km) | Sportivi, Mattino | 1:45.5 | 1:52.5 | 2:04.5 |
| A2 | `a2-3` Corta e veloce | Vigile 750 | Autodromo del Faro (2.5 km) | Sportivi, Mattino | 1:14.5 | 1:19.0 | 1:27.5 |
| A | `a-1` Oltre 850 metri | Argo 1000 | Autodromo della Rocca (3.2 km) | Sportivi, Pomeriggio | 1:37.5 | 1:44.0 | 1:54.5 |
| A | `a-2` Quindici tornanti | Duca 1200 | Pista della Gola (3.1 km) | Sportivi, Pomeriggio | 1:54.5 | 2:01.5 | 2:14.5 |
| A | `a-3` La pista di punta | Gigante 1300 | Pista di San Gaudenzio (4.1 km) | Sportivi, Pomeriggio | 2:09.5 | 2:18.0 | 2:32.5 |

Gold, silver and bronze ask for 1.08, 1.15 and 1.27 times the lap that the racing line of the computer riders plans for that bike on that circuit, rounded to the half second. The lab checks that a computer rider at full pace earns gold on every test, one at 0.97 of it silver or better and
a beginner (0.90) bronze and never gold, without a fall (full pace is 1.02 to 1.05 times the plan, 0.97 is 1.05 to 1.08, 0.90 is 1.13 to 1.16), so a medal is always within reach of a rider who knows the circuit, and that a change of the physics or of the generator that
moves the plan is noticed. Whether a *person* can reach them, and how hard the gold is, is not verified: nobody has ridden them by hand.

## The championship

`Campionato`, the button at the top left of the setup panel (across from `Patenti` and `Garage`), opens three cups, one for each class of bike that has a field to race against: **Coppa 125** (the 125 GP class: the Scintilla 125), **Coppa Supersport** (Argo 600, Tigre 675) and **Coppa Superbike** (Argo 1000, Duca 1200).
The other three classes (Roadster, Hypersport, GP 500) have no cup and stay for single races. A cup is **four rounds** on four of the named circuits, three laps each, against **the same eleven computer riders all season**: it is for one player, and the button is not there when hosting a co-op game.

| Cup | Class | Rounds (the circuit and its length) |
| --- | --- | --- |
| `c125` Coppa 125 | 125 GP | Autodromo di Montesole 2.7 km, Pista di Valle Alta 2.8 km, Autodromo del Faro 2.5 km, Circuito del Vento 2.9 km |
| `csup` Coppa Supersport | Supersport | Circuito dei Tre Ponti 2.9 km, Anello dei Cipressi 2.9 km, Circuito della Torre 3.6 km, Pista del Vento 3.5 km |
| `csbk` Coppa Superbike | Superbike | Autodromo della Rocca 3.2 km, Pista della Gola 3.1 km, Anello del Pozzo 4.1 km, Pista di San Gaudenzio 4.1 km |

**The field.** The names, bikes and colours of the eleven come from the cup alone (a generator of our own, so that they are the same on every machine) and no rival wears the colours of the player's bike. Their pace is the level of the setup panel (`Livello`) *when the season began* plus a fixed share of a spread, so that a rival who is quick in the first round is quick in the last
and the standings mean something: for `Principiante` 0.885 of the speed that the racing line plans, a spread of 0.06 and a `mistakes` setting (how often a rider overdoes a corner) of 0.28; for `Esperto` 0.935, 0.045 and 0.12; for `Campione` 0.968, 0.027 and 0.04. Changing `Livello` in the middle of a season changes nothing until a new one begins.

**The points** are the usual ones of a motorcycle championship, 25, 20, 16, 13, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 and 1 for the first fifteen places, and only for whoever crosses the line (twelve riders share 134 points a round, 536 a season). The standings are by points, then wins, then second places, then third places, then the order of the field with the player first.

**The panel.** Every cup is a button with the state of its season (`Stagione da cominciare`, `Gara 3 di 4 - 2° con 41 punti`, `Finita - 1° con 100 punti`) and the best trophy ever won in it. Under the cup chosen: its calendar (the rounds done, and the next one), the standings (before the first round, the eleven riders with their bikes and the player with his),
the bike (`In sella`: the one of the setup panel if it is of the class, else the first bike of the class) and the level of the rivals (fixed since the season began), and what `INVIO` does. A click or `INVIO` on a cup races the next round of its season, or the first round of a new season if there is none or the last one is over; the aids and the hour are the player's, the circuit, the laps and the rivals are the cup's.
`Ricomincia` appears when a season is in progress and, pressed a second time (the first only asks), throws it away and begins another at the level of the setup panel now. The panel opens on the cup in progress, else on the first cup never finished.

**The race.** Twelve riders: the player and the eleven; at the start the banner names the cup in the big line and the round, the circuit, the laps and the bike under it. The first round is gridded by pace (the quickest rival of the class on pole, the player in the middle), the others by the standings (the leader on pole). An ordinary race is over as soon as every *person* has crossed the line, and the computer riders still out are ranked by the distance they have ridden;
a cup's race **waits for the whole field** (`Race.waitForField`), because the points go to everyone who crosses the line: it is over when the last rider is in, or 45 s after the first, and the screen says `Gli altri stanno ancora arrivando` while the player waits (the first run of the in-game test found it: with the ordinary rules two riders in twelve crossed the line and nobody behind the player scored). When the table of results comes up the round counts for the season **once** (the replay and the way back to the table do not count it again) and four lines under the table say
what the round gave: the cup and the round, the place, the points and the place in the standings, the first three of the standings, and the next circuit (`INVIO` / `A` goes there: in a cup there is no "again" and no "new circuit") or, after the last round, `STAGIONE FINITA` with the place and the trophy: gold for the first place of the season, silver for the second, bronze for the third.
The best place of a finished season is kept for good, a new season does not take it away. The book is one line of text per cup in the preferences of the player, written after every round (a round that is not finished is not counted: leaving in the middle of one loses that round, not the season); the automated runs keep it in memory only.
`-pistacuplaps N` in an automated run shortens the rounds to `N` laps.

## Replay and photographs

When a race is over the results offer `T` (`Y`): the race is played back from a tape recorded while it was ridden (20 poses a second for every rider and the distance along the race as the rules saw it, at most 18,000 frames: fifteen minutes), and every bike stands where the tape says, at every frame, whatever the camera.
`ReplayEdit` cuts it like a television director, from the same tape and the same seed to the same edit, in shots of 2.6 to 9 s: it opens on the grid, cuts to every fall (the bike is shown lying on the asphalt, the others riding past), to chosen overtakes and battles, to the player's laps, and ends with the winner and the
player crossing the line. The cameras:

| Camera | What it is |
| --- | --- |
| `inseguimento`, `lontana`, `casco` | the three views of the race camera, on any rider (the helmet is the rider's own eye) |
| `panoramica` | a long lens beside the track that follows the rider past, from a metre or more beyond the barrier and high enough to see over it, the rider kept at a good size |
| `a bordo pista` | a low camera on the verge, behind the nearest barrier and close to the line (never more than 30 m from the rider at the pass), that looks up the road at the place the rider will come from |
| `elicottero` | a helicopter that trails the rider, circling slowly |
| `partenza`, `traguardo` | the grid seen from past the line, and the line seen from past it |

| Action | Keyboard | Controller |
| --- | --- | --- |
| Pause | `Space` | `A` |
| Jump 5 s back or forward (30 s with `Shift`) | `A` `D` / Left Right | D-pad |
| Wind back and forward | | triggers |
| Speed: 0.25, 0.5, 1, 2, 4 | `W` `S` / Up Down | `RB`, `LB` |
| Start, end | `Home`, `End` | |
| The television, then each camera by hand on the rider on screen | `C` | `X` |
| The next (previous) rider, in the order of the race at this moment | `Tab` (`Shift+Tab`) | `Y` |
| Hide the interface | `H` | left stick |
| A photograph, without the interface | `P` | right stick |
| Back to the results | `T`, `Enter`, `Backspace` | `B` |

The photograph is the camera of the moment drawn again into a picture of its own, twice the width of the window (at most 4K), so that it does not depend on the window, and written as a PNG in the game's photo folder (an automated run writes it in the cache, never in the player's folder).
It was blank in the first version, which read the screen: the headless window has none.

## Co-op

Up to four people race together; the others are computer riders on the host's machine. Every machine rides its own bike with its own physics (nobody waits for the network to steer) and sends its pose twenty
times a second; the host relays everybody's pose to everybody, gives the go and says how the race ended.

1. The host picks `Su pista` from `Ospita una partita` and `Via`. The `Welcome` tells a friend that the game is a race (and the seed); the friend builds the circuit and the scenery from the seed, says it is ready (`FloorReady`,
   as in the rest of the co-op) and receives the roster: laps, bike, aids and hour of the **host**, and every rider in the host's order with colours and a place on the grid. A friend's bike is a rider without a physics
   model here, drawn easing toward the pose that arrives; the computer riders of the host are the same on a friend's machine.
2. The host waits for every friend to have built the circuit and for `INVIO` / `A` (the status line at the bottom says who is still loading), then the lights start on every machine.
3. Whoever joins while the race is on the grid takes the next place at the back; whoever comes in after the start waits for the next race.
4. The end is the host's: the host's rules say when the race is over and send the results (places, times, falls, who retired), and every machine shows that order. A friend who leaves retires and the race does not wait for them;
   if the host goes, the race ends where it is ("Connessione persa") and a new one can be started alone.
5. After the results the host chooses another race on the same circuit or a new one; the friends build it again (a new epoch of the same game).

The map shows a dot for every rider; a friend has the colour of their bike and a white ring. The messages (protocol `pe-coop-35`) are `PistaPose` (a friend's pose, to the host), `PistaRoster`, `PistaStart`,
`PistaPoses` (everybody's poses, unreliable) and `PistaOver` (the results), all in `Scripts/Pista/PistaNet.cs` and `Scripts/Coop/CoopWire.cs`. A pose is about 35 bytes, so the nineteen riders of a full grid
(fifteen computer riders, the host and three friends) fit in one packet of 665 bytes; whatever arrives is checked by `NetReader` and a pose outside the world is thrown away. The replay and the ghost belong to each machine: they are not sent.

## Where the code is

| What | Where (`unity/PryingEyes/Assets/_PryingEyes/Scripts/`) |
| --- | --- |
| The pure core, with no `UnityEngine` (compiled by the lab from the same paths) | `Pista/PistaMath.cs`, `BikeSpec.cs`, `BikeModel.cs`, `Circuit.cs`, `CircuitGen.cs`, `Catalogue.cs`, `RacingLine.cs`, `BikeDriver.cs`, `RaceRules.cs`, `PoseTape.cs` (the tapes and the ghost files), `EngineSynth.cs` (the voices), `RaceReplay.cs` (the recording, the edit and the cameras), `Licences.cs` (the nine tests, the medals, the book of times), `BikeSheet.cs` (the figures and the bars of the garage screen), `Championship.cs` (the three cups, the eleven rivals, the points, the season and its book) |
| The shell in the game | `Pista/PistaDirector*.cs` (the race, its setup, the co-op, the timing, the replay), `PistaWorld*.cs`, `PistaBikeMesh.cs`, `PistaBikeView.cs`, `PistaCamera.cs`, `PistaHud.cs`, `PistaReplayUi.cs`, `PistaMap.cs`, `PistaInput.cs`, `PistaSound.cs`, `PistaRecords.cs`, `PistaLicences.cs` (the book in the preferences), `PistaCups.cs` (the championship's book in the preferences), `GarageStage.cs` (the showroom), `PistaMode.cs`, `PistaNet.cs`; the licence tests in the race are `PistaDirector.Licence.cs`, the championship in the race `PistaDirector.Cup.cs` |
| The way in | `GameMenu.Mode.cs` (the three modes), `GameMenu.Pista.cs` (the setup panel), `GameMenu.Licence.cs` (the licence panel), `GameMenu.Garage.cs` (the garage screen), `GameMenu.Cup.cs` (the championship panel), `Proc/FloorDirector.Pista.cs` (the start from the game's director), `Coop/CoopGame.Pista.cs` (the host's side) |
| The checks | `tools/tracklab` (the rules lab: `Program.cs` and one file of checks per group), `Pista/PistaTest.cs`, `PistaMenuTest.cs`, `PistaCoopTest.cs` |

## How it is checked

Run on 21 November 2026 on this PC, headless (`-batchmode`, and `-nographics` in the co-op runs), no exception in any log.

- **The rules lab**, `python tools/studio.py test track` (also `dotnet run -c Release --project tools/tracklab`, and part of the CI gate): 144 of 144 checks in twelve groups (`--only` takes the name of one): `bike` (25: power, gearing,
  acceleration, braking, ABS, wheelies and stoppies, steady turns, the grip limit, crests, surfaces, determinism), `circuit` (13: forty seeds, closure, grade, run-off, the twenty grid places), `race` (11: the racing line, the
  riders, a full grid, the lights, falls, gaps, wrong way), `coop` (10: a friend as a pose is ranked and awaited, the host's word ends the race and its order wins, a leaver does not block the end and is last, a newcomer
  takes the next place, a lost connection ends the race), `rider` (10: the three riding styles, the body shift and the slipstream), `timing` (12: the trace and the delta, the tapes, the ghost files and the twelve circuits), `roster` (14: the nine
  bikes, their classes, their numbers, their brakes, a clean lap each), `sound` (8: the voices are finite, loud, balanced, rich in harmonics, close without a click and different), `replay` (16: the recording, the falls found, the
  edit that covers the race without a gap, starts on the grid and ends at the line, cuts to every fall, never shows the same camera on the same rider twice in a row, is the same twice and different for another seed, and the cameras: beside the road and not on it, the rider at a good size, the low camera near and seeing the rider come, no NaN)
  `licence` (10: nine well-formed tests with the targets in order and 1.08 / 1.15 / 1.27 times the planned lap, a computer rider earning gold, silver and bronze without a fall, the edges of a medal, only a clean flying lap counting, the book keeping the best, coming back from the preferences on a machine with decimal commas and surviving damaged text) , `garage` (7: the figures of the garage screen: every bike's power, torque, mass, top speed, 0-100 and braking are those of a bike of its kind, the same every time and the same as the remembered ones, the seven bars stay in range and spread over the nine bikes, no two bikes draw the same picture, the order a person expects (the 125 is the lightest, the slowest and the most agile, the 1300 the hardest to turn and among the fastest) and a sentence for every bike) and `cup` (8: the three calendars well formed, the points following the table (134 a race, 536 a season), the field the same all season and at every level with nobody in the colours of the player, a season that adds up (a hand-made one: the points, the order of the table, the tie-breaks), the book keeping the season and the trophies (a new season keeps them, a finished one takes no more rounds), the book round-tripping through text on a machine with decimal commas and surviving damaged text, the computer riding two rounds of a cup on the real circuits (a rider at 0.97 of the pace wins both, one at 0.86 is last), and a cup's race that waits for the whole field: with a person in it the ordinary rules end it when the person crosses the line, with one rider in, and waiting brings in all twelve).
- **In the game, alone**: `-autotest -pistatest -pistabot -pistalaps 1 -pistarivals 5 -seed 777` (add `-nographics` to run it without a window): 89 of 89 in about 225 s. It starts a race by the same path as the menu and checks the scene (the player on foot
  and the usual HUD are off, the race camera and the sky are up, the road is not under the plain), the grid, the five lights, the start, a whole race ridden by the computer on the player's bike, the slipstream, the finish, the table of results, the ghost
  of the lap just ridden in the race after, a new race on the same circuit and one on a new circuit, the replay (recorded, started, edited, cameras by hand, rider by rider, pause, jump, speed, the end, back to the results with every bike free again), the five engines and the nine bikes,
  the messages of the co-op (a pose, the roster and the results go through the wire unchanged, a short or hostile message is refused), and last a licence test by the real path (its circuit, bike, three laps and nobody else, the three times on the screen, the medal of the best clean flying lap, the book, the verdict on the table, the same test again), then a championship season by the real path (round one on the first circuit of the calendar with twelve riders, the eleven of the cup and no one in the colours of the player, gridded by pace; the computer rides it; the points go in the book once, the race waits for the field (nearly everyone crosses the line); the replay and the way back do not count it twice, four lines under the table; the next round on the second circuit with the same eleven, gridded by the standings; two rounds written by hand; the last round closes the season with its trophy; a finished season is not taken up again; and leaving the mode clears the cup and the circuit chosen). With `-pistashots` (it needs a graphics device, 90 of 90) it also photographs the grid, the start, the
  three views, the results, the nine bikes in the paddock, every camera of the replay and a lap of the television in eight places, with the circuit from above, a licence test under way and its verdict, and the championship (the grid with the banner of the cup, the table of a round with the lines of the cup under it, the table that closes the season with the trophy), into `hotel_build/prove/pista`, and saves one replay photograph in the cache. `-pistacuponly` goes straight to the championship (19 checks, about 90 s), to look at the table of a round and iterate on it.
- **From the menu**: `-pistamenutest` (without `-autotest`, which has no menu): 40 of 40 in 6 s: `Gioca`, the three modes, the setup panel with the circuit, the rows stepping (the nine bikes, the styles, the named circuits), `Altra pista`, the note on the aids and the style read whole (four lines, clear of `Altra pista`), the start button reading `Via`, back and forward without losing the choices, `Via` on
  that very circuit with the chosen laps, rivals and bike. It touches no preferences. With `-pistamenulicence` the same way in goes to the licence panel (29 of 29 in 6 s): the `Patenti` button and the cross that reaches it, the nine tests with their bikes, the line under the one chosen, the cross, the medals of a book made on purpose (a licence obtained), back with the button still chosen, and a click that starts a test with the rules of the test over the choices of the setup. With `-pistamenugarage` the same way in goes to the garage screen (28 of 28 in 6 s): the `Garage` button under `Patenti` and the cross that reaches it, the showroom on (a camera and a texture of its own, the bike on the plate), the name, the sentence and the seven figures equal to those of `BikeSheet`, the bars as long as the figures say, the picture of the showroom not an empty frame, `Moto` stepping to another bike (the plate and the figures follow), `Colori` from `Di serie` through the liveries and round, the swatches, the cross, `Fatto` back to the setup with `Garage` still chosen and the showroom off, and a race that starts on the bike and in the colours chosen with no computer rider wearing them. With `-pistamenucup` the same way in goes to the championship panel (39 of 39 in 7 s): the `Campionato` button at the top of the cross, the three cups with their classes and no season, the line, the calendar, the field and the note under the cup chosen, the cross (right, down to `Indietro` or to `Ricomincia`), another cup chosen, a book made on purpose (a season in progress, a finished one with the gold cup), the standings and the marks of the calendar, the summary of the trophies, the two-step `Ricomincia` (the first press only asks, another cup chosen puts the question away, the second throws the season away and begins one at the level of the setup), back with the button still chosen, the cup in progress chosen when the panel is opened again, and a click on the finished cup that begins a new season (the trophy stays) and the first round with the laps of the cup, twelve riders and the bike and the aids of the player.
- **Two processes, over loopback**, `tools/coop/run_coop.ps1` (host `-coophost`, friend `-coopjoin`; the host's arguments `-pistacooptest -pista -pistabot -pistalaps 1 -pistarivals 3 -pistacoopwait 1 -seed 4242 -pistacoopseed 4242 -pistacooplaps 1`, the friend's
  `-pistacooptest -pistabot -pistarivals 3 -pistacoopseed 4242 -pistacooplaps 1`, plus `-pistacoopscenario` and the name of a scenario on both):
  - `race` (default): the friend has the host's circuit, laps and roster (itself, the host, three computer riders), the go comes from the host, everybody moves, and the race ends with the host's results, in the same order on both sides.
  - `again`: after the results the host starts another race, both build it, the friend takes its place on the grid, and the friend leaves while it is on: the host sees a retired rider, its bike gone from the scene and the race still running.
  - `hostquits`: the host leaves in the middle of the race: the friend's race ends where it is with the results on the screen, and it can start a race of its own that starts by itself.
  Seed 4242 is the circuit *Autodromo di Montesole*, 2,732 m; the race of one lap takes 83 s.

## Not verified, and decisions left to the developer

- Not played by hand: the feel of the physics, of the cameras and of the controls on a keyboard and on a controller is judged only by numbers and by pictures. The controller mapping (the race and the replay) has never been tried with a physical controller.
- The sounds (the nine engines, tyres, kerbs, wind, the start beeps) are made by code, measured in the lab and in the game, and never listened to.
- The replay was judged from its photographs and the checks of the cameras, not watched in motion: how the cuts, the speed of a pan or the whip of the low camera *feel* is not verified.
- The co-op was run only between two processes on one PC, never over a real network, and with the computer riding the bikes of both machines; three friends at once (a full table) is covered by the rules lab and the size of a packet, not by a run.
- The way into a co-op race through the menu (`Ospita una partita`, `Su pista`, `Via`) is read from the code but no test walks it: the co-op runs choose the mode with `-pista`.
- Split screen on one PC does not offer this mode.
- The desert's motorbike was not changed: the new bike model, mesh and sound belong to this mode alone. Whether the desert should use them is a decision for the developer.
- The medal times of the licence tests are tied to the racing line of the computer riders and checked against computer riders only; whether a person can reach silver and gold, and whether three laps are the right number, is not known.
- The garage screen was judged from photographs of the showroom of two bikes (the 1000 and the 125) and from a test that presses its buttons and reads its texts; nobody has used it with a controller or watched the plate turn. The scales of the bars (a full power bar is 210 hp, a full torque bar 170 Nm, a full speed bar 320 km/h and an empty one 150, a full mass bar 120 kg and an empty one 330) are a choice made so that the nine bikes spread along them, not a measurement, and the weights that make the handling word (70 % the rate of roll, 30 % the grip) are a judgment that no rider has tested.
- What the mode keeps: the ghosts of the best laps on the named circuits, in files of their own, the book of licence times, one line of text in the preferences, and the rows of the setup (the bike and the colours of the garage among them). The championship adds the book of its seasons and trophies to that. It has no saves of characters and no unlocks, and nothing is locked behind a licence or a cup; a career with money or a tuning shop (Tourist Trophy has them) is not made: the garage lets the player choose a bike and its colours, it sells and changes nothing, and the cups give a table of points and a trophy, nothing else.
- The championship was never played by a person: the three levels of the rivals were set against the computer riders of the lab (a rider at 0.97 of the pace wins a round, one at 0.86 is last), not against a human, so whether `Principiante` is gentle enough, whether a trophy is within reach at `Campione` and whether four rounds of three laps are the right length is not known. The points are those of a real championship and the calendars, the three classes with a cup and the three without are a choice. The rivals of a season are fixed by the cup and the level when it began; they do not learn and do not get better round by round. A race in progress is not saved. The panel was judged from photographs and from a test that presses its buttons, with no controller.
