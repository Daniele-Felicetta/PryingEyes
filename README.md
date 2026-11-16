# Prying Eyes

A first-person survival horror game set in **the Tower**: a descent through floors of hotel wings, churches, theatres and sanatoria,
generated again on every run. Open new areas with the points you earn, hold out against waves of monks, abbots and things that float,
and take the lift down to the next floor. Play alone, in online co-op with up to four players, or in split screen on one PC.

Unity 6 (6000.6.7f1), Windows player, co-op over FishNet (UDP port 7807). The main scene is
`unity/PryingEyes/Assets/_PryingEyes/Scenes/Torre.unity`.

## Play

- **From a release**: download the Windows zip of the latest release on the repository's Releases page, unzip the whole folder and run
  `PryingEyes.exe`. The main menu and the pause menu have an in-game guide with the controls ("Come si gioca").
- **From source**: open `unity/PryingEyes` with Unity 6000.6.7f1, open `Assets/_PryingEyes/Scenes/Torre.unity` and press Play. Building a
  player, the `Builds/` layout and the other platforms are in [Building, shipping and CI](docs/engineering/building-and-shipping.md).
- **Co-op**: *Cooperativa* in the main menu. One player hosts and the others type the host's address. There is no relay: to play over the
  Internet the host needs UDP port 7807 reachable. Design, protocol and limits are in [docs/multiplayer](docs/multiplayer/README.md).
- **Controller**: an Xbox-compatible (XInput) controller plays the game and drives the menus; press a button and the game finds it. Mapping and
  rules in [docs/game/controller.md](docs/game/controller.md).
- **Split screen**: *Schermo condiviso* in the main menu, two to four players on one PC in the same game and the same window, the screen
  divided in equal parts (everyone can use an Xbox-compatible controller; player 1 also has the keyboard and the mouse). Layout, controls and
  limits are in [docs/multiplayer/split-screen.md](docs/multiplayer/split-screen.md).

## What is in the game

- A procedural tower: every floor has a theme (hotel with church, theatre, sanatorium), a room grid with roles, and waves that start on their
  own. Areas behind steel shutters are bought with points. Three waves open the lift, and you can stay to fight and shop.
- 28 weapons in five grades, modelled in Blender, with real sights, recoil, lean, a knife, throwables and a mine.
- Perks in magic books, a mystery chest, a consecration altar, power-ups, trials, medals, door traps, falling chandeliers, exploding oil
  barrels and enemies that rise from the floor.
- Monks, abbots and flying eyes; the theatre and the sanatorium bring their own enemies (Maschera, Direttore, Infermiere, Primario), and
  later waves add the Appestato, the Flagellante, the Statua and the Cardinale. Dead bodies fall as ragdolls.
- Adaptive music, surface-specific impacts, light shafts through the windows and lamps and chandeliers you can shoot out.

Everything is described, with the numbers checked against the code, in [The Tower](docs/game/tower.md) and
[Weapons](docs/game/weapons.md); the other feature pages are in the [documentation index](docs/README.md).

## Repository map

| Path | What it holds |
| --- | --- |
| `unity/PryingEyes` | The game: the Unity project. Code in `Assets/_PryingEyes/Scripts` (`Proc/` floors and waves, `Coop/` co-op, `Armi/` weapons), editor tools in `Assets/_PryingEyes/Editor`. |
| `unity/PryingEyes/Packages` | Engine-independent C# packages: `layout`, `weapons`, `survival`. No Unity, no game names. |
| `unity/LayoutDemo` | An independent Unity project that consumes the packages: the proof they are reusable. |
| `tools/` | The production suite and the checks: the Python engine and its project profile, the .NET rule labs (`planlab`, `weaponslab`, `survivallab`), `check_suite.py`, `ship.ps1`, the co-op and trailer scripts, the unit tests. |
| `modelli/` | The Blender scripts that generate the weapons, furniture, church, themed props, hands and enemies. |
| `hotel_build/` | Screenshots and result files written by the editor tools and the in-game tests, older committed evidence, and the scripts of the Unreal prototype. |
| `index.html`, `js/`, `style.css` | The first prototype, a Three.js page. |
| `docs/` | The documentation. [docs/README.md](docs/README.md) is its index. |
| `.github/workflows/gate.yml` | The CI gate. |

## Working on it

[AGENTS.md](AGENTS.md) holds the working agreements for people and coding agents. In short:

```powershell
python tools/check_suite.py                                  # package and engine boundaries, documentation links
python -m unittest discover -s tools -p "test_*.py"          # hermetic unit suite, about a minute, no Unity
```

Both run before every commit and again on GitHub for every push to `preview` and every pull request against it. `tools/ship.ps1` does
commit, build, smoke test, push and the refresh of the playable build in one step. What each verification covers, and how to run the
heavier ones that need Unity and a built player, is in [Testing and verification](docs/engineering/testing.md).

The earlier versions of the game (the Three.js page, an Unreal Engine 5 prototype and the first Unity hall) are described in
[Earlier versions](docs/history/legacy-versions.md).
