# Documentation

The map of every page. The [project README](../README.md) says what the game is and how to run it; this index says where the details live.
Pages are grouped by who needs them: what the game is (`game/`), how it is built, verified and shipped (`engineering/`), and how it got here
(`history/`). The co-op design has its own folder (`multiplayer/`).

## Start here

| If you want to... | Read |
| --- | --- |
| understand the game | [The cathedral and the catacombs](game/cathedral-and-catacombs.md), [The Tower](game/tower.md) (the levels), then [Weapons](game/weapons.md); [The desert](game/the-desert.md) for the second way to play; [Characters, levels and saves](game/saves-and-characters.md) for who plays and what is kept |
| build it, ship a change, understand CI | [Building, shipping and CI](engineering/building-and-shipping.md) |
| know what is verified and how to run each check | [Testing and verification](engineering/testing.md) |
| reuse the production suite for another project | [Production suite](engineering/suite-architecture.md) |
| add something to a save without breaking old saves | [Save system](engineering/save-system.md) |
| see what changed and why | [Quality audit](history/quality-audit.md), [Survival redesign](history/zombies-redesign.md), [Earlier versions](history/legacy-versions.md) |

## The game (`game/`)

| Page | What it covers |
| --- | --- |
| [cathedral-and-catacombs.md](game/cathedral-and-catacombs.md) | The two zones: the cathedral to rebuild (works, market, blessings) and the catacombs below (branches, bosses, resources, satchel, extraction, death and remains, oboli). |
| [the-desert.md](game/the-desert.md) | The second way to play, chosen in the menu: an open, procedural desert with a day and night sky, buildings and towers made of the rooms of the floors, catacombs, bosses on the last floor, the cathedral as the base (armoury at twice the price, perk books and consecration as works), what is found where, how the world, the sand, the navmesh and the places are made, the decisions left to the developer, the checks. |
| [saves-and-characters.md](game/saves-and-characters.md) | The characters a player makes and chooses (name, look, levels, counters, one cathedral each), how they are chosen in split screen and online, the expedition that can be suspended and resumed, old saves that are upgraded, the test profile, the checks. |
| [tower.md](game/tower.md) | The Tower: procedural floors and themes, the wave cycle (default) and the candle cycle (legacy), points, perks, shops, shrines, power-ups, themed enemies, controls, code map. The floors are now the levels of the catacombs. |
| [weapons.md](game/weapons.md) | The 28 weapons in 5 grades, per-weapon numbers, gunplay, sights, and the Blender to Unity pipeline. |
| [guardaroba.md](game/guardaroba.md) | The fourth theme (wardrobes, vampires, scissors, lava): its 17 new props, materials, how to make and photograph them again, how they reach the game (the kit saved in `Torre.unity`), where the rooms place them, traps and what is verified. |
| [textures.md](game/textures.md) | The wall, column and floor textures redone on 17 November 2026: the two checkerboard marble floors that alternate room by room, the stone column mesh, the luminance kept, how to make them again and check them. |
| [props-phase5.md](game/props-phase5.md) | Sixteen hotel and church props rebuilt in detail (chair, table, wardrobe, desk, shelf, pew, sarcophagus, hooded statue, range cooker, floor lamp, altar, font, choir stall and more): what each is, the modelling tools, the three levels of detail and how Unity groups them, how to make them again and what is verified. |
| [medusa.md](game/medusa.md) | The flying jellyfish: the Blender model (66,000 triangles, 240 bones), its textures, the code that animates it, the effects of its attack, how to make it again and what is checked. |
| [horde-variants.md](game/horde-variants.md) | The plague bearer, flagellant, statue and cardinal; blood. |
| [equipment-and-trials.md](game/equipment-and-trials.md) | Holy water and the music box, the trial of each wave, the glass eyes, the reliquary mine. |
| [door-traps.md](game/door-traps.md) | Fire and lightning levers on bought doorways. |
| [barrels-and-rising.md](game/barrels-and-rising.md) | Holy oil barrels, and enemies rising from the floor. |
| [chandeliers.md](game/chandeliers.md) | Chandeliers that fall when shot, and breakable lamps. |
| [power.md](game/power.md) | Floor power: emergency lights, the switchboard and its lever. |
| [map.md](game/map.md) | The map: a hand-inked dossier (minimap and big sheet), what each mark means, how a floor is revealed, the painter and its lab, the checks. |
| [cathedrals-and-annexes.md](game/cathedrals-and-annexes.md) | Cathedral halls and annexes of the survival layout. |
| [arms-and-magic.md](game/arms-and-magic.md) | The biomechanical arms and articulated left hand, the spells and weapon seals of the second hand, the secret book room and its two 19-node grimoires. |
| [characters.md](game/characters.md) | The four survivors (Custode, Dottoressa, Reporter, Cacciatrice): who is who, how the code animates them (legs, gait, weapon in hand, fingers), how Blender makes them and how to make one more, where they show (co-op partners, split screen, your own body). |
| [third-person.md](game/third-person.md) | The third-person view (setting *Prospettiva*): the camera behind the shoulder, why shots still land on the crosshair, how it is rendered, its check and what is not verified. |
| [feel-and-reflections.md](game/feel-and-reflections.md) | Room reflections, scoreboard, inspection, ragdolls, vaulting, hit reactions, light shafts, empty magazines, medals, adaptive music, surface impacts. |
| [display-and-menu.md](game/display-and-menu.md) | The hotel-register main menu, the five settings tabs (display, image, audio, controls, game), the guide, the tips and the pause screen. |
| [menu-music.md](game/menu-music.md) | The main-menu theme (a generated waltz for music box and organ) and the menu tones: how they are made, when they play, their loudness, what was measured and what was not listened to. |
| [controller.md](game/controller.md) | The XInput controller: how the game finds it, the mapping, menu navigation with it, the on-screen hints, the checks and what is not verified. |
| [multiplayer/README.md](multiplayer/README.md) | Online co-op for up to four players: how it plays, the protocol, the tests, the limits (Italian). |
| [multiplayer/split-screen.md](multiplayer/split-screen.md) | Split screen on one PC for two to four players, in one game, in one window or over several monitors: layout, controllers, what is shared, how it works, the checks and what is not verified. |

## Engineering (`engineering/`)

| Page | What it covers |
| --- | --- |
| [building-and-shipping.md](engineering/building-and-shipping.md) | Build methods, the `Builds/` layout, `tools/ship.ps1`, the gate and the CI workflow, branches, other platforms, releases. |
| [testing.md](engineering/testing.md) | Every verification: the hermetic checks, the Unity acceptance checks, the in-game headless tests, navigation, co-op, the performance probe. |
| [suite-architecture.md](engineering/suite-architecture.md) | The reusable production suite: layers, the boundary guard, how a second project plugs in. |
| [save-system.md](engineering/save-system.md) | The save library and the rules for saves: the files on disk, what happens when one is read (nine states), how to change what a save contains without breaking old ones (the recipe and the version history), how it is checked. |
| [production-studio.md](engineering/production-studio.md) | The content pipeline: one source for weapon data, furnished rooms, the weapon-state module, the persistent queue, the evidence gallery. |
| [city-prototype.md](engineering/city-prototype.md) | The station district: the first project built on the suite beyond the hotel, its labs and verified results (Italian). |
| [trailer.md](engineering/trailer.md) | How the trailer is recorded from the game itself and edited, and how to make it again. |
| [unreal-prototype.md](engineering/unreal-prototype.md) | The Unreal Engine 5.8 feasibility prototype: one generated room exported from Unity and rebuilt in Unreal from the command line (no Blueprints), how the Unity look is matched, how frames are timed, results and traps. |

The reusable C# packages have their own READMEs:
[layout](../unity/PryingEyes/Packages/com.pryingeyes.layout/README.md),
[weapons](../unity/PryingEyes/Packages/com.pryingeyes.weapons/README.md),
[survival](../unity/PryingEyes/Packages/com.pryingeyes.survival/README.md),
[economy](../unity/PryingEyes/Packages/com.pryingeyes.economy/README.md),
[saves](../unity/PryingEyes/Packages/com.pryingeyes.saves/README.md).

## History (`history/`)

| Page | What it covers |
| --- | --- |
| [legacy-versions.md](history/legacy-versions.md) | The earlier versions kept for reference: the Three.js prototype, the Unreal Engine 5 prototype, and the first Unity hall. |
| [quality-audit.md](history/quality-audit.md) | The log of the quality passes of 12 to 15 November 2026: what was changed, measured and left open (Italian, long). |
| [zombies-redesign.md](history/zombies-redesign.md) | The record, pass by pass, of the redesign that made the wave cycle the default (Italian). |

The first README of the project was a single Italian page that held much of the above. It was split into these pages and checked against
the code; the last version of that page is in git history (`git show b77eca5:README.md`).

## Conventions

- Pages are written in English. Identifiers, flags, paths and in-game names stay verbatim. Four older pages are still in Italian and are
  marked above.
- A claim that was not checked against the code or by running something says "(not verified)".
- Each page starts with what it is, then how to run it, then how it was verified.
- No machine-specific paths: write paths relative to the repository root.
- When a change alters what a page says, update the page in the same commit.
- A new page is added to this index. `python tools/check_suite.py` fails on a broken link, a page missing from this index and a
  machine-specific path, and `tools/ship.ps1` and the CI gate run it.
