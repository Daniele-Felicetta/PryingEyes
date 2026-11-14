# Production Weapons

A local Unity package whose runtime assembly has `noEngineReferences: true`.
`WeaponState` owns ammunition, reload transitions and cadence. It has no Unity,
Prying Eyes catalog, input, clock, damage, upgrade, audio or HUD dependency.

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
- Reset actions on equip/switch/respawn; this clears reload and timer locks without
  changing stored ammunition. Inventory selection and upgrade policy stay in the game.

Verification in this repository:

```powershell
python tools/studio.py test weapon-state
```

The console lab compiles this exact source with .NET and compares all 30 real
weapon definitions against a frozen reload implementation from commit `6fcbecf`.
It checks frame traces, event stages, partial/empty magazines, scarce reserve,
upgraded capacities, several frame steps and accelerated reloads. Extra contracts
cover cancellation, a refill during reload, invalid inputs and cadence.
The reference is verification-only, never compiled into the package or game.

The native gunplay suite verifies the Unity adapter. The independent demo consumes
the package through its own manifest and checks fire, cooldown, reload lock and
ammunition conservation. This is an initial mechanical-state extraction: fire-mode
selection, burst input, inventory, recoil and presentation are still outside it.
