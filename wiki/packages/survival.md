---
title: Production Survival
kind: package
mode: none
summary: Engine-independent rules for the waves of the wave cycle: the budget of enemies per wave, the pace of their spawns and the limit on how many are alive at once.
image: img/packages/survival.webp
caption: The survival package, on a card.
sources: unity/PryingEyes/Packages/com.pryingeyes.survival, tools/survivallab, docs/engineering/suite-architecture.md
tests: python tools/studio.py test survival
---

# Production Survival

![Card: the survival package, a budget of enemies spread over a wave](../img/packages/survival.webp)

`com.pryingeyes.survival` (version 0.1.0, displayed as Production Survival) holds the rules that a wave needs and that do not depend on the engine: how many enemies a wave is worth, how fast they come, and how many may be alive at once. The [wave cycle](../cycles/wave-cycle.md) follows rules of this kind: 16 enemies plus 4 per wave, at most 12 plus 1 per wave alive at a time, and spawns spread over the open areas.

## Checks

`python tools/studio.py test survival` runs in the [CI gate](../pipeline/ci-gate.md) and compiles the package through the survival lab, `tools/survivallab`, without the editor.

## Related

- [The wave cycle](../cycles/wave-cycle.md): the waves these rules describe.
- [Labs](../pipeline/labs.md): the engine-free tests of the packages.
- [Trial: Testa](../trials/testa.md): one of the challenges that come with a wave.
