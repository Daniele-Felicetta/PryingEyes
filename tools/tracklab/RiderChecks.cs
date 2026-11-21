using System;
using System.Collections.Generic;
using PryingEyes.Pista;

/// <summary>How the rider sits and moves on the bike (the three styles of cornering, the weight along the bike) and the slipstream: what each must change, and what it must leave alone.</summary>
static partial class Program
{
    static void RiderChecks()
    {
        var flat = new FlatGround();
        var sb = Bikes.Superbike();
        RiderStyle[] styles = { RiderStyle.LeanBody, RiderStyle.Neutral, RiderStyle.LeanBike };
        string[] names = { "lean body", "neutral", "lean bike" };

        // ---- turn-in: how long the bike takes to fall to 25 degrees of frame lean at 108 km/h
        var turnIn = new float[3];
        for (int i = 0; i < 3; i++)
        {
            float t25 = -1f; int k = i;
            Ride(sb, flat, 4f, (t, b) => new BikeInputs { steer = 0.8f, throttle = ThrottleToHold(b, 30f) },
                (t, b) => { if (t25 < 0f && Math.Abs(b.lean) > 25f * PM.Deg2Rad) t25 = t; },
                b => { b.style = styles[k]; b.u = 30f; b.omegaF = b.omegaR = 30f / sb.wheelRadius; b.gear = 4; });
            turnIn[i] = t25;
        }
        Check("STYLE_LEAN_BODY_TURNS_IN_FASTER", turnIn[0] > 0f && turnIn[0] < turnIn[1] - 0.01f && turnIn[1] < turnIn[2] - 0.01f,
            "25 degrees of lean after " + turnIn[0].ToString("F3") + " s (" + names[0] + "), " + turnIn[1].ToString("F3") + " s (" + names[1] + "), " + turnIn[2].ToString("F3") + " s (" + names[2] + ")",
            "lean body quickest, lean bike slowest");

        // ---- the body goes in, the frame stays up: in the same steady corner the frame leans least with the body hung off and most with the body in line
        var frame = new float[3]; var crashedAny = false;
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            var bike = Ride(sb, flat, 6f, (t, b) => new BikeInputs { useCurvature = true, curvature = 1f / 70f, throttle = ThrottleToHold(b, 25f) }, null,
                b => { b.style = styles[k]; b.u = 25f; b.omegaF = b.omegaR = 25f / sb.wheelRadius; b.gear = 3; });
            frame[i] = Math.Abs(bike.lean) * PM.Rad2Deg; crashedAny |= bike.crashed;
        }
        Check("STYLE_BODY_POSITION_CHANGES_THE_FRAME_LEAN", !crashedAny && frame[0] + 2f < frame[1] && frame[1] + 2f < frame[2],
            "frame lean in a 70 m corner at 90 km/h: " + frame[0].ToString("F1") + " (" + names[0] + "), " + frame[1].ToString("F1") + " (" + names[1] + "), " + frame[2].ToString("F1") + " degrees (" + names[2] + ")",
            "each style at least 2 degrees more upright than the next");

        // ---- at the limit: a little more grip with the body hung off, less with the body in line (the lean assist keeps all of them up)
        var lat = new float[3]; var fellStyle = false;
        for (int i = 0; i < 3; i++)
        {
            int k = i; float m = 0f;
            Ride(sb, flat, 8f, (t, b) => new BikeInputs { steer = 1f, throttle = ThrottleToHold(b, 30f) }, (t, b) => { if (t > 3f) m = Math.Max(m, Math.Abs(b.latG)); fellStyle |= b.crashed; },
                b => { b.style = styles[k]; b.u = 30f; b.omegaF = b.omegaR = 30f / sb.wheelRadius; b.gear = 4; });
            lat[i] = m;
        }
        Check("STYLE_GRIP_AT_DEEP_LEAN", !fellStyle && lat[0] > lat[1] + 0.004f && lat[2] < lat[1] - 0.02f && lat[2] > 0.9f,
            "lateral g at full lock, 108 km/h: " + lat[0].ToString("F3") + " (" + names[0] + "), " + lat[1].ToString("F3") + " (" + names[1] + "), " + lat[2].ToString("F3") + " (" + names[2] + ")",
            "lean body above neutral, lean bike clearly below it, nobody falls");

        // ---- the weight along the bike: forward loads the front wheel in a launch, back loads the rear wheel under braking (the load, not the wheelie or the stoppie itself, because those
        // always grow into a loop or an endo once they start and then no longer tell the three positions apart)
        float[] shifts = { 1f, 0f, -1f };
        var frontLoad = new float[3];
        for (int i = 0; i < 3; i++)
        {
            int k = i; float least = 1e9f;
            Ride(sb, flat, 2.2f, (t, b) => new BikeInputs { throttle = 1f, riderFwd = shifts[k] }, (t, b) => { if (t > 0.5f) least = Math.Min(least, b.loadFront); },
                b => { b.antiWheelie = false; b.tcs = false; });
            frontLoad[i] = least;
        }
        Check("BODY_FORWARD_LOADS_THE_FRONT", frontLoad[0] > frontLoad[1] + 25f && frontLoad[1] > frontLoad[2] + 100f,
            "least load on the front wheel in a full launch: " + frontLoad[0].ToString("F0") + " N (forward), " + frontLoad[1].ToString("F0") + " N (middle), " + frontLoad[2].ToString("F0") + " N (back)",
            "more weight forward, more on the front");
        var rearLoad = new float[3];
        for (int i = 0; i < 3; i++)
        {
            int k = i; float least = 1e9f;
            var bike = new BikeModel(sb, 3) { antiStoppie = false };
            bike.Reset(0f, 0f, 0f, 0f);
            bike.u = 150f / 3.6f; bike.omegaF = bike.omegaR = bike.u / sb.wheelRadius; bike.gear = 4;
            for (float t = 0f; t < 2.5f; t += Dt)
            {
                bike.Step(Dt, flat, new BikeInputs { brakeFront = 0.3f, riderFwd = shifts[k] });
                if (t > 0.3f) least = Math.Min(least, bike.loadRear);
            }
            rearLoad[i] = least;
        }
        Check("BODY_BACK_LOADS_THE_REAR", rearLoad[2] > rearLoad[1] + 60f && rearLoad[1] > rearLoad[0] + 60f,
            "least load on the rear wheel braking from 150 km/h: " + rearLoad[0].ToString("F0") + " N (forward), " + rearLoad[1].ToString("F0") + " N (middle), " + rearLoad[2].ToString("F0") + " N (back)",
            "more weight back, more on the rear");

        // ---- the shift is the rider's own mass moving: the centre of gravity moves by his share, nothing more
        {
            var bike = new BikeModel(sb, 5); bike.Reset(0f, 0f, 0f, 0f);
            for (float t = 0f; t < 2f; t += Dt) bike.Step(Dt, flat, new BikeInputs { riderFwd = 1f });
            float share = sb.riderMass / sb.Mass * BikeModel.BodyTravel;
            Check("BODY_SHIFT_IS_THE_RIDERS_SHARE", Math.Abs(bike.body - 1f) < 0.02f && share > 0.04f && share < 0.09f && bike.u < 0.01f,
                "body settles at " + bike.body.ToString("F3") + ", the centre of gravity moves " + (share * 100f).ToString("F1") + " cm of a " + (sb.wheelbase * 100f).ToString("F0") + " cm wheelbase",
                "settles at the end of its travel, 4-9 cm for a 75 kg rider");
        }

        // ---- slipstream: the same bike goes faster in the hole in the air
        {
            float v0 = 0f, vHalf = 0f, v1 = 0f;
            Ride(sb, flat, 50f, (t, b) => Full(), (t, b) => { v0 = Math.Max(v0, b.u * 3.6f); });
            Ride(sb, flat, 50f, (t, b) => Full(), (t, b) => { vHalf = Math.Max(vHalf, b.u * 3.6f); }, b => b.draft = 0.5f);
            Ride(sb, flat, 50f, (t, b) => Full(), (t, b) => { v1 = Math.Max(v1, b.u * 3.6f); }, b => b.draft = 1f);
            Check("BIKE_SLIPSTREAM_CUTS_THE_DRAG", v1 > v0 * 1.04f && vHalf > v0 + 3f && vHalf < v1,
                "top speed in 50 s: " + v0.ToString("F0") + " km/h in clean air, " + vHalf.ToString("F0") + " half sheltered, " + v1.ToString("F0") + " fully sheltered",
                "fully sheltered at least 4 % faster, half way in between");
        }

        // ---- the race works out the slipstream from the poses: only from right behind, in line, going the same way, and fading with the gap
        {
            Func<float, float, float, bool, float, float[]> draftOf = (gap, side, speedA, crashedAhead, headingAhead) =>
            {
                var race = new Race(LabCircuits()[0], 3, 1);
                var a = race.AddRemote("follower", 0, 0); var b = race.AddRemote("leader", 0, 1);
                race.PlaceAll(new List<Racer> { a, b });
                race.phase = RacePhase.Racing;
                for (int i = 0; i < 120; i++)
                {
                    race.SetRemote(a, 0f, 0f, 0f, 0f, speedA, 0f, false);
                    race.SetRemote(b, side, 0f, gap, headingAhead, 70f, 0f, crashedAhead);
                    race.Step(RaceDt);
                }
                return new[] { a.draft, b.draft };
            };
            var near = draftOf(5f, 0f, 70f, false, 0f); var mid = draftOf(18f, 0f, 70f, false, 0f); var far = draftOf(60f, 0f, 70f, false, 0f);
            var offset = draftOf(8f, 3.2f, 70f, false, 0f); var down = draftOf(8f, 0f, 70f, true, 0f); var against = draftOf(8f, 0f, 70f, false, (float)Math.PI); var slow = draftOf(8f, 0f, 10f, false, 0f);
            Check("RACE_SLIPSTREAM_FROM_BEHIND_ONLY", near[0] > 0.5f && near[1] == 0f && offset[0] == 0f && far[0] == 0f && down[0] == 0f && against[0] == 0f && slow[0] == 0f,
                "5 m behind " + near[0].ToString("F2") + ", the leader " + near[1].ToString("F2") + "; 3 m to the side " + offset[0].ToString("F2") + ", 60 m behind " + far[0].ToString("F2") + ", a fallen rider ahead " + down[0].ToString("F2") + ", head on " + against[0].ToString("F2") + ", too slow " + slow[0].ToString("F2"),
                "sheltered only right behind, in line, and moving");
            Check("RACE_SLIPSTREAM_FADES_WITH_THE_GAP", near[0] > mid[0] + 0.1f && mid[0] > far[0] + 0.1f && near[0] <= 1f,
                "5 m " + near[0].ToString("F2") + ", 18 m " + mid[0].ToString("F2") + ", 60 m " + far[0].ToString("F2"), "less shelter the further back");
        }

        // ---- in a real race packs form: somebody is sheltered a good part of the time, and nobody always
        {
            var c = LabCircuits()[1];
            var race = new Race(c, 2, 11);
            var order = new List<Racer>();
            for (int i = 0; i < 10; i++) order.Add(race.AddAi("AI" + i, i % 3, i, 200 + i * 13, 0.92f + 0.004f * i, 0.04f));
            race.PlaceAll(order);
            race.StartCountdown();
            double sheltered = 0, total = 0; float top = 0f;
            float tt = 0f;
            while (race.phase != RacePhase.Over && tt < 140f)
            {
                race.Step(RaceDt); tt += RaceDt;
                if (race.phase != RacePhase.Racing) continue;
                foreach (var r in race.racers) { total += 1; if (r.draft > 0.2f) sheltered += 1; top = Math.Max(top, r.draft); }
            }
            double frac = sheltered / Math.Max(1, total);
            Check("RACE_SLIPSTREAM_PACKS_FORM", frac > 0.03 && frac < 0.7 && top > 0.5f,
                "10 riders for " + tt.ToString("F0") + " s: sheltered " + (frac * 100).ToString("F1") + " % of the rider-time, best shelter " + top.ToString("F2"), "between 3 % and 70 % of the time, at least once nearly full");
        }
    }
}
