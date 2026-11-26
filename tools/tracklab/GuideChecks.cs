using System;
using System.Collections.Generic;
using System.Text;
using PryingEyes.Pista;
using Production.Racing;

/// <summary>The driving line (DrivingLine.cs): green, yellow and red along the racing line the computer riders follow. A lap has braking zones before its slow corners, they start where the plan starts to slow (a little
/// earlier) and end where it stops, no stretch of advice is too short to read, a faster bike brakes longer, the computer rider really brakes where the line says, and what the aids level of the player lets him see follows
/// the menu.</summary>
static partial class Program
{
    static List<int> NamedSeeds()
    {
        var seeds = new List<int>();
        for (int i = 0; i < Catalogue.Count; i++) seeds.Add(Catalogue.All[i].seed);
        return seeds;
    }

    /// <summary>The samples where the plan itself slows the bike by more than the braking threshold (the raw plan, before the zones are cleaned up and given a lead).</summary>
    static bool[] PlanBrakes(RacingLine line)
    {
        int n = line.c.n;
        var b = new bool[n];
        float ds = Math.Max(0.5f, line.c.spacing);
        for (int i = 0; i < n; i++)
        {
            int j = i + 1 == n ? 0 : i + 1;
            float accel = (line.vplan[j] * line.vplan[j] - line.vplan[i] * line.vplan[i]) / (2f * ds);
            b[i] = accel < -DrivingLine.BrakeDecel;
        }
        return b;
    }

    /// <summary>Three laps alone with the computer rider on a circuit: for the second lap, how often the bike really brakes (it slows by more than a quarter of a g, the way the game lights the brake lamp of a rider)
    /// where the line says brake, and how often a braking bike is in or just beside a red zone.</summary>
    static void BrakesWhereTheLineSays(Circuit c, int bike, int seed, out double covered, out double placed, out int coreSteps, out int brakeSteps)
    {
        var spec = Bikes.Get(bike);
        var race = new Race(c, 3, seed);
        var r = race.AddAi("solo", bike, 0, seed, 0.96f, 0f);
        race.PlaceAll(new List<Racer> { r });
        var advice = DrivingLine.Classify(race.LineFor(spec));
        var raw = PlanBrakes(race.LineFor(spec));
        race.StartCountdown();
        int n = c.n, near = (int)Math.Round(20f / c.spacing);
        int core = 0, coreBraking = 0, braking = 0, brakingPlaced = 0;
        float t = 0f;
        while (race.phase != RacePhase.Over && t < 700f)
        {
            race.Step(RaceDt); t += RaceDt;
            if (race.phase != RacePhase.Racing || r.crashed) continue;
            float laps = r.progress / c.length;
            if (laps < 1f || laps >= 2f) continue;
            int i = (int)(r.s / c.spacing) % n;
            bool brakes = r.bike.longG < -0.25f;
            if (raw[i] && advice[i] == Advice.Brake) { core++; if (brakes) coreBraking++; }
            if (brakes)
            {
                braking++;
                bool inOrNear = false;
                for (int k = -near; k <= near && !inOrNear; k++) if (advice[((i + k) % n + n) % n] == Advice.Brake) inOrNear = true;
                if (inOrNear) brakingPlaced++;
            }
        }
        coreSteps = core; brakeSteps = braking;
        covered = core == 0 ? 0.0 : coreBraking / (double)core;
        placed = braking == 0 ? 1.0 : brakingPlaced / (double)braking;
    }

    /// <summary>The advice for the road ahead of a bike on sample i0 with this speed, as the player sees it (DrivingLine.Window): 'count' samples, from the one it is on.</summary>
    static Advice[] AdviceAhead(RacingLine line, Advice[] plan, int i0, float v, int count)
    {
        var a = new Advice[count];
        DrivingLine.Window(line, plan, i0, v, 0, a);
        return a;
    }

    static int FirstRed(Advice[] a) { for (int k = 0; k < a.Length; k++) if (a[k] == Advice.Brake) return k; return -1; }

    /// <summary>The braking sign for a bike on sample i0 with this speed and an aids level: the stretch the player sees (Window, with 'behind' samples behind the bike), its first red (FirstBraking) and the sign made from it.</summary>
    static BrakeCue CueAt(RacingLine line, Advice[] plan, int i0, float v, int aids, Advice[] win, int behind)
    {
        DrivingLine.Window(line, plan, i0, v, behind, win);
        return BrakeCues.Make(aids, DrivingLine.FirstBraking(line, win, i0, behind), v);
    }

    /// <summary>The braking zones of a lap that the live checks use: the plan brakes hard for at least MinZoneMetres, on road free of other braking for 60 m before, and ends up 12 % or more under the speed it had at the start.
    /// Each is the sample it starts on, the plan speed there and the lowest plan speed of the dip.</summary>
    static List<(int z0, float v, float low)> DeepZones(RacingLine line)
    {
        var res = new List<(int, float, float)>();
        var raw = PlanBrakes(line);
        int n = line.c.n, count = (int)Math.Round(DrivingLine.AheadMetres / line.c.spacing), minRun = Math.Max(2, (int)Math.Round(DrivingLine.MinZoneMetres / line.c.spacing));
        for (int z0 = 0; z0 < n; z0++)
        {
            if (!raw[z0] || raw[(z0 + n - 1) % n]) continue;
            int len = 0; while (len < n && raw[(z0 + len) % n]) len++;
            if (len < minRun) continue;
            bool free = true; for (int k = 1; k <= 30 && free; k++) if (raw[((z0 - k) % n + n) % n]) free = false;
            if (!free) continue;
            float v = line.vplan[z0], low = v;
            for (int k = 0; k < 2 * count; k++) { float vv = line.vplan[(z0 + k) % n]; if (vv < low) low = vv; else if (k > 2 && vv > low * 1.05f) break; }          // the bottom of the first dip: down to the lowest speed, until the plan has climbed 5 % above it
            if (1.0 - low / v < 0.12) continue;
            res.Add((z0, v, low));
        }
        return res;
    }

    /// <summary>One step of a lap ridden by the computer rider: where it was, how fast, whether it was braking (slowing by more than a quarter of a g) or on the throttle, whether the live advice showed any red in the
    /// first 20, 40 and 70 m of the road ahead, and what the braking sign said.</summary>
    struct RideStep { public int i; public float v; public bool braking, driving; public int red20, red40, red70; public CueKind sign; public float signM; }

    static readonly Dictionary<string, List<RideStep>> rideCache = new Dictionary<string, List<RideStep>>();

    /// <summary>The second of three laps alone on a circuit with the computer rider, step by step.</summary>
    static List<RideStep> RideTheLine(Circuit c, int bike, int seed)
    {
        string key = c.seed + "/" + bike + "/" + seed;
        if (rideCache.TryGetValue(key, out var cached)) return cached;
        var spec = Bikes.Get(bike);
        var race = new Race(c, 3, seed);
        var r = race.AddAi("solo", bike, 0, seed, 0.96f, 0f);
        race.PlaceAll(new List<Racer> { r });
        var line = race.LineFor(spec);
        var plan = DrivingLine.Classify(line);
        race.StartCountdown();
        int n = c.n, n20 = (int)Math.Round(20f / c.spacing), n40 = (int)Math.Round(40f / c.spacing), n70 = (int)Math.Round(70f / c.spacing);
        var win = new Advice[(int)Math.Round(DrivingLine.AheadMetres / c.spacing)];
        var steps = new List<RideStep>();
        float t = 0f;
        while (race.phase != RacePhase.Over && t < 700f)
        {
            race.Step(RaceDt); t += RaceDt;
            if (race.phase != RacePhase.Racing || r.crashed) continue;
            float laps = r.progress / c.length;
            if (laps < 1f || laps >= 2f) continue;
            int i0 = (int)(r.s / c.spacing) % n;
            float v = Math.Max(0f, r.bike.u);
            DrivingLine.Window(line, plan, i0, v, 0, win);
            int first = FirstRed(win);
            var sign = BrakeCues.Make(0, DrivingLine.FirstBraking(line, win, i0, 0), v);
            steps.Add(new RideStep { i = i0, v = v, braking = r.bike.longG < -0.25f, driving = r.bike.longG > 0.15f, red20 = first >= 0 && first < n20 ? 1 : 0, red40 = first >= 0 && first < n40 ? 1 : 0, red70 = first >= 0 && first < n70 ? 1 : 0, sign = sign.kind, signM = sign.metres });
        }
        rideCache[key] = steps;
        return steps;
    }

    static void GuideChecks()
    {
        int[] bikes = { 0, 3, 6 };           // a big one (Argo 1000), the small one of the career (Scintilla 125) and the biggest (Gigante 1300)
        var seeds = NamedSeeds();

        // ---- every named circuit has braking zones, they are a sensible part of the lap, and no piece of advice is too short
        {
            bool zones = true, share = true, shortest = true, closed = true, hasAll = true;
            double minShare = 9, maxShare = 0; float minZone = 9999f; int fewest = 99, most = 0;
            string bad = "";
            foreach (int seed in seeds)
            {
                var c = Circuits.Generate(seed);
                foreach (int b in bikes)
                {
                    var a = DrivingLine.Classify(new RacingLine(c, Bikes.Get(b)));
                    int z = DrivingLine.Zones(a, Advice.Brake);
                    float s = DrivingLine.Share(a, Advice.Brake);
                    bool good = z >= 3 && z <= 15 && s >= 0.08f && s <= 0.32f;
                    zones &= z >= 3 && z <= 15; if (z < fewest) fewest = z; if (z > most) most = z;
                    share &= s >= 0.08f && s <= 0.32f; if (s < minShare) minShare = s; if (s > maxShare) maxShare = s;
                    foreach (Advice k in new[] { Advice.Go, Advice.Lift, Advice.Brake })
                    {
                        float sh = DrivingLine.ShortestZone(a, k, c.spacing);
                        if (sh > 0f && sh < minZone) minZone = sh;
                        shortest &= sh == 0f || sh >= DrivingLine.MinZoneMetres - 0.01f;
                    }
                    hasAll &= DrivingLine.Share(a, Advice.Go) > 0.2f && DrivingLine.Share(a, Advice.Lift) > 0.02f;
                    closed &= a.Length == c.n;
                    if (!good && bad.Length < 160) bad += " [" + c.seed + " " + Bikes.Get(b).name + ": " + z + " zones, " + (s * 100).ToString("F1") + " %]";
                }
            }
            Check("GUIDE_EVERY_NAMED_CIRCUIT_HAS_BRAKING_ZONES_A_SENSIBLE_PART_OF_THE_LAP_AND_NO_PIECE_IS_TOO_SHORT", zones && share && shortest && closed && hasAll,
                seeds.Count + " circuits, " + bikes.Length + " bikes: " + fewest + " to " + most + " braking zones a lap, " + (minShare * 100).ToString("F1") + " to " + (maxShare * 100).ToString("F1") + " % of the lap in red, the shortest piece of advice " + minZone.ToString("F0") + " m; green over a fifth of every lap, yellow too" + bad,
                "3 to 15 braking zones, 8 to 32 % of the lap in red, no piece of advice shorter than " + DrivingLine.MinZoneMetres.ToString("F0") + " m, every colour present");
        }

        // ---- a red zone starts a little before the plan starts to slow and ends where it stops
        {
            bool startsOk = true, endsOk = true; int zonesSeen = 0; int worstLead = 0, worstTail = 0;
            int leadMax = (int)Math.Round(DrivingLine.LeadMetres / 2f) + (int)Math.Round(DrivingLine.MinZoneMetres / 2f) + 1;          // the lead, and a short piece folded into the zone
            foreach (int seed in seeds)
            {
                var c = Circuits.Generate(seed);
                var line = new RacingLine(c, Bikes.Get(3));
                var a = DrivingLine.Classify(line);
                var raw = PlanBrakes(line);
                int n = c.n;
                for (int i = 0; i < n; i++)
                {
                    if (a[i] != Advice.Brake || a[(i + n - 1) % n] == Advice.Brake) continue;
                    zonesSeen++;
                    int len = 0; while (len < n && a[(i + len) % n] == Advice.Brake) len++;
                    int first = -1, last = -1;
                    for (int k = 0; k < len; k++) if (raw[(i + k) % n]) { if (first < 0) first = k; last = k; }
                    if (first < 0) { startsOk = false; continue; }                      // a red zone with no braking of the plan in it
                    int lead = first, tail = len - 1 - last;
                    if (lead > worstLead) worstLead = lead; if (tail > worstTail) worstTail = tail;
                    startsOk &= lead <= leadMax;
                    endsOk &= tail <= 3;
                }
            }
            Check("GUIDE_A_RED_ZONE_STARTS_JUST_BEFORE_THE_PLAN_STARTS_TO_SLOW_AND_ENDS_WHERE_IT_STOPS", startsOk && endsOk && zonesSeen > 0,
                zonesSeen + " zones on " + seeds.Count + " circuits (Scintilla 125): the plan starts to brake at most " + worstLead + " samples (" + (worstLead * 2) + " m) after the red starts (the lead is " + DrivingLine.LeadMetres.ToString("F0") + " m) and stops at most " + worstTail + " samples before it ends",
                "every red zone has braking of the plan inside it, starting within the lead and ending within three samples of the red");
        }

        // ---- the colours follow the speed: yellow is where the bike is slowest (the corners), green and red are where it is fast (the road before the corner and the braking for it)
        {
            int good = 0, total = 0; double worst = 0;
            foreach (int seed in seeds)
            {
                var c = Circuits.Generate(seed);
                foreach (int bk in bikes)
                {
                    var line = new RacingLine(c, Bikes.Get(bk));
                    var a = DrivingLine.Classify(line);
                    var sum = new double[3]; var cnt = new double[3];
                    for (int i = 0; i < c.n; i++) { sum[(int)a[i]] += line.vplan[i]; cnt[(int)a[i]]++; }
                    if (cnt[0] == 0 || cnt[1] == 0 || cnt[2] == 0) { total++; continue; }
                    double go = sum[0] / cnt[0], lift = sum[1] / cnt[1], brake = sum[2] / cnt[2];
                    double ratio = lift / Math.Min(go, brake);
                    if (ratio > worst) worst = ratio;
                    total++; if (ratio < 0.9) good++;
                }
            }
            Check("GUIDE_YELLOW_IS_WHERE_THE_BIKE_IS_SLOWEST_AND_GREEN_AND_RED_WHERE_IT_IS_FAST", good == total,
                good + " of " + total + " laps (" + seeds.Count + " circuits, " + bikes.Length + " bikes): the mean planned speed in the yellow is under nine tenths of that in the green and in the red (the worst is " + (worst * 100).ToString("F0") + " %)",
                "yellow is the corner");
        }

        // ---- a faster bike brakes longer: more red on the same circuits
        {
            int moreOn = 0, total = 0; double redFast = 0, redSlow = 0;
            foreach (int seed in seeds)
            {
                var c = Circuits.Generate(seed);
                double f = DrivingLine.Share(DrivingLine.Classify(new RacingLine(c, Bikes.Get(6))), Advice.Brake) * c.length;
                double s = DrivingLine.Share(DrivingLine.Classify(new RacingLine(c, Bikes.Get(3))), Advice.Brake) * c.length;
                redFast += f; redSlow += s; total++; if (f > s) moreOn++;
            }
            Check("GUIDE_A_FASTER_BIKE_BRAKES_LONGER_THAN_A_SLOWER_ONE_ON_THE_SAME_CIRCUIT", moreOn >= total - 2 && redFast > 1.1 * redSlow,
                "the " + Bikes.Get(6).name + " has more red than the " + Bikes.Get(3).name + " on " + moreOn + " of " + total + " circuits (" + (redFast / total).ToString("F0") + " m a lap against " + (redSlow / total).ToString("F0") + " m)",
                "more red for the bike that arrives at the corner faster");
        }

        // ---- the computer rider brakes where the line says (the line is its own plan, so this is the line against what is really ridden)
        {
            double worstCovered = 9, worstPlaced = 9; bool good = true; int steps = 0; string bad = "";
            foreach (int seed in new[] { 4242, 22, 53 })
            {
                var c = Circuits.Generate(seed);
                foreach (int b in new[] { 0, 3 })
                {
                    double cov, pla; int core, brk;
                    BrakesWhereTheLineSays(c, b, 11, out cov, out pla, out core, out brk);
                    steps += core;
                    if (cov < worstCovered) worstCovered = cov; if (pla < worstPlaced) worstPlaced = pla;
                    if (cov < 0.85 || pla < 0.85 || core < 100) { good = false; if (bad.Length < 200) bad += " [" + seed + " " + Bikes.Get(b).name + ": " + (cov * 100).ToString("F0") + " % / " + (pla * 100).ToString("F0") + " %, " + core + " steps]"; }
                }
            }
            Check("GUIDE_THE_COMPUTER_RIDER_BRAKES_WHERE_THE_LINE_SAYS_RED", good,
                "second lap alone on 3 circuits with 2 bikes: where the plan brakes and the line is red the bike really slows by more than a quarter of a g at least " + (worstCovered * 100).ToString("F0") + " % of the time, and at least " + (worstPlaced * 100).ToString("F0") + " % of the braking is in or within 20 m of a red zone (" + steps + " steps of braking)" + bad,
                "the line and the ride agree");
        }

        // ---- the same line gives the same advice, and the aids level of the rider decides what he sees
        {
            var c = Circuits.Generate(4242);
            var a1 = DrivingLine.Classify(new RacingLine(c, Bikes.Get(4)));
            var a2 = DrivingLine.Classify(new RacingLine(c, Bikes.Get(4)));
            bool same = a1.Length == a2.Length; for (int i = 0; same && i < a1.Length; i++) same &= a1[i] == a2[i];
            bool full = true, marks = true, none = true;
            foreach (Advice k in new[] { Advice.Go, Advice.Lift, Advice.Brake })
            {
                full &= DrivingLine.Shows(0, k);
                marks &= DrivingLine.Shows(1, k) == (k == Advice.Brake);
                none &= !DrivingLine.Shows(2, k);
            }
            Check("GUIDE_THE_SAME_LINE_GIVES_THE_SAME_ADVICE_AND_THE_AIDS_LEVEL_DECIDES_WHAT_IS_SHOWN", same && full && marks && none,
                "two lines of the same bike on the same circuit give the same " + a1.Length + " pieces of advice (" + (same ? "yes" : "NO") + "); Completi shows all three colours (" + (full ? "yes" : "NO") + "), Sportivi only the red (" + (marks ? "yes" : "NO") + "), Nessuno nothing (" + (none ? "yes" : "NO") + ")",
                "deterministic advice; aids 0 all, 1 the brake zones, 2 none");
        }

        // ---- the live advice, as the player sees it: a bike that is at the start of a deep braking zone at the speed the plan has there is already told to brake (the red is at its nose) and the red lasts to the bottom of the zone,
        // a bike that stands still sees only green, and the faster the bike the earlier the red
        {
            // a braking zone: the plan brakes hard for at least MinZoneMetres (a shorter run is a blip where the curve of the plan changes shape) on a road free of other braking for 60 m before; the dip is the lowest speed of the
            // plan from the start of the zone till it is back at the speed it had, and it is deep when it ends 12 % or more under it (the rule asks for 10 %)
            int pre = 20;          // for the comparison of speeds the bike is 40 m before the zone
            int zonesSeen = 0, deep = 0, atNose = 0, noRed = 0, contiguousOk = 0, ordered = 0, slowerLater = 0, fasterEarlier = 0, blips = 0, crowded = 0, notDeep = 0; bool stillGreen = true;
            double shiftSlow = 0, shiftFast = 0; int worstNose = 0;
            foreach (int bk in new[] { 3, 0, 6 })
            foreach (int seed in seeds)
            {
                var c = Circuits.Generate(seed);
                var line = new RacingLine(c, Bikes.Get(bk));
                var plan = DrivingLine.Classify(line);
                var raw = PlanBrakes(line);
                int n = c.n, count = (int)Math.Round(DrivingLine.AheadMetres / c.spacing), minRun = Math.Max(2, (int)Math.Round(DrivingLine.MinZoneMetres / c.spacing));
                for (int z0 = 0; z0 < n; z0++)
                {
                    if (!raw[z0] || raw[(z0 + n - 1) % n]) continue;
                    int len = 0; while (len < n && raw[(z0 + len) % n]) len++;
                    if (len < minRun) { blips++; continue; }
                    bool free = true; for (int k = 1; k <= pre + 10 && free; k++) if (raw[((z0 - k) % n + n) % n]) free = false;
                    if (!free) { crowded++; continue; }
                    zonesSeen++;
                    float v = line.vplan[z0];
                    int minK = 0; float minV = v;
                    for (int k = 0; k < count; k++) { float vv = line.vplan[(z0 + k) % n]; if (k > 2 && vv >= v) break; if (vv < minV) { minV = vv; minK = k; } }
                    if (1.0 - minV / v < 0.12) { notDeep++; continue; }
                    deep++;
                    // at the start of the zone: the red is at the nose and goes on to the bottom
                    var a0 = AdviceAhead(line, plan, ((z0 - 1) % n + n) % n, v, count);
                    int k0 = FirstRed(a0);
                    if (k0 < 0) noRed++; else if (k0 <= 1) atNose++; else if (k0 > worstNose) worstNose = k0;
                    int red = 0, total = 0;
                    for (int k = Math.Max(0, k0); k0 >= 0 && k <= 1 + minK && k < count; k++) { total++; if (a0[k] == Advice.Brake) red++; }
                    if (k0 >= 0 && red >= 0.95 * total) contiguousOk++;
                    // from 40 m before it, at that speed, a bike 15 % under the plan speed and one 10 % over it: the faster the bike the earlier the red
                    int i0 = ((z0 - pre) % n + n) % n;
                    int kr = FirstRed(AdviceAhead(line, plan, i0, v, count));
                    int ks = FirstRed(AdviceAhead(line, plan, i0, v * 0.85f, count)), kf = FirstRed(AdviceAhead(line, plan, i0, v * 1.10f, count));
                    bool later = ks < 0 || ks >= kr, earlier = kf >= 0 && kf <= kr;
                    if (kr >= 0 && later && earlier) ordered++;
                    if (kr >= 0 && (ks < 0 || ks > kr)) slowerLater++;
                    if (kr >= 0 && kf >= 0 && kf < kr) fasterEarlier++;
                    shiftSlow += ((ks < 0 ? count : ks) - kr) * c.spacing; shiftFast += ((kf < 0 ? count : kf) - kr) * c.spacing;
                }
                if (bk == 3) for (int i0 = 0; i0 < n; i0 += 37) { var still = AdviceAhead(line, plan, i0, 0f, count); for (int k = 0; k < count; k++) stillGreen &= still[k] == Advice.Go; }
            }
            Check("GUIDE_A_BIKE_AT_THE_BRAKING_POINT_IS_TOLD_TO_BRAKE_TO_THE_BOTTOM_OF_THE_ZONE_AND_ONE_THAT_STANDS_STILL_SEES_ONLY_GREEN", deep > 100 && noRed == 0 && atNose >= 0.9 * deep && worstNose <= 20 && contiguousOk == deep && stillGreen,
                zonesSeen + " braking zones of " + DrivingLine.MinZoneMetres.ToString("F0") + " m or more on free road (" + seeds.Count + " circuits, three bikes; " + blips + " shorter blips of the plan and " + crowded + " zones too close to another left out), the bike at the start of each at the speed the plan has there: of the " + deep + " zones where the plan ends up 12 % or more under that speed (" + notDeep + " are shallower) the red is at its nose in " + atNose + (worstNose > 0 ? " and at most " + worstNose * 2 + " m ahead in the others (a zone that starts gently: the plan has to fall 3 % under the speed first)" : "") + (noRed > 0 ? ", NO red in " + noRed : "") + ", and it is red from there to the bottom of the zone in " + contiguousOk + "; a bike that stands still sees only green (" + (stillGreen ? "yes" : "NO") + ")",
                "red at the nose in nine zones of ten and never more than 40 m ahead, to the corner; green for a bike that is not moving");
            Check("GUIDE_THE_FASTER_THE_BIKE_THE_EARLIER_THE_RED", deep > 100 && ordered == deep && slowerLater >= 0.9 * deep,
                "the same " + deep + " zones from 40 m before, at the speed the plan has at the start: a bike 15 % under that speed sees the red no earlier than the bike at the plan speed (or none) and a bike 10 % over it no later, in " + ordered + " of them; the slower bike sees it strictly later in " + slowerLater + " (by " + (shiftSlow / Math.Max(1, deep)).ToString("F0") + " m on average), the faster one strictly earlier in " + fasterEarlier + " (by " + (-shiftFast / Math.Max(1, deep)).ToString("F0") + " m on average; the others see it at the nose too)",
                "the red follows the speed of the bike");
        }

        // ---- ridden by the computer rider: the red is near its nose when it brakes and gone when it has braked enough, and it is not there when it is on the throttle
        {
            int brakingSteps = 0, brakingRed = 0, drivingSteps = 0, drivingRed = 0, runs = 0, runsWithRed = 0, runsCleared = 0;
            double worstBraking = 9, worstDriving = 0;
            foreach (int seed in new[] { 4242, 22, 53 })
            {
                var c = Circuits.Generate(seed);
                foreach (int b in new[] { 0, 3 })
                {
                    var steps = RideTheLine(c, b, 11);
                    int bs = 0, br = 0, ds = 0, dr = 0;
                    foreach (var s in steps)
                    {
                        if (s.braking) { bs++; br += s.red70; }
                        if (s.driving) { ds++; dr += s.red20; }
                    }
                    brakingSteps += bs; brakingRed += br; drivingSteps += ds; drivingRed += dr;
                    if (bs > 0) worstBraking = Math.Min(worstBraking, br / (double)bs);
                    if (ds > 0) worstDriving = Math.Max(worstDriving, dr / (double)ds);
                    // the runs of braking of a second or more: red near the nose when it begins, and none within 20 m when it ends
                    int start = -1;
                    for (int k = 0; k <= steps.Count; k++)
                    {
                        bool inRun = k < steps.Count && steps[k].braking;
                        if (inRun && start < 0) start = k;
                        if (!inRun && start >= 0)
                        {
                            if (k - start >= 60)
                            {
                                runs++;
                                if (steps[start + 10].red70 == 1) runsWithRed++;
                                if (steps[k - 1].red20 == 0) runsCleared++;
                            }
                            start = -1;
                        }
                    }
                }
            }
            Check("GUIDE_RIDDEN_BY_THE_COMPUTER_RIDER_THE_RED_IS_NEAR_ITS_NOSE_WHEN_IT_BRAKES_GONE_WHEN_IT_HAS_BRAKED_AND_NOT_THERE_ON_THE_THROTTLE", brakingSteps > 500 && brakingRed >= 0.80 * brakingSteps && drivingRed <= 0.05 * drivingSteps && runs > 5 && runsWithRed >= 0.85 * runs && runsCleared >= 0.7 * runs,
                "second lap alone on 3 circuits with 2 bikes: while it brakes there is red in the 70 m ahead for " + (100.0 * brakingRed / Math.Max(1, brakingSteps)).ToString("F0") + " % of " + brakingSteps + " steps (the worst bike " + (worstBraking * 100).ToString("F0") + " %), while it accelerates there is red in the 20 m ahead for " + (100.0 * drivingRed / Math.Max(1, drivingSteps)).ToString("F1") + " % of " + drivingSteps + " (the worst " + (worstDriving * 100).ToString("F1") + " %); of " + runs + " braking runs of a second or more " + runsWithRed + " have red near the nose a sixth of a second in and " + runsCleared + " end with none in the 20 m ahead",
                "red when it must brake, gone when it has braked enough, none while it accelerates");
        }

        // ---- the stretch the player sees: Tidy folds the specks and keeps the ends, and on real roads at real speeds no piece is too short, no red is shallow and a stretch cut at the end keeps its red
        {
            Func<string, Advice[]> make = s => { var r = new Advice[s.Length]; for (int i = 0; i < s.Length; i++) r[i] = s[i] == 'G' ? Advice.Go : s[i] == 'Y' ? Advice.Lift : Advice.Brake; return r; };
            Func<Advice[], string> show = r => { var sb = new StringBuilder(); foreach (var x in r) sb.Append(x == Advice.Go ? 'G' : x == Advice.Lift ? 'Y' : 'R'); return sb.ToString(); };
            string[,] cases =
            {
                { "GGGGGYRGGGGG", "GGGGGGGGGGGG" },            // a yellow speck and a red speck in the middle: gone
                { "RRGGGGGGGGGG", "RRGGGGGGGGGG" },            // a short piece at the start of the stretch is cut by it: kept
                { "GGGGGGGGGGRR", "GGGGGGGGGGRR" },            // and at the end
                { "GGGGGRRRRRGGGGG", "GGGGGRRRRRGGGGG" },      // a piece of exactly the minimum stays
                { "GGGGGRRGGGGG", "GGGGGGGGGGGG" },            // a red of two samples is a speck
                { "GGGGGRRYYYGGGGG", "GGGGGGGGGGGGGGG" },      // two short pieces one after the other: both go, the shortest first
                { "RRRRRRYYYRRRRRR", "RRRRRRRRRRRRRRR" },      // a short yellow inside a red is red
            };
            bool tidyOk = true; string tidyBad = "";
            for (int t = 0; t < cases.GetLength(0); t++)
            {
                var a1 = make(cases[t, 0]); DrivingLine.Tidy(a1, 5);
                if (show(a1) != cases[t, 1]) { tidyOk = false; tidyBad += " [" + cases[t, 0] + " -> " + show(a1) + ", expected " + cases[t, 1] + "]"; }
            }
            Check("GUIDE_TIDY_FOLDS_THE_SPECKS_IN_THE_MIDDLE_AND_KEEPS_THE_ENDS", tidyOk,
                cases.GetLength(0) + " hand-made stretches: specks of two samples and two short pieces in a row are folded into their longer neighbour, the pieces at the ends and a piece of exactly five samples stay" + tidyBad,
                "no piece shorter than the minimum in the middle, the ends untouched");

            int windows = 0, tooShort = 0, shallowRed = 0, endKept = 0, endRaw = 0; int minRun = 5;
            var speeds = new[] { 5f, 15f, 25f, 35f, 45f, 55f, 65f, 75f };
            var seeds2 = new List<int>(seeds); seeds2.Add(777); seeds2.Add(1234);
            foreach (int seed in seeds2)
            {
                var c = Circuits.Generate(seed);
                foreach (int bk in new[] { 0, 6 })
                {
                    var line = new RacingLine(c, Bikes.Get(bk));
                    var plan = DrivingLine.Classify(line);
                    int n = c.n, behind = 4, count = (int)Math.Round(DrivingLine.AheadMetres / c.spacing);
                    var win = new Advice[behind + count];
                    for (int i0 = 0; i0 < n; i0 += 13)
                    {
                        foreach (float v in speeds)
                        {
                            DrivingLine.Window(line, plan, i0, v, behind, win);
                            windows++;
                            for (int q = 0; q < win.Length;)
                            {
                                int r = q + 1; while (r < win.Length && win[r] == win[q]) r++;
                                bool interior = q > 0 && r < win.Length;
                                if (interior && r - q < minRun) tooShort++;
                                if (interior && win[q] == Advice.Brake)
                                {
                                    float low = float.MaxValue;
                                    for (int k = q; k < r; k++) { int i = ((i0 + k - behind) % n + n) % n; if (line.vplan[i] < low) low = line.vplan[i]; }
                                    if (low > DrivingLine.DeepBelow * v + 0.001f) shallowRed++;
                                }
                                q = r;
                            }
                            // a red that runs to the end of the stretch is kept even when it is not deep yet (what comes after it is not seen)
                            int iLast = ((i0 + win.Length - 1 - behind) % n + n) % n;
                            if (DrivingLine.Advise(line.vplan[iLast], v, plan[iLast]) == Advice.Brake) { endRaw++; if (win[win.Length - 1] == Advice.Brake) endKept++; }
                        }
                    }
                }
            }
            Check("GUIDE_THE_STRETCH_THE_PLAYER_SEES_HAS_NO_SPECKS_AND_NO_SHALLOW_RED", windows > 5000 && tooShort == 0 && shallowRed == 0 && endRaw > 100 && endKept == endRaw,
                windows + " stretches (" + seeds2.Count + " circuits, two bikes, eight speeds from 5 to 75 m/s, every 13th sample): " + tooShort + " pieces shorter than " + minRun + " samples in the middle, " + shallowRed + " red pieces in the middle that never get under " + (DrivingLine.DeepBelow * 100).ToString("F0") + " % of the speed, and of the " + endRaw + " stretches that are red at their far end the red is kept in " + endKept,
                "none, none, kept");
        }

        // ---- the braking sign follows the aids: Completi and Sportivi show the very same sign (it is about the brakes, like the red of the ribbon), Nessuno shows none, and a bike that stands still sees none
        {
            bool same = true, none = true, still = true; int samples = 0, shown = 0;
            foreach (int seed in new[] { 4242, 22, 53 })
            {
                var c = Circuits.Generate(seed);
                foreach (int bk in new[] { 0, 3 })
                {
                    var line = new RacingLine(c, Bikes.Get(bk));
                    var plan = DrivingLine.Classify(line);
                    int behind = 4, count = (int)Math.Round(DrivingLine.AheadMetres / c.spacing);
                    var win = new Advice[behind + count];
                    for (int i0 = 0; i0 < c.n; i0 += 7)
                        foreach (float v in new[] { 0f, 12f, 30f, 55f })
                        {
                            var full = CueAt(line, plan, i0, v, 0, win, behind);
                            var sporty = CueAt(line, plan, i0, v, 1, win, behind);
                            var bare = CueAt(line, plan, i0, v, 2, win, behind);
                            samples++;
                            if (full.kind != sporty.kind || full.metres != sporty.metres || full.fill != sporty.fill || full.target != sporty.target) same = false;
                            if (bare.kind != CueKind.None) none = false;
                            if (v == 0f && full.kind != CueKind.None) still = false;
                            if (full.kind != CueKind.None) shown++;
                        }
                }
            }
            Check("CUE_THE_AIDS_DECIDE_WHETHER_THERE_IS_A_BRAKING_SIGN_AND_A_BIKE_THAT_STANDS_STILL_HAS_NONE", same && none && still && shown > 200,
                samples + " places of 3 circuits and 2 bikes at 4 speeds: Completi and Sportivi show the very same sign (" + (same ? "yes" : "NO") + "), Nessuno never shows one (" + (none ? "yes" : "NO") + "), a bike at 0 km/h never sees one (" + (still ? "yes" : "NO") + "); the sign is up in " + shown + " of them",
                "same for aids 0 and 1, none for 2, none at a standstill");
        }

        // ---- coming up to a braking zone at the speed the plan has at its start: nothing far off, a countdown that only goes down, the order to brake where the red begins, and the speed of the bottom of the zone
        {
            int zones = 0, ordered = 0, counted = 0, onRed = 0, nearRed = 0, worstNear = 0, bottomRight = 0, bottomUnknown = 0, noSign = 0; double nearSum = 0; string badBottom = "";
            foreach (int bk in new[] { 3, 0, 6 })
                foreach (int seed in seeds)
                {
                    var c = Circuits.Generate(seed);
                    var line = new RacingLine(c, Bikes.Get(bk)); var plan = DrivingLine.Classify(line);
                    int n = c.n, behind = 4, count = (int)Math.Round(DrivingLine.AheadMetres / c.spacing);
                    var win = new Advice[behind + count];
                    foreach (var z in DeepZones(line))
                    {
                        zones++;
                        float window = BrakeCues.ApproachWindow(z.v);
                        CueKind last = CueKind.None; bool order = true, down = true, firstApproach = true; float lastM = 0f, lastFill = 0f;
                        for (int k = count; k >= 0; k--)
                        {
                            var cue = CueAt(line, plan, ((z.z0 - k) % n + n) % n, z.v, 0, win, behind);
                            if (k < count && cue.kind == CueKind.Brake && last != CueKind.Brake && !(last == CueKind.Approach && lastM <= DrivingLine.LeadMetres + 0.01f)) order = false;          // the order to brake never comes out of nowhere: a countdown that has run down to within the lead of the red (the red shows 12 m before the plan brakes) comes before it
                            if (cue.kind == CueKind.Approach)
                            {
                                if (firstApproach) { firstApproach = false; if (cue.metres > window + 0.01f) down = false; }
                                else if (last == CueKind.Approach && (cue.metres > lastM + 0.01f || cue.fill < lastFill - 0.0001f)) down = false;
                                lastM = cue.metres; lastFill = cue.fill;
                            }
                            last = cue.kind;
                            if (k != 0) continue;
                            // the bike is on the first sample of the zone
                            if (cue.kind == CueKind.Brake) onRed++;
                            else if (cue.kind == CueKind.Approach) { nearRed++; nearSum += cue.metres; if (cue.metres > worstNear) worstNear = (int)cue.metres; }
                            else noSign++;
                            if (cue.kind == CueKind.None) continue;
                            if (cue.target <= 0f) bottomUnknown++;
                            else if (BrakeCues.TargetKmh(cue.target) == BrakeCues.TargetKmh(z.low)) bottomRight++;
                            else if (badBottom.Length < 150) badBottom += " [" + c.seed + " " + Bikes.Get(bk).name + " zone " + z.z0 + ": " + BrakeCues.TargetKmh(cue.target) + " km/h against " + BrakeCues.TargetKmh(z.low) + "]";
                        }
                        if (order) ordered++;
                        if (down && !firstApproach) counted++;
                    }
                }
            Check("CUE_COMING_UP_TO_A_BRAKING_ZONE_THE_SIGN_COUNTS_DOWN_AND_THEN_SAYS_BRAKE", zones > 100 && ordered == zones && counted == zones && noSign == 0 && onRed >= 0.9 * zones && worstNear <= 40,
                zones + " braking zones (" + seeds.Count + " circuits, three bikes), the bike coming up to each at the speed the plan has at its start, from 300 m before: the order to brake is always preceded by a countdown that has run down to within the lead of the red (" + DrivingLine.LeadMetres.ToString("F0") + " m) in " + ordered + ", it counts down from no further than five seconds of road (never under 60 m) and the metres and the bar only go one way in " + counted + "; on the first sample of the zone it says brake in " + onRed + " and counts down the last " + (nearRed == 0 ? 0 : (int)(nearSum / nearRed)) + " m on average (at most " + worstNear + " m) in " + nearRed + (noSign > 0 ? ", says NOTHING in " + noSign : ""),
                "a countdown, then brake, in every zone; brake at the start of nine zones of ten");
            Check("CUE_THE_SIGN_NAMES_THE_SPEED_OF_THE_BOTTOM_OF_THE_ZONE_IN_STEPS_OF_FIVE", bottomRight >= 0.9 * (onRed + nearRed) && bottomUnknown == 0,
                "on the first sample of each of the " + zones + " zones the speed on the sign is the lowest speed of the plan in the dip, to the nearest 5 km/h, in " + bottomRight + " of " + (onRed + nearRed) + " (" + bottomUnknown + " zones have no bottom to the plan within " + DrivingLine.ScanMetres.ToString("F0") + " m)" + badBottom,
                "the speed the braking is for");
        }

        // ---- the numbers and the words of the sign, from a table made by hand
        {
            Func<float, float, bool, float, DrivingLine.Braking> brk = (ahead, length, open, lowest) => new DrivingLine.Braking { ahead = ahead, length = length, open = open, lowest = lowest, bottom = lowest > 0f };          // a speed of 0 means the plan only keeps falling: no bottom
            var cases = new[]
            {
                new { aids = 0, b = brk(100f, 80f, false, 22.9f), v = 50f, kind = CueKind.Approach, metres = 100f, fill = 0.6f, target = 22.9f, title = "FRENA TRA 100 m", sub = "fino a 80 km/h" },
                new { aids = 1, b = brk(100f, 80f, false, 22.9f), v = 50f, kind = CueKind.Approach, metres = 100f, fill = 0.6f, target = 22.9f, title = "FRENA TRA 100 m", sub = "fino a 80 km/h" },
                new { aids = 2, b = brk(100f, 80f, false, 22.9f), v = 50f, kind = CueKind.None, metres = 0f, fill = 0f, target = 0f, title = "", sub = "" },
                new { aids = 0, b = brk(300f, 80f, false, 22.9f), v = 50f, kind = CueKind.None, metres = 0f, fill = 0f, target = 0f, title = "", sub = "" },
                new { aids = 0, b = brk(0f, 120f, false, 20f), v = 50f, kind = CueKind.Brake, metres = 120f, fill = 0.48f, target = 20f, title = "FRENA", sub = "fino a 70 km/h" },
                new { aids = 0, b = brk(50f, 80f, false, 15f), v = 10f, kind = CueKind.Approach, metres = 50f, fill = 1f / 6f, target = 15f, title = "FRENA TRA 50 m", sub = "fino a 55 km/h" },
                new { aids = 0, b = brk(70f, 80f, false, 15f), v = 10f, kind = CueKind.None, metres = 0f, fill = 0f, target = 0f, title = "", sub = "" },
                new { aids = 0, b = brk(-1f, 0f, false, 0f), v = 50f, kind = CueKind.None, metres = 0f, fill = 0f, target = 0f, title = "", sub = "" },
                new { aids = 0, b = brk(50f, 80f, false, 15f), v = 0.5f, kind = CueKind.None, metres = 0f, fill = 0f, target = 0f, title = "", sub = "" },
                new { aids = 0, b = brk(100f, 200f, true, 22.9f), v = 50f, kind = CueKind.Approach, metres = 100f, fill = 0.6f, target = 22.9f, title = "FRENA TRA 100 m", sub = "fino a 80 km/h" },
                new { aids = 0, b = brk(100f, 200f, true, 0f), v = 50f, kind = CueKind.Approach, metres = 100f, fill = 0.6f, target = 0f, title = "FRENA TRA 100 m", sub = "" },
                new { aids = 0, b = brk(250f, 50f, false, 30f), v = 80f, kind = CueKind.Approach, metres = 250f, fill = 1f / 6f, target = 30f, title = "FRENA TRA 250 m", sub = "fino a 110 km/h" },
                new { aids = 0, b = brk(63f, 40f, false, 23.5f), v = 40f, kind = CueKind.Approach, metres = 63f, fill = 1f - 63f / 200f, target = 23.5f, title = "FRENA TRA 65 m", sub = "fino a 85 km/h" },
                new { aids = 0, b = brk(2f, 40f, false, 23.5f), v = 40f, kind = CueKind.Approach, metres = 2f, fill = 1f - 2f / 200f, target = 23.5f, title = "FRENA TRA 5 m", sub = "fino a 85 km/h" },
                new { aids = 0, b = brk(104f, 40f, false, 23.5f), v = 40f, kind = CueKind.Approach, metres = 104f, fill = 1f - 104f / 200f, target = 23.5f, title = "FRENA TRA 100 m", sub = "fino a 85 km/h" },
                new { aids = 0, b = brk(105f, 40f, false, 23.5f), v = 40f, kind = CueKind.Approach, metres = 105f, fill = 1f - 105f / 200f, target = 23.5f, title = "FRENA TRA 110 m", sub = "fino a 85 km/h" },
                new { aids = 0, b = brk(0f, 400f, true, 20f), v = 50f, kind = CueKind.Brake, metres = 400f, fill = 1f, target = 20f, title = "FRENA", sub = "fino a 70 km/h" },
            };
            bool ok = true; string bad = "";
            for (int t = 0; t < cases.Length; t++)
            {
                var k = cases[t];
                var cue = BrakeCues.Make(k.aids, k.b, k.v);
                bool good = cue.kind == k.kind && Math.Abs(cue.metres - k.metres) < 0.01f && Math.Abs(cue.fill - k.fill) < 0.001f && Math.Abs(cue.target - k.target) < 0.01f && BrakeCueWords.Title(cue) == k.title && BrakeCueWords.Sub(cue) == k.sub;
                if (!good) { ok = false; if (bad.Length < 300) bad += " [case " + t + ": " + cue.kind + " " + cue.metres.ToString("F1") + " m, fill " + cue.fill.ToString("F3") + ", target " + cue.target.ToString("F1") + ", '" + BrakeCueWords.Title(cue) + "' / '" + BrakeCueWords.Sub(cue) + "']"; }
            }
            bool window = BrakeCues.ApproachWindow(0f) == 60f && BrakeCues.ApproachWindow(10f) == 60f && Math.Abs(BrakeCues.ApproachWindow(30f) - 150f) < 0.01f && BrakeCues.ApproachWindow(80f) == DrivingLine.AheadMetres;
            Check("CUE_THE_NUMBERS_AND_THE_WORDS_OF_THE_SIGN_FOLLOW_THE_RULES", ok && window,
                cases.Length + " hand-made cases (aids, the first red, the speed): the countdown is five seconds of road, never under 60 m and never over 300 m (the window is " + BrakeCues.ApproachWindow(10f).ToString("F0") + " m at 10 m/s, " + BrakeCues.ApproachWindow(30f).ToString("F0") + " at 30 and " + BrakeCues.ApproachWindow(80f).ToString("F0") + " at 80), the bar fills as the red comes and empties as it goes, the metres are written to 5 m under 100 and to 10 m over it and the speed to 5 km/h, and a plan that only keeps falling names no speed" + bad,
                "the rules of the sign");
        }

        // ---- ridden by the computer rider (it brakes on the plan, a little before the red reaches it): the sign has a countdown for it while it brakes, no order to brake while it accelerates, and nothing near the nose when it has braked enough
        {
            int brakingSteps = 0, brakingSign = 0, brakingOnRed = 0, drivingSteps = 0, drivingBrake = 0, drivingNear = 0, runs = 0, runsSign = 0, runsGone = 0; double nearSum = 0; int nearCount = 0;
            foreach (int seed in new[] { 4242, 22, 53 })
            {
                var c = Circuits.Generate(seed);
                foreach (int b in new[] { 0, 3 })
                {
                    var steps = RideTheLine(c, b, 11);
                    foreach (var s in steps)
                    {
                        if (s.braking)
                        {
                            brakingSteps++;
                            if (s.sign != CueKind.None) brakingSign++;
                            if (s.sign == CueKind.Brake) brakingOnRed++;
                            if (s.sign == CueKind.Approach && s.signM <= 70f) { nearSum += s.signM; nearCount++; }
                        }
                        if (s.driving)
                        {
                            drivingSteps++;
                            if (s.sign == CueKind.Brake) drivingBrake++;
                            if (s.sign == CueKind.Brake || (s.sign == CueKind.Approach && s.signM <= 20f)) drivingNear++;
                        }
                    }
                    int start = -1;
                    for (int k = 0; k <= steps.Count; k++)
                    {
                        bool inRun = k < steps.Count && steps[k].braking;
                        if (inRun && start < 0) start = k;
                        if (!inRun && start >= 0)
                        {
                            if (k - start >= 60)
                            {
                                runs++;
                                if (steps[start + 10].sign != CueKind.None) runsSign++;
                                var e = steps[k - 1];
                                if (!(e.sign == CueKind.Brake || (e.sign == CueKind.Approach && e.signM <= 20f))) runsGone++;
                            }
                            start = -1;
                        }
                    }
                }
            }
            Check("CUE_RIDDEN_BY_THE_COMPUTER_RIDER_THE_SIGN_COUNTS_DOWN_WHEN_IT_BRAKES_AND_IS_QUIET_WHEN_IT_ACCELERATES", brakingSteps > 500 && brakingSign >= 0.80 * brakingSteps && drivingSteps > 500 && drivingBrake <= 0.05 * drivingSteps && drivingNear <= 0.05 * drivingSteps && runs > 5 && runsSign >= 0.85 * runs && runsGone >= 0.70 * runs,
                "second lap alone on 3 circuits with 2 bikes: while it brakes the sign has something to say in " + (100.0 * brakingSign / Math.Max(1, brakingSteps)).ToString("F0") + " % of " + brakingSteps + " steps (the order itself in " + (100.0 * brakingOnRed / Math.Max(1, brakingSteps)).ToString("F0") + " %: it brakes before the red reaches it, and counts down the last " + (nearCount == 0 ? 0 : (int)(nearSum / nearCount)) + " m on average), while it accelerates it says brake in " + (100.0 * drivingBrake / Math.Max(1, drivingSteps)).ToString("F1") + " % of " + drivingSteps + " and has the red within 20 m in " + (100.0 * drivingNear / Math.Max(1, drivingSteps)).ToString("F1") + " %; of " + runs + " braking runs of a second or more " + runsSign + " have a sign a sixth of a second in and " + runsGone + " end with nothing within 20 m",
                "the sign and the ride agree");
        }
    }
}
