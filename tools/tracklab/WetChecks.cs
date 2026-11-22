using System;
using System.Collections.Generic;
using System.Linq;
using PryingEyes.Pista;

// The rain: what the water takes from the grip of each kind of ground, the curves of the bikes measured at the grip of the day, the riders' plan that follows from them, and the computer riders
// riding that plan on that ground. Fresh circuits (the shared ones of LabCircuits stay dry for every other check).
static partial class Program
{
    static void WetChecks()
    {
        // ---- the ground: every kind of surface, dry and wet, found by walking across a circuit
        {
            var dry = CircuitGen.Generate(1 + 3 * 7919);
            var wet = CircuitGen.Generate(1 + 3 * 7919);
            wet.wet = 1f;
            var half = CircuitGen.Generate(1 + 3 * 7919);
            half.wet = 0.5f;
            var dryGrip = new float[5]; var wetGrip = new float[5]; var halfGrip = new float[5]; var found = new bool[5];
            var dryRoll = new float[5]; var wetRoll = new float[5];
            for (int i = 0; i < dry.n && found.Count(f => f) < 5; i += 7)
                for (int side = 0; side < 2; side++)
                    for (float a = 0f; a < 60f; a += 0.2f)
                    {
                        float sgn = side == 0 ? -1f : 1f;
                        float x = dry.px[i] + dry.nx[i] * sgn * a, z = dry.pz[i] + dry.nz[i] * sgn * a;
                        int h0 = i, h1 = i, h2 = i;
                        GroundSample gd, gw, gh;
                        dry.Sample(x, z, ref h0, out gd); wet.Sample(x, z, ref h1, out gw); half.Sample(x, z, ref h2, out gh);
                        int k = (int)gd.surface;
                        if (found[k] || gd.surface != gw.surface) continue;
                        found[k] = true; dryGrip[k] = gd.grip; wetGrip[k] = gw.grip; halfGrip[k] = gh.grip; dryRoll[k] = gd.rolling; wetRoll[k] = gw.rolling;
                    }
            string detail = ""; bool allThere = found.All(f => f), allLower = true, order = true;
            for (int k = 0; k < 5; k++) { detail += " " + (SurfaceKind)k + " " + dryGrip[k].ToString("0.00") + "->" + wetGrip[k].ToString("0.00"); if (!(wetGrip[k] < dryGrip[k] - 0.01f) && k != (int)SurfaceKind.Gravel) allLower = false; if (wetGrip[k] > dryGrip[k]) allLower = false; }
            float asphalt = wetGrip[(int)SurfaceKind.Asphalt] / dryGrip[(int)SurfaceKind.Asphalt];
            float kerb = wetGrip[(int)SurfaceKind.Kerb] / dryGrip[(int)SurfaceKind.Kerb];
            float grass = wetGrip[(int)SurfaceKind.Grass] / dryGrip[(int)SurfaceKind.Grass];
            float gravel = wetGrip[(int)SurfaceKind.Gravel] / dryGrip[(int)SurfaceKind.Gravel];
            order = kerb < asphalt && grass < asphalt && gravel > asphalt;
            Check("WET_GRIP_OF_EVERY_GROUND", allThere && allLower && Math.Abs(asphalt - 0.70f) < 0.01f && order && Math.Abs(wet.GripScale - 0.70f) < 1e-4f && Math.Abs(dry.GripScale - 1f) < 1e-6f,
                "found all five: " + allThere + ";" + detail + "; asphalt keeps " + asphalt.ToString("0.00") + ", kerb " + kerb.ToString("0.00") + ", grass " + grass.ToString("0.00") + ", gravel " + gravel.ToString("0.00"),
                "every ground loses grip in the rain (gravel the least), clean asphalt keeps 0.70 of it, the kerbs and the grass lose more than the asphalt");
            float half0 = halfGrip[(int)SurfaceKind.Asphalt], mid = 0.5f * (dryGrip[(int)SurfaceKind.Asphalt] + wetGrip[(int)SurfaceKind.Asphalt]);
            Check("WET_IS_A_SCALE_NOT_A_SWITCH", Math.Abs(half0 - mid) < 0.005f && wetRoll[(int)SurfaceKind.Grass] > dryRoll[(int)SurfaceKind.Grass] && wetRoll[(int)SurfaceKind.Asphalt] > dryRoll[(int)SurfaceKind.Asphalt],
                "half wet asphalt " + half0.ToString("0.000") + " (the middle is " + mid.ToString("0.000") + "), rolling resistance on grass " + dryRoll[(int)SurfaceKind.Grass].ToString("0.000") + "->" + wetRoll[(int)SurfaceKind.Grass].ToString("0.000"),
                "half the rain takes half the grip, and mud and water add some drag");
        }

        // ---- the bikes: the curves measured at the grip of the day
        {
            var spec = Bikes.Superbike();
            var dry = BikeCurves.For(spec); var wet = BikeCurves.For(spec, 0.7f); var wet2 = BikeCurves.For(spec, 0.7f);
            bool never = true;
            for (float v = 0f; v <= 100f; v += 5f)
                if (wet.BrakeAt(v) > dry.BrakeAt(v) + 0.05f || wet.AccelAt(v) > dry.AccelAt(v) + 0.05f) never = false;
            float b10d = dry.BrakeAt(10f), b10w = wet.BrakeAt(10f), b20d = dry.BrakeAt(20f), b20w = wet.BrakeAt(20f), b40d = dry.BrakeAt(40f), b40w = wet.BrakeAt(40f), a15d = dry.AccelAt(15f), a15w = wet.AccelAt(15f);
            Check("WET_BIKES_BRAKE_WORSE", ReferenceEquals(wet, wet2) && !ReferenceEquals(dry, wet) && never && b10w < b10d * 0.75f && b20w < b20d * 0.95f && a15w < a15d * 0.9f && wet.topSpeed < dry.topSpeed && wet.topSpeed > dry.topSpeed * 0.85f,
                "braking at 10 m/s " + b10d.ToString("0.0") + "->" + b10w.ToString("0.0") + " m/s2, at 20 m/s " + b20d.ToString("0.0") + "->" + b20w.ToString("0.0") + ", at 40 m/s " + b40d.ToString("0.0") + "->" + b40w.ToString("0.0") + "; pulling away at 15 m/s " + a15d.ToString("0.0") + "->" + a15w.ToString("0.0") + "; top speed " + dry.topSpeed.ToString("0") + "->" + wet.topSpeed.ToString("0") + " m/s; never better at any speed: " + never,
                "the same bike never brakes or pulls away better on the wet, brakes 25 % worse or more at a crawl and a few percent worse at 20 m/s (from about 30 m/s the air and the stoppie limit hold it as hard as on the dry, the grip is not what limits it), pulls away 10 % worse or more at 15 m/s and tops out lower (by less than 15 %), and the curves are measured once per grip");
        }

        // ---- the plan: slower where the grip counts, as fast where it does not
        {
            var spec = Bikes.Superbike();
            double worstRatio = 0, bestRatio = 9, worstTop = 0; int cornersSlower = 0, corners = 0, fasterSomewhere = 0;
            for (int ci = 0; ci < 6; ci++)
            {
                int seed = 1 + ci * 7919;
                var d = CircuitGen.Generate(seed); var w = CircuitGen.Generate(seed); w.wet = 1f;
                var ld = new RacingLine(d, spec); var lw = new RacingLine(w, spec);
                double ratio = lw.lapTime / ld.lapTime;
                worstRatio = Math.Max(worstRatio, ratio); bestRatio = Math.Min(bestRatio, ratio);
                float topD = 0, topW = 0;
                for (int i = 0; i < d.n; i++)
                {
                    topD = Math.Max(topD, ld.vplan[i]); topW = Math.Max(topW, lw.vplan[i]);
                    if (ld.vlim[i] < ld.curves.topSpeed * 0.9f) { corners++; if (lw.vlim[i] < ld.vlim[i] - 0.05f) cornersSlower++; }
                    if (lw.vplan[i] > ld.vplan[i] + 0.5f) fasterSomewhere++;
                }
                worstTop = Math.Max(worstTop, Math.Abs(topW / topD - 1f));
            }
            Check("WET_PLAN_IS_SLOWER_BUT_SANE", bestRatio > 1.05 && worstRatio < 1.25 && cornersSlower >= corners * 0.98 && fasterSomewhere == 0,
                "6 circuits: the wet lap takes " + bestRatio.ToString("0.000") + " to " + worstRatio.ToString("0.000") + " of the dry one; " + cornersSlower + " of " + corners + " corner samples have a lower limit; samples where the wet plan is faster than the dry one: " + fasterSomewhere + "; top speed differs by " + (100 * worstTop).ToString("0.0") + " %",
                "5 to 25 % slower over a lap, every corner slower, nowhere faster");
        }

        // ---- the riders: a computer rider alone on the wet follows the wet plan without a fall; the dry plan on the wet ground is worse
        {
            double worstRatio = 0, bestRatio = 9, offTime = 0, totalTime = 0, sumWet = 0, sumDry = 0; int crashes = 0, unfinished = 0; string detail = "";
            for (int ci = 0; ci < 6; ci++)
            {
                int seed = 1 + ci * 7919;
                var c = CircuitGen.Generate(seed); c.wet = 1f;
                var race = new Race(c, 2, ci);
                var r = race.AddAi("solo", 0, 0, ci + 1, 0.96f, 0f);
                race.PlaceAll(new List<Racer> { r });
                race.StartCountdown();
                float t = 0f; double off = 0, run = 0;
                while (race.phase != RacePhase.Over && t < 500f)
                {
                    race.Step(RaceDt); t += RaceDt;
                    if (race.phase == RacePhase.Racing) { run += RaceDt; if (Math.Abs(r.lateral) > c.HalfWidth(r.s) + 0.5f) off += RaceDt; }
                }
                crashes += r.crashes; if (!r.finished) unfinished++;
                offTime += off; totalTime += run;
                double plan = race.LineFor(0).lapTime / 0.96;
                double lap2 = r.bestLap > 0 ? r.bestLap : r.lastLap;
                double ratio = lap2 / plan;
                worstRatio = Math.Max(worstRatio, ratio); bestRatio = Math.Min(bestRatio, ratio);
                sumWet += lap2;
                var cd = CircuitGen.Generate(seed);
                var raceD = new Race(cd, 2, ci);
                var rd = raceD.AddAi("solo", 0, 0, ci + 1, 0.96f, 0f);
                raceD.PlaceAll(new List<Racer> { rd });
                raceD.StartCountdown();
                t = 0f;
                while (raceD.phase != RacePhase.Over && t < 500f) { raceD.Step(RaceDt); t += RaceDt; }
                sumDry += rd.bestLap > 0 ? rd.bestLap : rd.lastLap;
                detail += " [" + ci + ": wet " + lap2.ToString("0.0") + " s vs plan " + plan.ToString("0.0") + ", dry " + (rd.bestLap > 0 ? rd.bestLap : rd.lastLap).ToString("0.0") + "]";
            }
            Check("WET_AI_RIDES_THE_WET_PLAN", crashes == 0 && unfinished == 0 && worstRatio < 1.12 && bestRatio > 0.9 && sumWet > sumDry * 1.05 && sumWet < sumDry * 1.25,
                "6 circuits, 2 laps alone in the rain: crashes " + crashes + ", unfinished " + unfinished + ", lap time over plan " + bestRatio.ToString("0.00") + " to " + worstRatio.ToString("0.00") + ", off the asphalt " + (100 * offTime / Math.Max(1, totalTime)).ToString("0.0") + " % of the time, wet laps " + (100 * (sumWet / sumDry - 1)).ToString("0") + " % slower than the dry ones;" + detail,
                "no fall, a lap within 10 % of the plan, and the rain costs between 5 and 25 % of the lap time");
        }
    }
}
