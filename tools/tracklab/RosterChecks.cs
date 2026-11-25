using System;
using System.Collections.Generic;
using PryingEyes.Pista;

/// <summary>The nine bikes: every one must be a believable machine of its kind (power, top speed, 0-100, braking), must not do anything silly with its aids on (loop at a launch, run over its own limiter),
/// and must be ridden round a lap by the computer rider without a fall, close to the lap its plan promises. The expectations are those of real bikes of each class.</summary>
static partial class Program
{
    struct Expect
    {
        public int index; public float hpLo, hpHi, topLo, topHi, t100Lo, t100Hi;
        public Expect(int index, float hpLo, float hpHi, float topLo, float topHi, float t100Lo, float t100Hi) { this.index = index; this.hpLo = hpLo; this.hpHi = hpHi; this.topLo = topLo; this.topHi = topHi; this.t100Lo = t100Lo; this.t100Hi = t100Hi; }
    }

    static readonly Expect[] Roster =
    {
        new Expect(0, 175, 205, 285, 320, 2.4f, 3.6f),
        new Expect(1, 105, 135, 245, 285, 3.0f, 4.2f),
        new Expect(2, 100, 140, 205, 255, 3.2f, 4.6f),
        new Expect(3, 40, 55, 190, 240, 3.8f, 7.5f),
        new Expect(4, 115, 140, 245, 285, 2.9f, 4.0f),
        new Expect(5, 170, 200, 270, 310, 2.5f, 3.5f),
        new Expect(6, 185, 210, 285, 330, 2.7f, 3.7f),
        new Expect(7, 175, 205, 290, 345, 2.2f, 3.6f),
        new Expect(8, 150, 172, 235, 280, 2.6f, 3.7f),
    };

    static void RosterChecks()
    {
        var flat = new FlatGround();
        Check("ROSTER_HAS_NINE_BIKES", Bikes.Count == 9 && Roster.Length == Bikes.Count, Bikes.Count + " bikes, " + Roster.Length + " expectations", "nine of each");
        var ids = new HashSet<string>(); var names = new HashSet<string>(); var skins = new HashSet<int>(); bool distinct = true;
        for (int i = 0; i < Bikes.Count; i++) { var s = Bikes.Get(i); distinct &= ids.Add(s.id) && names.Add(s.name) && skins.Add(s.skin) && s.skin >= 0 && s.skin < 12 && s.meshStyle >= 0 && s.meshStyle <= 2 && s.meshScale > 0.8f && s.meshScale < 1.2f; }
        Check("ROSTER_BIKES_ARE_DISTINCT", distinct, "ids, names, liveries, mesh styles", "every bike has its own id, name and colours, and a mesh the game can draw");
        int classes = 0; var seen = new HashSet<string>();
        for (int i = 0; i < Bikes.Count; i++) if (seen.Add(Bikes.Get(i).klass)) classes++;
        bool oneEach = true; for (int i = 0; i < Bikes.Count; i++) { var of = Bikes.OfClass(Bikes.Get(i).klass); oneEach &= Array.IndexOf(of, i) >= 0 && Bikes.Quickest(Bikes.Get(i).klass) >= 0; }
        Check("ROSTER_HAS_CLASSES", classes >= 5 && oneEach, classes + " classes (" + string.Join(", ", seen) + ")", "at least five classes, each bike in its own");

        foreach (var e in Roster)
        {
            var sp = Bikes.Get(e.index);
            float rpmAt; float hp = sp.PeakPowerKw(out rpmAt) * 1.341f;
            float t100 = -1f, vmax = 0f, maxPitch = 0f, maxRpm = 0f; int topGear = 0; bool crashed = false;
            Ride(sp, flat, 70f, (t, b) => Full(), (t, b) =>
            {
                if (t100 < 0f && b.u * 3.6f >= 100f) t100 = t;
                vmax = Math.Max(vmax, b.u * 3.6f); maxPitch = Math.Max(maxPitch, b.freePitch); maxRpm = Math.Max(maxRpm, b.rpm); topGear = Math.Max(topGear, b.gear); crashed |= b.crashed;
            });
            Check("ROSTER_" + sp.id.ToUpperInvariant() + "_IS_BELIEVABLE",
                hp >= e.hpLo && hp <= e.hpHi && vmax >= e.topLo && vmax <= e.topHi && t100 >= e.t100Lo && t100 <= e.t100Hi && !crashed && maxPitch < 0.30f && maxRpm < sp.limiterRpm + 700f && topGear >= sp.GearCount - 2,
                sp.name + ": " + hp.ToString("F0") + " hp at " + rpmAt.ToString("F0") + " rpm, " + (sp.Mass).ToString("F0") + " kg, top " + vmax.ToString("F0") + " km/h, 0-100 in " + t100.ToString("F2") + " s, front up to " + (maxPitch * PM.Rad2Deg).ToString("F1")
                    + " deg, " + topGear + "th gear, " + maxRpm.ToString("F0") + " rpm, fell " + crashed,
                "hp " + e.hpLo + "-" + e.hpHi + ", top " + e.topLo + "-" + e.topHi + ", 0-100 " + e.t100Lo + "-" + e.t100Hi + ", no loop, in the limiter, the top gears (a naked bike runs out of breath in fourth)");
        }

        // ---- braking from 150 km/h with the aids: a stop in a believable distance, without a fall
        {
            string detail = ""; bool all = true;
            for (int i = 0; i < Bikes.Count; i++)
            {
                var sp = Bikes.Get(i);
                var bike = new BikeModel(sp, 3); bike.Reset(0f, 0f, 0f, 0f);
                bike.u = 150f / 3.6f; bike.omegaF = bike.omegaR = bike.u / sp.wheelRadius; bike.gear = 4;
                for (float t = 0f; t < 10f && bike.u > 0.2f && !bike.crashed; t += Dt) bike.Step(Dt, flat, new BikeInputs { brakeFront = 1f, brakeRear = 0.3f });
                float d = bike.z;
                all &= !bike.crashed && d > 70f && d < 110f;
                detail += " " + sp.name + " " + d.ToString("F0") + " m;";
            }
            Check("ROSTER_BRAKES_ARE_BELIEVABLE", all, "150 km/h to a stop:" + detail, "70-110 m, no fall, for every bike");
        }

        // ---- the computer rider rides each bike round a lap, close to its plan, on two circuits
        {
            string detail = ""; bool all = true;
            var circuits = new[] { Circuits.Generate(4242), Circuits.Generate(22) };
            for (int i = 0; i < Bikes.Count; i++)
            {
                foreach (var c in circuits)
                {
                    var race = new Race(c, 2, 9);
                    var r = race.AddAi("solo", i, 0, 11 + i, 0.96f, 0f);
                    race.PlaceAll(new List<Racer> { r });
                    race.StartCountdown();
                    float t = 0f;
                    while (race.phase != RacePhase.Over && t < 500f) { race.Step(RaceDt); t += RaceDt; }
                    double plan = race.LineFor(i).lapTime / 0.96;
                    double lap = r.bestLap > 0 ? r.bestLap : r.lastLap;
                    double ratio = lap / plan;
                    bool good = r.finished && r.crashes == 0 && ratio > 0.9 && ratio < 1.12;
                    all &= good;
                    detail += " [" + Bikes.Get(i).name + " " + c.seed + ": " + lap.ToString("F1") + " s" + (good ? "" : " BAD ratio " + ratio.ToString("F2") + " falls " + r.crashes + " finished " + r.finished) + "]";
                }
            }
            Check("ROSTER_EVERY_BIKE_RIDES_A_CLEAN_LAP", all, "two laps alone, 2 circuits:" + detail, "a clean lap within 10 % of the plan, on every bike");
        }
    }
}
