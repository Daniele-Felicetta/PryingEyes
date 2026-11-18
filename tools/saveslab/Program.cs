using System;
using System.Collections.Generic;
using System.IO;

// The save rules outside Unity: the reusable package (JSON tree, versioned formats with migrations, atomic file store) and the game's own
// character schema (record, levels, looks, the import of the old cathedral file) compiled from the same files the game uses.
// Usage: dotnet run -c Release -- [report.json]
static partial class Program
{
    static readonly List<object> findings = new List<object>();
    static bool ok = true;
    static string subject = "saves";

    static void Check(string code, bool passed, string observed, string expected)
    {
        ok &= passed;
        findings.Add(new { code, subject, passed, observed, expected });
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {code}: {observed}");
    }

    static void Reject<T>(string code, Action action) where T : Exception
    {
        bool rejected = false; string seen = "nothing thrown";
        try { action(); } catch (T) { rejected = true; seen = typeof(T).Name; } catch (Exception e) { seen = e.GetType().Name + ": " + e.Message; }
        Check(code, rejected, seen, typeof(T).Name + " thrown");
    }

    static int Main(string[] args)
    {
        subject = "json"; JsonChecks();
        subject = "format"; FormatChecks();
        subject = "store"; StoreChecks();
        subject = "characters"; CharacterChecks();
        subject = "runs"; RunChecks();
        var report = new { schemaVersion = 1, operation = "saves.rules", ok, findings };
        if (args.Length > 0) File.WriteAllText(args[0], System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(ok ? "SAVES_RULES_PASS" : "SAVES_RULES_FAIL");
        return ok ? 0 : 1;
    }

    /// <summary>A scratch folder that is gone when the lab ends.</summary>
    static string Scratch(string name)
    {
        string folder = Path.Combine(Path.GetTempPath(), "saveslab-" + Guid.NewGuid().ToString("N").Substring(0, 8), name);
        Directory.CreateDirectory(folder);
        scratches.Add(Path.GetDirectoryName(folder));
        return folder;
    }
    static readonly List<string> scratches = new List<string>();
}
