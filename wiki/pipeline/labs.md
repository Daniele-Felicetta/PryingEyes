---
title: Labs
kind: pipeline
mode: none
summary: The engine-free .NET labs that run the rules of the packages (layout, weapons, survival, economy, saves) without the editor, through the studio's test command.
image: img/pipeline/labs.webp
caption: The labs, on a card.
sources: tools/planlab, tools/weaponslab, tools/survivallab, tools/economylab, tools/saveslab, tools/studio.py, docs/engineering/testing.md
---

# Labs

![Card: the labs, one rule package each, compiled from the same source](../img/pipeline/labs.webp)

A lab is a small .NET program that compiles the source of one package from its own path and runs its rules, with no Unity and no editor. There is one lab per package: `tools/planlab` for the layout, `tools/weaponslab` for the weapons, `tools/survivallab` for the survival budget, `tools/economylab` for the economy and `tools/saveslab` for the saves.

## How to run them

The gate runs four of them through the studio:

- `python tools/studio.py test weapon-state`
- `python tools/studio.py test survival`
- `python tools/studio.py test economy`
- `python tools/studio.py test saves`: the save library and the character and run files, 115 checks.

They take the project lock, so [ship](ship.md) does not run them. The [CI gate](ci-gate.md) does, on GitHub. Their jobs leave a `report.json` under `unity_logs/studio/`, as described in [the studio engine](studio-engine.md).

## Why they are separate from the game

A package must be checked in the same file it ships in. A lab compiles that file, so the test proves the package on its own, without the game. The rules are in [suite architecture](../../docs/engineering/suite-architecture.md).

## Related

- [Studio engine](studio-engine.md): the engine that runs the labs as jobs.
- [Production saves](../packages/saves.md): the package the saves lab checks.
- [Testing and verification](../../docs/engineering/testing.md): the whole list of checks.
