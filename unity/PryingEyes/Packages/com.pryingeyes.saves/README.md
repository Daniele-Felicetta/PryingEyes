# Production Saves

Engine-independent save files that can change shape between versions of a game without losing anyone's progress: a JSON tree, a
format with a version and a chain of migrations, and a folder store that writes atomically and never destroys what a player made.
The caller owns what a save contains and what each version changed; the package owns reading, upgrading and writing it safely.

```csharp
var format = new SaveFormat("my.character", current: 3,
    new Migration(1, "gold becomes a resource", doc => { var bank = doc.ArrayAt("bank"); /* ... */ }),
    new Migration(2, "new field: title", doc => doc.SetDefault("title", "Newcomer")));

var store = new SaveStore(folder, format);
var loaded = store.Load("ada");          // a v1 file is upgraded v1 -> v2 -> v3, the old file is kept in backup/, v3 is written back
if (loaded.Loadable) Use(loaded.Document);   // always the current version
store.Save("ada", document);             // atomic; the previous file is kept as ada.json.bak
```

| Type | What it owns | Guarantees |
| --- | --- | --- |
| `JsonItem` | A JSON value as a tree: read, edit, write. | Strict reader (no comments, no trailing commas, nothing after the value, nesting limit) that reports line and column; fields keep their order; a number keeps its text, so an integer above 2^53 survives; a missing or mistyped field reads as the fallback you give. |
| `Migration` | One step from version `n` to `n + 1`, in place, on the tree. | A step never sees a typed class, so it keeps working after the classes change. |
| `SaveFormat` | The name of a kind of document, the version this build writes and the chain of steps. | Checked at construction: no gap, no duplicate, no step past the current version (`SAVES_CHAIN_GAP`, `SAVES_STEP_*`). `Read` never throws for bad input: it answers `Ok`, `Upgraded`, `Corrupt`, `WrongFormat`, `TooNew`, `TooOld` or `MigrationFailed`. |
| `SaveStore` | A folder of documents, one `id.json` each. | Writes go to a temporary file that replaces the old one; the file replaced is kept once as `.bak`. An upgrade first copies the old file to `backup/id.vN.json` and leaves the file untouched if it cannot. A damaged file moves to `quarantine/` and the `.bak` takes over if it is good. A file from a newer game is neither read nor overwritten. Deleting moves to `deleted/`. A file that another program holds for a moment is retried for about a second. |

No engine references, no clock except the one the store is given, no game names, no idea of what is inside a save. `tools/saveslab`
compiles these exact files under .NET together with the game's own save schema and checks every guarantee above
(`python tools/studio.py test saves`, also in the CI gate); `unity/LayoutDemo` consumes the package in a separate Unity project
(`SavesDemo.cs`: a counter that survives a change of format). How a game adds a version is in `docs/engineering/save-system.md`.
