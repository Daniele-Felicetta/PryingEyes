# Guardaroba: the Count's wardrobe

Added on 16 November 2026. The fourth theme of the Tower after the hotel, the theatre and the sanatorium: wardrobes, vampires,
scissors and lava, with open-air terraces over a lake of lava. It started from a request for new assets, and the asset set is the part
that was asked for first, so it comes first here: 17 hand-modelled props (`modelli/hotel/genera_arredi4.py`). The theme's level
code is summarised at the end. **The theme is off by default** (no floor is a Guardaroba unless you ask, see below), so the game plays as before;
the props and the code are in the repository for whoever turns it on. New enemies and new weapons are not done.

## The 17 props

Every prop has the front on +Z and the origin at the centre of its base, like the other kit furniture. The size is the footprint and
the box collider the generator uses (`PropDefs`, measured from the FBX bounds), not always the full visual extent: see the note under the table.

| Prop | What it is | Size w x h x d (m) | Triangles | Where the rooms put it |
| --- | --- | --- | --- | --- |
| `gu_armadio` | The Count's wardrobe: dark walnut, a crown of blind arcades and pinnacles, two corner columns, two framed doors with a raised Gothic arch (brass hinges, bar handle), a drawer with rings. The doors are groups (`anta_sx`, `anta_dx`) with the origin on the hinge. | 1.50 x 2.67 x 0.76 | 6,228 | Wardrobe hall and changing room walls, cover |
| `gu_armadio_aperto` | The same, doors thrown open on black cloaks hanging from the rail. | 1.50 x 2.67 x 0.76 | 9,952 | One wall slot in five (changing room: one in seven), cover |
| `gu_forbici` | Tailor's scissors two and a half metres tall, standing on a stepped octagonal stone plinth with a brass bat: steel wedge blades slightly open, gold rings and shanks. | 0.90 x 2.50 x 0.90 | 4,276 | A free corner of the tailor's shop and of the forge |
| `gu_bara` | A vampire's coffin on a stone slab: hexagonal walnut case lined with purple silk, a pillow, brass handles, the stepped lid (group `coperchio`) slid aside. | 2.34 x 1.02 x 0.96 | 3,612 | Crypt rows, crypt arena benches, cover |
| `gu_bara_chiusa` | The same with the lid in place and two iron straps with a padlock. | 2.34 x 1.07 x 0.96 | 4,272 | Two crypt slots in five, cover |
| `gu_tavolo_sarto` | Cutting table: walnut top on four turned legs, a lower shelf of folded cloth, a red sheet with the pattern pinned on it, scissors, pincushion, three spools, a rolled tape, a black cloth hanging over the front edge. | 2.30 x 1.00 x 1.15 | 6,066 | Two rows in the tailor's shop, cover |
| `gu_macchina_cucire` | Treadle sewing machine: cast-iron frames with turned columns, walnut top, pedal, spoked wheel with connecting rod and belt, a black lacquered head with gold lines, a purple silk spool. | 0.95 x 1.14 x 0.48 | 3,844 | One to three against the walls of the tailor's shop |
| `gu_stoffe` | Fabric shelf: two sides, a back and five walnut shelves holding rolls side by side (red, purple, black, cream, velvet) with the cardboard cores sticking out, and a few piles of folded cloth. | 1.25 x 1.94 x 0.49 | 2,964 | Tailor's shop walls (65% of slots), cover |
| `gu_attaccapanni` | Coat stand: turned pole on three scrolled feet, six brass arms at two heights, three hanging cloaks (two black, one red) and a polished top hat. | 0.66 x 1.90 x 0.66 | 4,544 | One or two in the wardrobe hall and the changing room, cover |
| `gu_braciere` | Fire basket: three wrought-iron claw legs with a brace ring, an iron bowl with a rolled rim and two ring handles, a heap of glowing embers and seven flame tongues. | 0.76 x 1.05 x 0.70 | 2,992 | Lit fire points of the terrace (65%) and one to three in the forge |
| `gu_incudine` | Anvil on its block: a log with three iron hoops, an iron anvil with horn, heel and foot, a steel face, a red-hot iron on the face and a hammer on the block. | 1.04 x 0.83 x 0.60 | 1,262 | The forge (area / 60 of them), cover |
| `gu_crogiolo` | Pit forge: a stone hearth with embers and flames, two iron posts with a crossbar, a chain and an iron crucible full of molten metal running from the spout; a gold bat on the front of the hearth. | 1.66 x 1.95 x 0.91 | 3,292 | One in the forge, with its own pulsing light |
| `gu_gargoyle` | Guard gargoyle crouched on a plinth: spiked back, festooned bat wings, horns, ears, fangs, ember eyes, clawed hands on the front edge and the tail curled to one side. | 1.00 x 1.81 x 0.94 | 4,320 | Row at the far end of the crypt, terrace cover, cover |
| `gu_albero_secco` | The terrace's dead tree: a twisted trunk with roots coming out of the soil and branches forking three times, no leaves, in a low stone planter. | 1.56 x 3.14 x 1.56 | 4,428 | Terrace cover |
| `gu_specchio` | The vampire's mirror: a stepped pointed-arch gold frame between two turned posts with sled feet, black glass that reflects nothing and a gold bat on top. | 1.13 x 2.05 x 0.56 | 1,168 | Far end of the tailor's shop and the changing room (70%) |
| `gu_lampione` | Terrace street lamp: stone plinth, iron shaft with collars, four-pane lantern with a flame, pyramid roof and an iron bat as the weathervane. | 0.50 x 2.97 x 0.50 | 1,250 | Lit fire points of the terrace (35%) |
| `gu_urna` | Stone urn on a plinth: pod-shaped belly with two ring handles, a brass bat medallion on the front and dry soil with six bare twigs. | 0.60 x 1.42 x 0.58 | 1,340 | Crypt (one to three), terrace cover |

Total: 65,810 triangles for the 17 (3,900 on average; the wardrobe is the heaviest at 6,228 and 9,952 open). What they cost in the
player was not measured (not verified).

The collider is the body: the open doors of `gu_armadio_aperto` stretch the visual extent to 1.66 x 1.36 m, the slid lid of `gu_bara` to
2.84 m, the wings of `gu_gargoyle` to 1.21 x 1.10 m. Nothing is solid out there (a player walks through an open door or a wing).

## Materials

The generator gives each mesh a **role** (`A_*`), the importer swaps it for a kit material by name (`TorreBuilder.FillKit`, the `roles`
dictionary). The props reuse the existing roles (walnut `A_legno`, `A_scuro`, `A_ferro`, `A_acciaio`, `A_oro`, `A_ottone`, `A_lacca`,
`A_terra`, `A_paralume`, `A_fiamma`) and add five:

| Role | Kit material | Used for |
| --- | --- | --- |
| `A_basalto` | `kit_basalto` (stone texture, cold grey-blue tint) | Plinths, hearth, planters, gargoyle, urn, coffin slabs |
| `A_seta` | `kit_seta` (velvet, purple) | Coffin lining, purple rolls and spools |
| `A_panno` | `kit_panno` (velvet, near black) | Cloaks and coats, the cloth over the cutting table, black rolls |
| `A_brace` | `kit_brace` (emissive, HDR 2.6 / 0.62 / 0.08) | Embers, the hot iron, the molten metal, the gargoyle's eyes |
| `A_fuoco` | `fiamma_grande` (emissive, HDR 2.4 / 0.5 / 0.05) | The flames of the fire basket and of the pit forge |

The candle flame (`A_fiamma`, material `fiamma`) is yellow-white; the big fires are orange. The name of the big flame starts with
`fiamma` on purpose: `RoomKits.Candle` finds the flame meshes by that prefix, so a fire basket or a street lamp placed with `Candle` has its flame and its
light switched together, like a candelabrum.

## Making them again

Blender 4.5, from `modelli/hotel/`. The script takes the verb(s) and the names to do (none: all 17); previews are CPU renders.

```powershell
blender -b -P genera_arredi4.py -- render [names]     # modelli/hotel/arredi4/render/<name>.png (tracked, like the other sets)
blender -b -P genera_arredi4.py -- build [names]      # modelli/hotel/arredi4/<name>.fbx (git-ignored), to be copied by hand
Copy-Item modelli\hotel\arredi4\gu_*.fbx unity\PryingEyes\Assets\_PryingEyes\Models\Arredi\   # the copy that is tracked
```

Then Unity (`TorreBuilder.FillKit` imports the FBX, builds the prefabs in `Prefabs/Props` and the materials in `Materials/Kit`, and runs
before every Build, `PropShots`, `ProcShots` and `ProcCheck`). To see the props as the player sees them, without playing:

```powershell
Unity -batchmode -projectPath unity\PryingEyes -executeMethod PryingEyes.EditorTools.PropShots.Run -quit -logFile unity_logs\propshots.log -tag v1 -torch [-props gu_braciere,gu_crogiolo] [-prefix gu_] [-theme guardaroba] [-dist 1.0]
```

Each prop sits in a small dark room with the theme's fog, ambient light and grading, a warm lamp and, with `-torch`, the cone of the player's
torch. Output: `hotel_build/props/<name>_3q<tag>.png` and `<name>_fronte<tag>.png` (derived, not committed). The Blender previews are
for modelling only: they use a different tone mapper and make the flames look pale; the Unity photos are the reference.

## How the rooms use them

`Scripts/Proc/RoomKitsGuardaroba.cs` chooses the pieces by name. Ceiling heights come from `PlanThemes.cs`.

| Room | Height | Pieces |
| --- | --- | --- |
| Wardrobe hall (`SalaArmadi`) | 4.8 m, beams | Four walls of `gu_armadio` (one slot in five `gu_armadio_aperto`), crown chandeliers, candelabra, mannequins, one or two `gu_attaccapanni` |
| Tailor's shop (`Sartoria`) | 3.8 m | `gu_stoffe` (65%) or `arr_scaffale` on the long walls, two rows of `gu_tavolo_sarto` with mannequins and bare bulbs, one to three `gu_macchina_cucire`, `gu_specchio` (70%) or `tea_toeletta` at the far end, `gu_forbici` in a corner |
| Changing room (`Spogliatoio`) | 3.4 m | `gu_armadio` on the long walls, benches, mirrors, mannequins, `gu_attaccapanni`, one chandelier |
| Crypt (`Sepolcreto`) | 3.6 m, barrel vault | Two rows of `gu_bara` (two in five `gu_bara_chiusa`), candelabra at the heads, up to four `gu_gargoyle` at the far end, one to three `gu_urna`, red candles |
| Forge (`Forgia`) | 4.2 m, beams | Furnaces with a pulsing lava light, lava pools, one `gu_crogiolo` with its own pulsing light, `gu_incudine`, one to three lit `gu_braciere`, carts, `gu_forbici` in a corner |
| Terrace (`Terrazza`) | 6 m, open air | Benches along the parapet, lit `gu_braciere` and `gu_lampione`, cover: `gu_gargoyle`, bench, `gu_urna`, `gu_albero_secco`, statue |

Arenas and guard rooms get cover from `CoverGuardaroba` (`RoomKitsCover.cs`): wardrobes in barricades of up to three, open wardrobes,
mannequins, coat stands, fabric shelves, coffins, gargoyles, cutting tables, anvils, benches and carts. The crypt's arena benches are
`gu_bara`.

## The level code

Written before the assets, summarised from the sources (it is not the subject of this page):

- Theme: `Theme.Guardaroba` and six room kinds (`PlanTypes.cs`, appended at the end of the enums: the theme value travels in the co-op
  signal). A second draw, independent of the first, makes a floor from the third up a Guardaroba with probability
  `PlanGen.GuardarobaPercent` per hundred (`PlanThemes.cs`); **the default is 0**, and with 0 the plans of every seed are the ones of
  before (checked, see below). Launch flags (`ThemeFlag.cs`, read by the player and by the editor tools): `-guardaroba N` sets the
  percentage (in co-op give the same N on every machine: each one builds the floor from the seed, and a client whose plan digest differs from the
  host's refuses the floor with "Procedural plan mismatch", `FishNetWorldState.cs`), `-theme guardaroba` forces the theme of every grid floor. Room roles and their candidate kinds are in `PlanGrid.cs`, which also forces one or two terraces per floor.
- Open-air rooms (`CeilKind.Open`): no ceiling, a parapet 1.55 m high with merlons over the void (`FloorBuilder.Open.cs`).
- Outside: a night sky with a red moon (`NightSky.shader`, `GuardarobaKit.cs`) and, 17 m below the floor, a lake of lava
  (`FloorBuilder.Guardaroba.cs`); whoever falls into the void reaches the lava and is brought back up by the existing `FallGuard` at -20 m.
- Lava pools in the forge (`LavaPool.cs`): a stone rim 0.55 m high, a glowing surface, damage to whoever stands in it.
- The look of the theme (fog, ambient light, grading) is in `FloorDirector.Look.cs`.

## Traps

- **Emission turned itself off.** In this Unity version a material's `_EMISSION` keyword is derived from its global-illumination flags
  when it is saved or imported: a material saved with `MaterialGlobalIlluminationFlags.None` loses the keyword and the flames, neon and
  windows go dark. `TorreBuilder.Emit` and `WindowGlass` now use `BakedEmissive` (the game does no baked GI). The committed emissive
  materials (`fiamma`, `kit_neon`, `kit_uscita`, `kit_vetro_buio`, `vetrata0` and `vetrata_chiara`, among others) had flag 0 and change to 2.
- **Blender arguments must be separate.** The script compares each argument after `--` with the verbs and the names. A wrapper that hands
  over `build,render,gu_x` as one comma-joined argument makes it neither build nor render, and it prints nothing, so the previous FBX
  stay in place and look like a fresh build. Pass the verbs and names as separate arguments and look for the `[build] <name>` lines in the log.
- **A new role exists in two places**: `ga.ROLE` in `genera_arredi4.py` (the Blender look) and the `roles` dictionary in `TorreBuilder.cs`
  (the kit material). A role missing from the second gets the importer's default.
- **`FillKit` rewrites every prop prefab and kit material** each time it runs and Unity saves them again, so dozens of existing prefabs and
  materials show as modified after any Unity run (derived noise, see `AGENTS.md` rule 4). Stage only the new `gu_*` prefabs, FBX and `.meta`
  files, the five new kit materials and the emissive materials whose flag really changed.
- **The tracked FBX are the ones in `Models/Arredi`**; `modelli/hotel/arredi4/*.fbx` is git-ignored like the other sets. After a Blender `build`, copy them.

## Verification

- All 17 FBX were imported and photographed in the theme look with the torch (`PropShots`, 34 photos), and the three props with fire
  again after the last change of the flame colour.
- `ProcCheck -seeds 24 -seed0 3000 -theme guardaroba -survivallayout -propstats` with the new props: 24 floors, 180 rooms, 0 floors with
  problems, 0 unreachable rooms, 0 unreachable candles, exits and lifts fine; average build 81 ms and navmesh 154 ms per floor.
  The statistics of the 24 floors place 1,792 wardrobes, 514 open coffins, 307 gargoyles, 416 cutting tables and 17 crucibles.
- One floor of seed 3068 (floor 5) photographed room by room with the real look (`ProcShots -look`).
- With the theme drawn by chance (`-guardaroba 25`) `ProcCheck` over 48 floors gives 12 Guardaroba floors (Albergo 22, Teatro 10, Sanatorio 4) and again
  0 floors with problems and 0 unreachable rooms; with the default (off), 24 floors, 0 problems.
- **With the theme off nothing changes.** The plan generator was built from the commit before the theme (`3938be9`) and from this one and compared: the
  output of `grid`, `survival`, `stats`, `regions` and `annex` over 2,000 plans and the `dump` of 48 plans (12 seeds, floors 1, 3, 5, 8) are identical (only the
  timing line differs); with `guardaroba=25` the same comparison does differ, so it can tell. The changes to `FloorBuilder`, `RoomKits`
  and `FloorDirector.Look` are all behind `theme == Theme.Guardaroba`, a new `RoomKind` or `CeilKind.Open`. Commands in [../engineering/testing.md](../engineering/testing.md) (2.4).

Not verified: frame time in the player with the new props (only editor photos and the editor connectivity check), the player build,
any playtest, co-op with the theme on (every machine needs the same `-guardaroba N`; a mismatch is refused by the digest check, not tried),
the light balance of the fire baskets in the player. Enemies, weapons and sounds of the theme are not done.
