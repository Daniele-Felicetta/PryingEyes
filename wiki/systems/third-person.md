---
title: Third-person view
kind: system
mode: both
summary: The Prospettiva setting puts the camera behind your survivor's shoulder, 2.1 m behind your eye and 55 cm to the right, and brings it in when you aim.
image: img/systems/third-person.webp
caption: The third-person view, on a card.
sources: docs/game/third-person.md, docs/game/display-and-menu.md
---

# Third-person view

![Card: third-person view, the camera behind the survivor's shoulder](../img/systems/third-person.webp)

Since 0.8.0 the camera can sit behind the player's shoulder and show the player's own survivor. The setting is **Prospettiva** in the *Gioco* tab: *Prima persona* (the default) or *Terza persona*. The arrow buttons and the menu keys flip it, and it is saved as `video_thirdperson`.

## What it does

- **Camera.** It sits 2.1 m behind the eye, 55 cm to the right and 14 cm up. While aiming it comes in to 1.15 m behind and 32 cm to the right.
- **Body.** The player's own survivor is drawn. The camera pose is computed behind the shoulder, and the weapon, the prompts and the torch read it.
- **Switching.** The setting takes effect at once, in a game too.

## Standard and desert

Both modes. The setting lives in the game's menu and applies to both.

## Related

- [Display and menu](display-and-menu.md): where the setting lives.
- [Controller](controller.md): the aim and lean keys of a pad.
