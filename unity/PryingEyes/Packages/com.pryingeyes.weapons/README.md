# Production Weapons

A local Unity package whose runtime assembly has `noEngineReferences: true`: the data of a weapon, the rules of a catalogue of weapons, the arithmetic of the gunplay
(spread, bloom, recoil, damage) and the mechanical state of a weapon in a player's hands. Nothing here uses an engine, a clock, an input device, a file or a game. The caller owns where the records come from,
what a weapon looks and sounds like, who takes the damage and how the player is told; the package owns the numbers and the rules that need nothing else.

| Type | What it owns | Guarantees |
| --- | --- | --- |
| `WeaponDef`, `FireMode`, `ReloadStyle` | The record of one weapon, as a catalogue writes it: damage and its fall with range, pellets and their cone, pierce, rate of fire, magazine and reload, accuracy and how it grows with each shot, recoil, handling, and the hints for sight, sound, tracer and hands that the caller draws and plays. Plain data: one public field for each of the 63 columns of the catalogue. | The names of the fields and their order are the columns of a catalogue file, and the content hash of the catalogue is taken over them: neither changes without a new version of the catalogue (`WEAPON_DEF_FIELDS_UNCHANGED`, `WEAPON_DEF_DEFAULTS_UNCHANGED`). |
| `Rgba` | A colour as four floats: the tracer, the flash, the reticle and the grade of a weapon. The caller turns it into the colour of its own engine. | None to keep: it is four numbers. |
| `WeaponCatalogRules` | What a catalogue must satisfy once its records have been read: identifiers and indices that only ever grow, finite numbers, and statistics of a weapon that can be fired and reloaded. | The same texts as before the rules left the game, for the same faults, in the same order (`WEAPON_RULES_EQUAL_LEGACY_*`). |
| `WeaponBallistics`, `Pierce` | What a bullet does, in numbers: the fraction of the damage left at a distance, what the damage of a hit is made of (the weapon, the distance, the enemies already passed, the head, the upgrade), the cone of the pellets, and what each enemy passed through takes. | The same float to the last bit as the code the game had, over millions of inputs (`GUNPLAY_*_EQUALS_LEGACY`). |
| `AimStance`, `WeaponSpread`, `WeaponBloom` | The cone a shot falls in: the weapon's own from the hip to aiming, wider when moving or in the air, narrower crouching, plus the bloom that each shot leaves and that goes away by itself; the first shot after a pause, aiming and still, kept tight; the radius of a cone. | As above. |
| `WeaponRecoil`, `ViewRecoil` | The kick of the view at a shot (it grows with a burst; aiming and crouching soften it) and the way the view comes back: a target that a kick lifts, that returns to zero after a short pause, and a view that follows it. | As above, and `ViewRecoil` is checked through every number it keeps, not only the two it shows. |
| `WeaponState` | Ammunition, reload transitions and cadence (below). | See below. |

```csharp
var rifle = new WeaponDef { id = "rifle_a", name = "Rifle A", cls = "RIFLE", damage = 30f, rpm = 600f, mag = 30, reserve = 120, reserveMax = 240 };
var pistol = new WeaponDef { id = "pistol_a", name = "Pistol A", cls = "PISTOL", damage = 22f, rpm = 420f, mag = 15, reserve = 60, reserveMax = 120 };
string problem = WeaponCatalogRules.Check(new[] { 0, 1 }, new[] { rifle, pistol });    // null when the catalogue is valid, else "CODE: words"
```

The arithmetic takes numbers and gives numbers: where a bullet goes, whom it hits, what a camera does with the recoil and what the damage is then multiplied by in a
game (perks, upgrades, an enemy's own armour) are the caller's, and so is every random draw: the rules take the draw as an argument, so the same inputs give the same
result. They use a few helpers of their own that mean what the engine's `Mathf` means (`WeaponMath`, internal), so that an expression written once gives the same float
whichever side runs it.

The package reads no file. The caller makes the records from whatever it keeps them in, and hands `Check` the index each record declares and the records, in file
order. In Prying Eyes `WeaponDefs` reads `Resources/Production/weapons.json` with Newtonsoft.Json, checks that every column is there with the right type, and calls
the package for the rest: a package may not reference a library, so the reader stayed in the game, and the editor checks it there (`ProductionCatalog.VerifyMigration`: the
serialisation of the first weapons against a fixture, and nine invalid catalogues that the reader must refuse).

## State of a weapon in a player's hands

`WeaponState` owns ammunition, reload transitions and cadence. It has no Unity, catalogue, input, clock, damage, upgrade, audio or HUD dependency.

The actual game composes one state per inventory slot inside `Weapon.cs`.
`unity/LayoutDemo/Assets/WeaponDemo.cs` is a second caller with its own eight-round
weapon, immediate-mode buttons and no Prying Eyes scripts or assets. `WeaponRange.cs` there fires four weapons of a catalogue that belongs to no game at rows of targets: the cone,
the bloom, the damage at a distance and through enemies, the kick of the view, the rounds and the cadence are the package's, the random draws and the hits are the caller's
(`-executeMethod WeaponDemo.Verify` prints what each shot did and checks what the package promises of a shot).

```csharp
var state = new WeaponState();
state.SetAmmunition(2, 20);
state.BeginReload(8, ReloadTiming.Magazine(1.2f, 1.7f));
ReloadStep step = state.AdvanceReload(.1f);
state.AdvanceCadence(.1f);
// Caller maps step.Events and step.RoundsInserted to its presentation.
```

Interface rules:

- Supply finite, nonnegative elapsed seconds. Time scaling and rate bonuses belong
  to the caller. Reload time and fire cooldown can use different effective rates.
- `BeginReload` captures the capacity and timing for that reload. A full magazine,
  zero reserve or an already active reload returns false. Invalid values throw.
- Per-round reloads expose insertion counts, progress and completion. Cancellation
  keeps rounds already inserted. Magazine reloads transfer rounds at completion.
- `Ready` expresses mechanical readiness. Input, sprint, draw and knife locks stay
  with the caller. The accepted-shot path consumes a round and records its interval.
- Cadence keeps the current frame's overshoot but does not accumulate idle credit.
  Manual cycles and burst pauses are separate operations.
- `SetAmmunition` deliberately permits a count above a normal capacity for restoration
  and existing debug scenarios. A reload transfer cannot create ammunition, make
  reserve negative or increase the magazine above its captured capacity.
- Call `ResetActions()` on equip/switch/respawn; it clears reload and timer locks without
  changing stored ammunition. Inventory selection and upgrade policy stay in the game.

## Verification in this repository

```powershell
python tools/studio.py test weapon-state
```

The console lab (`tools/weaponslab`) compiles these exact sources with .NET. For `WeaponState` it compares all 30 real
weapon definitions against a frozen reload implementation from commit `6fcbecf`.
It checks frame traces, event stages, partial/empty magazines, scarce reserve,
upgraded capacities, several frame steps and accelerated reloads. Extra contracts
cover cancellation, a refill during reload, invalid inputs and cadence.
For the data it compares the record and the rules of a catalogue against a frozen copy of the code the game had before they moved (commit `e25e832`):
the same fields in the same order with the same defaults, the same grades, names, colours and labels, and the same answer to each of thousands of faulty catalogues
(each of the 30 weapons hit in turn by each kind of fault: a bad index or identifier, a number that is not finite, statistics that cannot be fired).
For the arithmetic it compares every function with the expressions that the game had in `Weapon.cs`, `PlayerController.cs` and `PlayerController.Ride.cs` (frozen in `LegacyGunplay.cs`), and the two
give the same float to the last bit: the 30 weapons and three made up to sit on the edges of the expressions (a range shorter than a tenth of a metre, four pellets and five),
over grids that include each value where a test of an expression flips and the float just either side of it (spread: weapons x speeds x stances x aims x blooms x multipliers; falloff
and damage: a quarter of a metre apart to 300 m, and every edge), and long sequences of shots and time with a fixed seed (the bloom; the view of the player, whose six numbers are
compared after every event, on foot and on a bike). A further set of statements says what the numbers mean whatever the old code did (the falloff ends where it says, a plain hit at
point blank does the weapon's damage, a bullet passes exactly as many enemies as the weapon allows, the bloom has a ceiling and a floor, the view has a ceiling and comes back).
When the package was written the lab was also tried against 19 deliberate mutations of it (a changed constant, a product in another order, `>` for `>=`, a flipped
sign), and each one made it fail. The references are verification-only, never compiled into the package or game.

The native gunplay suite verifies the Unity adapter. The independent demo consumes
the package through its own manifest and checks fire, cooldown, reload lock and
ammunition conservation, and its shooting range fires a rifle, a machine gun, a shotgun and a bolt action with everything above. Fire-mode selection, burst input, inventory, the direction a
bullet takes (a vector in the engine's own type), what it hits, the sway and kick of the
model and the presentation are still outside the package.

## Status

One game has used this package so far, Prying Eyes, so its interface is shaped by that one game and a revision is expected when a second one asks for it.
What still looks like that game:

- `WeaponDef.GradeName` and `ModeLabel` write the words that its HUD shows, in Italian, and `GradeColors` are the colours of its five grades.
- Some columns are that game's: `cityOnly` (a weapon that only the city prototype sells), `price` (in its points), `cls` (class names written in Italian, which its hands
  and its holds read), and the hints `snd`, `sight`, `casing` and `fx`, which name its sound sets, its sights, its casings and its effects. The package never reads them.
- Magazines, reserves and prices are whole numbers, angles are degrees and times are seconds, as that game's catalogue has them.
- The tuning of the arithmetic is that game's: a run counts in full at 5.6 m/s (`AimStance.RunSpeed`), a crouch narrows the cone by 28 %, the air widens it by 160 %, the view climbs at most 20 degrees and goes
  14 to a side, a burst adds 3.5 % to the kick up to the tenth shot. A second game that wants other numbers will need them as parameters (or in the record).
