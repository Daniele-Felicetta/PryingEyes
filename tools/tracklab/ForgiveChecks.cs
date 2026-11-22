using System;
using System.Collections.Generic;
using System.Text;
using PryingEyes.Pista;

/// <summary>Leaving the road. A rider who runs wide in a corner, cuts across the grass or comes back onto the asphalt at an angle must not fall just because the ground under him is loose: with the aids the front slides, the bike
/// stands up and slows down, and the gravel and the grass are a place to lose time and not a place to fall; without the aids it stays harsher; and what is not the ground's fault (a wall, a locked front, a head-on hit) is as
/// it was.</summary>
static partial class Program
{
    /// <summary>A straight road along z with the grounds of a circuit across it, with the numbers of Circuit.Sample: asphalt to 5 m from the middle, the kerb to 6, the paved run-off to 12, the gravel trap to 20, grass beyond.
    /// No barrier: the lab asks what the ground does, the wall has its own check.</summary>
    sealed class StripGround : IGround
    {
        public void Sample(float x, float z, ref int hint, out GroundSample g)
        {
            float a = Math.Abs(x);
            g = new GroundSample { y = 0f, vertCurv = 0f, bank = 0f };
            if (a <= 5f) { g.surface = SurfaceKind.Asphalt; g.grip = 1f; g.rolling = 0.015f; g.rough = 0f; }
            else if (a <= 6f) { g.surface = SurfaceKind.Kerb; g.grip = 0.93f; g.rolling = 0.02f; g.rough = 0.55f; }
            else if (a <= 12f) { g.surface = SurfaceKind.Apron; g.grip = 0.95f; g.rolling = 0.02f; g.rough = 0.05f; }
            else if (a <= 20f) { g.surface = SurfaceKind.Gravel; g.grip = 0.36f; g.rolling = 0.22f; g.rough = 0.6f; }
            else { g.surface = SurfaceKind.Grass; g.grip = 0.42f; g.rolling = 0.07f; g.rough = 0.25f; }
        }
        public bool Barrier(float x, float z, float radius, ref int hint, out float nx, out float nz, out float depth) { nx = nz = depth = 0f; return false; }
    }

    sealed class Excursion
    {
        public CrashKind crash = CrashKind.None;
        public float crashAt = -1f, uEnd, xEnd, offAt = -1f;
        public SurfaceKind crashOn = SurfaceKind.Asphalt;
        public bool Fell { get { return crash != CrashKind.None; } }
    }

    static int GearAt(float u) { return u > 60f ? 6 : u > 48f ? 5 : u > 36f ? 4 : u > 24f ? 3 : u > 12f ? 2 : 1; }

    /// <summary>One excursion: the bike at 'speed' on the road at x0 with a heading of 'headDeg' degrees toward +x. The rider holds 'steer' (a fraction of what the grip allows) and a steady pace until 'hold' seconds after the first
    /// tire leaves the asphalt, then lets go: no steering, no throttle and a little front brake. It rides for 'seconds'. 'level' is the aids level of the game: 0 everything (lean assist, ABS, traction control), 1 ABS and traction control, 2 ABS alone.</summary>
    static Excursion Leave(BikeSpec spec, int level, float speed, float x0, float headDeg, float steer, float hold, float seconds = 14f, float brake = 0.2f, bool keepPace = true)
    {
        var ground = new StripGround();
        var bike = new BikeModel(spec, 11);
        bike.Reset(x0, 0f, headDeg * PM.Deg2Rad, 0f);
        bike.u = speed; bike.omegaF = bike.omegaR = speed / spec.wheelRadius; bike.gear = GearAt(speed);
        bike.abs = true; bike.tcs = level < 2; bike.leanAssist = level == 0;          // the three levels of the game: Completi, Sportivi, Nessuno (ABS never goes)
        var r = new Excursion();
        for (float t = 0f; t < seconds; t += Dt)
        {
            bool off = Math.Abs(bike.x) > 5.2f;
            if (off && r.offAt < 0f) r.offAt = t;
            bool holding = r.offAt < 0f || t - r.offAt < hold;
            var inp = holding ? new BikeInputs { steer = steer, throttle = keepPace ? ThrottleToHold(bike, speed) : 0f } : new BikeInputs { brakeFront = brake };
            bike.Step(Dt, ground, inp);
            if (bike.crashed)
            {
                r.crash = bike.crashKind; r.crashAt = t;
                GroundSample g; int h = 0; ground.Sample(bike.x, bike.z, ref h, out g); r.crashOn = g.surface;
                break;
            }
        }
        r.uEnd = bike.u; r.xEnd = bike.x;
        return r;
    }


    /// <summary>The path of one excursion second by second, for finding out why a check fails: FORGIVE_TRACE="level,speed,steer,hold,bike" (bike 0 superbike, 1 the 125, 2 the roadster, 3 the 500) before the lab runs.</summary>
    static void ForgiveTrace(string spec)
    {
        var a = spec.Split(',');
        int level = int.Parse(a[0]); float v = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture), steer = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture), hold = float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture);
        var sp = new[] { Bikes.Superbike(), Bikes.Get(3), Bikes.Roadster(), Bikes.Gp500() }[a.Length > 4 ? int.Parse(a[4]) : 0];
        var ground = new StripGround();
        var bike = new BikeModel(sp, 11);
        bike.Reset(-3f, 0f, 0f, 0f);
        bike.u = v; bike.omegaF = bike.omegaR = v / sp.wheelRadius; bike.gear = GearAt(v);
        bike.abs = true; bike.tcs = level < 2; bike.leanAssist = level == 0;
        float offAt = -1f, next = 0f;
        for (float t = 0f; t < 6f; t += Dt)
        {
            if (offAt < 0f && Math.Abs(bike.x) > 5.2f) offAt = t;
            bool holding = offAt < 0f || t - offAt < hold;
            bike.Step(Dt, ground, holding ? new BikeInputs { steer = steer, throttle = ThrottleToHold(bike, v) } : new BikeInputs { brakeFront = 0.2f });
            if (t >= next || bike.crashed)
            {
                Console.WriteLine("  t " + F(t, "F2") + " x " + F(bike.x, "F1") + " " + bike.surfaceF + "/" + bike.surfaceR + " u " + F(bike.u, "F1") + " lean " + F(bike.lean * PM.Rad2Deg, "F0") + " deg latG " + F(bike.latG) + " frontSliding " + bike.frontSliding + " rearSliding " + bike.rearSliding + (bike.crashed ? " CRASH " + bike.crashKind : ""));
                next += 0.2f;
                if (bike.crashed) break;
            }
        }
    }

    static void ForgiveChecks()
    {
        var trace = Environment.GetEnvironmentVariable("FORGIVE_TRACE");
        if (!string.IsNullOrEmpty(trace)) ForgiveTrace(trace);
        var specs = new[] { Bikes.Superbike(), Bikes.Get(3), Bikes.Roadster(), Bikes.Gp500() };
        var names = new[] { "superbike", "125", "roadster", "500" };

        // ---- running wide in a corner: the rider holds the corner for a moment after the tires leave the asphalt, then lets go. With the assist the moment is long (1.2 s) and the rider asks for as much as the grip gives;
        // without it the moment is what a person needs to react (0.6 s) and he asks for no more than 0.7 of it (more than that is a fall on the asphalt already)
        foreach (int level in new[] { 0, 1, 2 })
        {
            int runs = 0, falls = 0; var detail = new StringBuilder();
            float hold = level == 0 ? 1.2f : 0.6f;
            for (int si = 0; si < specs.Length; si++)
                foreach (float v in new[] { 20f, 35f, 50f })
                    foreach (float steer in level == 0 ? new[] { 0.4f, 0.7f, 1.0f } : new[] { 0.4f, 0.7f })
                    {
                        var e = Leave(specs[si], level, v, -3f, 0f, steer, hold);
                        runs++;
                        if (e.Fell) { falls++; if (detail.Length < 500) detail.Append(names[si] + " " + F(v, "F0") + " m/s steer " + F(steer, "F1") + ": " + e.crash + " on the " + e.crashOn + " at " + F(e.crashAt, "F1") + " s; "); }
                    }
            string what = falls + " falls in " + runs + " runs of a corner that carries the bike across the kerb, the run-off, the gravel and the grass (4 bikes, 20/35/50 m/s, " + (level == 0 ? "0.4/0.7/1.0" : "0.4/0.7") + " of the grip, held for " + F(hold, "F1") + " s after leaving the asphalt)";
            if (level == 0)
                Check("FORGIVE_RUNNING_WIDE_WITH_ALL_THE_AIDS_IS_NOT_A_FALL", falls == 0, what + (falls > 0 ? ": " + detail : ""), "no falls");
            else if (level == 1)
                Check("FORGIVE_RUNNING_WIDE_WITH_ABS_AND_TRACTION_CONTROL_IS_RARELY_A_FALL", falls <= runs / 6, what + " with ABS and traction control only" + (falls > 0 ? ": " + detail : ""), "at most a sixth of the runs end in a fall");
            else
                Check("FORGIVE_RUNNING_WIDE_WITH_ABS_ONLY", falls <= runs / 4, what + " with ABS only" + (falls > 0 ? ": " + detail : ""), "at most a quarter of the runs end in a fall");
        }

        // ---- going straight across the run-off, the gravel and the grass at an angle
        {
            int runs = 0, falls = 0; var detail = new StringBuilder();
            for (int si = 0; si < specs.Length; si++)
                foreach (float v in new[] { 25f, 45f, 65f })
                    foreach (float ang in new[] { 5f, 12f, 25f })
                    {
                        var e = Leave(specs[si], 0, v, -2f, ang, 0f, 0f, 14f, 0.1f, false);
                        runs++;
                        if (e.Fell) { falls++; if (detail.Length < 600) detail.Append(names[si] + " " + F(v, "F0") + " m/s at " + F(ang, "F0") + " deg: " + e.crash + " on the " + e.crashOn + "; "); }
                    }
            Check("FORGIVE_STRAIGHT_ACROSS_THE_GRAVEL_IS_NOT_A_FALL", falls == 0,
                falls + " falls in " + runs + " runs straight off the road at 5/12/25 degrees (4 bikes, 25/45/65 m/s, hands off, a little front brake)" + (falls > 0 ? ": " + detail : ""), "no falls");
        }

        // ---- coming back onto the asphalt from the grass at an angle (the highside of the textbook)
        {
            int runs = 0, falls = 0; var detail = new StringBuilder();
            for (int si = 0; si < specs.Length; si++)
                foreach (float v in new[] { 15f, 25f, 40f })
                    foreach (float ang in new[] { 15f, 30f, 45f })
                    {
                        var e = Leave(specs[si], 0, v, 30f, -ang, 0f, 99f, 12f, 0f, true);
                        runs++;
                        if (e.Fell) { falls++; if (detail.Length < 600) detail.Append(names[si] + " " + F(v, "F0") + " m/s at " + F(ang, "F0") + " deg: " + e.crash + " at x=" + F(e.xEnd, "F0") + "; "); }
                    }
            Check("FORGIVE_COMING_BACK_FROM_THE_GRASS_IS_NOT_A_FALL", falls == 0,
                falls + " falls in " + runs + " runs back onto the asphalt at 15/30/45 degrees (4 bikes, 15/25/40 m/s, throttle holding the pace)" + (falls > 0 ? ": " + detail : ""), "no falls");
        }

        // ---- braking hard on the grass
        {
            int runs = 0, falls = 0; var detail = new StringBuilder();
            for (int si = 0; si < specs.Length; si++)
                foreach (float v in new[] { 20f, 35f, 50f })
                {
                    var ground = new StripGround();
                    var b = new BikeModel(specs[si], 5);
                    b.Reset(40f, 0f, 0f, 0f);
                    b.u = v; b.omegaF = b.omegaR = v / specs[si].wheelRadius; b.gear = GearAt(v);
                    for (float t = 0f; t < 10f && !b.crashed && b.u > 0.3f; t += Dt) b.Step(Dt, ground, new BikeInputs { brakeFront = 0.8f, brakeRear = 0.3f });
                    runs++;
                    if (b.crashed) { falls++; detail.Append(names[si] + " " + F(v, "F0") + " m/s: " + b.crashKind + "; "); }
                }
            Check("FORGIVE_BRAKING_ON_THE_GRASS_WITH_ABS_IS_NOT_A_FALL", falls == 0, falls + " falls in " + runs + " hard stops on the grass with ABS (4 bikes, 20/35/50 m/s)" + (falls > 0 ? ": " + detail : ""), "no falls");
        }

        // ---- what is not the ground's fault stays as it was
        {
            // a locked front wheel without ABS on the grass still folds the bike
            var ground = new StripGround();
            var b = new BikeModel(specs[0], 5); b.abs = false; b.antiStoppie = false;
            b.Reset(40f, 0f, 0f, 0f); b.u = 30f; b.omegaF = b.omegaR = 30f / specs[0].wheelRadius; b.gear = 4;
            for (float t = 0f; t < 8f && !b.crashed && b.u > 0.3f; t += Dt) b.Step(Dt, ground, new BikeInputs { brakeFront = 1f });
            Check("FORGIVE_A_LOCKED_FRONT_WITHOUT_ABS_STILL_FALLS", b.crashed, b.crashed ? "the locked front folded the bike (" + b.crashKind + ") with no aids" : "no fall (" + F(b.u, "F1") + " m/s left)", "falls");
        }
    }
}
