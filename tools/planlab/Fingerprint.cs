using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PryingEyes.Proc;

// The fingerprint of the floor generator: the SHA-256 of the text `dump` writes for a fixed set of plans, in the lab's default layout and in
// the layout the game uses (waves). In co-op every player builds the floor from the seed, so the same seed must give the same plan on every
// machine: a change that moves one hash must raise CoopProtocol.Revision (Scripts/Coop/CoopWire.cs).
//   fingerprint [--report FILE]    one line per plan; the report holds the plans and the revision read from CoopWire.cs
//   fingerprint --update FIXTURE   rewrite the fixture (tools/fixtures/plan-fingerprint.json). Refused, PLAN_CHANGED_WITHOUT_REVISION, when a
//                                  plan changed and the revision is still the one the fixture was made at
// `python tools/studio.py test plan` compares the report with the fixture. The text is hashed in the invariant culture (dump itself prints
// numbers in the culture of the machine) with its lines joined by LF, so the hash is the same on every machine.
static class Fingerprint
{
    static readonly int[] Seeds = { 1000, 2961, 3046, 777, 114, 4242 };
    static readonly int[] Floors = { 1, 3, 5, 8 };
    static readonly string[] Layouts = { "default", "waves" };
    const string CoopWire = "unity/PryingEyes/Assets/_PryingEyes/Scripts/Coop/CoopWire.cs";

    sealed class Print
    {
        public string layout; public int seed, floor; public string sha256;
        public string Key => layout + " " + seed + " " + floor;
        public string Name => layout + " seed " + seed + " floor " + floor;
    }

    public static int Run(string[] a, bool modified)
    {
        string report = null, update = null;
        for (int i = 1; i < a.Length; i++)
        {
            if (a[i] == "--report" && i + 1 < a.Length) report = a[++i];
            else if (a[i] == "--update" && i + 1 < a.Length) update = a[++i];
            else { Console.Error.WriteLine("usage: fingerprint [--report FILE] | fingerprint --update FIXTURE"); return 2; }
        }
        if (modified) { Console.Error.WriteLine("fingerprint takes no old, theme=, guardaroba= or salt=: it hashes the plans of the game as they are"); return 2; }
        string revision = ReadRevision();
        var prints = Compute();
        foreach (var p in prints) Console.WriteLine($"{p.layout,-7} seed {p.seed,4} floor {p.floor} {p.sha256}");
        if (update != null) return Update(update, revision, prints);
        if (report != null)
        {
            var plans = prints.ConvertAll(p => new { p.layout, p.seed, p.floor, p.sha256 });
            File.WriteAllText(report, JsonSerializer.Serialize(new { schemaVersion = 1, operation = "plan.fingerprint", ok = true, revision, plans }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return 0;
    }

    static List<Print> Compute()
    {
        var culture = CultureInfo.CurrentCulture; bool layout = PlanGen.SurvivalLayout;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var prints = new List<Print>();
            foreach (var name in Layouts)
            {
                PlanGen.SurvivalLayout = name == "waves";
                foreach (int seed in Seeds)
                    foreach (int floor in Floors)
                        prints.Add(new Print { layout = name, seed = seed, floor = floor, sha256 = Hash(seed, floor) });
            }
            return prints;
        }
        finally { CultureInfo.CurrentCulture = culture; PlanGen.SurvivalLayout = layout; }
    }

    // What dump writes, or what it says when the generator gives up: that is a change of the generator too.
    static string Hash(int seed, int floor)
    {
        var p = PlanGen.Generate(seed, floor);
        var lines = p == null ? new List<string> { "piano non generato: " + PlanGen.LastFailure } : Program.DumpLines(p, floor);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n"))).ToLowerInvariant();
    }

    static int Update(string path, string revision, List<Print> prints)
    {
        string was = null; var before = new Dictionary<string, string>();
        if (File.Exists(path))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            was = doc.RootElement.GetProperty("revision").GetString();
            foreach (var e in doc.RootElement.GetProperty("plans").EnumerateArray())
                before[e.GetProperty("layout").GetString() + " " + e.GetProperty("seed").GetInt32() + " " + e.GetProperty("floor").GetInt32()] = e.GetProperty("sha256").GetString();
        }
        var changed = prints.FindAll(p => before.TryGetValue(p.Key, out string old) && old != p.sha256);
        if (changed.Count > 0 && was == revision)
        {
            var names = changed.ConvertAll(p => p.Name);        // six at most, like the message of the suite (whose report names every plan)
            Console.Error.WriteLine($"PLAN_CHANGED_WITHOUT_REVISION: {changed.Count} of {prints.Count} plans changed ({string.Join("; ", names.GetRange(0, Math.Min(6, names.Count)))}{(names.Count > 6 ? "; ..." : "")}) and CoopProtocol.Revision is still {revision}, the one {path} was made at.");
            Console.Error.WriteLine($"Raise it in {CoopWire} (and say why in the comment of the constant), then run --update again. Nothing was written.");
            return 1;
        }
        var text = new StringBuilder();
        text.Append("{\n  \"schemaVersion\": 1,\n");
        text.Append("  \"about\": \"SHA-256 of the text that planlab dump prints (invariant culture, lines joined with LF) for the plans below, and the CoopProtocol.Revision it was made at. Written by `dotnet run --project tools/planlab -c Release -- fingerprint --update`, never by hand: see docs/engineering/testing.md.\",\n");
        text.Append("  \"revision\": ").Append(JsonSerializer.Serialize(revision)).Append(",\n  \"plans\": [\n");
        for (int i = 0; i < prints.Count; i++)
            text.Append("    ").Append(JsonSerializer.Serialize(new { prints[i].layout, prints[i].seed, prints[i].floor, prints[i].sha256 })).Append(i + 1 < prints.Count ? ",\n" : "\n");
        text.Append("  ]\n}\n");
        File.WriteAllText(path, text.ToString());
        Console.WriteLine($"{path}: {prints.Count} plans at {revision}" + (was == null ? " (new)" : was != revision ? $" (was {was})" : "") + (changed.Count > 0 ? $", {changed.Count} changed" : ""));
        return 0;
    }

    // The revision the game was built with: the constant of CoopWire.cs, found from the folder of the lab (or of the shell) upwards.
    static string ReadRevision()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, CoopWire);
                if (!File.Exists(path)) continue;
                var m = Regex.Match(File.ReadAllText(path), "const\\s+string\\s+Revision\\s*=\\s*\"([^\"]+)\"");
                if (!m.Success) throw new InvalidDataException("no const string Revision = \"...\" in " + path);
                return m.Groups[1].Value;
            }
        throw new FileNotFoundException(CoopWire + " is not above " + AppContext.BaseDirectory + " nor above the current folder");
    }
}
