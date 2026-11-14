using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Production.Weapons;

static class Program
{
    static readonly List<object> findings = new List<object>();
    static int failures, samples;
    static void Check(string code, string subject, bool passed, string observed, string expected)
    {
        if (!passed) { failures++; Console.WriteLine("FAIL " + subject + ": " + observed); }
        findings.Add(new { code, subject, passed, observed, expected });
    }
    static int Main(string[] args)
    {
        try
        {
            using var catalog = JsonDocument.Parse(File.ReadAllText(args[0]));
            foreach (var entry in catalog.RootElement.GetProperty("weapons").EnumerateArray())
            {
                var def = entry.GetProperty("definition");
                foreach (bool upgrade in new[] { false, true })
                foreach (bool empty in new[] { false, true })
                foreach (bool lowReserve in new[] { false, true })
                foreach (float dt in new[] { .02f, 1f / 30, .37f })
                foreach (float timeScale in new[] { 1f, .65f })
                    Compare(def, upgrade, empty, lowReserve, dt, timeScale);
            }
            Contracts(); Cadence();
        }
        catch (Exception e) { Check("STATE_LAB_EXCEPTION", "suite", false, e.ToString(), "completed verification"); }
        var report = new { schemaVersion = 1, operation = "weapon.state", ok = failures == 0, samples,
            referenceCommit = "6fcbecf", findings };
        if (args.Length > 1)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
            File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"WEAPON STATE {(failures == 0 ? "PASS" : "FAIL")}: {findings.Count - failures}/{findings.Count}, {samples} trace samples");
        return failures == 0 ? 0 : 1;
    }

    static void Compare(JsonElement d, bool upgrade, bool empty, bool lowReserve, float dt, float scale)
    {
        int capacity = (int)Math.Ceiling(d.GetProperty("mag").GetInt32() * (upgrade ? 1.5f : 1));
        int mag = empty ? 0 : capacity - 1, reserve = lowReserve ? 2 : capacity + 3;
        bool tube = d.GetProperty("style").GetString() == "Tube";
        float tactical = d.GetProperty("reload").GetSingle(), dry = d.GetProperty("reloadEmpty").GetSingle(), shell = d.GetProperty("shellTime").GetSingle();
        var timing = tube ? ReloadTiming.PerRound(shell, .35f, .25f, .55f) : ReloadTiming.Magazine(tactical, dry);
        var legacy = new LegacyReload(mag, reserve, capacity, tube, tactical, dry, shell);
        var state = new WeaponState(); state.SetAmmunition(mag, reserve); bool started = state.BeginReload(capacity, timing);
        bool equal = started == legacy.active; string observed = "identical ammo, stages and timing";
        int frame;
        for (frame = 0; equal && legacy.active && frame < 10000; frame++)
        {
            var a = legacy.Tick(dt / scale); var b = state.AdvanceReload(dt / scale); samples++;
            equal = state.Magazine == legacy.mag && state.Reserve == legacy.reserve && state.IsReloading == legacy.active &&
                state.RoundsLoaded == legacy.loaded && state.ReloadStartedEmpty == legacy.empty &&
                Math.Abs(state.ReloadElapsed - legacy.elapsed) < .00001f && b.RoundsInserted == a.rounds && (int)b.Events == a.events &&
                state.Magazine + state.Reserve == mag + reserve;
            if (!equal) observed = $"frame {frame}: {state.Magazine}/{state.Reserve} events {(int)b.Events}; reference {legacy.mag}/{legacy.reserve} events {a.events}";
        }
        Check("LEGACY_RELOAD_EQUIVALENCE", $"{d.GetProperty("id").GetString()} upgraded={upgrade} empty={empty} lowReserve={lowReserve} dt={dt} scale={scale}",
            equal && !state.IsReloading && frame < 10000, observed, "same per-frame state and presentation events as frozen reference");
    }

    static void Contracts()
    {
        var state = new WeaponState(); var timing = ReloadTiming.PerRound(.5f, .35f, .25f, .55f);
        state.SetAmmunition(0, 8); state.BeginReload(5, timing); state.AdvanceReload(1.2f);
        int loaded = state.Magazine; state.CancelReload(); state.AdvanceReload(50);
        Check("CANCEL_PRESERVES_INSERTED_AMMO", "incremental reload", loaded > 0 && state.Magazine == loaded && state.Magazine + state.Reserve == 8,
            $"{state.Magazine}/{state.Reserve}", "inserted rounds survive cancellation; no later inserts");
        state.SetAmmunition(0, 8); state.BeginReload(5, timing); state.AdvanceReload(.2f); state.SetAmmunition(5, 20); state.CancelReload(); state.AdvanceReload(50);
        Check("REFILL_CANCELS_WITHOUT_EXTRA_ROUNDS", "ammo pickup adapter", state.Magazine == 5 && state.Reserve == 20, $"{state.Magazine}/{state.Reserve}", "5/20");
        state.SetAmmunition(0, 2); state.BeginReload(5, timing); state.SetAmmunition(0, 0); state.AdvanceReload(50);
        Check("RESERVE_CHANGES_CANNOT_CREATE_ROUNDS", "external ammo change", state.Magazine == 0 && state.Reserve == 0, $"{state.Magazine}/{state.Reserve}", "0/0");
        int rejected = 0;
        foreach (Action invalid in new Action[] { () => state.SetAmmunition(-1, 2), () => state.AdvanceCadence(float.NaN),
            () => state.AdvanceReload(float.PositiveInfinity), () => ReloadTiming.PerRound(0, 0, 0, .5f), () => state.BeginReload(0, timing) })
            try { invalid(); } catch (ArgumentOutOfRangeException) { rejected++; }
        Check("INVALID_STATE_INPUT_REJECTED", "public interface", rejected == 5, rejected.ToString(), "5 invalid inputs rejected");
    }

    static void Cadence()
    {
        foreach (int fps in new[] { 30, 60, 144 })
        foreach (float rpm in new[] { 330f, 600f, 900f })
        {
            var state = new WeaponState(); float fire = 0, cycle = 0; bool equal = true; int fired = 0;
            for (int frame = 0; frame < fps * 4; frame++)
            {
                float dt = 1f / fps; fire = Math.Max(0, fire) - dt; if (cycle > 0) cycle -= dt;
                state.AdvanceCadence(dt);
                if (fire <= 0) { fire += 60 / rpm; state.RecordShot(60 / rpm); fired++; }
                equal &= Math.Abs(fire - state.FireDelay) < .000001f && Math.Abs(cycle - state.CycleDelay) < .000001f;
            }
            Check("CADENCE_FRAME_OVERSHOOT", $"{fps}fps/{rpm}rpm", equal, fired + " shots", "same frame debt as legacy; no stored idle credit");
        }
        var reset = new WeaponState(); reset.StartCycle(1); reset.RecordShot(.5f); reset.ResetActions();
        Check("RESET_UNLOCKS_NEXT_WEAPON", "switch/respawn adapter", reset.Ready, reset.Ready.ToString(), "no inherited cadence or cycle");
    }
}
