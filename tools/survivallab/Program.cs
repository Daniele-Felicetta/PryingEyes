using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Production.Survival;

static class Program
{
    static readonly List<object> findings = new List<object>();
    static bool ok = true;
    static void Check(string code, bool passed, string observed, string expected)
    {
        ok &= passed;
        findings.Add(new { code, subject = "wave schedule", passed, observed, expected });
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {code}: {observed}");
    }
    static void Reject(string code, Action action)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check(code, rejected, rejected.ToString(), "invalid input rejected");
    }
    static int Main(string[] args)
    {
        var wave = new WaveSchedule(12, 3, .5f);
        Check("WAVE_PENDING_COUNTS", !wave.Complete(0) && wave.Pending == 12, wave.Pending.ToString(), "pending prevents early completion");
        Check("WAVE_FIRST_SPAWN", wave.Advance(0, 0), "initial eligibility", "immediate");
        // A failed engine creation must not consume budget.
        Check("WAVE_FAILED_CREATION_RETRY", wave.Advance(.1f, 0) && wave.Pending == 12, wave.Pending.ToString(), "12 pending");
        wave.CommitSpawn();
        bool doubleCommit = false;
        try { wave.CommitSpawn(); } catch (InvalidOperationException) { doubleCommit = true; }
        Check("WAVE_COMMIT_ONCE", doubleCommit, doubleCommit.ToString(), "a second commit is rejected");
        Check("WAVE_PACING", !wave.Advance(.49f, 1) && wave.Advance(.011f, 1), "half-second deadline", "not before interval");
        wave.CommitSpawn();
        Check("WAVE_CAP", !wave.Advance(100, 3) && wave.Pending == 10, wave.Pending.ToString(), "population cap pauses spawning");
        Check("WAVE_RELEASE_SLOT", wave.Advance(0, 2), "slot released", "ready after population drops");
        wave.CommitSpawn();
        Check("WAVE_NO_CATCHUP_BURST", !wave.Advance(0, 2), "long blocked wait", "no banked catch-up spawns");
        int spawned = wave.Spawned;
        while (wave.Pending > 0)
            if (wave.Advance(.5f, 0)) { wave.CommitSpawn(); spawned++; }
        Check("WAVE_TOTAL_CONSERVED", spawned == 12 && wave.Spawned == 12 && wave.Pending == 0, spawned.ToString(), "12 successful creations");
        Check("WAVE_DRAIN_BEFORE_END", !wave.Complete(1) && wave.Complete(0), "last living enemy", "must die before wave completes");
        Check("WAVE_NO_OVERSPAWN", !wave.Advance(10, 0), wave.Spawned.ToString(), "exhausted budget cannot spawn");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity }) Reject("WAVE_BAD_DELTA", () => wave.Advance(invalid, 0));
        Reject("WAVE_BAD_POPULATION", () => wave.Advance(0, -1));
        Reject("WAVE_BAD_TOTAL", () => new WaveSchedule(0, 1, 1));
        Reject("WAVE_BAD_CAP", () => new WaveSchedule(1, 0, 1));
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity }) Reject("WAVE_BAD_INTERVAL", () => new WaveSchedule(1, 1, invalid));
        // Different caller cadence and a changing living population preserve exact budgets and caps.
        foreach (float dt in new[] { 1f / 30, 1f / 144, .7f })
        {
            var simulation = new WaveSchedule(96, 24, .22f); int live = 0, max = 0, killed = 0, tick = 0;
            while (!simulation.Complete(live) && tick < 100000)
            {
                if (simulation.Advance(dt, live)) { simulation.CommitSpawn(); live++; max = Math.Max(max, live); }
                if (++tick % 5 == 0 && live > 0) { live--; killed++; }
            }
            Check("WAVE_POPULATION_SIMULATION", simulation.Complete(live) && killed == 96 && max <= 24,
                $"dt={dt}, spawned={simulation.Spawned}, killed={killed}, peak={max}", "96 enemies, peak <=24, completed");
        }
        var report = new { schemaVersion = 1, operation = "survival.schedule", ok, findings };
        if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return ok ? 0 : 1;
    }
}
