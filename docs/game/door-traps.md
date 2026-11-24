# Door traps

Added on 15 November 2026 (survival cycle only). Code: `Scripts/Proc/DoorTraps.cs`, co-op in `Scripts/Coop/CoopGame.Traps.cs`.

On some paid doors (at most three per floor, openings at least 1.2 m wide, at least 2 m tall, no sill), once the door has
been bought a lever on the wall beside the opening switches the trap on for **20 s** for **1000 points**; then it needs
**40 s** to recharge. Two kinds alternate:

| Kind | Look |
| --- | --- |
| Fuoco | flame tongues rising from the threshold, flickering orange light, roaring loop |
| Fulmine | blue arcs from jamb to jamb, sparks, stuttering blue light, electric buzz |

Both kill every ordinary enemy that crosses the threshold box (opening width x 1.8 m x opening height), flying ones
included; the boss loses 8% of its health per second. Kills pay the player who pulled the lever (`DamageSource.Esplosione`,
60 points). A player standing inside burns at 30 hp/s. Each lever has a status lamp: green ready, red on, amber
recharging, dark while the door is still closed. A lever is placed on each face of the wall where there is solid wall next to
the opening (checked with raycasts) and floor in front of it.

Placement: every second paid door in plan order (the parity shifts with the floor number), kinds alternate by door.

Co-op: the client reserves the points and sends `UseTrap` (request id, trap index); the host accepts only if the trap is
ready and its door open, replies with `TrapReply` (a refusal refunds the points) and broadcasts `TrapsState` (state and
remaining tenths of a second for every trap) on every change and to late joiners. Only the host deals damage and credits
kills. Protocol `pe-coop-20`.

## Verification

`PryingEyes.exe -batchmode -autotest -nofire -noenemies -traptest [-trapshots] -seed S -floor F` (`Scripts/TrapTest.cs`,
18 checks on the real floor): traps exist on real doors with levers; unusable while the door is closed; with the door open
the lever offers the trap for 1000 points and lights it; three monks walking through the threshold die and the kills pay;
a player inside burns; at the end of the duration it recharges, the lever refuses without charging, then it is ready again;
the second trap lights too. The test writes `result.json` and, with `-trapshots`, its photos into `unity_logs/trappole`, whatever the folder
the player is launched from (`-testout DIR` writes them into `DIR`; see [testing.md](../engineering/testing.md)).
