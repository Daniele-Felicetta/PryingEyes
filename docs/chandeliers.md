# Falling chandeliers

Added on 15 November 2026 (`Scripts/Proc/Chandeliers.cs`), an environmental trap for the wave cycle.

- **Which ones.** Every hanging `chi_lampadario` prop of the floor (the iron crowns of the church naves, the chandeliers of the
  gallery halls and of the halls with a fountain) whose bottom is at least 2.1 m above the floor below it. The model inside the
  prop has the same name, so only the outermost object counts (seed 1000 floor 1: 19 chandeliers).
- **Shooting them.** A sphere collider on the default layer makes them a target for bullets (`IShootable`, like the holy-oil
  barrels); 60 points of damage bring one down (two pistol shots).
- **The fall.** Its lights go out (the child lights and the lamp hanging at its centre are taken out of the light pool), the
  chain snaps (a third stays hanging from the ceiling; chandeliers without a chain get one at build time), it falls with gravity
  turning by 9-18 degrees around an axis fixed at build time (identical on every co-op machine), bounces a few centimetres and
  stays on the floor. Procedural sounds: `SoundGen.ChainSnap` and `SoundGen.ChandelierCrash` (heavy thump, ringing iron, a
  thinning rain of crystals, rolling debris), dust and sparks, a view shake.
- **Damage.** Enemies within 1.5 m plus 60% of the chandelier's half width (and 2.5 m of height) die and fly away from the impact
  point (`Enemy.NoteBlast`, see the ragdolls); bosses lose a quarter of their health. Kills pay as explosions, credited to the
  player who brought it down. Players within three quarters of that radius lose 60. Three kills or more show a toast.
- **Afterwards** it is an obstacle: a carving `NavMeshObstacle` (enemies walk around it, players can use it as cover). It
  cannot be hit again.
- **Co-op** (`Coop/CoopGame.Chandeliers.cs`, protocol `pe-coop-24`): a guest's hit goes to the host (`ChandelierHit`), the host
  drops it (with damage) and tells everyone (`ChandelierDrop`); late joiners get the fallen ones placed directly.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -lampadaritest [-lampadarishots] -seed 1000 -floor 1`
(`Scripts/LampadariTest.cs`, 15 checks: chandeliers exist, one is visible from afar with floor below, one 30-damage shot is not
enough and the second drops it, its light goes out and the chain snaps, it lands, the three monks below die and pay, the one just
outside the radius and the distant player are unharmed, it carves the navmesh and cannot be hit again). Photos in
`unity_logs/lampadari`. Not rerun: the co-op scenarios.

## Breakable lamps

Added on 15 November 2026 (`Scripts/Proc/BreakableLamps.cs`).

Every electric lamp that is a visible fixture breaks with one shot: table lamps (`Abatjour`), hanging bulbs (`Lampadina`),
electric wall lamps (`Applique`; the candle ones are not electric and do not break), floor lamps (`arr_lampada`), neon tubes
(`Neon`) and surgical lamps (`AstaScialitica`). They are found after the floor is built among the pool lights flagged electric
whose parent has one of those names; a small sphere collider on the lamp is the bullet target. Breaking it takes its light out of
the pool (`wanted = false`), stops its flicker (`LampFlicker`, `NeonFlicker`), swaps bulb, shade and tube to their unlit
materials (`lampadina_spenta`, `paralume_spento`, `neon_spento`), plays a procedural pop with glass shards
(`SoundGen.BulbPop`) and throws sparks. During a fight lamps go out one after another and the room really gets darker.
Co-op (`Coop/CoopGame.Lamps.cs`, protocol `pe-coop-25`): guests send `LampHit`, the host breaks the lamp and sends
`LampBroken` to everyone, late joiners receive the broken ones silently.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -lamptest [-lampshots] -seed 1000 -floor 1`
(`Scripts/LampTest.cs`, 10 checks: lamps exist and none is a candle, a lit one is hit by a real bullet ray, its light goes out
and leaves the pool, the target disappears, the materials turn unlit, a second shot does nothing). The church floor of seed 1000
has 5 (electric wall lamps); the hotel floors have more. Photos in `unity_logs/lampade` (the wall goes from 42.5 to 25.2 of mean
luminance).

