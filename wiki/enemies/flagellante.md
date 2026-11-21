---
title: The Flagellante
kind: enemy
mode: both
summary: The Flagellante is a monk in a red robe that runs at you and leaps from a few metres away, with 95 hit points and a strike when it lands.
image: img/enemies/flagellante.webp
sources: docs/game/horde-variants.md, docs/game/tower.md, unity/PryingEyes/Assets/_PryingEyes/Scripts/Enemy.Varianti.cs, unity/PryingEyes/Assets/_PryingEyes/Scripts/Deserto/DesertSites.cs
---

# The Flagellante

![A Flagellante of the game, a monk in a red robe leaping forward](../img/enemies/flagellante.webp)

The Flagellante is a monk in a red robe that is quicker than the others and leaps at its target. From 2.6 to 7.5 m away, with a clear
line of sight, it leaps at about 11 m/s for 0.32 seconds, and it strikes when it lands.

## What it is

- **Hit points.** 95, against the monk's 200.
- **Pace.** 2.35 to 2.75 m/s before the wave multiplier (`Enemy.Varianti.cs`).
- **Leap.** From 2.6 to 7.5 m with a clear line: about 11 m/s for 0.32 s, then a strike on landing.

## Standard and desert

- **Standard.** The wave cycle draws it from the third wave: 8% of the spawns, plus 1.2% per wave, up to 20%.
- **Desert.** It joins the walkers from about 750 m from the cathedral (danger 1.5), with a weight of 9 in the mix.

## Related

- [The monk](monk.md): the body it is built on.
- [The Appestato](appestato.md): the other variant of the wave cycle.
- [The wave cycle](../cycles/wave-cycle.md): where the variants are drawn.
- [The desert](../modes/desert.md): where the walkers rest and wake.
