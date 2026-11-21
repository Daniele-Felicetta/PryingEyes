using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PryingEyes.Pista;

/// <summary>The keyboard (KeyShaper.cs, the shaper PistaInput puts between the keys and the bike): the ramps of the keys, the cap of the steering at speed, and what a person on a keyboard can do with them. The person is an
/// instrument, not a person: a rider that looks at the road the way people do (a point of the racing line 10 m plus one second ahead) and presses the keys at the rate of a hand (a decision every 60 to 150 ms, a reaction
/// of 40 to 250 ms, a dead band before it touches a steering key), on the real shaper, the real physics and the real circuits. It says how the keys compare with a stick, not how a keyboard feels.</summary>
static partial class Program
{
    /// <summary>How a hand decides: how long after a decision the key moves, how often it decides, and how far the steering must be from where the road asks before it touches a key.</summary>
    sealed class HumanStyle { public string name = ""; public float reaction, decision, deadband; }

    /// <summary>Where the rider looks: this many metres of the racing line plus this many seconds of speed ahead.</summary>
    const float KeyLookBase = 10f, KeyLookGain = 1.0f;

    sealed class KeyHuman
    {
        readonly BikeModel bike; readonly Circuit circuit; readonly RacingLine line;
        public readonly KeyShaper keys = new KeyShaper();
        readonly Queue<bool[]> pending = new Queue<bool[]>();
        readonly Queue<BikeInputs> analogLag = new Queue<BikeInputs>();
        bool[] held = new bool[4];                       // gas, front brake, left, right
        int hint = -1; float clock, nextDecision;
        public float pace = 0.9f;
        public HumanStyle style;
        public bool analog;

        public KeyHuman(BikeModel b, Circuit c, RacingLine l) { bike = b; circuit = c; line = l; }

        public BikeInputs Drive(float dt)
        {
            var b = bike; var inp = new BikeInputs();
            Proj proj;
            circuit.Project(b.x, b.z, ref hint, out proj);
            if (b.crashed) { keys.Step(false, false, false, false, false, 0f, dt); return inp; }
            float u = Math.Max(b.u, 0f);
            float s = proj.s;
            float look = Math.Max(7f, Math.Min(80f, KeyLookBase + KeyLookGain * u));
            float tx, tz;
            line.PointAt(s + look, 0f, out tx, out tz);
            float dx = tx - b.x, dz = tz - b.z;
            float sinH = (float)Math.Sin(b.heading), cosH = (float)Math.Cos(b.heading);
            float xr = dx * cosH - dz * sinH, zf = dx * sinH + dz * cosH;
            float chord2 = xr * xr + zf * zf + 0.01f;
            float kappa = zf > 0.5f ? 2f * xr / chord2 : (xr >= 0f ? 1f : -1f) / b.spec.minTurnRadius;
            // what a rider feels as "how much to lean": a fraction of the tightest turn at this speed
            float kcap = Math.Min(1f / b.spec.minTurnRadius, 9.81f * (float)Math.Tan(0.8f * b.spec.frameLeanMax) / Math.Max(u * u, 30f));
            float sd = Math.Max(-1f, Math.Min(1f, kappa / kcap));
            float ahead = Math.Max(6f, u * 0.45f + 6f);
            float vt = line.MinPlanAhead(s, ahead) * pace;
            float hw = circuit.HalfWidthAt(proj);
            if (Math.Abs(proj.d) > hw + 0.3f) vt = Math.Min(vt, 17f);

            if (analog)
            {
                inp.steer = sd;
                float err = vt - u;
                if (err > -0.4f) inp.throttle = Math.Max(0f, Math.Min(1f, 0.18f + 0.82f * (err + 0.4f) / 2.2f));
                else { float brake = Math.Max(0f, Math.Min(1f, (-err - 0.4f) / 6f)); inp.brakeFront = brake * 0.92f; inp.brakeRear = brake * 0.25f; }
                analogLag.Enqueue(inp);                    // a stick has the same reaction time as a hand on the keys
                int steps = Math.Max(1, (int)Math.Round(style.reaction / dt));
                while (analogLag.Count > steps + 1) analogLag.Dequeue();
                return analogLag.Count == steps + 1 ? analogLag.Peek() : new BikeInputs();
            }

            clock += dt;
            if (clock >= nextDecision)
            {
                nextDecision = clock + style.decision;
                float e = sd - keys.steer;
                var pressed = new bool[4];
                pressed[0] = u < vt - 1.2f;
                pressed[1] = u > vt + 2.0f;
                pressed[2] = e < -style.deadband;
                pressed[3] = e > style.deadband;
                pending.Enqueue(pressed);
                int lag = Math.Max(0, (int)Math.Round(style.reaction / style.decision));
                while (pending.Count > lag + 1) pending.Dequeue();
                if (pending.Count == lag + 1) held = pending.Peek();
            }
            keys.Step(held[0], held[1], false, held[2], held[3], u, dt);
            inp.throttle = keys.thr; inp.brakeFront = keys.bf; inp.steer = keys.steer;
            return inp;
        }
    }

    sealed class KeyRide { public bool finished; public float time; public int falls; }

    /// <summary>One lap alone on a circuit with the aids of a level (the mapping of PistaMode.ApplyAids), by a keyboard rider (or, with 'analog', by the same rider with a stick).</summary>
    static KeyRide RideKeys(Circuit circuit, int bike, int aids, float pace, bool analog, int seed, HumanStyle style, float capFloor)
    {
        var race = new Race(circuit, 1, seed);
        var me = race.AddHuman("keys", bike, 0, seed);
        var b = me.bike;
        b.abs = true; b.tcs = aids < 2; b.antiWheelie = aids < 2; b.antiStoppie = aids < 2; b.leanAssist = aids == 0; b.autoGears = aids < 2; b.autoHang = true; b.autoTuck = aids < 2;
        race.PlaceAll(new List<Racer> { me });
        var human = new KeyHuman(b, circuit, race.LineFor(bike)) { pace = pace, analog = analog, style = style };
        human.keys.capFloor = capFloor;
        race.StartCountdown();
        float t = 0f;
        while (race.phase != RacePhase.Over && t < 400f)
        {
            if (race.phase == RacePhase.Racing) me.input = human.Drive(RaceDt);
            race.Step(RaceDt); t += RaceDt;
        }
        return new KeyRide { finished = me.finished, time = me.finished ? me.finishTime : -1f, falls = me.crashes };
    }

    /// <summary>The rides of one rider on the same bikes and circuits, in the same order, so that two tallies compare ride by ride.</summary>
    sealed class KeyTally
    {
        public int falls, unfinished;
        public readonly List<float> times = new List<float>();
        public int Rides { get { return times.Count; } }
    }

    static KeyTally RideAll(int[] bikes, int[] circuits, int aids, float pace, bool analog, HumanStyle style, float capFloor = KeyShaper.CapFloor)
    {
        var tally = new KeyTally();
        foreach (int bike in bikes) foreach (int ci in circuits)
        {
            var r = RideKeys(CircuitGen.Generate(Catalogue.All[ci].seed), bike, aids, pace, analog, 11, style, capFloor);
            tally.falls += r.falls;
            if (!r.finished) tally.unfinished++;
            tally.times.Add(r.time);
        }
        return tally;
    }

    /// <summary>How long the rides took against the other tally, over the rides both finished (1 is as quick).</summary>
    static float TimeAgainst(KeyTally a, KeyTally b)
    {
        float x = 0f, y = 0f;
        for (int i = 0; i < a.times.Count; i++) if (a.times[i] > 0f && b.times[i] > 0f) { x += a.times[i]; y += b.times[i]; }
        return y > 0f ? x / y : 0f;
    }

    static string F(float v, string fmt = "F2") { return v.ToString(fmt, CultureInfo.InvariantCulture); }

    static void KeyboardChecks()
    {
        // ---- the ramps: the keys are not switches
        {
            float dt = 1f / 600f;
            var k = new KeyShaper(); float gasOpens = 0f, gasCloses = 0f, brakes = 0f, rear = 0f;
            while (k.thr < 1f && gasOpens < 5f) { k.Step(true, false, false, false, false, 0f, dt); gasOpens += dt; }
            while (k.thr > 0f && gasCloses < 5f) { k.Step(false, false, false, false, false, 0f, dt); gasCloses += dt; }
            k.Reset();
            while (k.bf < 1f && brakes < 5f) { k.Step(false, true, false, false, false, 0f, dt); brakes += dt; }
            k.Reset();
            while (k.br < 1f && rear < 5f) { k.Step(false, false, true, false, false, 0f, dt); rear += dt; }
            // a steering key held at 5 m/s reaches a full lean in a quarter of a second; a tap of a tenth of a second at 40 m/s is a small correction and lets go of it at once
            k.Reset(); float full = 0f;
            while (Math.Abs(k.steer) < 1f && full < 5f) { k.Step(false, false, false, false, true, 5f, dt); full += dt; }
            k.Reset();
            for (int i = 0; i < 60; i++) k.Step(false, false, false, true, false, 40f, dt);
            float tap = Math.Abs(k.steer); float back = 0f;
            while (k.steer != 0f && back < 5f) { k.Step(false, false, false, false, false, 40f, dt); back += dt; }
            // reversing is quicker than building up: from half a lean to the other side's zero against from zero to half a lean
            k.Reset(); float build = 0f;
            while (k.steer < 0.5f && build < 5f) { k.Step(false, false, false, false, true, 15f, dt); build += dt; }
            float reverse = 0f;
            while (k.steer > 0f && reverse < 5f) { k.Step(false, false, false, true, false, 15f, dt); reverse += dt; }
            bool ok1 = gasOpens > 0.30f && gasOpens < 0.33f && gasCloses > 0.13f && gasCloses < 0.16f && brakes > 0.40f && brakes < 0.44f && rear > 0.24f && rear < 0.26f
                && full > 0.20f && full < 0.25f && tap > 0.2f && tap < 0.4f && back < 0.12f && reverse < 0.6f * build;
            Check("KEYS_RAMP_THE_WAY_A_HAND_DOES", ok1,
                "the gas opens in " + F(gasOpens) + " s and closes in " + F(gasCloses) + " s, the front brake builds in " + F(brakes) + " s and the rear in " + F(rear) + " s; a steering key reaches a full lean in " + F(full)
                + " s at 5 m/s; a tap of 0.1 s at 40 m/s moves it " + F(tap) + " and lets go in " + F(back) + " s; half a lean builds in " + F(build) + " s and is undone in " + F(reverse) + " s",
                "gas 0.31 s up and 0.14 s down, front brake 0.42 s, rear brake 0.25 s, a full lean in about 0.23 s at 5 m/s, a tap under 0.4 and back in under 0.12 s, reversing in under 60% of building");
        }

        // ---- the cap of the steering at speed
        {
            var problems = new StringBuilder();
            float prev = 2f;
            for (float v = 0f; v <= 90f; v += 0.5f)
            {
                float c = KeyShaper.SteerCap(v);
                if (c > prev + 1e-6f || c < KeyShaper.CapFloor - 1e-6f || c > 1f + 1e-6f) problems.Append("cap " + F(c, "F3") + " at " + F(v, "F1") + " m/s; ");
                prev = c;
            }
            if (Math.Abs(KeyShaper.SteerCap(0f) - 1f) > 1e-6f || Math.Abs(KeyShaper.SteerCap(KeyShaper.CapSlow) - 1f) > 1e-6f || Math.Abs(KeyShaper.SteerCap(KeyShaper.CapFast) - KeyShaper.CapFloor) > 1e-6f
                || Math.Abs(KeyShaper.SteerCap(80f) - KeyShaper.CapFloor) > 1e-6f) problems.Append("the ends of the cap are not 1 and " + F(KeyShaper.CapFloor) + "; ");
            // a key held until it settles, to the right and to the left, at several speeds
            string settled = "";
            foreach (float v in new[] { 0f, 5f, 10f, 17.5f, 25f, 40f, 70f })
                foreach (bool right in new[] { true, false })
                {
                    var k = new KeyShaper();
                    for (int i = 0; i < 300; i++) k.Step(false, false, false, !right, right, v, 1f / 60f);
                    float want = (right ? 1f : -1f) * KeyShaper.SteerCap(v);
                    if (Math.Abs(k.steer - want) > 1e-4f) problems.Append("a key held " + (right ? "right" : "left") + " at " + F(v, "F1") + " m/s settles at " + F(k.steer, "F3") + " (the cap says " + F(want, "F3") + "); ");
                    if (right) settled += F(v, "F1") + " m/s " + F(k.steer, "F2") + ", ";
                }
            // the speed rises with the key held: the steering eases down to the cap, no jump and no overshoot
            {
                var k = new KeyShaper();
                for (int i = 0; i < 120; i++) k.Step(false, false, false, false, true, 8f, 1f / 60f);
                float before = k.steer, last = k.steer, biggest = 0f; bool monotone = true;
                for (int i = 0; i < 240; i++)
                {
                    k.Step(false, false, false, false, true, 30f, 1f / 60f);
                    if (k.steer > last + 1e-6f) monotone = false;
                    biggest = Math.Max(biggest, last - k.steer); last = k.steer;
                }
                if (Math.Abs(before - 1f) > 1e-4f || !monotone || Math.Abs(k.steer - KeyShaper.CapFloor) > 1e-4f || biggest > 0.1f)
                    problems.Append("climbing from 8 to 30 m/s with the key held goes " + F(before, "F3") + " -> " + F(k.steer, "F3") + " (monotone " + monotone + ", biggest step " + F(biggest, "F3") + "); ");
            }
            Check("KEYS_CAP_THE_STEERING_AT_SPEED", problems.Length == 0,
                problems.Length == 0 ? "the share a steering key reaches falls from 1 at " + F(KeyShaper.CapSlow, "F0") + " m/s to " + F(KeyShaper.CapFloor) + " at " + F(KeyShaper.CapFast, "F0") + " m/s and stays there; a key held to the right settles at " + settled.TrimEnd(' ', ',') + " and to the left at the same with the sign changed; with the speed rising under a held key it eases down to the cap without a jump" : problems.ToString(),
                "1 up to 10 m/s, 0.7 from 25 m/s, a linear fall between, the same on both sides, no jump when the speed changes");
        }

        // ---- a hand on the keys, at any frame rate, whatever it does
        {
            var problems = new StringBuilder();
            var rnd = new Random(7);
            foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 144f })
            {
                var k = new KeyShaper();
                bool up = false, down = false, rear = false, left = false, right = false;
                float speed = 20f, clock = 0f, next = 0f, worstSteer = 0f, worstGas = 0f, worstBrake = 0f;
                for (int i = 0; i < (int)(120f / dt); i++)
                {
                    clock += dt;
                    if (clock >= next)
                    {
                        next = clock + 0.05f + (float)rnd.NextDouble() * 0.4f;
                        up = rnd.Next(2) == 0; down = rnd.Next(4) == 0; rear = rnd.Next(5) == 0; left = rnd.Next(3) == 0; right = rnd.Next(3) == 0;
                    }
                    speed = Math.Max(0f, Math.Min(80f, speed + ((float)rnd.NextDouble() - 0.5f) * 16f * dt));
                    float s0 = k.steer, t0 = k.thr, b0 = k.bf, r0 = k.br;
                    k.Step(up, down, rear, left, right, speed, dt);
                    worstSteer = Math.Max(worstSteer, Math.Abs(k.steer - s0) / dt); worstGas = Math.Max(worstGas, Math.Abs(k.thr - t0) / dt);
                    worstBrake = Math.Max(worstBrake, Math.Max(Math.Abs(k.bf - b0), Math.Abs(k.br - r0)) / dt);
                    if (Math.Abs(k.steer) > 1f || k.thr < 0f || k.thr > 1f || k.bf < 0f || k.bf > 1f || k.br < 0f || k.br > 1f) { problems.Append("a key left its range at " + F(clock, "F1") + " s (" + F(1f / dt, "F0") + " Hz); "); break; }
                }
                if (worstSteer > 4.6f * 2.2f + 1e-3f || worstGas > 7f + 1e-3f || worstBrake > 9f + 1e-3f)
                    problems.Append(F(1f / dt, "F0") + " Hz: the steering moved " + F(worstSteer, "F1") + " per second, the gas " + F(worstGas, "F1") + ", a brake " + F(worstBrake, "F1") + "; ");
            }
            Check("KEYS_NEVER_LEAP_WHATEVER_THE_FRAME_RATE", problems.Length == 0,
                problems.Length == 0 ? "two minutes of keys pressed at random and a speed wandering from 0 to 80 m/s, at 30, 60 and 144 frames a second: every key stays in its range and the steering never moves faster than 10.1 a second, the gas 7 and the brakes 9" : problems.ToString(),
                "keys in range, steering at most 10.12 a second, gas at most 7, brakes at most 9");
        }

        // ---- a person on the keys, on the real circuits (the instrument is described at the top of the page): the same twelve rides (two bikes, six circuits) for every rider
        int[] circuits = { 0, 1, 2, 4, 5, 8 };
        int[] bikes = { 0, 3 };
        var quick = new HumanStyle { name = "quick", reaction = 0.04f, decision = 0.06f, deadband = 0.10f };
        var average = new HumanStyle { name = "average", reaction = 0.10f, decision = 0.10f, deadband = 0.15f };
        var clumsy = new HumanStyle { name = "clumsy", reaction = 0.25f, decision = 0.15f, deadband = 0.20f };
        const float easy = 0.9f, hard = 1.0f;       // the share of the speed the racing line allows that the rider asks of the bike
        var stickEasy = RideAll(bikes, circuits, 0, easy, true, quick);
        var stickHard = RideAll(bikes, circuits, 0, hard, true, quick);
        var quickEasy = RideAll(bikes, circuits, 0, easy, false, quick);
        var quickHard = RideAll(bikes, circuits, 0, hard, false, quick);
        var averageEasy = RideAll(bikes, circuits, 0, easy, false, average);
        var averageHard = RideAll(bikes, circuits, 0, hard, false, average);
        {
            bool ok = stickEasy.falls == 0 && stickHard.falls == 0 && stickEasy.unfinished + stickHard.unfinished == 0
                && quickEasy.unfinished + quickHard.unfinished + averageEasy.unfinished + averageHard.unfinished == 0
                && quickEasy.falls <= 1 && quickHard.falls <= 1 && averageEasy.falls <= 4 && averageHard.falls <= 4;
            Check("KEYBOARD_RIDERS_FINISH_THE_CIRCUITS_STANDING_UP", ok,
                "twelve rides (two bikes, six circuits) with all the aids (the default) and the cap: at " + F(easy, "F1") + " of the line's speed the quick rider has " + quickEasy.falls + " falls and the average " + averageEasy.falls
                + ", at " + F(hard, "F1") + " " + quickHard.falls + " and " + averageHard.falls + "; every ride finishes, and the reference with a stick has " + (stickEasy.falls + stickHard.falls) + " falls",
                "every ride finishes, at most 1 fall for the quick rider and 4 for the average in twelve rides, none for the stick");
        }
        {
            float qe = TimeAgainst(quickEasy, stickEasy), qh = TimeAgainst(quickHard, stickHard), ae = TimeAgainst(averageEasy, stickEasy), ah = TimeAgainst(averageHard, stickHard);
            Check("KEYBOARD_COSTS_LITTLE_TIME_AGAINST_A_STICK", qe <= 1.08f && qh <= 1.08f && ae <= 1.12f && ah <= 1.12f,
                "the same riders with a stick against the keys, ride by ride: the quick rider takes x" + F(qe, "F3") + " (" + F(easy, "F1") + ") and x" + F(qh, "F3") + " (" + F(hard, "F1") + "), the average x" + F(ae, "F3") + " and x" + F(ah, "F3"),
                "the quick rider at most 8% slower than with a stick, the average at most 12%");
        }
        {
            var quickFree = RideAll(bikes, circuits, 0, hard, false, quick, 1f);
            var averageFree = RideAll(bikes, circuits, 0, hard, false, average, 1f);
            var quickFreeEasy = RideAll(bikes, circuits, 0, easy, false, quick, 1f);
            var clumsyCapped = RideAll(bikes, circuits, 0, easy, false, clumsy);
            var clumsyFree = RideAll(bikes, circuits, 0, easy, false, clumsy, 1f);
            float tq = TimeAgainst(quickHard, stickHard), tqf = TimeAgainst(quickFree, stickHard), ta = TimeAgainst(averageHard, stickHard), taf = TimeAgainst(averageFree, stickHard);
            float below = TimeAgainst(quickEasy, quickFreeEasy);
            bool ok = quickFree.falls >= quickHard.falls + 3 && averageFree.falls >= averageHard.falls + 10 && tq < tqf && ta < taf && clumsyCapped.falls * 2 <= clumsyFree.falls
                && quickEasy.falls <= quickFreeEasy.falls && below <= 1.01f;
            Check("THE_STEERING_CAP_KEEPS_THE_KEYBOARD_ON_THE_BIKE", ok,
                "at " + F(hard, "F1") + " of the line's speed the quick rider has " + quickHard.falls + " falls with the cap and " + quickFree.falls + " without (x" + F(tq, "F3") + " against x" + F(tqf, "F3") + " the time of a stick), the average " + averageHard.falls + " and "
                + averageFree.falls + " (x" + F(ta, "F3") + " against x" + F(taf, "F3") + "); a clumsy rider at " + F(easy, "F1") + " has " + clumsyCapped.falls + " falls with the cap and " + clumsyFree.falls + " without; below the limit the cap costs the quick rider x" + F(below, "F3"),
                "far fewer falls and less time near the limit, half the falls for a clumsy rider, no cost to a skilled one below the limit");
        }
        {
            var averageSporty = RideAll(bikes, circuits, 1, easy, false, average);
            var clumsyCapped = RideAll(bikes, circuits, 0, easy, false, clumsy);
            var clumsySporty = RideAll(bikes, circuits, 1, easy, false, clumsy);
            float time = TimeAgainst(averageEasy, averageSporty);
            bool ok = averageSporty.falls >= averageEasy.falls * 3 + 2 && clumsySporty.falls >= clumsyCapped.falls * 3 && time <= 1.03f;
            Check("THE_LEAN_ASSIST_KEEPS_THE_KEYBOARD_ON_THE_BIKE", ok,
                "at " + F(easy, "F1") + " of the line's speed the average rider has " + averageEasy.falls + " falls with all the aids and " + averageSporty.falls + " with the sporty ones (no lean assist), a clumsy rider " + clumsyCapped.falls + " and " + clumsySporty.falls
                + "; the assist costs the average rider x" + F(time, "F3") + " the time",
                "at least three times fewer falls with the lean assist and no more than 3% more time");
        }
    }
}
