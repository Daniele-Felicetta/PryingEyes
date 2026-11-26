# Characters, levels and saves

Since 0.9.0 a player is a **character**: a name and one of the four survivors' looks, chosen in the menu
and kept for good. The character is who plays, alone, in online co-op and in split screen, and what it earns stays with it: experience and
levels, its own cathedral (store, works, market, blessing, remains) and the counters of its history. Characters are saved one file each, an
expedition in progress can be suspended and resumed, and a save written by an older game is upgraded when the new one reads it. The format, the
guarantees and the recipe for changing what a save contains are in [../engineering/save-system.md](../engineering/save-system.md); this
page is the game side: what the player sees and what is kept.

## Making and choosing a character

- **First start.** With no character, *Gioca* (and the co-op panel) opens *Nuovo personaggio* ("Per giocare serve un personaggio: dagli un
  nome."): a name, and a look chosen with the *Aspetto* button among the four survivors of [characters.md](characters.md) (it proposes the first
  look nobody has yet). *Crea* saves the file at once and the menu goes back to the panel the player came from.
- **Names.** 1 to 20 characters, at least one letter or digit, spaces tidied, and no two characters with the same name ignoring case
  (`CharacterNames`). A wrong name is explained on the panel in words.
- **Looks.** A character keeps its look for good. Two characters on one machine may share a look, but never in one game: in a split screen or an
  online game the second one takes the first free look for that game only (`CharacterLooks.Resolve`; the character is not changed).
- **The list.** *Personaggi* (main menu) shows every character, the one in use marked with a diamond, and for the selected one: look, level
  with an experience bar, kills and headshots, games (all, online, split screen), play time, works of the cathedral and returns, and the
  expedition it has suspended, if any. *Nuovo personaggio* adds one; *Elimina* asks first and moves the file to `deleted/` (it is put back by
  hand). A save the game cannot read is not hidden: a line under the list says how many there are and why.
- **The character in use** is the last one chosen (remembered between sessions). It shows under *Gioca* and in the co-op panel (*Il tuo
  personaggio*). It plays alone, online, and in seat 1 of a split screen.
- **Players of the earlier versions.** The old `cattedrale.json` (the whole cathedral of the PC in one file) becomes the first character, *Il
  Custode*, the first time the new game starts, with the experience its history is worth (below). The old file stays where it is, so an older
  game still reads it; a marker file keeps the import from repeating after the character is deleted.

## Levels

The character saves only its **total experience**; the level is always computed from it (`Leveling`), so a new version that changes the curve
fixes the old characters by itself. All numbers are one table in `Rules/Characters/Leveling.cs`.

| Source | Experience |
| --- | --- |
| a kill / a headshot kill / a knife kill (takedown included) | 5 / 8 / 10 |
| the boss of a branch | 150 |
| each level of the catacombs reached for the first time in an expedition | 40 |
| coming back up alive with the satchel | 100 |
| dying after going down | 25 |

To reach level *n* the total is `50 × (n − 1) × n` (100, 300, 600, 1000 ... 4,500 for level 10), to a maximum of 99. The pace is meant to be a
few expeditions per level at the start: a whole branch (90 kills, its boss, three levels, the return) is worth 820 experience, level 4, and
ten of them make level 13 (`LEVEL_PACE_IS_A_FEW_TRIPS` in the lab; nobody has played it yet). A level gives little on purpose, so the
character's story matters more than its numbers: **+10 starting points per level** for a descent (at most +250) and **+1 maximum health per
level** (at most +25), both added to what the cathedral's works give. Levelling up shows a banner on the player's own HUD.

The old file's history is worth experience when it is imported: each return 100, each death 25, each boss killed 150 and each level reached
40 (`CathedralJson`).

## What a character keeps

| Kept | Where | Notes |
| --- | --- | --- |
| name, look, when and by which version it was made | the character file | |
| experience (the level is derived) | `xp` | |
| counters: kills, headshots, games (all / online / split screen), play time | `stats` | play time is added every half minute and when the game stops |
| the cathedral: store, works built, market, blessing, remains in the catacombs, history | `cathedral` | the same tree as before, one per character; works are saved by id, an id this version does not know is kept |
| anything a later version adds (talents, achievements...) | `extras` | one key per system; see the recipe in the engineering page |

Writes are small and frequent: the file is marked when something changes and written within two seconds (`CharacterAutosave`), at once for what
matters (a work built, an expedition that starts or ends), and when the game pauses, loses the focus, returns to the menu or quits. A crash
loses at most the last two seconds of what was earned (the play time is the exception: it is added every half minute); the file written
before is kept as `.bak`.

## Playing together

- **Split screen** (`GameMenu.Split`): after choosing the number of players a panel asks *Chi gioca?*, one row per seat. Seat 1 starts as the
  character in use, the others as the most recently played characters nobody else has taken; the button of a row steps through the free
  characters and then *nuovo personaggio*. *Comincia* starts the game, creating a character named *Giocatore N* for any seat left on *nuovo
  personaggio* (it can be renamed or deleted from the list afterwards). Every seat then plays its own character: its experience, its kills,
  its play time and its loot go to its own file, and each seat has its own cathedral state (the seat swap, `Seats.Enter`, carries it with
  the other per-player globals). A level up shows on that seat's HUD only.
- **Online co-op**: every machine plays its own character. The name, the look and the level go in the greeting messages (`Hello`, `Welcome`,
  `PlayerJoined`; protocol `pe-coop-30`), the host gives a free look to whoever asked for one already taken, and the partners are drawn in
  the look they were given with the name above the head. What a player picks up goes to the store of that machine's character, as before;
  an older client (`pe-coop-29`) is refused at the handshake like any other protocol difference.
- The cathedral, its works and the market are still played alone: [cathedral-and-catacombs.md](cathedral-and-catacombs.md).

## A suspended expedition

In the pause menu, *Torna al menu* opens *Tornare al menu?*. Where saving is possible it offers **Salva e torna al menu** beside *Annulla* and
*Lascia la partita* (which still counts as dying, as before; *Esci dal gioco* does not save either). Saving writes
`characters/runs/<id>.json`, one per character, and returns to the menu. *Gioca* then asks *Una spedizione ti aspetta* (the character's name,
the catacomb level, the points, what the satchel holds and when it was saved) with **Riprendi la spedizione** and **Ricomincia dalla
cattedrale**. The second counts as abandoning: the satchel is treated like a death (half of the common resources are kept, the rest waits
as remains on the first level of the branch), and the file is archived, not destroyed (see below).

The game only lets a player save where saving is honest (`FloorDirector.SuspendProblem` says why not, in words, on the panel):

- alone, never in split screen or online (the state of several players and of the host would have to be saved together, which is not done);
- on a level of the catacombs, not in the cathedral and not before the level is ready;
- alive;
- between waves, never during one;
- before the exit has opened (after the boss or the last wave the level is finished: go down or up first).

**What it keeps:** the branch, the level and its seed, the points, the satchel, whether the return bell was used, the maps revealed, the
weapons (by id, with mode, upgrade, magazine and reserve ammunition), the perks bought and the revive used, the second hand's magic
(grimoires, essence, the chosen spell and weapon seal), the equipment (grenades, lures, mines) and the areas already bought on that level (their
doors are up again, so the points they cost are not paid twice). **What it does not keep:** where the player stands, the enemies, the wave (the
level restarts from its beginning with the same seed, so it is the same level and the waves start again from the first), the traps of the
doors, the torch charge, the health and what lies on the floor. The panel says it in one line, "Riprende dall'inizio del livello: le ondate
ricominciano."

Resuming consumes the file (two copies of one satchel must never exist): it is moved to `runs/consumed/<id>.json`, the last one per
character, so a resume that fails to start can be put back by hand. Deleting a character archives its expedition the same way.

## Old saves and new versions

Every file records its kind and its version. A file from an older game is copied to `backup/` first, upgraded step by step to the current
shape (each version has one small, tested step) and written back; a file the game cannot understand is never destroyed (a damaged one goes
to `quarantine/`, a newer one is left untouched and the list says so). New content in a later version (a work, a resource, a market offer, a
new system) reaches an old character without any step, because every field the file does not have takes its starting value. The rules for
whoever adds to a save, with the recipe and the examples, are in [../engineering/save-system.md](../engineering/save-system.md).

## Profiles

The player's characters live in `persistentDataPath/characters/`. Anything automated uses a folder of its own and never touches them:
`-autotest`, `-guntest`, any `-batchmode` process (the Unity builds, the menu review) and `-profilo NAME` read and write
`characters-<profile>/` (`characters-prova/` by default), with a single test character `prova` (`prova-<name>` for each process of a
co-op test, named by `-coopname`). In that profile there are no preferences, no import of the old file and no level bonuses, so the numbers
of the older tests do not move. `-chartest` deletes the test folder first and refuses to run on any other folder.

## Code map

| Piece | Where |
| --- | --- |
| The save library: JSON, formats and migrations, the store (atomic writes, backups, quarantine) | `unity/PryingEyes/Packages/com.pryingeyes.saves` ([README](../../unity/PryingEyes/Packages/com.pryingeyes.saves/README.md)) |
| The pure parts of the game side: looks, names, level curve, the character and the run records, the old file's import | `Rules/Characters/CharacterLooks.cs`, `CharacterRecord.cs`, `Leveling.cs`, `CathedralJson.cs`, `RunRecord.cs` (compiled by `tools/saveslab` too) |
| The list, creation, choice, deletion, writing, the suspended run's files, the test profile | `Scripts/Characters/Roster.cs` |
| Experience and play time; the autosave | `Scripts/Characters/Progress.cs`, `CharacterAutosave.cs` |
| What a run keeps and puts back (weapons, perks, magic, equipment) | `Scripts/Characters/RunSave.cs`, `Scripts/Weapon.Run.cs`, `Scripts/Magia/MagicState.cs` (`Capture`, `RestoreFrom`) |
| Suspend and resume | `Scripts/Proc/FloorDirector.Run.cs`, `Scripts/Cattedrale/Fabbrica.cs` (`Riprendi`, `Sospendi`) |
| The cathedral of each character | `Scripts/Cattedrale/Fabbrica.cs` (`Apri`, `Seleziona`, `Usa`, `Dimentica`) |
| The menu: list, creation, deletion, *Chi gioca?*; the expedition panel and the three-button leave | `Scripts/GameMenu.Characters.cs`, `GameMenu.Run.cs`, `GameMenu.Split.cs`, `GameMenu.cs` |
| Seats and the partners' looks | `Scripts/Coop/Seats*.cs`, `CoopGame.cs`, `CoopWire.cs`, `RemotePlayer.cs`, `SeatAvatar.cs` |
| The checks | `tools/saveslab`, `Scripts/Characters/CharacterTest.cs`, `Scripts/Cattedrale/CattedraleTest.cs`, `Scripts/SeatsTest.cs`, `Editor/MenuReview.cs` |

## Verification

On 18 November 2026, on the build of that day (`Builds/Audit`), with every process on one PC and no graphics unless said:

- `python tools/studio.py test saves`: 115 of 115 checks, also in the CI gate (what they cover:
  [../engineering/save-system.md](../engineering/save-system.md#how-it-is-checked)).
- `-batchmode -nographics -autotest -chartest` (`Characters/CharacterTest.cs`): 23 of 23, the characters in the real game on the test profile (creation,
  names refused, a cathedral for each character, one level for the experience that earns it, the files read again, a field and a work this
  version does not know that survive, the extras of a future system, deletion to `deleted/`).
- `-cattedraletest -nowaves -seed 1000`: 72 of 72 (49 before). The 23 new ones are the expedition that is suspended in the middle of the first level of
  the Ossario with a recognisable kit (the AR-9 Malleus with 5 rounds and 42 in reserve beside the pistol, a perk, 3 grenades and 2 lures, a spell
  with 77 essence, the points, a bought area), wiped as a new session would be and resumed: the level and its seed, the satchel, the points, the
  perk, both weapons with their ammunition, the equipment, the magic and the bought area with its doors up all come back, the file is consumed and the
  resume counts neither a descent nor a trip; then an abandoned expedition counts as a death and its file goes to `runs/consumed`.
- `-seatstest -splitscreen N` from the menu: 89 of 89 with two players and 99 of 99 with four, among them 7 checks on the characters of the seats.
- Co-op, `tools/coop/run_all.ps1 -Only a,i` (two processes) and `-Only g,h` (three and four): the usual grid scenario `a_griglia` passed (62 checks on the guest and 49 on the host,
  as before); the new `i_aspetti` passed (66 and 53: the four new checks of each process are the characters). In it the host asks for the Custode
  at level 7 and the guest for the same look at level 3 (`-cooplook`, `-cooplevel`): the host keeps the Custode, the guest is given the Dottoressa,
  both see the same looks and levels, and each partner's body is built with the look and the level declared. The three- and four-player scenarios
  passed with the same numbers as on 17 November (62, 48 and 49; 62, 48, 48 and 49), so the greeting sent to players who are already in is exercised
  too. Not run on this protocol: the wing plan, the late join, the clean exit, the killed process and the host's exit.
- `PryingEyes.EditorTools.MenuReview.Run` (the editor, with graphics): 2790 checks, 0 failed. The photographs of the list with a suspended expedition,
  the creation panel, *Chi gioca?* and the expedition panel with the worst numbers (1,234,567 points and eleven resources of 9999) were looked at at
  1280x720, the leave panel with its three buttons at 1024x768; the other sizes only through the checks that every text fits its box and stays on
  the screen ([display-and-menu.md](display-and-menu.md#verification)).
- `python tools/check_suite.py`: 0 findings, with the new package, lab and pages.

## Not verified

- A human playthrough: whether a level every few expeditions and the two bonuses feel right (the numbers are a starting point, not a tuning).
- Two machines over a real network and a controller on the new panels: the co-op checks ran with every process on one PC (loopback) and the
  menu review drives the panels with the mouse path only.
- Resuming an expedition in split screen or online: it is not implemented, by design (see above).
- That every old save in the wild imports as expected: the lab imports documents with the layout the 0.8 game wrote (`LEGACY_REAL_FILE_SHAPE`)
  and a damaged one, not every save of every player.
- The sound of the new panels and the look of the banners on a monitor: only the photographs of the menu review were looked at.
