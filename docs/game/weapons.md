# Weapons: 28 models in 5 power grades (Unity)

This page started as the English version of the section "Armi: 28 modelli in 5 gradi di potenza" of the former Italian README (`git show b77eca5:README.md`). It was checked against `Resources/Production/weapons.json`, `Scripts/Armi/`, `Scripts/Weapon.cs` and `Editor/` on 2026-11-15 (static checks only, nothing was run). Items marked "(not verified)" were not checked against the code. The Tower these weapons are used in is in [tower.md](tower.md).

The gunplay was rebuilt from scratch (`Scripts/Weapon.cs`, `Scripts/Armi/`). The four primitive weapons are gone: every weapon is a real 3D model built with a Blender script (hard-surface with bevels, Picatinny rails, M-LOK slots, real sights), with a PBR texture baked by Blender (colour, metallic/smoothness/occlusion, normals: wear on the edges, scratches, dirt in the cavities, stippling on the polymer, wood grain). 25 weapons are "real" and 3 are special (sacred, organic, alien), with bone, flesh and amber emissive materials.

## Controls

**LMB** fire, **RMB** aim (ADS), **R** reload, **B** change fire mode (auto/semi/burst where available), **1 / 2 / X / mouse wheel** change weapon (two slots), **Q / E** lean, **Shift** sprint (with a strong optic, zoom 3.9x or more, Shift holds your breath), **F** buys (at a wall) or uses an interactive object.

Also in the code, not listed above: **I** inspects the weapon (only at rest), **G** melee knife, **C** or **Left Ctrl** crouch, **Tab** scoreboard.

## Grades

I = Ordinary (grey), II = Choice (green), III = Rare (blue), IV = Epic (purple), V = Relic (gold).

Three weapons per floor appear on the walls: the most modest one near the arrival, the best one far away, and the maximum grade rises with the floors (not verified). With both slots full, a purchase replaces the weapon in hand. Buying a weapon you already own refills its reserve at half price, with a minimum of 150 points (`WallBuy.AmmoPrice`). Weapons flagged `cityOnly` are excluded from the tower's wall pool (`FloorDirector.cs`).

## Weapon table

`N x D` means N pellets of D damage each. "Pierces" means the bullet goes through enemies (the number is how many more, where the code gives one).

| Gr. | Weapon | Class | Damage | Fire | Magazine | Sight |
|---|---|---|---|---|---|---|
| I | PIETA' P-9 (starting weapon) | pistol | 25 | semi | 15 | three-dot iron sights |
| I | VIGILIA .45 | pistol | 38 | semi | 8 | fixed iron sights |
| I | TARANTOLA | micro-SMG | 13 | auto 1150 | 32 | peep aperture |
| II | CARDINALE .357 | revolver | 70, pierces | semi | 6 (cylinder) | notch and ramp |
| II | SERAFINO-18 | machine pistol | 16 | auto 1000 | 20 | open red dot |
| II | VESPER-9 | SMG | 20 | auto 850 | 40 | red dot |
| II | CIMBALO-45 | SMG | 29 | auto/semi 620 | 25 | holographic |
| II | TRINITA' | burst rifle | 36 | 3-round burst | 30 | carry handle with peep |
| II | AR-9 MALLEUS | assault rifle | 30 | auto/semi 700 | 30 | red dot |
| II | PREDICATORE | lever-action carbine | 66, pierces 1 | lever | 8 (tube) | horn notch |
| II | ROSARIO | bolt-action rifle | 118 | bolt | 5 | peep aperture |
| II | CAPPELLANO | double-barrel shotgun | 10 x 14 | 2 barrels | 2 | bead sight |
| II | BREACHER-12 | pump shotgun | 8 x 15 | pump | 8 (tube) | ghost ring |
| III | BORDONE | PDW (bullpup) | 23 | auto 900 | 50 (top) | ring |
| III | ARCANGELO-47 | assault rifle | 42 | auto/semi 600 | 30 | tangent iron sights |
| III | ORIFIAMMA | bullpup | 32 | auto/semi 680 | 30 | 1.5x optic |
| III | LANCE-7 | precision rifle | 72 | semi | 12 | 4x scope |
| III | MARTELLO-12 | semi-automatic shotgun | 8 x 13 | semi | 8 (box) | red dot |
| III | SALTERIO | drum machine gun | 36 | auto 640 | 75 | iron sights |
| III | TURIBOLO (special) | sacred flamethrower | 9 per tick, pierces 4 | auto 1100, range 11 m | 120 (tank) | iron sights |
| IV | SPORA (special) | alien spine launcher | 7 x 21 spines | semi 140 | 6 | iron sights |
| IV | EREMITA .50 | magnum pistol | 125, pierces 2 | semi | 7 | adjustable iron sights |
| IV | GLORIA | battle rifle | 55 | auto/semi 520 | 20 | 4x ACOG |
| IV | SENTINELLA | sniper rifle | 190, pierces 3 | bolt | 5 | 8x scope |
| IV | APOCALISSE-12 | automatic drum shotgun | 8 x 11 | auto 300 | 20 | holographic |
| IV | CAMPANA-249 | light machine gun | 40 | auto 780 | 100 (belt) | 3x ACOG |
| V | ALTARE-50 | anti-materiel rifle | 420, pierces 5 | semi | 5 | 10x scope |
| V | PUPILLA (special) | eye-rifle with beam | 90, pierces everything | semi 210 | 14 | amber ring |

Fire rates are rounds per minute.

The catalog also holds two city-only weapons that are not among the 28: RONDA-9 (grade I, urban pistol, 31 damage, 17 rounds) and ARGINE .45 (grade II, heavy urban pistol, 58 damage, 10 rounds).

## Special weapons

The TURIBOLO is a censer-flamethrower: the chain swings, and the fire is a jet of additive quads that pierces enemies lined up in front of it and has a short range. The SPORA is a launcher made of bone and flesh that fires seven green needles in a fan. The PUPILLA is a rifle with an eye in place of the muzzle, which emits a continuous amber beam that pierces everything.

There are no cartridge casings. The flame, beam and spine sounds are synthetic (`SoundGen.Special`).

## Per-weapon numbers

Each weapon has its own numbers (`Scripts/Armi/WeaponDefs.cs`, one `WeaponDef` per weapon, stored in `weapons.json`): damage falloff with distance, headshot multiplier, enemies pierced, spread standing still and aiming, how much it grows with each shot, while running, and airborne, vertical and horizontal recoil and how fast the view recovers, time to aim and to draw, walking and aiming speed, reload time with a full or an empty magazine, and a reload type: magazine, pistol with the slide locked back, tube reloaded round by round (interruptible by firing: not verified; with the realistic hold the left hand does every reload, see [arms-and-magic.md](arms-and-magic.md), and aiming waits for it to finish), revolver with the cylinder opening, double barrel that breaks open, or belt.

Crouch and airborne spread are not per weapon: `Weapon.cs` (`CurrentSpread`) applies the same factors to every weapon (x0.72 crouched, x2.6 airborne). Running is per weapon, through `moveSpread`.

## Gunplay

Recoil really moves the view: it rises a little during bursts and is softer when aiming and crouched, then recovers by itself. Pulling the mouse down compensates for it (not verified). Spread is a cone in degrees: standing still while aiming it is minimal, and the first shot after a pause is accurate. Walking, running or being airborne widens it. The on-screen crosshair opens accordingly and disappears when aiming (the spread formula is verified; the crosshair behaviour is not).

The model has inertia and lags slightly behind the mouse. It pulls back and raises itself in front of a wall. The carrier, bolt, pump, lever and hammer move, and casings eject and bounce. Each shot leaves a bright trace, sparks, dust and a hole in the wall (not verified).

Each class has its own shot sound: pistol, .45, revolver, magnum, SMG, rifle, AK, battle rifle, lever, sniper, anti-materiel, shotgun and machine gun (the sound sets exist in `SoundGen.cs`; rifle is the default). The manoeuvres have their own sounds (not verified).

## Sights

Real sights: iron sights (notch and bead, peep aperture, ghost ring, horn notch, ears), tube red dot and open red dot, holographic with circle and dashes, PDW ring. The red-dot reticle is *collimated*: it is drawn according to the direction of the gaze relative to the optic's axis, so it has no parallax, slides if the eye leaves the axis, and cannot be seen from the front (not verified). Magnified optics (ACOG, scopes) show in the lens the image of a second camera at the real magnification, with an engraved reticle (duplex cross with dots, or a luminous chevron), the shadow of the scope when the eye is off-axis, and the shooter's breathing. Shift holds the breath, and the breathing only applies to optics of 3.9x or more (`Weapon.cs`).

## Pipeline

Run from `modelli/armi/` with Blender 4.5 (headless) and the system Python with numpy and PIL:

```powershell
blender -b -P genera_armi.py -- render <names> rapido lato   # previews in render/<name>_<view>.png
python sheet_pil.py lato <names>                              # contact sheet render/foglio_lato.png
blender -b -P genera_armi.py -- bake [names]                  # UV, PBR bake in tex/raw/, FBX in export/ (~25 s per weapon: not verified)
python pack_tex.py [names]                                    # <name>_base.jpg, <name>_mask.png, <name>_normal.jpg in tex/
```

Import into Unity (the editor executable, project in `unity/PryingEyes`):

```powershell
Unity.exe -batchmode -projectPath unity/PryingEyes -executeMethod PryingEyes.EditorTools.ArmiImport.Run [-armi id1,id2] -quit
Unity.exe -batchmode -projectPath unity/PryingEyes -executeMethod PryingEyes.EditorTools.ArmiShots.Run -armi p9_pieta [-viste mano,mira,lato] -quit
```

Hands, from `modelli/hotel/` (the scripts are not in `modelli/armi/`):

```powershell
blender -b -P genera_braccia.py -- prova [giocatore,destra,sinistra,sotto,mano,gsx,gdx,manos] <id>   # previews in braccia/render/
blender -b -P genera_braccia.py -- build [id ...]                                                    # braccia/<id>.fbx
```

Then copy the FBX files into `unity/PryingEyes/Assets/_PryingEyes/Models/Mani/armi/` (manual step). Import the hands and photograph the views in one Unity session:

```powershell
Unity.exe -batchmode -projectPath unity/PryingEyes -executeMethod PryingEyes.EditorTools.ManiImport.RunAndShots [-mani id1,id2] [-armi id1,id2] [-viste mano,mira,ricarica] -quit
```

`-mani` limits which hand pairs are imported. `-armi` and `-viste` only limit the photos (`ArmiShots`), which go to `hotel_build/armi`. The build does not run the player.

Game tests, with the built player `PryingEyes.exe`:

```powershell
PryingEyes.exe -batchmode -guntest [-armi p9_pieta,ar9_malleus]     # photos and numbers in hotel_build/armi_gioco (guntest.txt: measured rate, max recoil, spread)
PryingEyes.exe -batchmode -guntest -fxtest -armi tu_turibolo        # plus isolated effect photos: fxtest0-2.png
python sheet_game.py hotel_build/armi lato 4                         # contact sheet (jpg) of one view: mano, mira, lato, sparo, ricarica
```

`sheet_game.py` resolves relative folders from the repository root, and writes `foglio_<view>.jpg` in that folder.

## Verification notes

Differences from the former Italian README, found by checking the code:

- SPORA: the damage and pellets were written as "21 x 7". The code has 7 pellets of 21 damage, so it is now "7 x 21".
- APOCALISSE-12: the class is an automatic drum shotgun (code class `FUCILE AUTOMATICO A TAMBURO`, 8 pellets, shell casings), not a generic drum rifle.
- TARANTOLA: the code's `sight` field is `iron`, while the model has a peep rear sight (`armi_mitra.py`). The table keeps the former README's "peep aperture", which describes the model.
- Crouch and airborne spread factors are shared constants in `Weapon.cs`, not per-weapon fields in `WeaponDefs.cs`.
- Rebuy price is half of the price with a minimum of 150 points, not simply half.
- Shift breath hold applies to optics of 3.9x or more, so the ACOG 3x and the 1.5x optic do not have it.
- The hands scripts are `genera_braccia.py` and `genera_mani2.py`, both in `modelli/hotel/`.
- The Unity commands need `-batchmode -projectPath unity/PryingEyes ... -quit` (as in the script headers).
- `ManiImport.RunAndShots`: `-armi` does not select what is imported; `-mani` does.
- Prefabs are `Resources/Mani.prefab` (a single file) and `Resources/ManiArmi/<id>.prefab`.
- `genera_braccia.py prova` also accepts `mano`, `manos` and `sinistra`, which the usage header omits.
- Pack outputs are `<name>_base.jpg`, `<name>_mask.png` and `<name>_normal.jpg`.

Not verified:

- The bake time of about 25 s per weapon.
- The wall rules (three weapons per floor, grade rising with the floors) and the replacement of the weapon in hand.
- That the hands preview runs "on CPU" (`genera_braccia.py` sets no render device; `genera_mani2.py` sets CYCLES).
- Recoil feel, inertia, wall pull-back, animations, traces, sparks, casings and the crosshair behaviour.
- The reticle collimation and parallax, and the picture-in-picture for optics above 1.4x (the code has only a comment for the latter).
- Interrupting a tube reload by firing.
- Which generator produces `Models/Mani/mani.fbx`: `genera_mani.py` writes `mani/mani.fbx`, `genera_mani2.py` writes `mani2/mani.fbx`, and the copy step is manual.
- The location of `PryingEyes.exe` after the build.

`ArmiShots.cs` and the other scripts under `Assets/_PryingEyes/Editor` write into the `hotel_build/` of the checkout the editor runs in, found through `TestPaths` like the test hooks of the player (see [../engineering/testing.md](../engineering/testing.md)); until 0.7.0 they wrote to an absolute path of the maintainer's machine. The Blender and texture scripts under `modelli/` find the Unity folders from their own location.
