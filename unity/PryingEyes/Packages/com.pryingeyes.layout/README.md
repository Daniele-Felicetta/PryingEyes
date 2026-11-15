# Production Layout 0.1

A deterministic district planner with no Unity, Hotel, HUD or GameManager references.
`Production.Layout.asmdef` disables engine references. The same source is compiled by
PlanLab and installed as a local package by the independent `unity/LayoutDemo` project.

```csharp
var plan = Production.Layout.DistrictPlan.Create(2026, 39, 29, 5);
```

Arguments are the seed, half width, half depth and road half width, in metres.
The result contains four building footprints/heights, pavement footprints, entrances,
lamp positions, four enemy spawn candidates and three panel positions/rotations.
These are data: the consumer supplies meshes, materials, content IDs and game rules.
The arrays are mutable for serialization; the planner retains no reference or global state.

The current recipe supports a 2x2 district. Roads must be at least 8 metres wide and
building footprints at least 20x12 metres. Invalid or nonfinite dimensions throw
`ArgumentException`. Spawn candidates follow resized bounds with a five metre inset.
Heights are seeded; no global random state is touched. This module plans geometry,
not traversability: the built scene must still pass collision and controller tests.

Prying Eyes maps the three panels in order to southwest, southeast and northwest
weapon shops. Content identity and pricing stay outside this package.

`RoomPlan.Create(recipe, width, depth)` plans a furnished room (parts, doors, stairs,
route and views) from a recipe. It rejects blocked passages, floating or blocked standing
points and stairs that are too steep. `unity/LayoutDemo` checks it with `Assets/room-demo.json`.

Standalone JSON output:

```powershell
dotnet run -c Release --project tools/planlab -- city unity/PryingEyes/Assets/_PryingEyes/City/citta.json unity_logs/city_layout.json
```

Independent Unity consumer, tested with Unity 6000.6.7f1:

```powershell
Unity.exe -batchmode -projectPath unity/LayoutDemo -executeMethod DistrictDemo.Verify -quit -logFile unity_logs/layout-demo.log
```

The demo installs the layout, weapons and survival packages and built-in Unity modules,
not Prying Eyes code. It checks determinism, a differently sized variant, invalid and
overflowing dimensions, room stairs and route validation, scene generation, and the
weapon, wave and paid-access states (20 checks). Open the project and choose
**Layout Demo > Generate and Verify** to generate its blockout scenes.
This is evidence of reuse for these packages; it does not claim that combat, presentation
or the full Prying Eyes production suite have been extracted.
