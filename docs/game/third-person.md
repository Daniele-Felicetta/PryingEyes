# Third-person view

Since 0.8.0 the camera can sit behind the player's shoulder and show the player's own survivor ([characters.md](characters.md)) with the
weapon in hand. It is a setting, **Prospettiva** in *Impostazioni*, tab *Audio e comandi*, first row of the left column: **Prima persona**
(the default, nothing changes) or **Terza persona**. The arrow buttons and the keys of the menu flip it; it is saved as `video_thirdperson`
in the player's preferences and applies at once, also in the middle of a game, in single player, in online co-op and, seat by seat, in the
split screen.

## How it plays

- The camera is 2.1 m behind the eye, 55 cm to the right and 14 cm up. While aiming it comes in to 1.15 m behind and 32 cm to the right,
  so the sights and the target stay readable. A wall behind the player stops it (a sphere of 20 cm swept from the eye, never closer than
  12 cm), so in a corridor the view comes in instead of going through the wall.
- The player's own body is drawn: the legs walk, run, crouch, lean and climb as the player does, the torso follows the aim, and the weapon is in
  the hands with the grip, the finger on the trigger, the recoil and the reload hand path of that weapon. Going down sits the survivor on the
  ground; dying lays them down. The arms and the weapon of the first person are not drawn.
- **The crosshair is the truth.** Shots, prompts and the torch start at the eye, not at the camera. Each frame, right after the eye is placed,
  it is turned (by at most 25 degrees) towards the point the camera's own centre ray hits, so a bullet lands where the crosshair is at every
  distance. Without this the crosshair would be a hand's breadth off. The tracers start at the muzzle of the weapon the survivor holds
  (`ThirdPersonView.MuzzleOf`, used by `Weapon.FireRays`), not at the hidden weapon of the first person.
- **Scoped optics return to the first person.** While aiming with a weapon that zooms (`WeaponDef.zoom` of 1.4 or more) the view goes back to the
  eye, because the magnified sight is a second camera of the weapon; the view returns when the aim is released. A dead player also looks
  from the eye. The change takes about a fifth of a second (5 per second).
- The body is lit by the lamps of the hotel and by the torches of the other players, never by the player's own torch (a beam at a few
  centimetres from the weapon would burn it), see *Light layers* in [characters.md](characters.md). Where the lamps are missing or at 40%
  (every floor starts on the emergency lights) the back of the survivor would be a black stain, so in the third person a **fill light**
  follows the player: a weak warm point light (intensity 5 in the dark, range 7 m, no shadows) 2.1 m behind, 90 cm above and 50 cm to the
  right of the eye, turned with the player. Its rendering layers are only the bit of the player's slot (`SurvivorLight.FillMask`), so it
  lights that survivor and nothing else: not the world, not the arms and the weapon of the first person. It fades out where the lamps
  already light the player (`Stealth.PlayerLum`, the same measure the stealth uses, between 0.04 and 0.4), so a lit room is not made brighter.

## How it works

`ThirdPersonView` (`Scripts/Survivors/ThirdPersonView.cs`) is one component per player, created when it is first needed and destroyed when the
setting is off and the blend is back to zero. The game does not know it exists: `PlayerController.cam` stays at the eye all the time.

1. `PlayerController` places the eye and calls `ThirdPersonView.AfterEyePose` (only when the setting is on or a component exists), before the
   weapon, the prompts and the torch read it. That call computes the camera pose behind the shoulder, finds the point it looks at and turns
   the eye towards it.
2. The real camera is moved only while it draws: on `RenderPipelineManager.beginCameraRendering` its transform and culling mask are replaced,
   on `endCameraRendering` they are put back. Nothing else ever sees the camera away from the eye.
3. The culling mask gains the player's own avatar layer (`Seats.FirstAvatarLayer` + the seat) and loses the weapon layer of the
   first person (`Seat.viewLayer`, 2 in single player) once the blend passes 0.3.
4. The body is a `SeatAvatar` (`Coop/SeatAvatar.cs`): in the split screen the seat's own, otherwise one made for the occasion
   (`SeatAvatar.CreateOwn`, no name label). It is driven from the player each frame (velocity, pitch, lean, crouch, sprint, weapon, aiming,
   reload, downed, alive, climbing, time since the last shot).
5. `UpdateFill` keeps the fill light (a child of the player, created the first time the blend passes 0.3, faded in with the blend, switched
   off with the view and destroyed with the component). `ThirdPersonView.fillScale` exists only for the tests (0 switches it off: the dark
   photos with and without).

`GameSettings.thirdPersonOverride` replaces the saved setting in the tests, so a test never touches the player's preferences.

## Verification

`-autotest -nofire -noenemies -tptest [-tpshots]` (`ThirdPersonTest.cs`, not in distribution builds) checks, from what the camera really has in
front of it while it draws: the first person is untouched (camera at the eye, no avatar, weapon layer drawn); the third person
(component, camera 1.2 to 2.7 m from the eye, own avatar with the weapon, weapon layer not drawn, avatar layer drawn, avatar at the player's
feet and facing the same way); the eye ray and the camera ray hit the same point of a wall (less than 12 cm apart); aiming brings the camera
closer by more than 30 cm; walking sends the velocity to the body; a wall 90 cm behind keeps the camera within 1.05 m; switching off restores
the camera, removes the avatar and the fill light, and draws the weapon again; the fill light exists and lights only the body's layers.
A game started with `-batchmode` and no window does not draw its main camera by itself, so the test makes the camera draw (`RenderNow`, onto
a small `RenderTexture`) before every group of checks; it also needs `holdLook` off for the walk, because `botDir` only moves the player while the
autotest's automatic turning is on. With `-tpshots` the photos go to `unity_logs/terza-persona/`: `ferma`, `muro`, `mira`, `cammino`,
`muro-dietro`, and two dark ones, `buio-senza` and `buio-con` (every lamp off and the torch on, without and with the fill light), which show
whether the character is readable in the dark and whether the body casts a shadow into the torch beam. The test also checks that every light
layer `SurvivorLight` uses is defined in the project (see the trap in [characters.md](characters.md)).

### What was run

On 2026-11-17, on the `Builds\Audit` player of the branch, with no window (`-batchmode`, the GPU on for the photos):

| Test | Result |
| --- | --- |
| `-autotest -nofire -noenemies -tptest -tpshots` | 19/19: the camera 2.18 m behind the eye (1.20 m aiming, 0.58 m with a wall 90 cm behind), eye ray and camera ray 0 cm apart on a wall, 2.3 m/s of walking reaches the body, switching off restores everything, the fill light lights only the body's layer, every light layer is defined |
| `tools/coop/run_split.ps1 -Players 4` | 64/64 in 13 of 14 runs (the four survivors in one split screen); one run ended 63/64, see below |
| `-splittest` | 71/71 |
| `tools/coop/run_all.ps1 -Only a,h` | all passed: two processes (62/62 and 49/49) and four (62/62, 48/48, 48/48, 49/49) on loopback, protocol `pe-coop-29` |
| `tools/coop/run_coop.ps1 -Name foto -HostGraphics ... -cooppics` | 13/13, with photos of a partner from the side, close and from behind |

The one run that did not pass was the first on the final build: the check *senza aiuto il giocatore 2 si dissangua* of `SeatsTest` (the third
time player 2 goes down nobody helps, so they bleed out and die once while the others stay up with their points) failed, 63/64. It did not repeat in
the 13 runs that followed (three on the same build, ten on one that only adds a diagnostic to that check), the third person is off in that test, and
the log of the failing run did not say which condition of the check had fallen, so the cause is not known. The check now logs a `seatstest diagnosi`
line with every condition when it fails.

The photos show the Custode behind the shoulder in the theatre hall (`ferma`, `mira`, `muro-dietro`) and, with every lamp off and the torch on,
a black silhouette without the fill light (`buio-senza`) and a readable character with it (`buio-con`); at intensity 10 and above the body starts to look
lit by a lamp, so 5 is the value.

### What was not verified

- A game window on a monitor. Every test runs without one, so the main camera drawing by itself every frame was never exercised; the swap is the
  same `beginCameraRendering` code in both cases (the test makes the camera draw).
- The *Prospettiva* row by hand: `MenuReview` checks its layout and wiring, but nobody flipped the setting in a running game.
- The return to the first person with a scoped optic and on death (by reading the code), and a split-screen seat in the third person (the
  split-screen tests do not switch the view on).
- A real controller, a real network (loopback only), the sound.
- The look of the other three survivors in the third person (only the Custode is the player's own body in the photos; the others were seen as
  partners and in the contact sheets).

Source: `Scripts/Survivors/ThirdPersonView.cs`, `Scripts/Coop/SeatAvatar.cs`, `Scripts/GameSettings.cs` (`ThirdPerson`), `Scripts/GameMenu.Design.cs`
(the row), `Scripts/PlayerController.cs` (the call), `Scripts/ThirdPersonTest.cs`.
