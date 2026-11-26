# Holy oil barrels and enemies rising from the floor

Added on 15 November 2026 (survival cycle).

## Holy oil barrels (`Scripts/Proc/Barrels.cs`)

Three to five per floor, only in rooms of at least 10 x 10 m, at least 3.5 m from any paid door, 9 m apart, on points with
1 m of free space; half the time a second barrel stands right next to the first. Placement comes from the plan seed and
the floor's spawn points, so it is identical on every machine. Each barrel is a wooden cask with iron bands and a red
cross, a capsule collider and a carving `NavMeshObstacle` (enemies walk around it).

Two shots (40 damage) or a nearby explosion set it off: a fireball (520 damage at the centre, 120 at 5.5 m, line of sight;
players up to 45), a ring of flames, a scorch mark, a strong camera shake, and every other barrel within 4.5 m goes off
after a short delay proportional to distance (chain reactions). Other barrels do not shield the blast. Holy water flasks,
carillons and Appestato bursts set off barrels in range. Kills pay as explosions. The exploded barrel stops blocking.

Bullets reach barrels through the new `IShootable` interface in `Weapon.ShootRay` (any non-enemy collider that implements
it receives the hit instead of a bullet hole).

Co-op: a client's hit sends `BarrelHit` (index) to the host, which explodes the barrel, deals all damage and broadcasts
`BarrelGone`; late joiners receive the barrels already gone without effects. Protocol `pe-coop-22`.

## Rising from the floor (`Scripts/Enemy.Rise.cs`)

Walking enemies born in a survival wave start 1.9 m below the floor and rise in 1.0-1.35 s with an ease-out, among dust,
debris, a dark patch of broken floor and a creak. During the rise they have no navigation or AI but can be shot; at the top
the agent is re-enabled and warped to the spawn point. Flying enemies appear as before. Clients see the rise through the
host's position updates.

## Verification

`PryingEyes.exe -batchmode -autotest -nofire -noenemies -barreltest [-barrelshots] -seed 1000 -floor 1`
(`Scripts/BarrelTest.cs`, 19 checks on seed 1000 floor 1, 16 on seed 2961 floor 3 where no pair exists): barrels exist in
large rooms; a 50-damage shot explodes one; three monks around it die and the kills pay; a player 9 m away is unharmed; the
exploded barrel stops blocking; the neighbour goes off in a chain; a newly spawned monk starts under the floor, stands up on
the navmesh in a little more than a second and walks toward the player. `result.json` and the photos in `unity_logs/botti`.
