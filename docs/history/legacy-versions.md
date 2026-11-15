# Earlier versions: Three.js prototype, Unreal Engine 5 prototype and the first Unity hall

These are not the main game. The main game is the Unity project in `unity/PryingEyes` (see [README.md](../../README.md) and [../game/tower.md](../game/tower.md)). The Three.js and Unreal Engine 5 prototypes are kept in the repository for reference only: they are not maintained, and their descriptions may no longer match the code or the editor state. The first Unity version, a single hall, was replaced by the Tower; its section at the end records what it introduced.

## Three.js prototype

First-person FPS draft. Models, audio and effects are procedural; PBR photo textures are loaded from a CDN, with a procedural fallback when offline.

### Running

ES Modules require HTTP (not `file://`). From the repository root:

```powershell
python -m http.server 8000
# open http://localhost:8000
```

or with Node:

```powershell
npx serve .
```

Then click **ENTRA NELLA SIMULAZIONE** (ENTER THE SIMULATION). The click is needed for Pointer Lock and the AudioContext.

### Controls

| Input | Action |
|---|---|
| WASD | movement |
| Mouse | aim |
| LMB | fire (auto, 700 RPM) |
| RMB | ADS aim (zoom + laser) |
| R | reload |
| Shift | sprint (only while moving forward) |
| C / Ctrl | crouch |
| Sprint + C | **slide** |
| Space | jump (with coyote time and jump buffer) |
| Q / E | lean |

### What it includes

**Movement (top priority):**
- Quake-style acceleration and friction (ground accel 34, air control, lateral friction)
- sprint with FOV kick, slide with momentum conservation, camera roll on strafe, Q/E lean
- procedural head-bob and footstep audio, coyote time 0.13 s, jump buffer 0.15 s
- AABB collision against crates, walls and pillars, eye height interpolated while crouching

**Materials (real photo PBR + procedural fallback):**
- photographic brick walls (albedo, bump, roughness, normal derived via Sobel, scale about 0.22 m per course)
- photographic wood loading bridge, puddles with animated water normals
- cement v2 (joints, cracks, oil, tyre marks, plus normal map), military crates with NX-07 stencil
- asynchronous CDN loading, automatic fallback when offline (toast "Texture HD attive" when ready)
- IBL + ACES + bloom + colour grading + SMAA

**Photorealistic weapon (AR-9 MALLEUS, BO2 style):** worn PBR metal (albedo, metalness, roughness, normal from scratches and oil), bevelled edges, stamped markings (calibre, selector, serial), bolt that cycles on every shot, digital ammo counter, star flash hider, fingerless gloves and watch on the hands, a dedicated light for the viewmodel, lowered running pose.

**AAA enemies:** WASP drone (blurred rotors, gimbal sensor that follows you, banking, smoke when damaged) and RAVAGER brute (articulated plates, pistons, enrage below 50% health with the shoulder pad lost). Burn marks anchored to the body, kills with physical dismemberment.

**Map: GRAND HOTEL CONTINENTAL, abandoned**
- 70 x 46 m hall: marble checkerboard floor, peeling wallpaper, worn rugs, central chandelier broken and flickering, moonlight through the skylight, stained-glass windows with moonlight blades, cobwebs and rubble
- reception with key board, lounge with sofas (some under sheets), luggage trolley, lifts, central staircase and walkable mezzanine (steps with automatic step-up)
- **crocodile glass enclosure in the upper-left (NW) corner**: basin, islet, heat lamp, sign, barriers, with 2 animated crocodiles (they breathe, move their tails, snap their jaws, and watch you if you approach)

**Gameplay:**
- 2 enemies (flying drone and bipedal brute) spawning from lifts, entrance and mezzanine, with separation, knockback, flash-hit and dismemberment
- headshot x2, escalating waves, score, shield that regenerates, HP, respawn
- HUD: dynamic crosshair, hitmarker, kill confirm, minimap, FPS counter, wave banner
- menu with sensitivity / FOV / quality (low, high, ultra) / bloom and shake toggles

### Files

Verified against the repository: all the files listed below exist.

- `index.html`: markup, HUD, menu and importmap (`three@0.160.0` from unpkg; the map also maps `three/addons/` to the unpkg `examples/jsm/` folder)
- `style.css`: HUD and menu styles
- `js/`: game split into ES modules
  - `core.js`: renderer, scene, camera, post (bloom, grading, SMAA), global state (CFG, player, weapon, enemies, input)
  - `audio.js`: procedural WebAudio effects
  - `textures.js`: canvas textures, Sobel normal maps, concrete, crates, walls, CDN photo PBR with fallback
  - `world.js`: materials, lights, sky, arena, lighthouse towers, puddles, volumetrics (`buildWorld` / `tickWorld`)
  - `effects.js`: particles, tracers, decals, shell casings, debris, burn marks, shockwave
  - `weapon.js`: AR-9 viewmodel, red dot, hands, animated reload
  - `combat.js`: hitscan, reload, crosshair kick
  - `player.js`: Quake-style controller, camera, damage, respawn
  - `enemies.js`: WASP and RAVAGER, damage, dismemberment, waves
  - `ui.js`: HUD, banners, toasts, minimap, FPS
  - `input.js`: keyboard and mouse, pointer lock, menu
  - `main.js`: bootstrap and game loop

## Unreal Engine 5 prototype

Project: `PryingEyes 5.8`, map `/Game/Maps/HotelPryingEyes`.

There are two UE project folders in the repository. Only `PryingEyes 5.8` contains the hotel map and is the one this section describes:

| Folder | `EngineAssociation` | Hotel map | Startup map in `Config/DefaultEngine.ini` |
|---|---|---|---|
| `PryingEyes 5.8\` | `5.8` (a numbered engine release) | `Content\Maps\HotelPryingEyes.umap` | `/Game/Maps/HotelPryingEyes` |
| `PryingEyes\` | a GUID (an engine build identified by GUID, not a release number) | not present | `/Game/DemoTemplate/_Core/Lvl_IntroRoom` (the template level) |

Both folders are excluded from git by `.gitignore` (`/PryingEyes/` and `/PryingEyes 5.8/`), so they are local copies only.

The hall is rebuilt in UE5 and driven via MCP (`http://127.0.0.1:8001/mcp`; scripts in `hotel_build/`):
- full shell: floor, walls, columns, mezzanine with railing, 12-step staircase, reception, lifts, chandeliers, east stained glass
- **crocodile glass enclosure in the NW corner**: real translucent glass, basin, islet, heat lamp, rope barriers, 2 crocodiles modelled from primitives
- Lumen GI + hardware ray tracing (enabled in the project), volumetric fog, cinematic PostProcess (bloom, vignette, grain), moonlight blade from the skylight, RectLight at the windows, PlayerStart at the entrance; press Play to walk around (default First Person template)
- checked by editor screenshots: marble checkerboard floor (`M_MarbleFloor`, procedural world-space), striped wallpaper (`M_Wallpaper`), weapon and WASP with readable silhouettes

### Gameplay (playable FPS, `hotel_build/gameplay/` + `phaseG1.ps1`)

Full loop verified in PIE with logs (`LogBlueprintUserMessages`):
- **BP_GameDirector**: waves, spawns, HP and regenerating shield, ammo 30/120 (LMB fire, R reload), protected respawn, score and kills, HUD (HP, shield, ammo, reserve, points, wave, crosshair, hitmarker), banners; movement: Shift sprint, Q/E lean, C crouch, sprint + C slide, head-bob and weapon kick; movement logic in `TickMove`, weapon in `TickGun`
- **BP_Enemy**: flying WASP (animated rotors + bob) / armoured RAVAGER (shoulder pads, red eyes, enrage below 50% HP with +35% speed), chase, melee attack, `TakeHit` + score (100/150)
- **BP_Gun**: viewmodel (body, barrel, sight, red dot, muzzle flash) following the camera with recoil kick; the trace starts beyond the weapon
- **WBP_HotelHUD**: 4 values + labels + crosshair, updated every tick by the Director
- **BP_CrocAnim** (2 instances on crocodiles A/B): sinusoidal jaw motion, phase-shifted
- **BP_Impact**: 0.12 s emissive flash on every impact point (walls and enemies)
- balance and tests: `TestAutoFire`, `TestAutoWalk` and `TestYawRate` on the Director for a full playtest without input (fire, reload, walk, rotation from inside the game, logs `POS` / `ONDATA` / `UCCISO`); enemy sweep disabled (no clipping into walls)
- verified: spawn, chase, attack, fire, kill, score, respawn, wave progression to ONDATA 12, no runtime errors; versioned DSL sources in `hotel_build/gameplay/*.lisp`, replay with `phaseG1.ps1`

## First Unity version: the hall

The first Unity version (Unity 6, `unity/PryingEyes`) was a single first-person hall generated by `HotelBuilder`, with waves of enemies. The hall (`Hotel.unity`, `HotelBuilder`) is still in the project but is no longer in the player build: the Tower replaced it ([../game/tower.md](../game/tower.md)). This is what that version introduced, as the former Italian README recorded it. Numbers and details have changed since; the current ones are in the game pages.

- **Wall-buy weapons in the style of CoD Zombies** (`Scripts/WallBuy.cs`): a metal panel, a chalk silhouette, the 3D weapon, name, price and a coloured light; walk up and press **F** to buy. Buying the same weapon again refills the reserve at half price. That version had four weapons; the current ones are in [../game/weapons.md](../game/weapons.md).
- **Points as currency** (`GameManager`): start with 500; +10 for a hit that does not kill, +60 for a kill, +100 when the killing hit is to the head, +130 with the knife (the Black Ops Zombies table, `Punti.cs`); no per-wave multiplier. Weapons cost points, so kills finance the arsenal.
- **Four weapons with distinct statistics and viewmodels** (`Scripts/Weapon.cs`): fire rate, damage, magazine, reserve, recoil, spread, headshot multiplier, reload; the shotgun fires 8 pellets; the viewmodel was rebuilt on purchase.
- **Dedicated sights**: a red dot on the AR-9, a reflex sight on the VESPER-9, a ghost notch and a bead on the BREACHER-12, a scope with turrets and lenses on the LANCE-7. Aiming (RMB) raised the weapon and centred the sight on the aim point.
- **Procedural PBR materials, nothing downloaded** (`Scripts/GunMats.cs`): worn albedo with scratches and oil stains, metallic and smoothness maps, a normal map generated with a Sobel filter, non-slip polymer and wood grain.
- **Viewmodel animation**: breathing, step sway, a running pose, recoil, the draw on purchase and an animated reload (the weapon turns, the magazine leaves and returns, the charging handle; the shotgun's pump).
- **Ranged monks** (`Scripts/Magic.cs`): with line of sight and the player between 3 and 26 m away, a monk charges a blue sphere on its staff for 0.9 s and casts a magic bolt (8 to 12 damage) at the chest; up close it still strikes with the staff. The bolt has an emissive core, orbiting rings, a light and a trail, and explodes on walls and on the player.
- **Mechanical enemies, grimmer and more photorealistic** (`Scripts/EnemyMats.cs`): procedural PBR materials (rusted iron with scratches and grease for the brutes, black composite with panels and screws for the drones), a procedural normal map, chest plates, pauldrons, pistons, cables, rotor cages, landing skids and dimmed sensor and visor lights.
- **Furniture and weapon shapes**: upholstered sofas (`HotelBuilder.Sofa`: base, seat and back cushions as ellipsoids, rolled arms, a decorative cushion, dark wooden legs) instead of rigid boxes; a less boxy weapon built from ellipsoids and capsules, with a procedural additive star as the muzzle flash instead of an orange square.
- **A slower pace**: walk 5.6 to 3.9 m/s, sprint 8.2 to 5.9, crouch 3.1 to 2.2; slow enemies (drone 2.7 to 3.5, brute 1.6 to 2.05, monk 1.2 to 1.5 m/s), longer attack cooldowns, waves with an 8 s pause and mostly walkers.
- **HUD**: the buy prompt near the crosshair and the weapon name beside the ammo; score and presences at the top left. The reference to the HUD is resolved at run time, so shots, reloads and weapon changes update the ammo counter at once.
- **Tests of that version**: `-autotest -magictest` (ranged darts and damage), `-autotest -enemyshot` (writes `hotel_build/enemies.png`), `-autotest -camshots` (writes `hotel_build/wallbuy.png`, a photo of the display), `-autotest -walltest` (prompt, purchase, points spent, reserve refill).
