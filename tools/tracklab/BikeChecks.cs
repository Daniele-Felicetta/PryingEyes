using System;
using System.Collections.Generic;
using PryingEyes.Pista;
using Production.Racing;

/// <summary>The motorcycle physics on a flat road: what a 1000 cc superbike is known to do (top speed, 0-100, braking, wheelies, lean) and the limits the model must respect.</summary>
static partial class Program
{
    const float Dt = 1f / 120f;

    /// <summary>Run a bike for 'seconds' with an input function of time; the callback may watch every step. Returns the bike.</summary>
    static BikeModel Ride(BikeSpec spec, IGround ground, float seconds, Func<float, BikeModel, BikeInputs> inputs, Action<float, BikeModel> watch = null,
        Action<BikeModel> setup = null)
    {
        var bike = new BikeModel(spec, 7);
        bike.Reset(0f, 0f, 0f, 0f);
        if (setup != null) setup(bike);
        for (float t = 0f; t < seconds; t += Dt)
        {
            bike.Step(Dt, ground, inputs(t, bike));
            if (watch != null) watch(t, bike);
        }
        return bike;
    }

    static BikeInputs Full(float thr = 1f) { return new BikeInputs { throttle = thr }; }

    static void BikeChecks()
    {
        var flat = new FlatGround();
        var sb = Bikes.Superbike();

        // ---- the engine on paper: peak power and the gearing
        float peakRpm;
        float kw = sb.PeakPowerKw(out peakRpm);
        float hp = kw * 1.341f;
        Check("BIKE_SUPERBIKE_POWER", hp > 175f && hp < 205f, "peak " + hp.ToString("F0") + " hp at " + peakRpm.ToString("F0") + " rpm", "175-205 hp");
        float firstTop = sb.redlineRpm / sb.TotalRatio(1) * RacingMath.TwoPi / 60f * sb.wheelRadius * 3.6f;
        float lastTop = sb.redlineRpm / sb.TotalRatio(6) * RacingMath.TwoPi / 60f * sb.wheelRadius * 3.6f;
        Check("BIKE_GEARING", firstTop > 105f && firstTop < 150f && lastTop > 300f, "first gear to " + firstTop.ToString("F0") + " km/h, sixth to " + lastTop.ToString("F0") + " km/h at the redline", "first 105-150, sixth above 300");

        // ---- standing start: 0-100 and 0-200 km/h, and the top speed
        float t100 = -1f, t200 = -1f, maxPitch = 0f, vmax = 0f;
        Ride(sb, flat, 40f, (t, b) => Full(), (t, b) =>
        {
            if (t100 < 0f && b.u * 3.6f >= 100f) t100 = t;
            if (t200 < 0f && b.u * 3.6f >= 200f) t200 = t;
            maxPitch = Math.Max(maxPitch, b.freePitch);
            vmax = Math.Max(vmax, b.u * 3.6f);
        });
        Check("BIKE_0_100", t100 > 2.4f && t100 < 3.6f, "0-100 km/h in " + t100.ToString("F2") + " s", "2.4-3.6 s");
        Check("BIKE_0_200", t200 > 5.5f && t200 < 9.5f, "0-200 km/h in " + t200.ToString("F2") + " s", "5.5-9.5 s");
        Check("BIKE_TOP_SPEED", vmax > 285f && vmax < 320f, "top speed " + vmax.ToString("F0") + " km/h in 40 s", "285-320 km/h");
        Check("BIKE_ANTI_WHEELIE_KEEPS_FRONT_LOW", maxPitch < 0.20f, "front lifted to " + (maxPitch * RacingMath.Rad2Deg).ToString("F1") + " degrees with the aids on", "under 11.5 degrees");

        // ---- without the aids a full-throttle launch in first and second gear wheelies; held, it loops
        float maxPitchRaw = 0f; bool looped = false;
        Ride(sb, flat, 8f, (t, b) => Full(), (t, b) => { maxPitchRaw = Math.Max(maxPitchRaw, b.freePitch); if (b.crashed && b.crashKind == CrashKind.Loop) looped = true; },
            b => { b.antiWheelie = false; b.tcs = false; });
        Check("BIKE_WHEELIE_WITHOUT_AIDS", maxPitchRaw > 0.45f, "front up to " + (maxPitchRaw * RacingMath.Rad2Deg).ToString("F0") + " degrees at full throttle with no aids", "above 26 degrees");
        Check("BIKE_LOOPS_IF_HELD", looped, looped ? "flipped over backwards" : "never flipped", "flips");

        // ---- braking from 100 and from 200 km/h with the front and rear brake and ABS
        for (int pass = 0; pass < 2; pass++)
        {
            float v0 = pass == 0 ? 100f / 3.6f : 200f / 3.6f;
            float dist = 0f, tStop = 0f, peakDec = 0f, stoppie = 0f; bool absSeen = false; bool crashedB = false;
            var bike = new BikeModel(sb, 3);
            bike.Reset(0f, 0f, 0f, 0f);
            bike.u = v0; bike.omegaF = bike.omegaR = v0 / sb.wheelRadius; bike.gear = pass == 0 ? 3 : 5;
            float prevU = v0;
            for (float t = 0f; t < 12f; t += Dt)
            {
                bike.Step(Dt, flat, new BikeInputs { brakeFront = 1f, brakeRear = 0.35f });
                dist = bike.z; tStop = t;
                peakDec = Math.Max(peakDec, (prevU - bike.u) / Dt / RacingMath.G);
                prevU = bike.u;
                stoppie = Math.Max(stoppie, -bike.freePitch);
                absSeen |= bike.absActive; crashedB |= bike.crashed;
                if (bike.u < 0.2f) break;
            }
            if (pass == 0)
            {
                Check("BIKE_BRAKE_100", dist > 26f && dist < 50f && !crashedB, "100 km/h to a stop in " + dist.ToString("F1") + " m, " + tStop.ToString("F2") + " s, peak " + peakDec.ToString("F2") + " g", "26-50 m");
                Check("BIKE_ABS_WORKS", absSeen, absSeen ? "ABS stepped in" : "ABS never needed", "ABS steps in at full brake");
            }
            else Check("BIKE_BRAKE_200", dist > 100f && dist < 175f && !crashedB, "200 km/h to a stop in " + dist.ToString("F0") + " m, " + tStop.ToString("F2") + " s, rear wheel up " + (stoppie * RacingMath.Rad2Deg).ToString("F0") + " degrees", "100-175 m");
        }

        // ---- without the rear-lift control, full front brake at speed lifts the rear wheel
        {
            var bike = new BikeModel(sb, 3); bike.antiStoppie = false;
            bike.Reset(0f, 0f, 0f, 0f);
            bike.u = 200f / 3.6f; bike.omegaF = bike.omegaR = bike.u / sb.wheelRadius; bike.gear = 5;
            float up = 0f;
            for (float t = 0f; t < 4f; t += Dt)
            {
                bike.Step(Dt, flat, new BikeInputs { brakeFront = 1f });
                up = Math.Max(up, -bike.freePitch);
                if (bike.crashed) break;
            }
            Check("BIKE_STOPPIE_WITHOUT_AIDS", up > 0.25f, "rear wheel up to " + (up * RacingMath.Rad2Deg).ToString("F0") + " degrees at full front brake from 200 km/h", "above 14 degrees");
        }

        // ---- the same without ABS at full front brake locks the front wheel
        {
            var bike = new BikeModel(sb, 3); bike.abs = false; bike.antiStoppie = false;
            bike.Reset(0f, 0f, 0f, 0f);
            bike.u = 150f / 3.6f; bike.omegaF = bike.omegaR = bike.u / sb.wheelRadius; bike.gear = 4;
            bool locked = false;
            for (float t = 0f; t < 6f; t += Dt)
            {
                bike.Step(Dt, flat, new BikeInputs { brakeFront = 1f });
                if (bike.slipFront < -0.5f) locked = true;
                if (bike.u < 0.2f || bike.crashed) break;
            }
            Check("BIKE_FRONT_LOCKS_WITHOUT_ABS", locked && bike.crashed, locked ? (bike.crashed ? "front locked and folded: " + bike.crashKind : "front locked, no fall") : "front never locked", "locks and falls");
        }

        // ---- steady turns: the lean the physics demands, tan(lean) = v2 k / g, and the yaw rate it gives
        foreach (float v in new[] { 12f, 25f, 40f })
        {
            float kappa = v < 30f ? 1f / 70f : 1f / 220f;
            var bike = Ride(sb, flat, 6f, (t, b) => new BikeInputs { useCurvature = true, curvature = kappa, throttle = ThrottleToHold(b, v) },
                null, b => { b.u = v; b.omegaF = b.omegaR = v / sb.wheelRadius; b.gear = v < 20f ? 3 : v < 33f ? 4 : 5; });
            float wanted = (float)Math.Atan(v * v * kappa / RacingMath.G);
            float cg = bike.lean + bike.hang * sb.hangMax;
            float radius = bike.u / Math.Max(1e-3f, Math.Abs(bike.yawRate));
            Check("BIKE_STEADY_TURN_" + (int)(v * 3.6f), Math.Abs(cg - wanted) < 0.03f && Math.Abs(radius - 1f / kappa) < 8f && !bike.crashed,
                "at " + (v * 3.6f).ToString("F0") + " km/h on a " + (1f / kappa).ToString("F0") + " m radius: lean " + (bike.lean * RacingMath.Rad2Deg).ToString("F1") + " + body " + (bike.hang * sb.hangMax * RacingMath.Rad2Deg).ToString("F1") + " deg (needs " + (wanted * RacingMath.Rad2Deg).ToString("F1") + "), radius " + radius.ToString("F0") + " m",
                "lean within 1.7 degrees, the radius asked for");
        }

        // ---- the limit of a turn: with the lean assist the bike holds about mu g and does not fall; without it, a lean past the grip is a lowside
        {
            float latMax = 0f; bool fell = false;
            Ride(sb, flat, 8f, (t, b) => new BikeInputs { steer = 1f, throttle = ThrottleToHold(b, 30f) }, (t, b) => { if (t > 3f) latMax = Math.Max(latMax, Math.Abs(b.latG)); fell |= b.crashed; },
                b => { b.u = 30f; b.omegaF = b.omegaR = 30f / sb.wheelRadius; b.gear = 4; });
            Check("BIKE_LEAN_ASSIST_HOLDS_THE_LIMIT", !fell && latMax > 1.05f && latMax < 1.45f, "full lock at 108 km/h: " + latMax.ToString("F2") + " g, fell " + fell, "1.05-1.45 g and no fall");
            bool fell2 = false; CrashKind kind = CrashKind.None; float fallAt = 0f;
            Ride(sb, flat, 8f, (t, b) => new BikeInputs { steer = 1f, throttle = ThrottleToHold(b, 30f) }, (t, b) => { if (b.crashed && !fell2) { fell2 = true; kind = b.crashKind; fallAt = t; } },
                b => { b.leanAssist = false; b.u = 30f; b.omegaF = b.omegaR = 30f / sb.wheelRadius; b.gear = 4; });
            Check("BIKE_PAST_THE_GRIP_FALLS", fell2, fell2 ? "fell after " + fallAt.ToString("F1") + " s: " + kind : "stayed up", "falls when the lean passes the grip");
        }

        // ---- braking hard in a corner (trail braking too hard) pushes the front past its grip
        {
            bool fell = false; CrashKind kind = CrashKind.None;
            Ride(sb, flat, 6f, (t, b) => new BikeInputs { useCurvature = true, curvature = 1f / 60f, brakeFront = t > 2f ? 1f : 0f, throttle = t > 2f ? 0f : ThrottleToHold(b, 33f) },
                (t, b) => { if (b.crashed && !fell) { fell = true; kind = b.crashKind; } }, b => { b.leanAssist = false; b.abs = false; b.u = 33f; b.omegaF = b.omegaR = 33f / sb.wheelRadius; b.gear = 4; });
            Check("BIKE_HARD_BRAKE_MID_CORNER_FALLS", fell, fell ? "front let go: " + kind : "held", "falls");
            float shallow = 0f; bool fell2 = false;
            Ride(sb, flat, 6f, (t, b) => new BikeInputs { useCurvature = true, curvature = 1f / 120f, brakeFront = t > 2f ? 0.35f : 0f, throttle = t > 2f ? 0f : ThrottleToHold(b, 25f) },
                (t, b) => { shallow = Math.Max(shallow, Math.Abs(b.latG)); fell2 |= b.crashed; }, b => { b.u = 25f; b.omegaF = b.omegaR = 25f / sb.wheelRadius; b.gear = 4; });
            Check("BIKE_GENTLE_TRAIL_BRAKING_HOLDS", !fell2, "gentle front brake in a wide curve: " + shallow.ToString("F2") + " g, fell " + fell2, "no fall");
        }

        // ---- a crest unloads the tires: the lean assist then allows less lateral g
        {
            float latFlat = 0f, latCrest = 0f; string how = "";
            Ride(sb, flat, 5f, (t, b) => new BikeInputs { steer = 1f, throttle = ThrottleToHold(b, 35f) }, (t, b) => { if (t > 3f) latFlat = Math.Max(latFlat, Math.Abs(b.latG)); }, b => { b.u = 35f; b.omegaF = b.omegaR = 35f / sb.wheelRadius; b.gear = 4; });
            Ride(sb, new CrestGround(), 5f, (t, b) => new BikeInputs { steer = 1f, throttle = ThrottleToHold(b, 35f) }, (t, b) => { if (t > 3f) latCrest = Math.Max(latCrest, Math.Abs(b.latG)); if (b.crashed && how == "") how = " (fell: " + b.crashKind + " at " + t.ToString("F2") + " s)"; }, b => { b.u = 35f; b.omegaF = b.omegaR = 35f / sb.wheelRadius; b.gear = 4; });
            Check("BIKE_CREST_UNLOADS_THE_TIRES", latCrest > 0.3f && latCrest < latFlat * 0.93f, "lateral g on the flat " + latFlat.ToString("F2") + ", over a crest " + latCrest.ToString("F2") + how, "less on a crest, but the assist keeps the bike up");
        }

        // ---- grass is slippery: the same corner at the same speed is much tighter in g
        {
            var grass = new FlatGround { grip = 0.4f };
            float lat = 0f;
            Ride(sb, grass, 5f, (t, b) => new BikeInputs { steer = 1f, throttle = ThrottleToHold(b, 20f) }, (t, b) => { if (t > 2f) lat = Math.Max(lat, Math.Abs(b.latG)); }, b => { b.u = 20f; b.omegaF = b.omegaR = 20f / sb.wheelRadius; b.gear = 3; });
            Check("BIKE_GRASS_LIMITS_LATERAL_G", lat < 0.65f, "on slippery ground the corner gives " + lat.ToString("F2") + " g", "under 0.65 g");
        }

        // ---- the other two classes are different machines
        {
            var s6 = Bikes.Supersport(); var r7 = Bikes.Roadster();
            float v6 = 0f, v7 = 0f, t6 = -1f, t7 = -1f;
            Ride(s6, flat, 40f, (t, b) => Full(), (t, b) => { v6 = Math.Max(v6, b.u * 3.6f); if (t6 < 0f && b.u * 3.6f >= 100f) t6 = t; });
            Ride(r7, flat, 40f, (t, b) => Full(), (t, b) => { v7 = Math.Max(v7, b.u * 3.6f); if (t7 < 0f && b.u * 3.6f >= 100f) t7 = t; });
            Check("BIKE_CLASSES_DIFFER", v6 > 240f && v6 < vmax - 10f && v7 > 190f && v7 < v6 - 10f && t6 > t100 && t7 > t6,
                "top speed 1000: " + vmax.ToString("F0") + ", 600: " + v6.ToString("F0") + ", 750 roadster: " + v7.ToString("F0") + " km/h; 0-100 " + t100.ToString("F1") + " / " + t6.ToString("F1") + " / " + t7.ToString("F1") + " s",
                "superbike fastest, then supersport, then roadster");
        }

        // ---- the gearbox goes through all the gears and never over the limiter for long
        {
            int topGear = 0; float maxRpm = 0f;
            Ride(sb, flat, 30f, (t, b) => Full(), (t, b) => { topGear = Math.Max(topGear, b.gear); maxRpm = Math.Max(maxRpm, b.rpm); });
            Check("BIKE_GEARBOX_AUTO", topGear == 6 && maxRpm < sb.limiterRpm + 700f, "reached gear " + topGear + ", highest rpm " + maxRpm.ToString("F0"), "gear 6, rpm under the limiter");
        }

        // ---- determinism: the same rides give the same numbers (no machine-dependent randomness)
        {
            var a = Ride(sb, flat, 6f, (t, b) => new BikeInputs { throttle = 0.8f, steer = (float)Math.Sin(t * 1.3f) });
            var b2 = Ride(sb, flat, 6f, (t, b) => new BikeInputs { throttle = 0.8f, steer = (float)Math.Sin(t * 1.3f) });
            Check("BIKE_IS_DETERMINISTIC", a.x == b2.x && a.z == b2.z && a.heading == b2.heading, "two identical rides end at (" + a.x.ToString("F4") + ", " + a.z.ToString("F4") + ") and (" + b2.x.ToString("F4") + ", " + b2.z.ToString("F4") + ")", "bitwise equal");
        }
    }

    /// <summary>A throttle that holds a speed: the lab's stand-in for a rider who keeps a steady pace in a corner.</summary>
    static float ThrottleToHold(BikeModel b, float v)
    {
        return RacingMath.Clamp01(0.25f + (v - b.u) * 0.6f);
    }

    /// <summary>A hump in the road everywhere: a constant negative vertical curvature (a long crest).</summary>
    sealed class CrestGround : IGround
    {
        public void Sample(float x, float z, ref int hint, out GroundSample g)
        {
            g = new GroundSample { y = 0f, surface = SurfaceKind.Asphalt, grip = 1f, rolling = 0.015f, vertCurv = -1f / 1500f };
        }
        public bool Barrier(float x, float z, float radius, ref int hint, out float nx, out float nz, out float depth) { nx = nz = depth = 0f; return false; }
    }
}
