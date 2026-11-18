using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Production.Saves
{
    /// <summary>What a <see cref="SaveStore"/> needs from a disk. The real one is <see cref="DiskSaveFiles"/>; a test can pass one that
    /// fails on purpose.</summary>
    public interface ISaveFiles
    {
        bool Exists(string path);
        /// <summary>The whole file as UTF-8 text.</summary>
        string ReadAllText(string path);
        /// <summary>Creates or overwrites the file with UTF-8 text (no byte-order mark).</summary>
        void WriteAllText(string path, string text);
        /// <summary>Moves a file; a file already at the destination is replaced.</summary>
        void Move(string from, string to);
        /// <summary>Puts 'source' in place of 'destination' in one step where the file system can (the destination may not exist yet).</summary>
        void Replace(string source, string destination);
        /// <summary>Copies a file; a file already at the destination is overwritten.</summary>
        void Copy(string from, string to);
        /// <summary>Deletes a file (nothing happens if it is not there).</summary>
        void Delete(string path);
        void CreateDirectory(string path);
        /// <summary>The full paths of the files directly inside a folder (empty when the folder does not exist).</summary>
        IReadOnlyList<string> Files(string folder);
    }

    /// <summary>The real disk. A move, replace or copy that fails because another program has the file open for a moment (an antivirus,
    /// a backup tool, a second window of the game) is tried again for about a second before it gives up; Windows does this often.</summary>
    public sealed class DiskSaveFiles : ISaveFiles
    {
        const int Attempts = 8;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        static void Retry(Action action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { action(); return; }
                catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && !(e is FileNotFoundException) && !(e is DirectoryNotFoundException) && attempt < Attempts)
                {
                    Thread.Sleep(40 * attempt);
                }
            }
        }

        public bool Exists(string path) => File.Exists(path);
        public string ReadAllText(string path) { string text = null; Retry(() => text = File.ReadAllText(path, Utf8)); return text; }
        public void WriteAllText(string path, string text) => Retry(() => File.WriteAllText(path, text, Utf8));
        public void Move(string from, string to) => Retry(() => { if (File.Exists(to)) File.Delete(to); File.Move(from, to); });
        public void Replace(string source, string destination) =>
            Retry(() => { if (File.Exists(destination)) File.Replace(source, destination, null); else File.Move(source, destination); });
        public void Copy(string from, string to) => Retry(() => File.Copy(from, to, true));
        public void Delete(string path) => Retry(() => File.Delete(path));
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public IReadOnlyList<string> Files(string folder) => Directory.Exists(folder) ? Directory.GetFiles(folder) : Array.Empty<string>();
    }

    /// <summary>What <see cref="SaveStore.Load"/> found and did.</summary>
    public sealed class StoreLoad
    {
        public string Id { get; }
        public LoadResult Result { get; internal set; }
        /// <summary>The file was unusable and the document comes from the previous version of it (<c>.bak</c>).</summary>
        public bool Recovered { get; internal set; }
        /// <summary>Where an unusable file was moved to (null when nothing was moved).</summary>
        public string QuarantinedAs { get; internal set; }
        /// <summary>The copy of the file as it was before an upgrade (null when nothing was upgraded).</summary>
        public string BackupOfOld { get; internal set; }
        /// <summary>The document is fine in memory but could not be written back (the file on disk stays as it was).</summary>
        public bool NotWritten { get; internal set; }
        public bool Loadable => Result != null && Result.Loadable;
        public JsonItem Document => Loadable ? Result.Document : null;

        internal StoreLoad(string id) { Id = id; }
    }

    /// <summary>A folder of save documents of one <see cref="SaveFormat"/>, one file per id (<c>id.json</c>).
    /// <list type="bullet">
    /// <item>Writing is atomic: the new text goes to a temporary file that then replaces the old one, so a crash leaves the old file or the
    /// new one, never half of a file. The version being replaced is kept once as <c>id.json.bak</c>.</item>
    /// <item>Reading upgrades an old file through the format's chain, keeps a copy of the old file in <c>backup/id.vN.json</c> first, and
    /// writes the upgraded file back. If that copy cannot be made the file is not touched.</item>
    /// <item>A file that cannot be understood is moved to <c>quarantine/</c> (never deleted) and the <c>.bak</c> takes its place if it is
    /// good. A file written by a newer game is neither read nor overwritten.</item>
    /// <item>Deleting moves the file to <c>deleted/</c>: nothing a player made is ever destroyed by the store.</item>
    /// </list></summary>
    public sealed class SaveStore
    {
        public const string BackupFolder = "backup", QuarantineFolder = "quarantine", DeletedFolder = "deleted";

        readonly ISaveFiles files;
        readonly Func<DateTime> clock;

        public string Folder { get; }
        public SaveFormat Format { get; }
        /// <summary>Called with a line for everything unusual the store does (an upgrade, a quarantine, a recovery, a failed copy).</summary>
        public Action<string> Log { get; set; }

        public SaveStore(string folder, SaveFormat format, ISaveFiles files = null, Func<DateTime> clock = null)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("SAVES_FOLDER: a store needs a folder");
            Folder = folder;
            Format = format ?? throw new ArgumentNullException(nameof(format));
            this.files = files ?? new DiskSaveFiles();
            this.clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>Letters, digits, '-' and '_', up to 64: an id is a file name, never a path.</summary>
        public static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (char c in id) if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
            return true;
        }

        static string Checked(string id)
        {
            if (!ValidId(id)) throw new ArgumentException("SAVES_BAD_ID: an id is letters, digits, '-' and '_' (up to 64 of them)");
            return id;
        }

        public string PathOf(string id) => Path.Combine(Folder, Checked(id) + ".json");
        public bool Exists(string id) => files.Exists(PathOf(id));
        string Sub(string folder, string file) => Path.Combine(Path.Combine(Folder, folder), file);
        string Stamp() => clock().ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);

        string Unique(string path)
        {
            if (!files.Exists(path)) return path;
            string stem = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path)), ext = Path.GetExtension(path);
            for (int n = 1; ; n++) { string candidate = stem + "-" + n + ext; if (!files.Exists(candidate)) return candidate; }
        }

        void Say(string line) => Log?.Invoke(line);

        /// <summary>The ids that have a file, in name order.</summary>
        public IReadOnlyList<string> Ids()
        {
            var ids = new List<string>();
            foreach (string file in files.Files(Folder))
            {
                if (!string.Equals(Path.GetExtension(file), ".json", StringComparison.OrdinalIgnoreCase)) continue;
                string stem = Path.GetFileNameWithoutExtension(file);
                if (ValidId(stem)) ids.Add(stem);
            }
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>Reads, upgrades and (unless 'persistUpgrade' is false) rewrites one document. See the class summary for what happens to
        /// files that are old, damaged or too new.</summary>
        public StoreLoad Load(string id, bool persistUpgrade = true)
        {
            string path = PathOf(id);
            var outcome = new StoreLoad(id);
            if (!files.Exists(path)) { outcome.Result = new LoadResult(LoadStatus.Missing, error: "no file"); return outcome; }
            string text;
            try { text = files.ReadAllText(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                outcome.Result = new LoadResult(LoadStatus.Unreadable, error: e.Message);
                return outcome;
            }
            outcome.Result = Format.Read(text);
            if (outcome.Result.Status == LoadStatus.Upgraded) KeepAndRewrite(id, path, outcome, persistUpgrade);
            else if (outcome.Result.Status == LoadStatus.Corrupt) Recover(id, path, outcome);
            return outcome;
        }

        /// <summary>Loads every document (upgrading the old ones). Unusable files come back as outcomes too, so a menu can say so.</summary>
        public IReadOnlyList<StoreLoad> LoadAll()
        {
            var all = new List<StoreLoad>();
            foreach (string id in Ids()) all.Add(Load(id));
            return all;
        }

        void KeepAndRewrite(string id, string path, StoreLoad outcome, bool persist)
        {
            var result = outcome.Result;
            Say($"{id}: upgraded from v{result.FoundVersion} to v{Format.Current}: {string.Join("; ", result.Applied)}");
            if (!persist) return;
            string keep = Sub(BackupFolder, id + ".v" + result.FoundVersion + ".json");
            try
            {
                files.CreateDirectory(Path.Combine(Folder, BackupFolder));
                if (!files.Exists(keep)) files.Copy(path, keep);
                outcome.BackupOfOld = keep;
                Save(id, result.Document);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                outcome.NotWritten = true;
                Say($"{id}: the upgraded file was not written ({e.Message}); the old file is untouched");
            }
        }

        void Recover(string id, string path, StoreLoad outcome)
        {
            Say($"{id}: unreadable ({outcome.Result.Error})");
            string aside = Unique(Sub(QuarantineFolder, id + "." + Stamp() + ".json"));
            try
            {
                files.CreateDirectory(Path.Combine(Folder, QuarantineFolder));
                files.Move(path, aside);
                outcome.QuarantinedAs = aside;
                Say($"{id}: the damaged file was moved to {aside}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Say($"{id}: the damaged file could not be moved ({e.Message})"); }
            string bak = path + ".bak";
            if (!files.Exists(bak)) return;
            LoadResult previous;
            try { previous = Format.Read(files.ReadAllText(bak)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return; }
            if (!previous.Loadable) return;
            outcome.Result = previous;
            outcome.Recovered = true;
            Say($"{id}: restored from the previous version of the file");
            try { Save(id, previous.Document); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { outcome.NotWritten = true; }
        }

        /// <summary>Writes a document (format and version stamped on). Throws <see cref="InvalidOperationException"/> if the file there was
        /// written by a newer game, and an IOException if the disk refuses; either way the old file is as it was.</summary>
        public void Save(string id, JsonItem document)
        {
            string path = PathOf(id);
            string text = Format.Write(document);
            files.CreateDirectory(Folder);
            if (files.Exists(path))
            {
                LoadResult current = null;
                try { current = Format.Read(files.ReadAllText(path)); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Say($"{id}: the old file could not be read before saving ({e.Message})"); }
                if (current != null && current.Status == LoadStatus.TooNew)
                    throw new InvalidOperationException($"SAVES_TOO_NEW: {id} was written by a newer game and is not overwritten ({current.Error})");
                if (current != null && current.Loadable)
                {
                    try { files.Copy(path, path + ".bak"); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Say($"{id}: no backup copy ({e.Message})"); }
                }
            }
            string tmp = path + ".tmp";
            try
            {
                files.WriteAllText(tmp, text);
                files.Replace(tmp, path);
            }
            catch
            {
                try { files.Delete(tmp); } catch (Exception) { }
                throw;
            }
        }

        /// <summary>Moves the document to <c>deleted/</c>. False if there was none.</summary>
        public bool Delete(string id)
        {
            string path = PathOf(id);
            if (!files.Exists(path)) return false;
            string target = Unique(Sub(DeletedFolder, id + "." + Stamp() + ".json"));
            files.CreateDirectory(Path.Combine(Folder, DeletedFolder));
            files.Move(path, target);
            if (files.Exists(path + ".bak")) files.Move(path + ".bak", target + ".bak");
            Say($"{id}: moved to {target}");
            return true;
        }
    }
}
