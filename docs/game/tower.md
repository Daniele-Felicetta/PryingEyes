# The Tower: procedural floors

This page describes the Tower, the procedural mode of Prying Eyes: a descent through floors generated at every run. It started as the English version of the section "La Torre: piani procedurali" of the former Italian README (`git show b77eca5:README.md`) and was checked against the code on 2026-11-15. Where the code differs from that original text, the code wins; the changes are listed in section 15. Numbers and names were checked against the code. Items marked "(not verified)" and the list in section 16 were not checked. Other descriptive text (room contents, ambience, models, file roles, stealth details) was translated from the original and not re-checked line by line.

Scene: `unity/PryingEyes/Assets/_PryingEyes/Scenes/Torre.unity`. Namespace of the floor code: `PryingEyes.Proc`.

Related pages, for what this one does not repeat:

- [weapons.md](weapons.md): the 28 weapons, the sights and the gunplay.
- [horde-variants.md](horde-variants.md): the plague bearer, flagellant, statue and cardinal; blood.
- [equipment-and-trials.md](equipment-and-trials.md): holy water, the music box, the trials, the glass eyes, the reliquary mine.
- [door-traps.md](door-traps.md), [barrels-and-rising.md](barrels-and-rising.md), [chandeliers.md](chandeliers.md), [power.md](power.md): the traps in the environment and the floor power.
- [cathedrals-and-annexes.md](cathedrals-and-annexes.md): the cathedral halls and annexes of the wave layout.
- [feel-and-reflections.md](feel-and-reflections.md): reflections, ragdolls, vaulting, hit reactions, light shafts, empty magazines, medals, music and surface impacts.
- [display-and-menu.md](display-and-menu.md): display settings and the menu. [../multiplayer/README.md](../multiplayer/README.md): online co-op.
- [../engineering/testing.md](../engineering/testing.md): how each of these is verified. [../history/zombies-redesign.md](../history/zombies-redesign.md): how the wave cycle came about.

**Since 17 November 2026 the floors are the levels of the catacombs** under the player's cathedral: the lifts are iron gates on stone
stairs, each branch of three levels has one theme and a boss on its third level, enemies drop resources, and alone you start and come
back to the cathedral. That layer is described in [cathedral-and-catacombs.md](cathedral-and-catacombs.md); this page still describes
each level. The automated tests keep the Tower as it was unless they pass `-catacombe` or `-cattedrale`, and `-torre` brings the Tower
back when playing.

## 1. Status: two cycles

| Cycle | Status | Selected by |
|---|---|---|
| **Wave cycle** | **Default** since 13 November 2026 (`Proc/SurvivalMode.cs`). Waves start on their own, areas are bought with points, there are no candles and no stealth. | Default. |
| **Candle cycle** | **Legacy.** Kept for the tests that verify it. Candles are lit, enemies are alerted by stealth, rounds are sealed by shutters. | Command line flag `-candles` or `-legacycycle`. It is also implied by `-oldplan`, `-stealthtest`, `-roundtest`, `-roundshots`, `-knifetest`, `-droptest` and `-architecturesupply`. |

Notes on the flags (`Proc/SurvivalMode.cs`):

- `-zombies` forces the wave cycle, even together with the legacy flags. It is checked first.
- There is no game setting for the cycle. The choice is read from the command line only, once per process.
- `-nowaves` and `-noenemies` hold the waves still, but the new floor, doors and points stay as in the wave cycle. `-cooptest` also holds them at start (`SurvivalMode.HoldWaves` is true) until the test script releases them with `Hold(false)`.
- The classic hall (`Hotel.unity`, `HotelBuilder`) stays in the project and is said to be out of the build (not verified).

Sections 2 to 4 describe the floor. Sections 5 and 6 describe the two cycles. Sections 7 onward apply to both cycles unless stated.

## 2. Floors, wings and themes

**History of the layout.** The first version built floors from wings: a hotel wing and a church wing, always both, joined by a portal and oriented differently. A floor is made of several such wings, joined by doors: three wings on floors 1 and 2, four on floors 3 and 4, five from floor 5 on (`PlanGen.UnitsFor`). Each wing has about 25 rooms (not verified). That is about 75 rooms on the first floors and about 125 at the bottom, roughly 6,000 m² of rooms on average (not verified). The wing layout is still available with `-oldplan`. The current layouts are the room grid (section 3, default of the candle cycle) and the wave layout (section 4, default of the wave cycle). The classic hall stays in the project but is not in the build (not verified).

**Themes.** Every floor has one theme, the same for all its wings:

- **Hotel with church** (`Albergo`): always on floors 1 and 2 (`PlanThemes.PickTheme`).
- **Theatre** (`Teatro`): an auditorium with boxes and a foyer, a stage with dressing rooms, joined by the proscenium arch.
- **Sanatorium** (`Sanatorio`): an institute with reception, wards, cells and services, plus the chapel.
- **Guardaroba** (`Guardaroba`): the Count's castle of wardrobes, vampires, scissors and lava, with open-air terraces; see [guardaroba.md](guardaroba.md). Off by default: `-guardaroba N` makes N floors in a hundred, from the third up, Guardaroba (`PlanGen.GuardarobaPercent`, default 0), and `-theme guardaroba` forces it.

From floor 3 the theme is drawn with equal weight among the first three, and the Guardaroba replaces it on the floors the second draw picks (none by default).

Theatre and sanatorium have their own plans, materials, lights, fog, sounds, furniture and models (phases 1 to 3 of the themes). The models are:

- Theatre: rows of tip-up seats, a dressing table with a bulb mirror, a tailor's dummy and a tripod spotlight (`modelli/hotel/genera_arredi3.py`).
- Sanatorium: an iron bed, a trolley, a drip stand, a wheelchair, a surgical lamp and a folding screen (`modelli/hotel/genera_arredi3.py`).

The rest of the furniture (benches, tables, shelves, box seats) is still the hotel's. Themed enemies are phase 4 (see section 12).

## 3. Room grid (default of the candle cycle, phase 4)

The floor is not a hundred small rooms but 13 to 17 large rooms on a grid, as in The Binding of Isaac. Each room is 1x1, 2x1, 1x2 or 2x2 cells (one cell is between 8.4 and 10.8 m on a side, not verified), has one to four doors, and totals about 1,550 m² with 76 m on foot from arrival to exit (not verified).

Each room has a role (`RoomType`). The colour of its door frames tells the role before you enter:

| Role | Door frame | Contents |
|---|---|---|
| **Partenza** (start) | Green | The arrival lift and a modest weapon on the wall. |
| **Guardia** (guard) | Steel | One or two guards who patrol only their room until they notice you. On the first floor the rooms attached to the start are empty. |
| **Candela** (arena) | Fire | The candle, a few stone columns, benches as cover where there is room, and the round with its shutters. From 71 to 310 m² (not verified), with one to four doors to close. |
| **Negozio** (shop) | Gold | Two weapons on the wall and the Scrigno. |
| **Biblioteca** (library) | Violet | The three perk lecterns. |
| **Segreta** (secret) | Turquoise | Entered only through a low passage, crouching with C or Ctrl. Enemies do not reach it. Inside is the Consacrazione altar. Present on 97% of floors (not verified). Candle cycle only. |
| **Boss** | Crimson | The exit lift and its guardian: an abbot, or the Direttore or the Primario in the theme. Twice the hit points on floors 1 and 2, two and a half times from floor 3 on. |

**Candles** (candle cycle): two up to floor 3, three from floor 4, four from floor 7 (`PlanGrid.cs`).

**Plan size.** The grid has 13 main rooms on floors 1 to 3, one more every three floors, up to 17. It has one fewer in simple mode (`PlanGrid.BuildGrid`).

The two halves of a floor (the arrival zone and the boss zone) are the two "wings" of the theme, each with its own furniture palette: church and hotel, or auditorium and stage for the theatre.

**Map.** `M` opens the map and `N` shows the minimap. Rooms are drawn as nodes: visited rooms in the colour of their role, with their name. Rooms beyond an open door are drawn as `?`, doors as notches. The secret room appears only after you have entered it (`FloorDirector.GridMapItems`).

**Generator.** `PlanGrid.cs` (integer geometry, like `PlanGen`). Speed claims from the original text, 2,000 plans in half a second, none failed, eight in ten on the first try, are not verified. To get statistics (roles, doors, sizes, arenas, shutters): `cd tools/planlab; dotnet run -c Release -- grid 2000` (the `grid` argument is not verified).

### Themed floor details

**Theatre.** The auditorium has red carpet with medallions, rows of tip-up seats facing the stage (three variants: some with seats up or without cushions, some rows missing), crown chandeliers, and a green exit sign above the foyer door. The boxes have two velvet armchairs. The foyer has chequered marble, a box office and a lounge. The bar is in the ridotto. The stage has the velvet curtain gathered at the sides of the arch, a footlight strip, black wings, a service lamp lit at the centre under a spotlight, the rehearsal piano, two tripod spotlights, and the Occhio d'oro on the backdrop with its own spotlight (visible from the auditorium through the arch). Behind it: the wing corridor with bare bulbs, dressing rooms with bulb-mirror dressing tables and a tailor's dummy, and a store with more dummies. Background sound: a distant orchestra tuning, the murmur of a full hall, an occasional applause or house bell. The fog is reddish.

**Sanatorium.** Linoleum, green paint and cold plaster. Reception with its hatch and waiting chairs, the wards and corridors with iron beds in rows along the walls, drip stands and some canvas screens, cells with a cot, a refectory with long tables, a tiled operating room with a steel table under the surgical lamp, a laundry with tubs, an archive with double rows of shelves, a morgue and a doctors' room. In the corridors, against the walls, sometimes a trolley, a drip stand or a shelf. The wheelchair waits at reception: in the middle of a corridor it used to block the navmesh. The light is from fluorescent tubes that flicker (a third are dead), and the fog is greenish. Background sound: the hum of the tubes, an occasional squeaking trolley, a distant monitor, or an iron gate slamming.

## 4. Wave layout (default of the wave cycle)

With the wave cycle the floor uses the same grid generator with `PlanGen.SurvivalLayout` set (`FloorDirector.cs`). It is a different layout:

- **7 to 9 main areas**, about 925 m² on average each, often with a three-level atrium (average not verified). The code gives 7 on floors 1 to 4, 8 on floors 5 to 8, 9 from floor 9 on, one fewer in simple mode (`PlanGrid.BuildGrid`).
- **No Segreta**: the secret room is not generated.
- Two candle arenas are still placed, but as decoration (see below).
- The arrival lift and the exit lift (the exit one in the boss area) are closed by a steel shutter, except the start.

Each area is opened with `F` in front of its door, paying points:

- The first area costs 500. Each further area costs 250 more per step of distance from the start beyond the first, up to 1,500 (`RoomAccessState.Price`).
- The purchase applies to everyone and for the rest of the floor. Two doors between the same areas open together. Lifts open with their area (`RoomAccessState`, `RoomAccessDoors`).
- In co-op, whoever stands in front of the door buys, and the guest validates the purchase (starting area connected, price, within 4.5 m). From far away it is refused and the points set aside come back (the 4.5 m rule is not verified).

Candles still exist as furniture (light and flame) but they are not lit and do not count as objectives (`SurvivalMode.DecorationOnly`).

The Biblioteca does not offer Velo or Passi di cenere in this cycle, because they only matter for stealth (`Biblioteca.Offer`).

## 5. Wave cycle (default)

The procedural mode works like Call of Duty: Zombies. There are no candles to light and no stealth: no alert, no detection tied to light, no Fantasma bonus, no takedown. The knife `G` is just a close-range hit.

**Waves** (`RoundDirector.Survival.cs`):

- Waves start on their own. The first one is 8 s after arrival, the others 10 s after the previous one ends. The number is shown in red at the bottom left.
- Enemies per wave: 16 plus 4 per wave, plus 8 for each player beyond the first, clamped between 20 and 100.
- Enemies alive at once: 12 plus 1 per wave, plus 3 per extra player, clamped between 14 and 28.
- Enemies spawn only in areas already open, at random weighted by floor area. They spawn 7 to 28 m from the players, out of sight while good spots exist, and reachable on foot within a few seconds.
- They are always aggressive. Speed multiplier: 1.6 at wave 1, rising linearly to 2.2 at wave 10 and staying there; flying enemies ×1.15 (`SurvivalMode.SpeedMultiplier`).
- **Reward** when a wave is cleared: 100 points plus 20 per wave, for everyone. Shield refilled to 50 and a quarter of the ammo are also restored.
- Every fifth wave is the **Processione dei Lumi** (section 9).
- Every eighth wave opens with the **Cardinale** (not in the original text; code: `RoundDirector.BeginSurvivalWave`).
- Three waves cleared open the exit lift, in the boss area. You can keep fighting and buying after that.
- If the player dies (in co-op, if everyone dies) the wave is cancelled with no reward, and the wave number goes back by one. Going down
  is not dying: while someone is down but alive the wave goes on (`Targets.AnyLiving`).
- If enemies stall for 120 s with none spawning or dying, the wave times out with no reward.

**Enemy variants in the wave cycle** (not in the original text; code: `RoundDirector.PickSurvivalType`):

- Flagellante from wave 3: 8% plus 1.2% per wave, up to 20%.
- Appestato from wave 4: 5% plus 1% per wave, up to 14%.
- Statua from wave 6: 5% chance, at most one alive at a time.
- Otherwise the floor's usual types (section 12 for themed ones).

**Shops and perks.** `F` opens areas, buys perks at the Biblioteca (section 8), and opens the Scrigno and the altar (section 8). Points come from kills, waves and descents.

## 6. Candle cycle (legacy)

Still available with `-candles` (or `-legacycycle`, `-oldplan`, and the test flags listed in section 1). It is described here as it was.

**How to play.** You arrive from a lift, sometimes in a hotel atrium and sometimes in a sacristy. Each floor has two to four **candelabra to light** (`F`), one per arena, marked by a cold halo (with `-oldplan`: 4 up to the second floor, then 6, scattered through the rooms). Lighting them calls enemies. When they are all lit, the exit lift opens and you can go down, with a bonus. Weapons are bought with points at the three wall displays on each floor. If you die you respawn at the arrival lift on the same floor.

**Stealth.** Lights dim as you go down. Monks, abbots and eyes patrol, investigate noises, and search the last point where they saw you. Eyes above the head show suspicion, search and hunt. In the dark and crouched you are harder to notice. The torch increases the distance from which you are seen. Close up it dims itself (below 3.4 m the light falls as the surface gets closer), so the altar, the Scrigno and the wall writing do not become white blocks. The heartbeat and flicker warn when a threat is near.

**Rounds.** Many candles stand in a zone that can be sealed: one to three connected rooms, between 28 and 330 m² (not verified), with at most four openings to close. About 72% of candles have one (not verified). Lighting the candle of a zone drops steel shutters over all its exits and starts a Black Ops Zombies-style round. Inside, the round's enemies spawn: always aggressive, more numerous, tougher and faster each round. The number is unique for the whole run and shown in red at the bottom left.

- If no priest knew about you, the round is **calm**: fewer enemies, starts after 6 seconds, 150 bonus points.
- If someone was already searching for you, it is on **alert**: more enemies, immediately.
- When all are down, the shutters rise: reward of 100 points plus 20 per round, shield full, a quarter of the ammo. The candle counts only from then on, and the lift opens with the last one.
- If you die, the round is cancelled, the candle goes out again, and the round number does not count.
- Enemies outside hear the noise and press against the shutters, but do not come in until they rise.

## 7. Points

You start with **500** points.

| Event | Points |
|---|---|
| A hit that does not kill | 10 |
| A kill | 60 |
| A lethal headshot | 100 (the lethal hit pays 60 to the body or 100 to the head, not 60 plus the 10 of the hit) |
| A knife kill | 130 |
| Takedown with the knife (candle cycle) | 130 |
| A lit candle (candle cycle) | 60 |
| A round (candle cycle) | 100 plus 20 per round |
| Wave cleared (wave cycle) | 100 plus 20 per wave |
| Campana (power-up) | 400, with no points for the kills it causes |
| Ghost bonus (candle cycle) | 200, for a candle lit with no priest having seen you since the last one |
| Descending a floor | 100 plus 50 times the floor number, and 40% of the ammo (`FloorDirector.Descend`; not in the original text) |

**Knife** (`G`, or KNIFE on touch). MISERICORDIA is always in your pocket and does not replace your weapon. A slash or a stab (120 damage, reach 1.75 m) makes a sound: whoever hears it looks for you, and whoever survives knows where you are and hunts you. Behind a monk or an abbot who is walking and has not seen you, the prompt PRESA ALLE SPALLE (takedown) appears. The strike is silent, kills outright (even the abbot), pays 130 points, and is not a sighting. Takedown reach is 2.35 m (`Knife.cs`). The original text says that within one metre the enemy still notices you from behind; this is not verified. Enemies spawned in a round are never taken down from behind, because they always know where you are. The takedown exists only in the candle cycle (`Stealth.Enabled`). In the wave cycle the knife is only a close-range hit.

## 8. Perks, shops, shrines

**Perks** (phase 3). The perks are those of Black Ops Zombies, but kept in **magic books**, one per perk, in the **Biblioteca** of every floor: three stone lecterns in a row, each with a book floating and turning, with a seal lit in its perk's colour. You buy one (`F`) and the other two close again.

There are four slots and four prices, depending on how many perks you already have: **2,000, 2,500, 3,000 and 4,000** points. With all slots full the Biblioteca offers nothing. Besides the Biblioteca, every floor hides one secret book room with a 19-node grimoire (spells for the left hand, or seals for the weapons): see [arms-and-magic.md](arms-and-magic.md). The eight perks:

| Perk | Effect |
|---|---|
| **Pelle di pietra** (stone skin) | +50% health. |
| **Mani svelte** (quick hands) | Reload 40% faster. |
| **Doppio colpo** (double tap) | Fire rate +33%. |
| **Rinascita** (rebirth) | One rebirth per floor, on the spot, with three seconds of respite. |
| **Passi di cenere** (ash steps) | Footsteps heard 40% less. |
| **Velo** (veil) | Seen 30% less. `H` makes you invisible for 5 s, then a rest (40 s cooldown; not in the original text). |
| **Occhio della Torre** (eye of the tower) | The ping shows enemies through walls; headshots ×1.25. |
| **Sete di sangue** (blood thirst) | Knife kills heal 25 points, takedowns 40. |

**Death** costs half of your points (rounded down to tens) and the last perk you bought (`FloorDirector.DeathPenalty`, `PlayerPerks.LoseLast`).

**Downed and revive** (0.7.2). At zero health, unless the Rinascita book lifts you, you do not die at once: you go down. Down, you crawl
at 0.85 m/s with the view low and tilted, you can still fire the weapon in hand, and you cannot run, jump, vault, use the knife, cast,
throw, buy or use things; enemies ignore you (`ITarget.Alive` is false) and look for someone else. A teammate standing within 1.8 m
revives you by holding Use (`F`, controller X held) for 3 s; once per floor you can get up by yourself by holding Use for 5 s. You get up
with half your health and 2.5 s of grace. If nobody can revive you (alone with the floor's self-revive spent, or every teammate down or
dead) you die at once; otherwise after 30 s you bleed out and die as described above. Going down to the next floor puts a downed player
back on their feet. The HUD line says how long is left and what to hold; teammates see a red card in their HUD, a red name with the
seconds over your body, and a message when you fall. Code: `PlayerController.Downed.cs`; the network part is `Coop/CoopGame.Revive.cs`.
Checked by `-seatstest` (downed, revived by a teammate, self-revive, bleed-out death and respawn) and by the `-cooptest` steps R4 and R5.

**Light** (0.7.2). The torch beam is 34% brighter (intensity 118, was 88), reaches 42 m (was 36) with a 78 degree cone (was 72), and the
soft spill around the player is brighter and wider (2.6 over 9 m, was 1.5 over 7 m); teammates' torches use the same numbers. The moonlight
through ordinary windows is more than twice as strong and wider (14 over 12 m with a 50 degree inner cone, was 6 over 8.5 m and 20 degrees),
the stained glass of the churches 25% stronger, and the ambient light of every theme 25% higher (`FloorDirector.Look.cs`).

Velo is not offered in the wave cycle, so `H` is effectively candle-cycle only.

**Scrigno** (the mystery box). For **950** points the clasp clicks, the barrel lid rises to its stop, and the treasure lights up. A column of light rises from the chest, and weapons of all five grades rise out of it, more and more slowly. It stops on one at random (the higher grades weigh more the deeper you are, never the starting pistol, never ones you already have). You have nine seconds to take it with `F`. Otherwise the weapon sinks back, the lid drops, and the points are lost. With full pockets it replaces the weapon in your hands. The opening timing follows the sound (`SoundGen.ChestOpen`): the click at 0 s, the hinge creak from 0.18 s, the thud at 1.32 s, two glass notes at 1.45 s. The five grades are as stated in the source (not verified).

**Altare della Consacrazione** (the Pack-a-Punch). For **3,000** points it consecrates the weapon in your hands, once per weapon: damage ×1.7, magazine and reserve ×1.5, golden metal and a golden name in the HUD (`Consacrazione.cs`). The altar is in the Segreta on candle-cycle grids. In the wave cycle the Segreta does not exist, so the altar is placed elsewhere.

**Placement.** The Biblioteca, the Scrigno and the altar are built before the navmesh (they are obstacles). Each is placed at a spot that can be reached on foot (`ShrineKit`, `FloorDirector.ValidateShrines`). In the candle-cycle grid they go in their roles' rooms (Biblioteca, Negozio, Segreta), otherwise in a fallback room.

## 9. Power-ups and presences

**Power-ups** (`Drops.cs`, `DropPickup.cs`). Four kinds, like the power-ups of Black Ops Zombies:

- **Reliquiario** (relic): ammo of both weapons to the maximum.
- **Sguardo** (gaze): for 30 s every shot kills.
- **Offerta** (offering): for 30 s points are doubled.
- **Campana** (bell): kills everyone in the round and everyone within 22 m, +400 points. But the toll is heard across the whole floor (alarm, 55 m), and the survivors come looking for you. In the candle cycle it is a double-edged weapon.

They drop from kills with a 4.5% chance (double with the knife, four times with a takedown). At most four per floor, drawn from a shuffled bag: all four, then again. They stay on the ground for 30 s and are picked up by passing close. Each has a column of light in its colour (amber, red, gold, blue) and a pool on the floor, so it is visible from far away. The original text says the last eight seconds blink (not verified).

**Processione dei Lumi** (every fifth round or wave). Only **Veglie** spawn, 60% more than normal, up to four more alive at the same time. At the end a Reliquiario always drops where the last one died (`Drops.lastKillPos`). In the wave cycle the procession is the same: Veglie only.

**Presences of the Tower** (names as in the code, `EnemyType`):

- **Veglia**: a candle hung in a torn votive veil. Fragile and fast, it shows its weak point as two embers in its wax face.
- **Portachiavi**: the reception key cabinet turned into a small walking reliquary: tarnished brass, four articulated keys and a bell that warns before the blow. The glowing lock is its weak point.
- **Medusa**: a slower flying presence with cyan tentacles.

## 10. Themed enemies

In the theatre and the sanatorium, the monks and abbots (outside the chapel) are replaced by four enemies built on the same skeleton and clips as the monk. They change costume, accessories, numbers and have one surprise each (`EnemyTemi.cs`, `FloorDirector.Retheme`):

| Enemy | Where | Hit points, speed | Behaviour |
|---|---|---|---|
| **Maschera** (mask) | Theatre, replaces the monk. The usher with a lantern: a faded harlequin with a porcelain mask that leaves only one eye uncovered. | 150, fast. Melee only. | At the first sight it blows a whistle, once every 14 s. Those not already hunting run on the spot (`NoiseKind.Alarm`). |
| **Direttore** (conductor) | Theatre, replaces the abbot. 2.4 m tall with a top hat, a chalky tailcoat and a tuning fork. | 330, slow. | Telegraphs its charge for 1.2 s, then throws a fan of three darts; five when hurt. |
| **Infermiere** (nurse) | Sanatorium, replaces the monk. A stained gown with a red cross and a drip stand whose bag glows green. | 190. | Its needle puts your legs to sleep: sedative, 55% speed for 2.8 s. |
| **Primario** (chief physician) | Sanatorium, replaces the abbot. Large as an abbot: a bloodied gown, a skullcap, the surgical lamp as a halo, a frontal mirror and a retractor. | 360. | When it sees you it calls two nurses, once every 45 s. They arrive out of sight. |

Each has its own ambience: a music box, a theatre tone, a monitor, clinical hum. The accessories are rigid, attached to the monk's `testa`, `bastone` and `petto` bones. The costumes are the monk's cloth repainted by `genera_costumi_tex.py`, and `Editor/NemiciTemiImport.cs` makes the prefabs in `Resources/`.

Boss: the guardian in the boss room is the heavy version of the theme (`FloorDirector`, with `BossHpMul`).

**Note on other enemies.** `Spazzino` and `Vigile` exist in `EnemyType`, but they belong to the city mode (`CityWaves.cs`). They are not Tower enemies.

## 11. Lighting, fog and the Tower's sound

Base light is higher, and the fog softens distance, with cooler tones in the church wing. The **Luminosità** (brightness) slider is in the settings of the start menu and the pause menu. It adjusts exposure immediately and saves the value for later runs.

Stained-glass windows with the Occhio are real-time coloured lights that cast the shadows of the pews. Only nearby lights are on, and only the four nearest cast shadows (`LightPool`; not verified).

The wings and rooms have their own ambience (`ProcAmbience.cs`): organ and a long reverb in the church, a hum and a short echo in the hotel, distant tolls, a gramophone waltz.

## 12. Controls

Game controls (translated from the original text, checked against the code where noted):

| Key | Action |
|---|---|
| `M` | Open or close the map of the explored floor. |
| `N` | Show or hide the minimap. |
| `T` | Turn the torch on or off. A battery lasts about 3.5 minutes of light (`Stealth/Torch.cs`, 210 s). |
| `Y` | Replace the battery when its charge is 80% or less (80% threshold not verified). At zero the torch uses a spare automatically, if you have one. |
| `F` | Pick up a battery from the floor, buy the door or weapon you are looking at, or (candle cycle) light the candelabrum. |
| `G` | Knife: a slash (130 points if it kills). In the candle cycle, also the takedown when the prompt appears. |
| `H` | Veil (candle cycle, if you have the perk): invisible for 5 seconds. |
| `V` | Ping. For a few seconds it shows pulsing signals on doors still to buy (candle cycle: on unlit candles) and on the lift. Nearby enemies hear it. |
| `3` / middle mouse | Left hand: tap to cast the selected spell, hold `3` to trace the selected seal on the weapon (needs the grimoires: [arms-and-magic.md](arms-and-magic.md)). |
| `6` / `7` | Next spell / next seal. |

The map reveals rooms you have visited and marks weapons, candelabra and the exit you have seen up close. Some walls hide low passages: hold C or Ctrl to crawl through. Enemies deal 50% extra damage (`PlayerController.cs`; the condition is not verified). The HUD shows charge, spares and the wait before the next ping. The arrow at the top points to the exit lift and shows when it is open (not verified).

### Controller

An XInput controller plays the normal game, the online co-op and the split screen, and drives every menu: left stick to move (pressed: run),
right stick to look (pressed: knife), RT to fire, LT to aim (with the aim, L3 and R3 lean), A to jump, B to crouch (on and off), X to reload
and, held, to use and buy, Y to change weapon (held: fire mode), LB for the left hand's spell (held: the seal), RB for the holy water, and the
D-pad for torch, music box, the next seal and the next spell (each with a second action when held: battery, mine, veil, ping). In the menus the D-pad or the left stick moves, A
confirms, B goes back and Start pauses. The game finds the controller by itself (press a button). The full tables, the rules and the limits are
in [controller.md](controller.md).

### Touch controls

On iOS and Android the game runs in landscape: drag on the left to move, drag in the free area on the right to look, and hold the buttons
for the continuous actions. The buttons (`TouchControls.cs`) are fire, aim, jump, crouch, run, use, reload, weapon, fire mode, torch,
battery, ping, lean left and right, pause, knife, veil and map. The Web build enables the same controls when it recognises a mobile browser.
Controls and performance have not been tried or tuned on real devices. Building for these platforms is in
[../engineering/building-and-shipping.md](../engineering/building-and-shipping.md#other-platforms).

## 13. Code map

The floor code is in `unity/PryingEyes/Assets/_PryingEyes/Scripts/Proc/` (namespace `PryingEyes.Proc`). Files outside that folder are noted.

**Plan generation**

- `PlanTypes.cs`, `PlanGen.cs`: the plan is integer geometry in decimetres, with no `UnityEngine` (it can be generated and tested outside Unity). `PlanGen.Compound` builds a floor from wings, `Attach` joins them on one side at random with one of the 8 transforms (rotations and mirror), and opens a door in a shared wall. Two small lift rooms (arrival and exit) attach to the outer walls. Windows and candle rooms are decided at the end. The original text says 3,000 plans in about 3 seconds, none failed (not verified).
- `PlanThemes.cs`: theatre and sanatorium, their wings, portals and room functions.
- `PlanGrid.cs`: the grid generator (growth of rectangles of cells, door tree, roles on leaves and distant rooms, rings, the two zones and their palettes, low passages). `ValidateGrid` checks roles, doors and sealable zones.
- `PlanAnnex.cs`: annex generation (annexes added to the plan).
- `RoundRegions.cs`: the sealable zones (integer geometry, no `UnityEngine`, tested with `planlab regions`).

**Building the floor**

- `MeshBuilder.cs`, `FloorBuilder.cs` (with `FloorBuilder.Annex.cs`, `Atrium.cs`, `Cathedral.cs`, `Curves.cs`, `Galleries.cs`, `Garden.cs`): thin-faced walls (each room draws its own, 15 cm inside the line), vertical slices for doors, arches and windows with pointed heads, flat, beam, truss or vault ceilings with ribs, opening frames, glass. A floor of about 105 rooms builds in about 210 ms, with the navmesh in about 40 ms (average over 500 floors in `ProcCheck`; not verified).
- `RoomKits.cs`, `RoomKits.Annex.cs`, `RoomKits.Spaces.cs`, `RoomKitsAlbergo.cs`, `RoomKitsChiesa.cs`, `RoomKitsSanatorio.cs`, `RoomKitsTeatro.cs`, `RoomKitsArena.cs`, `RoomKitsCover.cs`, `PropDefs.cs`: the furniture and light recipes per room type (seed per room). Furniture is prefabs from the `ProcKit`. If a model is missing, a box of the same size appears. L-shaped paths reserved between doors and free lanes in the nave keep furniture from closing a room.
- `RoomProbes.cs`: probes for the room kits.
- `Connectivity.cs`: a safety net. After the navmesh is built, every room, candle and exit must be reachable on foot. If not, it first removes furniture blocking door passages, then furniture closing the point where the path stops (two long tables forming a wall in the middle of an arena), then the surroundings of a candle sealed in a pocket. Only if that is not enough is the room emptied. A room counts as reached if you reach a point just inside one of its doors. After each change the navmesh is rebuilt.

**Floor and cycle logic**

- `FloorDirector.cs`: floor direction (hordes, weapons, bots), shrine validation (`ValidateShrines`), death penalty, the grid map (`GridMapItems`). Also `FloorDirector.Bot.cs`, `Look.cs`, `Trailer.cs`.
- `SurvivalMode.cs`: the cycle selection (section 1) and the speed multiplier.
- `RoundDirector.cs`: the round rules in `RoundRules`, the `Shutter`, and the round director (shutters, spawns, reward, cancellation).
- `RoundDirector.Survival.cs`: the wave cycle.
- `Enemy.Rounds.cs`: enemies that are always aggressive, movement, removal, and `SetHome` (a guard's home room).
- `HUD.Round.cs`: the red round number.
- `HotelNav` (in `Scripts/`): the door areas of the navmesh (`HotelNav.DoorBoxes`), one per opening. When shutters are down, enemies lose those areas from their mask (`HotelNav.EnemyMask`) and cannot pass, without rebuilding the navmesh or carving.

**Lifts, lights, interaction**

- `ProcLift.cs`: lifts.
- `Candelabro.cs`: candelabra. The decorative ones are lit; the objective ones are lit by the player.
- `Interact.cs`: the `F` interaction.
- `LightPool.cs`: pool of lights (only nearby ones on, only the four nearest cast shadows; not verified).
- `ProcAmbience.cs`: ambience (organ and long reverb in the church, hum and short echo in the hotel, distant tolls, gramophone waltz).
- `Power.cs`: power. Books, altars and some other things work only with power.
- `RoomAccessState.cs`, `RoomAccessDoors.cs`: the paid doors of the wave cycle.
- `ShrineKit.cs`: materials and free spots in common, with `RoomGrid` (tells whether a new obstacle keeps the doors reachable on foot).

**Shrines and perks**

- `Biblioteca.cs`: lecterns and books.
- `Scrigno.cs`: the Scrigno.
- `Consacrazione.cs`: the altar.
- `Drops.cs`, `DropPickup.cs`: the four power-ups (rules, timers, procedural models). `HUD.Punti.cs`: points that rise, and timers.
- `HUD.Perks.cs`: perk HUD.

**Other files in `Proc/`:** `AnnexRuntime.cs`, `Barrels.cs`, `BreakableLamps.cs`, `Chandeliers.cs`, `DoorTraps.cs`, `LampFlicker.cs`, `LightShafts.cs`, `NeonFlicker.cs`, `PerfProbe.cs` and `PerfProbe.Horde.cs`, `TrailerCapture.cs`, `TrailerJob.cs`, `Util.cs`.

**Outside `Proc/`** (the Tower uses them, but they are not in the folder):

- `Scripts/Punti.cs` (the points table and `DamageSource`), `Scripts/Knife.cs`, `Scripts/SoundKnife.cs` (procedural sounds of the knife and power-ups).
- `Scripts/Stealth/Enemy.Knife.cs` (reach, back, `CanTakedown`, `Provoke`), `Scripts/Stealth/Torch.cs`, `Scripts/Stealth/Guide.cs` (ping).
- `Scripts/Perks.cs` (perk state and effects), `Scripts/Medals.cs`, `Scripts/Equipment.cs`.
- `Scripts/EnemyTemi.cs` (themed enemies).

**Editor tools** (`Assets/_PryingEyes/Editor/`): `TorreBuilder.cs` (scene and material/prefab kit), `ProcShots.cs` (photos of a floor in edit mode, `-nav` draws the navmesh), `ProcCheck.cs` (connectivity over hundreds of floors, ASCII map of a room), `NavMap.cs` (floor navmesh in PPM: walkable, arrival component, doors that separate it from the rest), `NemiciTemiImport.cs`, `ArmiImport.cs`, `ManiImport.cs`. Scene and build: `-executeMethod PryingEyes.EditorTools.BuildPlayer.Torre` (builds the player, see [../engineering/building-and-shipping.md](../engineering/building-and-shipping.md); not run here).

## 14. Assets

- **Procedural PBR textures**: `modelli/hotel/genera_texture_chiesa.py` (wall stone, slabs, crypt stone, vault brick, parquet, tiles, concrete, four stained glass windows). Theatre and sanatorium: the `ruggine` (rust) texture in `modelli/hotel/genera_texture.py` (role `A_ruggine`: painted iron that chips and shows rust).
- **Blender models**: `modelli/chiesa/genera_chiesa.py` (bench, altar, reredos, candelabrum, candle stand, confessional, pulpit, font, statue, organ, sarcophagus, chandelier, stall, kneeler) and `modelli/hotel/genera_arredi2.py` (bed, bedside table, wardrobe, desk, chair, table, piano, stove, steel table, shelf).
- **Theatre and sanatorium models**: `modelli/hotel/genera_arredi3.py` (12 models; `blender -b -P genera_arredi3.py -- render [names]` makes CPU previews without touching the GPU, `-- build` makes the FBX files in `arredi3/`).
- **Guardaroba models**: `modelli/hotel/genera_arredi4.py` (17 props, `gu_*`; commands, materials and the photo tool `PropShots` in [guardaroba.md](guardaroba.md)).
- **Themed enemy accessories**: `modelli/temi/genera_accessori.py` (`blender -b -P genera_accessori.py -- build` makes the FBX; with `maschera`, `direttore`, `infermiere` or `primario` it makes CPU previews in `render/`). Costumes: `modelli/temi/genera_costumi_tex.py` (the design is evaluated in 3D at each pixel's position on the atlas; the normal map is the monk's). `Editor/NemiciTemiImport.cs` makes the prefabs in `Resources/` (`-executeMethod PryingEyes.EditorTools.NemiciTemiImport.RunAndShot`; photos in the hall in `hotel_build/shot_temi_*.png`).
- **Knife**: `modelli/armi/armi_coltello.py`, then `ArmiImport`/`ManiImport` with `-armi kn_coltello -mani kn_coltello`.
- **Destinations**: the FBX files are copied to `Assets/_PryingEyes/Models/{Chiesa,Arredi}`, and `TorreBuilder.FillKit` makes the prefabs from them.

## 15. Changes from the original section

Corrections where the code differs from the original text (the section of the former Italian README this page started from, see the top of the page):

1. **Cycle status.** The original describes the candle cycle as the main game in several places. It is now marked as legacy, and the wave cycle as the default since 13 November 2026 (`SurvivalMode.cs`).
2. **Wave-cycle speed.** The original says ×1.6 flat. The code gives ×1.6 at wave 1, rising to ×2.2 at wave 10, and ×1.15 for flying enemies (`SurvivalMode.SpeedMultiplier`).
3. **Grid room count.** The original says 10 to 13 large rooms. The code gives 13 on floors 1 to 3, rising by one every three floors up to 17, and one fewer in simple mode (`PlanGrid.BuildGrid`).
4. **Wave layout area count.** The original says about 8 main areas. The code gives 7 to 9 (7 on floors 1 to 4, 8 on floors 5 to 8, 9 from floor 9).
5. **Segreta.** The original describes the Segreta in general. It exists only in the candle-cycle grid. The wave layout has none (`PlanGrid.cs`).
6. **Cardinale.** The original does not mention it. The first enemy of every eighth wave is the Cardinale (`RoundDirector.BeginSurvivalWave`).
7. **Enemy variants of the wave cycle** (Flagellante, Appestato, Statua) are not in the original text. They are added in section 5.
8. **Descent bonus** (100 plus 50 times the floor, and a quarter of the ammo) is not in the original text. It is added in section 7.
9. **Code map.** Several files in `Proc/` were not listed, and some files attributed to it are in `Scripts/` (Punti, Knife, SoundKnife, Perks, Medals, Equipment, Enemy.Knife in `Scripts/Stealth/`, Torch in `Scripts/Stealth/`). Both are corrected in section 13.
10. **Spazzino and Vigile** are not Tower enemies. They belong to the city mode (section 10).

Not confirmed: the knife's "within one metre" rule from the original text (not verified; the code's takedown reach is 2.35 m, `Knife.cs`).

## 16. Unverified items

- **Measurements from the planlab and ProcCheck tools:** 1,550 m² per floor, 76 m to the exit, 71 to 310 m² per arena, 8.4 to 10.8 m per cell, 97% of floors with a Segreta, 925 m² per wave area, about 6,000 m² of rooms, about 25 rooms per wing, 28 to 330 m² sealable zones, about 72% of candles with a zone, 2,000 plans in half a second with none failed and eight in ten on the first try, 3,000 plans in about 3 seconds, 210 ms build and 40 ms navmesh per floor.
- **Co-op purchase range** of 4.5 m.
- **`Y` battery threshold** of 80%, and the automatic use of spares.
- **Last eight seconds** of power-up blinking, and the colours of the power-up light columns.
- **Lighting and fog descriptions**, the room and themed-ambience descriptions, and the furniture count of the models (12).
- **Five grades** of the Scrigno weapons (not checked against the weapon table).
- **Enemy damage ×1.5** for the candle cycle (code found, condition not checked).
- **`planlab grid` and `planlab regions`** command names (the folder `tools/planlab` exists; the arguments were not checked).
- **Classic hall not in the build** (`Hotel.unity` and `HotelBuilder` exist in the project, the build contents were not checked).
- **Occhio della Torre duration:** `PlayerPerks.EyeTime` is 6 s, but whether it is the ping's duration was not checked.
- **Map rule** ('?' for rooms beyond an open door, secret room shown only after entry) and **the 97% Segreta** share: not checked in `GridMapItems`.
