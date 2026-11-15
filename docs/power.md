# Floor power

Added on 15 November 2026 (`Scripts/Proc/Power.cs`).

In the wave cycle every floor now starts on emergency lighting, as in the Black Ops Zombies maps. The electrical panel hangs on a
wall of the paid area farthest from the arrival; pulling its lever brings the power back.

- **Emergency lighting.** Lights created by the room recipes are tagged as electric or not when they are registered with the
  light pool (`LightPool.Register(..., electric)`, `RoomKits.AddLight(..., electric: true)` by default): chandeliers, wall lamps,
  table and floor lamps, neon tubes, surgical lamps, theatre stage lights, gallery and atrium lamps and the rooms' ambient fill
  lights are electric; candles, the red sanctuary lamp, moonlight, window light, green exit signs and the theatre eye spot are
  not. Without power the electric ones run at 40%. `LightPool.LateUpdate` applies the scale after the flicker components
  (`LampFlicker`, `NeonFlicker`, `LightPulse`), which rewrite the intensity every frame; undriven lamps go back to the intensity
  they were registered with. With full power and the wave finished it does one last pass and then nothing.
- **The panel** (`PowerPanel`): an enamelled sheet-metal cabinet with indicator lamps, a dial and a warning plate, a knife switch
  on the right as seen from the room, conduits up to the ceiling and a caged emergency lamp that pulses red. It is placed on the
  longest wall of the deepest area (ties: the largest) where a full stretch of wall, floor and navmesh in front and free space to
  stand are found; the search is deterministic, so every co-op machine puts it in the same place. F pulls the lever (free).
- **Power on.** The lever drops with sparks at the contacts, the red lamp turns warm white, a procedural sound plays (lever
  thump, relays, a rising 50 Hz hum: `SoundGen.PowerOn`). Light comes back in a wave from the panel at 28 m/s; every lamp flickers
  on (on, dip, on) when the wave reaches it. The room reflection probes are rendered again when the wave is over
  (`RoomProbes.Rebake`), keeping the lights around the player on while each probe is photographed.
- **What needs power.** Perk books (`Biblioteca`), door traps (`DoorTrap.Use` and `HostActivate`) and the Consecration altar
  refuse with "Serve la corrente: trova il quadro elettrico" and show "SENZA CORRENTE" in their prompts. The mystery chest, wall
  buys and doors work without power. The objective line says "Trova il quadro elettrico" until the power is on.
- **Without a panel** (old candle cycle, `-nopower`, floors with no suitable wall) the power is on from the start.
- **Co-op** (`Coop/CoopGame.Power.cs`, protocol `pe-coop-23`): a guest pulling the lever sends `UsePower`; the host turns the power
  on and sends `PowerState` to everyone, and to late joiners with the floor state.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -powertest [-powershots] -seed 1000 -floor 1`
(`Scripts/PowerTest.cs`, 16 checks: the panel exists in the deepest area on a real wall with floor and navmesh in front,
electric lamps at 40% and the other lights untouched, trap and altar refused, F selects the panel, the lever turns the power on
for free, distant lamps wait for the wave, the lever is down, then every electric lamp is at full power and the trap works).
Photos in `unity_logs/corrente` (close and from the room, before and after). `TrapTest` turns the power on first
(`Power.ForceOn`). Not rerun for this change: the co-op scenarios.
