# Reflections, scoreboard, inspection and game feel

Added on 15 November 2026.

- **Room reflections** (`Scripts/Proc/RoomProbes.cs`): one box-projected reflection probe per room, sized to the room from
  floor to ceiling, rendered once right after the floor is built (one per frame, with the room's lights forced on through
  `LightPool.HoldPoint`) and then left static. Glossy surfaces (checkered marble, brass, glass, lacquered wood) now reflect
  their own room instead of the bright fallback environment, which flattened the marble halls. URP Forward+ probe blending,
  box projection and the probe atlas were already enabled in `HotelURP`. 128 px, 256 px for rooms over 400 m². Cost measured
  with the frame probe at 1080p on seed 2995 floor 3: 4.8 ms with and without probes; baking 9 probes takes about 120 ms
  during the arrival. `-noprobes` disables them for comparisons.
- **Scoreboard** (`Scripts/HUD.Score.cs`): hold Tab for wave and floor, points, kills and headshot share, accuracy, trials
  completed, deaths, time, throwables and, in co-op, the number of players. New counters `GameManager.headshots` and
  `GameManager.deaths`.
- **Weapon inspection** (`Weapon.Inspect`, key I): 2.6 s, the weapon rises and turns to show its left side with a slow
  sway, then returns; firing, aiming, reloading, switching or the knife interrupt it.
- **Explosion shake** (`PlayerController.Shake`): flasks, carillons, Appestato bursts and the Cardinal's fire ring kick the
  view and the field of view, stronger when closer (scaled by the motion comfort setting).
- **Corpses** (`Scripts/CorpseSink.cs`): monks and abbots finish their fall, then sink into the floor over 1.4 s with a puff of
  dust before being removed, instead of vanishing.

Verification: `PryingEyes.exe -batchmode -autotest -nofire -noenemies -feeltest [-feelshots] -seed 1000 -floor 1`
(`Scripts/FeelTest.cs`, 11 checks: probes rendered for every room and the light pool released, scoreboard opens with the
right numbers and closes, inspection runs and ends, a killed monk sinks and disappears). Photos in `unity_logs/rifiniture`.
