---
title: Production Weapons
kind: package
mode: none
summary: Engine-independent data and rules of a weapon: its record, the rules of a catalogue, ammunition, the transitions of a reload and the cadence of the shots, with no engine and no game dependency.
image: img/packages/weapons.webp
caption: The weapons package, on a card.
sources: unity/PryingEyes/Packages/com.pryingeyes.weapons, tools/weaponslab, docs/engineering/suite-architecture.md
tests: python tools/studio.py test weapon-state
---

# Production Weapons

![Card: the weapons package, a magazine and a reload](../img/packages/weapons.webp)

`com.pryingeyes.weapons` (version 0.1.0, displayed as Production Weapons) holds the numbers of a weapon and nothing else. It has the record of a weapon as the catalogue writes it (damage and its fall with range, rate of fire, magazine, recoil, accuracy, and the hints for sight and sound that the game reads) with the rules that a catalogue must satisfy, and the mechanical state of a weapon: how many rounds are in the magazine and in the reserve, which step a reload is in, and how fast the shots follow each other. The game reads `weapons.json` itself and keeps everything that looks, sounds or moves. Its `Weapon` class still mixes its input and its presentation with the state, which was extracted so that the state can be checked without the editor.

## Checks

`python tools/studio.py test weapon-state` runs in the [CI gate](../pipeline/ci-gate.md). It compiles the package through `tools/weaponslab`. Each of the game's weapons has a page of its own, starting with [the Pieta P-9](../weapons/pieta-p9.md).

## Related

- [Pieta P-9](../weapons/pieta-p9.md): the starting pistol, whose numbers this state follows.
- [Labs](../pipeline/labs.md): the engine-free tests of the packages.
- [Suite architecture](../../docs/engineering/suite-architecture.md): the note on what the game still mixes into `Weapon.cs`.
