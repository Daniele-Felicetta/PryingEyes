using System;
using System.Collections.Generic;
using System.IO;

// The track mode's rules outside Unity: the motorcycle physics, the circuit generator, the rider AI and the race rules, compiled from the same files the game uses
// (Assets/_PryingEyes/Scripts/Pista). Every check prints PASS or FAIL with what it saw.
// Usage: dotnet run -c Release -- [report.json] [--pictures folder] [--only bike|circuit|race|coop|rider|timing|roster|sound|replay|licence|garage|cup|keyboard|contact|career] [--scan FROM TO] [--seed N] [--dump-engines DIR]
static partial class Program
{
    static readonly List<object> findings = new List<object>();
    static bool ok = true;
    static string subject = "bike";

    static void Check(string code, bool passed, string observed, string expected)
    {
        ok &= passed;
        findings.Add(new { code, subject, passed, observed, expected });
        Console.WriteLine((passed ? "PASS " : "FAIL ") + code + ": " + observed);
    }

    static string picturesDir;
    static int seedArg = 0;

    static int Main(string[] args)
    {
        string report = null, only = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--pictures" && i + 1 < args.Length) picturesDir = args[++i];
            else if (args[i] == "--only" && i + 1 < args.Length) only = args[++i];
            else if (args[i] == "--seed" && i + 1 < args.Length) seedArg = int.Parse(args[++i]);
            else if (!args[i].StartsWith("--")) report = args[i];
        }
        if (picturesDir != null) Directory.CreateDirectory(picturesDir);
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--dump-engines") { DumpEngines(args[i + 1]); return 0; }
        for (int i = 0; i < args.Length - 2; i++) if (args[i] == "--scan") { ScanSeeds(int.Parse(args[i + 1]), int.Parse(args[i + 2])); return 0; }
        if (only == null || only == "bike") { subject = "bike"; BikeChecks(); }
        if (only == null || only == "circuit") { subject = "circuit"; CircuitChecks(); }
        if (only == null || only == "race") { subject = "race"; RaceChecks(); }
        if (only == null || only == "coop") { subject = "coop"; CoopChecks(); }
        if (only == null || only == "rider") { subject = "rider"; RiderChecks(); }
        if (only == null || only == "timing") { subject = "timing"; TimingChecks(); }
        if (only == null || only == "roster") { subject = "roster"; RosterChecks(); }
        if (only == null || only == "sound") { subject = "sound"; SoundChecks(); }
        if (only == null || only == "replay") { subject = "replay"; ReplayChecks(); }
        if (only == null || only == "licence") { subject = "licence"; LicenceChecks(); }
        if (only == null || only == "garage") { subject = "garage"; GarageChecks(); }
        if (only == null || only == "cup") { subject = "cup"; CupChecks(); }
        if (only == null || only == "keyboard") { subject = "keyboard"; KeyboardChecks(); }
        if (only == null || only == "contact") { subject = "contact"; ContactChecks(); }
        if (only == null || only == "career") { subject = "career"; CareerChecks(); }
        var doc = new { schemaVersion = 1, operation = "track.rules", ok, findings };
        if (report != null) File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(ok ? "TRACK_RULES_PASS" : "TRACK_RULES_FAIL");
        return ok ? 0 : 1;
    }
}
