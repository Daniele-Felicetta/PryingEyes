# Save system

How the game keeps what a player made, and how a save written by one version is read, upgraded and written by the next. The library is
`unity/PryingEyes/Packages/com.pryingeyes.saves` (no engine, no game names: [its README](../../unity/PryingEyes/Packages/com.pryingeyes.saves/README.md));
what the game keeps, and how it plays, is in [../game/saves-and-characters.md](../game/saves-and-characters.md). This page is for whoever
changes what a save contains: the files, the guarantees, the recipe, the version history, and how to check it.

The promise behind all of it, asked for on 18 November 2026: a player's characters and their game in progress must survive every new
version of the game. An old save is upgraded to the new shape (and gets whatever the new version added) the first time it is read, and a save
that this game does not understand is never destroyed.

## What is on disk

Everything lives in `persistentDataPath/characters/` (`characters-<profile>/` for the test profile and `-profilo NAME`, see
[the game page](../game/saves-and-characters.md#profiles)). `<id>` is `c-` and eight hexadecimal digits: the file name is the character's identity.

| Path | Kind, version | What it is |
| --- | --- | --- |
| `<id>.json` | `pryingeyes.character` v1 | One character: name, look, experience, counters, its cathedral (store, works, market, blessing, remains) and `extras`. |
| `<id>.json.bak` | | The file as it was before the last write. |
| `backup/<id>.vN.json` | | A copy of the file as it was before it was upgraded from version N. Never replaced. |
| `quarantine/` | | Files that could not be understood. Moved here, never deleted. |
| `deleted/` | | Characters the player deleted, with a time stamp in the name. They go back by moving the file out. |
| `runs/<id>.json` | `pryingeyes.run` v1 | The expedition that character suspended with *Salva e torna al menu* (at most one). |
| `runs/consumed/<id>.json` | | The last suspended expedition that was resumed or abandoned (one per character, the new one replaces the old). |
| `legacy-imported.txt` | | A marker: the old `cattedrale.json` of the versions before characters was already turned into a character. |

The library writes every file the same way: to a temporary file that then replaces the old one (a failed write leaves the old file as it was),
with the file it replaces kept once as `.bak`. A file another program holds for a moment (a backup tool, a second window of the game) is tried again
for about a second before giving up, because Windows does this often.

## What happens when a file is read

`SaveStore.Load` answers one of nine states and never throws for bad input (`LoadStatus` in `Runtime/SaveFormat.cs`):

| State | Meaning | What the game does |
| --- | --- | --- |
| `Ok` | The file is of the current version. | Reads it. |
| `Upgraded` | The file is older: it went through the chain of steps. | Copies the old file to `backup/` **first**, reads the upgraded document and writes it back once. |
| `Missing` | There is no such file. | Nothing (a new character, no suspended expedition). |
| `Unreadable` | The disk refused (locked, denied). The file is fine. | Skips it this time; the character list says so. |
| `Corrupt` | Not JSON, or not shaped like a save (no version, not an object). | Moves the file to `quarantine/`; if the `.bak` is a good save it takes the place (`Recovered`). |
| `WrongFormat` | Valid JSON, but another kind of document. | Left where it is; the character list says so. |
| `TooNew` | Written by a newer game. | **Never opened, never overwritten.** The character list says "e' di una versione piu' nuova del gioco (non lo tocco)", and saving over it is refused. Going back to an older game never loses a newer save. |
| `TooOld` | Older than the oldest version the chain can still upgrade. | Left where it is. It cannot happen while the chain starts at version 1. |
| `MigrationFailed` | A step threw. | Left where it is; nothing is written. |

After the library, the game reads the document **without trusting it** (`CharacterRecord.FromJson`, `RunRecord.FromJson`): a missing
or mistyped field takes its starting value, a number goes back inside its limits, a name is cleaned, a look that does not exist becomes the
first, the level is recomputed from the experience. A hand-edited or half-damaged file therefore loads as a valid character instead of an error.

## Changing what a save contains

There are three kinds of change. Pick the first that fits.

**1. New data a system needs: usually no migration.**

- *Something the cathedral learned* (a new work, a new resource, a new market offer): the cathedral subtree of the file is read into
  `Fabbrica.Dati` with `JsonUtility`, so a field the file does not have takes its default. Works are saved by id: an id this version does
  not know (written by a newer one) is kept in the file on every save (`Fabbrica.Stato.sconosciute`), and a new work simply starts unbuilt in
  an old save. Resources are strings in the store, so a new one needs nothing.
- *A new system* (a talent tree, achievements, a pet): its data goes under its own key of the character's `extras`. The file already has `extras`,
  so no version changes and no old file is rewritten. Read it with a default for every field.
- *A new counter*: add it to `CharacterStats` with the same default as a missing field (zero).
- *A suspended expedition that needs to remember a new thing*: its state is a tree with one key per system (`RunRecord.state`: weapons, perks,
  magic, equipment, and the areas bought on the level; `RunSave.CaptureKit`/`ApplyKit`, `FloorDirector.CaptureAreas`/`RestoreAreas`). A new
  system adds a key; an expedition saved before has no such key and the system starts from its default.
- *Content referenced by name* (a weapon in a suspended expedition) is saved by id (`WeaponDef.id`), never by position in a list, because a
  new version may add one in the middle. A weapon the game no longer has leaves its slot empty.

**2. A change of shape or of meaning of what is already saved: bump the version and add one step.** Rename a field, split one in two, change
a unit, move a block:

```csharp
// CharacterRecord.cs
public const int Version = 2;                                   // was 1

public static readonly SaveFormat Format = new SaveFormat(Kind, Version,
    new Migration(1, "stats.kills is renamed stats.totalKills and a title is added", doc =>
    {
        doc.ObjectAt("stats").Rename("kills", "totalKills");     // the field keeps its place in the file
        doc.SetDefault("title", "Newcomer");                      // written only if the file has none
    }));
```

and in `CharacterRecord.FromJson`/`ToJson` the field is now read and written under its new name.

1. Change `Version` and add **one** `Migration(oldVersion, "one line", doc => ...)`. A step works on the JSON tree (`JsonItem`), never on a
   typed class, so it keeps working after the classes change. It may only add, move or fix: an old save has to come out as the same save plus
   what the new version needs (`SetDefault` writes a field only if the file does not have it).
2. Never edit a step that has been released: a file at that version may be on someone's disk. A mistake in a released step is corrected by a
   later step.
3. In `tools/saveslab/CharacterChecks.cs` (or `RunChecks.cs`) add a **document in the old shape** (copy a real file from the released game)
   and check what comes out; the library checks that the chain has no gap and that every step is documented (`SAVES_CHAIN_GAP`, `SAVES_STEP_*`).
4. Run `python tools/studio.py test saves`, and add a row to the version history below.

**3. Removing something:** do not. A field the game no longer reads is harmless in the file; if it must go, a step removes it.

Two things a new version must never do: read a save with a typed class before it has been upgraded, and write a save in a shape an older
version of the same `Version` would misread. If an older game could misread it, the version changes, and the older game then refuses the file
(`TooNew`) instead of damaging it.

## Version history

| Kind | Version | Game | What it is |
| --- | --- | --- | --- |
| `pryingeyes.character` | 1 | 0.9.0 | The first character file. The old `cattedrale.json` of the versions before (the whole cathedral of the PC in one file, `"versione": 1`) is imported once as the character *Il Custode*, with the experience its history is worth, and stays where it is (an older game still reads it). |
| `pryingeyes.run` | 1 | 0.9.0 | The first suspended-expedition file. |

## Profiles and tests

An automated run (`-autotest`, `-guntest`, any `-batchmode` process such as the Unity builds and the menu review) and `-profilo NAME` use a folder
of their own and never touch the player's characters: no preferences, no import of the old file, no level bonuses (`Roster.IsTest`), a test
character `prova` (`prova-<name>` for the processes of a co-op test). `Fabbrica.Azzera` restarts that character from zero and
`-chartest` deletes the test folder first, and refuses to run on any other folder.

## How it is checked

`python tools/studio.py test saves` (the same as `dotnet run -c Release --project tools/saveslab`, also in the CI gate) compiles the
library files and the pure files of the game (`Scripts/Characters/CharacterLooks.cs`, `Leveling.cs`, `CharacterRecord.cs`, `CathedralJson.cs`,
`RunRecord.cs`) **from the same paths** that Unity compiles, under .NET, and runs 115 checks:

| Group | Checks | What they prove |
| --- | --- | --- |
| JSON (`JsonChecks.cs`) | 24 | The strict reader and the writer: field order is kept, a number keeps its text (an integer above 2^53 survives), escapes and Unicode, a byte-order mark is skipped, duplicate fields and bad text are reported with the line and column, nesting is bounded, a missing or mistyped field reads as the fallback. |
| Format (`FormatChecks.cs`) | 13 | The chain: a file is upgraded step by step from any version, from the middle too; the current version is read as it is; a newer one is refused, an older one than the chain knows is refused, another kind of document is refused; damage is a state, never an exception; a failing step is reported; the written document carries its format and version first. |
| Store (`StoreChecks.cs`) | 28 | The folder: round trip, the failed write that leaves the old file, `.bak`, a damaged file that never becomes the backup, a leftover temporary file, the copy before an upgrade (the first backup is never replaced), a damaged file that is quarantined and recovered from the `.bak`, a newer file that is never touched, a locked file that is retried and not mistaken for a damaged one, a file saved with a byte-order mark, deletes that move and never collide, ids that are paths are refused, and five checks on a real disk. |
| Characters (`CharacterChecks.cs`) | 33 | The level curve and its inverse, the bonuses and their caps, the pace (a few expeditions per level), the four looks and that two players never share one, the names (cleaned, unique ignoring case, a free variant that stays within 20 characters), a character that round-trips, damage that reads as defaults, unknown cathedral fields and works that survive, tomorrow's file refused, and the real shape of the old `cattedrale.json` becoming the first character (its history worth experience that cannot go negative). |
| Run (`RunChecks.cs`) | 6 | A suspended expedition that round-trips with its backpack, the equipment of every system and the extras, damage read as safe values, the floor bounded, one file per character in the store, a newer file never touched. |

Unity-side checks, which need a built player or the editor: `-chartest` (the real game on the test profile), `-cattedraletest` (a whole
expedition including the suspended one), `-seatstest` (the character of each seat), a co-op scenario, `MenuReview` (the new panels). They are
listed in [testing.md](testing.md) and their results in [the game page](../game/saves-and-characters.md#verification).

## Using it in another project

Copy nothing: reference the package. `unity/LayoutDemo` does (`SavesDemo.cs`: a counter that survives a change of format, written to show the
library from a project that has no game in it). `python tools/check_suite.py` fails if the package names the game or the engine
(AGENTS.md rule 1).

Not verified: `SavesDemo.cs` has not been opened in the LayoutDemo Unity project (that project was not opened); the library is checked under .NET, and the
game ran it on Windows only.
