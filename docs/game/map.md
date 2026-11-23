# The map

The map of a floor is a hand-inked dossier that fills in as you explore. It was redone on 17 November 2026 to carry more information, to be
drawn in more detail and to add to the dread: what you have not seen is an ink stain with eyes in it, what you have seen is a plan inked by a
shaking hand, with soot creeping in from the walls that face the unknown, and in the margins of the paper somebody who came before you has
left notes. This page says what it shows, how it is built and what is checked.

**Status (17 November 2026).** Compiled and run in the player, on top of the cathedral and catacombs of the same day
([cathedral-and-catacombs.md](cathedral-and-catacombs.md)): `-maptest` passes on the hotel, the theatre and the sanatorium, in the tower and in the
catacombs, and the photos it takes were looked at. Not tried: a real second player in split screen, co-op, and a controller (see the last section).

## What the player sees

| | |
| --- | --- |
| `M` (controller Back) | Opens and closes the big sheet. |
| `N` | Shows or hides the minimap. |

- **Minimap.** Top-right corner, centred on you, north up, about 5.9 px per metre (units of the 1280x720 HUD). It trembles when someone has seen you.
- **Big sheet.** The whole floor fitted to the screen, on a dark opaque board with a brass frame (the world behind it is dimmed to a tenth): the
  floor's epithet as title, the floor and the wave, a tally (rooms seen, open areas, weapons seen, power, kills), a legend of twelve marks, a
  compass and a 10 m scale bar. The paper fills the whole window; the window grows from the minimap to the sheet in 0.36 s, and it is the same
  drawing, only bigger.
- **The notes.** Four lines in a shaky hand on the paper, two on each side of the plan, different on every floor ("NON FIDARTI DELLE LUCI",
  "SONO GIA' PASSATO DI QUI", ...); one is in red. They say nothing useful, which is the point. They show on the big sheet, rarely on the minimap.
- **The sentinel.** A brass eye on the top edge of the frame. It is half closed while nobody looks for you and opens and turns red with `Stealth.Level`.
- Each player has their own map in their own part of the screen (split screen); what is discovered belongs to everyone.
- **Cathedral and catacombs.** The cathedral (the hub) has no map. In the catacombs it is the same map: the subtitle names the level ("L'Ossario ·
  livello III"), the legend calls the exit a gate (`Cancello`), and the Cartographer's Map (`revealMaps`) shows every room of the level from the start.
- **The desert** has no sheet (since 0.10.3 `M`, `Tab` or a tap of Select on the pad opens the Select menu, whose first page is a map of the desert: [the-desert.md](the-desert.md#the-select-menu)) but, since 0.9.1, a minimap of its own in the same corner, which `N` shows and hides: a cartographer's chart of 512 m of sand
  round you, painted by the code (not this map's code): see "Co-op, the minimap and the way home (0.9.1)" in [the-desert.md](the-desert.md).

## What is revealed

| State | When | How it looks |
| --- | --- | --- |
| Hidden | Not next to anything visited. The secret room stays hidden until you enter it. | Nothing on the paper. |
| Rumor | Behind an open door of a visited room (not the lift cabins, not the secret passages). | A ragged ink stain with a `?` and eyes that blink and follow you; they open wider as you approach. |
| Seen | You have been in it (a lift cabin also shows the room it opens on). | Wash in the colour of the room's role, hand-inked walls and doors, the footprints of the furniture (pews, beds, tables, columns, ...), the name in italic and the role in small capitals, soot bleeding out of the walls that face unknown space. |

A room comes up from the paper over 1.2 s after you enter it. Names and prices keep a fixed size on screen at any zoom.

## What it marks

- **Wall weapons** seen from within 8 m in the same room, with their name and price. **Candles**, lit or not. **Library, chest and altar** seen from within 10 m. **The switchboard** (red while the power is off, gold when on) and the power state in the tally.
- **Lifts.** The arrival one from the start; the exit one once seen, with a red cross while it is sealed.
- **Sealed doors** (wave cycle): a wax seal with the eye pressed in it and the price of the area behind it.
- **Blood** where enemies died, only in rooms you have seen (the last 90 stains).
- **You**: a red arrow, the cone of the torch (small and pale blue when it is off), and a heartbeat ring that quickens and reddens with `Stealth.Level`. **Companions** in their colour, with a red pulsing ring when down.
- **Enemies** as eyes, under the same rules as the eye tags of the HUD: only those that are alerted (or have some awareness of you), or every one with the Occhio perk; 44 m, 70 m with the perk.
- **Power-ups on the floor** as stars, and **pings**: the marks of the guide and the sonar ring that leaves you at the speed of the sound wave.

## How it is built

| File (`unity/PryingEyes/Assets/_PryingEyes/Scripts/Proc/Map/` unless noted) | What it does |
| --- | --- |
| `MapMath.cs`, `MapPen.cs`, `MapTextures.cs` | Vectors, colours, meshes and noise; the pen (wobbling ribbons, fills, stamps, hatching); the textures, all generated in code (ink ribbon, wash, stains, a 1024x512 paper). |
| `MapScene.cs` | What to show: the state of each room, the doors, the props, the marks, the blood. |
| `MapPainter.cs`, `.Shade.cs`, `.Props.cs`, `.Glyphs.cs` | Paint a room (seen or rumor), the inner shadow and the soot, the furniture, the eye, the seal, the marks and the notes. |
| `MapGraphic.cs` | The uGUI graphic that draws one of these meshes; `MapAssets` holds the textures. Their pixels are painted on a worker thread from the start of the process (about 0.7 s in the Mono player), so the first HUD only uploads them (4 ms). |
| `FloorMap.cs` | One per floor: what has been discovered, the scene, and a version for each room, for the doors, for the marks and for the blood, so only what changed is painted again. |
| `MapView.cs`, `.Labels.cs`, `.Live.cs`, `.Chrome.cs` | The view of one player: layout and morph, the text labels, the layer that moves every frame (you, companions, eyes, ping), the frame, legend and sentinel. |
| `Proc/HUD.Map.cs`, `Proc/FloorDirector.cs` | The HUD makes the view; `FloorDirector` answers `MapOpen(seat)` and `MiniOn(seat)` and records what each player discovers. |

The first group of files (`MapMath`, `MapPen`, `MapTextures`, `MapScene`, `MapPainter*`) has no `UnityEngine`: the game and `tools/planlab`
compile the same files, and it is why the textures can be painted on a worker thread (the noise is a pure function of its arguments). The view
is uGUI, not IMGUI, so the checks can photograph it with the HUD and it follows the scale of a split screen. The art is three nested canvases
(the static ink and wash, the labels, the live layer) so that only the live one is rebuilt every frame.

The map is an extra: if its view throws, `HUD.UpdateMapUI` logs the exception and lets the rest of the HUD carry on, and after three errors the view
switches itself off for good instead of filling the log every frame. Labels are Unity text (the painter only says where and what). The name of a room that is narrow wraps on a space and shrinks to the width of the
room (9 px at the least); a mark on a wall puts its label inside the room; the prices of two close doors do not sit on one another.

## The lab

```powershell
cd tools/planlab
dotnet run -c Release -- waves mapart <seed> <floor> out.png [all|walk|start] [width] [mini]   # a floor painted without Unity
dotnet run -c Release -- maptex <folder>                                                      # the generated textures as PNG
```

The lab draws a mark where Unity writes a label, uses invented furniture and a 512x512 paper; everything else is the painter of the game.
The plan seed of the game is `runSeed + floor * 1013`, so `-seed 2995 -floor 3` is `waves mapart 6034 3`.

## Traps

- uGUI does not rebuild a graphic while its renderer is culled, and nobody queues it again when it is shown: a legend or a letter that starts
  hidden stays empty (no material) for ever. `MapView.SetCull` marks the graphic dirty when it comes back; `-maptest` checks that nothing that
  should be visible is left without a material (`MapView.UndrawnParts`).
- A layer made with `new GameObject(name, typeof(RectTransform))` is 100x100 and centred. The art hangs from its corner, so a layer needs zero
  size, zero anchors and zero pivot (`MapView.NewLayer`), or all the static art is 50 m away.
- `Texture2D.LoadRawTextureData` wants every mip level; `SetPixelData(bytes, 0)` and `Apply` make the rest.
- A photograph lifts the darks: a board that is 97% opaque shows the HUD behind it in the picture (and, dimly, on a good screen). The board is opaque.

## Checks

- `-batchmode -autotest -nofire -noenemies -maptest [-mapshots]` (23 checks, see [testing.md](../engineering/testing.md)): the map and its view exist; the start room is seen and
  the rooms behind its doors are rumors; the minimap is small and `M` and `N` work; every room entered becomes seen, the secret one only when entered; a wall weapon, the blood
  and the seals of the sealed doors appear with their names and prices; every visible part has a material; one frame costs 0.13 ms (minimap) or 0.22 ms (sheet) and allocates
  nothing; a full repaint 5 ms; the textures 4 ms on the main thread; no exception; one map per seat in split screen. Run on `-seed 2995 -floor 3` (hotel), `2961 -floor 3`
  (theatre) and `3046 -floor 3` (sanatorium), in the tower and with `-catacombe` (the catacomb levels); with `-cattedrale -floor 0` it checks that the cathedral has
  no map. The photos (`unity_logs/mappa/`) were looked at on all three themes.
- The painter builds in the lab and its previews were looked at: rooms, rumors, seals, blood and marks.
- `-stealthtest -gridtest`: its map section asks `FloorMap` how many rooms are rumors at the start, that none is left when every room is visited, and that the secret room is hidden until entered.
- Not verified: the map in split screen with a second real player and in co-op (only that every seat has a visible map), with a controller, and with the power off (no special look yet).
- Related pages: [the tower](tower.md), [power](power.md), [split screen](../multiplayer/split-screen.md), [controller](controller.md).
