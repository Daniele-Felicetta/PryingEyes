---
title: The steel work table
kind: prop
mode: both
summary: The steel work table (arr-tavolo-acciaio) is a workbench with a rolled-edge top, a drawer with a bar handle and a lower shelf, placed in a row across the hotel's kitchen and in the sanatorium's rooms.
image: img/props/arr-tavolo-acciaio.webp
sources: docs/game/props-phase5.md, unity/PryingEyes/Assets/_PryingEyes/Scripts/Proc/RoomKitsAlbergo.cs, unity/PryingEyes/Assets/_PryingEyes/Scripts/Proc/RoomKitsSanatorio.cs, unity/PryingEyes/Assets/_PryingEyes/Scripts/Proc/RoomKitsCover.cs, unity/PryingEyes/Assets/_PryingEyes/Scripts/Proc/PropDefs.cs
---

# The steel work table

![A steel work table of the game, a workbench with a rolled top, a drawer and a lower shelf](../img/props/arr-tavolo-acciaio.webp)

The steel work table (`arr_tavolo_acciaio`) is a workbench with a top with a rolled edge, a drawer with a bar handle, a lower shelf and four
legs with feet.

## What it is

- **Model.** A rolled-edge top, a drawer with a bar handle, a lower shelf, and four legs with feet.
- **Size.** Model 1.90 x 0.90 x 0.84 m; collider 1.9 x 0.9 x 0.8 m (`PropDefs.cs`).
- **Levels of detail.** Three levels: the model, then 28 % and 9 % of its triangles.

## Numbers

- **Triangles.** 1,620 at level 0, 450 at level 1, 144 at level 2. The FBX it replaced had 192.
- **Placements.** 531 in 500 generated floors (`-propstats`).

## Where it stands

- In a row across the hotel's kitchen, 40 % of the way into the room (`RoomKitsAlbergo.cs`, `Cucina`).
- In the sanatorium's rooms: as a central table and in rows (`RoomKitsSanatorio.cs`).
- As cover, with weights of 1.2 and 1.5 (`RoomKitsCover.cs`).

## Standard and desert

- **Standard.** The hotel kitchen and the sanatorium's rooms, and the cover kit.
- **Desert.** The desert's hotels and sanatoria are dressed by the same room kits (`DesertSites.cs`, `DressNext`), so work tables stand there too.

## Related

- [The range cooker](arr-fornello.md): the cooker along the far wall of the same kitchen.
- [The warehouse shelf](arr-scaffale.md): the shelf of the same kitchens and wards.
- [The hotel](../places/hotel.md): the desert place built from hotel rooms.
