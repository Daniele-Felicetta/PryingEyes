using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PryingEyes.Pista;

/// <summary>Bikes touching (Race.Contacts and BikeModel.Shove): bikes cannot share a place, a nudge and a bump are not a fall, a bike that rides hard into the back of another falls (and both do in a head-on or a very hard hit), momentum is kept, a bike
/// that is not simulated here is never moved, a rider who has just got up is left alone, computer riders alone ride exactly as before, and a person in a full race is touched a few times and finishes about as quickly.</summary>
static partial class Program
{
    sealed class ContactScene
    {
        public Race race; public Racer a, b; public Circuit circuit;
        public int touches; public float closing;
    }

    /// <summary>Two riders on the straight before the line: two people (both simulated here, with no hands on the controls) or a person and the pose of a friend.</summary>
    static ContactScene ContactPair(int bikeA, int bikeB, bool bIsPose = false)
    {
        var circuit = Circuits.Generate(Catalogue.All[0].seed);
        var race = new Race(circuit, 3, 5);
        var scene = new ContactScene { race = race, circuit = circuit };
        scene.a = race.AddHuman("a", bikeA, 0, 1);
        scene.b = bIsPose ? race.AddRemote("b", bikeB, 0) : race.AddHuman("b", bikeB, 0, 2);
        race.PlaceAll(new List<Racer> { scene.a, scene.b });
        race.StartCountdown();
        while (race.phase != RacePhase.Racing) race.Step(RaceDt);
        race.OnContact = (x, y, c) => { scene.touches++; scene.closing = Math.Max(scene.closing, c); };
        return scene;
    }

    /// <summary>Put a rider on the road: 'along' metres from the line (negative: before it), 'lateral' metres to the right of the centre, at 'speed' along the road (or against it).</summary>
    static void PlaceAt(ContactScene s, Racer r, float along, float lateral, float speed, bool backwards = false)
    {
        float cx, cz, cy, h;
        s.circuit.At(along, out cx, out cz, out cy, out h);
        float x = cx + (float)Math.Cos(h) * lateral, z = cz - (float)Math.Sin(h) * lateral;
        float head = backwards ? h + (float)Math.PI : h;
        if (r.bike != null)
        {
            var b = r.bike;
            b.Reset(x, z, head, cy);
            b.u = speed; b.omegaF = b.omegaR = speed / b.spec.wheelRadius;
            b.gear = speed > 24f ? 5 : speed > 16f ? 4 : speed > 9f ? 3 : 2;
        }
        else s.race.SetRemote(r, x, cy, z, head, speed, 0f, false);
    }

    /// <summary>The smallest distance between a disc of one bike and a disc of the other (the tires and the middle of each).</summary>
    static float DiscGap(Racer a, Racer b)
    {
        float best = 1e9f;
        float ax = a.bike != null ? a.bike.x : a.x, az = a.bike != null ? a.bike.z : a.z, ah = a.bike != null ? a.bike.heading : a.heading;
        float bx = b.bike != null ? b.bike.x : b.x, bz = b.bike != null ? b.bike.z : b.z, bh = b.bike != null ? b.bike.heading : b.heading;
        float wa = Bikes.Get(a.bikeIndex).wheelbase * 0.5f, wb = Bikes.Get(b.bikeIndex).wheelbase * 0.5f;
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                float dx = ax + (float)Math.Sin(ah) * wa * i - (bx + (float)Math.Sin(bh) * wb * j);
                float dz = az + (float)Math.Cos(ah) * wa * i - (bz + (float)Math.Cos(bh) * wb * j);
                best = Math.Min(best, (float)Math.Sqrt(dx * dx + dz * dz));
            }
        return best;
    }

    static float Momentum(Racer r) { return Bikes.Get(r.bikeIndex).Mass * r.bike.u; }

    static void ContactChecks()
    {
        float limit = 2f * Race.ContactRadius;

        // ---- bikes cannot share a place
        {
            var s = ContactPair(0, 0);
            PlaceAt(s, s.a, -60f, 0f, 10f); PlaceAt(s, s.b, -60f, 0.3f, 10f);
            float before = DiscGap(s.a, s.b);
            for (int i = 0; i < 30; i++) s.race.Step(RaceDt);
            float after = DiscGap(s.a, s.b);
            Check("CONTACT_BIKES_CANNOT_SHARE_A_PLACE", before < 0.35f && after >= limit - 0.02f && !s.a.bike.crashed && !s.b.bike.crashed,
                "two bikes side by side with their middles " + F(before) + " m apart (discs of " + F(Race.ContactRadius) + " m) are " + F(after) + " m apart half a second later and still up",
                "at least " + F(limit - 0.02f) + " m, nobody down");
        }

        // ---- a nudge is not a fall
        {
            var s = ContactPair(0, 0);
            PlaceAt(s, s.a, -60f, 0f, 15f); PlaceAt(s, s.b, -60f, 0.9f, 15f);
            s.b.bike.heading -= 0.12f;                    // 15 m/s on a heading 0.12 rad to the left: 1.8 m/s towards the other bike
            float ua = 0f, ub = 0f;
            for (int i = 0; i < 90 && s.touches == 0; i++) { ua = s.a.bike.u; ub = s.b.bike.u; s.race.Step(RaceDt); }
            float lost = Math.Max(ua - s.a.bike.u, ub - s.b.bike.u);
            for (int i = 0; i < 60; i++) s.race.Step(RaceDt);
            Check("CONTACT_A_NUDGE_IS_NOT_A_FALL", s.touches >= 1 && s.closing > 1.4f && s.closing < 2.2f && !s.a.bike.crashed && !s.b.bike.crashed && lost < 0.6f && s.a.bumps >= 1,
                "two bikes at 15 m/s coming together sideways at 1.8 m/s touch " + s.touches + " time(s) at " + F(s.closing) + " m/s, the touch costs the speed along the road " + F(lost) + " m/s and both are up a second and a half later",
                "a touch at about 1.8 m/s, nobody down, under 0.6 m/s lost");
        }

        // ---- a bump is not a fall: a bike catching another one at 7 m/s (25 km/h) touches it and both stay up
        {
            var s = ContactPair(0, 0);
            PlaceAt(s, s.a, -60f, 0f, 27f); PlaceAt(s, s.b, -57f, 0f, 20f);
            for (int i = 0; i < 120 && s.touches == 0; i++) s.race.Step(RaceDt);
            for (int i = 0; i < 90; i++) s.race.Step(RaceDt);
            Check("CONTACT_A_BUMP_IS_NOT_A_FALL", s.touches >= 1 && s.closing > 5.5f && s.closing < 8.5f && !s.a.bike.crashed && !s.b.bike.crashed && s.a.bumps >= 1,
                "a bike at 27 m/s catches one at 20 m/s: they meet at " + F(s.closing) + " m/s, touch " + s.touches + " time(s) and both are up a second and a half later (" + (!s.a.bike.crashed && !s.b.bike.crashed) + ")",
                "closing about 7 m/s, nobody down");
        }

        // ---- riding hard into the back of another bike
        {
            var s = ContactPair(0, 0);
            PlaceAt(s, s.a, -60f, 0f, 32f); PlaceAt(s, s.b, -57f, 0f, 20f);
            float pa = 0f, pb = 0f, ua = 0f, ub = 0f;
            for (int i = 0; i < 120 && s.touches == 0; i++)
            {
                pa = Momentum(s.a); pb = Momentum(s.b); ua = s.a.bike.u; ub = s.b.bike.u;
                s.race.Step(RaceDt);
            }
            float after = Momentum(s.a) + Momentum(s.b), was = pa + pb;
            Check("CONTACT_THE_BIKE_THAT_RIDES_HARD_INTO_ANOTHER_FALLS", s.touches == 1 && s.closing > 10.5f && s.closing < 14.5f && s.a.bike.crashed && !s.b.bike.crashed && s.b.bike.u > ub + 2f && s.a.bike.u < ua - 2f && Math.Abs(after - was) < 0.03f * was,
                "a bike at 32 m/s catches one at 20 m/s: they meet at " + F(s.closing) + " m/s, the one behind falls (" + s.a.bike.crashed + ") and the one ahead does not (" + !s.b.bike.crashed + "), it is pushed from " + F(ub, "F1") + " to " + F(s.b.bike.u, "F1")
                + " m/s and the other slowed from " + F(ua, "F1") + " to " + F(s.a.bike.u, "F1") + "; momentum " + F(was, "F0") + " -> " + F(after, "F0"),
                "closing about 12 m/s, only the striker falls, speeds exchanged, momentum kept within 3%");
        }

        // ---- a very hard hit from behind brings both down
        {
            var s = ContactPair(0, 0);
            PlaceAt(s, s.a, -60f, 0f, 42f); PlaceAt(s, s.b, -57f, 0f, 22f);
            for (int i = 0; i < 120 && s.touches == 0; i++) s.race.Step(RaceDt);
            Check("CONTACT_A_VERY_HARD_HIT_BRINGS_BOTH_DOWN", s.touches == 1 && s.closing > 17f && s.a.bike.crashed && s.b.bike.crashed,
                "a bike at 42 m/s catches one at 22 m/s: they meet at " + F(s.closing) + " m/s and both fall (" + s.a.bike.crashed + ", " + s.b.bike.crashed + ")",
                "closing about 20 m/s, both down");
        }

        // ---- a head-on hit
        {
            var s = ContactPair(0, 3);
            PlaceAt(s, s.a, -60f, 0f, 20f); PlaceAt(s, s.b, -52f, 0f, 20f, true);
            for (int i = 0; i < 90 && s.touches == 0; i++) s.race.Step(RaceDt);
            Check("CONTACT_A_HEAD_ON_HIT_BRINGS_BOTH_DOWN", s.touches == 1 && s.closing > 30f && s.a.bike.crashed && s.b.bike.crashed,
                "two bikes riding at each other at 20 m/s meet at " + F(s.closing) + " m/s and both fall (" + s.a.bike.crashed + ", " + s.b.bike.crashed + ")",
                "closing about 40 m/s, both down");
        }

        // ---- only the bikes simulated here are moved
        {
            var s = ContactPair(0, 0, true);
            PlaceAt(s, s.a, -60f, 0f, 12f); PlaceAt(s, s.b, -60f, 0.3f, 12f);
            float px = s.b.x, pz = s.b.z, ax = s.a.bike.x, az = s.a.bike.z, ah = s.a.bike.heading;
            s.race.Step(RaceDt);
            // the middle of a bike only moves when the deepest disc pair is the two middles; the push is along the line between the discs: take its length
            float moved = Math.Abs((s.a.bike.x - ax) * (float)Math.Cos(ah) - (s.a.bike.z - az) * (float)Math.Sin(ah));
            bool poseSame = s.b.x == px && s.b.z == pz;
            Check("CONTACT_ONLY_MOVES_THE_BIKES_SIMULATED_HERE", poseSame && moved > 0.15f && moved < 0.30f,
                "a bike riding alongside the pose of a friend 0.3 m away is pushed " + F(moved) + " m (its share, with equal masses, of the " + F(limit - 0.3f) + " m the discs overlap) and the friend's pose stays exactly where it was (" + poseSame + ")",
                "the pose untouched, our bike moved about 0.25 m");
        }

        // ---- a rider who has just got up is left alone
        {
            var s = ContactPair(0, 0);
            PlaceAt(s, s.a, -60f, 0f, 0f); PlaceAt(s, s.b, -60f, 3f, 0f);
            s.race.Respawn(s.a);
            s.b.bike.Reset(s.a.bike.x + (float)Math.Cos(s.a.bike.heading) * 0.2f, s.a.bike.z - (float)Math.Sin(s.a.bike.heading) * 0.2f, s.a.bike.heading, s.a.bike.y);
            float ax = s.a.bike.x, az = s.a.bike.z;
            for (int i = 0; i < 90; i++) s.race.Step(RaceDt);
            float during = DiscGap(s.a, s.b);
            bool still = Math.Abs(during - 0.2f) < 0.03f;                          // a bike on the grid idles forward a little, but nothing pushed it away from the other
            for (int i = 0; i < (int)(Race.RespawnGrace * 60f); i++) s.race.Step(RaceDt);
            float later = DiscGap(s.a, s.b);
            Check("CONTACT_A_RIDER_WHO_JUST_GOT_UP_IS_LEFT_ALONE", still && during < 0.4f && later >= limit - 0.02f,
                "a bike placed 0.2 m from a rider who has just been put back on the track is " + F(during) + " m from him a second and a half later (nothing moved him: " + still + ") and " + F(later) + " m after the " + F(Race.RespawnGrace, "F0") + " s are up",
                "inside the other while the grace lasts, clear of it when it is over");
        }

        // ---- computer riders alone ride exactly as before
        {
            var circuit = Circuits.Generate(Catalogue.All[2].seed);
            var times = new List<string>();
            foreach (bool on in new[] { true, false })
            {
                var race = new Race(circuit, 1, 77) { contacts = on };
                var riders = new List<Racer>();
                for (int i = 0; i < 12; i++) riders.Add(race.AddAi("r" + i, i % 3 == 0 ? 0 : i % 3 == 1 ? 3 : 4, i, 900 + i * 13, 0.90f + 0.008f * i, 0.1f));
                race.PlaceAll(riders);
                race.StartCountdown();
                float t = 0f;
                while (race.phase != RacePhase.Over && t < 400f) { race.Step(RaceDt); t += RaceDt; }
                times.Add(string.Join(",", riders.Select(r => r.finishTime.ToString("F4", CultureInfo.InvariantCulture) + "/" + r.crashes + "/" + r.bumps)));
            }
            Check("CONTACT_COMPUTER_RIDERS_ALONE_RIDE_AS_BEFORE", times[0] == times[1] && times[0].Split(',').All(x => !x.StartsWith("-1")),
                "twelve computer riders on a circuit, once with the touching on and once off, finish with the same times, falls and touches to the fourth decimal (" + (times[0] == times[1]) + ")",
                "identical");
        }

        // ---- a person in a full race
        {
            var line = new StringBuilder();
            int totalBumps = 0, worstFalls = 0; float worstRatio = 0f;
            foreach (int ci in new[] { 0, 2, 5 })
            {
                var results = new List<float>(); var falls = new List<int>(); int bumps = 0;
                foreach (bool on in new[] { true, false })
                {
                    var circuit = Circuits.Generate(Catalogue.All[ci].seed);
                    var race = new Race(circuit, 2, 31 + ci) { contacts = on };
                    var me = race.AddHuman("person", 0, 0, 7001);
                    var riders = new List<Racer>();
                    for (int i = 0; i < 11; i++) riders.Add(race.AddAi("r" + i, 0, i, 400 + ci * 50 + i * 7, 0.90f + 0.008f * i, 0.08f));
                    var order = new List<Racer>(riders);
                    order.Insert(5, me);
                    race.PlaceAll(order);
                    var driver = new BikeDriver(me.bike, circuit, race.LineFor(0), 7002) { pace = 0.97f, mistakes = 0f };
                    driver.Reset();
                    race.StartCountdown();
                    float t = 0f;
                    while (race.phase != RacePhase.Over && t < 500f)
                    {
                        if (race.phase == RacePhase.Racing) { race.Avoid(me, driver, RaceDt); me.input = driver.Drive(RaceDt); }
                        race.Step(RaceDt); t += RaceDt;
                    }
                    results.Add(me.finished ? me.finishTime : -1f); falls.Add(me.crashes);
                    if (on) bumps = me.bumps;
                }
                float ratio = results[1] > 0f && results[0] > 0f ? results[0] / results[1] : 9f;
                worstRatio = Math.Max(worstRatio, ratio); worstFalls = Math.Max(worstFalls, falls[0]); totalBumps += bumps;
                line.Append("circuit " + ci + ": " + bumps + " touches, " + falls[0] + " falls, x" + F(ratio, "F3") + " the time without touching; ");
            }
            Check("CONTACT_A_PERSON_IN_A_FULL_RACE_IS_TOUCHED_NOW_AND_THEN", worstRatio < 1.06f && worstFalls <= 1 && totalBumps >= 3 && totalBumps <= 40,
                "a person (stood for by the computer driver, who goes round slower bikes as the computer riders do) among eleven riders for two laps on three circuits: " + line + totalBumps + " touches in all",
                "a few touches a race, at most one fall, at most 6% slower than with no touching");
        }
    }
}
