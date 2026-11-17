# Horde variants and blood

Added on 15 November 2026 so that survival waves stop feeling identical. The variants reuse the rigged monk and abbot
(same skeleton, clips and navigation) and change the body at runtime plus one rule each. Code: `Scripts/Enemy.Varianti.cs`.

| Variant | Base | From wave | What it does |
| --- | --- | --- | --- |
| Appestato | monk | 4 (5% + 1% per wave, max 14%) | Slow and bloated (sickly robe, swollen belly, glowing pustules). Within 1.9 m of a player it bursts; it also bursts when killed. The burst hurts players (34 to 10 damage within 3.6 m, line of sight) and every enemy in range (320 to 90), then leaves a toxic cloud for 6.5 s (`ToxicCloud.cs`: 8 damage/s to players, 60/s to enemies). Shooting it inside the horde is the point. A self-burst pays nothing; kills by its burst or cloud pay like a gun kill (`DamageSource.Esplosione`, `Scoppio`). |
| Flagellante | monk | 3 (8% + 1.2% per wave, max 20%) | Red robe, fast (2.35-2.75 m/s before the survival multiplier), 95 hp. From 2.6-7.5 m with a clear line it leaps (about 11 m/s for 0.32 s) and strikes on landing. |
| Statua | monk | 6 (5%, at most one alive) | Dark stone monk. It moves only when no player is looking at it (55 degrees from any player's view axis, line of sight, rechecked every 0.08 s), and then runs at 5.2 m/s with glowing eyes and a stone grinding loop. Bullets deal 40% (knife 15%); 720 hp; 38 melee damage. |
| Cardinale | abbot x1.18 | every 8th wave (not processions), first spawn | Boss: crimson robe, mitre, gold halo. HP 3200 + 1300 per extra player + 120 per wave. Fire ring (telegraphed 1.1 s, 22-28 damage within 7 m), a five-bolt fan (seven when enraged), summons three or four monks. Below half health it enrages (speed x1.35, cooldowns x0.7). Kill: 1000 points to the killer and a Max Ammo drop. The HUD shows its name and health bar (`HUD.Boss.cs`). |

Spawning lives in `RoundDirector.Survival.PickSurvivalType`. Themed floors keep their costumes for ordinary monks and
abbots (`FloorDirector.Retheme`); the variants are not re-themed. In co-op the host decides bursts, damage and who is
watching the statue; clients replay the visible effects (`Enemy.VariantPuppetTick`, `Die`). Protocol `pe-coop-19`.

## Blood

`Fx.Blood` sprays droplets and mist in the enemy's blood colour (`Enemy.BloodTint`: dark red for flesh, green bile for the
Appestato, acid green mucus for the Medusa, dust only for the statue and the moths) at every bullet hit, and places a splat on the wall
or floor behind the target along the bullet direction (2.4 m). `Fx.BloodPool` grows a pool under monks and abbots when they
die. Decals reuse the bullet-hole material with their own texture through a property block (no new shader variant), live
40-45 s and fade over the last 6 s; 72 at most, recycled.

## Verification

`PryingEyes.exe -batchmode -autotest -nofire -noenemies -variantitest [-variantishots] -seed 1000 -floor 1`
(`Scripts/VariantiTest.cs`, 24 checks, about 25 s): burst near the player, damage to player and neighbour, cloud; burst on
a distant kill with the player unharmed and the kill paid; the statue holds still while watched, closes in when the player
turns, stops again, takes 40% damage; the flagellant leaps; the cardinal is the boss, lights the bar, enrages, pays 1000 and
drops ammo, and the bar fades. Photos go to `unity_logs/varianti` (launch the player from the repository root: the tests
write relative paths).
