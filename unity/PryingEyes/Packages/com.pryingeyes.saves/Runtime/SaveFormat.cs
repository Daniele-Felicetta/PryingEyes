using System;
using System.Collections.Generic;

namespace Production.Saves
{
    /// <summary>One step of a chain: turns a document of version <see cref="From"/> into one of version From + 1, in place. A step
    /// works on the JSON tree, never on a typed class, so it keeps working after the classes of the game change. Write it so it can
    /// only add, move or fix: an old save must come out as the same save, plus what the new version needs.</summary>
    public sealed class Migration
    {
        public int From { get; }
        public int To => From + 1;
        /// <summary>One line saying what the step does; it is shown in the log and in the report of an upgrade.</summary>
        public string Note { get; }
        public Action<JsonItem> Apply { get; }

        public Migration(int from, string note, Action<JsonItem> apply)
        {
            if (from < 1) throw new ArgumentException("SAVES_STEP_RANGE: versions start at 1");
            if (string.IsNullOrWhiteSpace(note)) throw new ArgumentException("SAVES_STEP_NOTE: say what the step does");
            From = from; Note = note.Trim();
            Apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }
    }

    public enum LoadStatus
    {
        /// <summary>Read as it is: the file is of the current version.</summary>
        Ok,
        /// <summary>Read and upgraded from an older version.</summary>
        Upgraded,
        /// <summary>There is no such file (store only).</summary>
        Missing,
        /// <summary>The file could not be read from disk (store only): locked or denied. The file itself is fine.</summary>
        Unreadable,
        /// <summary>Not JSON, or not shaped like a save (no version, not an object).</summary>
        Corrupt,
        /// <summary>Valid JSON, but another kind of document.</summary>
        WrongFormat,
        /// <summary>Written by a newer game than this one: never opened and never overwritten.</summary>
        TooNew,
        /// <summary>Older than the oldest version the chain can still upgrade.</summary>
        TooOld,
        /// <summary>A step threw: the document is not returned and the file is left alone.</summary>
        MigrationFailed,
    }

    /// <summary>What reading a document found. <see cref="Document"/> is set only when <see cref="Loadable"/>, and is then always of the
    /// current version.</summary>
    public sealed class LoadResult
    {
        public LoadStatus Status { get; }
        public JsonItem Document { get; }
        /// <summary>The version the file was written with (0 when it could not be told).</summary>
        public int FoundVersion { get; }
        /// <summary>The notes of the steps that ran, oldest first ("v1 -> v2: ...").</summary>
        public IReadOnlyList<string> Applied { get; }
        public string Error { get; }
        public bool Loadable => Status == LoadStatus.Ok || Status == LoadStatus.Upgraded;

        public LoadResult(LoadStatus status, JsonItem document = null, int foundVersion = 0, IReadOnlyList<string> applied = null, string error = null)
        {
            Status = status; Document = document; FoundVersion = foundVersion; Error = error ?? "";
            Applied = applied ?? Array.Empty<string>();
        }

        public override string ToString() => Status + (Error.Length > 0 ? ": " + Error : "") + (Applied.Count > 0 ? " [" + string.Join("; ", Applied) + "]" : "");
    }

    /// <summary>A kind of save document and how old versions of it become the current one. Every document is an object that starts with
    /// <c>"format": name</c> and <c>"version": n</c>; the rest is the caller's. The version only goes up: a release that changes what a
    /// save means raises <see cref="Current"/> and adds one <see cref="Migration"/> from the version before.
    /// <para>The chain is checked when the format is built, so a forgotten step fails at start-up and not on a player's disk: the
    /// steps must start at their lowest version and have no gap up to Current - 1.</para></summary>
    public sealed class SaveFormat
    {
        public const string FormatKey = "format";
        public const string VersionKey = "version";

        readonly Dictionary<int, Migration> byFrom = new Dictionary<int, Migration>();

        public string Name { get; }
        /// <summary>The version this build writes.</summary>
        public int Current { get; }
        /// <summary>The oldest version this build can still read (the first step's From; Current when there are no steps).</summary>
        public int Oldest { get; }
        public IReadOnlyList<Migration> Steps { get; }

        public SaveFormat(string name, int current, params Migration[] steps)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("SAVES_FORMAT_NAME: a format needs a name");
            if (current < 1) throw new ArgumentException("SAVES_FORMAT_VERSION: versions start at 1");
            Name = name; Current = current;
            steps = steps ?? Array.Empty<Migration>();
            int lowest = current;
            foreach (var step in steps)
            {
                if (step == null) throw new ArgumentException("SAVES_STEP_MISSING: a null step");
                if (step.To > current) throw new ArgumentException($"SAVES_STEP_BEYOND_CURRENT: step v{step.From} leads to v{step.To}, past the current v{current}");
                if (byFrom.ContainsKey(step.From)) throw new ArgumentException($"SAVES_STEP_DUPLICATE: two steps leave v{step.From}");
                byFrom[step.From] = step;
                if (step.From < lowest) lowest = step.From;
            }
            for (int v = lowest; v < current; v++)
                if (!byFrom.ContainsKey(v)) throw new ArgumentException($"SAVES_CHAIN_GAP: nothing upgrades v{v} (the chain runs from v{lowest} to v{current})");
            Oldest = lowest;
            var ordered = new List<Migration>(byFrom.Values);
            ordered.Sort((a, b) => a.From.CompareTo(b.From));
            Steps = ordered;
        }

        /// <summary>Reads the text of a document and upgrades it to the current version. Never throws for bad input.</summary>
        public LoadResult Read(string json)
        {
            if (json == null) return new LoadResult(LoadStatus.Corrupt, error: "no text");
            JsonItem document;
            try { document = JsonItem.Parse(json); }
            catch (JsonSyntaxException e) { return new LoadResult(LoadStatus.Corrupt, error: e.Message); }
            return Upgrade(document);
        }

        /// <summary>Upgrades a parsed document in place (a failed step leaves it half-done: use a clone if you keep it).</summary>
        public LoadResult Upgrade(JsonItem document)
        {
            if (document == null || !document.IsObject) return new LoadResult(LoadStatus.Corrupt, error: "the document is not an object");
            var format = document.Get(FormatKey);
            if (format == null || !format.IsString) return new LoadResult(LoadStatus.WrongFormat, error: "the document has no '" + FormatKey + "' field");
            if (format.AsString() != Name) return new LoadResult(LoadStatus.WrongFormat, error: "the document is '" + format.AsString() + "', not '" + Name + "'");
            var versionItem = document.Get(VersionKey);
            if (versionItem == null || !versionItem.IsNumber) return new LoadResult(LoadStatus.Corrupt, error: "the document has no '" + VersionKey + "' number");
            long found = versionItem.AsLong(0);
            if (found < 1 || found > int.MaxValue || versionItem.AsDouble() != found) return new LoadResult(LoadStatus.Corrupt, error: "'" + VersionKey + "' is not a whole number from 1");
            int version = (int)found;
            if (version > Current) return new LoadResult(LoadStatus.TooNew, foundVersion: version, error: $"written by a newer game (v{version}; this one writes v{Current})");
            if (version < Oldest) return new LoadResult(LoadStatus.TooOld, foundVersion: version, error: $"v{version} is older than the oldest upgradable version (v{Oldest})");
            var applied = new List<string>();
            while (version < Current)
            {
                var step = byFrom[version];
                try { step.Apply(document); }
                catch (Exception e)
                {
                    return new LoadResult(LoadStatus.MigrationFailed, foundVersion: (int)found, applied: applied,
                        error: $"step v{step.From} -> v{step.To} ({step.Note}) failed: {e.GetType().Name}: {e.Message}");
                }
                version = step.To;
                document.Set(VersionKey, version);
                applied.Add($"v{step.From} -> v{step.To}: {step.Note}");
            }
            return new LoadResult(applied.Count == 0 ? LoadStatus.Ok : LoadStatus.Upgraded, document, (int)found, applied);
        }

        /// <summary>A copy of the document that starts with this format's name and version, then holds every other field of the original.
        /// The original is not changed (its children are shared, so write the copy out before changing them).</summary>
        public JsonItem Stamp(JsonItem document)
        {
            if (document == null || !document.IsObject) throw new ArgumentException("SAVES_NOT_AN_OBJECT: a save document is a JSON object");
            var stamped = JsonItem.Object().Set(FormatKey, Name).Set(VersionKey, Current);
            foreach (string key in document.Keys)
                if (key != FormatKey && key != VersionKey) stamped.Set(key, document.Get(key));
            return stamped;
        }

        /// <summary>The text to put in a file for this document (format and version first).</summary>
        public string Write(JsonItem document, bool indented = true) => Stamp(document).ToJson(indented);
    }
}
