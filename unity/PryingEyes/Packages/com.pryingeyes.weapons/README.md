# Production Weapons

A local Unity package whose runtime assembly has `noEngineReferences: true`: the data of a weapon, the rules of a catalogue of weapons and the mechanical
state of a weapon in a player's hands. Nothing here uses an engine, a clock, an input device, a file or a game. The caller owns where the records come from,
what a weapon looks and sounds like, who takes the damage and how the player is told; the package owns the numbers and the rules that need nothing else.

| Type | What it owns | Guarantees |
| --- | --- | --- |
| `WeaponDef`, `FireMode`, `ReloadStyle` | The record of one weapon, as a catalogue writes it: damage and its fall with range, pellets and their cone, pierce, rate of fire, magazine and reload, accuracy and how it grows with each shot, recoil, handling, and the hints for sight, sound, tracer and hands that the caller draws and plays. Plain data: one public field for each of the 63 columns of the catalogue. | The names of the fields and their order are the columns of a catalogue file, and the content hash of the catalogue is taken over them: neither changes without a new version of the catalogue (`WEAPON_DEF_FIELDS_UNCHANGED`, `WEAPON_DEF_DEFAULTS_UNCHANGED`). |
| `Rgba` | A colour as four floats: the tracer, the flash, the reticle and the grade of a weapon. The caller turns it into the colour of its own engine. | None to keep: it is four numbers. |
| `WeaponCatalogRules` | What a catalogue must satisfy once its records have been read: identifiers and indices that only ever grow, finite numbers, and statistics of a weapon that can be fired and reloaded. | The same texts as before the rules left the game, for the same faults, in the same order (`WEAPON_RULES_EQUAL_LEGACY_*`). |
| `WeaponState` | Ammunition, reload transitions and cadence (below). | See below. |

```csharp
var rifle = new WeaponDef { id = "rifle_a", name = "Rifle A", cls = "RIFLE", damage = 30f, rpm = 600f, mag = 30, reserve = 120, reserveMax = 240 };
var pistol = new WeaponDef { id = "pistol_a", name = "Pistol A", cls = "PISTOL", damage = 22f, rpm = 420f, mag = 15, reserve = 60, reserveMax = 120 };
string problem = WeaponCatalogRules.Check(new[] { 0, 1 }, new[] { rifle, pistol });    // null when the catalogue is valid, else "CODE: words"
```

The package reads no file. The caller makes the records from whatever it keeps them in, and hands `Check` the index each record declares and the records, in file
order. In Prying Eyes `WeaponDefs` reads `Resources/Production/weapons.json` with Newtonsoft.Json, checks that every column is there with the right type, and calls
the package for the rest: a package may not reference a library, so the reader stayed in the game, and the editor checks it there (`ProductionCatalog.VerifyMigration`: the
serialisation of the first weapons against a fixture, and nine invalid catalogues that the reader must refuse).

## State of a weapon in a player's hands

`WeaponState` owns ammunition, reload transitions and cadence. It has no Unity, catalogue, input, clock, damage, upgrade, audio or HUD dependency.

The actual game composes one state per inventory slot inside `Weapon.cs`.
`unity/LayoutDemo/Assets/WeaponDemo.cs` is a second caller with its own eight-round
weapon, immediate-mode buttons and no Prying Eyes scripts or assets.

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
The references are verification-only, never compiled into the package or game.

The native gunplay suite verifies the Unity adapter. The independent demo consumes
the package through its own manifest and checks fire, cooldown, reload lock and
ammunition conservation. Fire-mode selection, burst input, inventory, recoil and
presentation are still outside the package.

## Status

One game has used this package so far, Prying Eyes, so its interface is shaped by that one game and a revision is expected when a second one asks for it.
What still looks like that game:

- `WeaponDef.GradeName` and `ModeLabel` write the words that its HUD shows, in Italian, and `GradeColors` are the colours of its five grades.
- Some columns are that game's: `cityOnly` (a weapon that only the city prototype sells), `price` (in its points), `cls` (class names written in Italian, which its hands
  and its holds read), and the hints `snd`, `sight`, `casing` and `fx`, which name its sound sets, its sights, its casings and its effects. The package never reads them.
- Magazines, reserves and prices are whole numbers, angles are degrees and times are seconds, as that game's catalogue has them.
