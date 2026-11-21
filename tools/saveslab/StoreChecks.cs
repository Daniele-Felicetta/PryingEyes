using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Production.Saves;

static partial class Program
{
    static readonly DateTime Noon = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    static JsonItem Hero(string name, int n) => JsonItem.Object().Set("name", name).Set("n", n);

    static void StoreChecks()
    {
        string folder = FakeFiles.P("saves");
        SaveStore Fresh(out FakeFiles fs, SaveFormat format = null) { fs = new FakeFiles(); return new SaveStore(folder, format ?? ThreeVersions(), fs, () => Noon); }
        string Main(string id) => FakeFiles.P(folder, id + ".json");
        string Under(string sub, string file) => FakeFiles.P(folder, sub, file);

        var store = Fresh(out var files);
        store.Save("ada", Hero("Ada", 1));
        var loaded = store.Load("ada");
        Check("STORE_ROUND_TRIP", loaded.Loadable && loaded.Result.Status == LoadStatus.Ok && loaded.Document.Str("name") == "Ada" && files.Data[Main("ada")].StartsWith("{\n  \"format\": \"lab.hero\""),
            loaded.Result.ToString(), "saved, read back, the file starts with its format");
        Check("STORE_MISSING_IS_NOT_AN_ERROR", store.Load("nobody").Result.Status == LoadStatus.Missing && !store.Exists("nobody") && store.Exists("ada"), "an id without a file", "Missing");

        store = Fresh(out files);
        store.Save("ada", Hero("Ada", 1));
        string first = files.Data[Main("ada")];
        files.Fail = (op, path) => { if (op == "replace") throw new IOException("the disk is full"); };
        bool threw = false;
        try { store.Save("ada", Hero("Ada", 2)); } catch (IOException) { threw = true; }
        Check("STORE_FAILED_WRITE_LEAVES_THE_OLD_FILE", threw && files.Data[Main("ada")] == first && !files.Exists(Main("ada") + ".tmp"), threw ? "the replace failed after the new text was written" : "no error",
            "the old file as it was, the temporary file gone, the error reported");

        store = Fresh(out files);
        store.Save("ada", Hero("Ada", 1));
        string v1 = files.Data[Main("ada")];
        store.Save("ada", Hero("Ada", 2));
        Check("STORE_KEEPS_THE_PREVIOUS_FILE_AS_BAK", files.Data[Main("ada") + ".bak"] == v1 && files.Data[Main("ada")] != v1 && store.Load("ada").Document.Int("n") == 2, "second save", "bak = the first save");
        files.Data[Main("ada")] = "{broken";
        store.Save("ada", Hero("Ada", 3));
        Check("STORE_DAMAGE_NEVER_BECOMES_THE_BACKUP", files.Data[Main("ada") + ".bak"] == v1 && store.Load("ada").Document.Int("n") == 3, "saved over a damaged file", "the good .bak survives");
        files.Data[Main("ada") + ".tmp"] = "half a file";
        Check("STORE_LEFTOVER_TEMP_FILE_IS_NOT_A_SAVE", string.Join(",", store.Ids()) == "ada", string.Join(",", store.Ids()), "a crash left ada.json.tmp: it is not listed");
        store.Save("ada", Hero("Ada", 4));
        Check("STORE_LEFTOVER_TEMP_FILE_IS_REPLACED", store.Load("ada").Document.Int("n") == 4 && !files.Exists(Main("ada") + ".tmp"), "the next save", "overwrites the leftover");

        store = Fresh(out files);
        files.Data[Main("ada")] = V1Text;
        var upgraded = store.Load("ada");
        string backup = Under(SaveStore.BackupFolder, "ada.v1.json");
        Check("STORE_UPGRADE_KEEPS_AN_OLD_COPY_FIRST", upgraded.Result.Status == LoadStatus.Upgraded && upgraded.BackupOfOld == backup && files.Data[backup] == V1Text, upgraded.Result.ToString(), "backup/ada.v1.json holds the old file");
        var again = store.Load("ada");
        Check("STORE_UPGRADE_IS_WRITTEN_BACK_ONCE", again.Result.Status == LoadStatus.Ok && again.Result.Applied.Count == 0 && files.Data[backup] == V1Text && again.Document.Str("title") == "Newcomer" && again.Document.Path("bank", "0", "n").AsInt() == 12,
            again.Result.ToString(), "the second read finds the current version");
        store = Fresh(out files);
        files.Data[Main("ada")] = V1Text;
        files.Data[backup] = "the first copy";
        store.Load("ada");
        Check("STORE_FIRST_BACKUP_IS_NEVER_REPLACED", files.Data[backup] == "the first copy", files.Data[backup], "an existing backup of that version stays");
        store = Fresh(out files);
        files.Data[Main("ada")] = V1Text;
        files.Fail = (op, path) => { if (op == "copy" && path.Contains(SaveStore.BackupFolder)) throw new IOException("denied"); };
        var noCopy = store.Load("ada");
        Check("STORE_NO_BACKUP_NO_REWRITE", noCopy.Loadable && noCopy.NotWritten && noCopy.BackupOfOld == null && files.Data[Main("ada")] == V1Text && noCopy.Document.Str("title") == "Newcomer",
            noCopy.Result.ToString(), "usable in memory, the old file untouched on disk");
        store = Fresh(out files, new SaveFormat("lab.hero", 3, new Migration(1, "fine", d => { }), new Migration(2, "explodes", d => throw new InvalidOperationException("boom"))));
        files.Data[Main("ada")] = V1Text;
        var broken = store.Load("ada");
        Check("STORE_FAILED_MIGRATION_LEAVES_THE_FILE", broken.Result.Status == LoadStatus.MigrationFailed && files.Data[Main("ada")] == V1Text && files.Data.Keys.Count(k => k.Contains(SaveStore.BackupFolder)) == 0, broken.Result.ToString(), "the file as it was");

        store = Fresh(out files);
        string cut = "{\"format\":\"lab.hero\",\"version\":3,\"na";
        files.Data[Main("ada")] = cut;
        var damaged = store.Load("ada");
        Check("STORE_DAMAGED_FILE_IS_QUARANTINED", damaged.Result.Status == LoadStatus.Corrupt && damaged.QuarantinedAs != null && files.Data[damaged.QuarantinedAs] == cut && !store.Exists("ada") && store.Ids().Count == 0 &&
            damaged.QuarantinedAs.Contains(SaveStore.QuarantineFolder), damaged.Result.ToString(), "moved aside byte for byte, never deleted");
        store = Fresh(out files);
        store.Save("ada", Hero("Ada", 1));
        store.Save("ada", Hero("Ada", 2));
        files.Data[Main("ada")] = cut;
        var recovered = store.Load("ada");
        Check("STORE_DAMAGED_FILE_RECOVERS_FROM_THE_BAK", recovered.Recovered && recovered.Loadable && recovered.Document.Int("n") == 1 && store.Load("ada").Document.Int("n") == 1 && files.Data[recovered.QuarantinedAs] == cut,
            recovered.Result.ToString(), "the previous version takes its place, the damaged one is kept aside");

        store = Fresh(out files);
        string newer = "{\"format\":\"lab.hero\",\"version\":9,\"name\":\"Zed\"}";
        files.Data[Main("ada")] = newer;
        var tooNew = store.Load("ada");
        bool refused = false;
        try { store.Save("ada", Hero("Ada", 1)); } catch (InvalidOperationException e) { refused = e.Message.Contains("SAVES_TOO_NEW"); }
        Check("STORE_NEWER_FILE_IS_NEVER_TOUCHED", tooNew.Result.Status == LoadStatus.TooNew && refused && files.Data[Main("ada")] == newer && !files.Exists(Main("ada") + ".bak") && files.Data.Count == 1, tooNew.Result.ToString(),
            "not read, not overwritten, no copies made");
        store = Fresh(out files);
        files.Data[Main("ada")] = V1Text;
        files.Fail = (op, path) => { if (op == "read" && path == Main("ada")) throw new IOException("in use by another program"); };
        var locked = store.Load("ada");
        Check("STORE_A_LOCKED_FILE_IS_NOT_A_DAMAGED_ONE", locked.Result.Status == LoadStatus.Unreadable && files.Data[Main("ada")] == V1Text && files.Data.Count == 1, locked.Result.ToString(), "Unreadable, left alone");
        store = Fresh(out files);
        files.Data[Main("ada")] = "﻿" + store.Format.Write(Hero("Ada", 7));
        Check("STORE_READS_A_FILE_SAVED_WITH_A_BOM", store.Load("ada").Document.Int("n") == 7, "a file an editor saved with a byte-order mark", "read");

        store = Fresh(out files);
        store.Save("ada", Hero("Ada", 1)); store.Save("ada", Hero("Ada", 2));
        string last = files.Data[Main("ada")], bak = files.Data[Main("ada") + ".bak"];
        bool moved = store.Delete("ada");
        string gone = Under(SaveStore.DeletedFolder, "ada.20261118T120000000Z.json");
        Check("STORE_DELETE_MOVES_AND_NEVER_DESTROYS", moved && !store.Exists("ada") && files.Data[gone] == last && files.Data[gone + ".bak"] == bak && !store.Delete("ada"), "delete, then delete again", "moved with its .bak; the second finds nothing");
        store.Save("ada", Hero("Ada", 3)); store.Delete("ada");
        Check("STORE_DELETES_OF_THE_SAME_MOMENT_DO_NOT_COLLIDE", files.Data.Keys.Count(k => k.Contains(SaveStore.DeletedFolder) && k.EndsWith(".json")) == 2 && files.Data.ContainsKey(Under(SaveStore.DeletedFolder, "ada.20261118T120000000Z-1.json")),
            string.Join(", ", files.Data.Keys.Where(k => k.Contains(SaveStore.DeletedFolder)).Select(Path.GetFileName)), "the second one gets a -1");

        store = Fresh(out files);
        foreach (string name in new[] { "e.json", "a.json", "b.json.bak", "c.json.tmp", "d.txt", "bad id.json", "A_b-9.JSON" }) files.Data[FakeFiles.P(folder, name)] = "x";
        Check("STORE_LISTS_ONLY_SAVES", string.Join(",", store.Ids()) == "A_b-9,a,e", string.Join(",", store.Ids()), "A_b-9,a,e (name order, no .bak, .tmp or odd names)");
        var accepted = new[] { "../x", "a/b", "a\\b", "", "x y", ".", "..", "a.b", new string('a', 65), null }.Where(bad => { try { store.PathOf(bad); return true; } catch (ArgumentException) { return false; } }).ToArray();
        Check("STORE_REFUSES_IDS_THAT_ARE_PATHS", accepted.Length == 0, accepted.Length == 0 ? "10 ids refused" : "accepted: " + string.Join(" | ", accepted), "an id is a file name: no separators, dots, spaces, empty or longer than 64");
        Check("STORE_ACCEPTS_PLAIN_IDS", SaveStore.ValidId("c-1a2b3c4d") && SaveStore.ValidId("A_b-9") && SaveStore.ValidId(new string('a', 64)), "c-1a2b3c4d, A_b-9", "valid");

        store = Fresh(out files);
        store.Save("a", Hero("A", 1)); files.Data[Main("b")] = "garbage"; files.Data[Main("c")] = newer;
        var all = store.LoadAll();
        Check("STORE_LOAD_ALL_REPORTS_EVERY_FILE", all.Count == 3 && all[0].Id == "a" && all[0].Loadable && all[1].Result.Status == LoadStatus.Corrupt && all[2].Result.Status == LoadStatus.TooNew, string.Join(",", all.Select(l => l.Id + ":" + l.Result.Status)),
            "a Ok, b Corrupt, c TooNew: a menu can say so");

        // ---- the real disk
        string disk = Scratch("disk");
        var real = new SaveStore(disk, ThreeVersions(), null, () => Noon);
        real.Save("ada", Hero("Ada è", 1)); real.Save("ada", Hero("Ada è", 2));
        Check("STORE_REAL_DISK_ROUND_TRIP", real.Load("ada").Document.Str("name") == "Ada è" && real.Load("ada").Document.Int("n") == 2 && File.Exists(Path.Combine(disk, "ada.json.bak")) && !File.Exists(Path.Combine(disk, "ada.json.tmp")) &&
            File.ReadAllBytes(Path.Combine(disk, "ada.json"))[0] == (byte)'{', "two saves on a real folder", "read back with its accent, a .bak, no .tmp, no byte-order mark");
        File.WriteAllText(Path.Combine(disk, "old.json"), V1Text);
        var diskUp = real.Load("old");
        Check("STORE_REAL_DISK_UPGRADE", diskUp.Result.Status == LoadStatus.Upgraded && File.Exists(Path.Combine(disk, SaveStore.BackupFolder, "old.v1.json")) && real.Load("old").Result.Status == LoadStatus.Ok, diskUp.Result.ToString(), "upgraded, old copy kept, written back");
        bool deletedOld = real.Delete("old");
        var inDeleted = Directory.GetFiles(Path.Combine(disk, SaveStore.DeletedFolder));
        Check("STORE_REAL_DISK_DELETE_KEEPS_THE_FILE", deletedOld && inDeleted.Count(f => f.EndsWith(".json")) == 1 && inDeleted.Count(f => f.EndsWith(".json.bak")) == 1 && !File.Exists(Path.Combine(disk, "old.json")) && !File.Exists(Path.Combine(disk, "old.json.bak")),
            string.Join(", ", inDeleted.Select(Path.GetFileName)), "the file and its .bak moved to deleted/");
        File.WriteAllText(Path.Combine(disk, "bad.json"), "{\"format\":\"lab.hero\",\"ver");
        var quarantined = real.Load("bad");
        Check("STORE_REAL_DISK_QUARANTINE", quarantined.QuarantinedAs != null && File.Exists(quarantined.QuarantinedAs) && !File.Exists(Path.Combine(disk, "bad.json")) && File.ReadAllText(quarantined.QuarantinedAs).StartsWith("{\"format\""),
            "a truncated file", "moved to quarantine/ with its content");

        if (OperatingSystem.IsWindows())
        {
            string lockedFolder = Scratch("locked");
            var lockedStore = new SaveStore(lockedFolder, ThreeVersions());
            lockedStore.Save("ada", Hero("Ada", 1));
            string target = Path.Combine(lockedFolder, "ada.json");
            using (var holder = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var release = Task.Run(() => { Thread.Sleep(250); holder.Dispose(); });
                Exception saveError = null;
                try { lockedStore.Save("ada", Hero("Ada", 2)); } catch (Exception e) { saveError = e; }
                release.Wait();
                Check("STORE_RETRIES_A_FILE_ANOTHER_PROGRAM_HOLDS", saveError == null && lockedStore.Load("ada").Document.Int("n") == 2, saveError == null ? "saved after the other program let go" : saveError.Message,
                    "a lock of a quarter of a second does not lose the save (Windows antivirus and backup tools do this)");
            }
        }

        foreach (string scratch in scratches) { try { Directory.Delete(scratch, true); } catch (Exception) { } }
    }
}
