using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

// The rules of getting into a co-op session, and of the perks a player carries, outside Unity: the host's admission gate, the token a guest
// sends, the roster of connections and seats, and PlayerPerks. They are compiled from the files the game compiles (the admission files of
// Scripts/Coop, Scripts/PlayerPerks.cs and Punti.cs), never from copies. The assertions are those of the first lab that held them
// (tools/multiplayer-check, retired by T20), kept word for word and in the same blocks, so that the counts can be compared.
// Usage: dotnet run -c Release -- [report.json]
static partial class Program
{
    static readonly List<object> findings = new List<object>();
    static bool ok = true;
    static string subject = "admission";
    static int checks, reported;

    // The sentence says what must hold; the code of the finding is the same sentence in capitals, so a rule keeps its name wherever it is read.
    static void Check(bool condition, string message)
    {
        checks++; ok &= condition;
        findings.Add(new { code = Code(message), subject, passed = condition, observed = condition ? "holds" : "violated", expected = message });
        Console.WriteLine((condition ? "PASS " : "FAIL ") + message);
    }

    static string Code(string message)
    {
        var code = new StringBuilder();
        foreach (char c in message.ToUpperInvariant())
        {
            if (c >= 'A' && c <= 'Z' || c >= '0' && c <= '9') code.Append(c);
            else if (code.Length > 0 && code[code.Length - 1] != '_') code.Append('_');
        }
        return code.ToString().TrimEnd('_');
    }

    // One line per block with the assertions it ran, in the shape the first lab printed them.
    static void Block(string name)
    {
        Console.WriteLine("  " + name.PadRight(18) + (checks - reported).ToString().PadLeft(4));
        reported = checks;
    }

    static int Main(string[] args)
    {
        subject = "admission gate"; AdmissionGateChecks(); Block("admission gate");
        subject = "PlayerPerks"; PlayerPerksChecks(); Block("PlayerPerks");
        subject = "admission token"; TokenChecks(); Block("admission token");
        subject = "roster"; RosterChecks(); Block("roster");
        var report = new { schemaVersion = 1, operation = "admission.rules", ok, findings };
        if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine((ok ? "PASS: " : "FAIL: ") + checks + " assertions; admission gate, token, roster and perks.");
        Console.WriteLine(ok ? "ADMISSION_RULES_PASS" : "ADMISSION_RULES_FAIL");
        return ok ? 0 : 1;
    }
}
