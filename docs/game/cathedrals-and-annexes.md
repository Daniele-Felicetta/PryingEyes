# Cathedral halls and annexes (survival layout)

Request of 15 November 2026: more vertical space in the map; the big rooms become huge cathedrals; inside them (or
under or beside them) rooms that are hard to reach and unlock only with some effort: some by stairs, hidden or not,
inside the cathedral itself, some underground, some as secret gardens.

Scope: the procedural survival floors ("La Torre", `PlanGen.SurvivalLayout`, the default cycle). The legacy candle cycle
(`-candles`) and the city mini-map are untouched. The generator part is pure integer geometry with no engine types, so
`tools/planlab` compiles it from the same files.

## Cathedral halls

| Piece | Where | What it does |
| --- | --- | --- |
| Heights | `PlanGrid.cs`, `CathedralLow = 13.2`, `CathedralHigh = 17.6` | Rooms with a gallery get a 13.2 m wall (one gallery level) or 17.6 m (the three-level atrium). Gabled ceilings add 4.5 m. |
| Lancets | `PlanGen.TallenWindows` (`PlanGrid.cs`) | In rooms with walls of 11 m or more, windows facing the void become lancets from 2.6 m up to 6.4 m (13.2 m rooms) or 10.2 m (17.6 m rooms) above the sill. Walls shared with lower rooms get clerestory lancets (one-sided openings) above the neighbour's roof, one every 6.2 m. |
| Stained-glass light | `RoomKits.WindowLights` | A tall window starts its spot much higher, so intensity grows with height^1.6 (clamped to 1x-6x), range grows 0.9 m per metre of height and the pitch is steeper. Only grid plans, only lancets above 6 m. |
| Wall ribs | `FloorBuilder.Cathedral.cs` | Visual-only stone pilasters with three corbels and a capital, every 4.5 m between windows and doors, from just above the topmost gallery rail to 0.7 m under the ceiling. No collider, at most 40 cm deep, so the navmesh is unchanged. |

## Annexes

Three kinds, generated per floor by `PlanAnnexGen` (`PlanAnnex.cs`) and built by `FloorBuilder.Annex.cs` (tower, crypt) and
`FloorBuilder.Garden.cs` (garden), plus (16 November 2026) the secret book room on every floor (`Sacrario`, built by
`FloorBuilder.Sacrario.cs`; it holds a grimoire instead of a chest and opens by finding its wall: see
[arms-and-magic.md](arms-and-magic.md)). At most one annex per host room (the secret book room, outside a wall, may share a hall with
a tower or a crypt). Floor 1: crypt and tower. Floor 2: tower and garden.
Floor 3 and beyond: all three, when the floor has the rooms for them.

| Kind | Reached by | Notes |
| --- | --- | --- |
| Tower (`Torre`) | A spiral stair in a round shaft standing in a cathedral hall, up to a cell at 7.2 m (13.2 m halls) or 10.8 m (17.6 m halls). | The shaft base is closed by a false wall plug (`secret`: looks like wall until you look at it) or by an iron grate. |
| Crypt (`Cripta`) | A floor hatch in any large room, a spiral stair down to a chamber at -4.4 m with sarcophagi and an altar. | The chamber may extend under the neighbouring rooms or the void; it never touches a floor above it. The hatch is a stone disc. |
| Secret garden (`Giardino`) | A low passage through a wall of the host room, hidden by ivy, into an open-air walled garden under a night sky. | Always opened by a key. |

Every annex holds an ancient chest (`Scrigno.PlaceAt`): a free, one-shot rare weapon (grade 4 and up in towers, 3 and up
elsewhere). The gate is opened in one of three ways (`GateKind`):

- **Sigilli**: two levers (floors 1-3) or three, in the host room. The first is on the ground, the others on the gallery
  levels. All must be pulled within 160 s (130 s with three levers) of the first, otherwise they reset.
- **Tributo**: pay `1500 + 250 * min(floor, 8)` points at the gate or at a small altar near it.
- **Chiave**: the garden's golden key sits on a pedestal inside the tower, or in the crypt when there is no tower. The key
  is per floor and per machine.

## Runtime

- `FloorDirector.GoTo`: `FloorBuilder.Build` -> `AnnexRuntime.Place` (gates, levers, chests, keys; before the navmesh) ->
  `HotelNav.Rebuild` -> `Connectivity.Repair` -> `AnnexRuntime.Arm` (one log line per annex: entrance, inside and
  treasure reachable from the arrival lift).
- The closers (false wall, grate, hatch disc, garden door) are on `HotelNav.DoorLayer`: ignored by the navmesh, solid for
  the character controller. Opening disables the collider and animates; there is no re-bake.
- `FloorInstance.RoomAt` / `CeilingAt` are height-aware, so the crypt (below -1.5 m) and the garden belong to their host
  and the fall guard does not treat them as the void.
- `AnnexRuntime.Forbidden` keeps enemy spawns out of the shaft cylinder, the crypt chamber and the garden: both when the
  spawn candidates are built (`RoomKits.Spawns`) and when one is picked (`FloorDirector.PickSpawn`).
- State (opened gates, pulled levers, keys, spent chests) is local to each machine, like the shop chest. In co-op each
  player opens their own annexes; the plan itself is identical on every machine (`FloorSignature` includes the annexes).

## Verification

- `dotnet run -c Release -- annex <N> [list]` in `tools/planlab`: N seeds with the annex generator, 0 geometric or access
  faults, deterministic. `list` prints one `ANNEXLIST` line per plan.
- `Assets/_PryingEyes/Editor/AnnexShots.cs` (`-executeMethod PryingEyes.EditorTools.AnnexShots.Run -seed S -floor F`):
  edit-mode photographs of every annex from the player's eye height, gates closed, then opened, plus the cathedral halls
  and a top view.
- `-autotest -nofire -noenemies -annextest -seed S -floor F` (`Scripts/AnnexTest.cs`): per annex, walks with the real
  character controller. Closed: cannot enter. Opened (through the real lever, tribute or key logic, including the lever
  time limit): reaches the inside, the chest and the key, and walks back. Checks that every usable object is reachable
  with F and that no spawn candidate falls inside an annex.
- `tools/check_navigation.py` (strict bot) and the co-op scenarios must still pass: annexes must not break the paths
  between the arrival and the exit, nor the plan signature (`FloorSignature` hashes the annexes).

Automated player runs are silent: `GameSettings` mutes the listener before scene loading when launched with `-autotest`,
`-guntest`, `-batchmode`, `-nographics` or `-nosound`. Later settings updates keep it muted for that process. This does not
write the muted volume to PlayerPrefs, and ordinary interactive play still uses the player's saved volume. `AnnexTest`
checks the listener volume as part of its runtime assertions. Existing binaries need rebuilding to pick up this rule.

The architecture regression at run seed 2961, floor 1 exposed a lever pedestal placed directly on an upper-gallery route
waypoint. Gallery lever placement now reserves 0.9 m around every route segment rather than placing the pedestal at its
center. The existing `-architecturetest` exercises the real controller and navmesh for this case; `AnnexCheck` also checks
upper-gallery waypoints from the arrival lift.

Verification on 15 November 2026:

- `python tools/check_suite.py`: PASS, zero findings; Python unit suite: 73 tests, OK.
- `dotnet run --project tools/planlab -c Release -- annex 1500`: 1,500 plans, zero faults; 1,488 towers, 1,375 crypts,
  1,242 gardens. Minimum tower-cell headroom above its ceiling: 2.6 m.
- `AnnexCheck`, 120 plans: 743 cathedral halls, 330 annexes (118 towers, 110 crypts, 102 gardens), all 379 levers
  reachable; zero lateral entrances, unreachable gallery waypoints, rooms or exits. Mean build time 79 ms, navmesh 157 ms,
  sealed navmesh 154 ms; 173 registered lights per floor on average (`unity_logs/annexcheck-final.txt`).
- Strict navigation: run seeds 1000, 2961 and 9186, three consecutive floors each; all nine completed with zero warnings,
  narrow passages or failures (`unity_logs/annex-navigation-final`).
- Survival at run seed 2961: 86/86; local co-op at the same seed: host 38/38 and client 50/50, including matching floor
  signatures and no game exceptions (`unity_logs/annex-coop-final-silent`). Annex progress remains local as described above.
- Final rebuilt player: annex walks at run seed/floor 2095/7, 5041/4 and 6395/3 passed 49/49, 39/39 and 53/53 checks,
  including the automatic mute assertion; architecture at 2961/1 passed 74/74 after the lever-placement fix
  (`unity_logs/final-annextest*.log`, `unity_logs/final-architecturetest2961.log`). These final runs used no `-nosound` flag:
  the game applied the test mute itself.

## Known limits

- Difficulty, prices and the lever time limit are untested by a human player.
- The garden is lit by one moon spot and four lamps; the sky is a dome, not a skybox.
- No minimap marker for annexes, gates or keys; the F prompt names what the gate needs.
- Annex progress is not replicated in co-op. The plan rules changed (cathedral heights, annexes), so the protocol revision
  moved to `pe-coop-18` and builds from before this change cannot join.
