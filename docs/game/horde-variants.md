# Horde variants and blood

Survival waves mix common enemies with specialists. The walking variants reuse the rigged monk and abbot in
`Scripts/Enemy.Varianti.cs`. The Sentinel uses an articulated model and the shared flying navigation.

| Variant | Base | From wave | What it does |
| --- | --- | --- | --- |
| Appestato | monk | 4 (5% + 1% per wave, max 14%) | Slow and bloated (sickly robe, swollen belly, glowing pustules). Within 1.9 m of a player it bursts; it also bursts when killed. The burst hurts players (34 to 10 damage within 3.6 m, line of sight) and every enemy in range (320 to 90), then leaves a toxic cloud for 6.5 s (`ToxicCloud.cs`: 8 damage/s to players, 60/s to enemies). Shooting it inside the horde is the point. A self-burst pays nothing; kills by its burst or cloud pay like a gun kill (`DamageSource.Esplosione`, `Scoppio`). |
| Sentinella | flying reliquary | 4 (13%, at most two alive) | Hovers about 7 m from its target where space permits. It locks its aim when the amber iris opens, then fires a visible bolt after 0.85 s. Sidestep the shot or put a wall in its path. The iris is a weak point. |
| Flagellante | monk | 3 (8% + 1.2% per wave, max 20%) | Red robe, fast (2.35-2.75 m/s before the survival multiplier), 95 hp. From 2.6-7.5 m with a clear line it leaps (about 11 m/s for 0.32 s) and strikes on landing. |
| Statua | monk | 6 (5%, at most one alive) | Dark stone monk. It moves only when no player is looking at it (55 degrees from any player's view axis, line of sight, rechecked every 0.08 s), and then runs at 5.2 m/s with glowing eyes and a stone grinding loop. Bullets deal 40% (knife 15%); 720 hp; 38 melee damage. |
| Cardinale | abbot x1.18 | every 8th wave (not processions), first spawn | Boss: crimson robe, mitre, gold halo. HP 3200 + 1300 per extra player + 120 per wave. Fire ring (telegraphed 1.1 s, 22-28 damage within 7 m), a five-bolt fan (seven when enraged), summons three or four monks. Below half health it enrages (speed x1.35, cooldowns x0.7). Kill: 1000 points to the killer and a Max Ammo drop. The HUD shows its name and health bar (`HUD.Boss.cs`). |

Spawning lives in `RoundDirector.Survival.PickSurvivalType`. Themed floors keep their costumes for ordinary monks and
abbots (`FloorDirector.Retheme`); the variants are not re-themed. In co-op the host decides bursts, damage and who is
watching the statue. Clients replay the visible effects. The host also controls the Sentinel's aim, shot and damage.
Clients animate its charge and recoil from enemy events and replay the amber bolt using protocol `pe-coop-40`.

## Sentinel model and animation

`modelli/sentinel/generate.py` exports the FBX and its patina texture to `unity/PryingEyes/Assets/_PryingEyes/Resources/Sentinel` when run with
`blender -b --python-exit-code 1 -P modelli/sentinel/generate.py` from the repository root. The model has a bronze lancet
frame, three articulated verdigris feathers on each wing, six brass-edged shutters and a hanging censer. `SentinelRig.cs`
adds the hover, phased wing movement, shutter and light buildup, chassis recoil, and wing collapse on death. Its
projectile uses the same swept collision and wall checks as other enemy bolts. Base health is 185, the range is 18 m,
and a bolt travels at 14 m/s and deals 16 damage.

## Blood

`Fx.Blood` sprays droplets and mist in the enemy's blood colour (`Enemy.BloodTint`: dark red for flesh, green bile for the
Appestato, acid green mucus for the Medusa, dust only for the statue and the moths) at every bullet hit, and places a splat on the wall
or floor behind the target along the bullet direction (2.4 m). `Fx.BloodPool` grows a pool under monks and abbots when they
die. Decals reuse the bullet-hole material with their own texture through a property block (no new shader variant), live
40-45 s and fade over the last 6 s; 72 at most, recycled.

## Verification

`PryingEyes.exe -batchmode -autotest -nofire -noenemies -variantitest [-variantishots] -seed 1000 -floor 1`
(`Scripts/VariantiTest.cs`): burst near the player, damage to player and neighbour, cloud and burst on
a distant kill with the player unharmed and the kill paid; the statue holds still while watched, closes in when the player
turns, stops again, takes 40% damage; the flagellant leaps; the cardinal is the boss, lights the bar, enrages, pays 1000 and
drops ammo, and the bar fades. The Sentinel checks cover the imported rig, aim, charge, projectile damage,
cover, a sideways dodge and the co-op replica's charge and flash.
Photos include the Sentinel hovering, charging and firing. They go to `unity_logs/varianti` of the checkout.
