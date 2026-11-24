# Third-person view

Since 0.8.0 the camera can sit behind the player's shoulder and show the player's own survivor ([characters.md](characters.md)) with the
weapon in hand. It is a setting, **Prospettiva** in *Impostazioni*, tab *Gioco*, first row: **Prima persona**
(the default, nothing changes) or **Terza persona**. The arrow buttons and the keys of the menu flip it; it is saved as `video_thirdperson`
in the player's preferences and applies at once, also in the middle of a game, in single player, in online co-op and, seat by seat, in the
split screen.

The three controls below **Prospettiva** set **Distanza 3ª persona** (1.8 to 4.5 m), **Altezza 3ª persona** (35 cm below to 90 cm above
the eye) and **Spalla 3ª persona** (90 cm left to 90 cm right, with a centred position). The Desert also exposes them under **Select →
Telecamera**. The menus share the same saved values. **Ripristina scheda** or **Valori di partenza** restores the default framing.

## How it plays

- With the default framing on foot and looking straight ahead, the camera sits 2.55 m behind the eye, 60 cm to the right and 8 cm below it. Aiming brings it to 1.18 m behind and
  40 cm to the right. It filters vertical movement on steps and uses an anchor free of the first-person head bob. The mouse and controller
  still turn the view directly. A 20 cm sphere swept from the eye pulls the camera in as soon as an obstacle gets in the way. Once the
  obstacle clears, the camera moves back out at up to 4 m/s. In tight corners it draws the first-person hands to keep the survivor out
  of the camera.
- The survivor's boots hold their ground during the weight-bearing part of each step. A sprint lifts the feet higher and leans the body
  forward. A slide drops the hips and leads with one leg. In the air the legs change from an uneven takeoff pose to a wider falling pose,
  and the hips absorb the landing. The torso follows the aim and the weapon keeps its own grip, trigger, recoil and reload poses.
  A downed survivor sits on the ground.
- Shots, prompts and the torch start at the eye. Each frame, right after the eye is placed, it turns up to 25 degrees towards the point
  under the camera's crosshair. This lines up the shot with the target when the eye has a clear path and the correction fits within that angle.
  The tracers start at the muzzle of the weapon the survivor holds
  (`ThirdPersonView.MuzzleOf`, used by `Weapon.FireRays`), not at the hidden weapon of the first person. Since 0.8.0 so do the flash of a
  seal on the weapon (`Weapon.SigilShot`, see [arms-and-magic.md](arms-and-magic.md)) and the flame of a flamethrower: the two pieces of work
  were made apart and met at this point, where both still used the hidden weapon, which in the third person sits at a hand's breadth from the
  eye, behind the survivor's shoulder.
- **Scoped optics return to the first person.** While aiming with a weapon that zooms (`WeaponDef.zoom` of 1.4 or more) the view goes back to the
  eye because the magnified sight is a second camera of the weapon. The view returns when the aim is released and the transition takes
  about a fifth of a second. Death returns to the eye immediately.
- **Desert motorcycles** use the selected perspective when mounting and dismounting, with a render transition of about 0.4 seconds.
  In third person the boom is 70 cm farther back and 52 cm higher than on foot. With less than 1.25 m of clearance the local camera hides
  the rider and switches towards the eye. Pista uses its own race camera.
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
   weapon, the prompts and the torch read it. That call computes the camera pose behind the shoulder. The eye turns towards the centre ray
   of the pose that will actually be rendered, including while the camera retracts or moves between perspectives.
2. The real camera is moved only while it draws: on `RenderPipelineManager.beginCameraRendering` its transform and culling mask are replaced,
   on `endCameraRendering` they are put back. Nothing else ever sees the camera away from the eye.
3. With enough room to draw the body, the mask gains the player's own avatar layer (`Seats.FirstAvatarLayer` + the seat) and loses the
   first-person weapon layer (`Seat.viewLayer`, 2 in single player). The muzzle effects and fill light follow the visibility of the body.
   In the saddle this also switches between the bike's first-person arms and the seated survivor.
4. The body is a `SeatAvatar` (`Coop/SeatAvatar.cs`): in the split screen the seat's own, otherwise one made for the occasion
   (`SeatAvatar.CreateOwn`, no name label). It is driven from the player each frame (velocity, pitch, lean, crouch, sprint, weapon, aiming,
   reload, downed, alive, climbing, ground contact, slide, time since the last shot).
5. `UpdateFill` keeps the fill light (a child of the player, created the first time the body is visible, faded in with the blend, switched
   off with the view and destroyed with the component). `ThirdPersonView.fillScale` exists only for the tests (0 switches it off: the dark
   photos with and without).

`GameSettings.thirdPersonOverride` and `thirdPersonFramingOverride` let the camera tests replace the settings in memory.

## Verification

The Desert phase `-autotest -desertotest -desertonly motocamera` checks the mounted view, its clearance near a wall, the three framing
controls and the transitions onto and off the bike.

`-autotest -nofire -noenemies -tptest [-tpshots]` (`ThirdPersonTest.cs`, not in distribution builds) checks, from what the camera really has in
front of it while it draws: the first person is untouched (camera at the eye, no avatar, weapon layer drawn); the third person
(component, camera 1.2 to 2.7 m from the eye, own avatar with the weapon, weapon layer not drawn, avatar layer drawn, avatar at the player's
feet and facing the same way); the eye ray and the camera ray hit the same point of a wall (less than 12 cm apart); with a seal on the
weapon, the flash at the muzzle (`SigilFx.LastMuzzle`, a field that exists for the tests) falls within 2 cm of the muzzle of the body's weapon
and not at the hidden weapon of the first person; aiming brings the camera
closer by more than 30 cm. Walking sends the velocity to the body and keeps the feet in frame. A wall 90 cm behind keeps the camera within 1.05 m and shows the
first-person hands. Removing the wall lets the camera extend gradually. A short fall exercises the airborne state and checks that the hips
dip on landing before returning to standing height. Switching off restores the camera, removes the avatar and the fill light, and draws
the weapon again. The fill light exists and lights only the body's layers.
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

On the integrated tree of 0.8.0 (this branch together with the seals, the grimoires, the map, the new menu and the split screen over several
monitors), on the same evening, `-tptest` passed 20/20: the 19 checks above and the new one, where a seal on the weapon sends its flash to
0.0 cm from the muzzle of the body's weapon, 0.79 m from the eye. Also passing there: `-seatstest` 82, 87 and 92 checks with two, three and
four players (84 and 94 with the extra monitors pretended), `-splittest` 79/79 and `-padtest` 83/83.

**Not done in the third person** (found while integrating, not fixed in 0.8.0): the left hand of the spells and of the seal gesture, and the
skin of heat that a seal puts on the weapon (`WeaponAura`), belong to the first-person view, which the third person hides; the survivor's
body does not play the gesture and its weapon does not glow. The effects still happen: a seal sends its flash, tracer and impact, and the
spells their effects and damage, but the spells start from the position of the hidden first-person palm (`OffHand.PalmWorld`), near the eye,
not from the body's hand. This comes from reading the code (`MagicCaster`); nobody has looked at a spell cast in the third person in a running game.

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
- The *Prospettiva* row by hand: `MenuReview` checks it like every other row of the tab (hint, navigation chain, reset), but nobody flipped
  the setting in a running game.
- The return to the first person with a scoped optic and on death (by reading the code), and a split-screen seat in the third person (the
  split-screen tests do not switch the view on).
- A real controller, a real network (loopback only), the sound.
- The look of the other three survivors in the third person (only the Custode is the player's own body in the photos; the others were seen as
  partners and in the contact sheets).

Source: `Scripts/Survivors/ThirdPersonView.cs`, `Scripts/Coop/SeatAvatar.cs`, `Scripts/GameSettings.cs` (`ThirdPerson`), `Scripts/GameMenu.Design.cs`
(the row), `Scripts/PlayerController.cs` (the call), `Scripts/ThirdPersonTest.cs`.
