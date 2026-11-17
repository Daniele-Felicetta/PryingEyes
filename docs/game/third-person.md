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
  centimetres from the weapon would burn it), see *Light layers* in [characters.md](characters.md).

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

`GameSettings.thirdPersonOverride` replaces the saved setting in the tests, so a test never touches the player's preferences.

## Verification

`-autotest -nofire -noenemies -tptest [-tpshots]` (`ThirdPersonTest.cs`, not in distribution builds) checks, from what the camera really has in
front of it while it draws: the first person is untouched (camera at the eye, no avatar, weapon layer drawn); the third person
(component, camera 1.2 to 2.7 m from the eye, own avatar with the weapon, weapon layer not drawn, avatar layer drawn, avatar at the player's
feet and facing the same way); the eye ray and the camera ray hit the same point of a wall (less than 12 cm apart); aiming brings the camera
closer by more than 30 cm; walking sends the velocity to the body; a wall 90 cm behind keeps the camera within 1.05 m; switching off restores
the camera, removes the avatar and draws the weapon again. With `-tpshots` the photos go to `unity_logs/terza-persona/`.

RESULTS_PLACEHOLDER

Source: `Scripts/Survivors/ThirdPersonView.cs`, `Scripts/Coop/SeatAvatar.cs`, `Scripts/GameSettings.cs` (`ThirdPerson`), `Scripts/GameMenu.Design.cs`
(the row), `Scripts/PlayerController.cs` (the call), `Scripts/ThirdPersonTest.cs`.
